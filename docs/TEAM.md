# Dirt & Diamonds — Team Roster & Operating Model

**Status:** **confirmed by the owner 2026-10-10; roster updated 2026-10-13** (Gemini 3.1 Pro and Pro Math unavailable, §1a) (proposed the same day by Claude Opus 5.5 at the owner's request). It replaced the single-lead "Model Delegation Doctrine" that used to sit in BUILD_PLAN (removed 2026-10-10). The owner can reassign any role. Roles are assigned by **kind of work**, not by brand. If a model turns out stronger or weaker at something, move the work and update this file.

**Read with:** [GAME_VISION.md](GAME_VISION.md) (what we're building, v2), [BUILD_PLAN.md](BUILD_PLAN.md) (Phases 13–17 and Phase L), [progress.md](progress.md) (where we are), [README.md](README.md) (the docs map). Older docs name past authors ("Fable 5", "Opus 4.8", "Sonnet 5"); those are not roles. **This file is the only source of current roles.**

---

## 1. Who does what

| Model | Role | Owns | Never does |
| :-- | :-- | :-- | :-- |
| **The owner (you)** | Director and publisher | Vision, taste, art-direction picks, tagline approval, listening QA, playtests, ElevenLabs account/key/plan, Steamworks, trademark, pricing, every external submission and payment | — |
| **Claude Opus 5.5** | **Integration lead and chief architect** | The Phase 13 critical path in the calibrated core (roster capacities, schedules, pipeline/draft, re-band); final merge gate for every slice; writing task briefs for other models; reviewing GPT's work (sim, math and tools) and Sonnet's runtime work; narrative quality (story cutscene scripts, store copy); release-candidate test plan | Merging its own sim changes without an independent review; owner-only actions |
| ~~**Gemini 3.1 Pro**~~ | **Unavailable from 2026-10-13** (owner) | Its roles moved: independent sim-diff review → GPT 6 Astra Pro; presentation runtime (14a, 14d, 16a/b, 17a, L-7, S-4) → Claude Sonnet; sim-side league rules (13-4, 13-5) → Claude Opus. See §1a | — |
| ~~**Gemini 3.1 Pro Math**~~ | **Unavailable from 2026-10-13** (owner) | Calibration and math moved to **GPT 6 Astra Pro** (see §1a) | — |
| **Gemini 3.8 Flash** | **Content at volume** | Announcer personas and ~5,600 script lines (14c); cutscene definition JSON from approved scripts (16c–16e); team seed data (names, parks, colors); retake sheets; simpler screens and phone skins (15c/15f) | Touching `Assets/Simulation/`; approving its own content |
| **Claude Sonnet 5.5** | **UI and feature engineer** | Phase 15 design system and screen rebuilds (15b, 15c, 15e); **from 2026-10-13 also the presentation runtime** (14a buses + `VoiceDirector`, 14d `BroadcastBooth`, 16a/b `CutscenePlayer`/`CutsceneDirector`, 17a achievements/presence, L-7 press runtime, S-4); the Phase 13 league UI (bracket, draft board, recruiting, roster status, standings); 13-7 ripple fixes; the Around the League card | Calibrated-core changes |
| **Claude Fable 5.1** | **Inactive** (joined 2026-10; prepaid API credit exhausted 2026-10-10) | Authored the G-2 pitching-mode design ([pitching_mode.md](archive/pitching_mode.md) rev 1). Its R-1 review role moved to GPT 6 Astra Pro (owner, 2026-10-10). Re-engage only through a new brief with fresh credit | Merging its own changes; changing GAME_VISION without the owner's yes; touching `Assets/Simulation/` outside a briefed slice |
| **GPT 6 Astra Pro** | **Senior engineer: gameplay models + standalone tools; calibration and math owner; independent sim-diff reviewer** (owner 2026-10-10, expanded 2026-10-13 when Gemini 3.1 Pro became unavailable) | **Arcade engine** G-1 mapping, G-2, G-3, G-4 (arcade §12); **calibration** (per-tier bands, the new Pro guard, physics constants, difficulty targets, R1-MATH, S-2 and every former "Math" item); **independent review** of Claude Opus's and Claude Sonnet's sim diffs; R-1 (done 2026-10-10); self-contained tools with their own test suites: `Tools/VoiceGen`, `Tools/AudioManifestHarness`, `Tools/CutsceneHarness`, notices generator; 17b drafts | Merging its own sim changes without Gemini Pro's independent review and the Opus merge gate; game code under `Assets/` outside a briefed slice; real API runs; anything involving the live key |


### 1a. Role succession (owner, 2026-10-13): Gemini 3.1 Pro and Pro Math are unavailable

Gemini 3.8 Flash stays. Older docs, specs and briefs still name Gemini Pro; **read them through this table** (this file is the only source of current roles):

| Where a doc says | Read it as |
| :-- | :-- |
| **Gemini Pro reviews** a Claude Opus or Claude Sonnet sim/engine diff | **GPT 6 Astra Pro** reviews |
| **Gemini Pro reviews** GPT 6 Astra Pro's work | **Claude Opus** reviews |
| **Gemini Pro Math / "Math"** owns or signs a number | **GPT 6 Astra Pro** (Math). If GPT also authored the slice, **Claude Opus** reviews the math |
| **Gemini Pro authors** presentation runtime (14a, 14d, 16a/b, 17a, L-7 runtime, S-4) | **Claude Sonnet** authors; Claude Opus reviews |
| **Gemini Pro authors** sim-side league rules (13-4 playoffs + ranking, 13-5 Sunday lock + forfeits) | **Claude Opus** authors; GPT 6 Astra Pro reviews |
| **Gemini Pro reviews** a UI or presentation diff (15b/15c/15e, G-5) | **GPT 6 Astra Pro** reviews |
| Joint **Claude Opus + GPT 6 Astra Pro** slices | Each reviews the other's part; Claude Opus is still the merge gate |

**Independence rule:** the author never reviews their own work, and a same-family review (e.g. Opus reviewing Sonnet's sim code) is a stand-in, not an independent review. Sim-diff merges need a GPT review or a Claude review of GPT's work.

---

## 2. Phase-by-phase assignments

| Slice | Author | Reviewer | Owner gate |
| :-- | :-- | :-- | :-- |
| **13-0** sqlite MCP + MC flake recap/seed | Claude Opus | Gemini Pro (done 2026-10-10) | — |
| **13-1** schema v14 + world-gen | Claude Opus (+ Flash: team seed data) | GPT 6 Astra Pro | Team names |
| **13-2** roster capacities + innings + starter selection | Claude Opus | GPT 6 Astra Pro (sim + Math) | — |
| **13-3** schedules + calendars + re-band + new Pro guard | Claude Opus + GPT 6 Astra Pro (Math) | each other's part | Sanction the new guard |
| **13-4** playoffs + ranking | Claude Opus + GPT 6 Astra Pro (Math) (+ Sonnet: bracket UI) | each other's part | — |
| **13-5** Sunday lock + forfeits | Claude Opus | GPT 6 Astra Pro | — |
| **13-6** pipeline + draft | Claude Opus + GPT 6 Astra Pro (Math) (+ Sonnet: recruiting/draft UI) | each other's part | Playtest |
| **13-7** ripple | Sonnet | GPT 6 Astra Pro | — |
| **14a** buses + VoiceDirector | Claude Sonnet | Claude Opus | Ear check |
| **14b** VoiceGen + AudioManifestHarness | GPT Astra Pro | Claude Opus | Pilot run (your key) |
| **14c** 56 personas + scripts | Flash | Claude Opus (voice/quality) | **Approve taglines** |
| **14d** BroadcastBooth + highlights | Claude Sonnet | Claude Opus | Playtest |
| ~~**14e** voice notes~~ | **Cut 2026-10-10** (broadcasters only) | — | — |
| **14f** full generation + QA | Flash (retake sheets) | — | **Listening QA** |
| **15a** art direction | Claude Opus (direction briefs + prompt packs) | — | **Pick the direction** |
| **15b** design system | Sonnet | GPT 6 Astra Pro | Component gallery |
| **15c** screen rebuilds | Sonnet (complex) / Flash (simple) | GPT 6 Astra Pro | Screenshot review |
| **15d** art production | Owner + AI art tools (Flash: integration) | Claude Opus (consistency) | **Taste** |
| **15e** motion / **15f** phone skins | Sonnet / Flash | GPT 6 Astra Pro | — |
| **16a/16b** CutscenePlayer + Director + events | Claude Sonnet | Claude Opus | — |
| **16** CutsceneHarness | GPT Astra Pro | Claude Opus | — |
| **16c–16e** cutscene content | Claude Opus (story scripts) → Flash (definitions) | Claude Sonnet (wiring) | **Story sign-off** |
| **G-0** arcade feel prototype (batter vs pitcher, placeholder art) | **G-0a physics: Claude Opus (built 2026-10-13, `pG/G-0a-opus`)** · G-0b scene: Claude Sonnet | Claude Opus (stand-in review done 2026-10-13; same family, not independent) → **independent re-review: GPT 6 Astra Pro** | **"Hitting is fun"** |
| **G-1** attribute audit (**done inside R-1**, GPT 6 Astra Pro) → ratings-to-physics mapping (gameplay_core §1, arcade §4) | GPT 6 Astra Pro (mapping + Math) | Claude Opus | ~~Default difficulty target~~ (decided: O1 skill band) |
| **G-2** arcade physics core + rules engine + `ArcadeHarness` | GPT 6 Astra Pro (owner 2026-10-10: senior engineer for gameplay models) | Claude Opus (sim diff + merge gate) | — |
| **G-3** arcade AI + calibration to Phase 13 bands | GPT 6 Astra Pro (engine + Math) | Claude Opus | — |
| **G-4** integration (`EffectiveRatings`, `Player_Fielding`, flush, autopilot, booth beats, R1-EFFECTS projections) | GPT 6 Astra Pro | Claude Opus | — |
| **G-5** arcade presentation + controls + watch & skip | Claude Sonnet | GPT 6 Astra Pro | Playtest |
| ~~**G-6** take over the team + baserunning~~ | **Deferred past 1.0** (owner, 2026-10-16) | — | — |
| **G-7** venue catalog (68) + venue screens (reusable kits + team dressing, R-1 Q13) | Claude Sonnet (+ Flash: catalog data) | Claude Opus | — |
| **G-8** art: 16-bit sprites, field kits, venue art | Owner + artist/AI art tools (Flash: integration) | Claude Opus (consistency) | **Taste; art source** |
| **G-9** tuning + difficulty presets (O1 skill band as the default target) | GPT 6 Astra Pro (Math) + Claude Sonnet (data) | Claude Opus | **Sign-off; Classic's fate** |
| ~~old G-2 pitching mode / old G-8 Read v2~~ | **Retired 2026-10-13** by the arcade decision; docs in `archive/` | — | — |
| **L-0** docs + no-abortion content fix | Claude Opus | Gemini Pro (done) | **DONE 2026-10-10** |
| **L-1** schema v15 + identity + legends + poverty + New Career fields | Claude Opus (+ Sonnet: New Career UI) | GPT 6 Astra Pro | — |
| **L-2** legends roster, identity catalog, record map | Flash | Claude Opus | **Ethnicity list, legend backstories** |
| **L-3** tokens, `unknown_fan`, father arc, `ProContractSignedEvent`, reveal ordering | Claude Opus (scripts + event) | GPT 6 Astra Pro | **Father's lines** |
| **L-4** pregnancy, placement, `AdoptChild`, maternity, missing piece | Claude Opus (+ Flash: agency events) | GPT 6 Astra Pro | — |
| **L-5** temptation, `Indiscretions`, `TemptationMath`, `Active_Effects`, clutch, partner pool + kin guard | Claude Opus + GPT 6 Astra Pro (Math) (+ **owner: `DiscoveryChance`**) | each other's part | Tone of the lure |
| **L-6** hidden children: intake, both reveals, PI search, reclaim, `Succeed` rostered heir | Claude Opus | GPT 6 Astra Pro (sim + Math) | — |
| **L-7** press clippings + News tab | Claude Sonnet (runtime + tab) + Flash (articles) | Claude Opus | — |
| **L-8** Trailblazer route content + calls | Claude Opus (story) → Flash | Claude Sonnet (wiring) | **Story sign-off** |
| **S-1** substance engine, `ped_active` writer, Rehab absence, `AvatarDied` | Claude Opus | GPT 6 Astra Pro | — |
| **S-2** substance magnitudes | GPT 6 Astra Pro (Math) | Claude Opus | — |
| **S-3** substance content + PED re-gate | Claude Opus (scripts) → Flash | GPT 6 Astra Pro | — |
| **S-4** the asterisk (press + record book) | Claude Sonnet + Flash | Claude Opus | — |
| **R-1** full-project review ([brief](tasks/R-1_full_review_brief.md); Fable 5.1's credit ran out before it began) | **GPT 6 Astra Pro** (owner reassignment, 2026-10-10) | Claude Opus (triage) | **Accept / reject proposals** |
| **17a** achievements + presence | Claude Sonnet | Claude Opus | — |
| **17b** legal + notices drafts | GPT Astra Pro | Claude Opus | **Legal sign-off, trademark** |
| **17c** store copy + trailer script | Claude Opus | — | **Approve** |
| **17d** release-candidate QA | Claude Opus (plan + gates) | GPT 6 Astra Pro | **Full playthrough** |
| **17e** submission + launch | Owner | — | Owner |

**Parallel tracks from day one:** Claude Opus runs Phase 13 (the critical path). Claude Sonnet takes 14a (from Gemini Pro, 2026-10-13). GPT Astra Pro builds VoiceGen. Flash drafts announcer personas. Claude Opus writes the 15a art-direction briefs alongside 13-0/13-1.

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
2. **Auto-committer policy (R-1 accepted):** no automatic commits/pushes to main or shared/integration branches. Disable it for those targets; any retained automation is restricted to an explicitly chosen personal scratch branch. Reviewed human-directed integration only. This records policy, not a claim that an external service has been disabled.
3. ~~**Repo visibility.**~~ **Resolved 2026-10-10:** the repo is private.

## R-1 accepted repair assignments (2026-10-10)

The owner accepted all [R-1 answers](review/r1/06_owner_questions.md). The [BUILD_PLAN execution table](BUILD_PLAN.md#r-1-accepted-execution-order-2026-10-10) links the eight scoped briefs. Opus owns SAVE-1/SAVE-2/FAMILY and durable REVEAL/STEAM work; *(from 2026-10-13, §1a)* GPT 6 Astra Pro independently reviews them and Claude Sonnet owns the presentation/Steam integration. GPT 6 Astra Pro owns R1-MATH (Claude Opus reviews); GPT owns accepted EFFECTS projections and LEARN engine DTO/session seams, Opus development, Sonnet LEARN UI. These assignments supplement existing slices; they do not authorize simultaneous edits to shared files. Stop and hand off per §3.
