using System.Diagnostics;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.UI.Scouting;

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
        string tierScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_tier_{Guid.NewGuid():N}.db");
        string availScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_avail_{Guid.NewGuid():N}.db");
        string equipScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_equip_{Guid.NewGuid():N}.db");
        string promoScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_promo_{Guid.NewGuid():N}.db");
        string promoStreamScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_promostream_{Guid.NewGuid():N}.db");
        string promoAvatarScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_promoavatar_{Guid.NewGuid():N}.db");
        string devScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_dev_{Guid.NewGuid():N}.db");
        string devStreamScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_devstream_{Guid.NewGuid():N}.db");
        string practiceScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_practice_{Guid.NewGuid():N}.db");
        string devEquilibriumScratchPath = Path.Combine(Path.GetTempPath(), $"dnd_devequilibrium_{Guid.NewGuid():N}.db");

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
            RunTierLadderSuite(schemaPath, tierScratchPath);
            RunAvailabilitySuite(schemaPath, availScratchPath);
            RunEquipmentSuite(schemaPath, equipScratchPath);
            RunPromotionSweepSuite(schemaPath, promoScratchPath);
            RunPromotionStreamSuite(schemaPath, promoStreamScratchPath);
            RunPromotionAvatarSuite(schemaPath, promoAvatarScratchPath);
            RunDevelopmentCurveSuite();
            RunDevelopmentArcSuite(schemaPath, devScratchPath);
            RunDevelopmentStreamSuite(schemaPath, devStreamScratchPath);
            RunPracticeSeamSuite(schemaPath, practiceScratchPath);
            RunDevelopmentEquilibriumSuite(schemaPath, devEquilibriumScratchPath);
            RunScoutingGradeSuite();
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
            foreach (string variant in new[] { tierScratchPath, tierScratchPath + ".ref" })
            {
                TryDelete(variant);
                TryDelete(variant + "-wal");
                TryDelete(variant + "-shm");
            }
            foreach (string variant in new[]
                { availScratchPath, availScratchPath + ".bare", availScratchPath + ".abs", availScratchPath + ".career" })
            {
                TryDelete(variant);
                TryDelete(variant + "-wal");
                TryDelete(variant + "-shm");
            }
            foreach (string variant in new[]
                { equipScratchPath, equipScratchPath + ".bare", equipScratchPath + ".bat", equipScratchPath + ".pit" })
            {
                TryDelete(variant);
                TryDelete(variant + "-wal");
                TryDelete(variant + "-shm");
            }
            foreach (string variant in new[]
                {
                    promoScratchPath, promoScratchPath + ".detA", promoScratchPath + ".detB",
                    promoStreamScratchPath, promoStreamScratchPath + ".bare", promoStreamScratchPath + ".promo",
                    promoAvatarScratchPath,
                    devScratchPath, devScratchPath + ".detA", devScratchPath + ".detB",
                    devStreamScratchPath, devStreamScratchPath + ".bare", devStreamScratchPath + ".dev",
                    practiceScratchPath + ".seam", practiceScratchPath + ".practiced", practiceScratchPath + ".idle",
                    practiceScratchPath + ".guard",
                    devEquilibriumScratchPath,
                })
            {
                TryDelete(variant);
                TryDelete(variant + "-wal");
                TryDelete(variant + "-shm");
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
        Check("scratch schema applies at v10", db.GetSchemaVersion() == 10, $"user_version={db.GetSchemaVersion()}");

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
            db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 9));
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
            db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 10));
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
            var career = new CareerManager(db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 40));

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
            var career = new CareerManager(db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 60));
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
            var career = new CareerManager(db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 61));

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
        var career = new CareerManager(db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 70));

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
        var career = new CareerManager(db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 80));

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
        var career = new CareerManager(db, players, baseball, gameState, state, Solo(league), micro, new RngState(MicroSeed + 90));

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

    // ------------------------------------------------------------------
    // 13. Phase 9a tier ladder: 6-tier world-gen, per-tier environments
    //     (docs/design/tier_league_environments.md §2/§4/§5)
    // ------------------------------------------------------------------

    /// <summary>One tier's §4 acceptance band (design doc tier_league_environments.md).</summary>
    private readonly record struct TierBand(
        double AvgLo, double AvgHi, double ObpLo, double ObpHi, double SlgLo, double SlgHi,
        double KLo, double KHi, double BbLo, double BbHi, double HrLo, double HrHi,
        double RgLo, double RgHi);

    // §4 table verbatim; the MLB row is the shipped Phase 3 §8 band and
    // doubles as the 9a regression guard on the calibrated core.
    private static readonly TierBand[] TierBands =
    {
        new(.296, .316, .379, .399, .493, .523, .177, .207, .109, .129, .034, .042, 6.4, 8.0), // HS
        new(.284, .304, .364, .384, .473, .503, .184, .214, .103, .123, .033, .041, 5.8, 7.1), // College
        new(.272, .292, .348, .368, .453, .483, .190, .220, .097, .117, .032, .040, 5.3, 6.5), // MinorA
        new(.260, .280, .334, .354, .434, .464, .197, .227, .091, .111, .030, .039, 4.9, 5.9), // MinorAA
        new(.248, .268, .319, .339, .415, .445, .203, .233, .085, .105, .029, .037, 4.5, 5.4), // MinorAAA
        new(.240, .260, .308, .325, .395, .430, .200, .250, .075, .105, .027, .037, 4.2, 4.8), // MLB (§8)
    };

    private static void RunTierLadderSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9a tier ladder: 6-tier world, per-tier environments (design doc §2/§4/§5) ---");

        // ---- §5 HS resolver fixture: all-50 roster after the HS shift ----
        TierRatingDeltas hs = TierEffects.For(LeagueTier.HS);
        var hsBatter = new BatterRatings(
            TierEffects.Shift(50, hs.BatPower), TierEffects.Shift(50, hs.BatContact),
            TierEffects.Shift(50, hs.BatDiscipline), pedActive: false);
        var hsPitcher = new PitcherRatings(
            TierEffects.Shift(50, hs.PitStuff), TierEffects.Shift(50, hs.PitControl), 50);
        Span<double> hsProbs = stackalloc double[AtBatResolver.OutcomeCount];
        AtBatResolver.ComputeProbabilities(in hsBatter, in hsPitcher, TierEffects.Shift(50, hs.Defense), hsProbs);
        double[] hsExpected = { 0.41916, 0.19193, 0.11920, 0.17275, 0.05447, 0.00443, 0.03805 };
        bool hsFixtureOk = true;
        for (int o = 0; o < AtBatResolver.OutcomeCount; o++)
        {
            hsFixtureOk &= Math.Abs(hsProbs[o] - hsExpected[o]) <= 6e-5;
        }
        Check("§5 HS fixture: all-50 roster after the HS shift reproduces the doc's 7-outcome vector",
            hsFixtureOk,
            $"p=[{hsProbs[0]:F5} {hsProbs[1]:F5} {hsProbs[2]:F5} {hsProbs[3]:F5} {hsProbs[4]:F5} {hsProbs[5]:F5} {hsProbs[6]:F5}]");

        // ---- §2 MLB contract: the MLB delta vector is exactly zero ----
        TierRatingDeltas mlb = TierEffects.For(LeagueTier.MLB);
        Check("§2 MLB contract: delta vector all-zero, Shift(r, 0) is the identity over 0–100",
            mlb.BatPower == 0 && mlb.BatContact == 0 && mlb.BatDiscipline == 0
            && mlb.PitStuff == 0 && mlb.PitControl == 0 && mlb.Defense == 0
            && Enumerable.Range(0, 101).All(r => TierEffects.Shift(r, 0) == r));

        // ---- 6-tier flat world + a same-seed MLB-only reference world ----
        string referencePath = scratchPath + ".ref";
        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);

        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        var tierGenRng = new RngState(LeagueSeed + 1);
        bool tiersSeeded = LeagueGenerator.EnsureTierLeagues(db, players, baseball, ratingSpread: 0, ref tierGenRng);
        bool secondSkipped = !LeagueGenerator.EnsureTierLeagues(db, players, baseball, ratingSpread: 0, ref tierGenRng);

        bool tierCountsOk = baseball.CountTeams() == 6 * LeagueSimulator.TeamCount;
        var tierRoster = new List<RosterPlayerRow>();
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            tierCountsOk &= baseball.CountTeamsInTier((LeagueTier)t) == LeagueSimulator.TeamCount;
            tierCountsOk &= baseball.LoadRosterByTier((LeagueTier)t, tierRoster)
                == LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam;
        }
        var globalRoster = new List<RosterPlayerRow>();
        Check("EnsureTierLeagues seeds 5 missing tiers once (idempotent): 48 teams, 8 + full 9+5+3 per tier",
            tiersSeeded && secondSkipped && tierCountsOk
            && baseball.LoadRoster(globalRoster) == 6 * LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam,
            $"teams={baseball.CountTeams()} roster={globalRoster.Count}");

        // One season, all six sims on the same bus/clock. The MLB sim's seed is
        // shared with the reference world below, so its season must come out
        // bit-identical — the "9a moved nothing in the calibrated core" guard.
        var state = new GlobalState();
        var bus = new EventBus();
        var gameState = new GameStateQueries(db);
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);
        var normalizer = new StatsNormalizer(db, baseball);
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tierSim = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed + (ulong)t), (LeagueTier)t);
            tierSim.Initialize();
            tierSim.AttachTo(bus);
        }
        for (int i = 0; i < GlobalState.DaysPerSeason; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }
        Check("6-tier season: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);

        // ---- §4 per-tier bands + strictly monotone ladder ----
        Span<double> avgByTier = stackalloc double[LeagueDirectory.TierCount];
        Span<double> rgByTier = stackalloc double[LeagueDirectory.TierCount];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tier = (LeagueTier)t;
            LeagueBattingTotals bat = baseball.LoadLeagueBattingTotals(StartYear, tier);
            LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(StartYear, tier);
            long hits = bat.H;
            long singles = hits - bat.Doubles - bat.Triples - bat.Hr;
            long tb = singles + 2 * bat.Doubles + 3 * bat.Triples + 4 * bat.Hr;
            double avg = (double)hits / bat.Ab;
            double obp = (double)(hits + bat.Bb) / bat.Pa;
            double slg = (double)tb / bat.Ab;
            double kRate = (double)bat.So / bat.Pa;
            double bbRate = (double)bat.Bb / bat.Pa;
            double hrRate = (double)bat.Hr / bat.Pa;
            double runsPerTeamGame = (double)pit.Er / pit.Gs;
            avgByTier[t] = avg;
            rgByTier[t] = runsPerTeamGame;

            Console.WriteLine($"  {tier,-8} {avg:.000}/{obp:.000}/{slg:.000}  K% {kRate:P1}  BB% {bbRate:P1}  " +
                $"HR/PA {hrRate:P1}  R/G {runsPerTeamGame:F2}  ({bat.Pa:N0} PA)");

            TierBand band = TierBands[t];
            Check($"§4 [{tier}] slash line inside the tier band",
                avg >= band.AvgLo && avg <= band.AvgHi && obp >= band.ObpLo && obp <= band.ObpHi
                && slg >= band.SlgLo && slg <= band.SlgHi,
                $"{avg:.000}/{obp:.000}/{slg:.000}");
            Check($"§4 [{tier}] K/BB/HR rates and R/G inside the tier band",
                kRate >= band.KLo && kRate <= band.KHi && bbRate >= band.BbLo && bbRate <= band.BbHi
                && hrRate >= band.HrLo && hrRate <= band.HrHi
                && runsPerTeamGame >= band.RgLo && runsPerTeamGame <= band.RgHi,
                $"K {kRate:P1} BB {bbRate:P1} HR {hrRate:P1} R/G {runsPerTeamGame:F2}");
        }
        bool monotone = true;
        for (int t = 1; t < LeagueDirectory.TierCount; t++)
        {
            monotone &= avgByTier[t] < avgByTier[t - 1] && rgByTier[t] < rgByTier[t - 1];
        }
        Check("tier ladder is strictly monotone HS→MLB on AVG and R/G (legibility contract)", monotone,
            $"AVG {avgByTier[0]:.000}→{avgByTier[5]:.000}  R/G {rgByTier[0]:F2}→{rgByTier[5]:F2}");

        // ---- MLB bit-identity: the same seed in an MLB-only world ----
        using var refDb = new DatabaseManager(referencePath);
        refDb.InitializeSchema(schemaPath);
        var refPlayers = new PlayerQueries(refDb);
        var refBaseball = new BaseballQueries(refDb);
        var refGenRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(refDb, refPlayers, refBaseball, ratingSpread: 0, ref refGenRng);
        var refState = new GlobalState();
        var refBus = new EventBus();
        var refClock = new TimeManager(refDb, new GameStateQueries(refDb), refState, refBus);
        refClock.Initialize(StartYear);
        var refLeague = new LeagueSimulator(
            refDb, refBaseball, new StatsNormalizer(refDb, refBaseball),
            new RngState(SeasonSeed + (ulong)LeagueTier.MLB));
        refLeague.Initialize();
        refLeague.AttachTo(refBus);
        for (int i = 0; i < GlobalState.DaysPerSeason; i++)
        {
            refClock.AdvanceDay();
            refBus.DispatchPending();
        }
        LeagueBattingTotals mlbBat = baseball.LoadLeagueBattingTotals(StartYear, LeagueTier.MLB);
        LeaguePitchingTotals mlbPit = baseball.LoadLeaguePitchingTotals(StartYear, LeagueTier.MLB);
        LeagueBattingTotals refBat = refBaseball.LoadLeagueBattingTotals(StartYear);
        LeaguePitchingTotals refPit = refBaseball.LoadLeaguePitchingTotals(StartYear);
        Check("MLB regression guard: the 6-tier world's MLB season is BIT-IDENTICAL to a same-seed MLB-only world",
            mlbBat.Pa == refBat.Pa && mlbBat.Ab == refBat.Ab && mlbBat.H == refBat.H
            && mlbBat.Doubles == refBat.Doubles && mlbBat.Triples == refBat.Triples && mlbBat.Hr == refBat.Hr
            && mlbBat.Bb == refBat.Bb && mlbBat.So == refBat.So && mlbBat.Rbi == refBat.Rbi
            && mlbPit.G == refPit.G && mlbPit.Gs == refPit.Gs && mlbPit.W == refPit.W && mlbPit.L == refPit.L
            && mlbPit.OutsRecorded == refPit.OutsRecorded && mlbPit.HAllowed == refPit.HAllowed
            && mlbPit.Er == refPit.Er && mlbPit.Bb == refPit.Bb && mlbPit.So == refPit.So,
            $"PA {mlbBat.Pa}/{refBat.Pa} H {mlbBat.H}/{refBat.H} ER {mlbPit.Er}/{refPit.Er}");
    }

    // ------------------------------------------------------------------
    // Phase 8c: roster availability (arrest / injury / suspension)
    // ------------------------------------------------------------------

    private static void RunAvailabilitySuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 8c roster availability: ledger semantics, replacement shadowing, career benching ---");

        // ---- pure ledger semantics (no DB) ----
        var probeLedger = new AvailabilityLedger();
        var probeBus = new EventBus();
        probeLedger.AttachTo(probeBus);
        probeBus.Publish(new PlayerAbsenceChangedEvent("p1", (byte)AbsenceReason.Suspension, untilDay: 110, 0, 0));
        probeBus.DispatchPending();
        Check("ledger day boundaries: absent through until_day−1, available ON until_day",
            probeLedger.StateFor("p1", 100) == SlotAvailability.Absent
            && probeLedger.StateFor("p1", 109) == SlotAvailability.Absent
            && probeLedger.StateFor("p1", 110) == SlotAvailability.Available
            && probeLedger.StateFor("nobody", 100) == SlotAvailability.Available);

        int versionBefore = probeLedger.Version;
        probeBus.Publish(new PlayerAbsenceChangedEvent("p1", (byte)AbsenceReason.Arrest, 105, 0, 0));
        probeBus.DispatchPending();
        bool shorterIgnored = probeLedger.Version == versionBefore
            && probeLedger.TryGet("p1", out AbsenceEntry kept)
            && kept.Reason == AbsenceReason.Suspension && kept.UntilDay == 110;
        probeBus.Publish(new PlayerAbsenceChangedEvent("p1", (byte)AbsenceReason.Injury, 120, 8, 130));
        probeBus.DispatchPending();
        Check("keep-later merge (shorter overlap ignored, longer replaces) + injury rust window [until, penaltyUntil)",
            shorterIgnored && probeLedger.Version != versionBefore
            && probeLedger.StateFor("p1", 119) == SlotAvailability.Absent
            && probeLedger.StateFor("p1", 120) == SlotAvailability.Rusty
            && probeLedger.StateFor("p1", 129) == SlotAvailability.Rusty
            && probeLedger.StateFor("p1", 130) == SlotAvailability.Available);

        var rustSource = new BatterRatings(60, 60, 60, pedActive: true);
        BatterRatings rusted = LeagueSimulator.ApplyRust(in rustSource, 12);
        Check("rust dock arithmetic: −rust on every batter rating, PED flag preserved, rust 0 = identity",
            rusted.Power == 48 && rusted.Contact == 48 && rusted.Discipline == 48 && rusted.PedActive
            && LeagueSimulator.ApplyRust(in rustSource, 0).Power == 60);

        // ---- bit-identity: an ATTACHED but empty ledger changes nothing ----
        string barePath = scratchPath + ".bare";
        LeagueBattingTotals ledgeredBat;
        LeaguePitchingTotals ledgeredPit;
        using (var db = new DatabaseManager(scratchPath))
        {
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
            var state = new GlobalState();
            var bus = new EventBus();
            var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
            clock.Initialize(StartYear);
            var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed + 200));
            league.Initialize();
            league.Availability = new AvailabilityLedger(); // attached-and-empty — must be inert
            league.AttachTo(bus);
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            ledgeredBat = baseball.LoadLeagueBattingTotals(StartYear);
            ledgeredPit = baseball.LoadLeaguePitchingTotals(StartYear);
        }
        using (var bareDb = new DatabaseManager(barePath))
        {
            bareDb.InitializeSchema(schemaPath);
            var barePlayers = new PlayerQueries(bareDb);
            var bareBaseball = new BaseballQueries(bareDb);
            var bareGenRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(bareDb, barePlayers, bareBaseball, ratingSpread: 0, ref bareGenRng);
            var bareState = new GlobalState();
            var bareBus = new EventBus();
            var bareClock = new TimeManager(bareDb, new GameStateQueries(bareDb), bareState, bareBus);
            bareClock.Initialize(StartYear);
            var bareLeague = new LeagueSimulator(
                bareDb, bareBaseball, new StatsNormalizer(bareDb, bareBaseball), new RngState(SeasonSeed + 200));
            bareLeague.Initialize();
            bareLeague.AttachTo(bareBus); // no ledger at all — the pre-8c shape
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                bareClock.AdvanceDay();
                bareBus.DispatchPending();
            }
            LeagueBattingTotals bareBat = bareBaseball.LoadLeagueBattingTotals(StartYear);
            LeaguePitchingTotals barePit = bareBaseball.LoadLeaguePitchingTotals(StartYear);
            Check("empty-ledger season is BIT-IDENTICAL to a no-ledger season (the pre-8c guarantee)",
                ledgeredBat.Pa == bareBat.Pa && ledgeredBat.Ab == bareBat.Ab && ledgeredBat.H == bareBat.H
                && ledgeredBat.Doubles == bareBat.Doubles && ledgeredBat.Triples == bareBat.Triples
                && ledgeredBat.Hr == bareBat.Hr && ledgeredBat.Bb == bareBat.Bb && ledgeredBat.So == bareBat.So
                && ledgeredBat.Rbi == bareBat.Rbi
                && ledgeredPit.G == barePit.G && ledgeredPit.Gs == barePit.Gs
                && ledgeredPit.W == barePit.W && ledgeredPit.L == barePit.L
                && ledgeredPit.OutsRecorded == barePit.OutsRecorded && ledgeredPit.HAllowed == barePit.HAllowed
                && ledgeredPit.Er == barePit.Er && ledgeredPit.Bb == barePit.Bb && ledgeredPit.So == barePit.So,
                $"PA {ledgeredBat.Pa}/{bareBat.Pa} H {ledgeredBat.H}/{bareBat.H} ER {ledgeredPit.Er}/{barePit.Er}");
        }

        // ---- macro shadowing: a suspension costs exactly the absent window ----
        string absencePath = scratchPath + ".abs";
        using (var absDb = new DatabaseManager(absencePath))
        {
            absDb.InitializeSchema(schemaPath);
            var absPlayers = new PlayerQueries(absDb);
            var absBaseball = new BaseballQueries(absDb);
            var absGenRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(absDb, absPlayers, absBaseball, ratingSpread: 0, ref absGenRng);

            // ---- DB round-trip while the world is fresh: SQL keep-later + hydration ----
            var roster = new List<RosterPlayerRow>();
            absBaseball.LoadRoster(roster);
            string dbProbeId = roster[0].PlayerId;
            absPlayers.SetAbsence(dbProbeId, AbsenceReason.Suspension, 110, 0, 0);
            absPlayers.SetAbsence(dbProbeId, AbsenceReason.Arrest, 105, 0, 0); // shorter — SQL no-op
            bool sqlKeepLater = absPlayers.TryGetAbsence(dbProbeId, out PlayerAbsenceRow keptRow)
                && keptRow.Reason == AbsenceReason.Suspension && keptRow.UntilDay == 110;
            absPlayers.SetAbsence(dbProbeId, AbsenceReason.Injury, 120, 8, 130); // longer — replaces
            var hydrationRows = new List<PlayerAbsenceRow>();
            absPlayers.LoadActiveAbsences(100, hydrationRows);
            var hydrated = new AvailabilityLedger();
            hydrated.Seed(hydrationRows);
            var inertRows = new List<PlayerAbsenceRow>();
            Check("SQL keep-later upsert + LoadActiveAbsences→Seed hydration reproduce the row exactly",
                sqlKeepLater
                && hydrationRows.Count == 1
                && hydrated.StateFor(dbProbeId, 119) == SlotAvailability.Absent
                && hydrated.StateFor(dbProbeId, 125) == SlotAvailability.Rusty
                && hydrated.StateFor(dbProbeId, 130) == SlotAvailability.Available
                && absPlayers.LoadActiveAbsences(130, inertRows) == 0);

            // Targets: a lineup batter on the first team, a rotation starter on
            // the second — both suspended for days 15–21 (one full flush cycle).
            string batterId = roster.First(r => r.TeamId == roster[0].TeamId && !r.IsPitcher).PlayerId;
            string teammateId = roster.First(r => r.TeamId == roster[0].TeamId && !r.IsPitcher && r.PlayerId != batterId).PlayerId;
            int secondTeamId = roster.First(r => r.TeamId != roster[0].TeamId).TeamId;
            string starterId = roster.First(r => r.TeamId == secondTeamId && r.Role == PitcherRole.Starter).PlayerId;

            var absState = new GlobalState();
            var absBus = new EventBus();
            var absClock = new TimeManager(absDb, new GameStateQueries(absDb), absState, absBus);
            absClock.Initialize(StartYear);
            var absLedger = new AvailabilityLedger();
            absLedger.AttachTo(absBus);
            var absLeague = new LeagueSimulator(
                absDb, absBaseball, new StatsNormalizer(absDb, absBaseball), new RngState(SeasonSeed + 300));
            absLeague.Initialize();
            absLeague.Availability = absLedger;
            absLeague.AttachTo(absBus);

            // Days 2..14 (the known seed-day artifact: day 1 fires no event).
            for (long day = absState.CurrentDay; day < 14; day = absState.CurrentDay)
            {
                absClock.AdvanceDay();
                absBus.DispatchPending();
            }
            absLeague.FlushPending();
            int batterPa0 = SeasonPa(absPlayers, batterId);
            int teammatePa0 = SeasonPa(absPlayers, teammateId);
            int starterG0 = SeasonG(absPlayers, starterId);
            Check("pre-absence baseline: both targets have been playing", batterPa0 > 0 && starterG0 > 0,
                $"batter PA {batterPa0}, starter G {starterG0}");

            // Suspend both for days 15..21 (until_day 22), published on the
            // same bus the day events ride — FIFO puts it ahead of day 15.
            absBus.Publish(new PlayerAbsenceChangedEvent(batterId, (byte)AbsenceReason.Suspension, 22, 0, 0));
            absBus.Publish(new PlayerAbsenceChangedEvent(starterId, (byte)AbsenceReason.Suspension, 22, 0, 0));
            for (int i = 0; i < 7; i++)
            {
                absClock.AdvanceDay();
                absBus.DispatchPending();
            }
            absLeague.FlushPending();
            int batterPa1 = SeasonPa(absPlayers, batterId);
            int teammatePa1 = SeasonPa(absPlayers, teammateId);
            int starterG1 = SeasonG(absPlayers, starterId);
            Check("suspended window: batter PA and starter G frozen while the teammate keeps accruing",
                batterPa1 == batterPa0 && starterG1 == starterG0 && teammatePa1 > teammatePa0,
                $"batter PA {batterPa0}→{batterPa1}, starter G {starterG0}→{starterG1}, teammate PA {teammatePa0}→{teammatePa1}");

            // Days 22..28 — both are back.
            for (int i = 0; i < 7; i++)
            {
                absClock.AdvanceDay();
                absBus.DispatchPending();
            }
            absLeague.FlushPending();
            Check("post-absence window: both targets play again",
                SeasonPa(absPlayers, batterId) > batterPa1 && SeasonG(absPlayers, starterId) > starterG1,
                $"batter PA {batterPa1}→{SeasonPa(absPlayers, batterId)}, starter G {starterG1}→{SeasonG(absPlayers, starterId)}");
            Check("absence season: integrity ok, no FK violations, no open batch",
                !absDb.IsBatchActive && absDb.RunIntegrityCheck() == "ok" && absDb.RunForeignKeyCheck() == 0);
        }

        // ---- career benching: an absent avatar's game auto-resolves, stats freeze ----
        string careerPath = scratchPath + ".career";
        using var carDb = new DatabaseManager(careerPath);
        carDb.InitializeSchema(schemaPath);
        var carPlayers = new PlayerQueries(carDb);
        var carBaseball = new BaseballQueries(carDb);
        var carGenRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(carDb, carPlayers, carBaseball, ratingSpread: 0, ref carGenRng);
        var carState = new GlobalState();
        var carBus = new EventBus();
        var carGameState = new GameStateQueries(carDb);
        var carClock = new TimeManager(carDb, carGameState, carState, carBus);
        carClock.Initialize(StartYear);
        var carLedger = new AvailabilityLedger();
        carLedger.AttachTo(carBus);
        var carLeague = new LeagueSimulator(
            carDb, carBaseball, new StatsNormalizer(carDb, carBaseball), new RngState(SeasonSeed + 400));
        carLeague.Initialize();
        carLeague.Availability = carLedger;
        carLeague.AttachTo(carBus);
        var carMicro = new MicroGame(carDb, carBaseball);
        carMicro.Initialize();
        carMicro.Availability = carLedger;
        carMicro.LoggingEnabled = false;
        var carCareer = new CareerManager(
            carDb, carPlayers, carBaseball, carGameState, carState, Solo(carLeague), carMicro, new RngState(MicroSeed + 100));
        carCareer.Availability = carLedger;
        carCareer.AttachTo(carBus);
        carCareer.CreateAvatar("Benched", "Rookie", teamId: 3, new PlayerRatingsRow
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

        for (int i = 0; i < 5; i++)
        {
            carClock.AdvanceDay();
            carBus.DispatchPending();
        }
        int avatarPa0 = SeasonPa(carPlayers, carCareer.AvatarPlayerId);
        Check("avatar baseline: attended autopilot games accruing", avatarPa0 > 0, $"PA {avatarPa0}");

        // Suspend the avatar for the next 3 days, then flip to interactive
        // mode — the benched days must STILL resolve straight through the
        // autopilot (no pending game the player isn't allowed to play).
        carBus.Publish(new PlayerAbsenceChangedEvent(
            carCareer.AvatarPlayerId, (byte)AbsenceReason.Suspension, carState.CurrentDay + 4, 0, 0));
        carCareer.AutopilotAttendedGames = false;
        LeagueBattingTotals beforeBat = carBaseball.LoadLeagueBattingTotals(StartYear);
        bool neverPending = true;
        bool absentReported = true;
        for (int i = 0; i < 3; i++)
        {
            carClock.AdvanceDay();
            carBus.DispatchPending();
            neverPending &= !carCareer.HasPendingGame;
            absentReported &= carCareer.IsAvatarAbsentOn(carState.CurrentDay);
        }
        int avatarPa1 = SeasonPa(carPlayers, carCareer.AvatarPlayerId);
        Check("benched avatar in interactive mode: every game auto-resolves, avatar PA frozen, absence reported",
            neverPending && absentReported && avatarPa1 == avatarPa0,
            $"PA {avatarPa0}→{avatarPa1}");
        LeagueBattingTotals duringBat = carBaseball.LoadLeagueBattingTotals(StartYear);
        Check("the team's games still happened while the avatar sat (attended-game flushes kept landing)",
            duringBat.Pa > beforeBat.Pa, $"league PA {beforeBat.Pa}→{duringBat.Pa}");

        carClock.AdvanceDay();
        carBus.DispatchPending();
        bool backPending = carCareer.HasPendingGame && !carCareer.IsAvatarAbsentOn(carState.CurrentDay);
        var neutral = new NeutralBatterPolicy();
        if (carCareer.HasPendingGame)
        {
            carCareer.PlayPendingGame(ref neutral);
        }
        Check("suspension over: the next day parks a pending interactive game again and the avatar plays",
            backPending && SeasonPa(carPlayers, carCareer.AvatarPlayerId) > avatarPa1,
            $"PA {avatarPa1}→{SeasonPa(carPlayers, carCareer.AvatarPlayerId)}");
        Check("career benching world: integrity ok, no FK violations, no open batch",
            !carDb.IsBatchActive && carDb.RunIntegrityCheck() == "ok" && carDb.RunForeignKeyCheck() == 0);
    }

    // ------------------------------------------------------------------
    // Phase 8e: equipment quality (docs/design/equipment_quality.md §8)
    // ------------------------------------------------------------------

    private static void RunEquipmentSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 8e equipment quality: boost arithmetic, ledger parity, league direction ---");

        // ---- §2 boost table + applier arithmetic (no DB) ----
        Check("boost table pinned: quality 0/1/2/3 → +0/+2/+4/+6 (quality 0 all-zero by contract)",
            EquipmentEffects.BoostFor(0) == 0 && EquipmentEffects.BoostFor(1) == 2
            && EquipmentEffects.BoostFor(2) == 4 && EquipmentEffects.BoostFor(3) == 6);

        var gearBatter = new BatterRatings(60, 60, 60, pedActive: true);
        BatterRatings boosted = EquipmentEffects.Batter(in gearBatter, 6);
        BatterRatings identityB = EquipmentEffects.Batter(in gearBatter, 0);
        Check("batter boost: +6 Power/Contact, Discipline and PED untouched, boost 0 = identity",
            boosted.Power == 66 && boosted.Contact == 66 && boosted.Discipline == 60 && boosted.PedActive
            && identityB.Power == 60 && identityB.Contact == 60 && identityB.Discipline == 60 && identityB.PedActive);

        var gearPitcher = new PitcherRatings(60, 60, 55);
        PitcherRatings boostedP = EquipmentEffects.Pitcher(in gearPitcher, 4);
        PitcherRatings identityP = EquipmentEffects.Pitcher(in gearPitcher, 0);
        Check("pitcher boost: +4 Stuff/Control, Stamina untouched, boost 0 = identity",
            boostedP.Stuff == 64 && boostedP.Control == 64 && boostedP.Stamina == 55
            && identityP.Stuff == 60 && identityP.Control == 60 && identityP.Stamina == 55);

        // §5 order pinned at the clamp: gear applies to the baked value FIRST,
        // rust docks the boosted package. 97 +6 clamps to 100, then −8 → 92 —
        // the reversed order (97−8+6 = 95) would differ, so this pins it.
        var nearCap = new BatterRatings(97, 50, 50, pedActive: false);
        BatterRatings gearThenRust = LeagueSimulator.ApplyRust(EquipmentEffects.Batter(in nearCap, 6), 8);
        Check("gear-then-rust order pinned at the 100-clamp (97 → 100 → 92, not 95)",
            gearThenRust.Power == 92 && gearThenRust.Contact == 48 && gearThenRust.Discipline == 42);

        // ---- ledger merge = the SQL keep-higher rule (no DB) ----
        var probeLedger = new EquipmentLedger();
        var probeBus = new EventBus();
        probeLedger.AttachTo(probeBus);
        probeBus.Publish(new PlayerEquipmentChangedEvent("p1", 2));
        probeBus.DispatchPending();
        int versionAfterFirst = probeLedger.Version;
        probeBus.Publish(new PlayerEquipmentChangedEvent("p1", 1)); // downgrade — no-op
        probeBus.Publish(new PlayerEquipmentChangedEvent("p1", 2)); // same — no-op
        probeBus.Publish(new PlayerEquipmentChangedEvent("p2", 0)); // zero — never tracked
        probeBus.DispatchPending();
        bool noOpsIgnored = probeLedger.Version == versionAfterFirst
            && probeLedger.QualityFor("p1") == 2 && probeLedger.QualityFor("p2") == 0
            && probeLedger.QualityFor("nobody") == 0;
        probeBus.Publish(new PlayerEquipmentChangedEvent("p1", 3)); // upgrade — replaces
        probeBus.DispatchPending();
        Check("keep-higher merge: downgrade/same/zero are version-silent no-ops, upgrade replaces",
            noOpsIgnored && probeLedger.Version != versionAfterFirst && probeLedger.QualityFor("p1") == 3
            && probeLedger.Count == 1);

        // ---- bit-identity: an ATTACHED but empty ledger changes nothing ----
        string barePath = scratchPath + ".bare";
        LeagueBattingTotals ledgeredBat;
        LeaguePitchingTotals ledgeredPit;
        using (var db = new DatabaseManager(scratchPath))
        {
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
            var state = new GlobalState();
            var bus = new EventBus();
            var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
            clock.Initialize(StartYear);
            var league = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed + 500));
            league.Initialize();
            league.Equipment = new EquipmentLedger(); // attached-and-empty — must be inert
            league.AttachTo(bus);
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            ledgeredBat = baseball.LoadLeagueBattingTotals(StartYear);
            ledgeredPit = baseball.LoadLeaguePitchingTotals(StartYear);
        }

        LeagueBattingTotals bareBat;
        LeaguePitchingTotals barePit;
        using (var bareDb = new DatabaseManager(barePath))
        {
            bareDb.InitializeSchema(schemaPath);
            var barePlayers = new PlayerQueries(bareDb);
            var bareBaseball = new BaseballQueries(bareDb);
            var bareGenRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(bareDb, barePlayers, bareBaseball, ratingSpread: 0, ref bareGenRng);
            var bareState = new GlobalState();
            var bareBus = new EventBus();
            var bareClock = new TimeManager(bareDb, new GameStateQueries(bareDb), bareState, bareBus);
            bareClock.Initialize(StartYear);
            var bareLeague = new LeagueSimulator(
                bareDb, bareBaseball, new StatsNormalizer(bareDb, bareBaseball), new RngState(SeasonSeed + 500));
            bareLeague.Initialize();
            bareLeague.AttachTo(bareBus); // no ledger at all — the pre-8e shape
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                bareClock.AdvanceDay();
                bareBus.DispatchPending();
            }
            bareBat = bareBaseball.LoadLeagueBattingTotals(StartYear);
            barePit = bareBaseball.LoadLeaguePitchingTotals(StartYear);
            Check("empty-ledger season is BIT-IDENTICAL to a no-ledger season (the pre-8e guarantee)",
                ledgeredBat.Pa == bareBat.Pa && ledgeredBat.Ab == bareBat.Ab && ledgeredBat.H == bareBat.H
                && ledgeredBat.Doubles == bareBat.Doubles && ledgeredBat.Triples == bareBat.Triples
                && ledgeredBat.Hr == bareBat.Hr && ledgeredBat.Bb == bareBat.Bb && ledgeredBat.So == bareBat.So
                && ledgeredBat.Rbi == bareBat.Rbi
                && ledgeredPit.G == barePit.G && ledgeredPit.Gs == barePit.Gs
                && ledgeredPit.W == barePit.W && ledgeredPit.L == barePit.L
                && ledgeredPit.OutsRecorded == barePit.OutsRecorded && ledgeredPit.HAllowed == barePit.HAllowed
                && ledgeredPit.Er == barePit.Er && ledgeredPit.Bb == barePit.Bb && ledgeredPit.So == barePit.So,
                $"PA {ledgeredBat.Pa}/{bareBat.Pa} H {ledgeredBat.H}/{bareBat.H} ER {ledgeredPit.Er}/{barePit.Er}");

            // ---- micro consistency on the bare world (never flushed) ----
            var microNone = new MicroGame(bareDb, bareBaseball);
            microNone.Initialize();
            microNone.LoggingEnabled = false;
            var neutralA = new NeutralBatterPolicy();
            var microRngA = new RngState(MicroSeed + 500);
            MicroGameResult resultNone = microNone.PlayGame(1, 2, MicroGame.NoHuman, ref neutralA, ref microRngA);

            var microEmpty = new MicroGame(bareDb, bareBaseball);
            microEmpty.Initialize();
            microEmpty.LoggingEnabled = false;
            microEmpty.Equipment = new EquipmentLedger();
            var neutralB = new NeutralBatterPolicy();
            var microRngB = new RngState(MicroSeed + 500);
            MicroGameResult resultEmpty = microEmpty.PlayGame(1, 2, MicroGame.NoHuman, ref neutralB, ref microRngB);
            Check("micro: attached-empty ledger game is bit-identical to a no-ledger game",
                resultNone.HomeScore == resultEmpty.HomeScore && resultNone.AwayScore == resultEmpty.AwayScore
                && resultNone.Innings == resultEmpty.Innings
                && resultNone.HomeStarterPitches == resultEmpty.HomeStarterPitches
                && resultNone.AwayStarterPitches == resultEmpty.AwayStarterPitches,
                $"{resultNone.HomeScore}-{resultNone.AwayScore}/{resultEmpty.HomeScore}-{resultEmpty.AwayScore}");

            var microGeared = new MicroGame(bareDb, bareBaseball);
            microGeared.Initialize();
            microGeared.LoggingEnabled = false;
            var gearedLedger = new EquipmentLedger();
            var gearedBus = new EventBus();
            gearedLedger.AttachTo(gearedBus);
            var bareRoster = new List<RosterPlayerRow>();
            bareBaseball.LoadRoster(bareRoster);
            foreach (RosterPlayerRow row in bareRoster)
            {
                if (!row.IsPitcher && row.TeamId == 1)
                {
                    gearedBus.Publish(new PlayerEquipmentChangedEvent(row.PlayerId, 3));
                }
            }
            gearedBus.DispatchPending();
            microGeared.Equipment = gearedLedger;
            var neutralC = new NeutralBatterPolicy();
            var microRngC = new RngState(MicroSeed + 500);
            MicroGameResult resultGeared = microGeared.PlayGame(1, 2, MicroGame.NoHuman, ref neutralC, ref microRngC);
            Check("micro: a geared home lineup diverges a same-seed game (the boost is live in the micro path)",
                resultGeared.HomeScore != resultNone.HomeScore || resultGeared.AwayScore != resultNone.AwayScore
                || resultGeared.Innings != resultNone.Innings
                || resultGeared.HomeStarterPitches != resultNone.HomeStarterPitches
                || resultGeared.AwayStarterPitches != resultNone.AwayStarterPitches,
                $"none {resultNone.HomeScore}-{resultNone.AwayScore}, geared {resultGeared.HomeScore}-{resultGeared.AwayScore}");
        }

        // ---- all-batters-geared season: league offense rises ----
        string batPath = scratchPath + ".bat";
        using (var batDb = new DatabaseManager(batPath))
        {
            batDb.InitializeSchema(schemaPath);
            var batPlayers = new PlayerQueries(batDb);
            var batBaseball = new BaseballQueries(batDb);
            var batGenRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(batDb, batPlayers, batBaseball, ratingSpread: 0, ref batGenRng);

            // ---- SQL keep-higher upsert + hydration round-trip while fresh ----
            var roster = new List<RosterPlayerRow>();
            batBaseball.LoadRoster(roster);
            string probeId = roster[0].PlayerId;
            batPlayers.SetEquipment(probeId, 1, purchasedDay: 10);
            batPlayers.SetEquipment(probeId, 3, purchasedDay: 20);
            batPlayers.SetEquipment(probeId, 2, purchasedDay: 30); // downgrade — SQL no-op
            var equipmentRows = new List<PlayerEquipmentRow>();
            bool sqlKeepHigher = batPlayers.LoadAllEquipment(equipmentRows) == 1
                && equipmentRows[0].Quality == 3 && equipmentRows[0].PurchasedDay == 20;
            var hydrated = new EquipmentLedger();
            hydrated.Seed(equipmentRows);
            bool rejectsSentinel;
            try
            {
                batPlayers.SetEquipment(probeId, 0, purchasedDay: 1);
                rejectsSentinel = false;
            }
            catch (ArgumentOutOfRangeException)
            {
                rejectsSentinel = true;
            }
            Check("SQL keep-higher upsert + LoadAllEquipment→Seed hydration reproduce the row exactly; quality 0 write rejected",
                sqlKeepHigher && hydrated.QualityFor(probeId) == 3 && rejectsSentinel);
            batDb.ExecuteNonQuery(batDb.GetPooledCommand("DELETE FROM Player_Equipment;")); // clean slate for the geared season below

            var batState = new GlobalState();
            var batBus = new EventBus();
            var batClock = new TimeManager(batDb, new GameStateQueries(batDb), batState, batBus);
            batClock.Initialize(StartYear);
            var batLedger = new EquipmentLedger();
            batLedger.AttachTo(batBus);
            foreach (RosterPlayerRow row in roster)
            {
                if (!row.IsPitcher)
                {
                    batBus.Publish(new PlayerEquipmentChangedEvent(row.PlayerId, 3));
                }
            }
            batBus.DispatchPending();
            var batLeague = new LeagueSimulator(
                batDb, batBaseball, new StatsNormalizer(batDb, batBaseball), new RngState(SeasonSeed + 500));
            batLeague.Initialize();
            batLeague.Equipment = batLedger;
            batLeague.AttachTo(batBus);
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                batClock.AdvanceDay();
                batBus.DispatchPending();
            }
            LeagueBattingTotals gearedBat = batBaseball.LoadLeagueBattingTotals(StartYear);
            double bareAvg = (double)bareBat.H / bareBat.Ab;
            double gearedAvg = (double)gearedBat.H / gearedBat.Ab;
            Check("all-batters-geared (q3) season lifts league AVG and HR vs the same-seed bare world",
                gearedAvg > bareAvg + 0.002 && gearedBat.Hr > bareBat.Hr,
                $"AVG {bareAvg:F3}→{gearedAvg:F3}, HR {bareBat.Hr}→{gearedBat.Hr}");

            // ---- zero-alloc: a warm GEARED game day stays flat (the non-fast
            // PA path is pure struct math). Season totals were captured above;
            // these extra in-memory days are never flushed.
            for (int day = 1; day <= 7; day++)
            {
                batLeague.SimulateGameDay(day);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int day = 8; day <= 27; day++)
            {
                batLeague.SimulateGameDay(day);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("warm geared game day allocates zero bytes (20 days / 80 games)", allocated == 0, $"{allocated} B");
            Check("geared season world: integrity ok, no FK violations, no open batch",
                !batDb.IsBatchActive && batDb.RunIntegrityCheck() == "ok" && batDb.RunForeignKeyCheck() == 0);
        }

        // ---- all-pitchers-geared season: league offense falls, strikeouts rise ----
        string pitPath = scratchPath + ".pit";
        using var pitDb = new DatabaseManager(pitPath);
        pitDb.InitializeSchema(schemaPath);
        var pitPlayers = new PlayerQueries(pitDb);
        var pitBaseball = new BaseballQueries(pitDb);
        var pitGenRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(pitDb, pitPlayers, pitBaseball, ratingSpread: 0, ref pitGenRng);
        var pitState = new GlobalState();
        var pitBus = new EventBus();
        var pitClock = new TimeManager(pitDb, new GameStateQueries(pitDb), pitState, pitBus);
        pitClock.Initialize(StartYear);
        var pitLedger = new EquipmentLedger();
        pitLedger.AttachTo(pitBus);
        var pitRoster = new List<RosterPlayerRow>();
        pitBaseball.LoadRoster(pitRoster);
        foreach (RosterPlayerRow row in pitRoster)
        {
            if (row.IsPitcher)
            {
                pitBus.Publish(new PlayerEquipmentChangedEvent(row.PlayerId, 3));
            }
        }
        pitBus.DispatchPending();
        var pitLeague = new LeagueSimulator(
            pitDb, pitBaseball, new StatsNormalizer(pitDb, pitBaseball), new RngState(SeasonSeed + 500));
        pitLeague.Initialize();
        pitLeague.Equipment = pitLedger;
        pitLeague.AttachTo(pitBus);
        for (int i = 0; i < GlobalState.DaysPerSeason; i++)
        {
            pitClock.AdvanceDay();
            pitBus.DispatchPending();
        }
        LeagueBattingTotals gearedPitBat = pitBaseball.LoadLeagueBattingTotals(StartYear);
        double bareAvg2 = (double)bareBat.H / bareBat.Ab;
        double pitAvg = (double)gearedPitBat.H / gearedPitBat.Ab;
        Check("all-pitchers-geared (q3) season cuts league AVG and raises strikeouts vs the same-seed bare world",
            pitAvg < bareAvg2 - 0.002 && gearedPitBat.So > bareBat.So,
            $"AVG {bareAvg2:F3}→{pitAvg:F3}, SO {bareBat.So}→{gearedPitBat.So}");
        Check("pitcher-geared season world: integrity ok, no FK violations, no open batch",
            !pitDb.IsBatchActive && pitDb.RunIntegrityCheck() == "ok" && pitDb.RunForeignKeyCheck() == 0);
    }

    // ------------------------------------------------------------------
    // Phase 9c: promotion & advancement gates (promotion doc §2/§3/§10)
    // ------------------------------------------------------------------

    private static void RunPromotionSweepSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9c promotion gates: score math, conservation sweep, cream rises (doc §2/§3/§10) ---");

        // ---- §2.1 performance shrinkage (acceptance check 5) ----
        var fluke = new SeasonBattingLine { PlayerId = "fluke", Pa = 3, Ab = 2, H = 2, Doubles = 1, Bb = 1 };
        var star = new SeasonBattingLine { PlayerId = "star", Pa = 600, Ab = 520, H = 160, Doubles = 30, Hr = 25, Bb = 70 };
        var solid = new SeasonBattingLine { PlayerId = "solid", Pa = 600, Ab = 540, H = 130, Doubles = 22, Hr = 12, Bb = 55 };
        const double FixtureLeagueOps = 0.700;
        double flukeP = PromotionScore.BatterPerformance(in fluke, FixtureLeagueOps);
        double starP = PromotionScore.BatterPerformance(in star, FixtureLeagueOps);
        double solidP = PromotionScore.BatterPerformance(in solid, FixtureLeagueOps);
        Check("§2.1 shrinkage: a 3-PA 2.500-OPS fluke shrinks to ~league-average and never out-ranks a full season",
            starP > flukeP && flukeP < 105.0 && starP > solidP && starP > 115.0,
            $"fluke {flukeP:F1} solid {solidP:F1} star {starP:F1}");

        const double FixtureLeagueEra = 4.30;
        double aceP = PromotionScore.PitcherPerformance(600, 60, FixtureLeagueEra);  // 200 IP, 2.70 ERA
        double midP = PromotionScore.PitcherPerformance(600, 95, FixtureLeagueEra);  // 200 IP, 4.28 ERA
        double cupP = PromotionScore.PitcherPerformance(1, 0, FixtureLeagueEra);     // one perfect out
        Check("§2.1 pitchers: ERA− ordering holds; a 1-out 0.00-ERA cup of coffee is capped + shrunk to ~average",
            aceP > midP && aceP > 130.0 && Math.Abs(midP - 100.0) < 2.0 && cupP < 103.0,
            $"ace {aceP:F1} mid {midP:F1} cup {cupP:F1}");

        // ---- §2.2 scouting & age (acceptance check 6) ----
        double youngEqual = PromotionScore.Scouting(150, 17);
        double oldEqual = PromotionScore.Scouting(150, 33);
        double lowCeiling = PromotionScore.Scouting(120, 25);
        double highCeiling = PromotionScore.Scouting(180, 25);
        double youthA = PromotionScore.Combine(102.0, PromotionScore.Scouting(200, 17)); // struggling blue-chip teen
        double vetA = PromotionScore.Combine(112.0, PromotionScore.Scouting(140, 33));   // hot low-ceiling vet
        Check("§2.2 scouting gate: equal ratings → younger wins; equal age → higher-rated wins; both signals bind",
            youngEqual > oldEqual && highCeiling > lowCeiling && youthA > vetA
            && PromotionScore.AgeBonus(PromotionProfile.YoungestProspectAge) == PromotionProfile.AgeBonusMax
            && PromotionScore.AgeBonus(PromotionProfile.PeakAge) == 0.0
            && PromotionScore.AgeBonus(40) < 0.0 && PromotionScore.AgeBonus(60) >= -PromotionProfile.AgeBonusMax,
            $"S(150,17)={youngEqual:F1} S(150,33)={oldEqual:F1} youthA={youthA:F1} vetA={vetA:F1}");

        // ---- multi-rollover sweep world (no games attached: P shrinks to 100
        // everywhere and ranking is scouting-driven — the sweep MECHANICS are
        // under test here; real-stat integration is the avatar suite) ----
        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed + 90);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);
        LeagueGenerator.EnsureTierLeagues(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);

        // Fixtures (deterministic picks — rosters load ordered by team, player):
        // an elite young HS bat (cream), a hopeless MLB bat (dreg), and the
        // §3.1 removal triggers — age, health, amateur age-out.
        var tierRoster = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.HS, tierRoster);
        string cream = NthOfRole(tierRoster, PitcherRole.None, 0);
        string agedOut = NthOfRole(tierRoster, PitcherRole.None, 1);
        baseball.LoadRosterByTier(LeagueTier.MLB, tierRoster);
        string dreg = NthOfRole(tierRoster, PitcherRole.None, 0);
        string retiree = NthOfRole(tierRoster, PitcherRole.None, 1);
        string broken = NthOfRole(tierRoster, PitcherRole.Starter, 0);

        baseball.UpsertRatings(new PlayerRatingsRow
        {
            PlayerId = cream, IsPitcher = false,
            BatPower = 100, BatContact = 100, BatDiscipline = 100,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
        });
        players.SetAge(cream, 15);
        baseball.UpsertRatings(new PlayerRatingsRow
        {
            PlayerId = dreg, IsPitcher = false,
            BatPower = 0, BatContact = 0, BatDiscipline = 0,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 0,
        });
        players.SetAge(dreg, 25);
        players.SetAge(retiree, 41); // ages to 42 on the first rollover → forced retirement
        players.InsertBattingSeason(new BattingStatsRow
        {
            PlayerId = retiree, SeasonYear = StartYear - 1,
            Pa = 620, Ab = 560, H = 150, Doubles = 25, Triples = 3, Hr = 20, Bb = 50, So = 90, Rbi = 80, Sb = 5,
            Avg = 0.268, Obp = 0.328, Slg = 0.430, Ops = 0.758,
        });
        players.AdjustHealthCeiling(broken, -61); // 100 → 39, at/below the retirement floor (40)
        baseball.UpsertRatings(new PlayerRatingsRow
        {
            PlayerId = agedOut, IsPitcher = false,
            BatPower = 5, BatContact = 5, BatDiscipline = 5,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 5,
        });
        players.SetAge(agedOut, 19); // ages to 20 > HS cap, bottom score → washes out unpromoted

        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
        clock.Initialize(StartYear);
        // Aging first (CareerManager's job in the live wiring), promotion second,
        // per-rollover tracking third — per-channel FIFO order.
        bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
        var promo = new PromotionManager(
            db, players, baseball, new GameStateQueries(db),
            new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9C0001UL));
        promo.AttachTo(bus);

        const int Years = 12;
        var conservationFailures = new List<string>();
        var creamPath = new List<(bool Rostered, LeagueTier Tier)>();
        var dregPath = new List<(bool Rostered, LeagueTier Tier)>();
        var scratchSeasons = new List<BattingStatsRow>();
        bool retireeOk = false, brokenOk = false, agedOutOk = false;
        int firstRemovals = -1, firstIntake = -1;
        bus.Subscribe<SeasonRolledOverEvent>(e =>
        {
            PromotionSummary run = promo.LastRun;
            if (run.SeasonYear != e.PreviousSeasonYear)
            {
                conservationFailures.Add($"{e.PreviousSeasonYear}: pass did not run");
            }
            if (run.Intake != run.Removals)
            {
                conservationFailures.Add($"{e.PreviousSeasonYear}: intake {run.Intake} != removals {run.Removals}");
            }
            if (!TierLadderInvariantHolds(baseball, out string detail))
            {
                conservationFailures.Add($"{e.PreviousSeasonYear}: {detail}");
            }
            creamPath.Add(RosteredTierOf(players, baseball, cream));
            dregPath.Add(RosteredTierOf(players, baseball, dreg));
            if (e.PreviousSeasonYear == StartYear)
            {
                firstRemovals = run.Removals;
                firstIntake = run.Intake;
                retireeOk = players.TryGetById(retiree, out PlayerRow r1) && r1.TeamId is null
                    && players.LoadBattingSeasons(retiree, scratchSeasons) > 0;
                brokenOk = players.TryGetById(broken, out PlayerRow r2) && r2.TeamId is null;
                agedOutOk = players.TryGetById(agedOut, out PlayerRow r3) && r3.TeamId is null;
            }
        });

        for (int i = 0; i < GlobalState.DaysPerSeason * Years; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }

        Check("§10.2 conservation law: after every offseason, 48 teams × exactly 9+5+3, 816 rostered, intake ≡ removals",
            conservationFailures.Count == 0 && creamPath.Count == Years,
            conservationFailures.Count > 0 ? conservationFailures[0] : $"{Years} rollovers clean");
        Check("§10.4 removal triad: retiree (42), broken (health ≤ 40) and HS age-out all land in FA, stats intact, exactly matched by HS intake",
            retireeOk && brokenOk && agedOutOk && firstRemovals == 3 && firstIntake == 3,
            $"removals {firstRemovals} intake {firstIntake}");

        bool creamOk = true;
        for (int k = 0; k < creamPath.Count; k++)
        {
            creamOk &= creamPath[k].Rostered && (int)creamPath[k].Tier == Math.Min(k + 1, (int)LeagueTier.MLB);
        }
        Check("§10.3 cream rises: a max-rated 15-year-old climbs exactly one rung per offseason, HS→MLB in 5, then holds",
            creamOk,
            $"path {string.Join("→", creamPath.ConvertAll(p => p.Rostered ? p.Tier.ToString() : "FA"))}");

        bool dregOk = true;
        for (int k = 0; k < 4; k++)
        {
            dregOk &= dregPath[k].Rostered && (int)dregPath[k].Tier == (int)LeagueTier.MLB - (k + 1);
        }
        for (int k = 4; k < dregPath.Count; k++)
        {
            dregOk &= !dregPath[k].Rostered;
        }
        Check("§10.3 symmetric: a zero-rated MLB bat relegates one rung per offseason, then washes out at the College age cap",
            dregOk,
            $"path {string.Join("→", dregPath.ConvertAll(p => p.Rostered ? p.Tier.ToString() : "FA"))}");

        // ---- §10.9 talent-stratified equilibrium (pre-9d steady state) ----
        var meanTalent = new double[LeagueDirectory.TierCount];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            baseball.LoadRosterByTier((LeagueTier)t, tierRoster);
            int sum = 0;
            int batters = 0;
            foreach (RosterPlayerRow row in tierRoster)
            {
                if (row.Role == PitcherRole.None)
                {
                    sum += row.BatPower + row.BatContact + row.BatDiscipline;
                    batters++;
                }
            }
            meanTalent[t] = (double)sum / batters;
        }
        Console.WriteLine($"  mean batter talent after {Years} offseasons: " +
            string.Join("  ", Enumerable.Range(0, LeagueDirectory.TierCount)
                .Select(t => $"{(LeagueTier)t}={meanTalent[t]:F1}")));
        // Pre-9d equilibrium shape (doc §1/§10.9): the extremes stratify hard —
        // HS is the poached-and-refreshed intake pool (strictly lowest), MLB
        // accumulates every cascade's best (strictly highest, by a wide gap),
        // and the amateur boundary sorts (College > HS). The MIDDLE minors are
        // near-sealed until 9d: no age caps and no retirements until their
        // founding generation ages through, so their means hover at the
        // generation mean (movement there is only the capped swap churn plus
        // the MLB-retirement cascade from year ~6) — full strict monotonicity
        // across the middle rungs is 9d's development-curve handoff, not a
        // static-ratings property.
        bool hsLowest = true;
        bool mlbHighest = true;
        for (int t = 1; t < LeagueDirectory.TierCount; t++)
        {
            hsLowest &= meanTalent[t] > meanTalent[0];
            mlbHighest &= meanTalent[5] > meanTalent[t - 1];
        }
        Check($"§10.9 pre-9d equilibrium: after {Years} offseasons HS is strictly the weakest tier, MLB strictly the strongest (gap ≥ 20), College out-sorts HS",
            hsLowest && mlbHighest && meanTalent[5] - meanTalent[0] >= 20.0 && meanTalent[1] > meanTalent[0] + 5.0,
            $"HS {meanTalent[0]:F1} → MLB {meanTalent[5]:F1} (gap {meanTalent[5] - meanTalent[0]:F1})");
        Check("sweep world: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);

        // ---- §10.8 determinism: same seed → same promotions, same intake ids ----
        (string fpA, PromotionSummary lastA) = RunPromotionDeterminismWorld(scratchPath + ".detA", schemaPath);
        (string fpB, PromotionSummary lastB) = RunPromotionDeterminismWorld(scratchPath + ".detB", schemaPath);
        Check("§10.8 determinism: two same-seed 3-year worlds produce identical rosters (intake ids included) and summaries",
            fpA == fpB && fpA.Length > 0
            && lastA.Removals == lastB.Removals && lastA.Promotions == lastB.Promotions
            && lastA.Relegations == lastB.Relegations && lastA.Intake == lastB.Intake,
            $"removals {lastA.Removals}/{lastB.Removals} promotions {lastA.Promotions}/{lastB.Promotions}");
    }

    private static (string Fingerprint, PromotionSummary Last) RunPromotionDeterminismWorld(string path, string schemaPath)
    {
        using var db = new DatabaseManager(path);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var rng = new RngState(LeagueSeed + 91);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        LeagueGenerator.EnsureTierLeagues(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
        clock.Initialize(StartYear);
        bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
        var promo = new PromotionManager(
            db, players, baseball, new GameStateQueries(db),
            new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9C0DE7UL));
        promo.AttachTo(bus);
        for (int i = 0; i < GlobalState.DaysPerSeason * 3; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }

        var rows = new List<PlayerRow>();
        players.LoadAll(rows);
        rows.Sort(static (a, b) => string.CompareOrdinal(a.PlayerId, b.PlayerId));
        var fingerprint = new System.Text.StringBuilder(rows.Count * 44);
        foreach (PlayerRow row in rows)
        {
            fingerprint.Append(row.PlayerId).Append(':')
                .Append(row.TeamId?.ToString() ?? "-").Append(';');
        }
        return (fingerprint.ToString(), promo.LastRun);
    }

    private static void RunPromotionStreamSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9c neutrality/stream isolation: an evaluating no-op pass leaves the world bit-identical ---");

        // Two same-seed worlds — one with the promotion pass attached and
        // firing (but choosing no-op: spread-0 ratings, ages pinned to 25),
        // one without it — must produce BIT-IDENTICAL season-2 games in all
        // six tiers. This is §10.1's "inert until movement" clause plus
        // §10.8's stream isolation: attaching, evaluating and no-op'ing the
        // pass reads the database only and draws from no one's RNG stream.
        var batByTier = new LeagueBattingTotals[2][];
        var pitByTier = new LeaguePitchingTotals[2][];
        bool noOpRan = false;
        bool integrityOk = true;
        for (int variant = 0; variant < 2; variant++)
        {
            bool withPromo = variant == 1;
            string path = scratchPath + (withPromo ? ".promo" : ".bare");
            using var db = new DatabaseManager(path);
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var rng = new RngState(LeagueSeed + 95);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref rng);
            LeagueGenerator.EnsureTierLeagues(db, players, baseball, ratingSpread: 0, ref rng);
            // Pin ages inside every tier's window (HS cap 19, College cap 23,
            // retirement 42) so the pass finds no removals — and spread-0
            // ratings mean no swaps — leaving it a provable no-op.
            var tierRows = new List<RosterPlayerRow>();
            db.RunInBatch(() =>
            {
                for (int t = 0; t < LeagueDirectory.TierCount; t++)
                {
                    baseball.LoadRosterByTier((LeagueTier)t, tierRows);
                    int pinnedAge = (LeagueTier)t switch
                    {
                        LeagueTier.HS => 16,
                        LeagueTier.College => 20,
                        _ => 25,
                    };
                    foreach (RosterPlayerRow row in tierRows)
                    {
                        players.SetAge(row.PlayerId, pinnedAge);
                    }
                }
            });

            var state = new GlobalState();
            var bus = new EventBus();
            var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
            clock.Initialize(StartYear);
            PromotionManager? promo = null;
            if (withPromo)
            {
                bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
                promo = new PromotionManager(
                    db, players, baseball, new GameStateQueries(db),
                    new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9C0002UL));
                promo.AttachTo(bus);
            }
            // Season 1 idles gameless; the rollover fires on the last tick.
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            if (withPromo)
            {
                PromotionSummary run = promo!.LastRun;
                noOpRan = run.SeasonYear == StartYear && run.Removals == 0
                    && run.Promotions == 0 && run.Relegations == 0 && run.Intake == 0;
            }
            // Fresh same-seed sims join for season 2 (days 2..365) in BOTH worlds.
            var normalizer = new StatsNormalizer(db, baseball);
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                var sim = new LeagueSimulator(
                    db, baseball, normalizer, new RngState(SeasonSeed + 50 + (ulong)t), (LeagueTier)t);
                sim.Initialize();
                sim.AttachTo(bus);
            }
            for (int i = 0; i < GlobalState.DaysPerSeason - 1; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            batByTier[variant] = new LeagueBattingTotals[LeagueDirectory.TierCount];
            pitByTier[variant] = new LeaguePitchingTotals[LeagueDirectory.TierCount];
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                batByTier[variant][t] = baseball.LoadLeagueBattingTotals(StartYear + 1, (LeagueTier)t);
                pitByTier[variant][t] = baseball.LoadLeaguePitchingTotals(StartYear + 1, (LeagueTier)t);
            }
            integrityOk &= !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0;
        }

        bool identical = true;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            identical &= BattingTotalsEqual(in batByTier[0][t], in batByTier[1][t])
                && PitchingTotalsEqual(in pitByTier[0][t], in pitByTier[1][t]);
        }
        Check("§10.1/§10.8: the pass fired, evaluated and chose no-op (no removals, no swaps, no intake)",
            noOpRan);
        Check("§10.1/§10.8: season 2 is BIT-IDENTICAL across all six tiers with the no-op pass attached vs absent",
            identical && integrityOk,
            $"MLB PA {batByTier[0][5].Pa}/{batByTier[1][5].Pa} H {batByTier[0][5].H}/{batByTier[1][5].H}");
    }

    private static void RunPromotionAvatarSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9c avatar tier-transfer handoff (doc §6, acceptance check 7) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed + 97);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        LeagueGenerator.EnsureTierLeagues(db, players, baseball, ratingSpread: 0, ref genRng);

        var state = new GlobalState();
        var bus = new EventBus();
        var gameState = new GameStateQueries(db);
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);
        var normalizer = new StatsNormalizer(db, baseball);
        var leagues = new LeagueDirectory();
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var sim = new LeagueSimulator(
                db, baseball, normalizer, new RngState(SeasonSeed + 70 + (ulong)t), (LeagueTier)t);
            sim.Initialize();
            sim.AttachTo(bus);
            leagues.Register(sim);
        }
        var micro = new MicroGame(db, baseball);
        micro.Initialize();
        var career = new CareerManager(
            db, players, baseball, gameState, state, leagues, micro, new RngState(0x9CA7A7UL));
        career.AttachTo(bus);
        var promo = new PromotionManager(
            db, players, baseball, gameState, leagues, micro, new RngState(0x9C0003UL));
        promo.Career = career;
        promo.AttachTo(bus);

        int avatarEvents = 0;
        int lastAvatarEventTeam = 0;
        bus.Subscribe<AvatarChangedEvent>(e =>
        {
            avatarEvents++;
            lastAvatarEventTeam = e.TeamId;
        });

        const int HsTeam = 101;
        career.CreateAvatar("Ace", "Prospect", HsTeam, new PlayerRatingsRow
        {
            IsPitcher = false,
            BatPower = 100, BatContact = 100, BatDiscipline = 100,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
        });
        string avatarId = career.AvatarPlayerId;

        // Snapshots for the §6 destination proof (the out-ranked incumbent
        // must relegate into the avatar's vacated slot).
        var tierRoster = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.HS, tierRoster);
        var oldHsTeamIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (RosterPlayerRow row in tierRoster)
        {
            if (row.TeamId == HsTeam)
            {
                oldHsTeamIds.Add(row.PlayerId);
            }
        }
        baseball.LoadRosterByTier(LeagueTier.College, tierRoster);
        var oldCollegeTeamById = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RosterPlayerRow row in tierRoster)
        {
            oldCollegeTeamById.Add(row.PlayerId, row.TeamId);
        }
        avatarEvents = 0;

        // Season 1 + the rollover on the last tick: the max-rated avatar
        // dominates HS (real games via the autopilot micro-sim), ages to 20
        // (no succession trigger), and the promotion pass runs.
        for (int i = 0; i < GlobalState.DaysPerSeason; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }

        PromotionSummary run1 = promo.LastRun;
        bool inCollege = baseball.TryGetTeamTier(career.AvatarTeamId, out LeagueTier newTier)
            && newTier == LeagueTier.College;
        Check("§6 gate: a dominating HS avatar promotes to a real College team+slot on its first offseason",
            run1.SeasonYear == StartYear && run1.AvatarPromoted
            && run1.AvatarTeamId == career.AvatarTeamId && inCollege,
            $"avatar team {career.AvatarTeamId} ({newTier}), promoted={run1.AvatarPromoted}");
        Check("§6 school gate input: HS→College keeps the destination amateur (the 9b bridge derives the gate from this tier)",
            newTier == LeagueTier.College, $"tier {newTier}");

        // §6 destination: the avatar rose via merit swap (rollover 1 has no
        // College batter vacancies), so his out-ranked BATTER incumbent must
        // now occupy the avatar's vacated HS slot. Scoped to the avatar's
        // role cohort and bounded by its swap cap: the boundary's other
        // batter swap can legally land a second ex-destination player on
        // team 101 whenever its riser was a 101 teammate (ordinary churn —
        // more common since the 9d-2 seam removed the College age fudge),
        // and pitcher-role swaps are definitionally not the counterpart.
        baseball.LoadRosterByTier(LeagueTier.HS, tierRoster);
        int counterpartCount = 0;
        foreach (RosterPlayerRow row in tierRoster)
        {
            if (row.Role == PitcherRole.None
                && row.TeamId == HsTeam && !oldHsTeamIds.Contains(row.PlayerId)
                && oldCollegeTeamById.TryGetValue(row.PlayerId, out int fromTeam)
                && fromTeam == run1.AvatarTeamId)
            {
                counterpartCount++;
            }
        }
        Check("§6 destination: an out-ranked batter from the avatar's destination team relegated into the avatar's vacated HS slot (within the batter swap cap)",
            counterpartCount >= 1 && counterpartCount <= PromotionProfile.SwapCapBatters,
            $"counterparts {counterpartCount}");
        Check("§10.2 roster invariant holds across the avatar handoff",
            TierLadderInvariantHolds(baseball, out string invariantDetail), invariantDetail);
        Check("AvatarChangedEvent republished with the destination team (the 9b Life-sim bridge's input)",
            avatarEvents >= 1 && lastAvatarEventTeam == career.AvatarTeamId,
            $"events {avatarEvents}, team {lastAvatarEventTeam}");

        // Stat continuity + interactive scheduling in the NEW tier's round-robin.
        var avatarSeasons = new List<BattingStatsRow>();
        players.LoadBattingSeasons(avatarId, avatarSeasons);
        bool hasSeason1Line = false;
        foreach (BattingStatsRow row in avatarSeasons)
        {
            hasSeason1Line |= row.SeasonYear == StartYear && row.Pa > 0;
        }
        career.AutopilotAttendedGames = false;
        clock.AdvanceDay();
        bus.DispatchPending(); // season-2 day 2
        bool pendingInCollege = career.TryGetPendingGame(out PendingAttendedGame pending)
            && pending.HomeTeamId is >= 201 and <= 208 && pending.AwayTeamId is >= 201 and <= 208;
        var neutral = new NeutralBatterPolicy();
        career.PlayPendingGame(ref neutral);
        Check("§10.7 continuity + scheduling: season-1 stats persist by player_id; the pending game lands in the College round-robin and plays clean through the re-initialized micro-sim",
            hasSeason1Line && pendingInCollege && !career.HasPendingGame,
            $"pending {pending.HomeTeamId}@{pending.AwayTeamId}");
        career.AutopilotAttendedGames = true;

        // ---- §6 succession precedence: a parked succession skips the avatar's
        // promotion that offseason; the NPC churn still runs. ----
        career.AutopilotSuccession = false;
        string heirId = career.ConceiveChild("Heir", "Prospect", birthAge: 19);
        players.SetBaseballInterest(heirId, 100);
        players.SetAge(avatarId, 41); // ages to 42 at the next rollover → retirement trigger
        int teamBeforeSkip = career.AvatarTeamId;
        for (int i = 0; i < GlobalState.DaysPerSeason - 2; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }

        // Origin release, asserted BEFORE the next rollover (its age-out
        // removals would drop players out of the tier-joined aggregate): the
        // vacated HS team's games ran under the macro sim all season — a
        // stale attended-team claim would have skipped one pairing per day.
        LeaguePitchingTotals hsSeason2 = baseball.LoadLeaguePitchingTotals(StartYear + 1, LeagueTier.HS);
        Check("origin sim released: the HS tier completes a full season after the avatar leaves (no stale attended-team claim)",
            hsSeason2.Gs == LeagueSimulator.RegularSeasonDays * LeagueSimulator.TeamCount,
            $"Gs {hsSeason2.Gs}/{LeagueSimulator.RegularSeasonDays * LeagueSimulator.TeamCount}");

        clock.AdvanceDay(); // the season-3 boundary: rollover 2 fires here
        bus.DispatchPending();

        PromotionSummary run2 = promo.LastRun;
        Check("§6 succession precedence: the pending succession parks, the (still-dominant) avatar's promotion is skipped, NPC churn still runs",
            career.HasPendingSuccessionChoice && career.AvatarTeamId == teamBeforeSkip
            && !run2.AvatarPromoted && run2.SeasonYear == StartYear + 1
            && run2.Removals > 0 && run2.Intake == run2.Removals,
            $"removals {run2.Removals}, avatar team {career.AvatarTeamId}");

        career.ResolvePendingSuccession(heirId);
        Check("succession resolves cleanly after the skipped offseason: the heir inherits the College franchise",
            career.AvatarPlayerId == heirId && career.AvatarTeamId == teamBeforeSkip
            && !career.HasPendingSuccessionChoice,
            $"avatar team {career.AvatarTeamId}");
        Check("avatar world: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
    }

    // ------------------------------------------------------------------
    // Phase 9d: development/decline curves (docs/design/development_decline_curves.md)
    // ------------------------------------------------------------------

    private static void RunDevelopmentCurveSuite()
    {
        Console.WriteLine("--- Phase 9d development curve fixtures (doc §2/§3, acceptance check 2 — bell-injected, no db) ---");

        // ---- phase-weight shapes ----
        Check("§2.1 youthWeight: 1.0 at 15, 0.0 at the peak (27) and beyond, linear between",
            DevelopmentCurve.YouthWeight(15) == 1.0 && DevelopmentCurve.YouthWeight(27) == 0.0
            && DevelopmentCurve.YouthWeight(33) == 0.0
            && Math.Abs(DevelopmentCurve.YouthWeight(21) - 0.5) < 1e-12,
            $"w(21)={DevelopmentCurve.YouthWeight(21):F3}");
        bool ageMonotone = true;
        for (int age = 28; age <= 42; age++)
        {
            ageMonotone &= DevelopmentCurve.AgeWeight(age) > DevelopmentCurve.AgeWeight(age - 1);
        }
        Check("§2.3 ageWeight: 0 at the peak, 1.0 at retirement (42), strictly accelerating between",
            DevelopmentCurve.AgeWeight(27) == 0.0 && Math.Abs(DevelopmentCurve.AgeWeight(42) - 1.0) < 1e-12 && ageMonotone);
        Check("§2.3 healthScale: 1.0 at full health, 1.6 at the health-40 retirement floor",
            DevelopmentCurve.HealthScale(100) == 1.0 && Math.Abs(DevelopmentCurve.HealthScale(40) - 1.6) < 1e-12);

        // ---- growth fixtures (bell = 0, exact) ----
        int g15 = DevelopmentCurve.DevelopRating(40, 70, 15, RatingKind.BatPower, 100, 0.0, 0.0);
        int g21 = DevelopmentCurve.DevelopRating(40, 70, 21, RatingKind.BatPower, 100, 0.0, 0.0);
        int g27 = DevelopmentCurve.DevelopRating(40, 70, 27, RatingKind.BatPower, 100, 0.0, 0.0);
        Check("§2.1 growth: 40→55 at 15 (half the gap), 40→48 at 21 (quarter), 40→40 at the peak (growth stops)",
            g15 == 55 && g21 == 48 && g27 == 40, $"{g15}/{g21}/{g27}");
        Check("§2.1 no-headroom young player does not grow (potential = current)",
            DevelopmentCurve.DevelopRating(70, 70, 18, RatingKind.BatPower, 100, 0.0, 0.0) == 70);
        Check("§2.1 never overshoots: a max-lottery draw one point under the ceiling clamps AT the ceiling",
            DevelopmentCurve.DevelopRating(69, 70, 15, RatingKind.BatPower, 100, 0.0, 1.0) == 70
            && DevelopmentCurve.DevelopRating(99, 100, 16, RatingKind.BatPower, 100, 0.0, 1.0) == 100);

        int converging = 25;
        bool monotone = true;
        for (int age = 15; age <= 27; age++)
        {
            int next = DevelopmentCurve.DevelopRating(converging, 90, age, RatingKind.BatContact, 100, 0.0, 0.0);
            monotone &= next >= converging && next <= 90;
            converging = next;
        }
        Check("§2.1 a raw 25/90 prospect converges monotonically to within 3 of his ceiling by the peak",
            monotone && converging >= 87 && converging <= 90, $"peak value {converging}");

        // ---- decline fixtures (bell = 0, exact). Ages 35/41 (not the 9d-1
        // 33/39): the §7 tuning pass halved DeclineRate 6.0→3.0 (see the
        // DevelopmentProfile.DeclineRate doc comment) to fix a real
        // tier-equilibrium inversion, which pushes the age where erosion
        // first rounds to a nonzero point from 32 to 34 — decline is
        // imperceptible right at the peak and picks up through the mid-30s,
        // same accelerating shape, gentler magnitude. ----
        int d35 = 70 - DevelopmentCurve.DevelopRating(70, 70, 35, RatingKind.BatPower, 100, 0.0, 0.0);
        int d41 = 70 - DevelopmentCurve.DevelopRating(70, 70, 41, RatingKind.BatPower, 100, 0.0, 0.0);
        int d41Skill = 70 - DevelopmentCurve.DevelopRating(70, 70, 41, RatingKind.BatDiscipline, 100, 0.0, 0.0);
        int d41Frail = 70 - DevelopmentCurve.DevelopRating(70, 70, 41, RatingKind.BatPower, 40, 0.0, 0.0);
        Check("§2.3 decline: a veteran erodes (−1 at 35), accelerates with age (−3 at 41)",
            d35 == 1 && d41 == 3, $"35:{d35} 41:{d41}");
        Check("§2.3 kindWeight split: discipline/command decline at half the physical rate (crafty veteran)",
            d41Skill == 1 && d41Skill < d41, $"skill {d41Skill} vs physical {d41}");
        Check("§2.3 healthScale: an eroded health_ceiling (40) speeds decline (−4 vs −3 at 41)",
            d41Frail == 4 && d41Frail > d41, $"frail {d41Frail}");
        Check("§2 clamps: a collapsing rating floors at 0; growth+jitter ceilings at 100",
            DevelopmentCurve.DevelopRating(2, 2, 41, RatingKind.BatPower, 0, 0.0, 0.0) == 0);

        // ---- practice lever, curve side (the §4 conversion lands in 9d-2;
        // acceptance 2's "bounded, capped bonus" is a pure-curve property) ----
        int practiced = DevelopmentCurve.DevelopRating(40, 70, 20, RatingKind.BatPower, 100, 0.20, 0.0);
        int unpracticed = DevelopmentCurve.DevelopRating(40, 70, 20, RatingKind.BatPower, 100, 0.0, 0.0);
        int overPracticed = DevelopmentCurve.DevelopRating(40, 70, 20, RatingKind.BatPower, 100, 5.0, 0.0);
        int atCap = DevelopmentCurve.DevelopRating(40, 70, 20, RatingKind.BatPower, 100, DevelopmentProfile.PracticeFracCap, 0.0);
        int relieved = 70 - DevelopmentCurve.DevelopRating(70, 70, 41, RatingKind.BatPower, 100, DevelopmentProfile.PracticeFracCap, 0.0);
        Check("§4 practice adds a bounded extra gap fraction (55 vs 49 at 20), hard-capped (5.0 ≡ the cap)",
            practiced == 55 && unpracticed == 49 && overPracticed == atCap && atCap == 56,
            $"practiced {practiced} unpracticed {unpracticed} capped {atCap}");
        Check("§4 practice relieves veteran decline (staying in shape: −2 vs −3 at 41)",
            relieved == 2 && relieved < d41, $"relieved {relieved}");

        // ---- intake discount & creation headroom (§3.3, deterministic cores) ----
        Check("§3.3 prospect discount: full-roll 15-year-old sits 20 under his ceiling; zero at/past the peak; clamps at 0",
            DevelopmentCurve.RawCurrent(60, 15, 1.0) == 40
            && DevelopmentCurve.RawCurrent(60, 27, 1.0) == 60 && DevelopmentCurve.RawCurrent(60, 33, 1.0) == 60
            && DevelopmentCurve.RawCurrent(60, 21, 0.5) == 55
            && DevelopmentCurve.RawCurrent(5, 15, 1.0) == 0);
        Check("§3.3 avatar headroom: a modest 60 at 19 ceilings at 70; a max-built 100 gets zero headroom (decline-only)",
            DevelopmentCurve.HeadroomPotential(60, CareerManager.StartingAge) == 70
            && DevelopmentCurve.HeadroomPotential(100, CareerManager.StartingAge) == 100
            && DevelopmentCurve.HeadroomPotential(98, CareerManager.StartingAge) == 100);
    }

    private static void RunDevelopmentArcSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9d career arcs: growth→climb, decline→relegation→retirement, conservation (doc §10.3/.4/.7/.8) ---");

        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed + 100);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);
        LeagueGenerator.EnsureTierLeagues(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);

        // ---- §3.3 generation shape: every ratings row has a ceiling row and
        // current ≤ potential per rating; the HS discount has real teeth ----
        var roster = new List<RosterPlayerRow>();
        baseball.LoadRoster(roster);
        var potentials = new Dictionary<string, PlayerPotentialRow>();
        int potentialCount = baseball.LoadAllPotential(potentials);
        bool invariantHolds = true;
        foreach (RosterPlayerRow row in roster)
        {
            invariantHolds &= potentials.TryGetValue(row.PlayerId, out PlayerPotentialRow pot)
                && row.BatPower <= pot.BatPower && row.BatContact <= pot.BatContact
                && row.BatDiscipline <= pot.BatDiscipline && row.PitStuff <= pot.PitStuff
                && row.PitControl <= pot.PitControl && row.PitStamina <= pot.PitStamina
                && row.Fielding <= pot.Fielding;
        }
        Check("§3.3 world-gen: one Player_Potential row per player, current ≤ potential on every rating",
            potentialCount == roster.Count && invariantHolds, $"{potentialCount} ceilings / {roster.Count} rostered");
        var hsRoster = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.HS, hsRoster);
        int rawHsPlayers = 0;
        foreach (RosterPlayerRow row in hsRoster)
        {
            PlayerPotentialRow pot = potentials[row.PlayerId];
            int headroom = (pot.BatPower - row.BatPower) + (pot.BatContact - row.BatContact)
                + (pot.BatDiscipline - row.BatDiscipline) + (pot.PitStuff - row.PitStuff)
                + (pot.PitControl - row.PitControl) + (pot.PitStamina - row.PitStamina)
                + (pot.Fielding - row.Fielding);
            if (headroom > 0)
            {
                rawHsPlayers++;
            }
        }
        Check("§3.3 HS intake is raw-now/projectable-later: most of the tier carries real headroom",
            rawHsPlayers > hsRoster.Count / 2, $"{rawHsPlayers}/{hsRoster.Count} with headroom");

        // ---- fixtures: a raw high-ceiling HS prospect (§10.3) and a fading,
        // health-worn MLB veteran (§10.4), in one 24-offseason world ----
        string prospect = NthOfRole(hsRoster, PitcherRole.None, 0);
        var mlbRoster = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.MLB, mlbRoster);
        string veteran = NthOfRole(mlbRoster, PitcherRole.None, 0);

        players.SetAge(prospect, 15);
        baseball.UpsertRatings(new PlayerRatingsRow
        {
            PlayerId = prospect, IsPitcher = false,
            BatPower = 25, BatContact = 25, BatDiscipline = 25,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 40,
        });
        baseball.UpsertPotential(new PlayerPotentialRow
        {
            PlayerId = prospect,
            BatPower = 90, BatContact = 90, BatDiscipline = 90,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 60,
        });
        players.SetAge(veteran, 36);
        players.AdjustHealthCeiling(veteran, -40); // 100 → 60: healthScale 1.4, the §2.3 coupling
        baseball.UpsertRatings(new PlayerRatingsRow
        {
            PlayerId = veteran, IsPitcher = false,
            BatPower = 35, BatContact = 35, BatDiscipline = 35,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 45,
        });
        baseball.UpsertPotential(new PlayerPotentialRow
        {
            PlayerId = veteran,
            BatPower = 35, BatContact = 35, BatDiscipline = 35,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 45,
        });

        long hsBatSum = 0;
        int hsBatters = 0;
        baseball.LoadRosterByTier(LeagueTier.HS, hsRoster);
        foreach (RosterPlayerRow row in hsRoster)
        {
            if (row.Role == PitcherRole.None)
            {
                hsBatSum += row.BatPower + row.BatContact + row.BatDiscipline;
                hsBatters++;
            }
        }
        Check("§10.3 the prospect starts BELOW the promotion bar (seed 75, far under the HS batter mean)",
            75 < hsBatSum / hsBatters - 40, $"HS batter mean {hsBatSum / (double)hsBatters:F0}");

        // ---- the live wiring order (development doc §5): aging (CareerManager's
        // job) → DevelopmentManager → PromotionManager, per-channel FIFO ----
        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
        clock.Initialize(StartYear);
        bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
        var development = new DevelopmentManager(
            db, players, baseball, new GameStateQueries(db),
            new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9D0001UL));
        development.AttachTo(bus);
        var promo = new PromotionManager(
            db, players, baseball, new GameStateQueries(db),
            new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9C0001UL));
        promo.AttachTo(bus);

        const int Years = 24;
        var conservationFailures = new List<string>();
        var prospectPath = new List<(bool Rostered, LeagueTier Tier, int BatSum)>();
        var veteranPath = new List<(bool Rostered, LeagueTier Tier, int BatSum)>();
        bool devRanEveryYear = true;
        DevelopmentSummary firstRun = default;
        bus.Subscribe<SeasonRolledOverEvent>(e =>
        {
            devRanEveryYear &= development.LastRun.SeasonYear == e.PreviousSeasonYear;
            if (e.PreviousSeasonYear == StartYear)
            {
                firstRun = development.LastRun;
            }
            if (promo.LastRun.Intake != promo.LastRun.Removals)
            {
                conservationFailures.Add($"{e.PreviousSeasonYear}: intake {promo.LastRun.Intake} != removals {promo.LastRun.Removals}");
            }
            if (!TierLadderInvariantHolds(baseball, out string detail))
            {
                conservationFailures.Add($"{e.PreviousSeasonYear}: {detail}");
            }
            (bool rostered, LeagueTier tier) = RosteredTierOf(players, baseball, prospect);
            baseball.TryGetRatings(prospect, out PlayerRatingsRow p);
            prospectPath.Add((rostered, tier, p.BatPower + p.BatContact + p.BatDiscipline));
            (bool vetRostered, LeagueTier vetTier) = RosteredTierOf(players, baseball, veteran);
            baseball.TryGetRatings(veteran, out PlayerRatingsRow v);
            veteranPath.Add((vetRostered, vetTier, v.BatPower + v.BatContact + v.BatDiscipline));
        });

        for (int i = 0; i < GlobalState.DaysPerSeason * Years; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }

        Check("§5 the pass ran on every rollover; the first offseason moved ratings BOTH ways (growth and decline)",
            devRanEveryYear && firstRun.PlayersChanged > 0 && firstRun.PointsUp > 0 && firstRun.PointsDown > 0,
            $"year-1 changed {firstRun.PlayersChanged} (+{firstRun.PointsUp}/−{firstRun.PointsDown})");
        Check("§10.8 conservation still holds through 24 developed offseasons: 48 × 9+5+3, 816 rostered, intake ≡ removals",
            conservationFailures.Count == 0,
            conservationFailures.Count > 0 ? conservationFailures[0] : "24 rollovers clean");

        // ---- §10.3 the growth arc (the BUILD_PLAN exit criterion): distinct
        // from 9c's cream (an already-max seed promoted at UNMOVED ratings),
        // the raw prospect climbs only on DEVELOPED ratings — the sweep never
        // sees his 75-sum seed (develop-before-sort, doc §5). Since the 9d-2
        // scouting seam widened the amateur boundary (College incumbents lost
        // the age fudge, HS risers kept their real headroom), a big first
        // develop can clear the bar at rollover 1 — the assertion is the
        // developed climb, not the year number.
        // Ages: rollover k = age 15+k (peak 27 at k=12, snapshot index k-1).
        int peakSum = prospectPath[11].BatSum;
        bool grewMonotone = true;
        for (int k = 1; k <= 11; k++)
        {
            grewMonotone &= prospectPath[k].BatSum >= prospectPath[k - 1].BatSum - 4; // jitter-tolerant
        }
        int firstMove = -1;
        for (int k = 0; k < prospectPath.Count && firstMove < 0; k++)
        {
            if (prospectPath[k].Tier != LeagueTier.HS)
            {
                firstMove = k;
            }
        }
        bool climbedDeveloped = firstMove >= 0 && prospectPath[firstMove].BatSum >= 75 + 40;
        bool reachedMlb = false;
        foreach ((bool rostered, LeagueTier tier, _) in prospectPath)
        {
            reachedMlb |= rostered && tier == LeagueTier.MLB;
        }
        int finalSum = prospectPath[Years - 1].BatSum; // age 39
        // The raw seed provably can never clear a boundary himself: even
        // under the most charitable projection (saturated headroom), a
        // 75-sum's A sits far below a 100-average cohort plus the swap
        // margin — only the developed ratings moved him.
        double undevelopedA = PromotionScore.Combine(100.0, PromotionScore.Scouting(75, 16, 300));
        Check("§10.3 growth arc: the seed never climbs raw — his first move carries ≥ +40 developed points, and the undeveloped 75-sum provably misses the bar",
            climbedDeveloped && undevelopedA < 100.0 + PromotionProfile.SwapMargin,
            $"first move at rollover {firstMove + 1} with bat sum {(firstMove >= 0 ? prospectPath[firstMove].BatSum : 0)}; raw A {undevelopedA:F1}");
        Check("§10.3 growth arc: rises monotonically (jitter-tolerant) to within 30 of the 270 ceiling at the peak, never over",
            grewMonotone && peakSum >= 240 && peakSum <= 270, $"peak bat sum {peakSum}");
        // §7 tuning pass halved DeclineRate 6.0→3.0 (fixing a tier-equilibrium
        // inversion — see DevelopmentProfile.DeclineRate), so the 12-year
        // peak→39 decline is gentler than 9d-1's first-pass magnitude
        // (observed ~20 points here vs ~42 before); the margin is lowered to
        // match while still proving decline is real, not vanished.
        Check("§10.3 growth arc: the developed prospect climbs HS→…→MLB and declines past the peak",
            reachedMlb && finalSum < peakSum - 10, $"age-39 sum {finalSum} vs peak {peakSum}");

        // ---- §10.4 decline → relegation → retirement, zero new retirement
        // logic: the fading vet slides down the 9c ladder and the EXISTING
        // age-42 removal takes him at rollover 6 (36 → 42). ----
        bool declinedWhileRostered = true;
        bool relegatedBeforeOut = false;
        for (int k = 1; k < veteranPath.Count && veteranPath[k].Rostered; k++)
        {
            declinedWhileRostered &= veteranPath[k].BatSum < veteranPath[k - 1].BatSum;
        }
        foreach ((bool rostered, LeagueTier tier, _) in veteranPath)
        {
            relegatedBeforeOut |= rostered && tier < LeagueTier.MLB;
        }
        Check("§10.4 decline arc: the health-worn veteran's ratings fall EVERY season he remains rostered, and 9c's EXISTING merit swaps relegate him below MLB",
            declinedWhileRostered && relegatedBeforeOut,
            $"path {string.Join("→", veteranPath.ConvertAll(p => p.Rostered ? $"{p.Tier}:{p.BatSum}" : "FA"))}");
        bool removedAt42 = !veteranPath[6].Rostered && players.TryGetById(veteran, out PlayerRow vetRow)
            && vetRow.TeamId is null && vetRow.Age >= 42;
        bool neverBack = true;
        for (int k = 6; k < veteranPath.Count; k++)
        {
            neverBack &= !veteranPath[k].Rostered;
        }
        Check("§10.4 the EXISTING removal machinery retires him (unrostered by 42, row preserved, never re-rostered)",
            removedAt42 && neverBack);

        // ---- the current ≤ potential invariant survives 24 developed years ----
        baseball.LoadRoster(roster);
        baseball.LoadAllPotential(potentials);
        bool invariantStillHolds = true;
        foreach (RosterPlayerRow row in roster)
        {
            invariantStillHolds &= potentials.TryGetValue(row.PlayerId, out PlayerPotentialRow pot)
                && row.BatPower <= pot.BatPower && row.BatContact <= pot.BatContact
                && row.BatDiscipline <= pot.BatDiscipline && row.PitStuff <= pot.PitStuff
                && row.PitControl <= pot.PitControl && row.PitStamina <= pot.PitStamina
                && row.Fielding <= pot.Fielding;
        }
        Check("§2.1 never-overshoot holds world-wide after 24 offseasons (current ≤ potential everywhere)",
            invariantStillHolds);
        Check("development world: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);

        // ---- §10.7 determinism: same seed → byte-identical developed rosters ----
        string fpA = RunDevelopmentDeterminismWorld(scratchPath + ".detA", schemaPath);
        string fpB = RunDevelopmentDeterminismWorld(scratchPath + ".detB", schemaPath);
        Check("§10.7 determinism: two same-seed 3-year developed worlds produce byte-identical rosters, ratings and ceilings",
            fpA == fpB && fpA.Length > 0, $"fingerprint {fpA.Length} chars");
    }

    private static string RunDevelopmentDeterminismWorld(string path, string schemaPath)
    {
        using var db = new DatabaseManager(path);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var rng = new RngState(LeagueSeed + 101);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        LeagueGenerator.EnsureTierLeagues(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
        clock.Initialize(StartYear);
        bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
        var development = new DevelopmentManager(
            db, players, baseball, new GameStateQueries(db),
            new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9D0DE7UL));
        development.AttachTo(bus);
        var promo = new PromotionManager(
            db, players, baseball, new GameStateQueries(db),
            new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9C0DE7UL));
        promo.AttachTo(bus);
        for (int i = 0; i < GlobalState.DaysPerSeason * 3; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
        }

        var roster = new List<RosterPlayerRow>();
        baseball.LoadRoster(roster);
        var potentials = new Dictionary<string, PlayerPotentialRow>();
        baseball.LoadAllPotential(potentials);
        var fingerprint = new System.Text.StringBuilder(roster.Count * 64);
        foreach (RosterPlayerRow row in roster)
        {
            PlayerPotentialRow pot = potentials[row.PlayerId];
            fingerprint.Append(row.PlayerId).Append(':').Append(row.TeamId).Append(':')
                .Append(row.BatPower).Append(',').Append(row.BatContact).Append(',').Append(row.BatDiscipline).Append(',')
                .Append(row.PitStuff).Append(',').Append(row.PitControl).Append(',').Append(row.PitStamina).Append(',')
                .Append(row.Fielding).Append('|')
                .Append(pot.BatPower).Append(',').Append(pot.BatContact).Append(',').Append(pot.BatDiscipline).Append(',')
                .Append(pot.PitStuff).Append(',').Append(pot.PitControl).Append(',').Append(pot.PitStamina).Append(',')
                .Append(pot.Fielding).Append(';');
        }
        return fingerprint.ToString();
    }

    private static void RunDevelopmentStreamSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9d neutrality/stream isolation: an at-peak no-op pass leaves the world bit-identical (doc §10.1/.7) ---");

        // Two same-seed worlds — one with the development pass attached and
        // firing (but provably choosing no-op: every rostered age pinned to
        // the peak, where BOTH phase weights and the jitter scale are zero by
        // construction), one without it — must produce BIT-IDENTICAL season-2
        // games in all six tiers. Strictly stronger than §10.1's inert-without-
        // rollover clause (structural: the only subscription is the rollover),
        // this also proves §10.7's stream isolation: the pass fired, bulk-read
        // the world and drew its jitter bells from the dedicated fork without
        // perturbing any sim's stream or writing a byte.
        var batByTier = new LeagueBattingTotals[2][];
        var pitByTier = new LeaguePitchingTotals[2][];
        bool noOpRan = false;
        bool integrityOk = true;
        for (int variant = 0; variant < 2; variant++)
        {
            bool withDevelopment = variant == 1;
            string path = scratchPath + (withDevelopment ? ".dev" : ".bare");
            using var db = new DatabaseManager(path);
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var rng = new RngState(LeagueSeed + 105);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
            LeagueGenerator.EnsureTierLeagues(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
            // Pin every rostered age to the peak (27): youthWeight, ageWeight
            // and the phase-scaled jitter are all exactly zero there, so the
            // pass evaluates the full roster and writes nothing. (No aging
            // subscriber in either world — ages must STAY pinned.)
            var tierRows = new List<RosterPlayerRow>();
            db.RunInBatch(() =>
            {
                for (int t = 0; t < LeagueDirectory.TierCount; t++)
                {
                    baseball.LoadRosterByTier((LeagueTier)t, tierRows);
                    foreach (RosterPlayerRow row in tierRows)
                    {
                        players.SetAge(row.PlayerId, PromotionProfile.PeakAge);
                    }
                }
            });

            var state = new GlobalState();
            var bus = new EventBus();
            var clock = new TimeManager(db, new GameStateQueries(db), state, bus);
            clock.Initialize(StartYear);
            DevelopmentManager? development = null;
            if (withDevelopment)
            {
                development = new DevelopmentManager(
                    db, players, baseball, new GameStateQueries(db),
                    new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9D0002UL));
                development.AttachTo(bus);
            }
            // Season 1 idles gameless; the rollover fires on the last tick.
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            if (withDevelopment)
            {
                DevelopmentSummary run = development!.LastRun;
                noOpRan = run.SeasonYear == StartYear && run.PlayersChanged == 0
                    && run.PointsUp == 0 && run.PointsDown == 0;
            }
            // Fresh same-seed sims join for season 2 (days 2..365) in BOTH worlds.
            var normalizer = new StatsNormalizer(db, baseball);
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                var sim = new LeagueSimulator(
                    db, baseball, normalizer, new RngState(SeasonSeed + 60 + (ulong)t), (LeagueTier)t);
                sim.Initialize();
                sim.AttachTo(bus);
            }
            for (int i = 0; i < GlobalState.DaysPerSeason - 1; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            batByTier[variant] = new LeagueBattingTotals[LeagueDirectory.TierCount];
            pitByTier[variant] = new LeaguePitchingTotals[LeagueDirectory.TierCount];
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                batByTier[variant][t] = baseball.LoadLeagueBattingTotals(StartYear + 1, (LeagueTier)t);
                pitByTier[variant][t] = baseball.LoadLeaguePitchingTotals(StartYear + 1, (LeagueTier)t);
            }
            integrityOk &= !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0;
        }

        bool identical = true;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            identical &= BattingTotalsEqual(in batByTier[0][t], in batByTier[1][t])
                && PitchingTotalsEqual(in pitByTier[0][t], in pitByTier[1][t]);
        }
        Check("§10.1/§10.7: the pass fired at the peak-pinned rollover and provably changed nothing",
            noOpRan);
        Check("§10.1/§10.7: season 2 is BIT-IDENTICAL across all six tiers with the no-op pass attached vs absent",
            identical && integrityOk,
            $"MLB PA {batByTier[0][5].Pa}/{batByTier[1][5].Pa} H {batByTier[0][5].H}/{batByTier[1][5].H}");
    }

    // ------------------------------------------------------------------
    // Phase 9d-2: the Practice lever + the scouting seam
    // (development doc §4/§6, acceptance checks 5 and 9)
    // ------------------------------------------------------------------

    private static void RunPracticeSeamSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9d-2 Practice lever + scouting seam (doc §4/§6, acceptance checks 5/9) ---");

        // ---- pure fixtures: the §4 hours→fraction conversion ----
        double f500 = DevelopmentCurve.PracticeFraction(500);
        double f1000 = DevelopmentCurve.PracticeFraction(1000);
        double f2000 = DevelopmentCurve.PracticeFraction(2000);
        Check("§4 conversion: 0 at zero/negative hours, monotone in hours, exact at the e-fold (500h = cap·(1−1/e))",
            DevelopmentCurve.PracticeFraction(0) == 0.0 && DevelopmentCurve.PracticeFraction(-25) == 0.0
            && f1000 > f500 && f2000 > f1000
            && Math.Abs(f500 - DevelopmentProfile.PracticeFracCap * (1.0 - Math.Exp(-1.0))) < 1e-12,
            $"f(500)={f500:F6}");
        Check("§4 conversion: strictly diminishing returns; the first hour is worth PracticeFracPerHour",
            f1000 - f500 < f500 && f2000 - f1000 < f1000 - f500
            && Math.Abs(DevelopmentCurve.PracticeFraction(1) - DevelopmentProfile.PracticeFracPerHour) < 1e-6,
            $"500h slices {f500:F4} → {f1000 - f500:F4} → {f2000 - f1000:F4}");
        Check("§4 conversion: asymptotes at the seasonal cap — no grind exceeds it (you cannot grind a 40 to a 100 in one winter)",
            DevelopmentCurve.PracticeFraction(10_000) < DevelopmentProfile.PracticeFracCap
            && DevelopmentCurve.PracticeFraction(1_000_000_000) == DevelopmentProfile.PracticeFracCap);

        // ---- pure fixtures: the §6 ProjectionBonus (check 9's core) ----
        Check("§6 projection: at-ceiling young reads 0; full headroom reads the exact old AgeBonus (the fudge WAS an implicit full-headroom assumption); half headroom reads half",
            PromotionScore.ProjectionBonus(17, 0) == 0.0
            && PromotionScore.ProjectionBonus(17, PromotionProfile.HeadroomForFullProjection) == PromotionScore.AgeBonus(17)
            && PromotionScore.ProjectionBonus(17, 300) == PromotionScore.AgeBonus(17)
            && Math.Abs(PromotionScore.ProjectionBonus(17, 15) - PromotionScore.AgeBonus(17) / 2.0) < 1e-12,
            $"PB(17,30)={PromotionScore.ProjectionBonus(17, 30):F3}");
        Check("§6 projection: past the peak the age-driven decline stands regardless of paper headroom; the Scouting overload orders on headroom at equal current ratings",
            PromotionScore.ProjectionBonus(33, 300) == PromotionScore.AgeBonus(33)
            && PromotionScore.AgeBonus(33) < 0.0
            && PromotionScore.Scouting(162, 17, 90) > PromotionScore.Scouting(162, 17, 0)
            && PromotionScore.Scouting(162, 17, 0) == 100.0 * 162 / 150.0);

        // ---- §10.9 the seam in the SWEEP's S (check 9): two HS batters with
        // identical current ratings and age contest one College vacancy —
        // only stored headroom separates them. The at-ceiling twin's A (104)
        // also deliberately misses the merit-swap bar (worst incumbent 100 +
        // margin 5), so the projection the old fudge would have handed him
        // (+8.33 age bonus → A 106.8, over the bar) is provably no longer
        // fakeable: he stays down BECAUSE his ceiling says he is done. ----
        {
            using var db = new DatabaseManager(scratchPath + ".seam");
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(LeagueSeed + 212);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
            LeagueGenerator.EnsureTierLeagues(db, players, baseball, ratingSpread: 0, ref genRng);

            var hsRoster = new List<RosterPlayerRow>();
            baseball.LoadRosterByTier(LeagueTier.HS, hsRoster);
            string projectable = NthOfRole(hsRoster, PitcherRole.None, 0);
            string atCeiling = NthOfRole(hsRoster, PitcherRole.None, 1);
            int atCeilingTeam = 0;
            foreach (RosterPlayerRow row in hsRoster)
            {
                if (string.Equals(row.PlayerId, atCeiling, StringComparison.Ordinal))
                {
                    atCeilingTeam = row.TeamId;
                }
            }
            foreach (string probe in new[] { projectable, atCeiling })
            {
                players.SetAge(probe, 17);
                baseball.UpsertRatings(new PlayerRatingsRow
                {
                    PlayerId = probe, IsPitcher = false,
                    BatPower = 54, BatContact = 54, BatDiscipline = 54,
                    PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
                });
            }
            baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = projectable,
                BatPower = 84, BatContact = 84, BatDiscipline = 84,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
            baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = atCeiling,
                BatPower = 54, BatContact = 54, BatDiscipline = 54,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });

            var collegeRoster = new List<RosterPlayerRow>();
            baseball.LoadRosterByTier(LeagueTier.College, collegeRoster);
            string retiree = NthOfRole(collegeRoster, PitcherRole.None, 0);
            int vacatedTeam = 0;
            foreach (RosterPlayerRow row in collegeRoster)
            {
                if (string.Equals(row.PlayerId, retiree, StringComparison.Ordinal))
                {
                    vacatedTeam = row.TeamId;
                }
            }
            players.SetAge(retiree, HeirGenetics.HeirGeneticsProfile.MandatoryRetirementAge);

            var promo = new PromotionManager(
                db, players, baseball, new GameStateQueries(db),
                new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9D25EA11UL));
            promo.RunOffseason(StartYear);

            bool projectableRose = players.TryGetById(projectable, out PlayerRow xRow)
                && xRow.TeamId == vacatedTeam;
            bool ceilingHeld = players.TryGetById(atCeiling, out PlayerRow yRow)
                && yRow.TeamId == atCeilingTeam;
            Check("§10.9 scouting seam in the sweep: the one College vacancy goes to the HIGH-HEADROOM probe; the at-ceiling twin (identical current line and age) stays in HS",
                promo.LastRun.Removals == 1 && promo.LastRun.Promotions == 1
                && promo.LastRun.Relegations == 0 && promo.LastRun.Intake == 1
                && projectableRose && ceilingHeld,
                $"X→{xRow.TeamId} (vacated {vacatedTeam}), Y→{yRow.TeamId} (origin {atCeilingTeam})");
            Check("§10.9 seam world: roster invariant, integrity, FK, no open batch",
                TierLadderInvariantHolds(baseball, out string seamDetail)
                && !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0,
                seamDetail);
        }

        // ---- §10.5 the Practice lever end to end (check 5): two peak-pinned
        // worlds — where growth, decline AND jitter are exactly zero for
        // everyone but the practicing avatar — one banking 6 Practice hours
        // per day through the exact Game_State accumulate/consume path, one
        // idle. Every number below is an exact, jitter-free pin. ----
        var practiced = RunPracticeWorld(scratchPath + ".practiced", schemaPath, practiceHoursPerDay: 6);
        var idle = RunPracticeWorld(scratchPath + ".idle", schemaPath, practiceHoursPerDay: 0);
        Check("§10.5 accumulate path: 10 days × 6h bank exactly 60h in Game_State; the rollover consumes AND clears the credit",
            practiced.CreditAccrued && practiced.CreditCleared);
        Check("§10.5 the Practice lever: a season of 6h/day (frac 0.2469) moves the peak-pinned avatar +2 per rating; the idle twin moves ZERO; nobody overshoots the ceiling",
            practiced.BatSum == 186 && idle.BatSum == 180
            && practiced.NeverOvershot && idle.NeverOvershot,
            $"practiced {practiced.BatSum} vs idle {idle.BatSum} (start 180, ceiling 210)");
        Check("§10.5 practice is avatar-only: the practiced pass changed EXACTLY one player (+14 over 7 ratings), the idle pass is a complete no-op, every NPC bit-identical across the pair",
            practiced.PlayersChanged == 1 && practiced.PointsUp == 14 && idle.PlayersChanged == 0
            && practiced.NpcFingerprint == idle.NpcFingerprint && practiced.NpcFingerprint.Length > 0,
            $"changed {practiced.PlayersChanged}(+{practiced.PointsUp}) vs {idle.PlayersChanged}; fingerprint {practiced.NpcFingerprint.Length} chars");

        // ---- §4 the succession guard (contract, not dispatch-timing luck):
        // a retiring founder banks a season of credit; the rollover hands the
        // career to a peak-age heir whose development is EXACTLY the practice
        // term (both phase weights and jitter are zero at 27), so a leaked
        // credit would move his huge headroom by +15 per bat rating and a
        // held guard by exactly zero — deterministic, single-world. ----
        {
            using var db = new DatabaseManager(scratchPath + ".guard");
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(LeagueSeed + 211);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
            LeagueGenerator.EnsureTierLeagues(db, players, baseball, ratingSpread: 0, ref genRng);

            var state = new GlobalState();
            var bus = new EventBus();
            var gameState = new GameStateQueries(db);
            var clock = new TimeManager(db, gameState, state, bus);
            clock.Initialize(StartYear);
            var leagues = new LeagueDirectory();
            var normalizer = new StatsNormalizer(db, baseball);
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                var sim = new LeagueSimulator(
                    db, baseball, normalizer, new RngState(SeasonSeed + 90 + (ulong)t), (LeagueTier)t);
                sim.Initialize();
                leagues.Register(sim); // registered for the avatar plumbing, not attached — gameless
            }
            var micro = new MicroGame(db, baseball);
            micro.Initialize();
            var career = new CareerManager(
                db, players, baseball, gameState, state, leagues, micro, new RngState(0x9D2FADEUL));
            career.AttachTo(bus); // the REAL succession path runs at the rollover
            var development = new DevelopmentManager(
                db, players, baseball, gameState, leagues, micro, new RngState(0x9D2DE72UL));
            development.Career = career;
            development.AttachTo(bus); // after Career — the doc §5 order

            career.CreateAvatar("Fading", "Founder", 101, new PlayerRatingsRow
            {
                IsPitcher = false,
                BatPower = 60, BatContact = 60, BatDiscipline = 60,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
            players.SetAge(career.AvatarPlayerId, 41); // → 42 at the rollover: retirement trigger
            string heirId = career.ConceiveChild("Heir", "Founder", birthAge: PromotionProfile.PeakAge - 1);
            players.SetBaseballInterest(heirId, 100);
            baseball.UpsertRatings(new PlayerRatingsRow
            {
                PlayerId = heirId, IsPitcher = false,
                BatPower = 30, BatContact = 30, BatDiscipline = 30,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
            baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = heirId,
                BatPower = 90, BatContact = 90, BatDiscipline = 90,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });

            bool banked = false;
            bus.Subscribe<DayAdvancedEvent>(
                _ => gameState.AdjustInt64(GameStateKeys.AvatarPracticeCredit, 6));
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
                if (i == 9)
                {
                    banked = gameState.TryGetInt64(GameStateKeys.AvatarPracticeCredit, out long b) && b == 60;
                }
            }

            bool succeeded = career.LastSuccession.Kind == SuccessionOutcomeKind.Succeeded
                && string.Equals(career.AvatarPlayerId, heirId, StringComparison.Ordinal);
            bool keyCleared = gameState.TryGetInt64(GameStateKeys.AvatarPracticeCredit, out long left)
                && left == 0;
            baseball.TryGetRatings(heirId, out PlayerRatingsRow heirRow);
            Check("§4 succession guard: the founder's 2,190 banked hours are discarded at the handoff — the peak-age heir's 60-point-per-rating headroom develops by EXACTLY zero and the key clears",
                succeeded && banked && keyCleared
                && heirRow.BatPower == 30 && heirRow.BatContact == 30 && heirRow.BatDiscipline == 30,
                $"heir bat {heirRow.BatPower}/{heirRow.BatContact}/{heirRow.BatDiscipline}, cleared {keyCleared}");
            Check("§4 guard world: integrity ok, no FK violations, no open batch",
                !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
        }
    }

    // ------------------------------------------------------------------
    // Phase 9d §7: the tuning-harness equilibrium — the central 9d
    // deliverable (development doc §7/§10.6, acceptance check 6). Runs the
    // ladder for 80 simulated offseasons (aging → development → promotion,
    // the doc §5 order) and proves the population's talent distribution
    // settles to a STATIONARY, MONOTONE gradient across all six tiers — the
    // 9c "middle-minors flatness" (§10.9's own disclosure: no age caps and no
    // retirements until the founding generation ages through, so the middle
    // rungs hover at the generation mean) perturbed into a real ladder by
    // development moving ratings every year.
    // ------------------------------------------------------------------

    private static void RunDevelopmentEquilibriumSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Phase 9d §7 tuning harness: 80-offseason talent equilibrium (doc §7/§10.6, acceptance check 6) ---");

        // No LeagueSimulators attached — the same "no games" convention as
        // RunPromotionSweepSuite/RunDevelopmentArcSuite. With no season line,
        // P (performance) shrinks to a constant 100 for every candidate, so
        // PromotionScore.Combine(100, S) is a monotone affine function of S
        // alone — ranking (and therefore every promotion/merit-swap decision)
        // is IDENTICAL to a real-games world where P varies, just 5x faster
        // (proven empirically against a real-games run at development-doc
        // review time: same seed, same constants, talent means agree within
        // ~2 points and the monotonicity/stationarity verdict is unchanged).
        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed + 900);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);
        LeagueGenerator.EnsureTierLeagues(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);

        var state = new GlobalState();
        var bus = new EventBus();
        var gameState = new GameStateQueries(db);
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);
        bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
        var development = new DevelopmentManager(
            db, players, baseball, gameState, new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9D7000UL));
        development.AttachTo(bus);
        var promo = new PromotionManager(
            db, players, baseball, gameState, new LeagueDirectory(), new MicroGame(db, baseball), new RngState(0x9C7000UL));
        promo.AttachTo(bus);

        const int Years = 80;
        const int Warmup = 30; // discard the first 30 offseasons (transient from the flat generated-world start)
        string[] tierNames = { "HS", "College", "MinorA", "MinorAA", "MinorAAA", "MLB" };
        var talentHistory = new List<double[]>(Years);
        var conservationFailures = new List<string>();
        var roster = new List<RosterPlayerRow>();

        bus.Subscribe<SeasonRolledOverEvent>(_ =>
        {
            if (!TierLadderInvariantHolds(baseball, out string detail))
            {
                conservationFailures.Add(detail);
            }
        });

        for (int gen = 0; gen < Years; gen++)
        {
            for (int i = 0; i < GlobalState.DaysPerSeason; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }

            var talent = new double[LeagueDirectory.TierCount];
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                baseball.LoadRosterByTier((LeagueTier)t, roster);
                long sum = 0;
                int batters = 0;
                foreach (RosterPlayerRow row in roster)
                {
                    if (row.Role == PitcherRole.None)
                    {
                        sum += row.BatPower + row.BatContact + row.BatDiscipline;
                        batters++;
                    }
                }
                talent[t] = batters > 0 ? (double)sum / batters : 0.0;
            }
            talentHistory.Add(talent);
        }

        Check("§10.2 conservation holds through all 80 developed+promoted offseasons: 48 teams × exactly 9+5+3, 816 rostered",
            conservationFailures.Count == 0,
            conservationFailures.Count > 0 ? conservationFailures[0] : "80 rollovers clean");

        // ---- time-averaged equilibrium over the post-warmup window ----
        double[] mean = new double[LeagueDirectory.TierCount];
        for (int gen = Warmup; gen < Years; gen++)
        {
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                mean[t] += talentHistory[gen][t] / (Years - Warmup);
            }
        }
        Console.WriteLine("  time-averaged mean batter talent, generations " + (Warmup + 1) + ".." + Years + ": " +
            string.Join("  ", Enumerable.Range(0, LeagueDirectory.TierCount).Select(t => $"{tierNames[t]}={mean[t]:F1}")));

        bool monotone = true;
        for (int t = 1; t < LeagueDirectory.TierCount; t++)
        {
            monotone &= mean[t] > mean[t - 1];
        }
        Check("§7/§10.6 the middle-minors flatness is perturbed into a real gradient: mean batter talent is strictly monotone HS→MLB across all six tiers",
            monotone,
            string.Join(" < ", Enumerable.Range(0, LeagueDirectory.TierCount).Select(t => $"{tierNames[t]} {mean[t]:F1}")));

        // ---- stationarity: split the measurement window in half; the
        // steady state should not still be visibly drifting ----
        int mid = Warmup + (Years - Warmup) / 2;
        double[] firstHalf = new double[LeagueDirectory.TierCount];
        double[] secondHalf = new double[LeagueDirectory.TierCount];
        for (int gen = Warmup; gen < mid; gen++)
        {
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                firstHalf[t] += talentHistory[gen][t] / (mid - Warmup);
            }
        }
        for (int gen = mid; gen < Years; gen++)
        {
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                secondHalf[t] += talentHistory[gen][t] / (Years - mid);
            }
        }
        bool stationary = true;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            stationary &= Math.Abs(secondHalf[t] - firstHalf[t]) < 10.0;
        }
        Check("§7 a stationary window exists: first-half vs second-half of the measurement window drifts less than 10 points per tier",
            stationary,
            string.Join("  ", Enumerable.Range(0, LeagueDirectory.TierCount)
                .Select(t => $"{tierNames[t]} {secondHalf[t] - firstHalf[t]:+0.0;-0.0}")));

        Check("equilibrium world: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
    }

    /// <summary>
    /// One §10.5 world: every rostered age pinned to the peak (the
    /// stream-suite trick — growth, decline AND jitter are exactly zero
    /// there, and stay zero through the small age drift the rollover adds),
    /// then a modest-build avatar (bat 60s, ceiling 70s via the creation
    /// headroom) aged to hit the peak AT the rollover — his development is
    /// jitter-free and exactly the practice term. Pinning matters twice:
    /// avatar ids are wall-clock GUIDs, and an unpinned world would inherit
    /// that non-determinism through the roster-ordered jitter draw sequence.
    /// CareerManager owns the avatar but is deliberately NOT attached (no
    /// succession, no games); an aging subscriber and the development pass
    /// do the rollover work in the doc §5 order; a DayAdvancedEvent
    /// subscriber banks the day's Practice hours through the exact additive
    /// Game_State call the GameManager bridge uses.
    /// </summary>
    private static (int BatSum, bool NeverOvershot, bool CreditAccrued, bool CreditCleared,
        int PlayersChanged, int PointsUp, string NpcFingerprint)
        RunPracticeWorld(string path, string schemaPath, int practiceHoursPerDay)
    {
        using var db = new DatabaseManager(path);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed + 210);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        LeagueGenerator.EnsureTierLeagues(db, players, baseball, ratingSpread: 0, ref genRng);

        var tierRows = new List<RosterPlayerRow>();
        db.RunInBatch(() =>
        {
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                baseball.LoadRosterByTier((LeagueTier)t, tierRows);
                foreach (RosterPlayerRow row in tierRows)
                {
                    players.SetAge(row.PlayerId, PromotionProfile.PeakAge);
                }
            }
        });

        var state = new GlobalState();
        var bus = new EventBus();
        var gameState = new GameStateQueries(db);
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);
        var leagues = new LeagueDirectory();
        var normalizer = new StatsNormalizer(db, baseball);
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var sim = new LeagueSimulator(
                db, baseball, normalizer, new RngState(SeasonSeed + 80 + (ulong)t), (LeagueTier)t);
            sim.Initialize();
            leagues.Register(sim); // registered for the avatar plumbing, NOT attached — gameless
        }
        var micro = new MicroGame(db, baseball);
        micro.Initialize();
        var career = new CareerManager(
            db, players, baseball, gameState, state, leagues, micro, new RngState(0x9D2CAFEUL));
        career.CreateAvatar("Practice", "Prospect", 101, new PlayerRatingsRow
        {
            IsPitcher = false,
            BatPower = 60, BatContact = 60, BatDiscipline = 60,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
        });
        string avatarId = career.AvatarPlayerId;
        // Creation rolled the ceiling at 19 (+10 headroom per rating); the
        // avatar then ages to EXACTLY the peak at the rollover, where his
        // growth fraction is the practice term alone.
        players.SetAge(avatarId, PromotionProfile.PeakAge - 1);

        bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
        var development = new DevelopmentManager(
            db, players, baseball, gameState, leagues, micro, new RngState(0x9D2DE71UL));
        development.Career = career;
        development.AttachTo(bus);

        if (practiceHoursPerDay > 0)
        {
            bus.Subscribe<DayAdvancedEvent>(
                _ => gameState.AdjustInt64(GameStateKeys.AvatarPracticeCredit, practiceHoursPerDay));
        }
        bool creditAccrued = practiceHoursPerDay == 0;
        bool creditCleared = true;
        bus.Subscribe<SeasonRolledOverEvent>(_ =>
        {
            // Subscribed AFTER development: the pass has consumed and cleared.
            if (practiceHoursPerDay > 0)
            {
                creditCleared &= gameState.TryGetInt64(GameStateKeys.AvatarPracticeCredit, out long left)
                    && left == 0;
            }
        });

        for (int i = 0; i < GlobalState.DaysPerSeason; i++)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
            if (i == 9 && practiceHoursPerDay > 0)
            {
                creditAccrued = gameState.TryGetInt64(GameStateKeys.AvatarPracticeCredit, out long banked)
                    && banked == 10L * practiceHoursPerDay;
            }
        }

        baseball.TryGetRatings(avatarId, out PlayerRatingsRow avatarRatings);
        bool neverOvershot = baseball.TryGetPotential(avatarId, out PlayerPotentialRow ceiling)
            && avatarRatings.BatPower <= ceiling.BatPower
            && avatarRatings.BatContact <= ceiling.BatContact
            && avatarRatings.BatDiscipline <= ceiling.BatDiscipline;

        var roster = new List<RosterPlayerRow>();
        baseball.LoadRoster(roster);
        var fingerprint = new System.Text.StringBuilder(roster.Count * 32);
        foreach (RosterPlayerRow row in roster)
        {
            if (string.Equals(row.PlayerId, avatarId, StringComparison.Ordinal))
            {
                continue; // world-local GUID — the NPC set is what must match across the pair
            }
            fingerprint.Append(row.PlayerId).Append(':').Append(row.TeamId).Append(':')
                .Append(row.BatPower).Append(',').Append(row.BatContact).Append(',').Append(row.BatDiscipline).Append(',')
                .Append(row.PitStuff).Append(',').Append(row.PitControl).Append(',').Append(row.PitStamina).Append(',')
                .Append(row.Fielding).Append(';');
        }
        return (avatarRatings.BatPower + avatarRatings.BatContact + avatarRatings.BatDiscipline,
            neverOvershot, creditAccrued, creditCleared,
            development.LastRun.PlayersChanged, development.LastRun.PointsUp, fingerprint.ToString());
    }

    // ------------------------------------------------------------------
    // Phase 10c: Scouting grade curve (presentation_layer_narrative.md §5)
    // ------------------------------------------------------------------

    private static void RunScoutingGradeSuite()
    {
        Console.WriteLine("--- Phase 10c scouting grade curve fixtures (doc §5.2/§5.3, acceptance check 4 — pure, no db) ---");

        Check("§5.2 band boundaries: 49→C, 50→C+, 89→A, 90→A+, 0→F, 100→A+",
            ScoutingGrade.Grade(49) == GradeLetter.C && ScoutingGrade.Grade(50) == GradeLetter.CPlus
            && ScoutingGrade.Grade(89) == GradeLetter.A && ScoutingGrade.Grade(90) == GradeLetter.APlus
            && ScoutingGrade.Grade(0) == GradeLetter.F && ScoutingGrade.Grade(100) == GradeLetter.APlus);
        Check("§5.2 every other boundary (39/40, 59/60, 69/70, 79/80, 29/30)",
            ScoutingGrade.Grade(39) == GradeLetter.D && ScoutingGrade.Grade(40) == GradeLetter.C
            && ScoutingGrade.Grade(59) == GradeLetter.CPlus && ScoutingGrade.Grade(60) == GradeLetter.B
            && ScoutingGrade.Grade(69) == GradeLetter.B && ScoutingGrade.Grade(70) == GradeLetter.BPlus
            && ScoutingGrade.Grade(79) == GradeLetter.BPlus && ScoutingGrade.Grade(80) == GradeLetter.A
            && ScoutingGrade.Grade(29) == GradeLetter.F && ScoutingGrade.Grade(30) == GradeLetter.D);
        Check("§5.2 above/below the scale still saturate (no clamp needed): -10→F, 150→A+",
            ScoutingGrade.Grade(-10) == GradeLetter.F && ScoutingGrade.Grade(150) == GradeLetter.APlus);
        Check("§5.2 Label() renders the exact eight strings in band order",
            ScoutingGrade.Label(GradeLetter.F) == "F" && ScoutingGrade.Label(GradeLetter.D) == "D"
            && ScoutingGrade.Label(GradeLetter.C) == "C" && ScoutingGrade.Label(GradeLetter.CPlus) == "C+"
            && ScoutingGrade.Label(GradeLetter.B) == "B" && ScoutingGrade.Label(GradeLetter.BPlus) == "B+"
            && ScoutingGrade.Label(GradeLetter.A) == "A" && ScoutingGrade.Label(GradeLetter.APlus) == "A+");

        // ---- §5.3 present vs. future: a 17-year-old with a present-C bat and a future-A bat, the headroom story ----
        Check("§5.3 present/future: current 45 (C) vs potential 82 (A) renders as two different grades",
            ScoutingGrade.Grade(45) == GradeLetter.C && ScoutingGrade.Grade(82) == GradeLetter.A);

        // ---- §5.3 OFP. DISCLOSED FINDING (not silently patched — flagged for
        // Fable/Opus, see ScoutingGrade.OfpRating's doc comment): the doc's
        // literal "Grade(round(Scouting(...)))" over-grades almost everyone,
        // because PromotionScore.Scouting is 100-centred (Combine's own
        // ranking convention — an exactly-average peak-age player already
        // scores 100.0), not Grade's 0-100/50-average domain. Halving
        // recenters it exactly. ----
        int peakAvgOfp = ScoutingGrade.OfpRating(roleRatingSum: 150, age: 27, headroom: 0); // 3×50 = exactly league average
        Check("§5.3 OFP recentering: an exactly-average peak-age player with no headroom projects exactly 50 (C+)",
            peakAvgOfp == 50 && ScoutingGrade.Grade(peakAvgOfp) == GradeLetter.CPlus, $"OFP {peakAvgOfp}");

        int replacementOfp = ScoutingGrade.OfpRating(roleRatingSum: 120, age: 27, headroom: 0); // 3×40, the 8c call-up floor
        Check("§5.3 OFP recentering: a peak-age replacement-level player (3×40) projects exactly 40 (C)",
            replacementOfp == 40 && ScoutingGrade.Grade(replacementOfp) == GradeLetter.C, $"OFP {replacementOfp}");

        int youngProspectOfp = ScoutingGrade.OfpRating(roleRatingSum: 120, age: 15, headroom: 30); // saturated headroom, full age bonus
        int youngNoHeadroomOfp = ScoutingGrade.OfpRating(roleRatingSum: 120, age: 15, headroom: 0);
        Check("§5.3 OFP ties to the real sweep math: a 15-year-old with saturated headroom projects above his raw floor; one with none doesn't",
            youngProspectOfp > replacementOfp && youngNoHeadroomOfp == replacementOfp,
            $"headroom {youngProspectOfp} vs floor {replacementOfp} vs no-headroom {youngNoHeadroomOfp}");

        // Direct equivalence against the exact (disclosed, recentered) formula.
        double rawScouting = PromotionScore.Scouting(150, 27, 0);
        int expectedOfp = (int)Math.Round(rawScouting / 2.0, MidpointRounding.AwayFromZero);
        Check("§5.3 OFP is exactly Grade(round(Scouting(roleRatingSum, age, headroom)/2)) — literal formula check",
            ScoutingGrade.OfpRating(150, 27, 0) == expectedOfp
            && ScoutingGrade.OfpGrade(150, 27, 0) == ScoutingGrade.Grade(expectedOfp));
    }

    /// <summary>Nth rostered player of a role in load order (team_id, player_id) — deterministic fixture picks.</summary>
    private static string NthOfRole(List<RosterPlayerRow> roster, PitcherRole role, int index)
    {
        int seen = 0;
        foreach (RosterPlayerRow row in roster)
        {
            if (row.Role == role && seen++ == index)
            {
                return row.PlayerId;
            }
        }
        throw new InvalidOperationException($"No {role} at index {index} in a {roster.Count}-row roster.");
    }

    private static (bool Rostered, LeagueTier Tier) RosteredTierOf(
        PlayerQueries players, BaseballQueries baseball, string playerId)
    {
        if (!players.TryGetById(playerId, out PlayerRow row) || !row.TeamId.HasValue)
        {
            return (false, default);
        }
        baseball.TryGetTeamTier(row.TeamId.Value, out LeagueTier tier);
        return (true, tier);
    }

    /// <summary>
    /// The 9c conservation law (§10.2): 48 teams, every team exactly
    /// 9 batters + 5 starters + 3 relievers, 816 rostered in total.
    /// </summary>
    private static bool TierLadderInvariantHolds(BaseballQueries baseball, out string detail)
    {
        var teams = new List<TeamRow>();
        baseball.LoadAllTeams(teams);
        if (teams.Count != LeagueDirectory.TierCount * LeagueSimulator.TeamCount)
        {
            detail = $"{teams.Count} teams";
            return false;
        }
        var roster = new List<RosterPlayerRow>();
        baseball.LoadRoster(roster);
        var counts = new Dictionary<int, (int Batters, int Starters, int Relievers)>(teams.Count);
        foreach (TeamRow team in teams)
        {
            counts[team.TeamId] = default;
        }
        foreach (RosterPlayerRow row in roster)
        {
            if (!counts.TryGetValue(row.TeamId, out (int Batters, int Starters, int Relievers) c))
            {
                detail = $"player {row.PlayerId} on unknown team {row.TeamId}";
                return false;
            }
            counts[row.TeamId] = row.Role switch
            {
                PitcherRole.None => (c.Batters + 1, c.Starters, c.Relievers),
                PitcherRole.Starter => (c.Batters, c.Starters + 1, c.Relievers),
                _ => (c.Batters, c.Starters, c.Relievers + 1),
            };
        }
        foreach (KeyValuePair<int, (int Batters, int Starters, int Relievers)> entry in counts)
        {
            if (entry.Value != (LeagueSimulator.LineupSize, LeagueSimulator.RotationSize, LeagueSimulator.BullpenSize))
            {
                detail = $"team {entry.Key} = {entry.Value.Batters}/{entry.Value.Starters}/{entry.Value.Relievers}";
                return false;
            }
        }
        if (roster.Count != LeagueDirectory.TierCount * LeagueSimulator.TeamCount * LeagueSimulator.RosterSizePerTeam)
        {
            detail = $"{roster.Count} rostered";
            return false;
        }
        detail = $"{roster.Count} rostered across {teams.Count} teams";
        return true;
    }

    private static int SeasonPa(PlayerQueries players, string playerId)
    {
        var seasons = new List<BattingStatsRow>();
        players.LoadBattingSeasons(playerId, seasons);
        int pa = 0;
        foreach (BattingStatsRow row in seasons)
        {
            pa += row.Pa;
        }
        return pa;
    }

    private static int SeasonG(PlayerQueries players, string playerId)
    {
        var seasons = new List<PitchingStatsRow>();
        players.LoadPitchingSeasons(playerId, seasons);
        int g = 0;
        foreach (PitchingStatsRow row in seasons)
        {
            g += row.G;
        }
        return g;
    }

    /// <summary>
    /// Wraps a single simulator as the directory CareerManager takes since 9a
    /// (sparse registration is the harness contract — fixtures that exercise
    /// one tier register just that sim).
    /// </summary>
    private static LeagueDirectory Solo(LeagueSimulator league)
    {
        var directory = new LeagueDirectory();
        directory.Register(league);
        return directory;
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
