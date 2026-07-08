# At-Bat "Read the Pitch" Input Model

**Status:** DESIGN (Opus 4.8, 2026-07-07). No code. Supersedes the v4 Swing/Take
batter input inside `PitchChain.SimulatePa` for the *human* path only.

**Origin:** playtest (2026-07-07). The v4 model let the player click **Swing**
or **Take** with a binary zone-read toggle and a timing slider. Two problems
surfaced once 12d made per-pitch results visible:

1. **Swing/Take barely mattered.** The pitch *class* (Ball/Strike/Foul/InPlay)
   is drawn from the location-conditioned mixture **independent of whether the
   batter swung** — a `Take` can resolve as `PitchClass.InPlay`. Swing vs take
   only differed by whether `ContactQuality` was nonzero. Root cause confirmed
   in `PitchChain.SimulatePa:711-748`: `swung` is recorded into the
   `PitchResult` DTO but never gates the class draw.
2. **The read toggle and timing slider felt inert.** Their only feedback lived
   inside the reveal text, which vanished too fast to read (a pacing bug fixed
   separately). The mechanics worked but were invisible and low-impact.

**The user's direction (design decisions, answered 2026-07-07):**

- **Control set:** the player no longer clicks Swing/Take. Each pitch they submit
  a **guessed pitch type**, a **guessed location** (a zone grid), and set a
  per-pitch **approach dial** (patient ↔ aggressive). *The swing itself becomes
  emergent* — the avatar's ratings decide whether it offers.
- **Location grain:** a **3×3 zone grid** (plus an out-of-zone "expect a ball"
  read), not the v4 binary in/out.
- **Read weight:** **reads are decisive** — a correct type+location read plays
  like a great swing decision; a wrong read plays like getting fooled. Ratings
  (plate discipline, contact) set the baseline and the ceiling, the read moves
  the outcome hard between them.

---

## 1. The load-bearing safety seam (read this first)

`PitchChain.SimulatePa` is used **only by the attended micro-game**
(`MicroGame.cs:877`). The macro league sim uses `AtBatResolver.Resolve`, which
this design **never touches** — so the **MLB bit-identity guard (PA 48384 / H
10969 / ER 5237) cannot move by construction**; it exercises the macro path.

What *does* constrain `SimulatePa`:

- **Suite 5** — the count-chain absorption pinned analytically to
  `AtBatResolver.ComputeProbabilities` (`SolveNeutral`/`ComputeAbsorption`).
- **Suite 6** — 2000 **neutral-policy** attended games landing inside the §8
  bands, plus the fatigue/PED/determinism/zero-alloc micro checks.

Both run the **neutral batter policy** (`NeutralBatterPolicy` → `BatterIntent.Neutral`
→ `input = default`). Therefore the entire calibration surface is protected by
one rule:

> **INVARIANT N (the calibration contract):** the `Kind == Neutral` branch of
> `SimulatePa` keeps today's exact code and today's exact RNG sequence —
> byte-for-byte. Every new mechanic in this design lives on the `Kind == Read`
> (human) branch, which no calibration suite bands.

The human path is graded only by **suite 7** (the scripted-interactive-game
coherence check), which asserts *self-consistency* (count reconstructs, one hook
per pitch, no illegal class), never a batting-average band — because there is no
"correct" human batting line; it depends on player skill. This is the same
posture 12d-1 shipped under, one grain deeper.

**Consequence:** adding RNG draws on the human branch is allowed (it is off the
calibrated anchor already). Removing/reordering a draw on the *neutral* branch is
a hard stop.

---

## 2. What the player does now (the interaction)

Per pitch, the pre-pitch **look** is unchanged from v4 (`PitchLook`: a blurred
type cue + the scouting zone probability). The player answers with:

| Input | Control | Feeds |
|---|---|---|
| **Guessed type** | 3 buttons (FB / Breaking / Offspeed) | type-read correctness |
| **Guessed location** | 3×3 grid cell, or an "expect a ball" (out-of-zone) read | location-read accuracy |
| **Approach** | patience↔aggression slider, τ_appr ∈ [−1,+1], default 0 | swing-threshold bias |

