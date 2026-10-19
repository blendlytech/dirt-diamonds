namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One scheduled game: team INDEXES (0..teamCount-1 within a tier, ordered by team_id), not team ids.</summary>
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
/// Circle-method round-robin over <c>teamCount</c> teams (any even count;
/// slice 13-2 generalised the shipped 8-team version, brief D1-A) with team
/// index (teamCount-1) fixed; home/away alternates by cycle+slot. Every team
/// plays every regular-season day, one round per day, cycling through the
/// interim 154-day window. With 8 teams the pairings, and their emit order,
/// are exactly the shipped ones. Slice 13-3 replaces this with persisted
/// per-tier schedules (league_restructure.md §3).
/// </summary>
public static class LeagueSchedule
{
    /// <summary>
    /// Fills <paramref name="destination"/> (length ≥ teamCount / 2) with the
    /// day's games in macro play order. Caller guards the regular season
    /// (1..<see cref="LeagueSimulator.RegularSeasonDays"/>).
    /// </summary>
    public static void GetDayPairings(int teamCount, int dayOfSeason, Span<SchedulePairing> destination)
    {
        int roundsPerCycle = teamCount - 1;
        int pairingsPerDay = teamCount / 2;
        int cycle = (dayOfSeason - 1) / roundsPerCycle;
        int round = (dayOfSeason - 1) % roundsPerCycle;

        destination[0] = OrientPair(teamCount - 1, round, cycle, 0);
        for (int i = 1; i < pairingsPerDay; i++)
        {
            int a = (round + i) % roundsPerCycle;
            int b = (round - i + roundsPerCycle) % roundsPerCycle;
            destination[i] = OrientPair(a, b, cycle, i);
        }
    }

    /// <summary>
    /// The pairing involving <paramref name="teamIndex"/> on the given day.
    /// Always found inside the regular season (every team plays every day);
    /// false only for offseason days.
    /// </summary>
    public static bool TryGetPairingFor(int teamCount, int dayOfSeason, int teamIndex, out SchedulePairing pairing)
    {
        if (dayOfSeason < 1 || dayOfSeason > LeagueSimulator.RegularSeasonDays)
        {
            pairing = default;
            return false;
        }

        int pairingsPerDay = teamCount / 2;
        Span<SchedulePairing> pairings = stackalloc SchedulePairing[RosterCapacity.MaxTeamsPerTier / 2];
        GetDayPairings(teamCount, dayOfSeason, pairings[..pairingsPerDay]);
        for (int i = 0; i < pairingsPerDay; i++)
        {
            if (pairings[i].HomeTeam == teamIndex || pairings[i].AwayTeam == teamIndex)
            {
                pairing = pairings[i];
                return true;
            }
        }
        pairing = default;
        return false;
    }

    private static SchedulePairing OrientPair(int teamA, int teamB, int cycle, int slot) =>
        ((cycle + slot) & 1) == 0 ? new SchedulePairing(teamA, teamB) : new SchedulePairing(teamB, teamA);
}
