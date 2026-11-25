using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// Slice 13-6: the engine-backed pipeline suites (see Slice136Suites.cs).
internal static partial class Program
{
    private static int NewSeasonDay(int seasons) => seasons * GlobalState.DaysPerSeason + 1;

    // ------------------------------------------------------------------
    // pipeline-stream (from promotion-stream)
    // ------------------------------------------------------------------

    private static void RunPipelineStreamSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-6 pipeline-stream: a no-op pipeline is a no-op; every stream is reload-stable ---");

        // ---- a pipeline with nothing to do: no seniors, no class 4, no
        // declarations, every team at cap. Two same-seed Pro worlds (one with
        // the pipeline attached) cross the rollover and play season 2's first
        // weeks; the games must be bit-identical. ----
        var totals = new (long Pa, long H, long Er, RngState Rng)[2];
        bool noOp = false;
        for (int variant = 0; variant < 2; variant++)
        {
            bool attached = variant == 1;
            string path = Slice136Copy(schemaPath, 0, attached ? "noop_on" : "noop_off");
            using var db = new DatabaseManager(path);
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var gameState = new GameStateQueries(db);
            var queries = new PipelineQueries(db);
            // HS ≤ 17 (no seniors), College all 18 (class 1: nothing to declare, nobody leaves).
            var rows = new List<RosterPlayerRow>();
            db.RunInBatch(() =>
            {
                baseball.LoadRosterByTier(LeagueTier.HS, rows);
                foreach (RosterPlayerRow row in rows)
                {
                    players.SetAge(row.PlayerId, 16);
                }
                baseball.LoadRosterByTier(LeagueTier.College, rows);
                foreach (RosterPlayerRow row in rows)
                {
                    players.SetAge(row.PlayerId, 18);
                }
                gameState.SetInt64(GameStateKeys.CurrentDay, Abs136(StartYear, 355));
                gameState.SetInt64(GameStateKeys.StartSeasonYear, StartYear);
            });
            var state = new GlobalState();
            var bus = new EventBus();
            var clock = new TimeManager(db, gameState, state, bus);
            clock.Initialize(StartYear);
            var normalizer = new StatsNormalizer(db, baseball);
            var pro = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed + 1361), LeagueTier.Pro);
            pro.Initialize();
            pro.AttachTo(bus);
            var leagues = new LeagueDirectory();
            leagues.Register(pro);
            pro.PrepareSeason(StartYear);
            PipelineManager? pipeline = null;
            if (attached)
            {
                pipeline = new PipelineManager(db, players, baseball, gameState, state, leagues) { RatingSpread = 0 };
                pipeline.EnsureSeed(PipelineProfile.DefaultPipelineSeed);
                pipeline.AttachTo(bus);
                pipeline.EnsureAtBoot(state.CurrentDay);
            }
            while (state.CurrentDay < Abs136(StartYear + 1, 105))
            {
                clock.AdvanceDay();
                bus.DispatchPending();
            }
            LeagueBattingTotals bat = baseball.LoadLeagueBattingTotals(StartYear + 1, LeagueTier.Pro);
            LeaguePitchingTotals pit = baseball.LoadLeaguePitchingTotals(StartYear + 1, LeagueTier.Pro);
            totals[variant] = (bat.Pa, bat.H, pit.Er, pro.RngSnapshot);
            if (pipeline is not null)
            {
                PipelineSummary last = pipeline.LastRun;
                noOp = !last.ReInitialized && last.Draftees == 0 && last.ProReleases == 0 && last.Intake == 0
                    && last.HsGraduates == 0 && last.UndraftedSeniors == 0 && queries.CountDraftPicks(StartYear) == 0
                    && queries.CountAcceptedOffers(StartYear) == 0;
            }
        }
        Check("a pipeline with nothing to do (no seniors, no class 4, no declarations, every team at cap) moves nobody and re-initializes nothing",
            noOp);
        Check("…and season 2's games are bit-identical with the pipeline attached vs absent (Pro PA/H/ER and the sim's RNG)",
            totals[0].Pa == totals[1].Pa && totals[0].H == totals[1].H && totals[0].Er == totals[1].Er
            && totals[0].Rng.Equals(totals[1].Rng) && totals[0].Pa > 0,
            $"PA {totals[0].Pa}/{totals[1].Pa} H {totals[0].H}/{totals[1].H} ER {totals[0].Er}/{totals[1].Er}");

        // ---- reload stability: a save reloaded mid-season (after the Draft,
        // before Signing Day) produces the same recruits and intake ids at the
        // next rollover as an uninterrupted run ----
        string RunReloadWorld(string tag, long reloadDay)
        {
            string path = Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, tag);
            long end = NewSeasonDay(1);
            static void Seniors(PWorld p)
            {
                var hsRows = new List<RosterPlayerRow>();
                p.Baseball.LoadRosterByTier(LeagueTier.HS, hsRows);
                p.Db.RunInBatch(() =>
                {
                    foreach (RosterPlayerRow row in hsRows)
                    {
                        if (p.Players.TryGetById(row.PlayerId, out PlayerRow pr) && pr.Age == 18)
                        {
                            p.Players.SetAge(row.PlayerId, 19);
                        }
                    }
                });
            }
            if (reloadDay > 0)
            {
                using var first = new PWorld(schemaPath, path, career: false, withSims: false, startDay: 240, setup: Seniors);
                first.AdvanceTo(reloadDay);
            }
            using var w = reloadDay > 0
                ? new PWorld(schemaPath, path, career: false, withSims: false, startDay: 240)
                : new PWorld(schemaPath, path, career: false, withSims: false, startDay: 240, setup: Seniors);
            w.AdvanceTo(end);
            var sb = new System.Text.StringBuilder();
            using (Microsoft.Data.Sqlite.SqliteDataReader reader = w.Db.ExecuteReader(w.Db.GetPooledCommand(
                       "SELECT p.player_id, p.age, p.team_id, r.bat_power, r.pit_stuff, f.defensive_position FROM Players AS p " +
                       "JOIN Player_Ratings AS r ON r.player_id = p.player_id LEFT JOIN Player_Fielding AS f ON f.player_id = p.player_id " +
                       "WHERE p.team_id IS NOT NULL ORDER BY p.player_id;")))
            {
                while (reader.Read())
                {
                    for (int i = 0; i < 6; i++)
                    {
                        sb.Append(reader.IsDBNull(i) ? "-" : reader.GetValue(i).ToString()).Append(',');
                    }
                    sb.Append(';');
                }
            }
            sb.Append(DumpQueries(w.Db, new[]
            {
                "SELECT player_id, team_id, season_year, status FROM Recruiting_Offers ORDER BY player_id, team_id;",
                "SELECT * FROM College_Enrollment ORDER BY player_id;",
                "SELECT * FROM Draft_Picks ORDER BY season_year, overall_pick;",
            }));
            return Sha12(sb.ToString()) + $"|intake {w.Pipeline.LastRun.Intake}, recruits {w.Pipeline.LastRun.OutsideRecruitsJoined}";
        }
        string straight = RunReloadWorld("reload_a", 0);
        string reloaded = RunReloadWorld("reload_b", Abs136(StartYear, 290));
        Check("reload stability (Q8): a save reloaded on day 290 (after the offers, before Signing Day) reaches the rollover with byte-identical recruits, intake, enrollment and draft rows",
            straight == reloaded && !straight.Contains("intake 0,"), $"{straight} vs {reloaded}");

        // ---- the persisted seed is written once ----
        {
            string path = Slice136Copy(schemaPath, 0, "seed");
            using var db = new DatabaseManager(path);
            db.InitializeSchema(schemaPath);
            var gameState = new GameStateQueries(db);
            var state = new GlobalState();
            var pipeline = new PipelineManager(db, new PlayerQueries(db), new BaseballQueries(db), gameState, state, new LeagueDirectory());
            ulong defaultBefore = pipeline.Seed;
            pipeline.EnsureSeed(0xABCDUL);
            var reopened = new PipelineManager(db, new PlayerQueries(db), new BaseballQueries(db), gameState, state, new LeagueDirectory());
            reopened.EnsureSeed(0x1234UL);
            Check("§1.2 #7: pipeline_seed is written once from the boot draw (a later boot's draw is discarded); a fixture without one reads DefaultPipelineSeed",
                defaultBefore == PipelineProfile.DefaultPipelineSeed && pipeline.Seed == 0xABCDUL && reopened.Seed == 0xABCDUL
                && gameState.TryGetInt64(GameStateKeys.PipelineSeed, out long stored) && stored == 0xABCD);
        }
    }

    // ------------------------------------------------------------------
    // pipeline-avatar (from promotion-avatar): D4a-A, D5-A, D4b-C
    // ------------------------------------------------------------------

    private static void RunPipelineAvatarSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-6 pipeline-avatar: the grade gate, offers and the walk-on, declare/draft/Pro, the D4b-C release and the takeover ---");
        const int Spread = LeagueGenerator.DefaultRatingSpread;

        // ---- A. the four-year grade gate, senior offers, the choice, the move ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_gate"), career: true, startDay: 300,
                development: false, simTiers: new[] { LeagueTier.HS, LeagueTier.College });
            w.Pipeline.AutopilotPipeline = false;
            string id = w.Avatar(101, 100, 16);
            var tiers = new List<LeagueTier>();
            var offersByFall = new List<int>();
            for (int season = 0; season < 3; season++)
            {
                w.AdvanceTo(Abs136(StartYear + season, PipelineCalendar.OffersDay + 1));
                var offers = new List<RecruitingOfferRow>();
                offersByFall.Add(w.Queries.LoadOffersFor(id, StartYear + season, offers));
                w.AdvanceTo(NewSeasonDay(season + 1));
                w.Baseball.TryGetTeamTier(w.TeamOf(id), out LeagueTier tier);
                tiers.Add(tier);
            }
            Check("grade gate: a max-built freshman stays in HS through three offseasons (16 → 19) and gets no offer before his senior fall",
                tiers.All(t => t == LeagueTier.HS) && offersByFall.All(n => n == 0) && w.TeamOf(id) == 101,
                $"tiers [{string.Join(",", tiers)}], offers [{string.Join(",", offersByFall)}]");

            int senior = StartYear + 3;
            w.AdvanceTo(Abs136(senior, PipelineCalendar.OffersDay + 1));
            bool pending = w.Pipeline.TryGetPendingRecruitingChoice(out PendingRecruitingChoice choice);
            int chosen = pending && choice.Offers.Count > 1 ? choice.Offers[1].TeamId : 0;
            if (chosen != 0)
            {
                w.Pipeline.ResolveRecruiting(chosen);
            }
            bool answered = !w.Pipeline.TryGetPendingRecruitingChoice(out _);
            w.AdvanceTo(Abs136(senior, PipelineCalendar.SigningDay + 1));
            w.Players.InsertBattingSeason(new BattingStatsRow
            {
                PlayerId = id, SeasonYear = senior, Pa = 40, Ab = 35, H = 12, Bb = 5, Avg = 0.343, Obp = 0.425, Slg = 0.343, Ops = 0.768,
            });
            int changedBefore = w.Changed.Count;
            LeagueSimulator hs = w.Leagues.Get(LeagueTier.HS);
            LeagueSimulator college = w.Leagues.Get(LeagueTier.College);
            w.AdvanceTo(NewSeasonDay(4));
            var seasons = new List<BattingStatsRow>();
            w.Players.LoadBattingSeasons(id, seasons);
            Check("senior fall: 1–5 offers on day 274 (a max build gets five), the phone's pending choice resolves once, and Signing Day publishes RecruitSignedEvent for the chosen program",
                choice.Offers.Count == PipelineProfile.MaxAvatarOffers && answered && chosen != 0
                && w.Offers.Count == 1 && w.Offers[0].OfferCount == 5 && !w.Offers[0].IsWalkOn
                && w.Signed.Count == 1 && w.Signed[0].TeamId == chosen,
                $"offers {choice.Offers.Count}, chose {chosen}, signed {(w.Signed.Count > 0 ? w.Signed[0].TeamId : 0)}");
            Check("the rollover lands him on that program as class 1; the origin HS sim reclaims its team, the College sim claims his, AvatarChangedEvent republishes, his stats persist",
                w.TeamOf(id) == chosen && w.ClassOf(id) == 1 && w.Career!.AvatarTeamId == chosen
                && AttendedIndexOf(hs) == -1 && AttendedIndexOf(college) == college.IndexOfTeam(chosen)
                && w.Changed.Skip(changedBefore).Any(e => e.TeamId == chosen) && seasons.Any(s => s.SeasonYear == senior)
                && w.GameState.TryGetInt64(GameStateKeys.FamilyHsTeamId, out long family) && family == 101,
                $"team {w.TeamOf(id)}, class {w.ClassOf(id)}, HS attended {AttendedIndexOf(hs)}");
            Check("grade-gate world: integrity ok, no FK violations, no open batch", w.Integrity());
        }

        // ---- B. the walk-on and its displacement ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_walkon"), career: true,
                development: false, simTiers: new[] { LeagueTier.HS, LeagueTier.College });
            string id = w.Avatar(101, 20, 19);
            // Every College player returns (class 1): no program has an open spot anywhere.
            var rows = new List<CollegeEnrollmentRow>();
            w.Queries.LoadEnrollments(rows);
            w.Db.RunInBatch(() =>
            {
                foreach (CollegeEnrollmentRow row in rows)
                {
                    CollegeEnrollmentRow r = row;
                    r.ClassYear = 1;
                    w.Queries.UpsertEnrollment(in r);
                }
            });
            w.AdvanceTo(Abs136(StartYear, PipelineCalendar.SigningDay + 1));
            var offers = new List<RecruitingOfferRow>();
            w.Queries.LoadOffersFor(id, StartYear, offers);
            var college224 = new List<RosterPlayerRow>();
            w.Baseball.LoadRosterByTier(LeagueTier.College, college224);
            int battersBefore = college224.Count(r => r.TeamId == 224 && r.Role == PitcherRole.None);
            w.AdvanceTo(NewSeasonDay(1));
            w.Baseball.LoadRosterByTier(LeagueTier.College, college224);
            int battersAfter = college224.Count(r => r.TeamId == 224 && r.Role == PitcherRole.None);
            Check("D4a-A walk-on: no merit offer and no open spot anywhere → one walk-on (rank 24, team 224), accepted on Signing Day",
                w.Offers.Count == 1 && w.Offers[0].IsWalkOn && offers.Count == 1 && offers[0].TeamId == 224
                && offers[0].Status == RecruitingOfferStatus.Accepted,
                $"offers {offers.Count} → {(offers.Count > 0 ? offers[0].TeamId : 0)}");
            Check("…and at the rollover he joins 224 as class 1 by displacing its weakest same-role returning player, who retires (pipeline_retired); the program's shape holds",
                w.TeamOf(id) == 224 && w.ClassOf(id) == 1 && w.Pipeline.LastRun.Displaced == 1 && battersAfter == battersBefore,
                $"team {w.TeamOf(id)}, displaced {w.Pipeline.LastRun.Displaced}, batters {battersBefore}→{battersAfter}");
        }

        // ---- C. declare as a sophomore, the Draft, the bonus, Pro at the rollover ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_draft"), career: true,
                development: false, simTiers: new[] { LeagueTier.College, LeagueTier.Pro });
            string id = w.Avatar(201, 95, 21, classYear: 2);
            double fundsBefore = w.Players.TryGetById(id, out PlayerRow before) ? before.Funds : 0;
            w.AdvanceTo(Abs136(StartYear, PipelineCalendar.DraftDay + 1));
            var picks = new List<DraftPickRow>();
            w.Queries.LoadDraftPicks(StartYear, picks);
            DraftPickRow mine = picks.FirstOrDefault(p => p.PlayerId == id);
            int bonus = PipelineRules.SigningBonusForPick(mine.OverallPick);
            double fundsAfter = w.Players.TryGetById(id, out PlayerRow after) ? after.Funds : 0;
            Check("declaration: the window opens on day 176 (projected round in the event); autopilot declares a top-64 sophomore at the deadline",
                w.Declarations.Count == 1 && w.Declarations[0].ProjectedRound >= 1 && w.Declarations[0].ProjectedRound <= 2
                && !w.Declarations[0].IsAutomatic && w.DeclaredOf(id) == StartYear,
                $"projected slot {(w.Declarations.Count > 0 ? w.Declarations[0].ProjectedSlot : 0)}");
            Check("the Draft: 128 picks; he is drafted, ProContractSignedEvent(wasDrafted) once, the bonus credited once (AdjustFunds + one FundsImpulseEvent) and DraftCompletedEvent names his pick",
                picks.Count == 128 && mine.PlayerId == id && bonus > 0
                && w.Contracts.Count(c => c.PlayerId == id) == 1 && w.Contracts.All(c => c.WasDrafted) && w.Contracts.Count == 128
                && w.Funds.Count == 1 && w.Funds[0].Delta == bonus && Math.Abs(fundsAfter - fundsBefore - bonus) < 1e-9
                && w.Drafts.Count == 1 && w.Drafts[0].AvatarOverallPick == mine.OverallPick && w.Drafts[0].AvatarTeamId == mine.TeamId
                && w.TeamOf(id) == 201 && w.FlagActive(id, PipelineFlags.ProSigned),
                $"pick {mine.OverallPick} → {mine.TeamId}, bonus {bonus}, funds Δ {fundsAfter - fundsBefore}");
            w.AdvanceTo(NewSeasonDay(1));
            var pro = new List<RosterPlayerRow>();
            w.Baseball.LoadRosterByTier(LeagueTier.Pro, pro);
            Check("…and at the rollover he reports to the drafting Pro club (enrollment row gone), with no second contract event and no second bonus; Pro is 32 × 15/4/5",
                w.TeamOf(id) == mine.TeamId && w.ClassOf(id) == 0 && w.Contracts.Count(c => c.PlayerId == id) == 1
                && w.Funds.Count == 1 && pro.Count == 768 && w.Career!.AvatarTeamId == mine.TeamId,
                $"team {w.TeamOf(id)}, Pro {pro.Count}");
        }

        // ---- D. the undrafted junior returns; the next year, the undrafted senior is released with no children → game over ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_undrafted"), career: true,
                development: false, simTiers: new[] { LeagueTier.College });
            string id = w.Avatar(201, 15, 22, classYear: 3);
            w.AdvanceTo(Abs136(StartYear, PipelineCalendar.DeclarationDeadlineDay + 1));
            bool declared = w.DeclaredOf(id) == StartYear;
            w.AdvanceTo(Abs136(StartYear, PipelineCalendar.DraftDay + 1));
            bool cleared = w.DeclaredOf(id) is null;
            w.AdvanceTo(NewSeasonDay(1));
            Check("an undrafted junior returns: declared (class ≥ 3), undrafted, declared_season cleared, still on his program as class 4 after the rollover",
                declared && cleared && w.TeamOf(id) == 201 && w.ClassOf(id) == 4 && w.Released.Count == 0,
                $"team {w.TeamOf(id)}, class {w.ClassOf(id)}");
            w.AdvanceTo(NewSeasonDay(2));
            Check("D4b-C: an undrafted senior with no children is released → game over (NoHeirs), AvatarReleasedEvent once, unrostered and pipeline_retired, no throw",
                w.Released.Count == 1 && w.Released[0].Tier == (byte)LeagueTier.College && !w.Released[0].EnteredParentPhase
                && w.Career!.IsLineageOver && w.Career.LineageOverReason == LineageFailure.NoHeirs
                && w.TeamOf(id) == 0 && w.FlagActive(id, PipelineFlags.Retired) && !w.Career.IsAvatarRostered
                && w.GameState.TryGetText(GameStateKeys.LineageOverReason, out string reason) && reason == "NoHeirs",
                $"released {w.Released.Count}, reason {w.Career!.LineageOverReason}");
            w.AdvanceTo(NewSeasonDay(2) + 10);
            Check("…and the released, game-over avatar's world keeps ticking without throwing (no team, no attended game)",
                !w.Career.HasPendingGame && w.Integrity());
        }

        // ---- E. with children → the parent phase, then the takeover at 16 ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_parent"), career: true, startDay: 300,
                development: false, simTiers: new[] { LeagueTier.HS, LeagueTier.College });
            string id = w.Avatar(201, 15, 23, classYear: 4);
            string child = w.Career!.ConceiveChild("Kid", "Line", birthAge: 10);
            w.Players.SetBaseballInterest(child, 100);
            w.AdvanceTo(NewSeasonDay(1));
            bool attendedCleared = Enumerable.Range(0, LeagueDirectory.TierCount)
                .All(t => !w.Leagues.TryGet((LeagueTier)t, out LeagueSimulator sim) || AttendedIndexOf(sim) == -1);
            Check("D4b-C: with children, the release enters the parent phase — unrostered, pipeline_retired, every sim's attended claim cleared, AvatarTeamId 0, the key written, no throw",
                w.Released.Count == 1 && w.Released[0].EnteredParentPhase && w.Career.InParentPhase && !w.Career.IsLineageOver
                && !w.Career.IsAvatarRostered && w.Career.AvatarTeamId == 0 && w.TeamOf(id) == 0
                && w.FlagActive(id, PipelineFlags.Retired) && attendedCleared
                && w.GameState.TryGetInt64(GameStateKeys.ParentPhaseSinceDay, out long since) && since == NewSeasonDay(1)
                && w.Changed.Count > 0 && w.Changed[^1].TeamId == 0,
                $"in phase {w.Career.InParentPhase}, team {w.Career.AvatarTeamId}");
            w.Players.SetAge(child, PipelineProfile.TakeoverAge - 1);
            w.AdvanceTo(NewSeasonDay(2));
            LeagueSimulator hs = w.Leagues.Get(LeagueTier.HS);
            Check("the takeover: a willing child turning 16 takes over at the rollover as an HS freshman on the family's school (no key → the lowest HS team, 101); the pipeline now treats him as the avatar",
                w.Career.AvatarPlayerId == child && w.TeamOf(child) == 101 && w.Career.AvatarTeamId == 101 && !w.Career.InParentPhase
                && w.Career.IsAvatarRostered && AttendedIndexOf(hs) == hs.IndexOfTeam(101)
                && w.GameState.TryGetInt64(GameStateKeys.DynastyGeneration, out long generation) && generation == 2
                && w.GameState.TryGetInt64(GameStateKeys.ParentPhaseSinceDay, out long cleared) && cleared == 0
                && w.Pipeline.LastRun.AvatarTookOver && w.Pipeline.LastRun.Displaced == 1,
                $"avatar {(w.Career.AvatarPlayerId == child ? "child" : "parent")}, team {w.TeamOf(child)}");
            Check("parent-phase world: integrity ok, no FK violations, no open batch", w.Integrity());
        }

        // ---- F. every child 16 or older and unwilling → NoWillingHeir ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_nowilling"), career: true, startDay: 300,
                development: false, simTiers: new[] { LeagueTier.College });
            w.Avatar(201, 15, 23, classYear: 4);
            string child = w.Career!.ConceiveChild("Kid", "Line", birthAge: 18);
            w.Players.SetBaseballInterest(child, 0);
            w.AdvanceTo(NewSeasonDay(1));
            Check("D4b-C: released with children who are all 16+ and unwilling → game over NoWillingHeir (the parent phase ends with it)",
                w.Released.Count == 1 && w.Released[0].EnteredParentPhase && w.Career.IsLineageOver
                && w.Career.LineageOverReason == LineageFailure.NoWillingHeir && !w.Career.InParentPhase
                && w.GameState.TryGetInt64(GameStateKeys.ParentPhaseSinceDay, out long since) && since == 0,
                $"reason {w.Career.LineageOverReason}");
        }

        // ---- G. a Pro avatar the release order cuts takes the same path ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_procut"), career: true,
                development: false, simTiers: new[] { LeagueTier.Pro });
            string id = w.Avatar(301, 5, 30);
            w.AdvanceTo(NewSeasonDay(1));
            Check("D4b-C: a Pro avatar cut by the release order (his club takes on draftees) is released the same way — no children → NoHeirs",
                w.Released.Count == 1 && w.Released[0].Tier == (byte)LeagueTier.Pro && !w.Released[0].EnteredParentPhase
                && w.Career!.LineageOverReason == LineageFailure.NoHeirs && w.TeamOf(id) == 0,
                $"released {w.Released.Count}");
        }

        // ---- H. succession precedence ----
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "av_succession"), career: true,
                development: false, simTiers: new[] { LeagueTier.College });
            w.Career!.AutopilotSuccession = false;
            string id = w.Avatar(201, 15, 41, classYear: 4);
            string heir = w.Career.ConceiveChild("Heir", "Line", birthAge: 19);
            w.Players.SetBaseballInterest(heir, 100);
            w.AdvanceTo(NewSeasonDay(1));
            Check("succession precedence: a pending succession skips the avatar's moves that offseason (the class-4 avatar isn't released), while the NPC moves still run",
                w.Career.HasPendingSuccessionChoice && w.TeamOf(id) == 201 && w.Released.Count == 0
                && w.Pipeline.LastRun.Draftees == 128 && w.Pipeline.LastRun.ProReleases > 0,
                $"pending {w.Career.HasPendingSuccessionChoice}, team {w.TeamOf(id)}, draftees {w.Pipeline.LastRun.Draftees}");
        }
    }

    // ------------------------------------------------------------------
    // pipeline-record (from promotion-record): Argon's Q9 fixtures
    // ------------------------------------------------------------------

    private static void RunPipelineRecordSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-6 pipeline-record: record_arrest docks 1.0 point (offers, projected round); banned_for_life changes nothing ---");

        // A1 and A2: the offer ladder.
        PipelineCandidateRow a1 = Cand(PitcherRole.None, 39, 39, 38, 19);
        PipelineCandidateRow a2 = Cand(PitcherRole.None, 40, 40, 41, 19);
        var a1Clean = OffersFor(Val(in a1), Programs24());
        var a1Dock = OffersFor(Val(in a1, dock: true), Programs24());
        var a2Clean = OffersFor(Val(in a2), Programs24());
        var a2Dock = OffersFor(Val(in a2, dock: true), Programs24());
        Check("Q9 A1: 39/39/38 drops from one merit offer to the walk-on with record_arrest; clearing it flips back",
            !a1Clean.WalkOn && a1Clean.Offers.Count == 1 && a1Dock.WalkOn && a1Dock.Offers.Count == 1
            && OffersFor(Val(in a1), Programs24()).Offers.SequenceEqual(a1Clean.Offers));
        Check("Q9 A2: 40/40/41 drops from two offers to one",
            a2Clean.Offers.SequenceEqual(new[] { 223, 224 }) && a2Dock.Offers.SequenceEqual(new[] { 224 }) && !a2Dock.WalkOn,
            $"clean [{string.Join(",", a2Clean.Offers)}], docked [{string.Join(",", a2Dock.Offers)}]");

        // B: the spread-0 board, slot 64 → 96.
        var board = new List<double>();
        for (int i = 0; i < 63; i++)
        {
            board.Add(Val(Cand(PitcherRole.None, 52, 52, 52, 21)));
        }
        board.Add(Val(Cand(PitcherRole.None, 50, 50, 51, 21)));
        for (int i = 0; i < 31; i++)
        {
            board.Add(Val(Cand(PitcherRole.None, 50, 50, 50, 21)));
        }
        for (int i = 0; i < 200; i++)
        {
            board.Add(Val(Cand(PitcherRole.None, 49, 49, 49, 21)));
        }
        PipelineCandidateRow soph = Cand(PitcherRole.None, 51, 51, 50, 21);
        int SlotOf(double v) => board.Count(b => b > v) + 1;
        int cleanSlot = SlotOf(Val(in soph));
        int dockSlot = SlotOf(Val(in soph, dock: true));
        Check("Q9 B: a 51/51/50 sophomore on the spread-0 board falls from slot 64 (round 2) to slot 96 (round 3)",
            cleanSlot == 64 && dockSlot == 96 && PipelineRules.ProjectedRound(cleanSlot, 32) == 2
            && PipelineRules.ProjectedRound(dockSlot, 32) == 3, $"slot {cleanSlot} → {dockSlot}");

        // C and the engine: the dock is the avatar's only; banned_for_life is read nowhere.
        using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, "record"), career: true);
        string id = w.Avatar(201, 60, 21, classYear: 2);
        var college = new List<RosterPlayerRow>();
        w.Baseball.LoadRosterByTier(LeagueTier.College, college);
        string npc = college.First(r => r.TeamId == 202 && r.Role == PitcherRole.None).PlayerId;
        w.Queries.UpsertEnrollment(new CollegeEnrollmentRow { PlayerId = npc, TeamId = 202, EntrySeason = StartYear - 1, ClassYear = 2 });
        double Board(string playerId) => w.Pipeline.GetStatusView().DraftBoard.First(e => e.PlayerId == playerId).Value;
        double avatarClean = Board(id);
        double npcClean = Board(npc);
        w.Players.SetFlag(id, CareerRecordFlags.RecordArrest, true, 1);
        w.Players.SetFlag(npc, CareerRecordFlags.RecordArrest, true, 1);
        double avatarDock = Board(id);
        double npcFlagged = Board(npc);
        w.Players.SetFlag(id, CareerRecordFlags.RecordArrest, false, 2);
        w.Players.SetFlag(id, CareerRecordFlags.BannedForLife, true, 2);
        double avatarBanned = Board(id);
        Check("D6-A engine: record_arrest docks the avatar's draft-board value by exactly 1.0; an NPC carrying the flag is never read",
            Math.Abs(avatarClean - avatarDock - PipelineProfile.RecordArrestPenalty) < 1e-9 && npcFlagged == npcClean,
            $"avatar {avatarClean:F3} → {avatarDock:F3}; NPC {npcClean:F3} → {npcFlagged:F3}");
        Check("Q9 C: banned_for_life changes nothing (value, offers, slot); clearing record_arrest restores the clean value",
            avatarBanned == avatarClean && OffersFor(Val(in a1), Programs24()).Offers.SequenceEqual(a1Clean.Offers));
    }

    // ------------------------------------------------------------------
    // pipeline-lock: free agency at the Sunday lock
    // ------------------------------------------------------------------

    private static void RunPipelineLockSuite(string schemaPath)
    {
        Console.WriteLine("--- 13-6 pipeline-lock: signings below the cap, one re-init, the 0 B full-league lock, the flagged pool only ---");

        using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, "lock"), career: false, startDay: 90);
        var roster = new RosterManager(w.Db, w.Baseball, w.Leagues) { Market = w.Pipeline };
        roster.AttachTo(w.Bus);
        LeagueSimulator pro = w.Leagues.Get(LeagueTier.Pro);
        pro.PrepareSeason(StartYear);
        var proRows = new List<RosterPlayerRow>();
        w.Baseball.LoadRosterByTier(LeagueTier.Pro, proRows);
        // Team 301 loses a batter (released → a flagged free agent); an UNflagged
        // elite adult (an avatar's child) waits unrostered beside him.
        string released = proRows.First(r => r.TeamId == 301 && r.Role == PitcherRole.None).PlayerId;
        w.Db.RunInBatch(() =>
        {
            w.Players.SetTeam(released, null);
            w.Players.SetFlag(released, PipelineFlags.FreeAgent, true, 1);
            w.Players.Insert(new PlayerRow
            {
                PlayerId = "adult-child", FirstName = "Kid", LastName = "Line", Age = 25, TeamId = null, HealthCeiling = 100,
            });
            w.Baseball.UpsertRatings(new PlayerRatingsRow
            {
                PlayerId = "adult-child", BatPower = 99, BatContact = 99, BatDiscipline = 99,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
        });
        pro.Initialize();
        w.Bus.DispatchPending();
        w.Contracts.Clear();
        long lockDay = Abs136(StartYear, 91);
        roster.Lock(StartYear, lockDay, 91);
        w.Baseball.LoadRosterByTier(LeagueTier.Pro, proRows);
        w.Bus.DispatchPending();
        Check("a team below 24 signs the best flagged free agent at the next lock (the released batter returns), then re-initializes once; Pro is 768 again before designations",
            w.Pipeline.LastLockSignings == 1 && w.Pipeline.LockReinitializations == 1 && w.TeamOf(released) == 301
            && proRows.Count == 768 && roster.LastLockRows == 768 && !w.FlagActive(released, PipelineFlags.FreeAgent),
            $"signings {w.Pipeline.LastLockSignings}, re-inits {w.Pipeline.LockReinitializations}, Pro {proRows.Count}");
        Check("an unflagged unrostered player (the 99/99/99 adult child) is never signed; the signing is a first Pro contract (ProContractSignedEvent wasDrafted: false)",
            w.TeamOf("adult-child") == 0 && w.Contracts.Count == 1 && !w.Contracts[0].WasDrafted && w.Contracts[0].PlayerId == released
            && w.FlagActive(released, PipelineFlags.ProSigned));

        // ---- a full league's lock is a 0 B no-op ----
        for (int i = 0; i < 3; i++)
        {
            w.Pipeline.FillBeforeLock(StartYear, lockDay + 7);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int signed = 0;
        for (int i = 0; i < 100; i++)
        {
            signed += w.Pipeline.FillBeforeLock(StartYear, lockDay + 7);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        roster.Lock(StartYear, lockDay + 7, 98);
        Check("allocation: a full league's lock hook is a 0 B no-op (100 warm calls; no query, no re-init)",
            allocated == 0 && signed == 0 && w.Pipeline.LockReinitializations == 1, $"{allocated} B");

        // ---- a released NPC is a free agent with the flag ----
        roster.DetachFrom(w.Bus);
        w.AdvanceTo(NewSeasonDay(1));
        var pool = new List<FreeAgentRow>();
        w.Queries.LoadFreeAgentPool(pool);
        string? releasedNpc = pool.Select(p => p.Ratings.PlayerId).FirstOrDefault(p => w.FlagActive(p, PipelineFlags.FreeAgent));
        Check("the rollover's Pro releases are unrostered free agents with the pipeline_free_agent flag (set on the rollover day)",
            w.Pipeline.LastRun.ProReleases > 0 && releasedNpc is not null
            && pool.All(p => p.SetOnDay <= NewSeasonDay(1)) && pool.Count >= w.Pipeline.LastRun.ProReleases,
            $"releases {w.Pipeline.LastRun.ProReleases}, pool {pool.Count}");
        Check("lock world: integrity ok, no FK violations, no open batch", w.Integrity());
    }

    // ------------------------------------------------------------------
    // development-arc (rewritten onto the pipeline)
    // ------------------------------------------------------------------

    private static void RunDevelopmentArcSuite(string schemaPath)
    {
        Console.WriteLine("--- 9d/13-6 development arc: a developed prospect is recruited, drafted and reaches Pro; a worn veteran declines, is released and retires ---");
        string path = Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, "devarc");
        using var w = new PWorld(schemaPath, path, career: false, withSims: false);
        var hs = new List<RosterPlayerRow>();
        w.Baseball.LoadRosterByTier(LeagueTier.HS, hs);
        string prospect = NthOfRole(hs, PitcherRole.None, 0);
        w.Db.RunInBatch(() =>
        {
            w.Players.SetAge(prospect, 17);
            w.Baseball.UpsertRatings(new PlayerRatingsRow
            {
                PlayerId = prospect, BatPower = 25, BatContact = 25, BatDiscipline = 25,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 40,
            });
            w.Baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = prospect, BatPower = 92, BatContact = 92, BatDiscipline = 92,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 60,
            });
        });
        // The veteran is picked after the Draft, on a club that drafted a batter
        // (so the rollover's release step has a batter to trim).
        w.AdvanceTo(Abs136(StartYear, PipelineCalendar.DraftDay + 1));
        var picks = new List<DraftPickRow>();
        w.Queries.LoadDraftPicks(StartYear, picks);
        var proRows = new List<RosterPlayerRow>();
        w.Baseball.LoadRosterByTier(LeagueTier.Pro, proRows);
        var collegeRows = new List<RosterPlayerRow>();
        w.Baseball.LoadRosterByTier(LeagueTier.College, collegeRows);
        int club = picks.First(p => collegeRows.Any(c => c.PlayerId == p.PlayerId && c.Role == PitcherRole.None)).TeamId;
        string veteran = proRows.First(r => r.TeamId == club && r.Role == PitcherRole.None).PlayerId;
        w.Db.RunInBatch(() =>
        {
            w.Players.SetAge(veteran, 33);
            w.Players.AdjustHealthCeiling(veteran, -40);
            w.Baseball.UpsertRatings(new PlayerRatingsRow
            {
                PlayerId = veteran, BatPower = 35, BatContact = 35, BatDiscipline = 35,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 45,
            });
            w.Baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = veteran, BatPower = 35, BatContact = 35, BatDiscipline = 35,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 45,
            });
        });
        var path_ = new List<(LeagueTier Tier, int Team, int Sum)>();
        var vet = new List<(int Team, bool FreeAgent, bool Retired, int Sum)>();
        const int Years = 5;
        for (int y = 1; y <= Years; y++)
        {
            w.AdvanceTo(NewSeasonDay(y));
            int team = w.TeamOf(prospect);
            w.Baseball.TryGetTeamTier(team, out LeagueTier tier);
            w.Baseball.TryGetRatings(prospect, out PlayerRatingsRow p);
            path_.Add((team == 0 ? (LeagueTier)255 : tier, team, p.BatPower + p.BatContact + p.BatDiscipline));
            w.Baseball.TryGetRatings(veteran, out PlayerRatingsRow v);
            vet.Add((w.TeamOf(veteran), w.FlagActive(veteran, PipelineFlags.FreeAgent), w.FlagActive(veteran, PipelineFlags.Retired),
                v.BatPower + v.BatContact + v.BatDiscipline));
        }
        string Path_() => string.Join("→", path_.Select(s => $"{s.Tier}:{s.Sum}"));
        Check("the developed prospect (a raw 17-year-old): HS through his senior year, recruited into College, drafted as a sophomore, and in Pro by the fifth rollover, on DEVELOPED ratings",
            path_[0].Tier == LeagueTier.HS && path_[1].Tier == LeagueTier.HS && path_[2].Tier == LeagueTier.College
            && path_[4].Tier == LeagueTier.Pro && path_[2].Sum >= 75 + 90,
            Path_());
        Check("the worn veteran declines while rostered, is released into free agency (flagged), goes unsigned and retires at the next rollover (30+)",
            vet[0].Sum < 105 && vet[0].Team == 0 && vet[0].FreeAgent && !vet[0].Retired
            && vet[1].Team == 0 && !vet[1].FreeAgent && vet[1].Retired,
            string.Join("→", vet.Select(v => v.Team != 0 ? $"T{v.Team}:{v.Sum}" : v.Retired ? "retired" : "FA")));
        var roster = new List<RosterPlayerRow>();
        w.Baseball.LoadRoster(roster);
        var potentials = new Dictionary<string, PlayerPotentialRow>();
        w.Baseball.LoadAllPotential(potentials);
        bool neverOvershoot = roster.All(r => potentials.TryGetValue(r.PlayerId, out PlayerPotentialRow pot)
            && r.BatPower <= pot.BatPower && r.BatContact <= pot.BatContact && r.BatDiscipline <= pot.BatDiscipline
            && r.PitStuff <= pot.PitStuff && r.PitControl <= pot.PitControl && r.PitStamina <= pot.PitStamina && r.Fielding <= pot.Fielding);
        Check("§2.1 never-overshoot holds world-wide after five offseasons, intake and outside recruits included (current ≤ potential everywhere)",
            neverOvershoot && roster.Count > 0);
        Check("development world: integrity ok, no FK violations, no open batch", w.Integrity());

        // ---- determinism: a same-seed twin through the first rollover ----
        string Fingerprint(PWorld world)
        {
            var rows = new List<RosterPlayerRow>();
            world.Baseball.LoadRoster(rows);
            return Sha12(string.Join(";", rows.Select(r => $"{r.PlayerId}:{r.TeamId}:{r.BatPower},{r.PitStuff},{r.PitStamina}")));
        }
        string a;
        using (var t1 = new PWorld(schemaPath, Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, "devdetA"), career: false, withSims: false, startDay: 300))
        {
            t1.AdvanceTo(NewSeasonDay(1));
            a = Fingerprint(t1);
        }
        string b;
        using (var t2 = new PWorld(schemaPath, Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, "devdetB"), career: false, withSims: false, startDay: 300))
        {
            t2.AdvanceTo(NewSeasonDay(1));
            b = Fingerprint(t2);
        }
        Check("determinism: two same-seed worlds produce byte-identical rosters and ratings through a developed, pipelined rollover",
            a == b, $"{a} vs {b}");
    }

    // ------------------------------------------------------------------
    // practice-seam §10.9 (rewritten): the scouting seam through recruiting
    // ------------------------------------------------------------------

    private static void RunPracticeSeamRecruiting(string schemaPath)
    {
        string projectable = "", atCeiling = "", leaving = "";
        int vacated = 0;
        void Setup(PWorld p)
        {
            var hs = new List<RosterPlayerRow>();
            p.Baseball.LoadRosterByTier(LeagueTier.HS, hs);
            projectable = NthOfRole(hs, PitcherRole.None, 0);
            atCeiling = NthOfRole(hs, PitcherRole.None, 1);
            var enrollment = new List<CollegeEnrollmentRow>();
            p.Queries.LoadEnrollments(enrollment);
            var college = new List<RosterPlayerRow>();
            p.Baseball.LoadRosterByTier(LeagueTier.College, college);
            leaving = NthOfRole(college, PitcherRole.None, 0);
            vacated = college.First(r => r.PlayerId == leaving).TeamId;
            PWorld w = p;
            w.Db.RunInBatch(() =>
            {
            foreach (RosterPlayerRow row in hs)
            {
                w.Players.SetAge(row.PlayerId, 17); // no other seniors
            }
            foreach (string probe in new[] { projectable, atCeiling })
            {
                w.Players.SetAge(probe, 19);
                w.Baseball.UpsertRatings(new PlayerRatingsRow
                {
                    PlayerId = probe, BatPower = 54, BatContact = 54, BatDiscipline = 54,
                    PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
                });
            }
            w.Baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = projectable, BatPower = 84, BatContact = 84, BatDiscipline = 84,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
            w.Baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = atCeiling, BatPower = 54, BatContact = 54, BatDiscipline = 54,
                PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
            // Everyone returns (class 1): nobody is draft-eligible, so the Draft is empty.
            foreach (CollegeEnrollmentRow row in enrollment)
            {
                CollegeEnrollmentRow r = row;
                r.ClassYear = 1;
                w.Queries.UpsertEnrollment(in r);
            }
            });
        }
        using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, 0, "seam"), career: false, withSims: false,
            pipelineSpread: 0, startDay: 170, setup: Setup);
        // After the Draft, exactly one College opening appears: one batter's eligibility ends.
        w.AdvanceTo(Abs136(StartYear, PipelineCalendar.DraftDay + 1));
        w.Queries.UpsertEnrollment(new CollegeEnrollmentRow
        {
            PlayerId = leaving, TeamId = vacated, EntrySeason = StartYear - 3, ClassYear = PipelineProfile.FinalClassYear,
        });
        // The surplus outside recruits (48/48/48 at spread 0, V 98) sit below both probes.
        w.AdvanceTo(Abs136(StartYear, PipelineCalendar.SigningDay + 1));
        var accepted = new List<RecruitingOfferRow>();
        w.Queries.LoadAcceptedOffers(StartYear, accepted);
        Check("§10.9 scouting seam through recruiting: the one College opening goes to the HIGH-HEADROOM senior; his at-ceiling twin (identical current line and age) goes unrecruited",
            accepted.Count == 1 && accepted[0].PlayerId == projectable && accepted[0].TeamId == vacated
            && w.Pipeline.LastOutsideRecruitsSigned == 0,
            $"accepted {accepted.Count}: {(accepted.Count > 0 ? (accepted[0].PlayerId == projectable ? "projectable" : accepted[0].PlayerId) : "-")} → {(accepted.Count > 0 ? accepted[0].TeamId : 0)} (vacated {vacated})");
        w.AdvanceTo(NewSeasonDay(1));
        Check("…and at the rollover he joins the vacated program as class 1 while the twin retires from baseball (pipeline_retired); integrity ok",
            w.TeamOf(projectable) == vacated && w.ClassOf(projectable) == 1 && w.TeamOf(atCeiling) == 0
            && w.FlagActive(atCeiling, PipelineFlags.Retired) && w.Integrity());
    }
}
