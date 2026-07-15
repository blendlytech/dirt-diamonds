# Design — Robbery Hustle: One New High-Stakes Target ("The Jewelry Exchange")

**Author:** Claude Opus 4.8 (mechanical/calibration architecture) · **Slice:** Hustle depth, one target added to one hustle · **Status:** spec written for Sonnet 5 **or** Fable 5 to build against; Fable 5 reviews + gates. Companion to `docs/design/hustle_minigames_depth_pass.md` §5 (the Robbery contract this extends) and `docs/design/hustles_narcotics_fencing.md` (the shared resolver shape). Mirrors the section rigor of `docs/design/criminal_underworld_dirt3_product_and_turf.md`, adapted for a calibration spec.

This is the smallest, safest possible content slice: **add exactly one new `RobberyTarget`** — a new enum value plus one new row in the `Target(...)` switch — to the four-stage Robbery resolver. The stage machine, `ApproachProfile`, and every `Execute`/`PressLuck`/`Getaway`/`Bust` formula are already generic over any `TargetProfile`; this pass confirms that (against shipped code, §2) and adds a fourth rung to the target ladder that continues the established score/difficulty/heat progression. **Nothing else is touched** — not the other three targets, not the approaches, not Narcotics/Fencing/Hold'em, not the DIRT-3 narrative layer, not the schema. It is the "new mechanical content within an existing system" the user asked for, at minimum blast radius.

---

## 1. Design intent — what the new target should feel like

- **A fourth rung that reads as "the big score."** Today the Case stage offers three legible marks — a convenience store, a bookie's stash, a warehouse (`hustle_minigames_depth_pass.md` §5.1.1: *"a convenience store vs. a bookie's stash vs. a warehouse"*). The ladder tops out at an industrial warehouse. This slice adds the rung above it: the **Jewelry Exchange** — the heist a player graduates to, the one that most directly threatens the diamonds by way of the arrest teeth DIRT-3 just wired onto `robbery_bust`.
- **It extends the frontier, it doesn't move it.** The three shipped targets sit on a clean reward-vs-tail frontier (more score buys more bust probability, INV-2). The new target must be the next point *on that same line* — a sensible geometric-ish continuation of `350→900→1800` score, `0.20→0.40→0.60` difficulty, `3→6→10` heat — not an arbitrary jump, and it must not perturb the three points already on the line.
- **The top rung punishes half-measures.** The design statement the numbers below encode: at the jewelry exchange you *commit or you stay home*. A careful cased-and-bail line is still positive EV (the ladder invariant — every shipped target has a viable careful line), but it is thinner than the warehouse's, because you pay the biggest execute-bust tax on the board for only a half-score bail. The reward lives in pressing your luck. That is the fiction of the diamond job: you don't nibble at it.
- **The reward is a specialist's reward.** Because the execute roll folds in `Heat` and `Recklessness`, the jewelry exchange pays off best for an avatar who has invested in the life (high recklessness) and kept the heat down. A cold, low-reck avatar can still clear a careful bag; a committed criminal specialist turns it into the best money on the board. The target *rewards* the build the arc encourages.
- **Title resonance, deliberately.** The game is called *Dirt & Diamonds*. The top robbery target being, literally, diamonds is a wink worth taking — and it is the mechanically *correct* choice too (§3), because jewels are the archetypal fenceable score and this target's clean-getaway `hot_goods` flag feeds Fencing's `FreshFromAJob` acquisition (`hustle_minigames_depth_pass.md` §5.2), reinforcing the shipped robbery→fence economy instead of straining it.

**Flavor picked, alternatives rejected (briefly).** *Jewelry Exchange* wins over the other top-heist archetypes: a **bank** is generic and its cash take is a weak thematic fit for the `hot_goods`→fence hook; a **casino cage** ties nicely to the gambling underworld but is also cash (weak `hot_goods` fit) and strains the "solo & quiet" approach; an **armored car** is a moving-transport hit, which fights the fixed-location "case the target" beat. The jewelry exchange is a fixed location (casing fits), produces archetypal hot goods (the fence hook fits), maps cleanly to all three approaches (slip in after hours = solo-quiet; smash-and-grab takeover = strong-arm; full crew job = crew), sits unambiguously above an industrial warehouse in value-density and security, and lands the title wink. Enum identifier: **`JewelryExchange`**; button copy leans on "diamonds."

