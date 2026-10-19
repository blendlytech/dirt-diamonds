# Task Brief: 13-2 — Roster capacities, regulation innings, ride-the-ace starter selection

**Status:** **DRAFT, awaiting owner approval** (Claude Opus 5.5, 2026-10-18). Six owner decisions are open in §3.2 (D1–D6); each has a recommendation. Not startable until the owner approves them.
**Recommended model:** Claude Opus 5.5 (TEAM.md §2)
**Reviewer:** GPT 6 Astra Pro (sim diff + Math, TEAM.md §2)
**Owner gate:** this brief's decisions D1–D6 and the §5.3 expected-red list.
**Spec:** [league_restructure.md](../design/league_restructure.md) §4, §5.1, §5.2, §6, §13 (`LeagueSimulator`, `MicroGame`, `TierEffects` rows), §15 (13-2 row), §16-I1/I6/I7; [math/college_28_game.md](../design/math/college_28_game.md) (CS-M) §2, §2.1, §6; [arcade_baseball_overhaul.md](../design/arcade_baseball_overhaul.md) §8.1 (legal-defense lineups)
**Branch / worktree:** `p13/13-2-opus` from **`p13/integration`** (at or after `d951efe`), in your own worktree. Merges into `p13/integration`, never `main`. A stale local `p13/13-2-opus` from 2026-10-11 (`50301f3`, built on the superseded first 13-1) exists: rename it to `p13/13-2-opus-oct11` first, never delete it. Its `RosterCapacity`/`TierRules`/`StarterSelection`/`PitcherRestLedger` shape is reference only; its rest values predate the owner's College decision.

## 0. Preconditions (check first; stop and report if any fails)

1. 13-1 is merged into `p13/integration` (`d951efe`, 2026-10-18). ✔ at drafting.
2. `git fetch --all`, `git status`, `git worktree list`; nobody else holds `p13/13-2-opus`.
3. Baseline on the branch base before any edit: build 0/0; SchemaValidator 167/167; MonteCarlo 157/183 with exactly the 13-1 red list (brief 13-1 §8.1/§8.2); `--only resolver` 13/13. Record any difference.

## 1. Context

13-1 gave the world its v14 shape (68 teams, per-tier rosters of 16–24 players, `Player_Fielding`), but both sims still assume 8 teams × 9 + 5 + 3, nine innings and a five-man rotation, so every world-building suite and the career boot stop at `LeagueSimulator.Initialize`. 13-2 makes the sims run the v14 roster shapes: fixed-capacity buffers looped to per-team counts (§5.2, zero allocation preserved), regulation innings per tier (§4), and one shared "ride the ace" starter selection (§6) that produces the source spec's 2-man rotations with no special-case AI. 13-3 then replaces the interim calendar with persisted schedules and records the new Pro guard.

Code facts this brief relies on (verified at `d951efe`):

- `LeagueSimulator.cs:26–37`: `TeamCount = 8`, `LineupSize = 9`, `RotationSize = 5`, `BullpenSize = 3`, `RosterSizePerTeam`, `RegularSeasonDays = RoundsPerCycle (7) × 22`. `Initialize` (~L211) throws unless a tier has exactly 8 teams. Flush every 7 days (~L427, `DayOfSeason % RoundsPerCycle`).
- `LeagueSchedule.cs`: circle-method round-robin hard-wired to `LeagueSimulator.TeamCount` (`PairingsPerDay`, `RoundsPerCycle`).
- `MicroGame.cs:367–456`: slot arrays sized `_teamCount × LineupSize/RotationSize/BullpenSize`; throws unless every team has 9/5/3; rotation `_teamGamesPlayed % RotationSize` (~L785); bullpen loop to `BullpenSize` (~L914).
- Regulation innings hard-coded `inning >= 9` at `LeagueSimulator.cs:524/531` and `MicroGame.cs:833/844`.
- `CareerManager`, `DevelopmentManager`, `PromotionManager` read `LeagueSimulator.TeamCount`/`RosterSizePerTeam` for capacity hints and the 9c ladder.
- `PitcherFatigue` (micro only) has no multi-day recovery, and its `EffectiveRatings` contracts toward 50 (CS-M §2.1 R1-MATH dependency). R1-MATH is not started.

## 2. Hard constraints