There is **no Swing button and no Take button**. After the player submits, the
avatar decides — from the read quality and its ratings — whether it offers, and
if it does, how well it connects. The reveal (12d machinery) then narrates what
actually happened: *"You sat breaking ball away — he came with the heater; you
were fooled, swing and miss"* / *"Read it perfectly — smoked it."*

Timing (the v4 slider) is **removed**. Contact quality now comes from read
accuracy × the Contact rating, not a timing dial.

---

## 3. The sim model

### 3.1 A location grid the read can be graded against (`SimulatePa`, human branch only)

Today the sim draws one bit, `inZone` (`PitchChain.cs:631`), and that is the only
location fact. Grading a 3×3 read needs an actual **cell**.

- **Keep** the `inZone` draw exactly as today, on **both** branches — it is the
  only location input the neutral mixture consumes, so calibration is untouched
  (Invariant N).
- **Only on the human branch**, after `inZone` is known, draw a **sub-cell**:
  - `inZone` → one of the 9 strike-zone cells (`c ∈ 0..8`), center-weighted by
    the pitcher's control (a wilder pitcher spreads; a sharp one paints — v1 may
    ship uniform and refine later).
  - `!inZone` → an out-of-zone "chase region" tag (one of 8 edge directions, or a
    single coarse OUT for v1).

  This sub-draw is **new RNG, appended on the human branch only** — the neutral
  branch never draws it, so its sequence is byte-identical (Invariant N holds).

- **Location-read accuracy** `locAcc ∈ [0,1]`:
  - The coarse call first: did the player's in/out read match `inZone`? A wrong
    in/out call is the worst outcome (`locAcc` near 0).
  - If the in/out call was right *and* in-zone, refine by Chebyshev distance from
    the guessed cell to the true cell: exact = 1.0, adjacent ≈ 0.6, two away ≈
    0.3 (constants tunable).

- **Type-read correctness** `typeOk ∈ {0,1}`: guessed type == true `type`.

### 3.2 Read → belief → swing decision (new `PlayerInputModel` logic)

The avatar forms a **belief** about whether the pitch is a hittable strike, then
decides to offer. All constants are first-pass, `HatchHarness`-tunable.

1. **Read quality** `R ∈ [0,1]` blends the two reads, discipline-weighted:
   `R = wType·typeOk + wLoc·locAcc`, then sharpened by plate discipline:
   a high-Discipline avatar converts a good read into belief more reliably and is
   punished less by a marginal one. (Discipline is the "how much the read is worth
   to *this* hitter" scaler — the user's "let plate discipline determine whether
   he swings at a strike or ball.")

2. **Believed-strike** `bStrike ∈ [0,1]`: if the read is accurate the belief
   tracks the truth (`inZone`); as `R → 0` the belief decays toward the scouting
   prior (`PitchLook.ZoneProbability`) plus noise. A **wrong** read actively
   inverts belief (reads decisive: you're fooled, you think the chase is a
   strike).

3. **Swing probability** `pSwing = clamp( base(count) + AggBias·τ_appr +
   SwingReadGain·(bStrike − swingThreshold(Discipline)) )`. Aggressive approach
   raises it (more offers at everything); patient lowers it (more takes → more
   walks *and* more called strikes). One RNG draw decides swing vs take.

   The avatar therefore **swings at balls when fooled** (believed a chase was a
   strike) and **takes strikes when it misreads them as balls** — exactly the
   "reads decisive" feel, bounded by discipline.

### 3.3 Swing-gated class draw (the fix for "Take can be In-Play")

This is the structural change the playtest demanded. On the human branch the
class is gated on the swing decision:

- **Take** → the pitch is only **Ball** or **called Strike**, decided by the true
  `inZone` (in-zone taken pitch = called strike; out-of-zone = ball). Never Foul,
  never InPlay. *(Optional v1.1 umpire-noise on the edges; omit for v1.)*
- **Swing** → the pitch is **whiff (swinging Strike)**, **Foul**, or **InPlay** —
  never Ball. Contact probability `pContact = f(R, Contact rating)`; a whiff on
  miss. On contact, the existing two-strike foul rule (`FoulShareOfStrikes`)
  splits Foul vs InPlay. InPlay routes to the **unchanged** `DrawBallInPlay` with
  the contact-quality `q` below.

The count math (`++balls == BallStates` walk, `strikes++`, two-strike
strikeout/foul) is reused verbatim; only *which classes are reachable* is gated
by swing. The `PitchResult` DTO already carries `BatterSwung` — it now genuinely
predicts the reachable class set, so 12d's reveal copy (chase, called strike,
swing-and-miss, foul) all become correct-by-construction rather than best-effort.

