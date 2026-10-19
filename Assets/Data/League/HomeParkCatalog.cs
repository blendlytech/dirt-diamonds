using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DirtAndDiamonds.Data.League;

/// <summary>
/// The loaded Assets/Data/League/home_parks.json (13-1a, Gemini 3.8 Flash;
/// reviewed by Claude Opus): one fictional home park per Appendix A team. Pure
/// content, like schools.json. World-gen writes the names into Teams.home_park
/// (schema v14), so the catalog is read once, at world-gen, and the G-7 venue
/// catalog replaces it later.
///
/// The <c>SchoolCatalogJson</c> discipline: a hand-walked JsonDocument, no
/// reflection serializers, and every error loud and labelled with its team_id.
/// <see cref="ValidateAgainst"/> cross-checks the file against
/// <see cref="LeagueStructure.Teams"/> (every team exactly once, matching
/// abbreviations, no orphans), so a content edit that breaks the join fails
/// the boot or the harness, never a broadcast intro.
/// </summary>
public sealed class HomeParkCatalog
{
    private readonly Dictionary<int, (string Abbreviation, string HomePark)> _byTeamId;

    private HomeParkCatalog(Dictionary<int, (string, string)> byTeamId)
    {
        _byTeamId = byTeamId;
    }

    public int Count => _byTeamId.Count;

    public static HomeParkCatalog Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!document.RootElement.TryGetProperty("parks", out JsonElement parks)
            || parks.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Home park catalog must be an object with a 'parks' array.");
        }

        var byTeamId = new Dictionary<int, (string, string)>(parks.GetArrayLength());
        var names = new HashSet<string>(parks.GetArrayLength(), StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement park in parks.EnumerateArray())
        {
            if (!park.TryGetProperty("team_id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt32(out int teamId))
            {
                throw new FormatException("Home park catalog: a row is missing an integer 'team_id'.");
            }
            string abbreviation = RequireString(park, "abbreviation", teamId);
            string homePark = RequireString(park, "home_park", teamId);
            if (!byTeamId.TryAdd(teamId, (abbreviation, homePark)))
            {
                throw new FormatException($"Home park catalog: duplicate team_id {teamId}.");
            }
            if (!names.Add(homePark))
            {
                throw new FormatException($"Home park catalog: team {teamId} reuses the park name '{homePark}'.");
            }
        }
        return new HomeParkCatalog(byTeamId);
    }

    /// <summary>
    /// Fails loudly unless the catalog holds exactly one row per seed, with the
    /// seed's abbreviation, and nothing else.
    /// </summary>
    public void ValidateAgainst(IReadOnlyList<TeamSeed> seeds)
    {
        for (int i = 0; i < seeds.Count; i++)
        {
            TeamSeed seed = seeds[i];
            if (!_byTeamId.TryGetValue(seed.TeamId, out (string Abbreviation, string HomePark) row))
            {
                throw new InvalidOperationException(
                    $"Team {seed.TeamId} ({seed.City} {seed.Name}) has no row in home_parks.json — every team needs a home park.");
            }
            if (!string.Equals(row.Abbreviation, seed.Abbreviation, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"home_parks.json team {seed.TeamId} stores abbreviation '{row.Abbreviation}' but the league says '{seed.Abbreviation}' — the two disagree.");
            }
        }
        if (_byTeamId.Count != seeds.Count)
        {
            throw new InvalidOperationException(
                $"home_parks.json has {_byTeamId.Count} rows for {seeds.Count} teams — it holds a team the league doesn't.");
        }
    }

    /// <summary>The team_id → park map world-gen consumes (<c>LeagueGenerator.GenerateIfEmpty</c>).</summary>
    public IReadOnlyDictionary<int, string> ToHomeParkMap()
    {
        var map = new Dictionary<int, string>(_byTeamId.Count);
        foreach (KeyValuePair<int, (string Abbreviation, string HomePark)> entry in _byTeamId)
        {
            map.Add(entry.Key, entry.Value.HomePark);
        }
        return map;
    }

    private static string RequireString(JsonElement element, string property, int teamId)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Home park catalog: team {teamId} is missing a non-empty '{property}'.");
        }
        return value.GetString()!;
    }
}
