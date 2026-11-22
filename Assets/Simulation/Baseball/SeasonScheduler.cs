using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One generated regular-season game: team ids and its day-of-season.</summary>
public readonly struct GeneratedGame
{
    public readonly int DayOfSeason;
    public readonly int HomeTeamId;
    public readonly int AwayTeamId;

    public GeneratedGame(int dayOfSeason, int homeTeamId, int awayTeamId)
    {
        DayOfSeason = dayOfSeason;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
    }
}

/// <summary>
/// The seeded season scheduler (league_restructure.md §3.1, §16-I9; slice
/// 13-3). <see cref="Generate"/> is pure: teams + a stream in, one regular
/// season out, every balance holding by construction:
///
/// - HS: K6 circle rounds inside each 6-team conference, played twice with the
///   second leg's venues reversed (10 games, 5 home / 5 away).
/// - College: per conference, the three K4 division rounds × 4 with venues
///   alternating (2/2 per division opponent), plus the 8 cross-division
///   rounds of K(4,4,4) × 2, second copy reversed (1/1); 28 games, 14/14, no
///   cross-conference games.
/// - Pro: per conference, K4 division rounds × 8 alternating (4/4), the 12
///   cross-division rounds (a division-pair matching × a Latin shift) × 2
///   reversed (1/1), and 16 interconference rounds (East i vs West (i+s) mod
///   16, East hosting when s &lt; 8; 8/8); 64 games, 32/32.
///
/// The seed only permutes: team order inside each division (HS: conference),
/// the order of each conference's rounds, and (Pro) which 16 of the 64 game
/// days are interconference. The stream is derived from the save's
/// <c>schedule_seed</c>, the season year and the tier, never from a sim's
/// stream, so adding the scheduler moves no other draw (13-3 brief D1).
/// <see cref="EnsureSeasons"/> persists the current and the next season once
/// and never regenerates existing rows.
/// </summary>
public static class SeasonScheduler
{
    /// <summary>Game_State key of the save's schedule seed (written once at world-gen, D1).</summary>
    public const string ScheduleSeedKey = "schedule_seed";

    /// <summary>The seed a world without a <see cref="ScheduleSeedKey"/> row uses (every harness fixture).</summary>
    public const ulong DefaultScheduleSeed = 0x133D_5EED_0000_0001UL;

    /// <summary>The (season, tier) stream: a stable mix of the save's seed, the year and the tier.</summary>
    public static RngState StreamFor(ulong scheduleSeed, int seasonYear, LeagueTier tier) =>
        new(scheduleSeed
            ^ unchecked((ulong)seasonYear * 0x9E3779B97F4A7C15UL)
            ^ unchecked(((ulong)tier + 1) * 0xC2B2AE3D27D4EB4FUL));

    /// <summary>The save's schedule seed, or <see cref="DefaultScheduleSeed"/> when none is stored.</summary>
    public static ulong ResolveSeed(GameStateQueries gameState) =>
        gameState.TryGetInt64(ScheduleSeedKey, out long stored) ? unchecked((ulong)stored) : DefaultScheduleSeed;

    /// <summary>
    /// Generates and persists the tier's <paramref name="seasonYear"/> and
    /// <paramref name="seasonYear"/> + 1 regular seasons where none exist yet,
    /// one transaction per generated season. Idempotent: a season that has
    /// rows is never touched, so a reload never changes a schedule.
    /// </summary>
    public static void EnsureSeasons(
        DatabaseManager db, BaseballQueries baseball, ScheduleQueries schedule, GameStateQueries gameState,
        LeagueTier tier, int seasonYear)
    {
        ulong seed = ResolveSeed(gameState);
        List<TeamRow>? teams = null;
        List<GeneratedGame>? games = null;
        for (int year = seasonYear; year <= seasonYear + 1; year++)
        {
            if (schedule.CountRegularSeasonGames(year, tier) > 0)
            {
                continue;
            }
            teams ??= LoadTeams(baseball, tier);
            games ??= new List<GeneratedGame>(TierSeasonCalendar.MaxGameDays * RosterCapacity.MaxTeamsPerTier / 2);
            RngState rng = StreamFor(seed, year, tier);
            Generate(tier, teams, ref rng, games);
            db.BeginBatch();
            try
            {
                foreach (GeneratedGame game in games)
                {
                    schedule.InsertRegularSeasonGame(year, tier, game.DayOfSeason, game.HomeTeamId, game.AwayTeamId);
                }
                db.CommitBatch();
            }
            catch
            {
                db.RollbackBatch();
                throw;
            }
        }
    }

    private static List<TeamRow> LoadTeams(BaseballQueries baseball, LeagueTier tier)
    {
        var teams = new List<TeamRow>(RosterCapacity.MaxTeamsPerTier);
        baseball.LoadTeamsByTier(tier, teams);
        return teams;
    }

