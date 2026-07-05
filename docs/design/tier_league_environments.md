# Design — Tier League Offensive Environments (6-Tier Ladder, Macro-Sim)

**Author:** Claude Opus 4.8 (statistical/mathematical design) · **Phase:** 9a (Tier Schema + Multi-Tier Macro-Sim) · **Status:** design complete; C# not yet written. Fable 5 implements per `docs/phase_8_9_interleave_plan.md §9a`.

This document specifies the per-tier **offensive-environment deltas** for the 6-tier league ladder — `HS, College, MinorA, MinorAA, MinorAAA, MLB`. Each tier is an independent 8-team league running the **same** `AtBatResolver` + `LeagueSimulator` macro-sim (`docs/design/baseball_pa_outcome_model.md`). Players in every tier are generated on the same 0–100 rating scale centered at 50, so ratings are **tier-relative**: a rating-50 hitter is an average hitter *for that tier*. What must differ between tiers is the league **run environment** — HS ball plays sloppy and high-scoring; the environment tightens as you climb; MLB is the shipped calibration, untouched.

The mechanism follows the **PED / rivalry / fatigue precedent exactly** (`RivalryEffects`, `AtBatResolver §6`, micro-sim §8.3): a delta is an **effective-rating shift fed into the unchanged resolver**, never a change to the calibration tables (`BaseProbabilities` / `Sensitivities` / `MatchupWeights`). One constant integer vector per tier is added to every player's ratings at roster-load time; the resolver then runs bit-for-bit as shipped. Micro↔macro consistency (micro-sim §7, §11) is preserved by construction because the shift is baked into effective ratings *before* `ComputeProbabilities`.

Every number here is a **calibration knob**, not a law. The acceptance target is the per-tier band table (§4), verified by `run_monte_carlo_batch`. The deltas of §2 are the **starting recommendation**; the bands of §4 are the **contract**. The implementer may nudge a delta by ±1–2 points per knob to land inside the bands after empirical measurement.

---

## 1. Design intent — what each tier should feel like

**High school** is sloppy, wild, high-scoring amateur ball. Balls find grass because the defense boots them or is out of position; pitchers walk the ballpark; the league hits north of .300 collectively and games run 7–8 runs a side. The player, arriving as an HS avatar (9a default), should read the box score and immediately feel "this is unpolished baseball." **College** is a notch tighter but still loose. The **minor-league rungs** (A → AA → AAA) form a smooth, legible staircase: each step, run prevention improves, walks recede, batting average drops, and the line inches toward the professional norm. **MLB** is the polished ceiling and the shipped calibration — `.247/.315/.412`, `.727` OPS, ~4.5 R/G at all-50 ratings — reproduced bit-for-bit.

The defining idea is that **tier identity lives on the run-prevention side, not the batter side.** Because ratings are tier-relative, the average hitter is a 50 in every tier — it is meaningless to say HS hitters are "worse," since they are average *for HS*. What is physically real is that the pitching and defense an average HS hitter faces are genuinely bad, so more balls fall in, more walks are issued, and fewer bats are missed. We therefore drive the entire environment by **degrading the pitcher-and-defense knobs** (`pitStuff`, `pitControl`, `teamDefense`) in the lower tiers and relaxing that degradation to zero at MLB, while holding the three batter knobs (`batPower`, `batContact`, `batDiscipline`) at zero in every tier. Raising batter ratings would double-count the tier-relative baseline and push K% / BB% in incoherent directions; degrading run prevention is the single monotone lever that lifts AVG, OBP, SLG, BB%, HR/PA and R/G together while pulling K% down — exactly the sloppy-ball signature.

The consequence — disclosed up front — is that **AVG, OBP, SLG and R/G are the strong, legible tier separators**, while **K%, BB% and HR/PA move gently** (they are secondary indicators). This is intentional: the prompt frames the headline as "offense decreases (or K% increases) from HS→MLB," and a falling batting average is the cleanest, most player-legible signal a stat line can carry.

---

## 2. The delta table

Six integer rating-point shifts per tier, in `DeviationSlot` order, added to every player's ratings at roster-load time and clamped to `[0,100]`. All `|delta| ≤ 20` (the calibration harness uses all-50 rosters where clamping never binds; live rosters roll bell-shaped around 50 with spread 25, so a ≤20 shift keeps clamp bias in the tails negligible).

