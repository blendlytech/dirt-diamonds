using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DirtAndDiamonds.Narrative.Contacts;

/// <summary>Free-text authoring vocabulary for a contact's flavor (presentation_layer_narrative.md §4.2). Closed like every other content-loader enum — an unrecognized role is a loud load-time error, never a fire-time surprise.</summary>
public enum ContactRole : byte
{
    Unknown,
    Girlfriend,
    Coach,
    Agent,
    Dealer,
    Fixer,
    Family,
}

/// <summary>One entry in the Burner Phone's contact registry — pure content, never referenced by the Baseball or Life sims.</summary>
public sealed class ContactDefinition
{
    public readonly string Id;
    public readonly string DisplayName;
    public readonly ContactRole Role;

    /// <summary>Feeds the §6 portrait pipeline (10e); null until authored. Defaults to <see cref="Id"/> when omitted, so a future portrait asset set can key off it without a content edit.</summary>
    public readonly string PortraitKey;

    public ContactDefinition(string id, string displayName, ContactRole role, string portraitKey)
    {
        Id = id;
        DisplayName = displayName;
        Role = role;
        PortraitKey = portraitKey;
    }
}

/// <summary>
/// The loaded, validated contact registry (presentation_layer_narrative.md §4.2). Immutable
/// after construction — content loads once at boot, same lifecycle as GrittyEventLibrary. The
/// reserved "unknown" entry ("Unknown Number") is guaranteed resolvable even if a content edit
/// drops it from contacts.json, so an untagged/unrecognized contact id never crashes a render.
/// </summary>
public sealed class ContactRegistry
{
    public const string UnknownContactId = "unknown";

    private static readonly ContactDefinition FallbackUnknown =
        new(UnknownContactId, "Unknown Number", ContactRole.Unknown, UnknownContactId);

    private readonly Dictionary<string, ContactDefinition> _byId;

    public ContactRegistry(IReadOnlyList<ContactDefinition> contacts)
    {
        _byId = new Dictionary<string, ContactDefinition>(contacts.Count, StringComparer.Ordinal);
        foreach (ContactDefinition contact in contacts)
        {
            if (!_byId.TryAdd(contact.Id, contact))
            {
                throw new InvalidOperationException($"Duplicate contact id '{contact.Id}'.");
            }
        }
        _byId.TryAdd(UnknownContactId, FallbackUnknown);
    }

    /// <summary>Resolves a contact id to its registry entry, or the reserved "unknown" fallback when the id is absent — never throws, so a render call site can never crash on stale/unrecognized content.</summary>
    public ContactDefinition Resolve(string contactId) =>
        _byId.TryGetValue(contactId, out ContactDefinition? contact) ? contact : FallbackUnknown;

    public bool Contains(string contactId) => _byId.ContainsKey(contactId);
}

/// <summary>
/// Parses <c>contacts.json</c> (presentation_layer_narrative.md §4.2) into a
/// <see cref="ContactRegistry"/>. Hand-walked JsonDocument, same discipline as
/// GrittyEventJson — a malformed batch fails the boot/harness, never a render.
/// </summary>
public static class ContactJson
{
    public static ContactRegistry Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!document.RootElement.TryGetProperty("contacts", out JsonElement contactsElement)
            || contactsElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Contact registry content must be an object with a 'contacts' array.");
        }

        var contacts = new List<ContactDefinition>();
        foreach (JsonElement element in contactsElement.EnumerateArray())
        {
            contacts.Add(ParseContact(element));
        }
        return new ContactRegistry(contacts);
    }

    private static ContactDefinition ParseContact(JsonElement element)
    {
        string id = RequireString(element, "id", "<unnamed contact>");
        string displayName = RequireString(element, "display_name", id);

        string roleText = RequireString(element, "role", id);
        ContactRole role = roleText switch
        {
            "unknown" => ContactRole.Unknown,
            "girlfriend" => ContactRole.Girlfriend,
            "coach" => ContactRole.Coach,
            "agent" => ContactRole.Agent,
            "dealer" => ContactRole.Dealer,
            "fixer" => ContactRole.Fixer,
            "family" => ContactRole.Family,
            _ => throw new FormatException($"Contact '{id}': unknown role '{roleText}'."),
        };

        string portraitKey = element.TryGetProperty("portrait_key", out JsonElement portraitElement)
            ? portraitElement.GetString() ?? id
            : id;

        return new ContactDefinition(id, displayName, role, portraitKey);
    }

    private static string RequireString(JsonElement element, string property, string contactId)
    {
        if (!element.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Contact '{contactId}': required string property '{property}' is missing.");
        }
        return value.GetString()!;
    }
}
