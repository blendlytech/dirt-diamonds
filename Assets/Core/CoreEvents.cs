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
/// HS-6 follow-up (the per-game person refresh): published AFTER a write that
/// moved one of the §6.2 sim levers (happiness / confidence / teamwork) has
/// COMMITTED to Player_Person — the Baseball sims' PersonLedger consumes it,
/// the same writer → bus → ledger routing as
/// <see cref="PlayerEquipmentChangedEvent"/>. The payload carries the
/// committed row's ABSOLUTE lever values, read back post-commit — never the
/// nominal nudge (AdjustStat clamps in SQL, so only the row knows what
/// actually moved; absolutes make mirror and row agree by construction,
/// where a delta transport would need the EquipmentLedger's merge-rule
/// gymnastics). Deliberately distinct from <see cref="PersonStatImpulseEvent"/>:
/// that impulse is the LIFE sim's in-memory mirror feed and carries deltas
/// the Life sim folds into its own hydrated copy — the daily settle flush is
/// the Life sim's own write, so re-publishing it as an impulse would
/// double-apply. Payload is primitives only (the standing CoreEvents rule).
/// </summary>
public readonly struct PersonLeversChangedEvent : IGameEvent
{
    public readonly string PlayerId;

    /// <summary>Committed happiness, 0–100 (→ bat_contact / pit_control, §6.2).</summary>
    public readonly byte Happiness;

    /// <summary>Committed confidence, 0–100 (→ bat_power / pit_stuff, §6.2).</summary>
    public readonly byte Confidence;

    /// <summary>Committed teamwork, 0–100 (→ lineup team-defense share, §6.2).</summary>
    public readonly byte Teamwork;

    public PersonLeversChangedEvent(string playerId, byte happiness, byte confidence, byte teamwork)
    {
        PlayerId = playerId;
        Happiness = happiness;
        Confidence = confidence;
        Teamwork = teamwork;
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

/// <summary>
/// Published by the baseball sim's <c>PlayoffManager</c> after the batch that
/// decided a postseason series commits (slice 13-4, league_restructure.md §13).
/// Ids and counts only (Core events carry primitives, like
/// <see cref="RivalryChangedEvent"/>); presentation resolves names. Carries
/// the tier on every publish as its LeagueTier value (HS 0, College 1, Pro 2).
/// </summary>
public readonly struct PlayoffSeriesDecidedEvent : IGameEvent
{
    public readonly int SeasonYear;

    /// <summary>The LeagueTier value: 0 HS, 1 College, 2 Pro.</summary>
    public readonly byte Tier;

    /// <summary>Playoff_Series.series_id (a rowid: display and lookup only, never a stream key).</summary>
    public readonly long SeriesId;

    public readonly int Round;
    public readonly int WinnerTeamId;
    public readonly int LoserTeamId;
    public readonly int WinnerWins;
    public readonly int LoserWins;

    public PlayoffSeriesDecidedEvent(
        int seasonYear, byte tier, long seriesId, int round,
        int winnerTeamId, int loserTeamId, int winnerWins, int loserWins)
    {
        SeasonYear = seasonYear;
        Tier = tier;
        SeriesId = seriesId;
        Round = round;
        WinnerTeamId = winnerTeamId;
        LoserTeamId = loserTeamId;
        WinnerWins = winnerWins;
        LoserWins = loserWins;
    }
}

/// <summary>
/// Published once per tier-season when its final is decided, after the batch
/// commits (slice 13-4, spec §8.2a/§13). <see cref="Tier"/> is ALWAYS set: only
/// a Pro title can satisfy the Phase L reveal predicate
/// (<see cref="ChampionshipRules.IsRevealEligibleTier"/>); an HS or College
/// title never can. A crash between the commit and the publication marker
/// republishes on boot, so consumers dedupe per (season, tier).
/// </summary>
public readonly struct ChampionshipWonEvent : IGameEvent
{
    public readonly int SeasonYear;

    /// <summary>The LeagueTier value: 0 HS, 1 College, 2 Pro.</summary>
    public readonly byte Tier;

    public readonly int TeamId;

    public ChampionshipWonEvent(int seasonYear, byte tier, int teamId)
    {
        SeasonYear = seasonYear;
        Tier = tier;
        TeamId = teamId;
    }
}

/// <summary>The reveal-eligibility rule beside the event (spec §8.2a, §16-I8); the reveal itself is Phase L's.</summary>
public static class ChampionshipRules
{
    /// <summary>LeagueTier.Pro's value (GEMINI §2: HS = 0, College = 1, Pro = 2); the harness pins the equality.</summary>
    public const byte ProTier = 2;

    /// <summary>Only the Pro World Championship can fire the GOAT-father reveal.</summary>
    public static bool IsRevealEligibleTier(byte tier) => tier == ProTier;
}

/// <summary>
/// The avatar's Pro designation changed at a Sunday lock (slice 13-5, spec
/// §7, brief D5-A), published after the lock's batch commits. Avatar only,
/// and only on a transition (<see cref="IsActive"/> ≠ <see cref="WasActive"/>).
/// <see cref="WasActive"/> comes only from the row that covered the week just
/// ending (its <c>locked_through_day</c> equals this lock's day); with no such
/// row the default (active) applies, so a season's first lock never reads a
/// stale row from the last one (brief §10.5 C2). Primitives only: the
/// Narrative coach-beat writer consumes it without referencing Baseball.
/// </summary>
public readonly struct RosterDesignationChangedEvent : IGameEvent
{
    public readonly string PlayerId;
    public readonly int TeamId;
    public readonly bool IsActive;
    public readonly bool WasActive;

    /// <summary>The next lock day (absolute, exclusive): the designation covers days before it.</summary>
    public readonly long LockedThroughDay;

    public RosterDesignationChangedEvent(string playerId, int teamId, bool isActive, bool wasActive, long lockedThroughDay)
    {
        PlayerId = playerId;
        TeamId = teamId;
        IsActive = isActive;
        WasActive = wasActive;
        LockedThroughDay = lockedThroughDay;
    }
}

/// <summary>
/// A game was forfeited (slice 13-5, spec §4, brief D2): a team couldn't
/// field 9 eligible position players and a legal pitcher. Published once,
/// after the result commits (macro regular-season forfeits after the Sunday
/// flush that writes them; attended and postseason forfeits on the game day).
/// The score is <c>RegulationInnings(tier)</c>–0 to <see cref="WinnerTeamId"/>,
/// with <c>Schedule_Games.status = 2</c> and no player stats.
/// </summary>
public readonly struct GameForfeitedEvent : IGameEvent
{
    /// <summary><see cref="ForfeitMask"/> bit: the home team couldn't field.</summary>
    public const byte HomeForfeited = 1;

    /// <summary><see cref="ForfeitMask"/> bit: the away team couldn't field.</summary>
    public const byte AwayForfeited = 2;

    public readonly int SeasonYear;

    /// <summary>The LeagueTier value: 0 HS, 1 College, 2 Pro.</summary>
    public readonly byte Tier;

    /// <summary>Schedule_Games.game_id.</summary>
    public readonly long GameId;

    /// <summary>Schedule_Games.phase: 0 regular season, 1 College play-in, 2 postseason.</summary>
    public readonly byte Phase;

    public readonly int HomeTeamId;
    public readonly int AwayTeamId;

    /// <summary><see cref="HomeForfeited"/> | <see cref="AwayForfeited"/>; both set is a double forfeit.</summary>
    public readonly byte ForfeitMask;

    /// <summary>The team awarded the game (the home team or high seed on a double forfeit, D2d-A).</summary>
    public readonly int WinnerTeamId;

    public GameForfeitedEvent(
        int seasonYear, byte tier, long gameId, byte phase, int homeTeamId, int awayTeamId, byte forfeitMask, int winnerTeamId)
    {
        SeasonYear = seasonYear;
        Tier = tier;
        GameId = gameId;
        Phase = phase;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        ForfeitMask = forfeitMask;
        WinnerTeamId = winnerTeamId;
    }

    /// <summary>True when <paramref name="teamId"/> is the side (or a side) that couldn't field.</summary>
    public bool Forfeited(int teamId) =>
        (teamId == HomeTeamId && (ForfeitMask & HomeForfeited) != 0)
        || (teamId == AwayTeamId && (ForfeitMask & AwayForfeited) != 0);

    public bool Involves(int teamId) => teamId == HomeTeamId || teamId == AwayTeamId;
}

// ----------------------------------------------------------------------
// Slice 13-6: the pipeline (league_restructure.md §9; brief D4b-C, D5-A).
// Each publishes after its batch commits; primitives only, so Life,
// Narrative and UI learn about the pipeline without referencing Baseball.
// ----------------------------------------------------------------------

/// <summary>
/// The avatar's recruiting offers landed (Offers Day, the first Monday of
/// October): <see cref="OfferCount"/> Recruiting_Offers rows (status 0) for
/// <see cref="SeasonYear"/>. A walk-on is one offer with
/// <see cref="IsWalkOn"/> set. The phone's offer texts key on it.
/// </summary>
public readonly struct RecruitingOffersEvent : IGameEvent
{
    public readonly string PlayerId;
    public readonly int SeasonYear;
    public readonly int OfferCount;
    public readonly bool IsWalkOn;

    public RecruitingOffersEvent(string playerId, int seasonYear, int offerCount, bool isWalkOn)
    {
        PlayerId = playerId;
        SeasonYear = seasonYear;
        OfferCount = offerCount;
        IsWalkOn = isWalkOn;
    }
}

/// <summary>
/// The avatar signed with a College program on Signing Day (the second
/// Wednesday of November). He joins <see cref="TeamId"/> at the rollover.
/// The Signing Day cutscene hook (16b).
/// </summary>
public readonly struct RecruitSignedEvent : IGameEvent
{
    public readonly string PlayerId;
    public readonly int TeamId;
    public readonly int SeasonYear;
    public readonly long Day;

    public RecruitSignedEvent(string playerId, int teamId, int seasonYear, long day)
    {
        PlayerId = playerId;
        TeamId = teamId;
        SeasonYear = seasonYear;
        Day = day;
    }
}

/// <summary>
/// The July Draft committed (128 picks). <see cref="AvatarOverallPick"/> and
/// <see cref="AvatarTeamId"/> are 0 when the avatar wasn't picked.
/// <see cref="AvatarPlayerId"/> names the avatar when he was draft-eligible
/// (a College class ≥ 2 before the picks; null otherwise), and
/// <see cref="AvatarWasAutomaticSenior"/> says he was a class-4 senior, so
/// certainly in the pool. Both come from the pre-draft enrollment, so Draft
/// Night's text needs no state from day 176 (13-6a S1: reload-safe).
/// </summary>
public readonly struct DraftCompletedEvent : IGameEvent
{
    public readonly int SeasonYear;
    public readonly long Day;
    public readonly int AvatarOverallPick;
    public readonly int AvatarTeamId;
    public readonly string? AvatarPlayerId;
    public readonly bool AvatarWasAutomaticSenior;

    public DraftCompletedEvent(int seasonYear, long day, int avatarOverallPick, int avatarTeamId,
        string? avatarPlayerId, bool avatarWasAutomaticSenior)
    {
        SeasonYear = seasonYear;
        Day = day;
        AvatarOverallPick = avatarOverallPick;
        AvatarTeamId = avatarTeamId;
        AvatarPlayerId = avatarPlayerId;
        AvatarWasAutomaticSenior = avatarWasAutomaticSenior;
    }
}

/// <summary>
/// A player's FIRST Pro contract (spec §9.4): every draft pick on Draft Day
/// (<see cref="WasDrafted"/> true) and every free agent signed at a Sunday
/// lock without a prior Pro contract (false), NPCs included. Phase L keys
/// the father's first text on the avatar's.
/// </summary>
public readonly struct ProContractSignedEvent : IGameEvent
{
    public readonly string PlayerId;
    public readonly int TeamId;
    public readonly long Day;
    public readonly bool WasDrafted;

    public ProContractSignedEvent(string playerId, int teamId, long day, bool wasDrafted)
    {
        PlayerId = playerId;
        TeamId = teamId;
        Day = day;
        WasDrafted = wasDrafted;
    }
}

/// <summary>
/// The avatar was released at the rollover (D4b-C): an undrafted College
/// senior, or a Pro avatar the release order cut. He leaves baseball
/// (unrostered, pipeline_retired). <see cref="EnteredParentPhase"/> is true
/// when he has children (the parent phase), false when the release ended the
/// game (NoHeirs). <see cref="Tier"/> is the LeagueTier value he left.
/// </summary>
public readonly struct AvatarReleasedEvent : IGameEvent
{
    public readonly string PlayerId;
    public readonly byte Tier;
    public readonly int SeasonYear;
    public readonly long Day;
    public readonly bool EnteredParentPhase;

    public AvatarReleasedEvent(string playerId, byte tier, int seasonYear, long day, bool enteredParentPhase)
    {
        PlayerId = playerId;
        Tier = tier;
        SeasonYear = seasonYear;
        Day = day;
        EnteredParentPhase = enteredParentPhase;
    }
}

/// <summary>
/// The avatar's draft-declaration window opened (day 176, a College player
/// with class year ≥ 2). <see cref="ProjectedRound"/> is 1–4, or 0 when he
/// projects undrafted; <see cref="IsAutomatic"/> is true for a class-4
/// senior, who is always in the pool. The deadline is day 183; the clock
/// doesn't stop. The phone's declaration text keys on it (13-6a build record
/// §11.3: the one event beyond the brief's five, because Narrative can't read
/// the Baseball calendar).
/// </summary>
public readonly struct DraftDeclarationOpenedEvent : IGameEvent
{
    public readonly string PlayerId;
    public readonly int SeasonYear;
    public readonly long Day;
    public readonly int ProjectedSlot;
    public readonly int ProjectedRound;
    public readonly bool IsAutomatic;

    public DraftDeclarationOpenedEvent(string playerId, int seasonYear, long day, int projectedSlot, int projectedRound, bool isAutomatic)
    {
        PlayerId = playerId;
        SeasonYear = seasonYear;
        Day = day;
        ProjectedSlot = projectedSlot;
        ProjectedRound = projectedRound;
        IsAutomatic = isAutomatic;
    }
}
