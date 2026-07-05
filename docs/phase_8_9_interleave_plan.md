# Continuing Dirt & Diamonds: Phase 8/9 Interleave Plan

## Context

`BUILD_PLAN.md` lays out 11 phases; `docs/progress.md` (849 lines) shows Phases 0–7 shipped and fully harness-proven as of 2026-07-04 — database core, event bus, Monte Carlo macro-sim (M1), Markov micro-sim (M2), career/avatar wiring with bullpens+arsenals+schema v4, the life-sim needs/utility engine (M3), relationships/rivalry/heir-mechanics/succession (Phase 6, 3-generation run proven), and the full Gritty Event framework including marriage/conception (Phase 7). Schema is at v6. The log's own last line: *"The project has no open items pending fresh direction."* One small uncommitted retune (RivalryEffects ±16 + a harness rewrite) is already verified and just needs a commit.

BUILD_PLAN orders **Phase 8 (Economy & Hustles)** before **Phase 9 (Career Progression Ladder)**. But Phase 9 fixes the single biggest gap between the shipped build and `GAME_IDEA.md`'s actual pitch: today the game seeds one MLB-tier league and drops the avatar straight in as a made pro. There is no high-school starting point, no promotion ladder, and — critically — no player-driven daily clock: `LifeSimManager`/`UtilityCalculator` already runs a 24-hourly-tick **autopilot** loop for every NPC, avatar included. Building Phase 8's hustles as BUILD_PLAN's literal "time-skip abstractions" first would mean rewiring all four of them into a real clock later, once Phase 9 lands.

**Decision (confirmed with the user): interleave.** Stand up a minimal Phase 9 skeleton — the tier schema/multi-tier macro-sim plus a bare daily-clock loop that lets the avatar's hours be player-chosen — with explicitly **no promotion-gate AI and no development/decline curves yet**. Then build every Phase 8 hustle directly as a real "Work" block on that clock. Promotion gates and development curves (Phase 9's remaining, harder pieces) come after Phase 8 is done.

