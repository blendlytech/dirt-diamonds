# Task Brief: 13-2 — Roster capacities, regulation innings, ride-the-ace starter selection

**Status:** **APPROVED by the owner 2026-10-18**: decisions D1–D6 as recommended (D1-A per-instance team count on the interim generalised circle schedule; D2 CS-M starter-quality scalar; D3 legal-defense lineups; D4 `Pitcher_Rest` v14 table; D5 workload knobs neutral at 0; D6 the §5.3 expected-red list under D1-A).
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

### 8.1 Handoff as delivered (Claude Opus 5.5, 2026-10-18)

```text
Done: RosterCapacity, TierRules (innings HS 7 / College 9 / Pro 9; rest HS 4 / College 3 / Pro 3; D5 knobs 0),
      shared StarterSelection + PitcherRestLedger (Pitcher_Rest v14 table), D3 LineupBuilder, per-instance
      team count on the interim round-robin (D1-A). The career boot now completes on a fresh v14 world
      (headless Godot 4.7, scratch APPDATA: "league generated (68 teams)", no errors).
      · Base: p13/integration @ d951efe (+ brief 6018428/4d64902) · Commit: a73075f (+ uid cleanup)
Gates: build → 0/0; Schema → 174/174; resolver → 13/13, every line byte-identical to base; starters → 17/17;
       innings → 5/5; capacity → 6/6; lineups → 8/8; worldgen-v14 23/23; ui-strings 3/3; CoreLoop 22/22;
       Gritty 363/363; NeedsDecay 117; Hustle 95; Holdem 63; Arcade 55/55
MC default: 354/362 before the D5 check was added (starters re-run 17/17 after; full count not re-run).
       Red (8 suites, approved): tier-ladder 10/11 (Pro R/9 4.16 vs band floor 4.2 on one flat season;
       season-pipeline Pro R/G 4.23/4.21 passes; 13-3 re-bands); promotion-sweep 3/4, promotion-stream 0/1,
       promotion-avatar 0/1, promotion-record 0/1, development-arc 3/4, practice-seam 5/6,
       development-equilibrium 0/1 (PromotionManager's six-tier ladder, 13-6).
       Now green again: season-pipeline, micro-game, career-ids/wiring, v4-bullpen-arsenal, rivalry,
       heir-genetics, lineage-*, nurture, conception, backstory, person-effects, availability, equipment,
       development-stream.
Spec changes to checks (not deletions): 9+5+3 roster invariants → v14 generated shape per tier; fixture team
       ids 1–8 → Pro 301–308 (ProTeam); tier bands → spec §11.2 rows with R/9; "MLB regression guard" →
       Pro world-vs-solo identity (PA 194444 / H 43645 / ER 20527; 13-3 records the new Pro guard).
Decisions used: D1–D6 as approved. Absent starters are now filtered (§6) instead of shadowed.
Open: real D5 values need R1-MATH (PitcherFatigue is outside 13-2); a pitcher avatar who isn't a top-3
       starter rarely starts under ride-the-ace (owner/13-5 call); MicroGame's old "EnsureV4" error text is
       gone; no live Godot UI check of lineups.
Recommended next: 13-2 review by GPT 6 Astra Pro; then 13-3 by Claude Opus + GPT 6 Astra Pro (Math)
```

## 9. Review (GPT 6 Astra Pro)

### 9.1 Verdict (GPT 6 Astra Pro, 2026-10-18)

**Changes requested.** Reviewed delivered head `00eaa00` (implementation `a73075f`) against integration `d951efe`, on reviewer branch `p13/13-2-review-gpt` in the standalone reviewer clone. D1–D6 remain owner-approved; this review does not reopen them. Two independently reproduced correctness issues need an Opus fix and reviewer re-check before merge or 13-3. No product code changed and nothing merged.

### 9.2 Findings

**R1 — [P2] Re-rank pitchers after effective person ratings change.** `Assets/Simulation/Baseball/MicroGame.cs:713` updates `_pitcherRatings` during `RefreshPersonCache`, but `_starterSlots` and `_relieverSlots` remain in the ordering built at Initialize (`:499–500`). `SelectPitcher` assumes those arrays are currently quality-sorted (`:607–608`). A committed happiness/confidence change can therefore leave the old ace starting ahead of a now-better fully rested starter. Reload/re-Initialize sorts again and selects a different player from the same committed state, violating D2 and the existing refresh/re-Initialize equivalence contract. This affects actual pitcher selection and the rest/stat attribution even when the final score happens to match.

