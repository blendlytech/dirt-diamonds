using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// Typed query surface for the slice 13-6 pipeline (league_restructure.md §9;
/// brief §4 item 3): College_Enrollment, Recruiting_Offers, Draft_Picks and
/// the flagged Pro free-agent pool. Same contract as <see cref="BaseballQueries"/>:
/// compile-time-constant SQL, commands acquired from the pool once, typed and
/// prepared. No DDL: the three tables are v14's.
///
/// No Blind Queries: every statement here was executed on a scratch v14
/// database (sqlite3 3.53.3 over SchemaDefinitions.sql, foreign_keys ON) on
/// throwaway rows, insert and update paths both, with EXPLAIN QUERY PLAN
/// recorded in the 13-6 brief §11.2.
/// </summary>
public sealed class PipelineQueries
{
    // Plan: SEARCH College_Enrollment USING INDEX sqlite_autoindex (player_id) on conflict.
    private const string SqlUpsertEnrollment =
        "INSERT INTO College_Enrollment (player_id, team_id, entry_season, class_year, declared_season) " +
        "VALUES (@playerId, @teamId, @entrySeason, @classYear, @declaredSeason) " +
        "ON CONFLICT (player_id) DO UPDATE SET team_id = excluded.team_id, class_year = excluded.class_year, " +
        "declared_season = excluded.declared_season;";

    // Plan: SCAN College_Enrollment (≤ ~600 rows, once a rollover).
    private const string SqlAdvanceClassYears =
        "UPDATE College_Enrollment SET class_year = class_year + 1;";

    // Plan: SEARCH College_Enrollment USING INDEX sqlite_autoindex (player_id=?).
    private const string SqlDeleteEnrollment =
        "DELETE FROM College_Enrollment WHERE player_id = @playerId;";

    // Plan: SCAN College_Enrollment (PK-ordered, no temp B-tree).
    private const string SqlSelectEnrollments =
        "SELECT player_id, team_id, entry_season, class_year, declared_season " +
        "FROM College_Enrollment ORDER BY player_id;";

    // Plan: SEARCH College_Enrollment USING INDEX sqlite_autoindex (player_id=?).
    private const string SqlSetDeclared =
        "UPDATE College_Enrollment SET declared_season = @declaredSeason WHERE player_id = @playerId;";

    // Plan: the PK (player_id, team_id, season_year) on conflict.
    private const string SqlUpsertOffer =
        "INSERT INTO Recruiting_Offers (player_id, team_id, season_year, status) " +
        "VALUES (@playerId, @teamId, @seasonYear, @status) " +
        "ON CONFLICT (player_id, team_id, season_year) DO UPDATE SET status = excluded.status;";

    // Plan: SEARCH Recruiting_Offers USING INDEX sqlite_autoindex (player_id=?), then a temp B-tree on team_id.
    private const string SqlSelectOffersFor =
        "SELECT player_id, team_id, season_year, status FROM Recruiting_Offers " +
        "WHERE player_id = @playerId AND season_year = @seasonYear ORDER BY team_id;";

    // Plan: SCAN Recruiting_Offers (~150–200 rows a season, read twice a year; no index, no DDL).
    private const string SqlSelectAcceptedOffers =
        "SELECT player_id, team_id, season_year, status FROM Recruiting_Offers " +
        "WHERE season_year = @seasonYear AND status = 1 ORDER BY player_id;";

    // Plan: SCAN Recruiting_Offers.
    private const string SqlCountAcceptedOffers =
        "SELECT COUNT(*) FROM Recruiting_Offers WHERE season_year = @seasonYear AND status = 1;";

    // Plan: SCAN Recruiting_Offers.
    private const string SqlExpireOpenOffers =
        "UPDATE Recruiting_Offers SET status = 3 WHERE season_year = @seasonYear AND status = 0;";

    private const string SqlInsertDraftPick =
        "INSERT INTO Draft_Picks (season_year, overall_pick, round, team_id, player_id) " +
        "VALUES (@seasonYear, @overallPick, @round, @teamId, @playerId);";

