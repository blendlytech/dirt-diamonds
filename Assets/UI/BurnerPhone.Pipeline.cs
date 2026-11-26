using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Slice 13-6b: the Burner Phone's pipeline decision cards (the recruiting
/// offers and the Draft declaration). A partial of <see cref="BurnerPhone"/> so
/// the pipeline UI stays one reviewable unit. The card sits at the top of the
/// Events tab, next to the gritty-event feed, with a cross-tab banner and the
/// Events tab badge while a decision is owed (the prominence-pass vocabulary:
/// a level, never an auto tab switch).
///
/// Presentation only. It reads <see cref="PipelineManager.TryGetPendingRecruitingChoice"/>
/// and <see cref="PipelineManager.TryGetPendingDeclaration"/> (cached per day by the
/// engine, so polling them each frame is free) plus the existing DTO reads
/// (<see cref="BaseballQueries.LoadTeamsByTier"/> for program names,
/// <see cref="PlayoffManager.GetNationalRanking"/> for ranks), and sends intent
/// through <see cref="PipelineManager.ResolveRecruiting"/> /
/// <see cref="PipelineManager.ResolveDeclaration"/> only. It never writes the
/// database and never touches an RNG. The clock never stops for these choices
/// (13-6 brief D5-A): the card names its deadline, and an unanswered choice
/// resolves there by the engine's rule.
///
/// Every string is a <c>ui.pipeline.*</c> strings-table key. Node paths
/// verified against BurnerPhone.tscn before wiring.
/// </summary>
public sealed partial class BurnerPhone
{
    private enum PipelineCardKind
    {
        None,
        Recruiting,
        Declaration,
        Answered,
    }

    private Button _pipelineBanner = null!;
    private PanelContainer _pipelineCard = null!;
    private Label _pipelineHeadingLabel = null!;
    private Label _pipelineBodyLabel = null!;
    private Label _pipelineDeadlineLabel = null!;
    private VBoxContainer _pipelineChoicesBox = null!;
    private Label _pipelineAnsweredLabel = null!;

    // Dirty-flag identity (ui_conventions.md): the card re-renders only when
    // the kind or the day changes (the days-left line is the only per-day text).
    private PipelineCardKind _pipelineShownKind = PipelineCardKind.None;
    private long _pipelineShownDay = long.MinValue;

    // The Events tab badge's second source (the first is the gritty pending choice).
    private bool _pipelineDecisionPending;

    // The "answered" state: set on the press, shown for the rest of that day,
    // then the card is gone. A reload shows no card (the answer is persisted).
    private long _pipelineAnsweredDay = long.MinValue;
    private string _pipelineAnsweredHeading = string.Empty;
    private string _pipelineAnsweredText = string.Empty;

    private readonly List<TeamRow> _pipelineTeamsScratch = new();

    private void InitPipelineCard()
    {
        _pipelineBanner = GetNode<Button>("Screen/ScreenLayout/PipelineBanner");
        _pipelineCard = GetNode<PanelContainer>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/PipelineCard");
        _pipelineHeadingLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/PipelineCard/PipelineCardLayout/PipelineHeadingLabel");
        _pipelineBodyLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/PipelineCard/PipelineCardLayout/PipelineBodyLabel");
        _pipelineDeadlineLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/PipelineCard/PipelineCardLayout/PipelineDeadlineLabel");
        _pipelineChoicesBox = GetNode<VBoxContainer>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/PipelineCard/PipelineCardLayout/PipelineChoicesBox");
        _pipelineAnsweredLabel = GetNode<Label>("Screen/ScreenLayout/PhoneTabs/Events/EventsLayout/PipelineCard/PipelineCardLayout/PipelineAnsweredLabel");
        _pipelineBanner.Pressed += OnPipelineBannerPressed;
    }

    private void ExitPipelineCard()
    {
        _pipelineBanner.Pressed -= OnPipelineBannerPressed;
    }

