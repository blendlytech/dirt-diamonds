# Pitching Mode: "Work the Book"

**Status:** DESIGN **rev 2** (G-2). Feasibility review done ([G-2_pitching_mode_review.md](../review/G-2_pitching_mode_review.md)); its blockers B1–B3 and corrections C1–C4 are applied here. Next: Gemini 3.1 Pro Math curve review, then owner acceptance.
**Author:** Claude Fable 5.1, 2026-10-10 (rev 1, `6ba9055`), from [G-2_pitching_context_pack.md](../tasks/G-2_pitching_context_pack.md) (code facts as of `f983692`). **Rev 2:** Claude Opus 5.5, 2026-10-10. Fable's API credit ran out before its revision pass, so the integration lead applied the review in place (§3.5 is new; §3.3, §4.4, §4.6, §5, §7.2, §8, §9.1, §9.3, §10 and §12 are edited). The design and its decisions are Fable's.
**Scope:** the player-facing pitching mode for attended games when the avatar pitches. Self-contained: an engineer can implement from this file and Pro Math can tune from the constants table (§8) and the harness (§7).
**Related:** [gameplay_core.md](gameplay_core.md) (Phase G), the shipped "Read the Pitch" batting model (pack §5).

---

## 0. Summary and the five decisions

The player pitches with **three inputs per pitch**, mirroring batting's three: a **pitch** from the arsenal, a **target cell** on a 17-cell map (9 zone cells plus a ring of 8 chase cells), and an **effort** dial (−1 finesse … +1 max effort). The pitch **scatters** from the target by an amount set by control, effort, movement and fatigue, and lands in an executed cell. The opposing NPC batter keeps a **book** on the pitcher (tendencies by count, by previous pitch, by location) and leans on what the book says. Each pitch then resolves on a new **Model** batter branch that takes the neutral chain's own location-conditioned class rates and **tilts** them, resolver-style, by how well the batter anticipated the pitch, by location quality, by stuff, velocity and movement, and by sequencing. In-play balls use the shipped `DrawBallInPlay(p*, q)`.

The five decisions that shape everything else:

1. **Tilt, don't re-model.** The Model branch starts from the exact neutral rates and multiplies them by `exp(x)` terms that are zero at the reference. A pitcher who throws the reference distribution lands on the ratings line *by construction*; skill is a bounded gain budget. This is the same idiom as `AtBatResolver`, so Pro Math tunes it with the same intuitions. (Batting's emergent swing model cannot be centered this way, which is why its band is 1.8 OPS wide; §11.)
2. **A new branch, not the Read branch.** `BatterIntentKind.Model` is a sibling of `Read`. The human-batting Read path and the Neutral path are untouched; Invariant N holds by construction (§5.6).
3. **The book is the only intelligence.** The batter never sees the current pitch's inputs. It leans on Dirichlet-smoothed, decaying tendency tables whose prior strength and decay cap how sharp it can get. Those caps *are* the anti-omniscience bounds (§4.5).
4. **"Always challenge" dies because known strikes are hittable.** When the book says "it's in the zone", called strikes evaporate and balls in play go up. The chain's walk savings from pounding the zone no longer pay for themselves (§4.6).
5. **Band ≈ the 40-vs-70 gap, ±.100 OPS around ratings**, with an average player landing near ratings. This meets gameplay_core §1.4 and is a deliberate departure from batting as shipped; batting should be re-banded with the same idiom (§11.1).

Pitcher attributes, each felt per pitch: **stuff** → whiff tilt (through p* plus effort); **control** → scatter; **stamina** → capacity, with effort spending it faster; **velocity** → fastball whiff plus the velocity-differential timing term; **movement** → chase and weak contact (plus a touch more scatter); **usage** → the batter's prior.

---

## 1. Inputs: what the player submits per pitch

| Input | Domain | Why it exists |
| :-- | :-- | :-- |
| `Type` | `PitchType` from the arsenal (Fastball / Breaking / Offspeed as shipped) | Mixing types is the first thing the book reads, and velocity/movement live on the type. |
| `TargetCell` | 0..16 (§2.1): 0..8 zone 3×3, 9..16 chase ring | Location is the second thing the book reads; corners vs heart vs chase is the execution skill, and control decides whether you get what you aimed at. |
| `Effort` | `double` ∈ [−1, +1], UI snaps to {−1, −0.5, 0, +0.5, +1} | The one trade-off dial: +effort buys stuff and pays in scatter and fatigue; −effort tightens location and saves the tank. Mirrors batting's `approach`. |

No fourth input. Sequencing (changing speeds, tunneling) and reading the book are *skills the player exercises through these three*, not extra controls. Pickoffs, holds, pitch-outs and intentional walks are out of scope for G-2 (owner Q4).

**Defaults when the player does nothing** (auto-pitch / timeout): the UI submits the `Reference` behaviour (§7.2), which lands on ratings. Doing nothing never punishes.

---

## 2. Execution: where the pitch lands

### 2.1 The target map

Coordinates are in cell widths, origin at the center of the zone. The zone is the square `[−1.5, 1.5]²`.

```
Zone cells (0..8), row-major from the batter's top-left:  centers (cx, cy) ∈ {−1, 0, 1}²
  0 1 2
  3 4 5        cell 4 = Heart; 1,3,5,7 = Side; 0,2,6,8 = Corner
  6 7 8
Chase ring (9..16):  depth 1.5..2.5 off the zone
  9  = up        (|x| ≤ 1.5, 1.5 < y ≤ 2.5)       13 = up-in corner      (x,y both in the ring, inside half)
  10 = down                                       14 = up-away corner
  11 = in  (toward the batter)                    15 = down-in corner
  12 = away                                       16 = down-away corner
Waste (17, not targetable): any axis beyond 2.5. Inside-waste = x beyond 2.5 on the batter's side.
```

"In" and "away" are resolved by batter handedness in the presentation layer only; the sim works in a canonical frame where positive x is "in". Handedness never feeds a rating.

Cell quantization: `inZone = |x| ≤ 1.5 && |y| ≤ 1.5`; zone cell = `3·row + col` with `row = clamp(floor(y + 1.5), 0, 2)` flipped so row 0 is up; ring cells by sign tests; waste if either `|x| > 2.5` or `|y| > 2.5`.

### 2.2 Scatter

Per axis, the executed point is the target cell center plus a **triangular** miss (bounded, two uniform draws per axis, no Box–Muller):

```
M      = clamp(ScatterMaxAt50 − ScatterControlGain·dCtl, ScatterMin, ScatterMax)
         · (1 + ScatterEffortGain·effort)
         · (1 + ScatterMovementGain·mRel)
         · (1 + PressureScatterGain·pressure)        // pressure = 0 at launch; Active_Effects hook (§9.7)
missX  = M·(u1 + u2 − 1),   missY = M·(u3 + u4 − 1)   // u ~ NextDouble(), std = M/√6
dCtl   = (control_eff − 50)/50   (control_eff already carries tier, PED, fatigue; fixed at PA start)
mRel   = (movement(type) − movementMean)/50, movementMean = usage-weighted arsenal mean
```

With first-pass constants (control 50, effort 0): `M = 1.10`. The in-zone probability of each target is then:

| Target | P(in zone) | P(waste) |
| :-- | :-- | :-- |
| Heart (4) | 1.00 | 0 |
| Side (1,3,5,7) | 0.85 | 0 |
| Corner (0,2,6,8) | 0.72 | 0 |
| Ring side (9..12) | 0.15 | 0.15 |
| Ring corner (13..16) | 0.02 | 0.28 |

A corner target at control 50 lands in the zone 72% of the time, which matches the shipped binary `acc = 0.75`. At control 80, `M = 0.77`: corners 88%, ring sides 6% in zone and 6% waste. At control 20 (or a fatigued 50): `M = 1.43`: corners 62%, ring sides 21% in and 21% waste. (Tail mass beyond 0.5 cells is `(M − 0.5)²/(2M²)`; rev 2 corrected these two numbers, C3.) Waste pitches are where wild pitches and hit batters come from (§9.1), so poor control *and* chasing the ring with a tired arm are both felt.

