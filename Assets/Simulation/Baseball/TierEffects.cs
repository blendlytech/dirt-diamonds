using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One ladder tier's league-environment shift (docs/design/tier_league_environments.md
/// §2): constant integer rating-point deltas applied to every player's
/// EFFECTIVE ratings at roster-load time. Stamina is deliberately absent —
/// the fatigue model is micro-sim-only and separately calibrated (doc §6).
/// </summary>
public readonly struct TierRatingDeltas
{
    public readonly sbyte BatPower;
    public readonly sbyte BatContact;
    public readonly sbyte BatDiscipline;
    public readonly sbyte PitStuff;
    public readonly sbyte PitControl;
    public readonly sbyte Defense;

    public TierRatingDeltas(sbyte batPower, sbyte batContact, sbyte batDiscipline,
        sbyte pitStuff, sbyte pitControl, sbyte defense)
    {
        BatPower = batPower;
        BatContact = batContact;
        BatDiscipline = batDiscipline;
        PitStuff = pitStuff;
        PitControl = pitControl;
        Defense = defense;
    }
}

/// <summary>
/// Per-tier league-environment modifiers (Phase 9a), following the exact
/// PED/fatigue/rivalry precedent: effective ratings into the UNCHANGED
/// <see cref="AtBatResolver"/>, never a calibration-table edit. Unlike those
/// per-PA modifiers this one is baked into the sims' rating arrays once at
/// Initialize — a tier is a property of the whole league, so the per-PA hot
/// path pays nothing.
///
/// The MLB vector is all-zero BY CONTRACT: <see cref="Shift"/> of a 0–100
/// rating by 0 is the identity, so an MLB (or pre-v7) world's rating arrays
/// are bit-identical to the pre-tier code and the M1 calibration cannot move.
/// Delta values are the design doc's §2 table — tuning is a data edit here,
/// never a logic edit, and every change re-runs run_monte_carlo_batch.
/// </summary>
public static class TierEffects
{
    // The §2 table: batter knobs held at zero in every tier (ratings are
    // tier-relative — an average hitter is a 50 everywhere); the environment
    // is carried entirely by the run-prevention triple, degraded −4 per rung
    // descending from MLB. See the doc's §1 for why this is the one monotone
    // lever that lifts AVG/OBP/SLG/BB%/R/G together while easing K%.
    private static readonly TierRatingDeltas[] DeltasByTier =
    {
        //                   bPow bCon bDis pStf pCtl dFld
        new TierRatingDeltas(0, 0, 0, -20, -20, -20), // HS
        new TierRatingDeltas(0, 0, 0, -16, -16, -16), // College
        new TierRatingDeltas(0, 0, 0, -12, -12, -12), // MinorA
        new TierRatingDeltas(0, 0, 0, -8, -8, -8),    // MinorAA
        new TierRatingDeltas(0, 0, 0, -4, -4, -4),    // MinorAAA
        new TierRatingDeltas(0, 0, 0, 0, 0, 0),       // MLB — all-zero by contract, never edit
    };

    /// <summary>The §2 delta vector for one tier.</summary>
    public static TierRatingDeltas For(LeagueTier tier) => DeltasByTier[(int)tier];

    /// <summary>A 0–100 rating shifted by a tier delta, clamped to the rating bounds.</summary>
    public static byte Shift(int rating, int delta) => (byte)Math.Clamp(rating + delta, 0, 100);
}
