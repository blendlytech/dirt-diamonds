# Dirt & Diamonds — Phased Build Plan

Lead: Claude Fable 5 (orchestration + critical-path code). Delegates: Claude Opus 4.8 (statistical/mathematical design), Claude Sonnet 5 (high-volume, well-specified implementation). No phase begins until the previous phase's exit criteria pass.

## Model Delegation Doctrine

| Model | Owns | Examples |
| :---- | :---- | :---- |
| **Fable 5** (lead) | Cross-system integration, performance-critical code, everything that touches architectural boundaries, review of all delegated work | DatabaseManager + transactions, async event bus, zero-allocation Markov/Monte Carlo cores, EventDispatcher, final wiring |
| **Opus 4.8** | Mathematical & statistical design (design docs, not necessarily code) | PA outcome probability tables calibrated to MLB norms, 25×25 transition matrix math, PED multiplier calibration, non-linear need-decay curves, poker pot-odds AI |
| **Sonnet 5** | High-volume implementation from a precise spec; iteration & tuning loops | DTOs/query classes, StatsNormalizer, headless test harnesses for the project skills, gritty-event JSON content, UI scene wiring, populating rules docs, refactoring passes |

Rule of thumb: Fable 5 writes the spec and the skeleton; Sonnet 5 fills volume; Opus 4.8 owns any file where the math being *wrong* is worse than the code being *slow*. Fable 5 reviews every delegated diff before it merges.

## Phase 0 — Toolchain & Foundation (BLOCKING)

Nothing can compile until this is done. Owner: Fable 5 (system installs need user approval).

1. Install .NET 8 SDK and Godot 4.x **.NET edition** (winget available).
2. `git init` + Godot/C# `.gitignore` (repo is currently not version-controlled).
3. Create `project.godot` + `DirtAndDiamonds.csproj` targeting the existing `/Assets` tree.
4. **Repair `.mcp.json`** — three servers silently fail to connect:
   - `sqlite`: `@modelcontextprotocol/server-sqlite` is not an npm package (reference server was Python/uvx, now archived). Replace with a working SQLite MCP or fall back to `sqlite3` CLI checks via the `validate_sqlite_schema` skill.
   - `git`: same problem (`server-git` is Python/uvx). Replace or drop (git CLI suffices).
   - `csharp` (omnisharp-mcp): fails to load; drop it — `dotnet build` + IDE diagnostics cover it.
5. Populate the empty `database_rules.md` / `ui_conventions.md` rules files (Sonnet 5).

**Exit criteria:** `dotnet build` succeeds; Godot opens the project; git history started; schema validation path (MCP or CLI) proven working.

## Phase 1 — Database Core (single source of truth)

