using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Life;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// The "Plan Today" card in the dashboard's card row (12b — previously a
/// corner-anchored overlay under Main, which floated over the phone; it now
/// occupies a real container slot in BaseballDashboard.tscn so nothing ever
/// overlaps it). Phase 9b's UI half: lets the player plan the avatar's next
/// day across the six <see cref="DaySchedule"/> blocks (HS-4 added the
/// free-time activity picker to the original five) and submit it via
/// <see cref="LifeSimManager.SetTodaySchedule"/>. Not modal like
/// SuccessionScreen (or the Burner Phone's pending-choice thread) — a plan is optional (an unset plan
/// autopilots exactly as before 9b), so this never joins BaseballDashboard's
/// day-advance gate. School has no slider at all — its hours are
/// calendar-forced (<see cref="GameManager.GetMandatoryBlocksFor"/>), so the
/// card shows a read-only mandated-hours note and folds those hours into the
/// free-hours budget; the Game row is hidden — and its slider zeroed —
/// whenever it isn't schedulable
/// (<see cref="CareerManager.TryGetPendingGame"/>), so a stale hour never
/// rides along silently after a tier change or an offseason day. Playing an
/// attended game itself stays BaseballDashboard's flow — the Game slider
/// only reserves the hours, it never launches the at-bat view. HS-4 also adds
/// two read-only context lines above the blocks: the day's Travel cost
/// (<see cref="TravelTime.ComputeHours"/>, revised §5.3 — one round trip per
/// distinct place the current slider values leave home for, discounted by
/// <see cref="LifeSimManager.AvatarTransportHoursSaved"/> and folded into the
/// same free-hours budget School's mandated note already occupies) and the
/// avatar's GPA/Intelligence/Discipline/Happiness readout
/// (<see cref="LifeSimManager.TryGetPersonStats"/>) — neither is editable
/// here, they're context for the plan the player is about to make. Node paths
/// verified against ScheduleScreen.tscn before this script was written.
/// </summary>
public sealed partial class ScheduleScreen : PanelContainer
{
    [Export]
    public string PlanSetFormat { get; set; } =
        "Plan set — Sleep {0}h, School {1}h, Practice {2}h, Game {3}h, Work {4}h, {5} {6}h, Family {8}h, Travel {9}h, Free {7}h.";

    [Export]
    public string NoPlanText { get; set; } = "No plan set — today runs on autopilot.";

    [Export]
    public string FreeHoursFormat { get; set; } = "Free hours: {0}";

    [Export]
    public string OverAllocatedFormat { get; set; } = "Over by {0}h — reduce a block before confirming.";

    [Export]
    public string JailLockFormat { get; set; } =
        "In custody — day planning is locked until day {0}. Today runs on autopilot.";

    [Export]
    public string TravelCostFormat { get; set; } = "Travel today: {0}h across {1} trip(s).";

    [Export]
    public string PersonStatsFormat { get; set; } =
        "GPA {0:F2}  •  Intelligence {1}  Discipline {2}  Happiness {3}";

    [Export]
    public string SchoolMandatedFormat { get; set; } = "School today: {0}h (mandatory)";

    private Control _gameRow = null!;
    private Control _familyRow = null!;

    private HSlider _sleepSlider = null!;
    private Label _sleepValueLabel = null!;
    private Label _schoolNoteLabel = null!;
    private HSlider _practiceSlider = null!;
    private Label _practiceValueLabel = null!;
    private HSlider _gameSlider = null!;
    private Label _gameValueLabel = null!;
    private HSlider _workSlider = null!;
    private Label _workValueLabel = null!;
    private OptionButton _workActivityOption = null!;
    private HSlider _freeTimeSlider = null!;
    private Label _freeTimeValueLabel = null!;
    private OptionButton _freeTimeActivityOption = null!;
    private HSlider _familySlider = null!;
    private Label _familyValueLabel = null!;

    private Label _planStatusLabel = null!;
    private Label _lockLabel = null!;
    private Label _personStatsLabel = null!;
    private Label _transportLabel = null!;
    private Label _freeHoursLabel = null!;
    private Label _errorLabel = null!;
    private Button _confirmButton = null!;
    private Button _clearButton = null!;

