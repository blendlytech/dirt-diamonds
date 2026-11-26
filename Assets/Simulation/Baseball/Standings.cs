using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// One tier-season's regular-season record table (13-4 brief D1/D2, CS-M §3):
/// per-team wins, losses, runs, and the pairwise meeting and win matrices,
/// built from completed phase-0 games only (finals and forfeits). Exact
/// integer accumulators throughout, so every derived order is independent of
/// row order. Sized once by the tier capacity; a rebuild reuses the arrays.
/// Runs in flush/selection code, never per PA.
/// </summary>
public sealed class SeasonTable
{
    public const int Capacity = RosterCapacity.MaxTeamsPerTier;

    private readonly int[] _teamIds = new int[Capacity];
    private readonly int[] _wins = new int[Capacity];
    private readonly int[] _losses = new int[Capacity];
    private readonly int[] _runsScored = new int[Capacity];
    private readonly int[] _runsAllowed = new int[Capacity];
    // [i * Capacity + j]: games i played against j / games i won against j.
    private readonly int[] _meetings = new int[Capacity * Capacity];
    private readonly int[] _winsVs = new int[Capacity * Capacity];

    public int Count { get; private set; }

    /// <summary>Rebuilds the table for <paramref name="teamIds"/> (any order) from completed regular-season games.</summary>
    public void Build(ReadOnlySpan<int> teamIds, IReadOnlyList<CompletedGame> games)
    {
        if (teamIds.Length > Capacity)
        {
            throw new ArgumentException($"{teamIds.Length} teams exceed the tier capacity {Capacity}.", nameof(teamIds));
        }
        Count = teamIds.Length;
        teamIds.CopyTo(_teamIds);
        Array.Clear(_wins);
        Array.Clear(_losses);
        Array.Clear(_runsScored);
        Array.Clear(_runsAllowed);
        Array.Clear(_meetings);
        Array.Clear(_winsVs);
        for (int g = 0; g < games.Count; g++)
        {
            CompletedGame game = games[g];
            int home = IndexOf(game.HomeTeamId);
            int away = IndexOf(game.AwayTeamId);
            if (home < 0 || away < 0)
            {
                throw new InvalidOperationException(
                    $"Completed game {game.HomeTeamId} v {game.AwayTeamId} names a team outside this table.");
            }
            if (game.HomeScore == game.AwayScore)
            {
                throw new InvalidOperationException($"Completed game {game.HomeTeamId} v {game.AwayTeamId} is tied; baseball has no ties.");
            }
            _runsScored[home] += game.HomeScore;
            _runsAllowed[home] += game.AwayScore;
            _runsScored[away] += game.AwayScore;
            _runsAllowed[away] += game.HomeScore;
            _meetings[home * Capacity + away]++;
            _meetings[away * Capacity + home]++;
            if (game.HomeScore > game.AwayScore)
            {
                _wins[home]++;
                _losses[away]++;
                _winsVs[home * Capacity + away]++;
            }
            else
            {
                _wins[away]++;
                _losses[home]++;
                _winsVs[away * Capacity + home]++;
            }
        }
    }

    /// <summary>The team's index in this table, or -1.</summary>
    public int IndexOf(int teamId)
    {
        for (int i = 0; i < Count; i++)
        {
            if (_teamIds[i] == teamId)
            {
                return i;
            }
        }
        return -1;
    }

    public int TeamId(int index) => _teamIds[index];
    public int Wins(int index) => _wins[index];
    public int Losses(int index) => _losses[index];
    public int Games(int index) => _wins[index] + _losses[index];
    public int RunsScored(int index) => _runsScored[index];
    public int RunsAllowed(int index) => _runsAllowed[index];
    public int RunDifferential(int index) => _runsScored[index] - _runsAllowed[index];

    /// <summary>Completed games between two teams.</summary>
    public int Meetings(int index, int opponentIndex) => _meetings[index * Capacity + opponentIndex];

    /// <summary>Wins of <paramref name="index"/> over <paramref name="opponentIndex"/>.</summary>
    public int WinsVs(int index, int opponentIndex) => _winsVs[index * Capacity + opponentIndex];

