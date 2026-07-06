using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>Well-known Game_State keys. Add new keys here, never as inline strings.</summary>
public static class GameStateKeys
{
    /// <summary>Absolute 1-based game-day ordinal (long).</summary>
    public const string CurrentDay = "current_day";

    /// <summary>Season year that day 1 belongs to (long).</summary>
    public const string StartSeasonYear = "start_season_year";

    /// <summary>player_id (text) of the career avatar; absent until the player creates one.</summary>
    public const string AvatarPlayerId = "avatar_player_id";

    /// <summary>
    /// Current dynasty generation (long) — heir mechanics (design doc §1.3).
    /// 1 at founder creation, +1 on every successful succession handoff (§5.3).
    /// Written by the succession-handoff owner; the 3-generation exit
    /// criterion asserts this reaches 3.
    /// </summary>
    public const string DynastyGeneration = "dynasty_generation";

    /// <summary>player_id (text) of the gen-1 founder avatar (design doc §1.3) — legacy/records display.</summary>
    public const string DynastyFounderId = "dynasty_founder_id";

    /// <summary>
    /// Absent while the bloodline is still in play; set to a LineageFailure
    /// reason string (design doc §6: NoHeirs/NoWillingHeir/NoPlayableHeir)
    /// once a retirement trigger finds no eligible heir. Its presence IS the
    /// game-over flag.
    /// </summary>
    public const string LineageOverReason = "lineage_over_reason";

    /// <summary>
    /// player_id (text) of the Narcotics supplier faction rep, resolved once
    /// at the avatar's first Narcotics run and cached here so later runs
    /// don't rescan the player pool (hustles_narcotics_fencing.md §6).
    /// </summary>
    public const string HustleSupplierPlayerId = "hustle_supplier_player_id";

    /// <summary>player_id (text) of the local turf-crew faction rep (§6) — same resolve-once-and-cache pattern as <see cref="HustleSupplierPlayerId"/>.</summary>
    public const string HustleCrewPlayerId = "hustle_crew_player_id";

    /// <summary>player_id (text) of the Fencing "fence" rep — same pattern, filling the gap the design doc's §6 faction list left implicit for fenceStanding (§4.1).</summary>
    public const string HustleFencePlayerId = "hustle_fence_player_id";

    /// <summary>
    /// The avatar's accumulated Practice hours this season (long) — the 9d-2
    /// Life→Baseball bridge (development doc §4). GameManager adds each day's
    /// actually-ticked Practice block hours; DevelopmentManager converts the
    /// total into an extra growth fraction at the season rollover and clears
    /// it (and GameManager clears it on AvatarChangedEvent, so a new bloodline
    /// always starts at zero).
    /// </summary>
    public const string AvatarPracticeCredit = "avatar_practice_credit";
}

/// <summary>
/// Typed query surface for the Game_State key-value table (save metadata:
/// calendar day, start year, ...). Same discipline as <see cref="PlayerQueries"/>:
/// compile-time-constant SQL, commands pooled and prepared once, per-call work
/// limited to parameter values.
///
/// The value column is declared ANY (STRICT), so the @value parameter is
/// deliberately left untyped — Microsoft.Data.Sqlite binds from the runtime
/// value, and SQLite stores each value's native type (integers stay integers).
/// </summary>
public sealed class GameStateQueries
{
    private const string SqlUpsert =
        "INSERT INTO Game_State (key, value) VALUES (@key, @value) " +
        "ON CONFLICT (key) DO UPDATE SET value = excluded.value;";

    private const string SqlSelect =
        "SELECT value FROM Game_State WHERE key = @key;";

    // Additive counter upsert (9d-2 practice credit): an absent key seeds at
    // the delta, a present one accumulates — atomic in SQL, the AdjustFunds
    // discipline, so read-modify-write in C# never races another writer.
    // Semantics validated on a scratch db (development doc §4 / No Blind Queries).
    private const string SqlAdjust =
        "INSERT INTO Game_State (key, value) VALUES (@key, @delta) " +
        "ON CONFLICT (key) DO UPDATE SET value = value + excluded.value;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _upsert;
    private readonly SqliteCommand _select;
    private readonly SqliteCommand _adjust;

    public GameStateQueries(DatabaseManager db)
    {
        _db = db;

        _upsert = db.GetPooledCommand(SqlUpsert);
        if (_upsert.Parameters.Count == 0)
        {
            _upsert.Parameters.Add("@key", SqliteType.Text);
            _upsert.Parameters.Add(new SqliteParameter { ParameterName = "@value" });
            _upsert.Prepare();
        }

        _select = db.GetPooledCommand(SqlSelect);
        if (_select.Parameters.Count == 0)
        {
            _select.Parameters.Add("@key", SqliteType.Text);
            _select.Prepare();
        }

        _adjust = db.GetPooledCommand(SqlAdjust);
        if (_adjust.Parameters.Count == 0)
        {
            _adjust.Parameters.Add("@key", SqliteType.Text);
            _adjust.Parameters.Add("@delta", SqliteType.Integer);
            _adjust.Prepare();
        }
    }

    public void SetInt64(string key, long value) => Set(key, value);

    /// <summary>
    /// Adds <paramref name="delta"/> to an integer counter key, seeding an
    /// absent key at the delta — atomic in SQL (the AdjustFunds discipline).
    /// Only meaningful for integer-valued keys; a text key would fail the
    /// arithmetic loudly, same corruption class as <see cref="TryGetInt64"/>.
    /// </summary>
    public void AdjustInt64(string key, long delta)
    {
        _adjust.Parameters["@key"].Value = key;
        _adjust.Parameters["@delta"].Value = delta;
        _db.ExecuteNonQuery(_adjust);
    }

    public void SetText(string key, string value) => Set(key, value);

    private void Set(string key, object value)
    {
        _upsert.Parameters["@key"].Value = key;
        _upsert.Parameters["@value"].Value = value;
        _db.ExecuteNonQuery(_upsert);
    }

    /// <summary>
    /// False when the key is absent. A present key holding a non-integer value
    /// throws — that is save corruption, not a missing setting, and reseeding
    /// over it would destroy the calendar.
    /// </summary>
    public bool TryGetInt64(string key, out long value)
    {
        object? stored = Get(key);
        switch (stored)
        {
            case null:
                value = 0;
                return false;
            case long integer:
                value = integer;
                return true;
            default:
                throw new InvalidOperationException(
                    $"Game_State['{key}'] holds a {stored.GetType().Name}, expected an integer.");
        }
    }

    public bool TryGetText(string key, out string value)
    {
        object? stored = Get(key);
        switch (stored)
        {
            case null:
                value = string.Empty;
                return false;
            case string text:
                value = text;
                return true;
            default:
                throw new InvalidOperationException(
                    $"Game_State['{key}'] holds a {stored.GetType().Name}, expected text.");
        }
    }

    private object? Get(string key)
    {
        _select.Parameters["@key"].Value = key;
        return _db.ExecuteScalar(_select);
    }
}
