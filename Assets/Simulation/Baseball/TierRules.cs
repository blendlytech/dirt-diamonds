using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Per-tier game and pitching rules (league_restructure.md §4/§6, slice
/// 13-2). Every value here is data: a change is an edit to this table, gated
/// by run_monte_carlo_batch, never a logic change in either sim.
/// </summary>
public static class TierRules
{
    /// <summary>§4: HS 7, College 9, Pro 9. Extra innings while tied, as shipped.</summary>
    public static int RegulationInnings(LeagueTier tier) => tier == LeagueTier.HS ? 7 : 9;

    /// <summary>
    /// §6: a starter is fully rested once the calendar-date difference from his
    /// last start reaches this (Wed → Sat = 3). HS 4; College 3 (owner,
    /// 2026-10-18); Pro 3.
    /// </summary>
    public static int MinRestDays(LeagueTier tier) => tier switch
    {
        LeagueTier.HS => 4,
        LeagueTier.College => 3,
        _ => 3,
    };

    /// <summary>
    /// §6 workload knob, a Math data value (GPT 6 Astra Pro, CS-M §2.1): the
    /// fraction a short-rest start docks the starter's in-game stamina
    /// capacity. Ships NEUTRAL at 0 (13-2 brief D5) until R1-MATH fixes the
    /// fatigue contract; at 0 a short-rest start is bit-identical to a rested one.
    /// </summary>
    public const double ShortRestCapacityPenalty = 0.0;

    /// <summary>
    /// §6 workload knob, a Math data value (CS-M §2.1): the added independent
    /// injury probability per short-rest start. Ships NEUTRAL at 0 (D5), which
    /// also means no extra random draw is ever taken.
    /// </summary>
    public const double OveruseInjuryRisk = 0.0;

    /// <summary>The capacity multiplier a start gets: exactly 1 for a rested start, and at the neutral knob.</summary>
    public static double StartCapacityFactor(bool shortRest) =>
        shortRest ? 1.0 - ShortRestCapacityPenalty : 1.0;
}
