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
