using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Items;
using DirtAndDiamonds.Economy.Phone;

namespace DirtAndDiamonds.Economy.Family;

/// <summary>
/// The weekly family tick (docs/design/high_school_person_layer.md §3/§3.2/§4.2)
/// for the avatar's household: pays the wealth-tier allowance, refills a
/// Basic plan's minute allotment, runs the §3.2 parental auto-purchase, and
/// bills the §4.2 long-distance-relationship upkeep. One batch per tick (the
/// database rules' calendar-tick discipline), funds impulse published only
/// after the commit.
///
/// Parental support (allowance + auto-purchase) runs only while the avatar is
/// still on a HIGH SCHOOL tier team — a disclosed reading of the arc's scope:
/// mom does not wire a college sophomore $60/wk, and an eternal allowance
/// would drift the long-run funds trajectories the 8a economy was calibrated
/// around. The Basic-plan refill is the PLAN's own mechanic (the family tick
/// merely names its cadence, §4.2), so it keeps running after graduation —
/// otherwise a Basic-plan alum's phone would drain to a dead 0 forever.
///
/// §3.2's "phone upgrade" parental purchase is satisfied by construction and
/// deliberately not re-implemented here: creation seeds the wealth ladder's
/// phone (BackstoryGenerator) and succession seeds the inherited one, so no
/// reachable state has a phone below the family's ladder rung.
/// </summary>
public sealed class FamilyService
{
    /// <summary>Weekly, deliberately the same cadence (and day) as LifeSimManager.CostOfLivingCadenceDays — allowance lands the day the bill hits.</summary>
    public const int FamilyTickCadenceDays = 7;

    /// <summary>§3.2: only tier-3/4 ("Comfortable"/"Wealthy") parents auto-purchase; poorer families save up and self-buy.</summary>
    public const int ParentalAutobuyMinWealthTier = 3;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly BaseballQueries _baseball;
    private readonly ItemCatalog _catalog;
    private readonly PhoneService _phone;
    private readonly EventBus _bus;

    // Reused across ticks — one small list, never a hot loop, but the weekly
    // tick has no reason to allocate either.
    private readonly List<PlayerItemRow> _ownedScratch = new();
    private readonly List<EntityFlagRow> _flagsScratch = new();

    public FamilyService(
        DatabaseManager db, PlayerQueries players, PersonQueries persons,
        BaseballQueries baseball, ItemCatalog catalog, PhoneService phone, EventBus bus)
    {
        _db = db;
        _players = players;
        _persons = persons;
        _baseball = baseball;
        _catalog = catalog;
        _phone = phone;
        _bus = bus;
    }

