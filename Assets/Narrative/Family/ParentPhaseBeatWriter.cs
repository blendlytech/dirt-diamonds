using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Data.Family;
using DirtAndDiamonds.Economy.Family;
using DirtAndDiamonds.Simulation.Baseball;

namespace DirtAndDiamonds.Narrative.Family;

/// <summary>
/// Slice 13-6d (brief §4.4 item 5, §11.5): the parent phase's phone texts. A
/// Narrative-layer subscriber in the <c>PipelineBeatWriter</c> shape
/// (strings-table keys, <c>{tokens}</c> resolved at write time, its writes in
/// their own batch), but its one trigger is <see cref="DayAdvancedEvent"/>:
/// the engine publishes no job or training event, and this slice adds none.
/// What happened is derived from the shipped surface.
///
/// <list type="bullet">
/// <item><b>The first-day text</b> (<c>family_mom</c>): on the first tick after
/// a job started (<c>Game_State.parent_job_since_day</c> + 1), once per job
/// start. A yearly card that keeps the held job doesn't move that key, so it
/// sends nothing.</item>
/// <item><b>The first payday</b> (<c>family_mom</c>): on the first 7-day tick
/// after the job started, with the wage from <see cref="ParentJobService.TryGetLastSettlement"/>
/// (this handler is subscribed after the engine's, so the settlement has
/// landed). Sparing by design: no other payday text.</item>
/// <item><b>The training milestones</b> (<c>family_mom</c>): the first
/// success (a trained child's season or carry coaching is above zero) and the
/// first time a trained child's coaching reaches 70. Both read the child's
/// persisted plan and axes, so a reload can't lose or repeat one; an
/// engine-written <c>Entity_Flags</c> mark per milestone (written in the
/// same batch as its text) keeps each to once.</item>
/// </list>
///
/// The first two are pure arithmetic on persisted day numbers, so a reload
/// between a job and its text still gets the text exactly once. Children come
/// only from <see cref="ChildTrainingService.LoadTrainable"/> (Child edges),
/// so no text can hint at a hidden child. Copy has no avatar pronouns.
/// </summary>
public sealed class ParentPhaseBeatWriter
{
    public const string FirstDayKeyPrefix = "family.job.first_day.";
    public const string FirstPaydayKeyPrefix = "family.payday.first.";
    public const string FirstWinKey = "family.train.first_win";
    public const string Coaching70Key = "family.train.coaching_70";

    /// <summary>Engine-written marks on the parent: the milestone's text has been written.</summary>
    public const string FirstWinFlag = "parent_first_win";
    public const string Coaching70Flag = "parent_coaching_70";

    /// <summary>The coaching axis value that counts as "tops 70" (brief §4.4 item 5).</summary>
    public const int Coaching70Threshold = 70;

    public const string ParentContact = "family_mom";

    private readonly DatabaseManager _db;
    private readonly NarrativeLogQueries _log;
    private readonly GameStateQueries _gameState;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly ParentJobService _jobs;
    private readonly ChildTrainingService _training;
    private readonly GlobalState _state;
    private readonly Action<DayAdvancedEvent> _onDayAdvanced;

    private readonly List<PlayerRow> _trainableScratch = new(4);
    private readonly List<EntityFlagRow> _flagScratch = new(8);

    /// <summary>Resolves a strings-table key to its text at write time; null writes the key.</summary>
    public Func<string, string>? ResolveText;

    /// <summary>Texts written since construction (harness visibility).</summary>
    public int TextsWritten { get; private set; }

    /// <summary>The last text's key and contact (harness visibility).</summary>
    public string LastKey { get; private set; } = "";
    public string LastContact { get; private set; } = "";

