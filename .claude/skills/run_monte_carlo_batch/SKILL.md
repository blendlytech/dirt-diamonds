---
name: run_monte_carlo_batch
description: Tests the baseball simulation engine's statistical accuracy in isolation.
---
# run_monte_carlo_batch

**Purpose:** Tests the baseball simulation engine's statistical accuracy in isolation. Mandatory after **every** change to `AtBatResolver.cs` or the PED modifier weights (CLAUDE.md §Required Workflow Procedures).

**Execution:**

```powershell
dotnet run --project Tools/MonteCarloHarness -c Release
```

Optional: `-- --pa <count>` raises the Monte Carlo batch size (default 100,000; floor 10,000), `-- --repo <path>` if run outside the repo tree.

**What it proves (exit code 0 = all pass):**

1. **§5 numeric fixtures** — the resolver reproduces the worked examples in `docs/design/baseball_pa_outcome_model.md` (average baseline, elite slugger, ace) to ±1e-3, plus the §6 PED clamp (`min(100, round(power×1.5))`), seed determinism, and zero heap allocation per PA.
2. **§8 batch acceptance** — ≥10k average-vs-average PAs land inside every acceptance range (AVG .240–.260, OBP .308–.325, SLG .395–.430, OPS .710–.745, K% 20–25, BB% 7.5–10.5, HR/PA 2.7–3.7) and prints the slash line.
3. **Full pipeline** — two complete headless seasons on a scratch db (LeagueGenerator → TimeManager/EventBus → LeagueSimulator → season flush → StatsNormalizer), league-wide slash + R/G 4.2–4.8 per season, batting/pitching ledger consistency, stored rates match recomputation, integrity/FK checks.
4. **Flat GC profile** — a warmed game day allocates zero bytes.

**Tuning workflow (design doc §8, change one block then re-run):** slash line off → §2 baselines; stars/scrubs spread wrong → §4.2 sensitivities; a skill inert/overpowered → §4.1 weights; runs off but slash fine → `LeagueSimulator.SingleScoresFrom2nd` / `DoubleScoresFrom1st`.
