# Visual Redesign — New Art Direction, Design System and Every Screen (Phase 15)

**Owner:** Gemini 3.1 Pro (theme architecture, scene rebuilds, review) · Gemini 3.8 Flash (screen builds from approved frames, asset integration) · the user (art-direction sign-off at every gate)
**Status:** design complete 2026-10-09; no code yet. **Authority:** [GAME_VISION.md](../GAME_VISION.md) §5. User directive, 2026-10-09: *"It can use a complete new design visually."*
**Supersedes:** the visual parts of [presentation_layer_narrative.md](../reference/presentation_layer_narrative.md) §3 (theme) and §6 (portrait house style), [scene_art_guide.md](../archive/scene_art_guide.md) (house style), and the deprioritized Slice E (phone tier theming) and Slice F (animation pass), both absorbed here as 15f/15e. The *architecture* parts of those docs (scene isolation, read-only UI, bus signals) stay in force.

---

## 1. The directive and what it frees up

Every UI shipped to date is a deliberate thin slice of plain Godot controls: functional, legible, not a product. This phase **replaces the whole look**: layouts, theme, typography, color, iconography, motion, art and the main menu. Nothing visual is sacred.

**Carries over unchanged (the contract, not the look):**

- The **information architecture**: a baseball side (dashboard, stats, scouting, standings, calendar, at-bat) and a street side (the burner phone: contacts, messages, deals, bank).
- **UI is read-only over sim state.** DTOs in, player-intent signals out, one scene per interactive surface, node paths verified before wiring ([ui_conventions.md](../../.agents/rules/ui_conventions.md)).
- The performance rules: pooled elements, no per-frame formatting, dirty-flag label updates.

Because UI never writes state, **the entire redesign is calibration-inert**. No Monte Carlo re-band is needed; only "no regression" gate runs.

---

## 2. Recommended creative direction — "Two Worlds, One Life"

*A recommendation for the 15a exploration to validate or overturn. The user picks the direction at the 15a gate.*

The title is the art direction. The player lives in two visual worlds, and **the UI should change worlds when the player does**.

| | **DIAMOND** (baseball side) | **DIRT** (street side) |
| :-- | :-- | :-- |
| Reference feel | Prime-time sports broadcast graphics: scorebugs, lower-thirds, stat wipes | Night streets, burner phones, evidence photos, pawn-shop receipts |
| Light | Stadium lights, clean daylight, crisp edges | Sodium-vapor orange, neon spill, deep shadow |
| Color | Team colors drive accents on a confident neutral base | Desaturated, grainy, single neon accent; red for danger |
| Type | Bold condensed sports display + clean numerals for stats | Typewriter/receipt mono + handwritten marker annotations |
| Texture | Glossy panels, broadcast motion wipes | Film grain, halftone, polaroid borders, tape, creases |
| Motion | Fast, confident wipes and count-ups (a broadcast package) | Slow drifts, flicker, glitch on bad news |

- **The phone is the portal.** It's always on screen, a physical object in the DIAMOND world and the doorway into DIRT. Opening a shady thread "lowers the lights" (a vignette, the grain comes up, the accent turns neon).
- **Team identity is systemic.** Every team has primary/secondary/accent colors. Viewing a team (your own, an opponent, a draft card) re-tints the DIAMOND accents through theme variations, not per-scene styling.
- **Tier progression is visible.** HS looks like a hand-painted scoreboard and a local stream. College looks like a regional cable broadcast. Pro gets the full prime-time package. The visual production value climbs with the player's career.

---

## 3. Design system (15b)

One **Godot `Theme` resource family** under `Assets/UI/Theme/` is the single retrofit lever (same principle as Phase 10a, rebuilt from zero):

- **Tokens:** color (neutral ramps; semantic success/warn/danger/info; DIAMOND and DIRT palettes; per-team triplets loaded from `Assets/Data/Teams/team_colors.json`), type scale (display / headline / title / body / caption / numeric), spacing (4-pt grid), radii, elevation, border styles, and motion durations/easings as `const`s in one `UiMotion` class.
- **Components:** button (primary/secondary/ghost/danger), card, stat tile, meter/progress bar, tab bar, list row, message bubble (text / choice chips), scorebug, lower-third, bracket node, draft card, toast, modal, subtitle strip.
- **Fonts:** only licenses that permit commercial game distribution (SIL OFL or bought commercial licenses). Every font's license file is kept in `Assets/UI/Fonts/LICENSES/` and listed in the third-party notices ([steam_commercial_release.md](steam_commercial_release.md) §4).
- **Accessibility built in:** a colorblind-safe semantic palette (outcome colors never rely on red/green alone), a text-size setting (100/115/130%), a reduce-motion setting (it also governs cutscene camera moves), subtitles default on, and focus outlines.

---

## 4. Platform and layout requirements

- **Reference resolution 1920×1080**, scaling cleanly to 1280×720 and 3840×2160.
- **16:10 safe** (1280×800, 1920×1200) so a Steam Deck layout works without letterboxing the important parts.
- **Full keyboard and controller focus navigation** (focus neighbors on every interactive control, visible focus states). Controller support isn't required for launch, but building focus into the redesign keeps Steam Deck Verified and controller play reachable without rebuilding screens ([steam_commercial_release.md](steam_commercial_release.md) §6).
- Mouse remains the primary input.

