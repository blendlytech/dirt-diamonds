# Database Execution Rules (SQLite)

These rules govern *how* code talks to the database. The table structures themselves live in `database_schema.md`; the DDL source of truth is `Assets/Data/Database/SchemaDefinitions.sql`.

## Access Discipline

- **All access goes through `DatabaseManager.cs`.** No other class opens a connection, holds a connection string, or constructs raw SQL. Simulation systems consume typed query classes (e.g., `PlayerQueries.cs`) and DTOs only.
- **Parameterized queries only.** String-interpolated SQL is a review-blocking defect, even for internally generated values.
- **No Blind Queries.** Before writing or modifying any query with a join or a new column reference, validate the live schema via the SQLite MCP server, or the `validate_sqlite_schema` skill (sqlite3 CLI) when the MCP is unavailable.

## Transactions & Performance

- Batch mutations (day/week advancement, league-wide stat writes) are wrapped in a single `BEGIN TRANSACTION` / `COMMIT`. One transaction per calendar tick, not one per row.
- Prepared statements are cached and reused across the session; command objects are pooled — no per-call allocation inside simulation loops (zero-GC mandate).
- Reads used by the Monte Carlo macro-sim should be bulk loads into structs/arrays up front, not row-at-a-time queries mid-simulation.
- Enable WAL journal mode on connection open; the Gritty Event dispatcher polls while the sim writes.

## Schema Changes

- Any schema change is made in `SchemaDefinitions.sql` first, then validated through the schema validation path before dependent C# is written.
- Never rename or drop a column that saved games reference without a migration step keyed on a `schema_version` pragma (`PRAGMA user_version`).
- Indexes are mandatory on foreign keys used by hot paths: `player_id` on both stats tables, `player_1_id`/`player_2_id` on Relationships, `player_id`+`flag_name` on Entity_Flags.

## Data Integrity

- Foreign keys are enforced (`PRAGMA foreign_keys = ON`) on every connection.
- The `.db` file is game state and is never committed to git; only `SchemaDefinitions.sql` is versioned.
- Writes originating from the Life sim and the Baseball sim must not share a transaction — they arrive independently via the event bus and commit separately.
