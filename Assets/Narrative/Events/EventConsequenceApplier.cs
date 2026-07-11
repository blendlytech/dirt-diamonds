using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Baseball;
using DirtAndDiamonds.Simulation.Life;

namespace DirtAndDiamonds.Narrative.Events;

/// <summary>
/// The main-thread half of the Gritty Event pipeline
/// (gritty_event_framework.md §1/§4): consumes <see cref="GrittyEventFiredEvent"/>
/// off the bus pump, autopilot-resolves the branching choice, and applies the
/// consequences — database writes in its own batch (never the calendar
/// tick's, never another sim's), relationship writes straight into the
/// <see cref="RelationshipGraph"/> (in-memory; the graph publishes
/// RivalryChangedEvent and GameManager's day-cadence flush persists the
/// edge), and stress/funds deltas as bus impulses per the
/// life_sim_needs_decay.md §10 contract ("a gritty event raising stress
/// arrives via the EventBus, never a direct call").
///
/// Until the choice UI ships every fire is autopilot-resolved (the
/// AutopilotAttendedGames precedent); <see cref="GrittyEventResolvedEvent"/>
/// is the seam the future UI/feed renders from.
///
/// The avatar's own fires are the one exception once <see cref="AutopilotAvatarChoices"/>
/// is turned off (GameManager does this — headless/harness callers keep the
/// default true): instead of resolving inline, the fire parks as a
/// <see cref="PendingGrittyChoice"/> (see <see cref="TryGetPendingChoice"/>)
/// for the event-choice UI to read and answer via <see cref="ResolveChoice"/>.
/// A still-unresolved pending choice is forfeited to the autopilot draw
/// (never thrown away) the moment a new fire needs the slot — safe because
/// consequence application has no eligibility preconditions that can fail
/// from staleness (unlike CareerManager.Succeed).
/// </summary>
public readonly struct PendingGrittyChoice
{
    public readonly GrittyEventFiredEvent Fired;
    public readonly GrittyEventDefinition Definition;

    public PendingGrittyChoice(GrittyEventFiredEvent fired, GrittyEventDefinition definition)
    {
        Fired = fired;
        Definition = definition;
    }
}

public sealed class EventConsequenceApplier
{
    /// <summary>
    /// Injury-rust ceiling (Phase 8c): the effective-rating deduction a
    /// completely broken-down player (health_ceiling 0) carries through the
    /// post-return rust window. Scales linearly down to 0 at health 100, so a
    /// healthy player's injury heals clean. Same magnitude class as the
    /// rivalry ±16 / tier −20..0 modifiers; a calibration constant, tunable
    /// behind run_monte_carlo_batch like every effective-ratings knob.
    /// </summary>
    public const int InjuryRustMaxPenalty = 12;

    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly PersonQueries _persons;
    private readonly GrittyEventLibrary _library;
    private readonly RelationshipGraph _relationships;
    private readonly EventBus _bus;
    private readonly GameStateQueries _gameState;
    private readonly GlobalState _state;
    private readonly NarrativeLogQueries _narrativeLog;
    private readonly Action<GrittyEventFiredEvent> _onFired;

    private RngState _rng;

    // Target-pool scratch, reloaded per fire (fires are rare — a handful per
    // game day at most, bounded by EventDispatcher.MaxFiresPerDay).
    private readonly List<PlayerRow> _playerScratch = new(160);

    // Edge-walk scratch for the Partner lookups (exclusivity guard + the
    // conception request's co-parent resolution).
    private readonly List<RelationshipEdge> _edgeScratch = new(8);

    // Child-edge scratch for the child_development consequence — resolved
    // from the DATABASE, not the live graph: Child edges are DB-authored
    // (ConceiveChild's own committed batch) and never written through
    // RelationshipGraph, so a mid-session birth exists only in the DB until
    // the next boot hydration — the exact inverse of the Partner polarity.
    // Filled by PlayerQueries.LoadChildrenOf, which owns the Child-edge/age-
    // guard filtering (extracted here from this method for ChildRearingService
    // to share, HS-5 §7.1's weekly tick).
    private readonly List<PlayerRow> _childRowScratch = new(8);

