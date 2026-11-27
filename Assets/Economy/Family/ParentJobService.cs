using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Data.Family;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Economy.Family;

/// <summary>One job on the open card, with whether the parent may take it (D8-A: banned_for_life bars Coach).</summary>
public readonly struct ParentJobOption
{
    public readonly ParentJobProfile Job;
    public readonly bool Available;

    /// <summary>The active flag that bars it, when <see cref="Available"/> is false (the phone's reason line).</summary>
    public readonly string? BarredByFlag;

    public ParentJobOption(ParentJobProfile job, bool available, string? barredByFlag)
    {
        Job = job;
        Available = available;
        BarredByFlag = barredByFlag;
    }
}

/// <summary>The open job card (D2-A, the 13-6b decision-card pattern): raised on a day, answered by the player or resolved at the deadline.</summary>
public readonly struct PendingJobChoice
{
    public readonly long SinceDay;

    /// <summary>The day an unanswered card resolves: the held job is kept, or Steady with none (D2-A).</summary>
    public readonly long DeadlineDay;

    /// <summary>The job held while the card is open (a yearly card), or null (the first card).</summary>
    public readonly string? CurrentJobId;

    public readonly IReadOnlyList<ParentJobOption> Options;

    public PendingJobChoice(long sinceDay, long deadlineDay, string? currentJobId, IReadOnlyList<ParentJobOption> options)
    {
        SinceDay = sinceDay;
        DeadlineDay = deadlineDay;
        CurrentJobId = currentJobId;
        Options = options;
    }
}

/// <summary>One weekly settlement (the phone's job strip reads the last one).</summary>
public readonly struct JobSettlement
{
    public readonly long Day;
    public readonly string JobId;
    public readonly int Wage;
    public readonly int Tips;
    public readonly int StressDelta;
    public readonly int RecklessnessDelta;

    /// <summary>The care basis points applied to each child this week (0 when waived or the job has none).</summary>
    public readonly int CareBp;
    public readonly bool CareWaived;
    public readonly int ChildrenTouched;

    public JobSettlement(long day, string jobId, int wage, int tips, int stressDelta, int recklessnessDelta,
        int careBp, bool careWaived, int childrenTouched)
    {
        Day = day;
        JobId = jobId;
        Wage = wage;
        Tips = tips;
        StressDelta = stressDelta;
        RecklessnessDelta = recklessnessDelta;
        CareBp = careBp;
        CareWaived = careWaived;
        ChildrenTouched = childrenTouched;
    }

    /// <summary>The week's total pay: the wage plus the tips.</summary>
    public int Total => Wage + Tips;
}

/// <summary>
/// Slice 13-6c (brief §4.3 item 1, D1–D3-A, D8-A): the parent phase's job.
/// Layer 2 like <see cref="ChildRearingService"/>: it reads the job profile
/// from <see cref="ParentJobCatalog"/>, settles the week's wage (Nightlife's
/// tips from the NGHTTIPS stream), stress, recklessness and the job's care in
/// one batch on the 7-day tick, then publishes <see cref="FundsImpulseEvent"/>
/// and <see cref="StressImpulseEvent"/> after the commit. It owns the job card
/// (TryGetPendingJobChoice / ResolveJobChoice, a 14-day deadline, back at each
/// rollover) and the engine-written job_* and parent_phase flags.
///
/// It never touches the Life sim: <c>GameManager</c> hands it the week's
/// family hours (the same peeked value ChildRearingService reads, R3) and turns
/// its shift hours into the forced Work block and the standing day plan (A3).
/// With no parent phase it is never called, so every path stays bit-identical.
/// </summary>
public sealed class ParentJobService
{
    /// <summary>The card's deadline (D2-A).</summary>
    public const int DecisionWindowDays = 14;

    /// <summary>The settlement cadence: <see cref="ChildRearingService"/>'s 7-day tick.</summary>
    public const int TickCadenceDays = ChildRearingService.TickCadenceDays;

