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
///
/// N-1 (docs/design/hustle_minigames_depth_pass.md §3.2, §11 flat-N default):
/// after a run resolves, "Re-up &amp; Run Again" starts a fresh run from the
/// *current* live context — <see cref="_accrued"/> carries every prior run's
/// deltas in memory (INV-1: still nothing hits the DB until Done), and each
/// new run's <see cref="HustleContext"/> is rebuilt with funds/heat nudged by
/// that accrual, so a hot run's own heat makes the next drop's pSeize worse —
/// the built-in Lever-A tail scaling the spec calls out, no new risk math.
/// Capped at <see cref="MaxRunsPerDay"/> (flat, in-memory, no GameManager
/// hour-budget dependency per §11's resolved seam).
///
/// N-2 (§3.1): every run now opens with a supplier haggle (Stage 0) over the
/// pure <see cref="NarcoticsHustle.HaggleStart"/> family — the closed deal's
/// effective unit cost and allotment multiplier feed the buy-in stage via the
/// haggled <see cref="NarcoticsHustle.DropInventory"/> overload, its trust
/// delta folds through <see cref="FoldAccrued"/>, and a refusal or walk ends
/// the session with no run today (forfeit-to-no-deal). Re-ups re-haggle: each
/// run is its own supply buy, at trust nudged by the session's accrual.
///
/// P0 Hustle Feel Pass (UI layer only): panels fade in, results land through
/// the staged <see cref="ResultReveal"/> (the night's money counts up, then
/// heat/toxicity/stress drop line-by-line), and <see cref="UiSfx"/> stings
/// mark the locked deal, the seizure, and the payout. The resolver exposes no
/// pure seize/faction probability method, so this screen shows no odds number
/// — surfacing invented odds would need a resolver touch, which this pass
/// forbids. Zero resolver/sim calls were added; harness bands stay
/// byte-identical by construction.
/// </summary>
public sealed partial class NarcoticsHustleScreen : Control
{
    private const int MaxRunsPerDay = 3;

    [Export]
    public string CantAffordText { get; set; } = "Not enough funds/trust to run this hustle today.";

    [Export]
    public string HaggleAskFormat { get; set; } = "\"${0:F2} a unit. Street says ${1:F0}. Take it or talk fast.\" Patience: {2}.";

    [Export]
    public string HaggleDealFormat { get; set; } = "Locked in at ${0:F2}/unit. He'll front up to ${1:F0}. How deep do you go?";

    [Export]
    public string ResolvedHeadlineText { get; set; } = "THE CORNER PAID OUT";

    [Export]
    public string BustedHeadlineText { get; set; } = "SEIZED.";

    [Export]
    public string NoDealHeadlineText { get; set; } = "NO DEAL.";

    [Export]
    public string TakeFormat { get; set; } = "{0:+$#,##0;-$#,##0;$0}";

    [Export]
    public string BustCostFormat { get; set; } = "-${0:N0} — product and cash, gone";

    [Export]
    public string HeatLineFormat { get; set; } = "The block noticed — heat {0:+0;-0;0}.";

    [Export]
    public string BustHeatLineFormat { get; set; } = "They know your name now — heat +{0}.";

    [Export]
    public string HealthLineFormat { get; set; } = "Your own product is cutting into you — health {0}.";

    [Export]
    public string StressLineFormat { get; set; } = "Sleep doesn't come easy — stress +{0:0}.";

    [Export]
    public string SupplierRefusedText { get; set; } = "You pushed too hard. He pockets the sample and walks off. No run today.";

    [Export]
    public string SupplierWalkedText { get; set; } = "You passed on today's price. The corner stays quiet.";

    [Export]
    public string RunTallyFormat { get; set; } = "Session total so far: funds {0:+0;-0;0}, detection {1:+0;-0;0}.";

