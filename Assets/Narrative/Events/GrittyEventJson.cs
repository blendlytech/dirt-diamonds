using System;
using System.Collections.Generic;
using System.Text.Json;
using DirtAndDiamonds.Data;
using DirtAndDiamonds.Simulation.Life;

namespace DirtAndDiamonds.Narrative.Events;

/// <summary>
/// Parses a content batch (gritty_event_framework.md §2) into a
/// <see cref="GrittyEventLibrary"/>. Hand-walked JsonDocument, no reflection
/// serializers — every error is loud and labelled with the offending event id,
/// so a malformed Sonnet content batch fails the boot/harness, never fire
/// time. Load-time allocation is irrelevant (once per session); the resulting
/// model is flat structs/arrays for the zero-alloc evaluation sweep.
/// </summary>
public static class GrittyEventJson
{
    /// <summary>The reserved fallback thread ("Unknown Number") for events an authored batch never tags with "contact" — mirrored by <see cref="Contacts.ContactRegistry"/>'s built-in fallback entry.</summary>
    public const string UnknownContactId = "unknown";

    public static GrittyEventLibrary Parse(string json)
    {
        var events = new List<GrittyEventDefinition>();
        ParseInto(json, events);
        return new GrittyEventLibrary(events);
    }

    /// <summary>
    /// Merges several content batches (one JSON document each) into one
    /// library — GameManager loads every file in the Content folder, so a new
    /// Sonnet batch is a dropped-in file, not a wiring change. Duplicate ids
    /// across batches fail in the library constructor.
    /// </summary>
    public static GrittyEventLibrary Parse(IReadOnlyList<string> jsonDocuments)
    {
        var events = new List<GrittyEventDefinition>();
        for (int i = 0; i < jsonDocuments.Count; i++)
        {
            ParseInto(jsonDocuments[i], events);
        }
        return new GrittyEventLibrary(events);
    }

