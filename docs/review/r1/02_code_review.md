# R-1 code review

Baseline `2dec343` (2026-10-10); code is unchanged. Findings below distinguish observed gate failures, source-proven defects, and tests still needed. This is a subsystem review, not a claim that every branch or platform was executed. Gameplay/G-2 findings G01–G05 are in `03_gameplay.md`.

## Verification

| Gate | Result |
| --- | --- |
| Debug solution build | PASS, 0 warnings / 0 errors |
| GrittyEvents Release | PASS 363/363 |
| MonteCarlo Release | FAIL 351/352 on first run; diagnostic follow-up pending |
| CoreLoop Release | PASS 22/22 |
| NeedsDecay Release | PASS 117/117 |
| Hustle Release | PASS 95/95 |
| Holdem Release | PASS 63/63 |
| SchemaValidator Release, scratch DB | PASS 111/111 |

The brief's MonteCarlo expectation of 345 is stale; the reviewed baseline has 352 checks. Passing checks were run once. The failing MC run's initial output filter retained the total but omitted the failing name; a diagnostic repeat is being recorded separately. No live save was opened or modified. No Godot visual playthrough, Linux export, Steam login/cloud round-trip, real voice generation or external submission was performed; headless passes do not prove those.

## Baseball and career

### B01 — high: clean saves can lose background game statistics

Evidence: `Assets/Simulation/Baseball/LeagueSimulator.cs:414-431` accumulates games in memory and flushes only on the seven-day cycle; `:448-453` supplies `FlushPending`. `Assets/Core/TimeManager.cs:74-77` has already committed the advanced calendar. Neither `GameManager.SaveNow` (`Assets/Core/GameManager.cs:1043-1062`) nor `_ExitTree` (`:643-680`) flushes league simulators; their flushes cover life, relationships, clock and WAL only. Whole-Assets search finds FlushPending callers in career creation/succession, development and promotion, not save/exit.

Reproduction from the control flow: play a non-cycle day with background games, Save or quit, reopen. The date survives, the in-memory counting stats do not, and boot starts after those games. A WAL checkpoint cannot persist C# arrays. This can also leave attended and background ledgers inconsistent. Fix in a separately briefed core/save slice: flush all leagues at a quiescent save boundary (then normalize as needed), before checkpointing; ensure repeated save is idempotent. Required test: save/reopen on each day of a cycle, compare every team/player ledger against uninterrupted play.

### B02 — high: random career IDs undermine deterministic harness fixtures (known 13-0)

Verified `Guid.NewGuid()` at `CareerManager.cs:439,535-536,668`, with roster ordering by team/player ID at `Assets/Data/Database/BaseballQueries.cs:110,125`. Thus fixed RngState seeds do not fully determine career roster order or which player receives subsequent draws. The MC gate did fail at 351/352 in this review; the count alone does not prove which assertion failed. The reported counter-reload bug in `docs/tasks/13-0_review_careermanager_ids.md:11-25` concerns an in-flight diff absent from this baseline; do not report that counter as shipped code. Follow the assigned 13-0 fix and reload/collision fixtures, then its 20-run acceptance gate. R-1 does not replace that work.

### B03 — high: fatigue improves below-average effective pitchers

Source-proven by `PitcherFatigue.cs:98-100`: at multiplier .5, stuff/control 30 becomes 40; 50 remains 50. HS/College tier penalties make this a normal case (`TierEffects.cs:52-53`). The formula is also explicitly in the shipped contract; this is a design defect needing a sanctioned Math decision, not an accidental typo. See G02 for its direct contradiction with the upcoming G-2 gate. Neutral guards must not be silently re-baselined.

### B04 — medium: save/reload is not a deterministic continuation contract

`GameManager.cs:265-268` explicitly reseeds from TickCount64 on boot and says in-game replay determinism is not required. Career, micro and macro RNG state is not a persisted save contract. Distinguish fixed-seed harness reproducibility from save replay. Recommendation: decide before 1.0 whether reload may reroll future outcomes; if not, persist separately named streams plus pending work. Test cold-reload parity; do not infer it from same-process same-seed tests.

Existing strengths: normalized shared resolver, fixed arrays/stack buffers, independent neutral/read branches, explicit tier/person/gear caches, and warm allocation checks. Existing known gaps are planned: six-tier topology, replacement call-ups, no pitching UI, PED flag with no live writer, and rostered-heir handling. They are not unexpected regressions against v13.

## Core and persistence

### C01 — high: day persistence can get ahead of unprocessed events

`TimeManager.cs:74-80` commits current_day before publishing subsystem work; `EventBus.cs:189-199` dequeues an event and invokes handlers sequentially without a completion journal. A process interruption or exception after the date commit but before all handlers finish leaves an advanced date and partial/unapplied work, with no durable event to replay. This is distinct from B01's clean-exit loss. Independent Life/Baseball transactions are intentional, so a giant shared transaction would violate the current boundary. Recommended: persisted day-work journal with idempotent subsystem completion, or another explicit recovery protocol, tested by interruption after each commit. Treat as a 1.0 save-integrity gate.

### C02 — medium: the shipped UI is a clock driver despite the stated boundary

`Assets/UI/TimeControlBar.cs:176-184,312-322` calls the clock/day advance and changes `Career.AutopilotAttendedGames`. `Assets/UI/TutorialOverlay.cs:299` and `Assets/UI/Main.cs:161` write Game_State directly. This contradicts `GEMINI.md:15` and `.agents/rules/ui_conventions.md:17` (DTOs and intents only). The clock reference explicitly assigns that driver to the UI, so the repo has a contract conflict as well as an implementation boundary violation. Move ownership to Core and expose intents/DTOs during the planned screen work; do not claim a screen replacement is necessarily simulation-inert while the driver lives in it.

### C03 — medium: worker/database exclusivity is a convention, not enforced end to end

`Assets/UI/BaseballDashboard.cs:281-287` runs PlayPendingGame on a worker, including its DB flush. `DatabaseManager.cs:202-250` locks command acquisition/execution, but returns shared mutable pooled commands and readers outside those lock lifetimes. `GameManager.SaveNow:1043-1054` checks active batch but not InteractiveGameInFlight. The UI still has read/query surfaces. This is a concurrency risk, not a reproduced corruption: method locks do not make parameter assignment, reader consumption, and whole transactions mutually exclusive. Required test: save/read/close while an interactive game's final flush starts. Prefer a single DB owner or explicit quiescence gate; parameter binding plus execution must be protected together.

More subsystem results follow as the review progresses.
