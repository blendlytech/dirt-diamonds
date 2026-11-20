using System.Diagnostics;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// ----------------------------------------------------------------------
// Slice 13-3 (docs/tasks/13-3_schedules_calendars_reband_guard_brief.md):
// TierSeasonCalendar, the seeded SeasonScheduler + persisted Schedule_Games,
// results in the schedule (D4), pooled per-tier bands (D5), the new Pro guard
// (D6), the 13-3 development checks (D7) and the 13-2 carry-forwards.
// `--only calendar`, `--only schedule`, `--only development-tiers`;
// `--tier-pilot` and `--college-analysis rates [--pilot | --reduced]` are opt-in modes.
// ----------------------------------------------------------------------
internal static partial class Program
{
    /// <summary>First season year of the pooled replicate seasons (clear of every fixture's years).</summary>
    private const int PooledBaseYear = 3000;

    // D5 default pooled design (brief §10.2, Astra): independent flat seasons per tier.
    private const int HsPooledSeasons = 64;
    private const int CollegePooledSeasons = 16;
    private const int ProPooledSeasons = 8;

    // D6: the new Pro guard (league_restructure.md §11.3). CANDIDATE values,
    // recorded by Claude Opus on 2026-10-25 from the season-pipeline world
    // (owner's PC, 13-3 build); GPT 6 Astra Pro reproduces them 20/20 (M3) and
    // the owner sanctions them (brief §8.1/§10). A D5 outcome that changes the
    // sim (BaseOutAdvancement) would re-record them under its own sanction.
    private const long ProGuardPa = 81433;
    private const long ProGuardH = 18471;
    private const long ProGuardEr = 8803;
    private const string ProGuardLedgerSha256 = "932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84";

    /// <summary>The first Sunday (a flush day: day-of-season ≡ 0 mod 7) on or after the day.</summary>
    private static int SundayOnOrAfter(int dayOfSeason) => dayOfSeason + (7 - dayOfSeason % 7) % 7;

    /// <summary>Warm-up game days before an allocation measurement (HS has only 10 game days).</summary>
    private static int WarmGameDays(LeagueTier tier) => tier == LeagueTier.HS ? 3 : 7;

    // ------------------------------------------------------------------
    // D6: the new Pro guard
    // ------------------------------------------------------------------

    // Counting columns only (the REAL rate columns are derived), explicit
    // column order, ordered by the unique (season_year, player_id) key in
    // byte order; DumpQueries encodes with the invariant culture.
    private static readonly string[] ProGuardLedgerQueries =
    {
        "SELECT season_year, player_id, pa, ab, h, doubles, triples, hr, bb, so, rbi, sb FROM Batting_Stats " +
        "ORDER BY season_year, player_id;",
        "SELECT season_year, player_id, g, gs, w, l, sv, outs_recorded, h_allowed, er, bb, so FROM Pitching_Stats " +
        "ORDER BY season_year, player_id;",
    };