    /// <summary>The engine-written flag that marks the released parent (content gates on it, D10-A).</summary>
    public const string ParentPhaseFlag = "parent_phase";

    /// <summary>"TRAINSED": mixed into the live seed's tick count (§2).</summary>
    public const ulong LiveSeedTag = 0x5452_4149_4E53_4544UL;

    // ChildRearingService's own mirror of the Narrative ChildAxis ordinal (PersonQueries.AdjustChildAxis's contract).
    internal const int CareAxis = 0;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly BaseballQueries _baseball;
    private readonly GameStateQueries _gameState;
    private readonly ParentJobCatalog _catalog;
    private readonly EventBus _bus;

    private readonly List<PlayerRow> _childrenScratch = new(4);
    private readonly List<EntityFlagRow> _flagScratch = new(8);
    private readonly List<string> _holderScratch = new(2);

    private JobSettlement _lastSettlement;
    private bool _hasLastSettlement;

    public ParentJobService(
        DatabaseManager db, PlayerQueries players, PersonQueries persons, BaseballQueries baseball,
        GameStateQueries gameState, ParentJobCatalog catalog, EventBus bus)
    {
        _db = db;
        _players = players;
        _persons = persons;
        _baseball = baseball;
        _gameState = gameState;
        _catalog = catalog;
        _bus = bus;
    }

    public ParentJobCatalog Catalog => _catalog;

    /// <summary>Harness fixtures only: a fixed seed in place of the live tick-count seed, used when the key is first written.</summary>
    public ulong? SeedOverride { get; set; }

    // ------------------------------------------------------------------
    // The seed (§2, A5, R5)
    // ------------------------------------------------------------------

    /// <summary>
    /// The persisted parent-phase seed, written once on first use (live: the
    /// tick count mixed with "TRAINSED", odd; harnesses: <see cref="SeedOverride"/>).
    /// Never a boot draw, so no other consumer's stream moves.
    /// </summary>
    public ulong EnsureSeed()
    {
        if (_gameState.TryGetInt64(GameStateKeys.ParentPhaseSeed, out long stored))
        {
            return unchecked((ulong)stored);
        }
        ulong seed = SeedOverride ?? (unchecked((ulong)Environment.TickCount64) ^ LiveSeedTag | 1UL);
        _db.RunInBatch(() => _gameState.SetInt64(GameStateKeys.ParentPhaseSeed, unchecked((long)seed)));
        return seed;
    }

    // ------------------------------------------------------------------
    // The held job
    // ------------------------------------------------------------------

    /// <summary>The job the parent holds, or false (none yet, or none: the phase is off).</summary>
    public bool TryGetCurrentJob(out ParentJobProfile job)
    {
        if (_gameState.TryGetText(GameStateKeys.ParentJob, out string id) && id.Length > 0)
        {
            job = _catalog.Get(id);
            return true;
        }
        job = null!;
        return false;
    }

    /// <summary>The held job's id, or null.</summary>
    public string? CurrentJobId => TryGetCurrentJob(out ParentJobProfile job) ? job.Id : null;

    /// <summary>The absolute day the held job started, or 0.</summary>
    public long CurrentJobSinceDay =>
        _gameState.TryGetInt64(GameStateKeys.ParentJobSinceDay, out long since) ? since : 0;

    /// <summary>The held job's shift hours on <paramref name="day"/> (a forced Work block, D3-A), or 0 off-shift or with no job.</summary>
    public int ShiftHoursOn(long day) =>
        TryGetCurrentJob(out ParentJobProfile job) && job.IsShiftWeekday((int)GameCalendar.WeekdayForDay(day))
            ? job.ShiftHours
            : 0;

