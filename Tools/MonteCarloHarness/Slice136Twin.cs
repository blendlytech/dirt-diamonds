using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// Slice 13-6, D1-A (brief §3, Argon Q1, Amendment 7): season-pipeline's
// channels 2–7. Channel 1 is the guard itself (CheckProGuard, unwired) and
// channel 8 is the existing 20 postseason and designation channels. The
// guard world stays pipeline-free; a pipeline-attached twin proves the
// in-season pipeline changes no 2026 game, and its day-366 rollover moves
// players (the positive control).
internal static partial class Program
{
    /// <summary>What the unwired guard world looked like on day 365 (for the twin's comparisons).</summary>
    private sealed class GuardCapture
    {
        public RngState RngAt365;
        public string Ledger2026 = "";
        public readonly List<string> LockHashes2026 = new();
    }

    private static readonly string[] Ledger2026Queries =
    {
        $"SELECT season_year, player_id, pa, ab, h, doubles, triples, hr, bb, so, rbi, sb FROM Batting_Stats WHERE season_year = {StartYear} " +
        "ORDER BY season_year, player_id;",
        $"SELECT season_year, player_id, g, gs, w, l, sv, outs_recorded, h_allowed, er, bb, so FROM Pitching_Stats WHERE season_year = {StartYear} " +
        "ORDER BY season_year, player_id;",
    };

