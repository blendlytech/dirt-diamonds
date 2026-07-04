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