    /// <summary>
    /// Exact fraction comparison a = an/ad vs b = bn/bd (positive = a larger),
    /// with a zero denominator read as ½ (CS-M §3 zero-games rule).
    /// </summary>
    internal static int CompareFraction(long an, long ad, long bn, long bd)
    {
        if (ad == 0)
        {
            (an, ad) = (1, 2);
        }
        if (bd == 0)
        {
            (bn, bd) = (1, 2);
        }
        return (an * bd).CompareTo(bn * ad);
    }
}

/// <summary>
/// Standings tiebreakers (13-4 brief D2-A, Math record §10.1 Q8): order by
/// win% (exact cross-product; 0–0 reads ½). A tied group of any size breaks
/// by (1) head-to-head win% inside the group only, a mini-league; (2) run
/// differential, higher first; (3) runs allowed, fewer first; (4) the seeded
/// coin <see cref="PostseasonStreams.StandingsCoinKey"/> ascending, then
/// team id only on a 2⁻⁶⁴ key collision. Whenever a step splits a group,
/// every sub-group of two or more restarts from step 1. Each split strictly
/// shrinks a group and step 4 is a total order, so it terminates; it reads
/// only persisted results, the schedule seed and team ids (a pure function,
/// reload-invariant, and it never draws a sim RNG).
/// </summary>
public static class Standings
{
    /// <summary>
    /// Orders <paramref name="scopeTeamIds"/> best first. The table must hold
    /// every scoped team; H2H reads only games between scoped teams.
    /// </summary>
    public static List<int> Order(
        SeasonTable table, IReadOnlyList<int> scopeTeamIds, ulong scheduleSeed, int seasonYear, LeagueTier tier)
    {
        var indexes = new List<int>(scopeTeamIds.Count);
        foreach (int teamId in scopeTeamIds)
        {
            int index = table.IndexOf(teamId);
            if (index < 0)
            {
                throw new ArgumentException($"Team {teamId} is not in the season table.", nameof(scopeTeamIds));
            }
            indexes.Add(index);
        }
        var output = new List<int>(indexes.Count);
        foreach (List<int> group in Partition(indexes, (a, b) => SeasonTable.CompareFraction(
                     table.Wins(a), table.Games(a), table.Wins(b), table.Games(b))))
        {
            Resolve(table, group, scheduleSeed, seasonYear, tier, output);
        }
        for (int i = 0; i < output.Count; i++)
        {
            output[i] = table.TeamId(output[i]);
        }
        return output;
    }

    private static void Resolve(
        SeasonTable table, List<int> group, ulong scheduleSeed, int seasonYear, LeagueTier tier, List<int> output)
    {
        if (group.Count == 1)
        {
            output.Add(group[0]);
            return;
        }

        // 1. Head-to-head inside the group only.
        var h2hWins = new Dictionary<int, long>(group.Count);
        var h2hGames = new Dictionary<int, long>(group.Count);
        foreach (int t in group)
        {
            long wins = 0;
            long games = 0;
            foreach (int u in group)
            {
                if (u != t)
                {
                    wins += table.WinsVs(t, u);
                    games += table.Meetings(t, u);
                }
            }
            h2hWins[t] = wins;
            h2hGames[t] = games;
        }
        if (TrySplit(table, group, (a, b) => SeasonTable.CompareFraction(h2hWins[a], h2hGames[a], h2hWins[b], h2hGames[b]),
                scheduleSeed, seasonYear, tier, output))
        {
            return;
        }
        // 2. Run differential, higher first.
        if (TrySplit(table, group, (a, b) => table.RunDifferential(a).CompareTo(table.RunDifferential(b)),
                scheduleSeed, seasonYear, tier, output))
        {
            return;
        }
        // 3. Runs allowed, fewer first.
        if (TrySplit(table, group, (a, b) => table.RunsAllowed(b).CompareTo(table.RunsAllowed(a)),
                scheduleSeed, seasonYear, tier, output))
        {
            return;
        }
        // 4. The seeded coin, then team id only on a key collision.
        var ordered = new List<int>(group);
        ordered.Sort((a, b) =>
        {
            int byCoin = PostseasonStreams.StandingsCoinKey(scheduleSeed, seasonYear, tier, table.TeamId(a))
                .CompareTo(PostseasonStreams.StandingsCoinKey(scheduleSeed, seasonYear, tier, table.TeamId(b)));
            return byCoin != 0 ? byCoin : table.TeamId(a).CompareTo(table.TeamId(b));
        });
        output.AddRange(ordered);
    }

