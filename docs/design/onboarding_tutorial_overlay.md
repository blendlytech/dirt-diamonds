# Onboarding Tutorial Overlay (Day 1) + Self-Explaining Needs UI

**Status:** Design (Opus 4.8) — supersedes the delivery mechanism of `day1_phone_tutorial.md`, not its intent.
**Date:** 2026-07-11
**Slice:** Onboarding-2 ("teach the game in the first two days, especially Needs")

---

## 1. Why this exists — the constraint that forced it

The shipped tutorial (`getting_started_events.json`, commit `a8be02b`) rides the Gritty Event dispatcher. That
dispatcher makes two rules non-negotiable:

| Rule | Where | Consequence |
|---|---|---|
| The boot day is recorded as *already lived*; only day **advancement** evaluates | `EventDispatcher.cs:91-93`, `:138-142` | **No event can ever fire on day 1 of a fresh save.** |
| At most **one** event fires per subject per day | `EventDispatcher.cs:226` | Days 1–2 offer exactly **one** avatar card, total. |

The boot-day rule is load-bearing (it is what stops a reload from re-rolling a day already lived) and the
one-per-day valve is what the Act-1 avatar-starvation fix depends on. Neither is worth breaking for a tutorial.

Therefore: **a tutorial that covers the game in the first two days cannot be delivered by the event system.**
It has to be a UI surface. That is what this document specifies.

### 1.1 The real gap — bigger than "needs are under-explained"

The shipped tutorial gives Needs exactly one clause: *"your five needs (keep them off the floor)."* But the
single most load-bearing rule in the entire life sim is written down **nowhere** — not in the tutorial, not in
the UI, not in a tooltip:

> **Unallocated Free hours are the hours in which your player keeps himself alive.**

`LifeSimManager.cs:687` runs the utility AI once per free hour, which auto-selects Eat / Shower / Sleep /
Socialize / Workout. A day planned with 24 hours of school + practice + work has removed every hour in which
the avatar eats. The planner renders this as the bare string `"Free hours: 6"` (`ScheduleScreen.cs:49`) and
never once hints that those hours are doing anything at all.

A player can lose this game to a rule we never told them. **Fixing that line is the highest-value change in
this slice** — higher than the overlay itself.

---

## 2. Scope

**In scope**

1. `TutorialOverlay` — a first-run, day-1, skippable, resumable step sequence (§3, §5).
2. Permanent UI upgrades that outlive the tutorial: the Needs card (§4.1) and the Free-hours line (§4.2).
3. A **Replay tutorial** button in the phone's Settings tab (§3.4).
4. Retirement of the four `tut_*` events, returning the day-2 slot to the story arc (§6).

**Non-goals**

- No change to `EventDispatcher`, `NeedsEngine`, `UtilityCalculator`, `LifeSimManager`, or `ActionCatalog`.
  **Zero simulation touch** — this slice is calibration-inert by construction (see §7).
- No schema change. The one new persisted value is a `Game_State` KV row (§3.3).
- No new hustle, no new content arc.

---

## 3. Architecture

### 3.1 The overlay scene

`Assets/UI/TutorialOverlay.tscn` + `TutorialOverlay.cs`, root type **`Control`** (full-rect), per
`ui_conventions.md` ("UI scripts inherit from the matching Control-derived type, never plain Node").

It is a **permanent sibling in `Main.tscn`**, self-hiding via its own `Visible` flag, never freed by a screen
swap — the exact pattern `EquipmentShopScreen` already uses (`Main.cs:14-16`, `:38`, `:43`). It sits after
`EquipmentShopScreen` in the tree so it draws above it.

Structure:

```
TutorialOverlay (Control, full rect, mouse_filter = Stop)
└── Scrim (ColorRect, full rect, ~0.55 alpha)
    ├── Spotlight (ColorRect / NinePatch — optional per step, §3.2)
    └── Card (PanelContainer, theme_type_variation = "Card")
        └── CardLayout (VBoxContainer)
            ├── StepCounterLabel   ("Step 3 of 8", CaptionLabel)
            ├── TitleLabel         (HeadingLabel)
            ├── BodyLabel          (AutowrapMode = WordSmart)
            ├── DiagramContainer   (optional per step — §5 uses it for the needs bars)
            └── ButtonsRow (HBoxContainer)
                ├── SkipButton     ("Skip tutorial")
                ├── BackButton
                └── NextButton     (primary)
```

