using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data.Schools;
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

    private TextureRect _schoolLogo = null!;
    private Label _dayLabel = null!;
    private Label _fundsLabel = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _sleepBar = null!;
    private ProgressBar _hygieneBar = null!;
    private ProgressBar _socialBar = null!;
    private ProgressBar _fitnessBar = null!;
    private Label _hungerLabel = null!;
    private Label _sleepLabel = null!;
    private Label _hygieneLabel = null!;
    private Label _socialLabel = null!;
    private Label _fitnessLabel = null!;

    private long _shownDay = long.MinValue;
    private double _shownFunds = double.NaN;
    private NeedsState _shownNeeds;
    private bool _needsInitialized;

    // School-logo identity: re-resolved only when the avatar's team changes
    // (creation, succession, promotion). A school without authored logo art
    // (6 of 8 today) or a non-HS team simply keeps the rect hidden.
    private int _shownLogoTeamId = int.MinValue;

    // Same critical-needs cue as the Bank tab's NeedsCard: one bit per need
    // at/under NeedsEngine.CriticalThreshold, so the label recolor happens
    // only when a need actually crosses the line — the two surfaces can
    // never disagree about when the crisis branch fires.
    private int _shownCriticalMask = -1;

    public override void _Ready()
    {
        _schoolLogo = GetNode<TextureRect>("TopBarLayout/BarRow/SchoolLogo");
        _dayLabel = GetNode<Label>("TopBarLayout/BarRow/DayLabel");
        _fundsLabel = GetNode<Label>("TopBarLayout/BarRow/FundsLabel");
        _hungerBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/HungerRow/HungerBar");
        _sleepBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/SleepRow/SleepBar");
        _hygieneBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/HygieneRow/HygieneBar");
        _socialBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/SocialRow/SocialBar");
        _fitnessBar = GetNode<ProgressBar>("TopBarLayout/NeedsRow/FitnessRow/FitnessBar");
        _hungerLabel = GetNode<Label>("TopBarLayout/NeedsRow/HungerRow/HungerLabel");
        _sleepLabel = GetNode<Label>("TopBarLayout/NeedsRow/SleepRow/SleepLabel");
        _hygieneLabel = GetNode<Label>("TopBarLayout/NeedsRow/HygieneRow/HygieneLabel");
        _socialLabel = GetNode<Label>("TopBarLayout/NeedsRow/SocialRow/SocialLabel");
        _fitnessLabel = GetNode<Label>("TopBarLayout/NeedsRow/FitnessRow/FitnessLabel");

        // Marks are scene-authored at anchor 0.2; re-derived from the live
        // constant so a CriticalThreshold retune can't leave the bar lying
        // about where the crisis branch fires (Bank-tab idiom).
        float criticalAnchor = NeedsEngine.CriticalThreshold / NeedsEngine.MaxNeed;
        SetCriticalMarkAnchor(GetNode<ColorRect>("TopBarLayout/NeedsRow/HungerRow/HungerBar/HungerCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("TopBarLayout/NeedsRow/SleepRow/SleepBar/SleepCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("TopBarLayout/NeedsRow/HygieneRow/HygieneBar/HygieneCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("TopBarLayout/NeedsRow/SocialRow/SocialBar/SocialCriticalMark"), criticalAnchor);
        SetCriticalMarkAnchor(GetNode<ColorRect>("TopBarLayout/NeedsRow/FitnessRow/FitnessBar/FitnessCriticalMark"), criticalAnchor);
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

        int teamId = gm.Career.AvatarTeamId;
        if (teamId != _shownLogoTeamId)
        {
            _shownLogoTeamId = teamId;
            bool hasLogo = false;
            if (gm.Schools.TryGet(teamId, out SchoolDefinition school)
                && SchoolArtLibrary.TryLoad(school.LogoPath, out Texture2D logo))
            {
                _schoolLogo.Texture = logo;
                _schoolLogo.TooltipText = school.DisplayName;
                hasLogo = true;
            }
            _schoolLogo.Visible = hasLogo;
        }

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
            RefreshCriticalNeeds(in needs);
        }
    }

    private void RefreshCriticalNeeds(in NeedsState needs)
    {
        int mask = (needs.Hunger <= NeedsEngine.CriticalThreshold ? 1 : 0)
            | (needs.Sleep <= NeedsEngine.CriticalThreshold ? 2 : 0)
            | (needs.Hygiene <= NeedsEngine.CriticalThreshold ? 4 : 0)
            | (needs.Social <= NeedsEngine.CriticalThreshold ? 8 : 0)
            | (needs.Fitness <= NeedsEngine.CriticalThreshold ? 16 : 0);
        if (mask == _shownCriticalMask)
        {
            return;
        }
        _shownCriticalMask = mask;
        ApplyCriticalColor(_hungerLabel, (mask & 1) != 0);
        ApplyCriticalColor(_sleepLabel, (mask & 2) != 0);
        ApplyCriticalColor(_hygieneLabel, (mask & 4) != 0);
        ApplyCriticalColor(_socialLabel, (mask & 8) != 0);
        ApplyCriticalColor(_fitnessLabel, (mask & 16) != 0);
    }

    private static void ApplyCriticalColor(Label label, bool critical)
    {
        if (critical)
        {
            label.AddThemeColorOverride("font_color", UiColors.Danger);
        }
        else
        {
            label.RemoveThemeColorOverride("font_color");
        }
    }

    private static void SetCriticalMarkAnchor(ColorRect mark, float anchor)
    {
        mark.AnchorLeft = anchor;
        mark.AnchorRight = anchor;
    }

    private static bool NeedsEqual(in NeedsState a, in NeedsState b) =>
        a.Hunger == b.Hunger && a.Sleep == b.Sleep && a.Hygiene == b.Hygiene
        && a.Social == b.Social && a.Fitness == b.Fitness;
}
