using System;
using System.Collections.Generic;
using System.Text;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Economy.Hustles;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Hustles;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Always-present overlay (permanent sibling under Main, per Main.tscn) for an
/// armed Poker <see cref="PendingHustleSession"/> (docs/design/hustles_texas_holdem.md
/// §10/§8d Layer 3): a stakes/buy-in start panel seats a <see cref="HoldemSessionState"/>,
/// then the table panel drives the interactive
/// <see cref="HoldemHandState.StartHand"/>/<see cref="HoldemHandState.SubmitHeroAction"/>
/// contract directly, one hand at a time, entirely in memory — nothing reaches
/// the database until <see cref="OnDonePressed"/> applies the session's single
/// net <see cref="HustleResolution"/> via <see cref="HustleService.ApplyHoldemResolution"/>.
/// Same abandon-evaporates-cleanly forfeit discipline as
/// <see cref="NarcoticsHustleScreen"/>/<see cref="FencingScreen"/>: a session
/// left mid-hand when the day advances never touches real funds (§9's "no
/// mid-session DB writes"). <see cref="HoldemSession.CompleteHand"/> (bust/raid
/// roll, opponent rebuy, button rotation) is invoked automatically the instant
/// a hand completes, before the player can react — the raid/bust outcome is
/// read off <see cref="HoldemSessionState"/>, never a decision this screen
/// makes. Node paths verified against TexasHoldemTable.tscn (godot_scene_mapper)
/// before this script was written.
/// </summary>
public sealed partial class TexasHoldemTable : Control
{
    [Export]
    public string CantAffordText { get; set; } =
        "Not enough funds for this stakes tier — lower the stakes or bring more funds.";

    [Export]
    public string PotFormat { get; set; } = "Pot: ${0}  |  Current bet: ${1}";

    [Export]
    public string CallFormat { get; set; } = "Call ${0}";

    [Export]
    public string RaiseFormat { get; set; } = "Raise to ${0}";

    [Export]
    public string HandEndFormat { get; set; } = "{0}. Net chips: {1}{2} (rake ${3}).";

    [Export]
    public string SessionEndFormat { get; set; } =
        "{0}. Net funds {1:+0;-0;0}. Detection {2:+0;-0;0}, Recklessness {3:+0;-0;0}, Stress {4:+0;-0;0:F0}.";

    private Label _statusLabel = null!;

    private VBoxContainer _startPanel = null!;
    private OptionButton _tierOption = null!;
    private HSlider _opponentsSlider = null!;
    private Label _opponentsValueLabel = null!;
    private HSlider _buyInSlider = null!;
    private Label _buyInValueLabel = null!;
    private Button _sitDownButton = null!;

    private VBoxContainer _tablePanel = null!;
    private RichTextLabel _boardLabel = null!;
    private RichTextLabel _heroCardsLabel = null!;
    private Label _potLabel = null!;
    private Label _sessionStatsLabel = null!;
    private Label _seatsLabel = null!;
    private Label _actionLogLabel = null!;

    private HBoxContainer _actionPanel = null!;
    private Button _foldButton = null!;
    private Button _checkCallButton = null!;
    private HBoxContainer _raiseBox = null!;
    private HSlider _raiseSlider = null!;
    private Label _raiseValueLabel = null!;
    private Button _raiseButton = null!;

    private VBoxContainer _handEndPanel = null!;
    private Label _handResultLabel = null!;
    private Button _nextHandButton = null!;
    private Button _standUpButton = null!;

    private VBoxContainer _resultPanel = null!;
    private Label _resultLabel = null!;
    private Button _doneButton = null!;

    private bool _sessionActive;
    private long _sessionDay = -1;
    private HoldemContext _ctx;
    private RngState _rng;
    private HoldemSessionState? _session;

    /// <summary>§6 polish — the hero's stack after each completed hand this session, seeded with the buy-in; renders as <see cref="BuildSparkline"/>'s bankroll graph. UI-local, never persisted (mirrors <see cref="HoldemSessionState.HandsPlayed"/>'s own presentation-only counterpart).</summary>
    private readonly List<long> _stackHistory = new();

