# Real-Time Clock & Time Controls — Slice G

**Owner of this doc:** Opus 4.8 (architecture/design only — NO code). Per the standing roadmap
split, Opus owes this design *before* any build. Slice G is flagged in
[game-improvements-roadmap] / `docs/progress.md` as **"architecturally significant: the sim is
day-tick; must decide cosmetic intra-day clock vs. continuous simulation before any build."** This
doc makes that decision and specifies the seams. Likely **Fable 5** builds it (it touches the
day-advance driver, which is calibrated-core-adjacent); the standing review applies.

Grounded against the live code this session (No Blind Queries applies to design too):
`Assets/Core/TimeManager.cs` (`AdvanceDay`, the *only* time-advance path), `Assets/Core/GlobalState.cs`
(the calendar model — day is the finest grain), `Assets/Core/GameManager.cs` (the frame driver /
bus pump, `_Process` at `:588`, `ProcessMode = Always` at `:204`), `Assets/UI/BaseballDashboard.cs`
(the **sole** current caller of `AdvanceDay` — `OnPlayGamePressed`/`OnSkipDayPressed` at `:210`–`:226`,
the blocked predicate at `:291`–`:299`), `Assets/UI/TopBar.cs` (shell day+funds header),
`Assets/UI/BurnerPhone.cs` (`RefreshClock` at `:545` — date only), `Assets/Narrative/Events/EventDispatcher.cs`
(the 250 ms background poller that reacts to `current_day` moving), and
`Assets/Core/GameCalendar.cs` (day-ordinal → weekday/date math). `ui_conventions.md` and
`database_rules.md` are the acceptance frame.

---

## 1. Thesis & Scope — the one decision that governs everything

The user's ask (`GAME_IMPROVEMENTS.md` §Game UI):

> - *Actual time of day should be visible on the phone screen and should change like a real clock. (Maybe 1 minute in game = 1 second in real life)*
> - *Time should be able to pause, speed up, and slow down.*

and, from the screenshot, the Play/Skip location "should be where you can pause, speed up or slow
down time … [and] show the current time and date."

**The load-bearing fact:** the simulation has **no concept of time-of-day at all.** `GlobalState`'s
finest grain is `CurrentDay` (a 1-based absolute ordinal). A day is an **atomic unit of work**: one
`Clock.AdvanceDay()` commits `current_day + 1` in a single batch transaction, then publishes one
`DayAdvancedEvent` that fans out to *everything* — the six macro league sims each play a full day's
slate, the life sim runs all 24 hourly need-decay ticks, hustle/practice/family/child-rearing/autonomy
handlers tick, and (on a season boundary) development + promotion run. The gritty dispatcher, on its
own thread, notices `current_day` moved and evaluates the day. Nothing is continuous; **everything is
a discrete reaction to the day incrementing.**

Therefore the governing decision:

> **The intraday clock is a presentation/driver-layer construct. The simulation stays day-atomic and
> is not given hour/minute resolution.** A new *time driver* accumulates real-time into game-minutes,
> renders a ticking wall clock, and — when it crosses midnight — calls the **existing, unchanged**
> `Clock.AdvanceDay()` exactly as a click does today. Pause / speed / slow are multipliers on that
> accumulator.

This is the only option that respects the architecture (CLAUDE.md separation-of-concerns, the
zero-GC day-tick hot path, DB-as-source-of-truth). The rejected alternative — giving the sim genuine
intraday resolution — would mean re-plumbing thousands of entities with hour-of-day state, hourly
macro-sim slices, and an intraday transaction cadence. That is a different game engine, not a slice.
**We do not do that.** Everything below follows from the metronome model.

### 1.1 The reframe the user needs to hear (surface this before building)

At the proposed **1 game-minute = 1 real-second**, one game-day (1440 min) = **1440 real seconds = 24
real minutes.** A 365-day season would take ~146 real hours. That baseline ratio is a *savor-the-moment
ambience speed*, **not** the tool for traversing a multi-generational life sim. So Slice G's clock is
deliberately **two-tiered**:

- **Continuous clock (ambient / tactical):** the ticking wall clock the user asked for. Used to *live*
  a stretch of a day, watch time pass, and auto-pause when something needs you. Speed ladder tops out
  at a bounded rate (§6), not "traverse a decade."
- **Discrete step (strategic traversal):** the existing instant day-advance — today's "Skip Day" —
  **retained** as a one-press "jump to tomorrow." This is how you actually cover ground; the offseason
  already fast-sims. Bulk time travel stays discrete.

Both drive the *same* `AdvanceDay()`. The continuous clock is not a replacement for discrete advance;
it is an ambient layer *on top* of it. Stating this up front prevents the "why does a season take 2
hours" surprise.

**Four hard rules (carried from `ui_conventions.md` / `database_rules.md`):**

1. **No new sim resolution.** Zero `Assets/Simulation/` touch. `run_monte_carlo_batch` is inert by
   construction (prove it with `git diff --stat -- Assets/Simulation` — the 10/11/12 precedent). The
   day tick is byte-for-byte the same work whether a click or the clock triggered it.
2. **The day-advance gates are sacred.** The clock may *never* roll a day past a moment the player must
   act on — a game in flight, a pending attended game, a gritty-event choice, a succession choice, a
   hustle session. It consults the same predicate the dashboard already enforces (§4).
3. **Dirty-flag rendering, no per-frame allocation.** The clock label reformats only when the displayed
   minute changes; buttons re-evaluate disabled-state cheaply. No LINQ / string-format churn in
   `_Process` beyond the one-minute-tick reformat.
4. **Thin vertical slices (§7).** Each sub-slice leaves the game bootable and every screen reachable.

**Non-goals** (§8 expands): no positioning of schedule blocks on the clock (school 8am–3pm etc. — the
schedule is hour *budgets*, not clock coordinates); no per-frame persistence; no change to how the six
macro sims or the gritty poller work; no SceneTree pause.

---

## 2. Seam Audit — what the metronome needs, and what already exists

| Need | Live seam | New surface? |
| :--- | :-------- | :----------- |
| Advance one day | `TimeManager.AdvanceDay()` (`TimeManager.cs:64`) | **none** — called unchanged |
| "May I advance right now?" predicate | today it's **private** in `BaseballDashboard.RefreshDayControlsEnabled` (`:291`) — `_gameTask != null \|\| _awaitingPendingGame \|\| _refreshAfterDayTick \|\| GrittyEventChoices.HasPendingChoice \|\| Career.HasPendingSuccessionChoice` | **promote to a public `GameManager` predicate** (§4) so the driver and the dashboard share ONE definition |
| Day/season readout | `GlobalState.CurrentDay/SeasonYear/DayOfSeason`; `GameCalendar` for weekday/date | none |
| Human date string | `BurnerPhone.RefreshClock` (`:545`) already renders "TUE APR 14" | extended with time-of-day (§5) |
| Frame delta | Godot `_Process(double delta)` | the driver's own `_Process` |
| Background sim reaction | `EventDispatcher` polls `current_day` every 250 ms, reacts to movement (`EventDispatcher.cs:39`) | **none** — it does not care *how* the day moved |

**The reassuring finding:** the gritty dispatcher is already fully decoupled from the *trigger* of a
day change — it reacts to the `current_day` KV, not to a click. The continuous clock is invisible to
it. Likewise every `DayAdvancedEvent` subscriber. The metronome model slots in behind a single seam
(`AdvanceDay`) that the whole engine already funnels through.

---

## 3. Architecture — two new pieces, one promoted seam

### 3.1 `GameClock` — pure intraday time model (engine-independent, in `Assets/Core/`)

A small, Godot-free class mirroring `TimeManager`'s "pure logic, no engine" posture (so it is
unit-reasonable and Core stays harness-compilable). It owns **only** the cosmetic intraday state:

- `int MinuteOfDay` — 0..1439 (minutes since midnight). Starts at a canonical **wake time** (recommend
  **480 = 08:00**) on construction / each new day.
- `TimeSpeed Speed` — an enum: `Paused, Slow, Normal, Fast, Faster` (values below).
- `bool IsPaused => Speed == Paused` (or a separate manual-pause flag layered over an auto-pause flag,
  §4.1).
- `float Advance(float realSeconds)` → accumulates `realSeconds × minutesPerSecond(Speed)` into a
  `float` minute-accumulator; returns **whole game-minutes elapsed** and rolls `MinuteOfDay`. **Signals
  a midnight crossing** via a return flag / out-param `daysRolled` (0 or 1 — see §4.2 one-roll-per-frame).
- `void ResetToWake()` — called on each day rollover so the next day starts at 08:00 (or, if we persist,
  restores the saved minute — §3.4).

`GameClock` **never** calls `AdvanceDay` and never touches the DB or the bus. It is a metronome face,
nothing more. This keeps the "may I advance / actually advance" authority entirely in the driver +
GameManager, where the gates live.

### 3.2 `TimeControlBar` — the Godot driver + control cluster (in `Assets/UI/`)

A `Control`-derived scene that **replaces the Play/Skip button pair** in the dashboard header (the
screenshot's location) and becomes the sole day-advance driver. Its `_Process(double delta)`:

1. Reads `bool canAdvance = gm.CanAdvanceDay` (§4). If false, force `GameClock` into **auto-pause**
   (time cannot flow while a decision is owed) and render the paused face — the player is never carried
   past a gate.
2. If `canAdvance` and not manually paused: `int rolled = _clock.Advance((float)delta)`. If `rolled > 0`
   (midnight crossed), call `gm.Clock.AdvanceDay()` **once** (§4.2), then `_clock.ResetToWake()`.
3. Renders the clock readout ("2:47 PM") only when `MinuteOfDay` changed since last frame (dirty flag),
   and the date/day when `CurrentDay` changed (reuse `GameCalendar`, mirror `BurnerPhone.RefreshClock`).
4. Re-evaluates button disabled-states off `canAdvance` (cheap bool compare).

`ProcessMode = Always` (like GameManager `:204`) so the controls stay responsive if any future modal
pauses the SceneTree — but note we do **not** use SceneTree pause ourselves; "pause" is the `GameClock`
flag only (§4.3).

**Control cluster contents** (the header slot):

- ⏸ / ▶ **Pause/Resume** (toggles manual pause on `GameClock`).
- **Speed selector** — slow / normal / fast (three or four steps, §6). Renders the active step.
- ⏭ **Skip Day** — the retained instant single-day advance (today's Skip Day semantics verbatim:
  autopilot the avatar's game, jump to tomorrow). Independent of clock speed; always available when
  `canAdvance`.
- ▶ **Play Game** *(contextual)* — appears only on a day the avatar has a pending attended game
  (`Career.HasPendingGame` after the day's events drain). Pauses the clock and launches the
  interactive at-bat — today's "Play Today's Game" path (§4.4).
- **Clock readout** — "TUE · APR 14 · 2:47 PM" (weekday · date · time-of-day).

### 3.3 The Play/Skip reconciliation (what happens to today's two buttons)

Today's two buttons map cleanly onto the new model — **no behavior is lost:**

| Today | Slice G | Semantics |
| :---- | :------ | :-------- |
| **Skip Day** (autopilot game, advance) | ⏭ **Skip Day** (retained) *and* the continuous clock's midnight auto-roll | Both advance with the standing plan and autopilot any attended game. The clock is just "Skip Day, but paced and interruptible." |
| **Play Today's Game** (advance + play at-bat interactively) | ▶ **Play Game** (contextual) | Unchanged interactive-at-bat launch; now surfaced only when a game is actually pending, and it pauses the clock first. |

The mental model becomes: **let time ride** (clock flowing, days auto-advance, games autopilot) vs.
**grab the wheel** (pause; plan in the Calendar tab; press Play Game for your at-bat, or Skip Day to
jump). The clock auto-pauses on every decision gate (§4.1) so "let it ride" never sails you past
something you'd want to act on.

### 3.4 State & persistence — DECIDED: Option B (persist the exact time-of-day)

**User decision (2026-07-11): persist the intraday clock so it resumes exactly on reload.** The clock
restores to the minute you left it, not to a canonical morning.

**The cost is smaller than "schema change" implies — it is NOT a DDL change.** `Game_State` is a
key-value table (`GameStateQueries.cs:83` — upsert `INSERT … ON CONFLICT(key) DO UPDATE`). Persisting a
new value means **adding a KV *key***, exactly like `avatar_practice_credit`, `avatar_ex_partner_id`, and
the hustle faction reps were added — all without a schema bump. Concretely:

- **Two new `GameStateKeys` consts** (`GameStateQueries.cs:6`, the "add new keys here, never inline
  strings" home): `time_of_day_minutes` (long, 0..1439) and `time_speed` (long, the `TimeSpeed` enum
  ordinal — so the chosen pace resumes too).
- **No new table, no new column, no `PRAGMA user_version` bump, no `SchemaValidator` change, no
  migration step.** An old save simply lacks the keys → `TryGetInt64` returns false → default to the
  canonical wake time (480 = 08:00) and Normal speed. That absent-key default *is* the "migration" —
  the same graceful-absence pattern every additive KV key already relies on.
- **Write cadence — checkpoints only, never per frame.** Per-frame writes would violate the batch /
  zero-GC discipline (`database_rules.md`). The write rides the **already-existing save moments**:
  `GameManager.SaveNow()` (`GameManager.cs:911`) and `_ExitTree` (`:597`) — plus a cheap write on manual
  pause / speed-change (user-driven, infrequent). A normal quit therefore persists the exact minute and
  speed; only a hard crash falls back to the last checkpoint's minute (acceptable, and no worse than
  any other in-memory accumulator today). Reads happen once, at boot, when `GameClock` is constructed.

This keeps persistence a pure Game_State-KV concern with **zero DDL surface** — so even under Option B,
`SchemaValidator` re-runs green *unchanged* (§9). The earlier "schema-adjacent" framing overstated it;
the KV audit above is the accurate cost.

---

## 4. The gates — the safety-critical section

The clock's authority to advance is **exactly** the dashboard's current authority, promoted to a shared
seam so it cannot drift.

### 4.1 Promote the blocked predicate to `GameManager`

Add a public read-only predicate that aggregates the cross-cutting pending states GameManager already
owns or can reach:

```csharp
public bool CanAdvanceDay =>
       !_gameInFlight                       // an interactive at-bat task is running (dashboard-owned;
                                            //   expose via a small callback/flag the bar sets)
    && !GrittyEventChoices.HasPendingChoice // a gritty choice awaits the player
    && !Career.HasPendingSuccessionChoice   // an heir-succession choice is parked
    && !HasPendingHustleSession;            // an interactive hustle session is unresolved
```

(The dashboard's `_awaitingPendingGame` / `_refreshAfterDayTick` are *transient one-frame* flags of an
in-progress advance; the driver owns the advance now, so it tracks its own in-flight state rather than
reading those. The at-bat in-flight flag is the one piece currently private to the dashboard — the
cleanest wiring is for the dashboard to report game-start/game-end to the bar, or for the at-bat launch
to move into the bar entirely in a later sub-slice.)

**When `CanAdvanceDay` is false, the driver auto-pauses the clock** and shows a paused, reason-annotated
face ("Paused — decision needed"). The player resolves the gate; the clock resumes at its prior speed
(or stays paused if they manually paused).

### 4.2 One rollover per frame (back-pressure)

`GameClock.Advance` rolls **at most one** midnight per call; any surplus minutes stay in the accumulator
for next frame. Rationale: each `AdvanceDay()` is a *full day of sim work* (six macro sims + 24 life
ticks + persistence). Capping at one day/frame bounds per-frame cost and prevents a GC hitch or a very
high speed from doing five days of sim in a single frame. At 60 fps this still permits a high effective
ceiling (§6) — and the discrete ⏭ Skip Day / offseason fast-sim remain the tools for bulk traversal.

### 4.3 We do **not** use SceneTree pause

"Pause" is the `GameClock` accumulator flag only. The SceneTree keeps running (the bus must keep
pumping — GameManager is already `ProcessMode.Always` for exactly this reason). Pausing time ≠ pausing
the process. This avoids freezing UI animations, the gritty poller reaction, or the phone.

### 4.4 Interactive game vs. the flowing clock

On a day with a pending attended game, after that day's events drain (`Events.PendingCount == 0`,
`Career.HasPendingGame == true`), the driver **auto-pauses and surfaces ▶ Play Game** rather than
letting the clock autopilot past it. This preserves the "you never miss your at-bat" guarantee. If the
player instead presses ⏭ Skip Day (or was in a low-attention flow), the game autopilots — identical to
today's Skip. The interactive at-bat itself (the `_gameTask`, beat queue, `SequencerDrained` gating in
`BaseballDashboard._Process`) is **unchanged**; only its *launch trigger* relocates to the bar.

