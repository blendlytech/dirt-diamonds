# G-2 Context Pack: Pitching Mode

**For:** Claude Fable 5.1 (G-2 pitching-mode design). **Prepared by:** Claude Opus 5.5, 2026-10-10, from `origin/main @ f983692`.
**Purpose:** everything the design needs, in one file. **Do not explore the repo.** Every code fact below was checked against the source. If you need something that isn't here, write it into the spec's "Open questions for Opus" section instead of searching.

---

## 1. The game, in three lines

*Dirt & Diamonds* is a commercial Godot 4 + C# baseball life-sim. The player raises one ballplayer (batter **or** pitcher; male or female) from High School → College → Draft → Pro. Background games are simulated from ratings. The player's own **attended games** are played pitch by pitch, and that is where player skill lives.

**Owner directive (binding):** *Ratings set the floor and the ceiling; player skill decides where you land between them.* Skill mechanics live on a **human branch only**. The neutral branch, which keeps the league calibrated, must stay byte-identical.

## 2. The ask

Batting has a shipped, skill-based input model ("Read the Pitch", §5). **Pitching has none that the player can use.** A pitcher avatar's attended games run `NeutralPitcherPolicy`. Design the pitching mode so that:

- pitching feels like **the same game** as batting, mirrored;
- every pitching attribute matters in a way the player can feel: **stuff**, **control**, **stamina/fatigue**, plus the arsenal's per-pitch **velocity / movement / usage**;
- **mixing pitches and locations is the player's skill**: the batter AI learns your tendencies, so predictable pitchers get hit, but it can't be cheesed or feel like it cheats;
- the skill band matches the batting side: for an average-rated avatar, a poor vs an excellent pitching game ≈ the gap between a **40- and 70-rated** pitcher (§7), capped so skill can't make a 30 elite or ruin an 85;
- the neutral path stays byte-identical, with zero allocation on the sim hot path.

## 3. Ratings → outcomes: the macro resolver (`AtBatResolver.cs`, shipped, calibrated)

Every rating is 0–100, with 50 = league average. Deviation `d = (rating − 50) / 50`. Seven PA outcomes `{Out, Strikeout, Walk, Single, Double, Triple, HomeRun}`:

```
weight_O = base_O · exp(k_O · Σ_i w_{O,i} · d_i),   then renormalized
base  = { .460, .225, .090, .143, .046, .004, .032 }
k     = { .35,  .55,  .55,  .40,  .45,  .35,  .70  }
w rows (bPow, bCon, bDis, pStuff, pCtl, teamDef):
  Out       -0.20 -0.70  0.00  0.50  0.00  0.60
  Strikeout  0.00 -0.80 -0.20  1.00  0.00  0.00
  Walk       0.00  0.00  1.00  0.00 -1.00  0.00
  Single     0.00  0.90  0.00 -0.30  0.00 -0.50
  Double     0.60  0.30  0.00 -0.20  0.00 -0.40
  Triple     0.20  0.20  0.00  0.00  0.00 -0.30
  HomeRun    1.00  0.00  0.00 -0.40  0.00  0.00
```

All-50s reproduces **.247/.315/.412, K 22.5%, BB 9.0%, HR 3.2% of PA**. What +10 / +20 of a pitcher rating is worth against average opposition (from gameplay_core §1.1):

| Rating | +10 | +20 |
| :-- | :-- | :-- |
| pit_stuff | opp OPS −.045 (K → 24.2%) | −.089 (K → 26.0%) |
| pit_control | opp OPS −.006 (BB → 8.1%) | −.012 (BB → 7.4%) |
| (batter) bat_contact | OPS +.050 | +.103 |
| (batter) bat_discipline | OPS +.011 | +.022 |

Control is nearly inert in the macro sim. The design intent is that control earns its value in attended games (pitch location). Whether to also raise its macro weight is a separate Pro Math decision (G-1). Don't depend on it.

**Rating stack before the resolver** (each step clamps to [0,100]): tier environment (HS −20, College −16 on stuff/control/team defense) → person stats ±2 → equipment ≤ +6 → rivalry → PED (stamina ×1.5 for pitchers) → **fatigue** (§4.4). The attended PA receives the already-stacked **effective** ratings.

## 4. The attended PA: `PitchChain.SimulatePa` (shipped)

