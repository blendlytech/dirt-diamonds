using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// Typed query surface for Players and their satellite tables (stats, flags,
/// relationships). All SQL lives here as compile-time constants; commands are
/// acquired from the <see cref="DatabaseManager"/> pool once, parameter-typed,
/// prepared, and reused for the whole session — per-call work is limited to
/// setting parameter values.
///
/// Bulk reads fill caller-provided lists (cleared first) so hot paths can reuse
/// pre-sized buffers instead of allocating per query.
/// </summary>
public sealed class PlayerQueries
{
    private const string PlayerColumns =
        "player_id, first_name, last_name, age, team_id, funds, health_ceiling, recklessness, baseball_interest, detection_risk";

    private const string SqlInsertPlayer =
        "INSERT INTO Players (" + PlayerColumns + ") VALUES " +
        "(@playerId, @firstName, @lastName, @age, @teamId, @funds, @healthCeiling, @recklessness, @baseballInterest, @detectionRisk);";

    private const string SqlSelectPlayerById =
        "SELECT " + PlayerColumns + " FROM Players WHERE player_id = @playerId;";

    private const string SqlSelectAllPlayers =
        "SELECT " + PlayerColumns + " FROM Players;";

    private const string SqlCountPlayers =
        "SELECT COUNT(*) FROM Players;";

    private const string SqlUpdateFunds =
        "UPDATE Players SET funds = @funds WHERE player_id = @playerId;";

    private const string SqlDeletePlayer =
        "DELETE FROM Players WHERE player_id = @playerId;";

    private const string SqlInsertBattingSeason =
        "INSERT INTO Batting_Stats (player_id, season_year, pa, ab, h, doubles, triples, hr, bb, so, rbi, sb, avg, obp, slg, ops) VALUES " +
        "(@playerId, @seasonYear, @pa, @ab, @h, @doubles, @triples, @hr, @bb, @so, @rbi, @sb, @avg, @obp, @slg, @ops);";

    private const string SqlSelectBattingByPlayer =
        "SELECT stat_id, player_id, season_year, pa, ab, h, doubles, triples, hr, bb, so, rbi, sb, avg, obp, slg, ops " +
        "FROM Batting_Stats WHERE player_id = @playerId ORDER BY season_year;";

    private const string SqlUpsertFlag =
        "INSERT INTO Entity_Flags (player_id, flag_name, is_active, set_on_day) VALUES (@playerId, @flagName, @isActive, @setOnDay) " +
        "ON CONFLICT (player_id, flag_name) DO UPDATE SET is_active = excluded.is_active, set_on_day = excluded.set_on_day;";

    private const string SqlSelectActiveFlags =
        "SELECT flag_id, player_id, flag_name, is_active, set_on_day " +
        "FROM Entity_Flags WHERE player_id = @playerId AND is_active = 1;";

    private const string SqlUpsertRelationship =
        "INSERT INTO Relationships (player_1_id, player_2_id, affinity_score, type_enum) VALUES (@player1Id, @player2Id, @affinityScore, @typeEnum) " +
        "ON CONFLICT (player_1_id, player_2_id) DO UPDATE SET affinity_score = excluded.affinity_score, type_enum = excluded.type_enum;";

    // Two indexed probes glued with UNION ALL — an OR across player_1_id/player_2_id
    // would force a full scan past both indexes.
    private const string SqlSelectRelationshipsFor =
        "SELECT rel_id, player_1_id, player_2_id, affinity_score, type_enum FROM Relationships WHERE player_1_id = @playerId " +
        "UNION ALL " +
        "SELECT rel_id, player_1_id, player_2_id, affinity_score, type_enum FROM Relationships WHERE player_2_id = @playerId;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _insertPlayer;
    private readonly SqliteCommand _selectPlayerById;
    private readonly SqliteCommand _selectAllPlayers;
    private readonly SqliteCommand _countPlayers;
    private readonly SqliteCommand _updateFunds;
    private readonly SqliteCommand _deletePlayer;
    private readonly SqliteCommand _insertBattingSeason;
    private readonly SqliteCommand _selectBattingByPlayer;
    private readonly SqliteCommand _upsertFlag;
    private readonly SqliteCommand _selectActiveFlags;
    private readonly SqliteCommand _upsertRelationship;
    private readonly SqliteCommand _selectRelationshipsFor;

