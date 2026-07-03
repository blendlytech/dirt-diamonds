# Design — Non-Linear Need-Decay Curves (Life Sim / Utility AI)

**Author:** Claude Opus 4.8 (mathematical design) · **Phase:** 5 (Life Sim: Needs Engine & Utility AI → Milestone M3 "Life Happens") · **Status:** design formalized retroactively; first-pass curves already ship in `Assets/Simulation/Life/NeedsEngine.cs` and pass `simulate_utility_decay` 14/14. This doc is the spec that implementation was tuned against; it also closes the gaps the empirical first pass left open (§4 modifier semantics, §6 recovery, §7 Utility coupling).

This document is the mathematical specification for how the five life-sim needs **decay** over time. Per `.claude/rules/life_sim_ai.md`, needs decay by non-linear math that **accelerates as the value approaches zero, to simulate desperation**, following `New_Need = Old_Need − (Base_Decay · Environmental_Multiplier · Stress_Modifier)`. This doc turns that one-line mandate into a calibrated, testable curve model, and specifies the two modifiers the mandate names but does not define.

It is a **spec, not code**. `NeedsEngine.cs` (the shipped first pass) implements the decay law; `UtilityCalculator.cs` (still an empty stub) will consume the resulting need levels via §7. The Life sim never references the Baseball sim (CLAUDE.md architectural boundary); nothing here touches an `AtBatResolver`-style table.

Every constant below is a **calibration knob**, not a law. The acceptance target is a set of gameplay-pacing anchors (§8), verified by `simulate_utility_decay` over a simulated week. Tune the per-need table in §3; never hard-code magic numbers in the engine.

---

## 1. Need space

The sim tracks exactly **five** needs (`.claude/rules/life_sim_ai.md`), each a float on **[0, 100]** where 100 = fully satisfied and 0 = rock bottom:

| # | `NeedType` | Kind | Real-world clock it evokes | Empties fastest→slowest |
|---|------------|------|----------------------------|:-----------------------:|
| 0 | `Hunger`   | biological | hours between meals            | 1 (fastest) |
| 1 | `Sleep`    | biological | a waking day before exhaustion | 2 |
| 2 | `Hygiene`  | lifestyle  | days before you *must* clean up | 3 |
| 3 | `Social`   | lifestyle  | days of isolation before it bites | 4 |
| 4 | `Fitness`  | lifestyle  | a week-plus of no activity      | 5 (slowest) |

**Design axis — biological before lifestyle.** The two *biological* needs (Hunger, Sleep) are involuntary and fast: neglect them and the NPC is in trouble within a day. The three *lifestyle* needs (Hygiene, Social, Fitness) are slower, more discretionary, and define the "Sims-like" texture of managing a life around a baseball career. This ordering is a hard design invariant, not an accident of tuning — `simulate_utility_decay` asserts Hunger reaches critical first and Fitness last (§8).

`NeedsState` is a **struct** of five floats (`NeedsEngine.cs`), copied by value per tick — one NPC's needs are 20 bytes, no heap, no per-tick allocation.

---

## 2. The decay law

### 2.1 Continuous form (the conceptual model)

Let `v ∈ [0,100]` be a need's value and `f = 1 − v/100` its **deficit fraction** (0 when full, 1 when empty). The instantaneous decay rate, in points per in-game hour, is:

```
dv/dt  =  − D · E · S · ( 1 + a · f^p )
                          └──────────┘
                        acceleration term A(v)
```

- **`D` = `BaseDecayPerHour`** — points lost per hour at *full* satisfaction (`f = 0`, so `A = 1`). Sets the early-game pace.
- **`a` = `AccelerationCoefficient`**, **`p` = `AccelerationPower`** — shape the desperation ramp. At full, `A(100) = 1`; at empty, `A(0) = 1 + a`. So the need decays **`(1 + a)×` faster when empty than when full** — this *is* the `.claude/rules/life_sim_ai.md` "accelerates as the value approaches zero" mandate, expressed as a single monotone term.
- **`E` = `Environmental_Multiplier`**, **`S` = `Stress_Modifier`** — the two context knobs from the rules-doc formula, defined in §4. Both default to `1.0` (neutral).

