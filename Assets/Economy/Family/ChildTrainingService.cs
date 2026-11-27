using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Data.Family;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Economy.Family;

/// <summary>The week's shared session budget (D6-A): the job's count, spent by drills, the rest resolved by autopilot at week's end.</summary>
public readonly struct TrainingBudget
{
    public readonly int Total;
    public readonly int Used;

    /// <summary>The start day (a multiple of 7, the last tick day) of the training week.</summary>
    public readonly long WeekStartDay;

    public TrainingBudget(int total, int used, long weekStartDay)
    {
        Total = total;
        Used = used;
        WeekStartDay = weekStartDay;
    }

    public int Remaining => Math.Max(0, Total - Used);
}

/// <summary>Why a child can't be trained right now (the phone's disabled reason).</summary>
public enum TrainingBlock : byte
{
    None = 0,

    /// <summary>The parent holds no job yet (the card is open), so there is no budget.</summary>
    NoJob,

    /// <summary>This week's sessions are spent.</summary>
    NoSessionsLeft,

    /// <summary>Not the parent's child by a Child edge.</summary>
    NotAChild,

    /// <summary>Younger than 5 or older than 15.</summary>
    OutsideAges,

    /// <summary>On a team.</summary>
    Rostered,
}

/// <summary>One resolved session, for the phone's result line.</summary>
public readonly struct TrainingSessionResult
{
    public readonly string ChildId;
    public readonly long Day;
    public readonly long WeekStartDay;
    public readonly int SessionIndex;
    public readonly bool Autopilot;
    public readonly TrainingFocus Focus;
    public readonly TrainingIntensity Intensity;
    public readonly TrainingSessionOutcome Outcome;

    /// <summary>The coaching basis points that landed after the season cap.</summary>
    public readonly int CoachingBpApplied;

    /// <summary>Whole coaching and care points this session moved into Child_Development.</summary>
    public readonly int CoachingPointsMoved;
    public readonly int CarePointsMoved;

    public TrainingSessionResult(string childId, long day, long weekStartDay, int sessionIndex, bool autopilot,
        TrainingFocus focus, TrainingIntensity intensity, in TrainingSessionOutcome outcome,
        int coachingBpApplied, int coachingPointsMoved, int carePointsMoved)
    {
        ChildId = childId;
        Day = day;
        WeekStartDay = weekStartDay;
        SessionIndex = sessionIndex;
        Autopilot = autopilot;
        Focus = focus;
        Intensity = intensity;
        Outcome = outcome;
        CoachingBpApplied = coachingBpApplied;
        CoachingPointsMoved = coachingPointsMoved;
        CarePointsMoved = carePointsMoved;
    }
}

/// <summary>
/// Slice 13-6c (brief §4.3 item 2, D4–D6-A): backyard training. Layer 2 over
/// the pure <see cref="ChildTraining"/> rules, the <see cref="ChildRearingService"/>
/// shape: the trainable children are <see cref="PlayerQueries.LoadChildrenOf"/>
/// (Child edges only, never a parentage read, so a hidden child can't appear),
/// aged 5–15 and unrostered. The week's budget is the held job's count.
///
/// <see cref="PlayDrill"/> spends one session and resolves it now: the axis
/// flush, <c>training_sessions_used</c> and the child's <c>training_plan</c>
/// key commit in one batch on the press, so a reload after a drill sees it and
/// a reload before replays the same stream. At week's end (the 7-day tick)
/// <see cref="ProcessDay"/> resolves every unplayed session under the standing
/// plans at the neutral score (no tilt), then applies the idle fade. Axis
/// writes go through <see cref="PersonQueries.AdjustChildAxis"/> in whole
/// points from the basis-point carry. With no job the service writes nothing,
/// so every path without the parent phase stays bit-identical.
/// </summary>
public sealed class ChildTrainingService
{
    private const int CoachingAxis = 1;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly BaseballQueries _baseball;
    private readonly GameStateQueries _gameState;
    private readonly GlobalState _state;
    private readonly ParentJobService _jobs;

    private readonly List<PlayerRow> _childrenScratch = new(4);
    private readonly List<PlayerRow> _trainableScratch = new(4);
    private readonly Dictionary<string, TrainingSessionResult> _lastResults = new(StringComparer.Ordinal);

    public ChildTrainingService(
        DatabaseManager db, PlayerQueries players, PersonQueries persons, BaseballQueries baseball,
        GameStateQueries gameState, GlobalState state, ParentJobService jobs)
    {
        _db = db;
        _players = players;
        _persons = persons;
        _baseball = baseball;
        _gameState = gameState;
        _state = state;
        _jobs = jobs;
    }

    /// <summary>The start day of the training week holding <paramref name="day"/>: the last 7-day tick at or before it.</summary>
    public static long WeekStartFor(long day) => day - (day % ParentJobService.TickCadenceDays);

