# Task Brief: R1-SAVE-1 — Preserve completed play at save boundaries

**Status:** authorized by owner acceptance of all R-1 recommendations (2026-10-10); implementation pending.
**Recommended model:** Claude Opus 5.5
**Reviewer:** Gemini Pro
**Owner gate:** recommendations accepted; listed technical review gates still apply.
**Branch/worktree:** a dedicated slice branch and worktree under TEAM.md §3; never reuse an active sibling checkout.

## Context and dependencies

13-0 deterministic IDs; reconcile with the current schema branch before editing shared files.

Inputs: [R-1 code review](../review/r1/02_code_review.md), [gameplay audit](../review/r1/03_gameplay.md), [accepted decisions](../review/r1/06_owner_questions.md), current canonical specs and TEAM.md.

## Scope and outputs

Allowed paths: Assets/Core/, Assets/Simulation/Baseball/LeagueSimulator.cs, Assets/Simulation/Life/RelationshipGraph.cs, Assets/Data/, Tools/CoreLoopHarness/, Tools/MonteCarloHarness/, Tools/SchemaValidator/ and the matching shipped references.

Make Save/exit quiescent with respect to interactive workers and day processing. Flush every league's completed stat buffers before the save/checkpoint, and reject or await an in-flight operation through a Core-owned command. Relationship current/history dirty sets are acknowledged only after commit; a failed begin/write/commit retains retry state. Establish single-owner or whole-operation exclusion for pooled command parameters, execution, readers and transaction lifetime. UI submits intents and reads DTOs; it does not own the clock/save protocol. No save may report success while completed work exists only in memory.

## Hard constraints

Preserve Life/Baseball ownership and separate transactions; presentation submits intents/reads DTOs. No probability/topology changes unless explicitly covered by the accepted Math contract. No live saves or secrets. Apply project content boundaries. Coordinate shared schema/files with their assigned owner before starting.

## Tests and gates

Scratch-save parity against uninterrupted play for every day of the seven-day flush cycle; repeated saves are idempotent; injected relationship write/commit failure followed by retry/reload retains edges and history; save/read/close during an interactive final flush does not race or lose stats. Debug build, CoreLoop, SchemaValidator and MonteCarlo Release gates.

Run each relevant gate once after changes; diagnose failures. Commands: `dotnet build DirtAndDiamonds.sln -c Debug` and `dotnet run --project Tools/<named harness> -c Release`. Gate counts may grow; preserve existing assertions and explain additions. Math-only work runs its reproducible experiments, not unrelated runtime builds.

## Definition of done and handoff

- Spec, implementation and focused fixtures agree; gates pass and limitations are explicit.
- Commit on the slice branch, log progress, and hand off the commit to the named reviewer. No automatic merge/push.
- Stop at this slice boundary. Report what changed, gate results and the next assigned owner.

## Out of scope

Do not add recovery journals, RNG serialization or narrative choice persistence here; R1-SAVE-2 owns them. No changes to outcome probabilities, schedules or topology.
