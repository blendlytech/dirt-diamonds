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

/// <summary>
/// Subject fields a prerequisite may compare (gritty_event_framework.md §3).
/// Players-row columns, plus — since HS-5 — Family_Background.strictness,
/// which the poll snapshot LEFT JOINs in (COALESCEd to the neutral 50 for the
/// no-row majority, so a strictness gate simply never fires for people
/// without a generated family). TeammateExOfPartner (schema v13) is a
/// boolean (0/1) computed the same way from a Relationships/
/// Relationship_History self-join — the graph-reaching prerequisite
/// hs_clubhouse_cancer gates on.
///
/// Tier and Gpa (the HS onboarding arc, docs/design/hs_onboarding_events.md
/// §1) follow the exact same poll-join shape: Tier LEFT JOINs Team_Tiers
/// (COALESCEd to MLB(5) for a rostered team missing its row, the v6→v7
/// backfill convention — but a NULL team_id reads as sentinel −1). The
/// sentinel defeats every ">=" and "==" gate real content authors (both
/// shipped gates — onboarding's tier==0, the rookie batch's tier>=2 — hold
/// for 0–5 only, never −1), so unrostered subjects like seeded parent NPCs
/// can never satisfy either. DISCLOSED CAVEAT: it does NOT defeat a "&lt;"
/// gate — −1 &lt; N for any N ≥ 0, so a hypothetical future "tier &lt; 2" gate
/// on a scope:any event would also match an unrostered subject. No shipped
/// event uses "&lt;" on tier; a future one should prefer scope:avatar or an
/// explicit lower bound. Gpa LEFT JOINs Player_Person (COALESCEd to the
/// schema default 2.5).
/// </summary>
public enum SubjectField : byte
{
    Funds,
    Age,
    Recklessness,
    HealthCeiling,
    DetectionRisk,
    BaseballInterest,
    Strictness,
    TeammateExOfPartner,
    Tier,
    Gpa,
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

    /// <summary>
    /// Atomic clamped delta on one of the twelve <see cref="PersonStatId"/>
    /// integer columns (high_school_person_layer.md §9, HS-5) — the same
    /// writer (PersonQueries.AdjustStat) HS-4's schedule actions already use,
    /// extended into the gritty-event vocabulary. GPA is deliberately
    /// unreachable here (see PersonDrift.cs — it moves only through the
    /// weekly closed form).
    /// </summary>
    PersonStat,

    /// <summary>
    /// Ends the subject's current partnership (HS-5 breakup/divorce): the live
    /// Partner edge is RECLASSIFIED in place to the authored kind (Friend or
    /// Rival) and affinity — never deleted, so the ex remains graph history
    /// (the §8 "dating the rival's ex" substrate) and persistence rides the
    /// existing dirty-edge upsert with no delete path. Reclassification is
    /// what reopens ApplyRelationship's single-partner exclusivity guard, so
    /// re-dating/remarriage works afterwards. An unpartnered subject is a
    /// skip-by-design no-op (the empty-pool precedent).
    /// </summary>
    EndPartnership,

    /// <summary>
    /// Re-mints the Partner edge with the subject's recorded most-recent ex
    /// (HS-5 "getting back together"): the targeted counterpart to
    /// <see cref="Relationship"/>'s random-pool Partner mint, which
    /// ApplyRelationship's adjust-don't-reclassify rule correctly stops from
    /// re-partnering an existing friend/rival edge. Valid only on
    /// scope-avatar events (load-time gate, the <see cref="ConceiveChild"/>
    /// precedent) because only the avatar's romance history is tracked
    /// (Game_State avatar_ex_partner_id, written by <see cref="EndPartnership"/>).
    /// Skips when the subject is already partnered (exclusivity guard), no ex
    /// is recorded, or the recorded ex no longer has a Friend/Rival edge to
    /// the subject.
    /// </summary>
    RekindlePartnership,

    /// <summary>
    /// Atomic clamped delta on one Child_Development axis, applied to EVERY
    /// child of the subject — the younger endpoint of each Child edge, per
    /// the §1.2 age invariant — so rearing content feeds the §7 nurture
    /// blend (high_school_person_layer.md §7.1: care/coaching/funding accrue
    /// from rearing events, neglect from missed obligations). Valid only on
    /// scope-avatar events (load-time gate, the ConceiveChild precedent):
    /// only the avatar rears through events. A childless subject is a
    /// skip-by-design no-op (the empty-pool precedent). Children resolve
    /// from the DATABASE (Child edges are DB-authored in ConceiveChild's
    /// batch, never written through the live graph — the inverse polarity
    /// of the Partner lookups).
    /// </summary>
    ChildDevelopment,
}

/// <summary>Who a relationship consequence pairs the subject with (§4), resolved at apply time.</summary>
public enum RelationshipTargetSelector : byte
{
    Teammate,
    Opponent,
    League,

    /// <summary>
    /// The specific teammate who has a Relationship_History row with the
    /// subject's CURRENT partner (schema v13) — never a random teammate pool
    /// draw. Empty-pool skip (the existing precedent) when the subject isn't
    /// partnered or no teammate has that history, e.g. a poll-to-apply race
    /// on the live graph (gritty_event_framework.md §6 disclosed staleness).
    /// </summary>
    TeammateExOfPartner,
}

/// <summary>
/// One Child_Development axis (high_school_person_layer.md §7.1). CONTRACT:
/// the ordinal IS the index into PersonQueries.ChildAxisColumns' fixed column
/// table — mirrored across the layer wall, never referenced (the PersonStatId
/// precedent); the harness round-trip-pins each ordinal to its column.
/// </summary>
public enum ChildAxis : byte
{
    Care,
    Coaching,
    Funding,
    Neglect,
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

