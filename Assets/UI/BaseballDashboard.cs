using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.UI.Scouting;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Phase 10a: the left panel of the two-panel shell. Absorbs the retired
/// AttendedGameScreen's two roles verbatim — the day-advance clock (its
/// Play/Skip buttons remain the only caller of TimeManager.AdvanceDay) and
/// the attended at-bat launch through CareerManager.TryGetPendingGame /
/// PlayPendingGame — reusing the identical seams and the identical
/// day-advance gates: the day cannot advance while a game is in flight, a
/// pending attended game is unresolved, a gritty-event choice is pending, or
/// a succession choice is parked. The bridge/task/dirty-flag driver is the
/// Phase 5 design unchanged: the game runs on a background task (never the
/// UI thread), the view renders snapshot DTOs, and the player's swing/take
/// intent flows back through a <see cref="PlayerIntentBridge"/>. Node paths
/// verified against BaseballDashboard.tscn via godot_scene_mapper before
/// this script was written.
/// </summary>
public sealed partial class BaseballDashboard : PanelContainer
{
    // Player-facing text templates live on exported properties so the scene
    // (not compiled code) is the editing surface, per ui_conventions.
    [Export]
    public string DayFormat { get; set; } = "Day {0} — Season {1} (day {2} of {3})";

    [Export]
    public string NoGameText { get; set; } = "No game today.";

    [Export]
    public string GameRunningText { get; set; } = "Game in progress — swing or take when the count shows.";

    [Export]
    public string FinalFormat { get; set; } = "Final: away {0} — home {1} ({2} innings). You: {3} PA.";

    [Export]
    public string PaLineFormat { get; set; } = "Your PA: {0}";

    [Export]
    public string NpcPaLineFormat { get; set; } = "{0}: {1}";

    [Export]
    public string NpcRivalryPaLineFormat { get; set; } = "RIVAL {0}: {1}";

    /// <summary>Comma-separated player-facing names for the 7 PaOutcome values, in enum order.</summary>
    [Export]
    public string OutcomeNamesCsv { get; set; } = "Out,Strikeout,Walk,Single,Double,Triple,Home run";

    // Phase 8c roster availability — read straight off GameManager.Absences,
    // independent of the Play/Skip click flow, so a player clicking Skip Day
    // through a whole absence still sees why there's no at-bat view.
    [Export]
    public string AbsentStatusFormat { get; set; } = "{0} — back on day {1}.";

    [Export]
    public string RustyStatusFormat { get; set; } = "{0}, still rusty (-{1} rating) until day {2}.";

    [Export]
    public string InjuryReasonText { get; set; } = "Sidelined by injury";

    [Export]
    public string SuspensionReasonText { get; set; } = "Serving a suspension";

    [Export]
    public string ArrestReasonText { get; set; } = "In custody";

    // Phase 10c scouting report (presentation_layer_narrative.md §5) — the
    // OFP/tier/tool text templates and the four role-relative tool names,
    // per ui_conventions' "player-facing text on the scene, not in C#
    // literals" convention (the OutcomeNamesCsv/AbsenceReasonText precedent).
    [Export]
    public string OfpFormat { get; set; } = "OFP: {0} ({1})";

    [Export]
    public string TierFormat { get; set; } = "Tier: {0}";

    /// <summary>Comma-separated player-facing tier names, in LeagueTier enum order (HS…MLB).</summary>
    [Export]
    public string TierNamesCsv { get; set; } = "High School,College,Class A,Double-A,Triple-A,MLB";

    [Export]
    public string ToolGradeFormat { get; set; } = "{0} {1} → {2} {3}";

    [Export]
    public string DevFormat { get; set; } = "Season {0}: {1} players moved, +{2} / -{3} pts";

    [Export]
    public string DevNoneText { get; set; } = "No offseason development yet.";

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

    private Label _dayLabel = null!;
    private Button _playGameButton = null!;
    private Button _skipDayButton = null!;
    private Label _statusLabel = null!;
    private Label _availabilityLabel = null!;
    private AtBatView _atBatView = null!;

