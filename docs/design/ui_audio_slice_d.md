# UI Audio Foundation — Slice D

**Owner of this doc:** Opus 4.8 (architecture/design only — NO code). Per the standing roadmap split,
Opus owes this design *before* any build. Slice D is tagged in [game-improvements-roadmap] /
`docs/progress.md` as **"UI audio foundation — greenfield (NO audio infra/assets exist anywhere):
autoload `UiSfx`, pooled AudioStreamPlayers, placeholder tones, button/tab wiring. Unlocks B's parked
volume slider."** Likely **Fable 5** builds it (the roadmap pre-assigned it; §10 notes where Sonnet
could take the pure-wiring sub-slices).

Grounded against the live code this session (No Blind Queries applies to design too):
`project.godot` (`[autoload]` — **`GameManager` is the *only* autoload**; `[gui] theme/custom`),
`Assets/Core/GameManager.cs` (the singleton pattern `Instance` at `:216`, `ProcessMode = Always` at
`:219`, the subsystem-property convention `:58`–`:204`, the frame pump `_Process` at `:634`, teardown
`_ExitTree` at `:643`, the checkpoint `PersistTimeOfDay`/`SaveNow` idiom at `:892`),
`Assets/UI/BurnerPhone.tscn` (the **parked** `VolumeSlider` at `:717` — `editable = false`,
`value = 100`, `min 0`/`max 100`/`step 1`; the `VolumeNoteLabel` "Sound arrives in a later update." at
`:726`; the `OptionsCard` at `:696`), `Assets/UI/BurnerPhone.cs` (the button-wiring idiom —
`.Pressed +=` in `_Ready`/`-=` in `_ExitTree`; the tab hook `_phoneTabs.TabChanged += OnPhoneTabChanged`
at `:494`), and `Assets/Data/Database/GameStateQueries.cs` (the KV persistence surface, for the §3.5
alternative only). `ui_conventions.md` and `database_rules.md` are the acceptance frame.

**Greenfield confirmed by grep:** zero `AudioStreamPlayer`, zero `AudioServer`, zero `.ogg`/`.wav`, and
**no `default_bus_layout.tres`** exist anywhere. The project ships on the single implicit `Master` bus
with silence. This slice is pure addition — nothing to refactor, nothing to break.

---

## 1. Thesis & Scope — what this slice is, and what it is deliberately not

The user's ask (`GAME_IMPROVEMENTS.md`, and Slice B's own parked artifact) is modest and concrete:
**the game should make UI sounds, and the Settings-tab volume slider should work.** Slice B built the
slider disabled and left the note "Sound arrives in a later update" precisely so this slice could
finish the thought.

The governing decision:

> **Slice D lays an audio *foundation*, not a soundtrack.** It stands up one global audio service
> (`UiSfx`), a pooled set of voices, a small **semantic vocabulary** of UI sound roles, and
> **procedurally-synthesized placeholder tones** so the game is audible today with **zero committed
> audio assets**. Real recorded assets drop into the same enum→stream table later with **zero
> call-site churn** — the *slots* are the seam, the tones are throwaway.

Everything below follows from four load-bearing facts:

1. **No assets exist and we do not want to commit binary blobs for a placeholder pass.** So the
   placeholders are **synthesized in C# at boot** into `AudioStreamWAV` clips (§6) — short enveloped
   blips, one distinct timbre per semantic role. Nothing lands in `Assets/`; nothing needs a Phase-9
   export-filter entry (unlike the `.json`/`.sql` content did). When real audio arrives it is a data
   swap, not a code change.
2. **Audio must survive scene swaps and the at-bat SceneTree freeze.** A click during a modal, a
   "cash" sting when a sale resolves mid-overlay — the service must play regardless of tree pause.
   That means a **`ProcessMode = Always` autoload**, mirroring GameManager's own reason for it
   (`GameManager.cs:217`–`:219`).
