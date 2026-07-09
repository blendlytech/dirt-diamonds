using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Economy.Items;

/// <summary>Why an item purchase attempt was refused (None = it succeeded).</summary>
public enum ItemPurchaseFailure : byte
{
    None = 0,
    UnknownItem,
    UnknownPlayer,

    /// <summary>Player_Items already has this (player, item) pair — one-time ownership, no re-buys.</summary>
    AlreadyOwned,

    InsufficientFunds,
}

/// <summary>
/// Layer 2 purchase orchestration for the item catalog
/// (docs/design/high_school_person_layer.md §5) — the exact
/// <see cref="Equipment.EquipmentService"/> discipline: validate against the
/// current DB state, write funds + ownership in this service's own batch,
/// commit, and only then publish the funds impulse so the Life sim's
/// in-memory mirror catches up. Passive stat buffs (§5.2) and the transport
/// hours refund (§5.3) are computed at read (<see cref="ItemEffects"/>) and
/// never touch this write path.
///
/// Scope note: this is the Marketplace buy button only. Phone minute
/// metering (§4.2) lives in PhoneService; the §3.2 parental auto-purchase
/// tick lives in FamilyService.
///
/// §3.1: buying your own FIRST transport pays a one-time person-stat reward
/// (+work_ethic, +discipline, +maturity — the spec's "responsibility"),
/// written inside the purchase batch. Gifted players never earn it: a
/// creation transport gift means transport is already owned, so the
/// first-transport condition can never be true for them. One-time by
/// construction — after the rewarded purchase, transport is owned.
/// </summary>
public sealed class ItemService
{
    /// <summary>§3.1 responsibility reward per stat — first-pass invented magnitude (the doc pins the stats, not the size), tunable.</summary>
    public const int SelfBuyTransportReward = 5;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly ItemCatalog _catalog;
    private readonly EventBus _bus;

    // Reused across Owns/TryPurchase calls — this runs off a button press,
    // never a hot loop, but there is no reason to allocate a fresh list per
    // click when one scratch buffer does the job.
    private readonly List<PlayerItemRow> _ownershipScratch = new();

    public ItemService(DatabaseManager db, PlayerQueries players, PersonQueries persons, ItemCatalog catalog, EventBus bus)
    {
        _db = db;
        _players = players;
        _persons = persons;
        _catalog = catalog;
        _bus = bus;
    }

    /// <summary>
    /// True when <paramref name="playerId"/> already owns <paramref name="itemId"/>.
    /// A live DB read (Player_Items has no in-memory mirror, unlike gear
    /// quality) — call it for a purchase decision, not from a per-frame loop.
    /// </summary>
    public bool Owns(string playerId, string itemId)
    {
        _persons.LoadItemsFor(playerId, _ownershipScratch);
        for (int i = 0; i < _ownershipScratch.Count; i++)
        {
            if (_ownershipScratch[i].ItemId == itemId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Attempts to buy <paramref name="itemId"/> for <paramref name="playerId"/>
    /// on <paramref name="day"/>. Every refusal is a clean no-op with a typed
    /// reason — the UI disables buttons on the same predicate, but a stale
    /// click must never corrupt state.
    /// </summary>
    public bool TryPurchase(string playerId, string itemId, long day, out ItemPurchaseFailure failure)
    {
        if (!_catalog.TryGet(itemId, out ItemDefinition definition))
        {
            failure = ItemPurchaseFailure.UnknownItem;
            return false;
        }
        if (!_players.TryGetById(playerId, out PlayerRow row))
        {
            failure = ItemPurchaseFailure.UnknownPlayer;
            return false;
        }
        _persons.LoadItemsFor(playerId, _ownershipScratch);
        bool ownsAnyTransport = false;
        for (int i = 0; i < _ownershipScratch.Count; i++)
        {
            if (_ownershipScratch[i].ItemId == itemId)
            {
                failure = ItemPurchaseFailure.AlreadyOwned;
                return false;
            }
            ownsAnyTransport |= _ownershipScratch[i].Category == ItemCategory.Transport;
        }
        if (row.Funds < definition.Price)
        {
            failure = ItemPurchaseFailure.InsufficientFunds;
            return false;
        }

        _db.BeginBatch();
        try
        {
            _players.AdjustFunds(playerId, -definition.Price);
            _persons.AddItem(new PlayerItemRow
            {
                PlayerId = playerId,
                ItemId = itemId,
                Category = definition.Category,
                AcquiredDay = (int)day,
            });
            if (definition.Category == ItemCategory.Transport && !ownsAnyTransport)
            {
                ApplySelfBuyTransportReward(playerId);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Bus impulse follows the committed batch — the EquipmentService/
        // HustleService ordering, kept exactly.
        _bus.Publish(new FundsImpulseEvent(playerId, -definition.Price));
        failure = ItemPurchaseFailure.None;
        return true;
    }

    /// <summary>§3.1, inside the open purchase batch: the poverty-compensating one-time responsibility bump. A missing person row (a scratch save without the v11 boot backfill) starts from the schema's neutral defaults.</summary>
    private void ApplySelfBuyTransportReward(string playerId)
    {
        if (!_persons.TryGet(playerId, out PersonRow person))
        {
            person = PersonRow.Neutral(playerId);
        }
        person.WorkEthic = Math.Min(100, person.WorkEthic + SelfBuyTransportReward);
        person.Discipline = Math.Min(100, person.Discipline + SelfBuyTransportReward);
        person.Maturity = Math.Min(100, person.Maturity + SelfBuyTransportReward);
        _persons.Upsert(in person);
    }
}
