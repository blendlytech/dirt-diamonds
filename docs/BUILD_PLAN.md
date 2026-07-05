# Dirt & Diamonds — Phased Build Plan

Lead: Claude Fable 5 (orchestration + critical-path code). Delegates: Claude Opus 4.8 (statistical/mathematical design), Claude Sonnet 5 (high-volume, well-specified implementation). No phase begins until the previous phase's exit criteria pass.

## Model Delegation Doctrine

| Model | Owns | Examples |
| :---- | :---- | :---- |
| **Fable 5** (lead) | Cross-system integration, performance-critical code, everything that touches architectural boundaries, review of all delegated work | DatabaseManager + transactions, async event bus, zero-allocation Markov/Monte Carlo cores, EventDispatcher, final wiring |
| **Opus 4.8** | Mathematical & statistical design (design docs, not necessarily code) | PA outcome probability tables calibrated to MLB norms, 25×25 transition matrix math, PED multiplier calibration, non-linear need-decay curves, poker pot-odds AI |
| **Sonnet 5** | High-volume implementation from a precise spec; iteration & tuning loops | DTOs/query classes, StatsNormalizer, headless test harnesses for the project skills, gritty-event JSON content, UI scene wiring, populating rules docs, refactoring passes |

Rule of thumb: Fable 5 writes the spec and the skeleton; Sonnet 5 fills volume; Opus 4.8 owns any file where the math being *wrong* is worse than the code being *slow*. Fable 5 reviews every delegated diff before it merges.

## Phase 0 — Toolchain & Foundation (COMPLETE)

Nothing can compile until this is done. Owner: Fable 5 (system installs need user approval).

1. Install .NET 8 SDK and Godot 4.x **.NET edition** (winget available).
2. `git init` + Godot/C# `.gitignore` (repo is currently not version-controlled).
3. Create `project.godot` + `DirtAndDiamonds.csproj` targeting the existing `/Assets` tree.
4. **Repair `.mcp.json`** — three servers silently fail to connect.
5. Populate the empty `database_rules.md` / `ui_conventions.md` rules files (Sonnet 5).

**Exit criteria:** `dotnet build` succeeds; Godot opens the project; git history started; schema validation path (MCP or CLI) proven working.

## Phase 1 — Database Core (single source of truth) (COMPLETE)

