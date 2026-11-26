using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>
/// Typed query surface for slice 13-4: the first writer of Team_Seasons and
/// Playoff_Series, and the postseason (phase 1/2) rows of Schedule_Games.
/// Same contract as <see cref="ScheduleQueries"/>: compile-time constant SQL,
/// commands acquired from the <see cref="DatabaseManager"/> pool once in the
/// ctor, parameter-typed, prepared and reused; every write joins the caller's
/// open batch.
///
/// The DB rows are the postseason's only coordinator (13-4 brief D0-B): a
/// bracket is re-derived from Playoff_Series + Schedule_Games + the
/// Team_Seasons seeds after every commit, never from a JSON copy.
///
/// Validated on a scratch v14 database before use (No Blind Queries,
/// brief §0.4): the completed-game and postseason-day reads search
/// ix_sched_day; Team_Seasons reads and writes go through its primary key.
/// </summary>
public sealed class PlayoffQueries
{
    // D1: the regular season only (phase 0), finals and forfeits (status 1, 2).
    private const string SqlSelectCompletedRegularSeason =
        "SELECT home_team_id, away_team_id, home_score, away_score FROM Schedule_Games " +
        "WHERE season_year = @seasonYear AND tier = @tier AND phase = 0 AND status IN (1, 2) " +
        "ORDER BY day_of_season, game_id;";

    private const string SqlCountUnplayedRegularSeason =
        "SELECT COUNT(*) FROM Schedule_Games " +
        "WHERE season_year = @seasonYear AND tier = @tier AND phase = 0 AND status = 0;";

    // D1: an idempotent full-recompute upsert. The seed is selection's, never touched here.
    private const string SqlUpsertTeamSeason =
        "INSERT INTO Team_Seasons (team_id, season_year, wins, losses, runs_scored, runs_allowed, national_rank, seed) " +
        "VALUES (@teamId, @seasonYear, @wins, @losses, @runsScored, @runsAllowed, @nationalRank, NULL) " +
        "ON CONFLICT (team_id, season_year) DO UPDATE SET wins = excluded.wins, losses = excluded.losses, " +
        "runs_scored = excluded.runs_scored, runs_allowed = excluded.runs_allowed, national_rank = excluded.national_rank;";

    private const string SqlSetSeed =
        "UPDATE Team_Seasons SET seed = @seed WHERE team_id = @teamId AND season_year = @seasonYear;";

    private const string SqlSelectTeamSeasons =
        "SELECT ts.team_id, ts.season_year, ts.wins, ts.losses, ts.runs_scored, ts.runs_allowed, ts.national_rank, ts.seed " +
        "FROM Team_Seasons AS ts JOIN Team_Tiers AS tt ON tt.team_id = ts.team_id " +
        "WHERE ts.season_year = @seasonYear AND tt.tier = @tier ORDER BY ts.team_id;";

    private const string SqlCountSeries =
        "SELECT COUNT(*) FROM Playoff_Series WHERE season_year = @seasonYear AND tier = @tier;";

    private const string SqlInsertSeries =
        "INSERT INTO Playoff_Series (season_year, tier, round, best_of, high_seed_team_id, low_seed_team_id, high_wins, low_wins) " +
        "VALUES (@seasonYear, @tier, @round, @bestOf, @high, @low, 0, 0);";

    private const string SqlLastRowId = "SELECT last_insert_rowid();";

    private const string SqlSelectSeries =
        "SELECT series_id, season_year, tier, round, best_of, high_seed_team_id, low_seed_team_id, high_wins, low_wins, winner_team_id " +
        "FROM Playoff_Series WHERE season_year = @seasonYear AND tier = @tier ORDER BY round, series_id;";

    private const string SqlUpdateSeries =
        "UPDATE Playoff_Series SET high_wins = @highWins, low_wins = @lowWins, winner_team_id = @winner " +
        "WHERE series_id = @seriesId;";

