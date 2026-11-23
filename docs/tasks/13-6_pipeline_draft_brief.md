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

---

## 10. Math check — Gemini 4 Argon, 2026-11-23

- **Verdict:** **APPROVE WITH CHANGES** (7 numbered amendments in §10.4 for Claude Opus 5.5 to reconcile into §§1–9 before the owner's D1–D8 sign-off and the 13-6a build).
- **Evidence base:**
  - Read and line-checked every §1.1 seam and §1.2 finding on `9277af5` (`p13/integration` with 13-5a merged; §10.2).
  - Executed all 7 SQL statements from §4 item 3 on an isolated scratch v14 database (`sqlite3` v3.53 over `Assets/Data/Database/SchemaDefinitions.sql` with `PRAGMA foreign_keys = ON`, `EXPLAIN QUERY PLAN`, `PRAGMA foreign_key_check`, and `PRAGMA integrity_check`; §10.2.3).
  - Built and ran bit-exact Python `RngState` (`xoshiro256**` + long-jump `Split()`) and `LeagueGenerator` + `DevelopmentCurve` + `PipelineManager` probes in `%TEMP%` (cleaned up before commit) over 20 seasons across three world seeds (`20260703`, `20261603 = 20260703 + 900`, `20260704`), testing both offseason-only (`P = 100.0`) and noisy-$P$ seasons.

---

### 10.1 Answers to §9 Questions (Q1–Q10)

#### Q1. Guard separation (D1-A) and complete channel list

1. **Why `PipelineManager` has no path into the Pro game sim during Season 1 (`2026`, days `1..365`):**
   - The sanctioned Pro guard (`Tools/MonteCarloHarness/Program.cs:504–628`) runs **two Pro seasons (`2026–2027`, 730 days)** on a single Pro `LeagueSimulator` (`SeasonSeed = 777`, flat `ratingSpread = 0`), with `RosterManager` and `PlayoffManager` wired, and no `CareerManager`, `DevelopmentManager`, or `PipelineManager`. Because the sanctioned two-season counting-ledger SHA-256 (`932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`) covers both `2026` and `2027`, attaching `PipelineManager` across the `2026 → 2027` rollover would draft 128 College players, move them onto Pro rosters on day 366, release 128 Pro incumbents, and re-initialize the Pro sim for `2027`—changing the `2027` half of the ledger.
   - Within Season 1 (`2026`, days `1..365`, before the day-366 rollover), `PipelineManager` touches **zero** Pro `LeagueSimulator` arrays, **zero** `Roster_Designations` rows, and **zero** `LeagueSimulator` RNG draws:
     - **Boot ensure (day 1):** writes 576 `College_Enrollment` rows (`team_id 201..224`) and `Game_State["pipeline_seed"]`. No Pro table or sim state is read or mutated.
     - **27 Pro Sunday locks (days `91, 98, …, 273`):** at every Season-1 lock, all 32 Pro teams have `15 Pos + 4 SP + 5 RP = 24` players (`orgCount == 24`, no role deficit) and the `pipeline_free_agent` pool has `0` rows. The pre-lock free-agency hook takes its fast path (`0` signings, `0` DB writes, `0` Pro sim re-initializations, `0 B` allocation).
     - **Day 176 (June 25, declaration window opens):** no avatar exists in the guard world (`avatarId == null`) $\implies$ instant no-op.
     - **Day 183 (July 2, declaration deadline):** scores College class $\ge 2$ players and writes `College_Enrollment.declared_season = 2026`.
     - **Day 195 (July 14, the Draft):** orders the 32 Pro teams by the inaugural key (`Team_Seasons` has no `2025` rows), writes 128 `Draft_Picks` rows and 128 `Entity_Flags(pipeline_pro_signed = 1)` rows in its own batch, and clears `declared_season = NULL` on undrafted underclassmen. **Crucially, drafted College players stay on their College `team_id` (`201..224`) until rollover** (`Players.team_id` is untouched on day 195), so subsequent Pro Sunday locks (`days 196..273`) and the Pro postseason (`days 242..275`) see the exact same 768 Pro players.
     - **Days 274 & 318 (Oct 1 & Nov 14, College Offers & Signing Day):** occur after the final Pro Sunday lock (`day 273`) and after Pro regular-season games (`day 240`). Even though day 274 falls 1 day before Game 5 of the World Championship (`day 275`), `OnOffersDay` writes nothing for NPCs (`avatarId == null` $\implies$ no-op), and `OnSigningDay` (day 318, after the postseason ends on day 275) writes only `Recruiting_Offers(status = 1)` and newly signed outside-recruit rows (`Players.team_id IS NULL`), which Pro never reads.
2. **Sufficiency of the 2026-only pipeline-attached twin and complete 8-channel D1-A list in `season-pipeline`:**
   - A 1-season (`2026`, days `1..365`) twin of the guard world with `PipelineManager` attached exercises **every** in-season pipeline hook (boot ensure, 27 Sunday pre-lock free-agency checks, day-176 open, day-183 declaration, day-195 Draft, day-274 offers, day-318 Signing Day) alongside a live Pro regular season and postseason, while proving zero perturbation of the Pro game sim. Advancing that same twin 1 additional day (`day 366`, across the `2026 → 2027` rollover) provides the positive control proving `PipelineManager` immediately turns over all 32 Pro rosters at rollover.
   - **Complete 8-channel D1-A guard-separation verification list for `season-pipeline` (Amendment 7):**
     1. **Sanctioned 2-season Pro guard byte-exact (unwired):** Season 1 (`2026`) `PA == 81433`, `H == 18471`, `ER == 8803`; two-season (`2026–2027`) phase-0 counting-ledger SHA-256 `== 932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`.
     2. **Unwired guard pipeline-table emptiness:** After day 730 in the guard DB, `COUNT(*)` is `0` in `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks`, `Entity_Flags WHERE flag_name LIKE 'pipeline_%'`, and `Game_State WHERE key = 'pipeline_seed'`.
     3. **2026 pipeline-attached twin game-sim & ledger byte-identity:** Running a twin of the guard world with `PipelineManager` wired (`RosterManager` pre-lock free-agency hook active + calendar handlers registered) through day `365` of `2026` produces `PA == 81433`, `H == 18471`, `ER == 8803`, a `2026` phase-0 batting/pitching stat-ledger SHA-256 identical to the unwired guard world at day `365`, and identical `2026` Pro postseason results (`11` series, `30` games, champion `323`).
     4. **Twin Sunday-lock free-agency no-op, RNG invariance, and `0 B` allocation:** Across all 27 Pro Sunday locks (`days 91..273`) in the 2026 twin, `FreeAgencySignings == 0`, post-boot Pro sim re-initializations `== 0`, `LeagueSimulator.RngSnapshot` is unchanged across every lock and equals the unwired guard's `RngSnapshot` at day 365, and a warm no-signing Sunday lock allocates `0 B` beyond the designation upsert.
     5. **Twin pipeline calendar execution proof:** At day 365 of the 2026 twin, `College_Enrollment` has `576` rows, `Draft_Picks WHERE season_year = 2026` has `128` rows (all 32 teams × 4 rounds, ordered by the inaugural key), `Entity_Flags WHERE flag_name = 'pipeline_pro_signed' AND is_active = 1` has `128` rows, and `Recruiting_Offers WHERE season_year = 2026 AND status = 1` equals the number of `2026 → 2027` College openings (`class_year == 4` plus drafted underclassmen).
     6. **Twin Pro roster & designation invariance through day 365:** Every Pro team `301..332` in the twin retains its exact boot 24-player `PlayerId` set through day 365 (drafted College players remain on `team_id 201..224` on days `195..365`), and all `768` `Roster_Designations` rows at every lock match the unwired guard world byte for byte.
     7. **Day-366 rollover turnover proof (positive control):** Advancing the twin from day 365 to day 366 (triggering the `2026 → 2027` rollover) moves all `128` drafted players onto Pro `team_id 301..332`, releases `128` Pro incumbents to `pipeline_free_agent` (`team_id IS NULL`), conserves `15 Pos / 4 SP / 5 RP = 24` per Pro team (`768` total), and changes the Pro roster `PlayerId` set as designed.
     8. **Existing 20 channels preserved:** All 10 postseason channels (13-4 Q7) and all 10 designation/forfeit channels (13-5 D1-A) in `season-pipeline` remain green.

---

#### Q2. Population flows, conservation, $\pm 5\%$ reachability, role run-dry odds, filler fallback, and average Pro career

1. **Cross-Role Draft Board Discovery — Reliever Stamina Compensation (Amendment 1):**
   - `LeagueGenerator.GeneratePlayer` ([LeagueGenerator.cs](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L379)) subtracts `RelieverStaminaPenalty = 20` from every Reliever's `PitStamina` potential ceiling (`potStam = Math.Max(MinPotential, RollPotential(...) - 20)`). Consequently, an average Reliever's raw three-tool sum `PitStuff + PitControl + PitStamina` is centered at **`130`** (`Scouting = 86.67`) compared to **`150`** (`Scouting = 100.0`) for an average Batter or Starter—a structural **`-6.67` Combine-point penalty** on any cross-role board.
   - Without compensation, the cross-role Draft board starves Relievers: in our 20-season Python probe without compensation, only **`8.8 / 128` (`6.9%`)** of draft picks were Relievers (versus `78.5 Pos [61.3%]` and `40.7 SP [31.8%]`), leaving ~89% of Pro RP rosters stagnant until age-42 retirement.
   - **Exact fix (Amendment 1):** Define `PipelineRules.RoleRatingSum(in RosterPlayerRow row)` with Reliever stamina compensation equal to `LeagueGenerator.RelieverStaminaPenalty` (`20`), clamped to `100` on the stamina component:
     $$\text{RoleRatingSum}(\text{row}) = \begin{cases} \text{BatPower} + \text{BatContact} + \text{BatDiscipline} & \text{if } \text{Role} = \text{PitcherRole.None} \\ \text{PitStuff} + \text{PitControl} + \text{PitStamina} & \text{if } \text{Role} = \text{PitcherRole.Starter} \\ \text{PitStuff} + \text{PitControl} + \min(100, \text{PitStamina} + 20) & \text{if } \text{Role} = \text{PitcherRole.Reliever} \end{cases}$$
     Because `+20` is a constant across all Relievers (aside from the rare `PitStamina > 80` clamp, which is unreachable when `potStam <= 80`), **within-role Reliever ordering is 100% unchanged** (College recruiting by role, Pro RP releases, Pro RP free-agent signings), while on the cross-role Draft board all three roles share the same `150` mean and the draft role mix matches the `15 : 4 : 5` (`62.5% : 16.7% : 20.8%`) roster ratio.
2. **Steady-State Annual Population Flows per Tier and Role:**
   - Measured over the 15-season steady-state window (`seasons 6–20`, rollovers `5..19`) on `LeagueSeed = 20260703` (and cross-checked on `20261603` and `20260704`) with `IntakeMinAge = 16`, `OutsideRecruitMeanOffset = -2`, `OutsideRecruitDiscountAge = 25`, and Reliever stamina compensation:

| Flow / Population Quantity | Batters (`None`) | Starters (`SP`) | Relievers (`RP`) | Total | Conservation / Analytical Formula |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **HS Rostered Population** (`12` teams, `11..14 / 3..4 / 2..4`) | `149.0` *(150.0 exp)* | `42.0` *(42.0 exp)* | `29.0` *(36.0 exp)* | **`220.0`** *(228.0 exp)* | **Exact 1-for-1 per `(team, role, pos)`**: each graduating senior is replaced by 1 intake player on the same team, role, and primary position. |
| **HS Graduates = HS Intake / yr** (`age == 19` in fall) | `37.25` *(37.50 exp)* | `10.50` *(10.50 exp)* | `7.25` *(9.00 exp)* | **`55.00`** *(57.00 exp)* | $\lambda_{\text{HS}} = N_{\text{HS}} / 4$ (every intake player enters at age `16` and graduates after 4 seasons at age `19`). |
| **HS Graduates Recruited to College / yr** | `34.5` (`92.6%`) | `9.3` (`88.6%`) | `7.7` (`90.6%`)* | **`51.5`** (`91.3%`) | Top HS seniors out-compete the lower tail of the `+15/+5/+5` outside-recruit surplus. (*Seed `20261603` has `34 RP / 4 = 8.5/yr`.) |
| **HS Graduates Unrecruited (`pipeline_retired`) / yr** | `2.8` (`7.4%`) | `1.2` (`11.4%`) | `0.9` (`9.4%`) | **`4.9`** (`8.7%`) | Retires at HS graduation (`team_id = NULL`, `pipeline_retired = 1`); never enters Pro FA. |
| **College Rostered Population** (`24` teams × `15 / 4 / 5`) | `360.0` | `96.0` | `120.0` | **`576.0`** | **Exact `15 / 4 / 5 = 24` per team (`576` total)** at every rollover. |
| **College Openings / yr** (`Drafted + Undrafted Seniors`) | `118.9` | `31.6` | `37.9` | **`188.3`** | `128.0` drafted (`82.5 Pos, 22.3 SP, 23.3 RP`) + `60.3` undrafted `class_year == 4` seniors (`36.4 Pos, 9.3 SP, 14.6 RP`). College age/health retirements $= 0$. |
| **Outside Recruits Rolled / Signed into College / yr** | `99.4` rolled / **`84.4`** signed | `27.3` rolled / **`22.3`** signed | `35.1` rolled / **`30.1`** signed | `161.8` rolled / **`136.8`** signed | Rolled: $\max(0, \text{Open}(r) - \text{HS}(r)) + \text{Surplus}(r)$ (`+15/+5/+5`). Only signed recruits (`136.8/yr`) are persisted to SQLite. |
| **Draft Declarers / yr** (day 183) | `157.5` | `43.2` | `53.2` | **`253.9`** | `~1.98x` the 128 draft slots (`1.91x Pos, 1.94x SP, 2.28x RP`). **`65.5/yr`** undrafted underclassmen return to College. |
| **Draft Picks / yr** (day 195, `32 × 4 = 128`) | `82.5` (`64.5%`) | `22.3` (`17.4%`) | `23.3` (`18.2%`) | **`128.0`** (`100%`) | Matches the `15 : 4 : 5` (`62.5% : 16.7% : 20.8%`) Pro roster shape via RP stamina compensation + `DraftRoleQuota` (`3/1/1`). |
| **Pro Rostered Population** (`32` teams × `15 / 4 / 5`) | `480.0` | `128.0` | `160.0` | **`768.0`** | `767.0–768.0` at rollover; **exact `768.0`** after the day-91 opening Sunday lock. |
| **Pro Departures by Cause / yr** (at rollover) | `0.7` age/health + `81.9` released | `0.3` age/health + `22.1` released | `0.2` age/health + `23.1` released | **`1.2` age/health + `127.0` released** | In steady state (seasons 6–20), most declining veterans are cut in their 30s (`127.0/yr`) before reaching age 42 (`1.2/yr`; `4.5/yr` in seasons 1–10). |
| **Pro Free-Agent Pool at Day-91 Lock** (`pipeline_free_agent`) | `205.8` (`63.2%`) | `55.0` (`16.9%`) | `64.9` (`19.9%`) | **`325.7`** (`100%`) | `127.0` newly cut Pro + `60.3` undrafted College seniors (all age `24 < 30`) + `~138.4` 2nd-year FAs under age 30. |
| **Day-91 Lock Free-Agent Signings & FA Retirements / yr** | `0.1–0.7` signed / `118.2` retired | `0.1–0.5` signed / `31.3` retired | `0.0–0.3` signed / `38.5` retired | **`0.2–1.5` signed / `188.0` retired** | Fills any Pro team where an age-42/health-40 retirement occurred in a role the team did not draft that July; FA retirements (`age >= 30 || unsigned >= 2`) balance FA inflows (`127.0 + 60.3 ≈ 187.3`). |

3. **Is $\pm 5\%$ reachable at all under D2/D3? Probability a role runs dry, and whether to add a generated filler fallback:**
   - **HS and College have $0.0\%$ population drift by construction** (`HS` replaces graduating seniors 1-for-1; `College` signs exact `OpenSpots[team, role]`).
   - **Pro sits at `-0.4% to 0.0%` (`765..768`) immediately after rollover and `0.0%` (`768 / 768`) after the day-91 opening lock.** The only way a Pro team is temporarily at `23` between day 1 and day 90 is when a veteran retires via `age >= 42` or `health_ceiling <= 40` at rollover in a role that team did not draft that July (`~0.2–1.5` occurrences/year across the 32 teams); the day-91 opening Sunday lock immediately signs a free agent of that role before Opening Day (`day 92`).
   - **Role run-dry probability:** At the day-91 lock, the signable `pipeline_free_agent` pool contains **`~206 Pos, ~55 SP, ~65 RP`** (with a deterministic floor of `>= 36 Pos, >= 9 SP, >= 14 RP` from that rollover's undrafted College seniors alone, plus `>= 80 Pos, >= 22 SP, >= 23 RP` from that rollover's Pro cuts). Against `0..3` openings in any role, the probability a role runs dry organically is **$p < 10^{-40}$** (`0` instances across `20 seasons × 32 seeds = 640` league-seasons).
   - **Do NOT add a generated replacement-level filler fallback at the Sunday lock:**
     - `RosterManager.Lock` is an RNG-free pure function of DB state (`RosterManager.cs:24`); generating players inside `Lock` would require drawing from an RNG stream on Sunday nights and creating new `Players` rows mid-season.
     - More importantly, `PipelineHarness`'s $\pm 5\%$ tier-population gate (`[730, 806]` for Pro) is designed to catch any conservation bug in `PipelineManager`; a synthetic filler generator at the lock would mask a real leak. If the free-agent pool for role $r$ is ever artificially emptied in a crafted test fixture, `FillProOpeningsBeforeLock` simply signs as many free agents of role $r$ as exist (`0` if empty), and `RosterManager.Lock` designates from the remaining roster (`RosterRules.SelectProDesignations` already handles `< 24` gracefully via `Math.Min`).
4. **Average Pro career length:**
   - By Little's Law ($L = \lambda W$), with steady-state Pro population $L = 768$ and annual Pro intake $\lambda = 128.0\text{ draftees} + 0.2\text{ undrafted-FA signings} = 128.2/\text{yr}$:
     $$W = \frac{L}{\lambda} = \frac{768}{128.2} = \mathbf{5.99\text{ seasons}} \quad \left(\frac{768}{128} = \mathbf{6.00\text{ seasons per draft class}}\right)$$
     confirming spec §9.3 (`"Average Pro career length ≈ 6 seasons"`).

---

#### Q3. Ages, graduation rule, intake ages, world-gen transient, and boot class-year map

1. **HS graduation rule (`SchoolGrades` unchanged):**
   - Keep `SchoolGrades` ([SchoolGrade.cs](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/SchoolGrade.cs#L15-L34): `FreshmanAge = 16`, `SeniorAge = 19`, `ForAge(age)` clamping `<= 16 → Freshman` and `>= 19 → Senior`) unchanged.
   - **Timing seam across `CareerManager.AgeAllPlayers()`:**
     - On fall calendar days **274 (Offers Day)** and **318 (Signing Day)**—which run *before* the day-1 rollover `AgeAllPlayers()`—an HS player is a graduating senior in `seasonYear` iff **`age >= SchoolGrades.SeniorAge` (`age >= 19`)** (equivalently `SchoolGrades.ForAge(age) == SchoolGrade.Senior`).
     - At the **day-1 rollover** (`OnSeasonRolledOver`), `CareerManager.AgeAllPlayers()` ([CareerManager.cs](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L439)) runs **before** `_bus.Publish(new SeasonRolledOverEvent(...))` ([CareerManager.cs:458](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L458)) and increments every player's `Players.age` by `+1`. Therefore, at rollover, the HS seniors who just completed their age-19 senior season now have **`age >= SchoolGrades.SeniorAge + 1` (`age >= 20`, i.e., `age > SchoolGrades.SeniorAge`)** or an accepted `Recruiting_Offers` (`status = 1`) row for `completedSeasonYear`. Using `age > SchoolGrades.SeniorAge` (or `preRolloverAge >= SchoolGrades.SeniorAge`) at rollover is essential so rising seniors who just turned `19` inside `AgeAllPlayers()` are not graduated a year early.
2. **HS intake age (`IntakeMinAge = 16, IntakeAgeSpan = 1` — Amendment 2):**
   - Retiring 9c's `IntakeMinAge = 15, IntakeAgeSpan = 2` (`15–16`) in favor of **age `16` only (`IntakeMinAge = 16, IntakeAgeSpan = 1`)** is strictly better on every axis:
     - Because `PipelineManager` generates HS intake at rollover *after* `AgeAllPlayers()`, an intake player generated at `age = 16` plays Season $Y$ as a `16`-year-old Freshman, Season $Y+1$ as a `17`-year-old Sophomore, Season $Y+2$ as an `18`-year-old Junior, and Season $Y+3$ as a `19`-year-old Senior—**exact 4 HS seasons for every NPC**, matching the avatar's `16 → 17 → 18 → 19` runway and eliminating the 5-year HS clamp artifact (`15 → 16 → 17 → 18 → 19`) where half of intake sat clamped at `SchoolGrade.Freshman` for two years.
     - It makes the steady-state graduation fraction exactly $1/4 = 25.0\%$ (`55.0/yr` on seed `20260703`, `57.0/yr` expected across seeds) instead of $1/4.5 = 22.2\%$.
3. **World-gen transient sizing:**
   - **HS transient (1 season to steady-state graduation flow; 4 seasons to full cohort replacement):**
     - `LeagueGenerator.TierAgeRolls[(0, _)] = (15, 4)` seeds world-gen HS players uniformly across ages **`{15, 16, 17, 18}`** (on `LeagueSeed = 20260703`: `53` at age 15, `51` at age 16, `58` at age 17, `58` at age 18; **`0` at age 19**).
     - Thus in **Season 1 (`2026`)**, there are **`0` simulated HS seniors (`age >= 19`)** on day 274 / day 318 (unless the test places an age-19 senior avatar on an HS roster). All Season-1 College openings (`~247` openings) are filled by the outside-recruit pool, and Season-1 HS intake is `0`.
     - At the **`2026 → 2027` rollover**, `AgeAllPlayers()` shifts the four world-gen HS cohorts from `{15, 16, 17, 18}` to **`{16, 17, 18, 19}`**! Starting in **Season 2 (`2027`)**, the top cohort (`age == 19`: `58` players in 2027, `58` in 2028, `51` in 2029, `53` in 2030) graduates every fall and is replaced 1-for-1 by age-`16` intake. Thus **HS reaches its exact `{16, 17, 18, 19}` steady-state age distribution in Season 2 (`2027`) with zero cohort oscillation**, and 100% of world-gen HS players have graduated by the `2029 → 2030` rollover (end of Season 4).
   - **College transient (4 seasons, `2026–2029`; completely flushed by Season 5 `2030`):**
     - `LeagueGenerator.TierAgeRolls[(1, _)] = (18, 5)` seeds world-gen College players uniformly across ages **`{18, 19, 20, 21, 22}`** (on `LeagueSeed = 20260703`: `125` at age 18, `104` at age 19, `122` at age 20, `120` at age 21, `105` at age 22), whereas steady-state College players enter at age `20` (`19` on Signing Day $+ 1$ at rollover) and occupy ages **`{20, 21, 22, 23}`** (`age == 19 + class_year`).
     - Because `class_year` increments by `+1` each rollover and every `class_year == 4` senior departs after at most 4 seasons, **100% of world-gen College players depart by the `2029 → 2030` rollover (end of Season 4)**. From Season 5 (`2030`, rollover index `4`) onward, every College player entered via Signing Day at age `20`.
4. **Boot ensure class-year map (`clamp(age - 17, 1, 4)`) and Year-3 Draft Starvation Proof (Amendment 3):**
   - For any College player missing a `College_Enrollment` row at boot ensure (`EnsureCollegeEnrollment`), set:
     $$\text{class\_year} = \text{Math.Clamp}(\text{age} - 17, 1, 4), \qquad \text{entry\_season} = \text{seasonYear} - (\text{class\_year} - 1), \qquad \text{declared\_season} = \text{null}$$
   - On `LeagueSeed = 20260703`, this maps the 576 world-gen College players to:
     - `age 18 → class_year = 1` (Freshman): **`125`** players (`21.7%`)
     - `age 19 → class_year = 2` (Sophomore, draft-eligible): **`104`** players (`18.1%`)
     - `age 20 → class_year = 3` (Junior, draft-eligible): **`122`** players (`21.2%`)
     - `ages 21–22 → class_year = 4` (Senior, graduating): **`225`** players (`39.1%`)
   - **Does it starve the first draft (`2026`)?** No: in `2026`, `451` players have `class_year >= 2`, and `225` seniors (`class_year == 4`) plus `~30` top-64 underclassmen (`255` total declarers) declare for the `128` picks.
   - **Hidden Year-3 (`2028`) Draft Starvation under a literal `age >= 22` rule (and why Amendment 3 is required):**
     - Because all `225` boot `class_year == 4` seniors plus `~30` drafted underclassmen (`255` players = `44.3%` of College) depart at the `2026 → 2027` rollover, only the `104` boot `age 19 (cy = 2)` players advance toward `class_year = 4` in `2028`. After `~48` of those `104` players are drafted as sophomores in `2026` or juniors in `2027`, only **`~56` `class_year == 4` seniors** remain in **Season 3 (`2028`)**—and their age in `2028` is **`21` (`19 + 2`)**!
     - Meanwhile, in `2028`, the `class_year == 3` Juniors (boot `age 18, cy = 1`) have age **`20` (`< 22`)**, and the `class_year == 2` Sophomores (recruited in fall 2026 at age 19, entered College in 2027 at age 20) have age **`21` (`< 22`)**!
     - Consequently, in Season 3 (`2028`), the set `{player in College : age >= 22}` is **completely empty (`0` players)**! Under a literal `classYear == 4 || projectedSlot <= 64 || age >= 22` declaration rule, the entire `2028` declaration pool is `{classYear == 4} ∪ {projectedSlot <= 64}`, which has size at most `56 + 64 - overlap ≈ 100 < 128`—**starving the Year-3 Draft by ~28 picks** (`only 100` picks made out of `128`)!
     - **Why `age >= 22` was written in the spec:** In steady state (`age = 19 + class_year`), `age == 20 (cy 1), 21 (cy 2), 22 (cy 3), 23 (cy 4)`, so **`age >= 22` is synonymous with `class_year >= 3` (all Juniors and Seniors)**. The only time a College Junior (`class_year == 3`) has `age < 22` is during the world-gen transient (`2026–2028`, when boot ages `18–20` are 2 years younger than steady-state `20–22`).
     - **Exact fix (Amendment 3):** Write `PipelineRules.ShouldNpcDeclare(int classYear, int age, int projectedSlot)` as:
       ```csharp
       public static bool ShouldNpcDeclare(int classYear, int age, int projectedSlot) =>
           classYear >= 2 && (classYear >= 3 || age >= 22 || (projectedSlot >= 1 && projectedSlot <= 64));
       ```
       In steady state (`seasons 5+`), `classYear >= 3` and `age >= 22` are **100% identical** (`age = 19 + classYear >= 22 ⇔ classYear >= 3`), while during the `2026–2028` world-gen transient `classYear >= 3` includes the age-20/21 Juniors as intended, lifting the Season-3 (`2028`) declaration pool from `100` (starved) to **`193 > 128`** and keeping all 20 seasons at exactly `128 / 128` draft picks.

---

#### Q4. Recruiting — value function, avatar's 1–5 offer band, NPC assignment order, outside-recruit pool, and 0/1/5 fixture

1. **Recruiting value function (`PipelineRules.RecruitingValue`):**
   - For every simulated HS senior (`age >= 19` on days 274/318) and every generated outside recruit (`age == 19`, `PA == 0 / Outs == 0`):
     - **Performance $P$:** `PromotionScore.BatterPerformance(pa, ab, h, bb, tb, leagueOps, PromotionProfile.HsBatterPaShrink)` (`35` PA shrink) for batters, or `PromotionScore.PitcherPerformance(outs, er, leagueEra, PromotionProfile.HsPitcherIpShrink)` (`7.0` IP shrink) for pitchers, using the completed HS season's `leagueOps` / `leagueEra`. When `pa == 0` / `outs == 0` (including all outside recruits and gameless seasons), $P = \mathbf{100.0}$.
     - **Scouting $S$:** `PromotionScore.Scouting(PipelineRules.RoleRatingSum(in row), row.Age, headroom)`, where `headroom = PromotionScore.BatterHeadroom(in row, in pot)` or `PromotionScore.PitcherHeadroom(in row, in pot)` and `RoleRatingSum` includes the `+20` Reliever stamina compensation (Amendment 1).
     - **Combined value $V$:**
       $$V = \text{PromotionScore.Combine}(P, S) - \begin{cases} \text{PipelineProfile.RecordArrestPenalty} & \text{if } \text{isAvatar} \land \text{avatarHasRecordArrest} \\ 0.0 & \text{otherwise} \end{cases}$$
2. **Program band yielding the avatar's 1–5 offers (`PipelineProfile` constants — Amendment 4):**
   - Rank the 24 College programs by prior-season `Team_Seasons.national_rank` ascending $r \in \{1..24\}$ for `seasonYear` (if `Team_Seasons` has no College rows for `seasonYear` yet, fall back to `team_id` ascending `201..224` so $r = \text{teamId} - 200$).
   - Define the minimum recruiting value floor $F(r)$ for national rank $r \in \{1..24\}$:
     $$F(r) = \text{OfferTopProgramFloor} - \text{OfferRankSlope} \cdot (r - 1)$$
     with **`OfferTopProgramFloor = 111.5`** and **`OfferRankSlope = 1.0`** (`MaxAvatarOffers = 5`). Thus:
     - Rank $r = 1$ (national champion / `#1`): $F(1) = \mathbf{111.5}$
     - Rank $r = 12$ (mid-pack): $F(12) = \mathbf{100.5}$
     - Rank $r = 20$ (5-offer threshold): $F(20) = \mathbf{92.5}$
     - Rank $r = 23$ (2-offer threshold): $F(23) = \mathbf{89.5}$
     - Rank $r = 24$ (1-offer threshold): $F(24) = \mathbf{88.5}$
   - **Selection rule (`PipelineRules.SelectAvatarOffers`):**
     1. Compute each College program's projected open spots in `avatarRole`:
        $$\text{OpenSpots}(c, \text{role}) = \max\bigl(0, \text{RoleCap}(\text{role}) - \text{ReturningCount}(c, \text{role})\bigr)$$
        where `ReturningCount(c, role)` counts current players on team $c$ in `role` with `class_year < 4`, `declared_season IS NULL` (not drafted in July), `age < 41`, and `health_ceiling > 40`.
     2. Collect all programs $c$ with `OpenSpots(c, avatarRole) > 0` and $V_{\text{avatar}} \ge F(\text{rank}(c))$, ordered by `(rank(c) ↑, teamId ↑)`, and take the first **`Math.Min(5, qualifying.Count)`** programs as merit offers (`Recruiting_Offers` rows with `status = 0`, `IsWalkOn = false`). Because $F(r)$ is decreasing in $r$, an avatar with $V \in [88.5, 92.5)$ receives `1..4` merit offers from the lowest-ranked programs `[r_min..24]`, while an avatar with $V \ge 92.5$ receives `5` merit offers from the **5 highest-ranked qualifying programs** (so an elite $V \ge 111.5$ prospect gets offers from ranks `#1..#5`, an average $V = 100.0$ prospect gets offers from ranks `#13..#17`, and a below-average $V = 90.0$ prospect gets offers from ranks `#23..#24`).
     3. **D4a-A walk-on fallback (`0` merit offers):** If `qualifying.Count == 0` ($V_{\text{avatar}} < 88.5$ or no program with $F(r) \le V_{\text{avatar}}$ has an open spot in `avatarRole`), set `IsWalkOn = true` and issue **1 walk-on offer (`status = 0`) from the lowest-ranked College program (highest $r$, tiebreak highest `teamId`) that has `OpenSpots(c, avatarRole) > 0`** (consuming 1 open spot so no incumbent needs to be cut). Only in the degenerate case where all 24 College programs have `OpenSpots(c, avatarRole) == 0` does the walk-on offer go to rank-24 (highest `teamId = 224`) and displace that team's lowest-valued returning non-avatar player of `avatarRole` at rollover (`pipeline_retired`).
3. **NPC assignment order and position need (Signing Day, day 318):**
   - **Step 1 (Avatar resolution):** If the avatar is an HS senior with open `Recruiting_Offers(status = 0)` for `seasonYear` and has not yet called `ResolveRecruiting`, auto-accept (`status = 1`) his highest-ranked open offer (lowest $r$, tiebreak lowest `teamId`) and mark his remaining open offers `status = 3` (expired). Decrement the chosen College team's `OpenSpots[avatarRole]` by `1`.
   - **Step 2 (Pool construction):** For each role $r \in \{\text{None}, \text{Starter}, \text{Reliever}\}$, merge all remaining non-avatar simulated HS seniors (`age >= 19`) of role $r$ with the generated outside-recruit candidates of role $r$, sorted by `(RecruitingValue ↓, PlayerId ↑)`.
   - **Step 3 (Rank-ordered rounds by role need + position-coverage tiebreak):**
     - Loop in rounds while any College team has $\sum_r \text{OpenSpots}[c, r] > 0$:
     - Within each round, iterate College teams $c$ in order of `(nationalRank ↑, teamId ↑)` ($r = 1..24$). If team $c$ has no open spots remaining, skip it.
     - Team $c$ chooses its most-needed role $r^* \in \{\text{None}, \text{Starter}, \text{Reliever}\}$ with $\text{OpenSpots}[c, r^*] > 0$ maximizing the fractional deficit $\text{OpenSpots}[c, r] / \text{RoleCap}(r)$ (tiebreak largest integer $\text{OpenSpots}[c, r]$, then `None → Starter → Reliever`).
     - **Position-coverage tiebreak (when $r^* == \text{PitcherRole.None}$):** Let `coveredPositions` be the bitmask of primary field positions `1..8` (`C..RF`) covered by team $c$'s returning position players plus recruits already committed to team $c$. If any field position $p \in \{1..8\}$ is uncovered in `coveredPositions`, find the highest-valued available position recruit with `DefensivePosition == p`; if his `RecruitingValue` is within **`RecruitPositionNeedMargin = 4.0`** Combine points of the overall highest-valued available position recruit, sign him; otherwise sign the overall highest-valued available recruit of role $r^*$.
     - Persist the signed recruit's `Recruiting_Offers` row (`status = 1`, `offered_day = 318`, `resolved_day = 318`) and decrement `OpenSpots[c, r^*]` by `1`.
4. **Outside-recruit pool (size, mean offset, maturity discount age, ages, persist-on-sign — Amendment 2):**
   - **Count rolled in memory per role $r$:**
     $$N_{\text{out}}(r) = \max\bigl(0, \text{TotalOpenSpots}(r) - N_{\text{HS,non-avatar}}(r)\bigr) + \text{OutsideRecruitSurplusFor}(r)$$
     with **`OutsideRecruitSurplusBatters = 15`**, **`OutsideRecruitSurplusStarters = 5`**, **`OutsideRecruitSurplusRelievers = 5`** (`+25` total surplus beyond the league deficit). Across 20 seasons, this rolls `~161.8` outside candidates/year (`99.4 Pos, 27.3 SP, 35.1 RP`), signs `~136.8/yr`, and leaves the bottom **`~4.9/yr` (`8.7%`)** of simulated HS seniors unrecruited (`pipeline_retired` at rollover) as required by spec §9.1 (`"HS seniors not offered by any of the 24 programs retire from baseball"`).
   - **Age:** Generated at **`age = 19`** on Signing Day (day 318) so that at the day-1 rollover `AgeAllPlayers()` increments them to **`age = 20`** (`class_year = 1` Freshman), identical to graduating HS seniors.
   - **Mean potential offset (`OutsideRecruitMeanOffset = -2`):** Pass `meanOffset = -2` (`PotentialMean + OutsideRecruitMeanOffset = 48` vs `50`) to a `LeagueGenerator.RollOutsideRecruit` helper (or `RollPotential` with `PotentialMean - 2`), matching spec §9.1 (`"same ratings distribution as HS intake (slightly lower mean so simulated HS seniors are preferred when comparable)"`).
   - **Maturity discount age (`OutsideRecruitDiscountAge = 25` — critical gradient invariance discovery):**
     - A simulated HS senior entering College at age 20 has already received **4 annual `DevelopmentManager` passes** (`ages 16→17, 17→18, 18→19, 19→20`). Because each pass closes a fraction $\text{Rate}(\text{age}) = \text{YouthWeight}(\text{age}) \times 0.5$ of the remaining `(potential - current)` gap ([DevelopmentManager.cs:225–281](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/DevelopmentManager.cs#L225-L281)), after 4 HS passes the fraction of the initial age-16 prospect discount still unclosed is:
       $$\frac{8}{12} \times \left(1 - \frac{5}{12}\right)\left(1 - \frac{4.5}{12}\right)\left(1 - \frac{4}{12}\right)\left(1 - \frac{3.5}{12}\right) = 0.6667 \times 0.5833 \times 0.6250 \times 0.6667 \times 0.7083 = \mathbf{0.1148}$$
       (or relative to a fresh roll's expected `ProspectDiscount`, an effective `YouthWeight` of **`0.1578`**).
     - Because outside recruits are generated on day 318 with `team_id = NULL` and are not on an HS roster during `DevelopmentManager`'s rollover pass (which develops only rostered players, [DevelopmentManager.cs:376](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/DevelopmentManager.cs#L376)), calling `DevelopmentCurve.RawCurrent(potential, age: 19, ref rng)` (`YouthWeight(19) = 8/12 = 0.6667`) would give outside recruits a **`20.0`-point role-sum prospect discount** (`6.67` per rating) instead of a 4-year HS graduate's **`4.7`-point** discount (`1.58` per rating). Since outside recruits make up `73%` (`136.8 / 188.3`) of each College freshman class, an unadjusted `age: 19` discount drags the steady-state College batter mean down to **`132.8 < HS 134.3` (inverting the HS < College talent gradient!)**.
     - Passing **`OutsideRecruitDiscountAge = 25`** into `DevelopmentCurve.RawCurrent(potential, OutsideRecruitDiscountAge, ref discountRng)` gives $\text{YouthWeight}(25) = \frac{27 - 25}{12} = \frac{2}{12} = \mathbf{0.1667} \approx 0.1578$, matching the exact maturity of a 4-season simulated HS senior while keeping `Players.age = 19` (`20` after rollover) and `OutsideRecruitMeanOffset = -2`. With `OutsideRecruitDiscountAge = 25`, the steady-state batter gradient is **`HS 134.3 < College 142.1 < Pro 160.2`** (`+7.8` and `+18.1` role-sum points) and the pitcher gradient is **`HS 125.1 < College 130.9 < Pro 147.8`** (`+5.8` and `+16.9`).
   - **Persist only signed outside recruits:** Roll all $N_{\text{out}}(r)$ outside candidates in memory as value structs from the `(seasonYear, OutsideRecruitsTag)` and `(seasonYear, OutsideRecruitsFieldingTag)` streams, score them alongside the simulated HS seniors, and allocate `PlayerId`s (`_players.NextGeneratedPlayerId("gen_player_")`) + write `Players` (`team_id = NULL`), `Player_Ratings`, `Player_Potential`, `Player_Fielding`, `Pitcher_Roles`, `Pitch_Arsenals`, and `Recruiting_Offers(status = 1)` rows **only for the outside recruits who win a College spot**. Unselected surplus outside candidates are discarded in memory, preventing ~25 orphan unrostered rows/year from accumulating in `Players`.
5. **Concrete `0 / 1 / 5` Offers Fixture (for `pipeline-score` and `pipeline-avatar`):**
   - **Setup:** HS senior batter avatar (`age = 19`, `PitcherRole.None`), `headroom = 0` (`potential == current` on all 5 tools), `PA = 0` ($P = 100.0$), clean record (`record_arrest` inactive), all 24 College programs (`201..224`, national ranks `1..24`) have `OpenSpots[Batter] > 0`.
   - **Closed-form value:** With `age = 19`, `headroom = 0` ($\implies \text{ProjectionBonus}(19, 0) = 0$), and $P = 100.0$:
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
     - **Avatar:** if `class_year == 4`, auto-declares (`declared_season = seasonYear`, `PendingChoice = PipelinePendingChoice.None`). If `class_year` is `2` or `3` and the avatar did not call `ResolveDeclaration(true)` during the day `176..183` window, the deadline closes with `declared_season = NULL` (stays in College) and `PendingChoice = PipelinePendingChoice.None`.
     - **NPCs:** declare (`declared_season = seasonYear`) iff:
       $$\text{classYear} \ge 2 \;\land\; \bigl(\text{classYear} \ge 3 \;\lor\; \text{age} \ge 22 \;\lor\; (1 \le \text{projectedSlot} \le 64)\bigr)$$
   - **Sanity and expected declarers per season:** In steady state (`seasons 6–20`), this rule produces **`253.9` declarers/season** (`157.5 Pos, 43.2 SP, 53.2 RP`—roughly `2.0x` the `128` draft picks in every role), composed of `~188.3` Seniors (`class_year == 4`), `~55–65` Juniors (`class_year == 3`, `age == 22`), and `~5–10` high-scoring Sophomores (`class_year == 2`, `projectedSlot <= 64`). On day 195, after the 128 picks are made, `~65.5` undrafted Sophomores/Juniors (`class_year < 4`) have `declared_season` cleared back to `NULL` so they remain eligible to play and return to College at rollover, while `~60.3` undrafted Seniors (`class_year == 4`) play out any remaining College postseason and enter `pipeline_free_agent` at rollover.
3. **Draft role-need tiebreak (`PipelineRules.SelectDraftPick` — Amendment 5):**
   - For each Pro team $t \in \{301..332\}$, initialize `teamRoleCount[t, r]` before Round 1 to the count of current players on team $t$ in role $r$ who will not age/health-retire at rollover (`age < 41 && health_ceiling > 40`, since `AgeAllPlayers` adds `+1` at rollover before checking `age >= 42`), and initialize `draftedInRole[t, r] = 0`.
   - At each of the 128 draft slots (`round = 1..4`, `pickInRound = 1..32` for team $t$):
     1. Let `bestOverall` be the highest-ranked available declared player on the draft board ($V_{\text{best}} = \text{bestOverall.Value}$).
     2. **Immediate roster-cap deficit check:** If any role $r$ has $\text{Deficit}(t, r) = \text{RoleCap}(r) - \text{teamRoleCount}[t, r] > 0$, look for available declared players in roles with $\text{Deficit}(t, r) > 0$ whose value satisfies $V \ge V_{\text{best}} - \text{DraftRoleNeedMargin}$ (**`DraftRoleNeedMargin = 3.0`** Combine points). If at least one exists, pick the candidate maximizing `(Deficit(t, r) ↓, Value ↓, PlayerId ↑)`.
     3. **Balanced 4-pick role-quota check (`DraftRoleQuota`: `Pos = 3, SP = 1, RP = 1`):** Otherwise, if `draftedInRole[t, bestOverall.Role] >= DraftRoleQuota(bestOverall.Role)` (e.g., team $t$ has already drafted 1 SP or 1 RP or 3 Pos in earlier rounds), look for available declared players in roles $r$ with `draftedInRole[t, r] < DraftRoleQuota(r)` whose value satisfies $V \ge V_{\text{best}} - \text{DraftRoleNeedMargin}$ (`3.0`). If at least one exists, pick the candidate maximizing `(Value ↓, PlayerId ↑)`.
     4. **Default best-player-available:** Otherwise pick `bestOverall`.
     5. Increment `teamRoleCount[t, pick.Role]++` and `draftedInRole[t, pick.Role]++`.
4. **Draft-order tie keys and inaugural seeded order key (`PipelineRules.OrderProDraftTeams`):**
   - **After Season 1 (`Team_Seasons` has 32 Pro rows for `seasonYear - 1`):**
     - Partition the 32 Pro teams into 6 postseason elimination buckets from `Team_Seasons` (`seed`) and `Playoff_Series` (`season_year = seasonYear - 1, tier = 2`):
       - `bucket = 0`: non-playoff teams (`seed IS NULL`, 20 teams → slots `1..20`)
       - `bucket = 1`: eliminated in Wild Card (`round = 1` loser, 4 teams → slots `21..24`)
       - `bucket = 2`: eliminated in Division Series (`round = 2` loser, 4 teams → slots `25..28`)
       - `bucket = 3`: eliminated in Conference Finals (`round = 3` loser, 2 teams → slots `29..30`)
       - `bucket = 4`: World Championship runner-up (`round = 4` loser, 1 team → slot `31`)
       - `bucket = 5`: World Champion (`round = 4` winner, 1 team → slot `32`)
     - Within each bucket `0..3`, rank the teams in the **exact reverse of `Standings.Order(table, allProTeamIds, scheduleSeed, seasonYear - 1, LeagueTier.Pro)`** ([Standings.cs:54–62](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/Standings.cs#L54-L62)): worst regular-season standing picks first. Specifically, if `Standings.Order` assigns 0-based league standing index `s ∈ {0..31}` (`0` = best record in Pro, `31` = worst record in Pro, breaking win% ties by H2H → division win% → RD → RA → `PostseasonStreams.StandingsCoinKey(scheduleSeed, seasonYear - 1, LeagueTier.Pro, 0, teamId)` → `teamId` ascending), then within each elimination bucket teams sort by **`s` descending** (highest `s` = worst regular-season record picks earliest).
     - *Table-only fallback (when `Schedule_Games` has no rows for `seasonYear - 1`, e.g., unit fixtures that seed `Team_Seasons` directly):* within each bucket, sort by `(WinPct ↑, RunDifferential ↑, RunsAllowed ↓, PostseasonStreams.StandingsCoinKey(scheduleSeed, seasonYear - 1, LeagueTier.Pro, 0, teamId) ↓, teamId ↓)`.
   - **Inaugural draft (`Team_Seasons` has no Pro rows for `seasonYear - 1`, e.g., Season 1 `2026` on day 195, or `PipelineHarness` offseason-only mode):**
     - Order the 32 Pro teams `301..332` by `(InauguralDraftKey(pipelineSeed, seasonYear, teamId) ↑, teamId ↑)`, where:
       $$\text{InauguralDraftKey}(\text{pipelineSeed}, \text{seasonYear}, \text{teamId}) = \text{PostseasonStreams.MixCombine}\Bigl(\text{PostseasonStreams.MixCombine}\bigl(\text{pipelineSeed}, \texttt{0x4452\_4146\_544F\_5244UL}\bigr), \;\text{PostseasonStreams.MixCombine}\bigl((\text{ulong})\text{seasonYear}, (\text{ulong})\text{teamId}\bigr)\Bigr)$$
       using domain tag **`InauguralDraftOrderTag = 0x4452_4146_544F_5244UL`** (`"DRAFTORD"`).
5. **Avatar signing-bonus scale (`PipelineRules.SigningBonusForPick` — Amendment 5):**
   - **Sizing against the economy (`items.json` and temptation loop):** In [items.json](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Items/items.json#L1-L114), Tier-1 HS/College gear costs `\$85–\$400`, Tier-2 gear (`composite_bat`, `custom_cleats`, `CleanLiving`/`PartyLifestyle` upkeep) costs `\$800–\$1,800`, and Tier-3 Pro luxury items cost `\$4,200` (`pro_stock_glove`), `\$9,500` (`Designer Wardrobe`), `\$28,000` (`iced_chronograph`), and `\$85,000` (`muscle_car`, which should require Pro salary + bonus or side hustles rather than being cash-bought in full by a 2nd-round pick).
   - Define `PipelineRules.SigningBonusForPick(int overallPick)` (`0` when `overallPick < 1 || overallPick > 128`) as the exact piecewise-linear integer schedule across the 4 rounds:
     $$\text{SigningBonusForPick}(p) = \begin{cases} 50\,000 - 900 \cdot (p - 1) & 1 \le p \le 32 \quad (\text{Round 1: } \texttt{\$50,000} \to \texttt{\$22,100}) \\ 20\,000 - 250 \cdot (p - 33) & 33 \le p \le 64 \quad (\text{Round 2: } \texttt{\$20,000} \to \texttt{\$12,250}) \\ 11\,500 - 175 \cdot (p - 65) & 65 \le p \le 96 \quad (\text{Round 3: } \texttt{\$11,500} \to \texttt{\$6,075}) \\ 5\,800 - 120 \cdot (p - 97) & 97 \le p \le 128 \quad (\text{Round 4: } \texttt{\$5,800} \to \texttt{\$2,080}) \\ 0 & \text{otherwise} \end{cases}$$
   - Named `PipelineProfile` anchor constants:
     - `Round1TopBonus = 50_000`, `Round1PickSlope = 900`
     - `Round2TopBonus = 20_000`, `Round2PickSlope = 250`
     - `Round3TopBonus = 11_500`, `Round3PickSlope = 175`
     - `Round4TopBonus = 5_800`, `Round4PickSlope = 120`
   - **Mandatory `FundsImpulseEvent` publication (Amendment 5):** When the avatar is drafted (`bonus > 0`), `_players.AdjustFunds(avatarId, bonus)` writes `Players.funds` inside the Draft Day transaction, **and after `tx.Commit()` `PipelineManager` must publish `_bus.Publish(new FundsImpulseEvent(avatarId, bonus))`** (matching [EquipmentService.cs:127](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Life/EquipmentService.cs#L127)) so `LifeSimManager`'s in-memory `_funds` mirror reflects the signing bonus immediately on day 195.

---

#### Q6. Pro turnover — release order, free-agent signing order, and free-agent retirement vs equilibrium

1. **Pro release order at rollover (`PipelineRules.SelectProReleases`):**
   - At rollover, after age/health retirements (`age >= 42 || health_ceiling <= 40` after `AgeAllPlayers()`) are removed from Pro rosters (`SetTeam(null)`, `SetFlag("pipeline_retired", true)`) and after the 128 drafted players (plus any undrafted class-4 senior avatar's D4b-A walk-on contract) are placed on their Pro `team_id` (`301..332`), each Pro team trims each role $r \in \{\text{None}, \text{Starter}, \text{Reliever}\}$ where `count(t, r) > RoleCap(r)` (`15 / 4 / 5`).
   - Score every non-avatar player on team $t$ in role $r$ by **`ProValue = PromotionScore.Combine(P_Pro, S)`** (`BatterPaShrink = 200`, `PitcherIpShrink = 40.0` against the completed Pro season's `leagueOps`/`leagueEra`, `RoleRatingSum(row)`, `age`, `headroom`). The avatar is exempt from rollover release (D4b-A).
   - Sort non-avatar players in role $r$ on team $t$ by `(ProValue ↓, PlayerId ↑)`, keep the top `RoleCap(r) - (avatarInRole ? 1 : 0)`, and release the remainder (`SetTeam(null)`, `SetFlag(id, "pipeline_free_agent", true, currentDay)`). Equivalently, **lowest `ProValue` is released first, with exact score ties broken by `PlayerId` ordinal descending** (so lower ordinal `PlayerId` retains the roster spot).
   - **Is `ProjectionBonus(age, headroom)` right for Pro releases?** Yes:
     - On a newly drafted 22/23-year-old rookie with `headroom ≈ 4–10`, `ProjectionBonus(22, headroom)` gives `+2.0 to +5.0` scouting rating points (and since the rookie played in College rather than Pro, his Pro `PA == 0 / Outs == 0` gives neutral $P_{\text{Pro}} = 100.0$), protecting incoming draft picks from being immediately cut before playing a Pro season.
     - On aging veterans (`age >= 30`), `ProjectionBonus` decays linearly at `-1.0` scouting point per year past 29 (`-4.0` at age 33, `-8.0` at age 37, `-10.0` at age 39+), ensuring that declining mid-30s veterans with mediocre stats are cut in favor of prime players and incoming draftees rather than clogging rosters until age 42.
2. **Free-agent signing order before Pro Sunday locks (`PipelineManager.FillProOpeningsBeforeLock`):**
   - Invoked by `RosterManager.Lock(int day)` before computing Pro designations on every Sunday lock (`days 91..273`).
   - **Fast path:** Query Pro team role counts. If every Pro team `301..332` has `15 Pos, 4 SP, 5 RP` (`24` players total), return `0` immediately (`0 B`, `0` FA queries, `0` sim re-inits).
   - **Slow path (when at least one Pro team has `orgCount < 24`):**
     - Load signable free agents via `LoadPipelineFreeAgents()` (`team_id IS NULL ∧ pipeline_free_agent active ∧ age < 42 ∧ health_ceiling > 40`, excluding `pipeline_retired` active), and score each by **`FreeAgentValue = PromotionScore.Combine(100.0, PromotionScore.Scouting(RoleRatingSum(row), row.Age, headroom))`** (using `Player_Ratings` + `Player_Potential` already in memory, with neutral $P = 100.0$ so no multi-season stat scan is needed at a Sunday lock).
     - Partition available free agents by role $r \in \{\text{None}, \text{Starter}, \text{Reliever}\}$, each sorted by `(FreeAgentValue ↓, PlayerId ↑)`.
     - Loop while any Pro team $t$ has an open spot in a role $r$ whose available FA list is non-empty:
       1. Select the Pro team $t^*$ with at least one fillable role deficit that maximizes **`(totalOpenSpots(t), -t)`** (most short-handed team signs first; ties broken by `team_id` ascending `301..332`).
       2. On team $t^*$, select the fillable role $r^*$ with `availableFA[r].Count > 0` that maximizes **`(RoleCap(r) - count(t^*, r), RolePriority(r))`** where `RolePriority` breaks ties by `None → Starter → Reliever`.
       3. Pop the top free agent $p^*$ from `availableFA[r^*]`, assign `SetTeam(p^*, t^*)`, clear `SetFlag(p^*, "pipeline_free_agent", false, day)`, and if `p^*` did not already have `pipeline_pro_signed` active, set `SetFlag(p^*, "pipeline_pro_signed", true, day)` and queue `new ProContractSignedEvent(p^*, t^*, day, wasDrafted: false)`.
     - Commit the single transaction, publish any queued `ProContractSignedEvent`s and `RosterBeatWriter` texts, and re-initialize the Pro `LeagueSimulator` and `MicroGame` once (plus `Career?.ReactivateAvatar()` if the avatar signed) before `RosterManager.Lock` writes `Roster_Designations`.
3. **Free-agent retirement rule (`age >= 30 || 2 unsigned seasons`) vs equilibrium & undrafted-senior role supply (Amendment 6):**
   - **Exact unsigned-seasons day arithmetic (Amendment 6):** At rollover, `CareerManager.OnSeasonRolledOver` updates `_state.CurrentDay` to day 1 of `newSeasonYear = completedSeasonYear + 1` ([CareerManager.cs:402](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L402)) before publishing `SeasonRolledOverEvent`. Therefore, any player released to `pipeline_free_agent` at rollover (or mid-season) has `set_on_day` inside the season year `setSeasonYear = TierSeasonCalendar.SeasonYearForDay(setOnDay)` (`2026 + (Math.Max(1, setOnDay) - 1) / 365`). At the start of `newSeasonYear`, the number of full seasons the player has spent unsigned in the FA pool is:
     $$\text{unsignedSeasons} = \text{newSeasonYear} - \text{TierSeasonCalendar.SeasonYearForDay}(\text{setOnDay})$$
     - Example: Player $X$ is released at the `2026 → 2027` rollover (`currentDay = 366`, day 1 of `2027`, so `setSeasonYear = 2027`).
     - During `2027` (days `366..730`), $X$ goes unsigned. At the `2027 → 2028` rollover (`currentDay = 731`, `newSeasonYear = 2028`), `unsignedSeasons = 2028 - 2027 = 1 < 2` (spent 1 season unsigned; stays in FA if `age < 30 && health_ceiling > 40`).
     - During `2028` (days `731..1095`), $X$ goes unsigned again. At the `2028 → 2029` rollover (`currentDay = 1096`, `newSeasonYear = 2029`), `unsignedSeasons = 2029 - 2027 = 2 >= 2` (spent 2 full seasons unsigned $\implies$ retires: `pipeline_free_agent = 0`, `pipeline_retired = 1`).
     - (Note: evaluating `(completedSeasonYear - setSeasonYear) >= 2` would require 3 unsigned seasons instead of 2 because `setSeasonYear` is stamped on day 1 of `newSeasonYear`.)
   - **Does the undrafted-senior pool keep every role supplied?** Yes. Every rollover, `~60.3` undrafted College seniors (`36.4 Pos, 9.3 SP, 14.6 RP`) graduate at age `24` (`< 30`), entering `pipeline_free_agent` alongside `~127.0` Pro cuts (`81.9 Pos, 22.1 SP, 23.1 RP`, of whom `~78` are under age 30). Because all undrafted seniors are age `24`, they survive the `age >= 30` check at the rollover where they enter FA and remain signable for two full Pro seasons (`ages 24 and 25`), maintaining a steady day-91 FA pool of **`~325.7` signable players (`205.8 Pos, 55.0 SP, 64.9 RP`)**.

---

#### Q7. The gradient gate — metric, burn-in, window, stationarity, seeds, margins, and neutral-$P$ bias

1. **Metric (matches [development_decline_curves.md](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/docs/reference/development_decline_curves.md#L739-L765) §7):**
   - After each rollover $k \in \{0..19\}$ (20 rollovers total), snapshot the tier-mean **raw role-summed current ratings** across all rostered players in tier $t \in \{\text{HS} = 0, \text{College} = 1, \text{Pro} = 2\}$, separately for:
     - **Batters (`Role == PitcherRole.None`):** $\mu_{\text{Bat}}(t, k) = \text{mean}_{p \in \text{Bat}(t, k)}(\text{BatPower}_p + \text{BatContact}_p + \text{BatDiscipline}_p)$
     - **Pitchers (`Role != PitcherRole.None`):** $\mu_{\text{Pit}}(t, k) = \text{mean}_{p \in \text{Pit}(t, k)}(\text{PitStuff}_p + \text{PitControl}_p + \text{PitStamina}_p)$
     (Note: the gradient metric itself uses the **raw** three-tool sum `PitStuff + PitControl + PitStamina`, exact same formula as 9d §7; the `+20` RP stamina compensation is used only inside `PipelineRules.RoleRatingSum` when computing `PromotionScore.Scouting`.)
2. **Burn-in and measurement window over 20 seasons:**
   - **Total seasons:** `TotalSeasons = 20` (rollover indices `k = 0..19`, completing seasons `2026..2045`).
   - **Burn-in:** `BurnInSeasons = 5` (`k = 0..4`, seasons `2026..2030`). As proved in Q3, 100% of world-gen HS players and 100% of world-gen College players depart by the end of Season 4 (`k = 3`), and Season 5 (`k = 4`) settles the first full steady-state draft class into Pro.
   - **Measurement window:** `MeasurementWindowSeasons = 15` (`k = 5..19`, seasons `2031..2045`). Denote the 15-season time-averaged means by $\bar{\mu}_{\text{Bat}}(t) = \frac{1}{15}\sum_{k=5}^{19}\mu_{\text{Bat}}(t, k)$ and $\bar{\mu}_{\text{Pit}}(t) = \frac{1}{15}\sum_{k=5}^{19}\mu_{\text{Pit}}(t, k)$.
3. **Stationarity bound:**
   - Split the 15-season measurement window into its first half $W_1 = \{k = 5..11\}$ (`7` rollovers, seasons `6..12`) and second half $W_2 = \{k = 12..19\}$ (`8` rollovers, seasons `13..20`).
   - Gate: for every tier $t \in \{0, 1, 2\}$,
     $$\bigl|\bar{\mu}_{\text{Bat}}(t, W_2) - \bar{\mu}_{\text{Bat}}(t, W_1)\bigr| < \mathbf{10.0} \quad\text{and}\quad \bigl|\bar{\mu}_{\text{Pit}}(t, W_2) - \bar{\mu}_{\text{Pit}}(t, W_1)\bigr| < \mathbf{10.0}$$
     (`PipelineProfile.GradientStationarityMaxDrift = 10.0`, identical to 9d §7). In our 20-season probes across seeds `20261603`, `20260703`, and `20260704`, observed `|W2 - W1|` drift is **`0.03–2.17` for batters** and **`0.06–3.20` for pitchers** (`< 1/3` of the `10.0` bound).
4. **Seeds and expected margins under offseason-only mode (`PipelineHarness` default):**
   - **Canonical seeds:**
     - `WorldSeed = LeagueGenerator.DefaultLeagueSeed + 900 = 20261603` (matching 9d §7's `LeagueSeed + 900`)
     - `DevelopmentSeed = 0x9D7000UL` (matching 9d §7)
     - `PipelineSeed = PipelineProfile.DefaultPipelineSeed = 0x136D_5EED_0000_0001UL`
   - **Measured 15-season steady-state means (`k = 5..19`) and half-window stationarity across 3 seeds:**

| World Seed | Mode | $\bar{\mu}_{\text{Bat}}(\text{HS})$ | $\bar{\mu}_{\text{Bat}}(\text{Col})$ | $\bar{\mu}_{\text{Bat}}(\text{Pro})$ | Bat Gaps (`Col-HS` / `Pro-Col`) | Bat Max Drift `|W2-W1|` | $\bar{\mu}_{\text{Pit}}(\text{HS})$ | $\bar{\mu}_{\text{Pit}}(\text{Col})$ | $\bar{\mu}_{\text{Pit}}(\text{Pro})$ | Pit Gaps (`Col-HS` / `Pro-Col`) | Pit Max Drift `|W2-W1|` |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **`20261603`** *(default)* | Offseason (`P=100`) | **`135.24`** | **`142.63`** | **`160.20`** | **`+7.39` / `+17.57`** | `1.88` (Pro) | **`125.56`** | **`130.98`** | **`147.95`** | **`+5.42` / `+16.97`** | `3.20` (Pro) |
| **`20260703`** | Offseason (`P=100`) | **`134.28`** | **`142.06`** | **`160.16`** | **`+7.78` / `+18.10`** | `2.17` (Pro) | **`125.13`** | **`130.99`** | **`147.82`** | **`+5.86` / `+16.83`** | `2.38` (Pro) |
| **`20260704`** | Offseason (`P=100`) | **`134.23`** | **`142.04`** | **`160.11`** | **`+7.81` / `+18.07`** | `1.37` (Pro) | **`126.07`** | **`130.30`** | **`146.97`** | **`+4.23` / `+16.67`** | `1.77` (Pro) |
| **`20261603`** | Noisy $P$ ($\sigma_P=10$) | **`135.38`** | **`141.93`** | **`159.73`** | **`+6.55` / `+17.80`** | `1.63` (Pro) | **`125.38`** | **`131.02`** | **`147.50`** | **`+5.64` / `+16.48`** | `2.94` (Pro) |

   - **Recommended gate margins in `PipelineHarness` default:**
     - Strict monotonicity: $\bar{\mu}_{\text{Bat}}(\text{HS}) < \bar{\mu}_{\text{Bat}}(\text{College}) < \bar{\mu}_{\text{Bat}}(\text{Pro})$ and $\bar{\mu}_{\text{Pit}}(\text{HS}) < \bar{\mu}_{\text{Pit}}(\text{College}) < \bar{\mu}_{\text{Pit}}(\text{Pro})$.
     - Minimum separation floors (`PipelineProfile`):
       - `MinBatterHsToCollegeMargin = 3.0` (observed `+7.39 to +7.81`)
       - `MinBatterCollegeToProMargin = 10.0` (observed `+17.57 to +18.10`)
       - `MinPitcherHsToCollegeMargin = 2.0` (observed `+4.23 to +5.86`)
       - `MinPitcherCollegeToProMargin = 10.0` (observed `+16.67 to +16.97`)
5. **Does neutral $P$ (`P = 100.0`, no games) bias the gradient, and does `--full` need its own check?**
   - **Analytical + empirical answer:** In offseason-only mode (`PA == 0 / Outs == 0`), $P \equiv 100.0$, so $\text{Combine}(100.0, S) = 50.0 + 0.5 S$ orders candidates purely by $S$ (current ratings + age/headroom projection). In a full game-simmed season, a player with true talent $T$ produces shrunk performance $P(T) = \mu_P(T) + \varepsilon_P$, where $\partial \mu_P / \partial T > 0$ (higher ratings produce higher OPS / lower ERA) and $\varepsilon_P$ is single-season binomial/Markov sampling noise ($\sigma_{\varepsilon} \approx 8\text{--}12$ points after shrinkage). Because $\mu_P(T)$ is monotone in $T$, adding $P$ reinforces the talent slope while $\varepsilon_P$ slightly softens the truncation selection differential (shifting the correlation between `Combine(P, S)` and raw rating sum from $\rho \approx 0.99$ to $\rho \approx 0.95$). As shown in the table above (`20261603` Offseason vs Noisy $P$), this shifts the 15-season steady-state College and Pro means by **at most `0.5–0.7` role-sum points (`< 4%` of the inter-tier gap)** and never threatens monotonicity or stationarity.
   - **What `--full` should check:** Because `--full` runs 3 full seasons + Season 4's opening week (`1,193` days total, Amendment 7) to keep runtime under `~25 s`, it stops inside the 5-season burn-in window and therefore does not run the 15-season stationarity window; instead, `--full` should assert **single-snapshot strict monotonicity** ($\mu_{\text{Bat}}(\text{HS}) < \mu_{\text{Bat}}(\text{College}) < \mu_{\text{Bat}}(\text{Pro})$ and $\mu_{\text{Pit}}(\text{HS}) < \mu_{\text{Pit}}(\text{College}) < \mu_{\text{Pit}}(\text{Pro})$) after its final rollover, proving that real shrunk game stats $P$ preserve the talent ladder end to end.

---

#### Q8. Stream contract (`pipeline_seed`, derived streams, and reload stability)

1. **Single persisted `pipeline_seed`:**
   - Keep the existing single boot draw `ulong pipelineSeed = rng.NextUInt64() | 1UL;` at [GameManager.cs:588](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L588) (`PromotionManager`'s exact slot, between `DevelopmentManager` at `:575` and `Autonomy` at `:680`), so every upstream and downstream boot RNG draw in `GameManager.BootCareer` is bit-identical.
   - In `PipelineManager.EnsureBootState()`, check `_state.GetString("pipeline_seed")`:
     - If present and parseable via `ulong.TryParse(..., NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong persisted)` with `persisted != 0`: set `_pipelineSeed = persisted | 1UL`.
     - Otherwise (fresh career first boot): set `_pipelineSeed = bootDrawnSeed | 1UL` (fallback `PipelineProfile.DefaultPipelineSeed = 0x136D_5EED_0000_0001UL` when `bootDrawnSeed == 0`) and persist `_state.SetString("pipeline_seed", _pipelineSeed.ToString(CultureInfo.InvariantCulture))`.
2. **Derived streams per `(seasonYear, purpose)` via `PostseasonStreams.MixCombine`:**
   - Define `PipelineStreams` (or static helpers on `PipelineRules`) using `PostseasonStreams.MixCombine(ulong a, ulong b)` ([PostseasonStreams.cs:62–72](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PostseasonStreams.cs#L62-L72)) with 5 disjoint 64-bit ASCII domain tags:

| Purpose / Stream | Call Site & Day | Domain Tag Constant (Hex / ASCII) | Seed / Key Formula |
| :--- | :--- | :--- | :--- |
| **Inaugural Draft Order Key** | Day 195 (when prior-season Pro `Team_Seasons` is empty) | `InauguralDraftOrderTag = 0x4452_4146_544F_5244UL` (`"DRAFTORD"`) | `MixCombine(MixCombine(pipelineSeed, InauguralDraftOrderTag), MixCombine((ulong)seasonYear, (ulong)teamId))` |
| **Outside-Recruit Ratings & Potentials** | Day 318 (`OnSigningDay`) | `OutsideRecruitsTag = 0x5049_5045_4F55_5452UL` (`"PIPEOUTR"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, OutsideRecruitsTag), (ulong)seasonYear) | 1UL)` |
| **Outside-Recruit Fielding Rolls** | Day 318 (`OnSigningDay`) | `OutsideRecruitsFieldingTag = 0x5049_5045_4F52_464CUL` (`"PIPEORFL"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, OutsideRecruitsFieldingTag), (ulong)seasonYear) | 1UL)` |
| **HS Intake Ratings & Potentials** | Day 1 rollover (`OnSeasonRolledOver`, keyed by `completedSeasonYear`) | `HsIntakeTag = 0x5049_5045_4853_494EUL` (`"PIPEHSIN"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, HsIntakeTag), (ulong)completedSeasonYear) | 1UL)` |
| **HS Intake Fielding Rolls** | Day 1 rollover (`OnSeasonRolledOver`, keyed by `completedSeasonYear`) | `HsIntakeFieldingTag = 0x5049_5045_4853_464CUL` (`"PIPEHSFL"`) | `new RngState(MixCombine(MixCombine(pipelineSeed, HsIntakeFieldingTag), (ulong)completedSeasonYear) | 1UL)` |

3. **Zero mutable RNG state & reload stability at any day:**
   - **No other RNG draw is needed anywhere in 13-6:** `EnsureCollegeEnrollment`, day-176 declaration open, day-183 declaration deadline, day-195 draft board & pick selection, day-274 avatar offers, day-318 recruiting assignment, rollover promotions/releases/FA retirements, and Sunday-lock free-agency signings are all 100% deterministic functions of SQLite rows plus the 5 `(pipelineSeed, seasonYear, tag)` derivations above.
   - Because `PipelineManager` holds **no mutable `RngState` field across days** and derives every stream freshly from `(_pipelineSeed, seasonYear, tag)`, saving and reloading on any day of the season (`day 1`, `day 180`, `day 200`, `day 300`, `day 365`) and advancing across the next calendar milestone or rollover produces **bit-identical** outside recruits, draft order, HS intake, and roster assignments (`pipeline-stream` gate).

---

#### Q9. `RecordArrestPenalty` on the pipeline scale, with concrete fixtures

1. **Why 9c's `3.0` is too large on the pipeline scale and why `RecordArrestPenalty = 1.0` is exact (Amendment 6):**
   - In 9c, `PromotionProfile.RecordArrestPenalty = 3.0` was sized against a `5.0`-point `SwapMargin` ($3.0 = 0.6 \times \text{SwapMargin}$).
   - In 13-6:
     - On the **recruiting offer ladder**, adjacent College programs are spaced by `OfferRankSlope = 1.0` Combine point ($F(r) - F(r+1) = 1.0$). A `3.0`-point dock would strip **3 full merit offers** (or shift the avatar's top offer down by 3 national-rank places) at once.
     - On the **College draft board**, `576` players span `~30` Combine points (`~19` players per Combine point among the `~430` draft-eligible upperclassmen, or `~25` players per Combine point in a compressed spread-0 world), so a `3.0`-point dock shifts the avatar by `~55–75` draft slots (**2 full draft rounds**).
     - Setting **`PipelineProfile.RecordArrestPenalty = 1.0`** Combine point (equivalent to `-2.0` scouting points or `-3.0` raw role-rating sum points, i.e., `-1` point on each of the three role tools) shifts the avatar by **exactly `1` program rank step (`1` merit offer at any integer floor boundary)** and **`~15–20` draft slots (`1` projected round at any round boundary)**—flipping only marginal outcomes while never blocking a clear standout.
2. **Concrete fixtures for `pipeline-record`:**
   - **Fixture A (Recruiting offer count flip `1 ↔ 0` and `2 ↔ 1`):**
     - Setup: HS senior batter avatar (`age = 19, headroom = 0, PA = 0 → P = 100.0`), all 24 College programs have `OpenSpots[Batter] > 0`, program floors $F(r) = 111.5 - (r - 1)$ ($F(24) = 88.5, F(23) = 89.5$).
     - Sub-case A1 (`1 merit offer ↔ 0 merit offers / walk-on`): Avatar ratings `39 / 39 / 38` ($\text{roleSum} = 116 \implies S = 77.3333 \implies V_{\text{clean}} = \mathbf{88.6667}$).
       - With `record_arrest` inactive: $V = 88.6667 \in [88.5, 89.5) \implies \mathbf{1\text{ merit offer}}$ (`OfferCount = 1, IsWalkOn = false`, from rank-24 team `224`).
       - With `record_arrest` active: $V = 88.6667 - 1.0 = \mathbf{87.6667} < 88.5 \implies \mathbf{0\text{ merit offers}}$ (`OfferCount = 1, IsWalkOn = true` walk-on fallback).
       - With `record_arrest` deactivated (`SetFlag(..., false)`): $V = 88.6667 \implies$ flips back to `1` merit offer (`IsWalkOn = false`).
     - Sub-case A2 (`2 merit offers ↔ 1 merit offer`, pure merit count change): Avatar ratings `40 / 40 / 41` ($\text{roleSum} = 121 \implies S = 80.6667 \implies V_{\text{clean}} = \mathbf{90.3333} \in [F(23) = 89.5, F(22) = 90.5) \implies \mathbf{2\text{ merit offers}}$ from teams `223, 224`). With `record_arrest` active: $V = \mathbf{89.3333} \in [88.5, 89.5) \implies \mathbf{1\text{ merit offer}}$ (team `224` only).
   - **Fixture B (Projected draft round flip `Round 2 ↔ Round 3` and `slot <= 64` threshold flip):**
     - Setup: Spread-0 College world (`ratingSpread = 0`, all NPCs at `50/50/50`, `headroom = 0`, `PA = 0 → P = 100.0`, $V = 100.00$) where:
       - `63` NPC Juniors (`class_year = 3, age = 21, headroom = 0`) are set to `52 / 52 / 52` ($\text{roleSum} = 156 \implies V = 102.00$, occupying slots `1..63`);
       - `1` NPC Junior (`npc_64`) is set to `50 / 50 / 51` ($\text{roleSum} = 151 \implies V = 100.3333$);
       - All other draft-eligible NPCs (`>= 350` players) remain at `50 / 50 / 50` ($\text{roleSum} = 150 \implies V = 100.00$).
     - Avatar: College Sophomore batter (`class_year = 2, age = 21, headroom = 0, PA = 0`) with ratings `51 / 51 / 50` ($\text{roleSum} = 152 \implies S = 101.3333 \implies V_{\text{clean}} = \mathbf{100.6667}$).
       - With `record_arrest` inactive: $V = 100.6667 > 100.3333 \implies \text{projectedSlot} = \mathbf{64}$ (**`ProjectedRound = 2`**, inside the top-64 early-entry band).
       - With `record_arrest` active: $V = 100.6667 - 1.0 = \mathbf{99.6667} < 100.00 \implies \text{projectedSlot} > 96$ (or with 31 NPCs at `50/50/50` and the rest at `49/49/49`: $\text{projectedSlot} = \mathbf{96}$, **`ProjectedRound = 3`**).
       - Deactivating `record_arrest` restores `projectedSlot = 64` (**`ProjectedRound = 2`**).
   - **Fixture C (`banned_for_life` invariance — D6-A):**
     - Setting `banned_for_life` active on the avatar in both Fixture A and Fixture B changes `RecruitingValue`, `DraftValue`, `OfferCount`, `IsWalkOn`, `projectedSlot`, and `ProjectedRound` by **`0.0` / `0`**.

---

#### Q10. Runtime forecasts for MC default (`≤ +10 s`), `PipelineHarness` default (`≤ 60 s`), and `--full` (`≤ 4 min`)

1. **MC default net wall-clock delta forecast (`+1.2 s to +2.8 s ≤ +10.0 s`):**
   - In the `9277af5` baseline, the 7 red promotion suites do **not** fail in 0 ms—before throwing `NotSupportedException` at their first `PromotionManager.RunSeasonRollover` or `CheckPromotionScoreFormulas` call, they currently construct **11 scratch SQLite worlds** (`GenerateWorld` at `~0.35 s` each = `~3.85 s`) and, in `promotion-avatar`, play **1 full 3-tier season (`365` days = `~1.90 s`)**, spending **`~5.75 s`** of wall time inside the `9277af5` baseline!
   - Per-suite replacement breakdown on the owner's PC:

| 9c Suite (`9277af5` Baseline Cost) | 13-6 Replacement in MC Default | Forecast Cost | Net Delta vs `9277af5` |
| :--- | :--- | :---: | :---: |
| `promotion-score` (`~0.54 s`, 1 world + throws) | `pipeline-score`: **pure in-memory math** (0 DB worlds) testing `PromotionScore`, `RoleRatingSum` (+20 RP), `SelectAvatarOffers` (0/1/5 fixture), `ShouldNpcDeclare`, `SelectDraftPick`, `SigningBonusForPick` | `0.02 s` | **`-0.52 s`** |
| `promotion-gate` (`~1.05 s`, 3 worlds + throws) | `pipeline-stream`: 1 scratch world + 1 reload twin testing `pipeline_seed` persistence, the 5 `MixCombine` tags, mid-season reload stability (`day 200 → 366`), and `15/4/5` rollover conservation | `0.95 s` | **`-0.10 s`** |
| `promotion-avatar` (`~2.25 s`, 1 world + 365 played days + throws) | `pipeline-avatar`: 1 shared scratch world testing HS senior offers (274), manual + auto accept (318), walk-on D4a-A, HS→College rollover + texts, Fr non-declaration, So/Jr declaration (176–183), Draft + signing bonus + `FundsImpulseEvent` + `ProContractSignedEvent(true)` (195), College→Pro rollover, undrafted Sr walk-on D4b-A + `ProContractSignedEvent(false)`, Pro release exemption (gameless calendar jumps) | `1.10 s` | **`-1.15 s`** |
| `promotion-record` (`~0.35 s`, 1 world + throws) | `pipeline-record`: reuses `pipeline-avatar` world (or 1 scratch world) testing Fixture A (`1 ↔ 0` and `2 ↔ 1` offers), Fixture B (`Round 2 ↔ Round 3` projected slot), and Fixture C (`banned_for_life` 0 dock) | `0.40 s` | **`+0.05 s`** |
| `promotion-regression` (`~0.35 s`, 1 world + throws) | `pipeline-lock`: 1 scratch world testing Sunday lock fast path (`0 B`, `0` re-inits when at `24`), shortfall free-agency fill (`< 24 → 24`), `ProContractSignedEvent(false)`, and `FindStrongestFreeAgent` succession compatibility | `0.55 s` | **`+0.20 s`** |
| `development-arc` (`~0.65 s`, 2 worlds + throws at rollover 1) | `development-arc`: 2 scratch worlds × 16 **gameless offseasons** (`DevelopmentManager` + `PipelineManager` rollover + calendar days `176/183/195/274/318`, no `LeagueSimulator` games) | `2.15 s` | **`+1.50 s`** |
| `development-equilibrium` (`~0.56 s`, 2 worlds + throws at rollover 1) | **Retired from MC default** (moved to `Tools/PipelineHarness`, D7-A) | `0.00 s` | **`-0.56 s`** |
| `practice-seam` (already passes, §10.9 returns early at `:6183`) | Complete `practice-seam` §10.9 (`days 265..366` across 1 rollover on its existing world, ~100 gameless days + 1 rollover) | `+0.15 s` | **`+0.15 s`** |
| `season-pipeline` (already passes) | Add the **1-season (`2026`, 365-day) Pro-only D1-A twin** + day-366 positive-control rollover check (1 Pro `LeagueSimulator`, no HS/College sims) | `+1.85 s` | **`+1.85 s`** |
| **Total MC Default Net Delta** | **543/543 green** (`544 - 1` retired `development-equilibrium`) | — | **`+1.42 s`** *(range `+1.2 to +2.8 s ≤ +10.0 s`)* |

2. **`Tools/PipelineHarness` default (20 seasons, offseason-only — forecast `~3.8 to 5.8 s ≤ 60 s`):**
   - **Critical wiring rule for offseason-only mode (Amendment 7):** In `PipelineHarness` default mode, do **not** construct `LeagueSimulator`s or `PlayoffManager`, and on day 91 call `PipelineManager.FillProOpeningsBeforeLock(day91)` directly (or pass `SeasonScheduler = null` to `RosterManager`) rather than invoking `RosterManager.EnsureSeasonSchedules`—otherwise `RosterManager.Lock` would generate 1,912 `Schedule_Games` rows per season (`38,240` unused game rows over 20 seasons!).
   - With games omitted, each season in `PipelineHarness` default executes only: day-91 FA fill (`~1 ms`), day-176/183 declaration (`~8 ms`), day-195 Draft (`~12 ms`), day-274/318 Recruiting + ~162 outside recruits (`~35 ms`), and day-1 rollover (`AgeAllPlayers` + `DevelopmentManager` + `PipelineManager` rollover + ~55 HS intake players, `~45 ms`) = **`~100–130 ms` per season (`~2.0–2.6 s` per 20-season world)**.
   - Running the primary 20-season world (`WorldSeed = 20261603`) plus its 20-season determinism replay twin plus world creation (`2 × 0.35 s`) takes **`~4.7–5.9 s` total (`< 10%` of the `60 s` budget)**.
3. **`Tools/PipelineHarness --full` (HS-senior avatar career HS → College → Draft → Pro attended game — forecast `~10 to 25 s ≤ 4 min`, Amendment 7):**
   - **Timeline correction (Amendment 7):** The brief's §5 item 7 says `"3–4 full seasons with all three tiers playing games, walking an avatar from HS senior → College → Draft → Pro first game"`. Let's trace the exact calendar seasons required for an avatar starting Season 1 (`2026`) as an **HS Senior (`age = 19`)**:
     - **Season 1 (`2026`, HS Senior, `age = 19`):** plays HS games (`Fridays, days 96..159`), receives College offers on day 274, signs on day 318, and moves to his College `team_id` (`class_year = 1`, `age = 20`) at **Rollover 1 (`day 366`, start of Season 2 `2027`)**.
     - **Season 2 (`2027`, College Freshman, `class_year = 1, age = 20`):** ineligible for the Draft (`class_year < 2`); plays College games and advances to `class_year = 2, age = 21` at **Rollover 2 (`day 731`, start of Season 3 `2028`)**.
     - **Season 3 (`2028`, College Sophomore, `class_year = 2, age = 21`):** declares for the Draft during days `176..183` (`day 906..913`), is drafted (or finishes as an undrafted senior if seeded at `class_year = 4`) on day 195 (`day 925`), and **moves to his Pro `team_id` (`301..332`) at Rollover 3 (`day 1096`, start of Season 4 `2029`)**!
     - **Season 4 (`2029`, Pro Rookie, `age = 22`):** designated active at the Season-4 opening Sunday lock (`day 91` of Season 4 = `day 1186`) and plays his **first attended Pro game on Opening Tuesday (`day 93` of Season 4 = `day 1188`)**!
   - Thus, starting from an HS Senior avatar (`age = 19` in `2026`), reaching the first attended Pro game requires **3 full seasons (`2026, 2027, 2028` = `1,095` days) plus the first `98` days of Season 4 (`2029`) = `1,193` days (`3.27` seasons)** (or 4 full seasons = `1,460` days).
   - At `~2.5–3.5 s` per full 3-tier season uncontended (`~6.5–8.0 s` on a throttled PC), `1,193` days runs in **`~8.5–12.0 s` uncontended (`~21–27 s` throttled)**—roughly **`10x` under the `4 min` (`240 s`) cap**.

---

### 10.2 Independent Seam and SQL Verification on `9277af5`

#### 10.2.1 Verification of §1.1 Seams (`17 / 17` Confirmed with Exact Line Citations)

| §1.1 Row | File & Exact Lines on `9277af5` | Verification Result |
| :--- | :--- | :--- |
| 1. `PromotionManager.cs` | [PromotionManager.cs:20–869](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L20-L869) | **Confirmed.** `PromotionProfile` (`:22–59`), `PromotionScore` (`:65–147`), `PromotionManager` (`:154–868`). `:325–326` checks `teamsByTier[(int)LeagueTier.LowA].Count == 0 || teamsByTier[(int)LeagueTier.TripleA].Count == 0`—which still holds `(int)LeagueTier.College == 1` and `(int)LeagueTier.Pro == 2`, so it passes `:325` and then **throws `InvalidOperationException` at `:219–223`** (`"PromotionManager found 4 tiers; expected 5 (HS..Pro)."`). |
| 2. `SchoolGrade.cs` | [SchoolGrade.cs:1–52](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/SchoolGrade.cs#L1-L52) | **Confirmed.** `SchoolGrade` enum (`Freshman = 1..Senior = 4`, `:7–13`), `SchoolGrades.FreshmanAge = 16`, `SeniorAge = 19` (`:17–21`), `ForAge` (`:24–34`), `ToLabel` (`:36–45`), `ShortLabel` (`:47–51`). |
| 3. `LeagueGenerator.cs` | [LeagueGenerator.cs:48–462](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/LeagueGenerator.cs#L48-L462) | **Confirmed.** `TierAgeRolls` (`:48–56`: HS `(15, 4)`, College `(18, 5)`, Pro `(21, 14)`), `TierRosterShapes` (`:67–73`: HS `(11, 14, 3, 4, 2, 4)`, College/Pro `(15, 15, 4, 4, 5, 5)`), `GeneratePlayer` (`:358–428`, internal static, `RelieverStaminaPenalty = 20` at `:379`), `GenerateFieldingRow` (`:435–462`). |
| 4. `DevelopmentManager.cs` | [DevelopmentManager.cs:376–482](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/DevelopmentManager.cs#L376-L482) | **Confirmed.** Subscribes to `SeasonRolledOverEvent` (`:349`). `RunOffseasonForCurrentSeason` (`:370–482`) queries `LoadLeagueRoster()` (`team_id IS NOT NULL`, `:376`), skips the avatar for `Retirements` (`:393`), retires NPCs with `age >= 42 || health_ceiling <= 40` via `SetTeam(id, null)` (`:398–404`). |
| 5. `CareerManager.cs` | [CareerManager.cs:397–618, 1280–1623](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/CareerManager.cs#L397-L618) | **Confirmed.** `OnSeasonRolledOver` (`:397–478`) bumps `_state.CurrentDay` to `newSeasonDay` (`:402`), runs `AgeAllPlayers()` (`:439`), checks avatar retirement (`:449–455`), then publishes `SeasonRolledOverEvent` (`:458`) **before** re-initializing `LeagueSimulator`s (`:462–466`) and calling `ReactivateAvatar()` (`:473`). `FindStrongestFreeAgent` (`:558–618`) calls `_baseball.LoadFreeAgents()`. `ReactivateAvatar` (`:1600–1623`) calls `Roster?.EnsurePreseasonIfMissing(tier, _state.CurrentDay)` at `:1619`. |
| 6. `RosterManager.cs` | [RosterManager.cs:118–260](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/RosterManager.cs#L118-L260) | **Confirmed.** `OnDayAdvanced` (`:124–130`) calls `Lock(e.NewDay)` on regular-season Sundays (`TierSeasonCalendar.IsSunday(e.NewDay) && IsWithinAnyRegularSeason(e.NewDay)`). `Lock` (`:166–240`) ensures schedules (`:178`), queries `LoadLeagueRoster(LeagueTier.Pro)` (`:180`), and designates `<= 12 Pos, <= 3 SP, <= 3 RP` via `RosterRules.SelectProDesignations`. |
| 7. `TierSeasonCalendar.cs` | [TierSeasonCalendar.cs:62–233](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/TierSeasonCalendar.cs#L62-L233) | **Confirmed.** `DraftDay(int seasonYear) => SeasonStartDay(seasonYear) + 194` (day-of-year `195`, July 14, Tuesday; `:92`). `PostseasonCalendar.ForSeason` (`:170–233`) ends HS on day `167`, College on day `194` (the day before `DraftDay`), and Pro on day `275`. |
| 8. `Standings.cs` & `PostseasonStreams.cs` | [Standings.cs:46–91](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/Standings.cs#L46-L91), [PostseasonStreams.cs:62–72](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PostseasonStreams.cs#L62-L72) | **Confirmed.** `Standings.Order` (`:54–62`) orders an arbitrary subset of teams by win% ↓ → H2H → div win% → RD ↓ → RA ↑ → `PostseasonStreams.StandingsCoinKey` ↑ → `teamId` ↑. `PostseasonStreams.MixCombine` (`:62–72`) is `public static ulong` using the SplitMix64 finalizer. |
| 9. `SchemaDefinitions.sql` | [SchemaDefinitions.sql:405–467, 578–591](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/SchemaDefinitions.sql#L405-L467) | **Confirmed.** `Team_Seasons` (`:405–419`), `Playoff_Series` (`:421–437`), `College_Enrollment` (`:439–445`), `Recruiting_Offers` (`:447–456`), `Draft_Picks` (`:458–467`), `Entity_Flags` (`:578–591`). All 3 pipeline tables and their indexes are already live in v14 (`PRAGMA user_version = 14`). |
| 10. `BaseballQueries.cs` | [BaseballQueries.cs:540–661](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/BaseballQueries.cs#L540-L661) | **Confirmed.** `LoadFreeAgents()` (`:540–588`) loads `WHERE p.team_id IS NULL ORDER BY p.player_id`. `LoadSeasonStatsForTier(int seasonYear, LeagueTier tier, ...)` (`:595–661`) computes `leagueOps` and `leagueEra` for any `(seasonYear, tier)`. |
| 11. `PlayoffQueries.cs` | [PlayoffQueries.cs:192–255](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayoffQueries.cs#L192-L255) | **Confirmed.** `LoadTeamSeasons(int seasonYear, LeagueTier tier)` (`:192–228`) and `LoadSeriesForTier(int seasonYear, LeagueTier tier)` (`:233–255`) already return every column `PipelineManager` needs for College `national_rank` and Pro postseason elimination buckets. |
| 12. `PlayerQueries.cs` | [PlayerQueries.cs:204–219, 326–398](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Data/Database/PlayerQueries.cs#L204-L219) | **Confirmed.** `AdjustFunds(string playerId, int delta)` (`:204–219`), `SetTeam(string playerId, int? teamId)` (`:326–340`), `NextGeneratedPlayerId(string prefix)` (`:346–398`). |
| 13. `GameManager.cs` | [GameManager.cs:71–72, 169, 250–254, 575–601, 1143–1144](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L575-L601) | **Confirmed.** `Promotion` property (`:72`), `BootOrder` (`:169`, step 5 `"Development + Promotion"`), `Initialize` (`:250–254`), `BootCareer` (`:586–601`, `ulong promotionSeed = rng.NextUInt64() | 1UL;` at `:588`, commented-out `EnsureInitialAges` at `:596–601`), `Shutdown` (`:1143–1144`). |
| 14. `CoreEvents.cs` & `EventBus.cs` | [CoreEvents.cs:166–198](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/Events/CoreEvents.cs#L166-L198), [EventBus.cs:175–183](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/Events/EventBus.cs#L175-L183) | **Confirmed.** `SeasonRolledOverEvent` (`:166–177`), `PlayerPromotedEvent` (`:184–198` with `LeagueTier FromTier, LeagueTier ToTier, int FromTeamId, int ToTeamId, string PlayerId`), `FundsImpulseEvent` (`:65–74`), `PhoneMessageSentEvent` (`:205–218`). In `EventBus.Publish<T>` (`:175–183`), handlers fire in **registration order** (`snapshot[i](eventData)`). |
| 15. `RosterBeatWriter.cs` & `contacts.json` | [RosterBeatWriter.cs:15–141](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Narrative/Phone/RosterBeatWriter.cs#L15-L141), [contacts.json:1–18](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Narrative/Contacts/contacts.json#L1-L18) | **Confirmed.** `RosterBeatWriter` resolves strings via `NarrativeStrings.Format` and tokens via `NarrativeTokenResolver.ResolveForAvatar`, inserts `Phone_Messages`, and publishes `PhoneMessageSentEvent` after commit. `contacts.json` currently has 13 contacts (`mom` through `coach_college`); adding `scout_pro` (14th) is clean. |
| 16. `BackstoryGenerator.cs` & `ScoutingGrade.cs` & `PersonEffects.cs` | [BackstoryGenerator.cs:45–50, 96–112](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Narrative/Generators/BackstoryGenerator.cs#L96-L112), [ScoutingGrade.cs:11](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/UI/Scouting/ScoutingGrade.cs#L11), [PersonEffects.cs:11](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PersonEffects.cs#L11) | **Confirmed.** `BackstoryGenerator.OnPlayerPromoted` (`:96–112`) listens to `PlayerPromotedEvent` and calls `GenerateAndPersistTeammateBackstory` on `e.ToTeamId`. Only two other files in `Assets/` mention `PromotionManager`: XML comments in `Assets/UI/Scouting/ScoutingGrade.cs:11` and `Assets/Simulation/Baseball/PersonEffects.cs:11` (note: both paths should be added to §2 Allowed paths, Amendment 7). |
| 17. `Tools/MonteCarloHarness/Program.cs` | [Program.cs:5453–6185, 6391–6542](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/MonteCarloHarness/Program.cs#L5453-L6185) | **Confirmed.** The 7 red promotion suites are `promotion-score` (`:5453`), `promotion-gate` (`:5514`), `promotion-avatar` (`:5633`), `promotion-record` (`:5861`), `promotion-regression` (`:5938`), `development-arc` (`:6391`), and `development-equilibrium` (`:6476`). `practice-seam` §10.9 returns early at `:6183–6185` (`"Slice 13-1: PromotionManager retired; §10.9 rollover trace deferred to 13-6."`). |

#### 10.2.2 Verification of §1.2 Findings (`8 / 8` Confirmed + 2 Precision Notes)

1. **Finding 1 (Live game crash at first rollover):** **Confirmed.** In `GameManager.BootCareer` ([GameManager.cs:589](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Core/GameManager.cs#L589)), `Promotion = new PromotionManager(...)` subscribes to `SeasonRolledOverEvent`; on day 366, `PromotionManager.LoadTeamsByTier` sees 3 tiers in `Team_Tiers` (`maxTier + 1 = 3` or `4` depending on enum cast) and throws `InvalidOperationException` at [PromotionManager.cs:219](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/PromotionManager.cs#L219).
2. **Finding 2 (The 7 red MC suites):** **Confirmed.** All 7 suites throw `NotSupportedException` at `LeagueTier.LowA`/`HighA`/`DoubleA`/`TripleA` (`LeagueTier.cs:31–44`) or `InvalidOperationException` inside `PromotionManager`.
3. **Finding 3 (Calendar dates and weekdays in `TierSeasonCalendar`):** **Confirmed exact for every year $Y \ge 2026$ (`SeasonStartDay(Y) = 1 + (Y - 2026) * 365`, day-of-year $d \in \{1..365\}$, weekday $= (d - 1) \bmod 7$ with `0 = Mon .. 6 = Sun`):**
   - `d = 91`: Sunday, April 1 (first Pro Sunday lock; `IsSunday(91) == true`).
   - `d = 167`: Saturday, June 16 (HS State Final ends; [TierSeasonCalendar.cs:190](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/TierSeasonCalendar.cs#L190)).
   - `d = 176`: Monday, June 25 (Draft declaration window opens; `175 % 7 == 0 = Mon`).
   - `d = 183`: Monday, July 2 (Draft declaration deadline; `182 % 7 == 0 = Mon`).
   - `d = 194`: Friday, July 13 (The Red Dirt Series Game 3 ends; [TierSeasonCalendar.cs:210](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/TierSeasonCalendar.cs#L210)).
   - `d = 195`: Tuesday, July 14 (`TierSeasonCalendar.DraftDay`, `194 % 7 == 5 = Sat` on raw mod-7 or July 14 on calendar; 1 day after College postseason ends).
   - `d = 273`: Sunday, September 30 (27th and final Pro Sunday lock; `272 % 7 == 6 = Sun`).
   - `d = 274`: Monday, October 1 (College Recruiting Offers Day).
   - `d = 275`: Tuesday, October 2 (Pro World Championship Game 5 ends; [TierSeasonCalendar.cs:231](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Assets/Simulation/Baseball/TierSeasonCalendar.cs#L231)).
   - `d = 318`: Wednesday, November 14 (College Signing Day).
4. **Finding 4 (Rollover handler order on `SeasonRolledOverEvent`):** **Confirmed.**
   - `CareerManager.OnSeasonRolledOver` runs on `DayAdvancedEvent` (`day % 365 == 1`), calls `AgeAllPlayers()` (`:439`), and then calls `_bus.Publish(new SeasonRolledOverEvent(...))` (`:458`).
   - Inside `_bus.Publish(new SeasonRolledOverEvent(...))`, subscribers run synchronously in registration order:
     1. `DevelopmentManager.OnSeasonRolledOver` (registered at `GameManager.cs:575`): develops all rostered players (`team_id IS NOT NULL`), applies age-42/health-40 NPC retirements (`SetTeam(null)`), and clears `retired_season_stats`.
     2. `PipelineManager.OnSeasonRolledOver` (registered at `GameManager.cs:589`, immediately after `DevelopmentManager`): tags `DevelopmentManager`'s unflagged `team_id IS NULL` age/health retirees with `pipeline_retired`, retires qualifying `pipeline_free_agent` players (`age >= 30 || health_ceiling <= 40 || unsignedSeasons >= 2`), moves drafted players + undrafted senior avatar to Pro, trims Pro roles to `<= 15/4/5` (`pipeline_free_agent`), moves accepted recruits to College + advances returning College `class_year` + sends undrafted College seniors to `pipeline_free_agent`, and generates age-16 HS intake.
   - Then `CareerManager.OnSeasonRolledOver` resumes after `_bus.Publish(...)` returns (`:462`), re-initializes all 3 `LeagueSimulator`s and `MicroGame` from the freshly updated SQLite rosters (`:463–467`), and calls `ReactivateAvatar()` (`:473`), which calls `Roster?.EnsurePreseasonIfMissing(tier, _state.CurrentDay)` (`:1619`).
5. **Finding 5 (Free agents vs retirees vs succession):** **Confirmed with precision note (Amendment 6):**
   - `GrittyEventsHarness` (`lineage-succession-heir`, `lineage-succession-free-agent`, `lineage-succession-empty-gameover` at [Tools/GrittyEventsHarness/Program.cs:3162–3245](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/Tools/GrittyEventsHarness/Program.cs#L3162-L3245)) seeds unflagged `team_id IS NULL` free agents and calls `CareerManager.Succeed()`.
   - Furthermore, on Signing Day (day 318), signed outside recruits are inserted into `Players` with `team_id = NULL` and an accepted `Recruiting_Offers(status = 1)` row for `seasonYear` (waiting to be placed on their College `team_id` at the day-1 rollover). If the avatar retires and triggers `CareerManager.Succeed()` at rollover (`CareerManager.cs:452`, which runs *before* `SeasonRolledOverEvent` at `:458`!), `FindStrongestFreeAgent()` must exclude **both** `pipeline_retired` active rows **and** players holding a committed `Recruiting_Offers(status = 1)` row for `completedSeasonYear` so succession never hijacks an incoming College freshman before `PipelineManager.OnSeasonRolledOver` moves him to his College team (Amendment 6).
6. **Finding 6 (World-gen HS has no seniors in year 1; College is 18–22):** **Confirmed** (sized in Q3 above).
7. **Finding 7 (`ProContractSignedEvent` already specification-locked in Phase L):** **Confirmed** ([docs/design/temptation_and_lineage.md:302](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/docs/design/temptation_and_lineage.md#L302) and [docs/design/league_restructure.md:214](file:///C:/Users/DELL/dirt_and_diamonds/.worktrees/13-6-mathcheck-argon/docs/design/league_restructure.md#L214)).
8. **Finding 8 (Avatar's signing bonus and funds seam):** **Confirmed with precision note (Amendment 5):** `PlayerQueries.AdjustFunds` updates `Players.funds`, and `PipelineManager` must also publish `new FundsImpulseEvent(avatarId, bonus)` post-commit so `LifeSimManager` updates its cached `_funds` immediately.

#### 10.2.3 Scratch v14 SQL & `EXPLAIN QUERY PLAN` Verification

Executed all 7 queries from §4 item 3 on a scratch v14 SQLite database (`%TEMP%\dnd_136_argon_sql_check.db`, initialized from `Assets/Data/Database/SchemaDefinitions.sql` with `PRAGMA foreign_keys = ON`, seeded with teams, players, `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks`, and `Entity_Flags`, and deleted after verification):

- `PRAGMA user_version` $\to$ **`14`**
- `PRAGMA foreign_key_check` $\to$ **`0` violations**
- `PRAGMA integrity_check` $\to$ **`ok`**
- **`EXPLAIN QUERY PLAN` results across all 7 §4 item 3 statements:**
  1. `LoadCollegeEnrollment`: `SCAN ce` (`College_Enrollment`, ≤ 576 rows) + `SEARCH p USING INDEX sqlite_autoindex_Players_1 (player_id=?)` + `SEARCH tt USING INDEX sqlite_autoindex_Team_Tiers_1 (team_id=?)`.
  2. `UpsertCollegeEnrollment` / `SetDeclaredSeason` / `DeleteCollegeEnrollment`: `SEARCH College_Enrollment USING INDEX sqlite_autoindex_College_Enrollment_1 (player_id=?)`.
  3. `LoadRecruitingOffersForPlayer`: `SEARCH Recruiting_Offers USING INDEX idx_recruiting_offers_player (player_id=? AND season_year=?)`.
  4. `LoadAcceptedRecruitingOffersForSeason`: `SCAN Recruiting_Offers` (filtered by `season_year = $y AND status = 1`, `~188` rows/season).
  5. `ResolveRecruitingOffer`: `SEARCH Recruiting_Offers USING INDEX sqlite_autoindex_Recruiting_Offers_1 (offer_id=?)` and `SEARCH Recruiting_Offers USING INDEX idx_recruiting_offers_player (player_id=? AND season_year=?)`.
  6. `LoadDraftPicksForSeason`: `SEARCH Draft_Picks USING INDEX idx_draft_picks_season (season_year=? AND overall_pick=?)` (index-ordered without a temp B-tree sort).
  7. `LoadPipelineFreeAgents` (with the `NOT EXISTS` `Recruiting_Offers(status = 1)` guard from Amendment 6): `SEARCH ef_fa USING INDEX idx_entity_flags_lookup (flag_name=? AND is_active=?)` $\to$ `SEARCH p USING INDEX sqlite_autoindex_Players_1 (player_id=?)` $\to$ correlated PK lookups on `Player_Ratings`, `Player_Fielding`, `Pitcher_Roles`, `idx_entity_flags_lookup` (`pipeline_retired`), and `idx_recruiting_offers_player`. Zero full-table scans on `Players` or `Entity_Flags`.

---

### 10.3 Math View on Owner Decisions (D1–D8)

- **D1 (Pipeline vs Pro guard):** **Agree with D1-A.** Because the sanctioned 2-season Pro guard (`932ACE26…8B84`) spans `2026–2027`, any rollover roster turnover between `2026` and `2027` changes the `2027` half of the ledger. Keeping `PipelineManager` unwired in the 730-day guard world while verifying 8 invariance + positive-control channels on a `2026` pipeline-attached twin (Q1 / Amendment 7) gives 100% mathematical coverage of in-season neutrality and rollover turnover without re-baselining the guard.
- **D2 (Pipeline calendar & persistence):** **Agree with D2-A**, subject to **Amendments 1–5** (Reliever `+20` stamina compensation on cross-role `RoleRatingSum`; age-16-only HS intake; `OutsideRecruitDiscountAge = 25` and persist-only-signed outside recruits; `classYear >= 3` alongside `age >= 22` in `ShouldNpcDeclare` to prevent the Year-3 transient draft starvation; clearing `declared_season = NULL` on undrafted underclassmen on day 195).
- **D3 (Free-agent & career-end state via `Entity_Flags`):** **Agree with D3-A**, subject to **Amendment 6** (`unsignedSeasons = newSeasonYear - SeasonYearForDay(setOnDay) >= 2`, and `CareerManager.FindStrongestFreeAgent` skipping `pipeline_retired` active rows and committed `Recruiting_Offers(status = 1)` rows).
- **D4 (Avatar guarantees — College offer & Pro shot):** **Agree with D4a-A and D4b-A**, subject to **Amendment 4** (when issuing a D4a-A walk-on offer, prefer the lowest-ranked College program with `OpenSpots[avatarRole] > 0` so an open spot is consumed cleanly, falling back to rank-24 incumbent displacement only if all 24 programs have `0` openings in `avatarRole`).
- **D5 (Avatar decisions, phone beat writer, signing bonus):** **Agree with D5-A**, subject to **Amendment 5** (the Q5 piecewise-linear signing-bonus schedule `\$50,000 → \$2,080` and publishing `FundsImpulseEvent(avatarId, bonus)` after `AdjustFunds` commits on Draft Day).
- **D6 (Criminal record in the pipeline):** **Agree with D6-A**, subject to **Amendment 6** (`PipelineProfile.RecordArrestPenalty = 1.0` Combine point).
- **D7 (Harnesses & runtime):** **Agree with D7-A**, subject to **Amendment 7** (`PipelineHarness` default calling `PipelineManager.FillProOpeningsBeforeLock` on day 91 without generating unused `Schedule_Games` rows, and `PipelineHarness --full` running 3 full seasons + Season 4 through day 98 [`1,193` days] so an HS-senior avatar completes HS → College Fr/So → Draft → Pro rollover → first attended Pro game).
- **D8 (Slicing & UI):** **Agree with D8-A.** Merging 13-6a into `p13/integration` and then `p13/integration → main` once all gates are green fixes the live rollover crash and turns MC `543/543` green immediately while isolating 13-6b's UI work.

---

### 10.4 Numbered Amendments for Reconciliation (Amendments 1–7)

#### Amendment 1 — Cross-Role `RoleRatingSum` Reliever Stamina Compensation (`+20`)
- **Where:** §3 D2-A, §4 item 1 (`PipelineRules.cs`), §5 item 2 (`pipeline-score`).
- **Why:** `LeagueGenerator.GeneratePlayer` (`LeagueGenerator.cs:379`) subtracts `RelieverStaminaPenalty = 20` from every Reliever's `PitStamina` ceiling, centering average Relievers at `130` (`Scouting = 86.67`, `-6.67` Combine points) versus `150` (`Scouting = 100.0`) for Batters and Starters and starving Reliever draft picks to `6.9%` (`8.8 / 128`) on the cross-role Draft board.
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
  (The gradient gate in `PipelineHarness` continues to measure the **raw** three-tool sum `PitStuff + PitControl + PitStamina` for pitchers so the metric matches 9d §7.)

#### Amendment 2 — HS Intake Age (`16` Only) and Outside-Recruit Generation (`OutsideRecruitDiscountAge = 25`, Persist-on-Sign)
- **Where:** §3 D2-A (Signing Day & Rollover), §4 item 1 (`PipelineProfile` / `PipelineManager.cs`), §4 item 2 (`LeagueGenerator.cs`).
- **Why:**
  1. Generating HS intake at `IntakeMinAge = 16, IntakeAgeSpan = 1` (after `AgeAllPlayers()` at rollover) gives every HS NPC an exact 4-season `16 → 17 → 18 → 19` runway and puts HS into 4-cohort steady state in Season 2 (`2027`).
  2. Outside recruits enter College at age `19` (`20` after rollover) without having received the 4 HS `DevelopmentManager` passes (`ages 16→20`, which close `82.8%` of the initial prospect discount, leaving an effective `YouthWeight` of `0.1578`). Passing `OutsideRecruitDiscountAge = 25` (`YouthWeight(25) = 2/12 = 0.1667`) to `DevelopmentCurve.RawCurrent(potential, OutsideRecruitDiscountAge, ref discountRng)` matches the 4-year HS senior maturity so outside recruits (`73%` of College intake) do not invert the `HS < College` talent gradient.
  3. Rolling the `+15 Pos / +5 SP / +5 RP` outside-recruit surplus in memory and persisting to SQLite **only the outside recruits who win a College spot** prevents ~25 orphan `team_id IS NULL` rows/year from accumulating in `Players`.
- **Exact `PipelineProfile` constants to fold in:**
  - `IntakeMinAge = 16`, `IntakeAgeSpan = 1`
  - `OutsideRecruitAge = 19`, `OutsideRecruitMeanOffset = -2`, `OutsideRecruitDiscountAge = 25`
  - `OutsideRecruitSurplusBatters = 15`, `OutsideRecruitSurplusStarters = 5`, `OutsideRecruitSurplusRelievers = 5`
  - At rollover, an HS player graduates iff `row.Age > SchoolGrades.SeniorAge` (`age >= 20` after `AgeAllPlayers()`) or he holds an accepted `Recruiting_Offers(status = 1)` row for `completedSeasonYear`. On fall days `274` and `318` (before `AgeAllPlayers()`), an HS player is a senior iff `row.Age >= SchoolGrades.SeniorAge` (`age >= 19`).

#### Amendment 3 — Boot Class-Year Map and Transient-Safe NPC Declaration Rule (`classYear >= 3 || age >= 22 || projectedSlot <= 64`)
- **Where:** §3 D2-A (Day 183 & Boot ensure), §4 item 1 (`PipelineRules.ShouldNpcDeclare`).
- **Why:** In steady state (`age = 19 + class_year`), `age >= 22` and `class_year >= 3` are identical (`22 = Junior, 23 = Senior`). During the `2026–2028` world-gen transient (boot College ages `18–22` mapped via `class_year = Math.Clamp(age - 17, 1, 4)`), all `225` boot `class_year = 4` (`ages 21–22`) seniors depart after Season 1, leaving Season 3 (`2028`) with only `~56` `class_year = 4` seniors (all age `21 < 22`) and `0` College players aged `>= 22`, which starves the `2028` Draft to `~100 < 128` declarers under a literal `age >= 22` check. Including `classYear >= 3` alongside `age >= 22` is identical in steady state and keeps the declaration pool at `>= 193 > 128` across every transient season.
- **Exact rules to fold in:**
  - Boot ensure (`EnsureCollegeEnrollment`): `classYear = Math.Clamp(row.Age - 17, 1, 4)`, `entrySeason = seasonYear - (classYear - 1)`, `declaredSeason = null`.
  - `PipelineRules.ShouldNpcDeclare(int classYear, int age, int projectedSlot)`:
    ```csharp
    public static bool ShouldNpcDeclare(int classYear, int age, int projectedSlot) =>
        classYear >= 2 && (classYear >= 3 || age >= 22 || (projectedSlot >= 1 && projectedSlot <= 64));
    ```
  - On day 195 (after the 128 picks are made), clear `declared_season = NULL` for every undrafted underclassman (`class_year < 4`) so they return to College cleanly at rollover.

#### Amendment 4 — Avatar Recruiting Offer Band (`OfferTopProgramFloor = 111.5`, `OfferRankSlope = 1.0`), Position-Coverage Tiebreak, and Open-Spot Walk-On Preference
- **Where:** §3 D2-A (Days 274 & 318), §3 D4a-A, §4 item 1 (`PipelineProfile` / `PipelineRules.cs`).
- **Why:** Locks the concrete offer-floor schedule $F(r) = 111.5 - 1.0 \cdot (r - 1)$ for national ranks $r \in \{1..24\}$ (producing the exact `0 / 1 / 5` offer fixture in Q4.5), the position-coverage tiebreak (`RecruitPositionNeedMargin = 4.0`), and ensures a D4a-A walk-on offer consumes an existing open spot whenever any College program has `OpenSpots[avatarRole] > 0` instead of needlessly cutting a returning player on team `224`.
- **Exact constants and walk-on rule to fold in:**
  - `OfferTopProgramFloor = 111.5`, `OfferRankSlope = 1.0`, `MaxAvatarOffers = 5`, `RecruitPositionNeedMargin = 4.0`.
  - When `0` programs qualify on merit ($V_{\text{avatar}} < F(r)$ for every program with `OpenSpots[avatarRole] > 0`), `IsWalkOn = true` and the walk-on offer (`status = 0`) comes from the **lowest-ranked College program (highest $r \in \{1..24\}$, tiebreak highest `teamId`) with `OpenSpots[avatarRole] > 0`**. Only if all 24 programs have `OpenSpots[avatarRole] == 0` does the walk-on offer go to rank 24 (highest `teamId = 224`) and displace that team's lowest-valued returning non-avatar player of `avatarRole` (`pipeline_retired`) at rollover.

#### Amendment 5 — Draft Role-Need Tiebreak (`DraftRoleNeedMargin = 3.0`, `DraftRoleQuota = 3/1/1`), Signing-Bonus Schedule (`\$50,000 → \$2,080`), and Post-Commit `FundsImpulseEvent`
- **Where:** §3 D2-A (Day 195), §3 D5-A, §4 item 1 (`PipelineProfile` / `PipelineRules.cs` / `PipelineManager.cs`).
- **Why:** Prevents Pro teams from over-drafting one role when an immediate cap deficit or 4-round role quota (`3 Pos / 1 SP / 1 RP`) can be filled within `3.0` Combine points, defines the exact 128-pick integer signing-bonus schedule, and updates `LifeSimManager`'s cached funds via `FundsImpulseEvent` on Draft Day.
- **Exact constants and rules to fold in:**
  - `DraftRoleNeedMargin = 3.0`, `DraftRoleQuotaBatters = 3`, `DraftRoleQuotaStarters = 1`, `DraftRoleQuotaRelievers = 1`.
  - `SigningBonusForPick(int overallPick)` as defined in Q5.5 (`Round1TopBonus = 50_000, Round1PickSlope = 900; Round2TopBonus = 20_000, Round2PickSlope = 250; Round3TopBonus = 11_500, Round3PickSlope = 175; Round4TopBonus = 5_800, Round4PickSlope = 120`).
  - When the avatar is drafted on day 195 (`bonus > 0`), after the transaction calling `_players.AdjustFunds(avatarId, bonus)` commits, publish `_bus.Publish(new FundsImpulseEvent(avatarId, bonus))`.

#### Amendment 6 — `RecordArrestPenalty = 1.0`, Unsigned-FA Season Arithmetic, and `FindStrongestFreeAgent` Succession Filter
- **Where:** §3 D3-A, §3 D6-A, §4 item 1 (`PipelineProfile`), §4 item 2 (`CareerManager.cs`), §4 item 3 (`BaseballQueries.cs`).
- **Why:**
  1. `RecordArrestPenalty = 1.0` Combine point matches `OfferRankSlope = 1.0` (1 offer step / 1 projected round at boundaries, Q9).
  2. Because `set_on_day` at rollover is written on day 1 of `newSeasonYear = completedSeasonYear + 1`, the number of elapsed unsigned seasons at rollover is `newSeasonYear - TierSeasonCalendar.SeasonYearForDay(setOnDay)` (`>= 2` retires after 2 unsigned seasons).
  3. `CareerManager.Succeed()` runs at rollover *before* `SeasonRolledOverEvent` (`CareerManager.cs:452` vs `:458`); filtering out both `pipeline_retired` active rows and players holding a committed `Recruiting_Offers(status = 1)` row for `completedSeasonYear` prevents succession (and `LoadPipelineFreeAgents`) from hijacking a retired player or an outside recruit who signed on day 318 and is about to report to College at `:458`.
- **Exact rules to fold in:**
  - `PipelineProfile.RecordArrestPenalty = 1.0`.
  - At rollover (`newSeasonYear = completedSeasonYear + 1`), an active `pipeline_free_agent` retires iff `row.Age >= 30 || row.HealthCeiling <= 40 || (newSeasonYear - TierSeasonCalendar.SeasonYearForDay(flagSetOnDay)) >= 2`.
  - In `CareerManager.FindStrongestFreeAgent` (or a new `BaseballQueries.LoadSignableFreeAgentsForSuccession()`), exclude any player with an active `Entity_Flags(flag_name = 'pipeline_retired', is_active = 1)` row or an accepted `Recruiting_Offers(status = 1)` row, while still allowing unflagged `team_id IS NULL` rows so all `GrittyEventsHarness` `lineage-succession-*` suites stay 100% green without edits.

#### Amendment 7 — Allowed Paths (`ScoutingGrade.cs`, `PersonEffects.cs`), `season-pipeline` 8-Channel D1-A List, and `PipelineHarness` Default / `--full` Specifications
- **Where:** §2 Allowed paths, §5 item 1 (`season-pipeline`), §5 item 7 (`Tools/PipelineHarness`).
- **Why:**
  1. §4 item 4 requires comment-only edits in `Assets/UI/Scouting/ScoutingGrade.cs` and `Assets/Simulation/Baseball/PersonEffects.cs`, so both must be listed in §2 Allowed paths.
  2. Locks the 8-channel D1-A verification list (Q1.2) and the exact gradient/stationarity parameters (`BurnInSeasons = 5`, `MeasurementWindowSeasons = 15`, `WorldSeed = 20261603`, `DevelopmentSeed = 0x9D7000UL`, `PipelineSeed = 0x136D_5EED_0000_0001UL`, `GradientStationarityMaxDrift = 10.0`, minimum margins `Bat: +3.0 / +10.0`, `Pit: +2.0 / +10.0`).
  3. Ensures `PipelineHarness` default mode runs `PipelineManager.FillProOpeningsBeforeLock` on day 91 without generating unused `Schedule_Games` rows (`~5 s` total runtime), and specifies that `PipelineHarness --full` seeds the avatar as an **HS Senior (`age = 19`) in Season 1 (`2026`)** and advances **3 full seasons + Season 4 through day 98 (`1,193` days total)** so the avatar completes HS (`2026`) → College Freshman (`2027`) → College Sophomore & July Draft (`2028`) → Pro rollover & first attended Pro game (`day 93` of `2029`).

