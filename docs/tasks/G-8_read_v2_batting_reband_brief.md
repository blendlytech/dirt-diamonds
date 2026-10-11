# Task Brief: G-8 "Read v2": Re-band the Batting Model

> **R-1 follow-up:** owner accepted the review recommendations. [R1-MATH](R1-MATH_gameplay_contracts.md) is the technical acceptance gate for shared centering, waste and fatigue contracts; [accepted decisions](../review/r1/06_owner_questions.md) settle product scope. Do not copy the flagged rev 2 equations without that review.

**Model:** GPT 6 Astra Pro (owner assignment, 2026-10-10) · **Reviewers:** Claude Opus 5.5 (design + merge gate), Gemini 3.1 Pro (independent sim-diff review, GEMINI.md), Gemini 3.1 Pro Math (constants) · **Owner gate:** the owner accepts the design; playtest after G-8b.
**Worktree / branch:** `git worktree add ..\dnd-gpt -b g8/read-v2 g8/read-v2-brief` (TEAM.md §3). Never commit to `main`; never push unless the owner says so.

---

## 1. The owner decision you're implementing

**Owner decision O1 (2026-10-10): re-tune batting** to the same skill band as the new pitching mode.

- **Ratings set the floor and the ceiling; player skill decides where you land between them** (GEMINI.md §11, gameplay_core §1.4).
- For an average-rated avatar, **poor vs excellent ≈ the 40-vs-70 rating gap ≈ ±.100 OPS around the ratings line**, capped. Skill can't make a 30 elite or ruin an 85.
- An **average player lands near the ratings line**, and a **blind guesser lands at the ratings line**. Attending your own games must never make you worse than the autopilot by default.

**Why:** the shipped Read model (A0 sweep, 20k PAs, all-50 matchup) gives perfect **1.898**, random **.439** and fooled **.101** OPS against a ratings line of **.727**. That spread is about 18× the target, and the blind guesser sits .29 below ratings.

## 2. What stays

- The player's three inputs and the interaction: guessed type, guessed 3×3 cell or "expect a ball", and the approach dial (at_bat_read_input_model.md §2). The UI and the reveal narration ("sat on it", "fooled") keep working, and `PitchResult.TypeOk/LocAcc` keep their meaning.
- **Invariant N:** the Neutral batter branch plus the Neutral pitch call keep their exact code and RNG sequence. The macro resolver and its guard (PA 48384 / H 10969 / ER 5237) and suites 5/6 don't move.
- The pitching mode's `Model` branch ([pitching_mode.md](../design/pitching_mode.md)) is a sibling and must not regress.

## 3. What changes

The `Kind == Read` branch of `PitchChain.SimulatePa` (and `ReadInputModel`) stops being an uncentered emergent-swing model and resolves through the **same tilt idiom** as the pitching mode:

- start from the neutral **location-conditioned** class rates;
- multiply by bounded `exp(x)` tilts driven by read quality;
- then `DrawBallInPlay(p*, q)` with a bounded `q`.

**Centering is the whole job.** Every term must be **zero-mean at the blind-reader reference** (random type guess, random cell or out guess, approach 0), so that reader lands on p* by construction. See the centering requirement in [the G-2 review](../review/G-2_pitching_mode_review.md), blocker B1. It applies here identically. Approach stays a real trade-off (patient ↔ aggressive) with zero expected value at its center.

**Design choice that's yours:** whether Read v2 *shares* the pitching mode's tilt machinery, with the player's read grade producing the same anticipation terms the book produces (`aT`, `aZ`, `aC`), or keeps its own term set. Sharing is elegant and keeps the two halves "one game", but justify it either way.

## 4. Read first

1. GEMINI.md (binding), docs/README.md, docs/progress.md, docs/TEAM.md, and the rule file `.agents/rules/` for the baseball engine.
2. [G-2_pitching_context_pack.md](G-2_pitching_context_pack.md): a compact, verified map of the resolver, pitch chain, Read model and fatigue, with measured numbers.
3. [pitching_mode.md](../design/pitching_mode.md) and [G-2_pitching_mode_review.md](../review/G-2_pitching_mode_review.md). The spec is at **rev 2** on branch `g2/spec-rev2`, with the review's B1–B3 and C1–C4 applied (see the review's "Resolution" section). Read it from that branch. **§3.5 (centering) and §4.4 (zero-mean anticipation terms) are the template for your own centering**: the "indicator minus reference probability" form carries over directly to a blind reader.
4. The code: `Assets/Simulation/Baseball/PitchChain.cs` (`SimulatePa`, `ReadInputModel`), `InteractiveBatterPolicy.cs`, and `Tools/MonteCarloHarness/Program.cs` (the A0 sweep and suite 7).
5. [at_bat_read_input_model.md](../reference/at_bat_read_input_model.md) (the shipped contract you're superseding) and [at_bat_presentation.md](../reference/at_bat_presentation.md) (what the reveal shows).

## 5. Slices

**G-8a: design (stop after it for review).** Write `docs/design/read_v2_batting.md`, self-contained. It covers:
- the term set and its centering, derived analytically;
- the constants table (name, value, range, rationale, "for Pro Math");
- how the reveal and `PitchResult` stay meaningful;
- the scripted-reader harness (perfect / random / fooled / approach sweep, plus a "partial" reader at about 50% correct) with target lines and gates: random ≈ ratings ± .020, perfect − random ∈ [+.080, +.130], random − fooled ∈ [+.080, +.130];
- back-of-envelope estimates that the first-pass constants land in band;
- the migration plan for existing A0 checks;
- open questions.

Then stop, hand off, and wait for Claude Opus 5.5's review.

**G-8b: engine (only after the design is accepted).**
- Implement on the Read branch with zero heap allocation and determinism through `RngState`.
- Replace the A0 sweep's band expectations with G-8a's gates, keeping the coherence checks (take never Foul/InPlay, swing never Ball, the grade reconstructs).
- Gates: `dotnet build DirtAndDiamonds.sln -c Debug` at 0 warnings / 0 errors; `dotnet run --project Tools/MonteCarloHarness -c Release` all green, with suites 5/6/7 and the resolver guard byte-identical; `Tools/CoreLoopHarness` green.
- Hand off for the Gemini Pro sim-diff review and Opus merge gate.

## 6. Constraints

GEMINI.md §1, §3 and §11 are binding: human branch only, struct policies with no boxing, named constants, and identity never touching the sim. Use `double` throughout (as `PitchChain` does). Don't touch the pitching `Model` branch beyond any shared helper the design justifies.

## 7. Handoff (end each slice with this, then stop)

```text
Done: G-8<a|b> → <files> · Branch: g8/read-v2 @ <sha>
Gates: <results, or "docs only">
Key decisions: <3–5 lines>
Open: <questions>
Recommended next: <G-8a: "design review by Claude Opus 5.5" | G-8b: "sim-diff review by Gemini 3.1 Pro, then Opus merge gate">
```

Then add one line to the **Session Log** in `docs/progress.md` (not the Owner decisions block).
