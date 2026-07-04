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

-- ----------------------------------------------------------------------------
-- Teams — league structure (Phase 3 / schema v3). team_id is the value the
-- previously unconstrained Players.team_id points at; rosters and schedules
-- group by it. A real FK on Players.team_id needs a Players table rebuild and
-- is deferred — the relationship is enforced at the query layer for now, with
-- the mandated hot-path index (idx_players_team) added below.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Teams (
    team_id      INTEGER PRIMARY KEY,
    city         TEXT    NOT NULL,
    name         TEXT    NOT NULL,
    abbreviation TEXT    NOT NULL,
    league       TEXT,
    division     TEXT
) STRICT;

-- Mandated hot-path index: the macro-sim bulk-loads rosters grouped by team_id.
CREATE INDEX IF NOT EXISTS idx_players_team ON Players(team_id);

-- ----------------------------------------------------------------------------
-- Player_Ratings — baseball attributes driving the PA outcome model
-- (docs/design/baseball_pa_outcome_model.md). Split from Players so the life-
-- sim core table stays untouched and the v2→v3 migration is purely additive
-- (new table via IF NOT EXISTS, no ALTER on Players). One row per baseball-
-- active player; the macro-sim JOINs it to Players at roster-load time.
-- 50 = league-average on every 0–100 rating scale. is_pitcher tags who bats
-- vs. who pitches when lineups/rotations are built.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Player_Ratings (
    player_id      TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    is_pitcher     INTEGER NOT NULL DEFAULT 0  CHECK (is_pitcher     IN (0, 1)),
    bat_power      INTEGER NOT NULL DEFAULT 50 CHECK (bat_power      BETWEEN 0 AND 100),
    bat_contact    INTEGER NOT NULL DEFAULT 50 CHECK (bat_contact    BETWEEN 0 AND 100),
    bat_discipline INTEGER NOT NULL DEFAULT 50 CHECK (bat_discipline BETWEEN 0 AND 100),
    pit_stuff      INTEGER NOT NULL DEFAULT 50 CHECK (pit_stuff      BETWEEN 0 AND 100),
    pit_control    INTEGER NOT NULL DEFAULT 50 CHECK (pit_control    BETWEEN 0 AND 100),
    pit_stamina    INTEGER NOT NULL DEFAULT 50 CHECK (pit_stamina    BETWEEN 0 AND 100),
    fielding       INTEGER NOT NULL DEFAULT 50 CHECK (fielding       BETWEEN 0 AND 100)
) STRICT;

-- ----------------------------------------------------------------------------
-- Pitcher_Roles — schema v4. Starter/reliever split for bullpens (micro doc
-- §8.5): one row per baseball-active pitcher, none for position players.
-- Kept as a separate table (not an ALTER on Player_Ratings) so the v3→v4
-- migration stays purely additive and this script remains the whole migration,
-- the same pattern Player_Ratings used for v2→v3. role mirrors the C#
-- PitcherRole enum: 1 = Starter, 2 = Reliever. Consistency with
-- Player_Ratings.is_pitcher is a query-layer invariant (role row ⟺ is_pitcher).
-- The PK doubles as the mandated hot-path index for the roster join.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Pitcher_Roles (
    player_id TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    role      INTEGER NOT NULL CHECK (role IN (1, 2))
) STRICT;

-- v3→v4 backfill: every pre-v4 pitcher was a complete-game starter (rosters
-- were 9 + 5 with no relievers), so they migrate as role 1. INSERT OR IGNORE
-- keeps this idempotent on every boot; post-v4 code writes role rows
-- explicitly at player creation, so this only ever touches migrated saves.
INSERT OR IGNORE INTO Pitcher_Roles (player_id, role)
    SELECT player_id, 1 FROM Player_Ratings WHERE is_pitcher = 1;