**Max-effort cost:** `+1` effort widens `M` by 20% and costs 1.4 pitches of fatigue (§9.3); `−1` tightens it 20% and costs 0.6.

### 2.3 What the chain sees

The executed cell gives `inZone` directly; **no `zDraw`/`zRef` location draw happens on this path.** The chain still computes `zRef = ZoneProbability(count, control, type)` because the location-conditioned neutral rates (pack §4.3) are defined around it; those rates are reused verbatim as the **untilted** rates of the Model branch (§3.3). `trueCell` on this path is the executed cell (0..17), not a uniform draw.

---

## 3. Pitch quality and the Model branch

### 3.1 Per-pitch terms (centered to zero mean at the reference, §3.5)

| Term | Definition | Range |
| :-- | :-- | :-- |
| `e` | effort | [−1, 1] |
| `sPitch` | `EffortStuffGain·e / 50` (stuff *deviation from the pitcher's own effective stuff*; the rating itself is already in p*) | ±0.12 |
| `vRel⁺` | `max(0, (velocity(type) − velocityMean)/50)` · (Fastball 1.0, else `VeloOffTypeWeight`) | [0, 1] |
| `t` | timing disruption: `clamp(|velocity(type) − velocityPrev| / VeloDiffScale, 0, 1)`; `velocityPrev` = previous pitch of this PA, or `velocityMean` on the first pitch | [0, 1] |
| `mRel` | `(movement(type) − movementMean)/50` | ±1 |
| `loc` | executed-cell grade: Heart / Side / Corner / Chase / Waste | enum |
| `aZ, aT, aC` | anticipation terms from the book (§4.4) | [−1, 1] |

**Why relative velocity and movement:** `stuff` is the rating that already encodes "my pitches are hard to hit", through p*. If velocity and movement also acted in absolute terms, a 70-velocity arsenal would be double-counted in attended games and the pitcher's attended line would drift off the macro line. Centering on the usage-weighted arsenal mean makes them *within-pitcher* levers: the fastball earns whiffs because it is faster than *your* other pitches; the changeup earns `t` because it is slower than the pitch before it; the slider earns chases because it moves more than *your* average. An absolute arsenal effect is left as an owner knob, `ArsenalAbsoluteGain`, default 0 (owner Q5).

### 3.2 The tilts

Three class exponents and one in-play quality, each a linear sum with named gains (§8):

```
xBall  (out-of-zone pitches only; 0 in zone) =
         + ZoneKnownBall·aZ                    // batter knew it was a ball → lays off; knew "strike" and got a ball → chases
         − MovementChaseGain·mRel − TimingChaseGain·t
         − ChaseCellBias(loc)                  // ring corners are chased less than ring sides
xStrike = − ZoneKnownWhiff·aZ·[inZone] − TypeKnownWhiff·aT − CellKnownWhiff·aC
         + EffortWhiffGain·e + VeloWhiffGain·vRel⁺ + TimingWhiffGain·t + MovementWhiffGain·mRel
         + LocWhiff(loc)
xPlay   = + ZoneKnownPlay·aZ·[inZone] + TypeKnownPlay·aT + CellKnownPlay·aC
         + LocPlay(loc)
q       = clamp( TypeKnownQ·aT + CellKnownQ·aC + ZoneKnownQ·aZ·[inZone]
                 − MovementQualityGain·mRel − TimingQualityGain·t
                 + LocQ(loc),  QClampLo, QClampHi )
```

Each exponent is clamped (`BallClamp`, `WhiffClamp`, `PlayClamp`) before use. The clamps and `QClamp` are the **owner caps**: they bound what any pitch can do, so skill cannot make a 30 elite or ruin an 85 (§7.4 gate G8).

Location grades:

| `loc` | `LocQ` | `LocWhiff` | `LocPlay` | Meaning |
| :-- | :-- | :-- | :-- | :-- |
| Heart | +0.20 | −0.15 | +0.10 | a meatball: hit hard, rarely missed |
| Side | 0 | 0 | 0 | the reference zone pitch |
| Corner | −0.10 | +0.05 | −0.05 | painted |
| Chase | −0.25 | +0.10 | −0.10 | swung at only when chased; weak contact |
| Waste | Ball (§3.3 mixture, §9.1) | | | never swung at |

### 3.3 Resolution (one pitch)

```
(ball, strike, inPlay) = neutral location-conditioned rates for this count/zRef/inZone   // exactly pack §4.3, s = 0.55
if !inZone:                                    // waste-preserving mixture (rev 2, B2)
  w        = WasteRef[count][type]             // E_ref[P(waste | out-of-zone, count, type)], §3.5
  ballChase = max(0, (ball − w)/(1 − w))       // so w·1 + (1 − w)·ballChase = ball (the neutral out-of-zone rate)
  k'        = (1 − ballChase)/(1 − ball);  strike *= k';  inPlay *= k';  ball = ballChase
if loc == Waste: class = Ball (then §9.1 HBP draw)
else:
  wB = ball·exp(xBall);  wS = strike·exp(xStrike);  wP = inPlay·exp(xPlay);  normalize     // x's are the centered sums, §3.5
  class = draw(wB, wS, wP)                      // 1 NextDouble
  Ball   → ball
  Strike → at two strikes: foul with FoulShareOfStrikes (0.68, shipped) self-loops; else strike   // same draw as the neutral branch
  InPlay → DrawBallInPlay(p*, q)                // shipped draws
flavor = presentation-only sub-result draw (§3.4)  // 1 NextDouble, never affects the class
```

**Why the mixture:** a Waste pitch is a certain ball, so treating it as an extra ball on top of the neutral out-of-zone rate would inflate the Reference walk rate (at 0-0, zRef .51: P(ball | out) .486 → .537 / .588 / .640 at 10 / 20 / 30% waste share). Instead the reference's own waste share is carved *out of* the neutral ball rate. A pitcher who throws more waste than the reference pays in walks; one who throws less earns chases. If `w > ball`, `ballChase` clamps at 0 and the Reference line drifts toward more walks in that cell. That needs low control in a pitcher-ahead count (estimated: control_eff ≲ 25 at two strikes, under 2% of Reference pitches); suite 8 reports the clamp rate.

**What G1 claims (restated, rev 2):** with every term centered (§3.5), the waste mixture, and the Reference bot's executed in-zone rate equal to `zRef(count, type)` exactly (§7.2), the Reference bot reproduces the neutral chain's per-(count, in/out) class marginals and therefore p* **to first order**. Two second-order residuals remain, both bounded and expected well inside G1's ±.020: (1) Jensen's gap, since centered exponents still have `E[exp(x)] ≈ 1 + Var(x)/2` (first-pass σ(x) ≈ 0.15 gives about 1% relative weight inflation, mostly cancelled by renormalization), and the same convexity in `DrawBallInPlay` over centered `q`; (2) anticipation products such as `aZ·[inZone]`, which are zero-mean given the lean but correlate weakly with type through `typeShift`. Expected total: ≤ .008 opp OPS. G-2a runs G1 with no book (anticipation ≡ 0), so it isolates (1); G-2b re-runs it with the book.

### 3.4 Swing flavor (presentation-only)

The class decides outcomes; the **flavor** tells the story and is drawn after the class so it can never change it:

```
Ball    → Take                         (Chase-Take if out of zone and the book had aZ > 0.3: "laid off")
Strike  in zone     → Swinging with pSwingMiss = clamp(FlavorSwingBase − 0.35·aZ + 0.25·t + 0.20·sPitch·50/EffortStuffGain, 0.05, 0.95), else Looking
Strike  out of zone → Chase-Swinging (always a swing: an out-of-zone strike is a chase)
Foul    → Foul (two strikes), or Foul with FlavorEarlyFoul = 0.30 of pre-two-strike strikes (display only; the class stays "strike")
InPlay  → Swing
```

Pro Math can ignore this draw. It exists so the reveal (§9) can say "froze him" vs "swung through it" without a second resolution model.

### 3.5 Centering at the Reference (rev 2, blocker B1)

Rev 1 claimed the Reference bot sits at `x = 0` by construction. It doesn't: `t ≥ 0` (E_ref[t] ≈ 0.50), `vRel⁺ ≥ 0` (≈ +0.175 for the fastball), the location grades have a nonzero mean under the Reference's executed mix, and the anticipation leans are one-sided. Uncentered, the Reference bot lands about .03–.05 *better* than ratings and G1 fails. The fix has three parts.

**1. Execution terms: subtract their reference expectation.** Split each class exponent and `q` into an *execution* part (effort, velocity, timing, movement, location grade, chase-cell bias) and an *anticipation* part (`aZ`, `aT`, `aC`). Then center the execution part of each output per count and in/out:

```
x_k  = (exec_k − ExecCenter[count][inZone][k]) + antic_k          k ∈ {Ball, Strike, Play, Q}
ExecCenter[c][z][k] = E_ref[exec_k | count = c, inZone = z]
```

`ExecCenter` is **computed analytically at `BeginPa`**, with no warm-up run. Every input is a closed form there: the type mix `ρT`; the Reference target rule (§7.2); and the triangular scatter, whose per-axis CDF is piecewise quadratic, so `P(executed cell | target, M)` is exact. That gives `P(type, loc | count, in/out)`. On the first pitch (count 0-0) `t` uses `velocityPrev = velocityMean`; at other counts the previous type is drawn from `ρT`, independent of the current pitch under Reference. Effort is 0 for Reference, so effort terms contribute nothing to the center and stay a pure lever. The table is 12 counts × 2 × 4 = 96 doubles plus `WasteRef[12][3]` (§3.3), held in the preallocated book buffer (§5.3), so no allocation. Because `M` is fixed per PA (control_eff snapshots at PA start), one evaluation per PA is exact. Cost: about 2k flops per PA.

**2. Anticipation terms: "indicator minus reference probability" form** (§4.4). That makes each one exactly zero-mean whenever the next pitch is independent of the book's state, which holds for Reference by construction, since its draws never read the book.

**3. Location-grade centering.** The Reference's in-zone mix (§7.2) is 1/9 heart, 4/9 side and 4/9 corner targets, with scatter. So "painting more than the reference" earns, and "grooving more than the reference" pays. That is the intent of §3.2 restated relative to the reference, not relative to the Side grade.

`mRel` is already zero-mean over usage, but **not** conditional on in/out (`typeShift` correlates type with location), so it is centered with the rest of the execution part. `sPitch` and `EffortWhiffGain·e` are zero at Reference and need no centering.

---

## 4. The batter AI: the book

### 4.1 Shape

`struct BookBatterPolicy : IBatterPolicy`, one instance per **human pitcher per game**, shared by every NPC batter on the opposing team (the dugout talks). It is reset when a new pitcher enters. Its tables live in one `double[]` buffer allocated once in `MicroGame.Initialize` (§5.3), so nothing is allocated per game or per pitch.

Tables (pseudo-counts, `double`, matching every rate in `PitchChain`; rev 2, C2), decayed by `BookDecay` per observation in the row:

| Table | Shape | Keyed by |
| :-- | :-- | :-- |
| `typeByBucket` | [4 buckets][3 types] | count bucket |
| `typeBySeq` | [3 prevType][3 types] | previous pitch type (within the PA; first pitch uses a 4th "none" row) |
| `typeGlobal` | [3] | nothing (slow, catches period patterns that keep marginals skewed) |
| `zoneByBucket` | [4][2] | in / out, count bucket |
| `cellByBucket` | [4][17] | executed cell, count bucket |

Count buckets: `B0` 0-0 · `B1` pitcher ahead (strikes > balls, strikes < 2) · `B2` even or behind (balls ≥ strikes, not 0-0, strikes < 2) · `B3` two strikes.

Reference priors (what a "no information" batter expects):

```
ρT[type]   = arsenal usage share
ρZone(b)   = ZoneProbability(representative count of bucket b, control_eff, Fastball)    // 0-0, 0-1, 1-0, 1-2
ρL[cell]   = ρZone/9 for zone cells, (1 − ρZone)/8 for ring cells
```

### 4.2 Learning

On `OnPitchResolved(in PitchResult r)` (type, executed cell, count at the pitch, previous type):

```
row *= BookDecay;  row[observed] += 1          // for each of the five tables
```

Prior strength per table is scaled by the *current batter's* discipline when the sit is computed (not stored), so a disciplined lineup reads the same evidence faster:

```
αT = BookPriorType · disc,  αZ = BookPriorZone · disc,  αC = BookPriorCell · disc
disc = clamp(1 − BookReadDisGain·dDis, 0.5, 1.5)
```

### 4.3 The sit

Before each pitch the policy computes smoothed distributions and commits to a **sit** (the book's single best guess) plus **leans** (how far the book is from the prior). It never samples; it is deterministic given the tables, so it consumes **no RNG**.

```
πT ∝ αT·ρT + typeByBucket[b] + BookSeqWeight·typeBySeq[prev] + BookGlobalWeight·typeGlobal
sitType  = argmax(πT − ρT);   leanType = clamp((πT[sitType] − ρT[sitType])·readScale, 0, LeanCapType)

πL ∝ αC·ρL + cellByBucket[b];   πZone = Σ_{c<9} πL[c]  (blended with zoneByBucket at weight 1)
leanZone = clamp((πZone − ρZone)·readScale, −LeanCapZone, +LeanCapZone)      // signed: negative = "sitting on a ball"
side     = leanZone ≥ 0 ? zone : ring
sitCell  = argmax over cells on `side` of (πL[c]/πSide − ρL[c]/ρSide)
leanCell = clamp(that maximum · readScale, 0, LeanCapCell)

readScale = clamp(1 + BookApplyConGain·dCon + BookApplyDisGain·dDis, 0.6, 1.4)
```

The intent is `BatterIntent.Model(sitType, sitCell, leanType, leanZone, leanCell)`: three bytes and three doubles. The chain turns it into anticipation terms once it knows the executed pitch.

### 4.4 Anticipation terms (computed by the chain)

```
// rev 2: every term is "indicator − reference probability", so E_ref[a] = 0 given the lean (§3.5)
aT = leanType · ([type == sitType] − ρT[sitType]) / (1 − ρT[sitType]) / LeanScale
       // hit: +leanType/LeanScale (as rev 1); miss: −leanType·ρT[sitType]/(1 − ρT[sitType])/LeanScale
       // rev 1 used ρT[type] on a miss, which is not zero-mean
aZ = 2 · leanZone · ([inZone] − pRef) / LeanScale,   pRef = zRef(count, type)
       // equals rev 1's ±leanZone/LeanScale at pRef = 0.5
aC = (side of executed cell == side of sitCell)
       ? leanCell · (credit(sitCell, cell) − CreditRef[sitCell]) / (1 − CreditRef[sitCell]) / LeanScale
       : 0                                                                         // the zone term already covers a wrong side
credit = 1.0 same cell · 0.5 adjacent (Chebyshev 1, within the same side) · 0.2 otherwise
CreditRef[sitCell] = E_ref[credit(sitCell, executed cell) | same side]   // 17 values, analytic at BeginPa (§3.5); rev 1 used a flat 0.30
all three clamped to [−1, 1]
```

Positive = the batter was on it; negative = fooled. The signs flow into §3.2: `aZ > 0` on an in-zone pitch kills called strikes and raises contact; `aZ < 0` on an out-of-zone pitch is a chase; `aT < 0` is a batter sitting fastball who gets the changeup.

### 4.5 Anti-exploit bounds, by construction

- **No oracle.** The policy's `NextPitch` receives a `PitchLook` like every batter policy, but the pitching path passes `PitchLook.Hidden` (cue = none, zone probability = ρZone) and the policy reads only its tables. The current pitch's type, cell and effort are invisible until `OnPitchResolved`.
- **Sharpness is capped twice.** Decay bounds the effective sample to `1/(1 − BookDecay) = 10` observations per row, and the prior adds `α` pseudo-counts, so the maximum mass on any type after any history is `(α·ρ + 10)/(α + 10)`. With `α = 6`: a 55%-usage fastball can reach 0.83 (lean 0.28 → `aT = 0.56`); a 15% pitch can reach 0.68 (lean 0.53, capped at `LeanCapType = 0.5` → `aT = 1.0`). Being read on your *rare* pitch is possible only if you throw it ten times running in the same situation. (Rev 2 precision, C4: `πT` also adds `BookSeqWeight·typeBySeq` and `BookGlobalWeight·typeGlobal`, each with up to 10 effective observations, so the true ceiling is `(α·ρ + 20)/(α + 20)`, about 0.90 for the fastball. The binding bound is the `LeanCap*` clamp, not the decay.) Cells have `α = 12` over 17 cells, so a single cell needs 4–5 repeats in a bucket before `leanCell` clears 0.2.
- **Leans are clamped before use** (`LeanCapType/Zone/Cell`), and the tilt gains are a fixed budget, so the fully-read pitcher sits at the band floor and no lower (§7.3).
- **No fixed pattern is dominant.** Any policy that is a function of (count bucket) or (previous type) is learned by the two type tables within ~5 repeats; a pattern that keeps bucket and 2-gram statistics flat but skews marginals is caught by `typeGlobal`; a pattern that keeps *all* of these at the reference is, to the batter, indistinguishable from the reference and earns the reference line, not a bonus. The book is a minimax opponent whose value is the reference: predictability costs up to the floor, and out-thinking the book (throwing what it is not sitting on) earns a bounded edge that shrinks as the book re-learns. The positive half of the band comes mostly from **execution** (location, sequencing, effort), not from mixing.
- **The book is never shown before the pitch.** The reveal exposes the sit after the pitch, and the scouting hint (§9) summarizes tendencies the *pitcher* has shown, never the book's internal numbers.

### 4.6 Why "always challenge" stops being dominant

Suite 7 shows the binary seam's problem: always-in-zone walks 3.1% vs 9% neutral, and nothing pushes back. On this design, a pitcher who throws random types to random zone cells drives `πZone` toward 0.9 within a dozen pitches (`leanZone ≈ 0.45`, capped; `aZ ≈ 0.9`). On every in-zone pitch:

```
xStrike = −0.45·0.9 = −0.405,   xPlay = +0.35·0.9 = +0.315
in-zone neutral rates at s = 0.55 (ball .139, strike .639, inPlay .222; any zRef)
  → weights .139, .426, .304 → ball .160, strike .490, inPlay .350
out-of-zone (19% of Challenge pitches at M = 1.10), after the §3.3 mixture and xBall = −0.45 (a known strike chased):
  → ball .275, strike .538, inPlay .187
per pitch → ball .182, strike .499, inPlay .319
```

(Rev 2, C1: rev 1 used in-zone rates that didn't sum to 1. The numbers above come from the shipped `SolveNeutral` at the all-50 matchup, which absorbs to BB .090 / K .225 / BIP .685.)

Through the count chain that gives BB ≈ 1%, K ≈ 12%, BIP ≈ 87%. Called strikes vanish and almost every PA ends in play. At `q = 0`: AVG .288, OBP .296, SLG .481, **opp OPS ≈ .78**. With `ZoneKnownQ·aZ ≈ +0.036` on in-zone pitches: **≈ .81, against .728 for Reference**, and that's before the batter's `aC` on the predictable cells. The physical justification is real: a batter who knows the pitch is a strike doesn't take it. Meanwhile the Reference bot stays at .727 and the Perfect bot (§7.2) lands near .63, so Challenge is dominated in both directions. Gate G4 pins this.

---

## 5. Engine mapping

### 5.1 `PitchCall`

```csharp
public readonly struct PitchCall {
    public readonly bool IsCalled;        // shipped
    public readonly PitchType Type;       // shipped
    public readonly bool TargetInZone;    // shipped: legacy binary target (harness suite 7, InteractivePitcherPolicy.Throw)
    public readonly bool HasCell;         // NEW: false in Neutral and legacy calls
    public readonly byte TargetCell;      // NEW: 0..16
    public readonly double Effort;        // NEW: [-1, 1] (double, C2)
    public static PitchCall Neutral => default;                        // unchanged meaning: all false/zero
    public static PitchCall Throw(PitchType t, bool inZone) ...        // legacy, HasCell = false
    public static PitchCall ThrowAt(PitchType t, byte cell, double effort) ... // HasCell = true, TargetInZone = cell < 9
}
```

`default` must still mean Neutral, which is why the cell is gated by `HasCell` rather than a sentinel.

### 5.2 `IPitcherPolicy`

Add `void OnPitchResolved(in PitchResult result);` (explicit implementation; no-op in `NeutralPitcherPolicy`; `InteractivePitcherPolicy` forwards to `PlayerIntentBridge`; scripted bots record). **It is invoked only on the pitching-mode path** (`call.HasCell`), so the neutral path's code and draw sequence are untouched. `InteractivePitcherPolicy.NextPitch` blocks on a new `PlayerIntentBridge.AwaitPitchCall` overload returning (type, cell, effort); the existing `SubmitPitchCall(PitchType, bool)` stays for the harness.

### 5.3 `BatterIntent` / `IBatterPolicy`

- `BatterIntentKind.Model` (new enum value). `BatterIntent.Model(PitchType sitType, byte sitCell, double leanType, double leanZone, double leanCell)`. `BatterIntent` is a plain readonly struct (Kind, GuessType, GuessCell, Approach, ReadIsScripted, two bools, ScriptedLocAcc), not a union, so the Model fields are added with zero defaults and `BatterIntentKind` takes a new value (Q7, verified).
- `BookBatterPolicy : IBatterPolicy` (new, struct, explicit members). `BeginPa` records the batter's `dCon/dDis` and resets `prevType`; `NextPitch` returns the Model intent (no RNG); `OnPitchResolved` learns (§4.2); `OnPaResolved` no-op.
- **Where the book lives (Q3, verified).** `PlayHalfInning` builds a fresh `NeutralBatterPolicy` per half-inning (`MicroGame.cs`), so the book can't live there. Its tables, plus the §3.5 centering tables, sit in **one `double[]` (≈ 250 doubles) allocated once in `MicroGame.Initialize`**. `BookBatterPolicy` is a struct holding that reference. It is passed by `ref` through `PlayGame` → `PlayHalfInning` and reset (cleared, not reallocated) on a pitching change. That's zero allocation per game and per pitch. The Contrarian and Perfect bots (§7.2) read the same buffer read-only.
- **Batter ratings for the book (Q3b, verified).** `HumanPaContext` carries no batter ratings, and the `humanPitching` branch of `MicroGame` currently calls only `pitcherPolicy.BeginPa`. The book's `dCon/dDis` come from `PitchMatchup.BatterContact/BatterDiscipline` (already populated), and that branch **adds a `book.BeginPa(...)` call** beside `pitcherPolicy.BeginPa`.
- `PitchResult` gains: `TargetCell` (byte, 255 if none), `ExecutedCell` (byte, 0..17), `Effort` (double), `Flavor` (enum, §3.4), `LocGrade` (enum), `AnticipationZ/T/C` (3 doubles), `TimingDisruption` (double), `SitType`, `SitCell`, `LeanBucket` (0..3). All default-zero; adding fields changes no draw. `PitchResult` is a `readonly struct` passed `in`; add the fields through a new constructor overload and keep the shipped 7-arg and 9-arg constructors, defaulting the new fields (Q1, verified).

### 5.4 `SimulatePa` and `PitchMatchup`

`PitchMatchup` (or the per-PA context) gains the arsenal summary the path needs, computed once per PA at `BeginPa`: `velocityMean`, `movementMean`, `ρT[3]`, and `velocityPrev` state. Zero-alloc: `PitcherArsenal` is already a byte struct on `PitchMatchup`, so the means and `ρT` are arithmetic (Q5, verified). `ExecCenter`, `WasteRef` and `CreditRef` (§3.5) are filled into the book buffer at the same point.

The per-pitch loop gains **one early branch**:

```
call = pitcher.NextPitch(count, rng);
if (call.HasCell) { ResolveCalledCellPitch(...); continue; }     // NEW path, §5.5
// shipped loop from here, byte-identical: type / zRef / zDraw / inZone / blur / cue / look / batter.NextPitch / branch
```

### 5.5 Draw order on the pitching-mode path

For determinism review, in order, per pitch:

1. `pitcher.NextPitch` (human: no sim draws; bots: their own, documented).
2. Scatter: 4 × `NextDouble` (§2.2).
3. `batter.NextPitch(PitchLook.Hidden, count, rng)`: `BookBatterPolicy` draws nothing (a scripted batter on this path may).
4. Class draw: 1 × `NextDouble` (§3.3). Waste: skipped (Ball, no class draw), then an HBP draw of 1 × `NextDouble` if inside-waste (§9.1). The §3.3 mixture and the §3.5 centering are arithmetic and draw nothing.
5. Two-strike foul draw: 1 × `NextDouble` (shipped constant).
6. In play: `DrawBallInPlay(p*, q)` (shipped).
7. Flavor draw: 1 × `NextDouble` (§3.4).
8. `batter.OnPitchResolved`, `pitcher.OnPitchResolved`, `fatigue.AddPitches(1 + EffortFatigueGain·effort)` (§9.3).

### 5.6 Invariant N, by construction

- The branch in §5.4 is taken only when `call.HasCell`. `PitchCall.Neutral` and legacy `Throw(type, inZone)` have `HasCell = false`, so **every existing path runs the shipped code with the shipped draw order**: Neutral batter + Neutral call (calibration), Neutral batter + legacy call (suite 7), Read batter + Neutral call (human batting, A0 sweep).
- `BookBatterPolicy` is used only in the `humanPitching` half-inning branch of `PlayHalfInning`. Its buffer is allocated once in `MicroGame.Initialize`, which consumes no RNG. `NeutralBatterPolicy` is untouched.
- The Model branch is a new `case` in the intent switch; `Neutral` and `Read` cases are not edited. Struct field additions have zero-defaults and are never read on the old paths.
- `AtBatResolver` is not touched; p* is computed exactly as today and consumed by `DrawBallInPlay` exactly as today.
- Gate G5 (§7.4) re-runs suites 5/6/7, the A0 sweep and the resolver guard and requires byte-identical lines.

Effect on the shipped human-batting Read path: **none.** A later batting re-band (§11.1) would be its own versioned slice.

---

## 6. Where the skill lives: a reader's walkthrough

- **First time through the order**, the book is the prior: leans ≈ 0, every tilt is execution only. A control-70 pitcher painting corners and changing speeds is already ~.05 better than ratings; a control-30 pitcher trying the same thing throws waste pitches and walks people.
- **Second time through**, the book has ~30 pitches. If the player has been first-pitch fastball every time, `typeByBucket[B0]` is at 0.8 on fastball, the batter sits on it (`aT ≈ 0.5`, `aC` grows if it is always the same spot), and the reveal says so. The fix is cheap (a first-pitch breaking ball is now *fooling*, `aT ≈ −0.4`), which is the lesson.
- **With two strikes**, a chase pitch with movement from a tunneled velocity change is the strikeout pitch (`xBall −0.3`, `xStrike +0.3`); a chase pitch the book expected (`leanZone < 0`) is a ball, and three of those are a walk the player chose.
- **In the 7th**, fatigue has widened `M` by 30%, corners become sides and ring targets become waste. Taking effort off (−0.5) buys the location back at the cost of whiffs: the player feels stamina as a budget they can spend or save.

---

## 7. Skill band and harness ("P0 sweep")

### 7.1 Band

Target: **poor vs excellent ≈ ±.100 OPS around the ratings line** for an average avatar (the 40-vs-70 gap), hard-capped by the tilt clamps. Expected placements at all-50 (estimates, §7.3):

| Bot | Opp OPS (est.) | Role |
| :-- | :-- | :-- |
| Perfect | .62–.65 | ceiling |
| Contrarian | .69–.72 | mixing lever alone |
| **Reference** | **.727 ± .02** | self-consistency anchor |
| CoinFlip | .73–.76 | "no skill", slightly worse than ratings (one third of pitches are ring targets → walks) |
| Challenge | .78–.81 | the shipped exploit, now dominated (rev 2 re-derivation, §4.6) |
| PredictableCorner | .78–.83 | well-located but read |
| PredictableHeart | .84–.87 | floor (cap) |

**Where an average player lands:** near ratings. A casual player who mixes without a plan (`aT ≈ +0.1`), aims at sides and corners with control-50 scatter (`LocQ ≈ −0.02`), changes speeds occasionally (`t ≈ 0.2`) and leaves effort at 0 is estimated at .71–.74. A thoughtful player who reads the reveal gains ~.05; a sloppy one loses ~.05. Doing nothing (auto-pitch = Reference) is exactly ratings.

### 7.2 Scripted bots (precise enough to code)

All bots implement `IPitcherPolicy` as structs and run 20,000 PAs against `BookBatterPolicy` at the all-50 matchup unless stated. Bots that consult the book receive a `ref readonly` view of the policy from the harness (the harness owns both). Each bot's own randomness uses `rng` draws documented in the bot.

| Bot | Type | Target | Effort |
| :-- | :-- | :-- | :-- |
| **Reference** | ~ usage (1 draw) | zone target with probability `pIn(count, type)`, then uniform of 9; else uniform of 8 ring (2 draws). **Rev 2 (B3):** `pIn` is solved **exactly** per (count, type) so the *executed* in-zone rate equals `zRef(count, type, control_eff)`, the quantity the §3.3 mixture identity is defined on: `pIn = clamp((zRef − pRing)/(pZone − pRing), 0, 1)`, where `pZone`/`pRing` are the mean in-zone rates of a uniform zone/ring target under the closed-form scatter at this PA's `M` (at M = 1.10: .811 / .086, which covers zRef's [.20, .80]). No warm-up run, and identical across arsenals. `ρZone(bucket)` stays the **book's prior only** (Q6). | 0 |
| **CoinFlip** | uniform of 3 | uniform of 17 | 0 |
| **Challenge** | ~ usage | uniform of 9 zone cells | 0 |
| **Paint** | ~ usage | uniform of 8 ring cells | 0 |
| **PredictableHeart** | most-used type | cell 4 | 0 |
| **PredictableCorner** | most-used type | cell 8 (down-away) | 0 |
| **Contrarian** | `argmin(πT/ρT)` from the book | `argmin` credit vs `sitCell` on the side opposite `leanZone`'s sign (zone if `leanZone < 0`) | 0 |
| **Perfect** | Contrarian's type, except it alternates velocity rank when `t` would otherwise be 0 | strikes < 2: the corner cell with the lowest credit vs `sitCell`; two strikes: the ring side cell with the highest `mRel` for the chosen type | +0.5 at two strikes, −0.5 at three balls, else 0 |

Perfect is a hand-built heuristic ceiling (book-aware, well-located, sequenced, effort-managed). A true per-pitch optimizer would score a little lower still; the clamps bound it.

Additional runs: every bot at pitcher **30/30** and **85/85** (stuff/control) for the cap gates; Perfect and PredictableHeart over **full 110-pitch outings** (fatigue curve) reporting opp OPS by 20-pitch window; Reference at **HS tier** effective ratings (stuff/control −20, batters' discipline −20) to record the tier line.

Metrics per bot: opp AVG/OBP/SLG/OPS, K%, BB%, HR%, P/PA, executed in-zone %, waste %, chase % (out-of-zone strikes / out-of-zone pitches), whiff flavor %, mean `aT`/`aZ`/`aC`, mean `|lean|`, HBP %.

### 7.3 Back-of-envelope: do the first-pass constants land in the band?

Two yardsticks, both from the shipped numbers:

- **Strike tilt:** a sustained `xStrike = +0.10` lifts the per-pitch strike share ~7% after renormalization; strikeouts need three, so K rises ×1.16 (22.5% → 26%, the same move as +20 stuff in the macro table) and opp OPS falls ≈ **.030**.
- **Quality:** `DrawBallInPlay` at `q = +0.20` moves the in-play split from (out .672, 1B .209, 2B .067, 3B .006, HR .047) to (.608, .238, .082, .007, .065): hits per BIP .328 → .392, total bases .549 → .683. Over 68.5% BIP that is AVG +.048 and SLG +.100, so sustained `q = ±0.10` ≈ **±.075 OPS**.
- **Ball share:** −0.05 absolute per pitch ≈ BB −3 points ≈ **−.030 OPS** (suite 7's −0.11 gave −6 points).

Perfect (sustained means): corners (in-zone 72%): `LocQ −0.07`, `xStrike +0.04`; two-strike chase with `mRel ≈ +0.3`: `xBall −0.1` on a quarter of pitches; `t ≈ 0.6`: `xStrike +0.07`, `q −0.05`; book edge `aT ≈ −0.3`, `aC ≈ −0.2`: `q −0.03`, `xStrike +0.03`; effort: `xStrike +0.02`. Totals ≈ `q −0.15` (−.11), `xStrike +0.16` (−.05), walks ~5% (−.04) → about **−.20 before clamps**; `QClampLo = −0.30` leaves that, so Pro Math should expect to trim `LocCornerQ`, `TimingWhiffGain` or the chase gains, or tighten `QClamp`, to hit −.10. The constants are deliberately set so the *first* harness run overshoots the ceiling a little and undershoots nothing: tightening is a one-knob job, widening a many-knob one.

PredictableHeart: `aZ ≈ 0.9` (−.08 K-path, +.10 BIP-path), `aT ≈ 0.5`, `aC ≈ 0.6`: `q +0.07`; heart: `q +0.20`, `xStrike −0.15` → `q` clamps at `+0.30` → ≈ +.17 ⇒ .85–.90 before the walk gain from always being in the zone (−.05) ⇒ **.84–.87**. At the cap.

CoinFlip: 8/17 ring targets → executed in-zone ≈ 43%, waste ≈ 8% (walks ≈ 11%, `+.02`); `LocQ` mean ≈ −0.10 (ring targets mostly land Chase or Waste; `−.07` when in play, but a third of those pitches are balls) → net ≈ **+.01 to +.03**. "No skill" ≈ ratings, slightly worse.

### 7.4 Gates (MonteCarloHarness, new suite 8 "P0 sweep")

| Gate | Check |
| :-- | :-- |
| **G1 self-consistency** | Reference: opp OPS .727 ± .020, K% 22.5 ± 1.5, BB% 9.0 ± 1.5, P/PA 3.84 ± 0.12, HR% 3.2 ± 0.6 |
| **G2 ordering** | opp OPS: Perfect < Contrarian ≤ Reference ≤ CoinFlip < Challenge < PredictableCorner < PredictableHeart; every `<` is a gap ≥ .010, every `≤` allows a tie within noise |
| **G3 band** | Reference − Perfect ∈ [.080, .130]; PredictableHeart − Reference ∈ [.100, .160]; PredictableCorner − Reference ∈ [.050, .120] |
| **G4 not dominant** | Challenge ≥ Reference + .010 and Challenge ≥ Perfect + .080; Paint ≥ Reference + .030 |
| **G5 invariants** | suites 5/6/7 green; A0 sweep lines byte-identical; resolver guard PA 48384 / H 10969 / ER 5237 |
| **G6 zero-alloc** | `GC.GetAllocatedBytesForCurrentThread()` delta = 0 across a 20k-PA Perfect run after one warm-up PA |
| **G7 determinism** | two runs, same seed: identical per-pitch logs (cell, class, flavor, q) |
| **G8 caps** | at 85/85: PredictableHeart − Neutral(85/85) ≤ .160; at 30/30: Neutral(30/30) − Perfect ≤ .130 |
| **G9 fatigue** | Perfect, pitches 81–110 vs 1–30: waste % up by ≥ 5 points, opp OPS up by .03–.10 |

G1–G4 and G8–G9 are *self-consistency and spread* gates, never a batting-average band (pack §7).

---

## 8. Constants (all "for Pro Math"; first-pass values)

| Name | Value | Range | Rationale |
| :-- | :-- | :-- | :-- |
| `ScatterMaxAt50` | 1.10 cells | 0.80–1.40 | corner target in-zone 72% ≈ shipped `acc` 0.75 |
| `ScatterControlGain` | 0.55 | 0.30–0.80 | control 80 → corners 88%, control 20 → 56% |
| `ScatterMin` / `ScatterMax` | 0.45 / 1.80 | – | floors scatter at control 100+effort −1; caps a 0-control fatigued arm |
| `ScatterEffortGain` | 0.20 | 0.10–0.35 | max effort widens miss by 20% |
| `ScatterMovementGain` | 0.15 | 0–0.30 | big breakers are harder to command |
| `PressureScatterGain` | 0 | 0–0.30 | hook for `Active_Effects` (owner decision 2) |
| `EffortStuffGain` | 6 rating pts | 3–10 | +1 effort ≈ +6 stuff on this pitch (`sPitch` ±0.12) |
| `EffortWhiffGain` | 0.08 | 0.04–0.15 | `xStrike` from `e`; ≈ +20 stuff's K move at +1 across a PA |
| `EffortFatigueGain` | 0.40 | 0.20–0.60 | +1 effort costs 1.4 pitches, −1 costs 0.6 |
| `VeloWhiffGain` | 0.10 | 0–0.20 | fastball above the arsenal mean misses bats |
| `VeloOffTypeWeight` | 0.5 | 0–1 | velocity matters half as much off the fastball |
| `VeloDiffScale` | 20 rating pts | 10–40 | a 20-point speed change is full timing disruption |
| `TimingWhiffGain` | 0.12 | 0–0.25 | `xStrike` from `t` |
| `TimingChaseGain` | 0.10 | 0–0.20 | `xBall` from `t` on out-of-zone pitches |
| `TimingQualityGain` | 0.08 | 0–0.15 | `q` from `t` |
| `MovementChaseGain` | 0.25 | 0.10–0.40 | `xBall` from `mRel` out of zone: the chase pitch |
| `MovementWhiffGain` | 0.08 | 0–0.15 | `xStrike` from `mRel` |
| `MovementQualityGain` | 0.10 | 0–0.20 | `q` from `mRel`: weak contact |
| `ChaseCellBias` side / corner | 0 / +0.15 | 0–0.30 | ring corners are chased less than ring sides |
| `LocHeartQ / Whiff / Play` | +0.20 / −0.15 / +0.10 | see §3.2 | meatball |
| `LocSideQ / Whiff / Play` | 0 / 0 / 0 | – | the reference zone pitch |
| `LocCornerQ / Whiff / Play` | −0.10 / +0.05 / −0.05 | – | painted |
| `LocChaseQ / Whiff / Play` | −0.25 / +0.10 / −0.10 | – | chased pitches are weakly hit |
| `ZoneKnownWhiff` | 0.45 | 0.30–0.60 | called strikes ≈ 38% of strike class in the shipped mix; a known strike loses them |
| `ZoneKnownPlay` | 0.35 | 0.20–0.50 | …and they become balls in play |
| `ZoneKnownBall` | 0.50 | 0.30–0.70 | a known ball is taken; a surprise ball is chased |
| `ZoneKnownQ` | 0.04 | 0–0.10 | small: knowing "strike" alone doesn't make hard contact |
| `TypeKnownQ / Whiff / Play` | 0.06 / 0.06 / 0.04 | 0–0.12 | sitting on the type |
| `CellKnownQ / Whiff / Play` | 0.06 / 0.04 / 0.03 | 0–0.12 | sitting on the spot |
| `LeanScale` | 0.5 | 0.3–0.7 | +0.5 mass over the prior = fully "on it" |
| `LeanCapType / Zone / Cell` | 0.50 / 0.45 / 0.50 | 0.3–0.7 | the omniscience bound after `readScale` |
| `CreditRef[sitCell]` | computed (rev 2) | – | not a knob: E_ref of credit on the sit's side, analytic at `BeginPa` (§3.5, §4.4); rev 1's flat 0.30 was not zero-mean |
| `ExecCenter[12][2][4]`, `WasteRef[12][3]` | computed (rev 2) | – | not knobs: reference expectations of the execution terms and of the waste share, analytic at `BeginPa` (§3.3, §3.5) |
| `BookPriorType / Zone / Cell` | 6 / 6 / 12 | 3–20 | pseudo-counts; cells learn slower (17 bins) |
| `BookDecay` | 0.90 | 0.85–0.95 | effective memory 10 observations per row |
| `BookSeqWeight` | 0.5 | 0–1 | 2-gram table weight |
| `BookGlobalWeight` | 0.5 | 0–1 | global table weight |
| `BookReadDisGain` | 0.6 | 0.3–1.0 | discipline shrinks the prior (faster learning) |
| `BookApplyConGain / DisGain` | 0.4 / 0.2 | 0–0.6 | contact and discipline scale lean use (`readScale` ∈ [0.6, 1.4]) |
| `QClampLo / Hi` | −0.30 / +0.30 | ±0.2–0.4 | **owner cap** on in-play quality |
| `WhiffClampLo / Hi` | −0.60 / +0.35 | – | **owner cap**; asymmetric because `ZoneKnownWhiff` is the big term |
| `BallClamp` | ±0.60 | – | **owner cap** |
| `PlayClamp` | ±0.50 | – | **owner cap** |
| `FlavorSwingBase` | 0.40 | – | presentation only |
| `FlavorEarlyFoul` | 0.30 | – | presentation only (matches batting's early-foul share) |
| `HbpOnInsideWaste` | 0.08 | 0.03–0.15 | inside waste pitch hits the batter (resolves as Walk, flagged) |
| `WildPitchOnWaste` | 0.25 | 0.10–0.40 | **deferred** (§9.1) |
| `ArsenalAbsoluteGain` | 0 | 0–0.5 | owner knob: absolute velocity/movement effect (§3.1) |

---

## 9. Feel, presentation and edge cases

### 9.1 Waste pitches, HBP, wild pitches

- A **Waste** pitch is a Ball, never swung at. The reference waste share is carved out of the neutral out-of-zone ball rate (§3.3), so only waste *beyond* the reference costs walks. On an *inside* waste pitch the chain draws HBP with `HbpOnInsideWaste`; HBP ends the PA as `PaOutcome.Walk` (as today) with `PitchResult.Flavor = HitByPitch` so the UI and announcer show it. Nothing else in the sim changes (no injury at launch; owner Q3).
- **Wild pitches** need a runner-advance hook in `MicroGame` that the pack doesn't confirm (open Q4). **Deferred:** the DTO carries `IsWaste` from slice G-2a so the UI can show the catcher scrambling, and `WildPitchOnWaste` ships at 0 until the runner model has an "advance all one base" event.

### 9.2 Two-strike foul loop

Unchanged: `FoulShareOfStrikes = 0.68` self-loops on the strike class. The tilts change how often the strike class is drawn, not the foul share, so P/PA stays ≈ 3.84 at the reference and rises for pitchers the book is on (more fouls, fewer whiffs), which reads as "he's fighting off everything." Reported per bot.

### 9.3 Fatigue within an outing

`PitcherFatigue` semantics are unchanged: effective ratings snapshot at PA start. Its counter `_pitchesThrown` is already a `double` (`AddPitch` adds 1.0, `AddNpcPa` adds 3.9), so rev 2 adds **`AddPitches(double n)`** and the pitching path calls `AddPitches(1 + EffortFatigueGain·effort)` per pitch: 1.4 at max effort, 0.6 at finesse. Rev 1's effort-debt accumulator is dropped (Q2, verified). The neutral and legacy paths keep calling `AddPitch()`. The reveal shows tank remaining as a percentage of capacity (presentation reads it from the snapshot; it never writes).

### 9.4 Shadowed, pulled, absent

- The mode engages only in half-innings where `humanPitching` is true. A pulled avatar flips the policy back to `NeutralPitcherPolicy`; the book is discarded with the pitcher.
- A relief appearance starts a fresh book and the avatar's current `PitcherFatigue` state.
- An absent avatar (life event) has no attended pitching; the game is simulated as today.

### 9.5 Tiers

HS and College apply the tier environment before the PA (stuff/control −20 / −16), so a HS avatar scatters wider (`M ≈ 1.32` at a rated 50) and faces batters whose discipline is also lower, so the book learns slower (`disc` up to 1.5× prior). Predictability is punished less in HS and more in Pro: a built-in difficulty ramp with no extra knob. HS innings (7) only change capacity use, not the model.

### 9.6 PED

Stamina ×1.5 flows through capacity as shipped. No pitching-mode code reads `ped_active`; the substance engine owns it.

### 9.7 Person stats and pressure (hooks only)

`PressureScatterGain` (default 0) multiplies `M` by `(1 + gain·pressure)` where `pressure` ∈ [0, 1] would come from `Active_Effects` (owner decision 2). The natural pitcher analogue of the batting side's person `discipline` is person **composure**: when the owner adopts it, `pressure` = `max(0, leverage − composure)` style, defined in the Phase L effects spec, never here.

### 9.8 What the player sees (DTO only; presentation never writes)

`PitchRevealDto` (built from `PitchResult` by the bridge):

| Field | Source | UI use |
| :-- | :-- | :-- |
| `Type`, `TargetCell`, `ExecutedCell`, `InZone`, `IsWaste`, `LocGrade`, `MissCells` | pitch | the pitch flies to the executed cell with the target ghost; "painted" / "missed over the heart" |
| `Effort`, `FatiguePct` | call / snapshot | the tank meter |
| `Flavor` | §3.4 | take / froze / swung through / chased / fouled / in play / HBP |
| `SitType`, `SitCell`, `LeanBucket` (0 none, 1 slight, 2 leaning, 3 sitting) | book | the batter's reaction line *after* the pitch: "He was sitting fastball away" |
| `AnticipationBucket` (−2 fooled … +2 on it) | from `aZ/aT/aC` | reaction and announcer hook |
| `TimingBucket` (0..2) | `t` | "late on it" / "out in front" |
| `BookHintKey` (enum) | pitcher's own tendency: argmax lean across tables, emitted every 5 pitches or when a lean crosses 0.25 | the coach's note: "They've seen five first-pitch fastballs" |

`BroadcastBooth` events (bus, consumed by the announcer and cutscene director): `PaintedCorner`, `Meatball`, `Whiff`, `Chase`, `FooledHim`, `HitByPitch`, `OutOfGas` (fatigue multiplier ≤ 0.7). Announcer lines never speak player names; subtitles carry them.

The teaching loop is the hint plus the sit: the player learns that mixing beats the book, that corners beat sides beat the heart, that speed changes earn whiffs, and that the tank is a budget. Every one of those lessons is a sim fact in the DTO, not a presentation invention.

---

## 10. Engine slices

*The owner column below is rev 1's proposal. Current assignments live in [TEAM.md](../TEAM.md) §2, which takes precedence: the G-2a/b/d engine is assigned to GPT 6 Astra Pro, sequenced with G-8 as G-8a → G-2a → G-2b → G-8b.*

| Slice | Owner | Content | Gate |
| :-- | :-- | :-- | :-- |
| **G-2a Execution and the Model branch** | Claude Opus 5.5 | `PitchCall` cell/effort; scatter and quantization; `PitchResult` fields; `IPitcherPolicy.OnPitchResolved`; the pitching-mode path with the Model branch at `a = 0` (no book yet); `Reference`, `CoinFlip`, `Challenge`, `Paint`, `PredictableHeart/Corner` bots; suite 8 skeleton | G1, G5, G6, G7; Challenge and Paint lines recorded (expected: Challenge still favourable, the book comes next) |
| **G-2b The book** | Claude Opus 5.5, Gemini 3.1 Pro Math tunes | `BookBatterPolicy`, anticipation, `Contrarian` and `Perfect` bots, 30/30 and 85/85 and fatigue runs | G2, G3, G4, G8, G9; Pro Math signs the constants table. **G3 is expected to fail on the first run:** Perfect lands at about −.20 before clamps (§7.3), by design. Treat it as a tuning signal, not a regression. |
| **G-2c Pitching UI** | Claude Sonnet 5.5 | 17-cell picker, pitch selector, effort dial, `AwaitPitchCall` overload, `PitchRevealDto`, reveal panel, book hints, focus navigation, theme-driven styling; `godot_scene_mapper` on the at-bat scene first | build 0/0; a scripted UI run through 3 PAs; no sim writes (review) |
| **G-2d Edges and broadcast** | Claude Opus 5.5 (engine), Claude Sonnet 5.5 (UI) | `AddPitches` effort cost, HBP, waste/wild-pitch DTO, `BroadcastBooth` events, achievement mapping, HS tier line, optional wild pitches if Q4 is yes | G1–G9 re-run; `validate_audio_manifest` if lines are added; achievements map to bus events |

G-2a is independently shippable and already fixes the binary seam's worst case for the harness (cells + location grades). G-2b is where the owner's "predictable pitchers get hit" lands.

---

## 11. Disagreements

### 11.1 The band: pitching meets §1.4; batting as shipped does not, and should be re-banded

The A0 sweep says a *blind* reader bats .439 and a perfect one 1.898 against a ratings line of .727. That violates the owner's directive in both directions: a casual player is punished relative to auto-sim (retention risk: attending your own games makes you worse), and a good player bats like nobody in history. Pitching here is built to the §1.4 band (±.10, average player ≈ ratings). Two modes with bands an order of magnitude apart would feel like two games. **Recommendation:** accept this band for pitching, and schedule a versioned **"Read v2"** slice for batting that keeps its three inputs and its reveal but resolves through the same tilt idiom (the read score `R` becomes a bounded tilt on the neutral rates, with `R` at the random-reader level mapping to `x = 0`). That is the mirror the owner asked for; it is also the only way "ratings set the floor and the ceiling" can be literally true for batting. Owner Q1.

### 11.2 Observation 4 (reuse the Read branch with an NPC emitting Read intents): rejected

It would import batting's uncapped band wholesale (a book that guesses right 60% of the time would hit ~1.2 OPS), make the human-batting path hostage to NPC tuning, and still need stuff/velocity/movement/cell terms that the Read branch has no place for without changing human batting. A sibling branch costs one `case` and keeps both paths honest.

### 11.3 Observation 3 (9 + 8 cells): accepted, with the ring as *chase*, not *edge*

"Painting" is a within-zone distinction (corner vs side vs heart). The ring is where chases and waste pitches live. Calling the ring "edge" would invite targeting it as a safe strike, which at control 50 it is not (15% in zone).

### 11.4 Velocity and movement are relative, not a second stuff

The pack asks what velocity should do. It should **not** be a second stuff rating: p* already carries stuff. Velocity earns its keep as spread (fastball above your mean, the differential between consecutive pitches); movement as within-arsenal chase and weak contact. If the owner wants a fast arsenal to be better *in absolute terms*, that belongs in the ratings or in `ArsenalAbsoluteGain`, which is 0 here so the attended line stays on the macro line.

### 11.5 Suite 7 is a seam test, not a balance target

The 3.1% vs 18.4% walk spread proves the binary call has teeth. It must stay green (legacy path), but it should not be read as "the pitching lever"; after G-2 the binary path is harness-only and the UI never uses it.

### 11.6 G-1 should not raise control's macro weight much

Control now earns its value where the design intent says it should (attended location). Raising the macro control weight too would double-pay human pitchers in attended games versus their macro line. Recommend G-1 leaves `pit_control` near its current macro weight, or raises it only for the walk row.

### 11.7 Fatigue stays PA-granular

Agree with the pack's shipped semantics; per-pitch fatigue inside a PA would touch `PitcherFatigue` on both branches for a feel gain nobody will notice at ~4 pitches per PA. `AddPitches(1 + EffortFatigueGain·effort)` (§9.3) gives effort its cost without that change.

---

## 12. Open questions

### 12.1 Engine questions: answered (rev 2, verified in code by Claude Opus 5.5, 2026-10-10)

| # | Answer | Folded into |
| :-- | :-- | :-- |
| Q1 | `PitchResult` is a `readonly struct` passed `in` to `OnPitchResolved`. The interactive bridge enqueues copies into a `Queue<PitchResult>` (off the zero-GC path, allowed). There's no per-game pitch log on the hot path. Add fields through a new constructor overload; the 7-arg and 9-arg constructors stay and default the new fields. | §5.3 |
| Q2 | `PitcherFatigue._pitchesThrown` is already a `double`. Add `AddPitches(double)`; the effort-debt accumulator is dropped. | §9.3, §5.5 |
| Q3 | `PlayHalfInning` builds a fresh `NeutralBatterPolicy` per half-inning, so the book must outlive it. One `double[]` buffer allocated in `MicroGame.Initialize`; `BookBatterPolicy` is a struct holding it, passed by `ref` through `PlayGame` → `PlayHalfInning` and reset on a pitching change. Bots read it read-only. | §4.1, §5.3 |
| Q3b | `HumanPaContext` has no batter ratings. The book reads `PitchMatchup.BatterContact/BatterDiscipline`, and the `humanPitching` branch adds a `book.BeginPa` call. | §5.3 |
| Q4 | Out of scope for G-2. Wild pitches stay deferred behind the `IsWaste` DTO flag, and `WildPitchOnWaste` ships at 0. | §9.1 |
| Q5 | Yes. `PitcherArsenal` is a byte struct on `PitchMatchup`; the means and `ρT` are arithmetic at `BeginPa`. | §5.4 |
| Q6 | Yes, `ρZone(bucket)` is the book's prior only. The Reference bot targets `zRef(count, type)` exactly (B3). | §7.2 |
| Q7 | `BatterIntent` is a plain readonly struct, not a union. Add the Model fields with zero defaults; `BatterIntentKind` has room. | §5.3 |
| Q8 | Confirmed. The branch right after `pitcher.NextPitch` precedes every shared draw (`DrawType`, zone, blur), so the neutral sequence is untouched. | §5.4–§5.6 |
| Q9 | Yes. `PitchLook` is a readonly struct (Cue, ZoneProbability); add `static Hidden`, which the Model policy contractually ignores. | §4.5 |
| Q10 | Flag only at launch, no box-score column. | §9.1 |

### 12.2 For the owner

| # | Question | Recommended answer |
| :-- | :-- | :-- |
| O1 | Confirm the pitching band at ±.100 OPS (40-vs-70), average player ≈ ratings, and authorize a batting **Read v2** re-band to the same band (§11.1). | Yes to both. **Resolved:** recorded as decided in [gameplay_core.md](gameplay_core.md) §6 item 1 (2026-10-10). The batting re-band is slice G-8 ([brief](../tasks/G-8_read_v2_batting_reband_brief.md)). |
| O2 | Book scope: within one game only (reset per game), or a season-long scouting book on the avatar? | Within-game at launch. A season book is a good Phase G+ feature tied to press and scouting, with its own caps. |
| O3 | HBP: resolve as Walk with a flag (no injury, no ejection) at launch? | Yes. Injury links belong to the life sim later, through the bus. |
| O4 | Pickoffs, holds, pitch-outs, intentional walks: out of G-2? | Yes; intentional walk as a UI shortcut (four Waste pitches) can come with G-2d. |
| O5 | Should a faster or nastier arsenal be better in *absolute* terms in attended games (`ArsenalAbsoluteGain > 0`), accepting the attended line drifts off the macro line? | No at launch. |
| O6 | Pitcher person stat: adopt **composure** as the pressure hook (§9.7)? | Yes, when Phase L's `Active_Effects` lands; G-2 ships the hook at 0. |
