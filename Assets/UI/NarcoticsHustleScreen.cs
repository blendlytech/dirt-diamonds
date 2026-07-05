using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (permanent sibling under Main, per Main.tscn) for an
/// armed Narcotics <see cref="PendingHustleSession"/>: the whole 3-stage run
/// (Inventory Drop → Profit/Toxicity Cut → Territory Control) is player-driven
/// through the pure <see cref="NarcoticsHustle"/> resolver, entirely in
/// memory, and only reaches the database on <see cref="OnDonePressed"/> via
/// <see cref="HustleService.ApplyNarcoticsResolution"/> — an abandoned
/// mid-session run (the day advances before Done is pressed) simply evaporates
/// with nothing ever written, which IS the design's "no-deal default" forfeit
/// (docs/design/hustles_narcotics_fencing.md §2), no special-casing needed.
/// Node paths verified against NarcoticsHustleScreen.tscn (godot_scene_mapper)
/// before this script was written.
/// </summary>
public sealed partial class NarcoticsHustleScreen : Control
{
    [Export]
    public string CantAffordText { get; set; } = "Not enough funds/trust to run this hustle today.";

    [Export]
    public string BustedFormat { get; set; } = "Busted! Lost ${0:F0}. Detection +{1}, Stress +{2:F0}.";

    [Export]
    public string ResolvedFormat { get; set; } =
        "Deal done. Net funds {0:+0;-0;0}. Detection {1:+0;-0;0}, Health {2:+0;-0;0}, Stress {3:+0;-0;0:F0}.";