    // HS-4 free-time block (person-layer doc §2.1's Epic 4 activities): the
    // DaySchedule struct forces FreeTimeActivity back to Idle whenever hours
    // are 0 (see its constructor), so the option's selected index only ever
    // matters when the slider is above 0 — no extra gating needed here.
    private static readonly NpcActionId[] FreeTimeActivities =
    {
        NpcActionId.Church, NpcActionId.VideoGames, NpcActionId.Study, NpcActionId.Hangout,
    };

    private static string FreeTimeActivityLabel(NpcActionId id) => id switch
    {
        NpcActionId.Church => "Church",
        NpcActionId.VideoGames => "Video Games",
        NpcActionId.Study => "Study",
        NpcActionId.Hangout => "Hangout",
        _ => "Idle",
    };

    // Dirty-flag identity for the plan-status label (ui_conventions.md: no
    // per-frame string formatting) — reformatted only when the polled plan
    // actually differs from what's already on screen.
    private bool _shownHasPlan;
    private DaySchedule _shownPlan;

    // Same dirty-flag discipline for the jail-lock label (8c).
    private bool _shownLocked;
    private long _shownLockUntilDay;

    // HS-4: the Travel-cost readout depends on the avatar's owned transport,
    // which can change out from under an open screen (a marketplace buy) —
    // sentinel so the very first _Process call always formats once (a real
    // hours-saved value is never negative).
    private float _shownTransportHoursSaved = -1f;

    // HS-4: the GPA/person-stat readout — same sentinel discipline.
    // Intelligence/Discipline/Happiness are displayed rounded to whole points
    // (the underlying floats carry the HS-4 effect channel's fractional
    // remainder between days), so the dirty check compares the ROUNDED value,
    // not the raw float, to avoid reformatting every frame over sub-point churn.
    private bool _shownHasPersonStats;
    private double _shownGpa = double.MinValue;
    private int _shownIntelligence = int.MinValue;
    private int _shownDiscipline = int.MinValue;
    private int _shownHappiness = int.MinValue;

    // Set each frame by RefreshHoursLabels; combined with the jail lock to
    // drive ConfirmButton's disabled state every frame in _Process (the lock
    // can flip true/false with no slider change involved).
    private bool _overAllocated;

    // Calendar-mandated school hours for the day this plan will tick —
    // school has no slider, so this is what RefreshHoursLabels folds into
    // the free-hours budget and OnConfirmPressed submits as SchoolHours
    // (SubmitDaySchedule re-forces it server-side either way).
    private int _mandatedSchoolHours;

    public override void _Ready()
    {
        _gameRow = GetNode<Control>("Layout/GameRow");
        _familyRow = GetNode<Control>("Layout/FamilyRow");

        _sleepSlider = GetNode<HSlider>("Layout/SleepRow/SleepSlider");
        _sleepValueLabel = GetNode<Label>("Layout/SleepRow/SleepValueLabel");
        _schoolNoteLabel = GetNode<Label>("Layout/SchoolNoteLabel");
        _practiceSlider = GetNode<HSlider>("Layout/PracticeRow/PracticeSlider");
        _practiceValueLabel = GetNode<Label>("Layout/PracticeRow/PracticeValueLabel");
        _gameSlider = GetNode<HSlider>("Layout/GameRow/GameSlider");
        _gameValueLabel = GetNode<Label>("Layout/GameRow/GameValueLabel");
        _workSlider = GetNode<HSlider>("Layout/WorkRow/WorkSlider");
        _workValueLabel = GetNode<Label>("Layout/WorkRow/WorkValueLabel");
        _workActivityOption = GetNode<OptionButton>("Layout/WorkActivityRow/WorkActivityOption");
        _freeTimeSlider = GetNode<HSlider>("Layout/FreeTimeRow/FreeTimeSlider");
        _freeTimeValueLabel = GetNode<Label>("Layout/FreeTimeRow/FreeTimeValueLabel");
        _freeTimeActivityOption = GetNode<OptionButton>("Layout/FreeTimeActivityRow/FreeTimeActivityOption");
        _familySlider = GetNode<HSlider>("Layout/FamilyRow/FamilySlider");
        _familyValueLabel = GetNode<Label>("Layout/FamilyRow/FamilyValueLabel");

        _planStatusLabel = GetNode<Label>("Layout/PlanStatusLabel");
        _lockLabel = GetNode<Label>("Layout/LockLabel");
        _personStatsLabel = GetNode<Label>("Layout/PersonStatsLabel");
        _transportLabel = GetNode<Label>("Layout/TransportLabel");
        _freeHoursLabel = GetNode<Label>("Layout/FreeHoursLabel");
        _errorLabel = GetNode<Label>("Layout/ErrorLabel");
        _confirmButton = GetNode<Button>("Layout/ButtonsRow/ConfirmButton");
        _clearButton = GetNode<Button>("Layout/ButtonsRow/ClearButton");

        _sleepSlider.ValueChanged += _ => RefreshHoursLabels();
        _practiceSlider.ValueChanged += _ => RefreshHoursLabels();
        _gameSlider.ValueChanged += _ => RefreshHoursLabels();
        _workSlider.ValueChanged += _ => RefreshHoursLabels();
        _freeTimeSlider.ValueChanged += _ => RefreshHoursLabels();
        _familySlider.ValueChanged += _ => RefreshHoursLabels();
        _confirmButton.Pressed += OnConfirmPressed;
        _clearButton.Pressed += OnClearPressed;

        RefreshHoursLabels();
    }