The step table is **data, not control flow** — a `readonly TutorialStep[]` in the script (or a
`tutorial_steps.json` if the builder prefers; either is acceptable, but player-facing copy must not be
scattered through C# logic, per `ui_conventions.md` §Style).

```csharp
private readonly record struct TutorialStep(
    string Title,
    string Body,
    TutorialTarget Target,   // None | TimeBar | PhoneTabs | PlanToday | NeedsCard | BankFunds | SettingsSave
    bool ShowNeedsDiagram);
```

### 3.2 Spotlighting — Main is the bridge, the overlay never reaches across

`ui_conventions.md` forbids a scene reaching into another scene's tree. So the overlay does **not** resolve
`TimeControlBar` or the phone's Needs card itself. Instead:

- `TutorialOverlay` exposes `[Signal] TargetRectRequested(int target)` and a method
  `public void SetTargetRect(Rect2 rect)` (pass `new Rect2()` for "no highlight — center the card").
- **`Main`** — already the sanctioned shared-ancestor bridge for `HustleLaunchRequested` and
  `ShopOpenRequested` (`Main.cs:19-26`, `:115-118`) — listens, resolves the target `Control` from whichever
  subtree owns it, and hands back `control.GetGlobalRect()`.

This adds **no new coupling class**; it reuses the seam Main already exists to provide.

The spotlight is a cutout: draw the scrim as four `ColorRect`s around the target rect rather than one full-rect
scrim, or use a single `ColorRect` + a `Control` with `_Draw` punching the hole. Either is fine. If a target
`Control` is not visible (e.g. the phone is on a different tab), Main returns an empty `Rect2` and the step
degrades gracefully to a centered card — **never** a highlight floating over nothing.

> The overlay does not force tabs to switch. A step that talks about the Bank tab *asks* the player to open it
> ("Tap Bank."). Auto-driving another scene's `TabContainer` is exactly the cross-tree reach the conventions
> forbid, and a tutorial that moves the UI under the player's hands teaches nothing.

### 3.3 Persistence — one KV row, no schema change

New key in `GameStateQueries.cs`:

```csharp
/// <summary>
/// Onboarding overlay progress (long): absent/0 = not yet shown, 1..N = the next step index to
/// present (so a quit mid-tutorial resumes on the same step), -1 = finished or skipped.
/// </summary>
public const string TutorialStep = "tutorial_step";
```

Read/written with the Slice-G template verbatim: `GameState.TryGetInt64` / `GameState.SetInt64`
(`GameManager.cs:258`, `:886`). Written on **every** step advance, not just on completion — a crash or quit
three steps in must not restart the tutorial from zero.

**Save compatibility (required):** every save that predates this slice has no `tutorial_step` row, and would
therefore re-trigger the tutorial for a veteran mid-career. Guard on boot:

> If `tutorial_step` is absent **and** `current_day > 2`, write `-1` (done) and never show the overlay.

A fresh save is `current_day == 1`, so it falls through and the tutorial runs. This is the same
"absent key on an old save" posture `RefreshCarrierCard` already takes for pre-v11 `Phone_State` rows.

### 3.4 Replay

The phone's **Settings** tab (which already hosts Save Now and Quit —
`BurnerPhone.cs:451-458`) gains a third card: **"Replay tutorial"**. It emits a new
`[Signal] TutorialReplayRequested()`, Main catches it (same bridge as §3.2) and calls
`TutorialOverlay.Open(fromStep: 0)`.

This is what makes retiring the `tut_*` events safe (§6): the tutorial stops being a
one-shot you can scroll past and becomes a thing you can always go back to.

---

## 4. The permanent UI upgrades

**These are the part that matters.** The overlay is scaffolding the player sees once; these two changes are on
screen for the entire career. If the build runs out of time, build §4 and cut §3.

### 4.1 The Needs card (`BurnerPhone.tscn` → `Bank/BankScroll/BankLayout/NeedsCard`)

Today: a heading and five unlabeled `ProgressBar`s. It shows *what* the values are and nothing about what they
mean, how fast they move, or what happens at the bottom.

