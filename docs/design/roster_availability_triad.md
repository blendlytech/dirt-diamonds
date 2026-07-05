# Design — Roster Availability & the Arrest / Injury / Suspension Triad (Phase 8c)

**Author:** Claude Fable 5 (engine surface, shipped 2026-07-05) · **Phase:** 8c per `docs/phase_8_9_interleave_plan.md` · **Status:** engine COMPLETE and harness-proven; this doc is the contract for **Sonnet 5's content half** (the event JSON that actually fires arrests, injuries and suspensions). Companion to `gritty_event_framework.md` (whose §4 deferral note reserved exactly this consequence type) and `hustles_narcotics_fencing.md` §8/§12 (the risk stats 8b writes, and the tail-pricing frame).

Until 8c, a rostered player was unconditionally in every game — `LeagueSimulator`/`MicroGame` build fixed lineup/rotation/bullpen slots at `Initialize()` and no `IsAvailable` concept existed anywhere. This pass adds the availability dimension end to end without touching the per-PA hot path or the calibrated core.

---

## 1. The engine surface (what already exists — do not rebuild)

```
 gritty event choice ──"absence" consequence──► EventConsequenceApplier
   │ (own batch)  writes Player_Absences (schema v8, keep-later upsert)
   │ (post-commit) publishes PlayerAbsenceChangedEvent (Core, primitives only)
   ▼
 AvailabilityLedger (Assets/Simulation/Baseball/, RivalryLedger pattern)
   │ Version + day-gated cache rebuilds — never per PA
   ▼
 LeagueSimulator · MicroGame · CareerManager · GameManager
```

- **Absent** (`current_day < until_day`): the slot is shadowed by a nameless **replacement-level call-up** — every rating 40 (tier-shifted like a real player), stats credited to nobody (a discard line the flush never reads), no PED costs, no rivalries. Absent starters/relievers still burn their rotation/bullpen turn.
- **Rusty** (injury only, `until_day ≤ day < penalty_until_day`): the player plays with every effective rating docked `rating_penalty` points. The penalty is **engine-computed at apply time** from the subject's live `health_ceiling`: `round(12 · (100 − health)/100)` — a health-100 player heals clean, a broken-down one returns at up to −12 (rivalry-magnitude). The rust window is as long as the absence itself.
- **The avatar**: on an absent day the attended game still happens but resolves straight through the autopilot (the call-up bats in their slot; no pending interactive game ever parks). `CareerManager.IsAvatarAbsentOn(day)`, `GameManager.Absences` (the ledger) and `GameManager.AvatarScheduleLocked` are the UI's read surface.
- **Arrest additionally locks the daily schedule** (`SubmitDaySchedule` drops the submission while jailed) — jail days autopilot entirely. Injury/suspension leave life scheduling alone; they only take the Game away.
- Persistence: `Player_Absences` (one row per player, longest-absence-wins), hydrated into the ledger at boot. Survives save/load; proven by MonteCarloHarness's round-trip check.

## 2. The content contract (Sonnet's half)

**Consequence JSON** (closed enum — anything else is a load-time error):

```json
{ "type": "absence", "reason": "injury" | "suspension" | "arrest", "days": N }
```

- `days` is a whole number ≥ 1. **Semantics: the subject misses exactly `days` game days** — the row is written as `until_day = fire_day + days + 1` because an event fires *after* its day's games have played. In-season, days = games; offseason days burn absence without costing games (disclosed).
- Composes freely with the rest of the vocabulary in one choice — the canonical suspension shape is `absence` + `detection_risk` (negative — serving time cools heat) + `set_flag` (the record that cascades later).
- Overlapping absences don't stack: **whichever ends later wins wholesale** (SQL and ledger apply the identical rule). A shorter absence landing on an already-benched player is a silent no-op — author accordingly.

**What to key the triad on** (the 8b §8 contract, all live today):

| Trigger | Prerequisite shape | Guidance (from §12 of the hustles doc) |
|---|---|---|
| **Suspension** (PED test, league discipline) | `detection_risk >= N` (tiers ≥60 / ≥80 / ≥90) | 5–15 days, rising with the tier; pair with `detection_risk: −30..−50` and a `served_suspension`-style flag |
| **Arrest** | `flag_active: narc_watchlist` (+ high `detection_risk`) | 20–40 days; this is the tail with real economic teeth (schedule locked); set a record flag (`record_arrest`) for future cascades |
| **Injury** | `health_ceiling < 50` (the `injury_scare` event already keys near this) | 7–21 days; rust is automatic — do NOT also author a rating debuff |

- Weight discipline per `check_event_graph_integrity`: scope-`any` triad events stay at low weights (~0.1 cap) — every rostered NPC is a candidate subject, and a league-wide arrest wave is a content bug, not drama. Cooldowns and flag-gating are the pacing tools.
- Run `check_event_graph_integrity` on the batch, and bump `GrittyEventsHarness`'s folder-merge count/id list (the known gotcha, every content batch).

## 3. Engine guarantees (harness-proven, MonteCarloHarness suite "Phase 8c roster availability")

1. **No absences ⇒ bit-identical**: an attached-but-empty ledger reproduces a no-ledger season byte for byte; the MLB bit-identity regression guard is untouched (PA 48384, H 10969, ER 5237 — exact).
2. A suspended batter's PA and a suspended starter's G freeze for exactly the absent window and resume after; teammates keep accruing (the team never forfeits).
3. A benched avatar in interactive mode never parks a pending game; their season line freezes; the day still advances normally through the existing Play/Skip flow (the "no game today" branch).
4. DB round-trip: `SetAbsence` keep-later in SQL ≡ ledger merge in memory; `LoadActiveAbsences → Seed` reproduces state exactly at boot.

## 4. Disclosed simplifications (deliberate, revisit later)

- One absence per player, longest-wins — concurrent shorter absences (jail during a longer suspension) vanish, including their schedule-lock nuance.
- Replacement call-ups don't affect the baked team-defense byte, and their fielding is uncounted — defense stays at the healthy-roster bake.
- Suspension is **day-denominated** ("5 days" = 5 games in-season, 0 games if it straddles the offseason).
- A day plan submitted *before* an arrest lands still runs its (jailed) day; only new submissions are locked. Jail days have no jail-specific needs model — they autopilot like free days.
- W/L accounting: a shadowed starter's decision lands on the discard line, so league W = L = games holds only in absence-free worlds (the career-suite identity check runs in one).
- No dedicated "skip to release" jail UI — the existing day loop clicks through; UX polish belongs to the future copy/layout pass.

## 5. Model split

- **Fable 5 (done, this pass):** schema v8, `PlayerQueries` absence surface, `AvailabilityLedger`, `ConsequenceKind.Absence` + JSON + applier (incl. the rust formula and the `+1` day math), replacement shadowing in both sims, `CareerManager`/`GameManager` wiring, MonteCarloHarness +14 / GrittyEventsHarness +8 checks, §12 re-band addendum in the hustles doc.
- **Sonnet 5 (next):** the triad content batch (§2 above), `check_event_graph_integrity`, the GrittyEventsHarness folder-merge bump, and the ScheduleScreen/AttendedGameScreen copy for "out until day N" / jail lock (read `GameManager.Absences` + `AvatarScheduleLocked`; UI stays read-only per `ui_conventions.md`).
- **Escalate to Fable** only if content wants a resolver-level knob (rust ceiling, replacement rating, stacking semantics) — those sit behind `run_monte_carlo_batch`.