A Plan-agent pressure-test against the live code (`LeagueSimulator.cs`, `CareerManager.cs`, `BaseballQueries.cs`, `LeagueSchedule.cs`, `MicroGame.cs`) confirmed the ordering below is sound but caught real scope gaps in step 9a (query-layer tier filters, `CareerManager`'s tier-relative avatar index) — folded in below so 9b doesn't build on a foundation that silently mis-schedules the avatar's games.

## Sequenced Build Plan

Each step below is sized as one session's work, matching this project's own established cadence (see `progress.md`'s per-session entries) — schema-first where relevant, harness-proven before merge, model assignment stated. Standing rule carried forward unchanged: re-run `run_monte_carlo_batch` after anything touching the sim assembly; escalate to Fable 5 if any band moves.

### 9a — Tier Schema + Multi-Tier Macro-Sim

**Owner: Fable 5** (re-enters the calibrated core; carries the "no band moved" burden across every tier).

- Schema v7 (No Blind Queries: validate scratch + live via the `sqlite` MCP before writing DDL): add a `tier` dimension to `Teams` (HS, College, MinorA, MinorAA, MinorAAA, MLB) as a new column or reference table — additive, idempotent, same pattern as every prior migration (v2→v6).
- **Query-layer tier filters** (the pressure-test's real finding): `BaseballQueries.LoadAllTeams`/`LoadRoster`/`LoadFreeAgents` currently load every row in the DB unconditionally — each needs a tier-scoped overload.
- `LeagueGenerator` seeds one independent 8-team league per tier (**keep the 9+5+3 roster invariant uniform across every tier** — `LineupSize`/`RotationSize`/`BullpenSize` are `public const int` on `LeagueSimulator`, shared verbatim by `MicroGame`'s stackalloc/array-sizing; varying them per tier is a materially bigger refactor than this step should absorb. Disclose uniform rosters across HS→MLB as a deliberate known simplification).
- **Opus 4.8** designs each tier's offensive-baseline deltas as adjustments layered on the existing MLB-calibrated `AtBatResolver` weights (same precedent as the PED/rivalry modifiers — never touching the resolver's calibration tables directly). **Fable 5** implements: `LeagueSimulator` becomes one-instance-per-tier (a `tier → LeagueSimulator` map replaces the single instance), each running its own round-robin via `LeagueSchedule`.
- **`CareerManager` tier rewiring** (the pressure-test's second finding): `ActivateAvatar` builds `_avatarTeamIndex` from an unfiltered global team load, and `OnDayAdvanced` feeds that index into `LeagueSchedule.TryGetPairingFor` assuming an 0..7 index within one round-robin. Once `Teams` spans 6 tiers this index must become tier-relative, and `CareerManager` needs to hold a tier→sim map (mirrors the multi-`LeagueSimulator` change above) so the avatar's actual tier resolves correctly. Do this now — not deferred to 9b — or the daily-clock skeleton ships on top of silently-wrong game scheduling.
- **`StatsNormalizer` scoping**: `NormalizeSeason(seasonYear)` re-normalizes all Batting/Pitching_Stats rows for a season with no team/tier filter. Six tier-scoped sims each calling it independently means redundant rewrites and a transient cross-tier inconsistency mid-cycle — scope it per-tier in this same pass.
- New-game avatar creation defaults to an HS team.
- **Validate:** `run_monte_carlo_batch` gains a per-tier band check (Opus's designed ranges) **and** a regression guard that the existing MLB band is still exactly where Phase 3 left it (.247/.315/.412-ish). This is the step that could silently break the whole game's calibration if rushed — treat it with the same rigor as the v4 bullpen pass.

### 9b — Bare Daily-Clock Skeleton

**Owner: Fable 5 engine, Sonnet 5 UI.**

- Split the avatar out of `LifeSimManager`'s per-NPC autopilot tick — the avatar's hourly actions become player-chosen (or default-autopiloted if the player does nothing, mirroring the existing `AutopilotAttendedGames` precedent so headless harnesses keep working unmodified).
- New schedule surface: the player allocates today's hours across **Sleep / School / Practice / Game / Work** blocks. School only competes for hours in HS/College tiers. Game reuses the existing `CareerManager` pending-attended-game flow (now correctly tier-scoped per 9a). Practice/School are **inert placeholders** — they consume hours and produce no stat effect yet; the payoff lands with 9d's development curves. Work is the hook Phase 8's hustles plug into.
- New UI scene (`Assets/UI/ScheduleScreen.tscn`+`.cs`), following the established pattern from `EventChoiceScreen`/`SuccessionScreen`: node paths verified before any `GetNode<T>()`, no direct DB writes, player-intent signals up.
- **Validate:** extend `NeedsDecayHarness`/a new harness path proving a scripted week of manual block choices behaves as expected and that an untouched (autopilot) avatar-day reproduces the pre-9b trace bit-for-bit (the "nothing broke for headless callers" guarantee, same discipline as every prior additive pass).

### 8a — Survival Economy + Legal Work

**Owner: Sonnet 5** (low architectural risk — routes through existing, proven primitives).

- Recurring rent/food/gear drain on a calendar cadence, through the already-atomic `PlayerQueries.AdjustFunds` (floor-clamped SQL, already the gritty-event consequence pipeline's writer).
- Same pass closes the disclosed gap: `LifeSimManager.ApplyAction` currently debits `FinancialCost` into an in-memory-only `NpcRuntime.Funds` mirror, never through `AdjustFunds` — reroute it through the DB path (`FundsImpulseEvent` keeps the in-memory mirror in step, same pattern `EventConsequenceApplier` already uses).
- Legal Work becomes the first real Work-block payout: modest funds gain, Energy/Fitness needs drain, zero risk — per `gritty_events.md`'s own description. This makes the full loop end-to-end testable immediately: broke rookie, recurring costs, Work block, modest income.
- **Validate:** `simulate_utility_decay` extended with a funds-solvency check over a simulated month; `run_monte_carlo_batch` not touched (no sim-assembly surface moved).

### 8b — Narcotics (3-tier state machine) + Fencing Negotiation

**Owner: Opus 4.8 designs the risk/reward math and state machine; Sonnet 5 implements; Fable 5 reviews.**

- Isolated Hustle scene nodes per `ui_conventions.md` (`godot_scene_mapper` before any UI logic). Narcotics: Inventory Drop → Profit/Toxicity Cut → Territory Control vs Factions (using the existing `RelationshipGraph`). Fencing: negotiation mechanic, structure TBD by Opus's design doc.
- These are the first writers of accumulated "risk" state that step 8c's triad will consume.

### 8c — Arrest / Injury / Suspension Risk Triad

**Owner: Fable 5** (new engine surface — the roster-invariant/availability discipline, same weight class as the succession handoff); **Sonnet 5** for event content.

- Genuinely new engine surface, confirmed by exploration: today a rostered player is unconditionally in every game (`LeagueSimulator`/`MicroGame` build fixed lineup/rotation/bullpen slots once at `Initialize()`; no `IsAvailable`/benched-for-N-games concept exists anywhere).
- Arrest: jail time-skip + flags. Injury: availability + temporary rating hit keyed to `health_ceiling` (the same floor `HeirGenetics.HealthRetirementFloor` and the `injury_scare` gritty event already key off). Suspension: `detection_risk` thresholds (already written by the PED-cost path) benching the avatar for N games.
- Extends the Gritty Event consequence vocabulary (`gritty_event_framework.md` §4's closed enum) with a new roster/availability mutation type — genuinely new, not reconfiguration, per the design doc's own explicit deferral note.

### 8d — Texas Hold'em, 8e — Equipment Quality (can run in parallel with or after 8b/8c)

- **8d owner:** Opus 4.8 (pot-odds/bluffing math), Sonnet 5 (implementation), Fable 5 (review).
- **8e owner:** Fable 5 (calibrated-core-adjacent: purchasable gear tiers as effective-ratings modifiers, same precedent as PED/fatigue/rivalry — never touching `AtBatResolver`'s calibration tables directly, behind the `run_monte_carlo_batch` band check).

### 9c — Promotion / Advancement Gates, 9d — Player Development / Decline Curves

Resume once Phase 8 is done.

- **9c owner:** Opus 4.8 designs the promotion model (performance + scouting); Fable 5 owns the tier-transfer handoff (the avatar changes team *and* tier mid-save — the succession-handoff's sibling, same roster-invariant discipline).
- **9d owner:** Opus 4.8 designs peak-age growth/veteran decline curves; Sonnet 5 owns the tuning harness. This is where the Practice block (built inert in 9b) finally produces a real stat effect.
- **Exit criteria (BUILD_PLAN's own):** a generated player climbs HS→MLB across simulated seasons via performance + development; each tier's league line sits in its own calibrated band; the avatar's daily schedule measurably moves both stats and needs.

## Verification Methodology (unchanged project convention)

- No Blind Queries: every schema change validated via the `sqlite` MCP / `validate_sqlite_schema` skill before dependent C# is written.
- Every step that touches the sim assembly re-runs `run_monte_carlo_batch`; a moved band escalates to Fable 5.
- New UI scenes verified against their `.tscn` node tree before any `GetNode<T>()` call, per `ui_conventions.md`.
- New Gritty Event content batches run `check_event_graph_integrity`.
- Append each completed step's summary + next steps to `docs/progress.md` before ending a session, per the project's cross-cutting discipline — this plan's steps are sized to become that log's next several entries.

## Immediate Next Action

Commit the already-verified, uncommitted `RivalryEffects` ±16 retune (currently sitting in the working tree) before starting 9a, so 9a begins from a clean tree.