    private Label _statusLabel = null!;
    private VBoxContainer _hagglePanel = null!;
    private Label _askLabel = null!;
    private HSlider _bidSlider = null!;
    private Label _bidValueLabel = null!;
    private Button _counterButton = null!;
    private Button _acceptButton = null!;
    private Button _walkButton = null!;
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
    private Label _headlineLabel = null!;
    private Label _takeLabel = null!;
    private Label _lineA = null!;
    private Label _lineB = null!;
    private Label _lineC = null!;
    private Label _runTallyLabel = null!;
    private Button _reUpButton = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private HustleContext _baseCtx;
    private HustleContext _ctx;
    private RngState _rng;
    private HustleState? _state;
    private HaggleState? _haggle;
    private double _dealUnitCost;
    private double _dealBuyInMult;
    private HustleResolution _accrued;
    private int _runsUsed;
    private ResultReveal? _reveal;

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("Panel/Layout/StatusLabel");
        _hagglePanel = GetNode<VBoxContainer>("Panel/Layout/HagglePanel");
        _askLabel = GetNode<Label>("Panel/Layout/HagglePanel/AskLabel");
        _bidSlider = GetNode<HSlider>("Panel/Layout/HagglePanel/BidRow/BidSlider");
        _bidValueLabel = GetNode<Label>("Panel/Layout/HagglePanel/BidRow/BidValueLabel");
        _counterButton = GetNode<Button>("Panel/Layout/HagglePanel/CounterButton");
        _acceptButton = GetNode<Button>("Panel/Layout/HagglePanel/AcceptButton");
        _walkButton = GetNode<Button>("Panel/Layout/HagglePanel/WalkButton");
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
        _headlineLabel = GetNode<Label>("Panel/Layout/ResultPanel/HeadlineLabel");
        _takeLabel = GetNode<Label>("Panel/Layout/ResultPanel/TakeLabel");
        _lineA = GetNode<Label>("Panel/Layout/ResultPanel/LineA");
        _lineB = GetNode<Label>("Panel/Layout/ResultPanel/LineB");
        _lineC = GetNode<Label>("Panel/Layout/ResultPanel/LineC");
        _runTallyLabel = GetNode<Label>("Panel/Layout/ResultPanel/RunTallyLabel");
        _reUpButton = GetNode<Button>("Panel/Layout/ResultPanel/ReUpButton");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _buyInSlider.ValueChanged += _ => _buyInValueLabel.Text = ((int)_buyInSlider.Value).ToString();
        _cutSlider.ValueChanged += _ => _cutValueLabel.Text = _cutSlider.Value.ToString("F1");
        _bidSlider.ValueChanged += _ => _bidValueLabel.Text = _bidSlider.Value.ToString("F2");
        _counterButton.Pressed += OnHaggleCounterPressed;
        _acceptButton.Pressed += OnHaggleAcceptPressed;
        _walkButton.Pressed += OnHaggleWalkPressed;
        _commitButton.Pressed += OnCommitPressed;
        _cutButton.Pressed += OnCutPressed;
        _bankExitDropButton.Pressed += OnBankExitDropPressed;
        _holdButton.Pressed += OnHoldPressed;
        _encroachButton.Pressed += OnEncroachPressed;
        _takeOverButton.Pressed += OnTakeOverPressed;
        _reUpButton.Pressed += OnReUpPressed;
        _doneButton.Pressed += OnDonePressed;
    }

    public override void _ExitTree()
    {
        _counterButton.Pressed -= OnHaggleCounterPressed;
        _acceptButton.Pressed -= OnHaggleAcceptPressed;
        _walkButton.Pressed -= OnHaggleWalkPressed;
        _commitButton.Pressed -= OnCommitPressed;
        _cutButton.Pressed -= OnCutPressed;
        _bankExitDropButton.Pressed -= OnBankExitDropPressed;
        _holdButton.Pressed -= OnHoldPressed;
        _encroachButton.Pressed -= OnEncroachPressed;
        _takeOverButton.Pressed -= OnTakeOverPressed;
        _reUpButton.Pressed -= OnReUpPressed;
        _doneButton.Pressed -= OnDonePressed;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        bool isPending = gm.TryGetPendingHustleSession(out PendingHustleSession session)
            && session.Activity == WorkActivity.Narcotics;
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
        _baseCtx = gm.Hustles.BuildNarcoticsContext(gm.Career.AvatarPlayerId, day);
        _rng = new RngState(unchecked((ulong)System.Environment.TickCount64) ^ 0x4E41524331UL | 1UL);
        _accrued = default;
        _runsUsed = 0;

        BeginRun();
    }

    private void KillReveal()
    {
        _reveal?.Kill();
        _reveal = null;
    }

    /// <summary>
    /// Starts one run (the first, or a re-up): rebuilds <see cref="_ctx"/> from
    /// the base snapshot nudged by everything <see cref="_accrued"/> so far —
    /// funds reflect prior takes/losses, heat reflects prior detection deltas,
    /// supplier trust reflects prior haggle/run deltas (N-2: the haggle's own
    /// trust hit feeds the next run's floor and patience) — so a hot run's own
    /// heat feeds into the next drop's pSeize (§3.2's built-in tail scaling).
    /// No DB read; the live DB row never moves mid-session. Each run opens with
    /// its own supplier haggle (§3.1 Stage 0) — a re-up is a fresh supply buy.
    /// </summary>
    private void BeginRun()
    {
        KillReveal();
        _state = null;
        _haggle = null;
        double liveFunds = Math.Max(0, _baseCtx.Funds + _accrued.FundsDelta);
        double liveHeat = Math.Clamp(_baseCtx.Heat + _accrued.DetectionRiskDelta / 100.0, 0, 1);
        int liveTrust = Math.Clamp(_baseCtx.SupplierTrust + _accrued.SupplierTrustDelta, -100, 100);
        _ctx = new HustleContext(
            liveFunds, liveHeat, _baseCtx.Reck, liveTrust,
            _baseCtx.CrewStandingLocal + _accrued.CrewStandingDelta, _baseCtx.OwnsTurfLocal, _baseCtx.UsesProduct);

        // Best-case affordability gate before wasting the player's time haggling:
        // even at the widest allotment the supplier could grant, can a min buy-in fit?
        double bestCaseMax = Math.Min(_ctx.Funds, NarcoticsHustle.ComputeBuyInMax(in _ctx)
            * (NarcoticsHustle.NarcoticsProfile.HaggleBuyInMultMin + NarcoticsHustle.NarcoticsProfile.HaggleBuyInMultSpan));
        if (bestCaseMax < NarcoticsHustle.NarcoticsProfile.BuyInMin)
        {
            ShowBuyInPanel(maxBuyIn: 0, canAfford: false);
            return;
        }

        _haggle = NarcoticsHustle.HaggleStart(in _ctx, ref _rng);
        HaggleState haggle = _haggle.Value;
        _bidSlider.MinValue = 1.0;
        _bidSlider.MaxValue = haggle.CurrentAsk;
        _bidSlider.Value = haggle.CurrentAsk * 0.6;
        _bidValueLabel.Text = _bidSlider.Value.ToString("F2");
        RefreshHaggleLabel();
        _statusLabel.Text = string.Empty;
        ShowPanel(_hagglePanel);
    }

    private void ShowPanel(Control panel)
    {
        _hagglePanel.Visible = ReferenceEquals(panel, _hagglePanel);
        _startPanel.Visible = ReferenceEquals(panel, _startPanel);
        _postDropPanel.Visible = ReferenceEquals(panel, _postDropPanel);
        _postCutPanel.Visible = ReferenceEquals(panel, _postCutPanel);
        _resultPanel.Visible = ReferenceEquals(panel, _resultPanel);
        HustleFeel.FadeIn(panel);
    }

    private void RefreshHaggleLabel()
    {
        HaggleState haggle = _haggle!.Value;
        _askLabel.Text = string.Format(
            HaggleAskFormat, haggle.CurrentAsk, NarcoticsHustle.NarcoticsProfile.StreetPrice, haggle.PatienceRemaining);
    }

    private void OnHaggleCounterPressed()
    {
        UiSfx.Instance.Play(UiSound.Tap);
        _haggle = NarcoticsHustle.HaggleCounter(_haggle!.Value, _bidSlider.Value);
        AdvanceHaggle();
    }

    private void OnHaggleAcceptPressed()
    {
        _haggle = NarcoticsHustle.HaggleAccept(_haggle!.Value);
        AdvanceHaggle();
    }

    private void OnHaggleWalkPressed()
    {
        _haggle = NarcoticsHustle.HaggleWalk(_haggle!.Value);
        AdvanceHaggle();
    }

    private void AdvanceHaggle()
    {
        HaggleState haggle = _haggle!.Value;
        switch (haggle.Outcome)
        {
            case HaggleOutcomeKind.InProgress:
                _bidSlider.MaxValue = haggle.CurrentAsk;
                RefreshHaggleLabel();
                break;
            case HaggleOutcomeKind.Deal:
                UiSfx.Instance.Play(UiSound.Confirm);
                FoldAccrued(haggle.ToResolution());
                _dealUnitCost = haggle.DealUnitCost;
                _dealBuyInMult = haggle.BuyInMaxMult;
                double maxBuyIn = Math.Min(
                    _ctx.Funds, NarcoticsHustle.ComputeBuyInMax(in _ctx) * _dealBuyInMult);
                ShowBuyInPanel(maxBuyIn, canAfford: maxBuyIn >= NarcoticsHustle.NarcoticsProfile.BuyInMin);
                break;
            case HaggleOutcomeKind.Refused:
            case HaggleOutcomeKind.Walked:
            {
                // No run today (§3.1 forfeit-to-no-deal): a refusal still costs
                // trust, a walk costs nothing — either way the session ends and
                // whatever earlier runs accrued applies on Done.
                FoldAccrued(haggle.ToResolution());
                bool refused = haggle.Outcome == HaggleOutcomeKind.Refused;
                _runTallyLabel.Text = _runsUsed > 0
                    ? string.Format(RunTallyFormat, _accrued.FundsDelta, _accrued.DetectionRiskDelta)
                    : string.Empty;
                ShowPanel(_resultPanel);
                KillReveal();
                _reveal = ResultReveal.Begin(_resultPanel)
                    .Clear(_takeLabel, _lineB, _lineC, _reUpButton)
                    .Headline(_headlineLabel, NoDealHeadlineText, UiColors.Warning,
                        refused ? UiSound.Error : UiSound.Back)
                    .Line(_lineA, refused ? SupplierRefusedText : SupplierWalkedText)
                    .Footer(BuildFooter(includeTally: _runsUsed > 0, includeReUp: false));
                break;
            }
        }
    }

    private void ShowBuyInPanel(double maxBuyIn, bool canAfford)
    {
        _buyInSlider.MinValue = NarcoticsHustle.NarcoticsProfile.BuyInMin;
        _buyInSlider.MaxValue = canAfford ? maxBuyIn : NarcoticsHustle.NarcoticsProfile.BuyInMin;
        _buyInSlider.Value = _buyInSlider.MinValue;
        _buyInValueLabel.Text = ((int)_buyInSlider.Value).ToString();
        _commitButton.Disabled = !canAfford;
        _statusLabel.Text = canAfford
            ? string.Format(HaggleDealFormat, _dealUnitCost, maxBuyIn)
            : CantAffordText;
        ShowPanel(_startPanel);
    }

    private void OnCommitPressed()
    {
        UiSfx.Instance.Play(UiSound.Tap);
        _state = NarcoticsHustle.DropInventory(
            in _ctx, _buyInSlider.Value, _dealUnitCost, _dealBuyInMult, ref _rng);
        _cutSlider.Value = _cutSlider.MinValue;
        Advance();
    }

    private void OnCutPressed()
    {
        UiSfx.Instance.Play(UiSound.Tap);
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
                // A seizure ends the session outright — no re-up after a bust
                // (you're already burned; §3.2 only offers re-up after a clean resolve).
                ShowPanel(_resultPanel);
                KillReveal();
                _reveal = ResultReveal.Begin(_resultPanel)
                    .Clear(_runTallyLabel, _reUpButton)
                    .Headline(_headlineLabel, BustedHeadlineText, UiColors.Danger, UiSound.Error)
                    .CountUp(_takeLabel, BustCostFormat, -state.FundsDelta)
                    .Line(_lineA, string.Format(BustHeatLineFormat, state.DetectionRiskDelta))
                    .Line(_lineB, string.Format(StressLineFormat, state.StressDelta))
                    .Line(_lineC, null)
                    .Footer(BuildFooter(includeTally: false, includeReUp: false));
                break;
            case HustleStage.Resolved:
            {
                _runsUsed++;
                HustleResolution runningTotal = CombineAccrued(state);
                _runTallyLabel.Text = _runsUsed > 1
                    ? string.Format(RunTallyFormat, runningTotal.FundsDelta, runningTotal.DetectionRiskDelta)
                    : string.Empty;
                ShowPanel(_resultPanel);
                KillReveal();
                _reveal = ResultReveal.Begin(_resultPanel)
                    .Headline(_headlineLabel, ResolvedHeadlineText,
                        state.FundsDelta >= 0 ? UiColors.Success : UiColors.Warning,
                        state.FundsDelta >= 0 ? UiSound.Cash : UiSound.DayTick)
                    .CountUp(_takeLabel, TakeFormat, state.FundsDelta)
                    .Line(_lineA, string.Format(HeatLineFormat, state.DetectionRiskDelta))
                    .Line(_lineB, state.HealthCeilingDelta != 0
                        ? string.Format(HealthLineFormat, state.HealthCeilingDelta)
                        : null)
                    .Line(_lineC, string.Format(StressLineFormat, state.StressDelta))
                    .Footer(BuildFooter(includeTally: _runsUsed > 1, includeReUp: _runsUsed < MaxRunsPerDay));
                break;
            }
        }
    }

    /// <summary>The footer controls this result should end with; anything excluded is hidden outright.</summary>
    private Control[] BuildFooter(bool includeTally, bool includeReUp)
    {
        var footer = new List<Control>(3);
        if (includeTally)
        {
            footer.Add(_runTallyLabel);
        }
        else
        {
            _runTallyLabel.Visible = false;
        }
        if (includeReUp)
        {
            footer.Add(_reUpButton);
        }
        else
        {
            _reUpButton.Visible = false;
        }
        footer.Add(_doneButton);
        return footer.ToArray();
    }

    /// <summary>Folds the current run's terminal state into <see cref="_accrued"/> without committing — the in-memory INV-1 accumulator.</summary>
    private HustleResolution CombineAccrued(in HustleState terminal) => FoldAccrued(terminal.ToResolution());

    /// <summary>Resolution-shaped fold for deltas that don't come from a <see cref="HustleState"/> — the N-2 haggle's trust delta.</summary>
    private HustleResolution FoldAccrued(in HustleResolution delta)
    {
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

    private void OnReUpPressed()
    {
        UiSfx.Instance.Play(UiSound.Tap);
        BeginRun();
    }

    private void OnDonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (_state is { Stage: HustleStage.Busted } busted)
        {
            // A bust was never folded into _accrued (Advance skips CombineAccrued
            // on Busted) — fold it now so the seizure's own losses are applied too.
            CombineAccrued(busted);
        }
        if (_accrued.FundsDelta != 0 || _accrued.DetectionRiskDelta != 0 || _accrued.HealthCeilingDelta != 0
            || _accrued.RecklessnessDelta != 0 || _accrued.StressDelta != 0 || _accrued.SupplierTrustDelta != 0
            || _accrued.CrewStandingDelta != 0 || _accrued.SetWatchlistFlag || _accrued.SetBadProductFlag
            || _accrued.SetSpoiledGoodsFlag || _accrued.SetControlsTurfFlag)
        {
            gm.Hustles.ApplyNarcoticsResolution(gm.Career.AvatarPlayerId, _accrued, _sessionDay);
        }
        UiSfx.Instance.Play(UiSound.Back);
        KillReveal();
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
