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

## **3. League Structure (Phase 13 — `docs/design/league_restructure.md`)**

Three tiers only: `LeagueTier` HS = 0, College = 1, Pro = 2. There are no minor leagues.

| | HS | College | Pro |
| :-- | :-- | :-- | :-- |
| Teams | 12 (2 conf × 6) | 24 (2 conf × 3 div × 4) | 32 (2 conf × 4 div × 4) |
| Innings | 7 | 9 | 9 |
| Regular season | 10 games, Fridays | 14 games, Saturdays | 64 games, Tue/Thu/Sat |
| Playoffs | 6-team bracket, #1 seeds bye | 18-team tournament (17 ranked + play-in) → single-game final | 12 teams, Bo3 rounds → Bo5 Championship |

- Regulation innings are a per-tier parameter (`TierRules.RegulationInnings`), never a literal `9`.
- Roster sizes are capacities (`RosterCapacity`); loops run to per-team counts so buffers stay fixed-size and allocation-free.
- Starter selection: the best available starter with `days since last start >= MinRestDays(tier)` (HS/College 4, Pro 3). This produces the ace-heavy 2-man Pro rotation by design.
- Pro: Sunday-night roster lock; absent players are never replaced; a team that can't field 9 position players plus a pitcher forfeits (7–0 HS, 9–0 otherwise, no stats credited).
- Tier environments: effective-rating deltas on pitStuff/pitControl/teamDefense: HS −20, College −16, Pro 0. Never edit the resolver's calibration tables to create tier identity.
- Pro is calibrated to MLB norms (AVG .240–.260, OBP .308–.325, SLG .395–.430). Runs are banded per 9 innings so 7-inning HS games compare cleanly.

## **4. Presentation Boundary**

The announcer (`BroadcastBooth`) and cutscenes (`CutsceneDirector`) read the at-bat beat sequencer's output. The micro-sim never calls into them, and they never consume `RngState`.