    public PlayerQueries(DatabaseManager db)
    {
        _db = db;

        _insertPlayer = Acquire(SqlInsertPlayer,
            ("@playerId", SqliteType.Text), ("@firstName", SqliteType.Text), ("@lastName", SqliteType.Text),
            ("@age", SqliteType.Integer), ("@teamId", SqliteType.Integer), ("@funds", SqliteType.Real),
            ("@healthCeiling", SqliteType.Integer), ("@recklessness", SqliteType.Integer),
            ("@baseballInterest", SqliteType.Integer), ("@detectionRisk", SqliteType.Integer));

        _selectPlayerById = Acquire(SqlSelectPlayerById, ("@playerId", SqliteType.Text));
        _selectAllPlayers = Acquire(SqlSelectAllPlayers);
        _countPlayers = Acquire(SqlCountPlayers);
        _updateFunds = Acquire(SqlUpdateFunds, ("@funds", SqliteType.Real), ("@playerId", SqliteType.Text));
        _deletePlayer = Acquire(SqlDeletePlayer, ("@playerId", SqliteType.Text));

        _insertBattingSeason = Acquire(SqlInsertBattingSeason,
            ("@playerId", SqliteType.Text), ("@seasonYear", SqliteType.Integer),
            ("@pa", SqliteType.Integer), ("@ab", SqliteType.Integer), ("@h", SqliteType.Integer),
            ("@doubles", SqliteType.Integer), ("@triples", SqliteType.Integer), ("@hr", SqliteType.Integer),
            ("@bb", SqliteType.Integer), ("@so", SqliteType.Integer), ("@rbi", SqliteType.Integer),
            ("@sb", SqliteType.Integer), ("@avg", SqliteType.Real), ("@obp", SqliteType.Real),
            ("@slg", SqliteType.Real), ("@ops", SqliteType.Real));

        _selectBattingByPlayer = Acquire(SqlSelectBattingByPlayer, ("@playerId", SqliteType.Text));

        _upsertFlag = Acquire(SqlUpsertFlag,
            ("@playerId", SqliteType.Text), ("@flagName", SqliteType.Text),
            ("@isActive", SqliteType.Integer), ("@setOnDay", SqliteType.Integer));

        _selectActiveFlags = Acquire(SqlSelectActiveFlags, ("@playerId", SqliteType.Text));

        _upsertRelationship = Acquire(SqlUpsertRelationship,
            ("@player1Id", SqliteType.Text), ("@player2Id", SqliteType.Text),
            ("@affinityScore", SqliteType.Integer), ("@typeEnum", SqliteType.Text));

        _selectRelationshipsFor = Acquire(SqlSelectRelationshipsFor, ("@playerId", SqliteType.Text));
    }

    private SqliteCommand Acquire(string sql, params (string Name, SqliteType Type)[] parameters)
    {
        SqliteCommand command = _db.GetPooledCommand(sql);
        if (command.Parameters.Count == 0)
        {
            foreach ((string name, SqliteType type) in parameters)
            {
                command.Parameters.Add(name, type);
            }
            command.Prepare();
        }
        return command;
    }

    // ------------------------------------------------------------------
    // Players
    // ------------------------------------------------------------------

    public void Insert(in PlayerRow player)
    {
        SqliteParameterCollection p = _insertPlayer.Parameters;
        p["@playerId"].Value = player.PlayerId;
        p["@firstName"].Value = player.FirstName;
        p["@lastName"].Value = player.LastName;
        p["@age"].Value = player.Age;
        p["@teamId"].Value = player.TeamId.HasValue ? player.TeamId.Value : DBNull.Value;
        p["@funds"].Value = player.Funds;
        p["@healthCeiling"].Value = player.HealthCeiling;
        p["@recklessness"].Value = player.Recklessness;
        p["@baseballInterest"].Value = player.BaseballInterest;
        p["@detectionRisk"].Value = player.DetectionRisk;
        _db.ExecuteNonQuery(_insertPlayer);
    }

    /// <summary>Inserts a roster in one batch transaction (joins the caller's batch if one is open).</summary>
    public void BulkInsert(ReadOnlySpan<PlayerRow> players)
    {
        bool ownBatch = !_db.IsBatchActive;
        if (ownBatch)
        {
            _db.BeginBatch();
        }
        try
        {
            foreach (ref readonly PlayerRow player in players)
            {
                Insert(in player);
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

    public bool TryGetById(string playerId, out PlayerRow player)
    {
        _selectPlayerById.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectPlayerById);
        if (!reader.Read())
        {
            player = default;
            return false;
        }
        player = ReadPlayer(reader);
        return true;
    }

    /// <summary>
    /// Bulk-loads every player into <paramref name="destination"/> (cleared first).
    /// The macro-sim reads rosters up front through this — never row-at-a-time
    /// queries mid-simulation.
    /// </summary>
    public int LoadAll(List<PlayerRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAllPlayers);
        while (reader.Read())
        {
            destination.Add(ReadPlayer(reader));
        }
        return destination.Count;
    }

    public int Count() => Convert.ToInt32(_db.ExecuteScalar(_countPlayers) ?? 0);

    public void UpdateFunds(string playerId, double newFunds)
    {
        _updateFunds.Parameters["@funds"].Value = newFunds;
        _updateFunds.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_updateFunds);
    }

    /// <summary>Removes a player; stats, flags and relationships cascade via FK rules.</summary>
    public bool Delete(string playerId)
    {
        _deletePlayer.Parameters["@playerId"].Value = playerId;
        return _db.ExecuteNonQuery(_deletePlayer) > 0;
    }

