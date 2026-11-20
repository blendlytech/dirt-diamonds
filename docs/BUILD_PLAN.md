# Dirt & Diamonds — Phased Build Plan

**What we're building:** [GAME_VISION.md](GAME_VISION.md) (v3.0, canonical). **Who does what:** [TEAM.md](TEAM.md) (owner-confirmed; GPT 6.1 Sol is integration lead). **Where we are:** [progress.md](progress.md). **Docs map:** [README.md](README.md).

**Roles (owner, 2026-10-25): GPT-primary staffing; limited Gemini Argon review reserve.** [TEAM.md](TEAM.md) section 1a remaps every outstanding assignment: Opus to **GPT 6.1 Sol**, Sonnet/Flash to **GPT 6 Sol**, calibration/math and existing Astra work to **GPT 6 Astra**. Gemini Argon is optional for bounded architecture/schema/secrecy/math review packets, not routine implementation or a required gate. Separate author/reviewer sessions remain required. No phase merges before its gates pass; scope-realignment pauses and owner gates are unchanged.

> **Historical author names.** Shipped specs in `reference/` and `archive/` name an earlier model roster as their authors ("Fable 5", "Opus 4.8", "Sonnet 5"). Those are past authors, not current roles, and they're unrelated to **Claude Fable 5.1**, which joined in 2026-10 and authored the G-2 pitching design (the R-1 review is now GPT 6 Astra Pro's: [tasks/R-1_full_review_brief.md](tasks/R-1_full_review_brief.md)). Current roles live only in [TEAM.md](TEAM.md).

## Completed: Phases 0–12 and the post-12 arcs

Full history: [archive/progress_archive.md](archive/progress_archive.md). Each system's contract is in its [reference/](reference/) spec.

| Phase | Delivered | Reference spec(s) |
| :-- | :-- | :-- |
| 0 Toolchain | .NET 8, Godot 4 .NET, git, project files, MCP repair | — |
| 1 Database core | `SchemaDefinitions.sql` (now v13), `DatabaseManager`, typed query classes | rules: `.agents/rules/database_*.md` |
| 2 Core loop + event bus | `GameManager`, calendar, the async bus (the only Life↔Baseball channel) | — |
| 3 Macro-sim (M1) | `LeagueSimulator`, `AtBatResolver`, calibrated to MLB norms | [baseball_pa_outcome_model.md](reference/baseball_pa_outcome_model.md) |
| 4 Micro-sim (M2) | 25-state Markov at-bat sim, input blending, PED hooks (no trigger yet) | [baseball_markov_micro_sim.md](reference/baseball_markov_micro_sim.md), [at_bat_read_input_model.md](reference/at_bat_read_input_model.md) |
| 5 Life sim (M3) | Needs engine, utility AI, stress overlay | [life_sim_needs_decay.md](reference/life_sim_needs_decay.md) |
| 6 Relationships + legacy | Affinity graph, rivalries, heir genetics, succession | [heir_mechanics.md](reference/heir_mechanics.md) |
| 7 Gritty events | Dispatcher, conditions, JSON events, flag cascades, marriage + conception | [gritty_event_framework.md](reference/gritty_event_framework.md), [marriage_and_conception.md](reference/marriage_and_conception.md) |
| 8–9 Economy, hustles, career ladder (M4/M5) | Survival economy, Narcotics, Fencing, Hold'em, equipment, the availability triad, development curves, and the **6-tier league** (superseded by Phase 13) | [hustles_narcotics_fencing.md](reference/hustles_narcotics_fencing.md), [hustles_texas_holdem.md](reference/hustles_texas_holdem.md), [equipment_quality.md](reference/equipment_quality.md), [roster_availability_triad.md](reference/roster_availability_triad.md), [development_decline_curves.md](reference/development_decline_curves.md) |
| 10 Presentation (M6) | Two-world shell, phone messages, scouting, bank, portraits (visuals superseded by Phase 15) | [presentation_layer_narrative.md](reference/presentation_layer_narrative.md) |
| 11 Steam (M7) | Facepunch.Steamworks, achievements, cloud saves, presence, exports | [steam_publishing_ship_it.md](reference/steam_publishing_ship_it.md) |
| 12 "Saleable" | Render integrity, surface-the-sim cards, at-bat presentation | [surface_the_sim.md](reference/surface_the_sim.md), [at_bat_presentation.md](reference/at_bat_presentation.md) |
| Post-12 arcs | HS person layer HS-0…HS-6 (schema v11→v13), onboarding + tutorial overlay, UI slices A/B/D-1/G, event-choice prominence, the DIRT-1…4 criminal arc, the hustle depth pass (incl. robbery), the household board | [high_school_person_layer.md](reference/high_school_person_layer.md), [hs_onboarding_events.md](reference/hs_onboarding_events.md), [onboarding_tutorial_overlay.md](reference/onboarding_tutorial_overlay.md), [real_time_clock_slice_g.md](reference/real_time_clock_slice_g.md), [criminal_underworld_content_arc.md](reference/criminal_underworld_content_arc.md), [hustle_minigames_depth_pass.md](reference/hustle_minigames_depth_pass.md), [household_board.md](reference/household_board.md) |