---

## 2. What already exists (confirm generic-over-target, do not collide) — verified against shipped code this pass

The single most important thing to establish before writing a line: **the resolver, the Layer-2 service, the schema, and the narrative layer are all already generic over the target — a new target is data, not logic.** Verified against `Assets/Simulation/Hustles/RobberyHustle.cs`, `Assets/Economy/Hustles/HustleService.cs`, `Assets/UI/RobberyScreen.{cs,tscn}`, `Tools/HustleHarness/Program.cs`, and `Assets/Narrative/Events/Content/dirt_underworld_events.json` as shipped:

| Surface | Shipped shape (verified) | Does a new target touch it? |
|---|---|---|
| `enum RobberyTarget : byte` (`RobberyHustle.cs:40-45`) | `ConvenienceStore, BookieStash, Warehouse` | **YES — add `JewelryExchange`** (edit 1 of 2) |
| `Target(RobberyTarget)` switch (`:186-192`) | one `TargetProfile(baseScore, difficulty, baseHeat)` row per value, `_ =>` throws | **YES — add one row** (edit 2 of 2) |
| `TargetProfile` struct (`:169-184`) | `BaseScore` / `Difficulty` / `BaseHeat` only | No — the new row uses the existing three fields |
| `ComputeExecuteSuccessProbability` (`:263-272`) | `Target(state.Target).Difficulty + Approach(...).BustAdd + 0.15·Heat − 0.10·Reck − caseBonus` | No — reads the new row generically |
| `ComputePressLuckBustProbability` (`:299-306`) | reads **only** `Approach` + ctx — **target-independent** | No — the press beat is identical for every target |
| `Getaway` `pBotch` (`:353-356`) | `…+ 0.5·Target(...).Difficulty + 0.10·Heat − caseBonus` | No — reads the new row generically |
| `Bust` legal fee (`:394-395`) | `min(funds, BustLegalCostFrac(0.25) · Target(...).BaseScore)` | No — scales off the new `BaseScore` automatically |
| `ApproachProfile` + `Approach(...)` switch (`:194-226`) | keyed on `RobberyApproach`, entirely independent of target | **No — zero change (see §3.3)** |
| `CaseTarget` / `ChooseApproach` / `Execute` / `PressLuck` / `GrabAndRun` / `Getaway` / `Bust` | one method per stage, all take `Target(state.Target)` / `Approach(state.Approach)` | No — generic over any `TargetProfile` |
| `WorkActivity` enum (`HustleService.cs:21-28`) | `Robbery` member already exists | No — the planner arms `Robbery`; the *target* is chosen inside the screen, never in the planner |
| `BuildRobberyContext` (`HustleService.cs:172-177`) | reads `funds`, `detection_risk/100`, `recklessness/100`, `hasCrew` — **no target input** | **No — zero change** |
| `ApplyRobberyResolution` + `ApplyCore` (`:230-297`) | applies the target-agnostic `HustleResolution` (funds/detection/health/reckless + flags) + crew standing | **No — the DB never learns which target was hit** |
| `RobberyScreen.{cs,tscn}` (`:47-49, 82-85, 108-110` / tscn `:54-68`) | **three explicit `Button` nodes** in `CasePanel`, each wired to `OnTargetPicked(target)` | **YES — add a fourth button** (§4) |
| `HustleHarness` `RunRobberyChecks` (`Program.cs:806-1052`) | per-target/-approach EV/bust bands via a `Band(...)` helper | **YES — add ~6 checks** (§5) |
| DIRT-3 `dirt_robbery_fallout` / `dirt_sitting_on_hot_goods` (`dirt_underworld_events.json:870-915`) | gate `flag_active robbery_bust` / `flag_active hot_goods` — **target-agnostic engine flags** | **No — zero narrative impact** (§7.1) |

**The exhaustive edit surface is therefore: two lines of resolver data (`RobberyHustle.cs`), one UI button (`RobberyScreen.{tscn,cs}`), and a block of harness checks (`Program.cs`).** Everything else consumes the target through a `TargetProfile` it never has to know the identity of. This was confirmed by reading each call site, not assumed — the task's "confirm, don't assume" mandate.

