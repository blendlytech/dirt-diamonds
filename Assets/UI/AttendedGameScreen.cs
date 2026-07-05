using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Phase 5 thin slice: the screen that owns one attended game day. It bridges
/// the Godot main thread and the sim task through a
/// <see cref="PlayerIntentBridge"/> — the game runs on a background task
/// (never the UI thread), the view renders snapshot DTOs, and the player's
/// swing/take intent flows back through the bridge. This driver polls cheap
/// dirty flags in _Process; the labels themselves update only when a snapshot
/// actually changed (AtBatView.Render is called on change, per ui_conventions).
/// Node paths verified against AttendedGameScreen.tscn via godot_scene_mapper
/// before this script was written.
/// </summary>
public sealed partial class AttendedGameScreen : Control
{
    // Player-facing text templates live on exported properties so the scene
    // (not compiled code) is the editing surface, per ui_conventions.
    [Export]
    public string DayFormat { get; set; } = "Day {0} — Season {1}";

    [Export]
    public string NoGameText { get; set; } = "No game today.";

    [Export]
    public string GameRunningText { get; set; } = "Game in progress — swing or take when the count shows.";

    [Export]
    public string FinalFormat { get; set; } = "Final: away {0} — home {1} ({2} innings). You: {3} PA.";

    [Export]
    public string PaLineFormat { get; set; } = "Your PA: {0}";

    [Export]
    public string NpcPaLineFormat { get; set; } = "{0}: {1}";

    [Export]
    public string NpcRivalryPaLineFormat { get; set; } = "RIVAL {0}: {1}";

    /// <summary>Comma-separated player-facing names for the 7 PaOutcome values, in enum order.</summary>
    [Export]
    public string OutcomeNamesCsv { get; set; } = "Out,Strikeout,Walk,Single,Double,Triple,Home run";

    // Phase 8c roster availability — read straight off GameManager.Absences,
    // independent of the Play/Skip click flow, so a player clicking Skip Day
    // through a whole absence still sees why there's no at-bat view.
    [Export]
    public string AbsentStatusFormat { get; set; } = "{0} — back on day {1}.";

    [Export]
    public string RustyStatusFormat { get; set; } = "{0}, still rusty (-{1} rating) until day {2}.";

    [Export]
    public string InjuryReasonText { get; set; } = "Sidelined by injury";

    [Export]
    public string SuspensionReasonText { get; set; } = "Serving a suspension";

    [Export]
    public string ArrestReasonText { get; set; } = "In custody";

    private Label _dayLabel = null!;
    private Button _playGameButton = null!;
    private Button _skipDayButton = null!;
    private Label _statusLabel = null!;
    private Label _availabilityLabel = null!;
    private AtBatView _atBatView = null!;

    private readonly PlayerIntentBridge _bridge = new();
    private Task<MicroGameResult>? _gameTask;
    private string[] _outcomeNames = Array.Empty<string>();
    private bool _awaitingPendingGame;

    // Dirty-flag identity for the availability label (ui_conventions.md: no
    // per-frame string formatting).
    private SlotAvailability _shownAvailability = SlotAvailability.Available;
    private AbsenceReason _shownAvailabilityReason;
    private long _shownAvailabilityUntilDay;
    private long _shownAvailabilityPenaltyUntilDay;

    public override void _Ready()
    {
        _dayLabel = GetNode<Label>("Screen/TopBar/DayLabel");
        _playGameButton = GetNode<Button>("Screen/TopBar/PlayGameButton");
        _skipDayButton = GetNode<Button>("Screen/TopBar/SkipDayButton");
        _statusLabel = GetNode<Label>("Screen/TopBar/StatusLabel");
        _availabilityLabel = GetNode<Label>("Screen/AvailabilityLabel");
        _atBatView = GetNode<AtBatView>("Screen/AtBatView");

        _playGameButton.Pressed += OnPlayGamePressed;
        _skipDayButton.Pressed += OnSkipDayPressed;
        _atBatView.SwingCommitted += OnSwingCommitted;
        _atBatView.TakeCommitted += OnTakeCommitted;

        _outcomeNames = OutcomeNamesCsv.Split(',');
        RefreshDayLabel();
        RefreshDayControlsEnabled();
    }

