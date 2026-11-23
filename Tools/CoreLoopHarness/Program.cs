using System.Diagnostics;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

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
        string postseasonPath = Path.Combine(Path.GetTempPath(), $"dnd_coreloop_post_{Guid.NewGuid():N}.db");
        string rosterPath = Path.Combine(Path.GetTempPath(), $"dnd_coreloop_roster_{Guid.NewGuid():N}.db");

        try
        {
            RunEventBusChecks();
            RunCalendarChecks(schemaPath, scratchPath);
            RunPostseasonClockChecks(schemaPath, postseasonPath);
            RunRosterClockChecks(schemaPath, rosterPath);
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
            TryDelete(postseasonPath);
            TryDelete(postseasonPath + "-wal");
            TryDelete(postseasonPath + "-shm");
            TryDelete(rosterPath);
            TryDelete(rosterPath + "-wal");
            TryDelete(rosterPath + "-shm");
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
            Check("scratch schema applies at v14", db.GetSchemaVersion() == 14, $"user_version={db.GetSchemaVersion()}");

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

    // ------------------------------------------------------------------
    // 13-4: the postseason clock (brief §4.4, §5 CoreLoop row)
    // ------------------------------------------------------------------

    /// <summary>
    /// The real clock, career driver and PlayoffManager on a scratch world: a
    /// synthetic Pro regular season the avatar's team dominates (so it is the
    /// #1 seed with a bye), then day-by-day ticks through the postseason with
    /// the attended-game autopilot off.
    /// </summary>
    private static void RunPostseasonClockChecks(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- 13-4 postseason clock (stop on the avatar's game day; reload mid-series; free sliders after elimination) ---");
        const int startYear = 2026;
        var db = new DatabaseManager(scratchPath);
        try
        {
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(20260703UL);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
            var state = new GlobalState();
            var bus = new EventBus();
            var gameState = new GameStateQueries(db);
            var clock = new TimeManager(db, gameState, state, bus);
            clock.Initialize(startYear);
            int avatarTeam = LeagueStructure.TeamIdBase(LeagueTier.Pro) + 5;

            (CareerManager Career, PlayoffManager Playoffs) Compose(GlobalState calendar)
            {
                var normalizer = new StatsNormalizer(db, baseball);
                var pro = new LeagueSimulator(db, baseball, normalizer, new RngState(777UL), LeagueTier.Pro);
                pro.Initialize();
                pro.PrepareSeason(startYear);
                var leagues = new LeagueDirectory();
                leagues.Register(pro);
                var micro = new MicroGame(db, baseball) { LoggingEnabled = false };
                micro.RestLedger = pro.RestLedger;
                micro.Initialize();
                var careerManager = new CareerManager(db, players, baseball, gameState, calendar, leagues, micro, new RngState(0xBA5E_BA11UL))
                {
                    Normalizer = normalizer,
                    AutopilotAttendedGames = false,
                };
                var manager = new PlayoffManager(db, baseball, leagues);
                careerManager.Playoffs = manager;
                return (careerManager, manager);
            }

            (CareerManager career, PlayoffManager playoffs) = Compose(state);
            career.CreateAvatar("You", "Rookie", avatarTeam, new PlayerRatingsRow
            {
                IsPitcher = false, BatPower = 60, BatContact = 60, BatDiscipline = 60,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });

            // A synthetic regular season (no sim, written straight to Schedule_Games):
            // the avatar's team wins every game.
            var schedule = new ScheduleQueries(db);
            var rows = new List<ScheduledGameRow>();
            schedule.LoadRegularSeason(startYear, LeagueTier.Pro, rows);
            db.RunInBatch(() =>
            {
                foreach (ScheduledGameRow row in rows)
                {
                    bool homeWins = row.HomeTeamId == avatarTeam
                        || (row.AwayTeamId != avatarTeam && (row.HomeTeamId * 7 + row.AwayTeamId * 3) % 5 < 3);
                    schedule.RecordFinal(row.GameId, homeWins ? 4 : 1, homeWins ? 1 : 4);
                }
            });

            // Fast-forward to the last regular-season day with nothing attached.
            int selection = PostseasonCalendar.SelectionDay(LeagueTier.Pro);
            int lastDay = PostseasonCalendar.LastDay(LeagueTier.Pro);
            while (state.DayOfSeason < selection - 1)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            career.AttachTo(bus);
            playoffs.AttachTo(bus);

            // 1. The clock stops on the avatar's first postseason game day, and only there.
            int stoppedOn = 0;
            while (state.DayOfSeason < lastDay)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
                if (career.HasPendingGame)
                {
                    stoppedOn = state.DayOfSeason;
                    break;
                }
            }
            int divisionGame1 = PostseasonCalendar.GameDay(LeagueTier.Pro, 2, 1);
            var seasons = new List<TeamSeasonRow>();
            new PlayoffQueries(db).LoadTeamSeasons(startYear, LeagueTier.Pro, seasons);
            int avatarSeed = seasons.First(r => r.TeamId == avatarTeam).Seed ?? 0;
            Check("13-4: the clock stops on the avatar's postseason game day (the #1 seed's bye skips the Wild Card; first stop = Division game 1)",
                avatarSeed == 1 && stoppedOn == divisionGame1 && career.PendingIsPostseason,
                $"seed {avatarSeed}, stopped on day {stoppedOn} (expected {divisionGame1})");

            // Play game 1, then tick to game 2 (mid-series) and reload there.
            var policy = new NeutralBatterPolicy();
            career.PlayPendingGame(ref policy);
            while (!career.HasPendingGame && state.DayOfSeason < lastDay)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            career.TryGetPendingGame(out PendingAttendedGame before);
            career.DetachFrom(bus);
            playoffs.DetachFrom(bus);

            // 2. The reload: a fresh calendar read, sim, micro, career and manager over the same file.
            state = new GlobalState();
            clock = new TimeManager(db, gameState, state, bus);
            clock.Initialize(startYear);
            (career, playoffs) = Compose(state);
            bool loaded = career.LoadExistingAvatar();
            bool reclaimed = career.ReclaimPostseasonGame(state.CurrentDay);
            career.TryGetPendingGame(out PendingAttendedGame after);
            Check("13-4: a reload mid-series resumes the same fixture (same row, teams and day; still parked)",
                loaded && reclaimed && career.PendingIsPostseason && after.GameId == before.GameId && after.GameId != 0
                && after.HomeTeamId == before.HomeTeamId && after.AwayTeamId == before.AwayTeamId
                && after.AbsoluteDay == before.AbsoluteDay && before.DayOfSeason == PostseasonCalendar.GameDay(LeagueTier.Pro, 2, 2),
                $"game {before.GameId} day {before.DayOfSeason} → game {after.GameId} day {after.DayOfSeason}");

            // 3. Autopilot to the end; the avatar is free from the day after its last game.
            career.AttachTo(bus);
            playoffs.AttachTo(bus);
            career.AutopilotAttendedGames = true;
            career.PlayPendingGame(ref policy);
            while (state.DayOfSeason < lastDay)
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            int lastAlive = playoffs.LastAliveDay(startYear, LeagueTier.Pro, avatarTeam, out _, selection);
            bool champion = playoffs.GetBracket(startYear, LeagueTier.Pro).ChampionTeamId == avatarTeam;
            bool lockedOnLast = playoffs.TryGetLockedPostseasonBlocks(startYear, LeagueTier.Pro, avatarTeam, lastAlive, out int p1, out int g1);
            bool freeAfter = !playoffs.TryGetLockedPostseasonBlocks(startYear, LeagueTier.Pro, avatarTeam, lastAlive + 1, out _, out _);
            int practiceDay = selection + 2; // Fri: a Pro practice weekday, during the #1 seed's bye
            bool practiceLocked = playoffs.TryGetLockedPostseasonBlocks(startYear, LeagueTier.Pro, avatarTeam, practiceDay, out int p2, out int g2);
            int nonQualifier = seasons.First(r => !r.Seed.HasValue).TeamId;
            bool nonQualifierFree = !playoffs.TryGetLockedPostseasonBlocks(startYear, LeagueTier.Pro, nonQualifier, selection, out _, out _);
            Check($"13-4: blocks lock from selection through the avatar's last postseason day ({(champion ? "title" : "elimination")} on day {lastAlive}), then free sliders; non-qualifiers free from selection day",
                lockedOnLast && g1 == TierSeasonCalendar.GameHours && p1 == 0 && freeAfter
                && practiceLocked && p2 == TierSeasonCalendar.PracticeHours && g2 == 0 && nonQualifierFree && !career.HasPendingGame,
                $"last day {lastAlive}: game {g1}h; day {practiceDay}: practice {p2}h; non-qualifier {nonQualifier} free");
            career.DetachFrom(bus);
            playoffs.DetachFrom(bus);
        }
        finally
        {
            db.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // 13-5: the Sunday lock on the real clock (brief §5 CoreLoop fixtures)
    // ------------------------------------------------------------------

    private static void RunRosterClockChecks(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- 13-5 roster clock (an inactive avatar never parks; forfeits never open the at-bat view; lock, reload, reconcile, beats) ---");
        const int startYear = 2026;
        var db = new DatabaseManager(scratchPath);
        try
        {
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var genRng = new RngState(20260703UL);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
            var state = new GlobalState();
            var bus = new EventBus();
            var gameState = new GameStateQueries(db);
            var clock = new TimeManager(db, gameState, state, bus);
            clock.Initialize(startYear);
            int avatarTeam = LeagueStructure.TeamIdBase(LeagueTier.Pro) + 7;

            // Production order: absences, the tier sim, the lock, the career, the beats.
            var ledger = new AvailabilityLedger();
            ledger.AttachTo(bus);
            var normalizer = new StatsNormalizer(db, baseball);
            var pro = new LeagueSimulator(db, baseball, normalizer, new RngState(777UL), LeagueTier.Pro) { Availability = ledger };
            pro.Initialize();
            pro.AttachTo(bus);
            var leagues = new LeagueDirectory();
            leagues.Register(pro);
            var roster = new RosterManager(db, baseball, leagues) { Availability = ledger };
            roster.AttachTo(bus);
            pro.Designations = roster;
            CareerManager ComposeCareer(RosterManager designations, GlobalState calendar)
            {
                var micro = new MicroGame(db, baseball)
                {
                    LoggingEnabled = false, RestLedger = pro.RestLedger, Availability = ledger, Designations = designations,
                };
                micro.Initialize();
                return new CareerManager(db, players, baseball, gameState, calendar, leagues, micro, new RngState(0xBA5E_BA11UL))
                {
                    Normalizer = normalizer, Availability = ledger, Roster = designations, AutopilotAttendedGames = false,
                };
            }
            CareerManager career = ComposeCareer(roster, state);
            // A fringe avatar in a flat (all-50) world: the coach makes him inactive.
            career.CreateAvatar("Fringe", "Rookie", avatarTeam, new PlayerRatingsRow
            {
                IsPitcher = false, BatPower = 30, BatContact = 30, BatDiscipline = 30,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 30,
            });
            career.AttachTo(bus);
            var writer = new DirtAndDiamonds.Narrative.Roster.RosterBeatWriter(db, new NarrativeLogQueries(db), gameState, players, state);
            writer.AttachTo(bus);
            var stress = new List<float>();
            var forfeits = new List<GameForfeitedEvent>();
            bus.Subscribe<StressImpulseEvent>(e => stress.Add(e.Delta));
            bus.Subscribe<GameForfeitedEvent>(e => forfeits.Add(e));
            string avatarId = career.AvatarPlayerId;
            long Texts() => Convert.ToInt64(db.ExecuteScalar(db.GetPooledCommand(
                $"SELECT COUNT(*) FROM Game_Logs WHERE event_type = '{NarrativeLogQueries.EventType}' AND player_id = '{avatarId}';")));
            long Rows(string sql) => Convert.ToInt64(db.ExecuteScalar(db.GetPooledCommand(sql)));
            void AdvanceTo(int dayOfSeason, Action? eachDay = null)
            {
                while (state.DayOfSeason < dayOfSeason)
                {
                    clock.AdvanceDay();
                    bus.DispatchPending();
                    eachDay?.Invoke();
                }
            }
            string[] ActiveBench(RosterManager manager, long day)
            {
                int team = pro.IndexOfTeam(avatarTeam);
                var lineup = new HashSet<string>(pro.InitialLineup(team).ToArray().Select(pro.PlayerIdAt));
                return pro.PositionOrder(team).ToArray().Select(pro.PlayerIdAt)
                    .Where(id => manager.IsActive(id, day) && !lineup.Contains(id) && id != avatarId).ToArray();
            }

            // 1. The Sunday lock fires on 91 and the inactive avatar never parks a game that week.
            int firstLock = RosterRules.FirstLockDayOfSeason;
            AdvanceTo(firstLock);
            bool lockedOnSunday = roster.LockCount == 1 && roster.LastLockDay == state.CurrentDay;
            bool neverParked = true;
            bool noGameBlock = true;
            int gameDays = 0;
            AdvanceTo(firstLock + 6, () =>
            {
                neverParked &= !career.HasPendingGame;
                if (TierSeasonCalendar.TryGetLockedTeamBlocks(LeagueTier.Pro, state.DayOfSeason, out _, out int gameHours) && gameHours > 0)
                {
                    gameDays++;
                    noGameBlock &= !career.IsAvatarActiveOn(state.CurrentDay); // GameManager.GetMandatoryBlocksFor drops the Game block
                }
            });
            long teamGames = Rows($"SELECT COUNT(*) FROM Schedule_Games WHERE season_year = {startYear} AND phase = 0 AND status = 1 " +
                $"AND day_of_season BETWEEN {firstLock + 1} AND {firstLock + 6} AND (home_team_id = {avatarTeam} OR away_team_id = {avatarTeam});");
            Check("13-5: the lock fires on Sunday 91; the inactive avatar never parks a game all week, has no Game blocks, and his team's three games are played",
                lockedOnSunday && neverParked && noGameBlock && gameDays == 3 && teamGames == 3
                && !roster.IsActive(avatarId, state.CurrentDay),
                $"locks {roster.LockCount}, game days {gameDays}, team games {teamGames}");
            Check("13-5: being made inactive is one coach text and a +15 stress impulse",
                Texts() == 1 && stress.SequenceEqual(new[] { 15.0f }), $"texts {Texts()}, stress [{string.Join(",", stress)}]");

            // 2. A reload mid-week keeps the designations (hydrate, no reconcile).
            var reloaded = new RosterManager(db, baseball, leagues);
            bool reconciledMidweek = reloaded.HydrateAndReconcile(state.CurrentDay, state.SeasonYear, state.DayOfSeason);
            int team0 = pro.IndexOfTeam(avatarTeam);
            bool same = pro.PositionOrder(team0).ToArray().Select(pro.PlayerIdAt).Append(avatarId)
                .All(id => reloaded.IsActive(id, state.CurrentDay) == roster.IsActive(id, state.CurrentDay));
            Check("13-5: a reload mid-week keeps the week's designations (hydrated, nothing reconciled)",
                !reconciledMidweek && same && reloaded.LockedThroughDay == roster.LockedThroughDay);

            // 3. A missed lock (the manager detached across Sunday 98) is reconciled at boot.
            roster.DetachFrom(bus);
            AdvanceTo(firstLock + 8);
            var rebooted = new RosterManager(db, baseball, leagues) { Availability = ledger };
            bool reconciled = rebooted.HydrateAndReconcile(state.CurrentDay, state.SeasonYear, state.DayOfSeason);
            long nextSunday = state.CurrentDay - (state.DayOfSeason - (firstLock + 7)) + GameCalendar.DaysPerWeek;
            Check("13-5: a missed Sunday lock is reconciled at boot (nominal day = the last Sunday; it covers today)",
                reconciled && rebooted.LockedThroughDay == nextSunday && rebooted.IsLocked(state.CurrentDay)
                && !rebooted.IsActive(avatarId, state.CurrentDay) && Texts() == 1,
                $"reconciled {reconciled}, horizon {rebooted.LockedThroughDay} (expected {nextSunday}), texts {Texts()}");
            rebooted.AttachTo(bus);
            pro.Designations = rebooted;
            career.DetachFrom(bus);
            career.Roster = rebooted;
            career.AttachTo(bus);

            // 4. The re-activation beat fires once after the inactive stint: the active bench is out for week 105.
            int thirdLock = firstLock + 14;
            foreach (string id in ActiveBench(rebooted, state.CurrentDay))
            {
                bus.Publish(new PlayerAbsenceChangedEvent(id, (byte)AbsenceReason.Suspension,
                    state.CurrentDay - state.DayOfSeason + thirdLock + 7, 0, 0));
            }
            AdvanceTo(thirdLock);
            long textsAfter = Texts();
            bool active = rebooted.IsActive(avatarId, state.CurrentDay + 1);
            AdvanceTo(thirdLock + 2); // Tuesday: an active avatar's game parks again
            bool parksAgain = career.HasPendingGame;
            Check("13-5: the re-activation beat fires once after the inactive stint (one text, −5 stress) and his next game parks",
                active && textsAfter == 2 && stress.SequenceEqual(new[] { 15.0f, -5.0f }) && parksAgain,
                $"active {active}, texts {textsAfter}, stress [{string.Join(",", stress)}], parked {parksAgain}");

            // 5. A forfeit never opens the at-bat view: Thursday's opponent can't field.
            var policy = new NeutralBatterPolicy();
            career.PlayPendingGame(ref policy);
            int thursday = thirdLock + 4;
            pro.TryGetScheduledGame(startYear, thursday, avatarTeam, out long forfeitGame, out int home, out int away);
            int opponent = home == avatarTeam ? away : home;
            foreach (string id in pro.PositionOrder(pro.IndexOfTeam(opponent)).ToArray().Select(pro.PlayerIdAt).Skip(8))
            {
                bus.Publish(new PlayerAbsenceChangedEvent(id, (byte)AbsenceReason.Injury,
                    state.CurrentDay - state.DayOfSeason + thursday + 1, 0, 0));
            }
            int forfeitsBefore = forfeits.Count;
            long textsBefore = Texts();
            AdvanceTo(thursday);
            Check("13-5: a forfeit never opens the at-bat view — no pending game, the clock runs on, one GameForfeitedEvent, one coach text, no stress",
                !career.HasPendingGame && forfeits.Count == forfeitsBefore + 1 && forfeits[^1].GameId == forfeitGame
                && Texts() == textsBefore + 1 && stress.Count == 2
                && Rows($"SELECT status FROM Schedule_Games WHERE game_id = {forfeitGame};") == 2,
                $"pending {career.HasPendingGame}, events {forfeits.Count - forfeitsBefore}, texts {Texts() - textsBefore}");

            // 6. (D3-A) A reload on a regular-season game day re-parks the same fixture.
            AdvanceTo(thursday + 2);
            bool parked = career.TryGetPendingGame(out PendingAttendedGame before);
            career.DetachFrom(bus);
            state = new GlobalState();
            clock = new TimeManager(db, gameState, state, bus);
            clock.Initialize(startYear);
            CareerManager reloadedCareer = ComposeCareer(rebooted, state);
            bool loaded = reloadedCareer.LoadExistingAvatar();
            bool reclaimed = reloadedCareer.ReclaimRegularSeasonGame(state.CurrentDay);
            reloadedCareer.TryGetPendingGame(out PendingAttendedGame after);
            Check("13-5 (D3-A): a reload on a regular-season game day re-parks the same fixture (game_id, teams, day)",
                parked && loaded && reclaimed && !reloadedCareer.PendingIsPostseason && after.GameId == before.GameId
                && after.GameId != 0 && after.HomeTeamId == before.HomeTeamId && after.AwayTeamId == before.AwayTeamId
                && after.AbsoluteDay == before.AbsoluteDay,
                $"game {before.GameId} → {after.GameId}");
            rebooted.DetachFrom(bus);
            writer.DetachFrom(bus);
        }
        finally
        {
            db.Dispose();
        }
    }

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