    /// <summary>
    /// The decision card's refresh, called from <c>_Process</c> ahead of the
    /// gritty-event dirty-flag gate. Cheap by construction: the engine caches
    /// the pending getters per day, so a steady frame is two struct copies and
    /// an identity compare.
    /// </summary>
    private void RefreshPipelineCard(GameManager gm)
    {
        // A day's events are still draining: the pending state may be mid-write.
        if (gm.Events.PendingCount > 0)
        {
            return;
        }

        long day = gm.State.CurrentDay;
        PipelineManager pipeline = gm.Pipeline;
        PendingDeclaration declaration = default;
        PipelineCardKind kind;
        if (pipeline.TryGetPendingRecruitingChoice(out PendingRecruitingChoice recruiting))
        {
            kind = PipelineCardKind.Recruiting;
        }
        else if (pipeline.TryGetPendingDeclaration(out declaration))
        {
            kind = PipelineCardKind.Declaration;
        }
        else
        {
            kind = _pipelineAnsweredDay == day ? PipelineCardKind.Answered : PipelineCardKind.None;
        }

        if (kind == _pipelineShownKind && day == _pipelineShownDay)
        {
            return;
        }
        _pipelineShownKind = kind;
        _pipelineShownDay = day;

        ClearPipelineChoices();
        switch (kind)
        {
            case PipelineCardKind.Recruiting:
                RenderRecruitingCard(gm, in recruiting);
                break;
            case PipelineCardKind.Declaration:
                RenderDeclarationCard(gm, in declaration);
                break;
            case PipelineCardKind.Answered:
                RenderAnsweredCard();
                break;
            default:
                _pipelineCard.Visible = false;
                break;
        }

        bool decisionPending = kind == PipelineCardKind.Recruiting || kind == PipelineCardKind.Declaration;
        _pipelineBanner.Visible = decisionPending;
        if (decisionPending)
        {
            _pipelineBanner.Text = Tr(kind == PipelineCardKind.Recruiting ? "ui.pipeline.banner.recruit" : "ui.pipeline.banner.declare");
        }
        _pipelineDecisionPending = decisionPending;
        ApplyEventsTabBadge();
    }

    /// <summary>The Events tab's standing "•" badge: lit while a gritty choice OR a pipeline decision is owed.</summary>
    private void ApplyEventsTabBadge()
    {
        bool badge = _shownHasPending || _pipelineDecisionPending;
        _phoneTabs.SetTabTitle(_eventsTabIndex, badge ? UnreadMarker + _eventsTabPlainTitle : _eventsTabPlainTitle);
    }

