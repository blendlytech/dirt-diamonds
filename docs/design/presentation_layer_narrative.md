# Presentation Layer & Narrative Delivery — Design (Phase 10, "The Look")

**Owner of this doc:** Opus 4.8 (design only — NO code). Phase 10 is the first phase whose
center of gravity is UI, so the delegation inverts the usual split: **Sonnet 5 owns the
implementation volume**, Fable 5 owns the shell wiring + review, and Opus owns exactly the
two decisions where getting it *wrong* is worse than getting it *slow* — the **narrative-seam
architecture** (§4) and the **scouting-grade curve math** (§5). Everything else is a precise
spec for Sonnet.

Grounded against the live code this session (No Blind Queries applies to design too):
`Assets/UI/Main.cs` + `Main.tscn`, `Assets/Core/CoreEvents.cs`, `Assets/UI/EventChoiceScreen.cs`,
`Assets/Narrative/Events/GrittyEventModel.cs`, `Assets/Data/Database/BaseballDtos.cs`,
`Assets/Data/Database/NarrativePollQueries.cs`, `PromotionManager.cs` (the `PromotionScore`
class), and `SchemaDefinitions.sql` (`Game_Logs`). The BUILD_PLAN §10 exit criteria and
`ui_conventions.md` are the acceptance frame.

---

## 1. Thesis & Scope

Every UI shipped through Phase 9 is a deliberate **thin slice of plain Godot controls** — no
theme, no visual identity, functional but not the pitch. `GAME_IDEA.md`'s entire hook *is* the
presentation: a dark-mode split between a **Baseball Dashboard** and a **Burner Phone / Bank**,
with narrative arriving as **iMessage-style texts** from girlfriends, coaches, and shady contacts.
Phase 10 makes the game *look* like the pitch **without rewriting a single simulation system** —
it is a pure read-model + chrome phase.

**Three hard rules carried from `ui_conventions.md`, non-negotiable through this phase:**

1. **UI is read-only over sim state.** Every panel renders DTOs and emits player-intent signals
   up. No panel writes the DB or mutates sim state. The *only* narrative-log write this phase
   introduces (§4.3) is owned by a **sim-side** class (the consequence applier / GameManager),
   never a UI script.
2. **Scenes communicate only via the event bus / signals** — never by reaching into another
   scene's tree. The two-panel shell hosts the existing interactive screens as launched overlays;
   it does not merge their code.
3. **Thin vertical slices.** Phase 10 ships as five demoable sub-steps (§7), not a big-bang UI
   rewrite. Each step leaves the game bootable and every prior screen reachable.

**Explicit non-goals (deferred to Phase 11 or later):** no Steamworks wiring (the
`Assets/Platform/Steam/` stubs stay dormant), no new hustle/economy mechanics, no new schema, no
change to any calibrated sim band. Phase 10 must re-run **zero** `run_monte_carlo_batch` bands
by construction — it never touches `Assets/Simulation/`. The one piece of genuine math (the
scouting curve, §5) is a pure deterministic function with its own tiny fixture, not a sim change.

---

## 2. The Two-Panel Shell (Architecture)

### 2.1 Where it lives in the scene graph

Today `Main.cs` swaps a single **primary screen** in/out of `ScreenContainer` — `NewGameScreen`
before a career exists, `AttendedGameScreen` after — while a fixed set of **permanent overlay
siblings** (EventChoiceScreen, SuccessionScreen, BirthNotificationScreen, ScheduleScreen, the two
hustle screens, EquipmentShopScreen, TexasHoldemTable) sit alongside, each self-hiding via its own
`Visible` flag (`Main.cs:33-50`).

**Decision — the shell becomes the new career "home" primary screen, additively.** A new
`Assets/UI/TwoPanelShell.tscn` (root node `TwoPanelShell : Control`, PascalCase-after-file per
`ui_conventions.md`) replaces `AttendedGameScreen` as what `ShowAppropriateScreen` swaps in when
`Career.HasAvatar`. `NewGameScreen` is untouched (pre-career boot is unchanged). **The overlay
sibling set is preserved verbatim** — every existing interactive screen still launches on top of
the shell exactly as it does today, so no interactive screen is rewritten; they inherit the new
theme (§3) for free and otherwise keep their wiring.

