using System;
using System.Collections.Generic;
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
///
/// F-2 (§4.1): every lot now opens with an Acquire pick — Pawn Shop
/// Overstock / Fell Off a Truck / Fresh From a Job — over the pure
/// <see cref="FencingNegotiation.AcquireLot"/> table, whose V/initial-heat/
/// spoiled-goods outputs seed <see cref="FencingNegotiation.StartLot(in FencingContext, in LotAcquisition, ref RngState)"/>
/// instead of the old flat draw. FreshFromAJob is content-gated on
/// <see cref="_hotGoodsAvailable"/> — a session-local snapshot of
/// <see cref="HustleService.HasHotGoodsFlag"/> taken once at session start
/// and flipped false the moment it's used, so a second lot in the same
/// multi-lot session can't spend a flag that's already been folded into
/// <see cref="_accrued"/> (still nothing hits the DB until Done — the flag's
/// actual clear rides <see cref="_accrued"/>'s <c>SetConsumesHotGoodsFlag</c>
/// through the same single Done commit as everything else, INV-1).
///
/// P0 Hustle Feel Pass (UI layer only): the per-lot sting risk is surfaced
/// from the resolver's existing pure
/// <see cref="FencingNegotiation.ComputeStingProbability"/> before the player
/// closes a deal, results land through the staged <see cref="ResultReveal"/>
/// (the sale counts up, the sting/heat line drops after), panels fade in, and
/// <see cref="UiSfx"/> stings mark deals, stings, and walks. Zero resolver/sim
/// touch — the probability shown is a pure read with no RNG draw.
/// </summary>
public sealed partial class FencingScreen : Control
{
    private const int MaxLotsPerDay = 3;

    [Export]
    public string AcquirePawnShopText { get; set; } = "Pawn-Shop Overstock — legit-adjacent, low value, no heat";

    [Export]
    public string AcquireTruckText { get; set; } = "Fell Off a Truck — mid value, still warm";

    [Export]
    public string AcquireFreshJobText { get; set; } = "Fresh From a Job — your own hot goods, top dollar";

    [Export]
    public string OfferFormat { get; set; } = "His offer: ${0:N0}";

    [Export]
    public string PatienceFormat { get; set; } = "Patience left: {0}";

    [Export]
    public string StingOddsFormat { get; set; } = "Word is buyers have been flipping — {0:0}% chance he's wired.";

    [Export]
    public string DealHeadlineText { get; set; } = "SOLD.";

    [Export]
    public string TakeFormat { get; set; } = "+${0:N0}";

    [Export]
    public string StingLineFormat { get; set; } = "He was wired — half the money's marked. Heat +{0}.";

    [Export]
    public string WarmGoodsLineFormat { get; set; } = "Moving warm goods leaves prints — heat +{0}.";

    [Export]
    public string CleanDealText { get; set; } = "No tails, no questions. Clean money.";

    [Export]
    public string WalkHeadlineText { get; set; } = "NO SALE.";

    [Export]
    public string WalkText { get; set; } = "He turns it over once, shrugs, and walks. The lot's still yours.";

    [Export]
    public string LotTallyFormat { get; set; } = "Session total so far: funds {0:+0;-0;0}, detection {1:+0;-0;0}.";

    // Generous, fixed input ceiling for the counter ask — deliberately NOT
    // derived from FencingState.HiddenValue/HiddenReservation (VMax=800 bounds
    // R well under this); the UI never reads either hidden field.
    private const double AskCeiling = 2000;

    private VBoxContainer _acquirePanel = null!;
    private Button _pawnShopButton = null!;
    private Button _truckButton = null!;
    private Button _freshJobButton = null!;
    private Label _offerLabel = null!;
    private Label _patienceLabel = null!;
    private VBoxContainer _negotiatePanel = null!;
    private Label _stingOddsLabel = null!;
    private SpinBox _askSpinBox = null!;
    private Button _counterButton = null!;
    private Button _acceptButton = null!;
    private VBoxContainer _resultPanel = null!;
    private Label _headlineLabel = null!;
    private Label _takeLabel = null!;
    private Label _lineA = null!;
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
    private bool _hotGoodsAvailable;
    private ResultReveal? _reveal;

