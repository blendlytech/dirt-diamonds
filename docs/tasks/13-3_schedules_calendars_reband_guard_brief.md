# Task Brief: 13-3 — Season schedules and calendars, per-tier re-band, new Pro guard, development gradient

**Status:** **D1–D9 APPROVED by the owner 2026-10-25, all as recommended:** D1 a persisted schedule seed, with the current and next season ensured; D2 the construction and interpretations as written; D3-A in-season locks for every tier plus offseason sliders; D4 results written in 13-3; D5 the pooled R/9 gating, with any floor restatement returning to the owner; D6 the totals plus the ledger hash; D7 the equilibrium gradient moves to 13-6; D8 the §5.3 red list; D9 `main` after 13-6. **D10** (the runtime budget, added after Astra's measurement) is **pending owner**; it gates only the pooled checks, not the build's start. **Math check done** (§10.1–10.3, changes requested), and Opus reconciled the brief (§10.4). Its answers refine the M-deliverables and don't reopen D1–D9. **The build can start** once this brief reaches the build branch (§0).
**Recommended model:** Claude Opus 5.5 (engine + harness) **and** GPT 6 Astra Pro (Math) — TEAM.md §2, 13-3 row
**Reviewer:** each reviews the other's part (TEAM.md §1a): GPT 6 Astra Pro independently reviews Opus's sim diff; Claude Opus reviews Astra's Math record. Opus stays the merge gate. The owner's 2026-10-25 independence waiver (13-2 brief §9.8) covered the 13-2 fix round only and does **not** apply here.
**Owner gate:** D1–D9 now; **sanction of the new Pro guard** at the end (TEAM.md §2).
**Spec:** [league_restructure.md](../design/league_restructure.md) §3, §3.1, §3.2, §4, §6, §11, §12 (`Schedule_Games`), §13, §15 (13-3 row), §16-I2/I6/I7; [math/college_28_game.md](../design/math/college_28_game.md) (CS-M) §1, §6; [development_decline_curves.md](../reference/development_decline_curves.md) §4 (the Practice lever); 13-2 brief §5.3, §8.1, §9.7, §9.8
**Branches / worktrees:** build `p13/13-3-opus`; Math `p13/13-3-math-gpt`. Both start from **`p13/integration`** (at or after `fdbe17e` plus this brief's merge), each in its own worktree, and merge into `p13/integration`, **never `main`**.

## 0. Preconditions (check first; stop and report if any fails)

1. 13-2 is merged into `p13/integration` (`d240b3a`, record `fdbe17e`). ✔ at drafting.
2. This brief is approved (D1–D9), its Math sections are checked by Astra (§10), and it's on the build branch. ✔ 2026-10-25, apart from D10 (pending owner, needed before the pooled checks are installed). Like 13-2's brief, it can reach `p13/integration` with the build, or earlier as a docs merge; Astra's §9.9 13-2 record (`cfb3c09` on `p13/13-2-review-gpt`) merges into `p13/integration` along with it.
3. Astra's post-merge read of `f5ac6a5` (13-2 brief §9.9) is recorded. ✔ `cfb3c09`: accept, no blocking findings.
4. **Astra can run the harnesses on the owner's PC.** ✔ 2026-10-25 (§9.9 and §10.1; it needs the `.git` write escalation from its worktree). If that breaks again, stop and report; no waiver is implied.
5. Run `git fetch --all`, `git status` and `git worktree list`, and confirm that no one else holds `p13/13-3-opus` or `p13/13-3-math-gpt`.
6. **Baseline on the base before any edit** (13-2 §9.7/§9.8, owner's PC): build 0/0; MC default **361/369** with exactly the eight approved D6 reds (`tier-ladder` 10/11, `promotion-sweep` 3/4, `promotion-stream` 0/1, `promotion-avatar` 0/1, `promotion-record` 0/1, `development-arc` 3/4, `practice-seam` 5/6, `development-equilibrium` 0/1); `--only resolver` 13/13 byte-identical to `d951efe`; every allocation line 0 B; SchemaValidator 174/174; CoreLoop 22/22; GrittyEvents 363/363. Also record the **default MC wall time** on the owner's PC: it's the baseline for CS-M §6's ≤ 120 s budget. Record any difference.

## 1. Context

13-2 made both sims run the v14 roster shapes, but on an interim calendar. College and Pro play one circle-method round per day for 154 days (1,848 and 2,464 games a season), HS still plays the shipped 26-game Tue/Fri `HsSeasonCalendar` (156 games), and the old season guard is unreachable. 13-3 gives each tier its real season (spec §3): seeded, persisted schedules in `Schedule_Games` that both sims and the career driver read, and one `TierSeasonCalendar` for all three tiers. On those calendars it re-bands the tiers, retires the old season guard, records the new Pro guard (§11.3), and retunes development (the practice lever and the tier gradient). It's the last slice that changes the world's shape, and the arcade's G-3 calibration takes its per-tier bands from here (BUILD_PLAN). For the player ([GAME_VISION.md](../GAME_VISION.md)), it's the rhythm of the climb: 1 game a week in HS, 2 in College, 3 in Pro.

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
| HS | Fri Mar 2 (day 61) | Fri May 4 (day 124) | 10 Fridays | 10 | 60 | 6 | 156 (26-game calendar) |
| College | Wed Feb 7 (day 38) | Sat May 12 (day 132) | 28 (Wed + Sat × 14) | 28 | 336 | 12 | 1,848 |
| Pro | Tue Apr 3 (day 93) | Tue Aug 28 (day 240, D2) | 64 (Tue/Thu/Sat × 21 + week 22 Tue) | 64 | 1,024 | 16 | 2,464 |

The Draft falls on Sat Jul 14 (day 195). The College postseason ends by day 174 at the latest (Jun 23), and the Pro postseason starts in September (day ≥ 244). Sundays are the days with day ≡ 0 (mod 7), and none of them is a game day. **Exposure:** one real season has **38.5% (HS, 60 of 156), 18.2% (College) and 41.6% (Pro)** of today's season's games. The first interim Pro season has 2,448, because day 1 is skipped. *(Corrected per Astra §10.1: the draft compared HS against a dense 924-game schedule HS never ran.)* Every check that gates a rate or a count on one season has to be resized (§3.3 M1, §5.4).

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
  - **Recommended:** keep the numeric rows. Gate **R/9**, and report **ER/GS** beside it on the same sample. Gate each tier's band on a **pooled, fixed-seed sample of real-calendar seasons**, sized per tier by Astra (HS needs many more seasons than Pro) within the default runtime budget (D10). CS-M's opt-in batch is the calibration evidence.
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
- **D10. Default MC runtime budget** *(added 2026-10-25 after Astra's measurement; **pending owner**)*. CS-M §6 proposed ≤ 120 s on the owner's PC without measuring it. Astra measured today's default at **183.7 s** (§10.1), so no added sample fits.
  - **Recommended:** replace the absolute 120 s with a relative budget. 13-3 may add **at most 30 s** to the measured baseline, with College's share ≤ 15 s as CS-M proposed. Astra's timed pilot predicts the addition before the pooled checks go in, and the handoff measures it. If the pilot predicts more, the pooled sizes come back for review, and they're never cut after seeing results.
  - The 20-run replay (about an hour on that PC) runs once, unattended, for the guard record. Speeding up the existing suites is a separate, later task.
  - *Alternative:* hold 120 s. That first means cutting ≥ 64 s from the existing suites, which is profiling work outside 13-3.
- **D9. When `p13/integration` reaches `main`.** §15 says when the full MC suite is green with the new Pro guard, but the seven promotion suites stay red until 13-6.
  - **Recommended:** merge to `main` after 13-6, so `main` stays green as §15 intends. Phase G and L keep branching from `p13/integration`.
  - *Alternative:* merge after 13-3, with the seven reds owner-approved on `main`.

### 3.3 The split: Math deliverables (GPT 6 Astra Pro) vs the build (Claude Opus)

| # | Deliverable | Owner | Acceptance |
| :-- | :-- | :-- | :-- |
| B1–B7 | Calendar, scheduler, schedule-driven sims and career driver, D3 blocks, D4 results, harness and skill (§4.1) | **Opus** | §5.1 green; reviewed by Astra (M5) |
| **M1** | **Band sample design and the run-floor verdict (D5):** pooled seeds/seasons per tier, with a target standard error per band; the R/9-vs-ER/GS seam measured on the same sample; pooled measurements on Opus's build; a verdict on Pro R/9 4.16 vs the 4.2 floor. Default design (§10.2): **64 HS / 16 College / 8 Pro** fixed-seed flat seasons, pooled totals, cluster SEs; R, ER, outs and GS printed together; timed by a pilot first | **Astra** | Recorded in the Math doc with seeds, sample, intervals and runtime; reviewed by Opus |
| **M2** | **College re-band per CS-M:** confirm the §1 rows; acceptance for `RunCollegeRateSmoke` (2 checks); run the opt-in rates batch (16,384 seasons), or, if a timed pilot says that's infeasible, a reviewed reduced plan *before* running. Pilot and stop rule in §10.2 Q3: 32 training seasons; stop if the pilot reaches 60 s; no full matrix above a 30-minute forecast without Opus's review | **Astra** | CS-M §1/§6: pooled rates in band, clustered 95% intervals |
| **M3** | **New Pro guard sanction (D6):** reproduce Opus's candidate 20/20 byte-identical on the owner's PC (timing lines normalized); sign the values | **Astra** (owner sanctions) | Values and hash recorded in §10 and the skill |
| **M4** | **Development values (D7):** gradient targets and their tolerance; practice-lever constants (`PracticeFracPerHour`, `PracticeFracCap`, or per-tier if needed) for the §1.2/D3 hours; new goldens for `practice-seam` §10.5 | **Astra** | Values in the Math doc; Opus applies them as data edits |
| **M5** | Independent review of Opus's sim diff | **Astra** | 13-2 §9-style record |

**Arithmetic for M4 (Opus's arithmetic, not a target).** In-season team practice at 2 h, under D3-A windows:
- HS: Mon–Thu × 10 weeks = 40 days = 80 h.
- College: Mon/Tue/Thu/Fri × 14 weeks = 56 days = 112 h.
- Pro: Mon/Wed/Fri over weeks 1–21, plus the Monday of week 22 = 64 days = 128 h.

At today's constants that's a practice fraction of **.037 / .050 / .056**, against **.2469** for the shipped proof's 6 h a day all year. The lever is close to inert in-season. What the offseason sliders add depends on D3.

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
| Runtime | default MC | Wall time against §0's baseline (183.7 s, §10.1) and the D10 budget. Astra's timed pilot (4 HS + 4 College + 2 Pro seasons) predicts the pooled addition before those checks are installed (§10.2) |
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
- career-wiring's "avatar PA 450–800", as a per-tier band Astra signs after measuring the real fixture. Pilot envelopes, scaled from the 153-game fixture: HS 22–41, College 82–147, Pro 188–335 (§10.3 Q7);
- the 3-generation season counts;
- HS `GamesPerSeason` in `promotion-avatar`;
- the `starters` and `innings` day loops.

Each is listed in the handoff as old → new → reason. Same-seed delta checks (rivalry, person-effects ΔR/G ≤ .05) keep their ER/GS unit and .05 limit, on common-seed pools with at least the old exposure (16 HS / 6 College / 3 Pro seasons, or 3 HS seasons where the old fixture used sparse HS; §10.3). If one fails, it's reported with Astra's view, never widened silently. Retire by name with the replacement (13-1 rule).

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

### 10.1 Brief Math check — 2026-10-25

**GPT 6 Astra Pro; verdict: changes requested to the brief's exposure arithmetic and measurement plan, not to the approved D1–D9.** Calendar dates, pairing counts, D3-A windows, M1–M5 ownership and the D6 guard shape are sound. The corrections below govern this Math check; Opus should reconcile the earlier prose/spec exposure note before the build handoff. This is a review of `4ed94ee`, not M1–M4 calibration, M3 guard sanction or M5 review of an implementation. No sim, harness, bands or owner decisions were changed.

**Evidence on the owner's Windows PC:** Intel Pentium Silver N5030 @ 1.10 GHz, .NET SDK 8.0.422; isolated branch `p13/13-3-mathcheck-gpt` based on `origin/p13/13-3-brief-opus` (`4ed94ee`). Empty-commit write check succeeded with escalation and was soft-reset. The first sandboxed Debug build failed without compiler diagnostics; the escalated `dotnet build DirtAndDiamonds.sln -c Debug` passed, **0 warnings / 0 errors, 132.48 s**. Release harness compilation (`dotnet build Tools/MonteCarloHarness/MonteCarloHarness.csproj -c Release --no-restore`) passed 0/0, 19.58 s, excluded from simulation timing.

One baseline run: `dotnet run --project Tools/MonteCarloHarness -c Release --no-build`, default 100,000 PA, existing fixed seeds, scratch databases, unmodified source at `4ed94ee`: **361/369, exit 1, 183.6855 s end-to-end** (Stopwatch around the process). Exactly the eight approved D6 reds: `tier-ladder`, `promotion-sweep`, `promotion-stream`, `promotion-avatar`, `promotion-record`, `development-arc`, `practice-seam`, `development-equilibrium`; allocation checks report 0 B. The default includes resolver 13/13, but no fresh byte comparison against `d951efe` was performed. Local raw log: `%TEMP%/13-3-mathcheck-default.log` (untracked). No separate focused run, new-calendar simulation, full College analysis, 20-repeat replay, schema/boot gate or implementation test is claimed.

**Required factual corrections:**

- **HS exposure (§1 / §1.2):** the actual base already uses `HsSeasonCalendar.TryGetLeagueScheduleDay` inside `LeagueSimulator.OnDayAdvanced`. Its 26 games/team mean **156 league games**, not 924. The new 60 games retain **38.46%** of that exposure, not 6.5%. The 924 figure is the counterfactual dense 154-day schedule; it is not the executed HS baseline. College 336/1,848 = **18.18%**, Pro 1,024/2,464 = **41.56%** are full-season comparisons; the first interim Pro season has 2,448 games because day 1 is skipped. Spec §11.2's matching 924 comparison needs the same factual correction; D1–D9 do not change.
- **Calendar / M4:** days 61–124, 38–132, 93–240, Draft 195, 60/336/1,024 league games, and 80/112/128 practice hours all check out. Inclusive practice windows are days **57–124 (68 days), 36–132 (97), 92–240 (149)**. Current practice fractions are **.036964/.050171/.056465**; the last rounds to .056 at three decimals, not .057. The 6 h/day annual comparator is .24687. Counts concern the fixed sim calendar, not Gregorian leap-year variation.
- **HS uncertainty (§9 Q1):** with the assumed team-game run SD 3.5, 3.5/sqrt(120) = .3195 is an ER/GS-like SE. An approximate 7-inning **R/9** SE is **(9/7) × .3195 = .4108**, before covariance and variable-innings corrections. Opposing teams share a game; team-games are not independent clusters. Actual ratio uncertainty must be estimated from independent seed-season clusters.

### 10.2 Answers Q1–Q4: samples, floor, batch and guard

**Q1 — proposed default pooled design (M1, provisional until the new build is timed).** Use neutral spread-0 fixtures, no development, injury or active modifiers; exact existing tier deltas and selection rules. Fresh independent seasons, not repeated development of one roster. Pool totals (H, AB, BB, PA, total bases, SO, HR, runs, outs, GS), never averages of ratios. Share each tier's pool between rate assertions; the four College smoke seasons can be a subset, with their separate structural/finite-value checks retained.

| Tier | Independent seasons | League games / team-games | Approximate run SE under SD=3.5 | Planning SE targets |
| :-- | --: | :-- | --: | :-- |
| HS | 64 | 3,840 / 7,680 | .0514 R/9 | R/9 ≤ .06; each AVG/OBP/K/BB/HR rate ≤ .002; SLG ≤ .005 |
| College | 16 | 5,376 / 10,752 | .0338 R/9 | R/9 ≤ .04; other targets as HS |
| Pro | 8 | 8,192 / 16,384 | .0273 R/9 | R/9 ≤ .03; other targets as HS |

Total **17,408 games**. These are sample-size proposals, not measured SEs or a claim that 4.16 passes 4.2. Use world/replicate IDs **129–192 for HS and 129–136 for Pro**, with College tuples specified below, immutable schedule/outcome seed tuples labelled by tier and replicate; preserve College smoke tuples `(world 129/130, scheduler 1, outcome 1/2)` as its first four independent outcomes, with the remaining 12 world IDs 131–142, scheduler 1, outcome 1. Record the concrete stream mixer and seed manifest before running; no outcome-based seed replacement. Flat worlds do not create 128 independent roster populations merely by changing an unused spread seed. For the large M2 crossed matrix, retain the world-seed grouping and schedule/outcome dependence in uncertainty estimates.

Estimate ratio SE by seed-season totals: for r=sum(X)/sum(D), use residuals `X_i - r D_i`, sample SD divided by `sqrt(k) × mean(D_i)` (multiply by 27 for R/9). Report 95% t intervals with k−1 degrees of freedom; use paired residuals for comparisons. PA-level Bernoulli calculations are planning approximations only, especially for SLG and repeated matchups. The default gates pooled point estimates against unchanged bands; report intervals and whether SE targets were achieved. An interval crossing a boundary is inconclusive calibration, not permission to widen the band; a point outside the band fails. Freeze any revised sample plan before its acceptance run, with new holdout seeds if precision planning inspected outcomes. Eight Pro clusters give only a rough variance estimate; M2-sized evidence is separate from this smoke-level default gate.

**Runtime finding:** the existing default is already **63.69 s (53.1%) over 120 s**. No positive added sample can be certified within that budget on this measurement. The pipeline's internal 939 ms covers 4,912 games, suggesting about **3.33 s** for 17,408 games at that kernel throughput alone; this excludes world generation, DB setup/flush, schedule work and other suites and is **not** an end-to-end forecast. On B1–B7, time the fixed pilot **4 HS + 4 College + 2 Pro seasons**, with setup/flush included, then predict `64*tHS + 16*tCollege + 8*tPro` from per-season timings. Compare the actual whole default against 120 s and the College addition against 15 s. Profile/reuse samples and remove duplicate execution, not assertions. If still over budget, return a concrete runtime/fixture proposal for review; the present check does not increase the budget or certify feasibility. The base timing also rules out promising a ≤40-minute 20-run replay today.

**Q2 — Pro floor:** no reliable new-calendar center is established yet. This base reports tier R/9 **HS 7.14, College 6.32, Pro 4.16**; season-pipeline ER/GS is **4.23 (2026), 4.21 (2027)**. The tier run uses Pro outcome seed `SeasonSeed + 2 = 779`; pipeline uses 777 (world seed 20260703 in both). Those outputs are not a paired denominator experiment. The archived macro §8 row is explicitly **R/G 4.2–4.8**, and `AssertSeason` computes ER/GS; it was not an established same-sample R/9 band. On one sample, `R9/(ER/GS) = (R/ER) * 27*GS/outs` when ER>0. In the current macro all runs are earned, but extras can raise average outs above 27, while skipped home ninths lower them. Thus R/9 ≥ ER/GS is conditional on outs/GS ≤27, not universal. M1 must print R, ER, outs and GS together. Do not infer a downward floor change from the denominator label alone. If the fixed pooled Pro point remains below 4.2, present D5's two choices to the owner; no new floor or advancement knob is signed here.

**Q3 — opt-in M2:** 16,384 × 336 = **5,505,024 games**, outside the default budget. At the base pipeline throughput alone this is ~1,052 s (~17.5 min), a lower bound, not a runtime promise. On B1–B7 use a fixed **32-season training pilot**: world IDs 1–8 × scheduler seeds 1/2 × outcome seeds 1/2, flat College worlds, isolated from holdout 129–256. Time complete season setup, schedule generation, simulation and DB aggregation; checkpoint each tuple. Extrapolate `512 × pilot wall time` and reserve 25% overhead. Proposed operational stop rule: stop between tuples if the pilot reaches 60 s; do not start the full matrix if its upper forecast exceeds **30 minutes**, and obtain Opus review of a reduced plan first. These are proposed analysis limits, not a claim of prior owner approval. A reduced plan retains independent seeds and explicit SE/interval targets; it cannot discard failures or reduce samples after seeing a bad band. Log spread-25 diagnostics and their time separately. Freeze the execution plan before holdout; do not repeatedly peek until a rate passes.

**Q4 — guard:** retain approved D6: first-season flat-world PA/H/ER literals, plus SHA-256 over **both seasons' phase-0** Batting/Pitching_Stats rows, sorted by explicit unique keys, with explicit column order, invariant numeric encoding, null encoding and byte delimiters. Record schema/fixture/seed manifest with the hash. Hash semantic rows, not SQLite file bytes, row insertion order or timing. Spread 25 is a separate diagnostic, not a substitute. Candidate follows settled D5 and all gates except the seven enumerated promotion reds; M3 independently reproduces 20/20, Opus reviews Math, owner sanctions. A ledger hash does not prove every schedule row stayed identical: preserve independent scheduler determinism/balance checks. Survival of 13-5 designations is conditional on the guard's neutral roster fixture remaining identical; changed eligible personnel, starter usage or ordering require that slice's own explicit sanction. No blanket promise that a real roster-policy change is invisible.

### 10.3 Answers Q5–Q7 and implementation boundaries

**Q5 — practice:** the lever is a fraction of remaining potential gap (and mitigates older-player decline), not a direct rating-point award. Recommend testing a **shared** `PracticeFracPerHour=.001`, `PracticeFracCap=.25` candidate: full in-season practice yields **.068463/.090274/.100176** for HS/College/Pro. At a 20-point gap that is about **1.37/1.81/2.00 additional unrounded growth points** before other clamps/rounding; compare no practice, current .0005 and candidate .001 on identical players/RNG. These are candidate M4 targets, not signed code constants or new goldens. Keep NPC/no-practice behavior and the cap exact; do not invent per-tier multipliers to manufacture a gradient.

D3-A leaves **297/268/216 free-slider days** before 13-4 postseason overrides. At an illustrative 2 completed practice hours per such day, extra hours are **594/536/432** and annual totals **674/648/560**; offseason exposure can dominate and reverses the ordering of annual hours. D3-B is rejected and would provide zero offseason hours. Because `f(h)=cap*(1-exp(-slope*h/cap))` is nonlinear, report the offseason marginal `f(in+off)-f(in)`, not additive independent fractions or a fixed percentage share. Also test 0 and 6 h/day offseason scenarios, actual completion/credit consumption and the one-avatar-only lever. Do not claim all NPCs receive planner practice. M4 must check succession/reset and annual goldens on B1–B7; it cannot sign only the in-season arithmetic.

**Q6 — gradient:** retain the shipped definition: mean **raw batter** `(power + contact + discipline)` per tier, role=None, excluding effective tier/person/gear deltas; its scale is 0–300, not 0–100. Use generated **spread-25** worlds and the actual age/headroom distribution; a spread-0 world deliberately puts everyone at potential and is not a valid initial talent-gradient fixture. For the default single-pass check, pin generation/development seeds and handler order (one age tick and one development pass, no promotions), log pre/post means and adjacent gaps, require **HS < College < Pro with no negative tolerance**. Compare rational means by integer cross-products where practical, avoiding rounding to display precision. Do not invent absolute tier means or a minimum multi-point gap before measuring them. M4 should additionally report seed-level paired gaps for training IDs 1–32 and holdout 129–160; positive lower 95% bounds on pooled adjacent gaps support robustness, but do not replace the exact fixed-fixture check. The avatar-practice candidate cannot establish an NPC equilibrium.

13-6 owns actual graduation/recruiting/draft/retirement flows, roster conservation and the **20-season time-averaged equilibrium**. A finite 20-season monotone average alone does not prove stationarity: its brief must predeclare warmup/window, seeds and first/second-window drift tolerance after a training pilot. No numeric equilibrium drift threshold is signed from a single development pass. This refines measurement of D7 without reopening its scope split.

**Q7 — restated checks:** keep an explicit old → new → reason ledger and distinguish deterministic opportunities from stochastic PA. GS=2×games and W=L=games stay exact; 13-3 season counts come from completed scheduled games, not 154 or a guessed PA ratio. For a healthy everyday hitter in the same lineup slot, preliminary PA bands obtained solely by scaling the existing 450–800 over the executed 153-game fixture are **HS 22–41** (`10*(7/9)/153`), **College 82–147** (`28/153`), **Pro 188–335** (`64/153`), outward-rounded. These are exposure-derived pilot envelopes, **not signed acceptance bounds**: tier OBP, 7-inning endings, extras and lineup slot affect PA. M1/M4 must measure the actual career fixture before Opus installs goldens; report all scheduled/played/attended games and availability. Do not apply hitter PA bounds to pitcher avatars or pooled rates to individual players. The claim 'about a third' is not a fixture contract (64/153 is 41.83%).

For person-effects/rivalry paired ΔR/G ≤.05, keep the original **ER/GS estimand and .05 limit** unless separately sanctioned; D5's R/9 choice for tier bands does not silently change a paired regression's unit. Start with common-seed pools retaining at least old exposure (full-season 154 games/team: **16 HS / 6 College / 3 Pro seasons**); if a given old fixture used sparse HS instead, its exposure match is **3 HS seasons** for 26→10. Replay both arms with identical schedule tuples and retain exact rowless/neutral identity assertions. Use season-paired difference uncertainty; if precision is insufficient, propose more fixed samples rather than relaxing .05 or cherry-picking seeds. Structural starter/innings fixtures must preserve their intended gap/boundary cases while production calendar-loop counts change. Exact 23-starter tests plus the three requested regressions are not replaced by statistical bands.

**Handoff / open decisions:** Opus reconciles the factual HS baseline and the sample/runtime plan before relying on them. D1–D9 remain approved. No owner choice is requested now on the Pro floor: M1's new-calendar paired evidence is still owed. If measured optimized runtime cannot meet 120 s, or pooled Pro R/9 stays below 4.2, return the specific evidence and alternatives to the owner; neither limit changed here. All M1–M5 completion, development constants/goldens, the new Pro hash and owner sanction remain pending. CS-M's rest-3, retired-guard and pooled R/9 supersession was folded into its body, with a one-line pointer replacing the banner. **Recommended next: Claude Opus reviews this Math check and reconciles the brief, then builds B1–B7 after its integration merge; GPT measures the briefed Math deliverables and independently reviews the sim diff.**

### 10.4 Opus review of the Math check and reconciliation (Claude Opus 5.5, 2026-10-25)

**Accepted.** Re-derived and confirmed:
- Exposure: HS 60/156 = 38.46% (the base routes HS through `HsSeasonCalendar` at `LeagueSimulator.cs:509`; the draft's 924 was my error); 2,448 games in the first interim Pro season.
- Practice: .056465 → .056.
- Pooled design: 64/16/8 seasons = 17,408 games; run SEs .0514 / .0338 / .0273 R/9 under SD 3.5.
- Candidate practice fractions .068463 / .090274 / .100176; offseason days 297/268/216.
- PA envelopes 22–41 / 82–147 / 188–335; exposure-matched pools 16/6/3.
- Throughput bounds: 3.33 s and about 1,052 s.

**Reconciled here:**
- §1, the §1.2 table and the exposure line; spec §11.2.
- the §3.3 M4 arithmetic; the M1 and M2 rows (default design, pilot and stop rule).
- the §5.1 runtime row; §5.4 (PA envelopes, paired checks keep ER/GS and .05).
- §0 and the status line.

**Adopted as the build's measurement plan, still provisional where Astra says so:**
- the 64/16/8 design and the cluster-SE method, with R, ER, outs and GS printed together;
- the 32-season M2 pilot and its stop rule;
- the Q4 hash specification: phase-0 rows of both seasons, explicit keys and encodings, plus a seed manifest;
- Q5's shared `.001` candidate, to be *tested* against 0 and `.0005`, not signed;
- the Q6 raw-batter-sum, spread-25, single-pass check with no tolerance;
- the Q7 old → new ledger.

**One finding goes to the owner:** today's default MC is already 183.7 s against CS-M's unmeasured 120 s budget, so D10 was added. No D1–D9 change. The Pro floor stays open until M1's pooled measurement, as D5 says.

---

### Spec issues found while drafting — **resolved 2026-10-25** (owner: "resolve these problems")

After the owner approved D1–D9, Opus applied each decision to its spec home on `p13/13-3-brief-opus`:
- **1, 2:** spec §11.3, §15 (the integration paragraph and the 13-3/13-6 rows) and §16-I11/I12; BUILD_PLAN Phase 13; progress.md status lines.
- **3:** §3 table, §3.1 and §16-I9.
- **4:** §3.2 and §16-I10; the `development_decline_curves.md` banner.
- **5:** §11.2 and §16-I11.
- **6:** a dated supersession banner in `college_28_game.md`. The author's body is untouched; Astra folds it in at the Math check.
- **7:** §4.

`.agents/skills/run_monte_carlo_batch/SKILL.md` stays with the build (§4.1 item 9), because its new guard values don't exist yet. The original list, for the record:

1. **§11.3 / §15:** the new guard is recorded "on the first fully green run", and `p13/integration` merges when the "full MC suite is green" after 13-3. Neither can happen before 13-6, because the promotion suites stay red until then (D9).
2. **§11.3:** the development gradient is "retuned per slice 13-3", but its equilibrium needs 13-6's pipeline (D7).
3. **§3.1:** HS "Home/away alternates" is ambiguous, and the day of Pro week 22's single game isn't specified (D2). §3.1 also says generation "runs at season rollover", which D1 interprets.
4. **§3.2:** "keep their shipped mechanics" (HS locked all year) conflicts with §9.3's offseason training once extended to every tier (D3).
5. **§11.2:** "reuse the MLB row verbatim … per 9 innings". The shipped MLB run band was gated as ER/GS, not R/9 (CS-M §1's seam; D5).
6. **`college_28_game.md`:** the header still says "edited-section re-check pending" (done, CS-M brief §9.1). §2, §4 and §6 keep the rest-4 fixtures (14/14/0/0, gap 3 ineligible), superseded by spec §6 (rest 3). §6's "Frozen guards" still names the old guard as the reference.
7. **§4** cites `LeagueSimulator.cs ~L518–530` for innings; those lines moved in 13-2 (cosmetic).
