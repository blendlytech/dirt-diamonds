# Design — Plate-Appearance Outcome Probability Model (Macro-Sim / Monte Carlo)

**Author:** Claude Opus 4.8 (statistical/mathematical design) · **Phase:** 3 (Baseball Macro-Sim → Milestone M1 "The League Lives") · **Status:** design complete, backing schema (v3) shipped; C# resolver/simulator not yet written.

This document is the mathematical specification for the background-league plate-appearance (PA) model. It is a **spec, not code** — `AtBatResolver.cs` and `LeagueSimulator.cs` (Fable 5, next session) implement it. Per `.claude/rules/baseball_engine.md §1`, the macro-sim does **not** simulate pitch-by-pitch; it resolves each PA to one terminal outcome from empirical, matchup-adjusted probabilities. The pitch-level Markov micro-sim (Phase 4) is a separate model and lives in a separate doc.

Every constant below is a **calibration knob**, not a law. The acceptance target is a league-wide slash line inside MLB norms (§8), verified by `run_monte_carlo_batch` over 10,000 PAs. Tune the tables here; never hard-code magic numbers in the resolver.

---

## 1. Outcome space

Each PA resolves to exactly one of **seven** mutually exclusive terminal outcomes:

| # | Outcome  | `PaOutcome` | Counts as AB? | Is a hit? | Total bases |
|---|----------|-------------|:-------------:|:---------:|:-----------:|
| 0 | Out in play (GO/FO/LO) | `Out`       | yes | no  | 0 |
| 1 | Strikeout              | `Strikeout` | yes | no  | 0 |
| 2 | Walk                   | `Walk`      | no  | no  | 0 |
| 3 | Single                 | `Single`    | yes | yes | 1 |
| 4 | Double                 | `Double`    | yes | yes | 2 |
| 5 | Triple                 | `Triple`    | yes | yes | 3 |
| 6 | Home run               | `HomeRun`   | yes | yes | 4 |