### 3.4 Contact quality from the read (replaces timing)

On a swing that makes contact, `q = ContactBase + ContactReadGain·(2R − 1)`,
then scaled toward the ceiling by the Contact rating, clamped [−1,+1]. Fed to the
**unchanged** `DrawBallInPlay` (§5.3 BIP split) exactly where the v4 timing-derived
`ContactQuality` was fed. A perfect read + high Contact = peak `q` (XBH/HR mass);
a fooled swing that still nicks it = negative `q` (weak grounder / can-of-corn).
`Power` continues to enter through the anchor (`AtBatResolver`), untouched.

### 3.5 The approach dial

`τ_appr ∈ [−1,+1]` is the only *raw magnitude* input left. It shifts `pSwing`
(§3.2) and nothing else — the per-pitch agency lever the user asked for, replacing
the raw swing button. Patience genuinely trades called strikes for walks;
aggression trades chases for damage. Default 0 = the avatar's rating-driven
baseline approach.

---

## 4. Interfaces that change

- **`BatterIntent`** (`PitchChain.cs:172`): `Kind` collapses to `{ Neutral, Read }`
  (Swing/Take/Timing/GuessInZone removed). New fields: `GuessType` (PitchType),
  `GuessCell` (byte, 0..8, or a sentinel for "expect a ball"), `Approach`
  (double). `Neutral` stays `default` → the neutral branch is bit-identical.
- **`PitchMatchup`** (`PitchChain.cs:122`): gains `BatterContact` (byte) beside
  `BatterDiscipline` — the human branch needs Contact for §3.4. Additive; the
  neutral branch ignores it.
- **`AtBatSnapshot`** / **`PitchResult`** (`InteractiveBatterPolicy.cs`): the
  snapshot is unchanged (the look is still the input). `PitchResult` **gains the
  read grade** the reveal wants to narrate — `TypeOk`, `LocAcc` (or a bucketed
  read-grade enum), the believed-vs-true type/zone — so 12d's reveal can say
  *why* it went well/badly. **Additive only; the frozen 12d-1 fields keep their
  meaning** (`BatterSwung` now truly gates the class, which only makes the
  existing contract tighter, not different).
- **`InteractiveBatterPolicy.NextPitch`**: returns a `Read` intent built from the
  bridge's new submission (type + cell + approach) instead of swing/take.
- **`PlayerIntentBridge`**: `SubmitSwing`/`SubmitTake` → `SubmitRead(PitchType
  guessType, byte guessCell, double approach)`. One await, one release, same
  threading contract.
- **`NeutralBatterPolicy`** and every harness policy: unchanged (they return
  `Neutral`). Census is the same two `IBatterPolicy` implementors 12d-1 pinned.

---

## 5. UI (the `Assets/UI/` half)

- **Control bar** (`AtBatView.tscn`): the Swing/Take buttons + timing slider are
  replaced by (a) three type buttons, (b) a 3×3 grid of location cells plus an
  "expect a ball" toggle, (c) the approach slider (relabelled Patient↔Aggressive),
  with a live readout (the timing-label pattern just added). One **Commit**
  action submits the read (a button, or auto-commit when all three are set).
- **Reveal** (12d `ResultReveal`): now narrates the read — believed vs true type,
  believed vs true zone, and the graded result — using the new `PitchResult`
  fields. The diamond/dirt accent and the beat sequencer are unchanged.
- **Input gating** (12d §3): unchanged — controls enable only on entry to
  AwaitingInput; the read is submitted once per pitch.
- No schema change (`user_version` stays 10). No `Assets/Simulation` ↔ `Assets/UI`
  boundary change — the read still flows UI→bridge→policy, results flow back
  through the same three queues.

---

## 6. Sub-slices

