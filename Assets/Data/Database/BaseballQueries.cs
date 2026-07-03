using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// Typed query surface for the baseball macro-sim: Teams, Player_Ratings, the
/// roster join, season-stat upserts, PED post-game costs, and the rate-stat
/// denormalization UPDATEs consumed by StatsNormalizer. Same contract as
/// <see cref="PlayerQueries"/>: all SQL is compile-time constant, commands are
/// acquired from the <see cref="DatabaseManager"/> pool once in the ctor,
/// parameter-typed, prepared, and reused for the session.
///
/// Join validated against the live schema (EXPLAIN QUERY PLAN: idx_players_team
/// + Player_Ratings PK autoindex) per the No Blind Queries rule.
/// </summary>
public sealed class BaseballQueries
{
    private const string SqlInsertTeam =
        "INSERT INTO Teams (team_id, city, name, abbreviation, league, division) VALUES " +
        "(@teamId, @city, @name, @abbreviation, @league, @division);";

    private const string SqlSelectAllTeams =
        "SELECT team_id, city, name, abbreviation, league, division FROM Teams ORDER BY team_id;";

    private const string SqlCountTeams =
        "SELECT COUNT(*) FROM Teams;";

    private const string SqlUpsertRatings =
        "INSERT INTO Player_Ratings (player_id, is_pitcher, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding) VALUES " +
        "(@playerId, @isPitcher, @batPower, @batContact, @batDiscipline, @pitStuff, @pitControl, @pitStamina, @fielding) " +
        "ON CONFLICT (player_id) DO UPDATE SET is_pitcher = excluded.is_pitcher, " +
        "bat_power = excluded.bat_power, bat_contact = excluded.bat_contact, bat_discipline = excluded.bat_discipline, " +
        "pit_stuff = excluded.pit_stuff, pit_control = excluded.pit_control, pit_stamina = excluded.pit_stamina, " +
        "fielding = excluded.fielding;";

    // The macro-sim's single up-front bulk load. Ordered (team, player) so the
    // simulator's lineup/rotation assignment is deterministic across sessions.
    // Schema v4 adds the Pitcher_Roles LEFT JOIN (COALESCE 0 = None for position
    // players); plan re-validated via EXPLAIN QUERY PLAN — idx_players_team plus
    // both PK autoindexes, same shape as v3 (No Blind Queries).
    private const string SqlSelectRoster =
        "SELECT p.player_id, p.team_id, r.is_pitcher, COALESCE(pr.role, 0), " +
        "r.bat_power, r.bat_contact, r.bat_discipline, " +
        "r.pit_stuff, r.pit_control, r.pit_stamina, r.fielding " +
        "FROM Players AS p JOIN Player_Ratings AS r ON r.player_id = p.player_id " +
        "LEFT JOIN Pitcher_Roles AS pr ON pr.player_id = p.player_id " +
        "WHERE p.team_id IS NOT NULL ORDER BY p.team_id, p.player_id;";

    // Schema v4: bullpen role + arsenal writes (world-gen / avatar creation) and
    // the arsenal bulk load (rides the composite PK; ordered so slot-indexed
    // arsenal assembly is deterministic across sessions).
    private const string SqlUpsertPitcherRole =
        "INSERT INTO Pitcher_Roles (player_id, role) VALUES (@playerId, @role) " +
        "ON CONFLICT (player_id) DO UPDATE SET role = excluded.role;";

    private const string SqlUpsertArsenalPitch =
        "INSERT INTO Pitch_Arsenals (player_id, pitch_type, velocity, movement, usage_weight) VALUES " +
        "(@playerId, @pitchType, @velocity, @movement, @usageWeight) " +
        "ON CONFLICT (player_id, pitch_type) DO UPDATE SET velocity = excluded.velocity, " +
        "movement = excluded.movement, usage_weight = excluded.usage_weight;";

    private const string SqlSelectAllArsenals =
        "SELECT player_id, pitch_type, velocity, movement, usage_weight " +
        "FROM Pitch_Arsenals ORDER BY player_id, pitch_type;";

    // Rides the idx_entity_flags_active_name partial index (is_active = 1).
    private const string SqlSelectActiveFlagPlayerIds =
        "SELECT player_id FROM Entity_Flags WHERE flag_name = @flagName AND is_active = 1;";

