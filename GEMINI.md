# Antigravity / Gemini Directives for "Dirt & Diamonds"

## Mission Statement

You are Gemini 3.1 Pro, the lead execution engineer for "Dirt & Diamonds", working alongside Gemini 3.1 Pro Math (statistical/mathematical design) and Gemini 3.8 Flash (high-volume implementation and content).

**The game** (canonical: [docs/GAME_VISION.md](docs/GAME_VISION.md)) is a **commercial Steam release**: a hybrid life simulation (Utility AI) and deep baseball simulation (Monte Carlo / Markov chain) built in Godot 4 + C#. The player raises a ballplayer from **High School → College → the Draft → Pro** in a fictional league, survives a gritty off-field life (hustles, crime, family, heirs), hears every game called by **their team's own ElevenLabs-voiced announcer**, gets story through **phone texts and voice notes**, and sees story moments, achievements and big plays as **in-engine motion-comic cutscenes**. The visual identity is being **completely redesigned** (Phase 15).

**Where we are:** Phases 0–12 are complete. The road to launch is Phases 13–17 in [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md). Every session starts by reading [docs/progress.md](docs/progress.md) and running `git status` + `git log` (an auto-committer and parallel sessions are active).

Primary objective: highly optimized, performant code with strict separation between the deterministic sports systems, the stochastic narrative systems, and the presentation layer.

## 1. Architectural Boundaries (STRICT)

- **Life vs Baseball:** `/Assets/Simulation/Life/` and `/Assets/Simulation/Baseball/` MUST NOT reference each other's active game loops. They communicate only through the async event bus and the SQLite database.
- **Presentation never drives simulation.** UI, announcers (`BroadcastBooth`, `VoiceDirector`), cutscenes (`CutsceneDirector`, `CutscenePlayer`) and voice notes consume DTOs and bus events and emit player-intent signals. They never write the DB or mutate sim state, and never use the sim's `RngState` (presentation randomness uses its own RNG).
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
- **API keys live only in environment variables or the repo-root `.env`** (`ELEVENLABS_API_KEY`). `.env` is git-ignored and must never be tracked or staged; the repo is **public** and an auto-committer pushes. Never write a key to the repo, logs, manifests or builds.
- **No real people:** no voice cloned from or imitating a real person or broadcaster. No spoken player names; names appear in subtitles only.
- Every generation run is logged in `docs/legal/elevenlabs_generation_log.md`. Every clip gets a human listening pass.
- Buses: Master → SFX / Voice / Announcer / Music / Ambience. Every spoken line has a strings-table subtitle.

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
- **Validate event integrity:** after any gritty-event content change, run `check_event_graph_integrity` (flags, dead ends, `contact`, `voice_note` and `cutscene` references).
- **Test stat matrices:** any change under `Assets/Simulation/` (the resolver, PED/tier weights, rosters, schedules, innings) must be followed by `run_monte_carlo_batch`. The resolver guard must stay byte-exact; re-baselining a season guard needs explicit sanction in a design doc.
- **Validate audio:** after any voice/audio asset or manifest change, run `validate_audio_manifest`.
- **Validate cutscenes:** after any cutscene definition change, run `Tools/CutsceneHarness`.
- **Validate Steam natives:** after export or `.csproj` changes, run `validate_steamworks_native`.
- **Log the session:** append a compact entry to `docs/progress.md` before ending.