    // Plan: SEARCH Draft_Picks USING INDEX sqlite_autoindex (season_year=?), PK order.
    // Never queried by player_id (no index): "was drafted" is the flag or the in-memory class.
    private const string SqlSelectDraftPicks =
        "SELECT season_year, overall_pick, round, team_id, player_id FROM Draft_Picks " +
        "WHERE season_year = @seasonYear ORDER BY overall_pick;";

    // Plan: SEARCH Draft_Picks USING COVERING INDEX sqlite_autoindex (season_year=?).
    private const string SqlCountDraftPicks =
        "SELECT COUNT(*) FROM Draft_Picks WHERE season_year = @seasonYear;";

    // D3-A's pool. Plan: SEARCH f USING INDEX idx_entity_flags_active_name
    // (flag_name=?), Players / Player_Ratings / Pitcher_Roles PK probes, a temp
    // B-tree for the order. An inactive flag and an unflagged unrostered
    // player are excluded (validated on throwaway rows).
    private const string SqlSelectFreeAgentPool =
        "SELECT p.player_id, p.first_name, p.last_name, r.is_pitcher, COALESCE(pr.role, 0), " +
        "r.bat_power, r.bat_contact, r.bat_discipline, r.pit_stuff, r.pit_control, r.pit_stamina, r.fielding, " +
        "p.age, p.health_ceiling, COALESCE(f.set_on_day, 0) " +
        "FROM Entity_Flags AS f JOIN Players AS p ON p.player_id = f.player_id " +
        "JOIN Player_Ratings AS r ON r.player_id = p.player_id " +
        "LEFT JOIN Pitcher_Roles AS pr ON pr.player_id = p.player_id " +
        "WHERE f.flag_name = @flagName AND f.is_active = 1 AND p.team_id IS NULL ORDER BY p.player_id;";

    // C5's candidate row in one read per tier: the roster join's ratings and
    // role beside age, health_ceiling and the potential row (NULL when a
    // player has none). Plan: SCAN p USING INDEX idx_players_team, the
    // Team_Tiers rowid probe, PK probes on Player_Ratings / Pitcher_Roles /
    // Player_Potential, a temp B-tree for the order's last term (validated on
    // a scratch v14, brief §11.5).
    private const string SqlSelectCandidatesByTier =
        "SELECT p.player_id, p.first_name, p.last_name, p.team_id, r.is_pitcher, COALESCE(pr.role, 0), " +
        "r.bat_power, r.bat_contact, r.bat_discipline, r.pit_stuff, r.pit_control, r.pit_stamina, r.fielding, " +
        "p.age, p.health_ceiling, pp.player_id, pp.bat_power, pp.bat_contact, pp.bat_discipline, " +
        "pp.pit_stuff, pp.pit_control, pp.pit_stamina, pp.fielding " +
        "FROM Players AS p JOIN Player_Ratings AS r ON r.player_id = p.player_id " +
        "LEFT JOIN Pitcher_Roles AS pr ON pr.player_id = p.player_id " +
        "LEFT JOIN Player_Potential AS pp ON pp.player_id = p.player_id " +
        "JOIN Team_Tiers AS tt ON tt.team_id = p.team_id " +
        "WHERE tt.tier = @tier ORDER BY p.team_id, p.player_id;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _selectCandidatesByTier;
    private readonly SqliteCommand _upsertEnrollment;
    private readonly SqliteCommand _advanceClassYears;
    private readonly SqliteCommand _deleteEnrollment;
    private readonly SqliteCommand _selectEnrollments;
    private readonly SqliteCommand _setDeclared;
    private readonly SqliteCommand _upsertOffer;
    private readonly SqliteCommand _selectOffersFor;
    private readonly SqliteCommand _selectAcceptedOffers;
    private readonly SqliteCommand _countAcceptedOffers;
    private readonly SqliteCommand _expireOpenOffers;
    private readonly SqliteCommand _insertDraftPick;
    private readonly SqliteCommand _selectDraftPicks;
    private readonly SqliteCommand _countDraftPicks;
    private readonly SqliteCommand _selectFreeAgentPool;

