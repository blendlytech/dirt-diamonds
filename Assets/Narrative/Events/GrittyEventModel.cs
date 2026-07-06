using System;
using System.Collections.Generic;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Life;

namespace DirtAndDiamonds.Narrative.Events;

/// <summary>Who an event definition may pick as its subject.</summary>
public enum EventScope : byte
{
    /// <summary>Every player row is a candidate subject.</summary>
    Any,

    /// <summary>Only the current avatar (Game_State avatar_player_id).</summary>
    Avatar,
}

public enum PrerequisiteKind : byte
{
    Field,
    FlagActive,
    FlagInactive,
}

/// <summary>Players-row fields a prerequisite may compare (gritty_event_framework.md §3).</summary>
public enum SubjectField : byte
{
    Funds,
    Age,
    Recklessness,
    HealthCeiling,
    DetectionRisk,
    BaseballInterest,
}

public enum FieldComparison : byte
{
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Equal,
    NotEqual,
}

/// <summary>
/// One prerequisite leaf; an event's prerequisites all AND together (§3 — OR
/// is two events). Struct so the per-day evaluation sweep reads a flat array.
/// </summary>
public readonly struct EventPrerequisite
{
    public readonly PrerequisiteKind Kind;

    // Field form:
    public readonly SubjectField Field;
    public readonly FieldComparison Comparison;
    public readonly double Value;

    // Flag forms:
    public readonly string? FlagName;

    /// <summary>FlagActive only: additionally require current_day − set_on_day ≥ this (the cascade mechanism).</summary>
    public readonly int MinDaysSince;

    private EventPrerequisite(
        PrerequisiteKind kind, SubjectField field, FieldComparison comparison,
        double value, string? flagName, int minDaysSince)
    {
        Kind = kind;
        Field = field;
        Comparison = comparison;
        Value = value;
        FlagName = flagName;
        MinDaysSince = minDaysSince;
    }

    public static EventPrerequisite ForField(SubjectField field, FieldComparison comparison, double value) =>
        new(PrerequisiteKind.Field, field, comparison, value, null, 0);

    public static EventPrerequisite ForFlagActive(string flagName, int minDaysSince = 0) =>
        new(PrerequisiteKind.FlagActive, default, default, 0, flagName, minDaysSince);

    public static EventPrerequisite ForFlagInactive(string flagName) =>
        new(PrerequisiteKind.FlagInactive, default, default, 0, flagName, 0);
}

/// <summary>The closed consequence vocabulary (§4). Unknown types are load-time errors.</summary>
public enum ConsequenceKind : byte
{
    Funds,
    Stress,
    Interest,
    SetFlag,
    ClearFlag,
    Relationship,

    /// <summary>
    /// Requests an heir off the avatar via the bus (marriage_and_conception.md
    /// §4). Payload-free; valid only on scope-avatar events (load-time gate).
    /// </summary>
    ConceiveChild,

    /// <summary>
    /// Atomic clamped detection_risk delta (hustles_narcotics_fencing.md §5) —
    /// the same writer 8b's Hustles introduce, extended into the gritty-event
    /// vocabulary here so 8c's content can reuse a tested consequence type.
    /// </summary>
    DetectionRisk,

    /// <summary>Atomic clamped health_ceiling delta — see <see cref="DetectionRisk"/>.</summary>
    HealthCeiling,

    /// <summary>
    /// The roster/availability mutation (Phase 8c — the type the framework's
    /// §4 deferral note reserved): benches the subject for N days via
    /// Player_Absences + the AvailabilityLedger transport. Injury absences
    /// additionally compute a post-return rust penalty from the subject's
    /// live health_ceiling at apply time.
    /// </summary>
    Absence,
}

/// <summary>Who a relationship consequence pairs the subject with (§4), resolved at apply time.</summary>
public enum RelationshipTargetSelector : byte
{
    Teammate,
    Opponent,
    League,
}