    public override void _Ready()
    {
        _statusLabel = GetNode<Label>("Panel/Layout/StatusLabel");

        _startPanel = GetNode<VBoxContainer>("Panel/Layout/StartPanel");
        _tierOption = GetNode<OptionButton>("Panel/Layout/StartPanel/TierRow/TierOption");
        _opponentsSlider = GetNode<HSlider>("Panel/Layout/StartPanel/OpponentsRow/OpponentsSlider");
        _opponentsValueLabel = GetNode<Label>("Panel/Layout/StartPanel/OpponentsRow/OpponentsValueLabel");
        _buyInSlider = GetNode<HSlider>("Panel/Layout/StartPanel/BuyInRow/BuyInSlider");
        _buyInValueLabel = GetNode<Label>("Panel/Layout/StartPanel/BuyInRow/BuyInValueLabel");
        _sitDownButton = GetNode<Button>("Panel/Layout/StartPanel/SitDownButton");

        _tablePanel = GetNode<VBoxContainer>("Panel/Layout/TablePanel");
        _boardLabel = GetNode<RichTextLabel>("Panel/Layout/TablePanel/BoardLabel");
        _heroCardsLabel = GetNode<RichTextLabel>("Panel/Layout/TablePanel/HeroCardsLabel");
        _potLabel = GetNode<Label>("Panel/Layout/TablePanel/PotLabel");
        _sessionStatsLabel = GetNode<Label>("Panel/Layout/TablePanel/SessionStatsLabel");
        _seatsLabel = GetNode<Label>("Panel/Layout/TablePanel/SeatsLabel");
        _actionLogLabel = GetNode<Label>("Panel/Layout/TablePanel/ActionLogLabel");

        _actionPanel = GetNode<HBoxContainer>("Panel/Layout/TablePanel/ActionPanel");
        _foldButton = GetNode<Button>("Panel/Layout/TablePanel/ActionPanel/FoldButton");
        _checkCallButton = GetNode<Button>("Panel/Layout/TablePanel/ActionPanel/CheckCallButton");
        _raiseBox = GetNode<HBoxContainer>("Panel/Layout/TablePanel/ActionPanel/RaiseBox");
        _raiseSlider = GetNode<HSlider>("Panel/Layout/TablePanel/ActionPanel/RaiseBox/RaiseSlider");
        _raiseValueLabel = GetNode<Label>("Panel/Layout/TablePanel/ActionPanel/RaiseBox/RaiseValueLabel");
        _raiseButton = GetNode<Button>("Panel/Layout/TablePanel/ActionPanel/RaiseBox/RaiseButton");

        _handEndPanel = GetNode<VBoxContainer>("Panel/Layout/TablePanel/HandEndPanel");
        _handResultLabel = GetNode<Label>("Panel/Layout/TablePanel/HandEndPanel/HandResultLabel");
        _nextHandButton = GetNode<Button>("Panel/Layout/TablePanel/HandEndPanel/HandEndButtonsRow/NextHandButton");
        _standUpButton = GetNode<Button>("Panel/Layout/TablePanel/HandEndPanel/HandEndButtonsRow/StandUpButton");

        _resultPanel = GetNode<VBoxContainer>("Panel/Layout/ResultPanel");
        _resultLabel = GetNode<Label>("Panel/Layout/ResultPanel/ResultLabel");
        _doneButton = GetNode<Button>("Panel/Layout/ResultPanel/DoneButton");

        _tierOption.ItemSelected += _ => RefreshTierBounds();
        _opponentsSlider.ValueChanged += _ => _opponentsValueLabel.Text = ((int)_opponentsSlider.Value).ToString();
        _buyInSlider.ValueChanged += _ => _buyInValueLabel.Text = ((long)_buyInSlider.Value).ToString();
        _sitDownButton.Pressed += OnSitDownPressed;

        _foldButton.Pressed += OnFoldPressed;
        _checkCallButton.Pressed += OnCheckCallPressed;
        _raiseSlider.ValueChanged += _ =>
        {
            long amount = (long)_raiseSlider.Value;
            _raiseValueLabel.Text = amount.ToString();
            _raiseButton.Text = string.Format(RaiseFormat, amount);
        };
        _raiseButton.Pressed += OnRaisePressed;

        _nextHandButton.Pressed += OnNextHandPressed;
        _standUpButton.Pressed += OnStandUpPressed;
        _doneButton.Pressed += OnDonePressed;
    }

