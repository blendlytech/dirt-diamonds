# Task Brief: R-1 — Full-Project Review

> **History (2026-10-10).** This brief was written for Claude Fable 5.1. Fable was pointed at the narrower G-2 pitching-mode design first, and its prepaid API credit ran out before R-1 began. The owner then **reassigned R-1 to GPT 6 Astra Pro**. Since the brief was written:
> - The owner decided **O1** (the skill band; gameplay_core §6 item 1).
> - G-2 has a full design, [pitching_mode.md](../archive/pitching_mode.md) **rev 2**. In `03_gameplay.md`, check rev 2 rather than designing pitching again.
> - You also own G-8 and the G-2 engine (TEAM.md §2). R-1 is read-only on code, so it runs in its **own** worktree alongside them. Where your review touches G-2/G-8, flag it; don't redesign it.

**Model:** GPT 6 Astra Pro (owner assignment, 2026-10-10)
**Reviewer / triage:** the owner + Claude Opus 5.5 (integration lead)
**Owner gate:** the owner accepts or rejects each proposal
**Branch / worktree:** `review/r1` in its own worktree: `git worktree add ..\dnd-gpt-review -b review/r1 origin/main` (TEAM.md §3). Don't reuse `..\dnd-gpt` (G-8) or `..\dnd-gpt-voice` (14b).
**Pacing:** usage is plentiful, but the corpus (§3: about 1.36M tokens) is bigger than one context. Go **subsystem by subsystem** (§4) and commit findings to disk after each one, so the review survives a context reset or a cut-off session.

---

## 1. Your mission (the owner's words, 2026-10-10)

> A clear start-to-finish goal is presented to you. Take it as a **starting point**. You can make **suggested changes, improvements, feature add-ons, and differences of opinion**. Ultimately, you need to gain a **full understanding of the project inside and out**, and make sure that when the build continues, it continues from a point of **clarity, with no conflicting information in the repo**.

*Dirt & Diamonds* is a premium Steam baseball + life-sim game built in Godot 4 + C#. Its vision is complete and locked as [GAME_VISION.md](../GAME_VISION.md) v2. Phases 0–12 are built and calibrated; Phases 13–17, Phase G (gameplay core) and Phase L (lineage, temptation, vice) remain. You're the first model to read **everything**. Your output decides how the build continues.

## 2. Your latitude

- **Disagree freely.** The vision, the plan, the specs and the code are all open to challenge, including the owner's decisions, if you think the game is better another way. Make the case. The owner decides.
- **Propose; don't impose.** Don't edit GAME_VISION, the active specs, TEAM.md or any code. Put every change in your proposals (§5).
- **Exception: plain factual fixes.** A broken link, a wrong path, or a doc number that contradicts the code you can fix directly on your branch. List each one in `01_conflicts.md`.
- **Gameplay comes first** (owner priority). Do [design/arcade_baseball_overhaul.md](../design/arcade_baseball_overhaul.md) and [design/gameplay_core.md](../design/gameplay_core.md) before anything else, including the G-1 attribute audit. **The owner chose true arcade physics on 2026-10-13**, so attended games become a real-time physics game. Review that design; don't re-litigate the dice-table pitching mode, which was dropped.

## 3. Read in this order

