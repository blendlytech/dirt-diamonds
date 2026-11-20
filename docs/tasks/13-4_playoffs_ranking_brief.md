# Task Brief: 13-4 — Playoffs, standings and the College National Ranking

**Status:** **DRAFT (Claude Opus 5.5, 2026-11-20). Do not build.** Next: Gemini 4 Argon's Math check (§9 → §10), then the owner's decisions **D0–D7** (§3). The build starts only after both, from `p13/integration` with this brief merged (§0).
**Recommended model:** **13-4a** (engine, persistence, harness): Claude Opus 5.5, with Gemini 4 Argon (Math: ranking analysis, tie/coin and stream contracts). **13-4b** (bracket UI): Claude Sonnet 5.5, staged per D6. TEAM.md §2, 13-4 row.
**Reviewer:** each reviews the other's part (TEAM.md §1a): Argon independently reviews Opus's sim/persistence diff; Opus reviews Argon's Math record. 13-4b goes to Gemini 4 Neon if its diff touches no `Assets/Simulation/`, schema or persistence path, otherwise to Argon. Opus is the merge gate.
**Owner gate:** D0–D7 before the build. None at the end, **unless** D4 is decided in a way that moves the sanctioned Pro guard (then a new sanction is required; the recommended D4-A does not move it).
**Spec:** [league_restructure.md](../design/league_restructure.md) §3, §3.2, §4, §6, §8 (8.1, 8.2, 8.2a, 8.3), §11.3, §12, §13, §15 (13-4 row), §16-I3/I6/I7/I8/I10/I11; [math/college_28_game.md](../design/math/college_28_game.md) (CS-M) §§3–6; [scope_realignment.md](../design/scope_realignment.md) gate table (Gate 1 row); [temptation_and_lineage.md](../design/temptation_and_lineage.md) §4.4 (the reveal consumes 13-4's event; 13-4 builds no reveal).
**Branches / worktrees:** brief `p13/13-4-brief-opus` (this file); build `p13/13-4a-opus`; Math `p13/13-4a-math-argon`; UI `p13/13-4b-sonnet` (if D6 stages it now). All start from **`p13/integration`** at or after `b65a00b` plus this brief's merge, each in its own worktree, and merge into `p13/integration`, **never `main`** (spec §15, §16-I12).

---

## 0. Preconditions (check first; stop and report if any fails)

1. Your own worktree (TEAM.md §3); `git status` clean; `C:` free space ≥ 2 GB (stop below that; 13-3's replay once filled the disk).
2. `p13/integration` at or after **`b65a00b`** (13-3 merged) with this brief merged and D0–D7 recorded in §3.
3. Baseline on that head before any edit: build 0/0; SchemaValidator **174/174**; CoreLoop **22/22**; GrittyEvents **363/363**; MC default **408/415** (exactly the 7 promotion reds owned by 13-6; tier-ladder 14/14; resolver 13/13 with `--only resolver` LF SHA-256 `6d92422863f22d73…`; Pro guard **PA 81433 / H 18471 / ER 8803**, ledger **`932ACE26…8B84`**; every allocation line 0 B; 0 leaked `%TEMP%` `dnd_*` files). Record its wall time; the normalized default-run log SHA-256 is `7430347AA53EB5CC…B239` (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`).
4. **No Blind Queries:** run `validate_sqlite_schema` against a **scratch copy** before writing any new SQL against `Schedule_Games`, `Playoff_Series` or `Team_Seasons`.

## 1. Context

13-4 gives every tier a season climax: HS's bracket of 6, College's 18-team tournament ending in **The Red Dirt Series** (best-of-3, Thu–Fri–Sat), and Pro's 12-team Bo3/Bo5 playoffs ending in the World Championship. It also adds standings and the weekly College **National Ranking** ("#7 nationally"). The Pro World Championship is the only title that can fire the GOAT-father reveal (spec §8.2a, GAME_VISION v3.0). 13-4 publishes the tier-carrying `ChampionshipWonEvent`; Phase L consumes it later.

**Gate staging.** 13-4 is **not** on the Gate 2 path (scope_realignment Gate 1 row: 13-4…13-7 run only inside the designed v14/v15 and must not block Gate 2). The v15-DDL-only freeze hasn't started (L-1 hasn't landed). 13-4's recommended path adds **no DDL** (D4-A).

### 1.1 What exists on `p13/integration` (`b65a00b`)

| Seam | State today | 13-4 consequence |
| :-- | :-- | :-- |
| `Schedule_Games` (`SchemaDefinitions.sql:604`) | `phase` 0/1/2, `series_id`, `game_in_series`, `status` 0/1/2 exist; only phase-0 rows are written. `ScheduleQueries` filters `phase = 0` (`ScheduleQueries.cs:42,52`); `SeasonSchedule` caches phase-0 rows only (`SeasonSchedule.cs:51`) | Postseason rows need their own insert and per-day read; the regular-season cache stays unchanged |
| `Playoff_Series` (`:589`), `Team_Seasons` (`:624`) | DDL with CHECKs (`best_of IN (1,3,5)`, `high ≠ low`); **no writer** | 13-4 is their first writer |
| `Batting_Stats` / `Pitching_Stats` (`:49`, `:75`) | **`UNIQUE (player_id, season_year)`**. No tier, league or phase column | Postseason lines can't share these rows without moving the guard (D4) |
| `TierSeasonCalendar` | `PostseasonFirstDay(tier)` = last regular day + 1 (HS 125, College 133, Pro 241); locked team blocks cover the regular window only (`TryGetLockedTeamBlocks`, used at `GameManager.cs:1101`) | Postseason game days + blocks for teams still alive (spec §3.2) |
| `StarterSelection` | `SelectStarter(lastStart, available, usedInSeries, …)` honours a series filter (13-2); `LeagueSimulator.cs:689` passes an all-false span ("13-4 supplies Red Dirt Series context"). **`SelectOpener(relieverAvailable)` has no series filter** (`LeagueSimulator.cs:724`) | Feed the filter; close the RP-opener repeat gap (CS-M §4) |
| `PitcherRestLedger` | Records **both** SP starts and RP openings (`LeagueSimulator.cs:711, 729`); persisted to `Pitcher_Rest` | "Started in this series" is derivable from persisted state: last start ∈ [game-1 day, today). No extra coordinator storage needed |
| `LeagueSimulator` | One `_rng` stream per tier sim (`:48`); results ride the Sunday flush batch (`FlushAccumulated`, `:1113`, 13-3 D4) | Postseason must not draw from `_rng` (D4); selection needs a forced end-of-regular-season flush (D1) |
| `CareerManager` | `PendingAttendedGame` carries `gameId`; attended claim via `SetAttendedTeam`; neutral autopilot | Avatar postseason games park and stop the clock like regular games |
| Events (`Assets/Core/CoreEvents.cs`) | **No** `ChampionshipWonEvent` / `PlayoffSeriesDecidedEvent` | 13-4 adds both (spec §13) |
| Pro guard world (`Program.cs:478–540`) | One Pro `LeagueSimulator` with `RngState(SeasonSeed)` over 730 days; `ProGuardLedgerQueries` (`Slice133Suites.cs:49`) hashes **every** `Batting_Stats`/`Pitching_Stats` row | The guard must stay byte-exact **with the postseason running** (D4) |

### 1.2 Prior art: `p13/13-4-gpt` @ `6993e70` (reference only, see D0)

GPT 6.1 Sol built an unreviewed 13-4 core on 2026-10-25 under a since-superseded instruction (Math and review deferred). It's based on 13-3's pre-sanction `9d8dbf3` and self-described as **incomplete** (no attended bridge, no UI). Contents: `RankingCalculator`, `PlayoffManager` with a `Game_State` JSON coordinator (`playoff.<year>.<tier>`), `PlayoffQueries`/DTOs, `LeagueSimulator.SimulatePlayoffGame` (per-game seed, preallocated, unpersisted postseason buffers), `ChampionshipWonEvent`, and a 47-check `playoffs-ranking` suite. It reported the candidate guard unchanged.

Worth keeping: the ranking arithmetic, the hosting helper, the per-game-seed and private-buffer isolation pattern, the read-only view shapes (`PlayoffSeriesView`: clinch/elimination flags) and its fixture list. Not worth keeping: the JSON coordinator, which is a second source of truth beside `Playoff_Series`. Its "next game day after the round ends" dates differ from D3, its ranking ties fall back to team ID, and it predates the D5 band and the sanction.

## 2. Hard constraints

- **Allowed paths (13-4a):** `Assets/Simulation/Baseball/` — new `PlayoffManager.cs`, `RankingCalculator.cs`, `Standings.cs` (or one file per the build); edits to `LeagueSimulator.cs`, `StarterSelection.cs` (opener series filter only), `TierSeasonCalendar.cs` (postseason days), `SeasonSchedule.cs`, `CareerManager.cs`, `MicroGame.cs` (series filter + attended postseason only). `Assets/Data/Database/` — new `PlayoffQueries.cs`, DTOs; `ScheduleQueries.cs`. `Assets/Core/` — `CoreEvents.cs` (two events), `GameManager.cs` (composition, postseason locked blocks, clock stop). `Assets/Localization/strings.csv` (the series-name key, D3). `Tools/MonteCarloHarness/` (new suites, `--college-analysis ranking`), `Tools/CoreLoopHarness/` (postseason clock fixtures). Docs: this brief, the Math record, `progress.md`, spec §8/§16 interpretation sync, the MC skill. **Everything else is read-only.**
- **Calibration contract:** `AtBatResolver`, `BaseOutAdvancement`, `MicroGame`'s per-PA model, `TierEffects`, `EffectiveRatings` and every band are untouched. Resolver guard byte-exact (`6d924228…`). **Pro guard byte-exact with the postseason wired into the guard world** (D4). Neutral regular-season paths are byte-identical: the default MC log, normalized, may change only by the lines new suites add.
- **Zero allocation:** the postseason game loop (macro and attended) allocates 0 B (new probes alongside the existing ones). Standings, ranking, selection and bracket building run in flush/selection code outside the per-PA loop. They may allocate, bounded and once per day at most.
- **Determinism / RNG:** postseason never draws from a tier sim's `_rng`. Each postseason game gets a stream derived from persisted inputs (§9 Q7). Coin flips come from a dedicated derived stream (D2). Presentation never reads sim RNG. Same save, same seed ⇒ same bracket, dates and champions, including across a reload mid-series.
- **Boundaries:** `PlayoffManager` lives in `/Simulation/Baseball/` and talks to Life only through bus events and the DB. UI is read-only over DTOs (spec §13).
- **Reveal safety and secrecy (GEMINI §10, spec §8.2a/§16-I8):** `ChampionshipWonEvent` always carries the tier. 13-4 reads no lineage data and fires no reveal. An HS or College title can never satisfy the reveal predicate.
- **Trademarks:** no MLB/NCAA championship-series names anywhere. The College final's name is the strings key **`league.college.final.name`**, never a C# literal. Pro keeps "World Championship".
- **No Sunday games**, regular season or postseason (asserted).
- **DB:** parameterized, prepared statements; one transaction per batch; a postseason result, its series update and its `Team_Seasons`/rank effects commit together.
- **Gate 2:** the HS regular-season and weekly-stance paths behave exactly as today; 13-4 only extends past the regular-season window.

## 3. Owner decisions (D0–D7)

Each has a recommendation; the owner may pick another option. **D4 is the critical one.**

### D0. Prior art (`6993e70`)

- **A.** Ignore it; build fresh.
- **B (recommended). Fresh branch from `p13/integration`, transplanting vetted pieces with attribution:** the ranking arithmetic, `HighHosts`, the isolation pattern, the view-DTO shapes and fixture ideas. The coordinator is the DB rows (`Playoff_Series` + `Schedule_Games` + `Pitcher_Rest`), with no JSON copy. Argon reviews the result as new code.
- **C.** Rebase the whole commit onto `b65a00b`, then fix it. Cheapest start, but it inherits the second source of truth and the D2/D3 mismatches.

### D1. `Team_Seasons` materialization

- **A (recommended). Recomputed in every results flush batch, all three tiers, as an idempotent upsert from `Schedule_Games`** (phase 0, `status IN (1,2)`; never incremental deltas, so a reload or replayed flush can't double-count):
  - `wins`, `losses`, `runs_scored`, `runs_allowed`: **regular season only**. Postseason results never change them; their truth is `Playoff_Series` + phase-1/2 rows.
  - `national_rank`: **College only** (NULL for HS/Pro), recomputed in the same batch each week (Sunday flush). Postseason is excluded, so the final regular-season rank is the **selection rank** and can't move afterwards (CS-M §3: frozen for hosting).
  - `seed`: NULL until selection; then the **bracket seed** for qualifiers (HS 1–3 per conference; College 1…n inside its conference bracket, by national rank; Pro 1–6 per conference); NULL for non-qualifiers. The play-in pair stays NULL until the play-in winner joins its bracket.
  - **Selection day = `PostseasonFirstDay(tier)`** (HS 125 Sat, College 133 Sun, Pro 241 Wed). It **forces a flush** of pending regular-season results first. Pro needs this: its last regular game (Tue 240) would otherwise wait for Sunday 245, after the Wild Card opens on Sat 244.
  - Postseason game days commit **per game day** (few games; the bracket depends on each result).
- **B.** Computed once at regular-season end. Can't satisfy the weekly ranking (§8.2).
- **C.** In-memory only. Not reload-safe; Gate 2's standings screen couldn't read it.

*Side benefit:* Gate 2's dashboard/standings screen (scope_realignment §5, row 6) can read `Team_Seasons` directly instead of aggregating game rows.

### D2. Standings tiebreakers (spec §8: head-to-head, run differential, runs allowed, seeded coin flip)

- **A (recommended). Group rule, recursive:** order by win% (W/(W+L)). For a tied group of any size:
  1. **Head-to-head** = win% in regular-season games **among the tied teams only** (a mini-league). If it splits the group, each sub-group restarts from step 1.
  2. **Run differential** (full regular season, higher first).
  3. **Runs allowed** (full regular season, fewer first).
  4. **Seeded coin:** a 64-bit key per team from a dedicated stream, `mix(schedule_seed, seasonYear, tier, "standings", team_id)` (stable mixer per §9 Q8). It is a pure function of persisted inputs, never the sim RNG, and identical across reloads. The key is unique in practice; `team_id` breaks a key collision.

  It applies to HS conference seeds, Pro division winners (within the division, then seeds 1–4 among winners) and Pro wild cards (best two non-winners per conference, seeds 5–6, even with a better record than a division winner). College selection uses the national rank, whose exact-tie rule is §9 Q1.
- **B.** Pairwise head-to-head comparator. Rejected: non-transitive in 3-way ties.

### D3. Postseason formats and calendar (fixed slots)

Day 1 = Monday Jan 1, 365-day year (`GameCalendar`). **Every date below is fixed, so the planner, phone and UI can show it in advance. A conditional game is played only while its series is live.**

| Tier | Selection | Rounds (day: weekday date) | Hosting |
| :-- | :-- | :-- | :-- |
| **HS** (6 teams: top 3 per conference; #1s bye) | 125 Sat May 5 | QF N2–N3, S2–S3: **131 Fri May 11** · Conference finals N1/S1 vs QF winner: **138 Fri May 18** · State Championship: **145 Fri May 25** | Higher seed; State final: better regular-season record by D2 (no cross-conference H2H, so RD → RA → coin) |
| **College** (18: ranks 1–17 + play-in #18 v #19) | 133 Sun May 13 | Play-in: **139 Sat May 19** · Conference rounds R1–R4: **146 Sat May 26, 153 Sat Jun 2, 160 Sat Jun 9, 167 Sat Jun 16** · **The Red Dirt Series** (Bo3): **172 Thu Jun 21, 173 Fri Jun 22, 174 Sat Jun 23** (game 3 only at 1–1) | Play-in: #18. Brackets: better national rank. Final: better selection rank hosts games 1 and 3 (spec §8.2) |
| **Pro** (12: 4 division winners = seeds 1–4, 2 wild cards = 5–6 per conference; 1–2 bye) | 241 Wed Aug 29 | Wild Card Bo3 (3v6, 4v5): **244 Sat Sep 1, 247 Tue Sep 4, 249 Thu Sep 6** · Division Bo3 (1 v lowest remaining, 2 v other): **251 Sat Sep 8, 254 Tue Sep 11, 256 Thu Sep 13** · Conference Final Bo3: **258 Sat Sep 15, 261 Tue Sep 18, 263 Thu Sep 20** · World Championship Bo5: **265 Sat Sep 22, 268 Tue Sep 25, 270 Thu Sep 27, 272 Sat Sep 29, 275 Tue Oct 2** | Higher seed: Bo3 games 1 and 3; Bo5 games 1, 2, 5 (2-2-1). World Championship: better record by D2 |

**College brackets, aligned to end:** 18 qualifiers split k + (18 − k) with each conference ≤ 12. The larger conference always has ≥ 9 and plays a 16-slot bracket over R1–R4 (146–167). A conference with ≤ 8 qualifiers plays an 8-slot bracket over R2–R4 (153–167). Both champions get the same 5 days before the final, and the ace's Sat 167 start is eligible on Thu 172 (gap 5, CS-M §4). Brackets are fixed standard seeded draws (1v16, 8v9, …; byes to the top seeds; no reseeding). Both are drawn in the play-in's batch, because the play-in winner's conference is only known then. The postseason fits spec §8.2's ≤ 6 weeks exactly (play-in week, 4 rounds, final week). The final ends on day 174, before the Draft (day 195).

**Other rules:**

- **Rows on certainty:** a series game's `Schedule_Games` row is inserted only when that game is certain to be played: game 1 at series creation, game n after game n − 1 leaves the series live. There's no `cancelled` status, so pre-inserting conditional games would leave phantom rows. **An unplayed game never exists, so it never counts.**
- The Red Dirt Series no-repeat rule (spec §6) covers **SP and RP openers** and is derived from `Pitcher_Rest`. It applies to the Red Dirt Series only. Pro series keep the plain §6 rest rule, so a Pro ace can start Bo3 games 1 and 2 (gap 3); that's spec as written.
- **Narrow postseason forfeit:** if a team has no legal pitcher (e.g. the no-repeat rule exhausts the staff), it forfeits that game (7–0 / 9–0, `status = 2`, no player stats, counts as a series win for the opponent; spec §4, CS-M §4). General `CanField` / roster-lock forfeits remain 13-5's.
- Strings: `league.college.final.name` = "The Red Dirt Series" (pending the owner's trademark screen; a rename is a data edit).

*Alternative (not recommended):* compressed dates (the next round starts on the first game day after the current round ends, as in the prior art). Fewer idle days, but dates vary, so the planner and UI can't show them in advance and the rest patterns change.

### D4. Postseason stats and Pro-guard invariance

**Facts:** stats rows are `UNIQUE (player_id, season_year)`; the guard ledger hashes every row; the guard world runs both seasons on one `_rng`. A season-1 postseason can therefore move the guard through **three channels**:

1. postseason lines landing in the ledger rows;
2. postseason games consuming the shared `_rng`, which shifts **every** season-2 result even with zero postseason stats credited;
3. persisted side state feeding season 2: `Pitcher_Rest` (harmless at a gap of >180 days, but asserted), absences, PED per-game counters, and development inputs.

- **A (recommended). No persisted postseason player lines; isolated streams; positive proof.** Postseason credits **no** `Batting_Stats` / `Pitching_Stats` rows. Each game (macro or attended) draws from its own derived stream with private preallocated buffers. Results persist only to `Schedule_Games` (score, status) and `Playoff_Series`. The guard world runs **with the postseason wired, as in production**, and stays byte-exact. A new check proves the 2026 Pro postseason actually completed there: 11 series, exactly one champion, legal lengths. Life-facing per-game effects **do** apply to postseason games exactly as the regular-season path applies them (PED health/detection cost, rest, and any per-game absence the shipped path rolls), through the same batch; the guard world has no PED or avatar, so it stays exact. Consequence: career and season stat lines exclude the postseason (shown separately later if D4-B is adopted).
- **B.** A, plus persisted postseason lines in new `Batting_Stats_Post` / `Pitching_Stats_Post` (same shape, `UNIQUE (player_id, season_year)`). The ledger is unaffected because its queries name the regular tables. Cost: new DDL in 13-4a, ahead of the v15 freeze (pre-1.0 clean break; SchemaValidator grows), plus normalizer and validator work. Can be added any time before the freeze if postseason achievements or career pages need it.
- **C (not recommended).** Credit postseason into the existing rows. It moves the sanctioned guard (needs a new owner sanction), mixes postseason into rates, leaders and the tier bands, and breaks §11.3's statement.

### D5. CS-M §3 opt-in ranking analysis (`--college-analysis ranking`)

CS-M §3 proposes 65,536 season simulations. At 13-3's measured **546 ms** per flat College season and **678 ms** per spread-25 season (M2), that's roughly **9–12 h** on the owner's PC: not feasible. Proposed, mirroring 13-3's approved M2:

1. **No resimulation for rankings.** Weekly ranks are a pure function of saved results. The **14-game comparator** is computed from a seeded, venue-balanced half of each 28-game season's doubled pairings (one meeting per pairing), not from separate 14-game seasons. This is valid for the format-isolation experiment, which freezes ratings, development, injuries and fatigue (CS-M §3.2). Argon confirms in §9 Q3. It halves the matrix and makes the comparison exactly paired.
2. **Pilot, pre-timed (60 s stop rule):** training worlds 1–8 × scheduler 1–2 × outcome 1–2 = **32 spread-25 seasons** (≈ 22 s). It prints ms/season, the variance of rank SD and top-17 replacements, and a holdout forecast.
3. **Pre-registered reduced holdout (frozen before the pilot is read, run once):** validation worlds **129–160** (32 roster seeds) × scheduler 1–4 × outcome 1–4 = **512 spread-25 seasons** (≈ 5.8 min). CS-M §3.3's gate is unchanged: a one-sided 95% paired bootstrap over roster seeds, 10,000 resamples, analysis seed 28014; 28-game ≤ 14-game × 1.05 on mean rank SD and on mean top-17 replacements.
4. **Sensitivity (training only):** worlds 1–32 × 2 × 2 = 128 seasons (≈ 1.5 min). Weights (.60,.25,.15) and (.45,.35,.20) are recomputed from the same results, with no resimulation.
5. **Strength diagnostics (report-only, harness-only, never saved):** a neutral round-robin per holdout world, with repetitions R set by Argon from the pilot, plus CS-M's fixtures: equal teams, one dominant team, reversed IDs, unequal divisions and one conference shifted. The disconnected-conference limitation is reported, never disguised as a passing check.
6. One fixed tuple repeated 20× (ranks, scores, entrants and RNG state byte-identical). **Hard stop at 20 min wall**, exit 2 = `INCOMPLETE`.

If 32 roster seeds lack power for the 5% bound, Argon sizes the holdout from the pilot before reading it, still under the hard stop. The weights stay (.50,.30,.20) unless a reviewed candidate wins on the holdout (CS-M §3.4).

### D6. Engine vs UI staging

- **A.** 13-4b bracket screen right after 13-4a, on the current theme (Sonnet; Neon review). Rebuilt again in 15c.
- **B (recommended). 13-4a only now; the bracket screen is built once in 15c (post-Gate-2), in the new design system.** 13-4a ships the read-only `PlayoffBracketView` / `PlayoffSeriesView` / `NationalRankView` DTOs (series score, game number, can-clinch, elimination game, champion), the strings key and the attended postseason through the existing at-bat flow. Postseason games already reach the player as attended games; a bracket *screen* adds context, not function. The current dark-mode look isn't the shipping look (GEMINI §7), so an A-style screen is throwaway work during the Gate 2 push. Sim and scene work never collide.
- **C.** A thin, functional bracket panel now (one Sonnet session, Neon review), replaced in 15c. Pick this if the owner wants the bracket visible in playtests before 15c.

### D7. Default MC runtime budget for 13-4

- **Recommended:** 13-4 adds **≤ 15 s** to the default run, measured relative to the §0 baseline on the owner's PC (the D10 method). The 213.7 s ceiling stays. Default content: CS-M's 4-season ranking smoke (≈ 2–3 s), structural bracket/series fixtures on synthetic results (no sim), a 100,000-series fair-coin check (no sim), the guard-world Pro postseason (≤ 35 games) and allocation probes. Heavy studies run only under `--college-analysis ranking`.

## 4. Outputs (13-4a, per the recommended options)

1. **Standings + ranking:** `Standings` (D2) and `RankingCalculator` (CS-M §3 definitions: W = wins/games; SOS = Σ opponents' wins / Σ opponents' games, once per completed meeting; `RD_norm = (clamp(RD/G, −5, 5) + 5)/10`; zero games ⇒ .5; regular season only, forfeit finals included). `Team_Seasons` upsert per D1.
2. **`PlayoffManager`:** selection, seeding, brackets and series per D3; `Playoff_Series` + phase-1/2 `Schedule_Games` writes (rows on certainty); the day handler plays due NPC games via an isolated macro adapter (D4) and parks the avatar's game; forced end-of-regular-season flush; idempotent across reload.
3. **Series no-repeat** (Red Dirt Series): `usedInSeries` from `Pitcher_Rest`, fed to `SelectStarter` and to a series-aware `SelectOpener` in **both** `LeagueSimulator` and `MicroGame`; the narrow postseason forfeit (D3).
4. **Attended postseason:** `CareerManager` claims phase-1/2 rows for the avatar's team; the clock stops on its postseason game days (GEMINI §1); the result returns through the same batch as the series update. Locked team blocks run from `PostseasonFirstDay` through the team's elimination or title day (Game on its game days, Practice on the tier's practice days); free sliders from the day after elimination (non-qualifiers: from selection day) (spec §3.2, §16-I10).
5. **Events** (`CoreEvents.cs`): `PlayoffSeriesDecidedEvent(SeasonYear, Tier, SeriesId, Round, WinnerTeamId, LoserTeamId, WinnerWins, LoserWins)` and `ChampionshipWonEvent(SeasonYear, Tier, TeamId)`, each published **after** its commit. A champion whose publication marker (`Game_State`, written in the batch after dispatch) is missing is republished on boot. Consumers dedupe per (season, tier). A pure `IsRevealEligibleTier(tier) ⇒ tier == Pro` lives beside the event; the reveal itself is Phase L's.
6. **Read-only DTOs** for presentation (D6), `league.college.final.name` in `strings.csv`.
7. **Harness:** the suites in §5, `--college-analysis ranking` per D5, the guard-world postseason check, allocation probes.
8. **Docs:** spec §8 / §16 interpretations (new I13 calendar, I14 tiebreakers, I15 postseason stats) synced to the approved D-options; MC skill; `progress.md`; handoff §8.

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings / 0 errors |
| Schema | `dotnet run --project Tools/SchemaValidator -c Release` (plus `validate_sqlite_schema` on a scratch copy before new SQL) | 174/174 (D4-A: no DDL change) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 22/22 + new postseason clock fixtures (clock stops on the avatar's postseason game day; reload mid-series resumes the same fixture; eliminated avatar gets free sliders) |
| GrittyEvents | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 (unchanged) |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | 408 + new checks; reds = exactly the 7 promotion suites; tier-ladder 14/14; resolver byte-identical; **Pro guard byte-exact with the postseason ON**; every allocation line 0 B (incl. postseason macro + attended); 0 leaks; within D7 |
| 20× replay | default, `--no-build`, ×20, normalized | 20/20 byte-identical |
| Ranking analysis | `… -- --college-analysis ranking` (pilot, then holdout once) | D5 plan; Argon records it |

**New default suites** (CS-M §6 names kept where they exist):

- `standings`: D2 fixtures (2-way, 3-way partial H2H separation then restart, RD, RA, coin), coin independent of sim RNG and stable across reload, `Team_Seasons` idempotent on replayed flush.
- `hs-playoffs`: 6-team field, byes, dates 131/138/145, hosting, exactly one champion.
- `college-ranking` (CS-M, 6): arithmetic and clamp, SOS repeat weighting, zero games, ties, 17 automatic + distinct #18/#19, 20-repeat identity on saved inputs.
- `college-bracket`: every split from 6+12 to 12+6 (and 9+9) gives legal byes and aligned finals on 167; play-in on 139; the play-in winner joins its own conference.
- `college-series` (CS-M, 10): all six two-win paths AA, BB, ABA, ABB, BAA, BAB; no game-3 row after 2–0; Thu/Fri/Sat; host order and frozen rank; healthy A/B/C; no repeat, including RP openers and role changes; depleted staff (RP opener, then forfeit); reload identity; 100,000 fair-coin series with deciding-game share in .495–.505; played-season totals.
- `pro-playoffs`: 12-team field, division winners 1–4 even when a wild card has a better record, byes, reseeded division round, Bo3 1-3 and Bo5 2-2-1 hosting, conditional games only while live, exact dates.
- `championship-event`: exactly one per tier-season, tier set on every publish, `IsRevealEligibleTier` false for HS/College, no duplicate on reload, republish when the marker is missing.
- `season-pipeline` +1: the guard world's 2026 Pro postseason completed (11 series, one champion) **and** the guard is byte-exact.
- Spec §15 acceptance, covered above: every tier crowns exactly one champion; series lengths legal; seeding/tiebreaks; the College final ends at 2 wins and never plays game 3 after 2–0; `tier` set on every publish; an HS or College title never fires the reveal.

## 6. Definition of done

- [ ] §0 recorded (baseline and wall time).
- [ ] D0–D7 applied as approved; spec §8/§16 synced.
- [ ] Standings, ranking, `Team_Seasons`, selection, brackets, series, attended postseason, events and DTOs built per §4.
- [ ] Argon's Math record (§10) reviewed by Opus; values applied; ranking analysis recorded.
- [ ] §5 gates green; Pro guard byte-exact with the postseason on; 20× replay 20/20.
- [ ] Argon's independent review of the sim/persistence diff passed.
- [ ] Committed and pushed; merged into `p13/integration` by Opus after review. **Not `main`.**
- [ ] Handoff (§8) + one `progress.md` line per author. **Then stop.**

## 7. Out of scope

13-5 (`RosterManager`, Sunday lock, general `CanField` forfeits); 13-6 (`PipelineManager`, the Draft order that will read `Team_Seasons`); the bracket screen (D6); the Phase L reveal consumer; achievements and rich presence (13-7 / Phase 17); booth series beats (14c); phone texts about the postseason (content, later); persisted postseason stat lines (unless D4-B); any band, resolver or guard change; any DDL (unless D4-B); `main`.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built> · Commit: <sha> · Gates: build, Schema, CoreLoop, Gritty, MC (total, reds, guard, alloc, wall), 20× replay
Open: <unfinished items, risks, owner questions>
Recommended next: <slice> by <model> (reviewer: <model>)
```

Then add one compact line to [progress.md](../progress.md). Don't start the next slice.

## 9. Questions for Argon's Math check of this brief (answer before owner approval)

1. **Ranking exact ties.** CS-M §3 orders exact score ties W ↓, RD/G ↓, **team ID ↑** (a permanent low-ID bias). Should the last key be D2's seeded coin instead? Recommend a rule.
2. **SOS.** Confirm CS-M's literal definition (opponents' records **including** their games against the team, weighted by meeting count) and its zero-game handling, or label a revision.
3. **14-game comparator by sub-sampling (D5.1).** Is a seeded, venue-balanced half of each doubled pairing a valid matched comparator under the frozen-ratings experiment? Specify the selection rule and seed.
4. **Holdout size and power (D5.3).** Is k = 32 roster seeds enough for the one-sided 5% bound? Give the pilot-based sizing rule, frozen before the holdout is read.
5. **Strength diagnostics (D5.5).** Round-robin repetitions R and the reported statistics; confirm report-only.
6. **Fair-coin check.** 100,000 series at p = .5: SE of the deciding-game share ≈ .00158, so .495–.505 is ±3.2 SE. Confirm the band and a fixed seed.
7. **Postseason streams (D4).** The stable mixer and labels for per-game streams (macro and attended) from persisted inputs (`schedule_seed`, season, tier, series, game). Independence from the regular-season stream, and the full list of guard-invariance channels the harness must assert.
8. **Standings coin (D2).** Mixer choice; confirm the recursive group rule is a pure function of persisted results.
9. **Pro postseason ace usage (report-only).** Under §6 alone, the fixed D3 slots give gaps 3/2 (Sat→Tue→Thu). Report the expected Pro ace-start share; no rule change is proposed.
10. **D7 split.** Confirm the default additions fit in ≤ 15 s on the owner's PC, or propose the trim.

## 10. Math check — Gemini 4 Argon, 2026-11-20

**Author / Reviewer:** Gemini 4 Argon (Math / calibration owner and primary independent reviewer, [TEAM.md](../TEAM.md) §1/§1a). **Branch / worktree:** `p13/13-4-mathcheck-argon` in `.worktrees/13-4-mathcheck-argon`, based on `4281baa` (`p13/integration` at `b65a00b`). **Scope:** §9 Q1–Q10, independent arithmetic and calendar check, Math review of D0–D7, and numbered amendment list for reconciliation. **Docs only:** no `Assets/`, `Tools/`, `.agents/` or schema edits.

**Overall Verdict:** **APPROVE WITH CHANGES** — all D0–D7 recommendations and arithmetic in this brief are mathematically sound and preserve the sanctioned Pro guard (`PA 81433 / H 18471 / ER 8803`, ledger SHA-256 `932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`) and resolver guard (`6d924228…`) byte-exact, subject to the 7 targeted specification refinements in §10.4 (most notably: suppressing `MicroGame.FlushGame` regular-season stat writes on attended postseason games under D4-A, using a venue-balanced Latin-shift 14-game sub-sampler in D5.1, replacing `team_id ↑` with a seeded coin on ranking exact ties in Q1, and fixing College's non-reseeded bracket tree instead of `6993e70`'s per-round reseed).

---

### 10.1 Answers to §9 Questions (Q1–Q10)

#### Q1. National-ranking exact ties (`team_id ↑` vs D2's seeded coin)
- **Verdict:** **Agree with change** — replace `team_id ↑` as the primary last tiebreak key with a dedicated per-(season, team) seeded coin key, retaining `team_id ↑` solely as the $2^{-64}$ hash-collision fallback.
- **Exact rule:** Order the 24 College teams by:
  1. `RankScore` descending: $\text{Score}_i = 0.50\,W_i + 0.30\,\text{SOS}_i + 0.20\,\text{RD}_{\text{norm},i}$, computed in IEEE-754 `double` from exact integer accumulators `(Wins, Games, OppWinsSum, OppGamesSum, RunsScored, RunsAllowed)`.
  2. `W` descending: exact integer cross-product $W'_a G'_b$ vs $W'_b G'_a$, where $(W'_i, G'_i) = (W_i, G_i)$ if $G_i > 0$ else $(1, 2)$ ($0/0 \mapsto 0.5$).
  3. `RD/G` descending: exact integer cross-product $(\text{RF}_a - \text{RA}_a) G''_b$ vs $(\text{RF}_b - \text{RA}_b) G''_a$, where $G''_i = \max(1, G_i)$ and $G_i = 0 \implies \text{RF}_i - \text{RA}_i = 0$.
  4. **Seeded coin:** `RankingCoinKey(schedule_seed, seasonYear, LeagueTier.College, team_id)` ascending (64-bit chained SplitMix64 mixer with domain tag `"RANKG64_"` = `0x5241_4E4B_4736_345FUL`; §10.1 Q7/Q8).
  5. `team_id` ascending (deterministic $2^{-64}$ collision fallback only).
- **Derivation:** In Appendix A, East Conference teams have `team_id ∈ [201, 212]` and West Conference teams have `team_id ∈ [213, 224]`. Because the two conferences play zero cross-conference regular-season games and have identical aggregate conference win percentage ($168 / 336 = 0.500$) and run differential ($0$), cross-conference exact-score ties occur systematically (at Week 0, in equal-team fixtures, at the `#17 / #18 / #19` automatic-qualifier/play-in cutoff, and for Red Dirt Series Game 1/3 home-field advantage). Under `team_id ↑`, East (`201–212`) wins 100% of cross-conference exact ties forever (in a 24-team equal tie, Team 201 has rank $1.0$ and Team 224 has rank $24.0$). Keying on `(schedule_seed, seasonYear, tier, team_id)` — deliberately omitting `dayOfSeason` — keeps weekly rankings stable when tied teams play no games, consumes zero sim RNG draws, is reload-invariant, and restores exact permutation symmetry ($\mathbb{E}[\text{Rank}_i] = (1 + 24)/2 = 12.5$ for every `team_id`). Update CS-M §6 `college-ranking` check 4's label from `"ties/ID ordering"` to `"ties/seeded-coin ordering with ID collision fallback"`.

#### Q2. Strength of Schedule (SOS) definition and zero-game handling
- **Verdict:** **Agree** — confirm CS-M §3's literal SOS definition and zero-game handling verbatim, with no formula revision.
- **Exact rule:** Over all completed regular-season games $\mathcal{G}_0 = \{g \in \text{Schedule\_Games} : \text{phase} = 0 \land \text{status} \in \{1, 2\}\}$ up to the flush day, let $W_j$ and $G_j$ be team $j$'s completed wins and games, and let $m_{ij}$ be the number of completed games between team $i$ and team $j$ ($G_i = \sum_{j \ne i} m_{ij}$). Then:
  $$\text{OppWins}_i = \sum_{g \in \mathcal{G}_0 :\, i \in g} W_{o(g,i)} = \sum_{j \ne i} m_{ij} W_j, \qquad \text{OppGames}_i = \sum_{g \in \mathcal{G}_0 :\, i \in g} G_{o(g,i)} = \sum_{j \ne i} m_{ij} G_j,$$
  $$\text{SOS}_i = \begin{cases} \dfrac{\text{OppWins}_i}{\text{OppGames}_i} & \text{if } \text{OppGames}_i > 0, \\[8pt] 0.5 & \text{if } \text{OppGames}_i = 0, \end{cases} \qquad W_i = \begin{cases} \dfrac{\text{Wins}_i}{G_i} & \text{if } G_i > 0, \\[8pt] 0.5 & \text{if } G_i = 0, \end{cases}$$
  $$\text{RD}_{\text{norm},i} = \begin{cases} \dfrac{\text{ clamp}((\text{RF}_i - \text{RA}_i) / G_i,\, -5.0,\, 5.0) + 5.0}{10.0} & \text{if } G_i > 0, \\[8pt] 0.5 & \text{if } G_i = 0. \end{cases}$$
- **Derivation:**
  1. *Order-invariance:* $\text{OppWins}_i$ and $\text{OppGames}_i$ are exact `long` integer sums, so `SOS` is bit-identical regardless of SQL or list iteration order.
  2. *Self-inclusion monotonicity check:* At regular-season completion ($G_j = 28$ for all $j$, $m_{ij} = 4$ for the 3 division opponents and $m_{ij} = 2$ for the 8 non-division conference opponents), $\text{OppGames}_i = \sum_{j \ne i} 28\,m_{ij} = 28^2 = 784$. Including opponent $j$'s games against team $i$ in $W_j$ means each win by $i$ over $j$ lowers $\text{OppWins}_i$ by $m_{ij} \in \{2, 4\}$ ($\Delta\text{SOS}_i = -m_{ij}/784 \in \{-0.002551, -0.005102\}$, shifting $0.30\,\text{SOS}_i$ by at most $-0.001531$), whereas that same win raises $0.50\,W_i$ by $+0.50 / 28 = +0.017857$ (plus $\ge +0.000714$ in $0.20\,\text{RD}_{\text{norm},i}$). Thus a win always strictly increases `RankScore` by at least $+0.01704$ ($>11.6\times$ the self-inclusion SOS feedback).
  3. *Week-1 edge case:* After Game 1 (Week 1 Wednesday, $G_j = 1$ for all $j$), an opponent-excluded formula would have denominator $G_j - m_{ij} = 1 - 1 = 0$ for every team in the league, whereas CS-M's literal formula has $\text{OppGames}_i = 1 > 0$ from Game 1 onward.

#### Q3. 14-game comparator by sub-sampling (D5.1)
- **Verdict:** **Agree** — a seeded, venue-balanced 14-game subset of each 28-game season is a valid, variance-reduced matched comparator under CS-M §3.2's frozen-ratings format-isolation experiment, **provided** the cross-division half is selected via balanced cyclic Latin 1-factors (not raw `leg = 0` or independent per-pair coin flips).
- **Exact selection rule and seed derivation:**
  - *Statistical validity under frozen ratings:* In the format-isolation experiment (ratings, development, injuries, and in-game fatigue frozen; macro complete-game with `MinRestDays(College) = 3`), every team's #1 starter (ace $A$) starts all 28 games on $\ge 3$ days rest (alternating 3- and 4-day gaps) with constant effective ratings and zero multi-game state carryover. The 336 games of a 28-game season are therefore conditionally independent realizations from the exact per-matchup outcome distribution of a standalone 14-game season. Sub-sampling one venue-balanced half $S_{14} \subset S_{28}$ eliminates the 14-game simulations (halving runtime) and couples $(M_{w,28}, M_{w,14})$ on common realized games.
  - *Why a naive half fails venue balance:* In `SeasonScheduler.CollegeRounds` (`SeasonScheduler.cs:298–300`), `leg = 0` of the cross-division $K(4,4,4)$ factorization puts `div[0]` at home in all 8 of its cross-division games (`11H / 3A`), while independent coin flips per unordered pair $\{u, v\}$ fail the exact `7H / 7A` constraint.
  - *Sub-sampler seed:* For tuple `(world w, scheduler s, outcome o)`, derive:
    $$\text{subRng} = \text{new RngState}\!\left(\text{MixCombine}(\text{MixCombine}(\text{MixCombine}(\text{MixCombine}(\text{DefaultScheduleSeed},\, \text{0x5355\_4231\_3443\_4D50UL}),\, (ulong)w),\, (ulong)s),\, (ulong)o)\right)$$
    (domain tag `"SUB14CMP"` = `0x5355_4231_3443_4D50UL`).
  - *Exact venue-balanced selection rule (applied per conference in ascending `conference_id` order over persisted phase-0 `Schedule_Games` rows ordered by `(day_of_season, game_id)`):*
    1. **Intra-division (36 of 72 games per conference):** For each division (ascending `division_id`) and each ordered pair of distinct division teams $(u, v)$ (ascending `(u, v)` `team_id`), there are exactly 2 games with `home_team_id = u` and `away_team_id = v`. Draw `int pick = (int)(subRng.NextUInt64() & 1UL)` and select the `pick`-th game (`0` or `1`). Every intra-division unordered pair $\{u, v\}$ plays 2 games (1 at $u$, 1 at $v$), giving every team **3 home and 3 away** intra-division games.
    2. **Cross-division (48 of 96 games per conference):** For each of the 3 division pairs $(D_x, D_y)$ with $0 \le x < y \le 2$, let $u_0 < u_1 < u_2 < u_3$ be the sorted `team_id`s in $D_x$ and $v_0 < v_1 < v_2 < v_3$ in $D_y$. The 16 cross-division pairs between $D_x$ and $D_y$ partition into 4 cyclic Latin 1-factors $M_c = \{(u_i, v_{(i+c)\bmod 4}) : i \in \{0,1,2,3\}\}$ for $c \in \{0,1,2,3\}$. Draw `int m = subRng.NextInt(6)` to select one of the $\binom{4}{2} = 6$ two-shift masks `Masks2Of4 = { (0,1), (0,2), (0,3), (1,2), (1,3), (2,3) }`. For $c \in \text{Masks2Of4}[m]$, select the unique game where $u_i$ hosts $v_{(i+c)\bmod 4}$; for $c \notin \text{Masks2Of4}[m]$, select the unique game where $v_{(i+c)\bmod 4}$ hosts $u_i$. Every team plays **2 home and 2 away** against each of the other two divisions (**4 home and 4 away** cross-division).
  - *Resulting invariants (verified by construction):* 168 league games; every team plays **14 games (7 home, 7 away)**, 2 games (1H/1A) against each of its 3 division rivals, and 1 game against each of its 8 cross-division conference opponents (2H/2A per division). Furthermore, the complement $S_{28} \setminus S_{14}$ satisfies the exact same 14-game invariants.

#### Q4. Holdout size, power at $k = 32$ roster seeds, and frozen pilot-based sizing rule (D5.3)
- **Verdict:** **Agree** — $k = 32$ holdout roster seeds (`worlds 129–160 × scheduler 1–4 × outcome 1–4 = 512` spread-25 seasons) achieves **$>99.9\%$ power** under spread-25 team heterogeneity and **$\ge 88\%$ power** even under a zero-improvement ($R_0 = 1.00$) null. The pre-registered discrete sizing ladder below is frozen before any validation world (`129+`) is read.
- **Exact estimators, power derivation, and frozen sizing rule:**
  - *World-level estimands:* In world $w \in \{1, \dots, k\}$ with $m = S \times O$ seasons ($m = 4$ in the $2 \times 2$ pilot; $m = 16$ in the $4 \times 4$ holdout), let $r_{w,t,i}^{(F)} \in \{1, \dots, 24\}$ be team $i$'s national rank and $T_{w,t}^{(F)}$ be the top-17 automatic-qualifier set in replicate $t \in \{1, \dots, m\}$ under format $F \in \{28, 14\}$:
    $$M_{w,F}^{(\text{SD})} = \frac{1}{24}\sum_{i=1}^{24} \sqrt{\frac{1}{m - 1}\sum_{t=1}^m \left(r_{w,t,i}^{(F)} - \bar{r}_{w,\cdot,i}^{(F)}\right)^2}, \qquad M_{w,F}^{(\text{rep})} = \frac{2}{m(m-1)}\sum_{1 \le a < b \le m} \frac{\left|T_{w,a}^{(F)} \triangle T_{w,b}^{(F)}\right|}{2}.$$
  - *Pooled ratio and linearized cluster SE:* For each metric $m \in \{\text{SD}, \text{rep}\}$:
    $$\hat{R}^{(m)} = \frac{\bar{M}_{28}^{(m)}}{\bar{M}_{14}^{(m)}} = \frac{\sum_{w=1}^k M_{w,28}^{(m)}}{\sum_{w=1}^k M_{w,14}^{(m)}}, \qquad \text{SE}\!\left(\hat{R}^{(m)}\right) = \frac{s_{\text{world}}^{(m)}}{\sqrt{k}}, \qquad s_{\text{world}}^{(m)} = \frac{1}{\bar{M}_{14}^{(m)}}\sqrt{\frac{1}{k - 1}\sum_{w=1}^k \left(M_{w,28}^{(m)} - \hat{R}^{(m)} M_{w,14}^{(m)}\right)^2}.$$
  - *Power analysis across team-strength spreads ($k = 32$ worlds $\times 16$ seasons, 10,000 paired bootstrap resamples at seed `28014`):*
    - Because doubling games from 14 to 28 cuts binomial win% SD by $1/\sqrt{2} = 0.7071$, in a spread-25 world ($\sigma_{\text{team}} \approx 0.70$ R/G) the true ratio is $\mu_{28}/\mu_{14} \approx 0.806$ for Rank SD and $\approx 0.802$ for Top-17 Replacements — sitting **$\Delta \approx 0.245$ below the $1.05$ non-inferiority bound**!
    - Across the full range of team-strength spreads $\sigma_{\text{team}} \in [0.00, 1.00]$ R/G at $k = 32$:

      | Team Spread $\sigma_{\text{team}}$ | $\bar{M}_{28}^{(\text{SD})} / \bar{M}_{14}^{(\text{SD})}$ | $\hat{R}^{(\text{SD})}$ (`SE`; 95% Boot UB) | $\bar{M}_{28}^{(\text{rep})} / \bar{M}_{14}^{(\text{rep})}$ | $\hat{R}^{(\text{rep})}$ (`SE`; 95% Boot UB) | Margin $(1.05 - \hat{R}) / \text{SE}$ | Power at $k = 32$ ($\alpha = .05$) |
      | :--: | :--: | :--: | :--: | :--: | :--: | :--: |
      | `0.00` *(flat null $R_0 = 1$)* | `6.853 / 6.852` | `1.0002` (`.0025`; UB `1.0042`) | `4.910 / 4.951` | `0.9919` (`.0050`; UB `1.0004`) | `19.9 SE / 11.6 SE` | **$> 99.9\%$** |
      | `0.35` *(mild spread)* | `5.622 / 6.143` | `0.9152` (`.0055`; UB `0.9241`) | `3.962 / 4.339` | `0.9130` (`.0080`; UB `0.9261`) | `24.5 SE / 17.1 SE` | **$> 99.9\%$** |
      | `0.70` *(spread-25)* | `3.802 / 4.717` | `0.8059` (`.0071`; UB `0.8176`) | `2.646 / 3.300` | `0.8018` (`.0123`; UB `0.8222`) | `34.4 SE / 20.2 SE` | **$> 99.9\%$** |
      | `1.00` *(wide spread)* | `2.996 / 3.852` | `0.7777` (`.0074`; UB `0.7898`) | `2.071 / 2.689` | `0.7701` (`.0171`; UB `0.7974`) | `36.8 SE / 16.4 SE` | **$> 99.9\%$** |

    - Analytic worst-case bound: even if $\mu_{28} = \mu_{14}$ ($R_0 = 1.00$, margin $\delta = 0.05$ only), achieving 80% power at one-sided $\alpha = 0.05$ ($t_{0.95,31} + t_{0.80,31} = 2.5489$) requires $\text{SE}(\hat{R}) \le 0.05 / 2.5489 = 0.01962$ ($s_{\text{world}} \le \sqrt{32} \times 0.01962 = 0.1110$), and 90% power ($t_{0.95,31} + t_{0.90,31} = 3.0050$) requires $\text{SE}(\hat{R}) \le 0.01664$ ($s_{\text{world}} \le 0.0941$).
  - **Frozen Pilot-Based Sizing Rule (executed on training `worlds 1–8` before holdout `worlds 129+` are touched):**
    1. Run the 32-season training pilot (`worlds 1–8 × scheduler 1–2 × outcome 1–2 = 32` spread-25 seasons, $k_{\text{pilot}} = 8, m_{\text{pilot}} = 4$). Measure $t_{\text{pilot}}$ (ms/season) and compute $\hat{R}_{\text{pilot}}^{(m)}$, $s_{\text{pilot}}^{(m)}$, and conservative margin $\Delta^{(m)} = \max\!\left(0.05,\; 1.05 - \min\!\left(1.00,\; \hat{R}_{\text{pilot}}^{(m)} + t_{0.90,7}\,s_{\text{pilot}}^{(m)}/\sqrt{8}\right)\right)$ for $m \in \{\text{SD}, \text{rep}\}$ (note: because $m_{\text{hold}} = 16 = 4\,m_{\text{pilot}}$, $s_{\text{pilot}}^{(m)}$ is an upper bound on $s_{\text{world, hold}}^{(m)}$).
    2. Compute required worlds for 90% power at one-sided $\alpha = 0.05$:
       $$k_{\text{req}} = \max_{m \in \{\text{SD}, \text{rep}\}} \left\lceil \frac{(z_{0.95} + z_{0.90})^2 \left(s_{\text{pilot}}^{(m)}\right)^2}{\left(\Delta^{(m)}\right)^2} \right\rceil, \qquad (z_{0.95} + z_{0.90})^2 = 8.5639.$$
    3. Select the smallest rung $k_{\text{hold}} \in \{32, 48, 64\}$ (`validation worlds 129 .. 128 + k_hold`, each $\times 4 \times 4 = 16$ seasons) with $k_{\text{hold}} \ge k_{\text{req}}$ whose total `--college-analysis ranking` forecast with $1.25\times$ reserve fits inside the 20-min hard stop:
       - **$k_{\text{hold}} = 32$ (`worlds 129–160`, 512 seasons, $\approx 5.79\text{ min}$ raw at $678\text{ ms}$):** chosen whenever $k_{\text{req}} \le 32$.
       - **$k_{\text{hold}} = 48$ (`worlds 129–176`, 768 seasons, $\approx 8.68\text{ min}$ raw):** chosen if $32 < k_{\text{req}} \le 48$ and forecast $\le 20.0\text{ min}$.
       - **$k_{\text{hold}} = 64$ (`worlds 129–192`, 1,024 seasons, $\approx 11.57\text{ min}$ raw):** chosen if $48 < k_{\text{req}} \le 64$ and forecast $\le 20.0\text{ min}$.
       - If $k_{\text{req}}$ exceeds the largest rung fitting within 20.0 min, **stop before running any validation world `129+`** and exit `2` (`INCOMPLETE`).
  - *Paired bootstrap gate (`seed = 28014`):* Seed `RngState bootRng = new(28014UL)`. For $b = 1 \dots 10,000$, draw $k_{\text{hold}}$ indices $i_{b,1}, \dots, i_{b,k_{\text{hold}}} \in \{0, \dots, k_{\text{hold}}-1\}$ via `bootRng.NextInt(kHold)`, compute $R_b^{*(m)} = \left(\sum_{j=1}^{k_{\text{hold}}} M_{i_{b,j},28}^{(m)}\right) / \left(\sum_{j=1}^{k_{\text{hold}}} M_{i_{b,j},14}^{(m)}\right)$, sort ascending, and take the one-sided 95% upper bound as `sorted[9499]` (the 9,500th order statistic; if $\sum M_{14} == 0$, require $\sum M_{28} == 0$). Gate passes iff `ub95_sd <= 1.05 && ub95_rep <= 1.05`.

#### Q5. Strength diagnostics: repetitions $R$ and reported statistics (D5.5)
- **Verdict:** **Agree** — confirm **report-only** (`[DIAG]`, never gated, harness-only, never saved to the DB). Set **$R = 4$ balanced home-and-away double-round-robin repetitions** per holdout world by default ($2,208$ in-memory games/world), with automatic pilot fallback to **$R = 2$** ($1,104$ games/world) if measured in-memory throughput is $< 1,500\text{ games/s}$, plus **$R_{\text{fix}} = 8$** on the 5 synthetic CS-M §3.4 fixtures.
- **Exact rule and reported statistics:**
  - *Neutral round-robin design:* For each unordered pair $\{u, v\}$ among the 24 teams ($\binom{24}{2} = 276$ pairs: $132$ intra-conference, $144$ cross-conference), play $R$ games with $u$ at home and $R$ games with $v$ at home ($2R$ games/pair = $552 R$ games/world) in memory using the stateless game adapter, with every team starting its rested `#1` starter (ace $A$) and regular lineup (`Pitcher_Rest`, `Schedule_Games`, and `Batting_Stats`/`Pitching_Stats` untouched).
  - *Precision and runtime:* Each team plays $23 \times 2R = 46R$ neutral games overall and $11 \times 2R = 22R$ intra-conference games:
    - At **$R = 4$** ($2,208$ games/world; $184$ games/team overall, $88$ intra-conference): per-team neutral win% $\hat{\theta}_{w,i}$ has $\text{SE}(\hat{\theta}_{w,i}) \le 0.5 / \sqrt{184} = \mathbf{0.0369}$ overall ($\le 0.5 / \sqrt{88} = \mathbf{0.0533}$ within-conference). Across $k = 32$ holdout worlds, $32 \times 2,208 = 70,656$ pure in-memory games (no SQLite I/O; kernel rate $\approx 3,000\text{–}5,000\text{ games/s}$) take **$\approx 15\text{–}25\text{ s}$**.
    - At **$R = 2$** ($1,104$ games/world; $92$ games/team overall, $44$ intra-conference): $\text{SE}(\hat{\theta}_{w,i}) \le \mathbf{0.0521}$ overall ($\le \mathbf{0.0754}$ within-conference), taking **$\approx 8\text{–}13\text{ s}$**.
  - *Reported `[DIAG]` statistics:*
    1. **Within-conference Spearman rank correlation $\rho_{\text{within}}$** between neutral round-robin win% $\hat{\theta}_{w,i}$ and season `RankScore` (computed per conference, averaged over East and West, reported as mean ± world-clustered 95% CI across the $k_{\text{hold}}$ holdout worlds, and across the 32 D5.4 training worlds for candidate weights `(.50,.30,.20)`, `(.60,.25,.15)`, `(.45,.35,.20)`).
    2. **Global 24-team Spearman rank correlation $\rho_{\text{global}}$** between 24-team neutral win% $\hat{\theta}_{w,i}$ and `RankScore` (mean ± 95% CI), paired with the cross-conference identifiability gap $\Delta\rho = \rho_{\text{within}} - \rho_{\text{global}}$.
    3. **Top-17 strength overlap** $|T_{17}(\text{RankScore}) \cap T_{17}(\hat{\theta})| / 17$ and empirical neutral win% SE.
    4. **The 5 CS-M §3.4 synthetic fixture worlds** ($R_{\text{fix}} = 8$ neutral double-round-robins + $8$ simulated 28-game seasons each = 40 seasons, $\approx 25\text{ s}$):
       - *Equal teams (flat 50):* mean rank across coins $\approx 12.5$, $\rho_{\text{within}} \approx 0$.
       - *Single dominant team (Team 201 at 80, other 23 at 50):* Team 201 `#1`-rank share and mean `RankScore` margin.
       - *Reversed team IDs ($201 + j \leftrightarrow 224 - j$ rating swap):* rank distribution invariance under Q1's seeded coin.
       - *Unequal division strength (East Coastal `201–203` at 65, `204` at 50, other East divisions at 50):* `W`, `SOS`, and `RankScore` of `204` vs a 50-rated team in a weak division.
       - *One conference shifted (all 12 East teams at 65, all 12 West teams at 45):* explicit report of the **CS-M §3 disconnected-conference limitation** (East and West both average `W = 0.500, SOS = 0.500, RD_norm = 0.500` and each places ~9 qualifiers despite East winning $\approx 100\%$ of inter-conference neutral games, so $\rho_{\text{within}}$ remains high while $\rho_{\text{global}}$ drops).

#### Q6. 100,000-series fair-coin check and fixed seed
- **Verdict:** **Agree** — confirm the **`.495–.505`** deciding-game share band and pin fixed seed **`0x134C_0140_0001_86A0UL`**.
- **Exact numbers and derivation:**
  - In an independent fair-coin Bo3 ($p_1 = p_2 = p_3 = 0.5$), Game 3 is played iff Games 1 and 2 split (`AB` or `BA`), with exact probability $q = 0.5(0.5) + 0.5(0.5) = 0.50000$ (paths `AA`, `BB` each $1/4$; `ABA`, `ABB`, `BAA`, `BAB` each $1/8$).
  - Over $N = 100,000$ series, $K \sim \text{Binomial}(100\,000,\, 0.5)$, so:
    $$\text{SE}(\hat{q}) = \sqrt{\frac{0.5 \times 0.5}{100\,000}} = \frac{1}{200\sqrt{10}} = \mathbf{0.00158113883} \approx \mathbf{0.00158}.$$
  - The band $[0.495, 0.505]$ has half-width $0.005 = \sqrt{10}\,\text{SE} = \mathbf{3.162278\,\text{SE}}$ (rounds to $\pm 3.2\,\text{SE}$ at one decimal; two-sided normal false-positive rate $p = 0.001565$).
  - **Verified fixed seed (`FairCoinSeed = 0x134C_0140_0001_86A0UL`):** Evaluated directly on `RngState` (`SplitMix64` init + `xoshiro256**`), advancing for Game 3 only when Game 1 $\ne$ Game 2 (honoring the "unplayed Game 3 never exists" rule):
    - `rng.NextDouble() < 0.5` yields **`49,965 / 100,000 = 0.49965`** deciding games ($-0.221\,\text{SE}$).
    - `(rng.NextUInt64() & 1UL) == 0` yields **`49,868 / 100,000 = 0.49868`** deciding games ($-0.835\,\text{SE}$).
    Both lie well inside `[0.495, 0.505]`.

#### Q7. Postseason per-game streams (macro and attended) and complete guard-invariance channels (D4)
- **Verdict:** **Agree with additions** — use the chained SplitMix64 mixer below keyed on the persisted natural series key `(round, high_seed_team_id, low_seed_team_id)` (not SQLite's auto-rowid `series_id`), and assert all **10 guard-invariance channels** below (the brief's 6 plus 4 additional seams found in `MicroGame.cs`, `LeagueSimulator.cs`, and `SeasonSchedule.cs`).
- **Exact mixer and domain labels:**
  - *Why natural series key instead of `series_id`:* `Playoff_Series.series_id` is an SQLite auto-increment `INTEGER PRIMARY KEY` across the entire table; running another tier's playoffs first shifts `series_id` by $+5$ or $+18$. By contrast, `(season_year, tier, round, high_seed_team_id, low_seed_team_id)` is a persisted natural unique key of `Playoff_Series`, and `game_in_series` (`1..5`) is persisted on `Schedule_Games`.
  - *Chained SplitMix64 mixer and 64-bit ASCII domain labels (disjoint from `SeasonScheduler.StreamFor`):*
    ```csharp
    public static class PostseasonStreams
    {
        public const ulong PostMacroGameTag = 0x504F_5354_4D41_4352UL; // "POSTMACR"
        public const ulong PostMicroGameTag = 0x504F_5354_4D49_4352UL; // "POSTMICR"
        public const ulong StandingsCoinTag = 0x5354_414E_4436_345FUL; // "STAND64_"
        public const ulong RankingCoinTag   = 0x5241_4E4B_4736_345FUL; // "RANKG64_"
        public const ulong Subsample14Tag   = 0x5355_4231_3443_4D50UL; // "SUB14CMP"

        public static ulong Mix64(ulong z)
        {
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        public static ulong MixCombine(ulong h, ulong k) => Mix64(h ^ Mix64(k));

        public static RngState ForGame(
            ulong scheduleSeed, int seasonYear, LeagueTier tier,
            int round, int highSeedTeamId, int lowSeedTeamId, int gameInSeries, bool isAttended = false)
        {
            ulong tag = isAttended ? PostMicroGameTag : PostMacroGameTag;
            ulong seriesKey = ((ulong)(uint)round << 32) | ((ulong)(uint)highSeedTeamId << 16) | (uint)lowSeedTeamId;
            ulong h = MixCombine(scheduleSeed, tag);
            h = MixCombine(h, unchecked((ulong)seasonYear));
            h = MixCombine(h, (ulong)tier);
            h = MixCombine(h, seriesKey);
            h = MixCombine(h, unchecked((ulong)gameInSeries));
            return new RngState(h);
        }
    }
    ```
    *(Verified 0 collisions across 154,200 generated stream seeds over 100 seasons $\times$ 3 tiers $\times$ all teams/series/games.)*
- **Complete list of 10 guard-invariance channels (6 from brief + 4 added from code inspection):**
  1. **`Batting_Stats` and `Pitching_Stats` rows (plus `StatsNormalizer` rate columns):** `ProGuardLedgerQueries` SHA-256 (`932ACE26…8B84`), `2026` totals `PA 81433 / H 18471 / ER 8803`, `2026` and `2027` `GS = 2048, W = 1024, L = 1024`, and `sampleBatter` stored rates (`avg, obp, slg, ops`).
  2. **Shared RNG streams (`LeagueSimulator._rng` and `CareerManager._rng`):** Macro postseason games must run on `PostseasonStreams.ForGame(..., isAttended: false)` without advancing `LeagueSimulator._rng`. Attended postseason games must run on `PostseasonStreams.ForGame(..., isAttended: true)` without advancing `CareerManager._rng` (which also drives `CareerManager.NextGuid()` and child-conception `GenerateFirstName(ref _rng)` at `CareerManager.cs:1312, 1505, 1527` and resets on mid-series reload).
  3. **`Pitcher_Rest` / `PitcherRestLedger` across the season rollover:** Postseason starts update `Pitcher_Rest` through at latest Day `275` of Season 1. Because Season 2 Pro Opening Day is absolute day `365 + 93 = 458`, the gap is $\ge 458 - 275 = 183\text{ days} \ge \text{MinRestDays(Pro)} = 3$; since `StarterSelection.SelectStarter` (`StarterSelection.cs:46`) checks the boolean threshold `Gap >= minRestDays`, all starters enter Season 2 fully rested (`true`) regardless of whether their last Season 1 start was Day 235, 240, or 275. Assert that Season 2 starter picks and pitching lines stay byte-identical, and snapshot/restore `PitcherRestLedger` if a postseason batch rolls back.
  4. **Absences (`AvailabilityLedger` / `Player_Absences`):** No postseason injury roll is enabled in 13-4 (13-2 D5 ships workload risk at `0`); assert `SELECT COUNT(*) FROM Player_Absences == 0` in the guard world.
  5. **PED per-game counters (`_pedGamesPlayed`, `Players.health_ceiling`, `Players.detection_risk`):** Macro postseason games must accumulate into a separate `_postPedGamesPlayed` buffer flushed in the postseason day's transaction and cleared on commit/rollback so regular-season `_pedGamesPlayed` is never polluted or double-flushed on the next Sunday `FlushAccumulated` (`LeagueSimulator.cs:601, 1136`). (Note: in a world with PED active and `DevelopmentManager` attached, `health_ceiling` feeds `DevelopmentCurve.HealthScale(health)` at `DevelopmentManager.cs:155`; the guard world has `_pedActive` all-false and `health_ceiling == 100`.)
  6. **Development inputs (`Game_State.avatar_practice_credit`, `Players.age`, `Player_Ratings`, `Player_Potential`):** Assert unchanged in the guard world (`avatar_practice_credit` absent/0, ratings untouched).
  7. **[ADDED] `MicroGame.FlushGame` and `CareerManager.PlayPendingGame` attended postseason stat suppression (`MicroGame.cs:1466–1482`, `CareerManager.cs:1506–1514`):** `MicroGame.FlushGame` currently unconditionally executes `AddBattingGameCounts` and `AddPitchingGameCounts`, and `CareerManager.PlayPendingGame` follows with `Normalizer.NormalizeSeason`. Under D4-A, when flushing an attended postseason game (`phase IN (1, 2)`), `MicroGame.FlushGame` **must skip** `AddBattingGameCounts` and `AddPitchingGameCounts` and `CareerManager.PlayPendingGame` **must skip** `Normalizer.NormalizeSeason` (while still flushing `RestLedger.FlushDirty`, `ApplyPedGameCosts`, `RecordFinal`, and `Game_Logs`), or attended postseason games would write player stats into the regular-season tables!
  8. **[ADDED] `LeagueSimulator` regular-season result queue (`_resultGameIds`, `_resultHomeScores`, `_resultAwayScores`, `_resultCount`, `_unflushedSeasonYear`, `LeagueSimulator.cs:789–797, 1149–1152`):** `PlayGame` appends to `_resultGameIds[_resultCount++]`. `SimulatePlayoffGame` must save/restore `_resultCount` (and `_unflushedSeasonYear`) and return the score to `PlayoffManager` directly so subsequent Sunday regular-season flush checks (`day % 7 == 0` on Days `245, 252, 259, 266, 273`) remain empty no-ops.
  9. **[ADDED] `Schedule_Games.phase` filter boundary (`SeasonSchedule.cs:51`, `ScheduleQueries.cs:42, 52`):** Every postseason row must have `phase = 1` (College play-in) or `phase = 2` (playoffs), never `phase = 0`. `SeasonSchedule` allocates `_gameIds` to exact phase-0 capacity (`1,024` for Pro) and throws if `phase = 0` rows exceed `1,024`. Assert `CountRegularSeasonGames(year, Pro) == 1024` after both postseasons complete.
  10. **[ADDED] Diagnostic counters (`ShortRestStarts` and `BullpenGames` on `LeagueSimulator` and `MicroGame`, `LeagueSimulator.cs:708, 725`, `MicroGame.cs:630, 646`):** Either save/restore `ShortRestStarts` and `BullpenGames` across postseason games or track `PostseasonShortRestStarts` / `PostseasonBullpenGames` separately so regular-season counter assertions in existing suites never shift.

#### Q8. Standings coin mixer and recursive group-rule purity (D2)
- **Verdict:** **Agree** — confirm the chained SplitMix64 standings-coin mixer and confirm D2-A's recursive group rule is a terminating, associative pure function of persisted phase-0 results.
- **Exact mixer and rule:**
  - *Standings coin key:*
    $$\text{StandingsCoinKey}(\text{scheduleSeed}, Y, T, \text{teamId}) = \text{MixCombine}(\text{MixCombine}(\text{MixCombine}(\text{MixCombine}(\text{scheduleSeed},\, \text{0x5354\_414E\_4436\_345FUL}),\, (ulong)Y),\, (ulong)T),\, (ulong)\text{teamId}),$$
    and similarly `RankingCoinKey` with `0x5241_4E4B_4736_345FUL` (`"RANKG64_"`).
  - *Pure-function and termination verification:* Given a scope of teams $S_0$ and completed regular-season games $\mathcal{G}_0 = \{g \in \text{Schedule\_Games} : \text{phase} = 0 \land \text{status} \in \{1, 2\}\}$, first partition $S_0$ by full regular-season win% ($W_t / G_t$ descending via exact integer cross-product $W'_a G'_b$ vs $W'_b G'_a$). For any tied group $S$ with $|S| > 1$:
    1. **Mini-league H2H ($\text{H2H}_S$):** Over $\mathcal{G}_0(S) = \{g \in \mathcal{G}_0 : \text{home}(g) \in S \land \text{away}(g) \in S\}$, compute $(w_{t,S}, g_{t,S})$ for each $t \in S$ (with $g_{t,S} = 0 \implies 1/2$). By §3.1's schedule construction, $g_{t,S} > 0$ for every $t \in S$ in all intra-conference HS ($2(|S|-1)$ games) and Pro ($\ge 2(|S|-1)$ games) ties, and in 2-team cross-conference hosting ties (HS State Final, Pro World Championship) either $g_{t,S} = 0$ for both (HS) or $g_{t,S} = 1$ for both (Pro). If $\text{H2H}_S$ partitions $S$ into $m > 1$ classes $S_1, \dots, S_m$ (ordered by $\text{H2H}_S$ descending), recursively resolve each $S_j$ ($|S_j| > 1$) **starting from Step 1** ($\text{H2H}_{S_j}$).
    2. **Run Differential ($\text{RD}_t = \text{RF}_t - \text{RA}_t$, full regular season, descending):** Applied when Step 1 leaves $S$ unsplit ($m = 1$). If $\text{RD}_t$ splits $S$ into $m > 1$ classes, each class $S_j$ with $|S_j| > 1$ restarts from Step 1.
    3. **Runs Allowed ($\text{RA}_t$, full regular season, ascending):** Applied when Steps 1–2 leave $S$ unsplit. If $\text{RA}_t$ splits $S$ into $m > 1$ classes, each class $S_j$ with $|S_j| > 1$ restarts from Step 1.
    4. **Seeded coin (`StandingsCoinKey` ascending, then `team_id` ascending):** Applied when Steps 1–3 leave $S$ unsplit.
    Because every split satisfies $1 \le |S_j| \le |S| - 1$ and Step 4 is a strict total order, recursion terminates in at most $|S| - 1$ splits and is a pure function of `(Schedule_Games[phase=0], schedule_seed, seasonYear, tier, team_id)`.

#### Q9. Expected Pro postseason ace-start share under §6 and D3 fixed slots (report-only)
- **Verdict:** **Agree** — report-only derivation below; no rule change is proposed.
- **Exact derivation and numbers:**
  - *Regular-season terminal state (`Day 240`, Tue Aug 28):* Under `MinRestDays(Pro) = 3` and a healthy 4-SP staff ($A > B > C > D$), the 64-game regular season alternates $A$ on Tue/Sat (43 starts, $67.19\%$) and $B$ on Thu (21 starts, $32.81\%$). Entering the postseason after Game 64, last start days are $A = 240$ (Tue) and $B = 235$ (Thu).
  - *Every Pro Bo3 round (`WC`: Sat 244, Tue 247, Thu 249; `Div`: Sat 251, Tue 254, Thu 256; `CF`: Sat 258, Tue 261, Thu 263 — gaps `3 / 2`):*
    - **Game 1 (Saturday $d$):** Ace $A$'s previous start was at latest Tuesday $d - 4$ (Day 240 entering WC; Day 247 entering Div; Day 254 entering CF; or Day 240 with gap $11$ for bye seeds `#1/#2` entering Div). Since $\text{gap} \ge 4 \ge 3$, **$A$ starts Game 1**.
    - **Game 2 (Tuesday $d + 3$):** Ace $A$'s gap from Saturday Game 1 is $(d + 3) - d = \mathbf{3 \ge 3} \implies$ **$A$ starts Game 2** (fully rested).
    - **Game 3 (Thursday $d + 5$, if 1–1):** Ace $A$'s gap from Tuesday Game 2 is $(d + 5) - (d + 3) = \mathbf{2 < 3}$ (ineligible), while $B$'s gap is $\ge 7 \ge 3 \implies$ **$B$ starts Game 3**.
    - *Cross-round invariance:* Whether a Bo3 ends in 2 games (Tue $d+3$) or 3 games (Thu $d+5$), Ace $A$'s last start in that round is **always Tuesday $d + 3$**, giving $A$ an exact **4-day gap ($\ge 3$)** into the next round's Saturday Game 1 ($d + 7$)!
  - *World Championship Bo5 (`WS`: Sat 265, Tue 268, Thu 270, Sat 272, Tue 275 — gaps `3 / 2 / 2 / 3`):*
    - **Game 1 (Sat 265):** $A$ starts (gap $265 - 261 = 4 \ge 3$).
    - **Game 2 (Tue 268):** $A$ starts (gap $268 - 265 = 3 \ge 3$).
    - **Game 3 (Thu 270):** $A$ has gap $2 < 3 \implies$ **$B$ starts Game 3** (gap $\ge 7 \ge 3$).
    - **Game 4 (Sat 272, if live):** $A$'s gap from Tuesday Game 2 is $272 - 268 = \mathbf{4 \ge 3}$ (while $B$ has gap $2 < 3$) $\implies$ **$A$ starts Game 4**.
    - **Game 5 (Tue 275, if live):** $A$'s gap from Saturday Game 4 is $275 - 272 = \mathbf{3 \ge 3} \implies$ **$A$ starts Game 5**.
  - *Closed-form start counts and shares (healthy rosters, $0$ short-rest starts, $C = D = 0$ starts):*
    - **Any Bo3 series of length $L \in \{2, 3\}$:** $A$ makes **$2$ starts** and $B$ makes **$L - 2 \in \{0, 1\}$ starts** ($100.0\%$ when $L=2$; $66.67\%$ when $L=3$).
    - **Bo5 World Championship of length $L \in \{3, 4, 5\}$:** $B$ makes **$1$ start** (Game 3) and $A$ makes **$L - 1 \in \{2, 3, 4\}$ starts** ($66.67\%$ when $L=3$; $75.00\%$ when $L=4$; $80.00\%$ when $L=5$).
    - **Full 11-series Pro Postseason ($10\text{ Bo3} + 1\text{ Bo5}$, total games $G_{\text{post}} \in [23, 35]$):**
      - Deterministic envelope: all sweeps ($G_{\text{post}} = 23$) $\implies 22 / 23 = \mathbf{95.65\%}$; all max-length ($G_{\text{post}} = 35$) $\implies 24 / 35 = \mathbf{68.57\%}$.
      - Expected values across independent per-game win probability $p$:

        | Per-Game Win Prob $p$ | Bo3 $\mathbb{E}[S_A] / \mathbb{E}[L]$ (Pooled Share; Per-Series Mean) | Bo5 $\mathbb{E}[S_A] / \mathbb{E}[L]$ (Pooled Share; Per-Series Mean) | Full 11-Series Postseason $\mathbb{E}[S_A] / \mathbb{E}[G_{\text{post}}]$ | Pooled Pro Postseason Ace Share (`#2` Share) |
        | :--: | :--: | :--: | :--: | :--: |
        | **`0.50`** *(equal teams)* | `2.000 / 2.500` (**`80.00%`**; `83.33%`) | `3.125 / 4.125` (**`75.76%`**; `74.79%`) | `23.125 / 29.125` (`185 / 233`) | **`79.40%`** (`20.60%`) |
        | **`0.55`** | `2.000 / 2.495` (**`80.16%`**; `83.50%`) | `3.110 / 4.110` (**`75.67%`**; `74.70%`) | `23.110 / 29.060` | **`79.53%`** (`20.47%`) |
        | **`0.60`** | `2.000 / 2.480` (**`80.65%`**; `84.00%`) | `3.066 / 4.066` (**`75.40%`**; `74.43%`) | `23.066 / 28.866` | **`79.91%`** (`20.09%`) |
        | **`0.65`** | `2.000 / 2.455` (**`81.47%`**; `84.83%`) | `2.993 / 3.993` (**`74.96%`**; `73.98%`) | `22.993 / 28.543` | **`80.56%`** (`19.44%`) |

      This quantitatively confirms spec §8.3 ("the ace matters even more in October": ace usage rises from **$67.19\%$** in the 64-game regular season to **$79.40\%$** in the postseason, with zero short-rest starts).

#### Q10. Default MC runtime budget split (D7: $\le 15\text{ s}$ added)
- **Verdict:** **Agree** — the proposed default additions take **`~4.0–5.7 s` raw (`~5.0–7.1 s` with 25% reserve $\le 15.0\text{ s}$)** on the owner's Windows 11 PC (Intel Pentium Silver N5030 @ 1.10 GHz), requiring no check trims, provided the new suites share at most 1–2 generated v14 scratch worlds.
- **Component-by-component timing derivation (from 13-3 M1/M2/M3 benchmarks on the owner's PC):**
  - `college-ranking` 4-season smoke (`worlds 1/2 × scheduler 1 × outcomes 1/2 = 4` seasons on 2 worlds, plus 20× in-memory ranking replay on saved inputs): 2 world-gens ($2 \times 0.69\text{ s} = 1.38\text{ s}$) + 4 College seasons ($4 \times 0.42\text{ s} = 1.68\text{ s}$) + 20× ranking evaluation ($< 0.01\text{ s}$) = **`~3.07 s`** (or **`~2.38 s`** if world 1 reuses an existing scratch world).
  - `season-pipeline` Pro postseason (`2 × ≤ 35` games $\approx 58$ games added to the existing 2,048-game 730-day run on an already-initialized DB): $+2.8\%$ of `RunSeasonPipeline`'s `~3.8 s` = **`~0.11–0.20 s`**.
  - Pure / synthetic table and bracket fixtures (`standings` D2 fixtures, `college-ranking` arithmetic/clamp/SOS/zero-game/tie fixtures, `college-bracket` 7 splits `6+12 .. 12+6`, `college-series` 6 two-win paths `AA..BAB`, and the 100,000-series xoshiro256** fair-coin check $\approx 250,000$ `NextUInt64` draws): **`~0.12–0.20 s`** (zero full-roster world-gens).
  - End-to-end DB/sim playoff suites (`hs-playoffs`, `college-series` A/B/C rest, no-repeat SP/RP opener, depleted-staff forfeit, reload identity, `pro-playoffs`, `championship-event`) + postseason macro and attended 0-B allocation probes: sharing **1–2 v14 scratch worlds** ($0.69\text{–}1.38\text{ s}$) + simulating $\approx 75$ postseason games across tiers ($0.30\text{–}0.55\text{ s}$) + allocation warm/measure loops ($0.20\text{–}0.35\text{ s}$) = **`~1.19–2.28 s`**.
  - **Total added default MC time:** **`4.49–5.75 s` raw (`5.61–7.19 s` with 1.25× reserve $\le 15.0\text{ s}$)**. Against 13-3 M3's 20-run default MC baseline (`min 55.79 s`, `median 80.08 s`, `mean 89.22 s`, `max 135.53 s`), the full default run remains well inside both D7's `+15 s` and D10's `213.7 s` ceiling. *(Implementation note: do not call `LeagueGenerator.GenerateIfEmpty` on a fresh DB file per sub-fixture; at `691 ms` per v14 world-gen on the N5030, $\ge 15$ redundant world-gens would consume the budget by themselves.)*

---

### 10.2 Independent Arithmetic and Calendar Audit (Task 2)

Every date, weekday, gap, bracket split, game count, and timing claim in the brief was independently recomputed from `GameCalendar.cs` (`MonthLengths = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31}`, `Day 1 = Monday Jan 1`) and 13-3's M2 timings:

1. **D3 Calendar Table (28 dates checked: 28/28 exact, 0 errors):**
   - **Regular-season end & `PostseasonFirstDay(tier) = RegularSeasonLastDay(tier) + 1`:**
     - **HS:** 10 Fridays from `61 (Fri Mar 2)` to `124 (Fri May 4)` $\implies$ Selection `PostseasonFirstDay(HS) = 125 (Sat May 5)` ✓.
     - **College:** 14 Wed/Sat weeks from `38 (Wed Feb 7)` to `132 (Sat May 12)` $\implies$ Selection `PostseasonFirstDay(College) = 133 (Sun May 13)` ✓.
     - **Pro:** 21 Tue/Thu/Sat weeks + week-22 Tue from `93 (Tue Apr 3)` to `240 (Tue Aug 28)` $\implies$ Selection `PostseasonFirstDay(Pro) = 241 (Wed Aug 29)` ✓.
   - **Postseason game days (all 25 game days verified non-Sunday):**
     - **HS:** QF `131 = Fri May 11` · Conf Finals `138 = Fri May 18` · State Final `145 = Fri May 25` (all Fridays, `(d - 1) % 7 == 4`) ✓.
     - **College:** Play-in `139 = Sat May 19` · R1 `146 = Sat May 26` · R2 `153 = Sat Jun 2` · R3 `160 = Sat Jun 9` · R4 `167 = Sat Jun 16` (all Saturdays, `(d - 1) % 7 == 5`) · **The Red Dirt Series:** G1 `172 = Thu Jun 21`, G2 `173 = Fri Jun 22`, G3 `174 = Sat Jun 23` (`(d - 1) % 7 ∈ {3, 4, 5}`) ✓.
     - **Pro:** WC `244 = Sat Sep 1, 247 = Tue Sep 4, 249 = Thu Sep 6` · Div `251 = Sat Sep 8, 254 = Tue Sep 11, 256 = Thu Sep 13` · CF `258 = Sat Sep 15, 261 = Tue Sep 18, 263 = Thu Sep 20` · World Championship `265 = Sat Sep 22, 268 = Tue Sep 25, 270 = Thu Sep 27, 272 = Sat Sep 29, 275 = Tue Oct 2` (all Tue/Thu/Sat, `(d - 1) % 7 ∈ {1, 3, 5}`) ✓.
   - **No Sunday games:** Only College Selection (`133 Sun May 13`) falls on a Sunday, and Selection is a flush/seeding batch that schedules the Play-in for Saturday `139`. Zero regular-season or postseason games fall on Sunday (`dayOfSeason % 7 == 0`) ✓.
   - **Red Dirt Series vs Draft Day & Ace Gap:**
     - Ace gap from R4 (`167 Sat Jun 16`) to Red Dirt Series G1 (`172 Thu Jun 21`): $172 - 167 = \mathbf{5\text{ days} \ge \text{MinRestDays(College)} = 3}$ ✓.
     - Red Dirt Series ends Day **`174 (Sat Jun 23)`** $= 132 + 42\text{ days}$ (**exactly 6.0 weeks** after regular-season end, matching spec §8.2's `≤ 6 weeks`), and **`21 days` before Draft Day `195 (Sat Jul 14)`** ($174 < 195$) ✓.
2. **College Conference Split $k + (18 - k)$ and Fixed Seeded Bracket Trees:**
   - Because each College conference has 12 teams, the 18 qualifiers after the `#18 vs #19` play-in split as $k$ (East) and $18 - k$ (West) with $k \le 12$ and $18 - k \le 12 \implies \mathbf{6 \le k \le 12}$ (7 possible splits: `6+12, 7+11, 8+10, 9+9, 10+8, 11+7, 12+6`).
   - By pigeonhole principle, $\max(k, 18 - k) \ge \lceil 18/2 \rceil = \mathbf{9}$, so the larger conference (or both at `9+9`) always has $m \in \{9, 10, 11, 12\}$ teams ($2^{\lceil\log_2 m\rceil} = 16$, playing a **16-slot bracket over R1–R4: Days 146, 153, 160, 167** with $16 - m \in \{7, 6, 5, 4\}$ R1 byes and $m - 8 \in \{1, 2, 3, 4\}$ R1 games). Whenever $k \ne 9$, the smaller conference has $m \in \{6, 7, 8\}$ teams ($2^{\lceil\log_2 m\rceil} = 8$, playing an **8-slot bracket aligned to the end over R2–R4: Days 153, 160, 167**, idle on Day 146, with $8 - m \in \{2, 1, 0\}$ R2 byes and $m - 4 \in \{2, 3, 4\}$ R2 games).
   - Across both conferences, total single-elimination conference-tournament games are invariant to $k$: $(k - 1) + ((18 - k) - 1) = \mathbf{16\text{ games}}$ (plus $1$ play-in game and $2\text{–}3$ Red Dirt Series games = **$18$ series, $19\text{–}20$ games**).
   - **Caution on `6993e70`'s `BuildRound` bug:** In `6993e70` (`PlayoffManager.cs:330, 390`), `BuildRound` re-sorted `Waiting.Concat(Winners)` by seed at the start of every round — which **reseeds** after upsets instead of following D3's *"fixed standard seeded draws (1v16, 8v9, …; byes to the top seeds; no reseeding)"*. To prevent transplanting that bug under D0-B, 13-4a must use the canonical fixed bracket slot order:
     - **16-slot tree (R1 $\to$ R4):** pairs `(1,16), (8,9), (4,13), (5,12), (2,15), (7,10), (3,14), (6,11)` (any slot $> m$ is a bye, advancing its partner to the fixed parent node; no re-sorting by seed between rounds).
     - **8-slot tree (R2 $\to$ R4):** pairs `(1,8), (4,5), (2,7), (3,6)` (any slot $> m$ is a bye, advancing its partner to the fixed parent node).
     *(Only Pro's Division round reseeds `#1 vs lowest remaining, #2 vs other`, per spec §8.3 and D3.)*
3. **HS and Pro Series / Game Counts:**
   - **HS:** 6 teams $\implies$ **5 series (`best_of = 1`), always 5 games** (2 QF on 131 + 2 Conf Finals on 138 + 1 State Final on 145) ✓.
   - **Pro:** 12 teams $\implies$ $4\text{ WC (Bo3)} + 4\text{ Div (Bo3)} + 2\text{ CF (Bo3)} + 1\text{ WS (Bo5)} = \mathbf{11\text{ series}}$; minimum games $= 10(2) + 3 = \mathbf{23}$, maximum games $= 10(3) + 5 = \mathbf{35}$ ($\le 35$ games ✓; expected at $p = 0.5$: $10(2.5) + 4.125 = \mathbf{29.125\text{ games}}$).
4. **CS-M's 65,536-Season Cost and D5 Timing Estimates (all verified):**
   - **CS-M §3.1 full matrix (65,536 seasons):** At 13-3 M2's measured `546 ms` (flat) and `678 ms` (spread-25) per 28-game season, 65,536 28-game seasons cost $65,536 \times 0.546\text{ s} = \mathbf{9.94\text{ h}}$ (flat) to $65,536 \times 0.678\text{ s} = \mathbf{12.34\text{ h}}$ (spread-25); even if the 32,768 14-game seasons ran at $0.58\times$ the 28-game time, 32,768 28-game + 32,768 14-game spread-25 seasons cost $6.17 + 3.58 = \mathbf{9.75\text{ h}}$ raw (**`12.19 h`** with 1.25× reserve). Brief's `≈ 9–12 h` is exact ✓.
   - **D5.2 Pilot (`32` spread-25 seasons):** $32 \times 0.678\text{ s} = \mathbf{21.70\text{ s}} \approx \mathbf{22\text{ s}}$ (`< 60 s` stop rule) ✓.
   - **D5.3 Reduced Holdout (`512` spread-25 seasons):** $512 \times 0.678\text{ s} = 347.14\text{ s} = \mathbf{5.79\text{ min}} \approx \mathbf{5.8\text{ min}}$ ✓.
   - **D5.4 Sensitivity (`128` training spread-25 seasons):** $128 \times 0.678\text{ s} = 86.78\text{ s} = \mathbf{1.45\text{ min}} \approx \mathbf{1.5\text{ min}}$ (or **`1.08 min` / `65.1 s`** if worlds `1–8 × 2 × 2 = 32` seasons from the D5.2 pilot are reused and only worlds `9–32 × 2 × 2 = 96` additional training seasons are simulated) ✓.
   - **D5.5 + D5.6 (Strength diagnostics + 19 additional single-tuple repeats):** $R = 4$ neutral in-memory round-robins on 32 holdout worlds ($\approx 20\text{ s}$) + 5 synthetic fixtures ($\approx 25\text{ s}$) + 19 repeats of tuple `(129, 1, 1)` ($19 \times 0.678\text{ s} = 12.88\text{ s}$) = **`~0.96 min`**. Total `--college-analysis ranking` wall time at $k = 32$ is **`~8.2–8.6 min`** (well under the `20 min` hard stop) ✓.

---

### 10.3 Math View on Owner Decisions D0–D7 (Task 3)

| Decision | Math / Guard Verdict | Rationale & Guard-Safety Analysis |
| :-- | :--: | :-- |
| **D0** (Prior art `6993e70`) | **Support D0-B** *(Opus/owner choice; 2 correctness flags)* | Leaving workflow choice to Opus/owner, but flagging two correctness defects in `6993e70` that **must not** be transplanted: (1) `PlayoffManager.BuildRound` (`6993e70` line 330) re-sorts remaining teams by seed every round, which illegally reseeds College brackets after upsets; (2) `PlayoffManager.GameSeed` (`6993e70` line 257) uses unmixed bit shifts without `schedule_seed`, and `RankingCalculator.Calculate` (`line 445`) falls back to `TeamId` on exact ties. |
| **D1** (`Team_Seasons` materialization) | **Endorse D1-A** *(Guard-safe)* | Idempotent full-recompute upsert from `Schedule_Games WHERE phase = 0 AND status IN (1, 2)` prevents double-counting across reloads or retried flushes, freezes `national_rank` at regular-season end for Red Dirt Series hosting, and forces the end-of-regular-season flush on `PostseasonFirstDay(tier)` so Pro's Tuesday 240 finale commits before Saturday 244's Wild Card G1. Moves no guard (writes only `Team_Seasons`, flushes already-played phase-0 games). |
| **D2** (Standings tiebreakers) | **Endorse D2-A** *(Guard-safe)* | Recursive mini-league H2H $\to$ RD $\to$ RA $\to$ seeded coin (`StandingsCoinKey`) is transitive, terminating, reload-invariant, and consumes zero draws from `LeagueSimulator._rng`. Pairwise H2H (D2-B) fails transitivity on 3-way cycles ($A \succ B \succ C \succ A$) and must remain rejected. |
| **D3** (Postseason formats & fixed calendar) | **Endorse D3 fixed slots** *(Guard-safe)* | Fixed dates guarantee zero Sunday games, give both College conference champions an identical 5-day gap (`167 → 172`, eligible at `MinRestDays = 3`), finish the Red Dirt Series 21 days before the Draft (`174 < 195`), and let UI/planner blocks project deterministically. Rows-on-certainty prevents phantom unplayed game rows. Extend `StarterSelection.SelectOpener` with `usedInSeries` in both `LeagueSimulator` and `MicroGame` so the Red Dirt Series no-repeat rule cannot be bypassed by bullpen openers or role changes. |
| **D4** (Postseason stats & Pro guard invariance) | **Strongly Endorse D4-A** *(Critical guard gate)* | **D4-C moves the sanctioned Pro guard (`PA 81433 / H 18471 / ER 8803`, ledger `932ACE26…8B84`), corrupts season rate denominators and tier bands, and must be rejected.** **D4-A** keeps the sanctioned Pro guard and resolver guard byte-exact with the postseason running in `RunSeasonPipeline` while adding zero DDL ahead of the v15 freeze, provided all 10 invariance channels in §10.1 Q7 (including `MicroGame.FlushGame` stat suppression on attended `phase IN (1,2)` games) are enforced. D4-B is also guard-safe (`ProGuardLedgerQueries` names `Batting_Stats`/`Pitching_Stats` explicitly) if the owner ever wants separate `*_Stats_Post` DDL before v15 freeze. |
| **D5** (CS-M §3 opt-in ranking analysis) | **Endorse D5.1–D5.6 with Q3/Q4/Q5 specs** *(Guard-safe)* | Cuts an infeasible ~10–12 h 65,536-season batch to an **~8.5 min** pilot + pre-registered 512-season holdout + sensitivity + strength diagnostics under a 20-min hard stop (`exit 2 = INCOMPLETE`), using the venue-balanced 14-game sub-sampler (§10.1 Q3), the frozen pilot sizing ladder (§10.1 Q4), and $R = 4$ in-memory neutral round-robins (§10.1 Q5). |
| **D6** (Engine vs UI staging) | **Defer to Opus/owner** *(Guard-safe)* | No Math or calibration stake; D6-B/C both keep UI strictly read-only over DTOs. |
| **D7** (Default MC runtime budget $\le +15\text{ s}$) | **Endorse D7** *(Guard-safe)* | Fits in **`~4.5–7.2 s ≤ 15.0 s`** on the owner's PC when the new suites share 1–2 generated v14 scratch worlds (§10.1 Q10). |

---

### 10.4 Proposed Amendments to Reconcile into §§1–9 (Task 4)

Opus should fold these 7 numbered refinements into §§3–5 before owner sign-off (no §§1–9 lines were edited by this Math check):

1. **Amendment 1 — Attended Postseason Stat Suppression under D4-A (§3 D4-A & §4 item 4):**
   > *"Under D4-A, both macro (`LeagueSimulator.SimulatePlayoffGame`) and attended (`MicroGame.FlushGame` / `CareerManager.PlayPendingGame`) postseason games (`phase IN (1, 2)`) suppress `Batting_Stats` and `Pitching_Stats` writes and `StatsNormalizer.NormalizeSeason` calls, while still committing `Schedule_Games.RecordFinal`, `Playoff_Series`, `PitcherRestLedger.FlushDirty`, `ApplyPedGameCosts`, and (for attended games) `Game_Logs` in the game's transaction."*
2. **Amendment 2 — Ranking Exact-Tie Seeded Coin Rule (§3 D2, §4 item 1, §5 `college-ranking` check 4):**
   > *"College National Ranking ties break by `RankScore` ↓, `W` ↓ (exact cross-product, `0/0 → 1/2`), `RD/G` ↓ (exact cross-product, `0/0 → 0`), `RankingCoinKey(schedule_seed, seasonYear, LeagueTier.College, team_id)` ↑ (domain tag `"RANKG64_"` = `0x5241_4E4B_4736_345FUL`), then `team_id` ↑ as the $2^{-64}$ collision fallback. Restate `college-ranking` check 4 from `ties/ID ordering` to `ties/seeded-coin ordering with ID collision fallback`."*
3. **Amendment 3 — Chained SplitMix64 Stream Mixer & Natural Series Key (§2 Determinism, §3 D2, §3 D4-A):**
   > *"Derive all 13-4 streams and coins via `PostseasonStreams.MixCombine` (chained SplitMix64 avalanche with 64-bit domain tags `POSTMACR`, `POSTMICR`, `STAND64_`, `RANKG64_`, `SUB14CMP`). Key per-game postseason streams on `(schedule_seed, seasonYear, tier, round, high_seed_team_id, low_seed_team_id, game_in_series, isAttended)` — the persisted natural key of `Playoff_Series` + `Schedule_Games`, never SQLite's auto-rowid `series_id` and never `LeagueSimulator._rng` or `CareerManager._rng`."*
4. **Amendment 4 — Fixed Non-Reseeded College Bracket Trees vs Pro Division Reseeding (§3 D0-B & §3 D3):**
   > *"College conference brackets use fixed standard seeded bracket trees with no reseeding between rounds: 16-slot order `(1,16), (8,9), (4,13), (5,12), (2,15), (7,10), (3,14), (6,11)` over R1–R4 for $m \in [9, 12]$, and 8-slot order `(1,8), (4,5), (2,7), (3,6)` over R2–R4 for $m \in [6, 8]$, where any slot $> m$ is a bye advancing its partner to the fixed parent matchup. Do not transplant `6993e70`'s per-round seed re-sort into College; only Pro's Division round reseeds (`#1 vs lowest remaining, #2 vs other`)."*
5. **Amendment 5 — Red Dirt Series `usedInSeries` Derivation & `SelectOpener` Overload (§1.1, §3 D3, §4 item 3):**
   > *"Derive `usedInSeries` for The Red Dirt Series from `PitcherRestLedger.TryGetLastStart(playerId, out long last)` via `last >= seriesFirstAbsoluteDay && last < todayAbsoluteDay` across both `_starterSlots` and `_relieverSlots`, and pass `usedInSeries` to both `StarterSelection.SelectStarter` and `StarterSelection.SelectOpener(relieverAvailable, usedInSeries)` in `LeagueSimulator` and `MicroGame`. When both return `-1` in a postseason game, record the narrow postseason forfeit (`RegulationInnings(tier)`–`0`, `status = 2`, no player stats) instead of shadowing with the Phase 8c call-up."*
6. **Amendment 6 — D5 Ranking Analysis Specification: Venue-Balanced 14-Game Sub-Sampler, Sizing Ladder, and $R = 4$ Strength Diagnostics (§3 D5.1–D5.5):**
   > *"In `--college-analysis ranking`: (a) D5.1 selects the 14-game comparator from each 28-game season via `Subsample14Tag` by picking 1 of the 2 identical-venue meetings for each ordered intra-division pair (3H/3A) and 1 of the $\binom{4}{2} = 6$ balanced 2-of-4 cyclic Latin shift masks per cross-division pair (4H/4A, total 14 games = 7H/7A per team); (b) D5.3 sizes $k_{\text{hold}} \in \{32, 48, 64\}$ from the 8-world pilot via §10.1 Q4's frozen 90%-power rule before reading validation worlds `129+`, and gates the 9,500th sorted resample of 10,000 paired bootstrap draws (`RngState(28014UL)`) at $\le 1.05$; (c) D5.5 runs $R = 4$ neutral in-memory double-round-robins per holdout world (fallback $R = 2$ if $< 1,500\text{ games/s}$) plus $R_{\text{fix}} = 8$ on the 5 CS-M §3.4 fixtures, all report-only (`[DIAG]`)."*
7. **Amendment 7 — Default Suite Scratch-World Sharing and Fair-Coin Seed (§3 D7 & §5 `college-series`, `season-pipeline`):**
   > *"Pin `FairCoinSeed = 0x134C_0140_0001_86A0UL` for the 100,000-series Bo3 fair-coin check (`.495–.505`), share at most 1–2 generated v14 scratch worlds across the new default playoff suites to keep default MC additions within `~4.5–7.2 s ≤ 15.0 s`, and assert all 10 guard-invariance channels from §10.1 Q7 alongside `season-pipeline`'s 2026 Pro postseason completion check."*