    // Additive upserts — since Phase 5 they are THE season-stat write path for
    // both sims: the micro-sim adds a box score per attended game, the macro
    // sim adds one chunk per 7-day cycle flush, and the chunks compose on the
    // same (player, season) row instead of clobbering each other. Unqualified
    // column names in DO UPDATE refer to the existing row; excluded.* is the
    // incoming line.
    private const string SqlAddBattingGameCounts =
        "INSERT INTO Batting_Stats (player_id, season_year, pa, ab, h, doubles, triples, hr, bb, so, rbi, sb) VALUES " +
        "(@playerId, @seasonYear, @pa, @ab, @h, @doubles, @triples, @hr, @bb, @so, @rbi, @sb) " +
        "ON CONFLICT (player_id, season_year) DO UPDATE SET pa = pa + excluded.pa, ab = ab + excluded.ab, " +
        "h = h + excluded.h, doubles = doubles + excluded.doubles, triples = triples + excluded.triples, " +
        "hr = hr + excluded.hr, bb = bb + excluded.bb, so = so + excluded.so, rbi = rbi + excluded.rbi, " +
        "sb = sb + excluded.sb;";

    private const string SqlAddPitchingGameCounts =
        "INSERT INTO Pitching_Stats (player_id, season_year, g, gs, w, l, sv, outs_recorded, h_allowed, er, bb, so) VALUES " +
        "(@playerId, @seasonYear, @g, @gs, @w, @l, @sv, @outsRecorded, @hAllowed, @er, @bb, @so) " +
        "ON CONFLICT (player_id, season_year) DO UPDATE SET g = g + excluded.g, gs = gs + excluded.gs, " +
        "w = w + excluded.w, l = l + excluded.l, sv = sv + excluded.sv, " +
        "outs_recorded = outs_recorded + excluded.outs_recorded, h_allowed = h_allowed + excluded.h_allowed, " +
        "er = er + excluded.er, bb = bb + excluded.bb, so = so + excluded.so;";

    // Play-by-play / box-score log rows (micro doc §10). Schema validated via
    // the SQLite MCP before this query was written (No Blind Queries).
    private const string SqlInsertGameLog =
        "INSERT INTO Game_Logs (season_year, game_day, home_team_id, away_team_id, player_id, event_type, payload) VALUES " +
        "(@seasonYear, @gameDay, @homeTeamId, @awayTeamId, @playerId, @eventType, @payload);";

    // §6 post-game PED hook: erode health_ceiling, raise detection_risk, both
    // clamped in SQL so the CHECK bounds can never reject the write.
    private const string SqlApplyPedGameCosts =
        "UPDATE Players SET health_ceiling = MAX(0, health_ceiling - @healthCost), " +
        "detection_risk = MIN(100, detection_risk + @riskGain) WHERE player_id = @playerId;";

    // Rate-stat denormalization (StatsNormalizer). SQLite UPDATE expressions read
    // the pre-update row, so ops must restate the obp and slg expressions.
    // TB = h + doubles + 2*triples + 3*hr (singles carry 1 base already in h).
    private const string SqlNormalizeBattingRates =
        "UPDATE Batting_Stats SET " +
        "avg = CASE WHEN ab > 0 THEN CAST(h AS REAL) / ab ELSE 0.0 END, " +
        "obp = CASE WHEN ab + bb > 0 THEN CAST(h + bb AS REAL) / (ab + bb) ELSE 0.0 END, " +
        "slg = CASE WHEN ab > 0 THEN CAST(h + doubles + 2 * triples + 3 * hr AS REAL) / ab ELSE 0.0 END, " +
        "ops = CASE WHEN ab + bb > 0 THEN CAST(h + bb AS REAL) / (ab + bb) ELSE 0.0 END " +
        "    + CASE WHEN ab > 0 THEN CAST(h + doubles + 2 * triples + 3 * hr AS REAL) / ab ELSE 0.0 END " +
        "WHERE season_year = @seasonYear;";

    // ip is real innings (outs/3, decimal thirds), not the baseball ".1/.2"
    // notation — that is a UI formatting concern. ERA = 9*er/ip = 27*er/outs.
    private const string SqlNormalizePitchingRates =
        "UPDATE Pitching_Stats SET " +
        "ip = outs_recorded / 3.0, " +
        "era = CASE WHEN outs_recorded > 0 THEN 27.0 * er / outs_recorded ELSE 0.0 END, " +
        "whip = CASE WHEN outs_recorded > 0 THEN 3.0 * (bb + h_allowed) / outs_recorded ELSE 0.0 END " +
        "WHERE season_year = @seasonYear;";

