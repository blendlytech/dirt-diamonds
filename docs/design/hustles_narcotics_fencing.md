# Design — Hustles: Narcotics State Machine & Fencing Negotiation

**Author:** Claude Opus 4.8 (risk/reward math + state machine) · **Phase:** 8b (per `docs/phase_8_9_interleave_plan.md`) · **Status:** design only — no C# in this pass. Sonnet 5 implements against this contract; Fable 5 reviews (the risk stats this writes feed the PED-style modifier layer and 8c's triad). Companion to `gritty_event_framework.md` (shares its consequence vocabulary and the same rules doc, `.claude/rules/gritty_events.md`).

Per `.claude/rules/gritty_events.md`: *"Hustles operate as isolated interactive nodes. Narcotics: 3-tier system (Inventory Drop → Profit/Toxicity Cut → Territory Control vs Factions)."* This doc turns that one line into (a) a deterministic risk/reward resolver in the project's established pure-math shape, (b) a negotiation resolver for Fencing (structure was left "TBD by Opus's design" in the interleave plan), (c) the daily-clock integration seam, and (d) the **accumulated-risk-state contract** 8c's arrest/injury/suspension triad will consume.

The guiding constraint, from the game's own thesis: hustles must fund a broke rookie's survival, but every dollar of upside must buy a slice of tail risk that threatens the **baseball** career — heat toward a suspension, health loss toward an injury, an outstanding beef that becomes an on-field rivalry. A hustle that is a clean money printer breaks the game; a hustle whose expected value is negative never gets played. The numbers below are tuned to a *positive but lumpy* EV with career-threatening tails that scale with aggression.

---

## 1. Architecture — three layers, wall-clean

The same separation the sim engines already use (pure resolver ↔ orchestration ↔ UI), so the math is harness-provable in isolation and the UI stays a read-only renderer.

```
 Layer 1 — Pure resolvers (Assets/Simulation/Hustles/, engine-free, Data-free, Baseball-free)
 ┌──────────────────────────────────────────────────────────────────────┐
 │ NarcoticsHustle   · 3-stage state machine, decisions × RngState        │
 │ FencingNegotiation· alternating-offer resolver, decisions × RngState   │
 │  → both are PURE: (HustleContext snapshot, player decision, ref RngState)│
 │    → HustleResolution (a bundle of deltas). No DB, no bus, no graph.    │
 └──────────────────────────────────────────────────────────────────────┘
            │ HustleContext (in)                    │ HustleResolution (out)
 Layer 2 — Orchestration/application (main thread; Data + bus + RelationshipGraph)
 ┌──────────────────────────────────────────────────────────────────────┐
 │ HustleService: builds the context snapshot (Players row + faction      │
 │  edges), drives the resolver with the UI's decisions, applies the      │
 │  resolution through the SAME writers gritty events use (§5).           │
 └──────────────────────────────────────────────────────────────────────┘
            │ DTOs up                               │ intent signals down
 Layer 3 — UI (Assets/UI/, Sonnet, godot_scene_mapper FIRST)
 ┌──────────────────────────────────────────────────────────────────────┐
 │ NarcoticsHustleScreen.tscn/.cs, FencingScreen.tscn/.cs — isolated      │
 │  interactive nodes; render DTOs, emit decision signals, never write DB.│
 └──────────────────────────────────────────────────────────────────────┘
```

**Why a pure resolver that takes standings as *values*, not a `RelationshipGraph` reference.** The resolver stays a pure function of `(context, decision, rng)`, so the harness drives it with hand-built fixtures and asserts exact EVs with no graph/DB setup — exactly how `MonteCarloHarness` drives `AtBatResolver` and `HeirGenetics`. Faction standing enters as a plain `int` per faction and leaves as a plain delta; Layer 2 is the only thing that touches the actual `RelationshipGraph`.

**Wall compliance.** `Assets/Simulation/Hustles/` references only its own math plus `RngState` (folder-homed in Baseball but pure math — the same established exception the Narrative dispatcher already relies on; the Life↔Baseball wall does not cover pure PRNG math). It never references `Simulation.Baseball` game loops or `Data`. Layer 2 (`HustleService`) is orchestration like `GameManager`/`EventConsequenceApplier` — it may span Data + Life + bus.