```
Main (Node)
├── ScreenContainer            ← swaps: NewGameScreen  ⇄  TwoPanelShell   (career home)
│   └── TwoPanelShell (Control)
│       ├── BaseballDashboard (left panel, §5)
│       └── BurnerPhone       (right panel — Messages + Bank tabs, §4/§6)
├── EventChoiceScreen          ← retired in 10b (folds into the phone thread, §4.4)
├── SuccessionScreen           ← unchanged overlay
├── BirthNotificationScreen    ← unchanged overlay
├── ScheduleScreen             ← unchanged overlay (launched from the dashboard calendar)
├── NarcoticsHustleScreen      ← unchanged overlay (launched from the phone)
├── FencingScreen              ← unchanged overlay (launched from the phone)
├── EquipmentShopScreen        ← unchanged overlay (launched from the phone Bank tab)
├── TexasHoldemTable           ← unchanged overlay (launched from the phone)
└── AttendedGameScreen / AtBatView  ← the at-bat launch flow (see 2.2)
```

### 2.2 Absorbing `AttendedGameScreen`'s day-advance role

`AttendedGameScreen` currently owns two responsibilities: (a) the **day-advance clock** (its
Play/Skip buttons are the *only* caller of `TimeManager.AdvanceDay()` — the standing
"AdvanceDay is UI-button-only" gap), and (b) launching the **attended at-bat** via
`CareerManager.TryGetPendingGame` / `PlayPendingGame` + `AtBatView`.

**Decision — the dashboard's calendar strip absorbs (a) and (b) by reusing the identical seams,
and `AttendedGameScreen` retires.** The BaseballDashboard gains a calendar/advance control that
calls the *exact same* `TimeManager.AdvanceDay()` and `CareerManager.TryGetPendingGame` /
`PlayPendingGame` methods `AttendedGameScreen` calls today — no new sim surface, a mechanical
move of the button wiring into the shell. `AtBatView` is unchanged and launches from the
dashboard's "game today" affordance. This is the one existing screen Phase 10 *replaces* rather
than reskins, because its role is structurally the dashboard's. The availability/absence label
(`GameManager.Absences`, Phase 8c) moves onto the dashboard beside the calendar. **This closes
nothing about the AdvanceDay-headless gap** — it stays UI-button-only; it just moves which button.

### 2.3 Overlay z-ordering & the modal gate

Overlays keep their current self-hiding-`Visible` discipline and simply draw over the shell
(they are later siblings under `Main`, so they already paint on top). The **day-advance modal
gate** matters: `AttendedGameScreen` today blocks the day from advancing while a pending attended
game is unplayed; `EventChoiceScreen`/`SuccessionScreen` block while a choice is pending. Moving
day-advance onto the dashboard means **the dashboard's advance control must honor the same gates**
— it disables/redirects while `EventConsequenceApplier.HasPendingChoice`, while a succession
choice is parked, or while a pending attended game is unplayed. This is the one piece of real
control-flow the shell inherits and must reproduce (Fable owns this wiring in 10a; it is a
pressure-test point — a shell that advances the day past an unanswered event choice is a defect).

### 2.4 Why not merge the screens into the panels

Rejected: re-parenting ScheduleScreen/hustles/poker as children *inside* the panels. It would
break the "scenes never reach into another scene's tree" rule, force a rewrite of eight working
screens, and blow the thin-slice budget. Launching them as the existing overlays keeps every
Phase 8/9 screen byte-for-byte and lets the theme do the visual unification.

---

## 3. Visual Identity — the Theme Resource

### 3.1 The single retrofit lever

There is **no `Theme` resource anywhere in the project today** (verified: no `.theme`/`.tres`
theme, no `gui/theme/custom` in `project.godot`). Every control uses Godot defaults.

**Decision — one shared dark-mode `Theme` applied game-wide via the project default theme
setting, not per-scene.** Ship `Assets/UI/Theme/DirtAndDiamonds.theme` (a Godot `Theme` resource)
and point `project.godot`'s `gui/theme/custom` at it. Godot applies a project default theme to
**every `Control` in the game with zero per-scene wiring**, so this single setting reskins all
eleven existing thin-slice scenes at once, while per-scene `theme_override_*` remains available
for the two panels' bespoke chrome. This is the highest-leverage line in the whole phase.

