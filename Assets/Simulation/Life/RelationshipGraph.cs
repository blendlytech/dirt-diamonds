using System;
using System.Collections.Generic;
using DirtAndDiamonds.Core;

namespace DirtAndDiamonds.Simulation.Life;

/// <summary>
/// Relationship categories, mirroring the Relationships.type_enum CHECK list.
/// Deliberately a separate enum from Data's RelationshipType: this folder is
/// compiled standalone by Tools/NeedsDecayHarness and must stay Data-free, so
/// GameManager maps between the two at the persistence boundary.
/// </summary>
public enum RelationshipKind : byte
{
    Rival,
    Friend,
    Partner,
    Child,
}

/// <summary>
/// One edge of the graph as a plain record — the hydration/flush unit the
/// persistence bridge (GameManager) projects to and from RelationshipRow,
/// exactly how NpcSeed decouples LifeSimManager from PlayerQueries.
/// </summary>
public readonly record struct RelationshipSeed(
    string PlayerAId, string PlayerBId, int Affinity, RelationshipKind Kind);

/// <summary>One player's view of an edge, for adjacency queries.</summary>
public readonly struct RelationshipEdge
{
    public readonly string OtherId;
    public readonly int Affinity;
    public readonly RelationshipKind Kind;

    public RelationshipEdge(string otherId, int affinity, RelationshipKind kind)
    {
        OtherId = otherId;
        Affinity = affinity;
        Kind = kind;
    }
}

/// <summary>
/// Bidirectional affinity graph backed by the Relationships table (BUILD_PLAN
/// Phase 6). One edge per unordered pair, canonicalized to the same ordinal
/// player_1_id &lt; player_2_id ordering the query layer enforces, so a flush
/// upserts exactly the row hydration read.
///
/// Rivalry transport: whenever an edge's rivalry intensity changes (a Rival
/// edge's negative affinity, 0–100), the graph publishes
/// <see cref="RivalryChangedEvent"/> on the attached bus. The Baseball sim
/// consumes those events through its own ledger — per CLAUDE.md the two sims
/// never reference each other, and this class publishes only, never
/// subscribes.
///
/// Nothing in the live game mutates affinities yet (the writers are Phase 7
/// Gritty Events and the heir mechanics) — same precedent as the PED flag,
/// which shipped seasons before its writer. Until then edges are
/// DB-authorable and harness-driven.
/// </summary>
public sealed class RelationshipGraph
{
    public const int MinAffinity = -100;
    public const int MaxAffinity = 100;

    private struct EdgeData
    {
        public int Affinity;
        public RelationshipKind Kind;
    }

    // Canonical (ordinal-smaller, ordinal-larger) pair -> edge. ValueTuple keys
    // hash both strings ordinally with no per-lookup allocation.
    private readonly Dictionary<(string, string), EdgeData> _edges = new();

    // Adjacency: player id -> canonical keys of every edge touching them.
    private readonly Dictionary<string, List<(string, string)>> _adjacency = new(StringComparer.Ordinal);

    // Pairs mutated since the last CollectDirty — the persistence bridge's
    // flush set. Hydration (Seed) never dirties: those rows came FROM the DB.
    private readonly HashSet<(string, string)> _dirty = new();

    private EventBus? _bus;

    public int EdgeCount => _edges.Count;

    /// <summary>
    /// The §modifier derivation: only a Rival edge in the red projects onto
    /// baseball, scaled by how deep the animosity runs. Positive-affinity
    /// rivals (grudging respect) and every other kind contribute 0.
    /// </summary>
    public static byte RivalryIntensity(int affinity, RelationshipKind kind) =>
        kind == RelationshipKind.Rival && affinity < 0
            ? (byte)Math.Min(MaxAffinity, -affinity)
            : (byte)0;

    /// <summary>
    /// Boot-time hydration. Idempotent like LifeSimManager.Seed: a pair already
    /// tracked is skipped, so a mid-game re-projection is safe. Publishes
    /// nothing and dirties nothing — attach the bus afterwards and
    /// <see cref="AttachTo"/> announces the hydrated rivalries.
    /// </summary>
    public void Seed(IReadOnlyList<RelationshipSeed> seeds)
    {
        for (int i = 0; i < seeds.Count; i++)
        {
            RelationshipSeed seed = seeds[i];
            (string, string) key = CanonicalKey(seed.PlayerAId, seed.PlayerBId);
            if (_edges.ContainsKey(key))
            {
                continue;
            }
            _edges.Add(key, new EdgeData { Affinity = Clamp(seed.Affinity), Kind = seed.Kind });
            AddAdjacency(key);
        }
    }

