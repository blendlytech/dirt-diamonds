using System.Reflection;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Narrative.Pipeline;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// ----------------------------------------------------------------------
// Slice 13-6 (docs/tasks/13-6_pipeline_draft_brief.md): the pipeline suites
// that replace the 7 retired promotion reds — pipeline-score (pure),
// pipeline-stream, pipeline-avatar (D4a-A, D4b-C), pipeline-record (D6-A),
// pipeline-lock (free agency at the Sunday lock), the rewritten
// development-arc and practice-seam §10.9, and season-pipeline's D1-A twin
// channels 2–7. The worlds copy two generated base worlds (spread 25 and
// spread 0, the 13-5 Amendment 7 pattern) instead of re-generating.
// `--only pipeline-score,pipeline-stream,pipeline-avatar,pipeline-record,pipeline-lock`.
// ----------------------------------------------------------------------
internal static partial class Program
{
    private static readonly Dictionary<int, string> Slice136Bases = new();
    private static readonly List<string> Slice136Paths = new();

    private static long Abs136(int year, int dayOfSeason) =>
        (long)(year - StartYear) * GlobalState.DaysPerSeason + dayOfSeason;

    /// <summary>A generated, checkpointed base world (spread 25 or 0) to copy from; generated once per run.</summary>
    private static string Slice136Base(string schemaPath, int spread)
    {
        if (Slice136Bases.TryGetValue(spread, out string? path))
        {
            return path;
        }
        path = Path.Combine(Path.GetTempPath(), $"dnd_136base{spread}_{Guid.NewGuid():N}.db");
        Slice136Paths.Add(path);
        using (var db = new DatabaseManager(path))
        {
            db.InitializeSchema(schemaPath);
            var players = new PlayerQueries(db);
            var baseball = new BaseballQueries(db);
            var rng = new RngState(LeagueSeed + 1360 + (ulong)spread);
            LeagueGenerator.GenerateIfEmpty(db, players, baseball, spread, ref rng);
            db.CheckpointForSync();
        }
        Slice136Bases[spread] = path;
        return path;
    }

    private static string Slice136Copy(string schemaPath, int spread, string tag)
    {
        string path = Path.Combine(Path.GetTempPath(), $"dnd_136{tag}_{Guid.NewGuid():N}.db");
        Slice136Paths.Add(path);
        File.Copy(Slice136Base(schemaPath, spread), path);
        return path;
    }

    private static void CleanupSlice136()
    {
        foreach (string path in Slice136Paths)
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
        Slice136Paths.Clear();
        Slice136Bases.Clear();
    }

    /// <summary>
    /// One 13-6 world in the live composition order (GameManager): tier sims
    /// (registered; attached only when <c>attachSims</c>), the micro-sim, the
    /// career (or a bare aging subscriber), development, then the pipeline
    /// (the last rollover handler). Gameless unless the sims are attached.
    /// </summary>
    private sealed class PWorld : IDisposable
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
        public readonly CareerManager? Career;
        public readonly DevelopmentManager Development;
        public readonly PipelineManager Pipeline;
        public readonly List<ProContractSignedEvent> Contracts = new();
        public readonly List<FundsImpulseEvent> Funds = new();
        public readonly List<DraftCompletedEvent> Drafts = new();
        public readonly List<RecruitingOffersEvent> Offers = new();
        public readonly List<RecruitSignedEvent> Signed = new();
        public readonly List<AvatarReleasedEvent> Released = new();
        public readonly List<AvatarChangedEvent> Changed = new();
        public readonly List<DraftDeclarationOpenedEvent> Declarations = new();

