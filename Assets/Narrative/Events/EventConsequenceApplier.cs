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
    private readonly GrittyEventLibrary _library;
    private readonly RelationshipGraph _relationships;
    private readonly EventBus _bus;
    private readonly GameStateQueries _gameState;
    private readonly Action<GrittyEventFiredEvent> _onFired;

    private RngState _rng;

    // Target-pool scratch, reloaded per fire (fires are rare — a handful per
    // game day at most, bounded by EventDispatcher.MaxFiresPerDay).
    private readonly List<PlayerRow> _playerScratch = new(160);

    // Edge-walk scratch for the Partner lookups (exclusivity guard + the
    // conception request's co-parent resolution).
    private readonly List<RelationshipEdge> _edgeScratch = new(8);

    // Absence publications staged inside the DB batch (where the rust penalty
    // is computed against the same state the write saw), published after the
    // commit — the standard commit-then-publish ordering. Pooled; fires are
    // rare and the list is cleared per fire.
    private readonly List<PlayerAbsenceChangedEvent> _absenceScratch = new(4);

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
        DatabaseManager db, PlayerQueries players, GrittyEventLibrary library,
        RelationshipGraph relationships, EventBus bus, GameStateQueries gameState, ulong rngSeed)
    {
        _db = db;
        _players = players;
        _library = library;
        _relationships = relationships;
        _bus = bus;
        _gameState = gameState;
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

        bool isAvatar = _gameState.TryGetText(GameStateKeys.AvatarPlayerId, out string avatarId)
            && string.Equals(avatarId, fired.SubjectPlayerId, StringComparison.Ordinal);

        if (!AutopilotAvatarChoices && isAvatar)
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
        for (int i = 0; i < consequences.Length; i++)
        {
            needsTargets |= consequences[i].Kind == ConsequenceKind.Relationship;
        }
        if (needsTargets)
        {
            _players.LoadAll(_playerScratch);
        }

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
                }
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

        _bus.Publish(new GrittyEventResolvedEvent(fired.EventId, fired.SubjectPlayerId, choiceIndex, fired.Day));
    }

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

        // Two passes (count, then k-th) — no candidate list allocation.
        int candidates = 0;
        for (int i = 0; i < _playerScratch.Count; i++)
        {
            if (IsCandidate(_playerScratch[i], subjectId, subjectTeam, selector))
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
            if (IsCandidate(_playerScratch[i], subjectId, subjectTeam, selector) && pick-- == 0)
            {
                targetId = _playerScratch[i].PlayerId;
                return true;
            }
        }

        targetId = string.Empty;
        return false;
    }

    private static bool IsCandidate(in PlayerRow row, string subjectId, int? subjectTeam, RelationshipTargetSelector selector)
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
            _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, null),
        };
    }
}
