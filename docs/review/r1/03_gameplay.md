# R-1 gameplay review / G-1 attribute audit

Baseline: `2dec343`, branch `review/r1`, 2026-10-10. Review and proposals only. G-2/G-8 overlap is explicitly identified below; this report does not redesign either slice. Completed gameplay audit; verification results are consolidated in `02_code_review.md`.

## G-1: recomputed rating effects

I independently evaluated the seven normalized exponential weights in `Assets/Simulation/Baseball/AtBatResolver.cs:74-122,131-167`, with all other ratings at 50. No random sampling is involved. AVG = hits/(1-BB), OBP = hits+BB, SLG = weighted total bases/(1-BB).

| Attribute | +10 OPS change | +20 OPS change | Verdict on gameplay_core §1.1 |
| --- | ---: | ---: | --- |
| Power | +.033528 | +.070525 | Correct to stated precision |
| Contact | +.050455 | +.102851 | Correct |
| Bat discipline | +.010892 | +.022430 | Correct |
| Pitch stuff | -.045475 | -.089379 | Correct |
| Pitch control | -.006483 | -.012395 | Correct |
| Team defense | -.026512 | -.052490 | Correct; this is a whole-lineup mean, not one player's +10 |

Baseline: AVG .247253, OBP .315000, SLG .412088, OPS .727088. K .225, BB .090, HR .032. The prose should call discipline/control **small OPS levers**, not inert: +20 discipline raises BB to .110767 and +20 control lowers BB to .073534. OPS understates control's walk-prevention value. Weights changing in response to that wording would require a separate Math-owned experiment and explicit guard decision.

### Remaining attributes and modifier composition

- **Stamina:** `PitcherFatigue.cs:16-18,49-54`: capacity = 70 + .5 × effective stamina; +10 gives five pitches of capacity, not +10 pitches. PED multiplies the rating, clamps at 100, then maps to capacity. It does not multiply capacity by 1.5. Stamina/fatigue is micro-only (`TierEffects.cs:7-8`); background stamina value is not equivalent to attended stamina value.
- **Confidence/happiness/teamwork:** caps ±2 at `PersonEffects.cs:57-94`. Confidence changes power/stuff; happiness contact/control; teamwork defense. Integer rounding creates plateaus. Zero rows/neutral-50 rows are intended identity cases. Needs/stress are absent from the resolver inputs, and the baseball person projection includes only these three levers. This verifies the reported field disconnect; life consequences can still affect availability, development or events indirectly.
- **Tier:** `TierEffects.cs:50-59` really has HS -20, College -16, then three minor tiers, MLB 0. Bat discipline is unchanged. The future three-tier mapping is planned, not already shipped.
- **Equipment:** `EquipmentEffects.cs:31-46` gives 0/2/4/6; hitter power AND contact, pitcher stuff AND control. It is not a single arbitrary rating slot. Discipline and stamina pass through.
- **Composition:** tier then person at load (`MicroGame.cs:482-487`), gear/rust before pitching fatigue (`MicroGame.cs:650`), batter gear/rust and matchup rivalry at PA resolution (`MicroGame.cs:983-1002`), then PED power in the resolver (`AtBatResolver.cs:134-139`). A flat layer table must distinguish pitcher and hitter order, clamping, and injury rust.
- **Other person attributes:** `DevelopmentManager.cs:433` uses practice fraction and baseball ratings/potential, not work_ethic. Person discipline is distinct from bat discipline. Adding both to the same read scaler risks double counting; do not treat them as synonyms.
- **Read branch:** `PitchChain.cs:960-1099` uses .4 type/.6 location read quality, discipline-dependent conviction/threshold, and contact both in contact probability and quality. This is separate from the normalized neutral anchor. The large shipped A0 spread is an existing G-8 issue, not an invitation to tune the neutral resolver in R-1.
- **Health, recklessness, interest, detection and money:** these are career/life/economy inputs rather than extra resolver dimensions. Health also affects development and retirement; baseball_interest and age gate succession (`HeirGenetics.cs:61`, `CareerManager.cs:892,980`). Adoption official at 18 versus heir eligibility at 19 is intentional, not a conflict.

## Pitching rev 2: implementation blockers and test obligations

### G01 — high: multiplying the centered zone term by in-zone breaks centering

`pitching_mode.md:268-269` defines aZ = 2 L (I-p)/S. `:122-128` then uses aZ·I in strike/play/quality. For Reference, E[aZ]=0 but **E[aZ I] = 2 L p(1-p)/S**, not zero. At p=.5, L=.45, S=.5 it is .45. Even at p=.5 this remains, so it is not merely the weak type correlation acknowledged at `:164`. Out-of-zone xBall also samples a conditioned term. The claim at `:193` is insufficient for the outputs actually tilted. Required before G-2a/b acceptance: Math derives the conditional centers for each applied term and measures reference drift with a learned/nonempty book, separately by count, type and zone. Zero book alone cannot test it. G-8 must not copy this centering claim blindly.

