using DirtAndDiamonds.Data;

namespace DirtAndDiamonds.Simulation.Baseball;

/// <summary>
/// Denormalizes the rate columns (AVG/OBP/SLG/OPS on Batting_Stats, IP/ERA/WHIP
/// on Pitching_Stats) from the counting stats after a season flush, so the UI
/// never computes rates at render time. Two set-based UPDATEs in their own
/// batch transaction — it runs after (never inside) the simulator's
/// counting-stat batch.
/// </summary>
public sealed class StatsNormalizer
{
    private readonly DatabaseManager _db;
    private readonly BaseballQueries _queries;

    public StatsNormalizer(DatabaseManager db, BaseballQueries queries)
    {
        _db = db;
        _queries = queries;
    }

    /// <summary>Recomputes every rate column for one season. Returns rows touched.</summary>
    public int NormalizeSeason(int seasonYear)
    {
        int rows = 0;
        _db.BeginBatch();
        try
        {
            rows += _queries.NormalizeBattingRates(seasonYear);
            rows += _queries.NormalizePitchingRates(seasonYear);
            _db.CommitBatch();
        }
        catch
        {
            _db.RollbackBatch();
            throw;
        }
        return rows;
    }
}