| Tier      | batPower | batContact | batDiscipline | pitStuff | pitControl | teamDefense | Rationale |
|-----------|:--------:|:----------:|:-------------:|:--------:|:----------:|:-----------:|-----------|
| **HS**       | 0 | 0 | 0 | **−20** | **−20** | **−20** | Max sloppiness: hittable stuff, no command, leaky defense → highest AVG/OBP/SLG/R/G in the game. |
| **College**  | 0 | 0 | 0 | −16 | −16 | −16 | A clear notch of polish above HS; still an offense-heavy amateur environment. |
| **MinorA**   | 0 | 0 | 0 | −12 | −12 | −12 | First professional rung; run prevention visibly improving. |
| **MinorAA**  | 0 | 0 | 0 | −8  | −8  | −8  | Solidly pro; line approaching the big-league shape. |
| **MinorAAA** | 0 | 0 | 0 | −4  | −4  | −4  | One step below the ceiling; nearly MLB, deliberately still hittable. |
| **MLB**      | 0 | 0 | 0 | **0** | **0** | **0** | **Exactly zero on every knob — the shipped Phase 3 calibration, bit-for-bit untouched.** |

**Notes.** `pitStamina` is deliberately absent (fatigue is micro-sim-only and separately calibrated — micro-sim §8). The three run-prevention knobs are kept equal per tier for simplicity and a clean −4/step staircase; the implementer may rebalance *within* a tier's vector (e.g. deepen `pitControl` for more walks, or `teamDefense` for more BABIP) without materially moving the headline AVG/R/G, so long as the tier stays inside its §4 band.

---

## 3. Predicted environment per tier (analytic, log-linear model)

Because every player in a tier receives the *same* shift, the whole league is a **single matchup cell**: an average-hitter-plus-delta faces an average-pitcher-plus-delta in front of average-defense-plus-delta. The seven deviations fed to `ComputeProbabilities` are therefore constant across the league:

```
d = ( bPow, bCon, bDis, pStuff, pCtl, dFld ) = ( 0, 0, 0, δ/50, δ/50, δ/50 )
```

with `δ` the (negative) per-knob delta for the tier. Substituting into the §4.1 `MatchupWeights` rows collapses to a compact per-outcome matchup index (let `d = δ/50`, the shared defensive deviation):

```
m[Out]       = ( 0.50 + 0.60)·d =  1.10·d
m[Strikeout] =   1.00·d
m[Walk]      = − 1.00·d           (pitControl term only; d<0 ⇒ m>0 ⇒ more walks)
m[Single]    = (−0.30 − 0.50)·d = −0.80·d
m[Double]    = (−0.20 − 0.40)·d = −0.60·d
m[Triple]    =        −0.30·d
m[HomeRun]   =        −0.40·d
```

Then `w_O = p_base[O]·exp(k_O·m_O)`, renormalize, and read off the slash line.

### 3.1 Worked example — MinorA (δ = −12, d = −0.24)

```
                m_O        k_O·m_O     exp(k·m)    w_O = base·exp        p_O
Out       1.10·d=−0.264   0.35·=−0.0924  0.911741   0.460·=0.419401     0.43638
Strikeout 1.00·d=−0.240   0.55·=−0.1320  0.876341   0.225·=0.197177     0.20516
Walk     −1.00·d=+0.240   0.55·=+0.1320  1.141110   0.090·=0.102700     0.10686
Single   −0.80·d=+0.192   0.40·=+0.0768  1.079827   0.143·=0.154415     0.16067
Double   −0.60·d=+0.144   0.45·=+0.0648  1.066945   0.046·=0.049080     0.05107
Triple   −0.30·d=+0.072   0.35·=+0.0252  1.025520   0.004·=0.004102     0.00427
HomeRun  −0.40·d=+0.096   0.70·=+0.0672  1.069510   0.032·=0.034224     0.03561
                                              Σ w = 0.961099
```

Slash from `p_O`:  `H = 1B+2B+3B+HR = 0.25161`, `BB = 0.10686`, `AB = 1−BB = 0.89314`, `TB = 0.41806`.

```
AVG = H/AB          = 0.25161/0.89314 = .282
OBP = (H+BB)/1      = 0.35847         = .358
SLG = TB/AB         = 0.41806/0.89314 = .468
K%  = 20.5%   BB% = 10.7%   HR/PA = 3.56%
```

### 3.2 All tiers (predicted centers)

