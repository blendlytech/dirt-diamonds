using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>One equipped player as the ledger hands it to a sim's cache rebuild.</summary>
public readonly struct EquipmentEntry
{
    public readonly string PlayerId;
    public readonly byte Quality;

    public EquipmentEntry(string playerId, byte quality)
    {
        PlayerId = playerId;
        Quality = quality;
    }
}

/// <summary>
/// The Baseball sims' view of who owns upgraded gear (Phase 8e equipment
/// quality). Fed exclusively by <see cref="PlayerEquipmentChangedEvent"/> off
/// the bus (the RivalryLedger transport pattern: the Economy service writes
/// Player_Equipment in its own batch, then publishes; this class never
/// references the Life sim or opens a connection) plus a boot-time
/// <see cref="Seed"/> from the persisted rows.
///
/// Consumers (LeagueSimulator, MicroGame) never query per PA: they watch
/// <see cref="Version"/> and rebuild flat per-slot boost caches only when it
/// moves — gear never expires by calendar (unlike absences), so Version alone
/// gates the rebuild and the per-PA hot path stays array reads (zero-GC
/// mandate). All mutation happens on the dispatch thread; reads happen on the
/// same sim thread, so no lock is needed.
///
/// Merge rule, identical to the SQL upsert's: one quality per player,
/// keep-higher — a same-or-lower quality is a wholesale no-op, so mirror and
/// row can never disagree without a read-back.
/// </summary>
public sealed class EquipmentLedger
{
    private readonly Dictionary<string, byte> _quality = new(StringComparer.Ordinal);
    private readonly Action<PlayerEquipmentChangedEvent> _onEquipmentChanged;

    /// <summary>Bumped on every effective change; consumers refresh caches when it moves.</summary>
    public int Version { get; private set; }

    public int Count => _quality.Count;

    public EquipmentLedger()
    {
        _onEquipmentChanged = OnEquipmentChanged;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onEquipmentChanged);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onEquipmentChanged);

    /// <summary>
    /// Boot-time hydration from the persisted Player_Equipment rows — silent
    /// (no version churn matters at boot; consumers haven't cached yet).
    /// </summary>
    public void Seed(IReadOnlyList<PlayerEquipmentRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            PlayerEquipmentRow row = rows[i];
            Merge(row.PlayerId, (byte)Math.Clamp(row.Quality, 0, EquipmentEffects.MaxQuality));
        }
    }

    /// <summary>The player's owned gear quality (0 = standard issue, untracked).</summary>
    public byte QualityFor(string playerId) =>
        _quality.TryGetValue(playerId, out byte quality) ? quality : (byte)0;

    /// <summary>Fills <paramref name="destination"/> (cleared first) with every equipped player.</summary>
    public int CopyAll(List<EquipmentEntry> destination)
    {
        destination.Clear();
        foreach (KeyValuePair<string, byte> pair in _quality)
        {
            destination.Add(new EquipmentEntry(pair.Key, pair.Value));
        }
        return destination.Count;
    }

    private void OnEquipmentChanged(PlayerEquipmentChangedEvent e)
    {
        if (Merge(e.PlayerId, e.Quality))
        {
            Version++;
        }
    }

    /// <summary>The SQL upsert's keep-higher rule, mirrored exactly: a same-or-lower quality is a no-op.</summary>
    private bool Merge(string playerId, byte quality)
    {
        if (quality == 0 || (_quality.TryGetValue(playerId, out byte existing) && quality <= existing))
        {
            return false;
        }
        _quality[playerId] = quality;
        return true;
    }
}