### 3.2 What the theme defines (the design tokens)

A dark, "gritty-polaroid / burner-phone" palette (Sonnet finalizes exact hex against
`GAME_IDEA.md`'s art direction; these are the *token roles* to fill, not arbitrary values):

- **Surfaces:** near-black app background; one step lighter for panels; a third for cards/message
  bubbles. High contrast, low saturation.
- **Ink:** off-white primary text, mid-grey secondary/timestamp text — must clear WCAG AA on the
  panel surface (accessibility is a store-review consideration, not optional polish).
- **Accents:** one "diamond" accent (baseball/positive) and one "dirt" accent (illicit/risk/
  negative) — the two-sided identity in the title. Used for progress-bar fills, grade highlights,
  and unread-message dots.
- **Type scale:** a small set of sizes (display / heading / body / caption). No per-scene font
  sizes in C# — sizes come from the theme's typography.
- **StyleBoxes:** panel (rounded, subtle border), button (default/hover/pressed/disabled), message
  bubble (incoming vs. outgoing, distinct fills), progress bar (track + fill), and a "card" box
  for stat rows and scouting lines.

### 3.3 Progress bars, meters, calendar

`GAME_IDEA.md` calls out "satisfying progress bars." Standardize on a themed `ProgressBar`
StyleBox for: the five life needs (Hunger/Sleep/Hygiene/Social/Fitness), season progress,
detection-risk / health-ceiling meters, and rating/grade bars in the scouting card. One StyleBox,
reused — no bespoke draw code. The calendar is a themed read-only strip on the dashboard (current
day / day-of-season / season year off `DayAdvancedEvent`'s payload, which already carries all
three).

### 3.4 Localization posture

Full string-table extraction is **out of scope** for Phase 10 (disclosed), but the rule holds:
**no *new* player-facing string literals in C#.** New copy lives in scene `.tscn` text, the theme,
or the content JSON (§4). Existing literals are not required to move this phase, but nothing new
is added to the pile. This keeps the Phase-11 localization door open without paying for it now.

---

## 4. Narrative Delivery as Messages (Opus-owned seam)

This is the phase's marquee feature and the one place the current data model is genuinely
missing a dimension. The design below is **additive and schema-free**.

### 4.1 The gap: events have no sender

`GrittyEventDefinition` (`GrittyEventModel.cs:223`) carries `Prompt` (flavor text) and
`Choices[].Label` (button text) but **no sender/contact**. "Threaded per-contact" texts require a
notion of *who* is texting. Three options were considered:

- **(A) Derive the contact heuristically from the event id/category.** Rejected — fragile, and it
  hard-codes narrative authorship into UI string-matching.
- **(B) A schema table for contacts + messages.** Rejected — over-engineered; `Game_Logs` already
  persists arbitrary tagged narrative rows (§4.3), and contacts are content, not sim state.
- **(C, chosen) An additive `"contact"` field on the event JSON + a content-authored contact
  registry.** Clean, content-driven, no schema change, matches every prior additive-field pass.

### 4.2 The Contact dimension

- **Registry:** a new content file `Assets/Narrative/Contacts/contacts.json` mapping a stable
  `contact_id` → `{ display_name, role, portrait_key, tone }`. `role` is an enum
  (Girlfriend / Coach / Agent / Dealer / Fixer / Family / Unknown …); `portrait_key` feeds §6;
  `tone` is optional styling metadata. Loaded once at boot into an immutable registry, same
  lifecycle as `GrittyEventLibrary` (malformed → throws at load, never at render).
- **Event tag:** `GrittyEventDefinition` gains an optional `ContactId` (additive parse in
  `GrittyEventJson`, exactly the pattern used for prior optional fields like `label`/`prompt`
  fallbacks). **Default-derivation rule when a batch omits `"contact"`:** the event routes to a
  single reserved `"unknown"` contact thread ("Unknown Number") — never a crash, never a silent
  drop. Sonnet tags the existing ~19 events during 10b; untagged legacy content still renders.
- **Threading:** a thread = all narrative-log rows (§4.3) sharing a `contact_id`, ordered by day.
  The phone's Messages tab lists contacts (most-recent-first, unread dot on new fires); tapping
  one opens the thread.

**This keeps the wall intact:** contacts are pure Narrative/UI content. The Baseball and Life
sims never learn a contact exists.

### 4.3 Persistence — the read-model (additive `Game_Logs` write, no schema change)

`Game_Logs` (`SchemaDefinitions.sql:131`) is a general tagged log —
`(log_id, season_year, game_day, home_team_id, away_team_id, player_id, event_type TEXT, payload TEXT)`,
indexed by `player_id` and by `(season_year, game_day)`. Today **only the baseball side writes it**
(CareerManager/MicroGame game logs); narrative fires are **not** persisted anywhere the UI can
read back. So message-thread *history* (scroll-back across days) has no source yet.

**Decision — add a narrative-log write on `GrittyEventResolvedEvent`, owned by the consequence
applier / GameManager (sim-side, never UI):**

- On resolve, insert one `Game_Logs` row: `event_type = "narrative_msg"` (a new namespace;
  implementer verifies it collides with no existing `event_type` literal — a No-Blind-Queries
  check), `player_id = subject`, day/season from the fire, `payload` = compact JSON
  `{ "contact": <contact_id>, "prompt": <text>, "choice": <label>, "choice_index": <i> }`.
- **Forfeited/auto-resolved** events (autopilot picked the choice) still resolve → still log, so
  the thread reflects what actually happened, including choices the player never saw.
- The phone reads these back through a new **read-only** query on `Game_Logs`
  (`event_type = 'narrative_msg'`, ordered by day) — rides the existing
  `idx_game_logs_day`/`idx_game_logs_player` indexes; validate the plan via the sqlite MCP before
  writing it. This is the thread history.
- **Live fires** arrive on the bus (`GrittyEventFiredEvent`) for the "new message" animation +
  unread badge; the persisted row is the durable record the thread re-reads on next open.

**Disclosed migration nuance:** saves created before 10b have no `narrative_msg` history, so their
phone opens with empty threads that fill going forward. This is not a schema change and needs no
migration step — `Game_Logs` already exists; only the *write* is new.

### 4.4 Reskinning the pending-choice seam (retiring `EventChoiceScreen`)

`EventChoiceScreen.cs` today polls `EventConsequenceApplier.TryGetPendingChoice(out …)` each frame
and, on a pending fire, renders `Definition.Prompt` + a button per `Choice.Label`, calling
`GameManager.GrittyEventChoices.ResolveChoice(i)` on press (`EventChoiceScreen.cs:35-75`).

**Decision — the phone's active thread *is* the choice surface; `EventChoiceScreen` retires in
10b.** When `HasPendingChoice`, the phone auto-opens the pending event's contact thread, renders
the prompt as an **incoming bubble**, and renders the choices as **reply chips**; tapping a chip
calls the identical `ResolveChoice(i)`. The dirty-flag identity check (`_shownFireIdentity`,
rebuild only on a change of pending fire) carries over verbatim. The **day-advance gate** (§2.3)
already forbids advancing while a choice is pending, so the player can't miss it. `EventChoiceScreen`
is deleted from `Main.tscn` and its `.cs`/`.tscn` removed once the thread renders the pending
choice — a net simplification (one fewer overlay, the choice UI unified with its own history).

### 4.5 Relationship & life beats (optional, in-phase if budget allows)

Beyond gritty events, the bus already emits `ChildBornEvent`, `RivalryChangedEvent`,
`PlayerAbsenceChangedEvent` — natural "texts" ("Coach: you're benched 10 games", a partner's birth
announcement). These can surface as **system-authored messages** from the relevant contact thread
using the same narrative-log write. Kept as a stretch within 10b (Sonnet's call on budget); the
gritty-event thread is the required deliverable, these are the enrichment.

---

## 5. Scouting Reports — the Grade Curve (Opus-owned math)

The Baseball Dashboard frames the avatar's (and any scouted player's) ratings **as a scout would**:
letter grades + a projection. This is the one place in Phase 10 where wrong math reads as wrong
game, so the curve is specified here exactly and pinned by a fixture.

### 5.1 The inputs (all already in the DTO layer)

Per `BaseballDtos.cs`: `PlayerRatingsRow` — seven **0–100** ratings, **50 = league average**
(BatPower, BatContact, BatDiscipline, PitStuff, PitControl, PitStamina, Fielding);
`PlayerPotentialRow` — the per-rating **latent ceiling** (schema v10), with the invariant
`potential_i ≥ current_i`; `TeamRow.Tier` — ladder standing; and the existing projection functions
on `PromotionScore`: `Headroom(current, potential, isPitcher)`, `ProjectionBonus(age, headroom)`,
and `Scouting(roleRatingSum, age, headroom)` (returns a **100-centred** overall on Combine's
ranking scale — an exactly-average peak-age player scores 100.0, range ~0–200; *not* a 0–100
value. The first draft of this section misstated it as 0–100 — corrected per the 10c disclosed
finding, ruled by Fable 5 on 2026-07-06). The scouting card **reuses these verbatim** — it is a
read, not a recomputation.

### 5.2 The letter-grade curve (the deliverable)

A rating `r ∈ [0,100]` maps to a letter grade centered so that **50 (league average) reads as an
average grade** and **40 (the replacement-level call-up floor from Phase 8c) reads one grade
below**. Eight bands, tuned to the real 20-80 scouting ladder's semantics (plus-plus at the top,
well-below-replacement at the bottom):