`p` controls *where* on the descent the acceleration kicks in. With `p = 2` (the shipped default for all five needs) the ramp is gentle in the satisfied range and only bites in the bottom third — the NPC coasts while comfortable and spirals once genuinely deprived, which is the intended emotional shape.

### 2.2 Discrete form (the shipped integrator)

The sim advances in **1-hour ticks**. The engine is forward-Euler with step `h = 1h`, evaluating the rate at the top of the hour:

```
d(v)     =  D · E · S · ( 1 + a · (1 − v/100)^p )        # this hour's loss
v_next   =  clamp( v − d(v),  0,  100 )
```

This is exactly `NeedsEngine.DecayHour`. Forward-Euler slightly *under*-decays versus the continuous ODE (the true rate rises as `v` falls within the hour), but the constants in §3 are tuned against **this discrete trajectory** — the `simulate_utility_decay` harness runs the shipped integrator, not the ODE — so there is no calibration gap between model and code. The ODE in §2.1 is the conceptual limit; the discrete step is the source of truth.

**Properties that must hold (all harness-checked, §8):**

- **Bounded:** the `clamp` keeps `v ∈ [0,100]` for any `E, S ≥ 0` — no need can overshoot or go negative.
- **Monotone under passive decay:** with no replenishment, `v_next ≤ v` always (`d ≥ 0`), so a neglected need never spontaneously rises.
- **Accelerating:** `d(v)` is strictly decreasing in `v` for `a > 0` — the closer to empty, the bigger the hourly bite.

---

## 3. Per-need calibration table

Each need is a `static readonly NeedDecayProfile(BaseDecayPerHour, AccelerationCoefficient, AccelerationPower)` in `NeedsEngine.cs`. **Tuning is a data edit here — no logic change.**

| Need    | `D` /hr | `a`  | `p` | Full-rate | Empty-rate `D·(1+a)` | Hits critical (≤20) at | Floors (=0) at |
|---------|:-------:|:----:|:---:|:---------:|:--------------------:|:----------------------:|:--------------:|
| Hunger  | 4.20    | 1.6  | 2   | 4.20/hr   | 10.92/hr             | **hour 16**            | hour 18 |
| Sleep   | 3.40    | 1.4  | 2   | 3.40/hr   | 8.16/hr              | hour 20                | hour 23 |
| Hygiene | 1.40    | 1.3  | 2   | 1.40/hr   | 3.22/hr              | hour 47                | hour 54 |
| Social  | 1.00    | 1.2  | 2   | 1.00/hr   | 2.20/hr              | hour 66                | hour 77 |
| Fitness | 0.55    | 1.1  | 2   | 0.55/hr   | 1.155/hr             | **hour 122**           | hour 141 |

The "hits critical" / "floors" columns are computed from the shipped §2.2 integrator (exact hour of first crossing), consistent with the §9 harness trace. `a` tapers from 1.6 (Hunger) to 1.1 (Fitness): the faster, more visceral needs get the steeper desperation ramp, so hunger doesn't just empty first, it empties *increasingly hard*, while fitness slides gently. That coupling of "fast need ⇒ sharp ramp" is deliberate design, not two independent dials.

**How to move a curve:**
- Need decays too fast/slow **while comfortable** → change `D` first (it dominates the top two-thirds of the range).
- Desperation end feels too abrupt or too soft, but the early game is right → change `a` (empty-rate multiplier) or `p` (how late the ramp engages). Raise `p` to keep the satisfied range calm while sharpening the bottom.
- **Never** change `D` and `a` in the same pass — you lose the ability to attribute the harness delta.

---

## 4. The modifier layer (`E` and `S`)

`.claude/rules/life_sim_ai.md` names `Environmental_Multiplier` and `Stress_Modifier` in the formula but defines neither. This section is the missing spec. Both are **multiplicative on the decay rate**, both default to `1.0`, and both are supplied by the caller per tick — the engine treats them as opaque scalars (`DecayHour(state, environmentalMultiplier, stressModifier)`), so this section defines *what the Life sim must pass*, not new engine logic.

### 4.1 Environmental Multiplier `E ∈ [0.25, 3.0]`

