using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// The narrative-log row's discriminator (BurnerPhone "Events vs Messages"
/// tab split): an Event row is a gritty-event fire/resolution (renders on the
/// Events feed); a Text row is a standalone companion text (renders on the
/// Messages tab). Absent in the stored payload always reads as Event — every
/// row written before this split stays byte-compatible with no migration.
/// </summary>
public enum NarrativeMessageKind : byte
{
    Event,
    Text,
}

/// <summary>One persisted narrative message — the Burner Phone thread's read-model row (presentation_layer_narrative.md §4.3).</summary>
public struct NarrativeMessageRow
{
    public long LogId;
    public int SeasonYear;
    public long GameDay;
    public string ContactId;
    public NarrativeMessageKind Kind;
    public string Prompt;
    public string Choice;
    public int ChoiceIndex;

    /// <summary>The immediate narrative payoff (Events feed §2); "" when the choice/row carries none — the UI's "You: &lt;Choice&gt;" fallback then applies.</summary>
    public string Outcome;

    /// <summary>Kind=Text rows only: the companion text body. "" for Kind=Event rows.</summary>
    public string Body;

    /// <summary>
    /// Kind=Event rows only: the Events feed's card heading — the raw wire
    /// string a Narrative.Events.EventCategory encodes to (this Data-layer
    /// type stays enum-agnostic, same posture as <see cref="ContactId"/>
    /// staying a plain string rather than a ContactDefinition). Persisted at
    /// resolve time so history renders the category as-fired even if content
    /// later changes it. "general" for a pre-category row or a Kind=Text row.
    /// </summary>
    public string Category;
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
    // The @today upper bound (phone-split amendment §3) makes a future-dated
    // delayed-text row simply invisible until the calendar reaches it — no
    // scheduler, persistence and multi-day catch-up come free from the read
    // filter alone.
    private const string SqlSelectByPlayer =
        "SELECT log_id, season_year, game_day, payload FROM Game_Logs " +
        "WHERE player_id = @playerId AND event_type = '" + EventType + "' AND game_day <= @today " +
        "ORDER BY game_day, log_id;";

    // History tab paging (BurnerPhone's disclosed-seam follow-up): Kind lives
    // inside the JSON payload, not a column, so this can't filter to
    // Event-kind in SQL — it fetches raw rows newest-first below a log_id
    // cursor and LoadEventPageBefore below filters/loops client-side. Same
    // idx_game_logs_player index as SqlSelectByPlayer above.
    private const string SqlSelectPageBeforeLogId =
        "SELECT log_id, season_year, game_day, payload FROM Game_Logs " +
        "WHERE player_id = @playerId AND event_type = '" + EventType + "' AND game_day <= @today " +
        "AND log_id < @beforeLogId " +
        "ORDER BY log_id DESC LIMIT @batchSize;";

    /// <summary>Raw rows fetched per DB round trip while paging History — over-fetched because Text-kind rows are filtered out client-side.</summary>
    private const int RawBatchSize = 300;

