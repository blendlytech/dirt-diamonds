# Design — Markov Micro-Simulation (Interactive At-Bat / Pitch-Level Model)

**Author:** Claude Opus 4.8 (statistical/mathematical design) · **Phase:** 4 (Baseball Micro-Sim, "when the player's avatar is in-game") · **Status:** design complete; no C# written yet.

This is the mathematical specification for the **micro-sim** — the model that runs when the player's own avatar is batting (or pitching) in a live game, exposed through the interactive at-bat scene. It is the companion to [`baseball_pa_outcome_model.md`](baseball_pa_outcome_model.md) (the macro-sim / background-league model) and is written to be read alongside it. Per `.claude/rules/baseball_engine.md §2`, the micro-sim tracks **exactly 25 base-out states per half-inning** and drives them with **per-event 25×25 transition matrices**, blending the player's real-time **timing/location inputs** with their database attributes.

The single most important constraint, from the Phase 4 plan (`docs/progress.md`) and `.claude/rules/baseball_engine.md`:

> **Micro↔macro consistency** — a micro-simmed game's aggregate line must converge to the macro model's probabilities for the same ratings. **Shared calibration tables, not duplicated constants.**

Everything below is engineered so that this convergence is a **provable identity under neutral input**, not a calibration coincidence. The micro-sim does **not** own a second copy of the outcome math: it *consumes* `AtBatResolver.ComputeProbabilities` (macro doc §4) as its ground truth and layers pitch-level interactivity and pitcher fatigue on top, both routed back through that same resolver. This document is a **spec, not code** — `Assets/Simulation/Baseball/` gains the micro-sim classes next session (Fable 5), against the tables and contracts fixed here.

Like the macro doc: **every constant here is a calibration knob, not a law.** The acceptance target (§11) is verified by the harness behind `run_monte_carlo_batch`; tune the tables, never hard-code magic numbers in the resolver.

---

## 1. Scope — what the micro-sim is, and which pitches it actually simulates

The macro-sim resolves an entire background-league PA to one terminal outcome with a single draw (macro doc §7 wraps it in a cheap base-out loop for runs). The micro-sim is invoked only for the **player-attended game** — the one game per day the human is playing in — and adds two things the macro-sim deliberately omits:

1. **Pitch-by-pitch interactivity** for the human's own plate appearances: a ball/strike count that evolves, with the human supplying **swing timing** and **location** each pitch (`.claude/rules/baseball_engine.md §2`, "blend player inputs (timing/location)").
2. **Pitcher stamina / fatigue** (§8) — where `Player_Ratings.pit_stamina` and the PED `1.5×` stamina multiplier finally bind — driving late-game hittability and, eventually, bullpen decisions.

**Not every PA in the attended game is pitch-simulated.** That would be wasteful and pointless — the human only bats ~4–5 times in a 9-inning game. The rule:

| PA type | Resolution path | Interactive? |
|---|---|---|
| Human is the batter | **Pitch layer (§5–§6)** → terminal outcome | Yes |
| Human is the pitcher (if the avatar pitches) | **Pitch layer**, human on the mound side | Yes |
| Both sides are NPCs (the other 17 lineup slots) | `AtBatResolver.Resolve` directly, **with the fatigue-adjusted pitcher ratings of §8** | No |

So fatigue governs the *entire* attended game uniformly (it is a pitcher-side effective-ratings adjustment applied whether or not the PA is interactive), while the pitch chain is spun up only for the handful of PAs the human personally contests. This keeps the interactive surface tight and the zero-GC hot path (the NPC PAs) identical to the macro-sim's.

**Two nested layers, one engine.** The micro-sim is structurally two **absorbing Markov chains**, one inside the other, analysed with the *same* fundamental-matrix tool (§4):

- **Outer chain — base-out states** (§2–§3): 25 states per half-inning; one transition per completed PA. This is the chain `.claude/rules/baseball_engine.md §2` mandates and the formal generalization of the ad-hoc base-out machine already in `LeagueSimulator.PlayHalfInning`.
- **Inner chain — the pitch/count** (§5): 12 count states + 3 absorbing exits (BB / K / ball-in-play); one transition per pitch. This is where timing/location input enters and where fatigue accrues.

