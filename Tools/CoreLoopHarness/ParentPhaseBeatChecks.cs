using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Narrative.Family;
using DirtAndDiamonds.Narrative.Pipeline;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Tools.CoreLoopHarness;

/// <summary>
/// Slice 13-6d CoreLoop fixtures (brief §2 items (c) and (d), sanctioned at the
/// 13-6c merge): the retiree's release line in <see cref="PipelineBeatWriter"/>
/// and the parent phase's phone texts in <see cref="ParentPhaseBeatWriter"/>.
/// Both ride <see cref="ParentWorld"/>, the 13-6c real-loop fixture, with the
/// writer attached after the engine's handlers (GameManager's order).
/// </summary>
internal static partial class Program
{
    private const string BeatsFirstWinText = "saw {child} out there";
    private const string BeatsCoaching70Text = "{child} is getting somewhere";

    private static string BeatsText(string key) => key switch
    {
        "family.payday.first.coach" => "check ${wage}",
        "family.payday.first.nightlife" => "bar ${wage}",
        ParentPhaseBeatWriter.FirstWinKey => BeatsFirstWinText,
        ParentPhaseBeatWriter.Coaching70Key => BeatsCoaching70Text,
        _ => key,
    };

    private static List<NarrativeMessageRow> BeatsTexts(ParentWorld w)
    {
        var rows = new List<NarrativeMessageRow>();
        new NarrativeLogQueries(w.Db).LoadForPlayer(w.ParentId, w.Day, rows);
        return rows;
    }

    private static ParentPhaseBeatWriter AttachParentBeats(ParentWorld w)
    {
        var writer = new ParentPhaseBeatWriter(
            w.Db, new NarrativeLogQueries(w.Db), w.GameState, w.Players, w.Persons, w.Jobs, w.Training, w.State)
        {
            ResolveText = BeatsText,
        };
        writer.AttachTo(w.Bus);
        return writer;
    }

