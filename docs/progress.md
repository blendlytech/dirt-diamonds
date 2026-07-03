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
