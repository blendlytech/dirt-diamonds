---
name: run_pipeline_harness
description: Gates the 13-6 pipeline (graduation, recruiting, the Draft, Pro turnover) and the development curve over 20 simulated seasons, and runs the composed 1,193-day journey.
---
# run_pipeline_harness

**Purpose:** the gate for any change to `PipelineManager`, `PipelineRules` / `PipelineProfile` values, `PromotionScore` / `PromotionProfile`, the development curve (`DevelopmentManager`, `DevelopmentCurve`, `DevelopmentProfile`) or the pipeline-generated players in `LeagueGenerator` ([13-6 brief](../../../docs/tasks/13-6_pipeline_draft_brief.md) §5, D7-A). Run it alongside `run_monte_carlo_batch`, never instead of it.

**Execution:**

```powershell
dotnet run --project Tools/PipelineHarness -c Release            # default: 20 offseason-only seasons (≤ 60 s)
dotnet run --project Tools/PipelineHarness -c Release -- --full  # 1,193 days, every system composed (≤ 4 min)
```

`-- --repo <path>` if run outside the repo tree. Exit code 0 = all pass.

**Default mode (20 seasons, no game sims, P reads neutral; world 20261603, development 0x9D7000, pipeline `DefaultPipelineSeed`):**

1. **Conservation** after every rollover and every day-91 lock: HS teams keep their generated role shape and cover the field; College teams stay inside 17/4/5 (org 26) at their 15/4/5 target; Pro holds 32 × 15/4/5 = 768 (±5% if a role runs dry, shortfalls reported).
2. **Flows each season:** exactly 128 legal, unique picks of College-rostered players; Signing Day's recruits = the independently counted openings; intake = HS departures; the per-tier population ledger balances (draftees cut on arrival are a reported cause).
3. **The journey:** an intake player traced HS → College → drafted → Pro, with the count by season 20.
4. **The gradient (Q7):** tier-mean raw role sums after each rollover, 5 burn-in seasons then a 15-season window: HS < College < Pro, strict, gaps ≥ batters +3.0 / +10.0 and pitchers +2.0 / +10.0; stationarity (window halves within 10.0). 13-6a recorded batters +6.19 / +18.14, pitchers +5.40 / +16.97, max drift 2.33.
5. **Reported (not gated):** Pro arrivals a season, the Little's-law career, the day-91 free-agent pool by role, and draftees released at the rollover they arrive.
6. **Hygiene and determinism:** no unflagged player signed; enrollment rows only for College-rostered players; FK clean; no open batch; a same-seed 20-season twin byte-identical.

**`--full` (1,193 days: three seasons and season 4 through day 98, the canonical world):** three composed rollovers (sims, lock, career, playoffs, development, pipeline) dispatch without throwing; an age-19 HS-senior avatar is recruited (2026), plays College (2027), declares as a top-64 sophomore and is drafted (2028), and plays his first attended Pro game by day 1188; real standings drive the 2027 Draft order and recruiting; after the third rollover a single-snapshot gradient holds (HS < College < Pro). It runs at a pipeline build and its review, not on every change.

**Determinism check for a build or review:** run the default 20× with `--no-build`, normalize each log (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`, `\d+\.\d s` → `N s`) and compare SHA-256s: 20/20 identical.