    public override void _ExitTree()
    {
        // Aborts any in-flight game; it unwinds unflushed and the career
        // forfeits it to the autopilot on the next day tick.
        _bridge.Cancel();
        _playGameButton.Pressed -= OnPlayGamePressed;
        _skipDayButton.Pressed -= OnSkipDayPressed;
        _atBatView.SwingCommitted -= OnSwingCommitted;
        _atBatView.TakeCommitted -= OnTakeCommitted;
    }

    public override void _Process(double delta)
    {
        CareerManager career = GameManager.Instance!.Career;
        RefreshAvailabilityLabel(career);

        // The day tick's events dispatch on GameManager._Process (earlier this
        // frame, autoloads process first), so a requested game shows up here.
        if (_awaitingPendingGame && GameManager.Instance!.Events.PendingCount == 0)
        {
            _awaitingPendingGame = false;
            if (career.HasPendingGame)
            {
                StartInteractiveGame(career);
            }
            else
            {
                _statusLabel.Text = NoGameText; // offseason day
            }
            RefreshDayLabel();
        }

        if (_gameTask is null)
        {
            RefreshDayControlsEnabled();
            return;
        }

        if (_bridge.TryGetSnapshot(out AtBatSnapshot snapshot))
        {
            _atBatView.Render(new AtBatViewState(
                snapshot.Context.AwayScore, snapshot.Context.HomeScore,
                snapshot.Context.Inning, snapshot.Context.IsTopHalf,
                snapshot.Balls, snapshot.Strikes, snapshot.Context.Outs, snapshot.Context.Bases,
                snapshot.Look.Cue, snapshot.Look.ZoneProbability));
        }
        while (_bridge.TryDequeuePaOutcome(out PaOutcome outcome))
        {
            _atBatView.AppendPlayLine(string.Format(PaLineFormat, OutcomeName(outcome)));
        }
        while (_bridge.TryDequeueNpcPa(out NpcPaFeedEvent npcPa))
        {
            string format = npcPa.IsRivalryPa ? NpcRivalryPaLineFormat : NpcPaLineFormat;
            _atBatView.AppendPlayLine(string.Format(format, npcPa.BatterName, OutcomeName(npcPa.Outcome)));
        }

        if (_gameTask.IsCompleted)
        {
            FinishInteractiveGame();
        }
        RefreshDayControlsEnabled();
    }

    private void OnPlayGamePressed()
    {
        GameManager gm = GameManager.Instance!;
        _statusLabel.Text = GameRunningText;
        gm.Career.AutopilotAttendedGames = false;
        _awaitingPendingGame = true;
        gm.Clock.AdvanceDay();
    }

    private void OnSkipDayPressed()
    {
        GameManager gm = GameManager.Instance!;
        gm.Career.AutopilotAttendedGames = true;
        gm.Clock.AdvanceDay();
        _statusLabel.Text = string.Empty;
        RefreshDayLabel();
    }

    private void StartInteractiveGame(CareerManager career)
    {
        _bridge.Reset();
        career.FeedSink = _bridge; // cleared in FinishInteractiveGame, after the task is observed done
        PlayerIntentBridge bridge = _bridge;
        _gameTask = Task.Run(() =>
        {
            // Sim runs entirely off the UI thread; the flush is the sim's own
            // batch and nothing else touches the connection while it runs.
            var policy = new InteractiveBatterPolicy(bridge);
            return career.PlayPendingGame(ref policy);
        });
    }

    private void FinishInteractiveGame()
    {
        Task<MicroGameResult> task = _gameTask!;
        _gameTask = null;
        GameManager.Instance!.Career.FeedSink = null;

        if (task.IsCompletedSuccessfully)
        {
            MicroGameResult result = task.Result;
            string final = string.Format(
                FinalFormat, result.AwayScore, result.HomeScore, result.Innings, result.HumanPa);
            _atBatView.AppendPlayLine(final);
            _statusLabel.Text = final;
        }
        else
        {
            // Cancelled (scene exit) or faulted; observe so nothing is unobserved.
            _statusLabel.Text = task.Exception?.InnerException is OperationCanceledException
                ? string.Empty
                : task.Exception?.InnerException?.Message ?? string.Empty;
        }
        RefreshDayLabel();
    }

