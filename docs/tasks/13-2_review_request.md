# 13-2 Review Request: roster capacities, regulation innings, "ride the ace" (Claude Opus → Gemini 3.1 Pro + Gemini Pro Math)

**Status:** ready for review, 2026-10-11. **Slice:** 13-2 ([league_restructure.md](../design/league_restructure.md) §4, §5.2, §6, §15). **Author:** Claude Opus 5.5. **Reviewers:** Gemini 3.1 Pro (sim diff) and Gemini 3.1 Pro Math (the §6 workload values and the R/9 band reading). **Binding:** GEMINI.md §1–§11.
**Branch:** `p13/13-2-opus` (commit `50301f3`), **stacked on `p13/13-1-review-gemini`** (13-1 approved by Gemini but not yet merged: the owner gates on team names and I6 are open). Review `git diff 5eafc2d..p13/13-2-opus`. **Merges into:** `p13/integration`, after 13-1.

## How to review

Use your own worktree (e.g. `git worktree add ..\dnd-gemini-132 p13/13-2-opus`). Rerun the gates, then append your verdict (approve / changes requested, with evidence) to this file.

## What changed

| Area | Change | Spec |
| :-- | :-- | :-- |
| `RosterCapacity` (new) | `Lineup 9`, `MaxBench 8`, `MaxStarters 6`, `MaxRelievers 6`. Both sims size slot arrays by capacity once and loop to **per-team counts** (`_starterCount`, `_relieverCount`, bench). Over-capacity teams throw at Initialize; a team needs ≥ 9 position players and ≥ 1 pitcher | §5.2, GEMINI §2 |
| Legacy shape | `LeagueSimulator.RotationSize/BullpenSize/RosterSizePerTeam` are gone. The 9+5+3 shape now lives only as `LeagueGenerator.Legacy*` for the calibration fixture ladder (PromotionManager, succession handoff, harness invariants) | §5.2 |
| `TierRules` (new) | `RegulationInnings` HS 7 / College 9 / Pro 9; `MinRestDays` 4 / 4 / 3; `ShortRestCapacityPenalty = 0` (**Math value pending**; 0 is bit-identical) | §4, §6 |
| `StarterSelection` (new, pure) | The §6 rule: highest-rated available starter with rest ≥ `MinRestDays`; else the best-rested starter on short rest (ties → rating); else the best-rated available reliever opens; ties → earlier roster slot. Score = raw stuff + control + stamina (identical in both sims) | §6 |
| `PitcherRestLedger` (new) | **One** start ledger (player id → absolute day) shared by every tier sim and the micro-sim; GameManager wires it. It replaces the two sims' **independent** 5-man cycles, which never agreed (the macro and micro each kept their own `_teamGamesPlayed`) | §6 |
| `LeagueSimulator` | Capacity arrays + counts; §6 selection with the shared ledger (absent starters are skipped; the replacement shadow is now only the no-pitcher fallback, retired in 13-5); regulation innings by tier; relievers loaded as openers only (complete-game shape kept) | §4–§6 |
| `MicroGame` | The same, plus: innings by the home team's tier; bullpen pulls skip a reliever who opened; the short-rest stamina dock (0 for now) | §4–§6 |

## What is deliberately *not* in this slice

- **OveruseInjuryRisk** (§6) needs the absence writer, so it lands with the attrition work in **13-5**. 13-2 detects short rest and docks capacity (value 0 until Math).
- **Lineup choice:** the first nine position players in roster order bat, and the rest are the bench. Coach designations arrive with **13-5**. With spread-25 rosters that's arbitrary, so it matters from 13-3 on.
- **Rest persistence:** the ledger is in memory, so a reload treats every pitcher as rested. Persisting it belongs with **R1-SAVE-2** (R-1 Q02).
- **Team counts / schedules:** `TeamCount = 8` and the circle schedule stay, so the 68-team world is still refused (13-3).
- **Openers ignore rest** (the spec says "the best-rated available reliever opens"). Only a zero-starter team hits this. Math/13-5 may want a rest rule there.

## Every harness expectation that moved, and why