    private const string SqlInsertPostseasonGame =
        "INSERT INTO Schedule_Games (season_year, tier, day_of_season, home_team_id, away_team_id, phase, series_id, game_in_series, status) " +
        "VALUES (@seasonYear, @tier, @day, @home, @away, @phase, @seriesId, @gameInSeries, 0);";

    private const string SqlSelectPostseasonGames =
        "SELECT game_id, season_year, tier, day_of_season, home_team_id, away_team_id, phase, series_id, game_in_series, " +
        "status, home_score, away_score FROM Schedule_Games " +
        "WHERE season_year = @seasonYear AND tier = @tier AND phase IN (1, 2) ORDER BY day_of_season, game_id;";

    // status 1 final, 2 forfeit (the narrow postseason forfeit, D3).
    private const string SqlRecordPostseasonResult =
        "UPDATE Schedule_Games SET status = @status, home_score = @homeScore, away_score = @awayScore " +
        "WHERE game_id = @gameId AND phase IN (1, 2);";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _selectCompleted;
    private readonly SqliteCommand _countUnplayed;
    private readonly SqliteCommand _upsertTeamSeason;
    private readonly SqliteCommand _setSeed;
    private readonly SqliteCommand _selectTeamSeasons;
    private readonly SqliteCommand _countSeries;
    private readonly SqliteCommand _insertSeries;
    private readonly SqliteCommand _lastRowId;
    private readonly SqliteCommand _selectSeries;
    private readonly SqliteCommand _updateSeries;
    private readonly SqliteCommand _insertPostseasonGame;
    private readonly SqliteCommand _selectPostseasonGames;
    private readonly SqliteCommand _recordPostseasonResult;

