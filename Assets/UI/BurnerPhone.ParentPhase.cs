using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Data.Family;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Slice 13-6d (brief §4.4 items 1–3): the parent phase on the Burner Phone — the
/// job card (the 13-6b decision card, a third kind on the Events tab's pipeline
/// card), the Family tab's job strip and per-child training controls, and the
/// launch of the <see cref="BackyardDrill"/>. A partial of <see cref="BurnerPhone"/>
/// so the phase's UI stays one reviewable unit.
///
/// Presentation only. It reads <see cref="GameManager.TryGetPendingJobChoice"/>,
/// <see cref="GameManager.LoadTrainableChildren"/>, <see cref="GameManager.GetTrainingBudget"/>,
/// <see cref="GameManager.CanTrainChild"/>, <c>ParentJobs.TryGetLastSettlement</c> and
/// <c>Training.TryGetLastResult</c>, and sends intent through
/// <see cref="GameManager.ChooseParentJob"/> and <see cref="GameManager.SetTrainingPlan"/>
/// (the drill sends <c>PlayDrill</c> itself). It never writes the database and never
/// touches an RNG. The trainable list comes from <c>LoadTrainableChildren</c> only
/// (Child edges, so a hidden child can never appear). The job card keeps the
/// pipeline cards' rule: the clock never stops, the card names its deadline, and an
/// unanswered card resolves there by the engine's rule.
///
/// Every string is a <c>ui.job.*</c> or <c>ui.family.*</c> strings-table key. Node
/// paths verified against BurnerPhone.tscn before wiring.
/// </summary>
public sealed partial class BurnerPhone
{
    private const string BackyardDrillScenePath = "res://Assets/UI/BackyardDrill.tscn";

    // The Family tab's job strip (BurnerPhone.tscn, FamilyLayout/JobCard).
    private PanelContainer _jobCard = null!;
    private Label _jobHeadingLabel = null!;
    private Label _jobTitleLabel = null!;
    private Label _jobWageLabel = null!;
    private Label _jobNextShiftLabel = null!;
    private Label _trainingBudgetLabel = null!;

    // The job card's per-day pending read: the engine reads Game_State for it, so the phone
    // asks once per day change (or after its own answer), never per frame. It is only read
    // while the bus is drained (RefreshPipelineCard's own guard), so a card opened by the
    // day's tick is always seen.
    private long _jobCardDay = long.MinValue;
    private bool _jobCardOpen;
    private PendingJobChoice _jobCardChoice;

    // The Family tab's per-day rebuild state.
    private readonly List<PlayerRow> _trainableScratch = new(4);
    private readonly HashSet<string> _trainableIds = new(StringComparer.Ordinal);
    private readonly List<Control[]> _familyFocusRows = new();
    private readonly Dictionary<string, Button> _trainButtons = new(StringComparer.Ordinal);
    private bool _familyParentPhase;
    private string? _familyAvatarId;

    // The drill modal: owned by the phone, on its own canvas layer, so no other scene is touched.
    private CanvasLayer? _drillLayer;
    private string? _drillFocusChildId;

    private void InitParentPhaseUi()
    {
        const string strip = "Screen/ScreenLayout/PhoneTabs/Family/FamilyScroll/FamilyLayout/JobCard";
        _jobCard = GetNode<PanelContainer>(strip);
        _jobHeadingLabel = GetNode<Label>(strip + "/JobCardLayout/JobHeadingLabel");
        _jobTitleLabel = GetNode<Label>(strip + "/JobCardLayout/JobTitleLabel");
        _jobWageLabel = GetNode<Label>(strip + "/JobCardLayout/JobWageLabel");
        _jobNextShiftLabel = GetNode<Label>(strip + "/JobCardLayout/JobNextShiftLabel");
        _trainingBudgetLabel = GetNode<Label>(strip + "/JobCardLayout/TrainingBudgetLabel");
    }

    // ------------------------------------------------------------------
    // The job card (Events tab)
    // ------------------------------------------------------------------

