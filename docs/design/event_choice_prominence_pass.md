# Event-Choice Prominence & Immersion Pass

**Model:** Opus 4.8 (design) → build (Fable 5 or Sonnet 5 per §9) → Fable 5 review.
**Status:** P-1/P-2/P-3 BUILT (Sonnet 5) + **Fable 5 review SIGNED OFF 2026-07-11** (one fix: `PortraitView` pre-tree NRE — `SetIdentity` now defers its apply to `_Ready`; banner home resolved per §10). §5 locked (badge + banner, no auto-switch). P-4 parked. Remaining exit: user eye-gate playtest (§8).
**Touches:** `Assets/UI/` only. Zero `Assets/Simulation/` touch, zero schema change, zero content edit, no change to the resolution/forfeit *mechanics*.

---

## 1. Context — and a correction to the roadmap

The roadmap (progress.md VERY-NEXT-STEPS #4, and the auto-memory) records the **avatar event-choice UI as an unbuilt "next slice"** whose point is "to make every gritty/dirt event player-chosen instead of autopilot-resolved." That premise is **already satisfied in shipped code.** End to end today:

- The real game turns autopilot **off** for the avatar — [`GameManager.cs:591`](../../Assets/Core/GameManager.cs#L591) sets `GrittyEventChoices.AutopilotAvatarChoices = false` (headless/harness keep the default `true`).
- An avatar-subject fire **pauses as a pending choice** rather than resolving itself — [`EventConsequenceApplier.cs:170-186`](../../Assets/Narrative/Events/EventConsequenceApplier.cs#L170), with stale-choice forfeit-to-autopilot already handled.
- The **day cannot advance** past it — `GameManager.CanAdvanceDay` ([`:880`](../../Assets/Core/GameManager.cs#L880)) and the `TimeControlBar` both gate on `HasPendingChoice`; the clock auto-pauses and the bar shows a `DecisionNeeded` face.
- The **player picks on the phone** — the Events tab renders the pending event card + one `ReplyChip` button per `Choices[i].Label`, wired to `ResolveChoice(i)` ([`BurnerPhone.cs:772-789`](../../Assets/UI/BurnerPhone.cs#L772)). Resolution writes the outcome card + reaction texts.
- Coverage exists: the `GrittyEventsHarness` has the full pause/resolve/forfeit section; `AchievementManager` consumes `GrittyEventResolvedEvent`.

The content model was authored *for* this: `EventChoice.AutopilotWeight` is documented as the weight "until the choice UI exists," and `Prompt`/`Label`/`Outcome` as text "for the event-choice UI."

**So the mechanism is done. What is thin is its *presentation*.** This slice is the prominence/immersion amplification the user asked for — not a rebuild.

## 2. The gap this slice closes

When an avatar gritty/dirt event fires right now, the *only* signals to the player are:

1. The clock quietly stops.
2. A small grey status line on the time bar reads **"Paused — decision needed"** (`TimeControlBar.DecisionNeededText`, one line under the readout — [`:57`](../../Assets/UI/TimeControlBar.cs#L57), [`:249`](../../Assets/UI/TimeControlBar.cs#L249)).
3. If — and only if — the phone happens to be on the **Events** tab, the unresolved card + reply chips are visible.

The phone is always on-screen (two-panel shell: Dashboard | Phone) but it holds **eight tabs** (Events, History, Messages, Bank, Marketplace, Family, Settings, Calendar). A player mid-plan on Calendar, or reading Messages, gets **no badge, no banner, no cue** that a decision is waiting on a *different* tab — the clock simply stalls with a one-line grey explanation they may not read. The pending card also reads as an abstract "Hustle"/"Family" category heading rather than **a specific person confronting the player** (Sal the bookie, Coach, Mom), even though the contact identity is known and already portrayed in the Messages tab.

For a game whose top design goal is "more interactive / immersive / engaging story," an owed decision that the sim halts the whole world for should be **impossible to miss and feel like a beat**, not a silent stall.

## 3. Design principles (what this pass will and won't do)

- **Amplify attention *to* the phone; do not resurrect a full-screen modal.** The standalone `EventChoiceScreen` was **deliberately retired** in Phase 10b in favor of the phone-native thread ([`Main.cs:18-19`](../../Assets/UI/Main.cs#L18); presentation_layer_narrative.md §4.4). The phone *is* the diegetic decision surface — the immersion win is making the phone demand attention, not covering it with a system dialog.
- **Respect player agency.** A fire should not rip the player out of whatever tab they chose to be on without their consent (see the §5 decision). "Impossible to miss" ≠ "seizes control."
- **Zero sim/schema/content touch.** Everything here reads existing state (`TryGetPendingChoice`, `Contacts.Resolve`, the narrative log) and existing bus/clock seams. The resolution path, forfeit semantics, autopilot toggle, and `MaxFiresPerDay` valve are untouched. No new `Game_State` key, no DDL.
- **Rising-edge from the existing poll, not new bus wiring.** The phone already computes the no-pending → pending transition in `_Process` via its dirty-flag identity check (`_shownHasPending` false→true, [`BurnerPhone.cs:617-634`](../../Assets/UI/BurnerPhone.cs#L617)). That edge is the trigger for every attention cue below — no `GrittyEventFiredEvent` subscription, no thread concerns.
- **Thin vertical slices** (ui_conventions.md): each sub-slice in §6 is independently bootable and demoable.
- **Sound stays optional and deferred** per the user's 2026-07-11 "away from sounds and little things" directive. `UiSound.Alert` already exists (D-1) as a one-line hook; it is named where it belongs (§4.6) but is **not** the substance of this slice, and ships dark until the user re-opens audio.

## 4. The mechanisms

### 4.1 Rising-edge detector (the trigger)

In `BurnerPhone._Process`, the block that already sets `_shownHasPending` gains one branch: when `hasPending` rises from false to true (a *new* pending fire, identity-checked as it is today), raise a transient `_decisionArrived` latch consumed by §4.2–§4.4. When `hasPending` falls to false (resolved/forfeited), clear the badge/banner. No polling beyond what `_Process` already does; no new allocation on idle frames (the dirty-flag guard at [`:625`](../../Assets/UI/BurnerPhone.cs#L625) still short-circuits unchanged frames).

### 4.2 Events-tab unread badge (the persistent cue)

While a choice is pending, mark the **Events** tab itself. Resolve the Events tab index once in `_Ready` the exact way `_marketplaceTabIndex` is derived — `eventsTab.GetIndex()` ([`BurnerPhone.cs:495`](../../Assets/UI/BurnerPhone.cs#L495) precedent) — then prepend the existing `UnreadMarker` (`"• "`, [`:74`](../../Assets/UI/BurnerPhone.cs#L74)) to the Events tab title via `_phoneTabs.SetTabTitle(idx, "• Events")` while pending, restoring `"Events"` on resolve. This reuses the Messages tab's own unread-dot vocabulary, so the two surfaces read consistently. The badge is the **standing** reminder: even after any transient banner is gone, the dot says "a decision still waits here."

### 4.3 Shell attention banner (the impossible-to-miss cue)

A **non-blocking** banner announcing the owed decision, keyed on the §4.1 latch. Two viable homes; the build picks one against the live tree:

- **Preferred:** a thin bar docked at the top of the two-panel shell (a permanent, self-hiding sibling à la the theme-missing banner in [`Main.cs:83-95`](../../Assets/UI/Main.cs#L83), but styled and content-driven), reading e.g. **"📱 {Contact} needs an answer"** with the contact's display name from `Contacts.Resolve(pending.Fired… )`. Tapping it focuses the phone's Events tab (`_phoneTabs.CurrentTab = eventsIndex`) — the *player-initiated* switch, honoring §3's agency rule. It auto-hides on resolve.
- **Fallback:** if a shell-level band is awkward against the shipped layout, the banner lives as the top row of the Events tab's own layout and the §4.4 louder time-bar face carries the cross-tab cue instead.

Copy lives in an `[Export]` string (localization rule, ui_conventions.md). The banner never blocks input, never pauses the tree (the clock is already soft-paused by the existing gate), and carries no minute cost or tier lock (§4.3 never-gates invariant).

### 4.4 A louder "decision needed" time-bar face

The `DecisionNeeded` status line already exists but renders in the same neutral weight as "Paused." Give it visual priority: color it with the shared danger accent (`UiColors` — the danger red already introduced in T-1 / [`Assets/UI/UiColors.cs`](../../Assets/UI/UiColors.cs)), matching how the Needs card flags a critical need. This is a `_statusLabel` theme-color override applied when `face == BarFace.DecisionNeeded`, cleared otherwise — a few lines in `RefreshControls`' face switch ([`TimeControlBar.cs:244-254`](../../Assets/UI/TimeControlBar.cs#L244)), no new node. (A subtle pulse/tween is Slice-F territory — deferred, noted in §4.6.)

### 4.5 Richer pending event card (the "it's a person" beat)

Today `RenderEventsFeed` builds the pending card from category + prompt only ([`BurnerPhone.cs:775`](../../Assets/UI/BurnerPhone.cs#L775)). For the **live, unresolved** card only (past resolved cards stay as-is), add the contact's identity so the decision reads as someone reaching out:

- The contact's **display name** and **portrait** via `Contacts.Resolve(definition… )` + a `PortraitView` — the same component the Messages thread header already uses (`_threadPortrait`, [`:442`](../../Assets/UI/BurnerPhone.cs#L442)), so no new art pipeline.
- The event's **fire-time companion text** (`definition.TextMessage`, the "Knock 'em dead, kiddo" line) rendered inside the card above the choices when present, so the person's voice frames the decision rather than only landing later in Messages.
- The reply chips stay exactly as they are (`Label` → `ResolveChoice(i)`).

**Consequence hints on chips are explicitly out of scope** — surfacing "(−$500, +stress)" under a Label would spoil the authored fiction and leak the closed consequence vocabulary into player-facing copy. If wanted later it is an authored, per-choice `hint` content field (a DIRT-era content decision), not an engine dump.

### 4.6 Deferred hooks (named, not built here)

- **Audio:** one-shot `UiSound.Alert` on the §4.1 rising edge (D-1 already synthesized the tone). One line; ships dark per §3 until the user re-opens audio.
- **Motion:** banner slide-in, badge pulse, resolution-commit flourish on the card — Slice F. This slice keeps everything static-but-prominent; Slice F can layer motion onto these exact nodes later.

## 5. The one decision for the user — LOCKED

**Decision (user, 2026-07-11): badge + banner, NO auto-switch.**

On a new avatar fire the phone does **not** change tabs on its own. Attention is carried by the louder time-bar face (§4.4), the shell banner (§4.3), and the standing Events-tab dot (§4.2); the **only** tab switch is player-initiated — tapping the banner sets `_phoneTabs.CurrentTab = eventsIndex`. This honors §3's agency rule (never yanks the player out of Calendar-planning or a Messages thread) while staying impossible to miss.

(The rejected alternative was auto-switching to the Events tab on every fire — maximally prominent but seizes the tab the player deliberately chose and reads as the UI grabbing the wheel on low-stakes events. The two differ by one line, so a later flip is cheap if playtest disagrees.)

## 6. Sub-slices (each bootable)

- **P-1 — Attention core.** §4.1 rising-edge latch + §4.2 Events-tab badge + §4.4 louder time-bar face. Smallest demoable win: fire an avatar event → the tab dots and the bar goes red-and-loud, clears on resolve. No new scene node beyond the face color.
- **P-2 — Shell banner.** §4.3. Adds the cross-tab "someone needs an answer" bar + tap-to-focus. Resolves the §5 decision at build time.
- **P-3 — Card identity.** §4.5. Portrait + name + companion text on the live card.
- **P-4 (deferred).** §4.6 audio + motion — parked behind the user's audio/polish re-open.

## 7. Non-goals / invariants preserved

- No full-screen modal; no revival of `EventChoiceScreen`.
- No change to `ResolveChoice`, `PickChoice`, the forfeit path, `AutopilotAvatarChoices`, or `MaxFiresPerDay`.
- No `Assets/Simulation/` touch → the MLB bit-identity guard is inert by construction (MC re-run not mandated, but run per the standing rule to prove it).
- No schema change, no new `Game_State` key (the pending choice is transient applier state, not persisted world state — a mid-decision quit re-fires nothing; the pending fire is reconstructed from the same poll on reload exactly as today).
- The §4.3 never-gates invariant holds: neither tab costs minutes or locks by tier.

## 8. Testing & gates

Pure UX — **no new harness** (the tripwire is live boot + eye, the Slice-D/G precedent for presentation-only work). Required:

- `dotnet build` 0/0 across the solution (game + all harness projects).
- `git diff --stat -- Assets/Simulation` **empty**; run `run_monte_carlo_batch` anyway to show the guard byte-exact (345/345, PA 48384 / H 10969 / ER 5237).
- `GrittyEventsHarness` **286/286 unchanged** — the pause/resolve/forfeit section must stay green (this slice reads the same seam it tests, never mutates it).
- SchemaValidator / NeedsDecay / CoreLoop unchanged (no schema, no sim).
- **Live boot (godot MCP)** on a save with a *pending avatar fire staged* (or advance days until one fires): verify the badge appears, the banner announces the right contact, the time-bar face reddens, the card shows the portrait/name, tapping the banner focuses Events, and picking a chip clears **all** cues + advances the clock. Node paths verified via `godot_scene_mapper` before any new `GetNode` is written (ui_conventions.md "verify before wiring").
- **Eye-gate = user playtest**: does the owed decision now feel unmissable and like a person, not a stalled clock?

## 9. Model assignments

- **P-1 / P-2 / P-3** — **Sonnet 5** (pure `Assets/UI/` wiring over frozen seams; no zero-GC subtlety, no sim, no harness), **or Fable 5** if reallocating. Each sub-slice → **Fable 5 review** (the standing review of every build), which also runs the §8 gate set + live boot.
- **P-4** — parked until the user re-opens audio/polish.

## 10. Open seams (disclosed)

- **Banner home (§4.3)** — shell-level vs Events-tab-top is a build-time call against the shipped `TwoPanelShell.tscn` layout; both satisfy the requirement, the shell-level bar is preferred for cross-tab reach.
  - **RESOLVED (build P-2 + Fable review, 2026-07-11):** the build adopted a third home — a phone-internal banner inside `BurnerPhone`'s own `ScreenLayout`, docked between the status bar and the tab container. Review verdict: **approved, preferred over both sketched options.** It keeps the §4.3 requirement (visible across all eight tabs), reads diegetically as a phone notification rather than a system strip, and keeps tap-to-focus wiring local — a Main-level bar would have needed a cross-scene signal hop to switch the phone's tab (ui_conventions.md forbids reaching into another scene's tree). The dashboard half of the screen is still covered by the §4.4 red time-bar face, so nothing outside the phone loses its cue.
- **Multiple stacked owed decisions** — the applier holds exactly one pending choice and forfeits the prior to autopilot when a second fire needs the slot ([`EventConsequenceApplier.cs:176-182`](../../Assets/Narrative/Events/EventConsequenceApplier.cs#L176)). This slice surfaces **the one** current pending choice; it does not add a queue. If the forfeit-on-second-fire ever feels bad *with* the new prominence (a player watching a decision get auto-resolved out from under a visible banner), a pending-choice queue is a **separate** applier-layer slice, out of scope here and flagged for the user's playtest verdict.