**No schema change.** Every piece of state a hustle reads or writes already exists: `Players.{funds, detection_risk, health_ceiling, recklessness}`, `Entity_Flags`, `Relationships` (via `RelationshipGraph`), and additive `Game_State` keys for territory bookkeeping. This holds the project's standing discipline — a new mechanic that needs no migration doesn't invent one.

---

## 2. The daily-clock integration (the Work block)

The interleave plan builds every Phase 8 hustle "directly as a real Work block on that clock." 8a made **Legal Work** the *passive* in-tick Work payout. Narcotics and Fencing are *interactive* — a player steps through decisions — so they cannot resolve synchronously inside `LifeSimManager.OnDayAdvanced` (that runs to completion on one pump). They follow the **attended-Game precedent** instead: the Work block reserves the hours and applies the shared work-exertion needs cost inside the tick, while the reward/risk resolution is an interactive session the UI coordinates, decoupled from the tick.

- The avatar has a **selected Work activity** for the planned day: `LegalWork | Narcotics | Fencing` (one-shot, set alongside the schedule; default `LegalWork`). This is a `GameManager`-owned intent — `GameManager` already owns the cross-wall avatar state and the scheduling bridge.
- **Legal Work** (unchanged): resolves passively in the day-tick's Work block (8a's `ActionCatalog.LegalWork` — income + Sleep/Fitness drain + meal access).
- **Narcotics/Fencing**: the day-tick's Work block ticks under a new **`ActionCatalog.HustleWork`** definition — the *same* Sleep/Fitness drain + meal access as Legal Work but **`FinancialCost = 0`** (the money is the interactive session's job, not the tick's). Because `WorkHours > 0` with an interactive activity selected, `GameManager` **arms a pending hustle session** for that day (mirroring how the Game block arms `CareerManager`'s pending attended game).
- The player opens the Hustle screen and resolves the session; its `HustleResolution` applies on completion (§5). If the day advances with the session unplayed, it **forfeits to the no-deal default**: the safe branch — no buy-in, no sale, zero reward, zero added risk — mirroring the pending-game forfeit-to-autopilot rule. Forfeiting a hustle can never fail live-state re-validation (unlike `CareerManager.Succeed`), so the forfeit is a plain "you didn't do the deal."

**Considered and rejected: decoupled ad-hoc launch** (open a hustle any time from a menu, self-abstracting its own time cost). Rejected because it desyncs from the daily clock the whole interleave plan is built around and duplicates the needs/time accounting the Work block already does. Tying the hustle to the Work block keeps **one** source of "the avatar worked today," and the Game block already proves an interactive session can coexist with a needs-side-inert clock block.

**Once per day.** A hustle session is available only on a day whose Work block ran an interactive activity; the pending-session slot is single-valued, so the avatar runs at most one hustle per day by construction (no separate guard needed).

---

## 3. Narcotics — the 3-tier state machine

A single **run** walks three sequential stages. Between stages the player may **bank & exit** (take current profit, cap exposure) or **push on** (higher EV, higher variance) — that is the interactive tension. A failed bust check can end the run early with consequences.

```
        ┌─────────┐  buy-in     ┌──────────────┐  cut      ┌───────────────────┐  push
 Idle ─▶│Inventory│────────────▶│ Profit/      │──────────▶│ Territory Control  │──────▶ Resolved
        │  Drop   │             │ Toxicity Cut │           │   vs Factions      │       (payout)
        └────┬────┘             └──────┬───────┘           └─────────┬─────────┘
             │ seizure roll fails      │ (bank & exit any stage →         │ conflict roll
             ▼                         │  Resolved with profit so far)    ▼
          Busted ◀──────────────────────────────────────────────── Retaliation
        (lose buy-in, +heat)                                    (lose, +injury, +beef)
```

States: `Idle · InventoryDrop · ProfitToxicityCut · TerritoryControl · Resolved · Busted`. The resolver exposes one method per stage transition, each taking the current `HustleState` (a struct carrying accumulated inventory/cash/toxicity/deltas), the player's decision for that stage, and `ref RngState`, and returning the next `HustleState`. `Resolved`/`Busted` are absorbing; `HustleState.ToResolution()` projects the accumulated deltas into the final `HustleResolution` Layer 2 applies.

### 3.0 Context & shared constants

Snapshot inputs (`HustleContext`, read once at run start):

