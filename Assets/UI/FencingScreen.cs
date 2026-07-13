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
/// FencingScreen.tscn before this script was written.
///
/// F-1 (docs/design/hustle_minigames_depth_pass.md §4.2, §11 flat-N default):
/// after a lot closes or the fence walks, "Show Him Another Lot" starts a
/// fresh <see cref="FencingNegotiation.StartLot"/> draw — <see cref="_accrued"/>
/// folds every prior lot's deltas in memory (INV-1: nothing hits the DB until
/// Done), same <see cref="NarcoticsHustleScreen"/> accumulator shape. Heat
/// nudges from the accrued detection delta so a hot lot's own sting exposure
/// feeds the next lot's <see cref="FencingNegotiation.ComputeStingProbability"/>
/// — the built-in Lever-A tail scaling the spec calls out, no new risk math.
/// <see cref="FencingContext.FenceStanding"/> stays fixed at the session's
/// opening snapshot for every lot: <see cref="HustleResolution"/> has no
/// fence-standing delta field today (Fencing's Layer-2 apply path never
/// touches the relationship graph), so §4.2's "each closed deal nudges
/// FenceStanding" is a disclosed seam for a future Layer-1/2 slice, not
/// wired here — this sub-slice is UI-only per §10's role split.
/// Capped at <see cref="MaxLotsPerDay"/> (flat, in-memory, no GameManager
/// hour-budget dependency, same resolved seam as N-1).
/// </summary>
public sealed partial class FencingScreen : Control
{
    private const int MaxLotsPerDay = 3;

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

    [Export]
    public string LotTallyFormat { get; set; } = "Session total so far: funds {0:+0;-0;0}, detection {1:+0;-0;0}.";

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
    private Label _runTallyLabel = null!;
    private Button _showAnotherButton = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private FencingContext _baseCtx;
    private FencingContext _ctx;
    private RngState _rng;
    private FencingState _state;
    private HustleResolution _accrued;
    private int _lotsUsed;

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
        _runTallyLabel = GetNode<Label>("Panel/Layout/ResultPanel/RunTallyLabel");
        _showAnotherButton = GetNode<Button>("Panel/Layout/ResultPanel/ShowAnotherButton");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _askSpinBox.MaxValue = AskCeiling;
        _counterButton.Pressed += OnCounterPressed;
        _acceptButton.Pressed += OnAcceptPressed;
        _showAnotherButton.Pressed += OnShowAnotherPressed;
        _doneButton.Pressed += OnDonePressed;
    }

    public override void _ExitTree()
    {
        _counterButton.Pressed -= OnCounterPressed;
        _acceptButton.Pressed -= OnAcceptPressed;
        _showAnotherButton.Pressed -= OnShowAnotherPressed;
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
        _baseCtx = gm.Hustles.BuildFencingContext(gm.Career.AvatarPlayerId, day);
        _rng = new RngState(unchecked((ulong)System.Environment.TickCount64) ^ 0x46454E43494E47UL | 1UL);
        _accrued = default;
        _lotsUsed = 0;

        BeginLot();
    }

    /// <summary>
    /// Starts one lot (the first, or "show him another"): rebuilds
    /// <see cref="_ctx"/> from the base snapshot with Heat nudged by every
    /// prior lot's <see cref="_accrued"/> detection delta so a hot lot's own
    /// sting exposure feeds the next lot's sting roll (§4.2's tail scaling).
    /// FenceStanding is not nudged — see the class doc's disclosed seam.
    /// No DB read; the live DB row never moves mid-session.
    /// </summary>
    private void BeginLot()
    {
        double liveHeat = Math.Clamp(_baseCtx.Heat + _accrued.DetectionRiskDelta / 100.0, 0, 1);
        _ctx = new FencingContext(liveHeat, _baseCtx.FenceStanding);
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
            {
                string stingPart = _state.WatchlistFlag
                    ? string.Format(StingSuffix, _state.DetectionRiskDelta)
                    : string.Empty;
                _resultLabel.Text = string.Format(DealFormat, _state.DealPrice, _state.FundsDelta, stingPart);
                FinishLot();
                break;
            }
            case FencingOutcomeKind.Walk:
                _resultLabel.Text = WalkText;
                FinishLot();
                break;
        }
    }

    /// <summary>Folds the closed lot into <see cref="_accrued"/>, updates the running tally, and gates the re-offer per <see cref="MaxLotsPerDay"/>.</summary>
    private void FinishLot()
    {
        _lotsUsed++;
        HustleResolution runningTotal = CombineAccrued(in _state);
        _showAnotherButton.Visible = _lotsUsed < MaxLotsPerDay;
        _runTallyLabel.Text = _lotsUsed > 1
            ? string.Format(LotTallyFormat, runningTotal.FundsDelta, runningTotal.DetectionRiskDelta)
            : string.Empty;
        _negotiatePanel.Visible = false;
        _resultPanel.Visible = true;
    }

    /// <summary>Folds the current lot's terminal state into <see cref="_accrued"/> without committing — the in-memory INV-1 accumulator.</summary>
    private HustleResolution CombineAccrued(in FencingState terminal)
    {
        HustleResolution delta = terminal.ToResolution();
        _accrued = new HustleResolution(
            _accrued.FundsDelta + delta.FundsDelta,
            _accrued.DetectionRiskDelta + delta.DetectionRiskDelta,
            _accrued.HealthCeilingDelta + delta.HealthCeilingDelta,
            _accrued.RecklessnessDelta + delta.RecklessnessDelta,
            _accrued.StressDelta + delta.StressDelta,
            _accrued.SupplierTrustDelta + delta.SupplierTrustDelta,
            _accrued.CrewStandingDelta + delta.CrewStandingDelta,
            _accrued.SetWatchlistFlag || delta.SetWatchlistFlag,
            _accrued.SetBadProductFlag || delta.SetBadProductFlag,
            _accrued.SetSpoiledGoodsFlag || delta.SetSpoiledGoodsFlag,
            _accrued.SetControlsTurfFlag || delta.SetControlsTurfFlag);
        return _accrued;
    }

    private void OnShowAnotherPressed() => BeginLot();

    private void OnDonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (_accrued.FundsDelta != 0 || _accrued.DetectionRiskDelta != 0 || _accrued.HealthCeilingDelta != 0
            || _accrued.RecklessnessDelta != 0 || _accrued.StressDelta != 0 || _accrued.SupplierTrustDelta != 0
            || _accrued.CrewStandingDelta != 0 || _accrued.SetWatchlistFlag || _accrued.SetBadProductFlag
            || _accrued.SetSpoiledGoodsFlag || _accrued.SetControlsTurfFlag)
        {
            gm.Hustles.ApplyFencingResolution(gm.Career.AvatarPlayerId, _accrued, _sessionDay);
        }
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
