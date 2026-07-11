using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.UI.Scouting;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// UI-reorg: the "Player" phone tab — the avatar's Scouting Report,
/// Development, and Season Stats cards, moved verbatim out of
/// BaseballDashboard's retired MeRow (see BaseballDashboard.cs's UI-reorg
/// note). Self-contained and self-driving, the CalendarScreen/ScheduleScreen
/// precedent: it polls GameManager.Instance directly rather than being
/// pushed data by BurnerPhone.cs, so adding it as a phone tab is a pure
/// .tscn reparent with zero BurnerPhone.cs changes. Refreshes once per
/// day-advance settle point (day-advance is the only thing that can move
/// ratings — PED costs, the offseason development pass), dirty-flagged on
/// (current day, events drained) exactly like BaseballDashboard's old
/// RefreshScoutingCard cadence, rather than per-frame. Node paths verified
/// against PlayerScreen.tscn before this script was written.
/// </summary>
public sealed partial class PlayerScreen : PanelContainer
{
    [Export]
    public string OfpFormat { get; set; } = "OFP: {0} ({1})";

    [Export]
    public string TierFormat { get; set; } = "{0}";

    /// <summary>Comma-separated player-facing tier names, in LeagueTier enum order (HS…MLB).</summary>
    [Export]
    public string TierNamesCsv { get; set; } = "High School,College,Class A,Double-A,Triple-A,MLB";

    /// <summary>How the tier chip reads for a High-School avatar: tier name + academic grade (e.g. "High School · Junior").</summary>
    [Export]
    public string TierWithGradeFormat { get; set; } = "{0} · {1}";

    /// <summary>Comma-separated player-facing grade names, in SchoolGrade enum order (Freshman…Senior).</summary>
    [Export]
    public string SchoolGradeNamesCsv { get; set; } = "Freshman,Sophomore,Junior,Senior";

    // '›' rather than '→': the vendored Barlow faces cover Latin punctuation
    // but not the arrows block, and a tofu glyph here would ship on every
    // scouting row.
    [Export]
    public string ToolGradeFormat { get; set; } = "{0} {1} › {2} {3}";

    [Export]
    public string DevFormat { get; set; } = "Season {0}: {1} players moved, +{2} / -{3} pts";

    [Export]
    public string DevNoneText { get; set; } = "No offseason development yet.";

    // SB and SV are deliberately absent: both flush sites hard-code them to 0
    // (sv always 0 is a disclosed v4 artifact) — the card must not surface a
    // column the sim never populates.
    [Export]
    public string StatLineBattingFormat { get; set; } =
        "{0}-for-{1}, {2} HR, {3} RBI, {4} BB, {5} SO\nAVG {6:0.000} · OBP {7:0.000} · SLG {8:0.000} · OPS {9:0.000}";

    [Export]
    public string StatLinePitchingFormat { get; set; } =
        "{0}-{1}, {2:0.0} IP, {3} SO, {4} BB\nERA {5:0.00} · WHIP {6:0.00}";

    [Export]
    public string StatLineNoneText { get; set; } = "No stats yet this season.";

    [Export]
    public string BatPowerName { get; set; } = "Power";

    [Export]
    public string BatContactName { get; set; } = "Contact";

    [Export]
    public string BatDisciplineName { get; set; } = "Discipline";

    [Export]
    public string PitStuffName { get; set; } = "Stuff";

    [Export]
    public string PitControlName { get; set; } = "Control";

    [Export]
    public string PitStaminaName { get; set; } = "Stamina";

    [Export]
    public string FieldingName { get; set; } = "Fielding";

    private Label _ofpLabel = null!;
    private Label _tierLabel = null!;
    private readonly ToolRowRefs[] _toolRows = new ToolRowRefs[4];
    private Label _devSummaryLabel = null!;
    private Label _statLineLabel = null!;
    private string[] _tierNames = System.Array.Empty<string>();
    private string[] _gradeNames = System.Array.Empty<string>();

    // Dirty-flag identity (ui_conventions.md: no per-frame string
    // formatting/LINQ) — refresh once per day, exactly when the day's
    // deferred events (gritty consequences, offseason dev pass) have
    // finished draining, the same settle point BaseballDashboard's old
    // RefreshScoutingCard waited on.
    private long _shownDay = -1;

    private readonly struct ToolRowRefs
    {
        public readonly Label NameLabel;
        public readonly ProgressBar Bar;
        public readonly Label GradeLabel;

        public ToolRowRefs(Label nameLabel, ProgressBar bar, Label gradeLabel)
        {
            NameLabel = nameLabel;
            Bar = bar;
            GradeLabel = gradeLabel;
        }
    }

    public override void _Ready()
    {
        _ofpLabel = GetNode<Label>("Layout/ScoutingCard/ScoutingLayout/OfpRow/OfpLabel");
        _tierLabel = GetNode<Label>("Layout/ScoutingCard/ScoutingLayout/OfpRow/TierChip/TierLabel");
        for (int i = 0; i < _toolRows.Length; i++)
        {
            string rowPath = $"Layout/ScoutingCard/ScoutingLayout/ToolsList/ToolRow{i}";
            _toolRows[i] = new ToolRowRefs(
                GetNode<Label>($"{rowPath}/NameLabel"),
                GetNode<ProgressBar>($"{rowPath}/Bar"),
                GetNode<Label>($"{rowPath}/GradeLabel"));
        }
        _devSummaryLabel = GetNode<Label>("Layout/DevCard/DevLayout/DevSummaryLabel");
        _statLineLabel = GetNode<Label>("Layout/StatLineCard/StatLineLayout/StatLineLabel");

        _tierNames = TierNamesCsv.Split(',');
        _gradeNames = SchoolGradeNamesCsv.Split(',');
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        CareerManager career = gm.Career;
        bool hasAvatar = career.HasAvatar;
        Visible = hasAvatar;
        if (!hasAvatar)
        {
            return;
        }

        long day = gm.State.CurrentDay;
        if (day == _shownDay || gm.Events.PendingCount > 0)
        {
            return;
        }
        _shownDay = day;
        Refresh(gm, career);
    }