    private Label _statusLabel = null!;
    private VBoxContainer _startPanel = null!;
    private HSlider _buyInSlider = null!;
    private Label _buyInValueLabel = null!;
    private Button _commitButton = null!;
    private VBoxContainer _postDropPanel = null!;
    private HSlider _cutSlider = null!;
    private Label _cutValueLabel = null!;
    private Button _cutButton = null!;
    private Button _bankExitDropButton = null!;
    private VBoxContainer _postCutPanel = null!;
    private Button _holdButton = null!;
    private Button _encroachButton = null!;
    private Button _takeOverButton = null!;
    private VBoxContainer _resultPanel = null!;
    private Label _resultLabel = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private HustleContext _ctx;
    private RngState _rng;
    private HustleState? _state;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("Panel/Layout/StatusLabel");
        _startPanel = GetNode<VBoxContainer>("Panel/Layout/StartPanel");
        _buyInSlider = GetNode<HSlider>("Panel/Layout/StartPanel/BuyInRow/BuyInSlider");
        _buyInValueLabel = GetNode<Label>("Panel/Layout/StartPanel/BuyInRow/BuyInValueLabel");
        _commitButton = GetNode<Button>("Panel/Layout/StartPanel/CommitButton");
        _postDropPanel = GetNode<VBoxContainer>("Panel/Layout/PostDropPanel");
        _cutSlider = GetNode<HSlider>("Panel/Layout/PostDropPanel/CutRow/CutSlider");
        _cutValueLabel = GetNode<Label>("Panel/Layout/PostDropPanel/CutRow/CutValueLabel");
        _cutButton = GetNode<Button>("Panel/Layout/PostDropPanel/CutButton");
        _bankExitDropButton = GetNode<Button>("Panel/Layout/PostDropPanel/BankExitDropButton");
        _postCutPanel = GetNode<VBoxContainer>("Panel/Layout/PostCutPanel");
        _holdButton = GetNode<Button>("Panel/Layout/PostCutPanel/HoldButton");
        _encroachButton = GetNode<Button>("Panel/Layout/PostCutPanel/EncroachButton");
        _takeOverButton = GetNode<Button>("Panel/Layout/PostCutPanel/TakeOverButton");
        _resultPanel = GetNode<VBoxContainer>("Panel/Layout/ResultPanel");
        _resultLabel = GetNode<Label>("Panel/Layout/ResultPanel/ResultLabel");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _buyInSlider.ValueChanged += _ => _buyInValueLabel.Text = ((int)_buyInSlider.Value).ToString();
        _cutSlider.ValueChanged += _ => _cutValueLabel.Text = _cutSlider.Value.ToString("F1");
        _commitButton.Pressed += OnCommitPressed;
        _cutButton.Pressed += OnCutPressed;
        _bankExitDropButton.Pressed += OnBankExitDropPressed;
        _holdButton.Pressed += OnHoldPressed;
        _encroachButton.Pressed += OnEncroachPressed;
        _takeOverButton.Pressed += OnTakeOverPressed;
        _doneButton.Pressed += OnDonePressed;
    }

    public override void _ExitTree()
    {
        _commitButton.Pressed -= OnCommitPressed;
        _cutButton.Pressed -= OnCutPressed;
        _bankExitDropButton.Pressed -= OnBankExitDropPressed;
        _holdButton.Pressed -= OnHoldPressed;
        _encroachButton.Pressed -= OnEncroachPressed;
        _takeOverButton.Pressed -= OnTakeOverPressed;
        _doneButton.Pressed -= OnDonePressed;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        bool isPending = gm.TryGetPendingHustleSession(out PendingHustleSession session)
            && session.Activity == WorkActivity.Narcotics;
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
        _state = null;
        _ctx = gm.Hustles.BuildNarcoticsContext(gm.Career.AvatarPlayerId, day);
        _rng = new RngState(unchecked((ulong)System.Environment.TickCount64) ^ 0x4E41524331UL | 1UL);

        double maxBuyIn = Math.Min(_ctx.Funds, NarcoticsHustle.ComputeBuyInMax(in _ctx));
        bool canAfford = maxBuyIn >= NarcoticsHustle.NarcoticsProfile.BuyInMin;
        _buyInSlider.MinValue = NarcoticsHustle.NarcoticsProfile.BuyInMin;
        _buyInSlider.MaxValue = canAfford ? maxBuyIn : NarcoticsHustle.NarcoticsProfile.BuyInMin;
        _buyInSlider.Value = _buyInSlider.MinValue;
        _buyInValueLabel.Text = ((int)_buyInSlider.Value).ToString();
        _commitButton.Disabled = !canAfford;
        _statusLabel.Text = canAfford ? string.Empty : CantAffordText;

        ShowPanel(_startPanel);
    }

    private void ShowPanel(Control panel)
    {
        _startPanel.Visible = ReferenceEquals(panel, _startPanel);
        _postDropPanel.Visible = ReferenceEquals(panel, _postDropPanel);
        _postCutPanel.Visible = ReferenceEquals(panel, _postCutPanel);
        _resultPanel.Visible = ReferenceEquals(panel, _resultPanel);
    }

    private void OnCommitPressed()
    {
        _state = NarcoticsHustle.DropInventory(in _ctx, _buyInSlider.Value, ref _rng);
        _cutSlider.Value = _cutSlider.MinValue;
        Advance();
    }

    private void OnCutPressed()
    {
        _state = NarcoticsHustle.CutProduct(_state!.Value, in _ctx, _cutSlider.Value, ref _rng);
        Advance();
    }

    private void OnBankExitDropPressed()
    {
        _state = NarcoticsHustle.BankExit(_state!.Value, in _ctx);
        Advance();
    }

    private void OnHoldPressed() => Push(PushLevel.Hold);

    private void OnEncroachPressed() => Push(PushLevel.Encroach);

    private void OnTakeOverPressed() => Push(PushLevel.TakeOver);

    private void Push(PushLevel push)
    {
        _state = NarcoticsHustle.PushTerritory(_state!.Value, in _ctx, push, ref _rng);
        Advance();
    }

    private void Advance()
    {
        HustleState state = _state!.Value;
        switch (state.Stage)
        {
            case HustleStage.InventoryDrop:
                _statusLabel.Text = string.Empty;
                ShowPanel(_postDropPanel);
                break;
            case HustleStage.ProfitToxicityCut:
                _statusLabel.Text = string.Empty;
                ShowPanel(_postCutPanel);
                break;
            case HustleStage.Busted:
                _resultLabel.Text = string.Format(
                    BustedFormat, -state.FundsDelta, state.DetectionRiskDelta, state.StressDelta);
                ShowPanel(_resultPanel);
                break;
            case HustleStage.Resolved:
                _resultLabel.Text = string.Format(
                    ResolvedFormat, state.FundsDelta, state.DetectionRiskDelta, state.HealthCeilingDelta, state.StressDelta);
                ShowPanel(_resultPanel);
                break;
        }
    }

    private void OnDonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (_state is { Stage: HustleStage.Resolved or HustleStage.Busted } state)
        {
            gm.Hustles.ApplyNarcoticsResolution(gm.Career.AvatarPlayerId, state.ToResolution(), _sessionDay);
        }
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
