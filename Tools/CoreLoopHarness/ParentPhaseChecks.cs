using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Data.Family;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Life;

namespace DirtAndDiamonds.Tools.CoreLoopHarness;

/// <summary>
/// Slice 13-6c CoreLoop fixtures (brief §5): the parent phase's real day loop
/// — the Life sim, the three Family services on the 7-day tick and the career —
/// with GameManager's thin parent-phase glue re-composed headless (GameManager
/// is the one Godot-facing Core file; LiveWorld re-composes its boot order the
/// same way). <see cref="ParentWorld"/>'s glue methods mirror, line for line,
/// GameManager.QueueStandingParentPlan, SubmitDaySchedule's shift-day forcing,
/// OnChildRearingDayAdvanced, the three parent-phase handlers and AdvanceUntil.
/// </summary>
internal static partial class Program
{
    private const int ParentStartYear = 2026;

    /// <summary>A released parent in the parent phase with the real day loop (no tier sims attached: nothing to play).</summary>
    private sealed class ParentWorld : IDisposable
    {
        public readonly DatabaseManager Db;
        public readonly PlayerQueries Players;
        public readonly BaseballQueries Baseball;
        public readonly PersonQueries Persons;
        public readonly GameStateQueries GameState;
        public readonly GlobalState State = new();
        public readonly EventBus Bus = new();
        public readonly TimeManager Clock;
        public readonly LeagueDirectory Leagues = new();
        public readonly MicroGame Micro;
        public readonly CareerManager Career;
        public readonly LifeSimManager Life = new();
        public readonly ChildRearingService ChildRearing;
        public readonly ParentJobService Jobs;
        public readonly ChildTrainingService Training;
        public readonly string ParentId;

        // GameManager's glue state.
        public bool StandingPlanQueued;
        private bool _plannedHustle; // _plannedWorkActivity != LegalWork
        private bool _plannedWorkHadHours;

        // Observations.
        public readonly List<(long Day, int Work, int Family, bool Hustle)> TickedPlans = new();
        public readonly List<(long Day, double Delta)> LifeFundsByDay = new();
        public readonly List<(long Day, float Hours)> RearingHours = new();
        public readonly List<FundsImpulseEvent> FundsImpulses = new();
        public int HustleSessionsArmed;

