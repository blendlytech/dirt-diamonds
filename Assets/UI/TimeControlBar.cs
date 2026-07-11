using DirtAndDiamonds.Core;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Slice G-2 (real_time_clock_slice_g.md §3.2): the dashboard header's time
/// cluster and THE day-advance driver — replaces the old Play/Skip button
/// pair. Each frame it accumulates real time into the shared
/// <see cref="GameManager.TimeOfDay"/> clock and, on a midnight crossing,
/// calls the unchanged <see cref="TimeManager.AdvanceDay"/> exactly as a
/// Skip click does — the sim never learns whether a click or the clock
/// ticked the day. Every advance path consults
/// <see cref="GameManager.CanAdvanceDay"/> (§4.1) first, and the clock
/// auto-pauses — without touching the player's chosen Speed — while any
/// decision is owed, while a day's events are still settling, while an
/// attended game waits (§4.4: the contextual Play Game button surfaces
/// instead of the clock autopiloting past it), and while a hustle session
/// is unresolved. The latter two are SOFT stops: Skip Day stays enabled,
/// because skipping past them IS their designed autopilot/no-deal forfeit.
/// The at-bat machinery itself stays in <see cref="BaseballDashboard"/> —
/// this bar only emits <see cref="PlayGameRequested"/> /
/// <see cref="DaySettled"/> upward (ui_conventions: player-intent signals,
/// never reaching into a sibling tree). Node paths verified against
/// TimeControlBar.tscn (authored together with this script).
/// </summary>
public sealed partial class TimeControlBar : HBoxContainer
{
    /// <summary>The contextual Play press — a pending attended game is waiting and the player wants the at-bat. The dashboard owns the launch.</summary>
    [Signal]
    public delegate void PlayGameRequestedEventHandler();

    /// <summary>A day advance (Skip or midnight roll) has fully settled — the tick's bus fan-out drained, sims done. The dashboard refreshes its header off this.</summary>
    [Signal]
    public delegate void DaySettledEventHandler();

    [Export]
    public string PauseText { get; set; } = "Pause";

    [Export]
    public string ResumeText { get; set; } = "Resume";

    /// <summary>weekday3, month3, day-of-month, hour12, minute, am/pm.</summary>
    [Export]
    public string ReadoutFormat { get; set; } = "{0} {1} {2} · {3}:{4:D2} {5}";

    [Export]
    public string AmText { get; set; } = "AM";

    [Export]
    public string PmText { get; set; } = "PM";

    [Export]
    public string PausedText { get; set; } = "Paused";

    [Export]
    public string DecisionNeededText { get; set; } = "Paused — decision needed";

    [Export]
    public string GameReadyText { get; set; } = "Game day — play or skip";

    [Export]
    public string HustleWaitingText { get; set; } = "Paused — hustle session waiting";

    [Export]
    public string GameInProgressText { get; set; } = "Game in progress";

    private Button _playGameButton = null!;
    private Button _pauseButton = null!;
    private Button _slowButton = null!;
    private Button _normalButton = null!;
    private Button _fastButton = null!;
    private Button _skipDayButton = null!;
    private Label _readoutLabel = null!;
    private Label _statusLabel = null!;

    private GameClock _clock = null!;

    // The pace Resume returns to after a manual pause (§4.1) — tracked here,
    // not on the model, so auto-pauses never disturb it.
    private TimeSpeed _resumeSpeed = TimeSpeed.Normal;

    // This driver's transient in-progress-advance state (§4.1): an AdvanceDay
    // was issued and the tick's deferred events haven't drained yet. The
    // clock holds and Skip disables for the frame(s) it takes.
    private bool _advanceSettling;

    // Dirty flags (ui_conventions: no per-frame formatting/mutation).
    private long _shownDay = -1;
    private int _shownMinute = -1;
    private TimeSpeed _shownSpeed = (TimeSpeed)(-1);
    private BarFace _shownFace = (BarFace)(-1);

    /// <summary>The one line under the readout explaining why time is (or isn't) flowing.</summary>
    private enum BarFace
    {
        Flowing,
        Dormant, // no avatar — creation pending or lineage over
        ManualPaused,
        GameReady,
        GameInProgress,
        DecisionNeeded,
        HustleWaiting,
    }