    /// <summary>
    /// Starts publishing rivalry changes through <paramref name="bus"/> and
    /// immediately announces every currently active rivalry, so a subscriber
    /// wired at boot needs no separate hydration handshake with this graph.
    /// </summary>
    public void AttachTo(EventBus bus)
    {
        _bus = bus;
        foreach (KeyValuePair<(string, string), EdgeData> entry in _edges)
        {
            byte intensity = RivalryIntensity(entry.Value.Affinity, entry.Value.Kind);
            if (intensity > 0)
            {
                bus.Publish(new RivalryChangedEvent(entry.Key.Item1, entry.Key.Item2, intensity));
            }
        }
    }

    public void DetachFrom(EventBus bus)
    {
        if (ReferenceEquals(_bus, bus))
        {
            _bus = null;
        }
    }

    /// <summary>
    /// Creates or overwrites the edge for an unordered pair. Affinity is
    /// clamped to [-100, 100]; a change in rivalry intensity (including a
    /// dissolve to 0 via kind change or affinity sign flip) is published.
    /// </summary>
    public void SetRelationship(string playerAId, string playerBId, int affinity, RelationshipKind kind)
    {
        (string, string) key = CanonicalKey(playerAId, playerBId);
        int clamped = Clamp(affinity);

        byte oldIntensity = 0;
        if (_edges.TryGetValue(key, out EdgeData existing))
        {
            if (existing.Affinity == clamped && existing.Kind == kind)
            {
                return; // no-op write: nothing dirties, nothing publishes
            }
            oldIntensity = RivalryIntensity(existing.Affinity, existing.Kind);
        }
        else
        {
            AddAdjacency(key);
        }

        _edges[key] = new EdgeData { Affinity = clamped, Kind = kind };
        _dirty.Add(key);

        byte newIntensity = RivalryIntensity(clamped, kind);
        if (newIntensity != oldIntensity)
        {
            _bus?.Publish(new RivalryChangedEvent(key.Item1, key.Item2, newIntensity));
        }
    }

    /// <summary>
    /// Clamped affinity delta on an existing edge; false when the pair has no
    /// edge (relationship creation is an explicit act — use SetRelationship).
    /// </summary>
    public bool AdjustAffinity(string playerAId, string playerBId, int delta)
    {
        (string, string) key = CanonicalKey(playerAId, playerBId);
        if (!_edges.TryGetValue(key, out EdgeData edge))
        {
            return false;
        }
        SetRelationship(key.Item1, key.Item2, edge.Affinity + delta, edge.Kind);
        return true;
    }

    public bool TryGetRelationship(string playerAId, string playerBId, out int affinity, out RelationshipKind kind)
    {
        if (_edges.TryGetValue(CanonicalKey(playerAId, playerBId), out EdgeData edge))
        {
            affinity = edge.Affinity;
            kind = edge.Kind;
            return true;
        }
        affinity = 0;
        kind = default;
        return false;
    }

    /// <summary>Fills <paramref name="destination"/> (cleared first) with every edge touching a player.</summary>
    public int GetEdgesFor(string playerId, List<RelationshipEdge> destination)
    {
        destination.Clear();
        if (!_adjacency.TryGetValue(playerId, out List<(string, string)>? keys))
        {
            return 0;
        }
        for (int i = 0; i < keys.Count; i++)
        {
            (string, string) key = keys[i];
            EdgeData edge = _edges[key];
            string other = string.Equals(key.Item1, playerId, StringComparison.Ordinal) ? key.Item2 : key.Item1;
            destination.Add(new RelationshipEdge(other, edge.Affinity, edge.Kind));
        }
        return destination.Count;
    }

    /// <summary>
    /// Persistence bridge: fills <paramref name="destination"/> (cleared
    /// first) with every edge mutated since the previous call, then resets the
    /// dirty set. GameManager upserts these through PlayerQueries on the same
    /// cadence it flushes needs.
    /// </summary>
    public int CollectDirty(List<RelationshipSeed> destination)
    {
        destination.Clear();
        foreach ((string, string) key in _dirty)
        {
            EdgeData edge = _edges[key];
            destination.Add(new RelationshipSeed(key.Item1, key.Item2, edge.Affinity, edge.Kind));
        }
        _dirty.Clear();
        return destination.Count;
    }

    private void AddAdjacency((string, string) key)
    {
        Adjacent(key.Item1).Add(key);
        Adjacent(key.Item2).Add(key);
    }

    private List<(string, string)> Adjacent(string playerId)
    {
        if (!_adjacency.TryGetValue(playerId, out List<(string, string)>? list))
        {
            list = new List<(string, string)>(4);
            _adjacency.Add(playerId, list);
        }
        return list;
    }

    private static (string, string) CanonicalKey(string a, string b)
    {
        if (string.CompareOrdinal(a, b) > 0)
        {
            return (b, a);
        }
        if (string.CompareOrdinal(a, b) == 0)
        {
            throw new ArgumentException($"A player cannot have a relationship with themselves ({a}).");
        }
        return (a, b);
    }

    private static int Clamp(int affinity) => Math.Clamp(affinity, MinAffinity, MaxAffinity);
}
