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

        // §3.1 reward mirror bookkeeping: the ACTUAL clamped movement each
        // stat took (0 when already at the 100 ceiling), published as
        // person-stat impulses post-commit so the Life sim's hydrated copy
        // (the GPA drift's discipline input, HS-4) stays in step.
        int workEthicApplied = 0, disciplineApplied = 0, maturityApplied = 0;
        bool rewardApplied = false;

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
                ApplySelfBuyTransportReward(playerId, out workEthicApplied, out disciplineApplied, out maturityApplied);
                rewardApplied = true;
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Bus impulses follow the committed batch — the EquipmentService/
        // HustleService ordering, kept exactly. The acquired event is the
        // HS-4 §5.3 transport-refund re-projection seam (and any ownership
        // cache's invalidation signal).
        _bus.Publish(new FundsImpulseEvent(playerId, -definition.Price));
        _bus.Publish(new PlayerItemAcquiredEvent(playerId, itemId));
        if (rewardApplied)
        {
            PublishRewardImpulse(playerId, PersonStatOrdinalWorkEthic, workEthicApplied);
            PublishRewardImpulse(playerId, PersonStatOrdinalDiscipline, disciplineApplied);
            PublishRewardImpulse(playerId, PersonStatOrdinalMaturity, maturityApplied);
        }
        failure = ItemPurchaseFailure.None;
        return true;
    }

    // PersonStatId ordinals (Simulation.Life is not referenced from Economy —
    // the CoreEvents primitives rule; SchemaValidator pins the numbering).
    private const int PersonStatOrdinalMaturity = 1;
    private const int PersonStatOrdinalDiscipline = 10;
    private const int PersonStatOrdinalWorkEthic = 11;

    private void PublishRewardImpulse(string playerId, int statOrdinal, int applied)
    {
        if (applied != 0)
        {
            _bus.Publish(new PersonStatImpulseEvent(playerId, statOrdinal, applied));
        }
    }

    /// <summary>§3.1, inside the open purchase batch: the poverty-compensating one-time responsibility bump. A missing person row (a scratch save without the v11 boot backfill) starts from the schema's neutral defaults. The out params report the clamped movement each stat actually took, for the post-commit mirror impulses.</summary>
    private void ApplySelfBuyTransportReward(
        string playerId, out int workEthicApplied, out int disciplineApplied, out int maturityApplied)
    {
        if (!_persons.TryGet(playerId, out PersonRow person))
        {
            person = PersonRow.Neutral(playerId);
        }
        workEthicApplied = Math.Min(100, person.WorkEthic + SelfBuyTransportReward) - person.WorkEthic;
        disciplineApplied = Math.Min(100, person.Discipline + SelfBuyTransportReward) - person.Discipline;
        maturityApplied = Math.Min(100, person.Maturity + SelfBuyTransportReward) - person.Maturity;
        person.WorkEthic += workEthicApplied;
        person.Discipline += disciplineApplied;
        person.Maturity += maturityApplied;
        _persons.Upsert(in person);
    }
}