    private static string Ledger2026Sha(DatabaseManager db) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DumpQueries(db, Ledger2026Queries))));

    /// <summary>A read-only subscriber (after the lock) hashing each 2026 lock's 768 rows.</summary>
    private static void CaptureLockHashes(DatabaseManager db, EventBus bus, List<string> hashes) =>
        bus.Subscribe<DayAdvancedEvent>(e =>
        {
            if (e.SeasonYear == StartYear && RosterRules.IsLockDay(e.DayOfSeason))
            {
                hashes.Add(Sha12(DumpQueries(db, new[]
                {
                    $"SELECT player_id, team_id, is_active, locked_through_day FROM Roster_Designations WHERE locked_through_day = {e.Day + GameCalendar.DaysPerWeek} ORDER BY player_id;",
                })));
            }
        });

    private static void CheckPipelineFreeGuard(DatabaseManager db)
    {
        long enrolled = CountRows(db, "SELECT COUNT(*) FROM College_Enrollment;");
        long offers = CountRows(db, "SELECT COUNT(*) FROM Recruiting_Offers;");
        long picks = CountRows(db, "SELECT COUNT(*) FROM Draft_Picks;");
        long flags = CountRows(db, "SELECT COUNT(*) FROM Entity_Flags WHERE flag_name LIKE 'pipeline_%';");
        long seed = CountRows(db, "SELECT COUNT(*) FROM Game_State WHERE key = 'pipeline_seed';");
        Check("13-6 D1-A ch.2: the guard database has no pipeline rows after day 730 (no enrollment, offers, picks, pipeline_* flags or pipeline_seed)",
            enrolled == 0 && offers == 0 && picks == 0 && flags == 0 && seed == 0,
            $"enrollment {enrolled}, offers {offers}, picks {picks}, flags {flags}, seed {seed}");
    }

    private static void RunPipelineTwinChannels(string schemaPath, string path, GuardCapture guard)
    {
        Slice136Paths.Add(path);
        using var db = new DatabaseManager(path);
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
        var normalizer = new StatsNormalizer(db, baseball);
        var league = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed));
        league.Initialize();
        league.AttachTo(bus);
        var leagues = new LeagueDirectory();
        leagues.Register(league);
        // The pipeline exists before the lock (the GameManager order) and is
        // attached at the retired PromotionManager's slot, after the playoffs.
        var pipeline = new PipelineManager(db, players, baseball, gameState, state, leagues) { RatingSpread = 0 };
        var roster = new RosterManager(db, baseball, leagues) { Market = pipeline };
        var probe = new RosterGuardProbe(db, league, roster, bus);
        roster.AttachTo(bus);
        probe.AttachAfterLock(bus);
        var lockHashes = new List<string>();
        CaptureLockHashes(db, bus, lockHashes);
        league.Designations = roster;
        roster.HydrateAndReconcile(state.CurrentDay, state.SeasonYear, state.DayOfSeason);
        var playoffs = new PlayoffManager(db, baseball, leagues);
        playoffs.AttachTo(bus);
        pipeline.EnsureSeed(PipelineProfile.DefaultPipelineSeed);
        pipeline.AttachTo(bus);
        pipeline.EnsureAtBoot(state.CurrentDay);

        var bootSets = new Dictionary<int, HashSet<string>>();
        var proRows = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.Pro, proRows);
        foreach (RosterPlayerRow row in proRows)
        {
            (bootSets.TryGetValue(row.TeamId, out HashSet<string>? set) ? set : bootSets[row.TeamId] = new HashSet<string>(StringComparer.Ordinal))
                .Add(row.PlayerId);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (state.CurrentDay < GlobalState.DaysPerSeason)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
            probe.AfterDay(state);
        }
        stopwatch.Stop();
        Console.WriteLine($"  13-6 D1-A twin (Pro sim + pipeline, days 1–365): {stopwatch.ElapsedMilliseconds} ms");

        // ---- ch.3: the 2026 games are the guard's ----
        LeagueBattingTotals bat = baseball.LoadLeagueBattingTotals(StartYear);
        LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(StartYear);
        string ledger = Ledger2026Sha(db);
        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        var playoffQueries = new PlayoffQueries(db);
        playoffQueries.LoadSeries(StartYear, LeagueTier.Pro, series);
        playoffQueries.LoadPostseasonGames(StartYear, LeagueTier.Pro, games);
        string outcome = $"{series.Count}/{games.Count}/{series.FirstOrDefault(s => s.Round == PostseasonCalendar.FinalRound(LeagueTier.Pro)).WinnerTeamId}";
        Check("13-6 D1-A ch.3: a pipeline-attached twin matches 2026 exactly — PA 81433 / H 18471 / ER 8803, the guard's 2026 ledger byte for byte, the same postseason (11 series, 30 games, champion 323)",
            bat.Pa == ProGuardPa && bat.H == ProGuardH && pit.Er == ProGuardEr
            && string.Equals(ledger, guard.Ledger2026, StringComparison.Ordinal) && outcome == "11/30/323",
            $"PA {bat.Pa} H {bat.H} ER {pit.Er}; ledger {ledger[..12]}/{guard.Ledger2026[..Math.Min(12, guard.Ledger2026.Length)]}; postseason {outcome}");

        // ---- ch.4: no lock does anything ----
        for (int i = 0; i < 3; i++)
        {
            pipeline.FillBeforeLock(StartYear, Abs136(StartYear, 273));
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            pipeline.FillBeforeLock(StartYear, Abs136(StartYear, 273));
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("13-6 D1-A ch.4: at all 27 locks, 0 signings and 0 re-initializations; LeagueSimulator.RngSnapshot unchanged across every lock and equal to the guard's on day 365; the warm lock hook allocates 0 B",
            probe.Locks.Count == 27 && probe.RngMoves == 0 && probe.BadLocks == 0 && pipeline.FreeAgentSignings == 0
            && pipeline.LockReinitializations == 0 && league.RngSnapshot.Equals(guard.RngAt365) && allocated == 0,
            $"locks {probe.Locks.Count}, RNG moves {probe.RngMoves}, signings {pipeline.FreeAgentSignings}, re-inits {pipeline.LockReinitializations}, {allocated} B");

        // ---- ch.5: the twin's calendar ran ----
        var queries = new PipelineQueries(db);
        var picks = new List<DraftPickRow>();
        queries.LoadDraftPicks(StartYear, picks);
        var proTeams = new List<TeamRow>();
        baseball.LoadTeamsByTier(LeagueTier.Pro, proTeams);
        List<int> inaugural = PipelineRules.InauguralDraftOrder(proTeams.Select(t => t.TeamId).OrderBy(t => t).ToList(), pipeline.Seed, StartYear);
        bool inauguralOrder = picks.Count == 128 && picks.Take(32).Select(p => p.TeamId).SequenceEqual(inaugural)
            && picks.Skip(96).Select(p => p.TeamId).SequenceEqual(inaugural);
        long enrolled = CountRows(db, "SELECT COUNT(*) FROM College_Enrollment;");
        long signedFlags = CountRows(db, $"SELECT COUNT(*) FROM Entity_Flags WHERE flag_name = '{PipelineFlags.ProSigned}' AND is_active = 1;");
        long accepted = CountRows(db, $"SELECT COUNT(*) FROM Recruiting_Offers WHERE season_year = {StartYear} AND status = 1;");
        long openings = CountRows(db, "SELECT COUNT(*) FROM College_Enrollment AS e WHERE e.class_year >= 4 " +
            $"OR e.player_id IN (SELECT player_id FROM Draft_Picks WHERE season_year = {StartYear});");
        Check("13-6 D1-A ch.5: on day 365 the twin has 576 enrollment rows, 128 Draft_Picks for 2026 in the seeded inaugural order, 128 active pipeline_pro_signed flags, and accepted 2026 offers = the 2026→2027 College openings",
            enrolled == 576 && inauguralOrder && signedFlags == 128 && accepted == openings && accepted > 0,
            $"enrollment {enrolled}, picks {picks.Count} (inaugural {inauguralOrder}), flags {signedFlags}, accepted {accepted} / openings {openings}");

        // ---- ch.6: Pro rosters didn't change in-season ----
        baseball.LoadRosterByTier(LeagueTier.Pro, proRows);
        var nowSets = proRows.GroupBy(r => r.TeamId).ToDictionary(g => g.Key, g => g.Select(r => r.PlayerId).ToHashSet(StringComparer.Ordinal));
        bool unchanged = bootSets.Count == 32 && bootSets.All(kv => nowSets.TryGetValue(kv.Key, out HashSet<string>? now) && now.SetEquals(kv.Value));
        bool draftedStayed = picks.All(p => players.TryGetById(p.PlayerId, out PlayerRow pr) && pr.TeamId is int t
            && baseball.TryGetTeamTier(t, out LeagueTier tier) && tier == LeagueTier.College);
        Check("13-6 D1-A ch.6: through day 365 every Pro team keeps its boot 24-player set (draftees stay on College teams), and all 27 locks' 768 Roster_Designations rows match the guard's",
            unchanged && draftedStayed && lockHashes.Count == 27 && lockHashes.SequenceEqual(guard.LockHashes2026),
            $"sets unchanged {unchanged}, draftees on College {draftedStayed}, lock hashes {lockHashes.Count}/{guard.LockHashes2026.Count} equal {lockHashes.SequenceEqual(guard.LockHashes2026)}");

        // ---- ch.7: the rollover moves players (no career: no aging, no intake) ----
        clock.AdvanceDay();
        bus.DispatchPending();
        baseball.LoadRosterByTier(LeagueTier.Pro, proRows);
        bool shape = proRows.Count == 768 && proRows.GroupBy(r => r.TeamId).All(g =>
            g.Count(r => r.Role == PitcherRole.None) == 15 && g.Count(r => r.Role == PitcherRole.Starter) == 4
            && g.Count(r => r.Role == PitcherRole.Reliever) == 5);
        bool draftedMoved = picks.All(p => players.TryGetById(p.PlayerId, out PlayerRow pr) && pr.TeamId == p.TeamId);
        PipelineSummary run = pipeline.LastRun;
        Check("13-6 D1-A ch.7: the day-366 rollover moves the 128 draftees to Pro, releases 128 incumbents as flagged free agents and keeps 32 × 15/4/5 = 768; nobody ages, so HS intake is 0",
            run.Draftees == 128 && run.ProReleases == 128 && draftedMoved && shape && run.Intake == 0 && run.HsGraduates == 0,
            $"draftees {run.Draftees}, releases {run.ProReleases}, Pro {proRows.Count}, intake {run.Intake}");
        Check("13-6 D1-A twin: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
    }
}