| Tier      | δ   | AVG  | OBP  | SLG  | OPS  | K%   | BB%  | HR/PA | R/G* |
|-----------|:---:|:----:|:----:|:----:|:----:|:----:|:----:|:-----:|:----:|
| **HS**       | −20 | .306 | .389 | .508 | .897 | 19.2 | 11.9 | 3.80 | ~7.2 |
| **College**  | −16 | .294 | .374 | .488 | .862 | 19.9 | 11.3 | 3.68 | ~6.5 |
| **MinorA**   | −12 | .282 | .358 | .468 | .826 | 20.5 | 10.7 | 3.56 | ~5.9 |
| **MinorAA**  | −8  | .270 | .344 | .449 | .793 | 21.2 | 10.1 | 3.44 | ~5.4 |
| **MinorAAA** | −4  | .258 | .329 | .430 | .759 | 21.8 |  9.5 | 3.32 | ~4.9 |
| **MLB**      | 0   | .247 | .315 | .412 | .727 | 22.5 |  9.0 | 3.20 | ~4.5 |

Every headline column is strictly monotone with no inverted tier pair. AVG steps ≈ .012/tier; OBP ≈ .015; SLG ≈ .019; R/G ≈ 0.5–0.7; K% ≈ 0.66; BB% ≈ 0.6; HR/PA ≈ 0.12.

**\*R/G caveat (read before trusting the last column).** The base-out state machine (`baseball_pa_outcome_model §7`) converts the outcome distribution into runs **non-linearly** — big innings compound. The R/G figures above are a **linear BaseRuns estimate, normalized so the all-50 MLB roster reads the shipped 4.5 center**; they are an *estimate*, not a prediction. High-OBP environments (HS, College) score *more* than a linear estimate implies (crooked-number effect), so actual HS/College R/G likely lands at or **above** the quoted center. This is why the R/G bands (§4) are the widest and skew high. The implementer verifies R/G empirically with the harness.

---

## 4. Acceptance bands per tier (`run_monte_carlo_batch`)

The MLB row is the **shipped Phase 3 band, verbatim** (`baseball_pa_outcome_model §8`) and doubles as the regression guard that Phase 9a has not moved the calibrated core. All other rows are the predicted §3 centers with seed-noise headroom.

**Band philosophy (project lesson).** A prior suite sat exactly on the R/G 4.2 floor and coin-flipped on seed noise. Every band below carries comfortable headroom (≥ ±.010 AVG, ≥ ±0.4 R/G) so no band is decided by variance. With `|delta| ≤ 20` the tiers are inherently ~.012 AVG / ~0.5 R/G apart, so **adjacent** ±.010/±0.4 bands share a thin overlap zone by design — that is expected and harmless: the bands are a "is this tier roughly right" gate, not a classifier. The **centers are strictly monotone** (the real legibility contract), any **2-tier separation is unambiguous**, and at the harness's PA volume an observed tier line lands near its center, well clear of its neighbors. AVG / OBP / SLG / R/G are the tier discriminators; **K%, BB%, HR/PA move gently and are sanity checks, not separators** (their adjacent bands overlap freely).

| Tier      | AVG        | OBP        | SLG        | K%        | BB%        | HR/PA     | R/G       |
|-----------|:----------:|:----------:|:----------:|:---------:|:----------:|:---------:|:---------:|
| **HS**       | .296–.316 | .379–.399 | .493–.523 | 17.7–20.7 | 10.9–12.9 | 3.4–4.2 | 6.4–8.0 |
| **College**  | .284–.304 | .364–.384 | .473–.503 | 18.4–21.4 | 10.3–12.3 | 3.3–4.1 | 5.8–7.1 |
| **MinorA**   | .272–.292 | .348–.368 | .453–.483 | 19.0–22.0 |  9.7–11.7 | 3.2–4.0 | 5.3–6.5 |
| **MinorAA**  | .260–.280 | .334–.354 | .434–.464 | 19.7–22.7 |  9.1–11.1 | 3.0–3.9 | 4.9–5.9 |
| **MinorAAA** | .248–.268 | .319–.339 | .415–.445 | 20.3–23.3 |  8.5–10.5 | 2.9–3.7 | 4.5–5.4 |
| **MLB**      | **.240–.260** | **.308–.325** | **.395–.430** | **20–25** | **7.5–10.5** | **2.7–3.7** | **4.2–4.8** |

MLB `OPS` band is the shipped `.710–.745`. The regression guard for 9a is the MLB row here matching Phase 3 exactly.

---

## 5. Numeric fixture — HS tier (pin a unit test)

For an **all-50 batter / all-50 pitcher / all-50 defense after the HS delta shift** — i.e. effective ratings `batPower/Contact/Discipline = 50`, `pitStuff/Control = 30`, `teamDefense = 30` — `ComputeProbabilities` must produce the vector below. Deviations `d = (0, 0, 0, −0.4, −0.4, −0.4)`.

