# R1-MATH: fatigue and life-modifier contracts

**2026-10-25 · GPT 6 Astra Pro · Math design delivered for Claude Opus 5.5 review.** Base `fdbe17e` on `math/R1-MATH-gpt`. No production implementation, merge, season rebaseline or claim of arcade calibration. Values below are the author's recommendations; review is still required before implementation acceptance.

Authority: [R1-MATH brief](../../tasks/R1-MATH_gameplay_contracts.md), its **2026-10-13 banner**, [accepted R-1 Q03–Q05](../../review/r1/06_owner_questions.md), [gameplay core](../gameplay_core.md) §§1.3–1.4, [arcade](../arcade_baseball_overhaul.md) §§4.2/9, and [CS-M](college_28_game.md) §2.1. TEAM.md §1a transfers former Pro Math ownership to GPT and independent review to Opus.

**Path deviation:** the brief's `docs/design/pitching_mode.md` is archived. This owner-requested new file is the delivery location. Pitching centering, the waste bin and old G-8/Read-v2 work remain retired. No 13-3 brief or source file is edited. References to “G-8” here mean the retired batting slice, not current arcade art.

## 1. Decisions and boundaries

| Item | Recommendation | Activation boundary |
| --- | --- | --- |
| Fatigue | Replace regression toward 50 with multiplication toward zero, using the existing workload curve and rounding (§2) | Separate reviewed correction in attended simulation; not a neutral extraction |
| `ShortRestCapacityPenalty` | **Keep production 0.00 now.** Recommend **0.15** as the first attended-capacity candidate after correction, identically across tiers | Requires actual wiring and attended workload gates; this document does not turn D5 on |
| `OveruseInjuryRisk` | **Keep 0.00 for all tiers.** Do not adopt CS-M's .01 yet | No validated ordinary injury hazard, duration or persistence contract; requires a later injury brief and measured exposure |
| Needs/stress | One worst-source, at most **2 rating points**, game-start penalty; share its negative envelope with existing person levers (§4) | R1-EFFECTS through `EffectiveRatings` |
| Work ethic | Actual practice credit multiplier **0.8–1.2**, neutral at 50; retain the .25 practice-fraction cap (§5) | Opus development implementation within R1-EFFECTS |
| O1 | Average at the rating line; skilled/poor approximately ±.100 OPS at default difficulty (§6) | Arcade bot/assist calibration, not resolver probability edits |

`LeagueSimulator` **never constructs or calls `PitcherFatigue`**. Its complete-game abstraction stays that way. Sharing `EffectiveRatings` must accept explicit workload context: macro supplies fatigue disabled / multiplier 1. Do not infer pitch counts from PA, apply the attended short-rest factor as a macro rating dock, or add macro substitutions. Macro/attended parity means the same game-start life projection, not identical in-game fatigue or identical outcome engines.

13-2 D2 remains stuff + control after tier/person baking, ties by stamina descending then ordinal player ID, before in-game fatigue. D5 currently counts short-rest starts but does not apply capacity in either sim. Do not describe changing the constant as implementing the effect. R1-EFFECTS must explicitly reconcile the new combined person/life bake with D2 and refresh/re-Initialize parity; it must not silently make gear, fatigue or injury risk part of starter ranking.

## 2. Fatigue contract

Inputs: rested effective stuff/control `r ∈ {0,…,100}`, stamina `s ∈ {0,…,100}`, nonnegative outing pitch workload `p`, PED eligibility already resolved by its owner, fatigue-enabled flag, and one short-rest flag captured at the actual start. Stuff/control inputs are the final pre-fatigue values from the shared stack, never a previously fatigued output. Stamina is unchanged in returned ratings.

Keep existing capacity and workload constants:

- `sPED = ped ? min(100, floor((3*s+1)/2)) : s`; `C = 70 + 0.5*sPED`.
- With a later enabled short-rest penalty `a`, `Cstart = C*(1-a)` only for a short-rest **starter**, otherwise exactly `C`. Apply once, after PED-adjusted stamina; never reduce stamina as well. With current `a=0`, capacity is exact identity.
- `x=p/Cstart`. If disabled or `x<=0.6`, `m=1`; otherwise `m=max(0.5, 1-0.25*((x-0.6)/0.4)^2)`.
- Charge actual pitches with existing `AddPitch`; retain 3.9 workload per NPC PA in the legacy attended abstraction. No extra charge for a rendered pitch, skip, replay, refresh or reload. A reliever gets a fresh outing counter; reliever recovery is outside this start-date contract.
- **New effective value:** return `r` directly when `m>=1`; otherwise `clamp(roundAwayFromZero(r*m),0,100)`. No RNG draws. Keep the existing bullpen pull threshold **0.85** and pull timing; evaluate any resulting behavior in the attended gates rather than tuning it here.

Proof: `0<=m<=1`, so `0<=r*m<=r`; rounding a nonnegative value no greater than integer `r` cannot exceed `r`. The curve is nonincreasing in workload; multiplication and rounding are monotone. Larger capacity cannot cause more fatigue at fixed `p`. Thus neither weak nor average nor elite pitchers improve as workload increases. Quantization causes plateaus; strict degradation on every pitch is neither required nor possible. Zero-rated ability stays zero. At `x=1`, `m=.75`; the floor starts at `x=.6+.4*sqrt(2) ≈1.165685`.

| Rested rating | Current at m=.75 / .5 | Proposed at m=.75 / .5 |
| --- | --- | --- |
| 0 | 13 / 25 | 0 / 0 |
| 30 | 35 / 40 | 23 / 15 |
| 50 | 50 / 50 | 38 / 25 |
| 85 | 76 / 68 | 64 / 43 |
| 100 | 88 / 75 | 75 / 50 |

The stronger deterioration above 50 is intentional and must be reviewed; this is not merely a bug fix with identical strong-pitcher output. Against an all-50 batter and defense, the unchanged resolver gives expected OPS **.727088** fresh, **.792308** at capacity and **.866413** at the floor for an initially all-50 pitcher. These are isolated matchup results with a pitcher held in the game, not whole-game/late-inning promises: normal bullpen pulls often prevent capacity/floor exposure. No 30% scatter expansion is promised; that retired pitching-mode claim never followed from the old formula.

**Integration order:** preserve existing tier clamp → person clamp, equipment/rust/rivalry order and PED rounding. Add §4 by merging the person delta before its existing clamp. Apply fatigue once after all pre-fatigue pitcher modifiers; compute capacity once from stamina/PED/rest. The shared stack's pure extraction and this behavior change must be separately reviewable. Arcade §3.1's “reused as-is” is superseded by this correction only after review: arcade mappings must use the fatigued stuff/control as their capability inputs, with no second `m` multiplier on velocity or extra fatigue scatter penalty. G-1/G-3 must prove the concrete arsenal/velocity/scatter mapping; this doc supplies no new physics constants. `m` remains available for workload display and manager pull decisions. No rating mutation is written back to base ratings.

## 3. D5: start dates, capacity and injury

Use calendar-date differences and **HS 4 / College 3 / Pro 3**, not CS-M's superseded College rest-4 proposal. A never-started available pitcher is rested; missing a day is not a multiplicative daily penalty. Fully rested starts receive factor 1; every short-rest start receives the same factor .85 in the proposed nonzero experiment, regardless of missing days. The series no-repeat filter, availability, deterministic fallback and legal forfeit rules do not change.

At stamina 50, normal capacity is 95; candidate short capacity is **80.75**, comfort **48.45** versus 57. With PED stamina 75, capacity is **107.5**, candidate **91.375**. At stamina 100, candidate capacity is **102** versus 120. Healthy College Wed/Sat and weekly HS fixtures have zero short starts; Pro Tue/Thu/Sat remains 43/21/0/0 with zero short starts. Daily depleted fixtures exercise the effect. Do not weaken healthy staff to manufacture short-rest exposure.

