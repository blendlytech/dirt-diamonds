using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// One player's prerequisite-relevant fields, as the Gritty Event dispatcher
/// polls them (gritty_event_framework.md §1). Lean by design — no names, no
/// team join — the poll snapshots 100+ players once per game day.
/// </summary>
public struct PollPlayerRow
{
    public string PlayerId;
    public int Age;
    public int? TeamId;
    public double Funds;
    public int HealthCeiling;
    public int Recklessness;
    public int BaseballInterest;
    public int DetectionRisk;
}

/// <summary>
/// The Gritty Event dispatcher's typed query surface, bound to the
/// <see cref="DatabaseManager.ReadOnlyView"/> instead of the main connection —
/// these run on the background polling thread while the sims write
/// (database_rules.md: WAL exists for exactly this reader). Same discipline as
/// every other query class: const SQL, commands acquired once, prepared,
/// parameter-value-only per call. Strictly reads; the applier's writes go
/// through PlayerQueries on the main connection.
/// </summary>
public sealed class NarrativePollQueries
{
    private const string SqlSelectStateValue =
        "SELECT value FROM Game_State WHERE key = @key;";

    private const string SqlSelectPollPlayers =
        "SELECT player_id, age, team_id, funds, health_ceiling, recklessness, baseball_interest, detection_risk FROM Players;";

    // Rides idx_entity_flags_active_name (the v1 partial index built for the
    // event-dispatcher poll; plan re-verified via the sqlite MCP this session).
    private const string SqlSelectActiveFlags =
        "SELECT player_id, flag_name, set_on_day FROM Entity_Flags WHERE is_active = 1;";

    private readonly DatabaseManager.ReadOnlyView _view;
    private readonly SqliteCommand _selectStateValue;
    private readonly SqliteCommand _selectPollPlayers;
    private readonly SqliteCommand _selectActiveFlags;

    public NarrativePollQueries(DatabaseManager.ReadOnlyView view)
    {
        _view = view;

        _selectStateValue = view.GetPooledCommand(SqlSelectStateValue);
        if (_selectStateValue.Parameters.Count == 0)
        {
            _selectStateValue.Parameters.Add("@key", SqliteType.Text);
            _selectStateValue.Prepare();
        }

        _selectPollPlayers = view.GetPooledCommand(SqlSelectPollPlayers);
        _selectPollPlayers.Prepare();

        _selectActiveFlags = view.GetPooledCommand(SqlSelectActiveFlags);
        _selectActiveFlags.Prepare();
    }

    /// <summary>
    /// Reads an INTEGER Game_State value (e.g. current_day). False when the
    /// key is absent — a save that has never ticked has no calendar yet.
    /// </summary>
    public bool TryGetStateInteger(string key, out long value)
    {
        _selectStateValue.Parameters["@key"].Value = key;
        object? result = _view.ExecuteScalar(_selectStateValue);
        if (result is long integer)
        {
            value = integer;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>Reads a TEXT Game_State value (e.g. avatar_player_id). False when absent.</summary>
    public bool TryGetStateText(string key, out string value)
    {
        _selectStateValue.Parameters["@key"].Value = key;
        object? result = _view.ExecuteScalar(_selectStateValue);
        if (result is string text)
        {
            value = text;
            return true;
        }
        value = string.Empty;
        return false;
    }

    /// <summary>Snapshots every player's prerequisite fields into <paramref name="destination"/> (cleared first).</summary>
    public int LoadPollPlayers(List<PollPlayerRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _view.ExecuteReader(_selectPollPlayers);
        while (reader.Read())
        {
            destination.Add(new PollPlayerRow
            {
                PlayerId = reader.GetString(0),
                Age = reader.GetInt32(1),
                TeamId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Funds = reader.GetDouble(3),
                HealthCeiling = reader.GetInt32(4),
                Recklessness = reader.GetInt32(5),
                BaseballInterest = reader.GetInt32(6),
                DetectionRisk = reader.GetInt32(7),
            });
        }
        return destination.Count;
    }

    /// <summary>
    /// Snapshots every active flag into <paramref name="destination"/>
    /// (cleared first) keyed by (player_id, flag_name) → set_on_day; a flag
    /// stored with a NULL set_on_day maps to day 0 (always "old enough" for
    /// any min_days_since window).
    /// </summary>
    public int LoadActiveFlags(Dictionary<(string PlayerId, string FlagName), long> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _view.ExecuteReader(_selectActiveFlags);
        while (reader.Read())
        {
            long setOnDay = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
            destination[(reader.GetString(0), reader.GetString(1))] = setOnDay;
        }
        return destination.Count;
    }
}