    public override void _Ready()
    {
        // §3.2/§4.3: the controls stay responsive if any future modal pauses
        // the SceneTree — "pause" is only ever the GameClock flag, never a
        // tree pause.
        ProcessMode = ProcessModeEnum.Always;

        _playGameButton = GetNode<Button>("PlayGameButton");
        _pauseButton = GetNode<Button>("PauseButton");
        _slowButton = GetNode<Button>("SlowButton");
        _normalButton = GetNode<Button>("NormalButton");
        _fastButton = GetNode<Button>("FastButton");
        _skipDayButton = GetNode<Button>("SkipDayButton");
        _readoutLabel = GetNode<Label>("ReadoutBox/ReadoutLabel");
        _statusLabel = GetNode<Label>("ReadoutBox/StatusLabel");

        _clock = GameManager.Instance!.TimeOfDay;
        // A save persisted while manually paused resumes paused; Resume then
        // falls back to Normal rather than remembering across sessions.
        _resumeSpeed = _clock.IsPaused ? TimeSpeed.Normal : _clock.Speed;

        _playGameButton.Pressed += OnPlayGamePressed;
        _pauseButton.Pressed += OnPausePressed;
        _slowButton.Pressed += OnSlowPressed;
        _normalButton.Pressed += OnNormalPressed;
        _fastButton.Pressed += OnFastPressed;
        _skipDayButton.Pressed += OnSkipDayPressed;
    }

    public override void _ExitTree()
    {
        _playGameButton.Pressed -= OnPlayGamePressed;
        _pauseButton.Pressed -= OnPausePressed;
        _slowButton.Pressed -= OnSlowPressed;
        _normalButton.Pressed -= OnNormalPressed;
        _fastButton.Pressed -= OnFastPressed;
        _skipDayButton.Pressed -= OnSkipDayPressed;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;

        if (_advanceSettling && gm.Events.PendingCount == 0)
        {
            // The tick's fan-out has fully drained — a parked attended game
            // is now visible to the gates below, and the dashboard's header
            // refresh rides this signal (the old _refreshAfterDayTick drain).
            _advanceSettling = false;
            EmitSignal(SignalName.DaySettled);
        }

        bool hasAvatar = gm.Career.HasAvatar;
        bool gameReady = hasAvatar && gm.Career.HasPendingGame && !gm.InteractiveGameInFlight;
        bool canAdvance = hasAvatar && !_advanceSettling && gm.CanAdvanceDay;
        // §4.1/§4.4: time flows only while nothing is owed. gameReady and a
        // pending hustle hold the clock but leave Skip enabled — advancing
        // past them is their designed autopilot/no-deal forfeit.
        bool timeFlows = canAdvance && !gameReady && !gm.HasPendingHustleSession && !_clock.IsPaused;

        if (timeFlows)
        {
            _clock.Advance((float)delta, out bool crossedMidnight);
            if (crossedMidnight)
            {
                // The clock never autopilots the player past their own game
                // (§3.3): a midnight roll parks an attended day as pending,
                // exactly like the old Play click's advance did.
                gm.Career.AutopilotAttendedGames = false;
                gm.Clock.AdvanceDay();
                _clock.ResetToWake();
                _advanceSettling = true;
            }
        }

        RefreshReadout(gm);
        RefreshControls(gm, canAdvance, gameReady);
    }

    /// <summary>"TUE APR 14 · 2:47 PM" — reformats only when the displayed minute or the day actually changes.</summary>
    private void RefreshReadout(GameManager gm)
    {
        long day = gm.State.CurrentDay;
        int minute = _clock.MinuteOfDay;
        if (day == _shownDay && minute == _shownMinute)
        {
            return;
        }
        _shownDay = day;
        _shownMinute = minute;
        int dayOfSeason = GlobalState.DayOfSeasonForDay(day);
        CalendarDate date = GameCalendar.DateForDayOfSeason(dayOfSeason);
        Weekday weekday = GameCalendar.WeekdayForDayOfSeason(dayOfSeason);
        int hour = minute / 60;
        int hour12 = hour % 12 == 0 ? 12 : hour % 12;
        _readoutLabel.Text = string.Format(
            ReadoutFormat,
            GameCalendar.NameOf(weekday).Substring(0, 3).ToUpperInvariant(),
            GameCalendar.MonthName(date.Month).Substring(0, 3).ToUpperInvariant(),
            date.Day,
            hour12,
            minute % 60,
            hour < 12 ? AmText : PmText);
    }