## Phase 13 — League Restructure → Milestone M8 "The Pipeline" (ACTIVE: 13-0 done 2026-10-10; 13-1…13-3 build on `p13/integration`, spec §15)

**Spec:** [design/league_restructure.md](design/league_restructure.md), which resolves the user's source spec (bottom of this file). It replaces the Phase 9 league model (6 tiers including Single-A, Double-A and Triple-A; 8 teams per tier; fixed 9+5+3 rosters; tier-ladder promotion).

- **Three tiers, 68 teams:** HS 12 (2 conferences × 6), College 24 (2 × 3 divisions × 4), Pro 32 (2 × 4 divisions × 4). `LeagueTier` = HS 0 / College 1 / Pro 2, so shipped `tier >= 2` content gates keep meaning "pro ball".
- **Seasons:** HS 10 games (Fridays), College 28 (Wednesday + Saturday, 14 weeks; owner 2026-10-18), Pro 64 (Tue/Thu/Sat); HS games are 7 innings. Persisted `Schedule_Games` replace the round-robin and the 26-game HS calendar.
- **Rosters:** per-tier limits; Pro has 4 SP / 5 RP / 15 position players with active/inactive designations, the **Sunday roster lock**, and the **attrition & forfeit rule**. Roster constants become capacities (zero-alloc preserved). The replacement call-up is retired.
- **Ride the ace:** starter selection with per-tier rest days produces the spec's 2-man Pro rotation naturally. College keeps its 4-man rotation by rest days; its gaps are now 3–4 days, and the owner set College rest to **3** (2026-10-18), so the College ace starts every game (28). Starter-quality ordering and workload values are Math values (CS-M).
- **Playoffs:** HS bracket of 6 with byes; College 18-team tournament (National Ranking + play-in, single-elimination conference tournaments, then **The Red Dirt Series**: a best-of-3 between the two conference champions on consecutive days, Thu–Fri–Sat, game 3 only if needed; name pending the owner's trademark screen); Pro 12-team Bo3 rounds and a Bo5 Championship.
- **Pipeline:** HS graduation → recruiting / Signing Day → College class years → declare after sophomore year → July Draft (4 rounds, reverse standings) → Pro. `PipelineManager` replaces `PromotionManager`.
- **Calibration:** tier deltas HS −20 / College −16 / Pro 0; the resolver guard stays byte-exact; the 154-game MLB season guard is retired and a new Pro guard is recorded once (sanctioned).
- **Saves:** schema v14 is a clean break (old saves are refused and backed up).

**Slices:** 13-0 prerequisites (sqlite MCP, MC flake) → *(CS-0 College format docs, 2026-10-18; CS-M College Math plan before 13-2/13-3/13-4 build College)* → 13-1 schema + world-gen → 13-2 roster capacities + innings + starter selection → 13-3 schedules + calendars + re-band → 13-4 playoffs + ranking → 13-5 roster lock + forfeits → 13-6 pipeline + draft → 13-7 ripple (achievements, presence, docs, cards). Owners and gates are in spec §15.

- **Lineage hooks (Phase L depends on these):** `PipelineManager` publishes `ProContractSignedEvent` on **every** first Pro contract (drafted *or* signed from free agency); the playoffs publish `ChampionshipWonEvent` (also used by Phase 16b), which **carries the tier**: only a **Pro** World Championship can fire the reveal; a College title (the Red Dirt Series) or an HS State title feeds achievements and cutscenes only. See [design/temptation_and_lineage.md](design/temptation_and_lineage.md) §4.2/§4.4.
- **Downstream content (logged by CS-0):** the six College division booths need a small Red Dirt Series beat set (game 1, series lead, elimination game, clinch, champion): a 14c content item for GPT 6 Sol, unsized. The 13-4 bracket UI (GPT 6 Sol) shows College series status (game 1 / 2 / 3, clinch, elimination). Spec: [design/league_restructure.md](design/league_restructure.md) §8.2.

**Exit criteria:** a generated player goes HS → College → drafted → Pro across simulated seasons; all three leagues schedule, play and crown champions; per-tier bands green; the new Pro guard recorded; PipelineHarness population conservation within ±5% over 20 seasons.

## Phase G — Gameplay Core — Milestone MG "Play Ball" (scope realigned 2026-10-25)

**Governing spec:** [design/scope_realignment.md](design/scope_realignment.md) §2.1/§3.1. Older [arcade_baseball_overhaul.md](archive/arcade_baseball_overhaul.md) full-game plans are superseded; [gameplay_core.md](reference/attribute_contracts.md) §1 retains the attribute contracts.

- **Authoritative attended game:** the Markov `MicroGame`. The pure-C# `ClutchResolver` uses the existing arcade timing core for optional, offense-only avatar moments and returns contact quality, feeding a capped, centered tilt into `MicroGame`. Unscored batting practice writes nothing. Real-time fielding, baserunning and full 9-inning arcade physics are cancelled.
- **Shared ratings and neutral paths:** `EffectiveRatings` is the single modifier stack for macro, micro and clutch inputs. Skipped clutch moments, autopilot and background games apply no tilt and remain byte-identical to the calibrated sim. Retain `Player_Fielding` in schema v14.
- **Venues:** G-7/G-8 production beyond the High School slice is paused. Only the HS venue assets required by the loop and Gate 3 proceed; mass production for 68 venues waits for the golden slice. Park factors remain off for 1.0.

**Slice disposition:**

| Existing slice | Realigned scope / owner |
| :-- | :-- |
| G-0 / G-1 | Retain the prototype timing core and completed attribute audit; reuse batter-side mapping for clutch. |
| **G-2 / G-3 / G-6** | **Cancelled:** full arcade physics/rules, arcade AI calibration, team takeover and real-time baserunning. |
| **G-4 → SR-3a** | Shrink to `ClutchRequest` from `EffectiveRatings`, `ClutchResolver`, `BatterIntentKind.Clutch` and Math constants; **GPT 6 Astra**, GPT 6.1 Sol reviews. |
| **G-5 + G-9 → SR-3b** | Merge into clutch overlay, batting practice and clutch-only assists; **GPT 6 Sol**, GPT 6 Astra reviews. No general difficulty framework. |
| **G-7 / G-8** | **Paused beyond HS:** venue catalog/screens and art; GPT 6 Sol / owner + art tools, with GPT 6.1 Sol review. |

**Exit criteria:** `MicroGame` remains authoritative; the optional clutch tilt is capped and centered; `Tools/ArcadeHarness` validates the timing core and Monte Carlo validates integration with the neutral resolver guard byte-exact. Shared rating changes run both. Owner playtests the clutch feel; no AI-vs-AI full arcade bands are required.

Pitcher intent remains the open D-4 decision in scope_realignment §9; the archived pitching-mode design is not automatically reactivated. Models stop at their assigned slice and hand off per [TEAM.md](TEAM.md) §3.

## Phase L — Lineage, Temptation & Vice → Milestone ML "Blood"

**Specs:** [design/temptation_and_lineage.md](design/temptation_and_lineage.md) (decisions D1–D21) and [design/substances_and_addiction.md](design/substances_and_addiction.md). Opening slice brief: [tasks/16-1_opening_sequence_brief.md](tasks/16-1_opening_sequence_brief.md).

- **Identity + the secret:** `Player_Identity` (gender, ethnicity, listed position; never read by the sim); the legends roster with per-career position assignment; `Biological_Parentage` (one table for every secret); a guaranteed poverty start.
- **The father's arc:** the `unknown_fan` thread, `NarrativeTokenResolver`, the first text on `ProContractSignedEvent`, the record-chase ladder, and the reveal on `ChampionshipWonEvent` or career end (before succession).
- **Family:** either-gender civilian partners with a silent kin guard; planned and unplanned pregnancy (never abortion); placement for adoption and adopting in; maternity absence; the missing-piece freeze; hidden children entering the league at HS intake; opponent and teammate reveals; the paid search; reclaiming.
- **Temptation:** affairs (confidence override, clutch, nudges) with escalating random risk, on the shared `Active_Effects` layer.
- **Substances:** five substances on one addiction engine. The `ped_active` writer finally brings the shipped steroid multiplier to life. Fatal overdose and `AvatarDied`.
- **The Trailblazer:** a once-per-save route for the first woman to play pro, plus press clippings (a new shared feature).

**Slices:**

- **Lineage, temptation and family:** **L-0** docs + no-abortion fix (**DONE 2026-10-10**) → **L-1** schema v15 + identity + legends + poverty → **L-2** legends content → **L-3** tokens + father arc → **L-4** pregnancy, placement, adoption, missing piece → **L-5** temptation + `Active_Effects` + clutch → **L-6** hidden children + reveals + search → **L-7** press clippings → **L-8** Trailblazer content.
- **Substances:** **S-1** substance engine → **S-2** magnitudes → **S-3** content → **S-4** the asterisk.
- **Where to find details:** owners and dependencies are in temptation §14 and substances §8; roles are in [TEAM.md](TEAM.md).
- **Calibration:** every Phase L slice that touches `Assets/Simulation/` keeps "no rows or flags ⇒ bit-identical" and runs `run_monte_carlo_batch`.

**Exit criteria:**

- A full family story plays end to end in a scripted harness career: the father's first text and reveal; an affair with discovery; a placed child who later crosses paths with the heir; a search and a reclaim; an addiction with rehab; succession to an adopted or reclaimed heir.
- The Trailblazer reaches "the first."
- No Monte Carlo band moves with every new system idle.
- `check_event_graph_integrity` passes, including #9 (no voice notes), #11 (never abortion) and #12 (tokens).

## Phase 14 — Voice & Broadcast Audio → Milestone M9 "The Call"

**Specs:** [design/scope_realignment.md](design/scope_realignment.md) §3.4 governs the six-voice scope; [design/voice_and_broadcast_audio.md](design/voice_and_broadcast_audio.md) retains the pipeline contracts. **Production is paused for Gate 2; resume after Gate 4.**

- **ElevenLabs, offline only:** `Tools/VoiceGen` renders script sheets into mastered Ogg clips plus a manifest; the API key comes from the environment and never enters the repo or a build; a licensing log records every run; a human listens to every clip.
- **Play-by-play announcers:** **6 conference personas**, 2 per tier (HS, College, Pro), one booth per conference, about **900 lines each** (~5,600 total budget). SR-9 defines persona frameworks, trigger catalogs and scripts; 14c implements the revised sheets. About 10x situational depth per voice covers recklessness, rivalries, streaks and scandals, with fictional taglines. No per-team clips, spoken player names or avatar pronouns. `BroadcastContext` carries no biological parentage or hidden heirs; lineage calls unlock only after their narrative reveal.
- **Broadcasters only (owner, 2026-10-10):** no phone voice notes, no cutscene voice-over, no character dialogue; those are text. Avatar-facing calls use roles, never names or pronouns (the avatar can be male or female). The Trailblazer route adds a small gated set of its own calls.
- **Runtime:** Announcer / Music / Ambience buses with sidechain ducking (no Voice bus: voice is broadcasters only); a `VoiceDirector` autoload (presentation RNG only, so it's calibration-inert); a `BroadcastBooth` on the at-bat beat sequencer; a subtitle strip; volume sliders and toggles.

**Slices:** 14a buses + VoiceDirector → 14b VoiceGen + manifest + AudioManifestHarness → 14c personas + scripts → 14d BroadcastBooth + highlights → ~~14e voice notes~~ (cut) → 14f full generation + listening QA.

**Exit criteria:** all six conference booths call live games with their approved taglines and situational depth; `validate_audio_manifest` passes at 100% coverage; MC and Gritty harnesses untouched.

## Phase 15 — Visual Redesign → Milestone M10 "The Look, Reborn"

**Spec:** [design/visual_redesign.md](design/visual_redesign.md). User directive (2026-10-09): **the game may use a completely new visual design.** Phase 10's dark-mode thin-slice look is not the shipping look.

- **Recommended direction, "Two Worlds, One Life":** the DIAMOND side looks like a prime-time sports broadcast package; the DIRT side looks like night streets and the burner phone. The phone is the portal between them, team colors tint the UI systemically, and production value climbs with the tier. The user picks the final direction at the 15a gate.
- **Design system:** one Theme family, tokens and components; commercially licensed fonts; accessibility built in (colorblind-safe palette, text size, reduce motion, subtitles).
- **Every screen rebuilt**, including the **new front end** a Steam product needs (title screen; New Career with gender, ethnicity, the 10 positions and dating preference; loading, pause, credits), the Phase 13 league screens (bracket, draft board, recruiting, roster status), and the Phase L surfaces (the phone's **News** tab, the record book with asterisks, the Player screen's "missing piece" mark).
- **Art:** 68 team logos, park kits, scenes, portraits (women and every ethnicity in the identity catalog, the legends, the father), and icons. AI-assisted art is pre-generated, curated and disclosed. This phase **absorbs** the deprioritized Slice E (phone tiers) and Slice F (motion).
- 1080p reference, 16:10-safe, focus navigation everywhere (keeps Steam Deck and controller support reachable).

**Slices:** 15a art direction (user picks) → 15b design system → 15c screen rebuilds → 15d art production → 15e motion → 15f phone tiers. 15a–15b can run alongside Phase 13.

**Exit criteria:** every screen in the inventory uses the approved direction; no placeholder art; reduce-motion and text-size honored everywhere; inert sim (UI never writes state).

## Phase 16 — Cutscenes (In-Engine Motion Comics) → Milestone M11 "The Moments"

**Spec:** [design/cutscenes_motion_comic.md](design/cutscenes_motion_comic.md). User decision: in-engine motion comic, not video.

- A `CutscenePlayer` overlay plus JSON definitions (layered art, camera moves, captions, SFX, music, and broadcaster calls as audio beds; no character voice-over) with **live slots** (your portrait, name, team, stat line).
- **Four kinds:** story (~20, including the poverty prologue, the lineage reveal, the hidden-child reveals, the reclaim, overdose and death, and the Trailblazer's three acts), milestone (~10: Signing Day, Draft Night, first Pro game, championships…), **achievement** cards (one per achievement), and **big plays** (8 kits × 3 tier styles; the announcer's call is the audio bed).
- `CutsceneDirector` triggers come from the bus (new additive events `AchievementUnlocked`, `DraftCompleted`, `RecruitingCommitted`, `ChampionshipWon`) and from `BroadcastBooth.BigPlay`. Queue and rate limits; the game clock soft-pauses; everything is skippable; reduce-motion supported.

**Slices:** 16a player → 16b director + events → 16c big plays → 16d achievement cards → 16e story/milestone content. Depends on 14a/14b (booth calls as audio beds) and 15a/15b (art direction).

**Exit criteria:** every launch cutscene plays from its real trigger; `CutsceneHarness` green (refs, durations, reachability); MC and Gritty harnesses inert.

## Phase 17 — Steam Commercial Release → Milestone M12 "On Sale"

**Spec:** [design/steam_commercial_release.md](design/steam_commercial_release.md). It builds on the Phase 11 technical integration.

- **Achievements and rich presence** refreshed for the new league (Signed, Draft Day, First-Rounder, The Show, State/National Champs, Ring Bearer, Walk-Off, Grand Slam, Benched, Short-Handed, Going Straight, Banned for Life, plus the shipped set; "Went Pro" retired).
- **Legal and disclosure package:** the Steam AI-content survey (pre-generated art and ElevenLabs voices; no live generation), the mature-content survey (drugs and dealing, PEDs and substance abuse, simulated gambling, crime and violence, alcohol, implied sexual themes, adultery and pregnancy, overdose death, language), the ElevenLabs and art licensing logs, `THIRD_PARTY_NOTICES.txt`, and a **trademark screen** of the title, league and team names.
- **Store page:** capsules, screenshots, trailer, copy, system requirements.
- **Release-candidate QA:** the full gate suite, a full-career playthrough, Windows + Linux + Deck smoke tests, a cloud-save round-trip, install < 2 GB, a 10-season soak.
- **Save policy:** from 1.0, every schema change ships a migration and a frozen-save harness check.
- **Launch ops:** Coming Soon page early, Steamworks reviews, pricing, branches, patch cadence. The user owns every external submission and payment.

**Slices:** 17a achievements + presence → 17b legal + trademark → 17c store assets → 17d RC QA → 17e submission + launch.

**Exit criteria:** store and build reviews passed; RC checklist complete; the game is live on Steam.

## Revised Milestone Gate Sequence (scope realignment §4)

This order supersedes the earlier parallel Phase G/14/16 execution map. The four pivots are accepted; the gate refinements and remaining owner decisions retain their **PROPOSED** status in [design/scope_realignment.md](design/scope_realignment.md) §4/§9. This SR-0 handoff does not record a new owner approval.

| Gate | Exit / scope | Hold |
| :-- | :-- | :-- |
| **0 — Documentation alignment** | **SR-0:** GAME_VISION v3.0, GEMINI, BUILD_PLAN, TEAM and progress aligned to the scope spec. | Owner reads GAME_VISION v3.0. |
| **1 — Schema & core simulation freeze** | **13-1–13-3** merged to `p13/integration`, HS 10-game Friday season playable; **L-1 v15 DDL only** lands after v14. v14/v15 are pre-1.0 clean breaks, not migrations. | After v15 DDL, no new tables or backend mechanics until Gate 4; 13-4–13-7 / L-3–L-8 runtime only within already-designed v14/v15 and without blocking Gate 2. |
| **2 — 15-Minute Steam Next Fest Loop** | Cold boot — HS backstory — Burner Phone temptation — weekly stance — Friday `MicroGame` — consequences in <=15 minutes, zero UI friction. **SR-1a/b/c, SR-2a/b, SR-4**; owner picks **15a — 15b — 15c**, scoped to Burner Phone, dashboard/standings, broadcast at-bat view and stance UI. | Phases **14 and 16** paused. SR-3a/b optional clutch is not a prerequisite for the neutral loop and must not block it. |
| **3 — Golden Slice / visual identity lock** | **SR-5**, minimal 15d: one shipping-quality batter sheet, pitcher sheet, stadium backdrop and Burner Phone screen; owner validates readability, palette and joints. | Batch crowd/sprite generation, `Tools/SpriteBake` runs and 68-venue production. |
| **4 — External Playtest** | HS 10-game season, recruiting offers (**HS half of 13-6**) and HS gritty events, with local session logs measuring drop-off/confusion. | External distribution and Steam page actions remain owner actions. |

After Gate 4: resume the remaining Phase 13 and Phase L runtime, **Phase 14 / SR-9** (six booths), Phase 16 and Phase 17; **SR-6** mods loader, **SR-7** Tabloid feed and **SR-8** Rap Sheet exporter remain proposed commercial features (§6). **SR-5 belongs to Gate 3**, before batch assets resume. Every Tabloid/export DTO must preserve the secrecy invariant: only `LineageQueries` reads `Biological_Parentage`, and no father/hidden-heir hints precede narrative unlock.

Slice ownership and owner gates: [TEAM.md](TEAM.md) §2 and [scope_realignment.md](design/scope_realignment.md) §7 (**SR-0 through SR-9**). The file-by-file Gate 2 roadmap and regression envelope are in scope_realignment §5. Life and baseball communicate only through the async event bus and SQLite; presentation never drives simulation. Additive stance/event framework work is limited to that specified roadmap; neutral macro/micro paths stay byte-identical.

## Cross-Cutting Discipline (every session)

- Follow [TEAM.md](TEAM.md) §3: brief → your own branch and worktree → gates → independent review → merge → a compact `progress.md` entry.
- Any `Assets/Simulation/` change runs `run_monte_carlo_batch`. The resolver guard stays byte-exact, and re-baselining a season guard needs explicit sanction.
- Gates: event content → `check_event_graph_integrity`; schema → `validate_sqlite_schema`; voice → `validate_audio_manifest`; cutscenes → `CutsceneHarness`; exports → `validate_steamworks_native`.
- Presentation (announcers, cutscenes, the phone, press, the redesign) never writes sim state.
- **One home per decision:** direction changes go into GAME_VISION first, then the active spec, never as a header note on an old doc ([README.md](README.md)).
- No secrets in any tracked file. The repo is private, but its history was public until 2026-10-10. Keys live in env vars or the git-ignored `.env`.
- External submissions, payments and legal sign-offs are the owner's actions.

## Phase 13 Source Spec — Multi-Tiered League Specifications (user-authored, 2026-10-09)

*Kept verbatim as the source of truth for intent. Every ambiguity is resolved, and flagged, in [design/league_restructure.md](design/league_restructure.md) §16.*

*Amended by the owner on 2026-10-18 (recorded in [GAME_VISION.md](GAME_VISION.md) §4 and league_restructure §16-I6/I7): College now plays **2 games a week** (Wednesday + Saturday, 28 games), and its final is **The Red Dirt Series**, a best-of-3, in place of the single-game National Championship below. The text below stays as originally written.*

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

## R-1 accepted execution order (2026-10-10)

The owner accepted every recommendation in [Q01–Q17](review/r1/06_owner_questions.md). Approval settles scope and priorities; it does not certify formulas or mark runtime work done. R-1 contract reconciliation is complete on review/r1; integration remains reviewed, never automatic.

**Execution overlay (2026-10-25):** the revised gates above govern sequencing; this table retains accepted repair scope and ownership, not authorization to bypass the freeze or resume paused production.

13-0 is done (2026-10-10). Next: **13-1 (GPT 6.1 Sol, on `p13/integration`)**, then **R1-SAVE-1 (GPT 6.1 Sol)** on `main` (the v13 game still boots there; SAVE-1 is merged forward into `p13/integration`), while **R1-MATH (Math)** progresses independently. *(2026-10-13: G-8a is retired by the arcade decision; R1-MATH keeps its fatigue and life-modifier contracts.)* SAVE-1 and 13-1 both touch `GameManager`/`LeagueSimulator`, so one model (GPT 6.1 Sol) sequences them. Save recovery is a release gate, not optional polish. Keep one end-to-end saved career ahead of content volume.

| Brief | Owner | Exit/dependency |
| --- | --- | --- |
| [R1-SAVE-1](tasks/R1-SAVE-1_save_boundary.md) | GPT 6.1 Sol; GPT 6 Astra review | Quiescent saves, all-league flush, dirty retry and database ownership |
| [R1-SAVE-2](tasks/R1-SAVE-2_recovery_and_rng.md) | GPT 6.1 Sol; GPT 6 Astra review | After SAVE-1: durable day/choice/resolution recovery and independent RNG continuation |
| [R1-FAMILY](tasks/R1-FAMILY_partner_invariants.md) | GPT 6.1 Sol; GPT 6 Astra review | Partner invariant repair; after L-1, civilian/kin eligibility must precede L-4 |
| [R1-MATH](tasks/R1-MATH_gameplay_contracts.md) | GPT 6 Astra (Math); GPT 6.1 Sol review | Fatigue, O1 (now the arcade difficulty target) and life-modifier contracts. Its pitching-centering/waste part is retired (2026-10-13) |
| [R1-EFFECTS](tasks/R1-EFFECTS_life_projections.md) | GPT 6 Astra projection; GPT 6.1 Sol development | After Math: capped needs/stress, actual-practice work_ethic |
| [R1-LEARN](tasks/R1-LEARN_practice_and_modifiers.md) | GPT 6 Astra seam; GPT 6 Sol UI | Stable arcade DTOs: isolated practice on the arcade engine, pitch explanation, effective-rating breakdown |
| [R1-REVEAL](tasks/R1-REVEAL_durable_story_handoff.md) | GPT 6.1 Sol Core; GPT 6 Sol presentation | L-3/16b + SAVE-2: durable revelation before succession, playback never gates it |
| [R1-STEAM](tasks/R1-STEAM_offline_eligibility.md) | GPT 6.1 Sol durability; GPT 6 Sol 17a | SAVE-2: unavailable-Steam eligibility and idempotent reconciliation |

14b follows its updated brief (GPT 6.1 Sol review, per-clip crash recovery); no new generation spend. 15 uses reusable team-dressed kits; approve representative art before volume. Park factors are outside 1.0. *(Manual fielding was too, until the 2026-10-13 arcade decision put assisted fielding in launch scope.)* Full 13/L/G career, interrupted-save parity, Godot input/UX and exported Windows/Linux checks precede release.

Still required as concrete inputs: owner art/identity/legend/name selections; ChooseFatherFallback and DiscoveryChance intent; commercial/Steam prerequisites. Auto-committer policy prohibits main/shared targets; actual external configuration must be verified separately. These are not reopened yes/no votes on accepted R-1 scope.
