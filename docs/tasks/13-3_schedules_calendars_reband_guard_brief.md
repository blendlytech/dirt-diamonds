# Task Brief: 13-3 — Season schedules and calendars, per-tier re-band, new Pro guard, development gradient

**Status:** **D1–D9 APPROVED by the owner 2026-10-25, all as recommended:** D1 a persisted schedule seed, with the current and next season ensured; D2 the construction and interpretations as written; D3-A in-season locks for every tier plus offseason sliders; D4 results written in 13-3; D5 the pooled R/9 gating, with any floor restatement returning to the owner; D6 the totals plus the ledger hash; D7 the equilibrium gradient moves to 13-6; D8 the §5.3 red list; D9 `main` after 13-6. **Still open:** GPT 6 Astra Pro's check of the Math sections (§3.3, §5.1 band and guard rows, §9). Its answers can refine the M-deliverables but don't reopen D1–D9 without the owner. The build starts only after that check is recorded in §10 and this brief is merged into `p13/integration` (§0).
**Recommended model:** Claude Opus 5.5 (engine + harness) **and** GPT 6 Astra Pro (Math) — TEAM.md §2, 13-3 row
**Reviewer:** each reviews the other's part (TEAM.md §1a): GPT 6 Astra Pro independently reviews Opus's sim diff; Claude Opus reviews Astra's Math record. Opus stays the merge gate. The owner's 2026-10-25 independence waiver (13-2 brief §9.8) covered the 13-2 fix round only and does **not** apply here.
**Owner gate:** D1–D9 now; **sanction of the new Pro guard** at the end (TEAM.md §2).
**Spec:** [league_restructure.md](../design/league_restructure.md) §3, §3.1, §3.2, §4, §6, §11, §12 (`Schedule_Games`), §13, §15 (13-3 row), §16-I2/I6/I7; [math/college_28_game.md](../design/math/college_28_game.md) (CS-M) §1, §6; [development_decline_curves.md](../reference/development_decline_curves.md) §4 (the Practice lever); 13-2 brief §5.3, §8.1, §9.7, §9.8
**Branches / worktrees:** build `p13/13-3-opus`; Math `p13/13-3-math-gpt`. Both start from **`p13/integration`** (at or after `fdbe17e` plus this brief's merge), each in its own worktree, and merge into `p13/integration`, **never `main`**.

## 0. Preconditions (check first; stop and report if any fails)

1. 13-2 is merged into `p13/integration` (`d240b3a`, record `fdbe17e`). ✔ at drafting.
2. This brief is approved (D1–D9), its Math sections are checked by Astra, and it's merged into `p13/integration`.
3. Astra's owed post-merge read of `f5ac6a5` (13-2 brief §9.8 → §9.9) is recorded, or explicitly still owed. Any finding is fixed on `p13/integration` before 13-3 branches.
4. **Astra can run the harnesses on the owner's PC.** Its 13-2 re-check was blocked by tooling. Without working tools there's no Math phase and no independent review. Stop and report; no waiver is implied.
5. Run `git fetch --all`, `git status` and `git worktree list`, and confirm that no one else holds `p13/13-3-opus` or `p13/13-3-math-gpt`.
6. **Baseline on the base before any edit** (13-2 §9.7/§9.8, owner's PC): build 0/0; MC default **361/369** with exactly the eight approved D6 reds (`tier-ladder` 10/11, `promotion-sweep` 3/4, `promotion-stream` 0/1, `promotion-avatar` 0/1, `promotion-record` 0/1, `development-arc` 3/4, `practice-seam` 5/6, `development-equilibrium` 0/1); `--only resolver` 13/13 byte-identical to `d951efe`; every allocation line 0 B; SchemaValidator 174/174; CoreLoop 22/22; GrittyEvents 363/363. Also record the **default MC wall time** on the owner's PC: it's the baseline for CS-M §6's ≤ 120 s budget. Record any difference.

## 1. Context

13-2 made both sims run the v14 roster shapes, but on an interim calendar. Every tier plays one circle-method round per day for 154 days (HS 924 games, College 1,848, Pro 2,464 a season), HS still uses the shipped 26-game Tue/Fri `HsSeasonCalendar`, and the old season guard is unreachable. 13-3 gives each tier its real season (spec §3): seeded, persisted schedules in `Schedule_Games` that both sims and the career driver read, and one `TierSeasonCalendar` for all three tiers. On those calendars it re-bands the tiers, retires the old season guard, records the new Pro guard (§11.3), and retunes development (the practice lever and the tier gradient). It's the last slice that changes the world's shape, and the arcade's G-3 calibration takes its per-tier bands from here (BUILD_PLAN). For the player ([GAME_VISION.md](../GAME_VISION.md)), it's the rhythm of the climb: 1 game a week in HS, 2 in College, 3 in Pro.

### 1.1 Code facts this brief relies on (verified at `fdbe17e`)

| Fact | Where |
| :-- | :-- |
| Interim window `RegularSeasonDays = 154`; offseason guard; HS sparse-day seam; Sunday flush (`DayOfSeason % 7`); pairings from `LeagueSchedule` | `LeagueSimulator.cs:32`, `:501`, `:509`, `:521`, `:574` |
| `LeagueSchedule`: circle method for any even N, guarded by `RegularSeasonDays` | `LeagueSchedule.cs:31–84` (`:61`) |
| `HsSeasonCalendar`: Mar 1–May 31, Tue/Fri, 26 games, practice Mon/Wed/Thu; `TryGetLeagueScheduleDay` is the shared seam (identity for College/Pro) | `HsSeasonCalendar.cs:27–30`, `:57`, `:87–95`, `:107–115` |
| Career driver claims the avatar's game from the same math; the UI look-ahead `TryGetScheduledGameFor` is pure math (BaseballDashboard `:382/:435`, CalendarScreen `:20`) | `CareerManager.cs:1417–1440`, `:1465–1488` |
| Planner forced blocks: **HS only, locked all year** (practice 0 on off days); College/Pro keep free sliders | `GameManager.cs:1081–1094` |
| In-game world uses a **wall-clock seed**; sims fork it; **no persisted world seed** | `GameManager.cs:371`, `:400`, `:445–461` |
| **Fixed sim calendar:** 365 days, no leap years, day-of-season 1 = Monday every season | `GameCalendar.cs:35–43`, `:79–80` |
| `Schedule_Games` + `ix_sched_day` exist in v14 with **no writer**; `Team_Seasons` is 13-4's; SchemaValidator already checks columns, index, CHECKs, FKs | `SchemaDefinitions.sql:583–633`; `SchemaValidator/Program.cs:542–652` |
| **`PromotionManager` throws on any non-six-tier world**, so a live career stops at its first season rollover until 13-6 | `PromotionManager.cs:459–466` |
| Practice lever: `PracticeFracCap = 0.25`, `PracticeFracPerHour = 0.0005` (e-fold 500 h), saturating conversion | `DevelopmentManager.cs:90`, `:100`, `:205–209` |
| Season pipeline: 730 days; game counts from `RegularSeasonDays` (first season 153 days, the "M1 artifact"); runs gated as **ER / GS** ("R/G 4.2–4.8") | MC `Program.cs:442–534`, `:496–500`, `:553`, `:566` |
| Tier ladder: bands (HS / College / Pro = the MLB row) gated as **R/9 = 27·ER / outs**; Pro world-vs-solo identity check (not a frozen number) | MC `Program.cs:3962–3967`, `:4055`, `:4107` |
| 41 harness references to `RegularSeasonDays`/`DaysPerSeason`, e.g. career-wiring's "avatar PA 450–800" (`:1376`), 3-generation counts (`:2921`), HS `GamesPerSeason` (`:5259`), `starters` parity on `LeagueSchedule` (`:7549`), `innings` (`:8031`) | MC `Program.cs` |
| 13-2 carry-forward sites: the ledger summary; `ReRankPitchers` (both tables); the R1 checks | `PitcherRestLedger.cs:5–20`; `MicroGame.cs:738–739`, `:752`; MC `:7563–7710` |
| The MC skill still records the old guard and the 154-game pipeline | `.agents/skills/run_monte_carlo_batch/SKILL.md:13`, §3 |

### 1.2 The calendar is the same every season

Because day 1 is always a Monday, every rule in §3 resolves to fixed days-of-season. (CS-0's 400-year Gregorian sweep was a real-world sanity check; the sim calendar never varies.)

| Tier | Opens | Last regular game | Game days | Per team | League games | Per game day | Interim games |
| :-- | :-- | :-- | :-- | :-- | :-- | :-- | :-- |
| HS | Fri Mar 2 (day 61) | Fri May 4 (day 124) | 10 Fridays | 10 | 60 | 6 | 924 |
| College | Wed Feb 7 (day 38) | Sat May 12 (day 132) | 28 (Wed + Sat × 14) | 28 | 336 | 12 | 1,848 |
| Pro | Tue Apr 3 (day 93) | Tue Aug 28 (day 240, D2) | 64 (Tue/Thu/Sat × 21 + week 22 Tue) | 64 | 1,024 | 16 | 2,464 |

The Draft falls on Sat Jul 14 (day 195). The College postseason ends by day 174 at the latest (Jun 23), and the Pro postseason starts in September (day ≥ 244). Sundays are the days with day ≡ 0 (mod 7), and none of them is a game day. **Exposure:** one real season has **6.5% (HS), 18% (College) and 42% (Pro)** of an interim season's games. Every check that gates a rate or a count on one season has to be resized (§3.3 M1, §5.4).

## 2. Hard constraints

- **Allowed paths (Opus build):**
  - `Assets/Simulation/Baseball/`: new `TierSeasonCalendar.cs`, `SeasonScheduler.cs`, and `SeasonSchedule.cs` (the per-tier in-memory table).
  - In the same folder: `LeagueSimulator.cs`, `MicroGame.cs` (D4 results only), `CareerManager.cs`, `PitcherRestLedger.cs` (summary only), and `DevelopmentManager.cs` (Astra's M4 values in `DevelopmentProfile` only). `PromotionManager.cs` only for compile and comment edits.
  - Delete `HsSeasonCalendar.cs` and `LeagueSchedule.cs`.
  - `Assets/Data/Database/`: new `ScheduleQueries.cs`, plus the `GameStateQueries` key for D1.
  - `Assets/Core/GameManager.cs` (wiring and D3 blocks).
  - `Assets/UI/CalendarScreen.cs` and `ScheduleScreen.cs`: **comment-only** cref and wording fixes.
  - `Tools/MonteCarloHarness/`, `Tools/CoreLoopHarness/`, `Tools/SchemaValidator/` (query checks only).
  - `.agents/skills/run_monte_carlo_batch/SKILL.md`: guard retirement and record, and the 154-game and 9+5+3 text.
  - `docs/progress.md` (one line) and this brief's §8.1.
- **Allowed paths (Astra Math):** new `docs/design/math/13-3_bands_guard_development.md` (the Math record, like CS-M), this brief's §10, and `docs/progress.md` (one line). Scratch runs and scripts stay untracked or outside the repo. **No `Assets/` or `Tools/` edits on the Math branch:** Math values reach code through Opus's build commit, and Astra's review verifies them.
- **Not allowed:**
  - `AtBatResolver.cs`, `BaseOutMatrices.cs`, `BaseOutAdvancement.cs`, `PitcherFatigue.cs`, `EffectiveRatings`, `TierEffects.cs` (tier deltas).
  - `StarterSelection`'s rule.
  - `Assets/Simulation/Arcade/`, `Assets/Simulation/Life/`.
  - UI behavior.
  - Any `docs/design/` spec. Errors go in the handoff.
  - `GEMINI.md`, and anything in `.agents/` other than the one skill file.
  - Any DDL change. `Schedule_Games` is used as v14 defines it. If the build needs DDL, stop and report.
- **Calibration contract.**
  - The resolver guard (§5 fixtures + §8 batch) stays byte-identical.
  - No probability, weight, tier delta, fatigue or advancement value changes. The only constant edits are Astra's signed D7/M4 development values, which act only in the offseason pass, never inside a game.
  - The band rows stay the spec's numeric rows (§11.2, CS-M §1) unless D5 ends in an owner-approved restatement.
  - **Guards:** the old guard retires by name. The new Pro guard is recorded **once**, after D5 and D6 settle, on a run Astra reproduces, with the owner's sanction.
- **Zero allocation.**
  - Each tier's season table is pre-sized by capacity at `Initialize` and loaded once per season year.
  - The warm game-day schedule lookup, the warm macro day and the warm attended game all allocate 0 B.
- **Determinism.**
  - Schedule generation draws only from its own derived stream (D1), never from a sim's `RngState`, so adding it shifts no other stream.
  - Same seed → identical rows, ids and order. A reload never changes a schedule.
- **One schedule, two readers.** The tier sim and `CareerManager` read the same in-memory table loaded from `Schedule_Games`, so the macro sim's attended-team skip and the pending game come from the same row.
- **Rollover independence.** Generation never depends on `SeasonRolledOverEvent` handler order, because the live rollover throws in `PromotionManager` until 13-6 (§1.1).
- **GEMINI §1, §4:**
  - No Life references.
  - Presentation stays read-only.
  - One transaction per batch, with parameterized, cached queries.
  - No Blind Queries: validate every new query on a scratch v14 copy first.

## 3. Inputs

### 3.1 Spec decisions to implement as written

- **`TierSeasonCalendar` replaces `HsSeasonCalendar`** for all three tiers (§3, §3.2, §13), with the game days, practice days and windows of §1.2. No game is ever on a Sunday.
- **`SeasonScheduler.Generate(tier, seasonYear, rng)`** writes `Schedule_Games` rows (phase 0, status 0; §3.1, §12). `ScheduleQueries` reads them, and `LeagueSchedule` is retired (§13).
- **Pairings (§3.1):**
  - HS: a double round-robin inside each 6-team conference.
  - College: division × 4 (2 home / 2 away), the other 8 conference teams × 2 (1/1), no cross-conference games, 12 games per game day.
  - Pro: division × 8, other conference teams × 2, the other conference's teams × 1, 32/32 home/away, 16 games per game day.
  - Every team plays exactly once per game day.
- **Bands (§11.2, CS-M §1):** the HS, College and Pro rows verbatim, with runs per 9 innings (R/9). HS R/9 is 6.4–8.0. College keeps its rate rows verbatim and its runs band 5.8–7.1 as R/9, with legacy ER/GS reported on the same sample.
- **Guards (§11.3):** the resolver guard stays byte-exact; retire PA 48384 / H 10969 / ER 5237; record a new Pro guard.
- **Development (§11.3, §3.2):** the gradient is HS < College < Pro, and the parked practice-lever retune is folded in.

### 3.2 Owner decisions (recommendations marked)

- **D1. Schedule seed and generation timing.**
  - **Recommended:** a world `schedule_seed` written once to `Game_State` at world-gen. It's drawn from the boot stream *after* the existing forks, so no current stream moves; harnesses pass their own seed. Each (season year, tier) schedule uses a stream derived from (`schedule_seed`, year, tier) by a stable mixer.
  - Generation is an idempotent *ensure*: at boot and whenever a new season year begins, the **current and the next** season exist (one transaction per ensure), and rows are never regenerated. So the calendar UI can look across New Year, a reload never changes a schedule, and nothing waits on rollover handlers. This interprets §3.1's "runs at season rollover".
  - *Alternative:* generate inside `SeasonRolledOverEvent` from a fresh wall-clock draw. Then a schedule depends on reload timing, and generation sits behind the `PromotionManager` throw.
- **D2. Pairing construction and two interpretations.**
  - HS "home/away alternates" means the two meetings of each pairing are at opposite venues (5 home / 5 away per team). Strict week-to-week alternation isn't attempted: a 6-team round-robin can't give every team one.
  - Pro week 22's single game is on the **Tuesday** (day 240).
  - **Recommended construction** (Opus's design, Astra reviews), with rounds built so every balance holds by construction:
    - HS: K6 circle rounds, with the second leg reversed.
    - College: the §3.1 factorization.
    - Pro division games: K4 rounds × 8, alternating venue.
    - Pro cross-division games: a division-pair matching × a Latin shift, 3 × 4 = 12 rounds, played twice with the second copy reversed.
    - Pro interconference games: East *i* vs West (*i* + *s*) mod 16 for *s* = 0…15; East hosts when *s* < 8, which gives each team 8 home and 8 away.
    - Then a seeded permutation of rounds onto game days per conference. For Pro, the permutation also picks which 16 of the 64 days are interconference.
  - No soft constraints (home-stand length, rematch spacing) in 13-3.
  - *Alternative:* soft constraints via seeded search. More realistic, but harder to prove; later, if wanted.
- **D3. Planner blocks for every tier.** Today only HS locks its Practice and Game blocks, all year round.
  - **Recommended (A):** each tier locks team Practice and Game blocks from `TierSeasonCalendar` **inside its season window** (from the Monday of week 1 to the last regular-season game day). Outside it, **every tier gets the free sliders back**, so the offseason training of §9.3 ("signed prospect offseason") exists. Hours are data values, defaulting to HS's 2 h practice and 3 h game for all tiers.
  - This changes shipped HS behavior: an HS player could not practice in the offseason until now.
  - *B:* the shipped mechanics literally, locked all year for every tier (no offseason practice for anyone). *C:* HS as shipped, College/Pro locked in-season only.
  - Postseason blocks for teams still alive are 13-4's.
- **D4. Results in `Schedule_Games`.**
  - **Recommended:** 13-3 writes `status = 1` and both scores for every played game: the macro sim in its 7-day flush batch, the attended game in the micro flush. Results are acknowledged only after commit (13-2 R2 pattern; the "callers flush sequentially" contract covers them too).
  - This gives 13-4's standings and ranking their source, and the schedule screen real results. Forfeits (`status = 2`) are 13-5's, and `Team_Seasons` is 13-4's.
  - *Alternative:* rows stay at status 0 until 13-4.
- **D5. Band gating and the Pro run floor.** Astra does the work (M1); the owner approves the shape.
  - **Recommended:** keep the numeric rows. Gate **R/9**, and report **ER/GS** beside it on the same sample. Gate each tier's band on a **pooled, fixed-seed sample of real-calendar seasons**, sized per tier by Astra (HS needs many more seasons than Pro) within CS-M's ≤ 120 s default budget. CS-M's opt-in batch is the calibration evidence.
  - If the pooled Pro R/9 still sits below 4.2, **13-3 doesn't touch the sim.** Astra brings the owner a choice between (i) restating the Pro run floor, with the R/G → R/9 denominator rationale, and (ii) re-centring runs through `BaseOutAdvancement`'s two knobs in a separately sanctioned change. The guard waits for that answer.
  - *Alternative:* gate one season per tier as today. On an HS season of 60 games, that's mostly noise.
- **D6. The new Pro guard.**
  - **Recommended:** the season-pipeline world (flat spread 0, `LeagueSeed`, `SeasonSeed`, fixed schedule seed), first season (`StartYear`, 1,024 games). **PA, H and ER** are frozen as literals in the harness, and a SHA-256 of the full stat ledger (every Batting/Pitching_Stats row of both seasons, canonical order) is recorded beside them.
  - The guard is recorded after D5, on a run Astra reproduces 20/20 byte-identical on the owner's PC, and the owner sanctions it.
  - It also freezes the Pro scheduler: any later scheduler change moves it. 13-4's postseason must not move it (phase 0 only). 13-5's designations must keep it, or bring their own sanction.
  - *Alternative:* the three totals only (the spec's minimum).
- **D7. Development gradient scope.** §11.3's check is a time-averaged *equilibrium*: players must flow between tiers, and only 13-6's `PipelineManager` does that (`PromotionManager` throws on v14).
  - **Recommended:** 13-3 delivers Astra's gradient targets and the practice-lever retune, fed by the D3 calendars. It also adds the checks that don't need a pipeline: practice credit per tier calendar, and one development pass on a generated world keeping mean talent HS < College < Pro. **The equilibrium check moves to 13-6's PipelineHarness** (20 seasons).
  - *Alternative:* a throwaway tier-flow stub in 13-3, which 13-6 deletes.
- **D8. The §5.3 expected-red list.**
- **D9. When `p13/integration` reaches `main`.** §15 says when the full MC suite is green with the new Pro guard, but the seven promotion suites stay red until 13-6.
  - **Recommended:** merge to `main` after 13-6, so `main` stays green as §15 intends. Phase G and L keep branching from `p13/integration`.
  - *Alternative:* merge after 13-3, with the seven reds owner-approved on `main`.

### 3.3 The split: Math deliverables (GPT 6 Astra Pro) vs the build (Claude Opus)

| # | Deliverable | Owner | Acceptance |
| :-- | :-- | :-- | :-- |
| B1–B7 | Calendar, scheduler, schedule-driven sims and career driver, D3 blocks, D4 results, harness and skill (§4.1) | **Opus** | §5.1 green; reviewed by Astra (M5) |
| **M1** | **Band sample design and the run-floor verdict (D5):** pooled seeds/seasons per tier, with a target standard error per band; the R/9-vs-ER/GS seam measured on the same sample; pooled measurements on Opus's build; a verdict on Pro R/9 4.16 vs the 4.2 floor | **Astra** | Recorded in the Math doc with seeds, sample, intervals and runtime; reviewed by Opus |
| **M2** | **College re-band per CS-M:** confirm the §1 rows; acceptance for `RunCollegeRateSmoke` (2 checks); run the opt-in rates batch (16,384 seasons), or, if a timed pilot says that's infeasible, a reviewed reduced plan *before* running | **Astra** | CS-M §1/§6: pooled rates in band, clustered 95% intervals |
| **M3** | **New Pro guard sanction (D6):** reproduce Opus's candidate 20/20 byte-identical on the owner's PC (timing lines normalized); sign the values | **Astra** (owner sanctions) | Values and hash recorded in §10 and the skill |
| **M4** | **Development values (D7):** gradient targets and their tolerance; practice-lever constants (`PracticeFracPerHour`, `PracticeFracCap`, or per-tier if needed) for the §1.2/D3 hours; new goldens for `practice-seam` §10.5 | **Astra** | Values in the Math doc; Opus applies them as data edits |
| **M5** | Independent review of Opus's sim diff | **Astra** | 13-2 §9-style record |

**Arithmetic for M4 (Opus's arithmetic, not a target).** In-season team practice at 2 h, under D3-A windows:
- HS: Mon–Thu × 10 weeks = 40 days = 80 h.
- College: Mon/Tue/Thu/Fri × 14 weeks = 56 days = 112 h.
- Pro: Mon/Wed/Fri over weeks 1–21, plus the Monday of week 22 = 64 days = 128 h.

At today's constants that's a practice fraction of **.037 / .050 / .057**, against **.2469** for the shipped proof's 6 h a day all year. The lever is close to inert in-season. What the offseason sliders add depends on D3.

**Sequence.**
1. Opus builds B1–B7 with today's constants; the guard literal is left pending.
2. Astra measures M1, M2 and M4 on that head, on `p13/13-3-math-gpt`.
3. Opus applies the M-values and records the guard candidate.
4. Astra runs M3 and M5. Opus reviews the Math record.
5. The owner sanctions the guard.
6. Opus merges into `p13/integration`.

## 4. Outputs

### 4.1 Opus (`p13/13-3-opus`)

1. **`TierSeasonCalendar`**, in place of `HsSeasonCalendar`:
   - per tier: game day and game ordinal, practice day, the regular-season window, the postseason window start (for 13-4) and the Draft day;
   - the D3 hours;
   - static precomputed tables, 0 B lookups.
2. **`SeasonScheduler`** (pure, the D2 construction) and **`ScheduleQueries`**:
   - ensure a season in one transaction;
   - load a tier-season into a pre-sized table;
   - write results (D4).
   
   Also the D1 seed key.
3. **`LeagueSimulator`:**
   - game days come from the tier's table, loaded when the season year changes;
   - the offseason guard comes from the calendar, and `RegularSeasonDays` is retired;
   - the Sunday flush is unchanged;
   - the attended-team skip uses the same row;
   - results accumulate and flush (D4).
4. **`CareerManager`:** the pending game and `TryGetScheduledGameFor` read the tier sim's table. The signatures don't change, so the UI doesn't either.
5. **`MicroGame`:** the attended game's result is written in its flush and acknowledged after commit (D4).
6. **`GameManager`:** `GetMandatoryBlocksFor` per D3; the schedule ensure at boot (D1); wiring.
7. **`DevelopmentManager`:** Astra's M4 values, as data edits only.
8. **13-2 carry-forwards:**
   - one sentence in `PitcherRestLedger`'s summary: callers flush sequentially. One in-flight snapshot is safe because `DatabaseManager` refuses nested batches and the clock is gated while an attended game runs (`CareerManager._gameInFlight`, `GameManager.InteractiveGameInFlight`);
   - three `starters` checks (§5.1).
9. **Harness** (§5) and **`SKILL.md`**: the old guard retired by name, the new guard recorded, and the 154-game and 9+5+3 descriptions rewritten.
10. **Handoff** as §8.1.

### 4.2 Astra (`p13/13-3-math-gpt`)

`docs/design/math/13-3_bands_guard_development.md` (M1–M4, with evidence and the runtime measured on the owner's PC), §10 of this brief (summary, M3 sanction, M5 review), and one `progress.md` line.

## 5. Tests and gates

### 5.1 Must pass

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings, 0 errors |
| Schema | `dotnet run --project Tools/SchemaValidator -c Release` | 174/174, plus any `ScheduleQueries` checks |
| Resolver | `MonteCarloHarness -- --only resolver` | 13/13, byte-identical to `d951efe` |
| New `calendar` suite | `-- --only calendar` | §1.2 exactly per tier: opening, last game, game-day count and weekdays, Draft day 195, no Sunday; practice days and windows; D3 blocks in-season and outside the window; 0 B lookups |
| New `schedule` suite | `-- --only schedule` | **Per tier:** 60 / 336 / 1,024 games; 10 / 28 / 64 per team; 5/5, 14/14, 32/32 home/away; the §3.1 pairing counts and venues (HS 2 × each conference opponent at opposite venues; College division × 4 at 2/2, conference × 2 at 1/1, zero cross-conference; Pro × 8 at 4/4, × 2 at 1/1, × 1 interconference at 8/8); each team exactly once per game day; game days are calendar days only. **Determinism:** same seed → identical rows incl. `game_id`; another year → another order, same invariants; ensure is idempotent; reload is identical; current + next season present (D1). Covers CS-M's `RunCollegeScheduleSuite` checks 1–10 (fixtures: years 2026/2028 × scheduler seeds 1/2) |
| `season-pipeline` | default run | Two full Pro seasons of 1,024 games (GS = 2 × games, W = L = games; the "153-day" first season is gone, because no tier plays day 1); ledgers agree; R/9 gated per D5 and ER/GS printed; warm real game day 0 B; **new Pro guard literals + ledger hash** (after M3) |
| `tier-ladder` | default run | Bands on the D5 sample, R/9 + ER/GS; monotone HS > College > Pro on AVG and R/9; Pro world-vs-solo identity; **10/11 → all green** |
| `college-rate-smoke` (CS-M) | default run | 2/2 |
| `starters` | `-- --only starters` | 23 + **3 new** = 26/26: after a person refresh (a) a reliever re-rank, (b) a rating *increase* that promotes a lower-ranked starter, and (c) a stamina tie-break each match a fresh re-Initialize on pitcher ids **and** stat attribution. Each new check fails with `ReRankPitchers` disabled (mutation recorded) |
| Results (D4) | `-- --only schedule` | Every played row is final, with scores equal to the box-score runs; rollback → retry lands each result once; the attended game's row is final; W/L from rows equals W/L from `Pitching_Stats` |
| Development (D7) | `-- --only practice-seam,development-curve` | Non-promotion checks green with the M4 goldens; new per-tier practice-credit fixture; single-pass gradient check |
| Unchanged | `-- --only innings,capacity,lineups,worldgen-v14,ui-strings` | 5/5, 6/6, 8/8, 23/23, 3/3 (world-gen is untouched: schedule rows are written outside `LeagueGenerator`) |
| CoreLoop, Gritty | their harnesses | 22/22 (+ calendar checks if added), 363/363 |
| Replay | default MC × 20 on the owner's PC | Byte-identical apart from timing lines (CS-M §6) |
| Runtime | default MC | Wall time reported against §0's baseline and the ≤ 120 s budget |
| Opt-in | `-- --college-analysis rates` | Runs **only** that batch (M2), never the default suite |
| Godot boot | headless fresh v14 boot, scratch APPDATA | Current- and next-season rows for every tier (60 / 336 / 1,024 each); no errors |
| Isolation | `git diff --stat origin/p13/integration...HEAD` | §2 paths only |

### 5.2 Must not move

The resolver guard. 13-2's rest and selection behavior on the same day sequence (its `starters` fixtures are unchanged). Every 0 B line.

### 5.3 Expected red after 13-3 (owner approval needed, D8; the handoff reports the actual list)

- **The seven promotion suites** stay red until 13-6: `promotion-sweep`, `promotion-stream`, `promotion-avatar`, `promotion-record`, `development-arc`, `practice-seam`'s promotion check, and `development-equilibrium` (under D7, the gradient equilibrium moves to the PipelineHarness).
- **A live career can't cross a season rollover** (the `PromotionManager` throw) until 13-6. The Godot gate stops before day 365.
- **Green again:** `tier-ladder`. Everything else passes or carries an explained, owner-visible reason.

### 5.4 Restating checks (never deleting)

Checks whose expected values come from the 154-day season are restated from `TierSeasonCalendar`:
- `AssertSeason`'s game counts;
- career-wiring's "avatar PA 450–800" (a 64-game Pro season gives about a third of the PA), as a per-tier band Astra signs;
- the 3-generation season counts;
- HS `GamesPerSeason` in `promotion-avatar`;
- the `starters` and `innings` day loops.

Each is listed in the handoff as old → new → reason. Same-seed delta checks (rivalry, person-effects ΔR/G ≤ .05) see smaller samples. If one fails, it's reported with Astra's view, never widened silently. Retire by name with the replacement (13-1 rule).

## 6. Definition of done

- [ ] §0 recorded, including the baseline and its wall time.
- [ ] `TierSeasonCalendar`, `SeasonScheduler`, `ScheduleQueries` and `SeasonSchedule` built per D1–D4; `HsSeasonCalendar`, `LeagueSchedule` and `RegularSeasonDays` gone.
- [ ] Both sims and the career driver play only from persisted rows; D3 blocks; D4 results.
- [ ] 13-2 carry-forwards done (the ledger sentence + 3 mutation-verified checks).
- [ ] Astra's M1–M4 recorded and reviewed by Opus; M-values applied; D5 settled.
- [ ] The old guard retired by name; the new Pro guard recorded, reproduced 20/20 by Astra (M3) and **sanctioned by the owner**.
- [ ] §5.1 green; §5.2 unchanged; §5.3 actual list; §5.4 restatements listed.
- [ ] M5 independent review passed.
- [ ] Committed and pushed on both branches. Not merged.
- [ ] Handoff + one `progress.md` line per author. **Then stop.**

## 7. Out of scope

- Playoffs, the play-in, series, the Red Dirt Series, `PlayoffManager`, `RankingCalculator`, `Team_Seasons`, postseason rows and postseason blocks (**13-4**).
- The Sunday lock, forfeits (`status = 2`), retiring the replacement call-up (**13-5**).
- `PipelineManager`, the rollover throw, the equilibrium gradient (**13-6**, D7).
- Real values for the D5 workload knobs, the R1-MATH fatigue fix, CS-M's `RunCollegeWorkloadSuite` (**R1-MATH**).
- Schedule and calendar UI beyond comment fixes (**13-7**); league and brand names.
- Any resolver, tier-delta or advancement change (D5 (ii) would be its own sanctioned change).
- `docs/design/` edits, merging, `main`.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built>  · Base: p13/integration @ <sha>  · Commits: opus <sha>, math <sha>
Gates: build → <w/e>; Schema → <p/t>; resolver → <byte-exact?>; calendar → <p/t>; schedule → <p/t>;
       season-pipeline → <p/t, R/9, ER/GS>; tier-ladder → <p/t>; college-rate-smoke → <p/t>; starters → <p/t>;
       development → <p/t>; innings/capacity/lineups/worldgen/ui-strings → <p/t>; CoreLoop → <p/t>; Gritty → <p/t>;
       replay 20/20 → <identical?>; MC wall time → <s vs baseline>; Godot boot → <ok?>; isolation → <§2 only?>
MC default: <p/t>; red (actual): <suite → p/t, reason>; restated: <name → old → new → reason>
New Pro guard: PA <n> / H <n> / ER <n>, ledger <sha256>, reproduced <20/20> by Astra, owner sanction <date>
Decisions used: D1–D9 <as approved / changed and why>; Math: M1–M4 <values, doc commit>
Open: <spec errors, risks, owner questions>
Recommended next: 13-4 playoffs + ranking by Claude Opus (reviewer GPT 6 Astra Pro)
```

Then add one compact line to [progress.md](../progress.md). Don't start 13-4.

## 9. Questions for Astra's Math check of this brief (answer before owner approval)

1. **Sample sizes (D5/M1).** What pooled sample per tier gives a usable standard error inside ≤ 120 s? One HS season has 120 team-games. If the per-team-game run SD is about 3.5 (an assumption, not a measurement), one HS season's R/9 SE is about 0.32 against a 1.6-wide band.
2. **The Pro floor.** Where is the Pro run environment's center? Interim Pro-only R/G was 4.23/4.21 (ER/GS); one interim season gave R/9 4.16; the archived 8-team MLB value was 4.28. Was the MLB §8 row's 4.2–4.8 ever an R/9 band? R/9 ≥ ER/GS whenever fewer than 27 outs are recorded per team-game.
3. **The opt-in rates batch (M2).** Is 16,384 seasons / 5.5M games feasible? Specify the timed pilot and the stop rule.
4. **The guard's shape (D6).** Three totals vs totals + ledger hash; season 1 vs both; a flat world vs spread 25. Does the guard survive 13-5's designations?
5. **The practice retune (M4).** What should full in-season team practice be worth per tier (the §3.3 hours)? What's the offseason sliders' share under D3-A vs B?
6. **The gradient (D7/M4).** What target and tolerance should the single-pass check use, and what exactly moves to 13-6's PipelineHarness?
7. **Restated checks (§5.4).** Per-tier avatar PA bands for career-wiring. Does any same-seed delta check need a larger sample rather than a looser bound?

## 10. Math record and review (GPT 6 Astra Pro)

*(Empty until Astra's phase.)*

---

### Spec issues found while drafting (for the owner; not edited, per the brief rules)

1. **§11.3 / §15:** the new guard is recorded "on the first fully green run", and `p13/integration` merges when the "full MC suite is green" after 13-3. Neither can happen before 13-6, because the promotion suites stay red until then (D9).
2. **§11.3:** the development gradient is "retuned per slice 13-3", but its equilibrium needs 13-6's pipeline (D7).
3. **§3.1:** HS "Home/away alternates" is ambiguous, and the day of Pro week 22's single game isn't specified (D2). §3.1 also says generation "runs at season rollover", which D1 interprets.
4. **§3.2:** "keep their shipped mechanics" (HS locked all year) conflicts with §9.3's offseason training once extended to every tier (D3).
5. **§11.2:** "reuse the MLB row verbatim … per 9 innings". The shipped MLB run band was gated as ER/GS, not R/9 (CS-M §1's seam; D5).
6. **`college_28_game.md`:** the header still says "edited-section re-check pending" (done, CS-M brief §9.1). §2, §4 and §6 keep the rest-4 fixtures (14/14/0/0, gap 3 ineligible), superseded by spec §6 (rest 3). §6's "Frozen guards" still names the old guard as the reference.
7. **§4** cites `LeagueSimulator.cs ~L518–530` for innings; those lines moved in 13-2 (cosmetic).
