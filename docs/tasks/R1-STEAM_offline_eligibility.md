# Task Brief: R1-STEAM — Recover achievements earned without Steam

**Status:** authorized by owner acceptance of all R-1 recommendations (2026-10-10); implementation pending.
**Recommended model:** Claude Opus 5.5 (durable eligibility), Gemini Pro (Steam integration/17a)
**Reviewer:** Gemini Pro for persistence changes; Opus merge gate
**Owner gate:** recommendations accepted; listed technical review gates still apply.
**Branch/worktree:** a dedicated slice branch and worktree under TEAM.md §3; never reuse an active sibling checkout.

## Context and dependencies

R1-SAVE-2 durable facts/outbox and current 17a mapping. Work offline with a fake adapter until platform smoke testing.

Inputs: [R-1 code review](../review/r1/02_code_review.md), [gameplay audit](../review/r1/03_gameplay.md), [accepted decisions](../review/r1/06_owner_questions.md), current canonical specs and TEAM.md.

## Scope and outputs

Allowed paths: Assets/Platform/Steam/, Assets/Data/, additive Core events/DTOs and focused harness fixtures; release and Steam reference docs.

Reconstruct eligibility/totals from persisted gameplay where possible; persist a minimal durable record where event-only eligibility would be lost. Reconcile once Steam becomes available and on boot. Counters use idempotent totals/acknowledgements, not blind repeated increments; define retry/rejection behavior. Keep unavailable-client gameplay fully functional and distinguish it from Steam-client offline mode.

## Hard constraints

Preserve Life/Baseball ownership and separate transactions; presentation submits intents/reads DTOs. No probability/topology changes unless explicitly covered by the accepted Math contract. No live saves or secrets. Apply project content boundaries. Coordinate shared schema/files with their assigned owner before starting.

## Tests and gates

Earn arrest/narrative/season milestones with unavailable adapter, save/reload, enable adapter and reconcile twice; no loss or double increment. Simulate failure between remote acknowledgement and local completion. Build and focused harness; exported Windows/Linux and actual Steam tests belong to 17a/17d with owned accounts.

Run each relevant gate once after changes; diagnose failures. Commands: `dotnet build DirtAndDiamonds.sln -c Debug` and `dotnet run --project Tools/<named harness> -c Release`. Gate counts may grow; preserve existing assertions and explain additions. Math-only work runs its reproducible experiments, not unrelated runtime builds.

## Definition of done and handoff

- Spec, implementation and focused fixtures agree; gates pass and limitations are explicit.
- Commit on the slice branch, log progress, and hand off the commit to the named reviewer. No automatic merge/push.
- Stop at this slice boundary. Report what changed, gate results and the next assigned owner.

## Out of scope

No account creation, purchases, store submission, live credential handling or promise that headless tests certify Steam.