1. [README.md](../README.md): which doc is authoritative (`design/` = active, `reference/` = shipped systems, `archive/` = history; skip `archive/` unless you're resolving history).
2. [GAME_VISION.md](../GAME_VISION.md) v2.1 → [BUILD_PLAN.md](../BUILD_PLAN.md) → [TEAM.md](../TEAM.md) → [progress.md](../progress.md).
3. `GEMINI.md` (the binding directives; `CLAUDE.md` imports it) and `.agents/rules/*.md`, `.agents/skills/*/SKILL.md`.
4. Active specs: **[arcade_baseball_overhaul.md](../design/arcade_baseball_overhaul.md)** and **[gameplay_core.md](../design/gameplay_core.md)**, then [league_restructure.md](../design/league_restructure.md), [temptation_and_lineage.md](../design/temptation_and_lineage.md), [substances_and_addiction.md](../design/substances_and_addiction.md), [voice_and_broadcast_audio.md](../design/voice_and_broadcast_audio.md), [visual_redesign.md](../design/visual_redesign.md), [cutscenes_motion_comic.md](../design/cutscenes_motion_comic.md), [steam_commercial_release.md](../design/steam_commercial_release.md).
5. Briefs: [13-0_review_careermanager_ids.md](13-0_review_careermanager_ids.md), [16-1_opening_sequence_brief.md](16-1_opening_sequence_brief.md), [voicegen_task_brief.md](voicegen_task_brief.md).
6. The code, **one subsystem at a time** (§4), each with its `reference/` spec open.

**Size:** about 1.36M tokens in total (0.7M code, 0.5M docs), which is more than one context. Work subsystem by subsystem, and **write findings to disk as you go**, so a long session is resumable and nothing lives only in context.

## 4. Code map

| Subsystem | Path | Size (C#) | Reference spec(s) |
| :-- | :-- | :-- | :-- |
| Baseball sim: resolver, Markov micro-game, pitch chain, fatigue, tiers, career, succession, development, promotion | `Assets/Simulation/Baseball/` | 28 files, ~9.7k lines | [baseball_pa_outcome_model](../reference/baseball_pa_outcome_model.md), [baseball_markov_micro_sim](../reference/baseball_markov_micro_sim.md), [at_bat_read_input_model](../reference/at_bat_read_input_model.md), [heir_mechanics](../reference/heir_mechanics.md), [development_decline_curves](../reference/development_decline_curves.md), [equipment_quality](../reference/equipment_quality.md) |
| Life sim: needs, utility AI, relationships, person drift | `Assets/Simulation/Life/` | 10 files, ~2.3k | [life_sim_needs_decay](../reference/life_sim_needs_decay.md), [high_school_person_layer](../reference/high_school_person_layer.md) |
| Hustles | `Assets/Simulation/Hustles/`, `Assets/Economy/` | 12 + 10 files, ~5k | [hustles_*](../reference/), [hustle_minigames_depth_pass](../reference/hustle_minigames_depth_pass.md) |
| Narrative: events, dispatcher, contacts, social | `Assets/Narrative/` + `Content/*.json` | 7 files, ~2.6k + content | [gritty_event_framework](../reference/gritty_event_framework.md), [criminal_underworld_*](../reference/), [marriage_and_conception](../reference/marriage_and_conception.md) |
| Data: schema v13, queries, DTOs | `Assets/Data/` | 12 files, ~5.1k | `.agents/rules/database_*.md` |
| Core: game manager, bus, clock | `Assets/Core/` | 8 files, ~2.5k | [real_time_clock_slice_g](../reference/real_time_clock_slice_g.md) |
| UI | `Assets/UI/` | 27 files, ~9k | [presentation_layer_narrative](../reference/presentation_layer_narrative.md), [at_bat_presentation](../reference/at_bat_presentation.md), [onboarding_tutorial_overlay](../reference/onboarding_tutorial_overlay.md) |
| Steam | `Assets/Platform/` | 3 files | [steam_publishing_ship_it](../reference/steam_publishing_ship_it.md) |
| Harnesses | `Tools/*Harness`, `Tools/SchemaValidator` | 7 projects | `.agents/skills/*` |

## 5. Deliverables

Write these to `docs/review/r1/`, one file each:

| File | Contents |
| :-- | :-- |
| `00_project_map.md` | Your model of the whole project: systems, data flow, boundaries (Life ↔ Baseball through the bus only; presentation never writes state; the neutral/human branch split), what's built versus planned, per subsystem with paths. |
| `01_conflicts.md` | Every remaining contradiction, **doc ↔ doc and doc ↔ code**, with `file:line` on both sides and the fix (marked *fixed on branch* or *needs decision*). The goal is **zero**. |
| `02_code_review.md` | Per subsystem: correctness risks, determinism and calibration hazards, performance (the zero-alloc mandate), architecture and boundary violations, test gaps. Each item has a severity (blocker / high / medium / low) and evidence. |
| `03_gameplay.md` | **First priority.** The G-1 attribute audit: verify gameplay_core §1 against the code and propose the ratings-to-physics mapping (arcade §4.1). Also: a critique of the arcade engine design (physics model, AI, the §9 calibration contract, determinism, the `EffectiveRatings` extraction); your recommended default difficulty target; positions on the Phase G decisions (gameplay_core §6, arcade §13). |
| `04_proposals.md` | Changes, improvements, **feature add-ons** and **disagreements**, each with: *why*, *what changes* (docs and code), *cost* (S/M/L), *risk* (including calibration), *recommended model*. Rank by value. |
| `05_plan_to_1_0.md` | Your recommended path to launch: agree with or resequence BUILD_PLAN, and name **the next 5 slices** to run, each with its recommended model. |
| `06_owner_questions.md` | Every question for the owner, consolidated, each with your recommended answer. |

## 6. Known issues to verify, not assume

Each has been reported by the integration lead. Confirm or refute it against the code.

- **13-0:** `CareerManager` mints ids with `Guid.NewGuid()`, which causes the rare Monte Carlo flake; an in-flight fix needs one change ([13-0 brief](13-0_review_careermanager_ids.md)).
- **`ped_active` has no gameplay writer**, so the steroid multiplier never fires live.
- **`place_for_adoption`** (`hs_pregnancy_decision`) creates no child.
- **There's no pitching interface:** the attended game uses `NeutralPitcherPolicy`. The arcade engine replaces this, so confirm what in `MicroGame`/`CareerManager` the G-4 seam (arcade §2) must preserve.
- **Needs and stress never touch the field**, and only confidence, happiness and teamwork do (±2).
- **Discipline and control have small macro weights** (gameplay_core §1.1).
- **The spouse pool is the league** (ballplayers only), there's no gender anywhere, and position is Hitter/Pitcher only.
- **The shipped code is the 6-tier league**, while the plan is 3 tiers (Phase 13).
- **`MaturityAge = 19`**, while a reclaimed child becomes official at 18 (intended, D15).
- **The live save** `dirt_and_diamonds.db` was found at 0 bytes (2026-10-09).
- **Reference docs' "Status" lines are historical**, and their "Fable 5 / Opus 4.8 / Sonnet 5" authors are past, not current roles. (Neither is Claude Fable 5.1, who held this brief first.)

## 7. Tests and gates (read-only verification is allowed)

| Command | Expected today |
| :-- | :-- |
| `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings, 0 errors |
| `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 |
| `dotnet run --project Tools/MonteCarloHarness -c Release` | 352/352 (reviewed baseline 2dec343; rare flake: see 13-0) |
| `dotnet run --project Tools/CoreLoopHarness -c Release`, `NeedsDecayHarness`, `HustleHarness`, `HoldemHarness`, `SchemaValidator` | all green |

If a gate fails on a clean `origin/main`, that's a finding. Report it; don't fix it.

## 8. Hard constraints

- **Read-only on code.** Doc fixes only for plain factual errors (§2), on your branch.
- **Never** read out, print or commit `.env` values. Never commit to `main`. Never force-push.
- An auto-committer and parallel sessions are active: work only in your worktree, and run `git status` and `git log` before trusting any state.
- Owner-only actions (ElevenLabs runs, Steamworks, trademark, pricing, submissions) are out of scope.

## 9. Definition of done

- [ ] All seven deliverables exist and are self-contained (a reader needs nothing else to act on them).
- [ ] `01_conflicts.md` has no unresolved factual conflict left; every remaining item needs an owner decision.
- [ ] Every proposal has why / what / cost / risk / model.
- [ ] `05_plan_to_1_0.md` names the next 5 slices and their models.
- [ ] Your branch is committed (push only if the owner says so); nothing is merged.

**When every box is checked, stop.**

## 10. Handoff (end your session with this, addressed to the human)

```text
Done: R-1 full-project review → docs/review/r1/ (7 files) · Branch: review/r1 @ <sha>
Gates: build <result> · GrittyEvents <n/n> · MonteCarlo <n/n> · others <results>
Top 5 findings: <one line each>
Open: <owner questions count> owner questions in 06_owner_questions.md
Recommended next: Triage of R-1 by the owner + Claude Opus 5.5, then <first slice> by <model>
```

Then add one compact line to [progress.md](../progress.md) on your branch.
