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
///
/// P0 Hustle Feel Pass (UI layer only): authored mark/approach names and
/// flavor, live odds surfaced from the resolver's existing pure
/// <see cref="RobberyHustle.ComputeExecuteSuccessProbability"/> /
/// <see cref="RobberyHustle.ComputePressLuckBustProbability"/> before each
/// commit point (the getaway deliberately shows no number — the resolver
/// exposes no getaway probability and this pass adds none), a staged
/// <see cref="ResultReveal"/> (take counts up, consequences land line-by-line),
/// and <see cref="UiSfx"/> stings. Zero resolver/sim touch: every probability
/// shown comes from a pure preview call with no RNG draw, so the harness
/// bands stay byte-identical by construction.
/// </summary>
public sealed partial class RobberyScreen : Control
{
    // --- Authored content (indexed by RobberyTarget / RobberyApproach enum order) ---

    [Export]
    public string[] TargetNames { get; set; } =
    {
        "The Kwik-Stop on Route 9",
        "Sal's Back-Room Count",
        "The Bonded Freight Depot",
        "Castellano's Jewelry Exchange",
    };

    [Export]
    public string[] TargetHooks { get; set; } =
    {
        "a sleepy corner store",
        "fight-night cash box",
        "big freight, real guards",
        "the diamond score",
    };

    [Export]
    public string[] TargetFlavor { get; set; } =
    {
        "One bored clerk, a register, and a camera that hasn't worked since spring.",
        "Fight night — the count room behind the barber shop will be heavy. And Sal knows your face.",
        "Pallets of untraceable freight behind one padlocked dock door and a walking patrol.",
        "Loose stones in a floor safe. Silent alarms, an armed guard, and everything to gain.",
    };

    [Export]
    public string[] ApproachNames { get; set; } =
    {
        "Slip In Quiet",
        "Kick the Door In",
        "Bring the Crew",
    };

    [Export]
    public string[] ApproachFlavor { get; set; } =
    {
        "Just you, gloves on, nobody gets hurt.",
        "Fast and loud — take everything they've got, maybe take a beating.",
        "A bigger haul split three ways, and the crew remembers who ran it.",
    };

    // --- Formats ---

    [Export]
    public string TargetButtonFormat { get; set; } = "{0} — {1}";

    [Export]
    public string TargetFormat { get; set; } = "{0} — up to ${1:N0} in the room, heat +{2}";

    [Export]
    public string ApproachButtonFormat { get; set; } = "{0} — {1:0}% clean";

    [Export]
    public string CrewUnavailableText { get; set; } = "Bring the Crew — (no crew rep available)";

    [Export]
    public string JobFormat { get; set; } = "You're outside {0}. {1}";

    [Export]
    public string ExecuteOddsFormat { get; set; } = "Odds you pull it off: {0:0}%";

    [Export]
    public string GrabbedFormat { get; set; } = "You've got ${0:N0} in hand. The rest is sitting right there.";

    [Export]
    public string PressOddsFormat { get; set; } = "Push for it all: {0:0}% chance the whole thing comes apart.";

    [Export]
    public string ResolvedHeadlineText { get; set; } = "CLEAN GETAWAY";

    [Export]
    public string BustedHeadlineText { get; set; } = "BUSTED.";

    [Export]
    public string TakeFormat { get; set; } = "{0:+$#,##0;-$#,##0;$0}";

    [Export]
    public string BustCostFormat { get; set; } = "-${0:N0} bail and legal fees";

    [Export]
    public string HeatLineFormat { get; set; } = "Word gets around — heat +{0}.";

    [Export]
    public string BustHeatLineFormat { get; set; } = "Booked, printed, photographed — heat +{0}.";

    [Export]
    public string StressLineFormat { get; set; } = "Your hands won't stop shaking — stress +{0:0}.";

    [Export]
    public string HealthLineFormat { get; set; } = "You took a beating in there — health {0}.";

    [Export]
    public string CrewLineFormat { get; set; } = "The crew talks you up — standing +{0}.";

    private Label _statusLabel = null!;

    private VBoxContainer _casePanel = null!;
    private Button _storeButton = null!;
    private Button _bookieButton = null!;
    private Button _warehouseButton = null!;
    private Button _jewelryButton = null!;
    private CheckBox _caseItCheckBox = null!;

    private VBoxContainer _approachPanel = null!;
    private Label _approachTargetLabel = null!;
    private Label _approachFlavorLabel = null!;
    private Button _soloQuietButton = null!;
    private Button _strongArmButton = null!;
    private Button _crewButton = null!;

    private VBoxContainer _executePanel = null!;
    private Label _jobLabel = null!;
    private Label _executeOddsLabel = null!;
    private Button _executeButton = null!;

    private VBoxContainer _grabbedPanel = null!;
    private Label _grabbedLabel = null!;
    private Label _pressOddsLabel = null!;
    private Button _pressLuckButton = null!;
    private Button _grabAndRunButton = null!;

    private VBoxContainer _getawayPanel = null!;
    private Button _getawayButton = null!;

