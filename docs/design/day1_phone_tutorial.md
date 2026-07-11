# Day-1 Phone Tutorial — Design Contract

**Author:** Claude Opus 4.8 (architecture) · **Date:** 2026-07-11 · **Slice:** GAME_IMPROVEMENTS roadmap item #2 ("Tutorial phone thread")
**Status:** DESIGN COMPLETE — this is the authoring contract Sonnet 5 builds Stage 2 against. No code here.
**Implements:** `docs/game-idas/GAME_IMPROVEMENTS.md` lines 37–38 ("Add a tutorial on how to play the game" / "Add a section on how to play the game in cellphone at the very start of the game to teach players the basics").

---

## 0. What this is — and what it deliberately is NOT

There are **two different things** a fresh player meets in their first in-game days, and this doc is about the second one:

1. **The narrative Act-1 onboarding arc** (`hs_onboarding_events.md`, shipped) — *story* beats: first bell of freshman year, meet Coach Malone, tryouts, the lunchroom. It tells the story of being a 16-year-old freshman. **This doc does not touch it.**
2. **The mechanical "how to play" tutorial** (this doc) — teaches the player how to *operate the game*: the phone tabs, the Time bar, the Plan-Today schedule, game days, the Bank/needs, saving. It coaches the UI; it tells no story.

These are **orthogonal and coexist**. The tutorial's welcome takes day 2 (the player's very first moment is "here's your phone"); the narrative arc's first beat shifts to day 3, and the two interleave cleanly thereafter (§7). Do **not** re-gate, duplicate, or restructure the narrative arc.