    /// <summary>Splits by a "better is larger" comparison; when it splits, every class resolves from step 1.</summary>
    private static bool TrySplit(
        SeasonTable table, List<int> group, Comparison<int> better,
        ulong scheduleSeed, int seasonYear, LeagueTier tier, List<int> output)
    {
        List<List<int>> classes = Partition(group, better);
        if (classes.Count == 1)
        {
            return false;
        }
        foreach (List<int> subGroup in classes)
        {
            Resolve(table, subGroup, scheduleSeed, seasonYear, tier, output);
        }
        return true;
    }

    /// <summary>Equivalence classes of <paramref name="items"/> under <paramref name="better"/>, best class first (stable).</summary>
    private static List<List<int>> Partition(List<int> items, Comparison<int> better)
    {
        var sorted = new List<int>(items);
        // Stable: List.Sort isn't, so break comparator ties by the original position.
        var position = new Dictionary<int, int>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            position[items[i]] = i;
        }
        sorted.Sort((a, b) =>
        {
            int c = better(b, a);
            return c != 0 ? c : position[a].CompareTo(position[b]);
        });
        var classes = new List<List<int>>();
        foreach (int item in sorted)
        {
            if (classes.Count == 0 || better(classes[^1][0], item) != 0)
            {
                classes.Add(new List<int>());
            }
            classes[^1].Add(item);
        }
        return classes;
    }
}

/// <summary>
/// D1-A: <c>Team_Seasons</c> recomputed from <c>Schedule_Games</c> (phase 0,
/// final or forfeit) as an idempotent upsert — never incremental deltas, so
/// a reload or a replayed flush can't double-count. W/L/RS/RA are regular
/// season only (postseason truth is Playoff_Series + phase-1/2 rows); the
/// College national rank is recomputed in the same batch (NULL for HS/Pro).
/// The seed column is selection's and is never touched here. Joins the
/// caller's open batch; reads see that batch's uncommitted results.
/// </summary>
public sealed class TeamSeasonsWriter
{
    private readonly PlayoffQueries _queries;
    private readonly List<CompletedGame> _games = new(TierSeasonCalendar.MaxGameDays * RosterCapacity.MaxTeamsPerTier / 2);
    private readonly int[] _rankByIndex = new int[SeasonTable.Capacity];

    public TeamSeasonsWriter(PlayoffQueries queries)
    {
        _queries = queries;
    }

    /// <summary>The table of the last <see cref="Recompute"/> (valid until the next call).</summary>
    public SeasonTable Table { get; } = new();

    /// <summary>The National Ranking of the last College <see cref="Recompute"/> (rank order).</summary>
    public List<RankingCalculator.Entry> LastRanking { get; private set; } = new();

    /// <summary>Recomputes and upserts every team-season of the tier; returns the games read.</summary>
    public int Recompute(int seasonYear, LeagueTier tier, ReadOnlySpan<int> teamIds, ulong scheduleSeed)
    {
        _queries.LoadCompletedRegularSeason(seasonYear, tier, _games);
        Table.Build(teamIds, _games);
        bool ranked = tier == LeagueTier.College;
        if (ranked)
        {
            LastRanking = RankingCalculator.Rank(Table, scheduleSeed, seasonYear, RankingWeights.Default);
            foreach (RankingCalculator.Entry entry in LastRanking)
            {
                _rankByIndex[Table.IndexOf(entry.TeamId)] = entry.Rank;
            }
        }
        for (int i = 0; i < Table.Count; i++)
        {
            _queries.UpsertTeamSeason(
                Table.TeamId(i), seasonYear, Table.Wins(i), Table.Losses(i), Table.RunsScored(i), Table.RunsAllowed(i),
                ranked ? _rankByIndex[i] : null);
        }
        return _games.Count;
    }
}

/// <summary>The National Ranking's score weights (CS-M §3: .50 W, .30 SOS, .20 RD_norm).</summary>
public readonly record struct RankingWeights(double Win, double Sos, double RunDifferential)
{
    public static readonly RankingWeights Default = new(0.50, 0.30, 0.20);
}

