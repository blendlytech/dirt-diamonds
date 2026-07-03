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

    /// <summary>Comma-separated player-facing names for the 7 PaOutcome values, in enum order.</summary>
    [Export]
    public string OutcomeNamesCsv { get; set; } = "Out,Strikeout,Walk,Single,Double,Triple,Home run";

    // Thin-slice defaults for the debug avatar until the new-game creation
    // flow ships; ratings are a modest above-average rookie bat.
    [Export]
    public int DebugAvatarTeamId { get; set; } = 1;

    private Label _dayLabel = null!;
    private Button _playGameButton = null!;
    private Button _skipDayButton = null!;
    private Label _statusLabel = null!;
    private AtBatView _atBatView = null!;

    private readonly PlayerIntentBridge _bridge = new();
    private Task<MicroGameResult>? _gameTask;
    private string[] _outcomeNames = Array.Empty<string>();
    private bool _awaitingPendingGame;

    public override void _Ready()
    {
        _dayLabel = GetNode<Label>("Screen/TopBar/DayLabel");
        _playGameButton = GetNode<Button>("Screen/TopBar/PlayGameButton");
        _skipDayButton = GetNode<Button>("Screen/TopBar/SkipDayButton");
        _statusLabel = GetNode<Label>("Screen/TopBar/StatusLabel");
        _atBatView = GetNode<AtBatView>("Screen/AtBatView");

        _playGameButton.Pressed += OnPlayGamePressed;
        _skipDayButton.Pressed += OnSkipDayPressed;
        _atBatView.SwingCommitted += OnSwingCommitted;
        _atBatView.TakeCommitted += OnTakeCommitted;

        _outcomeNames = OutcomeNamesCsv.Split(',');
        RefreshDayLabel();
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
                SetDayControlsEnabled(true);
            }
            RefreshDayLabel();
        }

        if (_gameTask is null)
        {
            return;
        }

        if (_bridge.TryGetSnapshot(out AtBatSnapshot snapshot))
        {
            _atBatView.Render(new AtBatViewState(
                snapshot.Context.AwayScore, snapshot.Context.HomeScore,
                snapshot.Context.Inning, snapshot.Context.IsTopHalf,
                snapshot.Balls, snapshot.Strikes, snapshot.Context.Outs, snapshot.Context.Bases));
        }
        while (_bridge.TryDequeuePaOutcome(out PaOutcome outcome))
        {
            _atBatView.AppendPlayLine(string.Format(PaLineFormat, OutcomeName(outcome)));
        }

        if (_gameTask.IsCompleted)
        {
            FinishInteractiveGame();
        }
    }

    private void OnPlayGamePressed()
    {
        GameManager gm = GameManager.Instance!;
        EnsureDebugAvatar(gm);
        SetDayControlsEnabled(false);
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
        SetDayControlsEnabled(true);

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

    private void OnSwingCommitted(double timingError) => _bridge.SubmitSwing(timingError);

    private void OnTakeCommitted() => _bridge.SubmitTake();

    /// <summary>Placeholder until the new-game creation flow (life sim phase) ships.</summary>
    private void EnsureDebugAvatar(GameManager gm)
    {
        if (gm.Career.HasAvatar)
        {
            return;
        }
        gm.Career.CreateAvatar("You", "Rookie", DebugAvatarTeamId, new PlayerRatingsRow
        {
            IsPitcher = false,
            BatPower = 60,
            BatContact = 60,
            BatDiscipline = 60,
            PitStuff = 50,
            PitControl = 50,
            PitStamina = 50,
            Fielding = 50,
        });
    }

    private void SetDayControlsEnabled(bool enabled)
    {
        _playGameButton.Disabled = !enabled;
        _skipDayButton.Disabled = !enabled;
    }

    private void RefreshDayLabel()
    {
        GlobalState state = GameManager.Instance!.State;
        _dayLabel.Text = string.Format(DayFormat, state.CurrentDay, state.SeasonYear);
    }

    private string OutcomeName(PaOutcome outcome)
    {
        int index = (int)outcome;
        return index >= 0 && index < _outcomeNames.Length ? _outcomeNames[index] : outcome.ToString();
    }
}
