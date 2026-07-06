using Godot;

namespace DirtAndDiamonds.UI.Portraits;

/// <summary>
/// Phase 10e (presentation_layer_narrative.md §6): the pre-generated-asset
/// half of the portrait pipeline. Assets are authored for the avatar,
/// heirs, and named contacts only (the disclosed budget) — everyone else
/// resolves to <see cref="PortraitTile"/>'s procedural fallback, so a
/// missing file here is the expected, common case, not an error.
/// Lazy-load + cache per the doc's "loading discipline": a key's existence
/// is checked and its texture loaded at most once per process, never
/// touched again (and never per-frame) regardless of how many
/// <see cref="PortraitView"/> instances ask for it.
/// </summary>
public static class PortraitLibrary
{
    /// <summary>Drop a "{key}.png" here (key = player_id for the avatar/heirs, portrait_key for contacts) to replace the procedural fallback with real art. Empty today — no assets authored yet.</summary>
    public const string BasePath = "res://Assets/UI/Portraits/Art/";

    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.Ordinal);

    /// <summary>Resolves a key to its pre-generated portrait texture, or false if none was ever authored for it (the common case today).</summary>
    public static bool TryLoad(string key, out Texture2D texture)
    {
        if (!Cache.TryGetValue(key, out Texture2D? cached))
        {
            string path = $"{BasePath}{key}.png";
            cached = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
            Cache[key] = cached;
        }
        texture = cached!;
        return cached is not null;
    }
}
