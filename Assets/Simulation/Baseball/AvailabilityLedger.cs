using DirtAndDiamonds.Core;
using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>A slot's availability on a given day, as the sims' caches consume it.</summary>
public enum SlotAvailability : byte
{
    Available = 0,

    /// <summary>Out of the game entirely — a replacement-level call-up shadows the slot.</summary>
    Absent = 1,

    /// <summary>Playing, but with effective ratings reduced by the injury-rust penalty.</summary>
    Rusty = 2,
}

/// <summary>One tracked absence as the ledger hands it to a sim's cache rebuild.</summary>
public readonly struct AbsenceEntry
{
    public readonly string PlayerId;
    public readonly AbsenceReason Reason;
    public readonly long UntilDay;
    public readonly byte RatingPenalty;
    public readonly long PenaltyUntilDay;

    public AbsenceEntry(string playerId, AbsenceReason reason, long untilDay, byte ratingPenalty, long penaltyUntilDay)
    {
        PlayerId = playerId;
        Reason = reason;
        UntilDay = untilDay;
        RatingPenalty = ratingPenalty;
        PenaltyUntilDay = penaltyUntilDay;
    }

    /// <summary>This entry's effect on the given absolute day.</summary>
    public SlotAvailability StateOn(long day)
    {
        if (day < UntilDay)
        {
            return SlotAvailability.Absent;
        }
        if (RatingPenalty > 0 && day < PenaltyUntilDay)
        {
            return SlotAvailability.Rusty;
        }
        return SlotAvailability.Available;
    }
}

/// <summary>
/// The Baseball sims' view of who is out of games (Phase 8c roster
/// availability — arrest/injury/suspension). Fed exclusively by
/// <see cref="PlayerAbsenceChangedEvent"/> off the bus (the RivalryLedger
/// transport pattern: the Narrative applier writes Player_Absences in its own
/// batch, then publishes; this class never references the Life sim or opens a
/// connection) plus a boot-time <see cref="Seed"/> from the persisted rows.
///
/// Consumers (LeagueSimulator, MicroGame, CareerManager) never query per PA:
/// they watch <see cref="Version"/> AND the current day (an absence expires by
/// the calendar moving, not by any event) and rebuild flat per-slot caches
/// only when either moves — the per-PA hot path stays array reads (zero-GC
/// mandate). All mutation happens on the dispatch thread; reads happen on the
/// same sim thread, so no lock is needed.
///
/// Merge rule, identical to the SQL upsert's: one absence per player, and a
/// new absence wins only when it ends no earlier than the tracked one —
/// overlapping absences never stack (disclosed simplification).
/// </summary>
public sealed class AvailabilityLedger
{
    private readonly Dictionary<string, AbsenceEntry> _entries = new(StringComparer.Ordinal);
    private readonly Action<PlayerAbsenceChangedEvent> _onAbsenceChanged;

    /// <summary>Bumped on every effective change; consumers refresh caches when it (or the day) moves.</summary>
    public int Version { get; private set; }

    public int Count => _entries.Count;

    public AvailabilityLedger()
    {
        _onAbsenceChanged = OnAbsenceChanged;
    }

    public void AttachTo(EventBus bus) => bus.Subscribe(_onAbsenceChanged);

    public void DetachFrom(EventBus bus) => bus.Unsubscribe(_onAbsenceChanged);

    /// <summary>
    /// Boot-time hydration from the persisted Player_Absences rows — silent
    /// (no version churn matters at boot; consumers haven't cached yet).
    /// Rows the SQL scan already deemed inert are still merged safely.
    /// </summary>
    public void Seed(IReadOnlyList<PlayerAbsenceRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            PlayerAbsenceRow row = rows[i];
            Merge(new AbsenceEntry(
                row.PlayerId, row.Reason, row.UntilDay, (byte)Math.Clamp(row.RatingPenalty, 0, 100), row.PenaltyUntilDay));
        }
    }

    /// <summary>The player's availability on the given day (Available when untracked).</summary>
    public SlotAvailability StateFor(string playerId, long day) =>
        _entries.TryGetValue(playerId, out AbsenceEntry entry) ? entry.StateOn(day) : SlotAvailability.Available;

    /// <summary>The tracked absence for a player, if any (expired entries included — callers filter by day).</summary>
    public bool TryGet(string playerId, out AbsenceEntry entry) => _entries.TryGetValue(playerId, out entry);

    /// <summary>
    /// Fills <paramref name="destination"/> (cleared first) with every entry
    /// still in effect on <paramref name="day"/> — absent or rusty. Expired
    /// entries are skipped, not pruned (the dictionary is bounded by one entry
    /// per ever-absent player; pruning would bump nothing and save nothing).
    /// </summary>
    public int CopyActive(long day, List<AbsenceEntry> destination)
    {
        destination.Clear();
        foreach (KeyValuePair<string, AbsenceEntry> pair in _entries)
        {
            if (pair.Value.StateOn(day) != SlotAvailability.Available)
            {
                destination.Add(pair.Value);
            }
        }
        return destination.Count;
    }

    private void OnAbsenceChanged(PlayerAbsenceChangedEvent e)
    {
        if (Merge(new AbsenceEntry(
            e.PlayerId, (AbsenceReason)e.Reason, e.UntilDay, e.RatingPenalty, e.PenaltyUntilDay)))
        {
            Version++;
        }
    }

    /// <summary>The SQL upsert's keep-later rule, mirrored exactly: replace wholesale iff the new absence ends no earlier.</summary>
    private bool Merge(in AbsenceEntry entry)
    {
        if (_entries.TryGetValue(entry.PlayerId, out AbsenceEntry existing) && entry.UntilDay < existing.UntilDay)
        {
            return false;
        }
        _entries[entry.PlayerId] = entry;
        return true;
    }
}
