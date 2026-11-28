using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Data.Family;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.MonteCarloHarness;

// ----------------------------------------------------------------------
// Slice 13-6c (docs/tasks/13-6c_parent_phase_brief.md §5, `--only parent-phase`):
// the parent phase's engine.
//   (1) ChildTraining's odds, tilt, expertise (R6's RoleRatingSum overload),
//       fatigue, determinism, diminishing returns and the max(1, …) floor;
//   (2) the basis-point carry, the season cap and the fade;
//   (3) the A5 streams (distinct seeds, a pinned FNV-1a vector, distinct tags,
//       no GetHashCode);
//   (4) the identity: no job, no session → nothing moves through a takeover fold;
//   (5) a trained child's takeover folds exactly NurtureBlend's numbers;
//   (6) source scans (Child edges only, no parentage read, no boot draw);
//   (8) the lineage-failure rule without the pipeline is unchanged.
// (7) ui-strings belongs to 13-6d. Every check name carries "13-6c" so the
// lines this slice adds to the default MC log are removable by one rule.
// ----------------------------------------------------------------------
internal static partial class Program
{
    private static void RunParentPhaseSuite(string schemaPath, string repoRoot)
    {
        Console.WriteLine("--- 13-6c parent-phase (training rules, carry, streams, identity, fold, scans, lineage-failure) ---");
        RunTrainingRuleChecks(schemaPath);
        RunTrainingCarryChecks();
        RunTrainingStreamChecks(repoRoot);
        RunParentPhaseIdentityAndFoldChecks(schemaPath, repoRoot);
        RunParentPhaseSourceScans(repoRoot);
        RunParentPhaseLineageFailureCheck(schemaPath);
    }

    // ------------------------------------------------------------------
    // (1) the rules
    // ------------------------------------------------------------------

    private static void RunTrainingRuleChecks(string schemaPath)
    {
        int floor = ChildTraining.OddsPp(TrainingIntensity.Hard, -50, 0, ChildTraining.TiltPp(0.0), ChildTraining.FatiguePp(100));
        int ceiling = ChildTraining.OddsPp(TrainingIntensity.Easy, 10, 10, ChildTraining.TiltPp(1.0), 0);
        int coachNormal = ChildTraining.OddsPp(TrainingIntensity.Normal, 10, 0, 0, 0);
        Check("13-6c (1) odds: clamped to [5, 95] (Hard − 50 − 15 − 10 → 5; Easy + 10 + 10 + 15 → 95); Coach Normal at neutral = 70",
            floor == 5 && ceiling == 95 && coachNormal == 70, $"{floor} / {ceiling} / {coachNormal}");

        Check("13-6c (1) tilt: 0 at a score of 0.5 (and NaN), −15 at 0, +15 at 1, clamped outside [0, 1]",
            ChildTraining.TiltPp(0.5) == 0 && ChildTraining.TiltPp(double.NaN) == 0
            && ChildTraining.TiltPp(0.0) == -15 && ChildTraining.TiltPp(1.0) == 15
            && ChildTraining.TiltPp(-3.0) == -15 && ChildTraining.TiltPp(7.0) == 15,
            $"{ChildTraining.TiltPp(0.0)} / {ChildTraining.TiltPp(0.5)} / {ChildTraining.TiltPp(1.0)}");

        var flat = new Func<int, PlayerRatingsRow>(v => new PlayerRatingsRow
        {
            BatPower = v, BatContact = v, BatDiscipline = v, PitStuff = v, PitControl = v, PitStamina = v, Fielding = v,
        });
        var flatPotential = new Func<int, PlayerPotentialRow>(v => new PlayerPotentialRow
        {
            BatPower = v, BatContact = v, BatDiscipline = v, PitStuff = v, PitControl = v, PitStamina = v, Fielding = v,
        });
        PlayerRatingsRow aged = flat(40);
        PlayerPotentialRow prime = flatPotential(60);
        PlayerRatingsRow peakNow = flat(75);
        PlayerPotentialRow lowPotential = flatPotential(50);
        int fromPotential = ChildTraining.ExpertisePp(ChildTraining.PeakRoleSum(PitcherRole.None, in aged, in prime));
        int fromCurrent = ChildTraining.ExpertisePp(ChildTraining.PeakRoleSum(PitcherRole.None, in peakNow, in lowPotential));
        Check("13-6c (1) expertise: 0 at 135 (and below), +2 at 150, +5 at 180, +10 at 225 (and above); the peak of current and potential (an aged 120 with a 180 ceiling → +5; a 225 now over a 150 ceiling → +10)",
            ChildTraining.ExpertisePp(135) == 0 && ChildTraining.ExpertisePp(90) == 0 && ChildTraining.ExpertisePp(150) == 2
            && ChildTraining.ExpertisePp(180) == 5 && ChildTraining.ExpertisePp(225) == 10 && ChildTraining.ExpertisePp(300) == 10
            && fromPotential == 5 && fromCurrent == 10,
            $"aged {fromPotential}, peak-now {fromCurrent}");

        // R6: the pure overload agrees with the row overload for every rostered player of a generated world.
        int rows = 0;
        int mismatches = 0;
        int relievers = 0;
        string path = Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, "pp_rolesum");
        using (var db = new DatabaseManager(path))
        {
            InitializeIfFresh(db, schemaPath);
            var baseball = new BaseballQueries(db);
            var roster = new List<RosterPlayerRow>();
            for (int t = 0; t < LeagueDirectory.TierCount; t++)
            {
                baseball.LoadRosterByTier((LeagueTier)t, roster);
                foreach (RosterPlayerRow r in roster)
                {
                    rows++;
                    relievers += r.Role == PitcherRole.Reliever ? 1 : 0;
                    int pure = r.Role == PitcherRole.None
                        ? PipelineRules.RoleRatingSum(r.Role, r.BatPower, r.BatContact, r.BatDiscipline)
                        : PipelineRules.RoleRatingSum(r.Role, r.PitStuff, r.PitControl, r.PitStamina);
                    mismatches += pure == PipelineRules.RoleRatingSum(in r) ? 0 : 1;
                }
            }
        }
        Check("13-6c (1) R6: RoleRatingSum(role, three tools) equals the shipped RoleRatingSum(row) for every rostered player of a generated world (reliever stamina credit included)",
            rows > 1000 && relievers > 0 && mismatches == 0, $"{rows} rows, {relievers} relievers, {mismatches} mismatches");