### 4.1 Anchor and inversion
Per human PA, the driver computes `p* = AtBatResolver.ComputeProbabilities(batter, effectivePitcher, defense)`. Then `PitchChain.SolveNeutral(p*[Walk], p*[Strikeout])` inverts a 12-state count chain (balls 0–3 × strikes 0–2; exits BB/K/BIP) into count-independent per-pitch class rates `{Ball, StrikeClass, InPlay}`. Their absorption from 0-0 hits p*'s BB and K **exactly**. At two strikes, `FoulShareOfStrikes = 0.68` of strike-class pitches are fouls that self-loop, tuning ~3.84 pitches/PA. The BIP exit re-draws from p*'s in-play buckets via `DrawBallInPlay(p*, q)`:

```
in-play weights = p*[Out, 1B, 2B, 3B, HR] · exp(q · {−0.90, 0.25, 0.60, 0.40, 1.20}),  q ∈ [−1, +1]
```

So q = 0 is the macro split, and +q shifts mass to extra-base hits and home runs.

### 4.2 Per-pitch loop (shared by both branches)
```
call   = pitcher.NextPitch(count, rng)                      // IPitcherPolicy
type   = call.IsCalled ? call.Type : arsenal.DrawType(rng)  // usage-weighted
zRef   = ZoneProbability(count, control, type)              // the calibration reference
zDraw  = call.IsCalled ? ExecutedZoneProbability(call.TargetInZone, control) : zRef
inZone = rng.NextDouble() < zDraw
blur   = clamp(0.10 + 0.004·(movement(type)−50) − 0.002·(batterDiscipline−50), 0.02, 0.50)
cue    = type, or (with prob blur) one of the two wrong types uniformly
look   = PitchLook(cue, ZoneProbability(count, control, cue))
intent = batter.NextPitch(look, count, rng)                 // IBatterPolicy
```

```
ZoneProbability = clamp(0.45 + 0.003·(control−50) + 0.05·balls − 0.04·strikes + typeShift, 0.20, 0.80)
  typeShift: Fastball +0.06, Breaking −0.06, Offspeed −0.02
ExecutedZoneProbability(targetIn) = targetIn ? acc : 1 − acc,
  acc = clamp(0.75 + 0.004·(control−50), 0.50, 0.98)
```

### 4.3 The two batter branches
**Neutral branch** (`BatterIntentKind.Neutral`, the calibrated path). There is no swing decision. It draws the class from **location-conditioned** rates that are an exact mixture around `zRef`:

```
ballIn  = ball·(1 − s);   ballOut = ball·(1 + s·zRef/(1 − zRef)),   s = ZoneSeparation = 0.55
(s is shrunk if ballOut would exceed 0.95)
strike/inPlay scale by k = (1 − ballCond)/(1 − ball)
```

When the location is drawn from `zRef` (neutral pitcher), the marginal is exactly the §4.1 rates, so the macro identity holds. **A called pitch draws location from `zDraw ≠ zRef`, which genuinely re-weights outcomes.** That is the only lever a human pitcher has today. BIP always resolves with q = 0 on this branch.

**Read branch** (`BatterIntentKind.Read`, human batting; the model is in §5). Only the human batter reaches it today.

### 4.4 Fatigue (`PitcherFatigue.cs`)
Capacity = 70 + 0.5·stamina pitches (PED: stamina ×1.5, capped at 100). The multiplier m is 1 through 60% of capacity, then `1 − 0.25·t²`, floored at 0.5. Effective stuff and control = `50 + (rated − 50)·m`, rounded. NPC PAs charge 3.9 pitches each. **Within one human PA, the effective ratings are fixed at PA start;** `fatigue.AddPitch()` runs per pitch but only affects the next PA.

## 5. The batting model you are mirroring ("Read the Pitch", shipped)

Per pitch, the player submits `BatterIntent.Read(guessType, guessCell ∈ {0..8 = 3×3 grid, 9 = "expect a ball"}, approach ∈ [−1, +1])`. The swing is **emergent**:

```
trueCell  = inZone ? rng.NextInt(9) : 9          // uniform sub-cell; Read branch only
grade:  typeOk = guessType == type;  inOutOk = (guessCell<9) == inZone
        locAcc = !inOutOk ? 0 : !inZone ? 1 : {dist 0 → 1.0, 1 → 0.6, ≥2 → 0.3}   (Chebyshev)
R       = 0.40·typeOk + 0.60·locAcc
belief  = prior + commit·(target − prior), prior = look.ZoneProbability,
          target = inOutOk ? truth : 1 − truth     // a wrong call INVERTS belief: you're fooled
          commit = clamp(0.70 + 0.30·locAcc ± 0.30·dDisc, 0, 1)   (+ if inOutOk, − if not)
pSwing  = clamp(base(count) + 0.25·approach + 0.90·(belief − (0.50 + 0.10·dDisc)), 0, 1)
          base = clamp(0.45 + 0.10·strikes − 0.05·balls, 0.15, 0.85)
take:   inZone → called strike, else ball             (never Foul/InPlay)
swing:  P(contact) = clamp(0.60 + 0.25·(2R−1) + 0.15·dCon, 0.15, 0.92); miss → swinging strike
        contact → foul with 0.68 at two strikes (self-loop) / 0.30 earlier (counts as a strike)
        else in play with q = clamp(0.90·(2R−1) + 0.30·dCon, −1, 1) → DrawBallInPlay(p*, q)
```

On the Read branch, **the pitcher's stuff reaches outcomes only through p*** (the BIP split weights, which `SolveNeutral` doesn't otherwise use there). It has no per-pitch whiff or contact effect. Arsenal movement only drives cue blur, and velocity is unused everywhere.

**Measured spread (A0 sweep, 20,000 PAs, all-50 matchup):** see §9 (filled from this run of the harness).

## 6. The pitcher-side seam that already exists (engine-complete, unused)

There is no player-facing pitching mode, but the engine plumbing exists:

```csharp
public readonly struct PitchCall { bool IsCalled; PitchType Type; bool TargetInZone; }  // Neutral = default
public interface IPitcherPolicy {
    void BeginPa(in HumanPaContext context);
    PitchCall NextPitch(in CountState count, ref RngState rng);
    void OnPaResolved(PaOutcome outcome);
}
public struct NeutralPitcherPolicy : IPitcherPolicy { /* NextPitch => PitchCall.Neutral */ }
public struct InteractivePitcherPolicy : IPitcherPolicy { /* blocks on PlayerIntentBridge.AwaitPitchCall → Throw(type, targetInZone) */ }
```

- `MicroGame.PlayHalfInning` already branches on `humanPitching`. In that branch it runs `SimulatePa(anchor, rates, matchup, ref neutralBatter, ref pitcherPolicy, ...)`, so **the opposing batter is always `NeutralBatterPolicy`**: no swing model, no read of the pitcher.
- `CareerManager.PlayPendingGame(ref batterPolicy)` (the only path the UI calls) passes `NeutralPitcherPolicy`. `InteractivePitcherPolicy` is never constructed outside its definition, and no pitching UI exists.
- `PlayerIntentBridge.SubmitPitchCall(PitchType, bool targetInZone)` and `AtBatSnapshot.IsPitching` exist for the UI handshake. That path isn't zero-GC constrained (about 5 interactive PAs per game).
- Harness suite 7 already proves the seam has teeth: a scripted pitcher that always "paints" walks at least 3 percentage points more than one that always "challenges".
- `IPitcherPolicy` has **no** per-pitch result hook (`IBatterPolicy` has `OnPitchResolved(in PitchResult)`). The pitcher UI currently gets no per-pitch reveal. Adding a hook means changing the interface and every struct implementor.

