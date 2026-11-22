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
/// A written row is acknowledged only after that batch commits; on a
/// rollback it stays dirty for whichever sim flushes next (review R2).
/// **Callers flush sequentially:** one in-flight snapshot is safe only because
/// no two flushes of this shared ledger overlap — <see cref="DatabaseManager"/>
/// refuses nested batches, and the clock is gated while an attended game runs
/// on its worker (<c>CareerManager</c>'s in-flight flag,
/// <c>GameManager.InteractiveGameInFlight</c>), so macro days never flush
/// mid-game. A future slice that flushes concurrently must give each flush
/// its own snapshot first (13-2 review §9.7 observation 1).
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
    // What the last FlushDirty wrote (player → day), acknowledged on commit (13-2 review R2).
    private readonly Dictionary<string, long> _inFlight = new(InitialCapacity, StringComparer.Ordinal);
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

    /// <summary>
    /// What <see cref="RecordStartUndoable"/> overwrote: the pitcher's prior
    /// entry (or none) and whether it was already waiting for a flush.
    /// </summary>
    public readonly record struct StartUndo(string PlayerId, bool HadPrior, long PriorDay, bool WasDirty);

    /// <summary>
    /// <see cref="RecordStart"/> that returns what it overwrote, so a game
    /// that never commits can put the ledger back with <see cref="Revert"/>
    /// (13-4 review S1/N1). Allocation-free.
    /// </summary>
    public StartUndo RecordStartUndoable(string playerId, long day)
    {
        bool hadPrior = _lastStartDay.TryGetValue(playerId, out long prior);
        var undo = new StartUndo(playerId, hadPrior, prior, _dirty.Contains(playerId));
        RecordStart(playerId, day);
        return undo;
    }

    /// <summary>
    /// Restores the entry exactly as it was before the recorded start: the
    /// prior day, or no entry at all for a pitcher who had never started (no
    /// phantom day-0 row), and the prior dirty flag (a clean entry already
    /// matches its Pitcher_Rest row). Revert newest-first when undoing more
    /// than one start. Call only after the start's flush rolled back (or
    /// before any flush), never after a commit.
    /// </summary>
    public void Revert(in StartUndo undo)
    {
        if (undo.HadPrior)
        {
            _lastStartDay[undo.PlayerId] = undo.PriorDay;
        }
        else
        {
            _lastStartDay.Remove(undo.PlayerId);
        }
        if (!undo.WasDirty)
        {
            _dirty.Remove(undo.PlayerId);
        }
    }

    /// <summary>
    /// Writes every changed row through the caller's OPEN batch. The rows
    /// stay dirty: the caller calls <see cref="AcknowledgeFlush"/> only after
    /// its <c>CommitBatch</c> succeeds, so a rolled-back batch leaves them
    /// for the next flush to write again (13-2 review R2). The upsert is
    /// idempotent, so re-writing a row is always safe.
    /// </summary>
    public void FlushDirty(BaseballQueries queries)
    {
        _inFlight.Clear();
        if (_dirty.Count == 0)
        {
            return;
        }
        foreach (string playerId in _dirty)
        {
            long day = _lastStartDay[playerId];
            queries.UpsertPitcherRest(playerId, day);
            _inFlight[playerId] = day;
        }
    }

    /// <summary>
    /// The caller's batch committed: the rows the last <see cref="FlushDirty"/>
    /// wrote are durable, so they stop being dirty. A row whose start moved
    /// after it was written (a newer <see cref="RecordStart"/>) stays dirty.
    /// Call this only after <c>CommitBatch</c> returns; after a rollback,
    /// call nothing (or <see cref="AbandonFlush"/>) and the rows stay dirty.
    /// </summary>
    public void AcknowledgeFlush()
    {
        foreach (KeyValuePair<string, long> written in _inFlight)
        {
            if (_lastStartDay[written.Key] == written.Value)
            {
                _dirty.Remove(written.Key);
            }
        }
        _inFlight.Clear();
    }

    /// <summary>The caller's batch rolled back: forget what the last flush wrote; every row stays dirty.</summary>
    public void AbandonFlush() => _inFlight.Clear();

    /// <summary>Rows recorded since the last acknowledged flush (harness visibility).</summary>
    public int DirtyCount => _dirty.Count;

    public int Count => _lastStartDay.Count;
}
