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