        public ParentWorld(string schemaPath, string path, long startDay, double startFunds = 5000.0,
            (int Age, int Interest)[]? children = null, int weeklyFunding = 50, bool attachCareer = true)
        {
            Db = new DatabaseManager(path);
            Db.InitializeSchema(schemaPath);
            Players = new PlayerQueries(Db);
            Baseball = new BaseballQueries(Db);
            Persons = new PersonQueries(Db);
            GameState = new GameStateQueries(Db);
            var genRng = new RngState(0x136C_6E4EUL);
            bool fresh = LeagueGenerator.GenerateIfEmpty(Db, Players, Baseball, ratingSpread: 0, ref genRng);
            if (fresh)
            {
                Db.RunInBatch(() =>
                {
                    GameState.SetInt64(GameStateKeys.CurrentDay, startDay);
                    GameState.SetInt64(GameStateKeys.StartSeasonYear, ParentStartYear);
                });
            }
            Clock = new TimeManager(Db, GameState, State, Bus);
            Clock.Initialize(ParentStartYear);
            var pro = new LeagueSimulator(Db, Baseball, new StatsNormalizer(Db, Baseball), new RngState(0x136C_0001UL), LeagueTier.Pro);
            pro.Initialize();
            Leagues.Register(pro);
            Micro = new MicroGame(Db, Baseball) { LoggingEnabled = false };
            Micro.Initialize();
            Career = new CareerManager(Db, Players, Baseball, GameState, State, Leagues, Micro, new RngState(0x136C_CA2EUL));
            if (fresh)
            {
                // A retired Pro bat (peak 180: +5 pp expertise), released into the parent phase the way the pipeline does it.
                Career.CreateAvatar("Parent", "Phase", LeagueStructure.TeamIdBase(LeagueTier.Pro) + 3, new PlayerRatingsRow
                {
                    BatPower = 60, BatContact = 60, BatDiscipline = 60, PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
                });
                string parent = Career.AvatarPlayerId;
                Players.SetAge(parent, 30);
                foreach ((int age, int interest) in children ?? new[] { (8, 70) })
                {
                    string child = Career.ConceiveChild("Kid", "Phase", birthAge: age);
                    Players.SetBaseballInterest(child, interest);
                }
                Players.AdjustFunds(parent, startFunds - (Players.TryGetById(parent, out PlayerRow p0) ? p0.Funds : 0.0));
                Persons.UpsertChildRearingCommitment(new ChildRearingCommitmentRow { PlayerId = parent, WeeklyFunding = weeklyFunding });
                Db.RunInBatch(() =>
                {
                    Players.SetTeam(parent, null);
                    Players.SetFlag(parent, PipelineFlags.Retired, true, startDay);
                    Career.WriteReleaseStateInBatch(hasChildren: true, startDay);
                });
                Career.ApplyReleaseState(hasChildren: true);
                Career.ReactivateAvatar();
            }
            else
            {
                Career.LoadExistingAvatar();
            }
            ParentId = Career.AvatarPlayerId;

            // The Life sim (GameManager.SyncLifeSimAvatar's projection for an unrostered parent).
            Players.TryGetById(ParentId, out PlayerRow row);
            Life.Seed(new[] { new NpcSeed(ParentId, row.Funds) });
            Life.SetAvatar(ParentId);
            Life.SetPersonStats(ParentId, new PersonStats { Happiness = 60f });
            Life.AvatarBoardWealthTier = -1;
            Life.AvatarSchoolAvailable = false;

            ChildRearing = new ChildRearingService(Db, Players, Persons, Bus);
            ParentJobCatalog catalog = ParentJobCatalog.Parse(File.ReadAllText(
                Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(schemaPath)))!, "Data", "Family", "parent_jobs.json")));
            Jobs = new ParentJobService(Db, Players, Persons, Baseball, GameState, catalog, Bus) { SeedOverride = 0x136C_7E57_5EEDUL };
            Training = new ChildTrainingService(Db, Players, Persons, Baseball, GameState, State, Jobs);

            // GameManager's subscription order: the observer of the plan about to tick, LifeSim,
            // the persistence flush, the hustle seam, child rearing, the career, then the
            // parent-phase handlers (after the rollover's career/pipeline, after LifeSim's tick).
            Bus.Subscribe<DayAdvancedEvent>(e =>
            {
                if (Life.TryGetTodaySchedule(out DaySchedule plan))
                {
                    TickedPlans.Add((e.Day, plan.WorkHours, plan.FamilyHours, Life.AvatarWorkIsHustle));
                }
            });
            Life.AttachTo(Bus);
            Bus.Subscribe<DayAdvancedEvent>(e =>
            {
                double delta = Life.PeekFundsDelta(ParentId);
                LifeFundsByDay.Add((e.Day, delta));
                if (delta != 0.0)
                {
                    Db.RunInBatch(() => Players.AdjustFunds(ParentId, delta));
                    Life.ClearFundsDelta(ParentId);
                }
            });
            Bus.Subscribe<DayAdvancedEvent>(_ =>
            {
                if (_plannedHustle && _plannedWorkHadHours)
                {
                    HustleSessionsArmed++;
                }
                _plannedHustle = false;
                _plannedWorkHadHours = false;
            });
            Bus.Subscribe<DayAdvancedEvent>(OnChildRearingDayAdvanced);
            Bus.Subscribe<FundsImpulseEvent>(e => FundsImpulses.Add(e));
            if (attachCareer)
            {
                Career.AttachTo(Bus);
            }
            Bus.Subscribe<AvatarReleasedEvent>(OnParentPhaseReleased);
            Bus.Subscribe<SeasonRolledOverEvent>(OnParentPhaseRolledOver);
            Bus.Subscribe<AvatarChangedEvent>(OnParentPhaseAvatarChanged);
            Bus.Subscribe<DayAdvancedEvent>(OnParentPhaseDayAdvanced);
            QueueStandingParentPlan(); // GameManager queues it at boot
        }

        public long Day => State.CurrentDay;

        public double LifeFunds => Life.TryGetFunds(ParentId, out double f) ? f : 0.0;

        public double DbFunds => Players.TryGetById(ParentId, out PlayerRow r) ? r.Funds : 0.0;

        public List<PlayerRow> Children()
        {
            var rows = new List<PlayerRow>();
            Players.LoadChildrenOf(ParentId, rows);
            return rows;
        }

        // ---- GameManager's glue, re-composed ----

        public int ParentPhaseShiftHours(long day) => Career.HasAvatar && Career.InParentPhase ? Jobs.ShiftHoursOn(day) : 0;

        public void QueueStandingParentPlan()
        {
            if (!Career.HasAvatar || !Career.InParentPhase || Life.AvatarPlayerId is null || Life.HasTodaySchedule)
            {
                return;
            }
            long day = State.CurrentDay + 1;
            int workHours = Jobs.ShiftHoursOn(day);
            int familyHours = ParentJobService.StandingFamilyHoursOn(day);
            int travelHours = TravelTime.ComputeHours(0, 0, 0, workHours, 0, NpcActionId.Idle, Life.AvatarTransportHoursSaved, out _);
            Life.SetTodaySchedule(new DaySchedule(SleepProfile.OptimalHours, 0, 0, 0, workHours,
                familyHours: familyHours, travelHours: travelHours));
            Life.AvatarWorkIsHustle = workHours > 0;
            StandingPlanQueued = true;
        }

        /// <summary>GameManager.SubmitDaySchedule's parent-phase path (an unrostered parent: no School, free Practice/Game).</summary>
        public void SubmitDaySchedule(in DaySchedule schedule, bool hustleActivity)
        {
            int jobHours = ParentPhaseShiftHours(State.CurrentDay + 1);
            bool shiftDay = jobHours > 0;
            int workHours = shiftDay ? jobHours : schedule.WorkHours;
            if (shiftDay)
            {
                hustleActivity = false;
            }
            int travelHours = TravelTime.ComputeHours(0, schedule.PracticeHours, schedule.GameHours, workHours,
                schedule.FreeTimeHours, schedule.FreeTimeActivity, Life.AvatarTransportHoursSaved, out _);
            var forced = new DaySchedule(Math.Max(schedule.SleepHours, SleepProfile.MinPlannedSleepHours), 0,
                schedule.PracticeHours, schedule.GameHours, workHours, schedule.FreeTimeHours, schedule.FreeTimeActivity,
                schedule.FamilyHours, travelHours);
            Life.SetTodaySchedule(forced);
            Life.AvatarWorkIsHustle = shiftDay || hustleActivity;
            _plannedHustle = hustleActivity;
            _plannedWorkHadHours = forced.WorkHours > 0;
            StandingPlanQueued = false;
        }

        public void ChooseParentJob(string jobId)
        {
            Jobs.ResolveJobChoice(ParentId, jobId, State.CurrentDay);
            if (StandingPlanQueued)
            {
                Life.ClearTodaySchedule();
                StandingPlanQueued = false;
            }
            QueueStandingParentPlan();
        }

        private void OnChildRearingDayAdvanced(DayAdvancedEvent e)
        {
            if (!Career.HasAvatar)
            {
                return;
            }
            string avatarId = Career.AvatarPlayerId;
            float familyHours = Life.PeekFamilyHoursThisWeek(avatarId);
            ChildRearing.ProcessDay(avatarId, e.Day, familyHours);
            if (Career.InParentPhase)
            {
                ParentJobs(avatarId, e.Day, familyHours);
                Life.TryGetStress(avatarId, out float stress);
                Training.ProcessDay(avatarId, e.Day, stress);
            }
            if (e.Day % ChildRearingService.TickCadenceDays == 0)
            {
                RearingHours.Add((e.Day, familyHours));
                Life.ClearFamilyHoursThisWeek(avatarId);
            }
        }

        private void ParentJobs(string avatarId, long day, float familyHours) => Jobs.ProcessDay(avatarId, day, familyHours);

        private void OnParentPhaseDayAdvanced(DayAdvancedEvent e)
        {
            if (!Life.HasTodaySchedule)
            {
                StandingPlanQueued = false;
            }
            QueueStandingParentPlan();
        }

        private void OnParentPhaseReleased(AvatarReleasedEvent e)
        {
            if (!e.EnteredParentPhase || !Career.HasAvatar || !Career.InParentPhase
                || !string.Equals(e.PlayerId, Career.AvatarPlayerId, StringComparison.Ordinal))
            {
                return;
            }
            Jobs.OpenJobCard(e.PlayerId, State.CurrentDay);
            QueueStandingParentPlan();
        }

        private void OnParentPhaseRolledOver(SeasonRolledOverEvent e)
        {
            if (Career.HasAvatar && Career.InParentPhase && !Career.IsLineageOver)
            {
                Jobs.OpenJobCard(Career.AvatarPlayerId, State.CurrentDay);
            }
        }

        private void OnParentPhaseAvatarChanged(AvatarChangedEvent e)
        {
            StandingPlanQueued = false;
            if (e.TeamId != 0)
            {
                Jobs.ClearForTakeover(State.CurrentDay);
            }
        }

        /// <summary>GameManager.AdvanceUntil (no gritty dispatcher or parked game here: those probe inputs read false).</summary>
        public AdvanceSummary AdvanceUntil(long targetDay, Action? eachDay = null)
        {
            long startDay = State.CurrentDay;
            string avatarId = Career.AvatarPlayerId;
            double startFunds = LifeFunds;
            Life.TryGetStress(avatarId, out float startStress);
            AdvanceStopKind stop = AdvanceStopKind.TargetReached;
            while (State.CurrentDay < targetDay)
            {
                AdvanceStopKind blocked = AdvanceInterruptRules.BlockingStop(false, Career.HasPendingSuccessionChoice, Career.HasPendingGame);
                if (blocked != AdvanceStopKind.None)
                {
                    stop = blocked;
                    break;
                }
                double fundsBefore = LifeFunds;
                Life.TryGetStress(avatarId, out float stressBefore);
                bool cardBefore = Jobs.HasPendingJobChoice || Career.HasPendingSuccessionChoice;
                bool rolloverNext = State.DayOfSeason == GlobalState.DaysPerSeason;
                string trainableBefore = rolloverNext ? TrainableSignature(avatarId) : string.Empty;

                Clock.AdvanceDay();
                Bus.DispatchPending();
                eachDay?.Invoke();
                Bus.DispatchPending();

                bool avatarChanged = !Career.HasAvatar || !string.Equals(Career.AvatarPlayerId, avatarId, StringComparison.Ordinal);
                double fundsAfter = LifeFunds;
                Life.TryGetStress(avatarId, out float stressAfter);
                bool cardAfter = Jobs.HasPendingJobChoice || Career.HasPendingSuccessionChoice;
                bool windowChanged = rolloverNext && !avatarChanged
                    && !string.Equals(TrainableSignature(avatarId), trainableBefore, StringComparison.Ordinal);
                var probe = new AdvanceProbe(false, stressBefore, stressAfter, fundsBefore, fundsAfter,
                    Career.HasPendingGame, !cardBefore && cardAfter, windowChanged, avatarChanged);
                AdvanceStopKind hit = AdvanceInterruptRules.FirstStop(in probe, UtilityCalculator.StressOverrideThreshold);
                if (hit != AdvanceStopKind.None)
                {
                    stop = hit;
                    break;
                }
            }
            Life.TryGetStress(avatarId, out float endStress);
            return new AdvanceSummary(startDay, State.CurrentDay, stop, LifeFunds - startFunds, endStress - startStress);
        }

        private string TrainableSignature(string parentId)
        {
            if (!Career.InParentPhase)
            {
                return string.Empty;
            }
            var children = new List<PlayerRow>(4);
            Training.LoadTrainable(parentId, children);
            return string.Join('|', children.Select(c => c.PlayerId));
        }

        /// <summary>The next 7-day tick strictly after today.</summary>
        public long NextTick => (State.CurrentDay / 7 + 1) * 7;

        /// <summary>Setup: fast-forwards to <paramref name="target"/>, pressing on past any interrupt.</summary>
        public void RunTo(long target)
        {
            while (State.CurrentDay < target)
            {
                if (AdvanceUntil(target).DaysAdvanced == 0)
                {
                    throw new InvalidOperationException($"The fast-forward is blocked on day {State.CurrentDay}.");
                }
            }
        }

        public void Dispose() => Db.Dispose();
    }

    private static void RunParentPhaseChecks(string schemaPath, string basePath)
    {
        Console.WriteLine("--- 13-6c parent phase (job card, weekly wage, A3 standing plan, waiver, budget, reloads, economy, fast-forward, D7-A) ---");
        var paths = new List<string>();
        string NewPath(string tag)
        {
            string p = basePath + "." + tag + ".db";
            paths.Add(p);
            return p;
        }
        try
        {
            RunJobCardAndWageChecks(schemaPath, NewPath);
            RunStandingPlanChecks(schemaPath, NewPath);
            RunWaiverAndBudgetChecks(schemaPath, NewPath);
            RunParentReloadChecks(schemaPath, NewPath);
            RunParentEconomyChecks(schemaPath, NewPath);
            RunFastForwardChecks(schemaPath, NewPath);
            RunRetirementReleaseChecks(schemaPath, NewPath);
        }
        finally
        {
            foreach (string p in paths)
            {
                TryDelete(p);
                TryDelete(p + "-wal");
                TryDelete(p + "-shm");
            }
        }
    }

    // ------------------------------------------------------------------
    // The job card and the weekly wage
    // ------------------------------------------------------------------

    private static void RunJobCardAndWageChecks(string schemaPath, Func<string, string> newPath)
    {
        string path = newPath("card");
        long cardDay;
        double fundsBeforeReload;
        int paydays;
        {
            using var w = new ParentWorld(schemaPath, path, startDay: 50);
            w.Jobs.OpenJobCard(w.ParentId, w.Day); // GameManager.OnParentPhaseReleased
            cardDay = w.Day;
            w.RunTo(cardDay + ParentJobService.DecisionWindowDays - 1);
            bool openOnDay13 = w.Jobs.HasPendingJobChoice && w.Jobs.CurrentJobId is null
                && w.Jobs.TryGetPendingJobChoice(w.ParentId, out PendingJobChoice choice)
                && choice.DeadlineDay == cardDay + 14 && choice.Options.Count == 3 && choice.Options.All(o => o.Available);
            w.RunTo(cardDay + ParentJobService.DecisionWindowDays);
            var flags = new List<EntityFlagRow>();
            w.Players.LoadActiveFlags(w.ParentId, flags);
            Check("13-6c: the release's job card stays open 13 days (three jobs, all available) and an unanswered card resolves to Steady on day 14 (job_steady and parent_phase active, card closed)",
                openOnDay13 && w.Jobs.CurrentJobId == "steady" && !w.Jobs.HasPendingJobChoice
                && flags.Any(f => f.FlagName == "job_steady") && flags.Any(f => f.FlagName == ParentJobService.ParentPhaseFlag),
                $"card day {cardDay}, job {w.Jobs.CurrentJobId}");

            // Three paydays: the wage lands once a week, on the tick, as one +$1,000 impulse.
            long from = w.Day;
            w.RunTo(w.NextTick + 14);
            var wages = w.FundsImpulses.Where(f => f.Delta > 0).ToList();
            paydays = wages.Count;
            bool onTicks = wages.All(f => f.Delta == 1000.0);
            w.Jobs.TryGetLastSettlement(out JobSettlement last);
            Check("13-6c: Steady's $1,000 settles once a week on the 7-day tick (three ticks → three +$1,000 impulses, nothing between), and the settlement records its +10 stress",
                paydays == 3 && onTicks && last.Total == 1000 && last.StressDelta == 10 && last.JobId == "steady",
                $"days {from}→{w.Day}, wage impulses {paydays}");
            fundsBeforeReload = w.DbFunds;
        }
        {
            using var w = new ParentWorld(schemaPath, path, startDay: 50);
            double reloaded = w.DbFunds;
            int before = w.FundsImpulses.Count;
            w.RunTo(w.NextTick);
            int wagesAfter = w.FundsImpulses.Skip(before).Count(f => f.Delta == 1000.0);
            Check("13-6c: the wage survives a reload — the job, the funds row and the next payday (once) carry across a fresh boot",
                Math.Abs(reloaded - fundsBeforeReload) < 1e-6 && w.Jobs.CurrentJobId == "steady" && wagesAfter == 1,
                $"funds {fundsBeforeReload:F2} → {reloaded:F2}, next payday wages {wagesAfter}");
        }
    }

    // ------------------------------------------------------------------
    // A3: the standing plan, no double wage, no hustle
    // ------------------------------------------------------------------

    private static void RunStandingPlanChecks(string schemaPath, Func<string, string> newPath)
    {
        using var w = new ParentWorld(schemaPath, newPath("a3"), startDay: 63);
        w.Jobs.OpenJobCard(w.ParentId, w.Day);
        w.ChooseParentJob("steady");
        long tick = w.NextTick; // a full 7-day window after this one closes
        w.RunTo(tick);
        w.TickedPlans.Clear();
        w.LifeFundsByDay.Clear();
        w.RearingHours.Clear();
        string child = w.Children()[0].PlayerId;
        w.Persons.TryGetChild(child, out ChildDevelopmentRow before);
        AdvanceSummary week = w.AdvanceUntil(tick + 7);
        w.Persons.TryGetChild(child, out ChildDevelopmentRow after);

        bool everyDayPlanned = w.TickedPlans.Count == 7;
        bool shifts = w.TickedPlans.All(p => (w.Jobs.ShiftHoursOn(p.Day) == 8) == (p.Work == 8) && (p.Work == 0 || p.Hustle));
        int shiftDays = w.TickedPlans.Count(p => p.Work == 8);
        int familyHours = w.TickedPlans.Sum(p => p.Family);
        float rearing = w.RearingHours.Count == 1 ? w.RearingHours[0].Hours : -1f;
        bool noIncome = w.LifeFundsByDay.All(d => d.Delta <= 0.0);
        Check("13-6c A3: a fast-forwarded parent-phase week runs the standing plan every day — Steady's 8 h shifts on the shift days under the HustleWork drain, 1 family hour on days ≡ 1, 3, 5 (mod 7) — and ChildRearingService reads exactly 3 h (care and neglect at its neutral)",
            week.Stop == AdvanceStopKind.TargetReached && everyDayPlanned && shifts && shiftDays >= 4 && familyHours == 3
            && rearing == 3f && after.Neglect == Math.Max(0, before.Neglect - 1) && after.Funding == before.Funding,
            $"{w.TickedPlans.Count} planned, {shiftDays} shifts, family {familyHours} h, rearing read {rearing} h, neglect {before.Neglect}→{after.Neglect}");
        Check("13-6c A3: no LegalWork wage on a fast-forwarded shift day — the Life sim's funds never rise on any day of the week (the job's pay is the weekly settlement only)",
            noIncome, string.Join(" ", w.LifeFundsByDay.Select(d => $"{d.Day}:{d.Delta:F0}")));

        // A player-planned shift day: the job's hours are forced whatever Work and activity were sent.
        while (w.ParentPhaseShiftHours(w.Day + 1) == 0)
        {
            w.RunTo(w.Day + 1);
        }
        w.SubmitDaySchedule(new DaySchedule(8, 0, 0, 0, workHours: 2, familyHours: 2), hustleActivity: true);
        bool queuedForced = w.Life.TryGetTodaySchedule(out DaySchedule planned) && planned.WorkHours == 8
            && planned.FamilyHours == 2 && w.Life.AvatarWorkIsHustle && !w.StandingPlanQueued;
        w.LifeFundsByDay.Clear();
        int armedBefore = w.HustleSessionsArmed;
        w.RunTo(w.Day + 1);
        w.SubmitDaySchedule(new DaySchedule(8, 0, 0, 0, workHours: 6), hustleActivity: false);
        bool legalForced = w.Life.TryGetTodaySchedule(out DaySchedule legal) && (w.ParentPhaseShiftHours(w.Day + 1) == 0
            ? legal.WorkHours == 6 && !w.Life.AvatarWorkIsHustle
            : legal.WorkHours == 8 && w.Life.AvatarWorkIsHustle);
        bool plannedNoIncome = w.LifeFundsByDay.All(d => d.Delta <= 0.0);
        Check("13-6c A3: a planned shift day forces the job's 8 h whatever Work and activity were sent (2 h of a hustle → 8 h job, HustleWork drain), arms no hustle session, and credits no LegalWork wage",
            queuedForced && legalForced && plannedNoIncome && w.HustleSessionsArmed == armedBefore,
            $"forced {planned.WorkHours} h, armed {w.HustleSessionsArmed - armedBefore}");
    }

    // ------------------------------------------------------------------
    // Nightlife's waiver; the weekly budget and autopilot
    // ------------------------------------------------------------------

    private static void RunWaiverAndBudgetChecks(string schemaPath, Func<string, string> newPath)
    {
        {
            using var w = new ParentWorld(schemaPath, newPath("waiver"), startDay: 63);
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            w.ChooseParentJob("nightlife");
            w.RunTo(w.NextTick);
            // A planned week: 1 family hour every day of the tick window, shift days and off-days alike.
            long tick = w.NextTick;
            while (w.Day < tick)
            {
                w.SubmitDaySchedule(new DaySchedule(8, 0, 0, 0, 0, familyHours: 1), hustleActivity: false);
                w.RunTo(w.Day + 1);
            }
            w.Jobs.TryGetLastSettlement(out JobSettlement planned);
            float plannedHours = w.RearingHours[^1].Hours;
            w.RunTo(w.NextTick); // a standing-plan week: 3 h
            w.Jobs.TryGetLastSettlement(out JobSettlement standing);
            float standingHours = w.RearingHours[^1].Hours;
            Check("13-6c: Nightlife's care waiver reads the same family hours ChildRearingService read — a planned 7-hour week waives the −25 bp, the standing plan's 3-hour week doesn't",
                plannedHours >= 5f && planned.CareWaived && planned.CareBp == 0
                && standingHours == 3f && !standingSettlementWaived(standing),
                $"planned {plannedHours} h → waived {planned.CareWaived}; standing {standingHours} h → care {standing.CareBp} bp");

            static bool standingSettlementWaived(JobSettlement s) => s.CareWaived || s.CareBp != -25;
        }
        {
            using var w = new ParentWorld(schemaPath, newPath("budget"), startDay: 63,
                children: new[] { (9, 70) });
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            w.ChooseParentJob("coach");
            w.RunTo(w.NextTick);
            string child = w.Children()[0].PlayerId;
            TrainingBudget fresh = w.Training.GetBudget(w.Day);
            w.Life.TryGetStress(w.ParentId, out float stress);
            int expected = ChildTraining.OddsPp(TrainingIntensity.Normal, 10, w.Training.ExpertisePpFor(w.ParentId),
                15, ChildTraining.FatiguePp(stress));
            TrainingSessionResult drill = w.Training.PlayDrill(w.ParentId, child, 1.0, stress, w.Day);
            TrainingBudget spent = w.Training.GetBudget(w.Day);
            w.RunTo(w.NextTick);
            int autopilot = w.Training.LastAutopilotSessions;
            TrainingBudget reset = w.Training.GetBudget(w.Day);
            w.Persons.TryGetChild(child, out ChildDevelopmentRow dev1);
            string plan1 = w.GameState.TryGetText(GameStateKeys.TrainingPlan(child), out string t1) ? t1 : "";
            w.Training.ProcessDay(w.ParentId, w.Day, stress); // a second call on the same tick day
            w.Persons.TryGetChild(child, out ChildDevelopmentRow dev2);
            string plan2 = w.GameState.TryGetText(GameStateKeys.TrainingPlan(child), out string t2) ? t2 : "";
            Check("13-6c: Coach's budget is 3 a week; a drill spends one (+15 pp at a perfect score, +10 for Coach, the retired bat's peak-180 expertise), the week's end auto-resolves exactly the 2 unplayed sessions once (a repeat call moves nothing), and the budget resets to 3",
                fresh.Total == 3 && fresh.Used == 0 && spent.Used == 1 && drill.Outcome.OddsPp == expected
                && w.Training.ExpertisePpFor(w.ParentId) >= 5
                && autopilot == 2 && reset.Used == 0 && reset.Total == 3
                && dev1.Coaching == dev2.Coaching && dev1.Care == dev2.Care && plan1 == plan2 && plan1.Length > 0,
                $"odds {drill.Outcome.OddsPp} (expected {expected}), autopilot {autopilot}, budget {fresh.Remaining}/{spent.Remaining}/{reset.Remaining}");
        }
    }

    // ------------------------------------------------------------------
    // Reloads don't reroll a session or a tip week
    // ------------------------------------------------------------------

    private static void RunParentReloadChecks(string schemaPath, Func<string, string> newPath)
    {
        string path = newPath("reload");
        string fork = newPath("reload_fork");
        string child;
        {
            using var w = new ParentWorld(schemaPath, path, startDay: 63, children: new[] { (10, 70) });
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            w.ChooseParentJob("nightlife");
            w.RunTo(w.NextTick + 7); // two paydays: the parent-phase seed exists
            child = w.Children()[0].PlayerId;
        }
        // The save from before the drill (with any WAL sidecars the closed world left).
        File.Copy(path, fork, overwrite: true);
        foreach (string sidecar in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + sidecar))
            {
                File.Copy(path + sidecar, fork + sidecar, overwrite: true);
            }
        }
        TrainingSessionResult first;
        {
            using var w = new ParentWorld(schemaPath, path, startDay: 63);
            w.Life.TryGetStress(w.ParentId, out float stress);
            first = w.Training.PlayDrill(w.ParentId, child, 0.5, stress, w.Day);
        }
        int usedAfterReload;
        TrainingSessionResult second;
        int tipsA;
        {
            using var w = new ParentWorld(schemaPath, path, startDay: 63);
            usedAfterReload = w.Training.GetBudget(w.Day).Used;
            w.Life.TryGetStress(w.ParentId, out float stress);
            second = w.Training.PlayDrill(w.ParentId, child, 0.5, stress, w.Day);
            w.RunTo(w.NextTick);
            w.Jobs.TryGetLastSettlement(out JobSettlement s);
            tipsA = s.Tips;
        }
        TrainingSessionResult replay;
        int tipsB;
        {
            using var w = new ParentWorld(schemaPath, fork, startDay: 63);
            w.Life.TryGetStress(w.ParentId, out float stress);
            replay = w.Training.PlayDrill(w.ParentId, child, 0.5, stress, w.Day);
            w.RunTo(w.NextTick);
            w.Jobs.TryGetLastSettlement(out JobSettlement s);
            tipsB = s.Tips;
        }
        Check("13-6c: a reload doesn't reroll — the save from before a drill replays the same roll and outcome; after the drill's commit a reload sees the spent session (the next drill is the week's next ordinal); the same payday's tips are the same in both",
            replay.Outcome.Roll == first.Outcome.Roll && replay.Outcome.Success == first.Outcome.Success
            && replay.SessionIndex == first.SessionIndex && first.SessionIndex == 0
            && usedAfterReload == 1 && second.SessionIndex == 1 && second.WeekStartDay == first.WeekStartDay
            && tipsA == tipsB && tipsA >= 0 && tipsA <= 800,
            $"roll {first.Outcome.Roll}/{replay.Outcome.Roll}, tips {tipsA}/{tipsB}, after reload used {usedAfterReload}, next index {second.SessionIndex}");
    }

    // ------------------------------------------------------------------
    // The economy: 52 fast-forwarded weeks per job (Carbon's §9 Q1 in the real loop)
    // ------------------------------------------------------------------

    private static void RunParentEconomyChecks(string schemaPath, Func<string, string> newPath)
    {
        var nets = new Dictionary<string, (double Net, double Min, int Depleted, int Tips)>();
        foreach (string job in new[] { "coach", "steady", "nightlife" })
        {
            using var w = new ParentWorld(schemaPath, newPath("econ_" + job), startDay: 7, startFunds: 500.0,
                children: new[] { (6, 70) }, weeklyFunding: 50);
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            w.ChooseParentJob(job);
            double start = w.LifeFunds;
            long target = w.Day + 52 * 7;
            int depleted = 0;
            int tips = 0;
            double min = start;
            int tipWeeks = 0;
            while (w.Day < target)
            {
                AdvanceSummary run = w.AdvanceUntil(target, () =>
                {
                    min = Math.Min(min, w.LifeFunds);
                    if (w.Jobs.TryGetLastSettlement(out JobSettlement s) && s.Day == w.Day)
                    {
                        tips += s.Tips;
                        tipWeeks++;
                    }
                });
                depleted += run.Stop == AdvanceStopKind.FundsDepleted ? 1 : 0;
            }
            nets[job] = ((w.LifeFunds - start) / 52.0, min, depleted, tips);
        }
        (double coachNet, double coachMin, int coachDep, _) = nets["coach"];
        (double steadyNet, double steadyMin, int steadyDep, _) = nets["steady"];
        (double nightNet, double nightMin, int nightDep, int nightTips) = nets["nightlife"];
        double nightExpectedNet = nightNet - (nightTips - 52 * 400) / 52.0;
        Check("13-6c economy: 52 fast-forwarded weeks per job with one child and $50/wk funding, from $500: every job ends solvent with no FundsDepleted interrupt",
            coachDep == 0 && steadyDep == 0 && nightDep == 0 && coachMin > 0 && steadyMin > 0 && nightMin > 0,
            $"net/wk coach {coachNet:F1} (min {coachMin:F0}), steady {steadyNet:F1} (min {steadyMin:F0}), nightlife {nightNet:F1} (min {nightMin:F0})");
        Check("13-6c economy: Steady's net is above Nightlife's expected net (the tips re-centred on their $400 mean) — Carbon's §9 Q1 measured in the real loop",
            steadyNet > nightExpectedNet,
            $"steady {steadyNet:F1}/wk vs nightlife expected {nightExpectedNet:F1}/wk (actual {nightNet:F1}, tips {nightTips} over 52 wk)");
    }

    // ------------------------------------------------------------------
    // The fast-forward stops on each interrupt kind
    // ------------------------------------------------------------------

    private static void RunFastForwardChecks(string schemaPath, Func<string, string> newPath)
    {
        // The rules: each kind alone, the order, and the edges.
        static AdvanceStopKind Rule(bool evt = false, float s0 = 10, float s1 = 10, double f0 = 100, double f1 = 100,
            bool game = false, bool card = false, bool window = false, bool changed = false) =>
            AdvanceInterruptRules.FirstStop(new AdvanceProbe(evt, s0, s1, f0, f1, game, card, window, changed), 70f);
        bool each = Rule(evt: true) == AdvanceStopKind.EventFired && Rule(s0: 60, s1: 70) == AdvanceStopKind.StressSpike
            && Rule(f0: 5, f1: 0) == AdvanceStopKind.FundsDepleted && Rule(game: true) == AdvanceStopKind.GameDay
            && Rule(card: true) == AdvanceStopKind.DecisionCard && Rule(window: true) == AdvanceStopKind.TrainingWindow
            && Rule(changed: true) == AdvanceStopKind.AvatarChanged && Rule() == AdvanceStopKind.None;
        bool order = Rule(evt: true, s0: 60, s1: 90, f0: 5, f1: -1, game: true, card: true, window: true, changed: true) == AdvanceStopKind.EventFired
            && Rule(s0: 60, s1: 90, f0: 5, f1: -1, card: true) == AdvanceStopKind.StressSpike
            && Rule(f0: 5, f1: -1, card: true, window: true) == AdvanceStopKind.FundsDepleted
            && Rule(card: true, window: true) == AdvanceStopKind.DecisionCard;
        bool edges = Rule(s0: 75, s1: 80) == AdvanceStopKind.None && Rule(f0: 0, f1: -3) == AdvanceStopKind.None
            && AdvanceInterruptRules.BlockingStop(true, false, false) == AdvanceStopKind.EventFired
            && AdvanceInterruptRules.BlockingStop(false, true, false) == AdvanceStopKind.DecisionCard
            && AdvanceInterruptRules.BlockingStop(false, false, true) == AdvanceStopKind.GameDay
            && AdvanceInterruptRules.BlockingStop(false, false, false) == AdvanceStopKind.None;
        Check("13-6c fast-forward rules: each interrupt kind stops alone (event, stress ≥ 70, funds ≤ 0, game, card, training birthday, avatar change), in SR-2a's order first; stress and funds are edge-triggered; a pending choice blocks before a tick",
            each && order && edges);

        // The real loop: stress, funds, a card at the rollover, a training birthday, a clean target.
        using var w = new ParentWorld(schemaPath, newPath("ff"), startDay: 330, startFunds: 5000.0,
            children: new[] { (4, 70), (9, 70) });
        w.Jobs.OpenJobCard(w.ParentId, w.Day);
        w.ChooseParentJob("coach");
        long spikeDay = w.Day + 3;
        AdvanceSummary stress = w.AdvanceUntil(w.Day + 20, () =>
        {
            if (w.Day == spikeDay)
            {
                w.Bus.Publish(new StressImpulseEvent(w.ParentId, 100f)); // a gritty event's spike
            }
        });
        w.Life.SetStress(w.ParentId, 0f);
        long brokeDay = w.Day + 2;
        AdvanceSummary broke = w.AdvanceUntil(w.Day + 20, () =>
        {
            if (w.Day == brokeDay)
            {
                w.Bus.Publish(new FundsImpulseEvent(w.ParentId, -w.LifeFunds)); // a gritty event's bill
            }
        });
        w.Bus.Publish(new FundsImpulseEvent(w.ParentId, 5000.0));
        w.Bus.DispatchPending();
        // To the rollover: the yearly card opens on day 366 (and the 4-year-old turns 5 the same day: the card wins).
        AdvanceSummary card = w.AdvanceUntil(GlobalState.DaysPerSeason + 30);
        bool cardOnRollover = card.Stop == AdvanceStopKind.DecisionCard && w.State.DayOfSeason == 1;
        w.Jobs.ResolveJobChoice(w.ParentId, "coach", w.Day);
        int trainableAfter = w.Training.LoadTrainable(w.ParentId, new List<PlayerRow>());
        // The next rollover, with the card opened beforehand: the training birthday stops it (the 9-year-old is now 10; nobody crosses 5 or 16 → target).
        AdvanceSummary quiet = w.AdvanceUntil(w.Day + 30);
        Check("13-6c fast-forward (real loop): stops the day stress crosses 70, the day funds cross zero, and on the rollover day the yearly job card opens; a quiet month reaches its target",
            stress.Stop == AdvanceStopKind.StressSpike && stress.EndDay == spikeDay
            && broke.Stop == AdvanceStopKind.FundsDepleted && broke.EndDay == brokeDay && cardOnRollover
            && quiet.Stop == AdvanceStopKind.TargetReached,
            $"stress {stress.Stop}@{stress.EndDay} (spike {spikeDay}), funds {broke.Stop}@{broke.EndDay}, card {card.Stop}@{card.EndDay}, quiet {quiet.Stop}; trainable {trainableAfter}");

        using var b = new ParentWorld(schemaPath, newPath("ff_birthday"), startDay: 340, startFunds: 5000.0,
            children: new[] { (4, 70) });
        b.Jobs.OpenJobCard(b.ParentId, b.Day);
        b.ChooseParentJob("steady");
        b.Jobs.OpenJobCard(b.ParentId, 360); // an open card before the rollover: the rollover can't open it again
        AdvanceSummary birthday = b.AdvanceUntil(GlobalState.DaysPerSeason + 30);
        Check("13-6c fast-forward (real loop): with the card already open, a child's fifth birthday at the rollover stops it (TrainingWindow) — the trainable list grows from 0 to 1",
            birthday.Stop == AdvanceStopKind.TrainingWindow && b.State.DayOfSeason == 1
            && b.Training.LoadTrainable(b.ParentId, new List<PlayerRow>()) == 1,
            $"{birthday.Stop}@{birthday.EndDay}");
    }

    // ------------------------------------------------------------------
    // D7-A: retirement into the parent phase (the live composition)
    // ------------------------------------------------------------------

    private static void RunRetirementReleaseChecks(string schemaPath, Func<string, string> newPath)
    {
        long Day(int year, int dayOfSeason) => (long)(year - 2026) * GlobalState.DaysPerSeason + dayOfSeason;
        int proTeam = LeagueStructure.TeamIdBase(LeagueTier.Pro) + 7;

        (LiveWorld World, ParentJobService Jobs, List<AvatarReleasedEvent> Released, string Parent, string Child) Compose(
            string tag, int childAge, int interest, bool autopilot)
        {
            var w = new LiveWorld(schemaPath, newPath(tag), startDay: 362);
            w.Career.AutopilotSuccession = autopilot;
            ParentJobCatalog catalog = ParentJobCatalog.Parse(File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(schemaPath)))!, "Data", "Family", "parent_jobs.json")));
            var jobs = new ParentJobService(w.Db, w.Players, new PersonQueries(w.Db), w.Baseball, w.GameState, catalog, w.Bus)
            {
                SeedOverride = 0x136C_D7A0UL,
            };
            var released = new List<AvatarReleasedEvent>();
            w.Bus.Subscribe<AvatarReleasedEvent>(e =>
            {
                released.Add(e);
                if (e.EnteredParentPhase && w.Career.InParentPhase && w.Career.AvatarPlayerId == e.PlayerId)
                {
                    jobs.OpenJobCard(e.PlayerId, w.State.CurrentDay);
                }
            });
            w.Bus.Subscribe<SeasonRolledOverEvent>(_ =>
            {
                if (w.Career.HasAvatar && w.Career.InParentPhase && !w.Career.IsLineageOver)
                {
                    jobs.OpenJobCard(w.Career.AvatarPlayerId, w.State.CurrentDay);
                }
            });
            w.Bus.Subscribe<AvatarChangedEvent>(e =>
            {
                if (e.TeamId != 0)
                {
                    jobs.ClearForTakeover(w.State.CurrentDay);
                }
            });
            w.Career.CreateAvatar("Old", "Timer", proTeam, new PlayerRatingsRow
            {
                BatPower = 70, BatContact = 70, BatDiscipline = 70, PitStuff = 50, PitControl = 50, PitStamina = 50, Fielding = 50,
            });
            string parent = w.Career.AvatarPlayerId;
            w.Players.SetAge(parent, 41);
            string child = w.Career.ConceiveChild("Heir", "Timer", birthAge: childAge - 1);
            w.Players.SetBaseballInterest(child, interest);
            return (w, jobs, released, parent, child);
        }

        {
            var (w, jobs, released, parent, child) = Compose("d7_15", childAge: 15, interest: 100, autopilot: true);
            using (w)
            {
                w.AdvanceTo(Day(2027, 1));
                bool entered = w.Career.InParentPhase && !w.Career.IsLineageOver && !w.Career.IsAvatarRostered
                    && w.TeamOf(parent) == 0 && released.Count == 1 && released[0].EnteredParentPhase
                    && released[0].Tier == (byte)LeagueTier.Pro && jobs.HasPendingJobChoice
                    && !w.GameState.TryGetText(GameStateKeys.LineageOverReason, out _);
                w.Players.TryGetById(child, out PlayerRow kid15);
                w.AdvanceTo(Day(2028, 1));
                bool tookOver = w.Career.AvatarPlayerId == child && w.TeamOf(child) == 101 && !w.Career.InParentPhase
                    && !jobs.HasPendingJobChoice && jobs.CurrentJobId is null;
                Check("13-6c D7-A: a 42-year-old with a willing 15-year-old (NoPlayableHeir before) retires into the parent phase — released by the pipeline (unrostered, AvatarReleasedEvent once, the job card open) — and the child takes over at 16",
                    entered && kid15.Age == 15 && tookOver,
                    $"entered {entered}, child {kid15.Age}, avatar {(w.Career.AvatarPlayerId == child ? "child" : "parent")}");
            }
        }
        {
            var (w, jobs, released, parent, _) = Compose("d7_8", childAge: 8, interest: 35, autopilot: false);
            using (w)
            {
                w.AdvanceTo(Day(2027, 1));
                Check("13-6c D7-A: a 42-year-old whose only child is 8 with interest 35 (NoWillingHeir before) enters the parent phase instead (the interactive branch), with the card open",
                    w.Career.InParentPhase && !w.Career.IsLineageOver && !w.Career.IsAvatarRostered && released.Count == 1
                    && jobs.HasPendingJobChoice && !w.Career.HasPendingSuccessionChoice,
                    $"in phase {w.Career.InParentPhase}, over {w.Career.LineageOverReason}");
            }
        }
        {
            var (w, jobs, released, parent, child) = Compose("d7_17", childAge: 17, interest: 100, autopilot: true);
            using (w)
            {
                w.AdvanceTo(Day(2027, 1));
                var holders = new List<string>();
                w.Baseball.LoadActiveFlagPlayerIds(ParentJobService.ParentPhaseFlag, holders);
                Check("13-6c D7-A: a 42-year-old with a willing, unrostered 17-year-old is released and taken over in the same rollover — the child is the avatar on the family HS team, the parent retired, and no job card or parent_phase flag is left",
                    w.Career.AvatarPlayerId == child && w.TeamOf(child) == 101 && !w.Career.InParentPhase && w.TeamOf(parent) == 0
                    && released.Count == 1 && !jobs.HasPendingJobChoice && holders.Count == 0 && !w.Career.IsLineageOver,
                    $"avatar {(w.Career.AvatarPlayerId == child ? "child" : "parent")}, card {jobs.HasPendingJobChoice}, flags {holders.Count}");
            }
        }
        {
            var (w, jobs, released, parent, _) = Compose("d7_unwilling", childAge: 18, interest: 0, autopilot: true);
            using (w)
            {
                w.AdvanceTo(Day(2027, 1));
                Check("13-6c D7-A: a 42-year-old whose children are all 16+ and unwilling still gets NoWillingHeir (no release, no phase, still rostered)",
                    w.Career.IsLineageOver && w.Career.LineageOverReason == LineageFailure.NoWillingHeir && !w.Career.InParentPhase
                    && released.Count == 0 && w.TeamOf(parent) == proTeam && !jobs.HasPendingJobChoice,
                    $"reason {w.Career.LineageOverReason}");
            }
        }
    }
}
