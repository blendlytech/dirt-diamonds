using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Narrative.Events;
using DirtAndDiamonds.Simulation.Life;

namespace DirtAndDiamonds.Tools.GrittyEventsHarness;

/// <summary>
/// Phase 7 harness (gritty_event_framework.md §7): content loader contract,
/// the condition algebra, the dispatcher's day-gated polling + pacing rules,
/// every consequence writer, the stress scalar's Life-sim wiring, the
/// threaded end-to-end pipeline, the idle-poll allocation bound, and the
/// exit-criteria cascade (bribe → compromised_syndicate → shakedown fires a
/// season later).
///
/// Usage: dotnet run --project Tools/GrittyEventsHarness [-c Release] [-- --repo &lt;path&gt;]
/// </summary>
internal static class Program
{
    private static readonly List<(string Name, bool Pass, string Detail)> Results = new();
    private static readonly List<string> ScratchFiles = new();

    private static string _schemaPath = "";
    private static string _repoRoot = "";

    private static int Main(string[] args)
    {
        string? repoRoot = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--repo")
            {
                repoRoot = args[i + 1];
            }
        }
        repoRoot ??= FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Could not locate project.godot above the working directory; pass --repo <path>.");
            return 2;
        }
        _repoRoot = repoRoot;
        _schemaPath = Path.Combine(repoRoot, "Assets", "Data", "Database", "SchemaDefinitions.sql");

        try
        {
            RunContentChecks();
            RunConditionChecks();
            RunDispatcherChecks();
            RunCascadeAndWriterChecks();
            RunChoiceUiChecks();
            RunMarriageConceptionChecks();
            RunStressChecks();
            RunThreadingCheck();
            RunAllocationCheck();
            RunDeterminismCheck();
        }
        catch (Exception ex)
        {
            Check($"unhandled {ex.GetType().Name}", false, ex.Message);
        }
        finally
        {
            foreach (string scratch in ScratchFiles)
            {
                TryDelete(scratch);
                TryDelete(scratch + "-wal");
                TryDelete(scratch + "-shm");
            }
        }

        int failed = 0;
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
    // 1. Content loader contract
    // ------------------------------------------------------------------

    private static void RunContentChecks()
    {
        Console.WriteLine("--- Content loader ---");

        // The shipped seed batch is valid and carries the exit-criteria pair.
        string shipped = File.ReadAllText(Path.Combine(
            _repoRoot, "Assets", "Narrative", "Events", "Content", "core_events.json"));
        GrittyEventLibrary library = GrittyEventJson.Parse(shipped);
        Check("shipped seed batch loads",
            library.Count == 4
            && library.TryGetById("back_alley_bribe", out _)
            && library.TryGetById("syndicate_shakedown", out GrittyEventDefinition shakedown)
            && library.TryGetById("beanball_grudge", out _)
            && library.TryGetById("backyard_catch", out _)
            && shakedown.Prerequisites.Length == 1
            && shakedown.Prerequisites[0].MinDaysSince == 365,
            $"{library.Count} events");

        Check("loader rejects unknown comparison op", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "prerequisites": [ { "field": "funds", "op": "<>", "value": 1 } ], "choices": [ { "id": "a" } ] } ] }"""));
        Check("loader rejects unknown consequence type", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "teleport", "amount": 1 } ] } ] } ] }"""));
        Check("loader rejects out-of-range weight", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 1.5, "choices": [ { "id": "a" } ] } ] }"""));
        Check("loader rejects choiceless event", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "choices": [] } ] }"""));
        Check("library rejects duplicate event ids across batches", ThrowsAny(() =>
            GrittyEventJson.Parse(new[]
            {
                """{ "events": [ { "id": "dup", "scope": "any", "weight": 0.1, "choices": [ { "id": "a" } ] } ] }""",
                """{ "events": [ { "id": "dup", "scope": "any", "weight": 0.2, "choices": [ { "id": "b" } ] } ] }""",
            })));

        // check_event_graph_integrity §1: "Load the whole Content folder
        // together — cross-batch duplicate ids fail here." Until now this
        // check only ever loaded core_events.json by hardcoded path above, so
        // a second batch file's ids were never actually merge-checked against
        // the first. Mirrors GameManager.LoadGrittyEventContent's *.json scan
        // (System.IO here; Godot DirAccess there) so the harness proves the
        // exact multi-file path GameManager runs at boot.
        string contentDir = Path.Combine(_repoRoot, "Assets", "Narrative", "Events", "Content");
        string[] batchFiles = Directory.GetFiles(contentDir, "*.json");
        var batchDocuments = new string[batchFiles.Length];
        for (int i = 0; i < batchFiles.Length; i++)
        {
            batchDocuments[i] = File.ReadAllText(batchFiles[i]);
        }
        GrittyEventLibrary wholeFolder = GrittyEventJson.Parse(batchDocuments);
        Check("whole Content folder merges into one library (no cross-batch id collisions)",
            batchFiles.Length >= 3
            && wholeFolder.Count == 14
            && wholeFolder.TryGetById("back_alley_bribe", out _)
            && wholeFolder.TryGetById("syndicate_enforcers", out _)
            && wholeFolder.TryGetById("caught_juicing", out _)
            && wholeFolder.TryGetById("redemption_tour", out _)
            && wholeFolder.TryGetById("injury_scare", out _)
            && wholeFolder.TryGetById("road_trip_bonding", out _)
            && wholeFolder.TryGetById("spring_training_romance", out _)
            && wholeFolder.TryGetById("benched_for_video_games", out _)
            && wholeFolder.TryGetById("met_someone", out _)
            && wholeFolder.TryGetById("starting_a_family", out _)
            && wholeFolder.TryGetById("child_is_born", out _),
            $"{batchFiles.Length} files, {wholeFolder.Count} events");
    }

    // ------------------------------------------------------------------
    // 2. Condition algebra
    // ------------------------------------------------------------------

    private static void RunConditionChecks()
    {
        Console.WriteLine("--- Condition evaluator ---");

        var subject = new PollPlayerRow
        {
            PlayerId = "p1",
            Age = 14,
            Funds = 499.99,
            Recklessness = 80,
            HealthCeiling = 100,
            BaseballInterest = 10,
            DetectionRisk = 0,
        };
        var flags = new Dictionary<(string, string), long> { [("p1", "marked")] = 10 };

        bool fieldOps =
            ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Funds, FieldComparison.Less, 500), in subject, flags, 1)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Funds, FieldComparison.GreaterOrEqual, 500), in subject, flags, 1)
            && ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Recklessness, FieldComparison.Greater, 75), in subject, flags, 1)
            && ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Age, FieldComparison.Equal, 14), in subject, flags, 1)
            && ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.BaseballInterest, FieldComparison.NotEqual, 11), in subject, flags, 1)
            && ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.HealthCeiling, FieldComparison.LessOrEqual, 100), in subject, flags, 1);
        Check("field comparisons", fieldOps);

        bool flagForms =
            ConditionEvaluator.Holds(EventPrerequisite.ForFlagActive("marked"), in subject, flags, 11)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForFlagActive("unset"), in subject, flags, 11)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForFlagInactive("marked"), in subject, flags, 11)
            && ConditionEvaluator.Holds(EventPrerequisite.ForFlagInactive("unset"), in subject, flags, 11);
        Check("flag active/inactive", flagForms);

        // The cascade boundary: set day 10, window 365 — day 375 is exactly
        // enough, day 374 is one short.
        bool boundary =
            ConditionEvaluator.Holds(EventPrerequisite.ForFlagActive("marked", 365), in subject, flags, 375)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForFlagActive("marked", 365), in subject, flags, 374);
        Check("min_days_since boundary (day N holds, N−1 does not)", boundary);
    }

    // ------------------------------------------------------------------
    // 3. Dispatcher: day gating, pacing, caps
    // ------------------------------------------------------------------

    private const string AlwaysFireJson =
        """{ "events": [ { "id": "always", "scope": "any", "weight": 1.0, "prerequisites": [ { "field": "age", "op": ">=", "value": 0 } ], "choices": [ { "id": "only" } ] } ] }""";

    private static void RunDispatcherChecks()
    {
        Console.WriteLine("--- Dispatcher gating & pacing ---");

        // Day gating + weight extremes.
        {
            using World world = World.Create("gate", GrittyEventJson.Parse(new[]
            {
                """{ "events": [ { "id": "never", "scope": "any", "weight": 0.0, "choices": [ { "id": "a" } ] } ] }""",
                AlwaysFireJson,
            }));
            world.AddPlayer("p1", age: 27, teamId: 1);

            int atBoot = world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            int afterDay = world.Dispatcher.PollOnce();
            int sameDayAgain = world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            Check("first observed day records, never evaluates", atBoot == 0);
            Check("day change fires weight-1.0 exactly once; weight-0.0 never; same-day repoll is silent",
                afterDay == 1 && sameDayAgain == 0
                && world.Fired.Count == 1 && world.Fired[0].EventId == "always" && world.Fired[0].Day == 2,
                $"fired {world.Fired.Count}");
        }

        // One fired event per subject per day: the first satisfied definition wins.
        {
            using World world = World.Create("onePerSubject", GrittyEventJson.Parse(new[]
            {
                """{ "events": [ { "id": "first", "scope": "any", "weight": 1.0, "choices": [ { "id": "a" } ] }, { "id": "second", "scope": "any", "weight": 1.0, "choices": [ { "id": "a" } ] } ] }""",
            }));
            world.AddPlayer("p1", age: 27, teamId: 1);
            world.Dispatcher.PollOnce(); // record the boot day
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("one fire per subject per day (definition order wins)",
                world.Fired.Count == 1 && world.Fired[0].EventId == "first");
        }

        // MaxFiresPerDay safety valve.
        {
            using World world = World.Create("cap", GrittyEventJson.Parse(AlwaysFireJson));
            for (int i = 0; i < EventDispatcher.MaxFiresPerDay + 4; i++)
            {
                world.AddPlayer($"p{i}", age: 27, teamId: 1);
            }
            world.Dispatcher.PollOnce(); // record the boot day
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            int fired = world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("MaxFiresPerDay caps a runaway day",
                fired == EventDispatcher.MaxFiresPerDay && world.Fired.Count == EventDispatcher.MaxFiresPerDay,
                $"{fired} fires");
        }

        // Cooldown pacing + multi-day catch-up (each missed day evaluated once).
        {
            using World world = World.Create("cooldown", GrittyEventJson.Parse(
                """{ "events": [ { "id": "paced", "scope": "any", "weight": 1.0, "cooldown_days": 5, "choices": [ { "id": "a" } ] } ] }"""));
            world.AddPlayer("p1", age: 27, teamId: 1);
            world.Dispatcher.PollOnce();

            world.Clock.AdvanceDays(9); // days 2..10 in one catch-up sweep
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            // Day 2 fires; cooldown blocks 3–6; day 7 fires; blocks 8–11.
            bool paced = world.Fired.Count == 2 && world.Fired[0].Day == 2 && world.Fired[1].Day == 7;
            Check("cooldown paces refires through a multi-day catch-up",
                paced, string.Join(",", world.Fired.Select(f => f.Day)));
        }

        // Avatar scope only ever picks the avatar.
        {
            using World world = World.Create("avatarScope", GrittyEventJson.Parse(
                """{ "events": [ { "id": "self", "scope": "avatar", "weight": 1.0, "choices": [ { "id": "a" } ] } ] }"""));
            world.AddPlayer("npc", age: 27, teamId: 1);
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce(); // record the boot day
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("avatar scope binds to avatar_player_id",
                world.Fired.Count == 1 && world.Fired[0].SubjectPlayerId == "hero");
        }
    }

    // ------------------------------------------------------------------
    // 4. The exit-criteria cascade + every writer
    // ------------------------------------------------------------------

    private static void RunCascadeAndWriterChecks()
    {
        Console.WriteLine("--- Cascade & writers ---");

        // Deterministic single-choice versions of the shipped bribe/shakedown
        // pair (same prerequisites, weight pinned to 1.0).
        const string cascadeJson =
            """
            { "events": [
              { "id": "bribe", "scope": "any", "weight": 1.0,
                "prerequisites": [
                  { "field": "funds", "op": "<", "value": 500 },
                  { "field": "recklessness", "op": ">", "value": 75 },
                  { "flag_inactive": "compromised_syndicate" } ],
                "choices": [ { "id": "take", "consequences": [
                  { "type": "funds", "amount": 2000 },
                  { "type": "set_flag", "flag": "compromised_syndicate" },
                  { "type": "stress", "amount": 25 } ] } ] },
              { "id": "shakedown", "scope": "any", "weight": 1.0,
                "prerequisites": [ { "flag_active": "compromised_syndicate", "min_days_since": 365 } ],
                "choices": [ { "id": "pay", "consequences": [
                  { "type": "funds", "amount": -3000 },
                  { "type": "stress", "amount": 20 } ] } ] }
            ] }
            """;

        using (World world = World.Create("cascade", GrittyEventJson.Parse(cascadeJson)))
        {
            world.AddPlayer("mark", age: 27, teamId: 1, funds: 100, recklessness: 90);
            world.LifeSim.Seed(new[] { new NpcSeed("mark", 100) });
            world.LifeSim.AttachTo(world.Bus);
            world.Dispatcher.PollOnce();

            // Day 2: the bribe fires.
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Players.TryGetById("mark", out PlayerRow afterBribe);
            var flagRows = new List<EntityFlagRow>();
            world.Players.LoadActiveFlags("mark", flagRows);
            world.LifeSim.TryGetStress("mark", out float stressAfterBribe);
            world.LifeSim.TryGetFunds("mark", out double mirrorAfterBribe);

            Check("bribe: funds +2000 committed atomically",
                Math.Abs(afterBribe.Funds - 2100) < 1e-9, $"{afterBribe.Funds}");
            Check("bribe: compromised_syndicate set with set_on_day = fire day",
                flagRows.Count == 1 && flagRows[0].FlagName == "compromised_syndicate" && flagRows[0].SetOnDay == 2);
            Check("bribe: stress impulse landed via the bus", Math.Abs(stressAfterBribe - 25f) < 1e-3, $"{stressAfterBribe}");
            Check("bribe: life-sim funds mirror moved with the DB",
                mirrorAfterBribe > 2000 && mirrorAfterBribe <= 2100, $"{mirrorAfterBribe}");

            // Day 3: nothing — the window is 365 days, and the bribe cannot
            // refire behind its own flag.
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            int nextDayFires = world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("cascade waits: neither event fires the next day", nextDayFires == 0);

            // Day 367 = set day 2 + 365: the shakedown lands.
            world.Clock.AdvanceDays(364);
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Players.TryGetById("mark", out PlayerRow afterShakedown);
            world.LifeSim.TryGetStress("mark", out float stressAfterShakedown);
            world.LifeSim.TryGetFunds("mark", out double mirrorAfterShakedown);
            GrittyEventFiredEvent shakeFire = world.Fired.Last();

            Check("EXIT CRITERIA: shakedown fires exactly one season after the flag was planted",
                world.Fired.Count == 2 && shakeFire.EventId == "shakedown" && shakeFire.Day == 367,
                string.Join(",", world.Fired.Select(f => $"{f.EventId}@{f.Day}")));
            Check("shakedown: funds floor-clamps at 0 (2100 − 3000)",
                afterShakedown.Funds == 0, $"{afterShakedown.Funds}");
            Check("shakedown: stress re-spikes after the old arc relaxed away",
                Math.Abs(stressAfterShakedown - 20f) < 1e-3, $"{stressAfterShakedown}");
            Check("shakedown: funds mirror floor-clamped too", mirrorAfterShakedown == 0, $"{mirrorAfterShakedown}");
        }

        // Rivalry writer: create-then-deepen, target selection, Phase-6 transport.
        {
            using World world = World.Create("rivalry", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "beanball", "scope": "any", "weight": 1.0,
                  "prerequisites": [ { "field": "recklessness", "op": ">", "value": 60 } ],
                  "choices": [ { "id": "charge", "consequences": [
                    { "type": "relationship", "kind": "rival", "affinity": -35, "target": "opponent" } ] } ] } ] }
                """));
            world.AddPlayer("hothead", age: 27, teamId: 1, recklessness: 90);
            world.AddPlayer("teammate", age: 27, teamId: 1);
            world.AddPlayer("victim", age: 27, teamId: 2);

            var rivalryEvents = new List<RivalryChangedEvent>();
            world.Bus.Subscribe<RivalryChangedEvent>(rivalryEvents.Add);

            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            bool created = world.Graph.TryGetRelationship("hothead", "victim", out int affinity, out RelationshipKind kind)
                && affinity == -35 && kind == RelationshipKind.Rival
                && !world.Graph.TryGetRelationship("hothead", "teammate", out _, out _);
            Check("rivalry writer: opponent-targeted Rival edge created at −35",
                created, $"affinity {affinity}");
            Check("rivalry writer: RivalryChangedEvent reached the bus (Phase-6 transport intact)",
                rivalryEvents.Count == 1 && rivalryEvents[0].Intensity == 35);

            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            world.Graph.TryGetRelationship("hothead", "victim", out affinity, out _);
            Check("rivalry writer: second fire deepens the same edge (−70) and republishes",
                affinity == -70 && rivalryEvents.Count == 2 && rivalryEvents[1].Intensity == 70,
                $"affinity {affinity}");
        }

        // Interest writer: clamped delta.
        {
            using World world = World.Create("interest", GrittyEventJson.Parse(
                """
                { "events": [
                  { "id": "spark", "scope": "any", "weight": 1.0,
                    "prerequisites": [ { "field": "age", "op": "<", "value": 16 }, { "field": "baseball_interest", "op": "==", "value": 0 } ],
                    "choices": [ { "id": "a", "consequences": [ { "type": "interest", "amount": 8 } ] } ] },
                  { "id": "obsession", "scope": "any", "weight": 1.0,
                    "prerequisites": [ { "field": "age", "op": "<", "value": 16 }, { "field": "baseball_interest", "op": ">", "value": 0 } ],
                    "choices": [ { "id": "a", "consequences": [ { "type": "interest", "amount": 200 } ] } ] }
                ] }
                """));
            world.AddPlayer("kid", age: 12, teamId: null);
            world.Dispatcher.PollOnce();

            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            world.Players.TryGetById("kid", out PlayerRow afterSpark);

            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            world.Players.TryGetById("kid", out PlayerRow afterObsession);

            Check("interest writer: +8 lands; +200 clamps at 100 in SQL",
                afterSpark.BaseballInterest == 8 && afterObsession.BaseballInterest == 100,
                $"{afterSpark.BaseballInterest} → {afterObsession.BaseballInterest}");
        }

        // Autopilot choice weighting: a zero-weight choice is never drawn.
        {
            using World world = World.Create("choices", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "pick", "scope": "any", "weight": 1.0, "choices": [
                  { "id": "never", "autopilot_weight": 0 },
                  { "id": "always", "autopilot_weight": 5 } ] } ] }
                """));
            world.AddPlayer("p1", age: 27, teamId: 1);
            var resolved = new List<GrittyEventResolvedEvent>();
            world.Bus.Subscribe<GrittyEventResolvedEvent>(resolved.Add);
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDays(10);
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("autopilot choice draw honors weights (zero-weight never picked)",
                resolved.Count == 10 && resolved.All(r => r.ChoiceIndex == 1),
                $"{resolved.Count} resolutions");
        }
    }

    // ------------------------------------------------------------------
    // 4b. Event-choice UI: avatar fires pause, NPCs stay autopilot, a stale
    // pending choice forfeits rather than vanishing or throwing.
    // ------------------------------------------------------------------

    private static void RunChoiceUiChecks()
    {
        Console.WriteLine("--- Event-choice UI (pause/resolve/forfeit) ---");

        const string choiceUiJson =
            """
            { "events": [
              { "id": "choiceA", "scope": "avatar", "weight": 1.0, "cooldown_days": 5,
                "choices": [ { "id": "a1", "autopilot_weight": 1 }, { "id": "a2", "autopilot_weight": 0 } ] },
              { "id": "choiceB", "scope": "avatar", "weight": 1.0,
                "choices": [ { "id": "b1", "autopilot_weight": 1 } ] },
              { "id": "npcEvent", "scope": "any", "weight": 1.0,
                "choices": [ { "id": "only" } ] }
            ] }
            """;

        using World world = World.Create("choiceUi", GrittyEventJson.Parse(choiceUiJson));
        world.Applier.AutopilotAvatarChoices = false;
        world.AddPlayer("hero", age: 27, teamId: 1);
        world.AddPlayer("npc", age: 27, teamId: 2);
        world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");

        var resolved = new List<GrittyEventResolvedEvent>();
        world.Bus.Subscribe<GrittyEventResolvedEvent>(resolved.Add);

        world.Dispatcher.PollOnce(); // record the boot day

        // Day 2: hero's first satisfied event (choiceA) pauses; npc's
        // any-scope event still auto-resolves — the UI only changes the
        // avatar's path.
        world.Clock.AdvanceDay();
        world.Bus.DispatchPending();
        world.Dispatcher.PollOnce();
        world.Bus.DispatchPending();

        bool pausedForHero = world.Applier.HasPendingChoice
            && world.Applier.TryGetPendingChoice(out PendingGrittyChoice pendingA)
            && pendingA.Fired.EventId == "choiceA" && pendingA.Fired.SubjectPlayerId == "hero";
        Check("avatar-subject fire pauses instead of auto-resolving",
            pausedForHero, world.Applier.HasPendingChoice ? "pending" : "not pending");
        Check("NPC-subject fire still auto-resolves with the choice UI live",
            resolved.Count == 1 && resolved[0].EventId == "npcEvent");

        // Day 3: choiceA is in cooldown for hero, so choiceB fires next —
        // forfeiting the still-unresolved choiceA to its autopilot draw
        // (a2's weight is 0, so the forfeit deterministically picks a1/index 0).
        world.Clock.AdvanceDay();
        world.Bus.DispatchPending();
        world.Dispatcher.PollOnce();
        world.Bus.DispatchPending();

        GrittyEventResolvedEvent? forfeited = resolved.FirstOrDefault(r => r.EventId == "choiceA");
        bool nowPendingB = world.Applier.HasPendingChoice
            && world.Applier.TryGetPendingChoice(out PendingGrittyChoice pendingB)
            && pendingB.Fired.EventId == "choiceB";
        Check("an unresolved pending choice forfeits to autopilot when a new avatar fire needs the slot",
            forfeited is { EventId: "choiceA", ChoiceIndex: 0 } && nowPendingB,
            $"resolved: {string.Join(",", resolved.Select(r => r.EventId))}");

        // The UI's actual answer: resolving the still-open choiceB. Publish is
        // deferred (EventBus contract), so the resolved-event feed needs a pump.
        world.Applier.ResolveChoice(0);
        world.Bus.DispatchPending();
        Check("ResolveChoice applies the player's pick and clears pending",
            !world.Applier.HasPendingChoice
            && resolved.Any(r => r.EventId == "choiceB" && r.ChoiceIndex == 0));

        Check("ResolveChoice with nothing pending throws",
            ThrowsAny(() => world.Applier.ResolveChoice(0)));
    }

    // ------------------------------------------------------------------
    // 4c. Marriage & conception writers (marriage_and_conception.md §7
    // checks 1–4 — the Narrative side; CareerManager's consumer is
    // MonteCarloHarness territory, deliberately not compiled here).
    // ------------------------------------------------------------------

    private static void RunMarriageConceptionChecks()
    {
        Console.WriteLine("--- Marriage & conception writers ---");

        // (1) Load-time gate: conceive_child only parses on scope-avatar events.
        Check("loader rejects conceive_child on a non-avatar-scope event", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "conceive_child" } ] } ] } ] }"""));
        bool avatarScopeAccepted = false;
        try
        {
            GrittyEventLibrary parsed = GrittyEventJson.Parse(
                """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "conceive_child" } ] } ] } ] }""");
            avatarScopeAccepted = parsed.Count == 1;
        }
        catch (FormatException)
        {
        }
        Check("loader accepts conceive_child on scope: avatar", avatarScopeAccepted);

        // (2) Marriage exclusivity: the first partner consequence writes one
        // Partner edge; a second fire on the already-partnered subject is a
        // no-op — edge, counterpart and affinity all unchanged.
        {
            using World world = World.Create("marriage", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "meet", "scope": "avatar", "weight": 1.0,
                  "choices": [ { "id": "marry", "consequences": [
                    { "type": "relationship", "kind": "partner", "affinity": 60, "target": "league" } ] } ] } ] }
                """));
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.AddPlayer("spouseA", age: 27, teamId: 1);
            world.AddPlayer("spouseB", age: 27, teamId: 2);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();

            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("hero", edges);
            int partnerEdges = edges.Count(e => e.Kind == RelationshipKind.Partner);
            string? firstPartner = edges.FirstOrDefault(e => e.Kind == RelationshipKind.Partner).OtherId;
            Check("marriage: partner consequence creates exactly one Partner edge at 60",
                partnerEdges == 1 && edges.Single(e => e.Kind == RelationshipKind.Partner).Affinity == 60,
                $"{partnerEdges} partner edges");

            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Graph.GetEdgesFor("hero", edges);
            bool unchanged = edges.Count(e => e.Kind == RelationshipKind.Partner) == 1
                && edges.Single(e => e.Kind == RelationshipKind.Partner).OtherId == firstPartner
                && edges.Single(e => e.Kind == RelationshipKind.Partner).Affinity == 60;
            Check("marriage: a second partner consequence on a partnered subject is a no-op (exclusivity)",
                unchanged, $"partner: {firstPartner}");
        }

        // (3) Conception publish: the fire publishes exactly one request whose
        // PartnerId is the avatar's LIVE partner (null when unmarried), and
        // the applier writes no player rows itself — no Baseball code is even
        // compiled into this harness, so a request is all it CAN do.
        {
            using World world = World.Create("conception", GrittyEventJson.Parse(
                """
                { "events": [
                  { "id": "meet", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_inactive": "married" } ],
                    "choices": [ { "id": "marry", "consequences": [
                      { "type": "relationship", "kind": "partner", "affinity": 60, "target": "league" },
                      { "type": "set_flag", "flag": "married" } ] } ] },
                  { "id": "baby", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_active": "married" } ],
                    "choices": [ { "id": "welcome", "consequences": [ { "type": "conceive_child" } ] } ] }
                ] }
                """));
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.AddPlayer("spouse", age: 27, teamId: 2);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            var requests = new List<ChildConceptionRequestedEvent>();
            world.Bus.Subscribe<ChildConceptionRequestedEvent>(requests.Add);
            world.Dispatcher.PollOnce();

            world.Clock.AdvanceDay(); // day 2: meet (marries "spouse" — the only league candidate)
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            world.Clock.AdvanceDay(); // day 3: baby
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var rows = new List<PlayerRow>();
            world.Players.LoadAll(rows);
            Check("conception: one request published, PartnerId = the live Partner, no rows written by the applier",
                requests.Count == 1 && requests[0].ParentAvatarId == "hero"
                && requests[0].PartnerId == "spouse" && requests[0].Day == 3
                && rows.Count == 2,
                $"{requests.Count} requests, {rows.Count} player rows");
        }
        {
            using World world = World.Create("conceptionSingle", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "baby", "scope": "avatar", "weight": 1.0,
                  "choices": [ { "id": "welcome", "consequences": [ { "type": "conceive_child" } ] } ] } ] }
                """));
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            var requests = new List<ChildConceptionRequestedEvent>();
            world.Bus.Subscribe<ChildConceptionRequestedEvent>(requests.Add);
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("conception: an unmarried avatar's request carries PartnerId = null",
                requests.Count == 1 && requests[0].PartnerId is null,
                $"{requests.Count} requests");
        }

        // (4) The §5 content arc end-to-end: met_someone → +365 →
        // starting_a_family → +270 → child_is_born, paced entirely by the
        // flag cascade (min_days_since), with the arc left re-armable
        // (expecting cleared, married kept).
        {
            using World world = World.Create("familyArc", GrittyEventJson.Parse(
                """
                { "events": [
                  { "id": "met_someone", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_inactive": "married" } ],
                    "choices": [ { "id": "propose", "consequences": [
                      { "type": "relationship", "kind": "partner", "affinity": 60, "target": "league" },
                      { "type": "set_flag", "flag": "married" } ] } ] },
                  { "id": "starting_a_family", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_active": "married", "min_days_since": 365 }, { "flag_inactive": "expecting" } ],
                    "choices": [ { "id": "try", "consequences": [ { "type": "set_flag", "flag": "expecting" } ] } ] },
                  { "id": "child_is_born", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_active": "expecting", "min_days_since": 270 } ],
                    "choices": [ { "id": "welcome", "consequences": [
                      { "type": "conceive_child" }, { "type": "clear_flag", "flag": "expecting" } ] } ] }
                ] }
                """));
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.AddPlayer("spouse", age: 27, teamId: 2);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            var requests = new List<ChildConceptionRequestedEvent>();
            world.Bus.Subscribe<ChildConceptionRequestedEvent>(requests.Add);
            world.Dispatcher.PollOnce();

            // Staged like the shipped bribe→shakedown check: each beat's
            // flags must COMMIT (pump) before the next catch-up sweep, since
            // a sweep evaluates every missed day against one snapshot.
            world.Clock.AdvanceDay(); // day 2: met_someone
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Clock.AdvanceDays(365); // days 3..367: starting_a_family lands on exactly 367
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Clock.AdvanceDays(270); // days 368..637: child_is_born lands on exactly 637
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var flagRows = new List<EntityFlagRow>();
            world.Players.LoadActiveFlags("hero", flagRows);
            bool marriedStanding = flagRows.Any(f => f.FlagName == "married" && f.SetOnDay == 2);
            bool expectingCleared = flagRows.All(f => f.FlagName != "expecting");
            Check("§5 arc: met_someone@2 → starting_a_family@367 → child_is_born@637, flag-cascade-paced",
                world.Fired.Count == 3
                && world.Fired[0].EventId == "met_someone" && world.Fired[0].Day == 2
                && world.Fired[1].EventId == "starting_a_family" && world.Fired[1].Day == 367
                && world.Fired[2].EventId == "child_is_born" && world.Fired[2].Day == 637
                && requests.Count == 1 && requests[0].PartnerId == "spouse" && requests[0].Day == 637
                && marriedStanding && expectingCleared,
                string.Join(",", world.Fired.Select(f => $"{f.EventId}@{f.Day}")));
        }
    }

    // ------------------------------------------------------------------
    // 5. Stress scalar wiring (life_sim_needs_decay.md §4.2 goes live)
    // ------------------------------------------------------------------

    private static void RunStressChecks()
    {
        Console.WriteLine("--- Stress scalar ---");

        // The §4.2 mapping and the §9 modifier fixture, now through the live path.
        {
            float sCalm = NeedsEngine.StressModifierFor(0f);
            float sMax = NeedsEngine.StressModifierFor(100f);
            float sTwo = NeedsEngine.StressModifierFor(200f / 3f); // stress ≈66.67 → S = 2.0
            float stressed = NeedsEngine.DecayHour(40f, NeedsEngine.Hunger, 1f, sTwo);
            float calm = NeedsEngine.DecayHour(40f, NeedsEngine.Hunger, 1f, sCalm);
            Check("StressModifierFor: 0→1.0, 100→2.5; §9 fixture 26.76 vs calm 33.38",
                Math.Abs(sCalm - 1f) < 1e-6 && Math.Abs(sMax - 2.5f) < 1e-6
                && Math.Abs(stressed - 26.76f) < 0.01f && Math.Abs(calm - 33.38f) < 0.01f,
                $"S2={sTwo:F3}, stressed={stressed:F2}, calm={calm:F2}");
        }

        // Bus impulse accumulates + clamps; passive relaxation over one game day.
        {
            var bus = new EventBus();
            var lifeSim = new LifeSimManager();
            lifeSim.Seed(new[] { new NpcSeed("npc", 50_000) });
            lifeSim.AttachTo(bus);

            bus.Publish(new StressImpulseEvent("npc", 15f));
            bus.Publish(new StressImpulseEvent("npc", 10f));
            bus.Publish(new StressImpulseEvent("npc", 900f));
            bus.DispatchPending();
            lifeSim.TryGetStress("npc", out float clamped);
            Check("stress impulses accumulate and clamp at 100", Math.Abs(clamped - 100f) < 1e-3, $"{clamped}");

            lifeSim.SetStress("npc", 25f);
            bus.Publish(new DayAdvancedEvent(2, 2026, 2));
            bus.DispatchPending();
            lifeSim.TryGetStress("npc", out float relaxed);
            // 25 − 24·0.4 = 15.4; below the override line and no critical need,
            // so no stress-relief action can have contributed.
            Check("passive relaxation: 25 → 15.4 over one funded, calm day",
                Math.Abs(relaxed - 15.4f) < 0.01f, $"{relaxed:F2}");
        }

        // The high-stress override: full needs, ample funds — only stress forces action.
        {
            NpcActionId picked = UtilityCalculator.SelectAction(
                NeedsState.FullySatisfied(), 50_000, UtilityCalculator.DefaultWeights, out _, stress0To100: 85f);
            NpcActionId calmPick = UtilityCalculator.SelectAction(
                NeedsState.FullySatisfied(), 50_000, UtilityCalculator.DefaultWeights, out _, stress0To100: 0f);
            Check("high-stress override forces a stress-relief action with zero need deficit",
                ActionCatalog.Get(picked).IsStressRelief && calmPick == NpcActionId.Idle,
                $"stressed → {picked}, calm → {calmPick}");
        }

        // Relief actions actually discharge stress in the running sim.
        {
            var bus = new EventBus();
            var lifeSim = new LifeSimManager();
            lifeSim.Seed(new[] { new NpcSeed("npc", 50_000) });
            lifeSim.AttachTo(bus);
            lifeSim.SetStress("npc", 100f);
            bus.Publish(new DayAdvancedEvent(2, 2026, 2));
            bus.DispatchPending();
            lifeSim.TryGetStress("npc", out float discharged);
            // Passive alone would leave 90.4; relief actions must have fired.
            Check("stress-relief actions discharge stress faster than relaxation alone",
                discharged < 90.4f - LifeSimManager.StressReliefPerAction + 1f, $"{discharged:F2}");
        }

        // Stress 0 is bit-identical to the pre-Phase-7 decay call.
        {
            var state = new NeedsState { Hunger = 73f, Sleep = 41f, Hygiene = 88f, Social = 12f, Fitness = 55f };
            NeedsState viaDefault = NeedsEngine.DecayHour(state, EnvironmentalModifiers.Neutral);
            NeedsState viaStressZero = NeedsEngine.DecayHour(state, EnvironmentalModifiers.Neutral, NeedsEngine.StressModifierFor(0f));
            Check("stress 0 is bit-identical to the pre-Phase-7 path",
                viaDefault.Hunger == viaStressZero.Hunger && viaDefault.Sleep == viaStressZero.Sleep
                && viaDefault.Hygiene == viaStressZero.Hygiene && viaDefault.Social == viaStressZero.Social
                && viaDefault.Fitness == viaStressZero.Fitness);
        }
    }

    // ------------------------------------------------------------------
    // 6. The real thread against the real WAL file
    // ------------------------------------------------------------------

    private static void RunThreadingCheck()
    {
        Console.WriteLine("--- Threaded pipeline ---");

        using World world = World.Create("threaded", GrittyEventJson.Parse(AlwaysFireJson), pollIntervalMs: 20);
        world.AddPlayer("p1", age: 27, teamId: 1);

        world.Dispatcher.Start();
        try
        {
            // Give the poller a beat to record the boot day before advancing.
            Thread.Sleep(120);
            world.Clock.AdvanceDay();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (world.Fired.Count == 0 && DateTime.UtcNow < deadline)
            {
                world.Bus.DispatchPending(); // the main pump, exactly like GameManager._Process
                Thread.Sleep(10);
            }
        }
        finally
        {
            world.Dispatcher.Stop();
            world.Dispatcher.Stop(); // idempotent
        }

        Check("background thread sees the committed day over its own WAL connection and fires through the pump",
            world.Fired.Count == 1 && world.Fired[0].Day == 2,
            $"fired {world.Fired.Count}");
    }

    // ------------------------------------------------------------------
    // 7. Idle-poll allocation bound
    // ------------------------------------------------------------------

    private static void RunAllocationCheck()
    {
        Console.WriteLine("--- Idle-poll allocation ---");

        using World world = World.Create("alloc", GrittyEventJson.Parse(AlwaysFireJson));
        world.AddPlayer("p1", age: 27, teamId: 1);
        world.Dispatcher.PollOnce(); // record the boot day

        for (int i = 0; i < 1_000; i++)
        {
            world.Dispatcher.PollOnce(); // warm every pooled path
        }

        const int Polls = 10_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Polls; i++)
        {
            world.Dispatcher.PollOnce();
        }
        long perPoll = (GC.GetAllocatedBytesForCurrentThread() - before) / Polls;

        // ADO.NET allocates one internal SqliteDataReader + one boxed scalar
        // per ExecuteScalar (~464 B measured) — the bound catches leaks
        // (snapshot rebuilds, string churn), not the unavoidable reader.
        Check("idle poll stays under 512 B/poll", perPoll <= 512, $"{perPoll} B/poll");
    }

    // ------------------------------------------------------------------
    // 8. Determinism
    // ------------------------------------------------------------------

    private static void RunDeterminismCheck()
    {
        Console.WriteLine("--- Determinism ---");

        List<(string, long)> RunOnce()
        {
            using World world = World.Create("det", GrittyEventJson.Parse(
                """{ "events": [ { "id": "coin", "scope": "any", "weight": 0.5, "choices": [ { "id": "a" } ] } ] }"""),
                dispatcherSeed: 12345UL);
            world.AddPlayer("p1", age: 27, teamId: 1);
            world.AddPlayer("p2", age: 27, teamId: 2);
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDays(30);
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            return world.Fired.Select(f => (f.SubjectPlayerId, f.Day)).ToList();
        }

        List<(string, long)> first = RunOnce();
        List<(string, long)> second = RunOnce();
        Check("same seed ⇒ identical fire schedule",
            first.Count > 0 && first.SequenceEqual(second),
            $"{first.Count} fires over 30 days");
    }

    // ------------------------------------------------------------------
    // World: one scratch save + the full Phase-7 pipeline around it
    // ------------------------------------------------------------------

    private sealed class World : IDisposable
    {
        public DatabaseManager Db = null!;
        public DatabaseManager.ReadOnlyView View = null!;
        public EventBus Bus = null!;
        public GameStateQueries GameState = null!;
        public PlayerQueries Players = null!;
        public GlobalState State = null!;
        public TimeManager Clock = null!;
        public RelationshipGraph Graph = null!;
        public LifeSimManager LifeSim = null!;
        public EventDispatcher Dispatcher = null!;
        public EventConsequenceApplier Applier = null!;
        public readonly List<GrittyEventFiredEvent> Fired = new();

        public static World Create(
            string tag, GrittyEventLibrary library, int pollIntervalMs = 250, ulong dispatcherSeed = 777UL)
        {
            string path = Path.Combine(Path.GetTempPath(), $"dnd_gritty_{tag}_{Guid.NewGuid():N}.db");
            ScratchFiles.Add(path);

            var world = new World();
            world.Db = new DatabaseManager(path);
            world.Db.InitializeSchema(_schemaPath);
            world.Bus = new EventBus();
            world.GameState = new GameStateQueries(world.Db);
            world.Players = new PlayerQueries(world.Db);
            world.State = new GlobalState();
            world.Clock = new TimeManager(world.Db, world.GameState, world.State, world.Bus);
            world.Clock.Initialize(2026);
            world.Graph = new RelationshipGraph();
            world.Graph.AttachTo(world.Bus);
            world.LifeSim = new LifeSimManager();

            world.Applier = new EventConsequenceApplier(
                world.Db, world.Players, library, world.Graph, world.Bus, world.GameState, rngSeed: 4242UL);
            world.Applier.AttachTo(world.Bus);
            world.View = world.Db.CreateReadOnlyView();
            world.Dispatcher = new EventDispatcher(
                library, new NarrativePollQueries(world.View), world.Bus, dispatcherSeed, pollIntervalMs);

            world.Bus.Subscribe<GrittyEventFiredEvent>(world.Fired.Add);
            return world;
        }

        public void AddPlayer(
            string id, int age, int? teamId, double funds = 100_000, int recklessness = 0)
        {
            Players.Insert(new PlayerRow
            {
                PlayerId = id,
                FirstName = "Test",
                LastName = id,
                Age = age,
                TeamId = teamId,
                Funds = funds,
                HealthCeiling = 100,
                Recklessness = recklessness,
                BaseballInterest = 0,
                DetectionRisk = 0,
            });
        }

        public void Dispose()
        {
            Dispatcher.Dispose();
            View.Dispose();
            Db.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------

    private static bool Throws(string json)
    {
        try
        {
            GrittyEventJson.Parse(json);
            return false;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static bool ThrowsAny(Action action)
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

    private static void Check(string name, bool pass, string detail = "") =>
        Results.Add((name, pass, detail));

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
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
            // Scratch files live in %TEMP%; a straggler is harmless.
        }
    }
}
