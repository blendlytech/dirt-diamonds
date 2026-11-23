# Task Brief: 13-5 — Sunday roster lock, attrition, forfeits and call-up retirement

**Status:** **D1–D8 APPROVED by the owner 2026-11-23, all as recommended:** D1-A guard world wired + 10 channels; D2 forfeit contract (a, b, c-A, d-A, e, f); D3-A all tiers under the narrow Gate 2 sanction; D4a-A, D4b-A, D4c-A, D4d; D5-A; D6-A; D7 ≤ +10 s; D8-A. **Math check DONE** (Gemini 4 Argon, §10: APPROVE WITH CHANGES); Amendments 1–7 and Opus corrections C1–C3 reconciled into §§1–5 (§10.5). Spec synced: [league_restructure.md](../design/league_restructure.md) §16-I16–I18. **The 13-5a build can start** from `p13/integration` with this brief merged (§0).
**Recommended model:** **13-5a** (engine, persistence, harness): Claude Opus 5.5 (TEAM.md §2, 13-5 row). Gemini 4 Argon: the Math check of this brief (designation scoring, avatar inputs, stress magnitude, forfeit exposure) and the independent review of the build.
**Reviewer:** Gemini 4 Argon independently reviews Opus's sim/persistence diff (TEAM.md §1a). Opus reviews Argon's Math record. Opus is the merge gate after review.
**Owner gate:** D1–D8 before the build (**approved 2026-11-23**). **D3-A's Gate 2 sanction is granted** for exactly its four changes. If the build finds that the Pro guard moves (D1), it stops and brings a sanction (13-3 precedent); the recommended D1-A is designed not to move it.
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
   - *Lineup:* `LineupBuilder` is a greedy pick over a candidate set. Over any subset that still contains its 9 picks, it picks the same 9 in the same positions (each step's best unchosen candidate is still present and still best). So if the 9 starters are always active, the lineup over the actives equals today's lineup, byte for byte. Argon proved this for every step, including secondary, out-of-position, DH and featured player (§10.1 Q1). It holds **only if the lock computes the 9 starters with `featuredSlot = -1`** (Amendment 2).
   - *Pitchers:* on Tue/Thu/Sat with `MinRestDays(Pro) = 3`, the ace starts Tue and Sat (gaps 3 and 4) and #2 starts Thu (gap 7). Each team's 64 regular-season starts split **43 / 21 / 0 / 0**, and no reliever ever opens. The D3 postseason slots reach only #1 and #2 too (§10.1 Q1). Making SP #4 and RP #4–#5 inactive therefore changes no pick.
   - *Counter-case:* a single scalar score with id tie-breaks (D4a-B) would, in the flat world, make the 12 lowest ids active. That can drop a positional starter, so other player ids bat and the ledger SHA moves even when the totals don't.
3. **"Reshuffle" is new code on the hot path.** The macro sim never rebuilds a lineup today. A per-game-day rebuild must allocate nothing (GEMINI §3), so `LineupBuilder` needs an allocation-free twin, rebuilt only for teams whose Initialize lineup has an ineligible member, with a parity check against `Build`.
4. **No DDL is needed.** One row per player holds the current week's designation. The re-activation beat needs the prior `is_active`, which the lock reads before it overwrites. "Has this week been locked?" is `MAX(locked_through_day)` over the tier's teams. A row **covers** a day only when `locked_through_day > today` (strict, Amendment 1). Sunday `s` writes `s + 7`, so the column reads as "valid until the next lock day, exclusive". Tier isn't a `Teams` column (the validator caught a `Teams.tier` join in this brief's first draft); the queries join `Team_Tiers`, as 13-4a's `PlayoffQueries.cs:44–46` does.
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

**Owner, 2026-11-23: all eight approved as recommended** (D1-A, D2 a/b/c-A/d-A/e/f, D3-A, D4a-A/b-A/c-A/d, D5-A, D6-A, D7, D8-A). The alternatives below stay for the record.

### D1. Call-up retirement and designations vs the sanctioned Pro guard

