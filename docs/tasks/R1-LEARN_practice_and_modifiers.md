# Task Brief: R1-LEARN — Practice mode and effective-rating explanations

> **2026-10-13 update (arcade decision, GAME_VISION v2.1):** attended games become true arcade physics ([arcade_baseball_overhaul.md](../archive/arcade_baseball_overhaul.md)). Practice, the batting cage and the pitch explanation now run on the **arcade engine** (`Assets/Simulation/Arcade/`) in an isolated session, not the G-2/G-8 policies. Inputs/reveal DTOs come from the arcade slices (G-2/G-5). The no-manual-fielding line is overridden: fielding is assisted and in launch scope.

**Status:** authorized by owner acceptance of all R-1 recommendations (2026-10-10); implementation pending.
**Recommended model:** GPT 6 Astra Pro (engine DTO/session seam), then Claude Sonnet (UI)
**Reviewer:** Gemini Pro; Claude Opus merge gate
**Owner gate:** recommendations accepted; listed technical review gates still apply.
**Branch/worktree:** a dedicated slice branch and worktree under TEAM.md §3; never reuse an active sibling checkout.

## Context and dependencies

R1-MATH and stable G-2/G-8 input/reveal DTOs; UI follows 15a/15b. Separate engine and UI commits/worktrees.

Inputs: [R-1 code review](../review/r1/02_code_review.md), [gameplay audit](../review/r1/03_gameplay.md), [accepted decisions](../review/r1/06_owner_questions.md), current canonical specs and TEAM.md.

## Scope and outputs

Allowed paths: Assets/Core/, Assets/Simulation/Baseball/ input/session seams, Assets/Data/ DTOs, Assets/UI/ batting/pitching screens, relevant scenes and targeted harnesses.

Create isolated non-career practice using the production policies/resolver and its own RNG/state. Reuse real controls/reveal feedback; never write career stats, funds, flags, needs or RNG. Show chosen/executed/read/result after resolution without exposing hidden competitive book state before input. Add a read-only game-start effective-rating DTO with base rating, ordered modifier sources, clamps, fatigue and final values; UI displays it instead of recomputing math. Clearly distinguish missing-piece base locks from temporary boosts.

## Hard constraints

Preserve Life/Baseball ownership and separate transactions; presentation submits intents/reads DTOs. No probability/topology changes unless explicitly covered by the accepted Math contract. No live saves or secrets. Apply project content boundaries. Coordinate shared schema/files with their assigned owner before starting.

## Tests and gates

Snapshot career state and streams before/after practice: byte/state identity. Production/practice same-input same-local-seed parity. Modifier DTO matches actual final values at clamps and neutral cases. Build and relevant MC gates; Godot keyboard/controller practice, exit, reveal and save/reopen smoke tests. No standalone clone of the resolver.

Run each relevant gate once after changes; diagnose failures. Commands: `dotnet build DirtAndDiamonds.sln -c Debug` and `dotnet run --project Tools/<named harness> -c Release`. Gate counts may grow; preserve existing assertions and explain additions. Math-only work runs its reproducible experiments, not unrelated runtime builds.

## Definition of done and handoff

- Spec, implementation and focused fixtures agree; gates pass and limitations are explicit.
- Commit on the slice branch, log progress, and hand off the commit to the named reviewer. No automatic merge/push.
- Stop at this slice boundary. Report what changed, gate results and the next assigned owner.

## Out of scope

No manual fielding, park factors, new ratings or outcome tuning.
