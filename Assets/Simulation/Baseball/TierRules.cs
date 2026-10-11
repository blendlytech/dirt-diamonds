using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Per-tier game rules (league_restructure.md §4 and §6). Pure data, read at
/// Initialize or once per game, never per PA.
/// </summary>
public static class TierRules
{
    /// <summary>§4: HS plays 7 innings, College and Pro 9. Extra innings work as shipped.</summary>
    public static int RegulationInnings(LeagueTier tier) => tier == LeagueTier.HS ? 7 : 9;

    /// <summary>
    /// §6: a starter is fully rested once this many calendar days have passed
    /// since his last start. HS and College play weekly (4 → the ace starts
    /// every game); Pro plays Tue/Thu/Sat (3 → the ace takes two of every
    /// three games, a natural 2-man rotation).
    /// </summary>
    public static int MinRestDays(LeagueTier tier) => tier == LeagueTier.Pro ? 3 : 4;

    /// <summary>
    /// §6 workload risk: stamina points docked from a starter's in-game
    /// capacity (micro-sim fatigue) when he starts on short rest. A Gemini Pro
    /// Math value; 0 until Math sets it, and 0 is bit-identical to no penalty.
    /// The macro sim is complete-game and has no in-game fatigue, so it never
    /// applies this. (The paired OveruseInjuryRisk roll needs the absence
    /// writer and lands with the roster/attrition work in 13-5.)
    /// </summary>
    public const int ShortRestCapacityPenalty = 0;
}
