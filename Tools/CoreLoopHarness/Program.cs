using System.Diagnostics;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Tools.CoreLoopHarness;

/// <summary>
/// Phase 2 exit-criteria harness: proves the event bus contract, drives the
/// headless "advance 365 days" run against a scratch database, verifies the
/// calendar survives a save/reopen cycle, and scans the Simulation tree for
/// illegal Life↔Baseball cross-references.
///
/// Usage: dotnet run --project Tools/CoreLoopHarness [-c Release] [-- --repo &lt;path&gt;]
/// (repo root is auto-detected by walking up to project.godot when omitted).
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();

    private static int Main(string[] args)
    {
        string? repoRoot = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--repo")
            {
                repoRoot = args[i + 1];
            }
        }
        repoRoot ??= FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Could not locate project.godot above the working directory; pass --repo <path>.");
            return 2;
        }

        string schemaPath = Path.Combine(repoRoot, "Assets", "Data", "Database", "SchemaDefinitions.sql");
        string scratchPath = Path.Combine(Path.GetTempPath(), $"dnd_coreloop_{Guid.NewGuid():N}.db");

        try
        {
            RunEventBusChecks();
            RunCalendarChecks(schemaPath, scratchPath);
            RunBoundaryScan(repoRoot);
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
    // Event bus contract
    // ------------------------------------------------------------------

    private static void RunEventBusChecks()
    {
        Console.WriteLine("--- Event bus contract ---");

        // Deferred dispatch: publish must never invoke inline.
        {
            var bus = new EventBus();
            int seen = 0;
            bus.Subscribe<DayAdvancedEvent>(_ => seen++);
            bus.Publish(new DayAdvancedEvent(1, 2026, 1));
            Check("publish defers (no inline invoke)", seen == 0 && bus.PendingCount == 1);
            int dispatched = bus.DispatchPending();
            Check("pump delivers exactly the queue", dispatched == 1 && seen == 1 && bus.PendingCount == 0);
        }

        // Global FIFO across event types.
        {
            var bus = new EventBus();
            var order = new List<string>();
            bus.Subscribe<DayAdvancedEvent>(e => order.Add($"day{e.Day}"));
            bus.Subscribe<SeasonRolledOverEvent>(e => order.Add($"roll{e.NewSeasonYear}"));
            bus.Publish(new DayAdvancedEvent(365, 2026, 365));
            bus.Publish(new SeasonRolledOverEvent(2026, 2027));
            bus.Publish(new DayAdvancedEvent(366, 2027, 1));
            bus.DispatchPending();
            Check("publication order preserved across types",
                string.Join(",", order) == "day365,roll2027,day366", string.Join(",", order));
        }

        // Unsubscribe stops delivery; double-unsubscribe reports false.
        {
            var bus = new EventBus();
            int seen = 0;
            Action<DayAdvancedEvent> handler = _ => seen++;
            bus.Subscribe(handler);
            bus.Publish(new DayAdvancedEvent(1, 2026, 1));
            bus.DispatchPending();
            bool removed = bus.Unsubscribe(handler);
            bool removedAgain = bus.Unsubscribe(handler);
            bus.Publish(new DayAdvancedEvent(2, 2026, 2));
            bus.DispatchPending();
            Check("unsubscribe stops delivery", seen == 1 && removed && !removedAgain);
        }

        // A handler's follow-up publish joins the same drain.
        {
            var bus = new EventBus();
            var order = new List<string>();
            bus.Subscribe<SeasonRolledOverEvent>(e =>
            {
                order.Add("roll");
                bus.Publish(new DayAdvancedEvent(1, e.NewSeasonYear, 1));
            });
            bus.Subscribe<DayAdvancedEvent>(_ => order.Add("cascade"));
            bus.Publish(new SeasonRolledOverEvent(2026, 2027));
            int dispatched = bus.DispatchPending();
            Check("handler-published events drain in the same pump",
                dispatched == 2 && string.Join(",", order) == "roll,cascade");
        }

        // Runaway cycle trips the cap instead of hanging.
        {
            var bus = new EventBus();
            bus.Subscribe<DayAdvancedEvent>(e => bus.Publish(new DayAdvancedEvent(e.Day, e.SeasonYear, e.DayOfSeason)));
            bus.Publish(new DayAdvancedEvent(1, 2026, 1));
            bool threw = false;
            try
            {
                bus.DispatchPending();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Check("event cycle trips MaxEventsPerPump", threw);
        }

        // Pumping from inside a handler is a loud error.
        {
            var bus = new EventBus();
            bus.Subscribe<DayAdvancedEvent>(_ => bus.DispatchPending());
            bus.Publish(new DayAdvancedEvent(1, 2026, 1));
            bool threw = false;
            try
            {
                bus.DispatchPending();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Check("reentrant DispatchPending throws", threw);
        }

        // Zero-allocation steady state: warmed publish+pump must not allocate.
        {
            var bus = new EventBus();
            long sink = 0;
            bus.Subscribe<DayAdvancedEvent>(e => sink += e.Day);
            for (int i = 0; i < 50_000; i++)
            {
                bus.Publish(new DayAdvancedEvent(i, 2026, 1));
                bus.DispatchPending();
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                bus.Publish(new DayAdvancedEvent(i, 2026, 1));
                bus.DispatchPending();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("warm publish+pump allocates zero bytes", allocated == 0,
                $"{allocated} B over 10k events (sink {sink})");
        }
    }

    // ------------------------------------------------------------------
    // Calendar: seed, 365-day advance, persistence across reopen
    // ------------------------------------------------------------------

    private static void RunCalendarChecks(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- Calendar & time advancement ---");

        const int startYear = 2026;
        const string probeKey = "harness_probe_day";

        using (var db = new DatabaseManager(scratchPath))
        {
            db.InitializeSchema(schemaPath);
            Check("scratch schema applies at v2", db.GetSchemaVersion() == 2, $"user_version={db.GetSchemaVersion()}");

            var gameState = new GameStateQueries(db);

            // Game_State typed access on the ANY column.
            gameState.SetInt64("harness_int", 42);
            gameState.SetText("harness_text", "diamonds");
            bool intOk = gameState.TryGetInt64("harness_int", out long intVal) && intVal == 42;
            bool textOk = gameState.TryGetText("harness_text", out string textVal) && textVal == "diamonds";
            bool absentOk = !gameState.TryGetInt64("harness_missing", out _);
            bool mismatchThrew = false;
            try
            {
                gameState.TryGetInt64("harness_text", out _);
            }
            catch (InvalidOperationException)
            {
                mismatchThrew = true;
            }
            Check("Game_State preserves native types", intOk && textOk && absentOk,
                $"int={intVal} text='{textVal}'");
            Check("type mismatch throws instead of reseeding", mismatchThrew);

            var state = new GlobalState();
            var bus = new EventBus();
            var clock = new TimeManager(db, gameState, state, bus);

            // Advancing before Initialize is a programming error.
            bool uninitThrew = false;
            try
            {
                clock.AdvanceDay();
            }
            catch (InvalidOperationException)
            {
                uninitThrew = true;
            }
            Check("AdvanceDay before Initialize throws", uninitThrew);

            clock.Initialize(startYear);
            Check("new save seeds day 1 of start year",
                state.CurrentDay == 1 && state.SeasonYear == startYear && state.DayOfSeason == 1,
                $"day={state.CurrentDay} season={state.SeasonYear} dos={state.DayOfSeason}");

            // Subscribers for the 365-day run. The probe handler opens its own
            // batch per day — proof that handlers run after the tick's
            // transaction committed and commit separately (the Life/Baseball
            // isolation pattern); it would throw "batch already active" if
            // dispatch ever happened inside the tick's batch.
            int dayEvents = 0;
            int rolloverEvents = 0;
            long lastDay = state.CurrentDay;
            bool fieldsConsistent = true;
            SeasonRolledOverEvent lastRollover = default;
            bus.Subscribe<DayAdvancedEvent>(e =>
            {
                dayEvents++;
                fieldsConsistent &= e.Day == lastDay + 1
                    && e.SeasonYear == startYear + (int)((e.Day - 1) / GlobalState.DaysPerSeason)
                    && e.DayOfSeason == (int)((e.Day - 1) % GlobalState.DaysPerSeason) + 1;
                lastDay = e.Day;
                db.RunInBatch(() => gameState.SetInt64(probeKey, e.Day));
            });
            bus.Subscribe<SeasonRolledOverEvent>(e =>
            {
                rolloverEvents++;
                lastRollover = e;
            });

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 365; i++)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            stopwatch.Stop();

            Check("365 DayAdvanced events, fields consistent", dayEvents == 365 && fieldsConsistent,
                $"events={dayEvents}");
            Check("exactly one season rollover (2026 -> 2027)",
                rolloverEvents == 1 && lastRollover.PreviousSeasonYear == startYear && lastRollover.NewSeasonYear == startYear + 1,
                $"rollovers={rolloverEvents}");
            Check("calendar landed on day 366 = season 2, day 1",
                state.CurrentDay == 366 && state.SeasonYear == startYear + 1 && state.DayOfSeason == 1,
                $"day={state.CurrentDay} season={state.SeasonYear} dos={state.DayOfSeason}");
            Check("no batch left open after the run", !db.IsBatchActive);

            bool persisted = gameState.TryGetInt64(GameStateKeys.CurrentDay, out long storedDay) && storedDay == 366;
            bool probed = gameState.TryGetInt64(probeKey, out long probeDay) && probeDay == 366;
            Check("current_day persisted through per-tick batches", persisted, $"stored={storedDay}");
            Check("subscriber wrote its own batches post-commit", probed, $"probe={probeDay}");

            Console.WriteLine($"  365-day headless advance: {stopwatch.ElapsedMilliseconds} ms " +
                $"({365.0 * 1000 / Math.Max(1, stopwatch.ElapsedMilliseconds):F0} days/sec, " +
                "one calendar batch + one subscriber batch per tick)");
        }

        // Reopen: the calendar must load, not reseed — even when Initialize is
        // handed a different start year.
        using (var db = new DatabaseManager(scratchPath))
        {
            var gameState = new GameStateQueries(db);
            var state = new GlobalState();
            var clock = new TimeManager(db, gameState, state, new EventBus());
            clock.Initialize(1999);
            Check("reopen loads persisted calendar (no reseed)",
                state.CurrentDay == 366 && state.StartSeasonYear == startYear,
                $"day={state.CurrentDay} startYear={state.StartSeasonYear}");
        }
    }

    // ------------------------------------------------------------------
    // Architectural boundary: zero Life<->Baseball cross-references
    // ------------------------------------------------------------------

    private static void RunBoundaryScan(string repoRoot)
    {
        Console.WriteLine("--- Life<->Baseball boundary scan ---");
        string lifeDir = Path.Combine(repoRoot, "Assets", "Simulation", "Life");
        string baseballDir = Path.Combine(repoRoot, "Assets", "Simulation", "Baseball");

        List<string> lifeOffenders = ScanForReference(lifeDir, "Simulation.Baseball");
        List<string> baseballOffenders = ScanForReference(baseballDir, "Simulation.Life");

        Check("Life sources never reference Simulation.Baseball", lifeOffenders.Count == 0,
            string.Join(", ", lifeOffenders));
        Check("Baseball sources never reference Simulation.Life", baseballOffenders.Count == 0,
            string.Join(", ", baseballOffenders));
    }

    private static List<string> ScanForReference(string directory, string forbidden)
    {
        var offenders = new List<string>();
        if (!Directory.Exists(directory))
        {
            return offenders;
        }
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (File.ReadAllText(file).Contains(forbidden, StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }
        return offenders;
    }

    // ------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------

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