**Two collisions checked and cleared:**

1. **The `_ =>` throw arm in `Target(...)` (`:191`) stays correct.** It guards genuinely-invalid `(RobberyTarget)` casts. The new row is added *above* it (a normal `switch` arm), so `JewelryExchange` is handled and the default still throws for out-of-range bytes. No change to the arm.
2. **`ApproachProfile.Crew` still requires a resolved crew rep**, unchanged (`ChooseApproach` `:253-256`). The new target is orthogonal to the crew gate — a crew job on the jewelry exchange resolves through the identical `HasCrew` path. Nothing to reconcile.

---

## 3. Where the new target sits in the ladder — the derivation (show the work)

### 3.1 The three profile numbers, derived (not guessed)

The shipped ladder and its internal patterns:

| Target | BaseScore | Difficulty | BaseHeat |
|---|---|---|---|
| ConvenienceStore | 350 | 0.20 | 3 |
| BookieStash | 900 | 0.40 | 6 |
| Warehouse | 1800 | 0.60 | 10 |
| **JewelryExchange (proposed)** | **3600** | **0.70** | **15** |

**BaseScore = 3600.** Score ratios climb `900/350 = 2.57`, then `1800/900 = 2.00` — decelerating to a clean doubling. Continuing at `×2.0` gives `1800 × 2 = 3600`: *the jewelry exchange is worth twice the warehouse.* This also sets the biggest bail stake on the board (`0.25 × 3600 = 900`, clamped to funds), which is exactly the "bigger job, bigger bail" intent of `BustLegalCostFrac` (`RobberyHustle.cs:152-160`).

**BaseHeat = 15.** Heat increments run `+3, +4` (`3→6→10`); continuing the arithmetic-of-increments (`+3, +4, +5`) gives `10 + 5 = 15`. Equivalently the ratios `2.0, 1.67` continue at `≈1.5 → 15`. Both readings agree on **15** — the cleanest-supported of the three numbers.

**Difficulty = 0.70 (a +0.10 step, not the naive +0.20 → 0.80) — this is the one number that needs defending.** The surface pattern `0.20→0.40→0.60` is arithmetic `+0.20`, which would suggest `0.80`. **Rejected, deliberately, because difficulty is double-counted across two rolls:** it enters `Execute` `pFail` at weight `1.0` *and* `Getaway` `pBotch` at weight `0.5` (`:268`, `:354`). A nominal `+0.20` difficulty step therefore compounds into an effective hazard step far larger than the same `+0.20` lower on the ladder, where the targets are easy enough that the getaway term never pushes a sensible policy into a −EV region. The decisive test is the **ladder invariant** every shipped target satisfies — *there exists a viable, positive-EV careful line* (store cased-solo-bail `+147`, bookie cased-solo-press `+397`, warehouse cased-solo-press `+417` / cased-solo-bail `+194`). Holding score at 3600 and sweeping difficulty (neutral avatar, cased, solo, bail):

| Difficulty | careful cased-solo-**bail** mean | verdict |
|---|---|---|
| 0.70 | **+130** | +EV — invariant holds, comfortable margin |
| 0.72 | +79 | +EV — thinner |
| 0.75 | ≈ +4 | breakeven |
| 0.80 | **−120** | **−EV — invariant broken; a specialist-only trap** |

At `0.80` the only non-negative neutral line is cased-solo-*press* at a marginal `+141` (at a 72% bust rate) — the target becomes a near-zero-EV activity that arrests the player ~72% of the time, i.e. a trap rather than a top rung. **`0.70` is the value that keeps the new rung on the playable side of the frontier while still stepping the tail up.** The `+0.10` nominal step *is* the principled continuation once you account for difficulty's double weight; `0.80` was considered and rejected on the invariant. (This mirrors the harness's own precedent of flagging a nominal table value that its own formula contradicts — cf. `Program.cs:342-346`, the Narcotics `B=1000`-vs-cap note.)

### 3.2 The neutral-input roll table (derived from the shipped formulas)

