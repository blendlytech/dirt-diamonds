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
5. **Micro-sim analytics (micro doc §3–§5)** — all seven 25×25 A_e matrices row-stochastic and equivalent to the runtime advancement functions, RE24 anchor from the fundamental matrix (empty/0-out in 0.42–0.56, ordering sane), the §5.2 count-chain absorption pinned to `AtBatResolver.ComputeProbabilities` analytically (≤1e-9), sampled per-PA neutral consistency, pitches/PA 3.7–4.0, zero-alloc pitch chain.
6. **Micro-sim attended games (micro doc §11)** — 2000 neutral-policy games (fatigue off) land inside the macro §8 ranges (test 2); late-inning OPS rise +.030–.150 and R/G uplift with fatigue on (test 4); PED capacity fixture 70→105→120 + post-game costs (test 5); bit-for-bit game determinism and zero-alloc warm attended games incl. human PAs (test 6); Game_Logs play-by-play + additive box-score flush.
7. **Career wiring (Phase 5)** — avatar creation (roster stays 9+5, displaced player benched to free agency, Game_State records the id, reboot restores it); a full autopilot season through the real event loop where the macro sim skips the attended team's pairing and the micro-sim plays it: every game played exactly once (GS = 2×games, W = L = games), macro cycle flushes + micro per-game flushes compose on the same season rows (ledgers agree, league line stays in §8), avatar season PA 450–800; the `PlayerIntentBridge` handshake drives a full interactive game from a scripted UI thread; a cancelled interactive game unwinds unflushed and is forfeited to the autopilot on the next tick.

**Tuning workflow (macro doc §8 / micro doc §11, change one block then re-run):** slash line off → §2 baselines; stars/scrubs spread wrong → §4.2 sensitivities; a skill inert/overpowered → §4.1 weights; runs off but slash fine → `BaseOutAdvancement.SingleScoresFrom2nd` / `DoubleScoresFrom1st`; micro pitches/PA off → `PitchChain.FoulShareOfStrikes`; fatigue too weak/strong → `PitcherFatigue` curve then capacity knobs; human input feel → `PitchChain` gains / `PlayerInputModel` (gated on the neutral-consistency tests staying green).
