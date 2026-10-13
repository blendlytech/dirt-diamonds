# Project Directives for "Dirt & Diamonds" (binding for every model)

*Gemini reads this file directly; Claude Code reads it through `CLAUDE.md`, GPT agents through `AGENTS.md`.*

## Mission Statement

You are a member of the "Dirt & Diamonds" model team. **Roles, slice ownership, review pairs and the work flow are defined in [docs/TEAM.md](docs/TEAM.md) (owner-confirmed 2026-10-10) — read it before starting any slice.** In short: Claude Opus 5.5 is integration lead and chief architect (Phase 13 critical path, task briefs, final merge gate); Gemini 3.1 Pro is senior engineer for the presentation runtime (Phases 14/16, slices 13-4/13-5, 17a) and the independent reviewer of Claude Opus's sim diffs; Gemini 3.1 Pro Math owns calibration; Gemini 3.8 Flash owns content at volume. **Only start a slice that TEAM.md §2 assigns to you, from a task brief in `docs/tasks/`, in your own git worktree** (TEAM.md §3). **Stop when your task is done.** End with a handoff note to the human: what was done, the commit, the gates and their results, what's open, and "Recommended next: `<slice>` by `<model>`" from TEAM.md §2. Never start the next slice yourself.

**The game** (canonical: [docs/GAME_VISION.md](docs/GAME_VISION.md)) is a **commercial Steam release**: a hybrid life simulation (Utility AI) and deep baseball simulation (Monte Carlo / Markov chain) built in Godot 4 + C#. The player raises a ballplayer from **High School → College → the Draft → Pro** in a fictional league, survives a gritty off-field life (hustles, crime, family, heirs), hears every game called by **their team's own ElevenLabs-voiced announcer** (voice is broadcasters only), gets story through **phone texts** (including an unknown number that turns out to be the GOAT father who gave them up), lives with temptation and vice that really help and really cost, and sees story moments, achievements and big plays as **in-engine motion-comic cutscenes**. The visual identity is being **completely redesigned** (Phase 15).

**Where we are:** Phases 0–12 are complete. The road to launch is Phases 13–17 plus **Phase L** (lineage, temptation and vice) in [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md). The game, start to finish, is [docs/GAME_VISION.md](docs/GAME_VISION.md) **v2**. GPT 6 Astra Pro runs the independent full-project review, R-1 ([docs/tasks/R-1_full_review_brief.md](docs/tasks/R-1_full_review_brief.md); reassigned from Claude Fable 5.1 on 2026-10-10). Docs are organized by authority in [docs/README.md](docs/README.md) (active specs in `docs/design/`, shipped-system specs in `docs/reference/`, superseded docs in `docs/archive/`). Every session starts by reading [docs/progress.md](docs/progress.md) and running `git status` + `git log` (an auto-committer and parallel sessions are active).

Primary objective: highly optimized, performant code with strict separation between the deterministic sports systems, the stochastic narrative systems, and the presentation layer.

## 1. Architectural Boundaries (STRICT)

- **Life vs Baseball:** `/Assets/Simulation/Life/` and `/Assets/Simulation/Baseball/` MUST NOT reference each other's active game loops. They communicate only through the async event bus and the SQLite database.
- **Presentation never drives simulation.** UI, announcers (`BroadcastBooth`, `VoiceDirector`), cutscenes (`CutsceneDirector`, `CutscenePlayer`) and the phone consume DTOs and bus events and emit player-intent signals. They never write the DB or mutate sim state, and never use the sim's `RngState` (presentation randomness uses its own RNG).
- **Baseball engine:** data-oriented design for the macro sim; must process thousands of entities per frame.
- **Life sim engine:** Utility-based AI for NPC and avatar off-field decisions; need decay is non-linear.

## 2. League Rules (Phase 13, spec: [docs/design/league_restructure.md](docs/design/league_restructure.md))

- Three tiers: `LeagueTier` HS = 0, College = 1, Pro = 2. **No minor leagues.** HS 12 teams, College 24, Pro 32.
- HS games are 7 innings; College and Pro are 9. HS and College play 1 game a week; Pro plays 64 games (~3 a week).
- Pro rosters: ≤ 4 SP (≤ 3 active), ≤ 5 RP (3 active), 15 position players (12 active). Sunday-night roster lock. Absent players are never replaced; a team that can't field 9 position players and a pitcher forfeits.
- Progression: graduation → recruiting → College → draft-eligible after sophomore year → Draft → Pro.
- Roster sizes are **capacities**, never per-tier array sizes: fixed buffers, loops to per-team counts.