Neutral avatar = `RobberyCtx()` (funds 5000, `Heat 0.20`, `Reck 0.5`, `hasCrew true`) — the harness's canonical calibration point (`Program.cs:776-777`). Substituting into the three formulas: execute/press context term `= 0.15·0.20 − 0.10·0.5 = −0.02`; getaway context term `= 0.10·0.20 = +0.02`; cased bonus `= −0.12` (execute/getaway), `−0.06` (press). With `Difficulty = 0.70`:

| Approach | Execute success (cased) | Execute success (uncased) | Press-luck bust | Getaway botch (cased) |
|---|---|---|---|---|
| SoloQuiet | 1−0.56 = **0.44** | 1−0.68 = **0.32** | 0.17 (cased) / 0.23 | 0.40 |
| StrongArm (+0.12) | 1−0.68 = 0.32 | 1−0.80 = 0.20 | 0.29 (cased) / 0.35 | 0.60 (cased) / 0.67 |
| Crew (+0.06) | 1−0.62 = 0.38 | 1−0.74 = 0.26 | 0.23 (cased) / 0.29 | 0.55 (cased) |

*(Press-luck bust is target-independent by construction — `ComputePressLuckBustProbability` never reads the target — so these match the warehouse/bookie press odds at the same approach/ctx. That is a property to assert, §5 check 6.)*

FullScore = `BaseScore × ScoreMult`: Solo `3600` (grab-partial `1800`), StrongArm `4860` (`2430`), Crew `6120` (`3060`, player take `×0.60`). Bust legal fee `= 0.25 × 3600 = 900` (clamped to funds).

### 3.3 Does `ApproachProfile` (SoloQuiet / StrongArm / Crew) need any change? — **No. Verified.**

`ApproachProfile` and the `Approach(...)` switch (`:194-226`) are keyed **solely** on `RobberyApproach` and are read side-by-side with `TargetProfile` in every formula (`ScoreMult` in `ChooseApproach`; `BustAdd` in execute + press; `GetawayAdd`/`HealthAtRisk`/`TakeShare` in getaway/bust). Adding a target row changes none of those inputs. The strong-arm health hit (`Bust`, `:387-388`) is `−round(6 + 8·Reck)` — a function of `Reck` and approach only, **target-independent**, so it needs no per-target tuning either. The three approaches apply to the jewelry exchange exactly as they do to the other three marks, with no new hazard curve. **Zero approach change; disclosed and verified either way per the ask.**

### 3.4 The resulting policy bands vs. the shipped three (the frontier extension)

Full EV derivations for the three named policies on the new target (neutral avatar; `U` uniform per `RngState`; bust when `U ≥ success` on execute, `U < pBust` on press, `U < pBotch` botches the getaway):

- **Careful — `JewelryExchange, cased, SoloQuiet, bail`:** `EV = 0.44·(0.60·1800 + 0.40·900) + 0.56·(−900) = 0.44·1440 − 504 = +129.6`; **bust 56%.**
- **Committed — `JewelryExchange, cased, SoloQuiet, press`:** `EV = 0.56·(−900) + 0.44·0.17·(−900) + 0.44·0.83·(0.60·3600 + 0.40·1800) = −504 − 67.3 + 0.3652·2880 = +480.5`; **bust 63.5%.**
- **Reckless — `JewelryExchange, blind, StrongArm, press`:** `EV = 0.80·(−900) + 0.20·0.35·(−900) + 0.20·0.65·(0.33·4860 + 0.67·2430) = −720 − 63 + 0.13·3231.9 = −362.9`; **bust 87%** (plus a `health_ceiling` tail the funds-EV omits).

