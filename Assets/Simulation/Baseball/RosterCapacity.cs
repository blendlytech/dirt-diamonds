namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Fixed buffer capacities for both sims (league_restructure.md §5.2, slice
/// 13-2). They replace the shipped 9 + 5 + 3 roster SHAPE: buffers are sized
/// once by these capacities so <c>stackalloc</c> and array sizing stay
/// allocation-free, and every loop runs to a team's actual count, never to
/// the capacity. A team over any capacity fails loudly at Initialize.
/// </summary>
public static class RosterCapacity
{
    /// <summary>Always nine batters/defenders (universal DH, arcade §8.1).</summary>
    public const int Lineup = 9;

    public const int MaxBench = 8;
    public const int MaxStarters = 6;
    public const int MaxRelievers = 6;

    /// <summary>Lineup + bench: the most position players a team may carry (College's ≤ 17).</summary>
    public const int MaxPositionPlayers = Lineup + MaxBench;

    public const int MaxRoster = MaxPositionPlayers + MaxStarters + MaxRelievers;

    /// <summary>The largest league (Pro, 32 teams, §2.2); schedule scratch is sized by it.</summary>
    public const int MaxTeamsPerTier = 32;
}
