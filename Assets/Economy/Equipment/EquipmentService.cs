using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Economy.Equipment;

/// <summary>Why a purchase attempt was refused (None = it succeeded).</summary>
public enum EquipmentPurchaseFailure : byte
{
    None = 0,
    InvalidQuality,
    UnknownPlayer,

    /// <summary>Requested quality is not strictly above the currently owned tier (upgrade-only, no re-buys).</summary>
    NotAnUpgrade,

    InsufficientFunds,
}

/// <summary>One player's view of the gear shop — funds plus the owned tier (0 = standard issue).</summary>
public readonly struct EquipmentShopState
{
    public readonly double Funds;
    public readonly int OwnedQuality;

    public EquipmentShopState(double funds, int ownedQuality)
    {
        Funds = funds;
        OwnedQuality = ownedQuality;
    }
}

/// <summary>
/// Layer 2 purchase orchestration (Phase 8e, docs/design/equipment_quality.md
/// §6) — the HustleService application discipline: validate, then DB writes in
/// this service's own batch (funds via <see cref="PlayerQueries.AdjustFunds"/>,
/// the atomic floor-clamped writer — never UpdateFunds), commit, and only then
/// the bus impulses, so a subscriber reacting to one always observes the DB
/// state the purchase just produced. The published
/// <see cref="PlayerEquipmentChangedEvent"/> is what moves the Baseball sims'
/// EquipmentLedger; the <see cref="FundsImpulseEvent"/> keeps the Life sim's
/// in-memory funds mirror identical.
///
/// Prices are economy constants and live here; the rating boosts live in
/// <see cref="EquipmentEffects"/> (Baseball). Both are first-pass tunable data
/// behind run_monte_carlo_batch.
/// </summary>
public sealed class EquipmentService
{
    // The §2 price ladder, indexed by quality (quality 0 is never purchasable).
    // Full sticker on every rung, no trade-in credit: laddering 1→2→3 costs
    // $10,750 vs. $7,500 for saving straight to 3 — a real decision.
    private static readonly double[] PriceByQuality = { 0.0, 750.0, 2500.0, 7500.0 };

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly EventBus _bus;

    public EquipmentService(DatabaseManager db, PlayerQueries players, EventBus bus)
    {
        _db = db;
        _players = players;
        _bus = bus;
    }

    /// <summary>Sticker price for one quality tier (throws on an out-of-range quality — fail loud).</summary>
    public static double PriceForQuality(int quality) => PriceByQuality[quality];

    /// <summary>Snapshots the shop for <paramref name="playerId"/> — the avatar in practice, but generic over any subject id.</summary>
    public EquipmentShopState GetShopState(string playerId)
    {
        if (!_players.TryGetById(playerId, out PlayerRow row))
        {
            throw new InvalidOperationException($"'{playerId}' has no Players row — cannot shop for it.");
        }
        int owned = _players.TryGetEquipment(playerId, out PlayerEquipmentRow equipment) ? equipment.Quality : 0;
        return new EquipmentShopState(row.Funds, owned);
    }

    /// <summary>
    /// Attempts to buy <paramref name="quality"/> for <paramref name="playerId"/>
    /// on <paramref name="day"/>. Every refusal is a clean no-op with a typed
    /// reason — the UI disables buttons on the same predicate, but a stale
    /// click must never corrupt state.
    /// </summary>
    public bool TryPurchase(string playerId, int quality, long day, out EquipmentPurchaseFailure failure)
    {
        if (quality < 1 || quality > EquipmentEffects.MaxQuality)
        {
            failure = EquipmentPurchaseFailure.InvalidQuality;
            return false;
        }
        if (!_players.TryGetById(playerId, out PlayerRow row))
        {
            failure = EquipmentPurchaseFailure.UnknownPlayer;
            return false;
        }
        int owned = _players.TryGetEquipment(playerId, out PlayerEquipmentRow equipment) ? equipment.Quality : 0;
        if (quality <= owned)
        {
            failure = EquipmentPurchaseFailure.NotAnUpgrade;
            return false;
        }
        double price = PriceByQuality[quality];
        if (row.Funds < price)
        {
            failure = EquipmentPurchaseFailure.InsufficientFunds;
            return false;
        }

        _db.BeginBatch();
        try
        {
            _players.AdjustFunds(playerId, -price);
            _players.SetEquipment(playerId, quality, day);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        // Bus impulses follow the committed batch — the HustleService/
        // EventConsequenceApplier ordering, kept exactly.
        _bus.Publish(new FundsImpulseEvent(playerId, -price));
        _bus.Publish(new PlayerEquipmentChangedEvent(playerId, (byte)quality));
        failure = EquipmentPurchaseFailure.None;
        return true;
    }
}