Placed on the shipped frontier (all recomputed from the same formulas; the warehouse-reckless figure matches the depth-pass's own stated "≈ −40 mean … ~81% bust," `§5.2`, validating the derivation method):

| Policy (neutral avatar) | Target | Mean funds | Bust rate |
|---|---|---:|---:|
| Cautious (cased, solo, bail) | ConvenienceStore | +147 | 6% |
| Moderate (cased, solo, press) | BookieStash | +397 | 39% |
| (Warehouse, cased, solo, press) | Warehouse | +417 | 55% |
| **Committed (cased, solo, press)** | **JewelryExchange** | **+480** | **63%** |
| Reckless (blind, strong-arm, press) | Warehouse | −35 | 81% |
| **(Jewelry, blind, strong-arm, press)** | **JewelryExchange** | **−363** | **87%** |

The **committed** jewelry line (`+480 / 63%`) is the clean next point on the reward-and-tail frontier — more money and more bust than both the bookie moderate and the warehouse press. The **reckless** jewelry line (`−363 / 87%`) is the deepest trap, strictly beyond warehouse-reckless on both axes. The **careful** jewelry line (`+130 / 56%`) preserves the ladder invariant (a viable +EV careful bag exists) while being deliberately thinner than the warehouse's `+194` — the encoded "commit or stay home" statement, because bailing pays the biggest execute-bust tax for only a half-score partial. This is the calibration §5 asserts and Fable proves.

---

## 4. UI wiring — `RobberyScreen` (grounded in the real node tree)

**The target picker is three explicit `Button` nodes, not a dropdown.** `CasePanel` (`RobberyScreen.tscn:47-68`) holds `CaseLabel`, then `StoreButton` / `BookieButton` / `WarehouseButton`, then the trailing `CaseItCheckBox`. Each button is cached in `_Ready()` (`.cs:82-85`) and wired to a lambda `OnTargetPicked(target)` (`.cs:108-110`). So a fourth target is a fourth button, mirroring the three verbatim:

1. **`RobberyScreen.tscn`** — add one `Button` node named `JewelryButton` under `Panel/Layout/CasePanel`, **after `WarehouseButton` and before `CaseItCheckBox`** (VBox order = display order; keep "case it first" as the trailing toggle). Text: diamond-flavored, in the shipped voice, e.g. `"Jewelry Exchange (the big score — diamonds)"`. (Player-facing copy lives in the scene per `ui_conventions.md`.)
2. **`RobberyScreen.cs`** — (a) one field `private Button _jewelryButton = null!;` beside the other three (`:46-48`); (b) cache it in `_Ready()`: `_jewelryButton = GetNode<Button>("Panel/Layout/CasePanel/JewelryButton");`; (c) subscribe: `_jewelryButton.Pressed += () => OnTargetPicked(RobberyTarget.JewelryExchange);` beside the three (`:108-110`).

**Match the existing subscription discipline exactly:** the three target buttons use lambda handlers subscribed in `_Ready()` and are **not** unsubscribed in `_ExitTree()` (only the non-lambda stage handlers are, `.cs:121-128`). The new button follows the identical pattern — one lambda subscription, no `_ExitTree` line — so it introduces no inconsistency. `OnTargetPicked`, `OnApproachPicked`, `Advance()`, and `ShowPanel()` are fully generic over the chosen target and need **zero** change; the run drives through the same panels.

**No `[Export]` / format-string change.** `TargetFormat` (`.cs:26-27`) is one reusable format applied to every target; `{0}` renders the enum's `ToString()`, so the approach-panel label shows `"JewelryExchange — …"`. That PascalCase display is a **pre-existing cosmetic seam shared by all three shipped targets** (they already render `"ConvenienceStore"` etc. there) — consistent, not worsened, and explicitly out of scope (§8). The nice display name lives only on the button `text` we author in the scene.

**Verify-before-wiring (mandatory).** `ui_conventions.md` and the screen's own header comment (`.cs:21-22`, *"Node paths verified against RobberyScreen.tscn (godot_scene_mapper) before this script was written"*) require confirming the new `Panel/Layout/CasePanel/JewelryButton` path via `godot_scene_mapper` (or the Godot MCP) **before** writing the `GetNode<Button>` call.

---

## 5. HustleHarness calibration — new checks (with derived expected numbers)

Add a block to `RunRobberyChecks` (`Program.cs:806-1052`), in the established style, reusing the existing `Band(target, caseIt, approach, alwaysPress, seedBase)` helper (`:1007-1024`) and the already-computed `moderate` / `reckless` band variables (`:1026-1028`). Give the new bands independent seed bases (e.g. `40_000_000UL`, `50_000_000UL`, `60_000_000UL`). The harness total is printed dynamically from `Results.Count` (`:44`) — **there is no hardcoded count literal to bump** (unlike `GrittyEventsHarness`); adding ~6 checks simply moves the printed tally (currently 86 post-F-2) to ~92.

1. **Ladder monotonicity extends to the top rung (point check).** At neutral ctx, uncased solo-quiet: `ComputeExecuteSuccessProbability(JewelryExchange) < …(Warehouse)` **and** jewelry `FullScore > warehouse FullScore`. *Derived:* success `0.32 < 0.42`; score `3600 > 1800`. (Extends the shipped store-vs-warehouse check at `:995-998`.)
2. **The careful line is still +EV (the headline invariant).** `Band(JewelryExchange, caseIt:true, SoloQuiet, alwaysPress:false)` → `MeanFunds > 0`, with `BustRate ∈ [0.50, 0.62]`. *Derived:* mean `+130`, bust `56%`. This is the proof that the new top rung keeps a viable careful bag.
3. **The committed line extends the frontier beyond bookie (relative check).** `Band(JewelryExchange, caseIt:true, SoloQuiet, alwaysPress:true)` → `MeanFunds > moderate.MeanFunds` **and** `BustRate > moderate.BustRate`. *Derived:* `+480 > +397` and `63.5% > 38.6%` — more reward, more tail. (Referencing `moderate` means any regression in the shipped bookie band fails this too.)
4. **The reckless jewelry ceiling is the deepest trap (relative check).** `Band(JewelryExchange, caseIt:false, StrongArm, alwaysPress:true)` → `MeanFunds < reckless.MeanFunds` **and** `BustRate > reckless.BustRate`. *Derived:* `−363 < −35` and `87% > 81%`.
5. **The bigger score sets the biggest bail (point check).** Force a jewelry execute/press bust (via `FindSeed`); assert `FundsDelta == −(0.25 · 3600) = −900` at funds 5000, and `== −100` at `RobberyCtx(funds:100)` (funds-clamp). *Mirrors the shipped bookie bust-fee checks at `:879-892`.*
6. **The clean-getaway heat is the target's `BaseHeat` (point check).** Drive a cased jewelry run to a clean getaway (`FindSeed` on no-botch); assert `DetectionRiskDelta == CaseHeatCost(2) + 15 == 17`. *Mirrors the shipped bookie clean-getaway check at `:902-905` (which asserts `CaseHeatCost + 6`).*
7. **(Optional, cheap) Press-luck stays target-independent.** Assert `ComputePressLuckBustProbability` is equal for a jewelry vs. a warehouse `RobberyState` at the same approach/ctx/cased — documents that the press beat did not change with the new target.

**No-regression is structural, and re-proven.** Because the slice adds an enum value + one switch row + new checks — and modifies **no** existing constant, target row, approach row, or formula — the shipped store/bookie/warehouse bands are byte-identical; the existing `cautious`/`moderate`/`reckless` checks (`:1035-1051`) recompute the same numbers and must still print unchanged. The re-run is the proof (§6).

---

## 6. Gates & acceptance (Fable's review)

Pure mechanical add — no schema, no narrative, no Baseball touch. Gate set (mirrors `hustle_minigames_depth_pass.md` §9 and the DIRT-3 §7 discipline):

1. **`dotnet build` 0/0.** A malformed switch/enum fails the compile loudly.
2. **`HustleHarness` green** (`dotnet run --project Tools/HustleHarness`): the ~6 new checks pass at their derived bands, **and** the shipped `cautious`/`moderate`/`reckless` robbery checks + the printed band values are **unchanged** (byte-identical) — the no-regression proof. Determinism (`:1111-1112`) and zero-alloc (`:1216-1218`) robbery checks still pass (the record-struct add allocates nothing new).
3. **Scoped diff.** `git diff --stat -- Assets/Simulation` shows **only** `Assets/Simulation/Hustles/RobberyHustle.cs`, and only the two additive lines (enum value + switch row). `git diff --stat -- Assets/Simulation/Baseball` **empty** — `run_monte_carlo_batch` is not mandated (Baseball is untouched by construction) but if run, MLB guard byte-exact 345/345.
4. **Live boot smoke test** (godot MCP) on the real save: arm a `Robbery` Work session, confirm the **fourth** target button renders in `CasePanel` and drives a jewelry-exchange run through case → approach → execute → (press/bail) → getaway → result end-to-end, `Done` applies via `ApplyRobberyResolution`, errors empty. Node path `Panel/Layout/CasePanel/JewelryButton` verified via `godot_scene_mapper` before the `GetNode` was written.
5. **`SchemaValidator` / `GrittyEventsHarness` / `NeedsDecay` / `CoreLoop` untouched** — no DDL, no content, no needs/loop math in this slice (§7).

---

## 7. Boundary disclosures (carry into the build header)

1. **Zero DIRT-3 narrative impact — verified.** `dirt_robbery_fallout` gates `flag_active robbery_bust` (`dirt_underworld_events.json:877-879`) and `dirt_sitting_on_hot_goods` gates `flag_active hot_goods` (`:909-911`) — **target-agnostic engine flags** written by *any* robbery through the same `RobberyState.ToResolution()` → `ApplyRobberyResolution` → `ApplyCore` path (`HustleService.cs:278-297`). No DIRT-3 event reads *which* target was hit. A jewelry-exchange bust arms `robbery_bust` and a clean jewelry getaway arms `hot_goods` identically to a convenience-store run, so the arrest-absence teeth and the fence nudge fire unchanged — **for free, no JSON edit.** Zero narrative-layer touch.
2. **Zero Narcotics / Fencing / Hold'em touch.** The change is confined to the Robbery resolver's two data lines, the Robbery screen's one button, and the Robbery harness block. No other resolver, screen, context builder, or harness section is edited. (The `hot_goods` cross-hook into Fencing's `FreshFromAJob` is *engine behavior that already exists* — the new target participates in it via the shared flag, without any Fencing-side change.)
3. **Zero schema / database touch — no migration.** `RobberyTarget` is an in-memory `enum … : byte` chosen at runtime in the Case stage and never persisted — it is not a column, not a `Game_State` KV, not an `Entity_Flag`. The only thing that reaches SQLite is the target-agnostic `HustleResolution` (funds/detection/health/reckless + boolean flags); the DB never learns the target's identity. No `SchemaDefinitions.sql` change, no `PRAGMA user_version` bump, no `SchemaValidator` delta. `git diff --stat -- Assets/Data/Database` empty.
4. **Strictly one new target — no scope creep.** No second target, no new `RobberyApproach`, no formula/constant retune, no new `HustleResolution` field, no audio/motion. The three shipped target rows and all shipped constants are untouched.
5. **Pre-existing cosmetic seam left as-is.** All four targets render their PascalCase enum name in the approach-panel `TargetFormat` label; a friendlier per-target display name is a pre-existing polish item shared by the shipped three, not introduced or fixed here (§8).