3. **UI conventions forbid per-spawn allocation and per-frame churn.** `ui_conventions.md`: "Pooled UI
   elements for anything spawned in volume." Sounds are spawned in volume. So voices are a **fixed
   pre-created pool**, allocated round-robin, and streams are **synthesized once** — no `new` on the
   play path (§3.3). This is the zero-GC posture the codebase holds everywhere else.
4. **Volume is a device preference, not world state.** This is the one genuine architectural decision
   (§3.5) and it diverges from Slice G on purpose: the wall-clock's minute *is* per-save world state,
   so it rode `Game_State`; a player's volume preference is not — it should survive a new game, a save
   wipe, or switching saves. Recommended home: a `user://settings.cfg` `ConfigFile`, fully decoupled
   from the DB.

**Non-goals** (§8 expands): no music/soundtrack, no positional/3D audio, no per-event ambient beds, no
voice/dialogue, no at-bat crack-of-the-bat foley (a later presentation slice), no committed audio
assets, no new harness (audio is pure UX — there is nothing deterministic to assert; the tripwire is a
live boot + the ear).

**Four hard rules (carried from `ui_conventions.md` / `database_rules.md` / CLAUDE.md):**

1. **Zero `Assets/Simulation/` touch.** `run_monte_carlo_batch` is inert by construction (prove with
   `git diff --stat -- Assets/Simulation` — the Slice A/B/G precedent). Audio is presentation-only.
2. **No allocation on the play path.** Streams synthesized once at `_Ready`; voices pooled and reused;
   `Play(UiSound)` does a table lookup + a round-robin index bump + `AudioStreamPlayer.Play()`. No
   LINQ, no string work, no `new`.
3. **No schema change.** The recommended `ConfigFile` home is not the DB at all; even the §3.5
   fallback (Game_State KV) is additive keys, never DDL. `SchemaValidator` re-runs green *unchanged*.
4. **Thin vertical slices (§7).** Each sub-slice leaves the game bootable and every screen reachable.

---

## 2. Seam Audit — what the service needs, and what already exists

| Need | Live seam | New surface? |
| :--- | :-------- | :----------- |
| A global service reachable from any UI script | GameManager's `Instance` singleton + autoload registration (`project.godot [autoload]`, `GameManager.cs:216`) | **new second autoload `UiSfx`** with its own `Instance` (§3.1) — the roadmap's named "autoload `UiSfx`" |
| Play while the tree is paused | `ProcessMode = Always` (`GameManager.cs:219`) | `UiSfx` sets the same |
| A place to route SFX volume without touching a future Music bus | only the implicit `Master` bus exists | **new minimal `default_bus_layout.tres`**: `Master` → child `SFX` bus (§3.2) |
| Every button already emits a signal on press | `BaseButton.Pressed` — every screen wires it (`BurnerPhone.cs` `_Ready`) | **auto-wire once** via `SceneTree.NodeAdded` (§4), not 200 hand edits |
| Every tab already emits on change | `TabContainer.TabChanged` (`BurnerPhone.cs:494`) | auto-wire the same (§4) |
| The parked slider | `BurnerPhone.tscn:717` (`editable = false`) + `VolumeNoteLabel:726` | un-park: make editable, wire `ValueChanged`, delete the note (§5) |
| A device-scoped preference store | none (Game_State is save-scoped) | **new `user://settings.cfg` via `ConfigFile`** (§3.5, recommended) |

**The reassuring finding:** the two things we want to hear — button presses and tab switches —
*already* funnel through exactly two Godot signals (`BaseButton.Pressed`, `TabContainer.TabChanged`).
A single auto-wire pass over `SceneTree.NodeAdded` gives the entire game a default click and tab sound
from **one integration point**, with no per-screen edits and no risk of missing a button. The semantic
stings (§4.2) are then a small, curated hand-placed set on top.

---

## 3. Architecture — one autoload, one bus, a pool, a vocabulary

### 3.1 `UiSfx` — the global audio service (a second autoload, in `Assets/UI/`)