**Facts** (§1.2 #1–#2): the guard world never shadows, so call-up retirement can't touch it; designations can be built so the guard world's lineups and starters are provably unchanged.

- **A (recommended). Wire `RosterManager` into the guard world (production composition, as 13-4 wired the postseason) and prove the guard byte-exact.** No sanction is needed. The harness asserts these invariance channels:
  1. the guard (PA/H/ER and ledger SHA) is byte-exact;
  2. every team's 9 `LineupBuilder` starters are active every week, `LineupRebuildCount == 0`, and `OutOfPositionStarters` matches the unwired run;
  3. no inactive pitcher is ever selected: per team, regular-season starts split 43 / 21 / 0 / 0 with 0 RP openers, and all four short-rest and bullpen counters (regular and postseason) are 0;
  4. zero forfeits (no `status = 2` row, no `GameForfeitedEvent`), regular season and postseason;
  5. `LeagueSimulator.RngSnapshot` is unchanged across every lock, and the end-of-season snapshots and every postseason game seed match 13-4a;
  6. 27 locks a season (Sundays 91–273, 54 in the run), each writing 768 rows (576 active, 192 inactive) with `locked_through_day` = lock day + 7, and no stat, `Pitcher_Rest` or `Team_Seasons` write in the lock batch;
  7. the postseasons match 13-4a (2026: 11 series, 30 games, champion 323; 2027: 11 series, 32 games, champion 319);
  8. outside lock coverage (days 1–90 and 280–365), every Pro player reads as active and nothing is written;
  9. `RosterManager.Version` moves by exactly 1 per lock (54 in all) and never on other days;
  10. all ten 13-4 §10.1 Q7 postseason channels stay green.

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
  3. a regular-season parked-game reclaim on boot (every tier): the avatar team's earliest `status = 0` phase-0 row on or before today re-parks as the same fixture, `game_id` included (query validated, §4). Before it parks, the reclaim applies the same `CanField` and absent/inactive/autopilot checks as `OnDayAdvanced` (Amendment 6);
  4. the regular-season `StartUndo` revert on cancel or rollback, including `ShortRestStarts` and `BullpenGames`.

  *Why:* (3) and (4) fix defects inside Gate 2's own "one Friday game" loop (a reload on game day loses the game today). (1) and (2) apply one rule to every tier, as the spec says. Nothing changes without an absence.
- **B. Pro and College now; HS after Gate 2.** HS keeps the call-up and has no forfeits, behind a tier switch. (a) and (b) are deferred to post-Gate-2. Two absence models live side by side until then, and the HS reload defect stays in the demo tier.
- **C. Hybrid.** (3) and (4) for every tier now, as bug fixes. (1) and (2) for Pro and College now, HS after Gate 2. Pick this if the owner wants the HS rule frozen but the defects fixed.

### D4. Designations: who the coach makes active

- **a. NPC rule (Pro; Math values Argon's).**
  - **A (recommended):**
    - **Healthy** means not absent on any of the team's game days in the locked week (C1, §10.5). An absence that ends before the week's first game, such as one ending on a game-free Monday, doesn't count.
    - Over the healthy players, the 9 starters from **`LineupBuilder.Build(healthy, featuredSlot: -1)`** are always active. The lock never passes the avatar as featured (Amendment 2).
    - The other 3 active position slots go to the best remaining healthy players by the bench score in §10.1 Q2 (Amendment 3). It's three sequential greedy picks of `BatScore − 3·Rust` plus the largest still-uncovered coverage bonus (backup C 24, middle infield 18, outfield 15), plus 6 for a secondary position; ties go to `PlayerId` ascending.
    - SP: the top 3 healthy by `CompareQuality`. RP: the top 3 healthy by the same order. Pitcher quality is always the sims' **baked** stuff + control, exactly as `CompareQuality` reads it (C3).
    - When fewer healthy players exist than active slots, absent players fill them by **return day ascending, then role quality descending, then `PlayerId` ascending** (§10.1 Q4). They still can't play until they return, and an inactive player can't replace them (§7 attrition).
    - This is what makes D1-A provable.
  - *B:* one role-weighted scalar for all 12 slots. Simpler, but it can bench a positional starter, which moves the guard ledger in the flat world (§1.2 #2) and reshapes lineups in spread worlds.
- **b. The avatar.**
  - **A (recommended):** the same rule. Ratings decide who starts, and the avatar's **performance, health and coach-affinity** score decides only at the margin, against the bench candidates. A starting-calibre avatar is always active; a fringe avatar can lose his spot. A pitcher avatar is judged the same way at the SP #3/#4 and RP #3–#5 margin.
    - The margin score is the base role score plus **ΔP + ΔH + ΔA**, with the constants in §10.1 Q3 (Amendment 3).
    - ΔP: current-season OPS or ERA against the Pro tier, shrunk and clamped (min 20 PA / 18 outs, cap ±18).
    - ΔA: the affinity proxy, `0.12·(work_ethic−50) + 0.10·(discipline−50) + 0.08·(teamwork−50)`.
    - ΔH counts missed **game days** (C1): −12 per missed game in the locked week, minus 3 × rust for batters or 2 × rust for pitchers. An avatar who misses every game day that week goes to the unhealthy pool.
  - *B:* the avatar is fully scored, so the coach can bench a starting-calibre avatar for a slump or a bad relationship. More drama, but it can feel arbitrary, and it needs Argon's thresholds to stay fair.
- **c. Coach affinity (no coach entity exists, §1.1).**
  - **A (recommended):** a proxy from `Player_Person` (`teamwork`, `discipline`, `work_ethic`; `SchemaDefinitions.sql:420–435`), read through `PersonQueries`. The Baseball sim already reads it, so there's no Life reference. Neutral 50 = 0, with Math weights.
  - *B:* a real coach (a Players row plus a Relationships edge). It needs a `'Coach'` `type_enum` (a CHECK change, so DDL in the v15 window), or a `Friend` edge to new generated rows, which shifts world-gen streams and moves the guard. Defer to v15/Phase L.
  - *C:* no affinity term until B exists.
- **d. HS and College.** **Recommended:** no designations and no rows. Every rostered player is eligible each game day, and absent players simply aren't picked. College's active-20 cap (§5.1) never binds the sims (the lineup is 9, plus 4 SP and 5 RP), so it isn't modeled; Argon confirms (§9 Q9).

### D5. Coach beats (inactive, re-activated, forfeit) and the stress hit

- **A (recommended). Core events → a Narrative-layer writer.**
  - The events: `RosterDesignationChangedEvent(PlayerId, TeamId, IsActive, WasActive, LockedThroughDay)` (avatar only), published after the lock commits; `GameForfeitedEvent(SeasonYear, Tier byte, GameId, Phase byte, HomeTeamId, AwayTeamId, ForfeitMask byte, WinnerTeamId)`, published after its result commits. Both carry primitives only (13-4 §11.3 #1).
  - A new `Assets/Narrative/` subscriber writes the coach text with `NarrativeLogQueries.InsertText` in its own batch. It publishes `StressImpulseEvent` **+15.0** on inactivation and **−5.0** on re-activation (data values, §10.1 Q6).
  - Contacts: Pro `coach_reyes`, HS `coach_hs`, and a new `coach_college` row (placeholder name; not "Vance", which collides with the Vance County Vipers, §10.5).
  - Texts are `strings.csv` keys with placeholder lines and no `{tokens}` (`NarrativeTokenResolver` is L-3's). Claude Sonnet 5.5 polishes them later.
  - It fires on **transitions only**: inactive after active, active after inactive, and each forfeit involving the avatar's team. A second consecutive inactive week sends nothing. **A forfeit sends a coach text with no stress impulse** (Amendment 5).
  - `WasActive` comes from a row that covered the week just ending (`locked_through_day` = this lock day). A stale row, such as last season's final week, reads as the default (active), so a season's first lock never sends a spurious re-activation (C2).
- **B.** `RosterManager` writes an engine flag (`Entity_Flags`), and gritty-event content keys on it. It reuses the dispatcher, but the dispatcher is weighted and polled, so the text can lag or never fire.
- **C.** Events only in 13-5a; texts and stress arrive with Sonnet's content pass.

### D6. Lock timing and persistence

- **A (recommended).**
  - **Subscription and window.** `RosterManager` subscribes after the tier sims and before the career (`GameManager.cs:447–516`). On the Sunday tick (`DayOfSeason % 7 == 0`) inside the Pro window (**Sundays 91–273**: the Sunday before opening day 93 through the last postseason Sunday; final 275), it runs after the Pro sim's Sunday flush, so performance inputs see the week's macro stats.
  - **The lock.** It computes all 32 teams' designations and upserts **768 rows in one batch** with `locked_through_day` = the next Sunday's absolute day. Inside the window that's always today + 7, and the window never crosses a season year. A row covers a day only while **`locked_through_day > today`** (strict, Amendment 1), so the final lock (Sunday 273, written as 280) lapses on its own on day 280.
  - **Boot.** At boot it hydrates an in-memory active mask (a `Version`ed ledger the sims cache per slot, the `AvailabilityLedger` pattern). If no row covers today on a day in 91–279, it **reconciles** by locking now with the most recent Sunday as the nominal day. That's the crash between the day's commit and the lock's, including on Sunday itself: under the strict rule, last week's row stops covering on lock day.
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
- No opt-in analysis is planned. Forfeit-frequency estimates are Argon's, report-only (§10.1 Q7). Under the absence sources shipped today, organic forfeits are 0 in every tier: only the avatar can be absent.
- Argon's forecast is **+1.6–3.2 s raw (+1.9–4.0 s with a 1.25× reserve)**, provided the new suites share one or two generated v14 worlds (`Slice135World`; §10.1 Q8, Amendment 7). That's a forecast; the measured D7 gate decides.

### D8. Roster Status UI

- **A (recommended).** 13-5a ships a read-only `RosterStatusView` (this week's actives and inactives, the avatar's status, `locked_through_day`); the Roster Status screen (spec §13) is built once in 15c in the new design system. This mirrors 13-4's D6-B.
- **B.** A thin panel now (Sonnet, Neon review), replaced in 15c.

## 4. Outputs (13-5a, per the recommended options)

1. **`RosterRules`** (pure, allocation-free): `CanField` (D2e), the designation selector (D4a/b), and the tie and fill rules.
2. **`RosterManager`:** the Sunday lock (D6), coach AI inputs (performance from persisted stats, health from `AvailabilityLedger`, the affinity proxy from `PersonQueries`), boot hydrate and reconcile, the `CanField(teamId, day)` façade, the `RosterStatusView` DTO, and `RosterDesignationChangedEvent` after commit.
3. **`RosterQueries`** (the Data layer; SQL validated on a scratch v14, §0.4):
   - upsert: `INSERT INTO Roster_Designations (player_id, team_id, is_active, locked_through_day) VALUES (…) ON CONFLICT (player_id) DO UPDATE SET team_id = excluded.team_id, is_active = excluded.is_active, locked_through_day = excluded.locked_through_day`;
   - hydrate: `… FROM Roster_Designations d JOIN Team_Tiers tt ON tt.team_id = d.team_id WHERE tt.tier = @tier AND d.locked_through_day > @today` (strict, Amendment 1; the `Team_Tiers` join as in `PlayoffQueries.cs:44–46`; revalidated 2026-11-23, including the Sunday crash boundary);
   - prior state by `player_id` (autoindex);
   - the latest lock: `COALESCE(MAX(d.locked_through_day), 0)` with the same join.
4. **Call-up retirement, every sim:**
   - remove the replacement ratings, arsenal, discard-line accrual and `"Call-Up"` feed name;
   - an ineligible batter leaves the lineup, which is rebuilt by the allocation-free `LineupBuilder` twin only for affected teams (parity-checked against `Build`);
   - `LeagueSimulator` keeps per-slot teamwork (`_slotTeamwork`) after `Initialize`, so a reshuffled lineup re-bakes that game's defense byte with `PersonEffects.BakeTeamDefense`; `CountPedGame` and `CountPostseasonPedGame` credit the 9 who actually played (Amendment 5a);
   - an ineligible starter is skipped, then an eligible reliever opens, else the team forfeits;
   - `MaybePullPitcher` scans past ineligible relievers and keeps the current pitcher in if none is eligible. It captures the starter's count with `if (starterPitches < 0.0)`, not `bullpenUsed == 0` (Amendment 4). That also corrects opener games, which today backfill the last reliever's count (§10.5);
   - the macro postseason batter shadow is gone, and `SimulatePostseasonGame` checks `CanField` before recording any start (Amendment 5b);
   - an absent or inactive avatar is never featured: `MicroGame` filters its candidates to eligible players, passes `featuredSlot = IsEligible(humanSlot) ? humanSlot : -1`, and gates human batting and pitching the same way (Amendment 2). His team's game plays without him.
5. **Forfeits (D2):**
   - a status lane in the macro result queue, written at the Sunday flush;
   - a status-carrying `ScheduleQueries` result writer (`UPDATE Schedule_Games SET status = @status, home_score = @homeScore, away_score = @awayScore WHERE game_id = @gameId`, validated);
   - the attended forfeit in `CareerManager`;
   - `PlayoffManager` routed through `CanField`;
   - the double-forfeit award per D2d;
   - `GameForfeitedEvent` after each commit. Macro regular-season forfeits are buffered with the result lane and publish after `FlushAccumulated` commits; attended and postseason forfeits commit and publish on the game day (Amendment 5c).
6. **Avatar (D4b, D5, D6):**
   - an inactive Pro avatar never parks; his team's games autopilot without him;
   - `GetMandatoryBlocksFor` drops his Game blocks for the locked week (practice only, spec §7);
   - the beats per D5.
7. **D3-A items:**
   - `ReclaimRegularSeasonGame` at boot, beside `ReclaimPostseasonGame`, with the `CanField` and absent/inactive/autopilot checks of `OnDayAdvanced` (a new `IsAvatarActiveOn`; Amendment 6);
   - the `StartUndo` revert for regular-season attended games, snapshotting and restoring `ShortRestStarts` and `BullpenGames` beside the postseason counters (Amendment 4).
8. **Harness:** §5.
9. **Docs:** spec §7 and new §16 interpretations synced to the approved D-options (I16 lock and designations, I17 the forfeit contract, I18 the Gate 2 sanction scope); the MC skill; `progress.md`; the handoff (§8).

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings / 0 errors |
| Schema | `dotnet run --project Tools/SchemaValidator -c Release` (plus `validate_sqlite_schema` on a scratch copy before any new SQL) | 174/174 (no DDL) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 25/25 + the new clock fixtures below |
| GrittyEvents | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 (the contacts registry still loads with `coach_college`) |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | 470 + new checks; reds = exactly the 7 promotion suites; resolver byte-identical; **Pro guard byte-exact with `RosterManager` wired** (D1-A's 10 channels); every allocation line 0 B, including the new probes; 0 leaks; within D7 |
| 20× replay | default, `--no-build`, ×20, normalized | 20/20 byte-identical |

**Rewritten and new default suites:**

- **`availability` (rewritten, spec §15):** ledger semantics and the empty-ledger bit identity kept. Shadowing is replaced by:
  - an absent lineup batter is replaced by a real bench player who accrues PA;
  - the discard line is never written (it's gone);
  - an absent ace yields #2 with no call-up;
  - an absent avatar's games autopilot without him while the team plays.
- **`designations`** (on the shared `Slice135World`s, Amendment 7):
  - 768 rows per lock, 12/3/3 active per team;
  - `LineupBuilder` starters always active, computed unfeatured; the subset-greedy parity (random eligible subsets of spread-25 rosters, `Build` vs the twin, slot-for-slot);
  - Argon's Q2 bench fixture (every weight, the catcher-bucket clear, the `PlayerId` tie);
  - the Q4 unhealthy-fill fixture (`sp2`/`sp3` active, `sp1` inactive);
  - an injured ace inactive, and a player absent only on Monday stays active (C1);
  - the Q3 avatar margin fixtures (each of ΔP, ΔH and ΔA flips the call; the minimum-PA gate; a starting-calibre avatar stays active). Q3's flip 2a moves its return day to lock day + 3, so the absence covers Tuesday's game under C1 (ΔH = −12);
  - pitcher quality on baked ratings (C3); `WasActive` from the covering row only (C2);
  - idempotent re-lock; hydrate equals the last lock; boot reconcile after a simulated missed lock, including the Sunday crash boundary (Amendment 1); no RNG moved.
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
  - the coach-text row once, with no stress impulse (D5-A, Amendment 5);
  - a reload on a regular-season game day with a forfeit due resolves it with no park (Amendment 6).
- **`season-pipeline` additions:** D1-A's 10 channels.
- **Beats (D5-A, §10.1 Q6):** over three weekly locks, inactive → still inactive → active gives text and +15, then nothing, then text and −5.
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

---

## 10. Math check — Gemini 4 Argon, 2026-11-22

**Author / Reviewer:** Gemini 4 Argon (Math / calibration owner and primary independent reviewer, [TEAM.md](../TEAM.md) §1/§1a). **Branch / worktree:** `p13/13-5-mathcheck-argon` in `.worktrees/13-5-mathcheck-argon`, based on `917fc01` (`p13/integration` at `b7fbc4c`; `origin/p13/integration` at `37bed5a`; `C:` free `15.08 GB ≥ 2 GB`). **Scope:** §9 Q1–Q10, independent seam and SQL verification on `b7fbc4c`, Math review of D1–D8, and numbered amendment list for reconciliation. **Docs only:** no `Assets/`, `Tools/`, `.agents/` or schema edits.

**Overall Verdict:** **APPROVE WITH CHANGES** — all D1–D8 recommendations in this brief are mathematically sound, require zero new RNG draws or DDL, leave the 64/16/8 pooled tier-band samples untouched, and preserve the sanctioned Pro guard (`PA 81433 / H 18471 / ER 8803`, ledger SHA-256 `932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`) and resolver guard (`6d924228…`) byte-exact with `RosterManager` wired into the guard world (D1-A), subject to the 7 targeted specification refinements in §10.4 (most notably: using strict `d.locked_through_day > @today` instead of `>= @today` when `locked_through_day = sunday + 7` so a crash on Sunday before that Sunday's lock reconciles at boot instead of matching the prior week's row; computing the 9 automatic starters at the lock with unfeatured `featuredSlot = -1` while gating `MicroGame`'s featured player on `IsEligible(humanSlot)`; recomputing `_teamDefense[t]` and routing `SimulatePostseasonGame` and PED game counters through the active game-day lineup when a macro lineup reshuffles; and capturing `starterPitches` on `starterPitches < 0.0` in `MicroGame.MaybePullPitcher` when skipping an ineligible reliever at index `0`).

---

### 10.1 Answers to §9 Questions (Q1–Q10)

#### Q1. Guard invariance (D1-A): subset-greedy `LineupBuilder`, Pro starter gaps, and complete invariance channels
- **Verdict:** **Agree with two designation/featured-player specifications** — both the subset-greedy `LineupBuilder` theorem and the Pro starter-gap theorem hold across every regular-season and postseason game in the guard world, **provided**:
  1. At the Sunday lock (`RosterRules`), each team's 9 automatic starters are computed with **unfeatured `LineupBuilder.Build(healthy, featuredSlot: -1)`** (if `featuredSlot = avatarSlot` were passed at the lock, a fringe position-player avatar would be forced into the 9 automatic starters as the featured DH via `LineupBuilder.cs:80–83` and could never be evaluated at the D4b-A bench margin).
  2. At game time in `MicroGame` (`MicroGame.cs:647–656, 1169–1172`), `candidates` is filtered to **eligible** position players (`!IsAbsent && IsActive`) and `featuredSlot` is passed as **`IsEligible(humanSlot) ? humanSlot : -1`** (otherwise an absent or inactive avatar passed by `CareerManager.PlayPendingGame` at `CareerManager.cs:1585` would be re-promoted to featured DH at `MicroGame.cs:1169–1172`).
- **Exact rule:**
  - *Sunday lock position-player rule (D4a-A):* Let $C$ be a Pro team's 15 position players and $H \subseteq C$ be those healthy at the lock ($d_{\text{abs}} = 0$, i.e. `StateFor(playerId, lockDay + 1) != SlotAvailability.Absent`). When $|H| \ge 9$, run `LineupBuilder` with `featuredSlot = -1` over $H$; its 9 chosen slots $L_9(H)$ are marked `is_active = 1`. The remaining 3 active position-player slots are chosen from $H \setminus L_9(H)$ by Q2's bench score (or Q3's margin score when a non-starter avatar is in $H \setminus L_9(H)$).
  - *Game-day lineup-rebuild gate (`LeagueSimulator`):* Before playing a macro game (regular season or postseason), check whether all 9 slots of team $t$'s Initialize `_lineupSlots[t * 9 .. t * 9 + 8]` are eligible today (`!IsAbsent(slot) && IsActive(slot)`). If all 9 are eligible, use `_lineupSlots` and `_teamDefense[t]` directly (`0 B`, `0` rebuilds). Only when $\ge 1$ Initialize starter is ineligible does the sim invoke the allocation-free `LineupBuilder.BuildInto` twin over team $t$'s eligible position players and recompute that game's effective team-defense byte via `PersonEffects.BakeTeamDefense`.
  - *Complete 10-channel guard-invariance list for `season-pipeline` (the brief's 7 channels plus 3 integration seams):*
    1. **Sanctioned Pro guard byte-exact:** Season 1 (`2026`) `PA 81433 / H 18471 / ER 8803` and two-season phase-0 counting-ledger SHA-256 `932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84` (`Slice133Suites.cs:31–34`).
    2. **Lineup-starter coverage & zero rebuilds:** On every lock across both seasons, all 32 teams' 9 Initialize `LineupBuilder` starters are `is_active = 1`, `LineupRebuildCount == 0`, and `OutOfPositionStarters` matches the unwired baseline.
    3. **Pitcher pick invariance:** No inactive pitcher (`is_active = 0`) is ever selected in any regular-season or postseason game; per-team regular-season starts remain `#1 SP = 43, #2 SP = 21, #3 SP = 0, #4 SP = 0, RP = 0`, and `ShortRestStarts == 0, BullpenGames == 0, PostseasonShortRestStarts == 0, PostseasonBullpenGames == 0`.
    4. **Zero forfeits:** `SELECT COUNT(*) FROM Schedule_Games WHERE status = 2` is `0` across both regular seasons (`phase = 0`, 1,024 games/season) and both postseasons (`phase = 2`), and `0` `GameForfeitedEvent`s publish.
    5. **RNG stream invariance:** `LeagueSimulator.RngSnapshot` before and after each of the 54 Sunday locks is identical, and end-of-Season-1 / end-of-Season-2 `RngSnapshot` and all `PostseasonStreams` game seeds match the 13-4a baseline.
    6. **Lock cadence & batch isolation:** Exactly **27 locks per season** (`54` total over 730 days, on `DayOfSeason ∈ {91, 98, 105, ..., 273}`), each persisting `768` `Roster_Designations` rows (`576` active = `32 × (12 + 3 + 3)`, `192` inactive = `32 × (3 + 1 + 2)`) with `locked_through_day = lockAbsoluteDay + 7`, and zero `Batting_Stats`, `Pitching_Stats`, `Pitcher_Rest`, or `Team_Seasons` writes inside the lock transaction.
    7. **Postseason outcome invariance:** `2026` Pro postseason (`11` series, `30` games, champion `323`) and `2027` Pro postseason (`11` series, `32` games, champion `319`) match 13-4a §12.1 byte-for-byte.
    8. **[ADDED] Offseason / pre-window default-active transition:** On days outside the active lock coverage (`DayOfSeason ∈ [1, 90]` and `DayOfSeason ∈ [280, 365]`, where `MAX(locked_through_day) = seasonBase + 280 <= today`), `RosterManager` reports all 768 Pro players as active (`IsActive == true`) with no offseason lock writes.
    9. **[ADDED] Version-gated cache stability:** `RosterManager.Version` increments by exactly `+1` per Sunday lock (`54` total in the 730-day run) and `0` on non-lock days.
    10. **[ADDED] All 10 13-4 §10.1 Q7 postseason invariance channels remain green** alongside the 13-5 channels.
- **Derivation:**
  1. *Proof of the subset-greedy `LineupBuilder` property across all 4 steps (`LineupBuilder.cs:48–95`):*
     Let $C$ be a candidate set with $|C| \ge 9$, ordered by the strict total order $a \succ b \iff (\text{BatScore}_a > \text{BatScore}_b) \lor (\text{BatScore}_a = \text{BatScore}_b \land \text{PlayerId}_a <_{\text{ord}} \text{PlayerId}_b)$ (`LineupBuilder.cs:48–52`; strict because `PlayerId` is a PK). Let $L(C) = \{c_1^*, \dots, c_9^*\}$ be the 9 candidates chosen sequentially by `LineupBuilder.Build(C, featuredSlot)` at steps $t = 1, \dots, 9$, and let $S$ be any subset satisfying $L(C) \subseteq S \subseteq C$. The restriction of $\succ$ to $S$ preserves the relative order of every pair in $S$. By strong induction on $t \in \{1, \dots, 9\}$, suppose the chosen dictionary $U_{t-1} \subseteq L(C) \subseteq S$ and the `open` position list before step $t$ are identical on $C$ and $S$:
     - **Step 1 — Primary and Secondary fielding passes (`LineupBuilder.cs:56–69`, over `FieldingPositions.Field = [C, 1B, 2B, 3B, SS, LF, CF, RF]`):** For position $p_t$:
       - If the primary scan `FirstUnchosen(order, U_{t-1}, c => c.Primary == p_t)` on $C$ succeeded with $c_t^* \in L(C)$, then $c_t^* \in S \setminus U_{t-1}$ has `Primary == p_t`, and because $S \setminus U_{t-1} \subseteq C \setminus U_{t-1}$, no $x \in S \setminus U_{t-1}$ with `Primary == p_t` can satisfy $x \succ c_t^*$. Hence the primary scan on $S$ selects the exact same $c_t^*$.
       - If the primary scan on $C$ returned `-1`, then `{c ∈ C \ U_{t-1} : c.Primary == p_t} = ∅`, so its subset `{c ∈ S \ U_{t-1} : c.Primary == p_t}` is also empty and the primary scan on $S$ also returns `-1`. Both runs then execute the secondary scan `FirstUnchosen(order, U_{t-1}, c => c.Secondary == p_t)`: if $C$ found $c_t^* \in L(C)$, then $c_t^* \in S \setminus U_{t-1}$ is likewise the $\succ$-maximum secondary match in $S \setminus U_{t-1}$; if $C$ returned `-1`, $S$ also returns `-1` and both append $p_t$ to `open`.
     - **Step 2 — Out-of-position pass (`LineupBuilder.cs:71–77`):** For each $p \in \text{open}$, $C$ selects $c_t^* = \max_{\succ}(C \setminus U_{t-1})$. Since $c_t^* \in L(C) \subseteq S \implies c_t^* \in S \setminus U_{t-1} \subseteq C \setminus U_{t-1}$, $c_t^*$ is also $\max_{\succ}(S \setminus U_{t-1})$, so $S$ assigns $c_t^*$ to $p$ and increments `outOfPosition` identically.
     - **Step 3 — DH and Featured-Player pass (`LineupBuilder.cs:79–87`):** After the 8 fielding positions are filled ($U_8$ identical on $C$ and $S$):
       - If `featuredSlot` is present in $C$, then `LineupBuilder.Build(C, featuredSlot)` always includes `featuredSlot` in $L(C)$ (either already in $U_8$ or added at line 82), so `featuredSlot` $\in L(C) \subseteq S$ (`featuredIsCandidate == true` on both $C$ and $S$). Since $U_8$ is identical on $C$ and $S$, `chosen.ContainsKey(featuredSlot)` is identical: if `false`, both assign `(featuredSlot, null)`; if `true`, both execute line 86.
       - When line 86 executes (`featuredSlot` absent or already in $U_8$), $C$ picks $c_9^* = \max_{\succ}(C \setminus U_8) \in L(C) \subseteq S$, which is therefore also $\max_{\succ}(S \setminus U_8)$.
     - **Step 4 — Batting-order sort (`LineupBuilder.cs:89–95`):** Because the 9 `(Slot, Position)` entries in `chosen` and `outOfPosition` are identical, sorting `chosen.Keys` ascending produces identical `Slots`, `Positions`, `OutOfPosition`, and `TeamDefense` bytes. $\blacksquare$
  2. *Proof of the Pro starter-gap property across regular season and D3 postseason slots (`StarterSelection.cs:40–70`):*
     In the guard world, every Pro team has 4 SP ($S_1 \succ S_2 \succ S_3 \succ S_4$ in `StarterSelection.CompareQuality` order, strict by distinct `PlayerId`s) and 5 RP ($R_1 \succ \dots \succ R_5$), with `MinRestDays(Pro) = 3` and zero absences. Under D4a-A, $\{S_1, S_2, S_3\}$ and $\{R_1, R_2, R_3\}$ are active every week and $S_4, R_4, R_5$ are inactive.
     - **Regular season (64 games: Weeks $w = 0 \dots 20$ on Tue $93+7w$, Thu $95+7w$, Sat $97+7w$, plus Week 22 Tue $240$):**
       By induction on $w$, $S_1$ starts every Tuesday (gap $(93+7w) - (90+7w) = 3 \ge 3$, or $\infty$ at $w=0$) and every Saturday (gap $(97+7w) - (93+7w) = 4 \ge 3$), while on Thursday $S_1$ has gap $(95+7w) - (93+7w) = 2 < 3$ and $S_2$ starts (gap $(95+7w) - (88+7w) = 7 \ge 3$, or $\infty$ at $w=0$). On Week 22 Tuesday `240`, $S_1$ starts (gap $240 - 237 = 3 \ge 3$). Total regular-season starts: $S_1 = 43$, $S_2 = 21$, $S_3 = S_4 = 0$, RP openers $= 0$.
     - **Postseason (D3 slots: WC `244/247/249`, Div `251/254/256`, CF `258/261/263`, WS `265/268/270/272/275`; `usedInSeries` is all-`false` for Pro at `PlayoffManager.cs:446–447`):**
       In every Bo3 round (Sat $d$, Tue $d+3$, Thu $d+5$), $S_1$'s prior start was at latest Tuesday $d-4$, so $S_1$ starts Game 1 (Sat $d$, gap $\ge 4 \ge 3$) and Game 2 (Tue $d+3$, gap $3 \ge 3$), while $S_2$ starts Game 3 if played (Thu $d+5$, $S_1$ gap $2 < 3$, $S_2$ gap $\ge 7 \ge 3$). In the Bo5 World Championship, $S_1$ starts Game 1 (Sat 265, gap $\ge 4$), Game 2 (Tue 268, gap $3$), Game 4 if played (Sat 272, gap $272-268 = 4 \ge 3$), and Game 5 if played (Tue 275, gap $275-272 = 3 \ge 3$), while $S_2$ starts Game 3 (Thu 270, $S_1$ gap $2 < 3$, $S_2$ gap $\ge 7 \ge 3$). Entering Season 2 Opening Day (`365 + 93 = 458`), all starters have gap $\ge 458 - 275 = 183 \ge 3$. Thus $S_3, S_4$, and $R_1 \dots R_5$ make **zero** appearances in the guard world, and deactivating $S_4$ and $R_4, R_5$ changes zero picks. $\blacksquare$

#### Q2. Bench score for the 3 non-starter active position-player slots (D4a-A)
- **Verdict:** **Agree** — select the 3 active bench slots sequentially ($b = 0, 1, 2$) from the healthy non-starters $B = H_{\text{pos}} \setminus L_9(H_{\text{pos}})$ using an exact integer score combining `BatScore` (net of any injury rust), uncovered positional-bucket coverage (`Backup C`, `Middle Infield`, `Outfield`), and a two-position utility bonus, with ties broken by `PlayerId` ordinal ascending.
- **Exact rule (data constants in `RosterRules`):**
  - * Positional coverage buckets for candidate $c = (\text{PlayerId}, \text{BatScore}, \text{Primary}, \text{Secondary}, \text{Rust})$:*
    - $\text{IsBackupC}(c) \iff c.\text{Primary} = \text{C} \lor c.\text{Secondary} = \text{C}$.
    - $\text{IsMiddleInfield}(c) \iff c.\text{Primary} \in \{\text{2B}, \text{SS}\} \lor c.\text{Secondary} \in \{\text{2B}, \text{SS}\}$.
    - $\text{IsOutfield}(c) \iff c.\text{Primary} \in \{\text{LF}, \text{CF}, \text{RF}\} \lor c.\text{Secondary} \in \{\text{LF}, \text{CF}, \text{RF}\}$.
  - *Data constants:*
    - `BackupCatcherCoverageWeight = 24` ($8$ points per batting attribute).
    - `MiddleInfieldCoverageWeight = 18` ($6$ points per batting attribute).
    - `OutfieldCoverageWeight = 15` ($5$ points per batting attribute).
    - `SecondaryPositionBonus = 6` ($2$ points per batting attribute when `c.Secondary != null`).
    - `RustBatterMultiplier = 3` (each point of injury `RatingPenalty` docks all 3 batting ratings in `LeagueSimulator.ApplyRust`, so $-3 \cdot \text{Rust}$ on `BatScore`).
  - *Sequential 3-slot greedy selection ($b = 0, 1, 2$):*
    Initialize bench coverage flags $\text{cov}_C = \text{false}$, $\text{cov}_{\text{MI}} = \text{false}$, $\text{cov}_{\text{OF}} = \text{false}$. For each bench slot $b \in \{0, 1, 2\}$ (while unpicked candidates remain in $B$), evaluate each unpicked $c \in B$:
    $$\text{CoverageBonus}(c;\, \text{cov}) = \max\Big(24\,\mathbb{I}[\neg\text{cov}_C \land \text{IsBackupC}(c)],\; 18\,\mathbb{I}[\neg\text{cov}_{\text{MI}} \land \text{IsMiddleInfield}(c)],\; 15\,\mathbb{I}[\neg\text{cov}_{\text{OF}} \land \text{IsOutfield}(c)]\Big),$$
    $$\text{BenchScore}(c;\, \text{cov}) = \big(\text{BatScore}(c) - 3 \cdot \text{Rust}(c)\big) + \text{CoverageBonus}(c;\, \text{cov}) + 6\,\mathbb{I}[c.\text{Secondary} \ne \text{null}].$$
    Pick $c^* \in B$ maximizing $\text{BenchScore}(c;\, \text{cov})$ (ties broken by `string.CompareOrdinal(a.PlayerId, b.PlayerId) < 0`), mark $c^*$ active, remove $c^*$ from $B$, and update:
    $$\text{cov}_C \gets \text{cov}_C \lor \text{IsBackupC}(c^*), \quad \text{cov}_{\text{MI}} \gets \text{cov}_{\text{MI}} \lor \text{IsMiddleInfield}(c^*), \quad \text{cov}_{\text{OF}} \gets \text{cov}_{\text{OF}} \lor \text{IsOutfield}(c^*).$$
  - *Fixture exercising every Q2 weight:*
    Given 6 healthy non-starter bench candidates on a Pro team (all `Rust = 0`):
    - `b_c1`: `BatScore = 135` (`45/45/45`), `Primary = C, Secondary = null` $\implies$ Pick 1 score $135 + 24 + 0 = \mathbf{159}$.
    - `b_c2`: `BatScore = 152`, `Primary = C, Secondary = null` (a second backup catcher) $\implies$ if `b_c2` is present instead of or alongside `b_c1`, the higher-bat catcher `b_c2` takes Pick 1 at $152 + 24 = \mathbf{176}$, setting $\text{cov}_C = \text{true}$; on Picks 2–3 the second catcher `b_c1` drops to $135 + 0 = \mathbf{135}$ and is not redundantly activated!
    - `b_mi`: `BatScore = 140`, `Primary = SS, Secondary = 2B` $\implies$ score $140 + 18 + 6 = \mathbf{164}$ (wins Pick 2 over `b_1b`, sets $\text{cov}_{\text{MI}} = \text{true}$).
    - `b_of`: `BatScore = 142`, `Primary = CF, Secondary = null` $\implies$ score $142 + 15 + 0 = \mathbf{157}$ (wins Pick 3 over `b_1b` at `156`, sets $\text{cov}_{\text{OF}} = \text{true}$).
    - `b_1b`: `BatScore = 156`, `Primary = 1B, Secondary = null` $\implies$ score $156 + 0 + 0 = \mathbf{156}$ (inactive, beat by `b_c2` [`176`], `b_mi` [`164`], `b_of` [`157`]; if `b_1b` instead had `BatScore = 158`, $158 > 157$ flips Pick 3 to `b_1b`).
    - `b_3b_util`: `BatScore = 151`, `Primary = 3B, Secondary = 1B` $\implies$ score $151 + 0 + 6 = \mathbf{157}$ (ties `b_of` at `157`; broken deterministically by `PlayerId` ascending: `"b_3b_util" < "b_of"` wins, or `"b_z_3b_util" > "b_of"` loses).
- **Derivation:**
  1. *Disjoint bucket structure in `LeagueGenerator.SecondaryNeighbours` (`LeagueGenerator.cs:88–98`):* In `SecondaryNeighbours`, `C` maps to `{1B}`, middle/corner infielders `{2B, 3B, SS}` map only to `{1B, 2B, 3B, SS}`, and `{1B, LF, CF, RF}` map only to `{1B, 3B, LF, CF, RF}`. Thus no generated player ever has `C` as a secondary position, no outfielder/first-baseman can cover `2B/SS`, and no infielder (except `1B`) can cover the outfield. When an active starter goes down mid-week (§7 attrition), avoiding an out-of-position penalty in `LineupBuilder` requires one backup catcher, one middle-infield backup, and one outfield backup on the 3-man active bench.
  2. *Why sequential uncovered-bucket bonuses beat static position weights:* Under `LeagueGenerator.DepthPositionWeights` (`C` weight `3/13`, `LeagueGenerator.cs:67–77`), $\approx 14\%$ of Pro teams roll $\ge 2$ backup catchers among their 6 bench players. A static `+24` catcher weight would activate 2 or 3 backup catchers on the same 3-man bench in flat/near-flat worlds; clearing $\text{cov}_C$ after Pick 1 guarantees the 3 active bench slots diversify across `(Backup C, Middle Infield, Outfield)` while still allowing a $+15\text{ to }+25$ `BatScore` slugger to override coverage on merit.

#### Q3. Avatar inputs at the margin (D4b-A, D4c-A): Performance, Health, Affinity, Decision threshold, and flip fixtures
- **Verdict:** **Agree** — keep D4b-A's core-vs-margin protection (a healthy avatar in the 9 unfeatured `LineupBuilder` starters or the top 2 healthy SP / top 2 healthy RP is always active) and evaluate a fringe avatar at the 3-slot bench margin (or 3rd-active SP/RP margin) by adding $\Delta M = \Delta P + \Delta H + \Delta A$ to the avatar's base role score.
- **Exact rule (data constants in `RosterRules`):**
  1. **Core protection (D4b-A):**
     - Partition rostered players of the avatar's role into **Healthy for the week** (for NPCs: $d_{\text{abs}} = 0$, i.e., `UntilDay <= lockDay + 1`; for the avatar: $d_{\text{abs}} < 6$, i.e., `UntilDay <= lockDay + 6`, available for at least one day of the Mon–Sat game week) and **Unhealthy for the week** (NPCs with $d_{\text{abs}} \ge 1$; avatar with $d_{\text{abs}} = 6$, absent across the entire Mon–Sat game week).
     - Let $H_0$ be the players with $d_{\text{abs}} = 0$ (no overlapping absence on Monday `lockDay + 1`). If the avatar has $d_{\text{abs}} = 0$ and:
       - is a position player chosen among the **9 unfeatured `LineupBuilder.Build(H_0, featuredSlot: -1)` starters**, OR
       - is a starting pitcher in the **top 2 of $H_0$** by base `StarterSelection.CompareQuality`, OR
       - is a relief pitcher in the **top 2 of $H_0$** by base `StarterSelection.CompareQuality`,
       then the avatar is **automatically active** (`is_active = 1`).
  2. **Performance term ($\Delta P_{\text{bat}}$ and $\Delta P_{\text{pit}}$):**
     Read from persisted current-season (`seasonYear`) `Batting_Stats` / `Pitching_Stats` rows and tier totals (`LoadLeagueBattingTotals(seasonYear, LeagueTier.Pro)` / `LoadLeaguePitchingTotals(seasonYear, LeagueTier.Pro)` — already flushed before `RosterManager` runs on Sunday per D6-A):
     - *Data constants:* `PerfMinPa = 20`, `PerfPaShrink = 60`, `PerfMinOuts = 18` (`6.0 IP`), `PerfOutsShrink = 54` (`18.0 IP`), `PerfRelativeClamp = 0.50`, `PerfMaxDelta = 18.0`.
     - *Batter (`PA >= 20` and `OPS_tier > 1e-6`, else `0.0`):*
       $$w_{\text{PA}} = \frac{\text{PA}}{\text{PA} + 60}, \qquad r_{\text{bat}} = \text{clamp}\!\left(\frac{\text{OPS}_{\text{avatar}} - \text{OPS}_{\text{tier}}}{0.50 \cdot \text{OPS}_{\text{tier}}},\; -1.0,\; +1.0\right), \qquad \Delta P_{\text{bat}} = 18.0 \cdot w_{\text{PA}} \cdot r_{\text{bat}}.$$
     - *Pitcher (`OutsRecorded >= 18` and `ERA_tier > 1e-6`, else `0.0`, where $\text{ERA} = 27.0 \cdot \text{ER} / \text{OutsRecorded}$):*
       $$w_{\text{IP}} = \frac{\text{OutsRecorded}}{\text{OutsRecorded} + 54}, \qquad r_{\text{pit}} = \text{clamp}\!\left(\frac{\text{ERA}_{\text{tier}} - \text{ERA}_{\text{avatar}}}{0.50 \cdot \text{ERA}_{\text{tier}}},\; -1.0,\; +1.0\right), \qquad \Delta P_{\text{pit}} = 18.0 \cdot w_{\text{IP}} \cdot r_{\text{pit}}.$$
  3. **Health term ($\Delta H_{\text{bat}}$ and $\Delta H_{\text{pit}}$):**
     - *Data constants:* `AbsenceDayPenalty = 6.0` per missed Mon–Sat day; `RustBatterMultiplier = 3.0`; `RustPitcherMultiplier = 2.0`.
     - For an avatar with $0 \le d_{\text{abs}} \le 5$, where $d_{\text{abs}} = \text{clamp}\!\big(\text{UntilDay} - (\text{lockDay} + 1),\; 0,\; 6\big)$ is the number of absent days in the Monday–Saturday window `[\text{lockDay} + 1, \text{lockDay} + 6]`, let $d_0 = (\text{lockDay} + 1) + d_{\text{abs}}$ be the avatar's first available day of the week, and let $\text{rust} = \text{RatingPenalty}$ if `StateOn(d_0) == SlotAvailability.Rusty` (`d_0 >= UntilDay && d_0 < PenaltyUntilDay`) else `0`. Then:
       $$\Delta H_{\text{bat}} = -6.0 \cdot d_{\text{abs}} - 3.0 \cdot \text{rust}, \qquad \Delta H_{\text{pit}} = -6.0 \cdot d_{\text{abs}} - 2.0 \cdot \text{rust}.$$
       *(Note: in Q2's bench score, `-3 * Rust(c)` is already included in `BenchScore`; for the avatar, apply `-3.0 * rust` once—either inside `BenchScore` with $\Delta H_{\text{bat}} = -6.0 \cdot d_{\text{abs}}$ or in $\Delta H_{\text{bat}}$ with un-rusted `BenchScore`, never double-counted!)*
  4. **Coach Affinity proxy ($\Delta A$, D4c-A):**
     Read from `Player_Person` via `PersonQueries.TryGet(avatarId, out PersonRow person)` (defaulting to neutral `50 / 50 / 50` when `Persons is null` or no row exists):
     - *Data constants:* `AffinityWorkEthicWeight = 0.12`, `AffinityDisciplineWeight = 0.10`, `AffinityTeamworkWeight = 0.08` (sum $= 0.30$, range $[-15.0, +15.0]$, exact zero at `50/50/50`).
     $$\Delta A = 0.12\,(\text{work\_ethic} - 50) + 0.10\,(\text{discipline} - 50) + 0.08\,(\text{teamwork} - 50).$$
  5. **Margin decision rule and threshold:**
     - *Position-player margin (3 active bench slots over $B = (H_0 \setminus L_9(H_0)) \cup \{\text{avatar if } 0 \le d_{\text{abs}} \le 5 \text{ and not in } L_9(H_0)\}$):*
       In each of the 3 greedy bench picks ($b = 0, 1, 2$), an NPC candidate $c \in B$ scores $\text{Score}(c;\, \text{cov}) = \text{BenchScore}(c;\, \text{cov})$ (which includes $-3 \cdot \text{Rust}(c)$), while the avatar scores:
       $$\text{Score}(\text{avatar};\, \text{cov}) = \text{BenchScore}_{\text{unrusted}}(\text{avatar};\, \text{cov}) + \Delta P_{\text{bat}} + \Delta H_{\text{bat}} + \Delta A.$$
       At each pick, the highest score wins (ties broken by `PlayerId` ordinal ascending). Equivalently, against the 3rd-best NPC bench candidate's score $S_{\text{cut}} = S_{\text{NPC},(3)}$, the decision threshold is **$\text{Score}(\text{avatar}) - S_{\text{cut}} \ge 0$** (strict `> 0` when $\text{avatar.PlayerId} > \text{npc}_{(3)}.\text{PlayerId}$).
     - *Pitcher margin (3rd active SP slot over healthy non-top-2 SPs, or 3rd active RP slot over healthy non-top-2 RPs):*
       The top 2 no-overlap healthy non-avatar pitchers (`d_abs == 0`) by base `StarterSelection.CompareQuality` take active slots `1..2`. For the 3rd active slot, each remaining healthy NPC pitcher $c$ scores $\text{Score}(c) = (\text{PitStuff}_c + \text{PitControl}_c) - 2.0 \cdot \text{Rust}(c)$, while the avatar scores:
       $$\text{Score}(\text{avatar}) = (\text{PitStuff}_{\text{avatar}} + \text{PitControl}_{\text{avatar}}) + \Delta P_{\text{pit}} + \Delta H_{\text{pit}} + \Delta A.$$
       The highest score takes active slot `3` (exact score ties broken by `PitStamina` descending, then `PlayerId` ordinal ascending, reducing identically to `StarterSelection.CompareQuality` when $\Delta P = \Delta H = \Delta A = 0$).
  6. **Fixtures where each input independently flips the call:**
     Set up a Pro team where the 3rd active bench slot cutoff NPC (`npc_bench_3`) has `BatScore = 150` (`50/50/50`, `Primary = 1B, Secondary = null`, `Rust = 0`, all 3 coverage buckets already covered or no bench candidate covers an uncovered bucket $\implies S_{\text{cut}} = \mathbf{150.0}$), and a fringe position-player avatar (`avatar_fringe`, `Primary = 1B, Secondary = null`) has base `BatScore = 152` (`52/50/50`, not in the 9 `LineupBuilder` starters because the starting `1B` and `DH` have `BatScore = 165`):
     - **Baseline (neutral inputs: `PA = 0`, `d_abs = 0, rust = 0`, person `50/50/50`):** $\text{Score} = 152.0 + 0 + 0 + 0 = \mathbf{152.0 > 150.0} \implies$ **ACTIVE** (margin $+2.0$).
     - **Flip 1 — Performance alone flips the call (`ACTIVE → INACTIVE` and `INACTIVE → ACTIVE`):**
       - *Slump (`PA = 60`, `OPS = 0.490` vs `OPS_tier = 0.700`, $-30\%$):* $w_{\text{PA}} = 60 / 120 = 0.50$, $r_{\text{bat}} = -0.30 / 0.50 = -0.60 \implies \Delta P_{\text{bat}} = 18.0 \times 0.50 \times (-0.60) = \mathbf{-5.4}$. Total score $= 152.0 - 5.4 = \mathbf{146.6 < 150.0} \implies$ **INACTIVE** (margin $-3.4$).
       - *Early-season minimum gate (`PA = 16 < 20` with the same `OPS = 0.490`):* $\Delta P_{\text{bat}} = \mathbf{0.0} \implies$ score stays **`152.0 > 150.0` (ACTIVE)**.
       - *Hot streak on a trailing avatar (`BatScore = 148 < 150`, `PA = 60`, `OPS = 0.910` vs `0.700`, $+30\%$):* $\Delta P_{\text{bat}} = \mathbf{+5.4} \implies$ score $= 148.0 + 5.4 = \mathbf{153.4 > 150.0} \implies$ **flips `INACTIVE → ACTIVE`**.
       - *(Pitcher analogue at SP `#3/#4`: cutoff SP `#3` has `Stuff + Control = 100.0`; fringe SP `#4` avatar has `Stuff + Control = 102.0`; at `54 outs = 18.0 IP, 14 ER → ERA 7.00 vs 4.20`, $w_{\text{IP}} = 0.50, r_{\text{pit}} = -1.0 \implies \Delta P_{\text{pit}} = \mathbf{-9.0}$, flipping $102.0 \to \mathbf{93.0 < 100.0}$, **INACTIVE**.)*
     - **Flip 2 — Health alone flips the call (`ACTIVE → INACTIVE`, via either overlapping absence or injury rust):**
       - *Sub-fixture 2a (overlapping absence, `PA = 0`, person `50/50/50`):* Avatar has an arrest/suspension with `UntilDay = lockDay + 2` ($d_{\text{abs}} = 1$ missed day, Monday `lockDay + 1`). $\Delta H_{\text{bat}} = -6.0 \times 1 = \mathbf{-6.0} \implies$ score $= 152.0 - 6.0 = \mathbf{146.0 < 150.0} \implies$ **INACTIVE**.
       - *Sub-fixture 2b (injury rust, `d_abs = 0`, `RatingPenalty = 2` on `lockDay + 1 < PenaltyUntilDay`):* $\Delta H_{\text{bat}} = -3.0 \times 2 = \mathbf{-6.0} \implies$ score $= 152.0 - 6.0 = \mathbf{146.0 < 150.0} \implies$ **INACTIVE**.
     - **Flip 3 — Coach Affinity alone flips the call (`ACTIVE → INACTIVE` and `INACTIVE → ACTIVE`):**
       - *Low affinity (`PA = 0`, healthy, `work_ethic = 30, discipline = 30, teamwork = 30`, each $-20$):* $\Delta A = 0.12(-20) + 0.10(-20) + 0.08(-20) = \mathbf{-6.0} \implies$ score $= 152.0 - 6.0 = \mathbf{146.0 < 150.0} \implies$ **INACTIVE**.
       - *High affinity on a trailing avatar (`BatScore = 148 < 150`, `work_ethic = 70, discipline = 70, teamwork = 70`, each $+20$):* $\Delta A = \mathbf{+6.0} \implies$ score $= 148.0 + 6.0 = \mathbf{154.0 > 150.0} \implies$ **flips `INACTIVE → ACTIVE`**.
     - **Starting-calibre protection fixture (D4b-A):** Give the avatar `BatScore = 180` (`60/60/60`, one of the 9 unfeatured `LineupBuilder` starters) and `d_abs = 0`. Even with `PA = 120, OPS = 0.350` ($\Delta P = -12.0$), `rust = 8` ($\Delta H = -24.0$), and person `10/10/10` ($\Delta A = -12.0$), he is in $L_9(H_0)$ and stays **ACTIVE**.
- **Derivation:**
  In a 64-game Pro season (22 weeks, ~3 games/week), a regular accrues $\approx 11\text{–}12\text{ PA/week}$ ($\approx 250\text{ PA/season}$) and a fringe player accrues $\approx 40\text{–}120\text{ PA/season}$, while a #3/#4 SP or RP accrues $\approx 15\text{–}55\text{ IP/season}$. Setting `PerfMinPa = 20` ($\approx 1.5$ weeks) and `PerfMinOuts = 18` ($6.0\text{ IP}$) suppresses Opening-Week small-sample noise, while `PerfPaShrink = 60` and `PerfOutsShrink = 54` ($18.0\text{ IP}$) reach $50\%$ shrinkage weight after roughly a quarter-season of fringe exposure. Bounded caps ($\Delta P \in [-18, +18]$, $\Delta A \in [-15, +15]$, $\Delta H \in [-36, 0]$ before full-week disqualification) sit on the exact 3-rating `BatScore` scale ($\pm 5\text{ to }\pm 6$ points per attribute), ensuring each lever has real teeth at a 1–15 point bench margin without producing erratic swings.

#### Q4. Unhealthy fill ordering and tie key
- **Verdict:** **Agree** — when fewer healthy players exist in a role group (`Position`, `Starter`, `Reliever`) than its active target ($K \in \{12, 3, 3\}$), activate all healthy players in that role group and fill the remaining $K - |H_{\text{role}}|$ active slots from the role's unhealthy (absent) players ordered by **earliest return day (`UntilDay` ascending)**, then **base role quality descending**, then **`PlayerId` ordinal ascending**.
- **Exact rule:**
  Order the unhealthy candidates $u \in U_{\text{role}}$ by:
  1. **`UntilDay` ascending** (`u.UntilDay.CompareTo(v.UntilDay)` — earliest return day first).
  2. **Base role quality descending:**
     - Position players: `BatScore` (`BatPower + BatContact + BatDiscipline`) descending.
     - Pitchers (`Starter` or `Reliever`): `PitStuff + PitControl` descending, then `PitStamina` descending (the exact `StarterSelection.CompareQuality` prefix).
  3. **`PlayerId` ordinal ascending** (`string.CompareOrdinal(u.PlayerId, v.PlayerId)`).
  - *Fixture (`designations` unhealthy-fill check):* On a Pro team's 4-SP staff (`sp1 ≻ sp2 ≻ sp3 ≻ sp4` by `CompareQuality`), set absences on Sunday `lockDay = 98`: `sp1` until `108` (`UntilDay = 108`), `sp2` until `102` (`UntilDay = 102`), `sp3` until `102` (`UntilDay = 102`), and leave `sp4` healthy. Only $|H_{\text{SP}}| = 1 < 3$ SP is healthy, so `sp4` takes active slot 1; the 2 unhealthy-fill slots go to `sp2` and `sp3` (`UntilDay = 102 < 108`, with `sp2` ahead of `sp3` on `CompareQuality`), and `sp1` (`UntilDay = 108`) goes **inactive**.
- **Derivation:**
  Because an inactive player cannot replace an active player mid-week (§7 attrition), filling deficit active slots with the earliest-returning absent players maximizes expected available player-days during Monday–Saturday (`[lockDay + 1, lockDay + 6]`): in the fixture above, `sp2` and `sp3` return on Thursday `102` and can pitch Thursday `102` and Saturday `104`, whereas activating `sp1` (`UntilDay = 108`) would leave the slot dead all week. Because `PlayerId` is a primary key, `(UntilDay ↑, RoleQuality ↓, PlayerId ↑)` is a strict total order requiring zero RNG draws.

#### Q5. Forfeit runs (`9–0`) in the College National Ranking (D2c)
- **Verdict:** **Agree with D2c-A** — counting a College forfeit's `9–0` score in `runs_scored`, `runs_allowed`, `RD_norm`, and standings tiebreakers moves a 28-game season's `RankScore` by at most **`±0.006429` (`±9 / 1,400`)** relative to a zero-run forfeit (D2c-B) and by **`±0.003714` (`±5.2 / 1,400`)** relative to a typical played College win/loss, which is well inside a single win's `0.50 / 28 = 0.017857` weight. **D2c-A is completely acceptable; D2c-B is not needed.**
- **Exact rule:** Keep `PlayoffQueries.SqlSelectCompletedRegularSeason` (`PlayoffQueries.cs:24–27`: `WHERE phase = 0 AND status IN (1, 2)`), `SeasonTable.Build` (`Standings.cs:29–76`), and `RankingCalculator.Rank` (`Standings.cs:346–380`) unchanged. A College forfeit (`status = 2`) records `9–0` (`RegulationInnings(College) = 9`), crediting `+1` win and `+9` `runs_scored` to the winner and `+1` loss and `+9` `runs_allowed` to the forfeiting team.
- **Derivation and arithmetic (at $G = 28$ games, clamp $\pm 5.0\text{ R/G}$, `RankScore = 0.50·W + 0.30·SOS + 0.20·RD_norm`):**
  1. *Exact `RD_norm` and `RankScore` sensitivity per run at $G = 28$:*
     Inside the $\pm 5.0\text{ R/G}$ clamp ($|\text{RD}| \le 5 \times 28 = 140\text{ runs}$),
     $$\text{RD}_{\text{norm}} = \frac{\text{RD} / 28 + 5.0}{10.0} = 0.50 + \frac{\text{RD}}{280}, \qquad 0.20\,\text{RD}_{\text{norm}} = 0.10 + \frac{\text{RD}}{1400}.$$
     Each $+1$ run of season run differential adds $+1/28 = \mathbf{+0.035714\text{ R/G}}$ to $\text{RD}/G$, $+1/280 = \mathbf{+0.003571}$ to $\text{RD}_{\text{norm}}$, and $+1/1400 = \mathbf{+0.0007143}$ to `RankScore`. (If a team is already at or beyond the $\pm 140$-run clamp boundary, additional runs in that direction move `RD_norm` by `0`.)
  2. *Comparison across alternatives for one `9–0` forfeit at $G = 28$:*

     | Baseline Compared Against `9–0` Forfeit (`+9` RD) | $\Delta\text{RD}$ (runs) | $\Delta(\text{RD}/G)$ (R/G) | $\Delta\text{RD}_{\text{norm}}$ | $\Delta\text{RankScore}$ (`0.20·ΔRD_norm`) | Share of One Win's `0.50·ΔW` (`+0.017857`) | Forecast $\mathbb{E}[|\Delta\text{Rank}|]$ *(Mean / Bubble)* |
     | :-- | :--: | :--: | :--: | :--: | :--: | :--: |
     | **D2c-B (`W/L` only, `+0` RD)** | **`+9`** | **`+0.3214`** | **`+0.03214`** (`9/280`) | **`+0.006429`** (`9/1400`) | **`36.00%`** (`9/25`) | **`0.37` / `0.60` spots** *(forecast)* |
     | **1-run played win (`+1` RD)** | **`+8`** | **`+0.2857`** | **`+0.02857`** (`8/280`) | **`+0.005714`** (`8/1400`) | **`32.00%`** (`8/25`) | **`0.33` / `0.53` spots** *(forecast)* |
     | **Typical played College win (`+3.8` RD)** | **`+5.2`** | **`+0.1857`** | **`+0.01857`** (`5.2/280`) | **`+0.003714`** (`5.2/1400`) | **`20.80%`** (`5.2/25`) | **`0.21` / `0.35` spots** *(forecast)* |
     | **Forfeiting team (`-9` RD vs `0` RD)** | **`-9`** | **`-0.3214`** | **`-0.03214`** (`-9/280`) | **`-0.006429`** (`-9/1400`) | **`36.00%`** (`9/25`) | **`0.37` / `0.60` spots** *(forecast)* |

  3. *Rank-movement forecast explanation (labeled as a model forecast from the 24-team spread-25 `RankScore` distribution):*
     In a 24-team spread-25 College season, `RankScore` spans roughly $[0.30, 0.70]$ with cross-team SD $\sigma_{\text{Score}} \approx 0.102$, giving an average inter-rank spacing of $\approx 0.40 / 23 = 0.0174$ across all 24 teams and a normal order-statistic spacing of $\Delta\text{Score}_{\text{mid}} \approx \sigma_{\text{Score}} / (24\,\phi(0)) \approx 0.0107$ in the dense `#8–#19` middle bubble. A `+0.006429` shift (vs D2c-B) corresponds to $0.006429 / 0.0174 \approx \mathbf{0.37}$ rank spots on average ($0.006429 / 0.0107 \approx \mathbf{0.60}$ spots near the `#17/#18/#19` cut line), and the excess over a typical `+3.8`-run played win (`+0.003714`) is only **`0.21` spots** on average (**`0.35` spots** near the bubble). Furthermore, D2c-A preserves the league accounting identity $\sum_i \text{RS}_i = \sum_i \text{RA}_i$, penalizes a team that forfeits a game in same-record tiebreakers (`Standings.Order` Steps 2–3: `RD` ↓, `RA` ↑), and keeps 13-4a's verified `SeasonTable` / `RankingCalculator` and frozen D5 holdout untouched.

#### Q6. Stress deltas and coach-beat firing rules (D5-A)
- **Verdict:** **Agree** — set `InactiveStressDelta = +15.0f` on an active-to-inactive transition, `ReactivatedStressDelta = -5.0f` on an inactive-to-active transition, and **do not text or apply a stress impulse on a second (or $k$-th) consecutive inactive week** (`WasActive == false && IsActive == false`). Reconcile §5 `attended-forfeit`'s bullet (`:254`) with §3 D5-A (`:151`) and spec §7 (`league_restructure.md:160–162`).
- **Exact rule (data constants in `RosterBeatWriter` / `RosterNarrativeProfile`):**
  - `InactiveStressDelta = +15.0f` (published via `StressImpulseEvent(avatarId, +15.0f)` when `WasActive == true && IsActive == false`).
  - `ReactivatedStressDelta = -5.0f` (published via `StressImpulseEvent(avatarId, -5.0f)` when `WasActive == false && IsActive == true`).
  - *Transition gate:*
    - `WasActive == true && IsActive == false` (first week of an inactive stint, including Week 1 if the avatar had no prior `Roster_Designations` row, since the absent-row default is `WasActive = true`): write **1 coach text** (`coach_reyes`, key `roster.lock.inactive`) and publish **1 `StressImpulseEvent(avatarId, +15.0f)`**.
    - `WasActive == false && IsActive == false` (2nd, 3rd, … consecutive inactive week): **no event, no coach text, `0.0f` stress**.
    - `WasActive == false && IsActive == true` (first week back active after an inactive stint): write **1 coach text** (`coach_reyes`, key `roster.lock.reactivated`) and publish **1 `StressImpulseEvent(avatarId, -5.0f)`**.
    - `WasActive == true && IsActive == true`: **no event, no coach text, `0.0f` stress**.
  - *Forfeit beat (`GameForfeitedEvent` where `HomeTeamId == avatarTeamId || AwayTeamId == avatarTeamId`):*
    Write **1 coach text** from the tier's coach contact (`LeagueTier.HS → "coach_hs"`, `LeagueTier.College → "coach_college"`, `LeagueTier.Pro → "coach_reyes"`, keyed on whether the avatar's team forfeited or won by forfeit). Per spec §7 (`league_restructure.md:160–162`), §3 D5-A (`:151`), and §5 CoreLoop (`:262`), a forfeit carries **no stress impulse** (`0.0f`; see Amendment 5 to remove the stray `"and the stress event"` phrase from §5 `attended-forfeit` at line 254).
  - *Fixture:* In `designations` / `CoreLoop`, lock Week 1 with the fringe avatar inactive (`WasActive = true → IsActive = false`): assert `NarrativeLog` gains `+1` text from `coach_reyes` and `LifeSimManager` stress rises by `+15.0f`. Lock Week 2 with the avatar still inactive (`false → false`): assert `+0` texts and `+0.0f` stress impulse. Lock Week 3 with the avatar active again (`false → true`): assert `+1` re-activation text from `coach_reyes` and `-5.0f` stress impulse.
- **Derivation:**
  In `LifeSimManager` (`LifeSimManager.cs:93–100`), stress lives on `[0, 100]` with `StressRelaxationPerHour = 0.4f` ($9.6\text{/day}$ passive decay) and `StressReliefPerAction = 15.0f`. Across all 199 authored stress consequences in `Assets/Narrative/Events/Content/*.json` ($133$ positive: $\min = +1, \text{median} = +8, \max = +45$; $66$ negative: $\min = -30, \text{median} = -5, \max = -1$), moderate roster setbacks (`suspended_repeat_violation`, `sidelined_by_injury` surgery) carry **`+15`** and standard relief beats carry **`-5`**. Firing only on transitions (`WasActive != IsActive`) prevents weekly phone spam and prevents a multi-week injury or slump from dealing $+15 \times k$ cumulative stress while the avatar is already sidelined.

#### Q7. Forfeit exposure per tier-season under shipped absence sources and pooled-band invariance (report-only)
- **Verdict:** **Agree** — confirm that (1) the 64 HS / 16 College / 8 Pro pooled band samples attach no `AvailabilityLedger` and **cannot move by a single bit**, and (2) under the literal absence sources shipped on `b7fbc4c`, expected organic forfeits per tier-season are **identically `0.000`** across HS, College, and Pro. Below is both the exact shipped-source proof and the parametric binomial forfeit forecast (`[FORECAST]`) as a function of per-player daily absence prevalence $q$ once R1-MATH (`OveruseInjuryRisk`) and S-1 (NPC `ped_active`) turn on NPC absences.
- **Exact numbers and derivation:**
  1. *Pooled band sample invariance (`Slice133Suites.cs:586–607, 616–704`):*
     In `RunPooledTierBands`, `PlayReplicateSeasons` constructs each replicate's `LeagueSimulator` with `sim.Availability == null` (and no `RosterManager` or `EventDispatcher`). When `Availability is null`, `LeagueSimulator.RefreshAvailability` sets `_hasAbsences = false` (`LeagueSimulator.cs:1134–1137`). Every player is available on every game day, so `CanField` is `true` for every game and the 64/16/8 pooled band numbers are bit-identical to 13-3/13-4.
  2. *Exact forfeit rate under the shipped absence sources on `b7fbc4c` (`0.000` forfeits/season):*
     An exhaustive scan of `Assets/` on `b7fbc4c` shows that the **only** production caller of `PlayerQueries.SetAbsence` / publisher of `PlayerAbsenceChangedEvent` is `EventConsequenceApplier.ApplyAbsence` (`EventConsequenceApplier.cs:476–480`):
     - `TierRules.OveruseInjuryRisk` is **`0.0`** (`TierRules.cs:40`, shipped neutral in 13-2 D5 pending R1-MATH).
     - `ped_active` has **no writer yet** (ships in S-1; `progress.md:70`).
     - In `Assets/Narrative/Events/Content/`, only two files contain `"type": "absence"` consequences:
       - `dirt_underworld_events.json` (7 events): **every event is `"scope": "avatar"`**, which `EventDispatcher.EvaluateDay` (`EventDispatcher.cs:199–203`) never evaluates for NPCs.
       - `roster_availability_events.json` (5 events with `"scope": "any"`): `narcotics_arrest` requires `flag_active: "narc_watchlist"` (written only by `HustleService` on the avatar); `suspended_*` (3 events) require `detection_risk >= 60 / 80 / 90`; `sidelined_by_injury` requires `health_ceiling < 50`.
     - At world generation (`LeagueGenerator.cs:348, 351`), every NPC is inserted with `HealthCeiling = 100` and `DetectionRisk = 0`. Because no shipped event lowers an NPC's `health_ceiling` or raises an NPC's `detection_risk`, and no NPC has `ped_active`, **zero NPCs can become absent in a shipped career save** ($q_{\text{NPC}} = 0$).
     - At most **1 player in the universe (the avatar)** can be absent at any time. Because every team is generated with $\ge 11$ position players (HS $11\text{–}14$, College $15$, Pro $12$ active) and $\ge 5$ pitchers (HS $5\text{–}8$, College $9$, Pro $6$ active), losing 1 player leaves $\ge 10 \ge 9$ eligible position players and $\ge 4 \ge 1$ eligible pitchers (and $\ge 8 \ge 3$ pitchers in the Red Dirt Series Bo3). Hence:
       $$\mathbb{E}[\text{Forfeits per tier-season}\mid \text{shipped } b7fbc4c \text{ sources}] = \mathbf{0.000} \quad (\text{HS, College, and Pro}).$$
  3. *Analytic Binomial Forecast (`[FORECAST]`, report-only) for future NPC absence prevalence $q \in (0, 1]$ (once R1-MATH / S-1 activate NPC injuries and suspensions):*
     Let $q$ be the steady-state per-player probability of being absent on a game day. For a team with $N_{\text{pos}}$ eligible-pool position players and $N_{\text{pit}}$ eligible-pool pitchers:
     $$P(\text{team fails } \texttt{CanField}) = 1 - \left(1 - \sum_{k = N_{\text{pos}} - 8}^{N_{\text{pos}}} \binom{N_{\text{pos}}}{k} q^k (1-q)^{N_{\text{pos}}-k}\right)\left(1 - q^{N_{\text{pit}}}\right), \qquad P(\text{game forfeit}) = 1 - \big(1 - P(\text{team fails})\big)^2.$$
     - **HS ($65\text{ games/season} = 60\text{ reg} + 5\text{ post}$):** `LeagueGenerator` (`LeagueGenerator.cs:47`) rolls $N_{\text{pos}} \sim \text{Uniform}\{11, 12, 13, 14\}$ ($1/4$ each) and $N_{\text{pit}} = N_{\text{SP}} + N_{\text{RP}}$ with $N_{\text{SP}} \sim \text{Uniform}\{3, 4\}$, $N_{\text{RP}} \sim \text{Uniform}\{2, 3, 4\}$ ($N_{\text{pit}} \in [5, 8]$). The dominant term is the $1/4$ of HS teams with $N_{\text{pos}} = 11$, which forfeit when $\ge 3$ of 11 position players are simultaneously absent ($\binom{11}{3}q^3(1-q)^8 = 165\,q^3(1-q)^8$).
     - **College ($\approx 355.5\text{ games/season} = 336\text{ reg} + 19.5\text{ post}$):** Fixed $N_{\text{pos}} = 15, N_{\text{pit}} = 9$; requires $\ge 7$ of 15 position players absent ($\binom{15}{7}q^7 = 6435\,q^7$) or all 9 pitchers absent ($q^9$).
     - **Pro ($\approx 1053.1\text{ games/season} = 1024\text{ reg} + 29.1\text{ post}$):** Each Sunday lock activates up to $12$ healthy position players out of $15$ and $6$ healthy pitchers out of $9$. A Pro team forfeits if either $\ge 7$ of 15 position players (or all 9 pitchers) are already absent at the Sunday lock (prevalence $q$), OR $\ge 4$ of its 12 active position players go down with *new* mid-week absences during Monday–Saturday (mid-week new-absence hazard $q_{\text{mid}} \approx 0.5\,q$ for a ~12-day mean injury/suspension duration; we also report the unrebalanced 12-active-slot upper bound at full $q$):

     | Daily Absence Prevalence $q$ *(Forecast Scenario)* | HS $P(\text{team fails})$ *(at $N_{\text{pos}}=11$)* | **HS Forecast $\mathbb{E}[\text{Forfeits/Season}]$** (`65` games) | **College Forecast $\mathbb{E}[\text{Forfeits/Season}]$** (`355.5` games) | **Pro Forecast $\mathbb{E}[\text{Forfeits/Season}]$** *(Sunday-rebalanced; 12-slot UB)* |
     | :-- | :--: | :--: | :--: | :--: |
     | **`0.00%`** *(Shipped `b7fbc4c`)* | `0` (`0`) | **`0.0000`** | **`0.0000`** | **`0.0000`** (`0.0000`) |
     | **`1.00%`** *(Light R1-MATH/S-1)* | `4.00e-5` (`1.55e-4`) | **`0.0052`** (~1 per 192 seasons) | **`4.26e-8`** | **`0.0006`** (`0.0098`) |
     | **`2.00%`** *(Moderate attrition)* | `3.11e-4` (`1.17e-3`) | **`0.0404`** (~1 per 25 seasons) | **`5.09e-6`** | **`0.0098`** (`0.1467`) |
     | **`5.00%`** *(Heavy attrition)* | `4.45e-3` (`1.52e-2`) | **`0.5769`** (~1 per 1.7 seasons) | **`0.0025`** | **`0.3540`** (`4.7052`) |
     | **`10.00%`** *(Extreme stress test)* | `3.08e-2` (`8.96e-2`) | **`3.9405`** | **`0.2208`** | **`5.3564`** (`53.309`) |

#### Q8. Default MC runtime budget split (D7: $\le +10\text{ s}$)
- **Verdict:** **Agree** — the 13-5 default MC additions forecast to **`+1.55–3.18 s` raw (`+1.94–3.98 s` with $1.25\times$ reserve $\le +10.0\text{ s}$)** on the owner's Windows 11 PC (Intel Pentium Silver N5030 @ 1.10 GHz), requiring no suite trims, provided the new 13-5 suites share at most 1–2 generated v14 scratch worlds (the `Slice134World` pattern at `Slice134Suites.cs:194–250`).
- **Exact component breakdown (`[FORECAST]` calibrated from 13-4 §11.4 / §12.2 timings on the owner's PC: v14 world-gen `298–691 ms`, Pro season `1.4–1.8 s`, College season `208–420 ms`, HS season `35–70 ms`):**
  1. **Rewritten `availability` suite (`Program.cs:4205–4498`):** Replaces shadowing assertions in place on its existing scratch worlds with zero additional world-gens or season simulations $\implies$ **`~0.00 s` net delta**.
  2. **`season-pipeline` with `RosterManager` wired (D1-A):** 54 Sunday locks ($2 \times 27$) $\times 768$ pooled prepared-command PK upserts in `54` transactions (`~2.5 ms/lock` $\approx 0.14\text{ s}$) + 0 lineup rebuilds + D1-A channel assertions (`~0.05 s`) $\implies$ **`+0.15–0.25 s`**.
  3. **Shared 13-5 scratch world generation (`1` flat spread-0 world + `1` spread-25 world):** $2 \times (0.30\text{–}0.69\text{ s}) = \mathbf{+0.60\text{–}1.38\text{ s}}$.
  4. **`designations` suite (lock 768 rows, 12/3/3 active, subset-greedy parity over 1,000 random eligible subsets of the 32 spread-25 Pro teams in memory, Q4 unhealthy fill, injured ace inactive, Q3 avatar margin flips, idempotent re-lock, hydrate, boot reconcile):** **`+0.20–0.40 s`**.
  5. **`attrition` + `forfeits` (all 3 tiers) + `attended-forfeit` + `attended-undo` (D3-A cancel/rollback `StartUndo` mutation check) + 4 zero-B allocation probes:** targeted single-week/single-day drives and ~6 attended `MicroGame` runs on the shared worlds $\implies$ **`+0.60–1.15 s`**.
  - **Total forecast default MC addition:** **`+1.55–3.18 s` raw (`+1.94–3.98 s` with $1.25\times$ reserve $\le +10.0\text{ s}$)**.

#### Q9. College active-20 cap (D4d)
- **Verdict:** **Agree** — confirm that College's 20-player active cap (spec §5.1 / §16-I1) **never binds** `LeagueSimulator` or `MicroGame` and needs no model or `Roster_Designations` rows.
- **Exact rule:** `Roster_Designations` is Pro-only (`LeagueTier.Pro`). For `LeagueTier.HS` and `LeagueTier.College`, `RosterManager.IsActive(playerId, day)` returns `true` for every rostered player with zero database rows read or written.
- **Derivation:**
  1. *Maximum distinct players used by one team in one game:*
     - In `LeagueSimulator` (macro), a team uses **9 position players** (`LineupBuilder` starters; no mid-game bench substitutions exist at `LeagueSimulator.cs:1264`) and **1 pitcher** (complete-game starter or opener at `LeagueSimulator.cs:807–812`) $= \mathbf{10\text{ players}} \le 20$.
     - In `MicroGame` (attended), a team uses **9 position players** (`MicroGame.cs:659, 1364`) and at most **1 starting pitcher + all 5 relievers** (`MaybePullPitcher`, `MicroGame.cs:1305–1326`, since College caps RP at $\le 5$ per spec §5.1 and `LeagueGenerator.cs:48`) $= \mathbf{15\text{ players}} \le 20$ (and even counting the $\le 3$ unused SP on the bench, $9 + 4 + 5 = \mathbf{18\text{ players}} \le 20$).
  2. *No weekly lock in College:* Because College designations are *"coach's choice each game day"* with no Sunday lock (spec §5.1, §7, §16-I5), any game-day choice of 20 active players out of a 24–26 player College roster would trivially include today's 9 lineup starters + today's starter + all 5 relievers ($15 \le 20$) with 5 active slots to spare. Thus an active-20 game-day selector has zero reachable binding states in either engine.

#### Q10. Stream contract and double-forfeit resolution
- **Verdict:** **Agree** — confirm that 13-5 needs **zero new RNG streams and zero RNG draws** under the recommended options (D2d-A, D4a-A, D4b-A, D5-A), that skipping PA draws on a forfeited game is deterministic and guard-safe, and that D2d-A's double-forfeit rule (home team in regular season, high seed in postseason) is fair and RNG-free. For completeness, the fallback D2d-C seeded-coin specification is provided below in case the owner selects D2d-C.
- **Exact rule:**
  - *Under D2d-A (recommended):*
    - Regular-season double forfeit (`phase == 0`, `!homeCanField && !awayCanField`): award `RegulationInnings(tier)`–`0` (`status = 2`) to `HomeTeamId`.
    - Postseason double forfeit (`phase IN (1, 2)`, `!homeCanField && !awayCanField`): award `RegulationInnings(tier)`–`0` (`status = 2`) to `series.HighSeedTeamId` (13-4a's existing `PlayoffManager.ForfeitScore` at `PlayoffManager.cs:440–442`).
  - *Fallback specification if the owner chooses D2d-C (seeded coin, never sim RNG):*
    Add `ForfeitCoinTag = 0x464F_5246_434F_494EUL` (`"FORFCOIN"`) to `PostseasonStreams` (`PostseasonStreams.cs:11–18`) and derive:
    ```csharp
    public static bool DoubleForfeitHomeWins(
        ulong scheduleSeed, int seasonYear, LeagueTier tier, int dayOfSeason, int homeTeamId, int awayTeamId)
    {
        ulong gameKey = ((ulong)(uint)dayOfSeason << 32) | ((ulong)(uint)homeTeamId << 16) | (uint)awayTeamId;
        ulong h = MixCombine(scheduleSeed, 0x464F_5246_434F_494EUL); // "FORFCOIN"
        h = MixCombine(h, unchecked((ulong)seasonYear));
        h = MixCombine(h, (ulong)tier);
        h = MixCombine(h, gameKey);
        return (h & 1UL) == 0UL;
    }
    ```
- **Derivation:**
  Every 13-5 decision rule (`LineupBuilder`, `BenchScore`, avatar margin score, Q4 unhealthy fill, `CanField`, forfeit scores, and D2d-A double-forfeit award) is a pure deterministic function of persisted state with ties broken by `PlayerId` ordinal ascending. In any neutral or guard-world run, 0 games forfeit, so `LeagueSimulator._rng`, `CareerManager._rng`, and `PostseasonStreams` advance identically to 13-4a. When a game *does* forfeit in an attrition save, no PA occurs, so drawing 0 numbers from `LeagueSimulator._rng` / `CareerManager._rng` / `PostseasonStreams` is the exact representation of an unplayed game. Under D2d-A, because every tier's regular-season schedule has exact 50/50 home/away balance per team (HS `5H/5A`, College `14H/14A`, Pro `32H/32A`; `Slice133Suites.cs:322–352`), awarding a regular-season double forfeit to the home team is symmetric ex ante with zero coin machinery.

---

### 10.2 Independent Seam and SQL Verification on `b7fbc4c`

Every code seam and SQL statement cited in §1.1, §1.2, and §4 was independently inspected on `b7fbc4c` (`HEAD~1` of `917fc01`):

1. **Guard world attaches no `AvailabilityLedger` (§1.2 #1 — verified):**
   - In `Tools/MonteCarloHarness/Program.cs:529–540`, `RunSeasonPipeline` constructs `var league = new LeagueSimulator(db, baseball, normalizer, new RngState(SeasonSeed)); league.Initialize(); league.AttachTo(bus);` and wires `guardPlayoffs`, leaving `league.Availability`, `league.Persons`, `league.Rivalries`, and `league.Equipment` all `null` and creating no avatar.
   - In `Assets/Simulation/Baseball/LeagueSimulator.cs:1132–1138`, `RefreshAvailability(long day)` immediately sets `_hasAbsences = false; return;` when `Availability is null`. Consequently, in `SelectPitcher` (`:756, 782, 794`), `PlayHalfInning` (`:1269`), and `PickPostseasonPitcher` (`:990, 1009`), `_hasAbsences` is always `false` in the guard world, and call-up retirement cannot change a single instruction in the guard world.
2. **Subset-greedy `LineupBuilder` and Pro starter-gap seams (§1.2 #2 — verified):**
   - `Assets/Simulation/Baseball/LineupBuilder.cs:39–96`: `Build` sorts `candidates` by `b.BatScore.CompareTo(a.BatScore)` then `string.CompareOrdinal(a.PlayerId, b.PlayerId)` (`:48–52`), fills `FieldingPositions.Field` (`C, 1B, 2B, 3B, SS, LF, CF, RF`) by primary (`:58`) then secondary (`:61`), fills `open` out-of-position (`:72–77`), assigns `featuredSlot` (if in `order` and not in `chosen`, `:79–83`) else best remaining as DH (`:86`), and orders `chosen.Keys` ascending (`:89`). Proof in §10.1 Q1 confirms $L(C) \subseteq S \subseteq C \implies \text{Build}(S, \text{featuredSlot}) \equiv \text{Build}(C, \text{featuredSlot})$.
   - `Assets/Simulation/Baseball/StarterSelection.cs:40–70, 91–134`: `SelectStarter` scans candidates in `CompareQuality` order (`PitStuff + PitControl` ↓, `PitStamina` ↓, `PlayerId` ↑, `:117–134`) for the first available starter with `!usedInSeries[i] && Gap(lastStartDay[i], day) >= minRestDays` (`:46`). With `MinRestDays(Pro) = 3` on Tue/Thu/Sat regular-season dates (`93..240`) and D3 postseason dates (`244..275`), §10.1 Q1 proves `#1` and `#2` take 100% of starts in the guard world.
3. **Zero-RNG requirement (§1.2 #6 — verified):**
   - `LineupBuilder.cs:48–52` and `StarterSelection.cs:124–134` break all ties by `string.CompareOrdinal(aPlayerId, bPlayerId)`.
   - `PlayoffManager.ForfeitScore` (`Assets/Simulation/Baseball/PlayoffManager.cs:424–444`) resolves `PostseasonPlay.BothForfeit` deterministically via `bool highHome = row.HomeTeamId == series.HighSeedTeamId;` (`:440`) with zero RNG draws.
   - `LeagueSimulator.SimulatePostseasonGame` (`Assets/Simulation/Baseball/LeagueSimulator.cs:922–930`) returns `PostseasonPlay.{HomeForfeits, AwayForfeits, BothForfeit}` before entering the inning loop (`:944`), drawing 0 numbers from `ref RngState rng` when a forfeit occurs.
4. **Scratch v14 SQLite validation of all §4 SQL statements (§0.4 — verified, with one boundary fix in Amendment 1):**
   - Executed `Assets/Data/Database/SchemaDefinitions.sql` into a scratch database (`PRAGMA user_version = 14`, `PRAGMA foreign_keys = ON`) and verified `EXPLAIN QUERY PLAN` and live execution on throwaway rows:
     - `Roster_Designations` DDL (`SchemaDefinitions.sql:637–642`) and `SchemaValidator` check (`Tools/SchemaValidator/Program.cs:559–562`) match §1.1 (`player_id TEXT PRIMARY KEY`, `team_id INTEGER NOT NULL REFERENCES Teams(team_id)`, `is_active INTEGER NOT NULL CHECK (is_active IN (0, 1))`, `locked_through_day INTEGER NOT NULL`).
     - `Teams` has `conference_id` (`SchemaDefinitions.sql:218–228`), and `tier` lives on `Conferences` (`:195–199`) and `Team_Tiers` (`:340–343`), **never** on `Teams`.
     - `EXPLAIN QUERY PLAN` results on scratch v14:
       - Upsert (`INSERT INTO Roster_Designations ... ON CONFLICT (player_id) DO UPDATE ...`): PK index upsert, verified idempotent and FK-clean.
       - Hydrate (`SELECT d.player_id, d.team_id, d.is_active, d.locked_through_day FROM Roster_Designations d JOIN Teams t ON t.team_id = d.team_id JOIN Conferences c ON c.conference_id = t.conference_id WHERE c.tier = @tier AND d.locked_through_day > @today;`): `SCAN d` ($\le 768$ rows) $\to$ `SEARCH t USING INTEGER PRIMARY KEY (rowid=?)` $\to$ `SEARCH c USING INTEGER PRIMARY KEY (rowid=?)`. *(Note: joining `Team_Tiers tt ON tt.team_id = d.team_id WHERE tt.tier = @tier`, as `PlayoffQueries.cs:44–46` does, saves one PK hop; either plan scans at most 768 rows and is sub-millisecond.)*
       - Prior state by player (`SELECT is_active, locked_through_day FROM Roster_Designations WHERE player_id = @playerId;`): `SEARCH Roster_Designations USING INDEX sqlite_autoindex_Roster_Designations_1 (player_id=?)`.
       - Latest lock (`SELECT COALESCE(MAX(d.locked_through_day), 0) FROM Roster_Designations d JOIN Teams t ... JOIN Conferences c ... WHERE c.tier = @tier;`): `SEARCH d` + PK lookups on `t` and `c`.
       - Status-carrying schedule result update (`UPDATE Schedule_Games SET status = @status, home_score = @homeScore, away_score = @awayScore WHERE game_id = @gameId;`): `SEARCH Schedule_Games USING INTEGER PRIMARY KEY (rowid=?)`.
       - Regular-season parked-game reclaim (`SELECT game_id, day_of_season, home_team_id, away_team_id FROM Schedule_Games WHERE season_year = @seasonYear AND tier = @tier AND phase = 0 AND status = 0 AND day_of_season <= @dayOfSeason AND (home_team_id = @teamId OR away_team_id = @teamId) ORDER BY day_of_season, game_id LIMIT 1;`): `SEARCH Schedule_Games USING INDEX ix_sched_day (season_year=? AND tier=? AND day_of_season<?)`.
   - **Boundary defect caught in §4 item 3 (`d.locked_through_day >= @today` vs `> @today`):**
     Under D6-A (`:162`), Sunday `s` (e.g. `s = 91`) writes `locked_through_day = s + 7` (`98`, the next Sunday). If a crash occurs on Sunday `98` *after* `TimeManager.AdvanceDay()` commits `current_day = 98` (`TimeManager.cs:74`) and *before* `RosterManager` commits Sunday `98`'s lock (`locked_through_day = 105`), rebooting on `today = 98` with `d.locked_through_day >= @today` matches last week's `98 >= 98`, hydrates stale Week 1 designations, **skips boot reconcile**, and then expires on Monday `99` (`98 >= 99` false)! Changing the coverage predicate to strict **`d.locked_through_day > @today`** (`MAX(d.locked_through_day) > @today`, i.e., `locked_through_day == nominalSunday + 7 > today` for every day `today ∈ [nominalSunday, nominalSunday + 6]`) makes `98 > 98` **false** on Sunday `98` before lock `98` commits (triggering boot reconcile to `105 > 98`), while remaining **true** on `today = 98` after lock `98` commits (`105 > 98`) and on Monday–Saturday `99..104` (`105 > 99..104`), and automatically expiring the final Sunday `273` lock (`locked_through_day = 280`) on offseason Sunday `280` (`280 > 280` false)! (Amendment 1.)
5. **Additional simulation seams verified on `b7fbc4c` (Amendments 2–6):**
   - **Macro lineup reshuffle, team defense, PED counters, and postseason macro adapter (`LeagueSimulator.cs:444, 539–555, 861–867, 922–969`):**
     - `Initialize()` stores `teamworkBySlot` only in a local array (`:444`) to bake `_teamDefense[t]` (`:553–554`). To recompute a team's defense byte when a macro lineup reshuffles on a game day (matching `MicroGame.RebuildLineups` at `MicroGame.cs:657–669`), retain `_slotTeamwork` as a preallocated array on `LeagueSimulator`.
     - `CountPedGame` (`:861–865`) and `CountPostseasonPedGame` (`:962–966`) loop `_lineupSlots[team * 9 + i]`; when a team's lineup reshuffles for that game, they must loop the **active 9 slots that played that game**.
     - `SimulatePostseasonGame` (`:915–969`) must refresh the active-designation cache alongside `RefreshAvailability(day)`, check `CanField` (≥ 9 eligible position players and ≥ 1 legal pitcher) before recording starts, and use the reshuffled lineup when any Initialize starter is absent or inactive.
   - **`MicroGame.MaybePullPitcher` starter pitch-count capture bug when skipping an ineligible reliever (`MicroGame.cs:1305–1326`):**
     - At `MicroGame.cs:1314`, `if (bullpenUsed == 0) starterPitches = fatigue.PitchesThrown;` assumes the first reliever pulled after a starting pitcher is always at index `0`. When index `0` is absent or inactive (or opened the game) and `MaybePullPitcher` skips past ineligible relievers to pull index `k > 0`, checking `bullpenUsed == 0` after advancing (or after an opener) fails to capture `starterPitches`, causing lines `1257–1264` (`if (homeStarterPitches < 0.0) homeStarterPitches = homeFatigue.PitchesThrown;`) to overwrite the starter's pitch count with the **last reliever's** pitch count! Fix: gate starter pitch capture on **`if (starterPitches < 0.0) starterPitches = fatigue.PitchesThrown;`** before replacing the starter's `fatigue` struct (Amendment 4).
   - **Regular-season `StartUndo` counter restoration (`MicroGame.cs:561–581, 1176–1179`):**
     - Under D3-A item 4, removing `if (!_postseasonGame)` in `RevertPostseasonStarts` (`:569`) must also snapshot and restore regular-season **`ShortRestStarts`** and **`BullpenGames`** (`_shortRestBefore`, `_bullpenBefore`), which today are only snapshotted for `PostseasonShortRestStarts` / `PostseasonBullpenGames` (`:564–565, 579–580, 1177–1178`) (Amendment 4).
   - **Contacts and coach beat seams (`Assets/Narrative/Contacts/contacts.json:1–17`, `NarrativeLogQueries.cs:174–190`, `CoreEvents.cs:114–126`):**
     - Verified `contacts.json` has `coach_reyes` (`role: "coach"`, line 5) and `coach_hs` (`role: "coach"`, line 12), and no `coach_college`. Adding `{ "id": "coach_college", "display_name": "Coach Vance", "role": "coach" }` uses the existing valid `"coach"` role in `ContactRegistry.cs` with zero parser changes.

---

### 10.3 Math View on Owner Decisions D1–D8

| Decision | Math / Guard Verdict | Rationale & Guard-Safety Analysis |
| :-- | :--: | :-- |
| **D1** (Call-up retirement & designations vs Pro guard) | **Strongly Endorse D1-A** *(Guard-safe by construction)* | Wiring `RosterManager` into `RunSeasonPipeline` tests the real production composition (as 13-4a wired `PlayoffManager`) and is provably guard-neutral under §10.1 Q1's subset-greedy `LineupBuilder` theorem and Pro starter-gap proof. Asserting the 10 invariance channels in §10.1 Q1 (with the stop rule if any channel moves) is far stronger than leaving `RosterManager` unwired (D1-B). |
| **D2** (The forfeit contract: a–f) | **Endorse D2a, D2b, D2c-A, D2d-A, D2e, D2f** *(Guard-safe)* | `RegulationInnings(tier)`–`0` (`status = 2`) with zero player stats, zero recorded starts, zero PED game costs, and zero normalization matches spec §4 and 13-4a. §10.1 Q5 proves D2c-A moves College `RankScore` by at most `±0.006429` (`36%` of one win) vs D2c-B (`+0.003714` vs a typical played win) while preserving $\sum \text{RS} = \sum \text{RA}$ and 13-4a's frozen D5 holdout. D2d-A (home team in regular season, high seed in postseason) is venue-balanced (`5/5`, `14/14`, `32/32`) and draws 0 RNG. One `RosterRules.CanField` unifies regular season and postseason. |
| **D3** (Gate 2 HS conflict & carried 13-4 items) | **Endorse D3-A** *(or D3-C if owner wants HS call-up frozen; both guard-safe)* | Every option leaves a neutral HS season (no absences, cancels, or game-day reloads) byte-identical. D3-A fixes two real bugs inside Gate 2's own Friday loop—losing a parked regular-season game on reload (item 3) and keeping phantom `Pitcher_Rest` starts on a cancelled/rolled-back regular-season attended game (item 4)—while unifying absence semantics across all three tiers per spec §16-I5. |
| **D4** (Designations: NPC rule, avatar margin, coach affinity, HS/College) | **Endorse D4a-A, D4b-A, D4c-A, D4d** *(Guard-safe)* | D4a-A is the exact condition required for D1-A guard neutrality (D4a-B can bench a positional starter and shift the guard ledger SHA). D4b-A protects starting-calibre avatars while making performance, health, and coach affinity decisive at the bench and `#3` SP/RP margins (§10.1 Q2–Q3). D4c-A reads existing `Player_Person` columns (`work_ethic, discipline, teamwork`) with zero DDL and zero Life-sim coupling. §10.1 Q9 proves College's active-20 cap never binds either sim (D4d). |
| **D5** (Coach beats & stress hit) | **Endorse D5-A** *(Guard-safe)* | Deterministic post-commit bus events (`RosterDesignationChangedEvent`, `GameForfeitedEvent`) carrying primitives to an `Assets/Narrative/` subscriber guarantee immediate, transition-only coach texts and stress impulses (`+15.0f` on inactivation, `-5.0f` on re-activation, `0.0f` on consecutive inactive weeks; §10.1 Q6) without `Entity_Flags` polling lag (D5-B). |
| **D6** (Lock timing & persistence) | **Endorse D6-A with Amendment 1 (`>` coverage)** *(Guard-safe)* | Running on Sundays `91–273` after the Pro sim's Sunday flush ensures performance inputs see the just-completed week's stats and delivers the avatar's text on Sunday night per spec §7. Use strict `locked_through_day > today` (Amendment 1) so a Sunday crash before the lock reconciles cleanly at boot and the final lock (`273 → 280`) expires automatically on offseason Sunday `280`. |
| **D7** (Default MC budget $\le +10\text{ s}$) | **Endorse D7** *(Guard-safe)* | Forecasts to **`+1.55–3.18 s` raw (`+1.94–3.98 s` with 1.25× reserve $\le +10.0\text{ s}$)** on the owner's PC when the new suites share 1–2 generated v14 scratch worlds (§10.1 Q8). |
| **D8** (Roster Status UI staging) | **Endorse D8-A** *(Guard-safe)* | Mirrors 13-4 D6-B: ship the read-only `RosterStatusView` DTO in 13-5a and build the screen once in 15c's design system. |

---

### 10.4 Numbered Amendments to Reconcile into §§1–9

Opus should fold these 7 refinements into §§1–5 before owner sign-off (no §§1–9 lines or D-option texts were edited by this Math check):

1. **Amendment 1 — Strict `locked_through_day > @today` Coverage & Boot-Reconcile Invariant (§1.2 #4, §3 D6-A, §4 item 3):**
   > *"When locking on Sunday `nominalSunday` (`DayOfSeason % 7 == 0`, `DayOfSeason ∈ [91, 273]`), write `locked_through_day = nominalSunday + 7` (the next Sunday's absolute day). Define a row as covering `today` iff **`d.locked_through_day > @today`** (strict `>`, never `>=`), and in boot reconcile check whether `DayOfSeason ∈ [91, 279]` has `MAX(locked_through_day) <= today` (equivalently `< nominalSunday + 7`, where `nominalSunday = today - (dayOfSeason % 7)`). This ensures that (a) a crash on Sunday `s` after `TimeManager` commits `current_day = s` but before `RosterManager` commits lock `s` sees the prior week's `locked_through_day = s` as expired (`s > s` is false) and reconciles to `s + 7`, and (b) the final lock on Sunday `273` (`locked_through_day = 280`) automatically expires on offseason Sunday `280` (`280 > 280` is false), returning all players to the default-active state with no offseason write."*
2. **Amendment 2 — Unfeatured `LineupBuilder` at the Sunday Lock vs Eligibility-Gated Featured Avatar in `MicroGame` (§1.1, §3 D4a-A/D4b-A, §4 item 4):**
   > *"At the Sunday lock (`RosterRules`), always invoke `LineupBuilder` with **`featuredSlot = -1`** over the no-overlap healthy position players ($d_{\text{abs}} = 0$) to determine the 9 automatic starters $L_9$, so a fringe position-player avatar is not auto-locked via the featured-DH override and is instead evaluated at the 3-slot bench margin under D4b-A. In `MicroGame` (`RebuildLineups` and `PlayGameCore` at `MicroGame.cs:647–656, 1169–1172`), filter team candidates to eligible position players (`!IsAbsent && IsActive`) and pass `featuredSlot = IsEligible(humanSlot) ? humanSlot : -1` (and gate `humanBatting` / `humanPitching` on eligibility) so an absent or inactive avatar whose game autopilots via `CareerManager.PlayPendingGame` is never placed in the lineup."*
3. **Amendment 3 — Q2 Bench Score, Q3 Avatar Margin Score, and Q4 Unhealthy-Fill Constants in `RosterRules` (§3 D4a–c, §4 items 1–2, §5 `designations`):**
   > *"Pin the §10.1 Q2–Q4 data constants in `RosterRules`:*
   > *- **Q2 Bench Score (3 sequential greedy picks over healthy non-starters $B = H_0 \setminus L_9$):** `BackupCatcherCoverageWeight = 24` (`Primary == C || Secondary == C`), `MiddleInfieldCoverageWeight = 18` (`Primary ∈ {2B, SS} || Secondary ∈ {2B, SS}`), `OutfieldCoverageWeight = 15` (`Primary ∈ {LF, CF, RF} || Secondary ∈ {LF, CF, RF}`), taking the max over not-yet-covered bench buckets, plus `SecondaryPositionBonus = 6` (`Secondary != null`) and `BatScore - 3 * Rust`, updating covered buckets after each pick; ties break by `PlayerId` ordinal ascending.*
   > *- **Q3 Avatar Margin Inputs (for a non-starter position player or non-top-2 SP/RP with $d_{\text{abs}} < 6$):** $\Delta P$ from persisted current-season Pro stats with `PerfMinPa = 20`, `PerfPaShrink = 60`, `PerfMinOuts = 18` (`6.0 IP`), `PerfOutsShrink = 54` (`18.0 IP`), relative OPS/ERA delta clamped to `±50%`, `PerfMaxDelta = 18.0`; $\Delta H = -6.0 \cdot d_{\text{abs}} - m_{\text{role}} \cdot \text{rust}$ ($d_{\text{abs}} \in [0, 5]$ missed Mon–Sat days, $m_{\text{role}} = 3.0$ batter / $2.0$ pitcher; $d_{\text{abs}} = 6 \implies$ unhealthy pool; apply $-3 \cdot \text{rust}$ once); $\Delta A = 0.12(\text{work\_ethic} - 50) + 0.10(\text{discipline} - 50) + 0.08(\text{teamwork} - 50)$ from `Player_Person` (neutral `50/50/50 → 0.0`).*
   > *- **Q4 Unhealthy Fill:** when healthy players $< K \in \{12, 3, 3\}$, fill remaining active slots from unhealthy players ordered by `UntilDay` ↑, base role quality ↓ (`BatScore` ↓ for batters; `PitStuff + PitControl` ↓ then `PitStamina` ↓ for pitchers), `PlayerId` ↑."*
4. **Amendment 4 — `MicroGame.MaybePullPitcher` Starter Pitch Capture & Regular-Season `StartUndo` Counter Restore (§1.1, §3 D3-A, §4 items 4 & 7):**
   > *"In `MicroGame.MaybePullPitcher` (`MicroGame.cs:1305–1326`), scan from `bullpenUsed` for the first eligible reliever (`!IsAbsent && IsActive`); if none is eligible, set `bullpenUsed = _relieverCount[team]` and return (keeping the current pitcher in); when lifting the starting pitcher for the first pulled reliever, capture `if (starterPitches < 0.0) starterPitches = fatigue.PitchesThrown;` (not `if (bullpenUsed == 0)`, which misses the capture when index `0` is skipped as ineligible or opened the game). Under D3-A item 4, when lifting the postseason-only gate in `MicroGame.RevertPostseasonStarts` (`MicroGame.cs:567–581`), also snapshot and restore regular-season `ShortRestStarts` and `BullpenGames` alongside `PostseasonShortRestStarts` and `PostseasonBullpenGames`."*
5. **Amendment 5 — Macro Lineup Reshuffle Defense Bake, Postseason `CanField` Unification, and Pending Forfeit Events (§1.1, §3 D2e/D5-A, §4 items 4–5, §5 `attended-forfeit`):**
   > *"In `LeagueSimulator`: (a) retain per-slot person teamwork (`_slotTeamwork`) past `Initialize()` so that when a team's lineup reshuffles on a game day (regular season or `SimulatePostseasonGame`), the game uses the allocation-free `LineupBuilder` twin's 9 active slots for `PlayHalfInning`, recomputes that game's effective team-defense byte via `PersonEffects.BakeTeamDefense`, and credits `CountPedGame` / `CountPostseasonPedGame` to the 9 active slots that played; (b) route `SimulatePostseasonGame` through `RosterRules.CanField` (≥ 9 eligible position players and ≥ 1 legal pitcher in series) before recording starts; (c) buffer macro regular-season `GameForfeitedEvent`s alongside `_resultStatuses` (`status = 2`) and publish them after `FlushAccumulated` commits (while attended regular-season and all postseason forfeits commit and publish immediately on the game day). In §5 `attended-forfeit` (`:254`), drop `'and the stress event'` so it matches spec §7, §3 D5-A (`:151`), and §5 CoreLoop (`:262`): stress impulses fire on inactivation (`+15.0f`) and re-activation (`-5.0f`) transitions only (`WasActive != IsActive`), while forfeits involving the avatar's team write the coach text (`coach_hs`, `coach_college`, `coach_reyes`) with `0.0f` stress."*
6. **Amendment 6 — Regular-Season Boot Reclaim Eligibility & Forfeit Check (§3 D3-A item 3, §4 item 7):**
   > *"In `CareerManager.ReclaimRegularSeasonGame(long day)` (D3-A item 3), after finding the avatar team's earliest `phase = 0 AND status = 0 AND day_of_season <= today` row via `ix_sched_day`, apply the same `CanField` forfeit check and `AutopilotAttendedGames || IsAvatarAbsentOn(gameDay) || !IsAvatarActiveOn(gameDay)` auto-resolve check as `OnDayAdvanced`, so a save reloaded after a game-day crash while the avatar is absent, inactive, or facing a forfeit resolves cleanly instead of parking an unplayable interactive fixture."*
7. **Amendment 7 — Complete 10-Channel Guard-Invariance Suite & Shared Scratch Worlds (§3 D1-A/D7, §5 `season-pipeline`):**
   > *"In `season-pipeline`, assert all 10 guard-invariance channels from §10.1 Q1 (including default-active offseason transition on `DayOfSeason ∈ [1, 90] ∪ [280, 365]`, `RosterManager.Version == 54`, and `LineupRebuildCount == 0`), and share at most 1–2 generated v14 scratch worlds (`Slice135World`) across the new 13-5 suites to keep the default MC addition within `+1.9–4.0 s ≤ +10.0 s`."*

### 10.5 Opus review of the Math check and reconciliation — Claude Opus 5.5, 2026-11-23

**Verdict: accepted, with three corrections (C1–C3).** Amendments 1–7 and C1–C3 are folded into §1.2 (#2, #4), §3 (D1-A, D3-A, D4a/b, D5-A, D6-A, D7), §4 (items 3, 4, 5, 7) and §5 (`designations`, `attended-forfeit`, `season-pipeline`, beats). Argon's §10.1–§10.4 stay as authored. Argon wrote §10 under the owner's no-commit fallback; I checked its scope and committed it as `53652d2`. It adds only this brief's §10 (nothing deleted from §§1–9) and one `progress.md` line, and `git diff --check` was clean.

**Independently verified:**

- **Q1 proofs.** The subset-greedy argument holds at every `LineupBuilder` step. The featured-player caveat (Amendment 2) is real: `LineupBuilder.cs:79–83` forces a featured slot into the lineup, so a lock that passed the avatar would protect a fringe avatar. The starter split, re-derived from the calendar: 22 Tuesdays + 21 Saturdays = 43 for #1, 21 Thursdays for #2. Every postseason slot reaches only #1 and #2, and the gap into season 2 is 183 days.
- **Amendment 1 is a real boundary defect.** On a scratch v14 (`sqlite3` over `SchemaDefinitions.sql`, foreign keys on), with lock `98` written: Sunday `98` reads uncovered before the lock commits and covered after it, and Saturday `97` reads covered. That's the strict `>` rule working. I also switched the joins to `Team_Tiers`, matching 13-4a (both plans are a ≤ 768-row scan plus a primary-key lookup).
- **Amendment 4.** `MicroGame.cs:1314` captures on `bullpenUsed == 0`, and `starterPitches` starts at −1 and is backfilled after the game (`:1200`, `:1257–1264`). Beyond the skipped-index case, this is a **latent pre-13-5 bug in opener games**: `SelectPitcher` hands back `bullpenUsed = opener + 1` (`:766`), so an opener game today reports the last reliever's count as the starter's. The fix changes that report only in opener games, which need an unavailable rotation. The pitch-count band at `Program.cs:1797` samples games without absences, so no default-log line should move; the 13-5a log diff must show that.
- **Q2's coverage claims.** `LeagueGenerator.SecondaryNeighbours` (`:87–98`) gives no player C as a secondary, gives 2B/SS only to infielders, and gives the outfield only to outfielders and 1B. The buckets are as Argon describes.
- **`CompareQuality`** (`StarterSelection.cs:115–134`) is baked stuff + control, then stamina, then player id. That's consistent with Q3's slot-3 tie key, given C3.
- **Q5 arithmetic.** At G = 28, `0.20·RD_norm = 0.10 + RD/1400`; a 9–0 forfeit is 9/1400 = 0.006429, which is 36% of one win's 0.50/28.
- **Q9 and Q10.** At most 10 players per macro game and 15 per micro game, against College's 20; the D2d-C key packing fits 16-bit team ids.
- **Tier totals.** `BaseballQueries.LoadLeagueBattingTotals` / `LoadLeaguePitchingTotals(seasonYear, tier)` exist (`:1455`, `:1466`), so Q3's ΔP needs no new query.

**Corrections (applied; Argon to confirm in the 13-5a review):**

- **C1 — absence is measured on game days, not calendar days.** Q1, Q3 and Amendment 3 call a player healthy only if he's available on Monday (`lockDay + 1`), and score the avatar −6 per absent day across Monday–Saturday. Monday isn't a Pro game day. A player whose absence ends Tuesday morning would be inactive all week and miss three games for an absence that costs none.
  - *Applied:* healthy ⇔ not absent on any of the team's game days in the locked week (Tue/Thu/Sat, or the D3 slots in the postseason). If the team has no game that week, every player counts as healthy.
  - The avatar's ΔH = −12 per missed game. That keeps Argon's −36 floor at three games; an avatar who misses every game day goes to the unhealthy pool.
  - Q3's flip 2a moves its return day to lock day + 3, so it still flips (ΔH = −12). The Q4 fixture is unchanged: Tuesday's game is still missed.
- **C2 — `WasActive` reads only a row that covered the week just ending.** Rows persist across seasons. Without this rule, an avatar who was inactive in last season's final week would get a "re-activated" text at the next season's first lock. A row counts only when its `locked_through_day` equals this lock's day; otherwise the default (active) applies.
- **C3 — pitcher quality is the sims' baked stuff + control.** Q3's slot-3 score and Q4's fill key name `PitStuff + PitControl`, which reads like the raw `Player_Ratings` columns. `CompareQuality` ranks baked `PitcherRatings` (tier and person). They must match, or a person-baked rotation could split differently between the lock and game day. Batters keep the raw `BatPower + BatContact + BatDiscipline`, which is what `LineupBuilder`'s `BatScore` uses (`LeagueSimulator.cs:528`).

**Read with care:**

- **Q7's table and Q8's timings are forecasts.** No 13-5 code exists, and the session ran no harness. The shipped-source result (0 organic forfeits) is structural: absence consequences are avatar-scoped or gated on NPC state no shipped event moves.
- **Q6's +15 / −5** are data values with a rationale from the authored content's range, not a measurement. They're tunable after a playtest.
- **The `coach_college` placeholder** shouldn't be "Coach Vance": Vance County Vipers is a College team (Appendix A). Pick another placeholder; Sonnet names it.

**No owner-facing change:** the amendments and corrections refine the recommended options. None changes a D1–D8 choice or the sanctioned guard. **Next: the owner decides D1–D8.**
