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

    // Tier rides a LEFT JOIN + COALESCE(…, 5) so a Teams row missing its
    // Team_Tiers row degrades to MLB (the v6→v7 backfill value) instead of
    // silently vanishing from the load. Plan validated on a v7 scratch db:
    // Teams scan + Team_Tiers PK probe (No Blind Queries).
    private const string SqlSelectAllTeams =
        "SELECT t.team_id, t.city, t.name, t.abbreviation, t.league, t.division, COALESCE(tt.tier, 5) " +
        "FROM Teams AS t LEFT JOIN Team_Tiers AS tt ON tt.team_id = t.team_id ORDER BY t.team_id;";

    // Tier-scoped teams load (schema v7): one ladder tier's league, ordered by
    // team_id so team indexes stay deterministic within the tier. INNER JOIN —
    // a tier-scoped caller by definition wants only tiered teams.
    private const string SqlSelectTeamsByTier =
        "SELECT t.team_id, t.city, t.name, t.abbreviation, t.league, t.division, tt.tier " +
        "FROM Teams AS t JOIN Team_Tiers AS tt ON tt.team_id = t.team_id " +
        "WHERE tt.tier = @tier ORDER BY t.team_id;";

    private const string SqlCountTeams =
        "SELECT COUNT(*) FROM Teams;";

    private const string SqlCountTeamsInTier =
        "SELECT COUNT(*) FROM Team_Tiers WHERE tier = @tier;";

    private const string SqlUpsertTeamTier =
        "INSERT INTO Team_Tiers (team_id, tier) VALUES (@teamId, @tier) " +
        "ON CONFLICT (team_id) DO UPDATE SET tier = excluded.tier;";

    // One team's ladder tier; the COALESCE-through-Teams shape distinguishes a
    // missing team (no row → null scalar) from a merely untiered one (→ MLB).
    private const string SqlSelectTeamTier =
        "SELECT COALESCE(tt.tier, 5) FROM Teams AS t " +
        "LEFT JOIN Team_Tiers AS tt ON tt.team_id = t.team_id WHERE t.team_id = @teamId;";

    private const string SqlUpsertRatings =
        "INSERT INTO Player_Ratings (player_id, is_pitcher, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding) VALUES " +
        "(@playerId, @isPitcher, @batPower, @batContact, @batDiscipline, @pitStuff, @pitControl, @pitStamina, @fielding) " +
        "ON CONFLICT (player_id) DO UPDATE SET is_pitcher = excluded.is_pitcher, " +
        "bat_power = excluded.bat_power, bat_contact = excluded.bat_contact, bat_discipline = excluded.bat_discipline, " +
        "pit_stuff = excluded.pit_stuff, pit_control = excluded.pit_control, pit_stamina = excluded.pit_stamina, " +
        "fielding = excluded.fielding;";

    // Single-player ratings lookup (heir mechanics §9.2: resolving a parent's
    // ratings by id — the roster join in SqlSelectRoster only covers ROSTERED
    // players, but a Partner or a not-yet-succeeded heir may be unrostered).
    // Rides the Player_Ratings PK directly — no join, no plan check needed.
    private const string SqlSelectRatingsById =
        "SELECT player_id, is_pitcher, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding " +
        "FROM Player_Ratings WHERE player_id = @playerId;";

    // Player_Potential writes (schema v10): creation paths (world-gen intake,
    // heir conception, avatar creation) write each player's ceiling once; the
    // development pass never writes potential, only Player_Ratings.
    private const string SqlUpsertPotential =
        "INSERT INTO Player_Potential (player_id, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding) VALUES " +
        "(@playerId, @batPower, @batContact, @batDiscipline, @pitStuff, @pitControl, @pitStamina, @fielding) " +
        "ON CONFLICT (player_id) DO UPDATE SET " +
        "bat_power = excluded.bat_power, bat_contact = excluded.bat_contact, bat_discipline = excluded.bat_discipline, " +
        "pit_stuff = excluded.pit_stuff, pit_control = excluded.pit_control, pit_stamina = excluded.pit_stamina, " +
        "fielding = excluded.fielding;";

    // The development pass's one bulk read: every potential row, once per
    // offseason into a dictionary up front — never row-at-a-time mid-pass. A
    // deliberate full scan (cold, once per simulated year, the 9c season-line
    // precedent); plan validated on a v10 scratch db (No Blind Queries):
    // SCAN Player_Potential USING INDEX sqlite_autoindex_Player_Potential_1.
    private const string SqlSelectAllPotential =
        "SELECT player_id, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding " +
        "FROM Player_Potential ORDER BY player_id;";

    // Single-player potential probe (harness fixtures / a future scouting UI).
    // Plan validated on a v10 scratch db: PK autoindex SEARCH.
    private const string SqlSelectPotentialById =
        "SELECT player_id, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding " +
        "FROM Player_Potential WHERE player_id = @playerId;";

    // The macro-sim's single up-front bulk load. Ordered (team, player) so the
    // simulator's lineup/rotation assignment is deterministic across sessions.
    // Schema v4 adds the Pitcher_Roles LEFT JOIN (COALESCE 0 = None for position
    // players); plan re-validated via EXPLAIN QUERY PLAN — idx_players_team plus
    // both PK autoindexes, same shape as v3 (No Blind Queries).
    // first_name/last_name ride the same Players row already in the join (no
    // new join, no plan change) — the attended-game NPC feed needs a display
    // name and the macro-sim simply ignores the extra columns.
    private const string SqlSelectRoster =
        "SELECT p.player_id, p.first_name, p.last_name, p.team_id, r.is_pitcher, COALESCE(pr.role, 0), " +
        "r.bat_power, r.bat_contact, r.bat_discipline, " +
        "r.pit_stuff, r.pit_control, r.pit_stamina, r.fielding " +
        "FROM Players AS p JOIN Player_Ratings AS r ON r.player_id = p.player_id " +
        "LEFT JOIN Pitcher_Roles AS pr ON pr.player_id = p.player_id " +
        "WHERE p.team_id IS NOT NULL ORDER BY p.team_id, p.player_id;";

    // Tier-scoped roster load (schema v7): the exact SqlSelectRoster shape
    // narrowed to one ladder tier via a Team_Tiers probe — each per-tier
    // LeagueSimulator bulk-loads only its own league instead of every player
    // in the universe. Plan validated on a v7 scratch db: idx_players_team
    // scan + Team_Tiers/Player_Ratings/Pitcher_Roles PK probes (No Blind
    // Queries).
    private const string SqlSelectRosterByTier =
        "SELECT p.player_id, p.first_name, p.last_name, p.team_id, r.is_pitcher, COALESCE(pr.role, 0), " +
        "r.bat_power, r.bat_contact, r.bat_discipline, " +
        "r.pit_stuff, r.pit_control, r.pit_stamina, r.fielding " +
        "FROM Players AS p JOIN Player_Ratings AS r ON r.player_id = p.player_id " +
        "LEFT JOIN Pitcher_Roles AS pr ON pr.player_id = p.player_id " +
        "JOIN Team_Tiers AS tt ON tt.team_id = p.team_id " +
        "WHERE tt.tier = @tier ORDER BY p.team_id, p.player_id;";

    // Signable free agents for succession backfill (heir mechanics §5.4): the
    // roster join's exact column shape over the team_id IS NULL bucket (0
    // stands in for the absent team_id so the reader stays shared), windowed
    // to playable people — of age, not aged out, not health-retired — so an
    // underage heir or a retired avatar can never be "promoted" back onto a
    // roster. Bounds arrive as parameters because the constants live in the
    // Baseball layer's HeirGeneticsProfile, above this one. Plan validated via
    // the sqlite MCP (No Blind Queries): idx_players_team seeks the NULL
    // bucket, both PK autoindexes probe the joins.
    private const string SqlSelectFreeAgents =
        "SELECT p.player_id, p.first_name, p.last_name, 0, r.is_pitcher, COALESCE(pr.role, 0), " +
        "r.bat_power, r.bat_contact, r.bat_discipline, " +
        "r.pit_stuff, r.pit_control, r.pit_stamina, r.fielding " +
        "FROM Players AS p JOIN Player_Ratings AS r ON r.player_id = p.player_id " +
        "LEFT JOIN Pitcher_Roles AS pr ON pr.player_id = p.player_id " +
        "WHERE p.team_id IS NULL AND p.age >= @minAge AND p.age < @maxAge AND p.health_ceiling > @minHealth " +
        "ORDER BY p.player_id;";

    // Schema v4: bullpen role + arsenal writes (world-gen / avatar creation) and
    // the arsenal bulk load (rides the composite PK; ordered so slot-indexed
    // arsenal assembly is deterministic across sessions).
    private const string SqlUpsertPitcherRole =
        "INSERT INTO Pitcher_Roles (player_id, role) VALUES (@playerId, @role) " +
        "ON CONFLICT (player_id) DO UPDATE SET role = excluded.role;";

    // Single-player role lookup (heir mechanics §9.2: a pitcher heir inherits
    // parent A's bullpen role when it has one). Rides the PK directly.
    private const string SqlSelectPitcherRoleById =
        "SELECT role FROM Pitcher_Roles WHERE player_id = @playerId;";

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

    // Tier-scoped rate denormalization (schema v7): the same set-based UPDATEs
    // restricted to players CURRENTLY rostered in the tier, so six per-tier
    // sims flushing on the same day each rewrite only their own league's rows
    // instead of the whole season six times over. Free agents (team_id NULL)
    // fall outside every tier — harmless, their counting stats can't move
    // while unrostered. Plan validated on a v7 scratch db: Batting_Stats
    // UNIQUE-index probe + LIST SUBQUERY over idx_players_team/Team_Tiers PK.
    private const string SqlNormalizeBattingRatesTier =
        "UPDATE Batting_Stats SET " +
        "avg = CASE WHEN ab > 0 THEN CAST(h AS REAL) / ab ELSE 0.0 END, " +
        "obp = CASE WHEN ab + bb > 0 THEN CAST(h + bb AS REAL) / (ab + bb) ELSE 0.0 END, " +
        "slg = CASE WHEN ab > 0 THEN CAST(h + doubles + 2 * triples + 3 * hr AS REAL) / ab ELSE 0.0 END, " +
        "ops = CASE WHEN ab + bb > 0 THEN CAST(h + bb AS REAL) / (ab + bb) ELSE 0.0 END " +
        "    + CASE WHEN ab > 0 THEN CAST(h + doubles + 2 * triples + 3 * hr AS REAL) / ab ELSE 0.0 END " +
        "WHERE season_year = @seasonYear AND player_id IN " +
        "(SELECT p.player_id FROM Players AS p JOIN Team_Tiers AS tt ON tt.team_id = p.team_id WHERE tt.tier = @tier);";

    private const string SqlNormalizePitchingRatesTier =
        "UPDATE Pitching_Stats SET " +
        "ip = outs_recorded / 3.0, " +
        "era = CASE WHEN outs_recorded > 0 THEN 27.0 * er / outs_recorded ELSE 0.0 END, " +
        "whip = CASE WHEN outs_recorded > 0 THEN 3.0 * (bb + h_allowed) / outs_recorded ELSE 0.0 END " +
        "WHERE season_year = @seasonYear AND player_id IN " +
        "(SELECT p.player_id FROM Players AS p JOIN Team_Tiers AS tt ON tt.team_id = p.team_id WHERE tt.tier = @tier);";

    // Per-player season counting lines (Phase 9c): the promotion pass's one
    // bulk read of a completed season, consumed into dictionaries up front —
    // never row-at-a-time mid-sweep. Deliberate full scan: the UNIQUE index
    // leads on player_id, and §7's no-schema-change contract forbids a new
    // season_year index — this is a once-per-offseason cold read. Plan
    // validated via the sqlite MCP (No Blind Queries): SCAN Batting_Stats /
    // SCAN Pitching_Stats.
    private const string SqlSelectSeasonBattingLines =
        "SELECT player_id, pa, ab, h, doubles, triples, hr, bb " +
        "FROM Batting_Stats WHERE season_year = @seasonYear ORDER BY player_id;";

    private const string SqlSelectSeasonPitchingLines =
        "SELECT player_id, outs_recorded, er " +
        "FROM Pitching_Stats WHERE season_year = @seasonYear ORDER BY player_id;";

    private const string SqlLeagueBattingTotals =
        "SELECT COALESCE(SUM(pa), 0), COALESCE(SUM(ab), 0), COALESCE(SUM(h), 0), COALESCE(SUM(doubles), 0), " +
        "COALESCE(SUM(triples), 0), COALESCE(SUM(hr), 0), COALESCE(SUM(bb), 0), COALESCE(SUM(so), 0), " +
        "COALESCE(SUM(rbi), 0) FROM Batting_Stats WHERE season_year = @seasonYear;";

    private const string SqlLeaguePitchingTotals =
        "SELECT COALESCE(SUM(g), 0), COALESCE(SUM(gs), 0), COALESCE(SUM(w), 0), COALESCE(SUM(l), 0), " +
        "COALESCE(SUM(outs_recorded), 0), COALESCE(SUM(h_allowed), 0), COALESCE(SUM(er), 0), " +
        "COALESCE(SUM(bb), 0), COALESCE(SUM(so), 0) FROM Pitching_Stats WHERE season_year = @seasonYear;";

    // Tier-scoped league aggregates (schema v7): the run_monte_carlo_batch
    // per-tier band checks sum one ladder tier's season by the players'
    // CURRENT team's tier. Plan validated on a v7 scratch db: stats scan +
    // Players/Team_Tiers PK probes.
    private const string SqlLeagueBattingTotalsTier =
        "SELECT COALESCE(SUM(bs.pa), 0), COALESCE(SUM(bs.ab), 0), COALESCE(SUM(bs.h), 0), COALESCE(SUM(bs.doubles), 0), " +
        "COALESCE(SUM(bs.triples), 0), COALESCE(SUM(bs.hr), 0), COALESCE(SUM(bs.bb), 0), COALESCE(SUM(bs.so), 0), " +
        "COALESCE(SUM(bs.rbi), 0) FROM Batting_Stats AS bs " +
        "JOIN Players AS p ON p.player_id = bs.player_id " +
        "JOIN Team_Tiers AS tt ON tt.team_id = p.team_id " +
        "WHERE bs.season_year = @seasonYear AND tt.tier = @tier;";

    private const string SqlLeaguePitchingTotalsTier =
        "SELECT COALESCE(SUM(ps.g), 0), COALESCE(SUM(ps.gs), 0), COALESCE(SUM(ps.w), 0), COALESCE(SUM(ps.l), 0), " +
        "COALESCE(SUM(ps.outs_recorded), 0), COALESCE(SUM(ps.h_allowed), 0), COALESCE(SUM(ps.er), 0), " +
        "COALESCE(SUM(ps.bb), 0), COALESCE(SUM(ps.so), 0) FROM Pitching_Stats AS ps " +
        "JOIN Players AS p ON p.player_id = ps.player_id " +
        "JOIN Team_Tiers AS tt ON tt.team_id = p.team_id " +
        "WHERE ps.season_year = @seasonYear AND tt.tier = @tier;";

    private readonly DatabaseManager _db;
    private readonly SqliteCommand _insertTeam;
    private readonly SqliteCommand _selectAllTeams;
    private readonly SqliteCommand _selectTeamsByTier;
    private readonly SqliteCommand _countTeams;
    private readonly SqliteCommand _countTeamsInTier;
    private readonly SqliteCommand _upsertTeamTier;
    private readonly SqliteCommand _selectTeamTier;
    private readonly SqliteCommand _selectRosterByTier;
    private readonly SqliteCommand _normalizeBattingRatesTier;
    private readonly SqliteCommand _normalizePitchingRatesTier;
    private readonly SqliteCommand _leagueBattingTotalsTier;
    private readonly SqliteCommand _leaguePitchingTotalsTier;
    private readonly SqliteCommand _upsertRatings;
    private readonly SqliteCommand _selectRatingsById;
    private readonly SqliteCommand _upsertPotential;
    private readonly SqliteCommand _selectAllPotential;
    private readonly SqliteCommand _selectPotentialById;
    private readonly SqliteCommand _selectRoster;
    private readonly SqliteCommand _selectFreeAgents;
    private readonly SqliteCommand _upsertPitcherRole;
    private readonly SqliteCommand _selectPitcherRoleById;
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
    private readonly SqliteCommand _selectSeasonBattingLines;
    private readonly SqliteCommand _selectSeasonPitchingLines;

    public BaseballQueries(DatabaseManager db)
    {
        _db = db;

        _insertTeam = Acquire(SqlInsertTeam,
            ("@teamId", SqliteType.Integer), ("@city", SqliteType.Text), ("@name", SqliteType.Text),
            ("@abbreviation", SqliteType.Text), ("@league", SqliteType.Text), ("@division", SqliteType.Text));

        _selectAllTeams = Acquire(SqlSelectAllTeams);
        _selectTeamsByTier = Acquire(SqlSelectTeamsByTier, ("@tier", SqliteType.Integer));
        _countTeams = Acquire(SqlCountTeams);
        _countTeamsInTier = Acquire(SqlCountTeamsInTier, ("@tier", SqliteType.Integer));
        _upsertTeamTier = Acquire(SqlUpsertTeamTier,
            ("@teamId", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _selectTeamTier = Acquire(SqlSelectTeamTier, ("@teamId", SqliteType.Integer));
        _selectRosterByTier = Acquire(SqlSelectRosterByTier, ("@tier", SqliteType.Integer));
        _normalizeBattingRatesTier = Acquire(SqlNormalizeBattingRatesTier,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _normalizePitchingRatesTier = Acquire(SqlNormalizePitchingRatesTier,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _leagueBattingTotalsTier = Acquire(SqlLeagueBattingTotalsTier,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));
        _leaguePitchingTotalsTier = Acquire(SqlLeaguePitchingTotalsTier,
            ("@seasonYear", SqliteType.Integer), ("@tier", SqliteType.Integer));

        _upsertRatings = Acquire(SqlUpsertRatings,
            ("@playerId", SqliteType.Text), ("@isPitcher", SqliteType.Integer),
            ("@batPower", SqliteType.Integer), ("@batContact", SqliteType.Integer), ("@batDiscipline", SqliteType.Integer),
            ("@pitStuff", SqliteType.Integer), ("@pitControl", SqliteType.Integer), ("@pitStamina", SqliteType.Integer),
            ("@fielding", SqliteType.Integer));

        _selectRatingsById = Acquire(SqlSelectRatingsById, ("@playerId", SqliteType.Text));

        _upsertPotential = Acquire(SqlUpsertPotential,
            ("@playerId", SqliteType.Text),
            ("@batPower", SqliteType.Integer), ("@batContact", SqliteType.Integer), ("@batDiscipline", SqliteType.Integer),
            ("@pitStuff", SqliteType.Integer), ("@pitControl", SqliteType.Integer), ("@pitStamina", SqliteType.Integer),
            ("@fielding", SqliteType.Integer));

        _selectAllPotential = Acquire(SqlSelectAllPotential);
        _selectPotentialById = Acquire(SqlSelectPotentialById, ("@playerId", SqliteType.Text));

        _selectRoster = Acquire(SqlSelectRoster);

        _selectFreeAgents = Acquire(SqlSelectFreeAgents,
            ("@minAge", SqliteType.Integer), ("@maxAge", SqliteType.Integer), ("@minHealth", SqliteType.Integer));

        _upsertPitcherRole = Acquire(SqlUpsertPitcherRole,
            ("@playerId", SqliteType.Text), ("@role", SqliteType.Integer));

        _selectPitcherRoleById = Acquire(SqlSelectPitcherRoleById, ("@playerId", SqliteType.Text));

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

        _selectSeasonBattingLines = Acquire(SqlSelectSeasonBattingLines, ("@seasonYear", SqliteType.Integer));
        _selectSeasonPitchingLines = Acquire(SqlSelectSeasonPitchingLines, ("@seasonYear", SqliteType.Integer));

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
        ReadTeamRows(reader, destination);
        return destination.Count;
    }

    /// <summary>
    /// Loads one ladder tier's teams (schema v7), ordered by team_id — the
    /// per-tier sims' team-index order is deterministic within the tier.
    /// </summary>
    public int LoadTeamsByTier(LeagueTier tier, List<TeamRow> destination)
    {
        destination.Clear();
        _selectTeamsByTier.Parameters["@tier"].Value = (int)tier;
        using SqliteDataReader reader = _db.ExecuteReader(_selectTeamsByTier);
        ReadTeamRows(reader, destination);
        return destination.Count;
    }

    private static void ReadTeamRows(SqliteDataReader reader, List<TeamRow> destination)
    {
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
                Tier = (LeagueTier)reader.GetInt32(6),
            });
        }
    }

    public int CountTeams() => Convert.ToInt32(_db.ExecuteScalar(_countTeams) ?? 0);

    public int CountTeamsInTier(LeagueTier tier)
    {
        _countTeamsInTier.Parameters["@tier"].Value = (int)tier;
        return Convert.ToInt32(_db.ExecuteScalar(_countTeamsInTier) ?? 0);
    }

    /// <summary>Sets a team's ladder tier (schema v7). World-gen writes one row per team.</summary>
    public void UpsertTeamTier(int teamId, LeagueTier tier)
    {
        SqliteParameterCollection p = _upsertTeamTier.Parameters;
        p["@teamId"].Value = teamId;
        p["@tier"].Value = (int)tier;
        _db.ExecuteNonQuery(_upsertTeamTier);
    }

    /// <summary>
    /// One team's ladder tier — false when the team itself does not exist. A
    /// team missing only its Team_Tiers row reads as MLB, matching the v6→v7
    /// backfill (and <see cref="LoadAllTeams"/>'s COALESCE).
    /// </summary>
    public bool TryGetTeamTier(int teamId, out LeagueTier tier)
    {
        _selectTeamTier.Parameters["@teamId"].Value = teamId;
        object? result = _db.ExecuteScalar(_selectTeamTier);
        if (result is null)
        {
            tier = default;
            return false;
        }
        tier = (LeagueTier)Convert.ToInt32(result);
        return true;
    }

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
    /// Single-player ratings lookup by id — false when the player has no
    /// Player_Ratings row (e.g. a non-baseball Partner NPC). Unlike
    /// <see cref="LoadRoster"/> this is not restricted to rostered players,
    /// so it also resolves an unrostered heir or Partner.
    /// </summary>
    public bool TryGetRatings(string playerId, out PlayerRatingsRow ratings)
    {
        _selectRatingsById.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectRatingsById);
        if (!reader.Read())
        {
            ratings = default;
            return false;
        }
        ratings = new PlayerRatingsRow
        {
            PlayerId = reader.GetString(0),
            IsPitcher = reader.GetInt64(1) != 0,
            BatPower = reader.GetInt32(2),
            BatContact = reader.GetInt32(3),
            BatDiscipline = reader.GetInt32(4),
            PitStuff = reader.GetInt32(5),
            PitControl = reader.GetInt32(6),
            PitStamina = reader.GetInt32(7),
            Fielding = reader.GetInt32(8),
        };
        return true;
    }

    /// <summary>
    /// Writes a player's development ceiling (schema v10). Creation paths only
    /// — the development pass moves Player_Ratings, never the ceiling.
    /// </summary>
    public void UpsertPotential(in PlayerPotentialRow potential)
    {
        SqliteParameterCollection p = _upsertPotential.Parameters;
        p["@playerId"].Value = potential.PlayerId;
        p["@batPower"].Value = potential.BatPower;
        p["@batContact"].Value = potential.BatContact;
        p["@batDiscipline"].Value = potential.BatDiscipline;
        p["@pitStuff"].Value = potential.PitStuff;
        p["@pitControl"].Value = potential.PitControl;
        p["@pitStamina"].Value = potential.PitStamina;
        p["@fielding"].Value = potential.Fielding;
        _db.ExecuteNonQuery(_upsertPotential);
    }

    /// <summary>
    /// Bulk-loads every Player_Potential row into <paramref name="destination"/>
    /// (cleared first), keyed by player_id — the development pass's single
    /// up-front read, once per offseason (deliberate full scan; see the SQL
    /// comment). Mirrors the roster join's load-everything-then-loop shape.
    /// </summary>
    public int LoadAllPotential(Dictionary<string, PlayerPotentialRow> destination)
    {
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectAllPotential);
        while (reader.Read())
        {
            PlayerPotentialRow row = ReadPotentialRow(reader);
            destination.Add(row.PlayerId, row);
        }
        return destination.Count;
    }

    /// <summary>Single-player ceiling probe — false when the player has no Player_Potential row (pre-backfill or non-baseball people).</summary>
    public bool TryGetPotential(string playerId, out PlayerPotentialRow potential)
    {
        _selectPotentialById.Parameters["@playerId"].Value = playerId;
        using SqliteDataReader reader = _db.ExecuteReader(_selectPotentialById);
        if (!reader.Read())
        {
            potential = default;
            return false;
        }
        potential = ReadPotentialRow(reader);
        return true;
    }

    private static PlayerPotentialRow ReadPotentialRow(SqliteDataReader reader) => new()
    {
        PlayerId = reader.GetString(0),
        BatPower = reader.GetInt32(1),
        BatContact = reader.GetInt32(2),
        BatDiscipline = reader.GetInt32(3),
        PitStuff = reader.GetInt32(4),
        PitControl = reader.GetInt32(5),
        PitStamina = reader.GetInt32(6),
        Fielding = reader.GetInt32(7),
    };

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
        ReadRosterRows(reader, destination);
        return destination.Count;
    }

    /// <summary>
    /// Tier-scoped variant of <see cref="LoadRoster"/> (schema v7): every
    /// rostered, baseball-active player whose team plays in <paramref name="tier"/>,
    /// same (team_id, player_id) order. Each per-tier sim's one up-front load.
    /// </summary>
    public int LoadRosterByTier(LeagueTier tier, List<RosterPlayerRow> destination)
    {
        destination.Clear();
        _selectRosterByTier.Parameters["@tier"].Value = (int)tier;
        using SqliteDataReader reader = _db.ExecuteReader(_selectRosterByTier);
        ReadRosterRows(reader, destination);
        return destination.Count;
    }

    private static void ReadRosterRows(SqliteDataReader reader, List<RosterPlayerRow> destination)
    {
        while (reader.Read())
        {
            destination.Add(new RosterPlayerRow
            {
                PlayerId = reader.GetString(0),
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
            });
        }
    }

    /// <summary>
    /// Loads every signable free agent (heir mechanics §5.4 backfill pool):
    /// unrostered players with ratings, inside the caller's playable window —
    /// age in [minAge, maxAge) and health_ceiling above minHealth. TeamId is 0
    /// on every returned row. A cold succession-time read, never per-PA.
    /// </summary>
    public int LoadFreeAgents(List<RosterPlayerRow> destination, int minAge, int maxAge, int minHealth)
    {
        SqliteParameterCollection p = _selectFreeAgents.Parameters;
        p["@minAge"].Value = minAge;
        p["@maxAge"].Value = maxAge;
        p["@minHealth"].Value = minHealth;
        destination.Clear();
        using SqliteDataReader reader = _db.ExecuteReader(_selectFreeAgents);
        ReadRosterRows(reader, destination);
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

    /// <summary>Single-player bullpen-role lookup by id — false when the player has no Pitcher_Roles row (position players).</summary>
    public bool TryGetPitcherRole(string playerId, out PitcherRole role)
    {
        _selectPitcherRoleById.Parameters["@playerId"].Value = playerId;
        object? result = _db.ExecuteScalar(_selectPitcherRoleById);
        if (result is null)
        {
            role = PitcherRole.None;
            return false;
        }
        role = (PitcherRole)Convert.ToInt32(result);
        return true;
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

    /// <summary>Tier-scoped variant (schema v7): rewrites only rows of players currently rostered in the tier.</summary>
    public int NormalizeBattingRates(int seasonYear, LeagueTier tier)
    {
        SqliteParameterCollection p = _normalizeBattingRatesTier.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        return _db.ExecuteNonQuery(_normalizeBattingRatesTier);
    }

    /// <summary>Tier-scoped variant (schema v7): rewrites only rows of players currently rostered in the tier.</summary>
    public int NormalizePitchingRates(int seasonYear, LeagueTier tier)
    {
        SqliteParameterCollection p = _normalizePitchingRatesTier.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        return _db.ExecuteNonQuery(_normalizePitchingRatesTier);
    }

    // ------------------------------------------------------------------
    // Season lines (9c promotion pass)
    // ------------------------------------------------------------------

    /// <summary>
    /// Every player's batting counting line for one season into
    /// <paramref name="destination"/> (cleared first) — the promotion pass's
    /// single bulk read of the completed season (deliberate full scan, once
    /// per offseason; see the SQL comment).
    /// </summary>
    public int LoadSeasonBattingLines(int seasonYear, List<SeasonBattingLine> destination)
    {
        destination.Clear();
        _selectSeasonBattingLines.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_selectSeasonBattingLines);
        while (reader.Read())
        {
            destination.Add(new SeasonBattingLine
            {
                PlayerId = reader.GetString(0),
                Pa = reader.GetInt32(1),
                Ab = reader.GetInt32(2),
                H = reader.GetInt32(3),
                Doubles = reader.GetInt32(4),
                Triples = reader.GetInt32(5),
                Hr = reader.GetInt32(6),
                Bb = reader.GetInt32(7),
            });
        }
        return destination.Count;
    }

    /// <summary>Pitching counterpart of <see cref="LoadSeasonBattingLines"/> (outs + ER are all ERA needs).</summary>
    public int LoadSeasonPitchingLines(int seasonYear, List<SeasonPitchingLine> destination)
    {
        destination.Clear();
        _selectSeasonPitchingLines.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_selectSeasonPitchingLines);
        while (reader.Read())
        {
            destination.Add(new SeasonPitchingLine
            {
                PlayerId = reader.GetString(0),
                OutsRecorded = reader.GetInt32(1),
                Er = reader.GetInt32(2),
            });
        }
        return destination.Count;
    }

    // ------------------------------------------------------------------
    // League aggregates (validation harness / future standings UI)
    // ------------------------------------------------------------------

    /// <summary>Tier-scoped variant (schema v7): one ladder tier's season sums, by the players' current team's tier.</summary>
    public LeagueBattingTotals LoadLeagueBattingTotals(int seasonYear, LeagueTier tier)
    {
        SqliteParameterCollection p = _leagueBattingTotalsTier.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        using SqliteDataReader reader = _db.ExecuteReader(_leagueBattingTotalsTier);
        reader.Read();
        return ReadBattingTotals(reader);
    }

    /// <summary>Tier-scoped variant (schema v7): one ladder tier's season sums, by the players' current team's tier.</summary>
    public LeaguePitchingTotals LoadLeaguePitchingTotals(int seasonYear, LeagueTier tier)
    {
        SqliteParameterCollection p = _leaguePitchingTotalsTier.Parameters;
        p["@seasonYear"].Value = seasonYear;
        p["@tier"].Value = (int)tier;
        using SqliteDataReader reader = _db.ExecuteReader(_leaguePitchingTotalsTier);
        reader.Read();
        return ReadPitchingTotals(reader);
    }

    public LeagueBattingTotals LoadLeagueBattingTotals(int seasonYear)
    {
        _leagueBattingTotals.Parameters["@seasonYear"].Value = seasonYear;
        using SqliteDataReader reader = _db.ExecuteReader(_leagueBattingTotals);
        reader.Read();
        return ReadBattingTotals(reader);
    }

    private static LeagueBattingTotals ReadBattingTotals(SqliteDataReader reader)
    {
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
        return ReadPitchingTotals(reader);
    }

    private static LeaguePitchingTotals ReadPitchingTotals(SqliteDataReader reader)
    {
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
