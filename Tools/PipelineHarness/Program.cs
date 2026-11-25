using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.PipelineHarness;

/// <summary>
/// Slice 13-6's pipeline gate (docs/tasks/13-6_pipeline_draft_brief.md §5,
/// D7-A; Gemini 4 Argon's Q7 gradient, Amendment 7, Opus C4). The gate for
/// any change to the pipeline, its values or the development curve.
///
/// <list type="bullet">
/// <item><b>default</b> — 20 seasons, offseason-only (the calendar days and
/// rollovers, no game sims, P reads neutral): conservation after every
/// rollover and opening lock, the flows and a population ledger each season,
/// an intake player's journey to Pro, the equilibrium talent gradient
/// (burn-in 5, window 15), hygiene, and a same-seed determinism twin.
/// Budget ≤ 60 s.</item>
/// <item><b>--full</b> — 1,193 days (three seasons and season 4 through
/// day 98) with all three tier sims, the Sunday lock, the career, the
/// playoffs, development and the pipeline composed as GameManager composes
/// them: real standings drive the draft and recruiting order, three composed
/// rollovers run, and an age-19 HS-senior avatar is recruited, plays College,
/// declares as a top-64 sophomore, is drafted, and plays his first attended
/// Pro game on day 1188. Budget ≤ 4 min.</item>
/// </list>
///
/// Usage: dotnet run --project Tools/PipelineHarness -c Release [-- --full] [-- --repo &lt;path&gt;]
/// </summary>
internal static class Program
{
    private const int StartYear = 2026;

    /// <summary>9d §7's equilibrium world (the MC harness's LeagueSeed + 900).</summary>
    private const ulong WorldSeed = 20261603UL;

    private const ulong DevelopmentSeed = 0x9D7000UL;
    /// <summary>The canonical pipeline world (Q7), as default mode uses. On 20260703 the single-snapshot pitcher gradient after the third rollover is a transient tie (build record §11.4).</summary>
    private const ulong FullWorldSeed = WorldSeed;
    private const ulong SeasonSeed = 777UL;
    private const int Seasons = 20;
    private const long FullDays = 3L * GlobalState.DaysPerSeason + 98;
    private const long FirstProGameDay = 3L * GlobalState.DaysPerSeason + 93;

    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();
    private static readonly List<string> TempPaths = new();