        Check("13-6c (1) fatigue: 0 at stress ≤ 60, +5 at 80, 10 at 100",
            ChildTraining.FatiguePp(0) == 0 && ChildTraining.FatiguePp(60) == 0 && ChildTraining.FatiguePp(80) == 5
            && ChildTraining.FatiguePp(100) == 10,
            $"{ChildTraining.FatiguePp(60)} / {ChildTraining.FatiguePp(80)} / {ChildTraining.FatiguePp(100)}");

        // Determinism: the same seed and inputs give the same outcomes, session by session.
        bool same = true;
        bool varied = false;
        for (int i = 0; i < 200; i++)
        {
            RngState a = ChildTraining.SessionStream(0x136C_5EEDUL, "child-a", 7L * i, i % 3);
            RngState b = ChildTraining.SessionStream(0x136C_5EEDUL, "child-a", 7L * i, i % 3);
            TrainingSessionOutcome oa = ChildTraining.Resolve(ref a, TrainingIntensity.Normal, 70, 60);
            TrainingSessionOutcome ob = ChildTraining.Resolve(ref b, TrainingIntensity.Normal, 70, 60);
            same &= oa.Success == ob.Success && oa.Roll == ob.Roll && oa.CoachingBp == ob.CoachingBp && oa.CareBp == ob.CareBp;
            varied |= !oa.Success;
        }
        Check("13-6c (1) determinism: the same seed and inputs give the same roll and outcome for 200 sessions (and the rolls vary)",
            same && varied);