    private PanelContainer _scoutingCard = null!;
    private Label _ofpLabel = null!;
    private Label _tierLabel = null!;
    private readonly ToolRowRefs[] _toolRows = new ToolRowRefs[4];
    private PanelContainer _devCard = null!;
    private Label _devSummaryLabel = null!;
    private string[] _tierNames = Array.Empty<string>();

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

    private readonly PlayerIntentBridge _bridge = new();
    private Task<MicroGameResult>? _gameTask;
    private string[] _outcomeNames = Array.Empty<string>();
    private bool _awaitingPendingGame;

    // Dirty-flag identity for the availability label (ui_conventions.md: no
    // per-frame string formatting).
    private SlotAvailability _shownAvailability = SlotAvailability.Available;
    private AbsenceReason _shownAvailabilityReason;
    private long _shownAvailabilityUntilDay;
    private long _shownAvailabilityPenaltyUntilDay;

    public override void _Ready()
    {
        _dayLabel = GetNode<Label>("Layout/CalendarStrip/DayLabel");
        _playGameButton = GetNode<Button>("Layout/CalendarStrip/PlayGameButton");
        _skipDayButton = GetNode<Button>("Layout/CalendarStrip/SkipDayButton");
        _statusLabel = GetNode<Label>("Layout/CalendarStrip/StatusLabel");
        _availabilityLabel = GetNode<Label>("Layout/AvailabilityLabel");
        _atBatView = GetNode<AtBatView>("Layout/AtBatView");

        _scoutingCard = GetNode<PanelContainer>("Layout/ScoutingCard");
        _ofpLabel = GetNode<Label>("Layout/ScoutingCard/ScoutingLayout/OfpLabel");
        _tierLabel = GetNode<Label>("Layout/ScoutingCard/ScoutingLayout/TierLabel");
        for (int i = 0; i < _toolRows.Length; i++)
        {
            string rowPath = $"Layout/ScoutingCard/ScoutingLayout/ToolsList/ToolRow{i}";
            _toolRows[i] = new ToolRowRefs(
                GetNode<Label>($"{rowPath}/NameLabel"),
                GetNode<ProgressBar>($"{rowPath}/Bar"),
                GetNode<Label>($"{rowPath}/GradeLabel"));
        }
        _devCard = GetNode<PanelContainer>("Layout/DevCard");
        _devSummaryLabel = GetNode<Label>("Layout/DevCard/DevLayout/DevSummaryLabel");

        _playGameButton.Pressed += OnPlayGamePressed;
        _skipDayButton.Pressed += OnSkipDayPressed;
        _atBatView.SwingCommitted += OnSwingCommitted;
        _atBatView.TakeCommitted += OnTakeCommitted;

        _outcomeNames = OutcomeNamesCsv.Split(',');
        _tierNames = TierNamesCsv.Split(',');
        RefreshDayLabel();
        RefreshScoutingCard();
        RefreshDayControlsEnabled();
    }

    public override void _ExitTree()
    {
        // Aborts any in-flight game; it unwinds unflushed and the career
        // forfeits it to the autopilot on the next day tick.
        _bridge.Cancel();
        _playGameButton.Pressed -= OnPlayGamePressed;
        _skipDayButton.Pressed -= OnSkipDayPressed;
        _atBatView.SwingCommitted -= OnSwingCommitted;
        _atBatView.TakeCommitted -= OnTakeCommitted;
    }