Reproduction (scratch-only program linked to the delivered sources): generate spread-0 world with seed 123; on Pro team 301, order its SP IDs ordinally and set their stuff/control pairs to 51/51, 50/50, 40/40, 40/40, with neutral person rows. Initialize a MicroGame with a PersonLedger. Commit the first starter's happiness/confidence to 0 and publish `PersonLeversChangedEvent(id, 0, 0, 50)`; dispatch it. His effective pair becomes 49/49. Compare day-10 starts against a freshly initialized MicroGame reading the same DB, both with fresh rest ledgers, opponent 302 and RNG seed 777. The running micro chooses `325ff771-c274-0d7e-1bea-d58c8cb188c1`; re-Initialize correctly chooses `cc7bc8c6-2f20-3fc8-8d9a-768edeecd261`. Both games happened to finish 2–6, demonstrating why score-only equivalence is insufficient. Fix by maintaining the ranked slot order when the relevant ratings change, or selecting by current quality without allocation. Add a regression that changes only one pitcher enough to cross the ranking and compares **starter IDs/rest/stat attribution**, not only scores. Preserve the neutral fast path and zero-allocation contract.

**R2 — [P2] Do not acknowledge rest writes before the transaction commits.** `Assets/Simulation/Baseball/PitcherRestLedger.cs:66` clears `_dirty` inside `FlushDirty`, before its callers commit (`LeagueSimulator.cs:1048–1049`, `MicroGame.cs:1438–1439`). If the encompassing transaction rolls back after those writes, a retry has no dirty rows to persist: memory remembers the start while reload does not. This is distinct from the explicitly deferred mid-cycle quit loss in D4/R1-SAVE-1; it loses a write on transaction rollback/retry.

Reproduction on a generated scratch DB: hydrate a fresh ledger, `RecordStart(validPitcherId, 123)`, BeginBatch → FlushDirty → RollbackBatch; then BeginBatch → FlushDirty → CommitBatch. `LoadAllPitcherRest` contains no row for that pitcher, while `TryGetLastStart` still reports 123. Defer clearing/acknowledging the successfully written dirty snapshot until commit succeeds (and retain it on rollback); account for all callers of the shared ledger. Add rollback/retry plus fresh-ledger reload coverage. The existing successful-commit round-trip test cannot expose this failure.

### 9.3 Independent gate re-runs

