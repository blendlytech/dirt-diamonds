# Dirt & Diamonds — Phased Build Plan

Lead: Gemini 3.1 Pro (orchestration + critical-path code). Delegates: Gemini 3.1 Pro Math (statistical/mathematical design), Gemini 3.8 Flash (high-volume, well-specified implementation). No phase begins until the previous phase's exit criteria pass (except where the Phase Dependency Map allows parallel slices).

**The game:** see [GAME_VISION.md](GAME_VISION.md), the canonical pitch as of 2026-10-09 (it replaces the missing `GAME_IDEA.md`). In short: a commercial Steam game that raises a ballplayer HS → College → Draft → Pro in a fictional league, with a gritty life sim, ElevenLabs-voiced team announcers (voice is broadcasters only, 2026-10-10), in-engine motion-comic cutscenes, and a completely redesigned visual identity. **Phases 0–12 are complete; Phases 13–17 are the road to launch.**

## Model Delegation Doctrine

| Model | Owns | Examples |
| :---- | :---- | :---- |
| **Gemini 3.1 Pro** (lead) | Cross-system integration, performance-critical code, everything that touches architectural boundaries, review of all delegated work | DatabaseManager + transactions, async event bus, zero-allocation Markov/Monte Carlo cores, EventDispatcher, final wiring |
| **Gemini 3.1 Pro Math** | Mathematical & statistical design (design docs, not necessarily code) | PA outcome probability tables calibrated to MLB norms, 25×25 transition matrix math, PED multiplier calibration, non-linear need-decay curves, poker pot-odds AI |
| **Gemini 3.8 Flash** | High-volume implementation from a precise spec; iteration & tuning loops | DTOs/query classes, StatsNormalizer, headless test harnesses for the project skills, gritty-event JSON content, UI scene wiring, populating rules docs, refactoring passes |