    /// <summary>
    /// A3 (R1): the standing plan's family hours on <paramref name="day"/> (the
    /// day the plan ticks): 1 h when day % 7 ∈ {1, 3, 5}, so every 7-day tick
    /// window holds exactly 3 h, <see cref="ChildRearingService.CareNeutralHoursPerWeek"/>.
    /// The days follow the tick, not weekdays, which restart each season.
    /// </summary>
    public static int StandingFamilyHoursOn(long day)
    {
        long r = day % TickCadenceDays;
        return r is 1 or 3 or 5 ? 1 : 0;
    }

    /// <summary>The last weekly settlement this session (in memory; the funds row is the record).</summary>
    public bool TryGetLastSettlement(out JobSettlement settlement)
    {
        settlement = _lastSettlement;
        return _hasLastSettlement;
    }

    // ------------------------------------------------------------------
    // The card (D2-A)
    // ------------------------------------------------------------------

    /// <summary>
    /// The release and every rollover in the phase: marks the parent with the
    /// parent_phase flag and raises the job card (a no-op while one is open).
    /// </summary>
    public void OpenJobCard(string parentId, long day)
    {
        bool flagged = IsFlagActive(parentId, ParentPhaseFlag);
        bool open = _gameState.TryGetInt64(GameStateKeys.ParentJobPendingSinceDay, out long since) && since > 0;
        if (flagged && open)
        {
            return;
        }
        _db.BeginBatch();
        try
        {
            if (!flagged)
            {
                _players.SetFlag(parentId, ParentPhaseFlag, true, day);
            }
            if (!open)
            {
                _gameState.SetInt64(GameStateKeys.ParentJobPendingSinceDay, Math.Max(1, day));
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
    }

    public bool HasPendingJobChoice =>
        _gameState.TryGetInt64(GameStateKeys.ParentJobPendingSinceDay, out long since) && since > 0;

    /// <summary>The open card with every job and whether the parent may take it; false when no card is open.</summary>
    public bool TryGetPendingJobChoice(string parentId, out PendingJobChoice choice)
    {
        if (!_gameState.TryGetInt64(GameStateKeys.ParentJobPendingSinceDay, out long since) || since <= 0)
        {
            choice = default;
            return false;
        }
        var options = new List<ParentJobOption>(_catalog.Jobs.Count);
        foreach (ParentJobProfile job in _catalog.Jobs)
        {
            bool barred = IsBarred(parentId, job);
            options.Add(new ParentJobOption(job, !barred, barred ? job.BarredByFlag : null));
        }
        choice = new PendingJobChoice(since, since + DecisionWindowDays, CurrentJobId, options);
        return true;
    }

    /// <summary>True when an active flag on the parent bars <paramref name="job"/> (D8-A).</summary>
    public bool IsBarred(string parentId, ParentJobProfile job) =>
        job.BarredByFlag is not null && IsFlagActive(parentId, job.BarredByFlag);

    /// <summary>
    /// The player's answer: takes <paramref name="jobId"/> from
    /// <paramref name="day"/> and closes the card. Refused when no card is
    /// open, the id is unknown, or a flag bars the job.
    /// </summary>
    public void ResolveJobChoice(string parentId, string jobId, long day)
    {
        if (!HasPendingJobChoice)
        {
            throw new InvalidOperationException("No job card is open.");
        }
        if (!_catalog.TryGet(jobId, out ParentJobProfile job))
        {
            throw new ArgumentException($"'{jobId}' is not a parent-phase job.", nameof(jobId));
        }
        if (IsBarred(parentId, job))
        {
            throw new InvalidOperationException($"The '{jobId}' job is barred ({job.BarredByFlag}).");
        }
        TakeJob(parentId, job, day);
    }

    /// <summary>
    /// The deadline: an unanswered card keeps the held job, or takes the
    /// default (Steady) when there is none (D2-A; §11 B2 for the yearly card).
    /// </summary>
    private void ResolveAtDeadline(string parentId, long day)
    {
        ParentJobProfile job = TryGetCurrentJob(out ParentJobProfile held) && !IsBarred(parentId, held)
            ? held
            : _catalog.DefaultJob;
        TakeJob(parentId, job, day);
    }

    private void TakeJob(string parentId, ParentJobProfile job, long day)
    {
        bool hadJob = TryGetCurrentJob(out ParentJobProfile previous);
        bool same = hadJob && string.Equals(previous.Id, job.Id, StringComparison.Ordinal);
        _db.BeginBatch();
        try
        {
            if (!same)
            {
                _gameState.SetText(GameStateKeys.ParentJob, job.Id);
                _gameState.SetInt64(GameStateKeys.ParentJobSinceDay, Math.Max(1, day));
                if (hadJob)
                {
                    _players.SetFlag(parentId, previous.Flag, false, day);
                }
                _players.SetFlag(parentId, job.Flag, true, day);
            }
            _gameState.SetInt64(GameStateKeys.ParentJobPendingSinceDay, 0);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
    }

    // ------------------------------------------------------------------
    // The day and the week
    // ------------------------------------------------------------------

    /// <summary>
    /// The parent phase's daily step, after <see cref="ChildRearingService.ProcessDay"/>
    /// on the same day: an unanswered card past its deadline resolves, and on
    /// the 7-day tick a held job settles. <paramref name="familyHoursThisWeek"/>
    /// is the value GameManager peeked for ChildRearingService before clearing
    /// it (R3), so Nightlife's waiver reads the same hours.
    /// </summary>
    public void ProcessDay(string parentId, long day, float familyHoursThisWeek)
    {
        if (_gameState.TryGetInt64(GameStateKeys.ParentJobPendingSinceDay, out long since) && since > 0
            && day >= since + DecisionWindowDays)
        {
            ResolveAtDeadline(parentId, day);
        }
        if (day % TickCadenceDays == 0 && TryGetCurrentJob(out ParentJobProfile job))
        {
            Settle(parentId, job, day, familyHoursThisWeek);
        }
    }

    private void Settle(string parentId, ParentJobProfile job, long day, float familyHoursThisWeek)
    {
        int tips = job.TipMax > 0 ? ChildTraining.TipDollars(EnsureSeed(), day, job.TipMax) : 0;
        int pay = job.WeeklyWage + tips;
        bool waived = job.WeeklyCareBp != 0 && job.CareWaiverFamilyHours > 0
            && familyHoursThisWeek >= job.CareWaiverFamilyHours;
        int careBp = waived ? 0 : job.WeeklyCareBp;

        int touched = 0;
        if (careBp != 0)
        {
            _players.LoadChildrenOf(parentId, _childrenScratch);
        }
        _db.BeginBatch();
        try
        {
            if (pay != 0)
            {
                _players.AdjustFunds(parentId, pay);
            }
            if (job.WeeklyRecklessness != 0)
            {
                _players.AdjustRecklessness(parentId, job.WeeklyRecklessness);
            }
            if (careBp != 0)
            {
                for (int i = 0; i < _childrenScratch.Count; i++)
                {
                    PlayerRow child = _childrenScratch[i];
                    if (child.Age >= PipelineProfile.TakeoverAge || child.TeamId.HasValue)
                    {
                        continue; // §4.1: every Child-edge child under 16 who is unrostered (ages 0–4 included)
                    }
                    TrainingPlan plan = ChildTrainingService.ReadPlanOrDefault(_gameState, _baseball, child.PlayerId);
                    plan.CareBp += careBp;
                    int whole = ChildTraining.FlushWholePoints(ref plan.CareBp);
                    if (whole != 0)
                    {
                        _persons.AdjustChildAxis(child.PlayerId, CareAxis, whole, day);
                    }
                    ChildTrainingService.WritePlan(_gameState, child.PlayerId, in plan);
                    touched++;
                }
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }

        if (pay != 0)
        {
            _bus.Publish(new FundsImpulseEvent(parentId, pay));
        }
        if (job.WeeklyStress != 0)
        {
            _bus.Publish(new StressImpulseEvent(parentId, job.WeeklyStress));
        }
        _lastSettlement = new JobSettlement(day, job.Id, job.WeeklyWage, tips, job.WeeklyStress, job.WeeklyRecklessness,
            careBp, waived, touched);
        _hasLastSettlement = true;
    }

    // ------------------------------------------------------------------
    // The takeover (§4.3 item 1)
    // ------------------------------------------------------------------

    /// <summary>
    /// The takeover (AvatarChangedEvent to a rostered heir): clears the job,
    /// any open card and every holder's job_* and parent_phase flags. A no-op
    /// with nothing to clear, so a normal avatar change writes nothing.
    /// </summary>
    public void ClearForTakeover(long day)
    {
        bool hasJob = _gameState.TryGetText(GameStateKeys.ParentJob, out string id) && id.Length > 0;
        bool hasSince = _gameState.TryGetInt64(GameStateKeys.ParentJobSinceDay, out long since) && since != 0;
        bool hasCard = HasPendingJobChoice;
        var holders = new List<(string PlayerId, string Flag)>();
        CollectHolders(ParentPhaseFlag, holders);
        foreach (ParentJobProfile job in _catalog.Jobs)
        {
            CollectHolders(job.Flag, holders);
        }
        if (!hasJob && !hasSince && !hasCard && holders.Count == 0)
        {
            return;
        }
        _db.BeginBatch();
        try
        {
            if (hasJob)
            {
                _gameState.SetText(GameStateKeys.ParentJob, string.Empty);
            }
            if (hasSince)
            {
                _gameState.SetInt64(GameStateKeys.ParentJobSinceDay, 0);
            }
            if (hasCard)
            {
                _gameState.SetInt64(GameStateKeys.ParentJobPendingSinceDay, 0);
            }
            foreach ((string playerId, string flag) in holders)
            {
                _players.SetFlag(playerId, flag, false, day);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        _hasLastSettlement = false;
    }

    private void CollectHolders(string flag, List<(string, string)> destination)
    {
        _baseball.LoadActiveFlagPlayerIds(flag, _holderScratch);
        foreach (string playerId in _holderScratch)
        {
            destination.Add((playerId, flag));
        }
    }

    private bool IsFlagActive(string playerId, string flagName)
    {
        _players.LoadActiveFlags(playerId, _flagScratch);
        foreach (EntityFlagRow row in _flagScratch)
        {
            if (row.IsActive && string.Equals(row.FlagName, flagName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

// ----------------------------------------------------------------------
// D6-A fast-forward: the interrupt rules (§4.3 item 3). Pure, so
// GameManager.AdvanceUntil and SR-2a's AdvanceWeek share one rule set. They
// live here only because 13-6c's allowed paths have no other Godot-free home;
// SR-2a may move them beside its stance engine.
// ----------------------------------------------------------------------

/// <summary>Why a fast-forward stopped. The first four follow scope_realignment §3.2's AdvanceWeek order.</summary>
public enum AdvanceStopKind : byte
{
    /// <summary>Still running (a probe with nothing to stop for).</summary>
    None = 0,

    /// <summary>The target day was reached.</summary>
    TargetReached,

    /// <summary>A gritty event with choices is waiting for the player.</summary>
    EventFired,

    /// <summary>Stress rose to the threshold or above.</summary>
    StressSpike,

    /// <summary>Funds fell to zero or below.</summary>
    FundsDepleted,

    /// <summary>The avatar's attended game is waiting (never in the parent phase; SR-2a's).</summary>
    GameDay,

    /// <summary>A decision card is open: the job card, or a pending succession or takeover choice.</summary>
    DecisionCard,

    /// <summary>A child's birthday opened or closed training (ages 5 and 16).</summary>
    TrainingWindow,

    /// <summary>The avatar changed (a takeover at 16 ended the phase).</summary>
    AvatarChanged,
}

/// <summary>What the rules read after one day (and the day before it, for the edge-triggered stress and funds stops).</summary>
public readonly struct AdvanceProbe
{
    public readonly bool EventChoicePending;
    public readonly float StressBefore;
    public readonly float StressAfter;
    public readonly double FundsBefore;
    public readonly double FundsAfter;
    public readonly bool GamePending;

    /// <summary>A decision card opened this day (the job card, a succession or takeover choice); one already open before doesn't stop again.</summary>
    public readonly bool DecisionCardOpened;
    public readonly bool TrainingWindowChanged;
    public readonly bool AvatarChanged;

    public AdvanceProbe(bool eventChoicePending, float stressBefore, float stressAfter, double fundsBefore, double fundsAfter,
        bool gamePending, bool decisionCardOpened, bool trainingWindowChanged, bool avatarChanged)
    {
        EventChoicePending = eventChoicePending;
        StressBefore = stressBefore;
        StressAfter = stressAfter;
        FundsBefore = fundsBefore;
        FundsAfter = fundsAfter;
        GamePending = gamePending;
        DecisionCardOpened = decisionCardOpened;
        TrainingWindowChanged = trainingWindowChanged;
        AvatarChanged = avatarChanged;
    }
}

/// <summary>A fast-forward's result, shaped like SR-2a's week summary.</summary>
public readonly struct AdvanceSummary
{
    public readonly long StartDay;
    public readonly long EndDay;
    public readonly AdvanceStopKind Stop;
    public readonly double FundsDelta;
    public readonly float StressDelta;

    public AdvanceSummary(long startDay, long endDay, AdvanceStopKind stop, double fundsDelta, float stressDelta)
    {
        StartDay = startDay;
        EndDay = endDay;
        Stop = stop;
        FundsDelta = fundsDelta;
        StressDelta = stressDelta;
    }

    public long DaysAdvanced => EndDay - StartDay;
}

/// <summary>
/// The D6-A interrupt rules, in order: a gritty event with choices, stress,
/// funds, the attended game (scope_realignment §3.2's AdvanceWeek order), then
/// a decision card, a training birthday and an avatar change. Stress, funds
/// and cards are edge-triggered: they stop the day the value crosses the line
/// or the card opens, so a parent already over the line, or one who has seen
/// an open card (it resolves itself at its deadline), can still fast-forward.
/// </summary>
public static class AdvanceInterruptRules
{
    public static AdvanceStopKind FirstStop(in AdvanceProbe p, float stressThreshold)
    {
        if (p.EventChoicePending)
        {
            return AdvanceStopKind.EventFired;
        }
        if (p.StressAfter >= stressThreshold && p.StressBefore < stressThreshold)
        {
            return AdvanceStopKind.StressSpike;
        }
        if (p.FundsAfter <= 0.0 && p.FundsBefore > 0.0)
        {
            return AdvanceStopKind.FundsDepleted;
        }
        if (p.GamePending)
        {
            return AdvanceStopKind.GameDay;
        }
        if (p.DecisionCardOpened)
        {
            return AdvanceStopKind.DecisionCard;
        }
        if (p.TrainingWindowChanged)
        {
            return AdvanceStopKind.TrainingWindow;
        }
        return p.AvatarChanged ? AdvanceStopKind.AvatarChanged : AdvanceStopKind.None;
    }

    /// <summary>
    /// Before each day: what GameManager.CanAdvanceDay refuses (a gritty
    /// choice, a pending succession or takeover choice) and a parked game stop
    /// the advance without ticking.
    /// </summary>
    public static AdvanceStopKind BlockingStop(bool eventChoicePending, bool successionChoicePending, bool gamePending)
    {
        if (eventChoicePending)
        {
            return AdvanceStopKind.EventFired;
        }
        if (gamePending)
        {
            return AdvanceStopKind.GameDay;
        }
        return successionChoicePending ? AdvanceStopKind.DecisionCard : AdvanceStopKind.None;
    }
}
