using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Read-only snapshot of the live at-bat the driver hands to the view — the
/// UI renders this and nothing else (ui_conventions: UI is read-only over
/// simulation state; the driver owns every write). CueType/ZoneHint are the
/// v4 pre-pitch look the zone-read minigame is played against.
/// </summary>
public readonly struct AtBatViewState
{
    public readonly int AwayScore;
    public readonly int HomeScore;
    public readonly int Inning;
    public readonly bool IsTopHalf;
    public readonly int Balls;
    public readonly int Strikes;
    public readonly int Outs;
    /// <summary>3-bit base map, matching the sim (1 = 1B, 2 = 2B, 4 = 3B).</summary>
    public readonly int Bases;
    /// <summary>The (possibly blurred) pitch-type cue for the incoming pitch.</summary>
    public readonly PitchType CueType;
    /// <summary>Scouting P(in zone) implied by the cue, [0, 1].</summary>
    public readonly double ZoneHint;

    public AtBatViewState(
        int awayScore, int homeScore, int inning, bool isTopHalf,
        int balls, int strikes, int outs, int bases, PitchType cueType, double zoneHint)
    {
        AwayScore = awayScore;
        HomeScore = homeScore;
        Inning = inning;
        IsTopHalf = isTopHalf;
        Balls = balls;
        Strikes = strikes;
        Outs = outs;
        Bases = bases;
        CueType = cueType;
        ZoneHint = zoneHint;
    }
}

/// <summary>
/// The Phase 4 thin vertical slice (micro doc §12), extended by Phase 12d-2
/// (at_bat_presentation.md §3/§5) into a beat sequencer: the sim races ahead
/// (at most one human pitch, per <see cref="PlayerIntentBridge.AwaitIntent"/>
/// blocking), the view paces the presentation and gates player input.
/// <see cref="EnqueuePitchBeat"/>/<see cref="EnqueuePaOutcomeBeat"/>/
/// <see cref="EnqueueNpcBeat"/> queue events in the driver's dequeue order
/// (the causal bound from §3); <see cref="SetPendingSnapshot"/> is a
/// latest-wins pre-pitch state applied only once that queue drains — the
/// input-gating fix (§3): controls re-enable ONLY on entry to AwaitingInput,
/// never mid-reveal. No query, LINQ, or string-format runs per frame; the
/// sequencer is a light elapsed-time state machine, event-driven at each
/// beat boundary. Node paths below were verified against AtBatView.tscn via
/// godot_scene_mapper before being written.
/// </summary>
public sealed partial class AtBatView : PanelContainer
{
    /// <summary>Player committed a swing: the slider's τ ∈ [-1, +1] (§6) plus the zone read.</summary>
    [Signal]
    public delegate void SwingCommittedEventHandler(double timingError, bool guessInZone);

    /// <summary>Player took the pitch (with their zone read).</summary>
    [Signal]
    public delegate void TakeCommittedEventHandler(bool guessInZone);

    /// <summary>Cap on retained play-by-play lines before the oldest are dropped.</summary>
    [Export]
    public int MaxLogLines { get; set; } = 200;

    /// <summary>Player-facing names for the PitchType values, in enum order.</summary>
    [Export]
    public string PitchTypeNamesCsv { get; set; } = "Fastball,Breaking ball,Offspeed";

    /// <summary>Pre-pitch look line: {0} = cued pitch type, {1} = zone hint [0,1].</summary>
    [Export]
    public string PitchCueFormat { get; set; } = "Looks like: {0} — zone odds {1:P0}";

    /// <summary>Scoreboard inning line for the top half: {0} = inning number.</summary>
    [Export]
    public string TopInningFormat { get; set; } = "TOP {0}";

    /// <summary>Scoreboard inning line for the bottom half: {0} = inning number.</summary>
    [Export]
    public string BottomInningFormat { get; set; } = "BOT {0}";

    /// <summary>Scoreboard outs readout: {0} = outs this half-inning.</summary>
    [Export]
    public string OutsFormat { get; set; } = "{0} OUT";

    // 12d-2 beat pacing (at_bat_presentation.md §5.6: "all beat durations are
    // [Export] constants") — the windup is a fixed hold before a dequeued
    // pitch result is revealed (so the reveal never reads as an instant
    // teleport from click to result); PA/NPC beats get a shorter, simpler
    // pace since their own bigger presentation is 12d-3's scope.
    [Export]
    public int WindupDurationMs { get; set; } = 300;

