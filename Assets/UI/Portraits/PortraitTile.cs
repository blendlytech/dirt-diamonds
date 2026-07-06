using System.Text;

namespace DirtAndDiamonds.UI.Portraits;

/// <summary>
/// Phase 10e (presentation_layer_narrative.md §6): the deterministic
/// procedural-fallback math — "a themed initials-on-color tile seeded off
/// player_id, so the same NPC always draws the same fallback." Deliberately
/// Godot-free (the ScoutingGrade precedent) so Tools/MonteCarloHarness can
/// fixture-pin it directly. Uses a custom FNV-1a hash rather than
/// <see cref="string.GetHashCode"/> — .NET randomizes the latter per process
/// for hash-flooding protection, which would make the "fallback" a different
/// color every boot instead of a stable per-identity one.
/// </summary>
public static class PortraitTile
{
    public const int PaletteSize = 8;

    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    /// <summary>A stable [0, PaletteSize) bucket for <paramref name="seedId"/> — the same id always lands on the same bucket, across processes and machines.</summary>
    public static int PaletteIndex(string seedId)
    {
        uint hash = FnvOffsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(seedId))
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return (int)(hash % PaletteSize);
    }

    /// <summary>Up to two uppercase initials from a display name — first letter of the first two words, or the first two letters of a single-word name.</summary>
    public static string Initials(string displayName)
    {
        string[] words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "?";
        }
        if (words.Length == 1)
        {
            string only = words[0];
            return only.Length >= 2 ? only[..2].ToUpperInvariant() : only.ToUpperInvariant();
        }
        return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0]));
    }
}
