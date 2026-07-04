# Design — Marriage & Conception: the `Partner` Writer and the `conceive_child` Life-Event Consequence

**Author:** Claude Opus 4.8 (architecture) · **Phase:** 7 (Gritty Events — the deferred life-event consequence) · **Status:** design only — **no code written this pass.** This is the spec Fable 5 implements the engine plumbing against and Sonnet 5 authors the event content against (model split in §8). Companion to `gritty_event_framework.md` (which defers this in §4/§9) and `heir_mechanics.md` (which defers the partner system in §11.1/§11.2 and waits on it for the two-parent bloodline path).

This document closes the one hole both prior docs left open: **how a gritty event authors a `Partner` relationship (marriage) and triggers `CareerManager.ConceiveChild` (a child) without the Narrative layer referencing the Baseball sim.** The heir-blending math, interest roll, and the `ConceiveChild` seam itself already exist (Sonnet, Phase 6); this pass only supplies the *writers* that drive them from live play.

---

## 1. What already exists (the two seams this connects)

- **`RelationshipKind.Partner`** is already a graph edge kind, and the `relationship` consequence (`gritty_event_framework.md` §4) already creates/deepens edges of any kind through `RelationshipGraph.SetRelationship`/`AdjustAffinity`. What it *lacks* for marriage is a sensible target pool and single-partner exclusivity (§3).
- **`CareerManager.ConceiveChild(firstName, lastName, birthAge = 0)`** already inserts an unrostered heir off the avatar: parent A = avatar, parent B = the avatar's `Partner` counterpart's ratings (via `FindPartnerId` → `LoadRelationshipsFor`, a **DB read**) or `HeirGenetics.AverageParent()`, blends all seven ratings, rolls hidden `baseball_interest`, writes a `Child` edge to each real parent, and re-inits nothing. What it *lacks* is a trigger reachable from the event pipeline (§4).

The whole job is two small writers plus one cross-boundary hop.

## 2. The load-bearing constraint (why this needs its own pass)

`EventConsequenceApplier` lives in **Narrative** and is compiled by `Tools/GrittyEventsHarness` against **Data + Core + Simulation.Life + `RngState.cs` only** — deliberately *not* the Baseball assembly (verified in `GrittyEventsHarness.csproj`; the framework doc §1 states the same boundary). `ConceiveChild` lives in `CareerManager` (**Baseball**). Therefore:

> **The applier must not call `CareerManager` directly.** A conception is triggered by publishing a **Core `EventBus` event** that `CareerManager` (Baseball) subscribes to and services — exactly the pattern `StressImpulseEvent`/`FundsImpulseEvent` (Narrative → bus → Life) and `RivalryChangedEvent` (Life → bus → Baseball) already use.

This is not a style preference — a direct call breaks `GrittyEventsHarness`'s compile surface. The consequence is also a clean test split: `GrittyEventsHarness` proves the **request is published** (no Baseball compiled); `MonteCarloHarness` proves `CareerManager` **services the request** (Baseball compiled). Same division as the rivalry writer (dispatcher-side publish vs. ledger-side consume), already proven.

## 3. Marriage — the `Partner` writer (reuse `relationship`, add exclusivity)

Marriage is **not** a new consequence type. It is the existing `relationship` consequence with `kind: partner`, plus two semantic refinements confined to the `Partner` kind:

```json
{ "type": "relationship", "kind": "partner", "affinity": 60, "target": "league" }
```

- **Target pool — `league` for the first pass.** The applier's `TryPickTarget` already resolves `teammate | opponent | league` over the live `Players` snapshot; `league` (any other player) is the only pool that makes sense for a spouse today, because **every entity in the world is currently a ballplayer** — there is no civilian-NPC population to draw from yet. A partner drawn from `league` who has a `Player_Ratings` row contributes real ratings to the bloodline (`heir_mechanics.md` §2.3); this is exactly the two-parent path that doc designed and could not reach. The narrower "eligible singles" pool is deferred (§9).
- **Exclusivity — one active `Partner` at a time.** `ApplyRelationship`, when `consequence.RelationshipKind == Partner`, first checks whether the subject already has *any* `Partner` edge (walk `RelationshipGraph.GetEdgesFor`); if so, the consequence is **skipped** (same "empty pool → skip" precedent). This keeps `FindPartnerId` unambiguous (it returns the first `Partner` edge; there must be at most one) and prevents a scope-`any`/repeat-fire event from accreting spouses. Divorce/re-marriage is a future consequence (`clear` a partner), not this pass.

No new enum, no schema change — marriage is content (`kind: partner`) plus a ~6-line guard in the applier.

## 4. Conception — the `conceive_child` consequence (new closed-enum type)

A new member of the §4 closed consequence vocabulary:

```json
{ "type": "conceive_child" }
```