**Why recommend .15 conditionally:** it is a transparent, moderate 14.25-pitch reduction at stamina 50 and has a proven monotone direction under §2. That supports a candidate, not a claim of calibrated injuries or season realism. Future attended comparison: penalties {.10,.15,.20} on matched workloads, retain .15 unless predeclared gates reject it; freeze the choice before holdout. Keep both knobs zero in 13-3's neutral calibration population, preserving its bands/new Pro guard. A later nonzero macro injury policy can change seasons and must have its own reviewed affected-population evidence; it cannot be slipped into this correction.

**Why risk remains zero:** .01 means an expected one extra injury per 100 short-rest starts, not 1% of a roster or season. With an independent ordinary hazard `q`, total probability would be `q+(1-q)*.01`, not `q+.01`; absence duration and repeat exposure decide the actual cost. Neither the duration distribution nor that composition has been validated here. At zero, take **no draw**, create no absence and touch no RNG stream. A future risk brief must specify ordinary-hazard composition, duration, game timing, one persisted decision per actual start (retries/reloads cannot reroll), deduplication with existing absences, and an independent persisted injury stream. Forfeit/unused game 3/relief-only appearances are not short-rest starts. No injury sampling was performed in this delivery.

## 4. Needs/stress: one game-start contract

Life owns needs and stress. The Life/data seam supplies a committed, versioned snapshot for the avatar and game identity before either game engine starts; Baseball consumes a value DTO, never a Life loop or per-PA database query. High needs are good (`NeedsState.Full=100`), high stress is bad. Clamp finite inputs to [0,100]; absent need values default to 100, absent stress to 0. Reject nonfinite input at the producer boundary with a diagnostic and use the neutral component; do not pass NaN into rating math.

Let `n` be the minimum of hunger, sleep, hygiene, social and fitness. Define `dNeed=clamp((50-n)/50,0,1)`, `dStress=clamp((stress-50)/50,0,1)`, `severity=max(dNeed,dStress)`, and integer `dock=roundAwayFromZero(2*severity)`. The broad healthy plateau (all needs ≥50 and stress ≤50) is exactly zero. Maximum is 2; benefits above healthy are zero. All five needs matter, but multiple bad needs and stress **do not add**. Stress already accelerates need decay, so summing them would count the same strain again. Ties in the displayed winning cause use hunger, sleep, hygiene, social, fitness, stress order; numeric output is order independent.

At need minimum 50 / 37.5 / 12.5 / 0 (stress 0), docks are **0 / 1 / 2 / 2**. Stress 50 / 62.5 / 87.5 / 100 gives the same steps with healthy needs. This integer design intentionally preserves plateaus rather than pretending sub-point effects reach byte ratings.

Apply to the avatar's **power, contact, stuff, control only**. Discipline, stamina, team defense, fielding, speed, arm, availability and base development are unchanged by this projection. Avoid a second stamina penalty for the same lost sleep. An absent projection leaves NPCs and all existing neutral fixtures alone.

For the existing confidence delta on power/stuff and happiness delta on contact/control, let `d` be its already-quantized [-2,2] value. Replace it at the person stage with:

`combined = max(d,0) - max(max(-d,0), dock)`.

Then perform the existing person-stage [0,100] clamp. At dock zero, `combined=d` exactly. A negative person delta and deprivation share their strongest penalty; e.g. `d=-2,dock=2` stays -2, not -4. Positive confidence/happiness remains present and can offset deprivation (`d=+2,dock=2` gives 0). For any original delta, the incremental change is between -2 and 0. Teamwork is not merged or changed. This intentionally uses a conservative common negative envelope even if the two causes differ; it requires no invented causal attribution for existing person rows.

