using System.Diagnostics;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// ----------------------------------------------------------------------
// Slice 13-4 (docs/tasks/13-4_playoffs_ranking_brief.md): standings (D2),
// the College National Ranking (CS-M §3, Amendment 2), selection, brackets
// and series (D3, Amendment 4), the guard-world postseason and its Math
// record §10.1 Q7 invariance channels (D4-A), the attended postseason
// (Amendment 1), the championship event, and the opt-in ranking analysis
// (D5, `--college-analysis ranking [--pilot]`).
// `--only standings,hs-playoffs,college-ranking,college-bracket,college-series,
//  pro-playoffs,championship-event,attended-postseason`.
// ----------------------------------------------------------------------
internal static partial class Program
{
    /// <summary>The 100,000-series fair-coin check's pinned seed (Amendment 7; 49,965 deciding games).</summary>
    private const ulong FairCoinSeed = 0x134C_0140_0001_86A0UL;

    /// <summary>First season year of the 13-4 synthetic fixtures (clear of every other fixture's years).</summary>
    private const int Slice134BaseYear = 4000;

    // ------------------------------------------------------------------
    // season-pipeline: the guard world's postseasons + Q7 channels
    // ------------------------------------------------------------------

    /// <summary>Observes the guard world day by day for the Q7 invariance channels.</summary>
    private sealed class GuardWorldProbe
    {
        private readonly LeagueSimulator _league;
        public readonly Dictionary<int, RngState> RngAtSelection = new();
        public readonly Dictionary<int, RngState> RngAfterPostseason = new();
        public readonly Dictionary<int, (int ShortRest, int Bullpen)> CountersAtSelection = new();
        public readonly Dictionary<int, (int ShortRest, int Bullpen)> CountersAfterPostseason = new();
        public readonly Dictionary<int, long> MaxRestAfterPostseason = new();
        public readonly List<ChampionshipWonEvent> Champions = new();
        public readonly List<PlayoffSeriesDecidedEvent> Decided = new();
        public int PostseasonDaysWithQueuedResults;
        public int PostseasonDaysUnflushed;
        public readonly string RatingsBefore;
        private readonly DatabaseManager _db;

        public GuardWorldProbe(DatabaseManager db, LeagueSimulator league, PlayoffManager playoffs, EventBus bus)
        {
            _db = db;
            _league = league;
            RatingsBefore = Sha12(DumpQueries(db, new[] { "SELECT * FROM Player_Ratings ORDER BY player_id;" }));
            bus.Subscribe<ChampionshipWonEvent>(e => Champions.Add(e));
            bus.Subscribe<PlayoffSeriesDecidedEvent>(e => Decided.Add(e));
        }

        public void AfterDay(GlobalState state)
        {
            int d = state.DayOfSeason;
            int year = state.SeasonYear;
            int selection = PostseasonCalendar.SelectionDay(LeagueTier.Pro);
            int last = PostseasonCalendar.LastDay(LeagueTier.Pro);
            if (d == selection)
            {
                RngAtSelection[year] = _league.RngSnapshot;
                CountersAtSelection[year] = (_league.ShortRestStarts, _league.BullpenGames);
            }
            if (d > selection && d <= last)
            {
                PostseasonDaysWithQueuedResults += _league.PendingResultCount != 0 ? 1 : 0;
                PostseasonDaysUnflushed += _league.UnflushedSeasonYear != 0 ? 1 : 0;
            }
            if (d == last + 1)
            {
                RngAfterPostseason[year] = _league.RngSnapshot;
                CountersAfterPostseason[year] = (_league.ShortRestStarts, _league.BullpenGames);
                MaxRestAfterPostseason[year] = CountRows(_db, "SELECT MAX(last_start_day) FROM Pitcher_Rest;");
            }
        }
    }

    private static void CheckGuardWorldPostseason(
        DatabaseManager db, BaseballQueries baseball, LeagueSimulator league, PlayoffManager playoffs, GuardWorldProbe probe)
    {
        var queries = new PlayoffQueries(db);
        var scheduleQueries = new ScheduleQueries(db);
        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        foreach (int year in new[] { StartYear, StartYear + 1 })
        {
            queries.LoadSeries(year, LeagueTier.Pro, series);
            queries.LoadPostseasonGames(year, LeagueTier.Pro, games);
            (bool legal, string detail) = SeriesShapeLegal(LeagueTier.Pro, series, games);
            int champions = series.Count(s => s.Round == PostseasonCalendar.FinalRound(LeagueTier.Pro) && s.IsDecided);
            Console.WriteLine($"  {year} Pro postseason: {series.Count} series, {games.Count} games; champion " +
                $"{series.FirstOrDefault(s => s.Round == 4).WinnerTeamId}");
            Check($"[{year}] guard world's Pro postseason completed with the postseason wired: 11 series, exactly one champion, legal lengths, fixed dates",
                series.Count == 11 && champions == 1 && series.All(s => s.IsDecided) && legal,
                $"{series.Count} series, {champions} champion(s), {games.Count} games; {detail}");
        }

        long naturalDupes = CountRows(db,
            "SELECT COUNT(*) FROM (SELECT season_year, tier, round, high_seed_team_id, low_seed_team_id, COUNT(*) AS c " +
            "FROM Playoff_Series GROUP BY 1, 2, 3, 4, 5 HAVING c > 1);");
        Check("natural series key (season, tier, round, high, low) unique — the per-game stream key (Amendment 3)", naturalDupes == 0,
            $"{naturalDupes} duplicates");
        Check("selection found the regular season complete (forced selection-day flush; 0 unplayed rows)",
            playoffs.LastSelectionUnplayed == 0, $"{playoffs.LastSelectionUnplayed} unplayed");

        // Q7 channels (the guard itself, channel 1, is CheckProGuard above).
        bool ch2 = probe.RngAtSelection.Count == 2 && probe.RngAtSelection.All(kv =>
            probe.RngAfterPostseason.TryGetValue(kv.Key, out RngState after) && after.Equals(kv.Value));
        Check("Q7 ch.2: the postseason never advances LeagueSimulator._rng (state at selection == after the World Championship, both seasons)", ch2);

        int opener2027 = GlobalState.DaysPerSeason + TierSeasonCalendar.GameDay(LeagueTier.Pro, 1);
        long maxRest2026 = probe.MaxRestAfterPostseason.GetValueOrDefault(StartYear);
        Check("Q7 ch.3: postseason starts reach Pitcher_Rest (latest start inside the postseason) and every arm is rested for season 2",
            maxRest2026 >= PostseasonCalendar.GameDay(LeagueTier.Pro, 1, 1)
            && maxRest2026 <= PostseasonCalendar.LastDay(LeagueTier.Pro)
            && opener2027 - maxRest2026 >= TierRules.MinRestDays(LeagueTier.Pro),
            $"latest 2026 start day {maxRest2026}, 2027 opener day {opener2027}");
        Check("Q7 ch.4: no absences in the guard world (Player_Absences empty)",
            CountRows(db, "SELECT COUNT(*) FROM Player_Absences;") == 0);
        Check("Q7 ch.5: no PED costs anywhere (health_ceiling 100, detection_risk 0 for every player)",
            CountRows(db, "SELECT COUNT(*) FROM Players WHERE health_ceiling <> 100 OR detection_risk <> 0;") == 0);
        string ratingsAfter = Sha12(DumpQueries(db, new[] { "SELECT * FROM Player_Ratings ORDER BY player_id;" }));
        Check("Q7 ch.6: development inputs untouched (ratings hash unchanged; no avatar_practice_credit)",
            ratingsAfter == probe.RatingsBefore
            && CountRows(db, "SELECT COUNT(*) FROM Game_State WHERE key = 'avatar_practice_credit';") == 0,
            $"{probe.RatingsBefore} → {ratingsAfter}");
        Check("Q7 ch.8: no postseason day leaves a queued regular-season result or an unflushed season (Sunday flushes stay empty)",
            probe.PostseasonDaysWithQueuedResults == 0 && probe.PostseasonDaysUnflushed == 0,
            $"queued {probe.PostseasonDaysWithQueuedResults}, unflushed {probe.PostseasonDaysUnflushed}");
        Check("Q7 ch.9: phase boundary — 1,024 regular-season games per season; every postseason row is phase 2",
            scheduleQueries.CountRegularSeasonGames(StartYear, LeagueTier.Pro) == 1024
            && scheduleQueries.CountRegularSeasonGames(StartYear + 1, LeagueTier.Pro) == 1024
            && CountRows(db, "SELECT COUNT(*) FROM Schedule_Games WHERE series_id IS NOT NULL AND phase <> 2;") == 0
            && CountRows(db, "SELECT COUNT(*) FROM Schedule_Games WHERE phase = 0 AND series_id IS NOT NULL;") == 0);
        bool ch10 = probe.CountersAtSelection.Count == 2 && probe.CountersAtSelection.All(kv =>
            probe.CountersAfterPostseason.TryGetValue(kv.Key, out var after) && after == kv.Value);
        Check("Q7 ch.10: regular-season ShortRestStarts/BullpenGames unchanged by the postseason (kept apart)", ch10,
            $"postseason short-rest {league.PostseasonShortRestStarts}, bullpen {league.PostseasonBullpenGames}");
        Check("Q7 ch.7 (attended stat suppression) is proven by the attended-postseason suite; the guard world has no avatar", true);

        var championsByYear = probe.Champions.GroupBy(c => c.SeasonYear).ToDictionary(g => g.Key, g => g.Count());
        Check("guard world: exactly one ChampionshipWonEvent per Pro season, tier Pro on every publish; 11 PlayoffSeriesDecidedEvents per season",
            championsByYear.Count == 2 && championsByYear.Values.All(c => c == 1)
            && probe.Champions.All(c => c.Tier == (byte)LeagueTier.Pro)
            && probe.Decided.Count == 22 && probe.Decided.All(d => d.Tier == (byte)LeagueTier.Pro),
            $"champions {probe.Champions.Count}, decided {probe.Decided.Count}; macro games {playoffs.MacroGamesPlayed}");
    }

