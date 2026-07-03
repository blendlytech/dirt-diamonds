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

        try
        {
            RunResolverFixtures();
            RunMonteCarloBatch(paCount);
            RunSeasonPipeline(schemaPath, scratchPath);
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