## 3. C# Coding Standards & Performance

- **Godot 4 compatibility:** scripts inherit the appropriate Node/Control types; explicit `[Export]` for editor-visible fields; precise `GetNode<T>()`, cached in `_Ready()`.
- **Zero-allocation simulation:** no GC spikes in the baseball sim. Use structs for transient math (e.g. Markov state evaluation), object pooling for logs, UI elements and temporary nodes, and `Span<T>` / `Memory<T>` for heavy array work.
- **Naming:** PascalCase for classes, methods and properties (`AtBatResolver`); camelCase for locals and parameters (`pitcherStamina`); `_camelCase` for private fields (`_currentUtilityScore`).

## 4. Database Rules (SQLite)

- **Single source of truth:** the local `.db` holds player stats, relationships, schedules, playoffs, draft history and legacy flags.
- **No Blind Queries:** before writing complex SQL or any new column reference, validate the schema with the SQLite MCP or the `validate_sqlite_schema` skill. Point schema exploration at a **scratch copy**, never the live save.
- **Performance:** parameterized queries, cached plans, and one `BEGIN TRANSACTION` per batch (e.g. a calendar day).
- **Saves:** schema v14 (Phase 13) is a pre-1.0 clean break. **From 1.0 on, every schema change ships a migration** plus a frozen-save harness check.

## 5. Audio & Voice (Phase 14, spec: [docs/design/voice_and_broadcast_audio.md](docs/design/voice_and_broadcast_audio.md))

- **ElevenLabs is offline-only.** Clips are generated with `Tools/VoiceGen` and shipped as Ogg files with `Assets/Audio/Voice/voice_manifest.json`. The shipped game never calls an AI service.
- **API keys live only in environment variables or the repo-root `.env`** (`ELEVENLABS_API_KEY`, `ANTHROPIC_API_KEY`). `.env` is git-ignored and must never be tracked or staged. The repo is **private** (since 2026-10-10; its earlier history was public), and an auto-committer pushes. Never write a key to the repo, logs, manifests or builds, and never print `.env` values.
- **Broadcasters only (owner, 2026-10-10):** voice is for `BroadcastBooth` and the announcers. No phone voice notes, no cutscene voice-over, no character dialogue.
- **No real people:** no voice cloned from or imitating a real person or broadcaster. No spoken player names, and no avatar pronouns (the avatar can be male or female); names appear in subtitles only.
- Every generation run is logged in `docs/legal/elevenlabs_generation_log.md`. Every clip gets a human listening pass.
- Buses: Master → SFX / Announcer / Music / Ambience (no Voice bus). Every announcer line has a strings-table subtitle.

## 6. Cutscenes (Phase 16, spec: [docs/design/cutscenes_motion_comic.md](docs/design/cutscenes_motion_comic.md))

- In-engine motion comics defined in JSON (`Assets/Cutscenes/Definitions/`), played by `CutscenePlayer`, triggered by `CutsceneDirector` from bus events and `BroadcastBooth.BigPlay`.
- Always skippable; honor reduce-motion; soft-pause the game clock; never write state.

## 7. Visual Design (Phase 15, spec: [docs/design/visual_redesign.md](docs/design/visual_redesign.md))

- The shipped dark-mode thin-slice look is **not** the shipping look; the whole visual identity is being redesigned.
- All styling flows from the Theme family in `Assets/UI/Theme/`, never per-scene overrides. Fonts must be licensed for commercial games. Every interactive control gets focus navigation.

## 8. Third-Party Integrations

- **Steamworks:** use **Facepunch.Steamworks exclusively**. Never implement or reference Steamworks.NET. Native libraries (steam_api64.dll, libsteam_api.so) are targeted in the `.csproj`.
- **Commercial release** (Phase 17, [docs/design/steam_commercial_release.md](docs/design/steam_commercial_release.md)): every achievement maps to an existing bus event or persisted state; AI content (art, voice) is pre-generated and disclosed; team, league and announcer names must be fictional and trademark-screened.
- Steamworks submissions, payments, pricing and legal sign-off are **the user's actions**. Prepare them, never perform them.

## 9. Required Workflow Procedures