The inner chain produces the **event** that drives one outer-chain transition. The composition is exact and is the backbone of the consistency proof (§7).

---

## 2. The 25 base-out states (outer chain)

A half-inning is an absorbing Markov chain over base-occupancy × outs.

- **Base occupancy** is 3 bits, identical to the macro base-out machine (`LeagueSimulator`: bit 0 = runner on 1B, bit 1 = 2B, bit 2 = 3B) → `bases ∈ 0..7`.
- **Outs** ∈ `{0, 1, 2}`.
- 8 × 3 = **24 transient states**, plus **1 absorbing state** = the third out (inning over) → **25 states total.** This is the canonical sabermetric formulation (Tango *et al.*, *The Book*).

**Canonical index.** `state = bases * 3 + outs` for the 24 transient states (`0..23`); `state = 24` is the absorbing "3 outs" state. State 0 = *0 outs, bases empty* (matching the rules doc's "State 1: 0 outs, empty bases", 1-based in prose). Full enumeration:

| Bases (bits) | Occupancy | outs=0 | outs=1 | outs=2 |
|---|---|:--:|:--:|:--:|
| 000 | empty        | 0  | 1  | 2  |
| 001 | 1B           | 3  | 4  | 5  |
| 010 | 2B           | 6  | 7  | 8  |
| 011 | 1B,2B        | 9  | 10 | 11 |
| 100 | 3B           | 12 | 13 | 14 |
| 101 | 1B,3B        | 15 | 16 | 17 |
| 110 | 2B,3B        | 18 | 19 | 20 |
| 111 | loaded       | 21 | 22 | 23 |
| —   | **3 outs (absorbing)** | | **24** | |

Every half-inning starts in state 0 and runs until it is absorbed in state 24. **Runs scored are not part of the state** — run production is a reward accumulated *on the transitions* (a batter reaching or clearing bases), exactly as the macro `PlayHalfInning` already does. This keeps the chain finite (25 states) instead of unbounded (25 × score).

---

## 3. Per-event advancement matrices `A_event` and the composed transition matrix

`.claude/rules/baseball_engine.md §2`: *"Form a 25×25 transition matrix for each potential event."* We do — and they are **constant, sparse, and precomputed once**, never rebuilt per PA (zero-GC).

### 3.1 The events

The event set is the macro `PaOutcome` enum (`Out, Strikeout, Walk, Single, Double, Triple, HomeRun`) — **the same seven, shared, not re-enumerated.** Each event `e` defines a mapping from the current base-out state to a **distribution over successor states**, i.e. a 25×25 row-stochastic matrix `A_e`. The advancement rules are the macro doc §7 rules the codebase already implements in `LeagueSimulator.AdvanceWalk / AdvanceSingle / AdvanceDouble` and the inline Triple/HR/Out logic — reused verbatim, not re-specified:

- **`Out`, `Strikeout`** — `outs += 1` (to state 24 if it was the third), bases unchanged. `A` is a deterministic permutation (0/1 rows).
- **`Walk`** — batter to 1B, **force-only** advancement (a runner advances iff the base behind is occupied). Deterministic.
- **`Triple`** — batter to 3B, all runners score. Deterministic.
- **`HomeRun`** — bases cleared, batter + all runners score. Deterministic.
- **`Single`, `Double`** — deterministic base advances **plus** one discretionary runner decision each, governed by the shared knobs `LeagueSimulator.SingleScoresFrom2nd = 0.60` and `DoubleScoresFrom1st = 0.45`. Because a runner from 2B on a single either scores (leaves the base map) or stops at 3B (stays in it), the *successor base-out state* is genuinely random → `A_Single` and `A_Double` are **stochastic** (rows with two non-zero entries split `p` / `1−p` by the knob). This is the only source of stochasticity in the outer transition matrices.

Out/K also branch on whether they record the third out (→ state 24), which the matrix captures naturally.

### 3.2 The composed per-PA transition matrix

For a given batter/pitcher/defense matchup, let `p = (p_Out, …, p_HomeRun)` be the seven terminal-outcome probabilities. The **one-PA base-out transition matrix** is

```
T(matchup) = Σ_e  p_e · A_e            (a 25×25 row-stochastic matrix)
```

This `T` is exactly the object `.claude/rules/baseball_engine.md §2` calls for. **Its `p_e` come from the shared macro resolver** — for an NPC PA, `p = AtBatResolver.ComputeProbabilities(...)`; for a human PA, `p` is the pitch layer's terminal distribution (§5–§7), which is *also* anchored to that resolver. The `A_e` are matchup-independent constants. So the only per-matchup work is the seven-vector `p`; `T` is never materialized in the hot path.

### 3.3 Runtime vs. analytic representation (zero-GC)

- **Runtime (hot path):** apply the sparse advancement *function* to the current `(bases, outs)` — one `switch` on the drawn event, integer base-bit ops, one RNG draw for the Single/Double discretionary split. Identical in cost to the macro `PlayHalfInning`. **No matrix is allocated or multiplied.**
- **Analytic (offline / validation / AI):** the constant `A_e` matrices and the composed `T` are materialized *once* (outside any per-PA loop) for §4's run-expectancy and leverage tables and for the harness's consistency proofs. The two representations are provably equivalent — the advancement function **is** the sparse matrix.

The advancement function is the single source of truth; the matrices are generated from it in a one-time builder, so they can never drift apart.

---

## 4. Run expectancy & leverage from the absorbing chain (one shared analytic engine)

Both nested chains (base-out, and the pitch/count chain of §5) are **absorbing Markov chains**, so both are analysed with the same standard tool. Partition the transition matrix into transient (`Q`, transient→transient) and absorbing blocks. The **fundamental matrix**

```
N = (I − Q)^{-1}
```

gives `N_ij` = expected visits to transient state `j` starting from `i`. From it:

- **Expected PAs per half-inning**, and per-state **run expectancy** `RE(state)` — the expected runs scored before absorption from each of the 24 transient base-out states, given a matchup's `T`. For the league-average matchup this reproduces the familiar RE24 table (~0.48 runs in state 0), a direct calibration check against real baseball.
- **Leverage index** for any state/transition — how much a given base-out transition swings win probability. This is computed once per matchup, not per pitch.

Why this matters beyond validation:

- **The pitch/count chain (§5) is solved the same way** — its absorption probabilities `(P_BB, P_K, P_BIP)` are read directly off *its* fundamental matrix. One piece of math, two uses.
- **Leverage is the "how big was this moment?" signal** the Gritty Event system (Phase 7) will want — a walk-off vs. a mop-up at-bat differ by leverage, and that scalar can feed narrative stress/`Entity_Flags` weighting later. Designing it in now (as a pure read-off of `N`) costs nothing and avoids a bolt-on.

This section is analytic scaffolding; it allocates nothing in the per-pitch or per-PA hot path.

---

## 5. The pitch / count chain (inner chain) — human PAs only

When the human is batting (or pitching), the PA is played pitch by pitch. The state is the **count** `(b, s)`, `b ∈ {0,1,2,3}` balls, `s ∈ {0,1,2}` strikes: 12 transient states plus three absorbing exits:

```
  b=4  → Walk (BB)
  s=3  → Strikeout (K)          (a 2-strike foul does NOT advance to s=3)
  ball in play → BIP            (hands off to §5.3 for the hit/out type)
```

Start state `(0,0)`; one transition per pitch.

### 5.1 Per-pitch outcome classes

Each pitch resolves to exactly one of: **ball**, **called/swinging strike**, **foul** (strike only if `s < 2`; otherwise count-neutral), or **in play**. The per-pitch probabilities are a function of `(count, pitcher ratings, batter ratings, player input)`, built from the *same log-linear machinery* as the macro model (a base rate modulated by `exp(k · Σ w·deviation)`) — **shared mathematical DNA, coefficients tuned by the harness.** The pitch sequence is generated by two sub-decisions:

- **Pitcher target** — throw *in-zone* vs. *chase-zone*, with a zone probability `zoneProb(count)` that falls when ahead (bait) and rises when behind (must throw a strike). `pit_control` raises the probability the pitch lands where intended (fewer non-competitive balls, fewer mistakes in the zone); `pit_stuff` raises whiff and weak-contact rates. Fatigue (§8) enters here by degrading the pitcher's *effective* `pit_stuff`/`pit_control`.
- **Batter decision** — swing vs. take, and if swinging, the **timing/location quality** (§6). `bat_discipline` improves take/swing correctness (chase less out of zone, punish strikes); `bat_contact` reduces whiffs and fouls-into-outs; `bat_power` raises the in-play contact-quality ceiling.

Fouls are modeled as a **length-only refresh** at two strikes (they extend the PA, ~3.8–4.0 pitches/PA target, without changing which absorbing exit is reached), so foul rate `φ` is tuned to pitch-count realism independently of the outcome distribution.

### 5.2 The absorption-matching constraint (the consistency hinge)

The inner chain is a small absorbing Markov chain, so its three absorption probabilities `(P_BB, P_K, P_BIP)` are an analytic function (via §4's `N`) of the per-pitch class probabilities. **We pin them to the macro model:** at PA start compute the anchor

```
p* = AtBatResolver.ComputeProbabilities(effectiveBatter, effectivePitcher, fielding)   // shared macro tables
```

and require the neutral-input count chain to satisfy

```
P_K  = p*[Strikeout]
P_BB = p*[Walk]
P_BIP = p*[Out] + p*[Single] + p*[Double] + p*[Triple] + p*[HomeRun]   ( = 1 − P_K − P_BB )
```

Because the count chain has only 12 transient states, the per-pitch class probabilities `(π_ball, π_strike, π_inplay)` that yield a target `(P_BB, P_K, P_BIP)` are recovered by a cheap precomputed inversion (or Newton step) of the absorption equations, with foul rate `φ` fixed separately for pitch-count realism. **Under neutral input the inner chain is, by construction, an exact sampler of the macro walk/strikeout/in-play split.** Human skill (§6) perturbs `(π_ball, π_strike, π_inplay)` away from this neutral solve — better plate discipline shifts probability from K toward BB and BIP — bounded by clamps so a human can neither guarantee nor forfeit an outcome.

### 5.3 Ball-in-play → hit/out type

On the `BIP` exit, the specific outcome is drawn from the **macro BIP-conditional distribution** — `p*` renormalized over its five in-play buckets `{Out, Single, Double, Triple, HomeRun}` — with the batter's effective **power/contact perturbed by the swing's contact-quality `q`** (§6): a well-barreled ball nudges the split toward `Double`/`HomeRun`; weak contact toward `Out`. Under neutral contact (`q = 0`, no perturbation) this is *exactly* the macro BIP split.

Combining §5.2 and §5.3: **neutral input ⇒ (walk/K/BIP class split) = macro AND (BIP-conditional split) = macro ⇒ the full 7-way terminal distribution ≡ `AtBatResolver.ComputeProbabilities`.** That identity is the §7 consistency theorem, true by construction rather than by tuning.

---

## 6. Player input model (timing & location)

Two continuous inputs per pitch, both defined so that **neutral (league-average) play is the zero point**:

- **Timing** — signed error `τ` between the swing trigger and the ideal contact instant (early `< 0`, on-time `0`, late `> 0`), normalized to `[−1, +1]`. Maps to a **contact-quality** scalar `q ∈ [−1, +1]` peaked at `τ = 0`, e.g. `q = (1 − (τ/τ_tol)²)` clamped, where `τ_tol` narrows as `pit_stuff` rises (nastier stuff = smaller barrel window). `q` feeds §5.3's power/contact perturbation and the whiff/foul rates in §5.1.
- **Location** — the batter's guessed zone cell vs. the pitch's true cell (a 3×3 in-zone grid + a chase ring). A correct read widens the timing tolerance and raises swing quality; a wrong read on a chase pitch raises whiff/weak-contact. `bat_discipline` shrinks the penalty for laying off a true ball.

### 6.1 The neutral autopilot policy (headless determinism & the consistency anchor)

The consistency proof (§7) and every headless harness run (§11) require a **reference batter/pitcher policy** that stands in for the human and, by definition, reproduces the macro line. The **neutral policy** chooses take/swing and timing/location error whose *aggregate* per-pitch class probabilities are precisely the §5.2 neutral solve for the current ratings — i.e. it plays "exactly to the ratings." A human who out-times and out-reads the neutral policy beats their card; one who chases and mistimes underperforms it. The neutral policy is what runs in `run_monte_carlo_batch`, so micro-sim validation is fully deterministic (seeded `RngState`) and needs no human in the loop.

Timing/location and the neutral policy are **micro-sim-only** concepts; they do not exist in and never leak into the macro-sim.

---

## 7. Micro↔macro consistency contract

**Theorem (consistency by construction).** For any ratings, with the **neutral policy** (§6.1) and **full stamina** (`m = 1`, §8), the micro-sim's per-PA terminal-outcome distribution equals `AtBatResolver.ComputeProbabilities(batter, pitcher, fielding)` — and therefore a full micro-simmed game's aggregate batting/pitching line converges to the macro-sim's for the same rosters.

*Proof sketch.* §5.2 pins the walk/K/in-play split to `p*`; §5.3 pins the in-play-conditional split to `p*`; the product is `p*` itself, which is the macro resolver's output. The outer base-out chain (§3) reuses the *same* advancement functions and the *same* `SingleScoresFrom2nd`/`DoubleScoresFrom1st` knobs as the macro `PlayHalfInning`, so identical event streams produce identical run production. ∎

**What legitimately diverges** (the value the micro-sim adds, all routed through shared tables):

1. **Human skill** — timing/location better or worse than neutral shifts `p` via §5–§6, bounded by clamps. This is *supposed* to move outcomes; it is the game.
2. **Pitcher fatigue** (§8) — degrades effective `pit_stuff`/`pit_control`, so `p*` itself drifts hittable over a start. The macro-sim uses complete games at full stuff; the micro-sim's fresh-pitcher, neutral-policy limit recovers the macro line, and fatigue is the deliberate, principled departure from it.

**Bookkeeping note — HBP & IBB.** The macro `Walk` bucket folds in HBP (macro doc §1) and omits intentional walks. The micro-sim *may* surface **HBP** (a rare location-miss hit-batsman) and **IBB** (a pitcher/manager decision in a high-leverage, first-base-open state — the intentional walk the macro doc explicitly deferred here) for a richer box score and play-by-play. Both still produce the **`Walk` base-out transition** (batter to 1B, force-only advances) and are **aggregated into `p*[Walk]` for every consistency check**, so distinguishing them enriches `Game_Logs` without breaking the calibration anchor.

---

## 8. Pitcher stamina & fatigue — where `pit_stamina` and the PED stamina hook bind

This is the micro-sim's headline new mechanic and the reason `Player_Ratings.pit_stamina` exists (macro doc §3 carries it unused; `PitcherRatings.Stamina` is already plumbed through `AtBatResolver`).

### 8.1 Stamina pool

A pitcher starts a game with a **pitch-capacity** derived from stamina:

```
capacity = STAMINA_BASE + STAMINA_SLOPE · effectiveStamina         // knobs; e.g. 70 + 0.5·rating
                                                                    // → rating 0→70, 50→95, 100→120 pitches
```

Pitch count `n` accrues by one per pitch (max-effort pitches may cost >1 — a later knob).

### 8.2 Non-linear fatigue curve

Freshness is flat through a **comfort fraction** `α` of capacity, then declines **non-linearly (accelerating)** — mirroring the life-sim's "decay accelerates near the floor" philosophy (`.claude/rules/life_sim_ai.md`). Let `x = n / capacity`:

```
m(x) = 1                                   for x ≤ α                    ( α ≈ 0.6 )
m(x) = 1 − FATIGUE_C · ((x − α)/(1 − α))²   for x > α, clamped ≥ M_MIN   ( e.g. m(1) ≈ 0.75, floor 0.5 )
```

`m ∈ [M_MIN, 1]` is the **fatigue multiplier.** (An optional additive "overextension" penalty past `x = 1` can pull effective stuff/control *below* league-average for a truly gassed arm — a knob, off by default.)

### 8.3 Fatigue flows through the shared resolver

Fatigue is applied as an **effective-ratings** adjustment, then fed to the *same* `AtBatResolver` — no bespoke "fatigue penalty on outcomes":

```
pit_stuff_eff   = 50 + (pit_stuff   − 50) · m(x)
pit_control_eff = 50 + (pit_control − 50) · m(x)
```

A tired ace's stuff/control decay toward average, so `p*` naturally yields more hits/walks/HR — the late-inning hittability every baseball fan expects, emerging from the shared tables rather than a magic number. This governs **both** interactive human PAs and the NPC PAs resolved by `AtBatResolver.Resolve` (§1), so the whole attended game reflects the starter tiring.

### 8.4 PED stamina hook

Per `.claude/rules/baseball_engine.md` "PED Modifiers" (a temporary **1.5×** to power **and stamina**), and symmetric with the macro doc §6 power hook:

```
effectiveStamina = min(100, round(pit_stamina · 1.5))   when Entity_Flags 'ped_active' is set
```

applied to `pit_stamina` *before* §8.1's capacity. A juiced arm gets a deeper pool and fatigues later. Example: `pit_stamina 70` → capacity `70 + 0.5·70 = 105`; on PEDs `min(100, 105) = 100` → capacity `70 + 0.5·100 = 120` — ~15 extra pitches at strength before the same fatigue fraction. The batter power `1.5×` hook is unchanged and already lives in `AtBatResolver` (macro §6).

**Post-game costs — reuse the macro constants, do not duplicate.** After a game a flagged pitcher (or batter) appeared in, the same `LeagueSimulator.PedHealthCostPerGame` / `PedDetectionRiskPerGame` deltas apply to `health_ceiling` / `detection_risk` (macro §6). The flag source is `Entity_Flags(flag_name = LeagueSimulator.PedActiveFlagName)`, written by the Gritty Event system (Phase 7); until then it is always inactive and every PED hook is a no-op — the signatures exist so nothing has to be retrofitted.

### 8.5 Bullpens — deferred extension (needs a v4 schema step)

Fatigue creates the *decision* to pull a starter, but the current roster is `9 lineup + 5 rotation` (`LeagueSimulator.RosterSizePerTeam = 14`) with **no relievers**, and `Player_Ratings` has only an `is_pitcher` bit — no starter/reliever role, no per-pitch-type arsenal. Real bullpen substitution therefore wants a schema change (a `pitcher_role` column and/or a roster/lineup table, plus possibly pitch-type ratings) and must go through the **No Blind Queries** validation path (`.claude/rules/database_rules.md`) as **schema v4 before any dependent C#.** Phase 4's vertical slice runs on **v3 as-is**: complete-game starters whose *effectiveness* decays via §8.3. Bullpens are the first follow-on once the interactive slice is proven.

---

## 9. Determinism, zero-GC & implementation contract

Binding requirements on the Fable 5 implementation (from `CLAUDE.md §1` zero-GC mandate, `.claude/rules/baseball_engine.md`, and the macro doc §9, which this inherits):

- **Reuse, don't re-implement.** The terminal-outcome math is `AtBatResolver.ComputeProbabilities` (shared tables). The base-out advancement is the macro §7 logic already in `LeagueSimulator` — extract the advancement functions to a shared, testable location both sims call, rather than copying them.
- **Struct state, `stackalloc`, `ref RngState`.** The count state `(b, s)`, base-out state, and per-pitch working vectors are stack/struct; the `A_e` matrices and any `(I − Q)^{-1}` work are built **once** at load, never per pitch/PA. One `RngState` (macro §9's xoshiro256\*\* struct) threads the whole game by `ref`; the neutral policy consumes it so headless runs are bit-for-bit reproducible.
- **No per-pitch allocation.** A simulated pitch touches only preallocated arrays and the stack — same standard the macro-sim already meets.
- **PED-aware signatures.** Batter power `1.5×` (already in `AtBatResolver`) and the new pitcher stamina `1.5×` (§8.4) are applied *inside* the resolver/fatigue layer from the carried flag, so callers can't forget them.
- **The human is an input stream, the neutral policy is its headless stand-in.** The pitch resolver takes an abstract "batter action / pitcher action" the UI (§12) supplies for the human and the neutral policy (§6.1) supplies for tests and NPCs.

Illustrative shape (not prescriptive):

```csharp
public enum PitchClass : byte { Ball, Strike, Foul, InPlay }          // inner-chain per-pitch event

public readonly struct CountState { public readonly byte Balls, Strikes; }

public struct AtBatContext                 // one live human PA
{
    public CountState Count;
    public PitcherFatigue Fatigue;         // pit_stamina + PED → capacity, pitch count, m(x)
    // anchor p* recomputed as effective pitcher ratings drift with fatigue
}

// Neutral policy and human input both implement this; RngState drives the neutral one.
public interface IBatterPolicy { BatterAction Decide(in AtBatContext ctx, in PitchInfo pitch, ref RngState rng); }
```

`MicroGame` (the attended-game driver) owns: lineup/rotation load (reuse `BaseballQueries` roster load), the outer base-out chain, spinning up the inner pitch chain only for human PAs, applying §8 fatigue to every pitcher's effective ratings, accumulating the box score into the same `Batting_Stats`/`Pitching_Stats` counting-stat arrays and flushing them through the existing upsert path in **its own batch** (never the calendar tick's), and emitting play-by-play to `Game_Logs`. It reacts to the same bus events and never references the Life sim.

---

## 10. Play-by-play & `Game_Logs`

The micro-sim is the natural first writer of the `Game_Logs` table (schema v3, JSON `payload`). Each pitch/PA/scoring event appends a row via **pooled log writers** (`.claude/rules/ui_conventions.md` — pooled elements for anything spawned in volume; no per-frame string formatting). The at-bat scene renders this feed. Logging is **off the hot path** — payloads are built at PA boundaries, not inside the pitch loop's steady state — and league-level entries (final score) use the nullable `player_id`. No schema change: v3's `Game_Logs` already fits.

---

## 11. Calibration, validation & tuning order

**Harness:** extend `run_monte_carlo_batch` (or a sibling run behind the same skill) to drive the micro-sim under the **neutral policy** (`.claude/rules/baseball_engine.md` "Test Stat Matrices" + `CLAUDE.md` mandate to re-run after any resolver/PED change).

**Acceptance tests:**

| # | Test | Expectation |
|---|---|---|
| 1 | **Neutral consistency, per-PA** | Micro terminal distribution vs. `AtBatResolver.ComputeProbabilities` over the §5.2/§5.3 fixtures: per-outcome abs error ≤ **1e-3** (looser than macro's analytic fixtures because sampling is involved; tighten toward exact as the solve is inverted rather than sampled). |
| 2 | **Neutral consistency, aggregate** | Full micro-simmed game, neutral policy, **fatigue disabled** (`m ≡ 1`): league slash line inside the **macro §8 ranges** (.240–.260 / .308–.325 / .395–.430, K% 20–25, BB% 7.5–10.5, HR/PA 2.7–3.7, R/G 4.2–4.8). |
| 3 | **Pitch realism** | Pitches/PA ≈ **3.7–4.0**; called+swinging strike, foul, and in-play mix within MLB norms (`φ` tunes this without moving test 2). |
| 4 | **Fatigue behaves** | With fatigue **on**, a starter's opponent OPS in innings 7+ rises measurably vs. innings 1–3 (a new band, e.g. +.030–.080 OPS late), and full-game R/G rises modestly above test 2. |
| 5 | **PED stamina** | A flagged pitcher's capacity scales by the §8.4 formula (`70 → 105 → 120` fixture); post-game `health_ceiling`/`detection_risk` deltas match `LeagueSimulator.Ped*` constants. |
| 6 | **Determinism & zero-GC** | Fixed seed + neutral policy ⇒ bit-for-bit identical game; **0 B allocated** per warm simulated PA/pitch (same profiler check the macro-sim passes). |

**Tuning order** (change one block, re-run, never several at once — mirrors macro §8):

1. **Neutral consistency off (test 1/2)** → the §5.2 absorption inversion / §5.3 renormalization, *not* the macro tables (those are already calibrated; touching them re-opens macro §8).
2. **Pitches/PA off (test 3)** → foul rate `φ` and `zoneProb(count)`.
3. **Fatigue too weak/strong (test 4)** → §8.2 curve knobs (`α`, `FATIGUE_C`, `M_MIN`), then §8.1 `STAMINA_BASE`/`STAMINA_SLOPE`.
4. **Human skill feels inert/overpowered** → §6 timing tolerance `τ_tol` and the input→`(π, q)` gains (this is a *feel* knob, gated on tests 1–2 still passing under the neutral policy).

Because tests 1–2 lock the neutral limit to the already-validated macro line, **micro-sim tuning never silently breaks the background league** — the two models cannot diverge without a red harness.

---

## 12. UI / interaction contract (thin vertical slice)

Per `.claude/rules/ui_conventions.md` and the Phase 4 plan (step 3):

- **One scene, isolated:** the first at-bat view is a single `.tscn` under `Assets/UI/` (create the folder with it), root named in PascalCase after the file. It is the phase's demoable vertical slice — minimal but real.
- **Verify node paths first:** run `godot_scene_mapper` (or the Godot MCP) to confirm the actual node tree *before* writing any `GetNode<T>()`; hardcoded paths against an unverified tree are a review-blocking defect. Cache node refs in `_camelCase` fields in `_Ready()`, never in a per-frame/pitch loop.
- **UI is read-only over sim state.** It renders DTOs (count, base-out state, pitch cue, play-by-play line) and **emits player-intent signals upward** — swing timing and location guess — which the driver turns into a `BatterAction`. The UI **never** writes the database or mutates sim state (it does not touch `Batting_Stats`, `Game_Logs`, etc.; the driver owns all writes).
- **Off the UI thread.** The pitch resolution and any DB flush happen in the sim/async layer; the scene awaits results off the dispatcher. Pooled log lines, dirty-flag label updates, no per-frame LINQ or string formatting. Player-facing text lives in scene/resource files, not C# literals.

---

## 13. Schema dependency

**The Phase 4 vertical slice needs no new schema** — a deliberate contrast with Phase 3 (which required v3 first):

- `Player_Ratings.pit_stamina` (v3) drives §8; `PitcherRatings.Stamina` is already plumbed through `AtBatResolver`.
- `Game_Logs` (v3) already fits the §10 play-by-play, JSON `payload`, nullable `player_id`.
- The box score reuses the existing `Batting_Stats`/`Pitching_Stats` upsert path.

**Deferred to schema v4** (each via the **No Blind Queries** validation path — `SchemaDefinitions.sql` first, validated, *then* dependent C#):

- **Bullpen roles** — a `pitcher_role` column (or a roster/lineup table) so relievers exist and can be summoned as the starter fatigues (§8.5).
- **Pitch arsenals** — per-pitch-type ratings (velocity/movement per fastball/breaking/offspeed) for a richer pitch model; the slice runs on the coarse `pit_stuff`/`pit_control`/`pit_stamina` triple.

Neither is on the critical path for a working interactive at-bat, so they land after the slice proves out — keeping Phase 4 a thin, demoable increment rather than a schema-first big bang.

---

## 14. Summary of the shared surface (what must NOT be duplicated)

| Concern | Single source of truth (already exists) | Micro-sim usage |
|---|---|---|
| 7-way PA outcome distribution | `AtBatResolver.ComputeProbabilities` + `BaseProbabilities`/`Sensitivities`/`MatchupWeights` | `p*` anchor (§5.2/§5.3/§7) |
| Base-out advancement + discretionary knobs | macro §7 logic in `LeagueSimulator`; `SingleScoresFrom2nd`, `DoubleScoresFrom1st` | outer-chain `A_e` (§3) |
| RNG | `RngState` (xoshiro256\*\*) | one `ref` stream per game (§9) |
| PED power `1.5×` + post-game costs | `AtBatResolver` power hook; `LeagueSimulator.Ped*` constants | reused as-is; **adds** pitcher stamina `1.5×` (§8.4) |
| Absorbing-chain analysis | `(I − Q)^{-1}` fundamental matrix | run expectancy/leverage (§4) **and** count-chain absorption (§5.2) |

The micro-sim's *own* new constants are exactly: the pitch-chain class rates and `zoneProb`/`φ` (§5), the timing/location gains (§6), and the fatigue curve (§8.2) + capacity (§8.1). Everything else is borrowed. That is the whole point — the interactive game and the background league are the **same statistical universe**, one seen pitch-by-pitch and one seen a-PA-at-a-time.
