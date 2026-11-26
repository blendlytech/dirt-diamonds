using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Slice 13-6b: the read-only Draft board, a card on the League phone tab. A
/// partial of <see cref="LeagueScreen"/>. Before the Draft (day 195) it lists
/// the top of <see cref="PipelineStatusView.DraftBoard"/> with the avatar's row
/// highlighted and his projected round; from day 195 it lists the season's
/// <see cref="PipelineStatusView.DraftPicks"/> by round with the avatar's pick
/// highlighted. A player who isn't in College gets an empty state and never
/// builds the snapshot.
///
/// <see cref="PipelineManager.GetStatusView"/> builds a database snapshot (tens
/// of milliseconds), so it is called only when the tab is actually showing and
/// the day has changed (the tab opening counts: the identity is the day, and
/// nothing is built while the tab is hidden), never per frame. View-only: no
/// button, no intent, no write. All copy is <c>ui.pipeline.*</c> strings-table
/// keys. Node paths verified against LeagueScreen.tscn before wiring.
/// </summary>
public sealed partial class LeagueScreen
{
    /// <summary>Board rows listed: the first round (32 picks). The avatar's own row is appended when he sits below it.</summary>
    private const int DraftBoardRows = 32;

    private Label _draftHeadingLabel = null!;
    private Label _draftSummaryLabel = null!;
    private Label _draftNoteLabel = null!;
    private VBoxContainer _draftRows = null!;

    // Row labels are pooled: built on demand up to the largest list shown (a
    // full Draft is 128 picks plus four round headings), then reused and hidden.
    private readonly List<Label> _draftRowPool = new();
    private int _draftRowsUsed;

    // Player names never change, so each is read once per session.
    private readonly Dictionary<string, string> _draftNameCache = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TeamRow> _draftTeams = new();
    private readonly List<TeamRow> _draftTeamsScratch = new();

    // Dirty-flag identity: the day the card was last built for.
    private long _draftShownDay = long.MinValue;

    private void InitDraftCard()
    {
        _draftHeadingLabel = GetNode<Label>("Layout/DraftCard/DraftLayout/DraftHeading");
        _draftSummaryLabel = GetNode<Label>("Layout/DraftCard/DraftLayout/DraftSummaryLabel");
        _draftNoteLabel = GetNode<Label>("Layout/DraftCard/DraftLayout/DraftNoteLabel");
        _draftRows = GetNode<VBoxContainer>("Layout/DraftCard/DraftLayout/DraftRows");
        _draftHeadingLabel.Text = Tr("ui.pipeline.board.heading");
    }

    /// <summary>Called every frame from <c>_Process</c>; builds only on a day change while the tab is showing.</summary>
    private void RefreshDraftCard(GameManager gm, CareerManager career)
    {
        // A day's events are still draining, or the tab is closed: build nothing.
        if (!IsVisibleInTree() || gm.Events.PendingCount > 0)
        {
            return;
        }
        long day = gm.State.CurrentDay;
        if (day == _draftShownDay)
        {
            return;
        }
        _draftShownDay = day;
        RenderDraftCard(gm, career);
    }

    private void RenderDraftCard(GameManager gm, CareerManager career)
    {
        _draftRowsUsed = 0;
        _draftNoteLabel.Text = string.Empty;

        string avatarId = career.AvatarPlayerId;
        bool inCollege = gm.Players.TryGetById(avatarId, out PlayerRow avatar)
            && avatar.TeamId is int teamId
            && gm.Baseball.TryGetTeamTier(teamId, out LeagueTier tier)
            && tier == LeagueTier.College;
        if (!inCollege)
        {
            _draftSummaryLabel.Text = Tr("ui.pipeline.board.empty.not_college");
            HideUnusedDraftRows();
            return;
        }

        PipelineStatusView view = gm.Pipeline.GetStatusView();
        LoadDraftTeams(gm);

        if (gm.State.DayOfSeason >= PipelineCalendar.DraftDay)
        {
            RenderDraftPicks(gm, view, avatarId);
        }
        else
        {
            RenderDraftBoard(gm, view);
        }
        HideUnusedDraftRows();
    }

