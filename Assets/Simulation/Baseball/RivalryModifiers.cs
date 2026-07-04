using DirtAndDiamonds.Core;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One active rivalry as the ledger hands it to a sim's cache rebuild.</summary>
public readonly struct RivalryPair
{
    public readonly string PlayerAId;
    public readonly string PlayerBId;
    public readonly byte Intensity;

    public RivalryPair(string playerAId, string playerBId, byte intensity)
    {
        PlayerAId = playerAId;
        PlayerBId = playerBId;
        Intensity = intensity;
    }
}

/// <summary>
/// The Baseball sim's view of active rivalries (BUILD_PLAN Phase 6). Fed
/// exclusively by <see cref="RivalryChangedEvent"/> off the bus — this class
/// never references the Life sim, mirroring how the whole assembly's only
/// inputs are Core events and the database.
///
/// Consumers (LeagueSimulator, MicroGame) never query per PA: they watch
/// <see cref="Version"/> and rebuild a flat slot×slot intensity cache only
/// when it moves, keeping the per-PA hot path to one array read (zero-GC
/// mandate). All mutation happens on the dispatch thread; reads happen on the
/// same sim thread, so no lock is needed.
/// </summary>
public sealed class RivalryLedger
{
    private readonly Dictionary<(string, string), byte> _pairs = new();
    private readonly Action<RivalryChangedEvent> _onRivalryChanged;

    /// <summary>Bumped on every effective change; consumers refresh caches when it moves.</summary>
    public int Version { get; private set; }

    public int Count => _pairs.Count;

    public RivalryLedger()
    {
        _onRivalryChanged = OnRivalryChanged;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onRivalryChanged);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onRivalryChanged);

    public byte GetIntensity(string playerAId, string playerBId) =>
        _pairs.TryGetValue(CanonicalKey(playerAId, playerBId), out byte intensity) ? intensity : (byte)0;

    /// <summary>Fills <paramref name="destination"/> (cleared first) with every active rivalry.</summary>
    public int CopyPairs(List<RivalryPair> destination)
    {
        destination.Clear();
        foreach (KeyValuePair<(string, string), byte> entry in _pairs)
        {
            destination.Add(new RivalryPair(entry.Key.Item1, entry.Key.Item2, entry.Value));
        }
        return destination.Count;
    }

    private void OnRivalryChanged(RivalryChangedEvent e)
    {
        (string, string) key = CanonicalKey(e.PlayerAId, e.PlayerBId);
        if (e.Intensity == 0)
        {
            if (_pairs.Remove(key))
            {
                Version++;
            }
            return;
        }
        if (_pairs.TryGetValue(key, out byte existing) && existing == e.Intensity)
        {
            return;
        }
        _pairs[key] = e.Intensity;
        Version++;
    }

    // The publisher already canonicalizes; re-canonicalizing here costs one
    // comparison and makes the ledger safe against any future publisher.
    private static (string, string) CanonicalKey(string a, string b) =>
        string.CompareOrdinal(a, b) > 0 ? (b, a) : (a, b);
}

/// <summary>
/// How a rivalry bends a plate appearance — the Phase 6 "rivalry scores feed
/// baseball probability modifiers" hook. Follows the PED/fatigue precedent
/// exactly: adjust the effective ratings fed into the UNCHANGED
/// <see cref="AtBatResolver"/>, never its calibration tables.
///
/// Both men key up: the batter gains power, the pitcher gains stuff, each
/// scaled linearly by intensity. Through the §4.1 matchup weights that is a
/// three-true-outcomes shift — strikeouts and home runs both rise, singles
/// dip — with the league line essentially unmoved because rival pairs are
/// sparse and the boosts partially offset (harness-proven via a
/// delta-vs-control bound, not absolute §8 bands — a heavily rivalrous
/// season is EXPECTED to diverge from the neutral-league acceptance ranges).
///
/// Order of operations with PED: the rivalry boost lands on the raw rating;
/// the resolver then applies its own clamped 1.5× PED multiplier on top, the
/// same way it would for any rating source.
/// </summary>
public static class RivalryEffects
{
    /// <summary>Batter power boost at intensity 100 (+16 rating ⇒ +0.32 deviation).</summary>
    public const int MaxPowerBoost = 16;

    /// <summary>Pitcher stuff boost at intensity 100.</summary>
    public const int MaxStuffBoost = 16;

    public static BatterRatings Batter(in BatterRatings batter, byte intensity) =>
        new(BoostedRating(batter.Power, MaxPowerBoost, intensity), batter.Contact, batter.Discipline, batter.PedActive);

    public static PitcherRatings Pitcher(in PitcherRatings pitcher, byte intensity) =>
        new(BoostedRating(pitcher.Stuff, MaxStuffBoost, intensity), pitcher.Control, pitcher.Stamina);

    /// <summary>round-half-up of max·(intensity/100), clamped into the 0–100 rating scale.</summary>
    private static byte BoostedRating(byte rating, int maxBoost, byte intensity) =>
        (byte)Math.Min(100, rating + (maxBoost * intensity + 50) / 100);
}