    private void OnSwingCommitted(double timingError, bool guessInZone) =>
        _bridge.SubmitSwing(timingError, guessInZone);

    private void OnTakeCommitted(bool guessInZone) => _bridge.SubmitTake(guessInZone);

    /// <summary>
    /// Unified per-frame recomputation of the Play/Skip buttons' disabled
    /// state — blocked while a game is in flight (the original invariant)
    /// OR while either overlay (event choice / succession choice) has
    /// something for the player to resolve, so a player literally cannot
    /// advance the day out from under a pending decision. Called every
    /// frame rather than at scattered transition points so it can never
    /// drift out of sync with either overlay's own state.
    /// </summary>
    private void RefreshDayControlsEnabled()
    {
        GameManager gm = GameManager.Instance!;
        bool blocked = _gameTask is not null || _awaitingPendingGame
            || gm.GrittyEventChoices.HasPendingChoice
            || gm.Career.HasPendingSuccessionChoice;
        _playGameButton.Disabled = blocked;
        _skipDayButton.Disabled = blocked;
    }

    private void RefreshDayLabel()
    {
        GlobalState state = GameManager.Instance!.State;
        _dayLabel.Text = string.Format(DayFormat, state.CurrentDay, state.SeasonYear);
    }

    /// <summary>
    /// Phase 8c: an always-on readout of the avatar's current roster
    /// availability, straight off <see cref="GameManager.Absences"/> — unlike
    /// the one-shot NoGameText branch (which only fires when the player
    /// clicks Play), this stays accurate through a whole Skip-Day-only
    /// playthrough of an absence. TryGet returns expired entries too, so
    /// StateOn(today) — not the bool — decides visibility.
    /// </summary>
    private void RefreshAvailabilityLabel(CareerManager career)
    {
        SlotAvailability state = SlotAvailability.Available;
        AbsenceEntry entry = default;
        if (career.HasAvatar
            && GameManager.Instance!.Absences.TryGet(career.AvatarPlayerId, out entry))
        {
            state = entry.StateOn(GameManager.Instance!.State.CurrentDay);
        }

        bool changed = state != _shownAvailability
            || (state != SlotAvailability.Available
                && (entry.Reason != _shownAvailabilityReason
                    || entry.UntilDay != _shownAvailabilityUntilDay
                    || entry.PenaltyUntilDay != _shownAvailabilityPenaltyUntilDay));
        if (!changed)
        {
            return;
        }

        _shownAvailability = state;
        _shownAvailabilityReason = entry.Reason;
        _shownAvailabilityUntilDay = entry.UntilDay;
        _shownAvailabilityPenaltyUntilDay = entry.PenaltyUntilDay;

        _availabilityLabel.Visible = state != SlotAvailability.Available;
        _availabilityLabel.Text = state switch
        {
            SlotAvailability.Absent => string.Format(
                AbsentStatusFormat, AbsenceReasonText(entry.Reason), entry.UntilDay),
            SlotAvailability.Rusty => string.Format(
                RustyStatusFormat, AbsenceReasonText(entry.Reason), entry.RatingPenalty, entry.PenaltyUntilDay),
            _ => string.Empty,
        };
    }

    private string AbsenceReasonText(AbsenceReason reason) => reason switch
    {
        AbsenceReason.Injury => InjuryReasonText,
        AbsenceReason.Suspension => SuspensionReasonText,
        AbsenceReason.Arrest => ArrestReasonText,
        _ => string.Empty,
    };

    private string OutcomeName(PaOutcome outcome)
    {
        int index = (int)outcome;
        return index >= 0 && index < _outcomeNames.Length ? _outcomeNames[index] : outcome.ToString();
    }
}