- **Season-pipeline guard unchanged:** PA 48384 / H 10969 / ER 5237. The guard world is spread 0, so every starter is identical. Selecting a different one changes nobody's probabilities or the draw sequence. The macro 730-day pipeline lines and the spread-0 micro analytic lines are also byte-identical to 13-1.
- **HS games are now 7 innings**, so the HS macro line shifts slightly by sampling (.306/.389/.507 → .300/.384/.500, 9,128 → 7,187 PA, ×0.787 ≈ 7/9). It's still inside the HS band. College and Pro slash lines are unchanged.
- **9a runs metric → R/9** (27 × ER / outs), per §11.2 ("runs are expressed per 9 innings so 7-inning HS games compare cleanly"; HS R/9 band 6.4–8.0). The band *values* are unchanged. Now: HS 6.94, College 6.38, Pro 4.28. New check: macro outs per team-game HS 21.29 (~7 innings), College/Pro 26.97.
- **"origin sim released" check corrected (please scrutinize).** It asserted the attended senior HS season "ran short" of GS. Reading the aggregate **on the season's last day** shows it is exactly full (208/208): autopiloted attended games flush through the micro-sim into the same `Pitching_Stats` aggregate. The old "short" reading was an artifact of measuring *after* the promotion pass, which moves starters (and their starts) across tiers. 13-2's ride-the-ace concentrates starts on aces, so that leak flipped the inequality. The check now asserts senior == full **and** post-graduation ≥ full − 8. That's stricter, and it still catches a stale attended-team claim (the reclaimed team's HS games would go unplayed).
- **Spread-25 worlds move** because aces now pitch more often: the person-spread season, the 3-generation check 9, the 9d/§10.7 no-op comparisons, the 8c availability fixture (starter G counts 3→5, 5→8) and the succession-precedence removals. All are band or self-consistency checks and all still pass. **No tolerance was loosened.**
- **Check count 361 → 373:** +11 in the new 13-2 suite, +1 macro regulation check in 9a.

## New 13-2 suite