    private const string SqlLeagueBattingTotals =
        "SELECT COALESCE(SUM(pa), 0), COALESCE(SUM(ab), 0), COALESCE(SUM(h), 0), COALESCE(SUM(doubles), 0), " +
        "COALESCE(SUM(triples), 0), COALESCE(SUM(hr), 0), COALESCE(SUM(bb), 0), COALESCE(SUM(so), 0), " +
        "COALESCE(SUM(rbi), 0) FROM Batting_Stats WHERE season_year = @seasonYear;";

    private const string SqlLeaguePitchingTotals =
        "SELECT COALESCE(SUM(g), 0), COALESCE(SUM(gs), 0), COALESCE(SUM(w), 0), COALESCE(SUM(l), 0), " +
        "COALESCE(SUM(outs_recorded), 0), COALESCE(SUM(h_allowed), 0), COALESCE(SUM(er), 0), " +
        "COALESCE(SUM(bb), 0), COALESCE(SUM(so), 0) FROM Pitching_Stats WHERE season_year = @seasonYear;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _insertTeam;
    private readonly SqliteCommand _selectAllTeams;
    private readonly SqliteCommand _countTeams;
    private readonly SqliteCommand _upsertRatings;
    private readonly SqliteCommand _selectRoster;
    private readonly SqliteCommand _upsertPitcherRole;
    private readonly SqliteCommand _upsertArsenalPitch;
    private readonly SqliteCommand _selectAllArsenals;
    private readonly SqliteCommand _selectActiveFlagPlayerIds;
    private readonly SqliteCommand _addBattingGameCounts;
    private readonly SqliteCommand _addPitchingGameCounts;
    private readonly SqliteCommand _insertGameLog;
    private readonly SqliteCommand _applyPedGameCosts;
    private readonly SqliteCommand _normalizeBattingRates;
    private readonly SqliteCommand _normalizePitchingRates;
    private readonly SqliteCommand _leagueBattingTotals;
    private readonly SqliteCommand _leaguePitchingTotals;

