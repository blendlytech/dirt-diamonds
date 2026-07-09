using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Economy.Phone;

/// <summary>Why a phone-economy action was refused (None = it succeeded).</summary>
public enum PhoneActionFailure : byte
{
    None = 0,

    /// <summary>No Phone_State row (the pre-v11 phone — nothing to meter or top up) or an Unlimited plan that never needs minutes.</summary>
    NotMetered,

    UnknownPlayer,
    InsufficientMinutes,
    InsufficientFunds,

    /// <summary>Already at the Flagship tier — no further hardware upgrade exists.</summary>
    TopTierOwned,
}

/// <summary>
/// The §4.2 smartphone minutes economy (docs/design/high_school_person_layer.md):
/// hardware tier gates features (the UI's concern), plan + minutes meter usage
/// (this service's concern). Minutes gate ONLY player-initiated actions —
/// the §4.3 hard invariant is structural: nothing in the Narrative pipeline
/// references this class, so a pending gritty-event thread physically cannot
/// be minute-gated (GrittyEventsHarness proves it against a zero-minute row).
///
/// Bypass ladder, checked in order: no Phone_State row = the pre-v11 phone
/// (no accounting at all, per the schema comment) → Unlimited plan (§4.2
/// "bypasses all minute accounting") → Wi-Fi (§4.2 "any phone action taken
/// while on Wi-Fi is free") → the metered balance.
///
/// Purchases follow the EquipmentService/ItemService discipline: validate
/// against live DB state, write funds + phone state in one batch, commit,
/// then publish the funds impulse for the Life sim's in-memory mirror.
/// Hardware upgrades sell at the CARRIER (Bank tab), not the Marketplace —
/// a deliberate deviation from §4.1's letter, because tier 1 locks the
/// Marketplace and would soft-lock the burner kid out of ever upgrading.
/// </summary>
public sealed class PhoneService
{
    // §4.2 plan values (Phone_State.plan CHECK 0-2).
    public const int PrepaidPlan = 0;
    public const int BasicPlan = 1;
    public const int UnlimitedPlan = 2;

    // §4.2 hardware tiers (Phone_State.tier CHECK 1-3).
    public const int BurnerTier = 1;
    public const int MidTier = 2;
    public const int FlagshipTier = 3;

    // §4.2 first-pass minute economy, verbatim from the doc's table (§11).
    public const int TextMinuteCost = 2;
    public const int CallMinuteCost = 6;
    public const int MarketplaceBrowseMinuteCost = 3;
    /// <summary>§4.2 long-distance relationship weekly upkeep — staged for HS-5's hometown anchor, no consumer yet.</summary>
    public const int LongDistanceWeeklyMinutes = 20;

    /// <summary>§4.2 carrier bundle: $10 → 100 minutes.</summary>
    public const double BundlePriceDollars = 10.0;
    public const int BundleMinutes = 100;

    /// <summary>§4.2 Basic-plan weekly allotment, applied on the family tick (never reduces a topped-up balance).</summary>
    public const int BasicWeeklyRefillMinutes = 50;

    // Hardware upgrade prices — first-pass invented constants (nothing in the
    // doc pins them), tunable data like the minute costs above.
    public const double MidTierPhonePrice = 150.0;
    public const double FlagshipPhonePrice = 600.0;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly EventBus _bus;

    public PhoneService(DatabaseManager db, PlayerQueries players, PersonQueries persons, EventBus bus)
    {
        _db = db;
        _players = players;
        _persons = persons;
        _bus = bus;
    }

    /// <summary>Price of the hardware upgrade TO <paramref name="targetTier"/> (the EquipmentService.PriceForQuality shape).</summary>
    public static double PriceForTier(int targetTier) => targetTier switch
    {
        MidTier => MidTierPhonePrice,
        FlagshipTier => FlagshipPhonePrice,
        _ => throw new ArgumentOutOfRangeException(nameof(targetTier), targetTier, "No phone is sold at that tier."),
    };

    /// <summary>
    /// §4.2 Wi-Fi context: home Wi-Fi iff the family has it. Until HS-4's
    /// schedule brings locations, the phone is only ever browsed from home,
    /// so this IS the caller's onWifi argument; free-Wi-Fi venues (school,
    /// library) arrive with the time-block system.
    /// </summary>
    public bool IsOnHomeWifi(string playerId) =>
        _persons.TryGetFamily(playerId, out FamilyBackgroundRow family) && family.HomeWifi;