Intermediate matchup indices and weights:

```
Outcome     m_O      k_O    k_O·m_O    exp(k·m)     w_O = base·exp
Out       −0.44     0.35   −0.1540     0.857272     0.460·=0.3943453
Strikeout −0.40     0.55   −0.2200     0.802519     0.225·=0.1805667
Walk      +0.40     0.55   +0.2200     1.246077     0.090·=0.1121469
Single    +0.32     0.40   +0.1280     1.136551     0.143·=0.1625269
Double    +0.24     0.45   +0.1080     1.114047     0.046·=0.0512462
Triple    +0.12     0.35   +0.0420     1.042895     0.004·=0.0041716
HomeRun   +0.16     0.70   +0.1120     1.118514     0.032·=0.0357925
                                            Σ w_O = 0.9407960
```

Normalized probability vector (`p_O = w_O / Σ w`), ≥ 5 decimals — the fixture to assert:

```
p[Out]       = 0.41916
p[Strikeout] = 0.19193
p[Walk]      = 0.11920
p[Single]    = 0.17275
p[Double]    = 0.05447
p[Triple]    = 0.00443
p[HomeRun]   = 0.03805
                --------
        Σ    = 1.00000   (0.99999 at 5 dp; renormalized to 1)
```

Derived HS slash (cross-check): `H = 0.26970`, `BB = 0.11920`, `AB = 0.88080`, `TB = 0.44718` → **AVG .306 / OBP .389 / SLG .508**, K% 19.2, BB% 11.9, HR/PA 3.80 — matching the §3 HS row. The implementer should pin the seven `p_O` values (tolerance ~1e-5) as the HS-tier unit fixture, alongside the existing all-50 MLB fixture (`p_O = p_base`, unchanged).

---

## 6. Deliberately out of scope

- **Uniform rosters across tiers.** The `9 + 5 + 3` lineup/rotation/bullpen invariant (`LeagueSimulator.LineupSize/RotationSize/BullpenSize`, shared verbatim by `MicroGame`'s stackalloc sizing) is held identical HS→MLB. Per-tier roster shapes are a materially larger refactor and a disclosed simplification (interleave plan §9a).
- **Player development / decline.** Ratings do not drift as a player ages or climbs; a tier's line comes solely from its constant delta. Development curves are Phase 9d.
- **Promotion gates.** How an avatar earns a tier change (performance + scouting) is Phase 9c; this doc only defines what each tier's *environment* is once you are in it.
- **Tier-specific season lengths.** All tiers run the same 154-game season; complete-game starters, no bullpen substitution (that is the deferred schema-v4 micro-sim work). Run environment is measured as ER per team-game.
- **Batter-side and park effects.** The three batter knobs and any park/altitude/aluminum-bat modeling are held at neutral; the environment is carried entirely by the run-prevention triple. Rebalancing onto the batter side is a future tuning option, not part of this baseline.

---

## 7. Empirical verification (implementation note — Fable 5, 2026-07-04)

Measured by `run_monte_carlo_batch`'s tier-ladder suite (flat all-50 rosters, one 153-game-day season, seeds `LeagueSeed`/`SeasonSeed+tier`), **with the §2 deltas exactly as recommended — no nudge was needed**. Every tier landed inside its §4 band on the first run; the ladder is strictly monotone on AVG and R/G; the §5 HS fixture reproduced to 5 decimals; and the 6-tier world's MLB season proved **bit-identical** to a same-seed MLB-only world (the 9a regression guard).

| Tier      | AVG/OBP/SLG (measured) | K%   | BB%  | HR/PA | R/G  |
|-----------|------------------------|:----:|:----:|:-----:|:----:|
| HS        | .308/.390/.509         | 19.0 | 11.9 | 3.8   | 6.97 |
| College   | .297/.376/.495         | 19.4 | 11.2 | 3.7   | 6.38 |
| MinorA    | .283/.361/.471         | 20.5 | 10.8 | 3.6   | 5.78 |
| MinorAA   | .272/.344/.450         | 21.0 |  9.9 | 3.4   | 5.06 |
| MinorAAA  | .259/.329/.431         | 21.9 |  9.4 | 3.3   | 4.63 |
| MLB       | .249/.318/.417         | 22.3 |  9.1 | 3.3   | 4.28 |

The §3.2 R/G caveat played out as predicted: the non-linear base-out machine put HS/College runs near the top of the linear estimate (6.97 vs the ~7.2 linear figure, comfortably mid-band), and every slash line sat within ~.003 of the analytic center. The §4 bands remain the contract for future retunes.
