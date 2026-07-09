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

/// <summary>
/// A gritty event's conceive_child consequence, requesting an heir off the
/// avatar (marriage_and_conception.md §4.2). Published by the Narrative
/// consequence applier — which must not reference the Baseball sim — and
/// serviced by CareerManager's subscriber, the same Narrative → bus →
/// consumer routing as <see cref="StressImpulseEvent"/>. The co-parent id is
/// resolved from the live RelationshipGraph and rides the event, so a
/// same-session marriage-then-baby never depends on the day-cadence
/// relationship flush having reached the database.
/// </summary>
public readonly struct ChildConceptionRequestedEvent : IGameEvent
{
    /// <summary>The avatar the request was fired for; the consumer drops it if the avatar has since changed.</summary>
    public readonly string ParentAvatarId;

    /// <summary>The avatar's live Partner counterpart, or null when unmarried (average-parent bloodline path).</summary>
    public readonly string? PartnerId;

    /// <summary>Absolute game-day of the fire (the Game_Logs/Entity_Flags clock).</summary>
    public readonly long Day;

    public ChildConceptionRequestedEvent(string parentAvatarId, string? partnerId, long day)
    {
        ParentAvatarId = parentAvatarId;
        PartnerId = partnerId;
        Day = day;
    }
}

/// <summary>
/// Published by CareerManager after a bus-requested conception's batch has
/// committed (marriage_and_conception.md §4.3) — the seam a future birth
/// notification / family screen renders from. Ships before its consumer,
/// same precedent as GrittyEventResolvedEvent and LastSuccession.
/// </summary>
public readonly struct ChildBornEvent : IGameEvent
{
    public readonly string ChildId;
    public readonly string AvatarId;

    /// <summary>The co-parent, or null when the heir was conceived unpartnered.</summary>
    public readonly string? PartnerId;

    public readonly long Day;

    public ChildBornEvent(string childId, string avatarId, string? partnerId, long day)
    {
        ChildId = childId;
        AvatarId = avatarId;
        PartnerId = partnerId;
        Day = day;
    }
}

/// <summary>
/// Published by CareerManager after the avatar activates on a team — first
/// creation and succession both land here (a boot-time load activates before
/// the career attaches to the bus, so the bridge syncs that case directly).
/// GameManager bridges it into the Life sim's daily clock (avatar pointer +
/// the tier-derived school gate); the two sims still never reference each
/// other, per the architectural wall.
/// </summary>
public readonly struct AvatarChangedEvent : IGameEvent
{
    public readonly string AvatarPlayerId;

    /// <summary>The team the avatar activated on — the bridge derives the tier (and the 9b school gate) from it.</summary>
    public readonly int TeamId;

    public AvatarChangedEvent(string avatarPlayerId, int teamId)
    {
        AvatarPlayerId = avatarPlayerId;
        TeamId = teamId;
    }
}

/// <summary>
/// Published by the Narrative consequence applier AFTER an absence row has
/// committed to Player_Absences (Phase 8c roster availability) — the Baseball
/// sims' AvailabilityLedger consumes it, the same Narrative → bus → ledger
/// routing as <see cref="RivalryChangedEvent"/>. The payload is primitives
/// only (reason mirrors Data's AbsenceReason byte values: 1 = Injury,
/// 2 = Suspension, 3 = Arrest) — this file is compiled by the Data-free
/// NeedsDecayHarness, so no Data enum may appear here.
/// </summary>
public readonly struct PlayerAbsenceChangedEvent : IGameEvent
{
    public readonly string PlayerId;

    /// <summary>AbsenceReason byte value (1 = Injury, 2 = Suspension, 3 = Arrest).</summary>
    public readonly byte Reason;

    /// <summary>Absent while current_day &lt; this; available again ON this day.</summary>
    public readonly long UntilDay;

    /// <summary>Injury rust: effective-rating deduction while in the penalty window (0 = none).</summary>
    public readonly byte RatingPenalty;

    /// <summary>Rusty while UntilDay ≤ current_day &lt; this (0 when no rust window).</summary>
    public readonly long PenaltyUntilDay;

    public PlayerAbsenceChangedEvent(
        string playerId, byte reason, long untilDay, byte ratingPenalty, long penaltyUntilDay)
    {
        PlayerId = playerId;
        Reason = reason;
        UntilDay = untilDay;
        RatingPenalty = ratingPenalty;
        PenaltyUntilDay = penaltyUntilDay;
    }
}

/// <summary>
/// Published by the Economy equipment service AFTER a purchase row has
/// committed to Player_Equipment (Phase 8e equipment quality) — the Baseball
/// sims' EquipmentLedger consumes it, the same Economy/Narrative → bus →
/// ledger routing as <see cref="PlayerAbsenceChangedEvent"/>. The payload is
/// primitives only — this file is compiled by the Data-free NeedsDecayHarness,
/// so no Data type may appear here.
/// </summary>
public readonly struct PlayerEquipmentChangedEvent : IGameEvent
{
    public readonly string PlayerId;

    /// <summary>Owned gear quality, 1–3 (0 is the no-gear default and is never published).</summary>
    public readonly byte Quality;

    public PlayerEquipmentChangedEvent(string playerId, byte quality)
    {
        PlayerId = playerId;
        Quality = quality;
    }
}

/// <summary>
/// HS-4: a person-stat delta already committed to Player_Person by an atomic
/// clamped write inside the publisher's own batch (e.g. ItemService's §3.1
/// self-buy transport reward), mirrored to the Life sim's in-memory person
/// stats so the GPA drift's inputs stay in step — the FundsImpulseEvent
/// pattern exactly. <see cref="Stat"/> is the Simulation.Life.PersonStatId
/// ordinal (0 = Intelligence … 11 = WorkEthic) carried as a raw int: this
/// file is compiled by harnesses that do not compile the Life folder
/// (MonteCarloHarness, CoreLoopHarness), so no Life type may appear here —
/// the PlayerAbsenceChangedEvent primitives rule. GPA has no impulse — the
/// §2.2 weekly closed form is its only mover this arc.
/// </summary>
public readonly struct PersonStatImpulseEvent : IGameEvent
{
    public readonly string PlayerId;

    /// <summary>PersonStatId ordinal, 0–11 in Player_Person column order.</summary>
    public readonly int Stat;

    /// <summary>Signed; the ACTUAL clamped movement the DB took, not the nominal nudge.</summary>
    public readonly float Delta;

    public PersonStatImpulseEvent(string playerId, int stat, float delta)
    {
        PlayerId = playerId;
        Stat = stat;
        Delta = delta;
    }
}

/// <summary>
/// HS-4: published AFTER a Player_Items ownership row has committed —
/// marketplace purchases (ItemService) and §3.2 parental auto-gifts
/// (FamilyService) both raise it, the Economy → bus → consumer routing
/// PlayerEquipmentChangedEvent already uses. GameManager re-projects the
/// avatar's §5.3 transport-hours refund off it; it is also the ownership
/// cache-invalidation seam any UI can ride. Payload is primitives only (the
/// standing CoreEvents rule).
/// </summary>
public readonly struct PlayerItemAcquiredEvent : IGameEvent
{
    public readonly string PlayerId;

    /// <summary>The items.json catalog id just added to Player_Items.</summary>
    public readonly string ItemId;

    public PlayerItemAcquiredEvent(string playerId, string itemId)
    {
        PlayerId = playerId;
        ItemId = itemId;
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
