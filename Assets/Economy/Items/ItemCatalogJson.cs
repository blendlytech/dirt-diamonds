using System;
using System.Collections.Generic;
using System.Text.Json;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Economy.Items;

/// <summary>
/// Parses Assets/Data/Items/items.json (high_school_person_layer.md §5.1) into
/// an <see cref="ItemCatalog"/>. The GrittyEventJson discipline exactly:
/// hand-walked JsonDocument, no reflection serializers, every error loud and
/// labelled with the offending item id so a malformed content edit fails the
/// boot/harness — never a shop render. Load-time allocation is irrelevant
/// (once per session); the resulting model is flat for the zero-alloc §5.2
/// read-side aggregation.
/// </summary>
public static class ItemCatalogJson
{
    public static ItemCatalog Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!document.RootElement.TryGetProperty("items", out JsonElement itemsElement)
            || itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Item catalog must be an object with an 'items' array.");
        }

        var entries = new List<ItemDefinition>(itemsElement.GetArrayLength());
        foreach (JsonElement itemElement in itemsElement.EnumerateArray())
        {
            entries.Add(ParseItem(itemElement));
        }
        return new ItemCatalog(entries);
    }

    private static ItemDefinition ParseItem(JsonElement element)
    {
        string id = RequireString(element, "id", "<unnamed item>");
        string name = RequireString(element, "name", id);

        string categoryText = RequireString(element, "category", id);
        ItemCategory category = categoryText switch
        {
            "Transport" => ItemCategory.Transport,
            "Clothing" => ItemCategory.Clothing,
            "Jewelry" => ItemCategory.Jewelry,
            "Food" => ItemCategory.Food,
            "Gear" => ItemCategory.Gear,
            _ => throw new FormatException(
                $"Item '{id}': unknown category '{categoryText}' (expected Transport/Clothing/Jewelry/Food/Gear)."),
        };

        double price = RequireNumber(element, "price", id);
        if (price < 0.0 || !double.IsFinite(price))
        {
            throw new FormatException($"Item '{id}': price {price} must be a finite number ≥ 0.");
        }

        int modAttractiveness = 0, modSocialStatus = 0, modReputation = 0;
        if (element.TryGetProperty("modifiers", out JsonElement modifiersElement))
        {
            if (modifiersElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"Item '{id}': 'modifiers' must be an object.");
            }
            foreach (JsonProperty modifier in modifiersElement.EnumerateObject())
            {
                int value = ReadModifierValue(modifier, id);
                switch (modifier.Name)
                {
                    case "attractiveness":
                        modAttractiveness = value;
                        break;
                    case "social_status":
                        modSocialStatus = value;
                        break;
                    case "reputation":
                        modReputation = value;
                        break;
                    default:
                        // §5.2's vocabulary is CLOSED on purpose — these three
                        // stats never feed the sim, which is what keeps the
                        // whole item system calibration-inert. A new key here
                        // is a design change, not a content edit.
                        throw new FormatException(
                            $"Item '{id}': modifier '{modifier.Name}' is not a status stat (only attractiveness/social_status/reputation are item-buffable, §5.2).");
                }
            }
        }

        double transportHoursSaved = 0.0;
        if (element.TryGetProperty("transport_hours_saved", out JsonElement hoursElement))
        {
            if (category != ItemCategory.Transport)
            {
                throw new FormatException(
                    $"Item '{id}': transport_hours_saved is only valid on Transport items (§5.3), not {category}.");
            }
            transportHoursSaved = hoursElement.GetDouble();
            if (transportHoursSaved <= 0.0 || !double.IsFinite(transportHoursSaved))
            {
                throw new FormatException(
                    $"Item '{id}': transport_hours_saved {transportHoursSaved} must be a finite number > 0 (omit the field for no refund).");
            }
        }

        int autobuyMinTier = -1;
        if (element.TryGetProperty("autobuy_min_tier", out JsonElement autobuyElement))
        {
            autobuyMinTier = autobuyElement.GetInt32();
            if (autobuyMinTier is < 0 or > 4)
            {
                throw new FormatException($"Item '{id}': autobuy_min_tier {autobuyMinTier} is outside the wealth-tier range 0–4.");
            }
        }

        return new ItemDefinition(
            id, name, category, price,
            modAttractiveness, modSocialStatus, modReputation,
            transportHoursSaved, autobuyMinTier);
    }

    private static int ReadModifierValue(JsonProperty modifier, string id)
    {
        if (modifier.Value.ValueKind != JsonValueKind.Number || !modifier.Value.TryGetInt32(out int value))
        {
            throw new FormatException($"Item '{id}': modifier '{modifier.Name}' must be an integer.");
        }
        if (value is < -100 or > 100)
        {
            throw new FormatException($"Item '{id}': modifier '{modifier.Name}' value {value} is outside [-100, 100].");
        }
        return value;
    }

    private static string RequireString(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Item '{id}': missing or empty required string '{property}'.");
        }
        return value.GetString()!;
    }

    private static double RequireNumber(JsonElement element, string property, string id)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException($"Item '{id}': missing or non-numeric required field '{property}'.");
        }
        return value.GetDouble();
    }
}