`Active_Effects` must not encode a second field dock for a cause already represented by this projection (e.g. “sleep deprivation” as both lowered sleep and a separate rating effect). The producing feature must choose the state route or a tagged explicit effect route; an explicit same-cause effect must exclude every need/stress component attributed to that cause from this projection. Until a producer can supply that provenance, use the state route only for that cause. Distinct approved affair/clutch/PED/withdrawal magnitudes remain owned by L-5/S-2, not invented here. Healthy/no-effect extraction preserves their existing order. No direct gender, ethnicity or identity-position input is permitted.

Snapshot once per actual game, after committed pregame Life changes and before starter ranking/ratings consumption. Repeated initialization, watch/skip, save/reload and retry consume the **same game-keyed projection**, not a fresh bake from a later clock tick. An authorized pregame state update may replace the snapshot only before the start is committed. Implementation owns storage/schema and atomicity, coordinated with R1-SAVE work; this design creates no column. The R1-LEARN DTO reports raw sanitized inputs, winning cause, raw dock, original/combined person deltas, clamps and final ratings. UI displays these values rather than reproducing formulas.

The resolver sweep in §7 supports the two-point size: all-50 expected batting OPS falls **.016126**, allowed OPS rises **.010649**; worst sampled changes were .028716 and .015424. These are bounds on the tested grid, not a proof of global OPS limits or arcade behavior. The exact guarantee is the two-point cap and no added RNG. Same inputs give the same game-start ratings in attended and background avatar games.

## 5. Work ethic: actual practice only

Use `W=clamp(work_ethic,0,100)`; missing row defaults to 50. For each completed, credited practice interval of duration `h>=0`, weight it with `w(W)=1+0.004*(W-50)`, bounded [.8,1.2]. Record the work ethic at practice time; changing it at season end must not retroactively multiply a year's practice. No practice gives no credit. Accumulate fractional weighted hours without per-interval integer rounding; splitting one hour into four intervals cannot change credit. Neutral work ethic uses the existing unweighted-hours calculation exactly.

Let `H=sum(h*w(W))`. Feed the existing saturating conversion with that credit: `practiceFraction=.25*(1-exp(-.0005*H/.25))`, exactly zero for H=0. Do not multiply all development, existing ratings, potential, youth growth, health or jitter. The production conversion may need a fractional-hour overload; retain the old path when all weights are 1. The existing old-age practice mitigation remains part of this same practice term; no new decline scaler is added.

Because the conversion is increasing and concave through the origin, weighted credit in [.8,1.2] times actual hours gives a practice-fraction ratio within [.8,1.2] for nonzero practice, approaching 1 as the .25 cap saturates. At 365 actual hours and constant W=0/50/100, fractions are **.110584188 / .129522752 / .145888658**. The ratio concerns the practice term, not net annual stat change (which includes youth growth, aging, rounding and jitter).

Keep existing potential/base clamps and RNG draws/order. A missing-piece locked base rating remains exactly its locked value despite practice, age or jitter; consume that season's practice normally, with no deferred catch-up when unlocked. Compute/discard a blocked update if needed to preserve unrelated RNG continuation. Other unlocked ratings may benefit from the same legitimate practice. Tests must cover zero hours, neutral/missing W, split intervals, changing W during the year, cap saturation, potential, older players and a locked rating. Additional person discipline/composure field scalers remain deferred.

## 6. O1 is an arcade difficulty target

No revived pitch-cell model, waste bin or G-8 centering formulas. Tune human assists only, never macro resolver weights, opponent AI, base ratings or the underlying league bands. The numerical ±.100 OPS target governs; “40-to-70” and “±30 points” in older prose are qualitative comparisons, not a linear rating conversion or permission to add 30 points.

For default batting, compare visible-information average, skilled and poor input bots with the same-rated calibrated AI reference. For pitching use opponent OPS allowed: skilled is negative, poor positive. Average targets paired delta within **±.010 OPS**. At all-50, fresh, neutral context, target skilled/poor magnitudes **.070–.100**, with a **.110 outer acceptance ceiling** including statistical tolerance; at extreme ratings/fatigue require order and the ceiling, not an unattainable .100 gain at a saturated boundary. This .010 measurement allowance is not a larger nominal skill budget.