    public override void _ExitTree()
    {
        _sitDownButton.Pressed -= OnSitDownPressed;
        _foldButton.Pressed -= OnFoldPressed;
        _checkCallButton.Pressed -= OnCheckCallPressed;
        _raiseButton.Pressed -= OnRaisePressed;
        _nextHandButton.Pressed -= OnNextHandPressed;
        _standUpButton.Pressed -= OnStandUpPressed;
        _doneButton.Pressed -= OnDonePressed;
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        bool isPending = gm.TryGetPendingHustleSession(out PendingHustleSession session)
            && session.Activity == WorkActivity.Poker;
        if (!isPending)
        {
            Visible = false;
            _sessionActive = false;
            _session = null;
            return;
        }

        if (!_sessionActive || _sessionDay != session.Day)
        {
            ResetToStartPanel(gm, session.Day);
        }
        Visible = true;
    }

    // ------------------------------------------------------------------
    // Start panel — stakes/opponents/buy-in, then seat the session
    // ------------------------------------------------------------------

    private void ResetToStartPanel(GameManager gm, long day)
    {
        _sessionActive = true;
        _sessionDay = day;
        _session = null;
        _stackHistory.Clear();
        _ctx = gm.Hustles.BuildHoldemContext(gm.Career.AvatarPlayerId);
        _rng = new RngState(unchecked((ulong)System.Environment.TickCount64) ^ 0x504F4B4552UL | 1UL);

        _tierOption.Selected = 0;
        _opponentsSlider.Value = _opponentsSlider.MaxValue;
        _opponentsValueLabel.Text = ((int)_opponentsSlider.Value).ToString();
        RefreshTierBounds();
        ShowPanel(_startPanel);
    }

    private void RefreshTierBounds()
    {
        var tier = (StakesTier)_tierOption.Selected;
        HoldemProfile.StakesTierProfile profile = HoldemProfile.GetTier(tier);
        long maxAffordable = Math.Min((long)_ctx.Funds, profile.BuyInMax);
        bool canAfford = maxAffordable >= profile.BuyInMin;
        _buyInSlider.MinValue = profile.BuyInMin;
        _buyInSlider.MaxValue = canAfford ? maxAffordable : profile.BuyInMin;
        _buyInSlider.Value = _buyInSlider.MinValue;
        _buyInValueLabel.Text = ((long)_buyInSlider.Value).ToString();
        _sitDownButton.Disabled = !canAfford;
        _statusLabel.Text = canAfford ? string.Empty : CantAffordText;
    }

    private void OnSitDownPressed()
    {
        var tier = (StakesTier)_tierOption.Selected;
        int numOpponents = (int)_opponentsSlider.Value;
        long buyIn = (long)_buyInSlider.Value;

        _session = HoldemSession.StartSession(in _ctx, tier, buyIn, numOpponents, ref _rng);
        _stackHistory.Add(buyIn);
        _session.Table.StartHand(ref _rng);
        AdvanceAfterHandIfComplete();
        RefreshTableDisplay();
    }

    // ------------------------------------------------------------------
    // Table panel — one hand at a time
    // ------------------------------------------------------------------

    private void OnFoldPressed() => SubmitHeroAction(HeroAction.Fold());

    private void OnCheckCallPressed() => SubmitHeroAction(HeroAction.CheckOrCall());

    private void OnRaisePressed() => SubmitHeroAction(HeroAction.RaiseTo((long)_raiseSlider.Value));

    private void SubmitHeroAction(HeroAction action)
    {
        _session!.Table.SubmitHeroAction(action, ref _rng);
        AdvanceAfterHandIfComplete();
        RefreshTableDisplay();
    }

    private void OnNextHandPressed()
    {
        _session!.Table.StartHand(ref _rng);
        AdvanceAfterHandIfComplete();
        RefreshTableDisplay();
    }

    private void OnStandUpPressed()
    {
        HoldemSession.StandUp(_session!);
        RefreshTableDisplay();
    }

    /// <summary>Folds bust/raid/rebuy/button-rotation bookkeeping the instant a hand ends — before the player sees the next panel, per this screen's own doc comment.</summary>
    private void AdvanceAfterHandIfComplete()
    {
        if (_session is { IsOver: false } session && session.Table.HandComplete)
        {
            HoldemSession.CompleteHand(session, in _ctx, ref _rng);
            _stackHistory.Add(session.Table.Stack[session.HeroSeat]);
        }
    }

