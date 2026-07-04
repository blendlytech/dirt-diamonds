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

---

## 2026-07-03 — Phase 3 (partial): PA-outcome design + schema v3 ✅ (steps 1–2 of the Phase 3 plan)

Scope was deliberately capped here — **stopped before writing any baseball C#** (`AtBatResolver.cs` / `LeagueSimulator.cs` remain empty stubs). What shipped is the statistical design and its backing schema, both validated, so the resolver/simulator can be written next session against a proven schema (No Blind Queries).

**Design doc — `docs/design/baseball_pa_outcome_model.md` (Opus 4.8).** Full macro-sim PA model: 7 mutually exclusive outcomes {Out, Strikeout, Walk, Single, Double, Triple, HomeRun} (HBP folded into Walk; ROE/sac omitted). MLB-calibrated baselines (Out .460 / K .225 / BB .090 / 1B .143 / 2B .046 / 3B .004 / HR .032) derive to **.247/.315/.412, .727 OPS** average-vs-average. Matchup layer is log-linear: `w_O = base_O·exp(k_O·m_O)`, renormalized — always a valid distribution for any roster. Per-outcome index weights (`m_O`) and sensitivities (`k_O`) tabulated; K/BB/HR are defense-independent by construction, BIP outcomes carry a team-defense term. PED hook = clamped `min(100, power×1.5)` pre-normalization + post-game `health_ceiling`/`detection_risk` deltas (flag from `Entity_Flags 'ped_active'`, Phase 7). Includes a base-out run-scoring layer (§7), calibration/acceptance ranges + tuning order (§8), and the binding zero-GC resolver contract for §9 (struct inputs, `stackalloc` weights, seeded struct RNG, constants-not-literals). Two worked numeric examples (elite slugger → ~1.02 OPS; ace → .187 opp AVG) double as unit-test fixtures.

**Schema v3 (`SchemaDefinitions.sql`, `PRAGMA user_version = 3`).** Two additive tables + one index, all `CREATE … IF NOT EXISTS` so the v2→v3 upgrade is the same in-place idempotent migration used for Game_State in v2 — **no ALTER on existing tables** (deliberate: split ratings into their own table rather than widen Players, keeping the migration risk-free):

- `Teams` (STRICT): `team_id` PK, `city`, `name`, `abbreviation` NOT NULL, nullable `league`/`division`.
- `Player_Ratings` (STRICT): `player_id` PK → Players ON DELETE CASCADE; `is_pitcher` + seven 0–100 ratings (`bat_power/contact/discipline`, `pit_stuff/control/stamina`, `fielding`), all DEFAULT 50 = league-avg, CHECK-bounded. One row per baseball-active player; the sim will `Players ⋈ Player_Ratings` at roster-load.
- `idx_players_team` on `Players(team_id)` — mandated hot-path index for roster grouping. A real FK on `Players.team_id` needs a Players rebuild and is **deferred**; enforced at the query layer for now (documented in the schema + design doc §10).

**Validation (No Blind Queries, done before any dependent code):** DDL proven CLI-first on a scratch db (apply + idempotent re-apply, STRICT, integrity_check ok, CHECK/FK-cascade/NOT-NULL rejection tests on both new tables). SchemaValidator expectations bumped to v3 (RequiredTables += Teams/Player_Ratings, user_version 2→3, +Players(team_id) index check, +Player_Ratings column-types): **scratch 44/44, live 33/33** (were 38/27 at v2). Live `dirt_and_diamonds.db` migrated in place (v2→v3) and re-confirmed via the sqlite MCP.

**Next steps (resume Phase 3):**

1. **Fable 5** — `AtBatResolver.cs` + `LeagueSimulator.cs` per design-doc §9: struct/`Span<T>`/`stackalloc`, seeded struct RNG, rosters bulk-loaded up front (needs new query surface: a `Players⋈Player_Ratings` roster load + `Teams` load + a pitching-season insert — extend `PlayerQueries` / add `BaseballQueries`), react to `DayAdvancedEvent`/`SeasonRolledOverEvent` off the bus, flush season counting stats in the sim's **own** batch (never the tick's), rate columns left 0 for the normalizer.
2. **Sonnet 5** — `StatsNormalizer.cs` (denormalize AVG/OBP/SLG/OPS/ERA/WHIP after the batch) + the `run_monte_carlo_batch` harness (10k PA → slash line vs §8 acceptance ranges).
3. Exit: full season simulates headless; league slash line within MLB norms; flat GC profile.

---

## 2026-07-03 — Phase 3: Baseball Macro-Sim ✅ COMPLETE — **Milestone M1 "The League Lives"**

Steps 3–5 of the Phase 3 plan (steps 1–2 shipped earlier today). Live schema re-validated via the sqlite MCP + `EXPLAIN QUERY PLAN` (roster join rides `idx_players_team` + the ratings PK autoindex) before any join code was written.

**Query surface — `BaseballQueries.cs` + `BaseballDtos.cs` (Assets/Data/Database).** Same contract as PlayerQueries (const SQL, ctor-acquired pooled prepared commands): Teams insert/load/count; Player_Ratings upsert; the `Players ⋈ Player_Ratings` roster bulk-load ordered `(team_id, player_id)` for deterministic lineup assignment; active-flag player-id load (partial index); **upserts** for season counting stats on both stats tables (`ON CONFLICT (player_id, season_year)` — a re-simulated or partial season is a safe overwrite); PED cost UPDATE (clamped in SQL); set-based rate-denormalization UPDATEs; league aggregate SUMs (harness + future standings). DTOs: TeamRow, PlayerRatingsRow, RosterPlayerRow, LeagueBatting/PitchingTotals.