The delivery vehicle is the **existing gritty-event + contact-thread machinery** (`EventDispatcher`, `GrittyEventModel`, the JSON content-file format, the `text_message` delayed-text mechanic, the Burner Phone's Events/Messages tabs). This is exactly what the roadmap line calls for and is far cheaper than inventing a tooltip/overlay framework. **No new UI infrastructure, no engine change, no schema change** — the cleanest slice on the board (§3).

---

## 1. The problem this fixes

A brand-new player is dropped into a life-sim/baseball hybrid with a lot of surface: an eight-tab phone (Events, Calendar, History, Messages, Bank, Family, Marketplace, Settings), a real-time Time bar with pause/speed/skip that **just shipped** (`real_time_clock_slice_g.md`) and is the least self-evident control in the game, a per-day hour-allocation planner (Plan Today), a season calendar with sparse game days, an at-bat minigame, and standings/scouting tabs. Nothing currently explains any of it. The narrative onboarding tells them they're a freshman; it never tells them **which button advances time or how to plan a day**.

**The fix:** a short, self-paced tutorial delivered as a pinned **"Getting Started"** message thread plus **four lightweight acknowledgement cards** over the player's first several in-game days, teaching the actual controls in the order a new player meets them, skippable in one tap for anyone who already knows the game (including gen-2 heirs).

---

## 2. Design overview

### 2.1 Delivery model — a thread plus four checkpoints

- A new content batch `getting_started_events.json` with **4 carrier events** (`tut_welcome`, `tut_daily_loop`, `tut_game_day`, `tut_wrap`), each `scope:"avatar"`, `weight:1.0`, forming a short flag chain so they self-sequence one-per-eligible-day.
- Each carrier event renders as one card in the phone's **Events tab** with 3 acknowledgement choices, and seeds a burst of **texts into a dedicated "Getting Started" contact thread** (event-level fire-time text + per-choice delayed texts via `delay_days`). The thread is the persistent, **revisitable** "how to play" reference the user asked for — it lives in the Messages tab forever, re-readable any time.
- The teaching therefore lands two ways: the **card** is the "do this now" prompt the player acknowledges; the **thread** is the reference that accumulates and stays. The player *does* the actual actions (open Plan Today, press Skip Day, change speed) in the real UI — the event only coaches and confirms.

### 2.2 Why four carrier events (not eleven, not one)

- **Not one giant event:** a single event can seed many delayed texts, but it can't pace *interactive* acknowledgement across the natural "learn a thing, try it, learn the next" rhythm. Four checkpoints map to the four things a new player must grasp in order: **the phone → the daily loop (time + planning) → game day → the wrap-up (money/needs/saving).**
- **Not eleven (the narrative arc's count):** this is utilitarian, not story. Four beats cover the mechanics with the least intrusion, and — critically — the fewer day-blocking events, the gentler the interleave with the narrative arc (§7). Each carrier event still delivers *several* texts, so the Getting-Started thread reads richer than four lines.

### 2.3 Why the welcome fires first, on day 2 (the ordering decision)

`tut_welcome` gates on `tier==0` + `!tut_welcomed` only — nothing else — so it holds from the first evaluated day. Because `getting_started_events.json` sorts before every `hs_*` batch (§7.1) and the beat is weight 1.0, **`tut_welcome` wins day 2 and fires first**, ahead of the narrative arc's `hs_first_day`, which shifts to day 3. This is the chosen ordering: the player's very first in-game moment is "here's your phone," and the story's "first bell of freshman year" lands the next day.

What this does and doesn't cost:

1. **The narrative arc shifts back one day, nothing more.** `hs_first_day` fires day 3 instead of day 2, and the whole chain follows one day later — a pure time-shift, never a drop (the arc's own §4.4 pacing principle). The two arcs still alternate cleanly (§7.2).
2. **No shipped harness assertion breaks.** `GrittyEventsHarness.RunHsOnboardingArcChecks` asserts `hs_first_day` fires on day 2, but it does so over a **curated `onboarding + rookie` library that never loads the tutorial file** (`Program.cs` ~line 2112) — so that check stays green untouched. There is no existing whole-folder check that asserts any firing *day*, so nothing else moves either. The new day-2 ordering is proven only in the new `RunTutorialArcChecks` block, which loads both files together (§9.1).
3. **The batch stays fully self-contained.** With no `hs_started` dependency, every flag `tut_welcome`…`tut_wrap` reads is set within this batch — no cross-batch coupling to reason about (§6.3).
4. **Established saves still auto-suppress.** The `tier==0` gate means a returning save whose avatar has graduated past HS never sees the tutorial (§3.2); a save still at HS starts it on its next evaluated day.

*(The alternative — anchoring the welcome behind `hs_started` so the story keeps day 2 and the tutorial starts day 3 — was considered and set aside per the preference for the welcome to come first. It is recorded in §7.3 as a valid fallback.)*

---

## 3. Engine & content additions (the exact contract for Sonnet)

### 3.1 Engine additions: **none.**

The tutorial uses only what already exists: the `tier` `SubjectField` (shipped with the onboarding arc), `flag_active`/`flag_inactive` prerequisites with `min_days_since`, and the `set_flag` consequence. `text_message` (event-level string and choice-level `{ body, delay_days }`) is an existing field, not a new consequence. **No new `SubjectField`, no new `ConsequenceKind`, no new `RelationshipTargetSelector`, no dispatcher change, no schema DDL, no `user_version` bump.** This is materially cleaner than the onboarding arc (which added two `SubjectField`s) — nothing compiles into `Assets/Simulation/`, so **MLB bit-identity is byte-exact by construction and no re-band is possible.**

### 3.2 One new contact

Add to `Assets/Narrative/Contacts/contacts.json` (`RunContactRegistryChecks` pins `unresolved == 0`, so every `contact` id referenced by content must exist):

| id | display_name | role | carries |
|---|---|---|---|
| `getting_started` | Getting Started | `system` | all 4 tutorial events — the pinned how-to-play reference thread. Deliberately distinct from every narrative contact so coaching copy never pollutes a relationship thread. |

`role: "system"` is a new role string; the registry stores role as free text and the phone does not branch on it (`ContactDefinition` carries it as metadata), so no code change is required to accept it — confirm this against `ContactJson`'s role handling during the harness pass (the loader must not reject an unrecognized role; if it does, use an existing role such as `"unknown"` and note it). **Portrait:** the thread will fall back to the default portrait via `PortraitView`'s existing unknown-key fallback; a bespoke "Getting Started" portrait/icon is a follow-up left out of this slice (no image work in scope). Update the registry header comment to note the new system thread.

### 3.3 Consequences: flags only (deliberately consequence-free otherwise)

Tutorial choices set flags and seed texts. They apply **no** `person_stat`, `funds`, `stress`, `interest`, or `relationship` consequence — a *how-to-play* tutorial must never reward or penalize the player for reading it, and this keeps the batch trivially calibration-inert. This is a stated design rule, not an oversight.

---

## 4. The tutorial arc at a glance

4 events, all `scope:"avatar"`, `weight:1.0`, all carrying `{ "field": "tier", "op": "==", "value": 0 }`, all `contact: "getting_started"`, all `category: "general"`. File: `Assets/Narrative/Events/Content/getting_started_events.json`.

| # | id | gate (beyond `tier==0`) | teaches | sets |
|---|---|---|---|---|
| 1 | `tut_welcome` | `!tut_welcomed` (day-2 entry, no other gate) | The phone is mission control: the tab strip; **Events** (story cards + choices) vs **Messages** (texts, incl. this thread); where to find things. | `tut_welcomed` |
| 2 | `tut_daily_loop` | `tut_welcomed` (min 2), `!tut_daily_done` | The **Time bar** (Pause/Resume, Slow/Normal/Fast, **Skip Day**) and the **Plan Today** card (allocate the day across Sleep/Practice/Work/Free; School hours are calendar-mandatory; watch the Free-hours budget). | `tut_daily_done` |
| 3 | `tut_game_day` | `tut_daily_done` (min 2), `!tut_gameday_done` | **Game days**: they show on the **Calendar**; on a game day the Time bar surfaces a **Play Game** button (or Skip Day autopilots it); the at-bat; **League** tab = standings, **Player** tab = your stats/scouting. | `tut_gameday_done` |
| 4 | `tut_wrap` | `tut_gameday_done` (min 2), `!tut_done` | The **Bank** tab: funds, the five needs, the weekly cost-of-living bill; **Settings → Save Now** (the game also autosaves). "You're set — go live your life." | `tut_done` |

**The `min_days_since: 2` on links 2–4 is the interleave lever (§7):** it forces a one-day gap between tutorial beats, leaving the odd days to the narrative onboarding chain so the two alternate instead of the tutorial monopolizing the slot.

**Skip path:** `tut_welcome`'s third choice ("I've played before — skip the tips") sets **all four** end-flags at once (`tut_welcomed` + `tut_daily_done` + `tut_gameday_done` + `tut_done`), so no later tutorial event can ever hold. One tap dismisses the entire tutorial — this is also what makes a gen-2 heir's re-exposure a single trivially-dismissed card (§6.2).

---

## 5. Beat-by-beat authoring spec

Near-final copy below. Sonnet may polish prose; the **mechanics — flags, `min_days_since`, choice→flag wiring, `text_message` placement — are the contract.** Every choice sets its beat's flag(s) so the beat completes regardless of pick (the onboarding arc's "both choices set the flag" precedent). Each event ships an **event-level fire-time `text_message`** (the thread's opening line for that beat) and each choice seeds **choice-level delayed texts** (`{ body, delay_days }`) so the Getting-Started thread fills in over the following day or two rather than dumping all at once. Every choice carries an authored `outcome` (the Events-feed resolution line).

Copy references controls by their real on-screen labels (verified against `TimeControlBar.cs`, `ScheduleScreen.cs`, `BurnerPhone.cs`): **Pause / Resume / Slow / Normal / Fast / Skip Day / Play Game** on the Time bar; **Plan Today**, **Confirm**, **Clear**, the **Free hours** line on the schedule card; the tab names **Events / Calendar / Messages / Bank / League / Player / Settings**.

### 1 · `tut_welcome` — the phone
- **contact** getting_started · **category** general · **weight** 1.0
- **prereq:** `tier==0`, `flag_inactive tut_welcomed`  *(no other gate — this is the day-2 entry)*
- **prompt:** "New phone, new life. This thing is how you run all of it. Up top are your tabs — **Events** is where moments like this land, **Messages** is where your texts live (including these tips). Swipe around whenever you're lost."
- **text_message (fire-time):** "Welcome. I'll drop you a few tips here over the next couple days — this thread stays put if you want to reread it."
- **choices:**
  1. "Show me around" — aw 3 — **outcome:** "You thumb through the tabs. It's a lot, but it's all in one place." — sets `tut_welcomed` — **texts:** `{ body: "Events = story + choices. Messages = texts like this. Calendar shows your season; Bank is money, needs, and bills.", delay_days: 0 }`, `{ body: "No rush to master it. The next tip covers how a day actually moves.", delay_days: 1 }`
  2. "Just the essentials" — aw 2 — **outcome:** "Fine — the short version it is." — sets `tut_welcomed` — **texts:** `{ body: "Two tabs matter most at first: Events (this kind of card) and Bank (your money and needs). We'll cover the rest as it comes up.", delay_days: 0 }`
  3. "I've played before — skip the tips" — aw 1 — **outcome:** "Say no more. You know where everything is." — sets `tut_welcomed`, `tut_daily_done`, `tut_gameday_done`, `tut_done` — **texts:** `{ body: "You're all set. This thread's here if you ever want it.", delay_days: 0 }`

### 2 · `tut_daily_loop` — time + planning
- **contact** getting_started · **category** general · **weight** 1.0
- **prereq:** `tier==0`, `flag_active tut_welcomed` (min 2), `flag_inactive tut_daily_done`
- **prompt:** "Time moves on its own now. The bar up top runs the clock — **Pause** it, set **Slow / Normal / Fast**, or hit **Skip Day** to jump straight to tomorrow. Before a day rolls over, open **Plan Today** to decide how you spend it."
- **text_message (fire-time):** "The clock ticks in real time — Fast burns a day in a couple minutes, Skip Day ends it now."
- **choices:**
  1. "Walk me through planning a day" — aw 3 — **outcome:** "You open Plan Today and slide the hours around until the day adds up." — sets `tut_daily_done` — **texts:** `{ body: "Plan Today splits your hours across Sleep, Practice, Work, and Free time. School hours are locked in on school days — the card folds them in for you.", delay_days: 0 }`, `{ body: "Watch the 'Free hours' line — if it says you're over, trim a block before you Confirm.", delay_days: 1 }`
  2. "Just the clock, thanks" — aw 2 — **outcome:** "You get the hang of the bar — pause, speed up, skip. Simple." — sets `tut_daily_done` — **texts:** `{ body: "Paused, the world holds. It also auto-pauses when something needs you — a choice, a game, a deal.", delay_days: 0 }`
  3. "Got it" — aw 1 — **outcome:** "You've got the daily rhythm. Onward." — sets `tut_daily_done`

### 3 · `tut_game_day` — playing ball
- **contact** getting_started · **category** general · **weight** 1.0
- **prereq:** `tier==0`, `flag_active tut_daily_done` (min 2), `flag_inactive tut_gameday_done`
- **prompt:** "Game days are marked on your **Calendar**. When one lands, the Time bar swaps in a **Play Game** button — tap it to step into the at-bat yourself, or **Skip Day** to let the team play it out. Afterward, **League** has the standings and **Player** has your stat line."
- **text_message (fire-time):** "Game day soon. Play it live for the at-bats, or skip and let it sim — your call every time."
- **choices:**
  1. "How does the at-bat work?" — aw 3 — **outcome:** "You picture stepping in — read the pitch, pick your moment." — sets `tut_gameday_done` — **texts:** `{ body: "At the plate you read the pitch and choose how to attack it. Your ratings do the rest. It's optional every game — skipping just autopilots it.", delay_days: 0 }`, `{ body: "Check the Player tab after to see how the line moved, and League for where the team sits.", delay_days: 1 }`
  2. "I'll just sim them for now" — aw 2 — **outcome:** "You decide to let the team handle the games for a while. That's fine." — sets `tut_gameday_done` — **texts:** `{ body: "Skip Day plays your games for you — no penalty. Jump in live whenever you want the bat.", delay_days: 0 }`
  3. "Got it" — aw 1 — **outcome:** "Game days, handled." — sets `tut_gameday_done`

### 4 · `tut_wrap` — money, needs, saving
- **contact** getting_started · **category** general · **weight** 1.0
- **prereq:** `tier==0`, `flag_active tut_gameday_done` (min 2), `flag_inactive tut_done`
- **prompt:** "Last one. The **Bank** tab is your money, your five needs (keep them off the floor), and the weekly cost-of-living bill. **Settings** has **Save Now**, though the game saves itself as you go. That's the whole loop — now go live it."
- **text_message (fire-time):** "You're set. Keep your needs up, keep an eye on the bills, and the rest is your story to write."
- **choices:**
  1. "Thanks — I'm set" — aw 3 — **outcome:** "You pocket the phone and get on with it." — sets `tut_done` — **texts:** `{ body: "That's everything. This thread stays here if you need a refresher. Good luck out there.", delay_days: 0 }`
  2. "What happens if a need bottoms out?" — aw 2 — **outcome:** "You make a mental note to keep an eye on the meters." — sets `tut_done` — **texts:** `{ body: "Let a need crater and it starts dragging on everything else — eat, sleep, wash up, see people, stay fit. The Bank tab shows all five.", delay_days: 0 }`
  3. "Got it — done" — aw 1 — **outcome:** "Tutorial's behind you. Go play." — sets `tut_done`

---

## 6. Flag graph, closure & the skip path

### 6.1 The chain

```
 (creation → day 2, the first evaluated day)
        │
        ▼  tier==0, !tut_welcomed          ← no other gate; wins day 2
   tut_welcome ──sets──▶ tut_welcomed
        │                     └──(min 2)──▶ tut_daily_loop ──▶ tut_daily_done
        │                                        └──(min 2)──▶ tut_game_day ──▶ tut_gameday_done
        │                                                           └──(min 2)──▶ tut_wrap ──▶ tut_done
        │
        └── skip choice ──sets──▶ tut_welcomed + tut_daily_done + tut_gameday_done + tut_done  (chain short-circuits)
```

### 6.2 Closure analysis (`check_event_graph_integrity` #1–#6 pre-clearance)

- **Every `flag_active` read is set by an event *in this batch*:** `tut_welcomed`←1, `tut_daily_done`←2 (or skip), `tut_gameday_done`←3 (or skip). No dangling reads, no cross-batch dependency (§6.3). ✓
- **No orphan set_flags:** `tut_welcomed`/`tut_daily_done`/`tut_gameday_done` are each read by the next link. `tut_done` is the terminal one-shot guard, read self-referentially as `flag_inactive` on `tut_wrap` — the documented once-ever idiom (`got_report_card` precedent), not an orphan. ✓
- **≥3 choices:** all 4 events carry exactly 3. ✓
- **Scope (#6):** all 4 are `scope:"avatar"`. ✓
- **Tier reachability (#5):** entry gates `tier==0`, which a fresh HS avatar satisfies and a graduated/pro avatar never does; the chain reaches `tut_done` and stops (no infinite loop — every link is one-shot, no cooldown repeaters). ✓
- **No dead ends:** every event's flag opens the next link or is the terminal beat. ✓

Sonnet must additionally **add `getting_started` to the skill's contact list** and confirm the field-vocabulary line already covers `tier` (added by the onboarding pass) — no new field to register here.

### 6.3 Self-contained batch (no cross-batch dependency)

Every flag `tut_welcome`…`tut_wrap` reads is set within `getting_started_events.json` itself, so the batch closes on its own — `check_event_graph_integrity` passes over the tutorial file in isolation *and* over the merged folder. The tutorial's only interaction with the narrative arc is **ordering** (it takes day 2, shifting `hs_first_day` to day 3, §7), never a flag read. This is a deliberate simplification over the coupled alternative (§7.3).

### 6.4 Heir / veteran re-exposure

Flags are per-player, so a gen-2 heir (a fresh tier-0 avatar) will re-qualify for the tutorial. This is **acceptable and cheap**: the heir sees exactly one `tut_welcome` card and taps "I've played before — skip the tips," which short-circuits the whole chain (§4). A fully once-ever-per-save suppression would require gating on a `Game_State` key, which the `ConditionEvaluator` cannot read without an engine change — deliberately out of scope. The skip path is the sanctioned mitigation.

---

## 7. Day-2/3 competition audit & interleave with the narrative arc (load-bearing)

The dispatcher fires **at most one event per subject per day**, iterating events in **alphabetical file-load order**, first satisfied-and-won event wins and `break`s (`hs_onboarding_events.md` §5, verified in `EventDispatcher.EvaluateDay`). So the tutorial and the narrative arc **cannot both fire on the same day** — they serialize. The design goal is a clean **alternation**, never a monopoly, with nothing dropped.

### 7.1 File order

Content files load alphabetically. Current folder: `career_arc`, `child_rearing`, `core_events`, `hs_dating`, `hs_onboarding`, `hs_school_life`, `marriage_and_family`, `rookie_season`, `roster_availability`. **`getting_started_events.json` sorts after `core_events` and before every `hs_*` batch** — so once a tutorial beat is eligible on a given day, it is evaluated *before* the onboarding chain and wins that day's slot.

### 7.2 The interleave, day by day (fresh tier-0 avatar, full folder)

Tutorial links carry `min_days_since: 2`; onboarding week-one beats carry `min_days_since: 1`. `tut_welcome` takes day 2; that one-day gap alternates the rest:

| Day | Fires | Why |
|---|---|---|
| 2 | `tut_welcome` (tutorial) | Gated `tier==0` + `!tut_welcomed` only; sorts before `hs_onboarding`, wins the first evaluated day. Sets `tut_welcomed`. |
| 3 | `hs_first_day` (narrative) | `tut_daily_loop` needs `tut_welcomed` 2 days old → not until day 4. `hs_first_day` (weight 1.0, `!hs_started`) fills the gap. Sets `hs_started`. |
| 4 | `tut_daily_loop` (tutorial) | `tut_welcomed` now 2 days old; sorts first, wins over `hs_meet_coach`. Sets `tut_daily_done`. |
| 5 | `hs_meet_coach` (narrative) | `tut_game_day` needs `tut_daily_done` 2 days old → day 6. Onboarding fills the gap. |
| 6 | `tut_game_day` (tutorial) | Sets `tut_gameday_done`. `hs_tryouts` bumped. |
| 7 | `hs_tryouts` (narrative) | `tut_wrap` needs `tut_gameday_done` 2 days old → day 8. |
| 8 | `tut_wrap` (tutorial) | Sets `tut_done`. **Tutorial complete.** |
| 9+ | narrative arc only | No tutorial event can hold; the onboarding spine continues, ~1 beat behind its original schedule — a harmless time-shift (the arc's own §4.4 principle: beats shift, never drop). |

Result: **tutorial, story, tutorial, story, …** — the welcome comes first, then one meaningful card per day alternating, both arcs complete inside two in-game weeks. The `hs_crush_forms` romance beat (weight 0.05, `hs_dating`) remains a ~5%/day background competitor exactly as the onboarding doc already accepted; on a day it wins, it shifts *both* schedules by one day, still harmless.

### 7.3 The alternative not taken: anchor the welcome behind `hs_started`

The other option was to gate `tut_welcome` on `{ "flag_active": "hs_started", "min_days_since": 1 }` so the narrative arc keeps day 2 and the tutorial starts day 3. It was set aside per the preference for the welcome to fire first, but it remains a valid fallback: it costs a cross-batch flag read (validate over the merged library) and shifts the tutorial one day later, giving a **story, tutorial, story, …** cadence instead of **tutorial, story, tutorial, …**. The shipped `RunHsOnboardingArcChecks` curated-library check (`hs_first_day@2`) is unaffected either way, since it never loads the tutorial file — so the choice between the two is purely a product call about which card the player sees first, not a harness constraint.

---

## 8. Constants summary (first-pass, tunable as pure data edits)

| Constant | Value | Rationale |
|---|---|---|
| Carrier count | 4 | Phone → daily loop → game day → wrap. |
| Beat weight | 1.0 | Guaranteed fire once the window opens — a tutorial must not lose a coin flip. |
| Entry anchor | none (`tier==0`, `!tut_welcomed`) | Fires day 2, ahead of the narrative first beat, which shifts to day 3 (§2.3/§7). |
| Inter-beat `min_days_since` | 2 | Forces alternation with the onboarding chain (§7.2). |
| Consequences | flags only | A how-to-play tutorial never rewards/penalizes (§3.3); trivially calibration-inert. |
| Delayed-text spread | 0–1 days | Thread fills over the beat's day and the next, not all at once. |
| Skip | `tut_welcome` choice 3 sets all 4 end-flags | One-tap dismissal for veterans/heirs (§6.4). |

All of the above move via pure JSON edits + a harness re-run — no sim touch, no re-band.

---

## 9. Verification & harness contract (Stage 2)

### 9.1 GrittyEventsHarness additions

- **Bump the two whole-folder event-count literals `79 → 83`** (`RunContentChecks`'s `wholeFolder.Count == 79`, ~line 247; the contact-resolution check's `allEvents.Count == 79`, ~line 379). Adding 4 events moves the folder total; **no other `== N` literal changes** (the onboarding pacing check loads a curated 2-file library, not the folder) — and that curated check, including its `hs_first_day@2` assertion, stays green untouched because it never loads `getting_started_events.json`.
- **Contact resolution:** the whole-folder `unresolved == 0` check must still pass — it will only if `getting_started` is added to `contacts.json` (§3.2). Confirm all 4 tutorial events resolve their contact and are tagged non-unknown.
- **Loader accept:** the new file parses; `tut_welcome`…`tut_wrap` all resolve via `TryGetById`; each is `scope:Avatar`, weight 1.0, carries the `tier==0` gate, ships an event-level `text_message`, and carries exactly 3 choices each with an authored `outcome`.
- **New `RunTutorialArcChecks` World integration block** (mirror the onboarding block's shape, `Program.cs` ~line 2111), loading **`getting_started_events.json` + `hs_onboarding_events.json` together** (the real interleave surface):
  - (a) **Welcome-first ordering:** `tut_welcome` fires on day 2 and `hs_first_day` shifts to day 3 — proving the tutorial preempts the first slot and the narrative arc still starts, one day back.
  - (b) **Alternation:** across days 2–8, assert the §7.2 schedule exactly — `tut_welcome@2`, `hs_first_day@3`, `tut_daily_loop@4`, `hs_meet_coach@5`, `tut_game_day@6`, `hs_tryouts@7`, `tut_wrap@8` — proving deterministic alternation with nothing dropped (every week-one beat still fires, just time-shifted).
  - (c) **No double-fire:** no tutorial event id appears twice in `world.Fired`.
  - (d) **Skip path:** a second world where `tut_welcome`'s skip choice is forced (autopilot-weight it, or drive the choice) → assert `tut_daily_loop`/`tut_game_day`/`tut_wrap` **never** fire and `tut_done` is set.
  - (e) **Veteran suppression:** an avatar pre-seeded with `tut_done` → no tutorial event fires across N days.
  - (f) **Tier suppression:** a `tier>=1` avatar → no tutorial event fires (the `tier==0` gate holds), even with `hs_started` set.
- **`check_event_graph_integrity`**: closure per §6.2 — the batch is self-contained (§6.3), so it passes both in isolation and over the merged folder; `getting_started` added to the skill's contact list.

### 9.2 Gate suite (Stage 2 exit)

`dotnet build` 0/0; **GrittyEvents** current total → +the new tutorial checks, exit 0; **`run_monte_carlo_batch` 345/345 with MLB bit-identity byte-exact** (no `Assets/Simulation/` touch, no poll-SQL change — confirm the guard is untouched, do not re-band); **CoreLoop** 22/22; **NeedsDecay** 102/102; **SchemaValidator** 111/111 (no schema touch); live godot-MCP boot on the real save — **"gritty events 83"** logged, the Getting-Started thread appears in Messages by ~day 3, errors empty, clean stop.

### 9.3 Playtest checklist deltas (`docs/hs_manual_playtest_checklist.md`)

- On a fresh save, day 2 opens the **Getting Started** thread (the phone welcome), then day 3 delivers **first-day-of-school (Mom)**; over days 2–8 tutorial tips and story beats **alternate**, one card per day, and the Getting-Started thread accumulates in Messages (revisitable).
- The **skip choice** on the first tip dismisses the rest of the tutorial (no further Getting-Started cards).
- A gen-2 heir sees exactly one Getting-Started welcome card (dismissible), not the whole thread again.
- Fix the header gate-count to the new GrittyEvents total.

---

## 10. Model handoff

- **Stage 2 → Sonnet 5:** build against this contract — `getting_started_events.json` (§4/§5), the one `getting_started` contact (§3.2), the two count-literal bumps + `RunTutorialArcChecks` + the merged-library integrity run (§9.1), the skill/checklist/progress.md docs, all gates (§9.2). Direct precedent: the `hs_onboarding_events.json` batch (this is a strictly smaller, engine-free version of it).
- **Stage 3 → Fable 5:** standing review — the welcome-first day-2 ordering and the §7.2 alternation holding in the full-folder World check, that the shipped curated onboarding check (`hs_first_day@2`) stays green, that no tutorial event can reach a non-tier-0 or veteran avatar, content vs. this contract (≥3 choices, flag closure §6.2, self-contained batch §6.3); independent gate re-run incl. MC bit-identity; sign-off + memory.
```