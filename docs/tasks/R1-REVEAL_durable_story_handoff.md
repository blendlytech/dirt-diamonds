# Task Brief: R1-REVEAL — Revelation before succession without playback dependency

**Status:** authorized by owner acceptance of all R-1 recommendations (2026-10-10); implementation pending.
**Recommended model:** Claude Opus 5.5 (Core/lineage), Gemini Pro (presentation seam)
**Reviewer:** Each reviews the other's boundary; Opus merge gate
**Owner gate:** recommendations accepted; listed technical review gates still apply.
**Branch/worktree:** a dedicated slice branch and worktree under TEAM.md §3; never reuse an active sibling checkout.

## Context and dependencies

13-6 events, L-1 identity, R1-SAVE-2 durable operations; extends L-3 and 16b. Core can be tested before real media exists.

Inputs: [R-1 code review](../review/r1/02_code_review.md), [gameplay audit](../review/r1/03_gameplay.md), [accepted decisions](../review/r1/06_owner_questions.md), current canonical specs and TEAM.md.

## Scope and outputs

Allowed paths: Assets/Core/, Assets/Data/, lineage/career event producers, Assets/UI/Cutscenes/ and relevant CoreLoop/MonteCarlo/Gritty fixtures.

Persist revelation facts, genealogy-aware notification payload and replay/ack status before an avatar swap. Championship, retirement, permanent ban and death all take the same idempotent progression route. Succession never waits for a cutscene callback. Queue overflow, skip, disabled media and reload retain a replayable notification; presentation only submits explicit intents. Later-generation Trailblazer identity/contact text reflects actual ancestry and available living contacts.

## Hard constraints

Preserve Life/Baseball ownership and separate transactions; presentation submits intents/reads DTOs. No probability/topology changes unless explicitly covered by the accepted Math contract. No live saves or secrets. Apply project content boundaries. Coordinate shared schema/files with their assigned owner before starting.

## Tests and gates

Each ending trigger with skip, queue overflow, unavailable assets, reload before/after commit and double delivery. Exactly one reveal fact and one succession; notification remains available; no hidden lineage exposed before the trigger. Build, CoreLoop, Gritty and MC gates plus Godot soft-pause/input recovery smoke test.

Run each relevant gate once after changes; diagnose failures. Commands: `dotnet build DirtAndDiamonds.sln -c Debug` and `dotnet run --project Tools/<named harness> -c Release`. Gate counts may grow; preserve existing assertions and explain additions. Math-only work runs its reproducible experiments, not unrelated runtime builds.

## Definition of done and handoff

- Spec, implementation and focused fixtures agree; gates pass and limitations are explicit.
- Commit on the slice branch, log progress, and hand off the commit to the named reviewer. No automatic merge/push.
- Stop at this slice boundary. Report what changed, gate results and the next assigned owner.

## Out of scope

No bulk cutscene art/audio production or change to heir age 19.
