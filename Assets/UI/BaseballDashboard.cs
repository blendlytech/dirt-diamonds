using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.UI.Portraits;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Phase 10a: the left panel of the two-panel shell. Since Slice G-2 the
/// day-advance role lives in the header's <see cref="TimeControlBar"/> (the
/// continuous clock + Pause/speed/Skip/contextual Play cluster, the sole
/// caller of TimeManager.AdvanceDay); this panel keeps the attended at-bat
/// machinery — CareerManager.TryGetPendingGame / PlayPendingGame — launched
/// off the bar's PlayGameRequested signal, and refreshes its header off the
/// bar's DaySettled. The cross-cutting day-advance gates live in the shared
/// GameManager.CanAdvanceDay (G-1), which this panel feeds via
/// InteractiveGameInFlight while the game task runs. The
/// bridge/task/dirty-flag driver is the Phase 5 design unchanged: the game
/// runs on a background task (never the UI thread), the view renders
/// snapshot DTOs, and the player's swing/take intent flows back through a
/// <see cref="PlayerIntentBridge"/>. Node paths verified against
/// BaseballDashboard.tscn via godot_scene_mapper before this script was
/// written.
///
/// UI-reorg note: the Scouting Report / Development / Season Stats /
/// Standings / League Leaders / Plan Today cards that used to fill this
/// panel's MeRow/LeagueRow now live in the Burner Phone (see
/// <see cref="PlayerScreen"/>'s and <see cref="LeagueScreen"/>'s "Player"
/// and "League" tabs, and ScheduleScreen inside the phone's "Calendar" tab)
/// — this panel is deliberately left mostly blank below the header/at-bat
/// slot until contextual imagery/minigames replace the empty space.
/// </summary>
public sealed partial class BaseballDashboard : PanelContainer
{
    // Player-facing text templates live on exported properties so the scene
    // (not compiled code) is the editing surface, per ui_conventions.
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

    private PortraitView _avatarPortrait = null!;
    private TimeControlBar _timeControlBar = null!;
    private Label _statusLabel = null!;
    private PanelContainer _availabilityCard = null!;
    private Label _availabilityLabel = null!;
    private AtBatView _atBatView = null!;
    private PanelContainer _recapCard = null!;
    private Label _recapLabel = null!;

    private readonly PlayerIntentBridge _bridge = new();
    private Task<MicroGameResult>? _gameTask;
    private string[] _outcomeNames = Array.Empty<string>();

    // Dirty-flag identity for the availability label (ui_conventions.md: no
    // per-frame string formatting).
    private SlotAvailability _shownAvailability = SlotAvailability.Available;
    private AbsenceReason _shownAvailabilityReason;
    private long _shownAvailabilityUntilDay;
    private long _shownAvailabilityPenaltyUntilDay;

    public override void _Ready()
    {
        _avatarPortrait = GetNode<PortraitView>("Layout/HeaderBand/HeaderRow/AvatarPortrait");
        _timeControlBar = GetNode<TimeControlBar>("Layout/HeaderBand/HeaderRow/TimeControlBar");
        _statusLabel = GetNode<Label>("Layout/HeaderBand/HeaderRow/HeaderText/StatusLabel");
        _availabilityCard = GetNode<PanelContainer>("Layout/AvailabilityCard");
        _availabilityLabel = GetNode<Label>("Layout/AvailabilityCard/AvailabilityLabel");
        _atBatView = GetNode<AtBatView>("Layout/CenterSlot/AtBatView");
        _recapCard = GetNode<PanelContainer>("Layout/CenterSlot/RecapCard");
        _recapLabel = GetNode<Label>("Layout/CenterSlot/RecapCard/RecapLayout/RecapLabel");

        _timeControlBar.PlayGameRequested += OnPlayGameRequested;
        _timeControlBar.DaySettled += OnDaySettled;
        _atBatView.ReadCommitted += OnReadCommitted;

        _outcomeNames = OutcomeNamesCsv.Split(',');
        RefreshHeader();
    }

    public override void _ExitTree()
    {
        // Aborts any in-flight game; it unwinds unflushed and the career
        // forfeits it to the autopilot on the next day tick. The shared gate
        // must not stay latched by a game this panel is abandoning.
        _bridge.Cancel();
        if (_gameTask is not null && GameManager.Instance is { } gm)
        {
            gm.InteractiveGameInFlight = false;
        }
        _timeControlBar.PlayGameRequested -= OnPlayGameRequested;
        _timeControlBar.DaySettled -= OnDaySettled;
        _atBatView.ReadCommitted -= OnReadCommitted;
    }