- **A — sim seam (harness-gated, the sanctioned `Assets/Simulation` touch).**
  New `BatterIntent`/`PitchMatchup` shape, the location sub-cell draw, the
  read→belief→swing model in `PlayerInputModel`, the swing-gated human class
  branch in `SimulatePa`, the additive `PitchResult` read-grade fields, the
  bridge `SubmitRead`. Ends with the authoritative `run_monte_carlo_batch`:
  **Invariant N proven** (neutral branch byte-identical: suite 5 analytic pin
  exact, suite 6 bands unmoved, MLB bit-identity untouched since macro is
  untouched), plus **new suite-7 human-branch coherence checks** (a take never
  yields Foul/InPlay; a swing never yields Ball; the read-grade fields reconstruct
  from the scripted reads; swing rate moves monotonically with the approach dial).
- **B — UI.** The new control bar + the reveal narration + wiring. Pure
  `Assets/UI/`, `git diff --stat -- Assets/Simulation` empty. Build + Godot boot;
  the manual playtest is the real gate.

Optional **A0 design-review** checkpoint: because "reads decisive" is a big feel
swing, a 20k-run `HatchHarness`-style sweep of a *scripted* read policy (perfect
reader vs random reader vs fooled reader) to confirm the spread is dramatic but
not degenerate (a perfect reader shouldn't hit 1.000; a blind one shouldn't hit
.000) — before the UI is built on top.

---

## 7. Verification

1. **`run_monte_carlo_batch` (mandatory, sub-slice A):** suites 5 & 6 **unchanged**
   (Invariant N); the MLB bit-identity guard **unchanged** (macro untouched); the
   tier ladder unchanged; zero-alloc on the neutral path unchanged. A moved band
   on the neutral path = Invariant N violated = the change is wrong.
2. **New suite-7 coherence checks (sub-slice A):** the legality constraints above
   + read-grade reconstruction + the approach-dial monotonicity, over the scripted
   UI thread. Human branch may allocate (not zero-GC constrained; ~5 human PAs/game)
   but should be checked for *no* allocation leaking onto the neutral/NPC PAs in the
   same game.
3. **`dotnet build` 0/0**, **`git diff --stat -- Assets/Simulation` shows only
   `PitchChain.cs` + `InteractiveBatterPolicy.cs` (+ `PlayerInputModel` if split
   out)** for A, and **empty** for B.
4. **Manual playtest (the real gate):** with a high-Discipline/high-Contact avatar
   vs a low one, confirm reads visibly matter (a perfect read crushes it; a wrong
   read chases/whiffs); the approach dial visibly trades walks for damage; a Take
   is never reported In-Play; the reveal narrates the read correctly.

---

## 8. Model split (recommendation; user decides)

- **Design (this doc):** Opus 4.8 — done.
- **Sub-slice A (sim seam):** **Sonnet 5 authors** (the 12d-1 FeedSink/seam
  precedent), **Fable 5 owns the authoritative `run_monte_carlo_batch` sign-off**
  (sim-assembly touch — the 12d-1 rule: a moved neutral-path band escalates to
  Fable outright). Consider the A0 sweep with Fable before A lands.
- **Sub-slice B (UI):** **Sonnet 5** builds, **Fable 5** reviews (the 12d-2/12d-3
  precedent).

This matches the standing "Opus designs → Sonnet builds → Fable guards the
calibrated core" split.

---

## 9. Open risks & tuning knobs

- **Balance of "reads decisive."** The single biggest feel risk. Mitigated by the
  A0 sweep and by keeping discipline/contact as the baseline/ceiling so a weak
  hitter with perfect reads still isn't an All-Star. Re-band after the first
  playtest; every §3 constant is `HatchHarness`-tunable.
- **Sub-cell distribution.** v1 may ship uniform-within-region and refine to a
  control-weighted paint model later — the read grade is the only consumer, so this
  is a pure feel refinement, not a calibration lever.
- **Whiff/foul rate on the human swing.** The human branch now owns its own
  contact/whiff split; watch pitches/PA on the scripted game (suite 7 liveness)
  so a human PA doesn't run absurdly long or short. Not calibration-banded, but a
  feel + pacing check.
- **Approach vs count.** The `base(count)` term should encode real baseball
  (protect with two strikes, sit on a cookie 2-0) — a place a playtest will tune.
- **Pitcher-side symmetry.** Untouched; the `IPitcherPolicy` read-model twin is a
  later concern (no pitching UI exists yet), same as 12d.