    public override void _ExitTree()
    {
        _confirmButton.Pressed -= OnConfirmPressed;
        _clearButton.Pressed -= OnClearPressed;
    }

    /// <summary>
    /// 10d Bank-tab seam: pre-fills "Work as" from a phone launch button. Pure
    /// UI convenience — the player still has to confirm the plan and advance
    /// the day, same as picking the dropdown by hand.
    /// </summary>
    public void SelectWorkActivity(WorkActivity activity) => _workActivityOption.Selected = (int)activity;

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

        // Calendar-mandated blocks for the day this plan will tick
        // (CurrentDay + 1 — SubmitDaySchedule's own convention; it re-forces
        // these server-side, so the sliders here are display, not authority).
        MandatoryBlocks blocks = GameManager.Instance!.GetMandatoryBlocksFor(
            GameManager.Instance!.State.CurrentDay + 1);

        // School is not a slider (calendar-forced hours) — a read-only note
        // plus the fold into the free-hours budget, refreshed only when the
        // mandated value actually moves.
        int schoolTarget = lifeSim.AvatarSchoolAvailable ? blocks.SchoolHours : 0;
        if (schoolTarget != _mandatedSchoolHours)
        {
            _mandatedSchoolHours = schoolTarget;
            _schoolNoteLabel.Visible = schoolTarget > 0;
            if (schoolTarget > 0)
            {
                _schoolNoteLabel.Text = string.Format(SchoolMandatedFormat, schoolTarget);
            }
            RefreshHoursLabels();
        }

        if (blocks.PracticeGameLocked)
        {
            // HS: team practice and games land on HsSeasonCalendar's days —
            // the rows show what the calendar mandates (0 = off day) and the
            // Game row appears only on game days.
            _practiceSlider.Editable = false;
            if (_practiceSlider.Value != blocks.PracticeHours)
            {
                _practiceSlider.Value = blocks.PracticeHours;
            }
            _gameRow.Visible = blocks.GameHours > 0;
            _gameSlider.Editable = false;
            if (_gameSlider.Value != blocks.GameHours)
            {
                _gameSlider.Value = blocks.GameHours;
            }
        }
        else
        {
            // Legacy (College/pro this pass): Practice is a free slider and
            // Game reserves hours only when an attended game is pending.
            _practiceSlider.Editable = true;
            _gameSlider.Editable = true;
            bool hasPendingGame = career.TryGetPendingGame(out _);
            _gameRow.Visible = hasPendingGame;
            if (!hasPendingGame && _gameSlider.Value != 0)
            {
                _gameSlider.Value = 0;
            }
        }