    private void RefreshControls(GameManager gm, bool canAdvance, bool gameReady)
    {
        bool skipDisabled = !canAdvance;
        if (_skipDayButton.Disabled != skipDisabled)
        {
            _skipDayButton.Disabled = skipDisabled;
        }
        if (_playGameButton.Visible != gameReady)
        {
            _playGameButton.Visible = gameReady;
        }

        TimeSpeed speed = _clock.Speed;
        if (speed != _shownSpeed)
        {
            _shownSpeed = speed;
            _pauseButton.Text = _clock.IsPaused ? ResumeText : PauseText;
            _slowButton.SetPressedNoSignal(speed == TimeSpeed.Slow);
            _normalButton.SetPressedNoSignal(speed == TimeSpeed.Normal);
            _fastButton.SetPressedNoSignal(speed == TimeSpeed.Fast);
        }

        BarFace face = ComputeFace(gm, gameReady);
        if (face != _shownFace)
        {
            _shownFace = face;
            _statusLabel.Text = face switch
            {
                BarFace.ManualPaused => PausedText,
                BarFace.GameReady => GameReadyText,
                BarFace.GameInProgress => GameInProgressText,
                BarFace.DecisionNeeded => DecisionNeededText,
                BarFace.HustleWaiting => HustleWaitingText,
                _ => string.Empty,
            };
            _statusLabel.Visible = face is not (BarFace.Flowing or BarFace.Dormant);
        }
    }

    private BarFace ComputeFace(GameManager gm, bool gameReady)
    {
        if (!gm.Career.HasAvatar)
        {
            return BarFace.Dormant;
        }
        if (gm.InteractiveGameInFlight)
        {
            return BarFace.GameInProgress;
        }
        if (gameReady)
        {
            return BarFace.GameReady;
        }
        if (gm.GrittyEventChoices.HasPendingChoice || gm.Career.HasPendingSuccessionChoice)
        {
            return BarFace.DecisionNeeded;
        }
        if (gm.HasPendingHustleSession)
        {
            return BarFace.HustleWaiting;
        }
        return _clock.IsPaused ? BarFace.ManualPaused : BarFace.Flowing;
    }

    private void OnPlayGamePressed()
    {
        GameManager gm = GameManager.Instance!;
        // Stale-press guard: the game could have just autopiloted away.
        if (gm.Career.HasAvatar && gm.Career.HasPendingGame && !gm.InteractiveGameInFlight)
        {
            EmitSignal(SignalName.PlayGameRequested);
        }
    }

    private void OnSkipDayPressed()
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar || _advanceSettling || !gm.CanAdvanceDay)
        {
            return; // stale click; the per-frame disable catches up next frame
        }
        // Today's-Skip semantics verbatim (§3.3): autopilot any attended
        // game, jump to tomorrow morning — the instant strategic step, and
        // the designed forfeit for an unplayed pending game or hustle.
        gm.Career.AutopilotAttendedGames = true;
        gm.Clock.AdvanceDay();
        _clock.ResetToWake();
        _advanceSettling = true;
    }

    private void OnPausePressed()
    {
        if (_clock.IsPaused)
        {
            _clock.Speed = _resumeSpeed;
        }
        else
        {
            _resumeSpeed = _clock.Speed;
            _clock.Speed = TimeSpeed.Paused;
        }
        _shownSpeed = (TimeSpeed)(-1); // force the button-state refresh
        // §3.4: manual pause/speed changes are checkpoint writes — cheap,
        // user-driven, never per frame.
        GameManager.Instance!.PersistTimeOfDay();
    }

    private void OnSlowPressed() => SetSpeed(TimeSpeed.Slow);

    private void OnNormalPressed() => SetSpeed(TimeSpeed.Normal);

    private void OnFastPressed() => SetSpeed(TimeSpeed.Fast);

    private void SetSpeed(TimeSpeed speed)
    {
        _resumeSpeed = speed;
        _clock.Speed = speed;
        // Re-pressing the active toggle un-presses it visually — dirtying the
        // shown state makes the refresh reassert the true one either way.
        _shownSpeed = (TimeSpeed)(-1);
        GameManager.Instance!.PersistTimeOfDay();
    }
}
