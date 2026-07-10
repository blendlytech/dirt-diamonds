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
-- Relationship_History — schema v13. Append-only "ever partnered" ledger,
-- same canonical (player_1_id < player_2_id) ordering as Relationships. It
-- exists because Relationships.type_enum only holds the CURRENT kind: once a
-- Partner edge is reclassified to Friend/Rival (an organic NPC breakup via
-- NpcAutonomyService, or the avatar's end_partnership/rekindle_partnership
-- consequences), nothing else records that the pair ever dated. A row here
-- means exactly that, permanently — written once when a pair first becomes
-- Partner, never cleared. This is the graph-reaching substrate
-- hs_clubhouse_cancer's prerequisite needs to ask "does a teammate have an ex
-- who is my current partner" from the poll thread's read-only DB view,
-- instead of firing on flavor text alone (high_school_person_layer.md §9
-- disclosure (2)).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Relationship_History (
    player_1_id TEXT NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    player_2_id TEXT NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    CHECK (player_1_id <> player_2_id),
    PRIMARY KEY (player_1_id, player_2_id)
) STRICT;

CREATE INDEX IF NOT EXISTS idx_relationship_history_player_2 ON Relationship_History(player_2_id);

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

-- ----------------------------------------------------------------------------
-- Life_Stress — schema v6. Persists the §4.2 stress scalar LifeSimManager
-- tracks in-memory since Phase 7 (gritty_event_framework.md §9), one row per
-- tracked NPC. A separate table rather than an ALTER on Life_Needs so the
-- v5→v6 migration stays purely additive and this script remains the whole
-- migration — ALTER TABLE ADD COLUMN is not idempotent, and the separate-table
-- pattern is what Player_Ratings/Pitcher_Roles/Life_Needs already established.
-- No backfill, same reasoning as Life_Needs: LifeSimManager is the only
-- producer, and GameManager's hydration falls back to stress 0 (the in-memory
-- default) for any player_id absent here, matching pre-persistence behavior.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Life_Stress (
    player_id TEXT PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    stress    REAL NOT NULL DEFAULT 0 CHECK (stress BETWEEN 0 AND 100)
) STRICT;

-- ----------------------------------------------------------------------------
-- Team_Tiers — schema v7. The career-ladder dimension (Phase 9a): one row per
-- team naming which league tier it plays in. tier mirrors the C# LeagueTier
-- enum: 0 = HS, 1 = College, 2 = MinorA, 3 = MinorAA, 4 = MinorAAA, 5 = MLB.
-- A separate table rather than an ALTER on Teams so the v6→v7 migration stays
-- purely additive and this script remains the whole migration — ALTER TABLE
-- ADD COLUMN is not idempotent, the same reasoning as Pitcher_Roles/Life_Stress.
-- The PK doubles as the hot-path index for the tier-scoped roster join
-- (Players.team_id → Team_Tiers.team_id probe); the tier filter itself scans
-- at most 48 rows and needs no index of its own.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Team_Tiers (
    team_id INTEGER PRIMARY KEY REFERENCES Teams(team_id) ON DELETE CASCADE,
    tier    INTEGER NOT NULL CHECK (tier BETWEEN 0 AND 5)
) STRICT;

-- v6→v7 backfill: every pre-v7 team was the one MLB-calibrated league, so
-- existing teams migrate as tier 5 (MLB). INSERT OR IGNORE keeps this
-- idempotent on every boot; post-v7 code (LeagueGenerator/EnsureTierLeagues)
-- writes tier rows explicitly at team creation, so this only ever touches
-- migrated saves — a fresh world's Teams table is empty when this runs.
INSERT OR IGNORE INTO Team_Tiers (team_id, tier)
    SELECT team_id, 5 FROM Teams;