    /// <summary>Bounds the per-call loop in <see cref="LoadEventPageBefore"/> — a click-driven pagination call, not a per-frame one, but this keeps a single click from scanning unbounded history if a career logs a long run of nothing but Text rows.</summary>
    private const int MaxRawBatchesPerPage = 5;

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _insert;
    private readonly SqliteCommand _selectByPlayer;
    private readonly SqliteCommand _selectPageBeforeLogId;
    private readonly List<NarrativeMessageRow> _rawBatchScratch = new();
    private readonly List<NarrativeMessageRow> _collectedScratch = new();

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
            _selectByPlayer.Parameters.Add("@today", SqliteType.Integer);
            _selectByPlayer.Prepare();
        }

        _selectPageBeforeLogId = db.GetPooledCommand(SqlSelectPageBeforeLogId);
        if (_selectPageBeforeLogId.Parameters.Count == 0)
        {
            _selectPageBeforeLogId.Parameters.Add("@playerId", SqliteType.Text);
            _selectPageBeforeLogId.Parameters.Add("@today", SqliteType.Integer);
            _selectPageBeforeLogId.Parameters.Add("@beforeLogId", SqliteType.Integer);
            _selectPageBeforeLogId.Parameters.Add("@batchSize", SqliteType.Integer);
            _selectPageBeforeLogId.Prepare();
        }
    }

    /// <summary>
    /// Appends one Event-kind narrative row — the consequence applier's write
    /// on every resolved choice (autopilot or player-picked, including
    /// forfeits), so the Events feed reflects what actually happened even for
    /// a choice the player never saw. The payload omits "kind" entirely (old
    /// shape, byte-compatible) — the reader's missing-kind-means-Event rule
    /// is what makes that safe. <paramref name="outcome"/> is the amendment's
    /// per-choice immediate payoff; null/empty is the documented "You: &lt;Choice&gt;" UI fallback.
    /// </summary>
    /// <param name="category">The wire string a Narrative.Events.EventCategory encodes to — this Data-layer method takes it as an opaque tag, same as <paramref name="contactId"/>, never referencing the enum type itself (kept out of this project's dependency graph; see CoreLoopHarness/MonteCarloHarness, which compile this folder without Narrative/Events).</param>
    public void Insert(
        int seasonYear, long gameDay, string playerId, string contactId, string category,
        string prompt, string choiceLabel, int choiceIndex, string? outcome = null)
    {
        string payload = JsonSerializer.Serialize(new NarrativeMessagePayload
        {
            Contact = contactId,
            Category = category,
            Prompt = prompt,
            Choice = choiceLabel,
            ChoiceIndex = choiceIndex,
            Outcome = string.IsNullOrEmpty(outcome) ? null : outcome,
        });

        SqliteParameterCollection p = _insert.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@gameDay"].Value = gameDay;
        p["@playerId"].Value = playerId;
        p["@payload"].Value = payload;
        _db.ExecuteNonQuery(_insert);
    }

    /// <summary>
    /// Appends one Text-kind narrative row — a standalone companion text with
    /// no event/choice attached (the event-level fire-time text, or a
    /// choice's delayed reaction text). Renders on the Messages tab only.
    /// </summary>
    public void InsertText(int seasonYear, long gameDay, string playerId, string contactId, string body)
    {
        string payload = JsonSerializer.Serialize(new NarrativeMessagePayload
        {
            Kind = "text",
            Contact = contactId,
            Prompt = body,
            ChoiceIndex = -1,
        });

        SqliteParameterCollection p = _insert.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@gameDay"].Value = gameDay;
        p["@playerId"].Value = playerId;
        p["@payload"].Value = payload;
        _db.ExecuteNonQuery(_insert);
    }

    /// <summary>Every narrative message logged for <paramref name="playerId"/> with <c>game_day &lt;= today</c>, oldest first, into <paramref name="destination"/> (cleared first) — the phone's thread history. A delayed text dated past <paramref name="today"/> is simply absent until the calendar reaches it.</summary>
    public int LoadForPlayer(string playerId, long today, List<NarrativeMessageRow> destination)
    {
        destination.Clear();
        _selectByPlayer.Parameters["@playerId"].Value = playerId;
        _selectByPlayer.Parameters["@today"].Value = today;
        using SqliteDataReader reader = _db.ExecuteReader(_selectByPlayer);
        while (reader.Read())
        {
            destination.Add(ReadRow(reader));
        }
        return destination.Count;
    }

    /// <summary>
    /// History tab paging: fills <paramref name="destination"/> (cleared
    /// first) with up to <paramref name="pageSize"/> Event-kind rows older
    /// than <paramref name="beforeLogId"/> (pass <see cref="long.MaxValue"/>
    /// for the first page), oldest-first within the page — same display
    /// convention as <see cref="LoadForPlayer"/>. Loops over raw
    /// newest-first batches (Kind is inside the JSON payload, not a column,
    /// so SQL alone can't filter to Event-kind) until either
    /// <paramref name="pageSize"/> Event rows are collected or a raw batch
    /// comes back short of a full <see cref="RawBatchSize"/> — the latter
    /// IS the start of this player's logged history, reported via
    /// <paramref name="reachedBeginning"/> so the History tab can hide its
    /// "Load Older" control. Bounded by <see cref="MaxRawBatchesPerPage"/>
    /// per call regardless.
    /// </summary>
    public int LoadEventPageBefore(
        string playerId, long today, long beforeLogId, int pageSize,
        List<NarrativeMessageRow> destination, out bool reachedBeginning)
    {
        destination.Clear();
        _collectedScratch.Clear();
        reachedBeginning = false;
        long cursor = beforeLogId;

        for (int iteration = 0; iteration < MaxRawBatchesPerPage && _collectedScratch.Count < pageSize; iteration++)
        {
            _rawBatchScratch.Clear();
            _selectPageBeforeLogId.Parameters["@playerId"].Value = playerId;
            _selectPageBeforeLogId.Parameters["@today"].Value = today;
            _selectPageBeforeLogId.Parameters["@beforeLogId"].Value = cursor;
            _selectPageBeforeLogId.Parameters["@batchSize"].Value = RawBatchSize;
            using (SqliteDataReader reader = _db.ExecuteReader(_selectPageBeforeLogId))
            {
                while (reader.Read())
                {
                    _rawBatchScratch.Add(ReadRow(reader));
                }
            }

            if (_rawBatchScratch.Count == 0)
            {
                reachedBeginning = true;
                break;
            }

            cursor = _rawBatchScratch[^1].LogId; // rows arrive log_id DESC; the last one is the oldest fetched so far
            foreach (NarrativeMessageRow row in _rawBatchScratch)
            {
                if (row.Kind == NarrativeMessageKind.Event)
                {
                    _collectedScratch.Add(row);
                }
            }

            if (_rawBatchScratch.Count < RawBatchSize)
            {
                reachedBeginning = true;
                break;
            }
        }

        // _collectedScratch is newest-first (raw batches arrive DESC); flip
        // to oldest-first, then trim any overshoot from the oldest end so a
        // call never returns more than pageSize.
        _collectedScratch.Reverse();
        int start = Math.Max(0, _collectedScratch.Count - pageSize);
        for (int i = start; i < _collectedScratch.Count; i++)
        {
            destination.Add(_collectedScratch[i]);
        }
        return destination.Count;
    }

    private static NarrativeMessageRow ReadRow(SqliteDataReader reader)
    {
        NarrativeMessagePayload payload =
            JsonSerializer.Deserialize<NarrativeMessagePayload>(reader.GetString(3))
            ?? throw new InvalidOperationException(
                $"Game_Logs row {reader.GetInt64(0)} has an unparsable narrative_msg payload.");
        return new NarrativeMessageRow
        {
            LogId = reader.GetInt64(0),
            SeasonYear = reader.GetInt32(1),
            GameDay = reader.GetInt64(2),
            ContactId = payload.Contact,
            Kind = payload.Kind == "text" ? NarrativeMessageKind.Text : NarrativeMessageKind.Event,
            Prompt = payload.Kind == "text" ? "" : payload.Prompt,
            Choice = payload.Choice,
            ChoiceIndex = payload.ChoiceIndex,
            Outcome = payload.Outcome ?? "",
            Body = payload.Kind == "text" ? payload.Prompt : "",
            // Absent/empty reads as "general" — every row written before this
            // field existed stays byte-compatible, same discipline as Kind's
            // missing-means-Event rule. The actual enum mapping lives with
            // Narrative.Events.EventCategory, not in this Data-layer file.
            Category = string.IsNullOrEmpty(payload.Category) ? "general" : payload.Category,
        };
    }

    private sealed class NarrativeMessagePayload
    {
        /// <summary>Absent (null) reads as Event — every row written before the phone-split stays byte-compatible. Omitted from the serialized payload (not written as a JSON null) so an Insert() write with no kind is shape-identical to a pre-split row.</summary>
        [JsonPropertyName("kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kind { get; set; }

        [JsonPropertyName("contact")]
        public string Contact { get; set; } = "";

        /// <summary>Absent on every pre-category row — <see cref="ReadRow"/> defaults it to "general".</summary>
        [JsonPropertyName("category")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Category { get; set; }

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("choice")]
        public string Choice { get; set; } = "";

        [JsonPropertyName("choice_index")]
        public int ChoiceIndex { get; set; }

        /// <summary>Absent (null) on every pre-amendment row — the UI's "You: &lt;Choice&gt;" fallback covers it. Omitted (not written as a JSON null) so history re-renders identically after reload.</summary>
        [JsonPropertyName("outcome")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Outcome { get; set; }
    }
}