---

## 8. Model split & follow-ons

- **Opus 4.8 (this pass):** this spec.
- **Builder — Fable 5 (recommended), Sonnet 5 acceptable for the UI button.** Per `hustle_minigames_depth_pass.md` §10 (*"Layer-1 math (calibration-sensitive) … = Fable 5; UI-only sub-slices = Sonnet 5"*), the load-bearing part of this slice is the **calibration** (the three profile numbers land the §3 bands), so **Fable 5** should own the resolver row + the harness checks. The UI button is trivial and could be Sonnet's if splitting, but the whole slice is small enough for one model. Concretely the builder: (a) adds `JewelryExchange` to `enum RobberyTarget` and the `TargetProfile(3600, 0.70, 15)` row to `Target(...)`; (b) adds the `JewelryButton` node + field + `GetNode` + lambda subscription to `RobberyScreen.{tscn,cs}` (verify the node path first); (c) adds §5's ~6 harness checks; (d) runs `HustleHarness`.
- **Fable 5 (review):** the §6 gate set + the standing build review — confirm the two resolver lines are the only `Assets/Simulation` change, the shipped three bands print byte-identical, the new bands match §3.4's derivations, and the live boot drives a jewelry run end-to-end.
- **Deferred (not this slice):** a friendlier per-target display name for the approach label (pre-existing, all four targets, §7.5); on-screen odds via the public `Compute*Probability` methods (the depth-pass R-3/R-4 polish seam, already disclosed there); any *further* target or a new approach type (explicitly out of scope). The new target inherits the DIRT-3 `robbery_bust` arrest teeth and the `hot_goods`→fence economy with **zero** additional wiring — the payoff of the target-agnostic engine this slice deliberately does not disturb.