    private VBoxContainer _resultPanel = null!;
    private Label _headlineLabel = null!;
    private Label _takeLabel = null!;
    private Label _lineA = null!;
    private Label _lineB = null!;
    private Label _lineC = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private RobberyContext _ctx;
    private RngState _rng;
    private RobberyState _state;
    private ResultReveal? _reveal;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("Panel/Layout/StatusLabel");

        _casePanel = GetNode<VBoxContainer>("Panel/Layout/CasePanel");
        _storeButton = GetNode<Button>("Panel/Layout/CasePanel/StoreButton");
        _bookieButton = GetNode<Button>("Panel/Layout/CasePanel/BookieButton");
        _warehouseButton = GetNode<Button>("Panel/Layout/CasePanel/WarehouseButton");
        _jewelryButton = GetNode<Button>("Panel/Layout/CasePanel/JewelryButton");
        _caseItCheckBox = GetNode<CheckBox>("Panel/Layout/CasePanel/CaseItCheckBox");

        _approachPanel = GetNode<VBoxContainer>("Panel/Layout/ApproachPanel");
        _approachTargetLabel = GetNode<Label>("Panel/Layout/ApproachPanel/ApproachTargetLabel");
        _approachFlavorLabel = GetNode<Label>("Panel/Layout/ApproachPanel/ApproachFlavorLabel");
        _soloQuietButton = GetNode<Button>("Panel/Layout/ApproachPanel/SoloQuietButton");
        _strongArmButton = GetNode<Button>("Panel/Layout/ApproachPanel/StrongArmButton");
        _crewButton = GetNode<Button>("Panel/Layout/ApproachPanel/CrewButton");

        _executePanel = GetNode<VBoxContainer>("Panel/Layout/ExecutePanel");
        _jobLabel = GetNode<Label>("Panel/Layout/ExecutePanel/JobLabel");
        _executeOddsLabel = GetNode<Label>("Panel/Layout/ExecutePanel/ExecuteOddsLabel");
        _executeButton = GetNode<Button>("Panel/Layout/ExecutePanel/ExecuteButton");

        _grabbedPanel = GetNode<VBoxContainer>("Panel/Layout/GrabbedPanel");
        _grabbedLabel = GetNode<Label>("Panel/Layout/GrabbedPanel/GrabbedLabel");
        _pressOddsLabel = GetNode<Label>("Panel/Layout/GrabbedPanel/PressOddsLabel");
        _pressLuckButton = GetNode<Button>("Panel/Layout/GrabbedPanel/PressLuckButton");
        _grabAndRunButton = GetNode<Button>("Panel/Layout/GrabbedPanel/GrabAndRunButton");

        _getawayPanel = GetNode<VBoxContainer>("Panel/Layout/GetawayPanel");
        _getawayButton = GetNode<Button>("Panel/Layout/GetawayPanel/GetawayButton");

        _resultPanel = GetNode<VBoxContainer>("Panel/Layout/ResultPanel");
        _headlineLabel = GetNode<Label>("Panel/Layout/ResultPanel/HeadlineLabel");
        _takeLabel = GetNode<Label>("Panel/Layout/ResultPanel/TakeLabel");
        _lineA = GetNode<Label>("Panel/Layout/ResultPanel/LineA");
        _lineB = GetNode<Label>("Panel/Layout/ResultPanel/LineB");
        _lineC = GetNode<Label>("Panel/Layout/ResultPanel/LineC");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _storeButton.Text = string.Format(TargetButtonFormat, TargetNames[0], TargetHooks[0]);
        _bookieButton.Text = string.Format(TargetButtonFormat, TargetNames[1], TargetHooks[1]);
        _warehouseButton.Text = string.Format(TargetButtonFormat, TargetNames[2], TargetHooks[2]);
        _jewelryButton.Text = string.Format(TargetButtonFormat, TargetNames[3], TargetHooks[3]);

        _storeButton.Pressed += () => OnTargetPicked(RobberyTarget.ConvenienceStore);
        _bookieButton.Pressed += () => OnTargetPicked(RobberyTarget.BookieStash);
        _warehouseButton.Pressed += () => OnTargetPicked(RobberyTarget.Warehouse);
        _jewelryButton.Pressed += () => OnTargetPicked(RobberyTarget.JewelryExchange);
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
            if (_sessionActive)
            {
                KillReveal();
            }
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