    private void RefreshTableDisplay()
    {
        HoldemSessionState session = _session!;
        if (session.IsOver)
        {
            ShowResultSummary(session);
            ShowPanel(_resultPanel);
            return;
        }

        ShowPanel(_tablePanel);
        HoldemHandState table = session.Table;
        _boardLabel.Text = "Board: " + FormatCardsBbcode(table.Board.AsSpan(0, table.BoardCount));
        _heroCardsLabel.Text = "Your hand: " + FormatCardsBbcode(
            new[] { table.HoleCards[session.HeroSeat * 2], table.HoleCards[session.HeroSeat * 2 + 1] });
        _potLabel.Text = string.Format(PotFormat, table.Pot, table.CurrentBet);
        _sessionStatsLabel.Text = BuildSessionStats(session, table);
        _seatsLabel.Text = BuildSeatsSummary(table);
        _actionLogLabel.Text = BuildActionLog(table);

        if (table.HandComplete)
        {
            _handResultLabel.Text = BuildHandEndSummary(session, table);
            _actionPanel.Visible = false;
            _handEndPanel.Visible = true;
        }
        else
        {
            RefreshActionControls(session, table);
            _actionPanel.Visible = true;
            _handEndPanel.Visible = false;
        }
    }

    private void RefreshActionControls(HoldemSessionState session, HoldemHandState table)
    {
        int heroSeat = session.HeroSeat;
        long owe = Math.Max(0, table.CurrentBet - table.CommittedStreet[heroSeat]);
        long payIfCall = Math.Min(owe, table.Stack[heroSeat]);
        _checkCallButton.Text = owe <= 0 ? "Check" : string.Format(CallFormat, payIfCall);

        long available = table.Stack[heroSeat] + table.CommittedStreet[heroSeat];
        bool canRaise = available > table.CurrentBet;
        _raiseBox.Visible = canRaise;
        if (canRaise)
        {
            long minLegalTotal = Math.Min(table.CurrentBet + table.MinRaiseIncrement, available);
            _raiseSlider.MinValue = minLegalTotal;
            _raiseSlider.MaxValue = available;
            _raiseSlider.Value = minLegalTotal;
            _raiseValueLabel.Text = minLegalTotal.ToString();
            _raiseButton.Text = string.Format(RaiseFormat, minLegalTotal);
        }
    }

    private void ShowResultSummary(HoldemSessionState session)
    {
        string reason = session.Busted ? "Busted out" : session.Raided ? "Raided" : "Stood up";
        _resultLabel.Text = string.Format(
            SessionEndFormat, reason, session.FundsDelta, session.DetectionRiskDelta,
            session.RecklessnessDelta, session.StressDelta);
    }

    private void OnDonePressed()
    {
        GameManager gm = GameManager.Instance!;
        if (_session is { IsOver: true } session)
        {
            gm.Hustles.ApplyHoldemResolution(gm.Career.AvatarPlayerId, session.ToResolution(), _sessionDay);
        }
        gm.ClearPendingHustleSession();
        _sessionActive = false;
        _session = null;
        Visible = false;
    }

    private void ShowPanel(Control panel)
    {
        _startPanel.Visible = ReferenceEquals(panel, _startPanel);
        _tablePanel.Visible = ReferenceEquals(panel, _tablePanel);
        _resultPanel.Visible = ReferenceEquals(panel, _resultPanel);
    }

    // ------------------------------------------------------------------
    // Text formatting — event-driven (button presses), never per-frame
    // ------------------------------------------------------------------

    /// <summary>§6 polish — suit pips + rank in BBCode (red for hearts/diamonds) for the <see cref="RichTextLabel"/> board/hero lines, replacing the old plain-text "Ac Kd" debug format. Reads <see cref="Card.Rank"/>/<see cref="Card.Suit"/> directly rather than <see cref="Card.ToString"/>, which stays the harness/debug-only format it was documented as (never a UI dependency).</summary>
    private static readonly char[] SuitGlyphs = { '♣', '♦', '♥', '♠' };
    private const string RedSuitColor = "#e0524f";

