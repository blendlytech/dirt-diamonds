using System;
using System.Collections.Generic;
using Godot;

namespace DirtAndDiamonds.UI;

/// <summary>
/// Cached loader for the school art paths schools.json declares
/// (SchoolDefinition.LogoPath/SchoolPath/FieldPath) — the PortraitLibrary
/// idiom, keyed by the res:// path itself. Misses are cached too, so a school
/// whose PNGs aren't authored yet (6 of 8 today) costs one existence check
/// ever and simply renders nothing: art arrives school by school with zero
/// code churn, exactly like portraits.
/// </summary>
public static class SchoolArtLibrary
{
    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.Ordinal);

    /// <summary>Resolves an art path to its texture, or false if the file was never authored (the common case today).</summary>
    public static bool TryLoad(string path, out Texture2D texture)
    {
        if (!Cache.TryGetValue(path, out Texture2D? cached))
        {
            cached = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
            Cache[path] = cached;
        }
        texture = cached!;
        return cached is not null;
    }
}
