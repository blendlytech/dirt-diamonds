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

**What it does:** simulates 168 in-game hours (one week) of PASSIVE Need decay — no eating, sleeping, or other replenishing actions — for a standard NPC starting fully satisfied, and prints a text-based graph (density-ramp sparkline, one row per need) plus a fixed-hour table (0/3/6/12/24/48/72/96/120/144/168h) for Hunger, Sleep, Hygiene, Social, Fitness. It then exercises the utility/action-selection layer (`UtilityCalculator`, `ActionCatalog`) directly, and finally drives a real `LifeSimManager` through a simulated month via a local `EventBus`.

**What it proves (exit code 0 = all pass):**

1. **Bounds** — every need stays within [0, 100] across the week (clamping correctness).
2. **Monotonic passive decay** — values never rise under pure passive decay (no stress/environmental noise applied).
3. **3-hour game anchor** — no need drops below 65 after a 3-hour attended baseball game, i.e. players aren't starving to death after a game (the skill's core mandate).
4. **168-hour neglect anchor** — every need reaches `NeedsEngine.CriticalThreshold` (20) somewhere within the week of total neglect, so the life_sim_ai.md stress-override behavior has something to fire on.
5. **Relative pacing** — Hunger (fastest-tuned need) reaches critical within the first 48h; Fitness (slowest-tuned need) takes the longest of all five to reach critical — biological needs should outpace lifestyle needs.
6. **Modifier layer (E/S) fixtures** — the design doc's §9 modifier fixtures verbatim (`E=1.5·S=1.5` on full Hunger → 90.55; `S=2.0` on Hunger 40 → 26.76 vs calm 33.38), the §4.2 combined `E·S ≤ 3.0` ceiling clamp, the scalar-overload ≡ uniform-vector identity (the §4.1 `E_all` degenerate case, bit-exact), a per-need vector proof (an hour under `Workout`'s environment hits Hunger ×1.5 / Sleep ×1.3 while the other three needs decay bit-identically to neutral), and a data-integrity sweep asserting every `ActionCatalog` action's `Environment` multipliers stay inside the §4.1 design range [0.25, 3.0].
7. **Utility action-selection fixtures** — `UtilityCalculator.SelectAction` picks `Idle` when fully satisfied; picks the action that targets a critical need (e.g. `Eat` when Hunger ≤ `CriticalThreshold`) over both `Idle` and the stress-relief actions; and, when broke *and* in crisis, falls back to a free stress-relief action (`PickArgument`) rather than an unaffordable remedy — proving the stress override fires, targets the right need when affordable, and stays affordability-aware when not.
8. **`LifeSimManager` month-long proof (the M3 exit criterion, literally)** — an adequately-funded synthetic NPC, driven through 30 `DayAdvancedEvent`s, never has any need bottom out at 0 (contrast with checks 1-2's pure-passive trace, which floors every need by design). A companion broke NPC keeps its free needs (Sleep, Hygiene — $0 actions) managed but its money-gated needs (Hunger, Social, Fitness) collapse exactly like total neglect — expected, since no income mechanic exists yet (Phase 8 territory), not a bug in the selection logic.

**Tuning workflow — decay curves:** each need's curve lives in `NeedsEngine.cs` as a `static readonly NeedDecayProfile` (`BaseDecayPerHour`, `AccelerationCoefficient`, `AccelerationPower`) — tuning is a data edit, no logic change. Raise `BaseDecayPerHour` to make a need decay faster near full; raise `AccelerationCoefficient`/`AccelerationPower` to make the *desperation* end steeper without touching the early-game feel. Re-run after every change; if the 3-hour anchor fails, decay is too aggressive near full satisfaction — lower `BaseDecayPerHour` first before touching acceleration.

**Tuning workflow — environment vectors:** each action's per-need `EnvironmentalModifiers` (`ActionCatalog.cs`, applied by `LifeSimManager` to every decay tick spent performing that action) is a third data table. Values are design-doc §4.1 anchors where one exists (Sleep/Shower at home ×0.8 all; SocializeEvening Social ×0.4; Workout Hunger ×1.5) — keep authored values inside [0.25, 3.0] (a check enforces it) and remember `E·S` clamps at 3.0 in the engine. `SelectAction` does *not* see these (environment is a decay-side term, not a utility consideration), so retuning them moves the month-run trajectories but never the crossover sweep.

**Tuning workflow — action selection:** the action catalog (`ActionCatalog.cs`) and the consideration weights (`UtilityCalculator.ActionWeights`/`DefaultWeights`) are the two data tables that drive `SelectAction`. Raise an action's `RestoreAmount` or lower its `TemporalCostHours`/`FinancialCost`/`Risk0To100` to make it more attractive in general; raise `ActionWeights.NeedDeficitWeight` to make desperate needs dominate selection harder, or raise `StressReliefWeight` to make coping actions more competitive once a need is critical. Re-run after every change — the fixtures above hard-code their expected winners by utility value, so a reweight that flips one is expected to require updating the fixture's expected outcome, not just its pass/fail.

**Tuning workflow — stress scalar (live since Phase 7):** four more named constants, all data edits. `NeedsEngine.MaxStressModifier` (2.5) is the §4.2 `S_max` — how hard stress 100 accelerates all decay. `LifeSimManager.StressRelaxationPerHour` (0.4) sets how fast an arc fades passively (+25 event ≈ 2.5 days); `LifeSimManager.StressReliefPerAction` (15) is what one `DrinkAlone`/`PickArgument` buys back; `UtilityCalculator.StressOverrideThreshold` (70) is where high stress alone forces stress-relief actions with zero need deficit. The stress checks live in `Tools/GrittyEventsHarness` (stress is bus-fed by gritty events), so after retuning these run **both** harnesses: this one (the stress-0 traces must stay byte-identical — stress 0 maps to S=1 by construction) and `dotnet run --project Tools/GrittyEventsHarness -c Release` (relaxation/override/relief fixtures encode the current constants).
