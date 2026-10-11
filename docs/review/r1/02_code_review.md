# R-1 code review

Baseline `2dec343` (2026-10-10); code is unchanged. Findings below distinguish observed gate failures, source-proven defects, and tests still needed. This is a subsystem review, not a claim that every branch or platform was executed. Gameplay/G-2 findings G01–G05 are in `03_gameplay.md`.

## Verification

| Gate | Result |
| --- | --- |
| Debug solution build | PASS, 0 warnings / 0 errors |
| GrittyEvents Release | PASS 363/363 |
| MonteCarlo Release | FAIL 351/352 first run; diagnostic repeat PASS 352/352 |
| CoreLoop Release | PASS 22/22 |
| NeedsDecay Release | PASS 117/117 |
| Hustle Release | PASS 95/95 |
| Holdem Release | PASS 63/63 |
| SchemaValidator Release, scratch DB | PASS 111/111 |

The brief's MonteCarlo expectation of 345 is stale; the reviewed baseline has 352 checks. Passing checks were run once. The failing MC run's initial output filter retained the total but omitted the failing name; the diagnostic repeat passed 352/352; see the final section. No live save was opened or modified. No Godot visual playthrough, Linux export, Steam login/cloud round-trip, real voice generation or external submission was performed; headless passes do not prove those.

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



## Life and relationships

### L01 — high: failed relationship flush discards the retry set

`Assets/Core/GameManager.cs:1355-1382` collects dirty edges/history before opening its batch. `Assets/Simulation/Life/RelationshipGraph.cs:227-234,308-317` clears both dirty sets during collection. A failed begin/write/commit rolls back SQLite but never restores those sets. The in-memory graph remains changed; subsequent save has nothing to retry unless the pair changes again. Use snapshot-and-acknowledge-after-commit, with an injected write failure followed by a successful retry/reload test. Include history, not just current edges.

Needs/utility math has explicit bounds and dedicated neglect/game-duration gates (117/117). Person drift and household scheduling are deliberately separate from baseball; field effects require the accepted bus/projection route. No new decay constant is proposed. Missing coverage: cold reload mid-action, time jumps, and simultaneous arrest/schedule changes; a headless decay pass does not validate the hour-by-hour UI.

## Narrative and content

### N01 — medium: reload discards pending choices and cooldown history

`Assets/Narrative/Events/EventDispatcher.cs:56-60,131-152,200-223` keeps cooldowns in memory and initializes its first poll without replaying the current day. `EventConsequenceApplier.cs:111-112,146-160` keeps the pending choice in memory and clears it before ApplyChoice succeeds. Save/reopen can erase a presented choice; failed apply also loses the pending handle. Cooldown reset is explicitly disclosed by `docs/reference/marriage_and_conception.md:76`, so it is a shipped limitation, not a newly broken persistence promise. Before 1.0 persist the choice/cooldown state, acknowledge after commit, and test reopen while a choice is visible. Do not assume flags protect every cooldown-only event.

### N02 — high: a marriage can leave a friend/rival edge and a married flag

`EventConsequenceApplier.cs:538-565` only adjusts affinity when any edge already exists; it does not assign the requested Partner kind. `Assets/Narrative/Events/Content/marriage_and_family_events.json:23` requests Partner and the choice sets married. The target selector (`EventConsequenceApplier.cs:701-723`) accepts teammates with existing edges. A selected friend/ex/rival therefore stays that kind while the flag says married; conception subsequently cannot find that Partner. The same selector does not exclude a target who already has another Partner, so subject-only exclusivity does not guarantee one partner per person. Introduce a partner-specific eligibility/reclassification operation, rather than changing the semantics of every affinity event. Test existing friend, ex, already-partnered target, empty eligible pool, and flag/edge consistency.

The 363 content checks pass; that does not prove every stateful arc across saves or preexisting relationships. Contact/social autonomy must keep relationship history and kin eligibility consistent when L adds civilians and reclaimed relatives.

## Hustles and economy

### E01 — medium: one resolution crosses a durable boundary without a recovery record

