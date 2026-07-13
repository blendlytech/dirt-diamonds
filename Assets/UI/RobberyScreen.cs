using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (permanent sibling under Main, per Main.tscn) for an
/// armed Robbery <see cref="PendingHustleSession"/> (docs/design/hustle_minigames_depth_pass.md
/// §5, sub-slices R-2/R-3): the four-stage sequence (Case → Approach → Execute,
/// with the live press-your-luck beat → Getaway) is driven entirely through the
/// pure <see cref="RobberyHustle"/> resolver, entirely in memory, and only
/// reaches the database on <see cref="OnDonePressed"/> via
/// <see cref="HustleService.ApplyRobberyResolution"/> — an abandoned
/// mid-session run (the day advances before Done is pressed) simply evaporates
/// with nothing ever written, the same forfeit discipline as
/// <see cref="NarcoticsHustleScreen"/>/<see cref="FencingScreen"/> (INV-1).
/// Node paths verified against RobberyScreen.tscn (godot_scene_mapper) before
/// this script was written.
/// </summary>
public sealed partial class RobberyScreen : Control
{
    [Export]
    public string TargetFormat { get; set; } = "{0} — score ${1:F0}, heat +{2}";

    [Export]
    public string CrewUnavailableText { get; set; } = "(no crew rep available)";

    [Export]
    public string GrabbedFormat { get; set; } = "You've got ${0:F0} in hand. Push for the rest, or run?";

    [Export]
    public string BustedFormat { get; set; } = "Busted! Bail/legal fees ${0:F0}. Detection +{1}, Stress +{2:F0}.";

    [Export]
    public string ResolvedFormat { get; set; } =
        "Clean getaway. Net funds {0:+0;-0;0}. Detection {1:+0;-0;0}, Health {2:+0;-0;0}, Stress {3:+0;-0;0:F0}.";

    private Label _statusLabel = null!;

    private VBoxContainer _casePanel = null!;
    private Button _storeButton = null!;
    private Button _bookieButton = null!;
    private Button _warehouseButton = null!;
    private CheckBox _caseItCheckBox = null!;

    private VBoxContainer _approachPanel = null!;
    private Label _approachTargetLabel = null!;
    private Button _soloQuietButton = null!;
    private Button _strongArmButton = null!;
    private Button _crewButton = null!;

    private VBoxContainer _executePanel = null!;
    private Button _executeButton = null!;

    private VBoxContainer _grabbedPanel = null!;
    private Label _grabbedLabel = null!;
    private Button _pressLuckButton = null!;
    private Button _grabAndRunButton = null!;

    private VBoxContainer _getawayPanel = null!;
    private Button _getawayButton = null!;

    private VBoxContainer _resultPanel = null!;
    private Label _resultLabel = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private RobberyContext _ctx;
    private RngState _rng;
    private RobberyState _state;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("Panel/Layout/StatusLabel");

        _casePanel = GetNode<VBoxContainer>("Panel/Layout/CasePanel");
        _storeButton = GetNode<Button>("Panel/Layout/CasePanel/StoreButton");
        _bookieButton = GetNode<Button>("Panel/Layout/CasePanel/BookieButton");
        _warehouseButton = GetNode<Button>("Panel/Layout/CasePanel/WarehouseButton");
        _caseItCheckBox = GetNode<CheckBox>("Panel/Layout/CasePanel/CaseItCheckBox");

        _approachPanel = GetNode<VBoxContainer>("Panel/Layout/ApproachPanel");
        _approachTargetLabel = GetNode<Label>("Panel/Layout/ApproachPanel/ApproachTargetLabel");
        _soloQuietButton = GetNode<Button>("Panel/Layout/ApproachPanel/SoloQuietButton");
        _strongArmButton = GetNode<Button>("Panel/Layout/ApproachPanel/StrongArmButton");
        _crewButton = GetNode<Button>("Panel/Layout/ApproachPanel/CrewButton");

        _executePanel = GetNode<VBoxContainer>("Panel/Layout/ExecutePanel");
        _executeButton = GetNode<Button>("Panel/Layout/ExecutePanel/ExecuteButton");

        _grabbedPanel = GetNode<VBoxContainer>("Panel/Layout/GrabbedPanel");
        _grabbedLabel = GetNode<Label>("Panel/Layout/GrabbedPanel/GrabbedLabel");
        _pressLuckButton = GetNode<Button>("Panel/Layout/GrabbedPanel/PressLuckButton");
        _grabAndRunButton = GetNode<Button>("Panel/Layout/GrabbedPanel/GrabAndRunButton");

        _getawayPanel = GetNode<VBoxContainer>("Panel/Layout/GetawayPanel");
        _getawayButton = GetNode<Button>("Panel/Layout/GetawayPanel/GetawayButton");

