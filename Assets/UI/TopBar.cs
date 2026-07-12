using DirtAndDiamonds.Core;
using DirtAndDiamonds.Simulation.Life;
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
    private ProgressBar _hungerBar = null!;
    private ProgressBar _sleepBar = null!;
    private ProgressBar _hygieneBar = null!;
    private ProgressBar _socialBar = null!;
    private ProgressBar _fitnessBar = null!;

    private long _shownDay = long.MinValue;
    private double _shownFunds = double.NaN;
    private NeedsState _shownNeeds;
    private bool _needsInitialized;

    public override void _Ready()
    {
        _dayLabel = GetNode<Label>("TopBarLayout/BarRow/DayLabel");
        _fundsLabel = GetNode<Label>("TopBarLayout/BarRow/FundsLabel");
        _hungerBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/HungerRow/HungerBar");
        _sleepBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/SleepRow/SleepBar");
        _hygieneBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/HygieneRow/HygieneBar");
        _socialBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/SocialRow/SocialBar");
        _fitnessBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/FitnessRow/FitnessBar");
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

        if (!gm.LifeSim.TryGetNeeds(gm.Career.AvatarPlayerId, out NeedsState needs))
        {
            needs = NeedsState.FullySatisfied();
        }
        if (!_needsInitialized || !NeedsEqual(needs, _shownNeeds))
        {
            _needsInitialized = true;
            _shownNeeds = needs;
            _hungerBar.Value = needs.Hunger;
            _sleepBar.Value = needs.Sleep;
            _hygieneBar.Value = needs.Hygiene;
            _socialBar.Value = needs.Social;
            _fitnessBar.Value = needs.Fitness;
        }
    }

    private static bool NeedsEqual(in NeedsState a, in NeedsState b) =>
        a.Hunger == b.Hunger && a.Sleep == b.Sleep && a.Hygiene == b.Hygiene
        && a.Social == b.Social && a.Fitness == b.Fitness;
}
