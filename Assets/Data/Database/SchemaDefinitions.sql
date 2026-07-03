-- ============================================================================
-- Dirt & Diamonds — SQLite Schema (DDL source of truth)
-- Applied idempotently by DatabaseManager.InitializeSchema() on boot.
-- Schema evolution is keyed on PRAGMA user_version (see end of file);
-- never rename/drop a column saved games reference without a migration step.
--
-- Connection-level pragmas (journal_mode=WAL, foreign_keys=ON, synchronous)
-- are set by DatabaseManager on every connection open, NOT in this script,
-- because foreign_keys is per-connection and cannot be persisted here.
-- ============================================================================

BEGIN TRANSACTION;

-- ----------------------------------------------------------------------------
-- Players — one row per living or historical person in the universe.
-- team_id stays unconstrained until a Teams table ships in a later migration.
-- funds is polled by the Gritty Event dispatcher (e.g. "Funds < 500").
-- baseball_interest is the hidden heir stat; detection_risk is PED exposure.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Players (
    player_id         TEXT    PRIMARY KEY,
    first_name        TEXT    NOT NULL,
    last_name         TEXT    NOT NULL,
    age               INTEGER NOT NULL CHECK (age >= 0),
    team_id           INTEGER,
    funds             REAL    NOT NULL DEFAULT 0,
    health_ceiling    INTEGER NOT NULL DEFAULT 100 CHECK (health_ceiling    BETWEEN 0 AND 100),
    recklessness      INTEGER NOT NULL DEFAULT 0   CHECK (recklessness      BETWEEN 0 AND 100),
    baseball_interest INTEGER NOT NULL DEFAULT 0   CHECK (baseball_interest BETWEEN 0 AND 100),
    detection_risk    INTEGER NOT NULL DEFAULT 0   CHECK (detection_risk    BETWEEN 0 AND 100)
) STRICT;

-- ----------------------------------------------------------------------------
-- Batting_Stats — one row per player per season. Counting stats are the
-- source numbers; AVG/OBP/SLG/OPS are denormalized by StatsNormalizer after
-- batch writes so the UI never computes rates at render time.
-- The UNIQUE(player_id, season_year) index doubles as the mandated hot-path
-- index on player_id (leftmost-prefix lookup).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Batting_Stats (
    stat_id     INTEGER PRIMARY KEY,
    player_id   TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    season_year INTEGER NOT NULL,
    pa          INTEGER NOT NULL DEFAULT 0,
    ab          INTEGER NOT NULL DEFAULT 0,
    h           INTEGER NOT NULL DEFAULT 0,
    doubles     INTEGER NOT NULL DEFAULT 0,
    triples     INTEGER NOT NULL DEFAULT 0,
    hr          INTEGER NOT NULL DEFAULT 0,
    bb          INTEGER NOT NULL DEFAULT 0,
    so          INTEGER NOT NULL DEFAULT 0,
    rbi         INTEGER NOT NULL DEFAULT 0,
    sb          INTEGER NOT NULL DEFAULT 0,
    avg         REAL    NOT NULL DEFAULT 0,
    obp         REAL    NOT NULL DEFAULT 0,
    slg         REAL    NOT NULL DEFAULT 0,
    ops         REAL    NOT NULL DEFAULT 0,
    UNIQUE (player_id, season_year)
) STRICT;

-- ----------------------------------------------------------------------------
-- Pitching_Stats — one row per pitcher per season. outs_recorded is the exact
-- integer source of truth for innings; ip/era/whip are denormalized rates.
-- UNIQUE(player_id, season_year) doubles as the mandated player_id index.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Pitching_Stats (
    stat_id       INTEGER PRIMARY KEY,
    player_id     TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    season_year   INTEGER NOT NULL,
    g             INTEGER NOT NULL DEFAULT 0,
    gs            INTEGER NOT NULL DEFAULT 0,
    w             INTEGER NOT NULL DEFAULT 0,
    l             INTEGER NOT NULL DEFAULT 0,
    sv            INTEGER NOT NULL DEFAULT 0,
    outs_recorded INTEGER NOT NULL DEFAULT 0,
    h_allowed     INTEGER NOT NULL DEFAULT 0,
    er            INTEGER NOT NULL DEFAULT 0,
    bb            INTEGER NOT NULL DEFAULT 0,
    so            INTEGER NOT NULL DEFAULT 0,
    ip            REAL    NOT NULL DEFAULT 0,
    era           REAL    NOT NULL DEFAULT 0,
    whip          REAL    NOT NULL DEFAULT 0,
    UNIQUE (player_id, season_year)
) STRICT;