    /// <summary>The open job card, asked once per day change and after the phone's own answer.</summary>
    private bool TryGetJobCard(GameManager gm, out PendingJobChoice choice)
    {
        long day = gm.State.CurrentDay;
        if (day != _jobCardDay)
        {
            _jobCardDay = day;
            _jobCardOpen = gm.TryGetPendingJobChoice(out _jobCardChoice);
        }
        choice = _jobCardChoice;
        return _jobCardOpen;
    }

    private void RenderJobCard(GameManager gm, in PendingJobChoice pending)
    {
        _pipelineCard.Visible = true;
        _pipelineAnsweredLabel.Visible = false;
        bool yearly = pending.CurrentJobId is not null;
        _pipelineHeadingLabel.Text = Tr(yearly ? "ui.job.card.heading.yearly" : "ui.job.card.heading");
        _pipelineBodyLabel.Text = Tr(yearly ? "ui.job.card.body.yearly" : "ui.job.card.body");
        _pipelineBodyLabel.Visible = true;
        _pipelineDeadlineLabel.Visible = true;
        _pipelineChoicesBox.Visible = true;
        int daysLeft = (int)(pending.DeadlineDay - gm.State.CurrentDay);
        _pipelineDeadlineLabel.Text = Tr("ui.job.card.deadline") + " " + DaysLeftText(daysLeft) + "\n"
            + Tr(yearly ? "ui.job.card.unanswered.yearly" : "ui.job.card.unanswered.first");

        foreach (ParentJobOption option in pending.Options)
        {
            AddJobChoice(in option, pending.CurrentJobId);
        }
        LinkJobChoiceFocus();
    }

