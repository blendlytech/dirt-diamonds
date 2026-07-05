namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// How purchased gear quality bends effective ratings (Phase 8e,
/// docs/design/equipment_quality.md §2/§5). Follows the PED/fatigue/rivalry
/// precedent exactly: adjust the effective ratings fed into the UNCHANGED
/// <see cref="AtBatResolver"/>, never its calibration tables.
///
/// The boost touches batter Power + Contact (never Discipline — plate
/// judgment is not equipment) and pitcher Stuff + Control (never Stamina —
/// endurance belongs to the fatigue model). Quality 0 is all-zero BY
/// CONTRACT (the MLB-tier-vector precedent): applying boost 0 is the
/// identity, so a no-gear world's PA path is bit-identical to pre-8e.
///
/// Order of operations per PA (doc §5): baked array value (tier) → gear
/// boost UP → rust dock DOWN → rivalry → the resolver's own PED multiplier.
/// Gear applies first, to the tier-baked value — you bring your gear to the
/// park; rust erodes the whole package. The boost values are first-pass
/// calibration data like the tier/rivalry deltas — tuning is a data edit
/// here, never a logic edit, and every change re-runs run_monte_carlo_batch.
/// </summary>
public static class EquipmentEffects
{
    /// <summary>Highest purchasable quality (doc §2's ladder: 1 = Quality, 2 = Premium, 3 = Custom Pro).</summary>
    public const int MaxQuality = 3;

    // The §2 boost table, indexed by quality. Quality 0 (standard issue — no
    // Player_Equipment row) is zero by contract, never edit. Max +6 ≈ +0.12
    // deviations on the resolver's 50-points-per-σ scale — a real season
    // edge, nothing like PED's 1.5×.
    private static readonly byte[] BoostByQuality = { 0, 2, 4, 6 };

    /// <summary>The flat rating boost for one gear quality (throws on an out-of-range quality — fail loud).</summary>
    public static byte BoostFor(int quality) => BoostByQuality[quality];

    /// <summary>Batter-side gear boost (0 = the input unchanged); Discipline and the PED flag ride along.</summary>
    public static BatterRatings Batter(in BatterRatings b, byte boost) =>
        boost == 0 ? b : new BatterRatings(
            TierEffects.Shift(b.Power, boost), TierEffects.Shift(b.Contact, boost),
            b.Discipline, b.PedActive);

    /// <summary>Pitcher-side gear boost (0 = the input unchanged); Stamina rides along.</summary>
    public static PitcherRatings Pitcher(in PitcherRatings p, byte boost) =>
        boost == 0 ? p : new PitcherRatings(
            TierEffects.Shift(p.Stuff, boost), TierEffects.Shift(p.Control, boost), p.Stamina);
}
