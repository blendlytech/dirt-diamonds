using Microsoft.Data.Sqlite;

namespace DirtAndDiamonds.Data;

/// <summary>One persisted Roster_Designations row (schema v14; slice 13-5).</summary>
public readonly record struct RosterDesignationRow(string PlayerId, int TeamId, bool IsActive, long LockedThroughDay)
{
    /// <summary>The strict coverage rule (brief Amendment 1): a row covers a day only before its next lock day.</summary>
    public bool Covers(long day) => LockedThroughDay > day;
}

/// <summary>One player on a <see cref="RosterStatusView"/>.</summary>
public readonly record struct RosterStatusEntry(string PlayerId, bool IsActive, bool IsAvatar);

/// <summary>
/// A Pro team's designations for the locked week, as presentation reads
/// them (slice 13-5, brief D8-A): who is active and inactive, the avatar's
/// status, and the lock's horizon. Read-only; the Roster Status screen is
/// built on it in 15c. Outside lock coverage every player reads as active
/// and <see cref="IsLocked"/> is false.
/// </summary>
public sealed record RosterStatusView(
    int TeamId, long Day, bool IsLocked, long LockedThroughDay,
    IReadOnlyList<RosterStatusEntry> Active, IReadOnlyList<RosterStatusEntry> Inactive,
    bool HasAvatar, bool AvatarIsActive);

/// <summary>
/// Typed query surface for Roster_Designations (slice 13-5): the Sunday
/// lock's upsert, the boot hydrate, a player's prior row and the tier's
/// latest lock, plus the avatar's pitching counts for the lock's ΔP. Same
/// contract as <see cref="ScheduleQueries"/>: constant SQL, pooled prepared
/// commands, parameter-typed; every write joins the caller's open batch.
///
/// Validated on a scratch v14 database (No Blind Queries, brief §0.4,
/// revalidated 2026-11-23): the upsert is a primary-key upsert; the hydrate
/// and the latest lock scan Roster_Designations (≤ 768 rows) with a
/// Team_Tiers primary-key probe (the PlayoffQueries join; tier is not a
/// Teams column); the prior read and the pitching read search their
/// autoindexes. Coverage is strict: <c>locked_through_day &gt; @today</c>.
/// </summary>
public sealed class RosterQueries
{
    private const string SqlUpsert =
        "INSERT INTO Roster_Designations (player_id, team_id, is_active, locked_through_day) " +
        "VALUES (@playerId, @teamId, @isActive, @lockedThroughDay) " +
        "ON CONFLICT (player_id) DO UPDATE SET team_id = excluded.team_id, is_active = excluded.is_active, " +
        "locked_through_day = excluded.locked_through_day;";

    private const string SqlHydrate =
        "SELECT d.player_id, d.team_id, d.is_active, d.locked_through_day FROM Roster_Designations d " +
        "JOIN Team_Tiers tt ON tt.team_id = d.team_id " +
        "WHERE tt.tier = @tier AND d.locked_through_day > @today;";

    private const string SqlPrior =
        "SELECT is_active, locked_through_day FROM Roster_Designations WHERE player_id = @playerId;";

    private const string SqlLatestLock =
        "SELECT COALESCE(MAX(d.locked_through_day), 0) FROM Roster_Designations d " +
        "JOIN Team_Tiers tt ON tt.team_id = d.team_id WHERE tt.tier = @tier;";

    private const string SqlPitchingCounts =
        "SELECT outs_recorded, er FROM Pitching_Stats WHERE player_id = @playerId AND season_year = @seasonYear;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _upsert;
    private readonly SqliteCommand _hydrate;
    private readonly SqliteCommand _prior;
    private readonly SqliteCommand _latestLock;
    private readonly SqliteCommand _pitchingCounts;

    public RosterQueries(DatabaseManager db)
    {
        _db = db;
        _upsert = Acquire(SqlUpsert,
            ("@playerId", SqliteType.Text), ("@teamId", SqliteType.Integer),
            ("@isActive", SqliteType.Integer), ("@lockedThroughDay", SqliteType.Integer));
        _hydrate = Acquire(SqlHydrate, ("@tier", SqliteType.Integer), ("@today", SqliteType.Integer));
        _prior = Acquire(SqlPrior, ("@playerId", SqliteType.Text));
        _latestLock = Acquire(SqlLatestLock, ("@tier", SqliteType.Integer));
        _pitchingCounts = Acquire(SqlPitchingCounts, ("@playerId", SqliteType.Text), ("@seasonYear", SqliteType.Integer));
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

    /// <summary>Writes one player's designation for the locked week (one row per player); joins the caller's open batch.</summary>
    public void Upsert(string playerId, int teamId, bool isActive, long lockedThroughDay)
    {
        SqliteParameterCollection p = _upsert.Parameters;
        p["@playerId"].Value = playerId;
        p["@teamId"].Value = teamId;
        p["@isActive"].Value = isActive ? 1 : 0;
        p["@lockedThroughDay"].Value = lockedThroughDay;
        _db.ExecuteNonQuery(_upsert);
    }

    /// <summary>Every row of the tier that covers <paramref name="today"/> (strict), into <paramref name="destination"/> (cleared first).</summary>
    public int LoadCovering(LeagueTier tier, long today, List<RosterDesignationRow> destination)
    {
        destination.Clear();
        SqliteParameterCollection p = _hydrate.Parameters;
        p["@tier"].Value = (int)tier;
        p["@today"].Value = today;
        using SqliteDataReader reader = _db.ExecuteReader(_hydrate);
        while (reader.Read())
        {
            destination.Add(new RosterDesignationRow(
                reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2) != 0, reader.GetInt64(3)));
        }
        return destination.Count;
    }

    /// <summary>The player's row before this lock overwrites it (C2's source), whatever week it covered.</summary>
    public bool TryGetPrior(string playerId, out bool isActive, out long lockedThroughDay)
    {
        _prior.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_prior);
        if (!reader.Read())
        {
            isActive = true;
            lockedThroughDay = 0;
            return false;
        }
        isActive = reader.GetInt32(0) != 0;
        lockedThroughDay = reader.GetInt64(1);
        return true;
    }

    /// <summary>The tier's latest lock horizon (0 = never locked): covered today iff it is &gt; today.</summary>
    public long LatestLockedThroughDay(LeagueTier tier)
    {
        _latestLock.Parameters["@tier"].Value = (int)tier;
        return Convert.ToInt64(_db.ExecuteScalar(_latestLock));
    }

    /// <summary>A pitcher's season outs and earned runs (the exact integers ERA comes from); false with no row.</summary>
    public bool TryGetPitchingCounts(string playerId, int seasonYear, out long outsRecorded, out long earnedRuns)
    {
        SqliteParameterCollection p = _pitchingCounts.Parameters;
        p["@playerId"].Value = playerId;
        p["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_pitchingCounts);
        if (!reader.Read())
        {
            outsRecorded = 0;
            earnedRuns = 0;
            return false;
        }
        outsRecorded = reader.GetInt64(0);
        earnedRuns = reader.GetInt64(1);
        return true;
    }
}
