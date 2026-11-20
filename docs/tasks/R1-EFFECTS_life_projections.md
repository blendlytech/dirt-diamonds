# Task Brief: R1-EFFECTS — Accepted needs/stress and work-ethic projections

> **2026-10-13 update (arcade decision, GAME_VISION v2.1):** attended games become true arcade physics ([arcade_baseball_overhaul.md](../archive/arcade_baseball_overhaul.md)). Projections land in the shared `EffectiveRatings` stack (arcade §4.2), so attended (arcade) and background avatar games see the same effect. Park factors stay deferred; **manual fielding is no longer deferred** (assisted fielding is in launch scope).

**Status:** owner authorized through R-1 Q04/Q05; implementation waits for R1-MATH.
**Recommended model:** GPT 6 Astra Pro (game-start projection); Claude Opus 5.5 (practice development).
**Reviewer:** Gemini Pro (sim), Gemini Pro Math (constants), Claude Opus (integration).
**Branch/worktree:** separate scoped branches/worktrees under TEAM.md §3; coordinate shared files.

## Inputs and scope

Read [R1-MATH](R1-MATH_gameplay_contracts.md), [gameplay_core](../reference/attribute_contracts.md) and [accepted decisions](../review/r1/06_owner_questions.md). Use only the signed constants/order/caps; no invented values.

Allowed paths: Assets/Core/ event/DTO seams, Assets/Data/ projection persistence, Assets/Simulation/Life/ producer, Assets/Simulation/Baseball/ modifier/development consumers, relevant harnesses and references. Life and Baseball communicate through sanctioned events/data, never direct active-loop references.

- Bake one small capped needs/stress projection at game start, shared by attended/background avatar games; healthy/no-effects is exact identity. Avoid counting the same deprivation through multiple penalties.
- Work_ethic bounds the gain from actual practice. It cannot grant training without practice, bypass potential caps, or defeat a missing-piece base lock.
- Expose source/order/clamp/final values through the R1-LEARN DTO. UI never recalculates effects.
- Keep additional person discipline/composure field scalers out of 1.0. Leave park factors and manual fielding deferred.

## Tests and gates

Neutral/healthy identity, cap/extreme inputs, missing rows, repeated game-start baking, attended/background parity, save/reload projection parity, zero-practice work_ethic identity, potential/lock interactions. Run Debug build and relevant MonteCarlo/NeedsDecay/CoreLoop Release harnesses after changes. Preserve neutral calibration unless R1-MATH explicitly sanctions a separate baseline change; report that change distinctly.

## Done and handoff

Focused fixtures pass, effective DTO matches resolver inputs, no per-PA queries/allocations, and boundaries hold. Commit, log progress and hand off to the named reviewers. Stop at this slice; no new content, UI production or unsanctioned tuning.
