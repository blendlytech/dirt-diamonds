# Design — Hustle Mini-Games Depth Pass (+ Robberies, net-new)

**Author:** Claude Opus 4.8 (design) · **Status:** design only — no C# this pass; **§5.1 robbery stage list and §3.1 haggle EV-neutrality target LOCKED (Opus, on recommendations)**; **§11 open seams resolved to their recommended defaults**. Build flow per §10 (Fable 5 for any `Assets/Simulation/Hustles/` core touch + all reviews; Sonnet 5 for UI-only sub-slices). First build slice = **R-1** (robbery pure resolver + `HustleHarness` band, Fable 5). Companion to the two shipped hustle docs: `hustles_narcotics_fencing.md` (8b) and `hustles_texas_holdem.md` (8d). This doc **extends** those; it does not restate their architecture.

**Source of the ask:** `docs/game-idas/GAME_IMPROVEMENTS.md` lines 54–56 —
> - Drug dealing, should be series of progressive mini games with stages.
> - Fencing should be a series of progressive mini games with stages.
> - Make robberies more interactive (instead of just clicking a button to rob).

---

## 0. Reconciliation — what actually ships today (READ FIRST)

The roadmap (progress.md, auto-memory) lists **"Hustle minigames — Texas Hold'em, narcotics stages, fencing, robberies"** as an *unbuilt* future slice. **That premise is wrong for three of the four**, the same way the event-choice UI turned out already-shipped. Grounded against real code:

| Mini-game | Sim core (Layer 1, pure) | Screen (Layer 3) | Wired? | Verdict |
| --- | --- | --- | --- | --- |
| **Texas Hold'em** | `HoldemEvaluator`/`Equity`/`Agent`/`Hand`/`Session`/`Profile`/`Cards`/`PreflopTable` | `TexasHoldemTable.tscn/.cs` (full table state machine, one hand at a time) | ✅ `WorkActivity.Poker` + Main.tscn + ScheduleScreen dropdown | **SHIPPED, deep.** The skill hustle. Already a full NLHE table with side pots, raid rolls, rake. |
| **Narcotics** | `NarcoticsHustle` (3-stage: Inventory Drop → Profit/Toxicity Cut → Territory Control) | `NarcoticsHustleScreen.tscn/.cs` | ✅ `WorkActivity.Narcotics` | **SHIPPED, shallow.** Three sequential *decisions* (buy-in → cut factor → push level), one session, then resolve. The "stages" exist as resolver steps but present as three sliders + a result. |
| **Fencing** | `FencingNegotiation` (alternating-offer over a hidden reservation) | `FencingScreen.tscn/.cs` | ✅ `WorkActivity.Fencing` | **SHIPPED, shallow.** One lot, one negotiation, accept/counter/walk. No lot selection, no acquisition step, no multi-lot session. |
| **Robberies** | — none — | — none — | ❌ | **NOT BUILT.** No resolver, no screen, no `WorkActivity` member, no design doc. The one true greenfield. |

**So this pass is a *depth* pass on three shipped games plus *one* net-new game — not four builds.** The guiding rule throughout: **the calibrated Layer-1 sim cores are foundations, not blank pages.** Narcotics' and Hold'em's math is `HustleHarness`-proven at a tuned EV/skill band; a depth pass adds *stages and player-facing texture around* that math, and only re-tunes constants where a new stage genuinely changes the EV surface (flagged explicitly per §3/§4). Every Layer-1 touch re-runs `HustleHarness` and must not silently move the shipped EV bands.

## 1. Shared design frame — what "progressive mini-games with stages" means here

The user's phrase is the spec's north star. Today a hustle is: pick the activity in the planner → the day arms one `PendingHustleSession` → open one screen → make a few decisions → one `HustleResolution` applies. That's **one atomic session per armed day.** "Progressive mini-games with stages" asks for the *session itself* to become a short sequence of distinct, legible sub-games with visible progression, mounting stakes, and per-stage bail points — while preserving the two invariants that make the current model safe:

