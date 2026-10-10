# G-2 Pitching Mode: Feasibility Review

**Reviewer:** Claude Opus 5.5, 2026-10-10 · **Spec:** [pitching_mode.md](../design/pitching_mode.md) @ `6ba9055` · **Verdict:** the architecture is sound and buildable (early `HasCell` branch, sibling `Model` intent, tilt idiom, book). **Two blockers break G1 by construction.** Fix B1–B3 and fold in the engine answers. No re-architecture.

Numbers below come from reproducing the shipped `SolveNeutral` (all-50 matchup, BB .090 / K .225): **neutral per-pitch ball .3088, strike .5129, inPlay .1783.** Location-conditioned at `s = 0.55`: **in-zone .139 / .639 / .222** (same for any zRef); out-of-zone at zRef .51 / .45 / .39: ball **.486 / .448 / .417**.

## Blockers

**B1. The Reference bot is not at x = 0.** Several terms are one-sided or have nonzero means under Reference, so §3.3's "reproduces p* by construction" fails, and G1 (±.020) would fail.
- `t ≥ 0` always. Under Reference (iid usage, league-average arsenal) **E[t] = 0.50**, giving xStrike ≈ +0.06, q ≈ −0.04, plus a chase term.
- `vRel⁺ ≥ 0`. The fastball sits at **+0.175** above `velocityMean` 41.25, adding xStrike ≈ +0.01 overall.
- Location grades. Reference's executed mix (uniform zone targets, scatter, ring targets) has mean `LocQ/LocWhiff/LocPlay` ≠ 0.
- Anticipation. With `π ≈ ρ +` noise, `argmax` plus `clamp(…, 0, cap)` makes `leanType`/`leanCell` ≥ 0, so `E[aT]` and `E[aC]` are slightly nonzero.
- Rough total: Reference lands **.03–.05 better than ratings**.
- **Required:** define every per-pitch term **relative to its expectation under the Reference policy for this arsenal and count**, so all terms are zero-mean at the reference. For example `t' = t − E_ref[t | prevType]`, `vRel' = vRel⁺ − E_ref[vRel⁺]`, `Loc*' = Loc* − E_ref[Loc* | count]`. Compute the centering constants **analytically at `BeginPa`** from the arsenal and the Reference targeting (all inputs are known closed forms), not from a warm-up run. For anticipation, either center on its expectation under the prior or state the residual bias and size it. Then restate the G1 claim precisely.

**B2. Forced-Ball waste pitches inflate the Reference walk rate.** Out-of-zone neutral P(ball) is .486 at zRef .51. With waste forced to Ball, P(ball | out) becomes **.537 / .588 / .640** at a waste share of 10 / 20 / 30%, so BB rises well above 9%.
- **Required:** make the out-of-zone conditional a mixture that preserves the neutral rate: `w·1 + (1 − w)·ballChase = ballOut`, so `ballChase = (ballOut − w)/(1 − w)` with `w = E_ref[P(waste | out, count, type)]`. Strike and in-play rates rescale as in pack §4.3. Then a pitcher who throws *more* waste than the reference pays in walks, which is the intended feel. Say what happens if `w > ballOut` (clamp, and size how often that can occur).

**B3. Reference targeting must match `zRef(count, type, control)`, not `ρZone(bucket)`.** The mixture identity (pack §4.3) is per count and type. Since scatter is a closed-form triangular distribution, `P(in | target cell, M)` is analytic, so **solve `pIn` exactly per (count, type)**. Drop the 2,000-pitch warm-up; it's also nondeterministic across arsenals. Keep `ρZone(bucket)` as the book's prior only (Q6 answer: yes).

## Corrections

**C1. §4.6 arithmetic.** The in-zone rates used (.162 / .52 / .20) don't sum to 1. The correct values are .139 / .639 / .222, and the tilt (xStrike −0.405, xPlay +0.315) gives **.160 / .490 / .350**. The conclusion holds (in-play up about 58%, called strikes collapse). Re-derive the "opp OPS ≈ .77" estimate from these numbers.

**C2. Use `double`, not `float`,** for `Effort`, leans and book tables, to match every knob and rate in `PitchChain`. Mixed precision invites cross-platform determinism drift.

## Engine answers (fold these into the spec; they replace §12.1)

| Q | Answer (verified in code) |
| :-- | :-- |
| Q1 | `PitchResult` is a `readonly struct` passed `in` to `OnPitchResolved`. The interactive bridge enqueues copies into a `Queue<PitchResult>` (off the zero-GC path, allowed). There's no per-game pitch log on the hot path. Add fields through a new constructor overload. The old 7-arg and 9-arg ctors stay and default the new fields. |
| Q2 | `PitcherFatigue._pitchesThrown` is already a **double** (`AddPitch` += 1.0, `AddNpcPa` += 3.9). **Add `AddPitches(double)`** and drop the effort-debt accumulator in §9.3. |
| Q3 | `PlayHalfInning` builds a fresh `NeutralBatterPolicy` per half-inning, so the book must outlive it. **Back the book with one buffer allocated in `MicroGame.Initialize`** (≈103 doubles). `BookBatterPolicy` is a struct holding that reference, passed by `ref` through `PlayGame` → `PlayHalfInning` and reset on a pitching change. That's zero allocation per game and per pitch, and it lets Contrarian/Perfect read the same buffer read-only, which answers §7.2's "`ref readonly` view". |
| Q3b | **New:** `HumanPaContext` carries no batter ratings, and in the `humanPitching` branch `MicroGame` currently calls only `pitcherPolicy.BeginPa`. The book's `dCon/dDis` must come from `PitchMatchup.BatterContact/BatterDiscipline` (already populated), and the spec must add the `book.BeginPa` call in that branch. |
| Q4 | Out of scope for this pass. Keep wild pitches deferred behind the DTO flag as written. |
| Q5 | Yes. `PitcherArsenal` is a byte struct on `PitchMatchup`; the means and ρT are arithmetic at `BeginPa`. |
| Q6 | Yes, prior only (see B3). |
| Q7 | `BatterIntent` is a plain readonly struct (Kind, GuessType, GuessCell, Approach, ReadIsScripted, two bools, ScriptedLocAcc), not a union. Adding the Model fields with zero defaults is fine; `BatterIntentKind` has room. |
| Q8 | Confirmed. The branch right after `pitcher.NextPitch` precedes every shared draw (`DrawType`, zone, blur), so the neutral sequence is untouched. |
| Q9 | Yes. `PitchLook` is a readonly struct (Cue, ZoneProbability); add `static Hidden`. |
| Q10 | Flag only at launch. |

## Noted, no change requested

- §7.3 expects Perfect at about −.20 before clamps, which overshoots G3 on the first run by design. That's acceptable, since tightening is Pro Math's one-knob job, but G3 will fail on the first G-2b run. Say that in the G-2b gate line so it isn't read as a regression.
- §11.1 (re-band batting to the same idiom) is an owner decision (O1). The spec states it well.

## Scope for the revision pass

Edit `pitching_mode.md` in place:
- B1–B3, with the restated G1 claim and any constants they add to §8;
- C1 and C2;
- replace §12.1 with the answers above;
- update §9.3 (Q2) and §5.3/§5.4 (Q3, Q3b).

Don't restructure other sections. Keep edits targeted.