- `SchemaDefinitions.sql`: Players, Batting_Stats, Pitching_Stats, Relationships, Entity_Flags, Game_Logs + indexes on high-traffic tables.
- `DatabaseManager.cs`: connection lifecycle, parameterized queries only, `BEGIN TRANSACTION` batch API, pooled command objects.
- `PlayerQueries.cs` + DTOs (Sonnet 5, from Fable 5's spec).
- **Validate:** `validate_sqlite_schema` skill (build its checking script as part of this phase).

**Exit criteria:** schema round-trips 10k generated players; FK integrity checks pass; batch day-advance transaction benchmarked.

## Phase 2 — Core Loop, Time & Event Bus (COMPLETE)

- `GameManager.cs`, `TimeManager.cs` (calendar advancement in transactions), `GlobalState.cs`.
- The **async event dispatcher bus** — the only legal communication channel between Life and Baseball per CLAUDE.md. Getting this boundary right now prevents architectural rot later. Owner: Fable 5 exclusively.

**Exit criteria:** headless "advance 365 days" run completes with zero cross-references between `/Simulation/Life/` and `/Simulation/Baseball/`.

## Phase 3 — Baseball Macro-Sim (Monte Carlo) → Milestone M1 "The League Lives" (COMPLETE)

- Opus 4.8 designs the PA outcome probability model (batter vs. pitcher vs. fielding → 1B/2B/HR/SO/BB percentages) calibrated to real MLB averages.
- Fable 5 implements `LeagueSimulator.cs` + `AtBatResolver.cs`: structs, `Span<T>`, object pooling, zero GC pressure at thousands of entities/frame.
- Sonnet 5: `StatsNormalizer.cs` + the headless harness behind `run_monte_carlo_batch`.
- **Validate:** `run_monte_carlo_batch` — 10,000 at-bats must produce a realistic league slash line. Re-run after every AtBatResolver or PED-weight change (CLAUDE.md mandate).

**Exit criteria:** full season simulates headless; league-wide AVG/OBP/SLG within MLB norms; GC allocation profile flat.

## Phase 4 — Baseball Micro-Sim (Markov Chain) → Milestone M2 "You're in the Game" (COMPLETE)

- 25 base-out states, 25×25 transition matrix per event (Opus 4.8 math design; Fable 5 struct-based implementation).
- Player input blending (timing/location vs. DB attributes).
- PED modifiers: 1.5× power/stamina during matrix calc; post-game `health_ceiling` deduction + `detection_risk` increment.
- First thin UI slice: playable at-bat scene (verify nodes with `godot_scene_mapper` **before** writing UI logic — CLAUDE.md mandate).

**Exit criteria:** micro-sim outcome distribution converges with macro-sim for identical players; `run_monte_carlo_batch` passes.

## Phase 5 — Life Sim: Needs Engine & Utility AI → Milestone M3 "Life Happens" (COMPLETE)

- `NeedsEngine.cs`: five needs, non-linear decay accelerating near zero (Opus 4.8 curve design).
- `UtilityCalculator.cs`: Utility = Σ(Consideration × Weight); stress/emotion overlay that mutates weights and can force autonomous actions.
- Sonnet 5: harness behind `simulate_utility_decay` + tuning iterations.
- **Validate:** `simulate_utility_decay` — one simulated week must not starve an NPC during a 3-hour game.

**Exit criteria:** NPCs self-manage needs over a simulated month; stress override provably fires.

## Phase 6 — Relationships & Generational Legacy (COMPLETE)

- `RelationshipGraph.cs`: bidirectional affinity graph backed by the Relationships table; rivalry scores feed baseball probability modifiers (via the event bus, never direct reference).
- Heir mechanics: genetic stat blending, hidden `baseball_interest`, succession on retirement, game-over when lineage fails.

**Exit criteria:** simulated 3-generation run with succession handoffs.

## Phase 7 — Gritty Event Framework (COMPLETE)

- `EventDispatcher.cs` background polling of Entity_Flags/Players; `ConditionEvaluator.cs` for prerequisite booleans; JSON event schema (prerequisites, probability weight, payload, branching choices, hidden flag writes).
- Sonnet 5 authors event content at volume from Fable 5's schema.
- **Validate:** `check_event_graph_integrity` after every content batch — no orphaned flags, no dead-end branches, no unreachable events.

**Exit criteria:** cascading chain proven end-to-end (e.g., bribe → `compromised_syndicate` → syndicate event fires seasons later).

## Phases 8 & 9 — Economy, Hustles, & Career Ladder (Interleaved) → Milestones M4 & M5 (ACTIVE)

*Note: Per the `docs/phase_8_9_interleave_plan.md`, Phase 8 and 9 are being interleaved to stand up a skeletal career clock before layering on hustles.*

### 9a — Tier Schema + Multi-Tier Macro-Sim

**Owner:** Fable 5

- Schema v7: add a `tier` dimension to `Teams` (HS, College, MinorA, MinorAA, MinorAAA, MLB).
- Query-layer tier filters for `BaseballQueries`.
- `LeagueGenerator` seeds one independent 8-team league per tier.
- Opus 4.8 designs offensive-baseline deltas; Fable 5 implements `tier → LeagueSimulator` map.
- `CareerManager` tier rewiring for correct scheduling.
- `StatsNormalizer` scoping per-tier.
- New-game avatar creation defaults to an HS team.
- **Validate:** `run_monte_carlo_batch` gains a per-tier band check and a regression guard for the existing MLB band.

### 9b — Bare Daily-Clock Skeleton

**Owner: Fable 5 engine, Sonnet 5 UI.**

- Split the avatar out of `LifeSimManager`'s autopilot tick — avatar's hourly actions become player-chosen.
- Schedule surface: allocate hours across Sleep / School / Practice / Game / Work blocks. Game reuses `CareerManager` flow. Practice/School are inert placeholders for now.
- New UI scene: `Assets/UI/ScheduleScreen.tscn`.
- **Validate:** Extend `NeedsDecayHarness` / new harness proving scripted manual block choices behave as expected and untouched autopilot reproduces pre-9b traces.

### 8a — Survival Economy + Legal Work

**Owner:** Sonnet 5

- Recurring rent/food/gear drain on a calendar cadence, through `PlayerQueries.AdjustFunds`.
- Legal Work becomes the first real Work-block payout (modest funds, energy/fitness drain).
- **Validate:** `simulate_utility_decay` extended with funds-solvency check over a simulated month.

### 8b — Narcotics (3-tier state machine) + Fencing Negotiation

**Owner:** Opus 4.8 designs, Sonnet 5 implements, Fable 5 reviews.

- Isolated Hustle scene nodes. Narcotics: Inventory Drop → Profit/Toxicity Cut → Territory Control. Fencing negotiation.

### 8c — Arrest / Injury / Suspension Risk Triad

**Owner:** Fable 5 (engine), Sonnet 5 (event content).

- Roster/availability mutation type for Gritty Events.
- Arrest (jail time-skip), Injury (availability/rating hit), Suspension (`detection_risk` benching).

### 8d — Texas Hold'em, 8e — Equipment Quality

- **8d owner:** Opus 4.8 (pot-odds math), Sonnet 5 (implementation).
- **8e owner:** Fable 5 (gear tiers as effective-ratings modifiers).

### 9c — Promotion / Advancement Gates, 9d — Player Development / Decline Curves

- **9c owner:** Opus 4.8 (promotion model), Fable 5 (tier-transfer handoff).
- **9d owner:** Opus 4.8 (growth/decline curves), Sonnet 5 (tuning harness). Practice block finally produces a real stat effect.

**Exit criteria (Phases 8 & 9):** a generated player climbs from HS to the MLB across simulated seasons via performance + development; each tier's league line sits in its own calibrated band; the avatar's daily schedule measurably moves both stats and needs; full career loop playable (broke rookie → hustle income → stress consequences → gritty events → season play).

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