A “perfect” test bot must be labeled: the skilled calibration bot sees only player-visible cues and legal input deadlines. An omniscient bot with future ball/contact/RNG knowledge is a separate diagnostic ceiling and cannot be the default target. Average/blind means a declared uninformed input policy matched to the game's assist design, not proof that every possible random input distribution is neutral. Freeze bot policies, assist values, timing units and observation limits before holdout. Compare tired bots to the **same tired reference**, not the fresh baseline; O1 does not cancel fatigue.

G-3/G-9 own the actual bot/physics implementation and measurement. This delivery signs an evaluation contract for review, not assist constants or a claimed O1 pass. Arcade's AI calibration in §9 remains separately mandatory, including full-season fatigue/bullpen behavior. Fielding effects need their own metrics; this OPS contract does not claim to measure them.

## 7. Reproducible evidence and implementation gates

**Run here, once:** `dotnet run --project .scratch-r1math/MathProbe.csproj -c Release` in the math worktree, Windows .NET 8. The untracked project links the unchanged `AtBatResolver.cs`, `RngState.cs` and `PitcherFatigue.cs` at `fdbe17e`; proposed transforms live only in scratch code. Program SHA-256: `F8C2AD27B2B1274A90A9BF5E494F4BF25527D7E3891FC078758A29A0CF97A27F`. No game DB or live save. The following parameters and formulas fully specify the numerical experiments for reconstruction; scratch files are deliberately not shipped.

| Experiment | Exact sample and acceptance | Result |
| --- | --- | --- |
| Fatigue grid | Each r=0…100, s=0…100, PED off/on, penalty {0,.15}, integer p=0…300: **12,282,004** cases. §2 formula; integer bounds/nonincrease/fresh identity exact | **0 violations** |
| Resolver life effect | Cartesian batter power/contact/discipline, pitcher stuff/control, defense each in {0,30,50,85,100}, batter PED off/on: **31,250** matchups. Compare neutral with separate clamped two-point power/contact and stuff/control docks; no sampling | No reversed OPS direction within 1e-12. Max batting loss **.028716** at 50/50/0 vs 0/100, defense 0, PED on; max allowed rise **.015424** at 85/30/100 vs 30/30, defense 0, PED on |
| Life envelope | Effective minimum need 0…100 × stress 0…100 × person delta -2…2: **51,005** cases. Tests §4 bounds and dock-zero identity | **0 violations**; reduction to minimum is exact for this max-based formula |
| Practice | Integer hours 0…2000 × W 0…100: **202,101** cases. Zero/neutral identity exact, bounds within 1e-12 | **0 violations**; nonzero fraction ratios **.800160–1.199760** |
| Fatigue resolver sampling | `RngState` seeds **129,130,131,132**, **250,000 paired PAs per seed per arm** (1M pairs). All-50 batter/defense; fresh 50/50 pitcher vs capacity-rounded 38/38. Reuse each uniform draw for both distributions; cumulative seven-outcome sampler; no seasons | OPS deltas **.065667/.065353/.063891/.065286**, vs analytic **.065220**; maximum per-arm error vs analytic <.007, within a .010 reporting tolerance (a post-run sanity comparison, not a pre-registered acceptance test) |

Expected OPS is `(BB+1B+2B+3B+HR) + (1B+2*2B+3*3B+4*HR)/(1-BB)` for probability inputs (HBP is folded into Walk by the existing resolver). These are exact probability evaluations up to floating precision, not means of per-player OPS. The sampled fatigue check is a sanity check, not fitted constants, an injury study or proof of season calibration. No first-order approximation is used in the analytic OPS numbers. No retired centering residual is asserted. All scratch checks report **TOTAL violations 0**.

**Required later implementation gates (not run here):**