    // Absence publications staged inside the DB batch (where the rust penalty
    // is computed against the same state the write saw), published after the
    // commit — the standard commit-then-publish ordering. Pooled; fires are
    // rare and the list is cleared per fire.
    private readonly List<PlayerAbsenceChangedEvent> _absenceScratch = new(4);

    // PersonStat ordinal touch table (the PersonStatImpulseEvent gap fix):
    // tracks which of the 12 PersonStatId ordinals a PersonStat consequence
    // hit this fire, so a stat nudged twice in one choice (e.g. +5 then −3)
    // nets to a single post-batch publish instead of two. Pooled; every
    // ApplyChoice call clears exactly the flags it set.
    private readonly bool[] _touchedPersonStatOrdinals = new bool[PersonQueries.AdjustableStatCount];

    /// <summary>
    /// True (default, headless-safe): every fire — including the avatar's —
    /// resolves immediately via the weighted autopilot draw. False: an
    /// avatar-subject fire pauses as <see cref="PendingChoice"/> instead.
    /// </summary>
    public bool AutopilotAvatarChoices = true;

    private PendingGrittyChoice _pending;
    private bool _hasPending;

    public bool HasPendingChoice => _hasPending;

    public EventConsequenceApplier(
        DatabaseManager db, PlayerQueries players, PersonQueries persons, GrittyEventLibrary library,
        RelationshipGraph relationships, EventBus bus, GameStateQueries gameState,
        GlobalState state, NarrativeLogQueries narrativeLog, ulong rngSeed)
    {
        _db = db;
        _players = players;
        _persons = persons;
        _library = library;
        _relationships = relationships;
        _bus = bus;
        _gameState = gameState;
        _state = state;
        _narrativeLog = narrativeLog;
        _rng = new RngState(rngSeed | 1UL);
        _onFired = OnFired;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onFired);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onFired);

    /// <summary>The avatar-subject fire awaiting a player choice, when <see cref="HasPendingChoice"/>.</summary>
    public bool TryGetPendingChoice(out PendingGrittyChoice pending)
    {
        pending = _pending;
        return _hasPending;
    }

    /// <summary>The event-choice UI's answer: applies exactly like an autopilot resolution, just with a player-picked index.</summary>
    public void ResolveChoice(int choiceIndex)
    {
        if (!_hasPending)
        {
            throw new InvalidOperationException("No gritty-event choice is pending.");
        }
        if (choiceIndex < 0 || choiceIndex >= _pending.Definition.Choices.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(choiceIndex), choiceIndex, "Not a valid choice index for the pending event.");
        }
        GrittyEventFiredEvent fired = _pending.Fired;
        GrittyEventDefinition definition = _pending.Definition;
        _hasPending = false;
        ApplyChoice(fired, definition, choiceIndex);
    }

    private void OnFired(GrittyEventFiredEvent fired)
    {
        if (!_library.TryGetById(fired.EventId, out GrittyEventDefinition definition))
        {
            throw new InvalidOperationException(
                $"Gritty event '{fired.EventId}' fired but is not in the loaded library.");
        }

        if (!AutopilotAvatarChoices && IsAvatarSubject(fired.SubjectPlayerId))
        {
            if (_hasPending)
            {
                // A previous avatar choice was never answered — forfeit it to
                // the autopilot draw rather than lose or overwrite it silently.
                GrittyEventFiredEvent staleFired = _pending.Fired;
                GrittyEventDefinition staleDefinition = _pending.Definition;
                ApplyChoice(staleFired, staleDefinition, PickChoice(staleDefinition.Choices));
            }
            _pending = new PendingGrittyChoice(fired, definition);
            _hasPending = true;
            return;
        }

        ApplyChoice(fired, definition, PickChoice(definition.Choices));
    }

    /// <summary>
    /// Applies one fired event's chosen branch: DB consequences in their own
    /// batch, then bus impulses/relationship writes, then the resolved-event
    /// publish. Shared by the autopilot path (<see cref="OnFired"/>) and the
    /// UI's <see cref="ResolveChoice"/> — identical either way, only the
    /// choice-picking differs.
    /// </summary>
    private void ApplyChoice(GrittyEventFiredEvent fired, GrittyEventDefinition definition, int choiceIndex)
    {
        EventConsequence[] consequences = definition.Choices[choiceIndex].Consequences;

        // Relationship targets resolve before any write so an empty pool
        // (consequence skipped, §4) is decided on a consistent snapshot.
        bool needsTargets = false;
        bool endsPartnership = false;
        bool needsPersonSnapshot = false;
        for (int i = 0; i < consequences.Length; i++)
        {
            needsTargets |= consequences[i].Kind == ConsequenceKind.Relationship;
            endsPartnership |= consequences[i].Kind == ConsequenceKind.EndPartnership;
            needsPersonSnapshot |= consequences[i].Kind == ConsequenceKind.PersonStat;
        }
        if (needsTargets)
        {
            _players.LoadAll(_playerScratch);
        }

        // The ending partner resolves once, pre-batch, off the same live
        // graph the reclassification will hit — so the ex-history record
        // (below, inside the batch) and the graph write can never disagree.
        string? endedPartnerId = endsPartnership ? FindLivePartnerId(fired.SubjectPlayerId) : null;

        // Pre-batch person-stat snapshot (the ApplySelfBuyTransportReward
        // precedent): the ONLY way to learn AdjustStat's actual clamped
        // movement is to read the row on both sides of the write, since the
        // clamp happens in SQL. A missing row (dead in practice post-v11
        // boot backfill) leaves hasPersonBefore false, and the post-batch
        // side skips publishing rather than guess.
        PersonRow personBefore = default;
        bool hasPersonBefore = needsPersonSnapshot
            && _persons.TryGet(fired.SubjectPlayerId, out personBefore);

        // Database consequences commit atomically in this handler's own batch.
        _absenceScratch.Clear();
        _db.RunInBatch(() =>
        {
            for (int i = 0; i < consequences.Length; i++)
            {
                ref readonly EventConsequence consequence = ref consequences[i];
                switch (consequence.Kind)
                {
                    case ConsequenceKind.Funds:
                        _players.AdjustFunds(fired.SubjectPlayerId, consequence.Amount);
                        break;
                    case ConsequenceKind.Absence:
                        ApplyAbsence(fired, in consequence);
                        break;
                    case ConsequenceKind.Interest:
                        _players.AdjustBaseballInterest(fired.SubjectPlayerId, (int)Math.Round(consequence.Amount));
                        break;
                    case ConsequenceKind.DetectionRisk:
                        _players.AdjustDetectionRisk(fired.SubjectPlayerId, (int)Math.Round(consequence.Amount));
                        break;
                    case ConsequenceKind.HealthCeiling:
                        _players.AdjustHealthCeiling(fired.SubjectPlayerId, (int)Math.Round(consequence.Amount));
                        break;
                    case ConsequenceKind.SetFlag:
                        _players.SetFlag(fired.SubjectPlayerId, consequence.FlagName!, true, fired.Day);
                        break;
                    case ConsequenceKind.ClearFlag:
                        _players.SetFlag(fired.SubjectPlayerId, consequence.FlagName!, false, fired.Day);
                        break;
                    case ConsequenceKind.PersonStat:
                        _persons.AdjustStat(
                            fired.SubjectPlayerId, (int)consequence.PersonStat, (int)Math.Round(consequence.Amount));
                        _touchedPersonStatOrdinals[(int)consequence.PersonStat] = true;
                        break;
                    case ConsequenceKind.ChildDevelopment:
                        ApplyChildDevelopment(fired, in consequence);
                        break;
                }
            }

            // Ex-history record (HS-5 rekindle substrate): an avatar breakup
            // remembers WHO inside the same batch as the fire's other writes
            // (the trait-flag-in-batch discipline). Only the avatar's romance
            // history is tracked — a non-avatar end_partnership reclassifies
            // its edge exactly as before and records nothing.
            if (endedPartnerId is not null && IsAvatarSubject(fired.SubjectPlayerId))
            {
                _gameState.SetText(GameStateKeys.AvatarExPartnerId, endedPartnerId);
            }

            // The Burner Phone read-model (presentation_layer_narrative.md
            // §4.3): one row per resolved choice, atomic with the rest of
            // this fire's writes. Every path funnels through ApplyChoice —
            // autopilot, the UI's ResolveChoice, and a forfeited stale
            // pending choice all land here — so the thread reflects what
            // actually happened, including choices the player never saw.
            // fired.Day (not "now") computes the season year: a forfeited
            // fire carries its ORIGINAL day, which can lag the live calendar.
            EventChoice pickedChoice = definition.Choices[choiceIndex];
            _narrativeLog.Insert(
                _state.SeasonYearForDay(fired.Day), fired.Day, fired.SubjectPlayerId,
                definition.ContactId, definition.Prompt, pickedChoice.Label, choiceIndex, pickedChoice.Outcome);

            // Phone-split spec §1: the event's own fire-time companion text
            // (Mom's "Knock 'em dead today, kiddo.") lands with the FIRE day
            // regardless of which choice resolved it — same forfeit precedent
            // as the event row above.
            if (definition.TextMessage is not null)
            {
                _narrativeLog.InsertText(
                    _state.SeasonYearForDay(fired.Day), fired.Day, fired.SubjectPlayerId,
                    definition.ContactId, definition.TextMessage);
            }

            // Amendment §3: the choice's delayed REACTION text, dated
            // resolution day + delay_days — "resolution day" is "today" as of
            // THIS batch (GlobalState.CurrentDay), the day consequences
            // actually land. For an immediate autopilot resolution that's the
            // same as fired.Day; for a pending choice the player answers
            // later, or a forfeit triggered by a later fire, it's the later
            // day — "texts follow the decision," not the original fire.
            if (pickedChoice.TextMessageBody is not null)
            {
                long deliveryDay = _state.CurrentDay + pickedChoice.TextMessageDelayDays;
                _narrativeLog.InsertText(
                    _state.SeasonYearForDay(deliveryDay), deliveryDay, fired.SubjectPlayerId,
                    definition.ContactId, pickedChoice.TextMessageBody);
            }
        });

        // In-memory / bus-routed consequences follow the committed batch, so a
        // subscriber reacting to an impulse always observes the DB state the
        // same fire produced.
        for (int i = 0; i < consequences.Length; i++)
        {
            ref readonly EventConsequence consequence = ref consequences[i];
            switch (consequence.Kind)
            {
                case ConsequenceKind.Funds:
                    if (consequence.Amount != 0)
                    {
                        _bus.Publish(new FundsImpulseEvent(fired.SubjectPlayerId, consequence.Amount));
                    }
                    break;
                case ConsequenceKind.Stress:
                    if (consequence.Amount != 0)
                    {
                        _bus.Publish(new StressImpulseEvent(fired.SubjectPlayerId, (float)consequence.Amount));
                    }
                    break;
                case ConsequenceKind.Relationship:
                    ApplyRelationship(fired.SubjectPlayerId, in consequence);
                    break;
                case ConsequenceKind.EndPartnership:
                    ApplyEndPartnership(fired.SubjectPlayerId, in consequence, endedPartnerId);
                    break;
                case ConsequenceKind.RekindlePartnership:
                    ApplyRekindle(fired.SubjectPlayerId, in consequence);
                    break;
                case ConsequenceKind.ConceiveChild:
                    // The load-time gate (§4.1) restricts this consequence to
                    // scope-avatar events, so the subject IS the avatar. The
                    // co-parent comes from the LIVE graph — the same object
                    // that authored any same-session marriage — never the DB,
                    // whose Partner edge may still await the day-cadence
                    // flush (§4.2). CareerManager services the request on a
                    // later pump, outside this handler's committed batch.
                    _bus.Publish(new ChildConceptionRequestedEvent(
                        fired.SubjectPlayerId, FindLivePartnerId(fired.SubjectPlayerId), fired.Day));
                    break;
            }
        }

        // Absence publications follow the committed batch like every other
        // impulse, so the AvailabilityLedger only ever mirrors persisted rows.
        for (int i = 0; i < _absenceScratch.Count; i++)
        {
            _bus.Publish(_absenceScratch[i]);
        }
        _absenceScratch.Clear();

        // Person-stat impulses (closes the gritty→PersonStatImpulseEvent gap):
        // every ordinal a PersonStat consequence touched publishes its ACTUAL
        // clamped movement — before/after read around the committed batch,
        // since AdjustStat clamps in SQL and only the row knows what really
        // moved (the ItemService.ApplySelfBuyTransportReward precedent). This
        // is the FundsImpulseEvent pattern, and it is what LifeSimManager's
        // GPA-drift inputs (Intelligence/Discipline) and happiness reversion
        // were missing — a gritty event nudging those never reached the Life
        // sim's in-memory mirror before. Every set flag is cleared here
        // whether or not a snapshot was available, so no fire ever leaks a
        // stale touch into the next one.
        PersonRow personAfter = default;
        bool hasPersonAfter = needsPersonSnapshot
            && _persons.TryGet(fired.SubjectPlayerId, out personAfter);
        bool simLeversMoved = false;
        for (int ordinal = 0; ordinal < PersonQueries.AdjustableStatCount; ordinal++)
        {
            if (!_touchedPersonStatOrdinals[ordinal])
            {
                continue;
            }
            _touchedPersonStatOrdinals[ordinal] = false;
            if (!hasPersonBefore || !hasPersonAfter)
            {
                continue;
            }
            int delta = GetStatByOrdinal(in personAfter, ordinal) - GetStatByOrdinal(in personBefore, ordinal);
            if (delta == 0)
            {
                continue;
            }
            _bus.Publish(new PersonStatImpulseEvent(fired.SubjectPlayerId, ordinal, delta));
            simLeversMoved |= ordinal is (int)PersonStatId.Happiness
                or (int)PersonStatId.Confidence or (int)PersonStatId.Teamwork;
        }

        // HS-6 follow-up (per-game person refresh): the §6.2 sim-lever trio
        // additionally re-announces the subject's COMMITTED lever values for
        // the attended-game PersonLedger — absolutes, not deltas, same as
        // before this fix. Now driven by the actual-movement check above
        // rather than the nominal nudge, so a consequence that couldn't
        // actually move (already at the cap) no longer over-publishes.
        if (simLeversMoved)
        {
            _bus.Publish(new PersonLeversChangedEvent(
                fired.SubjectPlayerId,
                (byte)personAfter.Happiness, (byte)personAfter.Confidence, (byte)personAfter.Teamwork));
        }

        _bus.Publish(new GrittyEventResolvedEvent(fired.EventId, fired.SubjectPlayerId, choiceIndex, fired.Day));
    }

    /// <summary>PersonRow field lookup by PersonStatId ordinal — mirrors PersonQueries.AdjustStatColumns' order exactly (SchemaValidator-pinned).</summary>
    private static int GetStatByOrdinal(in PersonRow person, int ordinal) => ordinal switch
    {
        0 => person.Intelligence,
        1 => person.Maturity,
        2 => person.Happiness,
        3 => person.Charisma,
        4 => person.Confidence,
        5 => person.Reputation,
        6 => person.SocialStatus,
        7 => person.Attractiveness,
        8 => person.Teamwork,
        9 => person.Morality,
        10 => person.Discipline,
        11 => person.WorkEthic,
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, null),
    };

    /// <summary>
    /// The Phase 8c roster/availability mutation. An event fires during day
    /// D's pump — AFTER day D's games have played — so "benched for N days"
    /// covers days D+1 through D+N (until_day = D+N+1, exclusive): the
    /// subject misses exactly N game days. Injuries additionally compute the
    /// post-return rust penalty from the subject's health_ceiling AS OF THIS
    /// BATCH — after any HealthCeiling consequence earlier in the same
    /// choice, so "lose 20 health AND get hurt" prices the rust off the
    /// damaged body — with the rust window as long as the absence itself
    /// (you rehab as long as you sat). Writes through the keep-later upsert
    /// (a shorter overlapping absence is a DB no-op; the ledger applies the
    /// identical rule to the staged publication, so mirror and row can never
    /// disagree).
    /// </summary>
    private void ApplyAbsence(GrittyEventFiredEvent fired, in EventConsequence consequence)
    {
        int days = (int)consequence.Amount;
        long untilDay = fired.Day + days + 1;

        int penalty = 0;
        long penaltyUntilDay = 0;
        if (consequence.AbsenceReason == AbsenceReason.Injury)
        {
            if (!_players.TryGetById(fired.SubjectPlayerId, out PlayerRow subject))
            {
                throw new InvalidOperationException(
                    $"Absence consequence fired for '{fired.SubjectPlayerId}' but the Players row is missing.");
            }
            // Round-half-up of max·(100−health)/100, the RivalryEffects idiom.
            penalty = (InjuryRustMaxPenalty * (100 - subject.HealthCeiling) + 50) / 100;
            penaltyUntilDay = penalty > 0 ? untilDay + days : 0;
        }

        _players.SetAbsence(fired.SubjectPlayerId, consequence.AbsenceReason, untilDay, penalty, penaltyUntilDay);
        _absenceScratch.Add(new PlayerAbsenceChangedEvent(
            fired.SubjectPlayerId, (byte)consequence.AbsenceReason, untilDay, (byte)penalty, penaltyUntilDay));
    }

    /// <summary>
    /// HS-5 rearing (§7.1): one clamped axis delta for EVERY child of the
    /// subject — each Child edge whose other endpoint is YOUNGER (the §1.2
    /// age invariant excludes the subject's own parents, whose edges share
    /// the same kind). Runs inside the fire's DB batch like every other row
    /// write; a childless subject touches nothing (the empty-pool
    /// precedent). Applying to all children rather than drawing one keeps
    /// the consequence deterministic — no RNG consumed — and reads as a
    /// family beat; per-child targeting would be an additive selector field,
    /// not a reshape.
    /// </summary>
    private void ApplyChildDevelopment(GrittyEventFiredEvent fired, in EventConsequence consequence)
    {
        if (!_players.TryGetById(fired.SubjectPlayerId, out _))
        {
            throw new InvalidOperationException(
                $"child_development consequence fired for '{fired.SubjectPlayerId}' but the Players row is missing.");
        }
        _players.LoadChildrenOf(fired.SubjectPlayerId, _childRowScratch);
        int delta = (int)Math.Round(consequence.Amount);
        for (int i = 0; i < _childRowScratch.Count; i++)
        {
            _persons.AdjustChildAxis(_childRowScratch[i].PlayerId, (int)consequence.ChildAxis, delta, fired.Day);
        }
    }

    /// <summary>Weighted draw over autopilot_weight; an all-zero table falls back to the first choice.</summary>
    private int PickChoice(EventChoice[] choices)
    {
        int totalWeight = 0;
        for (int i = 0; i < choices.Length; i++)
        {
            totalWeight += choices[i].AutopilotWeight;
        }
        if (totalWeight <= 0)
        {
            return 0;
        }

        int roll = _rng.NextInt(totalWeight);
        for (int i = 0; i < choices.Length; i++)
        {
            roll -= choices[i].AutopilotWeight;
            if (roll < 0)
            {
                return i;
            }
        }
        return choices.Length - 1;
    }

    /// <summary>
    /// §4 relationship semantics: no existing edge → create it with the
    /// authored kind and affinity; an existing edge → adjust affinity only
    /// (a feud deepens; it doesn't reclassify a marriage). A Rival edge pushed
    /// negative rides the untouched Phase-6 transport into both baseball sims.
    /// </summary>
    private void ApplyRelationship(string subjectId, in EventConsequence consequence)
    {
        // Single-partner exclusivity (marriage_and_conception.md §3): an
        // already-partnered subject skips a new Partner consequence outright
        // (same precedent as an empty pool), so FindPartnerId stays
        // unambiguous and a repeat/scope-any fire can never accrete spouses.
        if (consequence.RelationshipKind == RelationshipKind.Partner
            && FindLivePartnerId(subjectId) is not null)
        {
            return;
        }

        if (!TryPickTarget(subjectId, consequence.Target, out string targetId))
        {
            return; // empty pool — consequence skipped by design
        }

        int affinity = (int)Math.Round(consequence.Amount);
        if (_relationships.TryGetRelationship(subjectId, targetId, out _, out _))
        {
            _relationships.AdjustAffinity(subjectId, targetId, affinity);
        }
        else
        {
            _relationships.SetRelationship(subjectId, targetId, affinity, consequence.RelationshipKind);
        }
    }

    /// <summary>
    /// HS-5 breakup/divorce: reclassifies the subject's live Partner edge to
    /// the authored kind/affinity via SetRelationship's overwrite path — the
    /// edge survives as ex-history (never deleted), the dirty flush upserts
    /// the kind change through the existing daily cadence, and a Rival
    /// reclassification into negative affinity rides the untouched Phase-6
    /// rivalry transport into both baseball sims (bitter exes are real).
    /// Unpartnered subjects skip (the empty-pool precedent), so the same
    /// choice is safe whether or not the romance arc actually ran. Consequences
    /// apply in authored order, so a choice may end one partnership and mint a
    /// rebound Partner in the same breath — end_partnership first.
    /// <paramref name="partnerId"/> arrives pre-resolved from ApplyChoice's
    /// pre-batch pass, where the same value fed the in-batch ex-history record
    /// — the two writes agree by construction.
    /// </summary>
    private void ApplyEndPartnership(string subjectId, in EventConsequence consequence, string? partnerId)
    {
        if (partnerId is null)
        {
            return;
        }
        _relationships.SetRelationship(
            subjectId, partnerId, (int)Math.Round(consequence.Amount), consequence.RelationshipKind);
    }

    /// <summary>
    /// HS-5 "getting back together": re-mints the Partner edge with the
    /// recorded most-recent ex (Game_State avatar_ex_partner_id — the
    /// load-time scope gate makes the subject the avatar). This is the
    /// deliberate counterpart to ApplyRelationship's adjust-don't-reclassify
    /// rule: that rule stops a feud consequence reclassifying a marriage, and
    /// it equally stopped a random-pool Partner consequence re-partnering an
    /// ex — so rekindling gets its own targeted write instead of a loophole.
    /// Skip-by-design (the empty-pool precedent) when already partnered
    /// (exclusivity guard), no ex was ever recorded, or the recorded ex no
    /// longer has a Friend/Rival edge to the subject (a stale id — e.g. one
    /// inherited across a succession handoff — can never mis-mint). The
    /// SetRelationship overwrite path dissolves a bitter-ex rivalry to
    /// intensity 0 through the untouched Phase-6 transport.
    /// </summary>
    private void ApplyRekindle(string subjectId, in EventConsequence consequence)
    {
        if (FindLivePartnerId(subjectId) is not null)
        {
            return;
        }
        if (!_gameState.TryGetText(GameStateKeys.AvatarExPartnerId, out string exId)
            || string.IsNullOrEmpty(exId))
        {
            return;
        }
        if (!_relationships.TryGetRelationship(subjectId, exId, out _, out RelationshipKind kind)
            || kind is not (RelationshipKind.Friend or RelationshipKind.Rival))
        {
            return;
        }
        _relationships.SetRelationship(
            subjectId, exId, (int)Math.Round(consequence.Amount), RelationshipKind.Partner);
    }

    /// <summary>Whether this subject is the current avatar (Game_State avatar_player_id) — the OnFired pause check and the ex-history record share this.</summary>
    private bool IsAvatarSubject(string subjectId) =>
        _gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string avatarId)
        && string.Equals(avatarId, subjectId, StringComparison.Ordinal);

    /// <summary>The subject's Partner counterpart in the in-memory graph, or null. At most one exists (the exclusivity guard); the first found is returned.</summary>
    private string? FindLivePartnerId(string subjectId)
    {
        _relationships.GetEdgesFor(subjectId, _edgeScratch);
        for (int i = 0; i < _edgeScratch.Count; i++)
        {
            if (_edgeScratch[i].Kind == RelationshipKind.Partner)
            {
                return _edgeScratch[i].OtherId;
            }
        }
        return null;
    }

    private bool TryPickTarget(string subjectId, RelationshipTargetSelector selector, out string targetId)
    {
        int? subjectTeam = null;
        for (int i = 0; i < _playerScratch.Count; i++)
        {
            if (string.Equals(_playerScratch[i].PlayerId, subjectId, StringComparison.Ordinal))
            {
                subjectTeam = _playerScratch[i].TeamId;
                break;
            }
        }

        // TeammateExOfPartner resolves the subject's current partner ONCE
        // here — an unpartnered subject has an empty pool by construction,
        // the same skip-by-design precedent as every other empty candidate
        // set (this can legitimately race a poll-time prerequisite that saw
        // a partner a day ago; see the selector's doc comment).
        string? partnerId = null;
        if (selector == RelationshipTargetSelector.TeammateExOfPartner)
        {
            partnerId = FindLivePartnerId(subjectId);
            if (partnerId is null)
            {
                targetId = string.Empty;
                return false;
            }
        }

        // Two passes (count, then k-th) — no candidate list allocation.
        int candidates = 0;
        for (int i = 0; i < _playerScratch.Count; i++)
        {
            if (IsCandidate(_playerScratch[i], subjectId, subjectTeam, selector, partnerId))
            {
                candidates++;
            }
        }
        if (candidates == 0)
        {
            targetId = string.Empty;
            return false;
        }

        int pick = _rng.NextInt(candidates);
        for (int i = 0; i < _playerScratch.Count; i++)
        {
            if (IsCandidate(_playerScratch[i], subjectId, subjectTeam, selector, partnerId) && pick-- == 0)
            {
                targetId = _playerScratch[i].PlayerId;
                return true;
            }
        }

        targetId = string.Empty;
        return false;
    }

    private bool IsCandidate(in PlayerRow row, string subjectId, int? subjectTeam, RelationshipTargetSelector selector, string? partnerId)
    {
        if (string.Equals(row.PlayerId, subjectId, StringComparison.Ordinal))
        {
            return false;
        }
        return selector switch
        {
            RelationshipTargetSelector.Teammate => row.TeamId is not null && row.TeamId == subjectTeam,
            // An unrostered subject (heir, free agent) has no "own" team; any
            // rostered player counts as an opponent for them.
            RelationshipTargetSelector.Opponent => row.TeamId is not null && row.TeamId != subjectTeam,
            RelationshipTargetSelector.League => true,
            // The real ex-teammate: same team as the subject, not the partner
            // themselves, and a Relationship_History hit against the partner
            // (schema v13) — never a random teammate pool draw.
            RelationshipTargetSelector.TeammateExOfPartner =>
                row.TeamId is not null && row.TeamId == subjectTeam
                && !string.Equals(row.PlayerId, partnerId, StringComparison.Ordinal)
                && _relationships.WasEverPartner(row.PlayerId, partnerId!),
            _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, null),
        };
    }
}
