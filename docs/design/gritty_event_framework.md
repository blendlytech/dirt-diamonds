# Design — Gritty Event Framework (Dispatcher, Condition Model & Content Schema)

**Author:** Claude Fable 5 (dispatcher skeleton + spec) · **Phase:** 7 (Gritty Event Framework) · **Status:** spec written alongside the implementation it describes; the seed content in `Assets/Narrative/Events/Content/core_events.json` doubles as the pipeline fixture set. Companion to the four existing design docs, same house style.

Per `.claude/rules/gritty_events.md`: the dispatcher **runs on a background polling loop checking the Entity_Flags and Players tables**; events are JSON with **prerequisites, a probability weight, and branching choices**; choices write **immediate consequences AND hidden flags** that trigger cascading events years later. This doc turns that mandate into a threading model, a condition algebra, a consequence vocabulary (the first rivalry/affinity/interest **writers**), and the content contract Sonnet 5 authors event batches against (validated per batch by `check_event_graph_integrity`).

The Phase-7 exit criterion this framework must prove end-to-end: *bribe → `compromised_syndicate` → syndicate event fires seasons later* (§7 check 6).

---

## 1. Architecture & threading

```
 poller thread (Narrative)                    main thread (bus pump in GameManager._Process)
┌────────────────────────────┐               ┌──────────────────────────────────────────────┐
│ EventDispatcher            │               │ EventConsequenceApplier                      │
│  · read-only WAL view      │ GrittyEvent-  │  · picks a branching choice (autopilot rng)  │
│  · poll current_day        │ FiredEvent    │  · resolves relationship targets             │
│  · on day change:          │ ──────────►   │  · one batch: funds/flags/interest writes    │
│    snapshot Players +      │  (EventBus,   │  · RelationshipGraph writes (in-memory,      │
│    active Entity_Flags,    │   thread-safe │    graph publishes RivalryChangedEvent)      │
│    evaluate + roll         │   publish)    │  · publishes StressImpulseEvent /            │
│                            │               │    FundsImpulseEvent / …ResolvedEvent        │
└────────────────────────────┘               └──────────────────────────────────────────────┘
```