---

## 5. The phone clock (the "visible on the phone screen" ask)

`BurnerPhone`'s status-bar `ClockLabel` currently renders date only ("TUE APR 14",
`BurnerPhone.RefreshClock:545`). Slice G extends it to append the live time-of-day from the same
`GameClock` the bar reads ("TUE APR 14 · 2:47 PM"), dirty-flagged on the displayed minute (not per
frame). Both surfaces (dashboard bar + phone status bar) are **read-only views of one `GameClock`
instance** — the bar owns/drives it; the phone reads it. Wiring: expose the clock on `GameManager`
(e.g. `public GameClock Clock2 { get; }` — name TBD to avoid colliding with `Clock` the `TimeManager`;
suggest `TimeOfDay` or `Wall`) so any screen can read `MinuteOfDay` without reaching into the bar.

---

## 6. Speed ladder (proposed, tunable)

Minutes-of-game per real-second. Kept small and bounded (rule 4.2 caps effective day-rate regardless):

| Step | min/sec | real-time per game-day | Feel |
| :--- | :------ | :--------------------- | :--- |
| ⏸ Pause | 0 | — | Held |
| ▶ Slow | 1 | 24 min | The user's "1 min = 1 sec" — savor a moment |
| ▶▶ Normal | ~5 | ~4.8 min | Default ambient flow |
| ▶▶▶ Fast | ~30 | ~48 sec | Blow through a quiet day |

