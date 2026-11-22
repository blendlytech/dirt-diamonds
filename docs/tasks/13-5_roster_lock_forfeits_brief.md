# Task Brief: 13-5 — Sunday roster lock, attrition, forfeits and call-up retirement

**Status:** **DRAFT 2026-11-22.** Awaiting Gemini 4 Argon's Math check (§9, answered in a new §10) and the owner's D1–D8 (§3). **No build until both are in.**
**Recommended model:** **13-5a** (engine, persistence, harness): Claude Opus 5.5 (TEAM.md §2, 13-5 row). Gemini 4 Argon: the Math check of this brief (designation scoring, avatar inputs, stress magnitude, forfeit exposure) and the independent review of the build.
**Reviewer:** Gemini 4 Argon independently reviews Opus's sim/persistence diff (TEAM.md §1a). Opus reviews Argon's Math record. Opus is the merge gate after review.
**Owner gate:** D1–D8 before the build. **D3 carries a Gate 2 sanction** under its recommended option. If the build finds that the Pro guard moves (D1), it stops and brings a sanction (13-3 precedent); the recommended D1-A is designed not to move it.
**Spec:** [league_restructure.md](../design/league_restructure.md) §4, §5.1, §7, §10, §11.3, §12, §13, §15 (13-5 row), §16-I5/I9/I13/I14; [scope_realignment.md](../design/scope_realignment.md) §4 (Gate 1 and Gate 2 rows) and §5 (regression envelope); [13-4 brief](13-4_playoffs_ranking_brief.md) §11.3 #3–#4, §11.5, §12.4 #3, §12.5 S1.
**Branches / worktrees:** brief `p13/13-5-brief-opus` (this file, `C:\Users\DELL\dnd-opus-135`); Math `p13/13-5-mathcheck-argon`; build `p13/13-5a-opus`; review `p13/13-5a-review-argon`. All start from **`p13/integration`** at or after **`b7fbc4c`** (13-4a merged) plus this brief's merge, each in its own worktree, and merge into `p13/integration`, **never `main`** (spec §15, §16-I12).

---

## 0. Preconditions (check first; stop and report if any fails)

