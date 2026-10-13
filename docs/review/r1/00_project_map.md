# R-1 project map

Reviewed baseline: `2dec343`, 2026-10-10. Worktree `C:\Users\DELL\dnd-gpt-review`, branch `review/r1`, created from local main as explicitly requested. All findings refer to that baseline. This branch adds review documents, updates the progress log and makes the factual documentation corrections listed in 01; proposals are not accepted design and no runtime code is changed.

## Product and durable flow

A premium Windows/Linux Steam baseball career and life simulation: create a poor freshman, schedule daily life and baseball, progress through school/pro careers, experience relationships and risky money choices, and continue through willing heirs. The locked vision adds the hidden GOAT parent, adoption/search, Trailblazer, temptation/substances, offline broadcaster audio, motion comics and a full visual redesign.

Godot presents C# systems backed by a local SQLite v13 save. Core boot opens/migrates data, hydrates world/ledgers/managers and wires bus subscriptions. Clock/day advance persists calendar state and queues events. Baseball runs background leagues and parks the avatar's attended game; the micro-game resolves pitch/PA/runner outcomes and accumulates stats. Life runs needs, actions, relationships and person drift. Narrative evaluates predicates and applies chosen consequences; economy services commit hustle results. Data query classes supply typed rows and persist state. UI renders state and submits actions; Steam consumes milestones and publishes presence.

The distinction between committed data and pending work is crucial: calendar, background stat buffers, bus queue, pending narrative choice, relationship dirty sets and RNG streams are not one durable unit. R-1's main release risks lie at those joins (02 B01/C01/L01/N01).

## Subsystem map

| System / source | Shipped responsibilities | Planned change / principal dependency |
| --- | --- | --- |
| `Assets/Simulation/Baseball/` — 28 C# files, 9,731 lines | Normalized PA model, Markov micro-game, human batting reads, fatigue, six tiers, schedule, career/succession, genetics, development, gear/person/absence/rivalry projections | 13: three tiers, persisted schedule, real roster losses/forfeits, playoffs/draft; G: batting reband and pitching; L: identity and lineage |
| `Assets/Simulation/Life/` — 10 / 2,278 | Needs/utility/action scheduling, households, graph/history, person drift | Civilian/kin graph and new effect projections; preserve Life/Baseball boundary |
| `Assets/Simulation/Hustles/` + `Assets/Economy/` — 12 / 3,254 + 10 / 1,700 | Pure risk mini-games and money/inventory/session services | Rebalance economy against new calendar; durable result handling |
| `Assets/Narrative/` — 7 / 2,646 plus JSON | Validated closed consequence vocabulary, conditions/dispatcher, contacts and autonomy | Hidden children, legends, temptation, substance and Trailblazer arcs; durable choices |
| `Assets/Data/` — 12 / 5,139 | v13 schema, pooled commands, typed queries and rows | v14 clean break for 13, v15 identity/lineage; save recovery contract |
| `Assets/Core/` — 8 / 2,472 | Boot, orchestration, deferred bus, time/clock and flush bridges | Explicit quiescent saves, recoverable day processing; presentation-independent ownership |
| `Assets/UI/` — 27 / 9,052 | Dashboard, batting, planner, phone, hustle panels, tutorial and time controls | G pitching/practice/read feedback, 15 redesign, 14 booth, 16 cutscenes |
| `Assets/Platform/Steam/` — 3 / 635 | Guarded Steam wrapper, achievements, rich presence | Export/depot/cloud/offline eligibility validation in 17 |
| `Tools/` — seven harness projects, 16,931 C# lines | Content, MC, clock, needs, hustles, poker and scratch schema gates | New topology, pitch centering, persistence interruption and media validation gates |

Counts describe scoped C# source inventory, excluding generated build files. They are not a claim that each line received equal scrutiny.

## Boundaries to preserve

- Life and Baseball exchange Core events/primitive projections, not direct manager calls. Each owns its transaction; recovery must coordinate completion without merging unrelated transactions.
- Presentation consumes DTOs/events and submits intents. Clock-driving and direct Game_State writes in current UI are exceptions requiring reconciliation (02 C02), not a precedent for cutscenes to decide succession.
- Neutral resolution and human-input branches have distinct guards. O1 authorizes rebanding human skill; it does not authorize changing neutral baseball. Phase 13's topology-dependent season totals need separate sanctioned replacement.
- Cache tier/person/gear/absence/rivalry data outside per-PA hot paths. New effects use bounded projections with no-rows identity fixtures.
- Audio is generated offline; the shipped game uses packaged assets. Broadcast/cutscene playback must never hold sole ownership of story completion or game state.
- Current reference docs explain shipped behavior. Active design describes future changes; TEAM owns current assignments; archived author/status labels are historical.

## Built versus planned

Phases 0–12 are present; 13–17, G and L remain substantially future work. L-0 is a design checkpoint. v13's six tiers and MaturityAge 19 are real; three tiers and official-at-18 reclaimed children are planned and intentionally different concepts. Current partners are ballplayers, position is Hitter/Pitcher, and hidden adopted children are not yet created. Real voice production and presentation polish are not established by a headless build.

## Review coverage and limits

Read the binding directives, rules/skills, canonical vision/plan/roles/progress, all active design specs and the required briefs. Reviewed source/reference paths by subsystem, with deeper tracing of resolver/modifier math, save/event flow, relationships, narrative consequences, service transactions and UI/Steam boundaries. Reference material was consulted selectively around those contracts; archive history was not exhaustively read. This is a broad subsystem audit, not an exhaustive line-by-line or every-state proof of the corpus. Remaining empirical work is explicitly listed in 02 and 05.

Recomputed G-1 weights analytically; ran the build and seven harnesses against scratch data. No live save, Godot visual playthrough, exported platform smoke test, account action or paid generation. See 02 for exact results, including the MC first-run failure. No assertion of 'zero remaining conflicts' is justified: 01 is the decision register for owner/Opus triage.