-- ----------------------------------------------------------------------------
-- Pitch_Arsenals — schema v4. Per-pitch-type ratings (micro doc §13): three
-- fixed types per pitcher, matching the C# PitchType enum by name. velocity
-- and movement are 0–100 (50 = league average, same scale as Player_Ratings);
-- usage_weight is the selection share of the pitcher's mix — the query layer
-- keeps the three weights summing to 100 per pitcher. The composite PK gives
-- the mandated player_id hot-path index via its leftmost prefix.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Pitch_Arsenals (
    player_id    TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    pitch_type   TEXT    NOT NULL CHECK (pitch_type IN ('Fastball', 'Breaking', 'Offspeed')),
    velocity     INTEGER NOT NULL DEFAULT 50 CHECK (velocity     BETWEEN 0 AND 100),
    movement     INTEGER NOT NULL DEFAULT 50 CHECK (movement     BETWEEN 0 AND 100),
    usage_weight INTEGER NOT NULL DEFAULT 0  CHECK (usage_weight BETWEEN 0 AND 100),
    PRIMARY KEY (player_id, pitch_type)
) STRICT;

-- v3→v4 backfill: migrated pitchers get a conservative league-shaped arsenal
-- derived from pit_stuff (fastball rides the raw stuff, breaking ball carries
-- the movement, offspeed sits behind both) with a standard 60/25/15 mix.
-- Fresh worlds never see these rows — LeagueGenerator writes varied arsenals
-- at creation and INSERT OR IGNORE makes the backfill a no-op for them.
INSERT OR IGNORE INTO Pitch_Arsenals (player_id, pitch_type, velocity, movement, usage_weight)
    SELECT player_id, 'Fastball', pit_stuff, 40, 60
    FROM Player_Ratings WHERE is_pitcher = 1;
INSERT OR IGNORE INTO Pitch_Arsenals (player_id, pitch_type, velocity, movement, usage_weight)
    SELECT player_id, 'Breaking', MAX(0, pit_stuff - 20), pit_stuff, 25
    FROM Player_Ratings WHERE is_pitcher = 1;
INSERT OR IGNORE INTO Pitch_Arsenals (player_id, pitch_type, velocity, movement, usage_weight)
    SELECT player_id, 'Offspeed', MAX(0, pit_stuff - 25), MIN(100, pit_stuff + 5), 15
    FROM Player_Ratings WHERE is_pitcher = 1;

-- ----------------------------------------------------------------------------
-- Life_Needs — schema v5. Persists the five NeedsEngine/NeedsState fields
-- LifeSimManager tracks in-memory (docs/design/life_sim_needs_decay.md §11),
-- one row per tracked NPC. player_id doubles as both PK and the mandated
-- hot-path index (single-row upserts/lookups by player_id, same pattern as
-- Player_Ratings/Pitcher_Roles). No backfill: unlike Player_Ratings, nothing
-- reads these values before LifeSimManager itself produces and writes them,
-- so a migrated v4 save simply has no row per player until the first day-tick
-- persist or clean quit — GameManager falls back to NeedsState.FullySatisfied()
-- for any player_id absent here, matching the pre-persistence default exactly.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Life_Needs (
    player_id TEXT PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    hunger    REAL NOT NULL DEFAULT 100 CHECK (hunger  BETWEEN 0 AND 100),
    sleep     REAL NOT NULL DEFAULT 100 CHECK (sleep   BETWEEN 0 AND 100),
    hygiene   REAL NOT NULL DEFAULT 100 CHECK (hygiene BETWEEN 0 AND 100),
    social    REAL NOT NULL DEFAULT 100 CHECK (social  BETWEEN 0 AND 100),
    fitness   REAL NOT NULL DEFAULT 100 CHECK (fitness BETWEEN 0 AND 100)
) STRICT;

COMMIT;

-- Schema version 5 — adds Life_Needs (purely additive, no backfill needed;
-- see comment on the table above). Bump with every migration.
PRAGMA user_version = 5;