1. Your own worktree (TEAM.md §3); `git status` clean; a write check (empty commit + soft reset); `C:` free space ≥ 2 GB.
2. `p13/integration` at or after `b7fbc4c`, with this brief merged, D1–D8 recorded in §3, and Argon's §10 reconciled.
3. Baseline on that head before any edit: build 0/0; SchemaValidator **174/174**; CoreLoop **25/25**; GrittyEvents **363/363**; MC default **470/477**, the reds exactly the 7 promotion suites (13-6's); resolver 13/13 (`--only resolver` LF SHA-256 `6d92422863f22d73…`); tier-ladder 14/14; `season-pipeline` green with the Pro guard **PA 81433 / H 18471 / ER 8803**, ledger **`932ACE26…8B84`**, and the 2026/2027 Pro postseasons at 11 series / one champion; every allocation line 0 B; 0 leaked `%TEMP%\dnd_*` files. Record the wall time and the normalized default-log SHA-256 (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`). 13-4 §12.5 recorded `9bd4066d56c9…`; compare later runs with your own baseline, as 13-4 §11.1 did.
4. **No Blind Queries.** This brief validated its §4 SQL on a scratch v14 database (`sqlite3` over `SchemaDefinitions.sql`, `user_version` 14): `EXPLAIN QUERY PLAN` on every statement, plus the upsert and the forfeit update executed on throwaway rows with `foreign_keys = ON`. Revalidate any new or changed statement on a scratch copy (`SchemaValidator --emit <scratch db>` or `validate_sqlite_schema`), never the live save.

## 1. Context

13-5 makes absence real. With no minor leagues there's nobody to call up (spec §7), so the nameless replacement-level call-up that shadows an absent player retires in every tier. A lineup missing a player reshuffles from who's left, and a team that can't field 9 position players and a pitcher forfeits (`RegulationInnings(tier)`–0, `status = 2`, spec §4). Pro adds the weekly **Sunday lock**: each Pro team's 18 actives (12 position players, 3 SP, 3 RP) are set by the coach and frozen for the week, and an active player who goes down mid-week can't be replaced by an inactive one. The coach designates the avatar too, and being made inactive costs the avatar that week's games, a coach text and a stress hit.

**Gate staging.** 13-5 isn't on the Gate 2 path (scope_realignment Gate 1 row: 13-4…13-7 run inside the designed v14/v15 and must not block Gate 2). The recommended path adds **no DDL**. But call-up retirement and `CanField` reach the HS regular season, which Gate 2 freezes (13-4 brief §2). That conflict is D3.

### 1.1 What exists on `p13/integration` (`b7fbc4c`)

| Seam | State today | 13-5 consequence |
| :-- | :-- | :-- |
| `Roster_Designations` (`SchemaDefinitions.sql:637–642`) | `player_id` PK (one row per player, `ON DELETE CASCADE`), `team_id` FK, `is_active` ∈ {0,1}, `locked_through_day`. Checked by `SchemaValidator/Program.cs:559`. **No reader or writer** | The shape covers the weekly lock (§1.2 #4). **No DDL** |
| `Schedule_Games.status` (`:603–618`) | 0 scheduled / 1 final / 2 forfeit. Regular results write `status = 1` only (`ScheduleQueries.cs:55–57`, `RecordFinal`); the macro result queue has no status lane (`LeagueSimulator.cs:849–857`). Only 13-4a's postseason writes 2 (`PlayoffQueries.cs:74–76`) | A status lane for macro results; a regular-season forfeit writer |
| `Team_Seasons` / ranking | Upserted from phase-0 rows with `status IN (1, 2)` (`PlayoffQueries.cs:23–26`), scores included | **Forfeits already count** in W/L and runs (D2c) |
| `AvailabilityLedger` (`AvailabilityLedger.cs:70–149`) | Bus-fed absences, `Seed` at boot (`GameManager.cs:418–422`), shared by every tier sim, the micro-sim and the career (`:464`, `:497`, `:504`). `SlotAvailability.Absent` is documented as "a replacement-level call-up shadows the slot" (`:11`) | The ledger stays; its meaning changes from "shadowed" to "a hole" |
| Macro call-up (`LeagueSimulator.cs`) | `ReplacementRating = 40` and tier-shifted call-up ratings (`:173–191`, `:466–475`); a discard stat line past the roster (`:420–424`). An absent batter's PA plays as the call-up (`PlayHalfInning`, `:1267–1305`). With no pitcher available, the call-up pitches (`SelectPitcher`, `:794–795`) | Retire both. An absent batter leaves the lineup; no pitcher means a forfeit |
| Macro lineup | Built **once at `Initialize`** by `LineupBuilder.Build` over all position players (`:535–558`); `PlayHalfInning` cycles those 9 (`:1264`). There's no bench use and no per-day rebuild | "The lineup reshuffles" needs a per-game-day build over the eligible players (§1.2 #2) |
| `LineupBuilder` (`LineupBuilder.cs:39–96`) | Greedy legal defense (C…RF by primary, then secondary, then out of position; DH last; a featured player replaces the DH). Allocates (`List`, `Dictionary`, LINQ): "load-time code" (`:21`) | An allocation-free twin for the game-day path, proven equal to `Build` |
| Macro postseason (`:901–1016`) | The narrow forfeit checks **pitchers only** (`PickPostseasonPitcher` returns −1, `:924–930`); absent **batters are still shadowed** through `PlayHalfInning` because `_hasAbsences` is live (`:946–955`) | Unify on `CanField`; retire the postseason batter shadow |
| `MicroGame` call-up | Per-team call-up ratings and arsenal, feed name `"Call-Up"` (`MicroGame.cs:207–231`, `:402–416`); shadowed starters (`:1192–1199`); **bullpen pulls take relievers in rank order and shadow an absent one** (`MaybePullPitcher`, `:1305–1324`) | Skip absent (and, in Pro, inactive) relievers; no call-up anywhere |
| `MicroGame` lineup | `RebuildLineups` (`:645–670`) runs `LineupBuilder.Build` per team; the human always starts as the featured DH (`:1166–1172`) | Rebuild when eligibility changes; an absent or inactive avatar is not featured |
| Postseason pitcher test | `MicroGame.HasLegalPostseasonPitcher` (`:601–627`) and `CareerManager.cs:1627–1641`; `PlayoffManager.ForfeitScore` (`PlayoffManager.cs:423–443`) awards a double forfeit to the **high seed** (spec §16-I13) | One `CanField` rule replaces it (D2e) |
| `CareerManager.OnDayAdvanced` (`CareerManager.cs:1414–1452`) | Claims the avatar team's row and parks it; an absent avatar's game autopilots (`:1448–1451`) on the career's `_rng` (`:1584–1586`). No forfeit path | `CanField` before parking; an inactive avatar never parks |
| Reload | `ReclaimPostseasonGame` (`:1460–1468`) re-parks **postseason only**, called from `GameManager.cs:527–530` | Carried 13-4 item a (D3) |
| Cancel/rollback | `PitcherRestLedger.RecordStartUndoable` / `Revert` (`PitcherRestLedger.cs:70–93`, 13-4 S1). `MicroGame.RevertPostseasonStarts` returns early unless `_postseasonGame` (`MicroGame.cs:567–581`) | Carried 13-4 item b (D3): drop the postseason-only gate |
| Clock (`TimeManager.cs:64–83`) | Day-granular: one batch, then `DayAdvancedEvent(Day, SeasonYear, DayOfSeason)`. **No hour clock.** Day-of-season 1 is a Monday every season (`GameCalendar.cs:78–80`); the tier sims flush on `DayOfSeason % 7 == 0` (`LeagueSimulator.cs:27`, `:661`) | "Sunday 23:59" = the Sunday tick, after the Pro sim's Sunday flush (D6) |
| Composition (`GameManager.cs:447–530`) | Tier sims attach first, then the career, then playoffs; boot seeds absences before the sims | `RosterManager` attaches after the tier sims, before the career |
| Coach | **No coach entity.** `coach_reyes` (Pro) and `coach_hs` are phone contacts only (`contacts.json`); there's no College coach contact. `RelationshipGraph` is Life code (`Simulation/Life/RelationshipGraph.cs`), and `Relationships.type_enum` allows Rival/Friend/Partner/Child only (`SchemaDefinitions.sql:106`) | "Coach affinity from `RelationshipGraph`" has no source (D4c) |
| Coach text and stress | `NarrativeLogQueries.InsertText(seasonYear, gameDay, playerId, contactId, body)` (`NarrativeLogQueries.cs:174`), written today only by `EventConsequenceApplier`; `StressImpulseEvent(PlayerId, Delta)` (`CoreEvents.cs:114`) feeds `LifeSimManager` | The delivery path for the beats (D5) |
| Guard world (`MonteCarloHarness/Program.cs:494–567`) | One Pro `LeagueSimulator` on `RngState(777)`, flat spread 0, playoffs wired (`:535–539`); **no `Availability`, no `Persons`, no avatar**. Guard constants and ledger: `Slice133Suites.cs:31–67` | The guard can't see call-up retirement; designations need a proof (D1) |
| Availability suite (`Program.cs:4205–4498`, registered at `:172`) | Ledger semantics, empty-ledger bit identity, "macro shadowing" (`:4303`) and career benching (`:4407–4497`) | Rewritten (spec §15, 13-5 row) |

### 1.2 Findings that shape the D-options

1. **The guard world never shadows anyone.** It attaches no `AvailabilityLedger` (`Program.cs:529–531`), and `RefreshAvailability` sets `_hasAbsences = false` when the ledger is null (`LeagueSimulator.cs:1134–1137`). Retiring the call-up can't move the guard.
2. **Designations can be made guard-neutral by construction.** In the guard world every Pro team carries 15 position players, 4 SP and 5 RP; nobody is absent; all ratings are 50.
   - *Lineup:* `LineupBuilder` is a greedy pick over a candidate set. Over any subset that still contains its 9 picks, it picks the same 9 in the same positions (each step's best unchosen candidate is still present and still best). So if the 9 starters are always active, the lineup over the actives equals today's lineup, byte for byte.
   - *Pitchers:* on Tue/Thu/Sat with `MinRestDays(Pro) = 3`, the ace starts Tue and Sat (gaps 3 and 4) and #2 starts Thu (gap 7); #3 and #4 never start, and no reliever ever opens. The D3 postseason slots (gaps 3, 2, 2/4) never reach #3 either. Making SP #4 and RP #4–#5 inactive therefore changes no pick.
   - *Counter-case:* a single scalar score with id tie-breaks (D4a-B) would, in the flat world, make the 12 lowest ids active. That can drop a positional starter, so other player ids bat and the ledger SHA moves even when the totals don't.
3. **"Reshuffle" is new code on the hot path.** The macro sim never rebuilds a lineup today. A per-game-day rebuild must allocate nothing (GEMINI §3), so `LineupBuilder` needs an allocation-free twin, rebuilt only for teams whose Initialize lineup has an ineligible member, with a parity check against `Build`.
4. **No DDL is needed.** One row per player holds the current week's designation. The re-activation beat needs the prior `is_active`, which the lock reads before it overwrites. "Has this week been locked?" is `MAX(locked_through_day)` over the tier's teams. Tier lives on `Conferences`, not `Teams` (the validator caught a `Teams.tier` join in this brief's first draft).
5. **The narrow postseason forfeit is a special case of `CanField`.** Its pitcher test (available, not used in the Red Dirt Series) is `CanField`'s pitcher clause plus the series filter. Its missing piece is the 9-position-player clause.
6. **Forfeits skip draws; they never add them.** A forfeited macro game draws nothing from `LeagueSimulator._rng`, and a forfeited attended game draws nothing from `CareerManager._rng`, so later games in that stream shift. That's deterministic and happens only when a forfeit happens (never in the guard world or a neutral run). Designations and double-forfeit awards need no RNG at all.

## 2. Hard constraints

- **Allowed paths (13-5a):**
  - `Assets/Simulation/Baseball/`: new `RosterManager.cs` (lock, coach AI, `CanField` façade) and `RosterRules.cs` (the pure `CanField` and designation rules); edits to `LeagueSimulator.cs`, `MicroGame.cs`, `CareerManager.cs`, `PlayoffManager.cs` (unification only), `LineupBuilder.cs` (the allocation-free twin), `AvailabilityLedger.cs` (doc and enum comments only).
  - `Assets/Data/Database/`: new `RosterQueries.cs` and DTOs (`RosterDesignationRow`, `RosterStatusView`); `ScheduleQueries.cs` (a status-carrying result write).
  - `Assets/Core/`: `CoreEvents.cs` (two events), `GameManager.cs` (composition, boot reconcile, regular-season reclaim if D3-A, the inactive avatar's planner blocks).
  - `Assets/Narrative/` (D5-A): a new beat writer, a `coach_college` row in `contacts.json`. `Assets/Localization/strings.csv`: the beat texts.
  - `Tools/MonteCarloHarness/`, `Tools/CoreLoopHarness/`.
  - Docs: this brief, the Math record, `progress.md`, spec §7/§16 sync, the MC skill.
  - **Everything else is read-only.**
- **Calibration contract:** `AtBatResolver`, `BaseOutAdvancement`, `MicroGame`'s per-PA model, `TierEffects`, `EffectiveRatings` and every band are untouched. Resolver guard byte-exact (`6d924228…`). **Pro guard byte-exact with `RosterManager` wired into the guard world** (D1). Neutral paths (no absences, no forfeits) are byte-identical: the default MC log, normalized, changes only by lines that new or rewritten suites add.
- **Zero allocation:** every game-day path allocates 0 B: `CanField`, the eligibility caches, the lineup rebuild, the macro and attended forfeit paths, and the designation lookups. The weekly lock and boot reconcile run outside the per-PA loop and may allocate, bounded, at most once a week.
- **Determinism / RNG:** **no new draws** from `LeagueSimulator._rng` or `CareerManager._rng`, and none from the postseason streams. The lock, the coach AI, `CanField`, the forfeit scores and the double-forfeit award are pure functions of persisted state; ties break by player id ascending, as `LineupBuilder` and `StarterSelection.CompareQuality` do (`StarterSelection.cs:124`). Same save ⇒ same designations, including across a reload. Narrative text variants, if any, use the Narrative layer's own RNG, never the sim's.
- **Boundaries:** `RosterManager` lives in `/Simulation/Baseball/` and never references `/Simulation/Life/` (no `RelationshipGraph`). Life and Narrative learn about designations and forfeits only through Core events carrying primitives, and the DB. The phone, dashboard and planner only read.
- **DB:** parameterized, prepared, pooled statements (`RosterQueries`); the lock is one transaction per week; a forfeit's result commits in its game's batch (macro: the Sunday flush; attended: its own batch). Baseball writes and Narrative writes never share a transaction. **No DDL:** v14 stays frozen until L-1's v15 window.
- **Spec rules kept:** no Sunday games (none are added); the lock is **Pro-only** and `CanField` applies to **every tier** (§16-I5, subject to D3's staging); forfeits credit no player stats (§4); the at-bat view never opens for a forfeit (§7).
- **Gate 2:** as D3 decides. Under every option, an HS regular season with no absences, cancels or reloads behaves exactly as today.

## 3. Owner decisions (D1–D8)

Each D-option below has a recommendation. Nothing is decided until the owner says so.

### D1. Call-up retirement and designations vs the sanctioned Pro guard

**Facts** (§1.2 #1–#2): the guard world never shadows, so call-up retirement can't touch it; designations can be built so the guard world's lineups and starters are provably unchanged.

- **A (recommended). Wire `RosterManager` into the guard world (production composition, as 13-4 wired the postseason) and prove the guard byte-exact.** No sanction is needed. The harness asserts these invariance channels:
  1. the guard (PA/H/ER and ledger SHA) is byte-exact;
  2. every team's 9 `LineupBuilder` starters are active every week, and the lineup-rebuild gate never fires;
  3. no inactive pitcher is ever selected, and the pick sequence matches the unwired run;
  4. zero forfeits, regular season and postseason;
  5. both RNG snapshots (`LeagueSimulator.RngSnapshot` and every postseason stream count) are unchanged across every lock;
  6. 27 locks a season (Sundays 91–273), 768 rows each, with no stat, `Pitcher_Rest` or `Team_Seasons` write in the lock batch;
  7. the postseason champion lines in the default log are unchanged.

  **Stop rule:** if any channel moves, the build stops. It brings either a fix or a sanction request on the 13-3 precedent: re-record the guard, Argon reproduces it 20/20 byte-identical, and the owner sanctions it.
- **B.** Leave the guard world as 13-4 left it (no `RosterManager`). The guard trivially holds, but it no longer runs the production composition, and designations are proven only by smaller fixtures.

### D2. The forfeit contract

- **a. Score.** **Recommended:** `RegulationInnings(tier)`–0 (HS 7–0, College/Pro 9–0), `status = 2`, as spec §4 and 13-4a already do.
- **b. Stats.** **Recommended: none.** No `Batting_Stats` / `Pitching_Stats` row or increment, no G/GS, no recorded start (the rest ledger is untouched), no PED game cost, no `Game_Logs` play-by-play, no normalization.
- **c. Standings and ranking.** **Recommended A:** keep 13-4a's approved behavior (D1-A / §16-I14). A forfeit is a W and an L, and its 9–0 counts in runs scored and allowed, run differential and College `RD_norm` (spec §4: "a 9–0 final"). *B:* W/L only, with the score excluded from runs. B changes 13-4a's `Standings`/`RankingCalculator` and needs Argon's ranking re-check.
- **d. Both teams can't field.** **Recommended A:** split by phase. In the **postseason** the high seed is awarded the game (13-4a's §16-I13 rule, unchanged). In the **regular season** the **home team** is awarded it: no seed exists, and home/away is balanced (HS 5/5, College 14/14, Pro 32/32), so the edge is fair over a season. Neither draws RNG. *B:* the home team everywhere (amends I13). *C:* a seeded coin keyed on the game's natural key `(schedule_seed, seasonYear, tier, day, home, away)` with a new domain tag through `PostseasonStreams`' mixer (fair, never sim RNG, more machinery).
- **e. One rule.** **Recommended:** `RosterRules.CanField` is the only implementation. It takes the team's eligibility on the day (available ∧ active, per §1.1) and the series filter. It needs **≥ 9 eligible position players and ≥ 1 legal pitcher** (an eligible SP or RP not used in the Red Dirt Series window). Each sim feeds it from its own allocation-free slot caches; `RosterManager.CanField(teamId, day)` is the read-only façade for tools and UI. It replaces `HasLegalPostseasonPitcher` and `PickPostseasonPitcher`'s −1 as the forfeit test; `PostseasonPlay` keeps its values.
- **f. Attended forfeits.** **Recommended:** `CareerManager` checks `CanField` for both teams before it parks. On a failure there's no park and no `MicroGame`. The forfeit commits in its own batch (regular season, through a new status-carrying `ScheduleQueries` writer) or through `RecordAttendedForfeit` (postseason). `GameForfeitedEvent` publishes after the commit. The coach text follows (D5).

### D3. Gate 2 conflicts (HS) and the two carried 13-4 items

Gate 2 freezes the HS regular-season path (13-4 brief §2; scope_realignment §5's envelope). §16-I5 applies call-up retirement and `CanField` forfeits to HS too. Two 13-4 items ride the same decision:

- (a) a **parked regular-season attended game is lost on reload**: `ReclaimPostseasonGame` re-parks postseason games only (13-4 §11.5);
- (b) a **cancelled or rolled-back regular-season attended game keeps its two recorded starts**: the `StartUndo` revert is gated to postseason games (`MicroGame.cs:569`; 13-4 §12.5 S1).

All four touch the HS regular season only when an absence, a forfeit, a cancel or a reload occurs. A neutral HS season stays byte-identical under every option.

- **A (recommended). All tiers now, under a narrow owner sanction** that lifts the Gate 2 HS freeze for exactly these four changes:
  1. HS call-up retirement and the lineup reshuffle;
  2. HS `CanField` forfeits;
  3. a regular-season parked-game reclaim on boot (every tier): the avatar team's earliest `status = 0` phase-0 row on or before today re-parks as the same fixture, `game_id` included (query validated, §4);
  4. the regular-season `StartUndo` revert on cancel or rollback, including `ShortRestStarts` and `BullpenGames`.

  *Why:* (3) and (4) fix defects inside Gate 2's own "one Friday game" loop (a reload on game day loses the game today). (1) and (2) apply one rule to every tier, as the spec says. Nothing changes without an absence.
- **B. Pro and College now; HS after Gate 2.** HS keeps the call-up and has no forfeits, behind a tier switch. (a) and (b) are deferred to post-Gate-2. Two absence models live side by side until then, and the HS reload defect stays in the demo tier.
- **C. Hybrid.** (3) and (4) for every tier now, as bug fixes. (1) and (2) for Pro and College now, HS after Gate 2. Pick this if the owner wants the HS rule frozen but the defects fixed.

### D4. Designations: who the coach makes active

- **a. NPC rule (Pro; Math values Argon's).**
  - **A (recommended):**
    - Over the players healthy at the lock, the 9 `LineupBuilder` starters are always active.
    - The other 3 active position slots go to the best remaining players by a Math-owned bench score (role-weighted: positional coverage plus bat score).
    - SP: the top 3 healthy by `CompareQuality`. RP: the top 3 healthy by the same order.
    - When fewer healthy players exist than active slots, absent players fill them, earliest return first. They still can't play until they return, and an inactive player can't replace them (§7 attrition).
    - This is what makes D1-A provable.
  - *B:* one role-weighted scalar for all 12 slots. Simpler, but it can bench a positional starter, which moves the guard ledger in the flat world (§1.2 #2) and reshapes lineups in spread worlds.
- **b. The avatar.**
  - **A (recommended):** the same rule. Ratings decide who starts, and the avatar's **performance, health and coach-affinity** score decides only at the margin, against the bench candidates. A starting-calibre avatar is always active; a fringe avatar can lose his spot. A pitcher avatar is judged the same way at the SP #3/#4 and RP #3–#5 margin.
  - *B:* the avatar is fully scored, so the coach can bench a starting-calibre avatar for a slump or a bad relationship. More drama, but it can feel arbitrary, and it needs Argon's thresholds to stay fair.
- **c. Coach affinity (no coach entity exists, §1.1).**
  - **A (recommended):** a proxy from `Player_Person` (`teamwork`, `discipline`, `work_ethic`; `SchemaDefinitions.sql:420–435`), read through `PersonQueries`. The Baseball sim already reads it, so there's no Life reference. Neutral 50 = 0, with Math weights.
  - *B:* a real coach (a Players row plus a Relationships edge). It needs a `'Coach'` `type_enum` (a CHECK change, so DDL in the v15 window), or a `Friend` edge to new generated rows, which shifts world-gen streams and moves the guard. Defer to v15/Phase L.
  - *C:* no affinity term until B exists.
- **d. HS and College.** **Recommended:** no designations and no rows. Every rostered player is eligible each game day, and absent players simply aren't picked. College's active-20 cap (§5.1) never binds the sims (the lineup is 9, plus 4 SP and 5 RP), so it isn't modeled; Argon confirms (§9 Q9).

### D5. Coach beats (inactive, re-activated, forfeit) and the stress hit

- **A (recommended). Core events → a Narrative-layer writer.**
  - The events: `RosterDesignationChangedEvent(PlayerId, TeamId, IsActive, WasActive, LockedThroughDay)` (avatar only), published after the lock commits; `GameForfeitedEvent(SeasonYear, Tier byte, GameId, Phase byte, HomeTeamId, AwayTeamId, ForfeitMask byte, WinnerTeamId)`, published after its result commits. Both carry primitives only (13-4 §11.3 #1).
  - A new `Assets/Narrative/` subscriber writes the coach text with `NarrativeLogQueries.InsertText` in its own batch. It publishes `StressImpulseEvent` on inactivation and an optional relief on re-activation; the magnitudes are data values (§9 Q6).
  - Contacts: Pro `coach_reyes`, HS `coach_hs`, and a new `coach_college` row (placeholder name).
  - Texts are `strings.csv` keys with placeholder lines and no `{tokens}` (`NarrativeTokenResolver` is L-3's). Claude Sonnet 5.5 polishes them later.
  - It fires on transitions only: inactive after active, active after inactive, and each forfeit involving the avatar's team.
- **B.** `RosterManager` writes an engine flag (`Entity_Flags`), and gritty-event content keys on it. It reuses the dispatcher, but the dispatcher is weighted and polled, so the text can lag or never fire.
- **C.** Events only in 13-5a; texts and stress arrive with Sonnet's content pass.

### D6. Lock timing and persistence

- **A (recommended).**
  - **Subscription and window.** `RosterManager` subscribes after the tier sims and before the career (`GameManager.cs:447–516`). On the Sunday tick (`DayOfSeason % 7 == 0`) inside the Pro window (**Sundays 91–273**: the Sunday before opening day 93 through the last postseason Sunday; final 275), it runs after the Pro sim's Sunday flush, so performance inputs see the week's macro stats.
  - **The lock.** It computes all 32 teams' designations and upserts **768 rows in one batch** with `locked_through_day` = the next Sunday's absolute day. Inside the window that's always today + 7, and the window never crosses a season year.
  - **Boot.** At boot it hydrates an in-memory active mask (a `Version`ed ledger the sims cache per slot, the `AvailabilityLedger` pattern). If no row covers today inside the window (a crash between the day's commit and the lock's), it **reconciles** by locking now with the most recent Sunday as the nominal day.
  - **Default.** A player with no covering row is **active**. That covers the days before the first lock, the offseason, a harness avatar created mid-week, and every HS/College player.
  - **Game days.** The designation is an array read (0 B).
  - **Clock.** `TimeManager` doesn't change. SR-2a's week advance runs the Sunday tick like any other day; the lock never stops it, and only the beats' own consequences (a stress spike) might.
- **B.** Lock lazily before the week's first Pro game (Tuesday). That avoids the Sunday ordering, but it contradicts §7's Sunday lock, and the avatar's text would arrive on game day.

### D7. Default MC runtime budget and harness plan

- **Recommended: ≤ +10 s** on the default run, as the median paired difference over interleaved runs against the §0 baseline build on the owner's PC (the D10 / 13-4 §11.4 method).
- Default content (§5):
  - the rewritten `availability` suite;
  - forfeit fixtures for every tier on **1–2 shared scratch worlds** (13-4 Amendment 7);
  - the guard world with `RosterManager` wired (54 locks × 768 upserts);
  - designation, reload and attended fixtures;
  - allocation probes.
- No opt-in analysis is planned. Forfeit-frequency estimates are Argon's, report-only (§9 Q7).

### D8. Roster Status UI

- **A (recommended).** 13-5a ships a read-only `RosterStatusView` (this week's actives and inactives, the avatar's status, `locked_through_day`); the Roster Status screen (spec §13) is built once in 15c in the new design system. This mirrors 13-4's D6-B.
- **B.** A thin panel now (Sonnet, Neon review), replaced in 15c.

## 4. Outputs (13-5a, per the recommended options)

1. **`RosterRules`** (pure, allocation-free): `CanField` (D2e), the designation selector (D4a/b), and the tie and fill rules.
2. **`RosterManager`:** the Sunday lock (D6), coach AI inputs (performance from persisted stats, health from `AvailabilityLedger`, the affinity proxy from `PersonQueries`), boot hydrate and reconcile, the `CanField(teamId, day)` façade, the `RosterStatusView` DTO, and `RosterDesignationChangedEvent` after commit.
3. **`RosterQueries`** (the Data layer; SQL validated on a scratch v14, §0.4):
   - upsert: `INSERT INTO Roster_Designations (player_id, team_id, is_active, locked_through_day) VALUES (…) ON CONFLICT (player_id) DO UPDATE SET team_id = excluded.team_id, is_active = excluded.is_active, locked_through_day = excluded.locked_through_day`;
   - hydrate: `… FROM Roster_Designations d JOIN Teams t ON t.team_id = d.team_id JOIN Conferences c ON c.conference_id = t.conference_id WHERE c.tier = @tier AND d.locked_through_day >= @today`;
   - prior state by `player_id` (autoindex);
   - the latest lock: `MAX(d.locked_through_day)` with the same join.
4. **Call-up retirement, every sim:**
   - remove the replacement ratings, arsenal, discard-line accrual and `"Call-Up"` feed name;
   - an ineligible batter leaves the lineup, which is rebuilt by the allocation-free `LineupBuilder` twin only for affected teams (parity-checked against `Build`);
   - an ineligible starter is skipped, then an eligible reliever opens, else the team forfeits;
   - `MaybePullPitcher` skips ineligible relievers, and the starter stays in if none is left;
   - the macro postseason batter shadow is gone;
   - an absent or inactive avatar is never featured, and his team's game plays without him.
5. **Forfeits (D2):**
   - a status lane in the macro result queue, written at the Sunday flush;
   - a status-carrying `ScheduleQueries` result writer (`UPDATE Schedule_Games SET status = @status, home_score = @homeScore, away_score = @awayScore WHERE game_id = @gameId`, validated);
   - the attended forfeit in `CareerManager`;
   - `PlayoffManager` routed through `CanField`;
   - the double-forfeit award per D2d;
   - `GameForfeitedEvent` after each commit.
6. **Avatar (D4b, D5, D6):**
   - an inactive Pro avatar never parks; his team's games autopilot without him;
   - `GetMandatoryBlocksFor` drops his Game blocks for the locked week (practice only, spec §7);
   - the beats per D5.
7. **D3-A items:**
   - `ReclaimRegularSeasonGame` at boot, beside `ReclaimPostseasonGame`;
   - the `StartUndo` revert for regular-season attended games.
8. **Harness:** §5.
9. **Docs:** spec §7 and new §16 interpretations synced to the approved D-options (I16 lock and designations, I17 the forfeit contract, I18 the Gate 2 sanction scope); the MC skill; `progress.md`; the handoff (§8).

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings / 0 errors |
| Schema | `dotnet run --project Tools/SchemaValidator -c Release` (plus `validate_sqlite_schema` on a scratch copy before any new SQL) | 174/174 (no DDL) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 25/25 + the new clock fixtures below |
| GrittyEvents | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 (the contacts registry still loads with `coach_college`) |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | 470 + new checks; reds = exactly the 7 promotion suites; resolver byte-identical; **Pro guard byte-exact with `RosterManager` wired** (D1-A's 7 channels); every allocation line 0 B, including the new probes; 0 leaks; within D7 |
| 20× replay | default, `--no-build`, ×20, normalized | 20/20 byte-identical |

**Rewritten and new default suites:**

- **`availability` (rewritten, spec §15):** ledger semantics and the empty-ledger bit identity kept. Shadowing is replaced by:
  - an absent lineup batter is replaced by a real bench player who accrues PA;
  - the discard line is never written (it's gone);
  - an absent ace yields #2 with no call-up;
  - an absent avatar's games autopilot without him while the team plays.
- **`designations`:**
  - 768 rows per lock, 12/3/3 active per team;
  - `LineupBuilder` starters always active; the subset-greedy parity (random eligible subsets of spread-25 rosters, `Build` vs the twin, slot-for-slot);
  - the unhealthy fill (earliest return);
  - an injured ace inactive;
  - the avatar margin rule (D4b): a starting-calibre avatar stays active, a fringe one with poor inputs goes inactive;
  - idempotent re-lock; hydrate equals the last lock; boot reconcile after a simulated missed lock; no RNG moved.
- **`attrition`:** an active player absent mid-week isn't replaced by an inactive one; the lineup reshuffles from the actives; a team at exactly 9 eligible plays, and at 8 it forfeits.
- **`forfeits`, per tier:**
  - HS 7–0 and College/Pro 9–0, `status = 2`;
  - no stat row, start, PED cost or normalization;
  - W/L and runs in `Team_Seasons` per D2c;
  - regular-season double forfeit to the home team, postseason to the high seed;
  - the pitcher-only and the position-player-only failure;
  - the Red Dirt Series exhaustion forfeit still fires through `CanField` (13-4's checks still green);
  - `GameForfeitedEvent` once, after commit.
- **`attended-forfeit`:**
  - the avatar's game with a decimated opponent and with his own decimated team: no park, no `MicroGame` call, `CareerManager.RngSnapshot` unchanged;
  - the coach-text row and the stress event once each (D5-A).
- **`season-pipeline` additions:** D1-A's channels.
- **`attended-undo`** (D3-A): a cancelled and a rolled-back regular-season attended game replay with the same starters as a never-cancelled one (mutation-tested, as 13-4 S1 was).
- **Allocation probes:** warm macro game days with a reshuffled lineup, a macro forfeit day, an attended game with an absent reliever, and a locked-week game day: 0 B.

**CoreLoop clock fixtures:**

- an **inactive avatar never parks a game** (Pro week: `HasPendingGame` false all week, no Game blocks, the team's games played);
- a **forfeit never opens the at-bat view** (no pending game, one `GameForfeitedEvent`, one coach text, the clock not stopped);
- the lock fires on Sunday and a reload mid-week keeps the designations;
- a missed lock is reconciled at boot;
- the re-activation beat fires once after an inactive stint;
- (D3-A) a reload on a regular-season game day re-parks the same fixture (`game_id`).

**Spec §15 acceptance:** the availability suite rewritten; forfeit fixtures for every tier.

## 6. Definition of done

- [ ] §0 recorded (the baseline and its wall time).
- [ ] D1–D8 applied as approved; Argon's §10 reconciled; spec §7/§16 synced (I16–I18).
- [ ] §4 built; call-up retirement complete (no replacement ratings, discard line or `"Call-Up"` left in `Assets/`).
- [ ] §5 gates green; the Pro guard byte-exact with `RosterManager` wired; 20× replay 20/20; D7 met.
- [ ] Argon's independent review of the sim/persistence diff passed; findings fixed.
- [ ] Committed and pushed; merged into `p13/integration` by Opus after the review. **Not `main`.**
- [ ] Handoff (§8) + one `progress.md` line per author. **Then stop.**

## 7. Out of scope

- 13-6: `PipelineManager`, free agency and releases at the lock, the Draft, org-cap enforcement on roster moves.
- The Roster Status screen (D8) and booth calls about forfeits or benchings (14c).
- Polished coach copy (Sonnet's content pass) and a real coach entity (D4c-B, v15 window).
- The HS mercy rule (§16 open) and College academic eligibility (§16 open).
- Moving regular-season attended games onto a reload-stable derived stream: today they use the boot-seeded `CareerManager._rng`, so an attended game replayed after a reload draws differently. That's pre-existing; logged for R1-SAVE.
- A mid-week crash losing the week's unflushed macro results (pre-existing, 13-4 §11.3 #4; forfeits ride the same queue).
- Any band, resolver or guard change; any DDL; `main`.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built> · Commit: <sha> · Gates: build, Schema, CoreLoop, Gritty, MC (total, reds, guard, alloc, wall), 20× replay
Open: <unfinished items, risks, owner questions>
Recommended next: <slice> by <model> (reviewer: <model>)
```

Then add one compact line to [progress.md](../progress.md). Don't start the next slice.

## 9. Questions for Argon's Math check of this brief (answer in a new §10)

1. **Guard invariance (D1-A).**
   - Confirm the subset-greedy argument for `LineupBuilder` (§1.2 #2), including its secondary and out-of-position steps and the DH.
   - Confirm the starter argument: under `MinRestDays(Pro) = 3` on Tue/Thu/Sat and the D3 postseason slots, SP #3/#4 never start and no RP opens in the guard world.
   - Complete the invariance-channel list the harness must assert.
2. **Bench score (D4a-A).** The role weights for the 3 non-starter active slots: positional coverage (backup C, middle infield, outfield) against bat score. Ties go to player id ascending.
3. **Avatar inputs (D4b).**
   - *Performance:* the statistic (e.g. OPS or ERA against the tier mean), its shrinkage and its early-season minimum PA/IP.
   - *Health:* how an absence that overlaps the coming week counts, and whether rust counts.
   - *Affinity:* the proxy weights (D4c-A).
   - *Decision:* the margin comparison and its threshold. Give a fixture where each input flips the call.
4. **Unhealthy fill.** Confirm earliest return first; the tie key.
5. **Forfeit runs in the ranking (D2c).** Quantify how much a received 9–0 can move `RD_norm` and the national rank over 28 games (clamp ±5 runs/game), and say whether D2c-A is acceptable or B is needed.
6. **Stress and beats (D5).** The stress delta on going inactive, any relief on re-activation, and whether a second consecutive inactive week texts again.
7. **Forfeit exposure (report-only).** Expected forfeits per tier-season under the shipped absence sources, given HS's 11–14 position players. Confirm the pooled band samples (no ledger attached) can't move.
8. **D7 split.** Confirm the default additions fit ≤ 10 s, or propose a trim.
9. **College active-20 (D4d).** Confirm it never binds the macro sim or `MicroGame` and needs no model.
10. **Stream contract.** Confirm 13-5 needs no stream at all: skipped draws on a forfeit are acceptable; the double-forfeit award needs none under D2d-A (or specify the D2d-C key and tag).
