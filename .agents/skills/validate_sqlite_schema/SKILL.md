---
name: validate_sqlite_schema
description: Ensures the massive generational SQLite database remains structurally sound as Fable 5 writes new code.
---
# validate_sqlite_schema

**Purpose:** Ensures the massive generational SQLite database remains structurally sound as Fable 5 writes new code.

**Execution:**

```powershell
powershell -File .claude/skills/validate_sqlite_schema/run_validation.ps1
```

This runs two passes via `Tools/SchemaValidator` (a headless console project that compiles the game's real `DatabaseManager`/`PlayerQueries` sources):

1. **Scratch pass** — applies `Assets/Data/Database/SchemaDefinitions.sql` to a throwaway database and verifies: WAL + foreign_keys on open, `user_version`, idempotent re-apply, all six tables present and STRICT, mandated hot-path index coverage (leftmost-prefix), column data types, a 10,000-player round-trip with field fidelity, FK orphan rejection + cascade delete, `PRAGMA foreign_key_check` / `integrity_check`, and a benchmarked single-transaction day-advance batch.
2. **Live audit** — read-only structural checks against `dirt_and_diamonds.db` (or `-LiveDb <path>`). Never mutates rows.

Exit code 0 = all checks passed. Direct invocation is also fine:

```powershell
dotnet run --project Tools/SchemaValidator -c Release              # scratch
dotnet run --project Tools/SchemaValidator -c Release -- --live dirt_and_diamonds.db
```

Options: `--players <n>` (scratch cohort size), `--schema <path>` (override DDL location).

**When to run:** after any change to `SchemaDefinitions.sql`, `DatabaseManager.cs`, or a query class — and before writing C# that depends on a new column or join (No Blind Queries rule). If the sqlite MCP is unavailable, this skill is the schema validation path.