| Symbol | Source | Meaning |
|---|---|---|
| `funds` | `Players.funds` | available capital |
| `heat` | `detection_risk / 100 ∈ [0,1]` | police attention |
| `reck` | `recklessness / 100 ∈ [0,1]` | aggression / nerve proxy (no charisma stat exists) |
| `supplierTrust ∈ [-100,100]` | `RelationshipGraph` edge to the supplier rep (§6) | connect quality |
| `crewStanding_f ∈ [-100,100]` | edge to turf-faction rep `f` | per-crew hostility |
| `ownsTurf_f` | `Game_State` flag `controls_turf_f` | territory already held |

Named constants (all tunable data, no design-doc anchor — first-pass, `simulate_utility_decay`/`HustleHarness`-calibrated like every other table in this codebase):

```
UnitCost      = 10      // $/pure unit at buy
StreetPrice   = 14      // $/unit at c = 1.0 (thin pure margin; cutting is where money is made)
BuyInMin      = 100
BuyInMaxBase  = 1000    // scaled down by heat, up by supplier trust (§3.1)
CutMax        = 2.5     // maximum step-on factor
```

### 3.1 Stage 1 — Inventory Drop

The player commits a buy-in `B` from `funds`, bounded by an offer the supplier is willing to front:

```
BuyInMax = BuyInMaxBase · (0.5 + 0.5·(supplierTrust + 100)/200) · (1 − 0.4·heat)
B ∈ [BuyInMin, min(funds, BuyInMax)]          // player's decision
```

Trust unlocks bigger drops (0.5×…1.0×); heat shrinks them (a watched dealer gets fronted less). `B` is **debited immediately** — capital is at risk from this point. The drop is then rolled for seizure:

```
pSeize = clamp(0.05 + 0.12·heat − 0.05·max(0, supplierTrust)/100,  0.02, 0.60)
```

- **Seized** (`Busted`): lose `B`; `Δdetection = +6`; set flag `narc_watchlist` (day-stamped); `Δreck = +2`; `Δstress = +15`. Run ends.
- **Clean**: `inventory = B / UnitCost` pure units. → `ProfitToxicityCut` (or bank & exit — but exiting here just returns the product unsold for a token `spoiled_goods`; a rational player always at least sells).

### 3.2 Stage 2 — Profit / Toxicity Cut

The player picks a step-on factor `c ∈ [1.0, CutMax]`. Cutting multiplies sellable units but discounts the price and raises toxicity:

```
toxicity  = (c − 1) / (CutMax − 1) ∈ [0, 1]
sellUnits = inventory · c
effPrice  = StreetPrice · (1 − 0.25·toxicity)      // stepped-on product sells cheaper
```

Note the built-in tension: revenue `≈ inventory·c·(1 − 0.25·toxicity)` is **concave** in `c` (volume up, price down), so there is a genuine optimum, not "always max." Bad product then risks a roll:

```
pBadBatch = 0.30 · toxicity
```

- **Bad batch**: only `0.40 · sellUnits` are salable (refunds/refusals); `Δdetection = +round(10·toxicity)`; local crew standing `Δcrew = −round(15·toxicity)` (poison on the block earns enemies); set flag `bad_product`. If the avatar is a user (flag `uses_product`, set only by a recklessness-gated content event — not by this hustle), additionally `Δhealth = −round(6·toxicity)`.
- **Clean batch**: full `sellUnits` salable.

Baseline "doing business" cost regardless of batch: `Δdetection += round(2 + 4·toxicity)`, `Δstress += round(5 + 10·toxicity)`.

→ `TerritoryControl` (or **bank & exit**: sell at Hold-level demand now, skip the conflict gamble).

### 3.3 Stage 3 — Territory Control vs Factions

The player picks a push level `t`, which sets both the market it can sell into and the conflict odds:

| `t` | demand `MarketCap` (units) | revenue mult `m` | `pConflict` |
|---|---|---|---|
| `Hold` | 60 | 1.00 | 0.00 |
| `Encroach` | 120 | 1.30 | 0.20 |
| `TakeOver` | 200 | 1.70 | 0.45 |

**Demand saturation** is what bounds the upside and makes over-buying/over-cutting wasteful: units up to `MarketCap` sell at `effPrice`; the excess fire-sales at `0.5·effPrice`. So revenue before conflict is:

```
soldFull = min(salableUnits, MarketCap)
soldFire = salableUnits − soldFull
grossRevenue = (soldFull · effPrice + soldFire · 0.5·effPrice) · m
```

