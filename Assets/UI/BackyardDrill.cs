using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Slice 13-6d (brief §4.4 item 3, D4-A): the Backyard Drill — three reps on a
/// timing meter, by the child's training focus (hitting: a front-toss window,
/// pitching: a release window, fielding: a glove window). A modal the Burner
/// Phone's Family tab opens on a child's Train button.
///
/// Presentation only. The one thing it sends the engine is the drill's score in
/// [0, 1] through <see cref="GameManager.PlayDrill"/>; the engine alone draws the
/// session's result from its own persisted seed and turns the score into a capped,
/// centered tilt (0.5 is no tilt, so skipping and autopilot are never punished). The
/// drill's own randomness — where the window sits each rep, where the marker
/// starts — is a private <see cref="RandomNumberGenerator"/> that nothing in the
/// simulation ever reads or seeds, and it never writes the database. Skip sends the
/// neutral 0.5; Back leaves without spending a session.
///
/// Reduce motion (the arcade prototype's setting, kept on this device in
/// user://settings.cfg): nothing moves. Each rep shows the window and three calls
/// (early, on time, late) on a still meter, and the rep is scored by the same
/// <see cref="ScoreRep"/> the moving meter uses. While the drill is open the game
/// clock is soft-paused and its previous pace restored on close.
///
/// Every string is a <c>ui.family.drill.*</c> strings-table key; keyboard and
/// controller reach is every button (the Now! button takes the press on
/// accept). Node paths verified against BackyardDrill.tscn (authored together
/// with this script).
/// </summary>
public sealed partial class BackyardDrill : Control
{
    /// <summary>The drill is over (a result was shown and dismissed, or it was left): the phone frees it and refreshes the Family tab.</summary>
    [Signal]
    public delegate void ClosedEventHandler(string childId);

    public const int RepCount = 3;

    private const string SettingsPath = "user://settings.cfg";
    private const string SettingsSection = "accessibility";
    private const string ReduceMotionKey = "reduce_motion";

    /// <summary>Half-width of the visible window, as a share of the meter (a rep scored 1 - 0.08/0.5 or better is inside it).</summary>
    private const double WindowHalfWidth = 0.08;

    /// <summary>Pause between a rep's result and the next rep.</summary>
    private const double RepPauseSeconds = 0.8;

    // The three calls of the reduce-motion variant sit at fixed places on the meter.
    private static readonly double[] CallPositions = { 0.2, 0.5, 0.8 };

    private enum Phase
    {
        Intro,
        Playing,
        RepResult,
        Result,
    }

    private Label _headingLabel = null!;
    private Label _childLabel = null!;
    private Label _instructionLabel = null!;
    private CheckButton _calmCheck = null!;
    private Label _repLabel = null!;
    private Control _meterBox = null!;
    private ColorRect _targetBand = null!;
    private ColorRect _marker = null!;
    private Label _repResultLabel = null!;
    private HBoxContainer _choicesRow = null!;
    private Button _earlyButton = null!;
    private Button _onTimeButton = null!;
    private Button _lateButton = null!;
    private HBoxContainer _buttonsRow = null!;
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _skipButton = null!;
    private Button _backButton = null!;
    private VBoxContainer _resultBox = null!;
    private Label _resultHeadingLabel = null!;
    private Label _resultBodyLabel = null!;
    private Label _resultDetailLabel = null!;
    private Button _doneButton = null!;

    // The drill's own visual randomness: never the simulation's, never seeded from it.
    private readonly RandomNumberGenerator _visualRng = new();

    private readonly List<double> _repScores = new(RepCount);

    private GameClock _clock = null!;
    private TimeSpeed _resumeSpeed;
    private bool _clockHeld;