    /// <summary>
    /// One regular season for <paramref name="tier"/>: games in play order
    /// (by day, then conference, then round slot). Throws when the teams
    /// don't have the §2.3 shape the constructions need.
    /// </summary>
    public static void Generate(LeagueTier tier, IReadOnlyList<TeamRow> teams, ref RngState rng, List<GeneratedGame> destination)
    {
        destination.Clear();
        List<List<int>[]> conferences = GroupTeams(tier, teams, ref rng);
        ReadOnlySpan<int> days = TierSeasonCalendar.GameDays(tier);
        switch (tier)
        {
            case LeagueTier.HS:
            {
                List<Round>[] rounds = { HsRounds(conferences[0][0]), HsRounds(conferences[1][0]) };
                FillDays(days, rounds, interconference: null, ref rng, destination);
                break;
            }
            case LeagueTier.College:
            {
                List<Round>[] rounds = { CollegeRounds(conferences[0]), CollegeRounds(conferences[1]) };
                FillDays(days, rounds, interconference: null, ref rng, destination);
                break;
            }
            default:
            {
                List<Round>[] rounds = { ProIntraRounds(conferences[0]), ProIntraRounds(conferences[1]) };
                List<Round> inter = ProInterRounds(Flatten(conferences[0]), Flatten(conferences[1]));
                FillDays(days, rounds, inter, ref rng, destination);
                break;
            }
        }
    }

    /// <summary>One round: every team in its group plays once (team ids, home first).</summary>
    private sealed class Round
    {
        public readonly List<(int Home, int Away)> Games = new();

        public void Add(int home, int away) => Games.Add((home, away));
    }

    // Conferences by conference_id, each a list of divisions (HS: one group of
    // the whole conference), teams by team_id then seed-shuffled per group.
    private static List<List<int>[]> GroupTeams(LeagueTier tier, IReadOnlyList<TeamRow> teams, ref RngState rng)
    {
        var byConference = new SortedDictionary<int, SortedDictionary<int, List<int>>>();
        foreach (TeamRow team in teams)
        {
            if (!byConference.TryGetValue(team.ConferenceId, out SortedDictionary<int, List<int>>? divisions))
            {
                divisions = new SortedDictionary<int, List<int>>();
                byConference.Add(team.ConferenceId, divisions);
            }
            int division = tier == LeagueTier.HS ? 0 : team.DivisionId
                ?? throw new InvalidOperationException($"Team {team.TeamId} ({tier}) has no division (§2.3).");
            if (!divisions.TryGetValue(division, out List<int>? members))
            {
                members = new List<int>();
                divisions.Add(division, members);
            }
            members.Add(team.TeamId);
        }

        (int divisions, int perDivision) shape = tier switch
        {
            LeagueTier.HS => (1, 6),
            LeagueTier.College => (3, 4),
            _ => (4, 4),
        };
        var result = new List<List<int>[]>(2);
        foreach (SortedDictionary<int, List<int>> divisions in byConference.Values)
        {
            if (divisions.Count != shape.divisions)
            {
                throw new InvalidOperationException(
                    $"{tier} conference has {divisions.Count} divisions; the scheduler needs {shape.divisions} (§2.3).");
            }
            var groups = new List<int>[shape.divisions];
            int g = 0;
            foreach (List<int> members in divisions.Values)
            {
                if (members.Count != shape.perDivision)
                {
                    throw new InvalidOperationException(
                        $"{tier} group has {members.Count} teams; the scheduler needs {shape.perDivision} (§2.3).");
                }
                members.Sort();
                Shuffle(members, ref rng);
                groups[g++] = members;
            }
            result.Add(groups);
        }
        if (result.Count != 2)
        {
            throw new InvalidOperationException($"{tier} has {result.Count} conferences; the scheduler needs 2 (§2.3).");
        }
        return result;
    }

    private static List<int> Flatten(List<int>[] groups)
    {
        var all = new List<int>(16);
        foreach (List<int> group in groups)
        {
            all.AddRange(group);
        }
        return all;
    }

    // HS: K6 circle method (team 5 fixed), 5 rounds; the second leg reverses venues.
    private static List<Round> HsRounds(List<int> t)
    {
        var rounds = new List<Round>(10);
        for (int leg = 0; leg < 2; leg++)
        {
            for (int r = 0; r < 5; r++)
            {
                var round = new Round();
                AddLeg(round, t[5], t[r], leg);
                for (int i = 1; i <= 2; i++)
                {
                    AddLeg(round, t[(r + i) % 5], t[(r - i + 5) % 5], leg);
                }
                rounds.Add(round);
            }
        }
        return rounds;
    }

    private static void AddLeg(Round round, int a, int b, int leg)
    {
        if (leg == 0)
        {
            round.Add(a, b);
        }
        else
        {
            round.Add(b, a);
        }
    }

    // The three perfect matchings of K4 (division rounds, and Pro's division-pair matchings).
    private static readonly (int A, int B)[][] K4Matchings =
    {
        new[] { (0, 1), (2, 3) },
        new[] { (0, 2), (1, 3) },
        new[] { (0, 3), (1, 2) },
    };