/// <summary>
/// The College National Ranking (CS-M §3, 13-4 brief §4.1 and Amendment 2):
/// <c>RankScore = .50·W + .30·SOS + .20·RD_norm</c> where W = wins/games;
/// SOS = Σ opponents' wins / Σ opponents' games, once per completed meeting
/// (opponents' records include their games against the team); RD_norm =
/// (clamp(RD/G, −5, 5) + 5) / 10; every term reads ½ at zero games. Exact
/// ties break by W (exact cross-product), RD/G (exact cross-product), the
/// seeded <see cref="PostseasonStreams.RankingCoinKey"/> ascending, then
/// team id only as the 2⁻⁶⁴ collision fallback, so no conference wins every
/// cross-conference tie (Math record §10.1 Q1). Regular season only; a
/// pure function of saved results. Allocation: bounded, flush-time only.
/// </summary>
public static class RankingCalculator
{
    /// <summary>One team's ranking inputs and score.</summary>
    public readonly record struct Entry(
        int TeamId, int Rank, double Score, int Wins, int Games, long OppWins, long OppGames, int RunDifferential)
    {
        public double WinPct => Games > 0 ? (double)Wins / Games : 0.5;
        public double Sos => OppGames > 0 ? (double)OppWins / OppGames : 0.5;
    }

    /// <summary>Ranks every team in <paramref name="table"/>; entries come back in rank order (1 first).</summary>
    public static List<Entry> Rank(SeasonTable table, ulong scheduleSeed, int seasonYear, RankingWeights weights)
    {
        int n = table.Count;
        var entries = new List<Entry>(n);
        for (int i = 0; i < n; i++)
        {
            long oppWins = 0;
            long oppGames = 0;
            for (int j = 0; j < n; j++)
            {
                int m = table.Meetings(i, j);
                if (m != 0)
                {
                    oppWins += (long)m * table.Wins(j);
                    oppGames += (long)m * table.Games(j);
                }
            }
            int games = table.Games(i);
            double w = games > 0 ? (double)table.Wins(i) / games : 0.5;
            double sos = oppGames > 0 ? (double)oppWins / oppGames : 0.5;
            double rdNorm = games > 0
                ? (Math.Clamp((double)table.RunDifferential(i) / games, -5.0, 5.0) + 5.0) / 10.0
                : 0.5;
            double score = weights.Win * w + weights.Sos * sos + weights.RunDifferential * rdNorm;
            entries.Add(new Entry(table.TeamId(i), 0, score, table.Wins(i), games, oppWins, oppGames,
                games > 0 ? table.RunDifferential(i) : 0));
        }

        entries.Sort((a, b) => Compare(a, b, scheduleSeed, seasonYear));
        for (int r = 0; r < entries.Count; r++)
        {
            entries[r] = entries[r] with { Rank = r + 1 };
        }
        return entries;
    }

    /// <summary>Amendment 2's total order (negative = a ranks ahead of b).</summary>
    internal static int Compare(in Entry a, in Entry b, ulong scheduleSeed, int seasonYear) =>
        Compare(in a, in b,
            PostseasonStreams.RankingCoinKey(scheduleSeed, seasonYear, LeagueTier.College, a.TeamId),
            PostseasonStreams.RankingCoinKey(scheduleSeed, seasonYear, LeagueTier.College, b.TeamId));

    /// <summary>The order with the coin keys given (the harness forces a key collision to prove the id fallback).</summary>
    internal static int Compare(in Entry a, in Entry b, ulong coinA, ulong coinB)
    {
        int c = b.Score.CompareTo(a.Score);
        if (c != 0)
        {
            return c;
        }
        c = SeasonTable.CompareFraction(b.Wins, b.Games, a.Wins, a.Games);
        if (c != 0)
        {
            return c;
        }
        // RD/G descending: (RD_a · max(1, G_b)) vs (RD_b · max(1, G_a)); 0 games ⇒ RD 0.
        c = ((long)b.RunDifferential * Math.Max(1, a.Games)).CompareTo((long)a.RunDifferential * Math.Max(1, b.Games));
        if (c != 0)
        {
            return c;
        }
        c = coinA.CompareTo(coinB);
        return c != 0 ? c : a.TeamId.CompareTo(b.TeamId);
    }
}