- `SchemaDefinitions.sql`: Players, Batting_Stats, Pitching_Stats, Relationships, Entity_Flags, Game_Logs + indexes on high-traffic tables.
- `DatabaseManager.cs`: connection lifecycle, parameterized queries only, `BEGIN TRANSACTION` batch API, pooled command objects.
- `PlayerQueries.cs` + DTOs (Sonnet 5, from Fable 5's spec).
- **Validate:** `validate_sqlite_schema` skill (build its checking script as part of this phase).

**Exit criteria:** schema round-trips 10k generated players; FK integrity checks pass; batch day-advance transaction benchmarked.

## Phase 2 — Core Loop, Time & Event Bus

- `GameManager.cs`, `TimeManager.cs` (calendar advancement in transactions), `GlobalState.cs`.
- The **async event dispatcher bus** — the only legal communication channel between Life and Baseball per CLAUDE.md. Getting this boundary right now prevents architectural rot later. Owner: Fable 5 exclusively.

**Exit criteria:** headless "advance 365 days" run completes with zero cross-references between `/Simulation/Life/` and `/Simulation/Baseball/`.

## Phase 3 — Baseball Macro-Sim (Monte Carlo) → Milestone M1 "The League Lives"

- Opus 4.8 designs the PA outcome probability model (batter vs. pitcher vs. fielding → 1B/2B/HR/SO/BB percentages) calibrated to real MLB averages.
- Fable 5 implements `LeagueSimulator.cs` + `AtBatResolver.cs`: structs, `Span<T>`, object pooling, zero GC pressure at thousands of entities/frame.
- Sonnet 5: `StatsNormalizer.cs` + the headless harness behind `run_monte_carlo_batch`.
- **Validate:** `run_monte_carlo_batch` — 10,000 at-bats must produce a realistic league slash line. Re-run after every AtBatResolver or PED-weight change (CLAUDE.md mandate).

**Exit criteria:** full season simulates headless; league-wide AVG/OBP/SLG within MLB norms; GC allocation profile flat.

## Phase 4 — Baseball Micro-Sim (Markov Chain) → Milestone M2 "You're in the Game"

- 25 base-out states, 25×25 transition matrix per event (Opus 4.8 math design; Fable 5 struct-based implementation).
- Player input blending (timing/location vs. DB attributes).
- PED modifiers: 1.5× power/stamina during matrix calc; post-game `health_ceiling` deduction + `detection_risk` increment.
- First thin UI slice: playable at-bat scene (verify nodes with `godot_scene_mapper` **before** writing UI logic — CLAUDE.md mandate).

**Exit criteria:** micro-sim outcome distribution converges with macro-sim for identical players; `run_monte_carlo_batch` passes.

## Phase 5 — Life Sim: Needs Engine & Utility AI → Milestone M3 "Life Happens"

- `NeedsEngine.cs`: five needs, non-linear decay accelerating near zero (Opus 4.8 curve design).
- `UtilityCalculator.cs`: Utility = Σ(Consideration × Weight); stress/emotion overlay that mutates weights and can force autonomous actions.
- Sonnet 5: harness behind `simulate_utility_decay` + tuning iterations.
- **Validate:** `simulate_utility_decay` — one simulated week must not starve an NPC during a 3-hour game.

**Exit criteria:** NPCs self-manage needs over a simulated month; stress override provably fires.

## Phase 6 — Relationships & Generational Legacy

- `RelationshipGraph.cs`: bidirectional affinity graph backed by the Relationships table; rivalry scores feed baseball probability modifiers (via the event bus, never direct reference).
- Heir mechanics: genetic stat blending, hidden `baseball_interest`, succession on retirement, game-over when lineage fails.

**Exit criteria:** simulated 3-generation run with succession handoffs.

## Phase 7 — Gritty Event Framework

- `EventDispatcher.cs` background polling of Entity_Flags/Players; `ConditionEvaluator.cs` for prerequisite booleans; JSON event schema (prerequisites, probability weight, payload, branching choices, hidden flag writes).
- Sonnet 5 authors event content at volume from Fable 5's schema.
- **Validate:** `check_event_graph_integrity` after every content batch — no orphaned flags, no dead-end branches, no unreachable events.

**Exit criteria:** cascading chain proven end-to-end (e.g., bribe → `compromised_syndicate` → syndicate event fires seasons later).

## Phase 8 — Economy & Hustles → Milestone M4 "Full Loop"

- Legal work (time-skip abstractions), Narcotics 3-tier state machine (Drop → Cut → Territory using the Relationship Graph), Fencing negotiation, Texas Hold'em (Opus 4.8 pot-odds/bluffing math; Sonnet 5 implementation under Fable 5 review).
- Each hustle is an isolated interactive node; `godot_scene_mapper` before any UI logic.

**Exit criteria:** full career loop playable: broke rookie → hustle income → stress consequences → gritty events → season play.

## Phase 9 — Steam & Publishing → Milestone M5 "Ship It"

- Facepunch.Steamworks **only** (Steamworks.NET forbidden): cloud saves for the SQLite DB, achievements hooked to the EventDispatcher, rich presence.
- `NativeLibrary.SetDllImportResolver` for steam_api64.dll / libsteam_api.so; `.csproj` native-library copy targets.
- **Validate:** `validate_steamworks_native`; Windows + Linux export builds.

## Cross-Cutting Discipline (every session)

- Append completed work + next steps to `docs/progress.md` before ending a session.
- Commit per completed sub-task; phases merge only with exit criteria green.
- UI is built as thin vertical slices inside each phase, not as a big-bang Phase — every milestone stays demoable.
- Schema changes go through the SQLite validation path first (No Blind Queries rule).
