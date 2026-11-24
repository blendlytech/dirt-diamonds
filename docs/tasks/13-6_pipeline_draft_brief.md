# Task Brief: 13-6 — PipelineManager: graduation, recruiting, class years, the Draft, Pro turnover

**Status:** **DRAFT 2026-11-23** (Claude Opus 5.5, `p13/13-6-brief-opus`). Awaiting Gemini 4 Argon's Math check (§9 → a new §10) and the owner's D1–D8.
**Recommended model:** **13-6a** (engine, persistence, harnesses): Claude Opus 5.5 (TEAM.md §2, 13-6 row). Gemini 4 Argon: the Math check of this brief (population flows, values, bands, the gradient gate, streams) and the independent review of the build. **13-6b** (phone decisions and the Recruiting/Draft screens, if D8-A): Claude Sonnet 5.5, Gemini 4 Neon reviews.
**Reviewer:** Gemini 4 Argon independently reviews Opus's sim/persistence diff (TEAM.md §1a). Opus reviews Argon's Math record. Opus is the merge gate after review.
**Owner gate:** D1–D8 before the build. If the build finds that the Pro guard moves (D1), it stops and brings a sanction (13-3 precedent); the recommended D1-A is designed not to move it.
**Spec:** [league_restructure.md](../design/league_restructure.md) §2.1, §5.1, §5.3, §9 (all), §10, §11.3 (development gradient), §12, §13, §15 (13-6 row), §16-I4/I5/I12/I16; [temptation_and_lineage.md](../design/temptation_and_lineage.md) §4.2 (the first Pro contract) and §8.1 (the Trailblazer's "first"); [13-5 brief](13-5_roster_lock_forfeits_brief.md) §11.5 and §12.3 (Nit N1); the 9c/9d records in [archive/promotion_advancement_gates.md](../archive/promotion_advancement_gates.md) and [reference/development_decline_curves.md](../reference/development_decline_curves.md).
**Branches / worktrees:** brief `p13/13-6-brief-opus` (this file, `C:\Users\DELL\dnd-opus-136`); Math `p13/13-6-mathcheck-argon` (`.worktrees/13-6-mathcheck-argon`); build `p13/13-6a-opus`; review `p13/13-6a-review-argon`. All start from **`p13/integration`** at or after **`9277af5`** (13-5a merged) plus this brief's merge, each in its own worktree, and merge into `p13/integration`. **`main` is reached only after 13-6a turns the 7 promotion reds green** (spec §15, §16-I12; D8 says whether that waits for 13-6b).

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
| Free-agent pool (`BaseballQueries.cs:179–186`, `:1010–1020`; `CareerManager.cs:1257–1283`) | "Signable" = `team_id IS NULL` + a ratings row + age in [18, 42) + health > 40. Used only by succession's backfill | It can't tell a released player from a retiree, an unrecruited HS senior or the avatar's adult children (D3) |
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
5. **Ages and class years don't line up out of the box.** `SchoolGrades` makes 19 the senior age; aging runs before the pipeline at rollover, so a just-graduated senior reads 20. Steady-state College freshmen are 20 (NPC and avatar alike), draft-eligible at 21. World-gen's HS 15–18 and College 18–22 are a one-off transient: no HS senior exists in the first fall, so the first College class comes entirely from outside recruits. 15–16 intake means a 15-year-old spends five HS seasons (the clamp). Argon sizes the transient (Q3); nothing here changes world-gen.
6. **The spec's "held-back" mechanic conflicts with its walk-on guarantee.** §9.1 keeps "merit-based graduation, held-back mechanic all unchanged" (a senior who out-ranks no College incumbent stays in HS) and also guarantees a walk-on offer "so the career always continues". Pro has the same gap: a released or undrafted avatar would be unrostered, which `CareerManager` can't represent today (D4).
7. **Boot-stream discipline.** `GameManager` hands `PromotionManager` `new RngState(rng.NextUInt64() | 1UL)` (`:586–588`). Removing that draw would shift every later boot consumer, the Life sim seed included. The pipeline keeps the draw at the same position, and uses it once to write a persisted `pipeline_seed` (the 13-3 `schedule_seed` pattern). From that seed it derives per-(season, purpose) streams, so a reload between rollovers can't change an intake or a recruit (Q8). The old boot fork was reload-unstable.
8. **The ban flag is Pro-only in content.** `banned_for_life` is set only by `dirt_permanent_ban`, gated `tier >= 2`. `record_arrest` is tier-agnostic. So in a pipeline with no avatar release (D4-A), the ban has nothing left to block, and only `record_arrest` matters for recruiting and the draft (D6).

## 2. Hard constraints

- **Allowed paths (13-6a):**
  - `Assets/Simulation/Baseball/`: new `PipelineManager.cs` (calendar handlers, rollover pass, free agency) and `PipelineRules.cs` (pure: value, bands, draft order, release order, declaration); **delete** `PromotionManager.cs`'s manager and summary (keep `PromotionScore`/`PromotionProfile`, trimmed); edits to `CareerManager.cs` (the avatar handoffs and guarantees, the record-flag constants' new home), `RosterManager.cs` (the pre-lock free-agency hook only), `LeagueGenerator.cs` (a fielding-row helper for generated players and an optional mean offset for outside recruits; **world-gen's draw order unchanged**), `SchoolGrade.cs` and `DevelopmentManager.cs` (comments only).
  - `Assets/Data/Database/`: new `PipelineQueries.cs` and DTOs (`CollegeEnrollmentRow`, `RecruitingOfferRow`, `DraftPickRow`, `FreeAgentRow`, `PipelineStatusView`).
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

### D1. The pipeline vs the sanctioned Pro guard

**Facts** (§1.2 #2): the guard world runs no offseason machinery, and its ledger spans 2026 and 2027.

- **A (recommended). Keep `PipelineManager` out of the guard world**, as `PromotionManager` and `DevelopmentManager` always were. The guard is the Pro *game sim's* regression check; the pipeline gets its own (`PipelineHarness`, §5). The harness adds channels that prove the separation:
  1. the guard (PA/H/ER and ledger SHA) is byte-exact;
  2. no `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks` or `pipeline_*` flag row exists in the guard database after 2027;
  3. a **pipeline-attached twin** of the guard world, run for 2026 only (through day 365, before the rollover), matches the guard world's 2026 ledger byte for byte. That proves the in-season pipeline days (176, 183, 195, 274, 318) and the 27 locks with no signings change no game. 2027 is where the twin diverges, by design;
  4. in that twin, every lock's free-agency step is a no-op (32 teams at 24, so no signing and no re-init), and `LeagueSimulator.RngSnapshot` is unchanged across each one.

  **Stop rule:** if channel 1 or 3 moves, the build stops and brings a fix or a sanction request (13-3 precedent).
- **B.** Wire the pipeline into the guard world and re-baseline the 2027 half of the ledger. It needs a 20/20 Argon reproduction and the owner's sanction, and it ties the guard to every future pipeline tuning.

### D2. The pipeline calendar and persistence

- **A (recommended).** Fixed, derived days (every one a non-Sunday; each a named rule in `TierSeasonCalendar` style, not a literal):
  - **Declaration opens, day 176** (the Monday after the Red Dirt Series' last possible day, 174). A College avatar with `class_year ≥ 2` gets the "Declare for the Draft?" decision with his projected round (D5).
  - **Declaration deadline, day 183** (the first Monday of July). NPCs declare by the rule in §9 Q5; an unanswered avatar decision resolves by the same rule. `declared_season` is written.
  - **The Draft, day 195** (the second Saturday of July, existing `DraftDay`). 128 picks in one batch; `Draft_Picks` rows; the avatar's signing bonus (avatar-only, D5); `ProContractSignedEvent(wasDrafted: true)` per pick; `DraftCompletedEvent`. **Drafted players stay on their College team until the rollover** (the College season is over); nobody joins a Pro roster mid-season.
  - **Offers, day 274** (the first Monday of October). Each program's `OpenSpots` by role (players who leave it at the coming rollover: drafted, finished class 4, and any departures). The avatar, a just-finished HS senior, gets 1–5 offers (Q4) as `Recruiting_Offers` rows (status 0).
  - **Signing Day, day 318** (the second Wednesday of November). The avatar's choice is applied first (unanswered → the best-ranked offering program). Then the NPC assignment runs: programs in last season's national-rank order take the best available recruit for their most-needed role, round by round, until every opening is filled. The pool is HS seniors plus generated outside recruits (Q4). Accepted rows get status 1; the avatar's other offers get 2; anything left open gets 3.
  - **Rollover (day 1, last handler):** retirements (age ≥ 42 or health ≤ 40, the shipped triggers; free agents per §9.4); HS graduates with an accepted offer → their College team and a class-1 enrollment row; unrecruited NPC seniors → retired from baseball; College class years +1, finished class 4 undrafted → Pro free agents, drafted → their Pro team (enrollment row deleted); Pro releases to exactly 15/4/5, lowest value first per role; HS intake per (team, role, defensive position) to refill graduates. Then one batch, every sim and the micro-sim re-initialize once, and the avatar re-activates.
  - **Sunday locks 91–273:** before designations, a team below 24 signs the best free agent for its most-needed role (only below the cap; one re-initialization of the Pro sim and the micro-sim per lock with a signing, before the lock reads rosters). In practice this is the day-91 lock after the rollover's releases. A free agent signed without a `pipeline_pro_signed` flag gets `ProContractSignedEvent(wasDrafted: false)`.
  - **Boot:** an idempotent ensure writes the missing class-year rows for College-rostered players (world-gen and any straggler) by an age map Argon sets (Q3). It also writes `pipeline_seed` once, from the kept boot draw. A crash between a calendar day's commit and the pipeline's re-runs that day's step at boot, keyed on its persisted output (offer rows for the season, a `Draft_Picks` row for the season, and so on), so each step is done exactly once.
- **B.** Everything at the rollover (offers, Signing Day, declaration and draft on 1 January). It's much simpler, but it has no Draft Night in July, no fall signing, and no signed-prospect offseason (spec §9.3, §10).

### D3. Free-agent and career-end state (no DDL)

- **A (recommended). Engine-written `Entity_Flags`**, set and cleared only by `PipelineManager` (and by succession's displacement, §4 item 8):
  - `pipeline_free_agent`: active while a player is a Pro free agent; `set_on_day` = the day he became one. Unsigned seasons = the season difference from `set_on_day`.
  - `pipeline_retired`: a baseball career is over (retirement, an unrecruited HS senior, a retired free agent). Never signable again.
  - `pipeline_pro_signed`: set at a player's first Pro contract. Its absence is what makes a signing "first" for `ProContractSignedEvent`.
  - The **pipeline's** free-agent pool is `team_id IS NULL ∧ pipeline_free_agent active`, with ratings (SQL validated, §4 item 3). An avatar's child, a retiree and an unrecruited teenager are never in it.
  - **Succession's backfill read stays as shipped in 13-6** (the unflagged window; pre-existing, logged), but the players it displaces or retires get the matching flags. Switching its read to the flagged pool is a sub-choice: it changes the `lineage-*` fixtures, so the recommendation is a follow-up.
  - Content doesn't read these flags in 13-6; if it ever does, they join `check_event_graph_integrity`'s engine-flag allowlist.
- **B. Derive the state** from `Draft_Picks`, stats rows and age, with no flags. "Unsigned seasons" and "first contract" then need stats-history scans, and retirees stay indistinguishable from free agents below 42.
- **C. No free agency in 13-6:** teams under the cap get generated replacement-level fillers. That's the simplest path, but it drops a spec rule and the undrafted-senior path.

### D4. The avatar's guarantees

Both parts keep the avatar rostered at every moment, which `CareerManager` requires (§1.2 #6).

- **a. HS → College.**
  - **A (recommended).** The walk-on guarantee replaces "held back": a senior always moves to College at the rollover after his senior season, by an offer he chose or by a walk-on from the lowest-ranked program. A walk-on joins as an extra if that program is under its role cap (≤ 17 position players, org ≤ 26). Otherwise he displaces its weakest same-role player (the succession displacement rule), who retires from baseball. The four-year HS grade gate is unchanged.
  - *B.* Keep held-back: a senior below every program's band repeats his senior year (no offers, no walk-on) and is recruited again next fall; the walk-on applies only after a second failed fall.
- **b. College → Pro and release.**
  - **A (recommended).** The avatar is **never released** in v1 (the 9c "promotes, never auto-relegates" precedent): an NPC of his role goes instead. An undrafted avatar who finishes class 4 gets a **guaranteed free-agent contract** from the lowest-ranked Pro team at the rollover (the walk-on rule, displacement included), with `ProContractSignedEvent(wasDrafted: false)`. An undrafted declared junior returns to College. Release, unsigned seasons and the career end they imply stay NPC-only until a follow-up gives the unrostered avatar a home (UI, Life and succession).
  - *B.* Full spec: the avatar can be released or go undrafted into an unrostered free agency, retire unsigned and end the career into succession. It needs an unrostered-avatar mode in `CareerManager`, the Life bridge and the UI. That's a larger slice.

### D5. The avatar's decisions, texts and money

- **A (recommended). Succession-style pending choices + a Narrative writer.**
  - `PipelineManager` exposes `TryGetPendingRecruitingChoice` / `ResolveRecruiting(teamId)` and `TryGetPendingDeclaration` / `ResolveDeclaration(bool declare)`, with an `AutopilotPipeline` flag (headless default true; `GameManager` sets it false, as it does `AutopilotSuccession`). An unanswered choice resolves at its deadline by the autopilot rule: the best-ranked offer; declare per the NPC rule. **The clock doesn't stop**: the phone text names the deadline.
  - Events (primitives only, after commit): `RecruitingOffersEvent(PlayerId, SeasonYear, OfferCount, IsWalkOn)`, `RecruitSignedEvent(PlayerId, TeamId, SeasonYear, Day)` (the Signing Day cutscene hook, 16b), `DraftCompletedEvent(SeasonYear, Day, AvatarOverallPick, AvatarTeamId)` (0/0 when the avatar wasn't picked), and `ProContractSignedEvent(PlayerId, TeamId, Day, WasDrafted)` (spec §9.4, every first Pro contract, NPCs included).
  - `Assets/Narrative/Pipeline/PipelineBeatWriter` writes the avatar's phone texts in their own batch: offers (`coach_hs`), Signing Day (`coach_college`), the declaration (`agent_diaz`), Draft Night (`agent_diaz`), the undrafted contract (`agent_diaz`). The text is placeholder `pipeline.*` keys with no tokens; Sonnet polishes it. The father's first text is Phase L's, keyed on `ProContractSignedEvent`.
  - **Signing bonus: avatar only** (`PlayerQueries.AdjustFunds` in the draft batch; Argon sets the slot scale, Q5). NPC funds aren't touched, because the gritty-event dispatcher polls `Players.funds` for scope-any events, and 128 NPC bonuses a year would shift NPC event eligibility.
- **B.** Decisions as gritty-event JSON with choice consequences that write flags the pipeline reads. It reuses the dispatcher, but the dispatcher is weighted and polled, so an offer can arrive late or not at all, and the choice set (1–5 programs) is dynamic.

### D6. The criminal record in the pipeline

- **A (recommended).** `record_arrest` docks the avatar's value by `RecordArrestPenalty` (3.0, re-sized by Argon, Q9) wherever the pipeline ranks him: his offer band and count, his projected round, NPC teams' draft boards, and (moot under D4b-A) release order. `banned_for_life` (Pro-only by content, §1.2 #8) has no 13-6 effect under D4b-A; the `pipeline-record` suite proves it. NPCs are never read for either flag. The two constants move with the flag names to a new home (`CareerRecordFlags`), since `PromotionManager` is deleted and GrittyEventsHarness reads them.
- **B.** Drop the record from the pipeline (the dock was a 9c promotion rule).

### D7. Harnesses and runtime

- **Recommended:**
  - **MC default:** the 7 reds are replaced by green suites (§5) at **≤ +10 s** median paired difference against the §0 baseline (the 13-5 D7 method, interleaved pairs). `development-equilibrium`'s 80-generation ladder run leaves the default.
  - **New `Tools/PipelineHarness`** (opt-in; the gate for any change to the pipeline, its values or the development curve, and it joins `run_monte_carlo_batch`'s skill for those paths):
    - **default mode:** 20 seasons, offseason-only (calendar days and rollovers with no game sims, as 9d §7's equilibrium ran). Performance reads neutral, so draft and recruiting order fall to their no-history rules. Budget ≤ 60 s;
    - **`--full` mode:** 3 seasons with all three tier sims playing (real P, real standings, real draft order) for the journey and a composed-rollover check. Budget ≤ 4 min; it runs at the 13-6a build and review, not on every change.
- *B.* Everything in MC default (over budget by an order of magnitude); *C.* only `--full` mode (20 seasons with games, ~10+ min a run on this PC).

### D8. Slicing and the UI

- **A (recommended).** **13-6a:** engine, persistence, events, beat writer, harnesses, all decisions working headlessly and through the `Resolve…` API (Opus; Argon reviews). **13-6b:** the phone's decision cards (offers, declare) and the read-only Draft board, on `PipelineStatusView` (Sonnet; Neon reviews; restyled in 15c). `p13/integration` → `main` after **13-6a** merges and its gates are green, without waiting for 13-6b. Draft Night and Signing Day cutscenes stay 16b's.
- *B.* One slice including the UI (spec §15's original row); `main` waits for the UI.

## 4. Outputs (13-6a, per the recommended options)

1. **`PipelineRules`** (pure, no RNG, no DB):
   - value: `PromotionScore.Combine(P, S)` with the tier's shrink and the tier's league OPS/ERA, the avatar's record dock (D6);
   - the avatar's offer band and count (Q4); the NPC recruiting order;
   - projected slot, the declaration rule and draft order, both the prior-season order and the inaugural one (Q5);
   - role need, release order and signing order (Q5–Q6);
   - the free-agent retirement test.
   Every constant is a named `PipelineProfile` data value.
2. **`PipelineManager`:** the D2 handlers; the boot ensure; the rollover pass (one batch, flush before, re-init once after, `ReactivateAvatar` last, as `PromotionManager` did); the pre-lock free-agency hook `RosterManager` calls (`IFreeAgencyMarket`); the D5 pending choices; `PipelineStatusView` (this season's class, offers, projected round, draft board, free agents); a `LastRun` summary for UI and harnesses (removals, graduates, recruits, outside recruits, draftees, releases, signings, intake, retirements).
3. **`PipelineQueries`** + DTOs. Validated on a scratch v14 (§0.4) with these results:
   - enrollment upsert `INSERT INTO College_Enrollment (player_id, team_id, entry_season, class_year, declared_season) VALUES (…) ON CONFLICT (player_id) DO UPDATE SET team_id = excluded.team_id, class_year = excluded.class_year, declared_season = excluded.declared_season` (insert and update paths executed);
   - class advance `UPDATE College_Enrollment SET class_year = class_year + 1` and departure `DELETE FROM College_Enrollment WHERE player_id = @playerId`;
   - enrollment load `SELECT … FROM College_Enrollment ORDER BY player_id` (PK-ordered scan, ≤ ~600 rows);
   - offers upsert `… ON CONFLICT (player_id, team_id, season_year) DO UPDATE SET status = excluded.status`; the avatar's offers `WHERE player_id = @playerId AND season_year = @season ORDER BY team_id` (PK search on `player_id`); a season's accepted rows `WHERE season_year = @season AND status = 1 ORDER BY player_id`, and the expiry `UPDATE … SET status = 3 WHERE season_year = @season AND status = 0`. Both are scans: the table grows ~150–200 rows a season, bounded and read twice a year; add no index (no DDL);
   - draft insert and the class read `WHERE season_year = @season ORDER BY overall_pick` (PK search). Never query by `player_id` (no index); "was drafted" is the `pipeline_pro_signed` flag or the in-memory class;
   - the free-agent pool `SELECT p.player_id, …, f.set_on_day FROM Entity_Flags AS f JOIN Players AS p … JOIN Player_Ratings AS r … LEFT JOIN Pitcher_Roles AS pr … WHERE f.flag_name = @flag AND f.is_active = 1 AND p.team_id IS NULL ORDER BY p.player_id` (search on `idx_entity_flags_active_name`, PK probes, a temp B-tree for the order; an inactive flag and an unflagged unrostered player correctly excluded);
   - flags through the existing `PlayerQueries.SetFlag` upsert; standings and series through the existing `PlayoffQueries.LoadTeamSeasons` / `LoadSeries`.
4. **Retire `PromotionManager`:** delete the manager and `PromotionSummary`; trim `PromotionProfile` to the value constants; move `RecordArrestFlagName`/`BannedForLifeFlagName` (D6); `GameManager` builds `PipelineManager` in `PromotionManager`'s slot with the same boot draw (§1.2 #7). The stale call-up and promotion-ladder comments in `PersonEffects.cs`, `ScoutingGrade.cs`, `SchoolGrade.cs` and `DevelopmentManager.cs` get the doc-only touch 13-5a §11.5 deferred (comments only).
5. **Free agency at the lock:** `RosterManager.Lock` calls the market first. With no team below 24 it's a no-op (0 B extra, no re-init). Otherwise the signings commit in their own batch, the Pro sim and the micro-sim re-initialize once, the avatar re-activates if his team changed, and only then do the designations run.
6. **Generated players get fielding rows:** HS intake inherits the departed player's defensive position (so every HS team keeps its field covered); outside recruits and fillers roll one from `DepthPositionWeights`. Draws come from the pipeline's derived streams and never from world-gen's.
7. **Core events** (`CoreEvents.cs`, primitives only): `RecruitingOffersEvent`, `RecruitSignedEvent`, `DraftCompletedEvent`, `ProContractSignedEvent` (D5). Each publishes after its batch commits.
8. **Avatar (D4, D6):** the walk-on and the undrafted-senior contract through the displacement rule; release protection; the record dock. Succession's displaced NPC gets `pipeline_free_agent` and its retiree gets `pipeline_retired` (its backfill read unchanged, D3).
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

- **`pipeline-score`** (from `promotion-sweep`): `PromotionScore` §2.1/§2.2 math unchanged (its three fixtures carried over); value per tier (HS shrink for HS seniors, College shrink for the draft); projected slot; draft order from a fixture standings table (non-playoff by reverse record and I14 ties, playoff teams by elimination round, champion last) and the seeded inaugural order; release order; the free-agent retirement test.
- **`pipeline-stream`** (from `promotion-stream`): a pipeline with nothing to do (no seniors, no class 4, no declarations, every team at cap) is a complete no-op, and the next season is bit-identical with the pipeline attached vs absent. Each derived stream is reload-stable: a save reloaded mid-season produces the same intake and recruit ids at the next rollover.
- **`pipeline-avatar`** (from `promotion-avatar`):
  - the four-year grade gate (a max-built freshman stays in HS through three offseasons);
  - senior fall: 1–5 offers, the choice lands him on that program as class 1 at the rollover, the origin HS sim reclaims its team, `AvatarChangedEvent` republishes, his stats persist;
  - the walk-on (no offers) and its displacement;
  - declaration as a sophomore, the draft, Pro at the rollover with `ProContractSignedEvent(wasDrafted: true)` once, bonus credited once;
  - undrafted junior returns; undrafted senior's guaranteed contract (`wasDrafted: false`);
  - never released;
  - succession precedence: a pending succession skips the avatar's moves that offseason, NPC moves still run.
- **`pipeline-record`** (from `promotion-record`): the dock flips a marginal offer and a marginal projected round, and clearing the record flips them back; `banned_for_life` changes nothing (D6-A); NPCs are never read.
- **`development-arc`** (rewritten): a developed HS prospect is recruited, drafted and reaches Pro; a health-worn Pro veteran declines, is released, goes unsigned and retires; never-overshoot holds; determinism.
- **`practice-seam`** (its §10.9 section rewritten): the one College opening goes to the high-headroom probe over its at-ceiling twin through recruiting.
- **`pipeline-lock`:** a team below 24 signs at the next lock, then re-initializes once; a full league's lock is a 0 B no-op; a released NPC is a free agent with the flag; an unflagged unrostered player (an avatar's adult child, an unrecruited senior) is never signed.
- **`season-pipeline` additions:** D1-A channels 2–4.

**`PipelineHarness` default (20 seasons, offseason-only):**

- **Conservation:** after every rollover and every opening lock, HS teams stay within their generated roster shape with the field covered, College teams hold their role counts (≤ 17 / ≤ 4 / ≤ 5, org ≤ 26), and Pro holds 32 × 15/4/5 = 768, or within ±5% if a role runs dry, with every shortfall reported.
- **Flows each season:** exactly 128 picks, unique players, all `Draft_Picks` rows legal; recruits = openings; intake = HS graduates; a population ledger per tier (in, out, by cause) that balances.
- **The journey:** at least one intake player traced HS → College → drafted → Pro, plus the count of intake players who reach Pro by season 20.
- **The equilibrium gradient:** time-averaged mean talent HS < College < Pro, strictly monotone, for batters and for pitchers (metric, burn-in, window and the stationarity bound per Q7).
- **Hygiene and determinism:** no unflagged player ever signed; no player in two tiers; enrollment rows only for College-rostered players; FK clean; no open batch; two same-seed runs byte-identical.

**`PipelineHarness --full` (3 seasons, all sims):** real standings drive draft and recruiting order; a composed rollover (sims, roster, career, playoffs, development, pipeline) dispatches without throwing (§1.2 #1); attended avatar games keep working across both tier changes.

**CoreLoop fixtures:**

- a rollover with the live composition doesn't throw and lands the avatar on the right team;
- a pending recruiting or declaration choice survives a reload and resolves once;
- a reload on day 195 after the Draft's commit doesn't re-draft;
- the offers and Signing Day texts arrive on their days;
- the clock never stops for a pipeline decision.

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
- An unrostered avatar: release, unsigned free agency, a career ending there (D4b-B follow-up).
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