    private string _childId = string.Empty;
    private string _childName = string.Empty;
    private Phase _phase = Phase.Intro;
    private bool _calm;
    private bool _submitted;
    private TrainingFocus _focus;
    private int _rep;
    private double _target;
    private double _markerPhase;
    private double _markerSpeed;
    private double _markerPos = 0.5;
    private double _repTimer;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _headingLabel = GetNode<Label>("Card/CardLayout/HeadingLabel");
        _childLabel = GetNode<Label>("Card/CardLayout/ChildLabel");
        _instructionLabel = GetNode<Label>("Card/CardLayout/InstructionLabel");
        _calmCheck = GetNode<CheckButton>("Card/CardLayout/CalmCheck");
        _repLabel = GetNode<Label>("Card/CardLayout/RepLabel");
        _meterBox = GetNode<Control>("Card/CardLayout/MeterBox");
        _targetBand = GetNode<ColorRect>("Card/CardLayout/MeterBox/TargetBand");
        _marker = GetNode<ColorRect>("Card/CardLayout/MeterBox/Marker");
        _repResultLabel = GetNode<Label>("Card/CardLayout/RepResultLabel");
        _choicesRow = GetNode<HBoxContainer>("Card/CardLayout/ChoicesRow");
        _earlyButton = GetNode<Button>("Card/CardLayout/ChoicesRow/EarlyButton");
        _onTimeButton = GetNode<Button>("Card/CardLayout/ChoicesRow/OnTimeButton");
        _lateButton = GetNode<Button>("Card/CardLayout/ChoicesRow/LateButton");
        _buttonsRow = GetNode<HBoxContainer>("Card/CardLayout/ButtonsRow");
        _startButton = GetNode<Button>("Card/CardLayout/ButtonsRow/StartButton");
        _stopButton = GetNode<Button>("Card/CardLayout/ButtonsRow/StopButton");
        _skipButton = GetNode<Button>("Card/CardLayout/ButtonsRow/SkipButton");
        _backButton = GetNode<Button>("Card/CardLayout/ButtonsRow/BackButton");
        _resultBox = GetNode<VBoxContainer>("Card/CardLayout/ResultBox");
        _resultHeadingLabel = GetNode<Label>("Card/CardLayout/ResultBox/ResultHeadingLabel");
        _resultBodyLabel = GetNode<Label>("Card/CardLayout/ResultBox/ResultBodyLabel");
        _resultDetailLabel = GetNode<Label>("Card/CardLayout/ResultBox/ResultDetailLabel");
        _doneButton = GetNode<Button>("Card/CardLayout/ResultBox/DoneButton");

        _targetBand.Color = UiColors.Success with { A = 0.55f };
        _marker.Color = UiColors.Warning;
        _stopButton.ActionMode = BaseButton.ActionModeEnum.Press; // timing: take the press, not the release

        _headingLabel.Text = Tr("ui.family.drill.heading");
        _calmCheck.Text = Tr("ui.family.drill.calm");
        _startButton.Text = Tr("ui.family.drill.start");
        _stopButton.Text = Tr("ui.family.drill.stop");
        _skipButton.Text = Tr("ui.family.drill.skip");
        _backButton.Text = Tr("ui.family.drill.back");
        _doneButton.Text = Tr("ui.family.drill.finish");
        _earlyButton.Text = Tr("ui.family.drill.choice.early");
        _onTimeButton.Text = Tr("ui.family.drill.choice.ontime");
        _lateButton.Text = Tr("ui.family.drill.choice.late");

        _calm = LoadReduceMotion();
        _calmCheck.SetPressedNoSignal(_calm);

        _calmCheck.Toggled += OnCalmToggled;
        _startButton.Pressed += OnStartPressed;
        _stopButton.Pressed += OnStopPressed;
        _skipButton.Pressed += OnSkipPressed;
        _backButton.Pressed += OnBackPressed;
        _doneButton.Pressed += OnDonePressed;
        _earlyButton.Pressed += OnEarlyPressed;
        _onTimeButton.Pressed += OnOnTimePressed;
        _lateButton.Pressed += OnLatePressed;
        _meterBox.Resized += OnMeterResized;

