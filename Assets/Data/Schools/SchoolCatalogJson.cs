using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DirtAndDiamonds.Data.Schools;

/// <summary>
/// Parses Assets/Data/Schools/schools.json into a <see cref="SchoolCatalog"/>.
/// The <c>ItemCatalogJson</c> discipline exactly: hand-walked JsonDocument, no
/// reflection serializers, every error loud and labelled with the offending
/// team_id so a malformed content edit fails the boot/harness — never a picker
/// render. Load-time allocation is irrelevant (once per session).
/// </summary>
public static class SchoolCatalogJson
{
    public static SchoolCatalog Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!document.RootElement.TryGetProperty("schools", out JsonElement schoolsElement)
            || schoolsElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("School catalog must be an object with a 'schools' array.");
        }

        var entries = new List<SchoolDefinition>(schoolsElement.GetArrayLength());
        foreach (JsonElement schoolElement in schoolsElement.EnumerateArray())
        {
            entries.Add(ParseSchool(schoolElement));
        }
        return new SchoolCatalog(entries);
    }

    private static SchoolDefinition ParseSchool(JsonElement element)
    {
        int teamId = RequireInt(element, "team_id", "<unnumbered school>");
        string label = $"team {teamId}";

        string abbreviation = RequireString(element, "abbreviation", label);
        string schoolName = RequireString(element, "school_name", label);
        string mascot = RequireString(element, "mascot", label);
        
        if (!element.TryGetProperty("images", out JsonElement imagesElement)
            || imagesElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"School {label}: missing required 'images' object.");
        }
        string logoPath = RequireString(imagesElement, "logo", $"{label} images");
        string schoolPath = RequireString(imagesElement, "school", $"{label} images");
        string fieldPath = RequireString(imagesElement, "field", $"{label} images");

        if (!element.TryGetProperty("colors", out JsonElement colorsElement)
            || colorsElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"School {label}: missing required 'colors' object.");
        }
        SchoolColor primary = RequireColor(colorsElement, "primary", label);
        SchoolColor secondary = RequireColor(colorsElement, "secondary", label);
        // Accent is optional; a row that omits it reuses its secondary as trim.
        SchoolColor accent = colorsElement.TryGetProperty("accent", out _)
            ? RequireColor(colorsElement, "accent", label)
            : secondary;

        string paletteDescription = RequireString(element, "palette_description", label);
        string flavor = RequireString(element, "flavor", label);

        return new SchoolDefinition(
            teamId, abbreviation, schoolName, mascot,
            logoPath, schoolPath, fieldPath,
            primary, secondary, accent, paletteDescription, flavor);
    }

    private static SchoolColor RequireColor(JsonElement colorsElement, string slot, string label)
    {
        if (!colorsElement.TryGetProperty(slot, out JsonElement colorElement)
            || colorElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"School {label}: color '{slot}' must be an object with 'name' and 'hex'.");
        }
        string name = RequireString(colorElement, "name", $"{label} color '{slot}'");
        string hex = RequireString(colorElement, "hex", $"{label} color '{slot}'");
        ValidateHex(hex, label, slot);
        return new SchoolColor(name, hex.ToUpperInvariant());
    }

    /// <summary>Enforces the <c>#RRGGBB</c> shape the UI's Color.FromHtml expects — a bad hex fails boot, not a swatch draw.</summary>
    private static void ValidateHex(string hex, string label, string slot)
    {
        bool ok = hex.Length == 7 && hex[0] == '#';
        for (int i = 1; ok && i < hex.Length; i++)
        {
            ok = Uri.IsHexDigit(hex[i]);
        }
        if (!ok)
        {
            throw new FormatException($"School {label}: color '{slot}' hex '{hex}' is not a #RRGGBB value.");
        }
    }

    private static string RequireString(JsonElement element, string property, string label)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"School {label}: missing or empty required string '{property}'.");
        }
        return value.GetString()!;
    }

    private static int RequireInt(JsonElement element, string property, string label)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            throw new FormatException($"School {label}: missing or non-integer required field '{property}'.");
        }
        return result;
    }
}
