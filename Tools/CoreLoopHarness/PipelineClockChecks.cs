using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Narrative.Pipeline;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.CoreLoopHarness;

/// <summary>
/// Slice 13-6 CoreLoop fixtures (brief §5): the live composition crosses a
/// rollover (§1.2 #1), the avatar's pending choice survives a reload and
/// resolves once, a reload after the Draft doesn't re-draft, the pipeline's
/// texts land on their days, the clock never stops for a pipeline decision,
/// and the D4b-C parent phase survives a reload and ends in a takeover at 16.
/// </summary>
internal static partial class Program
{
    /// <summary>GameManager's composition order, headless: sims → pipeline (before the lock) → lock → micro → career → playoffs → development → the pipeline's slot → beats.</summary>
    private sealed class LiveWorld : IDisposable
    {
        public readonly DatabaseManager Db;
        public readonly PlayerQueries Players;
        public readonly BaseballQueries Baseball;
        public readonly GameStateQueries GameState;
        public readonly PipelineQueries Queries;
        public readonly GlobalState State = new();
        public readonly EventBus Bus = new();
        public readonly TimeManager Clock;
        public readonly LeagueDirectory Leagues = new();
        public readonly MicroGame Micro;
        public readonly CareerManager Career;
        public readonly PipelineManager Pipeline;
        public readonly RosterManager Roster;
        public readonly bool AvatarLoaded;