-- ----------------------------------------------------------------------------
-- Player_Absences — schema v8. The roster-availability store (Phase 8c): one
-- row per player naming why they are out of games and until when. A player is
-- ABSENT (replacement-level call-up shadows their slot, no stats accrue) while
-- current_day < until_day, and — injury only — plays RUSTY (effective ratings
-- down rating_penalty points) while until_day <= current_day < penalty_until_day.
-- reason mirrors the C# AbsenceReason enum: 1 = Injury, 2 = Suspension,
-- 3 = Arrest. One absence per player (PK); the query-layer upsert keeps
-- whichever absence ends LATER (a shorter overlapping absence is ignored
-- wholesale — disclosed simplification, absences don't stack). A separate
-- additive table, the Pitcher_Roles/Life_Stress/Team_Tiers pattern. No
-- backfill: nothing produced absences before v8. The hydration read is a full
-- scan by design — the table holds at most one row per ever-absent player.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Player_Absences (
    player_id         TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    reason            INTEGER NOT NULL CHECK (reason IN (1, 2, 3)),
    until_day         INTEGER NOT NULL CHECK (until_day >= 0),
    rating_penalty    INTEGER NOT NULL DEFAULT 0 CHECK (rating_penalty    BETWEEN 0 AND 100),
    penalty_until_day INTEGER NOT NULL DEFAULT 0 CHECK (penalty_until_day >= 0)
) STRICT;

-- ----------------------------------------------------------------------------
-- Player_Equipment — schema v9. Purchasable gear quality (Phase 8e): one row
-- per player naming their owned gear tier (1 = Quality, 2 = Premium,
-- 3 = Custom Pro). No row = quality 0 (standard issue) — quality 0 is never
-- stored (the AbsenceReason.None rule). Upgrade-only: the query-layer upsert's
-- conditional DO UPDATE makes a same-or-lower write a wholesale no-op, so the
-- EquipmentLedger's in-memory keep-higher merge applies the identical rule
-- without a read-back. A separate additive table, the Pitcher_Roles/
-- Life_Stress/Team_Tiers/Player_Absences pattern. No backfill: nothing
-- produced equipment before v9. The hydration read is a full scan by design —
-- the table holds at most one row per ever-equipped player.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Player_Equipment (
    player_id     TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    quality       INTEGER NOT NULL CHECK (quality BETWEEN 1 AND 3),
    purchased_day INTEGER NOT NULL CHECK (purchased_day >= 0)
) STRICT;

-- ----------------------------------------------------------------------------
-- Player_Potential — schema v10. The 9d development ceiling (Phase 9d,
-- docs/design/development_decline_curves.md §3): one row per baseball-active
-- player mirroring Player_Ratings' seven rating columns, holding the latent
-- per-rating ceiling the offseason development pass grows each rating toward.
-- A separate additive table (never an ALTER on Player_Ratings), the
-- Pitcher_Roles/Team_Tiers/Player_Absences/Player_Equipment pattern, so this
-- script remains the whole migration. The PK doubles as the hot-path index
-- for the once-per-offseason bulk load and the single-row probes.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Player_Potential (
    player_id      TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    bat_power      INTEGER NOT NULL DEFAULT 50 CHECK (bat_power      BETWEEN 0 AND 100),
    bat_contact    INTEGER NOT NULL DEFAULT 50 CHECK (bat_contact    BETWEEN 0 AND 100),
    bat_discipline INTEGER NOT NULL DEFAULT 50 CHECK (bat_discipline BETWEEN 0 AND 100),
    pit_stuff      INTEGER NOT NULL DEFAULT 50 CHECK (pit_stuff      BETWEEN 0 AND 100),
    pit_control    INTEGER NOT NULL DEFAULT 50 CHECK (pit_control    BETWEEN 0 AND 100),
    pit_stamina    INTEGER NOT NULL DEFAULT 50 CHECK (pit_stamina    BETWEEN 0 AND 100),
    fielding       INTEGER NOT NULL DEFAULT 50 CHECK (fielding       BETWEEN 0 AND 100)
) STRICT;

-- v9→v10 backfill: every pre-v10 player gets potential = current ratings —
-- zero headroom, so the founding generation only ever DECLINES (no growth
-- spurt on the first 9d boot); only post-v10 intake (which writes its own
-- potential row at creation) gets the full growth arc. INSERT OR IGNORE keeps
-- this idempotent on every boot AND guarantees it never clobbers a real
-- potential row in a developed post-v10 world (validated on a scratch db:
-- copies on first apply, wholesale no-op after).
INSERT OR IGNORE INTO Player_Potential
    (player_id, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding)
    SELECT player_id, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding
    FROM Player_Ratings;

-- ----------------------------------------------------------------------------
-- Player_Person — schema v11. The High School phase's person-stat layer
-- (docs/game-idas/HIGH_SCHOOL.md §3.2, curated core): academic, social and
-- moral scalars for every person in the universe, NPCs included. 50 = neutral
-- on every 0–100 scale (the Player_Ratings convention); gpa rides the real
-- 0.0–4.0 academic scale (2.5 = the same neutral midpoint in spirit).
-- Alignment (Good/Neutral/Troubled) is DERIVED from morality at thresholds,
-- never stored; rarer traits (humility, generosity, ...) live as Entity_Flags,
-- not columns. A separate additive table (never an ALTER on Players), the
-- Player_Ratings/Team_Tiers/Player_Potential pattern.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Player_Person (
    player_id     TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    gpa           REAL    NOT NULL DEFAULT 2.5 CHECK (gpa           BETWEEN 0 AND 4),
    intelligence  INTEGER NOT NULL DEFAULT 50  CHECK (intelligence  BETWEEN 0 AND 100),
    maturity      INTEGER NOT NULL DEFAULT 50  CHECK (maturity      BETWEEN 0 AND 100),
    happiness     INTEGER NOT NULL DEFAULT 50  CHECK (happiness     BETWEEN 0 AND 100),
    charisma      INTEGER NOT NULL DEFAULT 50  CHECK (charisma      BETWEEN 0 AND 100),
    confidence    INTEGER NOT NULL DEFAULT 50  CHECK (confidence    BETWEEN 0 AND 100),
    reputation    INTEGER NOT NULL DEFAULT 50  CHECK (reputation    BETWEEN 0 AND 100),
    social_status INTEGER NOT NULL DEFAULT 50  CHECK (social_status BETWEEN 0 AND 100),
    attractiveness INTEGER NOT NULL DEFAULT 50 CHECK (attractiveness BETWEEN 0 AND 100),
    teamwork      INTEGER NOT NULL DEFAULT 50  CHECK (teamwork      BETWEEN 0 AND 100),
    morality      INTEGER NOT NULL DEFAULT 50  CHECK (morality      BETWEEN 0 AND 100),
    discipline    INTEGER NOT NULL DEFAULT 50  CHECK (discipline    BETWEEN 0 AND 100),
    work_ethic    INTEGER NOT NULL DEFAULT 50  CHECK (work_ethic    BETWEEN 0 AND 100)
) STRICT;

-- v10→v11 backfill AND standing heal: every person gets a neutral row.
-- Unlike the Life_Needs no-backfill pattern, person stats are READ by systems
-- that never write them (event prerequisites, dating, the sim's HS-6 modifier
-- layer), so absent-row fallbacks would have to be duplicated in every reader.
-- INSERT OR IGNORE over Players is idempotent, never clobbers a developed row,
-- and also heals any player generated mid-session by a path that predates the
-- person layer (post-v11 intake writes its own row at creation).
INSERT OR IGNORE INTO Player_Person (player_id)
    SELECT player_id FROM Players;

-- ----------------------------------------------------------------------------
-- Family_Background — schema v11. The backstory roll (HIGH_SCHOOL.md §3.1):
-- one row per person whose family was ever generated — the avatar (HS-2's
-- BackstoryGenerator) and, later, heirs (whose family is the avatar's own).
-- wealth_tier 0 = destitute … 4 = wealthy; it gates starting funds, phone
-- tier, transport, home_wifi and parental auto-purchases. parent ids point at
-- unrostered NPC Players rows (the heir precedent inverted — Child edges run
-- parent→child, and EvaluateSuccession's younger-endpoint filter already
-- excludes parents). SET NULL, not CASCADE: losing a parent row must not
-- erase the family's economic reality. No row = pre-v11 person (no family
-- features) — the Life_Needs absent-row fallback pattern.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Family_Background (
    player_id        TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    wealth_tier      INTEGER NOT NULL CHECK (wealth_tier BETWEEN 0 AND 4),
    household_income REAL    NOT NULL DEFAULT 0  CHECK (household_income >= 0),
    parent1_id       TEXT    REFERENCES Players(player_id) ON DELETE SET NULL,
    parent2_id       TEXT    REFERENCES Players(player_id) ON DELETE SET NULL,
    home_wifi        INTEGER NOT NULL DEFAULT 0  CHECK (home_wifi IN (0, 1)),
    allowance_weekly REAL    NOT NULL DEFAULT 0  CHECK (allowance_weekly >= 0),
    strictness       INTEGER NOT NULL DEFAULT 50 CHECK (strictness BETWEEN 0 AND 100)
) STRICT;

-- ----------------------------------------------------------------------------
-- Phone_State — schema v11. The smartphone economy (HIGH_SCHOOL.md §4.2):
-- avatar-only rows naming the hardware tier (1 = burner … 3 = flagship), the
-- plan (0 = prepaid pay-per-minute, 1 = basic bundle, 2 = unlimited) and the
-- prepaid minute balance. No row = the pre-v11 phone — every feature
-- unlocked, no minutes accounting — so migrated saves keep the phone they
-- already had (the Life_Needs absent-row fallback pattern; HS-2 writes a real
-- row at avatar creation and the fallback goes dormant for new careers).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Phone_State (
    player_id         TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    tier              INTEGER NOT NULL DEFAULT 1 CHECK (tier BETWEEN 1 AND 3),
    plan              INTEGER NOT NULL DEFAULT 0 CHECK (plan BETWEEN 0 AND 2),
    minutes_remaining INTEGER NOT NULL DEFAULT 0 CHECK (minutes_remaining >= 0),
    purchased_day     INTEGER NOT NULL DEFAULT 0 CHECK (purchased_day >= 0)
) STRICT;

-- ----------------------------------------------------------------------------
-- Player_Items — schema v11. Marketplace ownership (HIGH_SCHOOL.md §4.6):
-- one row per (player, catalog item). item_id names an entry in the item
-- catalog JSON (Assets/Data/Items/items.json, HS-3) — validated loudly at
-- catalog load, not an FK, because the catalog is content, not schema.
-- category mirrors the C# ItemCategory enum: 1 = Transport, 2 = Clothing,
-- 3 = Jewelry, 4 = Food, 5 = Gear — denormalized here so hot questions like
-- "does this player own transport" stay SQL-answerable without the catalog.
-- Passive stat bonuses from owned items are COMPUTED at read time, never
-- written back, so losing an item reverts cleanly. The composite PK's
-- leftmost prefix is the mandated player_id hot-path index. No backfill:
-- nothing produced items before v11 (Player_Equipment keeps baseball gear).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Player_Items (
    player_id    TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    item_id      TEXT    NOT NULL,
    category     INTEGER NOT NULL CHECK (category BETWEEN 1 AND 5),
    acquired_day INTEGER NOT NULL CHECK (acquired_day >= 0),
    PRIMARY KEY (player_id, item_id)
) STRICT;

-- ----------------------------------------------------------------------------
-- Child_Development — schema v11. The nurture half of heir development
-- (HIGH_SCHOOL.md §4.7, full child-rearing): accumulating 0–100 axes written
-- by rearing events and the weekly family tick (HS-5). At heir maturity or
-- takeover, final Potential + baseball_interest blend the HeirGenetics nature
-- roll with these axes. 50/50/50 neutral start, neglect accrues from 0. No
-- row = pure-nature heir (the exact pre-v11 behavior), so existing heirs and
-- NPC children are untouched — the Life_Needs absent-row fallback pattern.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Child_Development (
    child_id      TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    care          INTEGER NOT NULL DEFAULT 50 CHECK (care     BETWEEN 0 AND 100),
    coaching      INTEGER NOT NULL DEFAULT 50 CHECK (coaching BETWEEN 0 AND 100),
    funding       INTEGER NOT NULL DEFAULT 50 CHECK (funding  BETWEEN 0 AND 100),
    neglect       INTEGER NOT NULL DEFAULT 0  CHECK (neglect  BETWEEN 0 AND 100),
    last_tick_day INTEGER NOT NULL DEFAULT 0  CHECK (last_tick_day >= 0)
) STRICT;

-- ----------------------------------------------------------------------------
-- Child_Rearing_Commitment — schema v12. The player-set half of the §7.1
-- weekly family tick: a standing weekly dollar commitment toward the
-- avatar's children (applied by ChildRearingService, deducted from the
-- avatar's own funds — the time half of the allocation rides DaySchedule's
-- FamilyHours block instead, since minutes aren't schema state). One row
-- per avatar, PK on player_id — not child_id, because the commitment is a
-- household decision applied uniformly to every child (Child_Development's
-- own ApplyChildDevelopment precedent: fan out to all children, no
-- per-child targeting). No row = 0 commitment, the pre-v12 default.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Child_Rearing_Commitment (
    player_id      TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    weekly_funding INTEGER NOT NULL DEFAULT 0 CHECK (weekly_funding BETWEEN 0 AND 300)
) STRICT;

COMMIT;

-- Schema version 13 — adds Relationship_History (purely additive; see its
-- own comment above). Bump with every migration.
PRAGMA user_version = 13;
