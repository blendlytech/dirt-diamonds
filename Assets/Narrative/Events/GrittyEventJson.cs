using System;
using System.Collections.Generic;
using System.Text.Json;
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
            EventChoice choice = ParseChoice(choiceElement, id);
            if (!choiceIds.Add(choice.Id))
            {
                throw new FormatException($"Event '{id}': duplicate choice id '{choice.Id}'.");
            }
            choices[c++] = choice;
        }

        return new GrittyEventDefinition(id, scope, weight, cooldownDays, prerequisites, choices);
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

    private static EventChoice ParseChoice(JsonElement element, string eventId)
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
                consequences[i++] = ParseConsequence(consequenceElement, eventId, choiceId);
            }
        }

        return new EventChoice(choiceId, autopilotWeight, consequences);
    }

    private static EventConsequence ParseConsequence(JsonElement element, string eventId, string choiceId)
    {
        string type = RequireString(element, "type", eventId);
        switch (type)
        {
            case "funds":
                return EventConsequence.ForAmount(ConsequenceKind.Funds, RequireNumber(element, "amount", eventId));
            case "stress":
                return EventConsequence.ForAmount(ConsequenceKind.Stress, RequireNumber(element, "amount", eventId));
            case "interest":
                return EventConsequence.ForAmount(ConsequenceKind.Interest, RequireNumber(element, "amount", eventId));
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
                    _ => throw new FormatException(
                        $"Event '{eventId}' choice '{choiceId}': unknown relationship target '{targetText}'."),
                };

                return EventConsequence.ForRelationship(kind, affinity, target);
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