        public LiveWorld(string schemaPath, string path, long startDay)
        {
            Db = new DatabaseManager(path);
            Db.InitializeSchema(schemaPath);
            Players = new PlayerQueries(Db);
            Baseball = new BaseballQueries(Db);
            GameState = new GameStateQueries(Db);
            Queries = new PipelineQueries(Db);
            var genRng = new RngState(20260703UL);
            bool fresh = LeagueGenerator.GenerateIfEmpty(Db, Players, Baseball, LeagueGenerator.DefaultRatingSpread, ref genRng);
            if (fresh && startDay > 1)
            {
                Db.RunInBatch(() =>
                {
                    GameState.SetInt64(GameStateKeys.CurrentDay, startDay);
                    GameState.SetInt64(GameStateKeys.StartSeasonYear, 2026);
                });
            }
            Clock = new TimeManager(Db, GameState, State, Bus);
            Clock.Initialize(2026);
            var normalizer = new StatsNormalizer(Db, Baseball);
            var rest = new PitcherRestLedger();
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                var sim = new LeagueSimulator(Db, Baseball, normalizer, new RngState(0x1360_0000UL + (ulong)t), (LeagueTier)t) { RestLedger = rest };
                sim.Initialize();
                sim.AttachTo(Bus);
                Leagues.Register(sim);
            }
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                Leagues.Get((LeagueTier)t).PrepareSeason(State.SeasonYear);
            }
            Pipeline = new PipelineManager(Db, Players, Baseball, GameState, State, Leagues);
            Roster = new RosterManager(Db, Baseball, Leagues) { Market = Pipeline };
            Roster.AttachTo(Bus);
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                Leagues.Get((LeagueTier)t).Designations = Roster;
            }
            Roster.HydrateAndReconcile(State.CurrentDay, State.SeasonYear, State.DayOfSeason);
            Micro = new MicroGame(Db, Baseball) { LoggingEnabled = false, RestLedger = rest, Designations = Roster };
            Micro.Initialize();
            Career = new CareerManager(Db, Players, Baseball, GameState, State, Leagues, Micro, new RngState(0x136C_0E1FUL))
            {
                Normalizer = normalizer, Roster = Roster,
            };
            AvatarLoaded = Career.LoadExistingAvatar();
            Career.AttachTo(Bus);
            var playoffs = new PlayoffManager(Db, Baseball, Leagues);
            Career.Playoffs = playoffs;
            playoffs.AttachTo(Bus);
            if (AvatarLoaded)
            {
                Career.ReclaimRegularSeasonGame(State.CurrentDay);
                Career.ReclaimPostseasonGame(State.CurrentDay);
            }
            var development = new DevelopmentManager(Db, Players, Baseball, GameState, Leagues, Micro, new RngState(0x136D_E7UL)) { Career = Career };
            development.AttachTo(Bus);
            Pipeline.Micro = Micro;
            Pipeline.Career = Career;
            Pipeline.EnsureSeed(0x136C_0000_5EEDUL);
            Pipeline.AutopilotPipeline = false;
            Pipeline.AttachTo(Bus);
            Pipeline.EnsureAtBoot(State.CurrentDay);
            new PipelineBeatWriter(Db, new NarrativeLogQueries(Db), State).AttachTo(Bus);
        }

        /// <summary>Advances to the day, resolving any parked attended game with the autopilot (the UI would play it); returns false if the clock ever had to stop for something other than a game.</summary>
        public void AdvanceTo(long day, Action? eachDay = null)
        {
            while (State.CurrentDay < day)
            {
                Clock.AdvanceDay();
                Bus.DispatchPending();
                eachDay?.Invoke();
            }
        }

        public List<NarrativeMessageRow> Texts(string playerId)
        {
            var rows = new List<NarrativeMessageRow>();
            new NarrativeLogQueries(Db).LoadForPlayer(playerId, State.CurrentDay, rows);
            return rows;
        }

        public int TeamOf(string id) => Players.TryGetById(id, out PlayerRow row) && row.TeamId is int t ? t : 0;

        public void Dispose() => Db.Dispose();
    }

    private static void RunPipelineClockChecks(string schemaPath, string scratchPath, string parentPath)
    {
        Console.WriteLine("--- 13-6 pipeline clock (the composed rollover, reloads, texts on their days, the clock never stops, the parent phase) ---");
        long Day(int year, int dayOfSeason) => (long)(year - 2026) * GlobalState.DaysPerSeason + dayOfSeason;

        // ---- the senior's fall in the live composition ----
        string avatarId;
        {
            using var w = new LiveWorld(schemaPath, scratchPath, startDay: 170);
            w.Career.CreateAvatar("Clock", "Senior", 101, new PlayerRatingsRow
            {
                IsPitcher = false, BatPower = 80, BatContact = 80, BatDiscipline = 80,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
            avatarId = w.Career.AvatarPlayerId;
            w.Players.SetAge(avatarId, SchoolGrades.SeniorAge);
            w.AdvanceTo(Day(2026, PipelineCalendar.DraftDay));
        }
        {
            // A reload on day 195 after the Draft's commit doesn't re-draft.
            using var w = new LiveWorld(schemaPath, scratchPath, startDay: 170);
            var picks = new List<DraftPickRow>();
            w.Queries.LoadDraftPicks(2026, picks);
            Check("13-6: a reload on day 195 after the Draft's commit doesn't re-draft (128 picks, the reloaded pipeline ran no draft)",
                w.AvatarLoaded && picks.Count == 128 && w.Pipeline.DraftsRun == 0 && w.State.DayOfSeason == PipelineCalendar.DraftDay,
                $"picks {picks.Count}, drafts run {w.Pipeline.DraftsRun}");
            bool neverStopped = true;
            w.AdvanceTo(Day(2026, PipelineCalendar.OffersDay + 6), () => neverStopped &= !w.Career.HasPendingGame);
            bool pending = w.Pipeline.TryGetPendingRecruitingChoice(out PendingRecruitingChoice before);
            List<NarrativeMessageRow> texts = w.Texts(avatarId);
            Check("13-6: the offers text arrives on day 274 from the HS coach; the clock never stops for the pending choice (days run on, no parked game)",
                pending && before.Offers.Count >= 1 && neverStopped
                && texts.Any(t => t.ContactId == PipelineBeatWriter.HsCoachContact && t.Body == PipelineBeatWriter.OffersKey
                    && t.GameDay == Day(2026, PipelineCalendar.OffersDay)),
                $"pending {pending}, offers {before.Offers.Count}, texts {texts.Count}");
        }
        int chosen;
        {
            // The pending recruiting choice survives a reload and resolves once.
            using var w = new LiveWorld(schemaPath, scratchPath, startDay: 170);
            bool stillPending = w.Pipeline.TryGetPendingRecruitingChoice(out PendingRecruitingChoice choice);
            chosen = choice.Offers[^1].TeamId;
            w.Pipeline.ResolveRecruiting(chosen);
            bool resolvedOnce = !w.Pipeline.TryGetPendingRecruitingChoice(out _);
            bool threw = false;
            try
            {
                w.Pipeline.ResolveRecruiting(chosen);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Check("13-6: a pending recruiting choice survives a reload (same offers, still open) and resolves once (a second answer is refused)",
                stillPending && resolvedOnce && threw, $"offers {choice.Offers.Count}, chose {chosen}");
            w.AdvanceTo(Day(2026, PipelineCalendar.SigningDay));
            List<NarrativeMessageRow> texts = w.Texts(avatarId);
            Check("13-6: the Signing Day text arrives on day 318 from the College coach",
                texts.Any(t => t.ContactId == PipelineBeatWriter.CollegeCoachContact && t.Body == PipelineBeatWriter.SignedKey
                    && t.GameDay == Day(2026, PipelineCalendar.SigningDay)));
            int changed = 0;
            w.Bus.Subscribe<AvatarChangedEvent>(_ => changed++);
            Exception? failure = null;
            try
            {
                w.AdvanceTo(Day(2027, 1));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            LeagueSimulator college = w.Leagues.Get(LeagueTier.College);
            Check("13-6: a rollover with the live composition (three tier sims, the lock, the career, the playoffs, development, the pipeline) doesn't throw and lands the avatar on the program he chose",
                failure is null && w.TeamOf(avatarId) == chosen && w.Career.AvatarTeamId == chosen && changed > 0
                && college.TryGetScheduledGame(2027, TierSeasonCalendar.GameDay(LeagueTier.College, 1), chosen, out _, out _, out _),
                failure?.Message ?? $"team {w.TeamOf(avatarId)} (chose {chosen})");
        }

        // ---- D4b-C: the parent phase survives a reload; the takeover at 16 ----
        string parent;
        string child;
        {
            using var w = new LiveWorld(schemaPath, parentPath, startDay: 300);
            w.Career.CreateAvatar("Done", "Senior", 201, new PlayerRatingsRow
            {
                IsPitcher = false, BatPower = 10, BatContact = 10, BatDiscipline = 10,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 10,
            });
            parent = w.Career.AvatarPlayerId;
            w.Players.SetAge(parent, 24);
            w.Queries.UpsertEnrollment(new CollegeEnrollmentRow { PlayerId = parent, TeamId = 201, EntrySeason = 2023, ClassYear = 4 });
            child = w.Career.ConceiveChild("Next", "Senior", birthAge: 12);
            w.Players.SetBaseballInterest(child, 100);
            w.AdvanceTo(Day(2027, 3));
            Check("13-6 D4b-C: the undrafted senior with a child is released into the parent phase at the rollover (unrostered, no throw)",
                w.Career.InParentPhase && !w.Career.IsAvatarRostered && w.TeamOf(parent) == 0);
        }
        {
            using var w = new LiveWorld(schemaPath, parentPath, startDay: 300);
            bool offered = false;
            long from = w.State.CurrentDay;
            w.AdvanceTo(from + 7, () => offered |= w.Career.HasPendingGame);
            Check("13-6 D4b-C: a released avatar in the parent phase survives a reload — still unrostered, no throw, the week advances, no attended game is offered",
                w.AvatarLoaded && w.Career.AvatarPlayerId == parent && w.Career.InParentPhase && !w.Career.IsAvatarRostered
                && w.Career.AvatarTeamId == 0 && w.State.CurrentDay == from + 7 && !offered
                && !w.Career.TryGetScheduledGameFor(w.State.CurrentDay + 1, out _, out _),
                $"day {from}→{w.State.CurrentDay}, offered {offered}");
            w.Players.SetAge(child, PipelineProfile.TakeoverAge - 1);
            w.AdvanceTo(Day(2028, 1));
            Check("13-6 D4b-C: a child's takeover at 16 lands him on the right HS team (the family key is missing → the lowest-id HS team) as the avatar",
                w.Career.AvatarPlayerId == child && w.TeamOf(child) == 101 && w.Career.AvatarTeamId == 101 && !w.Career.InParentPhase,
                $"avatar {(w.Career.AvatarPlayerId == child ? "child" : "parent")}, team {w.TeamOf(child)}");
        }
    }
}
