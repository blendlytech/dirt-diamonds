# Dirt & Diamonds — Team Roster & Operating Model

**Status:** **confirmed by the owner 2026-10-10** (proposed the same day by Claude Opus 5.5 at the owner's request). It replaced the single-lead "Model Delegation Doctrine" that used to sit in BUILD_PLAN (removed 2026-10-10). The owner can reassign any role. Roles are assigned by **kind of work**, not by brand. If a model turns out stronger or weaker at something, move the work and update this file.

**Read with:** [GAME_VISION.md](GAME_VISION.md) (what we're building, v2), [BUILD_PLAN.md](BUILD_PLAN.md) (Phases 13–17 and Phase L), [progress.md](progress.md) (where we are), [README.md](README.md) (the docs map). Older docs name past authors ("Fable 5", "Opus 4.8", "Sonnet 5"); those are not roles. **This file is the only source of current roles.**

---

## 1. Who does what

| Model | Role | Owns | Never does |
| :-- | :-- | :-- | :-- |
| **The owner (you)** | Director and publisher | Vision, taste, art-direction picks, tagline approval, listening QA, playtests, ElevenLabs account/key/plan, Steamworks, trademark, pricing, every external submission and payment | — |
| **Claude Opus 5.5** | **Integration lead and chief architect** | The Phase 13 critical path in the calibrated core (roster capacities, schedules, pipeline/draft, re-band); final merge gate for every slice; writing task briefs for other models; reviewing GPT and Gemini Pro work; narrative quality (story cutscene scripts, store copy); release-candidate test plan | Merging its own sim changes without an independent review; owner-only actions |
| **Gemini 3.1 Pro** | **Senior engineer, presentation runtime** | Phase 14 runtime (`VoiceDirector`, buses, `BroadcastBooth`); Phase 16 runtime (`CutscenePlayer`, `CutsceneDirector`, new Core events); Phase 13 playoffs (13-4) and roster lock/forfeits (13-5); Phase 17a achievements/presence; **independent reviewer of Claude Opus's sim diffs** and of Sonnet's UI | Content writing at volume |
| **Gemini 3.1 Pro Math** | **Calibration and math owner** | Every "is the number right" question: per-tier bands and the new Pro guard, College ranking weights, short-rest/overuse risk, draft-pool conservation, announcer call-density weights | Shipping code without the harness proving its math |
| **Gemini 3.8 Flash** | **Content at volume** | Announcer personas and ~5,600 script lines (14c); cutscene definition JSON from approved scripts (16c–16e); team seed data (names, parks, colors); retake sheets; simpler screens and phone skins (15c/15f) | Touching `Assets/Simulation/`; approving its own content |
| **Claude Sonnet 5.5** | **UI and feature engineer** | Phase 15 design system and screen rebuilds (15b, 15c, 15e); the Phase 13 league UI (bracket, draft board, recruiting, roster status, standings); 13-7 ripple fixes; the Around the League card | Calibrated-core changes |
| **Claude Fable 5.1** | **Independent full-project reviewer** (joined 2026-10) | A complete read of the vision, docs and code; findings, proposals, add-ons and disagreements per [tasks/fable_5_1_full_review_brief.md](tasks/fable_5_1_full_review_brief.md). Its output feeds the owner and the integration lead; accepted proposals become slices through the normal flow | Merging its own changes; changing GAME_VISION without the owner's yes; touching `Assets/Simulation/` outside a briefed slice |
| **GPT 6 Astra Pro** | **Senior engineer: gameplay models + standalone tools** (expanded by the owner 2026-10-10: a strong model with plentiful subscription usage, so prefer it for heavy design and engineering over metered credit) | **G-2 pitching-mode engine** (G-2a/b/d, from the final [pitching_mode.md](design/pitching_mode.md)); **G-8 Read v2 batting re-band** (design + engine, [brief](tasks/G-8_read_v2_batting_reband_brief.md)); self-contained tools with their own test suites: `Tools/VoiceGen` (brief ready), `Tools/AudioManifestHarness`, `Tools/CutsceneHarness`; the third-party notices generator; drafts of the Phase 17b legal/disclosure package | Merging its own sim changes without Gemini Pro's independent review and the Opus merge gate; game code under `Assets/` outside a briefed slice; real API runs; anything involving the live key |

---

## 2. Phase-by-phase assignments

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **13-0** sqlite MCP + MC flake recap/seed | Claude Opus | Gemini Pro | — |
| **13-1** schema v14 + world-gen | Claude Opus (+ Flash: team seed data) | Gemini Pro | Team names |
| **13-2** roster capacities + innings + starter selection | Claude Opus | Gemini Pro + Math | — |
| **13-3** schedules + calendars + re-band + new Pro guard | Claude Opus + Math | Gemini Pro | Sanction the new guard |
| **13-4** playoffs + ranking | Gemini Pro + Math (+ Sonnet: bracket UI) | Claude Opus | — |
| **13-5** Sunday lock + forfeits | Gemini Pro | Claude Opus | — |
| **13-6** pipeline + draft | Claude Opus + Math (+ Sonnet: recruiting/draft UI) | Gemini Pro | Playtest |
| **13-7** ripple | Sonnet | Gemini Pro | — |
| **14a** buses + VoiceDirector | Gemini Pro | Claude Opus | Ear check |
| **14b** VoiceGen + AudioManifestHarness | GPT Astra Pro | Claude Opus | Pilot run (your key) |
| **14c** 56 personas + scripts | Flash | Claude Opus (voice/quality) | **Approve taglines** |
| **14d** BroadcastBooth + highlights | Gemini Pro (+ Sonnet: card UI) | Claude Opus | Playtest |
| ~~**14e** voice notes~~ | **Cut 2026-10-10** (broadcasters only) | — | — |
| **14f** full generation + QA | Flash (retake sheets) | — | **Listening QA** |
| **15a** art direction | Claude Opus (direction briefs + prompt packs) | — | **Pick the direction** |
| **15b** design system | Sonnet | Gemini Pro | Component gallery |
| **15c** screen rebuilds | Sonnet (complex) / Flash (simple) | Gemini Pro | Screenshot review |
| **15d** art production | Owner + AI art tools (Flash: integration) | Claude Opus (consistency) | **Taste** |
| **15e** motion / **15f** phone skins | Sonnet / Flash | Gemini Pro | — |
| **16a/16b** CutscenePlayer + Director + events | Gemini Pro | Claude Opus | — |
| **16** CutsceneHarness | GPT Astra Pro | Claude Opus | — |
| **16c–16e** cutscene content | Claude Opus (story scripts) → Flash (definitions) | Gemini Pro (wiring) | **Story sign-off** |
| **G-1** attribute audit (verify gameplay_core §1, settle weights) | Claude Fable 5.1 (in R-1; **blocked: R-1 not started**) → Gemini Pro Math | Claude Opus | ~~How much skill is worth~~ (decided, O1) |
| *(sequencing)* G-2 engine and G-8 both touch `PitchChain.SimulatePa` | GPT 6 Astra Pro owns both, so one model sequences them: **G-8a design → G-2a → G-2b → G-8b**, or G-8b sharing G-2's tilt helper | — | — |
| **G-8** Read v2 batting re-band to the ±.100 OPS skill band (owner O1, 2026-10-10) | GPT 6 Astra Pro (design G-8a, then engine G-8b) | Claude Opus (design + merge gate), Gemini Pro (sim diff), Math (constants) | Accept the design; playtest |
| **G-2** pitching mode: **design** (brief: [tasks/G-2_pitching_design_fable_brief.md](tasks/G-2_pitching_design_fable_brief.md); **rev 2 done**: Fable's rev 1 plus the feasibility review applied by Claude Opus, since Fable's credit ran out; awaiting Math curve review) → engine | **Design: Claude Fable 5.1** (owner, 2026-10-10; prepaid API credit, $5 approval steps) → **engine: GPT 6 Astra Pro** (owner reassignment 2026-10-10: G-2a, G-2b and G-2d engine; supersedes the owner column of pitching_mode.md §10) + Math; UI (G-2c, G-2d UI): Claude Sonnet | Design: Claude Opus (feasibility) + Gemini Pro Math (curves); engine: Gemini Pro (sim diff) + Claude Opus (spec fidelity, merge gate) | Owner accepts the spec; playtest |
| **G-3** at-bat view rebuild (batting + pitching UI) | Claude Sonnet | Gemini Pro | Screenshot review; playtest |
| **G-4** venue catalog (68) + venue screens | Claude Sonnet (+ Flash: catalog data) | Claude Opus | — |
| **G-5** venue art | Owner + AI art tools (Flash: integration) | Claude Opus (consistency) | **Taste** |
| **G-6** stadium mapping + optional park factors | Claude Sonnet / Math (factors) | Claude Opus | **Park factors yes/no** |
| **G-7** fielding moments (if approved) | Claude Opus + Claude Sonnet | Gemini Pro | **Approve the feature** |
| **L-0** docs + no-abortion content fix | Claude Opus | Gemini Pro | **DONE 2026-10-10** |
| **L-1** schema v15 + identity + legends + poverty + New Career fields | Claude Opus (+ Sonnet: New Career UI) | Gemini Pro | — |
| **L-2** legends roster, identity catalog, record map | Flash | Claude Opus | **Ethnicity list, legend backstories** |
| **L-3** tokens, `unknown_fan`, father arc, `ProContractSignedEvent`, reveal ordering | Claude Opus (scripts) + Gemini Pro (event) | each other | **Father's lines** |
| **L-4** pregnancy, placement, `AdoptChild`, maternity, missing piece | Claude Opus (+ Flash: agency events) | Gemini Pro | — |
| **L-5** temptation, `Indiscretions`, `TemptationMath`, `Active_Effects`, clutch, partner pool + kin guard | Claude Opus + Math (+ **owner: `DiscoveryChance`**) | Gemini Pro | Tone of the lure |
| **L-6** hidden children: intake, both reveals, PI search, reclaim, `Succeed` rostered heir | Claude Opus | Gemini Pro + Math | — |
| **L-7** press clippings + News tab | Gemini Pro (runtime) + Sonnet (tab) + Flash (articles) | Claude Opus | — |
| **L-8** Trailblazer route content + calls | Claude Opus (story) → Flash | Gemini Pro (wiring) | **Story sign-off** |
| **S-1** substance engine, `ped_active` writer, Rehab absence, `AvatarDied` | Claude Opus | Gemini Pro | — |
| **S-2** substance magnitudes | Math | Claude Opus | — |
| **S-3** substance content + PED re-gate | Claude Opus (scripts) → Flash | Gemini Pro | — |
| **S-4** the asterisk (press + record book) | Gemini Pro + Flash | Claude Opus | — |
| **R-1** full-project review — **not started** (Fable 5.1's API credit ran out after the G-2 design, 2026-10-10; `docs/review/fable_5_1/` does not exist) | Claude Fable 5.1 (**owner to confirm: refund credit or reassign**) | Claude Opus (triage) | **Accept / reject proposals** |
| **17a** achievements + presence | Gemini Pro | Claude Opus | — |
| **17b** legal + notices drafts | GPT Astra Pro | Claude Opus | **Legal sign-off, trademark** |
| **17c** store copy + trailer script | Claude Opus | — | **Approve** |
| **17d** release-candidate QA | Claude Opus (plan + gates) | Gemini Pro | **Full playthrough** |
| **17e** submission + launch | Owner | — | Owner |

**Parallel tracks from day one:** Claude Opus runs Phase 13 (the critical path). Gemini Pro starts 14a. GPT Astra Pro builds VoiceGen. Flash drafts announcer personas. Claude Opus writes the 15a art-direction briefs alongside 13-0/13-1.

---

## 3. How work flows

1. **Brief.** Claude Opus writes a task brief in `docs/tasks/` before any delegated slice starts, from [TASK_BRIEF_TEMPLATE.md](tasks/TASK_BRIEF_TEMPLATE.md) ([voicegen_task_brief.md](tasks/voicegen_task_brief.md) is a worked example). It must name the **recommended model** and the **handoff**: context, hard constraints (allowed paths), inputs, outputs, tests, definition of done, out of scope.
2. **Branch + worktree.** Each task runs on its own branch named `p<phase>/<slice>-<model>` (e.g. `p14/14b-gpt`), **checked out in that model's own git worktree** (`git worktree add ..\dnd-<model> -b <branch> origin/main`). A branch alone is not isolation: two models in the same checkout share one working tree and one `bin/` folder (2026-10-10: two sessions edited `CareerManager.cs` and rebuilt the MC harness under each other). **Nobody builds on `main` directly, and nobody builds in another model's worktree.**
3. **Build and self-check.** The author runs the gates named in the brief and hands back a diff plus the gate output.
4. **Review.** **The author never reviews their own work.** The reviewer from §2 checks the diff against the brief and the design doc, and reruns the gates.
5. **Merge.** Claude Opus merges once the review passes and the gates are green, then logs a compact entry in `progress.md`.
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

1. ~~**Who leads.**~~ **Resolved 2026-10-10:** the owner confirmed this roster; Claude Opus 5.5 is integration lead. GEMINI.md and league_restructure.md now point here.
2. **The auto-committer.** Six models working in parallel plus an auto-committer pushing to `main` will collide. Recommended: turn it off, or limit it to a personal scratch branch, and merge only through review (§3).
3. ~~**Repo visibility.**~~ **Resolved 2026-10-10:** the repo is private.
