using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// UI-reorg: the "League" phone tab — the avatar's tier Standings and League
/// Leaders cards, moved out of BaseballDashboard's retired LeagueRow (see
/// BaseballDashboard.cs's UI-reorg note). 13-7 made both true to schema v14:
/// the standings come from PlayoffManager.GetStandings (the avatar's conference
/// grouped by division, ordered by the same function selection uses) and the
/// leader qualifiers scale with games played. Self-contained and
/// self-driving, the CalendarScreen/ScheduleScreen precedent: it polls
/// GameManager.Instance directly rather than being pushed data by
/// BurnerPhone.cs. Refreshes once per day-advance settle point, dirty-flagged
/// on (current day, events drained), the same cadence BaseballDashboard's old
/// RefreshScoutingCard used. Node paths verified against LeagueScreen.tscn
/// before this script was written.
/// </summary>
public sealed partial class LeagueScreen : PanelContainer
{
    // Avatar's team marked with a plain-ASCII prefix (not a glyph — the
    // vendored Barlow faces don't cover every Unicode block). Games behind is
    // precomputed to a string in C# (always a multiple of 0.5 given integer
    // W/L) so the row format never needs a decimal-format culture call.
    [Export]
    public string StandingRowFormat { get; set; } = "{0}. {1}  {2}-{3}  {4:0.000}  {5}";

    [Export]
    public string StandingsAvatarRowFormat { get; set; } = "* {0}";

    [Export]
    public string StandingsLeaderGamesBehindText { get; set; } = "-";

    /// <summary>Wraps the GB of a team sitting ahead of the pct leader on the GB metric (reachable early-season, e.g. 3-1 under a 1-0 pct leader).</summary>
    [Export]
    public string StandingsAheadGamesBehindFormat { get; set; } = "+{0}";

    // Role-aware (batter sees HR/AVG/OPS leaders, pitcher sees ERA/W/SO).
    // Three row formats cover the three value shapes (plain count, .000 rate,
    // 0.00 ERA); the avatar's own row (if it ranks) gets the same plain-text
    // marker as StandingsAvatarRowFormat.
    [Export]
    public string LeaderRowCountFormat { get; set; } = "{0}. {1} {2} — {3:0}";

    [Export]
    public string LeaderRowAvgFormat { get; set; } = "{0}. {1} {2} — {3:0.000}";

    [Export]
    public string LeaderRowEraFormat { get; set; } = "{0}. {1} {2} — {3:0.00}";

    [Export]
    public string LeaderAvatarRowFormat { get; set; } = "* {0}";

    [Export]
    public string LeaderNoneText { get; set; } = "No qualifying leaders yet.";

    /// <summary>Comma-separated category headings shown for a batting avatar, in HR/AVG/OPS order.</summary>
    [Export]
    public string LeadersBattingCategoriesCsv { get; set; } = "Home Runs,Batting Avg,OPS";

    /// <summary>Comma-separated category headings shown for a pitching avatar, in ERA/W/SO order.</summary>
    [Export]
    public string LeadersPitchingCategoriesCsv { get; set; } = "ERA,Wins,Strikeouts";

    private Label _standingsLabel = null!;
    private Label _leadersLabel = null!;

    // Reusable query-result buffers — cleared and refilled per call, the
    // LoadRoster "destination" idiom, so the once-per-day-advance refresh
    // doesn't allocate a fresh list per category.
    private readonly List<TeamRow> _tierTeamsBuffer = new();
    private readonly List<ConferenceRow> _conferencesBuffer = new();
    private readonly List<DivisionRow> _divisionsBuffer = new();
    private readonly List<string> _standingsLines = new();
    private readonly List<LeagueLeaderRow> _leadersBuffer = new();

    private const int LeaderRowCount = 5;

