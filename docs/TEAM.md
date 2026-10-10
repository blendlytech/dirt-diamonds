# Dirt & Diamonds — Team Roster & Operating Model

**Status:** **confirmed by the owner 2026-10-10** (proposed the same day by Claude Opus 5.5 at the owner's request). It replaces the single-lead model in BUILD_PLAN's Model Delegation Doctrine. The owner can reassign any role. Roles are assigned by **kind of work**, not by brand. If a model turns out stronger or weaker at something, move the work and update this file.

**Read with:** [GAME_VISION.md](GAME_VISION.md) (what we're building), [BUILD_PLAN.md](BUILD_PLAN.md) (Phases 13–17), [progress.md](progress.md) (where we are).

---

## 1. Who does what

| Model | Role | Owns | Never does |
| :-- | :-- | :-- | :-- |
| **The owner (you)** | Director and publisher | Vision, taste, art-direction picks, tagline approval, listening QA, playtests, ElevenLabs account/key/plan, Steamworks, trademark, pricing, every external submission and payment | — |
| **Claude Opus 5.5** | **Integration lead and chief architect** | The Phase 13 critical path in the calibrated core (roster capacities, schedules, pipeline/draft, re-band); final merge gate for every slice; writing task briefs for other models; reviewing GPT and Gemini Pro work; narrative quality (story cutscene scripts, store copy); release-candidate test plan | Merging its own sim changes without an independent review; owner-only actions |
| **Gemini 3.1 Pro** | **Senior engineer, presentation runtime** | Phase 14 runtime (`VoiceDirector`, buses, `BroadcastBooth`); Phase 16 runtime (`CutscenePlayer`, `CutsceneDirector`, new Core events); Phase 13 playoffs (13-4) and roster lock/forfeits (13-5); Phase 17a achievements/presence; **independent reviewer of Claude Opus's sim diffs** and of Sonnet's UI | Content writing at volume |
| **Gemini 3.1 Pro Math** | **Calibration and math owner** | Every "is the number right" question: per-tier bands and the new Pro guard, College ranking weights, short-rest/overuse risk, draft-pool conservation, announcer call-density weights | Shipping code without the harness proving its math |
| **Gemini 3.8 Flash** | **Content at volume** | Announcer personas and ~5,600 script lines (14c); ~200 voice-note scripts (14e); cutscene definition JSON from approved scripts (16c–16e); team seed data (names, parks, colors); retake sheets; simpler screens and phone skins (15c/15f) | Touching `Assets/Simulation/`; approving its own content |
| **Claude Sonnet 5.5** | **UI and feature engineer** | Phase 15 design system and screen rebuilds (15b, 15c, 15e); the Phase 13 league UI (bracket, draft board, recruiting, roster status, standings); 13-7 ripple fixes; the Around the League card and phone voice-note bubble | Calibrated-core changes |
| **GPT 6 Astra Pro** | **Standalone tools and release paperwork** | Self-contained tools with their own test suites: `Tools/VoiceGen` (brief ready), `Tools/AudioManifestHarness`, `Tools/CutsceneHarness`; the third-party notices generator; drafts of the Phase 17b legal/disclosure package | Game code under `Assets/`; real API runs; anything involving the live key |

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
| **14e** voice notes | Flash (scripts) + Sonnet (bubble UI) | Claude Opus | — |
| **14f** full generation + QA | Flash (retake sheets) | — | **Listening QA** |
| **15a** art direction | Claude Opus (direction briefs + prompt packs) | — | **Pick the direction** |
| **15b** design system | Sonnet | Gemini Pro | Component gallery |
| **15c** screen rebuilds | Sonnet (complex) / Flash (simple) | Gemini Pro | Screenshot review |
| **15d** art production | Owner + AI art tools (Flash: integration) | Claude Opus (consistency) | **Taste** |
| **15e** motion / **15f** phone skins | Sonnet / Flash | Gemini Pro | — |
| **16a/16b** CutscenePlayer + Director + events | Gemini Pro | Claude Opus | — |
| **16** CutsceneHarness | GPT Astra Pro | Claude Opus | — |
| **16c–16e** cutscene content | Claude Opus (story scripts) → Flash (definitions) | Gemini Pro (wiring) | **Story sign-off** |
| **17a** achievements + presence | Gemini Pro | Claude Opus | — |
| **17b** legal + notices drafts | GPT Astra Pro | Claude Opus | **Legal sign-off, trademark** |
| **17c** store copy + trailer script | Claude Opus | — | **Approve** |
| **17d** release-candidate QA | Claude Opus (plan + gates) | Gemini Pro | **Full playthrough** |
| **17e** submission + launch | Owner | — | Owner |

**Parallel tracks from day one:** Claude Opus runs Phase 13 (the critical path). Gemini Pro starts 14a. GPT Astra Pro builds VoiceGen. Flash drafts announcer personas. Claude Opus writes the 15a art-direction briefs alongside 13-0/13-1.

---

## 3. How work flows

1. **Brief.** Claude Opus writes a task brief in `docs/tasks/` before any delegated slice starts, using [voicegen_task_brief.md](tasks/voicegen_task_brief.md) as the template: context, hard constraints (allowed paths), inputs, outputs, tests, definition of done, out of scope.
2. **Branch + worktree.** Each task runs on its own branch named `p<phase>/<slice>-<model>` (e.g. `p14/14b-gpt`), **checked out in that model's own git worktree** (`git worktree add ..\dnd-<model> -b <branch> origin/main`). A branch alone is not isolation: two models in the same checkout share one working tree and one `bin/` folder (2026-10-10: two sessions edited `CareerManager.cs` and rebuilt the MC harness under each other). **Nobody builds on `main` directly, and nobody builds in another model's worktree.**
3. **Build and self-check.** The author runs the gates named in the brief and hands back a diff plus the gate output.
4. **Review.** **The author never reviews their own work.** The reviewer from §2 checks the diff against the brief and the design doc, and reruns the gates.
5. **Merge.** Claude Opus merges once the review passes and the gates are green, then logs a compact entry in `progress.md`.
6. **Owner gates.** Where §2 names an owner gate, nothing merges until the owner says yes.

### Gates (from GEMINI.md §9)
- Anything under `Assets/Simulation/` → `run_monte_carlo_batch`. The resolver guard stays byte-exact; re-baselining a season guard needs explicit sanction.
- Event content → `check_event_graph_integrity`. Voice assets → `validate_audio_manifest`. Cutscenes → `CutsceneHarness`. Schema → `validate_sqlite_schema`. Exports → `validate_steamworks_native`.
- Presentation code (Phases 14–16 runtime, all of Phase 15) must leave the MC and Gritty harnesses **untouched** (`git diff --stat` shows no `Assets/Simulation/` paths).

---

## 4. Ground rules for every model

- **The repo is public, and an auto-committer pushes.** Never write a secret into any file. The ElevenLabs key lives only in the untracked `.env` or an environment variable. `git ls-files .env` must print nothing.
- **Owner-only actions:** ElevenLabs generation runs with the real key, Steamworks, trademark filings, pricing, store submission, force-pushes and history rewrites.
- **One source of truth per question:** the vision (GAME_VISION), the plan (BUILD_PLAN), state (progress.md), specs (`docs/design/`), roles (this file).
- **Instruction files:** Gemini reads `GEMINI.md`; Claude Code reads `CLAUDE.md`; GPT-based agents typically read `AGENTS.md`. Until they're unified (recommended: one canonical `AGENTS.md` with `CLAUDE.md` and `GEMINI.md` pointing to it), every model's task brief links GEMINI.md §1–§9 as binding.

## 5. Open decisions for the owner

1. ~~**Who leads.**~~ **Resolved 2026-10-10:** the owner confirmed this roster; Claude Opus 5.5 is integration lead. GEMINI.md and league_restructure.md now point here.
2. **The auto-committer.** Six models working in parallel plus an auto-committer pushing to `main` will collide. Recommended: turn it off, or limit it to a personal scratch branch, and merge only through review (§3).
3. **Repo visibility.** Recommended: make `blendlytech/dirt-diamonds` private until launch.
