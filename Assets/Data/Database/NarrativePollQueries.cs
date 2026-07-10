using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// One player's prerequisite-relevant fields, as the Gritty Event dispatcher
/// polls them (gritty_event_framework.md §1). Lean by design — no names, no
/// team join — the poll snapshots 100+ players once per game day. The one
/// join is HS-5's Family_Background.strictness (a PK LEFT JOIN, COALESCEd to
/// the neutral 50 for the no-row majority), so parental-approval content can
/// gate on how strict the household actually is.
///
/// TeammateExOfPartner is the second, graph-reaching addition (schema v13):
/// true when the subject has a live Partner edge AND that partner has a
/// Relationship_History row with one of the subject's teammates — i.e. "does
/// a teammate have an ex who is my current partner" is answerable from the
/// poll thread's read-only DB view, never the main-thread RelationshipGraph
/// (hs_clubhouse_cancer, high_school_person_layer.md §9 disclosure (2)).
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
    public int Strictness;
    public bool TeammateExOfPartner;
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

    // Schema MCP-validated 2026-07-09 (No Blind Queries): Family_Background
    // is PK player_id with strictness INTEGER NOT NULL DEFAULT 50 — the
    // LEFT JOIN rides the primary key, and COALESCE keeps row shape total.
    //
    // TeammateExOfPartner (schema v13, MCP-validated against the live save
    // with a synthetic history row before wiring): RelPairs/HistPairs unpivot
    // Relationships/Relationship_History bidirectionally so the correlated
    // EXISTS needs no player_1/player_2 OR-branching. For subject p: find p's
    // current Partner edge (partner_rel), then check whether the OTHER end
    // of that edge (the partner) has ANY Relationship_History row (ex_hist)
    // whose other side is a teammate of p (excluding p itself). Runs once per
    // evaluated day over the whole poll, same cost class as the strictness
    // join — Relationships/Relationship_History are both small tables next
    // to Players.
    private const string SqlSelectPollPlayers =
        "WITH RelPairs AS (" +
        "  SELECT player_1_id AS pid, player_2_id AS other_id, type_enum FROM Relationships " +
        "  UNION ALL " +
        "  SELECT player_2_id AS pid, player_1_id AS other_id, type_enum FROM Relationships" +
        "), HistPairs AS (" +
        "  SELECT player_1_id AS pid, player_2_id AS other_id FROM Relationship_History " +
        "  UNION ALL " +
        "  SELECT player_2_id AS pid, player_1_id AS other_id FROM Relationship_History" +
        ") " +
        "SELECT p.player_id, p.age, p.team_id, p.funds, p.health_ceiling, p.recklessness, " +
        "p.baseball_interest, p.detection_risk, COALESCE(f.strictness, 50), " +
        "CASE WHEN EXISTS (" +
        "  SELECT 1 FROM RelPairs partner_rel " +
        "  JOIN HistPairs ex_hist ON ex_hist.pid = partner_rel.other_id " +
        "  JOIN Players teammate ON teammate.player_id = ex_hist.other_id " +
        "  WHERE partner_rel.pid = p.player_id AND partner_rel.type_enum = 'Partner' " +
        "    AND teammate.team_id = p.team_id AND teammate.player_id != p.player_id" +
        ") THEN 1 ELSE 0 END " +
        "FROM Players p LEFT JOIN Family_Background f ON f.player_id = p.player_id;";

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
                Strictness = reader.GetInt32(8),
                TeammateExOfPartner = reader.GetInt32(9) != 0,
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
