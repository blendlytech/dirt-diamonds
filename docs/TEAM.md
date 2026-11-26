# Dirt & Diamonds — Team Roster & Operating Model

**Status:** owner-directed roster, **2026-11-19**, amended **2026-11-25**, until the owner changes it. It replaces the 2026-10-25 GPT-primary staffing: the GPT models (6.1 Sol, 6 Astra, 6 Sol) are no longer assigned. **2026-11-25 (owner): Gemini 4 Carbon replaces Gemini 4 Carbon completely** (Argon is leaving the owner's plan), and the Argon Orchestrator goes with it. Active models: **Claude Opus 5.5**, **Claude Sonnet 5.5**, **Gemini 4 Carbon** (Low–High), **Gemini 4 Neon** (Low–High), **Gemini Hydrogen**, plus the **Neon Orchestrator** (Low–High). Strength order for the Gemini models, from Google's published model cards (2026-11): Carbon ≈ Neon > Hydrogen. Carbon is Argon's deep-reasoning successor, built on it, with more thinking and longer verification loops. Gemini 3.8 Flash is also leaving and isn't assigned. Completed authorship and review history stays historical.

**Read with:** [GAME_VISION.md](GAME_VISION.md) (what we're building, v3.0), [BUILD_PLAN.md](BUILD_PLAN.md) (Phases 13–17 and Phase L), [progress.md](progress.md) (where we are), [README.md](README.md) (the docs map). Older docs name past authors ("Fable 5", "Opus 4.8", "Sonnet 5", "GPT 6.1 Sol", "GPT 6 Astra", "GPT 6 Sol"); those are not current roles. **This file is the only source of current roles.**

---

## 1. Who does what

| Model (typical effort) | Role | Owns | Never does |
| :-- | :-- | :-- | :-- |
| **The owner (you)** | Director and publisher | Vision, art direction, content/listening approval, playtests, accounts, keys, Steamworks, legal, trademark, pricing and external submissions/payments | Model implementation work |
| **Claude Opus 5.5** (High) | Integration lead, chief architect and core engineer | Phase 13 core, schema/persistence (R1-SAVE), Phase L/S core, task briefs, integration gate, narrative direction, release QA; review of Carbon-authored math/engine/tools | Approve its own implementation; owner-only actions |
| **Gemini 4 Carbon** (High for math, schema, persistence and reviews; Medium otherwise) | Simulation, calibration and math owner; primary independent reviewer | Math plans and constants (R1-MATH, SR-3a, S-2, 13-x Math), shared rating contracts, standalone tools; independent review of every Opus simulation/persistence/schema diff | Approve its own math or implementation; owner-only actions |
| **Claude Sonnet 5.5** (Med–High) | UI, presentation, feature and content engineer | Screens/theme, presentation runtime, narrative JSON, announcer scripts, seed data, art integration, mods and exporters | Unbriefed calibrated-core changes; approve its own work; owner-only actions |
| **Gemini 4 Neon** (Med) | Mid-tier engineer and UI/content reviewer | Review of Sonnet UI/content diffs that touch no `Assets/Simulation/`, schema or persistence paths; harness fixes, data files and mid-size tooling from a brief | Calibrated-core or schema authorship; self-review; owner-only actions |
| **Gemini Hydrogen** (Low–Med) | Mechanical worker | Link repair, rule/skill text syncs, archive moves, formatting, checklist-driven data edits, each with a pass/fail check named in the brief | Any C# under `Assets/`, any design decision, any review |
| **Neon Orchestrator** | Wave runner (mechanical and content waves) | Splits a briefed wave across workers on one branch: mechanical cleanup across Hydrogen workers (Neon reviews the combined diff), and, since 2026-11-25, the Argon Orchestrator's Gate 2+ content volume (e.g. SR-1b/1c Burner Phone arcs) across Sonnet/Hydrogen writers with Carbon consistency review. Opus integrates | Expanding scope beyond the wave brief; engine or schema changes |

### 1a. Current role succession (owner, 2026-11-19; Argon → Carbon 2026-11-25)

**Apply this mapping to every outstanding assignment, reviewer, merge gate and next-step recommendation in older specs, task briefs, rules and skills.** It supersedes the 2026-10-25 GPT-primary table and the 2026-10-13 succession. Historical completed work, branch names (e.g. `p13/13-4-gpt`, `p13/13-6-mathcheck-argon`) and commit attribution are not renamed: §2 keeps Gemini 4 Argon on slices it finished. Staffing updates do not resume paused slices or waive scope, test or owner gates.

| Earlier assignment | Current assignee |
| :-- | :-- |
| GPT 6.1 Sol (integration lead, brief author, merge gate, core) — formerly Claude Opus | **Claude Opus 5.5** |
| GPT 6 Astra / GPT 6 Astra Pro (sim, calibration, Math, primary reviewer) — formerly Gemini Pro Math | **Gemini 4 Carbon** |
| **Gemini 4 Argon** (2026-11-19 roster: sim, calibration, Math, primary reviewer; every outstanding Argon assignment in any doc, brief, rule or skill) | **Gemini 4 Carbon** (owner, 2026-11-25) |
| Argon Orchestrator (content waves) | **Neon Orchestrator** (owner, 2026-11-25) |
| GPT 6 Sol (UI, presentation, content) — formerly Claude Sonnet / Gemini Flash | **Claude Sonnet 5.5** |
| Gemini Argon review reserve (2026-10-25) | **Gemini 4 Carbon** (Argon became a full assignee 2026-11-19, then left 2026-11-25) |
| Gemini 3.8 Flash mechanical/seed-data work (Flash is leaving too) | **Gemini Hydrogen** (mechanical) or **Claude Sonnet 5.5** (content that needs judgement) |
| Any other unavailable-model assignment not explicitly listed | **Claude Opus 5.5** assigns it in the brief before work starts |

**Review independence:** a separate session reviews the exact diff; no model approves its own authored part.
- Opus implementation → **Carbon** review (cross-family).
- Carbon math/implementation → **Opus** review.
- Sonnet UI/content → **Neon** when the diff has no `Assets/Simulation/`, schema, persistence or `EffectiveRatings` paths; otherwise **Carbon**.
- Hydrogen/orchestrator waves → **Neon** reviews the combined diff; Opus runs the integration gate.
- Joint slices: each reviews only the other's part; if parts cannot be separated, the uninvolved senior model (Carbon or Opus) reviews the combined diff.

Opus performs the integration gate after review; its own code requires Carbon's review first. Owner gates remain unchanged.

**Effort and token discipline:** use High effort for Opus/Carbon on schema, persistence, calibration and secrecy-boundary work; Medium for Sonnet/Neon UI and reviews; Low–Medium for Hydrogen. Every delegated session gets a brief (or a compact review packet: question, constraints, exact diff/file ranges, existing gate results) rather than open-ended repository exploration.

---

## 2. Phase-by-phase assignments

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **13-0** sqlite MCP + MC flake recap/seed | Claude Opus | Gemini Pro (done 2026-10-10) | — |
| **13-1** schema v14 + world-gen *(done)* | Claude Opus 5.5 (+ Claude Sonnet 5.5: team seed data) | Gemini 4 Argon (historical) | Team names |
| **13-2** roster capacities + innings + starter selection *(done)* | Claude Opus 5.5 | Gemini 4 Argon (sim + Math; historical) | — |
| **13-3** schedules + calendars + re-band + new Pro guard *(done)* | Claude Opus 5.5 + Gemini 4 Argon (Math; historical) | each other's part | Sanction the new guard |
| **13-4** playoffs + ranking *(engine done; bracket UI in 15c)* | Claude Opus 5.5 + Gemini 4 Argon (Math; historical) (+ Claude Sonnet 5.5: bracket UI) | each other's part | — |
| **13-5** Sunday lock + forfeits *(13-5a done)* | Claude Opus 5.5 | Gemini 4 Argon (historical) | — |
| **13-6** pipeline + draft | Claude Opus 5.5 + Gemini 4 Argon (brief Math check, done 2026-11-24) (+ Claude Sonnet 5.5: recruiting/draft UI) | **Gemini 4 Carbon** reviews 13-6a; Opus reviewed the Math check; Gemini 4 Neon reviews 13-6b | Playtest |
| **13-7** ripple | Claude Sonnet 5.5 | Gemini 4 Carbon | — |
| **14a** buses + VoiceDirector | Claude Sonnet 5.5 | Claude Opus 5.5 | Ear check |
| **14b** VoiceGen + AudioManifestHarness | Gemini 4 Carbon | Claude Opus 5.5 | Pilot run (your key) |
| **14c / SR-9** 6 conference personas + ~900 lines each | Claude Sonnet 5.5 | Claude Opus 5.5 (voice/quality) | **Approve taglines** |
| **14d** BroadcastBooth + highlights | Claude Sonnet 5.5 | Claude Opus 5.5 | Playtest |
| ~~**14e** voice notes~~ | **Cut 2026-10-10** (broadcasters only) | — | — |
| **14f** full generation + QA | Claude Sonnet 5.5 (retake sheets) | — | **Listening QA** |
| **15a** art direction | Claude Opus 5.5 (direction briefs + prompt packs) | — | **Pick the direction** |
| **15b** design system | Claude Sonnet 5.5 | Gemini 4 Carbon | Component gallery |
| **15c** screen rebuilds | Claude Sonnet 5.5 (all screens) | Gemini 4 Carbon | Screenshot review |
| **15d** art production | Owner + AI art tools (Claude Sonnet 5.5: integration) | Claude Opus 5.5 (consistency) | **Taste** |
| **15e** motion / **15f** phone skins | Claude Sonnet 5.5 | Gemini 4 Carbon | — |
| **16a/16b** CutscenePlayer + Director + events | Claude Sonnet 5.5 | Claude Opus 5.5 | — |
| **16** CutsceneHarness | Gemini 4 Carbon | Claude Opus 5.5 | — |
| **16c–16e** cutscene content | Claude Opus 5.5 (story scripts) → Claude Sonnet 5.5 (definitions) | Gemini 4 Carbon | **Story sign-off** |
| **G-0** prototype (historical) | Built by Claude Opus / Claude Sonnet in 2026-10; retain original attribution | Completed review history remains in its brief | Outstanding timing/scene work is now **SR-3a: Gemini 4 Carbon / SR-3b: Claude Sonnet 5.5**, reviewed per those rows |
| **G-1** attribute audit (done inside R-1) → retained batter-side clutch mapping | Gemini 4 Carbon (mapping + Math) | Claude Opus 5.5 | Clutch scope per SR-3a |
| ~~**G-2** full arcade physics + rules~~ | **Cancelled 2026-10-25** (scope_realignment §2.1) | — | — |
| ~~**G-3** arcade AI + full-game calibration~~ | **Cancelled 2026-10-25** | — | — |
| **G-4 → SR-3a** reduced integration: `ClutchRequest` from `EffectiveRatings` | Gemini 4 Carbon | Claude Opus 5.5 | See SR-3a |
| **G-5 → SR-3b** clutch overlay + batting practice (merged with G-9) | Claude Sonnet 5.5 | Gemini 4 Carbon | Playtest |
| ~~**G-6** team takeover + real-time baserunning~~ | **Cancelled 2026-10-25** | — | — |
| **G-7** venue catalog + screens | Claude Sonnet 5.5 (+ Claude Sonnet 5.5: data); **paused beyond the HS slice** | Claude Opus 5.5 | Golden slice before mass generation |
| **G-8** field/venue art | Owner + art tools (Claude Sonnet 5.5: integration); **paused beyond the HS slice** | Claude Opus 5.5 | **Taste; Gate 3** |
| **G-9 → SR-3b** clutch-only tuning/assists | Claude Sonnet 5.5 (Gemini 4 Carbon Math constants in SR-3a) | Claude Opus 5.5 | Playtest; no general difficulty framework |
| ~~old G-2 pitching mode / old G-8 Read v2~~ | **Retired 2026-10-13** by the arcade decision; docs in `archive/` | — | — |
| **L-0** docs + no-abortion content fix | Claude Opus | Gemini Pro (done) | **DONE 2026-10-10** |
| **L-1** schema v15 + identity + legends + poverty + New Career fields | Claude Opus 5.5 (+ Claude Sonnet 5.5: New Career UI) | Gemini 4 Carbon | — |
| **L-2** legends roster, identity catalog, record map | Claude Sonnet 5.5 | Claude Opus 5.5 | **Ethnicity list, legend backstories** |
| **L-3** tokens, `unknown_fan`, father arc, `ProContractSignedEvent`, reveal ordering | Claude Opus 5.5 (scripts + event) | Gemini 4 Carbon | **Father's lines** |
| **L-4** pregnancy, placement, `AdoptChild`, maternity, missing piece | Claude Opus 5.5 (+ Claude Sonnet 5.5: agency events) | Gemini 4 Carbon | — |
| **L-5** temptation, `Indiscretions`, `TemptationMath`, `Active_Effects`, clutch, partner pool + kin guard | Claude Opus 5.5 + Gemini 4 Carbon (Math) (+ **owner: `DiscoveryChance`**) | each other's part | Tone of the lure |
| **L-6** hidden children: intake, both reveals, PI search, reclaim, `Succeed` rostered heir | Claude Opus 5.5 | Gemini 4 Carbon (sim + Math) | — |
| **L-7** press clippings + News tab | Claude Sonnet 5.5 (runtime + tab) + Claude Sonnet 5.5 (articles) | Claude Opus 5.5 | — |
| **L-8** Trailblazer route content + calls | Claude Opus 5.5 (story) → Claude Sonnet 5.5 | Gemini 4 Carbon | **Story sign-off** |
| **S-1** substance engine, `ped_active` writer, Rehab absence, `AvatarDied` | Claude Opus 5.5 | Gemini 4 Carbon | — |
| **S-2** substance magnitudes | Gemini 4 Carbon (Math) | Claude Opus 5.5 | — |
| **S-3** substance content + PED re-gate | Claude Opus 5.5 (scripts) → Claude Sonnet 5.5 | Gemini 4 Carbon | — |
| **S-4** the asterisk (press + record book) | Claude Sonnet 5.5 (runtime + content) | Claude Opus 5.5 | — |
| **R-1** full-project review ([brief](tasks/R-1_full_review_brief.md); Fable 5.1's credit ran out before it began) | **GPT 6 Astra Pro** (owner reassignment, 2026-10-10) | Claude Opus (triage) | **Accept / reject proposals** |
| **17a** achievements + presence | Claude Sonnet 5.5 | Claude Opus 5.5 | — |
| **17b** legal + notices drafts | Gemini 4 Carbon | Claude Opus 5.5 | **Legal sign-off, trademark** |
| **17c** store copy + trailer script | Claude Opus 5.5 | — | **Approve** |
| **17d** release-candidate QA | Claude Opus 5.5 (plan + gates) | Gemini 4 Carbon | **Full playthrough** |
| **17e** submission + launch | Owner | — | Owner |

### Scope realignment assignments (SR-0 through SR-9)

[scope_realignment.md](design/scope_realignment.md) supplies slice scope and owner gates; this roster supplies current authors and reviewers. SR-0 and other completed rows retain historical attribution. All outstanding work uses the 2026-11-19 roster in section 1a. Paused/cancelled status and unresolved scope decisions are unchanged.

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **SR-0** scope spec, GAME_VISION v3.0, GEMINI, BUILD_PLAN, TEAM, progress | Claude Opus (draft) → GPT (owner-assigned completion) | Owner | **Read GAME_VISION v3.0** |
| **SR-1a** event-framework `outcomes[]` | Claude Opus 5.5 | Gemini 4 Carbon | — |
| **SR-1b** Fencing + Robbery narrative arcs; mine hustle numbers | Claude Opus 5.5 → Claude Sonnet 5.5 | Gemini 4 Carbon | Story sign-off |
| **SR-1c** retire two screens + phone buttons; delete after parity | Claude Sonnet 5.5 | Gemini 4 Carbon | **Delete approval** |
| **SR-2a/b** stance engine / `GameManager` + stance UI | Claude Opus 5.5 / Claude Sonnet 5.5 | Gemini 4 Carbon (+ Math values) | Stance table sign-off |
| **SR-3a** `ClutchResolver` + `BatterIntentKind.Clutch` + tilt/Math constants | **Gemini 4 Carbon** | Claude Opus 5.5 | **Clutch feels right** |
| **SR-3b** clutch overlay + batting practice | Claude Sonnet 5.5 | Gemini 4 Carbon | Playtest |
| **SR-4** Gate 2 screens (scope_realignment §5 rows 6-12) | Claude Sonnet 5.5 | Gemini 4 Carbon | Screenshot review |
| **SR-5** Golden vertical slice (Gate 3) | Owner + AI art tools; Claude Sonnet 5.5 integration | Claude Opus 5.5 | **Taste** |
| **SR-6** `mods/` loader | Claude Sonnet 5.5 | Gemini 4 Carbon | — |
| **SR-7** Tabloid feed | Claude Sonnet 5.5 (runtime + content) | Claude Opus 5.5 | — |
| **SR-8** Rap Sheet exporter | Claude Sonnet 5.5 | Gemini 4 Carbon | — |
| **SR-9** six booths: persona framework, trigger catalog, scripts | Claude Opus 5.5 → Claude Sonnet 5.5 | Gemini 4 Carbon | **Approve taglines** |

**Execution priority:** Claude Opus 5.5 continues **13-1 on `p13/integration`**, reviewed by Gemini 4 Carbon (Argon before 2026-11-25), then 13-2/13-3 and the v15-DDL-only freeze gate. Prepare Gate 2 from scope_realignment §5: SR-1, SR-2 and SR-4 with 15a/15b/15c; the owner chooses art direction. Phases 14 and 16 production pause until after Gate 4; SR-9 informs the six-persona scope when audio resumes. SR-3a/b clutch work must not block the neutral `MicroGame` demo. SR-5 is Gate 3; SR-6–SR-8 follow Gate 4. No model starts the next slice without its brief and assignment.

---


## 3. How work flows

1. **Brief.** Claude Opus 5.5 writes a task brief in `docs/tasks/` before any delegated slice starts, from [TASK_BRIEF_TEMPLATE.md](tasks/TASK_BRIEF_TEMPLATE.md) ([voicegen_task_brief.md](tasks/voicegen_task_brief.md) is a worked example). It must name the **recommended model** and the **handoff**: context, hard constraints (allowed paths), inputs, outputs, tests, definition of done, out of scope.
2. **Branch + worktree.** Each task runs on its own branch named `p<phase>/<slice>-<model>` (e.g. `p14/14b-gpt`), **checked out in that model's own git worktree** (`git worktree add ..\dnd-<model> -b <branch> origin/main`). A branch alone is not isolation: two models in the same checkout share one working tree and one `bin/` folder (2026-10-10: two sessions edited `CareerManager.cs` and rebuilt the MC harness under each other). **Nobody builds on `main` directly, and nobody builds in another model's worktree.**
3. **Build and self-check.** The author runs the gates named in the brief and hands back a diff plus the gate output.
4. **Review.** **The author never reviews their own work.** The reviewer from §2 checks the diff against the brief and the design doc, and reruns the gates.
5. **Merge.** Claude Opus 5.5 merges once the review passes and the gates are green, then logs a compact entry in `progress.md`.
6. **Owner gates.** Where §2 names an owner gate, nothing merges until the owner says yes.
7. **Stop and hand off.** The **Author** column in §2 is each slice's **recommended model**. A model works only its own slice and **stops when the definition of done is met**. It never drifts into the next slice. It ends with a handoff note to the human: what was done, the commit, the gates and their results, what's open, and **"Recommended next: `<slice>` by `<model>`"** (plus the reviewer). The human starts the next model.
8. **Write the next prompt (owner, 2026-11-22).** After the handoff note, the finishing model writes the **complete prompt for the recommended next model**, ready to paste: role, task, worktree and branch, session start, preconditions, task details, rules and finish.
   - **Owner questions first.** If the model has questions only the owner can answer, and the next model needs those answers to proceed, it asks them in the handoff and **waits for the owner's reply** before writing the prompt. It then folds the answers in. Decisions the next step is designed to settle (for example D-options the owner decides after a Math check is reconciled) don't block the prompt; they're listed as open.
   - **Carry the rule forward.** Every prompt includes this step's rule, so each model in the chain does the same.
   - **Gemini sessions.** Their sandbox is scoped to `C:\Users\DELL\dirt_and_diamonds`. The prompt names a worktree under `.worktrees/` (pre-created by Opus, or say who creates it), opens with the sandbox block (`git` and `dotnet` need the owner's grant outside the sandbox; write check first), and gives the no-commit fallback.

### Gates (from GEMINI.md §9)
- Anything under `Assets/Simulation/` → `run_monte_carlo_batch`. The resolver guard stays byte-exact; re-baselining a season guard needs explicit sanction.
- Event content → `check_event_graph_integrity`. Voice assets → `validate_audio_manifest`. Cutscenes → `CutsceneHarness`. Schema → `validate_sqlite_schema`. Exports → `validate_steamworks_native`.
- Presentation code (Phases 14–16 runtime, all of Phase 15) must leave the MC and Gritty harnesses **untouched** (`git diff --stat` shows no `Assets/Simulation/` paths).

---

## 4. Ground rules for every model

- **The repo is private** (since 2026-10-10; it was public before, so treat its history as published). An auto-committer pushes. Never write a secret into any tracked file: keys (`ELEVENLABS_API_KEY`, `ANTHROPIC_API_KEY`) live only in the git-ignored `.env` or environment variables, and `git ls-files .env` must print nothing.
- **Owner-only actions:** ElevenLabs generation runs with the real key, Steamworks, trademark filings, pricing, store submission, force-pushes and history rewrites.
- **One source of truth per question:** the vision (GAME_VISION), the plan (BUILD_PLAN), state (progress.md), specs (`docs/design/`), roles (this file).
- **Instruction files:** `GEMINI.md` holds the binding project directives. `CLAUDE.md` (for Claude Code) imports it. GPT-based agents read `AGENTS.md`, which points to it too. Every task brief links GEMINI.md §1–§10 as binding.

## 5. Open decisions for the owner

1. ~~**Who leads.**~~ **Resolved 2026-10-10:** the owner confirmed the original roster. **Current lead: Claude Opus 5.5** under the 2026-11-19 roster directive.
2. **Auto-committer policy (R-1 accepted):** no automatic commits/pushes to main or shared/integration branches. Disable it for those targets; any retained automation is restricted to an explicitly chosen personal scratch branch. Reviewed human-directed integration only. This records policy, not a claim that an external service has been disabled.
3. ~~**Repo visibility.**~~ **Resolved 2026-10-10:** the repo is private.

## R-1 accepted repair assignments (2026-10-10)

The owner accepted all [R-1 answers](review/r1/06_owner_questions.md). The [BUILD_PLAN execution table](BUILD_PLAN.md#r-1-accepted-execution-order-2026-10-10) links the eight scoped briefs. Claude Opus 5.5 owns SAVE-1/SAVE-2/FAMILY and durable REVEAL/STEAM work; *(from 2026-10-13, §1a)* Gemini 4 Carbon independently reviews them and Claude Sonnet 5.5 owns the presentation/Steam integration. Gemini 4 Carbon owns R1-MATH (Claude Opus 5.5 reviews); Gemini 4 Carbon owns accepted EFFECTS projections and LEARN engine DTO/session seams, Claude Opus 5.5 development, Claude Sonnet 5.5 LEARN UI. These assignments supplement existing slices; they do not authorize simultaneous edits to shared files. Stop and hand off per §3.