    public override void _Process(double delta)
    {
        CareerManager career = GameManager.Instance!.Career;
        RefreshAvailabilityLabel(career);

        // Slice G-2: the post-advance settle wait (the old
        // _awaitingPendingGame/_refreshAfterDayTick drain) now lives in the
        // TimeControlBar, which owns every advance; this panel refreshes off
        // its DaySettled signal and launches off PlayGameRequested.
        if (_gameTask is null)
        {
            return;
        }

        // 12d-2 (at_bat_presentation.md §5.7): the dashboard only forwards, in
        // dequeue order, into the view's own beat queue — it never renders
        // directly. Pitch results are drained before PA outcomes/NPC feed so
        // the beat queue preserves the sim's causal order (§3):
        // [terminal pitch] -> [PA outcome] -> [NPC feed...] -> [next snapshot].
        // The pending snapshot is a latest-wins value the view applies (and
        // only then re-enables input) once its beat queue drains, so its
        // position here relative to the loops below doesn't affect ordering.
        if (_bridge.TryGetSnapshot(out AtBatSnapshot snapshot))
        {
            _atBatView.SetPendingSnapshot(new AtBatViewState(
                snapshot.Context.AwayScore, snapshot.Context.HomeScore,
                snapshot.Context.Inning, snapshot.Context.IsTopHalf,
                snapshot.Balls, snapshot.Strikes, snapshot.Context.Outs, snapshot.Context.Bases,
                snapshot.Look.Cue, snapshot.Look.ZoneProbability));
        }
        while (_bridge.TryDequeuePitchResult(out PitchResult pitchResult))
        {
            _atBatView.EnqueuePitchBeat(in pitchResult);
        }
        while (_bridge.TryDequeuePaOutcome(out PaOutcome outcome))
        {
            _atBatView.EnqueuePaOutcomeBeat(outcome, string.Format(PaLineFormat, OutcomeName(outcome)));
        }
        while (_bridge.TryDequeueNpcPa(out NpcPaFeedEvent npcPa))
        {
            string format = npcPa.IsRivalryPa ? NpcRivalryPaLineFormat : NpcPaLineFormat;
            _atBatView.EnqueueNpcBeat(npcPa.Outcome, string.Format(format, npcPa.BatterName, OutcomeName(npcPa.Outcome)));
        }

        // Fable review (12d-2+12d-3, finding 1): the sim task can complete
        // microseconds after the player's last intent while the view's beat
        // queue still holds the terminal pitch reveal, the PA reveal, and any
        // trailing NPC beats — gating on SequencerDrained too keeps the drain
        // loops above running every frame until the presentation actually
        // catches up, instead of tearing the view down mid-reveal.
        if (_gameTask.IsCompleted && _atBatView.SequencerDrained)
        {
            FinishInteractiveGame();
        }
    }

    /// <summary>
    /// Slice G-2: the bar's contextual Play press. The pending game already
    /// exists (the day ticked with autopilot off and parked it), so this
    /// launches the at-bat directly — no day advance, no settle wait. The
    /// guard makes a stale press a no-op.
    /// </summary>
    private void OnPlayGameRequested()
    {
        CareerManager career = GameManager.Instance!.Career;
        if (_gameTask is null && career.HasPendingGame)
        {
            _statusLabel.Text = GameRunningText;
            StartInteractiveGame(career);
        }
    }

    /// <summary>
    /// Slice G-2: a day advance (Skip or midnight roll) fully settled — the
    /// old post-drain refresh, now signal-driven. Clearing the status line
    /// here is the old Skip-click clear, one settled frame later.
    /// </summary>
    private void OnDaySettled()
    {
        _statusLabel.Text = string.Empty;
        RefreshHeader();
    }

    private void StartInteractiveGame(CareerManager career)
    {
        // Slice G-1: the shared gate must see the game the moment it exists,
        // before any other driver's frame could consult CanAdvanceDay.
        GameManager.Instance!.InteractiveGameInFlight = true;
        // 12c: the center slot's two occupants swap here — the at-bat view
        // takes over for the duration of the game, and the recap card (the
        // prior game's result, if any) steps aside until FinishInteractiveGame
        // swaps them back.
        _atBatView.Visible = true;
        _recapCard.Visible = false;
        _bridge.Reset();
        _atBatView.ResetSequencer();
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
        GameManager.Instance!.InteractiveGameInFlight = false;
        GameManager.Instance!.Career.FeedSink = null;
        _atBatView.Visible = false;

        if (task.IsCompletedSuccessfully)
        {
            MicroGameResult result = task.Result;
            string final = string.Format(
                FinalFormat, result.AwayScore, result.HomeScore, result.Innings, result.HumanPa);
            _atBatView.AppendPlayLine(final);
            _statusLabel.Text = final;
            _recapLabel.Text = final;
            _recapCard.Visible = true; // persists through idle/skip days until the next game starts
        }
        else
        {
            // Cancelled (scene exit) or faulted; observe so nothing is unobserved.
            _statusLabel.Text = task.Exception?.InnerException is OperationCanceledException
                ? string.Empty
                : task.Exception?.InnerException?.Message ?? string.Empty;
            // The game that would have replaced the prior recap never finished,
            // so that recap is still the last completed game — put it back.
            _recapCard.Visible = _recapLabel.Text.Length > 0;
        }
        RefreshHeader();
    }

    private void OnReadCommitted(int guessType, int guessCell, double approach) =>
        _bridge.SubmitRead((PitchType)guessType, (byte)guessCell, approach);

    /// <summary>
    /// The header band's portrait/identity — everything else the old
    /// scouting/dev/stat-line/standings/leaders cards showed now lives in the
    /// Burner Phone (PlayerScreen/LeagueScreen). Called at every day-advance
    /// settle point, same cadence those cards used to ride.
    /// </summary>
    private void RefreshHeader()
    {
        GameManager gm = GameManager.Instance!;
        CareerManager career = gm.Career;
        bool show = career.HasAvatar;
        _avatarPortrait.Visible = show;
        if (!show)
        {
            // No avatar means no game lifecycle owns the recap — an orphaned
            // "Last Game" card must not outlive the header around it.
            _recapCard.Visible = false;
            return;
        }

        string avatarId = career.AvatarPlayerId;
        if (gm.Players.TryGetById(avatarId, out PlayerRow player))
        {
            _avatarPortrait.SetIdentity(avatarId, $"{player.FirstName} {player.LastName}");
        }
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

        _availabilityCard.Visible = state != SlotAvailability.Available;
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