Four additions — **all presentation, no new queries, all dirty-flagged into the existing `RefreshBankTab`
identity (`_shownNeeds`), no per-frame formatting** (`ui_conventions.md` §Performance):

1. **The critical line.** Each need row gets a 2px `ColorRect` marker at the 20% mark of the bar
   (`anchor_left = anchor_right = 0.2`, `offset_right = 2`), because `NeedsEngine.CriticalThreshold = 20f` is
   the point at which **the sim overrides your plan** (`LifeSimManager.cs:766-781`). The player must be able to
   see that line coming.

2. **Critical state is loud.** When any need `<= NeedsEngine.CriticalThreshold`, its row label turns to the
   theme's danger color and a caption appears under the card:
   > *"Fitness is critical — your player will drop what he's doing to fix it."*

   This is not flavor. It is the literal behavior of `TickHour`'s crisis branch, and it is currently invisible.

3. **Ordering is information.** The rows are *already* in exact decay order (Hunger 4.2 → Sleep 3.4 → Hygiene
   1.4 → Social 1.0 → Fitness 0.55 per hour, `NeedsEngine.cs:142-146`). Say so, once, in a caption under the
   heading: *"Listed fastest-draining first."*

4. **A tooltip per row** (`Control.TooltipText`), stating the drain and the fix. Example for Hunger:
   > *"Drains fastest of the five. Falls faster the emptier it gets. Your player eats in Free hours — a meal
   > costs $12."*

   Per-row content in §4.3.

> **Required before writing tooltip copy:** run the `simulate_utility_decay` skill to get the true
> *hours-from-full-to-critical under neglect* for each need (acceleration makes the naive `100 / rate` wrong).
> Put the measured number in the tooltip. Do not guess it, and do not ship a number the harness didn't produce.

### 4.2 The Free-hours line — the load-bearing fix

`ScheduleScreen.cs:49-52`. Today:

```csharp
public string FreeHoursFormat { get; set; } = "Free hours: {0}";
public string OverAllocatedFormat { get; set; } = "Over by {0}h — reduce a block before confirming.";
```

Becomes:

```csharp
public string FreeHoursFormat { get; set; } =
    "Free hours: {0} — your player eats, washes, rests and sees people in these.";

public string NoFreeHoursText { get; set; } =
    "No free hours — nobody eats today. Your needs will all fall and none will recover.";

public string LowFreeHoursFormat { get; set; } =
    "Free hours: {0} — tight. That may not be enough to eat AND wash AND rest.";

public string OverAllocatedFormat { get; set; } = "Over by {0}h — reduce a block before confirming.";
```

Three states, chosen in the existing `free`-computing branch (`ScheduleScreen.cs:391-393`), styled with the
theme's danger color at zero:

| `free` | Line |
|---|---|
| `< 0` | `OverAllocatedFormat` (unchanged) |
| `== 0` | `NoFreeHoursText` — **danger color** |
| `1..3` | `LowFreeHoursFormat` — warning color |
| `>= 4` | `FreeHoursFormat` |

The `1..3` threshold is a **first-pass invention** and should be sanity-checked against
`simulate_utility_decay` (how many free hours does a standard avatar actually need to hold all five needs
steady across a school day?). If the harness says the real number is 5, use 5. Tune the constant, do not tune
the copy.

**This one change probably prevents more silent player deaths than the entire overlay.**

### 4.3 Per-need tooltip copy

Rates from `NeedsEngine.cs:142-146`; restores and costs from `ActionCatalog.cs:106-114`. Bracketed figures are
placeholders the harness fills in (§4.1).

| Need | Tooltip |
|---|---|
| **Hunger** | "Drains fastest of the five — critical in about [N]h of neglect. Your player eats in Free hours; a meal costs $12 and restores 45." |
| **Sleep** | "Second-fastest. A planned Sleep block restores 80 over 8 hours. **Work and hustle shifts drain it at double rate.**" |
| **Hygiene** | "Slow but steady. A shower restores 60 and costs nothing but an hour of Free time." |
| **Social** | "Slow. A night out restores 35 and costs $40; hanging out is free but weaker. Let it crater and it drags on everything." |
| **Fitness** | "Slowest to fall, slowest to rebuild. A workout restores 30 for $10 — but **it makes you hungrier and more tired** while you do it." |

