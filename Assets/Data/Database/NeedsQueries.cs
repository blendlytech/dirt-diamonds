using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>Row DTO mirroring Life_Needs one-to-one (schema v5).</summary>
public struct NeedsRow
{
    public string PlayerId;
    public float Hunger;
    public float Sleep;
    public float Hygiene;
    public float Social;
    public float Fitness;
}

/// <summary>
/// Typed query surface for Life_Needs. Same discipline as <see cref="PlayerQueries"/>:
/// compile-time-constant SQL, commands pooled and prepared once, per-call work
/// limited to parameter values. Deliberately dealing only in <see cref="NeedsRow"/>
/// (not <c>NeedsState</c>) so this class stays a plain Data-layer DTO surface —
/// LifeSimManager itself stays Data-free (Tools/NeedsDecayHarness compiles it
/// standalone); GameManager is the caller-side bridge that converts between the
/// two shapes, exactly like it already bridges PlayerRow → NpcSeed.
/// </summary>
public sealed class NeedsQueries
{
    private const string SqlUpsert =
        "INSERT INTO Life_Needs (player_id, hunger, sleep, hygiene, social, fitness) VALUES " +
        "(@playerId, @hunger, @sleep, @hygiene, @social, @fitness) " +
        "ON CONFLICT (player_id) DO UPDATE SET hunger = excluded.hunger, sleep = excluded.sleep, " +
        "hygiene = excluded.hygiene, social = excluded.social, fitness = excluded.fitness;";

    private const string SqlSelectAll =
        "SELECT player_id, hunger, sleep, hygiene, social, fitness FROM Life_Needs;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _upsert;
    private readonly SqliteCommand _selectAll;

    public NeedsQueries(DatabaseManager db)
    {
        _db = db;

        _upsert = db.GetPooledCommand(SqlUpsert);
        if (_upsert.Parameters.Count == 0)
        {
            _upsert.Parameters.Add("@playerId", SqliteType.Text);
            _upsert.Parameters.Add("@hunger", SqliteType.Real);
            _upsert.Parameters.Add("@sleep", SqliteType.Real);
            _upsert.Parameters.Add("@hygiene", SqliteType.Real);
            _upsert.Parameters.Add("@social", SqliteType.Real);
            _upsert.Parameters.Add("@fitness", SqliteType.Real);
            _upsert.Prepare();
        }

        _selectAll = db.GetPooledCommand(SqlSelectAll);
    }

    public void Upsert(in NeedsRow row)
    {
        SqliteParameterCollection p = _upsert.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@hunger"].Value = row.Hunger;
        p["@sleep"].Value = row.Sleep;
        p["@hygiene"].Value = row.Hygiene;
        p["@social"].Value = row.Social;
        p["@fitness"].Value = row.Fitness;
        _db.ExecuteNonQuery(_upsert);
    }

    /// <summary>Persists a whole day-tick's worth of NPCs in one batch transaction (joins the caller's batch if one is open).</summary>
    public void BulkUpsert(ReadOnlySpan<NeedsRow> rows)
    {
        bool ownBatch = !_db.IsBatchActive;
        if (ownBatch)
        {
            _db.BeginBatch();
        }
        try
        {
            foreach (ref readonly NeedsRow row in rows)
            {
                Upsert(in row);
            }
            if (ownBatch)
            {
                _db.CommitBatch();
            }
        }
        catch
        {
            if (ownBatch)
            {
                _db.RollbackBatch();
            }
            throw;
        }
    }

    /// <summary>Bulk-loads every persisted row into <paramref name="destination"/> (cleared first), keyed by player_id.</summary>
    public int LoadAll(Dictionary<string, NeedsRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAll);
        while (reader.Read())
        {
            string playerId = reader.GetString(0);
            destination[playerId] = new NeedsRow
            {
                PlayerId = playerId,
                Hunger = (float)reader.GetDouble(1),
                Sleep = (float)reader.GetDouble(2),
                Hygiene = (float)reader.GetDouble(3),
                Social = (float)reader.GetDouble(4),
                Fitness = (float)reader.GetDouble(5),
            };
        }
        return destination.Count;
    }
}