    // 13-7 D5: leader qualifiers scale with the avatar team's games played G. These
    // are presentation thresholds, not calibration: nothing in the sim reads them.
    //   rate leaders (AVG, OPS): PA   >= ceil(G x regulation innings x 0.344)
    //   ERA leaders:             outs >= ceil(G x regulation innings / 3)
    // 0.344 PA per regulation inning is the familiar 3.1 PA per nine-inning team
    // game (about 2.4 per seven-inning game); 1 IP per nine-inning game is 3 outs.
    // Integer arithmetic keeps the ceilings exact, and the floor of 1 keeps a
    // 0-game season from "qualifying" players with no plate appearances at all.
    private const int QualifyingPaPerThousandInnings = 344;
    private const int OutsPerInning = 3;
    private const int MinQualifier = 1;

    // Dirty-flag identity (ui_conventions.md) — refresh once per (day, avatar),
    // exactly when the day's deferred events have finished draining. The avatar
    // is part of the identity because an interactive succession takeover swaps
    // the avatar without advancing the day (13-6b review S1).
    private long _shownDay = -1;
    private string? _shownAvatarId;

    // 13-6b parent-phase smoke finding: Refresh hid the screen for a team-less
    // (unrostered) avatar, but _Process re-showed it every frame. The tier
    // verdict is remembered so the hide sticks until a rostered avatar returns.
    private bool _tierKnown = true;

    public override void _Ready()
    {
        _standingsLabel = GetNode<Label>("Layout/StandingsCard/StandingsLayout/StandingsLabel");
        _leadersLabel = GetNode<Label>("Layout/LeadersCard/LeadersLayout/LeadersLabel");
        InitDraftCard();
    }

    public override void _Process(double delta)
    {
        GameManager gm = GameManager.Instance!;
        CareerManager career = gm.Career;
        bool hasAvatar = career.HasAvatar;
        Visible = hasAvatar && _tierKnown;
        if (!hasAvatar)
        {
            return;
        }

        // The tier refresh runs first: it settles Visible, which the Draft
        // card's IsVisibleInTree() gate reads in the same frame.
        long day = gm.State.CurrentDay;
        string avatarId = career.AvatarPlayerId;
        if (gm.Events.PendingCount == 0
            && (day != _shownDay || !string.Equals(avatarId, _shownAvatarId, StringComparison.Ordinal)))
        {
            _shownDay = day;
            _shownAvatarId = avatarId;
            Refresh(gm, career);
        }

        RefreshDraftCard(gm, career);
    }

    private void Refresh(GameManager gm, CareerManager career)
    {
        string avatarId = career.AvatarPlayerId;
        if (!gm.Players.TryGetById(avatarId, out PlayerRow player)
            || !gm.Baseball.TryGetRatings(avatarId, out PlayerRatingsRow ratings))
        {
            return;
        }

        // A team-less avatar (retired to FA at lineage game-over) must not
        // fall back to default(LeagueTier), which is High School's standings.
        bool tierKnown = gm.Baseball.TryGetTeamTier(player.TeamId ?? 0, out LeagueTier tier);
        _tierKnown = tierKnown;
        Visible = tierKnown;
        if (!tierKnown)
        {
            return;
        }

        int gamesPlayed = RefreshStandings(gm, tier, player.TeamId);
        RefreshLeaders(gm, tier, gamesPlayed, ratings.IsPitcher, avatarId);
    }

