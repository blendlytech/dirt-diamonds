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
    /// <summary>
    /// Player committed a "Read the Pitch" guess (at_bat_read_input_model.md §2):
    /// a guessed pitch type, a guessed 3×3 zone cell (or
    /// <see cref="ReadInputModel.OutOfZoneCell"/> for "expect a ball"), and the
    /// approach dial. The swing itself is emergent — decided sim-side.
    /// </summary>
    [Signal]
    public delegate void ReadCommittedEventHandler(int guessType, int guessCell, double approach);

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
    public int WindupDurationMs { get; set; } = 400;

    // Playtest feedback (2026-07-07, user): at the original 700ms default the
    // 3-line reveal (call, flavor, read-tag) was unreadable — bumped so a
    // first-time player has time to actually read what happened, not just
    // see it flash. Still tunable; revisit after the next playtest.
    [Export]
    public int PitchRevealDurationMs { get; set; } = 2200;

    // 12d-3 bumps the PA-outcome hold to the design's own recommended default
    // (§5.6: "PA reveal ~1.2 s") now that the beat actually shows a PaReveal
    // chip (12d-2 only paced the log-line append against this duration).
    // Playtest feedback (2026-07-07): bumped further past that floor for the
    // same readability reason as PitchRevealDurationMs.
    [Export]
    public int PaOutcomeBeatDurationMs { get; set; } = 2400;

    [Export]
    public int NpcBeatDurationMs { get; set; } = 700;

    // 12d-3 fast-forward (§5.6, "a persistent pace toggle defaulting to
    // broadcast"): a simple speed multiplier on the sequencer's own elapsed-
    // time accumulation while FastForwardToggle is held down, rather than a
    // hold/click-to-pop input scheme — a disclosed first-pass call per the
    // doc's own "build-time feel decision" note. Every beat's exit is still
    // driven by the same elapsed-time compare, so this compresses cleanly:
    // no separate skip path, no risk of a beat never resolving.
    [Export]
    public double FastForwardSpeedMultiplier { get; set; } = 4.0;

    // 12d-2 ResultReveal copy (at_bat_presentation.md §5.1) — every branch the
    // frozen PitchResult DTO can actually report. Two contract notes from the
    // 12d-1 sign-off, built to literally: strike three renders from
    // PaEnded && Class==Strike (never the Strikes field, which never reaches
    // 3), and Foul only ever appears at a two-strike, count-preserving pitch.
    [Export]
    public string RevealBallText { get; set; } = "Ball";

    [Export]
    public string RevealWalkText { get; set; } = "Ball four — take your base";

    // Fable review (12d-2+12d-3): Class=Ball && BatterSwung is a frequent,
    // legal DTO state (the model draws the pitch class independent of
    // intent) — a chase must read as batter-negative, not fall through to
    // the take-a-ball copy.
    [Export]
    public string RevealChaseText { get; set; } = "Chased it — ball";

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

    // at_bat_read_input_model.md §3.5 approach dial — the live readout bucket
    // (Patient/Balanced/Aggressive), mirroring the old timing-slider readout
    // pattern (playtest feedback 2026-07-07: a bare slider has no on-screen
    // indication of what it controls until the reveal, itself easy to miss).
    [Export]
    public double ApproachNeutralHalfWidth { get; set; } = 0.15;

    [Export]
    public string ApproachPatientText { get; set; } = "Patient";

    [Export]
    public string ApproachBalancedText { get; set; } = "Balanced";

    [Export]
    public string ApproachAggressiveText { get; set; } = "Aggressive";

    // at_bat_read_input_model.md §4 read-grade tags — bucketed off the frozen
    // PitchResult.TypeOk/LocAcc fields, appended as a third reveal line so the
    // player learns WHY a pitch went well or badly, not just what happened.
    [Export]
    public string ReadPerfectText { get; set; } = "(read it perfectly)";

    [Export]
    public string ReadCloseText { get; set; } = "(good read)";

    [Export]
    public string ReadWrongTypeText { get; set; } = "(wrong pitch, right read)";

    [Export]
    public string ReadWrongZoneText { get; set; } = "(fooled on location)";

    [Export]
    public string ReadFooledText { get; set; } = "(completely fooled)";

    // 12d-3 §5.4 PaReveal — bigger/longer emphasis on the human's own PA
    // outcome, reusing the ResultReveal chip machinery per the doc's own
    // wording. Comma-separated, PaOutcome enum order (Out,Strikeout,Walk,
    // Single,Double,Triple,HomeRun) — the OutcomeNamesCsv precedent, but
    // broadcast-cased/punctuated per §5.4's own examples.
    [Export]
    public string PaRevealNamesCsv { get; set; } =
        "OUT,STRIKEOUT,WALK,SINGLE,DOUBLE,TRIPLE,HOME RUN!";

    /// <summary>Finishing scale of the reveal chip's entry tween — bigger than a pitch reveal's 1.0 (§5.4: "bigger scale... so a home run feels different from a groundout").</summary>
    [Export]
    public float PaRevealScale { get; set; } = 1.35f;

    // Theme variations the base-diamond panels swap between; theme-owned so
    // occupied/empty colors stay a designer decision, not a code constant.
    private static readonly StringName BaseLitVariation = "BaseLit";
    private static readonly StringName BaseDimVariation = "BaseDim";
    private static readonly StringName RevealDiamondVariation = "RevealLabelDiamond";
    private static readonly StringName RevealDirtVariation = "RevealLabelDirt";

    private const string ColorSuffix = "[/color]";

    private enum RevealAccent { Positive, Negative, Neutral }

    private enum BeatKind : byte { PitchResult, PaOutcome, NpcPa }

    /// <summary>
    /// One queued presentation beat — a pitch's truth (the frozen PitchResult
    /// DTO already carries BatterSwung/TypeOk/LocAcc, so no player-intent needs
    /// stamping in separately anymore), or an already-formatted PA/NPC log line
    /// (plus the raw outcome for the PaReveal chip / broadcast-log accent),
    /// paced in the driver's dequeue order (the causal bound
    /// at_bat_presentation.md §3 establishes).
    /// </summary>
    private readonly struct BeatEvent
    {
        public readonly BeatKind Kind;
        public readonly PitchResult Pitch;
        public readonly PaOutcome Outcome;
        public readonly string LogLine;

        private BeatEvent(BeatKind kind, in PitchResult pitch, PaOutcome outcome, string logLine)
        {
            Kind = kind;
            Pitch = pitch;
            Outcome = outcome;
            LogLine = logLine;
        }

        public static BeatEvent ForPitch(in PitchResult pitch) =>
            new(BeatKind.PitchResult, in pitch, default, string.Empty);
        public static BeatEvent ForPaOutcome(PaOutcome outcome, string logLine) =>
            new(BeatKind.PaOutcome, default, outcome, logLine);
        public static BeatEvent ForNpcPa(PaOutcome outcome, string logLine) =>
            new(BeatKind.NpcPa, default, outcome, logLine);
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
    private Button _fastballButton = null!;
    private Button _breakingButton = null!;
    private Button _offspeedButton = null!;
    private readonly Button[] _zoneCellButtons = new Button[9];
    private Button _outOfZoneButton = null!;
    private HSlider _approachSlider = null!;
    private Label _approachValueLabel = null!;
    private Button _commitButton = null!;
    private PanelContainer _revealChip = null!;
    private Label _revealLabel = null!;
    private CheckButton _fastForwardToggle = null!;

    private string[] _pitchTypeNames = System.Array.Empty<string>();
    private string[] _paRevealNames = System.Array.Empty<string>();
    private int _logLines;

    // 12d-3 broadcast-log colors (§5.5), resolved once from the same
    // RevealLabelDiamond/RevealLabelDirt theme variations the chip already
    // uses — a single source of truth for the two accents rather than a
    // second hardcoded hex pair drifting out of sync with the theme. Fable
    // review: pre-built as full "[color=#…]" prefixes at _Ready rather than
    // a Color re-formatted via ToHtml on every colored log line.
    private string _diamondColorPrefix = string.Empty;
    private string _dirtColorPrefix = string.Empty;

    // Fable review: cached off the FastForwardToggle's Toggled signal rather
    // than polling ButtonPressed every _Process frame.
    private bool _fastForwardOn;

    // "Read the Pitch" input state (at_bat_read_input_model.md §5): the
    // player's in-progress guess before Commit is pressed. Cleared back to
    // "nothing picked" every time controls re-enable for a fresh pitch (§3
    // input gating: a stale guess must never silently answer the next pitch).
    private PitchType _selectedType;
    private bool _hasTypeSelection;
    private byte _selectedCell;
    private bool _hasZoneSelection;
    private bool _intentEnabled;
    private readonly BaseButton.ToggledEventHandler[] _zoneCellHandlers = new BaseButton.ToggledEventHandler[9];

    // The beat sequencer (12d-2).
    private readonly Queue<BeatEvent> _beatQueue = new(16);
    private bool _hasPendingSnapshot;
    private AtBatViewState _pendingSnapshot;
    private SequencerPhase _phase = SequencerPhase.Idle;
    private double _phaseElapsedMs;
    private BeatEvent _currentBeat;

    // Fable review (finding 3): the reveal chip's own live tween, killed
    // before a new one starts so an interrupted reveal (fast-forward, or a
    // fresh game's ResetSequencer) can never leave a stale tween fighting the
    // next one over modulate/scale.
    private Tween? _revealTween;

    // Fable review (finding 3, secondary symptom): PulseControl fires once
    // per changed value per snapshot — under fast-forward two snapshots can
    // land inside one pulse's 200ms window, so the same control's "scale"
    // tween must be killed before a new pulse starts, same as the reveal chip.
    private readonly Dictionary<Control, Tween> _pulseTweens = new();

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
        _fastballButton = GetNode<Button>("Layout/Controls/TypeRow/FastballButton");
        _breakingButton = GetNode<Button>("Layout/Controls/TypeRow/BreakingButton");
        _offspeedButton = GetNode<Button>("Layout/Controls/TypeRow/OffspeedButton");
        for (int i = 0; i < _zoneCellButtons.Length; i++)
        {
            _zoneCellButtons[i] = GetNode<Button>($"Layout/Controls/ZoneRow/ZoneGrid/Cell{i}");
        }
        _outOfZoneButton = GetNode<Button>("Layout/Controls/ZoneRow/OutOfZoneButton");
        _approachSlider = GetNode<HSlider>("Layout/Controls/TopRow/ApproachSlider");
        _approachValueLabel = GetNode<Label>("Layout/Controls/TopRow/ApproachValueLabel");
        _commitButton = GetNode<Button>("Layout/Controls/CommitButton");
        _revealChip = GetNode<PanelContainer>("RevealOverlay/RevealChip");
        _revealLabel = GetNode<Label>("RevealOverlay/RevealChip/RevealLabel");
        _fastForwardToggle = GetNode<CheckButton>("Layout/Controls/TopRow/FastForwardToggle");

        _fastballButton.Toggled += OnFastballToggled;
        _breakingButton.Toggled += OnBreakingToggled;
        _offspeedButton.Toggled += OnOffspeedToggled;
        for (int i = 0; i < _zoneCellButtons.Length; i++)
        {
            byte cell = (byte)i;
            BaseButton.ToggledEventHandler handler = pressed => OnZoneCellToggled(pressed, cell);
            _zoneCellHandlers[i] = handler;
            _zoneCellButtons[i].Toggled += handler;
        }
        _outOfZoneButton.Toggled += OnOutOfZoneToggled;
        _commitButton.Pressed += OnCommitPressed;
        _fastForwardToggle.Toggled += OnFastForwardToggled;
        _approachSlider.ValueChanged += OnApproachSliderChanged;
        _approachValueLabel.Text = ApproachBucketText(_approachSlider.Value); // live readout — playtest feedback (2026-07-07): a bare slider had no on-screen indication of what it controlled
        _pitchTypeNames = PitchTypeNamesCsv.Split(',');
        _paRevealNames = PaRevealNamesCsv.Split(',');
        Color diamondColor = _revealLabel.GetThemeColor("font_color", RevealDiamondVariation);
        Color dirtColor = _revealLabel.GetThemeColor("font_color", RevealDirtVariation);
        _diamondColorPrefix = $"[color=#{diamondColor.ToHtml(false)}]";
        _dirtColorPrefix = $"[color=#{dirtColor.ToHtml(false)}]";
        _fastForwardOn = _fastForwardToggle.ButtonPressed;
        _playLog.Clear();
        SetIntentEnabled(false); // AwaitingInput is entered explicitly once the sequencer applies the first snapshot
    }

    public override void _ExitTree()
    {
        _fastballButton.Toggled -= OnFastballToggled;
        _breakingButton.Toggled -= OnBreakingToggled;
        _offspeedButton.Toggled -= OnOffspeedToggled;
        for (int i = 0; i < _zoneCellButtons.Length; i++)
        {
            _zoneCellButtons[i].Toggled -= _zoneCellHandlers[i];
        }
        _outOfZoneButton.Toggled -= OnOutOfZoneToggled;
        _commitButton.Pressed -= OnCommitPressed;
        _fastForwardToggle.Toggled -= OnFastForwardToggled;
        _approachSlider.ValueChanged -= OnApproachSliderChanged;
    }

    private void OnFastForwardToggled(bool pressed) => _fastForwardOn = pressed;

    /// <summary>Live readout of the approach dial's bucket (Patient/Balanced/Aggressive) — mirrors the old timing-slider readout so the dial's effect is never invisible.</summary>
    private void OnApproachSliderChanged(double value) => _approachValueLabel.Text = ApproachBucketText(value);

    /// <summary>True once the beat queue has fully drained and the sequencer is idle — the finish-seam gate the game-end truncation fix (Fable review, finding 1) needs: the driver must not tear the view down until every queued beat has actually played, and must keep draining the bridge every frame until this flips true.</summary>
    public bool SequencerDrained => _phase == SequencerPhase.Idle && _beatQueue.Count == 0;

    private double CurrentSpeed() => _fastForwardOn ? FastForwardSpeedMultiplier : 1.0;

    private double RevealDurationMs(BeatKind kind) => kind switch
    {
        BeatKind.PitchResult => PitchRevealDurationMs,
        BeatKind.PaOutcome => PaOutcomeBeatDurationMs,
        _ => NpcBeatDurationMs,
    };

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

        // 12d-3 fast-forward (§5.6): scale the sequencer's own elapsed-time
        // accumulation while held — every beat exit is this same compare, so
        // there is no separate skip path and nothing to desync.
        _phaseElapsedMs += delta * 1000.0 * CurrentSpeed();
        if (_phase == SequencerPhase.Windup)
        {
            if (_phaseElapsedMs >= WindupDurationMs)
            {
                ShowPitchReveal(in _currentBeat);
                _phase = SequencerPhase.Reveal;
                _phaseElapsedMs = 0.0;
            }
            return;
        }

        // Reveal
        if (_phaseElapsedMs >= RevealDurationMs(_currentBeat.Kind))
        {
            // A chip was shown for PitchResult (mid-Windup->Reveal transition,
            // below) and for PaOutcome (at StartBeat, immediately) — NpcPa
            // never shows one, so nothing to hide.
            if (_currentBeat.Kind == BeatKind.PitchResult || _currentBeat.Kind == BeatKind.PaOutcome)
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
                // §5.4: the human's own PA outcome — a bigger/longer reveal
                // than a pitch beat, no windup (the terminal pitch's own
                // Windup/Reveal already played the guess->truth arc).
                AppendPlayLine(beat.LogLine, PaRevealAccent(beat.Outcome));
                ShowPaReveal(beat.Outcome);
                _phase = SequencerPhase.Reveal;
                _phaseElapsedMs = 0.0;
                break;
            default: // NpcPa — ambient background noise, log line only, no chip.
                AppendPlayLine(beat.LogLine, PaRevealAccent(beat.Outcome));
                _phase = SequencerPhase.Reveal;
                _phaseElapsedMs = 0.0;
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
    public void EnqueuePitchBeat(in PitchResult result) =>
        _beatQueue.Enqueue(BeatEvent.ForPitch(in result));

    /// <summary>Queues one resolved human PA's already-formatted play-log line plus its raw outcome (for the PaReveal chip + broadcast-log accent), paced like every other beat.</summary>
    public void EnqueuePaOutcomeBeat(PaOutcome outcome, string logLine) => _beatQueue.Enqueue(BeatEvent.ForPaOutcome(outcome, logLine));

    /// <summary>Queues one NPC play-by-play line plus its outcome (for the broadcast-log accent only — no reveal chip), paced like every other beat.</summary>
    public void EnqueueNpcBeat(PaOutcome outcome, string logLine) => _beatQueue.Enqueue(BeatEvent.ForNpcPa(outcome, logLine));

    /// <summary>Clears all in-flight beat/reveal state for a fresh attended game — the driver's <see cref="PlayerIntentBridge.Reset"/> mirror.</summary>
    public void ResetSequencer()
    {
        _beatQueue.Clear();
        _hasPendingSnapshot = false;
        _phase = SequencerPhase.Idle;
        _phaseElapsedMs = 0.0;
        HideReveal();
        foreach (Tween tween in _pulseTweens.Values)
        {
            if (GodotObject.IsInstanceValid(tween))
            {
                tween.Kill();
            }
        }
        _pulseTweens.Clear();
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

    /// <summary>A small scale pulse on any Control — the shared motion primitive behind the count tick, a score bump, and a newly-lit base (§5.2). Kills any tween already pulsing this same control first (Fable review: two snapshots landing inside one pulse's window under fast-forward would otherwise fight over "scale").</summary>
    private void PulseControl(Control control)
    {
        if (_pulseTweens.TryGetValue(control, out Tween? existing) && GodotObject.IsInstanceValid(existing))
        {
            existing.Kill();
        }
        control.PivotOffset = control.Size / 2f;
        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.SetSpeedScale((float)CurrentSpeed());
        tween.TweenProperty(control, "scale", new Vector2(1.25f, 1.25f), 0.08)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(control, "scale", Vector2.One, 0.12)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _pulseTweens[control] = tween;
    }

    /// <summary>The ResultReveal beat (§5.1) — the just-thrown pitch's truth, tween'd in at scale 1.0 and held for <see cref="PitchRevealDurationMs"/>.</summary>
    private void ShowPitchReveal(in BeatEvent beat)
    {
        (string chipText, string logLine, RevealAccent accent) = BuildRevealCopy(in beat.Pitch);
        ShowRevealChip(chipText, accent, PitchRevealDurationMs, 1f);
        // §5.5: the play-log is "the persistent record behind the transient
        // ResultReveal" — every pitch gets a broadcast line, not just PAs.
        AppendPlayLine(logLine, accent);
    }

    /// <summary>The PaReveal beat (§5.4) — the human's own PA outcome, reusing the ResultReveal chip machinery at <see cref="PaRevealScale"/> ("bigger scale... so a home run feels different from a groundout") held for <see cref="PaOutcomeBeatDurationMs"/>.</summary>
    private void ShowPaReveal(PaOutcome outcome)
    {
        string text = ResolveCsvName((int)outcome, _paRevealNames, outcome.ToString());
        ShowRevealChip(text, PaRevealAccent(outcome), PaOutcomeBeatDurationMs, PaRevealScale);
    }

    /// <summary>Shared reveal-chip presentation: fade+scale in, hold, fade out — the primitive both the per-pitch and per-PA reveals ride, differing only in copy/accent/duration/finishing scale. Kills any tween already running on the chip first, and runs at the sequencer's own current speed (Fable review, finding 3): the two clocks — beat-exit timing and the tween — must be the same clock, or fast-forward desyncs them and orphaned tweens fight the next reveal.</summary>
    private void ShowRevealChip(string text, RevealAccent accent, int durationMs, float finishingScale)
    {
        _revealTween?.Kill();

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
        // Fable review: Size still reflects the PREVIOUS text at this point
        // (the label hasn't relaid-out yet) — defer the pivot read to after
        // this frame's layout pass so the scale-in pivots on-center.
        Callable.From(UpdateRevealChipPivot).CallDeferred();

        double fadeSec = System.Math.Min(0.15, durationMs / 3000.0);
        double holdSec = System.Math.Max(0.0, durationMs / 1000.0 - fadeSec * 2.0);
        Vector2 target = new(finishingScale, finishingScale);

        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.SetSpeedScale((float)CurrentSpeed());
        tween.TweenProperty(_revealChip, "modulate:a", 1.0, fadeSec);
        tween.TweenProperty(_revealChip, "scale", target, fadeSec)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.Chain().TweenInterval(holdSec);
        tween.Chain().TweenProperty(_revealChip, "modulate:a", 0.0, fadeSec);
        _revealTween = tween;
    }

    private void UpdateRevealChipPivot() => _revealChip.PivotOffset = _revealChip.Size / 2f;

    private void HideReveal()
    {
        _revealTween?.Kill();
        _revealTween = null;
        _revealChip.Visible = false;
    }

    /// <summary>§5.4's diamond/dirt split: hit/walk/HR = diamond (batter-positive), out/K = dirt (batter-negative).</summary>
    private static RevealAccent PaRevealAccent(PaOutcome outcome) => outcome switch
    {
        PaOutcome.Out or PaOutcome.Strikeout => RevealAccent.Negative,
        _ => RevealAccent.Positive,
    };

    /// <summary>at_bat_presentation.md §5.1's table, narrowed to exactly what the frozen PitchResult DTO reports, plus the read-grade tag (at_bat_read_input_model.md §4: TypeOk/LocAcc) on every pitch. Returns the chip's full multi-line text and a single-line broadcast-log echo.</summary>
    private (string ChipText, string LogLine, RevealAccent Accent) BuildRevealCopy(in PitchResult result)
    {
        string call;
        RevealAccent accent;
        switch (result.Class)
        {
            case PitchClass.Ball:
                if (result.PaEnded)
                {
                    call = RevealWalkText;
                    accent = RevealAccent.Positive;
                }
                else if (result.BatterSwung)
                {
                    // §5.1's "Ball, swung (chase)" row — the model draws the
                    // pitch class independent of intent, so a swung ball is a
                    // frequent legal state, not an edge case.
                    call = RevealChaseText;
                    accent = RevealAccent.Negative;
                }
                else
                {
                    call = RevealBallText;
                    accent = RevealAccent.Positive;
                }
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

        string logLine = call;

        string flavor = string.Format(
            RevealFlavorFormat, PitchTypeName(result.Type), result.InZone ? RevealInZoneText : RevealOutOfZoneText);
        string text = call + "\n" + flavor + "\n" + ReadGradeTag(result.TypeOk, result.LocAcc);

        return (text, logLine, accent);
    }

    /// <summary>Buckets the frozen PitchResult.TypeOk/LocAcc (at_bat_read_input_model.md §3.1) into a short read-grade tag — a perfect read (right type, exact/correct-call location) reads distinctly from a wrong-zone fooling or merely an imprecise-but-right-idea read.</summary>
    private string ReadGradeTag(bool typeOk, double locAcc)
    {
        if (typeOk && locAcc >= 1.0)
        {
            return ReadPerfectText;
        }
        if (!typeOk && locAcc <= 0.0)
        {
            return ReadFooledText;
        }
        if (locAcc <= 0.0)
        {
            return ReadWrongZoneText;
        }
        if (!typeOk)
        {
            return ReadWrongTypeText;
        }
        return ReadCloseText;
    }

    /// <summary>Buckets the approach dial ∈ [-1,+1] into Patient/Balanced/Aggressive — a symmetric tolerance band around 0 sized by <see cref="ApproachNeutralHalfWidth"/>.</summary>
    private string ApproachBucketText(double approach)
    {
        if (approach < -ApproachNeutralHalfWidth)
        {
            return ApproachPatientText;
        }
        if (approach > ApproachNeutralHalfWidth)
        {
            return ApproachAggressiveText;
        }
        return ApproachBalancedText;
    }

    /// <summary>Appends one plain (neutral) play-by-play line — the game-final summary, which has no batter-positive/negative accent.</summary>
    public void AppendPlayLine(string line) => AppendPlayLineRaw(line, string.Empty, string.Empty);

    /// <summary>§5.5: "the play-log becomes a broadcast, not a debug print" — colors the line via the same diamond/dirt theme accents the reveal chip uses, through BBCode (PlayLog has bbcode_enabled=true).</summary>
    private void AppendPlayLine(string line, RevealAccent accent)
    {
        if (accent == RevealAccent.Neutral)
        {
            AppendPlayLineRaw(line, string.Empty, string.Empty);
            return;
        }
        string prefix = accent == RevealAccent.Positive ? _diamondColorPrefix : _dirtColorPrefix;
        AppendPlayLineRaw(line, prefix, ColorSuffix);
    }

    /// <summary>The one place any play-log line reaches the (now-BBCode) RichTextLabel — escaping runs here unconditionally (Fable review, secondary finding a: the neutral path and the public single-arg AppendPlayLine used to reach the log unescaped, only the colored branch escaped).</summary>
    private void AppendPlayLineRaw(string line, string prefix, string suffix)
    {
        if (_logLines >= MaxLogLines)
        {
            _playLog.Clear();
            _logLines = 0;
        }
        _playLog.AppendText(prefix + line.Replace("[", "[lb]") + suffix);
        _playLog.AppendText("\n");
        _logLines++;
    }

    private void OnFastballToggled(bool pressed) => OnTypeToggled(pressed, PitchType.Fastball);
    private void OnBreakingToggled(bool pressed) => OnTypeToggled(pressed, PitchType.Breaking);
    private void OnOffspeedToggled(bool pressed) => OnTypeToggled(pressed, PitchType.Offspeed);

    private void OnTypeToggled(bool pressed, PitchType type)
    {
        if (!pressed)
        {
            return; // the group's own unpress of the previous button — nothing to record
        }
        _selectedType = type;
        _hasTypeSelection = true;
        UpdateCommitEnabled();
    }

    private void OnZoneCellToggled(bool pressed, byte cell)
    {
        if (!pressed)
        {
            return;
        }
        _selectedCell = cell;
        _hasZoneSelection = true;
        UpdateCommitEnabled();
    }

    private void OnOutOfZoneToggled(bool pressed)
    {
        if (!pressed)
        {
            return;
        }
        _selectedCell = ReadInputModel.OutOfZoneCell;
        _hasZoneSelection = true;
        UpdateCommitEnabled();
    }

    private void OnCommitPressed()
    {
        if (!_hasTypeSelection || !_hasZoneSelection)
        {
            return; // guarded by CommitButton.Disabled — defensive only
        }
        PitchType guessType = _selectedType;
        byte guessCell = _selectedCell;
        double approach = _approachSlider.Value;
        SetIntentEnabled(false);
        EmitSignal(SignalName.ReadCommitted, (int)guessType, (int)guessCell, approach);
    }

    /// <summary>Read controls lock while the sim resolves the pitch off the UI thread; re-enabling clears the prior guess so a stale type/cell can never silently answer the next pitch (the approach dial is a standing stance, not a per-pitch reset).</summary>
    private void SetIntentEnabled(bool enabled)
    {
        _intentEnabled = enabled;
        _fastballButton.Disabled = !enabled;
        _breakingButton.Disabled = !enabled;
        _offspeedButton.Disabled = !enabled;
        foreach (Button cell in _zoneCellButtons)
        {
            cell.Disabled = !enabled;
        }
        _outOfZoneButton.Disabled = !enabled;
        _approachSlider.Editable = enabled;
        if (enabled)
        {
            ClearReadSelections();
        }
        UpdateCommitEnabled();
    }

    private void ClearReadSelections()
    {
        _fastballButton.SetPressedNoSignal(false);
        _breakingButton.SetPressedNoSignal(false);
        _offspeedButton.SetPressedNoSignal(false);
        foreach (Button cell in _zoneCellButtons)
        {
            cell.SetPressedNoSignal(false);
        }
        _outOfZoneButton.SetPressedNoSignal(false);
        _hasTypeSelection = false;
        _hasZoneSelection = false;
    }

    private void UpdateCommitEnabled() =>
        _commitButton.Disabled = !(_intentEnabled && _hasTypeSelection && _hasZoneSelection);

    /// <summary>Index-with-fallback lookup shared by every CSV-backed name table on this view (Fable review, secondary finding e: ShowPaReveal used to inline its own copy of this pattern).</summary>
    private static string ResolveCsvName(int index, string[] names, string fallback) =>
        index >= 0 && index < names.Length ? names[index] : fallback;

    private string PitchTypeName(PitchType type) => ResolveCsvName((int)type, _pitchTypeNames, type.ToString());
}