        public PWorld(string schemaPath, string path, bool career, bool attachSims = false,
            int pipelineSpread = LeagueGenerator.DefaultRatingSpread, bool withSims = true, long startDay = 0,
            Action<PWorld>? setup = null, bool development = true, LeagueTier[]? simTiers = null)
        {
            Db = new DatabaseManager(path);
            Db.InitializeSchema(schemaPath);
            Players = new PlayerQueries(Db);
            Baseball = new BaseballQueries(Db);
            GameState = new GameStateQueries(Db);
            Queries = new PipelineQueries(Db);
            if (startDay > 0 && !GameState.TryGetInt64(GameStateKeys.CurrentDay, out _))
            {
                Db.RunInBatch(() =>
                {
                    GameState.SetInt64(GameStateKeys.CurrentDay, startDay);
                    GameState.SetInt64(GameStateKeys.StartSeasonYear, StartYear);
                });
            }
            Clock = new TimeManager(Db, GameState, State, Bus);
            Clock.Initialize(StartYear);
            var normalizer = new StatsNormalizer(Db, Baseball);
            if (withSims)
            {
                foreach (LeagueTier tier in simTiers ?? new[] { LeagueTier.HS, LeagueTier.College, LeagueTier.Pro })
                {
                    int t = (int)tier;
                    var sim = new LeagueSimulator(Db, Baseball, normalizer, new RngState(SeasonSeed + 1360 + (ulong)t), tier);
                    sim.Initialize();
                    if (attachSims)
                    {
                        sim.AttachTo(Bus);
                    }
                    Leagues.Register(sim);
                }
            }
            Micro = new MicroGame(Db, Baseball);
            Micro.Initialize();
            if (career)
            {
                Career = new CareerManager(Db, Players, Baseball, GameState, State, Leagues, Micro, new RngState(0x1360CA2EUL));
                Career.AttachTo(Bus);
            }
            else
            {
                Bus.Subscribe<SeasonRolledOverEvent>(_ => Players.AgeAllPlayers());
            }
            Development = new DevelopmentManager(Db, Players, Baseball, GameState, Leagues, Micro, new RngState(0x1360DE7UL))
            {
                Career = Career,
            };
            if (development)
            {
                Development.AttachTo(Bus);
            }
            Pipeline = new PipelineManager(Db, Players, Baseball, GameState, State, Leagues)
            {
                Micro = Micro,
                Career = Career,
                RatingSpread = pipelineSpread,
            };
            Pipeline.EnsureSeed(PipelineProfile.DefaultPipelineSeed);
            Pipeline.AttachTo(Bus);
            if (setup is not null)
            {
                // Fixture edits land before the boot ensure's calendar catch-up.
                Pipeline.EnsureCollegeEnrollment(State.SeasonYear);
                setup(this);
            }
            Pipeline.EnsureAtBoot(State.CurrentDay);
            Bus.Subscribe<ProContractSignedEvent>(e => Contracts.Add(e));
            Bus.Subscribe<FundsImpulseEvent>(e => Funds.Add(e));
            Bus.Subscribe<DraftCompletedEvent>(e => Drafts.Add(e));
            Bus.Subscribe<RecruitingOffersEvent>(e => Offers.Add(e));
            Bus.Subscribe<RecruitSignedEvent>(e => Signed.Add(e));
            Bus.Subscribe<AvatarReleasedEvent>(e => Released.Add(e));
            Bus.Subscribe<AvatarChangedEvent>(e => Changed.Add(e));
            Bus.Subscribe<DraftDeclarationOpenedEvent>(e => Declarations.Add(e));
        }

        public void AdvanceTo(long day)
        {
            while (State.CurrentDay < day)
            {
                Clock.AdvanceDay();
                Bus.DispatchPending();
            }
        }

        /// <summary>A career avatar on <paramref name="teamId"/> at <paramref name="age"/>, potential = current (headroom 0), with a College class row when he's College.</summary>
        public string Avatar(int teamId, int bat, int age, int classYear = 0, PitcherRole role = PitcherRole.None)
        {
            var ratings = new PlayerRatingsRow
            {
                IsPitcher = role != PitcherRole.None,
                BatPower = bat, BatContact = bat, BatDiscipline = bat,
                PitStuff = role != PitcherRole.None ? bat : 50, PitControl = role != PitcherRole.None ? bat : 50,
                PitStamina = role != PitcherRole.None ? bat : 50, Fielding = 50,
            };
            Career!.CreateAvatar("Pipe", "Line", teamId, in ratings, role == PitcherRole.None ? PitcherRole.Starter : role);
            string id = Career.AvatarPlayerId;
            Players.SetAge(id, age);
            Baseball.UpsertPotential(new PlayerPotentialRow
            {
                PlayerId = id,
                BatPower = ratings.BatPower, BatContact = ratings.BatContact, BatDiscipline = ratings.BatDiscipline,
                PitStuff = ratings.PitStuff, PitControl = ratings.PitControl, PitStamina = ratings.PitStamina, Fielding = 50,
            });
            if (classYear > 0)
            {
                Queries.UpsertEnrollment(new CollegeEnrollmentRow
                {
                    PlayerId = id, TeamId = teamId, EntrySeason = StartYear - (classYear - 1), ClassYear = classYear,
                });
            }
            return id;
        }

