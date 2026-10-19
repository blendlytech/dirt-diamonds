using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// The last-start day of every pitcher (league_restructure.md §6, slice 13-2):
/// the one memory starter selection reads. Keyed by player id on the ABSOLUTE
/// game day, so rest survives week and year boundaries, and shared by every
/// tier sim and the micro-sim (GameManager wires one instance to all), so the
/// attended game and the background league agree on who is rested.
///
/// Persisted in Pitcher_Rest (schema v14, brief D4): hydrated once, and the
/// rows that changed are written inside whichever sim batch flushes next (the
/// macro 7-day flush or an attended game's flush), so rest can trail by at
/// most what an unflushed stat line can (R-1 B01, fixed by R1-SAVE-1).
///
/// Allocation: the maps are pre-sized, so recording a start on the warm game
/// path never allocates.
/// </summary>
public sealed class PitcherRestLedger
{
    /// <summary>Initial map capacity: comfortably above every v14 world's pitcher count (68 × ≤ 12).</summary>
    public const int InitialCapacity = 4096;

    private readonly Dictionary<string, long> _lastStartDay = new(InitialCapacity, StringComparer.Ordinal);
    private readonly HashSet<string> _dirty = new(InitialCapacity, StringComparer.Ordinal);
    private bool _hydrated;

    /// <summary>Loads Pitcher_Rest once; later calls (every re-Initialize) are no-ops, so in-memory starts are never clobbered by a staler row.</summary>
    public void HydrateOnce(BaseballQueries queries)
    {
        if (_hydrated)
        {
            return;
        }
        _hydrated = true;
        var rows = new List<(string PlayerId, long LastStartDay)>();
        queries.LoadAllPitcherRest(rows);
        foreach ((string playerId, long day) in rows)
        {
            _lastStartDay[playerId] = day;
        }
    }

    /// <summary>The pitcher's last start day; false when he has never started.</summary>
    public bool TryGetLastStart(string playerId, out long day) => _lastStartDay.TryGetValue(playerId, out day);

    /// <summary>Records a start on <paramref name="day"/> (absolute game day) and marks it for the next flush.</summary>
    public void RecordStart(string playerId, long day)
    {
        _lastStartDay[playerId] = day;
        _dirty.Add(playerId);
    }

    /// <summary>Writes every changed row through the caller's OPEN batch, then clears the dirty set.</summary>
    public void FlushDirty(BaseballQueries queries)
    {
        if (_dirty.Count == 0)
        {
            return;
        }
        foreach (string playerId in _dirty)
        {
            queries.UpsertPitcherRest(playerId, _lastStartDay[playerId]);
        }
        _dirty.Clear();
    }

    public int Count => _lastStartDay.Count;
}