    /// <summary>
    /// Meters a player-initiated phone action costing <paramref name="minutes"/>.
    /// Free (true, nothing written) through every §4.2 bypass; on the metered
    /// path the balance is decremented in place. False + a typed reason means
    /// the action must not happen — the caller gates its UI on the same
    /// arithmetic, but a stale click must never overdraw.
    /// </summary>
    public bool TrySpendMinutes(string playerId, int minutes, bool onWifi, out PhoneActionFailure failure)
    {
        if (!_persons.TryGetPhone(playerId, out PhoneStateRow phone) || phone.Plan == UnlimitedPlan || onWifi)
        {
            failure = PhoneActionFailure.None;
            return true;
        }
        if (phone.MinutesRemaining < minutes)
        {
            failure = PhoneActionFailure.InsufficientMinutes;
            return false;
        }
        phone.MinutesRemaining -= minutes;
        _persons.UpsertPhone(in phone);
        failure = PhoneActionFailure.None;
        return true;
    }

    /// <summary>
    /// Buys the §4.2 carrier bundle ($10 → 100 minutes). Available on both
    /// metered plans — the doc's table files bundles under Prepaid, but a
    /// Basic-plan player who burns the weekly allotment mid-week would
    /// otherwise have no recourse until the next family tick (disclosed call).
    /// </summary>
    public bool TryBuyBundle(string playerId, out PhoneActionFailure failure)
    {
        if (!_persons.TryGetPhone(playerId, out PhoneStateRow phone) || phone.Plan == UnlimitedPlan)
        {
            failure = PhoneActionFailure.NotMetered;
            return false;
        }
        if (!_players.TryGetById(playerId, out PlayerRow row))
        {
            failure = PhoneActionFailure.UnknownPlayer;
            return false;
        }
        if (row.Funds < BundlePriceDollars)
        {
            failure = PhoneActionFailure.InsufficientFunds;
            return false;
        }

        _db.BeginBatch();
        try
        {
            _players.AdjustFunds(playerId, -BundlePriceDollars);
            phone.MinutesRemaining += BundleMinutes;
            _persons.UpsertPhone(in phone);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        _bus.Publish(new FundsImpulseEvent(playerId, -BundlePriceDollars));
        failure = PhoneActionFailure.None;
        return true;
    }

    /// <summary>
    /// Buys the next hardware tier (§4.1: rewrites Phone_State.tier; sold at
    /// the carrier, see the class note). One rung per purchase — the ladder
    /// is the progression, exactly like gear quality.
    /// </summary>
    public bool TryUpgradePhone(string playerId, long day, out PhoneActionFailure failure)
    {
        if (!_persons.TryGetPhone(playerId, out PhoneStateRow phone))
        {
            // The pre-v11 phone already behaves as fully unlocked.
            failure = PhoneActionFailure.NotMetered;
            return false;
        }
        if (phone.Tier >= FlagshipTier)
        {
            failure = PhoneActionFailure.TopTierOwned;
            return false;
        }
        if (!_players.TryGetById(playerId, out PlayerRow row))
        {
            failure = PhoneActionFailure.UnknownPlayer;
            return false;
        }
        double price = PriceForTier(phone.Tier + 1);
        if (row.Funds < price)
        {
            failure = PhoneActionFailure.InsufficientFunds;
            return false;
        }

        _db.BeginBatch();
        try
        {
            _players.AdjustFunds(playerId, -price);
            phone.Tier += 1;
            phone.PurchasedDay = (int)Math.Max(0, day);
            _persons.UpsertPhone(in phone);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        _bus.Publish(new FundsImpulseEvent(playerId, -price));
        failure = PhoneActionFailure.None;
        return true;
    }

    /// <summary>
    /// The §4.2 Basic-plan weekly refill, called by the family tick inside its
    /// batch: tops the balance up TO the allotment, never down (a mid-week
    /// bundle top-up above 50 survives the tick). Returns true when minutes
    /// were actually written.
    /// </summary>
    public bool ApplyWeeklyRefill(string playerId)
    {
        if (!_persons.TryGetPhone(playerId, out PhoneStateRow phone)
            || phone.Plan != BasicPlan
            || phone.MinutesRemaining >= BasicWeeklyRefillMinutes)
        {
            return false;
        }
        phone.MinutesRemaining = BasicWeeklyRefillMinutes;
        _persons.UpsertPhone(in phone);
        return true;
    }
}