    // ------------------------------------------------------------------
    // Plans (training_plan:<childId>)
    // ------------------------------------------------------------------

    /// <summary>The child's persisted plan, or false (no key: never trained, never touched by a job).</summary>
    public static bool TryReadPlan(GameStateQueries gameState, string childId, out TrainingPlan plan)
    {
        if (gameState.TryGetText(GameStateKeys.TrainingPlan(childId), out string text) && text.Length > 0)
        {
            plan = TrainingPlan.Parse(text);
            return true;
        }
        plan = default;
        return false;
    }

    /// <summary>The child's plan, or the default (Normal, in the child's role) when no key exists.</summary>
    public static TrainingPlan ReadPlanOrDefault(GameStateQueries gameState, BaseballQueries baseball, string childId)
    {
        if (TryReadPlan(gameState, childId, out TrainingPlan plan))
        {
            return plan;
        }
        bool isPitcher = baseball.TryGetRatings(childId, out PlayerRatingsRow ratings) && ratings.IsPitcher;
        return TrainingPlan.Default(isPitcher);
    }

    public static void WritePlan(GameStateQueries gameState, string childId, in TrainingPlan plan) =>
        gameState.SetText(GameStateKeys.TrainingPlan(childId), plan.Format());

    /// <summary>The child's standing plan for the phone (the default when no key exists; reading writes nothing).</summary>
    public TrainingPlan GetPlan(string childId) => ReadPlanOrDefault(_gameState, _baseball, childId);

    /// <summary>The household setting (D6-A): the child's focus and intensity, kept for every later session.</summary>
    public void SetPlan(string parentId, string childId, TrainingFocus focus, TrainingIntensity intensity)
    {
        if (!IsChildOf(parentId, childId))
        {
            throw new ArgumentException($"'{childId}' is not a child of the parent.", nameof(childId));
        }
        TrainingPlan plan = GetPlan(childId);
        if (TryReadPlan(_gameState, childId, out _) && plan.Focus == focus && plan.Intensity == intensity)
        {
            return;
        }
        plan.Focus = focus;
        plan.Intensity = intensity;
        _db.RunInBatch(() => WritePlan(_gameState, childId, in plan));
    }

    // ------------------------------------------------------------------
    // Who and how many
    // ------------------------------------------------------------------

    /// <summary>
    /// The trainable children: Child edges of the parent (LoadChildrenOf, so
    /// never a hidden child), aged 5–15, unrostered, ordered by player_id.
    /// </summary>
    public int LoadTrainable(string parentId, List<PlayerRow> destination)
    {
        _players.LoadChildrenOf(parentId, _childrenScratch);
        destination.Clear();
        foreach (PlayerRow child in _childrenScratch)
        {
            if (ChildTraining.IsTrainableAge(child.Age) && !child.TeamId.HasValue)
            {
                destination.Add(child);
            }
        }
        destination.Sort(static (a, b) => string.CompareOrdinal(a.PlayerId, b.PlayerId));
        return destination.Count;
    }

    /// <summary>The week's budget on <paramref name="day"/>: the held job's sessions (0 with no job) and those spent this week.</summary>
    public TrainingBudget GetBudget(long day)
    {
        long week = WeekStartFor(day);
        int total = _jobs.TryGetCurrentJob(out ParentJobProfile job) ? job.SessionsPerWeek : 0;
        return new TrainingBudget(total, UsedIn(week), week);
    }

    /// <summary>Why <paramref name="childId"/> can't be trained on <paramref name="day"/>, or <see cref="TrainingBlock.None"/>.</summary>
    public TrainingBlock CanTrain(string parentId, string childId, long day)
    {
        if (!_jobs.TryGetCurrentJob(out _))
        {
            return TrainingBlock.NoJob;
        }
        _players.LoadChildrenOf(parentId, _childrenScratch);
        PlayerRow? child = null;
        foreach (PlayerRow row in _childrenScratch)
        {
            if (string.Equals(row.PlayerId, childId, StringComparison.Ordinal))
            {
                child = row;
                break;
            }
        }
        if (child is not PlayerRow found)
        {
            return TrainingBlock.NotAChild;
        }
        if (!ChildTraining.IsTrainableAge(found.Age))
        {
            return TrainingBlock.OutsideAges;
        }
        if (found.TeamId.HasValue)
        {
            return TrainingBlock.Rostered;
        }
        return GetBudget(day).Remaining > 0 ? TrainingBlock.None : TrainingBlock.NoSessionsLeft;
    }

