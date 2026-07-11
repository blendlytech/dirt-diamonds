using DirtAndDiamonds.Core;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// The persistent shell header (TwoPanelShell's first row, above the
/// dashboard/phone panels): the sim day counter — the single day readout,
/// migrated from BaseballDashboard's header — and the avatar's funds, so
/// money is visible without opening the phone's Bank tab. Pure reads off
/// GameManager's in-memory state (GlobalState day fields, LifeSim funds
/// mirror — no DB touch); renders are dirty-flagged on (day, funds) per
/// ui_conventions, so nothing formats per frame. Node paths verified
/// against TopBar.tscn.
/// </summary>
public sealed partial class TopBar : PanelContainer
{
    [Export]
    public string DayFormat { get; set; } = "Day {0} — Season {1} (day {2} of {3})";

    [Export]
    public string FundsFormat { get; set; } = "${0:N0}";

    private Label _dayLabel = null!;
    private Label _fundsLabel = null!;

    private long _shownDay = long.MinValue;
    private double _shownFunds = double.NaN;

    public override void _Ready()
    {
        _dayLabel = GetNode<Label>("BarRow/DayLabel");
        _fundsLabel = GetNode<Label>("BarRow/FundsLabel");
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Career.HasAvatar)
        {
            Visible = false;
            return;
        }
        Visible = true;

        GlobalState state = gm.State;
        if (state.CurrentDay != _shownDay)
        {
            _shownDay = state.CurrentDay;
            _dayLabel.Text = string.Format(
                DayFormat, state.CurrentDay, state.SeasonYear, state.DayOfSeason, GlobalState.DaysPerSeason);
        }

        if (!gm.LifeSim.TryGetFunds(gm.Career.AvatarPlayerId, out double funds))
        {
            funds = 0.0;
        }
        if (funds != _shownFunds)
        {
            _shownFunds = funds;
            _fundsLabel.Text = string.Format(FundsFormat, funds);
        }
    }
}
