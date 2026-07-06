using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Life;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (a permanent sibling of the swappable screen under
/// Main, per Main.tscn — declared last so it draws above every other overlay)
/// that is Phase 9b's UI half: lets the player plan the avatar's next day
/// across the five <see cref="DaySchedule"/> blocks and submit it via
/// <see cref="LifeSimManager.SetTodaySchedule"/>. Not modal like
/// EventChoiceScreen/SuccessionScreen — a plan is optional (an unset plan
/// autopilots exactly as before 9b), so this never joins BaseballDashboard's
/// day-advance gate. The School and Game rows are hidden — and their sliders
/// zeroed — whenever they aren't schedulable
/// (<see cref="LifeSimManager.AvatarSchoolAvailable"/>,
/// <see cref="CareerManager.TryGetPendingGame"/>), so a stale hour never
/// rides along silently after a tier change or an offseason day. Playing an
/// attended game itself stays BaseballDashboard's flow — the Game slider
/// only reserves the hours, it never launches the at-bat view. Node paths
/// verified against ScheduleScreen.tscn before this script was written.
/// </summary>
public sealed partial class ScheduleScreen : Control
{
    [Export]
    public string PlanSetFormat { get; set; } =
        "Plan set — Sleep {0}h, School {1}h, Practice {2}h, Game {3}h, Work {4}h, Free {5}h.";

    [Export]
    public string NoPlanText { get; set; } = "No plan set — today runs on autopilot.";

    [Export]
    public string FreeHoursFormat { get; set; } = "Free hours: {0}";

    [Export]
    public string OverAllocatedFormat { get; set; } = "Over by {0}h — reduce a block before confirming.";

    [Export]
    public string JailLockFormat { get; set; } =
        "In custody — day planning is locked until day {0}. Today runs on autopilot.";

    private Control _schoolRow = null!;
    private Control _gameRow = null!;

    private HSlider _sleepSlider = null!;
    private Label _sleepValueLabel = null!;
    private HSlider _schoolSlider = null!;
    private Label _schoolValueLabel = null!;
    private HSlider _practiceSlider = null!;
    private Label _practiceValueLabel = null!;
    private HSlider _gameSlider = null!;
    private Label _gameValueLabel = null!;
    private HSlider _workSlider = null!;
    private Label _workValueLabel = null!;
    private OptionButton _workActivityOption = null!;

    private Label _planStatusLabel = null!;
    private Label _lockLabel = null!;
    private Label _freeHoursLabel = null!;
    private Label _errorLabel = null!;
    private Button _confirmButton = null!;
    private Button _clearButton = null!;

    // Dirty-flag identity for the plan-status label (ui_conventions.md: no
    // per-frame string formatting) — reformatted only when the polled plan
    // actually differs from what's already on screen.
    private bool _shownHasPlan;
    private DaySchedule _shownPlan;

    // Same dirty-flag discipline for the jail-lock label (8c).
    private bool _shownLocked;
    private long _shownLockUntilDay;

    // Set each frame by RefreshHoursLabels; combined with the jail lock to
    // drive ConfirmButton's disabled state every frame in _Process (the lock
    // can flip true/false with no slider change involved).
    private bool _overAllocated;

    public override void _Ready()
    {
        _schoolRow = GetNode<Control>("Panel/Layout/SchoolRow");
        _gameRow = GetNode<Control>("Panel/Layout/GameRow");

        _sleepSlider = GetNode<HSlider>("Panel/Layout/SleepRow/SleepSlider");
        _sleepValueLabel = GetNode<Label>("Panel/Layout/SleepRow/SleepValueLabel");
        _schoolSlider = GetNode<HSlider>("Panel/Layout/SchoolRow/SchoolSlider");
        _schoolValueLabel = GetNode<Label>("Panel/Layout/SchoolRow/SchoolValueLabel");
        _practiceSlider = GetNode<HSlider>("Panel/Layout/PracticeRow/PracticeSlider");
        _practiceValueLabel = GetNode<Label>("Panel/Layout/PracticeRow/PracticeValueLabel");
        _gameSlider = GetNode<HSlider>("Panel/Layout/GameRow/GameSlider");
        _gameValueLabel = GetNode<Label>("Panel/Layout/GameRow/GameValueLabel");
        _workSlider = GetNode<HSlider>("Panel/Layout/WorkRow/WorkSlider");
        _workValueLabel = GetNode<Label>("Panel/Layout/WorkRow/WorkValueLabel");
        _workActivityOption = GetNode<OptionButton>("Panel/Layout/WorkActivityRow/WorkActivityOption");

        _planStatusLabel = GetNode<Label>("Panel/Layout/PlanStatusLabel");
        _lockLabel = GetNode<Label>("Panel/Layout/LockLabel");
        _freeHoursLabel = GetNode<Label>("Panel/Layout/FreeHoursLabel");
        _errorLabel = GetNode<Label>("Panel/Layout/ErrorLabel");
        _confirmButton = GetNode<Button>("Panel/Layout/ButtonsRow/ConfirmButton");
        _clearButton = GetNode<Button>("Panel/Layout/ButtonsRow/ClearButton");

        _sleepSlider.ValueChanged += _ => RefreshHoursLabels();
        _schoolSlider.ValueChanged += _ => RefreshHoursLabels();
        _practiceSlider.ValueChanged += _ => RefreshHoursLabels();
        _gameSlider.ValueChanged += _ => RefreshHoursLabels();
        _workSlider.ValueChanged += _ => RefreshHoursLabels();
        _confirmButton.Pressed += OnConfirmPressed;
        _clearButton.Pressed += OnClearPressed;

        RefreshHoursLabels();
    }

    public override void _ExitTree()
    {
        _confirmButton.Pressed -= OnConfirmPressed;
        _clearButton.Pressed -= OnClearPressed;
    }

    public override void _Process(double delta)
    {
        CareerManager career = GameManager.Instance!.Career;
        if (!career.HasAvatar)
        {
            Visible = false;
            return;
        }
        Visible = true;
        LifeSimManager lifeSim = GameManager.Instance!.LifeSim;

        bool schoolAvailable = lifeSim.AvatarSchoolAvailable;
        _schoolRow.Visible = schoolAvailable;
        if (!schoolAvailable && _schoolSlider.Value != 0)
        {
            _schoolSlider.Value = 0; // fires ValueChanged -> RefreshHoursLabels
        }

        bool hasPendingGame = career.TryGetPendingGame(out _);
        _gameRow.Visible = hasPendingGame;
        if (!hasPendingGame && _gameSlider.Value != 0)
        {
            _gameSlider.Value = 0;
        }

        bool hasPlan = lifeSim.TryGetTodaySchedule(out DaySchedule plan);
        bool identical = hasPlan == _shownHasPlan && (!hasPlan || SchedulesEqual(plan, _shownPlan));
        if (!identical)
        {
            _shownHasPlan = hasPlan;
            _shownPlan = plan;
            _planStatusLabel.Text = hasPlan
                ? string.Format(
                    PlanSetFormat, plan.SleepHours, plan.SchoolHours, plan.PracticeHours,
                    plan.GameHours, plan.WorkHours, plan.FreeHours)
                : NoPlanText;
        }

        // 8c: an arrest locks the whole day plan (jail autopilots) —
        // AvatarScheduleLocked is only ever true for AbsenceReason.Arrest, per
        // GameManager's own contract. SubmitDaySchedule already enforces this
        // server-side; this is belt-and-suspenders UI feedback so a locked
        // player isn't left guessing why Confirm won't take.
        bool locked = GameManager.Instance!.AvatarScheduleLocked;
        long lockUntilDay = locked
            && GameManager.Instance!.Absences.TryGet(career.AvatarPlayerId, out AbsenceEntry lockEntry)
                ? lockEntry.UntilDay
                : 0;
        if (locked != _shownLocked || lockUntilDay != _shownLockUntilDay)
        {
            _shownLocked = locked;
            _shownLockUntilDay = lockUntilDay;
            _lockLabel.Visible = locked;
            if (locked)
            {
                _lockLabel.Text = string.Format(JailLockFormat, lockUntilDay);
            }
        }
        _confirmButton.Disabled = _overAllocated || locked;
    }

    private static bool SchedulesEqual(in DaySchedule a, in DaySchedule b) =>
        a.SleepHours == b.SleepHours && a.SchoolHours == b.SchoolHours
        && a.PracticeHours == b.PracticeHours && a.GameHours == b.GameHours
        && a.WorkHours == b.WorkHours;

    private void RefreshHoursLabels()
    {
        _sleepValueLabel.Text = ((int)_sleepSlider.Value).ToString();
        _schoolValueLabel.Text = ((int)_schoolSlider.Value).ToString();
        _practiceValueLabel.Text = ((int)_practiceSlider.Value).ToString();
        _gameValueLabel.Text = ((int)_gameSlider.Value).ToString();
        _workValueLabel.Text = ((int)_workSlider.Value).ToString();

        int total = (int)_sleepSlider.Value + (int)_schoolSlider.Value + (int)_practiceSlider.Value
            + (int)_gameSlider.Value + (int)_workSlider.Value;
        int free = DaySchedule.HoursPerDay - total;
        bool overAllocated = free < 0;
        _freeHoursLabel.Text = string.Format(overAllocated ? OverAllocatedFormat : FreeHoursFormat, Math.Abs(free));
        _overAllocated = overAllocated;
    }

    private void OnConfirmPressed()
    {
        var schedule = new DaySchedule(
            (int)_sleepSlider.Value, (int)_schoolSlider.Value, (int)_practiceSlider.Value,
            (int)_gameSlider.Value, (int)_workSlider.Value);
        var workActivity = (WorkActivity)_workActivityOption.Selected;
        try
        {
            GameManager.Instance!.SubmitDaySchedule(schedule, workActivity);
            _errorLabel.Visible = false;
        }
        catch (Exception ex)
        {
            _errorLabel.Text = ex.Message;
            _errorLabel.Visible = true;
        }
    }

    private void OnClearPressed()
    {
        GameManager.Instance!.ClearDaySchedule();
        _errorLabel.Visible = false;
    }
}
