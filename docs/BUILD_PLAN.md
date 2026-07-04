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
- **Survival economy (GAME_IDEA.md):** recurring living expenses — rent, food, gear — drained on a calendar cadence through the existing funds path; going broke feeds the needs engine (money-gated needs already collapse at $0, harness-proven). Includes persisting life-sim action spending to the DB (the disclosed Phase 7 gap).
- **Equipment quality (GAME_IDEA.md):** purchasable gear tiers as effective-ratings modifiers — the PED/fatigue/rivalry precedent, never touching `AtBatResolver` calibration tables; behind the `run_monte_carlo_batch` band check. Owner: Fable 5.
- **Arrest / injury / suspension (GAME_IDEA.md):** the risk triad on the illicit path — arrest (jail time-skip + flags), injury (availability + temporary rating hit keyed to `health_ceiling`), league suspension (`detection_risk` thresholds bench the avatar for N games via `CareerManager` availability). Consequence-vocabulary extension (Fable 5) + gritty-event content (Sonnet 5).

**Exit criteria:** full career loop playable: broke rookie → hustle income → stress consequences → gritty events → season play.

## Phase 9 — Career Progression Ladder & Player Development → Milestone M5 "The Long Road"

The GAME_IDEA premise the current build skips: you **start as an under-resourced high-school player** and climb **HS → College → Minors → MLB**. Today the sim seeds a single MLB-tier league and drops the avatar straight into it — there is no ladder, no development, and no player-driven daily grind toward the top.

- **League tiers** (schema-first, No Blind Queries): a `level`/`tier` dimension on Teams (HS, College, Minor A/AA/AAA, MLB) so the macro sim runs each tier in parallel and the avatar occupies exactly one at a time. Owner: **Fable 5** — re-enters `LeagueSimulator`/roster load and carries the `run_monte_carlo_batch` "no band moved" burden, since each tier calibrates to its own offensive baseline, not the MLB one.
- **Advancement gates**: performance + scouting drive promotion/demotion between tiers — amateur recruitment to college, draft/signing into the minors, call-ups to the MLB, and washing out downward. **Opus 4.8** designs the promotion model; **Fable 5** owns the tier-transfer handoff (the avatar changes team *and* tier mid-save — the succession-handoff's sibling, same roster-invariant discipline where a miscount silently drifts stat composition).
- **Player development / training**: attributes are static today (set at creation, blended for heirs, no growth curve — the disclosed §11.6 gap). Practice and coaching move ratings along age-appropriate development/decline curves, and this is where the daily "practice" block finally pays off. **Opus 4.8** designs the curves (peak-age growth, veteran decline); **Sonnet 5** owns the tuning harness.
- **The daily scheduling loop** (the Punch Club / New Star Soccer core the pitch names): the player allocates the rigid 24-hour clock across **sleep, school, practice, games, and work/hustle** blocks — each with needs, stat, and money consequences already modeled by the needs/utility/economy systems. The NPC `UtilityCalculator` auto-selects; the avatar's blocks are player-chosen. **School** is the early-tier (HS/College) obligation competing for those hours.

**Exit criteria:** a generated player climbs from HS to the MLB across simulated seasons via performance + development; each tier's league line sits in its own calibrated band; the avatar's daily schedule measurably moves both stats and needs.

## Phase 10 — Presentation Layer & Narrative Delivery → Milestone M6 "The Look"

Every UI shipped to date is a deliberate thin slice of plain Godot controls. GAME_IDEA's entire hook is 100% UI/text: a dark-mode split between a **"Baseball Dashboard"** and a **"Burner Phone / Bank."** This phase makes the game *look* like the pitch — it is the visual/asset phase the plan otherwise never schedules.

- **Two-panel shell**: the Baseball Dashboard (stats, scouting reports, calendar, progress bars) and the Burner Phone / Bank (contacts, illicit deals, bank balance, basic survival). Both stay read-only over sim state per `ui_conventions.md` — DTOs in, player-intent signals out, no scene reaching into another's tree. Owner: **Sonnet 5**.
- **Narrative delivery as messages**: fired Gritty Events and relationship beats surface as **iMessage-style texts** from girlfriends, coaches, and shady contacts, threaded per-contact in the Burner Phone — the event-choice UI's dressed-up form, consuming the existing `GrittyEventFiredEvent` / choice seam. **Sonnet 5**, over the already-pinned Fired/Resolved contract.
- **Scouting reports**: the Baseball Dashboard's read of the player's ratings and tier standing framed as a scout would (letter grades, projection), sourced from `Player_Ratings` + the Phase 9 tier/development state.
- **Visual identity**: dark-mode theme, satisfying progress bars, calendar interface — a shared Godot theme resource, not per-scene styling, retrofitting the existing thin-slice scenes.
- **Portrait pipeline**: pre-generated 2D contact portraits (Midjourney, gritty-polaroid / mugshot house style per GAME_IDEA) as an asset set keyed to `player_id`; the Steam-required **disclosure** of these pre-generated portraits carries into Phase 11's store copy.

**Exit criteria:** the full loop is playable through the two-panel shell in the shipping visual identity; narrative events arrive as threaded messages; scenes still communicate only via the event bus / signals.

## Phase 11 — Steam & Publishing → Milestone M7 "Ship It"

- Facepunch.Steamworks **only** (Steamworks.NET forbidden): cloud saves for the SQLite DB, achievements hooked to the EventDispatcher, rich presence.
- `NativeLibrary.SetDllImportResolver` for steam_api64.dll / libsteam_api.so; `.csproj` native-library copy targets.
- **Steam content compliance (GAME_IDEA.md):** disclose the pre-generated AI portraits in the store submission; complete the mature-content questionnaire (drug dealing, gambling references) for the appropriate rating.
- **Validate:** `validate_steamworks_native`; Windows + Linux export builds.

## Cross-Cutting Discipline (every session)

- Append completed work + next steps to `docs/progress.md` before ending a session.
- Commit per completed sub-task; phases merge only with exit criteria green.
- UI is built as thin vertical slices inside each phase, not as a big-bang Phase — every milestone stays demoable.
- Schema changes go through the SQLite validation path first (No Blind Queries rule).