**Opus observations (verify in your design; don't take on faith):**
1. **Pitch selection is nearly inert today.** On the Neutral batter branch, the type a human calls only shifts `zRef` through `typeShift`. Velocity and movement do nothing against an NPC batter.
2. **"Always challenge" looks dominant.** Aiming in-zone lowers BB and scales K and BIP up proportionally, and BIP quality is fixed at q = 0. Nothing punishes predictability, so the seam as shipped is exploitable.
3. **The target is binary (in/out),** while the batting side grades on a 3×3 cell plus out. A cell-level target (the spec proposes 9 cells + 8 edge/chase cells) would let location quality, painting corners vs grooving it, matter.
4. A natural shape, which you're free to reject: the human pitches against an **NPC batter that emits `BatterIntent.Read`** intents from a tendency model. That reuses the shipped Read branch (swing, contact, q) instead of building a third branch. The Read branch would then need to see the pitch's actual cell and the pitcher's stuff/movement/velocity. Any change there must keep the **human-batting** Read path's behaviour intact or be versioned deliberately.

## 7. Calibration contract (hard constraints)

- **Invariant N:** the `Kind == Neutral` batter branch with a `Neutral` pitch call keeps today's exact code **and exact RNG draw sequence**. Adding draws on human branches is allowed. Removing or reordering a draw on the neutral path is a hard stop. The macro resolver and its MLB bit-identity guard (PA 48384 / H 10969 / ER 5237) must not move.
- **Gates that must stay green:** MonteCarloHarness suite 5 (analytic pin), suite 6 (2,000 neutral attended games in band), suite 7 (interactive coherence), and the A0 sweep checks.
- **New human branches are graded by self-consistency plus a skill-bot spread, never by a batting-average band.** Batting's precedent is the A0 sweep (perfect / random / fooled scripted readers, 20k PAs each). Pitching needs its own scripted bots. The spec proposes perfect, coin-flip and contrarian. Define what each one *does* precisely enough to code.
- **Skill band target** (gameplay_core §1.4, owner to confirm): poor vs excellent ≈ the 40-vs-70 rating gap ≈ ±.100 OPS for an average avatar, capped.
- **Determinism:** all sim randomness goes through `ref RngState rng` (with `NextDouble()`, `NextInt(n)`, `NextUInt64()`, `Split()`). Same seed and inputs give the same game. Presentation (camera, VFX, announcer) uses its own RNG and never the sim's.
- **Performance:** the per-pitch path is struct math with no heap allocation. Policies are structs consumed through generic constraints, so there's no boxing. All interface members are implemented explicitly, since default interface methods would box.
- **Boundaries:** presentation never writes sim state. Gender, ethnicity and listed position are story-only and **may never** feed a rating or resolver path.
- **Tunables:** every new magnitude is a named constant (a Pro Math data value) and gets a first-pass value plus a rationale.

## 8. Open owner decisions that touch this design (gameplay_core §6)

1. How much skill is worth (recommended: the 40→70 gap, capped).
2. Needs and stress on the field (recommended: a small, capped game-day penalty via `Active_Effects`). It's fine to leave a hook for it.
3. Person stats beyond confidence/happiness/teamwork (proposed: person `discipline` feeds the read scaler). Pitching analogues are yours to propose.
4. Park factors (recommended off at launch).
5. Fielding moments (recommended: yes, opt-in). Out of scope here except as an interaction note.

## 9. Measured reference numbers (harness run on `f983692`, 2026-10-10: 352/352 checks passed)

**Batting A0 sweep:** 20,000 PAs per archetype, all-50 matchup, approach 0. Perfect = oracle-correct type, in/out and exact cell every pitch. Random = blind guesses. Fooled = always wrong.

| Reader | AVG/OBP/SLG | OPS | K% | BB% | Swing% | P/PA |
| :-- | :-- | :-- | :-- | :-- | :-- | :-- |
| Perfect | .604/.669/1.229 | **1.898** | 4.1% | 16.6% | 46.0% | 3.18 |
| Random | .146/.193/.246 | **.439** | 45.8% | 5.5% | 48.1% | 3.57 |
| Fooled | .041/.043/.058 | **.101** | 66.7% | 0.1% | 55.3% | 3.04 |
| *(ratings-only neutral, for reference)* | .247/.315/.412 | .727 | 22.5% | 9.0% | — | 3.84 |

Approach dial (random reader) swing rate: patient 29.4%, neutral 48.1%, aggressive 68.6%.

**This matters for your design.** The shipped batting skill spread (about **1.8 OPS** perfect vs fooled, and about 1.46 perfect vs random) is roughly **an order of magnitude wider** than the gameplay_core §1.4 target of about ±.100 OPS, the 40-vs-70 rating gap. A blind guesser bats far below the ratings-only line. The pitching mode must pick a band deliberately. Say whether it should mirror batting as shipped, or meet the §1.4 target, in which case batting needs a re-band too. This is the central disagreement-worthy question; make the case either way. A realistic human sits somewhere between Random and Perfect, so also say where you expect an *average* player to land.

**Pitcher-side seam (suite 7):** a scripted starter always aiming in-zone walks **3.1%** of batters vs **18.4%** always painting (13,150 calls, NPC batters neutral). In-zone aiming has no counterweight today.

**Neutral calibration (must not move):** micro attended games .249/.316/.413/.730, K 22.5%, BB 9.0%, HR 3.2%, 3.84 P/PA, R/G 4.24. Macro season .248/.316/.411/.727.