(A 4th "Faster" step is optional; the `Advance` one-roll-per-frame cap (§4.2) is the true ceiling —
even an absurd multiplier tops out near one day/frame. For *seasons*, use ⏭ Skip Day, not a speed.)
The exact numbers are a **Fable/Sonnet tuning knob** at build time, not a design commitment; the enum
and the multiplier table are the seam.

---

## 7. Sub-slices (thin vertical, each bootable)

1. **G-1 — `GameClock` model + persistence + shared gate seam.** Add `GameClock` (Core, pure); add the
   two `GameStateKeys` (`time_of_day_minutes`, `time_speed`) and seed the clock from them at boot
   (absent → 480 / Normal, §3.4); promote `CanAdvanceDay` to `GameManager`. No UI change yet; dashboard
   keeps driving via the new predicate (refactor its private blocked-check to read `gm.CanAdvanceDay`).
   *Verifiable: build green, day-advance behaves identically, all harnesses unchanged, `SchemaValidator`
   green unchanged (KV keys are not DDL).*
2. **G-2 — `TimeControlBar` replaces Play/Skip.** New scene in the dashboard header: Pause/Resume,
   speed selector, ⏭ Skip Day, contextual ▶ Play Game, clock readout. Drives `AdvanceDay` on midnight
   roll; auto-pauses on gates. Dashboard cedes the day-advance role (reports game in-flight to the bar).
   Writes `time_of_day_minutes`/`time_speed` at the existing checkpoints (`SaveNow`/`_ExitTree`) and on
   manual pause/speed-change (§3.4). *Verifiable live: clock ticks, pause holds, speed changes cadence,
   Skip Day jumps, a pending game pauses + Play launches the at-bat unchanged; quit + reload resumes the
   exact minute and speed.*