    public override void _Process(double delta)
    {
        CareerManager career = GameManager.Instance!.Career;
        RefreshAvailabilityLabel(career);

        // The day tick's events dispatch on GameManager._Process (earlier this
        // frame, autoloads process first), so a requested game shows up here.
        if (_awaitingPendingGame && GameManager.Instance!.Events.PendingCount == 0)
        {
            _awaitingPendingGame = false;
            if (career.HasPendingGame)
            {
                StartInteractiveGame(career);
            }
            else
            {
                _statusLabel.Text = NoGameText; // offseason day
            }
            RefreshDayLabel();
            RefreshScoutingCard();
        }

        if (_gameTask is null)
        {
            RefreshDayControlsEnabled();
            return;
        }

        if (_bridge.TryGetSnapshot(out AtBatSnapshot snapshot))
        {
            _atBatView.Render(new AtBatViewState(
                snapshot.Context.AwayScore, snapshot.Context.HomeScore,
                snapshot.Context.Inning, snapshot.Context.IsTopHalf,
                snapshot.Balls, snapshot.Strikes, snapshot.Context.Outs, snapshot.Context.Bases,
                snapshot.Look.Cue, snapshot.Look.ZoneProbability));
        }
        while (_bridge.TryDequeuePaOutcome(out PaOutcome outcome))
        {
            _atBatView.AppendPlayLine(string.Format(PaLineFormat, OutcomeName(outcome)));
        }
        while (_bridge.TryDequeueNpcPa(out NpcPaFeedEvent npcPa))
        {
            string format = npcPa.IsRivalryPa ? NpcRivalryPaLineFormat : NpcPaLineFormat;
            _atBatView.AppendPlayLine(string.Format(format, npcPa.BatterName, OutcomeName(npcPa.Outcome)));
        }

        if (_gameTask.IsCompleted)
        {
            FinishInteractiveGame();
        }
        RefreshDayControlsEnabled();
    }

    private void OnPlayGamePressed()
    {
        GameManager gm = GameManager.Instance!;
        _statusLabel.Text = GameRunningText;
        gm.Career.AutopilotAttendedGames = false;
        _awaitingPendingGame = true;
        gm.Clock.AdvanceDay();
    }

    private void OnSkipDayPressed()
    {
        GameManager gm = GameManager.Instance!;
        gm.Career.AutopilotAttendedGames = true;
        gm.Clock.AdvanceDay();
        _statusLabel.Text = string.Empty;
        RefreshDayLabel();
        RefreshScoutingCard();
    }

    private void StartInteractiveGame(CareerManager career)
    {
        _bridge.Reset();
        career.FeedSink = _bridge; // cleared in FinishInteractiveGame, after the task is observed done
        PlayerIntentBridge bridge = _bridge;
        _gameTask = Task.Run(() =>
        {
            // Sim runs entirely off the UI thread; the flush is the sim's own
            // batch and nothing else touches the connection while it runs.
            var policy = new InteractiveBatterPolicy(bridge);
            return career.PlayPendingGame(ref policy);
        });
    }

    private void FinishInteractiveGame()
    {
        Task<MicroGameResult> task = _gameTask!;
        _gameTask = null;
        GameManager.Instance!.Career.FeedSink = null;

        if (task.IsCompletedSuccessfully)
        {
            MicroGameResult result = task.Result;
            string final = string.Format(
                FinalFormat, result.AwayScore, result.HomeScore, result.Innings, result.HumanPa);
            _atBatView.AppendPlayLine(final);
            _statusLabel.Text = final;
        }
        else
        {
            // Cancelled (scene exit) or faulted; observe so nothing is unobserved.
            _statusLabel.Text = task.Exception?.InnerException is OperationCanceledException
                ? string.Empty
                : task.Exception?.InnerException?.Message ?? string.Empty;
        }
        RefreshDayLabel();
        RefreshScoutingCard();
    }

    private void OnSwingCommitted(double timingError, bool guessInZone) =>
        _bridge.SubmitSwing(timingError, guessInZone);

    private void OnTakeCommitted(bool guessInZone) => _bridge.SubmitTake(guessInZone);

    /// <summary>
    /// Unified per-frame recomputation of the Play/Skip buttons' disabled
    /// state — blocked while a game is in flight (the original invariant)
    /// OR while either overlay (event choice / succession choice) has
    /// something for the player to resolve, so a player literally cannot
    /// advance the day out from under a pending decision. Called every
    /// frame rather than at scattered transition points so it can never
    /// drift out of sync with either overlay's own state.
    /// </summary>
    private void RefreshDayControlsEnabled()
    {
        GameManager gm = GameManager.Instance!;
        bool blocked = _gameTask is not null || _awaitingPendingGame
            || gm.GrittyEventChoices.HasPendingChoice
            || gm.Career.HasPendingSuccessionChoice;
        _playGameButton.Disabled = blocked;
        _skipDayButton.Disabled = blocked;
    }