A `Node`-derived autoload registered in `project.godot [autoload]` *after* `GameManager` (order only
matters in that both are ready before the main scene spawns — the auto-wire in §4 relies on `UiSfx`
existing before any UI node enters the tree, which autoload ordering guarantees). It mirrors
GameManager's shape:

- `public static UiSfx Instance { get; private set; }` set in `_Ready` (the `GameManager.cs:216`
  idiom) so any script calls `UiSfx.Instance.Play(...)` without a node-path lookup.
- `ProcessMode = ProcessModeEnum.Always` — plays through the at-bat/menu tree freeze (§1 fact 2).
- Lives in **`Assets/UI/`**, NOT `Assets/Core/`. It is engine-coupled (owns `AudioStreamPlayer`
  children, `AudioServer` calls) and must **never** be dragged into `Tools/CoreLoopHarness`'s
  Core-compile surface, which exists specifically to catch engine leaks into Core. UI is the correct
  home; the autoload path in `project.godot` can point anywhere.

Why a *second autoload* rather than a `GameManager.UiSfx` subsystem property: audio has **zero
dependency on the save/DB** for its core job (playing sounds). Keeping it a sibling autoload keeps it
out of GameManager's heavy boot (DB open, schema apply, world-gen) and honors CLAUDE.md separation of
concerns — the sim and the audio service share nothing. The only storage audio touches is the volume
preference, and (§3.5) that is a `ConfigFile`, not the DB, so the decoupling is total.

### 3.2 The audio bus — `Master` → `SFX`

Create a minimal `default_bus_layout.tres` (Godot picks it up automatically; it is a normal `.tres`
resource, included in exports by default — no filter chore):

- **Bus 0 `Master`** (required, always present).
- **Bus 1 `SFX`**, sends to `Master`. All pooled voices target `"SFX"`. The volume slider drives
  *this* bus, so a future Music/ambient bus (§8, deferred) can be added and volumed independently
  without re-plumbing the slider.

The slider is a 0–100 linear percent; the bus wants decibels. Map with
`AudioServer.SetBusVolumeDb(sfxBusIdx, Mathf.LinearToDb(pct / 100f))`. Edge case: `LinearToDb(0)` is
`-inf` — at `pct == 0`, **mute the bus** (`AudioServer.SetBusMute(sfxBusIdx, true)`) instead of
setting `-inf` dB, and unmute for any `pct > 0`. Resolve the bus index once at `_Ready`
(`AudioServer.GetBusIndex("SFX")`), cache it, never per-play.

### 3.3 The voice pool — fixed, round-robin, zero-alloc

At `_Ready`, create a fixed array of **N `AudioStreamPlayer`** children (recommend **N = 8**; tunable
constant) added under `UiSfx`, each with `Bus = "SFX"`. A single `int _nextVoice` cursor round-robins:
`Play` grabs `_players[_nextVoice]`, sets its `Stream`, calls `Play()`, advances the cursor
(`_nextVoice = (_nextVoice + 1) % N`). Overlapping sounds (a click landing on top of a still-ringing
"cash" sting) simply use the next voice; only when all 8 are busy does the oldest get retasked — for
sub-200 ms UI blips at human click rates this is inaudible. **No allocation on this path**: players
pre-exist, streams pre-exist (§3.4), the cursor is an int. This is the `ui_conventions.md` object-pool
mandate applied to audio.

(`AudioStreamPlayer`, non-positional, is correct for UI — not `AudioStreamPlayer2D/3D`. UI sound has
no world position.)

### 3.4 The semantic vocabulary — `enum UiSound`

The heart of the "swap placeholders for real assets later, free" design. A small enum names UI sound
*roles*, and a fixed `AudioStream[]` indexed by the enum maps role → clip:

```
enum UiSound { Tap, Confirm, Back, Error, TabSwitch, Cash, Alert, DayTick }
```