**Deliberate simplifications for the macro-sim** (all documented so the micro-sim and box-score UI don't assume otherwise):

- **HBP is folded into `Walk`.** Hit-by-pitch (~1% of PA) is an on-base event that isn't an at-bat and isn't a hit — statistically identical to a walk for slash-line purposes. Folding it in lets `Walk` carry the full ~9% on-base-without-a-hit rate and keeps OBP correct without an eighth bucket. (The `Batting_Stats.bb` column therefore includes HBP; note this in any UI that later wants to split them.)
- **Reached-on-error, sacrifice fly/bunt, catcher's interference are omitted.** ROE (~0.5%) and productive outs roughly cancel in the run environment; sacrifices are a strategy layer the background sim doesn't model. All balls not in the hit buckets are `Out`.
- **No intentional-walk modeling.** IBB is a game-state decision, not a matchup; it belongs to the micro-sim, not the background league.

Because the seven probabilities always sum to 1, sampling is a single uniform draw against the cumulative distribution (§7).

---

## 2. League-average baselines

These are the probabilities for a perfectly average batter facing a perfectly average pitcher in front of an average defense — every rating = 50 (§3). They are calibrated to recent MLB league-wide rates (2021–2023 ≈ .248/.317/.411, ~22.5% K, ~8.5% BB, ~3.3% HR/PA).

```
p_base[Out]       = 0.460
p_base[Strikeout] = 0.225
p_base[Walk]      = 0.090   # BB + HBP
p_base[Single]    = 0.143
p_base[Double]    = 0.046
p_base[Triple]    = 0.004
p_base[HomeRun]   = 0.032
                    ------
                    1.000
```

**Derived league slash line** (proof of calibration — this is what an average-vs-average sim must reproduce):

```
PA  = 1
AB  = PA − Walk                 = 1 − 0.090            = 0.910
H   = 1B+2B+3B+HR               = 0.143+0.046+0.004+0.032 = 0.225
TB  = 1B + 2·2B + 3·3B + 4·HR   = 0.143+0.092+0.012+0.128  = 0.375

AVG = H  / AB                   = 0.225 / 0.910        = .247
OBP = (H+BB) / (AB+BB)          = 0.315 / 1.000        = .315
SLG = TB / AB                   = 0.375 / 0.910        = .412
OPS = OBP + SLG                                        = .727
K%  = 22.5%   BB% (incl HBP) = 9.0%   HR/PA = 3.2%
```

All four figures sit squarely inside modern MLB norms. **If a future MLB era shifts, retune only this block** and re-run `run_monte_carlo_batch`.

---

## 3. Rating inputs (schema `Player_Ratings`, added in schema v3)

The model is driven by three batter ratings, three pitcher ratings, and one fielding rating, all on a **0–100 scale where 50 = league average**. They live in the new `Player_Ratings` table (one row per baseball-active player; see `Assets/Data/Database/SchemaDefinitions.sql`, schema v3):

| Rating           | Column           | Drives |
|------------------|------------------|--------|
| Power            | `bat_power`      | HR, 2B, 3B ↑ |
| Contact          | `bat_contact`    | 1B ↑, SO ↓, Out ↓ |
| Discipline       | `bat_discipline` | BB ↑, SO ↓ |
| Stuff            | `pit_stuff`      | SO ↑, hits/HR ↓ |
| Control          | `pit_control`    | BB ↓ |
| Stamina          | `pit_stamina`    | fatigue (micro-sim + PED hook; not a PA input directly) |
| Fielding         | `fielding`       | balls-in-play outs ↑, 1B/2B/3B ↓ |

`is_pitcher` (also in `Player_Ratings`) tags who bats vs. who pitches when the simulator builds lineups and rotations.

**Normalization.** Convert every rating to a signed deviation in `[−1, +1]`:

```
r = (rating − 50) / 50
```

So 50 → 0 (neutral, no effect), 100 → +1 (elite), 0 → −1 (replacement-floor). This is the only transform; all matchup math below is in `r`-space. Symbols:

```
bPow, bCon, bDis   — batter power / contact / discipline deviations
pStuff, pCtl       — pitcher stuff / control deviations
dFld               — fielding deviation of the defense behind the pitcher
```

`dFld` for a PA is the **team defense** of the pitching side: the mean `fielding` of that team's non-pitcher position players, normalized. The simulator computes it once per team at roster-load time (not per PA).

---

## 4. Matchup modulation

Each outcome's baseline is scaled multiplicatively by a **log-linear matchup term**, then the whole vector is renormalized to sum to 1:

```
w_O = p_base[O] · exp( k_O · m_O )          for each outcome O
p_O = w_O / Σ_O w_O
```

- `m_O` is the **matchup index** for outcome `O`: a weighted sum of rating deviations, positive when the batter is favored toward that outcome.
- `k_O` is the **sensitivity** of that outcome to its matchup index.
- `exp(·)` guarantees every `w_O > 0` (no outcome can go negative or blow past 1), and renormalization guarantees a valid distribution for *any* rating combination — including two elite extremes at once. This is the property that makes the model safe to drive from arbitrary generated rosters.

When all ratings are 50, every `m_O = 0`, every `exp(0) = 1`, and `p_O = p_base[O]` — the league baseline of §2 falls out exactly.

### 4.1 Matchup indices

`+w` favors the batter; `−w` favors the pitcher/defense. Balls-in-play outcomes (`Single`, `Double`, `Triple`, `Out`) carry a defense term; `Strikeout`, `Walk`, and `HomeRun` are defense-independent (a struck-out, walked, or over-the-fence ball is never fielded).

```
m[Strikeout] =  1.00·pStuff − 0.80·bCon − 0.20·bDis
m[Walk]      =  1.00·bDis   − 1.00·pCtl
m[HomeRun]   =  1.00·bPow   − 0.40·pStuff
m[Triple]    =  0.20·bPow   + 0.20·bCon  − 0.30·dFld
m[Double]    =  0.60·bPow   + 0.30·bCon  − 0.20·pStuff − 0.40·dFld
m[Single]    =  0.90·bCon              − 0.30·pStuff − 0.50·dFld
m[Out]       =  0.50·pStuff + 0.60·dFld − 0.70·bCon  − 0.20·bPow
```

### 4.2 Sensitivities

```
k[Strikeout] = 0.55
k[Walk]      = 0.55
k[HomeRun]   = 0.70
k[Triple]    = 0.35
k[Double]    = 0.45
k[Single]    = 0.40
k[Out]       = 0.35
```

Higher `k` = a rating swing moves that outcome more. Power is the most leveraged skill (`k[HR]=0.70`) because HR rate has the widest real-world spread; triples are the least (rare, mostly park/speed which we don't model).

### 4.3 Defense independence, restated

`m[Strikeout]`, `m[Walk]`, `m[HomeRun]` contain **no `dFld` term** by construction. A great defense suppresses BABIP (singles/doubles/triples) and converts more balls in play to outs; it does nothing to walks, strikeouts, or home runs. This is the single most important structural correctness property of the model — do not add a fielding term to those three rows.

---

## 5. Worked examples (numeric acceptance fixtures)

These are computed from §2–§4 and double as **unit-test fixtures**: the resolver's per-outcome probabilities for these inputs must match to ~1e-3.

### 5.1 Average vs. average
All ratings 50 → every `m_O = 0` → `p_O = p_base` (§2). Slash **.247/.315/.412**, K% 22.5, BB% 9.0.

### 5.2 Elite slugger vs. average pitcher, average defense
Batter power 90 / contact 70 / discipline 70 (`bPow=0.8, bCon=0.4, bDis=0.4`); pitcher & defense neutral.

| Outcome | `m_O` | `w_O` | `p_O` (normalized) |
|---------|------:|------:|-------------------:|
| Out       | −0.44 | 0.3944 | 0.4053 |
| Strikeout | −0.40 | 0.1806 | 0.1856 |
| Walk      | +0.40 | 0.1122 | 0.1153 |
| Single    | +0.36 | 0.1652 | 0.1697 |
| Double    | +0.60 | 0.0603 | 0.0619 |
| Triple    | +0.24 | 0.0044 | 0.0045 |
| HomeRun   | +0.80 | 0.0560 | 0.0576 |

Slash **≈ .332 / .409 / .607**, OPS **≈ 1.02** — a legitimate middle-of-the-order bat. HR/PA 5.8%, K% down to 18.6%.

### 5.3 Ace vs. average batter, average defense
Pitcher stuff 90 / control 85 (`pStuff=0.8, pCtl=0.7`); batter & defense neutral.

| Outcome | `m_O` | `p_O` |
|---------|------:|------:|
| Out       | +0.40 | 0.4633 |
| Strikeout | +0.80 | 0.3059 |
| Walk      | −0.70 | 0.0536 |
| Single    | −0.24 | 0.1138 |
| Double    | −0.16 | 0.0375 |
| Triple    |  0.00 | 0.0035 |
| HomeRun   | −0.32 | 0.0224 |

Opponent slash **≈ .187 / .231 / .305**, K% 30.6, BB% 5.4 — a front-line ace holding the league to a sub-.540 OPS. The model produces the right shape at both tails without any special-casing.

---

## 6. PED multiplier hooks

Per `.claude/rules/baseball_engine.md` "PED Modifiers": active PED use applies a **temporary 1.5× multiplier to power and stamina** during outcome calculation, and every game played under the influence deducts from `health_ceiling` and raises `detection_risk`.

**In-model hook (pre-normalization).** When the batter's PED flag is active for the game, scale the *raw* power rating before computing `bPow`, clamped to the rating ceiling:

```
effectivePower = min(100, round(bat_power × 1.5))
bPow           = (effectivePower − 50) / 50
```

Everything downstream (§4) is unchanged — the boosted `bPow` simply flows through `m[HomeRun]`, `m[Double]`, `m[Triple]`, `m[Out]`. Pitcher `pit_stamina × 1.5` matters to the micro-sim's fatigue curve (Phase 4); the macro-sim carries the same flag through so a juiced reliever's line is consistent, but stamina is not itself a §4 PA input.

*Example.* The §5.2 slugger on PEDs: power 90 → `min(100, 135) = 100` → `bPow = 1.0`. `m[HR]` rises 0.80 → 1.00, `w[HR] = 0.032·e^0.70 = 0.0644` (was 0.0560). After renormalization HR/PA climbs from ~5.8% to ~6.6% and SLG rises accordingly — a visible but not cartoonish bump, exactly the "1.5× power" intent.

**Post-game hook (DB side, `LeagueSimulator`, not the resolver).** After committing a game a flagged player appeared in, in the simulator's own batch:

```
health_ceiling  −= PED_HEALTH_COST_PER_GAME     # small per-game erosion
detection_risk  += PED_DETECTION_RISK_PER_GAME   # clamp to [0,100]
```

The flag source is `Entity_Flags(flag_name='ped_active')`, written by the Gritty Event system in Phase 7. Until then it is always inactive and the multiplier is a no-op — the hook exists so the resolver's signature and the simulator's post-game step are already PED-aware. `PED_HEALTH_COST_PER_GAME` and `PED_DETECTION_RISK_PER_GAME` are calibration constants owned by whoever tunes the risk/reward of the drug system, not this doc.

---

## 7. From PA outcomes to runs (macro game loop)

The PA model gives *outcomes*; a game needs *runs*. The simulator wraps the resolver in a **base-out state machine** per half-inning — cheap, integer-only, and a natural precursor to the Phase 4 Markov states.

**State:** `outs ∈ {0,1,2}` and base occupancy as 3 bits `(on1B, on2B, on3B)`. Loop PAs until `outs == 3`; sum runs; repeat for 9 innings (extra innings while tied). Advancement per outcome:

```
Out / Strikeout : outs += 1.                              (no productive-out / sac modeling)
Walk            : batter → 1B; force only — advance a runner
                  exactly when the base behind is occupied
                  (1B→2B→3B chain; a forced runner on 3B scores).
Single          : batter → 1B; R1 → 2B;
                  R2 → home  w.p. SINGLE_SCORES_FROM_2ND (else → 3B);
                  R3 → home.
Double          : batter → 2B; R1 → home w.p. DOUBLE_SCORES_FROM_1ST (else → 3B);
                  R2 → home; R3 → home.
Triple          : batter → 3B; all runners score.
HomeRun         : batter + all runners score.
```

Discretionary advances use the same seeded RNG as the resolver, so games are reproducible. Default advancement constants (calibration knobs):

```
SINGLE_SCORES_FROM_2ND = 0.60
DOUBLE_SCORES_FROM_1ST = 0.45
```

**Target run environment:** ~4.4–4.7 R/G/team (2023 ≈ 4.62). Runs are attributed as **earned** to the pitcher of record (no errors modeled ⇒ no unearned runs in the macro-sim), which feeds `Pitching_Stats.er` and, with `outs_recorded`, ERA. W/L is decided by the final score.

*Note.* This section is the run-production layer around the PA model, included so the two are calibrated together. Double plays and productive outs are intentionally omitted; they roughly offset in aggregate R/G, and adding them is a later refinement gated on `run_monte_carlo_batch` showing a run-environment miss, not a slash-line miss.

---

## 8. Calibration & validation

**Skill:** `run_monte_carlo_batch` (Sonnet 5 builds the harness) simulates ≥10,000 PAs through the real `AtBatResolver` and prints the resulting slash line. Re-run after **every** change to the resolver or the PED weights (CLAUDE.md mandate).

**Acceptance ranges** (league-wide, average roster, 10k+ PA):

| Metric | Target | Accept |
|--------|:------:|:------:|
| AVG    | .247   | .240–.260 |
| OBP    | .315   | .308–.325 |
| SLG    | .412   | .395–.430 |
| OPS    | .727   | .710–.745 |
| K%     | 22.5%  | 20–25% |
| BB%    | 9.0%   | 7.5–10.5% |
| HR/PA  | 3.2%   | 2.7–3.7% |
| R/G    | 4.5    | 4.2–4.8 |

**Determinism.** The resolver and game loop take a seeded, struct-based RNG (§9) so a fixed seed reproduces a run bit-for-bit — required for the harness to attribute a slash-line change to a weight change rather than to variance.

**Tuning order** (change one block, re-run, never several at once):
1. **Slash line off** → §2 baselines.
2. **Right average but stars/scrubs too flat or too extreme** → §4.2 sensitivities `k_O`.
3. **A specific skill feels inert or overpowered** → §4.1 index weights.
4. **Slash line fine but runs off** → §7 advancement constants.

---

## 9. Implementation contract for `AtBatResolver.cs` (next session — spec only)

Binding requirements on the Fable 5 implementation (from `CLAUDE.md §1` zero-GC mandate and `.claude/rules/baseball_engine.md`):

- **Pure and allocation-free.** A `static` resolve method over `readonly struct` inputs; no heap allocation per PA. Use `stackalloc Span<double>` for the seven weights, not a `new double[]`.
- **Single uniform draw** against the cumulative of the seven `p_O`.
- **Struct RNG passed by `ref`** (e.g. xoshiro256\*\* / PCG) — deterministic, seedable, no `System.Random` instance in the hot loop.
- **Constants, not literals.** `p_base[]`, `k_O[]`, and the §4.1 weights are named `static readonly` tables so `run_monte_carlo_batch` tuning is a data edit, not a logic edit.
- **PED-aware signature** (§6): the batter input carries the active-PED flag; the resolver applies the clamped 1.5× power internally so callers can't forget it.

Suggested shape (illustrative, not prescriptive):

```csharp
public enum PaOutcome : byte { Out, Strikeout, Walk, Single, Double, Triple, HomeRun }

public readonly struct BatterRatings   // built from Player_Ratings + PED flag
{
    public readonly byte Power, Contact, Discipline;
    public readonly bool PedActive;
}
public readonly struct PitcherRatings { public readonly byte Stuff, Control, Stamina; }

// fielding = pitching team's normalized team-defense byte (0–100), precomputed per team.
public static PaOutcome Resolve(in BatterRatings b, in PitcherRatings p, byte fielding, ref RngState rng);
```

`LeagueSimulator.cs` (also next session) owns: bulk roster load via a `Players ⋈ Player_Ratings` join grouped by `team_id`, the round-robin schedule, per-season stat accumulation in preallocated arrays, the §7 game loop, and the **flush of season counting stats in its own batch transaction** — never the calendar tick's — on the season-end `DayAdvancedEvent`. It reacts to `DayAdvancedEvent`/`SeasonRolledOverEvent` off the `EventBus`; it never references the Life sim. Rate stats (AVG/OBP/SLG/OPS/ERA/WHIP) are left at 0 for `StatsNormalizer.cs` (Sonnet 5) to denormalize after the batch, per the `Batting_Stats`/`Pitching_Stats` schema comments.

---

## 10. Schema dependency (shipped in v3)

This model requires columns that did not exist at schema v2. Per the No Blind Queries rule they were added and validated **before** any of the C# above is written:

- **`Teams`** — league structure (`team_id`, `city`, `name`, `abbreviation`, `league`, `division`); the grouping target for rosters and schedules.
- **`Player_Ratings`** — the seven ratings + `is_pitcher` of §3, one row per baseball-active player, `ON DELETE CASCADE` from `Players`.
- **`idx_players_team`** on `Players(team_id)` — the mandated hot-path index for roster grouping (a real FK on `team_id` needs a `Players` rebuild and is deferred; the relationship is enforced at the query layer for now).

Both tables are additive (`CREATE TABLE IF NOT EXISTS`), so the v2→v3 upgrade is the same in-place idempotent migration pattern used for `Game_State` in v2. `PRAGMA user_version = 3`.