---

## 5. Screen inventory (15c) — everything gets redesigned

| Group | Screens |
| :-- | :-- |
| **Front end (new, needed for Steam)** | Title screen / main menu (Continue, New Career, Settings, Credits, Quit), New Career (avatar creation: name, **gender**, **ethnicity**, **listed position** (SP, RP, C, 1B, 2B, 3B, SS, LF, CF, RF), **dating preference**, look, ratings budget, starting school; [temptation_and_lineage.md](temptation_and_lineage.md) §3.1), loading screens (tips + art), pause menu, credits |
| **Shell** | Home layout (DIAMOND dashboard + phone), top bar (date, clock, funds), time controls |
| **Venues (new, high priority)** | One **interactive venue screen per team (68)**: an HS school screen or a College/Pro stadium screen, with hotspots (field, clubhouse/locker, office, training room, stands, lot) and the team's own art; the opponent's venue for away games ([gameplay_core.md](gameplay_core.md) §3) |
| **Baseball** | At-bat view (broadcast scorebug, strike zone, pitch reveal, count/bases/score, subtitle strip for announcer), box score, standings (conferences/divisions), leaders, playoff bracket, National Ranking, Draft board / Draft Night, Recruiting offers, roster status (active/inactive), scouting report card, player page, team page |
| **Life** | Schedule planner (hour blocks), calendar (weekly game days, postseason), needs panel, household board, school/GPA |
| **Phone (DIRT portal)** | Lock screen, home, Messages (threads, bubbles, choice chips; the `unknown_fan` thread re-labels at the reveal), **News** (press clippings, [temptation_and_lineage.md](temptation_and_lineage.md) §9), Events/History, Contacts, Bank, Marketplace/items, Calendar, Player, League, Settings, phone-tier skins (absorbs Slice E: each phone tier is a distinct physical model) |
| **Hustles** | Narcotics, Fencing, Robbery, Texas Hold'em, Legal Work. Each gets its own environment art and a DIRT treatment while keeping the HustleFeel reveal timing |
| **Overlays** | Event choice prominence (badge + banner), tutorial overlay, toasts, modals, cutscene player chrome (skip, subtitles) |

Every screen goes through the same loop: **style frame → user approval → build → screenshot review → live boot.**

---

## 6. Art production (15d)

| Asset set | Count | Notes |
| :-- | :-- | :-- |
| Team logos + color triplets | 68 | Original marks; trademark-screened |
| Ballparks / fields | ~10 kits | HS field kit (4 variants), college park kit (3), pro stadium kit (3), team-tinted |
| Location scenes | existing list in [scene_art_guide.md](../archive/scene_art_guide.md) + new | Home, school, clubhouse, street corner, pawn shop, jail, hospital, etc., in the new style |
| Contact and player portraits | about 40 authored + procedural fallback, covering **men and women in every identity-catalog ethnicity**, the legends roster (young and old), and heirs | Re-done in the new style; the existing Midjourney portraits are retired or restyled |
| Icons | ~120 | One icon family covering both worlds |
| Cutscene art kits | per [cutscenes_motion_comic.md](cutscenes_motion_comic.md) §6 | Produced with the same direction |

**AI-assisted art** (e.g. Midjourney) is allowed **pre-generated only, human-curated and cleaned up**, and disclosed in the Steam content survey. Logos and UI components are designed, not generated, to keep them trademark-clean and consistent.

---

## 7. Motion (15e, absorbs Slice F)

A broadcast-package motion language for DIAMOND (wipes, count-ups, scorebug transitions) and a drift/flicker language for DIRT. The shipped `HustleFeel` `ResultReveal` builder and the funds ticker are generalized into a shared `UiMotion` toolkit. Everything respects reduce-motion. Tweens are allocated per transition, never per frame.

---

## 8. Sub-slices and gates

| Slice | Content | Owner | Gate |
| :-- | :-- | :-- | :-- |
| **15a** | Art direction exploration: 3 style frames per world, a moodboard, a logo test on 4 teams | Pro (with art tools) | **User picks the direction** |
| **15b** | Design system: Theme family, tokens, components, fonts and licenses, team colors | Pro | Component gallery scene reviewed by the user |
| **15c** | Screen rebuilds in priority order: front end → shell → at-bat → phone → baseball screens → life → hustles → overlays | Flash builds → Pro review | Per screen: screenshot review + live boot clean + inert sim |
| **15d** | Art production (logos, parks, scenes, portraits, icons) | Flash (integration) + user (taste) | Asset manifest complete; no missing-texture fallbacks |
| **15e** | Motion pass | Pro | Reduce-motion honored everywhere |
| **15f** | Phone tier skins | Flash → Pro review | Each tier is visually distinct |

**Dependencies:** 15a and 15b can start alongside Phase 13. Screens for new league features (bracket, draft, recruiting, roster status) are designed here directly. Their thin functional versions from Phase 13 exist only to unblock harness and playtest work.
