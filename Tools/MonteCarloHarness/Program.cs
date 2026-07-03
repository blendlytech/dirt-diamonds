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

        try
        {
            RunResolverFixtures();
            RunMonteCarloBatch(paCount);
            RunSeasonPipeline(schemaPath, scratchPath);
            RunMicroAnalyticSuite();
            RunMicroGameSuite(schemaPath, microScratchPath);
            RunCareerWiringSuite(schemaPath, careerScratchPath);
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
        Check("scratch schema applies at v3", db.GetSchemaVersion() == 3, $"user_version={db.GetSchemaVersion()}");

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
        // pitch PA (count chain + BIP split) under the neutral policy must
        // reproduce the 7-way macro distribution. Sampling tolerances sized to
        // the draw counts; the binding exactness proof is the analytic pin above.
        var neutral = new NeutralBatterPolicy();
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
                    fixtureAnchor, in rates, ref neutral, ref idleFatigue, ref chainRng, out int paPitches);
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
                fixtureAnchor, in warmRates, ref neutral, ref profileFatigue, ref chainRng, out int p) + p;
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            PitchClassRates warmRates = PitchChain.SolveNeutral(
                fixtureAnchor[(int)PaOutcome.Walk], fixtureAnchor[(int)PaOutcome.Strikeout]);
            sink += (long)PitchChain.SimulatePa(
                fixtureAnchor, in warmRates, ref neutral, ref profileFatigue, ref chainRng, out int p) + p;
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

        const int fatigueGames = 1500;
        var fatigueOn = new MicroGame(db, baseball) { FatigueEnabled = true, LoggingEnabled = false };
        fatigueOn.Initialize();
        var fatigueOff = new MicroGame(db, baseball) { FatigueEnabled = false, LoggingEnabled = false };
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
        var uiDone = false;
        int snapshots = 0;
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
                        bridge.SubmitSwing(0.0); // on-time swing
                    }
                    else
                    {
                        bridge.SubmitTake();
                    }
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