        // HS-5 §7.1: the Family row only makes sense once the avatar has a
        // child — GameManager caches this (avatar-sync + ChildBornEvent), so
        // this poll never touches the DB.
        bool hasChildren = GameManager.Instance!.AvatarHasChildren;
        _familyRow.Visible = hasChildren;
        if (!hasChildren && _familySlider.Value != 0)
        {
            _familySlider.Value = 0;
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
                    plan.GameHours, plan.WorkHours, FreeTimeActivityLabel(plan.FreeTimeActivity),
                    plan.FreeTimeHours, plan.FreeHours, plan.FamilyHours, plan.TravelHours)
                : NoPlanText;
        }

        // HS-4 §5.3 (revised): the Travel-cost readout tracks the avatar's
        // best owned transport — a marketplace buy while this screen is open
        // must recompute the live preview even with no slider touched, so
        // RefreshHoursLabels (which reads the sliders + this rate together)
        // reruns whenever the rate itself moves.
        float transportSaved = lifeSim.AvatarTransportHoursSaved;
        if (transportSaved != _shownTransportHoursSaved)
        {
            _shownTransportHoursSaved = transportSaved;
            RefreshHoursLabels();
        }

        // HS-4 §2.1/§2.2: the first GPA/person-stat readout. No-op (label left
        // as last shown) for the rare frame before the avatar's person row is
        // hydrated — SyncLifeSimAvatar runs synchronously on avatar creation,
        // so this only ever matters for one boot-time frame at most.
        if (lifeSim.TryGetPersonStats(career.AvatarPlayerId, out PersonStats stats))
        {
            int intelligence = (int)MathF.Round(stats.Intelligence);
            int discipline = (int)MathF.Round(stats.Discipline);
            int happiness = (int)MathF.Round(stats.Happiness);
            if (!_shownHasPersonStats || stats.Gpa != _shownGpa || intelligence != _shownIntelligence
                || discipline != _shownDiscipline || happiness != _shownHappiness)
            {
                _shownHasPersonStats = true;
                _shownGpa = stats.Gpa;
                _shownIntelligence = intelligence;
                _shownDiscipline = discipline;
                _shownHappiness = happiness;
                _personStatsLabel.Text = string.Format(
                    PersonStatsFormat, stats.Gpa, intelligence, discipline, happiness);
            }
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
        && a.WorkHours == b.WorkHours && a.FreeTimeHours == b.FreeTimeHours
        && a.FreeTimeActivity == b.FreeTimeActivity && a.FamilyHours == b.FamilyHours
        && a.TravelHours == b.TravelHours;

    /// <summary>
    /// The live Travel-cost preview for the sliders' current values — the
    /// same formula <see cref="GameManager.SubmitDaySchedule"/> re-forces
    /// server-side, so this preview and the confirmed plan never disagree.
    /// </summary>
    private int ComputeTravelHours(out int trips) => TravelTime.ComputeHours(
        _mandatedSchoolHours, (int)_practiceSlider.Value, (int)_gameSlider.Value, (int)_workSlider.Value,
        (int)_freeTimeSlider.Value, FreeTimeActivities[_freeTimeActivityOption.Selected],
        Math.Max(_shownTransportHoursSaved, 0f), out trips);

    private void RefreshHoursLabels()
    {
        _sleepValueLabel.Text = ((int)_sleepSlider.Value).ToString();
        _practiceValueLabel.Text = ((int)_practiceSlider.Value).ToString();
        _gameValueLabel.Text = ((int)_gameSlider.Value).ToString();
        _workValueLabel.Text = ((int)_workSlider.Value).ToString();
        _freeTimeValueLabel.Text = ((int)_freeTimeSlider.Value).ToString();
        _familyValueLabel.Text = ((int)_familySlider.Value).ToString();

        int travelHours = ComputeTravelHours(out int trips);
        _transportLabel.Visible = trips > 0;
        if (trips > 0)
        {
            _transportLabel.Text = string.Format(TravelCostFormat, travelHours, trips);
        }

        // Mandated school hours count against the day even without a slider —
        // otherwise a school day's budget would let the player silently
        // over-allocate the six hours the calendar already spent. Travel
        // (§5.3, revised) competes for the same 24 hours the same way.
        int total = (int)_sleepSlider.Value + _mandatedSchoolHours + (int)_practiceSlider.Value
            + (int)_gameSlider.Value + (int)_workSlider.Value + (int)_freeTimeSlider.Value
            + (int)_familySlider.Value + travelHours;
        int free = DaySchedule.HoursPerDay - total;
        bool overAllocated = free < 0;
        _freeHoursLabel.Text = string.Format(overAllocated ? OverAllocatedFormat : FreeHoursFormat, Math.Abs(free));
        _overAllocated = overAllocated;
    }

    private void OnConfirmPressed()
    {
        int travelHours = ComputeTravelHours(out _);
        var schedule = new DaySchedule(
            (int)_sleepSlider.Value, _mandatedSchoolHours, (int)_practiceSlider.Value,
            (int)_gameSlider.Value, (int)_workSlider.Value, (int)_freeTimeSlider.Value,
            FreeTimeActivities[_freeTimeActivityOption.Selected], (int)_familySlider.Value, travelHours);
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