    private void AddJobChoice(in ParentJobOption option, string? currentJobId)
    {
        ParentJobProfile job = option.Job;
        string id = job.Id;
        string name = Tr("ui.job." + id + ".name");
        if (string.Equals(currentJobId, id, StringComparison.Ordinal))
        {
            name += " " + Tr("ui.job.card.current");
        }
        string pay = job.TipMax > 0
            ? string.Format(Tr("ui.job.card.pay.tips"), job.WeeklyWage, job.TipMax)
            : string.Format(Tr("ui.job.card.pay.flat"), job.WeeklyWage);
        string numbers = pay + " · "
            + string.Format(Tr("ui.job.card.shift"), job.ShiftHours, Tr("ui.job." + id + ".shift")) + " · "
            + string.Format(Tr("ui.job.card.sessions"), job.SessionsPerWeek);
        string text = name + "\n" + numbers + "\n" + Tr("ui.job." + id + ".pitch");
        if (!option.Available && option.BarredByFlag is string flag)
        {
            text += "\n" + Tr("ui.job.barred." + flag); // D8-A: the reason a barred job is greyed out
        }

        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = "ReplyChip",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Disabled = !option.Available,
        };
        button.Pressed += () => OnJobPicked(id);
        _pipelineChoicesBox.AddChild(button);
    }

    /// <summary>
    /// Keyboard/controller reach: Up/Down walk the choices that can be taken. A job
    /// that is barred is greyed out and skipped (a disabled button can't hold focus).
    /// </summary>
    private void LinkJobChoiceFocus()
    {
        var enabled = new List<Button>(3);
        foreach (Node child in _pipelineChoicesBox.GetChildren())
        {
            if (child is Button { Disabled: false } button)
            {
                enabled.Add(button);
            }
        }
        for (int i = 0; i < enabled.Count; i++)
        {
            if (i > 0)
            {
                enabled[i].FocusNeighborTop = enabled[i - 1].GetPath();
            }
            if (i < enabled.Count - 1)
            {
                enabled[i].FocusNeighborBottom = enabled[i + 1].GetPath();
            }
        }
    }

    private void OnJobPicked(string jobId)
    {
        GameManager gm = GameManager.Instance!;
        // One answer per card, and only a job the card offers as available (a stale press, or a barred job, does nothing).
        if (!gm.TryGetPendingJobChoice(out PendingJobChoice pending))
        {
            return;
        }
        bool offered = false;
        foreach (ParentJobOption option in pending.Options)
        {
            offered |= option.Available && string.Equals(option.Job.Id, jobId, StringComparison.Ordinal);
        }
        if (!offered)
        {
            return;
        }
        DisablePipelineChoices();
        gm.ChooseParentJob(jobId);
        RecordPipelineAnswer(
            gm, Tr("ui.job.card.heading"), string.Format(Tr("ui.job.card.answered"), Tr("ui.job." + jobId + ".name")));
        _jobCardDay = long.MinValue; // re-ask: the card is closed now
        _familyLoadedDay = long.MinValue; // and the Family tab's job strip is new
    }

    // ------------------------------------------------------------------
    // The Family tab: the job strip
    // ------------------------------------------------------------------

    /// <summary>True when the avatar or the parent-phase flag differs from what the Family tab last built (both in-memory reads).</summary>
    private bool FamilyPhaseChanged(GameManager gm) =>
        gm.Career.InParentPhase != _familyParentPhase
        || !string.Equals(gm.Career.AvatarPlayerId, _familyAvatarId, StringComparison.Ordinal);

    /// <summary>Called by <see cref="RefreshFamilyTab"/> (once per day change): the job, its last payday, the next shift and the week's session budget.</summary>
    private void RefreshJobStrip(GameManager gm)
    {
        if (gm.Career.InParentPhase != _familyParentPhase)
        {
            _jobCardDay = long.MinValue; // the phase began or ended: ask the card question again
        }
        _familyParentPhase = gm.Career.InParentPhase;
        _familyAvatarId = gm.Career.AvatarPlayerId;
        _jobCard.Visible = _familyParentPhase;
        _trainableIds.Clear();
        if (!_familyParentPhase)
        {
            return;
        }
        gm.LoadTrainableChildren(_trainableScratch);
        foreach (PlayerRow child in _trainableScratch)
        {
            _trainableIds.Add(child.PlayerId);
        }

        _jobHeadingLabel.Text = Tr("ui.family.job.heading");
        if (!gm.ParentJobs.TryGetCurrentJob(out ParentJobProfile job))
        {
            _jobTitleLabel.Text = Tr("ui.family.job.none");
            _jobWageLabel.Visible = false;
            _jobNextShiftLabel.Visible = false;
            _trainingBudgetLabel.Text = Tr("ui.family.budget.none");
            return;
        }

        _jobTitleLabel.Text = string.Format(Tr("ui.family.job.title"), Tr("ui.job." + job.Id + ".name"));
        _jobWageLabel.Visible = true;
        _jobWageLabel.Text = gm.ParentJobs.TryGetLastSettlement(out JobSettlement settlement)
            && string.Equals(settlement.JobId, job.Id, StringComparison.Ordinal)
                ? settlement.Tips > 0
                    ? string.Format(Tr("ui.family.job.wage.tips"), settlement.Total, settlement.Tips)
                    : string.Format(Tr("ui.family.job.wage"), settlement.Total)
                : Tr("ui.family.job.wage.none");
        _jobNextShiftLabel.Visible = true;
        _jobNextShiftLabel.Text = NextShiftText(gm);
        TrainingBudget budget = gm.GetTrainingBudget();
        _trainingBudgetLabel.Text = string.Format(Tr("ui.family.budget"), budget.Remaining, budget.Total);
    }

    /// <summary>The first shift from tomorrow on (the day the next plan ticks): "tomorrow" or the weekday, with its hours.</summary>
    private string NextShiftText(GameManager gm)
    {
        for (int offset = 1; offset <= 7; offset++)
        {
            long day = gm.State.CurrentDay + offset;
            int hours = gm.ParentJobs.ShiftHoursOn(day);
            if (hours > 0)
            {
                return offset == 1
                    ? string.Format(Tr("ui.family.job.next.tomorrow"), hours)
                    : string.Format(Tr("ui.family.job.next.day"), GameCalendar.NameOf(GameCalendar.WeekdayForDay(day)), hours);
            }
        }
        return Tr("ui.family.job.next.none");
    }

    // ------------------------------------------------------------------
    // The Family tab: per-child training
    // ------------------------------------------------------------------

    /// <summary>Called at the start of each Family tab rebuild: the previous rebuild's training controls are gone with their cards.</summary>
    private void ResetFamilyTraining()
    {
        _familyFocusRows.Clear();
        _trainButtons.Clear();
    }

    /// <summary>
    /// A child card's training block: the Train button with its reason, the standing-plan
    /// selectors (for a trainable child only) and the last result line. Selecting a plan
    /// writes it at once through the intent and rebuilds nothing, so focus stays put.
    /// </summary>
    private Control BuildTrainingControls(GameManager gm, in PlayerRow child)
    {
        string childId = child.PlayerId;
        var box = new VBoxContainer();

        TrainingBlock block = gm.CanTrainChild(childId);
        var train = new Button { Text = Tr("ui.family.train"), Disabled = block != TrainingBlock.None };
        train.Pressed += () => OpenBackyardDrill(childId);
        _trainButtons[childId] = train;
        var trainRow = new HBoxContainer();
        trainRow.AddChild(train);
        trainRow.AddChild(new Label
        {
            Text = block == TrainingBlock.None ? Tr("ui.family.train.ready") : BlockReasonText(block),
            ThemeTypeVariation = "CaptionLabel",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        box.AddChild(trainRow);
        var focusRow = new List<Control> { train };

        if (_trainableIds.Contains(childId))
        {
            TrainingPlan plan = gm.Training.GetPlan(childId);
            box.AddChild(new Label
            {
                Text = Tr("ui.family.plan.heading"),
                ThemeTypeVariation = "CaptionLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            OptionButton focusOption = NewPlanOption(
                new[]
                {
                    Tr("ui.family.plan.focus.hitting"),
                    Tr("ui.family.plan.focus.pitching"),
                    Tr("ui.family.plan.focus.fielding"),
                },
                (int)plan.Focus);
            OptionButton intensityOption = NewPlanOption(
                new[]
                {
                    Tr("ui.family.plan.intensity.easy"),
                    Tr("ui.family.plan.intensity.normal"),
                    Tr("ui.family.plan.intensity.hard"),
                },
                (int)plan.Intensity);
            var hardNote = new Label
            {
                Text = Tr("ui.family.plan.hard_note"),
                ThemeTypeVariation = "CaptionLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Visible = plan.Intensity == TrainingIntensity.Hard,
            };
            void OnPlanSelected(long _)
            {
                GameManager.Instance!.SetTrainingPlan(
                    childId, (TrainingFocus)focusOption.Selected, (TrainingIntensity)intensityOption.Selected);
                hardNote.Visible = intensityOption.Selected == (int)TrainingIntensity.Hard;
            }
            focusOption.ItemSelected += OnPlanSelected;
            intensityOption.ItemSelected += OnPlanSelected;

            var planRow = new HBoxContainer();
            planRow.AddChild(new Label { Text = Tr("ui.family.plan.focus"), CustomMinimumSize = new Vector2(60, 0) });
            planRow.AddChild(focusOption);
            planRow.AddChild(new Label { Text = Tr("ui.family.plan.intensity"), CustomMinimumSize = new Vector2(50, 0) });
            planRow.AddChild(intensityOption);
            box.AddChild(planRow);
            box.AddChild(hardNote);
            _familyFocusRows.Add(focusRow.ToArray());
            focusRow = new List<Control> { focusOption, intensityOption };
        }
        _familyFocusRows.Add(focusRow.ToArray());

        box.AddChild(new Label
        {
            Text = LastResultText(gm, childId),
            ThemeTypeVariation = "CaptionLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        return box;
    }

    private static OptionButton NewPlanOption(string[] items, int selected)
    {
        var option = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (string item in items)
        {
            option.AddItem(item);
        }
        option.Selected = selected;
        return option;
    }

    private string BlockReasonText(TrainingBlock block) => block switch
    {
        TrainingBlock.NoJob => Tr("ui.family.block.nojob"),
        TrainingBlock.NoSessionsLeft => Tr("ui.family.block.nosessions"),
        TrainingBlock.NotAChild => Tr("ui.family.block.notachild"),
        TrainingBlock.OutsideAges => Tr("ui.family.block.outsideages"),
        TrainingBlock.Rostered => Tr("ui.family.block.rostered"),
        _ => string.Empty,
    };

    private string LastResultText(GameManager gm, string childId)
    {
        if (!gm.Training.TryGetLastResult(childId, out TrainingSessionResult result))
        {
            return Tr("ui.family.last.none");
        }
        string focus = result.Focus switch
        {
            TrainingFocus.Hitting => Tr("ui.family.plan.focus.hitting"),
            TrainingFocus.Pitching => Tr("ui.family.plan.focus.pitching"),
            _ => Tr("ui.family.plan.focus.fielding"),
        };
        string intensity = result.Intensity switch
        {
            TrainingIntensity.Easy => Tr("ui.family.plan.intensity.easy"),
            TrainingIntensity.Normal => Tr("ui.family.plan.intensity.normal"),
            _ => Tr("ui.family.plan.intensity.hard"),
        };
        string key = result.Outcome.Success
            ? result.Autopilot ? "ui.family.last.success.auto" : "ui.family.last.success.drill"
            : result.Autopilot ? "ui.family.last.fail.auto" : "ui.family.last.fail.drill";
        return string.Format(Tr(key), focus, intensity);
    }

    /// <summary>
    /// Called at the end of each Family tab rebuild: links the training controls in reading order
    /// (left/right within a row, up/down between rows) and returns focus to the child the drill was
    /// just closed on, when that child's controls can still take it.
    /// </summary>
    private void LinkFamilyFocus()
    {
        for (int r = 0; r < _familyFocusRows.Count; r++)
        {
            Control[] row = _familyFocusRows[r];
            for (int c = 0; c < row.Length; c++)
            {
                if (c > 0)
                {
                    row[c].FocusNeighborLeft = row[c - 1].GetPath();
                }
                if (c < row.Length - 1)
                {
                    row[c].FocusNeighborRight = row[c + 1].GetPath();
                }
                if (r > 0)
                {
                    Control[] above = _familyFocusRows[r - 1];
                    row[c].FocusNeighborTop = above[Math.Min(c, above.Length - 1)].GetPath();
                }
                if (r < _familyFocusRows.Count - 1)
                {
                    Control[] below = _familyFocusRows[r + 1];
                    row[c].FocusNeighborBottom = below[Math.Min(c, below.Length - 1)].GetPath();
                }
            }
        }

        if (_drillFocusChildId is string childId)
        {
            _drillFocusChildId = null;
            if (_trainButtons.TryGetValue(childId, out Button? train) && !train.Disabled)
            {
                train.GrabFocus();
            }
        }
    }

    // ------------------------------------------------------------------
    // The drill
    // ------------------------------------------------------------------

    private void OpenBackyardDrill(string childId)
    {
        GameManager gm = GameManager.Instance!;
        if (_drillLayer is not null || !gm.Career.InParentPhase || gm.CanTrainChild(childId) != TrainingBlock.None)
        {
            return;
        }
        var drill = GD.Load<PackedScene>(BackyardDrillScenePath).Instantiate<BackyardDrill>();
        drill.Closed += OnDrillClosed;
        _drillLayer = new CanvasLayer { Layer = 60 };
        _drillLayer.AddChild(drill);
        AddChild(_drillLayer);
        drill.Begin(childId);
    }

    private void OnDrillClosed(string childId)
    {
        if (_drillLayer is null)
        {
            return; // a double close (Done and Cancel in one frame)
        }
        _drillLayer.QueueFree();
        _drillLayer = null;
        _drillFocusChildId = childId;
        _familyLoadedDay = long.MinValue; // the budget, the last result and the Train button's state are new
    }
}