- **Tap** — the default button press (the workhorse; §4.1 auto-wires it to every button).
- **Confirm** — a positive commit: purchase success, Save Now, plan confirmed, choice accepted.
- **Back** — close/cancel/navigate-back (quit-dialog cancel, overlay dismiss).
- **Error** — a denied action: insufficient funds, invalid input, blocked advance.
- **TabSwitch** — a `TabContainer` tab change (§4.1 auto-wires it).
- **Cash** — money arrives: a sale/hustle payout, funds gained.
- **Alert** — a gritty event fires / a choice now awaits the player (a soft attention chime).
- **DayTick** — the day advanced / a clock milestone (a subtle marker, used sparingly).

Keep it ~8 roles — enough to feel designed, small enough to synthesize and later re-record cheaply.
`Play(UiSound s)` indexes the array and hands the stream to the round-robin pool (§3.3). **The enum is
the stable contract**; the streams behind it (procedural today, recorded tomorrow) are swappable
without any caller changing.

### 3.5 Volume persistence — DECISION (recommended: `user://settings.cfg`)

This is the one real decision, and it deliberately **diverges from Slice G**. Slice G persisted the
wall clock in `Game_State` because the minute-of-day genuinely *is* per-save world state. **Volume is
not world state — it is a device/application preference.** Tying it to the save DB would mean a fresh
day-1 avatar, a save wipe, or a different save silently resets the player's volume. That is the wrong
behavior and the wrong coupling (it would drag `UiSfx` into `GameManager`→`DatabaseManager`).

| Option | Where | Pros | Cons |
| :----- | :---- | :--- | :--- |
| **A — `ConfigFile` (recommend)** | `user://settings.cfg`, `[audio] sfx_volume` | Correct semantics (per-device); **zero DB coupling** — `UiSfx` never touches the sim or the save; survives new-game/save-wipe/save-switch; the standard Godot options idiom; self-contained (load in `UiSfx._Ready`, save on slider release). | Introduces one new tiny persistence file (well-scoped, ~5 lines of `ConfigFile`). |
| **B — Game_State KV** | new `sfx_volume` key via `GameStateQueries` | Consistent with Slice G; reuses the existing KV surface. | **Wrong semantics** (volume dies with the save); couples `UiSfx`→GameManager→DB; the write has to ride GameManager's checkpoints instead of being self-contained. |

**Recommendation: Option A.** Volume is the textbook case a `ConfigFile` exists for, and it keeps the
audio service a clean island. Load once in `UiSfx._Ready` (absent file / absent key → default 100),
apply to the bus, seed the slider; save on slider `DragEnded`/`ValueChanged` (debounced to release, not
per-tick). This is the single item in this doc the user should confirm or veto before Fable builds —
the rest is settled (§10).

---

## 4. The wiring model — auto-wire the default, hand-place the semantics

The roadmap's "button/tab wiring" is the part that could balloon into touching every `.Pressed +=`
site across ~12 screens. It doesn't have to. Split it:

### 4.1 Default click + tab, auto-wired from ONE place (D-2)

In `UiSfx._Ready`, connect to `GetTree().NodeAdded`. On each node added:

- If it is a `BaseButton` and not already connected, connect its `Pressed` → `Play(UiSound.Tap)`.
  Guard with `IsConnected` so re-parenting can't double-wire. (A disabled button never emits `Pressed`,
  so no filter needed there.)
- If it is a `TabContainer`, connect its `TabChanged` → `Play(UiSound.TabSwitch)`.

This gives **every button and every tab in the game** a sound from a single integration point — no
per-screen edits, and structurally impossible to "miss a button." Autoload ordering guarantees `UiSfx`
is listening before the first UI node spawns.

**Opt-out:** any control that must stay silent (e.g., a slider handle rendered as a button, or a
rapid-repeat control) is excluded by putting it in a `"silent_ui"` group; the `NodeAdded` handler skips
group members. This is the escape hatch; expect to use it rarely.