- **Allowed paths:** `Assets/Simulation/Baseball/`: `LeagueSimulator.cs`, `MicroGame.cs`, `LeagueSchedule.cs` (D1 only), new `RosterCapacity.cs`, `TierRules.cs`, `StarterSelection.cs`, `PitcherRestLedger.cs`, `LineupBuilder.cs`; compile/handoff edits in `CareerManager.cs`, `DevelopmentManager.cs`, `PromotionManager.cs`. `Assets/Core/GameManager.cs` (wiring). `Assets/Data/Database/` (D4 only: the rest table and its queries). `Tools/MonteCarloHarness/`, `Tools/CoreLoopHarness/`, `Tools/SchemaValidator/` (D4). `docs/progress.md` (one line) and this brief's §9 (the reviewer's).
- **Not allowed:** `AtBatResolver.cs`, `BaseOutMatrices.cs`, `PitcherFatigue.cs` and `EffectiveRatings` (R1-MATH / G-4), `Assets/Simulation/Arcade/`, `Assets/Simulation/Life/`, any `docs/design/` spec, `GEMINI.md`, `.agents/`, UI scenes.
- **Calibration contract.** The resolver guard (§5 fixtures + §8 batch) stays byte-identical. No probability, weight, tier delta or fatigue value changes. The workload knobs ship neutral (D5), so a fully rested start is bit-identical to today's start path. The old season guard stays unreachable (13-3 records the new Pro guard); 13-2 reports, never re-baselines.
- **Zero allocation.** Buffers are sized once at `Initialize` by capacity (`RosterCapacity`), never per tier; loops run to per-team counts. The warm macro game day and a warm attended game allocate 0 bytes, as today.
- **Determinism.** Starter selection draws no random number (CS-M §2). Same seed + same inputs → identical games; reload → identical selections.
- **One rule, two sims.** Macro and micro use the same `StarterSelection` and the same rest ledger. The macro sim stays complete-game (§6).
- **GEMINI §1:** the sims read only the DB and bus; no Life references; no presentation touch.

## 3. Inputs

### 3.1 Spec decisions to implement as written

- `RosterCapacity`: `Lineup = 9`, `MaxBench = 8`, `MaxStarters = 6`, `MaxRelievers = 6` (§5.2). A team over capacity fails loudly at `Initialize`.
- `TierRules.RegulationInnings`: HS 7, College 9, Pro 9 (§4). The home half is skipped when the home team leads after the top of the final regulation inning; extra innings as shipped. No ties, no mercy rule.
- `TierRules.MinRestDays`: HS 4, **College 3 (owner, 2026-10-18)**, Pro 3 (§6). The day count is the calendar-date difference from the last start (Wed → Sat = 3).
- Starter selection (§6 + CS-M §2): filter absent pitchers, then (in a series) starters who already started in it; start the highest-quality starter with `gap ≥ MinRestDays`; else the best-rested unused starter on short rest (gap descending, then the quality order); else a reliever opens (bullpen game). The fallback never bypasses the series filter. A never-started starter counts as rested.
- **Red Dirt Series rule (hard):** within one series, no starter starts twice (§6, §16-I7). 13-2 builds the series filter into the selector and proves it with a synthetic series fixture; 13-4 supplies real series context.

### 3.2 Owner decisions (recommendations marked)

