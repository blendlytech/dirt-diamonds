# Dirt & Diamonds — Team Roster & Operating Model

**Status:** owner-directed GPT-only availability, **2026-10-25**, until the owner changes it. All active assignments use **GPT 6 Astra**, **GPT 6 Sol** or **GPT 6.1 Sol**. Claude and Gemini models are unavailable because their token budgets are exhausted. Completed authorship and review history stays historical.

**Read with:** [GAME_VISION.md](GAME_VISION.md) (what we're building, v3.0), [BUILD_PLAN.md](BUILD_PLAN.md) (Phases 13–17 and Phase L), [progress.md](progress.md) (where we are), [README.md](README.md) (the docs map). Older docs name past authors ("Fable 5", "Opus 4.8", "Sonnet 5"); those are not roles. **This file is the only source of current roles.**

---

## 1. Who does what

| Model | Role | Owns | Never does |
| :-- | :-- | :-- | :-- |
| **The owner (you)** | Director and publisher | Vision, art direction, content/listening approval, playtests, accounts, keys, Steamworks, legal, trademark, pricing and external submissions/payments | Model implementation work |
| **GPT 6.1 Sol** | Integration lead, systems architect and core engineer | Former Opus assignments: Phase 13 core, schema/persistence, Phase L/S core, task briefs, integration gate, narrative direction, release QA; review of Astra-authored math/engine/tools | Approve its own implementation; owner-only actions |
| **GPT 6 Astra** | Simulation, calibration and math owner; primary technical reviewer | Existing Astra assignments including SR-3a, R1-MATH, S-2, shared rating contracts and standalone tools; review of 6.1 Sol core and 6 Sol UI/content | Approve its own math or implementation; owner-only actions |
| **GPT 6 Sol** | UI, presentation, feature and content engineer | Former Sonnet and Flash assignments: screens/theme, presentation runtime, narrative JSON, announcer scripts, seed data, art integration, mods and exporters | Unbriefed calibrated-core changes; approve its own content; owner-only actions |

### 1a. Current role succession (owner, 2026-10-25)

**Apply this mapping to every outstanding assignment, reviewer, merge gate and next-step recommendation in older specs, task briefs, rules and skills.** It supersedes earlier model-availability tables and cross-family review requirements. Historical completed work, branch names and commit attribution are not renamed. The owner's GPT-only instruction does not resume paused slices or waive scope, test or owner gates.

| Earlier assignment | Current assignee |
| :-- | :-- |
| Claude Opus / Opus (including integration lead, brief author and merge gate) | **GPT 6.1 Sol** |
| Claude Sonnet / Sonnet | **GPT 6 Sol** |
| Gemini 3.8 Flash / Flash | **GPT 6 Sol** |
| GPT 6 Astra Pro / GPT Astra Pro / generic GPT reviewer | **GPT 6 Astra** |
| Gemini Pro Math / Math | **GPT 6 Astra**; **GPT 6.1 Sol** reviews Astra-authored values |
| Former Gemini Pro sim-side league author | **GPT 6.1 Sol**; **GPT 6 Astra** reviews |
| Former Gemini Pro presentation-runtime author | **GPT 6 Sol**; **GPT 6 Astra** reviews |
| Former Gemini Pro reviewer | **GPT 6 Astra**, or **GPT 6.1 Sol** for Astra-authored work |
| Any other unavailable-model assignment not explicitly listed | **GPT 6.1 Sol** assigns it to one of these three models in the brief before work starts |

**Review independence during GPT-only availability:** a separate session reviews the exact diff; no model approves its own authored part. 6.1 Sol implementation goes to Astra; Astra implementation/math goes to 6.1 Sol; 6 Sol UI/content goes to Astra or 6.1 Sol as listed below. For joint slices, each reviews only the other's part; if their changes cannot be reviewed separately, the remaining uninvolved model reviews the combined diff. This is same-family review under the owner's current availability constraint, not cross-family validation. 6.1 Sol performs the integration gate after review; its own code requires the assigned review first. Owner gates remain unchanged.

---

## 2. Phase-by-phase assignments

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **13-0** sqlite MCP + MC flake recap/seed | Claude Opus | Gemini Pro (done 2026-10-10) | — |
| **13-1** schema v14 + world-gen | GPT 6.1 Sol (+ GPT 6 Sol: team seed data) | GPT 6 Astra | Team names |
| **13-2** roster capacities + innings + starter selection | GPT 6.1 Sol | GPT 6 Astra (sim + Math) | — |
| **13-3** schedules + calendars + re-band + new Pro guard | GPT 6.1 Sol + GPT 6 Astra (Math) | each other's part | Sanction the new guard |
| **13-4** playoffs + ranking | GPT 6.1 Sol + GPT 6 Astra (Math) (+ GPT 6 Sol: bracket UI) | each other's part | — |
| **13-5** Sunday lock + forfeits | GPT 6.1 Sol | GPT 6 Astra | — |
| **13-6** pipeline + draft | GPT 6.1 Sol + GPT 6 Astra (Math) (+ GPT 6 Sol: recruiting/draft UI) | each other's part | Playtest |
| **13-7** ripple | GPT 6 Sol | GPT 6 Astra | — |
| **14a** buses + VoiceDirector | GPT 6 Sol | GPT 6.1 Sol | Ear check |
| **14b** VoiceGen + AudioManifestHarness | GPT 6 Astra | GPT 6.1 Sol | Pilot run (your key) |
| **14c / SR-9** 6 conference personas + ~900 lines each | GPT 6 Sol | GPT 6.1 Sol (voice/quality) | **Approve taglines** |
| **14d** BroadcastBooth + highlights | GPT 6 Sol | GPT 6.1 Sol | Playtest |
| ~~**14e** voice notes~~ | **Cut 2026-10-10** (broadcasters only) | — | — |
| **14f** full generation + QA | GPT 6 Sol (retake sheets) | — | **Listening QA** |
| **15a** art direction | GPT 6.1 Sol (direction briefs + prompt packs) | — | **Pick the direction** |
| **15b** design system | GPT 6 Sol | GPT 6 Astra | Component gallery |
| **15c** screen rebuilds | GPT 6 Sol (all screens) | GPT 6 Astra | Screenshot review |
| **15d** art production | Owner + AI art tools (GPT 6 Sol: integration) | GPT 6.1 Sol (consistency) | **Taste** |
| **15e** motion / **15f** phone skins | GPT 6 Sol | GPT 6 Astra | — |
| **16a/16b** CutscenePlayer + Director + events | GPT 6 Sol | GPT 6.1 Sol | — |
| **16** CutsceneHarness | GPT 6 Astra | GPT 6.1 Sol | — |
| **16c–16e** cutscene content | GPT 6.1 Sol (story scripts) → GPT 6 Sol (definitions) | GPT 6 Astra | **Story sign-off** |
| **G-0** prototype (historical) | Built by Claude Opus / Claude Sonnet in 2026-10; retain original attribution | Completed review history remains in its brief | Outstanding timing/scene work is now **SR-3a: GPT 6 Astra / SR-3b: GPT 6 Sol**, reviewed per those rows |
| **G-1** attribute audit (done inside R-1) → retained batter-side clutch mapping | GPT 6 Astra (mapping + Math) | GPT 6.1 Sol | Clutch scope per SR-3a |
| ~~**G-2** full arcade physics + rules~~ | **Cancelled 2026-10-25** (scope_realignment §2.1) | — | — |
| ~~**G-3** arcade AI + full-game calibration~~ | **Cancelled 2026-10-25** | — | — |
| **G-4 → SR-3a** reduced integration: `ClutchRequest` from `EffectiveRatings` | GPT 6 Astra | GPT 6.1 Sol | See SR-3a |
| **G-5 → SR-3b** clutch overlay + batting practice (merged with G-9) | GPT 6 Sol | GPT 6 Astra | Playtest |
| ~~**G-6** team takeover + real-time baserunning~~ | **Cancelled 2026-10-25** | — | — |
| **G-7** venue catalog + screens | GPT 6 Sol (+ GPT 6 Sol: data); **paused beyond the HS slice** | GPT 6.1 Sol | Golden slice before mass generation |
| **G-8** field/venue art | Owner + art tools (GPT 6 Sol: integration); **paused beyond the HS slice** | GPT 6.1 Sol | **Taste; Gate 3** |
| **G-9 → SR-3b** clutch-only tuning/assists | GPT 6 Sol (GPT 6 Astra Math constants in SR-3a) | GPT 6.1 Sol | Playtest; no general difficulty framework |
| ~~old G-2 pitching mode / old G-8 Read v2~~ | **Retired 2026-10-13** by the arcade decision; docs in `archive/` | — | — |
| **L-0** docs + no-abortion content fix | Claude Opus | Gemini Pro (done) | **DONE 2026-10-10** |
| **L-1** schema v15 + identity + legends + poverty + New Career fields | GPT 6.1 Sol (+ GPT 6 Sol: New Career UI) | GPT 6 Astra | — |
| **L-2** legends roster, identity catalog, record map | GPT 6 Sol | GPT 6.1 Sol | **Ethnicity list, legend backstories** |
| **L-3** tokens, `unknown_fan`, father arc, `ProContractSignedEvent`, reveal ordering | GPT 6.1 Sol (scripts + event) | GPT 6 Astra | **Father's lines** |
| **L-4** pregnancy, placement, `AdoptChild`, maternity, missing piece | GPT 6.1 Sol (+ GPT 6 Sol: agency events) | GPT 6 Astra | — |
| **L-5** temptation, `Indiscretions`, `TemptationMath`, `Active_Effects`, clutch, partner pool + kin guard | GPT 6.1 Sol + GPT 6 Astra (Math) (+ **owner: `DiscoveryChance`**) | each other's part | Tone of the lure |
| **L-6** hidden children: intake, both reveals, PI search, reclaim, `Succeed` rostered heir | GPT 6.1 Sol | GPT 6 Astra (sim + Math) | — |
| **L-7** press clippings + News tab | GPT 6 Sol (runtime + tab) + GPT 6 Sol (articles) | GPT 6.1 Sol | — |
| **L-8** Trailblazer route content + calls | GPT 6.1 Sol (story) → GPT 6 Sol | GPT 6 Astra | **Story sign-off** |
| **S-1** substance engine, `ped_active` writer, Rehab absence, `AvatarDied` | GPT 6.1 Sol | GPT 6 Astra | — |
| **S-2** substance magnitudes | GPT 6 Astra (Math) | GPT 6.1 Sol | — |
| **S-3** substance content + PED re-gate | GPT 6.1 Sol (scripts) → GPT 6 Sol | GPT 6 Astra | — |
| **S-4** the asterisk (press + record book) | GPT 6 Sol (runtime + content) | GPT 6.1 Sol | — |
| **R-1** full-project review ([brief](tasks/R-1_full_review_brief.md); Fable 5.1's credit ran out before it began) | **GPT 6 Astra Pro** (owner reassignment, 2026-10-10) | Claude Opus (triage) | **Accept / reject proposals** |
| **17a** achievements + presence | GPT 6 Sol | GPT 6.1 Sol | — |
| **17b** legal + notices drafts | GPT 6 Astra | GPT 6.1 Sol | **Legal sign-off, trademark** |
| **17c** store copy + trailer script | GPT 6.1 Sol | — | **Approve** |
| **17d** release-candidate QA | GPT 6.1 Sol (plan + gates) | GPT 6 Astra | **Full playthrough** |
| **17e** submission + launch | Owner | — | Owner |

### Scope realignment assignments (SR-0 through SR-9)

[scope_realignment.md](design/scope_realignment.md) supplies slice scope and owner gates; this roster supplies current authors and reviewers. SR-0 and other completed rows retain historical attribution. All outstanding work uses the GPT-only roster in section 1a. Paused/cancelled status and unresolved scope decisions are unchanged.

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **SR-0** scope spec, GAME_VISION v3.0, GEMINI, BUILD_PLAN, TEAM, progress | Claude Opus (draft) → GPT (owner-assigned completion) | Owner | **Read GAME_VISION v3.0** |
| **SR-1a** event-framework `outcomes[]` | GPT 6.1 Sol | GPT 6 Astra | — |
| **SR-1b** Fencing + Robbery narrative arcs; mine hustle numbers | GPT 6.1 Sol → GPT 6 Sol | GPT 6 Astra | Story sign-off |
| **SR-1c** retire two screens + phone buttons; delete after parity | GPT 6 Sol | GPT 6 Astra | **Delete approval** |
| **SR-2a/b** stance engine / `GameManager` + stance UI | GPT 6.1 Sol / GPT 6 Sol | GPT 6 Astra (+ Math values) | Stance table sign-off |
| **SR-3a** `ClutchResolver` + `BatterIntentKind.Clutch` + tilt/Math constants | **GPT 6 Astra** | GPT 6.1 Sol | **Clutch feels right** |
| **SR-3b** clutch overlay + batting practice | GPT 6 Sol | GPT 6 Astra | Playtest |
| **SR-4** Gate 2 screens (scope_realignment §5 rows 6-12) | GPT 6 Sol | GPT 6 Astra | Screenshot review |
| **SR-5** Golden vertical slice (Gate 3) | Owner + AI art tools; GPT 6 Sol integration | GPT 6.1 Sol | **Taste** |
| **SR-6** `mods/` loader | GPT 6 Sol | GPT 6 Astra | — |
| **SR-7** Tabloid feed | GPT 6 Sol (runtime + content) | GPT 6.1 Sol | — |
| **SR-8** Rap Sheet exporter | GPT 6 Sol | GPT 6 Astra | — |
| **SR-9** six booths: persona framework, trigger catalog, scripts | GPT 6.1 Sol → GPT 6 Sol | GPT 6 Astra | **Approve taglines** |

**Execution priority:** GPT 6.1 Sol continues **13-1 on `p13/integration`**, reviewed by GPT 6 Astra, then 13-2/13-3 and the v15-DDL-only freeze gate. Prepare Gate 2 from scope_realignment §5: SR-1, SR-2 and SR-4 with 15a/15b/15c; the owner chooses art direction. Phases 14 and 16 production pause until after Gate 4; SR-9 informs the six-persona scope when audio resumes. SR-3a/b clutch work must not block the neutral `MicroGame` demo. SR-5 is Gate 3; SR-6–SR-8 follow Gate 4. No model starts the next slice without its brief and assignment.

---


## 3. How work flows

1. **Brief.** GPT 6.1 Sol writes a task brief in `docs/tasks/` before any delegated slice starts, from [TASK_BRIEF_TEMPLATE.md](tasks/TASK_BRIEF_TEMPLATE.md) ([voicegen_task_brief.md](tasks/voicegen_task_brief.md) is a worked example). It must name the **recommended model** and the **handoff**: context, hard constraints (allowed paths), inputs, outputs, tests, definition of done, out of scope.
2. **Branch + worktree.** Each task runs on its own branch named `p<phase>/<slice>-<model>` (e.g. `p14/14b-gpt`), **checked out in that model's own git worktree** (`git worktree add ..\dnd-<model> -b <branch> origin/main`). A branch alone is not isolation: two models in the same checkout share one working tree and one `bin/` folder (2026-10-10: two sessions edited `CareerManager.cs` and rebuilt the MC harness under each other). **Nobody builds on `main` directly, and nobody builds in another model's worktree.**
3. **Build and self-check.** The author runs the gates named in the brief and hands back a diff plus the gate output.
4. **Review.** **The author never reviews their own work.** The reviewer from §2 checks the diff against the brief and the design doc, and reruns the gates.
5. **Merge.** GPT 6.1 Sol merges once the review passes and the gates are green, then logs a compact entry in `progress.md`.
6. **Owner gates.** Where §2 names an owner gate, nothing merges until the owner says yes.
7. **Stop and hand off.** The **Author** column in §2 is each slice's **recommended model**. A model works only its own slice and **stops when the definition of done is met**. It never drifts into the next slice. It ends with a handoff note to the human: what was done, the commit, the gates and their results, what's open, and **"Recommended next: `<slice>` by `<model>`"** (plus the reviewer). The human starts the next model.

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

1. ~~**Who leads.**~~ **Resolved 2026-10-10:** the owner confirmed the original roster. **Current lead: GPT 6.1 Sol** under the 2026-10-25 GPT-only directive.
2. **Auto-committer policy (R-1 accepted):** no automatic commits/pushes to main or shared/integration branches. Disable it for those targets; any retained automation is restricted to an explicitly chosen personal scratch branch. Reviewed human-directed integration only. This records policy, not a claim that an external service has been disabled.
3. ~~**Repo visibility.**~~ **Resolved 2026-10-10:** the repo is private.

## R-1 accepted repair assignments (2026-10-10)

The owner accepted all [R-1 answers](review/r1/06_owner_questions.md). The [BUILD_PLAN execution table](BUILD_PLAN.md#r-1-accepted-execution-order-2026-10-10) links the eight scoped briefs. GPT 6.1 Sol owns SAVE-1/SAVE-2/FAMILY and durable REVEAL/STEAM work; *(from 2026-10-13, §1a)* GPT 6 Astra independently reviews them and GPT 6 Sol owns the presentation/Steam integration. GPT 6 Astra owns R1-MATH (GPT 6.1 Sol reviews); GPT 6 Astra owns accepted EFFECTS projections and LEARN engine DTO/session seams, GPT 6.1 Sol development, GPT 6 Sol LEARN UI. These assignments supplement existing slices; they do not authorize simultaneous edits to shared files. Stop and hand off per §3.
