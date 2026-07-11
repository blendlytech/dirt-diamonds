using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// UI-reorg: the "League" phone tab — the avatar's tier Standings and League
/// Leaders cards, moved verbatim out of BaseballDashboard's retired
/// LeagueRow (see BaseballDashboard.cs's UI-reorg note). Self-contained and
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
    private readonly List<TeamRecordRow> _tierRecordsBuffer = new();
    private readonly List<LeagueLeaderRow> _leadersBuffer = new();

    private const int LeaderRowCount = 5;
    private const int MinPaForRateLeaders = 10;
    private const int MinOutsForEraLeaders = 15;

    // Dirty-flag identity (ui_conventions.md) — refresh once per day, exactly
    // when the day's deferred events have finished draining.
    private long _shownDay = -1;

    public override void _Ready()
    {
        _standingsLabel = GetNode<Label>("Layout/StandingsCard/StandingsLayout/StandingsLabel");
        _leadersLabel = GetNode<Label>("Layout/LeadersCard/LeadersLayout/LeadersLabel");
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
            || !gm.Baseball.TryGetRatings(avatarId, out PlayerRatingsRow ratings))
        {
            return;
        }

        // A team-less avatar (retired to FA at lineage game-over) must not
        // fall back to default(LeagueTier), which is High School's standings.
        bool tierKnown = gm.Baseball.TryGetTeamTier(player.TeamId ?? 0, out LeagueTier tier);
        Visible = tierKnown;
        if (!tierKnown)
        {
            return;
        }

        RefreshStandings(gm, tier, player.TeamId);
        RefreshLeaders(gm, tier, ratings.IsPitcher, avatarId);
    }

    /// <summary>
    /// The avatar's tier, teams ranked by win pct — a new aggregation query
    /// (<see cref="BaseballQueries.LoadTeamRecords"/>) merged in C# with
    /// <see cref="BaseballQueries.LoadTeamsByTier"/>'s names, GB computed here.
    /// </summary>
    private void RefreshStandings(GameManager gm, LeagueTier tier, int? avatarTeamId)
    {
        int seasonYear = gm.State.SeasonYear;
        gm.Baseball.LoadTeamsByTier(tier, _tierTeamsBuffer);
        gm.Baseball.LoadTeamRecords(seasonYear, tier, _tierRecordsBuffer);

        var rows = new (TeamRow Team, int Wins, int Losses)[_tierTeamsBuffer.Count];
        for (int i = 0; i < _tierTeamsBuffer.Count; i++)
        {
            TeamRow team = _tierTeamsBuffer[i];
            int wins = 0, losses = 0;
            foreach (TeamRecordRow record in _tierRecordsBuffer)
            {
                if (record.TeamId == team.TeamId)
                {
                    wins = record.Wins;
                    losses = record.Losses;
                    break;
                }
            }
            rows[i] = (team, wins, losses);
        }
        // Pct ties break on games above .500 (so the GB anchor among tied
        // teams is the one everyone else trails), then TeamId — Array.Sort is
        // unstable, and without a total order tied rows can swap between
        // refreshes.
        Array.Sort(rows, (a, b) =>
        {
            int byPct = WinPct(b.Wins, b.Losses).CompareTo(WinPct(a.Wins, a.Losses));
            if (byPct != 0)
            {
                return byPct;
            }
            int byMargin = (b.Wins - b.Losses).CompareTo(a.Wins - a.Losses);
            return byMargin != 0 ? byMargin : a.Team.TeamId.CompareTo(b.Team.TeamId);
        });

        if (rows.Length == 0)
        {
            _standingsLabel.Text = string.Empty;
            return;
        }

        (int leaderWins, int leaderLosses) = (rows[0].Wins, rows[0].Losses);
        var lines = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            (TeamRow team, int wins, int losses) = rows[i];
            double pct = WinPct(wins, losses);
            string gbText = i == 0
                ? StandingsLeaderGamesBehindText
                : FormatGamesBehind(GamesBehindNumerator(leaderWins, leaderLosses, wins, losses));
            string line = string.Format(StandingRowFormat, i + 1, team.Abbreviation, wins, losses, pct, gbText);
            lines[i] = avatarTeamId.HasValue && team.TeamId == avatarTeamId.Value
                ? string.Format(StandingsAvatarRowFormat, line)
                : line;
        }
        _standingsLabel.Text = string.Join("\n", lines);
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
    private void RefreshLeaders(GameManager gm, LeagueTier tier, bool isPitcher, string avatarId)
    {
        int seasonYear = gm.State.SeasonYear;
        string[] categoryNames = (isPitcher ? LeadersPitchingCategoriesCsv : LeadersBattingCategoriesCsv).Split(',');
        string block0, block1, block2;

        if (isPitcher)
        {
            gm.Baseball.LoadEraLeaders(seasonYear, tier, MinOutsForEraLeaders, LeaderRowCount, _leadersBuffer);
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
            gm.Baseball.LoadAvgLeaders(seasonYear, tier, MinPaForRateLeaders, LeaderRowCount, _leadersBuffer);
            block1 = FormatLeaderBlock(categoryNames[1], _leadersBuffer, LeaderRowAvgFormat, avatarId);
            gm.Baseball.LoadOpsLeaders(seasonYear, tier, MinPaForRateLeaders, LeaderRowCount, _leadersBuffer);
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
