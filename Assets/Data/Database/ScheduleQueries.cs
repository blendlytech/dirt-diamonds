using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>One persisted Schedule_Games row (schema v14, league_restructure.md §12).</summary>
public readonly struct ScheduledGameRow
{
    public readonly long GameId;
    public readonly int DayOfSeason;
    public readonly int HomeTeamId;
    public readonly int AwayTeamId;

    /// <summary>0 scheduled, 1 final, 2 forfeit (13-5).</summary>
    public readonly int Status;

    public ScheduledGameRow(long gameId, int dayOfSeason, int homeTeamId, int awayTeamId, int status)
    {
        GameId = gameId;
        DayOfSeason = dayOfSeason;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
        Status = status;
    }
}

/// <summary>
/// Typed query surface for Schedule_Games (slice 13-3): the seeded scheduler's
/// inserts, the sims' per-season load, and the played games' results. Same
/// contract as <see cref="BaseballQueries"/>: compile-time constant SQL,
/// commands acquired from the <see cref="DatabaseManager"/> pool once in the
/// ctor, parameter-typed, prepared and reused. Every write joins the caller's
/// open batch. Regular season only (phase 0); 13-4 owns playoff rows.
///
/// Validated on a scratch v14 database before use (No Blind Queries): the
/// season load and the count both search ix_sched_day; the result update is a
/// rowid lookup.
/// </summary>
public sealed class ScheduleQueries
{
    private const string SqlCountRegularSeason =
        "SELECT COUNT(*) FROM Schedule_Games " +
        "WHERE season_year = @seasonYear AND tier = @tier AND phase = 0;";

    private const string SqlInsertRegularSeasonGame =
        "INSERT INTO Schedule_Games (season_year, tier, day_of_season, home_team_id, away_team_id, phase, status) " +
        "VALUES (@seasonYear, @tier, @day, @home, @away, 0, 0);";

    // ORDER BY day then game_id: game ids are assigned in the generator's
    // emit order, so this is the macro sim's play order for each day.
    private const string SqlSelectRegularSeason =
        "SELECT game_id, day_of_season, home_team_id, away_team_id, status FROM Schedule_Games " +
        "WHERE season_year = @seasonYear AND tier = @tier AND phase = 0 " +
        "ORDER BY day_of_season, game_id;";

    private const string SqlRecordFinal =
        "UPDATE Schedule_Games SET status = 1, home_score = @homeScore, away_score = @awayScore " +
        "WHERE game_id = @gameId;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _countRegularSeason;
    private readonly SqliteCommand _insertRegularSeasonGame;
    private readonly SqliteCommand _selectRegularSeason;
    private readonly SqliteCommand _recordFinal;

    public ScheduleQueries(DatabaseManager db)
    {
        _db = db;
        _countRegularSeason = Acquire(SqlCountRegularSeason,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _insertRegularSeasonGame = Acquire(SqlInsertRegularSeasonGame,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer), ("@day", SqliteType.Integer),
            ("@home", SqliteType.Integer), ("@away", SqliteType.Integer));
        _selectRegularSeason = Acquire(SqlSelectRegularSeason,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _recordFinal = Acquire(SqlRecordFinal,
            ("@gameId", SqliteType.Integer), ("@homeScore", SqliteType.Integer), ("@awayScore", SqliteType.Integer));
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

    /// <summary>Regular-season games persisted for one tier-season (0 = not generated yet).</summary>
    public int CountRegularSeasonGames(int seasonYear, LeagueTier tier)
    {
        SqliteParameterCollection p = _countRegularSeason.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        return Convert.ToInt32(_db.ExecuteScalar(_countRegularSeason));
    }

    /// <summary>Inserts one scheduled regular-season game (status 0); joins the caller's open batch.</summary>
    public void InsertRegularSeasonGame(int seasonYear, LeagueTier tier, int dayOfSeason, int homeTeamId, int awayTeamId)
    {
        SqliteParameterCollection p = _insertRegularSeasonGame.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        p["@day"].Value = dayOfSeason;
        p["@home"].Value = homeTeamId;
        p["@away"].Value = awayTeamId;
        _db.ExecuteNonQuery(_insertRegularSeasonGame);
    }

    /// <summary>One tier-season's regular-season games in play order (day, then game id).</summary>
    public int LoadRegularSeason(int seasonYear, LeagueTier tier, List<ScheduledGameRow> destination)
    {
        destination.Clear();
        SqliteParameterCollection p = _selectRegularSeason.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        using SqliteDataReader reader = _db.ExecuteReader(_selectRegularSeason);
        while (reader.Read())
        {
            destination.Add(new ScheduledGameRow(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        return destination.Count;
    }

    /// <summary>Marks a played game final with its score (D4); joins the caller's open batch.</summary>
    public void RecordFinal(long gameId, int homeScore, int awayScore)
    {
        SqliteParameterCollection p = _recordFinal.Parameters;
        p["@gameId"].Value = gameId;
        p["@homeScore"].Value = homeScore;
        p["@awayScore"].Value = awayScore;
        _db.ExecuteNonQuery(_recordFinal);
    }
}