    public PlayoffQueries(DatabaseManager db)
    {
        _db = db;
        _selectCompleted = Acquire(SqlSelectCompletedRegularSeason,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _countUnplayed = Acquire(SqlCountUnplayedRegularSeason,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _upsertTeamSeason = Acquire(SqlUpsertTeamSeason,
            ("@teamId", SqliteType.Integer), ("@seasonYear", SqliteType.Integer), ("@wins", SqliteType.Integer),
            ("@losses", SqliteType.Integer), ("@runsScored", SqliteType.Integer), ("@runsAllowed", SqliteType.Integer),
            ("@nationalRank", SqliteType.Integer));
        _setSeed = Acquire(SqlSetSeed,
            ("@teamId", SqliteType.Integer), ("@seasonYear", SqliteType.Integer), ("@seed", SqliteType.Integer));
        _selectTeamSeasons = Acquire(SqlSelectTeamSeasons,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _countSeries = Acquire(SqlCountSeries, ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _insertSeries = Acquire(SqlInsertSeries,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer), ("@round", SqliteType.Integer),
            ("@bestOf", SqliteType.Integer), ("@high", SqliteType.Integer), ("@low", SqliteType.Integer));
        _lastRowId = Acquire(SqlLastRowId);
        _selectSeries = Acquire(SqlSelectSeries, ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _updateSeries = Acquire(SqlUpdateSeries,
            ("@seriesId", SqliteType.Integer), ("@highWins", SqliteType.Integer), ("@lowWins", SqliteType.Integer),
            ("@winner", SqliteType.Integer));
        _insertPostseasonGame = Acquire(SqlInsertPostseasonGame,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer), ("@day", SqliteType.Integer),
            ("@home", SqliteType.Integer), ("@away", SqliteType.Integer), ("@phase", SqliteType.Integer),
            ("@seriesId", SqliteType.Integer), ("@gameInSeries", SqliteType.Integer));
        _selectPostseasonGames = Acquire(SqlSelectPostseasonGames,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _recordPostseasonResult = Acquire(SqlRecordPostseasonResult,
            ("@gameId", SqliteType.Integer), ("@status", SqliteType.Integer),
            ("@homeScore", SqliteType.Integer), ("@awayScore", SqliteType.Integer));
    }

    private SqliteCommand Acquire(string sql, params (string Name, SqliteType Type)[] parameters)
    {
        SqliteCommand command = _db.GetPooledCommand(sql);
        if (command.Parameters.Count == 0 && parameters.Length > 0)
        {
            foreach ((string name, SqliteType type) in parameters)
            {
                command.Parameters.Add(name, type);
            }
            command.Prepare();
        }
        return command;
    }

    private static void SetYearTier(SqliteCommand command, int seasonYear, LeagueTier tier)
    {
        command.Parameters["@seasonYear"].Value = seasonYear;
        command.Parameters["@tier"].Value = (int)tier;
    }

    /// <summary>Completed regular-season games (phase 0, final or forfeit) in play order.</summary>
    public int LoadCompletedRegularSeason(int seasonYear, LeagueTier tier, List<CompletedGame> destination)
    {
        destination.Clear();
        SetYearTier(_selectCompleted, seasonYear, tier);
        using SqliteDataReader reader = _db.ExecuteReader(_selectCompleted);
        while (reader.Read())
        {
            destination.Add(new CompletedGame(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        }
        return destination.Count;
    }

    /// <summary>Regular-season rows still unplayed (status 0); selection requires 0.</summary>
    public int CountUnplayedRegularSeason(int seasonYear, LeagueTier tier)
    {
        SetYearTier(_countUnplayed, seasonYear, tier);
        return Convert.ToInt32(_db.ExecuteScalar(_countUnplayed));
    }

    /// <summary>D1 upsert of one team-season's regular-season record and (College) national rank; leaves the seed alone.</summary>
    public void UpsertTeamSeason(int teamId, int seasonYear, int wins, int losses, int runsScored, int runsAllowed, int? nationalRank)
    {
        SqliteParameterCollection p = _upsertTeamSeason.Parameters;
        p["@teamId"].Value = teamId;
        p["@seasonYear"].Value = seasonYear;
        p["@wins"].Value = wins;
        p["@losses"].Value = losses;
        p["@runsScored"].Value = runsScored;
        p["@runsAllowed"].Value = runsAllowed;
        p["@nationalRank"].Value = nationalRank.HasValue ? nationalRank.Value : DBNull.Value;
        _db.ExecuteNonQuery(_upsertTeamSeason);
    }

    /// <summary>Writes (or clears) a postseason seed; the row must already exist (selection upserts first).</summary>
    public void SetSeed(int teamId, int seasonYear, int? seed)
    {
        SqliteParameterCollection p = _setSeed.Parameters;
        p["@teamId"].Value = teamId;
        p["@seasonYear"].Value = seasonYear;
        p["@seed"].Value = seed.HasValue ? seed.Value : DBNull.Value;
        if (_db.ExecuteNonQuery(_setSeed) != 1)
        {
            throw new InvalidOperationException($"No Team_Seasons row for team {teamId} in {seasonYear} to seed.");
        }
    }

    /// <summary>One tier-season's Team_Seasons rows, by team id.</summary>
    public int LoadTeamSeasons(int seasonYear, LeagueTier tier, List<TeamSeasonRow> destination)
    {
        destination.Clear();
        SetYearTier(_selectTeamSeasons, seasonYear, tier);
        using SqliteDataReader reader = _db.ExecuteReader(_selectTeamSeasons);
        while (reader.Read())
        {
            destination.Add(new TeamSeasonRow(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }
        return destination.Count;
    }

    /// <summary>Playoff_Series rows for a tier-season (0 = selection hasn't run).</summary>
    public int CountSeries(int seasonYear, LeagueTier tier)
    {
        SetYearTier(_countSeries, seasonYear, tier);
        return Convert.ToInt32(_db.ExecuteScalar(_countSeries));
    }

    /// <summary>Inserts a 0–0 series; returns its series_id.</summary>
    public long InsertSeries(int seasonYear, LeagueTier tier, int round, int bestOf, int highSeedTeamId, int lowSeedTeamId)
    {
        SqliteParameterCollection p = _insertSeries.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        p["@round"].Value = round;
        p["@bestOf"].Value = bestOf;
        p["@high"].Value = highSeedTeamId;
        p["@low"].Value = lowSeedTeamId;
        _db.ExecuteNonQuery(_insertSeries);
        return Convert.ToInt64(_db.ExecuteScalar(_lastRowId));
    }

    /// <summary>A tier-season's series in (round, series_id) order.</summary>
    public int LoadSeries(int seasonYear, LeagueTier tier, List<PlayoffSeriesRow> destination)
    {
        destination.Clear();
        SetYearTier(_selectSeries, seasonYear, tier);
        using SqliteDataReader reader = _db.ExecuteReader(_selectSeries);
        while (reader.Read())
        {
            destination.Add(new PlayoffSeriesRow(
                reader.GetInt64(0), reader.GetInt32(1), (LeagueTier)reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9)));
        }
        return destination.Count;
    }

    /// <summary>Writes a series' score (and its winner once decided).</summary>
    public void UpdateSeries(long seriesId, int highWins, int lowWins, int? winnerTeamId)
    {
        SqliteParameterCollection p = _updateSeries.Parameters;
        p["@seriesId"].Value = seriesId;
        p["@highWins"].Value = highWins;
        p["@lowWins"].Value = lowWins;
        p["@winner"].Value = winnerTeamId.HasValue ? winnerTeamId.Value : DBNull.Value;
        _db.ExecuteNonQuery(_updateSeries);
    }

    /// <summary>Inserts one certain-to-be-played postseason game (status 0); returns its game_id.</summary>
    public long InsertPostseasonGame(
        int seasonYear, LeagueTier tier, int dayOfSeason, int homeTeamId, int awayTeamId, int phase, long seriesId, int gameInSeries)
    {
        if (phase is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Postseason rows are phase 1 (play-in) or 2 (playoff).");
        }
        SqliteParameterCollection p = _insertPostseasonGame.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        p["@day"].Value = dayOfSeason;
        p["@home"].Value = homeTeamId;
        p["@away"].Value = awayTeamId;
        p["@phase"].Value = phase;
        p["@seriesId"].Value = seriesId;
        p["@gameInSeries"].Value = gameInSeries;
        _db.ExecuteNonQuery(_insertPostseasonGame);
        return Convert.ToInt64(_db.ExecuteScalar(_lastRowId));
    }

    /// <summary>Every postseason row of a tier-season, (day, game_id) order.</summary>
    public int LoadPostseasonGames(int seasonYear, LeagueTier tier, List<PostseasonGameRow> destination)
    {
        destination.Clear();
        SetYearTier(_selectPostseasonGames, seasonYear, tier);
        using SqliteDataReader reader = _db.ExecuteReader(_selectPostseasonGames);
        while (reader.Read())
        {
            destination.Add(new PostseasonGameRow(
                reader.GetInt64(0), reader.GetInt32(1), (LeagueTier)reader.GetInt32(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt64(7), reader.GetInt32(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11)));
        }
        return destination.Count;
    }

    /// <summary>Marks a postseason game final (status 1) or forfeited (status 2) with its score.</summary>
    public void RecordPostseasonResult(long gameId, int status, int homeScore, int awayScore)
    {
        if (status is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A result is final (1) or forfeit (2).");
        }
        SqliteParameterCollection p = _recordPostseasonResult.Parameters;
        p["@gameId"].Value = gameId;
        p["@status"].Value = status;
        p["@homeScore"].Value = homeScore;
        p["@awayScore"].Value = awayScore;
        if (_db.ExecuteNonQuery(_recordPostseasonResult) != 1)
        {
            throw new InvalidOperationException($"Postseason game {gameId} does not exist (or is not phase 1/2).");
        }
    }
}