    public PipelineQueries(DatabaseManager db)
    {
        _db = db;
        _selectCandidatesByTier = Acquire(SqlSelectCandidatesByTier, ("@tier", SqliteType.Integer));
        _upsertEnrollment = Acquire(SqlUpsertEnrollment,
            ("@playerId", SqliteType.Text), ("@teamId", SqliteType.Integer), ("@entrySeason", SqliteType.Integer),
            ("@classYear", SqliteType.Integer), ("@declaredSeason", SqliteType.Integer));
        _advanceClassYears = Acquire(SqlAdvanceClassYears);
        _deleteEnrollment = Acquire(SqlDeleteEnrollment, ("@playerId", SqliteType.Text));
        _selectEnrollments = Acquire(SqlSelectEnrollments);
        _setDeclared = Acquire(SqlSetDeclared, ("@declaredSeason", SqliteType.Integer), ("@playerId", SqliteType.Text));
        _upsertOffer = Acquire(SqlUpsertOffer,
            ("@playerId", SqliteType.Text), ("@teamId", SqliteType.Integer), ("@seasonYear", SqliteType.Integer),
            ("@status", SqliteType.Integer));
        _selectOffersFor = Acquire(SqlSelectOffersFor, ("@playerId", SqliteType.Text), ("@seasonYear", SqliteType.Integer));
        _selectAcceptedOffers = Acquire(SqlSelectAcceptedOffers, ("@seasonYear", SqliteType.Integer));
        _countAcceptedOffers = Acquire(SqlCountAcceptedOffers, ("@seasonYear", SqliteType.Integer));
        _expireOpenOffers = Acquire(SqlExpireOpenOffers, ("@seasonYear", SqliteType.Integer));
        _insertDraftPick = Acquire(SqlInsertDraftPick,
            ("@seasonYear", SqliteType.Integer), ("@overallPick", SqliteType.Integer), ("@round", SqliteType.Integer),
            ("@teamId", SqliteType.Integer), ("@playerId", SqliteType.Text));
        _selectDraftPicks = Acquire(SqlSelectDraftPicks, ("@seasonYear", SqliteType.Integer));
        _countDraftPicks = Acquire(SqlCountDraftPicks, ("@seasonYear", SqliteType.Integer));
        _selectFreeAgentPool = Acquire(SqlSelectFreeAgentPool, ("@flagName", SqliteType.Text));
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
    // Candidates (C5)
    // ------------------------------------------------------------------

    /// <summary>
    /// Appends every rostered, ratings-bearing player in <paramref name="tier"/>
    /// as a <see cref="PipelineCandidateRow"/> (ratings, role, age, health,
    /// potential; stat lines left empty for the caller), in the roster join's
    /// (team_id, player_id) order. The destination is not cleared, so a
    /// caller can gather several tiers.
    /// </summary>
    public int AppendCandidatesByTier(LeagueTier tier, List<PipelineCandidateRow> destination)
    {
        int before = destination.Count;
        _selectCandidatesByTier.Parameters["@tier"].Value = (int)tier;
        using SqliteDataReader reader = _db.ExecuteReader(_selectCandidatesByTier);
        while (reader.Read())
        {
            string id = reader.GetString(0);
            bool hasPotential = !reader.IsDBNull(15);
            destination.Add(new PipelineCandidateRow
            {
                Ratings = new RosterPlayerRow
                {
                    PlayerId = id,
                    FirstName = reader.GetString(1),
                    LastName = reader.GetString(2),
                    TeamId = reader.GetInt32(3),
                    IsPitcher = reader.GetInt64(4) != 0,
                    Role = (PitcherRole)reader.GetInt32(5),
                    BatPower = reader.GetInt32(6),
                    BatContact = reader.GetInt32(7),
                    BatDiscipline = reader.GetInt32(8),
                    PitStuff = reader.GetInt32(9),
                    PitControl = reader.GetInt32(10),
                    PitStamina = reader.GetInt32(11),
                    Fielding = reader.GetInt32(12),
                },
                Age = reader.GetInt32(13),
                HealthCeiling = reader.GetInt32(14),
                HasPotential = hasPotential,
                Potential = hasPotential
                    ? new PlayerPotentialRow
                    {
                        PlayerId = id,
                        BatPower = reader.GetInt32(16),
                        BatContact = reader.GetInt32(17),
                        BatDiscipline = reader.GetInt32(18),
                        PitStuff = reader.GetInt32(19),
                        PitControl = reader.GetInt32(20),
                        PitStamina = reader.GetInt32(21),
                        Fielding = reader.GetInt32(22),
                    }
                    : default,
                Tier = tier,
            });
        }
        return destination.Count - before;
    }

    // ------------------------------------------------------------------
    // College_Enrollment
    // ------------------------------------------------------------------

    /// <summary>Inserts or updates one enrollment row (team, class year and declaration move; entry_season is set once).</summary>
    public void UpsertEnrollment(in CollegeEnrollmentRow row)
    {
        SqliteParameterCollection p = _upsertEnrollment.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@teamId"].Value = row.TeamId;
        p["@entrySeason"].Value = row.EntrySeason;
        p["@classYear"].Value = row.ClassYear;
        p["@declaredSeason"].Value = row.DeclaredSeason.HasValue ? row.DeclaredSeason.Value : DBNull.Value;
        _db.ExecuteNonQuery(_upsertEnrollment);
    }

    /// <summary>Every enrolled player's class year goes up by one (the rollover's step 4). Returns the rows touched.</summary>
    public int AdvanceClassYears() => _db.ExecuteNonQuery(_advanceClassYears);

    /// <summary>A departure: the player leaves College (drafted, graduated undrafted, retired).</summary>
    public void DeleteEnrollment(string playerId)
    {
        _deleteEnrollment.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_deleteEnrollment);
    }