    /// <summary>The parent's expertise (§4.2): from the peak of the current and potential role sums; 0 without ratings.</summary>
    public int ExpertisePpFor(string parentId)
    {
        if (!_baseball.TryGetRatings(parentId, out PlayerRatingsRow current))
        {
            return 0;
        }
        PitcherRole role = !current.IsPitcher
            ? PitcherRole.None
            : _baseball.TryGetPitcherRole(parentId, out PitcherRole stored) && stored != PitcherRole.None
                ? stored
                : PitcherRole.Starter;
        PlayerPotentialRow potential = _baseball.TryGetPotential(parentId, out PlayerPotentialRow p) ? p : ToPotential(in current);
        return ChildTraining.ExpertisePp(ChildTraining.PeakRoleSum(role, in current, in potential));
    }

    /// <summary>The last session this session resolved for the child (in memory; the text log is the record).</summary>
    public bool TryGetLastResult(string childId, out TrainingSessionResult result) =>
        _lastResults.TryGetValue(childId, out result);

    // ------------------------------------------------------------------
    // The drill (one intent) and the week's end
    // ------------------------------------------------------------------

    /// <summary>
    /// The drill's one intent: spends one of this week's sessions on the
    /// child and resolves it now with the drill's score in [0, 1] (0.5 = no
    /// tilt). One batch on the press. Refused when <see cref="CanTrain"/> isn't None.
    /// </summary>
    public TrainingSessionResult PlayDrill(string parentId, string childId, double score, float parentStress, long day)
    {
        TrainingBlock block = CanTrain(parentId, childId, day);
        if (block != TrainingBlock.None)
        {
            throw new InvalidOperationException($"Can't train '{childId}' today: {block}.");
        }
        _jobs.TryGetCurrentJob(out ParentJobProfile job);
        ulong seed = _jobs.EnsureSeed();
        long week = WeekStartFor(day);
        int index = UsedIn(week);
        int expertise = ExpertisePpFor(parentId);
        int fatigue = ChildTraining.FatiguePp(parentStress);
        TrainingSessionResult result;
        _db.BeginBatch();
        try
        {
            result = ResolveInBatch(seed, childId, week, index, score, expertise, fatigue, job, day, autopilot: false);
            WriteUsed(week, index + 1);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        _lastResults[childId] = result;
        return result;
    }

    /// <summary>
    /// The week's end, on the 7-day tick (after the job's settlement): the
    /// week that just closed resolves its unplayed sessions under the standing
    /// plans at the neutral score, spread round-robin over the trainable
    /// children ordered by player_id (continuing across weeks, the untrained
    /// first); then every trainable child with no session that week fades; then
    /// the budget opens for the new week. One batch. Nothing without a job.
    /// </summary>
    public void ProcessDay(string parentId, long day, float parentStress)
    {
        if (day % ParentJobService.TickCadenceDays != 0 || !_jobs.TryGetCurrentJob(out ParentJobProfile job))
        {
            return;
        }
        if (_gameState.TryGetInt64(GameStateKeys.TrainingWeekStartDay, out long opened) && opened >= day)
        {
            return; // this tick already closed the week (and opened the next): resolve once
        }
        long week = day - ParentJobService.TickCadenceDays;
        int used = UsedIn(week);
        int remaining = Math.Max(0, job.SessionsPerWeek - used);
        LoadTrainable(parentId, _trainableScratch);
        var results = new List<TrainingSessionResult>(remaining);
        bool autopilot = remaining > 0 && _trainableScratch.Count > 0;
        // The seed's first write is its own batch, so it lands before this one opens.
        ulong seed = autopilot ? _jobs.EnsureSeed() : 0;
        int expertise = autopilot ? ExpertisePpFor(parentId) : 0;
        int fatigue = ChildTraining.FatiguePp(parentStress);
        _db.BeginBatch();
        try
        {
            if (autopilot)
            {
                List<string> order = AutopilotOrder(week, job.SessionsPerWeek, remaining);
                for (int s = 0; s < order.Count; s++)
                {
                    results.Add(ResolveInBatch(seed, order[s], week, used + s, ChildTraining.TrainingProfile.NeutralScore,
                        expertise, fatigue, job, day, autopilot: true));
                }
            }
            foreach (PlayerRow child in _trainableScratch)
            {
                FadeInBatch(child.PlayerId, week, day);
            }
            _gameState.SetInt64(GameStateKeys.TrainingWeekStartDay, day);
            _gameState.SetInt64(GameStateKeys.TrainingSessionsUsed, 0);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        foreach (TrainingSessionResult r in results)
        {
            _lastResults[r.ChildId] = r;
        }
        LastAutopilotSessions = results.Count;
    }

    /// <summary>Sessions the last week's end resolved on autopilot (in memory; harness and summary visibility).</summary>
    public int LastAutopilotSessions { get; private set; }

    /// <summary>
    /// The autopilot's children for the week's unplayed sessions: the
    /// trainable list rotated so the round-robin continues across weeks
    /// (session g of the phase → child g mod n), children with no session this
    /// week first, then the rotation again.
    /// </summary>
    private List<string> AutopilotOrder(long week, int budget, int remaining)
    {
        int n = _trainableScratch.Count;
        int offset = (int)((week / ParentJobService.TickCadenceDays * budget) % n);
        var rotated = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            rotated.Add(_trainableScratch[(offset + i) % n].PlayerId);
        }
        var order = new List<string>(remaining);
        foreach (string id in rotated)
        {
            if (order.Count < remaining && ReadPlanOrDefault(_gameState, _baseball, id).LastSessionWeekStart != week)
            {
                order.Add(id);
            }
        }
        for (int i = 0; order.Count < remaining; i++)
        {
            order.Add(rotated[i % n]);
        }
        return order;
    }

    private TrainingSessionResult ResolveInBatch(ulong seed, string childId, long week, int sessionIndex, double score,
        int expertisePp, int fatiguePp, ParentJobProfile job, long day, bool autopilot)
    {
        TrainingPlan plan = ReadPlanOrDefault(_gameState, _baseball, childId);
        int coaching = _persons.TryGetChild(childId, out ChildDevelopmentRow dev) ? dev.Coaching : ChildDevelopmentRow.Neutral(childId).Coaching;
        int odds = ChildTraining.OddsPp(plan.Intensity, job.DrillBonusPp, expertisePp, ChildTraining.TiltPp(score), fatiguePp);
        RngState rng = ChildTraining.SessionStream(seed, childId, week, sessionIndex);
        TrainingSessionOutcome outcome = ChildTraining.Resolve(ref rng, plan.Intensity, odds, coaching);

        int applied = ChildTraining.AddCoachingGain(ref plan, outcome.CoachingBp, _state.SeasonYearForDay(day));
        plan.CareBp += outcome.CareBp;
        plan.LastSessionWeekStart = week;
        int coachPoints = ChildTraining.FlushWholePoints(ref plan.CoachBp);
        int carePoints = ChildTraining.FlushWholePoints(ref plan.CareBp);
        if (coachPoints != 0)
        {
            _persons.AdjustChildAxis(childId, CoachingAxis, coachPoints, day);
        }
        if (carePoints != 0)
        {
            _persons.AdjustChildAxis(childId, ParentJobService.CareAxis, carePoints, day);
        }
        WritePlan(_gameState, childId, in plan);
        return new TrainingSessionResult(childId, day, week, sessionIndex, autopilot, plan.Focus, plan.Intensity,
            in outcome, applied, coachPoints, carePoints);
    }

    private void FadeInBatch(string childId, long week, long day)
    {
        bool hasPlan = TryReadPlan(_gameState, childId, out TrainingPlan plan);
        if (hasPlan && plan.LastSessionWeekStart == week)
        {
            return; // trained this week
        }
        int coaching = _persons.TryGetChild(childId, out ChildDevelopmentRow dev) ? dev.Coaching : ChildDevelopmentRow.Neutral(childId).Coaching;
        if (!hasPlan)
        {
            plan = ReadPlanOrDefault(_gameState, _baseball, childId);
        }
        if (ChildTraining.ApplyIdleFade(ref plan, coaching) == 0)
        {
            return; // at or below 50: nothing to fade, and no key is written for it
        }
        int whole = ChildTraining.FlushWholePoints(ref plan.CoachBp);
        if (whole != 0)
        {
            _persons.AdjustChildAxis(childId, CoachingAxis, whole, day);
        }
        WritePlan(_gameState, childId, in plan);
    }

    private int UsedIn(long week) =>
        _gameState.TryGetInt64(GameStateKeys.TrainingWeekStartDay, out long start) && start == week
        && _gameState.TryGetInt64(GameStateKeys.TrainingSessionsUsed, out long used)
            ? (int)used
            : 0;

    private void WriteUsed(long week, int used)
    {
        _gameState.SetInt64(GameStateKeys.TrainingWeekStartDay, week);
        _gameState.SetInt64(GameStateKeys.TrainingSessionsUsed, used);
    }

    private bool IsChildOf(string parentId, string childId)
    {
        _players.LoadChildrenOf(parentId, _childrenScratch);
        foreach (PlayerRow row in _childrenScratch)
        {
            if (string.Equals(row.PlayerId, childId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static PlayerPotentialRow ToPotential(in PlayerRatingsRow r) => new()
    {
        PlayerId = r.PlayerId,
        BatPower = r.BatPower,
        BatContact = r.BatContact,
        BatDiscipline = r.BatDiscipline,
        PitStuff = r.PitStuff,
        PitControl = r.PitControl,
        PitStamina = r.PitStamina,
        Fielding = r.Fielding,
    };
}
