# Task Brief: R1-SAVE-2 — Durable day, choice, resolution and RNG continuation

**Status:** authorized by owner acceptance of all R-1 recommendations (2026-10-10); implementation pending.
**Recommended model:** Claude Opus 5.5
**Reviewer:** Gemini Pro
**Owner gate:** recommendations accepted; listed technical review gates still apply.
**Branch/worktree:** a dedicated slice branch and worktree under TEAM.md §3; never reuse an active sibling checkout.

## Context and dependencies

R1-SAVE-1; schema ownership coordinated with 13-1/L-1. Design the recovery protocol before implementing it.

Inputs: [R-1 code review](../review/r1/02_code_review.md), [gameplay audit](../review/r1/03_gameplay.md), [accepted decisions](../review/r1/06_owner_questions.md), current canonical specs and TEAM.md.

## Scope and outputs

Allowed paths: Assets/Core/, Assets/Data/, Assets/Narrative/Events/, Assets/Economy/Hustles/, Assets/Simulation/Baseball/, Tools/CoreLoopHarness/, Tools/GrittyEventsHarness/, Tools/HustleHarness/, Tools/MonteCarloHarness/, Tools/SchemaValidator/ and matching references.

Persist named RNG streams/counters and pending operations so cold reload continues the same future draw sequence. Introduce durable day-work and resolution identities with idempotent subsystem completion: a committed date cannot silently skip unfinished work. Preserve separate Life/Baseball transactions; no giant cross-system transaction. Persist pending narrative choices and cooldowns; clear/ack only after successful application. Make hustle money/faction/turf consequences recover as one logical resolution without duplicate payment or graph changes. Define schema upgrade/clean-break handling and snapshot consistency across RNG state and writes.

## Hard constraints

Preserve Life/Baseball ownership and separate transactions; presentation submits intents/reads DTOs. No probability/topology changes unless explicitly covered by the accepted Math contract. No live saves or secrets. Apply project content boundaries. Coordinate shared schema/files with their assigned owner before starting.

## Tests and gates

Interrupt after each commit boundary, reopen, finish recovery, compare every durable result with uninterrupted fixed-seed play. Repeat recovery twice to prove idempotency. Reopen during a visible choice; fail apply then retry. Verify independent RNG streams, empty pending queues and different reload points. Debug build plus CoreLoop, GrittyEvents, Hustle, MonteCarlo and SchemaValidator Release gates. Add focused meaningful recovery fixtures; never replace a failed baseline without explanation.

Run each relevant gate once after changes; diagnose failures. Commands: `dotnet build DirtAndDiamonds.sln -c Debug` and `dotnet run --project Tools/<named harness> -c Release`. Gate counts may grow; preserve existing assertions and explain additions. Math-only work runs its reproducible experiments, not unrelated runtime builds.

## Definition of done and handoff

- Spec, implementation and focused fixtures agree; gates pass and limitations are explicit.
- Commit on the slice branch, log progress, and hand off the commit to the named reviewer. No automatic merge/push.
- Stop at this slice boundary. Report what changed, gate results and the next assigned owner.

## Out of scope

No gameplay tuning, new story content, network services or live-save experiments. A WAL checkpoint alone is not the recovery design.
