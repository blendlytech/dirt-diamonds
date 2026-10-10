# **Database Schema Rules (SQLite)**

## Core Philosophy

The SQLite database is the single source of truth for the entire universe. Game state is saved by committing to these tables. DDL source of truth: `Assets/Data/Database/SchemaDefinitions.sql`. Current target: **schema v14** (Phase 13, `docs/design/league_restructure.md` §12). v14 is a pre-1.0 clean break; older saves are refused and backed up. **From 1.0 on, every schema change ships a migration.**

## Core Tables (shipped, carried forward)

**Players**: PK `player_id` (GUID). Fields: first_name, last_name, age, team_id, health_ceiling (0–100), recklessness (0–100), baseball_interest (heirs), detection_risk (PEDs). Plus `Player_Ratings`, `Player_Person` (HS person layer) and `Player_Absences` (availability).

**Batting_Stats & Pitching_Stats**: PK `stat_id`; FK player_id, season_year. Standard sabermetrics (PA, HR, AVG, OPS, IP, ERA, WHIP, SO).

**Relationships**: PK `rel_id`; FK player_1_id, player_2_id. affinity_score (−100 to 100), type_enum (Rival, Friend, Partner, Child).

**Entity_Flags**: narrative prerequisites for the Gritty Event System: player_id, flag_name (e.g. "compromised_syndicate"), is_active.

**Game_State** (KV) and **Game_Logs** (play-by-play and phone message read-model).

## League Tables (v14)

**Teams**: `tier` (0 HS, 1 College, 2 Pro), `conference_id`, `division_id` (NULL for HS), `home_park`. Ids: HS 101–112, College 201–224, Pro 301–332.

**Conferences / Divisions**: the league structure per tier.

**Schedule_Games**: every game of a season, generated at rollover: day_of_season, home/away, phase (regular / play-in / playoff), series link, status (scheduled / final / forfeit), score.

**Playoff_Series**: round, best_of, seeds, wins, winner.

**Team_Seasons**: materialized standings, national rank (College) and seed.

**Roster_Designations**: the Pro weekly active/inactive lock (`locked_through_day`).

**College_Enrollment**: team, entry season, class_year (1–4), declared season.

**Recruiting_Offers**: per player, team and season, with status.

**Draft_Picks**: season, overall pick, round, team, player.

## Execution Rules

Always use parameterized queries. When advancing days or weeks, wrap all stat updates in one `BEGIN TRANSACTION` block. Presentation-only data (announcer personas, cutscene definitions, voice manifests, the venue catalog, legends content, press templates) lives in content files under `Assets/`, **not** in the database.

## Planned Tables (v15, Phase L, after v14; `docs/design/temptation_and_lineage.md` §3)

**Player_Identity** (gender, ethnicity, listed_position; never read by the sim) · **Legend_Assignments** (legend → position, record stat/value) · **Biological_Parentage** (every lineage secret; readable only through `LineageQueries`) · **Indiscretions** (the hidden temptation risk ledger) · **Active_Effects** (temporary edges and penalties) · **Substance_Use** (dependence per substance; `docs/design/substances_and_addiction.md` §3). Shipped v13 is what the code uses today.
