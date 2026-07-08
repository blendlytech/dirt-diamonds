# At-Bat Presentation — Making the Micro-Sim Read as Baseball (Phase 12d)

**Owner of this doc:** Opus 4.8 (presentation architecture + the sim seam — NO code). Per the standing
Phase-12 split this is the pending Opus pass 12a/12b/12c flagged (`surface_the_sim.md` §8 names 12d
"the sibling step, with its own pending Opus design pass — not designed here"). 12d is the
**at-bat presentation** step of the Phase-12 "Saleable" arc
(12a render integrity → 12b layout → 12c surface-the-sim → **12d at-bat presentation** → 12e sound →
12f identity → 12g juice/onboarding → 12h store assets). The recommended build split (§10) lands the
sim seam first, then the presentation on top.

Grounded against the live code this session (No Blind Queries applies to design too):
`Assets/Simulation/Baseball/PitchChain.cs` (`SimulatePa` at `:595`, the per-pitch loop + class draw at
`:704-727`; `IBatterPolicy` at `:265`), `Assets/Simulation/Baseball/MicroGame.cs` (the human-PA driver
at `:858-891`), `Assets/Simulation/Baseball/InteractiveBatterPolicy.cs` (the `PlayerIntentBridge`
handshake + the two interactive policies), `Assets/UI/AtBatView.tscn` + `.cs` (the Phase-4 slice, now
the `CenterSlot` occupant), and `Assets/UI/BaseballDashboard.cs` (`_Process` at `:308-365`, the bridge
pump). `ui_conventions.md`, `.claude/rules/baseball_engine.md` (the micro-sim contract), and
`baseball_markov_micro_sim.md` (the §5/§6/§7 model this presents) are the acceptance frame.

---

## 1. Thesis & Scope

The attended game **works** and is **invisible**. The player sets a timing slider, reads a pitch cue,
clicks Swing or Take — and then the count silently changes on the next snapshot with **no explanation
of what just happened to the pitch they swung at**. There is no "strike swinging," no "fouled it
back," no "you were out in front," no "SMOKED it." The largest, most-savored surface in the game — the
one moment the whole life-sim/career scaffold exists to deliver — currently reads as a spreadsheet
that updates a `0-1` label. **A baseball game where you cannot feel a single pitch does not read as a
baseball game.** 12d turns the micro-sim the engine already runs, pitch by pitch, into a presented
at-bat.

The engine is not the problem. `PitchChain.SimulatePa` already computes, for every pitch, its class
(ball / strike / foul / in-play), the true thrown type, the true location, and — at the terminal
pitch — the plate-appearance outcome. **It throws all of that away** (`PitchChain.cs:704-727`): the
result of the loop iteration updates two local `int`s and continues. The UI is handed only the
*pre-pitch* snapshot (count + a blurred cue) and, at the very end, the `PaOutcome`. The presentation
gap is a **data-surfacing gap first** and a motion-design gap second.

**Four hard rules, carried from `ui_conventions.md` and the micro-sim contract:**

1. **UI stays read-only over sim state.** The presentation renders what the sim reports; it never
   writes the DB, never mutates sim state, never re-derives a probability. The one new sim-side change
   is an **observational hook** (§2) — a report, not a decision.
2. **The calibrated core does not move — asserted, not assumed.** 12d is the first sanctioned
   `Assets/Simulation/Baseball/` touch since Phase 9, so `run_monte_carlo_batch` is **no longer inert
   by construction** and MUST re-run. The seam is a no-op on every non-interactive policy and reorders
   zero RNG draws, so 266/266 holds and the MLB bit-identity guard stays exact **by construction**
   (§2, §7). This is the load-bearing acceptance gate and the reason 12d owes an Opus pass.
3. **The sim races; the UI paces (dirty-flag, never per-frame busy-work).** The sim resolves a PA far
   faster than a human watches one. The presentation is a **beat sequencer** that plays the sim's
   event stream back at broadcast pace and gates player input until the current beat finishes (§3).
   No query, LINQ, or string-format runs per frame — beats are event-driven, tween-driven.