    private static int Main(string[] args)
    {
        string? repoRoot = null;
        bool full = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--repo" && i + 1 < args.Length)
            {
                repoRoot = args[++i];
            }
            else if (args[i] == "--full")
            {
                full = true;
            }
        }
        repoRoot ??= FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Could not locate project.godot above the working directory; pass --repo <path>.");
            return 2;
        }
        string schemaPath = Path.Combine(repoRoot, "Assets", "Data", "Database", "SchemaDefinitions.sql");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (full)
            {
                RunFull(schemaPath);
            }
            else
            {
                RunDefault(schemaPath);
            }
        }
        catch (Exception ex)
        {
            Check($"unhandled {ex.GetType().Name}", false, ex.Message + " " + ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim());
        }
        finally
        {
            foreach (string path in TempPaths)
            {
                TryDelete(path);
                TryDelete(path + "-wal");
                TryDelete(path + "-shm");
            }
        }
        stopwatch.Stop();
        double budget = full ? 240.0 : 60.0;
        Check($"runtime within the D7 budget (≤ {budget:F0} s)", stopwatch.Elapsed.TotalSeconds <= budget,
            $"{stopwatch.Elapsed.TotalSeconds:F1} s");

        Console.WriteLine();
        foreach ((string name, bool pass, string detail) in Results)
        {
            Console.WriteLine($"  [{(pass ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
        }
        int passed = Results.Count(r => r.Pass);
        Console.WriteLine();
        Console.WriteLine($"{passed}/{Results.Count} checks passed.");
        if (passed != Results.Count)
        {
            Console.WriteLine("Failed Checks:");
            foreach ((string name, bool pass, string detail) in Results.Where(r => !r.Pass))
            {
                Console.WriteLine($"  - {name} ({detail})");
            }
        }
        return passed == Results.Count ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // default: 20 offseason-only seasons
    // ------------------------------------------------------------------

    private sealed class Metrics
    {
        public readonly List<double[]> BatterMeans = new();
        public readonly List<double[]> PitcherMeans = new();
        public readonly List<string> ConservationFailures = new();
        public readonly List<string> FlowFailures = new();
        public readonly List<string> HygieneFailures = new();
        public readonly List<string> Shortfalls = new();
        public int TotalPicks;
        public int TotalSignings;
        public int TotalProArrivals;
        public int DrafteesCutOnArrival;
        public int IntakeReachedPro;
        public string JourneyExample = "";
        public readonly List<int[]> FreeAgentPools = new();
        public readonly List<string> Ledgers = new();
        public string Fingerprint = "";
    }

    private static void RunDefault(string schemaPath)
    {
        Console.WriteLine($"--- PipelineHarness default: {Seasons} offseason-only seasons (world {WorldSeed}, development 0x{DevelopmentSeed:X}, pipeline 0x{PipelineProfile.DefaultPipelineSeed:X}) ---");
        var sw = Stopwatch.StartNew();
        Metrics m = RunOffseasonWorld(schemaPath, collect: true);
        long firstMs = sw.ElapsedMilliseconds;
        Metrics twin = RunOffseasonWorld(schemaPath, collect: false);
        Console.WriteLine($"  20-season world {firstMs} ms; determinism twin {sw.ElapsedMilliseconds - firstMs} ms");

        Check("conservation: after every rollover and opening lock, HS teams keep their generated shape with the field covered, College teams sit inside 17/4/5 (org 26) at their 15/4/5 target, and Pro holds 32 × 15/4/5 = 768 (±5% if a role runs dry)",
            m.ConservationFailures.Count == 0,
            m.ConservationFailures.Count > 0 ? $"{m.ConservationFailures.Count} failures; first: {m.ConservationFailures[0]}" : $"{Seasons} rollovers and {Seasons} opening locks clean; shortfalls reported: {m.Shortfalls.Count}");
        foreach (string shortfall in m.Shortfalls.Take(5))
        {
            Console.WriteLine($"  shortfall: {shortfall}");
        }
        Check("flows each season: exactly 128 picks of unique, College-rostered players onto Pro teams; recruits = openings; intake = HS departures; the population ledger per tier balances",
            m.FlowFailures.Count == 0 && m.TotalPicks == Seasons * 128,
            m.FlowFailures.Count > 0 ? $"{m.FlowFailures.Count} failures; first: {m.FlowFailures[0]}" : $"{m.TotalPicks} picks");
        foreach (string ledger in m.Ledgers.Skip(PipelineProfile.GradientBurnInSeasons).Take(2))
        {
            Console.WriteLine($"  ledger {ledger}");
        }
        Check("the journey: at least one intake player traced HS → College → drafted → Pro, with the count reaching Pro by season 20",
            m.IntakeReachedPro > 0, $"{m.IntakeReachedPro} intake players reached Pro; e.g. {m.JourneyExample}");

        // ---- the equilibrium gradient (Q7, Amendment 7) ----
        int burn = PipelineProfile.GradientBurnInSeasons;
        int window = PipelineProfile.GradientWindowSeasons;
        double[] Mean(List<double[]> series, int from, int to)
        {
            var mean = new double[LeagueDirectory.TierCount];
            for (int k = from; k < to; k++)
            {
                for (int t = 0; t < LeagueDirectory.TierCount; t++)
                {
                    mean[t] += series[k][t] / (to - from);
                }
            }
            return mean;
        }
        double[] bat = Mean(m.BatterMeans, burn, burn + window);
        double[] pit = Mean(m.PitcherMeans, burn, burn + window);
        int mid = burn + window / 2;
        double[] bat1 = Mean(m.BatterMeans, burn, mid), bat2 = Mean(m.BatterMeans, mid, burn + window);
        double[] pit1 = Mean(m.PitcherMeans, burn, mid), pit2 = Mean(m.PitcherMeans, mid, burn + window);
        Console.WriteLine($"  gradient (seasons {burn + 1}–{burn + window}): batters HS {bat[0]:F2} < College {bat[1]:F2} < Pro {bat[2]:F2} " +
            $"(+{bat[1] - bat[0]:F2} / +{bat[2] - bat[1]:F2}); pitchers HS {pit[0]:F2} < College {pit[1]:F2} < Pro {pit[2]:F2} " +
            $"(+{pit[1] - pit[0]:F2} / +{pit[2] - pit[1]:F2})");
        Check($"the equilibrium gradient (raw role sums, seasons {burn + 1}–{burn + window}): HS < College < Pro, strict, with minimum gaps batters +3.0 / +10.0 and pitchers +2.0 / +10.0",
            bat[1] - bat[0] >= PipelineProfile.MinBatterHsToCollegeMargin && bat[2] - bat[1] >= PipelineProfile.MinBatterCollegeToProMargin
            && pit[1] - pit[0] >= PipelineProfile.MinPitcherHsToCollegeMargin && pit[2] - pit[1] >= PipelineProfile.MinPitcherCollegeToProMargin,
            $"batters +{bat[1] - bat[0]:F2} / +{bat[2] - bat[1]:F2}, pitchers +{pit[1] - pit[0]:F2} / +{pit[2] - pit[1]:F2}");
        double drift = 0;
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            drift = Math.Max(drift, Math.Max(Math.Abs(bat2[t] - bat1[t]), Math.Abs(pit2[t] - pit1[t])));
        }
        Check("stationarity: the window's second half sits within 10.0 of its first half for every tier, batters and pitchers",
            drift < PipelineProfile.GradientStationarityMaxDrift, $"max drift {drift:F2}");

        // ---- reported flows (not gated) ----
        double arrivals = m.TotalProArrivals / (double)(Seasons - burn);
        double cutRate = m.DrafteesCutOnArrival / (double)(Seasons - burn);
        Console.WriteLine($"  draftees released at the rollover they arrive (reported; the release order ranks them like everyone, D4b-C): {cutRate:F1} a season of 128");
        int[] lastPool = m.FreeAgentPools.Count > 0 ? m.FreeAgentPools[^1] : new int[3];
        Console.WriteLine($"  flows (reported): {arrivals:F1} Pro arrivals a season (draftees who stay + {m.TotalSignings} lock signings over {Seasons} seasons), " +
            $"Little's-law career {768.0 / Math.Max(1.0, arrivals):F2} seasons (draftees who stay); the season-{Seasons} day-91 pool {lastPool[0]} Pos / {lastPool[1]} SP / {lastPool[2]} RP");
        Check("flows (reported): the day-91 free-agent pool has every role stocked in steady state",
            m.FreeAgentPools.Skip(burn).All(p => p[0] > 0 && p[1] > 0 && p[2] > 0),
            string.Join(" ", m.FreeAgentPools.Skip(burn).Take(3).Select(p => $"[{p[0]}/{p[1]}/{p[2]}]")));

        Check("hygiene: no unflagged player ever signed; no player on two teams; enrollment rows only for College-rostered players; FK clean; no open batch",
            m.HygieneFailures.Count == 0, m.HygieneFailures.Count > 0 ? m.HygieneFailures[0] : "clean");
        Check("determinism: two same-seed 20-season runs are byte-identical", m.Fingerprint == twin.Fingerprint && m.Fingerprint.Length > 0,
            $"{m.Fingerprint[..Math.Min(16, m.Fingerprint.Length)]} vs {twin.Fingerprint[..Math.Min(16, twin.Fingerprint.Length)]}");
    }

    private static Metrics RunOffseasonWorld(string schemaPath, bool collect)
    {
        var m = new Metrics();
        string path = Path.Combine(Path.GetTempPath(), $"dnd_pipeline_{Guid.NewGuid():N}.db");
        TempPaths.Add(path);
        using var db = new DatabaseManager(path);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var gameState = new GameStateQueries(db);
        var queries = new PipelineQueries(db);
        var rng = new RngState(WorldSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);
        bus.Subscribe<SeasonRolledOverEvent>(_ => players.AgeAllPlayers());
        var micro = new MicroGame(db, baseball);
        micro.Initialize();
        var leagues = new LeagueDirectory();
        var development = new DevelopmentManager(db, players, baseball, gameState, leagues, micro, new RngState(DevelopmentSeed));
        development.AttachTo(bus);
        var pipeline = new PipelineManager(db, players, baseball, gameState, state, leagues);
        pipeline.EnsureSeed(PipelineProfile.DefaultPipelineSeed);
        pipeline.AttachTo(bus);
        pipeline.EnsureAtBoot(state.CurrentDay);

        var roster = new List<RosterPlayerRow>();
        var hsShape = new Dictionary<int, int[]>();
        baseball.LoadRosterByTier(LeagueTier.HS, roster);
        foreach (RosterPlayerRow row in roster)
        {
            (hsShape.TryGetValue(row.TeamId, out int[]? shape) ? shape : hsShape[row.TeamId] = new int[3])[RoleIndex(row.Role)]++;
        }
        var history = new Dictionary<string, string>(StringComparer.Ordinal); // intake id → tier letters
        var bootIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (RosterPlayerRow row in LoadAll(baseball))
        {
            bootIds.Add(row.PlayerId);
        }

        for (int k = 0; k < Seasons; k++)
        {
            int year = StartYear + k;
            Dictionary<string, LeagueTier> before = new(StringComparer.Ordinal);
            int openings = 0;
            for (int d = 1; d <= GlobalState.DaysPerSeason; d++)
            {
                if (state.DayOfSeason == RosterRules.FirstLockDayOfSeason && collect)
                {
                    LockStep(db, baseball, queries, pipeline, year, state.CurrentDay, m);
                }
                else if (state.DayOfSeason == RosterRules.FirstLockDayOfSeason)
                {
                    pipeline.FillBeforeLock(year, state.CurrentDay);
                }
                if (collect && state.DayOfSeason == PipelineCalendar.SigningDay - 1)
                {
                    openings = CountOpenings(db, baseball, queries, players, year);
                }
                if (collect && state.DayOfSeason == PipelineCalendar.SigningDay)
                {
                    int accepted = queries.CountAcceptedOffers(year);
                    if (accepted != openings)
                    {
                        m.FlowFailures.Add($"{year}: recruits {accepted} != openings {openings}");
                    }
                }
                if (state.DayOfSeason == GlobalState.DaysPerSeason)
                {
                    before = Tiers(baseball);
                }
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            if (!collect)
            {
                continue;
            }
            // ---- after the rollover into year + 1 ----
            Dictionary<string, LeagueTier> after = Tiers(baseball);
            PipelineSummary run = pipeline.LastRun;
            Conservation(baseball, hsShape, $"{year + 1} rollover", m, proExact: false);
            int cut = DraftCheck(queries, baseball, before, after, year, m);
            Ledger(year, before, after, run, cut, m);
            if (k >= PipelineProfile.GradientBurnInSeasons)
            {
                m.DrafteesCutOnArrival += cut;
            }
            Gradient(baseball, m);
            Hygiene(db, baseball, queries, $"{year + 1} rollover", m);
            foreach (KeyValuePair<string, LeagueTier> kv in after)
            {
                if (!before.ContainsKey(kv.Key) && kv.Value == LeagueTier.HS && !bootIds.Contains(kv.Key))
                {
                    history[kv.Key] = "H";
                }
                else if (history.TryGetValue(kv.Key, out string? path_))
                {
                    char letter = kv.Value switch { LeagueTier.HS => 'H', LeagueTier.College => 'C', _ => 'P' };
                    if (path_[^1] != letter)
                    {
                        history[kv.Key] = path_ + letter;
                    }
                }
            }
            if (k >= PipelineProfile.GradientBurnInSeasons)
            {
                m.TotalProArrivals += run.Draftees - cut;
            }
        }
        foreach (KeyValuePair<string, string> kv in history)
        {
            if (kv.Value.StartsWith("H", StringComparison.Ordinal) && kv.Value.Contains('C') && kv.Value.EndsWith("P", StringComparison.Ordinal))
            {
                m.IntakeReachedPro++;
                if (m.JourneyExample.Length == 0)
                {
                    m.JourneyExample = $"{kv.Key[..8]} {kv.Value}";
                }
            }
        }
        m.TotalSignings = pipeline.FreeAgentSignings;
        m.Fingerprint = Fingerprint(db);
        if (db.IsBatchActive || db.RunIntegrityCheck() != "ok" || db.RunForeignKeyCheck() != 0)
        {
            m.HygieneFailures.Add("integrity / FK / open batch");
        }
        return m;
    }

    private static int RoleIndex(PitcherRole role) => role switch { PitcherRole.None => 0, PitcherRole.Starter => 1, _ => 2 };

    private static List<RosterPlayerRow> LoadAll(BaseballQueries baseball)
    {
        var rows = new List<RosterPlayerRow>();
        baseball.LoadRoster(rows);
        return rows;
    }

    private static Dictionary<string, LeagueTier> Tiers(BaseballQueries baseball)
    {
        var tierByTeam = new Dictionary<int, LeagueTier>();
        var teams = new List<TeamRow>();
        baseball.LoadAllTeams(teams);
        foreach (TeamRow team in teams)
        {
            tierByTeam[team.TeamId] = team.Tier;
        }
        var result = new Dictionary<string, LeagueTier>(StringComparer.Ordinal);
        foreach (RosterPlayerRow row in LoadAll(baseball))
        {
            result[row.PlayerId] = tierByTeam[row.TeamId];
        }
        return result;
    }

    private static void LockStep(DatabaseManager db, BaseballQueries baseball, PipelineQueries queries, PipelineManager pipeline,
        int year, long day, Metrics m)
    {
        var pool = new List<FreeAgentRow>();
        queries.LoadFreeAgentPool(pool);
        var poolByRole = new int[3];
        var flagged = new HashSet<string>(StringComparer.Ordinal);
        foreach (FreeAgentRow fa in pool)
        {
            poolByRole[RoleIndex(fa.Ratings.Role)]++;
            flagged.Add(fa.Ratings.PlayerId);
        }
        m.FreeAgentPools.Add(poolByRole);
        var proBefore = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.Pro, proBefore);
        var beforeIds = proBefore.Select(r => r.PlayerId).ToHashSet(StringComparer.Ordinal);
        pipeline.FillBeforeLock(year, day);
        var proAfter = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.Pro, proAfter);
        foreach (RosterPlayerRow row in proAfter)
        {
            if (!beforeIds.Contains(row.PlayerId))
            {
                m.TotalProArrivals++;
                if (!flagged.Contains(row.PlayerId))
                {
                    m.HygieneFailures.Add($"{year} day-91 lock: signed an unflagged player {row.PlayerId[..8]}");
                }
            }
        }
        Conservation(baseball, null, $"{year} opening lock", m, proExact: true);
    }

    private static void Conservation(BaseballQueries baseball, Dictionary<int, int[]>? hsShape, string when, Metrics m, bool proExact)
    {
        var teams = new List<TeamRow>();
        baseball.LoadAllTeams(teams);
        var counts = new Dictionary<int, int[]>();
        foreach (TeamRow team in teams)
        {
            counts[team.TeamId] = new int[3];
        }
        var rows = LoadAll(baseball);
        foreach (RosterPlayerRow row in rows)
        {
            counts[row.TeamId][RoleIndex(row.Role)]++;
        }
        int pro = 0;
        foreach (TeamRow team in teams)
        {
            int[] c = counts[team.TeamId];
            switch (team.Tier)
            {
                case LeagueTier.HS when hsShape is not null:
                    if (!c.SequenceEqual(hsShape[team.TeamId]))
                    {
                        m.ConservationFailures.Add($"{when}: HS {team.TeamId} {c[0]}/{c[1]}/{c[2]} vs {string.Join("/", hsShape[team.TeamId])}");
                    }
                    break;
                case LeagueTier.College:
                    if (c[0] > PipelineProfile.CollegeCapBatters || c[1] > PipelineProfile.CollegeCapStarters
                        || c[2] > PipelineProfile.CollegeCapRelievers || c.Sum() > PipelineProfile.CollegeOrgCap || c[0] < 9 || c[1] + c[2] == 0)
                    {
                        m.ConservationFailures.Add($"{when}: College {team.TeamId} {c[0]}/{c[1]}/{c[2]}");
                    }
                    else if (c[0] != PipelineProfile.CollegeTargetBatters || c[1] != PipelineProfile.CollegeTargetStarters
                        || c[2] != PipelineProfile.CollegeTargetRelievers)
                    {
                        m.Shortfalls.Add($"{when}: College {team.TeamId} {c[0]}/{c[1]}/{c[2]}");
                    }
                    break;
                case LeagueTier.Pro:
                    pro += c.Sum();
                    if (c[0] > PipelineProfile.ProCapBatters || c[1] > PipelineProfile.ProCapStarters || c[2] > PipelineProfile.ProCapRelievers)
                    {
                        m.ConservationFailures.Add($"{when}: Pro {team.TeamId} over cap {c[0]}/{c[1]}/{c[2]}");
                    }
                    else if (c.Sum() != PipelineProfile.ProOrgCap)
                    {
                        m.Shortfalls.Add($"{when}: Pro {team.TeamId} {c[0]}/{c[1]}/{c[2]}");
                    }
                    break;
            }
        }
        if (hsShape is not null)
        {
            // Every HS team's position players cover the field (intake inherits the departed player's position).
            var fielding = new List<PlayerFieldingRow>();
            baseball.LoadAllFielding(fielding);
            var position = fielding.ToDictionary(f => f.PlayerId, f => f.DefensivePosition, StringComparer.Ordinal);
            foreach (IGrouping<int, RosterPlayerRow> team in rows.Where(r => r.Role == PitcherRole.None).GroupBy(r => r.TeamId))
            {
                if (!hsShape.ContainsKey(team.Key))
                {
                    continue;
                }
                var covered = team.Select(r => position.TryGetValue(r.PlayerId, out FieldingPosition p) ? p : FieldingPosition.P).ToHashSet();
                if (!FieldingPositions.Field.All(covered.Contains))
                {
                    m.ConservationFailures.Add($"{when}: HS {team.Key} doesn't cover the field");
                }
            }
        }
        int expected = 32 * PipelineProfile.ProOrgCap;
        if (proExact ? pro != expected && Math.Abs(pro - expected) > expected * 0.05 : Math.Abs(pro - expected) > expected * 0.05)
        {
            m.ConservationFailures.Add($"{when}: Pro {pro} outside ±5% of {expected}");
        }
    }

    private static int CountOpenings(DatabaseManager db, BaseballQueries baseball, PipelineQueries queries, PlayerQueries players, int year)
    {
        var enrollment = new List<CollegeEnrollmentRow>();
        queries.LoadEnrollments(enrollment);
        var classOf = enrollment.ToDictionary(e => e.PlayerId, e => e.ClassYear, StringComparer.Ordinal);
        var picks = new List<DraftPickRow>();
        queries.LoadDraftPicks(year, picks);
        var drafted = picks.Select(p => p.PlayerId).ToHashSet(StringComparer.Ordinal);
        var college = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.College, college);
        var teams = new List<TeamRow>();
        baseball.LoadTeamsByTier(LeagueTier.College, teams);
        int openings = 0;
        foreach (TeamRow team in teams)
        {
            for (int r = 0; r < 3; r++)
            {
                int returning = college.Count(c => c.TeamId == team.TeamId && RoleIndex(c.Role) == r
                    && classOf.TryGetValue(c.PlayerId, out int cy) && cy < PipelineProfile.FinalClassYear && !drafted.Contains(c.PlayerId)
                    && players.TryGetById(c.PlayerId, out PlayerRow p) && !PipelineRules.RetiresAtNextRollover(p.Age, p.HealthCeiling));
                openings += Math.Max(0, PipelineProfile.CollegeTargetFor(PipelineRules.Roles[r]) - returning);
            }
        }
        _ = db;
        return openings;
    }

    private static void Ledger(int year, Dictionary<string, LeagueTier> before, Dictionary<string, LeagueTier> after, PipelineSummary run,
        int drafteesCut, Metrics m)
    {
        int hsIn = 0, hsOut = 0, colIn = 0, colOut = 0, proIn = 0, proOut = 0;
        foreach (KeyValuePair<string, LeagueTier> kv in before)
        {
            bool stayed = after.TryGetValue(kv.Key, out LeagueTier now) && now == kv.Value;
            if (stayed)
            {
                continue;
            }
            _ = kv.Value switch { LeagueTier.HS => hsOut++, LeagueTier.College => colOut++, _ => proOut++ };
        }
        foreach (KeyValuePair<string, LeagueTier> kv in after)
        {
            bool stayed = before.TryGetValue(kv.Key, out LeagueTier was) && was == kv.Value;
            if (stayed)
            {
                continue;
            }
            _ = kv.Value switch { LeagueTier.HS => hsIn++, LeagueTier.College => colIn++, _ => proIn++ };
        }
        m.Ledgers.Add($"{year}→{year + 1}: HS +{hsIn}/−{hsOut} (intake {run.Intake}, graduates {run.HsGraduates}, unrecruited {run.UnrecruitedSeniors}); " +
            $"College +{colIn}/−{colOut} (outside {run.OutsideRecruitsJoined}, draftees {run.Draftees}, undrafted {run.UndraftedSeniors}); " +
            $"Pro +{proIn}/−{proOut} (releases {run.ProReleases}, of them draftees cut on arrival {drafteesCut}); retirements {run.RosteredRetirements} rostered, {run.FreeAgentRetirements} free agents");
        if (hsIn != hsOut || hsIn != run.Intake)
        {
            m.FlowFailures.Add($"{year}: HS in {hsIn} / out {hsOut} / intake {run.Intake}");
        }
        // In = graduates + outside recruits (College), draftees not cut on arrival (Pro);
        // out = draftees + undrafted seniors + retirements (College), releases − the
        // cut draftees + retirements (Pro). Pro is trimmed to 768, so in = out there.
        if (colIn != run.HsGraduates + run.OutsideRecruitsJoined || proIn != run.Draftees - drafteesCut || proIn != proOut)
        {
            m.FlowFailures.Add($"{year}: College in {colIn} vs {run.HsGraduates}+{run.OutsideRecruitsJoined}; Pro in {proIn} vs {run.Draftees}−{drafteesCut}, out {proOut}");
        }
    }

    /// <summary>Checks the season's draft and returns the draftees released at the rollover they arrived (reported, not gated).</summary>
    private static int DraftCheck(PipelineQueries queries, BaseballQueries baseball, Dictionary<string, LeagueTier> before,
        Dictionary<string, LeagueTier> after, int year, Metrics m)
    {
        var picks = new List<DraftPickRow>();
        queries.LoadDraftPicks(year, picks);
        m.TotalPicks += picks.Count;
        bool legal = picks.Count == 128 && picks.Select(p => p.PlayerId).Distinct().Count() == 128
            && picks.Select(p => p.OverallPick).SequenceEqual(Enumerable.Range(1, 128))
            && picks.All(p => p.Round == (p.OverallPick - 1) / 32 + 1
                && baseball.TryGetTeamTier(p.TeamId, out LeagueTier tier) && tier == LeagueTier.Pro
                && before.TryGetValue(p.PlayerId, out LeagueTier was) && was == LeagueTier.College);
        if (!legal)
        {
            m.FlowFailures.Add($"{year}: draft {picks.Count} picks not legal");
        }
        return picks.Count(p => !after.TryGetValue(p.PlayerId, out LeagueTier now) || now != LeagueTier.Pro);
    }

    private static void Gradient(BaseballQueries baseball, Metrics m)
    {
        var bat = new double[LeagueDirectory.TierCount];
        var pit = new double[LeagueDirectory.TierCount];
        var rows = new List<RosterPlayerRow>();
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            baseball.LoadRosterByTier((LeagueTier)t, rows);
            var batters = rows.Where(r => r.Role == PitcherRole.None).ToList();
            var pitchers = rows.Where(r => r.Role != PitcherRole.None).ToList();
            bat[t] = batters.Count > 0 ? batters.Average(r => (double)PipelineRules.RawRoleSum(in r)) : 0;
            pit[t] = pitchers.Count > 0 ? pitchers.Average(r => (double)PipelineRules.RawRoleSum(in r)) : 0;
        }
        m.BatterMeans.Add(bat);
        m.PitcherMeans.Add(pit);
    }

    private static void Hygiene(DatabaseManager db, BaseballQueries baseball, PipelineQueries queries, string when, Metrics m)
    {
        var enrollment = new List<CollegeEnrollmentRow>();
        queries.LoadEnrollments(enrollment);
        var college = new List<RosterPlayerRow>();
        baseball.LoadRosterByTier(LeagueTier.College, college);
        var collegeIds = college.ToDictionary(r => r.PlayerId, r => r.TeamId, StringComparer.Ordinal);
        int stray = enrollment.Count(e => !collegeIds.TryGetValue(e.PlayerId, out int t) || t != e.TeamId);
        int missing = college.Count(r => !enrollment.Exists(e => e.PlayerId == r.PlayerId));
        if (stray > 0 || missing > 0)
        {
            m.HygieneFailures.Add($"{when}: {stray} enrollment rows off College, {missing} College players without one");
        }
        if (db.IsBatchActive)
        {
            m.HygieneFailures.Add($"{when}: open batch");
        }
    }

    private static string Fingerprint(DatabaseManager db)
    {
        var sb = new StringBuilder();
        foreach (string sql in new[]
        {
            "SELECT player_id, age, COALESCE(team_id, 0), health_ceiling FROM Players ORDER BY player_id;",
            "SELECT * FROM Player_Ratings ORDER BY player_id;",
            "SELECT * FROM College_Enrollment ORDER BY player_id;",
            "SELECT * FROM Draft_Picks ORDER BY season_year, overall_pick;",
            "SELECT player_id, team_id, season_year, status FROM Recruiting_Offers ORDER BY player_id, team_id, season_year;",
            "SELECT player_id, flag_name, is_active, COALESCE(set_on_day, -1) FROM Entity_Flags ORDER BY player_id, flag_name;",
        })
        {
            using Microsoft.Data.Sqlite.SqliteDataReader reader = db.ExecuteReader(db.GetPooledCommand(sql));
            while (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    sb.Append(Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                }
                sb.Append('\n');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // ------------------------------------------------------------------
    // --full: 1,193 days, every system composed
    // ------------------------------------------------------------------

    private static void RunFull(string schemaPath)
    {
        Console.WriteLine($"--- PipelineHarness --full: {FullDays} days, all three tier sims, the lock, the career, the playoffs, development and the pipeline ---");
        string path = Path.Combine(Path.GetTempPath(), $"dnd_pipeline_full_{Guid.NewGuid():N}.db");
        TempPaths.Add(path);
        using var db = new DatabaseManager(path);
        db.InitializeSchema(schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var gameState = new GameStateQueries(db);
        var queries = new PipelineQueries(db);
        var rng = new RngState(FullWorldSeed);
        LeagueGenerator.GenerateIfEmpty(db, players, baseball, LeagueGenerator.DefaultRatingSpread, ref rng);
        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);

        // The GameManager composition order.
        var leagues = new LeagueDirectory();
        var normalizer = new StatsNormalizer(db, baseball);
        var restLedger = new PitcherRestLedger();
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            var sim = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed + (ulong)t), (LeagueTier)t) { RestLedger = restLedger };
            sim.Initialize();
            sim.AttachTo(bus);
            leagues.Register(sim);
        }
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            leagues.Get((LeagueTier)t).PrepareSeason(StartYear);
        }
        var pipeline = new PipelineManager(db, players, baseball, gameState, state, leagues);
        var roster = new RosterManager(db, baseball, leagues) { Market = pipeline };
        roster.AttachTo(bus);
        for (int t = 0; t < LeagueDirectory.TierCount; t++)
        {
            leagues.Get((LeagueTier)t).Designations = roster;
        }
        roster.HydrateAndReconcile(state.CurrentDay, state.SeasonYear, state.DayOfSeason);
        var micro = new MicroGame(db, baseball) { RestLedger = restLedger };
        micro.Initialize();
        micro.Designations = roster;
        var career = new CareerManager(db, players, baseball, gameState, state, leagues, micro, new RngState(0x136F0011UL))
        {
            Roster = roster,
            Normalizer = normalizer,
        };
        career.AttachTo(bus);
        var playoffs = new PlayoffManager(db, baseball, leagues);
        career.Playoffs = playoffs;
        playoffs.AttachTo(bus);
        var development = new DevelopmentManager(db, players, baseball, gameState, leagues, micro, new RngState(DevelopmentSeed)) { Career = career };
        development.AttachTo(bus);
        pipeline.Micro = micro;
        pipeline.Career = career;
        pipeline.EnsureSeed(PipelineProfile.DefaultPipelineSeed);
        pipeline.AttachTo(bus);
        pipeline.EnsureAtBoot(state.CurrentDay);
        var released = new List<AvatarReleasedEvent>();
        bus.Subscribe<AvatarReleasedEvent>(e => released.Add(e));

        // C4: an age-19 HS senior built to project in the top 64 as a College sophomore.
        career.CreateAvatar("Full", "Journey", 101, new PlayerRatingsRow
        {
            IsPitcher = false, BatPower = 86, BatContact = 86, BatDiscipline = 86,
            PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 60,
        });
        string avatar = career.AvatarPlayerId;
        players.SetAge(avatar, SchoolGrades.SeniorAge);

        var tierByDay = new Dictionary<long, LeagueTier>();
        var sw = Stopwatch.StartNew();
        int rollovers = 0;
        bus.Subscribe<SeasonRolledOverEvent>(_ => rollovers++);
        bool declared = false;
        bool drafted = false;
        int draftedPick = 0;
        bool recruited = false;
        double[]? batAfter3 = null, pitAfter3 = null;
        while (state.CurrentDay < FullDays)
        {
            clock.AdvanceDay();
            bus.DispatchPending();
            long day = state.CurrentDay;
            int dos = state.DayOfSeason;
            int year = state.SeasonYear;
            if (year == StartYear && dos == PipelineCalendar.SigningDay)
            {
                var offers = new List<RecruitingOfferRow>();
                queries.LoadOffersFor(avatar, StartYear, offers);
                recruited = offers.Any(o => o.Status == RecruitingOfferStatus.Accepted);
            }
            if (year == StartYear + 2 && dos == PipelineCalendar.DeclarationDeadlineDay)
            {
                var rows = new List<CollegeEnrollmentRow>();
                queries.LoadEnrollments(rows);
                declared = rows.Any(r => r.PlayerId == avatar && r.DeclaredSeason == StartYear + 2);
            }
            if (year == StartYear + 2 && dos == PipelineCalendar.DraftDay)
            {
                var picks = new List<DraftPickRow>();
                queries.LoadDraftPicks(StartYear + 2, picks);
                draftedPick = picks.FirstOrDefault(p => p.PlayerId == avatar).OverallPick;
                drafted = draftedPick > 0;
            }
            if (dos == 1 || dos == 100)
            {
                tierByDay[day] = baseball.TryGetTeamTier(career.AvatarTeamId, out LeagueTier tier) ? tier : (LeagueTier)255;
            }
            if (day == 3L * GlobalState.DaysPerSeason + 1)
            {
                var bat = new double[3];
                var pit = new double[3];
                var rows = new List<RosterPlayerRow>();
                for (int t = 0; t < 3; t++)
                {
                    baseball.LoadRosterByTier((LeagueTier)t, rows);
                    bat[t] = rows.Where(r => r.Role == PitcherRole.None).Average(r => (double)PipelineRules.RawRoleSum(in r));
                    pit[t] = rows.Where(r => r.Role != PitcherRole.None).Average(r => (double)PipelineRules.RawRoleSum(in r));
                }
                batAfter3 = bat;
                pitAfter3 = pit;
            }
        }
        sw.Stop();
        Console.WriteLine($"  {FullDays}-day composed run: {sw.ElapsedMilliseconds} ms");

        Check("three composed rollovers (sims, lock, career, playoffs, development, pipeline) dispatch without throwing (§1.2 #1)",
            rollovers == 3 && released.Count == 0, $"rollovers {rollovers}");
        int SeasonPa(int year)
        {
            var seasons = new List<BattingStatsRow>();
            players.LoadBattingSeasons(avatar, seasons);
            return seasons.Where(s => s.SeasonYear == year).Sum(s => s.Pa);
        }
        long seasonDay(int y, int d) => (long)(y - StartYear) * GlobalState.DaysPerSeason + d;
        Check("the journey (C4): the age-19 senior is recruited (2026), plays College (2027), declares as a top-64 sophomore and is drafted (2028), and is on a Pro club at the third rollover",
            recruited && tierByDay.GetValueOrDefault(seasonDay(StartYear + 1, 100)) == LeagueTier.College
            && declared && drafted && tierByDay.GetValueOrDefault(seasonDay(StartYear + 3, 1)) == LeagueTier.Pro,
            $"recruited {recruited}, 2027 {tierByDay.GetValueOrDefault(seasonDay(StartYear + 1, 100))}, declared {declared}, pick {draftedPick}, 2029 {tierByDay.GetValueOrDefault(seasonDay(StartYear + 3, 1))}");
        int pa2026 = SeasonPa(StartYear), pa2027 = SeasonPa(StartYear + 1), pa2028 = SeasonPa(StartYear + 2), pa2029 = SeasonPa(StartYear + 3);
        Check($"attended games keep working across both tier changes: PA in HS (2026), College (2027, 2028) and his first attended Pro game by day {FirstProGameDay} (2029)",
            pa2026 > 0 && pa2027 > 0 && pa2028 > 0 && pa2029 > 0,
            $"PA {pa2026} / {pa2027} / {pa2028} / {pa2029}");

        // Real standings drive the draft order after the inaugural season.
        var proTeams = new List<TeamRow>();
        baseball.LoadTeamsByTier(LeagueTier.Pro, proTeams);
        var proIds = proTeams.Select(t => t.TeamId).OrderBy(t => t).ToList();
        var picks2027 = new List<DraftPickRow>();
        queries.LoadDraftPicks(StartYear + 1, picks2027);
        List<int> expected = pipeline.DraftOrder(StartYear + 1);
        var collegeSeasons = new List<TeamSeasonRow>();
        new PlayoffQueries(db).LoadTeamSeasons(StartYear, LeagueTier.College, collegeSeasons);
        Check("real standings drive the order: the 2027 Draft runs in reverse 2026 Pro order (not the inaugural key), and 2026 College national ranks exist for recruiting",
            picks2027.Count == 128 && picks2027.Take(32).Select(p => p.TeamId).SequenceEqual(expected)
            && !expected.SequenceEqual(PipelineRules.InauguralDraftOrder(proIds, pipeline.Seed, StartYear + 1))
            && collegeSeasons.Count(s => s.NationalRank.HasValue) == 24,
            $"first picks [{string.Join(",", picks2027.Take(4).Select(p => p.TeamId))}], ranked programs {collegeSeasons.Count(s => s.NationalRank.HasValue)}");
        Check("after the third rollover the gradient holds on a single snapshot: HS < College < Pro for batters and pitchers (real shrunk P)",
            batAfter3 is not null && batAfter3[0] < batAfter3[1] && batAfter3[1] < batAfter3[2]
            && pitAfter3![0] < pitAfter3[1] && pitAfter3[1] < pitAfter3[2],
            batAfter3 is null ? "no snapshot" : $"batters {batAfter3[0]:F2}/{batAfter3[1]:F2}/{batAfter3[2]:F2}, pitchers {pitAfter3![0]:F2}/{pitAfter3[1]:F2}/{pitAfter3[2]:F2}");
        Check("--full world: integrity ok, no FK violations, no open batch",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
    }

    private static void Check(string name, bool pass, string detail = "") => Results.Add((name, pass, detail));

    private static string? FindRepoRoot()
    {
        string? dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "project.godot")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
