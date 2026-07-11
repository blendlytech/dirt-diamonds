using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Equipment;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Economy.Items;
using DirtAndDiamonds.Economy.Phone;
using DirtAndDiamonds.Narrative.Contacts;
using DirtAndDiamonds.Narrative.Events;
using DirtAndDiamonds.Narrative.Social;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;
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
            RunContactRegistryChecks();
            RunNarrativeLogChecks();
            RunConditionChecks();
            RunDispatcherChecks();
            RunCascadeAndWriterChecks();
            RunChoiceUiChecks();
            RunMarriageConceptionChecks();
            RunPersonStatChecks();
            RunHsDatingArcChecks();
            RunEndPartnershipChecks();
            RunRekindleChecks();
            RunStrictnessChecks();
            RunTeammateExOfPartnerChecks();
            RunTierGpaChecks();
            RunHsOnboardingArcChecks();
            RunChildDevelopmentChecks();
            RunNpcAutonomyChecks();
            RunStressChecks();
            RunHustleIntegrationChecks();
            RunEquipmentIntegrationChecks();
            RunItemCatalogChecks();
            RunPhoneAndFamilyChecks();
            RunAbsenceChecks();
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
            batchFiles.Length >= 9
            && wholeFolder.Count == 79
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
            && wholeFolder.TryGetById("child_is_born", out _)
            && wholeFolder.TryGetById("narcotics_arrest", out _)
            && wholeFolder.TryGetById("suspended_lifetime_watch", out _)
            && wholeFolder.TryGetById("suspended_repeat_violation", out _)
            && wholeFolder.TryGetById("suspended_ped_test_flag", out _)
            && wholeFolder.TryGetById("sidelined_by_injury", out _)
            && wholeFolder.TryGetById("clubhouse_welcome", out _)
            && wholeFolder.TryGetById("first_paycheck", out _)
            && wholeFolder.TryGetById("splurge_callback", out _)
            && wholeFolder.TryGetById("frugal_callback", out _)
            && wholeFolder.TryGetById("hs_crush_forms", out _)
            && wholeFolder.TryGetById("hs_ask_out", out _)
            && wholeFolder.TryGetById("hs_parental_approval", out _)
            && wholeFolder.TryGetById("hs_sneaking_out", out _)
            && wholeFolder.TryGetById("hs_caught_sneaking_out", out _)
            && wholeFolder.TryGetById("hs_snuck_out_clean", out _)
            && wholeFolder.TryGetById("hs_breakup", out _)
            && wholeFolder.TryGetById("hs_rekindle", out _)
            && wholeFolder.TryGetById("hs_pregnancy_scare", out _)
            && wholeFolder.TryGetById("hs_pregnancy_decision", out _)
            && wholeFolder.TryGetById("hs_baby_born", out _)
            && wholeFolder.TryGetById("hs_clubhouse_cancer", out _)
            && wholeFolder.TryGetById("hs_hometown_anchor", out _)
            && wholeFolder.TryGetById("marry_hs_sweetheart", out _)
            && wholeFolder.TryGetById("marriage_on_the_rocks", out _)
            && wholeFolder.TryGetById("divorce_papers", out _)
            && wholeFolder.TryGetById("hs_bedtime_story", out _)
            && wholeFolder.TryGetById("hs_backyard_catch", out _)
            && wholeFolder.TryGetById("hs_paying_for_lessons", out _)
            && wholeFolder.TryGetById("hs_missed_the_recital", out _)
            && wholeFolder.TryGetById("hs_family_game_night", out _)
            && wholeFolder.TryGetById("hs_the_hard_conversation", out _)
            && wholeFolder.TryGetById("hs_school_calls_about_neglect", out _)
            && wholeFolder.TryGetById("hs_course_correction", out _)
            && wholeFolder.TryGetById("hs_first_day", out _)
            && wholeFolder.TryGetById("hs_meet_coach", out _)
            && wholeFolder.TryGetById("hs_tryouts", out _)
            && wholeFolder.TryGetById("hs_first_practice", out _)
            && wholeFolder.TryGetById("hs_first_game_nerves", out _)
            && wholeFolder.TryGetById("hs_lunchroom", out _)
            && wholeFolder.TryGetById("hs_first_report_card", out _)
            && wholeFolder.TryGetById("hs_report_card_slipping", out _)
            && wholeFolder.TryGetById("hs_coach_checkin", out _)
            && wholeFolder.TryGetById("hs_crosstown_rival_seed", out _)
            && wholeFolder.TryGetById("hs_homecoming", out _)
            && wholeFolder.TryGetById("hs_exam_crunch", out _)
            && wholeFolder.TryGetById("hs_cheating_fallout", out _)
            && wholeFolder.TryGetById("hs_part_time_job", out _)
            && wholeFolder.TryGetById("hs_payday", out _)
            && wholeFolder.TryGetById("hs_job_vs_practice", out _)
            && wholeFolder.TryGetById("hs_house_party", out _)
            && wholeFolder.TryGetById("hs_party_grounded", out _)
            && wholeFolder.TryGetById("hs_party_blows_over", out _)
            && wholeFolder.TryGetById("hs_crosstown_showdown", out _)
            && wholeFolder.TryGetById("hs_error_costs_the_game", out _)
            && wholeFolder.TryGetById("hs_family_money_tight", out _)
            && wholeFolder.TryGetById("hs_college_letter", out _)
            && wholeFolder.TryGetById("hs_scout_in_stands", out _),
            $"{batchFiles.Length} files, {wholeFolder.Count} events");
    }

    // ------------------------------------------------------------------
    // 1b. Contact registry (Phase 10b, presentation_layer_narrative.md §4.2)
    // ------------------------------------------------------------------

    private static void RunContactRegistryChecks()
    {
        Console.WriteLine("--- Contact registry ---");

        string contactsPath = Path.Combine(_repoRoot, "Assets", "Narrative", "Contacts", "contacts.json");
        ContactRegistry registry = ContactJson.Parse(File.ReadAllText(contactsPath));
        Check("shipped contacts.json parses and resolves 'unknown' to \"Unknown Number\"",
            registry.Resolve("unknown").DisplayName == "Unknown Number");

        ContactRegistry noUnknownAuthored = ContactJson.Parse(
            """{ "contacts": [ { "id": "someone", "display_name": "Someone", "role": "coach" } ] }""");
        Check("ContactRegistry synthesizes the 'unknown' fallback when a batch never authors it, and never throws on an unrecognized id",
            noUnknownAuthored.Resolve("unknown").Id == "unknown"
            && noUnknownAuthored.Resolve("nonexistent_contact_id").Id == "unknown");

        Check("ContactJson rejects an unrecognized role", ThrowsAny(() =>
            ContactJson.Parse("""{ "contacts": [ { "id": "x", "display_name": "X", "role": "wizard" } ] }""")));
        Check("ContactJson rejects a duplicate contact id", ThrowsAny(() =>
            ContactJson.Parse(
                """{ "contacts": [ { "id": "dup", "display_name": "A", "role": "coach" }, { "id": "dup", "display_name": "B", "role": "agent" } ] }""")));

        GrittyEventLibrary tagged = GrittyEventJson.Parse(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "contact": "coach_reyes", "choices": [ { "id": "a" } ] } ] }""");
        GrittyEventLibrary untagged = GrittyEventJson.Parse(
            """{ "events": [ { "id": "y", "scope": "any", "weight": 0.5, "choices": [ { "id": "a" } ] } ] }""");
        Check("GrittyEventJson: an authored \"contact\" parses verbatim; an omitted one defaults to \"unknown\"",
            tagged.TryGetById("x", out GrittyEventDefinition x) && x.ContactId == "coach_reyes"
            && untagged.TryGetById("y", out GrittyEventDefinition y) && y.ContactId == GrittyEventJson.UnknownContactId);

        // check_event_graph_integrity's 10b addendum: every shipped event's
        // contact resolves in the registry (or is the reserved unknown id) —
        // load the whole Content folder AND the whole registry together, the
        // same "load together" discipline as RunContentChecks' cross-batch
        // id-collision check.
        string contentDir = Path.Combine(_repoRoot, "Assets", "Narrative", "Events", "Content");
        string[] batchFiles = Directory.GetFiles(contentDir, "*.json");
        var batchDocuments = new string[batchFiles.Length];
        for (int i = 0; i < batchFiles.Length; i++)
        {
            batchDocuments[i] = File.ReadAllText(batchFiles[i]);
        }
        GrittyEventLibrary allEvents = GrittyEventJson.Parse(batchDocuments);
        int unresolved = 0;
        int taggedNonUnknown = 0;
        foreach (GrittyEventDefinition definition in allEvents.Events)
        {
            if (definition.ContactId != ContactRegistry.UnknownContactId && !registry.Contains(definition.ContactId))
            {
                unresolved++;
            }
            if (definition.ContactId != ContactRegistry.UnknownContactId)
            {
                taggedNonUnknown++;
            }
        }
        Check("every shipped event's contact resolves in the registry (or is the reserved 'unknown' id)",
            unresolved == 0 && allEvents.Count == 79 && taggedNonUnknown > 0,
            $"{unresolved} unresolved of {allEvents.Count} events, {taggedNonUnknown} tagged non-unknown");
    }

    // ------------------------------------------------------------------
    // 1c. Narrative log write (Phase 10b, presentation_layer_narrative.md §4.3)
    // ------------------------------------------------------------------

    private static void RunNarrativeLogChecks()
    {
        Console.WriteLine("--- Narrative log (Burner Phone read-model) ---");

        // A resolved autopilot fire writes exactly one narrative_msg row,
        // with the fire's own day/season and the picked choice's label.
        {
            using World world = World.Create("narrativeLog", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "tagged", "scope": "any", "weight": 1.0, "contact": "coach_reyes",
                  "choices": [ { "id": "only", "label": "Take it in stride" } ] } ] }
                """));
            world.AddPlayer("p1", age: 27, teamId: 1);
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var rows = new List<NarrativeMessageRow>();
            world.NarrativeLog.LoadForPlayer("p1", rows);
            Check("resolved fire logs exactly one narrative_msg row with contact/prompt/choice/day/season",
                rows.Count == 1 && rows[0].ContactId == "coach_reyes" && rows[0].Choice == "Take it in stride"
                && rows[0].ChoiceIndex == 0 && rows[0].GameDay == 2 && rows[0].SeasonYear == 2026,
                rows.Count == 1 ? $"contact={rows[0].ContactId} choice={rows[0].Choice} day={rows[0].GameDay} season={rows[0].SeasonYear}" : $"{rows.Count} rows");
        }

        // An untagged event's fire logs against the reserved "unknown" contact.
        {
            using World world = World.Create("narrativeLogUnknown", GrittyEventJson.Parse(
                """{ "events": [ { "id": "untagged", "scope": "any", "weight": 1.0, "choices": [ { "id": "only" } ] } ] }"""));
            world.AddPlayer("p1", age: 27, teamId: 1);
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var rows = new List<NarrativeMessageRow>();
            world.NarrativeLog.LoadForPlayer("p1", rows);
            Check("an untagged event's logged message defaults to the 'unknown' contact",
                rows.Count == 1 && rows[0].ContactId == "unknown");
        }

        // A forfeited stale pending choice still logs — with ITS OWN fire day,
        // not the day it was bumped, so the thread reflects the true timeline.
        {
            using World world = World.Create("narrativeLogForfeit", GrittyEventJson.Parse(
                """
                { "events": [
                  { "id": "choiceA", "scope": "avatar", "weight": 1.0, "cooldown_days": 5, "contact": "agent_diaz",
                    "choices": [ { "id": "a1", "autopilot_weight": 1 } ] },
                  { "id": "choiceB", "scope": "avatar", "weight": 1.0, "contact": "girlfriend_jess",
                    "choices": [ { "id": "b1", "autopilot_weight": 1 } ] }
                ] }
                """));
            world.Applier.AutopilotAvatarChoices = false;
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();

            // Day 2: choiceA fires and pauses (never resolved by the player).
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            // Day 3: choiceB fires, forfeiting choiceA to autopilot — choiceA
            // logs with day 2 (its own fire day), not day 3.
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var rows = new List<NarrativeMessageRow>();
            world.NarrativeLog.LoadForPlayer("hero", rows);
            Check("a forfeited pending choice logs with its ORIGINAL fire day, not the bump day",
                rows.Count == 1 && rows[0].ContactId == "agent_diaz" && rows[0].GameDay == 2,
                rows.Count == 1 ? $"contact={rows[0].ContactId} day={rows[0].GameDay}" : $"{rows.Count} rows");

            // The player answers choiceB — a second row lands, ordered after.
            world.Applier.ResolveChoice(0);
            world.Bus.DispatchPending();
            world.NarrativeLog.LoadForPlayer("hero", rows);
            Check("resolving the still-open choice appends a second row, oldest first",
                rows.Count == 2 && rows[0].ContactId == "agent_diaz" && rows[1].ContactId == "girlfriend_jess"
                && rows[1].GameDay == 3);
        }
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

        // Avatar starvation regression guard (Act-1 Stage-3 review fix). The
        // live-save failure mode: the avatar's Players row is created after
        // world generation, so it scans LAST in the poll; on a full league,
        // scope:any NPC texture exhausted MaxFiresPerDay every single day
        // (111/111 days at exactly 8 NPC fires, zero avatar fires ever) and
        // the avatar never received an event in the save's life. The fix
        // hoists the avatar to the front of the sweep — mirrored here with
        // the avatar deliberately inserted last behind a cap-saturating NPC
        // population.
        {
            using World world = World.Create("avatarStarvation", GrittyEventJson.Parse(
                """
                { "events": [
                  { "id": "npc_texture", "scope": "any", "weight": 1.0,
                    "prerequisites": [ { "field": "age", "op": ">=", "value": 21 } ],
                    "choices": [ { "id": "a" } ] },
                  { "id": "avatar_beat", "scope": "avatar", "weight": 1.0,
                    "choices": [ { "id": "a" } ] }
                ] }
                """));
            for (int i = 0; i < EventDispatcher.MaxFiresPerDay + 4; i++)
            {
                world.AddPlayer($"npc{i}", age: 27, teamId: 1);
            }
            world.AddPlayer("hero", age: 16, teamId: 1); // LAST row, the live shape
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce(); // record the boot day
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            int fired = world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("the avatar cannot be starved out of its day by cap-saturating NPC texture (evaluates first despite scanning last)",
                world.Fired.Any(f => f.SubjectPlayerId == "hero" && f.EventId == "avatar_beat"),
                string.Join(",", world.Fired.Select(f => f.SubjectPlayerId).Take(3)));
            Check("the MaxFiresPerDay valve still holds with the avatar hoisted (8 total, avatar's fire among them)",
                fired == EventDispatcher.MaxFiresPerDay && world.Fired.Count == EventDispatcher.MaxFiresPerDay,
                $"{fired} fires");
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
    // 4d. PersonStat consequence (HS-5, high_school_person_layer.md §9)
    // ------------------------------------------------------------------

    private static void RunPersonStatChecks()
    {
        Console.WriteLine("--- PersonStat consequence ---");

        Check("loader accepts person_stat with a known stat name", !Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "person_stat", "stat": "confidence", "amount": 3 } ] } ] } ] }"""));
        Check("loader rejects person_stat with an unknown stat name", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "person_stat", "stat": "gpa", "amount": 1 } ] } ] } ] }"""));

        using (World world = World.Create("personStat", GrittyEventJson.Parse(
            """
            { "events": [ { "id": "x", "scope": "avatar", "weight": 1.0,
              "choices": [ { "id": "a", "consequences": [
                { "type": "person_stat", "stat": "confidence", "amount": 7 },
                { "type": "person_stat", "stat": "morality", "amount": -60 } ] } ] } ] }
            """)))
        {
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");

            // The gritty→PersonStatImpulseEvent gap fix: gritty PersonStat
            // consequences must mirror into the Life sim exactly like
            // ItemService's HS-4 rewards already do (Tools/GrittyEventsHarness
            // "HS-4 publish" checks, ItemService.cs's ApplySelfBuyTransportReward
            // precedent) — subscribed before the fire so nothing is missed.
            var statImpulses = new List<PersonStatImpulseEvent>();
            var leverChanges = new List<PersonLeversChangedEvent>();
            world.Bus.Subscribe<PersonStatImpulseEvent>(statImpulses.Add);
            world.Bus.Subscribe<PersonLeversChangedEvent>(leverChanges.Add);

            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            bool found = world.Persons.TryGet("hero", out PersonRow row);
            Check("person_stat consequence adjusts the targeted column and leaves the rest neutral",
                found && row.Confidence == 57 && row.Discipline == 50 && row.Happiness == 50,
                $"confidence={row.Confidence}, discipline={row.Discipline}");
            Check("person_stat clamps at the floor (a -60 delta off neutral 50 bottoms out at 0, never negative)",
                found && row.Morality == 0, $"morality={row.Morality}");

            Check("gritty person_stat publishes PersonStatImpulseEvent with the ACTUAL clamped movement (confidence +7, morality -50 not -60)",
                statImpulses.Count == 2
                && statImpulses.Any(i => i.PlayerId == "hero" && i.Stat == (int)PersonStatId.Confidence && Math.Abs(i.Delta - 7f) < 1e-6)
                && statImpulses.Any(i => i.PlayerId == "hero" && i.Stat == (int)PersonStatId.Morality && Math.Abs(i.Delta - (-50f)) < 1e-6),
                string.Join(",", statImpulses.Select(i => $"{i.Stat}:{i.Delta}")));
            Check("gritty person_stat re-announces PersonLeversChangedEvent when a §6.2 sim lever moved (confidence is one; morality is not)",
                leverChanges.Count == 1 && leverChanges[0].PlayerId == "hero"
                && leverChanges[0].Happiness == 50 && leverChanges[0].Confidence == 57 && leverChanges[0].Teamwork == 50,
                $"count={leverChanges.Count}");
        }

        using (World world = World.Create("personStatNet", GrittyEventJson.Parse(
            """
            { "events": [ { "id": "y", "scope": "avatar", "weight": 1.0,
              "choices": [ { "id": "a", "consequences": [
                { "type": "person_stat", "stat": "happiness", "amount": 5 },
                { "type": "person_stat", "stat": "happiness", "amount": -3 },
                { "type": "person_stat", "stat": "teamwork", "amount": 10 } ] } ] } ] }
            """)))
        {
            world.AddPlayer("hero2", age: 17, teamId: 1);
            PersonRow maxedTeamwork = PersonRow.Neutral("hero2");
            maxedTeamwork.Teamwork = 100;
            world.Persons.Upsert(maxedTeamwork);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero2");

            var statImpulses = new List<PersonStatImpulseEvent>();
            world.Bus.Subscribe<PersonStatImpulseEvent>(statImpulses.Add);

            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            Check("a stat touched twice in one choice nets to ONE publish carrying the net delta (+5 then -3 = +2)",
                statImpulses.Count(i => i.Stat == (int)PersonStatId.Happiness) == 1
                && Math.Abs(statImpulses.First(i => i.Stat == (int)PersonStatId.Happiness).Delta - 2f) < 1e-6,
                string.Join(",", statImpulses.Select(i => $"{i.Stat}:{i.Delta}")));
            Check("a consequence that cannot actually move (already at the 100 cap) publishes no impulse at all",
                statImpulses.All(i => i.Stat != (int)PersonStatId.Teamwork));
        }
    }

    // ------------------------------------------------------------------
    // 4e. HS dating funnel flow (HS-5 content, high_school_person_layer.md §9)
    // ------------------------------------------------------------------

    private static void RunHsDatingArcChecks()
    {
        Console.WriteLine("--- HS dating funnel ---");

        // Mirrors hs_dating_events.json's hs_crush_forms -> hs_ask_out shape
        // with a minimal inline pair (isolates the mechanic from future
        // content edits, same discipline as RunMarriageConceptionChecks §4).
        using World world = World.Create("hsDating", GrittyEventJson.Parse(
            """
            { "events": [
              { "id": "crush", "scope": "avatar", "weight": 1.0,
                "prerequisites": [ { "flag_inactive": "hs_dating" }, { "flag_inactive": "hs_interest" } ],
                "choices": [ { "id": "shoot_shot", "consequences": [
                  { "type": "set_flag", "flag": "hs_interest" },
                  { "type": "person_stat", "stat": "confidence", "amount": 5 } ] } ] },
              { "id": "ask", "scope": "avatar", "weight": 1.0,
                "prerequisites": [ { "flag_active": "hs_interest" } ],
                "choices": [ { "id": "ask_out", "consequences": [
                  { "type": "relationship", "kind": "partner", "affinity": 50, "target": "teammate" },
                  { "type": "set_flag", "flag": "hs_dating" },
                  { "type": "clear_flag", "flag": "hs_interest" } ] } ] }
            ] }
            """));
        world.AddPlayer("hero", age: 17, teamId: 101);
        world.AddPlayer("crush_candidate", age: 17, teamId: 101);
        world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
        world.Dispatcher.PollOnce();

        world.Clock.AdvanceDay(); // day 2: crush
        world.Bus.DispatchPending();
        world.Dispatcher.PollOnce();
        world.Bus.DispatchPending();

        world.Clock.AdvanceDay(); // day 3: ask (hs_interest now active)
        world.Bus.DispatchPending();
        world.Dispatcher.PollOnce();
        world.Bus.DispatchPending();

        var flagRows = new List<EntityFlagRow>();
        world.Players.LoadActiveFlags("hero", flagRows);
        bool dating = flagRows.Any(f => f.FlagName == "hs_dating");
        bool interestCleared = flagRows.All(f => f.FlagName != "hs_interest");

        var edges = new List<RelationshipEdge>();
        world.Graph.GetEdgesFor("hero", edges);
        bool partnered = edges.Any(e => e.Kind == RelationshipKind.Partner
            && e.OtherId == "crush_candidate" && e.Affinity == 50);

        world.Persons.TryGet("hero", out PersonRow row);

        Check("HS dating funnel: crush -> ask_out ends in a Partner edge + hs_dating flag + confidence bump",
            world.Fired.Count == 2
            && world.Fired[0].EventId == "crush" && world.Fired[1].EventId == "ask"
            && dating && interestCleared && partnered && row.Confidence == 55,
            $"fired: {string.Join(",", world.Fired.Select(f => f.EventId))}, dating={dating}, partnered={partnered}, confidence={row.Confidence}");
    }

    // ------------------------------------------------------------------
    // 4f. EndPartnership consequence (HS-5 breakup/divorce realism)
    // ------------------------------------------------------------------

    private static void RunEndPartnershipChecks()
    {
        Console.WriteLine("--- EndPartnership (breakup/divorce) ---");

        Check("loader accepts end_partnership into friend", !Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "end_partnership", "kind": "friend", "affinity": 10 } ] } ] } ] }"""));
        Check("loader accepts end_partnership into rival", !Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "end_partnership", "kind": "rival", "affinity": -30 } ] } ] } ] }"""));
        Check("loader rejects end_partnership into partner (a breakup can't mint a Partner)", Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "end_partnership", "kind": "partner", "affinity": 10 } ] } ] } ] }"""));
        Check("loader rejects end_partnership without an affinity", Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "end_partnership", "kind": "friend" } ] } ] } ] }"""));

        // Full lifecycle: marry -> divorce (edge RECLASSIFIED, never deleted)
        // -> remarry (exclusivity guard reopened). Team-targeted so the two
        // partner picks are deterministic by construction: "marry" targets the
        // only opponent (ex, team 2); "remarry" targets the only teammate
        // (rebound, team 1).
        {
            using World world = World.Create("endPartnership", GrittyEventJson.Parse(
                """
                { "events": [
                  { "id": "marry", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_inactive": "married" }, { "flag_inactive": "divorced" } ],
                    "choices": [ { "id": "wed", "consequences": [
                      { "type": "relationship", "kind": "partner", "affinity": 60, "target": "opponent" },
                      { "type": "set_flag", "flag": "married" } ] } ] },
                  { "id": "split", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_active": "married" } ],
                    "choices": [ { "id": "sign", "consequences": [
                      { "type": "end_partnership", "kind": "rival", "affinity": -30 },
                      { "type": "clear_flag", "flag": "married" },
                      { "type": "set_flag", "flag": "divorced" } ] } ] },
                  { "id": "remarry", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_active": "divorced" }, { "flag_inactive": "remarried" } ],
                    "choices": [ { "id": "wed_again", "consequences": [
                      { "type": "relationship", "kind": "partner", "affinity": 45, "target": "teammate" },
                      { "type": "set_flag", "flag": "remarried" } ] } ] }
                ] }
                """));
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.AddPlayer("ex", age: 27, teamId: 2);
            world.AddPlayer("rebound", age: 27, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            var rivalryEvents = new List<RivalryChangedEvent>();
            world.Bus.Subscribe<RivalryChangedEvent>(rivalryEvents.Add);
            world.Dispatcher.PollOnce();

            world.Clock.AdvanceDay(); // day 2: marry (ex is the only opponent)
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Clock.AdvanceDay(); // day 3: split
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("hero", edges);
            Check("divorce reclassifies the Partner edge in place: one Rival edge to the ex at -30, zero Partner edges",
                edges.Count == 1 && edges[0].OtherId == "ex"
                && edges[0].Kind == RelationshipKind.Rival && edges[0].Affinity == -30,
                $"{edges.Count} edges, kind={(edges.Count > 0 ? edges[0].Kind.ToString() : "none")}");
            Check("the bitter-ex reclassification rides the Phase-6 rivalry transport (intensity 30 published)",
                rivalryEvents.Count == 1 && rivalryEvents[0].Intensity == 30,
                $"{rivalryEvents.Count} rivalry events");

            world.Clock.AdvanceDay(); // day 4: remarry (rebound is the only teammate)
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Graph.GetEdgesFor("hero", edges);
            bool remarried = edges.Any(e => e.Kind == RelationshipKind.Partner
                && e.OtherId == "rebound" && e.Affinity == 45);
            bool exHistoryKept = edges.Any(e => e.Kind == RelationshipKind.Rival && e.OtherId == "ex");
            Check("exclusivity guard reopens after the divorce: remarriage lands a fresh Partner edge, the ex stays as graph history",
                world.Fired.Count == 3 && remarried && exHistoryKept && edges.Count == 2,
                $"{edges.Count} edges, remarried={remarried}, exKept={exHistoryKept}");
        }

        // Unpartnered subject: end_partnership is a skip-by-design no-op.
        {
            using World world = World.Create("endPartnershipSolo", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "split_solo", "scope": "avatar", "weight": 1.0,
                  "choices": [ { "id": "sign", "consequences": [
                    { "type": "end_partnership", "kind": "rival", "affinity": -30 } ] } ] } ] }
                """));
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("hero", edges);
            Check("end_partnership on an unpartnered subject is a clean no-op (fires, writes nothing)",
                world.Fired.Count == 1 && edges.Count == 0,
                $"{world.Fired.Count} fires, {edges.Count} edges");
        }
    }

    // ------------------------------------------------------------------
    // 4g. RekindlePartnership consequence (HS-5 getting back together)
    // ------------------------------------------------------------------

    private static void RunRekindleChecks()
    {
        Console.WriteLine("--- RekindlePartnership (getting back together) ---");

        Check("loader accepts rekindle_partnership on a scope-avatar event", !Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "rekindle_partnership", "affinity": 40 } ] } ] } ] }"""));
        Check("loader rejects rekindle_partnership on a scope-any event (only the avatar's romance history is tracked)", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "rekindle_partnership", "affinity": 40 } ] } ] } ] }"""));
        Check("loader rejects rekindle_partnership without an affinity", Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "rekindle_partnership" } ] } ] } ] }"""));

        // Full lifecycle: partner -> breakup (edge reclassified Rival AND the
        // ex RECORDED in Game_State) -> rekindle (the SAME edge re-minted
        // Partner, rivalry dissolved through the Phase-6 transport).
        // Opponent-targeted so the partner pick is deterministic: "ex" is the
        // only opponent.
        {
            using World world = World.Create("rekindle", GrittyEventJson.Parse(
                """
                { "events": [
                  { "id": "date", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_inactive": "dating" }, { "flag_inactive": "split_up" } ],
                    "choices": [ { "id": "ask", "consequences": [
                      { "type": "relationship", "kind": "partner", "affinity": 50, "target": "opponent" },
                      { "type": "set_flag", "flag": "dating" } ] } ] },
                  { "id": "split", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_active": "dating" } ],
                    "choices": [ { "id": "end_it", "consequences": [
                      { "type": "end_partnership", "kind": "rival", "affinity": -25 },
                      { "type": "clear_flag", "flag": "dating" },
                      { "type": "set_flag", "flag": "split_up" } ] } ] },
                  { "id": "rekindle", "scope": "avatar", "weight": 1.0,
                    "prerequisites": [ { "flag_active": "split_up" } ],
                    "choices": [ { "id": "reach_out", "consequences": [
                      { "type": "rekindle_partnership", "affinity": 40 },
                      { "type": "set_flag", "flag": "dating" },
                      { "type": "clear_flag", "flag": "split_up" } ] } ] }
                ] }
                """));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.AddPlayer("ex", age: 17, teamId: 2);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            var rivalryEvents = new List<RivalryChangedEvent>();
            world.Bus.Subscribe<RivalryChangedEvent>(rivalryEvents.Add);
            world.Dispatcher.PollOnce();

            world.Clock.AdvanceDay(); // day 2: date (ex is the only opponent)
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Clock.AdvanceDay(); // day 3: split
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            bool exRecorded = world.GameState.TryGetText(GameStateKeys.AvatarExPartnerId, out string exId);
            Check("an avatar breakup records WHO in Game_State (avatar_ex_partner_id, written in the fire's own batch)",
                exRecorded && exId == "ex",
                $"recorded={exRecorded}, id={(exRecorded ? exId : "none")}");

            world.Clock.AdvanceDay(); // day 4: rekindle
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("hero", edges);
            Check("rekindle re-mints the Partner edge with the recorded ex at the authored affinity (the exact edge, not a pool draw)",
                world.Fired.Count == 3 && edges.Count == 1 && edges[0].OtherId == "ex"
                && edges[0].Kind == RelationshipKind.Partner && edges[0].Affinity == 40,
                $"{world.Fired.Count} fires, {edges.Count} edges, kind={(edges.Count > 0 ? edges[0].Kind.ToString() : "none")}");
            Check("the bitter-ex rivalry dissolves through the Phase-6 transport (intensity 25 on the split, 0 on the rekindle)",
                rivalryEvents.Count == 2 && rivalryEvents[0].Intensity == 25 && rivalryEvents[1].Intensity == 0,
                $"{rivalryEvents.Count} rivalry events: [{string.Join(",", rivalryEvents.Select(e => e.Intensity))}]");
        }

        // No recorded ex: a rekindle fire is a clean skip (the empty-pool
        // precedent) — safe whether or not a breakup ever actually ran.
        {
            using World world = World.Create("rekindleNoEx", GrittyEventJson.Parse(
                """{ "events": [ { "id": "rekindle_solo", "scope": "avatar", "weight": 1.0, "choices": [ { "id": "reach_out", "consequences": [ { "type": "rekindle_partnership", "affinity": 40 } ] } ] } ] }"""));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("hero", edges);
            Check("rekindle with no recorded ex is a clean no-op (fires, writes nothing)",
                world.Fired.Count == 1 && edges.Count == 0,
                $"{world.Fired.Count} fires, {edges.Count} edges");
        }

        // Already partnered (stale ex recorded): the exclusivity guard wins —
        // rekindling can never accrete a second Partner edge.
        {
            using World world = World.Create("rekindlePartnered", GrittyEventJson.Parse(
                """{ "events": [ { "id": "rekindle_taken", "scope": "avatar", "weight": 1.0, "choices": [ { "id": "reach_out", "consequences": [ { "type": "rekindle_partnership", "affinity": 40 } ] } ] } ] }"""));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.AddPlayer("old_flame", age: 17, teamId: 2);
            world.AddPlayer("current", age: 17, teamId: 1);
            world.Graph.SetRelationship("hero", "old_flame", 10, RelationshipKind.Friend);
            world.Graph.SetRelationship("hero", "current", 55, RelationshipKind.Partner);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.GameState.SetText(GameStateKeys.AvatarExPartnerId, "old_flame");
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("hero", edges);
            bool currentKept = edges.Any(e => e.OtherId == "current"
                && e.Kind == RelationshipKind.Partner && e.Affinity == 55);
            bool oldFlameUntouched = edges.Any(e => e.OtherId == "old_flame"
                && e.Kind == RelationshipKind.Friend && e.Affinity == 10);
            Check("rekindle while already partnered skips (exclusivity guard): current partner and old flame both untouched",
                world.Fired.Count == 1 && edges.Count == 2 && currentKept && oldFlameUntouched,
                $"{edges.Count} edges, currentKept={currentKept}, oldFlameUntouched={oldFlameUntouched}");
        }

        // A stale/foreign recorded id can never mis-mint: no surviving edge
        // (e.g. an id inherited across a succession handoff) skips, and so
        // does an edge in a non-re-datable kind (Child is lineage state).
        {
            using World world = World.Create("rekindleStale", GrittyEventJson.Parse(
                """{ "events": [ { "id": "rekindle_stale", "scope": "avatar", "weight": 1.0, "choices": [ { "id": "reach_out", "consequences": [ { "type": "rekindle_partnership", "affinity": 40 } ] } ] } ] }"""));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.AddPlayer("mom", age: 44, teamId: null);
            world.Graph.SetRelationship("hero", "mom", 80, RelationshipKind.Child);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.GameState.SetText(GameStateKeys.AvatarExPartnerId, "ghost");
            world.Dispatcher.PollOnce();

            world.Clock.AdvanceDay(); // day 2: recorded id has no edge at all
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("hero", edges);
            Check("rekindle with a recorded id that has no surviving edge skips (succession-stale ids are inert)",
                edges.Count == 1 && edges[0].Kind == RelationshipKind.Child,
                $"{edges.Count} edges, kind={(edges.Count > 0 ? edges[0].Kind.ToString() : "none")}");

            world.GameState.SetText(GameStateKeys.AvatarExPartnerId, "mom");
            world.Clock.AdvanceDay(); // day 3: recorded id resolves to a Child edge
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            world.Graph.GetEdgesFor("hero", edges);
            Check("rekindle never re-types a Child edge (lineage state is not a re-datable kind)",
                edges.Count == 1 && edges[0].Kind == RelationshipKind.Child && edges[0].Affinity == 80,
                $"{edges.Count} edges, kind={(edges.Count > 0 ? edges[0].Kind.ToString() : "none")}");
        }

        // Only the avatar's romance history is tracked: a non-avatar
        // end_partnership reclassifies its edge exactly as before but
        // records nothing.
        {
            using World world = World.Create("rekindleNonAvatar", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "npc_split", "scope": "any", "weight": 1.0,
                  "prerequisites": [ { "field": "recklessness", "op": ">=", "value": 60 } ],
                  "choices": [ { "id": "end_it", "consequences": [
                    { "type": "end_partnership", "kind": "friend", "affinity": 5 } ] } ] } ] }
                """));
            world.AddPlayer("hero", age: 27, teamId: 1);
            world.AddPlayer("npc", age: 27, teamId: 1, recklessness: 80);
            world.AddPlayer("npc_partner", age: 27, teamId: 2);
            world.Graph.SetRelationship("npc", "npc_partner", 60, RelationshipKind.Partner);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("npc", edges);
            bool reclassified = edges.Count == 1 && edges[0].Kind == RelationshipKind.Friend && edges[0].Affinity == 5;
            bool nothingRecorded = !world.GameState.TryGetText(GameStateKeys.AvatarExPartnerId, out _);
            Check("a non-avatar end_partnership still reclassifies its edge but records no ex (avatar-only history)",
                world.Fired.Count == 1 && world.Fired[0].SubjectPlayerId == "npc" && reclassified && nothingRecorded,
                $"{world.Fired.Count} fires, reclassified={reclassified}, nothingRecorded={nothingRecorded}");
        }
    }

    // ------------------------------------------------------------------
    // 4h. SubjectField.Strictness (HS-5, person-layer doc §3 gate extension)
    // ------------------------------------------------------------------

    private static void RunStrictnessChecks()
    {
        Console.WriteLine("--- SubjectField.Strictness (Family_Background gate) ---");

        Check("loader accepts a strictness field prerequisite", !Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "prerequisites": [ { "field": "strictness", "op": ">=", "value": 55 } ], "choices": [ { "id": "a" } ] } ] }"""));
        Check("loader still rejects an unknown prerequisite field (closed vocabulary)", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "prerequisites": [ { "field": "allowance", "op": ">=", "value": 1 } ], "choices": [ { "id": "a" } ] } ] }"""));

        var strictSubject = new PollPlayerRow { PlayerId = "s", Strictness = 80 };
        var neutralSubject = new PollPlayerRow { PlayerId = "n", Strictness = 50 };
        var noFlags = new Dictionary<(string, string), long>();
        Check("ConditionEvaluator compares the strictness field",
            ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Strictness, FieldComparison.GreaterOrEqual, 55), in strictSubject, noFlags, 1)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Strictness, FieldComparison.GreaterOrEqual, 55), in neutralSubject, noFlags, 1));

        // The shipped content actually uses the extension: hs_parental_approval
        // carries the strictness gate (lenient households never get the beat;
        // safe to gate because the event sets no flags — nothing downstream
        // can orphan).
        string shipped = File.ReadAllText(Path.Combine(
            _repoRoot, "Assets", "Narrative", "Events", "Content", "hs_dating_events.json"));
        GrittyEventLibrary shippedLibrary = GrittyEventJson.Parse(shipped);
        bool gateAuthored = shippedLibrary.TryGetById("hs_parental_approval", out GrittyEventDefinition approval)
            && approval.Prerequisites.Any(p =>
                p.Kind == PrerequisiteKind.Field && p.Field == SubjectField.Strictness
                && p.Comparison == FieldComparison.GreaterOrEqual && p.Value == 55);
        Check("hs_parental_approval ships with the strictness >= 55 gate", gateAuthored);

        // End to end through the REAL poll join: a strict household fires the
        // gate, a family-less save COALESCEs to the neutral 50 and never does.
        {
            using World world = World.Create("strictGate", GrittyEventJson.Parse(
                """{ "events": [ { "id": "strict_dad", "scope": "avatar", "weight": 1.0, "prerequisites": [ { "field": "strictness", "op": ">=", "value": 55 } ], "choices": [ { "id": "a" } ] } ] }"""));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Persons.UpsertFamily(new FamilyBackgroundRow
            {
                PlayerId = "hero",
                WealthTier = 2,
                HouseholdIncome = 60000,
                Parent1Id = null,
                Parent2Id = null,
                HomeWifi = true,
                AllowanceWeekly = 20,
                Strictness = 80,
            });
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("a strictness-gated event fires through the real Family_Background poll join",
                world.Fired.Count == 1, $"{world.Fired.Count} fires");
        }
        {
            using World world = World.Create("strictNoRow", GrittyEventJson.Parse(
                """{ "events": [ { "id": "strict_dad", "scope": "avatar", "weight": 1.0, "prerequisites": [ { "field": "strictness", "op": ">=", "value": 55 } ], "choices": [ { "id": "a" } ] } ] }"""));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("no Family_Background row COALESCEs to the neutral 50 — the gate never fires for a family-less save",
                world.Fired.Count == 0, $"{world.Fired.Count} fires");
        }
    }

    // ------------------------------------------------------------------
    // 4h2. SubjectField.TeammateExOfPartner (schema v13) — the graph-reaching
    // prerequisite hs_clubhouse_cancer needed: "does a teammate have an ex
    // who is my current partner," answered from the read-only poll DB via
    // Relationship_History, never the main-thread RelationshipGraph.
    // ------------------------------------------------------------------

    private static RelationshipType ToDbType(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Rival => RelationshipType.Rival,
        RelationshipKind.Friend => RelationshipType.Friend,
        RelationshipKind.Partner => RelationshipType.Partner,
        RelationshipKind.Child => RelationshipType.Child,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Mirrors GameManager.PersistRelationships exactly, so a test can arrange graph state and have the poll's DB view see it, same as a real day-tick flush.</summary>
    private static void FlushRelationships(World world)
    {
        var edges = new List<RelationshipSeed>();
        world.Graph.CollectDirty(edges);
        foreach (RelationshipSeed edge in edges)
        {
            world.Players.UpsertRelationship(edge.PlayerAId, edge.PlayerBId, edge.Affinity, ToDbType(edge.Kind));
        }
        var history = new List<(string PlayerAId, string PlayerBId)>();
        world.Graph.CollectDirtyHistory(history);
        foreach ((string playerAId, string playerBId) in history)
        {
            world.Players.InsertRelationshipHistory(playerAId, playerBId);
        }
    }

    private static void RunTeammateExOfPartnerChecks()
    {
        Console.WriteLine("--- SubjectField.TeammateExOfPartner (graph-reaching prerequisite) ---");

        Check("loader accepts a teammate_ex_of_partner field prerequisite", !Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "prerequisites": [ { "field": "teammate_ex_of_partner", "op": ">=", "value": 1 } ], "choices": [ { "id": "a" } ] } ] }"""));
        Check("loader accepts a teammate_ex_of_partner relationship target", !Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "relationship", "kind": "rival", "affinity": -10, "target": "teammate_ex_of_partner" } ] } ] } ] }"""));

        var flagged = new PollPlayerRow { PlayerId = "s", TeammateExOfPartner = true };
        var clean = new PollPlayerRow { PlayerId = "n", TeammateExOfPartner = false };
        var noFlags = new Dictionary<(string, string), long>();
        Check("ConditionEvaluator compares the teammate_ex_of_partner field",
            ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.TeammateExOfPartner, FieldComparison.GreaterOrEqual, 1), in flagged, noFlags, 1)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.TeammateExOfPartner, FieldComparison.GreaterOrEqual, 1), in clean, noFlags, 1));

        // RelationshipGraph: the ledger is monotonic (a reclassify away from
        // Partner can't lose the fact) and its own dirty set flushes exactly
        // once per newly-true pair, same shape as CollectDirty.
        {
            var graph = new RelationshipGraph();
            graph.SetRelationship("x", "y", 50, RelationshipKind.Partner);
            Check("a fresh Partner edge is recorded ever-partner", graph.WasEverPartner("x", "y"));

            var dirty = new List<(string PlayerAId, string PlayerBId)>();
            Check("the pair appears exactly once in the first history flush",
                graph.CollectDirtyHistory(dirty) == 1 && dirty[0] == ("x", "y"));
            Check("a second flush with no new history is empty", graph.CollectDirtyHistory(dirty) == 0);

            graph.SetRelationship("x", "y", -25, RelationshipKind.Rival);
            Check("reclassifying the edge away from Partner does not lose the ever-partner fact, and re-marks nothing",
                graph.WasEverPartner("x", "y") && graph.CollectDirtyHistory(dirty) == 0);

            var fresh = new RelationshipGraph();
            fresh.SeedHistory(new List<(string PlayerAId, string PlayerBId)> { ("x", "y") });
            Check("SeedHistory hydrates without dirtying (boot-time load, not a live write)",
                fresh.WasEverPartner("x", "y") && fresh.CollectDirtyHistory(dirty) == 0);
        }

        // The shipped content actually uses the extension: the old hs_dating
        // pacing gate is untouched, the new field gate sits alongside it, and
        // the relationship consequences target the resolved ex, not a random
        // teammate pool draw.
        string shipped = File.ReadAllText(Path.Combine(
            _repoRoot, "Assets", "Narrative", "Events", "Content", "hs_dating_events.json"));
        GrittyEventLibrary shippedLibrary = GrittyEventJson.Parse(shipped);
        bool gateAuthored = shippedLibrary.TryGetById("hs_clubhouse_cancer", out GrittyEventDefinition cancer)
            && cancer.Prerequisites.Any(p =>
                p.Kind == PrerequisiteKind.FlagActive && p.FlagName == "hs_dating" && p.MinDaysSince == 21)
            && cancer.Prerequisites.Any(p =>
                p.Kind == PrerequisiteKind.Field && p.Field == SubjectField.TeammateExOfPartner
                && p.Comparison == FieldComparison.GreaterOrEqual && p.Value == 1)
            && cancer.Choices.Any(c => c.Consequences.Any(cons =>
                cons.Kind == ConsequenceKind.Relationship && cons.Target == RelationshipTargetSelector.TeammateExOfPartner));
        Check("hs_clubhouse_cancer ships gated on teammate_ex_of_partner (hs_dating pacing intact) and targets it, not a random teammate", gateAuthored);

        // End to end through the REAL poll join AND the applier: a teammate
        // with Relationship_History against the current partner is the ONLY
        // one who can be picked — never a random pool draw. FlushRelationships
        // rides the exact graph-write paths (Partner mint, then an
        // NpcAutonomyService-shaped breakup) production code takes, so this
        // exercises the real SetRelationship history hook, not a DB shortcut.
        {
            using World world = World.Create("clubhouseCancer", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "cancer", "scope": "avatar", "weight": 1.0,
                  "prerequisites": [ { "field": "teammate_ex_of_partner", "op": ">=", "value": 1 } ],
                  "choices": [ { "id": "confront", "consequences": [
                    { "type": "relationship", "kind": "rival", "affinity": -20, "target": "teammate_ex_of_partner" } ] } ] } ] }
                """));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.AddPlayer("partner", age: 17, teamId: 2);
            world.AddPlayer("ex_teammate", age: 17, teamId: 1);
            world.AddPlayer("other_teammate", age: 17, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");

            world.Graph.SetRelationship("hero", "partner", 50, RelationshipKind.Partner);
            world.Graph.SetRelationship("partner", "ex_teammate", 30, RelationshipKind.Partner);
            world.Graph.SetRelationship("partner", "ex_teammate", 10, RelationshipKind.Friend); // the breakup
            FlushRelationships(world);

            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            var edges = new List<RelationshipEdge>();
            world.Graph.GetEdgesFor("ex_teammate", edges);
            bool targetedExTeammate = edges.Any(e => e.OtherId == "hero" && e.Kind == RelationshipKind.Rival && e.Affinity == -20);
            world.Graph.GetEdgesFor("other_teammate", edges);
            bool untouchedOther = edges.Count == 0;
            Check("the event fires and its rival edge lands on the real teammate ex, never a random teammate",
                world.Fired.Count == 1 && targetedExTeammate && untouchedOther,
                $"{world.Fired.Count} fires, targeted={targetedExTeammate}, otherUntouched={untouchedOther}");
        }

        // The regression this whole feature closes: without any recorded ex
        // history, the event must NOT fire even though the subject is dating
        // someone and has teammates — flavor text is no longer enough.
        {
            using World world = World.Create("clubhouseNoHistory", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "cancer", "scope": "avatar", "weight": 1.0,
                  "prerequisites": [ { "field": "teammate_ex_of_partner", "op": ">=", "value": 1 } ],
                  "choices": [ { "id": "confront", "consequences": [
                    { "type": "relationship", "kind": "rival", "affinity": -20, "target": "teammate_ex_of_partner" } ] } ] } ] }
                """));
            world.AddPlayer("hero", age: 17, teamId: 1);
            world.AddPlayer("partner", age: 17, teamId: 2);
            world.AddPlayer("teammate", age: 17, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");

            world.Graph.SetRelationship("hero", "partner", 50, RelationshipKind.Partner);
            FlushRelationships(world); // no Relationship_History row for partner/teammate — no ex exists

            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            Check("no recorded ex-history ⇒ the event never fires, even with a partner and teammates present",
                world.Fired.Count == 0, $"{world.Fired.Count} fires");
        }
    }

    // ------------------------------------------------------------------
    // 4h3. SubjectField.Tier / SubjectField.Gpa (HS onboarding arc,
    // docs/design/hs_onboarding_events.md §1) — the load-bearing NULL-team
    // sentinel and the gpa report-card partition.
    // ------------------------------------------------------------------

    private static void RunTierGpaChecks()
    {
        Console.WriteLine("--- SubjectField.Tier / SubjectField.Gpa ---");

        GrittyEventLibrary tierField = GrittyEventJson.Parse(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "prerequisites": [ { "field": "tier", "op": "==", "value": 0 } ], "choices": [ { "id": "a" } ] } ] }""");
        Check("GrittyEventJson accepts the 'tier' prerequisite field",
            tierField.TryGetById("x", out GrittyEventDefinition tx) && tx.Prerequisites.Length == 1
            && tx.Prerequisites[0].Field == SubjectField.Tier && tx.Prerequisites[0].Comparison == FieldComparison.Equal
            && tx.Prerequisites[0].Value == 0);

        GrittyEventLibrary gpaField = GrittyEventJson.Parse(
            """{ "events": [ { "id": "y", "scope": "avatar", "weight": 0.5, "prerequisites": [ { "field": "gpa", "op": ">=", "value": 2.5 } ], "choices": [ { "id": "a" } ] } ] }""");
        Check("GrittyEventJson accepts the 'gpa' prerequisite field",
            gpaField.TryGetById("y", out GrittyEventDefinition ty) && ty.Prerequisites[0].Field == SubjectField.Gpa
            && ty.Prerequisites[0].Comparison == FieldComparison.GreaterOrEqual && ty.Prerequisites[0].Value == 2.5);

        Check("GrittyEventJson still rejects an unrecognized prerequisite field name", ThrowsAny(() =>
            GrittyEventJson.Parse(
                """{ "events": [ { "id": "z", "scope": "avatar", "weight": 0.5, "prerequisites": [ { "field": "nonsense_field", "op": "==", "value": 0 } ], "choices": [ { "id": "a" } ] } ] }""")));

        var noFlags = new Dictionary<(string, string), long>();
        var hsSubject = new PollPlayerRow { PlayerId = "hs", Tier = 0, Gpa = 2.5 };
        var proSubject = new PollPlayerRow { PlayerId = "pro", Tier = 5, Gpa = 2.5 };
        var unrostered = new PollPlayerRow { PlayerId = "parent", Tier = -1, Gpa = 2.5 };
        Check("tier==0 holds for an HS subject and not a pro subject",
            ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Tier, FieldComparison.Equal, 0), in hsSubject, noFlags, 1)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Tier, FieldComparison.Equal, 0), in proSubject, noFlags, 1));
        // The sentinel defeats every >= and == gate this arc (or any shipped
        // content) actually authors on tier -- both real gates are checked
        // here (onboarding's ==0, the rookie batch's >=2). NOTE (disclosed,
        // not a bug): a hypothetical "<" gate is NOT structurally defeated --
        // -1 < N for any N >= 0, so a future "tier < 2" gate on a scope:any
        // event would also match unrostered subjects. No shipped event uses
        // "<" on tier; this is a caveat for future authors, not a regression.
        Check("the NULL-team sentinel (-1) matches no real >= or == tier gate this arc authors",
            !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Tier, FieldComparison.GreaterOrEqual, 2), in unrostered, noFlags, 1)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Tier, FieldComparison.Equal, 0), in unrostered, noFlags, 1));

        var solidGpa = new PollPlayerRow { PlayerId = "solid", Gpa = 2.5 };
        var slippingGpa = new PollPlayerRow { PlayerId = "slipping", Gpa = 2.4 };
        Check("gpa >= 2.5 / < 2.5 partitions exactly at the report-card boundary (the schema default 2.5 lands on the solid branch)",
            ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Gpa, FieldComparison.GreaterOrEqual, 2.5), in solidGpa, noFlags, 1)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Gpa, FieldComparison.Less, 2.5), in solidGpa, noFlags, 1)
            && ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Gpa, FieldComparison.Less, 2.5), in slippingGpa, noFlags, 1)
            && !ConditionEvaluator.Holds(EventPrerequisite.ForField(SubjectField.Gpa, FieldComparison.GreaterOrEqual, 2.5), in slippingGpa, noFlags, 1));
    }

    // ------------------------------------------------------------------
    // 4h4. HS onboarding arc + rookie-batch tier gate (Act-1 fix,
    // docs/design/hs_onboarding_events.md) — the shipped content batches
    // loaded together, exactly as GameManager.LoadGrittyEventContent loads
    // the whole Content folder, so the tier-gate interaction between the
    // two files is proven against the REAL shipped JSON, not a mirror.
    // ------------------------------------------------------------------

    private static readonly string[] RookieBatchEventIds =
    {
        "clubhouse_welcome", "playbook_hazing", "homesick", "first_paycheck", "splurge_callback",
        "frugal_callback", "coach_pep_talk", "in_a_slump", "on_a_heater", "clubhouse_prank",
        "road_roommate", "rookie_advice_veteran",
    };

    private static readonly string[] OnboardingEventIds =
    {
        "hs_first_day", "hs_meet_coach", "hs_tryouts", "hs_first_practice", "hs_first_game_nerves",
        "hs_lunchroom", "hs_first_report_card", "hs_report_card_slipping", "hs_coach_checkin",
        "hs_crosstown_rival_seed", "hs_homecoming",
    };

    private static void RunHsOnboardingArcChecks()
    {
        Console.WriteLine("--- HS onboarding arc + rookie tier gate (shipped content) ---");

        string onboardingJson = File.ReadAllText(Path.Combine(
            _repoRoot, "Assets", "Narrative", "Events", "Content", "hs_onboarding_events.json"));
        string rookieJson = File.ReadAllText(Path.Combine(
            _repoRoot, "Assets", "Narrative", "Events", "Content", "rookie_season_events.json"));

        // Stage-3 review pins (design contract §9.1/§4.2): the per-event
        // shape of both shipped batches, so a future content edit can't
        // silently drop the tier floor off one rookie event or shrink an
        // onboarding beat below HIGH_SCHOOL.md's 3-choice rule. The World
        // blocks below prove the BEHAVIOR; these pin the AUTHORED bytes.
        {
            GrittyEventLibrary both = GrittyEventJson.Parse(new[] { onboardingJson, rookieJson });
            int onboardingOk = 0;
            foreach (string id in OnboardingEventIds)
            {
                if (both.TryGetById(id, out GrittyEventDefinition e)
                    && e.Scope == EventScope.Avatar
                    && e.Choices.Length == 3
                    && e.Prerequisites.Any(p => p.Kind == PrerequisiteKind.Field
                        && p.Field == SubjectField.Tier && p.Comparison == FieldComparison.Equal && p.Value == 0))
                {
                    onboardingOk++;
                }
            }
            Check("all 11 onboarding events resolve, are avatar-scoped, carry the tier==0 gate, and ship exactly 3 choices",
                onboardingOk == OnboardingEventIds.Length, $"{onboardingOk}/{OnboardingEventIds.Length}");

            int rookieOk = 0;
            foreach (string id in RookieBatchEventIds)
            {
                if (both.TryGetById(id, out GrittyEventDefinition e)
                    && e.Prerequisites.Any(p => p.Kind == PrerequisiteKind.Field
                        && p.Field == SubjectField.Tier && p.Comparison == FieldComparison.GreaterOrEqual && p.Value == 2))
                {
                    rookieOk++;
                }
            }
            Check("all 12 rookie events still resolve and every one carries the tier>=2 floor (no partial re-gate)",
                rookieOk == RookieBatchEventIds.Length, $"{rookieOk}/{RookieBatchEventIds.Length}");
        }

        // (a) + (b), combined: a fresh age-16 HS avatar. The week-one chain
        // fires on the EXACT pacing the design doc's flag graph (§4.1)
        // predicts -- deterministic because every week-one event is weight
        // 1.0 (never loses its roll) and each is gated behind the previous
        // one's flag, so file order + min_days_since alone decide the winner
        // every day. hs_first_report_card (also weight 1.0) becomes the
        // first still-open event the moment hs_debut_done is 21 days old,
        // so it fires on EXACTLY day 28 (7 + 21) regardless of RNG -- gpa
        // stays at the neutral default 2.5, so hs_report_card_slipping's
        // gpa<2.5 gate can never hold for this subject, making the solid
        // branch's day-28 fire deterministic too. Loading the rookie batch
        // in the SAME library proves the regression guard (b) for free: not
        // one of its 12 tier>=2-gated events can ever win a subject-day slot
        // for a tier-0 avatar.
        {
            using World world = World.Create("hsOnboardingChain", GrittyEventJson.Parse(new[] { onboardingJson, rookieJson }));
            var baseball = new BaseballQueries(world.Db);
            baseball.InsertTeam(new TeamRow { TeamId = 101, City = "Crestwood", Name = "Cardinals", Abbreviation = "CRC", League = "HS", Division = "A" });
            baseball.UpsertTeamTier(101, LeagueTier.HS);

            world.AddPlayer("hero", age: 16, teamId: 101);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce(); // records the boot day (day 1, never evaluated)

            for (int day = 2; day <= 28; day++)
            {
                world.Clock.AdvanceDay();
                world.Bus.DispatchPending();
                world.Dispatcher.PollOnce();
                world.Bus.DispatchPending();
            }

            bool FiredOn(string eventId, long onDay) => world.Fired.Any(f => f.EventId == eventId && f.Day == onDay);
            Check("hs_first_day fires on day 2 of a fresh HS save",
                FiredOn("hs_first_day", 2));
            Check("the week-one chain progresses one beat per day exactly on the flag graph's pacing (days 2-7)",
                FiredOn("hs_first_day", 2) && FiredOn("hs_meet_coach", 3) && FiredOn("hs_tryouts", 4)
                && FiredOn("hs_first_practice", 5) && FiredOn("hs_lunchroom", 6) && FiredOn("hs_first_game_nerves", 7),
                string.Join(",", world.Fired.Take(6).Select(f => $"{f.EventId}@{f.Day}")));
            Check("hs_first_report_card (solid branch, default gpa 2.5) fires deterministically on day 28 (debut+21), never the slipping branch",
                FiredOn("hs_first_report_card", 28) && world.Fired.All(f => f.EventId != "hs_report_card_slipping"));
            Check("not one rookie-batch event fires across the whole 28-day run despite sharing a library with a tier-0 avatar",
                world.Fired.All(f => !RookieBatchEventIds.Contains(f.EventId)),
                string.Join(",", world.Fired.Select(f => f.EventId).Where(id => RookieBatchEventIds.Contains(id))));
        }

        // (c) positive control: a tier>=2 (MinorA, the floor) avatar still
        // gets the rookie batch -- the gate excludes HS/College, not
        // professional tiers. Isolated to the rookie file alone (the
        // onboarding file is irrelevant here: every one of its events
        // requires tier==0, trivially false for this subject).
        {
            using World world = World.Create("rookiePositiveControl", GrittyEventJson.Parse(rookieJson));
            var baseball = new BaseballQueries(world.Db);
            baseball.InsertTeam(new TeamRow { TeamId = 201, City = "Rockford", Name = "River Cats", Abbreviation = "ROC", League = "MiLB", Division = "A" });
            baseball.UpsertTeamTier(201, LeagueTier.MinorA);

            world.AddPlayer("proHero", age: 20, teamId: 201);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "proHero");
            world.Dispatcher.PollOnce();

            for (int day = 2; day <= 60 && !world.Fired.Any(f => f.EventId == "clubhouse_welcome"); day++)
            {
                world.Clock.AdvanceDay();
                world.Bus.DispatchPending();
                world.Dispatcher.PollOnce();
                world.Bus.DispatchPending();
            }

            Check("a tier>=2 (MinorA) avatar still fires clubhouse_welcome -- the rookie batch is NOT globally blocked, only HS/College",
                world.Fired.Any(f => f.EventId == "clubhouse_welcome"),
                $"{world.Fired.Count} fires");
        }

        // (d) contaminated-save guard: an HS-tier avatar with rookie_settled
        // pre-set (the pre-fix live-save scenario the design doc worried
        // about) still never fires a single rookie event -- the tier floor
        // heals contamination with zero DB surgery -- while the onboarding
        // chain fires normally regardless (contamination doesn't cross-block).
        {
            using World world = World.Create("contaminatedSaveGuard", GrittyEventJson.Parse(new[] { onboardingJson, rookieJson }));
            var baseball = new BaseballQueries(world.Db);
            baseball.InsertTeam(new TeamRow { TeamId = 102, City = "Riverton", Name = "Rams", Abbreviation = "RIV", League = "HS", Division = "A" });
            baseball.UpsertTeamTier(102, LeagueTier.HS);

            world.AddPlayer("contaminatedHero", age: 16, teamId: 102);
            world.Players.SetFlag("contaminatedHero", "rookie_settled", true, 0);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "contaminatedHero");
            world.Dispatcher.PollOnce();

            for (int day = 2; day <= 10; day++)
            {
                world.Clock.AdvanceDay();
                world.Bus.DispatchPending();
                world.Dispatcher.PollOnce();
                world.Bus.DispatchPending();
            }

            Check("a pre-contaminated HS avatar (rookie_settled already active) still never fires a rookie event -- the tier floor holds",
                world.Fired.All(f => !RookieBatchEventIds.Contains(f.EventId)));
            Check("the onboarding chain still starts normally on a contaminated save (hs_first_day fires day 2)",
                world.Fired.Any(f => f.EventId == "hs_first_day" && f.Day == 2));
        }

        // (e) NULL-team subject + the real SQL join, end to end: an
        // unrostered subject (the seeded parent NPCs' shape) polls the -1
        // sentinel through NarrativePollQueries itself, not just the
        // evaluator -- proving the CASE WHEN p.team_id IS NULL branch in the
        // actual poll SQL, alongside a rostered HS/pro pair and a rostered
        // team with NO Team_Tiers row (the v6->v7 COALESCE-to-MLB(5)
        // backfill convention, still honored with the new join in place).
        // Also proves gpa round-trips through the same join.
        {
            using World world = World.Create("tierPollJoin", GrittyEventJson.Parse(onboardingJson));
            var baseball = new BaseballQueries(world.Db);
            baseball.InsertTeam(new TeamRow { TeamId = 301, City = "Testville", Name = "HS Team", Abbreviation = "TVH", League = "HS", Division = "A" });
            baseball.UpsertTeamTier(301, LeagueTier.HS);
            baseball.InsertTeam(new TeamRow { TeamId = 302, City = "Testburg", Name = "Pro Team", Abbreviation = "TVP", League = "MLB", Division = "A" });
            baseball.UpsertTeamTier(302, LeagueTier.MLB);
            baseball.InsertTeam(new TeamRow { TeamId = 303, City = "Orphantown", Name = "No Tier Row", Abbreviation = "ORP", League = "MLB", Division = "A" });
            // Deliberately no UpsertTeamTier(303, ...) -- proves the missing-row COALESCE(tt.tier, 5) branch.

            world.AddPlayer("hsPlayer", age: 16, teamId: 301);
            world.AddPlayer("proPlayer", age: 25, teamId: 302);
            world.AddPlayer("untieredTeamPlayer", age: 25, teamId: 303);
            world.AddPlayer("parent", age: 44, teamId: null);

            PersonRow customGpa = PersonRow.Neutral("hsPlayer");
            customGpa.Gpa = 3.8;
            world.Persons.Upsert(customGpa);

            var poll = new NarrativePollQueries(world.View);
            var rows = new List<PollPlayerRow>();
            poll.LoadPollPlayers(rows);

            PollPlayerRow hsRow = rows.Single(r => r.PlayerId == "hsPlayer");
            PollPlayerRow proRow = rows.Single(r => r.PlayerId == "proPlayer");
            PollPlayerRow untieredRow = rows.Single(r => r.PlayerId == "untieredTeamPlayer");
            PollPlayerRow parentRow = rows.Single(r => r.PlayerId == "parent");

            Check("a rostered HS-tier subject polls tier 0 through the real Team_Tiers join",
                hsRow.Tier == 0);
            Check("a rostered MLB-tier subject polls tier 5",
                proRow.Tier == 5);
            Check("a rostered team missing its Team_Tiers row still COALESCEs to MLB(5) (the v6->v7 backfill convention, unbroken by the new join)",
                untieredRow.Tier == 5);
            Check("an UNROSTERED subject (NULL team_id, the seeded-parent shape) polls the -1 sentinel end to end through the real SQL, never MLB(5)",
                parentRow.TeamId == null && parentRow.Tier == -1);
            Check("gpa round-trips through the real Player_Person join",
                Math.Abs(hsRow.Gpa - 3.8) < 1e-9);
        }
    }

    // ------------------------------------------------------------------
    // 4i. ChildDevelopment consequence + incremental writer (HS-5, §7.1)
    // ------------------------------------------------------------------

    private static void RunChildDevelopmentChecks()
    {
        Console.WriteLine("--- Child_Development writer + consequence (§7.1) ---");

        Check("loader accepts child_development on a scope-avatar event", !Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "child_development", "axis": "care", "amount": 5 } ] } ] } ] }"""));
        Check("loader rejects child_development on a scope-any event (only the avatar rears through events)", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "child_development", "axis": "care", "amount": 5 } ] } ] } ] }"""));
        Check("loader rejects an unknown child_development axis", Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "child_development", "axis": "tutoring", "amount": 5 } ] } ] } ] }"""));
        Check("loader rejects child_development without an amount", Throws(
            """{ "events": [ { "id": "x", "scope": "avatar", "weight": 0.5, "choices": [ { "id": "a", "consequences": [ { "type": "child_development", "axis": "care" } ] } ] } ] }"""));

        // The mirrored ordinal contract: each ChildAxis ordinal drives exactly
        // its own column, a missing row seeds from that axis's schema default
        // (50/50/50/0) plus the delta, and the SQL clamps at both rails.
        {
            using World world = World.Create("childWriter", GrittyEventJson.Parse(
                """{ "events": [ { "id": "unused", "scope": "any", "weight": 0.0, "choices": [ { "id": "a" } ] } ] }"""));
            world.AddPlayer("kid", age: 1, teamId: null);

            world.Persons.AdjustChildAxis("kid", (int)ChildAxis.Care, 7, day: 5);
            bool seeded = world.Persons.TryGetChild("kid", out ChildDevelopmentRow row);
            Check("a first axis write seeds the row from the schema defaults plus the delta",
                seeded && row.Care == 57 && row.Coaching == 50 && row.Funding == 50 && row.Neglect == 0 && row.LastTickDay == 5,
                $"care={row.Care} coaching={row.Coaching} funding={row.Funding} neglect={row.Neglect} day={row.LastTickDay}");

            world.Persons.AdjustChildAxis("kid", (int)ChildAxis.Coaching, -60, day: 6);
            world.Persons.AdjustChildAxis("kid", (int)ChildAxis.Funding, 100, day: 7);
            world.Persons.AdjustChildAxis("kid", (int)ChildAxis.Neglect, 5, day: 8);
            world.Persons.TryGetChild("kid", out row);
            Check("each ChildAxis ordinal drives exactly its own column, clamped in SQL at both rails",
                row.Care == 57 && row.Coaching == 0 && row.Funding == 100 && row.Neglect == 5 && row.LastTickDay == 8,
                $"care={row.Care} coaching={row.Coaching} funding={row.Funding} neglect={row.Neglect} day={row.LastTickDay}");

            Check("an out-of-range axis ordinal throws loudly", ThrowsAny(() =>
                world.Persons.AdjustChildAxis("kid", 4, 1, day: 9)));
        }

        // End to end: the consequence reaches every CHILD of the subject (the
        // younger Child-edge endpoint, resolved from the DATABASE — the same
        // rows ConceiveChild's batch writes) and never the subject's own
        // parent, whose Child edge points the other way (§1.2).
        {
            using World world = World.Create("childApply", GrittyEventJson.Parse(
                """
                { "events": [ { "id": "backyard_catch_hs5", "scope": "avatar", "weight": 1.0,
                  "choices": [ { "id": "play", "consequences": [
                    { "type": "child_development", "axis": "coaching", "amount": 6 },
                    { "type": "child_development", "axis": "care", "amount": 2 } ] } ] } ] }
                """));
            world.AddPlayer("hero", age: 30, teamId: 1);
            world.AddPlayer("kid_a", age: 3, teamId: null);
            world.AddPlayer("kid_b", age: 1, teamId: null);
            world.AddPlayer("grandma", age: 61, teamId: null);
            world.Players.UpsertRelationship("hero", "kid_a", 30, RelationshipType.Child);
            world.Players.UpsertRelationship("hero", "kid_b", 30, RelationshipType.Child);
            world.Players.UpsertRelationship("hero", "grandma", 40, RelationshipType.Child);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            bool aWritten = world.Persons.TryGetChild("kid_a", out ChildDevelopmentRow rowA);
            bool bWritten = world.Persons.TryGetChild("kid_b", out ChildDevelopmentRow rowB);
            bool grandmaClean = !world.Persons.TryGetChild("grandma", out _);
            bool heroClean = !world.Persons.TryGetChild("hero", out _);
            Check("a rearing fire feeds every child's axes (both kids, DB-resolved) and skips the older Child-edge endpoint",
                world.Fired.Count == 1
                && aWritten && rowA.Coaching == 56 && rowA.Care == 52 && rowA.LastTickDay == 2
                && bWritten && rowB.Coaching == 56 && rowB.Care == 52
                && grandmaClean && heroClean,
                $"fires={world.Fired.Count} a=({(aWritten ? $"{rowA.Coaching}/{rowA.Care}" : "none")}) b=({(bWritten ? $"{rowB.Coaching}/{rowB.Care}" : "none")}) grandmaClean={grandmaClean}");
        }

        // A childless subject is a clean skip — fires, writes no row.
        {
            using World world = World.Create("childless", GrittyEventJson.Parse(
                """{ "events": [ { "id": "no_kids_yet", "scope": "avatar", "weight": 1.0, "choices": [ { "id": "a", "consequences": [ { "type": "child_development", "axis": "care", "amount": 5 } ] } ] } ] }"""));
            world.AddPlayer("hero", age: 30, teamId: 1);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            world.Dispatcher.PollOnce();
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();
            Check("a childless subject's child_development fire is a clean no-op",
                world.Fired.Count == 1 && !world.Persons.TryGetChild("hero", out _),
                $"{world.Fired.Count} fires");
        }
    }

    // ------------------------------------------------------------------
    // 4j. NPC autonomy tick (HS-5, person-layer doc §8 conservation)
    // ------------------------------------------------------------------

    private static List<SocialSeed> BuildAutonomyPopulation(int count, int teamSize)
    {
        var population = new List<SocialSeed>(count);
        for (int i = 0; i < count; i++)
        {
            // First 2/3 rostered in teamSize blocks, the rest unrostered —
            // stats spread deterministically so compatibility varies.
            int? teamId = i < count * 2 / 3 ? 100 + i / teamSize : null;
            population.Add(new SocialSeed($"npc{i:D3}", teamId, (i * 7) % 101, (i * 13) % 101));
        }
        return population;
    }

    private static void RunNpcAutonomyChecks()
    {
        Console.WriteLine("--- NPC autonomy tick (§8) ---");

        // Cadence: the gate lives inside ProcessDay (and is exposed static
        // for the caller's projection skip).
        {
            var graph = new RelationshipGraph();
            var service = new NpcAutonomyService(graph, new RngState(11UL).Split());
            List<SocialSeed> population = BuildAutonomyPopulation(60, 17);
            service.ProcessDay(3, null, population);
            bool offDay = service.LastTickInteractions == 0 && graph.EdgeCount == 0;
            service.ProcessDay(7, null, population);
            Check("weekly cadence: day 3 is a no-op, day 7 ticks (IsTickDay agrees)",
                offDay && !NpcAutonomyService.IsTickDay(3) && NpcAutonomyService.IsTickDay(7)
                && service.LastTickInteractions > 0,
                $"offDay={offDay}, tickInteractions={service.LastTickInteractions}");
        }

        // §8.1 hard budget: interactions never exceed the cap, at any
        // population size — and with all three tiers populated the tick
        // spends exactly the cap.
        {
            var graph = new RelationshipGraph();
            graph.SetRelationship("npc000", "npc001", 20, RelationshipKind.Friend);
            var service = new NpcAutonomyService(graph, new RngState(12UL).Split());
            List<SocialSeed> big = BuildAutonomyPopulation(800, 17);
            service.RunWeek(null, big);
            int first = service.LastTickInteractions;
            bool capped = first == NpcAutonomyService.NpcAutonomyProfile.MaxPairInteractionsPerWeek;
            List<SocialSeed> tiny = BuildAutonomyPopulation(3, 17);
            var tinyGraph = new RelationshipGraph();
            var tinyService = new NpcAutonomyService(tinyGraph, new RngState(13UL).Split());
            tinyService.RunWeek(null, tiny);
            Check("the 256-pair budget is exact with all tiers live and never exceeded at any population",
                capped && tinyService.LastTickInteractions <= NpcAutonomyService.NpcAutonomyProfile.MaxPairInteractionsPerWeek,
                $"big={first}, tiny={tinyService.LastTickInteractions}");
        }

        // §8.2 conservation: the avatar's edges are untouched across many
        // weeks — no nudge, no reclassify, no new edge — and Child edges are
        // lineage state everywhere.
        {
            var graph = new RelationshipGraph();
            graph.SetRelationship("avatar", "npc001", 55, RelationshipKind.Partner);
            graph.SetRelationship("avatar", "npc002", 30, RelationshipKind.Friend);
            graph.SetRelationship("avatar", "npc003", -40, RelationshipKind.Rival);
            graph.SetRelationship("npc004", "npc005", 30, RelationshipKind.Child);
            var service = new NpcAutonomyService(graph, new RngState(14UL).Split());
            List<SocialSeed> population = BuildAutonomyPopulation(120, 17);
            population.Add(new SocialSeed("avatar", 100, 90, 90));
            for (int week = 0; week < 30; week++)
            {
                service.RunWeek("avatar", population);
            }
            var avatarEdges = new List<RelationshipEdge>();
            graph.GetEdgesFor("avatar", avatarEdges);
            bool partnerKept = avatarEdges.Any(e => e.OtherId == "npc001" && e.Kind == RelationshipKind.Partner && e.Affinity == 55);
            bool friendKept = avatarEdges.Any(e => e.OtherId == "npc002" && e.Kind == RelationshipKind.Friend && e.Affinity == 30);
            bool rivalKept = avatarEdges.Any(e => e.OtherId == "npc003" && e.Kind == RelationshipKind.Rival && e.Affinity == -40);
            bool childKept = graph.TryGetRelationship("npc004", "npc005", out int childAffinity, out RelationshipKind childKind)
                && childKind == RelationshipKind.Child && childAffinity == 30;
            Check("30 weeks never touch the avatar's edges (§8.2) — kind, affinity, and count all preserved",
                avatarEdges.Count == 3 && partnerKept && friendKept && rivalKept,
                $"{avatarEdges.Count} avatar edges, partner={partnerKept} friend={friendKept} rival={rivalKept}");
            Check("30 weeks never touch a Child edge (lineage state)",
                childKept, $"kind={childKind}, affinity={childAffinity}");
        }

        // RNG-split proof: the service owns its fork BY VALUE — running it
        // leaves the caller's stream exactly where Split() (which never
        // advances the parent) left it.
        {
            var parent = new RngState(99UL);
            RngState control = parent; // struct copy — the never-forked twin
            var graph = new RelationshipGraph();
            var service = new NpcAutonomyService(graph, parent.Split());
            List<SocialSeed> population = BuildAutonomyPopulation(200, 17);
            service.RunWeek(null, population);
            Check("RNG-split isolation: the caller's stream is bit-identical after fork + a full tick",
                parent.NextUInt64() == control.NextUInt64() && graph.EdgeCount > 0,
                $"edges={graph.EdgeCount}");
        }

        // Determinism: identical fork seed + population + starting graph ⇒
        // identical edge sets, week after week.
        {
            static List<RelationshipSeed> RunFiveWeeks()
            {
                var graph = new RelationshipGraph();
                graph.SetRelationship("npc010", "npc011", 40, RelationshipKind.Friend);
                var service = new NpcAutonomyService(graph, new RngState(4242UL).Split());
                List<SocialSeed> population = BuildAutonomyPopulation(150, 17);
                for (int week = 0; week < 5; week++)
                {
                    service.RunWeek(null, population);
                }
                var edges = new List<RelationshipSeed>();
                graph.CollectEdges(edges);
                edges.Sort((a, b) =>
                {
                    int first = string.CompareOrdinal(a.PlayerAId, b.PlayerAId);
                    return first != 0 ? first : string.CompareOrdinal(a.PlayerBId, b.PlayerBId);
                });
                return edges;
            }
            List<RelationshipSeed> runA = RunFiveWeeks();
            List<RelationshipSeed> runB = RunFiveWeeks();
            bool identical = runA.Count == runB.Count;
            for (int i = 0; identical && i < runA.Count; i++)
            {
                identical = runA[i] == runB[i];
            }
            Check("same fork seed + same inputs ⇒ bit-identical edge evolution (5 weeks)",
                identical && runA.Count > 0, $"{runA.Count} vs {runB.Count} edges");
        }

        // The tick actually LIVES: over a season's worth of weeks it mints
        // friendships and rivalries, promotes at least one NPC romance
        // (single-partner exclusivity never violated), and eventually breaks
        // one up — the organic exes §8 exists to create.
        {
            var graph = new RelationshipGraph();
            var service = new NpcAutonomyService(graph, new RngState(20260709UL).Split());
            List<SocialSeed> population = BuildAutonomyPopulation(200, 17);
            var everPartnered = new HashSet<(string, string)>();
            var edges = new List<RelationshipSeed>();
            int weeks = 0;
            for (; weeks < 120; weeks++)
            {
                service.RunWeek(null, population);
                graph.CollectEdges(edges);
                foreach (RelationshipSeed edge in edges)
                {
                    if (edge.Kind == RelationshipKind.Partner)
                    {
                        everPartnered.Add((edge.PlayerAId, edge.PlayerBId));
                    }
                }
            }
            graph.CollectEdges(edges);
            int friends = edges.Count(e => e.Kind == RelationshipKind.Friend);
            int rivals = edges.Count(e => e.Kind == RelationshipKind.Rival);
            int exes = everPartnered.Count(pair =>
                graph.TryGetRelationship(pair.Item1, pair.Item2, out _, out RelationshipKind kind)
                && kind is RelationshipKind.Friend or RelationshipKind.Rival);
            // Exclusivity: nobody ever holds two live Partner edges.
            var partnerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RelationshipSeed edge in edges)
            {
                if (edge.Kind != RelationshipKind.Partner)
                {
                    continue;
                }
                partnerCounts[edge.PlayerAId] = partnerCounts.GetValueOrDefault(edge.PlayerAId) + 1;
                partnerCounts[edge.PlayerBId] = partnerCounts.GetValueOrDefault(edge.PlayerBId) + 1;
            }
            bool exclusive = partnerCounts.Values.All(count => count == 1);
            Check("a season of ticks mints friendships AND rivalries (both signs of the compatibility roll)",
                friends > 0 && rivals > 0, $"{friends} friends, {rivals} rivals, {edges.Count} edges");
            Check("NPC romances form organically and single-partner exclusivity holds throughout",
                everPartnered.Count > 0 && exclusive,
                $"{everPartnered.Count} ever-partnered pairs, exclusive={exclusive}");
            Check("at least one NPC partnership has ended into a surviving Friend/Rival edge — organic exes exist (§8)",
                exes > 0, $"{exes} exes of {everPartnered.Count} ever-partnered");
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

        // Schema v6 persistence: an impulse-raised scalar round-trips through
        // Life_Stress and hydrates back over a fresh manager's 0 default —
        // the exact NeedsQueries/SetStress bridge GameManager runs at boot
        // and on every day-tick flush.
        {
            string path = Path.Combine(Path.GetTempPath(), $"dnd_gritty_stresspersist_{Guid.NewGuid():N}.db");
            ScratchFiles.Add(path);
            using var db = new DatabaseManager(path);
            db.InitializeSchema(_schemaPath);
            var players = new PlayerQueries(db);
            players.Insert(new PlayerRow
            {
                PlayerId = "npc", FirstName = "Test", LastName = "npc", Age = 27,
                TeamId = 1, Funds = 1_000, HealthCeiling = 100,
            });
            var needsQueries = new NeedsQueries(db);

            var bus = new EventBus();
            var lifeSim = new LifeSimManager();
            lifeSim.Seed(new[] { new NpcSeed("npc", 1_000) });
            lifeSim.AttachTo(bus);
            bus.Publish(new StressImpulseEvent("npc", 42.5f));
            bus.DispatchPending();
            lifeSim.TryGetStress("npc", out float live);
            needsQueries.BulkUpsertStress(new[] { new StressRow { PlayerId = "npc", Stress = live } });

            var rebooted = new LifeSimManager();
            rebooted.Seed(new[] { new NpcSeed("npc", 1_000) });
            var persisted = new Dictionary<string, float>();
            needsQueries.LoadAllStress(persisted);
            foreach (KeyValuePair<string, float> entry in persisted)
            {
                rebooted.SetStress(entry.Key, entry.Value);
            }
            rebooted.TryGetStress("npc", out float hydrated);
            Check("schema v6: stress round-trips Life_Stress and hydrates over a reboot's 0 default",
                Math.Abs(live - 42.5f) < 1e-3f && persisted.Count == 1 && hydrated == live,
                $"live={live:F2}, hydrated={hydrated:F2}");

            // The next flush overwrites (upsert, not insert-once), and the
            // 0–100 CHECK holds the same range contract the scalar clamps to.
            needsQueries.UpsertStress(new StressRow { PlayerId = "npc", Stress = 7.25f });
            needsQueries.LoadAllStress(persisted);
            bool outOfRangeRejected = false;
            try
            {
                needsQueries.UpsertStress(new StressRow { PlayerId = "npc", Stress = 150f });
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                outOfRangeRejected = true;
            }
            Check("schema v6: stress upsert overwrites; CHECK rejects out-of-range",
                Math.Abs(persisted["npc"] - 7.25f) < 1e-6f && outOfRangeRejected,
                $"after overwrite {persisted["npc"]:F2}");
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
    // 5b. Hustles Layer-2/DB integration (hustles_narcotics_fencing.md §9
    // check 9) — HustleService and the pure Hustles resolvers are Data-free
    // themselves, so this runs in "whichever harness compiles Data" rather
    // than a redundant third project just for one check.
    // ------------------------------------------------------------------

    private static void RunHustleIntegrationChecks()
    {
        Console.WriteLine("--- Hustles Layer-2 integration ---");

        string path = Path.Combine(Path.GetTempPath(), $"dnd_gritty_hustle_{Guid.NewGuid():N}.db");
        ScratchFiles.Add(path);
        using var db = new DatabaseManager(path);
        db.InitializeSchema(_schemaPath);

        var gameState = new GameStateQueries(db);
        var players = new PlayerQueries(db);
        var bus = new EventBus();
        var graph = new RelationshipGraph();
        graph.AttachTo(bus);

        players.Insert(new PlayerRow
        {
            PlayerId = "avatar", FirstName = "Test", LastName = "Avatar", Age = 22, TeamId = null,
            Funds = 500, HealthCeiling = 15, Recklessness = 10, BaseballInterest = 100, DetectionRisk = 95,
        });
        for (int i = 0; i < 10; i++)
        {
            players.Insert(new PlayerRow
            {
                PlayerId = $"npc{i}", FirstName = "Test", LastName = $"Npc{i}", Age = 25, TeamId = null,
                Funds = 100, HealthCeiling = 100, Recklessness = 0, BaseballInterest = 0, DetectionRisk = 0,
            });
        }

        var service = new HustleService(db, players, gameState, graph, bus, rngSeed: 555UL);

        var rivalryEvents = new List<RivalryChangedEvent>();
        bus.Subscribe<RivalryChangedEvent>(rivalryEvents.Add);

        service.BuildNarcoticsContext("avatar", day: 10);
        bool supplierResolved = gameState.TryGetText(GameStateKeys.HustleSupplierPlayerId, out string supplierId);
        bool crewResolved = gameState.TryGetText(GameStateKeys.HustleCrewPlayerId, out string crewId);
        Check("hustle integration: BuildNarcoticsContext resolves and caches supplier/crew reps",
            supplierResolved && crewResolved && supplierId != "avatar" && crewId != "avatar" && supplierId != crewId,
            $"supplier={supplierId} crew={crewId}");

        var flags = new List<EntityFlagRow>();
        players.LoadActiveFlags(supplierId, flags);
        bool supplierTagged = flags.Exists(f => f.FlagName == "faction_supplier" && f.IsActive);
        players.LoadActiveFlags(crewId, flags);
        bool crewTagged = flags.Exists(f => f.FlagName == "faction_crew_1" && f.IsActive);
        Check("hustle integration: faction reps are narratively tagged via Entity_Flags", supplierTagged && crewTagged);

        // A resolution that exercises every writer at once, with clamping
        // deliberately in play (detection_risk 95+20 must clamp to 100;
        // health_ceiling 15-30 must floor at 0) and a crew delta deep enough
        // to push a fresh edge into Rival territory, publishing RivalryChangedEvent.
        var resolution = new HustleResolution(
            fundsDelta: 250, detectionRiskDelta: 20, healthCeilingDelta: -30, recklessnessDelta: 5, stressDelta: 12,
            supplierTrustDelta: 10, crewStandingDelta: -45,
            setWatchlistFlag: true, setBadProductFlag: true, setSpoiledGoodsFlag: false, setControlsTurfFlag: false);
        service.ApplyNarcoticsResolution("avatar", in resolution, day: 10);
        bus.DispatchPending();

        players.TryGetById("avatar", out PlayerRow after);
        Check("hustle integration: funds/detection/health/recklessness move by the clamped deltas",
            after.Funds == 750 && after.DetectionRisk == 100 && after.HealthCeiling == 0 && after.Recklessness == 15,
            $"funds={after.Funds} detect={after.DetectionRisk} health={after.HealthCeiling} reck={after.Recklessness}");

        players.LoadActiveFlags("avatar", flags);
        bool watchlistSet = flags.Exists(f => f.FlagName == "narc_watchlist" && f.IsActive && f.SetOnDay == 10);
        bool badProductSet = flags.Exists(f => f.FlagName == "bad_product" && f.IsActive && f.SetOnDay == 10);
        Check("hustle integration: flags are set with the resolution's day", watchlistSet && badProductSet);

        graph.TryGetRelationship("avatar", crewId, out int crewAffinity, out RelationshipKind crewKind);
        Check("hustle integration: crew edge lands a deep Rival (<= -40) and publishes RivalryChangedEvent",
            crewKind == RelationshipKind.Rival && crewAffinity <= -40 && rivalryEvents.Count > 0,
            $"affinity={crewAffinity} kind={crewKind} events={rivalryEvents.Count}");

        graph.TryGetRelationship("avatar", supplierId, out int supplierAffinity, out RelationshipKind supplierKind);
        Check("hustle integration: supplier edge created as Friend with the trust delta",
            supplierKind == RelationshipKind.Friend && supplierAffinity == 10,
            $"affinity={supplierAffinity} kind={supplierKind}");
    }

    // ------------------------------------------------------------------
    // 5c. Phase 8e equipment Layer-2/DB integration (equipment_quality.md §8
    // check 6) — EquipmentService is the same Data + Core orchestration class
    // as HustleService, so its integration checks live here too. The sim-side
    // consumption of the ledger is MonteCarloHarness's job; this proves the
    // purchase path: validation, the one batch, and the post-commit events.
    // ------------------------------------------------------------------

    private static void RunEquipmentIntegrationChecks()
    {
        Console.WriteLine("--- Equipment Layer-2 integration (Phase 8e) ---");

        string path = Path.Combine(Path.GetTempPath(), $"dnd_gritty_equip_{Guid.NewGuid():N}.db");
        ScratchFiles.Add(path);
        using var db = new DatabaseManager(path);
        db.InitializeSchema(_schemaPath);

        var players = new PlayerQueries(db);
        var bus = new EventBus();
        players.Insert(new PlayerRow
        {
            PlayerId = "buyer", FirstName = "Test", LastName = "Buyer", Age = 22, TeamId = null,
            Funds = 10_000, HealthCeiling = 100, Recklessness = 0, BaseballInterest = 100, DetectionRisk = 0,
        });
        players.Insert(new PlayerRow
        {
            PlayerId = "pauper", FirstName = "Test", LastName = "Pauper", Age = 22, TeamId = null,
            Funds = 100, HealthCeiling = 100, Recklessness = 0, BaseballInterest = 0, DetectionRisk = 0,
        });

        // The live transport: the same bus feeds the sims' ledger and the Life
        // sim's funds mirror, so both event streams are observed here.
        var ledger = new EquipmentLedger();
        ledger.AttachTo(bus);
        var fundsEvents = new List<FundsImpulseEvent>();
        bus.Subscribe<FundsImpulseEvent>(fundsEvents.Add);

        var shop = new EquipmentService(db, players, bus);

        EquipmentShopState fresh = shop.GetShopState("buyer");
        Check("equipment integration: fresh shop snapshot reads funds + standard-issue tier",
            fresh.Funds == 10_000 && fresh.OwnedQuality == 0);

        bool bought1 = shop.TryPurchase("buyer", 1, day: 12, out EquipmentPurchaseFailure firstFailure);
        bus.DispatchPending();
        players.TryGetById("buyer", out PlayerRow afterFirst);
        bool rowExact = players.TryGetEquipment("buyer", out PlayerEquipmentRow firstRow)
            && firstRow.Quality == 1 && firstRow.PurchasedDay == 12;
        Check("equipment integration: q1 purchase — funds down exactly $750, row exact, ledger + funds mirror events observed",
            bought1 && firstFailure == EquipmentPurchaseFailure.None && afterFirst.Funds == 9_250 && rowExact
            && ledger.QualityFor("buyer") == 1
            && fundsEvents.Count == 1 && fundsEvents[0].PlayerId == "buyer" && fundsEvents[0].Delta == -750,
            $"funds={afterFirst.Funds} ledger={ledger.QualityFor("buyer")} impulses={fundsEvents.Count}");

        bool boughtSame = shop.TryPurchase("buyer", 1, 13, out EquipmentPurchaseFailure sameFailure);
        bool boughtBroke = shop.TryPurchase("pauper", 1, 13, out EquipmentPurchaseFailure brokeFailure);
        bool boughtInvalid = shop.TryPurchase("buyer", 4, 13, out EquipmentPurchaseFailure invalidFailure);
        bool boughtGhost = shop.TryPurchase("ghost", 1, 13, out EquipmentPurchaseFailure ghostFailure);
        bus.DispatchPending();
        players.TryGetById("buyer", out PlayerRow afterRejects);
        players.TryGetById("pauper", out PlayerRow pauperAfter);
        Check("equipment integration: rejections are typed clean no-ops (re-buy / broke / invalid quality / unknown player)",
            !boughtSame && sameFailure == EquipmentPurchaseFailure.NotAnUpgrade
            && !boughtBroke && brokeFailure == EquipmentPurchaseFailure.InsufficientFunds
            && !boughtInvalid && invalidFailure == EquipmentPurchaseFailure.InvalidQuality
            && !boughtGhost && ghostFailure == EquipmentPurchaseFailure.UnknownPlayer
            && afterRejects.Funds == 9_250 && pauperAfter.Funds == 100
            && ledger.QualityFor("buyer") == 1 && ledger.QualityFor("pauper") == 0
            && fundsEvents.Count == 1);

        // Skipping a rung is legal — upgrade-only means strictly higher, not
        // adjacent. Full sticker, no trade-in credit.
        bool bought3 = shop.TryPurchase("buyer", 3, day: 40, out EquipmentPurchaseFailure ladderFailure);
        bus.DispatchPending();
        players.TryGetById("buyer", out PlayerRow afterLadder);
        EquipmentShopState finalState = shop.GetShopState("buyer");
        Check("equipment integration: 1→3 rung-skip at full sticker — funds −$7,500, row/ledger/snapshot all read q3",
            bought3 && ladderFailure == EquipmentPurchaseFailure.None && afterLadder.Funds == 1_750
            && players.TryGetEquipment("buyer", out PlayerEquipmentRow ladderRow)
            && ladderRow.Quality == 3 && ladderRow.PurchasedDay == 40
            && ledger.QualityFor("buyer") == 3
            && finalState.Funds == 1_750 && finalState.OwnedQuality == 3
            && fundsEvents.Count == 2 && fundsEvents[1].Delta == -7_500,
            $"funds={afterLadder.Funds} ledger={ledger.QualityFor("buyer")}");

        Check("equipment integration: no open batch, integrity ok",
            !db.IsBatchActive && db.RunIntegrityCheck() == "ok" && db.RunForeignKeyCheck() == 0);
    }

    // ------------------------------------------------------------------
    // 5b. Phase 8c absence consequence (roster availability). The ledger
    // itself lives in the Baseball assembly — MonteCarloHarness proves it;
    // here the contract is the Narrative half: the Player_Absences row and
    // the post-commit PlayerAbsenceChangedEvent transport.
    // ------------------------------------------------------------------

    private static void RunAbsenceChecks()
    {
        Console.WriteLine("--- Phase 8c absence consequence ---");

        GrittyEventLibrary parsed = GrittyEventJson.Parse(
            """
            { "events": [ { "id": "triad", "scope": "any", "weight": 1.0, "choices": [ { "id": "a", "consequences": [
                { "type": "absence", "reason": "injury", "days": 10 },
                { "type": "absence", "reason": "suspension", "days": 5 },
                { "type": "absence", "reason": "arrest", "days": 30 } ] } ] } ] }
            """);
        parsed.TryGetById("triad", out GrittyEventDefinition triad);
        EventConsequence[] parsedConsequences = triad.Choices[0].Consequences;
        Check("absence consequence parses all three reasons with their day counts",
            parsedConsequences.Length == 3
            && parsedConsequences[0].Kind == ConsequenceKind.Absence
            && parsedConsequences[0].AbsenceReason == AbsenceReason.Injury && (int)parsedConsequences[0].Amount == 10
            && parsedConsequences[1].AbsenceReason == AbsenceReason.Suspension && (int)parsedConsequences[1].Amount == 5
            && parsedConsequences[2].AbsenceReason == AbsenceReason.Arrest && (int)parsedConsequences[2].Amount == 30);

        Check("loader rejects an unknown absence reason", Throws(
            """{ "events": [ { "id": "x", "scope": "any", "weight": 1.0, "choices": [ { "id": "a", "consequences": [ { "type": "absence", "reason": "vacation", "days": 3 } ] } ] } ] }"""));
        Check("loader rejects zero and fractional absence days",
            Throws("""{ "events": [ { "id": "x", "scope": "any", "weight": 1.0, "choices": [ { "id": "a", "consequences": [ { "type": "absence", "reason": "arrest", "days": 0 } ] } ] } ] }""")
            && Throws("""{ "events": [ { "id": "x", "scope": "any", "weight": 1.0, "choices": [ { "id": "a", "consequences": [ { "type": "absence", "reason": "injury", "days": 2.5 } ] } ] } ] }"""));

        // End-to-end: a PED-test suspension keyed on detection_risk (the 8b §8
        // contract's reader) and an injury priced off live health_ceiling.
        using World world = World.Create("absence", GrittyEventJson.Parse(
            """
            { "events": [
              { "id": "ped_test", "scope": "any", "weight": 1.0,
                "prerequisites": [ { "field": "detection_risk", "op": ">=", "value": 80 } ],
                "choices": [ { "id": "suspended", "consequences": [
                  { "type": "absence", "reason": "suspension", "days": 5 },
                  { "type": "detection_risk", "amount": -40 },
                  { "type": "set_flag", "flag": "served_suspension" } ] } ] },
              { "id": "breakdown", "scope": "any", "weight": 1.0,
                "prerequisites": [ { "field": "health_ceiling", "op": "<", "value": 50 } ],
                "choices": [ { "id": "hurt", "consequences": [
                  { "type": "absence", "reason": "injury", "days": 10 } ] } ] }
            ] }
            """));
        world.AddPlayer("doper", age: 27, teamId: 1);
        world.AddPlayer("brittle", age: 27, teamId: 1);
        world.Players.AdjustDetectionRisk("doper", 85);
        world.Players.AdjustHealthCeiling("brittle", -60); // 100 → 40
        var published = new List<PlayerAbsenceChangedEvent>();
        world.Bus.Subscribe<PlayerAbsenceChangedEvent>(published.Add);
        world.Dispatcher.PollOnce();

        world.Clock.AdvanceDay(); // day 2 — both prerequisites hold, both fire
        world.Bus.DispatchPending();
        world.Dispatcher.PollOnce();
        world.Bus.DispatchPending();

        bool suspensionRow = world.Players.TryGetAbsence("doper", out PlayerAbsenceRow suspension);
        bool injuryRow = world.Players.TryGetAbsence("brittle", out PlayerAbsenceRow injury);
        world.Players.TryGetById("doper", out PlayerRow doper);
        Check("suspension row: until_day = fire day + days + 1 (misses exactly N game days), no rust",
            suspensionRow && suspension.Reason == AbsenceReason.Suspension && suspension.UntilDay == 8
            && suspension.RatingPenalty == 0 && suspension.PenaltyUntilDay == 0,
            $"until {suspension.UntilDay} penalty {suspension.RatingPenalty}");
        Check("same choice: detection_risk cooled by serving the suspension (85 → 45)",
            doper.DetectionRisk == 45, $"{doper.DetectionRisk}");
        Check("injury row: rust priced off live health (40 → penalty 7), rust window as long as the absence",
            injuryRow && injury.Reason == AbsenceReason.Injury && injury.UntilDay == 13
            && injury.RatingPenalty == 7 && injury.PenaltyUntilDay == 23,
            $"until {injury.UntilDay} penalty {injury.RatingPenalty} rustUntil {injury.PenaltyUntilDay}");
        Check("PlayerAbsenceChangedEvent published post-commit for each absence (ledger transport)",
            published.Count == 2
            && published.Exists(p => p.PlayerId == "doper" && p.Reason == (byte)AbsenceReason.Suspension && p.UntilDay == 8)
            && published.Exists(p => p.PlayerId == "brittle" && p.Reason == (byte)AbsenceReason.Injury
                && p.RatingPenalty == 7 && p.PenaltyUntilDay == 23),
            $"{published.Count} publications");

        var flags = new List<EntityFlagRow>();
        world.Players.LoadActiveFlags("doper", flags);
        Check("absence composes with flags in one choice (served_suspension set on the fire day)",
            flags.Exists(f => f.FlagName == "served_suspension" && f.IsActive && f.SetOnDay == 2));
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
        public PersonQueries Persons = null!;
        public GlobalState State = null!;
        public TimeManager Clock = null!;
        public RelationshipGraph Graph = null!;
        public LifeSimManager LifeSim = null!;
        public EventDispatcher Dispatcher = null!;
        public EventConsequenceApplier Applier = null!;
        public NarrativeLogQueries NarrativeLog = null!;
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
            world.Persons = new PersonQueries(world.Db);
            world.State = new GlobalState();
            world.Clock = new TimeManager(world.Db, world.GameState, world.State, world.Bus);
            world.Clock.Initialize(2026);
            world.Graph = new RelationshipGraph();
            world.Graph.AttachTo(world.Bus);
            world.LifeSim = new LifeSimManager();
            world.NarrativeLog = new NarrativeLogQueries(world.Db);

            world.Applier = new EventConsequenceApplier(
                world.Db, world.Players, world.Persons, library, world.Graph, world.Bus, world.GameState,
                world.State, world.NarrativeLog, rngSeed: 4242UL);
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
            // Real gameplay never has a Players row without a Player_Person
            // row (the v11 boot backfill invariant) — the harness's synthetic
            // players honor the same contract so a PersonStat consequence has
            // something to adjust.
            Persons.Upsert(PersonRow.Neutral(id));
        }

        public void Dispose()
        {
            Dispatcher.Dispose();
            View.Dispose();
            Db.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Item catalog (HS-3, high_school_person_layer.md §5)
    // ------------------------------------------------------------------

    private static void RunItemCatalogChecks()
    {
        Console.WriteLine("--- Item catalog (§5) ---");

        // The shipped file parses; every ItemCategory value is represented so
        // each category's parse branch is exercised by real content.
        string shipped = File.ReadAllText(Path.Combine(_repoRoot, "Assets", "Data", "Items", "items.json"));
        ItemCatalog catalog = ItemCatalogJson.Parse(shipped);
        var seen = new bool[6];
        foreach (ItemDefinition entry in catalog.Entries)
        {
            seen[(int)entry.Category] = true;
        }
        Check("shipped items.json loads with every category represented",
            catalog.Count >= 12 && seen[1] && seen[2] && seen[3] && seen[4] && seen[5],
            $"{catalog.Count} items");

        // The HS-2 creation contract: BackstoryGenerator.TransportGiftByTier's
        // three gift ids (tiers 2/3/4) must exist as Transport entries whose
        // autobuy floors mirror the gifting tiers. Ids are pinned as strings
        // here because this harness deliberately does not compile the Baseball
        // assembly; MonteCarloHarness holds the mirror check from the other
        // side (BackstoryProfile → the json file).
        bool hasBike = catalog.TryGet("commuter_bike", out ItemDefinition bike);
        bool hasSedan = catalog.TryGet("used_sedan", out ItemDefinition sedan);
        bool hasCoupe = catalog.TryGet("new_coupe", out ItemDefinition coupe);
        Check("HS-2 transport-gift contract ids present with the creation ladder's autobuy tiers",
            hasBike && hasSedan && hasCoupe
            && bike.Category == ItemCategory.Transport && bike.AutobuyMinTier == 2
            && sedan.Category == ItemCategory.Transport && sedan.AutobuyMinTier == 3
            && coupe.Category == ItemCategory.Transport && coupe.AutobuyMinTier == 4);

        // §5.1's worked example is shipped verbatim — the doc's own fixture.
        Check("§5.1 used_sedan example matches the doc cell for cell",
            catalog.TryGet("used_sedan", out ItemDefinition example)
            && example.Name == "Used Sedan"
            && example.Price == 1200.0
            && example.ModSocialStatus == 4 && example.ModReputation == 2 && example.ModAttractiveness == 0
            && example.TransportHoursSaved == 1.0);

        Check("AutobuysAt honors the tier floor (and never fires without one)",
            sedan.AutobuysAt(3) && sedan.AutobuysAt(4) && !sedan.AutobuysAt(2)
            && catalog.Require("skateboard").AutobuysAt(4) == false);

        // Loader rejections — each malformed doc is loud and id-labelled.
        Check("loader rejects duplicate item ids", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Gear", "price": 1 }, { "id": "x", "name": "B", "category": "Gear", "price": 2 } ] }"""));
        Check("loader rejects unknown category", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Vehicle", "price": 1 } ] }"""));
        Check("loader rejects negative price", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Gear", "price": -5 } ] }"""));
        Check("loader rejects a non-status modifier stat (the §5.2 closed vocabulary — calibration inertness)", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Gear", "price": 1, "modifiers": { "teamwork": 5 } } ] }"""));
        Check("loader rejects transport_hours_saved on a non-Transport item", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Clothing", "price": 1, "transport_hours_saved": 0.5 } ] }"""));
        Check("loader rejects autobuy_min_tier outside 0-4", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Gear", "price": 1, "autobuy_min_tier": 5 } ] }"""));
        Check("loader rejects a missing name", ItemParseThrows(
            """{ "items": [ { "id": "x", "category": "Gear", "price": 1 } ] }"""));
        Check("loader rejects a non-integer modifier value", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Gear", "price": 1, "modifiers": { "reputation": 1.5 } } ] }"""));
        Check("loader rejects a modifier outside [-100, 100]", ItemParseThrows(
            """{ "items": [ { "id": "x", "name": "A", "category": "Gear", "price": 1, "modifiers": { "reputation": 101 } } ] }"""));

        // §5.2 aggregation: computed at read, per-stat sum capped at +15,
        // negatives pass through, final effective clamps to [0, 100].
        var owned = new List<PlayerItemRow>();
        StatusBuffs none = ItemEffects.ComputeBuffs(owned, catalog);
        Check("§5.2 empty ownership buffs nothing",
            none.Attractiveness == 0 && none.SocialStatus == 0 && none.Reputation == 0
            && ItemEffects.EffectiveStat(50, none.SocialStatus) == 50);

        owned.Add(OwnedRow("p1", "used_sedan", ItemCategory.Transport));
        StatusBuffs single = ItemEffects.ComputeBuffs(owned, catalog);
        Check("§5.2 single item buffs exactly its modifiers",
            single.SocialStatus == 4 && single.Reputation == 2 && single.Attractiveness == 0
            && ItemEffects.EffectiveStat(50, single.SocialStatus) == 54);

        owned.Add(OwnedRow("p1", "new_coupe", ItemCategory.Transport));
        owned.Add(OwnedRow("p1", "gold_watch", ItemCategory.Jewelry));
        owned.Add(OwnedRow("p1", "designer_jacket", ItemCategory.Clothing));
        owned.Add(OwnedRow("p1", "silver_chain", ItemCategory.Jewelry));
        owned.Add(OwnedRow("p1", "mall_wardrobe", ItemCategory.Clothing));
        owned.Add(OwnedRow("p1", "laptop", ItemCategory.Gear));
        owned.Add(OwnedRow("p1", "bluetooth_speaker", ItemCategory.Gear));
        StatusBuffs hoard = ItemEffects.ComputeBuffs(owned, catalog);
        Check("§5.2 hoarding hits the +15 cap (raw social_status sum 28)",
            hoard.SocialStatus == ItemEffects.ItemBuffCap && hoard.Attractiveness == 13,
            $"social {hoard.SocialStatus}, attract {hoard.Attractiveness}");
        Check("§5.2 effective stat clamps at 100 under a capped buff",
            ItemEffects.EffectiveStat(95, hoard.SocialStatus) == 100);

        var edgy = new List<PlayerItemRow> { OwnedRow("p2", "fake_chain", ItemCategory.Jewelry) };
        StatusBuffs negative = ItemEffects.ComputeBuffs(edgy, catalog);
        Check("§5.2 negative modifiers pass the cap and clamp at the floor",
            negative.Reputation == -1 && ItemEffects.EffectiveStat(0, negative.Reputation) == 0);

        // §5.3: highest-value owned transport wins; non-transport is ignored.
        Check("§5.3 no transport owned refunds 0 hours",
            ItemEffects.BestTransportHoursSaved(edgy, catalog) == 0.0);
        var garage = new List<PlayerItemRow>
        {
            OwnedRow("p3", "skateboard", ItemCategory.Transport),
            OwnedRow("p3", "gold_watch", ItemCategory.Jewelry),
        };
        Check("§5.3 skateboard refunds its 0.25",
            ItemEffects.BestTransportHoursSaved(garage, catalog) == 0.25);
        garage.Add(OwnedRow("p3", "commuter_bike", ItemCategory.Transport));
        garage.Add(OwnedRow("p3", "used_sedan", ItemCategory.Transport));
        Check("§5.3 the car supersedes the bike and the board",
            ItemEffects.BestTransportHoursSaved(garage, catalog) == 1.0);

        // The boot-time ownership audit: valid rows pass, an unknown id and a
        // category drift each fail loudly.
        Check("ownership audit passes valid rows", !ThrowsAny(() => catalog.ValidateOwnership(garage)));
        Check("ownership audit rejects an id missing from the catalog", ThrowsAny(() =>
            catalog.ValidateOwnership(new List<PlayerItemRow> { OwnedRow("p4", "hoverboard", ItemCategory.Transport) })));
        Check("ownership audit rejects a stored-category drift", ThrowsAny(() =>
            catalog.ValidateOwnership(new List<PlayerItemRow> { OwnedRow("p4", "used_sedan", ItemCategory.Jewelry) })));

        // LoadAllItems round-trip on a real scratch save: the whole-table read
        // the boot audit uses returns every row across players, and AddItem's
        // OR-IGNORE double-own stays a no-op.
        string path = Path.Combine(Path.GetTempPath(), $"dnd_gritty_items_{Guid.NewGuid():N}.db");
        ScratchFiles.Add(path);
        using var db = new DatabaseManager(path);
        db.InitializeSchema(_schemaPath);
        var players = new PlayerQueries(db);
        var persons = new PersonQueries(db);
        players.Insert(new PlayerRow
        {
            PlayerId = "owner1", FirstName = "Test", LastName = "One", Age = 16, TeamId = null,
            Funds = 100, HealthCeiling = 100, Recklessness = 0, BaseballInterest = 0, DetectionRisk = 0,
        });
        players.Insert(new PlayerRow
        {
            PlayerId = "owner2", FirstName = "Test", LastName = "Two", Age = 16, TeamId = null,
            Funds = 100, HealthCeiling = 100, Recklessness = 0, BaseballInterest = 0, DetectionRisk = 0,
        });
        persons.AddItem(OwnedRow("owner1", "commuter_bike", ItemCategory.Transport));
        persons.AddItem(OwnedRow("owner1", "fake_chain", ItemCategory.Jewelry));
        persons.AddItem(OwnedRow("owner2", "used_sedan", ItemCategory.Transport));
        persons.AddItem(OwnedRow("owner2", "used_sedan", ItemCategory.Transport)); // OR-IGNORE no-op
        var loaded = new List<PlayerItemRow>();
        int count = persons.LoadAllItems(loaded);
        Check("LoadAllItems reads every row across players (double-own ignored)",
            count == 3 && loaded.Count == 3, $"{count} rows");
        Check("boot audit passes over rows read back from the save",
            !ThrowsAny(() => catalog.ValidateOwnership(loaded)));
    }

    // ------------------------------------------------------------------
    // Phone minutes economy + weekly family tick (HS-3,
    // high_school_person_layer.md §3/§3.2/§4.2/§4.3 — the §9.1 HS-3
    // acceptance hooks: minutes-economy arithmetic and the "pending event
    // resolves at 0 minutes" invariant).
    // ------------------------------------------------------------------

    private static void RunPhoneAndFamilyChecks()
    {
        Console.WriteLine("--- Phone minutes economy + family tick (§4.2/§3.2/§4.3) ---");

        ItemCatalog catalog = ItemCatalogJson.Parse(
            File.ReadAllText(Path.Combine(_repoRoot, "Assets", "Data", "Items", "items.json")));

        string path = Path.Combine(Path.GetTempPath(), $"dnd_gritty_phone_{Guid.NewGuid():N}.db");
        ScratchFiles.Add(path);
        using var db = new DatabaseManager(path);
        db.InitializeSchema(_schemaPath);
        var players = new PlayerQueries(db);
        var persons = new PersonQueries(db);
        var baseball = new BaseballQueries(db);
        var bus = new EventBus();
        var impulses = new List<FundsImpulseEvent>();
        bus.Subscribe<FundsImpulseEvent>(impulses.Add);

        var phone = new PhoneService(db, players, persons, bus);
        var family = new FamilyService(db, players, persons, baseball, catalog, phone, bus);
        var shop = new ItemService(db, players, persons, catalog, bus);

        // Two teams pin the parental-support window: 900 = the HS window
        // open, 901 = graduated (any non-HS tier).
        baseball.InsertTeam(new TeamRow { TeamId = 900, City = "Test", Name = "Preps", Abbreviation = "PRE" });
        baseball.UpsertTeamTier(900, LeagueTier.HS);
        baseball.InsertTeam(new TeamRow { TeamId = 901, City = "Test", Name = "Pros", Abbreviation = "PRO" });
        baseball.UpsertTeamTier(901, LeagueTier.MLB);

        void AddPerson(string id, int? teamId, double funds)
        {
            players.Insert(new PlayerRow
            {
                PlayerId = id, FirstName = "Test", LastName = id, Age = 16, TeamId = teamId,
                Funds = funds, HealthCeiling = 100, Recklessness = 0, BaseballInterest = 0, DetectionRisk = 0,
            });
        }
        double FundsOf(string id) => players.TryGetById(id, out PlayerRow row) ? row.Funds : double.NaN;
        PhoneStateRow PhoneOf(string id) => persons.TryGetPhone(id, out PhoneStateRow row) ? row : default;
        int ItemCount(string id)
        {
            var rows = new List<PlayerItemRow>();
            return persons.LoadItemsFor(id, rows);
        }
        bool OwnsItem(string id, string itemId)
        {
            var rows = new List<PlayerItemRow>();
            persons.LoadItemsFor(id, rows);
            return rows.Any(r => r.ItemId == itemId);
        }

        // --- §4.2 spend arithmetic ---------------------------------------
        AddPerson("prepaid", 900, 500);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "prepaid", Tier = 1, Plan = PhoneService.PrepaidPlan, MinutesRemaining = 30, PurchasedDay = 0 });
        AddPerson("unlimited", 900, 500);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "unlimited", Tier = 3, Plan = PhoneService.UnlimitedPlan, MinutesRemaining = 7, PurchasedDay = 0 });
        AddPerson("norow", 900, 500);

        Check("§4.2 spend: no Phone_State row is free and writes nothing",
            phone.TrySpendMinutes("norow", PhoneService.CallMinuteCost, onWifi: false, out _)
            && !persons.TryGetPhone("norow", out _));
        Check("§4.2 spend: Unlimited bypasses all minute accounting",
            phone.TrySpendMinutes("unlimited", PhoneService.CallMinuteCost, onWifi: false, out _)
            && PhoneOf("unlimited").MinutesRemaining == 7);
        Check("§4.2 spend: Wi-Fi is free on a metered plan",
            phone.TrySpendMinutes("prepaid", PhoneService.CallMinuteCost, onWifi: true, out _)
            && PhoneOf("prepaid").MinutesRemaining == 30);
        bool spentBrowse = phone.TrySpendMinutes("prepaid", PhoneService.MarketplaceBrowseMinuteCost, onWifi: false, out _);
        bool spentCall = phone.TrySpendMinutes("prepaid", PhoneService.CallMinuteCost, onWifi: false, out _);
        bool spentText = phone.TrySpendMinutes("prepaid", PhoneService.TextMinuteCost, onWifi: false, out _);
        Check("§4.2 spend: metered browse+call+text decrement exactly (30-3-6-2=19)",
            spentBrowse && spentCall && spentText && PhoneOf("prepaid").MinutesRemaining == 19,
            $"{PhoneOf("prepaid").MinutesRemaining} min");
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "prepaid", Tier = 1, Plan = PhoneService.PrepaidPlan, MinutesRemaining = 2, PurchasedDay = 0 });
        Check("§4.2 spend: insufficient minutes refuses and writes nothing",
            !phone.TrySpendMinutes("prepaid", PhoneService.MarketplaceBrowseMinuteCost, onWifi: false, out PhoneActionFailure broke)
            && broke == PhoneActionFailure.InsufficientMinutes && PhoneOf("prepaid").MinutesRemaining == 2);

        // --- §4.2 carrier bundle -----------------------------------------
        impulses.Clear();
        Check("§4.2 bundle: $10 → 100 minutes, funds and balance move together",
            phone.TryBuyBundle("prepaid", out _)
            && PhoneOf("prepaid").MinutesRemaining == 102
            && Math.Abs(FundsOf("prepaid") - 490.0) < 1e-9,
            $"{PhoneOf("prepaid").MinutesRemaining} min, ${FundsOf("prepaid")}");
        bus.DispatchPending();
        Check("§4.2 bundle: funds impulse published post-commit",
            impulses.Count == 1 && impulses[0].PlayerId == "prepaid" && Math.Abs(impulses[0].Delta + 10.0) < 1e-9);
        AddPerson("skint", 900, 4);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "skint", Tier = 1, Plan = PhoneService.PrepaidPlan, MinutesRemaining = 0, PurchasedDay = 0 });
        Check("§4.2 bundle: refused broke, nothing moves",
            !phone.TryBuyBundle("skint", out PhoneActionFailure skintWhy)
            && skintWhy == PhoneActionFailure.InsufficientFunds
            && PhoneOf("skint").MinutesRemaining == 0 && Math.Abs(FundsOf("skint") - 4.0) < 1e-9);
        Check("§4.2 bundle: refused on Unlimited (NotMetered)",
            !phone.TryBuyBundle("unlimited", out PhoneActionFailure unlimitedWhy)
            && unlimitedWhy == PhoneActionFailure.NotMetered);

        // --- §4.1 hardware upgrade (sold at the carrier) ------------------
        Check("upgrade: burner→mid charges $150 and rewrites tier + purchased_day",
            phone.TryUpgradePhone("skint", day: 9, out PhoneActionFailure poorWhy) == false
            && poorWhy == PhoneActionFailure.InsufficientFunds
            && phone.TryUpgradePhone("prepaid", day: 9, out _)
            && PhoneOf("prepaid").Tier == 2 && PhoneOf("prepaid").PurchasedDay == 9
            && Math.Abs(FundsOf("prepaid") - 340.0) < 1e-9,
            $"tier {PhoneOf("prepaid").Tier}, ${FundsOf("prepaid")}");
        Check("upgrade: flagship refuses TopTierOwned",
            !phone.TryUpgradePhone("unlimited", day: 9, out PhoneActionFailure topWhy)
            && topWhy == PhoneActionFailure.TopTierOwned);
        Check("upgrade: no rung is priced outside 2-3",
            PhoneService.PriceForTier(2) == PhoneService.MidTierPhonePrice
            && PhoneService.PriceForTier(3) == PhoneService.FlagshipPhonePrice
            && ThrowsAny(() => PhoneService.PriceForTier(1)));

        // --- §4.2 Basic-plan weekly refill ---------------------------------
        AddPerson("basic", 900, 100);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "basic", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 12, PurchasedDay = 0 });
        Check("§4.2 refill: Basic below the allotment tops up to exactly 50",
            phone.ApplyWeeklyRefill("basic") && PhoneOf("basic").MinutesRemaining == PhoneService.BasicWeeklyRefillMinutes);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "basic", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 80, PurchasedDay = 0 });
        Check("§4.2 refill: a topped-up balance is never reduced",
            !phone.ApplyWeeklyRefill("basic") && PhoneOf("basic").MinutesRemaining == 80);
        Check("§4.2 refill: Prepaid and Unlimited are untouched",
            !phone.ApplyWeeklyRefill("skint") && !phone.ApplyWeeklyRefill("unlimited") && !phone.ApplyWeeklyRefill("norow"));

        // --- §3/§3.2 weekly family tick ------------------------------------
        AddPerson("kid", 900, 100);
        persons.UpsertFamily(new FamilyBackgroundRow
        {
            PlayerId = "kid", WealthTier = 3, HouseholdIncome = 135_000,
            Parent1Id = null, Parent2Id = null, HomeWifi = true, AllowanceWeekly = 60, Strictness = 50,
        });
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "kid", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 10, PurchasedDay = 0 });

        family.ProcessDay("kid", 8); // 8 % 7 != 0
        Check("§3.2 tick: a non-cadence day is a complete no-op",
            Math.Abs(FundsOf("kid") - 100.0) < 1e-9 && PhoneOf("kid").MinutesRemaining == 10 && ItemCount("kid") == 0);

        bus.DispatchPending(); // drain the queued upgrade/bundle impulses first (publish is deferred)
        impulses.Clear();
        family.ProcessDay("kid", 7);
        bus.DispatchPending();
        Check("§3 tick: allowance paid (+60), impulse published, refill applied",
            Math.Abs(FundsOf("kid") - 160.0) < 1e-9
            && impulses.Count == 1 && impulses[0].PlayerId == "kid" && Math.Abs(impulses[0].Delta - 60.0) < 1e-9
            && PhoneOf("kid").MinutesRemaining == 50,
            $"${FundsOf("kid")}, {PhoneOf("kid").MinutesRemaining} min");
        Check("§3.2 tick: tier-3 parents gift the highest qualifying rung (used_sedan), avatar funds untouched by it",
            ItemCount("kid") == 1 && OwnsItem("kid", "used_sedan"));

        family.ProcessDay("kid", 14);
        Check("§3.2 tick: week 2 buys the next uncovered rung (mall_wardrobe, bike covered by the sedan)",
            ItemCount("kid") == 2 && OwnsItem("kid", "mall_wardrobe") && !OwnsItem("kid", "commuter_bike")
            && Math.Abs(FundsOf("kid") - 220.0) < 1e-9);

        family.ProcessDay("kid", 21);
        Check("§3.2 tick: week 3 has nothing left to gift (everything at-level is covered)",
            ItemCount("kid") == 2 && Math.Abs(FundsOf("kid") - 280.0) < 1e-9);

        AddPerson("rich", 900, 0);
        persons.UpsertFamily(new FamilyBackgroundRow
        {
            PlayerId = "rich", WealthTier = 4, HouseholdIncome = 320_000,
            Parent1Id = null, Parent2Id = null, HomeWifi = true, AllowanceWeekly = 150, Strictness = 50,
        });
        family.ProcessDay("rich", 7);
        family.ProcessDay("rich", 14);
        family.ProcessDay("rich", 21);
        Check("§3.2 tick: tier-4 ladder gifts coupe then jacket then stops (wardrobe covered)",
            ItemCount("rich") == 2 && OwnsItem("rich", "new_coupe") && OwnsItem("rich", "designer_jacket")
            && Math.Abs(FundsOf("rich") - 450.0) < 1e-9);

        AddPerson("modest", 900, 100);
        persons.UpsertFamily(new FamilyBackgroundRow
        {
            PlayerId = "modest", WealthTier = 2, HouseholdIncome = 72_000,
            Parent1Id = null, Parent2Id = null, HomeWifi = true, AllowanceWeekly = 25, Strictness = 50,
        });
        family.ProcessDay("modest", 7);
        Check("§3.2 tick: tier-2 parents pay allowance but never auto-purchase",
            Math.Abs(FundsOf("modest") - 125.0) < 1e-9 && ItemCount("modest") == 0);

        AddPerson("grad", 901, 100); // non-HS team: the parental-support window is closed
        persons.UpsertFamily(new FamilyBackgroundRow
        {
            PlayerId = "grad", WealthTier = 3, HouseholdIncome = 135_000,
            Parent1Id = null, Parent2Id = null, HomeWifi = true, AllowanceWeekly = 60, Strictness = 50,
        });
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "grad", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 5, PurchasedDay = 0 });
        family.ProcessDay("grad", 7);
        Check("§3.2 tick: a graduated avatar keeps the plan refill but loses allowance and gifts",
            Math.Abs(FundsOf("grad") - 100.0) < 1e-9 && ItemCount("grad") == 0
            && PhoneOf("grad").MinutesRemaining == 50);

        AddPerson("legacy", 900, 100); // pre-HS-2 save shape: no family row at all
        family.ProcessDay("legacy", 7);
        Check("§3.2 tick: no Family_Background row is a clean no-op",
            Math.Abs(FundsOf("legacy") - 100.0) < 1e-9 && ItemCount("legacy") == 0);

        // --- §4.2 LDR upkeep (hs_hometown_anchor "commit_long_distance") --
        void SetupLdr(string id, bool homeWifi)
        {
            AddPerson(id, 900, 100);
            persons.UpsertFamily(new FamilyBackgroundRow
            {
                PlayerId = id, WealthTier = 1, HouseholdIncome = 40_000,
                Parent1Id = null, Parent2Id = null, HomeWifi = homeWifi, AllowanceWeekly = 0, Strictness = 50,
            });
        }
        SetupLdr("ldr", homeWifi: false);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "ldr", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 12, PurchasedDay = 0 });
        players.SetFlag("ldr", "hs_dating", isActive: true, setOnDay: 0);
        players.SetFlag("ldr", "long_distance", isActive: true, setOnDay: 0);
        family.ProcessDay("ldr", 7);
        Check("§4.2 LDR: committed long-distance bills 20 min on top of the Basic refill (12→50→30)",
            PhoneOf("ldr").MinutesRemaining == 30, $"{PhoneOf("ldr").MinutesRemaining} min");

        SetupLdr("ex", homeWifi: false);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "ex", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 12, PurchasedDay = 0 });
        players.SetFlag("ex", "long_distance", isActive: true, setOnDay: 0); // grow_apart: long_distance set, hs_dating cleared/never set
        family.ProcessDay("ex", 7);
        Check("§4.2 LDR: an ex (long_distance set, hs_dating not active) is never billed upkeep",
            PhoneOf("ex").MinutesRemaining == 50, $"{PhoneOf("ex").MinutesRemaining} min");

        SetupLdr("dating_only", homeWifi: false);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "dating_only", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 12, PurchasedDay = 0 });
        players.SetFlag("dating_only", "hs_dating", isActive: true, setOnDay: 0);
        family.ProcessDay("dating_only", 7);
        Check("§4.2 LDR: dating locally (hs_dating without long_distance) is never billed upkeep",
            PhoneOf("dating_only").MinutesRemaining == 50, $"{PhoneOf("dating_only").MinutesRemaining} min");

        SetupLdr("broke_ldr", homeWifi: false);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "broke_ldr", Tier = 1, Plan = PhoneService.PrepaidPlan, MinutesRemaining = 15, PurchasedDay = 0 });
        players.SetFlag("broke_ldr", "hs_dating", isActive: true, setOnDay: 0);
        players.SetFlag("broke_ldr", "long_distance", isActive: true, setOnDay: 0);
        family.ProcessDay("broke_ldr", 7);
        Check("§4.2 LDR: insufficient minutes goes unpaid, not negative (Prepaid, 15 < 20)",
            PhoneOf("broke_ldr").MinutesRemaining == 15, $"{PhoneOf("broke_ldr").MinutesRemaining} min");

        SetupLdr("wifi_ldr", homeWifi: true);
        persons.UpsertPhone(new PhoneStateRow { PlayerId = "wifi_ldr", Tier = 2, Plan = PhoneService.BasicPlan, MinutesRemaining = 12, PurchasedDay = 0 });
        players.SetFlag("wifi_ldr", "hs_dating", isActive: true, setOnDay: 0);
        players.SetFlag("wifi_ldr", "long_distance", isActive: true, setOnDay: 0);
        family.ProcessDay("wifi_ldr", 7);
        Check("§4.2 LDR: home Wi-Fi bypasses the upkeep like any other metered action (12→50, no further drain)",
            PhoneOf("wifi_ldr").MinutesRemaining == 50, $"{PhoneOf("wifi_ldr").MinutesRemaining} min");

        // --- §3.1 self-buy transport reward --------------------------------
        AddPerson("earner", null, 2000);
        Check("§3.1 first self-bought transport pays +5 work_ethic/discipline/maturity",
            shop.TryPurchase("earner", "skateboard", day: 3, out _)
            && persons.TryGet("earner", out PersonRow earned)
            && earned.WorkEthic == 55 && earned.Discipline == 55 && earned.Maturity == 55
            && Math.Abs(FundsOf("earner") - 1920.0) < 1e-9);
        Check("§3.1 the reward is one-time (a second transport pays nothing)",
            shop.TryPurchase("earner", "commuter_bike", day: 4, out _)
            && persons.TryGet("earner", out PersonRow second)
            && second.WorkEthic == 55 && second.Discipline == 55 && second.Maturity == 55);
        AddPerson("gifted", null, 2000);
        persons.AddItem(new PlayerItemRow { PlayerId = "gifted", ItemId = "commuter_bike", Category = ItemCategory.Transport, AcquiredDay = 0 });
        Check("§3.1 a gifted-transport player never earns the reward",
            shop.TryPurchase("gifted", "used_sedan", day: 3, out _)
            && !persons.TryGet("gifted", out _));
        AddPerson("shopper", null, 2000);
        Check("§3.1 a non-transport purchase never touches person stats",
            shop.TryPurchase("shopper", "fake_chain", day: 3, out _)
            && !persons.TryGet("shopper", out _));

        // --- HS-4 publisher side: acquired events + person-stat mirror -----
        // The subscriber half (LifeSimManager's mirror) is NeedsDecayHarness
        // territory; this pins that the Economy services actually PUBLISH,
        // post-commit, with the actual clamped deltas and the contracted
        // PersonStatId ordinals.
        var acquired = new List<PlayerItemAcquiredEvent>();
        var statImpulses = new List<PersonStatImpulseEvent>();
        bus.Subscribe<PlayerItemAcquiredEvent>(acquired.Add);
        bus.Subscribe<PersonStatImpulseEvent>(statImpulses.Add);
        bus.DispatchPending(); // drain everything queued before the collectors attached
        acquired.Clear();
        statImpulses.Clear();

        AddPerson("mover", null, 2000);
        shop.TryPurchase("mover", "skateboard", day: 5, out _);
        bus.DispatchPending();
        Check("HS-4 publish: a purchase raises PlayerItemAcquiredEvent post-commit (the §5.3 transport re-projection seam)",
            acquired.Count == 1 && acquired[0].PlayerId == "mover" && acquired[0].ItemId == "skateboard");
        Check("HS-4 publish: the §3.1 reward mirrors as three person-stat impulses (ordinals 1/10/11 = maturity/discipline/work_ethic, +5 each)",
            statImpulses.Count == 3
            && statImpulses.All(i => i.PlayerId == "mover" && Math.Abs(i.Delta - 5f) < 1e-6)
            && statImpulses.Select(i => i.Stat).OrderBy(s => s).SequenceEqual(new[] { 1, 10, 11 }));

        acquired.Clear();
        statImpulses.Clear();
        shop.TryPurchase("mover", "fake_chain", day: 5, out _);
        bus.DispatchPending();
        Check("HS-4 publish: a non-transport purchase raises the acquired event but zero stat impulses",
            acquired.Count == 1 && acquired[0].ItemId == "fake_chain" && statImpulses.Count == 0);

        AddPerson("kid2", 900, 100);
        persons.UpsertFamily(new FamilyBackgroundRow
        {
            PlayerId = "kid2", WealthTier = 3, HouseholdIncome = 135_000,
            Parent1Id = null, Parent2Id = null, HomeWifi = true, AllowanceWeekly = 60, Strictness = 50,
        });
        acquired.Clear();
        family.ProcessDay("kid2", 7);
        bus.DispatchPending();
        Check("HS-4 publish: a §3.2 parental gift raises PlayerItemAcquiredEvent too (both Player_Items writers covered)",
            acquired.Count == 1 && acquired[0].PlayerId == "kid2" && acquired[0].ItemId == "used_sedan");

        // --- §4.3 the narrative-never-gates invariant ----------------------
        // An avatar with a metered phone at literally 0 minutes: the event
        // still fires, still parks as a pending choice, and resolving it
        // applies every consequence while the Phone_State row stays
        // byte-identical. Nothing in Narrative can even reference the phone —
        // this proves the wired path end to end.
        const string brokeJson =
            """
            { "events": [
              { "id": "no_minutes_event", "scope": "avatar", "weight": 1.0,
                "choices": [ { "id": "answer", "autopilot_weight": 1, "consequences": [
                  { "type": "funds", "amount": -50 } ] } ] }
            ] }
            """;
        using (World world = World.Create("phoneInvariant", GrittyEventJson.Parse(brokeJson)))
        {
            world.Applier.AutopilotAvatarChoices = false;
            world.AddPlayer("hero", age: 17, teamId: 900, funds: 1000);
            world.GameState.SetText(GameStateKeys.AvatarPlayerId, "hero");
            var worldPersons = new PersonQueries(world.Db);
            worldPersons.UpsertPhone(new PhoneStateRow
            {
                PlayerId = "hero", Tier = 1, Plan = PhoneService.PrepaidPlan, MinutesRemaining = 0, PurchasedDay = 0,
            });

            world.Dispatcher.PollOnce(); // record the boot day
            world.Clock.AdvanceDay();
            world.Bus.DispatchPending();
            world.Dispatcher.PollOnce();
            world.Bus.DispatchPending();

            bool pending = world.Applier.HasPendingChoice;
            if (pending)
            {
                world.Applier.ResolveChoice(0);
                world.Bus.DispatchPending();
            }
            world.Players.TryGetById("hero", out PlayerRow heroAfter);
            worldPersons.TryGetPhone("hero", out PhoneStateRow phoneAfter);
            Check("§4.3 a pending event fires, parks, and resolves at 0 minutes",
                pending && !world.Applier.HasPendingChoice
                && Math.Abs(heroAfter.Funds - 950.0) < 1e-9,
                $"pending={pending}, funds={heroAfter.Funds}");
            Check("§4.3 the zero-minute Phone_State row is byte-identical after the resolve",
                phoneAfter.Tier == 1 && phoneAfter.Plan == PhoneService.PrepaidPlan
                && phoneAfter.MinutesRemaining == 0 && phoneAfter.PurchasedDay == 0);
        }
    }

    private static bool ItemParseThrows(string json)
    {
        try
        {
            ItemCatalogJson.Parse(json);
            return false;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static PlayerItemRow OwnedRow(string playerId, string itemId, ItemCategory category) => new()
    {
        PlayerId = playerId,
        ItemId = itemId,
        Category = category,
        AcquiredDay = 0,
    };

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