    private static string FormatCardsBbcode(ReadOnlySpan<Card> cards)
    {
        if (cards.Length == 0)
        {
            return "—";
        }
        var sb = new StringBuilder();
        for (int i = 0; i < cards.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }
            AppendCardBbcode(sb, cards[i]);
        }
        return sb.ToString();
    }

    private static void AppendCardBbcode(StringBuilder sb, Card card)
    {
        char rank = card.Rank switch
        {
            14 => 'A',
            13 => 'K',
            12 => 'Q',
            11 => 'J',
            10 => 'T',
            _ => (char)('0' + card.Rank),
        };
        char suit = SuitGlyphs[card.Suit];
        bool red = card.Suit is 1 or 2; // diamonds, hearts (Card.Suit packing: 0=c,1=d,2=h,3=s)
        if (red)
        {
            sb.Append("[color=").Append(RedSuitColor).Append(']');
        }
        sb.Append(rank).Append(suit);
        if (red)
        {
            sb.Append("[/color]");
        }
    }

    /// <summary>§6 polish — hands-played counter + a compact Unicode-block sparkline of the hero's post-hand stack across the session, both reading only <see cref="HoldemSessionState"/>/<see cref="_stackHistory"/> (no sim touch).</summary>
    private static readonly char[] SparkLevels = { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    private string BuildSessionStats(HoldemSessionState session, HoldemHandState table)
    {
        long currentStack = table.Stack[session.HeroSeat];
        string handWord = session.HandsPlayed == 1 ? "hand" : "hands";
        string spark = BuildSparkline(_stackHistory);
        return spark.Length > 0
            ? $"{session.HandsPlayed} {handWord} played   {spark}   ${currentStack}"
            : $"{session.HandsPlayed} {handWord} played   ${currentStack}";
    }

    private static string BuildSparkline(List<long> history)
    {
        if (history.Count < 2)
        {
            return string.Empty;
        }
        long min = long.MaxValue, max = long.MinValue;
        foreach (long v in history)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        long range = Math.Max(1, max - min);
        var sb = new StringBuilder(history.Count);
        foreach (long v in history)
        {
            int level = (int)((v - min) * (SparkLevels.Length - 1) / range);
            sb.Append(SparkLevels[level]);
        }
        return sb.ToString();
    }

    private static string BuildSeatsSummary(HoldemHandState table)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < table.SeatCount; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            sb.Append(table.IsHero[i] ? "You" : $"Seat {i}");
            sb.Append(": $").Append(table.Stack[i]);
            if (table.Status[i] == SeatStatus.Folded)
            {
                sb.Append(" (folded)");
            }
            else if (table.Status[i] == SeatStatus.AllIn)
            {
                sb.Append(" (all-in)");
            }
            if (!table.HandComplete && i == table.ActingSeat)
            {
                sb.Append(" <- acting");
            }
        }
        return sb.ToString();
    }

    private static string BuildActionLog(HoldemHandState table)
    {
        if (table.ActionLogCount == 0)
        {
            return string.Empty;
        }
        var sb = new StringBuilder();
        for (int i = 0; i < table.ActionLogCount; i++)
        {
            HandActionLogEntry e = table.ActionLog[i];
            if (i > 0)
            {
                sb.Append('\n');
            }
            string who = table.IsHero[e.SeatIndex] ? "You" : $"Seat {e.SeatIndex}";
            string verb = e.Kind switch
            {
                HeroAction.ActionKind.Fold => "folds",
                HeroAction.ActionKind.CheckOrCall => e.Amount > 0 ? $"calls to ${e.Amount}" : "checks",
                HeroAction.ActionKind.RaiseTo => $"raises to ${e.Amount}",
                _ => "acts",
            };
            sb.Append('[').Append(e.Street).Append("] ").Append(who).Append(' ').Append(verb);
        }
        return sb.ToString();
    }

    private string BuildHandEndSummary(HoldemSessionState session, HoldemHandState table)
    {
        HandResult result = table.Result;
        long net = result.NetChipChange[session.HeroSeat];
        string showdown = result.WentToShowdown ? "Showdown" : "Won uncontested";
        return string.Format(HandEndFormat, showdown, net >= 0 ? "+" : "", net, result.Rake);
    }
}
