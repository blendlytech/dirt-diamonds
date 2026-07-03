# Progress & Next Steps

At the end of every coding session, before you clear the chat, instruct Fable 5 to append a summary of what was completed and what the immediate next steps are to this document. When you begin a new session, you can load this file to instantly orient the AI without needing to explain the whole project again.

---

## 2026-07-02 — Phase 0: Toolchain & Foundation ✅ COMPLETE

**Installed (winget, user scope):**

- .NET SDK **8.0.422** → `C:\Program Files\dotnet\` (machine PATH)
- Godot **4.7-stable Mono** → `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe` (on user PATH by directory, but **no `godot` alias** — winget lacked admin for symlinks; use the full path or `godot_console.exe` for headless)
- SQLite CLI **3.53.3** → `%LOCALAPPDATA%\Microsoft\WinGet\Packages\SQLite.SQLite_Microsoft.Winget.Source_8wekyb3d8bbwe\sqlite3.exe` (`sqlite3` resolves in fresh shells)

**Created:** git repo + Godot/C# `.gitignore`; `project.godot` (features `4.7`, `C#`, assembly `DirtAndDiamonds`); `DirtAndDiamonds.csproj` (`Godot.NET.Sdk/4.7.0`, net8.0, nullable, unsafe blocks for `Span<T>` work); `DirtAndDiamonds.sln`; populated `.claude/rules/database_rules.md` and `ui_conventions.md`.

**Repaired `.mcp.json`:** `sqlite` now uses npm package `mcp-sqlite` (drop-in, same db-path arg); dropped `git` (Python-only upstream; git CLI suffices), `csharp` (omnisharp-mcp broken; `dotnet build` covers it), and `terminal` (was a mislabeled duplicate of sequential-thinking). `godot` MCP got explicit `GODOT_PATH` env since there's no PATH alias. **Restart the session for the new sqlite MCP to connect.**

**Exit criteria verified:**

- `dotnet build` → succeeded, 0 warnings / 0 errors (Godot.NET.Sdk 4.7.0 restored from NuGet).
- Godot `--headless --import` → clean run, `.godot/` generated. Note: Godot's C# plugin needs `dotnet` on PATH; fresh shells have it (this session's stale shell needed `$env:Path` prepended manually).
- Schema validation path → `sqlite3` CLI proven against `dirt_and_diamonds.db`: WAL mode set, smoke table round-trip, `PRAGMA integrity_check` → `ok`. The db file now exists (gitignored) so the sqlite MCP has a target.
- Git history started (initial commit).

**Next steps (Phase 1 — Database Core):**

