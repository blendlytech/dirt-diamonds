namespace DirtAndDiamonds.Core;

/// <summary>
/// Marker for payloads carried by the <see cref="EventBus"/>. Implementations
/// must be readonly structs — the bus stores them in typed queues and invokes
/// handlers through <c>Action&lt;T&gt;</c> generic instantiations, so a struct
/// payload never boxes (zero-GC mandate).
/// </summary>
public interface IGameEvent
{
}

/// <summary>
/// Published by <see cref="TimeManager"/> after a calendar tick's batch
/// transaction has committed. Subscribers that persist their reaction open
/// their own batch — Life-sim and Baseball-sim writes never share the tick's
/// transaction (or each other's).
/// </summary>
public readonly struct DayAdvancedEvent : IGameEvent
{
    /// <summary>Absolute 1-based game-day ordinal (matches Entity_Flags.set_on_day).</summary>
    public readonly long Day;

    public readonly int SeasonYear;

    /// <summary>1..<see cref="GlobalState.DaysPerSeason"/> within <see cref="SeasonYear"/>.</summary>
    public readonly int DayOfSeason;

    public DayAdvancedEvent(long day, int seasonYear, int dayOfSeason)
    {
        Day = day;
        SeasonYear = seasonYear;
        DayOfSeason = dayOfSeason;
    }
}

/// <summary>
/// Published immediately after the <see cref="DayAdvancedEvent"/> whose tick
/// crossed a season boundary (day-of-season wrapped back to 1).
/// </summary>
public readonly struct SeasonRolledOverEvent : IGameEvent
{
    public readonly int PreviousSeasonYear;
    public readonly int NewSeasonYear;

    public SeasonRolledOverEvent(int previousSeasonYear, int newSeasonYear)
    {
        PreviousSeasonYear = previousSeasonYear;
        NewSeasonYear = newSeasonYear;
    }
}

/// <summary>
/// Published by the Life sim's RelationshipGraph whenever a pair's rivalry
/// intensity changes; the Baseball sim's RivalryLedger consumes it (BUILD_PLAN
/// Phase 6: rivalry scores feed baseball probability modifiers via the event
/// bus, never a direct reference). The payload is raw ids plus a 0–100
/// intensity — the two sims share no relationship types beyond this struct.
/// </summary>
/// <summary>
/// Published by the Phase-7 Gritty Event dispatcher — from its background
/// polling thread (EventBus.Publish is thread-safe for exactly this caller) —
/// when an event's prerequisites hold and its probability roll succeeds. The
/// consequence applier consumes it on the main pump and resolves the choice.
/// </summary>
public readonly struct GrittyEventFiredEvent : IGameEvent
{
    /// <summary>The event definition's stable JSON id (e.g. "back_alley_bribe").</summary>
    public readonly string EventId;

    public readonly string SubjectPlayerId;

    /// <summary>Absolute game-day the fire was rolled for (the flag-write clock).</summary>
    public readonly long Day;

    public GrittyEventFiredEvent(string eventId, string subjectPlayerId, long day)
    {
        EventId = eventId;
        SubjectPlayerId = subjectPlayerId;
        Day = day;
    }
}

/// <summary>
/// Published by the consequence applier after a fired gritty event's choice
/// has been resolved and its consequences committed — the seam a future event
/// UI/feed renders from (same LastSuccession precedent).
/// </summary>
public readonly struct GrittyEventResolvedEvent : IGameEvent
{
    public readonly string EventId;
    public readonly string SubjectPlayerId;

    /// <summary>Index into the definition's choices array.</summary>
    public readonly int ChoiceIndex;

    public readonly long Day;

    public GrittyEventResolvedEvent(string eventId, string subjectPlayerId, int choiceIndex, long day)
    {
        EventId = eventId;
        SubjectPlayerId = subjectPlayerId;
        ChoiceIndex = choiceIndex;
        Day = day;
    }
}

/// <summary>
/// A stress delta for one tracked person — the Life sim's §4.2 stress
/// scalar's live source. Per life_sim_needs_decay.md §10, cross-system
/// signals like a gritty event raising stress arrive via the EventBus, never
/// a direct call; LifeSimManager subscribes and clamp-accumulates.
/// </summary>
public readonly struct StressImpulseEvent : IGameEvent
{
    public readonly string PlayerId;

    /// <summary>Signed; negative = relief. Accumulated into [0,100].</summary>
    public readonly float Delta;

    public StressImpulseEvent(string playerId, float delta)
    {
        PlayerId = playerId;
        Delta = delta;
    }
}

/// <summary>
/// A funds delta already committed to the Players table (the atomic
/// floor-clamped writer), mirrored to the Life sim's in-memory funds so
/// utility decisions see gritty-event money immediately.
/// </summary>
public readonly struct FundsImpulseEvent : IGameEvent
{
    public readonly string PlayerId;

    public readonly double Delta;

    public FundsImpulseEvent(string playerId, double delta)
    {
        PlayerId = playerId;
        Delta = delta;
    }
}

public readonly struct RivalryChangedEvent : IGameEvent
{
    /// <summary>Canonical pair order: <see cref="PlayerAId"/> sorts before <see cref="PlayerBId"/> ordinally.</summary>
    public readonly string PlayerAId;

    public readonly string PlayerBId;

    /// <summary>0–100; the clamp of a Rival edge's negative affinity. 0 = rivalry dissolved.</summary>
    public readonly byte Intensity;

    public RivalryChangedEvent(string playerAId, string playerBId, byte intensity)
    {
        PlayerAId = playerAId;
        PlayerBId = playerBId;
        Intensity = intensity;
    }
}