| Rating band | Grade | Scout meaning |
| :---------- | :---- | :------------ |
| 90–100 | **A+** | Elite / plus-plus (top of scale) |
| 80–89  | **A**  | Plus |
| 70–79  | **B+** | Above-average |
| 60–69  | **B**  | Solid-average+ |
| 50–59  | **C+** | Average regular (50 = league mean) |
| 40–49  | **C**  | Fringe / replacement (40 = call-up floor) |
| 30–39  | **D**  | Well below replacement |
| 0–29   | **F**  | Non-prospect |

These thresholds are the **normative Opus deliverable** — a pure `Grade(int rating) → GradeLetter`
lookup, deterministic, with a fixture pinning every boundary (49→C, 50→C+, 89→A, 90→A+, 0→F,
100→A+). No RNG, no tier-relativity: the grade reflects the raw tool on one absolute scale
(ratings are already one scale across tiers — 9a bakes tier difference into the *environment*, not
the batter ratings, so an absolute grade is consistent).

### 5.3 Present vs. Future (the projection story)

Each tool shows **two grades**: **present** = `Grade(current_i)`, **future** =
`Grade(potential_i)`. A 17-year-old with a present-C bat and a future-A bat is the scouting story
Player_Potential was built to tell — and it's exactly the headroom the 9d development pass grows
into. The card renders both (e.g., a bar from present-fill to future-ghost).