If `pConflict` rolls (only for `Encroach`/`TakeOver`), resolve the turf fight — nerve vs. how hostile the crew already is:

```
hostility = max(0, −crewStanding_local) / 100
pWin      = clamp(0.45 + 0.25·reck − 0.20·hostility,  0.10, 0.85)
```

- **Win**: keep `grossRevenue` at the chosen `m`; set `controls_turf_local` (`Game_State`, durable — the hook for later passive turf income, §8); `ΔsupplierTrust = +10`; `Δreck = +2`.
- **Lose (retaliation)**: revenue collapses to Hold economics (`m→1.0`, `MarketCap→60`); extra `Δfunds = −0.5·B` (medical/protection); `Δhealth = −round(8 + 6·reck)`; `Δdetection = +8`; `Δstress = +25`; and the crew rep edge is pushed to a **deep Rival**: `crewStanding_local ← min(−40, crewStanding_local − 40)`. A Rival edge in the red is exactly what the Phase-6 transport carries into the baseball sims — **if the crew rep is a rostered player, a turf war becomes an on-field rivalry, for free** (§6).

No conflict (or `t = Hold`): `grossRevenue` stands. Either way → `Resolved`.

### 3.4 Worked EVs (the calibration targets `HustleHarness` asserts)

Three canonical policies, at a mid-career avatar (`heat = 0.20`, `reck = 0.5`, `supplierTrust = 20`, neutral local crew):

| Policy | `B` | `c` | `t` | ≈ EV net funds | catastrophic-loss tail |
|---|---|---|---|---|---|
| **Safe** | 300 | 1.0 (pure) | Hold | **+$150 ± small** | ~7% seizure (−$300); no injury/beef |
| **Moderate** | 300 | 1.6 | Hold | **+$275** | ~7% seizure; ~12% small loss (bad batch) |
| **Aggressive** | 1000 | 2.5 | TakeOver | **+$500** | ~8% seizure (−$1000); ~18% net retaliation (−$800 + `Δhealth ≈ −11` + a Rival + heavy heat) |

The shape to preserve, not the exact figures: **EV rises with aggression, but the aggressive tail converts money risk into career risk** (health→8c injury, detection→8c suspension/arrest, a Rival→on-field). `HustleHarness` proves EV *bands* and *bust-rate bands* over N seeded runs per policy (the `run_monte_carlo_batch` precedent: assert a range, not a point), plus **monotonicity** (mean and tail-risk both non-decreasing across Safe→Moderate→Aggressive) and **saturation** (an over-cut/over-bought run at `Hold` leaves salable units stranded in fire-sale — proving demand actually binds).

---

## 4. Fencing — the negotiation mechanic

Selling hot merchandise to a fence: an information-and-nerve game, not twitch. A **lot** carries a hidden true value `V` and a hidden fence **reservation** `R` (the most they will pay); the player never sees either, only the fence's live offer. Each round the player weighs the sure offer on the table against a counter that might extract more — but risks the fence walking, and each extra round raises sting exposure.

### 4.1 Setup

```
V         ~ Uniform[VMin, VMax]                 // VMin=200, VMax=800 (a lot's fenced worth)
rMult     = clamp(baseR + 0.10·(fenceStanding/100),  0.40, 0.85)   // baseR ~ Uniform[0.45,0.75]
R         = V · rMult                            // hidden reservation
patience  = PatienceBase = 4                     // fence walks after this many counters
o₁        = R · OpenFrac,  OpenFrac = 0.55       // fence's opening lowball
pSting    = clamp(0.04 + 0.10·heat − 0.06·max(0,fenceStanding)/100,  0.01, 0.40)  // is this fence wired?
```

Better standing with the fence lifts `R` (they trust you, pay closer to value), lowers `pSting`, and can grant `+1 patience`. All hidden values are drawn once per lot from `RngState`.

### 4.2 Rounds

Each round the player chooses **Accept** (take the offer on the table) or **Counter q** (name a price):

```
if q ≤ fenceCeil:   deal at q            // fence's live willingness, ramping toward R
else:               fence raises its offer toward R and burns a round
    o_{i+1} = o_i + ClimbFrac·(R − o_i),   ClimbFrac = 0.5
    patience −= 1
if patience == 0 and no deal:  fence WALKS
```