    private void ClearPipelineChoices()
    {
        foreach (Node child in _pipelineChoicesBox.GetChildren())
        {
            _pipelineChoicesBox.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void RenderRecruitingCard(GameManager gm, in PendingRecruitingChoice pending)
    {
        _pipelineCard.Visible = true;
        _pipelineAnsweredLabel.Visible = false;
        _pipelineHeadingLabel.Text = Tr("ui.pipeline.recruit.heading");
        _pipelineBodyLabel.Text = Tr(pending.IsWalkOn ? "ui.pipeline.recruit.walk_on.body" : "ui.pipeline.recruit.body");
        _pipelineBodyLabel.Visible = true;
        _pipelineDeadlineLabel.Text = Tr("ui.pipeline.recruit.deadline") + " " + DaysLeftText(PipelineCalendar.SigningDay - gm.State.DayOfSeason);

        // Program names come from the existing team read; ranks from the
        // National Ranking view for the season the offers were made in. Like the
        // engine (RankedPrograms), a ranking that doesn't cover every program is
        // ignored rather than shown half-true.
        gm.Baseball.LoadTeamsByTier(LeagueTier.College, _pipelineTeamsScratch);
        var ranks = new Dictionary<int, int>();
        IReadOnlyList<NationalRankView> ranking = gm.Playoffs.GetNationalRanking(pending.SeasonYear);
        if (ranking.Count == _pipelineTeamsScratch.Count)
        {
            foreach (NationalRankView view in ranking)
            {
                ranks[view.TeamId] = view.Rank;
            }
        }

        var rows = new List<(int TeamId, string Name, int Rank)>(pending.Offers.Count);
        foreach (RecruitingOfferRow offer in pending.Offers)
        {
            string name = ProgramName(offer.TeamId);
            rows.Add((offer.TeamId, name, ranks.TryGetValue(offer.TeamId, out int rank) ? rank : int.MaxValue));
        }
        rows.Sort((a, b) =>
        {
            int byRank = a.Rank.CompareTo(b.Rank);
            return byRank != 0 ? byRank : a.TeamId.CompareTo(b.TeamId);
        });

        foreach ((int teamId, string name, int rank) in rows)
        {
            string text = pending.IsWalkOn
                ? string.Format(Tr("ui.pipeline.recruit.offer.walk_on"), name)
                : rank != int.MaxValue
                    ? string.Format(Tr("ui.pipeline.recruit.offer.ranked"), name, rank)
                    : string.Format(Tr("ui.pipeline.recruit.offer.unranked"), name);
            int capturedTeamId = teamId; // captured by value, not the loop variable
            string capturedName = name;
            AddPipelineChoice(text, () => OnRecruitingPicked(capturedTeamId, capturedName));
        }
        LinkPipelineChoiceFocus();
    }

    private void RenderDeclarationCard(GameManager gm, in PendingDeclaration pending)
    {
        _pipelineCard.Visible = true;
        _pipelineAnsweredLabel.Visible = false;
        _pipelineHeadingLabel.Text = Tr("ui.pipeline.declare.heading");
        string projection = pending.ProjectedRound > 0
            ? string.Format(Tr("ui.pipeline.declare.projected"), pending.ProjectedRound)
            : Tr("ui.pipeline.declare.undrafted");
        _pipelineBodyLabel.Text = projection + "\n" + Tr("ui.pipeline.declare.body");
        _pipelineBodyLabel.Visible = true;
        _pipelineDeadlineLabel.Text = Tr("ui.pipeline.declare.deadline") + " "
            + DaysLeftText(PipelineCalendar.DeclarationDeadlineDay - gm.State.DayOfSeason);

        AddPipelineChoice(Tr("ui.pipeline.declare.yes"), () => OnDeclarationAnswered(declare: true));
        AddPipelineChoice(Tr("ui.pipeline.declare.no"), () => OnDeclarationAnswered(declare: false));
        LinkPipelineChoiceFocus();
    }

    private void RenderAnsweredCard()
    {
        _pipelineCard.Visible = true;
        _pipelineHeadingLabel.Text = _pipelineAnsweredHeading;
        _pipelineBodyLabel.Visible = false;
        _pipelineDeadlineLabel.Text = string.Empty;
        _pipelineAnsweredLabel.Text = _pipelineAnsweredText;
        _pipelineAnsweredLabel.Visible = true;
    }

    private void AddPipelineChoice(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = "ReplyChip",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.Pressed += onPressed;
        _pipelineChoicesBox.AddChild(button);
    }

    /// <summary>
    /// Keyboard/controller reach: each choice's Up/Down neighbors are its list
    /// neighbors (the first and last fall back to the engine's automatic ones).
    /// </summary>
    private void LinkPipelineChoiceFocus()
    {
        int count = _pipelineChoicesBox.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var button = (Button)_pipelineChoicesBox.GetChild(i);
            if (i > 0)
            {
                button.FocusNeighborTop = _pipelineChoicesBox.GetChild(i - 1).GetPath();
            }
            if (i < count - 1)
            {
                button.FocusNeighborBottom = _pipelineChoicesBox.GetChild(i + 1).GetPath();
            }
        }
    }

    private string ProgramName(int teamId)
    {
        foreach (TeamRow team in _pipelineTeamsScratch)
        {
            if (team.TeamId == teamId)
            {
                return string.Format(Tr("ui.pipeline.name.full"), team.City, team.Name);
            }
        }
        return teamId.ToString();
    }

    private string DaysLeftText(int daysLeft) =>
        daysLeft <= 1 ? Tr("ui.pipeline.deadline.day") : string.Format(Tr("ui.pipeline.deadline.days"), daysLeft);

    private void OnRecruitingPicked(int teamId, string programName)
    {
        GameManager gm = GameManager.Instance!;
        // One answer per choice: a stale press can never reach a second Resolve (which throws).
        if (!gm.Pipeline.TryGetPendingRecruitingChoice(out _))
        {
            return;
        }
        DisablePipelineChoices();
        gm.Pipeline.ResolveRecruiting(teamId);
        RecordPipelineAnswer(gm, Tr("ui.pipeline.recruit.heading"), string.Format(Tr("ui.pipeline.recruit.answered"), programName));
    }

    private void OnDeclarationAnswered(bool declare)
    {
        GameManager gm = GameManager.Instance!;
        if (!gm.Pipeline.TryGetPendingDeclaration(out _))
        {
            return;
        }
        DisablePipelineChoices();
        gm.Pipeline.ResolveDeclaration(declare);
        RecordPipelineAnswer(
            gm,
            Tr("ui.pipeline.declare.heading"),
            Tr(declare ? "ui.pipeline.declare.answered.yes" : "ui.pipeline.declare.answered.no"));
    }

    private void DisablePipelineChoices()
    {
        foreach (Node child in _pipelineChoicesBox.GetChildren())
        {
            ((Button)child).Disabled = true;
        }
    }

    /// <summary>Collapses the card to its answered line for the rest of the day; the next frame re-renders it.</summary>
    private void RecordPipelineAnswer(GameManager gm, string heading, string text)
    {
        _pipelineAnsweredDay = gm.State.CurrentDay;
        _pipelineAnsweredHeading = heading;
        _pipelineAnsweredText = text;
        _pipelineShownKind = PipelineCardKind.None;
        _pipelineShownDay = long.MinValue;
    }

    /// <summary>Banner tap: the only tab change this slice makes, and only on an explicit press.</summary>
    private void OnPipelineBannerPressed()
    {
        _phoneTabs.CurrentTab = _eventsTabIndex;
        if (_pipelineChoicesBox.GetChildCount() > 0)
        {
            ((Button)_pipelineChoicesBox.GetChild(0)).GrabFocus();
        }
    }
}
