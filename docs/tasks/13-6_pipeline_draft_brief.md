# Task Brief: 13-6 — PipelineManager: graduation, recruiting, class years, the Draft, Pro turnover

**Status:** **RECONCILED 2026-11-24** (Claude Opus 5.5, `p13/13-6-brief-opus`). Drafted 2026-11-23. Gemini 4 Argon's Math check (§10, APPROVE WITH CHANGES) is reviewed and reconciled into §§1–9: Amendments 1–7 and Opus corrections C1–C5 (§10.5). **Owner decided D1–D8 on 2026-11-24:** all A, except **D4b = C (owner)**: an undrafted senior or a cut Pro avatar is released, which is game over without children or the parent phase with them (§3). **Ready for the 13-6a build.** *(Roster, 2026-11-25: Gemini 4 Carbon replaced Gemini 4 Argon; Carbon reviews 13-6a.)*
**Recommended model:** **13-6a** (engine, persistence, harnesses): Claude Opus 5.5 (TEAM.md §2, 13-6 row). Gemini 4 Argon did the Math check of this brief (§10). **Gemini 4 Carbon** does the independent review of the build (TEAM.md §1a, 2026-11-25). **13-6b** (phone decisions and the Recruiting/Draft screens, if D8-A): Claude Sonnet 5.5, Gemini 4 Neon reviews.
**Reviewer:** Gemini 4 Carbon independently reviews Opus's sim/persistence diff (TEAM.md §1a). Opus reviewed Argon's Math record (§10.5). Opus is the merge gate after review.
**Owner gate:** D1–D8 before the build. If the build finds that the Pro guard moves (D1), it stops and brings a sanction (13-3 precedent); the recommended D1-A is designed not to move it.
**Spec:** [league_restructure.md](../design/league_restructure.md) §2.1, §5.1, §5.3, §9 (all), §10, §11.3 (development gradient), §12, §13, §15 (13-6 row), §16-I4/I5/I12/I16; [temptation_and_lineage.md](../design/temptation_and_lineage.md) §4.2 (the first Pro contract) and §8.1 (the Trailblazer's "first"); [13-5 brief](13-5_roster_lock_forfeits_brief.md) §11.5 and §12.3 (Nit N1); the 9c/9d records in [archive/promotion_advancement_gates.md](../archive/promotion_advancement_gates.md) and [reference/development_decline_curves.md](../reference/development_decline_curves.md).
**Branches / worktrees:** brief `p13/13-6-brief-opus` (this file, `C:\Users\DELL\dnd-opus-136`); Math `p13/13-6-mathcheck-argon` (`.worktrees/13-6-mathcheck-argon`); build `p13/13-6a-opus`; review `p13/13-6a-review-carbon`. All start from **`p13/integration`** at or after **`9277af5`** (13-5a merged) plus this brief's merge, each in its own worktree, and merge into `p13/integration`. **`main` is reached only after 13-6a turns the 7 promotion reds green** (spec §15, §16-I12; D8 says whether that waits for 13-6b).

---

## 0. Preconditions (check first; stop and report if any fails)

1. Your own worktree (TEAM.md §3); `git status` clean; a write check (empty commit + soft reset); `C:` free space ≥ 2 GB.
2. `p13/integration` at or after `9277af5`, with this brief merged, D1–D8 recorded in §3, and Argon's §10 reconciled.
3. Baseline on that head before any edit (recorded by Opus on `9277af5`, 2026-11-23): build 0/0; SchemaValidator **174/174**; CoreLoop **32/32**; GrittyEvents **363/363**; MC default **537/544**, the reds exactly the 7 promotion suites (`promotion-sweep`, `promotion-stream`, `promotion-avatar`, `promotion-record`, `development-arc`, `practice-seam`, `development-equilibrium`, each an unhandled `InvalidOperationException` from `PromotionManager`'s six-tier guard); resolver 13/13; tier-ladder 14/14; `season-pipeline` 38/38 with the Pro guard **PA 81433 / H 18471 / ER 8803**, ledger **`932ACE26…8B84`**, 2026/2027 Pro postseasons 11/30/323 and 11/32/319; every allocation line 0 B; 0 leaked `%TEMP%\dnd_*` files. Record your own wall time and the normalized default-log SHA-256 (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`); 13-5a §11.4 / §12.1 recorded `40ab9f957887…`. (Opus's post-merge run took 325.5 s on a throttled PC; Argon's 13-5a median was 88.3 s. Compare against your own baseline.)
4. **No Blind Queries.** This brief validated its §4 SQL on a scratch v14 database (`sqlite3` 3.53 over `SchemaDefinitions.sql`, `user_version` 14, `foreign_keys = ON`): every statement executed on throwaway rows (insert and update paths), `PRAGMA foreign_key_check` clean, and `EXPLAIN QUERY PLAN` recorded in §4 item 3. Revalidate any new or changed statement on a scratch copy, never the live save.

## 1. Context

13-6 closes the league loop. Today nothing moves a player between tiers on the v14 world: `PromotionManager` (the 9c six-tier ladder) refuses to run on 12/24/32 teams, so the MC promotion suites are red and **the live game throws at its first season rollover** (§1.2 #1). The spec's replacement (§9) is a pipeline, not a ladder: HS seniors are recruited into College in the fall, College players declare and are drafted in July (4 rounds × 32 picks, reverse Pro standings), drafted players report to Pro the next spring, and Pro teams over the 24-player org cap release their lowest-value players into a free-agent pool that signs only at the Sunday lock. `PipelineManager` owns all of it and publishes `ProContractSignedEvent`, the Phase L hook for the father's first text.

**Gate staging.** 13-6 isn't on the Gate 2 path (scope_realignment Gate 1 row). It adds **no DDL**: `College_Enrollment`, `Recruiting_Offers` and `Draft_Picks` are already in v14. It changes no in-season game path: every roster move happens at the rollover or at a Sunday lock, so a neutral HS regular season stays byte-identical.

### 1.1 What exists on `p13/integration` (`9277af5`)

| Seam | State today | 13-6 consequence |
| :-- | :-- | :-- |
| `PromotionManager` (`PromotionManager.cs:352–869`) | The 9c sweep: removals, vacancy cascades, merit swaps, HS intake, one batch, sims re-init, `ReactivateAvatar`. Throws unless every tier has 8 teams (`:460–468`). Wired live at `GameManager.cs:586–590` on a boot-forked stream | **Retired** (deleted). `PipelineManager` takes its subscription slot and its boot draw (§1.2 #7) |
| `PromotionScore` / `PromotionProfile` (`:15–280`) | Pure value math: `BatterPerformance`/`PitcherPerformance` (tier-relative, shrunk, HS shrink constants), `Scouting(roleSum, age, headroom)`, `Combine` (0.5/0.5), `RecordArrestPenalty` 3.0, age caps HS 19 / College 23 | **Kept** as the value function (spec §9.1/§9.5). Ladder-only members (`LadderTeamsPerTier`, swap caps/margin, intake ages) move to `PipelineProfile` or go |
| `SchoolGrades` (`SchoolGrade.cs`) | Grade from age: Freshman 16 (`CareerManager.StartingAge`) … Senior 19 (`HighSchoolAgeCap`); clamped | Kept. Recruiting targets players whose grade is Senior in the completed HS season (§1.2 #5) |
| `College_Enrollment` (`SchemaDefinitions.sql:644–650`) | `player_id` PK, `team_id` FK, `entry_season`, `class_year ≥ 1`, `declared_season` NULL. **No reader or writer; world-gen writes no rows** | Class years, declaration, eligibility. A boot ensure backfills world-gen's College players (D2) |
| `Recruiting_Offers` (`:653–659`) | PK (`player_id`, `team_id`, `season_year`); status 0 open / 1 accepted / 2 declined / 3 expired. No index on `season_year` | The avatar's offers and every accepted recruit |
| `Draft_Picks` (`:661–668`) | PK (`season_year`, `overall_pick`), `round`, `team_id`, `player_id`. No index on `player_id` | The draft record; read by season only |
| `Entity_Flags` (`:144–157`) | UNIQUE (`player_id`, `flag_name`), `is_active`, `set_on_day`; partial index on active `flag_name` | The free-agent / retired / first-contract state (D3) |
| Free-agent pool (`BaseballQueries.cs:179–186`, `:1010–1020`; `CareerManager.cs:1257–1283`) | "Signable" = `team_id IS NULL` + a ratings row + age in [18, 42) + health > 40. Used only by succession's backfill, which runs at rollover **before** the pipeline (subscriber #2 vs #4) | It can't tell a released player from a retiree, an unrecruited HS senior, a signed outside recruit awaiting the rollover, or the avatar's adult children (D3; Amendment 6 narrows it by two exclusions) |
| World-gen (`LeagueGenerator.cs:23–50`) | Ages HS 15–18, College 18–22, Pro 21–36; rosters HS 11–14 / 3–4 / 2–4, College and Pro exactly 15/4/5 = 24 | Steady-state ages differ from world-gen (§1.2 #5); College has 2 spare org slots (cap 26) |
| `GeneratePlayer` (`:328–421`) | Writes Players, ratings, potential, role, optional person row; **no `Player_Fielding` row** (`:314–315`: "intake and fillers get theirs in 13-6/G-4") | Every pipeline-generated player gets a fielding row (§4 item 6) |
| Rollover order (`GameManager.cs:447–590`) | `SeasonRolledOverEvent` handlers in attach order: tier sims (flush) → `CareerManager` (`AgeAllPlayers`, succession; `CareerManager.cs:1380–1425`) → `DevelopmentManager` → `PromotionManager` (`RosterManager` and `PlayoffManager` don't subscribe) | `PipelineManager` keeps the last slot: develop-before-sort holds |
| Avatar handoff (`CareerManager.cs:1134–1150`) | `ReactivateAvatar` re-reads the avatar's team, clears every sim's attended claim, re-activates, republishes `AvatarChangedEvent`. Throws if the avatar is unrostered | Reused for HS→College and College→Pro. The avatar must never be left unrostered (D4) |
| Draft day (`TierSeasonCalendar.cs:37–38, :103`) | `DraftDay` = the second Saturday of July = **day 195** | The Draft runs on its tick |
| Standings / bracket (`PlayoffQueries.cs:197, :235`; `Standings.cs:141`) | `Team_Seasons` (W/L, runs, `national_rank`, `seed`) and `Playoff_Series` (round, winner) per season and tier; `Standings.Order` is the I14 tiebreak | Draft order (prior Pro season) and recruiting order (last College national rank) |
| Sunday lock (`RosterManager.cs`) | Pro Sundays 91–273, after the Pro sim's flush, 768 rows; reads `LoadRosterByTier` | Free-agent signings run first inside the lock (D2, §4 item 5) |
| Contacts (`contacts.json`) | `coach_hs`, `coach_college` ("Coach Halloran", placeholder), `coach_reyes` (Pro), `agent_diaz` | Offer, Signing Day, declaration and draft texts (D5) |
| Guard world (`MonteCarloHarness/Program.cs:504–628`) | One Pro sim (flat, seed 777), `RosterManager`, `PlayoffManager`; **no career, no aging, no development, no promotion** over 2026–2027 | The ledger spans both seasons, so a pipeline at the 2026→2027 rollover would move it (D1) |
| The 7 reds (`Program.cs:174–183`) | 4 promotion suites; `development-arc` and `development-equilibrium` drive the ladder through 24 and 80 offseasons; `practice-seam` fails only in its last section (§10.9 scouting seam, `:6344–6424`) | Rewritten onto the pipeline or moved to `PipelineHarness` (§5) |

### 1.2 Findings that shape the D-options

1. **The live game can't cross a season today.** `PromotionManager.RunOffseason` throws on the 3-tier world, and `EventBus.DispatchOne` (`EventBus.cs:188–199`) doesn't catch handler exceptions, so the first rollover's pump throws out of the clock. CoreLoop never crosses a rollover with the live composition, so nothing catches it. 13-6a must add a composed-rollover check (§5).
2. **The guard world can stay pipeline-free.** It has never run aging, development or promotion. Its 2027 season is "the same rosters, one year on", and both seasons feed the sanctioned ledger. Attaching `PipelineManager` there would draft, release and re-init at the 2026→2027 rollover and move the ledger. Unlike 13-5, invariance can't be proven by construction (D1).
3. **Conservation is by construction, not by tuning.** Every move is a matched exchange per (team, role): HS graduates are replaced by intake, College departures by recruits, and Pro is trimmed to exactly 15/4/5 after the draft. The ±5% Pro population gate (spec §9.3) can fail only where the free-agent pool lacks a role at the opening lock. That's Argon's Q2.
4. **The free-agent pool needs state the schema doesn't name.** "Unsigned for 2 seasons", "retired" and "first Pro contract" aren't columns. Today's unflagged pool would sign an unrecruited 19-year-old, a retired avatar under 42, or the avatar's own adult children, who are the heir pool. `Entity_Flags` carries engine-written state without DDL (D3).
5. **Ages and class years don't line up out of the box.** `SchoolGrades` makes 19 the senior age; aging runs before the pipeline at rollover, so a just-graduated senior reads 20. Steady-state College freshmen are 20 (NPC and avatar alike), draft-eligible at 21. World-gen's HS 15–18 and College 18–22 are a one-off transient: no HS senior exists in the first fall, so the first College class comes entirely from outside recruits. 15–16 intake would give a 15-year-old five HS seasons (the clamp), so **intake is age 16 only** (Amendment 2). The transient clears in four seasons (Argon Q3). In seasons 2–3 no College player is 22 or older, so the declaration rule keys on class year as well as age (Amendment 3). Nothing here changes world-gen.
6. **The spec's "held-back" mechanic conflicts with its walk-on guarantee.** *(Resolved 2026-11-24 by D4a-A and the owner's D4b-C: a released avatar is unrostered by design, so 13-6a adds an unrostered-avatar mode.)* §9.1 keeps "merit-based graduation, held-back mechanic all unchanged" (a senior who out-ranks no College incumbent stays in HS) and also guarantees a walk-on offer "so the career always continues". Pro has the same gap: a released or undrafted avatar would be unrostered, which `CareerManager` can't represent today (D4).
7. **Boot-stream discipline.** `GameManager` hands `PromotionManager` `new RngState(rng.NextUInt64() | 1UL)` (`:586–588`). Removing that draw would shift every later boot consumer, the Life sim seed included. The pipeline keeps the draw at the same position, and uses it once to write a persisted `pipeline_seed` (the 13-3 `schedule_seed` pattern). From that seed it derives per-(season, purpose) streams, so a reload between rollovers can't change an intake or a recruit (Q8). The old boot fork was reload-unstable.
8. **The ban flag is Pro-only in content.** `banned_for_life` is set only by `dirt_permanent_ban`, gated `tier >= 2`. `record_arrest` is tier-agnostic. So in a pipeline with no avatar release (D4-A), the ban has nothing left to block, and only `record_arrest` matters for recruiting and the draft (D6).

## 2. Hard constraints

- **Allowed paths (13-6a):**
  - `Assets/Simulation/Baseball/`: new `PipelineManager.cs` (calendar handlers, rollover pass, free agency) and `PipelineRules.cs` (pure: value, bands, draft order, release order, declaration); **delete** `PromotionManager.cs`'s manager and summary (keep `PromotionScore`/`PromotionProfile`, trimmed); edits to `CareerManager.cs` (the avatar handoffs, D4b-C's unrostered-avatar mode and the takeover at 16, the record-flag constants' new home), `RosterManager.cs` (the pre-lock free-agency hook only), `LeagueGenerator.cs` (a fielding-row helper for generated players, plus an outside-recruit overload taking a mean offset and a discount age (Amendment 2); **world-gen's draw order unchanged**), and comments only in `SchoolGrade.cs`, `DevelopmentManager.cs`, `PersonEffects.cs` and `Assets/UI/Scouting/ScoutingGrade.cs` (Amendment 7).
  - `Assets/Data/Database/`: new `PipelineQueries.cs` and DTOs (`CollegeEnrollmentRow`, `RecruitingOfferRow`, `DraftPickRow`, `FreeAgentRow`, a `PipelineCandidateRow` carrying age, `health_ceiling`, potential and the tier's stat line next to the `RosterPlayerRow` ratings (C5), and `PipelineStatusView`). In `BaseballQueries.cs`, only `SqlSelectFreeAgents` changes: two `NOT EXISTS` exclusions (Amendment 6, C1). In `GameStateQueries.cs`, only two additive `GameStateKeys` change: `parent_phase_since_day` and `family_hs_team_id` (D4b-C; `pipeline_seed` joins them).
  - **Life and UI stay read-only.** If the Life sim, the phone or a screen can't run with an unrostered avatar, 13-6a stops and reports it. It doesn't patch them; that work goes to the parent-phase brief.
  - `Assets/Core/`: `CoreEvents.cs` (four events, §4 item 7), `GameManager.cs` (composition; the boot ensure; the boot draw kept in place).
  - `Assets/Narrative/` (D5-A): a new `PipelineBeatWriter`. `Assets/Localization/strings.csv`: `pipeline.*` placeholder texts.
  - `Tools/MonteCarloHarness/`, `Tools/CoreLoopHarness/`, new `Tools/PipelineHarness/` (added to `DirtAndDiamonds.sln`), `Tools/GrittyEventsHarness/` (the record-flag constants' new home only).
  - Docs: this brief, the Math record, `progress.md`, the spec §9/§16 sync, the MC skill, a `PipelineHarness` skill entry.
  - **Everything else is read-only.**
- **Calibration contract:** `AtBatResolver`, `BaseOutAdvancement`, `MicroGame`'s per-PA model, `TierEffects`, `EffectiveRatings`, `DevelopmentCurve` constants and every band are untouched. Resolver guard byte-exact (`6d924228…`). **Pro guard byte-exact** (D1). Neutral paths are byte-identical: the default MC log, normalized, changes only by lines that new or rewritten suites add.
- **Zero allocation:** no game-day path changes. Pipeline handlers run at most a few times a season (four calendar days, the rollover, the Sunday locks) and may allocate, bounded. A Sunday lock with no free-agent signing must add **0 B** to the lock's existing cost and must not re-initialize any sim.
- **Determinism / RNG:** **no draws** from any sim's stream, `CareerManager._rng` or the postseason streams. Every pipeline draw comes from a stream derived from the persisted `pipeline_seed` by (season year, purpose tag) (§1.2 #7, Q8). Everything else (values, offers, draft order after the inaugural season, releases, signings) is a pure function of persisted state, with ties broken by `player_id` (or `team_id`) ascending. Same save ⇒ same pipeline, including across a reload at any day.
- **Boundaries:** `PipelineManager` lives in `/Simulation/Baseball/` and never references `/Simulation/Life/`. Life, Narrative and UI learn about the pipeline only through Core events carrying primitives and the DB. The phone and the screens read DTOs and send player intent (`ResolvePending…`), never write.
- **DB:** parameterized, prepared, pooled statements in `PipelineQueries`. One batch per handler run (a calendar day's pipeline work, the rollover pass, one lock's signings). Baseball and Narrative writes never share a transaction. **No DDL:** v14 stays frozen until L-1's v15 window.
- **Spec rules kept:** 4 rounds × 32 = 128 picks on day 195 in reverse prior-season Pro order (no lottery, a seeded inaugural order; §9.3, R-1 Q09); class years 1–4 advance at rollover; declaration after the sophomore season or later; drafted players report next spring; Pro org cap 24; free agency only at the Sunday lock and only below the cap; unsigned free agents retire at rollover at age ≥ 30 or after 2 unsigned seasons; `ProContractSignedEvent` for **every** first Pro contract.
- **Content lines:** the pipeline never reads `Player_Identity` (L-1, not yet present) and never filters by gender or ethnicity. The Trailblazer route (temptation §8.1) and kin exclusion (`LineageQueries.IsKin`) are Phase L; 13-6 adds nothing that would need to read either, and the free-agent pool already excludes the avatar's children (D3).

## 3. Owner decisions (D1–D8)

**Decided by the owner, 2026-11-24:** D1-A, D2-A, D3-A, D4a-A, **D4b-C (the owner's own option, below)**, D5-A, D6-A, D7 as recommended, D8-A. The D4b follow-ups were also answered:

- **Slicing:** 13-6a builds the hook (release, game over, entering the parent phase, the takeover at 16). The three jobs and the training mini-game get their own brief, proposed for the owner's approval (§7).
- **Takeover:** a child takes over **at 16, as an HS freshman**.
- **Pro cuts:** a cut Pro avatar takes **the same path** as an undrafted senior.

### D1. The pipeline vs the sanctioned Pro guard

**Facts** (§1.2 #2): the guard world runs no offseason machinery, and its ledger spans 2026 and 2027.

- **A (recommended). Keep `PipelineManager` out of the guard world**, as `PromotionManager` and `DevelopmentManager` always were. The guard is the Pro *game sim's* regression check; the pipeline gets its own (`PipelineHarness`, §5). `season-pipeline` adds eight channels that prove the separation (Argon Q1, Amendment 7):
  1. **The guard is byte-exact (no pipeline):** 2026 PA 81433 / H 18471 / ER 8803; the two-season ledger SHA `932ACE26…8B84`.
  2. **The guard database has no pipeline rows:** after day 730, zero rows in `College_Enrollment`, `Recruiting_Offers` and `Draft_Picks`, no `pipeline_*` flags, and no `pipeline_seed` key.
  3. **A pipeline-attached twin matches 2026 exactly:** a twin of the guard world, run through day 365 (before the rollover), produces the same PA/H/ER, a byte-identical 2026 ledger, and the same 2026 Pro postseason (11 series, 30 games, champion 323). This proves the in-season pipeline days (176, 183, 195, 274, 318) change no game. 2027 is where the twin diverges, by design.
  4. **No lock does anything in the twin:** at all 27 locks there are 0 signings and 0 re-initializations. `LeagueSimulator.RngSnapshot` is unchanged across each lock, and at day 365 it equals the guard's. A warm lock allocates 0 B beyond the designation upsert.
  5. **The twin's calendar ran:** at day 365, 576 enrollment rows; 128 `Draft_Picks` rows for 2026 in inaugural order; 128 active `pipeline_pro_signed` flags; accepted 2026 offers = the 2026→2027 College openings.
  6. **The twin's Pro rosters didn't change in-season:** through day 365, every Pro team keeps its boot 24-player set (draftees stay on College teams), and every lock's 768 `Roster_Designations` rows match the guard's.
  7. **The twin's rollover moves players (positive control):** advancing to day 366 moves the 128 draftees to Pro, releases 128 incumbents with `pipeline_free_agent`, and keeps 32 × 15/4/5 = 768. The twin has no `CareerManager`, so nobody ages and HS intake is 0. The pipeline must run with no tier sims except Pro and no avatar (C3).
  8. **Existing channels stay green:** the 10 postseason channels (13-4 Q7) and the 10 designation/forfeit channels (13-5 D1-A).

  **Stop rule:** if channel 1 or 3 moves, the build stops and brings a fix or a sanction request (13-3 precedent).
- **B.** Wire the pipeline into the guard world and re-baseline the 2027 half of the ledger. It needs a 20/20 Argon reproduction and the owner's sanction, and it ties the guard to every future pipeline tuning.

### D2. The pipeline calendar and persistence

- **A (recommended).** Fixed, derived days (every one a non-Sunday; each a named rule in `TierSeasonCalendar` style, not a literal):
  - **Declaration opens, day 176** (the Monday after the Red Dirt Series' last possible day, 174). A College avatar with `class_year ≥ 2` gets the "Declare for the Draft?" decision with his projected round (D5).
  - **Declaration deadline, day 183** (the first Monday of July). NPCs with `class_year ≥ 2` declare iff `classYear ≥ 3 ∨ age ≥ 22 ∨ 1 ≤ projectedSlot ≤ 64` (Amendment 3). The `classYear ≥ 3` term is identical to `age ≥ 22` in steady state and keeps ≥ 193 declarers through the world-gen transient; expect ~253 declarers a season. An unanswered avatar decision resolves by the same rule; a class-4 senior is always in the pool. `declared_season` is written.
  - **The Draft, day 195** (the second Saturday of July, existing `DraftDay`). 128 picks in one batch, on the board of declared players ranked by value (`RoleRatingSum` with reliever compensation, Amendment 1). Each pick is best-available, with a role-need tiebreak (Amendment 5): a role short of its cap after known retirements, then a 3/1/1 role quota across the four picks, may take a player within `DraftRoleNeedMargin` 3.0 of the best. The batch also writes:
    - `Draft_Picks` rows and a `pipeline_pro_signed` flag per pick;
    - `declared_season = NULL` for undrafted underclassmen, who return (Amendment 3);
    - the avatar's signing bonus by slot (Amendment 5, avatar-only, D5).
    After the commit: `FundsImpulseEvent` for the bonus, `ProContractSignedEvent(wasDrafted: true)` per pick, then `DraftCompletedEvent`. **Drafted players stay on their College team until the rollover** (the College season is over); nobody joins a Pro roster mid-season.
  - **Offers, day 274** (the first Monday of October). Each program's `OpenSpots` by role = role cap minus returning players (`class_year < 4`, not drafted, not due to retire). The avatar, a just-finished HS senior, gets offers from every program whose floor `F(r) = 111.5 − 1.0·(r − 1)` (national rank `r`, `team_id` order if no College standings) he clears and that has an open spot in his role. He gets the best-ranked five, as `Recruiting_Offers` rows (status 0). With none, he gets one walk-on offer (D4a-A, Amendment 4).
  - **Signing Day, day 318** (the second Wednesday of November). The avatar's choice is applied first (unanswered → the best-ranked offering program). Then the NPC assignment runs: programs in last season's national-rank order take, round by round, the best available recruit for their most-needed role (largest fractional deficit). A team missing a field position takes the best recruit at it if he is within `RecruitPositionNeedMargin` 4.0 of the best (Amendment 4). This continues until every opening is filled.
    - The pool is HS seniors (`age ≥ 19` before the rollover) plus outside recruits. Outside recruits are rolled in memory: the role's deficit plus a surplus of 15/5/5, age 19, potential mean offset −2, generation discount age 25 (Amendment 2). So ~8% of HS seniors go unrecruited.
    - **Only outside recruits who sign are written**, with `team_id NULL` until the rollover.
    - Accepted rows get status 1, the avatar's other offers get 2, and anything left open gets 3.
  - **Rollover (day 1, last handler, after aging and development; C2 fixes the order):**
    1. Free agents carried in from earlier seasons retire at `age ≥ 30 ∨ health ≤ 40 ∨ NewSeasonYear − SeasonYearForDay(set_on_day) ≥ 2` (Amendment 6). A release from this rollover always gets at least one signable season.
    2. Rostered retirements (age ≥ 42 or health ≤ 40, the shipped triggers), flagged `pipeline_retired`.
    3. HS players with `age > 19` (seniors who just aged) graduate. Those with an accepted offer go to their College team as class 1; unrecruited NPCs are `pipeline_retired`. Signed outside recruits join their College team as class 1.
    4. College class years go up by 1. Draftees go to their Pro team (enrollment row deleted). Finished class-4 NPCs go undrafted to free agency. An undrafted class-4 **avatar is released** (D4b-C): game over without children, the parent phase with them.
    5. Pro releases bring each team to exactly 15/4/5, lowest value first per role, **the avatar included** (D4b-C). Released NPCs get `pipeline_free_agent`; a released avatar takes the D4b-C path instead.
    6. HS intake at **age 16** per (team, role, defensive position) refills the graduates.
    7. An unplanned College departure (a health retirement) isn't backfilled. That team runs one role short until the next Signing Day refills it.

    Then one batch, every sim and the micro-sim re-initialize once, and the avatar re-activates.
  - **Sunday locks 91–273:** before designations, a team below 24 signs the best free agent for its most-needed role. Signings happen only below the cap. The most short-handed team picks first, and free agents are valued at neutral P. A lock with a signing re-initializes the Pro sim and the micro-sim once, before the lock reads rosters. In practice this is the day-91 lock after the rollover's releases, and it's ~0–1 signing a season (Argon Q2). A free agent signed without a `pipeline_pro_signed` flag gets `ProContractSignedEvent(wasDrafted: false)`. The fast path reads the Pro sim's per-team role counts: 0 queries, 0 B.
  - **Boot:** an idempotent ensure writes the missing class-year rows for College-rostered players (world-gen and any straggler): `class_year = clamp(age − 17, 1, 4)`, `entry_season = season − (class_year − 1)`, `declared_season = NULL` (Amendment 3). It also writes `pipeline_seed` once, from the kept boot draw. A crash between a calendar day's commit and the pipeline's re-runs that day's step at boot, keyed on its persisted output (offer rows for the season, a `Draft_Picks` row for the season, and so on), so each step is done exactly once.
- **B.** Everything at the rollover (offers, Signing Day, declaration and draft on 1 January). It's much simpler, but it has no Draft Night in July, no fall signing, and no signed-prospect offseason (spec §9.3, §10).

### D3. Free-agent and career-end state (no DDL)

- **A (recommended). Engine-written `Entity_Flags`**, set and cleared only by `PipelineManager` (and by succession's displacement, §4 item 8):
  - `pipeline_free_agent`: active while a player is a Pro free agent; `set_on_day` = the day he became one. Unsigned seasons at the rollover into year Y = `Y − SeasonYearForDay(set_on_day)` (Amendment 6; `PreviousSeasonYear` would retire after three).
  - `pipeline_retired`: a baseball career is over (retirement, an unrecruited HS senior, a retired free agent). Never signable again.
  - `pipeline_pro_signed`: set at a player's first Pro contract. Its absence is what makes a signing "first" for `ProContractSignedEvent`.
  - The **pipeline's** free-agent pool is `team_id IS NULL ∧ pipeline_free_agent active`, with ratings (SQL validated, §4 item 3). An avatar's child, a retiree and an unrecruited teenager are never in it.
  - **Succession's backfill read keeps its unflagged window** (pre-existing, logged), with **two exclusions** (Amendment 6, C1). Succession runs at rollover *before* the pipeline, so without them it could take a signed outside recruit (`team_id NULL` until subscriber #4) or a retiree. The exclusions: `NOT EXISTS` an accepted `Recruiting_Offers` row, and `NOT EXISTS` an active `pipeline_retired` flag. Both were validated on a scratch v14 (§10.5). The `lineage-*` fixtures have neither kind of row, so they don't move. The players it displaces or retires get the matching flags. Switching its read fully to the flagged pool stays a follow-up sub-choice, because that changes the `lineage-*` fixtures.
  - Content doesn't read these flags in 13-6; if it ever does, they join `check_event_graph_integrity`'s engine-flag allowlist.
- **B. Derive the state** from `Draft_Picks`, stats rows and age, with no flags. "Unsigned seasons" and "first contract" then need stats-history scans, and retirees stay indistinguishable from free agents below 42.
- **C. No free agency in 13-6:** teams under the cap get generated replacement-level fillers. That's the simplest path, but it drops a spec rule and the undrafted-senior path.

### D4. The avatar's guarantees

Both parts keep the avatar rostered at every moment, which `CareerManager` requires (§1.2 #6).

- **a. HS → College.**
  - **A (recommended).** The walk-on guarantee replaces "held back": a senior always moves to College at the rollover after his senior season, by an offer he chose or by a walk-on. The walk-on offer comes from the **lowest-ranked program with an open spot in his role** (Amendment 4), so it costs nobody a place. Only if no program has one does it come from the rank-24 program, where he displaces its weakest same-role returning player (the succession displacement rule), who retires from baseball. The four-year HS grade gate is unchanged.
  - *B.* Keep held-back: a senior below every program's band repeats his senior year (no offers, no walk-on) and is recruited again next fall; the walk-on applies only after a second failed fall.
- **b. College → Pro and release.**
  - **C (owner, 2026-11-24: decided).** **An undrafted senior is released, and so is an avatar the Pro release order cuts**: Pro gives the avatar no protection.
    - **No children → game over:** `NoHeirs`, through the shipped `PersistGameOver`.
    - **Children → the parent phase.** The avatar leaves baseball (`team_id NULL`, `pipeline_retired`, so no free-agent read can pick him up), chooses one of **three jobs**, each with its own benefits and pitfalls, and spends his free time **training his children**. The training is a mini-game that can succeed or fail.
    - **The takeover:** a willing child (`baseball_interest ≥` threshold) takes over **at 16 as an HS freshman** and enters the pipeline as the avatar did. If every child is 16 or older and unwilling, it's game over (`NoWillingHeir`).
    - **13-6a builds the hook only;** the jobs and the mini-game are their own brief (§4 item 8, §7). An undrafted declared junior still returns to College.
  - *A (was recommended; superseded by C).* The avatar is **never released** in v1 (the 9c "promotes, never auto-relegates" precedent): an NPC of his role goes instead. An undrafted avatar who finishes class 4 gets a **guaranteed free-agent contract** from the lowest-ranked Pro team at the rollover (the walk-on rule, displacement included), with `ProContractSignedEvent(wasDrafted: false)`. An undrafted declared junior returns to College. Release, unsigned seasons and the career end they imply stay NPC-only until a follow-up gives the unrostered avatar a home (UI, Life and succession).
  - *B (not chosen).* Full spec: the avatar can be released or go undrafted into an unrostered free agency, retire unsigned and end the career into succession. It needs an unrostered-avatar mode in `CareerManager`, the Life bridge and the UI. That's a larger slice.

### D5. The avatar's decisions, texts and money

- **A (recommended). Succession-style pending choices + a Narrative writer.**
  - `PipelineManager` exposes `TryGetPendingRecruitingChoice` / `ResolveRecruiting(teamId)` and `TryGetPendingDeclaration` / `ResolveDeclaration(bool declare)`, with an `AutopilotPipeline` flag (headless default true; `GameManager` sets it false, as it does `AutopilotSuccession`). An unanswered choice resolves at its deadline by the autopilot rule: the best-ranked offer; declare per the NPC rule. **The clock doesn't stop**: the phone text names the deadline.
  - Events (primitives only, after commit): `RecruitingOffersEvent(PlayerId, SeasonYear, OfferCount, IsWalkOn)`, `RecruitSignedEvent(PlayerId, TeamId, SeasonYear, Day)` (the Signing Day cutscene hook, 16b), `DraftCompletedEvent(SeasonYear, Day, AvatarOverallPick, AvatarTeamId)` (0/0 when the avatar wasn't picked), and `ProContractSignedEvent(PlayerId, TeamId, Day, WasDrafted)` (spec §9.4, every first Pro contract, NPCs included).
  - `Assets/Narrative/Pipeline/PipelineBeatWriter` writes the avatar's phone texts in their own batch: offers (`coach_hs`), Signing Day (`coach_college`), the declaration (`agent_diaz`), Draft Night (`agent_diaz`), and the release (`coach_college` or `coach_reyes`, with a game-over or parent-phase variant). The text is placeholder `pipeline.*` keys with no tokens; Sonnet polishes it. The father's first text is Phase L's, keyed on `ProContractSignedEvent`.
  - **Signing bonus: avatar only** (`PlayerQueries.AdjustFunds` in the draft batch, then `FundsImpulseEvent` after the commit, the pattern every money writer uses: the event mirrors a committed delta into the Life sim and doesn't write). The scale is piecewise-linear by slot (Amendment 5): round 1 $50,000 → $22,100, round 2 $20,000 → $12,250, round 3 $11,500 → $6,075, round 4 $5,800 → $2,080. Each round's top value and per-pick slope are `PipelineProfile` values. NPC funds aren't touched, because the gritty-event dispatcher polls `Players.funds` for scope-any events, and 128 NPC bonuses a year would shift NPC event eligibility.
- **B.** Decisions as gritty-event JSON with choice consequences that write flags the pipeline reads. It reuses the dispatcher, but the dispatcher is weighted and polled, so an offer can arrive late or not at all, and the choice set (1–5 programs) is dynamic.

### D6. The criminal record in the pipeline

- **A (recommended).** `record_arrest` docks the avatar's value by `RecordArrestPenalty` = **1.0** Combine point (Amendment 6). That's one offer-floor step, or ~15–20 draft slots: one projected round at a boundary. The 9c value of 3.0 would cost three offers or nearly two rounds. The dock applies wherever the pipeline ranks him: his offer band and count, his projected round, NPC teams' draft boards, and his Pro release order (D4b-C, so the dock can now push a marginal Pro avatar out). `banned_for_life` (Pro-only by content, §1.2 #8) has no 13-6 effect, as approved; the `pipeline-record` suite proves it. Whether a ban should now force the D4b-C release path is an open item for the parent-phase brief (§7). NPCs are never read for either flag. The two constants move with the flag names to a new home (`CareerRecordFlags`), since `PromotionManager` is deleted and GrittyEventsHarness reads them.
- **B.** Drop the record from the pipeline (the dock was a 9c promotion rule).

### D7. Harnesses and runtime

- **Recommended:**
  - **MC default:** the 7 reds are replaced by green suites (§5) at **≤ +10 s** median paired difference against the §0 baseline (the 13-5 D7 method, interleaved pairs). `development-equilibrium`'s 80-generation ladder run leaves the default.
  - **New `Tools/PipelineHarness`** (opt-in; the gate for any change to the pipeline, its values or the development curve, and it joins `run_monte_carlo_batch`'s skill for those paths):
    - **default mode:** 20 seasons, offseason-only (calendar days and rollovers with no game sims, as 9d §7's equilibrium ran). It generates no `Schedule_Games` rows, and day 91 calls the free-agency hook directly. Performance reads neutral, so draft and recruiting order fall to their no-history rules. Budget ≤ 60 s (Argon forecasts ~5 s with its determinism twin);
    - **`--full` mode:** 1,193 days (3 seasons plus season 4 through day 98) with all three tier sims playing (real P, real standings, real draft order). It carries a harness-seeded age-19 HS-senior avatar (Amendment 7) through HS → recruited → College → sophomore declaration → drafted → Pro, ending with his first attended Pro game on day 1188 and three composed rollovers. The avatar is built so that he projects in the top 64 at day 176 of season 3, so autopilot declares him; the harness asserts he declared, was drafted and played (C4). Budget ≤ 4 min (Argon forecasts 10–27 s); it runs at the 13-6a build and review, not on every change.
- *B.* Everything in MC default (over budget by an order of magnitude); *C.* only `--full` mode (20 seasons with games, ~10+ min a run on this PC).

### D8. Slicing and the UI

- **A (recommended).** **13-6a:** engine, persistence, events, beat writer, harnesses, all decisions working headlessly and through the `Resolve…` API (Opus; Argon reviews). **13-6b:** the phone's decision cards (offers, declare) and the read-only Draft board, on `PipelineStatusView` (Sonnet; Neon reviews; restyled in 15c). `p13/integration` → `main` after **13-6a** merges and its gates are green, without waiting for 13-6b. Draft Night and Signing Day cutscenes stay 16b's.
- *B.* One slice including the UI (spec §15's original row); `main` waits for the UI.

## 4. Outputs (13-6a, per the recommended options)

1. **`PipelineRules`** (pure, no RNG, no DB):
   - value: `PromotionScore.Combine(P, S)` with the tier's shrink and the tier's league OPS/ERA, and the avatar's record dock (D6). The scouting sum is `RoleRatingSum`, which credits relievers `+LeagueGenerator.RelieverStaminaPenalty` stamina, clamped at 100 (Amendment 1). Without it, relievers take 6% of picks instead of 20% on the cross-role board. Within-role order is unchanged, and the gradient metric stays on raw sums;
   - the avatar's offer band and count, and the walk-on (Amendment 4); the NPC recruiting order with its position-coverage tiebreak;
   - projected slot (rank among `class_year ≥ 2` players), projected round, the declaration rule, the draft pick with its role-need tiebreak (Amendment 5), and draft order:
     - the prior-season order is reverse elimination round, then reverse `Standings.Order` within a round;
     - the inaugural order is keyed by `MixCombine(MixCombine(seed, "DRAFTORD"), MixCombine(year, teamId))` (Argon Q5);
   - release order (Pro value; the avatar is ranked like anyone else, D4b-C) and signing order (most short-handed team first, neutral-P value);
   - the free-agent retirement test (Amendment 6) and `SigningBonusForPick` (Amendment 5).

   Every constant is a named `PipelineProfile` data value. The values from Argon's §10:
   - recruiting and intake: `IntakeMinAge 16`, `IntakeAgeSpan 1`, `OutsideRecruitAge 19`, `OutsideRecruitMeanOffset −2`, `OutsideRecruitDiscountAge 25`, surpluses `15/5/5`;
   - offers: `OfferTopProgramFloor 111.5`, `OfferRankSlope 1.0`, `MaxAvatarOffers 5`, `RecruitPositionNeedMargin 4.0`;
   - draft: `DraftRoleNeedMargin 3.0`, `DraftRoleQuota 3/1/1`, the four bonus tops and slopes, `RecordArrestPenalty 1.0`;
   - free agency: `FreeAgentRetireAge 30`, `FreeAgentMaxUnsignedSeasons 2`;
   - stream tags: the four stream tags and `InauguralDraftOrderTag` (Q8, table below);
   - `DefaultPipelineSeed 0x136D_5EED_0000_0001` (harness fixtures only).
2. **`PipelineManager`:** the D2 handlers; the boot ensure; the rollover pass (one batch, flush before, re-init once after, `ReactivateAvatar` last, as `PromotionManager` did); the pre-lock free-agency hook `RosterManager` calls (`IFreeAgencyMarket`); the D5 pending choices; `PipelineStatusView` (this season's class, offers, projected round, draft board, free agents); a `LastRun` summary for UI and harnesses (removals, graduates, recruits, outside recruits, draftees, releases, signings, intake, retirements).
3. **`PipelineQueries`** + DTOs. Validated on a scratch v14 (§0.4) with these results:
   - enrollment upsert `INSERT INTO College_Enrollment (player_id, team_id, entry_season, class_year, declared_season) VALUES (…) ON CONFLICT (player_id) DO UPDATE SET team_id = excluded.team_id, class_year = excluded.class_year, declared_season = excluded.declared_season` (insert and update paths executed);
   - class advance `UPDATE College_Enrollment SET class_year = class_year + 1` and departure `DELETE FROM College_Enrollment WHERE player_id = @playerId`;
   - enrollment load `SELECT … FROM College_Enrollment ORDER BY player_id` (PK-ordered scan, ≤ ~600 rows);
   - offers upsert `… ON CONFLICT (player_id, team_id, season_year) DO UPDATE SET status = excluded.status`; the avatar's offers `WHERE player_id = @playerId AND season_year = @season ORDER BY team_id` (PK search on `player_id`); a season's accepted rows `WHERE season_year = @season AND status = 1 ORDER BY player_id`, and the expiry `UPDATE … SET status = 3 WHERE season_year = @season AND status = 0`. Both are scans: the table grows ~150–200 rows a season, bounded and read twice a year; add no index (no DDL);
   - draft insert and the class read `WHERE season_year = @season ORDER BY overall_pick` (PK search). Never query by `player_id` (no index); "was drafted" is the `pipeline_pro_signed` flag or the in-memory class;
   - the free-agent pool `SELECT p.player_id, …, f.set_on_day FROM Entity_Flags AS f JOIN Players AS p … JOIN Player_Ratings AS r … LEFT JOIN Pitcher_Roles AS pr … WHERE f.flag_name = @flag AND f.is_active = 1 AND p.team_id IS NULL ORDER BY p.player_id` (search on `idx_entity_flags_active_name`, PK probes, a temp B-tree for the order; an inactive flag and an unflagged unrostered player correctly excluded);
   - flags through the existing `PlayerQueries.SetFlag` upsert; standings and series through the existing `PlayoffQueries.LoadTeamSeasons` / `LoadSeries`;
   - **succession's free-agent read** (`BaseballQueries.SqlSelectFreeAgents`, Amendment 6 / C1) gains `AND NOT EXISTS (SELECT 1 FROM Recruiting_Offers AS ro WHERE ro.player_id = p.player_id AND ro.status = 1) AND NOT EXISTS (SELECT 1 FROM Entity_Flags AS ef WHERE ef.player_id = p.player_id AND ef.flag_name = 'pipeline_retired' AND ef.is_active = 1)`. The flag name is bound as a parameter in code.
     - Plan: `idx_players_team` search, two correlated subqueries, PK and `UNIQUE (player_id, flag_name)` probes, a temp B-tree for the order.
     - On throwaway rows it drops exactly the committed recruit and the active retiree. It keeps the flagged free agent, an unflagged adult child, a retiree whose flag is inactive, and a declined-offer holder.
   - **Streams** (Q8): each is `new RngState(MixCombine(MixCombine(pipelineSeed, tag), (ulong)year) | 1UL)`, derived afresh at each use, never held across days:

     | Stream | Tag | Year key |
     | :-- | :-- | :-- |
     | Outside recruits' ratings | `"PIPEOUTR"` | the fall season |
     | Outside recruits' fielding | `"PIPEORFL"` | the fall season |
     | HS intake ratings | `"PIPEHSIN"` | the completed season |
     | HS intake fielding: secondary position, speed, arm (`Player_Fielding` carries all three) | `"PIPEHSFL"` | the completed season |

     None collides with the five `PostseasonStreams` tags.
4. **Retire `PromotionManager`:** delete the manager and `PromotionSummary`; trim `PromotionProfile` to the value constants; move `RecordArrestFlagName`/`BannedForLifeFlagName` (D6); `GameManager` builds `PipelineManager` in `PromotionManager`'s slot with the same boot draw (§1.2 #7). The stale call-up and promotion-ladder comments in `PersonEffects.cs`, `ScoutingGrade.cs`, `SchoolGrade.cs` and `DevelopmentManager.cs` get the doc-only touch 13-5a §11.5 deferred (comments only).
5. **Free agency at the lock:** `RosterManager.Lock` calls the market first. With no team below 24 it's a no-op (0 B extra, no re-init). Otherwise the signings commit in their own batch, the Pro sim and the micro-sim re-initialize once, the avatar re-activates if his team changed, and only then do the designations run.
6. **Generated players get fielding rows:** HS intake inherits the departed player's defensive position (so every HS team keeps its field covered); outside recruits and fillers roll one from `DepthPositionWeights`. Draws come from the pipeline's derived streams and never from world-gen's.
   - Outside recruits use a `LeagueGenerator` overload: potential mean offset −2, and current from `DevelopmentCurve.RawCurrent(potential, OutsideRecruitDiscountAge = 25, …)` while `Players.age` stays 19. That matches an HS graduate's four development passes (remaining discount fraction 0.158 vs 0.167). With the chronological age 19 instead, College would average below HS (Argon Q4.4).
   - They're rolled in memory and written only if they sign (Amendment 2).
7. **Core events** (`CoreEvents.cs`, primitives only): `RecruitingOffersEvent`, `RecruitSignedEvent`, `DraftCompletedEvent`, `ProContractSignedEvent` (D5), `AvatarReleasedEvent` (D4b-C). Each publishes after its batch commits.
8. **Avatar (D4, D6):** the walk-on (an open spot first, then displacement, Amendment 4); the record dock.
   - **The D4b-C hook (13-6a builds this; the jobs and the mini-game don't):**
     - **Release** (rollover steps 4–5): the avatar gets `team_id NULL` and `pipeline_retired`. The flag also keeps succession's free-agent read from picking him up: `FindStrongestFreeAgent` excludes only the incoming heir.
     - **No children:** the shipped `PersistGameOver(NoHeirs)`.
     - **Children:** an additive `Game_State` key, `parent_phase_since_day`, marks the phase; its presence is the state, so there's no DDL. `CareerManager` gains an **unrostered-avatar mode**: `ReactivateAvatar` and `ActivateAvatar` don't throw, every sim's attended claim is cleared, the Life sim keeps running, and no attended or clutch game is offered.
     - A Core event, `AvatarReleasedEvent(PlayerId, Tier, SeasonYear, Day, EnteredParentPhase)`, publishes after the batch commits.
     - **Takeover** (the rollover, in the parent phase, before HS intake):
       - Look at the parent's children: `PlayerQueries.LoadRelationshipsFor` Child edges with `IsChildOf`'s direction check, exactly as `EvaluateSuccession` reads them (`CareerManager.cs:890–916`). A child who is 16 or older with `baseball_interest ≥ HeirGeneticsProfile.InterestPlayThreshold` is eligible. Selection follows succession's shipped rule (the player picks; autopilot takes the highest role sum, ties by `player_id`).
       - The heir joins **the family's HS team** as a freshman, by displacing the weakest same-role player, who retires. Stats rows carry no team, so the pipeline writes an additive `Game_State` key, `family_hs_team_id`, when an avatar leaves HS. If the key is missing (an avatar created above HS), the heir joins the lowest-`team_id` HS team. The shipped handoff then runs: re-point `avatar_player_id`, `dynasty_generation + 1`, clear `parent_phase_since_day`.
       - No eligible child, and every child 16 or older → `PersistGameOver(NoWillingHeir)`. Otherwise the phase continues.
       - Retirement-age succession (a Pro avatar reaching 42) keeps the shipped `MaturityAge 19` rule; aligning it to 16 is a parent-phase-brief item (§7).
     - The interactive takeover reuses the shipped pending-succession API. Headless and harness runs use the autopilot rule. Succession's displaced NPC gets `pipeline_free_agent` and its retiree gets `pipeline_retired`; its backfill read gains the two exclusions in item 3 (D3, C1).
   - **Optional collaborators (C3):** `PipelineManager` runs with any subset of tier sims, no `CareerManager`, no avatar and no `RosterManager`. That covers the D1-A twin, the offseason-only `PipelineHarness` and pure fixtures. A missing sim is simply not re-initialized, and with no avatar there are no avatar steps.
9. **Narrative (D5-A):** `PipelineBeatWriter`; `pipeline.*` keys in `strings.csv` (re-import `strings.en.translation` per the Godot headless note, keeping only the translation).
10. **Harnesses:** §5.
11. **Docs:** spec §9 / new §16-I19… synced to the approved D-options; the MC skill and a `PipelineHarness` entry; `progress.md`; the handoff (§8).
12. **13-5 Nit N1** (Argon §12.3): in `Slice135Suites.cs:322–348`, a C3 sub-fixture that swaps one team's raw #3/#4 SP order through `Confidence`/`Happiness` before `Initialize`, asserting `rawOrderDiffers > 0`.

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings / 0 errors |
| Schema | `dotnet run --project Tools/SchemaValidator -c Release` (plus `validate_sqlite_schema` on a scratch copy before any new SQL) | 174/174 (no DDL) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 32/32 + the new fixtures below |
| GrittyEvents | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 (the record flags read from their new home) |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | **all green** (the 7 reds replaced); resolver byte-identical; Pro guard byte-exact plus D1-A's channels; every allocation line 0 B, including the no-signing lock probe; 0 leaks; within D7 |
| PipelineHarness | `dotnet run --project Tools/PipelineHarness -c Release` and `-- --full` | all green (below); within D7 |
| 20× replay | MC default and PipelineHarness default, `--no-build`, ×20, normalized | 20/20 byte-identical each |

**MC default: the 7 reds, replaced** (one or two shared spread-25 worlds, the 13-5 Amendment 7 pattern):

- **`pipeline-score`** (from `promotion-sweep`): `PromotionScore` §2.1/§2.2 math unchanged (its three fixtures carried over); value per tier (HS shrink for HS seniors, College shrink for the draft); `RoleRatingSum` (a reliever and a starter with equal raw tools score 20 apart; the clamp at 100); projected slot and round. It also covers:
  - **the offer fixture (Argon Q4.5):** a batter senior with `P = 100` and headroom 0, so `V = 50 + roleSum/3`, gets:
    - 0 merit offers and a walk-on from rank 24 at 36/36/36 (V 86.00);
    - 1 offer (rank 24) at 39/39/38 (V 88.67);
    - 5 offers (ranks 13–17) at 50/50/50 (V 100.00);
  - the walk-on prefers an open spot;
  - `ShouldNpcDeclare` in steady state and in the transient;
  - draft order: from a fixture standings table (non-playoff by reverse record and I14 ties, playoff teams by elimination round, champion last) and the seeded inaugural order;
  - the role-need tiebreak and quota;
  - `SigningBonusForPick` (picks 1, 32, 33, 64, 65, 96, 97, 128 → 50,000 / 22,100 / 20,000 / 12,250 / 11,500 / 6,075 / 5,800 / 2,080; 0 and 129 → 0);
  - release order;
  - the free-agent retirement test (released into Y: kept at Y+1, retired at Y+2; age 30 retires).
- **`pipeline-stream`** (from `promotion-stream`): a pipeline with nothing to do (no seniors, no class 4, no declarations, every team at cap) is a complete no-op, and the next season is bit-identical with the pipeline attached vs absent. Each derived stream is reload-stable: a save reloaded mid-season produces the same intake and recruit ids at the next rollover.
- **`pipeline-avatar`** (from `promotion-avatar`):
  - the four-year grade gate (a max-built freshman stays in HS through three offseasons);
  - senior fall: 1–5 offers, the choice lands him on that program as class 1 at the rollover, the origin HS sim reclaims its team, `AvatarChangedEvent` republishes, his stats persist;
  - the walk-on (no offers) and its displacement;
  - declaration as a sophomore, the draft, Pro at the rollover with `ProContractSignedEvent(wasDrafted: true)` once, bonus credited once;
  - undrafted junior returns;
  - **D4b-C, the release:** an undrafted senior with no children → game over `NoHeirs`, published once; with children → the parent phase (unrostered, `pipeline_retired`, every sim's attended claim cleared, no throw). A Pro avatar cut by the release order takes the same path;
  - **the takeover:** in the parent phase, a willing child turning 16 takes over as an HS freshman at the rollover, and the pipeline then treats him as the avatar. All children 16 or older and unwilling → game over `NoWillingHeir`;
  - succession precedence: a pending succession skips the avatar's moves that offseason, NPC moves still run.
- **`pipeline-record`** (from `promotion-record`), Argon's Q9 fixtures. Clearing the record flips every case back, and NPCs are never read.
  - **A1:** 39/39/38 drops from 1 offer to the walk-on.
  - **A2:** 40/40/41 drops from 2 offers to 1.
  - **B:** a sophomore at 51/51/50 on the spread-0 board falls from slot 64 (round 2) to slot 96 (round 3).
  - **C:** `banned_for_life` changes nothing (D6-A).
- **`development-arc`** (rewritten): a developed HS prospect is recruited, drafted and reaches Pro; a health-worn Pro veteran declines, is released, goes unsigned and retires; never-overshoot holds; determinism.
- **`practice-seam`** (its §10.9 section rewritten): the one College opening goes to the high-headroom probe over its at-ceiling twin through recruiting.
- **`pipeline-lock`:** a team below 24 signs at the next lock, then re-initializes once; a full league's lock is a 0 B no-op; a released NPC is a free agent with the flag; an unflagged unrostered player (an avatar's adult child, an unrecruited senior) is never signed.
- **`season-pipeline` additions:** D1-A channels 2–7 (channels 1 and 8 are the existing checks), on one Pro-only 2026 twin. Argon forecasts +1.85 s; the whole MC default comes to about +1.8 s (range +1.2 to +2.8 s, Q10).

**`PipelineHarness` default (20 seasons, offseason-only):**

- **Conservation:** after every rollover and every opening lock, HS teams stay within their generated roster shape with the field covered, College teams hold their role counts (≤ 17 / ≤ 4 / ≤ 5, org ≤ 26), and Pro holds 32 × 15/4/5 = 768, or within ±5% if a role runs dry, with every shortfall reported.
- **Flows each season:** exactly 128 picks, unique players, all `Draft_Picks` rows legal; recruits = openings; intake = HS graduates; a population ledger per tier (in, out, by cause) that balances.
- **The journey:** at least one intake player traced HS → College → drafted → Pro, plus the count of intake players who reach Pro by season 20.
- **The equilibrium gradient** (Argon Q7, Amendment 7):
  - **Metric:** tier-mean **raw** role sums after each rollover, batters and pitchers separately.
  - **Seeds:** world `20261603` (9d §7), development `0x9D7000`, `DefaultPipelineSeed`.
  - **Window:** 5 burn-in seasons, then a 15-season window.
  - **Monotonicity:** HS < College < Pro, strict, with minimum gaps of batters +3.0 / +10.0 and pitchers +2.0 / +10.0. Observed: batters +6.8 / +18.3, pitchers +5.9 / +17.8.
  - **Stationarity:** the second half of the window is within 10.0 of the first for every tier (observed 1.7–2.5).
  - **Flows:** about 128 Pro arrivals a year, a Little's-law career of 6.0 seasons (spec §9.3), and a free-agent pool at the day-91 lock with every role stocked. These are reported, not gated.
- **Hygiene and determinism:** no unflagged player ever signed; no player in two tiers; enrollment rows only for College-rostered players; FK clean; no open batch; two same-seed runs byte-identical.

**`PipelineHarness --full` (1,193 days, all sims):**

- Real standings drive draft and recruiting order.
- Three composed rollovers (sims, roster, career, playoffs, development, pipeline) dispatch without throwing (§1.2 #1).
- The age-19 HS-senior avatar is recruited (2026), plays College (2027), declares as a top-64 sophomore and is drafted (2028), and plays his first attended Pro game on day 1188 (C4). Attended games keep working across both tier changes.
- After the third rollover, a single-snapshot gradient check holds: HS < College < Pro for batters and pitchers.

**CoreLoop fixtures:**

- a rollover with the live composition doesn't throw and lands the avatar on the right team;
- a pending recruiting or declaration choice survives a reload and resolves once;
- a reload on day 195 after the Draft's commit doesn't re-draft;
- the offers and Signing Day texts arrive on their days;
- the clock never stops for a pipeline decision;
- **D4b-C:** a released avatar in the parent phase survives a reload: still unrostered, no throw, the week advances, and no attended game is offered. A child's takeover at 16 lands him on the right HS team.

**Spec §15 acceptance:** the PipelineHarness gates above; the promotion suites retired or replaced, so `p13/integration` can merge to `main`.

## 6. Definition of done

- [ ] §0 recorded (the baseline and its wall time).
- [ ] D1–D8 applied as approved; Argon's §10 reconciled; the spec §9/§16 synced.
- [ ] §4 built; `PromotionManager` deleted; no ladder code or "call-up" comment left in `Assets/`.
- [ ] §5 gates green: MC default all green, the Pro guard byte-exact; the PipelineHarness green; 20× replays 20/20; D7 met.
- [ ] Argon's independent review of the sim/persistence diff passed.
- [ ] Committed and pushed; merged into `p13/integration` by Opus after the review. Then `p13/integration` → `main` per D8, **with the owner's go-ahead** (it changes `main`'s save version to v14).
- [ ] Handoff (§8) + one `progress.md` line per author. **Then stop.**

## 7. Out of scope

- The phone decision UI, the Recruiting Offers and Draft screens (13-6b / 15c), and the Signing Day / Draft Night cutscenes (16b). The drafting team's "we've drafted…" clip (14c).
- **The parent-phase brief (D4b-C follow-up, its own slice: Sonnet builds, Argon does the math, the owner approves the proposal first):**
  - the **three jobs**, each with its own benefits and pitfalls (money, free time, stress, risk);
  - the **child-training mini-game**, which can succeed or fail, with what success and failure change (the child's ratings within potential, `baseball_interest`), how much training the chosen job's free time allows, and its own seeded resolver (presentation never drives the sim);
  - the parent-phase UI and phone texts;
  - whether `banned_for_life` should force the release path;
  - aligning retirement-age succession to the takeover at 16.

  13-6a ships only the hook (§4 item 8). An unrostered avatar's unsigned free agency (D4b-B) isn't planned.
- The father's texts, the Trailblazer's "first", kin exclusion and identity (Phase L, keyed on `ProContractSignedEvent`).
- Succession's backfill read switching to the flagged pool (D3 sub-choice).
- College academic eligibility and the HS mercy rule (§16 open); mid-season trades (spec: the lock is the only roster-change moment).
- Any band, resolver, development-curve or guard change; any DDL; renaming `PromotionScore`.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built> · Commit: <sha> · Gates: build, Schema, CoreLoop, Gritty, MC (total, guard, alloc, wall), PipelineHarness (default, --full), 20× replays
Open: <unfinished items, risks, owner questions>
Recommended next: <slice> by <model> (reviewer: <model>)
```

Then add one compact line to [progress.md](../progress.md). Don't start the next slice.

## 9. Questions for Argon's Math check of this brief (answer in a new §10)

*Answered in §10 (Gemini 4 Argon, 2026-11-24) and reconciled into §§1–8 (§10.5). Kept as asked.*

1. **Guard separation (D1-A).** Confirm the pipeline has no path into the Pro game sim during 2026 (the in-season days only write pipeline tables, and the locks sign nobody when every team is at 24). Confirm the 2026-only twin is a sufficient channel, and complete the channel list.
2. **Population flows and conservation.** Give the steady-state flows per tier and role: HS graduates a season, College openings, outside recruits needed, the draft's role mix, Pro departures by cause, and the free-agent pool's size and role mix at the day-91 lock. Is ±5% reachable at all under D2/D3? What's the probability a role runs dry, and should a replacement-level filler (the succession rule) close it? Is the average Pro career ≈ 6 seasons (spec §9.3)?
3. **Ages and class years.** Confirm the graduation rule (grade Senior in the completed season, `SchoolGrades` unchanged) and the intake ages (spec's 15–16, or 16 only, so that every NPC has exactly four HS seasons). Size the world-gen transient (no first-fall seniors; College 18–22 vs steady-state 20–23). Set the boot ensure's class-year map for world-gen College ages (e.g. `clamp(age − 17, 1, 4)`) and confirm it doesn't starve the first draft.
4. **Recruiting.**
   - the value function per tier (`Combine` with HS shrink for seniors);
   - the program band that yields the avatar's 1–5 offers;
   - the NPC assignment order (rank-ordered rounds by role need) and position need;
   - the outside-recruit pool: its size beyond the deficit, so weak HS seniors can go unrecruited, its "slightly lower mean" (the offset), and its ages.
   Give a fixture where the avatar's offer count moves across 0/1/5.
5. **The Draft.**
   - projected slot;
   - the NPC declaration rule (projected ≤ 64 or age ≥ 22): sanity and the expected declarers a season;
   - the role-need tiebreak: a precise rule, for example a role short after known departures takes that role's best when it's within Δ of the overall best;
   - the draft-order tie keys and the inaugural seeded order's key (via `PostseasonStreams`' mixer with a new domain tag, or the pipeline seed);
   - the avatar's signing-bonus scale by slot, as a Pro Math data value, sized against the temptation loop's money.
6. **Pro turnover.** The release order (Pro value; is the age projection right for releases?), the free-agent signing order, and the free-agent retirement rule (≥ 30 or 2 unsigned seasons) against the equilibrium. Does the undrafted-senior pool keep the role mix supplied?
7. **The gradient gate.** The metric (role-summed talent, batters and pitchers separately), the burn-in and measurement window over 20 seasons, the stationarity bound, the seeds, and the expected margins under offseason-only mode. Does neutral P (no games) bias the gradient, and does `--full` need its own check?
8. **Stream contract.** Confirm one persisted `pipeline_seed` (written once from the kept boot draw) and derived streams per (season year, purpose): intake, outside recruits, fielding rolls, the inaugural draft order. Confirm no other draw is needed, and that a reload at any day reproduces the next rollover.
9. **The record dock.** Re-size `RecordArrestPenalty` on the pipeline's scale so it flips only marginal outcomes (one offer, one projected round), with fixtures.
10. **D7 split.** Forecast the MC default additions (≤ +10 s) and the PipelineHarness default (≤ 60 s) and `--full` (≤ 4 min) on the owner's PC, or propose trims.

---

## 10. Math check — Gemini 4 Argon, 2026-11-24

*Note: Prior commit `7202d65` was voided via `git revert` (`3a1b590`) because its `file:line` citations and SQL index/column names (`offer_id`, `idx_recruiting_offers_player`, `idx_draft_picks_season`) did not match `9277af5`; every citation, SQL plan, simulation number and fixture below is freshly derived directly from `c9d56ad` and `9277af5`.*

- **Verdict:** **APPROVE WITH CHANGES** (7 numbered amendments in §10.4 for Claude Opus 5.5 to reconcile into §§1–9 before the owner's D1–D8 sign-off and the 13-6a build).
- **Evidence base:**
  - Inspected and line-verified all 17 §1.1 seam rows and all 8 §1.2 findings directly against the `9277af5` tree (§10.2.1–§10.2.2).
  - Executed all §4 item 3 SQL statements on an isolated scratch v14 database (`%TEMP%\dnd_136_scratch_v14.db`, built with `sqlite3` over `Assets/Data/Database/SchemaDefinitions.sql`, `PRAGMA foreign_keys = ON`, `PRAGMA user_version = 14`), verifying insert/update/delete execution, `EXPLAIN QUERY PLAN`, `PRAGMA foreign_key_check` (`0` violations), and `PRAGMA integrity_check` (`ok`), then deleted the scratch file (§10.2.3).
  - Executed a bit-exact Python port of `RngState.cs` (`xoshiro256**` + `SplitMix64` + long-jump `Split()`), `LeagueGenerator.cs` (`RollRating` round-to-even + `RelieverStaminaPenalty = 20` + `RawCurrent` round-away-from-zero), `DevelopmentManager.cs`, `PromotionScore.cs`, and `PipelineManager` in `%TEMP%\dnd_136_probe.py` (cleaned up after execution) over 20 seasons across three world seeds (`20260703`, `20261603 = 20260703 + 900`, `20260704`), plus four ablations (`rp_comp = False`, `out_disc_age = 19`, `decl_fix = False`, and `noisy_p = True` with $\sigma_P = 10.0$).

---

### 10.1 Answers to §9 Questions (Q1–Q10)

#### Q1. Guard separation (D1-A) and complete channel list

1. **Why `PipelineManager` has no path into the Pro game sim during Season 1 (`2026`, days `1..365`):**
   - In `RunSeasonPipeline` ([Tools/MonteCarloHarness/Program.cs:504–627](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L504-L627)), the guard world runs **two Pro seasons (`2026–2027`, 730 days)** on one Pro `LeagueSimulator` (`SeasonSeed = 777`, flat `ratingSpread: 0`), with `RosterManager` and `PlayoffManager` wired and no `CareerManager`, `DevelopmentManager`, or `PromotionManager`. Because the sanctioned counting-ledger SHA-256 (`932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`) spans both `2026` and `2027`, attaching `PipelineManager` across the `2026 → 2027` rollover would move 128 drafted College players onto Pro teams, release 128 Pro incumbents, and re-initialize the Pro sim at the `2026 → 2027` rollover—altering the `2027` half of the ledger.
   - Within Season 1 (`2026`, days `1..365`, prior to the day-366 rollover), `PipelineManager` has **zero** path into the Pro game sim:
     - **Boot ensure (day 1):** writes `576` `College_Enrollment` rows (`team_id 201..224`) and `Game_State["pipeline_seed"]`. No Pro table, `Roster_Designations` row, or `LeagueSimulator` state is touched.
     - **27 Pro Sunday locks (days `91, 98, …, 273`):** at every Season-1 lock, all 32 Pro teams have `15 Pos + 4 SP + 5 RP = 24` players (`orgCount == 24`, zero role deficits) and the `pipeline_free_agent` pool is empty. The pre-lock free-agency hook takes its fast path (`0` signings, `0` DB writes, `0` Pro sim re-initializations, `0 B` allocation).
     - **Day 176 (June 25, Monday — declaration opens):** no avatar exists in the guard world (`avatarId == null`) $\implies$ no-op.
     - **Day 183 (July 2, Monday — declaration deadline):** scores draft-eligible College players (`class_year >= 2`) and sets `College_Enrollment.declared_season = 2026`.
     - **Day 195 (July 14, Saturday — `TierSeasonCalendar.DraftDay`):** orders the 32 Pro teams by the inaugural key (`Team_Seasons` has no `2025` rows), writes `128` `Draft_Picks` rows and `128` `Entity_Flags(pipeline_pro_signed = 1)` rows in its own batch, and clears `declared_season = NULL` for undrafted underclassmen (`class_year < 4`). **Drafted players remain on their College `team_id` (`201..224`) until rollover** (`Players.team_id` is untouched on day 195), so Pro Sunday locks on days `196..273` and the Pro postseason on days `244..275` see the exact same 768 Pro players.
     - **Days 274 & 318 (Oct 1 Monday & Nov 14 Wednesday — Offers & Signing Day):** run after the last Pro Sunday lock (`day 273`) and after the Pro regular season ends (`day 240`). On day 274, `avatarId == null` $\implies$ no-op; on day 318 (after Pro World Championship Game 5 on day 275), `OnSigningDay` writes only `Recruiting_Offers(status = 1)` and newly signed outside-recruit rows (`Players.team_id IS NULL`), which Pro never reads.
2. **Sufficiency of the 2026-only twin and complete 8-channel D1-A list for `season-pipeline` (Amendment 7):**
   - A 1-season (`2026`, days `1..365`) pipeline-attached twin of the guard world exercises **every** in-season pipeline hook (boot ensure, 27 pre-lock free-agency checks, days `176, 183, 195, 274, 318`) alongside a full Pro regular season and postseason, while advancing that twin 1 additional day (`day 366`, across the `2026 → 2027` rollover) provides the positive control proving rollover roster turnover works.
   - **Complete 8-channel D1-A guard-separation list in `season-pipeline`:**
     1. **Sanctioned 2-season Pro guard byte-exact (unwired):** Season 1 (`2026`) `PA == 81433`, `H == 18471`, `ER == 8803`; two-season (`2026–2027`) phase-0 counting-ledger SHA-256 `== 932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`.
     2. **Unwired guard pipeline-table emptiness:** After day 730 in the guard DB, `COUNT(*)` is `0` in `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks`, `Entity_Flags WHERE flag_name LIKE 'pipeline_%'`, and `Game_State WHERE key = 'pipeline_seed'`.
     3. **2026 pipeline-attached twin game-sim & ledger byte-identity:** Running a twin of the guard world with `PipelineManager` wired (`RosterManager` pre-lock free-agency hook active + calendar handlers registered) through day `365` of `2026` produces `PA == 81433`, `H == 18471`, `ER == 8803`, a `2026` phase-0 batting/pitching stat-ledger SHA-256 identical to the unwired guard world at day `365`, and identical `2026` Pro postseason results (`11` series, `30` games, champion `323`).
     4. **Twin Sunday-lock free-agency no-op, RNG invariance, and `0 B` allocation:** Across all 27 Pro Sunday locks (`days 91..273`) in the 2026 twin, `FreeAgencySignings == 0`, post-boot Pro sim re-initializations `== 0`, `LeagueSimulator.RngSnapshot` is unchanged across every lock and equals the unwired guard's `RngSnapshot` at day 365, and a warm no-signing Sunday lock allocates `0 B` beyond the designation upsert.
     5. **Twin pipeline calendar execution proof:** At day 365 of the 2026 twin, `College_Enrollment` has `576` rows, `Draft_Picks WHERE season_year = 2026` has `128` rows (32 teams × 4 rounds, ordered by the inaugural key), `Entity_Flags WHERE flag_name = 'pipeline_pro_signed' AND is_active = 1` has `128` rows, and `Recruiting_Offers WHERE season_year = 2026 AND status = 1` equals the `2026 → 2027` College openings (`class_year == 4` plus drafted underclassmen).
     6. **Twin Pro roster & designation invariance through day 365:** Every Pro team `301..332` in the twin retains its exact boot 24-player `PlayerId` set through day 365 (drafted College players stay on `team_id 201..224` on days `195..365`), and all `768` `Roster_Designations` rows at every lock match the unwired guard world byte for byte.
     7. **Day-366 rollover turnover proof (positive control):** Advancing the twin from day 365 to day 366 (the `2026 → 2027` rollover) moves all `128` drafted players onto Pro `team_id 301..332`, releases `128` Pro incumbents to `pipeline_free_agent` (`team_id IS NULL`), conserves `15 Pos / 4 SP / 5 RP = 24` per Pro team (`768` total), and changes the Pro roster `PlayerId` set as designed.
     8. **Existing 20 channels preserved:** All 10 postseason channels (13-4 Q7) and all 10 designation/forfeit channels (13-5 D1-A) in `season-pipeline` remain green.

---

#### Q2. Population flows, conservation, $\pm 5\%$ reachability, role run-dry odds, filler fallback, and average Pro career

1. **Cross-Role Draft Board Discovery — Reliever Stamina Compensation (Amendment 1):**
   - `LeagueGenerator.GeneratePlayer` ([LeagueGenerator.cs:119, 375–380](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L375-L380)) subtracts `RelieverStaminaPenalty = 20` from every Reliever's `potential.PitStamina` ceiling (`potential.PitStamina = Math.Max(0, potential.PitStamina - RelieverStaminaPenalty)`). As a result, an average Reliever's raw three-tool sum `PitStuff + PitControl + PitStamina` is centered at **`130`** (`Scouting = 86.67`) instead of **`150`** (`Scouting = 100.0`) for an average Batter or Starter—a **`-6.67` Combine-point handicap** on any cross-role board.
   - In 9c ([PromotionManager.cs:559–565](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L559-L565)), `SweepRole` ran separately per role, so Relievers never competed against Batters or Starters. In 13-6, the July Draft board ranks all three roles together. Without stamina compensation (`Ablation 1` on seed `20261603`), Relievers fall to only **`8.13 / 128` (`6.4%`)** of draft picks (`91.67 Pos [71.6%]`, `28.20 SP [22.0%]`, `8.13 RP [6.4%]`), starving Pro bullpen turnover.
   - **Exact fix (Amendment 1):** Define `PipelineRules.RoleRatingSum(in RosterPlayerRow row)` with Reliever stamina compensation equal to `LeagueGenerator.RelieverStaminaPenalty` (`20`), clamped to `100` on the stamina component:
     $$\text{RoleRatingSum}(\text{row}) = \begin{cases} \text{BatPower} + \text{BatContact} + \text{BatDiscipline} & \text{if } \text{Role} = \text{PitcherRole.None} \\ \text{PitStuff} + \text{PitControl} + \text{PitStamina} & \text{if } \text{Role} = \text{PitcherRole.Starter} \\ \text{PitStuff} + \text{PitControl} + \min(100, \text{PitStamina} + 20) & \text{if } \text{Role} = \text{PitcherRole.Reliever} \end{cases}$$
     Because `+20` is uniform across Relievers (and `potential.PitStamina <= 80` for generated Relievers, so `PitStamina + 20 <= 100` never clips), **within-role Reliever ordering is 100% invariant**, while on the cross-role Draft board all three roles share the `150` mean and the draft role mix matches the `15 : 4 : 5` (`62.5% : 16.7% : 20.8%`) Pro roster ratio.
2. **Steady-State Annual Population Flows per Tier and Role (`seasons 6–20`, rollovers `5..19`):**
   - Measured over the 15-season steady-state window on `WorldSeed = 20261603` (`LeagueSeed + 900`, the 9d §7 equilibrium seed at [Program.cs:6460](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L6460)) and cross-checked on `20260703` and `20260704`:

| Flow / Population Quantity (`20261603` [range across `20260703..20260704`]) | Batters (`None`) | Starters (`SP`) | Relievers (`RP`) | Total | Conservation / Analytical Formula |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **HS Rostered Population** (`12` teams, `11..14 / 3..4 / 2..4`) | `146.00` `[146–155]` | `43.00` `[42–43]` | `36.00` `[29–37]` | **`225.00`** `[220–234]` | **Exact 1-for-1 per `(team, role, pos)`**: each graduating senior is replaced by 1 intake player on the same team, role, and primary position. |
| **HS Graduates = HS Intake / yr** (`age == 19` in fall) | `35.87` `[35.9–38.7]` | `10.67` `[10.3–10.7]` | `9.00` `[7.5–9.3]` | **`55.53`** `[55.1–58.7]` | $\lambda_{\text{HS}} = N_{\text{HS}} / 4$ (every intake player enters at age `16` and graduates after 4 seasons at age `19`; theoretical mean $228 / 4 = 57.00/\text{yr}$). |
| **HS Graduates Recruited to College / yr** | `33.20` (`92.6%`) | `9.47` (`88.8%`) | `8.47` (`94.1%`) | **`51.13`** (`92.1%`) | Top simulated HS seniors out-score the lower tail of the `+15/+5/+5` outside-recruit surplus (`meanOffset = -2`). |
| **HS Graduates Unrecruited (`pipeline_retired`) / yr** | `2.67` (`7.4%`) | `1.20` (`11.2%`) | `0.53` (`5.9%`) | **`4.40`** (`7.9%`) | Retires at HS graduation (`team_id = NULL`, `pipeline_retired = 1`); never enters Pro FA. |
| **College Rostered Population** (`24` teams × `15 / 4 / 5`) | `360.00` | `96.00` | `120.00` | **`576.00`** | **Exact `15 / 4 / 5 = 24` per team (`576` total)** at every rollover. |
| **College Openings / yr** (`Drafted + Undrafted Seniors`) | `117.93` | `31.40` | `38.67` | **`188.00`** `[187.9–188.5]` | `128.00` drafted (`80.80 Pos, 21.93 SP, 25.27 RP`) + `60.00` undrafted `class_year == 4` seniors (`37.13 Pos, 9.47 SP, 13.40 RP`). College age/health retirements $= 0$. |
| **Outside Recruits Rolled / Signed into College / yr** | `97.07` rolled / **`84.73`** signed | `25.73` rolled / **`21.93`** signed | `34.67` rolled / **`30.20`** signed | `157.47` rolled / **`136.87`** signed | Rolled: $\max(0, \text{Open}(r) - \text{HS}(r)) + \text{Surplus}(r)$ (`+15/+5/+5`). Only signed recruits (`136.87/yr`) are persisted to SQLite. |
| **Draft Declarers / yr** (day 183) | `158.60` | `41.27` | `53.53` | **`253.40`** `[252.7–253.4]` | `~1.98x` the 128 draft slots (`1.96x Pos, 1.88x SP, 2.12x RP`). **`65.40/yr`** undrafted underclassmen return to College. |
| **Draft Picks / yr** (day 195, `32 × 4 = 128`) | `80.80` (`63.1%`) | `21.93` (`17.1%`) | `25.27` (`19.7%`) | **`128.00`** (`100%`) | Matches the `15 : 4 : 5` (`62.5% : 16.7% : 20.8%`) Pro roster shape via RP stamina compensation + `DraftRoleQuota` (`3/1/1`). |
| **Pro Rostered Population** (`32` teams × `15 / 4 / 5`) | `480.00` | `128.00` | `160.00` | **`768.00`** | `767–768` at rollover; **exact `768.00`** after the day-91 opening Sunday lock. |
| **Pro Departures by Cause / yr** (at rollover) | `0.07` age/health + `80.73` released | `0.40` age/health + `21.53` released | `0.07` age/health + `25.20` released | **`0.53` age/health + `127.47` released** | In steady state (`seasons 6–20`), declining veterans are released in their 30s (`127.47/yr`) before reaching age 42 (`0.33–0.53/yr`). |
| **Pro Free-Agent Pool at Day-91 Lock** (`pipeline_free_agent`) | `207.80` (`62.8%`) | `55.67` (`16.8%`) | `67.40` (`20.4%`) | **`330.87`** `[328.7–330.9]` | `127.47` newly cut Pro + `60.00` undrafted College seniors (all age `24 < 30`) + `~143.4` 2nd-year FAs under age 30. |
| **Day-91 Lock Free-Agent Signings & FA Retirements / yr** | `0.00–0.60` signed / `118.60` retired | `0.00–0.40` signed / `31.80` retired | `0.00–0.30` signed / `38.20` retired | **`0.00–1.30` signed / `188.60` retired** | Signings occur at day 91 whenever a Pro team's rollover age/health retirements in role $r$ exceed its July draft picks in role $r$; FA retirements (`188.60/yr`) balance FA inflows (`127.47 + 60.00 = 187.47/yr`). |

3. **Is $\pm 5\%$ reachable at all under D2/D3? Probability a role runs dry, and whether a generated filler should close it:**
   - **HS and College have $0.0\%$ population drift by construction** (`HS` replaces graduating seniors 1-for-1 per `(team, role, pos)`; `College` signs exact `OpenSpots[team, role]`).
   - **Pro sits at `-0.4% to 0.0%` (`765..768`) immediately after rollover and `0.0%` (`768 / 768`) after the day-91 opening Sunday lock.** Any Pro team where rollover age-42/health-40 retirements in role $r$ exceeded that team's July draft picks in role $r$ exits rollover at `23` (`RoleCap(r) - 1`) and immediately signs the top free agent of role $r$ at the day-91 opening lock (before Pro Opening Day on day 93).
   - **Role run-dry probability:** At the day-91 lock, the signable `pipeline_free_agent` pool contains **`~207 Pos, ~56 SP, ~67 RP`** (with a hard single-offseason floor of `>= 36 Pos, >= 9 SP, >= 12 RP` from that rollover's undrafted age-24 College seniors alone, plus `>= 79 Pos, >= 21 SP, >= 25 RP` from that rollover's Pro cuts). Against `0..3` openings in any role, the probability a role runs dry organically is **$p < 10^{-40}$**.
   - **Do NOT add a generated replacement-level filler fallback at the Sunday lock:**
     - `RosterManager.Lock` is an RNG-free pure function of persisted state ([RosterManager.cs:22–24](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/RosterManager.cs#L22-L24)); generating players at a Sunday lock would consume RNG draws on Sunday nights and create new `Players` rows mid-season.
     - More importantly, `PipelineHarness`'s $\pm 5\%$ Pro population check (`[730, 806]`) exists to detect conservation bugs in `PipelineManager`; a synthetic filler generator at the lock would mask a real leak. If a unit test artificially empties the free-agent pool for role $r$, `FillProOpeningsBeforeLock` signs as many free agents of role $r$ as exist (`0` if empty), and `RosterManager.Lock` designates from the remaining roster (`RosterRules.SelectPositionPlayers` / `SelectPitchers` already handle `< RoleCap` via `Math.Min`).
4. **Average Pro career length:**
   - By Little's Law ($L = \lambda W$), with steady-state Pro population $L = 768$ and annual Pro arrivals $\lambda = 128.0\text{ draftees} + (0.0\text{ to }0.5)\text{ undrafted-FA signings} \in [128.0, 128.5]/\text{yr}$:
     $$W = \frac{L}{\lambda} = \frac{768}{128.0} = \mathbf{6.00\text{ seasons}} \quad (\mathbf{5.98\text{ to }6.00\text{ seasons}})$$
     confirming spec §9.3 (`"768 Pro roster spots ÷ about 6-season average careers ≈ 128 a year"`).

---

#### Q3. Ages, graduation rule, intake ages, world-gen transient, and boot class-year map

1. **HS graduation rule (`SchoolGrades` unchanged):**
   - Keep `SchoolGrades` ([SchoolGrade.cs:32–52](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/SchoolGrade.cs#L32-L52): `FreshmanAge = 16`, `SeniorAge = 19`, `ForAge(age)` clamping `<= 16 → Freshman` and `>= 19 → Senior`) unchanged.
   - **Age threshold before vs after `CareerManager.AgeAllPlayers()`:**
     - On fall calendar days **274 (Offers Day)** and **318 (Signing Day)**—which fire on `DayAdvancedEvent` *before* the season boundary—an HS player is a graduating senior in `seasonYear` iff **`age >= SchoolGrades.SeniorAge` (`age >= 19`)** (`SchoolGrades.ForAge(age) == SchoolGrade.Senior`).
     - At the **day-1 rollover** (`SeasonRolledOverEvent`), `CareerManager.OnSeasonRolledOver` calls `_players.AgeAllPlayers()` ([CareerManager.cs:1385](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L1385)) *before* `DevelopmentManager` and `PipelineManager` run (`GameManager.cs:548, 577, 590`), incrementing every player's `Players.age` by `+1`. Consequently, at rollover, the HS seniors who just completed their age-19 senior season now have **`age > SchoolGrades.SeniorAge` (`age >= 20`)** (or hold an accepted `Recruiting_Offers(status = 1)` row for `completedSeasonYear`). Checking `age > SchoolGrades.SeniorAge` at rollover ensures rising seniors who just aged `18 → 19` inside `AgeAllPlayers()` are not graduated a year early.
2. **HS intake age (`IntakeMinAge = 16, IntakeAgeSpan = 1` — Amendment 2):**
   - Replace 9c's `IntakeMinAge = 15, IntakeAgeSpan = 2` ([PromotionManager.cs:111–112](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L111-L112)) with **age `16` only (`IntakeMinAge = 16, IntakeAgeSpan = 1`)**:
     - Because `PipelineManager` generates HS intake at rollover *after* `AgeAllPlayers()`, an intake player generated at `age = 16` plays Season $Y$ at age `16` (Freshman), Season $Y+1$ at age `17` (Sophomore), Season $Y+2$ at age `18` (Junior), and Season $Y+3$ at age `19` (Senior)—**exact 4 HS seasons for every NPC**, matching the avatar's `16 → 17 → 18 → 19` runway and eliminating the 5-year HS clamp artifact (`15 → 16 → 17 → 18 → 19`).
3. **World-gen transient sizing:**
   - **HS transient (1 season to steady-state graduation flow; 4 seasons to full cohort replacement):**
     - `LeagueGenerator.TierAgeRolls[(0, _)] = (15, 4)` ([LeagueGenerator.cs:25](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L25)) seeds world-gen HS players across ages **`{15, 16, 17, 18}`** (on `20260703`: `53` at 15, `51` at 16, `58` at 17, `58` at 18; on `20261603`: `67` at 15, `65` at 16, `41` at 17, `52` at 18; **`0` at age 19**).
     - Therefore, in **Season 1 (`2026`)**, there are **`0` simulated HS seniors (`age >= 19`)** on days 274 and 318 (unless a test seeds an age-19 senior avatar). All Season-1 College openings (`~247` openings) are filled by outside recruits, and Season-1 HS intake is `0`.
     - At the **`2026 → 2027` rollover**, `AgeAllPlayers()` shifts the four world-gen HS cohorts `{15, 16, 17, 18}` to **`{16, 17, 18, 19}`**. From **Season 2 (`2027`)** onward, the age-19 cohort graduates every fall and is replaced 1-for-1 by age-16 intake, putting HS into its exact `{16, 17, 18, 19}` steady-state age distribution in Season 2 and flushing 100% of world-gen HS players by the end of Season 4 (`2029 → 2030` rollover).
   - **College transient (4 seasons, `2026–2029`; completely flushed by Season 5 `2030`):**
     - `LeagueGenerator.TierAgeRolls[(1, _)] = (18, 5)` ([LeagueGenerator.cs:26](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L26)) seeds world-gen College players across ages **`{18, 19, 20, 21, 22}`** (on `20260703`: `125, 104, 122, 120, 105`; on `20261603`: `118, 111, 120, 107, 120`), whereas steady-state College freshmen enter at age `20` (`19` on Signing Day $+ 1$ at rollover) and occupy ages **`{20, 21, 22, 23}`** (`age == 19 + class_year`).
     - Because `class_year` increments by `+1` each rollover and every `class_year == 4` senior departs at rollover, **100% of world-gen College players depart by the `2029 → 2030` rollover (end of Season 4)**. From Season 5 (`2030`, rollover index `4`) onward, every College player entered via Signing Day at age `20`.
4. **Boot ensure class-year map (`clamp(age - 17, 1, 4)`) and transient declaration proof (Amendment 3):**
   - For any College-rostered player missing a `College_Enrollment` row at boot ensure (`EnsureCollegeEnrollment`), set:
     $$\text{class\_year} = \text{Math.Clamp}(\text{age} - 17, 1, 4), \qquad \text{entry\_season} = \text{seasonYear} - (\text{class\_year} - 1), \qquad \text{declared\_season} = \text{null}$$
   - On `20260703` (`20261603`), this partitions the 576 world-gen College players into:
     - `age 18 → class_year = 1` (Freshman): **`125`** (`118`) players
     - `age 19 → class_year = 2` (Sophomore, draft-eligible): **`104`** (`111`) players
     - `age 20 → class_year = 3` (Junior, draft-eligible): **`122`** (`120`) players
     - `ages 21–22 → class_year = 4` (Senior, graduating): **`225`** (`227`) players
   - **First draft (`2026`):** `451` (`458`) players have `class_year >= 2`, and `225` (`227`) seniors plus top-64 underclassmen (`~255` declarers) supply the `128` picks with zero starvation.
   - **Transient gap in Seasons 2–4 (`2027–2029`) under a literal `age >= 22` check (Amendment 3):**
     - Because all `~225` boot `class_year == 4` (`ages 21–22`) seniors depart at the `2026 → 2027` rollover, the remaining world-gen College cohorts (`boot ages 18, 19, 20` mapped to `cy = 1, 2, 3`) reach `class_year == 3` (Junior) at **age `20` (`< 22`)** and reach `class_year == 4` (Senior) at **age `21` (`< 22`)**!
     - Consequently, in Seasons 2 and 3 (`2027` and `2028`), **zero (`0`) College players have `age >= 22`**. If `ShouldNpcDeclare` were written literally as `classYear >= 2 && (projectedSlot <= 64 || age >= 22)` (omitting `classYear >= 3` / `classYear == 4`), only `64` players would declare for `128` picks in `2027`, `2028`, and `2029`! Even under `classYear == 4 || age >= 22 || projectedSlot <= 64` (`Ablation 3` on `20261603`), none of the age-20 Juniors declare unless in the top 64 (`160` declarers in Year 3 vs `195` with `classYear >= 3`, and if two cohorts are thin, `< 128`).
     - In steady state (`age = 19 + class_year`), `age >= 22` and `class_year >= 3` are **100% identical** (`age == 22` is `class_year == 3` Junior; `age == 23` is `class_year == 4` Senior). Writing:
       ```csharp
       public static bool ShouldNpcDeclare(int classYear, int age, int projectedSlot) =>
           classYear >= 2 && (classYear >= 3 || age >= 22 || (projectedSlot >= 1 && projectedSlot <= 64));
       ```
       is identical to the spec's rule in steady state while ensuring every Junior (`classYear == 3`) and Senior (`classYear == 4`) declares during the `2026–2029` world-gen age transient (`193–199` Year-3 declarers across seeds `20260703..20260704`).

---

#### Q4. Recruiting — value function, avatar's 1–5 offer band, NPC assignment order, outside-recruit pool, and 0/1/5 fixture

1. **Recruiting value function (`PipelineRules.RecruitingValue`):**
   - For every simulated HS senior (`age >= 19` on days 274/318) and every generated outside recruit (`age == 19`, `PA == 0 / Outs == 0`):
     - **Performance $P$:** `PromotionScore.BatterPerformance(in batLine, hsLeagueOps, PromotionProfile.HsBatterPaShrink)` (`35` PA shrink, [PromotionManager.cs:36](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L36)) for batters, or `PromotionScore.PitcherPerformance(outs, er, hsLeagueEra, PromotionProfile.HsPitcherIpShrink)` (`7.0` IP shrink, [PromotionManager.cs:42](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L42)) for pitchers. When `Pa == 0` / `OutsRecorded == 0` (all outside recruits and offseason-only runs), $P = \mathbf{100.0}$.
     - **Scouting $S$:** `PromotionScore.Scouting(PipelineRules.RoleRatingSum(in row), row.Age, PromotionScore.Headroom(in row, in pot, isPitcher))` ([PromotionManager.cs:254–275](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L254-L275)), where `RoleRatingSum` includes the `+20` Reliever stamina compensation (Amendment 1).
     - **Combined value $V$:**
       $$V = \text{PromotionScore.Combine}(P, S) - \begin{cases} \text{PipelineProfile.RecordArrestPenalty} & \text{if } \text{isAvatar} \land \text{avatarHasRecordArrest} \\ 0.0 & \text{otherwise} \end{cases}$$
2. **Program band yielding the avatar's 1–5 offers (`PipelineProfile` constants — Amendment 4):**
   - Order the 24 College programs `201..224` by completed-season `Team_Seasons.national_rank` ascending $r \in \{1..24\}$ (if `Team_Seasons` has no College rows for `seasonYear`, fall back to `team_id` ascending `201..224` so $r = \text{teamId} - 200$).
   - Define the minimum recruiting value floor $F(r)$ for national rank $r \in \{1..24\}$:
     $$F(r) = \text{OfferTopProgramFloor} - \text{OfferRankSlope} \cdot (r - 1)$$
     with **`OfferTopProgramFloor = 111.5`** and **`OfferRankSlope = 1.0`** (`MaxAvatarOffers = 5`):
     - Rank $r = 1$ (`#1` program): $F(1) = \mathbf{111.5}$
     - Rank $r = 12$ (mid-pack): $F(12) = \mathbf{100.5}$
     - Rank $r = 20$ (5-offer threshold): $F(20) = \mathbf{92.5}$
     - Rank $r = 23$ (2-offer threshold): $F(23) = \mathbf{89.5}$
     - Rank $r = 24$ (1-offer threshold): $F(24) = \mathbf{88.5}$
   - **Selection rule (`PipelineRules.SelectAvatarOffers`):**
     1. Compute each College program's open spots in `avatarRole`:
        $$\text{OpenSpots}(c, \text{role}) = \max\bigl(0, \text{RoleCap}(\text{role}) - \text{ReturningCount}(c, \text{role})\bigr)$$
        where `ReturningCount(c, role)` counts current players on team $c$ in `role` who are not departing at rollover (`class_year < 4`, not drafted in July, `age < 41`, `health_ceiling > 40`).
     2. Collect all programs $c$ with `OpenSpots(c, avatarRole) > 0` and $V_{\text{avatar}} \ge F(\text{rank}(c))$, ordered by `(rank(c) ↑, teamId ↑)`, and take the first **`Math.Min(5, qualifying.Count)`** programs as merit offers (`Recruiting_Offers` rows with `status = 0`, `IsWalkOn = false`). Because $F(r)$ decreases by `1.0` per rank, an avatar with $V \in [88.5, 92.5)$ receives `1..4` merit offers from the lowest-ranked programs, while an avatar with $V \ge 92.5$ receives `5` merit offers from the **5 highest-ranked qualifying programs** (`#1..#5` at $V \ge 111.5$; `#13..#17` at $V = 100.0$).
     3. **D4a-A walk-on fallback (`0` merit offers):** If `qualifying.Count == 0`, set `IsWalkOn = true` and issue **1 walk-on offer (`status = 0`) from the lowest-ranked College program (highest $r$, tiebreak highest `teamId`) that has `OpenSpots(c, avatarRole) > 0`** (consuming an open spot cleanly without cutting a returning player). Only if all 24 College programs have `OpenSpots(c, avatarRole) == 0` does the walk-on offer go to rank-24 (highest `teamId = 224`) and displace that program's lowest-valued returning non-avatar player of `avatarRole` (`pipeline_retired`) at rollover.
3. **NPC assignment order and position need (Signing Day, day 318):**
   - **Step 1 (Avatar resolution):** If the avatar is an HS senior with open `Recruiting_Offers(status = 0)` for `seasonYear`, apply his chosen offer (or if unanswered, the highest-ranked offering program: lowest $r$, tiebreak lowest `teamId`) as `status = 1`, mark his other offers `status = 2` (when chosen manually) or `status = 3` (expired on autopilot; `UPDATE Recruiting_Offers SET status = 3 WHERE season_year = @season AND status = 0` cleans up any remaining status-0 rows), and decrement the chosen program's `OpenSpots[avatarRole]` by `1` (down to `0`).
   - **Step 2 (Pool construction):** For each role $r \in \{\text{None}, \text{Starter}, \text{Reliever}\}$, merge all remaining non-avatar simulated HS seniors (`age >= 19`) of role $r$ with the generated outside-recruit candidates of role $r$, sorted by `(RecruitingValue ↓, PlayerId ↑)`.
   - **Step 3 (Rank-ordered rounds by role need + position-coverage tiebreak):**
     - Loop in rounds while any College team has $\sum_r \text{OpenSpots}[c, r] > 0$:
     - Within each round, iterate College teams $c$ in order of `(nationalRank ↑, teamId ↑)` ($r = 1..24$). Skip team $c$ if $\sum_r \text{OpenSpots}[c, r] == 0$.
     - Team $c$ selects its most-needed role $r^*$ with $\text{OpenSpots}[c, r^*] > 0$ maximizing the fractional deficit $\text{OpenSpots}[c, r] / \text{RoleCap}(r)$ (tiebreak largest integer $\text{OpenSpots}[c, r]$, then `None → Starter → Reliever`).
     - **Position-coverage tiebreak (when $r^* == \text{PitcherRole.None}$):** Let `coveredPositions` be the bitmask of primary field positions `1..8` (`C..RF`, [LeagueGenerator.cs:253–255](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L253-L255)) covered by team $c$'s returning position players plus recruits already committed to team $c$. If any field position $p \in \{1..8\}$ is uncovered in `coveredPositions`, find the highest-valued available position recruit with `DefensivePosition == p`; if his `RecruitingValue` is within **`RecruitPositionNeedMargin = 4.0`** Combine points of the overall highest-valued available position recruit, sign him; otherwise sign the overall highest-valued available recruit of role $r^*$.
4. **Outside-recruit pool (size, mean offset, maturity discount age, ages, persist-on-sign — Amendment 2):**
   - **Count rolled in memory per role $r$:**
     $$N_{\text{out}}(r) = \max\bigl(0, \text{TotalOpenSpots}(r) - N_{\text{HS,non-avatar}}(r)\bigr) + \text{OutsideRecruitSurplusFor}(r)$$
     with **`OutsideRecruitSurplusBatters = 15`**, **`OutsideRecruitSurplusStarters = 5`**, **`OutsideRecruitSurplusRelievers = 5`** (`+25` total surplus beyond the league deficit). Across 20 seasons, this rolls `~157.5` outside candidates/year (`97.1 Pos, 25.7 SP, 34.7 RP`), signs `~136.9/yr`, and leaves the weakest **`~4.4/yr` (`7.9%`)** of simulated HS seniors unrecruited (`pipeline_retired` at rollover).
   - **Age & mean offset:** Generated at **`OutsideRecruitAge = 19`** on Signing Day (`20` after rollover `AgeAllPlayers()`) with **`OutsideRecruitMeanOffset = -2`** (potential bell centered at `50 - 2 = 48` vs `50` in `LeagueGenerator.RollRating`, [LeagueGenerator.cs:520–528](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L520-L528)).
   - **Maturity discount age (`OutsideRecruitDiscountAge = 25` — required to prevent `HS > College` gradient inversion):**
     - A simulated HS senior entering College at age 20 has already undergone **4 annual `DevelopmentManager` passes** (`ages 16→17, 17→18, 18→19, 19→20`). Because each pass closes $\text{GrowthRate} \cdot \text{YouthWeight}(\text{age}) = 0.5 \cdot \frac{27 - \text{age}}{12}$ of the remaining `(potential - current)` gap ([DevelopmentManager.cs:134–180](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/DevelopmentManager.cs#L134-L180)), after 4 HS passes the unclosed fraction of his initial age-16 discount (`YouthWeight(16) = 11/12 = 0.9167`) corresponds to an effective `YouthWeight` of:
       $$\frac{11}{12} \times \left(1 - \frac{5}{12}\right)\left(1 - \frac{4.5}{12}\right)\left(1 - \frac{4}{12}\right)\left(1 - \frac{3.5}{12}\right) = 0.9167 \times 0.5833 \times 0.6250 \times 0.6667 \times 0.7083 = \mathbf{0.1578}$$
     - Outside recruits are not on an HS roster during `DevelopmentManager.RunDevelopment` ([DevelopmentManager.cs:358](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/DevelopmentManager.cs#L358) loads only `team_id IS NOT NULL`). If `DevelopmentCurve.RawCurrent` is called with their chronological `generationAge = 19` (`YouthWeight(19) = 8/12 = 0.6667`, over $4\times$ larger than `0.1578`), outside recruits (`73%` of each incoming College class) enter College raw, dragging the 15-season steady-state College batter mean down to **`133.72 < HS 135.12` (`Ablation 2`, inverting the `HS < College` talent gradient)**.
     - Passing **`OutsideRecruitDiscountAge = 25`** (`YouthWeight(25) = 2/12 = 0.1667 \approx 0.1578`) into `DevelopmentCurve.RawCurrent(potential, OutsideRecruitDiscountAge, ref discountRng)` matches the 4-pass HS graduate maturity while keeping `Players.age = 19` (`20` after rollover) and `OutsideRecruitMeanOffset = -2`, producing **`HS 135.12 < College 141.94 < Pro 160.21`** (`+6.82` and `+18.27` role-sum points).
   - **Persist only signed outside recruits:** Roll all $N_{\text{out}}(r)$ candidates in memory from the `(seasonYear, OutsideRecruitsTag)` and `(seasonYear, OutsideRecruitsFieldingTag)` streams, score them alongside the simulated HS seniors, and write `Players`, `Player_Ratings`, `Player_Potential`, `Player_Fielding`, `Pitcher_Roles`, `Pitch_Arsenals`, and `Recruiting_Offers(status = 1)` rows **only for the outside recruits who sign with a College team** (discarding unselected surplus candidates in memory so `Players` never accumulates ~21 orphan rows/year).
5. **Concrete `0 / 1 / 5` Offers Fixture (for `pipeline-score` and `pipeline-avatar`):**
   - **Setup:** HS senior batter avatar (`age = 19`, `PitcherRole.None`), `headroom = 0` (`potential == current` on all tools), `Pa = 0` ($P = 100.0$), clean record (`record_arrest` inactive), all 24 College programs (`201..224`, national ranks `1..24`) have `OpenSpots[Batter] > 0`.
   - **Closed-form value:** With `age = 19`, `headroom = 0` ($\implies \text{ProjectionBonus}(19, 0) = 0$ at [PromotionManager.cs:235–245](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L235-L245)), and $P = 100.0$:
     $$S = 100.0 \times \frac{\text{roleSum}}{150.0} = \frac{2}{3}\text{roleSum}, \qquad V = 0.5(100.0) + 0.5 S = 50.0 + \frac{\text{roleSum}}{3.0}$$
   - **Offer cases against $F(r) = 111.5 - (r - 1)$ ($F(24) = 88.5, F(23) = 89.5, F(20) = 92.5, F(17) = 95.5, F(13) = 99.5, F(12) = 100.5$):**
     1. **`0` merit offers (`1` walk-on offer, `IsWalkOn = true`):** Avatar ratings `36 / 36 / 36` ($\text{roleSum} = 108 \implies S = 72.00 \implies V = \mathbf{86.00} < F(24) = 88.5$). Zero programs qualify on merit; D4a-A issues `1` walk-on offer (`OfferCount = 1, IsWalkOn = true`) from the lowest-ranked program with an open batter spot (`teamId = 224, nationalRank = 24`).
     2. **`1` merit offer (`OfferCount = 1, IsWalkOn = false`):** Avatar ratings `39 / 39 / 38` ($\text{roleSum} = 116 \implies S = 77.3333 \implies V = \mathbf{88.6667} \in [F(24) = 88.5, F(23) = 89.5)$). Qualifies at rank `24` only (`teamId = 224`).
     3. **`5` merit offers (`OfferCount = 5, IsWalkOn = false`):** Avatar ratings `50 / 50 / 50` ($\text{roleSum} = 150 \implies S = 100.00 \implies V = \mathbf{100.00} \in [F(13) = 99.5, F(12) = 100.5)$). Ranks `13..24` (`12` programs) qualify; capped at `MaxAvatarOffers = 5` highest-ranked qualifying programs, yielding offers from ranks **`13, 14, 15, 16, 17`** (`teamIds 213..217`).

---

#### Q5. The Draft — projected slot, declaration rule, role-need tiebreak, draft-order tie keys, and signing-bonus scale

1. **Projected slot and projected round (`PipelineRules.ComputeDraftBoard`):**
   - Rank all rostered College players (`team_id 201..224`) who are draft-eligible (`College_Enrollment.class_year >= 2`) by `(DraftValue ↓, PlayerId ↑)`, where `DraftValue` is `PromotionScore.Combine(P_College, S)` (`BatterPaShrink = 200, PitcherIpShrink = 40.0` against College `leagueOps`/`leagueEra`, `RoleRatingSum` with `+20` RP stamina compensation, `age`, `headroom`) minus `PipelineProfile.RecordArrestPenalty` (`1.0`) if the player is an arrested avatar.
   - `projectedSlot` is the player's 1-based rank ($1..N_{\text{elig}}$) on that board (`0` if `class_year < 2`).
   - `ProjectedRound(int projectedSlot)` is `(projectedSlot >= 1 && projectedSlot <= 128) ? ((projectedSlot - 1) / 32 + 1) : 0` (`1..4` for slots `1..128`, `0` for `> 128` = projected undrafted).
2. **NPC declaration rule and expected declarers (`PipelineRules.ShouldNpcDeclare` — Amendment 3):**
   - On day 183 (July 2, declaration deadline), every draft-eligible College player (`class_year >= 2`) is evaluated:
     - **Avatar:** if an unanswered declaration choice is pending on day 183 (or `AutopilotPipeline` is true), resolves by the NPC rule (`ShouldNpcDeclare(classYear, age, projectedSlot)`); if the avatar explicitly called `ResolveDeclaration(bool declare)` during days `176..183`, that choice stands (except `class_year == 4` seniors, whose eligibility ends after year 4 per spec §9.2 and who are automatically in the pool).
     - **NPCs:** declare (`declared_season = seasonYear`) iff:
       $$\text{classYear} \ge 2 \;\land\; \bigl(\text{classYear} \ge 3 \;\lor\; \text{age} \ge 22 \;\lor\; (1 \le \text{projectedSlot} \le 64)\bigr)$$
   - **Expected declarers per season:** In steady state (`seasons 6–20`), this yields **`253.4` declarers/season** (`158.6 Pos, 41.3 SP, 53.5 RP`, roughly `1.98x` the `128` draft picks across all three roles). After the 128 picks are made on day 195, **`65.4/yr`** undrafted Sophomores/Juniors (`class_year < 4`) have `declared_season` cleared back to `NULL` and return to College, while **`60.0/yr`** undrafted Seniors (`class_year == 4`) enter `pipeline_free_agent` at rollover.
3. **Draft role-need tiebreak (`PipelineRules.SelectDraftPick` — Amendment 5):**
   - For each Pro team $t \in \{301..332\}$, initialize `teamRoleCount[t, r]` before Round 1 to the count of current players on team $t$ in role $r$ who will not age/health-retire at rollover (`age < 41 && health_ceiling > 40`), and initialize `draftedInRole[t, r] = 0`.
   - At each of the 128 draft slots (`round = 1..4`, `pickInRound = 1..32` for team $t$):
     1. Let `bestOverall` be the highest-ranked available declared player on the draft board ($V_{\text{best}} = \text{bestOverall.Value}$).
     2. **Immediate roster-cap deficit check:** If any role $r$ has $\text{Deficit}(t, r) = \text{RoleCap}(r) - \text{teamRoleCount}[t, r] > 0$, look for available declared players in roles with $\text{Deficit}(t, r) > 0$ whose value satisfies $V \ge V_{\text{best}} - \text{DraftRoleNeedMargin}$ (**`DraftRoleNeedMargin = 3.0`** Combine points). If at least one exists, pick the candidate maximizing `(Deficit(t, r) ↓, Value ↓, PlayerId ↑)`.
     3. **Balanced 4-pick role-quota check (`DraftRoleQuota`: `Pos = 3, SP = 1, RP = 1`):** Otherwise, if `draftedInRole[t, bestOverall.Role] >= DraftRoleQuota(bestOverall.Role)`, look for available declared players in roles $r$ with `draftedInRole[t, r] < DraftRoleQuota(r)` whose value satisfies $V \ge V_{\text{best}} - \text{DraftRoleNeedMargin}$ (`3.0`). If at least one exists, pick the candidate maximizing `(Value ↓, PlayerId ↑)`.
     4. **Default best-player-available:** Otherwise pick `bestOverall`.
     5. Increment `teamRoleCount[t, pick.Role]++` and `draftedInRole[t, pick.Role]++`.
4. **Draft-order tie keys and inaugural seeded order key (`PipelineRules.OrderProDraftTeams`):**
   - **With prior Pro season history (`Team_Seasons` has 32 Pro rows for `seasonYear - 1`):**
     - Partition the 32 Pro teams into 6 elimination buckets from `Team_Seasons` (`seed`, [PlayoffQueries.cs:197](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayoffQueries.cs#L197)) and `Playoff_Series` (`season_year = seasonYear - 1, tier = LeagueTier.Pro`, [PlayoffQueries.cs:235](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayoffQueries.cs#L235)):
       - `bucket = 0`: non-playoff teams (`seed IS NULL`, 20 teams → picks `1..20`)
       - `bucket = 1`: eliminated in Wild Card (`round = 1` loser, 4 teams → picks `21..24`)
       - `bucket = 2`: eliminated in Division Series (`round = 2` loser, 4 teams → picks `25..28`)
       - `bucket = 3`: eliminated in Conference Final (`round = 3` loser, 2 teams → picks `29..30`)
       - `bucket = 4`: World Championship runner-up (`round = 4` loser, 1 team → pick `31`)
       - `bucket = 5`: World Champion (`round = 4` winner, 1 team → pick `32`)
     - Within each bucket `0..3`, rank the bucket's teams in the **exact reverse of `Standings.Order(table, allProTeamIds, scheduleSeed, seasonYear - 1, LeagueTier.Pro)`** ([Standings.cs:141–165](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/Standings.cs#L141-L165)): worst regular-season standing picks earliest (if `Standings.Order` assigns 0-based league standing index $s \in \{0..31\}$ where `0` is best record and `31` is worst record, teams within a bucket sort by **$s$ descending**).
     - *Table-only fallback (when `Schedule_Games` has no rows for `seasonYear - 1`, e.g. unit fixtures that seed `Team_Seasons` rows directly without game rows):* within each bucket, sort by `(WinPct ↑, RunDifferential ↑, RunsAllowed ↓, PostseasonStreams.StandingsCoinKey(scheduleSeed, seasonYear - 1, LeagueTier.Pro, teamId) ↓, teamId ↓)`.
   - **Inaugural draft (`Team_Seasons` has no Pro rows for `seasonYear - 1`, e.g. Season 1 `2026` on day 195, or `PipelineHarness` offseason-only mode):**
     - Order the 32 Pro teams `301..332` by `(InauguralDraftKey(pipelineSeed, seasonYear, teamId) ↑, teamId ↑)` using `PostseasonStreams.MixCombine` ([PostseasonStreams.cs:35](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PostseasonStreams.cs#L35)):
       $$\text{InauguralDraftKey}(\text{pipelineSeed}, \text{seasonYear}, \text{teamId}) = \text{PostseasonStreams.MixCombine}\Bigl(\text{PostseasonStreams.MixCombine}\bigl(\text{pipelineSeed}, \texttt{0x4452\_4146\_544F\_5244UL}\bigr), \;\text{PostseasonStreams.MixCombine}\bigl(\text{unchecked}((\text{ulong})\text{seasonYear}), \text{unchecked}((\text{ulong})\text{teamId})\bigr)\Bigr)$$
       with domain tag **`InauguralDraftOrderTag = 0x4452_4146_544F_5244UL`** (`"DRAFTORD"`).
5. **Avatar signing-bonus scale (`PipelineRules.SigningBonusForPick` — Amendment 5):**
   - **Sizing against the economy:** In [items.json:3–114](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Items/items.json#L3-L114), everyday items (`skateboard`, `commuter_bike`, `mall_wardrobe`, `silver_chain`, `gaming_console`, `laptop`) cost `\$80–\$700`, mid-tier items (`used_sedan`, `designer_jacket`, `gold_watch`) cost `\$850–\$2,500`, and top status items (`new_coupe`) cost `\$9,000`, while criminal buyouts and Phase L temptations / substances draw thousands to tens of thousands of dollars.
   - Define `PipelineRules.SigningBonusForPick(int overallPick)` (`0` when `overallPick < 1 || overallPick > 128`) as the piecewise-linear integer schedule across the 4 rounds:
     $$\text{SigningBonusForPick}(p) = \begin{cases} 50\,000 - 900 \cdot (p - 1) & 1 \le p \le 32 \quad (\text{Round 1: } \texttt{\$50,000} \to \texttt{\$22,100}) \\ 20\,000 - 250 \cdot (p - 33) & 33 \le p \le 64 \quad (\text{Round 2: } \texttt{\$20,000} \to \texttt{\$12,250}) \\ 11\,500 - 175 \cdot (p - 65) & 65 \le p \le 96 \quad (\text{Round 3: } \texttt{\$11,500} \to \texttt{\$6,075}) \\ 5\,800 - 120 \cdot (p - 97) & 97 \le p \le 128 \quad (\text{Round 4: } \texttt{\$5,800} \to \texttt{\$2,080}) \\ 0 & \text{otherwise} \end{cases}$$
   - Named `PipelineProfile` constants: `Round1TopBonus = 50_000, Round1PickSlope = 900; Round2TopBonus = 20_000, Round2PickSlope = 250; Round3TopBonus = 11_500, Round3PickSlope = 175; Round4TopBonus = 5_800, Round4PickSlope = 120`.
   - **Mandatory `FundsImpulseEvent` publication (Amendment 5):** Per [PlayerQueries.cs:425–434](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayerQueries.cs#L425-L434) and [CoreEvents.cs:128–144](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/CoreEvents.cs#L128-L144), `_players.AdjustFunds(avatarId, bonus)` writes `Players.funds` inside the Draft Day transaction, **and after the batch commits `PipelineManager` must publish `_bus?.Publish(new FundsImpulseEvent(avatarId, bonus))`** so `LifeSimManager`'s in-memory funds mirror updates immediately on day 195.

---

#### Q6. Pro turnover — release order, free-agent signing order, and free-agent retirement vs equilibrium

1. **Pro release order at rollover (`PipelineRules.SelectProReleases`):**
   - At rollover, `PipelineManager` first retires rostered NPCs with `age >= 42 || health_ceiling <= 40` ([PromotionManager.cs:695–703](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L695-L703): `SetTeam(id, null)`, `SetFlag(id, "pipeline_retired", true, currentDay)`), then moves the 128 drafted players (plus any undrafted class-4 senior avatar's D4b-A walk-on contract) onto their Pro `team_id` (`301..332`), and finally trims each Pro team and role $r \in \{\text{None}, \text{Starter}, \text{Reliever}\}$ where `count(t, r) > RoleCap(r)` (`15 / 4 / 5`).
   - Score every non-avatar player on team $t$ in role $r$ by **`ProValue = PromotionScore.Combine(P_Pro, S)`** (`BatterPaShrink = 200`, `PitcherIpShrink = 40.0` against the completed Pro season's `leagueOps`/`leagueEra`, `RoleRatingSum(row)`, `age`, `headroom`). The avatar is never released (D4b-A).
   - Sort non-avatar players in role $r$ on team $t$ by `(ProValue ↓, PlayerId ↑)`, keep the top `RoleCap(r) - (avatarInRole ? 1 : 0)`, and release the remainder (`SetTeam(id, null)`, `SetFlag(id, "pipeline_free_agent", true, currentDay)`).
   - **Is `ProjectionBonus(age, headroom)` right for Pro releases?** Yes:
     - Incoming 21–24-year-old draftees have `PA_Pro == 0 / Outs_Pro == 0` ($\implies P_{\text{Pro}} = 100.0$) and positive `ProjectionBonus(age, headroom)` (`+2.5 to +5.0` scouting points), protecting new draft picks from being cut before playing a Pro game.
     - Past `PeakAge = 27`, `ProjectionBonus` is `-0.8333 * (age - 27)` clamped at `-10.0` ([PromotionManager.cs:215–245](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L215-L245)), which combines with `DevelopmentCurve` physical/skill decline to release fading mid-30s veterans (`127.47/yr`) rather than letting them clog Pro rosters until age 42.
2. **Free-agent signing order before Pro Sunday locks (`PipelineManager.FillProOpeningsBeforeLock`):**
   - Called at the top of `RosterManager.Lock` ([RosterManager.cs:306](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/RosterManager.cs#L306)) before `RosterManager` iterates `sim.PositionOrder(team)` / `sim.StarterRank(team)` / `sim.RelieverRank(team)`.
   - **Fast path:** Check the Pro `LeagueSimulator`'s per-team role counts (`sim.PositionOrder(team).Length == 15 && sim.StarterRank(team).Length == 4 && sim.RelieverRank(team).Length == 5` for all `team = 0..31`). When every team is at `15 / 4 / 5 = 24`, return `0` immediately (**`0 B` allocated, `0` DB queries, `0` sim re-initializations**).
   - **Slow path (when at least one Pro team is below `15 / 4 / 5`):**
     - Load signable free agents (`team_id IS NULL ∧ pipeline_free_agent active ∧ age < 42 ∧ health_ceiling > 40`, excluding `pipeline_retired` active) and score each by **`FreeAgentValue = PromotionScore.Combine(100.0, PromotionScore.Scouting(RoleRatingSum(row), row.Age, headroom))`** (using `Player_Ratings` + `Player_Potential`, with neutral $P = 100.0$ so no historical stat table scan is needed at a lock).
     - Partition available free agents by role $r$, sorted by `(FreeAgentValue ↓, PlayerId ↑)`.
     - While any Pro team $t$ has a role deficit `RoleCap(r) - count(t, r) > 0` in a role where `availableFA[r].Count > 0`:
       1. Pick the team $t^*$ with at least one fillable role deficit maximizing `(totalOpenSpots(t) ↓, teamId ↑)` (most short-handed team first, ties by `team_id` ascending `301..332`).
       2. On team $t^*$, pick the fillable role $r^*$ maximizing `((RoleCap(r) - count(t^*, r)) / RoleCap(r) ↓, (RoleCap(r) - count(t^*, r)) ↓, RolePriority(r))` (`None → Starter → Reliever`).
       3. Pop the top free agent $p^*$ of role $r^*$, `SetTeam(p^*, t^*)`, `SetFlag(p^*, "pipeline_free_agent", false, lockDay)`, and if `p^*` does not have `pipeline_pro_signed` active, `SetFlag(p^*, "pipeline_pro_signed", true, lockDay)` and queue `new ProContractSignedEvent(p^*, t^*, lockDay, wasDrafted: false)`.
     - Commit the single batch, re-initialize the Pro `LeagueSimulator` and `MicroGame` once (plus `Career?.ReactivateAvatar()` if the avatar's team changed), publish any queued `ProContractSignedEvent`s, and let `RosterManager.Lock` compute and write the `768` `Roster_Designations` rows from the freshly re-initialized Pro `LeagueSimulator`.
3. **Free-agent retirement rule (`age >= 30 || 2 unsigned seasons`) vs equilibrium & undrafted-senior role supply (Amendment 6):**
   - **Exact unsigned-seasons day arithmetic using `GlobalState.SeasonYearForDay` (Amendment 6):** In `TimeManager.AdvanceDay()` ([TimeManager.cs:74–81](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/TimeManager.cs#L74-L81)), `_state.CurrentDay` is advanced to day 1 of `newSeasonYear` *before* `SeasonRolledOverEvent(previousSeasonYear, newSeasonYear)` is queued. Therefore, any player released to `pipeline_free_agent` at rollover is stamped with `set_on_day = _state.CurrentDay` (day 1 of `newSeasonYear`), so `_state.SeasonYearForDay(setOnDay)` ([GlobalState.cs:38](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GlobalState.cs#L38)) equals the season year he entered free agency. At the rollover into `e.NewSeasonYear`, the number of full seasons he has spent unsigned is:
     $$\text{unsignedSeasons} = e.\text{NewSeasonYear} - \text{state.SeasonYearForDay}(\text{setOnDay})$$
     and he retires (`pipeline_free_agent = 0`, `pipeline_retired = 1`) iff:
     $$\text{age} \ge 30 \;\lor\; \text{health\_ceiling} \le 40 \;\lor\; \bigl(e.\text{NewSeasonYear} - \text{state.SeasonYearForDay}(\text{setOnDay})\bigr) \ge 2$$
     (Using `e.PreviousSeasonYear - state.SeasonYearForDay(setOnDay) >= 2` would off-by-one to 3 unsigned seasons.)
   - **Undrafted-senior role supply:** Every rollover, `~60.0` undrafted `class_year == 4` College seniors (`37.13 Pos, 9.47 SP, 13.40 RP`) enter `pipeline_free_agent` at age `24 < 30`, alongside `~127.47` Pro releases (`80.73 Pos, 21.53 SP, 25.20 RP`). Because all undrafted seniors are age `24`, they survive the `age >= 30` check at entry and stay signable for two full Pro seasons (`ages 24–25`), keeping the day-91 free-agent pool at a stationary **`~330.9` signable players (`207.8 Pos [62.8%], 55.7 SP [16.8%], 67.4 RP [20.4%]`)**.

---

#### Q7. The gradient gate — metric, burn-in, window, stationarity, seeds, margins, and neutral-$P$ bias

1. **Metric (matches 9d §7 at [Tools/MonteCarloHarness/Program.cs:6499–6516](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L6499-L6516)):**
   - After each rollover $k \in \{0..19\}$ (20 rollovers total), measure the tier-mean **raw role-summed current ratings** across all rostered players in tier $t \in \{\text{HS} = 0, \text{College} = 1, \text{Pro} = 2\}$, separately for:
     - **Batters (`Role == PitcherRole.None`):** $\mu_{\text{Bat}}(t, k) = \text{mean}_{p \in \text{Bat}(t, k)}(\text{BatPower}_p + \text{BatContact}_p + \text{BatDiscipline}_p)$
     - **Pitchers (`Role != PitcherRole.None`):** $\mu_{\text{Pit}}(t, k) = \text{mean}_{p \in \text{Pit}(t, k)}(\text{PitStuff}_p + \text{PitControl}_p + \text{PitStamina}_p)$
     (The gradient metric uses the **raw** three-tool sum `PitStuff + PitControl + PitStamina`; the `+20` Reliever stamina compensation is used only inside `PipelineRules.RoleRatingSum` when computing `PromotionScore.Scouting`.)
2. **Burn-in and measurement window over 20 seasons:**
   - **Total seasons:** `TotalSeasons = 20` (rollovers `k = 0..19`, completing seasons `2026..2045`).
   - **Burn-in:** `BurnInSeasons = 5` (`k = 0..4`, seasons `2026..2030`). As proved in Q3, 100% of world-gen HS players and 100% of world-gen College players have departed by the end of Season 4 (`k = 3`), and Season 5 (`k = 4`) settles the first full steady-state draft class into Pro.
   - **Measurement window:** `MeasurementWindowSeasons = 15` (`k = 5..19`, seasons `2031..2045`), with time-averaged means $\bar{\mu}_{\text{Bat}}(t) = \frac{1}{15}\sum_{k=5}^{19}\mu_{\text{Bat}}(t, k)$ and $\bar{\mu}_{\text{Pit}}(t) = \frac{1}{15}\sum_{k=5}^{19}\mu_{\text{Pit}}(t, k)$.
3. **Stationarity bound:**
   - Split the 15-season measurement window into its first half $W_1 = \{k = 5..11\}$ (`7` rollovers) and second half $W_2 = \{k = 12..19\}$ (`8` rollovers), matching [Program.cs:6545–6572](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L6545-L6572).
   - Gate: for every tier $t \in \{0, 1, 2\}$,
     $$\bigl|\bar{\mu}_{\text{Bat}}(t, W_2) - \bar{\mu}_{\text{Bat}}(t, W_1)\bigr| < \mathbf{10.0} \quad\text{and}\quad \bigl|\bar{\mu}_{\text{Pit}}(t, W_2) - \bar{\mu}_{\text{Pit}}(t, W_1)\bigr| < \mathbf{10.0}$$
     (`PipelineProfile.GradientStationarityMaxDrift = 10.0`). Observed `|W2 - W1|` drift across all three seeds is **`1.66–2.09` for batters** and **`2.23–2.49` for pitchers** (`< 1/4` of the `10.0` bound).
4. **Seeds and measured margins under offseason-only and noisy-$P$ modes:**
   - **Canonical seeds:** `WorldSeed = LeagueGenerator.DefaultLeagueSeed + 900 = 20261603` (matching 9d §7 at [Program.cs:6460](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L6460)), `DevelopmentSeed = 0x9D7000UL` ([Program.cs:6470](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L6470)), `PipelineSeed = PipelineProfile.DefaultPipelineSeed = 0x136D_5EED_0000_0001UL`.

| World Seed | Mode | $\bar{\mu}_{\text{Bat}}(\text{HS})$ | $\bar{\mu}_{\text{Bat}}(\text{Col})$ | $\bar{\mu}_{\text{Bat}}(\text{Pro})$ | Bat Gaps (`Col-HS` / `Pro-Col`) | Bat Max Drift `|W2-W1|` | $\bar{\mu}_{\text{Pit}}(\text{HS})$ | $\bar{\mu}_{\text{Pit}}(\text{Col})$ | $\bar{\mu}_{\text{Pit}}(\text{Pro})$ | Pit Gaps (`Col-HS` / `Pro-Col`) | Pit Max Drift `|W2-W1|` |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **`20261603`** *(default)* | Offseason (`P=100`) | **`135.12`** | **`141.94`** | **`160.21`** | **`+6.82` / `+18.27`** | `1.75` | **`124.64`** | **`130.58`** | **`148.34`** | **`+5.94` / `+17.76`** | `2.23` |
| **`20260703`** | Offseason (`P=100`) | **`135.27`** | **`142.13`** | **`160.44`** | **`+6.86` / `+18.31`** | `1.66` | **`127.09`** | **`130.87`** | **`148.25`** | **`+3.78` / `+17.38`** | `2.49` |
| **`20260704`** | Offseason (`P=100`) | **`135.04`** | **`142.12`** | **`159.99`** | **`+7.08` / `+17.87`** | `2.09` | **`124.77`** | **`130.86`** | **`148.21`** | **`+6.09` / `+17.35`** | `2.29` |
| **`20261603`** | Noisy $P$ ($\sigma_P=10$) | **`135.12`** | **`142.26`** | **`159.32`** | **`+7.14` / `+17.06`** | `1.61` | **`124.64`** | **`131.08`** | **`147.37`** | **`+6.44` / `+16.29`** | `2.41` |

   - **Recommended gate margins in `PipelineHarness` default (`PipelineProfile`):**
     - Strict monotonicity: $\bar{\mu}_{\text{Bat}}(\text{HS}) < \bar{\mu}_{\text{Bat}}(\text{College}) < \bar{\mu}_{\text{Bat}}(\text{Pro})$ and $\bar{\mu}_{\text{Pit}}(\text{HS}) < \bar{\mu}_{\text{Pit}}(\text{College}) < \bar{\mu}_{\text{Pit}}(\text{Pro})$.
     - Minimum separation floors: `MinBatterHsToCollegeMargin = 3.0` (observed `+6.82 to +7.08`), `MinBatterCollegeToProMargin = 10.0` (observed `+17.87 to +18.31`), `MinPitcherHsToCollegeMargin = 2.0` (observed `+3.78 to +6.09`), `MinPitcherCollegeToProMargin = 10.0` (observed `+17.35 to +17.76`).
5. **Does neutral $P$ (`P = 100.0`, no games) bias the gradient, and does `--full` need its own check?**
   - In offseason-only mode (`Pa == 0 / OutsRecorded == 0`), $P \equiv 100.0$, so $\text{Combine}(100.0, S) = 50.0 + 0.5 S$ orders candidates purely by $S$. When games are played, expected shrunk performance $\mathbb{E}[P \mid T]$ increases monotonically with true talent $T$, while single-season sampling variance ($\sigma_P \approx 10$ points after shrinkage) slightly attenuates top-tail truncation selection. As shown in `Ablation 4` (`20261603` Offseason vs Noisy $P$), noisy $P$ shifts the 15-season steady-state tier means by **at most `0.32–0.97` role-sum points (`~5%` of the inter-tier gap)** and leaves monotonicity and stationarity untouched.
   - Because `PipelineHarness --full` runs `3.27` seasons (`1,193` days, Amendment 7) to stay well within its runtime budget, it finishes inside the 5-season burn-in window and should assert **single-snapshot strict monotonicity** ($\mu_{\text{Bat}}(\text{HS}) < \mu_{\text{Bat}}(\text{College}) < \mu_{\text{Bat}}(\text{Pro})$ and $\mu_{\text{Pit}}(\text{HS}) < \mu_{\text{Pit}}(\text{College}) < \mu_{\text{Pit}}(\text{Pro})$) after its third rollover, proving real shrunk game stats $P$ preserve the talent gradient.

---

#### Q8. Stream contract (`pipeline_seed`, derived streams, and reload stability)

1. **Single persisted `pipeline_seed`:**
   - Keep the existing boot draw `new RngState(rng.NextUInt64() | 1UL)` at [GameManager.cs:588](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L588) (`PromotionManager`'s exact slot, between `DevelopmentManager` at `:575` and `LifeSim` at `:601`), so every upstream and downstream boot RNG draw in `GameManager` stays bit-identical.
   - Following the `SeasonScheduler.ScheduleSeedKey` pattern ([GameManager.cs:487–491](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L487-L491), [SeasonScheduler.cs:47–60](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/SeasonScheduler.cs#L47-L60)), store `PipelineSeedKey = "pipeline_seed"` once in `Game_State` via `GameStateQueries.TryGetInt64` / `SetInt64` (`unchecked((long)pipelineSeed)`), falling back to `DefaultPipelineSeed = 0x136D_5EED_0000_0001UL` in harness fixtures that do not seed the key.
2. **Derived streams per `(seasonYear, purpose)` via `PostseasonStreams.MixCombine` ([PostseasonStreams.cs:35](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PostseasonStreams.cs#L35)):**

| Purpose / Stream | Call Site & Day | Domain Tag Constant (Hex / ASCII) | Seed / Key Formula |
| :--- | :--- | :--- | :--- |
| **Inaugural Draft Order Key** | Day 195 (when prior-season Pro `Team_Seasons` is empty) | `InauguralDraftOrderTag = 0x4452_4146_544F_5244UL` (`"DRAFTORD"`) | `MixCombine(MixCombine(pipelineSeed, InauguralDraftOrderTag), MixCombine(unchecked((ulong)seasonYear), unchecked((ulong)teamId)))` |
| **Outside-Recruit Ratings, Potentials & Arsenals** | Day 318 (`OnSigningDay`) | `OutsideRecruitsTag = 0x5049_5045_4F55_5452UL` (`"PIPEOUTR"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, OutsideRecruitsTag), unchecked((ulong)seasonYear)) \| 1UL)` |
| **Outside-Recruit Fielding Rolls** | Day 318 (`OnSigningDay`) | `OutsideRecruitsFieldingTag = 0x5049_5045_4F52_464CUL` (`"PIPEORFL"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, OutsideRecruitsFieldingTag), unchecked((ulong)seasonYear)) \| 1UL)` |
| **HS Intake Ratings, Potentials & Arsenals** | Day 1 rollover (`OnSeasonRolledOver`, keyed by `completedSeasonYear`) | `HsIntakeTag = 0x5049_5045_4853_494EUL` (`"PIPEHSIN"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, HsIntakeTag), unchecked((ulong)completedSeasonYear)) \| 1UL)` |
| **HS Intake Fielding Rolls** | Day 1 rollover (`OnSeasonRolledOver`, keyed by `completedSeasonYear`) | `HsIntakeFieldingTag = 0x5049_5045_4853_464CUL` (`"PIPEHSFL"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, HsIntakeFieldingTag), unchecked((ulong)completedSeasonYear)) \| 1UL)` |

3. **Zero mutable RNG state & reload stability at any day:**
   - No other RNG draw is needed anywhere in 13-6 (`EnsureCollegeEnrollment`, day-176 open, day-183 declaration, day-195 draft board & pick selection, day-274 offers, day-318 recruiting assignment, rollover moves/releases/FA retirements, and Sunday-lock free agency are all pure deterministic functions of SQLite state plus the 5 `(pipelineSeed, seasonYear, tag)` derivations above).
   - Because `PipelineManager` holds **no mutable `RngState` field across days**, reloading a save at any day of the season produces bit-identical outside recruits, draft picks, intake players, and roster assignments at the next milestone or rollover (`pipeline-stream` gate).

---

#### Q9. `RecordArrestPenalty` on the pipeline scale, with concrete fixtures

1. **Re-sizing `RecordArrestPenalty` from `3.0` to `1.0` Combine point (Amendment 6):**
   - In 9c ([PromotionManager.cs:76, 124](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L76)), `PromotionProfile.RecordArrestPenalty = 3.0` was sized against `SwapMargin = 5.0` ($3.0 = 0.6 \times \text{SwapMargin}$).
   - In 13-6:
     - On the **recruiting offer ladder**, adjacent College programs are spaced by `OfferRankSlope = 1.0` Combine point ($F(r) - F(r+1) = 1.0$). A `3.0`-point penalty would strip **3 merit offers** (or shift the avatar's top offer down by 3 national-rank places) at once.
     - On the **College draft board**, `~450` draft-eligible players span `~25–30` Combine points (`~15–20` players per Combine point), so a `3.0`-point penalty drops the avatar by `~45–60` slots (nearly 2 full rounds).
     - Setting **`PipelineProfile.RecordArrestPenalty = 1.0`** Combine point (equivalent to `-2.0` scouting points or `-3.0` raw role-rating sum points) shifts the avatar by **1 program rank step (`1` offer at any integer floor boundary)** and **`~15–20` draft slots (`1` projected round near a round boundary)**.
2. **Concrete fixtures for `pipeline-record`:**
   - **Fixture A (Recruiting offer count flip `1 ↔ 0` and `2 ↔ 1`):**
     - Setup: HS senior batter avatar (`age = 19, headroom = 0, Pa = 0 → P = 100.0`), all 24 College programs have `OpenSpots[Batter] > 0`, program floors $F(r) = 111.5 - (r - 1)$ ($F(24) = 88.5, F(23) = 89.5, F(22) = 90.5$).
     - Sub-case A1 (`1 merit offer ↔ 0 merit offers / walk-on`): Avatar ratings `39 / 39 / 38` ($\text{roleSum} = 116 \implies S = 77.3333 \implies V_{\text{clean}} = \mathbf{88.6667}$).
       - `record_arrest` inactive: $V = 88.6667 \in [88.5, 89.5) \implies \mathbf{1\text{ merit offer}}$ (`OfferCount = 1, IsWalkOn = false`, team `224`).
       - `record_arrest` active: $V = 88.6667 - 1.0 = \mathbf{87.6667} < 88.5 \implies \mathbf{0\text{ merit offers}}$ (`OfferCount = 1, IsWalkOn = true` walk-on fallback).
       - `record_arrest` cleared: flips back to `1` merit offer (`IsWalkOn = false`).
     - Sub-case A2 (`2 merit offers ↔ 1 merit offer`): Avatar ratings `40 / 40 / 41` ($\text{roleSum} = 121 \implies S = 80.6667 \implies V_{\text{clean}} = \mathbf{90.3333} \in [F(23) = 89.5, F(22) = 90.5) \implies \mathbf{2\text{ merit offers}}$ from teams `223, 224`). With `record_arrest` active: $V = \mathbf{89.3333} \in [88.5, 89.5) \implies \mathbf{1\text{ merit offer}}$ (team `224` only).
   - **Fixture B (Projected draft round flip `Round 2 ↔ Round 3` across the `slot <= 64` cutline):**
     - Setup: Spread-0 College world (`ratingSpread = 0`, all NPCs at `50/50/50`, `headroom = 0`, `Pa = 0 → P = 100.0`, $V = 100.00$) where `63` NPC Juniors (`class_year = 3, age = 21, headroom = 0`) are set to `52 / 52 / 52` ($\text{roleSum} = 156 \implies V = 102.00$, slots `1..63`), `1` NPC Junior is set to `50 / 50 / 51` ($\text{roleSum} = 151 \implies V = 100.3333$), `31` NPC Juniors are at `50 / 50 / 50` ($V = 100.00$, slots `65..95`), and the remaining draft-eligible NPCs are at `49 / 49 / 49` ($\text{roleSum} = 147 \implies V = 99.00$).
     - Avatar: College Sophomore batter (`class_year = 2, age = 21, headroom = 0, Pa = 0`) with ratings `51 / 51 / 50` ($\text{roleSum} = 152 \implies S = 101.3333 \implies V_{\text{clean}} = \mathbf{100.6667}$).
       - `record_arrest` inactive: $V = 100.6667 > 100.3333 \implies \text{projectedSlot} = \mathbf{64}$ (**`ProjectedRound = 2`**).
       - `record_arrest` active: $V = 100.6667 - 1.0 = \mathbf{99.6667} \in (99.00, 100.00) \implies \text{projectedSlot} = \mathbf{96}$ (**`ProjectedRound = 3`**).
       - `record_arrest` cleared: restores `projectedSlot = 64` (**`ProjectedRound = 2`**).
   - **Fixture C (`banned_for_life` invariance — D6-A):**
     - Activating `banned_for_life` on the avatar in both Fixture A and Fixture B changes `RecruitingValue`, `DraftValue`, `OfferCount`, `IsWalkOn`, `projectedSlot`, and `ProjectedRound` by **`0.0` / `0`**.

---

#### Q10. Runtime forecasts for MC default (`≤ +10 s`), `PipelineHarness` default (`≤ 60 s`), and `--full` (`≤ 4 min`)

1. **MC default net wall-clock delta forecast (`+1.2 s to +2.8 s ≤ +10.0 s`):**
   - In the `9277af5` baseline, the 7 red promotion suites construct **11 scratch SQLite worlds** (`GenerateIfEmpty` at `~0.35 s` each = `~3.85 s`) and, in `promotion-avatar` ([Program.cs:5271–5275](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L5271-L5275)), simulate **1 full 3-tier season (`365` days = `~1.90 s`)** before throwing `InvalidOperationException` at `PromotionManager.RunOffseason`, already spending **`~5.75 s`** inside the `9277af5` baseline:

| 9c Suite (`9277af5` Baseline Cost) | 13-6 Replacement in MC Default | Forecast Cost | Net Delta vs `9277af5` |
| :--- | :--- | :---: | :---: |
| `promotion-sweep` (`~0.54 s`, 1 world + throws) | `pipeline-score`: pure in-memory math (0 DB worlds) testing `PromotionScore`, `RoleRatingSum` (+20 RP), `SelectAvatarOffers` (0/1/5 fixture), `ShouldNpcDeclare`, `OrderProDraftTeams`, `SelectDraftPick`, `SigningBonusForPick`, `SelectProReleases`, FA retirement test | `0.02 s` | **`-0.52 s`** |
| `promotion-stream` (`~1.05 s`, 3 worlds + throws) | `pipeline-stream`: 1 no-op pair + 1 reload twin testing `pipeline_seed` persistence, the 5 `MixCombine` tags, mid-season reload stability, and `15/4/5` rollover conservation | `0.95 s` | **`-0.10 s`** |
| `promotion-avatar` (`~2.25 s`, 1 world + 365 played days + throws) | `pipeline-avatar`: 1 shared scratch world testing 4-year HS grade gate, senior offers (274) + Signing Day (318) + rollover to College (`class_year = 1`), walk-on + displacement (D4a-A), sophomore declaration (176–183) + Draft (195) + bonus + `FundsImpulseEvent` + `ProContractSignedEvent(true)` + rollover to Pro, undrafted junior return, undrafted senior guaranteed contract (`wasDrafted: false`, D4b-A), release protection, and succession precedence | `1.10 s` | **`-1.15 s`** |
| `promotion-record` (`~0.35 s`, 1 world + throws) | `pipeline-record`: reuses scratch world testing Fixture A (`1 ↔ 0` and `2 ↔ 1` offers), Fixture B (`Round 2 ↔ Round 3` projected slot), and Fixture C (`banned_for_life` 0 dock) | `0.40 s` | **`+0.05 s`** |
| *(new)* `pipeline-lock` (`0.00 s`) | `pipeline-lock`: 1 scratch world testing Sunday lock fast path (`0 B`, `0` re-inits at `24`), shortfall free-agency fill (`< 24 → 24`) + single re-init + `ProContractSignedEvent(false)`, and unflagged-player exclusion | `0.55 s` | **`+0.55 s`** |
| `development-arc` (`~0.65 s`, 2 worlds + throws at rollover 1) | `development-arc`: 2 scratch worlds × 16 gameless offseasons (`DevelopmentManager` + `PipelineManager` rollover + calendar days `176/183/195/274/318`, no `LeagueSimulator` games) | `2.15 s` | **`+1.50 s`** |
| `development-equilibrium` (`~0.56 s`, 2 worlds + throws at rollover 1) | **Moved to `Tools/PipelineHarness`** (D7-A) | `0.00 s` | **`-0.56 s`** |
| `practice-seam` (passes 10/11, throws at `:6413` in §10.9) | Rewrite `practice-seam` §10.9 (`:6344–6428`) onto `PipelineManager` recruiting (1 College opening goes to the high-headroom senior probe over its at-ceiling twin) | `+0.15 s` | **`+0.15 s`** |
| `season-pipeline` (already passes) | Add the **1-season (`2026`, 365-day) Pro-only D1-A twin** + day-366 positive-control rollover check (1 Pro `LeagueSimulator`, no HS/College sims) | `+1.85 s` | **`+1.85 s`** |
| **Total MC Default Net Delta** | **All suites green** | — | **`+1.77 s`** *(range `+1.2 to +2.8 s ≤ +10.0 s`)* |

2. **`Tools/PipelineHarness` default (20 seasons, offseason-only — forecast `~4.0 to 6.0 s ≤ 60 s`):**
   - In `PipelineHarness` default mode, no `LeagueSimulator`s or `PlayoffManager` are attached, and on day 91 `PipelineManager.FillProOpeningsBeforeLock` runs directly without calling `SeasonScheduler.EnsureSeasons` (avoiding generating `38,240` unused `Schedule_Games` rows across 20 gameless seasons).
   - Each gameless season executes only day-91 FA fill (`~1 ms`), day-176/183 declaration (`~8 ms`), day-195 Draft (`~12 ms`), day-274/318 Recruiting + ~157 outside recruits (`~35 ms`), and day-1 rollover (`AgeAllPlayers` + `DevelopmentManager` + `PipelineManager` rollover + ~56 HS intake players, `~45 ms`) = **`~100–130 ms` per season (`~2.0–2.6 s` per 20-season world)**. Running the primary 20-season world (`WorldSeed = 20261603`) plus its 20-season determinism replay twin takes **`~4.7–5.9 s` total (`< 10%` of the `60 s` budget)**.
3. **`Tools/PipelineHarness --full` (3 full seasons + Season 4 opening week = `1,193` days — forecast `~10 to 25 s ≤ 4 min`, Amendment 7):**
   - **Exact timeline for an avatar to traverse HS → College → Draft → Pro attended game (Amendment 7):**
     - Seed the avatar as an **HS Senior (`age = 19`) in Season 1 (`2026`)**:
       - **Season 1 (`2026`, HS Senior, `age = 19`):** plays HS games (`Fridays, days 61..124`), gets offers on day 274, signs on day 318, and moves to his College `team_id` (`class_year = 1, age = 20`) at **Rollover 1 (`day 366`, start of `2027`)**.
       - **Season 2 (`2027`, College Freshman, `class_year = 1, age = 20`):** ineligible for the Draft (`class_year < 2`); plays College games and advances to `class_year = 2, age = 21` at **Rollover 2 (`day 731`, start of `2028`)**.
       - **Season 3 (`2028`, College Sophomore, `class_year = 2, age = 21`):** declares for the Draft during days `176..183` (`day 906..913`), is drafted on day 195 (`day 925`), and **moves to his Pro `team_id` (`301..332`) at Rollover 3 (`day 1096`, start of `2029`)**.
       - **Season 4 (`2029`, Pro Rookie, `age = 22`):** designated active at the Season-4 opening Sunday lock (`day 91` of Season 4 = `day 1186`) and plays his **first attended Pro game on Opening Tuesday (`day 93` of Season 4 = `day 1188`)**.
     - Advancing **3 full seasons + Season 4 through day 98 (`1,193` days = `3.27` seasons)** completes the entire composed journey (HS attended game → College attended game → Draft → Pro attended game + 3 composed rollovers) in **`~9–12 s` uncontended (`~21–27 s` throttled) $\ll 4\text{ min}$**.

---

### 10.2 Independent Seam and SQL Verification on `9277af5`

#### 10.2.1 Verification of §1.1 Seams (`17 / 17` Confirmed with Exact `file:line` Citations on `9277af5`)

| §1.1 Row | Exact `file:line` on `9277af5` | Verification Result |
| :--- | :--- | :--- |
| 1. `PromotionManager` | [PromotionManager.cs:288–869](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L288-L869), [GameManager.cs:586–590](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L586-L590) | **Confirmed.** `PromotionSummary` (`:288–322`), `PromotionManager` (`:352–869`). Throws `InvalidOperationException` at `:460–468` unless every tier has `LadderTeamsPerTier = 8` teams. Constructed at `GameManager.cs:586–590` with `new RngState(rng.NextUInt64() \| 1UL)` (`:588`). |
| 2. `PromotionScore` / `PromotionProfile` | [PromotionManager.cs:15–280](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L15-L280) | **Confirmed.** `PromotionProfile` (`:15–125`: weights `0.5/0.5`, shrinks `200 / 40.0` and HS `35 / 7.0`, `AgeBonusMax = 10.0`, `PeakAge = 27`, `YoungestProspectAge = 15`, `HeadroomForFullProjection = 30`, `HighSchoolAgeCap = 19`, `CollegeAgeCap = 23`, `IntakeMinAge = 15`, `IntakeAgeSpan = 2`, `RecordArrestPenalty = 3.0`), `PromotionScore` (`:137–280`: `Ops`, `LeagueOps`, `LeagueEra`, `BatterPerformance`, `PitcherPerformance`, `AgeBonus`, `ProjectionBonus`, `Headroom`, `Scouting`, `Combine`). |
| 3. `SchoolGrades` | [SchoolGrade.cs:13–52](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/SchoolGrade.cs#L13-L52) | **Confirmed.** `SchoolGrade` (`Freshman = 0 .. Senior = 3`, `:13–19`), `SchoolGrades.FreshmanAge = CareerManager.StartingAge` (`16`, `:35`), `SeniorAge = PromotionProfile.HighSchoolAgeCap` (`19`, `:38`), `ForAge(int age)` (`:50–51`). |
| 4. `College_Enrollment` | [SchemaDefinitions.sql:644–650](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/SchemaDefinitions.sql#L644-L650) | **Confirmed.** `player_id TEXT PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE`, `team_id INTEGER NOT NULL REFERENCES Teams(team_id)`, `entry_season INTEGER NOT NULL`, `class_year INTEGER NOT NULL CHECK (class_year >= 1)`, `declared_season INTEGER`, `STRICT`. No reader or writer in `Assets/` today. |
| 5. `Recruiting_Offers` | [SchemaDefinitions.sql:653–659](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/SchemaDefinitions.sql#L653-L659) | **Confirmed.** `player_id`, `team_id`, `season_year`, `status INTEGER NOT NULL DEFAULT 0 CHECK (status BETWEEN 0 AND 3)`, `PRIMARY KEY (player_id, team_id, season_year) STRICT`. No index on `season_year`. |
| 6. `Draft_Picks` | [SchemaDefinitions.sql:661–668](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/SchemaDefinitions.sql#L661-L668) | **Confirmed.** `season_year`, `overall_pick CHECK (overall_pick >= 1)`, `round CHECK (round >= 1)`, `team_id REFERENCES Teams(team_id)`, `player_id REFERENCES Players(player_id) ON DELETE CASCADE`, `PRIMARY KEY (season_year, overall_pick) STRICT`. No index on `player_id`. |
| 7. `Entity_Flags` | [SchemaDefinitions.sql:144–157](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/SchemaDefinitions.sql#L144-L157), [PlayerQueries.cs:102–109](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayerQueries.cs#L102-L109) | **Confirmed.** `UNIQUE (player_id, flag_name)` (`:150`) and partial index `idx_entity_flags_active_name ON Entity_Flags(flag_name) WHERE is_active = 1` (`:155–156`). Written via `PlayerQueries.SetFlag` (`SqlUpsertFlag` at `:102–104`). |
| 8. Free-agent pool | [BaseballQueries.cs:179–186, 1010–1020](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/BaseballQueries.cs#L179-L186), [CareerManager.cs:1257–1284](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L1257-L1284) | **Confirmed.** `SqlSelectFreeAgents` (`:179–186`) filters `p.team_id IS NULL AND p.age >= @minAge AND p.age < @maxAge AND p.health_ceiling > @minHealth` (`[19, 42)`, `> 40` in `FindStrongestFreeAgent` at `CareerManager.cs:1260–1263`). |
| 9. World-gen | [LeagueGenerator.cs:23–50](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L23-L50) | **Confirmed.** `TierAgeRolls` (`:23–28`: HS `15–18`, College `18–22`, Pro `21–36`), `TierRosterShapes` (`:45–50`: HS `11–14 / 3–4 / 2–4`, College & Pro `15 / 4 / 5 = 24`). |
| 10. `GeneratePlayer` | [LeagueGenerator.cs:313–315, 328–421](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L328-L421) | **Confirmed.** Writes `Players`, `Player_Ratings`, `Player_Potential`, optional `Player_Person`, and `Pitcher_Roles`, but **no `Player_Fielding` row** (`:313–315`). Applies `RelieverStaminaPenalty = 20` to `potential.PitStamina` (`:375–380`). |
| 11. Rollover order | [TimeManager.cs:77–81](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/TimeManager.cs#L77-L81), [GameManager.cs:459–590](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L459-L590), [CareerManager.cs:1380–1425](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L1380-L1425) | **Confirmed.** `TimeManager.AdvanceDay` publishes `DayAdvancedEvent` then `SeasonRolledOverEvent`. `SeasonRolledOverEvent` handlers fire in registration order: tier `LeagueSimulator`s (`:478`, flush) → `CareerManager` (`:548`, `AgeAllPlayers` at `:1385` + succession) → `DevelopmentManager` (`:577`, develops ratings only; never retires anyone) → `PromotionManager` (`:590`). |
| 12. Avatar handoff | [CareerManager.cs:795–819, 1134–1150](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L1134-L1150) | **Confirmed.** `ReactivateAvatar` (`:1134–1150`) throws if the avatar is unrostered (`:1137–1141`), calls `sim.ClearAttendedTeam()` across all tiers (`:1142–1148`), and calls `ActivateAvatar` (`:1149`), which republishes `AvatarChangedEvent` (`:818`). |
| 13. Draft day | [TierSeasonCalendar.cs:37–38, 103–104](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/TierSeasonCalendar.cs#L37-L38), [GameCalendar.cs:50–115](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameCalendar.cs#L50-L115) | **Confirmed.** `DraftDay` is the second Saturday of July = **day-of-season `195`** (July 14, `Weekday.Saturday`). |
| 14. Standings / bracket | [PlayoffQueries.cs:197–211, 235–248](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayoffQueries.cs#L197-L248), [Standings.cs:141–165](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/Standings.cs#L141-L165) | **Confirmed.** `LoadTeamSeasons` (`:197`) and `LoadSeries` (`:235`) read `Team_Seasons` and `Playoff_Series`; `Standings.Order` (`:141`) implements the recursive I14 tiebreak. |
| 15. Sunday lock | [RosterManager.cs:111–117, 306–400](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/RosterManager.cs#L306-L400) | **Confirmed.** Runs on Pro Sundays `91–273`. Note that `Lock` reads the Pro `LeagueSimulator`'s in-memory `PositionOrder(team)`, `StarterRank(team)`, and `RelieverRank(team)` (`:339, :349, :360`), which is why any pre-lock free-agent signing must re-initialize the Pro `LeagueSimulator` (and `MicroGame`) before designations run. |
| 16. Contacts | [contacts.json:3–17](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Narrative/Contacts/contacts.json#L3-L17) | **Confirmed.** `coach_reyes` (`:5`), `agent_diaz` (`:6`), `coach_hs` (`:12`), and `coach_college` (`:13`) are all present in `contacts.json`. |
| 17. Guard world & the 7 reds | [Tools/MonteCarloHarness/Program.cs:174–183, 504–627, 4841–6577](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L174-L183) | **Confirmed.** `RunSeasonPipeline` (`:504–627`) runs 2 Pro seasons without aging/development/promotion. The 7 red suites (`:174–183`) are `promotion-sweep` (`:4841`), `promotion-stream` (`:5101`), `promotion-avatar` (`:5210`), `promotion-record` (`:5600`), `development-arc` (`:5789`), `practice-seam` (§10.9 at `:6344–6428`), and `development-equilibrium` (`:6443`). |

#### 10.2.2 Verification of §1.2 Findings (`8 / 8` Confirmed on `9277af5`)

1. **Finding 1 (Live game throws at first rollover):** **Confirmed.** `PromotionManager.RunOffseason` throws `InvalidOperationException` at [PromotionManager.cs:460–468](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L460-L468), and `EventBus.DispatchOne` ([EventBus.cs:188–201](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/EventBus.cs#L188-L201)) invokes `handlers[i](gameEvent)` without catching exceptions.
2. **Finding 2 (Guard world can stay pipeline-free):** **Confirmed** ([Program.cs:504–627](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L504-L627); proved in Q1).
3. **Finding 3 (Conservation is by construction):** **Confirmed** (proved in Q2).
4. **Finding 4 (Free-agent pool needs engine-written `Entity_Flags`):** **Confirmed** ([BaseballQueries.cs:179–186](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/BaseballQueries.cs#L179-L186), [CareerManager.cs:1257–1284](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L1257-L1284); see also the rollover ordering note in Amendment 6).
5. **Finding 5 (Ages and class years transient):** **Confirmed** ([SchoolGrade.cs:35–51](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/SchoolGrade.cs#L35-L51), [LeagueGenerator.cs:23–28](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L23-L28); sized in Q3).
6. **Finding 6 (Held-back vs walk-on guarantee & unrostered avatar constraint):** **Confirmed** ([CareerManager.cs:1137–1141](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L1137-L1141)).
7. **Finding 7 (Boot-stream discipline):** **Confirmed** ([GameManager.cs:586–588](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L586-L588); derived stream contract in Q8).
8. **Finding 8 (`banned_for_life` is Pro-only in content):** **Confirmed.** In `Assets/Narrative/Events/Content/dirt_underworld_events.json`, `banned_for_life` is set only by `dirt_permanent_ban` (`field tier >= 2`), whereas `record_arrest` is set across all tiers (`narcotics_arrest`, `dirt_busted`, `dirt_robbery_fallout`, `dirt_the_indictment_a/b`, verified at [Tools/GrittyEventsHarness/Program.cs:3310–3323](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/GrittyEventsHarness/Program.cs#L3310-L3323)).

#### 10.2.3 Scratch v14 SQL & `EXPLAIN QUERY PLAN` Verification

Executed all §4 item 3 statements on an isolated scratch v14 SQLite database (`%TEMP%\dnd_136_scratch_v14.db`, built from `Assets/Data/Database/SchemaDefinitions.sql` with `PRAGMA foreign_keys = ON`, seeded with throwaway rows across `Conferences`, `Divisions`, `Teams`, `Team_Tiers`, `Players`, `Player_Ratings`, `Pitcher_Roles`, `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks`, and `Entity_Flags`, and deleted immediately after):

- `SELECT user_version FROM pragma_user_version` $\to$ **`14`**
- `SELECT COUNT(*) FROM pragma_foreign_key_check` $\to$ **`0`**
- `SELECT integrity_check FROM pragma_integrity_check` $\to$ **`ok`**
- **Verified `EXPLAIN QUERY PLAN` output on `SchemaDefinitions.sql`:**
  1. **Enrollment upsert & departure delete** (`INSERT INTO College_Enrollment … ON CONFLICT (player_id) DO UPDATE …` and `DELETE FROM College_Enrollment WHERE player_id = @playerId`):
     `SEARCH College_Enrollment USING INDEX sqlite_autoindex_College_Enrollment_1 (player_id=?)`
  2. **Enrollment load** (`SELECT player_id, team_id, entry_season, class_year, declared_season FROM College_Enrollment ORDER BY player_id`):
     `SCAN College_Enrollment USING INDEX sqlite_autoindex_College_Enrollment_1` (PK-ordered scan, $\le 576$ rows, no temp B-tree).
  3. **Avatar's offers** (`SELECT player_id, team_id, season_year, status FROM Recruiting_Offers WHERE player_id = @playerId AND season_year = @season ORDER BY team_id`):
     `SEARCH Recruiting_Offers USING INDEX sqlite_autoindex_Recruiting_Offers_1 (player_id=?)` (composite PK `(player_id, team_id, season_year)` prefix search on `player_id`, ordered by second PK column `team_id` with no temp B-tree).
  4. **Season's accepted offers** (`SELECT player_id, team_id, season_year, status FROM Recruiting_Offers WHERE season_year = @season AND status = 1 ORDER BY player_id`):
     `SCAN Recruiting_Offers USING INDEX sqlite_autoindex_Recruiting_Offers_1` (ordered by leading PK column `player_id` with no temp B-tree; `~188` rows/season).
  5. **Offer expiry** (`UPDATE Recruiting_Offers SET status = 3 WHERE season_year = @season AND status = 0`):
     `SCAN Recruiting_Offers`
  6. **Draft class read** (`SELECT season_year, overall_pick, round, team_id, player_id FROM Draft_Picks WHERE season_year = @season ORDER BY overall_pick`):
     `SEARCH Draft_Picks USING INDEX sqlite_autoindex_Draft_Picks_1 (season_year=?)` (composite PK `(season_year, overall_pick)` prefix search on `season_year`, ordered by `overall_pick` with no temp B-tree).
  7. **Free-agent pool** (`SELECT p.player_id, … FROM Entity_Flags AS f JOIN Players AS p ON p.player_id = f.player_id JOIN Player_Ratings AS r ON r.player_id = p.player_id LEFT JOIN Pitcher_Roles AS pr ON pr.player_id = p.player_id WHERE f.flag_name = @flag AND f.is_active = 1 AND p.team_id IS NULL ORDER BY p.player_id`):
     `|--SEARCH f USING INDEX idx_entity_flags_active_name (flag_name=?)`
     `|--SEARCH p USING INDEX sqlite_autoindex_Players_1 (player_id=?)`
     `|--SEARCH r USING INDEX sqlite_autoindex_Player_Ratings_1 (player_id=?)`
     `|--SEARCH pr USING INDEX sqlite_autoindex_Pitcher_Roles_1 (player_id=?) LEFT-JOIN`
     ``--USE TEMP B-TREE FOR ORDER BY`
     (Verified on throwaway rows that `p_fa_1` [active flag] is returned while `p_fa_inactive` [inactive flag] and `p_unflagged` [no flag] are excluded.)

---

### 10.3 Math View on Owner Decisions (D1–D8)

- **D1 (Pipeline vs Pro guard):** **Agree with D1-A.** Because the sanctioned 2-season Pro guard (`932ACE26…8B84`) covers `2026–2027`, any rollover roster turnover between `2026` and `2027` changes the `2027` half of the ledger. Keeping `PipelineManager` unwired in the 730-day guard world while verifying 8 invariance + positive-control channels on a `2026` pipeline-attached twin (Q1 / Amendment 7) proves in-season neutrality and rollover turnover without re-baselining the guard.
- **D2 (Pipeline calendar & persistence):** **Agree with D2-A**, subject to **Amendments 1–5** (`+20` Reliever stamina compensation in `RoleRatingSum`; age-16-only HS intake; `OutsideRecruitDiscountAge = 25` and persist-only-signed outside recruits; `classYear >= 3` alongside `age >= 22` in `ShouldNpcDeclare`; clearing `declared_season = NULL` on undrafted underclassmen on day 195).
- **D3 (Free-agent & career-end state via `Entity_Flags`):** **Agree with D3-A**, subject to **Amendment 6** (`unsignedSeasons = e.NewSeasonYear - state.SeasonYearForDay(setOnDay) >= 2`, and preventing signed outside recruits from sitting in `Players` with `team_id IS NULL` across `CareerManager.OnSeasonRolledOver`'s succession backfill read).
- **D4 (Avatar guarantees — HS → College & College → Pro):** **Agree with D4a-A and D4b-A**, subject to **Amendment 4** (a D4a-A walk-on offer comes from the lowest-ranked College program with `OpenSpots[avatarRole] > 0` so an open spot is consumed cleanly, falling back to rank-24 incumbent displacement only if all 24 programs have `0` openings in `avatarRole`).
- **D5 (Avatar decisions, phone beat writer, signing bonus):** **Agree with D5-A**, subject to **Amendment 5** (the Q5 piecewise-linear signing-bonus schedule `\$50,000 → \$2,080` and publishing `FundsImpulseEvent(avatarId, bonus)` after `AdjustFunds` commits on Draft Day).
- **D6 (Criminal record in the pipeline):** **Agree with D6-A**, subject to **Amendment 6** (`PipelineProfile.RecordArrestPenalty = 1.0` Combine point).
- **D7 (Harnesses & runtime):** **Agree with D7**, subject to **Amendment 7** (`PipelineHarness` default calling `PipelineManager.FillProOpeningsBeforeLock` on day 91 without generating unused `Schedule_Games` rows, and `PipelineHarness --full` running 3 full seasons + Season 4 through day 98 [`1,193` days] from an HS-senior avatar start).
- **D8 (Slicing & UI):** **Agree with D8-A.** Merging 13-6a into `p13/integration` and then `p13/integration → main` once all gates are green fixes the live rollover throw and turns MC default green immediately while isolating 13-6b's UI work.

---

### 10.4 Numbered Amendments for Reconciliation (Amendments 1–7)

#### Amendment 1 — Cross-Role `RoleRatingSum` Reliever Stamina Compensation (`+20`)
- **Where:** §3 D2-A, §4 item 1 (`PipelineRules.cs`), §5 (`pipeline-score`).
- **Why:** `LeagueGenerator.GeneratePlayer` ([LeagueGenerator.cs:119, 375–380](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L375-L380)) subtracts `RelieverStaminaPenalty = 20` from every Reliever's `PitStamina` ceiling, centering average Relievers at `130` (`Scouting = 86.67`, `-6.67` Combine points) versus `150` (`Scouting = 100.0`) for Batters and Starters and starving Reliever draft picks to `6.4%` (`8.13 / 128`) on the cross-role Draft board.
- **Exact rule to fold in:**
  ```csharp
  public static int RoleRatingSum(in RosterPlayerRow row) => row.Role switch
  {
      PitcherRole.None     => row.BatPower + row.BatContact + row.BatDiscipline,
      PitcherRole.Starter  => row.PitStuff + row.PitControl + row.PitStamina,
      PitcherRole.Reliever => row.PitStuff + row.PitControl
                              + Math.Min(100, row.PitStamina + LeagueGenerator.RelieverStaminaPenalty),
      _                    => row.BatPower + row.BatContact + row.BatDiscipline,
  };
  ```
  (The equilibrium gradient check in `PipelineHarness` continues to measure the **raw** three-tool sum `PitStuff + PitControl + PitStamina` for pitchers so the metric matches 9d §7.)

#### Amendment 2 — HS Intake Age (`16` Only), Graduation Timing Across `AgeAllPlayers()`, and Outside-Recruit Generation (`OutsideRecruitDiscountAge = 25`, Persist-on-Sign)
- **Where:** §3 D2-A (Signing Day & Rollover), §4 item 1 (`PipelineProfile` / `PipelineManager.cs`), §4 item 6 (`LeagueGenerator.cs`).
- **Why:**
  1. Generating HS intake at `IntakeMinAge = 16, IntakeAgeSpan = 1` (after `AgeAllPlayers()` at rollover) gives every HS NPC an exact 4-season `16 → 17 → 18 → 19` runway and puts HS into 4-cohort steady state in Season 2 (`2027`).
  2. Outside recruits enter College at age `19` (`20` after rollover) without having received the 4 HS `DevelopmentManager` passes (which reduce an age-16 freshman's initial `YouthWeight` from `0.9167` to an effective `0.1578`). Passing `OutsideRecruitDiscountAge = 25` (`YouthWeight(25) = 2/12 = 0.1667`) to `DevelopmentCurve.RawCurrent(potential, OutsideRecruitDiscountAge, ref discountRng)` matches 4-year HS senior maturity so outside recruits (`73%` of College intake) do not invert the `HS < College` talent gradient (`135.12 > 133.72` at `discountAge = 19` vs `135.12 < 141.94` at `discountAge = 25`).
  3. Rolling the `+15 Pos / +5 SP / +5 RP` outside-recruit surplus in memory and persisting to SQLite **only the outside recruits who win a College spot** prevents ~21 orphan `team_id IS NULL` rows/year from accumulating in `Players`.
- **Exact constants and rules to fold in:**
  - `IntakeMinAge = 16`, `IntakeAgeSpan = 1`
  - `OutsideRecruitAge = 19`, `OutsideRecruitMeanOffset = -2`, `OutsideRecruitDiscountAge = 25`
  - `OutsideRecruitSurplusBatters = 15`, `OutsideRecruitSurplusStarters = 5`, `OutsideRecruitSurplusRelievers = 5`
  - On fall days `274` and `318` (before `AgeAllPlayers()`), an HS player is a graduating senior iff `row.Age >= SchoolGrades.SeniorAge` (`age >= 19`). At the day-1 rollover (after `CareerManager.OnSeasonRolledOver` calls `_players.AgeAllPlayers()`), an HS player graduates iff `row.Age > SchoolGrades.SeniorAge` (`age >= 20`) or he holds an accepted `Recruiting_Offers(status = 1)` row for `completedSeasonYear`.

#### Amendment 3 — Boot Class-Year Map (`clamp(age - 17, 1, 4)`), Transient-Safe NPC Declaration Rule (`classYear >= 3 || age >= 22 || projectedSlot <= 64`), and Undrafted Underclassman Reset
- **Where:** §3 D2-A (Day 183, Day 195 & Boot ensure), §4 item 1 (`PipelineRules.ShouldNpcDeclare`).
- **Why:** In steady state (`age = 19 + class_year`), `age >= 22` and `class_year >= 3` are identical (`22 = Junior, 23 = Senior`). During the `2026–2029` world-gen transient (`TierAgeRolls[College] = 18..22` mapped via `class_year = Math.Clamp(age - 17, 1, 4)`), all `~225` boot `class_year = 4` (`ages 21–22`) seniors depart after Season 1, leaving Seasons 2–3 (`2027–2028`) with **zero** College players aged `>= 22` (`class_year = 3` Juniors are age `20`; `class_year = 4` Seniors are age `21`). Including `classYear >= 3` alongside `age >= 22` is identical in steady state and keeps the declaration pool at `>= 193 > 128` across every transient season.
- **Exact rules to fold in:**
  - Boot ensure (`EnsureCollegeEnrollment`): `classYear = Math.Clamp(row.Age - 17, 1, 4)`, `entrySeason = seasonYear - (classYear - 1)`, `declaredSeason = null`.
  - `PipelineRules.ShouldNpcDeclare(int classYear, int age, int projectedSlot)`:
    ```csharp
    public static bool ShouldNpcDeclare(int classYear, int age, int projectedSlot) =>
        classYear >= 2 && (classYear >= 3 || age >= 22 || (projectedSlot >= 1 && projectedSlot <= 64));
    ```
  - On day 195 (after the 128 picks are made), clear `declared_season = NULL` for every undrafted underclassman (`class_year < 4`) so they return to College at rollover and are counted as returning players on Day 274 (`Offers Day`).

#### Amendment 4 — Avatar Recruiting Offer Band (`OfferTopProgramFloor = 111.5`, `OfferRankSlope = 1.0`), Position-Coverage Tiebreak (`RecruitPositionNeedMargin = 4.0`), and Open-Spot Walk-On Preference
- **Where:** §3 D2-A (Days 274 & 318), §3 D4a-A, §4 item 1 (`PipelineProfile` / `PipelineRules.cs`).
- **Why:** Locks the concrete offer-floor schedule $F(r) = 111.5 - 1.0 \cdot (r - 1)$ for national ranks $r \in \{1..24\}$ (producing the exact `0 / 1 / 5` offer fixture in Q4.5), the position-coverage tiebreak (`RecruitPositionNeedMargin = 4.0`), and ensures a D4a-A walk-on offer consumes an existing open spot whenever any College program has `OpenSpots[avatarRole] > 0` instead of needlessly cutting a returning player on team `224`.
- **Exact constants and walk-on rule to fold in:**
  - `OfferTopProgramFloor = 111.5`, `OfferRankSlope = 1.0`, `MaxAvatarOffers = 5`, `RecruitPositionNeedMargin = 4.0`.
  - When `0` programs qualify on merit ($V_{\text{avatar}} < F(r)$ for every program with `OpenSpots[avatarRole] > 0`), `IsWalkOn = true` and the walk-on offer (`status = 0`) comes from the **lowest-ranked College program (highest $r \in \{1..24\}$, tiebreak highest `teamId`) with `OpenSpots[avatarRole] > 0`**. Only if all 24 programs have `OpenSpots[avatarRole] == 0` does the walk-on offer go to rank 24 (highest `teamId = 224`) and displace that team's lowest-valued returning non-avatar player of `avatarRole` (`pipeline_retired`) at rollover.

#### Amendment 5 — Draft Role-Need Tiebreak (`DraftRoleNeedMargin = 3.0`, `DraftRoleQuota = 3/1/1`), Signing-Bonus Schedule (`\$50,000 → \$2,080`), and Post-Commit `FundsImpulseEvent`
- **Where:** §3 D2-A (Day 195), §3 D5-A, §4 item 1 (`PipelineProfile` / `PipelineRules.cs` / `PipelineManager.cs`).
- **Why:** Prevents Pro teams from over-drafting one role when an immediate cap deficit or 4-round role quota (`3 Pos / 1 SP / 1 RP`) can be filled within `3.0` Combine points, defines the exact 128-pick integer signing-bonus schedule, and updates `LifeSimManager`'s cached funds via `FundsImpulseEvent` ([CoreEvents.cs:133–144](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/CoreEvents.cs#L133-L144), [PlayerQueries.cs:425–434](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayerQueries.cs#L425-L434)) on Draft Day.
- **Exact constants and rules to fold in:**
  - `DraftRoleNeedMargin = 3.0`, `DraftRoleQuotaBatters = 3`, `DraftRoleQuotaStarters = 1`, `DraftRoleQuotaRelievers = 1`.
  - `SigningBonusForPick(int overallPick)` as defined in Q5.5 (`Round1TopBonus = 50_000, Round1PickSlope = 900; Round2TopBonus = 20_000, Round2PickSlope = 250; Round3TopBonus = 11_500, Round3PickSlope = 175; Round4TopBonus = 5_800, Round4PickSlope = 120`).
  - When the avatar is drafted on day 195 (`bonus > 0`), after the transaction calling `_players.AdjustFunds(avatarId, bonus)` commits, publish `_bus?.Publish(new FundsImpulseEvent(avatarId, bonus))`.

#### Amendment 6 — `RecordArrestPenalty = 1.0`, Unsigned-FA Season Arithmetic via `GlobalState.SeasonYearForDay`, and Rollover Succession Guard for Signed Outside Recruits
- **Where:** §3 D3-A, §3 D6-A, §4 item 1 (`PipelineProfile`), §4 item 2 (`PipelineManager.cs`).
- **Why:**
  1. `RecordArrestPenalty = 1.0` Combine point matches `OfferRankSlope = 1.0` (1 offer step / 1 projected round at boundaries, Q9).
  2. Because `TimeManager.AdvanceDay()` sets `_state.CurrentDay` to day 1 of `e.NewSeasonYear` before publishing `SeasonRolledOverEvent` ([TimeManager.cs:74–81](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/TimeManager.cs#L74-L81)), a player released at rollover has `set_on_day` in `_state.SeasonYearForDay(setOnDay)` ([GlobalState.cs:38](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GlobalState.cs#L38)), so elapsed unsigned seasons at rollover into `e.NewSeasonYear` is `e.NewSeasonYear - _state.SeasonYearForDay(setOnDay)` (`>= 2` retires after 2 unsigned seasons).
  3. At rollover, `CareerManager.OnSeasonRolledOver` (subscriber #2, [CareerManager.cs:1380–1396](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L1380-L1396)) runs `RunSuccessionCheck() → Succeed() → FindStrongestFreeAgent()` *before* `PipelineManager.OnSeasonRolledOver` (subscriber #4), and D3-A keeps `FindStrongestFreeAgent` on the unflagged `team_id IS NULL` read (`LoadFreeAgents`). If outside recruits signed on Day 318 were inserted into `Players` with `team_id = NULL` on Day 318 (waiting to be moved to their College `team_id` at subscriber #4), `FindStrongestFreeAgent` could hijack a signed 20-year-old outside recruit into a Pro/College succession backfill slot before subscriber #4 runs!
- **Exact rules to fold in:**
  - `PipelineProfile.RecordArrestPenalty = 1.0`.
  - At rollover (`e.NewSeasonYear = e.PreviousSeasonYear + 1`), an active `pipeline_free_agent` retires iff `row.Age >= 30 || row.HealthCeiling <= 40 || (e.NewSeasonYear - _state.SeasonYearForDay(flagSetOnDay)) >= 2`.
  - To prevent `CareerManager.FindStrongestFreeAgent()` (which stays on the unflagged `LoadFreeAgents` read per D3-A) from ever seeing a signed outside recruit with `team_id IS NULL` at rollover before `PipelineManager.OnSeasonRolledOver` runs, either (a) on Day 318 insert signed outside recruits directly with `team_id` set to their committed College team (only if the College sim is already past its postseason, i.e. after day 174, which day 318 always is!) or (b) add `AND NOT EXISTS (SELECT 1 FROM Recruiting_Offers AS ro WHERE ro.player_id = p.player_id AND ro.status = 1)` (plus `AND NOT EXISTS (SELECT 1 FROM Entity_Flags AS ef WHERE ef.player_id = p.player_id AND ef.flag_name = 'pipeline_retired' AND ef.is_active = 1)`) to `SqlSelectFreeAgents` in `BaseballQueries.cs:179–186`, which keeps all existing `lineage-*` fixtures 100% unchanged while excluding retired players and committed recruits.

#### Amendment 7 — Allowed Paths (`PersonEffects.cs`, `ScoutingGrade.cs`), `season-pipeline` 8-Channel D1-A List, and `PipelineHarness` Default / `--full` (`1,193`-Day) Specifications
- **Where:** §2 Allowed paths, §5 (`season-pipeline` and `PipelineHarness`).
- **Why:**
  1. §4 item 4 specifies comment-only edits in `Assets/Simulation/Baseball/PersonEffects.cs` and `Assets/UI/Scouting/ScoutingGrade.cs`, so both files should be listed in §2 Allowed paths alongside `SchoolGrade.cs` and `DevelopmentManager.cs`.
  2. Locks the 8-channel D1-A verification list (Q1.2) and the exact 20-season gradient/stationarity parameters (`BurnInSeasons = 5`, `MeasurementWindowSeasons = 15`, `WorldSeed = 20261603`, `DevelopmentSeed = 0x9D7000UL`, `PipelineSeed = 0x136D_5EED_0000_0001UL`, `GradientStationarityMaxDrift = 10.0`, minimum margins `Bat: +3.0 / +10.0`, `Pit: +2.0 / +10.0`).
  3. Ensures `PipelineHarness` default mode runs `PipelineManager.FillProOpeningsBeforeLock` on day 91 without generating unused `Schedule_Games` rows (`~5 s` total runtime), and specifies that `PipelineHarness --full` seeds the avatar as an **HS Senior (`age = 19`) in Season 1 (`2026`)** and advances **3 full seasons + Season 4 through day 98 (`1,193` days = `3.27` seasons)** so the avatar completes HS Senior (`2026`) → College Freshman (`2027`) → College Sophomore & July Draft (`2028`) → Pro rollover (`day 1096`) → first Pro Sunday lock (`day 1186`) & first attended Pro game (`day 1188`).

---

### 10.5 Opus review and reconciliation — Claude Opus 5.5, 2026-11-24

**Verdict: Argon's §10 is ACCEPTED.** All 7 amendments are reconciled into §§1–8. For Amendment 6's either/or, option (b) is chosen (C1). I add five corrections, C1–C5. Nothing blocks the owner's D1–D8.

**Provenance.** The branch carries the void `7202d65`, its revert `3a1b590` (`git diff c9d56ad..3a1b590` is empty) and the clean `c5a74d3`. The diff touches only this brief and `progress.md`. Every claim below was re-checked at `9277af5`, not taken from §10.

#### 10.5.1 What I re-derived at `9277af5`

| §10 claim | Check | Result |
| :-- | :-- | :-- |
| Amendment 1: reliever penalty | `LeagueGenerator.cs:119` `public const int RelieverStaminaPenalty = 20`; `:379` applies it to the stamina **potential**, and current is derived from potential | Confirmed. The constant is public, so `PipelineRules` can reference it |
| Q3/Q4: age-16 intake, discount age 25 | `DevelopmentManager.cs`: `YouthWeight = clamp((27 − age)/12)` (`:134`); a pass closes `0.5·YW` of the gap (`:177–180`); generation discounts `ProspectDiscount 20 · YW · gapRoll` (`:222–224`). Passes run after `AgeAllPlayers`, at ages 17–20 | Confirmed: `11/12 · (7/12)(7.5/12)(8/12)(8.5/12) = 0.1578` vs `YW(25) = 0.1667`. It holds only if outside recruits are **not** developed at the rollover they join (see C1) |
| Q4.5 / Q9: fixtures | `Scouting = 100·(sum + ProjectionBonus)/150` (`PromotionManager.cs:274`); `ProjectionBonus(age, 0) = 0` before 27 (`:235–245`); `Combine` 0.5/0.5 | All six values reproduce: 86.00, 88.67, 90.33, 100.00, 100.67, 99.67, with the offer sets and slots 64/96 |
| Amendment 5: `FundsImpulseEvent` | `CoreEvents.cs:127–144` ("a funds delta already committed"); seven existing writers pair `AdjustFunds` with it | Confirmed, and **a real catch:** the draft brief would have left the Life sim's funds stale after Draft Day. No double credit |
| Amendment 6: unsigned seasons | `GlobalState.cs:38` `SeasonYearForDay = Start + (day − 1)/365` | Confirmed: released into Y, kept at the rollover into Y+1, retired at Y+2 |
| Q8: streams | `PostseasonStreams.MixCombine` is public static (`:35`); the five existing tags don't collide with `DRAFTORD`/`PIPEOUTR`/`PIPEORFL`/`PIPEHSIN`/`PIPEHSFL` (ASCII checked); `Player_Fielding` has `secondary_position`, `speed`, `arm` (`SchemaDefinitions.sql:555–563`) | Confirmed: the intake fielding stream is needed even though intake inherits the primary position |
| Amendment 7: paths | `PersonEffects.cs:55, :112` and `ScoutingGrade.cs:9` say "call-up" | Confirmed |
| Q2: career length | `768 / 128 = 6.0` | Confirmed |
| §10.2.3: SQL plans | Re-run on a fresh scratch v14 (`sqlite3` 3.53.3 over `9277af5`'s schema, `user_version` 14, FK on) | All six plans reproduce. `Player_Ratings` shows as a covering-index probe when only the key is selected. FK check empty; `integrity_check` ok |

#### 10.5.2 Amendments: dispositions

1. **Amendment 1 (`RoleRatingSum` +20 for relievers): accepted.** It applies to value only; the gradient metric stays raw. The clamp matters only for a hand-built avatar reliever above 80 stamina.
2. **Amendment 2 (intake 16, the graduation test across aging, outside-recruit constants, write only on signing): accepted.** The rollover clause "or holds an accepted offer" is redundant, since offers only go to `age ≥ 19`, which is `> 19` after aging. It's harmless and kept as a guard.
3. **Amendment 3 (boot map, transient-safe declaration rule, reset for undrafted underclassmen): accepted.** It also settles the spec-vs-transient question without changing the spec's steady-state rule.
4. **Amendment 4 (offer floor, the position tiebreak, the walk-on prefers an open spot): accepted.** The floor is a data value: an avatar at V = 100 gets mid-pack offers (ranks 13–17), which is the intended feel for an average College prospect. `pipeline-score` pins it.
5. **Amendment 5 (draft tiebreak and 3/1/1 quota, bonus schedule, post-commit funds event): accepted.** The quota sums to five for four picks, so a team takes 3+1, 3+1 or 2+1+1. Argon measured the mix at 63/17/20%, against the 62.5/16.7/20.8% roster.
6. **Amendment 6 (penalty 1.0, unsigned-season math, the succession guard): accepted, choosing (b) (C1).**
7. **Amendment 7 (paths, eight channels, PipelineHarness constants, the 1,193-day `--full`): accepted, plus C4.**

#### 10.5.3 Corrections

- **C1. Amendment 6's guard is option (b): narrow succession's read. Option (a), writing outside recruits onto College rosters on day 318, is rejected.**
  - (a) would put about eight extra players on every College roster for days 318–365.
  - Under (a), recruits would be College-rostered when `DevelopmentManager` runs at the rollover, so they'd get a development pass (age 20, `YW 7/12`). Discount age 25 already stands in for an HS graduate's four passes. They'd enter more mature than HS graduates (remaining fraction 0.118 vs 0.158), which breaks Amendment 2's calibration.
  - And under (a), outside recruits would join College before HS signees do.
  - (b) adds two `NOT EXISTS` clauses to `SqlSelectFreeAgents`. On a scratch v14 it removes exactly the committed recruit and the active retiree. It keeps the flagged free agent, an unflagged adult child (the heir pool, so `lineage-*` doesn't move), a retiree whose flag is inactive and a declined-offer holder. Plan: `idx_players_team` search, then the `Recruiting_Offers` PK and `Entity_Flags` `UNIQUE (player_id, flag_name)` probes, then a temp B-tree.
  - `BaseballQueries.cs` (that constant only) joins §2's allowed paths.
- **C2. The rollover pass order is written out** (D2-A, steps 1–7).
  - The free-agent retirement test runs on the pool carried in, **before** this rollover's releases and undrafted seniors join, so a fresh release always gets one signable season. That's what Argon's pool (~331 at day 91) assumes. Otherwise most veterans released in their 30s would retire before ever reaching a lock.
  - An unplanned College departure isn't backfilled; next Signing Day's `OpenSpots` refills it. College has no lock and fields from 9 + a pitcher, and Argon measured 0 such departures a year.
- **C3. `PipelineManager` collaborators are optional.** The D1-A twin has only a Pro sim and no `CareerManager` or avatar. The offseason harness has no sims. Channel 7 in that twin therefore has no aging and no HS intake, and it asserts only the draftee and release moves.
- **C4. `--full` must make the journey happen, not assume it.** On autopilot, a sophomore declares only if he projects in the top 64. So the harness builds its age-19 avatar to project there at day 176 of season 3 and asserts declared, drafted and played. Otherwise an average avatar stays in College past day 1193 and the check passes vacuously.
- **C5. The DTOs need age and health.** `RosterPlayerRow` (`BaseballDtos.cs:172`) has ratings and role only. Value, offers, releases and free-agent retirement read age, `health_ceiling`, potential (headroom) and the tier's stat line, so the build gets one `PipelineCandidateRow`. I also escaped a `|` in §10.2.1 row 1 that was splitting that table cell.

#### 10.5.4 Observations (no change)

- **Most College intake is outside recruits (~73%).** Twelve HS teams can't supply 24 programs, so for NPCs, HS → College is the minority path. The avatar's journey is unaffected.
- **Pro free agency is a holding pen.** About 331 free agents, ~0–1 signing a season, ~189 retirements, all by construction: a team is below 24 only when retirements in a role exceed its picks in that role. It's working as designed, and it's why the undrafted-senior contract (D4b-A) has to be a guarantee rather than a market.
- **Argon agreed with every recommended D-option (§10.3), and so do I.** The amendments refine D2–D7 within their A options; none changes what the owner is choosing.

#### 10.5.5 Gates for this review

| Check | Result |
| :-- | :-- |
| Scratch v14 (fresh, `9277af5` schema) | The amended free-agent read returns the expected rows; six plans reproduce; FK check empty; `integrity_check` ok |
| Arithmetic | Fixtures A1/A2/B/Q4.5; the 0.1578 discount product; the bonus endpoints; `768/128`; the unsigned-season boundary: all reproduced |
| Docs only | Only this brief and `progress.md` changed; no DDL |