    private void RefreshDayLabel()
    {
        GlobalState state = GameManager.Instance!.State;
        _dayLabel.Text = string.Format(
            DayFormat, state.CurrentDay, state.SeasonYear, state.DayOfSeason, GlobalState.DaysPerSeason);
    }

    /// <summary>
    /// Phase 10c (presentation_layer_narrative.md §5): the scouting report
    /// card — present/future grades per role tool, the OFP headline off
    /// <see cref="PromotionScore.Scouting"/>, tier standing, and the last
    /// offseason's <see cref="DevelopmentManager.LastRun"/> movement. Called
    /// at every point <see cref="RefreshDayLabel"/> already is (day-advance
    /// is the only thing that can move ratings — PED costs, the offseason
    /// development pass — so that's the right refresh granularity per
    /// ui_conventions, not per-frame).
    /// </summary>
    private void RefreshScoutingCard()
    {
        GameManager gm = GameManager.Instance!;
        CareerManager career = gm.Career;
        bool show = career.HasAvatar;
        _scoutingCard.Visible = show;
        _devCard.Visible = show;
        if (!show)
        {
            return;
        }

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

        string tierName = gm.Baseball.TryGetTeamTier(player.TeamId ?? 0, out LeagueTier tier)
            ? TierName(tier)
            : string.Empty;
        _tierLabel.Text = string.Format(TierFormat, tierName);

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

    /// <summary>
    /// Phase 8c: an always-on readout of the avatar's current roster
    /// availability, straight off <see cref="GameManager.Absences"/> — unlike
    /// the one-shot NoGameText branch (which only fires when the player
    /// clicks Play), this stays accurate through a whole Skip-Day-only
    /// playthrough of an absence. TryGet returns expired entries too, so
    /// StateOn(today) — not the bool — decides visibility.
    /// </summary>
    private void RefreshAvailabilityLabel(CareerManager career)
    {
        SlotAvailability state = SlotAvailability.Available;
        AbsenceEntry entry = default;
        if (career.HasAvatar
            && GameManager.Instance!.Absences.TryGet(career.AvatarPlayerId, out entry))
        {
            state = entry.StateOn(GameManager.Instance!.State.CurrentDay);
        }

        bool changed = state != _shownAvailability
            || (state != SlotAvailability.Available
                && (entry.Reason != _shownAvailabilityReason
                    || entry.UntilDay != _shownAvailabilityUntilDay
                    || entry.PenaltyUntilDay != _shownAvailabilityPenaltyUntilDay));
        if (!changed)
        {
            return;
        }

        _shownAvailability = state;
        _shownAvailabilityReason = entry.Reason;
        _shownAvailabilityUntilDay = entry.UntilDay;
        _shownAvailabilityPenaltyUntilDay = entry.PenaltyUntilDay;

        _availabilityLabel.Visible = state != SlotAvailability.Available;
        _availabilityLabel.Text = state switch
        {
            SlotAvailability.Absent => string.Format(
                AbsentStatusFormat, AbsenceReasonText(entry.Reason), entry.UntilDay),
            SlotAvailability.Rusty => string.Format(
                RustyStatusFormat, AbsenceReasonText(entry.Reason), entry.RatingPenalty, entry.PenaltyUntilDay),
            _ => string.Empty,
        };
    }

    private string AbsenceReasonText(AbsenceReason reason) => reason switch
    {
        AbsenceReason.Injury => InjuryReasonText,
        AbsenceReason.Suspension => SuspensionReasonText,
        AbsenceReason.Arrest => ArrestReasonText,
        _ => string.Empty,
    };

    private string OutcomeName(PaOutcome outcome)
    {
        int index = (int)outcome;
        return index >= 0 && index < _outcomeNames.Length ? _outcomeNames[index] : outcome.ToString();
    }
}