- **Verify UI nodes:** before writing C# for any scene (hustle mini-games, the at-bat view, the phone, cutscene chrome), run `godot_scene_mapper` or the Godot MCP to verify node paths in the `.tscn`.
- **Validate event integrity:** after any gritty-event content change, run `check_event_graph_integrity` (flags, dead ends, `contact` and `cutscene` references; any `voice_note` fails, since voice is broadcasters only; planned checks #11 never-abortion and #12 tokens land with Phase L).
- **Test stat matrices:** any change under `Assets/Simulation/` (the resolver, PED/tier weights, rosters, schedules, innings) must be followed by `run_monte_carlo_batch`. The resolver guard must stay byte-exact; re-baselining a season guard needs explicit sanction in a design doc.
- **Test the arcade engine:** any change under `Assets/Simulation/Arcade/` (physics constants, AI noise tables, rules) must be followed by `dotnet run --project Tools/ArcadeHarness -c Release`: per-tier bands, monotonicity, rules integrity and watched-equals-skipped determinism. A change to the shared `EffectiveRatings` stack runs both harnesses.
- **Validate audio:** after any voice/audio asset or manifest change, run `validate_audio_manifest`.
- **Validate cutscenes:** after any cutscene definition change, run `Tools/CutsceneHarness`.
- **Validate Steam natives:** after export or `.csproj` changes, run `validate_steamworks_native`.
- **Log the session:** append a compact entry to `docs/progress.md` before ending.

## 10. Lineage, Temptation & Vice (Phase L, specs: [docs/design/temptation_and_lineage.md](docs/design/temptation_and_lineage.md), [docs/design/substances_and_addiction.md](docs/design/substances_and_addiction.md))

- **Identity never touches the sim.** Gender, ethnicity and listed position (`Player_Identity`) are story-only. No rating, cap or resolver path may read them.
- **Secrecy invariant.** Only `LineageQueries` reads `Biological_Parentage`. No UI, article, event or `RelationshipGraph` hydration may hint at the father or a hidden child before its reveal. Every dating, fling, affair and adoption pool goes through `LineageQueries.IsKin` and silently excludes blood relatives.
- **Content lines.** Temptation and substance **boosts are adults-only**: 18+ and tier ≥ College, enforced by a loader guard. Nothing sexual is explicit at any age. **Never abortion:** every pregnancy reaches a birth (kept or placed), and check #11 enforces it.
- **Calibration discipline.** Every new effect (`Active_Effects`, clutch, missing-piece freezes, affair nudges, substance edges) must be bit-identical when no rows or flags exist, and every magnitude is a Pro Math data value gated by `run_monte_carlo_batch`. `ped_active` is engine-written by the substance engine only.
- **Text.** Phone and press text resolve `{tokens}` at write time through `NarrativeTokenResolver`. Unknown tokens fail at load.

## 11. Gameplay Core (Phase G, high priority; specs: [docs/design/arcade_baseball_overhaul.md](docs/design/arcade_baseball_overhaul.md) for the attended game, [docs/design/gameplay_core.md](docs/design/gameplay_core.md) for attributes and venues)

- **Attended games are true arcade physics (owner, 2026-10-13).** Bat contact, ball flight, fielding and baserunning are decided by a real-time physics engine (`Assets/Simulation/Arcade/`) and the player's input. Ratings shape the physics (arcade §4). **Every background game stays on the calibrated macro sim**, and its neutral policies and resolver guard stay byte-exact.
- **The arcade engine is engine-independent and deterministic.** It uses pure C# with no Godot types, a fixed step, and a seeded `RngState` plus a recorded input stream. A skipped game must equal the watched game byte for byte. Godot only renders snapshots and collects input. UI and the booth never touch its RNG.
- **Arcade calibration:** AI-vs-AI arcade games must land in the Phase 13 per-tier bands (arcade §9). Physics constants and AI noise tables are data edits owned by Pro Math. Difficulty presets change only the human's assists, never the AI or ratings.
- **One modifier stack.** Effective ratings (tier, person, gear, rust, rivalry, `Active_Effects`, PED, fatigue) are built once in `EffectiveRatings` and shared by the macro sim, `MicroGame` and the arcade engine. Never re-derive them inside one engine.
- **Positions:** the sim reads the defensive position only from `Player_Fielding`, never from `Player_Identity`.
- Attribute effects and sizes are defined in gameplay_core §1 and arcade §4. Any change to a resolver weight, a cap, a physics constant or a difficulty target is a Pro Math call gated by the matching harness.
- Every team (all 68) gets its own venue (`Assets/Data/Venues/venues.json`), interactive venue screen, 16-bit field and art. The arcade field reads park dimensions from the venue catalog. No screen may hard-code a team or a park.