- **D1. Team count and the interim calendar.** The circle-method schedule needs `N − 1` days per round-robin; 12/24/32 teams × 22 cycles would run 242–682 days, past a year. **Recommended (A):** make the team count per sim instance (from the tier's `Teams` rows, capacity 32) and generalise `LeagueSchedule` to any even N, playing one round per game day over today's 154-day window (rounds wrap; flush every 7 days unchanged). This is an interim schedule, replaced wholesale by 13-3's `SeasonScheduler`, but it lets every tier sim, the career boot and most MC suites run again in 13-2. *Alternative (B):* keep `TeamCount = 8` and prove 13-2 only on harness fixture worlds of 8 teams with v14-shaped rosters; the career boot stays red until 13-3, and 13-3 takes team count + scheduler together.
- **D2. The starter-quality scalar** (the spec doesn't define it; CS-M §2 proposes one for 13-2 to own). **Recommended:** CS-M's proposal, unchanged: the mean of the starter's tier-shifted effective stuff and control before in-game fatigue; ties by stamina descending, then player id ascending. One definition across HS, College and Pro; no tier gets a private scorer. Opus (as CS-M's reviewer) supports it; GPT confirms as Math owner in review.
- **D3. Who plays (lineups).** Rosters now carry 11–15 position players. **Recommended:** a deterministic legal-defense pick from `Player_Fielding` (arcade §8.1): for C, 1B, 2B, 3B, SS, LF, CF, RF in that order, the best available player whose primary is that position, else whose secondary is, else the best remaining (recorded as out of position for G-4; no rating effect in 13-2); the DH is the best remaining bat. "Best" = the batter role-rating sum (power + contact + discipline) the shipped 9c scouting score uses (`PromotionManager.cs` ~L700); ties by player id. The batting order keeps the shipped slot order of the nine chosen. The team-defense byte is unchanged. *Alternative:* the first nine position players in roster order, deferring legal defense to 13-5.
- **D4. Rest persistence.** CS-M §2 requires start dates to survive weeks, year rollover and reload. **Recommended:** a small additive v14 table `Pitcher_Rest (player_id PK → Players ON DELETE CASCADE, last_start_day INTEGER NOT NULL)`, written in the sims' existing flush batch. v14 is unreleased (13-1 ships no v14 save anyone keeps), so this is a v14 addition, not a v15; SchemaValidator gains its checks. Rest written since the last flush can be lost on a mid-cycle quit, exactly like stats today (R-1 B01, fixed by R1-SAVE-1, not here). *Alternative:* in-memory only (a reload makes every starter rested).
- **D5. Workload knobs.** `ShortRestCapacityPenalty` and `OveruseInjuryRisk` (§6) ship as named Math data values set to **0** (neutral: no capacity change, no extra RNG draw), with the short-rest hook wired and tested for the neutral identity. GPT sets real values after R1-MATH fixes the fatigue contract (CS-M §2.1: 0.15 / 0.01 are candidates, not settled).
- **D6. The §5.3 expected-red list** below.

## 4. Outputs

1. `RosterCapacity.cs`, `TierRules.cs` (innings, rest, the two D5 knobs), `StarterSelection.cs` (pure, allocation-free, used by both sims), `PitcherRestLedger.cs` (last-start day per player; hydrated at `Initialize`, flushed with the sim's batch), `LineupBuilder.cs` (D3).
2. `LeagueSimulator` and `MicroGame`: capacity-sized buffers, per-team counts, per-tier regulation innings, starter selection replacing the 5-man `% RotationSize` cycles, bullpen loop to the team's reliever count, bullpen games when no starter is available. Per-instance team count and the generalised `LeagueSchedule` under D1-A.
3. `GameManager`: one shared rest ledger wired to every tier sim and the micro-sim.
4. `CareerManager`/`DevelopmentManager`/`PromotionManager`: compile against the new counts; the attended-team handoff uses per-team counts. `PromotionManager`'s six-tier pass stays red (13-6).
5. D4: `Pitcher_Rest` DDL + query class methods, validated on the scratch DB first (No Blind Queries).
6. Harness work (§5) and the handoff (§8).

## 5. Tests and gates

### 5.1 Must pass

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings, 0 errors |
| Schema (D4) | `dotnet run --project Tools/SchemaValidator -c Release` | all green; new `Pitcher_Rest` checks (columns, FK, cascade) |
| Resolver | `MonteCarloHarness -- --only resolver` | 13/13, every line byte-identical to the base |
| New `capacity` suite | `-- --only capacity` | buffers sized by capacity; mixed HS shapes (3–4 SP, 2–4 RP, 11–14 pos) and College/Pro 4/5/15 simulate; an over-capacity team fails loudly; warm macro day and warm attended game allocate 0 bytes |
| New `innings` suite | `-- --only innings` | HS games are 7 innings in both sims (bottom of the 7th skipped when home leads; extras when tied); College/Pro 9; deterministic replay |
| New `starters` suite | `-- --only starters` | **Rest fixtures (§6, rest 3):** healthy College 4-SP staff over 28 Wed/Sat dates → 28/0/0/0 starts, zero short-rest; gap 2 ineligible, gap 3 eligible; ace the only SP → 28 starts, all rested. **Pro** Tue/Thu/Sat → ace 2 of 3, #2 the Thursdays. **HS** weekly → ace every game. Best-eligible + stable ties (D2); best-rested short-rest fallback; bullpen game when no SP; absence filter and return; never-started = rested; year-boundary and reload equivalence; **series fixture:** Thu/Fri/Sat Bo3 → ace G1, #2 G2, #3 G3, never one starter twice, including the fallback; macro/micro select the same starter for the same inputs; selection allocates 0 bytes |
| New `lineups` suite (D3) | `-- --only lineups` | every generated team fields a legal defense (8 field positions + DH); out-of-position only when forced; deterministic; team-defense byte unchanged |
| worldgen-v14, ui-strings | `-- --only worldgen-v14,ui-strings` | 23/23, 3/3 (unchanged) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 22/22 |
| Isolation | `git diff --stat origin/p13/integration...HEAD` | only §2 allowed paths |

### 5.2 Must not move

The resolver guard (above). The workload knobs at 0 keep every rested start's capacity and RNG draws identical to the shipped path.

### 5.3 Expected red after 13-2 (owner approval needed; the handoff reports the actual list)

Under **D1-A**:
- **The season-pipeline guard** (PA 48384 / H 10969 / ER 5237) stays unreachable: it is tied to the 8-team 154-game MLB world. 13-3 records the new Pro guard (§11.3).
- **Per-tier bands and the tier ladder** (`tier-ladder`, season-pipeline band checks): reported, not gated, because the interim calendar plays 154 games for every tier; 13-3 re-bands on the real calendars (CS-M §6).
- **Promotion suites** (`promotion-*`, `practice-seam`, `development-equilibrium` and whatever else rides the 9c ladder): red until 13-6 replaces `PromotionManager`.
- **Fixtures that hard-code team ids 1–8** (e.g. `v4-bullpen-arsenal`, `micro-game`): migrated to v14 ids where structural, reported where not.
- Everything else (career wiring, lineage, rivalry, availability, equipment, person effects, heir genetics, …) is expected to **run again** and must pass or carry an explained, owner-visible reason.

Under **D1-B**: the 13-1 red list stands, minus whatever the new fixture suites prove.

### 5.4 Retiring checks

As in 13-1: retire by name with the reason and the replacement (e.g. the five-man-rotation assumptions → the `starters` suite). Never delete a check to make a count match.

## 6. Definition of done

- [ ] Preconditions §0 recorded; the stale branch renamed, not deleted.
- [ ] `RosterCapacity`, `TierRules`, `StarterSelection`, `PitcherRestLedger`, `LineupBuilder` per D1–D5; no `LineupSize`/`RotationSize`/`BullpenSize`/`RosterSizePerTeam` sizing left in either sim.
- [ ] Regulation innings per tier in both sims; no hard-coded `inning >= 9`.
- [ ] One starter-selection rule in both sims, the series rule included; rest persisted per D4.
- [ ] §5.1 green; §5.2 unchanged; §5.3 actual list recorded; §5.4 retirements listed.
- [ ] Committed on `p13/13-2-opus` and pushed. Not merged.
- [ ] Handoff + one `progress.md` line. **Then stop.**

## 7. Out of scope

- `SeasonScheduler`, `TierSeasonCalendar`, `Schedule_Games` writes, the 28-game College calendar, per-tier bands, the new Pro guard (**13-3**).
- Playoffs, series context, the Red Dirt Series itself, ranking (**13-4**); 13-2 only builds and proves the selector's series filter.
- The Sunday lock, `Roster_Designations`, forfeits, retiring the replacement call-up (**13-5**); absences keep the shipped replacement behavior.
- `PipelineManager` (**13-6**). Real `ShortRestCapacityPenalty`/`OveruseInjuryRisk` values and the fatigue fix (**R1-MATH**, GPT). What out-of-position means on the field, speed/arm effects, two-catcher rosters (**G-4**).
- Any `docs/design/` edit (errors go in the handoff), and merging.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built>  · Base: p13/integration @ <sha>  · Commit: <sha>
Gates: build → <w/e>; Schema → <p/t>; resolver → <byte-exact?>; capacity → <p/t>; innings → <p/t>;
       starters → <p/t>; lineups → <p/t>; worldgen/ui-strings → <p/t>; CoreLoop → <p/t>; isolation → <empty?>
MC default: <p/t>; red suites (actual): <suite → p/t, reason>; retired checks: <name → reason → replacement>
Decisions used: D1–D6 <as approved / changed and why>
Open: <R1-MATH workload values, G-4 out-of-position, risks>
Recommended next: 13-2 review by GPT 6 Astra Pro; then 13-3 by Claude Opus + GPT 6 Astra Pro (Math)
```

Then add one compact line to [progress.md](../progress.md). Don't start 13-3.

## 9. Review (GPT 6 Astra Pro)

*Reserved for the reviewer: verdict, findings, gate re-runs.*