    [Export]
    public int PitchRevealDurationMs { get; set; } = 700;

    [Export]
    public int PaOutcomeBeatDurationMs { get; set; } = 900;

    [Export]
    public int NpcBeatDurationMs { get; set; } = 250;

    // 12d-2 ResultReveal copy (at_bat_presentation.md §5.1) — every branch the
    // frozen PitchResult DTO can actually report. Two contract notes from the
    // 12d-1 sign-off, built to literally: strike three renders from
    // PaEnded && Class==Strike (never the Strikes field, which never reaches
    // 3), and Foul only ever appears at a two-strike, count-preserving pitch.
    [Export]
    public string RevealBallText { get; set; } = "Ball";

    [Export]
    public string RevealWalkText { get; set; } = "Ball four — take your base";

    [Export]
    public string RevealCalledStrikeText { get; set; } = "Strike — caught looking";

    [Export]
    public string RevealSwingMissText { get; set; } = "Swing and a miss";

    [Export]
    public string RevealStrikeThreeText { get; set; } = "Strike three!";

    [Export]
    public string RevealFoulText { get; set; } = "Fouled it back";

    [Export]
    public string RevealInPlayText { get; set; } = "In play…";

    /// <summary>The true-pitch flavor line under the call: {0} = true type, {1} = true zone read.</summary>
    [Export]
    public string RevealFlavorFormat { get; set; } = "{0} — {1}";

    [Export]
    public string RevealInZoneText { get; set; } = "in the zone";

    [Export]
    public string RevealOutOfZoneText { get; set; } = "out of the zone";

    // Theme variations the base-diamond panels swap between; theme-owned so
    // occupied/empty colors stay a designer decision, not a code constant.
    private static readonly StringName BaseLitVariation = "BaseLit";
    private static readonly StringName BaseDimVariation = "BaseDim";
    private static readonly StringName RevealDiamondVariation = "RevealLabelDiamond";
    private static readonly StringName RevealDirtVariation = "RevealLabelDirt";

    private enum RevealAccent { Positive, Negative, Neutral }

    private enum BeatKind : byte { PitchResult, PaOutcome, NpcPa }

    /// <summary>One queued presentation beat — a pitch's truth, or an already-formatted PA/NPC log line, paced in the driver's dequeue order (the causal bound at_bat_presentation.md §3 establishes).</summary>
    private readonly struct BeatEvent
    {
        public readonly BeatKind Kind;
        public readonly PitchResult Pitch;
        public readonly string LogLine;

        private BeatEvent(BeatKind kind, in PitchResult pitch, string logLine)
        {
            Kind = kind;
            Pitch = pitch;
            LogLine = logLine;
        }

        public static BeatEvent ForPitch(in PitchResult pitch) => new(BeatKind.PitchResult, in pitch, string.Empty);
        public static BeatEvent ForPaOutcome(string logLine) => new(BeatKind.PaOutcome, default, logLine);
        public static BeatEvent ForNpcPa(string logLine) => new(BeatKind.NpcPa, default, logLine);
    }

    /// <summary>Idle = between beats (about to pop the next one, or — with an empty queue and a pending snapshot — the moment AwaitingInput is entered and controls re-enable).</summary>
    private enum SequencerPhase { Idle, Windup, Reveal }

    private Label _awayScoreLabel = null!;
    private Label _homeScoreLabel = null!;
    private Label _inningLabel = null!;
    private Label _countLabel = null!;
    private Label _outsLabel = null!;
    private Panel _base1 = null!;
    private Panel _base2 = null!;
    private Panel _base3 = null!;
    private Label _pitchCueLabel = null!;
    private RichTextLabel _playLog = null!;
    private CheckButton _zoneReadToggle = null!;
    private HSlider _timingSlider = null!;
    private Button _swingButton = null!;
    private Button _takeButton = null!;
    private PanelContainer _revealChip = null!;
    private Label _revealLabel = null!;

    private string[] _pitchTypeNames = System.Array.Empty<string>();
    private int _logLines;

    // The beat sequencer (12d-2).
    private readonly Queue<BeatEvent> _beatQueue = new(16);
    private bool _hasPendingSnapshot;
    private AtBatViewState _pendingSnapshot;
    private SequencerPhase _phase = SequencerPhase.Idle;
    private double _phaseElapsedMs;
    private double _currentRevealDurationMs;
    private BeatEvent _currentBeat;