    /// <summary>
    /// Every series decided at a legal length, its rows exactly its played
    /// games (rows on certainty), every row on its fixed slot, high-seed
    /// hosting per Bo1 / Bo3 1-3 / Bo5 2-2-1, never a Sunday.
    /// </summary>
    private static (bool Legal, string Detail) SeriesShapeLegal(
        LeagueTier tier, List<PlayoffSeriesRow> series, List<PostseasonGameRow> games)
    {
        foreach (PlayoffSeriesRow s in series)
        {
            int played = s.HighWins + s.LowWins;
            int winnerWins = Math.Max(s.HighWins, s.LowWins);
            if (!s.IsDecided || winnerWins != s.WinsNeeded || played < s.WinsNeeded || played > s.BestOf
                || s.BestOf != PostseasonCalendar.BestOf(tier, s.Round))
            {
                return (false, $"series {s.SeriesId} r{s.Round} {s.HighWins}-{s.LowWins} of {s.BestOf}");
            }
            List<PostseasonGameRow> rows = games.Where(g => g.SeriesId == s.SeriesId).OrderBy(g => g.GameInSeries).ToList();
            if (rows.Count != played)
            {
                return (false, $"series {s.SeriesId}: {rows.Count} rows for {played} games");
            }
            for (int k = 0; k < rows.Count; k++)
            {
                PostseasonGameRow g = rows[k];
                bool highHosts = PostseasonCalendar.HighSeedHosts(s.BestOf, k + 1);
                if (g.GameInSeries != k + 1 || !g.IsPlayed
                    || g.DayOfSeason != PostseasonCalendar.GameDay(tier, s.Round, k + 1)
                    || g.DayOfSeason % GameCalendar.DaysPerWeek == 0
                    || g.HomeTeamId != (highHosts ? s.HighSeedTeamId : s.LowSeedTeamId)
                    || g.Phase != PostseasonCalendar.Phase(tier, s.Round))
                {
                    return (false, $"series {s.SeriesId} game {k + 1}: day {g.DayOfSeason}, home {g.HomeTeamId}, phase {g.Phase}");
                }
            }
        }
        return (true, "all legal");
    }

    // ------------------------------------------------------------------
    // Shared fixture worlds (Amendment 7: at most two generated v14 worlds)
    // ------------------------------------------------------------------

    private sealed class Slice134World : IDisposable
    {
        public readonly string Path;
        public readonly DatabaseManager Db;
        public readonly PlayerQueries Players;
        public readonly BaseballQueries Baseball;
        public readonly StatsNormalizer Normalizer;
        public readonly GameStateQueries GameState;
        public readonly PlayoffQueries Queries;
        public readonly ScheduleQueries Schedule;
        private readonly LeagueSimulator?[] _sims = new LeagueSimulator?[LeagueDirectory.TierCount];

        public Slice134World(string schemaPath, string path)
        {
            Path = path;
            Db = new DatabaseManager(path);
            Db.InitializeSchema(schemaPath);
            Players = new PlayerQueries(Db);
            Baseball = new BaseballQueries(Db);
            var genRng = new RngState(LeagueSeed);
            LeagueGenerator.GenerateIfEmpty(Db, Players, Baseball, ratingSpread: 0, ref genRng);
            Normalizer = new StatsNormalizer(Db, Baseball);
            GameState = new GameStateQueries(Db);
            Queries = new PlayoffQueries(Db);
            Schedule = new ScheduleQueries(Db);
        }

        /// <summary>The world's one sim per tier (fixtures key their own years and absolute days).</summary>
        public LeagueSimulator Sim(LeagueTier tier)
        {
            LeagueSimulator? sim = _sims[(int)tier];
            if (sim is null)
            {
                sim = new LeagueSimulator(Db, Baseball, Normalizer, new RngState(0x1340_0000UL + (ulong)tier), tier);
                sim.Initialize();
                _sims[(int)tier] = sim;
            }
            return sim;
        }

        public LeagueDirectory Leagues(params LeagueTier[] tiers)
        {
            var directory = new LeagueDirectory();
            foreach (LeagueTier tier in tiers)
            {
                directory.Register(Sim(tier));
            }
            return directory;
        }

        public void Dispose() => Db.Dispose();
    }

    private static Slice134World? _world134;
    private static readonly List<string> Slice134Paths = new();

    private static Slice134World World134(string schemaPath)
    {
        if (_world134 is null)
        {
            _world134 = new Slice134World(schemaPath, Slice134TempPath(""));
        }
        return _world134;
    }

    /// <summary>Main's finally: closes the 13-4 world and deletes every scratch file the suites made.</summary>
    private static void CleanupSlice134()
    {
        _world134?.Dispose();
        _world134 = null;
        foreach (string path in Slice134Paths)
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
        Slice134Paths.Clear();
    }