`Assets/Economy/HustleService.cs:188-211,230-299` commits the core money/risk result and subsequently applies faction relationship/trust/turf effects. Interruption or a later write failure can preserve money while losing the rest. The session-level single-net-resolution promise in `docs/reference/hustle_minigames_depth_pass.md:31-33` is not an atomic guarantee for all its effects. Add a durable resolution ID and completion/retry semantics; preserve the rule against sharing Life/Baseball transactions. Test interruption between core commit and faction effects. Pure resolver tests (Hustle 95/95, Holdem 63/63) do not cover this service/persistence boundary.

Pure mini-games are appropriately isolated from UI and SQLite. Existing mean/tail-risk checks should remain separate: an acceptable average can hide an excessive money-printing strategy or a catastrophic tail. Recheck economy progression after the new season lengths change the number of earning days; do not transplant old per-season balances unchanged.

## Data, performance and UI

SchemaValidator passed 111/111 against scratch data, not the owner's save. v13 constraints and prepared queries are present; v14 is an approved clean break, not a promised migration of old careers. C01/C03/L01 are the principal persistence findings. Add foreign-key/integrity checks and save/reopen parity to the future v14 world-generation gate. A clean schema alone cannot prove recovery from interrupted gameplay.

The hot baseball path uses cached modifiers and fixed arrays; existing warm allocation checks passed as part of MC's successful repeat. This review did not profile Godot frame times, UI allocations, large social graphs, export startup or an end-to-end maximum-size world. The new 68-team topology and pitch books require new scale measurements; do not apply a zero-allocation requirement to loading/authoring paths as if they were per-pitch loops. Sorting, query hydration and cache rebuild work should stay at defined day/roster/version boundaries.

UI review focused on time ownership, interactive workers, save/exit, and presentation seams (C02/C03). No visual, controller, keyboard-only, subtitle or accessibility acceptance was performed. The visual redesign must preserve simulation ownership and input/soft-pause recovery when screens are dismissed, replaced or interrupted. Add these to 15's acceptance criteria rather than treating a successful C# build as screen verification.

## Steam and release

### S01 — medium: events earned with Steam unavailable are not replayed at boot

`Assets/Platform/Steam/SteamIntegration.cs:89-96` no-ops unlocks when unavailable. `AchievementManager.cs:136-145` boot sync reevaluates tier/milestones, whereas absence/narrative achievements are triggered from events (`:175-194`). Server-only Journeyman accumulation likewise cannot count an offline season without a later durable source. Distinguish Steam-client-offline behavior from Steam entirely unavailable: this finding concerns the latter. Reconcile eligibility from saved facts or a durable pending record and test later availability; do not award duplicate increments. Whether unavailable-client play earns achievements is an owner policy decision, not a reason to break offline gameplay.

Build success does not establish native Steam library packaging, Linux execution, cloud conflict handling, depot contents, commercial audio rights or store acceptance. Phase 17 must verify these on exported Windows/Linux builds with owned test accounts. No real account/service action was taken in R-1.

## Known-issue disposition and harness limits

Confirmed against the baseline: random career IDs; six tiers; neutral pitcher policy/no pitching UI; weak discipline/control OPS weights; needs/stress absent on-field; MaturityAge 19; league-player spouse pool with no stored gender; adoption choice without creating its hidden child; and no live gameplay writer for ped_active. Some content narrows the spouse selector to teammate; this is still ballplayers, not a civilian pool. These planned gaps belong to G/13/L, not surprise regressions.

The reported zero-byte live save was not rechecked: no live save was opened or changed, and its current condition is unknown. Historical reference authors/status are not current assignments. The in-flight 13-0 counter fix was not in the baseline and was not reviewed here.

Monte Carlo's diagnostic repeat passed **352/352**. Initial output filtering lost the failing assertion name; the first **351/352** result is real but cannot be attributed conclusively. The random-ID source risk remains independently verified. A0 diagnostics: Perfect OPS 1.898, Random .439, Fooled .101. They support the need for G-8, not a new neutral baseline. Only the failed gate was repeated; all other passing gates ran once.