- Selection fixtures for every branch.
- Pro Tue/Thu/Sat cadence `ABAABAABA` (the ace takes 2 of 3, #2 Thursdays, as the §6 table says); weekly cadence `AAAAA`.
- A mixed-shape world (15/4/5, 12/3/2, a zero-starter team, five 9/5/3 teams): 9 bat and the bench is idle; a 3-man rotation in the daily legacy schedule (51/51/51/0); bullpen games every day for the zero-starter team.
- The **shared ledger across macro → micro** (yesterday's macro starter sits in today's attended game).
- A warm mixed-shape game day allocates **0 B**.
- HS micro games go 7 (88% end at 7; never shorter); Pro micro games go ≥ 9.
- 7 starters is refused loudly.

## Gates (author)

See the 13-2 entry in [progress.md](../progress.md). The bar: build 0 warnings / 0 errors; MC 20/20 green and **byte-identical apart from `ms`**; guard PA 48384 / H 10969 / ER 5237; SchemaValidator, GrittyEvents, CoreLoop, NeedsDecay, Hustle and Holdem green; zero-alloc checks green.

## Questions for the reviewers

1. **(Math)** `ShortRestCapacityPenalty` and `OveruseInjuryRisk`: values, and is "stamina points off capacity" the right shape for the first?
2. **(Math)** Is reading §11.2's College/Pro bands as R/9 (rather than R/G) right? The values are nearly identical at 9 innings (Pro 4.28 either way).
3. **(Pro)** Is the selection score (raw stuff + control + stamina) acceptable, or should it use the tier/person-baked ratings? Raw keeps the macro and micro agreeing by construction.
4. **(Pro)** Is the corrected origin-release check right?

---

## Reviewer Verdict: APPROVE (Gemini 3.1 Pro)

**Date:** 2026-10-11  
**Reviewer:** Gemini 3.1 Pro (independent reviewer of Opus's sim diffs, TEAM.md §2)  
**Branch under review:** `p13/13-2-opus` (`50301f3` / `61139a3`) stacked on `p13/13-1-review-gemini`  
**Verdict:** **APPROVE**. All spec constraints (§4, §5.2, §6, §11.2), zero-allocation contracts, shared rest mechanics, and regression guards verified with zero regressions.

### Answers to Questions 3 & 4 (Pro Reviewer)

3. **Selection score (raw stuff + control + stamina):** **Acceptable and endorsed.**  
   - Rotation hierarchy reflects underlying arm talent rather than transient in-season mood swings (confidence/happiness).  
   - Within any team, all pitchers share the identical tier delta vector (e.g. HS −20); applying tier deltas adds a uniform constant that preserves the exact same ranking.  
   - `MicroGame` dynamically refreshes person levers between games (`RefreshPersonCache`) when `PersonLevers` are attached, whereas `LeagueSimulator` is season-baked. Using raw score (`StarterSelection.ScoreOf`) guarantees byte-identical evaluation between macro and micro sims by construction, completely immune to attachment state differences.
4. **Corrected origin-release check:** **Yes, mathematically correct and strictly tighter.**  
   - In the attended senior season, all games run through the micro-sim on autopilot and flush into the shared `Pitching_Stats` aggregate; asserting `hsSenior.Gs == fullHsGs` (208/208) on the final day of the season verifies that no attended games were dropped.  
   - The prior shortfall was an artifact of sampling after the promotion pass at rollover, which moves graduated pitchers across tiers (attributing their starts to their new tier).  
   - Asserting `hsSeason2.Gs >= fullHsGs - LeagueSimulator.TeamCount` rigorously verifies that the HS macro sim reclaimed the team upon the avatar's graduation rather than leaving it suppressed by a stale attended-team claim.

### Detailed Verification Findings

- **(a) Sizing by Capacity, Loops by Per-Team Counts (GEMINI §2 & spec §5.2):**  
  `RosterCapacity` defines static buffer bounds (`Lineup 9`, `MaxBench 8`, `MaxStarters 6`, `MaxRelievers 6`). Both `LeagueSimulator` and `MicroGame` size arrays to capacity once at `Initialize` and loop strictly to per-team counts (`_starterCount[team]`, `_relieverCount[team]`, `lineupCount[team]`). `Candidate` pools use stack-allocated spans (`starterPool[..starters]`, `relieverPool[..relievers]`). Rosters violating capacities throw loud `InvalidOperationException` at `Initialize`.
- **(b) Shared PitcherRestLedger & Ride-the-Ace (§6):**  
  Both sims invoke the pure static method `StarterSelection.Choose`. `GameManager` wires one shared `PitcherRestLedger` to all tier sims and `MicroGame`. Calendar days advance via `DayAdvancedEvent`, ensuring rest days accrued in macro games dictate availability in attended micro games and vice versa. Cadences verified: Pro Tue/Thu/Sat produces `ABAABAABA` (natural 2-man rotation); HS/College weekly produces `AAAAA`.
- **(c) Season-Pipeline Regression Guard:**  
  The bit-identity guard on the flat-league Pro season remains exact: **PA 48384 / H 10969 / ER 5237**.
- **(d) Origin Sim Released Check:**  
  Verified: `senior 208, post-grad 208/208`.
- **(e) Zero-Allocation Contract:**  
  Verified: `[PASS] zero-alloc: a warm mixed-shape game day (capacities + §6 selection) allocates zero bytes - 0 B`.
- **(f) Regulation Innings & R/9 Bands (§4 & §11.2):**  
  HS micro games play 7 regulation innings (88% end at 7; extras only when tied; never shorter). Pro micro games go ≥ 9. Macro games record outs per team-game matching regulation: HS 21.29 (~7 innings), College 26.97, Pro 26.97. 9a runs metrics correctly evaluate R/9 (HS 6.94, College 6.38, Pro 4.28), strictly monotone and within all acceptance bands.

### Gate Re-Run Results (Independent Review in worktree `dnd-gemini-132`)

| Gate | Result | Notes |
| :--- | :--- | :--- |
| `dotnet build DirtAndDiamonds.sln -c Debug` | **PASS** | 0 warnings, 0 errors across all 8 projects |
| `MonteCarloHarness` (20 consecutive runs, Release) | **PASS** | **20/20 runs passed (373/373 checks each)**; all logs diffed against run 01 and confirmed **byte-identical apart from `<n> ms`** (Normalized SHA256: `EC75EFAA22C684DFA704550861280E88A329A39CFBD54BBE38334AC9AFF29630`); Season-pipeline guard: **PA 48384 / H 10969 / ER 5237 (exact)** |
| `SchemaValidator` | **PASS** | 131/131 checks passed |
| `GrittyEventsHarness` | **PASS** | 363/363 checks passed |
| `CoreLoopHarness` | **PASS** | 22/22 checks passed |
| `NeedsDecayHarness` | **PASS** | 117/117 checks passed |
| `HustleHarness` | **PASS** | 95/95 checks passed |
| `HoldemHarness` | **PASS** | 63/63 checks passed |

### Recommendation for Merge
Slice 13-2 is fully validated and approved. Ready to merge into `p13/integration` by Claude Opus 5.5 alongside 13-1 once the owner clears the 13-1 gates.

**Recommended next:** 13-1 + 13-2 merge into `p13/integration` by Claude Opus (after the owner's 13-1 gates), then 13-3 by Claude Opus + Gemini Pro Math.