1. Write `Assets/Data/Database/SchemaDefinitions.sql` per `.claude/rules/database_schema.md` (Players, Batting_Stats, Pitching_Stats, Relationships, Entity_Flags, Game_Logs + hot-path indexes).
2. `DatabaseManager.cs`: connection lifecycle (WAL + foreign_keys ON), parameterized-only API, transaction batch API, pooled commands. Needs a SQLite ADO.NET provider — pick `Microsoft.Data.Sqlite` and add the PackageReference.
3. `PlayerQueries.cs` + DTOs (Sonnet 5 from Fable 5's spec).
4. Build the checking script behind the `validate_sqlite_schema` skill; exit criteria: 10k-player round-trip, FK integrity, benchmarked day-advance transaction.

---

## 2026-07-02 — Phase 1: Database Core ✅ COMPLETE

**Schema (`Assets/Data/Database/SchemaDefinitions.sql`):** all six tables (Players, Batting_Stats, Pitching_Stats, Relationships, Entity_Flags, Game_Logs) as **STRICT** tables with CHECK bounds, FK cascade rules, and the mandated hot-path indexes (UNIQUE `(player_id, season_year)` doubles as the stats player_id index; `idx_relationships_player_2` for reverse graph traversal; partial index on active flag names for the event-dispatcher poll). Idempotent (`IF NOT EXISTS`), `PRAGMA user_version = 1`. Validated DDL-first via sqlite3 CLI (No Blind Queries): apply, integrity_check, FK/CHECK/STRICT rejection tests, re-apply. Additions beyond the rules doc: `Players.funds` (gritty-event prerequisites poll it), `Entity_Flags.set_on_day` (cascades "seasons later"), `Pitching_Stats.outs_recorded` (exact IP source). `team_id` is unconstrained until a Teams table ships via migration.

**`DatabaseManager.cs`:** sole connection owner, engine-independent (no Godot types). WAL + foreign_keys verified on open (`Pooling=false` so the file handle releases on quit), `synchronous=NORMAL`, `busy_timeout=5000`. Session-pooled prepared commands via `GetPooledCommand(sql)`; execution wrappers enlist the active batch (Microsoft.Data.Sqlite requires `command.Transaction` set). Batch API: `BeginBatch/CommitBatch/RollbackBatch/RunInBatch` — nested batches throw by design (one transaction per calendar tick; Life/Baseball never share one). `InitializeSchema` is the only non-parameterized path and only accepts the checked-in DDL.

**`PlayerQueries.cs` + `PlayerDtos.cs`:** struct row DTOs (PlayerRow, BattingStatsRow, PitchingStatsRow, RelationshipRow, EntityFlagRow), allocation-free RelationshipType↔string map. All SQL as compile-time constants; commands acquired once in the ctor, parameter-typed, prepared. Bulk reads fill caller-provided lists. Relationship upsert canonicalizes pair ordering (`player_1_id < player_2_id`); relationship lookup uses UNION ALL of two indexed probes (OR would skip both indexes). Flag upsert via `ON CONFLICT (player_id, flag_name)`.

**Tooling:** `Tools/SchemaValidator` console project (added to sln; `Compile Remove="Tools/**"` keeps it out of the game assembly). It compiles the DB-layer sources directly — if anyone adds a Godot type to them, the tool stops building. Scratch mode = full exit-criteria run; `--live <db>` = read-only audit. Skill wired: `.claude/skills/validate_sqlite_schema/run_validation.ps1` runs both passes; SKILL.md documents usage. `Microsoft.Data.Sqlite 8.0.11` added to both csprojs.

**Exit criteria (all green, 36/36 scratch + 25/25 live):**

- 10k-player round-trip: insert 287 ms (single transaction), load 62 ms, full field fidelity including NULL team_id.
- FK integrity: orphan insert rejected (SQLITE_CONSTRAINT), `foreign_key_check` clean, delete cascades to stats+flags.
- Day-advance benchmark: 20,000 statements (funds update + flag upsert × 10k players) in **479 ms** one transaction (~41.7k stmts/sec).
- Live `dirt_and_diamonds.db` initialized to schema v1 and passes the read-only audit (WAL, FKs, STRICT, index coverage).

**Next steps (Phase 2 — Core Loop, Time & Event Bus):**

1. `GameManager.cs` (autoload): owns the `DatabaseManager` instance (construct with `user://` path via `ProjectSettings.GlobalizePath`, call `InitializeSchema` on boot), disposes on quit.
2. `TimeManager.cs`: calendar advancement — each tick wraps its writes in one `RunInBatch` (the benchmarked path).
3. `GlobalState.cs` + the **async event dispatcher bus** — the only legal Life↔Baseball channel. Fable 5 owns this exclusively.
4. Exit criteria: headless "advance 365 days" run, zero cross-references between `/Simulation/Life/` and `/Simulation/Baseball/`.

---

## 2026-07-03 — Phase 2: Core Loop, Time & Event Bus ✅ COMPLETE

**Schema v2:** additive `Game_State` KV table (`key TEXT PK`, `value ANY` — STRICT preserves native value types) for save metadata (current_day, start_season_year); `PRAGMA user_version = 2`. Validated CLI-first on scratch per No Blind Queries, then the live db migrated in place (idempotent DDL *is* the migration for additive changes); sqlite MCP connected this session and confirmed. SchemaValidator expectations updated — 38/38 scratch, 27/27 live audit.

**`EventBus.cs` (Assets/Core):** the only legal Life↔Baseball channel. Deferred dispatch — `Publish` enqueues, handlers run at the driver's `DispatchPending` (GameManager per frame / harness per tick), so subscribers always run *after* the publisher's batch commits and open their own transactions. Global FIFO across event types (typed `Queue<T>` channels + a channel-ref order queue → struct events never box); **0 B allocated over 10k warm publish+pump cycles** (harness-proven). Copy-on-write handler arrays; thread-safe publish (Phase 7's polling dispatcher will need it); handler cascades drain in the same pump, capped at 1M events; reentrant pump throws.

**Calendar core (engine-free):** `GlobalState` — in-memory mirror of the DB calendar; 1-based absolute day ordinal (same clock as `Entity_Flags.set_on_day`), fixed 365-day seasons, derived SeasonYear/DayOfSeason. `TimeManager` — `Initialize(startYear)` loads or seeds day 1; `AdvanceDay` = one batch transaction per tick, then `DayAdvancedEvent` (+ `SeasonRolledOverEvent` on boundary). `GameStateQueries` — pooled/prepared upsert+select; `@value` parameter deliberately untyped so the ANY column stores native types; a type-mismatched read throws (save corruption must not look like a missing key and trigger a reseed).

**`GameManager.cs` autoload** (registered in project.godot; the only Godot-facing file in Core): opens `user://dirt_and_diamonds.db` via `GlobalizePath`, reads DDL through Godot `FileAccess` into the new `DatabaseManager.ApplySchema(string)` overload (res:// is inside the .pck when exported — **Phase 9 note: add `*.sql` to export include filters**), pumps the bus in `_Process` (ProcessMode Always so menus/pauses never stall dispatch), disposes the DB on `_ExitTree`. Minimal `Assets/Main.tscn` added as main scene so the project boots headless.

**`Tools/CoreLoopHarness`** (in sln): compiles Data + Core sources directly, *excluding* GameManager — the build now breaks if an engine type leaks into the headless core. 22/22 checks: full bus contract (defer, cross-type FIFO, unsubscribe, same-pump cascade, cycle cap, reentrancy, zero-alloc), Game_State type fidelity, calendar seed, 365-day run, reopen persistence (Initialize loads, never reseeds), Life↔Baseball boundary scan.

**Exit criteria (all green):**

- Headless 365-day advance: **96 ms** (~3.8k days/sec) with one calendar batch + one subscriber batch per tick; lands on day 366 = season 2027 day 1, exactly one rollover event, per-event fields consistent, `current_day` persisted, no batch left open.
- Zero cross-references between `/Simulation/Life/` and `/Simulation/Baseball/` (harness scans both directions).
- In-engine boot: `--headless --quit` → `[GameManager] … schema v2, journal=wal, fk=on, day 1 (season 2026, day 1)`, clean dispose on quit.

**Next steps (Phase 3 — Baseball Macro-Sim / Monte Carlo → Milestone M1 "The League Lives"):**

1. Opus 4.8 design doc first: PA outcome probability model (batter vs pitcher vs fielding → 1B/2B/3B/HR/BB/SO/out percentages) calibrated to MLB norms, with PED-multiplier hooks.
2. A Teams table is now unavoidable (league structure, schedules) → schema v3 through the validation path before any C# references it.
3. `LeagueSimulator.cs` + `AtBatResolver.cs` (Fable 5): structs/`Span<T>`, rosters bulk-loaded up front via `PlayerQueries.LoadAll`, league days react to `DayAdvancedEvent` off the bus, results committed in the sim's own batch (never the tick's).
4. Sonnet 5: `StatsNormalizer.cs` (denormalized AVG/OBP/SLG/OPS after batch writes) + the headless harness behind `run_monte_carlo_batch`.
5. Exit: full season simulates headless; league slash line within MLB norms; flat GC profile.