        _visualRng.Randomize();
    }

    public override void _ExitTree()
    {
        _calmCheck.Toggled -= OnCalmToggled;
        _startButton.Pressed -= OnStartPressed;
        _stopButton.Pressed -= OnStopPressed;
        _skipButton.Pressed -= OnSkipPressed;
        _backButton.Pressed -= OnBackPressed;
        _doneButton.Pressed -= OnDonePressed;
        _earlyButton.Pressed -= OnEarlyPressed;
        _onTimeButton.Pressed -= OnOnTimePressed;
        _lateButton.Pressed -= OnLatePressed;
        _meterBox.Resized -= OnMeterResized;
        ReleaseClock();
    }

    /// <summary>The phone's entry: opens the drill for a child and soft-pauses the game clock.</summary>
    public void Begin(string childId)
    {
        GameManager gm = GameManager.Instance!;
        _childId = childId;
        _childName = gm.Players.TryGetById(childId, out PlayerRow child) ? child.FirstName : string.Empty;

        _clock = gm.TimeOfDay;
        if (!_clock.IsPaused)
        {
            _resumeSpeed = _clock.Speed;
            _clock.Speed = TimeSpeed.Paused;
            _clockHeld = true;
        }

        TrainingPlan plan = gm.Training.GetPlan(childId);
        _childLabel.Text = string.Format(
            Tr("ui.family.drill.child"), _childName, FocusName(plan.Focus), IntensityName(plan.Intensity));
        _markerSpeed = plan.Focus switch
        {
            TrainingFocus.Hitting => 1.0,
            TrainingFocus.Pitching => 0.85,
            _ => 0.7,
        };
        _focus = plan.Focus;
        SetIntroText(_focus);
        EnterPhase(Phase.Intro);

        if (gm.CanTrainChild(childId) != TrainingBlock.None)
        {
            ShowUnavailable();
        }
    }

    /// <summary>One rep's score in [0, 1]: 1 at the window's center, falling to 0 half a meter away. A player stopping at random averages about 0.46, just under the neutral 0.5.</summary>
    public static double ScoreRep(double position, double target) =>
        Math.Clamp(1.0 - Math.Abs(position - target) / 0.5, 0.0, 1.0);

    /// <summary>The drill's score: the mean of its reps (0.5 for no reps), in [0, 1].</summary>
    public static double ScoreDrill(IReadOnlyList<double> reps)
    {
        if (reps.Count == 0)
        {
            return 0.5;
        }
        double sum = 0.0;
        foreach (double rep in reps)
        {
            sum += rep;
        }
        return Math.Clamp(sum / reps.Count, 0.0, 1.0);
    }

    public override void _Process(double delta)
    {
        if (_phase == Phase.Playing && !_calm)
        {
            _markerPhase += delta * _markerSpeed;
            _markerPos = Mathf.PingPong((float)_markerPhase, 1.0f);
            PlaceMarker(_markerPos);
        }
        else if (_phase == Phase.RepResult)
        {
            _repTimer -= delta;
            if (_repTimer <= 0.0)
            {
                NextRepOrFinish();
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    // ------------------------------------------------------------------
    // Flow
    // ------------------------------------------------------------------

    private void EnterPhase(Phase phase)
    {
        _phase = phase;
        bool intro = phase == Phase.Intro;
        bool playing = phase is Phase.Playing or Phase.RepResult;
        bool result = phase == Phase.Result;
        bool motionMeter = playing && !_calm;

        _instructionLabel.Visible = intro;
        _calmCheck.Visible = intro;
        _repLabel.Visible = playing;
        _meterBox.Visible = intro || playing;
        _repResultLabel.Visible = playing;
        _choicesRow.Visible = playing && _calm;
        _buttonsRow.Visible = !result;
        _startButton.Visible = intro;
        _stopButton.Visible = motionMeter;
        _skipButton.Visible = !result;
        _backButton.Visible = !result;
        _resultBox.Visible = result;
        _marker.Visible = playing && !_calm;
        _targetBand.Visible = playing;

        // A modal's buttons are inert while a rep's result is showing, so a double press can't skip one.
        _stopButton.Disabled = phase == Phase.RepResult;
        _earlyButton.Disabled = phase == Phase.RepResult;
        _onTimeButton.Disabled = phase == Phase.RepResult;
        _lateButton.Disabled = phase == Phase.RepResult;

        LinkFocus();
        if (intro)
        {
            _startButton.GrabFocus();
        }
        else if (phase == Phase.Playing)
        {
            (_calm ? _onTimeButton : _stopButton).GrabFocus();
        }
        else if (result)
        {
            _doneButton.GrabFocus();
        }
    }

    /// <summary>Keyboard/controller reach: the visible controls form one ring, in reading order.</summary>
    private void LinkFocus()
    {
        var ring = new List<Control>(8);
        void Add(Control control)
        {
            if (control.IsVisibleInTree())
            {
                ring.Add(control);
            }
        }
        Add(_calmCheck);
        Add(_earlyButton);
        Add(_onTimeButton);
        Add(_lateButton);
        Add(_startButton);
        Add(_stopButton);
        Add(_skipButton);
        Add(_backButton);
        Add(_doneButton);
        for (int i = 0; i < ring.Count; i++)
        {
            Control prev = ring[(i + ring.Count - 1) % ring.Count];
            Control next = ring[(i + 1) % ring.Count];
            ring[i].FocusNeighborLeft = prev.GetPath();
            ring[i].FocusNeighborTop = prev.GetPath();
            ring[i].FocusNeighborRight = next.GetPath();
            ring[i].FocusNeighborBottom = next.GetPath();
        }
    }

    private void OnStartPressed()
    {
        if (_phase != Phase.Intro || _submitted)
        {
            return;
        }
        _repScores.Clear();
        _rep = 0;
        StartRep();
    }

    private void StartRep()
    {
        _rep++;
        _repLabel.Text = string.Format(Tr("ui.family.drill.rep"), _rep, RepCount);
        _repResultLabel.Text = string.Empty;
        // Calm calls land at 0.2, 0.5 and 0.8, so its window stays where one of them is within 0.15.
        _target = _calm ? 0.2 + _visualRng.Randf() * 0.6 : 0.25 + _visualRng.Randf() * 0.5;
        _markerPhase = _visualRng.Randf() * 2.0f;
        _markerPos = Mathf.PingPong((float)_markerPhase, 1.0f);
        EnterPhase(Phase.Playing);
        UpdateMeter();
    }

    private void OnStopPressed()
    {
        if (_phase == Phase.Playing && !_calm)
        {
            FinishRep(_markerPos);
        }
    }

    private void OnEarlyPressed() => OnCallPressed(0);

    private void OnOnTimePressed() => OnCallPressed(1);

    private void OnLatePressed() => OnCallPressed(2);

    private void OnCallPressed(int call)
    {
        if (_phase == Phase.Playing && _calm)
        {
            _markerPos = CallPositions[call];
            PlaceMarker(_markerPos);
            FinishRep(_markerPos);
        }
    }

    private void FinishRep(double position)
    {
        double score = ScoreRep(position, _target);
        _repScores.Add(score);
        _repResultLabel.Text = score >= 1.0 - WindowHalfWidth / 0.5
            ? Tr("ui.family.drill.rep.great")
            : score >= 0.6
                ? Tr("ui.family.drill.rep.good")
                : Tr("ui.family.drill.rep.miss");
        _repTimer = RepPauseSeconds;
        EnterPhase(Phase.RepResult);
        _marker.Visible = true; // freeze the marker where the stop landed, in either variant
    }

    private void NextRepOrFinish()
    {
        _repTimer = double.MaxValue; // one transition per rep
        if (_rep < RepCount)
        {
            StartRep();
        }
        else
        {
            Submit(ScoreDrill(_repScores), skipped: false);
        }
    }

    private void OnSkipPressed()
    {
        if (_phase != Phase.Result && !_submitted)
        {
            Submit(0.5, skipped: true); // a skipped drill is the neutral score: no tilt
        }
    }

    private void OnBackPressed()
    {
        if (_phase != Phase.Result)
        {
            Close(); // leaving spends nothing
        }
    }

    private void OnDonePressed() => Close();

    private void OnCalmToggled(bool on)
    {
        _calm = on;
        SaveReduceMotion(on);
        SetIntroText(_focus); // the intro line names the variant
    }

    // ------------------------------------------------------------------
    // The one intent
    // ------------------------------------------------------------------

    /// <summary>
    /// The drill's single call into the engine: the session, with the drill's score
    /// in [0, 1] (0.5 when skipped). The returned result is what the card shows;
    /// nothing here ever rolls it. A refusal (no session left, no longer a child
    /// of the avatar) shows the unavailable card instead of throwing.
    /// </summary>
    private void Submit(double score, bool skipped)
    {
        if (_submitted)
        {
            return;
        }
        _submitted = true;
        GameManager gm = GameManager.Instance!;
        TrainingSessionResult result;
        try
        {
            result = gm.PlayDrill(_childId, Math.Clamp(score, 0.0, 1.0));
        }
        catch (InvalidOperationException)
        {
            ShowUnavailable();
            return;
        }

        bool win = result.Outcome.Success;
        _resultHeadingLabel.Text = Tr(win ? "ui.family.drill.result.win" : "ui.family.drill.result.lose");
        _resultBodyLabel.Text = string.Format(
            Tr(win ? "ui.family.drill.result.win.body" : "ui.family.drill.result.lose.body"), _childName);
        string scoreLine = skipped
            ? Tr("ui.family.drill.result.skipped")
            : string.Format(Tr("ui.family.drill.result.score"), (int)Math.Round(score * 100.0));
        string detail = scoreLine + "\n" + string.Format(Tr("ui.family.drill.result.odds"), result.Outcome.OddsPp);
        if (!win && result.Intensity == TrainingIntensity.Hard)
        {
            detail += "\n" + Tr("ui.family.drill.result.hard_fail");
        }
        _resultDetailLabel.Text = detail;
        EnterPhase(Phase.Result);
    }

    private void ShowUnavailable()
    {
        _submitted = true;
        _resultHeadingLabel.Text = Tr("ui.family.drill.heading");
        _resultBodyLabel.Text = Tr("ui.family.drill.nosession");
        _resultDetailLabel.Text = string.Empty;
        EnterPhase(Phase.Result);
    }

    private void Close()
    {
        ReleaseClock();
        EmitSignal(SignalName.Closed, _childId);
    }

    // ------------------------------------------------------------------
    // Meter, clock and device setting
    // ------------------------------------------------------------------

    private void OnMeterResized() => UpdateMeter();

    private void UpdateMeter()
    {
        float width = _meterBox.Size.X;
        float height = _meterBox.Size.Y;
        float bandWidth = (float)(2.0 * WindowHalfWidth) * width;
        _targetBand.Position = new Vector2((float)_target * width - bandWidth / 2f, 0f);
        _targetBand.Size = new Vector2(bandWidth, height);
        _marker.Size = new Vector2(_marker.Size.X, height);
        PlaceMarker(_markerPos);
    }

    private void PlaceMarker(double position)
    {
        float travel = Math.Max(0f, _meterBox.Size.X - _marker.Size.X);
        _marker.Position = new Vector2((float)position * travel, 0f);
    }

    private void ReleaseClock()
    {
        // Only undo our own pause: if the player resumed the clock meanwhile, leave it alone.
        if (_clockHeld && _clock is not null && _clock.IsPaused)
        {
            _clock.Speed = _resumeSpeed;
        }
        _clockHeld = false;
    }

    private void SetIntroText(TrainingFocus focus)
    {
        _instructionLabel.Text = _calm
            ? Tr("ui.family.drill.intro.calm")
            : focus switch
            {
                TrainingFocus.Hitting => Tr("ui.family.drill.intro.hitting"),
                TrainingFocus.Pitching => Tr("ui.family.drill.intro.pitching"),
                _ => Tr("ui.family.drill.intro.fielding"),
            };
    }

    private string FocusName(TrainingFocus focus) => focus switch
    {
        TrainingFocus.Hitting => Tr("ui.family.plan.focus.hitting"),
        TrainingFocus.Pitching => Tr("ui.family.plan.focus.pitching"),
        _ => Tr("ui.family.plan.focus.fielding"),
    };

    private string IntensityName(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Easy => Tr("ui.family.plan.intensity.easy"),
        TrainingIntensity.Normal => Tr("ui.family.plan.intensity.normal"),
        _ => Tr("ui.family.plan.intensity.hard"),
    };

    private static bool LoadReduceMotion()
    {
        var config = new ConfigFile();
        return config.Load(SettingsPath) == Error.Ok && (bool)config.GetValue(SettingsSection, ReduceMotionKey, false);
    }

    private static void SaveReduceMotion(bool on)
    {
        var config = new ConfigFile();
        config.Load(SettingsPath); // an absent file is fine: we set our key and keep any others
        config.SetValue(SettingsSection, ReduceMotionKey, on);
        Error err = config.Save(SettingsPath);
        if (err != Error.Ok)
        {
            GD.PushWarning($"BackyardDrill: failed to save {SettingsPath} ({err}).");
        }
    }
}