`fenceCeil` starts below `R` and climbs to `R` as patience burns (a fence with one round left will meet a reasonable ask; early on it holds firm). The **pot-odds decision** each round: accept the sure `o_i` now, or gamble a counter for the gap to `R` against the walk/sting cost of another round.

### 4.3 Outcomes

- **Deal at price `d`**: `Δfunds = +d`. Roll `pSting`: on hit, `Δdetection = +10`, flag `narc_watchlist`, and a portion of `d` is forfeited as marked bills (`d → 0.5·d`) — the fence was an informant.
- **Walk**: lot unsold; flag `spoiled_goods`; `Δstress = +8`; no funds.

**Neutral autopilot policy** (the headless harness stand-in, `InteractiveBatterPolicy`'s precedent): accept once `o_i ≥ AcceptFrac · anchor` where `anchor` is the fence's first offer scaled by a fixed factor — a simple, seed-deterministic threshold so the harness has a stable baseline to assert against. Player "skill" is expressed through *standing* (better `R`, lower sting, more patience), never twitch. Calibration target: neutral-policy fencing EVs a **steady small positive** (safer than Narcotics, richer than Legal Work), low sting rate; an aggressive-counter policy has a higher mean-when-it-lands but more walks (zero outcomes) and more sting exposure — `HustleHarness` asserts both bands and that walk-rate rises with counter-aggression.

---

## 5. Applying a resolution — reuse the consequence writers

A `HustleResolution` is a bundle of the *same primitives* gritty events already apply, plus two the framework deliberately left out. The clean path (recommended) is to **factor the consequence-application core out of `EventConsequenceApplier` into a shared `ConsequenceApplier`** that both the gritty-event flow and `HustleService` call, and **extend the closed consequence enum** with the two risk writers 8c needs anyway:

| Resolution delta | Writer (all single-writer-clean, already proven) |
|---|---|
| funds | `PlayerQueries.AdjustFunds` (atomic floor-clamped) + `FundsImpulseEvent` (Life mirror) |
| stress | `StressImpulseEvent` on the bus |
| **detection_risk** | **new** `PlayerQueries.AdjustDetectionRisk` — atomic `MIN(100, MAX(0, …))`; generalizes `BaseballQueries.ApplyPedGameCosts`' half |
| **health_ceiling** | **new** `PlayerQueries.AdjustHealthCeiling` — atomic `MAX(0, …)`; the other half of the PED-cost pattern |
| recklessness | reuse/extend the same atomic clamped-in-SQL pattern (`AdjustRecklessness`) |
| set/clear flag | `PlayerQueries.SetFlag` (`set_on_day` = the run's day — the cascade clock 8c and gritty events both read) |
| faction affinity | `RelationshipGraph.SetRelationship` / `AdjustAffinity` (Rival edges ride the untouched Phase-6 rivalry transport) |

Application discipline is `EventConsequenceApplier`'s exactly: **DB writes in the service's own batch** (never the calendar tick's, never a sim's), *then* the bus impulses and the in-memory `RelationshipGraph` writes, so any subscriber reacting to an impulse always observes the DB state the same run produced. Extending the enum is an engine change (an unknown consequence type is a load-time error), but it is the change 8c must make regardless — doing it here means 8c inherits a tested vocabulary. If the shared-core refactor is judged too invasive for one session, the fallback is a parallel `HustleService` apply-loop over the same writers; Fable's review call.

---

## 6. Factions — minimal, on the existing player-id substrate

Factions are just **affinity edges to a small fixed set of faction-rep NPCs**, so everything rides `RelationshipGraph` and the rivalry transport with zero new concepts:

- One **supplier** rep and a few **turf-crew** reps, chosen from existing non-avatar life-sim NPCs at the first hustle and tagged with flags (`faction_supplier`, `faction_crew_1`, …). No new `Players` rows — the population already exists (817 NPCs).
- `supplierTrust` / `crewStanding_f` are the affinities on those edges; Territory Control and bad batches move them.
- **The baseball tail falls out for free where it naturally occurs:** the Phase-6 transport only carries *rostered* players' rivalries into the sims, so a crew rep who happens to be a rostered player becomes an on-field rival when a turf war pushes the edge deep-negative; a civilian rep's rivalry stays purely narrative. 8b requires nothing of that transport — it simply reuses it.

Deliberately minimal for 8b: no faction-vs-faction politics, no rep AI. Standing is a number the hustle reads and writes; that is the whole model.

---

## 7. Determinism & performance

- Every resolver takes `ref RngState` (xoshiro struct) — wall-clock-seeded in game (matching the league/dispatcher contract), harness-seeded for bit-reproducible run sequences. `HustleHarness` drives `EvaluateRun`/`EvaluateNegotiation` synchronously.
- Transient state (`HustleState`, per-round negotiation state) is `struct`, decisions are enums, no per-run heap churn — the zero-GC mandate applied on principle even though hustles are not a per-frame hot path. `HustleHarness` asserts a warm run allocates ~0 B.
- Same seed ⇒ identical run/negotiation outcome (a harness check, like the dispatcher's §7.10).

---

## 8. The accumulated-risk-state contract for 8c

8b is *"the first writer of accumulated risk state that step 8c's triad will consume."* The exact surface it produces, so 8c builds on a stable vocabulary:

| Signal | Written by | 8c consumer |
|---|---|---|
| `detection_risk` ↑ | seizure, business baseline, bad batch, retaliation, fencing sting | **Suspension/Arrest** — the same threshold the PED path already raises |
| `health_ceiling` ↓ | turf-war retaliation (and product use, if that flag is set elsewhere) | **Injury** — same floor `HeirGenetics.HealthRetirementFloor` / `injury_scare` key off |
| `recklessness` ↑ | aggressive stage choices | gritty-event prerequisites (`Recklessness > N`) — escalates the event graph |
| flag `narc_watchlist` | seizure, fencing sting | **Arrest** trigger candidate |
| flag `bad_product` | bad batch | reputation / cascade content |
| flag `controls_turf_f` | won TakeOver | durable territory — **passive turf-income hook (deferred to a follow-on; 8b sets the flag, does not build the recurring tick)** |
| Rival edge (crew rep) | turf-war loss | organic on-field rivalry (Phase-6 transport, already live) |

8c owns the *reading* of these (arrest roll on `detection_risk`/`narc_watchlist`, injury on `health_ceiling`, suspension on `detection_risk`) and the genuinely-new roster/availability mutation. 8b owns only the *writing*.

---

## 9. Acceptance checks (`Tools/HustleHarness`, new — compiles `Assets/Simulation/Hustles/*.cs` + `RngState.cs`)

1. **State-machine legality:** every transition from every state lands in a legal next state; `Resolved`/`Busted` are absorbing; bank-&-exit from each stage projects the profit-so-far.
2. **Stage-1:** `BuyInMax` scales correctly with trust/heat; `B` clamped to `[BuyInMin, min(funds, BuyInMax)]`; a forced-seize seed loses exactly `B` and writes the seizure deltas.
3. **Stage-2:** revenue is concave in `c` (a mid `c` beats both `1.0` and `CutMax` at fixed demand — proving the price/volume trade is real); bad-batch seed applies the 0.40 haircut + toxicity-scaled deltas.
4. **Stage-3:** the `MarketCap`/`m`/`pConflict` table drives revenue; **demand saturation** strands fire-sale units when `salableUnits > MarketCap`; a forced-retaliation seed writes the injury/beef/heat bundle and pushes the crew edge below −40.
5. **EV & tail bands (§3.4):** over N seeded runs per canonical policy, mean net funds and catastrophic-loss rate sit in the designed bands; **monotonic** across Safe→Moderate→Aggressive for both mean and tail.
6. **Fencing:** hidden `R` respects standing; neutral policy's EV band + sting-rate band hold; walk-rate rises with counter-aggression; a forced-sting seed halves the deal and writes the sting deltas.
7. **Determinism:** same seed ⇒ identical resolution (Narcotics run and Fencing negotiation both).
8. **Zero-alloc:** a warm resolved run and a warm completed negotiation each allocate ~0 B.
9. **Resolution → writers (Layer-2 integration, in whichever harness compiles Data):** a `HustleResolution` applied through `HustleService` moves `funds`/`detection_risk`/`health_ceiling`/`recklessness` by the clamped deltas, sets the flags with the right `set_on_day`, and a deep-Rival crew edge publishes `RivalryChangedEvent`.

---

## 10. Model split & follow-ons

- **Opus 4.8 (this pass):** the math, the state machine, and the contracts above. No code.
- **Sonnet 5 (implements):**
  - Layer 1 pure resolvers (`Assets/Simulation/Hustles/`) + `HustleHarness` (checks 1–8).
  - Layer 2 `HustleService` + the two new atomic risk writers + the consequence-enum extension (or the recommended shared-`ConsequenceApplier` refactor) + check 9.
  - Layer 3 UI scenes (`NarcoticsHustleScreen`, `FencingScreen`) — **`godot_scene_mapper` before any `GetNode<T>()`**, per `ui_conventions.md`; the ScheduleScreen Work-activity selector; the pending-session arming + forfeit wiring in `GameManager`.
  - `check_event_graph_integrity` if any content flags (`narc_watchlist` etc.) get referenced by new event JSON in the same pass.
- **Fable 5 (reviews):** because a hustle now writes the risk stats that feed the PED-style modifier layer and because turf-war Rival edges feed the calibrated baseball rivalry layer — **re-run `run_monte_carlo_batch` to confirm no league band moved** (hustle-created rivalries could nudge lines the way Phase 6's did; the harness's empty-vs-heated season guard is the tripwire), and sign off on the detection/health/recklessness writers' clamping and single-writer discipline.
- **Deferred (noted, not built here):** passive turf income on `controls_turf_f`; faction-vs-faction politics; a `uses_product` self-harm loop; the hustle result feed/UI copy pass (same future pass as the other screens' copy).

---

## 11. Fable 5 review addendum (2026-07-05) — rulings on the two disclosed design-math findings

Recorded here so the doc and `HustleHarness` agree; Opus's sections above are left as written.

**Finding 1 — §9 check 3's "a mid `c` beats both `1.0` and `CutMax`" is unreachable under §3.0's own constants. Ruling: no retune; the harness's diminishing-returns assertion is the permanent check.** With the 0.25 toxicity discount and `CutMax = 2.5`, raw fixed-demand revenue is `∝ c·(7−c)/6` — vertex at exactly `c = 3.5`, so the curve is monotone increasing over the whole playable domain (verified independently; folding in bad-batch expectation moves the vertex to ≈2.64, still just outside). The curve *is* strictly concave, which is what §3.2's own prose claims; only check 3's "mid beats both ends" operationalization assumed an interior vertex the constants never delivered. An interior raw-revenue vertex requires discount ≥ 0.375 at `CutMax 2.5` (or `CutMax < 1.5` at 0.25) — vertex sits at `(1+a)/(2a)` for `a = k/(CutMax−1)` — a drastic re-tune that would shift every calibrated EV figure and undercut §3.0's "cutting is where the money is made" economy, for no gameplay gain: the "real optimum, not max-everything" tension is already delivered at the system level by the layers this doc also specifies (demand saturation strands max-cut volume in fire-sale at any serious buy-in; bad-batch expectation and the toxicity-scaled detection/crew/stress costs push the *utility* optimum interior). Check 3 should be read as "revenue exhibits strictly diminishing marginal returns to cutting" — exactly what `HustleHarness` asserts.

**Finding 2 — §3.4's Aggressive row names `B = 1000` at `heat = 0.20, supplierTrust = 20`, but §3.1's own formula caps `BuyInMax` at `1000·0.8·0.92 = 736` there. Ruling: doc-internal error; the harness's feasible-ceiling policy (`min(1000, funds, BuyInMax)`) is the faithful reading, and the resolver's fail-loud out-of-range throw is correct. No code change.** Measured means over 20k seeded runs — Safe **+$93.9**, Moderate **+$225.5**, Aggressive **+$1,444.2** (at the feasible `B = 736`) — each verified against a closed-form recomputation (≈93 / ≈225 / ≈1,435), so the divergence from §3.4's rough +150/+275/+500 point figures is real model behavior, not sampling noise. The doc's own "shape to preserve, not the exact figures" contract holds everywhere: strict EV monotonicity Safe→Aggressive, all-positive EV, seizure rate ≈ `pSeize` independent of `B`, tail risk monotone. **Balance watch-item handed to 8c, deliberately not retuned now:** Aggressive's ≈2× buy-in per session (~7× a Legal Work day) is priced against a tail whose career consequences (arrest/suspension/injury off `detection_risk`/`health_ceiling`/`narc_watchlist`) are written by 8b but not yet *read* by anything — tuning EV before 8c's consumers exist would be tuning against half the cost function. Re-band the §3.4 targets in the 8c pass once the tail actually costs something.