    // Dirty-check baselines for the count/base/score "tween, not a silent
    // relabel" pulses (§5.2) — -1 means "no prior state", so the very first
    // snapshot of a fresh game never fires a pulse against stale/zeroed data.
    private int _shownBalls = -1;
    private int _shownStrikes = -1;
    private int _shownBases = -1;
    private int _shownAwayScore = -1;
    private int _shownHomeScore = -1;

    public override void _Ready()
    {
        _awayScoreLabel = GetNode<Label>("Layout/Scoreboard/ScoreboardRow/AwayBox/AwayScore");
        _homeScoreLabel = GetNode<Label>("Layout/Scoreboard/ScoreboardRow/HomeBox/HomeScore");
        _inningLabel = GetNode<Label>("Layout/Scoreboard/ScoreboardRow/CenterBox/InningLabel");
        _countLabel = GetNode<Label>("Layout/Scoreboard/ScoreboardRow/CenterBox/CountRow/CountLabel");
        _outsLabel = GetNode<Label>("Layout/Scoreboard/ScoreboardRow/CenterBox/CountRow/OutsLabel");
        _base1 = GetNode<Panel>("Layout/Scoreboard/ScoreboardRow/CenterBox/DiamondCenter/Diamond/Base1");
        _base2 = GetNode<Panel>("Layout/Scoreboard/ScoreboardRow/CenterBox/DiamondCenter/Diamond/Base2");
        _base3 = GetNode<Panel>("Layout/Scoreboard/ScoreboardRow/CenterBox/DiamondCenter/Diamond/Base3");
        _pitchCueLabel = GetNode<Label>("Layout/PitchCueRow/CueChip/PitchCueLabel");
        _playLog = GetNode<RichTextLabel>("Layout/PlayLog");
        _zoneReadToggle = GetNode<CheckButton>("Layout/Controls/ZoneReadToggle");
        _timingSlider = GetNode<HSlider>("Layout/Controls/TimingSlider");
        _swingButton = GetNode<Button>("Layout/Controls/SwingButton");
        _takeButton = GetNode<Button>("Layout/Controls/TakeButton");
        _revealChip = GetNode<PanelContainer>("RevealOverlay/RevealChip");
        _revealLabel = GetNode<Label>("RevealOverlay/RevealChip/RevealLabel");

        _swingButton.Pressed += OnSwingPressed;
        _takeButton.Pressed += OnTakePressed;
        _pitchTypeNames = PitchTypeNamesCsv.Split(',');
        _playLog.Clear();
        SetIntentEnabled(false); // AwaitingInput is entered explicitly once the sequencer applies the first snapshot
    }

    public override void _ExitTree()
    {
        _swingButton.Pressed -= OnSwingPressed;
        _takeButton.Pressed -= OnTakePressed;
    }

    /// <summary>
    /// The sequencer's own light per-frame tick (at_bat_presentation.md §5.7:
    /// "AtBatView gains a light _Process/timer"). No query/LINQ/string-format
    /// runs here on the idle path — an elapsed-time comparison and, at most,
    /// one beat transition per call.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_phase == SequencerPhase.Idle)
        {
            if (_beatQueue.Count > 0)
            {
                StartBeat(_beatQueue.Dequeue());
            }
            else if (_hasPendingSnapshot)
            {
                _hasPendingSnapshot = false;
                ApplyState(in _pendingSnapshot);
                SetIntentEnabled(true); // AwaitingInput entered — the ONLY place controls re-enable
            }
            return;
        }

        _phaseElapsedMs += delta * 1000.0;
        if (_phase == SequencerPhase.Windup)
        {
            if (_phaseElapsedMs >= WindupDurationMs)
            {
                ShowPitchReveal(in _currentBeat.Pitch);
                _phase = SequencerPhase.Reveal;
                _phaseElapsedMs = 0.0;
                _currentRevealDurationMs = PitchRevealDurationMs;
            }
            return;
        }

