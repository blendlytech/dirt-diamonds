using System;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (permanent sibling under Main, per Main.tscn) for an
/// armed Fencing <see cref="PendingHustleSession"/>: a turn-based alternating-
/// offer negotiation driven through the pure <see cref="FencingNegotiation"/>
/// resolver, entirely in memory until <see cref="OnDonePressed"/> commits it
/// via <see cref="HustleService.ApplyFencingResolution"/> — same abandon-
/// evaporates-cleanly forfeit discipline as <see cref="NarcoticsHustleScreen"/>.
/// The lot's hidden true value/reservation are never read by this script —
/// only <see cref="FencingState.CurrentOffer"/>/<see cref="FencingState.PatienceRemaining"/>
/// ever reach a label, per the design's "no twitch skill, standing only" rule
/// (docs/design/hustles_narcotics_fencing.md §4). Node paths verified against
/// FencingScreen.tscn (godot_scene_mapper) before this script was written.
/// </summary>
public sealed partial class FencingScreen : Control
{
    [Export]
    public string OfferFormat { get; set; } = "Current offer: ${0:F0}";

    [Export]
    public string PatienceFormat { get; set; } = "Patience: {0}";

    [Export]
    public string DealFormat { get; set; } = "Deal at ${0:F0}. Net funds +${1:F0}{2}";

    [Export]
    public string StingSuffix { get; set; } = " (stung! Detection +{0})";

    [Export]
    public string WalkText { get; set; } = "The fence walked. Lot unsold.";

    // Generous, fixed input ceiling for the counter ask — deliberately NOT
    // derived from FencingState.HiddenValue/HiddenReservation (VMax=800 bounds
    // R well under this); the UI never reads either hidden field.
    private const double AskCeiling = 2000;

    private Label _offerLabel = null!;
    private Label _patienceLabel = null!;
    private VBoxContainer _negotiatePanel = null!;
    private SpinBox _askSpinBox = null!;
    private Button _counterButton = null!;
    private Button _acceptButton = null!;
    private VBoxContainer _resultPanel = null!;
    private Label _resultLabel = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private FencingContext _ctx;
    private RngState _rng;
    private FencingState _state;

    public override void _Ready()
    {
        _offerLabel = GetNode<Label>("Panel/Layout/OfferLabel");
        _patienceLabel = GetNode<Label>("Panel/Layout/PatienceLabel");
        _negotiatePanel = GetNode<VBoxContainer>("Panel/Layout/NegotiatePanel");
        _askSpinBox = GetNode<SpinBox>("Panel/Layout/NegotiatePanel/AskRow/AskSpinBox");
        _counterButton = GetNode<Button>("Panel/Layout/NegotiatePanel/CounterButton");
        _acceptButton = GetNode<Button>("Panel/Layout/NegotiatePanel/AcceptButton");
        _resultPanel = GetNode<VBoxContainer>("Panel/Layout/ResultPanel");
        _resultLabel = GetNode<Label>("Panel/Layout/ResultPanel/ResultLabel");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _askSpinBox.MaxValue = AskCeiling;
        _counterButton.Pressed += OnCounterPressed;
        _acceptButton.Pressed += OnAcceptPressed;
        _doneButton.Pressed += OnDonePressed;
    }

    public override void _ExitTree()
    {
        _counterButton.Pressed -= OnCounterPressed;
        _acceptButton.Pressed -= OnAcceptPressed;
        _doneButton.Pressed -= OnDonePressed;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        bool isPending = gm.TryGetPendingHustleSession(out PendingHustleSession session)
            && session.Activity == WorkActivity.Fencing;
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
        _ctx = gm.Hustles.BuildFencingContext(gm.Career.AvatarPlayerId, day);
        _rng = new RngState(unchecked((ulong)System.Environment.TickCount64) ^ 0x46454E43494E47UL | 1UL);
        _state = FencingNegotiation.StartLot(in _ctx, ref _rng);

        _askSpinBox.Value = _state.CurrentOffer;
        _negotiatePanel.Visible = true;
        _resultPanel.Visible = false;
        RefreshNegotiationLabels();
    }

    private void RefreshNegotiationLabels()
    {
        _offerLabel.Text = string.Format(OfferFormat, _state.CurrentOffer);
        _patienceLabel.Text = string.Format(PatienceFormat, _state.PatienceRemaining);
    }

    private void OnAcceptPressed()
    {
        _state = FencingNegotiation.Accept(in _state, in _ctx, ref _rng);
        Advance();
    }

    private void OnCounterPressed()
    {
        _state = FencingNegotiation.Counter(in _state, in _ctx, _askSpinBox.Value, ref _rng);
        Advance();
    }

    private void Advance()
    {
        switch (_state.Outcome)
        {
            case FencingOutcomeKind.InProgress:
                RefreshNegotiationLabels();
                break;
            case FencingOutcomeKind.Deal:
                string stingPart = _state.WatchlistFlag
                    ? string.Format(StingSuffix, _state.DetectionRiskDelta)
                    : string.Empty;
                _resultLabel.Text = string.Format(DealFormat, _state.DealPrice, _state.FundsDelta, stingPart);
                _negotiatePanel.Visible = false;
                _resultPanel.Visible = true;
                break;
            case FencingOutcomeKind.Walk:
                _resultLabel.Text = WalkText;
                _negotiatePanel.Visible = false;
                _resultPanel.Visible = true;
                break;
        }
    }

    private void OnDonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (_state.Outcome != FencingOutcomeKind.InProgress)
        {
            gm.Hustles.ApplyFencingResolution(gm.Career.AvatarPlayerId, _state.ToResolution(), _sessionDay);
        }
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