public readonly struct EventConsequence
{
    public readonly ConsequenceKind Kind;

    /// <summary>Funds/Stress/Interest delta, or the Relationship affinity value/delta. Signed.</summary>
    public readonly double Amount;

    public readonly string? FlagName;

    // Relationship form:
    public readonly RelationshipKind RelationshipKind;
    public readonly RelationshipTargetSelector Target;

    // Absence form (Amount carries the day count):
    public readonly AbsenceReason AbsenceReason;

    private EventConsequence(
        ConsequenceKind kind, double amount, string? flagName,
        RelationshipKind relationshipKind, RelationshipTargetSelector target,
        AbsenceReason absenceReason = AbsenceReason.None)
    {
        Kind = kind;
        Amount = amount;
        FlagName = flagName;
        RelationshipKind = relationshipKind;
        Target = target;
        AbsenceReason = absenceReason;
    }

    public static EventConsequence ForAmount(ConsequenceKind kind, double amount)
    {
        if (kind is not (ConsequenceKind.Funds or ConsequenceKind.Stress or ConsequenceKind.Interest
            or ConsequenceKind.DetectionRisk or ConsequenceKind.HealthCeiling))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not an amount-shaped consequence.");
        }
        return new EventConsequence(kind, amount, null, default, default);
    }

    public static EventConsequence ForFlag(ConsequenceKind kind, string flagName)
    {
        if (kind is not (ConsequenceKind.SetFlag or ConsequenceKind.ClearFlag))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a flag-shaped consequence.");
        }
        return new EventConsequence(kind, 0, flagName, default, default);
    }

    public static EventConsequence ForRelationship(
        RelationshipKind kind, double affinity, RelationshipTargetSelector target) =>
        new(ConsequenceKind.Relationship, affinity, null, kind, target);

    /// <summary>Payload-free by design (§4) — twins/birth-age/co-parent selection would be additive fields, not a reshape.</summary>
    public static EventConsequence ForConceiveChild() =>
        new(ConsequenceKind.ConceiveChild, 0, null, default, default);

    /// <summary>Benches the subject for <paramref name="days"/> days (Phase 8c). Amount carries the day count.</summary>
    public static EventConsequence ForAbsence(AbsenceReason reason, int days)
    {
        if (reason == AbsenceReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "An absence needs a real reason.");
        }
        if (days < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, "An absence must last at least one day.");
        }
        return new EventConsequence(ConsequenceKind.Absence, days, null, default, default, reason);
    }
}

public sealed class EventChoice
{
    public readonly string Id;

    /// <summary>Relative weight for autopilot resolution until the choice UI exists (≥0).</summary>
    public readonly int AutopilotWeight;

    /// <summary>Player-facing button text for the event-choice UI. Content-authored, or a humanized fallback of <see cref="Id"/> (GrittyEventJson.Humanize) when the batch omits "label".</summary>
    public readonly string Label;

    public readonly EventConsequence[] Consequences;

    public EventChoice(string id, int autopilotWeight, string label, EventConsequence[] consequences)
    {
        Id = id;
        AutopilotWeight = autopilotWeight;
        Label = label;
        Consequences = consequences;
    }
}

public sealed class GrittyEventDefinition
{
    public readonly string Id;
    public readonly EventScope Scope;

    /// <summary>Per-day fire probability once prerequisites hold, [0,1].</summary>
    public readonly double Weight;

    /// <summary>Min days between fires for the same subject (in-memory pacing; 0 = none).</summary>
    public readonly int CooldownDays;

    /// <summary>Player-facing flavor text for the event-choice UI. Content-authored, or a humanized fallback of <see cref="Id"/> (GrittyEventJson.Humanize) when the batch omits "prompt".</summary>
    public readonly string Prompt;

    /// <summary>
    /// The Burner Phone thread this event's fires post to (presentation_layer_narrative.md
    /// §4.2) — additive, content-authored via the JSON "contact" field. A batch that omits
    /// it resolves to the reserved "unknown" ("Unknown Number") thread, never a crash or a
    /// silent drop; the id is a Narrative.Contacts.ContactRegistry key, resolved at render
    /// time, never validated against the registry at load time (the two content files load
    /// independently — check_event_graph_integrity is the authoring-time check).
    /// </summary>
    public readonly string ContactId;

    public readonly EventPrerequisite[] Prerequisites;
    public readonly EventChoice[] Choices;

    public GrittyEventDefinition(
        string id, EventScope scope, double weight, int cooldownDays, string prompt, string contactId,
        EventPrerequisite[] prerequisites, EventChoice[] choices)
    {
        Id = id;
        Scope = scope;
        Weight = weight;
        CooldownDays = cooldownDays;
        Prompt = prompt;
        ContactId = contactId;
        Prerequisites = prerequisites;
        Choices = choices;
    }
}

/// <summary>
/// The loaded, validated set of event definitions the dispatcher evaluates and
/// the applier resolves against. Immutable after construction — content is
/// loaded once at boot; a malformed batch throws there, never at fire time.
/// </summary>
public sealed class GrittyEventLibrary
{
    private readonly GrittyEventDefinition[] _events;
    private readonly Dictionary<string, GrittyEventDefinition> _byId;

    public GrittyEventLibrary(IReadOnlyList<GrittyEventDefinition> events)
    {
        _events = new GrittyEventDefinition[events.Count];
        _byId = new Dictionary<string, GrittyEventDefinition>(events.Count, StringComparer.Ordinal);
        for (int i = 0; i < events.Count; i++)
        {
            GrittyEventDefinition definition = events[i];
            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException($"Duplicate gritty event id '{definition.Id}'.");
            }
            _events[i] = definition;
        }
    }

    /// <summary>Definition order = evaluation order (first satisfied event wins a subject's day).</summary>
    public IReadOnlyList<GrittyEventDefinition> Events => _events;

    public int Count => _events.Length;

    public bool TryGetById(string eventId, out GrittyEventDefinition definition) =>
        _byId.TryGetValue(eventId, out definition!);
}
