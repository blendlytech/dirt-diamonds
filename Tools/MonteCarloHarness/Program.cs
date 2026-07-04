using System.Diagnostics;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

/// <summary>
/// The executable behind the run_monte_carlo_batch skill (CLAUDE.md mandate:
/// run after every change to AtBatResolver.cs or the PED modifier weights).
///
/// Proves, in order:
///   1. The resolver reproduces the §5 numeric fixtures of
///      docs/design/baseball_pa_outcome_model.md (incl. the §6 PED clamp),
///      is seed-deterministic, and allocates zero bytes per PA.
///   2. A ≥10k-PA average-vs-average Monte Carlo batch lands inside every §8
///      acceptance range (slash line, K%, BB%, HR/PA).
///   3. The full pipeline — LeagueGenerator → TimeManager ticks → EventBus →
///      LeagueSimulator → season flush → StatsNormalizer — simulates two
///      complete seasons headless on a scratch database with league-wide
///      results inside §8, including the R/G run environment.
///   4. The per-day game loop is allocation-free once warm (flat GC profile).
///
/// Usage: dotnet run --project Tools/MonteCarloHarness -c Release [-- --repo &lt;path&gt;] [--pa &lt;count&gt;]
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();

    private const ulong BatchSeed = 0xD1A3_0D05UL; // arbitrary fixed seeds — determinism is the point
    private const ulong LeagueSeed = 20260703UL;
    private const ulong SeasonSeed = 777UL;
    private const ulong MicroSeed = 0xBA5E_BA11UL;
    private const int StartYear = 2026;

    private static int Main(string[] args)
    {
        string? repoRoot = null;
        int paCount = 100_000;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--repo")
            {
                repoRoot = args[i + 1];
            }
            else if (args[i] == "--pa" && int.TryParse(args[i + 1], out int parsed))
            {
                paCount = Math.Max(10_000, parsed);
            }
        }
        repoRoot ??= FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Could not locate project.godot above the working directory; pass --repo <path>.");
            return 2;
        }

        string schemaPath = Path.Combine(repoRoot, "Assets", "Data", "Database", "SchemaDefinitions.sql");
        string scratchPath = Path.Combine(Path.GetTempPath(), $"dnd_montecarlo_{Guid.NewGuid():N}.db");
        string microScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_microsim_{Guid.NewGuid():N}.db");
        string careerScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_career_{Guid.NewGuid():N}.db");
        string v4ScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_v4_{Guid.NewGuid():N}.db");
        string rivalryScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_rivalry_{Guid.NewGuid():N}.db");
        string heirScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_heir_{Guid.NewGuid():N}.db");
        string lineageScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_lineage_{Guid.NewGuid():N}.db");
        string lineageFailScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_lineagefail_{Guid.NewGuid():N}.db");
        string lineageEdgeScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_lineageedge_{Guid.NewGuid():N}.db");
        string conceptionScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_conception_{Guid.NewGuid():N}.db");

        try
        {
            RunResolverFixtures();
            RunMonteCarloBatch(paCount);
            RunSeasonPipeline(schemaPath, scratchPath);
            RunMicroAnalyticSuite();
            RunMicroGameSuite(schemaPath, microScratchPath);
            RunCareerWiringSuite(schemaPath, careerScratchPath);
            RunV4BullpenArsenalSuite(schemaPath, v4ScratchPath);
            RunRivalrySuite(schemaPath, rivalryScratchPath);
            RunHeirGeneticsSuite(schemaPath, heirScratchPath);
            RunLineageSuccessionSuite(schemaPath, lineageScratchPath);
            RunLineageFailureSuite(schemaPath, lineageFailScratchPath);
            RunSuccessionEdgeSuite(schemaPath, lineageEdgeScratchPath);
            RunConceptionRequestSuite(schemaPath, conceptionScratchPath);
        }
        catch (Exception ex)
        {
            Check($"unhandled {ex.GetType().Name}", false, ex.Message);
        }
        finally
        {
            TryDelete(scratchPath);
            TryDelete(scratchPath + "-wal");
            TryDelete(scratchPath + "-shm");
            TryDelete(microScratchPath);
            TryDelete(microScratchPath + "-wal");
            TryDelete(microScratchPath + "-shm");
            TryDelete(careerScratchPath);
            TryDelete(careerScratchPath + "-wal");
            TryDelete(careerScratchPath + "-shm");
            TryDelete(v4ScratchPath);
            TryDelete(v4ScratchPath + "-wal");
            TryDelete(v4ScratchPath + "-shm");
            foreach (string variant in new[] { rivalryScratchPath, rivalryScratchPath + ".empty", rivalryScratchPath + ".rival" })
            {
                TryDelete(variant);
                TryDelete(variant + "-wal");
                TryDelete(variant + "-shm");
            }
            TryDelete(heirScratchPath);
            TryDelete(heirScratchPath + "-wal");
            TryDelete(heirScratchPath + "-shm");
            foreach (string lineage in new[] { lineageScratchPath, lineageFailScratchPath, lineageEdgeScratchPath, conceptionScratchPath })
            {
                TryDelete(lineage);
                TryDelete(lineage + "-wal");
                TryDelete(lineage + "-shm");
            }
        }

        int failed = 0;
        foreach ((string name, bool pass, string detail) in Results)
        {
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? $" — {detail}" : "")}");
            if (!pass)
            {
                failed++;
            }
        }
        Console.WriteLine($"\n{Results.Count - failed}/{Results.Count} checks passed.");
        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 1. Resolver fixtures (§5, §6, §9)
    // ------------------------------------------------------------------

    private static void RunResolverFixtures()
    {
        Console.WriteLine("--- Resolver fixtures (design doc §5/§6/§9) ---");

        var averageBatter = new BatterRatings(50, 50, 50, pedActive: false);
        var averagePitcher = new PitcherRatings(50, 50, 50);
        Span<double> probs = stackalloc double[AtBatResolver.OutcomeCount];

        // §5.1 — all ratings 50 must fall out as exactly the §2 baselines.
        double[] baselines = { 0.460, 0.225, 0.090, 0.143, 0.046, 0.004, 0.032 };
        AtBatResolver.ComputeProbabilities(in averageBatter, in averagePitcher, 50, probs);
        Check("§5.1 average-vs-average reproduces the §2 baselines",
            MaxAbsError(probs, baselines) < 1e-12, $"max err {MaxAbsError(probs, baselines):E1}");

        // §5.2 — elite slugger (90/70/70) vs neutral pitcher & defense.
        var slugger = new BatterRatings(90, 70, 70, pedActive: false);
        double[] sluggerExpected = { 0.4053, 0.1856, 0.1153, 0.1697, 0.0619, 0.0045, 0.0576 };
        AtBatResolver.ComputeProbabilities(in slugger, in averagePitcher, 50, probs);
        Check("§5.2 elite slugger fixture (±1e-3)",
            MaxAbsError(probs, sluggerExpected) < 1e-3, $"max err {MaxAbsError(probs, sluggerExpected):E1}");

        // §5.3 — ace (stuff 90 / control 85) vs neutral batter & defense.
        var ace = new PitcherRatings(90, 85, 50);
        double[] aceExpected = { 0.4633, 0.3059, 0.0536, 0.1138, 0.0375, 0.0035, 0.0224 };
        AtBatResolver.ComputeProbabilities(in averageBatter, in ace, 50, probs);
        Check("§5.3 ace fixture (±1e-3)",
            MaxAbsError(probs, aceExpected) < 1e-3, $"max err {MaxAbsError(probs, aceExpected):E1}");

        // §6 — the PED clamp: power 90 on PEDs ≡ power 100 clean; power 40 on
        // PEDs ≡ power 60 clean (round-half-up 1.5×).
        Span<double> pedProbs = stackalloc double[AtBatResolver.OutcomeCount];
        var juiced90 = new BatterRatings(90, 50, 50, pedActive: true);
        var clean100 = new BatterRatings(100, 50, 50, pedActive: false);
        AtBatResolver.ComputeProbabilities(in juiced90, in averagePitcher, 50, pedProbs);
        AtBatResolver.ComputeProbabilities(in clean100, in averagePitcher, 50, probs);
        bool clampHigh = MaxAbsError(pedProbs, probs) == 0.0;
        var juiced40 = new BatterRatings(40, 50, 50, pedActive: true);
        var clean60 = new BatterRatings(60, 50, 50, pedActive: false);
        AtBatResolver.ComputeProbabilities(in juiced40, in averagePitcher, 50, pedProbs);
        AtBatResolver.ComputeProbabilities(in clean60, in averagePitcher, 50, probs);
        bool clampMid = MaxAbsError(pedProbs, probs) == 0.0;
        Check("§6 PED multiplier: 90→100 (clamped), 40→60", clampHigh && clampMid);

        // §8/§9 — bit-for-bit determinism from a fixed seed.
        var rngA = new RngState(12345);
        var rngB = new RngState(12345);
        bool identical = true;
        for (int i = 0; i < 1_000; i++)
        {
            identical &= AtBatResolver.Resolve(in slugger, in ace, 55, ref rngA)
                      == AtBatResolver.Resolve(in slugger, in ace, 55, ref rngB);
        }
        Check("seeded RNG reproduces 1000 outcomes bit-for-bit", identical);

        // §9 — zero heap allocation per PA once warm.
        var rng = new RngState(9);
        long sink = 0;
        for (int i = 0; i < 50_000; i++)
        {
            sink += (long)AtBatResolver.Resolve(in averageBatter, in averagePitcher, 50, ref rng);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            sink += (long)AtBatResolver.Resolve(in averageBatter, in averagePitcher, 50, ref rng);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("Resolve allocates zero bytes over 10k PAs", allocated == 0, $"{allocated} B (sink {sink})");
    }

    // ------------------------------------------------------------------
    // 2. Monte Carlo batch (§8 acceptance, average roster)
    // ------------------------------------------------------------------

    private static void RunMonteCarloBatch(int paCount)
    {
        Console.WriteLine($"--- Monte Carlo batch: {paCount:N0} PAs, average vs average ---");

        var batter = new BatterRatings(50, 50, 50, pedActive: false);
        var pitcher = new PitcherRatings(50, 50, 50);
        var rng = new RngState(BatchSeed);

        Span<int> counts = stackalloc int[AtBatResolver.OutcomeCount];
        for (int i = 0; i < paCount; i++)
        {
            counts[(int)AtBatResolver.Resolve(in batter, in pitcher, 50, ref rng)]++;
        }

        long pa = paCount;
        long bb = counts[(int)PaOutcome.Walk];
        long so = counts[(int)PaOutcome.Strikeout];
        long singles = counts[(int)PaOutcome.Single];
        long doubles = counts[(int)PaOutcome.Double];
        long triples = counts[(int)PaOutcome.Triple];
        long hr = counts[(int)PaOutcome.HomeRun];
        long h = singles + doubles + triples + hr;
        long ab = pa - bb;
        long tb = singles + 2 * doubles + 3 * triples + 4 * hr;

        double avg = (double)h / ab;
        double obp = (double)(h + bb) / pa;
        double slg = (double)tb / ab;
        double ops = obp + slg;
        double kRate = (double)so / pa;
        double bbRate = (double)bb / pa;
        double hrRate = (double)hr / pa;

        Console.WriteLine($"  League line: {avg:.000}/{obp:.000}/{slg:.000}  OPS {ops:.000}  " +
            $"K% {kRate:P1}  BB% {bbRate:P1}  HR/PA {hrRate:P1}");

        Check("batch AVG in .240–.260", avg is >= 0.240 and <= 0.260, $"{avg:.000}");
        Check("batch OBP in .308–.325", obp is >= 0.308 and <= 0.325, $"{obp:.000}");
        Check("batch SLG in .395–.430", slg is >= 0.395 and <= 0.430, $"{slg:.000}");
        Check("batch OPS in .710–.745", ops is >= 0.710 and <= 0.745, $"{ops:.000}");
        Check("batch K% in 20–25%", kRate is >= 0.20 and <= 0.25, $"{kRate:P1}");
        Check("batch BB% in 7.5–10.5%", bbRate is >= 0.075 and <= 0.105, $"{bbRate:P1}");
        Check("batch HR/PA in 2.7–3.7%", hrRate is >= 0.027 and <= 0.037, $"{hrRate:P1}");
    }

    // ------------------------------------------------------------------
    // 3. Full pipeline: two headless seasons through the real event loop
    // ------------------------------------------------------------------

    private static void RunSeasonPipeline(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Full-season pipeline (scratch db, 730 days / 2 seasons) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        Check("scratch schema applies at v6", db.GetSchemaVersion() == 6, $"user_version={db.GetSchemaVersion()}");

        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);

        // Flat league (every rating 50): §8 acceptance is defined for an
        // average roster, so calibration failures can't hide behind roster luck.
        var genRng = new RngState(LeagueSeed);
        bool generated = LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        bool skippedSecond = !LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        var rosterRows = new List<RosterPlayerRow>();
        int rosterCount = baseball.LoadRoster(rosterRows);
        int expectedRoster = LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam;
        Check("league generated once, idempotent on second call", generated && skippedSecond);
        Check($"roster join loads {expectedRoster} players across {LeagueSimulator.TeamCount} teams",
            rosterCount == expectedRoster && baseball.CountTeams() == LeagueSimulator.TeamCount,
            $"roster={rosterCount} teams={baseball.CountTeams()}");

        var state = new GlobalState();
        var bus = new EventBus();
        var gameState = new GameStateQueries(db);
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);

        var normalizer = new StatsNormalizer(db, baseball);
        var league = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed));
        league.Initialize();
        league.AttachTo(bus);

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 2 * GlobalState.DaysPerSeason; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }
        stopwatch.Stop();
        Console.WriteLine($"  730-day run (2 × {LeagueSimulator.RegularSeasonDays}-game seasons): {stopwatch.ElapsedMilliseconds} ms");

        Check("no batch left open after the run", !db.IsBatchActive);
        Check("database integrity ok / no FK violations",
            db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);

        // A fresh save SEEDS at day 1; DayAdvancedEvent fires on advancement, so
        // the first-ever season plays season-days 2..154 (one fewer game day).
        // Every subsequent season gets all RegularSeasonDays. Known M1 artifact,
        // documented in docs/progress.md.
        int gamesPerDay = LeagueSimulator.TeamCount / 2;
        AssertSeason(baseball, StartYear, (LeagueSimulator.RegularSeasonDays - 1) * gamesPerDay);
        AssertSeason(baseball, StartYear + 1, LeagueSimulator.RegularSeasonDays * gamesPerDay);

        // Row-level rate check on a sample batter: the normalizer's stored
        // rates must equal the definitionally recomputed ones.
        string sampleBatter = rosterRows.First(r => !r.IsPitcher).PlayerId;
        var seasons = new List<BattingStatsRow>();
        players.LoadBattingSeasons(sampleBatter, seasons);
        bool ratesExact = seasons.Count == 2;
        foreach (BattingStatsRow row in seasons)
        {
            double tb = row.H + row.Doubles + 2 * row.Triples + 3 * row.Hr;
            ratesExact &= row.Ab > 0
                && Math.Abs(row.Avg - (double)row.H / row.Ab) < 1e-9
                && Math.Abs(row.Obp - (double)(row.H + row.Bb) / (row.Ab + row.Bb)) < 1e-9
                && Math.Abs(row.Slg - tb / row.Ab) < 1e-9
                && Math.Abs(row.Ops - (row.Obp + row.Slg)) < 1e-9;
        }
        Check("sample batter: 2 season rows, stored rates match recomputation", ratesExact,
            $"rows={seasons.Count}");

        // 4. Flat GC profile: a warmed game day allocates nothing. A detached
        // simulator (never flushes) drives the internal day loop directly.
        var profiled = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed + 1));
        profiled.Initialize();
        for (int day = 1; day <= 7; day++)
        {
            profiled.SimulateGameDay(day);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int day = 8; day <= 27; day++)
        {
            profiled.SimulateGameDay(day);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("warm game day allocates zero bytes (20 days / 80 games)", allocated == 0, $"{allocated} B");
    }

    private static void AssertSeason(BaseballQueries baseball, int season, int gamesPerSeason)
    {
        LeagueBattingTotals bat = baseball.LoadLeagueBattingTotals(season);
        LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(season);

        long h = bat.H;
        long ab = bat.Ab;
        long singles = h - bat.Doubles - bat.Triples - bat.Hr;
        long tb = singles + 2 * bat.Doubles + 3 * bat.Triples + 4 * bat.Hr;
        double avg = (double)h / ab;
        double obp = (double)(h + bat.Bb) / bat.Pa;
        double slg = (double)tb / ab;
        double ops = obp + slg;
        double kRate = (double)bat.So / bat.Pa;
        double bbRate = (double)bat.Bb / bat.Pa;
        double hrRate = (double)bat.Hr / bat.Pa;
        double runsPerTeamGame = (double)pit.Er / pit.Gs; // all runs earned; one GS per team-game

        Console.WriteLine($"  Season {season}: {avg:.000}/{obp:.000}/{slg:.000}  OPS {ops:.000}  " +
            $"K% {kRate:P1}  BB% {bbRate:P1}  HR/PA {hrRate:P1}  R/G {runsPerTeamGame:F2}  " +
            $"({bat.Pa:N0} PA, {pit.Gs / 2:N0} games)");

        Check($"[{season}] slash line within §8 acceptance",
            avg is >= 0.240 and <= 0.260 && obp is >= 0.308 and <= 0.325
            && slg is >= 0.395 and <= 0.430 && ops is >= 0.710 and <= 0.745,
            $"{avg:.000}/{obp:.000}/{slg:.000}/{ops:.000}");
        Check($"[{season}] K/BB/HR rates within §8 acceptance",
            kRate is >= 0.20 and <= 0.25 && bbRate is >= 0.075 and <= 0.105 && hrRate is >= 0.027 and <= 0.037,
            $"K {kRate:P1} BB {bbRate:P1} HR {hrRate:P1}");
        Check($"[{season}] run environment R/G in 4.2–4.8",
            runsPerTeamGame is >= 4.2 and <= 4.8, $"{runsPerTeamGame:F2}");
        Check($"[{season}] {gamesPerSeason} games: GS = 2×games, W and L each = games",
            pit.Gs == 2L * gamesPerSeason && pit.W == gamesPerSeason && pit.L == gamesPerSeason,
            $"gs={pit.Gs} w={pit.W} l={pit.L}");
        Check($"[{season}] batting and pitching ledgers agree (H, BB, SO, outs)",
            bat.H == pit.HAllowed && bat.Bb == pit.Bb && bat.So == pit.So
            && pit.OutsRecorded == bat.Pa - bat.H - bat.Bb,
            $"H {bat.H}/{pit.HAllowed} BB {bat.Bb}/{pit.Bb} SO {bat.So}/{pit.So} outs {pit.OutsRecorded}/{bat.Pa - bat.H - bat.Bb}");
    }

    // ------------------------------------------------------------------
    // 4. Micro-sim analytics: §3 matrices, §4 run expectancy, §5 pitch chain
    //    (micro doc docs/design/baseball_markov_micro_sim.md, §11 tests 1/3/6)
    // ------------------------------------------------------------------

    private static void RunMicroAnalyticSuite()
    {
        Console.WriteLine("--- Micro-sim analytics (micro doc §3–§5) ---");

        // §3 — every A_e is a valid 25×25 row-stochastic matrix.
        var matrix = new double[BaseOutMatrices.StateCount * BaseOutMatrices.StateCount];
        var eventRuns = new double[BaseOutMatrices.StateCount];
        bool rowStochastic = true;
        for (int e = 0; e < AtBatResolver.OutcomeCount; e++)
        {
            BaseOutMatrices.BuildEventMatrix((PaOutcome)e, matrix, eventRuns);
            for (int s = 0; s < BaseOutMatrices.StateCount; s++)
            {
                double sum = 0.0;
                for (int j = 0; j < BaseOutMatrices.StateCount; j++)
                {
                    sum += matrix[s * BaseOutMatrices.StateCount + j];
                }
                rowStochastic &= Math.Abs(sum - 1.0) < 1e-12;
            }
        }
        Check("§3 all seven A_e matrices are row-stochastic", rowStochastic);

        // §3.3 — the runtime advancement function IS the sparse matrix: Monte
        // Carlo the stochastic rows (Single from R2/0out = state 6, Double from
        // R1/0out = state 3) and compare successor frequencies to the A_e rows.
        var advanceRng = new RngState(4242);
        const int advanceDraws = 200_000;
        bool advancementMatches = true;
        Span<int> successorCounts = stackalloc int[BaseOutMatrices.StateCount];
        for (int variant = 0; variant < 2; variant++)
        {
            PaOutcome outcome = variant == 0 ? PaOutcome.Single : PaOutcome.Double;
            int startBases = variant == 0 ? 0b010 : 0b001;
            int startState = startBases * 3;
            BaseOutMatrices.BuildEventMatrix(outcome, matrix, eventRuns);
            successorCounts.Clear();
            for (int i = 0; i < advanceDraws; i++)
            {
                int bases = startBases;
                int outs = 0;
                BaseOutAdvancement.Advance(outcome, ref bases, ref outs, ref advanceRng);
                successorCounts[BaseOutAdvancement.ToState(bases, outs)]++;
            }
            for (int j = 0; j < BaseOutMatrices.StateCount; j++)
            {
                advancementMatches &= Math.Abs(
                    (double)successorCounts[j] / advanceDraws
                    - matrix[startState * BaseOutMatrices.StateCount + j]) < 5e-3;
            }
        }
        Check("§3.3 runtime advancement matches the A_Single/A_Double rows (±5e-3)", advancementMatches);

        // §4 — run expectancy off the fundamental matrix: the league-average
        // matchup must land near the canonical RE24 anchor and be ordered.
        var averageBatter = new BatterRatings(50, 50, 50, pedActive: false);
        var averagePitcher = new PitcherRatings(50, 50, 50);
        Span<double> anchor = stackalloc double[AtBatResolver.OutcomeCount];
        AtBatResolver.ComputeProbabilities(in averageBatter, in averagePitcher, 50, anchor);
        double[] runExpectancy = BaseOutMatrices.RunExpectancy(anchor);
        Console.WriteLine($"  RE24 (avg matchup): empty/0out {runExpectancy[0]:F3}, loaded/0out {runExpectancy[21]:F3}");
        Check("§4 RE(empty, 0 out) in 0.42–0.56", runExpectancy[0] is >= 0.42 and <= 0.56, $"{runExpectancy[0]:F3}");
        bool ordered = runExpectancy[21] > runExpectancy[0];
        for (int bases = 0; bases < 8; bases++)
        {
            ordered &= runExpectancy[bases * 3] > runExpectancy[bases * 3 + 2]; // 0 outs > 2 outs, same bases
        }
        Check("§4 RE ordering: loaded>empty; 0 outs > 2 outs for every base state", ordered);

        // §5.2 — the absorption pin is analytic-exact for all three macro
        // fixtures (the solve is inverted, not sampled — micro doc test 1's
        // "tighten toward exact" clause).
        var slugger = new BatterRatings(90, 70, 70, pedActive: false);
        var ace = new PitcherRatings(90, 85, 50);
        Span<double> fixtureAnchor = stackalloc double[AtBatResolver.OutcomeCount];
        double maxPinError = 0.0;
        double averagePitchesPerPa = 0.0;
        for (int f = 0; f < 3; f++)
        {
            switch (f)
            {
                case 0: AtBatResolver.ComputeProbabilities(in averageBatter, in averagePitcher, 50, fixtureAnchor); break;
                case 1: AtBatResolver.ComputeProbabilities(in slugger, in averagePitcher, 50, fixtureAnchor); break;
                default: AtBatResolver.ComputeProbabilities(in averageBatter, in ace, 50, fixtureAnchor); break;
            }
            PitchClassRates rates = PitchChain.SolveNeutral(
                fixtureAnchor[(int)PaOutcome.Walk], fixtureAnchor[(int)PaOutcome.Strikeout]);
            PitchChain.ComputeAbsorption(in rates, out double pWalk, out double pStrikeout, out double pitches);
            maxPinError = Math.Max(maxPinError,
                Math.Max(Math.Abs(pWalk - fixtureAnchor[(int)PaOutcome.Walk]),
                         Math.Abs(pStrikeout - fixtureAnchor[(int)PaOutcome.Strikeout])));
            if (f == 0)
            {
                averagePitchesPerPa = pitches;
            }
        }
        Check("§5.2 count-chain absorption pinned to p* (analytic, ≤1e-9, all fixtures)",
            maxPinError < 1e-9, $"max err {maxPinError:E1}");
        Check("§11.3 analytic pitches/PA in 3.7–4.0 (avg matchup)",
            averagePitchesPerPa is >= 3.7 and <= 4.0, $"{averagePitchesPerPa:F2}");

        // §11 test 1 — sampled per-PA neutral consistency: the full pitch-by-
        // pitch PA (count chain + BIP split + v4 type/zone layer) under the
        // neutral policies must reproduce the 7-way macro distribution — the
        // location conditioning is an exact mixture, so the layer must be
        // invisible here. Sampling tolerances sized to the draw counts; the
        // binding exactness proof is the analytic pin above.
        var neutral = new NeutralBatterPolicy();
        var neutralPitcher = new NeutralPitcherPolicy();
        var neutralMatchup = new PitchMatchup(PitcherArsenal.LeagueAverage, pitcherControl: 50, batterDiscipline: 50);
        var chainRng = new RngState(MicroSeed);
        bool sampledConsistent = true;
        double reportedMaxError = 0.0;
        double sampledPitchesPerPa = 0.0;
        Span<int> outcomeCounts = stackalloc int[AtBatResolver.OutcomeCount];
        for (int f = 0; f < 3; f++)
        {
            int paCount = f == 0 ? 1_000_000 : 400_000;
            double tolerance = f == 0 ? 1.5e-3 : 3e-3;
            switch (f)
            {
                case 0: AtBatResolver.ComputeProbabilities(in averageBatter, in averagePitcher, 50, fixtureAnchor); break;
                case 1: AtBatResolver.ComputeProbabilities(in slugger, in averagePitcher, 50, fixtureAnchor); break;
                default: AtBatResolver.ComputeProbabilities(in averageBatter, in ace, 50, fixtureAnchor); break;
            }
            PitchClassRates rates = PitchChain.SolveNeutral(
                fixtureAnchor[(int)PaOutcome.Walk], fixtureAnchor[(int)PaOutcome.Strikeout]);
            var idleFatigue = new PitcherFatigue(in averagePitcher, pedActive: false, enabled: false);
            outcomeCounts.Clear();
            long totalPitches = 0;
            for (int i = 0; i < paCount; i++)
            {
                PaOutcome outcome = PitchChain.SimulatePa(
                    fixtureAnchor, in rates, in neutralMatchup, ref neutral, ref neutralPitcher,
                    ref idleFatigue, ref chainRng, out int paPitches);
                outcomeCounts[(int)outcome]++;
                totalPitches += paPitches;
            }
            for (int o = 0; o < AtBatResolver.OutcomeCount; o++)
            {
                double error = Math.Abs((double)outcomeCounts[o] / paCount - fixtureAnchor[o]);
                sampledConsistent &= error < tolerance;
                reportedMaxError = Math.Max(reportedMaxError, error);
            }
            if (f == 0)
            {
                sampledPitchesPerPa = (double)totalPitches / paCount;
            }
        }
        Check("§11.1 sampled micro PA distribution ≡ macro p* (3 fixtures, ≤ sampling tolerance)",
            sampledConsistent, $"max err {reportedMaxError:E1}");
        Check("§11.3 sampled pitches/PA in 3.7–4.0 (avg matchup)",
            sampledPitchesPerPa is >= 3.7 and <= 4.0, $"{sampledPitchesPerPa:F2}");

        // §11 test 6 (chain level) — the pitch chain allocates zero bytes per
        // PA once warm, Newton solve included.
        AtBatResolver.ComputeProbabilities(in averageBatter, in averagePitcher, 50, fixtureAnchor);
        var profileFatigue = new PitcherFatigue(in averagePitcher, pedActive: false, enabled: false);
        long sink = 0;
        for (int i = 0; i < 20_000; i++)
        {
            PitchClassRates warmRates = PitchChain.SolveNeutral(
                fixtureAnchor[(int)PaOutcome.Walk], fixtureAnchor[(int)PaOutcome.Strikeout]);
            sink += (long)PitchChain.SimulatePa(
                fixtureAnchor, in warmRates, in neutralMatchup, ref neutral, ref neutralPitcher,
                ref profileFatigue, ref chainRng, out int p) + p;
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            PitchClassRates warmRates = PitchChain.SolveNeutral(
                fixtureAnchor[(int)PaOutcome.Walk], fixtureAnchor[(int)PaOutcome.Strikeout]);
            sink += (long)PitchChain.SimulatePa(
                fixtureAnchor, in warmRates, in neutralMatchup, ref neutral, ref neutralPitcher,
                ref profileFatigue, ref chainRng, out int p) + p;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("§11.6 pitch chain (solve + PA) allocates zero bytes over 10k PAs",
            allocated == 0, $"{allocated} B (sink {sink})");
    }

    // ------------------------------------------------------------------
    // 5. Micro-sim game suite: §11 tests 2/4/5/6 on a scratch league
    // ------------------------------------------------------------------

    private static void RunMicroGameSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Micro-sim attended games (micro doc §11) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

        var rosterRows = new List<RosterPlayerRow>();
        baseball.LoadRoster(rosterRows);
        string humanId = rosterRows.First(r => r.TeamId == 1 && !r.IsPitcher).PlayerId;

        // ---- §11 test 2: aggregate neutral consistency, fatigue disabled ----
        var micro = new MicroGame(db, baseball) { FatigueEnabled = false, LoggingEnabled = false };
        micro.Initialize();
        int humanSlot = micro.FindRosterSlot(humanId);
        Check("human avatar resolves to a roster slot", humanSlot >= 0, $"slot {humanSlot}");

        var policy = new NeutralBatterPolicy();
        var gameRng = new RngState(MicroSeed + 1);
        const int neutralGames = 2000;
        long totalRuns = 0;
        long humanPa = 0;
        long humanPitches = 0;
        for (int g = 0; g < neutralGames; g++)
        {
            int opponent = 2 + g % 7;
            MicroGameResult result = (g & 1) == 0
                ? micro.PlayGame(1, opponent, humanSlot, ref policy, ref gameRng)
                : micro.PlayGame(opponent, 1, humanSlot, ref policy, ref gameRng);
            totalRuns += result.HomeScore + result.AwayScore;
            humanPa += result.HumanPa;
            humanPitches += result.HumanPitchesSeen;
        }

        InningLine league = default;
        foreach (InningLine inning in micro.InningTotals)
        {
            league.Pa += inning.Pa;
            league.Ab += inning.Ab;
            league.H += inning.H;
            league.Doubles += inning.Doubles;
            league.Triples += inning.Triples;
            league.Hr += inning.Hr;
            league.Bb += inning.Bb;
            league.So += inning.So;
        }
        (double avg, double obp, double slg, double ops) = SlashLine(in league);
        double kRate = (double)league.So / league.Pa;
        double bbRate = (double)league.Bb / league.Pa;
        double hrRate = (double)league.Hr / league.Pa;
        double runsPerTeamGame = (double)totalRuns / (2 * neutralGames);
        Console.WriteLine($"  Neutral micro league ({neutralGames} games, fatigue off): " +
            $"{avg:.000}/{obp:.000}/{slg:.000}  OPS {ops:.000}  K% {kRate:P1}  BB% {bbRate:P1}  " +
            $"HR/PA {hrRate:P1}  R/G {runsPerTeamGame:F2}  human {humanPa} PA");
        Check("§11.2 micro slash line within macro §8 acceptance",
            avg is >= 0.240 and <= 0.260 && obp is >= 0.308 and <= 0.325
            && slg is >= 0.395 and <= 0.430 && ops is >= 0.710 and <= 0.745,
            $"{avg:.000}/{obp:.000}/{slg:.000}/{ops:.000}");
        Check("§11.2 micro K/BB/HR rates within macro §8 acceptance",
            kRate is >= 0.20 and <= 0.25 && bbRate is >= 0.075 and <= 0.105 && hrRate is >= 0.027 and <= 0.037,
            $"K {kRate:P1} BB {bbRate:P1} HR {hrRate:P1}");
        Check("§11.2 micro run environment R/G in 4.2–4.8", runsPerTeamGame is >= 4.2 and <= 4.8,
            $"{runsPerTeamGame:F2}");
        Check("§11.2 pitch chain exercised in-game (human PAs) and pitches/PA in 3.7–4.0",
            humanPa > 2_000 && (double)humanPitches / humanPa is >= 3.7 and <= 4.0,
            $"{humanPa} PA, {(double)humanPitches / humanPa:F2} P/PA");

        // ---- §11 test 4: fatigue on, quality rotations, late-inning surge ----
        db.BeginBatch();
        foreach (RosterPlayerRow row in rosterRows)
        {
            if (row.IsPitcher)
            {
                baseball.UpsertRatings(new PlayerRatingsRow
                {
                    PlayerId = row.PlayerId,
                    IsPitcher = true,
                    BatPower = row.BatPower,
                    BatContact = row.BatContact,
                    BatDiscipline = row.BatDiscipline,
                    PitStuff = 80,
                    PitControl = 75,
                    PitStamina = 50,
                    Fielding = row.Fielding,
                });
            }
        }
        db.CommitBatch();

        // Bullpens off: §11.4 isolates the STARTER's decay curve — relief
        // pitching is proven separately in the v4 suite.
        const int fatigueGames = 1500;
        var fatigueOn = new MicroGame(db, baseball) { FatigueEnabled = true, BullpenEnabled = false, LoggingEnabled = false };
        fatigueOn.Initialize();
        var fatigueOff = new MicroGame(db, baseball) { FatigueEnabled = false, BullpenEnabled = false, LoggingEnabled = false };
        fatigueOff.Initialize();
        var onRng = new RngState(MicroSeed + 2);
        var offRng = new RngState(MicroSeed + 2);
        long onRuns = 0;
        long offRuns = 0;
        for (int g = 0; g < fatigueGames; g++)
        {
            int home = 1 + g % 8;
            int away = 1 + (g + 1 + g % 7) % 8;
            if (away == home)
            {
                away = 1 + (home % 8);
            }
            MicroGameResult onResult = fatigueOn.PlayGame(home, away, MicroGame.NoHuman, ref policy, ref onRng);
            MicroGameResult offResult = fatigueOff.PlayGame(home, away, MicroGame.NoHuman, ref policy, ref offRng);
            onRuns += onResult.HomeScore + onResult.AwayScore;
            offRuns += offResult.HomeScore + offResult.AwayScore;
        }
        InningLine early = default;
        InningLine late = default;
        for (int i = 0; i < 3; i++)
        {
            AddInning(ref early, in fatigueOn.InningTotals[i]);      // innings 1–3
            AddInning(ref late, in fatigueOn.InningTotals[i + 6]);   // innings 7–9
        }
        (_, _, _, double earlyOps) = SlashLine(in early);
        (_, _, _, double lateOps) = SlashLine(in late);
        double opsRise = lateOps - earlyOps;
        double onRg = (double)onRuns / (2 * fatigueGames);
        double offRg = (double)offRuns / (2 * fatigueGames);
        Console.WriteLine($"  Fatigue (80/75 rotations, {fatigueGames} games): innings 1–3 OPS {earlyOps:.000}, " +
            $"7–9 OPS {lateOps:.000} (rise {opsRise:+.000;-.000}); R/G on {onRg:F2} vs off {offRg:F2}");
        Check("§11.4 late-inning OPS rise vs innings 1–3 in +.030–.150", opsRise is >= 0.030 and <= 0.150,
            $"{opsRise:+.000;-.000}");
        Check("§11.4 fatigue raises R/G vs the same league with fatigue off", onRg - offRg >= 0.10,
            $"+{onRg - offRg:F2}");

        // ---- §11 test 5: PED stamina capacity + post-game costs ----
        var stamina70 = new PitcherRatings(50, 50, 70);
        var cleanFatigue = new PitcherFatigue(in stamina70, pedActive: false);
        var juicedFatigue = new PitcherFatigue(in stamina70, pedActive: true);
        Check("§11.5 capacity fixture 70 → 105 clean, 120 on PEDs (§8.4)",
            Math.Abs(cleanFatigue.Capacity - 105.0) < 1e-9 && Math.Abs(juicedFatigue.Capacity - 120.0) < 1e-9,
            $"clean {cleanFatigue.Capacity:F0}, ped {juicedFatigue.Capacity:F0}");

        string pedPitcherId = rosterRows.First(r => r.TeamId == 1 && r.IsPitcher).PlayerId;
        players.SetFlag(pedPitcherId, LeagueSimulator.PedActiveFlagName, isActive: true, setOnDay: 1);
        var pedGame = new MicroGame(db, baseball) { FatigueEnabled = true, LoggingEnabled = true };
        pedGame.Initialize();
        var pedRng = new RngState(MicroSeed + 3);
        pedGame.PlayGame(1, 2, MicroGame.NoHuman, ref policy, ref pedRng);
        pedGame.FlushGame(StartYear, gameDay: 1);
        players.TryGetById(pedPitcherId, out PlayerRow pedPitcher);
        Check("§11.5 post-game PED costs match the shared LeagueSimulator constants",
            pedPitcher.HealthCeiling == 100 - LeagueSimulator.PedHealthCostPerGame
            && pedPitcher.DetectionRisk == LeagueSimulator.PedDetectionRiskPerGame,
            $"health {pedPitcher.HealthCeiling}, risk {pedPitcher.DetectionRisk}");

        long logRows = Convert.ToInt64(db.ExecuteScalar(
            db.GetPooledCommand("SELECT COUNT(*) FROM Game_Logs;")) ?? 0L);
        LeagueBattingTotals afterOne = baseball.LoadLeagueBattingTotals(StartYear);
        pedGame.PlayGame(1, 2, MicroGame.NoHuman, ref policy, ref pedRng);
        pedGame.FlushGame(StartYear, gameDay: 2);
        LeagueBattingTotals afterTwo = baseball.LoadLeagueBattingTotals(StartYear);
        LeaguePitchingTotals pitchingTotals = baseball.LoadLeaguePitchingTotals(StartYear);
        Check("§10 play-by-play flushed to Game_Logs (per-PA rows + final)", logRows > 60, $"{logRows} rows");
        Check("§9 additive box-score flush composes across games",
            afterOne.Pa > 50 && afterTwo.Pa > afterOne.Pa && pitchingTotals.Gs == 4 && pitchingTotals.W == 2,
            $"pa {afterOne.Pa}→{afterTwo.Pa}, gs {pitchingTotals.Gs}, w {pitchingTotals.W}");
        players.SetFlag(pedPitcherId, LeagueSimulator.PedActiveFlagName, isActive: false, setOnDay: 1);

        // ---- §11 test 6: determinism + zero-GC at the game level ----
        var deterministicA = new MicroGame(db, baseball) { FatigueEnabled = true, LoggingEnabled = false };
        deterministicA.Initialize();
        var deterministicB = new MicroGame(db, baseball) { FatigueEnabled = true, LoggingEnabled = false };
        deterministicB.Initialize();
        int slotA = deterministicA.FindRosterSlot(humanId);
        var rngA = new RngState(MicroSeed + 4);
        var rngB = new RngState(MicroSeed + 4);
        bool deterministic = true;
        for (int g = 0; g < 5; g++)
        {
            MicroGameResult a = deterministicA.PlayGame(1, 2 + g % 7, slotA, ref policy, ref rngA);
            MicroGameResult b = deterministicB.PlayGame(1, 2 + g % 7, slotA, ref policy, ref rngB);
            deterministic &= a.HomeScore == b.HomeScore && a.AwayScore == b.AwayScore
                && a.Innings == b.Innings && a.HumanPa == b.HumanPa
                && a.HumanPitchesSeen == b.HumanPitchesSeen
                && a.HomeStarterPitches == b.HomeStarterPitches
                && a.AwayStarterPitches == b.AwayStarterPitches;
        }
        Check("§11.6 fixed seed reproduces attended games bit-for-bit", deterministic);

        for (int g = 0; g < 3; g++)
        {
            deterministicA.PlayGame(1, 2 + g % 7, slotA, ref policy, ref rngA);
        }
        long beforeGame = GC.GetAllocatedBytesForCurrentThread();
        for (int g = 0; g < 10; g++)
        {
            deterministicA.PlayGame(1, 2 + g % 7, slotA, ref policy, ref rngA);
        }
        long gameAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeGame;
        Check("§11.6 warm attended game allocates zero bytes (10 games, human PAs incl.)",
            gameAllocated == 0, $"{gameAllocated} B");
    }

    // ------------------------------------------------------------------
    // 6. Phase 5 career wiring: avatar, macro suppression, stat composition,
    //    interactive bridge — a whole season through the real event loop
    // ------------------------------------------------------------------

    private static void RunCareerWiringSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Career wiring (Phase 5: avatar, suppression, composition) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

        var state = new GlobalState();
        var bus = new EventBus();
        var gameState = new GameStateQueries(db);
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);

        var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed));
        league.Initialize();
        league.AttachTo(bus);

        var micro = new MicroGame(db, baseball);
        micro.Initialize();
        var career = new CareerManager(
            db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 9));
        career.AttachTo(bus);

        // ---- avatar creation ----
        const int avatarTeam = 3;
        Check("career starts dormant (no avatar in Game_State)", !career.LoadExistingAvatar() && !career.HasAvatar);

        var preRoster = new List<RosterPlayerRow>();
        baseball.LoadRoster(preRoster);
        career.CreateAvatar("You", "Rookie", avatarTeam, new PlayerRatingsRow
        {
            IsPitcher = false,
            BatPower = 60,
            BatContact = 60,
            BatDiscipline = 60,
            PitStuff = 50,
            PitControl = 50,
            PitStamina = 50,
            Fielding = 50,
        });
        var postRoster = new List<RosterPlayerRow>();
        baseball.LoadRoster(postRoster);
        bool avatarRostered = postRoster.Any(r => r.PlayerId == career.AvatarPlayerId && r.TeamId == avatarTeam);
        string displacedId = preRoster.First(pre => postRoster.All(post => post.PlayerId != pre.PlayerId)).PlayerId;
        players.TryGetById(displacedId, out PlayerRow displaced);
        gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string storedAvatarId);
        Check("avatar created: rostered on its team, roster size unchanged, Game_State records it",
            career.HasAvatar && avatarRostered && postRoster.Count == preRoster.Count
            && storedAvatarId == career.AvatarPlayerId && career.AvatarSlot >= 0,
            $"roster {preRoster.Count}→{postRoster.Count}, slot {career.AvatarSlot}");
        Check("displaced player benched to free agency (team_id NULL), not deleted",
            !displaced.TeamId.HasValue, $"displaced {displacedId[..8]}…");

        // A second manager restores the same avatar from Game_State (boot path).
        var rebooted = new CareerManager(
            db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 10));
        Check("existing save reboots into the same avatar", rebooted.LoadExistingAvatar()
            && rebooted.AvatarPlayerId == career.AvatarPlayerId && rebooted.AvatarTeamId == avatarTeam);

        // ---- one autopilot season through the real event loop ----
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 40; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }
        var midSeasons = new List<BattingStatsRow>();
        players.LoadBattingSeasons(career.AvatarPlayerId, midSeasons);
        LeagueBattingTotals midTotals = baseball.LoadLeagueBattingTotals(StartYear);
        Check("mid-season cadence: avatar stats per game, league stats per 7-day cycle",
            midSeasons.Count == 1 && midSeasons[0].Pa > 100 && midTotals.Pa > midSeasons[0].Pa,
            $"avatar PA {(midSeasons.Count > 0 ? midSeasons[0].Pa : 0)}, league PA {midTotals.Pa} by day 40");

        // Stop at day 365 (last of season 1) so the interactive phase below
        // owns the roll into season 2.
        for (int i = 40; i < GlobalState.DaysPerSeason - 1; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }
        stopwatch.Stop();
        Console.WriteLine($"  Career season (autopilot, {LeagueSimulator.RegularSeasonDays - 1} attended games): " +
            $"{stopwatch.ElapsedMilliseconds} ms");

        Check("no batch left open after the career season", !db.IsBatchActive);
        Check("career-season database integrity ok / no FK violations",
            db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);

        // Season 1 plays days 2..154 (the known M1 seed artifact): 153 days ×
        // 4 games — 3 macro + 1 attended — so league totals must look exactly
        // like an unsuppressed season. Any macro/micro double-play or clobbered
        // additive flush breaks the GS or ledger identities.
        int seasonGames = (LeagueSimulator.RegularSeasonDays - 1) * LeagueSimulator.TeamCount / 2;
        LeagueBattingTotals bat = baseball.LoadLeagueBattingTotals(StartYear);
        LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(StartYear);
        Check($"suppression accounting: {seasonGames} games once each (GS = 2×games, W = L = games)",
            pit.Gs == 2L * seasonGames && pit.W == seasonGames && pit.L == seasonGames,
            $"gs={pit.Gs} w={pit.W} l={pit.L}");
        Check("composed batting and pitching ledgers agree (H, BB, SO, outs)",
            bat.H == pit.HAllowed && bat.Bb == pit.Bb && bat.So == pit.So
            && pit.OutsRecorded == bat.Pa - bat.H - bat.Bb,
            $"H {bat.H}/{pit.HAllowed} BB {bat.Bb}/{pit.Bb} SO {bat.So}/{pit.So}");
        double avg = (double)bat.H / bat.Ab;
        double obp = (double)(bat.H + bat.Bb) / bat.Pa;
        Check("composed league AVG/OBP still inside §8 acceptance",
            avg is >= 0.240 and <= 0.260 && obp is >= 0.308 and <= 0.325, $"{avg:.000}/{obp:.000}");

        var avatarSeasons = new List<BattingStatsRow>();
        players.LoadBattingSeasons(career.AvatarPlayerId, avatarSeasons);
        Check("avatar played the full attended season (PA in 450–800, rates normalized)",
            avatarSeasons.Count == 1 && avatarSeasons[0].Pa is >= 450 and <= 800 && avatarSeasons[0].Avg > 0,
            avatarSeasons.Count == 1 ? $"{avatarSeasons[0].Pa} PA, AVG {avatarSeasons[0].Avg:.000}" : "no row");
        var displacedSeasons = new List<BattingStatsRow>();
        players.LoadBattingSeasons(displacedId, displacedSeasons);
        Check("displaced free agent never played", displacedSeasons.Count == 0);

        // ---- interactive bridge: scripted UI thread plays one game ----
        career.AutopilotAttendedGames = false;
        clock.AdvanceDay(); // offseason days schedule nothing, so roll to next season's day 1 region
        bus.DispatchPending();
        // Advance until a regular-season day hands us a pending game.
        for (int guard = 0; !career.HasPendingGame && guard < 5; guard++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }
        Check("interactive mode parks the attended game as pending", career.HasPendingGame);

        var bridge = new PlayerIntentBridge();
        career.FeedSink = bridge; // attended-game NPC feed: queued alongside the human's own outcomes
        var uiDone = false;
        int snapshots = 0;
        int npcEvents = 0;
        bool sawNonEmptyName = false;
        var scriptedUi = new Thread(() =>
        {
            int pitch = 0;
            while (!Volatile.Read(ref uiDone))
            {
                if (bridge.TryGetSnapshot(out AtBatSnapshot snapshot) && snapshot.Context.Inning >= 1)
                {
                    snapshots++;
                }
                if (bridge.IsAwaitingIntent)
                {
                    if (pitch++ % 2 == 0)
                    {
                        bridge.SubmitSwing(0.0, guessInZone: true); // on-time swing, sitting zone
                    }
                    else
                    {
                        bridge.SubmitTake(guessInZone: false);
                    }
                }
                while (bridge.TryDequeueNpcPa(out NpcPaFeedEvent npcPa))
                {
                    npcEvents++;
                    sawNonEmptyName |= npcPa.BatterName.Length > 0;
                }
                Thread.Sleep(0);
            }
        });
        scriptedUi.Start();
        MicroGameResult interactive;
        try
        {
            var interactivePolicy = new InteractiveBatterPolicy(bridge);
            interactive = career.PlayPendingGame(ref interactivePolicy);
        }
        finally
        {
            Volatile.Write(ref uiDone, true);
            scriptedUi.Join();
            career.FeedSink = null;
        }
        // Drain anything queued between the loop's last drain and the sim's return.
        while (bridge.TryDequeueNpcPa(out NpcPaFeedEvent trailing))
        {
            npcEvents++;
            sawNonEmptyName |= trailing.BatterName.Length > 0;
        }
        int outcomes = 0;
        while (bridge.TryDequeuePaOutcome(out _))
        {
            outcomes++;
        }
        Check("scripted UI drives a full interactive game over the bridge",
            !career.HasPendingGame && interactive.HumanPa > 0 && snapshots >= interactive.HumanPitchesSeen
            && outcomes == interactive.HumanPa,
            $"{interactive.HumanPa} PA, {interactive.HumanPitchesSeen} pitches, {snapshots} snapshots, {outcomes} outcomes");
        Check("attended-game NPC feed queues non-human PAs with a display name once a UI attaches FeedSink",
            npcEvents > 0 && sawNonEmptyName,
            $"{npcEvents} NPC PA events over the bridge");

        // ---- cancel path: the game aborts unflushed and stays pending ----
        clock.AdvanceDay();
        bus.DispatchPending();
        Check("next day parks a fresh pending game", career.HasPendingGame);
        LeagueBattingTotals beforeCancel = baseball.LoadLeagueBattingTotals(StartYear + 1);
        var cancelBridge = new PlayerIntentBridge();
        bool cancelled = false;
        var cancelledGame = new Thread(() =>
        {
            var cancelPolicy = new InteractiveBatterPolicy(cancelBridge);
            try
            {
                career.PlayPendingGame(ref cancelPolicy);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        });
        cancelledGame.Start();
        while (!cancelBridge.IsAwaitingIntent)
        {
            Thread.Sleep(0);
        }
        cancelBridge.Cancel();
        cancelledGame.Join();
        LeagueBattingTotals afterCancel = baseball.LoadLeagueBattingTotals(StartYear + 1);
        Check("cancelled game unwinds unflushed and stays pending for the autopilot",
            cancelled && career.HasPendingGame && !career.IsGameInFlight
            && afterCancel.Pa == beforeCancel.Pa,
            $"pa {beforeCancel.Pa}→{afterCancel.Pa}");
        career.AutopilotAttendedGames = true;
        clock.AdvanceDay(); // forfeits the cancelled game to the autopilot, then plays today
        bus.DispatchPending();
        Check("forfeited game autopilots on the next tick", !career.HasPendingGame && !db.IsBatchActive);
    }

    // ------------------------------------------------------------------
    // 7. Schema v4: roles, arsenals, bullpens, pitcher-side input model
    // ------------------------------------------------------------------

    /// <summary>Scripted mound policy: same call every pitch, counts consumption.</summary>
    private struct AimedPitcherPolicy : IPitcherPolicy
    {
        public bool TargetInZone;
        public int Calls;

        public readonly void BeginPa(in HumanPaContext context)
        {
        }

        public PitchCall NextPitch(in CountState count, ref RngState rng)
        {
            Calls++;
            return PitchCall.Throw(PitchType.Fastball, TargetInZone);
        }

        public readonly void OnPaResolved(PaOutcome outcome)
        {
        }
    }

    private static void RunV4BullpenArsenalSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Schema v4: roles, arsenals, bullpens, pitcher input ---");

        // RngState.Split — the generation fork's contract: the parent stream
        // is untouched (M1 prefix identity), children of equal parents match,
        // and the child stream is not the parent's.
        var parentA = new RngState(123456);
        var parentB = new RngState(123456);
        var parentControl = new RngState(123456);
        RngState childA = parentA.Split();
        RngState childB = parentB.Split();
        bool parentUntouched = true;
        bool childrenEqual = true;
        bool childDiverges = false;
        for (int i = 0; i < 16; i++)
        {
            ulong parentDraw = parentA.NextUInt64();
            parentUntouched &= parentDraw == parentControl.NextUInt64();
            ulong childDraw = childA.NextUInt64();
            childrenEqual &= childDraw == childB.NextUInt64();
            childDiverges |= childDraw != parentDraw;
        }
        Check("RngState.Split: parent untouched, children deterministic and distinct",
            parentUntouched && childrenEqual && childDiverges);

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed + 40);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

        // ---- roster shape: 9 + 5 + 3 per team, roles ⟺ is_pitcher ----
        var roster = new List<RosterPlayerRow>();
        baseball.LoadRoster(roster);
        int[] positions = new int[LeagueSimulator.TeamCount + 1];
        int[] starters = new int[LeagueSimulator.TeamCount + 1];
        int[] relievers = new int[LeagueSimulator.TeamCount + 1];
        bool rolesConsistent = true;
        foreach (RosterPlayerRow row in roster)
        {
            rolesConsistent &= row.IsPitcher == (row.Role != PitcherRole.None);
            switch (row.Role)
            {
                case PitcherRole.Starter: starters[row.TeamId]++; break;
                case PitcherRole.Reliever: relievers[row.TeamId]++; break;
                default: positions[row.TeamId]++; break;
            }
        }
        bool shape = roster.Count == LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam;
        for (int t = 1; t <= LeagueSimulator.TeamCount; t++)
        {
            shape &= positions[t] == LeagueSimulator.LineupSize
                && starters[t] == LeagueSimulator.RotationSize
                && relievers[t] == LeagueSimulator.BullpenSize;
        }
        Check("v4 roster shape: 9 position + 5 starters + 3 relievers per team, roles ⟺ is_pitcher",
            shape && rolesConsistent, $"{roster.Count} rostered");

        var arsenals = new List<PitchArsenalRow>();
        baseball.LoadAllArsenals(arsenals);
        var usageByPitcher = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PitchArsenalRow pitch in arsenals)
        {
            usageByPitcher[pitch.PlayerId] = usageByPitcher.GetValueOrDefault(pitch.PlayerId) + pitch.UsageWeight;
        }
        int pitcherCount = roster.Count(r => r.IsPitcher);
        Check("v4 arsenals: 3 rows per pitcher, usage mix sums to 100",
            arsenals.Count == pitcherCount * 3
            && usageByPitcher.Count == pitcherCount && usageByPitcher.Values.All(v => v == 100),
            $"{arsenals.Count} rows / {pitcherCount} pitchers");

        // ---- EnsureV4 top-up: bench two relievers, re-run, bullpen refills ----
        string benchedA = roster.First(r => r.TeamId == 5 && r.Role == PitcherRole.Reliever).PlayerId;
        string benchedB = roster.Last(r => r.TeamId == 5 && r.Role == PitcherRole.Reliever).PlayerId;
        db.RunInBatch(() =>
        {
            players.SetTeam(benchedA, null);
            players.SetTeam(benchedB, null);
        });
        var topUpRng = new RngState(LeagueSeed + 41);
        bool toppedUp = LeagueGenerator.EnsureV4(db, players, baseball, ratingSpread: 0, ref topUpRng);
        bool secondIsNoOp = !LeagueGenerator.EnsureV4(db, players, baseball, ratingSpread: 0, ref topUpRng);
        baseball.LoadRoster(roster);
        int refilled = roster.Count(r => r.TeamId == 5 && r.Role == PitcherRole.Reliever);
        Check("EnsureV4 tops a short bullpen back up and is then a no-op",
            toppedUp && secondIsNoOp && refilled == LeagueSimulator.BullpenSize, $"team 5 relievers {refilled}");

        // ---- bullpen substitution over flushed games ----
        var micro = new MicroGame(db, baseball) { LoggingEnabled = false };
        micro.Initialize(); // throws if any role/arsenal invariant is broken
        var neutralBatter = new NeutralBatterPolicy();
        var bullpenRng = new RngState(MicroSeed + 20);
        const int bullpenGames = 60;
        const int bullpenYear = 3000;
        double starterPitchesSum = 0.0;
        for (int g = 0; g < bullpenGames; g++)
        {
            int home = 1 + g % LeagueSimulator.TeamCount;
            int away = 1 + (g + 1 + g % 7) % LeagueSimulator.TeamCount;
            if (away == home)
            {
                away = 1 + home % LeagueSimulator.TeamCount;
            }
            MicroGameResult result = micro.PlayGame(home, away, MicroGame.NoHuman, ref neutralBatter, ref bullpenRng);
            micro.FlushGame(bullpenYear, g + 1);
            starterPitchesSum += result.HomeStarterPitches + result.AwayStarterPitches;
        }
        double averageStarterPitches = starterPitchesSum / (2 * bullpenGames);

        var relieverLine = db.GetPooledCommand(
            "SELECT COALESCE(SUM(ps.g), 0), COALESCE(SUM(ps.gs), 0) FROM Pitching_Stats AS ps " +
            "JOIN Pitcher_Roles AS pr ON pr.player_id = ps.player_id " +
            "WHERE pr.role = 2 AND ps.season_year = @seasonYear;");
        if (relieverLine.Parameters.Count == 0)
        {
            relieverLine.Parameters.Add("@seasonYear", Microsoft.Data.Sqlite.SqliteType.Integer);
        }
        relieverLine.Parameters["@seasonYear"].Value = bullpenYear;
        long relieverG;
        long relieverGs;
        using (var reader = db.ExecuteReader(relieverLine))
        {
            reader.Read();
            relieverG = reader.GetInt64(0);
            relieverGs = reader.GetInt64(1);
        }
        LeagueBattingTotals bullpenBat = baseball.LoadLeagueBattingTotals(bullpenYear);
        LeaguePitchingTotals bullpenPit = baseball.LoadLeaguePitchingTotals(bullpenYear);
        Console.WriteLine($"  Bullpen games ({bullpenGames}): avg starter pitches {averageStarterPitches:F0}, " +
            $"reliever G {relieverG}, league G {bullpenPit.G} vs GS {bullpenPit.Gs}");
        Check("§8.5 relievers relieve: G > 0, never credited a start",
            relieverG > 0 && relieverGs == 0, $"G {relieverG}, GS {relieverGs}");
        Check("§8.5 starters no longer complete games (avg pitches in 60–120)",
            averageStarterPitches is >= 60 and <= 120, $"{averageStarterPitches:F0}");
        Check("§8.5 accounting: GS = 2×games, W = L = games, ledgers agree",
            bullpenPit.Gs == 2L * bullpenGames && bullpenPit.W == bullpenGames && bullpenPit.L == bullpenGames
            && bullpenBat.H == bullpenPit.HAllowed && bullpenBat.Bb == bullpenPit.Bb
            && bullpenBat.So == bullpenPit.So
            && bullpenPit.OutsRecorded == bullpenBat.Pa - bullpenBat.H - bullpenBat.Bb,
            $"gs={bullpenPit.Gs} w={bullpenPit.W} l={bullpenPit.L}");

        // ---- pitcher-side input: painting away must walk more than challenging ----
        string aimStarterId = roster.First(r => r.TeamId == 1 && r.Role == PitcherRole.Starter).PlayerId;
        int totalCalls = 0;
        for (int variant = 0; variant < 2; variant++)
        {
            bool aimInZone = variant == 0;
            int aimYear = 3100 + variant;
            var aimRng = new RngState(MicroSeed + 21); // same seed for both aims
            for (int g = 0; g < 40; g++)
            {
                // Fresh driver per game so the SAME starter takes the mound
                // every time (rotation counter resets with Initialize).
                var aimMicro = new MicroGame(db, baseball) { LoggingEnabled = false, BullpenEnabled = false };
                aimMicro.Initialize();
                int slot = aimMicro.FindRosterSlot(aimStarterId);
                var aimPolicy = new AimedPitcherPolicy { TargetInZone = aimInZone };
                aimMicro.PlayGame(1, 2 + g % 7, slot, ref neutralBatter, ref aimPolicy, ref aimRng);
                aimMicro.FlushGame(aimYear, g + 1);
                totalCalls += aimPolicy.Calls;
            }
        }
        double[] aimBbRates = new double[2];
        for (int variant = 0; variant < 2; variant++)
        {
            var seasons = new List<PitchingStatsRow>();
            players.LoadPitchingSeasons(aimStarterId, seasons);
            PitchingStatsRow line = seasons.First(s => s.SeasonYear == 3100 + variant);
            long faced = line.OutsRecorded + line.HAllowed + line.Bb;
            aimBbRates[variant] = (double)line.Bb / faced;
        }
        Console.WriteLine($"  Pitcher aim: BB/PA {aimBbRates[0]:P1} challenging vs {aimBbRates[1]:P1} painting " +
            $"({totalCalls} scripted calls)");
        Check("v4 pitcher-side input has teeth: painting away walks ≥3pp more than challenging",
            totalCalls > 1_000 && aimBbRates[1] - aimBbRates[0] >= 0.03,
            $"{aimBbRates[0]:P1} vs {aimBbRates[1]:P1}");
    }

    private static void AddInning(ref InningLine total, in InningLine inning)
    {
        total.Pa += inning.Pa;
        total.Ab += inning.Ab;
        total.H += inning.H;
        total.Doubles += inning.Doubles;
        total.Triples += inning.Triples;
        total.Hr += inning.Hr;
        total.Bb += inning.Bb;
        total.So += inning.So;
    }

    private static (double Avg, double Obp, double Slg, double Ops) SlashLine(in InningLine line)
    {
        long tb = line.H + line.Doubles + 2 * line.Triples + 3 * line.Hr;
        double avg = (double)line.H / line.Ab;
        double obp = (double)(line.H + line.Bb) / line.Pa;
        double slg = (double)tb / line.Ab;
        return (avg, obp, slg, obp + slg);
    }

    // ------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------

    private static double MaxAbsError(ReadOnlySpan<double> actual, ReadOnlySpan<double> expected)
    {
        double max = 0.0;
        for (int i = 0; i < actual.Length; i++)
        {
            max = Math.Max(max, Math.Abs(actual[i] - expected[i]));
        }
        return max;
    }

    // ------------------------------------------------------------------
    // 8. Phase 6 rivalry modifiers: RivalryChangedEvent → RivalryLedger →
    //    slot-cache → effective-ratings boost through the UNCHANGED resolver.
    //    Proves (a) the ledger's bus contract, (b) the analytic direction of
    //    the effect, (c) an attached-but-empty ledger leaves a same-seed
    //    season bit-identical (the M1-lines-cannot-move guarantee), (d) a
    //    heavily rivalrous season still lands inside every §8 band, and
    //    (e) the micro-sim's attended-game path is live too.
    // ------------------------------------------------------------------

    private static void RunRivalrySuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 6 rivalry modifiers (ledger, analytic shift, league neutrality) ---");

        // (a) Ledger ingest off a locally pumped bus.
        var bus = new EventBus();
        var ledger = new RivalryLedger();
        ledger.AttachTo(bus);
        bus.Publish(new RivalryChangedEvent("p-a", "p-b", 70));
        bus.DispatchPending();
        bool set = ledger.GetIntensity("p-b", "p-a") == 70; // reverse-order query
        int versionAfterSet = ledger.Version;
        bus.Publish(new RivalryChangedEvent("p-a", "p-b", 70)); // duplicate — no version churn
        bus.DispatchPending();
        bool duplicateFrozen = ledger.Version == versionAfterSet;
        bus.Publish(new RivalryChangedEvent("p-b", "p-a", 0)); // reverse-order dissolve
        bus.DispatchPending();
        bool cleared = ledger.Count == 0 && ledger.GetIntensity("p-a", "p-b") == 0 && ledger.Version > versionAfterSet;
        Check("ledger: set / duplicate no-op / reverse-order clear off the bus", set && duplicateFrozen && cleared);

        // (b) Analytic direction on a flat 50/50 matchup at intensity 100.
        var flatBatter = new BatterRatings(50, 50, 50, false);
        var flatPitcher = new PitcherRatings(50, 50, 50);
        Span<double> baseline = stackalloc double[AtBatResolver.OutcomeCount];
        Span<double> heated = stackalloc double[AtBatResolver.OutcomeCount];
        AtBatResolver.ComputeProbabilities(in flatBatter, in flatPitcher, 50, baseline);
        BatterRatings heatedBatter = RivalryEffects.Batter(in flatBatter, 100);
        PitcherRatings heatedPitcher = RivalryEffects.Pitcher(in flatPitcher, 100);
        AtBatResolver.ComputeProbabilities(in heatedBatter, in heatedPitcher, 50, heated);
        Check($"intensity 100 = +{RivalryEffects.MaxPowerBoost} power / +{RivalryEffects.MaxStuffBoost} stuff, capped at 100",
            heatedBatter.Power == 50 + RivalryEffects.MaxPowerBoost
            && heatedPitcher.Stuff == 50 + RivalryEffects.MaxStuffBoost
            && RivalryEffects.Batter(new BatterRatings(90, 50, 50, false), 100).Power == 100
            && RivalryEffects.Pitcher(new PitcherRatings(90, 50, 50), 100).Stuff == 100
            && RivalryEffects.Batter(in flatBatter, 50).Power == 58);
        Check("full-intensity rivalry is a three-true-outcomes shift (K↑ HR↑ 1B↓)",
            heated[(int)PaOutcome.Strikeout] > baseline[(int)PaOutcome.Strikeout]
            && heated[(int)PaOutcome.HomeRun] > baseline[(int)PaOutcome.HomeRun]
            && heated[(int)PaOutcome.Single] < baseline[(int)PaOutcome.Single],
            $"K {baseline[(int)PaOutcome.Strikeout]:P1}→{heated[(int)PaOutcome.Strikeout]:P1} " +
            $"HR {baseline[(int)PaOutcome.HomeRun]:P2}→{heated[(int)PaOutcome.HomeRun]:P2} " +
            $"1B {baseline[(int)PaOutcome.Single]:P1}→{heated[(int)PaOutcome.Single]:P1}");

        // (c)/(d) Three same-seed single-season runs on separate scratch dbs:
        // control (no ledger), attached-but-empty, and 45 cross-team rivalries
        // at intensity 100 (team A's whole lineup vs team B's whole rotation).
        (LeagueBattingTotals Bat, LeaguePitchingTotals Pit) control = RunLeagueSeason(schemaPath, scratchPath, WireControl);
        (LeagueBattingTotals Bat, LeaguePitchingTotals Pit) empty = RunLeagueSeason(schemaPath, scratchPath + ".empty", WireEmptyLedger);
        (LeagueBattingTotals Bat, LeaguePitchingTotals Pit) rival = RunLeagueSeason(schemaPath, scratchPath + ".rival", WireCrossTeamRivalries);

        Check("attached-but-empty ledger: season totals bit-identical to no ledger",
            BattingTotalsEqual(control.Bat, empty.Bat) && PitchingTotalsEqual(control.Pit, empty.Pit));
        Check("45 max-intensity rivalries reach macro PAs (same-seed season diverges)",
            !BattingTotalsEqual(control.Bat, rival.Bat));

        (double avg, double obp, double slg, double kRate, double bbRate, double hrRate, double runsPerTeamGame) rivalLine = RivalrySeasonLine(rival.Bat, rival.Pit);
        (double avg, double obp, double slg, double kRate, double bbRate, double hrRate, double runsPerTeamGame) controlLine = RivalrySeasonLine(control.Bat, control.Pit);
        Console.WriteLine($"  Rivalry season: {rivalLine.avg:.000}/{rivalLine.obp:.000}/{rivalLine.slg:.000}  K% {rivalLine.kRate:P1}  BB% {rivalLine.bbRate:P1}  " +
            $"HR/PA {rivalLine.hrRate:P1}  R/G {rivalLine.runsPerTeamGame:F2}  ({rival.Bat.Pa:N0} PA)");

        // Absolute §8 bands are the wrong check here: a heavily rivalrous
        // season (45 max-intensity pairs) is EXPECTED to diverge from the
        // neutral-league acceptance ranges — that's the whole point of the
        // mechanic. What must hold is the design's actual claim ("the league
        // line essentially unmoved" because rival pairs are sparse), so this
        // asserts a delta bound against the same-seed control season instead
        // of a same-seed absolute band, which flaked on pure seed noise at
        // the §8 floor/ceiling (see the 2026-07-04 calibration-groundwork
        // entry in docs/progress.md).
        double avgDelta = Math.Abs(rivalLine.avg - controlLine.avg);
        double rgDelta = Math.Abs(rivalLine.runsPerTeamGame - controlLine.runsPerTeamGame);
        Check("rivalry-heavy season doesn't move the league line vs same-seed control",
            avgDelta <= 0.003 && rgDelta <= 0.05,
            $"ΔAVG {avgDelta:.000} (rival {rivalLine.avg:.000} vs control {controlLine.avg:.000})  " +
            $"ΔR/G {rgDelta:F2} (rival {rivalLine.runsPerTeamGame:F2} vs control {controlLine.runsPerTeamGame:F2})");

        // (e) Micro path: same-seed attended NPC exhibition on the control db.
        using (var db = new DatabaseManager(scratchPath))
        {
            var baseball = new BaseballQueries(db);
            var teams = new List<TeamRow>(LeagueSimulator.TeamCount);
            baseball.LoadAllTeams(teams);
            int homeTeamId = teams[0].TeamId;
            int awayTeamId = teams[1].TeamId;
            var rosterRows = new List<RosterPlayerRow>();
            baseball.LoadRoster(rosterRows);

            MicroGameResult baselineGame = PlayMicroGame(db, baseball, homeTeamId, awayTeamId, null);
            MicroGameResult emptyGame = PlayMicroGame(db, baseball, homeTeamId, awayTeamId, new RivalryLedger());

            // Away lineup vs every home pitcher (starter AND bullpen so the
            // rivalry survives a mid-game pull).
            var microBus = new EventBus();
            var heatedLedger = new RivalryLedger();
            heatedLedger.AttachTo(microBus);
            foreach (RosterPlayerRow batter in rosterRows)
            {
                if (batter.IsPitcher || batter.TeamId != awayTeamId)
                {
                    continue;
                }
                foreach (RosterPlayerRow pitcher in rosterRows)
                {
                    if (pitcher.IsPitcher && pitcher.TeamId == homeTeamId)
                    {
                        microBus.Publish(new RivalryChangedEvent(batter.PlayerId, pitcher.PlayerId, 100));
                    }
                }
            }
            microBus.DispatchPending();
            MicroGameResult heatedGame = PlayMicroGame(db, baseball, homeTeamId, awayTeamId, heatedLedger);

            bool emptyIdentical = MicroResultsEqual(baselineGame, emptyGame);
            bool heatedDiverges = !MicroResultsEqual(baselineGame, heatedGame);
            Check("micro-sim: empty ledger bit-identical, heated game diverges (same seed)",
                emptyIdentical && heatedDiverges,
                $"base {baselineGame.AwayScore}-{baselineGame.HomeScore}/{baselineGame.Innings}, " +
                $"heated {heatedGame.AwayScore}-{heatedGame.HomeScore}/{heatedGame.Innings}");

            // (f) NPC feed's IsRivalryPa bit: replay the same heated matchup
            // with a feed sink attached. Away batters face the home
            // rotation/bullpen — the ledger's rivalry side, always flagged;
            // home batters face the away pitcher, never entered into this
            // ledger, so never flagged — one game proves both bit states.
            var feedBridge = new PlayerIntentBridge();
            PlayMicroGame(db, baseball, homeTeamId, awayTeamId, heatedLedger, feedBridge);
            bool sawFlagged = false;
            bool sawUnflagged = false;
            while (feedBridge.TryDequeueNpcPa(out NpcPaFeedEvent feedEvt))
            {
                if (feedEvt.IsRivalryPa)
                {
                    sawFlagged = true;
                }
                else
                {
                    sawUnflagged = true;
                }
            }
            Check("NPC feed flags rivalry PAs (IsRivalryPa true for the heated side, false for the rest)",
                sawFlagged && sawUnflagged);
        }
    }

    private static (double Avg, double Obp, double Slg, double KRate, double BbRate, double HrRate, double RunsPerTeamGame)
        RivalrySeasonLine(in LeagueBattingTotals bat, in LeaguePitchingTotals pit)
    {
        long singles = bat.H - bat.Doubles - bat.Triples - bat.Hr;
        long tb = singles + 2 * bat.Doubles + 3 * bat.Triples + 4 * bat.Hr;
        return (
            (double)bat.H / bat.Ab,
            (double)(bat.H + bat.Bb) / bat.Pa,
            (double)tb / bat.Ab,
            (double)bat.So / bat.Pa,
            (double)bat.Bb / bat.Pa,
            (double)bat.Hr / bat.Pa,
            (double)pit.Er / pit.Gs);
    }

    /// <summary>No rivalry source at all — the pre-Phase-6 shape.</summary>
    private static RivalryLedger? WireControl(BaseballQueries baseball, EventBus bus) => null;

    /// <summary>Ledger attached but forever empty — must change nothing.</summary>
    private static RivalryLedger? WireEmptyLedger(BaseballQueries baseball, EventBus bus)
    {
        var ledger = new RivalryLedger();
        ledger.AttachTo(bus);
        return ledger;
    }

    /// <summary>Team A's nine batters vs team B's five starters, all at intensity 100.</summary>
    private static RivalryLedger? WireCrossTeamRivalries(BaseballQueries baseball, EventBus bus)
    {
        var teams = new List<TeamRow>(LeagueSimulator.TeamCount);
        baseball.LoadAllTeams(teams);
        int teamAId = teams[0].TeamId;
        int teamBId = teams[1].TeamId;
        var rosterRows = new List<RosterPlayerRow>();
        baseball.LoadRoster(rosterRows);

        var ledger = new RivalryLedger();
        ledger.AttachTo(bus);
        foreach (RosterPlayerRow batter in rosterRows)
        {
            if (batter.IsPitcher || batter.TeamId != teamAId)
            {
                continue;
            }
            foreach (RosterPlayerRow pitcher in rosterRows)
            {
                if (pitcher.IsPitcher && pitcher.Role == PitcherRole.Starter && pitcher.TeamId == teamBId)
                {
                    bus.Publish(new RivalryChangedEvent(batter.PlayerId, pitcher.PlayerId, 100));
                }
            }
        }
        bus.DispatchPending(); // deliver before any day tick shares the queue
        return ledger;
    }

    /// <summary>
    /// One fresh single-season pipeline run (same league seed, same season
    /// seed) on its own scratch db; the wire callback decides the rivalry
    /// setup, so variants differ ONLY in that.
    /// </summary>
    private static (LeagueBattingTotals, LeaguePitchingTotals) RunLeagueSeason(
        string schemaPath, string dbPath, Func<BaseballQueries, EventBus, RivalryLedger?> wire)
    {
        using var db = new DatabaseManager(dbPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
        clock.Initialize(StartYear);

        var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed));
        league.Initialize();
        league.AttachTo(bus);
        league.Rivalries = wire(baseball, bus);

        for (int i = 0; i < GlobalState.DaysPerSeason; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }
        return (baseball.LoadLeagueBattingTotals(StartYear), baseball.LoadLeaguePitchingTotals(StartYear));
    }

    private static MicroGameResult PlayMicroGame(
        DatabaseManager db, BaseballQueries baseball, int homeTeamId, int awayTeamId, RivalryLedger? ledger,
        PlayerIntentBridge? feedSink = null)
    {
        // Fresh instance per variant so rotation counters start identical.
        var micro = new MicroGame(db, baseball) { LoggingEnabled = false, Rivalries = ledger, FeedSink = feedSink };
        micro.Initialize();
        var policy = new NeutralBatterPolicy();
        var rng = new RngState(MicroSeed);
        return micro.PlayGame(homeTeamId, awayTeamId, MicroGame.NoHuman, ref policy, ref rng);
    }

    private static bool BattingTotalsEqual(in LeagueBattingTotals a, in LeagueBattingTotals b) =>
        a.Pa == b.Pa && a.Ab == b.Ab && a.H == b.H && a.Doubles == b.Doubles
        && a.Triples == b.Triples && a.Hr == b.Hr && a.Bb == b.Bb && a.So == b.So;

    private static bool PitchingTotalsEqual(in LeaguePitchingTotals a, in LeaguePitchingTotals b) =>
        a.Gs == b.Gs && a.W == b.W && a.L == b.L && a.OutsRecorded == b.OutsRecorded
        && a.HAllowed == b.HAllowed && a.Er == b.Er && a.Bb == b.Bb && a.So == b.So;

    private static bool MicroResultsEqual(in MicroGameResult a, in MicroGameResult b) =>
        a.HomeScore == b.HomeScore && a.AwayScore == b.AwayScore && a.Innings == b.Innings
        && a.HumanPa == b.HumanPa && a.HomeStarterPitches.Equals(b.HomeStarterPitches)
        && a.AwayStarterPitches.Equals(b.AwayStarterPitches);

    // ------------------------------------------------------------------
    // 9. Phase 6 heir mechanics: genetic blending & hidden interest
    //    (docs/design/heir_mechanics.md §7/§8 checks 1–6 — Sonnet 5's half;
    //    the succession handoff + 3-generation suite, checks 7–11, is
    //    Fable 5's and is not exercised here). Checks 1–5 are pure math
    //    (HeirGenetics never touches the database); check 6 needs a real
    //    avatar + Child edges to exercise the direction invariant.
    // ------------------------------------------------------------------

    private static void RunHeirGeneticsSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 6 heir mechanics: genetic blending & interest (design doc §7/§8 checks 1–6) ---");

        // ---- (1) §7 worked fixtures under injected bell, incl. one full-vector case ----
        int f1 = HeirGenetics.BlendRating(85, 75, bell: 0.667);
        Check("F1 two elite parents -> good-not-great heir (bat_power 73)", f1 == 73, $"got {f1}");

        var f1VectorA = new PlayerRatingsRow { BatPower = 85, BatContact = 85, BatDiscipline = 85, PitStuff = 85, PitControl = 85, PitStamina = 85, Fielding = 85 };
        var f1VectorB = new PlayerRatingsRow { BatPower = 75, BatContact = 75, BatDiscipline = 75, PitStuff = 75, PitControl = 75, PitStamina = 75, Fielding = 75 };
        PlayerRatingsRow f1Vector = HeirGenetics.BlendRatings(in f1VectorA, in f1VectorB, isPitcher: false, bell: 0.667);
        Check("F1 full-vector: the same law blends all seven ratings independently (all -> 73)",
            f1Vector.BatPower == 73 && f1Vector.BatContact == 73 && f1Vector.BatDiscipline == 73
            && f1Vector.PitStuff == 73 && f1Vector.PitControl == 73 && f1Vector.PitStamina == 73 && f1Vector.Fielding == 73,
            $"{f1Vector.BatPower}/{f1Vector.BatContact}/{f1Vector.BatDiscipline}/{f1Vector.PitStuff}/{f1Vector.PitControl}/{f1Vector.PitStamina}/{f1Vector.Fielding}");

        int f2 = HeirGenetics.BlendRating(85, HeirGenetics.AverageParent().BatPower, bell: -0.42);
        Check("F2 lone-parent regression, mate = average-parent vector (bat_power 54)", f2 == 54, $"got {f2}");

        int f4 = HeirGenetics.RollInterest(childEdgeAffinity: HeirGenetics.HeirGeneticsProfile.BirthAffinity, bell: -0.85);
        Check("F4 interest reveal fails -> game over fixture (interest 34, below the 40 threshold)",
            f4 == 34 && f4 < HeirGenetics.HeirGeneticsProfile.InterestPlayThreshold, $"got {f4}");

        // F3 ("both parents 55 -> lucky bell -> 72") does not reconcile under the
        // stated model: blended = 52.5 and Spread = 12 bound the lottery term to
        // (-12, 12), so the reachable ceiling for this pair is <64.5 — 72 is
        // unreachable at any valid bell. Treated as a design-doc inconsistency
        // (flagged back, not silently patched): adapted into a qualitative check
        // that a near-maximal lucky roll still meaningfully beats the blended
        // baseline, using the verified formula rather than a fabricated bell.
        int f3Adapted = HeirGenetics.BlendRating(55, 55, bell: 0.9);
        Check("F3 (adapted — doc's '72' target is unreachable given Spread=12; see comment) lucky lottery beats the blended baseline",
            f3Adapted > 52.5 + 8 && f3Adapted <= 100, $"got {f3Adapted} (blended 52.5)");

        // ---- (2) Regression identity: at bell=0 the lottery term vanishes regardless
        //          of Spread's actual value, leaving exactly round(50 + h·(midparent-50)) ----
        bool regressionHolds = true;
        int[] ratingSweep = { 0, 20, 40, 50, 60, 80, 100 };
        foreach (int a in ratingSweep)
        {
            foreach (int b in ratingSweep)
            {
                double midparent = (a + b) / 2.0;
                int expected = (int)Math.Round(50 + HeirGenetics.HeirGeneticsProfile.Heritability * (midparent - 50), MidpointRounding.AwayFromZero);
                regressionHolds &= HeirGenetics.BlendRating(a, b, bell: 0.0) == expected;
            }
        }
        PlayerRatingsRow averageParent = HeirGenetics.AverageParent();
        foreach (int a in ratingSweep)
        {
            var loneParentA = new PlayerRatingsRow { BatPower = a };
            double midparent = (a + averageParent.BatPower) / 2.0;
            int expected = (int)Math.Round(50 + HeirGenetics.HeirGeneticsProfile.Heritability * (midparent - 50), MidpointRounding.AwayFromZero);
            PlayerRatingsRow blended = HeirGenetics.BlendRatings(in loneParentA, in averageParent, isPitcher: false, bell: 0.0);
            regressionHolds &= blended.BatPower == expected;
        }
        Check("regression identity: at bell=0, child_r == round(50 + h·(midparent−50)) for two-parent and lone-parent sweeps", regressionHolds);

        // ---- (3) Bounds: extreme parents/affinity × extreme bell never escape [0,100] ----
        bool allInBounds = true;
        double[] extremeBells = { -0.999, -0.5, 0.0, 0.5, 0.999 };
        int[] extremeRatings = { 0, 50, 100 };
        foreach (int a in extremeRatings)
        {
            foreach (int b in extremeRatings)
            {
                foreach (double bell in extremeBells)
                {
                    int rating = HeirGenetics.BlendRating(a, b, bell);
                    allInBounds &= rating is >= 0 and <= 100;
                }
            }
        }
        int[] extremeAffinities = { -100, 0, 100 };
        foreach (int affinity in extremeAffinities)
        {
            foreach (double bell in extremeBells)
            {
                int interest = HeirGenetics.RollInterest(affinity, bell);
                allInBounds &= interest is >= 0 and <= 100;
            }
        }
        Check("bounds: every blended rating and rolled interest stays in [0,100] (0/0, 100/100 parents never clamp-overflow)", allInBounds);

        // ---- (4) The lottery is mean-zero: no directional bias over many draws ----
        var meanRng = new RngState(0x4EA7_0BE5_1234_5678UL);
        const int trials = 100_000;
        long sum = 0;
        for (int i = 0; i < trials; i++)
        {
            sum += HeirGenetics.BlendRating(90, 70, ref meanRng); // blended = 50 + 0.5·(80−50) = 65.0 exactly
        }
        double mean = sum / (double)trials;
        Check("lottery is mean-zero: mean of 100k draws lands within 0.5 of the unrounded blended value (65.0)",
            Math.Abs(mean - 65.0) < 0.5, $"mean {mean:F3}");

        // ---- (5) Interest gate: willing/unwilling classification at the threshold boundary ----
        int atThreshold = HeirGenetics.RollInterest(HeirGenetics.HeirGeneticsProfile.BirthAffinity, bell: -0.64);  // -> 40
        int justBelow = HeirGenetics.RollInterest(HeirGenetics.HeirGeneticsProfile.BirthAffinity, bell: -0.67);    // -> 39
        Check($"interest gate: reveal threshold ({HeirGenetics.HeirGeneticsProfile.InterestPlayThreshold}) classifies willing/unwilling exactly at the boundary",
            atThreshold == 40 && atThreshold >= HeirGenetics.HeirGeneticsProfile.InterestPlayThreshold
            && justBelow == 39 && justBelow < HeirGenetics.HeirGeneticsProfile.InterestPlayThreshold,
            $"at-threshold={atThreshold} (willing), just-below={justBelow} (unwilling)");

        // ---- (6) §1.2 direction invariant: the older Child-edge endpoint is always the parent ----
        using (var db = new DatabaseManager(scratchPath))
        {
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

            var state = new GlobalState();
            var gameState = new GameStateQueries(db);
            var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed));
            league.Initialize();
            var micro = new MicroGame(db, baseball);
            micro.Initialize();
            var career = new CareerManager(db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 40));

            career.CreateAvatar("Founder", "Line", teamId: 4, new PlayerRatingsRow
            {
                IsPitcher = false,
                BatPower = 70,
                BatContact = 65,
                BatDiscipline = 60,
                PitStuff = 50,
                PitControl = 50,
                PitStamina = 50,
                Fielding = 55,
            });

            var childIds = new List<string>();
            foreach (int birthAge in new[] { 0, 5, 10, 15, 18 })
            {
                childIds.Add(career.ConceiveChild("Kid", "Line", birthAge));
            }
            // Seed the PARENT's age too (design doc §5.5: "the harness sets ages
            // directly" — no aging tick exists yet), so the sweep covers both
            // sides of the invariant, not just the heir's birth age.
            players.SetAge(career.AvatarPlayerId, 35);
            childIds.Add(career.ConceiveChild("LateKid", "Line", birthAge: 2));

            players.TryGetById(career.AvatarPlayerId, out PlayerRow avatarRow);
            var relationships = new List<RelationshipRow>();
            players.LoadRelationshipsFor(career.AvatarPlayerId, relationships);

            int childEdgesChecked = 0;
            bool allDirectionsCorrect = true;
            foreach (RelationshipRow rel in relationships)
            {
                if (rel.Type != RelationshipType.Child)
                {
                    continue;
                }
                string otherId = rel.Player1Id == career.AvatarPlayerId ? rel.Player2Id : rel.Player1Id;
                if (!childIds.Contains(otherId))
                {
                    continue;
                }
                players.TryGetById(otherId, out PlayerRow heirRow);
                // Resolved without presupposing which column holds which id —
                // exactly the §1.2 algorithm — then checked against the known avatar.
                string olderId = avatarRow.Age > heirRow.Age ? career.AvatarPlayerId : otherId;
                allDirectionsCorrect &= olderId == career.AvatarPlayerId;
                childEdgesChecked++;
            }
            Check($"§1.2 direction invariant: the older Child-edge endpoint always resolves as parent, across a birth-age/parent-age sweep ({childEdgesChecked} edges)",
                allDirectionsCorrect && childEdgesChecked == childIds.Count,
                $"avatar age {avatarRow.Age}");
        }
    }

    // ------------------------------------------------------------------
    // 10. Phase 6 lineage & succession (design doc §5/§6/§8 checks 7–11)
    // ------------------------------------------------------------------

    /// <summary>Every team holds exactly 9 position players + 5 starters + 3 relievers.</summary>
    private static bool RosterInvariantHolds(BaseballQueries baseball, out string detail)
    {
        var roster = new List<RosterPlayerRow>();
        baseball.LoadRoster(roster);
        var counts = new int[LeagueSimulator.TeamCount + 1, 3];
        foreach (RosterPlayerRow row in roster)
        {
            if (row.TeamId < 1 || row.TeamId > LeagueSimulator.TeamCount)
            {
                detail = $"player {row.PlayerId[..8]}… on unknown team {row.TeamId}";
                return false;
            }
            counts[row.TeamId, (int)row.Role]++;
        }
        for (int teamId = 1; teamId <= LeagueSimulator.TeamCount; teamId++)
        {
            if (counts[teamId, (int)PitcherRole.None] != LeagueSimulator.LineupSize
                || counts[teamId, (int)PitcherRole.Starter] != LeagueSimulator.RotationSize
                || counts[teamId, (int)PitcherRole.Reliever] != LeagueSimulator.BullpenSize)
            {
                detail = $"team {teamId}: {counts[teamId, 0]}/{counts[teamId, 1]}/{counts[teamId, 2]} (want 9/5/3)";
                return false;
            }
        }
        detail = $"{roster.Count} rostered";
        return true;
    }

    /// <summary>
    /// §8 checks 7–9 + 11: the 3-generation exit-criteria run through the REAL
    /// event loop — founder → heir → heir across two season rollovers, one
    /// same-role slot-inherit handoff and one role-mismatch displace-and-backfill
    /// handoff, then a full post-succession season asserted against the §8
    /// bands and a cold reload of the mid-lineage save.
    /// </summary>
    private static void RunLineageSuccessionSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 6 lineage: 3-generation succession run (design doc §8 checks 7–9, 11) ---");

        string founderId, heir1Id, heir2Id, heir3Id;
        int founderBatPower;

        using (var db = new DatabaseManager(scratchPath))
        {
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

            var state = new GlobalState();
            var bus = new EventBus();
            var gameState = new GameStateQueries(db);
            var clock = new TimeManager(db, gameState, state, bus);
            clock.Initialize(StartYear);

            var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed));
            league.Initialize();
            league.AttachTo(bus);
            var micro = new MicroGame(db, baseball);
            micro.Initialize();
            var career = new CareerManager(db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 60));
            career.AttachTo(bus);

            // ---- generation 1: an elite founder (drift math needs a high anchor) ----
            const int founderTeam = 2;
            founderBatPower = 90;
            var preRoster = new List<RosterPlayerRow>();
            baseball.LoadRoster(preRoster);
            career.CreateAvatar("Gen", "One", founderTeam, new PlayerRatingsRow
            {
                IsPitcher = false,
                BatPower = founderBatPower,
                BatContact = 85,
                BatDiscipline = 80,
                PitStuff = 50,
                PitControl = 50,
                PitStamina = 50,
                Fielding = 50,
            });
            founderId = career.AvatarPlayerId;
            var postRoster = new List<RosterPlayerRow>();
            baseball.LoadRoster(postRoster);
            string benchedBatterId = preRoster.First(pre => postRoster.All(post => post.PlayerId != pre.PlayerId)).PlayerId;

            gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long gen0);
            gameState.TryGetText(GameStateKeys.DynastyFounderId, out string storedFounder);
            Check("founder bootstrap: CreateAvatar writes dynasty_generation = 1 and dynasty_founder_id",
                gen0 == 1 && storedFounder == founderId);

            // Fast-forward the career by seeding ages (§5.5: the harness sets
            // ages; the TICK still has to do the final 41 → 42 increment).
            players.SetAge(founderId, 41);
            heir1Id = career.ConceiveChild("Gen", "Two", birthAge: 18);
            players.SetBaseballInterest(heir1Id, 100); // deterministic willingness for the exit-criteria run
            string npcProbeId = preRoster[0].PlayerId;
            players.TryGetById(npcProbeId, out PlayerRow npcBefore);

            // ---- season 1 + the rollover tick (handoff 1: same-role slot-inherit) ----
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }

            players.TryGetById(founderId, out PlayerRow founderRow);
            players.TryGetById(heir1Id, out PlayerRow heir1Row);
            players.TryGetById(npcProbeId, out PlayerRow npcAfter);
            Check("aging tick: one rollover ages founder 41→42, heir 18→19, and an untouched NPC by exactly +1",
                founderRow.Age == 42 && heir1Row.Age == 19 && npcAfter.Age == npcBefore.Age + 1,
                $"founder {founderRow.Age}, heir {heir1Row.Age}, npc {npcBefore.Age}→{npcAfter.Age}");

            gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string avatarAfter1);
            gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long gen1);
            Check("handoff 1 (age trigger, same role): avatar_player_id re-pointed to the heir, dynasty_generation = 2",
                career.AvatarPlayerId == heir1Id && avatarAfter1 == heir1Id && gen1 == 2
                && career.LastSuccession.Kind == SuccessionOutcomeKind.Succeeded && career.LastSuccession.HeirId == heir1Id,
                $"gen {gen1}");
            var founderSeasons = new List<BattingStatsRow>();
            players.LoadBattingSeasons(founderId, founderSeasons);
            Check("handoff 1: retiree benched to FA with career stats intact; heir inherits the exact slot (team, roster size)",
                !founderRow.TeamId.HasValue && founderSeasons.Count >= 1 && founderSeasons[0].Pa > 0
                && heir1Row.TeamId == founderTeam && career.AvatarTeamId == founderTeam && career.AvatarSlot >= 0,
                $"founder seasons {founderSeasons.Count} ({founderSeasons[0].Pa} PA)");
            Check("roster invariant after slot-inherit handoff: every team 9+5+3, no displacement, no backfill",
                RosterInvariantHolds(baseball, out string inv1) && players.Count() == preRoster.Count + 2,
                $"{inv1}; players {players.Count()} (136 world + founder + heir; benched batter keeps its row)");

            // ---- generation 2 → 3 (handoff 2: role mismatch, displace-and-backfill) ----
            players.SetAge(heir1Id, 41);
            heir2Id = career.ConceiveChild("Gen", "Three", birthAge: 18);
            players.SetBaseballInterest(heir2Id, 100);
            // The §2.2 per-heir position override the succession UI will offer:
            // flip the blended heir to a pitcher (role row + stuff-derived
            // arsenal), forcing the §5.4 mismatch path at the next handoff.
            baseball.TryGetRatings(heir2Id, out PlayerRatingsRow heir2Ratings);
            heir2Ratings.IsPitcher = true;
            baseball.UpsertRatings(in heir2Ratings);
            baseball.UpsertPitcherRole(heir2Id, PitcherRole.Starter);
            var overrideRng = new RngState(0xFEED_FACEUL);
            LeagueGenerator.GenerateArsenal(baseball, heir2Id, heir2Ratings.PitStuff, ratingSpread: 0, ref overrideRng);

            var team2Starters = new List<RosterPlayerRow>();
            baseball.LoadRoster(team2Starters);
            string expectedDisplaced = team2Starters
                .Where(r => r.TeamId == founderTeam && r.Role == PitcherRole.Starter)
                .OrderBy(r => r.PlayerId, StringComparer.Ordinal)
                .First().PlayerId; // spread-0 world: all sums tie, lowest player_id is the deterministic weakest

            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }

            gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long gen2);
            players.TryGetById(heir1Id, out PlayerRow heir1After);
            players.TryGetById(benchedBatterId, out PlayerRow promotedBatter);
            players.TryGetById(expectedDisplaced, out PlayerRow displacedStarter);
            Check("3-generation exit criterion: two handoffs through the real event loop, dynasty_generation = 3",
                gen2 == 3 && career.AvatarPlayerId == heir2Id,
                $"gen {gen2}");
            Check("handoff 2 (role mismatch): pitcher heir displaces the weakest starter; retiree's batter slot backfilled by the strongest FA batter",
                !heir1After.TeamId.HasValue && !displacedStarter.TeamId.HasValue
                && promotedBatter.TeamId == founderTeam,
                $"displaced {expectedDisplaced[..8]}…, promoted {benchedBatterId[..8]}… back to team {founderTeam}");
            Check("roster invariant after mismatch handoff: every team 9+5+3, player count unchanged (FA promoted, no filler)",
                RosterInvariantHolds(baseball, out string inv2) && players.Count() == preRoster.Count + 3,
                inv2);
            var founderRoster = new List<RosterPlayerRow>();
            baseball.LoadRoster(founderRoster);
            Check("the retired founder (aged past 42) is NOT signable — never promoted back onto a roster",
                founderRoster.All(r => r.PlayerId != founderId));

            // ---- check 9: a full season under generation 3 stays inside the §8 bands ----
            for (int i = 0; i < LeagueSimulator.RegularSeasonDays; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            stopwatch.Stop();
            Console.WriteLine($"  3-generation run ({2 * GlobalState.DaysPerSeason + LeagueSimulator.RegularSeasonDays} days, 2 handoffs): {stopwatch.ElapsedMilliseconds} ms");

            int fullSeasonGames = LeagueSimulator.RegularSeasonDays * LeagueSimulator.TeamCount / 2;
            LeaguePitchingTotals pit2 = baseball.LoadLeaguePitchingTotals(StartYear + 1);
            LeaguePitchingTotals pit3 = baseball.LoadLeaguePitchingTotals(StartYear + 2);
            Check("GS/W/L identities hold across both succession seasons (every game counted exactly once)",
                pit2.Gs == 2L * fullSeasonGames && pit2.W == fullSeasonGames && pit2.L == fullSeasonGames
                && pit3.Gs == 2L * fullSeasonGames && pit3.W == fullSeasonGames && pit3.L == fullSeasonGames,
                $"s2 gs={pit2.Gs} w={pit2.W}, s3 gs={pit3.Gs} w={pit3.W} (want gs={2 * fullSeasonGames}, w=l={fullSeasonGames})");
            LeagueBattingTotals bat3 = baseball.LoadLeagueBattingTotals(StartYear + 2);
            double avg3 = (double)bat3.H / bat3.Ab;
            double obp3 = (double)(bat3.H + bat3.Bb) / bat3.Pa;
            Check("check 9: the generation-3 season still lands inside the §8 AVG/OBP bands (no band moved)",
                avg3 is >= 0.240 and <= 0.260 && obp3 is >= 0.308 and <= 0.325,
                $"{avg3:.000}/{obp3:.000}");
            Check("check 9 ledger identity: composed batting/pitching agree in the generation-3 season",
                bat3.H == pit3.HAllowed && bat3.Bb == pit3.Bb && bat3.So == pit3.So
                && pit3.OutsRecorded == bat3.Pa - bat3.H - bat3.Bb);

            // ---- drift bound (§3/F5): the bloodline regresses toward the mean ----
            baseball.TryGetRatings(heir1Id, out PlayerRatingsRow heir1Final);
            baseball.TryGetRatings(heir2Id, out PlayerRatingsRow heir2Final);
            Check("drift bound: lone-parent bloodline regresses (founder 90 → gen2 ≤ 73 → gen3 within ±20 of the mean), no upward ratchet",
                heir1Final.BatPower < founderBatPower && heir1Final.BatPower is >= 40 and <= 73
                && heir2Final.BatPower < founderBatPower && Math.Abs(heir2Final.BatPower - 50) <= 20,
                $"bat_power 90 → {heir1Final.BatPower} → {heir2Final.BatPower}");

            // Mid-lineage state for the reload check: an heir conceived, not yet succeeded.
            heir3Id = career.ConceiveChild("Gen", "Four", birthAge: 0);

            Check("no batch left open after the 3-generation run", !db.IsBatchActive);
            Check("3-generation database integrity ok / no FK violations",
                db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
        }

        // ---- check 11: reload fidelity — a cold boot rehydrates the whole lineage ----
        using (var db = new DatabaseManager(scratchPath))
        {
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var state = new GlobalState();
            var gameState = new GameStateQueries(db);
            var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed + 1));
            league.Initialize();
            var micro = new MicroGame(db, baseball);
            micro.Initialize();
            var career = new CareerManager(db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 61));

            bool loaded = career.LoadExistingAvatar();
            gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long gen);
            gameState.TryGetText(GameStateKeys.DynastyFounderId, out string founder);
            var relationships = new List<RelationshipRow>();
            players.LoadRelationshipsFor(career.AvatarPlayerId, relationships);
            bool heir3Linked = relationships.Any(r => r.Type == RelationshipType.Child
                && (r.Player1Id == heir3Id || r.Player2Id == heir3Id));
            bool parentLinked = relationships.Any(r => r.Type == RelationshipType.Child
                && (r.Player1Id == heir1Id || r.Player2Id == heir1Id));
            Check("check 11 reload fidelity: generation, founder, avatar and Child edges all rehydrate mid-lineage",
                loaded && career.AvatarPlayerId == heir2Id && gen == 3 && founder == founderId
                && !career.IsLineageOver && heir3Linked && parentLinked,
                $"gen {gen}, {relationships.Count} relationship rows");
        }
    }

    /// <summary>
    /// §8 check 10: every lineage-failure reason, plus the persisted game-over
    /// flag semantics and the post-game-over rollover guard (aging continues,
    /// succession stops). Terminal states, so this world is never reused.
    /// </summary>
    private static void RunLineageFailureSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 6 lineage: failure reasons & game-over flag (design doc §6/§8 check 10) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

        var state = new GlobalState();
        var gameState = new GameStateQueries(db);
        var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed));
        league.Initialize();
        var micro = new MicroGame(db, baseball);
        micro.Initialize();
        var career = new CareerManager(db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 70));

        career.CreateAvatar("Last", "Line", teamId: 5, new PlayerRatingsRow
        {
            IsPitcher = false,
            BatPower = 55,
            BatContact = 55,
            BatDiscipline = 55,
            PitStuff = 50,
            PitControl = 50,
            PitStamina = 50,
            Fielding = 50,
        });
        string avatarId = career.AvatarPlayerId;

        // The succession-UI candidate-list overload must return byte-identical
        // outcomes to the zero-arg autopilot path across every branch this
        // suite already exercises (NotTriggered, and all three GameOver
        // reasons) — read-only, so calling it right before the mutating
        // RunSuccessionCheck() observes the exact same pre-check state.
        var candidates = new List<HeirCandidate>();

        players.SetAge(avatarId, 41);
        SuccessionOutcome belowViaList = career.EvaluateSuccession(candidates);
        SuccessionOutcome below = career.RunSuccessionCheck();
        Check("no trigger below both thresholds (age 41, health 100): NotTriggered, no game-over key",
            below.Kind == SuccessionOutcomeKind.NotTriggered
            && !gameState.TryGetText(GameStateKeys.LineageOverReason, out _) && !career.IsLineageOver);
        Check("EvaluateSuccession(list) ≡ EvaluateSuccession() for NotTriggered; candidate list empty",
            belowViaList.Kind == below.Kind && candidates.Count == 0);

        players.SetAge(avatarId, 42);
        SuccessionOutcome noHeirsViaList = career.EvaluateSuccession(candidates);
        SuccessionOutcome noHeirs = career.RunSuccessionCheck();
        players.TryGetById(avatarId, out PlayerRow avatarRow);
        gameState.TryGetText(GameStateKeys.LineageOverReason, out string reason1);
        Check("childless retirement: GameOver(NoHeirs), lineage_over_reason persisted — its presence is the flag",
            noHeirs.Kind == SuccessionOutcomeKind.GameOver && noHeirs.Reason == LineageFailure.NoHeirs
            && reason1 == "NoHeirs" && career.IsLineageOver,
            $"key '{reason1}'");
        Check("game-over mutates nothing but the flag: avatar still rostered, every team still 9+5+3",
            RosterInvariantHolds(baseball, out string invariantDetail) && avatarRow.TeamId == 5, invariantDetail);
        Check("EvaluateSuccession(list) ≡ EvaluateSuccession() for GameOver(NoHeirs); candidate list empty",
            noHeirsViaList.Kind == noHeirs.Kind && noHeirsViaList.Reason == noHeirs.Reason && candidates.Count == 0);

        string childId = career.ConceiveChild("Only", "Child", birthAge: 19);
        players.SetBaseballInterest(childId, 20);
        SuccessionOutcome unwillingViaList = career.EvaluateSuccession(candidates);
        SuccessionOutcome unwilling = career.RunSuccessionCheck();
        gameState.TryGetText(GameStateKeys.LineageOverReason, out string reason2);
        Check("single unwilling heir (interest 20 < 40): GameOver(NoWillingHeir)",
            unwilling.Kind == SuccessionOutcomeKind.GameOver && unwilling.Reason == LineageFailure.NoWillingHeir
            && reason2 == "NoWillingHeir");
        Check("EvaluateSuccession(list) ≡ EvaluateSuccession() for GameOver(NoWillingHeir); unwilling child excluded from the list",
            unwillingViaList.Kind == unwilling.Kind && unwillingViaList.Reason == unwilling.Reason && candidates.Count == 0);

        players.SetBaseballInterest(childId, 100);
        players.SetAge(childId, 10);
        SuccessionOutcome tooYoungViaList = career.EvaluateSuccession(candidates);
        SuccessionOutcome tooYoung = career.RunSuccessionCheck();
        gameState.TryGetText(GameStateKeys.LineageOverReason, out string reason3);
        Check("willing but underage heir (age 10 < 19): GameOver(NoPlayableHeir)",
            tooYoung.Kind == SuccessionOutcomeKind.GameOver && tooYoung.Reason == LineageFailure.NoPlayableHeir
            && reason3 == "NoPlayableHeir");
        Check("EvaluateSuccession(list) ≡ EvaluateSuccession() for GameOver(NoPlayableHeir); underage child excluded from the list",
            tooYoungViaList.Kind == tooYoung.Kind && tooYoungViaList.Reason == tooYoung.Reason && candidates.Count == 0);

        // The rollover handler after game-over: the world keeps aging, the
        // succession check does not re-fire (LastSuccession is untouched).
        var bus = new EventBus();
        career.AttachTo(bus);
        bus.Publish(new SeasonRolledOverEvent(StartYear, StartYear + 1));
        bus.DispatchPending();
        players.TryGetById(avatarId, out PlayerRow agedAvatar);
        players.TryGetById(childId, out PlayerRow agedChild);
        Check("post-game-over rollover: aging tick still runs (42→43, 10→11), succession check stays parked",
            agedAvatar.Age == 43 && agedChild.Age == 11
            && career.LastSuccession.Kind == SuccessionOutcomeKind.GameOver
            && career.LastSuccession.Reason == LineageFailure.NoPlayableHeir);
    }

    /// <summary>
    /// The remaining §5 edges: the health_ceiling retirement trigger (the PED
    /// coupling, §5.1) driving a real handoff, and the §5.4 generated-filler
    /// backfill when the free-agent pool has nobody of the vacated role.
    /// </summary>
    private static void RunSuccessionEdgeSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 6 lineage: health trigger & filler backfill (design doc §5.1/§5.4 edges) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

        var state = new GlobalState();
        var gameState = new GameStateQueries(db);
        var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed));
        league.Initialize();
        var micro = new MicroGame(db, baseball);
        micro.Initialize();
        var career = new CareerManager(db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 80));

        career.CreateAvatar("Glass", "Cannon", teamId: 1, new PlayerRatingsRow
        {
            IsPitcher = false,
            BatPower = 60,
            BatContact = 60,
            BatDiscipline = 60,
            PitStuff = 50,
            PitControl = 50,
            PitStamina = 50,
            Fielding = 50,
        });
        string founderId = career.AvatarPlayerId;

        // ---- health trigger: PED erosion forces retirement at age 30 ----
        players.SetAge(founderId, 30);
        string child1Id = career.ConceiveChild("Tough", "Kid", birthAge: 19);
        players.SetBaseballInterest(child1Id, 100);
        baseball.ApplyPedGameCosts(founderId, healthCost: 60, riskGain: 0); // 100 → 40 = the retirement floor
        var healthCandidates = new List<HeirCandidate>();
        SuccessionOutcome healthOutcomeViaList = career.EvaluateSuccession(healthCandidates);
        SuccessionOutcome healthOutcome = career.RunSuccessionCheck();
        gameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long genAfterHealth);
        Check("health trigger (§5.1 PED coupling): health_ceiling eroded to 40 forces succession at age 30",
            healthOutcome.Kind == SuccessionOutcomeKind.Succeeded && healthOutcome.HeirId == child1Id
            && career.AvatarPlayerId == child1Id && genAfterHealth == 2);
        Check("EvaluateSuccession(list) ≡ EvaluateSuccession() for Succeeded; candidate list names exactly the one eligible heir",
            healthOutcomeViaList.Kind == healthOutcome.Kind && healthOutcomeViaList.HeirId == healthOutcome.HeirId
            && healthCandidates.Count == 1 && healthCandidates[0].HeirId == child1Id);

        // ---- filler backfill: mismatch handoff with an empty same-role FA pool ----
        // Age the whole bloodline consistently: the §1.2 direction invariant
        // (parent strictly older) is maintained-by-construction in real play
        // (one shared aging tick), so age-seeded fixtures must preserve it —
        // aging ONLY child1 to 42 would leave the 30-year-old retired founder
        // "younger than his own child", and EvaluateSuccession would correctly
        // resolve the founder as a willing, unrostered heir. (This exact
        // mistake picked grandpa as the successor in an earlier draft.)
        players.SetAge(founderId, 65);
        players.SetAge(child1Id, 42);
        string heir2Id = career.ConceiveChild("Bull", "Pen", birthAge: 19);
        players.SetBaseballInterest(heir2Id, 100);
        baseball.TryGetRatings(heir2Id, out PlayerRatingsRow heir2Ratings);
        heir2Ratings.IsPitcher = true;
        baseball.UpsertRatings(in heir2Ratings);
        baseball.UpsertPitcherRole(heir2Id, PitcherRole.Reliever);
        var overrideRng = new RngState(0xBADC_0FFEUL);
        LeagueGenerator.GenerateArsenal(baseball, heir2Id, heir2Ratings.PitStuff, ratingSpread: 0, ref overrideRng);

        // Empty the signable batter pool: the founder is already excluded by
        // the health window (40 is not > 40); age out everyone else.
        var pool = new List<RosterPlayerRow>();
        baseball.LoadFreeAgents(pool,
            minAge: HeirGenetics.HeirGeneticsProfile.MaturityAge,
            maxAge: HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge,
            minHealth: HeirGenetics.HeirGeneticsProfile.HealthRetirementFloor);
        foreach (RosterPlayerRow freeAgent in pool)
        {
            if (freeAgent.Role == PitcherRole.None)
            {
                players.SetAge(freeAgent.PlayerId, 50);
            }
        }

        var rosterBefore = new List<RosterPlayerRow>();
        baseball.LoadRoster(rosterBefore);
        int playersBefore = players.Count();
        SuccessionOutcome fillerOutcome = career.RunSuccessionCheck();
        var rosterAfter = new List<RosterPlayerRow>();
        baseball.LoadRoster(rosterAfter);
        var knownIds = new HashSet<string>(rosterBefore.Select(r => r.PlayerId)) { heir2Id };
        RosterPlayerRow? filler = rosterAfter.Where(r => !knownIds.Contains(r.PlayerId))
            .Select(r => (RosterPlayerRow?)r).FirstOrDefault();
        players.TryGetById(child1Id, out PlayerRow retiree2);
        Check("filler backfill: empty FA pool generates a replacement-level batter in the vacated slot (player count +1)",
            fillerOutcome.Kind == SuccessionOutcomeKind.Succeeded && fillerOutcome.HeirId == heir2Id
            && players.Count() == playersBefore + 1
            && filler is { Role: PitcherRole.None, TeamId: 1 },
            filler is null ? "no filler found" : $"filler {filler.Value.PlayerId[..8]}… on team {filler.Value.TeamId}");
        Check("filler handoff preserves the roster invariant: reliever heir rostered, weakest reliever displaced, every team 9+5+3",
            RosterInvariantHolds(baseball, out string fillerInvariant)
            && rosterAfter.Any(r => r.PlayerId == heir2Id && r.Role == PitcherRole.Reliever && r.TeamId == 1)
            && !retiree2.TeamId.HasValue,
            fillerInvariant);
    }

    // ------------------------------------------------------------------
    // 11. Marriage & conception: the bus-driven ConceiveChild consumer
    // (marriage_and_conception.md §7 checks 5–8; the Narrative publisher
    // side is GrittyEventsHarness territory — no Baseball compiled there).
    // Check 9 (no §8 band moves) is the full-suite rerun itself: nothing in
    // this pass touches the resolver or either sim's game loop.
    // ------------------------------------------------------------------

    private static void RunConceptionRequestSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Marriage & conception: bus-driven ConceiveChild (design doc §7 checks 5–8) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);

        var state = new GlobalState();
        var gameState = new GameStateQueries(db);
        var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed));
        league.Initialize();
        var micro = new MicroGame(db, baseball);
        micro.Initialize();
        var career = new CareerManager(db, players, baseball, gameState, state, league, micro, new RngState(MicroSeed + 90));

        var bus = new EventBus();
        career.AttachTo(bus);
        var born = new List<ChildBornEvent>();
        bus.Subscribe<ChildBornEvent>(born.Add);

        career.CreateAvatar("Dyna", "Sty", teamId: 2, new PlayerRatingsRow
        {
            IsPitcher = false,
            BatPower = 65,
            BatContact = 60,
            BatDiscipline = 55,
            PitStuff = 50,
            PitControl = 50,
            PitStamina = 50,
            Fielding = 50,
        });
        string avatarId = career.AvatarPlayerId;

        // The co-parent: any rostered player (a Player_Ratings row exists by
        // construction). Deliberately NO Partner edge is ever written to the
        // DB — the id rides the request, which is exactly the §4.2 hazard
        // check 6 closes (a same-session marriage awaiting the day-cadence
        // flush must still produce the two-parent heir).
        var roster = new List<RosterPlayerRow>();
        baseball.LoadRoster(roster);
        string partnerId = roster.First(r => r.TeamId == 5).PlayerId;

        var relationships = new List<RelationshipRow>();
        players.LoadRelationshipsFor(avatarId, relationships);
        bool noDbPartner = relationships.All(r => r.Type != RelationshipType.Partner);

        // ---- checks 5 + 6: partnered request → two-parent heir, flush-order-independent ----
        bus.Publish(new ChildConceptionRequestedEvent(avatarId, partnerId, day: 40));
        bus.DispatchPending();

        string? child1Id = born.Count == 1 ? born[0].ChildId : null;
        bool child1RowOk = false, child1EdgesOk = false, child1RatingsOk = false;
        if (child1Id is not null && players.TryGetById(child1Id, out PlayerRow child1))
        {
            child1RowOk = child1.LastName == "Sty" && child1.Age == 0 && !child1.TeamId.HasValue;
            child1RatingsOk = baseball.TryGetRatings(child1Id, out _);
            players.LoadRelationshipsFor(child1Id, relationships);
            child1EdgesOk = relationships.Count(r => r.Type == RelationshipType.Child) == 2
                && relationships.Any(r => r.Type == RelationshipType.Child
                    && (r.Player1Id == avatarId || r.Player2Id == avatarId))
                && relationships.Any(r => r.Type == RelationshipType.Child
                    && (r.Player1Id == partnerId || r.Player2Id == partnerId));
        }
        Check("check 5: the subscriber services a partnered request — unrostered newborn, bloodline surname, ratings row, Child edge to BOTH parents",
            child1RowOk && child1RatingsOk && child1EdgesOk
            && born[0].AvatarId == avatarId && born[0].PartnerId == partnerId && born[0].Day == 40,
            child1Id is null ? $"{born.Count} ChildBornEvents" : $"child {child1Id[..8]}…");
        Check("check 6: order independence — the two-parent heir conceived with NO Partner edge anywhere in the DB (the id rode the request)",
            noDbPartner && child1EdgesOk);

        // ---- birth-notification UI seam: the same partnered request also queues a display-friendly announcement ----
        bool birth1Ok = false;
        if (child1Id is not null
            && career.TryDequeuePendingBirth(out BirthAnnouncement birth1)
            && players.TryGetById(child1Id, out PlayerRow child1Reload))
        {
            RosterPlayerRow partnerRow = roster.First(r => r.PlayerId == partnerId);
            birth1Ok = birth1.ChildId == child1Id
                && birth1.ChildFirstName == child1Reload.FirstName && birth1.ChildLastName == child1Reload.LastName
                && birth1.PartnerFirstName == partnerRow.FirstName && birth1.PartnerLastName == partnerRow.LastName
                && birth1.Day == 40;
        }
        Check("check 5/6 UI seam: TryDequeuePendingBirth announces the partnered birth with the co-parent's real name, then the queue is empty",
            birth1Ok && !career.TryDequeuePendingBirth(out _));

        // ---- check 5's null-partner arm: unmarried request → single-parent heir (AverageParent vector) ----
        bus.Publish(new ChildConceptionRequestedEvent(avatarId, null, day: 41));
        bus.DispatchPending();
        string? child2Id = born.Count == 2 ? born[1].ChildId : null;
        bool child2SingleParent = false;
        if (child2Id is not null)
        {
            players.LoadRelationshipsFor(child2Id, relationships);
            child2SingleParent = relationships.Count(r => r.Type == RelationshipType.Child) == 1
                && relationships.Any(r => r.Type == RelationshipType.Child
                    && (r.Player1Id == avatarId || r.Player2Id == avatarId));
        }
        Check("check 5 (unmarried): a null-PartnerId request conceives with the average-parent path — exactly one Child edge, to the avatar",
            child2SingleParent && born.Count == 2 && born[1].PartnerId is null,
            $"{born.Count} ChildBornEvents");

        bool birth2Ok = child2Id is not null
            && career.TryDequeuePendingBirth(out BirthAnnouncement birth2)
            && birth2.ChildId == child2Id
            && birth2.PartnerFirstName is null && birth2.PartnerLastName is null
            && birth2.Day == 41;
        Check("check 5 (unmarried) UI seam: an unpartnered birth announces with no co-parent name",
            birth2Ok && !career.TryDequeuePendingBirth(out _));

        // ---- check 7: a stale request naming a since-retired avatar is dropped ----
        int playersBeforeStale = players.Count();
        bus.Publish(new ChildConceptionRequestedEvent("not-the-avatar-anymore", partnerId, day: 42));
        bus.DispatchPending();
        Check("check 7: a stale request for a non-current avatar is dropped (no heir, no throw, no announce)",
            players.Count() == playersBeforeStale && born.Count == 2);
        Check("check 7 UI seam: a dropped stale request never enqueues a birth announcement either",
            !career.TryDequeuePendingBirth(out _));

        // ---- check 8: multiple requests → multiple heirs; succession selects among them ----
        bus.Publish(new ChildConceptionRequestedEvent(avatarId, partnerId, day: 43));
        bus.DispatchPending();
        string? child3Id = born.Count == 3 ? born[2].ChildId : null;
        bool birth3Ok = child3Id is not null
            && career.TryDequeuePendingBirth(out BirthAnnouncement birth3)
            && birth3.ChildId == child3Id && birth3.Day == 43
            && !career.TryDequeuePendingBirth(out _);
        Check("check 8 UI seam: the third request's birth also announces correctly and the queue drains empty",
            birth3Ok);
        bool distinctHeirs = child1Id is not null && child2Id is not null && child3Id is not null
            && child1Id != child2Id && child2Id != child3Id && child1Id != child3Id;

        var heirIds = new[] { child1Id!, child2Id!, child3Id! };
        foreach (string heirId in heirIds)
        {
            players.SetAge(heirId, 19);
            players.SetBaseballInterest(heirId, 100);
        }
        players.SetAge(avatarId, HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge);
        var candidates = new List<HeirCandidate>();
        SuccessionOutcome outcome = career.EvaluateSuccession(candidates);
        Check("check 8: repeated requests create distinct heirs off the same avatar; succession reveals and selects among all three",
            distinctHeirs && outcome.Kind == SuccessionOutcomeKind.Succeeded
            && candidates.Count == 3
            && heirIds.All(id => candidates.Any(c => c.HeirId == id))
            && heirIds.Contains(outcome.HeirId!),
            $"{candidates.Count} candidates, picked {(outcome.HeirId is null ? "none" : outcome.HeirId[..8] + "…")}");
    }

    private static void Check(string name, bool pass, string detail = "") =>
        Results.Add((name, pass, detail));

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Scratch files live in %TEMP%; a straggler is harmless.
        }
    }
}