4. **Thin vertical slice.** 12d ships as three sequenced sub-slices (§6), each leaving the game
   bootable and an attended game playable end to end — the seam first, then the pitch reveal, then the
   PA-result juice.

**Non-goals.** No sprite/animated-character art — the presentation is **typographic + motion** within
the established theme (the portrait budget is procedural fallbacks; a 2D animated batter/pitcher is out
of scope and out of the art budget). No pitching-side presentation — no pitching UI exists (the
`InteractivePitcherPolicy` is engine-only, disclosed since v4), so 12d is **batter-only**; the seam is
designed to extend symmetrically later (§8). No sound — that is **12e**, and the beat structure here is
explicitly designed to be the SFX trigger surface it hangs on (§8). No schema change, no new persisted
state (a presentation is ephemeral) — `PRAGMA user_version` stays 10.

---

## 2. The Seam Finding — the sim discards the per-pitch result (the load-bearing section)

A presentation that can't say *why* the count changed is worse than useless; reconstructing it
UI-side from count deltas is **lossy and wrong**: at fewer than two strikes a foul and a called strike
both read as "strikes +1," a swinging strike and a called strike are indistinguishable, and the
pitch that *ends* the PA (the in-play ball, the punch-out, the ball four) can't be rendered at all
because it never reaches a snapshot. The only correct source is the sim itself, at the pitch it
already computes.

**The seam already exists in miniature — this is the `FeedSink` pattern, one grain finer.** The
attended-game NPC play-by-play (`NpcPaFeedEvent`, `MicroGame.FeedSink`, `PlayerIntentBridge._npcFeed`)
is *exactly* this shape: an optional, observational, UI-only event stream that is **null on the
macro/harness hot path** (so the zero-GC and bit-identity contracts are untouched) and published only
when a live viewer has attached. 12d extends that established, harness-proven pattern from the
**PA grain** (already surfaced) to the **pitch grain** (currently discarded) of the *human's own* PA.
The `RivalryLedger` / `AvailabilityLedger` / `EquipmentLedger` optional-field precedent is the same
idea: an additive observer that the hot path never pays for.

**Why the sim touch is provably safe (the bit-identity contract).** The hook is a method call inserted
between existing statements in `SimulatePa`. The single invariant that makes it inert:

> **No `rng.NextDouble()` / `rng.NextInt()` call in `SimulatePa` is added, removed, reordered, or
> made conditional. The hook consumes no RNG and returns nothing the loop reads.**

Under that invariant the draw sequence for any given seed is byte-for-byte what it is today, so:

- `NeutralBatterPolicy` (the harness's headless stand-in and every autopiloted/skipped game) implements
  the hook as an **empty `readonly` no-op** — a benched-avatar, macro, or MonteCarlo game is
  bit-identical to today. This is what the **MLB bit-identity regression guard** (PA/H/ER exact) and the
  full §8 tier-ladder bands re-confirm; a moved band means the invariant was violated and the change is
  wrong, not the calibration.
- Only `InteractiveBatterPolicy` (a live human, a handful of PAs per game — the doc's own "not zero-GC
  constrained, clarity over allocation" path) does real work in the hook: forward to the bridge.

This is the "wrong-is-worse-than-slow" core of 12d, and the whole reason it is an Opus seam and not a
straight Sonnet UI slice: the *presentation* is ordinary UI volume, but the *seam* sits in the hottest
calibrated method in the codebase and its correctness is a re-run-the-harness-and-prove-nothing-moved
claim, exactly like every prior sim-adjacent observer.

---

## 3. The Beat Model — the at-bat as a broadcast sequence

An at-bat is a sequence of **beats**. The sim emits them as fast as it can solve; the UI plays them at
a pace a human reads as baseball, and holds the player's next input until the current beat lands.

```
  ┌── per human pitch ───────────────────────────────────────────────┐
  │  1. AwaitingInput  cue shown ("Looks like: Fastball — zone 45%")  │  ← controls ENABLED
  │                    player sets timing + read, commits Swing/Take  │
  │  2. PitchInFlight  controls locked; short windup/travel beat      │  ← the pitch "arrives"
  │  3. PitchReveal    the truth: true type + location + class,       │  ← the missing beat
  │                    count/bases/score tween their deltas           │
  └──────────────────────────────────────────────────────────────────┘
        │ (PA continues)                    │ (terminal pitch: BB / K / in-play)
        └── back to 1, next snapshot        ▼
                                     4. PaReveal   "STRIKEOUT" / "WALK" / "SINGLE" / "HOME RUN!"
                                                   bigger emphasis, runners advance, score bumps
                                                   │
                                     5. NpcFeed    queued NPC PAs play out (paced), then
                                                   back to 1 with the next human PA's first pitch
```

**The pacing architecture — sim races, UI paces, input gated (the interaction-design core).** The
critical structural fact, verified in the code: `InteractiveBatterPolicy.NextPitch` calls
`bridge.AwaitIntent`, which **blocks the sim thread** until the UI submits. So:

- **Within a PA**, the sim can be **at most one human pitch ahead** of the UI: it publishes pitch N's
  result and pitch N+1's pre-pitch snapshot, then blocks on N+1's `AwaitIntent`. It cannot advance
  further without the human's input, and the UI won't release that input until pitch N's reveal
  finishes. Bounded.
- **At a PA boundary**, the sim runs past the human PA — publishing the terminal pitch result, then
  the PA outcome, then a burst of NPC-PA feed events — and blocks at the *next* human PA's first
  `AwaitIntent`, publishing that pre-pitch snapshot. So the pending set is **causally ordered and
  bounded**: `[terminal pitch] → [PA outcome] → [NPC feed …] → [next pre-pitch snapshot]`.

The sequencer's rule is therefore simple and robust: **drain the FIFO beat queues in order; treat the
pre-pitch snapshot (a latest-wins dirty flag) as the state to apply only once the beat queue is
empty** — at which point it enters `AwaitingInput` and enables the controls. Because the sim can only
ever have one outstanding snapshot (it blocks at the next human pitch), latest-wins never drops a beat
the player needed to see. This is the whole correctness argument for the presentation, and it needs no
sequence numbers or new locking — just ordered draining plus "apply snapshot last."

**Input gating is a real fix, not polish.** Today `AtBatView.Render` calls `SetIntentEnabled(true)`
every time a snapshot arrives (`AtBatView.cs:153`). With paced beats that re-enables input *during a
reveal* — and because the sim is already blocked on the *next* pitch's `AwaitIntent`, a stray click
would silently pre-answer the next pitch. 12d **decouples "snapshot arrived" from "controls enabled"**:
the controls enable only on entry to `AwaitingInput` (queue drained), never inside a reveal. The bridge
already ignores a submit when it isn't awaiting batter intent (`SubmitBatter` at
`InteractiveBatterPolicy.cs:284`), but the UI must not *offer* the input either.

---

## 4. The New Sim Seam (precise for the implementer)

All of §4 is the **seam sub-slice (12d-1)** and carries the bit-identity burden. It is the only
`Assets/Simulation/` touch in the whole phase.

**4.1 The report DTO** — a new `readonly struct PitchResult` (Baseball namespace, beside
`AtBatSnapshot` in `InteractiveBatterPolicy.cs`, the natural home for the bridge-facing DTOs), carrying
only facts the loop *already computed* this iteration — nothing re-derived, nothing new drawn:

- `PitchClass Class` — `Ball` / `Strike` / `Foul` / `InPlay` (the existing enum, `PitchChain.cs:6`).
- `PitchType Type` — the **true** thrown type (the reveal's honesty beat: the cue may have been blurred;
  now show what it actually was — "you sat fastball; it was a slider").
- `bool InZone` — the **true** drawn location (lets the reveal grade the player's zone read at zero
  extra sim cost — both `type` and `inZone` are already locals at `PitchChain.cs:619-624`).
- `bool BatterSwung` — distinguishes called vs swinging strike, whiff vs foul, take vs chase.
- `byte Balls, Strikes` — the count **after** this pitch (so the reveal owns the count tick).
- `bool PaEnded` — true on the terminal pitch (ball four / strike three / in-play).

Deliberately **excluded**: contact quality `q`, the discipline edge, the anchor probabilities. The
`PitchClass` already tells the outcome story; surfacing the continuous internals would leak calibration
the player can't act on and invite the UI to editorialize on sim math. The player's *own* timing τ and
read guess are already UI-side — the reveal can say "you were early" or "good read" from those alone
(§5), needing only `InZone` and `Class` from the sim to confirm right/wrong.

**4.2 The hook** — one method added to `IBatterPolicy` (`PitchChain.cs:265`), mirroring the existing
`OnPaResolved` exactly:

```csharp
/// <summary>Per-pitch observation hook (UI feedback). Consumes no RNG; a no-op off the interactive path.</summary>
void OnPitchResolved(in PitchResult result);
```

Implementor census (all must add it; all but one are trivial): `NeutralBatterPolicy`
(`PitchChain.cs:277`) and any harness scripted batter policy → **empty `readonly` no-op**;
`InteractiveBatterPolicy` (`InteractiveBatterPolicy.cs:311`) → `_bridge.PublishPitchResult(in result)`.
The generic constraint on `SimulatePa<TBatter, …>` devirtualizes the call, so the no-op truly costs
nothing on the hot path (no boxing — the same reason every policy member is explicit today).

**4.3 The emit site** — inside `SimulatePa` (`PitchChain.cs:704-727`), the class of each pitch is known
at the branch that classifies `draw`. The hook fires **once per pitch, after the count/`balls`/`strikes`
update, before the loop continues or returns**, with `PaEnded` set on the returning branches. The hard
rule from §2 governs: the call is *inserted between existing statements*; **no `rng.` call moves**. The
in-play branch computes its `PaOutcome` via `DrawBallInPlay` and returns — the terminal `PitchResult`
carries `Class = InPlay, PaEnded = true`; the *outcome itself* still travels the existing
`OnPaResolved` → `PublishPaResolved` path (`MicroGame.cs:880`), so the PA-result beat reuses that seam
verbatim and `PitchResult` never duplicates the outcome. (A called strike three emits
`Class = Strike, PaEnded = true`, then `OnPaResolved(Strikeout)` — two beats, "caught looking" then
"STRIKEOUT," which is the right broadcast cadence.)

**4.4 The bridge** — `PlayerIntentBridge` gains a third queue exactly like `_npcFeed`
(`InteractiveBatterPolicy.cs:89`): a `Queue<PitchResult> _pitchResults`, an internal
`PublishPitchResult(in PitchResult)` (sim side, under `_gate`), and a public
`bool TryDequeuePitchResult(out PitchResult)` (UI side). `Reset()` clears it alongside the others. This
is the non-hot-path clarity-over-allocation surface the class already documents (~a few pitches per PA).

**No `MicroGame` change is needed for batting** — the human batter's hook rides its policy, which
already holds the bridge; `MicroGame` only ever calls `batterPolicy.OnPaResolved` (unchanged). (When a
pitching UI eventually exists, `IPitcherPolicy` gets the symmetric hook the same way — flagged, not
built.)

---

## 5. The Presentation Spec (for the build slice, over the seam)

Everything here lives inside `AtBatView.tscn`/`.cs` (the `CenterSlot` occupant) and the dashboard's
existing bridge pump — no other scene changes. Verify node paths against the edited `.tscn`
(scene-mapper / raw read) before any `GetNode<T>()`, the standing rule. All player-facing copy lives on
`[Export]` templates (the `PitchCueFormat` / `OutcomeNamesCsv` precedent — zero new C# string literals).
Reuse the 10a theme's two accents — **diamond `#53C7A4`** for batter-positive beats, **dirt `#C96A3B`**
for batter-negative — no new palette.

**5.1 The `ResultReveal` element (the missing beat).** A new dedicated node — a centered `Label` on a
`Card`/chip `StyleBox`, hidden by default, layered above the scoreboard/diamond so it reads as the
focal point — driven by a Godot `Tween` (scale-up + fade-in on entry, fade-out on exit). It shows the
just-thrown pitch's call, colored and worded off `PitchResult`:

| `Class` + swing/read | Reveal copy (template) | Accent |
| :--- | :--- | :--- |
| Ball, took | "Ball — {location}" (+ "good eye" on a correct read) | diamond |
| Ball, swung (chase) | "Chased it — ball" / "swing, off the plate" | dirt |
| Strike, took | "Strike — caught looking" (+ "fooled you" on a wrong read) | dirt |
| Strike, swung | "Swing and a miss" (+ timing tag, §5.3) | dirt |
| Foul | "Fouled it back" / "fought it off" | neutral |
| InPlay | "In play…" (hands off to the PA reveal) | neutral |

`Type` feeds an optional flavor tag ("94 — fastball", using the arsenal velocity byte) — a small honest
"here's what it really was" line under the call, distinct from the pre-pitch `PitchCueLabel`, which
keeps showing the *blurred guess*. The narrative arc per pitch: **guess (cue) → truth (reveal) →
consequence (count tick)**.

**5.2 The count / bases / score tick as motion, not a silent relabel.** The scoreboard nodes already
exist (`CountLabel`, `Base1/2/3` panels with `BaseLit`/`BaseDim` theme variations, `AwayScore`/
`HomeScore`). 12d animates their *change* — a small pulse/flash on the count when it advances, a base
lighting via its variation when a runner reaches, a score bump — all cheap `Tween`s on existing nodes,
timed to land on the `PitchReveal` / `PaReveal` beat rather than jumping ahead of it. This is what makes
the count change *caused by* the pitch the player just watched.

**5.3 The timing slider earns its meaning.** The player's submitted τ ∈ [-1,+1] is already UI-side, so
the reveal can grade it with **no new sim surface**: bucket τ into Early / On-time / Late and pair it
with the `Class` — "you were early — swing and miss," "right on it — smoked" (on a good in-play),
"late — fouled back." Combined with the `InZone`-vs-guess read grade, this is the feedback loop that
teaches the zone-read minigame the sim has run headless since v4 but never *shown*.

**5.4 The `PaReveal` beat.** On the PA outcome (existing `PublishPaResolved` path), a bigger, longer
emphasis than a pitch reveal — "STRIKEOUT" / "WALK" / "SINGLE" / "HOME RUN!" — reusing `OutcomeNamesCsv`
and the diamond/dirt split (hit/walk/HR = diamond, out/K = dirt), with a stronger tween (bigger scale,
longer hold) so a home run *feels* different from a groundout. Runners/score finish advancing on this
beat.

**5.5 The play-log becomes a broadcast, not a debug print.** The existing `PlayLog` RichTextLabel keeps
the scrolling history, but each per-pitch and per-PA line now reads like a broadcast (colored via
`RichTextLabel` BBCode against the theme accents) so even the text log reads like a game. It stays the
persistent record behind the transient `ResultReveal`.

**5.6 Respect the player's time — a fast-forward affordance.** Broadcast pace is right for the savored
moment, but a player who wants speed must have it. Recommendation: a **hold-to-fast-forward / click-to-
pop-current-beat** affordance (collapse the current beat's tween to done, advance immediately) plus,
optionally, a persistent pace toggle defaulting to broadcast. This never touches the sim (the sim is
already ahead and blocked); it only compresses the UI's playback. All beat durations are `[Export]`
constants (recommended defaults: windup ~300 ms, pitch reveal ~600–800 ms, PA reveal ~1.2 s, NPC line
~250 ms) so pacing is tuned in the editor, not in code.

**5.7 The sequencer lives in `AtBatView`; the dashboard just forwards.** Per `ui_conventions` ("the
view renders DTOs handed to it"), the beat state machine (§3) is `AtBatView`'s own — it gains a light
`_Process`/timer that advances beats and gates input. `BaseballDashboard._Process` (`:342-358`) keeps
draining the bridge each frame but, instead of rendering immediately, **forwards** each dequeued event
into the view's queue: `TryDequeuePitchResult → EnqueuePitch`, `TryDequeuePaOutcome → EnqueuePaOutcome`
(existing), `TryDequeueNpcPa → EnqueueNpcPa` (existing), and hands the latest snapshot to the view as
its **pending pre-pitch state** (applied when the queue drains). The dashboard's ordering obligation is
only to forward in the order it dequeues — the causal bound from §3 does the rest.

---

## 6. Sequenced Sub-Slices (thin, one demoable step each)

| Slice | Deliverable | Owner |
| :---- | :---------- | :---- |
| **12d-1** | The sim seam: `PitchResult` DTO, `IBatterPolicy.OnPitchResolved` (+ all no-op impls), the `SimulatePa` emit site under the no-RNG-reorder invariant, the bridge queue. **Ends with `run_monte_carlo_batch` re-run: 266/266, no band moved, MLB bit-identity exact.** No visible change yet — the queue is drained-and-ignored until 12d-2. | build + **Fable authoritative harness run** |
| **12d-2** | The beat sequencer + input-gating fix + `ResultReveal` (pitch beat) + count/base/score tween. An attended game now shows every pitch's result at broadcast pace. | build |
| **12d-3** | `PaReveal` emphasis, the timing/read feedback tags (§5.3), broadcast play-log, and the fast-forward affordance. The full juice pass. | build |

12d-1 is load-bearing and lands first (it establishes the DTO the rest renders and clears the
harness gate before any UI depends on it). 12d-2 and 12d-3 are pure UI over a frozen seam.

---

## 7. Verification (the standing discipline, plus the sim-touch gate)

1. **The harness gate (new for this phase, mandatory).** `run_monte_carlo_batch`: **266/266, no band
   moved**, the MLB bit-identity regression guard exact (PA/H/ER byte-for-byte), the full 9a tier
   ladder inside its bands. This is the proof the §2 invariant held. A moved band is a **hard stop** —
   the seam is wrong, not the calibration; escalate to Fable per the standing rule. Extend the suite-7
   scripted-interactive-game check to assert the new hook fires **exactly once per pitch** and that the
   reported `Balls`/`Strikes` progression reconstructs the game's final count (a coherence check on the
   report, cheap, deterministic).
2. **Node paths verified before every `GetNode<T>()`** — scene-mapper / raw `.tscn` read against the
   edited `AtBatView.tscn` (review-blocking if skipped).
3. **Live windowed boot + screenshot** (the 12b/12c `PrintWindow`/`PW_RENDERFULLCONTENT` technique,
   process-targeted by name) with the at-bat view active — zero debug-output errors, every new node
   path resolves.
4. **No schema change — asserted:** `SchemaValidator` green, `user_version` stays 10.
5. **The sim diff is *exactly* the seam:** `git diff --stat -- Assets/Simulation` shows only
   `PitchChain.cs` + `InteractiveBatterPolicy.cs` (DTO/hook/bridge) — no `MicroGame`/resolver/weight
   file, confirming the batting-only, no-math-touch scope.
6. **Manual playtest is the real gate (human sign-off).** Motion, pacing, and input-gating are only
   truly verifiable by playing an attended game — `AdvanceDay` is UI-button-only and no headless path
   drives a live, animated pitch. Confirm: every pitch reveals its result; the count never jumps ahead
   of the reveal; input is dead during a reveal and live at the next cue; a home run feels bigger than
   a groundout; fast-forward compresses cleanly; a full PA (BB, K, and in-play) each reads correctly.

---

## 8. Disclosed Simplifications & Open Questions

- **Batter-only.** No pitching presentation — no pitching UI exists (the `InteractivePitcherPolicy` is
  engine-only). The seam is deliberately shaped to extend symmetrically: when a pitching UI lands,
  `IPitcherPolicy` gets the same `OnPitchResolved` hook and the same bridge queue. Flagged, not built.
- **Typographic + motion, no character art.** The presentation is reveals, tweens, and a broadcast log
  against the existing theme — not animated sprites. That is the deliberate art-budget scope (portraits
  are still procedural fallbacks); a richer visual at-bat is a later, asset-gated phase, not 12d.
- **Sound is 12e, and 12d builds its seam.** Every beat transition (windup, pitch reveal by class, PA
  reveal by outcome) is a natural SFX trigger point; 12e hangs audio on the same state machine with no
  restructure. Called out so 12e inherits it.
- **Fast-forward defaults to broadcast pace.** The pace/skip affordance is a first-pass call (§5.6);
  whether it's hold-to-ff, click-to-pop, a persistent toggle, or all three is a build-time feel
  decision, all beat durations `[Export]`-tunable either way.
- **The report surfaces facts, not internals.** `PitchResult` carries `Class`/`Type`/`InZone`/count,
  not contact-quality `q` or the anchor split — a deliberate line (§4.1). If a playtest wants richer
  "barrel it" feedback than the class + the player's own τ can express, adding `q` is a one-field seam
  extension behind the same harness gate, flagged not pre-built.

---

## 9. Cross-References

- `docs/design/baseball_markov_micro_sim.md` (the §5 count chain / §6 input model / §7 consistency
  identity this presents; `PitchChain.SimulatePa` is its implementation).
- `docs/design/surface_the_sim.md` (the sibling 12c step; §8 defers 12d to this doc; the `CenterSlot`
  dual-occupant lifecycle the at-bat view lives in).
- `.claude/rules/ui_conventions.md` (read-only-over-sim, view-renders-DTOs, no per-frame LINQ/string-
  format, pooled/tweened elements, thin vertical slices).
- `.claude/rules/baseball_engine.md` (the micro-sim Markov contract the seam must not perturb).
- `Assets/Simulation/Baseball/PitchChain.cs:595` (`SimulatePa`, the per-pitch loop + the discard the
  seam recovers) and `:265` (`IBatterPolicy`, the hook's home).
- `Assets/Simulation/Baseball/InteractiveBatterPolicy.cs` (`PlayerIntentBridge` + `NpcPaFeedEvent`, the
  FeedSink observer precedent the pitch-result queue mirrors verbatim).
- `Assets/UI/AtBatView.tscn` + `.cs` (the scene the presentation is built into) and
  `Assets/UI/BaseballDashboard.cs:308-365` (the bridge pump that forwards beats to it).

---

## 10. Recommended Model Split (user decides; recorded per the model-assignment workflow)

The seam and the presentation are cleanly separable, and the seam is the delicate part:

- **12d-1 (the sim seam) → the harness-bearing owner.** It edits the hottest calibrated method and its
  correctness is a "re-run the batch, prove nothing moved" claim. It is squarely the `FeedSink`
  observational pattern (which Sonnet has built before), so **Sonnet 5 can author it**, but the
  **authoritative `run_monte_carlo_batch` sign-off is Fable 5's** (sim-assembly touch, the 12c-normalize-
  seam precedent) and any unexpected band move escalates to Fable outright.
- **12d-2 / 12d-3 (the presentation) → Sonnet 5.** Pure UI volume over a frozen DTO — the beat
  sequencer, reveals, tweens, feedback copy, fast-forward. No sim risk once 12d-1 is green.
- **Review → Fable 5**, per the standing Phase-12 UI-slice split.

Net: **Opus (this doc) owns the seam architecture + bit-identity contract; Sonnet 5 builds seam-first
then presentation; Fable 5 owns the authoritative harness run and review.** This matches the user's
stated "Opus design → Sonnet builds it" while protecting the calibrated core with the harness gate.
