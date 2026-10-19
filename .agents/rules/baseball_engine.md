# **Baseball Simulation Engine Rules**

## **1. Macro-Simulation (Monte Carlo)**

Used for simulating background league games.

Do not simulate pitch-by-pitch. Calculate plate appearance (PA) outcomes based on empirical probabilities.

Compare batter attributes against pitcher attributes and defensive fielding ratings to generate outcome percent ages (1B, 2B, HR, SO, BB).

## **2. Micro-Simulation (Markov Chain)**

Used when the player's avatar is actively in a game.

Track exactly 25 distinct base-out states per half-inning (e.g., State 1: 0 outs, empty bases).

Form a 25x25 transition matrix for each potential event.

Blend player inputs (timing/location) with their database attributes to determine the transition event.

PED Modifiers
Active PED use applies a temporary 1.5x multiplier to power/stamina stats during matrix calculations.

Deduct from health_ceiling and increase detection_risk in the database after every game played under the influence.

`ped_active` is **engine-written by the substance engine only** (Phase L, `docs/design/substances_and_addiction.md` §4). Until S-1 lands, nothing in the live game sets it.

**Arcade engine (Phase G, owner 2026-10-13, `docs/design/arcade_baseball_overhaul.md`):** every game the avatar's team plays runs on a real-time physics engine in `Assets/Simulation/Arcade/`, not on `MicroGame`. Background games stay on the macro sim, and its neutral policies and resolver guard stay byte-identical.

- **Engine-independent:** no Godot types inside `Assets/Simulation/Arcade/`.
- **Deterministic:** a fixed step, contact and catches solved analytically, a seeded `RngState` plus recorded inputs. Watched = skipped.
- **Shared ratings:** the arcade engine reads `EffectiveRatings`, the same modifier stack as the macro sim.
- **Calibration:** AI-vs-AI games hit the Phase 13 per-tier bands, gated by `Tools/ArcadeHarness`. Physics constants and AI noise tables are Math-owned data.
- **Isolation:** the arcade engine never references the Life sim, the same boundary as the rest of the baseball engine.

Gender, ethnicity and listed position never enter the sim. The defensive position comes only from `Player_Fielding`.

## **3. League Structure (Phase 13 — `docs/design/league_restructure.md`)**

Three tiers only: `LeagueTier` HS = 0, College = 1, Pro = 2. There are no minor leagues.

| | HS | College | Pro |
| :-- | :-- | :-- | :-- |
| Teams | 12 (2 conf × 6) | 24 (2 conf × 3 div × 4) | 32 (2 conf × 4 div × 4) |
| Innings | 7 | 9 | 9 |
| Regular season | 10 games, Fridays | 28 games, Wednesday + Saturday (14 weeks) | 64 games, Tue/Thu/Sat |
| Playoffs | 6-team bracket, #1 seeds bye | 18-team single-elimination tournament (17 ranked + play-in) → **The Red Dirt Series**, best-of-3 Thu–Fri–Sat, game 3 only if needed | 12 teams, Bo3 rounds → Bo5 World Championship |

- Regulation innings are a per-tier parameter (`TierRules.RegulationInnings`), never a literal `9`.
- Roster sizes are capacities (`RosterCapacity`); loops run to per-team counts so buffers stay fixed-size and allocation-free.
- No tier plays on Sunday. The Red Dirt Series name is pending the owner's trademark screen: player-facing text uses a strings-table key, never a C# literal. Never use MLB's or the NCAA's championship-series names. `ChampionshipWonEvent` carries the tier; only a Pro title can fire the lineage reveal.
- Starter selection: the best available starter with `days since last start >= MinRestDays(tier)` (HS 4, College 3, Pro 3; a day count is the calendar-date difference, so Wed → Sat = 3). This makes the College ace start every game (owner, 2026-10-18) and produces the ace-heavy 2-man Pro rotation by design. Within one series no starter starts twice; the short-rest fallback skips them and a reliever opens if no one else qualifies.
- Pro: Sunday-night roster lock; absent players are never replaced; a team that can't field 9 position players plus a pitcher forfeits (7–0 HS, 9–0 otherwise, no stats credited).
- Tier environments: effective-rating deltas on pitStuff/pitControl/teamDefense: HS −20, College −16, Pro 0. Never edit the resolver's calibration tables to create tier identity.
- Pro is calibrated to MLB norms (AVG .240–.260, OBP .308–.325, SLG .395–.430). Runs are banded per 9 innings so 7-inning HS games compare cleanly.

## **4. Presentation Boundary**

The announcer (`BroadcastBooth`) and cutscenes (`CutsceneDirector`) read the at-bat beat sequencer's output. The micro-sim never calls into them, and they never consume `RngState`.