-- ----------------------------------------------------------------------------
-- Relationships — bidirectional affinity graph; one row per unordered pair
-- (canonical ordering player_1_id < player_2_id is enforced by the query
-- layer, uniqueness by the index below). type_enum values mirror the
-- RelationshipType C# enum.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Relationships (
    rel_id         INTEGER PRIMARY KEY,
    player_1_id    TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    player_2_id    TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    affinity_score INTEGER NOT NULL DEFAULT 0 CHECK (affinity_score BETWEEN -100 AND 100),
    type_enum      TEXT    NOT NULL CHECK (type_enum IN ('Rival', 'Friend', 'Partner', 'Child')),
    CHECK (player_1_id <> player_2_id),
    UNIQUE (player_1_id, player_2_id)
) STRICT;

-- Mandated hot-path index: player_1_id is covered by the UNIQUE prefix;
-- player_2_id needs its own for reverse traversal of the graph.
CREATE INDEX IF NOT EXISTS idx_relationships_player_2 ON Relationships(player_2_id);

-- ----------------------------------------------------------------------------
-- Entity_Flags — narrative prerequisites for the Gritty Event System.
-- set_on_day is the absolute game-day ordinal the flag was written, so
-- cascades can fire "seasons later". UNIQUE(player_id, flag_name) doubles as
-- the mandated player_id+flag_name hot-path index.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Entity_Flags (
    flag_id    INTEGER PRIMARY KEY,
    player_id  TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    flag_name  TEXT    NOT NULL,
    is_active  INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
    set_on_day INTEGER,
    UNIQUE (player_id, flag_name)
) STRICT;

-- The event dispatcher polls by flag name across all players; partial index
-- keeps that scan off the dead flags.
CREATE INDEX IF NOT EXISTS idx_entity_flags_active_name
    ON Entity_Flags(flag_name) WHERE is_active = 1;

-- ----------------------------------------------------------------------------
-- Game_Logs — append-only record of simulated games/events. payload is JSON
-- produced by the pooled log writers; player_id is nullable because league-
-- level entries (standings snapshots etc.) have no single subject.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Game_Logs (
    log_id       INTEGER PRIMARY KEY,
    season_year  INTEGER NOT NULL,
    game_day     INTEGER NOT NULL,
    home_team_id INTEGER,
    away_team_id INTEGER,
    player_id    TEXT REFERENCES Players(player_id) ON DELETE SET NULL,
    event_type   TEXT    NOT NULL,
    payload      TEXT
) STRICT;

CREATE INDEX IF NOT EXISTS idx_game_logs_player ON Game_Logs(player_id);
CREATE INDEX IF NOT EXISTS idx_game_logs_day    ON Game_Logs(season_year, game_day);

-- ----------------------------------------------------------------------------
-- Game_State — single-row-per-key save metadata (calendar day, start season
-- year, ...). Read/written only through GameStateQueries; the calendar tick
-- updates current_day inside its batch transaction. `ANY` keeps each value's
-- native SQLite type under STRICT (integers stay integers).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Game_State (
    key   TEXT PRIMARY KEY,
    value ANY  NOT NULL
) STRICT;

COMMIT;

-- Schema version 2 — Phase 2 adds Game_State (additive; IF NOT EXISTS migrates
-- v1 saves in place on boot). Bump with every migration.
PRAGMA user_version = 2;