    private void RenderDraftBoard(GameManager gm, PipelineStatusView view)
    {
        if (view.AvatarClassYear <= 1)
        {
            _draftSummaryLabel.Text = Tr("ui.pipeline.board.empty.freshman");
        }
        else if (view.AvatarProjectedRound > 0)
        {
            _draftSummaryLabel.Text = string.Format(Tr("ui.pipeline.board.projected"), view.AvatarProjectedRound, view.AvatarProjectedSlot);
        }
        else if (view.AvatarProjectedSlot > 0)
        {
            _draftSummaryLabel.Text = string.Format(Tr("ui.pipeline.board.undrafted"), view.AvatarProjectedSlot);
        }
        else
        {
            _draftSummaryLabel.Text = Tr("ui.pipeline.board.off_board");
        }

        IReadOnlyList<DraftBoardEntry> board = view.DraftBoard;
        if (board.Count == 0)
        {
            _draftNoteLabel.Text = Tr("ui.pipeline.board.empty.no_board");
            return;
        }

        int shown = Math.Min(board.Count, DraftBoardRows);
        for (int i = 0; i < shown; i++)
        {
            AddBoardRow(gm, board[i]);
        }
        // The avatar below the first round still sees his own row, so the board
        // never hides where he stands.
        for (int i = shown; i < board.Count; i++)
        {
            if (board[i].IsAvatar)
            {
                AddBoardRow(gm, board[i]);
                break;
            }
        }
        _draftNoteLabel.Text = string.Format(Tr("ui.pipeline.board.shown"), shown, board.Count);
    }

    private void AddBoardRow(GameManager gm, in DraftBoardEntry entry)
    {
        string role = Tr(entry.Role switch
        {
            PitcherRole.Starter => "ui.pipeline.role.starter",
            PitcherRole.Reliever => "ui.pipeline.role.reliever",
            _ => "ui.pipeline.role.batter",
        });
        string text = string.Format(
            Tr("ui.pipeline.board.row"),
            entry.ProjectedSlot, PlayerName(gm, entry.PlayerId), role, TeamAbbreviation(entry.TeamId), ClassLabel(entry.ClassYear));
        AddDraftRow(text, entry.IsAvatar, heading: false);
    }

    private void RenderDraftPicks(GameManager gm, PipelineStatusView view, string avatarId)
    {
        _draftNoteLabel.Text = string.Format(Tr("ui.pipeline.picks.caption"), view.SeasonYear);
        IReadOnlyList<DraftPickRow> picks = view.DraftPicks;
        if (picks.Count == 0)
        {
            _draftSummaryLabel.Text = Tr("ui.pipeline.picks.none");
            return;
        }

        _draftSummaryLabel.Text = string.Empty;
        int round = 0;
        foreach (DraftPickRow pick in picks)
        {
            if (pick.Round != round)
            {
                round = pick.Round;
                AddDraftRow(string.Format(Tr("ui.pipeline.picks.round"), round), highlight: false, heading: true);
            }
            bool isAvatar = string.Equals(pick.PlayerId, avatarId, StringComparison.Ordinal);
            if (isAvatar)
            {
                _draftSummaryLabel.Text = string.Format(Tr("ui.pipeline.picks.you"), pick.OverallPick, TeamAbbreviation(pick.TeamId));
            }
            AddDraftRow(
                string.Format(Tr("ui.pipeline.picks.row"), pick.OverallPick, TeamAbbreviation(pick.TeamId), PlayerName(gm, pick.PlayerId)),
                isAvatar,
                heading: false);
        }
    }

    private void AddDraftRow(string text, bool highlight, bool heading)
    {
        if (_draftRowsUsed == _draftRowPool.Count)
        {
            var label = new Label();
            _draftRows.AddChild(label);
            _draftRowPool.Add(label);
        }
        Label row = _draftRowPool[_draftRowsUsed++];
        row.Visible = true;
        row.ThemeTypeVariation = heading ? "HeadingLabel" : highlight ? "HighlightLabel" : string.Empty;
        row.Text = highlight ? string.Format(Tr("ui.pipeline.marker.you"), text) : text;
    }

    private void HideUnusedDraftRows()
    {
        for (int i = _draftRowsUsed; i < _draftRowPool.Count; i++)
        {
            _draftRowPool[i].Visible = false;
        }
    }

    private void LoadDraftTeams(GameManager gm)
    {
        _draftTeams.Clear();
        foreach (LeagueTier tier in new[] { LeagueTier.College, LeagueTier.Pro })
        {
            gm.Baseball.LoadTeamsByTier(tier, _draftTeamsScratch);
            foreach (TeamRow team in _draftTeamsScratch)
            {
                _draftTeams[team.TeamId] = team;
            }
        }
    }

    private string TeamAbbreviation(int teamId) =>
        _draftTeams.TryGetValue(teamId, out TeamRow team) ? team.Abbreviation : teamId.ToString();

    private string PlayerName(GameManager gm, string playerId)
    {
        if (_draftNameCache.TryGetValue(playerId, out string? cached))
        {
            return cached;
        }
        string name = gm.Players.TryGetById(playerId, out PlayerRow player)
            ? string.Format(Tr("ui.pipeline.name.full"), player.FirstName, player.LastName)
            : playerId;
        _draftNameCache[playerId] = name;
        return name;
    }

    private string ClassLabel(int classYear) =>
        classYear is >= 1 and <= 4 ? Tr("ui.pipeline.class." + classYear) : classYear.ToString();
}
