using DirtAndDiamonds.Simulation.Life;
using DirtAndDiamonds.Core;

namespace DirtAndDiamonds.Tools.NeedsDecayHarness;

/// <summary>
/// The executable behind the simulate_utility_decay skill (life_sim_ai.md mandate:
/// balance the passive Need decay curves before any utility-action tuning happens).
///
/// Simulates 168 in-game hours (one week) of PASSIVE decay — no eating, sleeping,
/// or other replenishing actions — for a standard NPC starting fully satisfied, and
/// prints a text-based graph of all five needs (Hunger, Sleep, Hygiene, Social,
/// Fitness) across the week. Two anchors drive the tuning:
///   1. A 3-hour attended baseball game must not leave the NPC starving.
///   2. A full week of total neglect must drive every need into desperation
///      (CriticalThreshold, life_sim_ai.md's stress-override line).
///
/// Usage: dotnet run --project Tools/NeedsDecayHarness
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();

    private const int HorizonHours = 168; // one week
    private const int GameLengthHours = 3; // a 3-hour attended baseball game
    private const float StarvingThreshold = 65f; // "not starving" bar after a single game
    private const int GraphSampleStepHours = 6;
    private const int GraphColumns = HorizonHours / GraphSampleStepHours; // 28 columns

    private static readonly NeedType[] AllNeeds =
    {
        NeedType.Hunger, NeedType.Sleep, NeedType.Hygiene, NeedType.Social, NeedType.Fitness,
    };

    private static int Main()
    {
        // Full-resolution hourly trace, one array per need, reused for the graph,
        // the fixed-hour table, and every check below.
        var trace = new Dictionary<NeedType, float[]>();
        foreach (NeedType need in AllNeeds)
        {
            trace[need] = new float[HorizonHours + 1];
        }

        NeedsState state = NeedsState.FullySatisfied();
        foreach (NeedType need in AllNeeds)
        {
            trace[need][0] = state.Get(need);
        }
        for (int hour = 1; hour <= HorizonHours; hour++)
        {
            state = NeedsEngine.DecayHour(state);
            foreach (NeedType need in AllNeeds)
            {
                trace[need][hour] = state.Get(need);
            }
        }

        PrintGraph(trace);
        PrintFixedHourTable(trace);

        RunChecks(trace);
        RunModifierChecks();
        RunUtilityChecks();
        RunActionThresholdChecks();
        RunLifeSimChecks();
        RunDailyClockChecks();
        RunSurvivalEconomyChecks();
        RunHouseholdBoardChecks();
        RunSleepBandChecks();
        RunPersonLayerChecks();
        RunRelationshipGraphChecks();

        int failed = 0;
        Console.WriteLine();
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
    // Text-based graph: one row per need, sampled every 6h across the week.
    // ------------------------------------------------------------------

    private static readonly char[] DensityRamp = { ' ', '.', ':', '-', '=', '+', '*', '#', '%', '@' };

    private static void PrintGraph(Dictionary<NeedType, float[]> trace)
    {
        Console.WriteLine($"--- simulate_utility_decay: {HorizonHours}h passive decay, standard NPC, full satisfaction start ---");
        Console.WriteLine($"(each column = {GraphSampleStepHours}h; density ' .:-=+*#%@' == value 0..100)\n");

        foreach (NeedType need in AllNeeds)
        {
            float[] values = trace[need];
            var row = new char[GraphColumns];
            for (int col = 0; col < GraphColumns; col++)
            {
                int hour = Math.Min(col * GraphSampleStepHours, HorizonHours);
                row[col] = DensityRamp[Math.Clamp((int)MathF.Round(values[hour] / 100f * (DensityRamp.Length - 1)), 0, DensityRamp.Length - 1)];
            }
            Console.WriteLine($"  {need,-8} [{new string(row)}] {values[HorizonHours],5:F1}");
        }
        Console.WriteLine();
    }

    private static void PrintFixedHourTable(Dictionary<NeedType, float[]> trace)
    {
        int[] checkpoints = { 0, 3, 6, 12, 24, 48, 72, 96, 120, 144, 168 };
        Console.Write("  Need     ");
        foreach (int h in checkpoints)
        {
            Console.Write($"{$"{h}h",7}");
        }
        Console.WriteLine();

        foreach (NeedType need in AllNeeds)
        {
            float[] values = trace[need];
            Console.Write($"  {need,-8} ");
            foreach (int h in checkpoints)
            {
                Console.Write($"{values[h],7:F1}");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    // Checks
    // ------------------------------------------------------------------

    private static void RunChecks(Dictionary<NeedType, float[]> trace)
    {
        // 1. Clamping: never leaves [0, 100].
        bool inBounds = true;
        foreach (NeedType need in AllNeeds)
        {
            foreach (float v in trace[need])
            {
                if (v < NeedsEngine.MinNeed || v > NeedsEngine.MaxNeed)
                {
                    inBounds = false;
                }
            }
        }
        Check("all needs stay within [0, 100] across the week", inBounds);

        // 2. Monotonic non-increase under pure passive decay (no replenishment modeled).
        bool monotonic = true;
        foreach (NeedType need in AllNeeds)
        {
            float[] values = trace[need];
            for (int h = 1; h <= HorizonHours; h++)
            {
                if (values[h] > values[h - 1] + 1e-6f)
                {
                    monotonic = false;
                }
            }
        }
        Check("passive decay is monotonically non-increasing (no stress/env noise)", monotonic);

        // 3. A 3-hour attended game must not leave the NPC starving.
        foreach (NeedType need in AllNeeds)
        {
            float afterGame = trace[need][GameLengthHours];
            Check($"{need} stays above {StarvingThreshold:F0} after a {GameLengthHours}h attended game",
                afterGame >= StarvingThreshold, $"{afterGame:F1}");
        }

        // 4. A full week of total neglect must push every need into desperation.
        foreach (NeedType need in AllNeeds)
        {
            float afterWeek = trace[need][HorizonHours];
            Check($"{need} reaches CriticalThreshold ({NeedsEngine.CriticalThreshold:F0}) within {HorizonHours}h of total neglect",
                afterWeek <= NeedsEngine.CriticalThreshold, $"{afterWeek:F1}");
        }

        // 5. Hunger (fastest-tuned need) hits critical well before the week is out —
        //    otherwise the "desperation" stress overlay never has a chance to fire.
        int hungerCriticalHour = FirstHourAtOrBelow(trace[NeedType.Hunger], NeedsEngine.CriticalThreshold);
        Check("Hunger reaches CriticalThreshold inside the first 48h",
            hungerCriticalHour >= 0 && hungerCriticalHour <= 48, $"hour {hungerCriticalHour}");

        // 6. Fitness (slowest-tuned need) still has the longest runway of all five —
        //    proves the relative pacing (bio needs decay faster than lifestyle needs).
        int fitnessCriticalHour = FirstHourAtOrBelow(trace[NeedType.Fitness], NeedsEngine.CriticalThreshold);
        bool fitnessIsSlowest = AllNeeds.Where(n => n != NeedType.Fitness)
            .All(n => FirstHourAtOrBelow(trace[n], NeedsEngine.CriticalThreshold) is int other && other >= 0 && other <= fitnessCriticalHour);
        Check("Fitness takes the longest to reach CriticalThreshold of all five needs",
            fitnessIsSlowest, $"Fitness hour {fitnessCriticalHour}");
    }

    private static int FirstHourAtOrBelow(float[] values, float threshold)
    {
        for (int h = 0; h < values.Length; h++)
        {
            if (values[h] <= threshold)
            {
                return h;
            }
        }
        return -1;
    }

    // ------------------------------------------------------------------
    // Modifier layer (E and S) — life_sim_needs_decay.md §4/§9. The §9
    // "modifier fixtures" are quoted verbatim from the design doc, which
    // wrote them to be verified "once E/S are wired to context" — i.e. by
    // the per-action Environmental Multiplier pass that added these checks.
    // ------------------------------------------------------------------

    private const float ModifierFixtureTolerance = 0.005f; // doc quotes values to 2 decimals

    private static void RunModifierChecks()
    {
        Console.WriteLine("--- modifier layer (E/S) fixtures (life_sim_needs_decay.md §4/§9) ---\n");

        // §9 fixture: Hunger at v=100, labor hustle E=1.5, high stress S=1.5 → 90.55
        // (neutral would lose only 4.20).
        float hustleStressed = NeedsEngine.DecayHour(100f, NeedsEngine.Hunger, environmentalMultiplier: 1.5f, stressModifier: 1.5f);
        Check("§9 fixture: Hunger v=100, E=1.5, S=1.5 → 90.55",
            MathF.Abs(hustleStressed - 90.55f) <= ModifierFixtureTolerance, $"{hustleStressed:F4}");

        // §9 fixture: Hunger at v=40, E=1, S=2.0 → 26.76; calm from the same point → 33.38.
        // Stress nearly doubles the bite on an already-hungry NPC — the intended
        // "stress compounds desperation" behavior.
        float hungryStressed = NeedsEngine.DecayHour(40f, NeedsEngine.Hunger, environmentalMultiplier: 1f, stressModifier: 2f);
        Check("§9 fixture: Hunger v=40, E=1, S=2.0 → 26.76",
            MathF.Abs(hungryStressed - 26.76f) <= ModifierFixtureTolerance, $"{hungryStressed:F4}");
        float hungryCalm = NeedsEngine.DecayHour(40f, NeedsEngine.Hunger, environmentalMultiplier: 1f, stressModifier: 1f);
        Check("§9 fixture: Hunger v=40 calm (S=1) → 33.38",
            MathF.Abs(hungryCalm - 33.38f) <= ModifierFixtureTolerance, $"{hungryCalm:F4}");

        // §4.2 combined ceiling: E=2 · S=2 = 4 clamps to MaxCombinedModifier (3), so it
        // decays exactly as hard as E=3 alone — and no harder.
        float clamped = NeedsEngine.DecayHour(100f, NeedsEngine.Hunger, environmentalMultiplier: 2f, stressModifier: 2f);
        float atCeiling = NeedsEngine.DecayHour(100f, NeedsEngine.Hunger, environmentalMultiplier: NeedsEngine.MaxCombinedModifier, stressModifier: 1f);
        Check($"§4.2 ceiling: E·S=4 clamps to {NeedsEngine.MaxCombinedModifier:F0} (Hunger v=100 → 87.40)",
            MathF.Abs(clamped - 87.4f) <= ModifierFixtureTolerance && clamped == atCeiling, $"{clamped:F4} vs E=3 alone {atCeiling:F4}");

        // Degenerate-case identity: the scalar overload must stay exactly the uniform
        // vector — the §4.1 "E_all" contract. Composite mid-range state, per-need equality.
        NeedsState composite = NeedsState.FullySatisfied();
        composite.Set(NeedType.Hunger, 73.2f);
        composite.Set(NeedType.Sleep, 55f);
        composite.Set(NeedType.Social, 31.7f);
        NeedsState viaScalar = NeedsEngine.DecayHour(composite, environmentalMultiplier: 1.3f, stressModifier: 1.1f);
        NeedsState viaVector = NeedsEngine.DecayHour(composite, EnvironmentalModifiers.Uniform(1.3f), stressModifier: 1.1f);
        bool scalarEqualsUniform = true;
        foreach (NeedType need in AllNeeds)
        {
            if (viaScalar.Get(need) != viaVector.Get(need))
            {
                scalarEqualsUniform = false;
            }
        }
        Check("scalar DecayHour ≡ Uniform-vector DecayHour (E_all degenerate case, bit-exact)", scalarEqualsUniform);

        // Per-need vector has teeth: an hour under Workout's environment (Hunger ×1.5,
        // Sleep ×1.3) hits exactly those two needs harder while the other three decay
        // bit-identically to neutral.
        NeedsState full = NeedsState.FullySatisfied();
        NeedsState underWorkout = NeedsEngine.DecayHour(full, ActionCatalog.Workout.Environment);
        NeedsState underNeutral = NeedsEngine.DecayHour(full, EnvironmentalModifiers.Neutral);
        Check("Workout env: Hunger decays ×1.5 (100 → 93.70)",
            MathF.Abs(underWorkout.Hunger - 93.7f) <= ModifierFixtureTolerance, $"{underWorkout.Hunger:F4}");
        Check("Workout env: Sleep decays ×1.3 (100 → 95.58)",
            MathF.Abs(underWorkout.Sleep - 95.58f) <= ModifierFixtureTolerance, $"{underWorkout.Sleep:F4}");
        Check("Workout env: Hygiene/Social/Fitness decay bit-identically to neutral",
            underWorkout.Hygiene == underNeutral.Hygiene
            && underWorkout.Social == underNeutral.Social
            && underWorkout.Fitness == underNeutral.Fitness);

        // Data integrity: every catalog action's authored E stays inside the §4.1
        // design range [0.25, 3.0] for every need.
        bool allInRange = true;
        foreach (NpcActionDefinition def in ActionCatalog.All)
        {
            foreach (NeedType need in AllNeeds)
            {
                float e = def.Environment.Get(need);
                if (e < 0.25f || e > 3.0f)
                {
                    allInRange = false;
                }
            }
        }
        Check("every catalog action's Environment multipliers stay in the §4.1 range [0.25, 3.0]", allInRange);

        // Phase 8a: School/LegalWork are schedule-block-only, deliberately
        // excluded from ActionCatalog.All (so the sweep above never sees
        // them) — a separate assertion keeps the §4.1 range guarantee whole.
        bool blockOnlyInRange = true;
        foreach (NpcActionDefinition def in new[] { ActionCatalog.School, ActionCatalog.LegalWork })
        {
            foreach (NeedType need in AllNeeds)
            {
                float e = def.Environment.Get(need);
                if (e < 0.25f || e > 3.0f)
                {
                    blockOnlyInRange = false;
                }
            }
        }
        Check("schedule-block-only actions (School/LegalWork) also stay in the §4.1 Environment range", blockOnlyInRange);

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    // Utility action-selection fixtures (UtilityCalculator.SelectAction).
    // Expected winners below are hand-derived from the ActionCatalog/
    // ActionWeights constants and re-verified whenever either changes.
    // ------------------------------------------------------------------

    private static void RunUtilityChecks()
    {
        Console.WriteLine("--- utility action-selection fixtures (UtilityCalculator.SelectAction) ---\n");

        // 1. Fully satisfied + ample funds: every action's need-deficit term is
        //    0, so the free/no-risk/no-time Idle action wins outright (utility
        //    ~0.80 vs. the next-best Shower ~0.79).
        NeedsState full = NeedsState.FullySatisfied();
        NpcActionId satisfiedPick = UtilityCalculator.SelectAction(full, 1000.0, UtilityCalculator.DefaultWeights, out float satisfiedUtility);
        Check("fully satisfied + ample funds picks Idle", satisfiedPick == NpcActionId.Idle,
            $"picked {satisfiedPick} (utility {satisfiedUtility:F3})");

        // 2. Hunger critical (10), everything else full, generous funds: Eat
        //    wins — the override correctly targets the actual crisis, beating
        //    Idle (~0.50) and both stress-relief actions (~1.23/~1.21), i.e.
        //    addressing the real problem outranks merely coping with it.
        NeedsState hungerCritical = NeedsState.FullySatisfied();
        hungerCritical.Set(NeedType.Hunger, 10f);
        NpcActionId crisisPick = UtilityCalculator.SelectAction(hungerCritical, 1000.0, UtilityCalculator.DefaultWeights, out float crisisUtility);
        Check("Hunger critical + generous funds picks Eat (targets the actual need)", crisisPick == NpcActionId.Eat,
            $"picked {crisisPick} (utility {crisisUtility:F3})");

        // 3. Same crisis, but broke (funds = 0): Eat becomes ruinously
        //    unaffordable (FinancialCostScore goes deeply negative) and loses
        //    to the free stress-relief action PickArgument — which only wins
        //    BECAUSE of the critical-only stress-relief bonus (hand-verified:
        //    without that +0.8 term PickArgument scores ~0.41, below Idle's
        //    ~0.50; with it, ~1.21). Proves the affordability-awareness and
        //    the stress-relief gating together: a broke NPC in crisis copes
        //    for free rather than idling or attempting something it can't
        //    pay for.
        NpcActionId brokeCrisisPick = UtilityCalculator.SelectAction(hungerCritical, 0.0, UtilityCalculator.DefaultWeights, out float brokeCrisisUtility);
        Check("Hunger critical + zero funds picks PickArgument (free coping, gated by the crisis)", brokeCrisisPick == NpcActionId.PickArgument,
            $"picked {brokeCrisisPick} (utility {brokeCrisisUtility:F3})");

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    // Need-deficit crossover sweep — the precise, deterministic form of the
    // "NPCs eat very frequently" artifact flagged after the first tuning
    // pass. For each need-restoring action, finds the highest need value at
    // which it first overtakes Idle (every other need full, funds ample).
    // A trivial deficit shouldn't win (that was the bug); a real deficit
    // should win with room to spare before CriticalThreshold ever has to
    // force the issue via the stress override.
    // ------------------------------------------------------------------

    private const double CrossoverFunds = 1000.0;
    private const int MaxSensibleCrossover = 75; // shouldn't fire on a <25% deficit
    private const int MinSensibleCrossover = 25; // should fire well above CriticalThreshold (20)

    private static readonly (NpcActionId Id, NeedType Need)[] NeedRestoringActions =
    {
        (NpcActionId.Eat, NeedType.Hunger),
        (NpcActionId.Sleep, NeedType.Sleep),
        (NpcActionId.Shower, NeedType.Hygiene),
        (NpcActionId.SocializeEvening, NeedType.Social),
        (NpcActionId.Workout, NeedType.Fitness),
    };

    private static void RunActionThresholdChecks()
    {
        Console.WriteLine("--- need-deficit crossover sweep (need value where an action first beats Idle) ---\n");

        foreach ((NpcActionId id, NeedType need) in NeedRestoringActions)
        {
            int crossover = FindCrossoverValue(id, need);
            Console.WriteLine($"    {id,-16} beats Idle once {need,-8} drops to {crossover}");
            Check($"{id} doesn't fire on a trivial deficit ({need} crossover <= {MaxSensibleCrossover})",
                crossover >= 0 && crossover <= MaxSensibleCrossover, $"crosses at {crossover}");
            Check($"{id} fires with real margin before CriticalThreshold ({need} crossover >= {MinSensibleCrossover})",
                crossover >= MinSensibleCrossover, $"crosses at {crossover}");
        }

        Console.WriteLine();
    }

    // Scans from fully satisfied down to empty; needScore is monotonically
    // non-decreasing as the need drops and Idle's own utility never improves
    // across the scan (it only drops, at the CriticalThreshold override), so
    // the first value where actionId wins is the one and only crossover.
    private static int FindCrossoverValue(NpcActionId actionId, NeedType need)
    {
        for (int v = 100; v >= 0; v--)
        {
            NeedsState state = NeedsState.FullySatisfied();
            state.Set(need, v);
            NpcActionId picked = UtilityCalculator.SelectAction(state, CrossoverFunds, UtilityCalculator.DefaultWeights, out _);
            if (picked == actionId)
            {
                return v;
            }
        }
        return -1;
    }

    // ------------------------------------------------------------------
    // LifeSimManager integration proof — the literal M3 exit criterion:
    // "NPCs self-manage needs over a simulated month; stress override
    // provably fires." Drives a real EventBus + LifeSimManager by hand
    // (no TimeManager/database needed) through 30 DayAdvancedEvents.
    // ------------------------------------------------------------------

    private static void RunLifeSimChecks()
    {
        Console.WriteLine("--- LifeSimManager: simulated month (30 days), utility-driven action selection ---\n");

        const string FundedNpc = "funded-npc";
        const string BrokeNpc = "broke-npc";
        const int SimulatedDays = 30;

        var bus = new EventBus();
        var lifeSim = new LifeSimManager();
        lifeSim.Seed(new[]
        {
            new NpcSeed(FundedNpc, 50000.0),
            new NpcSeed(BrokeNpc, 0.0),
        });
        lifeSim.AttachTo(bus);

        var fundedMin = new Dictionary<NeedType, float>();
        var brokeMin = new Dictionary<NeedType, float>();
        foreach (NeedType need in AllNeeds)
        {
            fundedMin[need] = NeedsEngine.MaxNeed;
            brokeMin[need] = NeedsEngine.MaxNeed;
        }

        for (int day = 1; day <= SimulatedDays; day++)
        {
            bus.Publish(new DayAdvancedEvent(day, 2026, day));
            bus.DispatchPending();

            lifeSim.TryGetNeeds(FundedNpc, out NeedsState fundedNeeds);
            lifeSim.TryGetNeeds(BrokeNpc, out NeedsState brokeNeeds);
            foreach (NeedType need in AllNeeds)
            {
                fundedMin[need] = Math.Min(fundedMin[need], fundedNeeds.Get(need));
                brokeMin[need] = Math.Min(brokeMin[need], brokeNeeds.Get(need));
            }
        }

        Console.WriteLine($"  Funded NPC ($50,000 seed) — {SimulatedDays}-day minimum per need:");
        foreach (NeedType need in AllNeeds)
        {
            Console.WriteLine($"    {need,-8} min {fundedMin[need],6:F1}");
        }
        Console.WriteLine($"  Broke NPC ($0 seed) — {SimulatedDays}-day minimum per need:");
        foreach (NeedType need in AllNeeds)
        {
            Console.WriteLine($"    {need,-8} min {brokeMin[need],6:F1}");
        }
        Console.WriteLine();

        // The M3 exit criterion, literally: an adequately-funded NPC
        // self-manages needs over a simulated month and never bottoms out —
        // contrast with the pure-passive-decay trace above, where every need
        // floors at 0 by design (no actions are modeled there at all).
        bool fundedNeverFloored = true;
        foreach (NeedType need in AllNeeds)
        {
            if (fundedMin[need] <= NeedsEngine.MinNeed)
            {
                fundedNeverFloored = false;
            }
        }
        Check("adequately-funded NPC's needs never bottom out over a simulated month", fundedNeverFloored);

        // A broke NPC (no income mechanic exists yet — that's Phase 8
        // territory) can still keep its free needs managed indefinitely
        // (Sleep, Shower cost $0 in the catalog), but its money-gated needs
        // (Hunger via Eat, Social via SocializeEvening, Fitness via Workout)
        // collapse exactly like total neglect. Expected, not a bug: nothing
        // in the current catalog restores those needs for free.
        bool freeNeedsManaged = brokeMin[NeedType.Sleep] > NeedsEngine.MinNeed && brokeMin[NeedType.Hygiene] > NeedsEngine.MinNeed;
        Check("broke NPC's free needs (Sleep, Hygiene) still stay managed", freeNeedsManaged);

        bool paidNeedsCollapse = brokeMin[NeedType.Hunger] <= NeedsEngine.MinNeed
            && brokeMin[NeedType.Social] <= NeedsEngine.MinNeed
            && brokeMin[NeedType.Fitness] <= NeedsEngine.MinNeed;
        Check("broke NPC's money-gated needs (Hunger/Social/Fitness) collapse without an income mechanic (expected — no Phase 8 economy yet)", paidNeedsCollapse);
    }

    // ------------------------------------------------------------------
    // Phase 9b: the avatar daily clock. Two guarantees under test:
    //   1. An untouched (never-planned) avatar day reproduces the pre-9b
    //      autopilot trace bit-for-bit — marking an avatar alone changes
    //      NOTHING for headless callers (the AutopilotAttendedGames mirror).
    //   2. A planned day runs its blocks exactly: sleep restores at the
    //      catalog rate under the catalog environment, inert blocks decay
    //      neutrally, free hours autopilot, and the plan is one-shot.
    // ------------------------------------------------------------------

    private static void RunDailyClockChecks()
    {
        Console.WriteLine("--- 9b daily clock: avatar schedule blocks vs autopilot (bit-for-bit guarantee) ---\n");

        const string AvatarId = "avatar";
        const string BystanderId = "bystander-npc";
        const double SeedFunds = 50000.0;
        const float SeedStress = 50f; // nonzero so the stress path is exercised on both sides

        // --- DaySchedule validation ---
        bool negativeThrows = Throws(() => new DaySchedule(-1, 0, 0, 0, 0));
        bool overflowThrows = Throws(() => new DaySchedule(10, 8, 4, 3, 3)); // 28h
        Check("DaySchedule rejects negative hours and >24h totals", negativeThrows && overflowThrows);

        // --- Surface guards: avatar pointer + school gate ---
        var gated = new LifeSimManager();
        gated.Seed(new[] { new NpcSeed(AvatarId, SeedFunds) });
        bool untrackedThrows = Throws(() => gated.SetAvatar("nobody"));
        bool noAvatarThrows = Throws(() => gated.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 0)));
        gated.SetAvatar(AvatarId);
        // AvatarSchoolAvailable defaults false — an MLB avatar has no school.
        bool schoolBlocked = Throws(() => gated.SetTodaySchedule(new DaySchedule(8, 6, 0, 0, 0)));
        gated.AvatarSchoolAvailable = true;
        gated.SetTodaySchedule(new DaySchedule(8, 6, 0, 0, 0));
        bool accepted = gated.TryGetTodaySchedule(out DaySchedule roundTrip)
            && roundTrip.SleepHours == 8 && roundTrip.SchoolHours == 6 && roundTrip.FreeHours == 10;
        Check("untracked avatar / plan-without-avatar both throw", untrackedThrows && noAvatarThrows);
        Check("School hours rejected unless AvatarSchoolAvailable (the HS/College tier projection)",
            schoolBlocked && accepted);
        // Re-pointing the avatar (succession) drops the pending plan.
        gated.SetAvatar(AvatarId);
        Check("SetAvatar drops any pending plan (an heir never inherits the retiree's day)",
            !gated.HasTodaySchedule);

        // --- Guarantee 1: never-planned avatar's Needs/Stress reproduce the
        // pre-9b autopilot trace bit-for-bit, 30 days. Funds no longer stay
        // bit-identical to a plain NPC (8a design): the recurring cost-of-
        // living drain fires for the avatar whether or not a schedule was
        // ever submitted, so "becoming the avatar" alone is no longer fully
        // inert — but nothing BEYOND that intentional, exactly-quantifiable
        // divergence should differ. The bystander NPC (never the avatar in
        // either world) must stay FULLY bit-identical, funds included. ---
        (LifeSimManager control, EventBus controlBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, SeedStress, markAvatar: false);
        (LifeSimManager marked, EventBus markedBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, SeedStress, markAvatar: true);

        bool needsStressIdentical = true;
        bool bystanderFullyIdentical = true;
        int costOfLivingDays = 0;
        for (int day = 1; day <= 30; day++)
        {
            controlBus.Publish(new DayAdvancedEvent(day, 2026, day));
            controlBus.DispatchPending();
            markedBus.Publish(new DayAdvancedEvent(day, 2026, day));
            markedBus.DispatchPending();
            if (day % LifeSimManager.CostOfLivingCadenceDays == 0)
            {
                costOfLivingDays++;
            }

            control.TryGetNeeds(AvatarId, out NeedsState controlAvatarNeeds);
            marked.TryGetNeeds(AvatarId, out NeedsState markedAvatarNeeds);
            foreach (NeedType need in AllNeeds)
            {
                needsStressIdentical &= controlAvatarNeeds.Get(need) == markedAvatarNeeds.Get(need);
            }
            control.TryGetStress(AvatarId, out float controlAvatarStress);
            marked.TryGetStress(AvatarId, out float markedAvatarStress);
            needsStressIdentical &= controlAvatarStress == markedAvatarStress;
            bystanderFullyIdentical &= PersonStateIdentical(control, marked, BystanderId);
        }
        Check("never-planned avatar's Needs/Stress reproduce the pre-9b autopilot trace bit-for-bit over 30 days",
            needsStressIdentical);
        Check("bystander NPC stays fully bit-identical (needs+stress+funds) regardless of who's marked avatar",
            bystanderFullyIdentical);

        control.TryGetFunds(AvatarId, out double controlAvatarFunds);
        marked.TryGetFunds(AvatarId, out double markedAvatarFunds);
        double expectedDrain = costOfLivingDays * LifeSimManager.WeeklyCostOfLiving;
        Check($"never-planned avatar's Funds diverge from a plain NPC by EXACTLY the 8a cost-of-living drain ({costOfLivingDays} cadence day(s) x ${LifeSimManager.WeeklyCostOfLiving:F0})",
            controlAvatarFunds - markedAvatarFunds == expectedDrain,
            $"control {controlAvatarFunds:F2} - marked {markedAvatarFunds:F2} = {controlAvatarFunds - markedAvatarFunds:F2}, expected {expectedDrain:F2}");

        // --- Guarantee 2a: a fully-allocated day matches the closed form exactly ---
        // 8 Sleep + 6 School + 4 Practice + 3 Game + 3 Work = 24h, zero free.
        (LifeSimManager planned, EventBus plannedBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        planned.AvatarSchoolAvailable = true;
        planned.SetTodaySchedule(new DaySchedule(8, 6, 4, 3, 3));
        plannedBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        plannedBus.DispatchPending();

        // Canonical day order: School (8a: real meal-access restore + neutral
        // env) -> Practice+Game (still Idle-equivalent, neutral, no restore)
        // -> Work (8a: real meal-access restore + income + heavy Sleep/Fitness
        // drain) -> Sleep as the night cap (run first it would burn the
        // restore against the clamp).
        NeedsState expected = NeedsState.FullySatisfied();
        float sleepRestorePerHour = ActionCatalog.Sleep.RestoreAmount / ActionCatalog.Sleep.TemporalCostHours;
        float schoolRestorePerHour = ActionCatalog.School.RestoreAmount / ActionCatalog.School.TemporalCostHours;
        float workRestorePerHour = ActionCatalog.LegalWork.RestoreAmount / ActionCatalog.LegalWork.TemporalCostHours;
        for (int h = 0; h < 6; h++) // School
        {
            expected.Restore(NeedType.Hunger, schoolRestorePerHour);
            expected = NeedsEngine.DecayHour(expected, ActionCatalog.School.Environment, NeedsEngine.StressModifierFor(0f));
        }
        for (int h = 0; h < 4 + 3; h++) // Practice + Game
        {
            expected = NeedsEngine.DecayHour(expected, ActionCatalog.Idle.Environment, NeedsEngine.StressModifierFor(0f));
        }
        for (int h = 0; h < 3; h++) // Work
        {
            expected.Restore(NeedType.Hunger, workRestorePerHour);
            expected = NeedsEngine.DecayHour(expected, ActionCatalog.LegalWork.Environment, NeedsEngine.StressModifierFor(0f));
        }
        for (int h = 0; h < 8; h++) // Sleep
        {
            expected.Restore(NeedType.Sleep, sleepRestorePerHour);
            expected = NeedsEngine.DecayHour(expected, ActionCatalog.Sleep.Environment, NeedsEngine.StressModifierFor(0f));
        }
        planned.TryGetNeeds(AvatarId, out NeedsState actual);
        bool blocksExact = true;
        foreach (NeedType need in AllNeeds)
        {
            if (actual.Get(need) != expected.Get(need))
            {
                blocksExact = false;
            }
        }
        Check("fully-allocated day matches the closed-form block math bit-exactly (School/Work meal access + income, sleep env, inert neutral)",
            blocksExact, $"Sleep {actual.Sleep:F2}, Hunger {actual.Hunger:F2}");
        Check("the plan is one-shot (consumed by the day it ran)", !planned.HasTodaySchedule);

        // Day 1 isn't a cost-of-living cadence day (1 % 7 != 0), so Work's 3h
        // of LegalWork income is the ONLY funds-affecting event this fully-
        // allocated day (no free hours, so no autopilot spending either) —
        // an exact, noise-free arithmetic check.
        planned.TryGetFunds(AvatarId, out double actualFunds);
        double expectedFunds = SeedFunds + 3 * (-ActionCatalog.LegalWork.FinancialCost / ActionCatalog.LegalWork.TemporalCostHours);
        Check("fully-allocated day: Work block income lands exactly (3h x $10/hr, no cadence/free-hour noise)",
            actualFunds == expectedFunds, $"{actualFunds:F2} vs expected {expectedFunds:F2}");

        // --- Guarantee 2b: a scripted week of manual plans + free-hour autopilot ---
        // 19h planned (8 Sleep + 6 School + 2 Practice + 3 Game), 5h free for
        // the autopilot to feed/wash the avatar. The bystander NPC in the same
        // world must stay bit-identical to the no-avatar control world.
        // Calm world (stress 0): the stress path is the 30-day check's job,
        // and a ~1.7× stress decay multiplier would drown what this check
        // measures — whether the free evening hours cover the basics at all.
        (LifeSimManager weekControl, EventBus weekControlBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: false);
        (LifeSimManager week, EventBus weekBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        week.AvatarSchoolAvailable = true;

        var weekEndMin = new Dictionary<NeedType, float>();
        foreach (NeedType need in AllNeeds)
        {
            weekEndMin[need] = NeedsEngine.MaxNeed;
        }
        bool oneShotAllWeek = true;
        bool bystanderUntouched = true;
        float weekHungerSum = 0f;
        for (int day = 1; day <= 7; day++)
        {
            week.SetTodaySchedule(new DaySchedule(8, 6, 2, 3, 0));
            weekBus.Publish(new DayAdvancedEvent(day, 2026, day));
            weekBus.DispatchPending();
            weekControlBus.Publish(new DayAdvancedEvent(day, 2026, day));
            weekControlBus.DispatchPending();

            oneShotAllWeek &= !week.HasTodaySchedule;
            bystanderUntouched &= PersonStateIdentical(weekControl, week, BystanderId);
            week.TryGetNeeds(AvatarId, out NeedsState avatarNeeds);
            foreach (NeedType need in AllNeeds)
            {
                weekEndMin[need] = Math.Min(weekEndMin[need], avatarNeeds.Get(need));
            }
            weekHungerSum += avatarNeeds.Hunger;
        }
        Console.WriteLine("  Scripted-week avatar (8 Sleep/6 School/2 Practice/3 Game + 5 free) — worst END-OF-DAY value per need:");
        foreach (NeedType need in AllNeeds)
        {
            Console.WriteLine($"    {need,-8} min {weekEndMin[need],6:F1}");
        }
        Console.WriteLine();

        Check("scripted week: plan re-set daily, consumed daily", oneShotAllWeek);
        Check("scripted week: bystander NPC in the same world stays bit-identical to the no-avatar control",
            bystanderUntouched);
        // End-of-day (post-night-cap) state is the "does the plan work" bar:
        // Sleep must end every day restored well clear of critical (the 8h
        // block's whole point), and Hygiene must stay off-critical via
        // free-hour Showers (passive Hygiene floors at 0 by 72h — staying up
        // proves the evening autopilot washed the avatar). Social/Fitness
        // still ride unasserted (out of scope).
        Check("scripted week: every day ends with Sleep well restored (night cap did its job)",
            weekEndMin[NeedType.Sleep] > 2f * NeedsEngine.CriticalThreshold,
            $"worst end-of-day Sleep {weekEndMin[NeedType.Sleep]:F1}");
        Check("scripted week: Hygiene stays off-critical all week (evening autopilot Showers happened)",
            weekEndMin[NeedType.Hygiene] > NeedsEngine.CriticalThreshold,
            $"worst end-of-day Hygiene {weekEndMin[NeedType.Hygiene]:F1}");

        // Hunger under THIS heavily-committed plan (19h/24h, only 5 free
        // hours for autopilot Eat) still floors by week's end even with 8a's
        // meal access — Hunger's own decay curve floors within 24h of PURE
        // PASSIVE neglect (see the very first table this harness prints), so
        // no "modest" restore was ever going to fully prevent flooring under
        // a comparably packed day; that's a genuine, disclosed tuning
        // reality, not an engine defect (same conclusion 9b originally
        // reached, now re-confirmed after actually trying the fix). What 8a
        // DOES prove: meal access measurably helps. A/B control below runs
        // the IDENTICAL 19-committed/5-free split with School's 6h reassigned
        // to inert Practice instead (same autopilot noise, same everything
        // else) — an apples-to-apples isolation of School's meal access alone.
        (LifeSimManager weekNoMeal, EventBus weekNoMealBus) =
            NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        float noMealHungerSum = 0f;
        for (int day = 1; day <= 7; day++)
        {
            weekNoMeal.SetTodaySchedule(new DaySchedule(8, 0, 8, 3, 0));
            weekNoMealBus.Publish(new DayAdvancedEvent(day, 2026, day));
            weekNoMealBus.DispatchPending();
            weekNoMeal.TryGetNeeds(AvatarId, out NeedsState noMealNeeds);
            noMealHungerSum += noMealNeeds.Hunger;
        }
        Check("scripted week: School's meal access leaves Hunger measurably higher than an equivalent all-inert schedule (8a fix; sum of end-of-day Hunger across the week)",
            weekHungerSum > noMealHungerSum,
            $"meal-access sum {weekHungerSum:F1} vs inert-control sum {noMealHungerSum:F1}");
        // Work is 0 in this fixture's schedule, so School (meal access, no
        // funds effect) is the only block-side funds touch here — still $0.
        // Falling funds are therefore exact proof the free evening hours ran
        // the utility loop rather than idling silently.
        week.TryGetFunds(AvatarId, out double weekEndFunds);
        Check("scripted week: avatar funds fell (free-hour autopilot really acted — School doesn't spend/earn, Work is 0 this week)",
            weekEndFunds < SeedFunds, $"{SeedFunds:F0} -> {weekEndFunds:F0}");
    }

    /// <summary>Two-person world for the clock checks: avatar + bystander, both stress-seeded, optionally avatar-marked.</summary>
    private static (LifeSimManager, EventBus) NewClockWorld(
        string avatarId, string bystanderId, double funds, float stress, bool markAvatar)
    {
        var bus = new EventBus();
        var manager = new LifeSimManager();
        manager.Seed(new[] { new NpcSeed(avatarId, funds), new NpcSeed(bystanderId, funds) });
        manager.SetStress(avatarId, stress);
        manager.SetStress(bystanderId, stress);
        if (markAvatar)
        {
            manager.SetAvatar(avatarId);
        }
        manager.AttachTo(bus);
        return (manager, bus);
    }

    /// <summary>Float-exact needs + stress + funds equality for one person across two managers.</summary>
    private static bool PersonStateIdentical(LifeSimManager a, LifeSimManager b, string playerId)
    {
        a.TryGetNeeds(playerId, out NeedsState needsA);
        b.TryGetNeeds(playerId, out NeedsState needsB);
        foreach (NeedType need in AllNeeds)
        {
            if (needsA.Get(need) != needsB.Get(need))
            {
                return false;
            }
        }
        a.TryGetStress(playerId, out float stressA);
        b.TryGetStress(playerId, out float stressB);
        a.TryGetFunds(playerId, out double fundsA);
        b.TryGetFunds(playerId, out double fundsB);
        return stressA == stressB && fundsA == fundsB;
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    // ------------------------------------------------------------------
    // Phase 8a: survival economy solvency — the interleave plan's explicit
    // "funds-solvency check over a simulated month" ask. Two scripted
    // avatars (each its own LifeSimManager/EventBus world, mirroring
    // RunDailyClockChecks' NewClockWorld helper) from CareerManager's real
    // starting funds ($500 — this harness stays Baseball-free, so the value
    // is a literal, not a reference): one schedules Work daily, one doesn't.
    // ------------------------------------------------------------------

    private const double SurvivalStartingFunds = 500.0; // mirrors CareerManager.StartingFunds

    private static void RunSurvivalEconomyChecks()
    {
        Console.WriteLine("--- 8a survival economy: 30-day funds solvency (Work vs no Work) ---\n");

        const int SimulatedDays = 30;

        (LifeSimManager worker, EventBus workerBus) =
            NewClockWorld("working-avatar", "bystander-a", SurvivalStartingFunds, stress: 0f, markAvatar: true);
        (LifeSimManager idler, EventBus idlerBus) =
            NewClockWorld("idle-avatar", "bystander-b", SurvivalStartingFunds, stress: 0f, markAvatar: true);

        for (int day = 1; day <= SimulatedDays; day++)
        {
            worker.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 8)); // 8 Sleep + 8 Work + 8 free
            workerBus.Publish(new DayAdvancedEvent(day, 2026, day));
            workerBus.DispatchPending();

            idler.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 0)); // 8 Sleep + 16 free, no Work
            idlerBus.Publish(new DayAdvancedEvent(day, 2026, day));
            idlerBus.DispatchPending();
        }

        worker.TryGetFunds("working-avatar", out double workerFunds);
        idler.TryGetFunds("idle-avatar", out double idlerFunds);
        Console.WriteLine($"  Working avatar (8 Sleep/8 Work/8 free): ${SurvivalStartingFunds:F0} -> ${workerFunds:F0} over {SimulatedDays} days");
        Console.WriteLine($"  Idle avatar (8 Sleep/16 free, no Work):  ${SurvivalStartingFunds:F0} -> ${idlerFunds:F0} over {SimulatedDays} days");
        Console.WriteLine();

        Check("30-day solvency: a working avatar's funds GROW (Legal Work income outpaces rent + autopilot spend)",
            workerFunds > SurvivalStartingFunds, $"{SurvivalStartingFunds:F0} -> {workerFunds:F0}");
        Check("30-day solvency: a non-working avatar's funds SHRINK (recurring cost-of-living + autopilot spend, no income)",
            idlerFunds < SurvivalStartingFunds, $"{SurvivalStartingFunds:F0} -> {idlerFunds:F0}");
        Check("30-day solvency: working clearly beats not working",
            workerFunds > idlerFunds, $"worker {workerFunds:F0} vs idler {idlerFunds:F0}");
    }

    // ------------------------------------------------------------------
    // Household board (docs/design/household_board.md): while a covered
    // high-schooler, the family pays a wealth-tier share of Eat's cost and
    // the weekly cost-of-living bill — and nothing else. Every OTHER check
    // in this harness runs at the -1 no-coverage default, which is the
    // slice's byte-identity guarantee; these drive the covered tiers.
    // ------------------------------------------------------------------

    private static void RunHouseholdBoardChecks()
    {
        Console.WriteLine("--- household board: family covers a wealth-tier share of meals + cost of living ---\n");

        // Table pins — the design doc's §2 table cell by cell, plus the -1
        // sentinel and out-of-range fallback every harness world relies on.
        double[] expectedShares = { 1.0, 0.5, 0.0, 0.0, 0.0 };
        bool tableExact = HouseholdBoard.BoardShareByTier.Length == expectedShares.Length;
        for (int tier = 0; tableExact && tier < expectedShares.Length; tier++)
        {
            tableExact = HouseholdBoard.BoardShareByTier[tier] == expectedShares[tier]
                && HouseholdBoard.ShareFor(tier) == expectedShares[tier];
        }
        Check("BoardShareByTier pins the §2 table (1.0 / 0.5 / 0 / 0 / 0)", tableExact);
        Check("ShareFor(-1) and ShareFor(5) both fall back to 1.0 (full price — the byte-identity default)",
            HouseholdBoard.ShareFor(-1) == 1.0 && HouseholdBoard.ShareFor(5) == 1.0);

        // Decision-side: the shipped broke-crisis fixture (RunUtilityChecks #3)
        // picks PickArgument because Eat is unaffordable at $0. A covered
        // kid's meal is free — same needs, same $0 — and the pick flips to
        // Eat: he raids the fridge instead of picking a fight.
        NeedsState hungerCritical = NeedsState.FullySatisfied();
        hungerCritical.Set(NeedType.Hunger, 10f);
        NpcActionId coveredBrokePick = UtilityCalculator.SelectAction(
            hungerCritical, 0.0, UtilityCalculator.DefaultWeights, out _, 0f, eatCostShare: 0.0);
        Check("broke + hungry + meals covered picks Eat (the shipped broke-crisis fixture flips)",
            coveredBrokePick == NpcActionId.Eat, $"picked {coveredBrokePick}");

        // Execution-side, the meal debit: 8 Sleep + 15 Practice + exactly one
        // free hour, avatar pinned hunger-critical, so that hour is a forced
        // Eat in every world — and the day's ONLY spend (sleep/practice are
        // free, day 1 carries no bill), so the funds delta IS the meal price.
        double fullMeal = MealDayDelta(-1);
        double halfMeal = MealDayDelta(1);
        double freeMeal = MealDayDelta(2);
        Check("one forced meal debits x1.0 / x0.5 / x0.0 by tier (-12 / -6 / 0 exactly)",
            fullMeal == -12.0 && halfMeal == -6.0 && freeMeal == 0.0,
            $"tier -1: {fullMeal:F2}, tier 1: {halfMeal:F2}, tier 2: {freeMeal:F2}");

        // Execution-side, the weekly bill: 24h fully booked for 7 days (no
        // free hours, no autopilot spend, no income) — the only funds
        // movement is the day-7 cost-of-living debit, scaled by the share.
        double fullBill = BilledWeekDelta(-1);
        double halfBill = BilledWeekDelta(1);
        double freeBill = BilledWeekDelta(2);
        Check("the weekly bill debits x1.0 / x0.5 / x0.0 by tier (-70 / -35 / 0 exactly)",
            fullBill == -LifeSimManager.WeeklyCostOfLiving
            && halfBill == -LifeSimManager.WeeklyCostOfLiving * 0.5 && freeBill == 0.0,
            $"tier -1: {fullBill:F2}, tier 1: {halfBill:F2}, tier 2: {freeBill:F2}");

        // AvatarWeeklyCostOfLiving is the number the Bank tab renders.
        (LifeSimManager probe, EventBus _) =
            NewClockWorld("board-avatar", "board-bystander", 500.0, stress: 0f, markAvatar: true);
        double defaultBill = probe.AvatarWeeklyCostOfLiving;
        probe.AvatarBoardWealthTier = 1;
        double sharedBill = probe.AvatarWeeklyCostOfLiving;
        probe.AvatarBoardWealthTier = 4;
        double coveredBill = probe.AvatarWeeklyCostOfLiving;
        Check("AvatarWeeklyCostOfLiving tracks the tier (70 default, 35 shared, 0 covered)",
            defaultBill == LifeSimManager.WeeklyCostOfLiving
            && sharedBill == LifeSimManager.WeeklyCostOfLiving * 0.5 && coveredBill == 0.0,
            $"default {defaultBill:F0}, tier 1 {sharedBill:F0}, tier 4 {coveredBill:F0}");

        // The board rule must never leak into the background population: a
        // bystander NPC's fortnight is byte-identical whether the avatar's
        // household covers everything or nothing.
        (LifeSimManager coveredWorld, EventBus coveredBus) =
            NewClockWorld("board-avatar-b", "board-bystander-b", 500.0, stress: 0f, markAvatar: true);
        coveredWorld.AvatarBoardWealthTier = 4;
        (LifeSimManager defaultWorld, EventBus defaultBus) =
            NewClockWorld("board-avatar-b", "board-bystander-b", 500.0, stress: 0f, markAvatar: true);
        for (int day = 1; day <= 14; day++)
        {
            coveredBus.Publish(new DayAdvancedEvent(day, 2026, day));
            coveredBus.DispatchPending();
            defaultBus.Publish(new DayAdvancedEvent(day, 2026, day));
            defaultBus.DispatchPending();
        }
        Check("a bystander NPC's fortnight is byte-identical under tier 4 vs no coverage",
            PersonStateIdentical(coveredWorld, defaultWorld, "board-bystander-b"));

        Console.WriteLine();
    }

    /// <summary>Funds delta of the one-forced-meal day (see the meal-debit check) for a given board tier.</summary>
    private static double MealDayDelta(int wealthTier)
    {
        const double StartFunds = 200.0;
        (LifeSimManager world, EventBus bus) =
            NewClockWorld("meal-avatar", "meal-bystander", StartFunds, stress: 0f, markAvatar: true);
        world.AvatarBoardWealthTier = wealthTier;
        NeedsState needs = NeedsState.FullySatisfied();
        needs.Set(NeedType.Hunger, 10f);
        world.SetNeeds("meal-avatar", needs);
        world.SetTodaySchedule(new DaySchedule(8, 0, 15, 0, 0)); // 8 Sleep + 15 Practice + 1 free hour
        bus.Publish(new DayAdvancedEvent(1, 2026, 1));
        bus.DispatchPending();
        world.TryGetFunds("meal-avatar", out double funds);
        return funds - StartFunds;
    }

    // ------------------------------------------------------------------
    // Sleep bands (SleepProfile, user-directed 2026-07-12): a planned night
    // under 6h frays the avatar, exactly 8h grants the boost, hours past 8
    // bank nothing. Applies only in TickScheduledDay — avatar-only by
    // construction — so every autopilot/NPC trace is untouched.
    // ------------------------------------------------------------------

    private static void RunSleepBandChecks()
    {
        Console.WriteLine("--- sleep bands: 2h floor, <6 hurts, 8 boosts, >8 banks nothing ---\n");

        Check("SleepProfile pins: floor 2, healthy-min 6, optimal 8",
            SleepProfile.MinPlannedSleepHours == 2 && SleepProfile.HealthyMinHours == 6
            && SleepProfile.OptimalHours == 8);
        Check("SleepProfile effect pins: short +8 stress / -1 mood, 8h -4 stress / +1 mood, oversleep -1 mood",
            SleepProfile.ShortNightStress == 8f && SleepProfile.ShortNightHappinessDelta == -1f
            && SleepProfile.GoodNightStressRelief == 4f && SleepProfile.GoodNightHappinessDelta == 1f
            && SleepProfile.OversleepHappinessDelta == -1f);

        // Oversleep is a block-only definition: Sleep's exact restful 0.8
        // environment, zero restore, and absent from the autopilot scan.
        bool inAll = false;
        foreach (NpcActionDefinition def in ActionCatalog.All)
        {
            inAll |= def.Id == NpcActionId.Oversleep;
        }
        NpcActionDefinition oversleep = ActionCatalog.Get(NpcActionId.Oversleep);
        Check("Oversleep: zero restore, Sleep's 0.8 environment, never autopilot-selectable",
            !inAll && oversleep.PrimaryNeed is null && oversleep.RestoreAmount == 0f
            && oversleep.Environment.Sleep == 0.8f && oversleep.Environment.Hunger == 0.8f);

        // Stress bands, exactly: four worlds identical except the night —
        // fully booked days (sleep + practice = 24, zero free hours) so no
        // autopilot noise, day 1 so no bill. Every world relaxes stress the
        // same 24 hourly steps; the band is the only difference, so the
        // deltas are float-exact.
        float shortStress = PlannedNightStress(4, 20);
        float neutralStress = PlannedNightStress(7, 17);
        float bestStress = PlannedNightStress(8, 16);
        Check("a 4h night ends exactly +8 stress over a neutral 7h night",
            shortStress == neutralStress + SleepProfile.ShortNightStress,
            $"4h {shortStress:F2} vs 7h {neutralStress:F2}");
        Check("an 8h night ends exactly -4 stress under a neutral 7h night",
            bestStress == neutralStress - SleepProfile.GoodNightStressRelief,
            $"8h {bestStress:F2} vs 7h {neutralStress:F2}");

        // Mood bands, directional (the daily reversion pulls part-way back,
        // so exact values would pin PersonDrift, not the band): hydrated at
        // the neutral 50 setpoint, one planned day.
        float shortMood = PlannedNightHappiness(4, 20);
        float neutralMood = PlannedNightHappiness(7, 17);
        float bestMood = PlannedNightHappiness(8, 16);
        float overMood = PlannedNightHappiness(10, 14);
        Check("mood: short night < neutral, 8h > neutral, oversleep < neutral",
            shortMood < neutralMood && bestMood > neutralMood && overMood < neutralMood,
            $"4h {shortMood:F2}, 7h {neutralMood:F2}, 8h {bestMood:F2}, 10h {overMood:F2}");

        // Restore cap: hours past 8 bank nothing — under the OLD semantics a
        // 10h block restored +100 and would end strictly better-rested than
        // an 8h block; now the extra hours are pure Oversleep (zero restore,
        // decay still ticking), so a 10h night can never end better-rested
        // than an 8h one. (Measured: it ends slightly WORSE — the 8h block
        // already reaches the cap, and the extra hours only decay from it.)
        float sleep8 = PlannedNightSleepNeed(8, 16);
        float sleep10 = PlannedNightSleepNeed(10, 14);
        Check("hours past 8 bank no restore (a 10h night never ends better-rested than an 8h one)",
            sleep10 <= sleep8,
            $"8h ends Sleep {sleep8:F2}, 10h ends {sleep10:F2}");

        // The bands never leak into the background population: the bystander
        // NPC's day is byte-identical whatever the avatar planned.
        (LifeSimManager shortWorld, EventBus shortBus) =
            NewClockWorld("sleep-avatar", "sleep-bystander", 500.0, stress: 30f, markAvatar: true);
        shortWorld.SetTodaySchedule(new DaySchedule(4, 0, 20, 0, 0));
        shortBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        shortBus.DispatchPending();
        (LifeSimManager bestWorld, EventBus bestBus) =
            NewClockWorld("sleep-avatar", "sleep-bystander", 500.0, stress: 30f, markAvatar: true);
        bestWorld.SetTodaySchedule(new DaySchedule(8, 0, 16, 0, 0));
        bestBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        bestBus.DispatchPending();
        Check("bystander NPC byte-identical under a 4h vs 8h avatar night",
            PersonStateIdentical(shortWorld, bestWorld, "sleep-bystander"));

        Console.WriteLine();
    }

    /// <summary>End-of-day avatar stress after one planned day of sleepHours + practiceHours (must sum to 24), start stress 30.</summary>
    private static float PlannedNightStress(int sleepHours, int practiceHours)
    {
        (LifeSimManager world, EventBus bus) =
            NewClockWorld("night-avatar", "night-bystander", 500.0, stress: 30f, markAvatar: true);
        world.SetTodaySchedule(new DaySchedule(sleepHours, 0, practiceHours, 0, 0));
        bus.Publish(new DayAdvancedEvent(1, 2026, 1));
        bus.DispatchPending();
        world.TryGetStress("night-avatar", out float stress);
        return stress;
    }

    /// <summary>End-of-day avatar happiness (hydrated neutral 50) after one planned day of sleepHours + practiceHours.</summary>
    private static float PlannedNightHappiness(int sleepHours, int practiceHours)
    {
        (LifeSimManager world, EventBus bus) =
            NewClockWorld("night-avatar", "night-bystander", 500.0, stress: 0f, markAvatar: true);
        world.SetPersonStats("night-avatar", PersonStats.Neutral());
        world.SetTodaySchedule(new DaySchedule(sleepHours, 0, practiceHours, 0, 0));
        bus.Publish(new DayAdvancedEvent(1, 2026, 1));
        bus.DispatchPending();
        world.TryGetPersonStats("night-avatar", out PersonStats stats);
        return stats.Happiness;
    }

    /// <summary>End-of-day avatar Sleep need after one planned day of sleepHours + practiceHours.</summary>
    private static float PlannedNightSleepNeed(int sleepHours, int practiceHours)
    {
        (LifeSimManager world, EventBus bus) =
            NewClockWorld("night-avatar", "night-bystander", 500.0, stress: 0f, markAvatar: true);
        world.SetTodaySchedule(new DaySchedule(sleepHours, 0, practiceHours, 0, 0));
        bus.Publish(new DayAdvancedEvent(1, 2026, 1));
        bus.DispatchPending();
        world.TryGetNeeds("night-avatar", out NeedsState needs);
        return needs.Sleep;
    }

    /// <summary>Funds delta of a fully-booked 7-day week (see the weekly-bill check) for a given board tier.</summary>
    private static double BilledWeekDelta(int wealthTier)
    {
        const double StartFunds = 500.0;
        (LifeSimManager world, EventBus bus) =
            NewClockWorld("bill-avatar", "bill-bystander", StartFunds, stress: 0f, markAvatar: true);
        world.AvatarBoardWealthTier = wealthTier;
        for (int day = 1; day <= 7; day++)
        {
            world.SetTodaySchedule(new DaySchedule(8, 0, 16, 0, 0)); // 24h booked — zero free hours
            bus.Publish(new DayAdvancedEvent(day, 2026, day));
            bus.DispatchPending();
        }
        world.TryGetFunds("bill-avatar", out double funds);
        return funds - StartFunds;
    }

    // ------------------------------------------------------------------
    // Phase 6: RelationshipGraph — canonical storage, clamping, rivalry
    // derivation, event transport, and the persistence dirty-set. All local
    // (graph + a hand-pumped EventBus), no database — the DB projection is
    // GameManager's boundary, proven by the live boot smoke test.
    // ------------------------------------------------------------------

    private static void RunRelationshipGraphChecks()
    {
        // Canonical pair storage: written (b, a), readable either way, and
        // visible from both endpoints' adjacency.
        var graph = new RelationshipGraph();
        graph.SetRelationship("npc-b", "npc-a", 40, RelationshipKind.Friend);
        bool foundForward = graph.TryGetRelationship("npc-a", "npc-b", out int affinity, out RelationshipKind kind);
        bool foundReverse = graph.TryGetRelationship("npc-b", "npc-a", out int affinityReverse, out _);
        Check("edge written (b,a) is readable as (a,b) and (b,a) with one stored value",
            foundForward && foundReverse && affinity == 40 && affinityReverse == 40 && kind == RelationshipKind.Friend);

        var edges = new List<RelationshipEdge>();
        graph.GetEdgesFor("npc-a", edges);
        bool aSeesB = edges.Count == 1 && edges[0].OtherId == "npc-b";
        graph.GetEdgesFor("npc-b", edges);
        bool bSeesA = edges.Count == 1 && edges[0].OtherId == "npc-a";
        Check("adjacency is bidirectional (each endpoint sees the other)", aSeesB && bSeesA);

        // Affinity clamps to [-100, 100] on set and on adjust.
        graph.SetRelationship("npc-a", "npc-c", 250, RelationshipKind.Partner);
        graph.TryGetRelationship("npc-a", "npc-c", out int clampedHigh, out _);
        graph.AdjustAffinity("npc-a", "npc-c", -999);
        graph.TryGetRelationship("npc-a", "npc-c", out int clampedLow, out _);
        Check($"affinity clamps to [{RelationshipGraph.MinAffinity}, {RelationshipGraph.MaxAffinity}]",
            clampedHigh == RelationshipGraph.MaxAffinity && clampedLow == RelationshipGraph.MinAffinity,
            $"set 250 → {clampedHigh}, adjust -999 → {clampedLow}");

        // Rivalry intensity derivation: only a Rival edge in the red projects.
        Check("rivalry intensity = -affinity on a negative Rival edge, else 0",
            RelationshipGraph.RivalryIntensity(-63, RelationshipKind.Rival) == 63
            && RelationshipGraph.RivalryIntensity(-63, RelationshipKind.Friend) == 0
            && RelationshipGraph.RivalryIntensity(20, RelationshipKind.Rival) == 0);

        // Event transport through a locally pumped bus.
        var bus = new EventBus();
        var received = new List<RivalryChangedEvent>();
        bus.Subscribe<RivalryChangedEvent>(e => received.Add(e));

        graph.AttachTo(bus);
        bus.DispatchPending();
        Check("AttachTo announces nothing when no rivalry exists", received.Count == 0);

        graph.SetRelationship("npc-a", "npc-b", -80, RelationshipKind.Rival);
        bus.DispatchPending();
        Check("rivalry creation publishes canonical pair + intensity",
            received.Count == 1 && received[0].PlayerAId == "npc-a" && received[0].PlayerBId == "npc-b"
            && received[0].Intensity == 80);

        graph.AdjustAffinity("npc-a", "npc-c", 5); // non-rival edge moves — no rivalry traffic
        graph.SetRelationship("npc-a", "npc-b", -80, RelationshipKind.Rival); // exact no-op write
        bus.DispatchPending();
        Check("non-rival changes and no-op writes publish nothing", received.Count == 1);

        graph.SetRelationship("npc-a", "npc-b", 15, RelationshipKind.Rival); // buried the hatchet
        bus.DispatchPending();
        Check("affinity sign flip dissolves the rivalry (publishes intensity 0)",
            received.Count == 2 && received[1].Intensity == 0);

        // AttachTo on a pre-seeded graph announces hydrated rivalries — the
        // boot handshake GameManager relies on (Seed itself must stay silent).
        var hydrated = new RelationshipGraph();
        hydrated.Seed(new[]
        {
            new RelationshipSeed("npc-x", "npc-y", -45, RelationshipKind.Rival),
            new RelationshipSeed("npc-x", "npc-z", -45, RelationshipKind.Friend),
        });
        received.Clear();
        hydrated.AttachTo(bus);
        bus.DispatchPending();
        Check("AttachTo after Seed announces exactly the active rivalries",
            received.Count == 1 && received[0].Intensity == 45);

        // Persistence dirty-set: mutations accumulate once, Seed never dirties,
        // and CollectDirty drains.
        var dirty = new List<RelationshipSeed>();
        bool seedClean = hydrated.CollectDirty(dirty) == 0;
        graph.CollectDirty(dirty);
        // Every mutation above touched only the (a,b) and (a,c) pairs, however
        // many times — the dirty set coalesces to one entry per pair.
        bool mutationsCollected = dirty.Count == 2;
        bool drained = graph.CollectDirty(dirty) == 0;
        Check("CollectDirty: Seed is clean, mutations coalesce per pair, second call empty",
            seedClean && mutationsCollected && drained);
    }

    // ------------------------------------------------------------------
    // HS-4 person layer (high_school_person_layer.md §2.2/§2.3/§5.3): the
    // GPA closed form and its end-to-end drive, happiness reversion, the
    // person-stat effect channel, the delta/settle bridge bookkeeping, and
    // the transport-hours refund. The §9.1 acceptance hook's own suite.
    // ------------------------------------------------------------------

    private static void RunPersonLayerChecks()
    {
        Console.WriteLine("--- HS-4 person layer: GPA drift, happiness reversion, effect channel, transport refund ---\n");

        const string AvatarId = "hs-avatar";
        const string BystanderId = "hs-bystander";
        const double SeedFunds = 50000.0;

        // --- §2.2 closed form, pure ---
        Check("GPA closed form: the §2.2 neutral identity is EXACT (full attendance, 50/50, no study, no partner, no stress → 0.0)",
            PersonDrift.GpaWeeklyDelta(1f, 50f, 50f, 0f, 0f) == 0.0);
        Check("GPA closed form: full truancy costs exactly GpaBasePerWeek, regardless of aptitude (you can't ace classes you skip)",
            PersonDrift.GpaWeeklyDelta(0f, 50f, 50f, 0f, 0f) == -PersonDrift.GpaBasePerWeek
            && PersonDrift.GpaWeeklyDelta(0f, 100f, 100f, 0f, 0f) == -PersonDrift.GpaBasePerWeek);
        Check("GPA closed form: 100/100 aptitude at full attendance earns exactly +GpaBasePerWeek (symmetric with truancy)",
            PersonDrift.GpaWeeklyDelta(1f, 100f, 100f, 0f, 0f) == PersonDrift.GpaBasePerWeek);
        // Hand-computed week (the doc's §2.2 fixture mandate): int 70, disc 60,
        // attendance 24/30 = 0.8, 6h study, stress 25.
        //   aptitude = (0.5·20 + 0.5·10)/50 = 0.3
        //   Δ = 0.15·(0.8·1.3 − 1) + 0.01·6 − 0.10·0.25 = 0.006 + 0.06 − 0.025 = 0.041
        double handComputed = PersonDrift.GpaWeeklyDelta(0.8f, 70f, 60f, 6f, 25f);
        Check("GPA closed form: hand-computed mixed week lands on 0.041 (int 70 / disc 60 / 80% attendance / 6h study / stress 25)",
            Math.Abs(handComputed - 0.041) < 1e-6, $"Δ = {handComputed:F9}");
        // §2.3 reversion step: zero at the setpoint, proportional off it.
        Check("happiness reversion step: 0 at the setpoint, ~-4 for a +20 spike (rate 0.2/day)",
            PersonDrift.HappinessDailyStep(50f, 50f) == 0f
            && Math.Abs(PersonDrift.HappinessDailyStep(70f, 50f) + 4f) < 1e-4);

        // --- DaySchedule free-time block validation ---
        Check("DaySchedule free-time block: negative hours / >24h totals / non-free-time activity all throw; 0 hours normalizes the activity to Idle",
            Throws(() => new DaySchedule(8, 0, 0, 0, 0, -1, NpcActionId.Study))
            && Throws(() => new DaySchedule(10, 8, 4, 0, 0, 3, NpcActionId.Study)) // 25h
            && Throws(() => new DaySchedule(8, 0, 0, 0, 0, 2, NpcActionId.LegalWork))
            && Throws(() => new DaySchedule(8, 0, 0, 0, 0, 2, NpcActionId.Idle))
            && new DaySchedule(8, 0, 0, 0, 0, 0, NpcActionId.Study).FreeTimeActivity == NpcActionId.Idle);
        Check("free-time catalog: exactly Church/VideoGames/Study/Hangout qualify, and none is autopilot-selectable (absent from All)",
            ActionCatalog.IsFreeTimeActivity(NpcActionId.Church)
            && ActionCatalog.IsFreeTimeActivity(NpcActionId.VideoGames)
            && ActionCatalog.IsFreeTimeActivity(NpcActionId.Study)
            && ActionCatalog.IsFreeTimeActivity(NpcActionId.Hangout)
            && !ActionCatalog.IsFreeTimeActivity(NpcActionId.School)
            && Array.TrueForAll(ActionCatalog.All, def => !ActionCatalog.IsFreeTimeActivity(def.Id)));

        // --- §9.1 neutral bit-identity: hydrating the person layer (neutral
        // row, no free-time block, no transport) must change NOTHING about
        // needs/stress/funds over a mixed planned+autopilot fortnight. ---
        (LifeSimManager plain, EventBus plainBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        (LifeSimManager hydrated, EventBus hydratedBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        plain.AvatarSchoolAvailable = true;
        hydrated.AvatarSchoolAvailable = true;
        hydrated.SetPersonStats(AvatarId, PersonStats.Neutral());
        bool neutralIdentity = true;
        for (int day = 1; day <= 14; day++)
        {
            if (day <= 7)
            {
                // 7h nights: the sleep-band NEUTRAL hour count (SleepProfile),
                // so this fixture keeps isolating the §2.2 identity — an 8h
                // night now deliberately moves happiness (the boost).
                plain.SetTodaySchedule(new DaySchedule(7, 6, 0, 0, 0));
                hydrated.SetTodaySchedule(new DaySchedule(7, 6, 0, 0, 0));
            }
            plainBus.Publish(new DayAdvancedEvent(day, 2026, day));
            plainBus.DispatchPending();
            hydratedBus.Publish(new DayAdvancedEvent(day, 2026, day));
            hydratedBus.DispatchPending();
            neutralIdentity &= PersonStateIdentical(plain, hydrated, AvatarId)
                && PersonStateIdentical(plain, hydrated, BystanderId);
        }
        Check("§9.1 neutral bit-identity: a neutral-hydrated avatar's needs/stress/funds trace matches an unhydrated twin bit-for-bit over 14 mixed days",
            neutralIdentity);
        hydrated.TryGetPersonStats(AvatarId, out PersonStats afterFortnight);
        hydrated.TryPeekPersonDeltas(AvatarId, out PersonStats fortnightDeltas);
        Check("§2.2 neutral identity end-to-end: two neutral weeks (planned full attendance + pure autopilot) hold GPA at exactly 2.5, zero pending delta",
            afterFortnight.Gpa == 2.5 && fortnightDeltas.Gpa == 0.0
            && afterFortnight.Happiness == 50f && fortnightDeltas.Happiness == 0f,
            $"gpa {afterFortnight.Gpa:F9}, Δgpa {fortnightDeltas.Gpa:E2}");
        Check("unhydrated twin exposes no person surface (TryGet/TryPeek false — NPCs cost nothing)",
            !plain.TryGetPersonStats(AvatarId, out _) && !plain.TryPeekPersonDeltas(AvatarId, out _)
            && !hydrated.TryPeekPersonDeltas(BystanderId, out _));

        // --- Truancy end-to-end: a week of deliberate 0-school plans ---
        (LifeSimManager truant, EventBus truantBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        truant.AvatarSchoolAvailable = true;
        truant.SetPersonStats(AvatarId, PersonStats.Neutral());
        for (int day = 1; day <= 7; day++)
        {
            truant.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 0));
            truantBus.Publish(new DayAdvancedEvent(day, 2026, day));
            truantBus.DispatchPending();
        }
        truant.TryGetPersonStats(AvatarId, out PersonStats truantStats);
        double truantExpected = 2.5 + PersonDrift.GpaWeeklyDelta(0f, 50f, 50f, 0f, 0f);
        Check("truancy end-to-end: seven planned 0-school days cost exactly one GpaBasePerWeek (planning 0 school = deliberate truancy; autopilot days would have attended)",
            truantStats.Gpa == truantExpected, $"gpa {truantStats.Gpa:F4} vs {truantExpected:F4}");

        // --- Study payoff end-to-end: full attendance + 10h of Study evenings ---
        (LifeSimManager scholar, EventBus scholarBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        scholar.AvatarSchoolAvailable = true;
        scholar.SetPersonStats(AvatarId, PersonStats.Neutral());
        for (int day = 1; day <= 7; day++)
        {
            if (day <= 5)
            {
                // 7h = the sleep-band neutral count — isolates Study's own
                // happiness cost from the 8h-night boost.
                scholar.SetTodaySchedule(new DaySchedule(7, 6, 0, 0, 0, 2, NpcActionId.Study));
            }
            scholarBus.Publish(new DayAdvancedEvent(day, 2026, day));
            scholarBus.DispatchPending();
        }
        scholar.TryGetPersonStats(AvatarId, out PersonStats scholarStats);
        double scholarExpected = 2.5 + PersonDrift.GpaWeeklyDelta(1f, 50f, 50f, 10f, 0f);
        Check("study end-to-end: 5 school days + 2 autopilot days + 10 Study hours land exactly the StudyHoursTerm (+0.1)",
            scholarStats.Gpa == scholarExpected, $"gpa {scholarStats.Gpa:F4} vs {scholarExpected:F4}");
        Check("study's happiness cost moved the stat (the §2.1 grind trade-off)", scholarStats.Happiness < 50f,
            $"happiness {scholarStats.Happiness:F2}");

        // --- Aptitude drift + the school gate + stress drag ---
        var gifted = PersonStats.Neutral();
        gifted.Intelligence = 100f;
        gifted.Discipline = 100f;
        (LifeSimManager honors, EventBus honorsBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        honors.AvatarSchoolAvailable = true;
        honors.SetPersonStats(AvatarId, gifted);
        (LifeSimManager pro, EventBus proBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        pro.AvatarSchoolAvailable = false; // post-graduation tier — GPA freezes
        var struggling = PersonStats.Neutral();
        struggling.Intelligence = 0f;
        struggling.Discipline = 0f;
        pro.SetPersonStats(AvatarId, struggling);
        (LifeSimManager frayed, EventBus frayedBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 80f, markAvatar: true);
        frayed.AvatarSchoolAvailable = true;
        frayed.SetPersonStats(AvatarId, PersonStats.Neutral());
        for (int day = 1; day <= 7; day++)
        {
            honorsBus.Publish(new DayAdvancedEvent(day, 2026, day));
            honorsBus.DispatchPending();
            proBus.Publish(new DayAdvancedEvent(day, 2026, day));
            proBus.DispatchPending();
            if (day == 7)
            {
                // The seed-80 arc fully relaxes/relieves away in ~2 days
                // (passive 9.6/day + crisis relief), so re-fray right before
                // the weekly tick — the point under test is that the drag
                // samples whatever the scalar IS at tick time.
                frayed.SetStress(AvatarId, 80f);
            }
            frayedBus.Publish(new DayAdvancedEvent(day, 2026, day));
            frayedBus.DispatchPending();
        }
        honors.TryGetPersonStats(AvatarId, out PersonStats honorsStats);
        Check("aptitude drift: a 100/100 autopilot week earns exactly +GpaBasePerWeek",
            honorsStats.Gpa == 2.5 + PersonDrift.GpaBasePerWeek, $"gpa {honorsStats.Gpa:F4}");
        pro.TryGetPersonStats(AvatarId, out PersonStats proStats);
        pro.TryPeekPersonDeltas(AvatarId, out PersonStats proDeltas);
        Check("school gate: with AvatarSchoolAvailable false (pro tiers) GPA freezes — even for a 0/0 aptitude",
            proStats.Gpa == 2.5 && proDeltas.Gpa == 0.0, $"gpa {proStats.Gpa:F4}");
        frayed.TryGetPersonStats(AvatarId, out PersonStats frayedStats);
        frayed.TryGetStress(AvatarId, out float frayedEndStress);
        double frayedExpected = 2.5 + PersonDrift.GpaWeeklyDelta(1f, 50f, 50f, 0f, frayedEndStress);
        Check("stress drag: sampled AT the weekly tick (post-relaxation), full-attendance neutral week loses exactly the drag term",
            frayedStats.Gpa == frayedExpected && frayedStats.Gpa < 2.5,
            $"gpa {frayedStats.Gpa:F6}, end stress {frayedEndStress:F1}");

        // --- Effect channel: a VideoGames evening, ops replicated exactly ---
        (LifeSimManager gamer, EventBus gamerBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        gamer.SetPersonStats(AvatarId, PersonStats.Neutral());
        // 7h = sleep-band neutral, keeping the op-for-op expectation pure.
        gamer.SetTodaySchedule(new DaySchedule(7, 0, 0, 0, 0, 2, NpcActionId.VideoGames));
        gamerBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        gamerBus.DispatchPending();
        float expHappiness = 50f, expDiscipline = 50f, expUnflushedH = 0f, expUnflushedD = 0f;
        for (int h = 0; h < 2; h++)
        {
            float movedH = Math.Clamp(expHappiness + 1.0f, 0f, 100f) - expHappiness;
            expHappiness += movedH;
            expUnflushedH += movedH;
            float movedD = Math.Clamp(expDiscipline - 0.2f, 0f, 100f) - expDiscipline;
            expDiscipline += movedD;
            expUnflushedD += movedD;
        }
        float reversion = PersonDrift.HappinessDailyStep(expHappiness, 50f);
        float movedRev = Math.Clamp(expHappiness + reversion, 0f, 100f) - expHappiness;
        expHappiness += movedRev;
        expUnflushedH += movedRev;
        gamer.TryGetPersonStats(AvatarId, out PersonStats gamerStats);
        gamer.TryPeekPersonDeltas(AvatarId, out PersonStats gamerDeltas);
        Check("effect channel: a 2h VideoGames evening applies +1.0 happiness/-0.2 discipline per hour, then the daily reversion — op-for-op exact",
            gamerStats.Happiness == expHappiness && gamerStats.Discipline == expDiscipline
            && gamerDeltas.Happiness == expUnflushedH && gamerDeltas.Discipline == expUnflushedD,
            $"happiness {gamerStats.Happiness:F4} (Δ{gamerDeltas.Happiness:F4}), discipline {gamerStats.Discipline:F4}");

        // --- Settle bookkeeping: whole parts persist, fractions carry ---
        var applied = default(PersonStats);
        applied.Happiness = (int)gamerDeltas.Happiness; // what the bridge would persist
        gamer.SettlePersonDeltas(AvatarId, in applied);
        gamer.TryPeekPersonDeltas(AvatarId, out PersonStats settled);
        Check("settle bookkeeping: persisting the whole part leaves exactly the fraction banked (nothing rounds away)",
            settled.Happiness == expUnflushedH - (int)expUnflushedH
            && settled.Discipline == expUnflushedD,
            $"happiness remainder {settled.Happiness:F4}, discipline untouched {settled.Discipline:F4}");

        // --- Clamp bookkeeping at the ceiling ---
        var nearCap = PersonStats.Neutral();
        nearCap.Happiness = 99.5f;
        (LifeSimManager capped, EventBus cappedBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        capped.SetPersonStats(AvatarId, nearCap);
        // 7h = sleep-band neutral, keeping the clamp arithmetic pure.
        capped.SetTodaySchedule(new DaySchedule(7, 0, 0, 0, 0, 2, NpcActionId.VideoGames));
        cappedBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        cappedBus.DispatchPending();
        capped.TryGetPersonStats(AvatarId, out PersonStats cappedStats);
        capped.TryPeekPersonDeltas(AvatarId, out PersonStats cappedDeltas);
        float expCapH = 99.5f, expCapUnflushed = 0f;
        for (int h = 0; h < 2; h++)
        {
            float moved = Math.Clamp(expCapH + 1.0f, 0f, 100f) - expCapH;
            expCapH += moved;
            expCapUnflushed += moved;
        }
        float capReversion = Math.Clamp(expCapH + PersonDrift.HappinessDailyStep(expCapH, 99.5f), 0f, 100f) - expCapH;
        expCapH += capReversion;
        expCapUnflushed += capReversion;
        Check("clamp bookkeeping: a nudge into the 100 ceiling accrues only the CLAMPED movement (setpoint = the hydrated 99.5, so reversion pulls back down)",
            cappedStats.Happiness == expCapH && cappedDeltas.Happiness == expCapUnflushed
            && cappedStats.Happiness <= 100f && cappedDeltas.Happiness < 1f,
            $"happiness {cappedStats.Happiness:F4}, Δ {cappedDeltas.Happiness:F4}");

        // --- Hangout: charisma channel + the Social restore ---
        (LifeSimManager social, EventBus socialBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        social.SetPersonStats(AvatarId, PersonStats.Neutral());
        social.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 0, 3, NpcActionId.Hangout));
        socialBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        socialBus.DispatchPending();
        social.TryGetPersonStats(AvatarId, out PersonStats socialStats);
        Check("Hangout: 3 free hours moved charisma up (+0.3/h) and restored Social at the block rate",
            socialStats.Charisma > 50f && Math.Abs(socialStats.Charisma - 50.9f) < 1e-3,
            $"charisma {socialStats.Charisma:F4}");
        (LifeSimManager pious, EventBus piousBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        pious.SetPersonStats(AvatarId, PersonStats.Neutral());
        pious.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 0, 2, NpcActionId.Church));
        piousBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        piousBus.DispatchPending();
        pious.TryGetPersonStats(AvatarId, out PersonStats piousStats);
        Check("Church: 2 free hours moved morality (+0.4/h)",
            Math.Abs(piousStats.Morality - 50.8f) < 1e-3, $"morality {piousStats.Morality:F4}");

        // --- Person-stat impulse: mirrors the DB, never re-queues a flush ---
        (LifeSimManager mirrored, EventBus mirroredBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        mirrored.SetPersonStats(AvatarId, PersonStats.Neutral());
        mirroredBus.Publish(new PersonStatImpulseEvent(AvatarId, (int)PersonStatId.Discipline, 5f));
        mirroredBus.Publish(new PersonStatImpulseEvent(AvatarId, 99, 5f)); // out-of-range ordinal: ignored
        mirroredBus.Publish(new PersonStatImpulseEvent(BystanderId, (int)PersonStatId.Discipline, 5f)); // unhydrated: ignored
        mirroredBus.DispatchPending();
        mirrored.TryGetPersonStats(AvatarId, out PersonStats mirroredStats);
        mirrored.TryPeekPersonDeltas(AvatarId, out PersonStats mirroredDeltas);
        Check("person-stat impulse: moves the in-memory CURRENT value only (the DB already moved — unflushed stays 0); junk ordinals and unhydrated targets are ignored",
            mirroredStats.Discipline == 55f && mirroredDeltas.Discipline == 0f);

        // --- §5.3 (revised): travel cost replaces the flat refund ---

        // Pure TravelTime.ComputeHours checks: trip counting + rounding.
        Check("TravelTime: 1 trip (Work), no transport — 1.25h rounds to 1h",
            TravelTime.ComputeHours(0, 0, 0, 2, 0, NpcActionId.Idle, 0f, out int oneTripWalkTrips) == 1
            && oneTripWalkTrips == 1);
        Check("TravelTime: 1 trip (Work), car (1.0 saved) — floors to the 0.25h minimum, rounds to 0h",
            TravelTime.ComputeHours(0, 0, 0, 2, 0, NpcActionId.Idle, 1.0f, out _) == 0);
        Check("TravelTime: School+Work+Hangout = 3 trips, no transport — 3.75h rounds to 4h",
            TravelTime.ComputeHours(6, 0, 0, 2, 2, NpcActionId.Hangout, 0f, out int threeTripWalkTrips) == 4
            && threeTripWalkTrips == 3);
        Check("TravelTime: School+Work+Hangout = 3 trips, car (1.0 saved) — 0.75h rounds to 1h",
            TravelTime.ComputeHours(6, 0, 0, 2, 2, NpcActionId.Hangout, 1.0f, out _) == 1);
        Check("TravelTime: Practice AND Game the same day is still ONE team-facility trip",
            TravelTime.ComputeHours(0, 2, 3, 0, 0, NpcActionId.Idle, 0f, out int practiceGameTrips) == 1
            && practiceGameTrips == 1);
        Check("TravelTime: Study free time stays home (0 trips) — VideoGames too, Hangout/Church travel",
            TravelTime.ComputeHours(0, 0, 0, 0, 4, NpcActionId.Study, 0f, out int studyTrips) == 0 && studyTrips == 0
            && TravelTime.ComputeHours(0, 0, 0, 0, 4, NpcActionId.VideoGames, 0f, out int gamesTrips) == 0 && gamesTrips == 0
            && TravelTime.ComputeHours(0, 0, 0, 0, 4, NpcActionId.Church, 0f, out int churchTrips) == 1 && churchTrips == 1);
        Check("TravelTime: zero trips is always zero hours, regardless of transport",
            TravelTime.ComputeHours(0, 0, 0, 0, 0, NpcActionId.Idle, 0f, out _) == 0);

        // DaySchedule.FreeHours: travel is a real allocated block now, not a
        // bonus layered on top — a walker's Practice day has strictly fewer
        // free hours than a driver's IDENTICAL Practice day. (Practice, not
        // Work: Practice rides Idle's neutral definition with no meal-access
        // buffering, isolating the travel effect from School/Work's restore.)
        int walkerTravel = TravelTime.ComputeHours(0, 1, 0, 0, 0, NpcActionId.Idle, 0f, out _);
        int driverTravel = TravelTime.ComputeHours(0, 1, 0, 0, 0, NpcActionId.Idle, 1.0f, out _);
        var walkerDay = new DaySchedule(8, 0, 1, 0, 0, travelHours: walkerTravel);
        var driverDay = new DaySchedule(8, 0, 1, 0, 0, travelHours: driverTravel);
        Check("DaySchedule: a car's discounted trip leaves strictly MORE free hours than walking the same day",
            driverDay.FreeHours > walkerDay.FreeHours
            && walkerDay.FreeHours == DaySchedule.HoursPerDay - 8 - 1 - walkerTravel,
            $"walker free {walkerDay.FreeHours}, driver free {driverDay.FreeHours}");

        // Integration: since forced-Idle Travel hours and an autopilot's own
        // Idle pick are decay-identical, a walker/driver pair only visibly
        // diverges once a need has actually crossed a crossover threshold
        // within the window — so seed Hunger just below the Eat crossover
        // (59, per the sweep above) rather than leaving it to chance. A
        // driver's extra autopilot-chooseable hour can act on it a full hour
        // sooner than a walker's forced-Idle travel hour ever could.
        var hungryNeeds = NeedsState.FullySatisfied();
        hungryNeeds.Hunger = 55f;
        (LifeSimManager walker, EventBus walkerBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        (LifeSimManager driver, EventBus driverBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        walker.SetNeeds(AvatarId, hungryNeeds);
        driver.SetNeeds(AvatarId, hungryNeeds);
        driver.AvatarTransportHoursSaved = 1.0f;
        walker.SetTodaySchedule(walkerDay);
        driver.SetTodaySchedule(driverDay);
        walkerBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        walkerBus.DispatchPending();
        driverBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        driverBus.DispatchPending();
        Check("travel cost: a planned Practice day with a car diverges from the same day on foot (an already-hungry avatar gets serviced a full hour sooner)",
            !PersonStateIdentical(walker, driver, AvatarId));

        // A schedule with no away-blocks (0 trips) costs nothing regardless
        // of transport — the car's discount only ever matters if you go
        // somewhere.
        (LifeSimManager homebodyWalker, EventBus homebodyWalkerBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        (LifeSimManager homebodyDriver, EventBus homebodyDriverBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        homebodyDriver.AvatarTransportHoursSaved = 1.0f;
        homebodyWalker.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 0));
        homebodyDriver.SetTodaySchedule(new DaySchedule(8, 0, 0, 0, 0));
        homebodyWalkerBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        homebodyWalkerBus.DispatchPending();
        homebodyDriverBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        homebodyDriverBus.DispatchPending();
        Check("travel cost: a schedule with no away-blocks is transport-inert (bit-identical to a no-transport twin)",
            PersonStateIdentical(homebodyWalker, homebodyDriver, AvatarId));

        // Unplanned days never touch DaySchedule/TravelHours at all — an
        // autopilot day stays the pre-HS-4 24 hours exactly, transport or not.
        (LifeSimManager idleWalker, EventBus idleWalkerBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        (LifeSimManager idleDriver, EventBus idleDriverBus) = NewClockWorld(AvatarId, BystanderId, SeedFunds, stress: 0f, markAvatar: true);
        idleDriver.AvatarTransportHoursSaved = 1.0f;
        idleWalkerBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        idleWalkerBus.DispatchPending();
        idleDriverBus.Publish(new DayAdvancedEvent(1, 2026, 1));
        idleDriverBus.DispatchPending();
        Check("travel cost: an UNPLANNED day is travel-inert (bit-identical to a no-transport twin)",
            PersonStateIdentical(idleWalker, idleDriver, AvatarId));
    }

    private static void Check(string name, bool pass, string detail = "") =>
        Results.Add((name, pass, detail));
}