        KillReveal();
        _caseItCheckBox.ButtonPressed = false;
        _statusLabel.Text = string.Empty;
        ShowPanel(_casePanel);
    }

    private void KillReveal()
    {
        _reveal?.Kill();
        _reveal = null;
    }

    private void ShowPanel(Control panel)
    {
        _casePanel.Visible = ReferenceEquals(panel, _casePanel);
        _approachPanel.Visible = ReferenceEquals(panel, _approachPanel);
        _executePanel.Visible = ReferenceEquals(panel, _executePanel);
        _grabbedPanel.Visible = ReferenceEquals(panel, _grabbedPanel);
        _getawayPanel.Visible = ReferenceEquals(panel, _getawayPanel);
        _resultPanel.Visible = ReferenceEquals(panel, _resultPanel);
        HustleFeel.FadeIn(panel);
    }

    private void OnTargetPicked(RobberyTarget target)
    {
        UiSfx.Instance.Play(UiSound.Tap);
        _state = RobberyHustle.CaseTarget(in _ctx, target, _caseItCheckBox.ButtonPressed);
        _approachTargetLabel.Text = string.Format(
            TargetFormat, TargetNames[(int)target], _state.FullScore, _state.DetectionRiskDelta);
        _approachFlavorLabel.Text = TargetFlavor[(int)target];
        RefreshApproachButtons();
        _statusLabel.Text = string.Empty;
        ShowPanel(_approachPanel);
    }

    /// <summary>
    /// Previews each approach's execute odds on its own button before the
    /// player commits (P0: odds at the commit point). Pure preview:
    /// <see cref="RobberyHustle.ChooseApproach"/> +
    /// <see cref="RobberyHustle.ComputeExecuteSuccessProbability"/> draw no RNG
    /// and mutate nothing — the real transition only runs on the actual pick.
    /// </summary>
    private void RefreshApproachButtons()
    {
        SetApproachPreview(_soloQuietButton, RobberyApproach.SoloQuiet);
        SetApproachPreview(_strongArmButton, RobberyApproach.StrongArm);
        _crewButton.Disabled = !_ctx.HasCrew;
        if (_ctx.HasCrew)
        {
            SetApproachPreview(_crewButton, RobberyApproach.Crew);
        }
        else
        {
            _crewButton.Text = CrewUnavailableText;
        }
    }

    private void SetApproachPreview(Button button, RobberyApproach approach)
    {
        RobberyState preview = RobberyHustle.ChooseApproach(in _state, in _ctx, approach);
        double pSuccess = RobberyHustle.ComputeExecuteSuccessProbability(in preview, in _ctx);
        button.Text = string.Format(ApproachButtonFormat, ApproachNames[(int)approach], pSuccess * 100.0);
    }

    private void OnApproachPicked(RobberyApproach approach)
    {
        if (approach == RobberyApproach.Crew && !_ctx.HasCrew)
        {
            _statusLabel.Text = CrewUnavailableText;
            return;
        }
        UiSfx.Instance.Play(UiSound.Tap);
        _state = RobberyHustle.ChooseApproach(in _state, in _ctx, approach);

        double pSuccess = RobberyHustle.ComputeExecuteSuccessProbability(in _state, in _ctx);
        _jobLabel.Text = string.Format(
            JobFormat, TargetNames[(int)_state.Target], ApproachFlavor[(int)approach]);
        _executeOddsLabel.Text = string.Format(ExecuteOddsFormat, pSuccess * 100.0);
        _executeOddsLabel.AddThemeColorOverride("font_color", HustleFeel.OddsColor(pSuccess));

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
        UiSfx.Instance.Play(UiSound.Tap);
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
            {
                // The press-your-luck beat: the danger sting lands and the
                // resolver's own bust probability is on the table before the
                // player decides.
                double pBust = RobberyHustle.ComputePressLuckBustProbability(in _state, in _ctx);
                _grabbedLabel.Text = string.Format(GrabbedFormat, _state.PartialTake);
                _pressOddsLabel.Text = string.Format(PressOddsFormat, pBust * 100.0);
                _pressOddsLabel.AddThemeColorOverride("font_color", HustleFeel.RiskColor(pBust));
                UiSfx.Instance.Play(UiSound.Alert);
                ShowPanel(_grabbedPanel);
                break;
            }
            case RobberyStage.Escaping:
                ShowPanel(_getawayPanel);
                break;
            case RobberyStage.Busted:
                ShowPanel(_resultPanel);
                KillReveal();
                _reveal = ResultReveal.Begin(_resultPanel)
                    .Headline(_headlineLabel, BustedHeadlineText, UiColors.Danger, UiSound.Error)
                    .CountUp(_takeLabel, BustCostFormat, -_state.FundsDelta)
                    .Line(_lineA, string.Format(BustHeatLineFormat, _state.DetectionRiskDelta))
                    .Line(_lineB, string.Format(StressLineFormat, _state.StressDelta))
                    .Line(_lineC, _state.HealthCeilingDelta != 0
                        ? string.Format(HealthLineFormat, _state.HealthCeilingDelta)
                        : null)
                    .Footer(_doneButton);
                break;
            case RobberyStage.Resolved:
                ShowPanel(_resultPanel);
                KillReveal();
                _reveal = ResultReveal.Begin(_resultPanel)
                    .Headline(_headlineLabel, ResolvedHeadlineText, UiColors.Success, UiSound.Cash)
                    .CountUp(_takeLabel, TakeFormat, _state.FundsDelta)
                    .Line(_lineA, string.Format(HeatLineFormat, _state.DetectionRiskDelta))
                    .Line(_lineB, string.Format(StressLineFormat, _state.StressDelta))
                    .Line(_lineC, _state.CrewStandingDelta > 0
                        ? string.Format(CrewLineFormat, _state.CrewStandingDelta)
                        : null)
                    .Footer(_doneButton);
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
        UiSfx.Instance.Play(UiSound.Back);
        KillReveal();
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
