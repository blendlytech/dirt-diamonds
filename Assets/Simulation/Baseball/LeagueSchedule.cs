namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One scheduled game: team INDEXES (0..TeamCount-1, ordered by team_id), not team ids.</summary>
public readonly struct SchedulePairing
{
    public readonly int HomeTeam;
    public readonly int AwayTeam;

    public SchedulePairing(int homeTeam, int awayTeam)
    {
        HomeTeam = homeTeam;
        AwayTeam = awayTeam;
    }
}

/// <summary>
/// The league schedule as a pure function of the season day — extracted from
/// <see cref="LeagueSimulator"/> so the macro loop and the Phase 5 career
/// driver derive the SAME pairings from the same math (single source of
/// truth; the driver must know which game the avatar's team plays today in
/// order to claim it from the macro sim).
///
/// Circle-method round-robin with team index (TeamCount-1) fixed; home/away
/// alternates by cycle+slot so pairings balance over the 22 cycles. Every
/// team plays every regular-season day. GetDayPairings emits slots in the
/// exact order the macro sim historically played them, so consuming the
/// pairings preserves the macro rng draw order bit-for-bit.
/// </summary>
public static class LeagueSchedule
{
    public const int PairingsPerDay = LeagueSimulator.TeamCount / 2;

    private const int RoundsPerCycle = LeagueSimulator.TeamCount - 1;

    /// <summary>
    /// Fills <paramref name="destination"/> (length ≥ <see cref="PairingsPerDay"/>)
    /// with the day's games in macro play order. Caller guards the regular
    /// season (1..<see cref="LeagueSimulator.RegularSeasonDays"/>).
    /// </summary>
    public static void GetDayPairings(int dayOfSeason, Span<SchedulePairing> destination)
    {
        int cycle = (dayOfSeason - 1) / RoundsPerCycle;
        int round = (dayOfSeason - 1) % RoundsPerCycle;

        destination[0] = OrientPair(LeagueSimulator.TeamCount - 1, round, cycle, 0);
        for (int i = 1; i < PairingsPerDay; i++)
        {
            int a = (round + i) % RoundsPerCycle;
            int b = (round - i + RoundsPerCycle) % RoundsPerCycle;
            destination[i] = OrientPair(a, b, cycle, i);
        }
    }

    /// <summary>
    /// The pairing involving <paramref name="teamIndex"/> on the given day.
    /// Always found inside the regular season (every team plays every day);
    /// false only for offseason days.
    /// </summary>
    public static bool TryGetPairingFor(int dayOfSeason, int teamIndex, out SchedulePairing pairing)
    {
        if (dayOfSeason < 1 || dayOfSeason > LeagueSimulator.RegularSeasonDays)
        {
            pairing = default;
            return false;
        }

        Span<SchedulePairing> pairings = stackalloc SchedulePairing[PairingsPerDay];
        GetDayPairings(dayOfSeason, pairings);
        foreach (SchedulePairing candidate in pairings)
        {
            if (candidate.HomeTeam == teamIndex || candidate.AwayTeam == teamIndex)
            {
                pairing = candidate;
                return true;
            }
        }
        pairing = default;
        return false;
    }

    private static SchedulePairing OrientPair(int teamA, int teamB, int cycle, int slot) =>
        ((cycle + slot) & 1) == 0 ? new SchedulePairing(teamA, teamB) : new SchedulePairing(teamB, teamA);
}