And one card-level caption, because it is true of all five and stated nowhere:

> *"All five fall faster the emptier they get. Stress makes everything fall faster still — up to 2½× in a bad
> stretch."*

(`NeedsEngine.cs:166` acceleration term; `:133` `MaxStressModifier = 2.5f`.)

---

## 5. The day-1 step script

Eight steps, ~15 seconds of reading each, skippable at any point, resumable across a quit. Copy is final unless
the harness contradicts a number.

| # | Title | Target | Body |
|---|---|---|---|
| 1 | **This is your phone** | PhoneTabs | "Everything you do off the field runs through here. Events is where moments land and you answer them. Messages is your texts. Calendar is your season and your day plan. Bank is money and needs. You'll be back here a lot." |
| 2 | **Time runs on its own** | TimeBar | "The clock is live. Pause it, run it Slow, Normal or Fast, or hit Skip Day to jump to tomorrow. It pauses itself when something needs you — a choice, a game, a deal." |
| 3 | **Five needs, always falling** | NeedsCard *(+ diagram)* | "Open **Bank**. Hunger, Sleep, Hygiene, Social, Fitness — every one of them drops every hour of every day, and they're listed fastest-first. Hunger falls about eight times quicker than Fitness." |
| 4 | **They fall faster the lower they get** | NeedsCard *(+ diagram)* | "This is the part that kills careers. A need at 30 isn't just worse than one at 60 — it's dropping *faster*. Neglect compounds. And stress from a bad stretch makes all five fall faster still." |
| 5 | **The line at 20** | NeedsCard *(+ diagram)* | "See the mark near the bottom of each bar? Drop below it and your player stops taking orders. He'll abandon whatever you scheduled and go fix it himself — eat, crash, whatever it takes. You don't want to find out which." |
| 6 | **Free hours are how he survives** ⭐ | PlanToday | "Open **Calendar → Plan Today**. Sleep, School, Practice, Work — you allocate the day. Whatever you *don't* allocate is **Free hours**, and that is when your player eats, washes, rests and sees people. **Book all 24 hours and nobody eats.** Watch that line every day." |
| 7 | **Game days** | TimeBar | "Games are on your Calendar. When one lands, the Time bar swaps in **Play Game** — step in and take the at-bats yourself, or Skip Day and let the team handle it. No penalty either way. Player has your stat line afterward; League has the standings." |
| 8 | **Money, bills, saving** | BankFunds | "Bank shows your cash and a cost-of-living bill every week. The game saves itself as you go — there's a Save Now in Settings if you want it, and a **Replay tutorial** button right under it if you ever want this again. Go play." |

Steps 3, 4 and 5 are the heart of this slice and they all point at the same card, deliberately: the player
should be *looking at the five bars* while being told how they behave.

**Step 6 carries a ⭐ because it is the one step that must not be cut.**

---

## 6. What happens to the four `tut_*` events

**Recommendation: retire them.** `getting_started_events.json` is deleted; the `getting_started` contact stays
in `contacts.json` (harmless, and useful if we ever want a hint thread).

Rationale:

- Their content is 100% subsumed by §5, and taught *worse* — a phone card that scrolls away, days after the
  player needed it, versus an overlay pointing at the live widget on day 1.
- They consume **four** of the avatar's one-per-day event slots across days 2–8, which is why `hs_first_day`
  currently slips to day 3. Retiring them **gives the story arc its day-2 slot back**, which is what the
  narrative arc wanted before the tutorial was wedged in front of it.
- Re-readability — the reason they were events at all — is now served properly by **Settings → Replay
  tutorial** (§3.4).

**Consequences to land in the same commit:**