1. Exhaustive unit grid above, plus disability/fresh identity, exact comfort and floor boundary neighbors, fractional NPC workloads, invalid-input boundaries, mixed stuff/control, same-value replay, one short-rest application/PED ordering, and snapshot/reload/retry identity. Integer bytes/IDs/RNG state: **zero tolerance**; floating analytic values: absolute **1e-12** (capacity fixtures **1e-9**). No database query or allocation in warmed per-PA/per-pitch transforms.
2. MC and Arcade harnesses for shared ratings/fatigue; NeedsDecay/CoreLoop and development checks for Life/practice integration; Debug solution build. Preserve all existing assertions in §8. Compare base and changed binary on identical seeds/populations with neutral features; byte identity includes random continuation, not just totals. Default MC replay stays at the established 20 identical-seed runs when that integration gate is due, not 20 repeats of the large analysis matrix.
3. Attended fatigue/D5 analysis: training seeds **1–128**, holdout **129–256**; for each seed, tier and penalty {0,.10,.15,.20}, play **100 games** on one fixed spread-0 roster, and separately the matched spread-25 roster. Record roster generation seed=seed and game RNG seed=100000+seed per arm (each arm resets to the same start; diverging games need not remain draw-aligned). Healthy and forced-short-rest fixtures separate. Fix ordinary hazards off. Freeze the chosen penalty before inspecting holdout. Report workload, pull innings, bullpen use, OPS allowed, R/9, starter IDs and late-inning exposure; require exact selection legality, monotonic capacity/rating gates and unchanged neutral macro outputs. Empirical whole-game dominance is not required when substitutions differ. No season-band widening is authorized on a failed attended run.
4. O1 analysis: training **1–128**, holdout **129–256**; **2,000 PAs per seed per bot/reference per cell**, hence **256,000 per arm per holdout cell**. Cells: HS/College/Pro; symmetric ratings 0/30/50/85/100; fresh and m=.75 (exactly the same effective inputs per comparison), with asymmetric 30-vs-85 and 85-vs-30 stress cells. Keep tier world/park fixed within each comparison; declare venue and lineup fixture with the implementation commit. Record paired seed-level differences; report pooled OPS and **99% seed-cluster confidence intervals**, fixed bootstrap seed **60013**, **10,000** cluster resamples. The CI must fit average ±.010 and outer ±.110; all-50 skilled/poor point estimates must fit the target bands. Ambiguous confidence intervals mean more predeclared samples or a failed gate, never picking favorable seeds. Full-season AI-vs-AI calibration is separate from these PA cells.
5. If nonzero injury risk is later proposed, use separate training/holdout seeds 1–128/129–256 and at least **100,000 forced short-rest starts per chosen risk**; at q=.01 a ±.001 isolated frequency tolerance is about 3.2 standard errors. Zero-risk/rested cases must take zero draws. Add actual duration/ordinary-hazard/absence exposure studies before acceptance. This is a future test design, not evidence that .01 is approved.

## 8. Frozen guards and permitted differences

**Exact baseline set:** all **369 default MC assertions at `fdbe17e`** keep their predicates, thresholds, seeds and populations under this design; eight remain the owner-approved D6 integration reds: `tier-ladder`, `promotion-sweep`, `promotion-stream`, `promotion-avatar`, `promotion-record`, `development-arc`, `practice-seam`, `development-equilibrium` (one each). No test deletion, widening, relabeling a failure as a pass, or new historical output hash is authorized. New fixtures may be added. Subsequent 13-3 changes have their own brief/sanction and are not authorized by R1-MATH.

The guards especially relevant to this handoff are explicitly frozen:

| Guard | What stays frozen |
| --- | --- |
| `resolver` | All **13/13**: exact §5 baseline/elite/ace distributions and their existing tolerances, PED 90→100 / 40→60, 1,000-outcome determinism, 0 B allocation; 100,000-PA batch seed **0xD1A30D05**, existing slash/K/BB/HR bands and deterministic output. Resolver weights, normalization, seven outcomes and draw count unchanged |
| `season-pipeline` / `tier-ladder` | Macro shared-world vs Pro-only identity; tier population/denominator contract and every band below. Legacy PA **48384 / H 10969 / ER 5237** remains a historical 8-team guard, not resurrected as a v14 target. Interim 13-2 PA 194444 / H 43645 / ER 20527 is **not** a sanctioned guard. **Only 13-3** may record its new Pro guard; preserve it byte-exact once sanctioned |
| `starters`, `innings`, `capacity`, `lineups`, `worldgen-v14` | Existing **23/5/6/8/23** checks; rest 4/3/3, D2 order, refresh parity and rollback/retry evidence, no-repeat series fallback, dimensions and regulation length. D5's neutral constants/helper/no-draw assertion stays valid while production knobs are zero |
| `micro-analytic`, `micro-game` with fatigue disabled | Existing probability/matrix/neutral-policy contract; no empty effect, ledger or disabled-fatigue drift; preserve current RNG call order and all neutral rate/allocation/identity assertions |
| `micro-game` fatigue / `v4-bullpen-arsenal` | Capacity 70-stamina **105 clean /120 PED**, workload constants, .85 pull rule, accounting, deterministic replay and zero allocation. Existing late-inning OPS rise **+.030–.150**, fatigue R/G increase **≥.10**, starter average pitches **60–120** remain acceptance gates; no threshold replacement is granted here |
| `person-effects`, `rivalry`, `equipment`, `availability` | Existing empty-ledger/no-row identities, person ±2 caps, tier-then-person clamp order, gear/rust/rivalry/PED order, absence rules and zero-allocation assertions |
| `development-curve`, `development-stream`, `practice-seam` and related development guards | Existing no-practice/neutral-W results, RNG draws, potential/age/health/jitter rules and practice cap .25. Work ethic may affect only the explicitly nonneutral practiced population; locked bases stay exact |
| Arcade / Life / persistence | Existing ArcadeHarness rules, monotonicity and fixed-seed watched=skipped contracts, plus Phase 13 AI bands; NeedsDecay's existing curves; schema, transactional ownership and CoreLoop invariants. This work claims no repair of the separately recorded cross-platform arcade replay discrepancy |

Frozen tier bands, copied from the integration fixture (rates are fractions; runs are R/9):

| Tier | AVG | OBP | SLG | K/PA | BB/PA | HR/PA | R/9 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| HS | .296–.316 | .379–.399 | .493–.523 | .177–.207 | .109–.129 | .034–.042 | 6.4–8.0 |
| College | .284–.304 | .364–.384 | .473–.503 | .184–.214 | .103–.123 | .033–.041 | 5.8–7.1 |
| Pro | .240–.260 | .308–.325 | .395–.430 | .200–.250 | .075–.105 | .027–.037 | 4.2–4.8 |

**Expected changed behavior, not a baseline waiver:** enabled attended fatigue after comfort will change rating bytes, outcomes and potentially downstream RNG consumption through different game paths. New nonhealthy avatar projections and nonneutral actual practice also change their affected populations. Do not demand old byte-identical scores there; demand repeated runs of the new implementation agree and the unchanged acceptance predicates pass. If the preserved fatigue/attended rate predicates fail, report both baselines and return to Math/Opus for a separately reviewed adjustment; do not fold an unexplained change into 13-3. No current shipped save migration is performed. Implementation must version any new saved projection/weighted-practice state through its own schema brief and preserve neutral defaults.

## 9. Handoff

Deliverables: corrected fatigue formula/proof, D5 recommendation (production **0/0**, candidate capacity .15 and injury .01 deferred), small bounded life projection with anti-stacking semantics, actual-practice work ethic, O1 measurement contract, reproducible scratch evidence and frozen-guard ledger. Runtime implementation, full attended/arcade validation, nonzero workload activation and new persistence are still open, by design of this docs-only task. Scratch remains untracked; no `Assets/`, `Tools/` or `docs/tasks/13-3*` edits.

**Recommended next: R1-MATH review by Claude Opus 5.5.** GPT checks the 13-3 brief's Math sections when the owner sends it. This handoff does not authorize starting either implementation or a 13-3 review now.