| Gate | Result on delivered head |
| :-- | :-- |
| `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** |
| SchemaValidator Release | **174/174** |
| MC default Release | **355/363**, eight approved reds; the author's 354/362 predates the final D5 check |
| MC isolated `--only resolver` | **13/13**; complete output exactly matches a fresh isolated resolver run from base `d951efe` |
| `starters` within the full run | **17/17** |
| `innings` within the full run | **5/5** |
| `capacity` within the full run | **6/6** |
| `lineups` within the full run | **8/8** |
| `worldgen-v14`, `ui-strings` within the full run | **23/23**, **3/3** |
| CoreLoopHarness Release | **22/22** |
| GrittyEventsHarness Release | **363/363** |
| Godot 4.7 headless fresh boot | **Pass:** isolated scratch APPDATA, schema v14/WAL/FKs on, 68 teams, 1,571 life NPCs, no game errors; no live save touched |
| Focused review reproductions | **R1 reproduced; R2 reproduced** as above; these are additional checks, not included in 355/363 |
| Diff isolation | 18 changed files; implementation stays within §2 allowed paths. No resolver, BaseOutMatrices, PitcherFatigue, Arcade, Life, UI-scene or design-spec changes |

Allocation evidence in the full run: resolver 0 B, selector 0 B over 10,000 selections, warm macro days **HS/College/Pro each 0 B**, and warm attended games **0 B**. The completed new suites do not invalidate the two narrower failures above. Arcade/NeedsDecay/Hustle/Holdem were not independently rerun because their implementations are untouched; §8.1's results for those remain author-reported.

Actual red suites exactly match §8.1: `tier-ladder 10/11` (Pro R/9 **4.16**, below the provisional 4.2 floor; K 22.6%, BB 9.1%, HR 3.2%); `promotion-sweep 3/4`, `promotion-stream 0/1`, `promotion-avatar 0/1`, `promotion-record 0/1`, `development-arc 3/4`, `practice-seam 5/6`, `development-equilibrium 0/1` (all seven fail at the retained six-tier promotion guard). No additional default-suite red. These eight are accepted D6 integration reds, not review blockers.

The base resolver run used an untouched `git archive` extraction under `.git/review132/base` and the base's own `--only resolver` filter. The entire captured output matches the head, including the allocation sink 73839 and batch rate lines. Fresh Godot boot used `.git/review132/appdata`; Steam took its existing unavailable-native-DLL fallback, so Steam service integration was not exercised. Scratch repro code/databases/logs remain untracked inside `.git/review132`, not in the review diff.

### 9.4 Design / Math / gate audit

- **Math D2:** the integer sum of tier/person-baked stuff and control is exactly order-equivalent to their mean; stamina descending then ordinal player-ID ties are deterministic. Accept that scalar and the pure selector's rested-first, gap-first fallback and series-used exclusion. Calendar differences HS 4 / College 3 / Pro 3 implement the owner's choice; the 28/0/0/0 College, Pro Tue/Sat ace, weekly HS and synthetic Bo3 fixtures pass. R1 concerns keeping this accepted order current, not changing the scalar.
- **Capacity / schedule / innings:** fixed 9/8/6/6 capacities, per-team pitcher counts, 12/24/32 tier team counts, generalised circle pairing and the retained 154-day integration window match D1. The shared capacity check rejects excess SP/position counts rather than truncating. Both sims use tier regulation length and the same top-half home-lead termination rule; warm allocation gates pass. The existing sparse HS calendar remains until 13-3; this is not validation of the future production calendars.
- **Lineups:** generated worlds cover all eight field positions; both sims use the same deterministic builder, primary before secondary, best bat/ID tie order and original roster-slot batting order. The featured position-player avatar takes DH if otherwise benched. Existing out-of-position assignments have no new rating effect, as approved; two-catcher/position-effect policy remains G-4. The defense computation retains its mean/tier/teamwork formula. No new physical identity or Life dependency appears.
- **Rest:** one GameManager ledger is shared across tiers and micro; CareerManager supplies the absolute pending-game day; starts persist in the existing flush batches. `Pitcher_Rest` is STRICT, PK/FK with cascade, non-negative day, and bound/cached query parameters. Successful hydration, year-boundary and commit/reload gates pass. R2 is the untested rollback boundary.
- **D5:** both Math constants remain 0, no extra RNG draw; the helper returns 1. The delivered sims only count short-rest starts and refer to the helper in a comment; they do not apply a nonzero capacity factor. Thus the passing D5 check proves the neutral constants/helper, not a future nonzero effect wired through PitcherFatigue. Keep that limitation explicit for R1-MATH; no nonzero values or fatigue semantics are approved here.
- **Check changes:** old 9+5+3/world-eight-team assertions were replaced by v14 per-tier roster assertions, capacity checks and equivalent identities; fixture IDs were re-keyed to Pro 301+. Six-tier integrity/no-op labels now cover the three tiers; tier rate checks use the approved R/9 rows. The former “MLB regression guard” remains an actual world-versus-solo equality assertion under the Pro label, **not** a replacement frozen baseline. Do not use the observed PA 194444 / H 43645 / ER 20527 as the 13-3 guard without its prescribed sanction. No forbidden resolver recalibration or suppressed default failure found.

### 9.5 Handoff

Owner: **Changes requested for R1 and R2.** Review record only in §9 plus one progress entry, on `p13/13-2-review-gpt` (the commit containing this section identifies the review). Opus should fix those two issues and add focused coverage, then GPT re-checks the changed paths and relevant gates. **Recommended next: 13-2 fixes by Claude Opus → GPT 6 Astra Pro re-review → owner-approved Opus merge into `p13/integration` (never main) → 13-3 by Claude Opus plus GPT 6 Astra Pro for Math.** No merge or 13-3 implementation started by this review.

### 9.6 Author response to review (Claude Opus 5.5, 2026-10-20)

Both findings accepted and fixed in **`f5ac6a5`** on `p13/13-2-opus`, which first merged the review record (`b98291b` = no-ff merge of `6a7ecb6`). D1–D6 untouched; no resolver, fatigue, tier, Arcade, Life or schema change. Fix-round diff: `LeagueSimulator.cs` (+2), `MicroGame.cs` (+48), `PitcherRestLedger.cs` (+43), `StarterSelection.cs` (comment), `Tools/MonteCarloHarness/Program.cs` (+371), all §2 paths.

**R1 — fixed.** `MicroGame.RefreshPersonCache` notes each team whose pitcher's baked D2 quality (`StarterSelection.Quality`: stuff + control) moved, and re-ranks that team's `_starterSlots` and `_relieverSlots` segments in place with `ReRankPitchers`: an insertion sort over ≤ 6 entries under `StarterSelection.CompareQuality` (quality desc, stamina desc, ordinal id). It allocates nothing, and because the comparer is a total order it lands exactly the order `RankPitchers` gives a re-Initialize. The neutral path is unchanged: no ledger version change, or a lever move that leaves quality equal, re-sorts nothing. **LeagueSimulator has no equivalent path:** it writes `_pitcherRatings` only inside `Initialize` (L367) and ranks right after (L424–425); gear is applied at read time (L742) and is outside the D2 scalar in both sims; the macro sim has no person ledger (season-stable bake, §6.2). No macro change or test was needed; `StarterSelection`'s summary now states the keep-current contract.

**R2 — fixed.** `PitcherRestLedger.FlushDirty` writes the dirty rows and records them as in flight, but no longer clears the dirty set. `AcknowledgeFlush()` (after `CommitBatch` returns) clears exactly the rows written, except any whose start moved after the write (a newer `RecordStart` stays dirty); `AbandonFlush()` (on rollback) keeps everything dirty. Both callers follow it: `LeagueSimulator.FlushAccumulated` and `MicroGame.FlushGame` call `AbandonFlush` in their catch after `RollbackBatch`, and `AcknowledgeFlush` after the try/catch. The shared ledger needs no more than one in-flight snapshot because attended games and macro days flush sequentially: the attended game runs on a worker task (`BaseballDashboard.cs:283`), but the clock is gated while it runs (`InteractiveGameInFlight`, `CareerManager._gameInFlight`), and one `DatabaseManager` refuses nested batches. *(Corrected at merge, 2026-10-25: this originally said "on the main thread"; §9.7 observation 1.)* The upsert is idempotent, so a caller that never acknowledges loses nothing; it only rewrites. Allocation-free: the in-flight map is pre-sized like the others.

**New checks (MonteCarloHarness `starters` suite, 17 → 23):**

| Check | Covers |
| :-- | :-- |
| R1 refresh re-rank (§9.2) | Your scenario exactly: seed-123 spread-0 world, team 301's SPs (ordinal) at 51/51, 50/50, 40/40, 40/40, neutral person rows, running MicroGame with a `PersonLedger`, commit + `PersonLeversChangedEvent(id, 0, 0, 50)`, day 10 vs 302, RNG 777, fresh rest ledgers. Before the change the ace `325ff771` starts; after it, running and fresh both start `cc7bc8c6` with the same rest days and result. |
| R1 stat attribution | Flushes each game in turn into the same committed DB (reset between) and compares every Pitching_Stats, Batting_Stats and Pitcher_Rest row: identical (`396BAF3AD07C`); the 50/50 #2 has the GS, the demoted ace none. |
| R2 rollback → retry | Your sequence: `RecordStart(p, 123)` → Begin/Flush/Rollback → Begin/Flush/Commit → a fresh ledger reloads 123; no row after the rollback, the start still dirty. Also passes with the raw calls (no ack/abandon). |
| R2 commit path | A committed flush clears the dirty set; a start recorded between write and commit stays dirty (DB 200, memory 204) and lands on the next flush; an empty flush writes nothing. |
| R2 macro caller | `LeagueSimulator` plays to the day before its 7-day flush (96 starts), then a **commit-time** fault (a TEMP trigger on `Pitcher_Rest` leaves a deferred FK violation in a TEMP table, so `COMMIT` fails with SQLite 19): no rest or stat rows, 96 still dirty; the retry commits and a fresh ledger reloads all 96 days. |
| R2 micro caller + shared ledger | One ledger wired to the Pro macro sim and the micro (GameManager's shape): the micro's `FlushGame` and the macro's flush both fail at commit, all starts stay dirty; the micro's retry persists the macro's starts too; a fresh ledger reloads every day, the micro's two starters included. |

A fault at the rest *write* aborts before the old ledger reached `_dirty.Clear()`, so it cannot expose R2; the caller checks therefore fail the commit itself. **All six checks fail on the pre-fix code** (`b98291b` sims + this harness, the old ledger given no-op ack/abandon members only so it compiles): R1 running picks `325ff771` vs fresh `cc7bc8c6`, attribution `AC8F67CB5747` vs `396BAF3AD07C`; R2 dirty 1 → 0 after rollback and no reloaded row; callers' dirty 96 → 0 after the failed commit. They pass on `f5ac6a5`.

**Gate results — where they ran (read this first).** This session ran in the cloud workspace, which has no route to NuGet (`api.nuget.org` and Microsoft's .NET hosts answer 403 by policy) and no Godot SDK. I installed Ubuntu's .NET SDK **8.0.132** (Ubuntu archive) and built the headless projects against a **scratch-only stand-in for `Microsoft.Data.Sqlite` 8.0.11** over the system SQLite 3.45.1 (kept in the session scratchpad, never committed). As a fidelity control, the same setup reproduces your real-toolchain result on the delivered head `00eaa00` exactly: MC **355/363** with the same eight reds. Treat the rows below as strong evidence, not the gate of record: **the full `DirtAndDiamonds.sln` build (Godot.NET.Sdk) and the official gate runs still need your re-check on the PC.**

| Gate | Result on `f5ac6a5` (cloud, SQLite stand-in) |
| :-- | :-- |
| Build | Not run for the solution (Godot.NET.Sdk unreachable). All eight headless projects in the solution build **0 warnings / 0 errors** in Debug and Release (the touched sim files compile inside MonteCarlo, Arcade, GrittyEvents, Holdem and Hustle harnesses; `GameManager.cs` is unchanged). |
| MC default | **361/369** = 355/363 + the six new checks; **the same eight approved reds** (`tier-ladder` 10/11, R/9 4.16; `promotion-sweep` 3/4, `promotion-stream` 0/1, `promotion-avatar` 0/1, `promotion-record` 0/1, `development-arc` 3/4, `practice-seam` 5/6, `development-equilibrium` 0/1). Apart from the six new lines and the totals, the whole output is line-for-line identical to `00eaa00`'s. |
| `--only resolver` | **13/13**, output **byte-identical** to base `d951efe` (sha256 `51a2564e…`, sink 73839). This path does not touch SQLite. |
| Allocation lines | All **0 B**: resolver, selector (10k), warm macro days HS/College/Pro, warm game day, pitch chain, **warm attended game**, warm geared day. |
| SchemaValidator | **174/174** |
| CoreLoop | **22/22** |
| GrittyEvents | **363/363** |
| Isolation | `origin/p13/integration...HEAD`: 18 files, all §2 paths; the fix round adds no new file. |

**Open:** the PC re-check above (solution build, MC default, resolver byte diff, Schema, CoreLoop, Gritty, and optionally a Godot headless boot). Unchanged from §8.1: D5 values (R1-MATH), the non-top-3 pitcher avatar (owner/13-5), G-4 out-of-position effects.

**Recommended next:** GPT 6 Astra Pro re-checks R1/R2 on `p13/13-2-review-gpt` (re-runs the gates on the PC); then Claude Opus merges 13-2 into `p13/integration` (owner-approved; never `main`). 13-3 does not start before that merge.

### 9.7 Stand-in re-review (Claude Sonnet 5.5, 2026-10-25)

**Verdict: Approve, no blocking findings.** A same-family stand-in for GPT 6 Astra Pro's re-check (TEAM.md §1a): it is **not independent**, so the owner decides whether it unlocks the merge or waits for Astra. Reviewed `b98291b..f5ac6a5` (head `c362dc6`) in worktree `dnd-sonnet-132review`, branch `p13/13-2-rereview-sonnet`. No product code changed in this review and nothing was merged.

**R1 (re-rank after a person refresh): fixed.**
- `RefreshPersonCache` runs before `SelectPitcher` in `PlayGame` (L988 → L1014), so the re-rank lands before the game's selection.
- `BakeSlotRatings` is the only writer of `_pitcherRatings` after Initialize. Stamina is a raw roster byte, not person-baked, so the `StarterSelection.Quality` trigger (stuff + control) is exactly what can move the D2 order.
- `ReRankPitchers` is an allocation-free insertion sort under `CompareQuality`, a total order (player ids are distinct), so it reproduces what `RankPitchers` gives a re-Initialize. Both tables are re-ranked, and two pitchers moving in opposite directions in one refresh is handled by the single pass per team.
- `LeagueSimulator` has no stale-order path: `_pitcherRatings` is written only in `Initialize` (L367) and ranked right after (L424–425). Gear is applied at read time (L742), outside the D2 scalar, and the macro sim has no person ledger. Agree with §9.6: no macro change needed.

**R2 (acknowledge rest rows only after commit): fixed.**
- `FlushDirty` records an in-flight snapshot and keeps the rows dirty. `AcknowledgeFlush` clears only rows whose day still equals what was written (a newer `RecordStart` stays dirty). `AbandonFlush` clears the snapshot only.
- Both callers (`LeagueSimulator.FlushAccumulated`, `MicroGame.FlushGame`) call `AbandonFlush` after `RollbackBatch` in the catch and `AcknowledgeFlush` only after the try/catch, so after `CommitBatch` returned. `FlushDirty`, `AcknowledgeFlush` and `AbandonFlush` have no other callers in `Assets/` or `Tools/` apart from the harness. The upsert is idempotent, so a forgotten acknowledge costs a rewrite, never a lost row.
- Rest now behaves like the stat accumulators beside it: both survive a rollback and are retried.

**Mutation check (done here, not only reported).** With the re-rank disabled and the ledger clearing `_dirty` inside `FlushDirty`, `-- --only starters` fails exactly the six new checks (R1 ×2, R2 ×4; the pre-fix dirty 96 → 0 is visible in the output). The working tree was restored (`git checkout -- Assets`) and `git status` was clean afterwards. The checks are not vacuous.

**Observations (none blocking; for the owner or Opus, no action required for the merge):**
1. **The single in-flight snapshot relies on sequential flushes.** `_inFlight` is one map on a ledger shared by every sim. It is safe because `DatabaseManager` allows one batch at a time and the clock is gated while an attended game runs (`CareerManager._gameInFlight`, `GameManager.InteractiveGameInFlight`); `BaseballDashboard` runs `PlayPendingGame` on `Task.Run`, so that gate matters. If a future slice ever flushes macro days while an attended game is in flight, an acknowledge could clear another flush's snapshot before its commit. Suggest one sentence in `PitcherRestLedger`'s summary stating "callers flush sequentially" (this review changes no code, so it is recorded here only).
2. **Coverage is starter-table-only for R1.** The reliever table goes through the same `ReRankPitchers`, so the risk is low, but no check exercises a reliever re-rank, a rating *increase* promoting a lower-ranked starter, or a stamina tie-break after a refresh. A cheap extra case for the next harness pass, not a blocker.
3. **Rest can now be durable before the stats beside it** (the shared-ledger check shows the micro's retry persisting the macro's rolled-back rest rows while the macro's stats retry later). The `LeagueSimulator` comment says rest is "never staler than the stats beside it"; that is still true (it can only be fresher). No correctness effect.
4. **D5 limitation from §9.4 stands:** the delivered sims count short-rest starts but apply no capacity factor; nonzero values belong to R1-MATH.

**Gates (this PC, head `c362dc6`):**

| Gate | Result |
| :-- | :-- |
| `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** |
| MonteCarloHarness Release (default) | **361/369**, exactly the eight approved D6 reds: `tier-ladder` 10/11 (Pro R/9 4.16), `promotion-sweep` 3/4, `promotion-stream` 0/1, `promotion-avatar` 0/1, `promotion-record` 0/1, `development-arc` 3/4, `practice-seam` 5/6, `development-equilibrium` 0/1 (the retained six-tier promotion guard). No other red. |
| New suites in that run | `starters` **23/23** (17 + the six new checks), `innings` 5/5, `capacity` 6/6, `lineups` 8/8, `worldgen-v14` 23/23, `ui-strings` 3/3 |
| `-- --only resolver` | **13/13**; full output **byte-identical** to the same command in a scratch `git archive d951efe` extraction (both sha256 `6c0155ed…9ef0`, 20 lines) |
| Allocation lines (full run) | **all 0 B**: resolver, selector (10k), warm macro days HS/College/Pro, warm game day, pitch chain, warm attended game, warm geared day |
| SchemaValidator Release | **174/174** |
| CoreLoopHarness Release | **22/22** |
| GrittyEventsHarness Release | **363/363** |
| `git diff --stat origin/p13/integration...HEAD` | 18 files, all within brief §2 allowed paths (sims, `Assets/Core/GameManager.cs`, `Assets/Data/Database/`, the three harnesses, `docs/progress.md`, this brief) |
| Godot 4.7 headless boot | **Not run** (needs the owner's Godot path; optional per the review brief). The fix round touches no boot path; Astra's `00eaa00` boot pass stands. |

These official-toolchain numbers equal §9.6's cloud-session numbers (361/369, the same eight reds, all allocation lines 0 B), so the SQLite stand-in caveat in §9.6 is closed.

**Handoff:** Recommended next: the owner decides whether this stand-in review unlocks the merge (waiver) or waits for GPT 6 Astra Pro's independent re-check; then Claude Opus merges 13-2 into `p13/integration` (owner-approved; never `main`). 13-3 does not start before that merge.

### 9.8 Merge record (Claude Opus 5.5, 2026-10-25)

**Merged** `p13/13-2-rereview-sonnet` @ `85b2b44` into **`p13/integration`** (never `main`) as no-ff merge **`d240b3a`**, from `d951efe`. The merged tree is byte-identical to `85b2b44`, the head Sonnet's §9.7 PC gates ran on, so those results are this merge's gate evidence; this record is docs-only.

**Owner waiver (2026-10-25), this fix round only.** GPT 6 Astra Pro's independent re-check of R1/R2 was blocked by its execution tooling. The owner accepted Claude Sonnet 5.5's same-family stand-in re-review (§9.7) in its place and approved the merge. Basis: Astra independently reviewed all of 13-2 (§9.1–9.5); this round is two small fixes with six mutation-verified checks; every official gate ran on the owner's PC. The waiver does **not** change TEAM.md §1a for any other sim merge.

**Owed:** GPT 6 Astra Pro does a post-merge code read of `f5ac6a5` (R1/R2) when its tools work, recorded as §9.9 on `p13/13-2-review-gpt`; any finding becomes a fix on `p13/integration`.

**Follow-ups carried into 13-3 (from §9.7, none blocking):** a one-line "callers flush sequentially" contract in `PitcherRestLedger`'s summary (obs. 1); `starters` checks for a reliever re-rank, a rating *increase* promoting a starter, and a stamina tie-break after a refresh (obs. 2). Unchanged: D5 values (R1-MATH), the non-top-3 pitcher avatar (owner/13-5), G-4 out-of-position effects. §9.6's threading sentence is corrected above.

**Gates as merged:** build 0/0; MC default 361/369 with exactly the eight approved D6 reds; `--only resolver` 13/13 byte-identical to `d951efe`; all allocation lines 0 B; SchemaValidator 174/174; CoreLoop 22/22; GrittyEvents 363/363; isolation §2 only (§9.7, owner's PC). Godot headless boot not re-run (fix round touches no boot path; Astra's `00eaa00` pass stands).

**Next:** 13-3 by Claude Opus + GPT 6 Astra Pro (Math), from `p13/integration` @ this merge.