Encodes **what the NPC is doing / where they are** this hour. `E > 1` accelerates decay, `E < 1` slows it. Sourced from the current location/activity context (Life sim only). Anchor values:

| Context | Affected need(s) | `E` | Rationale |
|---------|------------------|:---:|-----------|
| Resting at home             | all           | 0.8  | comfort slows the bleed |
| Neutral / out and about     | all           | 1.0  | the calibrated baseline (§3 is defined at `E=1`) |
| Attended baseball game (3h) | all           | 1.0  | must not starve the fan — the §8 anchor is tested here |
| Sweltering day outdoors     | Hygiene       | 1.8  | sweat; Hunger 1.2 (dehydration) |
| Legal-work / labor hustle   | Fitness       | 2.5  | `.claude/rules/gritty_events.md`: "heavily drain Energy and Fitness"; Hunger 1.5 |
| Bar / party                 | Social        | 0.4  | being social *slows* social decay (it does not replenish — that's §6) |

**Per-need vs uniform.** Conceptually `E` is a **per-need vector** — a hot day hits Hygiene, not Social. The shipped `DecayHour` takes a single scalar applied to all five needs (the degenerate case, adequate for the current no-location build). When locations/activities land, the Life sim passes a per-need `E`; the natural extension is a five-element `E` argument or five `DecayHour` calls. The scalar form is `E_all`; the vector is the target. Either way the range and defaults above hold.

### 4.2 Stress Modifier `S ∈ [1.0, 2.5]`

Encodes the **stress/emotion overlay** (`.claude/rules/life_sim_ai.md` "Stress & Emotion Overlay"): high toxicity/stress from gritty events accelerates *all* need decay — a person under a police-raid or blackmail arc physically frays faster. `S ≥ 1` always (stress never *helps* a need). Map from a `stress ∈ [0,100]` overlay scalar:

```
S = 1 + (S_max − 1) · (stress / 100)          S_max = 2.5
```

So calm (`stress=0`) → `S=1.0`; a high-stress arc (`stress≈80`) → `S≈2.2`. `S` is the **decay-side** effect of stress; the **action-override** effect of stress (forcing autonomous stress-relief actions regardless of temporal cost) is the Utility layer's job (§7), not a decay term. Keeping them separate means the same `stress` scalar drives both without double-counting.

**Combined ceiling.** `E·S` is capped at `3.0` in aggregate so a labor hustle during a high-stress arc can't produce a physically absurd per-hour cliff. At the cap, a full Hunger need loses `4.2·3.0 = 12.6/hr` — empty by nightfall, which is the intended "everything is falling apart at once" ceiling, not a bug.

---

## 5. The floor and the desperation zone

Once a need reaches **0** under continued deficit it stays pinned at 0 (the `clamp`). This is **intended**, not a curve defect: 0 means "rock bottom and miserable," and the accelerating ramp (§2) is precisely what makes the final approach feel like a spiral rather than a gentle glide.

Two thresholds structure the range:

```
100 ────────────── satisfied ──────────────── 20 ── desperation ── 0
                 (managed by the NPC)      CriticalThreshold      floor
```

- **`CriticalThreshold = 20`** (`NeedsEngine.CriticalThreshold`) — the shared line the §7 stress overlay keys off. At or below it, the Utility layer's stress overlay is allowed to **override queued actions** and force the NPC to satisfy this need (or seek stress relief) regardless of temporal cost.
- **Floor = 0** — total desperation. In a *functioning* sim the NPC never reaches it, because §7 fires recovery actions well above the critical line. The floor is only observed under the harness's artificial **total-neglect** scenario (no actions ever taken), which exists precisely to prove the desperation machinery has something to fire on.

The interesting *gameplay* dynamic range is therefore `[20, 100]` (managed) with `[0, 20]` as the override zone — not the full `[0,100]`. The pinned-0 cliff in the harness graph (§9) is the artificial-neglect tail, read as "this NPC has completely fallen apart," and is the correct behavior.

---

## 6. Recovery (the companion to decay)

Decay only removes; actions restore. Recovery is specified here so the curves connect to the Utility layer, but the **restore amounts belong to the action catalog** (UtilityCalculator's domain), not this doc.

**Restore model — additive, per-action, clamped:**

```
v_next = clamp( v + restore,  0,  100 )
```

An action declares a vector of `(NeedType, restore)` deltas applied on completion (instant actions) or accrued per hour over the action's duration (time-extended actions like an 8-hour sleep restoring Sleep at a fixed rate). The engine surface is a single `NeedsState.Restore(need, amount)` mirror of `Set`.

**Deliberate asymmetry — recovery is *not* accelerated.** The desperation ramp (§2) is a **decay-only** non-linearity: you get hungry faster when starving, but a sandwich fills the same 30 points whether you were peckish or famished. There is no symmetric "recovers faster near full" term. This asymmetry is the whole point — it is what makes deep deficits expensive to climb out of and thus worth avoiding, which is what gives the need economy its tension. Do not "fix" it into a symmetric curve.

*Illustrative restore anchors (owned by the action catalog, listed for calibration context only):* a full meal `Hunger +45`, a night's sleep `Sleep +80` (accrued over 8h), a shower `Hygiene +60`, an evening out `Social +35`, a workout `Fitness +30` (and, per `.claude/rules/gritty_events.md`, that same workout *costs* Fitness's opposite via its `E`-boosted Hunger/Sleep decay — actions trade needs against each other).

---

## 7. Coupling to the Utility AI (handoff to `UtilityCalculator.cs`)

This doc owns the curves; the next doc/skill owns action selection. The contract between them:

**Need Deficit consideration.** `.claude/rules/life_sim_ai.md`: `Utility = Σ (Consideration · Weight)`, and Need Deficit is a consideration. It should be **non-linear in the same direction as decay** — a starving NPC values food disproportionately:

```
deficitScore(v) = ( (100 − v) / 100 ) ^ q          q ≈ 2  (curve, not a law)
```

so a need at 20 contributes `0.64` to its satisfying action's utility while a need at 80 contributes `0.04` — low needs dominate selection, mirroring the decay asymmetry. `q` is a UtilityCalculator knob, named here only to pin the *shape* (convex, low-need-dominant).

**Stress override.** When `any need ≤ CriticalThreshold`, the stress/emotion overlay (driven by the same `stress` scalar as §4.2's `S`) may raise that need's consideration weight sharply — or, per the rules-doc mandate, force an autonomous **stress-relief** action (alcohol, arguments) that ignores temporal cost. The decay layer's only responsibility is to *expose* `v` and the `CriticalThreshold` constant; the override logic lives in `UtilityCalculator`. This is the clean seam: decay produces the number, utility decides what to do about it.

---

## 8. Calibration & validation

**Skill:** `simulate_utility_decay` → `dotnet run --project Tools/NeedsDecayHarness`. Simulates 168 in-game hours (one week) of **passive-only** decay (no replenishment, `E=S=1`) for a standard NPC starting fully satisfied, prints a density-ramp graph + fixed-hour table, and asserts the anchors below. **Mandatory after any change to a §3 profile** (CLAUDE.md-style tripwire for this subsystem).

**Acceptance anchors** (all currently green, 14/14):

| # | Anchor | Why it exists |
|---|--------|---------------|
| 1 | Every need stays in `[0,100]` all week | clamp correctness (§2.2) |
| 2 | Passive decay is monotonically non-increasing | no spontaneous rise (§2.2) |
| 3 | **No need < 65 after a 3-hour attended game** | the core mandate — a fan isn't starving after a ballgame |
| 4 | Every need reaches `CriticalThreshold` within the week of total neglect | the stress overlay (§7) must have something to fire on |
| 5 | Hunger reaches critical inside the first 48h | fastest biological need paces the desperation loop |
| 6 | Fitness takes the longest of all five to reach critical | biological-before-lifestyle ordering (§1) holds |

**Tuning order** (change one block, re-run, never several at once):
1. **3-hour-game anchor fails** (decay too hot near full) → lower the offending need's `D` *before* touching `a`.
2. **A need reaches critical too early/late** → adjust that need's `D`.
3. **The desperation tail feels wrong but early game is fine** → adjust `a` / `p`.
4. **Relative ordering (anchor 5/6) breaks** → re-space the `D` column so biological > lifestyle.

**Determinism.** Passive decay is a pure function of the constants — no RNG — so a profile change maps 1:1 to a harness-table change, which is the whole point of tuning against the harness.

---

## 9. Worked trajectories (double as test fixtures)

Verbatim from the shipped harness — an exact regression fixture for the §3 constants. Any change to those constants that moves these numbers must be intentional:

```
  Need          0h     3h     6h    12h    24h    48h    72h    96h   120h   144h   168h
  Hunger     100.0   87.3   74.1   42.8    0.0    0.0    0.0    0.0    0.0    0.0    0.0
  Sleep      100.0   89.8   79.3   56.2    0.0    0.0    0.0    0.0    0.0    0.0    0.0
  Hygiene    100.0   95.8   91.6   83.0   64.8   16.5    0.0    0.0    0.0    0.0    0.0
  Social     100.0   97.0   94.0   87.9   75.5   47.2    8.8    0.0    0.0    0.0    0.0
  Fitness    100.0   98.3   96.7   93.4   86.7   72.9   58.0   41.2   21.2    0.0    0.0
```

**Modifier fixtures** (single-hour, exact — verify the §4 layer once `E`/`S` are wired to context):

- Hunger at `v=100`, labor hustle `E=1.5`, high stress `S=1.5`:
  `f=0 → A=1`, `d = 4.20·1.5·1.5·1 = 9.45` → `v_next = 90.55`. (Neutral would lose only 4.20.)
- Hunger at `v=40`, `E=1`, high stress `S=2.0`:
  `f=0.6 → A = 1 + 1.6·0.36 = 1.576`, `d = 4.20·2.0·1.576 = 13.24` → `v_next = 26.76`.
  Calm (`S=1`) from the same point loses 6.62 → `33.38`. Stress nearly doubles the bite and shoves a moderately-hungry NPC toward the critical line — the intended "stress compounds desperation" behavior.

---

## 10. Implementation contract for `NeedsEngine.cs`

Binding requirements (CLAUDE.md §1 zero-GC + the architectural boundary). The first pass already satisfies these; they are restated so any refactor preserves them:

- **Engine-free.** No Godot types in `NeedsEngine.cs` — it is pure C# math, compiled directly by `Tools/NeedsDecayHarness` (which breaks if a Godot type leaks in, mirroring the other harnesses' guard).
- **Struct state, zero per-tick allocation.** `NeedsState` is a value struct; `DecayHour` returns a new struct by value. A per-NPC day/week advance over thousands of NPCs must not allocate — no LINQ, no boxing, no `new[]` in the tick.
- **Constants, not literals.** The five profiles and `CriticalThreshold` are named `static readonly` / `const`, so `simulate_utility_decay` tuning is a data edit (§3), not a logic edit.
- **Modifiers are caller-supplied** (§4): the engine takes `E, S` as parameters (default `1.0`) and never reaches out to read stress/location itself — that keeps it pure and the boundary clean.
- **Life-side only.** Nothing in this file or its driver references `/Assets/Simulation/Baseball/`. Cross-system signals (e.g. a gritty event raising `stress`) arrive via the `EventBus`, never a direct call.

**Open follow-on work (not this doc):**
1. `UtilityCalculator.cs` — §7 (Need Deficit consideration + stress override). No harness yet; needs its own once there are actions to select between.
2. Wire `DecayHour` into a real **per-NPC hourly/daily tick** driver (analogous to `TimeManager`/`DayAdvancedEvent`, Life-side, engine-free). Currently only the harness exercises it.
3. Add the §6 `Restore` surface and the per-need `E` vector (§4.1) when locations/activities and the action catalog land.

---

## 11. Schema dependency

**None for this pass.** Needs are in-memory runtime state (`NeedsState` struct). Persisting them to survive a save/quit is a later step — the natural home is a small additive `Needs` column set or KV rows keyed by `player_id` (five REAL columns, `[0,100]` CHECK-bounded), added through the **No Blind Queries** validation path (`SchemaDefinitions.sql` first, `PRAGMA user_version` bump, validate, then C#) exactly like every prior schema change. It is deliberately deferred until the per-NPC tick driver (§10 follow-on #2) exists to produce values worth persisting.
