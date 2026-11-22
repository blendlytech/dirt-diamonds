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

**Clutch Moment / Arcade core (Phase G reduced to Clutch Moment, `docs/design/scope_realignment.md` §2–§3):**

- Attended games use the Markov `MicroGame` (owner, 2026-10-25); `Assets/Simulation/Arcade/` is only the optional, offense-only **Clutch Moment** timing check for the avatar's high-leverage plate appearances (`ClutchResolver` returns contact quality; a capped, centered tilt feeds `MicroGame`, which remains authoritative for the play result). Full 9-inning arcade physics, real-time fielding and baserunning are cancelled.
- **Deterministic timing core:** `Assets/Simulation/Arcade/` stays pure C#, with no Godot types, a fixed step, a seed and recorded input. Godot renders snapshots and emits input intent; presentation never drives simulation or consumes the sim's RNG. Batting practice is unscored, writes nothing and uses its own RNG.
- **Neutral paths stay byte-identical:** skipping the clutch moment, autopilot and background games apply no tilt and preserve the calibrated macro/micro sim and resolver guard. The timing check never supplies a play result directly.
- **One modifier stack:** effective ratings (tier, person, gear, rust, rivalry, `Active_Effects`, PED, fatigue) are built once in `EffectiveRatings` and shared by the macro sim, `MicroGame` and `ClutchRequest`. Never re-derive them inside one engine.
- **Calibration and gates:** Gemini 4 Argon owns clutch thresholds, frequency, tilt caps and centering constants (SR-3a); Claude Opus 5.5 reviews. `Tools/ArcadeHarness` validates the clutch/timing core. `run_monte_carlo_batch` validates sim integration and neutral byte identity. Shared `EffectiveRatings` changes run both harnesses.
- **Isolation:** `Assets/Simulation/Arcade/` never references the Life sim, the same boundary as the rest of the baseball engine.

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
