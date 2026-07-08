using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.UI.Portraits;
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
    public string TierFormat { get; set; } = "{0}";

    /// <summary>Comma-separated player-facing tier names, in LeagueTier enum order (HS…MLB).</summary>
    [Export]
    public string TierNamesCsv { get; set; } = "High School,College,Class A,Double-A,Triple-A,MLB";

    // '›' rather than '→': the vendored Barlow faces cover Latin punctuation
    // but not the arrows block, and a tofu glyph here would ship on every
    // scouting row.
    [Export]
    public string ToolGradeFormat { get; set; } = "{0} {1} › {2} {3}";

    [Export]
    public string DevFormat { get; set; } = "Season {0}: {1} players moved, +{2} / -{3} pts";

    [Export]
    public string DevNoneText { get; set; } = "No offseason development yet.";

    // 12c-2 StatLineCard (surface_the_sim.md §4/§5) — role-aware avatar season
    // line, sourced from BaseballQueries.TryGetBattingSeasonLine/
    // TryGetPitchingSeasonLine. Same [Export]-template convention as the
    // scouting/dev cards; StatLineNoneText covers both roles' "hasn't played
    // this season yet" case (no row).
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

    // 12c-3 StandingsCard (surface_the_sim.md §4/§5) — tier standings, avatar's
    // team marked with a plain-ASCII prefix (the NpcRivalryPaLineFormat
    // precedent: a text marker, not a glyph, since the vendored Barlow faces
    // don't cover every Unicode block — see ToolGradeFormat's note). Games
    // behind is precomputed to a string in C# (always a multiple of 0.5 given
    // integer W/L) so the row format never needs a decimal-format culture call.
    [Export]
    public string StandingRowFormat { get; set; } = "{0}. {1}  {2}-{3}  {4:0.000}  {5}";

    [Export]
    public string StandingsAvatarRowFormat { get; set; } = "* {0}";

    [Export]
    public string StandingsLeaderGamesBehindText { get; set; } = "-";

    /// <summary>Wraps the GB of a team sitting ahead of the pct leader on the GB metric (reachable early-season, e.g. 3-1 under a 1-0 pct leader).</summary>
    [Export]
    public string StandingsAheadGamesBehindFormat { get; set; } = "+{0}";

    // 12c-3 LeadersCard — role-aware (the isPitcher split every other card on
    // this dashboard already branches on): a batter sees HR/AVG/OPS leaders,
    // a pitcher sees ERA/W/SO. Three row formats cover the three value shapes
    // (plain count, .000 rate, 0.00 ERA); the avatar's own row (if it ranks)
    // gets the same plain-text marker as StandingsAvatarRowFormat.
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

    private PortraitView _avatarPortrait = null!;
    private Label _dayLabel = null!;
    private Button _playGameButton = null!;
    private Button _skipDayButton = null!;
    private Label _statusLabel = null!;
    private PanelContainer _availabilityCard = null!;
    private Label _availabilityLabel = null!;
    private AtBatView _atBatView = null!;
    private PanelContainer _recapCard = null!;
    private Label _recapLabel = null!;

    private PanelContainer _scoutingCard = null!;
    private Label _ofpLabel = null!;
    private Label _tierLabel = null!;
    private readonly ToolRowRefs[] _toolRows = new ToolRowRefs[4];
    private PanelContainer _devCard = null!;
    private Label _devSummaryLabel = null!;
    private PanelContainer _statLineCard = null!;
    private Label _statLineLabel = null!;
    private PanelContainer _standingsCard = null!;
    private Label _standingsLabel = null!;
    private PanelContainer _leadersCard = null!;
    private Label _leadersLabel = null!;
    private string[] _tierNames = Array.Empty<string>();

    // Reusable query-result buffers for the 12c-3 league cards — cleared and
    // refilled per call, the LoadRoster "destination" idiom, so the once-per-
    // day-advance refresh doesn't allocate a fresh list per category.
    private readonly List<TeamRow> _tierTeamsBuffer = new();
    private readonly List<TeamRecordRow> _tierRecordsBuffer = new();
    private readonly List<LeagueLeaderRow> _leadersBuffer = new();

    private const int LeaderRowCount = 5;
    private const int MinPaForRateLeaders = 10;
    private const int MinOutsForEraLeaders = 15;

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

    // Skip Day's card refresh must wait for the day tick's deferred events to
    // pump (same reason _awaitingPendingGame waits) — a synchronous refresh in
    // the click handler reads the DB before the sims have played the day.
    private bool _refreshAfterDayTick;

    // Dirty-flag identity for the availability label (ui_conventions.md: no
    // per-frame string formatting).
    private SlotAvailability _shownAvailability = SlotAvailability.Available;
    private AbsenceReason _shownAvailabilityReason;
    private long _shownAvailabilityUntilDay;
    private long _shownAvailabilityPenaltyUntilDay;

    public override void _Ready()
    {
        _avatarPortrait = GetNode<PortraitView>("Layout/HeaderBand/HeaderRow/AvatarPortrait");
        _dayLabel = GetNode<Label>("Layout/HeaderBand/HeaderRow/HeaderText/DayLabel");
        _playGameButton = GetNode<Button>("Layout/HeaderBand/HeaderRow/PlayGameButton");
        _skipDayButton = GetNode<Button>("Layout/HeaderBand/HeaderRow/SkipDayButton");
        _statusLabel = GetNode<Label>("Layout/HeaderBand/HeaderRow/HeaderText/StatusLabel");
        _availabilityCard = GetNode<PanelContainer>("Layout/AvailabilityCard");
        _availabilityLabel = GetNode<Label>("Layout/AvailabilityCard/AvailabilityLabel");
        _atBatView = GetNode<AtBatView>("Layout/CenterSlot/AtBatView");
        _recapCard = GetNode<PanelContainer>("Layout/CenterSlot/RecapCard");
        _recapLabel = GetNode<Label>("Layout/CenterSlot/RecapCard/RecapLayout/RecapLabel");

        _scoutingCard = GetNode<PanelContainer>("Layout/MeRow/ScoutingCard");
        _ofpLabel = GetNode<Label>("Layout/MeRow/ScoutingCard/ScoutingLayout/OfpRow/OfpLabel");
        _tierLabel = GetNode<Label>("Layout/MeRow/ScoutingCard/ScoutingLayout/OfpRow/TierChip/TierLabel");
        for (int i = 0; i < _toolRows.Length; i++)
        {
            string rowPath = $"Layout/MeRow/ScoutingCard/ScoutingLayout/ToolsList/ToolRow{i}";
            _toolRows[i] = new ToolRowRefs(
                GetNode<Label>($"{rowPath}/NameLabel"),
                GetNode<ProgressBar>($"{rowPath}/Bar"),
                GetNode<Label>($"{rowPath}/GradeLabel"));
        }
        _devCard = GetNode<PanelContainer>("Layout/MeRow/DevCard");
        _devSummaryLabel = GetNode<Label>("Layout/MeRow/DevCard/DevLayout/DevSummaryLabel");
        _statLineCard = GetNode<PanelContainer>("Layout/MeRow/StatLineCard");
        _statLineLabel = GetNode<Label>("Layout/MeRow/StatLineCard/StatLineLayout/StatLineLabel");
        _standingsCard = GetNode<PanelContainer>("Layout/LeagueRow/StandingsCard");
        _standingsLabel = GetNode<Label>("Layout/LeagueRow/StandingsCard/StandingsLayout/StandingsLabel");
        _leadersCard = GetNode<PanelContainer>("Layout/LeagueRow/LeadersCard");
        _leadersLabel = GetNode<Label>("Layout/LeagueRow/LeadersCard/LeadersLayout/LeadersLabel");

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
        // frame, autoloads process first), so a requested game shows up here —
        // and the Skip path's card refresh waits on the same drain.
        if ((_awaitingPendingGame || _refreshAfterDayTick) && GameManager.Instance!.Events.PendingCount == 0)
        {
            bool startRequested = _awaitingPendingGame;
            _awaitingPendingGame = false;
            _refreshAfterDayTick = false;
            if (startRequested)
            {
                if (career.HasPendingGame)
                {
                    StartInteractiveGame(career);
                }
                else
                {
                    _statusLabel.Text = NoGameText; // offseason day
                }
            }
            RefreshDayLabel();
            RefreshScoutingCard();
        }

        if (_gameTask is null)
        {
            RefreshDayControlsEnabled();
            return;
        }

        // 12d-2 (at_bat_presentation.md §5.7): the dashboard only forwards, in
        // dequeue order, into the view's own beat queue — it never renders
        // directly. Pitch results are drained before PA outcomes/NPC feed so
        // the beat queue preserves the sim's causal order (§3):
        // [terminal pitch] -> [PA outcome] -> [NPC feed...] -> [next snapshot].
        // The pending snapshot is a latest-wins value the view applies (and
        // only then re-enables input) once its beat queue drains, so its
        // position here relative to the loops below doesn't affect ordering.
        if (_bridge.TryGetSnapshot(out AtBatSnapshot snapshot))
        {
            _atBatView.SetPendingSnapshot(new AtBatViewState(
                snapshot.Context.AwayScore, snapshot.Context.HomeScore,
                snapshot.Context.Inning, snapshot.Context.IsTopHalf,
                snapshot.Balls, snapshot.Strikes, snapshot.Context.Outs, snapshot.Context.Bases,
                snapshot.Look.Cue, snapshot.Look.ZoneProbability));
        }
        while (_bridge.TryDequeuePitchResult(out PitchResult pitchResult))
        {
            _atBatView.EnqueuePitchBeat(in pitchResult);
        }
        while (_bridge.TryDequeuePaOutcome(out PaOutcome outcome))
        {
            _atBatView.EnqueuePaOutcomeBeat(outcome, string.Format(PaLineFormat, OutcomeName(outcome)));
        }
        while (_bridge.TryDequeueNpcPa(out NpcPaFeedEvent npcPa))
        {
            string format = npcPa.IsRivalryPa ? NpcRivalryPaLineFormat : NpcPaLineFormat;
            _atBatView.EnqueueNpcBeat(npcPa.Outcome, string.Format(format, npcPa.BatterName, OutcomeName(npcPa.Outcome)));
        }

        // Fable review (12d-2+12d-3, finding 1): the sim task can complete
        // microseconds after the player's last intent while the view's beat
        // queue still holds the terminal pitch reveal, the PA reveal, and any
        // trailing NPC beats — gating on SequencerDrained too keeps the drain
        // loops above running every frame until the presentation actually
        // catches up, instead of tearing the view down mid-reveal.
        if (_gameTask.IsCompleted && _atBatView.SequencerDrained)
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
        _refreshAfterDayTick = true;
        gm.Clock.AdvanceDay();
        _statusLabel.Text = string.Empty;
        RefreshDayLabel(); // the clock itself advances synchronously; the cards wait for the pump
    }

    private void StartInteractiveGame(CareerManager career)
    {
        // 12c: the center slot's two occupants swap here — the at-bat view
        // takes over for the duration of the game, and the recap card (the
        // prior game's result, if any) steps aside until FinishInteractiveGame
        // swaps them back.
        _atBatView.Visible = true;
        _recapCard.Visible = false;
        _bridge.Reset();
        _atBatView.ResetSequencer();
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
        _atBatView.Visible = false;

        if (task.IsCompletedSuccessfully)
        {
            MicroGameResult result = task.Result;
            string final = string.Format(
                FinalFormat, result.AwayScore, result.HomeScore, result.Innings, result.HumanPa);
            _atBatView.AppendPlayLine(final);
            _statusLabel.Text = final;
            _recapLabel.Text = final;
            _recapCard.Visible = true; // persists through idle/skip days until the next game starts
        }
        else
        {
            // Cancelled (scene exit) or faulted; observe so nothing is unobserved.
            _statusLabel.Text = task.Exception?.InnerException is OperationCanceledException
                ? string.Empty
                : task.Exception?.InnerException?.Message ?? string.Empty;
            // The game that would have replaced the prior recap never finished,
            // so that recap is still the last completed game — put it back.
            _recapCard.Visible = _recapLabel.Text.Length > 0;
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
        bool blocked = _gameTask is not null || _awaitingPendingGame || _refreshAfterDayTick
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
        _avatarPortrait.Visible = show;
        _scoutingCard.Visible = show;
        _devCard.Visible = show;
        _statLineCard.Visible = show;
        _standingsCard.Visible = show;
        _leadersCard.Visible = show;
        if (!show)
        {
            // No avatar means no game lifecycle owns the recap — an orphaned
            // "Last Game" card must not outlive the cards around it.
            _recapCard.Visible = false;
            return;
        }

        string avatarId = career.AvatarPlayerId;
        if (!gm.Players.TryGetById(avatarId, out PlayerRow player)
            || !gm.Baseball.TryGetRatings(avatarId, out PlayerRatingsRow ratings)
            || !gm.Baseball.TryGetPotential(avatarId, out PlayerPotentialRow potential))
        {
            return;
        }
        _avatarPortrait.SetIdentity(avatarId, $"{player.FirstName} {player.LastName}");

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
        _tierLabel.Text = string.Format(TierFormat, tierKnown ? TierName(tier) : string.Empty);

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

        RefreshStatLineCard(gm, avatarId, isPitcher);
        // The tier-scoped cards render only when the tier is actually known —
        // a team-less avatar (retired to FA at lineage game-over) must not
        // fall back to default(LeagueTier), which is High School's standings.
        _standingsCard.Visible = tierKnown;
        _leadersCard.Visible = tierKnown;
        if (tierKnown)
        {
            RefreshStandingsCard(gm, tier, player.TeamId);
            RefreshLeadersCard(gm, tier, isPitcher, avatarId);
        }
    }

    /// <summary>
    /// 12c-2 (surface_the_sim.md §4/§5): the avatar's current-season line,
    /// role-aware off the same isPitcher split the scouting card branches on.
    /// A missing row (avatar hasn't played this season yet) renders
    /// <see cref="StatLineNoneText"/> rather than a blank/zeroed line.
    /// </summary>
    private void RefreshStatLineCard(GameManager gm, string avatarId, bool isPitcher)
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

    /// <summary>
    /// 12c-3 (surface_the_sim.md §3/§4/§5): the avatar's tier, teams ranked by
    /// win pct — a new aggregation query (<see cref="BaseballQueries.LoadTeamRecords"/>)
    /// merged in C# with <see cref="BaseballQueries.LoadTeamsByTier"/>'s names,
    /// exactly the doc's prescribed split (no new storage, GB computed here).
    /// Rides the same day-advance cadence as the rest of RefreshScoutingCard.
    /// </summary>
    private void RefreshStandingsCard(GameManager gm, LeagueTier tier, int? avatarTeamId)
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
    /// 12c-3 (surface_the_sim.md §4/§5): top-N leaderboards, role-aware off the
    /// same isPitcher split the scouting/stat-line cards branch on — a batter
    /// sees HR/AVG/OPS, a pitcher sees ERA/W/SO. Each category is its own query
    /// (LoadHrLeaders/LoadAvgLeaders/... — the ORDER BY column can't be bound),
    /// rendered into one heading-plus-rows block per category and joined into
    /// the single label, the DevSummaryLabel/StatLineLabel multi-line idiom.
    /// </summary>
    private void RefreshLeadersCard(GameManager gm, LeagueTier tier, bool isPitcher, string avatarId)
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

        _availabilityCard.Visible = state != SlotAvailability.Available;
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