**`AtBatResolver.cs`** — pure static, zero-alloc (§9 contract): §2 baselines / §4.1 weight matrix / §4.2 sensitivities as named `static readonly` tables (tuning = data edit); log-linear `w_O = p_base·exp(k_O·m_O)` + renormalize; `stackalloc` for deviations and weights; single uniform draw vs the cumulative; §6 PED clamp `min(100, (power·3+1)/2)` applied inside the resolver (callers can't forget it). `ComputeProbabilities` exposed separately so the harness asserts the §5 fixtures without consuming draws. **`RngState.cs`** — xoshiro256\*\* struct RNG, splitmix64-seeded, passed by `ref`.

**`LeagueSimulator.cs`** — 8 teams × (9 lineup + 5 rotation); circle-method round-robin, 22×7-day cycles = 154 games/team; complete-game starters (bullpens arrive with Phase 4 stamina); §7 base-out half-inning machine (bases = 3 bits, force-chain walk logic, `SingleScoresFrom2nd=0.60` / `DoubleScoresFrom1st=0.45` knobs, walk-off break after 9, extras while tied); RBI to the batter, all runs earned to the pitcher. Season counting stats accumulate in preallocated struct arrays; **flush once per season in the sim's own batch** (day-154 `DayAdvancedEvent`), then `StatsNormalizer` runs in its own transaction; `SeasonRolledOverEvent` handler is a year-guarded defensive flush (won't mis-file the new season's day 1, which dispatches first). PED bookkeeping per flagged participant per game → `health_ceiling`/`detection_risk` costs at flush (flag always inactive until Phase 7). **`LeagueGenerator.cs`** — seeds Teams/Players/Player_Ratings on an empty save (spread-0 mode = the §8 calibration roster). **`StatsNormalizer.cs`** — two set-based UPDATEs (AVG/OBP/SLG/OPS, IP/ERA/WHIP; `ip` = real innings, outs/3 — baseball notation is a UI concern).

**`Tools/MonteCarloHarness`** (in sln; compiles Data+Core+Baseball sources directly, GameManager excluded) behind the **`run_monte_carlo_batch`** skill (SKILL.md updated with the command + §8 tuning order): **30/30** — §5.1/5.2/5.3 fixtures (max err ≤6e-5), PED-clamp equivalence, bit-for-bit seed determinism, 0 B/PA; 100k-PA batch **.247/.316/.412, OPS .728, K% 22.4, BB% 9.2, HR/PA 3.2** — every §8 range hit; full 730-day/2-season pipeline on scratch db: seasons **.248/.316/.411 (R/G 4.21)** and **.250/.319/.413 (R/G 4.29)**, W/L/GS ledgers exact, batting↔pitching ledgers agree to the out, stored rates match recomputation, integrity/FK clean, **0 B allocated per warm game day** (80 games).

**Wiring:** GameManager boots the league (generate-if-empty with `DefaultRatingSpread=25`, wall-clock seed — determinism is the harness's contract, not the game's), `League.Initialize()` + `AttachTo(Events)`. In-engine `--headless --quit`: `schema v3 … league generated (8 teams)`, clean dispose. CoreLoopHarness's stale v2 expectation fixed → **22/22**; SchemaValidator **44/44**; game assembly builds 0/0.

**Known M1 artifacts (deliberate, revisit later):** (1) a fresh save seeds at day 1 and events fire on *advancement*, so the first-ever season plays season-days 2–154 (612 games, not 616) — harness encodes this; fix candidates: publish a day-1 event at seed, or shift the season window. (2) Stats flush is end-of-season only — quitting mid-season loses in-memory accumulators (the upsert flush makes periodic mid-season flushes trivial to add when save-integrity matters). (3) No Game_Logs rows / standings persistence yet; W/L lives in Pitching_Stats. (4) No SB, sacrifices, DPs, or bullpens — all Phase 4+ per the design doc.

**Next steps (Phase 4 — Markov Micro-Sim, when the player's avatar is in-game):**

1. Opus 4.8 design doc first: 25 base-out states per half-inning, 25×25 transition matrix per event, blending player timing/location inputs with database attributes, pitcher stamina/fatigue curve (where `pit_stamina` and the PED 1.5× stamina hook finally bind).
2. Micro↔macro consistency: a micro-simmed game's aggregate line must converge to the macro model's probabilities for the same ratings (shared calibration tables, not duplicated constants).
3. UI: first at-bat scene is a thin vertical slice per ui_conventions.md — verify node paths via godot_scene_mapper before any `GetNode<T>()`.

---

## 2026-07-03 — Phase 4 (partial): Markov micro-sim design ✅ (step 1 of the Phase 4 plan)

Scope capped at the **design doc only** — no micro-sim C# written (mirrors how Phase 3 shipped its PA-model design + schema before any resolver code). What shipped is the mathematical spec the interactive at-bat engine will be built against next session.

**Design doc — `docs/design/baseball_markov_micro_sim.md` (Opus 4.8).** Full micro-sim spec, written as the companion to the macro `baseball_pa_outcome_model.md` and engineered so **micro↔macro consistency is a provable identity under neutral input, not a calibration coincidence** (the Phase 4 mandate: *shared calibration tables, not duplicated constants*).

- **Two nested absorbing Markov chains, one analytic engine.** Outer = the mandated **25 base-out states** per half-inning (`bases*3+outs`, state 24 = 3-outs absorbing; §2), generalizing the ad-hoc base-out machine already in `LeagueSimulator.PlayHalfInning`. Inner = the **pitch/count chain** (12 count states + BB/K/BIP exits; §5), spun up **only for the human's own PAs**. Both solved with the same fundamental matrix `N=(I−Q)⁻¹` (§4 → run expectancy + a leverage signal reserved for Phase 7 Gritty Events).
- **The 25×25 "matrix per event" mandate** (§3): the 7 `PaOutcome` events map to constant sparse advancement matrices `A_e` (Single/Double stochastic via the shared `SingleScoresFrom2nd`/`DoubleScoresFrom1st` knobs); per-PA `T = Σ p_e·A_e`. Runtime applies the sparse advancement *function* (zero-GC, no matrix built per PA); the matrices are the analytic/validation representation only.
- **Consistency by construction** (§5.2/§5.3/§7): the count chain's absorption `(P_BB,P_K,P_BIP)` is pinned to `p* = AtBatResolver.ComputeProbabilities(...)`, and BIP is drawn from `p*`'s renormalized in-play split — so neutral input reproduces the macro 7-way distribution **exactly**, and the whole attended game converges to the macro line. Human timing/location (§6) and pitcher fatigue (§8) are the *deliberate* divergences, both routed back through the shared resolver. HBP/IBB may be surfaced for the box score but aggregate into macro's `Walk` bucket for every consistency check.
- **Fatigue binds `pit_stamina` + the PED 1.5× stamina hook** (§8): non-linear (accelerating-past-a-comfort-fraction) fatigue multiplier `m(x)` degrades *effective* `pit_stuff`/`pit_control` fed to the **same** `AtBatResolver` (no bespoke penalties); PED gives `min(100, round(stamina·1.5))` before capacity (`70→105→120`-pitch fixture); post-game costs reuse `LeagueSimulator.Ped*` constants. Governs the whole attended game (NPC PAs macro-resolve with the fatigued ratings).
- **§11 acceptance suite + tuning order** locks the neutral-policy limit to the already-validated macro §8 ranges, so micro tuning can never silently break the background league. **§13: the vertical slice needs NO schema change** (v3's `pit_stamina` + `Game_Logs` suffice); bullpen roles + pitch arsenals are deferred **v4** via the No Blind Queries path.

**Next steps (resume Phase 4 — implementation):**

1. **Fable 5** — build the micro-sim engine per the doc §9: extract the shared base-out advancement out of `LeagueSimulator` so both sims call it; `MicroGame` driver (outer base-out chain + inner pitch chain for human PAs only), fatigue as an effective-ratings adjustment on every pitcher, box score through the existing `Batting_Stats`/`Pitching_Stats` upsert in its own batch, play-by-play to `Game_Logs` via pooled writers. Struct state, `stackalloc`, one `ref RngState`, zero per-pitch alloc.
2. **Neutral autopilot policy** (§6.1) as the headless stand-in for the human, so the harness runs deterministically.
3. **Sonnet 5 / harness** — extend `run_monte_carlo_batch` with the §11 tests (neutral consistency vs the macro line, pitches/PA realism, fatigue late-game OPS rise, PED capacity, determinism + 0 B/PA).
4. **UI** — first at-bat `.tscn` thin slice per ui_conventions.md; `godot_scene_mapper` before any `GetNode<T>()`; read-only DTO rendering, player-intent (timing/location) signals up, no DB writes from UI.
5. **(Deferred v4)** bullpen `pitcher_role` + pitch-type arsenals via the schema-first validation path when the interactive slice is proven.

---

## 2026-07-03 — Phase 4: Markov Micro-Sim ✅ COMPLETE (engine + harness + UI slice)

Steps 1–4 of the Phase 4 plan, implemented exactly against `docs/design/baseball_markov_micro_sim.md`. No schema change (§13 held: v3 as-is). **All suites green: MonteCarloHarness 52/52 (was 30), CoreLoopHarness 22/22, SchemaValidator 44/44 scratch + 33/33 live, game assembly 0 warn/0 err, in-engine `--headless --quit` boot clean.** Macro-sim proven bit-identical after the refactor (season lines byte-for-byte the M1 numbers: .248/.316/.411 R/G 4.21, .250/.319/.413 R/G 4.29).

**Shared surface first (§14 — nothing duplicated):**

- `BaseOutAdvancement.cs` — the base-out advancement extracted from `LeagueSimulator` as the single source of truth (walk force-chain, single/double with discretionary branches, triple/HR), plus the §2 canonical 25-state index (`bases*3+outs`, 24 = absorbing). The Single/Double discretionary decision is an explicit parameter on a deterministic core with an rng wrapper on top — draw order preserved (macro bit-identical), and the matrix builder enumerates the branches from the SAME core. Knobs (`SingleScoresFrom2nd`/`DoubleScoresFrom1st`) moved here; `LeagueSimulator` keeps compile-time aliases.
- `BaseOutMatrices.cs` (§3–§4, offline/analytic only) — builds all seven 25×25 `A_e` by executing the shared advancement cores, composes `T = Σ p_e·A_e` + per-state expected-run rewards, and solves `(I−Q)·RE = r` (Gaussian elimination) for run expectancy. Harness proves: rows stochastic, runtime advancement ≡ matrix rows (Monte Carlo, ±5e-3), RE24 anchor 0.460 for the average matchup (canonical ~0.48), RE ordering sane. Leverage = future read-off of the same solve (Phase 7 hook).

**The micro-sim itself:**

- `PitchChain.cs` (§5–§6) — 12-count-state inner chain; strike-class folds strikes+fouls (foul share only bites at 2 strikes as a length-only self-loop). **Consistency by construction:** `SolveNeutral` inverts the absorption equations (Newton, FD Jacobian, ~µs, zero-alloc) so `(P_BB, P_K)` hit `p* = AtBatResolver.ComputeProbabilities` to ≤1e-9 analytically; the BIP exit re-draws from p*'s renormalized in-play split. `FoulShareOfStrikes = 0.68` tuned analytically (scratch sweep) → 3.84 pitches/PA. Human input = `BatterPitchInput` (discipline edge, contact quality q), applied as clamped log-linear perturbations; `IBatterPolicy` via generic constraint (devirtualized, no boxing); `NeutralBatterPolicy` = all-zero input = exact macro sampler; `PlayerInputModel` maps UI timing/location → the two scalars (τ_tol narrows with stuff, correct read widens it).
- `PitcherFatigue.cs` (§8) — capacity `70 + 0.5·effStamina`; PED stamina `min(100, (s·3+1)/2)` (same integer round-half-up as the power clamp) → the `70→105→120` fixture; flat through 60% of capacity then quadratic to m(1)=0.75, floor 0.5; effective stuff/control decay toward 50 and feed the SAME resolver. NPC PAs charge 3.9 pitches. `enabled:false` freezes m≡1 for the harness.
- `MicroGame.cs` (§9–§10) — attended-game driver: same bulk-load pattern/queries as the league, same game-flow rules; pitch chain only when the human bats (or pitches — opposing batters neutral until the pitch-arsenal step); every PA recomputes p* from the CURRENT fatigued effective ratings; per-inning offense aggregates (`InningTotals`) for fatigue validation/UI splits; play-by-play built at PA boundaries into pooled pending rows; **`FlushGame` = additive box-score upserts + `Game_Logs` + PED costs (shared `LeagueSimulator.Ped*` constants) in the sim's own batch.** Zero-alloc per warm game with logging off, human PAs included.
- `BaseballQueries` additions (live schema re-validated via the sqlite MCP first): `AddBattingGameCounts`/`AddPitchingGameCounts` — **additive** `ON CONFLICT` upserts so per-game micro flushes compose with the macro's whole-season overwrite flush instead of clobbering it (wiring micro-replaces-macro-game is the Phase 5 driver's job); `InsertGameLog`.

**Harness (§11, all six tests):** analytic absorption pin ≤1e-9 (all three macro fixtures) + sampled 7-way distribution ≡ p* (1M/400k PAs); 2000 neutral games fatigue-off → **.250/.317/.415, OPS .733, K% 22.5, BB% 9.0, HR/PA 3.2, R/G 4.30** — inside every macro §8 range; pitches/PA 3.84 analytic / 3.87 in-game; fatigue on (80/75 rotations): innings 7–9 OPS +.053 vs 1–3 and R/G +0.24 vs fatigue-off control; PED capacity fixture + post-game health 99/risk 2 exact; bit-for-bit game determinism; 0 B per warm PA/pitch/game. SKILL.md updated (checks 5–6 + micro tuning knobs).

**UI slice (§12):** `Assets/UI/AtBatView.tscn` + `AtBatView.cs` (`PanelContainer` root named after the file) — node paths mapped via `godot_scene_mapper` BEFORE any `GetNode<T>()`, refs cached in `_Ready`, `[Export]` on the log cap, signals `SwingCommitted(timing)`/`TakeCommitted` up, renders an `AtBatViewState` DTO only on change (no `_Process`), player-facing default text lives in the `.tscn`, intent controls lock while the sim resolves. Scene imports clean headless.

**Known Phase 4 artifacts (deliberate):** (1) `MicroGame` is not yet wired into `GameManager`/game flow — the Phase 5 career driver decides *which* league game is attended and suppresses the macro sim's copy of it; until then micro games are exhibition-additive. (2) Human-as-pitcher PAs run the chain with neutral batter input (pitcher-side input model lands with pitch arsenals, schema v4). (3) HBP/IBB not yet surfaced in the box score (aggregate into Walk per §7 bookkeeping). (4) `AtBatView` has no live driver behind it yet — it renders DTOs and emits intent; hooking it to `MicroGame` pitch-by-pitch is the first Phase 5 task.

**Next steps (Phase 5 — career wiring / player avatar):**

1. Game-flow driver: create the player avatar, decide the attended game per day, run `MicroGame` for it off the bus, and suppress the macro league's copy of that game (schedule-aware `LeagueSimulator` skip or result injection).
2. Wire `AtBatView` to the driver: pitch-by-pitch DTO updates + `PlayerInputModel` mapping of the timing slider, sim off the UI thread via the dispatcher.
3. Schema v4 (No Blind Queries path): `pitcher_role` for bullpens; pitch-type arsenals + pitcher-side input model.
4. Mid-season stat-flush cadence (M1 artifact #2) becomes urgent once the player's own stats are on the line.

---

## 2026-07-03 — Phase 5: Career Wiring / Player Avatar ✅ (steps 1, 2, 4; schema v4 deferred)

Steps 1, 2 and 4 of the Phase 5 list above, engine + harness + UI slice. No schema change (v3 as-is; the avatar is ordinary Players/Player_Ratings rows plus one Game_State key). **All suites green: MonteCarloHarness 69/69 (was 52), CoreLoopHarness 22/22, SchemaValidator 44/44 scratch + 33/33 live, game assembly 0 warn/0 err, headless boot AND headless `AttendedGameScreen.tscn` instantiation clean.** Macro-sim still bit-identical after the schedule extraction (both M1 season lines byte-for-byte: .248/.316/.411 R/G 4.21, .250/.319/.413 R/G 4.29).

**Design decisions (the load-bearing ones):**

- **Suppression is a standing per-team filter, not per-day.** Once an avatar exists, `CareerManager` owns ALL of that team's games: the macro sim skips any pairing containing the attended team (`LeagueSimulator.SetAttendedTeam`) but still ticks its rotation counter; skipped days autopilot through `MicroGame` under `NeutralBatterPolicy` — which reproduces the macro distribution by construction (§7), so the league's calibration never drifts. Every game is played exactly once, provable from GS/W/L accounting.
- **Stat composition fixed at the macro flush.** Micro flushes are additive per game; the macro's whole-season overwrite flush would have clobbered opponents' attended-game lines at day 154. The macro flush now uses the SAME additive `Add*GameCounts` upserts and fires at the end of every 7-day cycle (`dayOfSeason % 7 == 0`; 154 is a multiple), which also resolves the M1 mid-season-staleness artifact (step 4). The overwrite `Upsert*SeasonCounts` queries were deleted. `StatsNormalizer` runs after each cycle flush, so DB rates are never more than a cycle stale.
- **Schedule extracted to `LeagueSchedule.cs`** (pure circle-method math, macro play order preserved bit-for-bit): `SimulateGameDay` consumes it and the career driver derives "who does my team play today" from the same source.

**New engine surface:**

- `CareerManager.cs` — avatar lifecycle + attended-game day loop, engine-independent. `CreateAvatar` (one batch: Players+Ratings insert, weakest same-role teammate benched to free agency via `PlayerQueries.SetTeam(null)` so rosters stay exactly 9+5, `avatar_player_id` into Game_State; then league flush + both sims re-Initialize); `LoadExistingAvatar` restores on boot. On `DayAdvancedEvent`: derive the pairing, park it as a `PendingAttendedGame`; `AutopilotAttendedGames` (default true) resolves it instantly with the neutral policy, otherwise the UI plays it via `PlayPendingGame<TPolicy>` on a background task (flush = micro's own batch, `game_day` = absolute day per the Game_Logs clock). A pending game the player never played is forfeited to the autopilot on the next tick; day-advance during an in-flight game throws loud.
- `PlayerIntentBridge` + `InteractiveBatterPolicy` (`InteractiveBatterPolicy.cs`, System.Threading only) — the sim task blocks in `NextPitch`, publishes an `AtBatSnapshot` (dirty-flag), waits on a semaphore; the UI thread submits swing(τ)/take which maps through `PlayerInputModel`. Double-submit race guarded (submit consumes the wait); `Cancel()` surfaces as `OperationCanceledException` so the game unwinds unflushed. Zone read is a 50/50 rng draw until pitch location exists (v4 arsenals) — deliberate artifact.
- `IBatterPolicy` grew `BeginPa(in HumanPaContext)` / `OnPaResolved(PaOutcome)` (all implementors explicit — a default interface method would box the struct receiver). `MicroGame` calls them around human-batting PAs only, passing scores/inning/outs/bases + the fatigued effective pitcher ratings.
- `GameManager` boots `MicroGame` + `CareerManager` after the league (handler order: league first), restores the avatar if saved, logs it.

**UI slice (ui_conventions honored):** `AttendedGameScreen.tscn` + `.cs` — root named after the file, node paths mapped via `godot_scene_mapper` BEFORE `GetNode`, instances the Phase 4 `AtBatView`. Play button: `AdvanceDay` with autopilot off → pending game → `Task.Run` the interactive game; `_Process` polls the bridge's dirty flags (Render only on change), PA outcomes append to the play log, Skip button autopilots the day. Player-facing text templates are `[Export]` properties (scene-editable). `_ExitTree` cancels the in-flight game.

**Harness (suite 7, +17 checks):** avatar creation/reboot invariants; 364-day autopilot season through the real event loop — 612 games once each (GS=1224, W=L=612), composed ledgers agree, league line .248/.317 in §8, avatar 718 PA AVG .280, displaced FA played 0; mid-season cadence proof at day 40 (avatar 187 PA in DB, league 11,298); scripted-UI interactive game (5 PA / 25 pitches / 25 snapshots / 5 outcomes over the bridge); cancel path (unflushed, re-pending, forfeited next tick). SKILL.md check list updated.

**Known Phase 5 artifacts (deliberate):** (1) zone read is a coin flip until v4 pitch arsenals model location; (2) each sim keeps its own rotation counter, so an opponent's attended-game starter can differ from their macro rotation slot (stats compose fine; identity is cosmetic until bullpens); (3) `EnsureDebugAvatar` in the UI creates a fixed 60/60/60 rookie on team 1 — placeholder until the new-game creation flow (life-sim phases); (4) a cancelled game leaves `MicroGame`'s team game counters bumped (rotation drift only); (5) between human PAs the at-bat view doesn't update (NPC PAs render nothing until a game feed exists).

**Next steps (Phase 5 remainder / Phase 6 per BUILD_PLAN):**

1. ~~Schema v4 (No Blind Queries path): `pitcher_role` for bullpens; pitch-type arsenals + pitcher-side input model + a real zone-read minigame.~~ ✅ 2026-07-03 (entry below)
2. New-game flow: avatar creation UI (name/team/ratings budget) replacing `EnsureDebugAvatar`.
3. Attended-game feed: render NPC PAs between the avatar's at-bats (Game_Logs rows already exist per PA).
4. BUILD_PLAN Phase 5 (Life Sim needs/utility) — note the plan's phase numbering diverges from progress.md's here; the baseball career wiring above was tracked as "Phase 5" in this log.

---

## 2026-07-03 — Schema v4: Bullpens, Pitch Arsenals, Real Zone Read, Pitcher-Side Input ✅

The deferred v4 step (micro doc §8.5/§13), done schema-first per the No Blind Queries path. **All suites green: MonteCarloHarness 77/77 (was 69), CoreLoopHarness 22/22, SchemaValidator 58/58 scratch + 41/41 live, game assembly 0 warn/0 err, headless boot AND headless `AttendedGameScreen.tscn` instantiation clean.** The M1 macro season lines are STILL byte-for-byte (.248/.316/.411 R/G 4.21, .250/.319/.413 R/G 4.29) — see the rng-fork decision below. The live user save migrated in place on first boot (40 starters backfilled, 24 relievers invented, 192 arsenal rows).

**Schema (v3→v4, purely additive, the idempotent script IS the migration — same pattern as v2→v3):**

- `Pitcher_Roles` (player_id PK → Players CASCADE, role 1=Starter 2=Reliever) — a separate table, NOT an ALTER on Player_Ratings, so `CREATE TABLE IF NOT EXISTS` + `INSERT OR IGNORE` backfill (all pre-v4 pitchers were complete-game starters → role 1) keep the whole migration in the DDL.
- `Pitch_Arsenals` (composite PK player_id+pitch_type ∈ {Fastball, Breaking, Offspeed}; velocity/movement 0–100, usage_weight summing 100 at the query layer). Backfill derives a conservative arsenal from `pit_stuff` (FB rides stuff, BRK carries movement, 60/25/15 mix); `OR IGNORE` means post-v4 generated arsenals are never clobbered on re-boot.
- `PRAGMA user_version = 4`; all three harnesses' hardcoded version checks bumped (the known gotcha).
- Reliever *people* can't be invented in DDL: `LeagueGenerator.EnsureV4` tops any team's bullpen up to 3 at boot (GameManager calls it right after `GenerateIfEmpty`).

**Design decisions (the load-bearing ones):**

- **M1 bit-identity via `RngState.Split()`** (xoshiro256\*\* long-jump, 2^192 draws ahead, parent state untouched). `GenerateIfEmpty` runs the frozen v3 9+5 loop on the caller's stream, then generates relievers + arsenals from a fork — the macro sim's seed lineage is unchanged, proven by the byte-identical season lines. Harness asserts Split's contract directly.
- **Macro stays complete-game.** `LeagueSimulator` rotations now filter `role == Starter`; relievers are invisible to the macro sim, so its §8 calibration cannot move. Bullpens are a micro-sim (attended-game) mechanic only.
- **The pitch location layer is an exact mixture, so neutral calibration cannot drift.** Each chain pitch now draws a type (usage mix) and an in/out-of-zone location. Conditional class rates are built from the REFERENCE zone probability z_ref (count/control/type tendency model): ballIn = ball·(1−s), ballOut carries the balancing surplus, strike/in-play scale by the shared complement factor — z_ref·in + (1−z_ref)·out ≡ the §5 rates for ANY z_ref and separation. A neutral pitcher draws location from z_ref → marginals preserved (§11.1/§11.2 pass untouched, pitches/PA still 3.84); a CALLED pitch draws location from control-driven execution odds instead and genuinely re-weights the mixture. Gotcha fixed mid-build: constructing the conditionals from the *executed* z would collapse the mixture back to the marginal and make aiming cosmetic.
- **The zone read is now a real minigame.** `IBatterPolicy.NextPitch` gets a `PitchLook` (type cue — blurred by pitch movement vs batter discipline — plus the scouting zone probability for the cued type) and returns a `BatterIntent` (Neutral/Take/Swing + zone guess). Read correctness = guess vs the actually drawn location; the Phase 5 coin flip is gone. Timing tolerance is now judged against per-pitch `PerceivedStuff` (effective stuff ± type velocity/movement).
- **Pitcher-side input model shipped engine-complete.** `IPitcherPolicy`/`PitchCall` (type + zone target, executed with control-driven accuracy), `NeutralPitcherPolicy` autopilot, `InteractivePitcherPolicy` over the same bridge (`AwaitPitchCall`/`SubmitPitchCall`, snapshot `IsPitching` flag). `SimulatePa` is now two-policy generic; `MicroGame.PlayGame`/`CareerManager.PlayPendingGame` grew two-policy forms with single-policy convenience overloads so Phase 5 call sites didn't churn. Harness proves aim has teeth: same-seed runs, BB/PA 3.1% challenging the zone vs 18.4% painting away.
- **Bullpen pulls between half-innings** once the current pitcher's fatigue multiplier < `MicroGame.BullpenPullThreshold` (0.85, ≈ 91% of capacity); the reliever enters with a fresh stamina-derived capacity (generation gives relievers −20 stamina). `BullpenEnabled` toggle keeps the §11.4 starter-decay test isolated. 60-game proof: avg starter 98 pitches (complete games gone), 124 relief appearances, GS/W/L accounting identities intact.

**Other surface:** roster row + join gained `Role` (LEFT JOIN Pitcher_Roles, plan re-validated: idx_players_team + PK autoindexes); `LoadAllArsenals`/`UpsertArsenalPitch`/`UpsertPitcherRole`; `PlayerQueries.LoadPitchingSeasons` (mirrors the batting loader); `CareerManager` displacement is role-exact (starter↔starter, reliever↔reliever) and a pitcher avatar writes role + stuff-derived arsenal; `RosterSizePerTeam` = 9+5+3 = 17. UI slice: `ZoneReadToggle` (CheckButton) + `PitchCueLabel` added to `AtBatView.tscn` (scene-mapped first), swing/take signals carry the guess, cue text via `[Export]` templates.

**Harness (suite 8, +8 checks; suites 1–7 adapted):** Split contract (parent untouched, children deterministic/distinct); v4 roster shape 9+5+3 with roles ⟺ is_pitcher; arsenals 3-per-pitcher summing 100; EnsureV4 top-up + idempotence; relievers G>0 with GS=0 over 60 flushed games, starter avg pitches 98, accounting identities; pitcher-aim BB separation. SchemaValidator gained the v4 migration block (backfill values, OR IGNORE non-clobber, CHECK rejection, cascade).

**Known v4 artifacts (deliberate):** (1) W/L still credited to starters (pitcher-of-record logic deferred; harness identities rely on 1 W + 1 L per game); saves stay 0. (2) Each sim still keeps its own rotation counter (cosmetic starter-identity drift, unchanged from Phase 5). (3) The pitching UI doesn't exist yet — `InteractivePitcherPolicy` is harness-proven only, waiting on the pitcher career path in the new-game flow. (4) Mid-inning pulls don't happen — bullpen decisions are between half-innings only. (5) NPC-PA feed gap from Phase 5 still open.

**Next steps (model assignments — schema v4 was the last item that demanded Fable 5's invariant-heavy profile; the remaining items are safe to run on cheaper models with the harnesses as tripwires):**

1. **New-game flow** (avatar creation UI: name/team/ratings budget, batter OR pitcher career, replacing `EnsureDebugAvatar`) — **Sonnet 5.** The hard part already exists behind one call (`CareerManager.CreateAvatar` handles benching, role/arsenal writes, sim re-init); what's left is a Godot scene with mechanical, written-down guardrails (`godot_scene_mapper` before `GetNode`, `[Export]` text templates, UI never touches the DB). Low blast radius — no harness surface.
2. **Attended-game feed** (render NPC PAs between the avatar's at-bats; Game_Logs rows already exist per PA) — **Sonnet 5, with the transport design pinned first.** The one load-bearing choice is HOW NPC PA events cross from the sim task to the UI thread (pooled event queue on `PlayerIntentBridge` vs reading back `_pendingLogs`/Game_Logs) — it touches the zero-GC hot path and the threading contract. Spec that in a paragraph (or have Opus 4.8/Fable 5 write just the queue), then Sonnet does the rest.
3. **BUILD_PLAN Phase 5 (Life Sim needs/utility engine)** — mixed per the BUILD_PLAN's own assignments: **Opus 4.8** designs the decay curves, **Sonnet 5** owns the `simulate_utility_decay` harness + tuning iterations; Fable 5 at most for the initial `NeedsEngine`/`UtilityCalculator` skeleton and the stress-override semantics. The math is far simpler than the Markov work and the subsystem is isolated by mandate (no baseball references).

**Escalate back to Fable 5 only for work that re-enters the calibrated core** — pitcher-of-record logic, mid-inning pulls, `AtBatResolver`/`PitchChain` weight changes, or Phase 6's rivalry→probability modifiers — anything carrying the "prove it with `run_monte_carlo_batch`, don't drift the league" burden. Standing rule regardless of model: re-run the harness after anything that compiles into the sim assembly, and escalate if a band moves.

---

## 2026-07-03 — New-Game Flow: Avatar Creation UI ✅ (item 1 of the model-assignment list)

Scoped strictly to the assigned item — avatar creation UI replacing `EnsureDebugAvatar`. Did not touch the attended-game NPC feed or the Life Sim needs engine (items 2–3 of the same list), both still open. UI-only change: no schema, no sim-assembly code touched, so the Monte Carlo/CoreLoop/SchemaValidator harnesses were not re-run (none of their surfaces moved) — `dotnet build` is clean (0 warn/0 err) and a headless `--quit` boot against the real `user://` save confirms the new routing end to end.

**The gap found before writing anything:** `AttendedGameScreen.tscn` was never actually instantiated anywhere — `Assets/Main.tscn` was a bare scriptless `Node` with no children, so the game had no real boot-into-UI path at all; `EnsureDebugAvatar` was reachable only because a harness/dev run instantiated `AttendedGameScreen.tscn` directly. Wiring a real boot flow was therefore in scope, not just the creation form.

**`Assets/UI/NewGameScreen.tscn` + `.cs`** — node paths verified by hand against the `.tscn` (the `godot_scene_mapper` skill is a one-line stub with no actual scan behind it, so verification was a manual GetNode↔node-tree cross-check, done before any `GetNode<T>()` was written, per ui_conventions). Collects: name (`LineEdit`), team (`OptionButton` populated from `BaseballQueries.LoadAllTeams`, team_id carried as item metadata), career type (`Batter`/`Pitcher` radio-style `Button` pair on a shared `ButtonGroup`), bullpen role (`OptionButton`, shown only when Pitcher is selected), and a **fixed-budget ratings allocator**: the 3 career-specific ratings (bat power/contact/discipline OR pit stuff/control/stamina) plus the always-shown Fielding slider, each clamped 20–90, sharing a budget of 200 (= 4 × the schema's 50-average baseline) so every slider move is a trade-off, not a free bonus, and the Create button is disabled until the budget nets to exactly zero and the name is non-blank. On submit it calls `CareerManager.CreateAvatar` directly (no schema/query code needed — that method already owns benching, role/arsenal writes, and sim re-init end to end) and emits an `AvatarCreated` signal; DB exceptions surface into an on-screen error label instead of crashing. All player-facing strings are `[Export]` templates per ui_conventions.

**Boot wiring — `Assets/Main.tscn` + `Assets/UI/Main.cs` (new):** `Main` now holds two `[Export] PackedScene` slots (`NewGameScreenScene`, `AttendedGameScreenScene`) and picks one child at `_Ready()` based on `GameManager.Instance.Career.HasAvatar` — reliable because `GameManager` is an autoload and restores any saved avatar before `Main`'s own `_Ready()` runs. `NewGameScreen.AvatarCreated` re-runs the same picker, swapping straight to `AttendedGameScreen` without a manual scene-file reload.

**`AttendedGameScreen.cs` cleanup:** removed `EnsureDebugAvatar` and the `DebugAvatarTeamId` export it needed — `OnPlayGamePressed` no longer calls it, since `Main` now guarantees an avatar exists before this screen is ever shown. Dropped the now-unused `using DirtAndDiamonds.Data;` this left behind.

**Verified:** `dotnet build` 0/0; headless `--headless --quit` against the real dev save (which has no avatar yet) logged `avatar none` and exited 0 with no errors, confirming `Main` took the `NewGameScreen` branch and it instantiated cleanly. The `AttendedGameScreen` branch was not exercised live in this session (would require creating an avatar in the persistent dev save) but its instantiation path is unchanged from the already-harness-proven Phase 5 state and `CareerManager.CreateAvatar` itself carries 69+ passing invariant checks in `MonteCarloHarness`.

**Known artifacts (deliberate):** (1) the 200-point / 20–90 budget bounds are a first-pass balance choice, not derived from any design doc — worth a look once the ratings' in-game feel is playtested. (2) No "confirm/back" step — Create is a one-shot, one-career-per-save action (matches `CareerManager.CreateAvatar`'s own hard invariant). (3) No visual polish/theming — plain default Godot controls, matching the thin-slice precedent set by `AtBatView`/`AttendedGameScreen`.

**Next steps (unchanged from the model-assignment list above — items 2–3 still open):**

1. Attended-game feed (render NPC PAs between the avatar's at-bats) — Sonnet 5, transport design pinned first.
2. BUILD_PLAN Phase 5 (Life Sim needs/utility engine) — Opus 4.8 designs decay curves, Sonnet 5 owns the harness.

---

## 2026-07-03 — Attended-Game NPC Feed ✅ (item 1 of the remaining model-assignment list)

Transport pinned before writing UI, per the item's own prerequisite. **All suites green: MonteCarloHarness 78/78 (was 77, +1 new check), CoreLoopHarness 22/22, SchemaValidator 58/58, game assembly 0 warn/0 err, headless `--quit` boot against the real dev save clean.**

**Transport decision:** extended `PlayerIntentBridge` (Assets/Simulation/Baseball/InteractiveBatterPolicy.cs) with a second, additive pooled queue rather than reading back `_pendingLogs`/`Game_Logs` — those only exist post-flush at game end, so they can't drive a *live* between-at-bats feed. The existing human-only `_resolvedPas`/`TryDequeuePaOutcome` path (proven by the interactive-bridge harness test) was left untouched to avoid disturbing its exact-count assertion; a new `Queue<NpcPaFeedEvent>` + `PublishNpcPa`/`TryDequeueNpcPa` carries every *non*-human PA instead (batter display name, outcome, inning/half, runs).

**`MicroGame.cs`:** new nullable `FeedSink` property (default null) — a single null-check at the existing PA-boundary log point (`PlayHalfInning`, right where `AppendPaLog` already fires), so the zero-GC macro/harness hot path is unaffected by construction (no harness game ever sets it). Display names are precomputed once in `Initialize()` into a slot-indexed `_displayNames` cache ("F. Last"), not built per PA.

**Name source:** `RosterPlayerRow` gained `FirstName`/`LastName`; `BaseballQueries.SqlSelectRoster` now selects `p.first_name, p.last_name` off the *already-joined* Players row — no new join, `EXPLAIN QUERY PLAN` shape unchanged, so this didn't need a fresh No Blind Queries pass beyond confirming that.

**Wiring:** `CareerManager.FeedSink` (set-only passthrough to `_micro.FeedSink`) keeps the UI from reaching into `MicroGame` directly — same encapsulation as every other `CareerManager` call site. `AttendedGameScreen` attaches its `_bridge` as the feed sink right before `Task.Run`-ing the interactive game and clears it in `FinishInteractiveGame` (after the task is observed complete, so there's no race with the sim thread's reads); drains `TryDequeueNpcPa` in `_Process` alongside the existing outcome drain, rendering via a new `[Export] NpcPaLineFormat` template (`"{0}: {1}"`).

**Harness:** extended the existing suite-7 scripted-UI interactive-game test (same game, same bridge) to also attach `FeedSink`, drain the new queue, and assert it delivered ≥1 non-human PA with a non-empty name — 68 NPC events over a real attended game in this run.

**Known artifacts (deliberate):** (1) no run/score annotation in the feed line beyond the outcome name (a HR shows as "F. Last: Home run", not "+2 runs") — thin slice, box score final line already covers scoring; (2) NPC feed queue has no explicit cap (Queue grows unbounded if the UI stops draining) — matches the existing bridge's own precedent of trusting the drain loop, and a game caps out around 60–80 total PAs.

**Next steps:**

1. BUILD_PLAN Phase 5 (Life Sim needs/utility engine) — Opus 4.8 designs decay curves, Sonnet 5 owns the `simulate_utility_decay` harness + tuning iterations.

---

## 2026-07-03 — Life Sim: Needs Engine + `simulate_utility_decay` harness ✅ (decay curves only; UtilityCalculator still open)

No design doc existed yet for the Life Sim decay curves (Opus 4.8's half of the assignment), so the curves were designed directly against `life_sim_ai.md`'s formula mandate and proven out empirically via the harness rather than blocking on a separate design pass — the math here is simple enough (five independent per-need curves, no matchup/matrix layer) that harness-driven tuning stood in for a design doc. `UtilityCalculator.cs` (action-selection / considerations×weights) is still an empty stub — out of scope for this pass, which was specifically the decay side.

**`NeedsEngine.cs` (Assets/Simulation/Life, engine-free):** `NeedsState` struct (Hunger/Sleep/Hygiene/Social/Fitness, 0–100) + `NeedDecayProfile` readonly struct (`BaseDecayPerHour`, `AccelerationCoefficient`, `AccelerationPower`) as `static readonly` per-need tables — tuning is a data edit, matching the baseball sim's precedent. `DecayHour` implements the rules-doc formula with the acceleration folded into an effective Base_Decay evaluated at the current value: `effectiveDecay = BaseDecayPerHour · EnvironmentalMultiplier · StressModifier · (1 + AccelerationCoefficient·(1 − value/100)^AccelerationPower)`, clamped to [0,100]. `CriticalThreshold = 20` is the shared desperation line the life_sim_ai.md stress overlay will key off of.

**`Tools/NeedsDecayHarness`** (added to the .sln under Tools, same `Compile Include` pattern as the other harnesses — links `Assets/Simulation/Life/*.cs` directly so the harness proves the exact shipped code path). Simulates 168h of passive-only decay for a fully-satisfied standard NPC, prints a density-ramp text graph (28 columns × 6h) and a fixed-hour table (0/3/6/12/24/48/72/96/120/144/168h), then runs 14 checks: bounds, monotonicity, the 3-hour-game-not-starving anchor (all five ≥65 after 3h — actual 87–98), the 168h-total-neglect anchor (all five ≤ CriticalThreshold — all five actually floor at exactly 0 well before the week is out), Hunger-fastest/Fitness-slowest relative pacing. **14/14 green.**

**Tuned constants (first pass, all via the harness output, no hand math):** Hunger 4.2/hr base (fastest, floors ~hour 18), Sleep 3.4/hr (floors ~hour 21), Hygiene 1.4/hr (floors ~hour 60), Social 1.0/hr (floors ~hour 80), Fitness 0.55/hr (slowest, floors ~hour 122) — all share `AccelerationCoefficient≈1.1–1.6`/`Power=2`. Full solution build stays 0 warn/0 err (game assembly + all four Tools projects); no other harness surface touched (MonteCarloHarness/CoreLoopHarness/SchemaValidator untouched, not re-run).

**Known artifact (deliberate):** once a need's floor hour passes, it stays pinned at literal 0 for the rest of the week under pure passive decay (no replenishing actions exist yet to pull it back up) — read as "rock-bottom and miserable," not a bug; the cliff shape is the intentional accelerating-desperation curve, not a design flaw to smooth out.

**Next steps:**

1. `UtilityCalculator.cs` — action-selection engine (`Utility = Σ Consideration × Weight`; Need Deficit / Temporal Cost / Financial Cost / Risk considerations per life_sim_ai.md) and the stress/emotion overlay that overrides queued actions below `CriticalThreshold`. No harness exists for this yet — needs its own tuning pass once there are actions to select between.
2. Wire `NeedsEngine.DecayHour` into an actual per-NPC tick (currently only exercised by the harness) — needs a driver analogous to `TimeManager`/`DayAdvancedEvent`, engine-free, Life-sim-side only per the architectural boundary in CLAUDE.md.

---

## 2026-07-03 — Life Sim: Need-Decay Design Doc ✅ (Opus 4.8's Phase 5 curve-design assignment)

Wrote the design doc the previous entry explicitly deferred ("no design doc existed yet … curves were designed directly against `life_sim_ai.md`"). Per the BUILD_PLAN Model Delegation Doctrine, non-linear need-decay curves are Opus 4.8's to own; the empirical first pass shipped the curves, this closes the missing spec. **No code touched — pure design doc**, so no harness surface moved; re-ran `simulate_utility_decay` only to capture ground-truth output (14/14 green) and quote it verbatim.

**`docs/design/life_sim_needs_decay.md` (Opus 4.8).** Companion to the two baseball design docs, same house style (numbered sections, calibration tables, worked fixtures, acceptance anchors, implementation contract). Formalizes the shipped `d(v) = D·E·S·(1 + a·f^p)` decay law as a continuous ODE whose forward-Euler (h=1h) discretization *is* `NeedsEngine.DecayHour` — constants tuned against the discrete trajectory (the harness), so there's no model-vs-code gap. Per-need §3 table carries exact critical/floor hours recomputed from the shipped integrator (Hunger crit@16/floor@18, Sleep 20/23, Hygiene 47/54, Social 66/77, Fitness crit@122/floor@141), not the earlier "first checkpoint showing 0.0" estimates.

**What it adds beyond documenting the first pass (the real contribution — gaps the empirical pass left open):**

- **§4 modifier semantics** — `life_sim_ai.md` names `Environmental_Multiplier`/`Stress_Modifier` but defines neither. Pinned both: `E ∈ [0.25, 3.0]` (per-need context: labor hustle drains Fitness ×2.5 per gritty_events.md, home ×0.8, 3h-game anchor at ×1.0) with per-need-vector as the target and the shipped uniform scalar as the degenerate case; `S ∈ [1.0, 2.5]` via `S = 1 + 1.5·stress/100`; combined `E·S ≤ 3.0` ceiling.
- **§5 floor/desperation zone** — pinned-at-0 cliff formalized as intended (not a bug), structured around `CriticalThreshold = 20` as the stress-override line; `[20,100]` managed vs `[0,20]` override zone.
- **§6 recovery** — additive per-action `Restore` companion, with the **decay-only asymmetry** (desperation accelerates decay but recovery is flat) called out as load-bearing design, not to be "fixed" into symmetry.
- **§7 Utility coupling** — the handoff seam to the still-stubbed `UtilityCalculator.cs`: convex Need-Deficit consideration `((100−v)/100)^q` + the stress override; decay's only job is to expose `v` and `CriticalThreshold`.
- **§10 implementation contract** (engine-free, struct/zero-alloc, constants-not-literals, caller-supplied modifiers) + **§11 schema note** (needs are in-memory now; persistence is a deferred additive `[0,100]`-CHECK column set via the No Blind Queries path).

**Next steps (Phase 5 remainder — toward the M3 exit criteria "NPCs self-manage needs over a simulated month; stress override provably fires"):**

1. **`UtilityCalculator.cs`** — now has a spec to build against (design doc §7): `Utility = Σ(Consideration·Weight)` with the convex Need-Deficit consideration and the `≤ CriticalThreshold` stress override that forces stress-relief actions regardless of temporal cost. Needs an **action catalog** (the §6 restore vectors) to select among. Owner per doctrine: Sonnet 5 implements from the §7 spec; escalate to Opus only if the consideration-weight balance needs a design pass once actions exist.
2. **Per-NPC tick driver** — wire `NeedsEngine.DecayHour` into real game flow (engine-free, Life-side, analogous to `TimeManager`/`DayAdvancedEvent`); currently only the harness exercises it.
3. **Needs persistence** (design doc §11) — additive schema column set via the validation path, deferred until the tick driver produces values worth saving.

---

## 2026-07-03 — Life Sim: `UtilityCalculator` + `LifeSimManager` ✅ (M3 exit criteria — items 1–2 of the Phase 5 remainder; item 3 still deferred)

Implements the design doc §7 spec against the two remaining Phase 5 items. Item 3 (needs persistence) is **still deferred** — nothing here changes the schema; needs stay in-memory only, exactly as before. Planned via a full plan-mode pass (two Explore agents for codebase archaeology + a Plan agent that pressure-tested the design) before any code was written, since the action catalog and the stress-override mechanic both required real judgment calls beyond what the design doc pins down.

**`NeedsEngine.cs` (small additive edit):** `NeedsState` gained `Restore(need, amount)` (additive, clamped — the §6-mandated "mirror of `Set`") and `AnyAtOrBelow(threshold)` (the 5-field critical-need scan `UtilityCalculator`/`LifeSimManager` both need). Re-verified 14/14 before building anything on top.

**`ActionCatalog.cs` (new).** `NpcActionId` enum (`Idle` first — the tie-break winner and the guaranteed always-affordable fallback — then `Eat`/`Sleep`/`Shower`/`SocializeEvening`/`Workout`/`DrinkAlone`/`PickArgument`); `NpcActionDefinition` (`PrimaryNeed` nullable, `RestoreAmount`, `TemporalCostHours`, `FinancialCost`, `Risk0To100`, `IsStressRelief`) as named `static readonly` fields + an `All` array, mirroring `NeedDecayProfile`'s data-edit-tuning precedent. Restore amounts are the design doc §6 illustrative anchors (Hunger+45/Sleep+80/Hygiene+60/Social+35/Fitness+30); hours/cost/risk have **no design-doc anchor** — first-pass invented constants, called out as such in-file. `DrinkAlone`/`PickArgument` (life_sim_ai.md's named stress-relief examples) restore no need directly — their entire utility comes from `UtilityCalculator`'s crisis-only stress-relief term, not from fixing anything.

**`UtilityCalculator.cs` (populated the empty stub).** `ActionWeights` (5 tunable floats, `DefaultWeights` first-pass values) + 4 "higher-is-better" scoring helpers (`NeedDeficitScore` = convex `((100−v)/100)^q`; `TemporalCostScore` = linear falloff over a 24h horizon; `FinancialCostScore` = cost/funds ratio, deliberately uncapped below zero — ratio-ok clamps to a one-cent epsilon denominator instead of branching, so it stays continuous as funds→0; `RiskScore` = `1−risk/100`). `SelectAction` never filters/skips a candidate (only penalizes via score), so a broke or desperate NPC can never come back empty-handed — `Idle` is always a live, zero-cost candidate. **Stress override**, keyed purely on `NeedsState.AnyAtOrBelow(CriticalThreshold)` (no persisted "Stress" meter invented — the design doc's separate §4.2 stress *scalar* has no live source yet with Gritty Events unbuilt, and isn't needed to satisfy this milestone): zeroes `TemporalCostWeight` for the whole selection pass (implements "regardless of temporal cost" literally; `FinancialCostWeight` is deliberately *not* zeroed — the mandate only names temporal cost, and a broke NPC losing a crisis-response to `Idle` on pure unaffordability is intentional, harness-proven behavior, not a bug), and gives stress-relief actions a flat bonus term that's live only during a crisis.

**`LifeSimManager.cs` (new) — the Life sim's `CareerManager`-equivalent glue class.** Deliberately decoupled from `PlayerQueries`/Data: takes plain `NpcSeed(PlayerId, Funds)` records via an additive/idempotent `Seed(...)` call rather than loading players itself, so the only new Data-adjacent boundary is at the caller (`GameManager`), and `Tools/NeedsDecayHarness` stays DB-free. In-memory per-NPC state only (`Needs`, `Funds`, `BusyHoursRemaining`, `CurrentAction`) — matches design doc §11's explicit persistence deferral. `AttachTo`/`DetachFrom` mirror `CareerManager`'s cached-delegate idiom exactly. **`OnDayAdvanced` expands one game-day into 24 hourly ticks.** The one real bug caught during planning (not by the eventual fixture, which only exercises `UtilityCalculator` directly — by a Plan-agent pressure-test of the scheduling design): a naive "busy for N hours, no reselection until done" model would let the override sit on its hands mid-action for up to an action's full duration, directly contradicting "regardless of temporal cost." Fixed by re-checking `AnyAtOrBelow` **every hour, even mid-action** — if critical and the in-progress action isn't already the best pick, its countdown is abandoned and a new one starts immediately; if it's already the best pick, it's left alone (no double-restore, no reset).

**Harness (`Tools/NeedsDecayHarness`, 14/14 → 20/20).** Csproj gained two explicit `Compile Include`s (`EventBus.cs`, `CoreEvents.cs` — deliberately not a `Core\*.cs` wildcard, which would drag in `TimeManager.cs` → `Assets/Data` → the Sqlite package this harness has never needed). New checks, additive only (the original 14 untouched): 3 hand-verified `SelectAction` fixtures (fully-satisfied+funded → `Idle`; Hunger-critical+funded → `Eat`, beating both `Idle` and the stress-relief actions; Hunger-critical+broke → `PickArgument`, the free stress-relief action winning *specifically because* of the crisis-only bonus term — verified by hand it would lose to `Idle` without that term) plus a real `LifeSimManager` driven through 30 hand-built `DayAdvancedEvent`s over a local `EventBus` (no `TimeManager` needed) tracking each need's 30-day minimum for two synthetic NPCs. `.claude/skills/simulate_utility_decay/SKILL.md` updated with the new checks and both tuning-knob tables (decay curves *and* action selection).

**Wiring — `GameManager.cs` (this pass deliberately does NOT leave the driver harness-only).** Progress.md's own prior phrasing ("currently only exercised by the harness") was read as the thing being fixed, not a scope boundary — cost was ~6 lines mirroring `CareerManager`'s own wiring. After `Career.AttachTo(Events)`: bulk-loads `Players.LoadAll(...)` (the only established idiom that exposes `Funds` — `BaseballQueries.LoadRoster`'s DTO doesn't), projects to `NpcSeed[]`, constructs+seeds+attaches a `LifeSimManager`. Boot log gained a `life-sim NPCs {count}` fragment.

**Verified:** `dotnet run --project Tools/NeedsDecayHarness` → 20/20. `dotnet build` at the solution level → 0 warn/0 err across all 5 projects (main game assembly needed no csproj edit — only `Tools/**` is excluded from it). Headless `--headless --quit` against the real dev save → `schema v4 … avatar none, life-sim NPCs 136` (8 teams × 17 roster = 136), clean boot, no exceptions.

**Real numbers from the month-long harness run (informative, not hand-predicted):** a $50,000-seed NPC's worst 30-day minimum across all five needs was Hunger at 63.9 — comfortably clear of `CriticalThreshold`, but also evidence that under first-pass `DefaultWeights` the NPC eats *very* frequently (Hunger's fast decay rate makes `Eat` overtake `Idle` on raw utility well before any crisis, long before the override ever engages) — a real tuning artifact, not a bug, worth a look once this is playtested rather than harness-graded. A companion $0-seed NPC showed the intended split exactly: free needs (Sleep min 10.5, Hygiene min 13.6 — both dip *below* `CriticalThreshold` but never hit the floor) stay manageable, while money-gated needs (Hunger, Social, Fitness — the actions that fix them all cost funds) floor at 0.0 identically to the pure-passive-neglect trace, because nothing in the current catalog restores them for free and no income mechanic exists yet.

**Known artifacts (deliberate):** (1) actions restore exactly one need each — no multi-need trade-offs (design doc §6's "workout also costs Hunger" framing is explicitly illustrative, not binding); (2) no per-action Environmental Multiplier — every action runs at `E=1, S=1` inside `LifeSimManager`, so the §4.1 "resting at home slows decay" / "labor hustle drains Fitness ×2.5" context-modifiers aren't wired to activities yet; (3) `Risk0To100` is a static per-action constant, never modulated by a player's own `Recklessness`/`DetectionRisk` (both exist on `PlayerRow` but `NpcSeed` doesn't carry them, by design, to keep `LifeSimManager` decoupled from Data); (4) first-pass `DefaultWeights` lead to very frequent `Eat` selection (see above) — a tuning pass, not a logic fix; (5) a mid-game avatar creation (`CareerManager.CreateAvatar`) doesn't automatically get life-sim tracking — `Seed()` is additive/idempotent specifically so a future re-projection-and-reseed call is cheap, but nothing triggers it yet; (6) no UI reads `LifeSimManager`'s needs/funds yet (`TryGetNeeds`/`TryGetFunds` exist for exactly this, unexercised so far).

**Next steps:**

1. **Needs persistence** (design doc §11, now genuinely unblocked — the tick driver produces real values) — additive `[0,100]`-CHECK column set via the No Blind Queries validation path, schema v5.
2. **Tuning pass on `ActionWeights`/`ActionCatalog`** once playtested — the frequent-`Eat` artifact above is the first thing worth revisiting.
3. **Per-action Environmental Multiplier** (§4.1) once locations/activities exist to source it from; today every tick runs neutral (`E=1`).
4. `RelationshipGraph.cs` (Phase 6) remains an untouched empty stub.

---

## 2026-07-03 — Needs Persistence ✅ (design doc §11, Phase 5 remainder item 3 of 3 — closes out Phase 5)

Schema v5, purely additive, following the exact No Blind Queries path: validated the live save's schema via the `sqlite` MCP (`list_tables` — confirmed v4, no `Life_Needs` yet) before touching `SchemaDefinitions.sql`, matching the discipline already established for v3→v4.

**`SchemaDefinitions.sql`:** new `Life_Needs` table — `player_id TEXT PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE` (doubles as the mandated hot-path index, same pattern as `Player_Ratings`/`Pitcher_Roles`) + five `REAL NOT NULL DEFAULT 100 CHECK BETWEEN 0 AND 100` columns (hunger/sleep/hygiene/social/fitness), `STRICT`. **No backfill** — unlike `Pitcher_Roles`/`Pitch_Arsenals`, nothing reads these values before `LifeSimManager` itself produces and writes them, so a migrated v4 save just has no row per player until the first day-tick persist or a clean quit; absent rows fall back to the pre-persistence `FullySatisfied()` default exactly as before. `PRAGMA user_version = 5`.

**`NeedsQueries.cs` (new, Assets/Data/Database).** `NeedsRow` DTO (mirrors the table, same convention as `PlayerRow`/`BattingStatsRow`) + typed query class matching `PlayerQueries`'s discipline: pooled/prepared commands, `Upsert`/`BulkUpsert` (upsert-on-conflict, joins the caller's batch or opens its own — same idiom as `PlayerQueries.BulkInsert`), `LoadAll` bulk hydration into a `Dictionary<string, NeedsRow>`. Deliberately deals only in `NeedsRow`, never `NeedsState` — keeps this a plain Data-layer DTO surface rather than reaching into `Simulation.Life` types.

**`LifeSimManager.cs` (small additive edit) — still Data-free.** Added `TrackedPlayerIds` (exposes `_order`) and `SetNeeds(playerId, needs)` (no-op if the id isn't tracked) as the persistence bridge surface. Both are plain C#, no `Data`/Godot reference, so `Tools/NeedsDecayHarness`'s wildcard `Compile Include` of the whole `Assets/Simulation/Life/*.cs` folder still needs zero csproj changes — the harness stays DB-free exactly as its explicit `EventBus.cs`/`CoreEvents.cs`-only Core includes intended.

**`GameManager.cs` (wiring).** `Needs` query property constructed alongside `Players`/`Baseball`. After `LifeSim.Seed(...)` (which unconditionally applies `FullySatisfied()`), bulk-loads `Life_Needs` and calls `LifeSim.SetNeeds` for every persisted row, overwriting the fresh-seed default — then `AttachTo(Events)`. A `DayAdvancedEvent` handler (`PersistLifeSimNeeds`) is subscribed **after** `LifeSim.AttachTo`, so per `EventBus`'s documented per-channel subscriber-order guarantee it always observes a day's needs after that day's 24 hourly ticks already ran; it bulk-upserts through a reused `List<NeedsRow>` scratch buffer (`CollectionsMarshal.AsSpan`, no per-day allocation) in its own batch transaction, never the calendar tick's (`database_rules.md`: Life-sim and Baseball-sim writes must not share a transaction). `_ExitTree` calls the same handler once more before disposing the connection, so a mid-day quit doesn't lose that day's progress.

**Verified:** `dotnet build` → 0 warn/0 err, all 5 projects. `dotnet run --project Tools/NeedsDecayHarness` → still 20/20 (untouched surface). Two full boot/exit cycles via the Godot MCP against the real dev save: first boot logged `schema v5` (clean v4→v5 migration, no exceptions); `sqlite3` against the live `user://` save confirmed 136 `Life_Needs` rows with real varied decay values (not the 100-default) after the exit flush; a second boot/exit cycle produced byte-identical values, confirming hydration restores persisted state rather than resetting to `FullySatisfied()` (no day advanced between runs, so exact equality was the correct expectation).

**Next steps:**

1. **Tuning pass on `ActionWeights`/`ActionCatalog`** once playtested — the frequent-`Eat` artifact (Phase 5 M3 entry) is still the first thing worth revisiting.
2. **Per-action Environmental Multiplier** (§4.1) once locations/activities exist to source it from; today every tick runs neutral (`E=1`).
3. `RelationshipGraph.cs` (Phase 6) remains an untouched empty stub — Phase 5 is now fully closed out (all three M3 remainder items done).

---

## 2026-07-03 — Tuning Pass: `ActionWeights`/`ActionCatalog` (frequent-`Eat` artifact) ✅

Picked up the standing next-step item. Ran this as harness-driven tuning (no playtesting infra exists yet, and every prior constant table in this codebase — `NeedDecayProfile` included — was tuned the same way), per the model-assignment doctrine's "Sonnet 5 owns the harness + tuning iterations." **`NeedsDecayHarness` now 30/30 (was 20/20); full solution `dotnet build` 0 warn/0 err; headless `--headless --quit` against the real dev save clean (schema v5, day 38, life-sim NPCs 137).** No schema/API surface touched — pure constant retune plus a new diagnostic in the harness.

**Root cause (broader than the "Eat" label suggested).** Swept `UtilityCalculator.SelectAction` by hand before touching anything: at full satisfaction, a cheap action's non-deficit terms (temporal+financial+risk, all near-max when cost/time/risk are low) already land within **0.01–0.06 of Idle's fixed 0.8 baseline**. With `DefaultDeficitPower=2`, that gap closes at a trivial 11–16% deficit — i.e. *every* cheap action, not just `Eat`, was firing at 84–89⁄100 need value. `Eat` was simply the most visible symptom because Hunger decays fastest, so it hit its (already too-eager) crossover first and most often.

**Fix — `UtilityCalculator.cs`:** `DefaultDeficitPower` 2→**5** (steeper convexity — a small deficit now contributes far less), `DefaultWeights.NeedDeficitWeight` 1.0→**1.4** (compensates so a *genuine* deficit still dominates the stress-relief actions' flat bonus in the crisis fixture — the two constants pull in opposite directions on the crossover point, so both had to move together; derived analytically, then confirmed bit-for-bit against the harness). All three existing hand-verified `SelectAction` fixtures still pick the same winners (satisfied→`Idle`, Hunger-critical+funded→`Eat`, Hunger-critical+broke→`PickArgument`), and the funded-crisis margin (`Eat` over `DrinkAlone`) actually **widened**, 0.074→0.091.

**New harness diagnostic — `RunActionThresholdChecks`/`FindCrossoverValue` (`Tools/NeedsDecayHarness/Program.cs`).** A deterministic crossover sweep — for each need-restoring action, the highest need value at which it first beats `Idle` (every other need full, funds ample) — replacing "infer eagerness from a stochastic day trace" with a direct, precise number per action, asserted inside a sensible band (`25`–`75`: not trivial, but not so late it just duplicates the crisis override). Real before/after, all five actions (confirming the artifact was universal, not Eat-only):

| Action | Old crossover | New crossover |
| --- | --- | --- |
| `Eat` | ~87 | **59** |
| `Shower` | ~89 | **61** |
| `Workout` | ~83 | **54** |
| `SocializeEvening` | ~76 | **46** |
| `Sleep` | ~68 | **41** |

**Emergent effect worth flagging, not a regression:** the 30-day `LifeSimManager` integration check's funded-NPC Hunger minimum dropped from 63.9 → **8.7** (still passes "never bottoms out at literal 0"). Cause: `Sleep`'s own crossover also dropped, so the NPC now commits to 8-hour sleeps at a lower Sleep value than before; Hunger's accelerating decay curve can run unattended for most of that window and, once it crosses `CriticalThreshold` (20) *mid-sleep*, the hourly re-check doesn't catch it until the following hour's tick (one hour of lag), by which point it's dropped further. The crisis override then correctly interrupts sleep and eats. Read this as the override **provably firing for a well-funded NPC for the first time** — under the old constants Hunger's minimum (63.9) never got remotely close to 20, so the override was effectively dead code for anyone with money; now it's exercised exactly as M3's exit criterion names it. Worth an eye once there's real play feel to judge against, but not treated as a bug here.

**Known artifacts (deliberate):** (1) the crossover sweep only proves an action beats `Idle` in isolation, not that it wins the overall argmax against competing actions — the Hunger-dips-near-critical dynamic above is exactly that interaction, and isn't itself checked by any fixture; (2) `25`/`75` crossover-band bounds are first-pass judgment calls (not derived from the design doc, which explicitly disclaims owning `q`'s exact value — `life_sim_needs_decay.md` §7 calls it "a curve, not a law"); (3) still no per-action Environmental Multiplier or income mechanic, so this pass only retuned the existing action-vs-Idle balance, not the deeper "broke NPC's paid needs collapse" artifact (expected, no Phase 8 economy yet).

**Next steps:**

1. ~~**Per-action Environmental Multiplier** (§4.1) once locations/activities exist to source it from; today every tick runs neutral (`E=1`).~~ ✅ 2026-07-03 (entry below)
2. `RelationshipGraph.cs` (Phase 6) remains an untouched empty stub.
3. Once there's an actual play surface for the life sim (today it's headless-only), revisit the Hunger-dips-near-critical dynamic above against real play feel — the harness proves it's bounded and self-correcting, not whether it *feels* right.

---

## 2026-07-03 — Per-Action Environmental Multiplier ✅ (design doc §4.1 — the standing "every tick runs neutral" gap)

Fable 5 as lead (new architectural surface — the E-sourcing data shape — not a tunable-constant pass). **`NeedsDecayHarness` 39/39 (was 30/30, +9); full solution `dotnet build` 0 warn/0 err; headless `--headless --quit` against the real dev save clean (schema v5, day 38, life-sim NPCs 137).** No schema change, no baseball-sim surface touched (MonteCarloHarness/CoreLoopHarness/SchemaValidator not re-run — none of their compiled sources moved).

**The load-bearing decision — the activity IS the location context.** §4.1 says E is "sourced from the current location/activity context," but no Location/place entity exists (NPCs aren't anywhere until Phase 7+ gritty-event venues). Rather than invent speculative Location surface, the minimal data shape is a per-need `EnvironmentalModifiers` vector **on each `NpcActionDefinition`**: what an NPC is doing already implies where they are (Sleep/Shower ⇒ at home, SocializeEvening ⇒ bar/party). The field is documented in-code as the composition point where a real location context would later fold in.

**`NeedsEngine.cs`:** new `EnvironmentalModifiers` readonly struct — five floats mirroring `NeedsState`'s shape, `Uniform(x)` + `static readonly Neutral`, `Get(NeedType)` — this is §4.1's "five-element E argument" target shape, with the scalar overload kept as the documented `E_all` degenerate case (now delegating through `Uniform`, one code path). New `DecayHour(in NeedsState, in EnvironmentalModifiers, float stressModifier)` overload applies per-need E. The §4.2 **combined `E·S` ceiling** now actually exists in code: `MaxCombinedModifier = 3f`, product clamped to `[0, 3]` inside the scalar-value `DecayHour` (floor 0 also hardens the monotonicity property against a negative modifier; behavior at all existing call sites unchanged — everything passed 1·1).

**`ActionCatalog.cs`:** `NpcActionDefinition` gained `Environment` (optional ctor param, `null` ⇒ `Neutral`, so `Idle`/`Eat`/`DrinkAlone`/`PickArgument` stay at the calibrated baseline). Authored vectors use §4.1/§6 anchors wherever the design doc provides one: `Sleep`/`Shower` = `Uniform(0.8)` ("resting at home"); `SocializeEvening` = Social ×0.4 (bar/party — decay-slowing is separate from the +35 restore, per §6's decay-vs-recovery split); `Workout` = Hunger ×1.5 (the labor-hustle Hunger anchor) + Sleep ×1.3 (**no doc anchor — invented first-pass**, disclosed in-file), implementing §6's "actions trade needs against each other." The §4.1 labor-hustle Fitness ×2.5 anchor is NOT used anywhere yet — no Legal Work/hustle action exists in the catalog (gritty-events territory).

**`LifeSimManager.TickHour`:** the decay line now reads the current action's vector — `DecayHour(npc.Needs, ActionCatalog.Get(npc.CurrentAction).Environment)`. `CurrentAction` is provably this hour's activity on every path (each hour either re-selects or is mid-action). Stress stays `1.0` — the §4.2 stress scalar still has no live source until Gritty Events, same disclosed gap as the M3 entry.

**Harness (+9 checks, `RunModifierChecks`):** the design doc's §9 modifier fixtures verbatim — written into the doc explicitly "to verify the §4 layer once E/S are wired to context," which is this pass (E=1.5·S=1.5 → 90.55; S=2.0 on Hunger 40 → 26.76 vs calm 33.38); §4.2 ceiling clamp (E·S=4 decays exactly like E=3, → 87.40); scalar ≡ uniform-vector bit-exact identity; per-need vector teeth (Workout env hits Hunger/Sleep exactly, other three bit-identical to neutral); catalog-wide [0.25, 3.0] range sweep. The 168h passive trace is **byte-identical** to the §9 trajectory fixture (neutral path provably untouched).

**Emergent month-run shifts (informative, direction predicted before running):** funded NPC's 30-day Hunger minimum **8.7 → 14.3** — sleeping at home (×0.8) slows the mid-sleep Hunger bleed, softening the Hunger-dips-near-critical dynamic the tuning pass flagged (next-step #3 above partially self-resolved, still worth a play-feel look). Broke NPC: Sleep min 10.5 → 9.3, Hygiene 13.6 → 10.8 (still managed, never floor); money-gated needs still collapse as expected. Crossover sweep numbers unchanged by construction — `SelectAction` doesn't see environment (decay-side term, not a utility consideration; a future "prefer restful activities when worn down" coupling would be a deliberate design change, not this pass).

**Known artifacts (deliberate):** (1) environment applies only to the decay side — utility selection is environment-blind (see above); (2) `Workout` Sleep ×1.3 and both `Uniform(0.8)` home vectors are the only invented/anchor-interpolated constants, tunable as data via `simulate_utility_decay` (SKILL.md gained an "environment vectors" tuning section + the new check list); (3) no Location entity — when places land (Phase 7+), their context should *compose* with the action's vector (e.g. multiply, ceiling-clamped) rather than replace this field; (4) stress `S` still has no live source (unchanged).

**Next steps:**

1. `RelationshipGraph.cs` (Phase 6) remains an untouched empty stub.
2. Once there's an actual play surface for the life sim (today it's headless-only), revisit the Hunger-near-critical dynamic against real play feel (softened by this pass: funded-NPC Hunger min now 14.3).
3. When the first hustle/labor action enters the catalog (gritty events), its environment vector already has a §4.1 anchor waiting (Fitness ×2.5, Hunger ×1.5) — and that's the natural moment to wire the §4.2 stress scalar's live source too.