    private static void ParseInto(string json, List<GrittyEventDefinition> events)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!document.RootElement.TryGetProperty("events", out JsonElement eventsElement)
            || eventsElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Gritty event content must be an object with an 'events' array.");
        }

        foreach (JsonElement eventElement in eventsElement.EnumerateArray())
        {
            events.Add(ParseEvent(eventElement));
        }
    }

    private static GrittyEventDefinition ParseEvent(JsonElement element)
    {
        string id = RequireString(element, "id", "<unnamed event>");

        string scopeText = RequireString(element, "scope", id);
        EventScope scope = scopeText switch
        {
            "any" => EventScope.Any,
            "avatar" => EventScope.Avatar,
            _ => throw new FormatException($"Event '{id}': unknown scope '{scopeText}' (expected 'any' or 'avatar')."),
        };

        double weight = RequireNumber(element, "weight", id);
        if (weight is < 0.0 or > 1.0 || double.IsNaN(weight))
        {
            throw new FormatException($"Event '{id}': weight {weight} is outside [0, 1].");
        }

        int cooldownDays = 0;
        if (element.TryGetProperty("cooldown_days", out JsonElement cooldownElement))
        {
            cooldownDays = cooldownElement.GetInt32();
            if (cooldownDays < 0)
            {
                throw new FormatException($"Event '{id}': cooldown_days must be ≥ 0.");
            }
        }

        EventPrerequisite[] prerequisites = Array.Empty<EventPrerequisite>();
        if (element.TryGetProperty("prerequisites", out JsonElement prereqElement))
        {
            prerequisites = new EventPrerequisite[prereqElement.GetArrayLength()];
            int i = 0;
            foreach (JsonElement leaf in prereqElement.EnumerateArray())
            {
                prerequisites[i++] = ParsePrerequisite(leaf, id);
            }
        }

        if (!element.TryGetProperty("choices", out JsonElement choicesElement)
            || choicesElement.ValueKind != JsonValueKind.Array
            || choicesElement.GetArrayLength() == 0)
        {
            throw new FormatException($"Event '{id}': at least one choice is required.");
        }

        var choices = new EventChoice[choicesElement.GetArrayLength()];
        var choiceIds = new HashSet<string>(StringComparer.Ordinal);
        int c = 0;
        foreach (JsonElement choiceElement in choicesElement.EnumerateArray())
        {
            EventChoice choice = ParseChoice(choiceElement, id, scope);
            if (!choiceIds.Add(choice.Id))
            {
                throw new FormatException($"Event '{id}': duplicate choice id '{choice.Id}'.");
            }
            choices[c++] = choice;
        }

        string prompt = element.TryGetProperty("prompt", out JsonElement promptElement)
            ? promptElement.GetString() ?? Humanize(id)
            : Humanize(id);

        // Additive (presentation_layer_narrative.md §4.2): a batch that omits
        // "contact" routes to the reserved "unknown" thread, same fallback
        // discipline as the prompt/label humanization above.
        string contactId = element.TryGetProperty("contact", out JsonElement contactElement)
            ? contactElement.GetString() ?? UnknownContactId
            : UnknownContactId;

        // The Events feed's card heading (BurnerPhone playtest follow-up):
        // additive like "contact" above — a batch that omits "category"
        // routes to the reserved General fallback rather than failing load.
        EventCategory category = EventCategory.General;
        if (element.TryGetProperty("category", out JsonElement categoryElement))
        {
            string categoryText = categoryElement.GetString()
                ?? throw new FormatException($"Event '{id}': 'category' must be a string.");
            category = categoryText switch
            {
                "baseball" => EventCategory.Baseball,
                "family" => EventCategory.Family,
                "romance" => EventCategory.Romance,
                "school" => EventCategory.School,
                "hustle" => EventCategory.Hustle,
                "career" => EventCategory.Career,
                "general" => EventCategory.General,
                _ => throw new FormatException($"Event '{id}': unknown category '{categoryText}'."),
            };
        }

        // Phone-split spec §1: an optional fire-time companion text. Present
        // means non-empty — a malformed/blank string is a loud content error,
        // never a silent no-text (the closed-vocabulary discipline every
        // other optional field here follows).
        string? textMessage = null;
        if (element.TryGetProperty("text_message", out JsonElement textMessageElement))
        {
            if (textMessageElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(textMessageElement.GetString()))
            {
                throw new FormatException($"Event '{id}': 'text_message' must be a non-empty string.");
            }
            textMessage = textMessageElement.GetString();
        }

        return new GrittyEventDefinition(
            id, scope, weight, cooldownDays, prompt, contactId, category, prerequisites, choices, textMessage);
    }

    /// <summary>Player-facing fallback for content that omits "prompt"/"label": snake_case id → Title Case words.</summary>
    private static string Humanize(string id)
    {
        string[] words = id.Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
        }
        return string.Join(' ', words);
    }

    private static EventPrerequisite ParsePrerequisite(JsonElement element, string eventId)
    {
        if (element.TryGetProperty("field", out JsonElement fieldElement))
        {
            string fieldName = fieldElement.GetString()
                ?? throw new FormatException($"Event '{eventId}': prerequisite 'field' must be a string.");
            SubjectField field = fieldName switch
            {
                "funds" => SubjectField.Funds,
                "age" => SubjectField.Age,
                "recklessness" => SubjectField.Recklessness,
                "health_ceiling" => SubjectField.HealthCeiling,
                "detection_risk" => SubjectField.DetectionRisk,
                "baseball_interest" => SubjectField.BaseballInterest,
                "strictness" => SubjectField.Strictness,
                "teammate_ex_of_partner" => SubjectField.TeammateExOfPartner,
                "tier" => SubjectField.Tier,
                "gpa" => SubjectField.Gpa,
                _ => throw new FormatException($"Event '{eventId}': unknown prerequisite field '{fieldName}'."),
            };

            string op = RequireString(element, "op", eventId);
            FieldComparison comparison = op switch
            {
                "<" => FieldComparison.Less,
                "<=" => FieldComparison.LessOrEqual,
                ">" => FieldComparison.Greater,
                ">=" => FieldComparison.GreaterOrEqual,
                "==" => FieldComparison.Equal,
                "!=" => FieldComparison.NotEqual,
                _ => throw new FormatException($"Event '{eventId}': unknown comparison op '{op}'."),
            };

            double value = RequireNumber(element, "value", eventId);
            return EventPrerequisite.ForField(field, comparison, value);
        }

        if (element.TryGetProperty("flag_active", out JsonElement activeElement))
        {
            string flag = activeElement.GetString()
                ?? throw new FormatException($"Event '{eventId}': 'flag_active' must be a string.");
            int minDaysSince = 0;
            if (element.TryGetProperty("min_days_since", out JsonElement minDaysElement))
            {
                minDaysSince = minDaysElement.GetInt32();
                if (minDaysSince < 0)
                {
                    throw new FormatException($"Event '{eventId}': min_days_since must be ≥ 0.");
                }
            }
            return EventPrerequisite.ForFlagActive(flag, minDaysSince);
        }

        if (element.TryGetProperty("flag_inactive", out JsonElement inactiveElement))
        {
            string flag = inactiveElement.GetString()
                ?? throw new FormatException($"Event '{eventId}': 'flag_inactive' must be a string.");
            return EventPrerequisite.ForFlagInactive(flag);
        }

        throw new FormatException(
            $"Event '{eventId}': prerequisite must be a field comparison, 'flag_active', or 'flag_inactive'.");
    }

    private static EventChoice ParseChoice(JsonElement element, string eventId, EventScope scope)
    {
        string choiceId = RequireString(element, "id", eventId);

        int autopilotWeight = 1;
        if (element.TryGetProperty("autopilot_weight", out JsonElement weightElement))
        {
            autopilotWeight = weightElement.GetInt32();
            if (autopilotWeight < 0)
            {
                throw new FormatException($"Event '{eventId}' choice '{choiceId}': autopilot_weight must be ≥ 0.");
            }
        }

        EventConsequence[] consequences = Array.Empty<EventConsequence>();
        if (element.TryGetProperty("consequences", out JsonElement consequencesElement))
        {
            consequences = new EventConsequence[consequencesElement.GetArrayLength()];
            int i = 0;
            foreach (JsonElement consequenceElement in consequencesElement.EnumerateArray())
            {
                consequences[i++] = ParseConsequence(consequenceElement, eventId, choiceId, scope);
            }
        }

        string label = element.TryGetProperty("label", out JsonElement labelElement)
            ? labelElement.GetString() ?? Humanize(choiceId)
            : Humanize(choiceId);

        // Amendment §2: the immediate narrative payoff. Present means
        // non-empty; absent is the documented "You: <Label>" UI fallback,
        // never a loader concern.
        string? outcome = null;
        if (element.TryGetProperty("outcome", out JsonElement outcomeElement))
        {
            if (outcomeElement.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(outcomeElement.GetString()))
            {
                throw new FormatException($"Event '{eventId}' choice '{choiceId}': 'outcome' must be a non-empty string.");
            }
            outcome = outcomeElement.GetString();
        }

        // Amendment §1: choice-level "text_message" is either a plain string
        // (delay 0) or { "body": ..., "delay_days": N >= 0 } — two accepted
        // shapes, both closed-vocabulary and both loudly rejected otherwise.
        string? textMessageBody = null;
        int textMessageDelayDays = 0;
        if (element.TryGetProperty("text_message", out JsonElement choiceTextElement))
        {
            if (choiceTextElement.ValueKind == JsonValueKind.String)
            {
                if (string.IsNullOrEmpty(choiceTextElement.GetString()))
                {
                    throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': 'text_message' must be a non-empty string.");
                }
                textMessageBody = choiceTextElement.GetString();
            }
            else if (choiceTextElement.ValueKind == JsonValueKind.Object)
            {
                textMessageBody = RequireString(choiceTextElement, "body", eventId);
                if (string.IsNullOrEmpty(textMessageBody))
                {
                    throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': 'text_message.body' must be a non-empty string.");
                }
                if (choiceTextElement.TryGetProperty("delay_days", out JsonElement delayElement))
                {
                    if (delayElement.ValueKind != JsonValueKind.Number)
                    {
                        throw new FormatException(
                            $"Event '{eventId}' choice '{choiceId}': 'text_message.delay_days' must be a number.");
                    }
                    double delayRaw = delayElement.GetDouble();
                    if (delayRaw < 0 || delayRaw != Math.Floor(delayRaw))
                    {
                        throw new FormatException(
                            $"Event '{eventId}' choice '{choiceId}': 'text_message.delay_days' must be a whole number ≥ 0 (got {delayRaw}).");
                    }
                    textMessageDelayDays = (int)delayRaw;
                }
            }
            else
            {
                throw new FormatException(
                    $"Event '{eventId}' choice '{choiceId}': 'text_message' must be a string or an object with 'body'.");
            }
        }

        return new EventChoice(
            choiceId, autopilotWeight, label, consequences, outcome, textMessageBody, textMessageDelayDays);
    }

    private static EventConsequence ParseConsequence(
        JsonElement element, string eventId, string choiceId, EventScope scope)
    {
        string type = RequireString(element, "type", eventId);
        switch (type)
        {
            case "conceive_child":
                // Only the avatar has a tracked bloodline; a scope-any
                // conception is meaningless, so the apply path stays total
                // (marriage_and_conception.md §4.1 — fail loud at boot).
                if (scope != EventScope.Avatar)
                {
                    throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': 'conceive_child' is only valid on a 'scope: avatar' event.");
                }
                return EventConsequence.ForConceiveChild();
            case "funds":
                return EventConsequence.ForAmount(ConsequenceKind.Funds, RequireNumber(element, "amount", eventId));
            case "stress":
                return EventConsequence.ForAmount(ConsequenceKind.Stress, RequireNumber(element, "amount", eventId));
            case "interest":
                return EventConsequence.ForAmount(ConsequenceKind.Interest, RequireNumber(element, "amount", eventId));
            case "detection_risk":
                return EventConsequence.ForAmount(ConsequenceKind.DetectionRisk, RequireNumber(element, "amount", eventId));
            case "health_ceiling":
                return EventConsequence.ForAmount(ConsequenceKind.HealthCeiling, RequireNumber(element, "amount", eventId));
            case "absence":
            {
                // Phase 8c roster availability. Days are a whole-day count ≥ 1
                // — a fractional or zero-day bench is a content error, loud at
                // load like every other malformed consequence.
                string reasonText = RequireString(element, "reason", eventId);
                AbsenceReason reason = reasonText switch
                {
                    "injury" => AbsenceReason.Injury,
                    "suspension" => AbsenceReason.Suspension,
                    "arrest" => AbsenceReason.Arrest,
                    _ => throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': unknown absence reason '{reasonText}' " +
                        "(expected 'injury', 'suspension' or 'arrest')."),
                };
                double daysRaw = RequireNumber(element, "days", eventId);
                if (daysRaw < 1 || daysRaw != Math.Floor(daysRaw))
                {
                    throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': absence 'days' must be a whole number ≥ 1 (got {daysRaw}).");
                }
                return EventConsequence.ForAbsence(reason, (int)daysRaw);
            }
            case "set_flag":
                return EventConsequence.ForFlag(ConsequenceKind.SetFlag, RequireString(element, "flag", eventId));
            case "clear_flag":
                return EventConsequence.ForFlag(ConsequenceKind.ClearFlag, RequireString(element, "flag", eventId));
            case "relationship":
            {
                string kindText = RequireString(element, "kind", eventId);
                RelationshipKind kind = kindText switch
                {
                    "rival" => RelationshipKind.Rival,
                    "friend" => RelationshipKind.Friend,
                    "partner" => RelationshipKind.Partner,
                    // 'child' edges are lineage state owned by ConceiveChild/succession,
                    // deliberately not authorable as an event consequence (§4).
                    _ => throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': unknown relationship kind '{kindText}'."),
                };

                double affinity = RequireNumber(element, "affinity", eventId);

                string targetText = RequireString(element, "target", eventId);
                RelationshipTargetSelector target = targetText switch
                {
                    "teammate" => RelationshipTargetSelector.Teammate,
                    "opponent" => RelationshipTargetSelector.Opponent,
                    "league" => RelationshipTargetSelector.League,
                    "teammate_ex_of_partner" => RelationshipTargetSelector.TeammateExOfPartner,
                    _ => throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': unknown relationship target '{targetText}'."),
                };

                return EventConsequence.ForRelationship(kind, affinity, target);
            }
            case "end_partnership":
            {
                // HS-5 breakup/divorce: reclassifies the subject's live
                // Partner edge in place (never deletes it — the ex stays
                // graph history). Friend or Rival only; 'partner' here would
                // be a no-op contradiction and 'child' is lineage state.
                string kindText = RequireString(element, "kind", eventId);
                RelationshipKind kind = kindText switch
                {
                    "friend" => RelationshipKind.Friend,
                    "rival" => RelationshipKind.Rival,
                    _ => throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': end_partnership kind must be 'friend' or 'rival' (got '{kindText}')."),
                };
                return EventConsequence.ForEndPartnership(kind, RequireNumber(element, "affinity", eventId));
            }
            case "rekindle_partnership":
                // HS-5 "getting back together": re-mints Partner on the
                // recorded ex. Only the avatar's romance history is tracked
                // (Game_State avatar_ex_partner_id), so a scope-any rekindle
                // is meaningless — same load-time gate as conceive_child.
                if (scope != EventScope.Avatar)
                {
                    throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': 'rekindle_partnership' is only valid on a 'scope: avatar' event.");
                }
                return EventConsequence.ForRekindlePartnership(RequireNumber(element, "affinity", eventId));
            case "child_development":
            {
                // HS-5 rearing content (§7.1): only the avatar rears through
                // events — same load-time gate as conceive_child.
                if (scope != EventScope.Avatar)
                {
                    throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': 'child_development' is only valid on a 'scope: avatar' event.");
                }
                string axisText = RequireString(element, "axis", eventId);
                ChildAxis axis = axisText switch
                {
                    "care" => ChildAxis.Care,
                    "coaching" => ChildAxis.Coaching,
                    "funding" => ChildAxis.Funding,
                    "neglect" => ChildAxis.Neglect,
                    _ => throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': unknown child_development axis '{axisText}' " +
                        "(expected 'care', 'coaching', 'funding' or 'neglect')."),
                };
                return EventConsequence.ForChildDevelopment(axis, RequireNumber(element, "amount", eventId));
            }
            case "person_stat":
            {
                string statText = RequireString(element, "stat", eventId);
                PersonStatId stat = statText switch
                {
                    "intelligence" => PersonStatId.Intelligence,
                    "maturity" => PersonStatId.Maturity,
                    "happiness" => PersonStatId.Happiness,
                    "charisma" => PersonStatId.Charisma,
                    "confidence" => PersonStatId.Confidence,
                    "reputation" => PersonStatId.Reputation,
                    "social_status" => PersonStatId.SocialStatus,
                    "attractiveness" => PersonStatId.Attractiveness,
                    "teamwork" => PersonStatId.Teamwork,
                    "morality" => PersonStatId.Morality,
                    "discipline" => PersonStatId.Discipline,
                    "work_ethic" => PersonStatId.WorkEthic,
                    // gpa is deliberately absent — it moves only through the
                    // HS-4 weekly closed form (PersonDrift.cs), never a
                    // per-event nudge.
                    _ => throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': unknown person_stat '{statText}'."),
                };
                return EventConsequence.ForPersonStat(stat, RequireNumber(element, "amount", eventId));
            }
            default:
                throw new FormatException(
                    $"Event '{eventId}' choice '{choiceId}': unknown consequence type '{type}'.");
        }
    }

    private static string RequireString(JsonElement element, string property, string eventId)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Event '{eventId}': required string property '{property}' is missing.");
        }
        return value.GetString()!;
    }

    private static double RequireNumber(JsonElement element, string property, string eventId)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException($"Event '{eventId}': required numeric property '{property}' is missing.");
        }
        return value.GetDouble();
    }
}