**Overall Future Potential (OFP):** one headline grade = `Grade(round(Scouting(roleRatingSum, age,
headroom) / 2))`, i.e. the same projection the promotion sweep scores through, converted from
Combine's 100-centred units onto `Grade`'s 0–100/50-average domain. The `/2` is exact, not a
fudge: at peak age with zero headroom `Scouting = 2 × role-average rating`, so the halved value
is precisely the per-rating role average (plus `ProjectionBonus/3`, the same bonus re-expressed in
per-rating units). This ties the scouting headline to the *actual* promotion math the avatar is
judged by — the scout's projection and the game's projection are the same number, by construction.
*(Ruling, Fable 5, 2026-07-06: this section originally read `Grade(round(Scouting(...)))` — a scale
mismatch that would over-grade nearly every rostered player. The conversion lives permanently at
the display consumer, `ScoutingGrade.OfpRating`; `Scouting`'s own `/150` divisor must NOT be
re-derived, because its 100-centring is load-bearing inside `Combine` against the equally
100-centred Performance scores — changing it would silently re-weight every promotion pool, a band
move in closed 9c.)* Optionally surface a tier label ("projects as a MLB regular") derived from the
OFP band; the numeric OFP grade is the required piece.

### 5.4 Role split & development card

Batters show BatPower / BatContact / BatDiscipline / Fielding; pitchers show PitStuff / PitControl
/ PitStamina / Fielding, plus per-pitch Velocity/Movement from `PitchArsenalRow` if the arsenal
read is cheap (stretch). The role split is already in the DTOs (`IsPitcher`, `Role`). A small
**development card** reads `DevelopmentManager.LastRun` (the `DevelopmentSummary` seam explicitly
built in 9d-1 as "the read surface a future development UI card renders") to show last offseason's
rating movement — the payoff surface for the whole 9d curve system.