3. **G-3 — phone status-bar time-of-day.** Extend `BurnerPhone.RefreshClock` to read `GameClock` and
   show the ticking time. *Verifiable: phone clock advances in lockstep with the bar.*
4. **G-4 (optional refinement) — "pause each morning" toggle.** A user option (Settings tab, riding
   Slice B's OptionsCard) that auto-pauses at each day rollover so deliberate planners can plan every
   day while "let it ride" players leave it off. *Skippable / later.*

---

## 8. Non-goals (explicit)

- **No intraday sim resolution.** Schedule blocks stay hour *budgets*, not clock positions; the macro
  sims stay day-atomic; no hourly transactions. (This is the whole thesis.)
- **No per-frame persistence.** The intraday offset persists (Option B) but written **checkpoint-only**
  (`SaveNow`/`_ExitTree`/pause/speed-change), never per frame.
- **No change to the gritty poller, the six macro sims, development/promotion, or `DayAdvancedEvent`
  fan-out.** They react to `current_day`; the trigger is invisible to them.
- **No SceneTree pause.**
- **No generational time-travel via the continuous clock.** That is ⏭ Skip Day + offseason fast-sim's
  job (§1.1).
- **No `Assets/Simulation/` touch** → `run_monte_carlo_batch` inert by construction (prove with
  `git diff --stat`).

---

## 9. Verification & expected gates

Slice G is UI/driver + one pure Core model + one promoted predicate. Expected gate posture:

- **Build 0/0** (game + all 6 harness projects; `GameClock` is pure so CoreLoopHarness compiles it).
- **`run_monte_carlo_batch` NOT re-run-mandated** — `git diff --stat -- Assets/Simulation` empty by
  construction; state it, the 10/11/12 precedent.
- **`SchemaValidator` unchanged** — persistence (Option B) adds Game_State **KV keys**, not DDL, so
  `PRAGMA user_version` does not move and the validator re-runs green *unchanged* (§3.4).
- **CoreLoop / NeedsDecay / GrittyEvents** unchanged — the day tick is byte-identical work; the harness
  drives `AdvanceDay` directly and never sees the clock.
- **Live boot (godot MCP) on the real save:** clock ticks, pause/speed/Skip behave, a pending attended
  game pauses and Play launches the unchanged at-bat, phone clock tracks the bar, errors empty.

---

## 10. Decisions (all settled — nothing blocks the build)

- **Model:** metronome — cosmetic intraday clock drives the unchanged discrete `AdvanceDay`; sim stays
  day-atomic (§1). *Settled.*
- **Intraday persistence:** **Option B** — persist `time_of_day_minutes` + `time_speed` as Game_State
  KV keys (checkpoint-only writes; absent-key default = 08:00 / Normal), so the clock resumes exactly
  on reload. No DDL, no `user_version` bump, no `SchemaValidator` change (§3.4). *Settled 2026-07-11 by
  the user.*
- Speed-ladder numbers (§6) and the exact wake time remain build-time tuning knobs, not design
  commitments.

**The design is complete and unblocked.** Next step: Fable builds **G-1** (pure `GameClock` + KV
seed/restore + the promoted `CanAdvanceDay` gate), then G-2 (the control bar) and G-3 (phone
time-of-day).

---

*Related: [surface_the_sim.md] (the dashboard placement precedent this reuses), `ui_conventions.md`,
`database_rules.md`, `docs/game-idas/GAME_IMPROVEMENTS.md` §Game UI. Roadmap slot: Slice G in
`docs/progress.md`.*