        bool monotone = true;
        bool floorHolds = true;
        bool zeroAtCap = true;
        foreach (TrainingIntensity intensity in new[] { TrainingIntensity.Easy, TrainingIntensity.Normal, TrainingIntensity.Hard })
        {
            int previous = int.MaxValue;
            for (int c = 0; c <= 99; c++)
            {
                int gain = ChildTraining.CoachingGainBp(intensity, c);
                monotone &= gain <= previous;
                floorHolds &= gain >= 1;
                previous = gain;
            }
            zeroAtCap &= ChildTraining.CoachingGainBp(intensity, 100) == 0;
        }
        Check("13-6c (1) diminishing returns: the gain never rises with coaching; the max(1, …) floor gives ≥ 1 bp at every coaching 0–99 (no Easy-76 / Normal-88 / Hard-94 stall); 0 at 100; Easy/Normal/Hard full gains 5/9/15 bp",
            monotone && floorHolds && zeroAtCap
            && ChildTraining.CoachingGainBp(TrainingIntensity.Easy, 50) == 5
            && ChildTraining.CoachingGainBp(TrainingIntensity.Normal, 50) == 9
            && ChildTraining.CoachingGainBp(TrainingIntensity.Hard, 40) == 15
            && ChildTraining.CoachingGainBp(TrainingIntensity.Easy, 76) == 2
            && ChildTraining.CoachingGainBp(TrainingIntensity.Normal, 99) == 1);