    private static PlayerRow ReadPlayer(SqliteDataReader reader) => new()
    {
        PlayerId = reader.GetString(0),
        FirstName = reader.GetString(1),
        LastName = reader.GetString(2),
        Age = reader.GetInt32(3),
        TeamId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        Funds = reader.GetDouble(5),
        HealthCeiling = reader.GetInt32(6),
        Recklessness = reader.GetInt32(7),
        BaseballInterest = reader.GetInt32(8),
        DetectionRisk = reader.GetInt32(9),
    };

    // ------------------------------------------------------------------
    // Batting stats
    // ------------------------------------------------------------------

    public void InsertBattingSeason(in BattingStatsRow row)
    {
        SqliteParameterCollection p = _insertBattingSeason.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@seasonYear"].Value = row.SeasonYear;
        p["@pa"].Value = row.Pa;
        p["@ab"].Value = row.Ab;
        p["@h"].Value = row.H;
        p["@doubles"].Value = row.Doubles;
        p["@triples"].Value = row.Triples;
        p["@hr"].Value = row.Hr;
        p["@bb"].Value = row.Bb;
        p["@so"].Value = row.So;
        p["@rbi"].Value = row.Rbi;
        p["@sb"].Value = row.Sb;
        p["@avg"].Value = row.Avg;
        p["@obp"].Value = row.Obp;
        p["@slg"].Value = row.Slg;
        p["@ops"].Value = row.Ops;
        _db.ExecuteNonQuery(_insertBattingSeason);
    }

    public int LoadBattingSeasons(string playerId, List<BattingStatsRow> destination)
    {
        destination.Clear();
        _selectBattingByPlayer.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectBattingByPlayer);
        while (reader.Read())
        {
            destination.Add(new BattingStatsRow
            {
                StatId = reader.GetInt64(0),
                PlayerId = reader.GetString(1),
                SeasonYear = reader.GetInt32(2),
                Pa = reader.GetInt32(3),
                Ab = reader.GetInt32(4),
                H = reader.GetInt32(5),
                Doubles = reader.GetInt32(6),
                Triples = reader.GetInt32(7),
                Hr = reader.GetInt32(8),
                Bb = reader.GetInt32(9),
                So = reader.GetInt32(10),
                Rbi = reader.GetInt32(11),
                Sb = reader.GetInt32(12),
                Avg = reader.GetDouble(13),
                Obp = reader.GetDouble(14),
                Slg = reader.GetDouble(15),
                Ops = reader.GetDouble(16),
            });
        }
        return destination.Count;
    }

    // ------------------------------------------------------------------
    // Entity flags (Gritty Event prerequisites)
    // ------------------------------------------------------------------

    public void SetFlag(string playerId, string flagName, bool isActive, long? setOnDay)
    {
        SqliteParameterCollection p = _upsertFlag.Parameters;
        p["@playerId"].Value = playerId;
        p["@flagName"].Value = flagName;
        p["@isActive"].Value = isActive ? 1 : 0;
        p["@setOnDay"].Value = setOnDay.HasValue ? setOnDay.Value : DBNull.Value;
        _db.ExecuteNonQuery(_upsertFlag);
    }

    public int LoadActiveFlags(string playerId, List<EntityFlagRow> destination)
    {
        destination.Clear();
        _selectActiveFlags.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectActiveFlags);
        while (reader.Read())
        {
            destination.Add(new EntityFlagRow
            {
                FlagId = reader.GetInt64(0),
                PlayerId = reader.GetString(1),
                FlagName = reader.GetString(2),
                IsActive = reader.GetInt64(3) != 0,
                SetOnDay = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            });
        }
        return destination.Count;
    }

    // ------------------------------------------------------------------
    // Relationships
    // ------------------------------------------------------------------

    /// <summary>
    /// Upserts the single row for an unordered pair; ids are swapped into the
    /// canonical player_1_id &lt; player_2_id ordering the schema expects.
    /// </summary>
    public void UpsertRelationship(string playerAId, string playerBId, int affinityScore, RelationshipType type)
    {
        if (string.CompareOrdinal(playerAId, playerBId) > 0)
        {
            (playerAId, playerBId) = (playerBId, playerAId);
        }

        SqliteParameterCollection p = _upsertRelationship.Parameters;
        p["@player1Id"].Value = playerAId;
        p["@player2Id"].Value = playerBId;
        p["@affinityScore"].Value = affinityScore;
        p["@typeEnum"].Value = RelationshipTypeMap.ToDbString(type);
        _db.ExecuteNonQuery(_upsertRelationship);
    }

    public int LoadRelationshipsFor(string playerId, List<RelationshipRow> destination)
    {
        destination.Clear();
        _selectRelationshipsFor.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectRelationshipsFor);
        while (reader.Read())
        {
            destination.Add(new RelationshipRow
            {
                RelId = reader.GetInt64(0),
                Player1Id = reader.GetString(1),
                Player2Id = reader.GetString(2),
                AffinityScore = reader.GetInt32(3),
                Type = RelationshipTypeMap.FromDbString(reader.GetString(4)),
            });
        }
        return destination.Count;
    }
}