    public BaseballQueries(DatabaseManager db)
    {
        _db = db;

        _insertTeam = Acquire(SqlInsertTeam,
            ("@teamId", SqliteType.Integer), ("@city", SqliteType.Text), ("@name", SqliteType.Text),
            ("@abbreviation", SqliteType.Text), ("@league", SqliteType.Text), ("@division", SqliteType.Text));

        _selectAllTeams = Acquire(SqlSelectAllTeams);
        _countTeams = Acquire(SqlCountTeams);

        _upsertRatings = Acquire(SqlUpsertRatings,
            ("@playerId", SqliteType.Text), ("@isPitcher", SqliteType.Integer),
            ("@batPower", SqliteType.Integer), ("@batContact", SqliteType.Integer), ("@batDiscipline", SqliteType.Integer),
            ("@pitStuff", SqliteType.Integer), ("@pitControl", SqliteType.Integer), ("@pitStamina", SqliteType.Integer),
            ("@fielding", SqliteType.Integer));

        _selectRoster = Acquire(SqlSelectRoster);

        _upsertPitcherRole = Acquire(SqlUpsertPitcherRole,
            ("@playerId", SqliteType.Text), ("@role", SqliteType.Integer));

        _upsertArsenalPitch = Acquire(SqlUpsertArsenalPitch,
            ("@playerId", SqliteType.Text), ("@pitchType", SqliteType.Text),
            ("@velocity", SqliteType.Integer), ("@movement", SqliteType.Integer),
            ("@usageWeight", SqliteType.Integer));

        _selectAllArsenals = Acquire(SqlSelectAllArsenals);
        _selectActiveFlagPlayerIds = Acquire(SqlSelectActiveFlagPlayerIds, ("@flagName", SqliteType.Text));

        _addBattingGameCounts = Acquire(SqlAddBattingGameCounts,
            ("@playerId", SqliteType.Text), ("@seasonYear", SqliteType.Integer),
            ("@pa", SqliteType.Integer), ("@ab", SqliteType.Integer), ("@h", SqliteType.Integer),
            ("@doubles", SqliteType.Integer), ("@triples", SqliteType.Integer), ("@hr", SqliteType.Integer),
            ("@bb", SqliteType.Integer), ("@so", SqliteType.Integer), ("@rbi", SqliteType.Integer),
            ("@sb", SqliteType.Integer));

        _addPitchingGameCounts = Acquire(SqlAddPitchingGameCounts,
            ("@playerId", SqliteType.Text), ("@seasonYear", SqliteType.Integer),
            ("@g", SqliteType.Integer), ("@gs", SqliteType.Integer), ("@w", SqliteType.Integer),
            ("@l", SqliteType.Integer), ("@sv", SqliteType.Integer), ("@outsRecorded", SqliteType.Integer),
            ("@hAllowed", SqliteType.Integer), ("@er", SqliteType.Integer), ("@bb", SqliteType.Integer),
            ("@so", SqliteType.Integer));

        _insertGameLog = Acquire(SqlInsertGameLog,
            ("@seasonYear", SqliteType.Integer), ("@gameDay", SqliteType.Integer),
            ("@homeTeamId", SqliteType.Integer), ("@awayTeamId", SqliteType.Integer),
            ("@playerId", SqliteType.Text), ("@eventType", SqliteType.Text), ("@payload", SqliteType.Text));

        _applyPedGameCosts = Acquire(SqlApplyPedGameCosts,
            ("@healthCost", SqliteType.Integer), ("@riskGain", SqliteType.Integer), ("@playerId", SqliteType.Text));

        _normalizeBattingRates = Acquire(SqlNormalizeBattingRates, ("@seasonYear", SqliteType.Integer));
        _normalizePitchingRates = Acquire(SqlNormalizePitchingRates, ("@seasonYear", SqliteType.Integer));
        _leagueBattingTotals = Acquire(SqlLeagueBattingTotals, ("@seasonYear", SqliteType.Integer));
        _leaguePitchingTotals = Acquire(SqlLeaguePitchingTotals, ("@seasonYear", SqliteType.Integer));
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
    // Teams
    // ------------------------------------------------------------------

    public void InsertTeam(in TeamRow team)
    {
        SqliteParameterCollection p = _insertTeam.Parameters;
        p["@teamId"].Value = team.TeamId;
        p["@city"].Value = team.City;
        p["@name"].Value = team.Name;
        p["@abbreviation"].Value = team.Abbreviation;
        p["@league"].Value = team.League is null ? DBNull.Value : team.League;
        p["@division"].Value = team.Division is null ? DBNull.Value : team.Division;
        _db.ExecuteNonQuery(_insertTeam);
    }

    public int LoadAllTeams(List<TeamRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAllTeams);
        while (reader.Read())
        {
            destination.Add(new TeamRow
            {
                TeamId = reader.GetInt32(0),
                City = reader.GetString(1),
                Name = reader.GetString(2),
                Abbreviation = reader.GetString(3),
                League = reader.IsDBNull(4) ? null : reader.GetString(4),
                Division = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return destination.Count;
    }

    public int CountTeams() => Convert.ToInt32(_db.ExecuteScalar(_countTeams) ?? 0);

    // ------------------------------------------------------------------
    // Ratings & roster
    // ------------------------------------------------------------------

    public void UpsertRatings(in PlayerRatingsRow ratings)
    {
        SqliteParameterCollection p = _upsertRatings.Parameters;
        p["@playerId"].Value = ratings.PlayerId;
        p["@isPitcher"].Value = ratings.IsPitcher ? 1 : 0;
        p["@batPower"].Value = ratings.BatPower;
        p["@batContact"].Value = ratings.BatContact;
        p["@batDiscipline"].Value = ratings.BatDiscipline;
        p["@pitStuff"].Value = ratings.PitStuff;
        p["@pitControl"].Value = ratings.PitControl;
        p["@pitStamina"].Value = ratings.PitStamina;
        p["@fielding"].Value = ratings.Fielding;
        _db.ExecuteNonQuery(_upsertRatings);
    }

    /// <summary>
    /// Bulk-loads every rostered, baseball-active player (Players ⋈ Player_Ratings,
    /// team_id set) into <paramref name="destination"/> (cleared first), ordered by
    /// (team_id, player_id). The macro-sim calls this once at load — never
    /// row-at-a-time queries mid-simulation.
    /// </summary>
    public int LoadRoster(List<RosterPlayerRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectRoster);
        while (reader.Read())
        {
            destination.Add(new RosterPlayerRow
            {
                PlayerId = reader.GetString(0),
                TeamId = reader.GetInt32(1),
                IsPitcher = reader.GetInt64(2) != 0,
                Role = (PitcherRole)reader.GetInt32(3),
                BatPower = reader.GetInt32(4),
                BatContact = reader.GetInt32(5),
                BatDiscipline = reader.GetInt32(6),
                PitStuff = reader.GetInt32(7),
                PitControl = reader.GetInt32(8),
                PitStamina = reader.GetInt32(9),
                Fielding = reader.GetInt32(10),
            });
        }
        return destination.Count;
    }

    /// <summary>Sets a pitcher's bullpen role (schema v4). Position players never get a row.</summary>
    public void UpsertPitcherRole(string playerId, PitcherRole role)
    {
        if (role == PitcherRole.None)
        {
            throw new ArgumentException("PitcherRole.None is the absence of a row, not a storable role.", nameof(role));
        }
        SqliteParameterCollection p = _upsertPitcherRole.Parameters;
        p["@playerId"].Value = playerId;
        p["@role"].Value = (int)role;
        _db.ExecuteNonQuery(_upsertPitcherRole);
    }

    /// <summary>Writes one pitch of a pitcher's arsenal (schema v4).</summary>
    public void UpsertArsenalPitch(in PitchArsenalRow pitch)
    {
        SqliteParameterCollection p = _upsertArsenalPitch.Parameters;
        p["@playerId"].Value = pitch.PlayerId;
        p["@pitchType"].Value = PitchTypeName(pitch.Type);
        p["@velocity"].Value = pitch.Velocity;
        p["@movement"].Value = pitch.Movement;
        p["@usageWeight"].Value = pitch.UsageWeight;
        _db.ExecuteNonQuery(_upsertArsenalPitch);
    }

    /// <summary>
    /// Bulk-loads every arsenal row ordered (player_id, pitch_type) — the sims'
    /// one up-front load, mirroring <see cref="LoadRoster"/>.
    /// </summary>
    public int LoadAllArsenals(List<PitchArsenalRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAllArsenals);
        while (reader.Read())
        {
            destination.Add(new PitchArsenalRow
            {
                PlayerId = reader.GetString(0),
                Type = ParsePitchType(reader.GetString(1)),
                Velocity = reader.GetInt32(2),
                Movement = reader.GetInt32(3),
                UsageWeight = reader.GetInt32(4),
            });
        }
        return destination.Count;
    }

    /// <summary>Enum ↔ stored-name mapping for Pitch_Arsenals.pitch_type (CHECK-constrained).</summary>
    public static string PitchTypeName(PitchType type) => type switch
    {
        PitchType.Fastball => "Fastball",
        PitchType.Breaking => "Breaking",
        PitchType.Offspeed => "Offspeed",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static PitchType ParsePitchType(string name) => name switch
    {
        "Fastball" => PitchType.Fastball,
        "Breaking" => PitchType.Breaking,
        "Offspeed" => PitchType.Offspeed,
        _ => throw new InvalidOperationException($"Unknown pitch_type '{name}' in Pitch_Arsenals."),
    };

    /// <summary>Player ids whose named flag is currently active (e.g. "ped_active"), via the partial index.</summary>
    public int LoadActiveFlagPlayerIds(string flagName, List<string> destination)
    {
        destination.Clear();
        _selectActiveFlagPlayerIds.Parameters["@flagName"].Value = flagName;
        using SqliteDataReader reader = _db.ExecuteReader(_selectActiveFlagPlayerIds);
        while (reader.Read())
        {
            destination.Add(reader.GetString(0));
        }
        return destination.Count;
    }

    // ------------------------------------------------------------------
    // Season stat flush (each sim's own batch; additive — see SQL comment)
    // ------------------------------------------------------------------

    /// <summary>Adds one batting chunk (attended game or macro cycle) to the (player, season) row.</summary>
    public void AddBattingGameCounts(
        string playerId, int seasonYear,
        int pa, int ab, int h, int doubles, int triples, int hr, int bb, int so, int rbi, int sb)
    {
        SqliteParameterCollection p = _addBattingGameCounts.Parameters;
        p["@playerId"].Value = playerId;
        p["@seasonYear"].Value = seasonYear;
        p["@pa"].Value = pa;
        p["@ab"].Value = ab;
        p["@h"].Value = h;
        p["@doubles"].Value = doubles;
        p["@triples"].Value = triples;
        p["@hr"].Value = hr;
        p["@bb"].Value = bb;
        p["@so"].Value = so;
        p["@rbi"].Value = rbi;
        p["@sb"].Value = sb;
        _db.ExecuteNonQuery(_addBattingGameCounts);
    }

    /// <summary>Adds one pitching chunk (attended game or macro cycle) to the (player, season) row.</summary>
    public void AddPitchingGameCounts(
        string playerId, int seasonYear,
        int g, int gs, int w, int l, int sv, int outsRecorded, int hAllowed, int er, int bb, int so)
    {
        SqliteParameterCollection p = _addPitchingGameCounts.Parameters;
        p["@playerId"].Value = playerId;
        p["@seasonYear"].Value = seasonYear;
        p["@g"].Value = g;
        p["@gs"].Value = gs;
        p["@w"].Value = w;
        p["@l"].Value = l;
        p["@sv"].Value = sv;
        p["@outsRecorded"].Value = outsRecorded;
        p["@hAllowed"].Value = hAllowed;
        p["@er"].Value = er;
        p["@bb"].Value = bb;
        p["@so"].Value = so;
        _db.ExecuteNonQuery(_addPitchingGameCounts);
    }

    /// <summary>Appends one play-by-play / box-score row to Game_Logs (micro doc §10).</summary>
    public void InsertGameLog(
        int seasonYear, int gameDay, int? homeTeamId, int? awayTeamId,
        string? playerId, string eventType, string? payload)
    {
        SqliteParameterCollection p = _insertGameLog.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@gameDay"].Value = gameDay;
        p["@homeTeamId"].Value = homeTeamId.HasValue ? homeTeamId.Value : DBNull.Value;
        p["@awayTeamId"].Value = awayTeamId.HasValue ? awayTeamId.Value : DBNull.Value;
        p["@playerId"].Value = playerId is null ? DBNull.Value : playerId;
        p["@eventType"].Value = eventType;
        p["@payload"].Value = payload is null ? DBNull.Value : payload;
        _db.ExecuteNonQuery(_insertGameLog);
    }

    public void ApplyPedGameCosts(string playerId, int healthCost, int riskGain)
    {
        SqliteParameterCollection p = _applyPedGameCosts.Parameters;
        p["@healthCost"].Value = healthCost;
        p["@riskGain"].Value = riskGain;
        p["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_applyPedGameCosts);
    }

    // ------------------------------------------------------------------
    // Rate denormalization (StatsNormalizer)
    // ------------------------------------------------------------------

    public int NormalizeBattingRates(int seasonYear)
    {
        _normalizeBattingRates.Parameters["@seasonYear"].Value = seasonYear;
        return _db.ExecuteNonQuery(_normalizeBattingRates);
    }

    public int NormalizePitchingRates(int seasonYear)
    {
        _normalizePitchingRates.Parameters["@seasonYear"].Value = seasonYear;
        return _db.ExecuteNonQuery(_normalizePitchingRates);
    }

    // ------------------------------------------------------------------
    // League aggregates (validation harness / future standings UI)
    // ------------------------------------------------------------------

    public LeagueBattingTotals LoadLeagueBattingTotals(int seasonYear)
    {
        _leagueBattingTotals.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_leagueBattingTotals);
        reader.Read();
        return new LeagueBattingTotals
        {
            Pa = reader.GetInt64(0),
            Ab = reader.GetInt64(1),
            H = reader.GetInt64(2),
            Doubles = reader.GetInt64(3),
            Triples = reader.GetInt64(4),
            Hr = reader.GetInt64(5),
            Bb = reader.GetInt64(6),
            So = reader.GetInt64(7),
            Rbi = reader.GetInt64(8),
        };
    }

    public LeaguePitchingTotals LoadLeaguePitchingTotals(int seasonYear)
    {
        _leaguePitchingTotals.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_leaguePitchingTotals);
        reader.Read();
        return new LeaguePitchingTotals
        {
            G = reader.GetInt64(0),
            Gs = reader.GetInt64(1),
            W = reader.GetInt64(2),
            L = reader.GetInt64(3),
            OutsRecorded = reader.GetInt64(4),
            HAllowed = reader.GetInt64(5),
            Er = reader.GetInt64(6),
            Bb = reader.GetInt64(7),
            So = reader.GetInt64(8),
        };
    }
}