        bool easyNeverLowers = ChildTraining.SuccessCareBp(TrainingIntensity.Easy) > 0
            && ChildTraining.FailureCareBp(TrainingIntensity.Easy) == 0
            && ChildTraining.FailureCareBp(TrainingIntensity.Normal) == 0
            && ChildTraining.FailureCareBp(TrainingIntensity.Hard) == -25
            && ChildTraining.SuccessCareBp(TrainingIntensity.Normal) == 12
            && ChildTraining.SuccessCareBp(TrainingIntensity.Hard) == 0;
        Check("13-6c (1) outcomes: Easy never lowers an axis (+18 care on a success, nothing on a failure); Normal +12 / nothing; Hard 0 / −25 care (burnout, never an injury)",
            easyNeverLowers);
    }

    // ------------------------------------------------------------------
    // (2) the carry, the cap, the fade
    // ------------------------------------------------------------------

    private static void RunTrainingCarryChecks()
    {
        int p250 = 250, m250 = -250, p99 = 99, m99 = -99, p100 = 100;
        int w1 = ChildTraining.FlushWholePoints(ref p250);
        int w2 = ChildTraining.FlushWholePoints(ref m250);
        int w3 = ChildTraining.FlushWholePoints(ref p99);
        int w4 = ChildTraining.FlushWholePoints(ref m99);
        int w5 = ChildTraining.FlushWholePoints(ref p100);
        bool truncates = w1 == 2 && p250 == 50 && w2 == -2 && m250 == -50 && w3 == 0 && p99 == 99 && w4 == 0 && m99 == -99
            && w5 == 1 && p100 == 0;
        var rng = new RngState(0x136C_CA22UL);
        int carry = 0;
        long moved = 0;
        long added = 0;
        bool inside = true;
        for (int i = 0; i < 20_000; i++)
        {
            int delta = rng.NextInt(61) - 30;
            carry += delta;
            added += delta;
            moved += ChildTraining.FlushWholePoints(ref carry);
            inside &= carry > -100 && carry < 100;
        }
        Check("13-6c (2) carry: a flush moves whole points truncating toward zero (250 → 2 r 50; −250 → −2 r −50; ±99 → 0) and 20,000 signed deltas keep the carry within (−100, +100) with nothing lost",
            truncates && inside && moved * 100 + carry == added, $"carry {carry}, moved {moved}");

        var plan = TrainingPlan.Default(isPitcher: false);
        int total = 0;
        for (int i = 0; i < 200; i++)
        {
            total += ChildTraining.AddCoachingGain(ref plan, 15, 2030);
        }
        bool capped = total == ChildTraining.TrainingProfile.SeasonCoachingCapBp && plan.SeasonCoachBp == 600;
        int nextSeason = ChildTraining.AddCoachingGain(ref plan, 15, 2031);
        Check("13-6c (2) season cap: 200 Hard gains in one season stop at exactly 600 bp (+6 coaching); a new seasonYear resets it",
            capped && nextSeason == 15 && plan.SeasonCoachBp == 15 && plan.SeasonYear == 2031,
            $"season total {total}, next season {nextSeason}");

        var fade = TrainingPlan.Default(false);
        bool neutral = ChildTraining.ApplyIdleFade(ref fade, 50) == 0 && fade.CoachBp == 0;
        var below = TrainingPlan.Default(false);
        bool belowStays = ChildTraining.ApplyIdleFade(ref below, 44) == 0 && below.CoachBp == 0;
        var near = TrainingPlan.Default(false);
        near.CoachBp = 3;
        int nearDelta = ChildTraining.ApplyIdleFade(ref near, 50);
        int coaching = 53;
        var slide = TrainingPlan.Default(false);
        bool neverBelow = true;
        for (int week = 0; week < 600; week++)
        {
            ChildTraining.ApplyIdleFade(ref slide, coaching);
            coaching += ChildTraining.FlushWholePoints(ref slide.CoachBp);
            neverBelow &= coaching * 100 + slide.CoachBp >= 5000;
        }
        Check("13-6c (2) fade: −6 bp a week, never below 50 (no fade at 50 with a zero carry; 50 + 3 bp fades to exactly 50; a child below 50 isn't raised; 600 idle weeks from 53 end at exactly 50)",
            neutral && belowStays && nearDelta == -3 && near.CoachBp == 0 && neverBelow && coaching == 50 && slide.CoachBp == 0,
            $"after 600 weeks: {coaching} r {slide.CoachBp}");

        var round = new TrainingPlan
        {
            Focus = TrainingFocus.Fielding, Intensity = TrainingIntensity.Hard, CoachBp = -42, CareBp = 77,
            SeasonYear = 2031, SeasonCoachBp = 345, LastSessionWeekStart = 1092,
        };
        TrainingPlan parsed = TrainingPlan.Parse(round.Format());
        bool corruptThrows = false;
        try
        {
            TrainingPlan.Parse("hit:normal:1:2:3");
        }
        catch (FormatException)
        {
            corruptThrows = true;
        }
        Check("13-6c (2) the training_plan record round-trips (signed carries, the cap's season, the last week) and a malformed value throws instead of resetting",
            parsed.Focus == round.Focus && parsed.Intensity == round.Intensity && parsed.CoachBp == -42 && parsed.CareBp == 77
            && parsed.SeasonYear == 2031 && parsed.SeasonCoachBp == 345 && parsed.LastSessionWeekStart == 1092 && corruptThrows,
            round.Format());
    }

    // ------------------------------------------------------------------
    // (3) the streams
    // ------------------------------------------------------------------

    private static void RunTrainingStreamChecks(string repoRoot)
    {
        const ulong seed = 0x136C_5EED_0000_0001UL;
        var firsts = new HashSet<ulong>();
        int streams = 0;
        for (long day = 1; day <= 12L * GlobalState.DaysPerSeason; day++)
        {
            for (int session = 0; session < 3; session++)
            {
                RngState s = ChildTraining.SessionStream(seed, "a-child-id", day, session);
                firsts.Add(s.NextUInt64());
                streams++;
            }
        }
        var tips = new HashSet<ulong>();
        for (long day = 7; day <= 12L * GlobalState.DaysPerSeason; day += 7)
        {
            RngState t = ChildTraining.TipStream(seed, day);
            tips.Add(t.NextUInt64());
        }
        Check("13-6c (3) streams: one child over 12 seasons × every day × 3 sessions gives all-distinct SessionStream draws, and every weekly tip stream is distinct",
            firsts.Count == streams && streams == 13_140 && tips.Count == 625, $"{firsts.Count}/{streams} sessions, {tips.Count} tip weeks");

        Check("13-6c (3) HashId64 is FNV-1a 64 over UTF-16 code units: the pinned vectors \"\" → CBF29CE484222325, \"a\" → AF63DC4C8601EC8C, \"foobar\" → 85944171F73967E8",
            ChildTraining.HashId64("") == 0xCBF29CE484222325UL && ChildTraining.HashId64("a") == 0xAF63DC4C8601EC8CUL
            && ChildTraining.HashId64("foobar") == 0x85944171F73967E8UL,
            $"{ChildTraining.HashId64("a"):X16} {ChildTraining.HashId64("foobar"):X16}");

        ulong[] shipped =
        {
            PostseasonStreams.PostMacroGameTag, PostseasonStreams.PostMicroGameTag, PostseasonStreams.StandingsCoinTag,
            PostseasonStreams.RankingCoinTag, PostseasonStreams.Subsample14Tag, PipelineProfile.OutsideRecruitsTag,
            PipelineProfile.OutsideRecruitsFieldingTag, PipelineProfile.HsIntakeTag, PipelineProfile.HsIntakeFieldingTag,
            PipelineProfile.InauguralDraftOrderTag,
        };
        bool distinct = ChildTraining.TrainingStreamTag != ChildTraining.NightTipsStreamTag
            && Array.IndexOf(shipped, ChildTraining.TrainingStreamTag) < 0
            && Array.IndexOf(shipped, ChildTraining.NightTipsStreamTag) < 0
            && Ascii(ChildTraining.TrainingStreamTag) == "CHLDTRNG" && Ascii(ChildTraining.NightTipsStreamTag) == "NGHTTIPS";
        Check("13-6c (3) tags: CHLDTRNG and NGHTTIPS decode as their names and differ from each other and from the five PostseasonStreams and five PipelineProfile tags",
            distinct, $"{Ascii(ChildTraining.TrainingStreamTag)} / {Ascii(ChildTraining.NightTipsStreamTag)}");

        var offenders = new List<string>();
        foreach (string rel in new[]
            {
                "Assets/Economy/Family/ParentJobService.cs", "Assets/Economy/Family/ChildTrainingService.cs",
                "Assets/Simulation/Baseball/ChildTraining.cs",
            })
        {
            string text = File.ReadAllText(Path.Combine(repoRoot, rel));
            if (text.Contains("GetHashCode", StringComparison.Ordinal))
            {
                offenders.Add(rel);
            }
        }
        Check("13-6c (3) no GetHashCode in either service or the rules (per-process in .NET; the streams use HashId64)",
            offenders.Count == 0, string.Join(", ", offenders));
    }

    private static string Ascii(ulong tag)
    {
        var chars = new char[8];
        for (int i = 0; i < 8; i++)
        {
            chars[i] = (char)((tag >> (8 * (7 - i))) & 0xFF);
        }
        return new string(chars);
    }

    // ------------------------------------------------------------------
    // (4) the identity and (5) the fold
    // ------------------------------------------------------------------

    /// <summary>The 13-6c services on a PWorld: the release opens the card (when asked), and every day in the phase runs the job's step then the week's training, GameManager's order.</summary>
    private sealed class ParentPhaseServices
    {
        public readonly ParentJobService Jobs;
        public readonly ChildTrainingService Training;

        public ParentPhaseServices(PWorld w, string repoRoot, bool openCardOnRelease)
        {
            var persons = new PersonQueries(w.Db);
            ParentJobCatalog catalog = ParentJobCatalog.Parse(
                File.ReadAllText(Path.Combine(repoRoot, "Assets", "Data", "Family", "parent_jobs.json")));
            Jobs = new ParentJobService(w.Db, w.Players, persons, w.Baseball, w.GameState, catalog, w.Bus)
            {
                SeedOverride = 0x136C_0000_5EEDUL,
            };
            Training = new ChildTrainingService(w.Db, w.Players, persons, w.Baseball, w.GameState, w.State, Jobs);
            if (openCardOnRelease)
            {
                w.Bus.Subscribe<AvatarReleasedEvent>(e =>
                {
                    if (e.EnteredParentPhase && w.Career!.InParentPhase && w.Career.AvatarPlayerId == e.PlayerId)
                    {
                        Jobs.OpenJobCard(e.PlayerId, w.State.CurrentDay);
                    }
                });
            }
            w.Bus.Subscribe<DayAdvancedEvent>(e =>
            {
                if (w.Career is { HasAvatar: true, InParentPhase: true } career)
                {
                    Jobs.ProcessDay(career.AvatarPlayerId, e.Day, 0f);
                    Training.ProcessDay(career.AvatarPlayerId, e.Day, 0f);
                }
            });
        }
    }

    private static void RunParentPhaseIdentityAndFoldChecks(string schemaPath, string repoRoot)
    {
        const int Spread = LeagueGenerator.DefaultRatingSpread;
        string[] identityDump =
        {
            "SELECT * FROM Players ORDER BY player_id;",
            "SELECT * FROM Player_Potential ORDER BY player_id;",
            "SELECT * FROM Child_Development ORDER BY child_id;",
            "SELECT * FROM Game_State ORDER BY key;",
            "SELECT player_id, flag_name, is_active, set_on_day FROM Entity_Flags ORDER BY player_id, flag_name;",
        };

        // A released senior with a 13-year-old (a seeded, non-neutral Child_Development
        // row so the fold moves something) through the release and the takeover at 16.
        (string Dump, string Parent, string Child, bool TookOver, long PlanKeys) Run(string tag, bool services, int natureInterest,
            Action<PWorld, ParentPhaseServices?, string, string>? beforeTakeover)
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, tag), career: true, startDay: 300,
                development: false, simTiers: new[] { LeagueTier.HS, LeagueTier.College });
            string parent = w.Avatar(201, 15, 23, classYear: 4);
            string child = w.Career!.ConceiveChild("Kid", "Line", birthAge: 13);
            w.Players.SetBaseballInterest(child, natureInterest);
            var persons = new PersonQueries(w.Db);
            persons.UpsertChild(new ChildDevelopmentRow { ChildId = child, Care = 70, Coaching = 60, Funding = 50, Neglect = 0, LastTickDay = 1 });
            w.Career.Persons = persons; // the fold reads Child_Development through it
            ParentPhaseServices? svc = services ? new ParentPhaseServices(w, repoRoot, openCardOnRelease: beforeTakeover is not null) : null;
            w.AdvanceTo(NewSeasonDay(3) - 1);
            beforeTakeover?.Invoke(w, svc, parent, child);
            w.AdvanceTo(NewSeasonDay(3));
            bool tookOver = w.Career.AvatarPlayerId == child && w.TeamOf(child) == 101;
            return (DumpQueries(w.Db, identityDump), parent, child, tookOver,
                CountRows(w.Db, "SELECT COUNT(*) FROM Game_State WHERE key LIKE 'training_plan:%' OR key = 'parent_phase_seed';"));
        }

        var bare = Run("pp_bare", services: false, natureInterest: 100, beforeTakeover: null);
        var inert = Run("pp_inert", services: true, natureInterest: 100, beforeTakeover: null);
        Check("13-6c (4) identity: with the services attached but no job and no session, a parent phase through the takeover fold leaves Players, Player_Potential, Child_Development, Game_State and Entity_Flags byte-identical to a world without them, and writes no training_plan:* or seed key",
            bare.TookOver && inert.TookOver && bare.Dump == inert.Dump && inert.PlanKeys == 0,
            $"took over {bare.TookOver}/{inert.TookOver}, dump {Sha12(bare.Dump)} vs {Sha12(inert.Dump)}, keys {inert.PlanKeys}");

        // (5) Coach from the release, autopilot Normal for the two trainable seasons (ages 14–15).
        ChildDevelopmentRow devBefore = default;
        string deadlineJob = "";
        var trained = Run("pp_trained", services: true, natureInterest: 60, beforeTakeover: (w, svc, parent, child) =>
        {
            new PersonQueries(w.Db).TryGetChild(child, out devBefore);
            deadlineJob = svc!.Jobs.CurrentJobId ?? "";
        });
        // The card opened at the release and resolved at its deadline (Steady); a second
        // world answers it with Coach on the first day of the phase.
        ChildDevelopmentRow coachDev = default;
        PlayerPotentialRow coachNature = default;
        int coachInterest = 0;
        PlayerPotentialRow coachAfter = default;
        int coachInterestAfter = 0;
        string coachJob = "";
        {
            using var w = new PWorld(schemaPath, Slice136Copy(schemaPath, Spread, "pp_coach"), career: true, startDay: 300,
                development: false, simTiers: new[] { LeagueTier.HS, LeagueTier.College });
            string parent = w.Avatar(201, 15, 23, classYear: 4);
            string child = w.Career!.ConceiveChild("Kid", "Line", birthAge: 13);
            w.Players.SetBaseballInterest(child, 60);
            var persons = new PersonQueries(w.Db);
            persons.UpsertChild(new ChildDevelopmentRow { ChildId = child, Care = 70, Coaching = 60, Funding = 50, Neglect = 0, LastTickDay = 1 });
            w.Career.Persons = persons;
            var svc = new ParentPhaseServices(w, repoRoot, openCardOnRelease: true);
            w.AdvanceTo(NewSeasonDay(1));
            svc.Jobs.ResolveJobChoice(parent, "coach", w.State.CurrentDay);
            coachJob = svc.Jobs.CurrentJobId ?? "";
            w.AdvanceTo(NewSeasonDay(3) - 1);
            persons.TryGetChild(child, out coachDev);
            w.Baseball.TryGetPotential(child, out coachNature);
            w.Players.TryGetById(child, out PlayerRow row);
            coachInterest = row.BaseballInterest;
            w.AdvanceTo(NewSeasonDay(3));
            w.Baseball.TryGetPotential(child, out coachAfter);
            w.Players.TryGetById(child, out PlayerRow after);
            coachInterestAfter = after.BaseballInterest;
        }
        int k = NurtureBlend.PotentialDelta(in coachDev);
        int j = NurtureBlend.InterestDelta(in coachDev);
        PlayerPotentialRow expected = NurtureBlend.ApplyToPotential(in coachNature, k);
        bool potentialExact = coachAfter.BatPower == expected.BatPower && coachAfter.BatContact == expected.BatContact
            && coachAfter.BatDiscipline == expected.BatDiscipline && coachAfter.PitStuff == expected.PitStuff
            && coachAfter.PitControl == expected.PitControl && coachAfter.PitStamina == expected.PitStamina
            && coachAfter.Fielding == expected.Fielding;
        Check("13-6c (5) the fold: a Coach-trained child (two autopilot Normal seasons from coaching 60) takes over with potential + k and interest + j, exactly NurtureBlend's numbers from the trained axes (k > 0, coaching rose)",
            coachJob == "coach" && coachDev.Coaching > 60 && k > 0 && potentialExact
            && coachInterestAfter == NurtureBlend.FinalInterest(coachInterest, in coachDev),
            $"coaching {coachDev.Coaching}, care {coachDev.Care}; k {k}, j {j}; interest {coachInterest}→{coachInterestAfter}");
        Check("13-6c (5) …and with the card unanswered, the release's card resolves to Steady at its deadline (D2-A), which trains on autopilot too (2 sessions a week): coaching rose before the takeover",
            trained.TookOver && deadlineJob == "steady" && devBefore.Coaching > 60 && trained.PlanKeys > 0,
            $"job '{deadlineJob}', coaching {devBefore.Coaching}, keys {trained.PlanKeys}");
    }

    // ------------------------------------------------------------------
    // (6) source scans
    // ------------------------------------------------------------------

    private static void RunParentPhaseSourceScans(string repoRoot)
    {
        string jobs = File.ReadAllText(Path.Combine(repoRoot, "Assets", "Economy", "Family", "ParentJobService.cs"));
        string training = File.ReadAllText(Path.Combine(repoRoot, "Assets", "Economy", "Family", "ChildTrainingService.cs"));
        string rules = File.ReadAllText(Path.Combine(repoRoot, "Assets", "Simulation", "Baseball", "ChildTraining.cs"));
        string[] secret = { "Biological_Parentage", "LineageQueries", "IsKin" };
        bool blind = true;
        foreach (string text in new[] { jobs, training, rules })
        {
            foreach (string token in secret)
            {
                blind &= !text.Contains(token, StringComparison.Ordinal);
            }
        }
        Check("13-6c (6) secrecy: the trainable list reads PlayerQueries.LoadChildrenOf (Child edges) and neither service nor the rules reads Biological_Parentage or LineageQueries",
            blind && training.Contains("_players.LoadChildrenOf(parentId", StringComparison.Ordinal)
            && jobs.Contains("_players.LoadChildrenOf(parentId", StringComparison.Ordinal));

        string uiDir = Path.Combine(repoRoot, "Assets", "UI");
        string[] drill = Directory.Exists(uiDir) ? Directory.GetFiles(uiDir, "BackyardDrill*.cs") : Array.Empty<string>();
        bool drillClean = drill.All(f => !File.ReadAllText(f).Contains("RngState", StringComparison.Ordinal));
        Check("13-6c (6) the drill scene never references RngState (vacuous until 13-6d builds Assets/UI/BackyardDrill.cs)",
            drillClean, drill.Length == 0 ? "not built yet (13-6d)" : $"{drill.Length} file(s)");

        string game = File.ReadAllText(Path.Combine(repoRoot, "Assets", "Core", "GameManager.cs"));
        int draws = CountOccurrences(game, "rng.NextUInt64()");
        int splits = CountOccurrences(game, "rng.Split()");
        bool servicesDrawNothing = !jobs.Contains("NextUInt64", StringComparison.Ordinal)
            && !training.Contains("NextUInt64", StringComparison.Ordinal)
            && !jobs.Contains(".Split()", StringComparison.Ordinal) && !training.Contains(".Split()", StringComparison.Ordinal);
        Check("13-6c (6) no new boot draw: GameManager still has the fb15873 boot chain's 4 rng.NextUInt64() and 2 rng.Split() calls, and neither service draws from a sim stream",
            draws == 4 && splits == 2 && servicesDrawNothing, $"NextUInt64 {draws}, Split {splits}");
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        for (int at = text.IndexOf(token, StringComparison.Ordinal); at >= 0; at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    // ------------------------------------------------------------------
    // (8) the lineage-failure rule without the pipeline
    // ------------------------------------------------------------------

    private static void RunParentPhaseLineageFailureCheck(string schemaPath)
    {
        string path = Slice136Copy(schemaPath, LeagueGenerator.DefaultRatingSpread, "pp_lineage");
        using var db = new DatabaseManager(path);
        InitializeIfFresh(db, schemaPath);
        var players = new PlayerQueries(db);
        var baseball = new BaseballQueries(db);
        var gameState = new GameStateQueries(db);
        var state = new GlobalState();
        var bus = new EventBus();
        var clock = new TimeManager(db, gameState, state, bus);
        clock.Initialize(StartYear);
        var leagues = new LeagueDirectory();
        var pro = new LeagueSimulator(db, baseball, new StatsNormalizer(db, baseball), new RngState(SeasonSeed), LeagueTier.Pro);
        pro.Initialize();
        leagues.Register(pro);
        var micro = new MicroGame(db, baseball) { LoggingEnabled = false };
        micro.Initialize();
        var career = new CareerManager(db, players, baseball, gameState, state, leagues, micro, new RngState(0x136C_1F00UL));
        career.CreateAvatar("No", "Pipeline", ProTeam(5), new PlayerRatingsRow
        {
            BatPower = 55, BatContact = 55, BatDiscipline = 55, PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
        });
        string avatarId = career.AvatarPlayerId;
        players.SetAge(avatarId, 41);
        string child = career.ConceiveChild("Young", "Kid", birthAge: 9);
        players.SetBaseballInterest(child, 100);
        bool optInDefault = career.EnableParentPhaseOnRetirement;
        career.AttachTo(bus);
        bus.Publish(new SeasonRolledOverEvent(StartYear, StartYear + 1));
        bus.DispatchPending();
        gameState.TryGetText(GameStateKeys.LineageOverReason, out string reason);
        Check("13-6c (8) without the pipeline the opt-in is off (default false): a 42-year-old with only a willing 10-year-old still ends NoPlayableHeir at the rollover, persisted, nothing deferred, still rostered",
            !optInDefault && !career.EnableParentPhaseOnRetirement && career.IsLineageOver
            && career.LineageOverReason == LineageFailure.NoPlayableHeir && reason == "NoPlayableHeir"
            && !career.RetirementReleasePending && !career.InParentPhase && career.IsAvatarRostered,
            $"reason {reason}, opt-in {optInDefault}");
    }
}
