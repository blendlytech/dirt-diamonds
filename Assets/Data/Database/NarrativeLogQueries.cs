using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>One persisted narrative message — the Burner Phone thread's read-model row (presentation_layer_narrative.md §4.3).</summary>
public struct NarrativeMessageRow
{
    public long LogId;
    public int SeasonYear;
    public long GameDay;
    public string ContactId;
    public string Prompt;
    public string Choice;
    public int ChoiceIndex;
}

/// <summary>
/// Typed query surface for the narrative-message read-model: an additive
/// Game_Logs write (event_type = "narrative_msg", no schema change — the
/// column already exists, only the write is new) on every resolved gritty
/// event choice, and the phone's read-back filtered to one subject. Same
/// pooled-prepared-command discipline as every other query class. The
/// payload is an engine-generated wire format, not authored content, so it
/// round-trips through System.Text.Json's reflection (de)serializer rather
/// than GrittyEventJson's hand-walked parser (that discipline is for loud,
/// labelled errors on untrusted content batches — not applicable here).
/// </summary>
public sealed class NarrativeLogQueries
{
    /// <summary>Verified against BaseballQueries' InsertGameLog usage ("pa"/"final") — no collision (No Blind Queries).</summary>
    public const string EventType = "narrative_msg";

    private const string SqlInsert =
        "INSERT INTO Game_Logs (season_year, game_day, player_id, event_type, payload) VALUES " +
        "(@seasonYear, @gameDay, @playerId, '" + EventType + "', @payload);";

    // Rides idx_game_logs_player; event_type further filters within that
    // player's (small) row set — validated via the sqlite MCP before writing.
    private const string SqlSelectByPlayer =
        "SELECT log_id, season_year, game_day, payload FROM Game_Logs " +
        "WHERE player_id = @playerId AND event_type = '" + EventType + "' ORDER BY game_day, log_id;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _insert;
    private readonly SqliteCommand _selectByPlayer;

    public NarrativeLogQueries(DatabaseManager db)
    {
        _db = db;

        _insert = db.GetPooledCommand(SqlInsert);
        if (_insert.Parameters.Count == 0)
        {
            _insert.Parameters.Add("@seasonYear", SqliteType.Integer);
            _insert.Parameters.Add("@gameDay", SqliteType.Integer);
            _insert.Parameters.Add("@playerId", SqliteType.Text);
            _insert.Parameters.Add("@payload", SqliteType.Text);
            _insert.Prepare();
        }

        _selectByPlayer = db.GetPooledCommand(SqlSelectByPlayer);
        if (_selectByPlayer.Parameters.Count == 0)
        {
            _selectByPlayer.Parameters.Add("@playerId", SqliteType.Text);
            _selectByPlayer.Prepare();
        }
    }

    /// <summary>
    /// Appends one narrative message row — the consequence applier's write on
    /// every resolved choice (autopilot or player-picked, including
    /// forfeits), so the thread reflects what actually happened even for a
    /// choice the player never saw.
    /// </summary>
    public void Insert(
        int seasonYear, long gameDay, string playerId, string contactId,
        string prompt, string choiceLabel, int choiceIndex)
    {
        string payload = JsonSerializer.Serialize(new NarrativeMessagePayload
        {
            Contact = contactId,
            Prompt = prompt,
            Choice = choiceLabel,
            ChoiceIndex = choiceIndex,
        });

        SqliteParameterCollection p = _insert.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@gameDay"].Value = gameDay;
        p["@playerId"].Value = playerId;
        p["@payload"].Value = payload;
        _db.ExecuteNonQuery(_insert);
    }

    /// <summary>Every narrative message logged for <paramref name="playerId"/>, oldest first, into <paramref name="destination"/> (cleared first) — the phone's thread history.</summary>
    public int LoadForPlayer(string playerId, List<NarrativeMessageRow> destination)
    {
        destination.Clear();
        _selectByPlayer.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectByPlayer);
        while (reader.Read())
        {
            NarrativeMessagePayload payload =
                JsonSerializer.Deserialize<NarrativeMessagePayload>(reader.GetString(3))
                ?? throw new InvalidOperationException(
                    $"Game_Logs row {reader.GetInt64(0)} has an unparsable narrative_msg payload.");
            destination.Add(new NarrativeMessageRow
            {
                LogId = reader.GetInt64(0),
                SeasonYear = reader.GetInt32(1),
                GameDay = reader.GetInt64(2),
                ContactId = payload.Contact,
                Prompt = payload.Prompt,
                Choice = payload.Choice,
                ChoiceIndex = payload.ChoiceIndex,
            });
        }
        return destination.Count;
    }

    private sealed class NarrativeMessagePayload
    {
        [JsonPropertyName("contact")]
        public string Contact { get; set; } = "";

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("choice")]
        public string Choice { get; set; } = "";

        [JsonPropertyName("choice_index")]
        public int ChoiceIndex { get; set; }
    }
}
