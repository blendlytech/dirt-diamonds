# Task Brief: <slice id> — <title>

**Recommended model:** <model, from TEAM.md §2>
**Reviewer:** <model>
**Owner gate:** <what the owner must approve, or "none">
**Spec:** <docs/design/... §n>
**Branch / worktree:** `p<phase>/<slice>-<model>` in your own worktree (TEAM.md §3)

## 1. Context

What this is, why it matters, and the part of [GAME_VISION.md](../GAME_VISION.md) it serves.

## 2. Hard constraints

- Allowed paths (everything else is read-only).
- The calibration contract, if `Assets/Simulation/` is touched: neutral branches stay byte-identical; run `run_monte_carlo_batch`.
- Content lines (GEMINI.md §10): never abortion, adults-only rewards, nothing explicit, secrecy.

## 3. Inputs

The files, data and decisions this task starts from.

## 4. Outputs

Exactly what this task produces.

## 5. Tests and gates

Each gate to run, the command, and the expected result.

## 6. Definition of done

A checklist. **When every item is checked, stop.**

## 7. Out of scope

Everything this task must not do, including the next slice.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built>  · Commit: <sha>  · Gates: <name → result, ...>
Open: <anything unfinished, risks, owner questions>
Recommended next: <slice> by <model> (reviewer: <model>)
```

Then add one compact line to [progress.md](../progress.md). Don't start the next slice.