    private static void RunParentPhaseBeatChecks(string schemaPath, string basePath, string repoRoot)
    {
        Console.WriteLine("--- 13-6d parent-phase texts (the retiree release line, first day, first payday, training milestones) ---");
        var paths = new List<string>();
        string NewPath(string tag)
        {
            string p = basePath + "." + tag + ".db";
            paths.Add(p);
            return p;
        }
        try
        {
            RunRetireeReleaseTextChecks(schemaPath, NewPath);
            RunJobTextChecks(schemaPath, NewPath);
            RunTrainingMilestoneChecks(schemaPath, NewPath);
            RunParentTextSecrecyScan(repoRoot);
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
    // (c) The retiree's release line
    // ------------------------------------------------------------------

    private static void RunRetireeReleaseTextChecks(string schemaPath, Func<string, string> newPath)
    {
        using var w = new ParentWorld(schemaPath, newPath("retiree"), startDay: 50);
        var configured = new PipelineBeatWriter(w.Db, new NarrativeLogQueries(w.Db), w.State, w.Players) { RetirementAge = 42 };
        configured.AttachTo(w.Bus);

        void Release(int age, byte tier, bool parentPhase)
        {
            w.Players.SetAge(w.ParentId, age);
            w.Bus.Publish(new AvatarReleasedEvent(w.ParentId, tier, w.State.SeasonYear, w.Day, parentPhase));
            w.Bus.DispatchPending();
        }

        Release(42, tier: 2, parentPhase: true);
        bool retiree = configured.LastKey == PipelineBeatWriter.ReleaseRetiredParentKey && configured.LastContact == "coach_reyes";
        Release(41, tier: 2, parentPhase: true);
        bool healthFloor = configured.LastKey == PipelineBeatWriter.ReleaseProParentKey;
        Release(41, tier: 1, parentPhase: true);
        bool college = configured.LastKey == PipelineBeatWriter.ReleaseCollegeParentKey;
        Release(55, tier: 2, parentPhase: false);
        bool gameOver = configured.LastKey == PipelineBeatWriter.ReleaseProGameOverKey;
        Check("13-6d (c): a release into the parent phase at or past the mandatory retirement age writes the retiree line from coach_reyes; a younger (health-floor) Pro release and a College release keep their tier text, and a game-over release is untouched even at 55",
            retiree && healthFloor && college && gameOver && configured.TextsWritten == 4,
            $"retiree {retiree}, healthFloor {healthFloor}, college {college}, gameOver {gameOver}, texts {configured.TextsWritten}");

        // The identity: a writer built the old way (no player query, no retirement age) writes today's text at any age.
        var legacy = new PipelineBeatWriter(w.Db, new NarrativeLogQueries(w.Db), w.State);
        legacy.AttachTo(w.Bus);
        w.Players.SetAge(w.ParentId, 60);
        w.Bus.Publish(new AvatarReleasedEvent(w.ParentId, 2, w.State.SeasonYear, w.Day, true));
        w.Bus.DispatchPending();
        Check("13-6d (c): a writer without the retirement age (the pre-13-6d construction) still writes the Pro parent-phase text for a 60-year-old — the retiree line is opt-in",
            legacy.LastKey == PipelineBeatWriter.ReleaseProParentKey, legacy.LastKey);
    }

    // ------------------------------------------------------------------
    // (d) The first-day text and the first payday
    // ------------------------------------------------------------------

    private static void RunJobTextChecks(string schemaPath, Func<string, string> newPath)
    {
        // A chosen job: the first-day text on the first tick after, the first payday on the first 7-day tick, nothing after.
        {
            using var w = new ParentWorld(schemaPath, newPath("jobtext"), startDay: 63, children: new[] { (3, 70) }); // an untrainable child: no milestone muddies the counts
            ParentPhaseBeatWriter writer = AttachParentBeats(w);
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            w.RunTo(w.Day + 5); // the card is open and no job is held: nothing to say yet
            int quiet = writer.TextsWritten;
            w.ChooseParentJob("coach"); // taken on day 68
            long chosen = w.Day;
            w.RunTo(chosen + 1);
            List<NarrativeMessageRow> afterDay1 = BeatsTexts(w);
            bool firstDay = afterDay1.Count == 1 && afterDay1[0].Body == "family.job.first_day.coach"
                && afterDay1[0].ContactId == ParentPhaseBeatWriter.ParentContact && afterDay1[0].GameDay == chosen + 1;
            long payday = ParentPhaseBeatWriter.FirstPaydayAfter(chosen);
            w.RunTo(payday - 1);
            int beforePayday = BeatsTexts(w).Count;
            w.RunTo(payday);
            List<NarrativeMessageRow> atPayday = BeatsTexts(w);
            // Coach's $650 wage; this tick's settlement landed before the writer read it.
            bool paydayText = atPayday.Count == 2 && atPayday[1].Body == "check $650" && atPayday[1].GameDay == payday;
            w.RunTo(payday + 35);
            int total = BeatsTexts(w).Count;
            Check("13-6d (d): no text while the card is open; a chosen job's first-day text lands once on the first tick after (from family_mom); the first payday text lands once on the first 7-day tick after it with the settled wage ($650 for Coach); five more paydays send nothing (sparing)",
                quiet == 0 && firstDay && beforePayday == 1 && paydayText && total == 2 && CountBody(BeatsTexts(w), "check $650") == 1
                && CountBody(BeatsTexts(w), "family.job.first_day.coach") == 1,
                $"quiet {quiet}, firstDay {firstDay}, before payday {beforePayday}, payday text {paydayText}, total {total}");
        }

        // A job taken at the deadline (the unanswered card): its first-day text comes the day after the tick that took it.
        {
            using var w = new ParentWorld(schemaPath, newPath("jobdeadline"), startDay: 63, children: new[] { (3, 70) });
            ParentPhaseBeatWriter writer = AttachParentBeats(w);
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            long opened = w.Day;
            w.RunTo(opened + ParentJobService.DecisionWindowDays); // Steady takes over on this tick
            long took = w.Jobs.CurrentJobSinceDay;
            int sameDay = writer.TextsWritten;
            w.RunTo(took + 1);
            List<NarrativeMessageRow> rows = BeatsTexts(w);
            Check("13-6d (d): an unanswered card's deadline job (Steady) sends its first-day text on the day after the tick that took it — once, with nothing on the tick itself",
                w.Jobs.CurrentJobId == "steady" && sameDay == 0 && rows.Count == 1
                && rows[0].Body == "family.job.first_day.steady" && rows[0].GameDay == took + 1,
                $"took on {took}, texts on the tick {sameDay}, then {rows.Count}");
        }

        // A reload: nothing is lost and nothing repeats (the texts are arithmetic on persisted days).
        {
            string path = newPath("jobreload");
            long chosen;
            {
                using var w = new ParentWorld(schemaPath, path, startDay: 63, children: new[] { (3, 70) });
                AttachParentBeats(w);
                w.Jobs.OpenJobCard(w.ParentId, w.Day);
                w.ChooseParentJob("nightlife");
                chosen = w.Day;
                // Save and reload between the job and its first-day text.
            }
            int firstDayCount;
            int paydayCount;
            {
                using var w = new ParentWorld(schemaPath, path, startDay: 63);
                AttachParentBeats(w);
                w.RunTo(chosen + 1);
                firstDayCount = CountBody(BeatsTexts(w), "family.job.first_day.nightlife");
                w.RunTo(ParentPhaseBeatWriter.FirstPaydayAfter(chosen) - 1);
            }
            {
                using var w = new ParentWorld(schemaPath, path, startDay: 63); // reload again, before the payday
                AttachParentBeats(w);
                w.RunTo(ParentPhaseBeatWriter.FirstPaydayAfter(chosen));
                w.Jobs.TryGetLastSettlement(out JobSettlement settlement);
                List<NarrativeMessageRow> rows = BeatsTexts(w);
                paydayCount = rows.Count(r => r.Body.StartsWith("bar $", StringComparison.Ordinal));
                bool amount = rows.Any(r => r.Body == $"bar ${settlement.Total}");
                w.RunTo(ParentPhaseBeatWriter.FirstPaydayAfter(chosen) + 28);
                int firstDayAfter = CountBody(BeatsTexts(w), "family.job.first_day.nightlife");
                Check("13-6d (d): across two reloads the first-day text (sent once, the tick after the job) and the first payday text (once, carrying that tick's settled total, tips included) neither vanish nor repeat",
                    firstDayCount == 1 && firstDayAfter == 1 && paydayCount == 1 && amount && CountBody(BeatsTexts(w), $"bar ${settlement.Total}") == 1,
                    $"first day {firstDayCount}/{firstDayAfter}, payday {paydayCount}, amount {amount}");
            }
        }
    }

    // ------------------------------------------------------------------
    // (d) The training milestones
    // ------------------------------------------------------------------

    private static void RunTrainingMilestoneChecks(string schemaPath, Func<string, string> newPath)
    {
        string path = newPath("milestones");
        string child;
        long drillDay;
        {
            using var w = new ParentWorld(schemaPath, path, startDay: 63, children: new[] { (9, 70) });
            ParentPhaseBeatWriter writer = AttachParentBeats(w);
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            w.ChooseParentJob("coach");
            child = w.Children()[0].PlayerId;
            w.RunTo(w.Day + 1); // the first-day text; no session yet
            int before = writer.TextsWritten;
            bool noMilestoneYet = BeatsTexts(w).All(r => !r.Body.StartsWith("saw ", StringComparison.Ordinal));

            // A perfect drill: success (the seeded stream is fixed), or retry within the week's three sessions.
            bool success = false;
            for (int i = 0; i < 3 && !success; i++)
            {
                w.Life.TryGetStress(w.ParentId, out float stress);
                success = w.Training.PlayDrill(w.ParentId, child, 1.0, stress, w.Day).Outcome.Success;
            }
            drillDay = w.Day;
            w.RunTo(drillDay + 1);
            List<NarrativeMessageRow> rows = BeatsTexts(w);
            var flags = new List<EntityFlagRow>();
            w.Players.LoadActiveFlags(w.ParentId, flags);
            bool firstWin = success && CountBody(rows, "saw Kid out there") == 1
                && flags.Exists(f => f.IsActive && f.FlagName == ParentPhaseBeatWriter.FirstWinFlag);
            w.RunTo(drillDay + 21); // autopilot successes and more ticks: still once
            bool once = CountBody(BeatsTexts(w), "saw Kid out there") == 1;
            Check("13-6d (d): the first successful session writes one 'first win' text with the child's first name ({child} resolved at write time) on the next tick — from the persisted plan, not the in-memory result — and marks the parent; later successes (drills or autopilot) repeat nothing",
                noMilestoneYet && before >= 1 && firstWin && once && ParentPhaseBeatWriter.FirstWinKey == "family.train.first_win",
                $"success {success}, texts {CountBody(BeatsTexts(w), "saw Kid out there")}");

            // Coaching tops 70: lift the axis (a child's training can get there; this fixture takes the shortcut) and tick.
            w.Persons.TryGetChild(child, out ChildDevelopmentRow axes);
            bool below = axes.Coaching < ParentPhaseBeatWriter.Coaching70Threshold;
            Check("13-6d (d): before the axis reaches 70 there is no coaching-70 text (a few weeks of Coach training starts well below it)",
                below && CountBody(BeatsTexts(w), "Kid is getting somewhere") == 0,
                $"coaching {axes.Coaching}");
            w.Persons.AdjustChildAxis(child, 1, ParentPhaseBeatWriter.Coaching70Threshold - axes.Coaching + 1, w.Day);
            w.RunTo(w.Day + 1);
            w.RunTo(w.Day + 14);
            var flags70 = new List<EntityFlagRow>();
            w.Players.LoadActiveFlags(w.ParentId, flags70);
            Check("13-6d (d): the first time a trained child's coaching reaches 70 writes one text (with the first name) and marks the parent; two more weeks of ticks repeat nothing",
                CountBody(BeatsTexts(w), "Kid is getting somewhere") == 1
                && flags70.Exists(f => f.IsActive && f.FlagName == ParentPhaseBeatWriter.Coaching70Flag),
                $"texts {CountBody(BeatsTexts(w), "Kid is getting somewhere")}");
        }

        // Reload: both milestones were marked, so a fresh boot repeats neither.
        {
            using var w = new ParentWorld(schemaPath, path, startDay: 63);
            AttachParentBeats(w);
            int win = CountBody(BeatsTexts(w), "saw Kid out there");
            int seventy = CountBody(BeatsTexts(w), "Kid is getting somewhere");
            w.RunTo(w.Day + 21);
            Check("13-6d (d): after a reload neither milestone text repeats (the marks are persisted, the plan and axis are unchanged)",
                win == 1 && seventy == 1 && CountBody(BeatsTexts(w), "saw Kid out there") == 1
                && CountBody(BeatsTexts(w), "Kid is getting somewhere") == 1,
                $"win {win}, seventy {seventy}");
        }

        // A job with only an untrainable child (under 5) never sends a training milestone.
        {
            using var w = new ParentWorld(schemaPath, newPath("milestones_none"), startDay: 63, children: new[] { (3, 70) });
            ParentPhaseBeatWriter writer = AttachParentBeats(w);
            w.Jobs.OpenJobCard(w.ParentId, w.Day);
            w.ChooseParentJob("steady");
            w.RunTo(w.Day + 35);
            Check("13-6d (d): with only an untrainable 3-year-old the parent still gets the first-day and first-payday texts, and no training milestone text or mark ever",
                writer.TextsWritten == 2 && CountBody(BeatsTexts(w), "saw Kid out there") == 0,
                $"{writer.TextsWritten} texts");
        }
    }

    private static void RunParentTextSecrecyScan(string repoRoot)
    {
        string source = File.ReadAllText(Path.Combine(repoRoot, "Assets", "Narrative", "Family", "ParentPhaseBeatWriter.cs"));
        Check("13-6d (d): the parent-phase text writer reads no Biological_Parentage or LineageQueries (secrecy) and takes its children only from ChildTrainingService.LoadTrainable, and publishes no bus event",
            !source.Contains("Biological_Parentage", StringComparison.Ordinal)
            && !source.Contains("LineageQueries", StringComparison.Ordinal)
            && !source.Contains("LoadChildrenOf", StringComparison.Ordinal)
            && source.Contains("LoadTrainable", StringComparison.Ordinal)
            && !source.Contains(".Publish", StringComparison.Ordinal));
    }

    private static int CountBody(List<NarrativeMessageRow> rows, string body)
    {
        int count = 0;
        foreach (NarrativeMessageRow row in rows)
        {
            if (string.Equals(row.Body, body, StringComparison.Ordinal))
            {
                count++;
            }
        }
        return count;
    }
}