        public int TeamOf(string id) => Players.TryGetById(id, out PlayerRow row) && row.TeamId is int t ? t : 0;

        public int ClassOf(string id)
        {
            var rows = new List<CollegeEnrollmentRow>();
            Queries.LoadEnrollments(rows);
            foreach (CollegeEnrollmentRow row in rows)
            {
                if (row.PlayerId == id)
                {
                    return row.ClassYear;
                }
            }
            return 0;
        }

        public int? DeclaredOf(string id)
        {
            var rows = new List<CollegeEnrollmentRow>();
            Queries.LoadEnrollments(rows);
            foreach (CollegeEnrollmentRow row in rows)
            {
                if (row.PlayerId == id)
                {
                    return row.DeclaredSeason;
                }
            }
            return null;
        }

        public bool FlagActive(string id, string flag)
        {
            var ids = new List<string>();
            Baseball.LoadActiveFlagPlayerIds(flag, ids);
            return ids.Contains(id);
        }

        public bool Integrity() => !Db.IsBatchActive && Db.RunIntegrityCheck() == "ok" && Db.RunForeignKeyCheck() == 0;

        public void Dispose() => Db.Dispose();
    }

    private static readonly FieldInfo AttendedField =
        typeof(LeagueSimulator).GetField("_attendedTeamIndex", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static int AttendedIndexOf(LeagueSimulator sim) => (int)AttendedField.GetValue(sim)!;

    private static PipelineCandidateRow Cand(PitcherRole role, int a, int b, int c, int age, int headroom = 0)
    {
        var row = new RosterPlayerRow
        {
            PlayerId = $"c{a}{b}{c}{age}", Role = role, IsPitcher = role != PitcherRole.None,
            BatPower = role == PitcherRole.None ? a : 50, BatContact = role == PitcherRole.None ? b : 50,
            BatDiscipline = role == PitcherRole.None ? c : 50,
            PitStuff = role != PitcherRole.None ? a : 50, PitControl = role != PitcherRole.None ? b : 50,
            PitStamina = role != PitcherRole.None ? c : 50, Fielding = 50,
        };
        var pot = new PlayerPotentialRow
        {
            PlayerId = row.PlayerId,
            BatPower = row.BatPower + (role == PitcherRole.None ? headroom : 0), BatContact = row.BatContact,
            BatDiscipline = row.BatDiscipline,
            PitStuff = row.PitStuff + (role != PitcherRole.None ? headroom : 0), PitControl = row.PitControl,
            PitStamina = row.PitStamina, Fielding = 50,
        };
        return new PipelineCandidateRow { Ratings = row, Age = age, HealthCeiling = 100, HasPotential = true, Potential = pot };
    }

    private static double Val(in PipelineCandidateRow c, bool dock = false) =>
        PipelineRules.Value(in c, LeagueTier.HS, 0.700, 4.00, neutralPerformance: false, recordDock: dock);

    private static List<PipelineRules.Program> Programs24(int open = 3)
    {
        var list = new List<PipelineRules.Program>();
        for (int r = 1; r <= 24; r++)
        {
            list.Add(new PipelineRules.Program(200 + r, r, open));
        }
        return list;
    }

    private static (List<int> Offers, bool WalkOn, bool Displaces) OffersFor(double value, List<PipelineRules.Program> programs)
    {
        var offers = new List<int>();
        PipelineRules.SelectAvatarOffers(value, programs, offers, out bool walkOn, out bool displaces);
        return (offers, walkOn, displaces);
    }

    // ------------------------------------------------------------------
    // pipeline-score (from promotion-sweep): the pure rules
    // ------------------------------------------------------------------

    private static void RunPipelineScoreSuite()
    {
        Console.WriteLine("--- 13-6 pipeline-score: value, offers, declaration, the draft, releases, free agency (pure) ---");

        // ---- PromotionScore §2.1/§2.2, carried over from promotion-sweep ----
        var fluke = new SeasonBattingLine { PlayerId = "fluke", Pa = 3, Ab = 2, H = 2, Doubles = 1, Bb = 1 };
        var star = new SeasonBattingLine { PlayerId = "star", Pa = 600, Ab = 520, H = 160, Doubles = 30, Hr = 25, Bb = 70 };
        var solid = new SeasonBattingLine { PlayerId = "solid", Pa = 600, Ab = 540, H = 130, Doubles = 22, Hr = 12, Bb = 55 };
        double flukeP = PromotionScore.BatterPerformance(in fluke, 0.700);
        double starP = PromotionScore.BatterPerformance(in star, 0.700);
        double solidP = PromotionScore.BatterPerformance(in solid, 0.700);
        Check("§2.1 shrinkage (carried): a 3-PA fluke shrinks to ~league-average and never out-ranks a full season",
            starP > flukeP && flukeP < 105.0 && starP > solidP && starP > 115.0,
            $"fluke {flukeP:F1} solid {solidP:F1} star {starP:F1}");
        double aceP = PromotionScore.PitcherPerformance(600, 60, 4.30);
        double midP = PromotionScore.PitcherPerformance(600, 95, 4.30);
        double cupP = PromotionScore.PitcherPerformance(1, 0, 4.30);
        Check("§2.1 pitchers (carried): ERA− ordering; a 1-out 0.00-ERA cup of coffee is capped and shrunk to ~average",
            aceP > midP && aceP > 130.0 && Math.Abs(midP - 100.0) < 2.0 && cupP < 103.0,
            $"ace {aceP:F1} mid {midP:F1} cup {cupP:F1}");
        Check("§2.2 scouting (carried): younger wins at equal ratings, higher-rated at equal age; the age bonus is +10 at 15, 0 at 27, negative past it",
            PromotionScore.Scouting(150, 17) > PromotionScore.Scouting(150, 33)
            && PromotionScore.Scouting(180, 25) > PromotionScore.Scouting(120, 25)
            && PromotionScore.AgeBonus(PromotionProfile.YoungestProspectAge) == PromotionProfile.AgeBonusMax
            && PromotionScore.AgeBonus(PromotionProfile.PeakAge) == 0.0 && PromotionScore.AgeBonus(40) < 0.0);

        // ---- Amendment 1: RoleRatingSum ----
        PipelineCandidateRow sp = Cand(PitcherRole.Starter, 60, 60, 60, 25);
        PipelineCandidateRow rp = Cand(PitcherRole.Reliever, 60, 60, 60, 25);
        PipelineCandidateRow rpHigh = Cand(PitcherRole.Reliever, 60, 60, 90, 25);
        Check("Amendment 1: a reliever and a starter with equal raw tools score 20 apart (reliever stamina credited back), clamped at 100; the raw sum is uncompensated",
            PipelineRules.RoleRatingSum(in rp.Ratings) - PipelineRules.RoleRatingSum(in sp.Ratings) == LeagueGenerator.RelieverStaminaPenalty
            && PipelineRules.RoleRatingSum(in rpHigh.Ratings) == 220 && PipelineRules.RawRoleSum(in rp.Ratings) == 180,
            $"SP {PipelineRules.RoleRatingSum(in sp.Ratings)} RP {PipelineRules.RoleRatingSum(in rp.Ratings)} RP(90 stamina) {PipelineRules.RoleRatingSum(in rpHigh.Ratings)}");

        // ---- value per tier: HS shrink for seniors, College shrink for the draft ----
        PipelineCandidateRow hot = Cand(PitcherRole.None, 50, 50, 50, 19);
        hot.Batting = new SeasonBattingLine { Pa = 40, Ab = 34, H = 15, Doubles = 4, Hr = 3, Bb = 6 };
        double hsValue = PipelineRules.Value(in hot, LeagueTier.HS, 0.700, 4.0, false, false);
        double collegeValue = PipelineRules.Value(in hot, LeagueTier.College, 0.700, 4.0, false, false);
        double neutralValue = PipelineRules.Value(in hot, LeagueTier.HS, 0.700, 4.0, true, false);
        Check("value per tier: the same hot 40-PA line counts for more under HS shrink (35) than College shrink (200); neutral P reads 100",
            hsValue > collegeValue && collegeValue > neutralValue && Math.Abs(neutralValue - 100.0) < 1e-9,
            $"HS {hsValue:F2} College {collegeValue:F2} neutral {neutralValue:F2}");

        // ---- projected round ----
        Check("projected round: slots 1/32/33/64/65/128 → rounds 1/1/2/2/3/4; 129 and 0 (ineligible) → 0",
            PipelineRules.ProjectedRound(1, 32) == 1 && PipelineRules.ProjectedRound(32, 32) == 1
            && PipelineRules.ProjectedRound(33, 32) == 2 && PipelineRules.ProjectedRound(64, 32) == 2
            && PipelineRules.ProjectedRound(65, 32) == 3 && PipelineRules.ProjectedRound(128, 32) == 4
            && PipelineRules.ProjectedRound(129, 32) == 0 && PipelineRules.ProjectedRound(0, 32) == 0);

        // ---- the offer fixture (Argon Q4.5) ----
        double v86 = Val(Cand(PitcherRole.None, 36, 36, 36, 19));
        double v88 = Val(Cand(PitcherRole.None, 39, 39, 38, 19));
        double v100 = Val(Cand(PitcherRole.None, 50, 50, 50, 19));
        var o86 = OffersFor(v86, Programs24());
        var o88 = OffersFor(v88, Programs24());
        var o100 = OffersFor(v100, Programs24());
        Check("Q4.5 offers: 36/36/36 (V 86.00) gets no merit offer and a walk-on from rank 24 (team 224)",
            Math.Abs(v86 - 86.0) < 1e-9 && o86.WalkOn && !o86.Displaces && o86.Offers.SequenceEqual(new[] { 224 }),
            $"V {v86:F4}, offers [{string.Join(",", o86.Offers)}], walk-on {o86.WalkOn}");
        Check("Q4.5 offers: 39/39/38 (V 88.67) gets exactly one merit offer, from rank 24",
            Math.Abs(v88 - (50.0 + 116 / 3.0)) < 1e-9 && !o88.WalkOn && o88.Offers.SequenceEqual(new[] { 224 }),
            $"V {v88:F4}, offers [{string.Join(",", o88.Offers)}]");
        Check("Q4.5 offers: 50/50/50 (V 100.00) gets five offers, ranks 13–17 (teams 213–217)",
            Math.Abs(v100 - 100.0) < 1e-9 && !o100.WalkOn && o100.Offers.SequenceEqual(new[] { 213, 214, 215, 216, 217 }),
            $"V {v100:F4}, offers [{string.Join(",", o100.Offers)}]");
        var someOpen = Programs24(open: 0);
        someOpen[21] = someOpen[21] with { OpenSpots = 1 };
        someOpen[22] = someOpen[22] with { OpenSpots = 1 };
        var walkOpen = OffersFor(v86, someOpen);
        var walkFull = OffersFor(v86, Programs24(open: 0));
        Check("Amendment 4: the walk-on prefers an open spot (the lowest-ranked program with one, rank 23); only with none anywhere does rank 24 take him and displace",
            walkOpen.WalkOn && !walkOpen.Displaces && walkOpen.Offers.SequenceEqual(new[] { 223 })
            && walkFull.WalkOn && walkFull.Displaces && walkFull.Offers.SequenceEqual(new[] { 224 }),
            $"open → [{string.Join(",", walkOpen.Offers)}], full → [{string.Join(",", walkFull.Offers)}] displaces {walkFull.Displaces}");
        var fullForMerit = Programs24();
        fullForMerit[23] = fullForMerit[23] with { OpenSpots = 0 };
        Check("offers need an open spot in his role: with rank 24 full, the 88.67 avatar's lone merit offer becomes a walk-on from rank 23",
            OffersFor(v88, fullForMerit) is { WalkOn: true } w && w.Offers.SequenceEqual(new[] { 223 }));

        // ---- the declaration rule (Amendment 3) ----
        Check("ShouldNpcDeclare, steady state: a sophomore declares inside the top 64 only; a junior (22) and a senior always; a freshman never",
            PipelineRules.ShouldNpcDeclare(2, 21, 64) && !PipelineRules.ShouldNpcDeclare(2, 21, 65)
            && PipelineRules.ShouldNpcDeclare(3, 22, 300) && PipelineRules.ShouldNpcDeclare(4, 23, 300)
            && !PipelineRules.ShouldNpcDeclare(1, 20, 1));
        Check("ShouldNpcDeclare, the world-gen transient: an age-20 junior still declares (class ≥ 3); an age-20 sophomore off the top 64 doesn't; an age-22 sophomore does",
            PipelineRules.ShouldNpcDeclare(3, 20, 300) && !PipelineRules.ShouldNpcDeclare(2, 20, 100)
            && PipelineRules.ShouldNpcDeclare(2, 22, 300));

        // ---- draft order: the prior season, then the seeded inaugural order ----
        var teams = Enumerable.Range(301, 32).ToList();
        var buckets = teams.ToDictionary(t => t, _ => 0);
        buckets[301] = 5;
        buckets[302] = 4;
        buckets[303] = 3;
        buckets[304] = 3;
        foreach (int t in new[] { 305, 306, 307, 308 })
        {
            buckets[t] = 2;
        }
        foreach (int t in new[] { 309, 311, 312, 320 })
        {
            buckets[t] = 1;
        }
        List<int> prior = PipelineRules.PriorSeasonDraftOrder(teams, buckets);
        Check("draft order (prior season): non-playoff teams first by reverse standings (worst first), then by elimination round, the champion last; a non-playoff 310 picks before playoff team 320 though it finished higher",
            prior[0] == 332 && prior[^1] == 301 && prior[^2] == 302
            && prior.IndexOf(310) < prior.IndexOf(320) && prior.Take(20).All(t => buckets[t] == 0)
            && prior.Skip(20).Take(4).SequenceEqual(new[] { 320, 312, 311, 309 }),
            $"first {prior[0]}, picks 21–24 [{string.Join(",", prior.Skip(20).Take(4))}], last {prior[^1]}");
        var table = new List<TeamSeasonRow>
        {
            new(301, 4000, 10, 5, 60, 50, null, null),
            new(302, 4000, 10, 5, 70, 50, null, null),   // same pct, better run differential
            new(303, 4000, 10, 5, 60, 40, null, null),   // same pct and differential as 302 (+20), fewer allowed
            new(304, 4000, 5, 10, 40, 60, null, null),
        };
        List<int> tableOrder = PipelineRules.TableOnlyStandings(table, SeasonScheduler.DefaultScheduleSeed, 4000, LeagueTier.Pro);
        Check("draft order (table-only fallback): win pct, then run differential, then fewer runs allowed (I14 keys)",
            tableOrder.SequenceEqual(new[] { 303, 302, 301, 304 }), string.Join(",", tableOrder));
        List<int> inaugural = PipelineRules.InauguralDraftOrder(teams, PipelineProfile.DefaultPipelineSeed, 2026);
        List<int> again = PipelineRules.InauguralDraftOrder(teams, PipelineProfile.DefaultPipelineSeed, 2026);
        List<int> nextYear = PipelineRules.InauguralDraftOrder(teams, PipelineProfile.DefaultPipelineSeed, 2027);
        Check("draft order (inaugural, Q5): a seeded permutation of all 32 Pro teams, deterministic, keyed by season",
            inaugural.Distinct().Count() == 32 && inaugural.SequenceEqual(again) && !inaugural.SequenceEqual(nextYear)
            && !inaugural.SequenceEqual(teams),
            $"first five [{string.Join(",", inaugural.Take(5))}]");

        // ---- the role-need tiebreak and the 3/1/1 quota (Amendment 5) ----
        List<PipelineChoice> Board(params (string Id, PitcherRole Role, double Value)[] entries) =>
            entries.Select(e => new PipelineChoice { PlayerId = e.Id, Role = e.Role, Value = e.Value }).ToList();
        var need = Board(("bat", PitcherRole.None, 105.0), ("sp", PitcherRole.Starter, 103.0), ("rp", PitcherRole.Reliever, 101.0));
        int needPick = PipelineRules.SelectDraftPick(need, new[] { 15, 3, 5 }, new[] { 0, 0, 0 });
        var far = Board(("bat", PitcherRole.None, 105.0), ("sp", PitcherRole.Starter, 101.5));
        int farPick = PipelineRules.SelectDraftPick(far, new[] { 15, 3, 5 }, new[] { 0, 0, 0 });
        var quota = Board(("bat", PitcherRole.None, 105.0), ("rp", PitcherRole.Reliever, 103.0));
        int quotaPick = PipelineRules.SelectDraftPick(quota, new[] { 15, 4, 5 }, new[] { 3, 0, 0 });
        int bpaPick = PipelineRules.SelectDraftPick(quota, new[] { 15, 4, 5 }, new[] { 2, 0, 0 });
        Check("Amendment 5: a role short of its cap takes its best within 3.0 of the best overall (SP 103 over bat 105); outside the margin, best available",
            need[needPick].PlayerId == "sp" && far[farPick].PlayerId == "bat");
        Check("Amendment 5: with the best overall's role at its 3/1/1 quota, a role under quota within the margin goes instead; under quota, best available",
            quota[quotaPick].PlayerId == "rp" && quota[bpaPick].PlayerId == "bat");

        // ---- the signing bonus (Amendment 5) ----
        int[] picksAt = { 1, 32, 33, 64, 65, 96, 97, 128, 0, 129 };
        int[] bonuses = picksAt.Select(PipelineRules.SigningBonusForPick).ToArray();
        Check("SigningBonusForPick: picks 1/32/33/64/65/96/97/128 → $50,000/22,100/20,000/12,250/11,500/6,075/5,800/2,080; 0 and 129 → 0",
            bonuses.SequenceEqual(new[] { 50_000, 22_100, 20_000, 12_250, 11_500, 6_075, 5_800, 2_080, 0, 0 }),
            string.Join("/", bonuses));

        // ---- release order ----
        var group = Board(("a", PitcherRole.None, 100), ("c", PitcherRole.None, 95), ("b", PitcherRole.None, 95),
            ("d", PitcherRole.None, 90), ("e", PitcherRole.None, 80), ("f", PitcherRole.None, 85));
        var released = new List<string>();
        PipelineRules.SelectReleases(group, 4, released);
        Check("release order: keep the cap by value (ties by player_id), release the rest lowest first",
            released.SequenceEqual(new[] { "e", "f" }), string.Join(",", released));

        // ---- the free-agent retirement test (Amendment 6) ----
        Check("free-agent retirement: released into Y is kept at the rollover into Y+1 and retired at Y+2; age 30 or health 40 retires at once",
            !PipelineRules.ShouldRetireFreeAgent(25, 100, 2027, 2026) && PipelineRules.ShouldRetireFreeAgent(25, 100, 2028, 2026)
            && PipelineRules.ShouldRetireFreeAgent(30, 100, 2027, 2026) && PipelineRules.ShouldRetireFreeAgent(25, 40, 2027, 2026));

        // ---- the calendar (D2-A) and the streams (Q8) ----
        int[] days = { PipelineCalendar.DeclarationOpenDay, PipelineCalendar.DeclarationDeadlineDay, PipelineCalendar.DraftDay,
            PipelineCalendar.OffersDay, PipelineCalendar.SigningDay };
        Check("D2-A calendar: declaration opens day 176 (Mon), deadline 183 (Mon), the Draft 195 (Sat), offers 274 (Mon), Signing Day 318 (Wed); none on a Sunday",
            days.SequenceEqual(new[] { 176, 183, 195, 274, 318 })
            && days.All(d => GameCalendar.WeekdayForDayOfSeason(d) != Weekday.Sunday)
            && GameCalendar.WeekdayForDayOfSeason(318) == Weekday.Wednesday && GameCalendar.WeekdayForDayOfSeason(195) == Weekday.Saturday,
            string.Join(",", days));
        ulong[] tags = { PipelineProfile.OutsideRecruitsTag, PipelineProfile.OutsideRecruitsFieldingTag, PipelineProfile.HsIntakeTag,
            PipelineProfile.HsIntakeFieldingTag, PipelineProfile.InauguralDraftOrderTag };
        ulong[] postseason = { PostseasonStreams.PostMacroGameTag, PostseasonStreams.PostMicroGameTag, PostseasonStreams.StandingsCoinTag,
            PostseasonStreams.RankingCoinTag, PostseasonStreams.Subsample14Tag };
        RngState s1 = PipelineRules.Stream(PipelineProfile.DefaultPipelineSeed, PipelineProfile.HsIntakeTag, 2026);
        RngState s2 = PipelineRules.Stream(PipelineProfile.DefaultPipelineSeed, PipelineProfile.HsIntakeTag, 2026);
        RngState s3 = PipelineRules.Stream(PipelineProfile.DefaultPipelineSeed, PipelineProfile.HsIntakeTag, 2027);
        RngState s4 = PipelineRules.Stream(PipelineProfile.DefaultPipelineSeed, PipelineProfile.HsIntakeFieldingTag, 2026);
        Check("Q8 streams: five distinct tags, none a postseason tag; a stream is a pure function of (seed, tag, year)",
            tags.Distinct().Count() == 5 && !tags.Intersect(postseason).Any()
            && s1.NextUInt64() == s2.NextUInt64() && s1.NextUInt64() != s3.NextUInt64() && s2.NextUInt64() != s4.NextUInt64());
    }
}