**Toggle nuance:** `CheckButton`/`CheckBox`/`OptionButton` also derive `BaseButton` and will click on
toggle — that is desirable (a toggle *is* a UI action). No special-casing unless a specific control
feels wrong in playtest, then group-opt-out.

### 4.2 Semantic stings, hand-placed at a curated few sites (D-3)

The non-default sounds are worth a deliberate, *small* set of explicit `UiSfx.Instance.Play(...)` calls
at high-signal moments (these are the only hand edits, and they are a handful, not a sweep):

- **Confirm** — `BurnerPhone.OnSavePressed` (save ok), successful purchase/upgrade
  (`OnBuyItemPressed`/carrier buys on success), plan/commitment confirmed, event-choice accepted.
- **Error** — insufficient-funds / denied branches already present in those same handlers (they set a
  "Not enough cash" label today — the sound rides the same branch).
- **Cash** — funds-gained moments (hustle payout, sale) where the code already knows a positive
  delta landed.
- **Alert** — where a gritty event becomes a pending choice (the same transition `BurnerPhone._Process`
  already dirty-flags for the Events feed) or a delayed text arrives.
- **Back** — quit-dialog cancel, overlay/dialog dismiss.
- **DayTick** — the `TimeControlBar`'s midnight roll / Skip Day (sparingly — this fires a lot; may be
  left off if it grates).

These sit *behind the default Tap*: a button that also means "confirm" plays Confirm (the explicit call
wins by being more specific — or simply group-opt-out the Tap on those buttons and call the semantic
sound). Keep the curated set short; it is polish, not coverage.

---

## 5. Un-parking Slice B's volume slider

Slice B built the slider deliberately inert (`BurnerPhone.tscn:717` `editable = false`, `value = 100`)
with the caption "Sound arrives in a later update." (`:726`). Slice D finishes it:

1. `.tscn`: set `editable = true` on `VolumeSlider`; **delete** the `VolumeNoteLabel` (or repurpose it
   to a live "SFX volume" caption). The `0–100`/`step 1` range stays.
2. `BurnerPhone.cs`: in `_Ready`, get the slider, seed its `Value` from `UiSfx.Instance.Volume`
   (which `UiSfx` loaded from config, §3.5), and connect `ValueChanged` →
   `UiSfx.Instance.SetVolume((float)v)` (applies to the bus live, so the player hears the level while
   dragging). Save-to-config debounces to `DragEnded` (avoid a `ConfigFile.Save` per integer tick).
   Disconnect in `_ExitTree` per the file's existing signal-hygiene idiom.
3. The slider is a **read/write view of one authority** (`UiSfx.Volume`) — exactly the ui_conventions
   "UI renders service state, emits intent" posture. `UiSfx` owns the number; the slider reflects and
   nudges it.

A tiny nicety worth including: play a single `UiSound.Tap` (or a dedicated soft blip) on slider release
so the player *hears* the chosen level at the moment they set it — the standard "volume preview" touch.

---

## 6. Placeholder tone design — synthesized, throwaway, distinct

No assets, no blobs: at `_Ready`, `UiSfx` **synthesizes** each `UiSound`'s clip into an
`AudioStreamWAV` (`Format16Bits`, mono, `MixRate 44100`) by filling a `byte[]` with a short enveloped
waveform in C#. Each role gets a distinct, legible character (frequencies/lengths are tuning knobs, not
commitments — the point is *distinguishable*, not *pretty*):

| Role | Character (placeholder) | ~len |
| :--- | :---------------------- | :--- |
| `Tap` | short high sine click, fast decay | 40 ms |
| `Confirm` | two-note rising blip (e.g. 660→880 Hz) | 120 ms |
| `Back` | two-note falling blip (880→660 Hz) | 120 ms |
| `Error` | low square/triangle buzz (~160 Hz) | 160 ms |
| `TabSwitch` | soft mid tick, gentle envelope | 50 ms |
| `Cash` | bright triad/arpeggio blip | 180 ms |
| `Alert` | clear two-tone chime (attention) | 220 ms |
| `DayTick` | subtle low marker, quiet | 90 ms |