    /// <summary>
    /// The new Pro guard on the season-pipeline world (flat spread 0,
    /// LeagueSeed, SeasonSeed, the default schedule seed): the first season's
    /// PA, H and ER plus a SHA-256 of both seasons' counting ledger (13-3
    /// brief D6, §10.2 Q4). Byte-exact once recorded.
    /// </summary>
    private static void CheckProGuard(DatabaseManager db, BaseballQueries baseball)
    {
        LeagueBattingTotals bat = baseball.LoadLeagueBattingTotals(StartYear);
        LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(StartYear);
        string ledger = DumpQueries(db, ProGuardLedgerQueries);
        string sha = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ledger)));
        Console.WriteLine($"  Pro guard manifest: schema v{db.GetSchemaVersion()}, league seed {LeagueSeed}, season seed {SeasonSeed}, " +
            $"schedule seed 0x{SeasonScheduler.DefaultScheduleSeed:X}, seasons {StartYear}–{StartYear + 1}, spread 0");
        Console.WriteLine($"  Pro guard: PA {bat.Pa} / H {bat.H} / ER {pit.Er}; ledger SHA-256 {sha}");
        bool recorded = ProGuardPa != 0 && ProGuardLedgerSha256.Length == 64;
        Check("new Pro guard (§11.3, 13-3 D6): first-season PA/H/ER and the two-season ledger hash are byte-exact",
            recorded && bat.Pa == ProGuardPa && bat.H == ProGuardH && pit.Er == ProGuardEr
            && string.Equals(sha, ProGuardLedgerSha256, StringComparison.Ordinal),
            recorded
                ? $"PA {bat.Pa}/{ProGuardPa} H {bat.H}/{ProGuardH} ER {pit.Er}/{ProGuardEr} ledger {sha[..12]}/{ProGuardLedgerSha256[..12]}"
                : $"not recorded yet — candidate PA {bat.Pa} / H {bat.H} / ER {pit.Er} / ledger {sha}");
    }

    // ------------------------------------------------------------------
    // calendar: TierSeasonCalendar (§3 table, §3.2 D3 blocks)
    // ------------------------------------------------------------------

    private static void RunCalendarSuite()
    {
        Console.WriteLine("--- 13-3 TierSeasonCalendar (league_restructure §3/§3.2; fixed 365-day year, day 1 = Monday) ---");

        static bool AllOn(LeagueTier tier, params Weekday[] weekdays)
        {
            foreach (int day in TierSeasonCalendar.GameDays(tier))
            {
                if (Array.IndexOf(weekdays, GameCalendar.WeekdayForDayOfSeason(day)) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        ReadOnlySpan<int> hs = TierSeasonCalendar.GameDays(LeagueTier.HS);
        bool hsWeekly = true;
        for (int k = 1; k < hs.Length; k++)
        {
            hsWeekly &= hs[k] - hs[k - 1] == 7;
        }
        Check("HS: 10 Fridays from the first Friday of March (Fri Mar 2 = day 61) to Fri May 4 (day 124), weekly",
            hs.Length == 10 && hs[0] == 61 && hs[^1] == 124 && hsWeekly && AllOn(LeagueTier.HS, Weekday.Friday)
            && hs[0] == GameCalendar.DayOfSeasonFor(3, 2), $"{hs[0]}..{hs[^1]} ({hs.Length})");

        ReadOnlySpan<int> college = TierSeasonCalendar.GameDays(LeagueTier.College);
        bool collegeShape = college.Length == 28;
        for (int w = 0; collegeShape && w < 14; w++)
        {
            collegeShape &= GameCalendar.WeekdayForDayOfSeason(college[2 * w]) == Weekday.Wednesday
                && college[2 * w + 1] == college[2 * w] + 3;
        }
        Check("College: Wed + Sat × 14 weeks = 28, from the Wednesday before the second Saturday of February (day 38) to the 14th Saturday (Sat May 12, day 132)",
            collegeShape && college[0] == 38 && college[1] == GameCalendar.DayOfSeasonFor(2, 10)
            && college[^1] == GameCalendar.DayOfSeasonFor(5, 12), $"{college[0]}..{college[^1]} ({college.Length})");

        ReadOnlySpan<int> pro = TierSeasonCalendar.GameDays(LeagueTier.Pro);
        bool proShape = pro.Length == 64;
        for (int w = 0; proShape && w < 21; w++)
        {
            proShape &= GameCalendar.WeekdayForDayOfSeason(pro[3 * w]) == Weekday.Tuesday
                && pro[3 * w + 1] == pro[3 * w] + 2 && pro[3 * w + 2] == pro[3 * w] + 4;
        }
        Check("Pro: Tue/Thu/Sat × 21 weeks from the first Tuesday of April (day 93) + week 22's Tuesday (Aug 28, day 240) = 64",
            proShape && pro[0] == GameCalendar.DayOfSeasonFor(4, 3) && pro[^1] == GameCalendar.DayOfSeasonFor(8, 28)
            && pro[^1] == pro[62] + 3 && GameCalendar.WeekdayForDayOfSeason(pro[^1]) == Weekday.Tuesday,
            $"{pro[0]}..{pro[^1]} ({pro.Length})");

        bool noSunday = true;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            foreach (int day in TierSeasonCalendar.GameDays((LeagueTier)t))
            {
                noSunday &= GameCalendar.WeekdayForDayOfSeason(day) != Weekday.Sunday;
            }
        }
        Check("no tier ever plays on a Sunday (§3, §16-I7); the Draft is the second Saturday of July (Sat Jul 14, day 195)",
            noSunday && TierSeasonCalendar.DraftDay == GameCalendar.DayOfSeasonFor(7, 14)
            && GameCalendar.WeekdayForDayOfSeason(TierSeasonCalendar.DraftDay) == Weekday.Saturday,
            $"draft day {TierSeasonCalendar.DraftDay}");

        Check("season windows run from the Monday of week 1 to the last game: HS 57–124, College 36–132, Pro 92–240",
            TierSeasonCalendar.SeasonWindowFirstDay(LeagueTier.HS) == 57 && TierSeasonCalendar.RegularSeasonLastDay(LeagueTier.HS) == 124
            && TierSeasonCalendar.SeasonWindowFirstDay(LeagueTier.College) == 36 && TierSeasonCalendar.RegularSeasonLastDay(LeagueTier.College) == 132
            && TierSeasonCalendar.SeasonWindowFirstDay(LeagueTier.Pro) == 92 && TierSeasonCalendar.RegularSeasonLastDay(LeagueTier.Pro) == 240
            && TierSeasonCalendar.PostseasonFirstDay(LeagueTier.Pro) == 241);

        // Practice days, their separation from game days, and the D3 locked blocks.
        int[] practiceDays = new int[LeagueDirectory.TierCount];
        int[] lockedPracticeHours = new int[LeagueDirectory.TierCount];
        bool separated = true;
        bool blocksMatch = true;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tier = (LeagueTier)t;
            for (int day = 1; day <= GlobalState.DaysPerSeason; day++)
            {
                bool practice = TierSeasonCalendar.IsPracticeDay(tier, day);
                bool game = TierSeasonCalendar.IsGameDay(tier, day);
                practiceDays[t] += practice ? 1 : 0;
                separated &= !(practice && game);
                bool locked = TierSeasonCalendar.TryGetLockedTeamBlocks(tier, day, out int ph, out int gh);
                lockedPracticeHours[t] += ph;
                blocksMatch &= locked == TierSeasonCalendar.IsInSeasonWindow(tier, day)
                    && ph == (practice ? TierSeasonCalendar.PracticeHours : 0)
                    && gh == (game ? TierSeasonCalendar.GameHours : 0)
                    && (locked || (ph == 0 && gh == 0));
            }
        }
        Check("in-season practice days: HS Mon–Thu (40), College Mon/Tue/Thu/Fri (56), Pro Mon/Wed/Fri (64); never on a game day",
            practiceDays[0] == 40 && practiceDays[1] == 56 && practiceDays[2] == 64 && separated,
            $"HS {practiceDays[0]}, College {practiceDays[1]}, Pro {practiceDays[2]}");
        Check("D3 blocks: locked (practice 2 h / game 3 h, 0 on off days) inside each tier's season window, free sliders outside it; in-season team practice HS 80 h / College 112 h / Pro 128 h",
            blocksMatch && lockedPracticeHours[0] == 80 && lockedPracticeHours[1] == 112 && lockedPracticeHours[2] == 128
            && !TierSeasonCalendar.TryGetLockedTeamBlocks(LeagueTier.HS, 200, out _, out _)
            && TierSeasonCalendar.TryGetLockedTeamBlocks(LeagueTier.HS, 63, out int sundayPractice, out int sundayGame)
            && sundayPractice == 0 && sundayGame == 0,
            $"hours {lockedPracticeHours[0]}/{lockedPracticeHours[1]}/{lockedPracticeHours[2]}");

        long sink = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            var tier = (LeagueTier)(i % 3);
            int day = 1 + i % GlobalState.DaysPerSeason;
            sink += TierSeasonCalendar.IsGameDay(tier, day) ? 1 : 0;
            sink += TierSeasonCalendar.TryGetLockedTeamBlocks(tier, day, out int p, out int g) ? p + g : 0;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("calendar lookups allocate zero bytes (10k)", allocated == 0, $"{allocated} B (sink {sink})");
    }

    // ------------------------------------------------------------------
    // schedule: SeasonScheduler + Schedule_Games + D4 results
    // ------------------------------------------------------------------

    /// <summary>Per-tier structural verdicts over one generated season.</summary>
    private readonly record struct ScheduleVerdict(
        bool Counts, bool HomeAway, bool Division, bool Conference, bool CrossConference, bool InterHome,
        bool Daily, bool DaysOk, string Detail);

    private static ScheduleVerdict ValidateSchedule(LeagueTier tier, List<TeamRow> teams, List<GeneratedGame> games)
    {
        int perTeam = TierSeasonCalendar.GamesPerTeam(tier);
        var byId = teams.ToDictionary(t => t.TeamId);
        var played = new Dictionary<int, int>();
        var home = new Dictionary<int, int>();
        var interHome = new Dictionary<int, int>();
        var pairCount = new Dictionary<(int, int), int>();
        var pairHome = new Dictionary<(int, int), int>(); // home games of the lower id
        foreach (GeneratedGame g in games)
        {
            played[g.HomeTeamId] = played.GetValueOrDefault(g.HomeTeamId) + 1;
            played[g.AwayTeamId] = played.GetValueOrDefault(g.AwayTeamId) + 1;
            home[g.HomeTeamId] = home.GetValueOrDefault(g.HomeTeamId) + 1;
            (int, int) key = g.HomeTeamId < g.AwayTeamId ? (g.HomeTeamId, g.AwayTeamId) : (g.AwayTeamId, g.HomeTeamId);
            pairCount[key] = pairCount.GetValueOrDefault(key) + 1;
            pairHome[key] = pairHome.GetValueOrDefault(key) + (g.HomeTeamId == key.Item1 ? 1 : 0);
            if (byId[g.HomeTeamId].ConferenceId != byId[g.AwayTeamId].ConferenceId)
            {
                interHome[g.HomeTeamId] = interHome.GetValueOrDefault(g.HomeTeamId) + 1;
            }
        }

        bool counts = games.Count == perTeam * teams.Count / 2 && teams.All(t => played.GetValueOrDefault(t.TeamId) == perTeam);
        bool homeAway = teams.All(t => home.GetValueOrDefault(t.TeamId) == perTeam / 2);
        (int Games, int Home) divisionRule = tier switch
        {
            LeagueTier.College => (4, 2),
            LeagueTier.Pro => (8, 4),
            _ => (2, 1), // HS: a conference is one group
        };
        bool division = true;
        bool conference = true;
        bool cross = true;
        for (int a = 0; a < teams.Count; a++)
        {
            for (int b = a + 1; b < teams.Count; b++)
            {
                TeamRow ta = teams[a];
                TeamRow tb = teams[b];
                (int, int) key = ta.TeamId < tb.TeamId ? (ta.TeamId, tb.TeamId) : (tb.TeamId, ta.TeamId);
                int n = pairCount.GetValueOrDefault(key);
                int h = pairHome.GetValueOrDefault(key);
                bool sameConference = ta.ConferenceId == tb.ConferenceId;
                bool sameDivision = sameConference && (tier == LeagueTier.HS || ta.DivisionId == tb.DivisionId);
                if (sameDivision)
                {
                    division &= n == divisionRule.Games && h == divisionRule.Home;
                }
                else if (sameConference)
                {
                    conference &= n == 2 && h == 1;
                }
                else
                {
                    cross &= n == (tier == LeagueTier.Pro ? 1 : 0);
                }
            }
        }
        bool interHomeOk = tier != LeagueTier.Pro || teams.All(t => interHome.GetValueOrDefault(t.TeamId) == 8);

        bool daily = true;
        var days = new SortedSet<int>();
        foreach (IGrouping<int, GeneratedGame> day in games.GroupBy(g => g.DayOfSeason))
        {
            days.Add(day.Key);
            var seen = new HashSet<int>();
            foreach (GeneratedGame g in day)
            {
                daily &= seen.Add(g.HomeTeamId) & seen.Add(g.AwayTeamId);
            }
            daily &= seen.Count == teams.Count;
        }
        bool daysOk = days.SequenceEqual(TierSeasonCalendar.GameDays(tier).ToArray());
        return new ScheduleVerdict(counts, homeAway, division, conference, cross, interHomeOk, daily, daysOk,
            $"{games.Count} games on {days.Count} days");
    }

    private static List<GeneratedGame> GenerateFixture(LeagueTier tier, List<TeamRow> teams, ulong scheduleSeed, int year)
    {
        var games = new List<GeneratedGame>();
        RngState rng = SeasonScheduler.StreamFor(scheduleSeed, year, tier);
        SeasonScheduler.Generate(tier, teams, ref rng, games);
        return games;
    }

    private static void RunScheduleSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- 13-3 SeasonScheduler + Schedule_Games (league_restructure §3.1/§12/§16-I9; CS-M §6) ---");
        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        var teamsByTier = new List<TeamRow>[LeagueDirectory.TierCount];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            teamsByTier[t] = new List<TeamRow>();
            baseball.LoadTeamsByTier((LeagueTier)t, teamsByTier[t]);
        }

        // CS-M §6 fixtures: years 2026/2028 × scheduler seeds 1/2 (four schedules per tier).
        (ulong Seed, int Year)[] fixtures = { (1, 2026), (1, 2028), (2, 2026), (2, 2028) };
        var verdicts = new ScheduleVerdict[LeagueDirectory.TierCount][];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            verdicts[t] = fixtures.Select(f => ValidateSchedule(
                (LeagueTier)t, teamsByTier[t], GenerateFixture((LeagueTier)t, teamsByTier[t], f.Seed, f.Year))).ToArray();
        }
        string Detail(int t) => string.Join("; ", verdicts[t].Select(v => v.Detail));
        bool All(int t, Func<ScheduleVerdict, bool> rule) => verdicts[t].All(rule);

        // HS (§3.1: double round-robin in each 6-team conference)
        Check("HS schedule: 12 teams, 60 games, 10 per team, 5 home / 5 away (4 fixtures)",
            teamsByTier[0].Count == 12 && All(0, v => v.Counts && v.HomeAway), Detail(0));
        Check("HS schedule: each conference opponent twice at opposite venues; zero cross-conference games",
            All(0, v => v.Division && v.CrossConference));
        Check("HS schedule: the 10 Fridays only, 6 games each, every team exactly once per game day",
            All(0, v => v.Daily && v.DaysOk));

        // College: CS-M RunCollegeScheduleSuite checks 1–10 (§3.1/§16-I6)
        const int College = 1;
        Check("College schedule (CS-M 1): 24 teams, 336 games", teamsByTier[College].Count == 24 && All(College, v => v.Counts), Detail(College));
        Check("College schedule (CS-M 2): 28 games per team", All(College, v => v.Counts));
        Check("College schedule (CS-M 3): 14 home / 14 away", All(College, v => v.HomeAway));
        Check("College schedule (CS-M 4): division opponents × 4 with 2/2 venues", All(College, v => v.Division));
        Check("College schedule (CS-M 5): other conference teams × 2 with 1/1 venues", All(College, v => v.Conference));
        Check("College schedule (CS-M 6): zero cross-conference regular-season games", All(College, v => v.CrossConference));
        Check("College schedule (CS-M 7): 28 dates, 12 games each, no team twice on a date", All(College, v => v.Daily && v.DaysOk));
        Check("College schedule (CS-M 8): Wednesday/Saturday only, never Sunday",
            All(College, v => v.DaysOk) && TierSeasonCalendar.GameDays(LeagueTier.College).ToArray()
                .All(d => GameCalendar.WeekdayForDayOfSeason(d) is Weekday.Wednesday or Weekday.Saturday));
        bool collegeSeparated = true;
        foreach (int d in TierSeasonCalendar.GameDays(LeagueTier.College))
        {
            collegeSeparated &= !TierSeasonCalendar.IsPracticeDay(LeagueTier.College, d);
        }
        Check("College schedule (CS-M 9): February opening (Wed day 38), 14th-Saturday ending (day 132), practice separated from games",
            TierSeasonCalendar.GameDay(LeagueTier.College, 1) == 38 && TierSeasonCalendar.RegularSeasonLastDay(LeagueTier.College) == 132
            && collegeSeparated);

        // Pro (§3.1)
        Check("Pro schedule: 32 teams, 1,024 games, 64 per team, 32 home / 32 away (4 fixtures)",
            teamsByTier[2].Count == 32 && All(2, v => v.Counts && v.HomeAway), Detail(2));
        Check("Pro schedule: division opponents × 8 (4/4), other conference teams × 2 (1/1)",
            All(2, v => v.Division && v.Conference));
        Check("Pro schedule: the other conference's teams × 1, 8 interconference home games per team",
            All(2, v => v.CrossConference && v.InterHome));
        Check("Pro schedule: Tue/Thu/Sat game days (+ week 22's Tuesday), 16 games each, every team exactly once per game day",
            All(2, v => v.Daily && v.DaysOk));

        // Determinism: same tuple → identical; another year → another order, same invariants.
        bool sameSeedSame = true;
        bool otherYearDiffers = true;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tier = (LeagueTier)t;
            List<GeneratedGame> a = GenerateFixture(tier, teamsByTier[t], 1, 2026);
            List<GeneratedGame> b = GenerateFixture(tier, teamsByTier[t], 1, 2026);
            List<GeneratedGame> c = GenerateFixture(tier, teamsByTier[t], 1, 2028);
            sameSeedSame &= a.SequenceEqual(b);
            otherYearDiffers &= !a.SequenceEqual(c);
        }
        Check("scheduler determinism: the same (seed, year, tier) gives the identical schedule; another year another order",
            sameSeedSame && otherYearDiffers);

        // Persistence (D1): ensure writes current + next once; rows = the generator's order; never regenerated.
        var schedule = new ScheduleQueries(db);
        var gameState = new GameStateQueries(db);
        bool persisted = true;
        bool reloadIdentical = true;
        var rows = new List<ScheduledGameRow>();
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tier = (LeagueTier)t;
            int expected = TierSeasonCalendar.GamesPerTeam(tier) * teamsByTier[t].Count / 2;
            SeasonScheduler.EnsureSeasons(db, baseball, schedule, gameState, tier, StartYear);
            persisted &= schedule.CountRegularSeasonGames(StartYear, tier) == expected
                && schedule.CountRegularSeasonGames(StartYear + 1, tier) == expected
                && schedule.CountRegularSeasonGames(StartYear + 2, tier) == 0;
            schedule.LoadRegularSeason(StartYear, tier, rows);
            List<GeneratedGame> generated = GenerateFixture(tier, teamsByTier[t], SeasonScheduler.DefaultScheduleSeed, StartYear);
            reloadIdentical &= rows.Count == generated.Count && rows.Select(r => (r.DayOfSeason, r.HomeTeamId, r.AwayTeamId, r.Status))
                .SequenceEqual(generated.Select(g => (g.DayOfSeason, g.HomeTeamId, g.AwayTeamId, 0)));
        }
        string idsBefore = DumpQueries(db, new[] { "SELECT game_id, season_year, tier, day_of_season, home_team_id, away_team_id FROM Schedule_Games ORDER BY game_id;" });
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            SeasonScheduler.EnsureSeasons(db, baseball, schedule, gameState, (LeagueTier)t, StartYear);
        }
        string idsAfter = DumpQueries(db, new[] { "SELECT game_id, season_year, tier, day_of_season, home_team_id, away_team_id FROM Schedule_Games ORDER BY game_id;" });
        Check("D1 ensure: every tier's current and next season persisted (60 / 336 / 1,024 rows each), nothing beyond",
            persisted);
        Check("D1 reload identity: persisted rows are the generator's schedule in play order, and a second ensure rewrites nothing (same ids)",
            reloadIdentical && idsBefore == idsAfter, $"ids {Sha12(idsBefore)}");

        // A stored schedule_seed changes the schedule; the default applies when none is stored.
        List<GeneratedGame> seeded = GenerateFixture(LeagueTier.Pro, teamsByTier[2], 0x5EEDUL, StartYear);
        List<GeneratedGame> defaulted = GenerateFixture(LeagueTier.Pro, teamsByTier[2], SeasonScheduler.DefaultScheduleSeed, StartYear);
        ulong resolvedDefault = SeasonScheduler.ResolveSeed(gameState);
        db.RunInBatch(() => gameState.SetInt64(SeasonScheduler.ScheduleSeedKey, 0x5EED));
        ulong resolvedStored = SeasonScheduler.ResolveSeed(gameState);
        Check("D1 seed: a world without schedule_seed uses the default; a stored seed is used and gives a different schedule",
            resolvedDefault == SeasonScheduler.DefaultScheduleSeed && resolvedStored == 0x5EED && !seeded.SequenceEqual(defaulted));

        // The sim's in-memory table: current + next season, lookups by team.
        var normalizer = new StatsNormalizer(db, baseball);
        var pro = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed + 133), LeagueTier.Pro);
        pro.Initialize();
        pro.PrepareSeason(StartYear);
        int teamId = teamsByTier[2][5].TeamId;
        int opening = TierSeasonCalendar.GameDay(LeagueTier.Pro, 1);
        bool found = pro.TryGetScheduledGame(StartYear, opening, teamId, out long gameId, out int homeId, out int awayId);
        bool nextFound = pro.TryGetScheduledGame(StartYear + 1, opening, teamId, out _, out _, out _);
        bool offDay = pro.TryGetScheduledGame(StartYear, opening + 1, teamId, out _, out _, out _);
        bool otherYear = pro.TryGetScheduledGame(StartYear + 5, opening, teamId, out _, out _, out _);
        schedule.LoadRegularSeason(StartYear, LeagueTier.Pro, rows);
        ScheduledGameRow row = rows.First(r => r.GameId == gameId);
        Check("the sim's schedule table: a team's opening-day game matches its row; next season loaded; off days and unloaded seasons have none",
            pro.CurrentSeason.Count == 1024 && found && (homeId == teamId || awayId == teamId)
            && row.DayOfSeason == opening && row.HomeTeamId == homeId && row.AwayTeamId == awayId
            && nextFound && !offDay && !otherYear);
        long sink = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            sink += pro.TryGetScheduledGame(StartYear, TierSeasonCalendar.GameDay(LeagueTier.Pro, 1 + i % 64), teamId, out long g, out _, out _) ? g : 0;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("schedule lookups allocate zero bytes (10k)", allocated == 0, $"{allocated} B (sink {sink})");

        RunScheduleResultChecks(db, baseball, normalizer, teamsByTier[2]);
    }

    /// <summary>
    /// D4: played games' finals land in Schedule_Games inside the sims' own
    /// flush batches — macro on the Sunday flush, attended in the micro's —
    /// survive a commit-time rollback and land exactly once on the retry.
    /// </summary>
    private static void RunScheduleResultChecks(
        DatabaseManager db, BaseballQueries baseball, StatsNormalizer normalizer, List<TeamRow> proTeams)
    {
        var schedule = new ScheduleQueries(db);
        const int year = StartYear;
        var sim = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed + 134), LeagueTier.Pro);
        sim.Initialize();
        int attendedTeam = proTeams[0].TeamId;
        sim.SetAttendedTeam(attendedTeam);

        long FinalCount() => Convert.ToInt64(db.ExecuteScalar(db.GetPooledCommand(
            "SELECT COUNT(*) FROM Schedule_Games WHERE season_year = 2026 AND tier = 2 AND status = 1;")));
        long FinalRuns() => Convert.ToInt64(db.ExecuteScalar(db.GetPooledCommand(
            "SELECT COALESCE(SUM(home_score + away_score), 0) FROM Schedule_Games WHERE season_year = 2026 AND tier = 2 AND status = 1;")));
        void Play(int fromDay, int toDay, List<string>? errors = null)
        {
            for (int day = fromDay; day <= toDay; day++)
            {
                try
                {
                    sim.ProcessDay(new DayAdvancedEvent(day, year, day));
                }
                catch (Exception ex) when (errors is not null)
                {
                    errors.Add(ex.Message);
                }
            }
        }

        // Week 1 (Tue/Thu/Sat 93/95/97), flushed Sunday 98: 3 × 15 macro games (the attended team's are skipped).
        Play(1, 98);
        LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(year, LeagueTier.Pro);
        long finals1 = FinalCount();
        Check("D4 macro results: week 1's played games are final after the Sunday flush (45 = 3 days × 15; the attended team's 3 stay scheduled)",
            finals1 == 45 && pit.W == 45 && pit.L == 45 && FinalRuns() == pit.Er,
            $"finals {finals1}, W {pit.W}, runs in rows {FinalRuns()} vs ER {pit.Er}");

        // Week 2 with a commit-time fault on the Sunday flush (day 105): rolled
        // back, the results stay buffered with the stats; week 3's Sunday (112) lands both once.
        Play(99, 104);
        SetRestCommitFault(db, on: true);
        var errors = new List<string>();
        Play(105, 105, errors);
        SetRestCommitFault(db, on: false);
        long finalsAfterFault = FinalCount();
        Play(106, 112);
        long finalsRetry = FinalCount();
        LeaguePitchingTotals pitRetry = baseball.LoadLeaguePitchingTotals(year, LeagueTier.Pro);
        long duplicates = Convert.ToInt64(db.ExecuteScalar(db.GetPooledCommand(
            "SELECT COUNT(*) FROM Schedule_Games WHERE season_year = 2026 AND tier = 2 AND status = 1 AND (home_score IS NULL OR away_score IS NULL OR home_score = away_score);")));
        Check("D4 rollback: a failed flush commits no result; the retry lands weeks 2 and 3 exactly once (135 finals, runs = ER, no tie or null score)",
            errors.Count == 1 && finalsAfterFault == 45 && finalsRetry == 135 && pitRetry.W == 135
            && FinalRuns() == pitRetry.Er && duplicates == 0,
            $"errors {errors.Count}, finals {finals1}→{finalsAfterFault}→{finalsRetry}, W {pitRetry.W}");

        // The attended game: MicroGame writes its row's final in its own flush.
        sim.TryGetScheduledGame(year, 93, attendedTeam, out long attendedGameId, out int home, out int away);
        var micro = new MicroGame(db, baseball) { LoggingEnabled = false };
        micro.Initialize();
        micro.SetGameDay(93);
        micro.SetScheduledGame(attendedGameId);
        var policy = new NeutralBatterPolicy();
        var rng = new RngState(MicroSeed + 133);
        MicroGameResult result = micro.PlayGame(home, away, MicroGame.NoHuman, ref policy, ref rng);
        micro.FlushGame(year, 93);
        var rows = new List<ScheduledGameRow>();
        schedule.LoadRegularSeason(year, LeagueTier.Pro, rows);
        string scores = DumpQueries(db, new[] { $"SELECT status, home_score, away_score FROM Schedule_Games WHERE game_id = {attendedGameId};" });
        // A micro game with no schedule row set writes no result.
        sim.TryGetScheduledGame(year, 95, attendedTeam, out long unsetGameId, out int home2, out int away2);
        micro.SetGameDay(95);
        micro.PlayGame(home2, away2, MicroGame.NoHuman, ref policy, ref rng);
        micro.FlushGame(year, 95);
        string unset = DumpQueries(db, new[] { $"SELECT status FROM Schedule_Games WHERE game_id = {unsetGameId};" });
        Check("D4 attended result: the micro flush marks its schedule row final with the game's score; a game with no row set writes none",
            scores.Contains($"1|{result.HomeScore}|{result.AwayScore}|", StringComparison.Ordinal)
            && unset.Contains("\n0|", StringComparison.Ordinal),
            $"{scores.Split('\n')[1]} vs {result.HomeScore}-{result.AwayScore}");
        Check("schedule world: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
    }

    // ------------------------------------------------------------------
    // D5: pooled per-tier bands (tier-ladder) and CS-M's College rate smoke
    // ------------------------------------------------------------------

    /// <summary>Season totals for one tier-season (pooled by summing, never by averaging ratios).</summary>
    private readonly record struct SeasonTotals(
        long Pa, long Ab, long H, long Tb, long Bb, long So, long Hr, long Er, long Outs, long Gs);

    private static SeasonTotals LoadSeasonTotals(BaseballQueries baseball, int year, LeagueTier tier)
    {
        LeagueBattingTotals bat = baseball.LoadLeagueBattingTotals(year, tier);
        LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(year, tier);
        long singles = bat.H - bat.Doubles - bat.Triples - bat.Hr;
        return new SeasonTotals(bat.Pa, bat.Ab, bat.H, singles + 2 * bat.Doubles + 3 * bat.Triples + 4 * bat.Hr,
            bat.Bb, bat.So, bat.Hr, pit.Er, pit.OutsRecorded, pit.Gs);
    }

    /// <summary>
    /// A pooled ratio Σx/Σd with its seed-season cluster standard error and a
    /// 95% t interval on k − 1 degrees of freedom (brief §10.2): residuals
    /// x_i − r·d_i, sample SD / (√k · mean d).
    /// </summary>
    private static (double Estimate, double Se, double Lo, double Hi) PooledRatio(
        IReadOnlyList<SeasonTotals> seasons, Func<SeasonTotals, double> x, Func<SeasonTotals, double> d, double scale = 1.0)
    {
        double sx = seasons.Sum(x);
        double sd = seasons.Sum(d);
        double r = sx / sd;
        int k = seasons.Count;
        double se = 0;
        if (k > 1)
        {
            double meanD = sd / k;
            double sumSq = seasons.Sum(s => Math.Pow(x(s) - r * d(s), 2));
            se = Math.Sqrt(sumSq / (k - 1)) / (Math.Sqrt(k) * meanD);
        }
        double t = TCritical95(Math.Max(1, k - 1));
        return (scale * r, scale * se, scale * (r - t * se), scale * (r + t * se));
    }

    // Two-sided 95% t critical value: Cornish–Fisher expansion around z (accurate to ~0.01 for df ≥ 5).
    private static double TCritical95(int df)
    {
        const double z = 1.959964;
        double z3 = z * z * z;
        double z5 = z3 * z * z;
        return z + (z3 + z) / (4.0 * df) + (5 * z5 + 16 * z3 + 3 * z) / (96.0 * df * df);
    }

    /// <summary>
    /// Plays <paramref name="seasons"/> independent replicate seasons of one
    /// tier on the shared flat world: a fresh sim per replicate (its own
    /// outcome stream and rest ledger), season year PooledBaseYear + r (its
    /// own schedule stream), every day through the sim's real day handler.
    /// Returns per-season totals and wall-clock milliseconds.
    /// </summary>
    private static (List<SeasonTotals> Seasons, double Ms) PlayReplicateSeasons(
        DatabaseManager db, BaseballQueries baseball, StatsNormalizer normalizer, LeagueTier tier, int seasons)
    {
        var totals = new List<SeasonTotals>(seasons);
        var stopwatch = Stopwatch.StartNew();
        for (int r = 0; r < seasons; r++)
        {
            int year = PooledBaseYear + r;
            ulong outcomeSeed = 0x1333_0000_0000UL ^ ((ulong)tier << 16) ^ (ulong)r;
            var sim = new LeagueSimulator(db, baseball, normalizer, new RngState(outcomeSeed), tier);
            sim.Initialize();
            long dayBase = (long)r * GlobalState.DaysPerSeason;
            for (int day = 1; day <= GlobalState.DaysPerSeason; day++)
            {
                sim.ProcessDay(new DayAdvancedEvent(dayBase + day, year, day));
            }
            sim.FlushPending();
            totals.Add(LoadSeasonTotals(baseball, year, tier));
        }
        stopwatch.Stop();
        return (totals, stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// D5: every tier's band gated on its pooled point estimate (R/9 gated,
    /// ER/GS reported on the same sample), with 95% cluster intervals and
    /// the planning SE targets reported; strict HS > College > Pro on pooled
    /// AVG and R/9; CS-M's College rate smoke on the first four College
    /// replicates. <paramref name="pilot"/> runs the timing pilot instead.
    /// </summary>
    private static void RunPooledTierBands(string schemaPath, string scratchPath, bool pilot)
    {
        (int hs, int college, int pro) = pilot ? (4, 4, 2) : (HsPooledSeasons, CollegePooledSeasons, ProPooledSeasons);
        Console.WriteLine($"--- 13-3 pooled tier bands (D5): HS {hs} / College {college} / Pro {pro} flat seasons{(pilot ? " — TIMING PILOT" : "")} ---");
        var setup = Stopwatch.StartNew();
        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        var normalizer = new StatsNormalizer(db, baseball);
        setup.Stop();

        int[] counts = { hs, college, pro };
        var pooled = new List<SeasonTotals>[LeagueDirectory.TierCount];
        var ms = new double[LeagueDirectory.TierCount];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            (pooled[t], ms[t]) = PlayReplicateSeasons(db, baseball, normalizer, (LeagueTier)t, counts[t]);
        }

        if (pilot)
        {
            double perHs = ms[0] / hs, perCollege = ms[1] / college, perPro = ms[2] / pro;
            double predicted = HsPooledSeasons * perHs + CollegePooledSeasons * perCollege + ProPooledSeasons * perPro;
            Console.WriteLine($"  pilot: world setup {setup.ElapsedMilliseconds} ms; per season HS {perHs:F0} ms, College {perCollege:F0} ms, Pro {perPro:F0} ms");
            Console.WriteLine($"  predicted default addition: 64×HS + 16×College + 8×Pro = {predicted / 1000:F1} s (+ {setup.ElapsedMilliseconds / 1000.0:F1} s setup); " +
                $"College share {CollegePooledSeasons * perCollege / 1000:F1} s");
            Check("tier pilot ran every tier", pooled.All(p => p.Count > 0));
            return;
        }

        var avg = new double[LeagueDirectory.TierCount];
        var r9 = new double[LeagueDirectory.TierCount];
        double[] r9SeTarget = { 0.06, 0.04, 0.03 };
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var tier = (LeagueTier)t;
            List<SeasonTotals> s = pooled[t];
            var a = PooledRatio(s, x => x.H, x => x.Ab);
            var o = PooledRatio(s, x => x.H + x.Bb, x => x.Pa);
            var g = PooledRatio(s, x => x.Tb, x => x.Ab);
            var k = PooledRatio(s, x => x.So, x => x.Pa);
            var b = PooledRatio(s, x => x.Bb, x => x.Pa);
            var h = PooledRatio(s, x => x.Hr, x => x.Pa);
            var run9 = PooledRatio(s, x => x.Er, x => x.Outs, 27.0);
            var runGs = PooledRatio(s, x => x.Er, x => x.Gs);
            avg[t] = a.Estimate;
            r9[t] = run9.Estimate;
            long games = s.Sum(x => x.Gs) / 2;
            Console.WriteLine($"  {tier,-8} {s.Count} seasons, {games:N0} games: {a.Estimate:.000}/{o.Estimate:.000}/{g.Estimate:.000}  " +
                $"K% {k.Estimate:P1}  BB% {b.Estimate:P1}  HR/PA {h.Estimate:P2}  R/9 {run9.Estimate:F3} [{run9.Lo:F3}, {run9.Hi:F3}] (SE {run9.Se:F4}, target ≤ {r9SeTarget[t]:F2})  " +
                $"ER/GS {runGs.Estimate:F3} [{runGs.Lo:F3}, {runGs.Hi:F3}]  (R = ER {s.Sum(x => x.Er):N0}, outs {s.Sum(x => x.Outs):N0}, GS {s.Sum(x => x.Gs):N0}; " +
                $"rate SEs AVG {a.Se:F4} OBP {o.Se:F4} SLG {g.Se:F4} K {k.Se:F4} BB {b.Se:F4} HR {h.Se:F4})");

            TierBand band = TierBands[t];
            Check($"pooled [{tier}] slash line inside the tier band (point estimate; {s.Count} seasons)",
                a.Estimate >= band.AvgLo && a.Estimate <= band.AvgHi && o.Estimate >= band.ObpLo && o.Estimate <= band.ObpHi
                && g.Estimate >= band.SlgLo && g.Estimate <= band.SlgHi,
                $"{a.Estimate:.000} [{a.Lo:.000},{a.Hi:.000}] / {o.Estimate:.000} [{o.Lo:.000},{o.Hi:.000}] / {g.Estimate:.000} [{g.Lo:.000},{g.Hi:.000}]");
            Check($"pooled [{tier}] K/BB/HR rates and R/9 inside the tier band (point estimate; ER/GS reported)",
                k.Estimate >= band.KLo && k.Estimate <= band.KHi && b.Estimate >= band.BbLo && b.Estimate <= band.BbHi
                && h.Estimate >= band.HrLo && h.Estimate <= band.HrHi
                && run9.Estimate >= band.RgLo && run9.Estimate <= band.RgHi,
                $"K {k.Estimate:P1} BB {b.Estimate:P1} HR {h.Estimate:P2} R/9 {run9.Estimate:F3} [{run9.Lo:F3},{run9.Hi:F3}] " +
                $"(band {band.RgLo:F1}–{band.RgHi:F1}{(run9.Lo < band.RgLo || run9.Hi > band.RgHi ? "; interval crosses a boundary: inconclusive calibration, reported" : "")}) ER/GS {runGs.Estimate:F3}");
        }
        Check("pooled tier ladder strictly monotone HS > College > Pro on AVG and R/9 (legibility contract)",
            avg[0] > avg[1] && avg[1] > avg[2] && r9[0] > r9[1] && r9[1] > r9[2],
            $"AVG {avg[0]:.000} > {avg[1]:.000} > {avg[2]:.000}; R/9 {r9[0]:F2} > {r9[1]:F2} > {r9[2]:F2}");

        // CS-M RunCollegeRateSmoke (2 checks) on the first four College replicates.
        List<SeasonTotals> smoke = pooled[1].Take(4).ToList();
        int collegeGames = TierSeasonCalendar.GamesPerTeam(LeagueTier.College) * 24 / 2;
        Check("college-rate-smoke 1 (CS-M): four seasons with positive, reconciled exposure (GS = 2 × 336, AB = PA − BB, outs ≤ PA − H − BB)",
            smoke.Count == 4 && smoke.All(x => x.Gs == 2 * collegeGames && x.Pa > 0 && x.Ab == x.Pa - x.Bb && x.Outs > 0
                && x.Outs <= x.Pa - x.H - x.Bb));
        bool finite = smoke.All(x =>
        {
            double avgS = (double)x.H / x.Ab, obp = (double)(x.H + x.Bb) / x.Pa, slg = (double)x.Tb / x.Ab;
            double kS = (double)x.So / x.Pa, bbS = (double)x.Bb / x.Pa, hrS = (double)x.Hr / x.Pa, run = 27.0 * x.Er / x.Outs;
            return new[] { avgS, obp, kS, bbS, hrS }.All(v => double.IsFinite(v) && v >= 0 && v <= 1)
                && double.IsFinite(slg) && slg >= 0 && slg <= 4 && double.IsFinite(run) && run >= 0;
        });
        Check("college-rate-smoke 2 (CS-M): all seven metrics finite and in domain (rates in [0,1], SLG in [0,4], R/9 ≥ 0)", finite);
        Check("pooled world: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
    }

    // ------------------------------------------------------------------
    // 13-2 carry-forwards: re-rank coverage after a person refresh
    // ------------------------------------------------------------------

    /// <summary>
    /// One refresh scenario on Pro team 301 vs 302 (day 10, RNG 777, fresh
    /// rest ledgers): set the starters' (or, with every starter absent, the
    /// relievers') stuff/control and stamina, initialize a RUNNING micro,
    /// commit + announce one pitcher's levers, then compare the running
    /// micro's starting pitcher and full stat attribution with a FRESH
    /// micro initialized over the same committed rows.
    /// </summary>
    private static void RunRefreshScenario(
        string schemaPath, string dbPath, string name, bool relievers, int[] quality, int[] stamina,
        int moved, byte happiness, byte confidence, int expectedBefore, int expectedAfter)
    {
        using var db = new DatabaseManager(dbPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var persons = new PersonQueries(db);
        var genRng = new RngState(123);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: 0, ref genRng);
        int teamId = ProTeam(1);
        int opponentId = ProTeam(2);
        var roster = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.Pro, roster);
        PitcherRole role = relievers ? PitcherRole.Reliever : PitcherRole.Starter;
        string[] staff = roster.Where(r => r.TeamId == teamId && r.IsPitcher && r.Role == role)
            .Select(r => r.PlayerId).OrderBy(id => id, StringComparer.Ordinal).Take(quality.Length).ToArray();
        if (staff.Length != quality.Length)
        {
            Check($"{name}: team 301 has the {quality.Length} pitchers the scenario needs", false, $"{staff.Length}");
            return;
        }
        for (int k = 0; k < staff.Length; k++)
        {
            baseball.TryGetRatings(staff[k], out PlayerRatingsRow ratings);
            ratings.PitStuff = quality[k];
            ratings.PitControl = quality[k];
            ratings.PitStamina = stamina[k];
            baseball.UpsertRatings(in ratings);
            persons.Upsert(PersonRow.Neutral(staff[k]));
        }
        // A bullpen game: every starter on both teams absent on day 10.
        var absences = new AvailabilityLedger();
        if (relievers)
        {
            absences.Seed(roster.Where(r => (r.TeamId == teamId || r.TeamId == opponentId) && r.IsPitcher && r.Role == PitcherRole.Starter)
                .Select(r => new PlayerAbsenceRow { PlayerId = r.PlayerId, Reason = AbsenceReason.Injury, UntilDay = 20 })
                .ToList());
        }

        MicroGame NewMicro(PersonLedger levers) => new(db, baseball)
        {
            LoggingEnabled = false, Persons = persons, PersonLevers = levers, RestLedger = new PitcherRestLedger(),
            Availability = absences,
        };
        string PlayDay10(MicroGame micro)
        {
            micro.RefreshAvailability(10);
            micro.SetGameDay(10);
            var policy = new NeutralBatterPolicy();
            var rng = new RngState(777);
            micro.PlayGame(teamId, opponentId, MicroGame.NoHuman, ref policy, ref rng);
            return micro.LastStarters.Home;
        }

        var bus = new EventBus();
        var levers = new PersonLedger();
        levers.AttachTo(bus);
        MicroGame running = NewMicro(levers);
        running.Initialize();
        MicroGame control = NewMicro(new PersonLedger());
        control.Initialize();
        string before = PlayDay10(control);

        PersonRow changed = PersonRow.Neutral(staff[moved]);
        changed.Happiness = happiness;
        changed.Confidence = confidence;
        persons.Upsert(in changed);
        bus.Publish(new PersonLeversChangedEvent(staff[moved], happiness, confidence, 50));
        bus.DispatchPending();

        string runningStarter = PlayDay10(running);
        MicroGame fresh = NewMicro(new PersonLedger());
        fresh.Initialize();
        string freshStarter = PlayDay10(fresh);

        running.FlushGame(StartYear, 10);
        string runningRows = DumpQueries(db, AttributionQueries);
        db.RunInBatch(() =>
        {
            db.ExecuteNonQuery(db.GetPooledCommand("DELETE FROM Pitching_Stats;"));
            db.ExecuteNonQuery(db.GetPooledCommand("DELETE FROM Batting_Stats;"));
            db.ExecuteNonQuery(db.GetPooledCommand("DELETE FROM Pitcher_Rest;"));
        });
        fresh.FlushGame(StartYear, 10);
        string freshRows = DumpQueries(db, AttributionQueries);
        Check(name,
            before == staff[expectedBefore] && runningStarter == staff[expectedAfter] && freshStarter == staff[expectedAfter]
            && runningRows == freshRows,
            $"before #{Array.IndexOf(staff, before)}, running #{Array.IndexOf(staff, runningStarter)}, fresh #{Array.IndexOf(staff, freshStarter)}; " +
            $"attribution {Sha12(runningRows)}/{Sha12(freshRows)}");
    }

    /// <summary>13-2 brief §9.8 follow-up (§9.7 observation 2): reliever re-rank, a rating increase, a stamina tie-break.</summary>
    private static void RunRefreshRankCarryForwardChecks(string schemaPath, string scratchPath)
    {
        // Relievers 51/50/40/40/40: the top reliever's levers drop to 0 (49/49), so the 50/50 #2 opens the bullpen game.
        RunRefreshScenario(schemaPath, scratchPath + ".rp",
            "13-3 carry-forward: a reliever re-rank after a person refresh — the running micro's bullpen game opens with the same reliever as a fresh Initialize, attribution identical",
            relievers: true, new[] { 51, 50, 40, 40, 40 }, new[] { 50, 50, 50, 50, 50 },
            moved: 0, happiness: 0, confidence: 0, expectedBefore: 0, expectedAfter: 1);
        // Starters 50/49/40/40: #2's levers rise to 100 (51/51), above the ace.
        RunRefreshScenario(schemaPath, scratchPath + ".up",
            "13-3 carry-forward: a rating INCREASE promotes a lower-ranked starter — running and fresh both start the promoted #2, attribution identical",
            relievers: false, new[] { 50, 49, 40, 40 }, new[] { 50, 50, 50, 50 },
            moved: 1, happiness: 100, confidence: 100, expectedBefore: 0, expectedAfter: 1);
        // Starters 51/49/40/40 with #2's stamina 70: #2 rises to 51/51, tying the ace; stamina descending breaks the tie.
        RunRefreshScenario(schemaPath, scratchPath + ".tie",
            "13-3 carry-forward: a stamina tie-break after a refresh — #2 ties the ace at 51/51 and its higher stamina wins in running and fresh, attribution identical",
            relievers: false, new[] { 51, 49, 40, 40 }, new[] { 50, 70, 50, 50 },
            moved: 1, happiness: 100, confidence: 100, expectedBefore: 0, expectedAfter: 1);
    }

    // ------------------------------------------------------------------
    // D7: the 13-3 development checks
    // ------------------------------------------------------------------

    private static void RunDevelopmentTiersSuite(string schemaPath, string scratchPath)
    {
        Console.WriteLine("--- 13-3 development (D7): calendar practice per tier, single-pass talent gradient ---");

        // Calendar practice: the locked in-season hours each tier banks, through the conversion.
        var hours = new long[LeagueDirectory.TierCount];
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            for (int day = 1; day <= GlobalState.DaysPerSeason; day++)
            {
                if (TierSeasonCalendar.TryGetLockedTeamBlocks((LeagueTier)t, day, out int ph, out _))
                {
                    hours[t] += ph;
                }
            }
        }
        double[] frac = hours.Select(h => DevelopmentCurve.PracticeFraction(h)).ToArray();
        Console.WriteLine($"  in-season team practice: HS {hours[0]} h → {frac[0]:F6}, College {hours[1]} h → {frac[1]:F6}, Pro {hours[2]} h → {frac[2]:F6} " +
            $"(PracticeFracPerHour {DevelopmentProfile.PracticeFracPerHour}, cap {DevelopmentProfile.PracticeFracCap})");
        Check("calendar practice credit per tier: 80 / 112 / 128 in-season hours, each converting below the seasonal cap, ordered HS < College < Pro",
            hours[0] == 80 && hours[1] == 112 && hours[2] == 128
            && frac.All(f => f > 0 && f < DevelopmentProfile.PracticeFracCap) && frac[0] < frac[1] && frac[1] < frac[2]);

        // Single-pass gradient (brief §10.3 Q6): spread-25 world, one age tick + one development pass, no promotions.
        using var db = new DatabaseManager(scratchPath);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var genRng = new RngState(LeagueSeed + 133);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);
        var normalizer = new StatsNormalizer(db, baseball);
        var leagues = new LeagueDirectory();
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var sim = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed + (ulong)t), (LeagueTier)t);
            sim.Initialize();
            leagues.Register(sim);
        }
        var micro = new MicroGame(db, baseball) { LoggingEnabled = false };
        micro.Initialize();

        (long Sum, long Count)[] Means()
        {
            var means = new (long, long)[LeagueDirectory.TierCount];
            var rows = new List<RosterPlayerRow>();
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                baseball.LoadRosterByTier((LeagueTier)t, rows);
                foreach (RosterPlayerRow row in rows.Where(r => !r.IsPitcher))
                {
                    means[t].Item1 += row.BatPower + row.BatContact + row.BatDiscipline;
                    means[t].Item2++;
                }
            }
            return means;
        }
        // Strict ordering by integer cross-products: a/b < c/d ⟺ a·d < c·b.
        static bool Below((long Sum, long Count) a, (long Sum, long Count) b) => a.Sum * b.Count < b.Sum * a.Count;
        static string Show((long Sum, long Count)[] m) =>
            string.Join(" < ", m.Select(x => (x.Sum / (double)x.Count).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

        (long Sum, long Count)[] pre = Means();
        players.AgeAllPlayers();
        var development = new DevelopmentManager(db, players, baseball, new GameStateQueries(db), leagues, micro,
            new RngState(LeagueSeed + 134));
        development.RunDevelopment(StartYear);
        (long Sum, long Count)[] post = Means();
        Console.WriteLine($"  mean raw batter (power + contact + discipline, 0–300), position players: pre {Show(pre)}; post {Show(post)}");
        Check("single-pass gradient: mean raw batter talent strictly HS < College < Pro on the generated spread-25 world, before and after one age tick + development pass (no tolerance)",
            Below(pre[0], pre[1]) && Below(pre[1], pre[2]) && Below(post[0], post[1]) && Below(post[1], post[2]),
            $"pre {Show(pre)}; post {Show(post)}; pass changed {development.LastRun.PlayersChanged} players");
    }

    // ------------------------------------------------------------------
    // Opt-in: CS-M's College rate analysis (M2) — never part of the default run
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>--college-analysis rates [--pilot | --reduced]</c>: flat College
    /// seasons over (world, scheduler, outcome) tuples, a fresh scratch world
    /// per world seed, every tuple's season appended to that world under its
    /// own year. The pilot is CS-M/brief §10.2 Q3's 32-season training set
    /// (worlds 1–8 × scheduler 1/2 × outcome 1/2) with a 60 s stop rule. The
    /// full matrix is validation worlds 129–256 × scheduler 1–8 × outcome 1–16
    /// (forecast 175 min, over the 30 min stop rule). <c>--reduced</c> is the
    /// approved M2 holdout (Math record §3.3, reviewed in brief §10.7): the
    /// pre-registered sub-lattice worlds 129–160 × scheduler 1–4 × outcome 1–4
    /// = 512 flat seasons, gated, then 128 spread-25 diagnostic seasons
    /// (worlds 129–160 × scheduler 1/2 × outcome 1/2), reported only, with a
    /// 20 min hard stop between tuples (a stopped run is incomplete, exit 2).
    /// Pooled rates with world-clustered and tuple-clustered 95% intervals;
    /// prints a per-tuple manifest. Exit 0 only when every gated metric's
    /// point estimate is inside the College band.
    /// </summary>
    private static int RunCollegeAnalysis(string schemaPath, bool pilot, bool reduced)
    {
        var batches = pilot
            ? new[] { new CollegeBatch("PILOT", 1, 8, 2, 2, Spread: 0, Gated: true) }
            : reduced
                ? new[]
                {
                    new CollegeBatch("REDUCED HOLDOUT (flat, gated)", 129, 32, 4, 4, Spread: 0, Gated: true),
                    new CollegeBatch("SPREAD-25 DIAGNOSTIC (reported, not gated)", 129, 32, 2, 2,
                        Spread: LeagueGenerator.DefaultRatingSpread, Gated: false),
                }
                : new[] { new CollegeBatch("FULL", 129, 128, 8, 16, Spread: 0, Gated: true) };
        double stopSeconds = pilot ? 60 : reduced ? 20 * 60 : double.PositiveInfinity;
        var total = Stopwatch.StartNew();
        bool pass = true;
        foreach (CollegeBatch batch in batches)
        {
            (bool stopped, bool inBand) = RunCollegeBatch(schemaPath, batch, total, stopSeconds, forecastFullMatrix: pilot);
            pass &= !batch.Gated || inBand;
            if (stopped && !pilot)
            {
                // The pilot's 60 s rule judges what it played (CS-M); the holdout's 20 min kill leaves no verdict.
                Console.WriteLine($"  INCOMPLETE: the {stopSeconds / 60:F0} min stop rule ended the run at {total.Elapsed.TotalMinutes:F1} min; no verdict.");
                return 2;
            }
            if (stopped)
            {
                break;
            }
        }
        Console.WriteLine($"  college analysis total {total.Elapsed.TotalMinutes:F2} min; gated verdict {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    private readonly record struct CollegeBatch(
        string Label, int FirstWorld, int Worlds, int Schedulers, int Outcomes, int Spread, bool Gated);

    /// <summary>One batch of <see cref="RunCollegeAnalysis"/>: whether the time rule stopped it, and whether every point estimate is in band.</summary>
    private static (bool Stopped, bool InBand) RunCollegeBatch(
        string schemaPath, CollegeBatch batch, Stopwatch total, double stopSeconds, bool forecastFullMatrix)
    {
        (string label, int firstWorld, int worlds, int schedulers, int outcomes, int spread, bool gated) = batch;
        Console.WriteLine($"--- CS-M College rate analysis ({label}): worlds {firstWorld}–{firstWorld + worlds - 1} × " +
            $"scheduler 1–{schedulers} × outcome 1–{outcomes} = {worlds * schedulers * outcomes:N0} seasons, spread {spread} ---");
        var stopwatch = Stopwatch.StartNew();
        var perWorld = new List<SeasonTotals>(worlds);
        var perTuple = new List<SeasonTotals>(worlds * schedulers * outcomes);
        int seasons = 0;
        bool stopped = false;
        for (int w = firstWorld; w < firstWorld + worlds && !stopped; w++)
        {
            string path = Path.Combine(Path.GetTempPath(), $"dnd_college_analysis_{w}_{Guid.NewGuid():N}.db");
            try
            {
                using var db = new DatabaseManager(path);
                db.InitializeSchema(schemaPath);
                var players = new PlayerQueries(db);
                var baseball = new BaseballQueries(db);
                var genRng = new RngState(LeagueSeed ^ (ulong)w);
                LeagueGenerator.GenerateIfEmpty(db, players, baseball, ratingSpread: spread, ref genRng);
                var normalizer = new StatsNormalizer(db, baseball);
                var gameState = new GameStateQueries(db);
                var sum = new SeasonTotals();
                int year = PooledBaseYear;
                for (int s = 1; s <= schedulers && !stopped; s++)
                {
                    db.RunInBatch(() => gameState.SetInt64(SeasonScheduler.ScheduleSeedKey, s));
                    for (int o = 1; o <= outcomes; o++, year += 2)
                    {
                        // Each tuple gets a fresh pair of years so its schedule seed applies (ensure never regenerates).
                        var sim = new LeagueSimulator(db, baseball, normalizer,
                            new RngState(0xC5AA_0000_0000UL ^ ((ulong)w << 24) ^ ((ulong)s << 12) ^ (ulong)o), LeagueTier.College);
                        sim.Initialize();
                        long dayBase = (long)(year - PooledBaseYear) * GlobalState.DaysPerSeason;
                        for (int day = 1; day <= GlobalState.DaysPerSeason; day++)
                        {
                            sim.ProcessDay(new DayAdvancedEvent(dayBase + day, year, day));
                        }
                        sim.FlushPending();
                        SeasonTotals t = LoadSeasonTotals(baseball, year, LeagueTier.College);
                        sum = new SeasonTotals(sum.Pa + t.Pa, sum.Ab + t.Ab, sum.H + t.H, sum.Tb + t.Tb, sum.Bb + t.Bb,
                            sum.So + t.So, sum.Hr + t.Hr, sum.Er + t.Er, sum.Outs + t.Outs, sum.Gs + t.Gs);
                        perTuple.Add(t);
                        seasons++;
                        Console.WriteLine($"  tuple (world {w}, scheduler {s}, outcome {o}) year {year}: {t.Gs / 2} games, R/9 {27.0 * t.Er / t.Outs:F3}");
                        if (total.Elapsed.TotalSeconds >= stopSeconds)
                        {
                            Console.WriteLine($"  STOP RULE: {stopSeconds:F0} s reached after {seasons} seasons of this batch.");
                            stopped = true;
                            break;
                        }
                    }
                }
                // A world cut short by the stop rule is still one cluster of what it played.
                perWorld.Add(sum);
            }
            finally
            {
                TryDelete(path);
                TryDelete(path + "-wal");
                TryDelete(path + "-shm");
            }
        }

        stopwatch.Stop();
        double perSeason = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, seasons);
        Console.WriteLine($"  {seasons} seasons in {stopwatch.Elapsed.TotalSeconds:F1} s ({perSeason:F0} ms/season)" +
            (forecastFullMatrix ? $"; full-matrix forecast 16,384 × {perSeason:F0} ms × 1.25 = {16_384 * perSeason * 1.25 / 60_000:F1} min" : ""));
        if (perWorld.Count == 0)
        {
            return (stopped, false);
        }
        TierBand band = TierBands[(int)LeagueTier.College];
        bool In((double Estimate, double Se, double Lo, double Hi) v, double lo, double hi) => v.Estimate >= lo && v.Estimate <= hi;
        bool all = true;
        void Line(string name, Func<SeasonTotals, double> x, Func<SeasonTotals, double> d, double lo, double hi, double scale = 1.0)
        {
            var byWorld = PooledRatio(perWorld, x, d, scale);
            var byTuple = PooledRatio(perTuple, x, d, scale);
            bool inBand = In(byWorld, lo, hi);
            all &= inBand;
            Console.WriteLine($"  [{(gated ? (inBand ? "PASS" : "FAIL") : "DIAG")}] {name} {byWorld.Estimate:F4}  " +
                $"world-clustered (k {perWorld.Count}) SE {byWorld.Se:F5} [{byWorld.Lo:F4}, {byWorld.Hi:F4}]  " +
                $"tuple-clustered (k {perTuple.Count}) SE {byTuple.Se:F5} [{byTuple.Lo:F4}, {byTuple.Hi:F4}]  band {lo}–{hi}");
        }
        Line("AVG", x => x.H, x => x.Ab, band.AvgLo, band.AvgHi);
        Line("OBP", x => x.H + x.Bb, x => x.Pa, band.ObpLo, band.ObpHi);
        Line("SLG", x => x.Tb, x => x.Ab, band.SlgLo, band.SlgHi);
        Line("K%", x => x.So, x => x.Pa, band.KLo, band.KHi);
        Line("BB%", x => x.Bb, x => x.Pa, band.BbLo, band.BbHi);
        Line("HR/PA", x => x.Hr, x => x.Pa, band.HrLo, band.HrHi);
        Line("R/9", x => x.Er, x => x.Outs, band.RgLo, band.RgHi, 27.0);
        var rg = PooledRatio(perWorld, x => x.Er, x => x.Gs);
        var og = PooledRatio(perWorld, x => x.Outs, x => x.Gs);
        Console.WriteLine($"  ER/GS {rg.Estimate:F4} [{rg.Lo:F4}, {rg.Hi:F4}], outs/GS {og.Estimate:F4} [{og.Lo:F4}, {og.Hi:F4}] (reported, not gated); " +
            $"totals PA {perTuple.Sum(t => t.Pa):N0}, R = ER {perTuple.Sum(t => t.Er):N0}, outs {perTuple.Sum(t => t.Outs):N0}, GS {perTuple.Sum(t => t.Gs):N0}");
        return (stopped, all);
    }
}
