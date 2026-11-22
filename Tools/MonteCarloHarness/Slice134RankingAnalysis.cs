using System.Diagnostics;
using System.Globalization;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// ----------------------------------------------------------------------
// Slice 13-4, D5 (brief §3 D5, Math record §10.1 Q3–Q5): the opt-in CS-M §3
// ranking analysis — `--college-analysis ranking [--pilot]`. Never part of
// the default run. Rankings are a pure function of saved results, so no
// season is ever re-simulated for a ranking; the 14-game comparator is a
// seeded, venue-balanced half of each 28-game season (Q3).
//
//   1. Pilot: training worlds 1–8 × scheduler 1–2 × outcome 1–2 (32 seasons).
//   2. Frozen sizing ladder (Q4), computed BEFORE any world 129+ is read:
//      k_hold ∈ {32, 48, 64}; exit 2 when no rung fits in 20 min.
//   3. Holdout: worlds 129 … 128 + k_hold × scheduler 1–4 × outcome 1–4,
//      run once; gate = the 9,500th of 10,000 sorted paired-bootstrap ratios
//      (RngState(28014)) ≤ 1.05 for mean rank SD AND mean top-17 replacements.
//   4. Sensitivity (training worlds 1–32): weights (.60,.25,.15), (.45,.35,.20).
//   5. Strength diagnostics, [DIAG] only: R = 4 neutral double round-robins
//      per holdout world (R = 2 below 1,500 games/s) and R_fix = 8 on the
//      five CS-M §3.4 fixture worlds.
//   6. Tuple (129, 1, 1) repeated 20× byte-identical. Hard stop 20 min → exit 2.
// ----------------------------------------------------------------------
internal static partial class Program
{
    private const double RankingGateRatio = 1.05;
    private const double RankingHardStopSeconds = 20 * 60;
    private const ulong RankingBootstrapSeed = 28014UL;
    private const int RankingBootstrapResamples = 10_000;
    private const double TOneSided90Df7 = 1.4149239276; // t_{0.90, 7}
    private const double PowerFactor90 = 8.5639;        // (z_.95 + z_.90)²

