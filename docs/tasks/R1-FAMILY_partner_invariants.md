# Task Brief: R1-FAMILY — Partner invariants and eligible civilian pool

**Status:** authorized by owner acceptance of all R-1 recommendations (2026-10-10); implementation pending.
**Recommended model:** Claude Opus 5.5
**Reviewer:** Gemini Pro
**Owner gate:** recommendations accepted; listed technical review gates still apply.
**Branch/worktree:** a dedicated slice branch and worktree under TEAM.md §3; never reuse an active sibling checkout.

## Context and dependencies

Partner edge/flag repair can start on v13 after 13-0; identity/civilian/kin portion requires L-1 and must land before L-4 conception.

Inputs: [R-1 code review](../review/r1/02_code_review.md), [gameplay audit](../review/r1/03_gameplay.md), [accepted decisions](../review/r1/06_owner_questions.md), current canonical specs and TEAM.md.

## Scope and outputs

Allowed paths: Assets/Narrative/, Assets/Simulation/Life/RelationshipGraph.cs, Assets/Data/, related Core events, Tools/GrittyEventsHarness/, Tools/MonteCarloHarness/, Tools/SchemaValidator/ and marriage/lineage contracts.

Give Partner creation explicit eligibility/reclassification semantics: selected friend/ex/rival becomes Partner only if allowed; enforce exclusivity for both participants. Married flags and edges cannot disagree after an empty pool, rejected target or failed transaction. Preserve generic affinity semantics and relationship history. After L-1, provide the minimal civilian pool and IsKin/age/gender eligibility needed by L-4; L-5 extends temptation rather than introducing its prerequisite spouse population. Keep dating preference distinct from conception eligibility and preserve adoption for any couple.

## Hard constraints

Preserve Life/Baseball ownership and separate transactions; presentation submits intents/reads DTOs. No probability/topology changes unless explicitly covered by the accepted Math contract. No live saves or secrets. Apply project content boundaries. Coordinate shared schema/files with their assigned owner before starting.

## Tests and gates

Existing friend, rival and ex; already-partnered target; no eligible target; rollback/retry; reload graph/history; kin exclusion and supported conception/adoption paths for both avatar genders after identity exists. Debug build, GrittyEvents, SchemaValidator and MonteCarlo Release gates.

Run each relevant gate once after changes; diagnose failures. Commands: `dotnet build DirtAndDiamonds.sln -c Debug` and `dotnet run --project Tools/<named harness> -c Release`. Gate counts may grow; preserve existing assertions and explain additions. Math-only work runs its reproducible experiments, not unrelated runtime builds.

## Definition of done and handoff

- Spec, implementation and focused fixtures agree; gates pass and limitations are explicit.
- Commit on the slice branch, log progress, and hand off the commit to the named reviewer. No automatic merge/push.
- Stop at this slice boundary. Report what changed, gate results and the next assigned owner.

## Out of scope

No temptation magnitudes, explicit content or baseball retune. No generic reclassification of every relationship consequence.