Payload-free. `ConsequenceKind.ConceiveChild`; `EventConsequence.ForConceiveChild()` factory (mirrors `ForFlag`'s shape — no `amount`, no `flag`). Semantics:

### 4.1 Load-time validation (fail loud at boot)

A `conceive_child` consequence may appear **only on a `scope: avatar` event.** Only the avatar has a tracked bloodline (`ConceiveChild` blends off the avatar and re-points nothing); a scope-`any` conception is meaningless. `GrittyEventJson.Parse` rejects it on any other scope with an event-id-labelled error — consistent with the framework's "a malformed batch fails at boot, not silently at fire time." This makes the apply-time path total: the subject *is* the avatar by construction.

### 4.2 Apply-time behavior (the cross-boundary hop)

When the applier resolves a `conceive_child` consequence:

1. **Resolve the co-parent from the live graph, not the DB.** The applier already holds `_relationships` (the in-memory `RelationshipGraph`) and may reference Life. It walks the subject's edges for a `Partner`; the partner's id (or `null` if unmarried) is captured. **Resolving from the in-memory graph — the same object that authored the marriage this session — sidesteps a real ordering hazard:** `ConceiveChild`'s own `FindPartnerId` reads the *database*, but a `Partner` edge authored earlier this session only persists on GameManager's day-cadence flush, so a same-session marriage-then-baby could miss it. Passing the id *in the request* makes conception order-independent of the flush.
2. **Publish the request (deferred).** `_bus.Publish(new ChildConceptionRequestedEvent(avatarId, partnerId, fired.Day))`. Deferred dispatch means it drains on a later pump — outside the applier's own DB batch (which already committed the fire's funds/flags), so no shared/nested transaction with the Baseball write. Same routing as `StressImpulseEvent`.
3. That is the applier's whole involvement. It never sees an heir, a rating, or a `Player_Ratings` row.

### 4.3 The Baseball-side consumer

`CareerManager` (already subscribes to `DayAdvancedEvent`/`SeasonRolledOverEvent` via `AttachTo(bus)`) adds a `ChildConceptionRequestedEvent` subscriber:

- **No-op unless the request's `ParentAvatarId` equals the current avatar** (defensive; the load-time gate already guarantees it, but the avatar can change via succession between publish and drain — a stale request for a now-retired avatar is dropped).
- **Generate names:** `lastName` = the avatar's own surname (bloodline continuity — query the avatar's `Players` row); `firstName` from a name pool. `LeagueGenerator` owns the name pools already used for generated players — expose a small `GenerateName(ref RngState)` (or a first-name-only accessor) so the subscriber pulls a name without inserting a row. Draw uses `CareerManager._rng` (thread-confined, seeded — determinism is the harness's, wall-clock in game, matching every other generator).
- **Conceive:** call the existing `ConceiveChild` with an **explicit partner id** rather than letting it re-derive one from the DB. Extend the signature to `ConceiveChild(firstName, lastName, birthAge = 0, string? partnerId = null)`: when `partnerId` is non-null, skip `FindPartnerId` and use it directly (ratings-or-average handled by the existing `TryGetRatings` branch); when null, the current DB-reading `FindPartnerId` path is unchanged (harness/direct callers keep working). `birthAge = 0` — a newborn, aged by the §5.5 yearly tick.
- **Announce (the feed seam):** publish `ChildBornEvent(childId, avatarId, partnerId, day)` after the insert commits — the resolved-event seam a future birth notification / family screen renders from, mirroring `GrittyEventResolvedEvent` and `LastSuccession`. No live consumer required now (shipped-before-its-consumer precedent).

**Multiple children are desirable and unconstrained.** Unlike marriage, conception has no exclusivity guard — `heir_mechanics.md` §6 *wants* multiple children as the natural hedge against `NoWillingHeir`. Pacing (a career shouldn't spawn twelve kids) is content's job via `cooldown_days` and flags, not the engine's.

## 5. The content arc (how the two writers compose)

The engine hard-codes no "marriage-then-baby" sequence; **content expresses the arc through the existing flag cascade** (`gritty_event_framework.md` §3's `min_days_since`), exactly as `bribe → compromised_syndicate → shakedown` does. The canonical shape (Sonnet authors, §8):

| Event (avatar scope) | Prereqs | Consequences |
|---|---|---|
| `met_someone` | `age > 20`, `flag_inactive: married` | `relationship kind=partner target=league affinity=60`; `set_flag married` |
| `starting_a_family` | `flag_active: married (min_days_since: 365)`, `flag_inactive: expecting` | `set_flag expecting` (+ flavor) |
| `child_is_born` | `flag_active: expecting (min_days_since: 270)` | `conceive_child`; `clear_flag expecting` (so the arc can repeat for a second child) |

This keeps the engine's two capabilities orthogonal (author a partner / conceive a child) and lets narrative pacing, branching, and repetition live entirely in JSON under `check_event_graph_integrity`. The `married`/`expecting` flags round-trip through `Entity_Flags` and survive saves; `cooldown_days` alone would not (it is in-memory pacing — §2 of the framework doc).

## 6. Boundary & consistency (respecting `heir_mechanics.md` §9.3)

- **Marriage** writes the `Partner` edge through `RelationshipGraph` (Narrative → graph, the sanctioned rivalry-writer path); it persists on GameManager's existing day-cadence relationship flush and hydrates at next boot. No change to the persistence bridge.
- **Conception** writes `Players`/`Player_Ratings`/`Child` rows through `CareerManager`'s Data queries in its **own batch** (never shared with a Life-sim write — `database_rules.md`), exactly as `CreateAvatar`/`ConceiveChild` already do. The `Child` edge is a **Data write**, not a `RelationshipGraph` call — preserving §9.3's rule that lineage reaches Life-relevant data through the DB, and that `Child` edges have no live Life-sim consumer (only lineage reads them, and lineage reads the DB).
- **No schema change.** `Partner`/`Child` kinds, `Players.age`/`baseball_interest`, and `Player_Ratings` all exist; the two new events are Core types (no DDL). Re-validate the live save per No Blind Queries before query work, but none is expected.

## 7. Acceptance checks

**`GrittyEventsHarness` (Narrative side — no Baseball compiled):**
1. Loader **rejects** a `conceive_child` consequence on a non-`avatar`-scope event (event-id-labelled, at parse time); accepts it on `scope: avatar`.
2. Marriage: a `relationship kind=partner` consequence creates exactly one `Partner` edge; a **second** partner consequence on an already-partnered subject is a no-op (exclusivity), edge count and counterpart unchanged.
3. Conception: a `conceive_child` fire on the avatar publishes exactly one `ChildConceptionRequestedEvent` whose `PartnerId` equals the avatar's current live `Partner` (and is `null` when unmarried) — observed on a locally pumped bus, with **no** `Player_Ratings` write from the applier.
4. The §5 arc end-to-end against a scratch save: `met_someone` (partner + `married`) → `starting_a_family` at +365 (`expecting`) → `child_is_born` at +270 (`conceive_child` request published, `expecting` cleared) — proving the flag cascade paces the arc.

**`MonteCarloHarness` (Baseball side — `CareerManager` compiled):**
5. The `ChildConceptionRequestedEvent` subscriber creates one unrostered heir with the request's partner as parent B (real ratings when the partner has a `Player_Ratings` row; `AverageParent` vector when `PartnerId` is `null`), the avatar's surname, and a `Child` edge to each real parent — the existing `ConceiveChild` invariants, now driven by a bus event.
6. **Order independence:** a request whose `Partner` edge has **not** yet been flushed to the DB still conceives the two-parent heir, because the id rides the event (the §4.2 hazard, closed).
7. A stale request naming a since-retired avatar is dropped (no heir, no throw).
8. Multiple `conceive_child` requests over a career create multiple distinct heirs off the same avatar; a subsequent succession can select among them (ties into the existing `EvaluateSuccession` fixtures — the `NoWillingHeir` hedge made reachable).
9. **Standing rule:** re-run the full `run_monte_carlo_batch` season — **no §8 band moves** (conception writes only ordinary rows through the unchanged resolver; this is the `heir_mechanics.md` §3 calibration-safety guarantee, reconfirmed).

## 8. Model split

- **Fable 5 — the engine plumbing.** The two Core events (`ChildConceptionRequestedEvent`, `ChildBornEvent`); `ConsequenceKind.ConceiveChild` + `ForConceiveChild` + `GrittyEventJson` parse & the load-time avatar-scope rejection; the applier's partner-resolution-from-graph + request publish + the `Partner` exclusivity guard in `ApplyRelationship`; the `CareerManager` subscriber + `LeagueGenerator.GenerateName` + the `ConceiveChild` `partnerId` overload + the `ChildBornEvent` publish; harness checks 1–9. This re-enters the career-wiring/bloodline path and carries the "no band moved" burden — the same reason succession was Fable's in `heir_mechanics.md` §9.4.
- **Sonnet 5 — the content + copy.** The §5 marriage→family→birth event arc (avatar-scoped, flag-cascaded) in a Content JSON with player-facing `prompt`/`label` text, `check_event_graph_integrity` per batch; later, the birth notification/feed surface once `ChildBornEvent` has a UI consumer (unchanged feed-assignment rationale).

Seam: Fable builds the two writers and the cross-boundary hop (boundary-sensitive, calibration-adjacent); Sonnet builds the narrative that drives them (pure JSON under the integrity skill).

## 9. Deferred / known gaps (deliberate)

1. **Spouse pool is `league`** (any ballplayer) — no civilian-NPC population exists to marry, and no "eligible singles" filter (age, already-married, already-a-rival). A first pass that unblocks the two-parent bloodline; refine when a civilian population or a dating sub-system lands.
2. **No divorce / re-marriage** — the exclusivity guard makes `Partner` write-once until a future `clear`-partner consequence exists. Widowhood/legacy on a partner's retirement is out of scope.
3. **Partner contributes ratings only, not needs/stress** — a `Partner` edge has no Life-sim effect yet (it is not consumed by needs or utility); it exists for the bloodline and for future life-sim coupling. Same "shipped before its live consumer" precedent as `Child` edges.
4. **`conceive_child` is payload-free** — no twins, no birth-age control, no explicit co-parent selection from JSON. If content later needs them, they are additive fields, not a reshape.
5. **Names are generated, not authored** — the child's first name comes from `LeagueGenerator`'s pool; surname is the bloodline's. A future naming UI at the birth event is a natural `ChildBornEvent` consumer.
