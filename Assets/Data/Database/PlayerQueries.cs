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

    private const string SqlUpdateTeam =
        "UPDATE Players SET team_id = @teamId WHERE player_id = @playerId;";

    // Targeted single-player age write — heir mechanics (design doc §5.5): the
    // yearly aging tick itself is a separate, not-yet-built surface (a
    // set-based age = age + 1 over every player, owned by the succession
    // handoff), but the harness needs to seed a specific player's age directly
    // to exercise the §1.2 direction invariant before that tick exists.
    private const string SqlUpdateAge =
        "UPDATE Players SET age = @age WHERE player_id = @playerId;";

    // The yearly aging tick (heir mechanics §5.5): one set-based statement over
    // every player, fired once per SeasonRolledOverEvent by the succession
    // owner (CareerManager). No join, no filter — retirees keep aging (the FA
    // signability window reads age) and heirs must mature.
    private const string SqlAgeAllPlayers =
        "UPDATE Players SET age = age + 1;";

    // Targeted single-player interest write. Production sets interest once at
    // conception (via Insert); this exists so the harness can seed the §8
    // check-10 lineage-failure fixtures (an heir forced unwilling) exactly the
    // way SqlUpdateAge seeds ages — and it is the write path Phase 7 gritty
    // events will use when they re-weight a not-yet-revealed heir's interest.
    private const string SqlUpdateInterest =
        "UPDATE Players SET baseball_interest = @interest WHERE player_id = @playerId;";

    // Gritty-event consequence writers (gritty_event_framework.md §4): atomic
    // read-modify-write with the clamp in SQL (the PED-cost precedent), so a
    // consequence never races a stale C#-side read of the current value.
    private const string SqlAdjustFunds =
        "UPDATE Players SET funds = MAX(0, funds + @delta) WHERE player_id = @playerId;";

    private const string SqlAdjustInterest =
        "UPDATE Players SET baseball_interest = MAX(0, MIN(100, baseball_interest + @delta)) WHERE player_id = @playerId;";

    private const string SqlDeletePlayer =
        "DELETE FROM Players WHERE player_id = @playerId;";

    private const string SqlInsertBattingSeason =
        "INSERT INTO Batting_Stats (player_id, season_year, pa, ab, h, doubles, triples, hr, bb, so, rbi, sb, avg, obp, slg, ops) VALUES " +
        "(@playerId, @seasonYear, @pa, @ab, @h, @doubles, @triples, @hr, @bb, @so, @rbi, @sb, @avg, @obp, @slg, @ops);";

    private const string SqlSelectBattingByPlayer =
        "SELECT stat_id, player_id, season_year, pa, ab, h, doubles, triples, hr, bb, so, rbi, sb, avg, obp, slg, ops " +
        "FROM Batting_Stats WHERE player_id = @playerId ORDER BY season_year;";

    // Same shape as the batting loader; rides the UNIQUE(player_id, season_year)
    // prefix (validated via EXPLAIN QUERY PLAN alongside the v4 queries).
    private const string SqlSelectPitchingByPlayer =
        "SELECT stat_id, player_id, season_year, g, gs, w, l, sv, outs_recorded, h_allowed, er, bb, so, ip, era, whip " +
        "FROM Pitching_Stats WHERE player_id = @playerId ORDER BY season_year;";

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

    // Boot-time hydration of the whole RelationshipGraph — a deliberate full
    // scan, same bulk-load-up-front pattern as SqlSelectAllPlayers.
    private const string SqlSelectAllRelationships =
        "SELECT rel_id, player_1_id, player_2_id, affinity_score, type_enum FROM Relationships;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _insertPlayer;
    private readonly SqliteCommand _selectPlayerById;
    private readonly SqliteCommand _selectAllPlayers;
    private readonly SqliteCommand _countPlayers;
    private readonly SqliteCommand _updateFunds;
    private readonly SqliteCommand _updateTeam;
    private readonly SqliteCommand _updateAge;
    private readonly SqliteCommand _ageAllPlayers;
    private readonly SqliteCommand _updateInterest;
    private readonly SqliteCommand _adjustFunds;
    private readonly SqliteCommand _adjustInterest;
    private readonly SqliteCommand _deletePlayer;
    private readonly SqliteCommand _insertBattingSeason;
    private readonly SqliteCommand _selectBattingByPlayer;
    private readonly SqliteCommand _selectPitchingByPlayer;
    private readonly SqliteCommand _upsertFlag;
    private readonly SqliteCommand _selectActiveFlags;
    private readonly SqliteCommand _upsertRelationship;
    private readonly SqliteCommand _selectRelationshipsFor;
    private readonly SqliteCommand _selectAllRelationships;

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
        _updateTeam = Acquire(SqlUpdateTeam, ("@teamId", SqliteType.Integer), ("@playerId", SqliteType.Text));
        _updateAge = Acquire(SqlUpdateAge, ("@age", SqliteType.Integer), ("@playerId", SqliteType.Text));
        _ageAllPlayers = Acquire(SqlAgeAllPlayers);
        _updateInterest = Acquire(SqlUpdateInterest, ("@interest", SqliteType.Integer), ("@playerId", SqliteType.Text));
        _adjustFunds = Acquire(SqlAdjustFunds, ("@delta", SqliteType.Real), ("@playerId", SqliteType.Text));
        _adjustInterest = Acquire(SqlAdjustInterest, ("@delta", SqliteType.Integer), ("@playerId", SqliteType.Text));
        _deletePlayer = Acquire(SqlDeletePlayer, ("@playerId", SqliteType.Text));

        _insertBattingSeason = Acquire(SqlInsertBattingSeason,
            ("@playerId", SqliteType.Text), ("@seasonYear", SqliteType.Integer),
            ("@pa", SqliteType.Integer), ("@ab", SqliteType.Integer), ("@h", SqliteType.Integer),
            ("@doubles", SqliteType.Integer), ("@triples", SqliteType.Integer), ("@hr", SqliteType.Integer),
            ("@bb", SqliteType.Integer), ("@so", SqliteType.Integer), ("@rbi", SqliteType.Integer),
            ("@sb", SqliteType.Integer), ("@avg", SqliteType.Real), ("@obp", SqliteType.Real),
            ("@slg", SqliteType.Real), ("@ops", SqliteType.Real));

        _selectBattingByPlayer = Acquire(SqlSelectBattingByPlayer, ("@playerId", SqliteType.Text));
        _selectPitchingByPlayer = Acquire(SqlSelectPitchingByPlayer, ("@playerId", SqliteType.Text));

        _upsertFlag = Acquire(SqlUpsertFlag,
            ("@playerId", SqliteType.Text), ("@flagName", SqliteType.Text),
            ("@isActive", SqliteType.Integer), ("@setOnDay", SqliteType.Integer));

        _selectActiveFlags = Acquire(SqlSelectActiveFlags, ("@playerId", SqliteType.Text));

        _upsertRelationship = Acquire(SqlUpsertRelationship,
            ("@player1Id", SqliteType.Text), ("@player2Id", SqliteType.Text),
            ("@affinityScore", SqliteType.Integer), ("@typeEnum", SqliteType.Text));

        _selectRelationshipsFor = Acquire(SqlSelectRelationshipsFor, ("@playerId", SqliteType.Text));
        _selectAllRelationships = Acquire(SqlSelectAllRelationships);
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

    /// <summary>Moves a player to a team, or off all rosters (null = free agent; the roster join excludes them).</summary>
    public void SetTeam(string playerId, int? teamId)
    {
        _updateTeam.Parameters["@teamId"].Value = teamId.HasValue ? teamId.Value : DBNull.Value;
        _updateTeam.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_updateTeam);
    }

    /// <summary>
    /// Sets a player's age directly — a targeted single-row write, distinct
    /// from the not-yet-built yearly aging tick (a set-based age = age + 1
    /// over every player, design doc §5.5). Lets the harness seed exact ages
    /// to exercise the heir-mechanics §1.2 direction invariant.
    /// </summary>
    public void SetAge(string playerId, int age)
    {
        _updateAge.Parameters["@age"].Value = age;
        _updateAge.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_updateAge);
    }

    /// <summary>
    /// The §5.5 yearly aging tick: ages every player by one year in a single
    /// set-based statement (its own implicit transaction — one statement per
    /// season rollover, never one per row). Returns the rows touched.
    /// </summary>
    public int AgeAllPlayers() => _db.ExecuteNonQuery(_ageAllPlayers);

    /// <summary>
    /// Sets a player's hidden baseball_interest directly — harness fixture
    /// seeding today (heir mechanics §8 check 10), the Phase 7 gritty-event
    /// write path for re-weighting a not-yet-revealed heir later.
    /// </summary>
    public void SetBaseballInterest(string playerId, int interest)
    {
        _updateInterest.Parameters["@interest"].Value = interest;
        _updateInterest.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_updateInterest);
    }

    /// <summary>
    /// Gritty-event funds writer: atomic delta, floor-clamped at 0 in SQL.
    /// The applier pairs this with a FundsImpulseEvent so the Life sim's
    /// in-memory funds mirror moves identically.
    /// </summary>
    public void AdjustFunds(string playerId, double delta)
    {
        _adjustFunds.Parameters["@delta"].Value = delta;
        _adjustFunds.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_adjustFunds);
    }

    /// <summary>
    /// Gritty-event interest writer (heir_mechanics.md §11.2 — events
    /// re-weight a not-yet-revealed heir): atomic delta, clamped [0,100] in SQL.
    /// </summary>
    public void AdjustBaseballInterest(string playerId, int delta)
    {
        _adjustInterest.Parameters["@delta"].Value = delta;
        _adjustInterest.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_adjustInterest);
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

    public int LoadPitchingSeasons(string playerId, List<PitchingStatsRow> destination)
    {
        destination.Clear();
        _selectPitchingByPlayer.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectPitchingByPlayer);
        while (reader.Read())
        {
            destination.Add(new PitchingStatsRow
            {
                StatId = reader.GetInt64(0),
                PlayerId = reader.GetString(1),
                SeasonYear = reader.GetInt32(2),
                G = reader.GetInt32(3),
                Gs = reader.GetInt32(4),
                W = reader.GetInt32(5),
                L = reader.GetInt32(6),
                Sv = reader.GetInt32(7),
                OutsRecorded = reader.GetInt32(8),
                HAllowed = reader.GetInt32(9),
                Er = reader.GetInt32(10),
                Bb = reader.GetInt32(11),
                So = reader.GetInt32(12),
                Ip = reader.GetDouble(13),
                Era = reader.GetDouble(14),
                Whip = reader.GetDouble(15),
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
            destination.Add(ReadRelationship(reader));
        }
        return destination.Count;
    }

    /// <summary>Bulk-loads every relationship row into <paramref name="destination"/> (cleared first) — RelationshipGraph hydration.</summary>
    public int LoadAllRelationships(List<RelationshipRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAllRelationships);
        while (reader.Read())
        {
            destination.Add(ReadRelationship(reader));
        }
        return destination.Count;
    }

    private static RelationshipRow ReadRelationship(SqliteDataReader reader) => new()
    {
        RelId = reader.GetInt64(0),
        Player1Id = reader.GetString(1),
        Player2Id = reader.GetString(2),
        AffinityScore = reader.GetInt32(3),
        Type = RelationshipTypeMap.FromDbString(reader.GetString(4)),
    };
}