        _resultPanel = GetNode<VBoxContainer>("Panel/Layout/ResultPanel");
        _resultLabel = GetNode<Label>("Panel/Layout/ResultPanel/ResultLabel");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _storeButton.Pressed += () => OnTargetPicked(RobberyTarget.ConvenienceStore);
        _bookieButton.Pressed += () => OnTargetPicked(RobberyTarget.BookieStash);
        _warehouseButton.Pressed += () => OnTargetPicked(RobberyTarget.Warehouse);
        _soloQuietButton.Pressed += () => OnApproachPicked(RobberyApproach.SoloQuiet);
        _strongArmButton.Pressed += () => OnApproachPicked(RobberyApproach.StrongArm);
        _crewButton.Pressed += () => OnApproachPicked(RobberyApproach.Crew);
        _executeButton.Pressed += OnExecutePressed;
        _pressLuckButton.Pressed += OnPressLuckPressed;
        _grabAndRunButton.Pressed += OnGrabAndRunPressed;
        _getawayButton.Pressed += OnGetawayPressed;
        _doneButton.Pressed += OnDonePressed;
    }

    public override void _ExitTree()
    {
        _executeButton.Pressed -= OnExecutePressed;
        _pressLuckButton.Pressed -= OnPressLuckPressed;
        _grabAndRunButton.Pressed -= OnGrabAndRunPressed;
        _getawayButton.Pressed -= OnGetawayPressed;
        _doneButton.Pressed -= OnDonePressed;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        bool isPending = gm.TryGetPendingHustleSession(out PendingHustleSession session)
            && session.Activity == WorkActivity.Robbery;
        if (!isPending)
        {
            Visible = false;
            _sessionActive = false;
            return;
        }

        if (!_sessionActive || _sessionDay != session.Day)
        {
            StartSession(gm, session.Day);
        }
        Visible = true;
    }

    private void StartSession(GameManager gm, long day)
    {
        _sessionActive = true;
        _sessionDay = day;
        _ctx = gm.Hustles.BuildRobberyContext(gm.Career.AvatarPlayerId, day);
        _rng = new RngState(unchecked((ulong)System.Environment.TickCount64) ^ 0x524F42424552UL | 1UL);

        _caseItCheckBox.ButtonPressed = false;
        _crewButton.Disabled = !_ctx.HasCrew;
        _statusLabel.Text = string.Empty;
        ShowPanel(_casePanel);
    }

    private void ShowPanel(Control panel)
    {
        _casePanel.Visible = ReferenceEquals(panel, _casePanel);
        _approachPanel.Visible = ReferenceEquals(panel, _approachPanel);
        _executePanel.Visible = ReferenceEquals(panel, _executePanel);
        _grabbedPanel.Visible = ReferenceEquals(panel, _grabbedPanel);
        _getawayPanel.Visible = ReferenceEquals(panel, _getawayPanel);
        _resultPanel.Visible = ReferenceEquals(panel, _resultPanel);
    }

    private void OnTargetPicked(RobberyTarget target)
    {
        _state = RobberyHustle.CaseTarget(in _ctx, target, _caseItCheckBox.ButtonPressed);
        _approachTargetLabel.Text = string.Format(TargetFormat, target, _state.FullScore, _state.DetectionRiskDelta);
        _statusLabel.Text = string.Empty;
        ShowPanel(_approachPanel);
    }

    private void OnApproachPicked(RobberyApproach approach)
    {
        if (approach == RobberyApproach.Crew && !_ctx.HasCrew)
        {
            _statusLabel.Text = CrewUnavailableText;
            return;
        }
        _state = RobberyHustle.ChooseApproach(in _state, in _ctx, approach);
        _statusLabel.Text = string.Empty;
        ShowPanel(_executePanel);
    }

    private void OnExecutePressed()
    {
        _state = RobberyHustle.Execute(in _state, in _ctx, ref _rng);
        Advance();
    }

    private void OnPressLuckPressed()
    {
        _state = RobberyHustle.PressLuck(in _state, in _ctx, ref _rng);
        Advance();
    }

    private void OnGrabAndRunPressed()
    {
        _state = RobberyHustle.GrabAndRun(in _state);
        Advance();
    }

    private void OnGetawayPressed()
    {
        _state = RobberyHustle.Getaway(in _state, in _ctx, ref _rng);
        Advance();
    }

    private void Advance()
    {
        switch (_state.Stage)
        {
            case RobberyStage.Grabbed:
                _grabbedLabel.Text = string.Format(GrabbedFormat, _state.PartialTake);
                ShowPanel(_grabbedPanel);
                break;
            case RobberyStage.Escaping:
                ShowPanel(_getawayPanel);
                break;
            case RobberyStage.Busted:
                _resultLabel.Text = string.Format(
                    BustedFormat, -_state.FundsDelta, _state.DetectionRiskDelta, _state.StressDelta);
                ShowPanel(_resultPanel);
                break;
            case RobberyStage.Resolved:
                _resultLabel.Text = string.Format(
                    ResolvedFormat, _state.FundsDelta, _state.DetectionRiskDelta, _state.HealthCeilingDelta, _state.StressDelta);
                ShowPanel(_resultPanel);
                break;
        }
    }

    private void OnDonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (_state.Stage is RobberyStage.Resolved or RobberyStage.Busted)
        {
            gm.Hustles.ApplyRobberyResolution(gm.Career.AvatarPlayerId, _state.ToResolution(), _sessionDay);
        }
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