---

## 6. Portrait Pipeline

- **Asset set** keyed to `player_id`: pre-generated 2D portraits in the `GAME_IDEA.md` house style
  (gritty-polaroid / mugshot). Loaded by a `portrait_key` (contacts, §4.2) or `player_id` (avatar
  + scouted players).
- **Budget decision (disclosed):** portraits are authored for the **avatar, heirs, and named
  contacts** only — not all ~817 background NPCs. Everyone without a portrait asset gets a
  **deterministic procedural fallback** (a themed initials-on-color tile seeded off `player_id`,
  so the same NPC always draws the same fallback). No missing-image boxes ever render.
- **Loading discipline:** portraits load through the object-pool / lazy-load pattern, never
  instantiated per-frame; the fallback is a themed StyleBox + label, zero asset cost.
- **Steam disclosure carry-forward:** the pre-generated AI portraits are a **Steam content
  disclosure** item — recorded here so Phase 11's store-copy/questionnaire step (BUILD_PLAN §11)
  inherits it. No action this phase beyond the note.

---

## 7. Sequenced Sub-Plan (one session each, house cadence)

| Step | Deliverable | Owner |
| :--- | :---------- | :---- |
| **10a — Shell + Theme foundation** | `TwoPanelShell.tscn` as the new career home; project-default dark-mode `Theme` retrofitted game-wide (§3); dashboard absorbs day-advance/play-today reusing the exact `TimeManager`/`CareerManager` seams **incl. the day-advance gates** (§2.2–2.3); every existing overlay still launches. | **Fable 5** (shell wiring + gate correctness) + **Sonnet 5** (theme tokens/StyleBoxes) |
| **10b — Burner Phone: messages** | Contact registry + additive `"contact"` JSON field; sim-side `Game_Logs` `narrative_msg` write on resolve; phone Messages tab (threads from the read-model); reskin the pending-choice seam and **retire `EventChoiceScreen`** (§4). Tag existing events with contacts. | **Sonnet 5** (impl, over Opus's §4 seam) |
| **10c — Baseball Dashboard: scouting** | The §5 grade curve (+ its fixture), present/future per-tool grades, OFP headline off `PromotionScore.Scouting`, tier standing, the `DevelopmentManager.LastRun` card. | **Sonnet 5** (impl, over Opus's §5 math) |
| **10d — Bank / survival strip** | Phone's Bank tab: funds, weekly cost-of-living, gear/equipment summary (8e), the five needs meters — all read-only DTOs; launch buttons for the existing shop/hustle/poker overlays. | **Sonnet 5** |
| **10e — Portrait pipeline** | Portrait load-by-key + procedural fallback (§6); wire avatar + contact portraits into the phone/dashboard; record the Steam disclosure. | **Sonnet 5** (assets) + **Fable 5** (disclosure carry) |

10a is the load-bearing step (the shell + the day-advance gate move) and is Fable's to get right;
10b/10c carry the two Opus-designed cores; 10d/10e are lower-risk read-model + asset plumbing.
10c and 10d can run in either order; 10e can trail. **10a must land before all others** (they
render inside its shell).

---

## 8. Acceptance Criteria & Verification

Phase 10 touches **no** `Assets/Simulation/` code, so the sim-band discipline is satisfied by
*absence of change*, not a re-run — but state that explicitly per step (a `git show --stat`
proving no `Simulation/Baseball` or `Simulation/Life` file moved is the standing evidence). The
UI-verification discipline from every prior UI pass applies in full:

1. **Node paths verified before `GetNode<T>()`** — run `godot_scene_mapper` / the Godot MCP
   against each new `.tscn` before writing its script (`ui_conventions.md`, review-blocking if
   skipped).
2. **Live headless boot** via the Godot MCP against the real dev save (schema v10) with the new
   shell + phone + dashboard in the tree across multiple frames — **zero debug-output errors**,
   confirming every new `GetNode` path resolves. This is the established stand-in for the
   AdvanceDay-is-UI-button-only gap (no headless input injection exists).
3. **`check_event_graph_integrity`** after 10b's contact-tagging content batch (a content batch
   per the standing rule) — no orphaned flags, plus the new "every event's `contact` resolves in
   the registry (or defaults to `unknown`)" check.
4. **The scouting curve gets a deterministic fixture** (10c) — every band boundary pinned
   (49→C / 50→C+ / 89→A / 90→A+ / 0→F / 100→A+), present-vs-future, and OFP ≡
   `Grade(round(Scouting(...) / 2))` (the §5.3 ruled recentering). This is the only new *logic* in the phase; it is tested like
   logic, not eyeballed. (A tiny harness path, or fold into an existing Data-compiling harness —
   implementer's call, "whichever harness already compiles the DTO layer.")
5. **No schema change — asserted**, not assumed: `SchemaValidator` re-runs green unchanged;
   `PRAGMA user_version` stays at 10; `Game_Logs` is reused, not altered.
6. **Wall audit:** `CoreLoopHarness`'s Life↔Baseball boundary scan stays clean (the phone is
   Narrative/UI; the shell reads DTOs and calls existing seams — no new cross-reference).

**BUILD_PLAN §10 exit criteria (the phase-close bar):** the full career loop is playable through
the two-panel shell in the shipping dark-mode identity; fired gritty events arrive as threaded
messages the player answers in-thread; the scouting card reads present/future grades; and scenes
still communicate only via the bus/signals. A **manual playtest** — create/advance an avatar,
trigger a gritty event, answer it in the phone, open the scouting card, roll a season and watch the
development card update — is the human sign-off (the same manual-playtest posture every prior UI
entry carries, since AdvanceDay is UI-button-only).

---

## 9. Disclosed Simplifications & Open Questions

- **`AttendedGameScreen` retires** (its role is structurally the dashboard's, §2.2); every other
  Phase 8/9 screen is preserved as a themed overlay.
- **Contact tagging is content work** over ~19 existing events; untagged events route to the
  reserved `unknown` thread — no crash, no drop.
- **Pre-10b saves have no message back-history** (the `narrative_msg` write is new; not a schema
  change, no migration).
- **Portraits are avatar/heir/contact-only**; all other players use the deterministic procedural
  fallback (asset-budget call).
- **Localization is not extracted** this phase — only the "no *new* C# string literals" rule holds.
- **Relationship/life-beat messages (§4.5) are a stretch**, not a required deliverable.
- **Open question for Sonnet in 10a:** whether the dashboard's day-advance control fully subsumes
  `AttendedGameScreen` or a slimmer "AttendedGameScreen becomes an embedded dashboard sub-view"
  refactor is cheaper against the live `.tscn` — decide by inspecting the actual node tree
  (`godot_scene_mapper`) before committing to the delete. The *seam calls* are identical either
  way; only the node-tree mechanics differ.
- **Open question for Phase 11 (noted, not resolved):** achievements hooking `EventDispatcher` and
  cloud-saving the `.db` are Phase 11; the portrait AI-disclosure is the one Phase-11 artifact this
  phase generates.

---

## 10. Cross-References

- `docs/BUILD_PLAN.md` §10 (exit criteria, Sonnet-owned presentation phase, portrait pipeline).
- `.claude/rules/ui_conventions.md` (read-only-over-sim, one-scene-per-surface, theme-not-per-scene,
  pooled elements, no per-frame LINQ/string-format, thin vertical slices).
- `Assets/Core/CoreEvents.cs` (the bus contracts the phone consumes: `GrittyEventFiredEvent` /
  `GrittyEventResolvedEvent`, `ChildBornEvent`, `RivalryChangedEvent`, `PlayerAbsenceChangedEvent`).
- `Assets/Narrative/Events/GrittyEventModel.cs` (`GrittyEventDefinition` / `EventChoice` — where
  `ContactId` is added additively).
- `Assets/Data/Database/BaseballDtos.cs` + `PromotionManager.cs` `PromotionScore`
  (`PlayerRatingsRow`, `PlayerPotentialRow`, `Scouting`/`Headroom`/`ProjectionBonus` — the
  scouting-card inputs).
- `SchemaDefinitions.sql` `Game_Logs` (the narrative-log substrate — reused, not altered).
- `docs/design/development_decline_curves.md` (§9d `DevelopmentManager.LastRun` — the development
  card's read surface).
```