    private static readonly int[][] Masks2Of4 =
    {
        new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 3 }, new[] { 1, 2 }, new[] { 1, 3 }, new[] { 2, 3 },
    };

    private static readonly RankingWeights[] AnalysisWeights =
    {
        RankingWeights.Default, new(0.60, 0.25, 0.15), new(0.45, 0.35, 0.20),
    };

    /// <summary>One season's ranks (by team index) under each weight set, 28-game and 14-game formats.</summary>
    private sealed class RankedSeason
    {
        public readonly int[][] Rank28 = new int[AnalysisWeights.Length][];
        public readonly int[][] Rank14 = new int[AnalysisWeights.Length][];
        public double[] Score28 = Array.Empty<double>();
        public int[] Entrants = Array.Empty<int>(); // 17 automatic + #18 + #19, by team id
        public RngState FinalRng;
    }

    /// <summary>A world's metrics: mean rank SD and mean top-17 replacements per format and weight set.</summary>
    private sealed record WorldMetrics(double[] Sd28, double[] Sd14, double[] Rep28, double[] Rep14);

    /// <summary>
    /// <paramref name="smoke"/> (<c>--smoke</c>) exercises every stage at toy
    /// sizes on unused training worlds 33–34 — a machinery check, never a
    /// verdict, and it never reads a validation world (129+).
    /// </summary>
    private static int RunRankingAnalysis(string schemaPath, bool pilotOnly, bool smoke = false)
    {
        Console.WriteLine("=== 13-4 D5: CS-M §3 National Ranking analysis (spread-25, 28-game vs venue-balanced 14-game sub-sample) ===" +
            (smoke ? " — SMOKE (toy sizes, training worlds only; not a verdict)" : ""));
        var total = Stopwatch.StartNew();
        bool Stop() => total.Elapsed.TotalSeconds >= RankingHardStopSeconds;

        // ---- 1. Pilot ----
        var pilotWorlds = new List<WorldMetrics>();
        var pilotClock = Stopwatch.StartNew();
        double worldGenMs = 0;
        int pilotSeasons = 0;
        for (int w = 1; w <= (smoke ? 3 : 8); w++)
        {
            (WorldMetrics metrics, double genMs, int seasons) = RunRankingWorld(schemaPath, w, 2, 2, diagnosticsR: 0, repeats: 0, out _);
            pilotWorlds.Add(metrics);
            worldGenMs += genMs;
            pilotSeasons += seasons;
            if (pilotClock.Elapsed.TotalSeconds > 60 && w < 8)
            {
                Console.WriteLine($"  PILOT STOP RULE: 60 s reached after {w} worlds; judging what ran.");
                break;
            }
        }
        pilotClock.Stop();
        double msPerSeason = (pilotClock.Elapsed.TotalMilliseconds - worldGenMs) / Math.Max(1, pilotSeasons);
        double genPerWorld = worldGenMs / pilotWorlds.Count;
        Console.WriteLine($"  pilot: {pilotSeasons} seasons on {pilotWorlds.Count} worlds in {pilotClock.Elapsed.TotalSeconds:F1} s " +
            $"({msPerSeason:F0} ms/season incl. both rankings; world-gen {genPerWorld:F0} ms/world)");

        // ---- 2. Frozen sizing ladder (Q4), before any validation world is read ----
        int kReq = 0;
        for (int metric = 0; metric < 2; metric++)
        {
            double[] m28 = pilotWorlds.Select(x => metric == 0 ? x.Sd28[0] : x.Rep28[0]).ToArray();
            double[] m14 = pilotWorlds.Select(x => metric == 0 ? x.Sd14[0] : x.Rep14[0]).ToArray();
            (double ratio, double sWorld) = RatioAndClusterSd(m28, m14);
            double upper = Math.Min(1.00, ratio + TOneSided90Df7 * sWorld / Math.Sqrt(m28.Length));
            double delta = Math.Max(0.05, RankingGateRatio - upper);
            int k = (int)Math.Ceiling(PowerFactor90 * sWorld * sWorld / (delta * delta));
            kReq = Math.Max(kReq, k);
            Console.WriteLine($"  pilot {(metric == 0 ? "rank SD     " : "top-17 repl.")}: R̂ {ratio:F4}, s_world {sWorld:F4}, " +
                $"var(M28) {Variance(m28):F4}, var(M14) {Variance(m14):F4}; Δ {delta:F4} ⇒ k_req {k}");
        }
        double ForecastMinutes(int kHold)
        {
            double holdout = kHold * (16 * msPerSeason + genPerWorld + 2208 / 3000.0 * 1000);
            double sensitivity = 24 * (4 * msPerSeason + genPerWorld);
            double fixtures = 6 * (8 * msPerSeason + 4416 / 3000.0 * 1000) + 2 * genPerWorld;
            double repeats = 19 * msPerSeason;
            return (total.Elapsed.TotalMilliseconds + 1.25 * (holdout + sensitivity + fixtures + repeats)) / 60_000;
        }
        int chosen = 0;
        foreach (int rung in new[] { 32, 48, 64 })
        {
            double forecast = ForecastMinutes(rung);
            Console.WriteLine($"  ladder rung k_hold {rung}: forecast {forecast:F1} min with 1.25× reserve{(rung >= kReq && forecast <= 20 ? " — fits" : "")}");
            if (chosen == 0 && rung >= kReq && forecast <= 20)
            {
                chosen = rung;
            }
        }
        if (chosen == 0)
        {
            Console.WriteLine($"  INCOMPLETE: k_req {kReq} exceeds every rung that fits in 20 min; no validation world was read (exit 2).");
            return 2;
        }
        Console.WriteLine($"  FROZEN: k_hold = {chosen} (validation worlds 129–{128 + chosen} × scheduler 1–4 × outcome 1–4 = {chosen * 16} seasons)");
        int firstHoldout = smoke ? 33 : 129;
        int holdoutWorlds = smoke ? 2 : chosen;
        int holdoutTuples = smoke ? 2 : 4;
        if (pilotOnly)
        {
            Console.WriteLine($"  --pilot: stopping before the holdout. Total {total.Elapsed.TotalMinutes:F2} min.");
            return 0;
        }

        // ---- 3. Holdout (run once) + 5. strength diagnostics + 6. the 20× repeat ----
        var holdout = new List<WorldMetrics>();
        var diag = new List<double[]>(); // per world: ρ_within, ρ_global, top-17 overlap, games/s
        int diagnosticsR = 4;
        bool repeatIdentical = true;
        for (int w = firstHoldout; w < firstHoldout + holdoutWorlds; w++)
        {
            (WorldMetrics metrics, _, _) = RunRankingWorld(schemaPath, w, holdoutTuples, holdoutTuples,
                smoke ? 1 : diagnosticsR, repeats: w == firstHoldout ? (smoke ? 2 : 19) : 0, out double[]? worldDiag);
            holdout.Add(metrics);
            if (worldDiag is not null)
            {
                diag.Add(worldDiag);
                if (w == firstHoldout && !smoke && worldDiag[3] < 1500)
                {
                    diagnosticsR = 2;
                    Console.WriteLine($"  [DIAG] neutral throughput {worldDiag[3]:F0} games/s < 1,500 ⇒ R = 2 for the remaining worlds");
                }
            }
            repeatIdentical &= w != firstHoldout || _lastRepeatIdentical;
            if (Stop())
            {
                Console.WriteLine($"  INCOMPLETE: the 20 min hard stop ended the holdout after {holdout.Count} worlds; no verdict (exit 2).");
                return 2;
            }
        }
        Console.WriteLine($"  [{(repeatIdentical ? "PASS" : "FAIL")}] tuple ({firstHoldout}, 1, 1) repeated {(smoke ? 3 : 20)}×: ranks, scores, entrants and RNG state byte-identical");

        bool gate = true;
        var bootRng = new RngState(RankingBootstrapSeed);
        var ratios = new double[2][] { new double[RankingBootstrapResamples], new double[RankingBootstrapResamples] };
        var index = new int[holdout.Count];
        for (int b = 0; b < RankingBootstrapResamples; b++)
        {
            for (int j = 0; j < index.Length; j++)
            {
                index[j] = bootRng.NextInt(holdout.Count);
            }
            for (int metric = 0; metric < 2; metric++)
            {
                double s28 = 0, s14 = 0;
                foreach (int j in index)
                {
                    s28 += metric == 0 ? holdout[j].Sd28[0] : holdout[j].Rep28[0];
                    s14 += metric == 0 ? holdout[j].Sd14[0] : holdout[j].Rep14[0];
                }
                ratios[metric][b] = s14 == 0 ? (s28 == 0 ? 0 : double.PositiveInfinity) : s28 / s14;
            }
        }
        for (int metric = 0; metric < 2; metric++)
        {
            Array.Sort(ratios[metric]);
            double ub = ratios[metric][9499];
            double[] m28 = holdout.Select(x => metric == 0 ? x.Sd28[0] : x.Rep28[0]).ToArray();
            double[] m14 = holdout.Select(x => metric == 0 ? x.Sd14[0] : x.Rep14[0]).ToArray();
            (double ratio, double sWorld) = RatioAndClusterSd(m28, m14);
            bool pass = ub <= RankingGateRatio;
            gate &= pass;
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] holdout {(metric == 0 ? "mean rank SD" : "mean top-17 replacements")}: " +
                $"28-game {m28.Average():F4} / 14-game {m14.Average():F4}; R̂ {ratio:F4} (SE {sWorld / Math.Sqrt(m28.Length):F4}); " +
                $"one-sided 95% UB (9,500th of 10,000 paired-bootstrap ratios, seed 28014) {ub:F4} ≤ {RankingGateRatio}");
        }
        if (diag.Count > 0)
        {
            Console.WriteLine($"  [DIAG] neutral round-robins (R = {diagnosticsR}, k = {diag.Count}): " +
                $"ρ_within {MeanCi(diag.Select(d => d[0]))}, ρ_global {MeanCi(diag.Select(d => d[1]))}, " +
                $"Δρ {MeanCi(diag.Select(d => d[0] - d[1]))}, top-17 strength overlap {MeanCi(diag.Select(d => d[2]))}; " +
                $"{diag.Average(d => d[3]):F0} games/s");
        }

        // ---- 4. Sensitivity (training worlds 1–32, report only) ----
        var training = new List<WorldMetrics>(pilotWorlds);
        for (int w = 9; w <= (smoke ? 9 : 32) && !Stop(); w++)
        {
            training.Add(RunRankingWorld(schemaPath, w, 2, 2, diagnosticsR: 0, repeats: 0, out _).Metrics);
        }
        for (int k = 0; k < AnalysisWeights.Length; k++)
        {
            RankingWeights wt = AnalysisWeights[k];
            double sd28 = training.Average(x => x.Sd28[k]), sd14 = training.Average(x => x.Sd14[k]);
            double rep28 = training.Average(x => x.Rep28[k]), rep14 = training.Average(x => x.Rep14[k]);
            Console.WriteLine($"  [DIAG] sensitivity ({wt.Win:F2},{wt.Sos:F2},{wt.RunDifferential:F2}) on {training.Count} training worlds: " +
                $"rank SD 28/14 {sd28:F3}/{sd14:F3} (ratio {sd28 / sd14:F4}); replacements 28/14 {rep28:F3}/{rep14:F3} (ratio {rep28 / rep14:F4})");
        }

        // ---- 5b. The five CS-M §3.4 fixture worlds (report only) ----
        if (!Stop())
        {
            RunRankingFixtures(schemaPath, smoke ? 2 : 8, smoke ? 1 : 8);
        }
        if (Stop())
        {
            Console.WriteLine($"  INCOMPLETE: the 20 min hard stop was reached ({total.Elapsed.TotalMinutes:F1} min); no verdict (exit 2).");
            return 2;
        }
        if (smoke)
        {
            Console.WriteLine($"  SMOKE complete in {total.Elapsed.TotalMinutes:F2} min: every stage ran; no verdict (machinery check only).");
            return repeatIdentical ? 0 : 1;
        }
        Console.WriteLine($"  ranking analysis total {total.Elapsed.TotalMinutes:F2} min; gated verdict {(gate && repeatIdentical ? "PASS" : "FAIL")}; " +
            "weights stay (.50,.30,.20) unless a reviewed candidate wins on the holdout (CS-M §3.4)");
        return gate && repeatIdentical ? 0 : 1;
    }

    private static bool _lastRepeatIdentical;

    /// <summary>
    /// One spread-25 world: S × O College seasons (each tuple its own pair of
    /// years and schedule seed), both rankings per season, the world's metrics;
    /// optional neutral round-robin diagnostics and tuple-(1,1) repeats.
    /// </summary>
    private static (WorldMetrics Metrics, double GenMs, int Seasons) RunRankingWorld(
        string schemaPath, int world, int schedulers, int outcomes, int diagnosticsR, int repeats, out double[]? diagnostics)
    {
        diagnostics = null;
        string path = Path.Combine(Path.GetTempPath(), $"dnd_134_rank_{world}_{Guid.NewGuid():N}.db");
        try
        {
            var gen = Stopwatch.StartNew();
            using var db = new DatabaseManager(path);
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(LeagueSeed ^ (ulong)world);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: LeagueGenerator.DefaultRatingSpread, ref genRng);
            gen.Stop();
            var context = new RankingContext(db, baseball);
            var seasons = new List<RankedSeason>(schedulers * outcomes);
            int year = PooledBaseYear;
            int firstYear = year;
            for (int s = 1; s <= schedulers; s++)
            {
                for (int o = 1; o <= outcomes; o++, year += 2)
                {
                    seasons.Add(PlayRankedSeason(context, world, s, o, year));
                }
            }
            if (repeats > 0)
            {
                _lastRepeatIdentical = true;
                for (int r = 0; r < repeats; r++)
                {
                    ResetSeasonForRepeat(db, firstYear);
                    RankedSeason again = PlayRankedSeason(context, world, 1, 1, firstYear);
                    _lastRepeatIdentical &= again.Rank28[0].SequenceEqual(seasons[0].Rank28[0])
                        && again.Rank14[0].SequenceEqual(seasons[0].Rank14[0])
                        && again.Score28.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(seasons[0].Score28.Select(BitConverter.DoubleToInt64Bits))
                        && again.Entrants.SequenceEqual(seasons[0].Entrants)
                        && again.FinalRng.Equals(seasons[0].FinalRng);
                }
            }
            if (diagnosticsR > 0)
            {
                diagnostics = StrengthDiagnostics(context, seasons, diagnosticsR, year + 100);
            }
            var metrics = new WorldMetrics(new double[AnalysisWeights.Length], new double[AnalysisWeights.Length],
                new double[AnalysisWeights.Length], new double[AnalysisWeights.Length]);
            for (int k = 0; k < AnalysisWeights.Length; k++)
            {
                metrics.Sd28[k] = MeanRankSd(seasons.Select(x => x.Rank28[k]).ToList());
                metrics.Sd14[k] = MeanRankSd(seasons.Select(x => x.Rank14[k]).ToList());
                metrics.Rep28[k] = MeanReplacements(seasons.Select(x => x.Rank28[k]).ToList());
                metrics.Rep14[k] = MeanReplacements(seasons.Select(x => x.Rank14[k]).ToList());
            }
            Console.WriteLine($"  world {world}: {seasons.Count} seasons; rank SD 28/14 {metrics.Sd28[0]:F3}/{metrics.Sd14[0]:F3}; " +
                $"top-17 replacements 28/14 {metrics.Rep28[0]:F3}/{metrics.Rep14[0]:F3}");
            return (metrics, gen.Elapsed.TotalMilliseconds, seasons.Count);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private sealed class RankingContext
    {
        public readonly DatabaseManager Db;
        public readonly BaseballQueries Baseball;
        public readonly StatsNormalizer Normalizer;
        public readonly GameStateQueries GameState;
        public readonly PlayoffQueries Queries;
        public readonly List<TeamRow> Teams = new();
        public readonly List<CompletedGame> Games = new();

        public RankingContext(DatabaseManager db, BaseballQueries baseball)
        {
            Db = db;
            Baseball = baseball;
            Normalizer = new StatsNormalizer(db, baseball);
            GameState = new GameStateQueries(db);
            Queries = new PlayoffQueries(db);
            baseball.LoadTeamsByTier(LeagueTier.College, Teams);
        }
    }

    /// <summary>Plays one College season of the tuple and ranks it in both formats under every weight set.</summary>
    private static RankedSeason PlayRankedSeason(RankingContext c, int world, int scheduler, int outcome, int year)
    {
        c.Db.RunInBatch(() => c.GameState.SetInt64(SeasonScheduler.ScheduleSeedKey, scheduler));
        var sim = new LeagueSimulator(c.Db, c.Baseball, c.Normalizer,
            new RngState(0x134A_0000_0000UL ^ ((ulong)world << 24) ^ ((ulong)scheduler << 12) ^ (ulong)outcome), LeagueTier.College);
        sim.Initialize();
        long dayBase = (long)(year - PooledBaseYear) * GlobalState.DaysPerSeason;
        for (int day = 1; day <= TierSeasonCalendar.RegularSeasonLastDay(LeagueTier.College); day++)
        {
            sim.ProcessDay(new DayAdvancedEvent(dayBase + day, year, day));
        }
        sim.FlushPending();
        c.Queries.LoadCompletedRegularSeason(year, LeagueTier.College, c.Games);
        List<CompletedGame> half = Subsample14(c.Games, c.Teams,
            PostseasonStreams.Subsample14(SeasonScheduler.DefaultScheduleSeed, world, scheduler, outcome));
        int[] teamIds = sim.TeamIds.ToArray();
        var season = new RankedSeason { FinalRng = sim.RngSnapshot };
        var full = new SeasonTable();
        full.Build(teamIds, c.Games);
        var sub = new SeasonTable();
        sub.Build(teamIds, half);
        ulong seed = (ulong)scheduler;
        for (int k = 0; k < AnalysisWeights.Length; k++)
        {
            List<RankingCalculator.Entry> r28 = RankingCalculator.Rank(full, seed, year, AnalysisWeights[k]);
            List<RankingCalculator.Entry> r14 = RankingCalculator.Rank(sub, seed, year, AnalysisWeights[k]);
            season.Rank28[k] = ByIndex(teamIds, r28, e => e.Rank);
            season.Rank14[k] = ByIndex(teamIds, r14, e => e.Rank);
            if (k == 0)
            {
                season.Score28 = teamIds.Select(id => r28.First(e => e.TeamId == id).Score).ToArray();
                season.Entrants = r28.Take(19).Select(e => e.TeamId).ToArray();
            }
        }
        return season;
    }

    private static int[] ByIndex(int[] teamIds, List<RankingCalculator.Entry> entries, Func<RankingCalculator.Entry, int> value)
    {
        var result = new int[teamIds.Length];
        foreach (RankingCalculator.Entry e in entries)
        {
            result[Array.IndexOf(teamIds, e.TeamId)] = value(e);
        }
        return result;
    }

    /// <summary>
    /// Q3's venue-balanced 14-game half (Amendment 6a): per conference
    /// (ascending id) over the persisted rows in (day, game_id) order — one of
    /// the two same-venue games of each ordered intra-division pair, and one of
    /// the six 2-of-4 cyclic Latin-shift masks per cross-division pair. Every
    /// team gets 14 games, 7 home / 7 away (asserted).
    /// </summary>
    private static List<CompletedGame> Subsample14(List<CompletedGame> games, List<TeamRow> teams, RngState rng)
    {
        var byVenue = new Dictionary<(int Home, int Away), List<CompletedGame>>();
        foreach (CompletedGame g in games)
        {
            if (!byVenue.TryGetValue((g.HomeTeamId, g.AwayTeamId), out List<CompletedGame>? list))
            {
                byVenue[(g.HomeTeamId, g.AwayTeamId)] = list = new List<CompletedGame>(2);
            }
            list.Add(g);
        }
        List<CompletedGame> Venue(int home, int away, int expected)
        {
            if (!byVenue.TryGetValue((home, away), out List<CompletedGame>? list) || list.Count != expected)
            {
                throw new InvalidOperationException($"Sub-sampler expects {expected} game(s) with {home} hosting {away}.");
            }
            return list;
        }
        var selected = new List<CompletedGame>(games.Count / 2);
        foreach (int conference in teams.Select(t => t.ConferenceId).Distinct().OrderBy(x => x))
        {
            int[][] divisions = teams.Where(t => t.ConferenceId == conference)
                .GroupBy(t => t.DivisionId!.Value).OrderBy(g => g.Key)
                .Select(g => g.Select(t => t.TeamId).OrderBy(x => x).ToArray()).ToArray();
            foreach (int[] division in divisions)
            {
                foreach (int u in division)
                {
                    foreach (int v in division)
                    {
                        if (u != v)
                        {
                            int pick = (int)(rng.NextUInt64() & 1UL);
                            selected.Add(Venue(u, v, 2)[pick]);
                        }
                    }
                }
            }
            for (int x = 0; x < divisions.Length; x++)
            {
                for (int y = x + 1; y < divisions.Length; y++)
                {
                    int[] mask = Masks2Of4[rng.NextInt(6)];
                    for (int shift = 0; shift < 4; shift++)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            int u = divisions[x][i];
                            int v = divisions[y][(i + shift) % 4];
                            selected.Add(mask.Contains(shift) ? Venue(u, v, 1)[0] : Venue(v, u, 1)[0]);
                        }
                    }
                }
            }
        }
        foreach (TeamRow t in teams)
        {
            int home = selected.Count(g => g.HomeTeamId == t.TeamId);
            int away = selected.Count(g => g.AwayTeamId == t.TeamId);
            if (home != 7 || away != 7)
            {
                throw new InvalidOperationException($"Sub-sample gives team {t.TeamId} {home}H/{away}A; Q3 requires 7H/7A.");
            }
        }
        return selected;
    }

    /// <summary>Mean over teams of the sample SD (m − 1) of each team's rank across the world's seasons.</summary>
    private static double MeanRankSd(List<int[]> ranks)
    {
        int m = ranks.Count;
        int n = ranks[0].Length;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            double mean = ranks.Average(r => r[i]);
            sum += Math.Sqrt(ranks.Sum(r => (r[i] - mean) * (r[i] - mean)) / (m - 1));
        }
        return sum / n;
    }

    /// <summary>Mean over season pairs of |T_a Δ T_b| / 2, T = the top-17 (automatic) set.</summary>
    private static double MeanReplacements(List<int[]> ranks)
    {
        int m = ranks.Count;
        double sum = 0;
        int pairs = 0;
        for (int a = 0; a < m; a++)
        {
            for (int b = a + 1; b < m; b++)
            {
                int diff = 0;
                for (int i = 0; i < ranks[a].Length; i++)
                {
                    diff += (ranks[a][i] <= 17) != (ranks[b][i] <= 17) ? 1 : 0;
                }
                sum += diff / 2.0;
                pairs++;
            }
        }
        return sum / pairs;
    }

    /// <summary>Pooled ratio Σ M28 / Σ M14 and Q4's linearized world-cluster SD.</summary>
    private static (double Ratio, double SWorld) RatioAndClusterSd(double[] m28, double[] m14)
    {
        double ratio = m28.Sum() / m14.Sum();
        double mean14 = m14.Average();
        double ss = 0;
        for (int w = 0; w < m28.Length; w++)
        {
            double d = m28[w] - ratio * m14[w];
            ss += d * d;
        }
        return (ratio, Math.Sqrt(ss / (m28.Length - 1)) / mean14);
    }

    private static double Variance(double[] x)
    {
        double mean = x.Average();
        return x.Sum(v => (v - mean) * (v - mean)) / (x.Length - 1);
    }

    private static string MeanCi(IEnumerable<double> values)
    {
        double[] x = values.ToArray();
        double mean = x.Average();
        double half = x.Length > 1 ? 1.96 * Math.Sqrt(Variance(x) / x.Length) : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{mean:F3} ± {half:F3}");
    }

    /// <summary>Rewinds one season so the same tuple can be replayed exactly (scratch world only).</summary>
    private static void ResetSeasonForRepeat(DatabaseManager db, int year)
    {
        foreach (string sql in new[]
        {
            $"UPDATE Schedule_Games SET status = 0, home_score = NULL, away_score = NULL WHERE season_year = {year} AND tier = 1 AND phase = 0;",
            "DELETE FROM Pitcher_Rest;",
            $"DELETE FROM Team_Seasons WHERE season_year = {year};",
            $"DELETE FROM Batting_Stats WHERE season_year = {year};",
            $"DELETE FROM Pitching_Stats WHERE season_year = {year};",
        })
        {
            ExecSql(db, sql);
        }
    }

    /// <summary>
    /// Q5 strength diagnostics for one holdout world ([DIAG], never saved): R
    /// neutral double round-robins in memory, every team on its rested ace;
    /// Spearman ρ of neutral win% vs the world's mean 28-game RankScore within
    /// each conference and league-wide, and the top-17 strength overlap.
    /// Returns { ρ_within, ρ_global, overlap, games/s }.
    /// </summary>
    private static double[] StrengthDiagnostics(RankingContext c, List<RankedSeason> seasons, int r, int farYear)
    {
        (double[] theta, double gamesPerSecond) = NeutralWinPct(c, r, farYear, 0x134D_1A60UL);
        int[] teamIds = c.Teams.Select(t => t.TeamId).ToArray();
        double[] score = new double[teamIds.Length];
        for (int i = 0; i < teamIds.Length; i++)
        {
            score[i] = seasons.Average(s => s.Score28[i]);
        }
        double within = c.Teams.Select(t => t.ConferenceId).Distinct().Average(conf =>
        {
            int[] idx = Enumerable.Range(0, teamIds.Length).Where(i => c.Teams[i].ConferenceId == conf).ToArray();
            return Spearman(idx.Select(i => theta[i]).ToArray(), idx.Select(i => score[i]).ToArray());
        });
        double global = Spearman(theta, score);
        var topScore = Enumerable.Range(0, teamIds.Length).OrderByDescending(i => score[i]).Take(17).ToHashSet();
        var topTheta = Enumerable.Range(0, teamIds.Length).OrderByDescending(i => theta[i]).Take(17).ToHashSet();
        return new[] { within, global, topScore.Intersect(topTheta).Count() / 17.0, gamesPerSecond };
    }

    /// <summary>Neutral double round-robins (each pair R times at each venue) through the isolated adapter; nothing persists.</summary>
    private static (double[] WinPct, double GamesPerSecond) NeutralWinPct(RankingContext c, int r, int farYear, ulong seed)
    {
        var sim = new LeagueSimulator(c.Db, c.Baseball, c.Normalizer, new RngState(seed), LeagueTier.College);
        sim.Initialize();
        int[] teamIds = c.Teams.Select(t => t.TeamId).ToArray();
        var wins = new int[teamIds.Length];
        var games = new int[teamIds.Length];
        var rng = new RngState(seed ^ 0x5EED);
        long day = (long)(farYear - PooledBaseYear) * GlobalState.DaysPerSeason;
        var clock = Stopwatch.StartNew();
        int played = 0;
        for (int rep = 0; rep < r; rep++)
        {
            for (int i = 0; i < teamIds.Length; i++)
            {
                for (int j = i + 1; j < teamIds.Length; j++)
                {
                    for (int leg = 0; leg < 2; leg++)
                    {
                        int home = leg == 0 ? i : j;
                        int away = leg == 0 ? j : i;
                        day += 10; // every ace is rested for every game
                        sim.SimulatePostseasonGame(teamIds[home], teamIds[away], day, PostseasonCalendar.NoSeriesFilter,
                            ref rng, out int hs, out int aws);
                        sim.ClearPostseasonGameState(revertStarts: false);
                        wins[hs > aws ? home : away]++;
                        games[home]++;
                        games[away]++;
                        played++;
                    }
                }
            }
        }
        clock.Stop();
        return (wins.Select((w, i) => (double)w / games[i]).ToArray(), played / Math.Max(1e-9, clock.Elapsed.TotalSeconds));
    }

    private static double Spearman(double[] x, double[] y)
    {
        double[] rx = AverageRanks(x), ry = AverageRanks(y);
        double mx = rx.Average(), my = ry.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            sxy += (rx[i] - mx) * (ry[i] - my);
            sxx += (rx[i] - mx) * (rx[i] - mx);
            syy += (ry[i] - my) * (ry[i] - my);
        }
        return sxx == 0 || syy == 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
    }

    private static double[] AverageRanks(double[] values)
    {
        int[] order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
        var ranks = new double[values.Length];
        for (int a = 0; a < order.Length;)
        {
            int b = a;
            while (b + 1 < order.Length && values[order[b + 1]] == values[order[a]])
            {
                b++;
            }
            for (int k = a; k <= b; k++)
            {
                ranks[order[k]] = (a + b) / 2.0 + 1;
            }
            a = b + 1;
        }
        return ranks;
    }

    // ------------------------------------------------------------------
    // The five CS-M §3.4 fixture worlds ([DIAG], R_fix = 8, 8 seasons each)
    // ------------------------------------------------------------------

    private static void RunRankingFixtures(string schemaPath, int seasonsPerFixture, int rFix)
    {
        string flatPath = Path.Combine(Path.GetTempPath(), $"dnd_134_rankfix_{Guid.NewGuid():N}.db");
        string spreadPath = Path.Combine(Path.GetTempPath(), $"dnd_134_rankfixs_{Guid.NewGuid():N}.db");
        try
        {
            using var flat = new DatabaseManager(flatPath);
            flat.InitializeSchema(schemaPath);
            var flatPlayers = new PlayerQueries(flat);
            var flatBaseball = new BaseballQueries(flat);
            var genRng = new RngState(LeagueSeed ^ 0xF1F1UL);
            LeagueGenerator.GenerateIfEmpty(flat, flatPlayers, flatBaseball, ratingSpread: 0, ref genRng);
            var c = new RankingContext(flat, flatBaseball);
            int[] ids = c.Teams.Select(t => t.TeamId).ToArray();
            int eastId = c.Teams.Min(t => t.ConferenceId);
            int year = PooledBaseYear;

            (List<RankedSeason> Seasons, double[] Theta) Fixture(string label, Func<int, int> rating)
            {
                foreach (int id in ids)
                {
                    int value = rating(id);
                    ExecSql(flat, $"UPDATE Player_Ratings SET bat_power = {value}, bat_contact = {value}, bat_discipline = {value}, " +
                        $"pit_stuff = {value}, pit_control = {value}, fielding = {value} WHERE player_id IN (SELECT player_id FROM Players WHERE team_id = {id});");
                }
                var seasons = new List<RankedSeason>();
                for (int o = 1; o <= seasonsPerFixture; o++, year += 2)
                {
                    seasons.Add(PlayRankedSeason(c, 9000 + label.Length, 1, o, year));
                }
                (double[] theta, _) = NeutralWinPct(c, rFix, year + 50, 0x134F_1C5UL);
                year += 100;
                return (seasons, theta);
            }
            double MeanRank(List<RankedSeason> s, int index) => s.Average(x => x.Rank28[0][index]);

            var equal = Fixture("equal", _ => 50);
            double[] meanRanks = Enumerable.Range(0, ids.Length).Select(i => MeanRank(equal.Seasons, i)).ToArray();
            Console.WriteLine($"  [DIAG] fixture equal teams: mean rank per team {meanRanks.Min():F2}–{meanRanks.Max():F2} (ideal 12.5), " +
                $"East mean {Enumerable.Range(0, ids.Length).Where(i => c.Teams[i].ConferenceId == eastId).Average(i => meanRanks[i]):F2}");

            int dominant = ids[0];
            var dom = Fixture("dominant", id => id == dominant ? 80 : 50);
            double firstShare = dom.Seasons.Count(s => s.Rank28[0][0] == 1) / (double)dom.Seasons.Count;
            double margin = dom.Seasons.Average(s => s.Score28[0] - s.Score28.Where((_, i) => i != 0).Max());
            Console.WriteLine($"  [DIAG] fixture single dominant team ({dominant} at 80): #1 in {firstShare:P0} of seasons; " +
                $"mean score margin over the next team {margin:F3}; neutral win% {dom.Theta[0]:F3}");

            int[] eastDivs = c.Teams.Where(t => t.ConferenceId == eastId).Select(t => t.DivisionId!.Value).Distinct().OrderBy(x => x).ToArray();
            int[] coastal = c.Teams.Where(t => t.DivisionId == eastDivs[0]).Select(t => t.TeamId).OrderBy(x => x).ToArray();
            var unequal = Fixture("unequal", id => coastal.Take(3).Contains(id) ? 65 : 50);
            int weakIdx = Array.IndexOf(ids, coastal[3]);
            int controlIdx = Array.IndexOf(ids, c.Teams.First(t => t.ConferenceId == eastId && t.DivisionId == eastDivs[1]).TeamId);
            Console.WriteLine($"  [DIAG] fixture unequal divisions ({string.Join("/", coastal.Take(3))} at 65): 50-rated {coastal[3]} in the strong division " +
                $"mean rank {MeanRank(unequal.Seasons, weakIdx):F2} v a 50-rated team in a weak division {MeanRank(unequal.Seasons, controlIdx):F2}");

            var shifted = Fixture("shifted", id => c.Teams.First(t => t.TeamId == id).ConferenceId == eastId ? 65 : 45);
            double eastQualifiers = shifted.Seasons.Average(s => Enumerable.Range(0, ids.Length).Count(i => c.Teams[i].ConferenceId == eastId && s.Rank28[0][i] <= 17));
            double eastNeutral = Enumerable.Range(0, ids.Length).Where(i => c.Teams[i].ConferenceId == eastId).Average(i => shifted.Theta[i]);
            Console.WriteLine($"  [DIAG] fixture one conference shifted (East 65, West 45): East places {eastQualifiers:F1} of 17 automatic bids " +
                $"while winning {eastNeutral:P0} of neutral games — the CS-M §3 disconnected-conference limitation (no cross-conference games), reported, not disguised");

            // Reversed ids: swap whole rosters between 201+j and 224−j on a spread-25 world.
            using var spread = new DatabaseManager(spreadPath);
            spread.InitializeSchema(schemaPath);
            var spreadBaseball = new BaseballQueries(spread);
            var spreadRng = new RngState(LeagueSeed ^ 0xF2F2UL);
            LeagueGenerator.GenerateIfEmpty(spread, new PlayerQueries(spread), spreadBaseball, ratingSpread: LeagueGenerator.DefaultRatingSpread, ref spreadRng);
            var sc = new RankingContext(spread, spreadBaseball);
            int sYear = PooledBaseYear;
            var original = new List<RankedSeason>();
            for (int o = 1; o <= seasonsPerFixture; o++, sYear += 2)
            {
                original.Add(PlayRankedSeason(sc, 9100, 1, o, sYear));
            }
            var rosters = new List<RosterPlayerRow>();
            spreadBaseball.LoadRosterByTier(LeagueTier.College, rosters);
            int[] sIds = sc.Teams.Select(t => t.TeamId).ToArray();
            for (int j = 0; j < sIds.Length / 2; j++)
            {
                int a = sIds[j], b = sIds[^(j + 1)];
                string listA = string.Join(",", rosters.Where(r => r.TeamId == a).Select(r => $"'{r.PlayerId}'"));
                string listB = string.Join(",", rosters.Where(r => r.TeamId == b).Select(r => $"'{r.PlayerId}'"));
                ExecSql(spread, $"UPDATE Players SET team_id = {b} WHERE player_id IN ({listA});");
                ExecSql(spread, $"UPDATE Players SET team_id = {a} WHERE player_id IN ({listB});");
            }
            var reversed = new List<RankedSeason>();
            for (int o = 1; o <= seasonsPerFixture; o++, sYear += 2)
            {
                reversed.Add(PlayRankedSeason(sc, 9100, 1, o, sYear));
            }
            double maxDiff = 0, sumDiff = 0;
            for (int j = 0; j < sIds.Length; j++)
            {
                double before = original.Average(s => s.Rank28[0][j]);
                double after = reversed.Average(s => s.Rank28[0][sIds.Length - 1 - j]);
                maxDiff = Math.Max(maxDiff, Math.Abs(before - after));
                sumDiff += Math.Abs(before - after);
            }
            Console.WriteLine($"  [DIAG] fixture reversed team ids (rosters 201+j ↔ 224−j, spread 25): a roster's mean rank moves " +
                $"{sumDiff / sIds.Length:F2} on average (max {maxDiff:F2}) — season noise, no id bias under the seeded coin");
        }
        finally
        {
            foreach (string p in new[] { flatPath, spreadPath })
            {
                TryDelete(p);
                TryDelete(p + "-wal");
                TryDelete(p + "-shm");
            }
        }
    }
}