    public override void _Ready()
    {
        _acquirePanel = GetNode<VBoxContainer>("Panel/Layout/AcquirePanel");
        _pawnShopButton = GetNode<Button>("Panel/Layout/AcquirePanel/PawnShopButton");
        _truckButton = GetNode<Button>("Panel/Layout/AcquirePanel/TruckButton");
        _freshJobButton = GetNode<Button>("Panel/Layout/AcquirePanel/FreshJobButton");
        _offerLabel = GetNode<Label>("Panel/Layout/OfferLabel");
        _patienceLabel = GetNode<Label>("Panel/Layout/PatienceLabel");
        _negotiatePanel = GetNode<VBoxContainer>("Panel/Layout/NegotiatePanel");
        _stingOddsLabel = GetNode<Label>("Panel/Layout/NegotiatePanel/StingOddsLabel");
        _askSpinBox = GetNode<SpinBox>("Panel/Layout/NegotiatePanel/AskRow/AskSpinBox");
        _counterButton = GetNode<Button>("Panel/Layout/NegotiatePanel/CounterButton");
        _acceptButton = GetNode<Button>("Panel/Layout/NegotiatePanel/AcceptButton");
        _resultPanel = GetNode<VBoxContainer>("Panel/Layout/ResultPanel");
        _headlineLabel = GetNode<Label>("Panel/Layout/ResultPanel/HeadlineLabel");
        _takeLabel = GetNode<Label>("Panel/Layout/ResultPanel/TakeLabel");
        _lineA = GetNode<Label>("Panel/Layout/ResultPanel/LineA");
        _runTallyLabel = GetNode<Label>("Panel/Layout/ResultPanel/RunTallyLabel");
        _showAnotherButton = GetNode<Button>("Panel/Layout/ResultPanel/ShowAnotherButton");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _askSpinBox.MaxValue = AskCeiling;
        _pawnShopButton.Text = AcquirePawnShopText;
        _truckButton.Text = AcquireTruckText;
        _freshJobButton.Text = AcquireFreshJobText;
        _pawnShopButton.Pressed += OnPawnShopPressed;
        _truckButton.Pressed += OnTruckPressed;
        _freshJobButton.Pressed += OnFreshJobPressed;
        _counterButton.Pressed += OnCounterPressed;
        _acceptButton.Pressed += OnAcceptPressed;
        _showAnotherButton.Pressed += OnShowAnotherPressed;
        _doneButton.Pressed += OnDonePressed;
    }

    public override void _ExitTree()
    {
        _pawnShopButton.Pressed -= OnPawnShopPressed;
        _truckButton.Pressed -= OnTruckPressed;
        _freshJobButton.Pressed -= OnFreshJobPressed;
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
        _baseCtx = gm.Hustles.BuildFencingContext(gm.Career.AvatarPlayerId, day);
        _hotGoodsAvailable = gm.Hustles.HasHotGoodsFlag(gm.Career.AvatarPlayerId);
        _rng = new RngState(unchecked((ulong)System.Environment.TickCount64) ^ 0x46454E43494E47UL | 1UL);
        _accrued = default;
        _lotsUsed = 0;

        BeginLot();
    }

    private void KillReveal()
    {
        _reveal?.Kill();
        _reveal = null;
    }

    /// <summary>
    /// Starts one lot (the first, or "show him another"): rebuilds
    /// <see cref="_ctx"/> from the base snapshot with Heat nudged by every
    /// prior lot's <see cref="_accrued"/> detection delta so a hot lot's own
    /// sting exposure feeds the next lot's sting roll (§4.2's tail scaling).
    /// FenceStanding is not nudged — see the class doc's disclosed seam.
    /// No DB read; the live DB row never moves mid-session. Opens on the
    /// Acquire panel (§4.1) — the negotiation itself only starts once a
    /// source is picked, in <see cref="OnAcquireSourcePressed"/>.
    /// </summary>
    private void BeginLot()
    {
        KillReveal();
        double liveHeat = Math.Clamp(_baseCtx.Heat + _accrued.DetectionRiskDelta / 100.0, 0, 1);
        _ctx = new FencingContext(liveHeat, _baseCtx.FenceStanding);

        _freshJobButton.Disabled = !_hotGoodsAvailable;
        _acquirePanel.Visible = true;
        _offerLabel.Visible = false;
        _patienceLabel.Visible = false;
        _negotiatePanel.Visible = false;
        _resultPanel.Visible = false;
        HustleFeel.FadeIn(_acquirePanel);
    }

    /// <summary>§4.1: the Acquire pick — draws the lot via <see cref="FencingNegotiation.AcquireLot"/> and starts the negotiation from it. FreshFromAJob spends the session's one-time <see cref="_hotGoodsAvailable"/> snapshot; the actual DB flag only clears on Done, via <see cref="_accrued"/>'s folded <c>SetConsumesHotGoodsFlag</c> (INV-1).</summary>
    private void OnAcquireSourcePressed(FencingSource source)
    {
        UiSfx.Instance.Play(UiSound.Tap);
        LotAcquisition acquisition = FencingNegotiation.AcquireLot(source, _hotGoodsAvailable, ref _rng);
        if (source == FencingSource.FreshFromAJob)
        {
            _hotGoodsAvailable = false;
        }
        _state = FencingNegotiation.StartLot(in _ctx, in acquisition, ref _rng);

        // P0: the wire risk is on the table before any deal can close — a pure
        // read of the same probability CloseDeal will roll against.
        double pSting = FencingNegotiation.ComputeStingProbability(in _ctx);
        _stingOddsLabel.Text = string.Format(StingOddsFormat, pSting * 100.0);
        _stingOddsLabel.AddThemeColorOverride("font_color", HustleFeel.RiskColor(pSting));

        _askSpinBox.Value = _state.CurrentOffer;
        _acquirePanel.Visible = false;
        _offerLabel.Visible = true;
        _patienceLabel.Visible = true;
        _negotiatePanel.Visible = true;
        _resultPanel.Visible = false;
        HustleFeel.FadeIn(_negotiatePanel);
        RefreshNegotiationLabels();
    }

