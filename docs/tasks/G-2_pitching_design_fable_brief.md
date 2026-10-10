# Task Brief: G-2 Pitching-Mode Design

**Model:** Claude Fable 5.1 (owner assignment, 2026-10-10) · **Reviewer:** Claude Opus 5.5 (feasibility) + Gemini 3.1 Pro Math (curves) · **Owner gate:** the owner accepts the spec.
**Worktree / branch:** `git worktree add ..\dnd-fable -b g2/fable-design g2/fable-pack` (a local branch; worktrees share one repo, so nothing needs pushing) (TEAM.md §3).
**Billing note:** this session runs on prepaid API credit with a $5 approval gate. Spend your tokens on reasoning and the spec, not on exploration.

---

## 1. Read only these two files

1. This brief.
2. [G-2_pitching_context_pack.md](G-2_pitching_context_pack.md): the verified code facts, constraints and reference numbers.

**Do not** Glob, Grep, Explore, or open other repo files, and **do not** start subagents. The pack is the source of truth for this task. If you need a fact it doesn't contain, record it under "Open questions for Opus" in your spec and design around it.

## 2. Deliverable

Write **`docs/design/pitching_mode.md`** on your branch. It must be self-contained, so an engineer (Claude Opus 5.5) can implement it and Pro Math can tune it without reading anything else. It covers:

1. **The player's per-pitch inputs** and why each exists: pitch from the arsenal, target cell (a 3×3 grid plus edge/chase cells, or your counter-proposal), intent dial, and anything else you justify. Keep the input load comparable to batting's three inputs.
2. **Execution model:** where the pitch actually lands, as a function of target, control and fatigue (scatter), and how that maps onto the chain's in/out-of-zone draw and the batter's `trueCell`.
3. **Pitch quality:** how stuff and the arsenal's velocity, movement and usage shape whiff, contact and contact quality (q), per pitch and per location. Velocity is unused today and movement only blurs the cue, so decide what each one does.
4. **The batter AI:** how the opposing batter reads you. Cover its memory of your tendencies (by count, by pitch, by location, by sequence), how batter discipline and contact scale its learning and reach, and its **anti-exploit bounds**: it must punish predictability without being omniscient, and no fixed pattern may be dominant. Show why "always challenge" (pack §6, observation 2) stops being dominant.
5. **Mapping onto the engine:** which branch the human-pitching PA runs (reuse the Read branch, a new branch, or something else) and exactly which seams change. That includes `PitchCall`, `IPitcherPolicy`, `PitchMatchup`, `SimulatePa`, any per-pitch result hook, and the effect on the shipped human-batting Read path. Prove **Invariant N** holds by construction.
6. **Skill band and harness:** concrete scripted bots (perfect, coin-flip, contrarian, plus a "predictable" bot that the batter AI must punish), the metric each produces, and the target spread (poor vs excellent ≈ the 40-vs-70 rating gap, capped). Include rough analytic or back-of-envelope estimates showing your first-pass constants land near the band.
7. **Constants table:** every new knob with a name, first-pass value, range, and one-line rationale, flagged "for Pro Math".
8. **Feel and presentation hooks:** what the player sees after each pitch (the reveal, the batter's reaction, scouting feedback that teaches) and what the UI needs from the sim, as DTO fields only. Presentation never writes state.
9. **Edge cases:** wild pitches and HBP if you include them (HBP is folded into Walk today), the two-strike foul loop, fatigue within an outing, shadowed or absent pitchers, tier differences (HS 7 innings), and PED.
10. **Engine slice breakdown:** 2–5 ordered implementation slices, each with a gate. Name Claude Opus 5.5 as engine and Claude Sonnet 5.5 as UI.
11. **Disagreements:** anywhere you think the pack's framing, the gameplay_core proposal, or an owner recommendation is wrong, make the case.
12. **Open questions for Opus**, and **owner questions**, each with your recommended answer.

## 3. Constraints

The constraints in pack §7 are binding: Invariant N, determinism through `RngState`, zero allocation on the sim path, struct policies, identity never touching the sim, and named tunables.

## 4. Working style (credit-aware)

- Think as hard as the problem needs, then write the spec **to disk** in as few edits as practical, ideally one Write plus a couple of targeted Edits.
- No long recap in chat. The spec is the output.
- **Stop when the deliverable meets §2.** Don't implement code or start another task.

## 5. Handoff (end your session with this)

```text
Done: G-2 pitching-mode design → docs/design/pitching_mode.md · Branch: g2/fable-design @ <sha>
Key decisions: <3–5 lines>
Open: <n> questions for Opus, <n> owner questions (in the spec)
Recommended next: G-2 feasibility review by Claude Opus 5.5 (+ Gemini Pro Math on the curves), then a Fable revision pass
```

Then commit the spec on your branch (don't push; the owner decides) and add one line to `docs/progress.md` on your branch.
