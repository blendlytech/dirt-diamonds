namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Roster capacities (league_restructure.md §5.2, GEMINI.md §2): fixed buffer
/// sizes, never per-tier roster shapes. Both sims size their slot arrays to
/// these once at Initialize (so <c>stackalloc</c> and array shapes stay
/// allocation-free) and carry per-team counts; every loop runs to the team's
/// count, never to the capacity. A team over a capacity is a generation bug
/// and fails loudly at Initialize.
/// </summary>
public static class RosterCapacity
{
    /// <summary>Always 9 batters/defenders in the game.</summary>
    public const int Lineup = 9;

    /// <summary>Position players beyond the nine (Pro 6, College up to 8, HS up to 5).</summary>
    public const int MaxBench = 8;

    public const int MaxStarters = 6;

    public const int MaxRelievers = 6;

    /// <summary>Lineup plus bench: the most position players a team may carry.</summary>
    public const int MaxPositionPlayers = Lineup + MaxBench;
}