    /// <summary>
    /// The avatar's conference from <see cref="PlayoffManager.GetStandings"/>:
    /// HS one table of six, College and Pro one table per division, each ordered
    /// by <see cref="Standings.Order"/> (the seeding's own tiebreakers), with
    /// conference and division headers from the Conferences / Divisions rows. The
    /// national rank (College) and postseason seed ride on the row once written.
    /// Returns the avatar team's regular-season games completed (W + L), which
    /// scales the leader qualifiers.
    /// </summary>
    private int RefreshStandings(GameManager gm, LeagueTier tier, int? avatarTeamId)
    {
        int seasonYear = gm.State.SeasonYear;
        gm.Baseball.LoadTeamsByTier(tier, _tierTeamsBuffer);
        gm.Baseball.LoadConferences(_conferencesBuffer);
        gm.Baseball.LoadDivisions(_divisionsBuffer);
        StandingsView view = gm.Playoffs.GetStandings(seasonYear, tier);

        int avatarConference = 0;
        foreach (TeamRow team in _tierTeamsBuffer)
        {
            if (avatarTeamId.HasValue && team.TeamId == avatarTeamId.Value)
            {
                avatarConference = team.ConferenceId;
                break;
            }
        }

        int avatarGames = 0;
        int shownConference = 0;
        _standingsLines.Clear();
        foreach (StandingsGroupView group in view.Groups)
        {
            // An avatar whose team isn't found shows every group rather than none.
            bool shown = avatarConference == 0 || group.ConferenceId == avatarConference;
            if (shown && group.ConferenceId != shownConference)
            {
                shownConference = group.ConferenceId;
                AddHeader(string.Format(Tr("ui.league.standings.conference"), ConferenceName(group.ConferenceId)));
            }
            if (shown && group.DivisionId.HasValue)
            {
                AddHeader(string.Format(Tr("ui.league.standings.division"), DivisionName(group.DivisionId.Value)));
            }
            for (int i = 0; i < group.Rows.Count; i++)
            {
                StandingsRowView row = group.Rows[i];
                if (avatarTeamId.HasValue && row.TeamId == avatarTeamId.Value)
                {
                    avatarGames = row.Wins + row.Losses;
                }
                if (shown)
                {
                    _standingsLines.Add(FormatStandingsRow(group.Rows, i, avatarTeamId));
                }
            }
        }
        _standingsLabel.Text = string.Join("\n", _standingsLines);
        return avatarGames;
    }

    // A header after the first group is preceded by a blank line.
    private void AddHeader(string text)
    {
        if (_standingsLines.Count > 0)
        {
            _standingsLines.Add(string.Empty);
        }
        _standingsLines.Add(text);
    }

    private string FormatStandingsRow(IReadOnlyList<StandingsRowView> rows, int index, int? avatarTeamId)
    {
        StandingsRowView row = rows[index];
        StandingsRowView leader = rows[0];
        string gbText = index == 0
            ? StandingsLeaderGamesBehindText
            : FormatGamesBehind(GamesBehindNumerator(leader.Wins, leader.Losses, row.Wins, row.Losses));
        string line = string.Format(
            StandingRowFormat, index + 1, StandingsTeamAbbreviation(row.TeamId), row.Wins, row.Losses, WinPct(row.Wins, row.Losses), gbText);
        if (row.NationalRank.HasValue)
        {
            line += "  " + string.Format(Tr("ui.league.standings.rank"), row.NationalRank.Value);
        }
        if (row.Seed.HasValue)
        {
            line += "  " + string.Format(Tr("ui.league.standings.seed"), row.Seed.Value);
        }
        return avatarTeamId.HasValue && row.TeamId == avatarTeamId.Value
            ? string.Format(StandingsAvatarRowFormat, line)
            : line;
    }

    private string StandingsTeamAbbreviation(int teamId)
    {
        foreach (TeamRow team in _tierTeamsBuffer)
        {
            if (team.TeamId == teamId)
            {
                return team.Abbreviation;
            }
        }
        return teamId.ToString();
    }

    private string ConferenceName(int conferenceId)
    {
        foreach (ConferenceRow conference in _conferencesBuffer)
        {
            if (conference.ConferenceId == conferenceId)
            {
                return conference.Name;
            }
        }
        return conferenceId.ToString();
    }

    private string DivisionName(int divisionId)
    {
        foreach (DivisionRow division in _divisionsBuffer)
        {
            if (division.DivisionId == divisionId)
            {
                return division.Name;
            }
        }
        return divisionId.ToString();
    }

    private static double WinPct(int wins, int losses) => wins + losses > 0 ? (double)wins / (wins + losses) : 0.0;

    /// <summary>surface_the_sim.md §5's GB formula, times 2 (an integer — W/L are integers, so GB is always a whole or half game).</summary>
    private static int GamesBehindNumerator(int leaderWins, int leaderLosses, int teamWins, int teamLosses) =>
        (leaderWins - teamWins) + (teamLosses - leaderLosses);