        // Reveal
        if (_phaseElapsedMs >= _currentRevealDurationMs)
        {
            if (_currentBeat.Kind == BeatKind.PitchResult)
            {
                HideReveal();
            }
            _phase = SequencerPhase.Idle;
            _phaseElapsedMs = 0.0;
        }
    }

    private void StartBeat(BeatEvent beat)
    {
        _currentBeat = beat;
        switch (beat.Kind)
        {
            case BeatKind.PitchResult:
                _phase = SequencerPhase.Windup;
                _phaseElapsedMs = 0.0;
                break;
            case BeatKind.PaOutcome:
                AppendPlayLine(beat.LogLine);
                _phase = SequencerPhase.Reveal;
                _phaseElapsedMs = 0.0;
                _currentRevealDurationMs = PaOutcomeBeatDurationMs;
                break;
            default: // NpcPa
                AppendPlayLine(beat.LogLine);
                _phase = SequencerPhase.Reveal;
                _phaseElapsedMs = 0.0;
                _currentRevealDurationMs = NpcBeatDurationMs;
                break;
        }
    }

    /// <summary>Latest pre-pitch state — a dirty-flag, latest-wins value applied only once the beat queue drains (never mid-reveal).</summary>
    public void SetPendingSnapshot(in AtBatViewState state)
    {
        _pendingSnapshot = state;
        _hasPendingSnapshot = true;
    }

    /// <summary>Queues one resolved pitch (Phase 12d-1's <see cref="PitchResult"/>) for the sequencer to reveal in order.</summary>
    public void EnqueuePitchBeat(in PitchResult result) => _beatQueue.Enqueue(BeatEvent.ForPitch(in result));

    /// <summary>Queues one resolved human PA's already-formatted play-log line, paced like every other beat.</summary>
    public void EnqueuePaOutcomeBeat(string logLine) => _beatQueue.Enqueue(BeatEvent.ForPaOutcome(logLine));

    /// <summary>Queues one NPC play-by-play line, paced like every other beat.</summary>
    public void EnqueueNpcBeat(string logLine) => _beatQueue.Enqueue(BeatEvent.ForNpcPa(logLine));

    /// <summary>Clears all in-flight beat/reveal state for a fresh attended game — the driver's <see cref="PlayerIntentBridge.Reset"/> mirror.</summary>
    public void ResetSequencer()
    {
        _beatQueue.Clear();
        _hasPendingSnapshot = false;
        _phase = SequencerPhase.Idle;
        _phaseElapsedMs = 0.0;
        HideReveal();
        _shownBalls = -1;
        _shownStrikes = -1;
        _shownBases = -1;
        _shownAwayScore = -1;
        _shownHomeScore = -1;
        SetIntentEnabled(false);
    }

    /// <summary>Applies one pre-pitch state to the labels/diamond — the count/base/score "tween, not a silent relabel" pass (§5.2): a pulse fires only where the value actually changed since the last applied state.</summary>
    private void ApplyState(in AtBatViewState state)
    {
        _awayScoreLabel.Text = state.AwayScore.ToString();
        _homeScoreLabel.Text = state.HomeScore.ToString();
        _inningLabel.Text = string.Format(
            state.IsTopHalf ? TopInningFormat : BottomInningFormat, state.Inning);
        _countLabel.Text = $"{state.Balls}-{state.Strikes}";
        _outsLabel.Text = string.Format(OutsFormat, state.Outs);

        bool countChanged = _shownBalls >= 0 && (state.Balls != _shownBalls || state.Strikes != _shownStrikes);
        bool awayScored = _shownAwayScore >= 0 && state.AwayScore > _shownAwayScore;
        bool homeScored = _shownHomeScore >= 0 && state.HomeScore > _shownHomeScore;
        bool basesKnown = _shownBases >= 0;

        SetBase(_base1, (state.Bases & 0b001) != 0, basesKnown && (_shownBases & 0b001) != 0);
        SetBase(_base2, (state.Bases & 0b010) != 0, basesKnown && (_shownBases & 0b010) != 0);
        SetBase(_base3, (state.Bases & 0b100) != 0, basesKnown && (_shownBases & 0b100) != 0);

        if (countChanged)
        {
            PulseControl(_countLabel);
        }
        if (awayScored)
        {
            PulseControl(_awayScoreLabel);
        }
        if (homeScored)
        {
            PulseControl(_homeScoreLabel);
        }

        _shownBalls = state.Balls;
        _shownStrikes = state.Strikes;
        _shownBases = state.Bases;
        _shownAwayScore = state.AwayScore;
        _shownHomeScore = state.HomeScore;

        _pitchCueLabel.Text = string.Format(PitchCueFormat, PitchTypeName(state.CueType), state.ZoneHint);
    }

    /// <summary>Lights or dims one base-diamond panel via its theme variation; pulses only the pitch that newly lit it (a runner reaching, not one already standing there).</summary>
    private void SetBase(Panel basePanel, bool occupied, bool wasLit)
    {
        basePanel.ThemeTypeVariation = occupied ? BaseLitVariation : BaseDimVariation;
        if (occupied && !wasLit)
        {
            PulseControl(basePanel);
        }
    }

    /// <summary>A small scale pulse on any Control — the shared motion primitive behind the count tick, a score bump, and a newly-lit base (§5.2).</summary>
    private void PulseControl(Control control)
    {
        control.PivotOffset = control.Size / 2f;
        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(control, "scale", new Vector2(1.25f, 1.25f), 0.08)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(control, "scale", Vector2.One, 0.12)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
    }

    /// <summary>The ResultReveal beat (§5.1) — the just-thrown pitch's truth, tween'd in and held for <see cref="PitchRevealDurationMs"/>, then faded out by the same tween that timed the beat.</summary>
    private void ShowPitchReveal(in PitchResult result)
    {
        (string text, RevealAccent accent) = BuildRevealCopy(in result);
        _revealLabel.Text = text;
        _revealLabel.ThemeTypeVariation = accent switch
        {
            RevealAccent.Positive => RevealDiamondVariation,
            RevealAccent.Negative => RevealDirtVariation,
            _ => string.Empty,
        };

        _revealChip.Visible = true;
        _revealChip.Modulate = new Color(1f, 1f, 1f, 0f);
        _revealChip.Scale = new Vector2(0.85f, 0.85f);
        _revealChip.PivotOffset = _revealChip.Size / 2f;

        double fadeSec = System.Math.Min(0.15, PitchRevealDurationMs / 3000.0);
        double holdSec = System.Math.Max(0.0, PitchRevealDurationMs / 1000.0 - fadeSec * 2.0);

        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_revealChip, "modulate:a", 1.0, fadeSec);
        tween.TweenProperty(_revealChip, "scale", Vector2.One, fadeSec)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.Chain().TweenInterval(holdSec);
        tween.Chain().TweenProperty(_revealChip, "modulate:a", 0.0, fadeSec);
    }

    private void HideReveal() => _revealChip.Visible = false;

    /// <summary>at_bat_presentation.md §5.1's table, narrowed to exactly what the frozen PitchResult DTO reports (no timing/read grading — that's §5.3, 12d-3's scope).</summary>
    private (string Text, RevealAccent Accent) BuildRevealCopy(in PitchResult result)
    {
        string call;
        RevealAccent accent;
        switch (result.Class)
        {
            case PitchClass.Ball:
                call = result.PaEnded ? RevealWalkText : RevealBallText;
                accent = RevealAccent.Positive;
                break;
            case PitchClass.Strike:
                call = result.PaEnded
                    ? RevealStrikeThreeText // §contract note 1: PaEnded && Class==Strike, never the Strikes field
                    : (result.BatterSwung ? RevealSwingMissText : RevealCalledStrikeText);
                accent = RevealAccent.Negative;
                break;
            case PitchClass.Foul:
                call = RevealFoulText;
                accent = RevealAccent.Neutral;
                break;
            default: // InPlay — hands off to the PA reveal (12d-3)
                call = RevealInPlayText;
                accent = RevealAccent.Neutral;
                break;
        }
        string flavor = string.Format(
            RevealFlavorFormat, PitchTypeName(result.Type), result.InZone ? RevealInZoneText : RevealOutOfZoneText);
        return (call + "\n" + flavor, accent);
    }

    /// <summary>Appends one play-by-play line (already-localized text from the driver's feed).</summary>
    public void AppendPlayLine(string line)
    {
        if (_logLines >= MaxLogLines)
        {
            _playLog.Clear();
            _logLines = 0;
        }
        _playLog.AppendText(line);
        _playLog.AppendText("\n");
        _logLines++;
    }

    private void OnSwingPressed()
    {
        SetIntentEnabled(false);
        EmitSignal(SignalName.SwingCommitted, _timingSlider.Value, _zoneReadToggle.ButtonPressed);
    }

    private void OnTakePressed()
    {
        SetIntentEnabled(false);
        EmitSignal(SignalName.TakeCommitted, _zoneReadToggle.ButtonPressed);
    }

    /// <summary>Intent controls lock while the sim resolves the pitch off the UI thread.</summary>
    private void SetIntentEnabled(bool enabled)
    {
        _swingButton.Disabled = !enabled;
        _takeButton.Disabled = !enabled;
        _timingSlider.Editable = enabled;
        _zoneReadToggle.Disabled = !enabled;
    }

    private string PitchTypeName(PitchType type)
    {
        int index = (int)type;
        return index >= 0 && index < _pitchTypeNames.Length ? _pitchTypeNames[index] : type.ToString();
    }
}