- `check_event_graph_integrity` — remove the 10 day-1-tutorial checks added on 2026-07-11 (including contact
  check #8). Gritty harness tally: **277 → 267**, event count **83 → 79**.
- `hs_first_day` returns to **day 2** on fresh saves. Update `docs/hs_manual_playtest_checklist.md`
  (Session A2b, added for the tutorial) accordingly.
- MC harness: **untouched, byte-exact.** No event in this batch ever wrote a `person_stat`, `funds`, `stress`
  or `relationship` consequence (that was the whole point of its flags-only design), so deleting them cannot
  move a single calibration number.

> This deletes content committed in `a8be02b` — one day old. Flagged explicitly rather than done silently. If
> you'd rather keep them, the fallback is to strip all four down to pure story flavor with **zero** mechanics
> copy, so the player is never taught the same rule twice in two different voices. But they still eat the day
> slots, and I don't recommend it.

---

## 7. Invariants & risk

| Invariant | Status |
|---|---|
| Monte Carlo (345/345) | **Inert.** No file under `Simulation/Baseball/` is touched. |
| Needs decay harness (102/102) | **Inert.** `NeedsEngine`/`ActionCatalog`/`LifeSimManager` are read-only inputs to this slice — we surface their constants, we do not change them. |
| Gritty events (277/277) | Drops to **267/267** *by deletion only* (§6). No surviving event's definition changes. |
| Schema (111/111), `PRAGMA user_version` | **Unchanged.** `tutorial_step` is a `Game_State` KV row, not a column. |
| CoreLoop (22/22) | Unchanged. |
| Save compatibility | Guarded — §3.3's `current_day > 2` rule stops the overlay ambushing a mid-career save. |

The genuine risks, stated plainly:

1. **Spotlight rects go stale** if the player resizes the window or scrolls the Bank tab mid-step. Mitigation:
   re-request the target rect on `Resized` and each `_Process` tick *while a step with a target is live* (this
   is a handful of `GetGlobalRect()` calls on a paused, modal surface — the no-LINQ-in-`_Process` rule is about
   the sim loop, and this is not it). If the rect comes back empty, fall back to a centered card.
2. **The overlay opening before the shell exists.** `Main.ShowAppropriateScreen()` builds the shell; the
   overlay must only open *after* that, and only on the `HasAvatar` branch. Open it at the tail of
   `OnAvatarCreated()` and at the tail of `_Ready()` when `Career.HasAvatar` is already true.
3. **Tab-dependent targets.** Steps 3–6 and 8 point into the phone, which may be on any tab. Per §3.2 we do not
   auto-switch; the copy says "Open Bank" and the step degrades to a centered card if the target isn't visible.

---

## 8. Build order & model assignments

Per `docs/progress.md` workflow (Opus architecture → Sonnet logic → Fable execution/review):

| Stage | Owner | Work |
|---|---|---|
| **T-0** | *(done — this doc)* | **Opus 4.8** — architecture, constraint analysis, step script, copy. |
| **T-1** ⭐ | **Fable 5** | §4.2 Free-hours line + §4.1 Needs card. Pure presentation, no new queries. **Run `simulate_utility_decay` first** to source the real hours-to-critical figures and to sanity-check the low-free-hours threshold. Ship this even if T-2 slips — it's the highest-value change in the slice. |
| **T-2** | **Sonnet 5** | §3 `TutorialOverlay` scene + script, `Main` bridge, `GameStateKeys.TutorialStep` + save-compat guard, Settings → Replay button. **Run `godot_scene_mapper` before writing any `GetNode<T>()`** (`ui_conventions.md`, review-blocking). |
| **T-3** | **Fable 5** | §6 retirement: delete `getting_started_events.json`, update `check_event_graph_integrity` (277→267), restore `hs_first_day` to day 2, update the playtest checklist. Then review T-1+T-2 and re-run all five harnesses to prove MC/Needs byte-exact. |

**T-1 and T-2 are independent** — different files, no shared seam — and can run in parallel.

## 9. Verify

Beyond the harnesses (§7), this slice is UI, so it must be *driven*, not just compiled:

1. **Fresh save → the overlay opens on day 1**, all 8 steps advance, Back works, Skip works.
2. **Quit on step 4 → reload → resumes on step 4** (the `tutorial_step` KV round-trip).
3. **Load the existing mid-career test save → the overlay does NOT appear** (§3.3 guard).
4. **Settings → Replay tutorial → it opens from step 1.**
5. **Plan a 24-hour day in Plan Today** → the Free-hours line goes to the danger state and reads
   *"No free hours — nobody eats today."*
6. **Drive a need under 20** (plan a few brutal days, or edit the test save) → the row goes danger-colored,
   the critical caption names the right need, and the 20-marker sits where the color flips.
7. Confirm the test save is left pristine afterward, per the Slice-G discipline.
