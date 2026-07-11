using System;
using System.Collections.Generic;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Data.Schools;

/// <summary>
/// One school color: a player-facing name plus a validated <c>#RRGGBB</c> hex.
/// Deliberately Godot-free (stored as a string, not a <c>Godot.Color</c>) so the
/// catalog compiles headless in the harness exactly like <c>ItemDefinition</c>;
/// the UI converts <see cref="Hex"/> via <c>Color.FromHtml</c> at render time.
/// </summary>
public readonly struct SchoolColor
{
    public readonly string Name;

    /// <summary>Uppercase <c>#RRGGBB</c>; format is enforced loudly at parse (SchoolCatalogJson).</summary>
    public readonly string Hex;

    public SchoolColor(string name, string hex)
    {
        Name = name;
        Hex = hex;
    }
}

/// <summary>
/// One high-school branding entry (Assets/Data/Schools/schools.json). Immutable
/// content loaded once per session — the school and its baseball team share one
/// mascot, so <see cref="TeamId"/> keys a join to <c>Teams(team_id)</c> for the
/// HS tier (101–108) without being an FK. Pure display flavor, the same
/// "content, not schema" contract as <c>ItemDefinition</c>.
/// </summary>
public sealed class SchoolDefinition
{
    /// <summary>Matches <c>Teams.team_id</c> for the school's HS-tier baseball team.</summary>
    public readonly int TeamId;

    /// <summary>Cross-checked against <c>Teams.abbreviation</c> at boot — copy drift fails loudly.</summary>
    public readonly string Abbreviation;

    /// <summary>Player-facing school name (e.g. "Crestwood High School") — copy lives in content, not C#.</summary>
    public readonly string SchoolName;

    /// <summary>Mirrors <c>Teams.name</c>; the school and its team share this mascot.</summary>
    public readonly string Mascot;

    /// <summary>Path to the logo image (e.g. "res://Assets/Graphics/HighSchools/CRW_logo.png").</summary>
    public readonly string LogoPath;
    
    /// <summary>Path to the school exterior image (e.g. "res://Assets/Graphics/HighSchools/CRW_school.png").</summary>
    public readonly string SchoolPath;
    
    /// <summary>Path to the baseball field image (e.g. "res://Assets/Graphics/HighSchools/CRW_field.png").</summary>
    public readonly string FieldPath;

    public readonly SchoolColor Primary;
    public readonly SchoolColor Secondary;

    /// <summary>Trim/tertiary color; falls back to <see cref="Secondary"/> when a row omits it.</summary>
    public readonly SchoolColor Accent;

    /// <summary>One-line palette copy for scouting/UI.</summary>
    public readonly string PaletteDescription;

    /// <summary>Program-identity copy for menus and rival chatter.</summary>
    public readonly string Flavor;

    public SchoolDefinition(
        int teamId, string abbreviation, string schoolName, string mascot,
        string logoPath, string schoolPath, string fieldPath,
        SchoolColor primary, SchoolColor secondary, SchoolColor accent,
        string paletteDescription, string flavor)
    {
        TeamId = teamId;
        Abbreviation = abbreviation;
        SchoolName = schoolName;
        Mascot = mascot;
        LogoPath = logoPath;
        SchoolPath = schoolPath;
        FieldPath = fieldPath;
        Primary = primary;
        Secondary = secondary;
        Accent = accent;
        PaletteDescription = paletteDescription;
        Flavor = flavor;
    }

    /// <summary>Picker/label copy: "Crestwood High School — Cardinals".</summary>
    public string DisplayName => $"{SchoolName} — {Mascot}";
}

/// <summary>
/// The loaded school catalog — content, not schema: <c>Teams.team_id</c> carries
/// no FK to this file, so this class carries the validation the database cannot.
/// The constructor enforces structural integrity (unique team ids and
/// abbreviations); <see cref="ValidateAgainstRoster"/> is the loud boot-time
/// cross-check GameManager runs against the live HS roster (a missing school,
/// an orphan entry, or an abbreviation mismatch fails the boot, never a render)
/// — the <c>ItemCatalog.ValidateOwnership</c> precedent. Lookup is by team id;
/// <see cref="Entries"/> preserves authoring order.
/// </summary>
public sealed class SchoolCatalog
{
    private readonly Dictionary<int, SchoolDefinition> _byTeamId;
    private readonly List<SchoolDefinition> _entries;

    public SchoolCatalog(List<SchoolDefinition> entries)
    {
        _entries = entries;
        _byTeamId = new Dictionary<int, SchoolDefinition>(entries.Count);
        var seenAbbr = new HashSet<string>(entries.Count, StringComparer.Ordinal);
        foreach (SchoolDefinition entry in entries)
        {
            if (!_byTeamId.TryAdd(entry.TeamId, entry))
            {
                throw new FormatException($"School catalog: duplicate team_id {entry.TeamId}.");
            }
            if (!seenAbbr.Add(entry.Abbreviation))
            {
                throw new FormatException($"School catalog: duplicate abbreviation '{entry.Abbreviation}'.");
            }
        }
    }

    public int Count => _entries.Count;

    /// <summary>All entries in authoring order.</summary>
    public IReadOnlyList<SchoolDefinition> Entries => _entries;

    public bool TryGet(int teamId, out SchoolDefinition definition) =>
        _byTeamId.TryGetValue(teamId, out definition!);

    /// <summary>Lookup that throws on a missing team id — for callers holding an already-validated HS team row.</summary>
    public SchoolDefinition Require(int teamId) =>
        _byTeamId.TryGetValue(teamId, out SchoolDefinition? definition)
            ? definition
            : throw new InvalidOperationException($"School catalog has no entry for team_id {teamId}.");

    /// <summary>
    /// The loud boot-time cross-check: every HS-tier team must have exactly one
    /// school entry whose abbreviation agrees with the roster, and no school
    /// entry may reference a team_id absent from the HS roster (an orphan left
    /// behind by a roster edit). Both directions are checked so a rename in
    /// either file surfaces at boot with the offending id spelled out.
    /// </summary>
    public void ValidateAgainstRoster(IReadOnlyList<TeamRow> hsTeams)
    {
        var rosterIds = new HashSet<int>(hsTeams.Count);
        for (int i = 0; i < hsTeams.Count; i++)
        {
            TeamRow team = hsTeams[i];
            rosterIds.Add(team.TeamId);
            if (!_byTeamId.TryGetValue(team.TeamId, out SchoolDefinition? school))
            {
                throw new InvalidOperationException(
                    $"HS team {team.TeamId} ({team.City} {team.Name}) has no entry in schools.json — every HS-tier team needs a school.");
            }
            if (!string.Equals(school.Abbreviation, team.Abbreviation, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"schools.json team {team.TeamId} stores abbreviation '{school.Abbreviation}' but the roster says '{team.Abbreviation}' — the two files disagree.");
            }
        }
        foreach (SchoolDefinition entry in _entries)
        {
            if (!rosterIds.Contains(entry.TeamId))
            {
                throw new InvalidOperationException(
                    $"schools.json entry for team_id {entry.TeamId} ('{entry.SchoolName}') has no matching HS-tier team — orphaned school entry.");
            }
        }
    }
}
