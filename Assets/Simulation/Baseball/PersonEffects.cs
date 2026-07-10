using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One player's person→performance deltas (HS-6, high_school_person_layer.md
/// §6.2): confidence drives bat_power / pit_stuff, happiness drives
/// bat_contact / pit_control, teamwork contributes to team defense. Each
/// component is a <see cref="PersonEffects.PointsFor"/> result, so
/// <c>default</c> (all zero) is exactly the neutral-50s person — the identity.
/// </summary>
public readonly struct PersonRatingDeltas
{
    /// <summary>Points from `confidence` → bat_power (batter) / pit_stuff (pitcher).</summary>
    public readonly sbyte Confidence;

    /// <summary>Points from `happiness` → bat_contact (batter) / pit_control (pitcher).</summary>
    public readonly sbyte Happiness;

    /// <summary>Points from `teamwork` → this player's team-defense contribution (lineup only).</summary>
    public readonly sbyte Teamwork;

    public PersonRatingDeltas(sbyte confidence, sbyte happiness, sbyte teamwork)
    {
        Confidence = confidence;
        Happiness = happiness;
        Teamwork = teamwork;
    }
}

/// <summary>
/// HS-6: the ONLY path from the person layer into the sim
/// (high_school_person_layer.md §6) — the exact <see cref="TierEffects"/> /
/// <see cref="RivalryEffects"/> / <see cref="EquipmentEffects"/> precedent:
/// constant, zero-centered, capped effective-rating deltas fed into the
/// UNCHANGED <see cref="AtBatResolver"/>, baked into the sims' rating arrays
/// at Initialize/roster-load alongside the tier deltas ("season-stable" —
/// every existing re-Initialize beat, promotion/development/succession,
/// refreshes them). The per-PA hot path pays nothing.
///
/// Zero-at-50 BY CONTRACT: <see cref="PointsFor"/> of a neutral 50 is 0, and
/// <see cref="Bake"/> with a 0 person delta collapses to the exact pre-HS-6
/// <see cref="TierEffects.Shift"/> call — so a world with no Player_Person
/// rows (every harness guard world), an unwired Persons source, or all-neutral
/// rows produces bit-identical rating arrays and the MLB bit-identity guard
/// stays byte-exact. Caps are ±2 first-pass (tunable to ±3 per the doc's §6.2
/// band — ≈±0.04σ on the resolver's 50-points-per-σ scale, deliberately
/// smaller than equipment's +6): who you are nudges the margins, it does not
/// swing a career. Tuning is a data edit here, never a logic edit, and every
/// change re-runs run_monte_carlo_batch.
///
/// bat_discipline / pit_stamina are untouched (the equipment-doc precedent:
/// plate judgment belongs to the discipline rating, endurance to the fatigue
/// model); the person-stat `discipline` → command lever is reserved and
/// deferred past this first band. Replacement-level call-ups carry no person
/// delta — the same rule rivalry/gear already follow.
/// </summary>
public static class PersonEffects
{
    /// <summary>±cap on the `confidence` → power/stuff lever (§6.2, tunable to 3).</summary>
    public const int ConfidenceCap = 2;

    /// <summary>±cap on the `happiness` → contact/control lever (§6.2, tunable to 3).</summary>
    public const int HappinessCap = 2;

    /// <summary>±cap on the `teamwork` → team-defense lever (§6.2, tunable to 3).</summary>
    public const int TeamworkCap = 2;

    /// <summary>
    /// §6.1: the effective-rating points a 0–100 person stat is worth —
    /// linear and symmetric (0 → −cap, 50 → 0, 100 → +cap), rounded
    /// away-from-zero (the HeirGenetics rounding), clamped to ±cap.
    /// </summary>
    public static int PointsFor(int stat, int cap) =>
        Math.Clamp(
            (int)Math.Round(cap * (stat - 50) / 50.0, MidpointRounding.AwayFromZero),
            -cap, cap);

    /// <summary>The §6.2 delta vector for one person row.</summary>
    public static PersonRatingDeltas For(in PersonRow person) => new(
        (sbyte)PointsFor(person.Confidence, ConfidenceCap),
        (sbyte)PointsFor(person.Happiness, HappinessCap),
        (sbyte)PointsFor(person.Teamwork, TeamworkCap));

    /// <summary>
    /// §6.3 order of operations, pinned in ONE place both sims call:
    /// base rating → tier delta → person delta, each step clamped to [0,100]
    /// via <see cref="TierEffects.Shift"/> (reuse, don't duplicate the clamp).
    /// Order is observable only at the clamps (e.g. base 1, tier −20,
    /// person +2 bakes to 2, not 0) and is harness-pinned so it can never
    /// silently flip. A 0 person delta is the exact pre-HS-6 tier bake.
    /// </summary>
    public static byte Bake(int baseRating, int tierDelta, int personDelta) =>
        TierEffects.Shift(TierEffects.Shift(baseRating, tierDelta), personDelta);

    /// <summary>
    /// Team defense with the §6.2 teamwork lever: the sims' existing
    /// mean-then-tier-shift formula, then shifted by the rounded mean of the
    /// lineup's individual teamwork points (each a <see cref="PointsFor"/>
    /// result, so the team shift is itself bounded by ±<see cref="TeamworkCap"/>).
    /// A zero points sum reproduces the pre-HS-6 defense byte bit-exactly.
    /// Season-stable roster-load bake, never a per-PA move; a shadowed
    /// replacement call-up contributes nothing.
    /// </summary>
    public static byte BakeTeamDefense(int fieldingSum, int lineupSize, int tierDefenseDelta, int teamworkPointsSum)
    {
        byte tiered = TierEffects.Shift((fieldingSum + lineupSize / 2) / lineupSize, tierDefenseDelta);
        int teamworkDelta = (int)Math.Round(
            teamworkPointsSum / (double)lineupSize, MidpointRounding.AwayFromZero);
        return TierEffects.Shift(tiered, teamworkDelta);
    }
}