    public ParentPhaseBeatWriter(
        DatabaseManager db, NarrativeLogQueries log, GameStateQueries gameState, PlayerQueries players,
        PersonQueries persons, ParentJobService jobs, ChildTrainingService training, GlobalState state)
    {
        _db = db;
        _log = log;
        _gameState = gameState;
        _players = players;
        _persons = persons;
        _jobs = jobs;
        _training = training;
        _state = state;
        _onDayAdvanced = OnDayAdvanced;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onDayAdvanced);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onDayAdvanced);

    private void OnDayAdvanced(DayAdvancedEvent e)
    {
        if (!_gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string parentId) || parentId.Length == 0
            || !_jobs.TryGetCurrentJob(out ParentJobProfile job))
        {
            return; // no avatar, or no job held: the phase is off, or the card is still open
        }

        long since = _jobs.CurrentJobSinceDay;
        if (e.Day == since + 1)
        {
            WriteText(parentId, FirstDayKeyPrefix + job.Id, null, flag: null, e.Day);
        }
        if (e.Day == FirstPaydayAfter(since))
        {
            // The engine's settlement ran on this tick before this handler; fall back to the base wage if it hasn't.
            int wage = _jobs.TryGetLastSettlement(out JobSettlement settlement)
                && settlement.Day == e.Day
                && string.Equals(settlement.JobId, job.Id, StringComparison.Ordinal)
                    ? settlement.Total
                    : job.WeeklyWage;
            WriteText(parentId, FirstPaydayKeyPrefix + job.Id, new[] { ("wage", wage.ToString()) }, flag: null, e.Day);
        }

        CheckTrainingMilestones(parentId, e.Day);
    }

    /// <summary>
    /// The first 7-day tick (a multiple of 7, ChildRearingService's cadence)
    /// strictly after the job's start day: the day the first settlement runs.
    /// </summary>
    public static long FirstPaydayAfter(long jobSinceDay) =>
        (jobSinceDay / ParentJobService.TickCadenceDays + 1) * ParentJobService.TickCadenceDays;

    private void CheckTrainingMilestones(string parentId, long day)
    {
        _players.LoadActiveFlags(parentId, _flagScratch);
        bool winDone = HasFlag(FirstWinFlag);
        bool seventyDone = HasFlag(Coaching70Flag);
        if (winDone && seventyDone)
        {
            return;
        }

        _training.LoadTrainable(parentId, _trainableScratch);
        foreach (PlayerRow child in _trainableScratch)
        {
            // A trained child has a plan key (written on a session); no key, no milestone.
            if (!ChildTrainingService.TryReadPlan(_gameState, child.PlayerId, out TrainingPlan plan))
            {
                continue;
            }
            var tokens = new[] { ("child", child.FirstName) };
            if (!winDone && (plan.SeasonCoachBp > 0 || plan.CoachBp != 0))
            {
                WriteText(parentId, FirstWinKey, tokens, FirstWinFlag, day);
                winDone = true;
            }
            if (!seventyDone && plan.LastSessionWeekStart > 0
                && _persons.TryGetChild(child.PlayerId, out ChildDevelopmentRow axes)
                && axes.Coaching >= Coaching70Threshold)
            {
                WriteText(parentId, Coaching70Key, tokens, Coaching70Flag, day);
                seventyDone = true;
            }
            if (winDone && seventyDone)
            {
                return;
            }
        }
    }

    private bool HasFlag(string flagName)
    {
        foreach (EntityFlagRow row in _flagScratch)
        {
            if (row.IsActive && string.Equals(row.FlagName, flagName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>One text (and, for a milestone, its once-only mark) in one batch.</summary>
    private void WriteText(
        string playerId, string key, (string Name, string Value)[]? tokens, string? flag, long day)
    {
        string body = ResolveText?.Invoke(key) ?? key;
        if (tokens is not null)
        {
            foreach ((string name, string value) in tokens)
            {
                body = body.Replace("{" + name + "}", value, StringComparison.Ordinal);
            }
        }
        _db.BeginBatch();
        try
        {
            _log.InsertText(_state.SeasonYear, _state.CurrentDay, playerId, ParentContact, body);
            if (flag is not null)
            {
                _players.SetFlag(playerId, flag, true, day);
            }
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        TextsWritten++;
        LastKey = key;
        LastContact = ParentContact;
    }
}