    /// <summary>
    /// Runs the family tick for <paramref name="avatarId"/> when
    /// <paramref name="day"/> is a tick day. Safe to call every day — the
    /// cadence gate lives here so the harness drives it exactly like the
    /// GameManager subscriber does. No Family_Background row (a pre-HS-2
    /// save) is a clean no-op.
    /// </summary>
    public void ProcessDay(string avatarId, long day)
    {
        if (day % FamilyTickCadenceDays != 0)
        {
            return;
        }
        if (!_persons.TryGetFamily(avatarId, out FamilyBackgroundRow family))
        {
            return;
        }

        bool parentalSupport = IsHighSchooler(avatarId);
        double allowancePaid = 0.0;
        string? giftedItemId = null;

        _db.BeginBatch();
        try
        {
            _phone.ApplyWeeklyRefill(avatarId);
            if (IsCommittedLongDistance(avatarId))
            {
                _phone.TrySpendMinutes(
                    avatarId, PhoneService.LongDistanceWeeklyMinutes, _phone.IsOnHomeWifi(avatarId), out _);
            }
            if (parentalSupport)
            {
                if (family.AllowanceWeekly > 0.0)
                {
                    _players.AdjustFunds(avatarId, family.AllowanceWeekly);
                    allowancePaid = family.AllowanceWeekly;
                }
                if (family.WealthTier >= ParentalAutobuyMinWealthTier)
                {
                    giftedItemId = RunParentalAutobuy(avatarId, family.WealthTier, day);
                }
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        if (allowancePaid > 0.0)
        {
            // Post-commit impulse so the Life sim's in-memory funds mirror
            // catches up — the ItemService/EquipmentService ordering, kept.
            _bus.Publish(new FundsImpulseEvent(avatarId, allowancePaid));
        }
        if (giftedItemId is not null)
        {
            // HS-4: a gifted car must re-project the §5.3 transport refund
            // (and invalidate any ownership cache) exactly like a purchase.
            _bus.Publish(new PlayerItemAcquiredEvent(avatarId, giftedItemId));
        }
    }

    /// <summary>
    /// §3.2: buys (as a gift — household money, the avatar's funds are never
    /// touched) the highest-value catalog item the family tier qualifies for,
    /// the avatar lacks, and the avatar isn't already covered for: an owned
    /// same-category item at or above the candidate's price counts as "owning
    /// something at that level", so tier-4 parents never gift a bike to a kid
    /// with a car. One item per tick — the weekly drip, not a shopping spree.
    /// Returns the gifted item id (for the caller's post-commit publish), or
    /// null when nothing qualified.
    /// </summary>
    private string? RunParentalAutobuy(string avatarId, int wealthTier, long day)
    {
        _persons.LoadItemsFor(avatarId, _ownedScratch);

        ItemDefinition? pick = null;
        foreach (ItemDefinition candidate in _catalog.Entries)
        {
            if (!candidate.AutobuysAt(wealthTier) || IsOwnedOrCovered(candidate))
            {
                continue;
            }
            // Ties keep the first authoring-order entry, like the listing.
            if (pick is null || candidate.Price > pick.Price)
            {
                pick = candidate;
            }
        }
        if (pick is null)
        {
            return null;
        }

        _persons.AddItem(new PlayerItemRow
        {
            PlayerId = avatarId,
            ItemId = pick.Id,
            Category = pick.Category,
            AcquiredDay = (int)day,
        });
        return pick.Id;
    }

    private bool IsOwnedOrCovered(ItemDefinition candidate)
    {
        for (int i = 0; i < _ownedScratch.Count; i++)
        {
            PlayerItemRow owned = _ownedScratch[i];
            if (owned.ItemId == candidate.Id)
            {
                return true;
            }
            if (owned.Category == candidate.Category
                && _catalog.Require(owned.ItemId).Price >= candidate.Price)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True while the avatar plays for a HIGH SCHOOL tier team — the parental-support window.</summary>
    private bool IsHighSchooler(string avatarId) =>
        _players.TryGetById(avatarId, out PlayerRow row)
        && row.TeamId.HasValue
        && _baseball.TryGetTeamTier(row.TeamId.Value, out LeagueTier tier)
        && tier == LeagueTier.HS;

    /// <summary>
    /// True only for the `hs_hometown_anchor` "commit_long_distance" branch:
    /// both `long_distance` AND `hs_dating` still active. The `grow_apart`
    /// branch also sets `long_distance` (it doubles as "this arc is resolved,
    /// don't refire the anchor event"), but it clears `hs_dating` in the same
    /// consequence list, so an ex never gets billed for upkeep on a
    /// relationship that already ended.
    /// </summary>
    private bool IsCommittedLongDistance(string avatarId)
    {
        _players.LoadActiveFlags(avatarId, _flagsScratch);
        bool longDistance = false;
        bool stillDating = false;
        foreach (EntityFlagRow flag in _flagsScratch)
        {
            if (string.Equals(flag.FlagName, "long_distance", StringComparison.Ordinal))
            {
                longDistance = true;
            }
            else if (string.Equals(flag.FlagName, "hs_dating", StringComparison.Ordinal))
            {
                stillDating = true;
            }
        }
        return longDistance && stillDating;
    }
}