### G02 — high: the fatigue gate contradicts the retained fatigue model

`PitcherFatigue.cs:98-100` computes 50 + (rating-50)×m. Thus effective 50 stays 50 at every fatigue level, while effective 30 becomes 40 at m=.5: a tired weak pitcher improves. G-2 explicitly retains that formula (`pitching_mode.md:524`) and its scatter formula only reads effective control (`:68-74`). It cannot produce the promised 30% scatter expansion for a fatigued 50 (`:87,381`) or a guaranteed all-50 late-outing deterioration (`:449`). HS/College tier shifts make the reversed effect especially relevant. Required: separately brief a Math-approved fatigue contract and guards; either revise G9 to reflect the existing model or authorize a human-path fatigue term. Do not silently change neutral calibration.

### G03 — high: waste is an eighteenth result but the learning table has seventeen bins

`pitching_mode.md:56,337` allows executed cell 17. `:215,232` specifies cellByBucket[4][17] and unconditional row[observed] += 1. There is no stated waste mapping or exclusion. Literal implementation indexes beyond the row. Define waste observation handling before G-2b, including its contribution to zone counts and CreditRef. Add a waste-at-every-count fixture; the fix must not silently teach that waste is a hittable ring cell.

### G04 — medium: several exactness claims need proofs the spec does not supply

- `CreditRef[sitCell]` is only 17 values (`:275`), but the executed mix depends on count/type/scatter. Specify which distribution it averages and prove the conditional mean for each count; a single table cannot simply be assumed to center all count-dependent distributions.
- `ExecCenter` uses prior previous-type usage at nonzero counts (`:191`). Surviving to a count selects previous pitches by their ball/strike/in-play probabilities; independent type draws do not imply the previous type conditional on that count has the unconditional usage distribution. Quantify this separately from the Jensen residual.
- HBP ends the PA immediately (`:515`) whereas waste-mixture centering treats waste as one Ball (`:149-156`). First-order equality before HBP does not establish equality after it. Re-run all reference statistics with HBP enabled, not just G-2a without the edge feature.
- Clamped exponents bound per-pitch effects, not automatically ±.100 final PA OPS. The first-pass estimate itself predicts -.20 (`:432`). Gates over pitcher 30/50/85, batter extremes, arsenal extremes, counts and fatigue are essential. Do not present estimates as established outcomes.
- The perfect bot reads hidden book state (`:405-418`) unavailable to a player before the pitch (`:286`). It is a ceiling diagnostic, not evidence an expert using the visible clues can attain that ceiling. Add a visible-information bot/playtest benchmark.

### G05 — medium: tier behavior and guard terminology are wrong

`pitching_mode.md:420,534` lowers HS batter discipline, but `TierEffects.cs:52` leaves every batter rating unchanged. `pitching_mode.md:445` calls PA 48384/H 10969/ER 5237 a resolver guard; it is the **season-pipeline** guard, sanctioned for replacement at 13-3 (`.agents/skills/run_monte_carlo_batch/SKILL.md:12-13`). Separate the stable resolver invariant from the topology-dependent season baseline in the eventual G-2 brief.

## Recommendations on the five Phase G decisions

1. **Skill band — already decided:** retain O1, about ±.100 OPS around the relevant ratings-only baseline for both modes, with blind/average play near baseline. Treat the 40-to-70 analogy as intuition, not a second numeric gate: simultaneous rating changes are nonlinear and depend on position. Run the G-8 blind reader across priors/counts, not only uniform guesses. No new owner vote needed for O1.
2. **Needs/stress:** accept a small, capped game-start effect, applied consistently to attended and background avatar games. Preview the cause and magnitude. Healthy/no-effects must be exactly identity. Math chooses constants; avoid adding several overlapping penalties for the same sleepless night.
3. **Other person stats:** first add work_ethic to development as a bounded modifier of actual practice. Defer person discipline/composure as extra on-field levers until the existing three and needs are legible. Do not add a thirteenth stat merely to name a pressure hook.
4. **Park factors:** off at launch. Distinct venue art and shared functional screens satisfy the approved identity without another calibration dimension. Arithmetic-mean-one factors alone would not guarantee league-rate neutrality after matchup/PA weighting.
5. **Fielding moments:** defer from 1.0. They require a new outcome/runner interaction and an explicit exception to listed-position-is-story-only (`GAME_VISION.md:166`), not just another UI prompt. Keep position-specific presentation while leaving fielding outcomes rating-driven.

## Gameplay additions proposed for owner/Opus triage

Prioritize a practice mode with the same inputs/reveal and no career consequences, visible game-start modifier breakdown, and a short explanation of why a pitch succeeded. Keep these bounded UI features ahead of season-long batter books, park physics or manual fielding. Detailed ranked proposals and ownership are in `04_proposals.md`.