    /// <summary>Sets (or, with null, clears) a player's declared_season.</summary>
    public void SetDeclared(string playerId, int? declaredSeason)
    {
        _setDeclared.Parameters["@declaredSeason"].Value = declaredSeason.HasValue ? declaredSeason.Value : DBNull.Value;
        _setDeclared.Parameters["@playerId"].Value = playerId;
        _db.ExecuteNonQuery(_setDeclared);
    }

    /// <summary>Every enrollment row, by player_id (the destination is cleared first).</summary>
    public int LoadEnrollments(List<CollegeEnrollmentRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectEnrollments);
        while (reader.Read())
        {
            destination.Add(new CollegeEnrollmentRow
            {
                PlayerId = reader.GetString(0),
                TeamId = reader.GetInt32(1),
                EntrySeason = reader.GetInt32(2),
                ClassYear = reader.GetInt32(3),
                DeclaredSeason = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            });
        }
        return destination.Count;
    }

    // ------------------------------------------------------------------
    // Recruiting_Offers
    // ------------------------------------------------------------------

    public void UpsertOffer(in RecruitingOfferRow row)
    {
        SqliteParameterCollection p = _upsertOffer.Parameters;
        p["@playerId"].Value = row.PlayerId;
        p["@teamId"].Value = row.TeamId;
        p["@seasonYear"].Value = row.SeasonYear;
        p["@status"].Value = (int)row.Status;
        _db.ExecuteNonQuery(_upsertOffer);
    }

    /// <summary>One player's offers for a season, by team_id (the avatar's offer list).</summary>
    public int LoadOffersFor(string playerId, int seasonYear, List<RecruitingOfferRow> destination)
    {
        destination.Clear();
        _selectOffersFor.Parameters["@playerId"].Value = playerId;
        _selectOffersFor.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_selectOffersFor);
        ReadOffers(reader, destination);
        return destination.Count;
    }

    /// <summary>A season's accepted offers (every signed recruit), by player_id.</summary>
    public int LoadAcceptedOffers(int seasonYear, List<RecruitingOfferRow> destination)
    {
        destination.Clear();
        _selectAcceptedOffers.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_selectAcceptedOffers);
        ReadOffers(reader, destination);
        return destination.Count;
    }

    /// <summary>The number of accepted offers in a season (Signing Day's persisted output, its done key).</summary>
    public int CountAcceptedOffers(int seasonYear)
    {
        _countAcceptedOffers.Parameters["@seasonYear"].Value = seasonYear;
        return Convert.ToInt32(_db.ExecuteScalar(_countAcceptedOffers) ?? 0);
    }

    /// <summary>Signing Day's tail: anything still open in the season expires (status 3). Returns the rows touched.</summary>
    public int ExpireOpenOffers(int seasonYear)
    {
        _expireOpenOffers.Parameters["@seasonYear"].Value = seasonYear;
        return _db.ExecuteNonQuery(_expireOpenOffers);
    }

    private static void ReadOffers(SqliteDataReader reader, List<RecruitingOfferRow> destination)
    {
        while (reader.Read())
        {
            destination.Add(new RecruitingOfferRow
            {
                PlayerId = reader.GetString(0),
                TeamId = reader.GetInt32(1),
                SeasonYear = reader.GetInt32(2),
                Status = (RecruitingOfferStatus)reader.GetInt32(3),
            });
        }
    }

    // ------------------------------------------------------------------
    // Draft_Picks
    // ------------------------------------------------------------------

    public void InsertDraftPick(in DraftPickRow row)
    {
        SqliteParameterCollection p = _insertDraftPick.Parameters;
        p["@seasonYear"].Value = row.SeasonYear;
        p["@overallPick"].Value = row.OverallPick;
        p["@round"].Value = row.Round;
        p["@teamId"].Value = row.TeamId;
        p["@playerId"].Value = row.PlayerId;
        _db.ExecuteNonQuery(_insertDraftPick);
    }

    /// <summary>A season's draft class, by overall pick.</summary>
    public int LoadDraftPicks(int seasonYear, List<DraftPickRow> destination)
    {
        destination.Clear();
        _selectDraftPicks.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_selectDraftPicks);
        while (reader.Read())
        {
            destination.Add(new DraftPickRow
            {
                SeasonYear = reader.GetInt32(0),
                OverallPick = reader.GetInt32(1),
                Round = reader.GetInt32(2),
                TeamId = reader.GetInt32(3),
                PlayerId = reader.GetString(4),
            });
        }
        return destination.Count;
    }

    /// <summary>The number of picks recorded for a season (the Draft's done key).</summary>
    public int CountDraftPicks(int seasonYear)
    {
        _countDraftPicks.Parameters["@seasonYear"].Value = seasonYear;
        return Convert.ToInt32(_db.ExecuteScalar(_countDraftPicks) ?? 0);
    }

    // ------------------------------------------------------------------
    // The Pro free-agent pool (D3-A)
    // ------------------------------------------------------------------

    /// <summary>
    /// Every flagged, unrostered Pro free agent with ratings, by player_id.
    /// An unflagged unrostered player (an avatar's adult child, an
    /// unrecruited senior, a retiree) is never in it.
    /// </summary>
    public int LoadFreeAgentPool(List<FreeAgentRow> destination)
    {
        destination.Clear();
        _selectFreeAgentPool.Parameters["@flagName"].Value = PipelineFlags.FreeAgent;
        using SqliteDataReader reader = _db.ExecuteReader(_selectFreeAgentPool);
        while (reader.Read())
        {
            destination.Add(new FreeAgentRow
            {
                Ratings = new RosterPlayerRow
                {
                    PlayerId = reader.GetString(0),
                    FirstName = reader.GetString(1),
                    LastName = reader.GetString(2),
                    TeamId = 0,
                    IsPitcher = reader.GetInt64(3) != 0,
                    Role = (PitcherRole)reader.GetInt32(4),
                    BatPower = reader.GetInt32(5),
                    BatContact = reader.GetInt32(6),
                    BatDiscipline = reader.GetInt32(7),
                    PitStuff = reader.GetInt32(8),
                    PitControl = reader.GetInt32(9),
                    PitStamina = reader.GetInt32(10),
                    Fielding = reader.GetInt32(11),
                },
                Age = reader.GetInt32(12),
                HealthCeiling = reader.GetInt32(13),
                SetOnDay = reader.GetInt64(14),
            });
        }
        return destination.Count;
    }
}
