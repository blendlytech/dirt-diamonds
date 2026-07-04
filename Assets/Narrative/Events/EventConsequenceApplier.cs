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
/// </summary>
public sealed class EventConsequenceApplier
{
    private readonly DatabaseManager _db;
    private readonly PlayerQueries _players;
    private readonly GrittyEventLibrary _library;
    private readonly RelationshipGraph _relationships;
    private readonly EventBus _bus;
    private readonly Action<GrittyEventFiredEvent> _onFired;

    private RngState _rng;

    // Target-pool scratch, reloaded per fire (fires are rare — a handful per
    // game day at most, bounded by EventDispatcher.MaxFiresPerDay).
    private readonly List<PlayerRow> _playerScratch = new(160);

    public EventConsequenceApplier(
        DatabaseManager db, PlayerQueries players, GrittyEventLibrary library,
        RelationshipGraph relationships, EventBus bus, ulong rngSeed)
    {
        _db = db;
        _players = players;
        _library = library;
        _relationships = relationships;
        _bus = bus;
        _rng = new RngState(rngSeed | 1UL);
        _onFired = OnFired;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onFired);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onFired);

    private void OnFired(GrittyEventFiredEvent fired)
    {
        if (!_library.TryGetById(fired.EventId, out GrittyEventDefinition definition))
        {
            throw new InvalidOperationException(
                $"Gritty event '{fired.EventId}' fired but is not in the loaded library.");
        }

        int choiceIndex = PickChoice(definition.Choices);
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
                    case ConsequenceKind.Interest:
                        _players.AdjustBaseballInterest(fired.SubjectPlayerId, (int)Math.Round(consequence.Amount));
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
            }
        }

        _bus.Publish(new GrittyEventResolvedEvent(fired.EventId, fired.SubjectPlayerId, choiceIndex, fired.Day));
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