- **INV-1 — One net resolution per armed day.** No mid-session DB writes; the whole staged sequence accrues into a single `HustleResolution` applied on completion, and **forfeits cleanly to no-deal if the day advances mid-sequence** (the shipped abandon-evaporates discipline). Stages accrue *in memory* (the `HustleState`/`FencingState` `record struct` `with`-copy pattern already does exactly this).
- **INV-2 — Every dollar of upside buys a slice of career-threatening tail risk.** More stages = more upside reachable = the tail must scale with how deep the player pushes. Depth must not turn a lumpy-but-bounded hustle into a money printer (the `hustles_narcotics_fencing.md` §7 thesis).

Two structural levers deliver "stages" without violating INV-1/INV-2:

- **Lever A — Multi-round sessions (a *series*).** Instead of one lot / one buy-in, the session is N rounds the player can *keep going or bank*: each round is +EV in isolation but adds heat/exposure, so "how many rounds do I run before I walk" becomes the meta-decision. This is the cleanest realization of "series of mini-games" and reuses the accrue-in-memory pattern verbatim.
- **Lever B — Front-stages (acquisition / casing).** Add an earlier interactive stage *before* the existing resolver runs — Fencing gains a "where did the goods come from" acquisition step; Narcotics gains a supplier-haggle before the drop; Robberies is *entirely* front-stages (case → approach → execute → getaway). Front-stages gate or modify the existing stage's context rather than replacing it.

Each game below picks the lever(s) that fit its fiction, and **every new stage is an independently bootable sub-slice** (ui_conventions thin-slice rule) so nothing is a big-bang rebuild.

## 2. Architecture — unchanged; everything slots into the shipped seam

No new architecture. Every addition lives in the existing three layers and the existing `WorkActivity`/`PendingHustleSession` seam:

- **Layer 1** additions are new pure methods / new `record struct` fields on the existing resolvers (`NarcoticsHustle`, `FencingNegotiation`) or one new pure resolver file (`RobberyHustle`). Engine-free, Data-free, `RngState`-only — `HustleHarness`-drivable exactly as today.
- **Layer 2** (`HustleService`) gains one `Build…Context`/`Apply…Resolution` pair for Robberies (mirroring the three that exist), and Robberies adds **one** `WorkActivity` member (`Robbery`) — the same one-member extension Poker was. **No new resolution shape:** everything projects into the shared `HustleResolution` (Robberies leaves the faction/Narcotics fields at their defaults, like Fencing and Hold'em already do). One additive flag field may be needed (`SetRobberyBustFlag` → `"robbery_bust"`, the analogue of `gambling_bust`/`narc_watchlist`) — a clean additive extension of the closed flag set, no schema change.
- **Layer 3** additions are new panels *inside* the existing screens (Narcotics/Fencing gain stage panels the way `TexasHoldemTable` already has start/table/hand-end/result panels), plus one new screen `RobberyScreen.tscn/.cs` (a fourth permanent sibling under Main, forfeit-clean like the other three).

**No schema change anywhere.** Every stat any stage reads or writes already exists (`Players.{funds, detection_risk, health_ceiling, recklessness}`, `Entity_Flags`, `Relationships`, additive `Game_State` keys for any bookkeeping). This holds the standing discipline.

**The wall holds:** Life↔Baseball separation is untouched; the hustle resolvers stay in `Assets/Simulation/Hustles/` referencing only their own math + `RngState`.

---

## 3. Narcotics — Lever A (multi-run supply chain) + Lever B (supplier haggle)

**Today:** three decisions in one session — buy-in amount (Inventory Drop) → cut factor (Profit/Toxicity Cut) → push level (Territory Control) — resolve. The three "stages" are real resolver steps but read as *three sliders and a result screen*. Depth targets: make the stages feel like distinct beats, and let a good run *compound* across the day instead of being one shot.

### 3.1 Stage 0 (new front-stage, Lever B) — Supplier Haggle

Before `DropInventory`, a short negotiation with the supplier rep (`HustleSupplierPlayerId`, already resolved by `HustleService`) sets **this run's `UnitCost` and `BuyInMax` multiplier** instead of taking the flat `NarcoticsProfile.UnitCost = 10` and the trust-derived ceiling as givens.

- **Reuse, don't reinvent:** this is a *tiny* alternating-offer, structurally the Fencing negotiation inverted (player wants a *lower* buy price; supplier has a hidden floor keyed on `SupplierTrust`). Rather than duplicate `FencingNegotiation`, extract the alternating-offer kernel or add a `NarcoticsHustle.HaggleSupply(...)` pure method that returns an effective `UnitCost` + a small `SupplierTrustDelta` (push too hard, trust drops; walk away, no run today).
- **Stakes:** a good haggle widens margin on *every* unit the whole run; a blown haggle can leave the supplier unwilling (forfeit-to-no-deal, INV-1). No new tail risk here — the risk still lives in the drop/cut/push stages.
- **EV-neutrality target — LOCKED (Opus, on recommendations):** lowering `UnitCost` is a direct margin lever, so the haggle's reachable range is tuned so the **neutral-autopilot expected effective `UnitCost` ≈ the shipped flat 10** — a skilled haggler beats it, a bad/greedy one pays more or gets no run, and the *average* run's EV band is preserved. The bind is asserted by `HustleHarness`: driving the neutral-haggle-autopilot (accept once cost ≤ a fixed fraction of the opening ask, mirroring `NeutralAcceptDecision`) across seeds must land the mean effective cost inside a tight tolerance of 10, and the end-to-end Narcotics EV band must not move outside its shipped tolerance. Rejected alternative: letting the haggle be a pure margin *bonus* (expected cost < 10) — that would inflate the shipped EV and break INV-2's lumpy-but-bounded thesis. The haggle adds *variance and player agency*, not free money.

### 3.2 Stage 3+ (new, Lever A) — Re-up & run again

After a run resolves (Territory Control or an early Bank-Exit), if the player still has capital and time in the Work block, offer **"re-up and run again"** vs. **"bank the day."** Each subsequent run:

- Starts from the *current* live context (funds already updated in-memory, heat already accrued in-memory — INV-1 still holds, nothing hit the DB yet), so a hot run makes the next drop's seizure roll worse (`pSeize` already scales with `Heat`). **This is the built-in tail scaling for Lever A** — the deeper you push in a day, the more your own accrued heat threatens the next drop. No new risk math; the existing `Heat`-scaled `pSeize`/`pBadBatch` does the work once heat accrues within the session.
- Caps at a small N (e.g. 3 runs/day) by the Work block's hour budget — a `Game_State`-free in-memory counter on the screen's session state.
- The whole multi-run sequence still accrues into **one** `HustleResolution` applied once on Done (INV-1).

**Sub-slice N-1 (Lever A, biggest win, no new resolver math):** the re-up loop. Purely a Layer-3 change plus a thin Layer-2/GameManager check that the Work block still has hours — the resolver already supports being called again with an updated context. **N-2 (Lever B):** the supplier haggle front-stage (new pure method + harness autopilot + screen panel).

## 4. Fencing — Lever B (acquisition) + Lever A (multi-lot fence session)

**Today:** one lot appears, one alternating-offer negotiation, accept/counter/walk, done. Missing entirely: *where the goods came from* and *why there's only ever one lot*.

### 4.1 Stage 0 (new front-stage, Lever B) — Acquire the lot

The goods have to come from somewhere. Add a short **acquisition stage** that sets the lot's hidden `V` (true value) and a starting heat cost, instead of `StartLot` drawing `V` from a flat `[VMin, VMax]` uniform:

- **A small pick, not a minigame-within-a-minigame:** present 2–3 sourcing options with legible risk/value tradeoffs — e.g. *pawn-shop overstock* (low V, near-zero heat), *"fell off a truck"* (mid V, small heat + `spoiled_goods` chance), *fresh from a job* (high V, notable heat, and — the cross-hustle hook — **only available if the player ran a Robbery that armed a `hot_goods` flag**, §5.6). The chosen option seeds `StartLot`'s `V` and an initial `DetectionRiskDelta`.
- This makes Fencing's `V` *chosen* rather than random, turning the negotiation's ceiling into something the player influenced — the "stage" the user asked for, upstream of the existing negotiation.

### 4.2 Stage 2+ (new, Lever A) — Multi-lot session

After a lot closes or the fence walks, if the fence's patience/standing allows, offer **"show him another lot"** vs. **"that's enough for today."** Subsequent lots:

- Draw from the same acquisition pool; each closed deal nudges `FenceStanding` (already a `SupplierTrust`-like edge) and each *sting* roll is independent, so running many lots raises cumulative sting exposure — Lever-A tail scaling, reusing the existing per-deal `ComputeStingProbability`.
- Accrue into one `HustleResolution` (INV-1).

**Sub-slice F-1 (Lever A):** the multi-lot loop (Layer 3 + a re-`StartLot` call — the resolver already supports it). **F-2 (Lever B):** the acquisition front-stage (new pure `AcquireLot` method feeding `StartLot`'s `V`, + screen panel; `hot_goods` cross-hook is content-gated so it degrades gracefully before Robberies ships).

## 5. Robberies — net-new, Lever B all the way down (four stages)

The only true greenfield. `GAME_IMPROVEMENTS.md` frames it against a strawman "just clicking a button to rob" — so the whole point is an *interactive, staged* hustle. It is the natural home for Lever B (sequential front-stages), and it is where the highest tails live (this is the hustle that most directly threatens the baseball career via arrest).

### 5.1 The four stages (one session, accrue-in-memory, INV-1) — LOCKED

**Decision (Opus, on recommendations):** case → approach → execute (with a mid-execute press-your-luck beat) → getaway, exactly as below. This shape was chosen because it gives each stage a *distinct verb* (choose target / choose method / commit-or-bail / escape) and makes the early picks visibly pay off in the getaway roll (§5.1.4), so the sequence reads as one connected job rather than four independent dice rolls — the core of what "make robberies more interactive" asks for. A flatter two-stage (pick-then-roll) was rejected as barely more interactive than the strawman "click to rob"; a longer sequence was rejected as over-budget for one Work block.

1. **Case the target (pick).** Choose a target from 2–3 offered marks, each with a legible **score / heat / difficulty** profile (a convenience store vs. a bookie's stash vs. a warehouse). Optionally spend a beat "casing" to *reveal* one hidden attribute (reduces variance on the execute roll) at a small time/heat cost. Sets the run's base reward and difficulty.
2. **Approach (pick).** Choose *how*: solo & quiet (low score mult, low heat, low bust chance), strong-arm (higher score, `health_ceiling` at risk if it goes wrong, moderate bust), or crew job (needs a crew rep — reuse `HustleCrewPlayerId` — highest score, split take, standing/heat consequences). This picks the resolver's difficulty/hazard curve.
3. **Execute (the roll + one live decision).** The core resolution: a success/partial/failure roll driven by `(target difficulty, approach, Recklessness, Heat)` with a **single interactive beat** — a "keep going / grab and run" choice mid-execution (press your luck for the full score at rising bust probability, or bail with the partial take). This is the game's "stages with stakes" crescendo. On failure: `SetRobberyBustFlag` (`"robbery_bust"`), a `DetectionRiskDelta` spike toward 8c's arrest triad, possible `HealthCeilingDelta` on strong-arm, and **absence teeth** if the arc later wires an arrest-absence (mirrors DIRT-1's `dirt_busted`).
4. **Getaway (roll, modified by earlier picks).** A final heat/detection roll: a clean getaway banks the score with low added heat; a botched one converts a successful grab into a partial + heat spike (or, on a bad execute + bad getaway, a bust). Casing and a quiet approach lower this roll's hazard — so the *early* stages visibly pay off here, making the sequence feel connected rather than four independent rolls.

### 5.2 Outputs & cross-hooks

- **Projects into the shared `HustleResolution`:** `FundsDelta` (the take), `DetectionRiskDelta`, `HealthCeilingDelta` (strong-arm), `RecklessnessDelta`, `StressDelta`, and `SetRobberyBustFlag`. Faction/Narcotics fields default. Crew jobs also write a `CrewStandingDelta` (reuse the Narcotics faction-edge apply path in `HustleService`).
- **Bust monetary stake — user ruling 2026-07-12 (R-1 build):** a bust writes a *negative* `FundsDelta` (bail/legal fees, `BustLegalCostFrac` × the mark's base score, clamped to funds on hand so it zeroes out but never indebts), so a robbery gone wrong is a real financial loss, not only a career one. `RobberyContext` carries `Funds` for the clamp. Harness-proven consequence: the blind-warehouse always-press policy is net-negative in money (≈ −40 mean) on top of its ~81% bust rate — the tail now eats the upside past the sensible frontier.
- **Cross-hustle hook (the reason robberies + fencing ship together well):** a successful robbery arms a `hot_goods` `Entity_Flag`, which unlocks Fencing's high-value "fresh from a job" acquisition option (§4.1). This is the content spine that makes the four hustles an *economy* rather than four silos — robbery feeds fencing, fencing launders the take. Purely flag-gated, so it degrades gracefully in either build order.
- **DIRT arc hook:** `robbery_bust`/arrest slots naturally into the `criminal_underworld_content_arc.md` triad and the shipped `compromised_syndicate` spine — a future DIRT batch can fire consequences off it. Named, not built here.

### 5.3 New surface area (all additive)

- Layer 1: new `RobberyHustle.cs` (pure resolver + `RobberyContext`/`RobberyState` `record struct`s in the established shape) with a neutral autopilot policy for the harness (a fixed "quiet approach, bail on the press-your-luck beat" stand-in, mirroring Fencing's `NeutralAcceptDecision` and Hold'em's TAG autopilot).
- Layer 2: `HustleService.BuildRobberyContext` + `ApplyRobberyResolution`; one `WorkActivity.Robbery` member; optional `SetRobberyBustFlag` on `HustleResolution`.
- Layer 3: `RobberyScreen.tscn/.cs` (case → approach → execute → getaway → result panels, forfeit-clean).
- Planner: `ScheduleScreen`'s `WorkActivityOption` gains "Robbery" (the dropdown already enumerates `WorkActivity`, so this is one entry).

**Sub-slices R-1 → R-4**, each bootable: R-1 the pure resolver + harness band (no UI); R-2 the screen shell with case/approach/execute/getaway wired to R-1; R-3 the press-your-luck live beat + getaway modifiers; R-4 the `hot_goods`→Fencing cross-hook. R-1 (Layer-1 math + a fresh EV/tail band) is the foundation everything else sits on.

---

## 6. Hold'em — presentation refresh only (NO sim touch)

Hold'em is already the deepest of the four (full NLHE table, side pots, streets, rake, raid rolls, archetype field). The user's list does **not** ask to deepen poker — it asks to deepen *drug dealing and fencing* and to make *robberies* interactive. So Hold'em's slice is **presentation-only**, and explicitly **out of scope for any Layer-1 touch** (the `Holdem*` math is `HoldemHarness`-proven at a skill-monotone band; do not perturb it).

Optional Layer-3-only polish, if the user wants it after the higher-value work (each independently deferrable, none touching the sim):

- Card/board rendering as actual pips/suits instead of the current text labels (`FormatCards` → a small card-face control) — pure view.
- A running session bankroll graph / hands-played counter — reads existing `HoldemSessionState`.
- Location/stakes flavor: name the tiers (back-room / social club / high-roller) — cosmetic labels over the existing `StakesTier`.

**Recommendation:** park Hold'em polish entirely until Narcotics/Fencing depth + Robberies ship. It is the lowest-value item on this list (the game is already good) and the user's own ask omits it.

---

## 7. Cross-cutting: the daily-clock & forfeit contract (unchanged, must hold)

Every addition rides the shipped seam **without changing it:**

- The planner arms **one** `PendingHustleSession(activity, day)` (Robbery is a new activity value; the arming code at `GameManager.cs:1074` already branches on "any non-LegalWork activity", so `Robbery` needs zero new arming logic — exactly as Poker needed none).
- The clock **soft-pauses** while a hustle session is open (the shipped hustle-session soft-pause deviation from Slice G — advancing the day IS the forfeit). Multi-round sessions (Levers A) do not change this: the *whole* staged sequence is one soft-paused session; advancing mid-sequence forfeits it to no-deal (INV-1).
- **`CanAdvanceDay` is NOT gated on `HasPendingHustleSession`** (the shipped, deliberate choice — a hard gate would soft-lock the can't-afford-to-start panel). Depth doesn't change this: forfeit-to-no-deal stays the safe exit at every stage.
- **No mid-session DB writes at any stage.** The accrue-in-memory `record struct` pattern is mandatory for every new stage; the single `Apply…Resolution` on Done is the only DB touch (INV-1).

---

## 8. What this pass explicitly will NOT do (non-goals)

- **No rebuild of the shipped sim cores.** Narcotics' 3-stage math, Fencing's alternating-offer math, and all of Hold'em stay as calibrated; new stages *wrap* them, and any constant retune is flagged + harness-proven (§3.1, §5.1).
- **No new resolution shape / no schema change.** Everything projects into `HustleResolution`; at most one additive flag field (`SetRobberyBustFlag`).
- **No new time model.** No hustle becomes an ad-hoc launch; every one stays a Work-block-armed session (the `hustles_narcotics_fencing.md` §2 "considered and rejected: decoupled ad-hoc launch" ruling still binds).
- **No Hold'em sim touch.** Presentation-only, and deferred.
- **No content/arc authoring here.** `robbery_bust`/`hot_goods` are *hooks* for a future DIRT batch (Opus → Sonnet), not authored events in this pass.
- **No audio/motion.** Per the standing "away from sounds and little things" directive; Slice-D/F hooks can layer on later.

---

## 9. Testing & gates (per sub-slice)

- **Any `Assets/Simulation/Hustles/` touch** (all Narcotics/Fencing math additions, the whole Robbery resolver) ⇒ **mandatory `HustleHarness` re-run** proving: (a) the new stage's math is correct against fixtures, (b) the shipped Narcotics/Fencing EV bands **did not move** except where a retune was explicitly sanctioned (§3.1), and (c) Robbery's fresh EV/tail band is skill-sensible (a cautious policy is +EV-but-lumpy, a reckless press-your-luck policy carries a real bust rate). New neutral-autopilot policies per new resolver so bands are assertable headlessly (the `NeutralAcceptDecision`/TAG-autopilot precedent).
- **`Assets/Simulation` (Baseball) stays untouched** ⇒ `git diff --stat -- Assets/Simulation/Baseball` empty; the MLB bit-identity guard is inert by construction, but `run_monte_carlo_batch` runs anyway per the standing rule to prove 345/345 byte-exact.
- **UI-only sub-slices** (the re-up/multi-lot loops, screen panels, the `RobberyScreen` shell): live boot (godot MCP) on the real save + node paths verified via `godot_scene_mapper` before any `GetNode` (ui_conventions "verify before wiring"); no harness delta expected.
- **`SchemaValidator` unchanged** (no DDL; any `Game_State`/`Entity_Flags` additions are KV/flag rows, not schema). `GrittyEventsHarness` unchanged unless/until a DIRT batch consumes the new hooks.
- **Forfeit discipline test:** for every new multi-stage/multi-round path, `HustleHarness` (or a live-boot check) confirms that abandoning mid-sequence applies **zero** net resolution (INV-1) — the safety property that makes soft-pause + no hard gate correct.

---

## 10. Model assignments & recommended build order

**Roles:** Layer-1 math (calibration-sensitive) and every review = **Fable 5**. UI-only sub-slices (screen panels, the re-up/multi-lot loops, `RobberyScreen` shell) = **Sonnet 5** (or Fable if reallocating). Each sub-slice → **Fable 5 review** + its §9 gate set.

Ordered by value ÷ risk (highest first). Each row is an independently shippable slice:

1. **R-1 — Robbery pure resolver + harness band (Fable 5).** The one true greenfield; net-new EV/tail band, no shipped calibration to disturb, unlocks the most-requested "interactive robbery." Foundation for R-2–R-4.
2. **N-1 — Narcotics re-up loop (Sonnet 5, Lever A).** Biggest depth win for zero new resolver math — the shipped resolver already re-runs with an updated context; heat accrual gives free tail scaling.
3. **R-2/R-3 — Robbery screen + press-your-luck beat (Sonnet 5 → Fable review).** Makes R-1 playable and delivers the crescendo stage.
4. **F-1 — Fencing multi-lot loop (Sonnet 5, Lever A).** Same low-risk re-`StartLot` reuse as N-1.
5. **N-2 — Narcotics supplier haggle (Fable 5, Lever B).** New pure method + EV-neutral retune of `UnitCost` reachability + harness autopilot; touches calibration, so Fable + a proven band.
6. **F-2 — Fencing acquisition front-stage (Fable 5 for the `AcquireLot` math, Sonnet for the panel).** Includes the `hot_goods` cross-hook.
7. **R-4 — Robbery→Fencing `hot_goods` cross-hook (Sonnet 5).** Ties the economy together once both games exist.
8. **Hold'em polish (deferred / optional, Sonnet 5).** Presentation-only; park until the user asks.

**Before building:** nothing blocking — §5.1 (robbery stages) and §3.1 (haggle EV-neutrality) are LOCKED and the §11 seams are resolved to their recommended defaults. R-1 is ready to build against a frozen contract. The user may still eyeball the locked stage list on first playtest, but no design call gates the build.

---

## 11. Open seams — RESOLVED to recommended defaults (Opus, on recommendations)

The three build-time seams are resolved to their recommended defaults below so the build slices carry no open questions. Each remains a cheap flip if the build surfaces a reason (all are local, none touch the shared seam).

- **Haggle-kernel reuse (§3.1) — RESOLVED: parallel method.** N-2 adds a parallel `NarcoticsHustle.HaggleSupply` pure method rather than extracting a shared alternating-offer kernel from `FencingNegotiation`. Rationale: the parallel method is lower-risk (zero refactor of the shipped, harness-proven Fencing math — a calibrated file) and the two negotiations differ enough in framing (buyer-side floor vs. seller-side reservation, different context inputs) that forcing them into one kernel is premature abstraction. Revisit only if a *third* alternating-offer stage appears and the three obviously converge.
- **Re-up hour budget (§3.2 / §4.2) — RESOLVED: flat N (=3), in-memory.** The N-run/N-lot cap is a flat in-memory counter on the screen's session state (default 3), needing **zero** GameManager change and no dependency on the Work block exposing remaining-hours. The hour-aware cap is a strictly-additive follow-on if playtest wants it to feel more diegetic; it is not a prerequisite for shipping N-1/F-1.
- **`hot_goods` lifetime (§5.2 / §4.1) — RESOLVED: consume-on-use.** A successful robbery sets `hot_goods`; it unlocks exactly one high-value "fresh from a job" acquisition option in Fencing, and is **cleared when that lot is acquired** (not on a timer). Simplest correct semantics, no decay bookkeeping, and it reads right diegetically (you have hot goods until you move them). A decay timer is a later refinement only if hoarding `hot_goods` across many days feels exploitable in playtest.
- **Robbery arrest-absence teeth (§5.1) — deferred to the DIRT arc (unchanged).** Whether `robbery_bust` triggers real bench-time (like DIRT-1's `dirt_busted` arrest absence) is a content/DIRT decision, not a mini-game decision — flagged for a future DIRT batch (Opus → Sonnet), out of scope for this pass. The mini-game only *sets the flag*; a content batch decides its teeth.
