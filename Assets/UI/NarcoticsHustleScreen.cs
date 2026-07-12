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
/// </summary>
public sealed partial class NarcoticsHustleScreen : Control
{
    private const int MaxRunsPerDay = 3;

    [Export]
    public string CantAffordText { get; set; } = "Not enough funds/trust to run this hustle today.";

    [Export]
    public string BustedFormat { get; set; } = "Busted! Lost ${0:F0}. Detection +{1}, Stress +{2:F0}.";

    [Export]
    public string ResolvedFormat { get; set; } =
        "Deal done. Net funds {0:+0;-0;0}. Detection {1:+0;-0;0}, Health {2:+0;-0;0}, Stress {3:+0;-0;0:F0}.";

    [Export]
    public string RunTallyFormat { get; set; } = "Session total so far: funds {0:+0;-0;0}, detection {1:+0;-0;0}.";

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
    private Label _runTallyLabel = null!;
    private Button _reUpButton = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private HustleContext _baseCtx;
    private HustleContext _ctx;
    private RngState _rng;
    private HustleState? _state;
    private HustleResolution _accrued;
    private int _runsUsed;

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
        _runTallyLabel = GetNode<Label>("Panel/Layout/ResultPanel/RunTallyLabel");
        _reUpButton = GetNode<Button>("Panel/Layout/ResultPanel/ReUpButton");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _buyInSlider.ValueChanged += _ => _buyInValueLabel.Text = ((int)_buyInSlider.Value).ToString();
        _cutSlider.ValueChanged += _ => _cutValueLabel.Text = _cutSlider.Value.ToString("F1");
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

    /// <summary>
    /// Starts one run (the first, or a re-up): rebuilds <see cref="_ctx"/> from
    /// the base snapshot nudged by everything <see cref="_accrued"/> so far —
    /// funds reflect prior takes/losses, heat reflects prior detection deltas —
    /// so a hot run's own heat feeds into the next drop's pSeize (§3.2's
    /// built-in tail scaling). No DB read; the live DB row never moves mid-session.
    /// </summary>
    private void BeginRun()
    {
        _state = null;
        double liveFunds = Math.Max(0, _baseCtx.Funds + _accrued.FundsDelta);
        double liveHeat = Math.Clamp(_baseCtx.Heat + _accrued.DetectionRiskDelta / 100.0, 0, 1);
        _ctx = new HustleContext(
            liveFunds, liveHeat, _baseCtx.Reck, _baseCtx.SupplierTrust,
            _baseCtx.CrewStandingLocal + _accrued.CrewStandingDelta, _baseCtx.OwnsTurfLocal, _baseCtx.UsesProduct);

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
                // A seizure ends the session outright — no re-up after a bust
                // (you're already burned; §3.2 only offers re-up after a clean resolve).
                _reUpButton.Visible = false;
                _runTallyLabel.Text = string.Empty;
                ShowPanel(_resultPanel);
                break;
            case HustleStage.Resolved:
                _resultLabel.Text = string.Format(
                    ResolvedFormat, state.FundsDelta, state.DetectionRiskDelta, state.HealthCeilingDelta, state.StressDelta);
                _runsUsed++;
                HustleResolution runningTotal = CombineAccrued(state);
                _reUpButton.Visible = _runsUsed < MaxRunsPerDay;
                _runTallyLabel.Text = _runsUsed > 1
                    ? string.Format(RunTallyFormat, runningTotal.FundsDelta, runningTotal.DetectionRiskDelta)
                    : string.Empty;
                ShowPanel(_resultPanel);
                break;
        }
    }

    /// <summary>Folds the current run's terminal state into <see cref="_accrued"/> without committing — the in-memory INV-1 accumulator.</summary>
    private HustleResolution CombineAccrued(in HustleState terminal)
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

    private void OnReUpPressed() => BeginRun();

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
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        Visible = false;
    }
}