    private void Refresh(GameManager gm, CareerManager career)
    {
        string avatarId = career.AvatarPlayerId;
        if (!gm.Players.TryGetById(avatarId, out PlayerRow player)
            || !gm.Baseball.TryGetRatings(avatarId, out PlayerRatingsRow ratings)
            || !gm.Baseball.TryGetPotential(avatarId, out PlayerPotentialRow potential))
        {
            return;
        }

        bool isPitcher = ratings.IsPitcher;
        var roster = new RosterPlayerRow
        {
            PlayerId = avatarId,
            FirstName = player.FirstName,
            LastName = player.LastName,
            TeamId = player.TeamId ?? 0,
            IsPitcher = isPitcher,
            BatPower = ratings.BatPower,
            BatContact = ratings.BatContact,
            BatDiscipline = ratings.BatDiscipline,
            PitStuff = ratings.PitStuff,
            PitControl = ratings.PitControl,
            PitStamina = ratings.PitStamina,
            Fielding = ratings.Fielding,
        };
        int headroom = PromotionScore.Headroom(in roster, in potential, isPitcher);
        int roleRatingSum = isPitcher
            ? ratings.PitStuff + ratings.PitControl + ratings.PitStamina
            : ratings.BatPower + ratings.BatContact + ratings.BatDiscipline;
        int ofp = ScoutingGrade.OfpRating(roleRatingSum, player.Age, headroom);
        _ofpLabel.Text = string.Format(OfpFormat, ScoutingGrade.Label(ofp), ofp);

        bool tierKnown = gm.Baseball.TryGetTeamTier(player.TeamId ?? 0, out LeagueTier tier);
        // A High-School avatar's chip carries its academic grade (freshman …
        // senior), derived from age — the SchoolGrade the promotion gate reads.
        string tierText = tierKnown
            ? (tier == LeagueTier.HS
                ? string.Format(TierWithGradeFormat, TierName(tier), GradeName(SchoolGrades.ForAge(player.Age)))
                : TierName(tier))
            : string.Empty;
        _tierLabel.Text = string.Format(TierFormat, tierText);

        if (isPitcher)
        {
            SetToolRow(0, PitStuffName, ratings.PitStuff, potential.PitStuff);
            SetToolRow(1, PitControlName, ratings.PitControl, potential.PitControl);
            SetToolRow(2, PitStaminaName, ratings.PitStamina, potential.PitStamina);
        }
        else
        {
            SetToolRow(0, BatPowerName, ratings.BatPower, potential.BatPower);
            SetToolRow(1, BatContactName, ratings.BatContact, potential.BatContact);
            SetToolRow(2, BatDisciplineName, ratings.BatDiscipline, potential.BatDiscipline);
        }
        SetToolRow(3, FieldingName, ratings.Fielding, potential.Fielding);

        DevelopmentSummary dev = gm.Development.LastRun;
        _devSummaryLabel.Text = dev.SeasonYear <= 0
            ? DevNoneText
            : string.Format(DevFormat, dev.SeasonYear, dev.PlayersChanged, dev.PointsUp, dev.PointsDown);

        RefreshStatLine(gm, avatarId, isPitcher);
    }

    private void RefreshStatLine(GameManager gm, string avatarId, bool isPitcher)
    {
        int seasonYear = gm.State.SeasonYear;
        if (isPitcher)
        {
            _statLineLabel.Text = gm.Baseball.TryGetPitchingSeasonLine(avatarId, seasonYear, out PitchingSeasonLine line)
                ? string.Format(
                    StatLinePitchingFormat, line.W, line.L, line.Ip, line.So, line.Bb, line.Era, line.Whip)
                : StatLineNoneText;
        }
        else
        {
            _statLineLabel.Text = gm.Baseball.TryGetBattingSeasonLine(avatarId, seasonYear, out BattingSeasonLine line)
                ? string.Format(
                    StatLineBattingFormat, line.H, line.Ab, line.Hr, line.Rbi, line.Bb, line.So,
                    line.Avg, line.Obp, line.Slg, line.Ops)
                : StatLineNoneText;
        }
    }

    private void SetToolRow(int index, string toolName, int current, int potential)
    {
        ToolRowRefs row = _toolRows[index];
        row.NameLabel.Text = toolName;
        row.Bar.Value = current;
        row.GradeLabel.Text = string.Format(
            ToolGradeFormat, ScoutingGrade.Label(current), current, ScoutingGrade.Label(potential), potential);
    }

    private string TierName(LeagueTier tier)
    {
        int index = (int)tier;
        return index >= 0 && index < _tierNames.Length ? _tierNames[index] : tier.ToString();
    }

    private string GradeName(SchoolGrade grade)
    {
        int index = (int)grade;
        return index >= 0 && index < _gradeNames.Length ? _gradeNames[index] : grade.ToString();
    }
}