**Legacy role names.** Everything before 2026-10-09 (this file's phase bodies, `docs/progress.md`, `docs/progress_archive.md`, every `docs/design/*.md`) names the earlier model roster. Read them as roles, not models:

| Legacy name | Role | Current model |
| :---- | :---- | :---- |
| Fable 5 | Engine, calibrated core, review of every delegated build | Gemini 3.1 Pro |
| Opus 4.8 | Design docs, math/statistical specs | Gemini 3.1 Pro Math |
| Sonnet 5 | Content batches, data edits, UI builds, harnesses | Gemini 3.8 Flash |

Rule of thumb: Gemini 3.1 Pro writes the spec and the skeleton; Gemini 3.8 Flash fills volume; Gemini 3.1 Pro Math owns any file where the math being *wrong* is worse than the code being *slow*. Gemini 3.1 Pro reviews every delegated diff before it merges.

## Phase 0 — Toolchain & Foundation (COMPLETE)

Nothing can compile until this is done. Owner: Fable 5 (system installs need user approval).

1. Install .NET 8 SDK and Godot 4.x **.NET edition** (winget available).
2. `git init` + Godot/C# `.gitignore` (repo is currently not version-controlled).
3. Create `project.godot` + `DirtAndDiamonds.csproj` targeting the existing `/Assets` tree.
4. **Repair `.mcp.json`** — three servers silently fail to connect.
5. Populate the empty `database_rules.md` / `ui_conventions.md` rules files (Sonnet 5).

**Exit criteria:** `dotnet build` succeeds; Godot opens the project; git history started; schema validation path (MCP or CLI) proven working.

## Phase 1 — Database Core (single source of truth) (COMPLETE)

- `SchemaDefinitions.sql`: Players, Batting_Stats, Pitching_Stats, Relationships, Entity_Flags, Game_Logs + indexes on high-traffic tables.
- `DatabaseManager.cs`: connection lifecycle, parameterized queries only, `BEGIN TRANSACTION` batch API, pooled command objects.
- `PlayerQueries.cs` + DTOs (Sonnet 5, from Fable 5's spec).
- **Validate:** `validate_sqlite_schema` skill (build its checking script as part of this phase).

**Exit criteria:** schema round-trips 10k generated players; FK integrity checks pass; batch day-advance transaction benchmarked.

## Phase 2 — Core Loop, Time & Event Bus (COMPLETE)

- `GameManager.cs`, `TimeManager.cs` (calendar advancement in transactions), `GlobalState.cs`.
- The **async event dispatcher bus** — the only legal communication channel between Life and Baseball per CLAUDE.md. Getting this boundary right now prevents architectural rot later. Owner: Fable 5 exclusively.

**Exit criteria:** headless "advance 365 days" run completes with zero cross-references between `/Simulation/Life/` and `/Simulation/Baseball/`.

## Phase 3 — Baseball Macro-Sim (Monte Carlo) → Milestone M1 "The League Lives" (COMPLETE)

- Opus 4.8 designs the PA outcome probability model (batter vs. pitcher vs. fielding → 1B/2B/HR/SO/BB percentages) calibrated to real MLB averages.
- Fable 5 implements `LeagueSimulator.cs` + `AtBatResolver.cs`: structs, `Span<T>`, object pooling, zero GC pressure at thousands of entities/frame.
- Sonnet 5: `StatsNormalizer.cs` + the headless harness behind `run_monte_carlo_batch`.
- **Validate:** `run_monte_carlo_batch` — 10,000 at-bats must produce a realistic league slash line. Re-run after every AtBatResolver or PED-weight change (CLAUDE.md mandate).

**Exit criteria:** full season simulates headless; league-wide AVG/OBP/SLG within MLB norms; GC allocation profile flat.

## Phase 4 — Baseball Micro-Sim (Markov Chain) → Milestone M2 "You're in the Game" (COMPLETE)

- 25 base-out states, 25×25 transition matrix per event (Opus 4.8 math design; Fable 5 struct-based implementation).
- Player input blending (timing/location vs. DB attributes).
- PED modifiers: 1.5× power/stamina during matrix calc; post-game `health_ceiling` deduction + `detection_risk` increment.
- First thin UI slice: playable at-bat scene (verify nodes with `godot_scene_mapper` **before** writing UI logic — CLAUDE.md mandate).

**Exit criteria:** micro-sim outcome distribution converges with macro-sim for identical players; `run_monte_carlo_batch` passes.

## Phase 5 — Life Sim: Needs Engine & Utility AI → Milestone M3 "Life Happens" (COMPLETE)

- `NeedsEngine.cs`: five needs, non-linear decay accelerating near zero (Opus 4.8 curve design).
- `UtilityCalculator.cs`: Utility = Σ(Consideration × Weight); stress/emotion overlay that mutates weights and can force autonomous actions.
- Sonnet 5: harness behind `simulate_utility_decay` + tuning iterations.
- **Validate:** `simulate_utility_decay` — one simulated week must not starve an NPC during a 3-hour game.

**Exit criteria:** NPCs self-manage needs over a simulated month; stress override provably fires.

## Phase 6 — Relationships & Generational Legacy (COMPLETE)

- `RelationshipGraph.cs`: bidirectional affinity graph backed by the Relationships table; rivalry scores feed baseball probability modifiers (via the event bus, never direct reference).
- Heir mechanics: genetic stat blending, hidden `baseball_interest`, succession on retirement, game-over when lineage fails.

**Exit criteria:** simulated 3-generation run with succession handoffs.

## Phase 7 — Gritty Event Framework (COMPLETE)

- `EventDispatcher.cs` background polling of Entity_Flags/Players; `ConditionEvaluator.cs` for prerequisite booleans; JSON event schema (prerequisites, probability weight, payload, branching choices, hidden flag writes).
- Sonnet 5 authors event content at volume from Fable 5's schema.
- **Validate:** `check_event_graph_integrity` after every content batch — no orphaned flags, no dead-end branches, no unreachable events.

**Exit criteria:** cascading chain proven end-to-end (e.g., bribe → `compromised_syndicate` → syndicate event fires seasons later).

## Phases 8 & 9 — Economy, Hustles, & Career Ladder (Interleaved) → Milestones M4 & M5 (COMPLETE)

*Note: Per the `docs/phase_8_9_interleave_plan.md`, Phase 8 and 9 were interleaved to stand up a skeletal career clock before layering on hustles. All of 9a–9d and 8a–8e shipped and were signed off 2026-07-04…06 — see `docs/progress_archive.md`. The 6-tier / 8-team league model below is what shipped; Phase 13 proposes replacing it.*

### 9a — Tier Schema + Multi-Tier Macro-Sim

**Owner:** Fable 5

- Schema v7: add a `tier` dimension to `Teams` (HS, College, MinorA, MinorAA, MinorAAA, MLB).
- Query-layer tier filters for `BaseballQueries`.
- `LeagueGenerator` seeds one independent 8-team league per tier.
- Opus 4.8 designs offensive-baseline deltas; Fable 5 implements `tier → LeagueSimulator` map.
- `CareerManager` tier rewiring for correct scheduling.
- `StatsNormalizer` scoping per-tier.
- New-game avatar creation defaults to an HS team.
- **Validate:** `run_monte_carlo_batch` gains a per-tier band check and a regression guard for the existing MLB band.

### 9b — Bare Daily-Clock Skeleton

**Owner: Fable 5 engine, Sonnet 5 UI.**

- Split the avatar out of `LifeSimManager`'s autopilot tick — avatar's hourly actions become player-chosen.
- Schedule surface: allocate hours across Sleep / School / Practice / Game / Work blocks. Game reuses `CareerManager` flow. Practice/School are inert placeholders for now.
- New UI scene: `Assets/UI/ScheduleScreen.tscn`.
- **Validate:** Extend `NeedsDecayHarness` / new harness proving scripted manual block choices behave as expected and untouched autopilot reproduces pre-9b traces.

### 8a — Survival Economy + Legal Work

**Owner:** Sonnet 5

- Recurring rent/food/gear drain on a calendar cadence, through `PlayerQueries.AdjustFunds`.
- Legal Work becomes the first real Work-block payout (modest funds, energy/fitness drain).
- **Validate:** `simulate_utility_decay` extended with funds-solvency check over a simulated month.

### 8b — Narcotics (3-tier state machine) + Fencing Negotiation

**Owner:** Opus 4.8 designs, Sonnet 5 implements, Fable 5 reviews.

- Isolated Hustle scene nodes. Narcotics: Inventory Drop → Profit/Toxicity Cut → Territory Control. Fencing negotiation.

### 8c — Arrest / Injury / Suspension Risk Triad

**Owner:** Fable 5 (engine), Sonnet 5 (event content).

- Roster/availability mutation type for Gritty Events.
- Arrest (jail time-skip), Injury (availability/rating hit), Suspension (`detection_risk` benching).

### 8d — Texas Hold'em, 8e — Equipment Quality

- **8d owner:** Opus 4.8 (pot-odds math), Sonnet 5 (implementation).
- **8e owner:** Fable 5 (gear tiers as effective-ratings modifiers).

### 9c — Promotion / Advancement Gates, 9d — Player Development / Decline Curves

- **9c owner:** Opus 4.8 (promotion model), Fable 5 (tier-transfer handoff).
- **9d owner:** Opus 4.8 (growth/decline curves), Sonnet 5 (tuning harness). Practice block finally produces a real stat effect.

**Exit criteria (Phases 8 & 9):** a generated player climbs from HS to the MLB across simulated seasons via performance + development; each tier's league line sits in its own calibrated band; the avatar's daily schedule measurably moves both stats and needs; full career loop playable (broke rookie → hustle income → stress consequences → gritty events → season play).

## Phase 10 — Presentation Layer & Narrative Delivery → Milestone M6 "The Look" (COMPLETE)

*10a–10e shipped 2026-07-06 (shell + theme, burner-phone messages, scouting dashboard, bank strip, portrait pipeline).* **2026-10-09:** the *visual* identity below is superseded by Phase 15 (complete redesign). The architecture (two worlds, messages as narrative delivery, read-only UI) carries forward.

Every UI shipped to date is a deliberate thin slice of plain Godot controls. GAME_IDEA's entire hook is 100% UI/text: a dark-mode split between a **"Baseball Dashboard"** and a **"Burner Phone / Bank."** This phase makes the game *look* like the pitch — it is the visual/asset phase the plan otherwise never schedules.

- **Two-panel shell**: the Baseball Dashboard (stats, scouting reports, calendar, progress bars) and the Burner Phone / Bank (contacts, illicit deals, bank balance, basic survival). Both stay read-only over sim state per `ui_conventions.md` — DTOs in, player-intent signals out, no scene reaching into another's tree. Owner: **Sonnet 5**.
- **Narrative delivery as messages**: fired Gritty Events and relationship beats surface as **iMessage-style texts** from girlfriends, coaches, and shady contacts, threaded per-contact in the Burner Phone — the event-choice UI's dressed-up form, consuming the existing `GrittyEventFiredEvent` / choice seam. **Sonnet 5**, over the already-pinned Fired/Resolved contract.
- **Scouting reports**: the Baseball Dashboard's read of the player's ratings and tier standing framed as a scout would (letter grades, projection), sourced from `Player_Ratings` + the Phase 9 tier/development state.
- **Visual identity**: dark-mode theme, satisfying progress bars, calendar interface — a shared Godot theme resource, not per-scene styling, retrofitting the existing thin-slice scenes.
- **Portrait pipeline**: pre-generated 2D contact portraits (Midjourney, gritty-polaroid / mugshot house style per GAME_IDEA) as an asset set keyed to `player_id`; the Steam-required **disclosure** of these pre-generated portraits carries into Phase 11's store copy.

**Exit criteria:** the full loop is playable through the two-panel shell in the shipping visual identity; narrative events arrive as threaded messages; scenes still communicate only via the event bus / signals.

## Phase 11 — Steam & Publishing → Milestone M7 "Ship It" (COMPLETE)

*11a–11e shipped 2026-07-06 (native lifecycle, achievements, cloud saves, rich presence, export + compliance).* **2026-10-09:** the commercial launch (store page, disclosures, RC QA, achievement refresh) is Phase 17.

- Facepunch.Steamworks **only** (Steamworks.NET forbidden): cloud saves for the SQLite DB, achievements hooked to the EventDispatcher, rich presence.
- `NativeLibrary.SetDllImportResolver` for steam_api64.dll / libsteam_api.so; `.csproj` native-library copy targets.
- **Steam content compliance (GAME_IDEA.md):** disclose the pre-generated AI portraits in the store submission; complete the mature-content questionnaire (drug dealing, gambling references) for the appropriate rating.
- **Validate:** `validate_steamworks_native`; Windows + Linux export builds.

## Phase 12 — "Saleable" (COMPLETE)

*12a–12d shipped 2026-07-07: render integrity + layout honesty, surface-the-sim cards (stat line, standings, leaders), and the at-bat presentation layer. Details in `docs/progress_archive.md`.*

## Post-Phase-12 Arcs (COMPLETE, 2026-07-08 … 07-15)

Not separately phased here — tracked slice-by-slice in `docs/progress.md`: the High School arc HS-0…HS-6 (schema v11→v13), Act 1 onboarding + tutorial overlay, UI slices A/B/D-1/G (real-time clock), the event-choice prominence pass, the criminal-underworld content arc DIRT-1…DIRT-4, and the hustle depth pass (robberies, narcotics re-up/haggle, fencing multi-lot, hustle feel pass). Remaining exits on these are **user playtests**; the deprioritized polish is now absorbed: D-2/D-3 audio into Phase 14, Slice E phone theming and Slice F animation into Phase 15.

## Phase 13 — League Restructure → Milestone M8 "The Pipeline" (ACTIVE: design complete, build next)

**Spec:** [design/league_restructure.md](design/league_restructure.md), which resolves the user's source spec (bottom of this file). It replaces the Phase 9 league model (6 tiers including Single-A, Double-A and Triple-A; 8 teams per tier; fixed 9+5+3 rosters; tier-ladder promotion).

- **Three tiers, 68 teams:** HS 12 (2 conferences × 6), College 24 (2 × 3 divisions × 4), Pro 32 (2 × 4 divisions × 4). `LeagueTier` = HS 0 / College 1 / Pro 2, so shipped `tier >= 2` content gates keep meaning "pro ball".
- **Seasons:** HS 10 games (Fridays), College 14 (Saturdays), Pro 64 (Tue/Thu/Sat); HS games are 7 innings. Persisted `Schedule_Games` replace the round-robin and the 26-game HS calendar.
- **Rosters:** per-tier limits; Pro has 4 SP / 5 RP / 15 position players with active/inactive designations, the **Sunday roster lock**, and the **attrition & forfeit rule**. Roster constants become capacities (zero-alloc preserved). The replacement call-up is retired.
- **Ride the ace:** starter selection with per-tier rest days produces the spec's 2-man Pro rotation naturally.
- **Playoffs:** HS bracket of 6 with byes; College 18-team tournament (National Ranking + play-in, conference tournaments, a single-game final); Pro 12-team Bo3 rounds and a Bo5 Championship.
- **Pipeline:** HS graduation → recruiting / Signing Day → College class years → declare after sophomore year → July Draft (4 rounds, reverse standings) → Pro. `PipelineManager` replaces `PromotionManager`.
- **Calibration:** tier deltas HS −20 / College −16 / Pro 0; the resolver guard stays byte-exact; the 154-game MLB season guard is retired and a new Pro guard is recorded once (sanctioned).
- **Saves:** schema v14 is a clean break (old saves are refused and backed up).

**Slices:** 13-0 prerequisites (sqlite MCP, MC flake) → 13-1 schema + world-gen → 13-2 roster capacities + innings + starter selection → 13-3 schedules + calendars + re-band → 13-4 playoffs + ranking → 13-5 roster lock + forfeits → 13-6 pipeline + draft → 13-7 ripple (achievements, presence, docs, cards). Owners and gates are in spec §15.

**Exit criteria:** a generated player goes HS → College → drafted → Pro across simulated seasons; all three leagues schedule, play and crown champions; per-tier bands green; the new Pro guard recorded; PipelineHarness population conservation within ±5% over 20 seasons.

## Phase 14 — Voice & Broadcast Audio → Milestone M9 "The Call"

**Spec:** [design/voice_and_broadcast_audio.md](design/voice_and_broadcast_audio.md).

- **ElevenLabs, offline only:** `Tools/VoiceGen` renders script sheets into mastered Ogg clips plus a manifest; the API key comes from the environment and never enters the repo or a build; a licensing log records every run; a human listens to every clip.
- **Play-by-play announcers:** 56 unique booths (24 College + 32 Pro), each with a fictional persona, archetype and **signature taglines** (home run, win, strikeout, grand slam, walk-off); ~120 clips per Pro booth, ~62 per College booth; **3 shared HS local-radio voices** with school-name stingers. Your team's booth calls your games as radio homers; other booths appear in Around the League highlights and on Draft Night. Sound bites only, and no spoken player names.
- ~~Phone voice notes~~ **cut 2026-10-10:** voice is broadcasters only. Phone messages and cutscene dialogue stay text.
- **Runtime:** Voice / Announcer / Music / Ambience buses with sidechain ducking; a `VoiceDirector` autoload (presentation RNG only, so it's calibration-inert); a `BroadcastBooth` on the at-bat beat sequencer; a subtitle strip; volume sliders and toggles.

**Slices:** 14a buses + VoiceDirector → 14b VoiceGen + manifest + AudioManifestHarness → 14c personas + scripts → 14d BroadcastBooth + highlights → ~~14e voice notes~~ (cut) → 14f full generation + listening QA.

**Exit criteria:** every College/Pro team's announcer calls a live game with its taglines; HS games use the radio pool; `validate_audio_manifest` passes at 100% coverage; MC and Gritty harnesses untouched.

## Phase 15 — Visual Redesign → Milestone M10 "The Look, Reborn"

**Spec:** [design/visual_redesign.md](design/visual_redesign.md). User directive (2026-10-09): **the game may use a completely new visual design.** Phase 10's dark-mode thin-slice look is not the shipping look.

- **Recommended direction, "Two Worlds, One Life":** the DIAMOND side looks like a prime-time sports broadcast package; the DIRT side looks like night streets and the burner phone. The phone is the portal between them, team colors tint the UI systemically, and production value climbs with the tier. The user picks the final direction at the 15a gate.
- **Design system:** one Theme family, tokens and components; commercially licensed fonts; accessibility built in (colorblind-safe palette, text size, reduce motion, subtitles).
- **Every screen rebuilt**, including the **new front end** a Steam product needs (title screen, New Career, loading, pause, credits) and the Phase 13 league screens (bracket, draft board, recruiting, roster status).
- **Art:** 68 team logos, park kits, scenes, portraits and icons. AI-assisted art is pre-generated, curated and disclosed. This phase **absorbs** the deprioritized Slice E (phone tiers) and Slice F (motion).
- 1080p reference, 16:10-safe, focus navigation everywhere (keeps Steam Deck and controller support reachable).

**Slices:** 15a art direction (user picks) → 15b design system → 15c screen rebuilds → 15d art production → 15e motion → 15f phone tiers. 15a–15b can run alongside Phase 13.

**Exit criteria:** every screen in the inventory uses the approved direction; no placeholder art; reduce-motion and text-size honored everywhere; inert sim (UI never writes state).

## Phase 16 — Cutscenes (In-Engine Motion Comics) → Milestone M11 "The Moments"

**Spec:** [design/cutscenes_motion_comic.md](design/cutscenes_motion_comic.md). User decision: in-engine motion comic, not video.

- A `CutscenePlayer` overlay plus JSON definitions (layered art, camera moves, captions, voice, SFX, music) with **live slots** (your portrait, name, team, stat line).
- **Four kinds:** story (~20), milestone (~10: Signing Day, Draft Night, first Pro game, championships…), **achievement** cards (one per achievement), and **big plays** (8 kits × 3 tier styles; the announcer's call is the audio bed).
- `CutsceneDirector` triggers come from the bus (new additive events `AchievementUnlocked`, `DraftCompleted`, `RecruitingCommitted`, `ChampionshipWon`) and from `BroadcastBooth.BigPlay`. Queue and rate limits; the game clock soft-pauses; everything is skippable; reduce-motion supported.

**Slices:** 16a player → 16b director + events → 16c big plays → 16d achievement cards → 16e story/milestone content. Depends on 14a/14b (booth calls as audio beds) and 15a/15b (art direction).

**Exit criteria:** every launch cutscene plays from its real trigger; `CutsceneHarness` green (refs, durations, reachability); MC and Gritty harnesses inert.

## Phase 17 — Steam Commercial Release → Milestone M12 "On Sale"

**Spec:** [design/steam_commercial_release.md](design/steam_commercial_release.md). It builds on the Phase 11 technical integration.

- **Achievements and rich presence** refreshed for the new league (Signed, Draft Day, First-Rounder, The Show, State/National Champs, Ring Bearer, Walk-Off, Grand Slam, Benched, Short-Handed, Going Straight, Banned for Life, plus the shipped set; "Went Pro" retired).
- **Legal and disclosure package:** the Steam AI-content survey (pre-generated art and ElevenLabs voices; no live generation), the mature-content survey (drugs, simulated gambling, crime, alcohol, mature relationships, language), the ElevenLabs and art licensing logs, `THIRD_PARTY_NOTICES.txt`, and a **trademark screen** of the title, league and team names.
- **Store page:** capsules, screenshots, trailer, copy, system requirements.
- **Release-candidate QA:** the full gate suite, a full-career playthrough, Windows + Linux + Deck smoke tests, a cloud-save round-trip, install < 2 GB, a 10-season soak.
- **Save policy:** from 1.0, every schema change ships a migration and a frozen-save harness check.
- **Launch ops:** Coming Soon page early, Steamworks reviews, pricing, branches, patch cadence. The user owns every external submission and payment.

**Slices:** 17a achievements + presence → 17b legal + trademark → 17c store assets → 17d RC QA → 17e submission + launch.

**Exit criteria:** store and build reviews passed; RC checklist complete; the game is live on Steam.

## Phase Dependency Map

```text
13 League ──────────────┬──────────────► 17 Steam Release
14 Voice ── 14a/14b ──┐ │                     ▲
15 Visual ─ 15a/15b ──┼─┴─► 16 Cutscenes ─────┘
                      └──────────────────────► (14c–14f, 15c–15f run in parallel)
```

Phase 13 is the critical path (engine + calibration). Phases 14 and 15 are presentation-only and can start their early slices alongside it. Phase 16 needs both the voice pipeline and the approved art direction. Phase 17 needs everything.

## Cross-Cutting Discipline (every session)

- Append completed work + next steps to `docs/progress.md` before ending a session.
- Commit per completed sub-task; phases merge only with exit criteria green.
- UI is built as thin vertical slices inside each phase, not as a big-bang Phase — every milestone stays demoable.
- Schema changes go through the SQLite validation path first (No Blind Queries rule).
- Voice/audio assets go through `validate_audio_manifest`; cutscene definitions through `CutsceneHarness`; event content referencing either through `check_event_graph_integrity`.
- Presentation (announcers, voice, cutscenes, the visual redesign) never writes sim state; any slice touching `Assets/Simulation/` still triggers `run_monte_carlo_batch`.
- No secrets in the repo: ElevenLabs and other API keys live only in environment variables.
- External submissions, payments and legal sign-offs (Steamworks, trademark, pricing) are the user's actions.

## Phase 13 Source Spec — Multi-Tiered League Specifications (user-authored, 2026-10-09)

*Kept verbatim as the source of truth for intent. Every ambiguity is resolved, and flagged, in [design/league_restructure.md](design/league_restructure.md) §16.*

**System Prompt / Instructions for League Simulation Engine:**

You are tasked with implementing the core architecture for a custom, multi-tiered baseball universe encompassing High School, College, and Professional play. This world operates under highly restrictive rules, condensed schedules, and a strict progression pipeline without minor leagues.

Please ensure the simulation engine strictly adheres to the following constraints:

### 1. The Global Pipeline & Draft

- **No Minor Leagues:** Player progression is strictly linear: High School -> College -> Pro.
- **Draft Eligibility:** College players are eligible to declare for the Professional Draft after completing their Sophomore year.

### 2. High School Level (12 Teams)

- **Game Length:** 7 innings.
- **Schedule:** 1 game per week.
- **Structure:** 2 Conferences, with 6 teams in each.
- **Rosters:** Wide open. No strict limits on roster construction.
- **Playoffs:** The top 3 teams from each conference advance to a single-elimination (1-game) playoff bracket. The 1st seed in each conference receives a first-round bye.

### 3. College Level (24 Teams)

- **Game Length:** 9 innings.
- **Schedule:** 1 game per week.
- **Structure:** 2 Conferences, comprising a total of 6 Divisions (4 teams per division).
- **Rosters:** Semi-restricted. Features 2 extra open starting spots compared to the strict Professional limits.
- **Playoffs (18-Team Tournament):**
  - The top 17 teams make the playoffs based on an overarching National Ranking across both conferences.
  - A single-elimination play-in game determines the 18th and final playoff spot.
  - The playoffs consist of Conference Tournaments, culminating in a single-game National Championship between the winner of each conference.

### 4. Professional Level (32 Teams)

- **Game Length:** 9 innings.
- **Schedule:** 64-game regular season. Teams average 3 games per week.
- **Structure:** 2 Conferences, comprising a total of 8 Divisions (4 teams per division).
- **Playoffs:** Series-based format. Early playoff rounds are Best-of-3 game series, culminating in a Best-of-5 Championship Series.

### 5. Professional Roster Constraints & Mechanics (STRICT)

- **Starting Pitchers:** Maximum of 4 total per organization (Up to 3 Active, 1 Inactive).
- **Bullpen:** Maximum of 5 total per organization (3 Active, 2 Inactive).
- **Position Players:** Exactly 12 Active (9 Starters, 3 Bench) and 3 Inactive.
- **Pitching Philosophy:** Teams heavily rely on their aces, often employing a 2-man rotation and starting their #1 pitcher in every game possible.
- **The Weekly "Sunday" Roster Lock:** Active and Inactive roster designations must be submitted on Sunday night and are permanently locked for the week.
- **The Attrition & Forfeit Rule:** If a player is injured during the week, they *cannot* be replaced by an Inactive player. If injuries deplete an Active roster to the point where a team cannot field 9 defensive players and a valid pitcher, that team immediately forfeits the game.
