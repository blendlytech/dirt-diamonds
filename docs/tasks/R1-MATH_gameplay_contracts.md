# Task Brief: R1-MATH — Pitching centering, fatigue and accepted life modifiers

**Status:** authorized by owner acceptance of all R-1 recommendations (2026-10-10); implementation pending.
**Recommended model:** Gemini Pro Math
**Reviewer:** Claude Opus 5.5 (contract) and GPT 6 Astra Pro (implementation feasibility)
**Owner gate:** recommendations accepted; listed technical review gates still apply.
**Branch/worktree:** a dedicated slice branch and worktree under TEAM.md §3; never reuse an active sibling checkout.

## Context and dependencies

Read R-1 G01–G05 and existing G-8a/pitching rev 2. This gate precedes acceptance of G-2a/b and shared G-8 helpers.

Inputs: [R-1 code review](../review/r1/02_code_review.md), [gameplay audit](../review/r1/03_gameplay.md), [accepted decisions](../review/r1/06_owner_questions.md), current canonical specs and TEAM.md.

## Scope and outputs

Allowed paths: docs/design/pitching_mode.md, docs/design/gameplay_core.md, relevant G-2/G-8 briefs, and isolated scratch math experiments/harness fixtures agreed with the implementation owner. No production tuning in this design slice.

Derive centers for the terms actually applied conditional on zone/count/type, with a nonempty learned book; account for previous-type selection, CreditRef, HBP and waste. Specify an eighteenth waste observation bin or explicit excluded-observation policy and corresponding priors. Prove/measure reference neutrality and O1 caps across extreme ratings. Author a separate fatigue contract: fatigue must not improve below-average pitchers, and all-50 late-outing promises must follow from the actual model. Explicitly identify any approved neutral change, new baseline and migration; default neutral guards remain frozen. Calibrate one small capped needs/stress game-start projection shared by attended/background avatar play, with healthy identity and no double counting; work_ethic modifies actual practice development first. Additional person field scalers stay deferred.

## Hard constraints

Preserve Life/Baseball ownership and separate transactions; presentation submits intents/reads DTOs. No probability/topology changes unless explicitly covered by the accepted Math contract. No live saves or secrets. Apply project content boundaries. Coordinate shared schema/files with their assigned owner before starting.

## Tests and gates

Analytic counterexamples and reproducible seeded sweeps by count/type/zone; nonempty book, HBP, waste, 30/50/85 ratings and late outings. Compare a visible-information expert benchmark with omniscient ceiling diagnostics. Quantify first/second-order residuals separately. Deliver constants, tolerance/seed/sample-size rationale and an exact list of guards that remain frozen or require later sanctioned changes.

Run each relevant gate once after changes; diagnose failures. Commands: `dotnet build DirtAndDiamonds.sln -c Debug` and `dotnet run --project Tools/<named harness> -c Release`. Gate counts may grow; preserve existing assertions and explain additions. Math-only work runs its reproducible experiments, not unrelated runtime builds.

## Definition of done and handoff

- Spec, implementation and focused fixtures agree; gates pass and limitations are explicit.
- Commit on the slice branch, log progress, and hand off the commit to the named reviewer. No automatic merge/push.
- Stop at this slice boundary. Report what changed, gate results and the next assigned owner.

## Out of scope

Do not claim owner approval supplies a mathematical proof. No live resolver edits or arbitrary rebaseline. GPT implements the accepted G engine contract; Opus owns development changes.