    private static string Slice134TempPath(string tag)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dnd_134{tag}_{Guid.NewGuid():N}.db");
        Slice134Paths.Add(path);
        return path;
    }

    /// <summary>A fixture's absolute day: each fixture year sits on its own stretch of the calendar.</summary>
    private static long Abs134(int year, int dayOfSeason) => (long)(year - StartYear) * GlobalState.DaysPerSeason + dayOfSeason;

    /// <summary>A deterministic team strength for synthetic seasons (a hash, so it isn't team-id order).</summary>
    private static long HashPower(int teamId) => (long)(PostseasonStreams.Mix64((ulong)teamId ^ 0x5EA5_0134UL) >> 2);

    /// <summary>
    /// Writes a complete synthetic regular season (no sim, no stat rows): the
    /// stronger team by <paramref name="power"/> wins every game by a seeded
    /// score. Ensures the season's schedule first. Returns the games written.
    /// </summary>
    private static int WriteSyntheticSeason(Slice134World w, LeagueTier tier, int year, Func<int, long> power, ulong seed) =>
        WriteSyntheticSeason(w.Db, w.Sim(tier), w.Schedule, year, power, seed);

    private static int WriteSyntheticSeason(
        DatabaseManager db, LeagueSimulator sim, ScheduleQueries schedule, int year, Func<int, long> power, ulong seed)
    {
        sim.PrepareSeason(year);
        var rows = new List<ScheduledGameRow>();
        schedule.LoadRegularSeason(year, sim.Tier, rows);
        var rng = new RngState(seed);
        db.RunInBatch(() =>
        {
            foreach (ScheduledGameRow row in rows)
            {
                long hp = power(row.HomeTeamId);
                long ap = power(row.AwayTeamId);
                bool homeWins = hp != ap ? hp > ap : (rng.NextUInt64() & 1UL) == 0;
                int winner = 2 + rng.NextInt(8);
                int loser = rng.NextInt(winner);
                schedule.RecordFinal(row.GameId, homeWins ? winner : loser, homeWins ? loser : winner);
            }
        });
        return rows.Count;
    }

    /// <summary>Drives a manager's day handler (no clock) over a range of days; dispatches the bus after each.</summary>
    private static void DrivePostseason(PlayoffManager playoffs, LeagueSimulator sim, int year, int fromDay, int toDay, EventBus? bus = null)
    {
        for (int d = fromDay; d <= toDay; d++)
        {
            playoffs.WritePublicationMarkers();
            playoffs.ProcessDay(sim, new DayAdvancedEvent(Abs134(year, d), year, d));
            bus?.DispatchPending();
        }
    }

    private static List<TeamRow> TeamsOfTier(Slice134World w, LeagueTier tier)
    {
        var teams = new List<TeamRow>();
        w.Baseball.LoadTeamsByTier(tier, teams);
        return teams;
    }

    private static void ExecSql(DatabaseManager db, string sql) =>
        db.RunInBatch(() => db.ExecuteNonQuery(db.GetPooledCommand(sql)));

    // ------------------------------------------------------------------
    // standings (D2-A)
    // ------------------------------------------------------------------

    private static SeasonTable TableOf(int teams, params (int Home, int Away, int HomeScore, int AwayScore)[] games)
    {
        var table = new SeasonTable();
        var ids = new int[teams];
        for (int i = 0; i < teams; i++)
        {
            ids[i] = i + 1;
        }
        table.Build(ids, games.Select(g => new CompletedGame(g.Home, g.Away, g.HomeScore, g.AwayScore)).ToList());
        return table;
    }

    private static void RunStandingsSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 standings (D2-A: win% → mini-league H2H → RD → RA → seeded coin, recursive) ---");
        const ulong seed = SeasonScheduler.DefaultScheduleSeed;
        const int year = 2026;

        // 2-way: 1 and 2 at .500; 1 beat 2, though 2's RD is far better (H2H comes first).
        SeasonTable t2 = TableOf(4, (1, 2, 2, 1), (3, 1, 9, 0), (2, 4, 9, 0));
        List<int> o2 = Standings.Order(t2, new[] { 1, 2, 3, 4 }, seed, year, LeagueTier.Pro);
        Check("D2 2-way tie: head-to-head breaks it before run differential", o2.SequenceEqual(new[] { 3, 1, 2, 4 }), string.Join(",", o2));

        // 3-way at 3-3: group H2H puts 1 first (3-1) and ties 2 and 3 (1-2 each); the {2,3}
        // sub-group RESTARTS at H2H (2 beat 3) even though 3's run differential is better.
        SeasonTable t3 = TableOf(5,
            (1, 2, 3, 2), (1, 2, 3, 2), (1, 3, 3, 2), (3, 1, 3, 2), (2, 3, 3, 2),
            (4, 1, 1, 0), (4, 1, 1, 0), (2, 5, 1, 0), (2, 5, 1, 0), (4, 2, 1, 0),
            (3, 5, 10, 0), (3, 5, 10, 0), (4, 3, 1, 0));
        List<int> o3 = Standings.Order(t3, new[] { 1, 2, 3 }, seed, year, LeagueTier.Pro);
        Check("D2 3-way: partial H2H separation, then the tied pair restarts from H2H (not RD)",
            t3.Wins(0) == 3 && t3.Losses(0) == 3 && t3.Wins(1) == 3 && t3.Losses(1) == 3 && t3.Wins(2) == 3 && t3.Losses(2) == 3
            && t3.RunDifferential(2) > t3.RunDifferential(1) && o3.SequenceEqual(new[] { 1, 2, 3 }),
            $"{string.Join(",", o3)}; RD {t3.RunDifferential(1)} v {t3.RunDifferential(2)}");

        // RD: H2H split 1-1, RD decides.
        SeasonTable tRd = TableOf(3, (1, 2, 5, 0), (2, 1, 1, 0), (1, 3, 1, 0), (2, 3, 1, 0), (3, 1, 1, 0), (3, 2, 1, 0));
        List<int> oRd = Standings.Order(tRd, new[] { 1, 2 }, seed, year, LeagueTier.Pro);
        Check("D2 run differential breaks an even H2H (higher first)", oRd.SequenceEqual(new[] { 1, 2 }),
            $"RD {tRd.RunDifferential(0)} v {tRd.RunDifferential(1)}");

        // RA: H2H 1-1, equal RD, fewer runs allowed first.
        SeasonTable tRa = TableOf(3, (1, 2, 3, 2), (2, 1, 3, 2), (1, 3, 1, 0), (2, 3, 2, 1), (3, 1, 9, 8), (3, 2, 5, 4));
        List<int> oRa = Standings.Order(tRa, new[] { 1, 2 }, seed, year, LeagueTier.Pro);
        bool raSetup = tRa.Wins(0) == tRa.Wins(1) && tRa.Losses(0) == tRa.Losses(1)
            && tRa.RunDifferential(0) == tRa.RunDifferential(1) && tRa.RunsAllowed(0) != tRa.RunsAllowed(1);
        int fewerRa = tRa.RunsAllowed(0) < tRa.RunsAllowed(1) ? 1 : 2;
        Check("D2 runs allowed breaks an even H2H and RD (fewer first)", raSetup && oRa[0] == fewerRa,
            $"RA {tRa.RunsAllowed(0)} v {tRa.RunsAllowed(1)}, RD {tRa.RunDifferential(0)} v {tRa.RunDifferential(1)}");

        // Coin: a perfectly symmetric pair (1-1 H2H, equal RD and RA).
        SeasonTable tCoin = TableOf(2, (1, 2, 3, 2), (2, 1, 3, 2));
        List<int> oCoin = Standings.Order(tCoin, new[] { 1, 2 }, seed, year, LeagueTier.Pro);
        int coinFirst = PostseasonStreams.StandingsCoinKey(seed, year, LeagueTier.Pro, 1)
            < PostseasonStreams.StandingsCoinKey(seed, year, LeagueTier.Pro, 2) ? 1 : 2;
        int flips = 0;
        for (int y = 0; y < 64; y++)
        {
            flips += Standings.Order(tCoin, new[] { 1, 2 }, seed, 3000 + y, LeagueTier.Pro)[0] == 1 ? 1 : 0;
        }
        Check("D2 seeded coin: an exact tie orders by StandingsCoinKey ascending; neither team wins every season's coin",
            oCoin[0] == coinFirst && flips is > 0 and < 64, $"team 1 first in {flips}/64 seasons");

        // The coin is a pure function of persisted inputs: unmoved by sim RNG, stable across a rebuild.
        var noise = new RngState(12345);
        noise.NextUInt64();
        SeasonTable rebuilt = TableOf(2, (2, 1, 3, 2), (1, 2, 3, 2)); // same games, other row order
        Check("D2 coin is independent of sim RNG and stable across a reload (rebuilt table, reordered rows)",
            Standings.Order(rebuilt, new[] { 2, 1 }, seed, year, LeagueTier.Pro).SequenceEqual(oCoin));

        // Team_Seasons is idempotent on a replayed flush (DB, synthetic HS season).
        Slice134World w = World134(schemaPath);
        int hsYear = Slice134BaseYear;
        int games = WriteSyntheticSeason(w, LeagueTier.HS, hsYear, HashPower, 0x1340_51UL);
        LeagueSimulator hs = w.Sim(LeagueTier.HS);
        var writer = new TeamSeasonsWriter(w.Queries);
        string Dump() => DumpQueries(w.Db, new[] { $"SELECT * FROM Team_Seasons WHERE season_year = {hsYear} ORDER BY team_id;" });
        w.Db.RunInBatch(() => writer.Recompute(hsYear, LeagueTier.HS, hs.TeamIds, hs.ScheduleSeed));
        string first = Dump();
        w.Db.RunInBatch(() => writer.Recompute(hsYear, LeagueTier.HS, hs.TeamIds, hs.ScheduleSeed));
        string second = Dump();
        var rows = new List<TeamSeasonRow>();
        w.Queries.LoadTeamSeasons(hsYear, LeagueTier.HS, rows);
        Check("Team_Seasons (D1-A) is an idempotent recompute: a replayed flush leaves every row identical; ΣW = ΣL = games; HS has no rank or seed",
            first == second && rows.Count == 12 && rows.Sum(r => r.Wins) == games && rows.Sum(r => r.Losses) == games
            && rows.All(r => r.NationalRank is null && r.Seed is null), $"{rows.Count} rows, {games} games, {Sha12(first)}");
    }

    // ------------------------------------------------------------------
    // hs-playoffs
    // ------------------------------------------------------------------

    private static void RunHsPlayoffsSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 HS playoffs (6 teams: top 3 per conference, #1s bye; Fridays 131/138/145) ---");
        Slice134World w = World134(schemaPath);
        int year = Slice134BaseYear + 10;
        WriteSyntheticSeason(w, LeagueTier.HS, year, HashPower, 0x1340_52UL);
        LeagueSimulator hs = w.Sim(LeagueTier.HS);
        var playoffs = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.HS));
        DrivePostseason(playoffs, hs, year, PostseasonCalendar.SelectionDay(LeagueTier.HS), PostseasonCalendar.LastDay(LeagueTier.HS));

        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        var seasons = new List<TeamSeasonRow>();
        w.Queries.LoadSeries(year, LeagueTier.HS, series);
        w.Queries.LoadPostseasonGames(year, LeagueTier.HS, games);
        w.Queries.LoadTeamSeasons(year, LeagueTier.HS, seasons);
        List<TeamRow> teams = TeamsOfTier(w, LeagueTier.HS);
        int ConfOf(int id) => teams.First(t => t.TeamId == id).ConferenceId;
        static bool AtLeast(TeamSeasonRow a, TeamSeasonRow b) =>
            (long)a.Wins * (b.Wins + b.Losses) >= (long)b.Wins * (a.Wins + a.Losses);
        var seeded = seasons.Where(r => r.Seed.HasValue).ToList();
        bool field = seeded.Count == 6 && seeded.GroupBy(r => ConfOf(r.TeamId))
            .All(g => g.Select(r => r.Seed!.Value).OrderBy(x => x).SequenceEqual(new[] { 1, 2, 3 }));
        bool seedsByRecord = seeded.GroupBy(r => ConfOf(r.TeamId)).All(g =>
        {
            var ordered = g.OrderBy(r => r.Seed).ToList();
            return ordered.Zip(ordered.Skip(1)).All(p => AtLeast(p.First, p.Second))
                && seasons.Where(r => ConfOf(r.TeamId) == g.Key && !r.Seed.HasValue).All(r => AtLeast(ordered[^1], r));
        });
        Check("HS field: 6 teams, seeds 1–3 per conference in D2 record order", field && seedsByRecord,
            string.Join(" ", seeded.OrderBy(r => r.TeamId).Select(r => $"{r.TeamId}#{r.Seed}")));

        int SeedOf(int id) => seasons.First(r => r.TeamId == id).Seed ?? 0;
        var qf = series.Where(s => s.Round == 1).ToList();
        var cf = series.Where(s => s.Round == 2).ToList();
        var state = series.Where(s => s.Round == 3).ToList();
        Check("HS byes and hosting: QF = #2 (host) v #3 on day 131; conference finals = #1 (host) v the QF winner on 138",
            qf.Count == 2 && qf.All(s => SeedOf(s.HighSeedTeamId) == 2 && SeedOf(s.LowSeedTeamId) == 3)
            && cf.Count == 2 && cf.All(s => SeedOf(s.HighSeedTeamId) == 1 && qf.Any(q => q.WinnerTeamId == s.LowSeedTeamId))
            && series.All(s => s.BestOf == 1));
        (bool legal, string detail) = SeriesShapeLegal(LeagueTier.HS, series, games);
        var table = new SeasonTable();
        var completed = new List<CompletedGame>();
        w.Queries.LoadCompletedRegularSeason(year, LeagueTier.HS, completed);
        table.Build(hs.TeamIds, completed);
        bool stateHost = state.Count == 1 && Standings.Order(table, new[] { state[0].HighSeedTeamId, state[0].LowSeedTeamId },
            hs.ScheduleSeed, year, LeagueTier.HS)[0] == state[0].HighSeedTeamId;
        Check("HS State Championship on 145 between the conference champions, hosted by the better record (D2)",
            stateHost && cf.All(c => c.IsDecided && state[0].Involves(c.WinnerTeamId!.Value)));
        Check("HS: 5 Bo1 series, dates 131/138/145 (Fridays), exactly one champion", legal && series.Count == 5 && games.Count == 5
            && games.Select(g => g.DayOfSeason).Distinct().OrderBy(d => d).SequenceEqual(new[] { 131, 138, 145 })
            && series.Count(s => s.Round == 3 && s.IsDecided) == 1, detail);
    }

    // ------------------------------------------------------------------
    // college-ranking (CS-M §6, 6 checks + the 4-season smoke)
    // ------------------------------------------------------------------

    private static void RunCollegeRankingSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 College National Ranking (CS-M §3: .50 W + .30 SOS + .20 RD_norm; Amendment 2 ties) ---");
        const ulong seed = SeasonScheduler.DefaultScheduleSeed;

        // 1. Arithmetic and clamp: 1 beat 2 20–0 (RD/G clamps to ±5); 3 never played.
        SeasonTable t = TableOf(3, (1, 2, 20, 0));
        List<RankingCalculator.Entry> r = RankingCalculator.Rank(t, seed, 2026, RankingWeights.Default);
        RankingCalculator.Entry e1 = r.First(x => x.TeamId == 1), e2 = r.First(x => x.TeamId == 2), e3 = r.First(x => x.TeamId == 3);
        Check("college-ranking 1: score arithmetic and the ±5 RD/G clamp (.70 / .50 / .30)",
            Math.Abs(e1.Score - 0.70) < 1e-12 && Math.Abs(e3.Score - 0.50) < 1e-12 && Math.Abs(e2.Score - 0.30) < 1e-12
            && r.Select(x => x.TeamId).SequenceEqual(new[] { 1, 3, 2 }), $"{e1.Score:R} {e3.Score:R} {e2.Score:R}");

        // 2. SOS weights each completed meeting: 1 meets 2 twice and 3 once.
        SeasonTable sos = TableOf(4, (1, 2, 2, 1), (2, 1, 2, 1), (1, 3, 3, 0), (2, 4, 1, 0), (3, 4, 1, 0), (4, 3, 1, 0));
        RankingCalculator.Entry s1 = RankingCalculator.Rank(sos, seed, 2026, RankingWeights.Default).First(x => x.TeamId == 1);
        long expectWins = 2L * sos.Wins(1) + sos.Wins(2);
        long expectGames = 2L * sos.Games(1) + sos.Games(2);
        Check("college-ranking 2: SOS = Σ opponents' wins / Σ opponents' games, once per completed meeting (opponents' records include the team)",
            s1.OppWins == expectWins && s1.OppGames == expectGames && Math.Abs(s1.Sos - (double)expectWins / expectGames) < 1e-15,
            $"{s1.OppWins}/{s1.OppGames}");

        // 3. Zero games: every term is ½, every score exactly .5.
        SeasonTable zero = TableOf(24);
        List<RankingCalculator.Entry> z = RankingCalculator.Rank(zero, seed, 2026, RankingWeights.Default);
        Check("college-ranking 3: zero games ⇒ W = SOS = RD_norm = ½, score .5 for all 24",
            z.All(x => x.Score == 0.5 && x.WinPct == 0.5 && x.Sos == 0.5));

        // 4. Exact ties: seeded-coin ordering with the team-id collision fallback (Amendment 2).
        bool coinOrder = z.Zip(z.Skip(1)).All(p =>
            PostseasonStreams.RankingCoinKey(seed, 2026, LeagueTier.College, p.First.TeamId)
            < PostseasonStreams.RankingCoinKey(seed, 2026, LeagueTier.College, p.Second.TeamId));
        int lowHalfTop = 0;
        for (int y = 0; y < 64; y++)
        {
            lowHalfTop += RankingCalculator.Rank(zero, seed, 3000 + y, RankingWeights.Default).Take(12).Count(x => x.TeamId <= 12);
        }
        var a = new RankingCalculator.Entry(7, 0, 0.5, 0, 0, 0, 0, 0);
        var b = new RankingCalculator.Entry(9, 0, 0.5, 0, 0, 0, 0, 0);
        bool fallback = RankingCalculator.Compare(in a, in b, 42UL, 42UL) < 0 && RankingCalculator.Compare(in b, in a, 42UL, 42UL) > 0;
        Check("college-ranking 4: ties/seeded-coin ordering with ID collision fallback (no low-id bias across seasons)",
            coinOrder && fallback && lowHalfTop is > 307 and < 461,
            $"ids 1–12 hold {lowHalfTop}/{64 * 12} top-12 places over 64 tied seasons (½ = 384)");

        // The 4-season smoke (CS-M) on the shared world: real macro College seasons.
        Slice134World w = World134(schemaPath);
        var smoke = Stopwatch.StartNew();
        var completed = new List<CompletedGame>();
        var seasons = new List<TeamSeasonRow>();
        bool weekly = true, permutations = true, idempotent = true, repeat20 = true;
        int smokeYear = 0;
        LeagueSimulator? lastSim = null;
        for (int k = 0; k < 4; k++)
        {
            int year = Slice134BaseYear + 20 + 2 * k;
            var sim = new LeagueSimulator(w.Db, w.Baseball, w.Normalizer, new RngState(0x1340_C0DEUL + (ulong)k), LeagueTier.College);
            sim.Initialize();
            int firstSunday = SundayOnOrAfter(TierSeasonCalendar.GameDay(LeagueTier.College, 1));
            for (int d = 1; d <= TierSeasonCalendar.RegularSeasonLastDay(LeagueTier.College); d++)
            {
                sim.ProcessDay(new DayAdvancedEvent(Abs134(year, d), year, d));
                if (d == firstSunday)
                {
                    w.Queries.LoadTeamSeasons(year, LeagueTier.College, seasons);
                    weekly &= seasons.Count == 24 && seasons.All(x => x.NationalRank.HasValue)
                        && seasons.Select(x => x.NationalRank!.Value).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 24));
                }
            }
            sim.FlushPending();
            w.Queries.LoadTeamSeasons(year, LeagueTier.College, seasons);
            permutations &= seasons.Count == 24
                && seasons.Select(x => x.NationalRank ?? 0).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 24));
            string Dump() => DumpQueries(w.Db, new[] { $"SELECT * FROM Team_Seasons WHERE season_year = {year} ORDER BY team_id;" });
            string before = Dump();
            var writer = new TeamSeasonsWriter(w.Queries);
            w.Db.RunInBatch(() => writer.Recompute(year, LeagueTier.College, sim.TeamIds, sim.ScheduleSeed));
            idempotent &= before == Dump();
            w.Queries.LoadCompletedRegularSeason(year, LeagueTier.College, completed);
            var table = new SeasonTable();
            table.Build(sim.TeamIds, completed);
            List<RankingCalculator.Entry> reference = RankingCalculator.Rank(table, sim.ScheduleSeed, year, RankingWeights.Default);
            for (int rep = 0; rep < 20; rep++)
            {
                var again = new SeasonTable();
                again.Build(sim.TeamIds, completed);
                repeat20 &= RankingCalculator.Rank(again, sim.ScheduleSeed, year, RankingWeights.Default).SequenceEqual(reference);
            }
            smokeYear = year;
            lastSim = sim;
        }
        smoke.Stop();
        Console.WriteLine($"  4-season College ranking smoke: {smoke.ElapsedMilliseconds} ms");
        Check("college-ranking smoke: 4 macro seasons — ranks written from the first Sunday flush, a permutation of 1–24 every flush; a replayed flush is identical",
            weekly && permutations && idempotent);
        Check("college-ranking 6: 20 repeats on saved inputs give identical ranks, scores and order", repeat20);

        // 5. Selection: 17 automatic + a distinct #18/#19 play-in (#18 hosts).
        var directory = new LeagueDirectory();
        directory.Register(lastSim!);
        var playoffs = new PlayoffManager(w.Db, w.Baseball, directory);
        int selection = PostseasonCalendar.SelectionDay(LeagueTier.College);
        playoffs.ProcessDay(lastSim!, new DayAdvancedEvent(Abs134(smokeYear, selection), smokeYear, selection));
        w.Queries.LoadTeamSeasons(smokeYear, LeagueTier.College, seasons);
        var series = new List<PlayoffSeriesRow>();
        w.Queries.LoadSeries(smokeYear, LeagueTier.College, series);
        int RankOf(int id) => seasons.First(x => x.TeamId == id).NationalRank!.Value;
        var auto = seasons.Where(x => x.Seed.HasValue).ToList();
        Check("college-ranking 5: ranks 1–17 qualify automatically (seeded by rank inside each conference); #18 hosts #19 in the play-in",
            auto.Count == 17 && auto.All(x => x.NationalRank <= 17) && series.Count == 1 && series[0].Round == 1
            && RankOf(series[0].HighSeedTeamId) == 18 && RankOf(series[0].LowSeedTeamId) == 19,
            series.Count == 1 ? $"automatic {auto.Count}, play-in #{RankOf(series[0].HighSeedTeamId)} v #{RankOf(series[0].LowSeedTeamId)}" : $"{series.Count} series");
    }

    // ------------------------------------------------------------------
    // college-bracket: every conference split, fixed trees, aligned finals
    // ------------------------------------------------------------------

    private static void RunCollegeBracketSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 College brackets (D3/Amendment 4: 16-slot R1–R4 or 8-slot R2–R4, no reseeding; both end on 167) ---");
        Slice134World w = World134(schemaPath);
        List<TeamRow> teams = TeamsOfTier(w, LeagueTier.College);
        int eastId = teams.Min(t => t.ConferenceId);
        int[] east = teams.Where(t => t.ConferenceId == eastId).Select(t => t.TeamId).OrderBy(x => x).ToArray();
        int[] west = teams.Where(t => t.ConferenceId != eastId).Select(t => t.TeamId).OrderBy(x => x).ToArray();
        LeagueSimulator college = w.Sim(LeagueTier.College);
        int[] leaves16 = { 1, 16, 8, 9, 4, 13, 5, 12, 2, 15, 7, 10, 3, 14, 6, 11 };
        int[] leaves8 = { 1, 8, 4, 5, 2, 7, 3, 6 };
        var failures = new List<string>();
        var playInFailures = new List<string>();
        var seriesRows = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        var seasons = new List<TeamSeasonRow>();
        for (int k = 6; k <= 12; k++)
        {
            int year = Slice134BaseYear + 40 + k;
            // A ranking with exactly k East qualifiers whichever team wins the play-in.
            bool playInEast = k <= 9;
            int autoEast = playInEast ? k - 1 : k;
            int autoWest = 17 - autoEast;
            var ranked = new List<int>();
            for (int i = 0, e = 0, v = 0; i < 17; i++)
            {
                bool takeEast = e < autoEast && (v >= autoWest || i % 2 == 0);
                ranked.Add(takeEast ? east[e++] : west[v++]);
            }
            int[] pool = playInEast ? east.Skip(autoEast).ToArray() : west.Skip(autoWest).ToArray();
            ranked.Add(pool[0]);
            ranked.Add(pool[1]);
            ranked.AddRange(east.Concat(west).Where(id => !ranked.Contains(id)));
            var playoffs = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.College));
            w.Db.RunInBatch(() =>
            {
                for (int i = 0; i < 24; i++)
                {
                    w.Queries.UpsertTeamSeason(ranked[i], year, 0, 0, 0, 0, i + 1);
                }
                playoffs.SelectCollegeFromRanking(year, ranked);
            });
            DrivePostseason(playoffs, college, year, PostseasonCalendar.SelectionDay(LeagueTier.College), PostseasonCalendar.LastDay(LeagueTier.College));

            w.Queries.LoadSeries(year, LeagueTier.College, seriesRows);
            w.Queries.LoadPostseasonGames(year, LeagueTier.College, games);
            w.Queries.LoadTeamSeasons(year, LeagueTier.College, seasons);
            int Conf(int id) => east.Contains(id) ? 0 : 1;
            int Seed(int id) => seasons.First(x => x.TeamId == id).Seed ?? 0;
            int RankOf(int id) => seasons.First(x => x.TeamId == id).NationalRank!.Value;
            (bool legal, string detail) = SeriesShapeLegal(LeagueTier.College, seriesRows, games);
            if (!legal)
            {
                failures.Add($"k={k}: {detail}");
            }
            PlayoffSeriesRow playIn = seriesRows.Single(s => s.Round == 1);
            int pw = playIn.WinnerTeamId!.Value;
            int[] m = { seasons.Count(x => x.Seed.HasValue && Conf(x.TeamId) == 0), seasons.Count(x => x.Seed.HasValue && Conf(x.TeamId) == 1) };
            PostseasonGameRow playInGame = games.Single(g => g.SeriesId == playIn.SeriesId);
            if (m[0] != k || m[1] != 18 - k || Seed(pw) != m[Conf(pw)] || playInGame.DayOfSeason != 139 || playInGame.Phase != 1
                || playIn.HighSeedTeamId != ranked[17])
            {
                playInFailures.Add($"k={k}: split {m[0]}+{m[1]}, play-in winner {pw} seed {Seed(pw)} (conf size {m[Conf(pw)]}), day {playInGame.DayOfSeason}");
            }
            for (int c = 0; c < 2; c++)
            {
                bool big = m[c] >= 9;
                int[] leaves = big ? leaves16 : leaves8;
                int first = big ? 2 : 3;
                var conf = seriesRows.Where(s => s.Round is >= 2 and <= 5 && Conf(s.HighSeedTeamId) == c).ToList();
                int expectedFirstRound = big ? m[c] - 8 : m[c] - 4;
                bool shape = conf.Count == m[c] - 1 && conf.Count(s => s.Round == first) == expectedFirstRound
                    && conf.Count(s => s.Round == 5) == 1 && (big || conf.All(s => s.Round >= 3))
                    && conf.All(s => Conf(s.LowSeedTeamId) == c);
                // No reseeding: two seeds meet at level L only inside the same fixed subtree.
                foreach (PlayoffSeriesRow s in conf)
                {
                    int level = s.Round - first + 1;
                    int pa = Array.IndexOf(leaves, Seed(s.HighSeedTeamId));
                    int pb = Array.IndexOf(leaves, Seed(s.LowSeedTeamId));
                    shape &= level >= 1 && pa >= 0 && pb >= 0 && (pa >> level) == (pb >> level) && (pa >> (level - 1)) != (pb >> (level - 1))
                        && Seed(s.HighSeedTeamId) < Seed(s.LowSeedTeamId);
                }
                if (!shape)
                {
                    failures.Add($"k={k} conf {c} (m={m[c]}): {conf.Count} series " +
                        string.Join(" ", conf.Select(s => $"r{s.Round}:{Seed(s.HighSeedTeamId)}v{Seed(s.LowSeedTeamId)}")));
                }
            }
            PlayoffSeriesRow final = seriesRows.Single(s => s.Round == 6);
            var champs = seriesRows.Where(s => s.Round == 5).Select(s => s.WinnerTeamId!.Value).ToList();
            if (!(champs.Count == 2 && final.Involves(champs[0]) && final.Involves(champs[1])
                && RankOf(final.HighSeedTeamId) < RankOf(final.LowSeedTeamId) && seriesRows.Count == 18
                && games.Count(g => g.DayOfSeason == 146) == Math.Max(0, m[0] - 8) + Math.Max(0, m[1] - 8)))
            {
                failures.Add($"k={k}: final {final.HighSeedTeamId}v{final.LowSeedTeamId}, {seriesRows.Count} series");
            }
        }
        Check("college-bracket: every split 6+12 … 12+6 gives legal byes, fixed trees (no reseeding), aligned conference finals on 167, an 18-series postseason",
            failures.Count == 0, failures.Count == 0 ? "7 splits" : string.Join("; ", failures));
        Check("college-bracket: the play-in is phase 1 on day 139 (#18 hosts) and its winner joins its own conference as the last seed",
            playInFailures.Count == 0, string.Join("; ", playInFailures));
    }

    // ------------------------------------------------------------------
    // college-series (CS-M §6, 10 checks): The Red Dirt Series
    // ------------------------------------------------------------------

    private static void RunCollegeSeriesSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 The Red Dirt Series (Bo3 Thu–Fri–Sat; no-repeat SP + RP; narrow forfeit; reload identity) ---");
        Slice134World w = World134(schemaPath);
        LeagueSimulator college = w.Sim(LeagueTier.College);
        const int final = PostseasonCalendar.CollegeFinalRound;
        int collegeBase = LeagueStructure.TeamIdBase(LeagueTier.College);
        int teamA = collegeBase + 1;
        int teamB = collegeBase + 2;
        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();

        // 1–3. The six two-win paths through the manager's own series logic.
        string[] paths = { "AA", "BB", "ABA", "ABB", "BAA", "BAB" };
        bool pathsOk = true, noGame3 = true, slots = true;
        for (int p = 0; p < paths.Length; p++)
        {
            int year = Slice134BaseYear + 60 + p;
            var playoffs = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.College));
            w.Db.RunInBatch(() => playoffs.CreateSeries(year, LeagueTier.College, final, teamA, teamB));
            foreach (char winner in paths[p])
            {
                w.Queries.LoadPostseasonGames(year, LeagueTier.College, games);
                PostseasonGameRow next = games.Single(g => !g.IsPlayed);
                bool homeWins = (winner == 'A') == (next.HomeTeamId == teamA);
                playoffs.RecordResult(next, 1, homeWins ? 4 : 1, homeWins ? 1 : 4);
            }
            w.Queries.LoadSeries(year, LeagueTier.College, series);
            w.Queries.LoadPostseasonGames(year, LeagueTier.College, games);
            int expectedWinner = paths[p][^1] == 'A' ? teamA : teamB;
            pathsOk &= series.Count == 1 && series[0].WinnerTeamId == expectedWinner && games.Count == paths[p].Length
                && Math.Max(series[0].HighWins, series[0].LowWins) == 2 && SeriesShapeLegal(LeagueTier.College, series, games).Legal;
            noGame3 &= paths[p].Length == 3 || games.All(g => g.GameInSeries <= 2);
            slots &= games.All(g => GameCalendar.WeekdayForDayOfSeason(g.DayOfSeason)
                == (g.GameInSeries switch { 1 => Weekday.Thursday, 2 => Weekday.Friday, _ => Weekday.Saturday })
                && g.HomeTeamId == (g.GameInSeries == 2 ? teamB : teamA));
        }
        Check("college-series 1: all six two-win paths AA, BB, ABA, ABB, BAA, BAB end at 2 wins with the right winner", pathsOk);
        Check("college-series 2: no game-3 row after 2–0 (rows on certainty)", noGame3);
        Check("college-series 3: games on Thu 172 / Fri 173 / Sat 174; the high seed (better selection rank) hosts games 1 and 3", slots);

        // 4. Healthy A/B/C: three different arms in quality order.
        int year4 = Slice134BaseYear + 90;
        long d0 = Abs134(year4, PostseasonCalendar.GameDay(LeagueTier.College, final, 1));
        int teamC = collegeBase + 3;
        int teamD = collegeBase + 4;
        for (int g = 0; g < 3; g++)
        {
            RngState rng = PostseasonStreams.ForGame(college.ScheduleSeed, year4, LeagueTier.College, final, teamC, teamD, g + 1);
            college.SimulatePostseasonGame(teamC, teamD, d0 + g, d0, ref rng, out _, out _);
            college.ClearPostseasonGameState(revertStarts: false);
        }
        bool abc = Enumerable.Range(0, 3).All(k =>
            college.RestLedger.TryGetLastStart(college.StarterId(teamC, k), out long last) && last == d0 + k);
        Check("college-series 4: a healthy staff starts A, B, C (quality order) in games 1–3", abc);

        // 5. No repeat covers RP openers and role changes: every starter and reliever #1
        // (who started game 1 before moving to the bullpen) used ⇒ reliever #2 opens game 2, #3 game 3.
        int year5 = Slice134BaseYear + 91;
        long f5 = Abs134(year5, PostseasonCalendar.GameDay(LeagueTier.College, final, 1));
        int teamE = collegeBase + 5;
        int teamF = collegeBase + 6;
        (int starters, int relievers) = college.StaffCounts(teamE);
        for (int k = 0; k < starters; k++)
        {
            college.RestLedger.RecordStart(college.StarterId(teamE, k), f5);
        }
        college.RestLedger.RecordStart(college.RelieverId(teamE, 0), f5);
        int bullpenBefore = college.PostseasonBullpenGames;
        for (int g = 1; g < 3; g++)
        {
            RngState rng = PostseasonStreams.ForGame(college.ScheduleSeed, year5, LeagueTier.College, final, teamE, teamF, g + 1);
            college.SimulatePostseasonGame(teamE, teamF, f5 + g, f5, ref rng, out _, out _);
            college.ClearPostseasonGameState(revertStarts: false);
        }
        bool rpNoRepeat = relievers >= 3
            && college.RestLedger.TryGetLastStart(college.RelieverId(teamE, 0), out long r1) && r1 == f5
            && college.RestLedger.TryGetLastStart(college.RelieverId(teamE, 1), out long r2) && r2 == f5 + 1
            && college.RestLedger.TryGetLastStart(college.RelieverId(teamE, 2), out long r3) && r3 == f5 + 2
            && college.PostseasonBullpenGames - bullpenBefore == 2;
        Check("college-series 5: no repeat, including RP openers and a role change (an SP-turned-RP who started game 1 can't open)",
            rpNoRepeat, $"{starters} SP / {relievers} RP");

        // 6. Depleted staff: no legal pitcher ⇒ the narrow forfeit (9–0, status 2, a series win; no start recorded).
        int year6 = Slice134BaseYear + 92;
        int teamG = collegeBase + 7;
        int teamH = collegeBase + 8;
        var forfeitPlayoffs = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.College));
        w.Db.RunInBatch(() => forfeitPlayoffs.CreateSeries(year6, LeagueTier.College, final, teamG, teamH));
        w.Queries.LoadPostseasonGames(year6, LeagueTier.College, games);
        PostseasonGameRow g1 = games.Single();
        forfeitPlayoffs.RecordResult(g1, 1, g1.HomeTeamId == teamH ? 5 : 2, g1.HomeTeamId == teamH ? 2 : 5); // H wins game 1
        long f6 = Abs134(year6, g1.DayOfSeason);
        (int sG, int rG) = college.StaffCounts(teamG);
        for (int k = 0; k < sG; k++)
        {
            college.RestLedger.RecordStart(college.StarterId(teamG, k), f6);
        }
        for (int k = 0; k < rG; k++)
        {
            college.RestLedger.RecordStart(college.RelieverId(teamG, k), f6);
        }
        bool hadAce = college.RestLedger.TryGetLastStart(college.StarterId(teamH, 0), out long hAceBefore);
        int day2 = PostseasonCalendar.GameDay(LeagueTier.College, final, 2);
        forfeitPlayoffs.ProcessDay(college, new DayAdvancedEvent(Abs134(year6, day2), year6, day2));
        w.Queries.LoadPostseasonGames(year6, LeagueTier.College, games);
        w.Queries.LoadSeries(year6, LeagueTier.College, series);
        PostseasonGameRow g2 = games.Single(g => g.GameInSeries == 2);
        bool hAceSame = college.RestLedger.TryGetLastStart(college.StarterId(teamH, 0), out long hAceAfter) == hadAce && hAceAfter == hAceBefore;
        bool forfeit = g2.Status == 2 && g2.HomeTeamId == teamH && g2.HomeScore == TierRules.RegulationInnings(LeagueTier.College) && g2.AwayScore == 0
            && series[0].WinnerTeamId == teamH && series[0].LowWins == 2 && games.Count == 2 && hAceSame;
        Check("college-series 6: depleted staff (every SP and RP used) ⇒ the narrow forfeit: 9–0, status 2, a series win, no start recorded",
            forfeit, $"status {g2.Status} {g2.HomeScore}-{g2.AwayScore}, winner {series[0].WinnerTeamId}");

        // 7–9. Reload identity, frozen rank, played-season totals on a full synthetic College postseason.
        int year7 = Slice134BaseYear + 93;
        WriteSyntheticSeason(w, LeagueTier.College, year7, HashPower, 0x1340_53UL);
        w.Db.CheckpointForSync();
        string pathX = Slice134TempPath("x");
        string pathY = Slice134TempPath("y");
        File.Copy(w.Path, pathX, overwrite: true);
        File.Copy(w.Path, pathY, overwrite: true);
        int g1Day = PostseasonCalendar.GameDay(LeagueTier.College, final, 1);
        PostseasonCopyRun straight = RunCollegePostseasonCopy(pathX, year7, reloadAfterDay: 0);
        PostseasonCopyRun reloaded = RunCollegePostseasonCopy(pathY, year7, reloadAfterDay: g1Day);
        Check("college-series 7: reload mid-series (after The Red Dirt Series game 1) replays the same bracket, dates, scores and champion",
            straight.Dump == reloaded.Dump, $"{Sha12(straight.Dump)} / {Sha12(reloaded.Dump)}");
        bool frozen = straight.RanksAtSelection.Count == 24 && straight.RanksAtSelection.SequenceEqual(straight.RanksAfterReflush);
        Check("college-series 8: the national rank is frozen at selection (a flush after the postseason doesn't move it)", frozen);
        Check("college-series 9: played-season totals — 18 series, 19–20 games, exactly one champion",
            straight.Series == 18 && straight.Games is 19 or 20 && straight.Champions == 1,
            $"{straight.Series} series, {straight.Games} games");

        // 10. 100,000 fair-coin series at the pinned seed (Amendment 7).
        var coin = new RngState(FairCoinSeed);
        int deciding = 0;
        for (int i = 0; i < 100_000; i++)
        {
            bool first = coin.NextDouble() < 0.5;
            bool second = coin.NextDouble() < 0.5;
            if (first != second)
            {
                deciding++;
                coin.NextDouble(); // game 3 exists only at 1–1
            }
        }
        double share = deciding / 100_000.0;
        Check("college-series 10: 100,000 fair-coin Bo3 series — deciding-game share in .495–.505 (49,965 at FairCoinSeed)",
            deciding == 49_965 && share is >= 0.495 and <= 0.505, $"{deciding:N0} ({share:F5})");
    }

    private sealed record PostseasonCopyRun(
        string Dump, List<(int TeamId, int? Rank)> RanksAtSelection, List<(int TeamId, int? Rank)> RanksAfterReflush,
        int Series, int Games, int Champions);

    /// <summary>
    /// Plays a whole College postseason on a copy of the shared world; when
    /// <paramref name="reloadAfterDay"/> &gt; 0 the database, sim, rest ledger
    /// and manager are all thrown away after that day and rebuilt from disk.
    /// </summary>
    private static PostseasonCopyRun RunCollegePostseasonCopy(string path, int year, int reloadAfterDay)
    {
        int selection = PostseasonCalendar.SelectionDay(LeagueTier.College);
        int last = PostseasonCalendar.LastDay(LeagueTier.College);
        var ranksAtSelection = new List<(int, int?)>();
        DatabaseManager db = new DatabaseManager(path);
        try
        {
            int from = selection;
            int[] stops = reloadAfterDay > 0 ? new[] { reloadAfterDay, last } : new[] { last };
            for (int pass = 0; pass < stops.Length; pass++)
            {
                if (pass > 0)
                {
                    db.Dispose();
                    db = new DatabaseManager(path); // the reload: fresh connection, sim, rest ledger and manager
                }
                var baseball = new BaseballQueries(db);
                var sim = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(1), LeagueTier.College);
                sim.Initialize();
                sim.PrepareSeason(year);
                var directory = new LeagueDirectory();
                directory.Register(sim);
                var playoffs = new PlayoffManager(db, baseball, directory);
                for (int d = from; d <= stops[pass]; d++)
                {
                    playoffs.ProcessDay(sim, new DayAdvancedEvent(Abs134(year, d), year, d));
                    if (d == selection)
                    {
                        var rows = new List<TeamSeasonRow>();
                        new PlayoffQueries(db).LoadTeamSeasons(year, LeagueTier.College, rows);
                        ranksAtSelection.AddRange(rows.Select(r => (r.TeamId, r.NationalRank)));
                    }
                }
                from = stops[pass] + 1;
            }
            var q = new PlayoffQueries(db);
            var series = new List<PlayoffSeriesRow>();
            var games = new List<PostseasonGameRow>();
            q.LoadSeries(year, LeagueTier.College, series);
            q.LoadPostseasonGames(year, LeagueTier.College, games);
            string dump = DumpQueries(db, new[]
            {
                $"SELECT round, best_of, high_seed_team_id, low_seed_team_id, high_wins, low_wins, winner_team_id FROM Playoff_Series WHERE season_year = {year} ORDER BY round, high_seed_team_id;",
                $"SELECT day_of_season, home_team_id, away_team_id, phase, game_in_series, status, home_score, away_score FROM Schedule_Games WHERE season_year = {year} AND phase IN (1, 2) ORDER BY day_of_season, home_team_id;",
                $"SELECT team_id, national_rank, seed FROM Team_Seasons WHERE season_year = {year} ORDER BY team_id;",
            });
            // A replayed flush after the postseason: the national rank must not move.
            var baseballAfter = new BaseballQueries(db);
            var simAfter = new LeagueSimulator(db, baseballAfter, new StatsNormalizer(db, baseballAfter), new RngState(1), LeagueTier.College);
            simAfter.Initialize();
            var writer = new TeamSeasonsWriter(q);
            db.RunInBatch(() => writer.Recompute(year, LeagueTier.College, simAfter.TeamIds, simAfter.ScheduleSeed));
            var after = new List<TeamSeasonRow>();
            q.LoadTeamSeasons(year, LeagueTier.College, after);
            return new PostseasonCopyRun(dump, ranksAtSelection, after.Select(r => (r.TeamId, r.NationalRank)).ToList(),
                series.Count, games.Count, series.Count(s => s.Round == PostseasonCalendar.CollegeFinalRound && s.IsDecided));
        }
        finally
        {
            db.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // pro-playoffs
    // ------------------------------------------------------------------

    private static void RunProPlayoffsSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 Pro playoffs (12 teams; division winners 1–4, wild cards 5–6; WC/Div/CF Bo3, World Championship Bo5) ---");
        Slice134World w = World134(schemaPath);
        int year = Slice134BaseYear + 100;
        List<TeamRow> teams = TeamsOfTier(w, LeagueTier.Pro);
        int conf0 = teams.Min(t => t.ConferenceId);
        int[] divisions = teams.Where(t => t.ConferenceId == conf0).Select(t => t.DivisionId!.Value).Distinct().OrderBy(x => x).ToArray();
        // One conference stacks its first division with the four strongest teams and
        // its second with the four weakest: that division's winner has a worse record
        // than the stacked division's runners-up, yet still takes a top-4 seed.
        long Power(int teamId)
        {
            TeamRow t = teams.First(x => x.TeamId == teamId);
            if (t.ConferenceId == conf0 && t.DivisionId == divisions[0])
            {
                return 1_000_000_000L + teamId;
            }
            if (t.ConferenceId == conf0 && t.DivisionId == divisions[1])
            {
                return teamId;
            }
            return 1000 + HashPower(teamId) % 100_000_000L;
        }
        WriteSyntheticSeason(w, LeagueTier.Pro, year, Power, 0x1340_54UL);
        LeagueSimulator pro = w.Sim(LeagueTier.Pro);
        var playoffs = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.Pro));
        DrivePostseason(playoffs, pro, year, PostseasonCalendar.SelectionDay(LeagueTier.Pro), PostseasonCalendar.LastDay(LeagueTier.Pro));

        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        var seasons = new List<TeamSeasonRow>();
        w.Queries.LoadSeries(year, LeagueTier.Pro, series);
        w.Queries.LoadPostseasonGames(year, LeagueTier.Pro, games);
        w.Queries.LoadTeamSeasons(year, LeagueTier.Pro, seasons);
        TeamSeasonRow Row(int id) => seasons.First(x => x.TeamId == id);
        int Seed(int id) => Row(id).Seed ?? 0;
        int Conf(int id) => teams.First(t => t.TeamId == id).ConferenceId;
        static bool Better(TeamSeasonRow a, TeamSeasonRow b) =>
            (long)a.Wins * (b.Wins + b.Losses) > (long)b.Wins * (a.Wins + a.Losses);
        var seeded = seasons.Where(x => x.Seed.HasValue).ToList();
        Check("Pro field: 12 teams, seeds 1–6 per conference", seeded.Count == 12
            && seeded.GroupBy(x => Conf(x.TeamId)).All(g => g.Select(x => x.Seed!.Value).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 6))));
        // Seeds 1–4 are exactly the division winners (no team in a winner's division has a better record).
        bool winnersTop4 = seeded.Where(x => x.Seed <= 4).All(x =>
        {
            int? division = teams.First(t => t.TeamId == x.TeamId).DivisionId;
            return teams.Where(t => t.DivisionId == division && t.TeamId != x.TeamId).All(t => !Better(Row(t.TeamId), x));
        }) && seeded.Where(x => x.Seed <= 4).Select(x => teams.First(t => t.TeamId == x.TeamId).DivisionId).Distinct().Count() == 8;
        var c0 = seeded.Where(x => Conf(x.TeamId) == conf0).ToDictionary(x => x.Seed!.Value);
        Check("Pro seeding: division winners take seeds 1–4 even when a wild card has a better record", winnersTop4 && Better(c0[5], c0[4]),
            $"conf {conf0}: #4 {c0[4].Wins}-{c0[4].Losses} v #5 {c0[5].Wins}-{c0[5].Losses}");
        var wc = series.Where(s => s.Round == 1).ToList();
        var div = series.Where(s => s.Round == 2).ToList();
        bool byes = wc.Count == 4 && wc.All(s => Seed(s.HighSeedTeamId) + Seed(s.LowSeedTeamId) == 9 && Seed(s.HighSeedTeamId) is 3 or 4);
        bool reseed = div.Count == 4 && div.GroupBy(s => Conf(s.HighSeedTeamId)).All(g =>
        {
            var wcWinners = wc.Where(s => Conf(s.HighSeedTeamId) == g.Key).Select(s => s.WinnerTeamId!.Value).ToList();
            int lowest = wcWinners.OrderByDescending(Seed).First();
            return g.Any(s => Seed(s.HighSeedTeamId) == 1 && s.LowSeedTeamId == lowest)
                && g.Any(s => Seed(s.HighSeedTeamId) == 2 && s.LowSeedTeamId != lowest);
        });
        Check("Pro: #1/#2 bye; WC 3v6 and 4v5; the Division round reseeds (#1 v the lowest remaining seed)", byes && reseed);
        (bool legal, string detail) = SeriesShapeLegal(LeagueTier.Pro, series, games);
        PlayoffSeriesRow ws = series.SingleOrDefault(s => s.Round == 4);
        Check("Pro: Bo3 hosting 1-3, Bo5 2-2-1, conditional games only while live, exact Sat/Tue/Thu dates; 11 series, one champion",
            legal && series.Count == 11 && ws.IsDecided && ws.BestOf == 5
            && games.All(g => GameCalendar.WeekdayForDayOfSeason(g.DayOfSeason) is Weekday.Tuesday or Weekday.Thursday or Weekday.Saturday),
            $"{detail}; {games.Count} games");
    }

    // ------------------------------------------------------------------
    // championship-event
    // ------------------------------------------------------------------

    private static void RunChampionshipEventSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 ChampionshipWonEvent (tier on every publish; reveal only for Pro; marker + boot republish) ---");
        Slice134World w = World134(schemaPath);
        int year = Slice134BaseYear + 110;
        WriteSyntheticSeason(w, LeagueTier.HS, year, HashPower, 0x1340_55UL);
        LeagueSimulator hs = w.Sim(LeagueTier.HS);
        var bus = new EventBus();
        var champions = new List<ChampionshipWonEvent>();
        var decided = new List<PlayoffSeriesDecidedEvent>();
        bus.Subscribe<ChampionshipWonEvent>(e => champions.Add(e));
        bus.Subscribe<PlayoffSeriesDecidedEvent>(e => decided.Add(e));
        var playoffs = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.HS));
        playoffs.AttachTo(bus);
        DrivePostseason(playoffs, hs, year, PostseasonCalendar.SelectionDay(LeagueTier.HS), PostseasonCalendar.LastDay(LeagueTier.HS), bus);
        bool markerBeforeDispatchDrain = w.GameState.TryGetInt64(PlayoffManager.MarkerKey(year, LeagueTier.HS), out _);
        playoffs.WritePublicationMarkers();
        bool marker = w.GameState.TryGetInt64(PlayoffManager.MarkerKey(year, LeagueTier.HS), out long markedTeam);
        Check("championship-event: exactly one per tier-season, published after the commit with the tier set (HS); marker written after dispatch",
            champions.Count == 1 && champions[0].Tier == (byte)LeagueTier.HS && champions[0].SeasonYear == year
            && decided.Count == 5 && decided.All(d => d.Tier == (byte)LeagueTier.HS)
            && !markerBeforeDispatchDrain && marker && markedTeam == champions[0].TeamId);
        Check("championship-event: IsRevealEligibleTier is false for HS and College, true only for Pro (ProTier pins LeagueTier.Pro)",
            !ChampionshipRules.IsRevealEligibleTier((byte)LeagueTier.HS) && !ChampionshipRules.IsRevealEligibleTier((byte)LeagueTier.College)
            && ChampionshipRules.IsRevealEligibleTier((byte)LeagueTier.Pro) && ChampionshipRules.ProTier == (byte)LeagueTier.Pro);

        // Reload with the marker written: no duplicate. Marker missing (a crash): republished once.
        var rebooted = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.HS));
        rebooted.AttachTo(bus);
        int none = rebooted.RepublishMissingChampions(year);
        bus.DispatchPending();
        ExecSql(w.Db, $"DELETE FROM Game_State WHERE key = '{PlayoffManager.MarkerKey(year, LeagueTier.HS)}';");
        var crashed = new PlayoffManager(w.Db, w.Baseball, w.Leagues(LeagueTier.HS));
        crashed.AttachTo(bus);
        int republished = crashed.RepublishMissingChampions(year);
        bus.DispatchPending();
        crashed.WritePublicationMarkers();
        Check("championship-event: no duplicate on reload; a missing marker republishes the same champion once, then writes the marker",
            none == 0 && republished == 1 && champions.Count == 2 && champions[1].TeamId == champions[0].TeamId
            && champions[1].Tier == (byte)LeagueTier.HS && w.GameState.TryGetInt64(PlayoffManager.MarkerKey(year, LeagueTier.HS), out _),
            $"reload {none}, republished {republished}, events {champions.Count}");
        playoffs.DetachFrom(bus);
        rebooted.DetachFrom(bus);
        crashed.DetachFrom(bus);
    }

    // ------------------------------------------------------------------
    // attended-postseason (Amendment 1) + allocation probes
    // ------------------------------------------------------------------

    private static void RunAttendedPostseasonSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-4 attended postseason (no stat rows, no normalization; result + series + rest + PED commit together) ---");
        string path = Slice134TempPath("att");
        using var db = new DatabaseManager(path);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        var state = new GlobalState();
        var gameState = new GameStateQueries(db);
        var queries = new PlayoffQueries(db);
        var schedule = new ScheduleQueries(db);
        int year = StartYear;
        int avatarTeam = ProTeam(5);

        // Every player on the avatar's team is PED-flagged (the avatar, created later, is not).
        var roster = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.Pro, roster);
        foreach (RosterPlayerRow row in roster.Where(r => r.TeamId == avatarTeam))
        {
            ExecSql(db, $"INSERT INTO Entity_Flags (player_id, flag_name, is_active, set_on_day) VALUES ('{row.PlayerId}', '{LeagueSimulator.PedActiveFlagName}', 1, 1);");
        }

        var pro = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed), LeagueTier.Pro);
        pro.Initialize();
        var leagues = new LeagueDirectory();
        leagues.Register(pro);
        var micro = new MicroGame(db, baseball);
        micro.RestLedger = pro.RestLedger;
        micro.Initialize();
        var career = new CareerManager(db, players, baseball, gameState, state, leagues, micro, new RngState(MicroSeed + 134))
        {
            Normalizer = new StatsNormalizer(db, baseball),
        };
        career.CreateAvatar("You", "Rookie", avatarTeam, new PlayerRatingsRow
        {
            IsPitcher = false, BatPower = 60, BatContact = 60, BatDiscipline = 60,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
        });
        var playoffs = new PlayoffManager(db, baseball, leagues);
        career.Playoffs = playoffs;
        var bus = new EventBus();
        career.AttachTo(bus);
        playoffs.AttachTo(bus);

        // A synthetic regular season the avatar's team dominates (no sim, so no stat rows).
        WriteSyntheticSeason(db, pro, schedule, year,
            id => id == avatarTeam ? long.MaxValue : HashPower(id), 0x1340_56UL);
        // The normalization sentinel: a wrong stored AVG survives only if no NormalizeSeason runs.
        string sentinel = roster.First(r => r.TeamId == ProTeam(9) && !r.IsPitcher).PlayerId;
        db.RunInBatch(() => baseball.AddBattingGameCounts(sentinel, year, 10, 10, 5, 0, 0, 0, 0, 0, 0, sb: 0));
        ExecSql(db, $"UPDATE Batting_Stats SET avg = 0.999 WHERE player_id = '{sentinel}' AND season_year = {year};");

        RngState careerRngBefore = career.RngSnapshot;
        RngState macroRngBefore = pro.RngSnapshot;
        (int microShort, int microBullpen) = (micro.ShortRestStarts, micro.BullpenGames);
        long gameLogsBefore = CountRows(db, "SELECT COUNT(*) FROM Game_Logs;");

        // Clock fixture: autopilot off ⇒ the avatar's first postseason game parks (and isn't macro-played).
        career.AutopilotAttendedGames = false;
        int selection = PostseasonCalendar.SelectionDay(LeagueTier.Pro);
        int lastDay = PostseasonCalendar.LastDay(LeagueTier.Pro);
        int parkedOn = 0;
        for (int d = selection; d <= lastDay; d++)
        {
            bus.Publish(new DayAdvancedEvent(Abs134(year, d), year, d));
            bus.DispatchPending();
            if (career.HasPendingGame)
            {
                parkedOn = d;
                break;
            }
        }
        career.TryGetPendingGame(out PendingAttendedGame parked);
        var parkedRows = new List<PostseasonGameRow>();
        queries.LoadPostseasonGames(year, LeagueTier.Pro, parkedRows);
        PostseasonGameRow parkedRow = parkedRows.FirstOrDefault(g => g.GameId == parked.GameId);
        Check("attended: the avatar's postseason game parks on its game day (the clock stops) and the day handler leaves it unplayed",
            parkedOn > 0 && career.PendingIsPostseason && parkedRow.GameId != 0 && !parkedRow.IsPlayed
            && parkedRow.DayOfSeason == parkedOn && parkedRow.Involves(avatarTeam),
            $"parked on day {parkedOn}, game {parked.GameId}");

        // Play it, then autopilot the rest of the postseason.
        var policy = new NeutralBatterPolicy();
        career.PlayPendingGame(ref policy);
        career.AutopilotAttendedGames = true;
        for (int d = parkedOn + 1; d <= lastDay; d++)
        {
            bus.Publish(new DayAdvancedEvent(Abs134(year, d), year, d));
            bus.DispatchPending();
        }

        var series = new List<PlayoffSeriesRow>();
        var games = new List<PostseasonGameRow>();
        queries.LoadSeries(year, LeagueTier.Pro, series);
        queries.LoadPostseasonGames(year, LeagueTier.Pro, games);
        var avatarGames = games.Where(g => g.Involves(avatarTeam)).ToList();
        (bool legal, string detail) = SeriesShapeLegal(LeagueTier.Pro, series, games);
        long statRows = CountRows(db, $"SELECT (SELECT COUNT(*) FROM Batting_Stats WHERE season_year = {year}) + (SELECT COUNT(*) FROM Pitching_Stats WHERE season_year = {year});");
        double sentinelAvg = Convert.ToDouble(db.ExecuteScalar(db.GetPooledCommand(
            $"SELECT avg FROM Batting_Stats WHERE player_id = '{sentinel}' AND season_year = {year};")), System.Globalization.CultureInfo.InvariantCulture);
        Check("attended (Amendment 1): attended phase-2 games write no Batting_Stats / Pitching_Stats row and run no normalization",
            avatarGames.Count > 0 && statRows == 1 && sentinelAvg == 0.999,
            $"{avatarGames.Count} attended games, stat rows {statRows}, sentinel avg {sentinelAvg}");
        long lastAvatarDay = Abs134(year, avatarGames.Max(g => g.DayOfSeason));
        long avatarRest = CountRows(db, $"SELECT COUNT(*) FROM Pitcher_Rest pr JOIN Players p ON p.player_id = pr.player_id WHERE p.team_id = {avatarTeam} AND pr.last_start_day = {lastAvatarDay};");
        long pedHealth = CountRows(db, $"SELECT COALESCE(SUM(100 - health_ceiling), 0) FROM Players WHERE team_id = {avatarTeam};");
        long pedRisk = CountRows(db, $"SELECT COALESCE(SUM(detection_risk), 0) FROM Players WHERE team_id = {avatarTeam};");
        long logs = CountRows(db, "SELECT COUNT(*) FROM Game_Logs;") - gameLogsBefore;
        Check("attended: each result commits with its series update, rest, PED costs and Game_Logs (one batch)",
            avatarGames.All(g => g.IsPlayed) && legal && avatarRest >= 1 && pedHealth > 0
            && pedRisk * LeagueSimulator.PedHealthCostPerGame == pedHealth * LeagueSimulator.PedDetectionRiskPerGame && logs > 0,
            $"{detail}; rest rows {avatarRest}, PED health −{pedHealth} risk +{pedRisk}, logs +{logs}");
        Check("Q7 ch.2 (attended): neither the career's own stream nor the macro sim's _rng advances on postseason games",
            career.RngSnapshot.Equals(careerRngBefore) && pro.RngSnapshot.Equals(macroRngBefore));
        Check("Q7 ch.10 (attended): the micro-sim's regular-season ShortRestStarts/BullpenGames don't move",
            micro.ShortRestStarts == microShort && micro.BullpenGames == microBullpen,
            $"postseason short-rest {micro.PostseasonShortRestStarts}, bullpen {micro.PostseasonBullpenGames}");
        Check("attended: the postseason completes around the avatar — 11 series, one champion",
            series.Count == 11 && series.Count(s => s.Round == 4 && s.IsDecided) == 1);

        // 13-4 review fixes (Argon §12.3): S1 (micro starts undone on cancel and on a
        // rolled-back flush), N1 (macro rollback leaves no phantom day-0 entry and puts
        // the counters back), N2 (the calendar look-up finds a certain postseason game).
        {
            int uHome = ProTeam(14), uAway = ProTeam(15);
            string[] uPitchers = roster.Where(r => r.IsPitcher && (r.TeamId == uHome || r.TeamId == uAway)).Select(r => r.PlayerId).ToArray();
            string Snapshot(PitcherRestLedger ledger) => string.Join(",", uPitchers.Select(id => ledger.TryGetLastStart(id, out long d) ? d.ToString() : "-"))
                + $"|dirty {ledger.DirtyCount}";
            long uDay = Abs134(year + 60, 172);
            var uMicro = new MicroGame(db, baseball) { LoggingEnabled = false };
            uMicro.Initialize();
            var fresh = new MicroGame(db, baseball) { LoggingEnabled = false };
            fresh.Initialize();
            int uHuman = uMicro.FindRosterSlot(roster.First(r => r.TeamId == uHome && !r.IsPitcher).PlayerId);
            string s0 = Snapshot(uMicro.RestLedger);
            (int ps0, int pb0) = (uMicro.PostseasonShortRestStarts, uMicro.PostseasonBullpenGames);
            ulong uSeed = pro.ScheduleSeed;
            RngState Stream() => PostseasonStreams.ForGame(uSeed, year + 60, LeagueTier.Pro, 6, uHome, uAway, 1, isAttended: true);

            // S1a: cancelled mid-play (the interactive policy's OperationCanceledException).
            bool cancelled = false;
            var cancelling = new CancellingBatterPolicy();
            RngState rngC = Stream();
            uMicro.SetGameDay(uDay);
            uMicro.SetPostseasonGame(uDay);
            try
            {
                uMicro.PlayGame(uHome, uAway, uHuman, ref cancelling, ref rngC);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            string s1 = Snapshot(uMicro.RestLedger);
            bool countersBack = uMicro.PostseasonShortRestStarts == ps0 && uMicro.PostseasonBullpenGames == pb0;

            // The replay picks exactly what a never-cancelled game picks.
            var neutralU = new NeutralBatterPolicy();
            RngState rngR = Stream();
            uMicro.SetGameDay(uDay);
            uMicro.SetPostseasonGame(uDay);
            MicroGameResult replay = uMicro.PlayGame(uHome, uAway, uHuman, ref neutralU, ref rngR);
            var replayStarters = uMicro.LastStarters;
            var neutralF = new NeutralBatterPolicy();
            RngState rngF = Stream();
            fresh.SetGameDay(uDay);
            fresh.SetPostseasonGame(uDay);
            MicroGameResult clean = fresh.PlayGame(uHome, uAway, uHuman, ref neutralF, ref rngF);
            Check("13-4 review S1: a cancelled attended postseason game leaves the rest ledger and postseason counters as it found them; the replay matches a never-cancelled game",
                cancelled && s1 == s0 && countersBack && replayStarters == fresh.LastStarters
                && replay.HomeScore == clean.HomeScore && replay.AwayScore == clean.AwayScore,
                $"cancelled {cancelled}, ledger {(s1 == s0 ? "restored" : "moved")}, starters {replayStarters} vs {fresh.LastStarters}");

            // S1b: the replay's flush rolls back (its in-batch series update throws).
            bool rolledBack = false;
            try
            {
                uMicro.FlushGame(year + 60, checked((int)uDay), (_, _) => throw new InvalidOperationException("review S1 probe"));
            }
            catch (InvalidOperationException)
            {
                rolledBack = true;
            }
            long restRows = CountRows(db, $"SELECT COUNT(*) FROM Pitcher_Rest WHERE last_start_day = {uDay};");
            Check("13-4 review S1: a rolled-back attended postseason flush puts the starts and counters back (nothing durable)",
                rolledBack && Snapshot(uMicro.RestLedger) == s0 && restRows == 0
                && uMicro.PostseasonShortRestStarts == ps0 && uMicro.PostseasonBullpenGames == pb0,
                $"rest rows on the day {restRows}");

            // N1: the macro adapter's rollback on pitchers who had never started.
            string m0 = Snapshot(pro.RestLedger);
            (int mps0, int mpb0) = (pro.PostseasonShortRestStarts, pro.PostseasonBullpenGames);
            bool neverStarted = m0.Split('|')[0].Split(',').All(x => x == "-");
            RngState rngM = PostseasonStreams.ForGame(uSeed, year + 61, LeagueTier.Pro, 2, uHome, uAway, 1);
            PostseasonPlay played = pro.SimulatePostseasonGame(uHome, uAway, Abs134(year + 61, 251), PostseasonCalendar.NoSeriesFilter, ref rngM, out _, out _);
            string m1 = Snapshot(pro.RestLedger);
            pro.ClearPostseasonGameState(revertStarts: true);
            Check("13-4 review N1: a rolled-back macro postseason game removes a never-started pitcher's entry (no phantom day-0 row) and restores the counters",
                neverStarted && played == PostseasonPlay.Played && m1 != m0 && Snapshot(pro.RestLedger) == m0
                && pro.PostseasonShortRestStarts == mps0 && pro.PostseasonBullpenGames == mpb0,
                $"never started {neverStarted}, after game {(m1 != m0 ? "moved" : "unchanged")}");
        }

        // N2: the career's calendar look-up finds the avatar's certain postseason games.
        state.SetCalendar(Abs134(year, lastDay), year);
        PostseasonGameRow firstAttended = avatarGames.OrderBy(g => g.DayOfSeason).First();
        bool found = career.TryGetScheduledGameFor(Abs134(year, firstAttended.DayOfSeason), out int n2Home, out int n2Away);
        bool offDay = career.TryGetScheduledGameFor(Abs134(year, firstAttended.DayOfSeason - 1), out _, out _)
            == avatarGames.Any(g => g.DayOfSeason == firstAttended.DayOfSeason - 1);
        Check("13-4 review N2: TryGetScheduledGameFor returns the avatar's postseason matchup once its row exists",
            found && n2Home == firstAttended.HomeTeamId && n2Away == firstAttended.AwayTeamId && offDay,
            $"day {firstAttended.DayOfSeason}: {n2Home} v {n2Away}");

        // Allocation probes: the postseason game loops (macro adapter + attended micro) allocate 0 B warm.
        int home = ProTeam(11), away = ProTeam(12);
        long probeDay = Abs134(year + 50, 250);
        // The seed is a Game_State read (DB work, once per game in production): hoisted out of the measured loops.
        ulong probeSeed = pro.ScheduleSeed;
        for (int g = 0; g < 3; g++)
        {
            RngState rng = PostseasonStreams.ForGame(probeSeed, year + 50, LeagueTier.Pro, 2, home, away, 1);
            pro.SimulatePostseasonGame(home, away, probeDay + 10 * g, PostseasonCalendar.NoSeriesFilter, ref rng, out _, out _);
            pro.ClearPostseasonGameState(revertStarts: false);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int g = 0; g < 20; g++)
        {
            RngState rng = PostseasonStreams.ForGame(probeSeed, year + 50, LeagueTier.Pro, 2, home, away, g + 1);
            pro.SimulatePostseasonGame(home, away, probeDay + 100 + 10 * g, probeDay + 90 + 10 * g, ref rng, out _, out _); // window: the previous game
            pro.ClearPostseasonGameState(revertStarts: false);
        }
        long macroAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var quiet = new MicroGame(db, baseball) { LoggingEnabled = false };
        quiet.Initialize();
        var neutral = new NeutralBatterPolicy();
        for (int g = 0; g < 3; g++)
        {
            RngState rng = PostseasonStreams.ForGame(probeSeed, year + 51, LeagueTier.Pro, 3, home, away, g + 1, isAttended: true);
            quiet.SetGameDay(probeDay + 1000 + 10 * g);
            quiet.SetPostseasonGame(probeDay + 1000);
            quiet.PlayGame(home, away, MicroGame.NoHuman, ref neutral, ref rng);
        }
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int g = 0; g < 10; g++)
        {
            RngState rng = PostseasonStreams.ForGame(probeSeed, year + 52, LeagueTier.Pro, 3, home, away, g + 1, isAttended: true);
            quiet.SetGameDay(probeDay + 2000 + 10 * g);
            quiet.SetPostseasonGame(probeDay + 1990 + 10 * g); // window: the previous game
            quiet.PlayGame(home, away, MicroGame.NoHuman, ref neutral, ref rng);
        }
        long attendedAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("postseason game loops allocate zero bytes warm (macro adapter: 20 games; attended micro: 10 games)",
            macroAllocated == 0 && attendedAllocated == 0, $"macro {macroAllocated} B, attended {attendedAllocated} B");
        career.DetachFrom(bus);
        playoffs.DetachFrom(bus);
    }
}

/// <summary>13-4 review S1 probe: the interactive policy's cancellation, at the human's first PA.</summary>
internal struct CancellingBatterPolicy : IBatterPolicy
{
    public void BeginPa(in HumanPaContext context) => throw new OperationCanceledException("review S1 probe");
    public BatterIntent NextPitch(in PitchLook look, in CountState count, ref RngState rng) => default;
    public void OnPitchResolved(in PitchResult result) { }
    public void OnPaResolved(PaOutcome outcome) { }
}