    // The numerator goes negative when a team sits above the pct leader on
    // the GB metric (1-0 leads 3-1 on pct, but 3-1 is half a game ahead by
    // GB), and C# integer division truncates toward zero — so format the
    // magnitude and render the sign explicitly.
    private string FormatGamesBehind(int numerator)
    {
        int magnitude = Math.Abs(numerator);
        string text = magnitude % 2 == 0 ? (magnitude / 2).ToString() : magnitude / 2 + ".5";
        return numerator < 0 ? string.Format(StandingsAheadGamesBehindFormat, text) : text;
    }

    /// <summary>
    /// Top-N leaderboards, role-aware — a batter sees HR/AVG/OPS, a pitcher
    /// sees ERA/W/SO. Each category is its own query (LoadHrLeaders/
    /// LoadAvgLeaders/... — the ORDER BY column can't be bound), rendered
    /// into one heading-plus-rows block per category and joined into the
    /// single label.
    /// </summary>
    private void RefreshLeaders(GameManager gm, LeagueTier tier, int gamesPlayed, bool isPitcher, string avatarId)
    {
        int seasonYear = gm.State.SeasonYear;
        int innings = TierRules.RegulationInnings(tier);
        int minPa = Math.Max(MinQualifier, (gamesPlayed * innings * QualifyingPaPerThousandInnings + 999) / 1000);
        int minOuts = Math.Max(MinQualifier, (gamesPlayed * innings + OutsPerInning - 1) / OutsPerInning);
        string[] categoryNames = (isPitcher ? LeadersPitchingCategoriesCsv : LeadersBattingCategoriesCsv).Split(',');
        string block0, block1, block2;

        if (isPitcher)
        {
            gm.Baseball.LoadEraLeaders(seasonYear, tier, minOuts, LeaderRowCount, _leadersBuffer);
            block0 = FormatLeaderBlock(categoryNames[0], _leadersBuffer, LeaderRowEraFormat, avatarId);
            gm.Baseball.LoadWinLeaders(seasonYear, tier, LeaderRowCount, _leadersBuffer);
            block1 = FormatLeaderBlock(categoryNames[1], _leadersBuffer, LeaderRowCountFormat, avatarId);
            gm.Baseball.LoadStrikeoutLeaders(seasonYear, tier, LeaderRowCount, _leadersBuffer);
            block2 = FormatLeaderBlock(categoryNames[2], _leadersBuffer, LeaderRowCountFormat, avatarId);
        }
        else
        {
            gm.Baseball.LoadHrLeaders(seasonYear, tier, LeaderRowCount, _leadersBuffer);
            block0 = FormatLeaderBlock(categoryNames[0], _leadersBuffer, LeaderRowCountFormat, avatarId);
            gm.Baseball.LoadAvgLeaders(seasonYear, tier, minPa, LeaderRowCount, _leadersBuffer);
            block1 = FormatLeaderBlock(categoryNames[1], _leadersBuffer, LeaderRowAvgFormat, avatarId);
            gm.Baseball.LoadOpsLeaders(seasonYear, tier, minPa, LeaderRowCount, _leadersBuffer);
            block2 = FormatLeaderBlock(categoryNames[2], _leadersBuffer, LeaderRowAvgFormat, avatarId);
        }

        _leadersLabel.Text = string.Join("\n\n", block0, block1, block2);
    }

    private string FormatLeaderBlock(string categoryName, List<LeagueLeaderRow> rows, string rowFormat, string avatarId)
    {
        if (rows.Count == 0)
        {
            return categoryName + "\n" + LeaderNoneText;
        }
        var lines = new string[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            LeagueLeaderRow row = rows[i];
            string line = string.Format(rowFormat, i + 1, row.FirstName, row.LastName, row.Value);
            lines[i] = row.PlayerId == avatarId ? string.Format(LeaderAvatarRowFormat, line) : line;
        }
        return categoryName + "\n" + string.Join("\n", lines);
    }
}