All share a short attack + exponential decay envelope so nothing clicks/pops (DC-safe start/end at
zero). This is ~one small synth helper (a `MakeTone(freqs, ms, wave)` → `AudioStreamWAV`) called eight
times. **These are explicitly disposable** — the moment real recorded `AudioStream` resources exist,
they replace the synth calls in the enum→stream table (§3.4) and every call site is unchanged. Nothing
downstream knows or cares whether the stream was synthesized or loaded.

(If Fable prefers, `AudioStreamGenerator` real-time playback is an alternative to pre-baked
`AudioStreamWAV`; pre-baked is simpler and zero-runtime-cost, so it is the recommendation. Either way,
no committed asset.)

---

## 7. Sub-slices (thin vertical, each bootable)

1. **D-1 — the service + bus + pool + tones + slider.** Add the `UiSfx` autoload (register in
   `project.godot`), the `default_bus_layout.tres` (`Master`→`SFX`), the 8-voice pool, the `UiSound`
   enum + synthesized placeholder streams, the `Play(UiSound)` / `Volume` / `SetVolume` API, and the
   `ConfigFile` load/save (§3.5). Un-park the volume slider (§5). *No game-wide wiring yet* — prove the
   service in isolation. **Verifiable live:** the slider moves the level audibly, a release-preview blip
   plays, quit+relaunch restores the saved volume; boot log shows the pool/bus up; errors empty.
2. **D-2 — auto-wire default Tap + TabSwitch.** The single `SceneTree.NodeAdded` hook (§4.1) + the
   `"silent_ui"` opt-out group. **Verifiable live:** every button clicks, every tab switch sounds, from
   one integration point; no double-fire on re-parent; grouped controls stay silent.
3. **D-3 — semantic stings.** The curated hand-placed `Confirm`/`Error`/`Cash`/`Alert`/`Back`/`DayTick`
   calls (§4.2). **Verifiable live:** a purchase confirms, an insufficient-funds click errors, a payout
   cashes, a pending event alerts.
4. **D-4 (optional, deferred) — Music/ambient bus + second slider.** A second `Music` bus under Master
   and a matching Settings slider, hooked but with no content yet (or a single looping ambient bed).
   *Skippable; folds toward Slice E/F presentation work.*

Each sub-slice compiles, boots, and leaves every screen reachable.

---

## 8. Non-goals (explicit)

- **No music/soundtrack, no ambient beds.** The `SFX` bus and a *reserved* Music bus are the seam;
  content is later (D-4 / a presentation slice).
- **No positional/3D audio, no voice/dialogue.**
- **No at-bat foley** (bat crack, crowd) — that belongs to the at-bat presentation layer, not the UI
  foundation.
- **No committed audio assets.** Placeholders are synthesized; real assets are a later data drop.
- **No `Assets/Simulation/` touch** → `run_monte_carlo_batch` inert by construction (`git diff --stat`).
- **No schema change** (recommended `ConfigFile` home is not the DB; the §3.5 fallback is additive KV,
  never DDL).
- **No new harness.** Audio is pure UX with nothing deterministic to assert; the tripwire is the live
  boot + the ear (§9). Existing harnesses re-run only to prove *no regression*, and they are untouched
  by construction.

---

## 9. Verification & expected gates

Slice D is UI/presentation only: one new autoload + one bus resource + one un-parked slider + a wiring
hook. Expected gate posture:

- **Build 0/0** (game + all 6 harness projects). `UiSfx` lives in `Assets/UI/`, so **no harness
  compiles it** — `CoreLoopHarness`'s Core-leak scan never sees it, and the sim harnesses never
  reference it.
- **`run_monte_carlo_batch` NOT re-run-mandated** — `git diff --stat -- Assets/Simulation` empty by
  construction; state it (the Slice A/B/G precedent).