    private void OnPawnShopPressed() => OnAcquireSourcePressed(FencingSource.PawnShopOverstock);

    private void OnTruckPressed() => OnAcquireSourcePressed(FencingSource.FellOffATruck);

    private void OnFreshJobPressed() => OnAcquireSourcePressed(FencingSource.FreshFromAJob);

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
        UiSfx.Instance.Play(UiSound.Tap);
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
                string heatLine = _state.WatchlistFlag
                    ? string.Format(StingLineFormat, _state.DetectionRiskDelta)
                    : _state.DetectionRiskDelta > 0
                        ? string.Format(WarmGoodsLineFormat, _state.DetectionRiskDelta)
                        : CleanDealText;
                UiSound heatSting = _state.WatchlistFlag ? UiSound.Error : UiSound.DayTick;
                FinishLot()
                    .Headline(_headlineLabel, DealHeadlineText, UiColors.Success, UiSound.Cash)
                    .CountUp(_takeLabel, TakeFormat, _state.FundsDelta)
                    .Line(_lineA, heatLine, heatSting)
                    .Footer(BuildFooter());
                break;
            }
            case FencingOutcomeKind.Walk:
                FinishLot()
                    .Headline(_headlineLabel, WalkHeadlineText, UiColors.Warning, UiSound.Back)
                    .Clear(_takeLabel)
                    .Line(_lineA, WalkText)
                    .Footer(BuildFooter());
                break;
        }
    }

    /// <summary>Folds the closed lot into <see cref="_accrued"/>, swaps to the result panel, and starts the staged reveal the caller composes onto.</summary>
    private ResultReveal FinishLot()
    {
        _lotsUsed++;
        HustleResolution runningTotal = CombineAccrued(in _state);
        _runTallyLabel.Text = _lotsUsed > 1
            ? string.Format(LotTallyFormat, runningTotal.FundsDelta, runningTotal.DetectionRiskDelta)
            : string.Empty;
        _negotiatePanel.Visible = false;
        _offerLabel.Visible = false;
        _patienceLabel.Visible = false;
        _resultPanel.Visible = true;
        HustleFeel.FadeIn(_resultPanel);
        KillReveal();
        _reveal = ResultReveal.Begin(_resultPanel);
        return _reveal;
    }

    /// <summary>The footer controls this result should end with — the tally only once there's a tally, the re-offer only while lots remain.</summary>
    private Control[] BuildFooter()
    {
        var footer = new List<Control>(3);
        if (_lotsUsed > 1)
        {
            footer.Add(_runTallyLabel);
        }
        else
        {
            _runTallyLabel.Visible = false;
        }
        if (_lotsUsed < MaxLotsPerDay)
        {
            footer.Add(_showAnotherButton);
        }
        else
        {
            _showAnotherButton.Visible = false;
        }
        footer.Add(_doneButton);
        return footer.ToArray();
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
            _accrued.SetControlsTurfFlag || delta.SetControlsTurfFlag,
            setGamblingBustFlag: false, setRobberyBustFlag: false,
            setConsumesHotGoodsFlag: _accrued.SetConsumesHotGoodsFlag || delta.SetConsumesHotGoodsFlag);
        return _accrued;
    }

    private void OnShowAnotherPressed()
    {
        UiSfx.Instance.Play(UiSound.Tap);
        BeginLot();
    }

    private void OnDonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (_accrued.FundsDelta != 0 || _accrued.DetectionRiskDelta != 0 || _accrued.HealthCeilingDelta != 0
            || _accrued.RecklessnessDelta != 0 || _accrued.StressDelta != 0 || _accrued.SupplierTrustDelta != 0
            || _accrued.CrewStandingDelta != 0 || _accrued.SetWatchlistFlag || _accrued.SetBadProductFlag
            || _accrued.SetSpoiledGoodsFlag || _accrued.SetControlsTurfFlag || _accrued.SetConsumesHotGoodsFlag)
        {
            gm.Hustles.ApplyFencingResolution(gm.Career.AvatarPlayerId, _accrued, _sessionDay);
        }
        UiSfx.Instance.Play(UiSound.Back);
        KillReveal();
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
