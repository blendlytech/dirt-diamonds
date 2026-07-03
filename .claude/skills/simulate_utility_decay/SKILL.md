---
name: simulate_utility_decay
description: Balances the life simulation aspects (The Sims elements).
---
# simulate_utility_decay

**Purpose:** Balances the life simulation aspects (The Sims elements). Mandatory after any change to `NeedsEngine.cs`'s decay profiles (life_sim_ai.md's Needs Engine mandate).

**Execution:**

```powershell
dotnet run --project Tools/NeedsDecayHarness
```

**What it does:** simulates 168 in-game hours (one week) of PASSIVE Need decay — no eating, sleeping, or other replenishing actions — for a standard NPC starting fully satisfied, and prints a text-based graph (density-ramp sparkline, one row per need) plus a fixed-hour table (0/3/6/12/24/48/72/96/120/144/168h) for Hunger, Sleep, Hygiene, Social, Fitness.

**What it proves (exit code 0 = all pass):**

1. **Bounds** — every need stays within [0, 100] across the week (clamping correctness).
2. **Monotonic passive decay** — values never rise under pure passive decay (no stress/environmental noise applied).
3. **3-hour game anchor** — no need drops below 65 after a 3-hour attended baseball game, i.e. players aren't starving to death after a game (the skill's core mandate).
4. **168-hour neglect anchor** — every need reaches `NeedsEngine.CriticalThreshold` (20) somewhere within the week of total neglect, so the life_sim_ai.md stress-override behavior has something to fire on.
5. **Relative pacing** — Hunger (fastest-tuned need) reaches critical within the first 48h; Fitness (slowest-tuned need) takes the longest of all five to reach critical — biological needs should outpace lifestyle needs.

**Tuning workflow:** each need's curve lives in `NeedsEngine.cs` as a `static readonly NeedDecayProfile` (`BaseDecayPerHour`, `AccelerationCoefficient`, `AccelerationPower`) — tuning is a data edit, no logic change. Raise `BaseDecayPerHour` to make a need decay faster near full; raise `AccelerationCoefficient`/`AccelerationPower` to make the *desperation* end steeper without touching the early-game feel. Re-run after every change; if the 3-hour anchor fails, decay is too aggressive near full satisfaction — lower `BaseDecayPerHour` first before touching acceleration.
