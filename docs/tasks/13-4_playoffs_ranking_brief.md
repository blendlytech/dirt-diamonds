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

## 10. Math record and review (Gemini 4 Argon)

*Empty until Argon's Math check.*