    // Division K4 rounds × copies; venues flip every copy (copies/2 home games per opponent).
    private static void AddDivisionRounds(List<int>[] divisions, int copies, List<Round> rounds)
    {
        for (int copy = 0; copy < copies; copy++)
        {
            foreach ((int A, int B)[] matching in K4Matchings)
            {
                var round = new Round();
                foreach (List<int> d in divisions)
                {
                    foreach ((int a, int b) in matching)
                    {
                        AddLeg(round, d[a], d[b], copy & 1);
                    }
                }
                rounds.Add(round);
            }
        }
    }

    // College: 12 division rounds + the 8-round K(4,4,4) factorization × 2.
    // Halves X_0 = X[0..1], X_1 = X[2..3]; for (a, h, k) in {0,1}^3 the round
    // pairs A_a–B_h (matching k), A_{1-a}–C_k (matching h) and B_{1-h}–C_{1-k}
    // (matching a), which covers every cross-division edge exactly once.
    private static List<Round> CollegeRounds(List<int>[] div)
    {
        var rounds = new List<Round>(28);
        AddDivisionRounds(div, copies: 4, rounds);
        for (int leg = 0; leg < 2; leg++)
        {
            for (int a = 0; a < 2; a++)
            {
                for (int h = 0; h < 2; h++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        var round = new Round();
                        for (int i = 0; i < 2; i++)
                        {
                            AddLeg(round, Half(div[0], a, i), Half(div[1], h, i ^ k), leg);
                            AddLeg(round, Half(div[0], 1 - a, i), Half(div[2], k, i ^ h), leg);
                            AddLeg(round, Half(div[1], 1 - h, i), Half(div[2], 1 - k, i ^ a), leg);
                        }
                        rounds.Add(round);
                    }
                }
            }
        }
        return rounds;
    }

    private static int Half(List<int> division, int half, int i) => division[2 * half + i];

    // Pro, one conference: 24 division rounds + 12 cross-division rounds × 2.
    private static List<Round> ProIntraRounds(List<int>[] div)
    {
        var rounds = new List<Round>(48);
        AddDivisionRounds(div, copies: 8, rounds);
        for (int leg = 0; leg < 2; leg++)
        {
            foreach ((int A, int B)[] pairing in K4Matchings)
            {
                for (int shift = 0; shift < 4; shift++)
                {
                    var round = new Round();
                    foreach ((int x, int y) in pairing)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            AddLeg(round, div[x][i], div[y][(i + shift) % 4], leg);
                        }
                    }
                    rounds.Add(round);
                }
            }
        }
        return rounds;
    }

    // Pro interconference: East i vs West (i + s) mod 16; East hosts when s < 8.
    private static List<Round> ProInterRounds(List<int> east, List<int> west)
    {
        var rounds = new List<Round>(16);
        for (int s = 0; s < 16; s++)
        {
            var round = new Round();
            for (int i = 0; i < 16; i++)
            {
                AddLeg(round, east[i], west[(i + s) % 16], s < 8 ? 0 : 1);
            }
            rounds.Add(round);
        }
        return rounds;
    }

    // Places the rounds on the tier's game days: each conference's rounds in
    // a seeded order; Pro's 16 interconference days at seeded positions.
    private static void FillDays(
        ReadOnlySpan<int> days, List<Round>[] conferenceRounds, List<Round>? interconference,
        ref RngState rng, List<GeneratedGame> destination)
    {
        foreach (List<Round> rounds in conferenceRounds)
        {
            Shuffle(rounds, ref rng);
        }
        var isInter = new bool[days.Length];
        if (interconference is not null)
        {
            Shuffle(interconference, ref rng);
            var slots = new List<int>(days.Length);
            for (int d = 0; d < days.Length; d++)
            {
                slots.Add(d);
            }
            Shuffle(slots, ref rng);
            for (int k = 0; k < interconference.Count; k++)
            {
                isInter[slots[k]] = true;
            }
        }

        int intra = 0;
        int inter = 0;
        for (int d = 0; d < days.Length; d++)
        {
            if (isInter[d])
            {
                Emit(days[d], interconference![inter++], destination);
                continue;
            }
            foreach (List<Round> rounds in conferenceRounds)
            {
                Emit(days[d], rounds[intra], destination);
            }
            intra++;
        }
        foreach (List<Round> rounds in conferenceRounds)
        {
            if (intra != rounds.Count)
            {
                throw new InvalidOperationException($"Scheduler placed {intra} of {rounds.Count} rounds (construction bug).");
            }
        }
    }

    private static void Emit(int day, Round round, List<GeneratedGame> destination)
    {
        foreach ((int home, int away) in round.Games)
        {
            destination.Add(new GeneratedGame(day, home, away));
        }
    }

    // Fisher–Yates with the scheduler's own stream.
    private static void Shuffle<T>(List<T> items, ref RngState rng)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