- **The poller never writes; the applier never polls.** The dispatcher thread owns a **read-only companion connection** (`DatabaseManager.CreateReadOnlyView()` — DatabaseManager stays the sole class that opens connections; the view is a nested type it constructs). WAL, enabled since Phase 1 *for exactly this reader*, lets the poll run while a sim batch commits. All poll SQL lives in a typed query class (`NarrativePollQueries`, Data layer) per database_rules — Narrative constructs no SQL.
- **Cheap poll, day-gated evaluation.** The steady-state loop is a single prepared `current_day` read per interval (default 250 ms). Full snapshot + evaluation runs only when the day moves — the dispatcher reacts to *day advancement*, never to boot (the first observed day is recorded, not evaluated, so a reload never re-rolls a day already lived).
- **Catch-up on multi-day jumps.** A fast-forwarded save (autopilot seasons) may move the day by N between polls; the dispatcher evaluates each missed day in order against the **current** snapshot (per-day probability is preserved; prerequisites are approximated by present-state — disclosed, harmless at interactive pace where N=1).
- **Publication.** A fire publishes `GrittyEventFiredEvent{EventId, SubjectId, Day}` via `EventBus.Publish` (thread-safe by Phase-2 design, built for this exact caller). Handlers run on the main pump; the applier's DB writes are main-thread, own-batch — never the calendar tick's, never the poller's.
- **Consequence routing honors the standing contracts.** Stress and funds reach the Life sim **via the bus** (`StressImpulseEvent`/`FundsImpulseEvent` — life_sim_needs_decay.md §10: "a gritty event raising stress arrives via the EventBus, never a direct call"). Relationship writes are direct `RelationshipGraph` calls (the graph's own contract is *publish-only, never subscribes*; it remains the sole publisher of `RivalryChangedEvent`, so the Phase-6 transport chain into both baseball sims is reused untouched). Narrative references Core + Data + Simulation.Life — never Simulation.Baseball (the STRICT boundary is Life↔Baseball; Narrative is orchestration, like GameManager).

## 2. Event content schema (the Sonnet contract)

One JSON document per content batch: `{ "events": [ … ] }`, loaded by `GrittyEventJson.Parse` (loud, event-id-labelled errors on any unknown field/op/type — a malformed batch fails at boot, not silently at fire time). Shipped batches live in `Assets/Narrative/Events/Content/`.

```json
{
  "id": "back_alley_bribe",          // unique, snake_case, stable across edits
  "scope": "any",                    // "any" (every player is a subject) | "avatar"
  "weight": 0.10,                    // per-day fire probability once prerequisites hold, [0,1]
  "cooldown_days": 120,              // min days between fires for the SAME subject (0 = none)
  "prerequisites": [ … ],            // ALL must hold (AND); see §3
  "choices": [                       // ≥1; branching choices per gritty_events.md
    {
      "id": "accept",                // unique within the event
      "autopilot_weight": 3,         // relative pick weight until the choice UI exists (≥0)
      "consequences": [ … ]          // applied in order; see §4
    }
  ]
}
```

**Repeatability is content's job, not the engine's.** `cooldown_days` is in-memory pacing (resets on reload — deliberate; it is pacing, not state). An event that must fire **once ever** sets a flag in its consequences and requires that flag inactive in its prerequisites — that round-trips through Entity_Flags and survives saves. `check_event_graph_integrity` should flag any event whose every choice re-satisfies its own prerequisites (an unpaced loop).

## 3. Prerequisites (`ConditionEvaluator`)

A prerequisite is exactly one of:

| Form | JSON | Semantics |
|---|---|---|
| Field comparison | `{ "field": "funds", "op": "<", "value": 500 }` | Subject's Players-row field vs constant. Fields: `funds`, `age`, `recklessness`, `health_ceiling`, `detection_risk`, `baseball_interest`. Ops: `<  <=  >  >=  ==  !=`. |
| Flag active | `{ "flag_active": "compromised_syndicate", "min_days_since": 365 }` | Subject has the flag active; optional `min_days_since` (default 0) additionally requires `current_day − set_on_day ≥ N` — **this is the cascade mechanism** ("seasons later" = 365·k). |
| Flag inactive | `{ "flag_inactive": "compromised_syndicate" }` | Subject does not have the flag active. |

All prerequisites AND together (the rules doc's own example is a conjunction). OR is expressed as two events sharing consequences — keeps the evaluator branch-free and the integrity skill's reachability analysis tractable. Evaluation is zero-allocation: field reads off a struct snapshot row, flag probes into a per-day rebuilt `(player, flag) → set_on_day` map.

## 4. Consequences (the writers)

Applied in listed order. `amount` may be negative anywhere it appears.

| `type` | Payload | What it writes |
|---|---|---|
| `funds` | `amount` | Atomic SQL `funds = MAX(0, funds + Δ)` on Players (**new** `PlayerQueries.AdjustFunds`) + `FundsImpulseEvent` so the Life sim's in-memory mirror moves identically. |
| `stress` | `amount` | `StressImpulseEvent(subject, Δ)` — the §5 stress scalar's live source. Negative Δ = relief. |
| `interest` | `amount` | Atomic clamped `baseball_interest += Δ` (**new** `PlayerQueries.AdjustBaseballInterest`) — the heir-mechanics §4 re-weighting writer (`heir_mechanics.md` §11.2). |
| `set_flag` / `clear_flag` | `flag` | `Entity_Flags` upsert via the existing `PlayerQueries.SetFlag`, `set_on_day` = fire day (the cascade clock). |
| `relationship` | `kind`, `affinity`, `target` | If the (subject, target) pair has **no** edge: `RelationshipGraph.SetRelationship(kind, affinity)`. If an edge exists: `AdjustAffinity(affinity)` — existing kind preserved (a feud deepens; it doesn't reclassify a marriage). Targets: `teammate` \| `opponent` \| `league` (uniform random over the respective pool, resolved at apply time; consequence skipped if the pool is empty). A `Rival` edge pushed negative publishes `RivalryChangedEvent` through the untouched Phase-6 chain — **this is the organic rivalry writer**. |

Consequences the framework deliberately does *not* have yet: `ConceiveChild` (the §11.2 marriage/child life-event — needs partner-target semantics worth their own pass), roster/team mutations, and PED flag writes (Phase 8 hustle territory). The vocabulary is a closed enum — an unknown `type` is a load-time error, so adding one is an engine change, not a content change.

**Choice resolution.** Until the choice UI ships, every fire is autopilot-resolved: one weighted draw over `autopilot_weight` (all-zero → first choice). `GrittyEventResolvedEvent{EventId, SubjectId, ChoiceIndex, Day}` is published after application — the seam for the future event UI/feed (same `LastSuccession` precedent).

**Safety valves.** Per subject: at most **one** fired event per day (first satisfied definition in file order wins). Global: `MaxFiresPerDay = 8` per evaluated day — a content mistake (weight 1.0, scope any) degrades to a noisy day, not 136 events.

## 5. The stress scalar goes live (closes life_sim_needs_decay.md §4.2)

Per-NPC `stress ∈ [0,100]`, in-memory in `LifeSimManager` (persistence is a deferred additive schema pass, same §11 precedent as needs were):

- **Source:** `StressImpulseEvent` (bus), clamped accumulate.
- **Decay-side effect:** every hourly tick passes `S = 1 + (S_max − 1)·stress/100`, `S_max = 2.5` (`NeedsEngine.StressModifierFor`, the §4.2 mapping verbatim) into the existing `DecayHour` — at stress 0 this is bit-identical to every pre-Phase-7 trace.
- **Relaxation:** `StressRelaxationPerHour = 0.4` (≈9.6/day — a +25 event frays an NPC for ~2½ days; first-pass constant, `simulate_utility_decay`-tunable).
- **Action-side effect** (life_sim_ai.md: *"High stress can override queued actions, forcing … stress-relief actions regardless of temporal cost"*): the Utility crisis pass now also engages at `stress ≥ StressOverrideThreshold = 70` — same override (temporal weight zeroed, stress-relief bonus live), new trigger. Completing a stress-relief action (`DrinkAlone`/`PickArgument`) subtracts `StressReliefPerAction = 15`.

## 6. Probability & determinism

The dispatcher owns a thread-confined `RngState` (xoshiro, seeded at construction). Fire roll: `NextDouble() < weight`, at most once per (event, subject, day). In-game the seed is wall-clock (matching the league's contract — determinism is the harness's, not the game's); the harness seeds it and drives `EvaluateDay` synchronously for bit-reproducible fire schedules.

## 7. Acceptance checks (Tools/GrittyEventsHarness)

1. Loader round-trips the shipped seed batch; rejects bad op / unknown consequence / out-of-range weight loudly.
2. `ConditionEvaluator` truth table incl. the `min_days_since` boundary (day N ⊨, day N−1 ⊭).
3. Day-gating: construction evaluates nothing; a repeated same-day evaluation refires nothing.
4. weight 1.0 fires on day change; weight 0.0 never; per-subject 1/day + `MaxFiresPerDay` cap hold.
5. Writers, each proven in the DB / on the bus: funds floor-clamped; interest clamped; flag `set_on_day` = fire day; relationship consequence creates the edge **and** a subscriber observes `RivalryChangedEvent`.
6. **The exit-criteria cascade:** bribe fires → `compromised_syndicate` set → shakedown refuses to fire the next day → fires at +365 — end-to-end through dispatcher, bus, and applier against a scratch save.
7. Stress: impulse → accelerated decay (§9 fixture: Hunger 40, S=2.0 → 26.76 vs calm 33.38); relaxation; relief actions reduce it; stress 85 with full needs picks a stress-relief action (the override provably fires without any critical need).
8. Threaded end-to-end: real poller thread + WAL scratch file, day advanced by the real `TimeManager` on the main thread, fire observed through the real pump; clean `Stop()`.
9. Idle-poll allocation bounded (≤ 512 B/poll — ADO.NET allocates one internal reader + one boxed scalar per ExecuteScalar, ~464 B measured; the bound catches leaks, the mandate's true hot paths stay 0 B).
10. Same seed ⇒ identical fire sequence.

## 8. Implementation contract

- Poll SQL: typed, prepared, view-bound (`NarrativePollQueries`); the active-flag scan rides `idx_entity_flags_active_name` (v1 built it for this poll; re-verified via EXPLAIN this session).
- The dispatcher is engine-free (no Godot types) and harness-drivable without its thread (`EvaluateDay(day)` public, `Start`/`Stop` idempotent). GameManager owns its lifecycle and stops it **before** disposing the database.
- Content files are not Godot resources — like the `.sql`, `Content/*.json` must join the export include filters (Phase 9 note).
- Constants (`MaxFiresPerDay`, poll interval, stress knobs) are named statics — tuning is a data edit.

## 9. Model split & follow-ons

- **Fable 5 (this pass):** everything above + the harness.
- **Sonnet 5:** event content batches against §2–§4 (run `check_event_graph_integrity` per batch — that skill's checks should be built against §2's schema); the event UI consuming `GrittyEventFiredEvent`/`ResolvedEvent` when the choice surface ships; rivalry-at-bat feed flag (unchanged assignment).
- **Deferred:** stress persistence (additive schema pass), `ConceiveChild`/marriage consequence, per-event Environmental-Multiplier venues (§4.1 location composition), avatar choice UI (autopilot until then).
