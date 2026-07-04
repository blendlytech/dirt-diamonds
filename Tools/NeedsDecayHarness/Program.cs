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

    private static void Check(string name, bool pass, string detail = "") =>
        Results.Add((name, pass, detail));
}