    // PersonStat form (Amount carries the signed integer delta):
    public readonly PersonStatId PersonStat;

    // ChildDevelopment form (Amount carries the signed integer delta):
    public readonly ChildAxis ChildAxis;

    private EventConsequence(
        ConsequenceKind kind, double amount, string? flagName,
        RelationshipKind relationshipKind, RelationshipTargetSelector target,
        AbsenceReason absenceReason = AbsenceReason.None,
        PersonStatId personStat = default,
        ChildAxis childAxis = default)
    {
        Kind = kind;
        Amount = amount;
        FlagName = flagName;
        RelationshipKind = relationshipKind;
        Target = target;
        AbsenceReason = absenceReason;
        PersonStat = personStat;
        ChildAxis = childAxis;
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

    /// <summary>Signed integer delta on one of the twelve adjustable person stats (§9, HS-5). GPA is not reachable this way.</summary>
    public static EventConsequence ForPersonStat(PersonStatId stat, double amount) =>
        new(ConsequenceKind.PersonStat, amount, null, default, default, AbsenceReason.None, stat);

    /// <summary>
    /// Reclassifies the subject's live Partner edge to <paramref name="newKind"/>
    /// at <paramref name="affinity"/> (HS-5 breakup/divorce). Friend or Rival
    /// only — a breakup cannot mint a new Partner (that is ForRelationship's
    /// job) and Child edges are lineage state (the ForRelationship precedent).
    /// </summary>
    public static EventConsequence ForEndPartnership(RelationshipKind newKind, double affinity)
    {
        if (newKind is not (RelationshipKind.Friend or RelationshipKind.Rival))
        {
            throw new ArgumentOutOfRangeException(nameof(newKind), newKind,
                "A partnership can only end into a Friend or Rival edge.");
        }
        return new EventConsequence(ConsequenceKind.EndPartnership, affinity, null, newKind, default);
    }

    /// <summary>
    /// Re-mints the Partner edge with the subject's recorded ex at
    /// <paramref name="affinity"/> (HS-5 rekindle). The kind is always
    /// Partner by construction — the target is the recorded ex, never a pool
    /// draw, so this carries no selector.
    /// </summary>
    public static EventConsequence ForRekindlePartnership(double affinity) =>
        new(ConsequenceKind.RekindlePartnership, affinity, null, RelationshipKind.Partner, default);

    /// <summary>
    /// Signed integer delta on one Child_Development axis, applied to every
    /// child of the subject (§7.1, HS-5 rearing content). Targets resolve at
    /// apply time from the subject's Child edges — no selector.
    /// </summary>
    public static EventConsequence ForChildDevelopment(ChildAxis axis, double amount) =>
        new(ConsequenceKind.ChildDevelopment, amount, null, default, default,
            AbsenceReason.None, default, axis);
}

public sealed class EventChoice
{
    public readonly string Id;

    /// <summary>Relative weight for autopilot resolution until the choice UI exists (≥0).</summary>
    public readonly int AutopilotWeight;

    /// <summary>Player-facing button text for the event-choice UI. Content-authored, or a humanized fallback of <see cref="Id"/> (GrittyEventJson.Humanize) when the batch omits "label".</summary>
    public readonly string Label;

    public readonly EventConsequence[] Consequences;

    /// <summary>
    /// The immediate narrative payoff (Events feed §2 of the phone-split
    /// amendment): renders as the resolution of the event card the moment
    /// this choice resolves. Content-authored, optional — null when the
    /// batch omits "outcome", in which case the Events feed falls back to
    /// "You: &lt;Label&gt;" so every pre-existing event degrades gracefully.
    /// </summary>
    public readonly string? Outcome;

    /// <summary>
    /// The REACTION text this choice's contact sends, in their voice, dated
    /// resolution day + <see cref="TextMessageDelayDays"/> (§1 of the
    /// amendment). Null when the batch omits choice-level "text_message".
    /// </summary>
    public readonly string? TextMessageBody;

    /// <summary>Days after resolution the reaction text lands (0 = same day). Only meaningful when <see cref="TextMessageBody"/> is non-null.</summary>
    public readonly int TextMessageDelayDays;

    public EventChoice(
        string id, int autopilotWeight, string label, EventConsequence[] consequences,
        string? outcome = null, string? textMessageBody = null, int textMessageDelayDays = 0)
    {
        Id = id;
        AutopilotWeight = autopilotWeight;
        Label = label;
        Consequences = consequences;
        Outcome = outcome;
        TextMessageBody = textMessageBody;
        TextMessageDelayDays = textMessageDelayDays;
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

    /// <summary>
    /// The fire-time companion text (phone-split spec §1): a REAL text the
    /// contact sends the moment this event fires, in their voice, part of
    /// the scene regardless of which choice the player eventually picks.
    /// Content-authored, optional — null when the batch omits "text_message".
    /// </summary>
    public readonly string? TextMessage;

    public GrittyEventDefinition(
        string id, EventScope scope, double weight, int cooldownDays, string prompt, string contactId,
        EventPrerequisite[] prerequisites, EventChoice[] choices, string? textMessage = null)
    {
        Id = id;
        Scope = scope;
        Weight = weight;
        CooldownDays = cooldownDays;
        Prompt = prompt;
        ContactId = contactId;
        Prerequisites = prerequisites;
        Choices = choices;
        TextMessage = textMessage;
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