- **`SchemaValidator` unchanged** — recommended persistence is a `ConfigFile`, not the DB; `PRAGMA
  user_version` does not move. (Even under the §3.5-B fallback it is additive KV, not DDL — still
  green unchanged.)
- **CoreLoop / NeedsDecay / GrittyEvents / Schema** unchanged — none touch audio.
- **Live boot (godot MCP) on the real save:** the boot log shows the SFX bus + voice pool initialized;
  buttons click; tabs sound; the volume slider moves the level and mutes at 0; a purchase confirms and
  a denied action errors; **quit + relaunch restores the saved volume**; errors empty. The Steam-DLL
  absent notice remains the known-benign dev condition. *(The godot-MCP "hear it" limitation is the
  same eyeball seam every UI slice discloses — structural/headless verification confirms the players
  exist and the bus responds; the actual audibility is the user's playtest ear.)*

---

## 10. Decisions & model assignments

**Settled (nothing blocks the build):**

- **Service shape:** a second `ProcessMode.Always` autoload `UiSfx` in `Assets/UI/`, GameManager's
  `Instance` idiom, decoupled from the sim/DB (§3.1). *Settled.*
- **Voices:** fixed 8-voice pooled `AudioStreamPlayer` round-robin, zero-alloc play path (§3.3).
  *Settled* (pool size is a tuning knob).
- **Vocabulary:** the `UiSound` enum → stream table is the stable contract; placeholders synthesized,
  real assets swap in free (§3.4, §6). *Settled.*
- **Wiring:** auto-wire `Tap`/`TabSwitch` from one `SceneTree.NodeAdded` hook + a `"silent_ui"`
  opt-out; curated semantic stings on top (§4). *Settled.*
- **Bus:** `Master`→`SFX`, slider drives `SFX` via `LinearToDb` with a mute-at-0 edge (§3.2). *Settled.*
- Tone frequencies/lengths (§6), pool size, and the exact curated sting sites (§4.2) are build-time
  tuning knobs, not design commitments.

**The one item to confirm before Fable builds:**

- **Volume persistence home (§3.5): recommend `user://settings.cfg` `ConfigFile` (Option A)** — correct
  per-device semantics + total DB decoupling — over Game_State KV (Option B). This is the only place
  Slice D intentionally diverges from Slice G, and it is the user's call. If the user prefers
  save-scoped consistency with G, flip to B; Fable's D-1 changes only the ~5 persistence lines.

**Model assignments (per the standing model-per-task workflow):**

- **D-1 — Fable 5.** The autoload + bus + zero-GC voice pool + procedural synth is the load-bearing,
  allocation-sensitive foundation the roadmap already tagged for Fable. Carries no sim risk (MC inert),
  so no re-band burden — but the pooling/synth discipline is Fable-flavored.
- **D-2 / D-3 — Fable 5, or Sonnet 5 if reallocating.** Pure wiring under this spec (the auto-wire hook
  and the curated sting calls) has no zero-GC subtlety beyond calling the D-1 API and no harness
  tripwire; it is Sonnet-safe if the user wants to reserve Fable for calibrated-core work. Roadmap
  default is Fable for the whole slice.
- **D-4 — deferred** (folds toward Slice E/F); assign when its slot comes.

**The design is complete and unblocked** pending the single §3.5 confirmation. Next step: Fable builds
**D-1** (service + bus + pool + tones + un-parked slider), then D-2 (auto-wire) and D-3 (stings).

---

*Related: `real_time_clock_slice_g.md` (the autoload + persistence-decision precedent this mirrors and
deliberately diverges from), `ui_conventions.md` (pooling / no-per-frame-alloc / thin-slice frame),
`database_rules.md`, `docs/game-idas/GAME_IMPROVEMENTS.md`. Roadmap slot: Slice D in `docs/progress.md`.
Unlocks Slice B's parked `VolumeSlider`. Feeds Slice F (motion) — a tween that also plays a sound is a
one-liner once this exists.*
