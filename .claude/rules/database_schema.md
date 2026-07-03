# **Database Schema Rules (SQLite)**

## Core Philosophy

The SQLite database is the single source of truth for the entire universe. Game state is saved by committing to these tables.

## Table Structures

**Players**
Primary Key: player_id (GUID)

Fields: first_name, last_name, age, team_id, health_ceiling (0-100), recklessness (0-100), baseball_interest (for heirs), detection_risk (PEDs).

Batting_Stats & Pitching_Stats

Primary Key: stat_id

Foreign Key: player_id, season_year

Fields: Standard sabermetrics (PA, HR, AVG, OPS, IP, ERA, WHIP, SO).

Relationships

Primary Key: rel_id

Foreign Keys: player_1_id, player_2_id

Fields: affinity_score (-100 to 100), type_enum (Rival, Friend, Partner, Child).

Entity_Flags

Purpose: Tracks narrative prerequisites for the Gritty Event System.

Fields: player_id, flag_name (e.g., "compromised_syndicate"), is_active.

Execution Rules
Always use parameterized queries.

When advancing days/weeks, wrap all stat updates in a BEGIN TRANSACTION block to ensure fast batch writes.
