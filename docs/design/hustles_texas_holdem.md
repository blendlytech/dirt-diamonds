# Design — Hustle: Texas Hold'em (pot-odds / bluffing math + card simulation)

**Author:** Claude Opus 4.8 (pot-odds/bluffing math + hand-evaluation + betting state machine) · **Phase:** 8d (per `docs/phase_8_9_interleave_plan.md`) · **Status:** design only — no C# in this pass; **sim/math core only, the `.tscn`/UI is a deferred follow-on slice** (this session's confirmed scope). Sonnet 5 implements against this contract; Fable 5 reviews. Companion to `hustles_narcotics_fencing.md` (shares its three-layer architecture, its `HustleContext`→`HustleResolution` seam, its consequence-writer vocabulary, and the same rules doc, `.claude/rules/gritty_events.md`).

Per `.claude/rules/gritty_events.md`: *"Texas Hold'em: Direct mathematical card simulation evaluating pot odds."* This doc turns that one line into (a) a deterministic 7-card hand evaluator, (b) an equity engine that *is* the mandated "direct card simulation" (Monte-Carlo runout on `RngState`), (c) the pot-odds / minimum-defense-frequency / bluffing math that drives both the opponent AI and the headless hero autopilot, (d) a no-limit betting state machine with side-pot resolution, and (e) the daily-clock/session integration and the accumulated-risk-state it writes for 8c — all in the project's established pure-resolver shape so the math is `HoldemHarness`-provable in isolation.

**The thesis — Hold'em is the *skill* hustle.** Narcotics and Fencing are RNG-dominated: the design tunes them to a *positive-but-lumpy* EV so a rational player always has +EV to reach for. Poker is the opposite by nature and that is the point of shipping it: its EV is **player-determined**. A disciplined player grinds a positive win-rate against a weak field (minus the house's rake); a reckless or unskilled player bleeds. The house edge is the **rake**, not an RNG wall, so the calibration target is not "a fixed positive EV" (that would be dishonest about poker) but **skill monotonicity** — a tight-aggressive policy must beat a calling-station/maniac policy at the same table, and the rake must bind hard enough that a coin-flip-quality player is slightly negative. The single shared constraint with the other hustles still holds: every dollar of upside buys a slice of tail risk that threatens the **baseball** career — here, an underground-game **raid** that seizes the table and spikes `detection_risk` toward 8c's arrest/suspension triad.

---

## 1. Architecture — the same three layers, wall-clean

Identical to `hustles_narcotics_fencing.md` §1 — a pure resolver, an orchestration service, and (deferred here) an isolated UI node. Hold'em slots into the exact seam the other two hustles already cut.

```
 Layer 1 — Pure resolvers (Assets/Simulation/Hustles/, engine-free, Data-free, Baseball-free)
 ┌──────────────────────────────────────────────────────────────────────┐
 │ HoldemEvaluator · 7-card → best-5 comparable HandScore (deterministic) │
 │ HoldemEquity    · MC runout equity vs. N live opponents × RngState      │
 │ HoldemAgent     · pot-odds / MDF / bluff decision (opponents + autopilot)│
 │ HoldemHand      · no-limit betting state machine (streets, side pots)   │
 │ HoldemSession   · buy-in → hands → stand-up; projects HustleResolution  │
 │  → PURE: (HoldemContext snapshot, hero decision, ref RngState) → deltas. │
 │    No DB, no bus, no graph, no Godot.                                    │
 └──────────────────────────────────────────────────────────────────────┘
            │ HoldemContext (in)                    │ HustleResolution (out)
 Layer 2 — Orchestration/application (main thread; Data + bus)
 ┌──────────────────────────────────────────────────────────────────────┐
 │ HustleService (extend): BuildHoldemContext + ApplyHoldemResolution      │
 │  through the SAME writers gritty events + the other hustles use (§11).  │
 │  WorkActivity gains a `Poker` member; the §2 arming/forfeit is reused.  │
 └──────────────────────────────────────────────────────────────────────┘
            │ DTOs up                               │ hero-action signals down
 Layer 3 — UI (Assets/UI/TexasHoldemTable.tscn/.cs) — DEFERRED to a follow-on slice
 ┌──────────────────────────────────────────────────────────────────────┐
 │ Renders the hand DTO (board, hole cards, pot, stacks, legal actions),   │
 │  emits Fold/Check-Call/Raise-to intent signals, never writes the DB.    │
 └──────────────────────────────────────────────────────────────────────┘
```

**Why the resolver stays pure with a headless autopilot.** Exactly as Fencing's `NeutralAcceptDecision` and the `InteractiveBatterPolicy` precedent: the hero's decisions come from the UI in-game, but a deterministic **neutral autopilot policy** stands in for the harness so `HoldemHarness` can play thousands of full sessions from seeds and assert EV/skill bands with no UI. Poker's autopilot is richer than fencing's threshold — it is the same `HoldemAgent` decision engine at a fixed "tight-aggressive" style — but the contract is identical: pure function of `(context, decisions, RngState)`.

**Wall compliance.** `Assets/Simulation/Hustles/` references only its own math plus `RngState` (the same established pure-PRNG exception the other hustles rely on — the Life↔Baseball wall does not cover pure math). It never references `Simulation.Baseball` game loops or `Data`. Layer 2 (`HustleService`) is orchestration and may span Data + bus, as it already does.

**One additive struct change, nothing else.** Hold'em reuses the shared `HustleResolution` (Fencing already reuses it, leaving the faction fields at their defaults). Hold'em populates `FundsDelta`/`DetectionRiskDelta`/`RecklessnessDelta`/`StressDelta` and needs **one new flag field, `SetGamblingBustFlag`** (→ `"gambling_bust"`), the poker analogue of Fencing's sting flag and a clean additive extension of the closed flag set — the same "extend the vocabulary once, 8c inherits it tested" move 8b made for the two risk writers. No schema change: every stat it reads/writes (`Players.{funds, detection_risk, recklessness}`, `Entity_Flags`) already exists.

---

## 2. Daily-clock integration — a fourth Work activity

Hold'em is an *interactive* hustle, so it follows the attended-Game / Narcotics precedent verbatim (`hustles_narcotics_fencing.md` §2): the day-tick's Work block reserves the hours and charges the shared work-exertion needs cost under `ActionCatalog.HustleWork` (`FinancialCost = 0` — the money is the interactive session's job), and `GameManager` **arms a pending hustle session** for the day.

- `WorkActivity` gains a **`Poker`** member (`{ LegalWork, Narcotics, Fencing, Poker }`). Selecting it for the planned day arms a `PendingHustleSession(WorkActivity.Poker, day)`.
- The player opens the Hold'em table and plays the session; its `HustleResolution` applies once on completion (§11).
- **Forfeit-to-no-deal** if the day advances unplayed: no buy-in, no result, zero funds change, zero added heat — the same safe default the other hustles forfeit to. (There is no "autopilot poker night" on forfeit; not sitting down is simply not sitting down. The autopilot policy exists only to drive the *harness*, never to auto-play the player's real money.)
- **Once per day** by construction — the pending-session slot is single-valued, same as the other hustles.

No new orchestration concepts; this is a one-member enum extension plus the two `HustleService` methods in §11.

---

## 3. Scope, table model & rake

The v1 sim/math core is a **single-table no-limit Texas Hold'em cash game**, hero + up to 5 AI opponents (6-max), full four-street play (preflop / flop / turn / river) with standard blinds, button rotation, and **side-pot-correct all-in resolution**. A **session** is a sequence of hands at one table; the hero buys in for a stack, plays, and **stands up** (bank & exit) with whatever remains — the direct structural analogue of a Narcotics run with bank-&-exit between stages, here between hands.

**Stakes tiers** — the hero's session-level choice (the poker analogue of Narcotics' buy-in × push level), controlling blinds, buy-in, and — critically — **field quality**. Weak fields are where money is made; sharp fields punish a mediocre hero. This is the risk/reward lever.

| `StakesTier` | Blinds (SB/BB) | Buy-in (100 BB) | Field skew (§7) | per-hand raid hazard `h` (§9) |
|---|---|---|---|---|
| `Low` | 1 / 2 | 200 | mostly Station/Maniac/LAG (soft, exploitable) | 0.000 (a friendly home game) |
| `Mid` | 5 / 10 | 1000 | balanced mix | small |
| `High` | 25 / 50 | 5000 | mostly TAG/Nit/LAG (sharp) | higher |

Buy-in is bounded by `min(funds, 100·BB)`; the hero may buy in short (≥ 20 BB), the decision the UI collects at session start (harness passes it directly). **No markers/credit in v1** — the hero can never lose more than the buy-in (see §9 and the deferred debt cascade in §11).

**Rake — the house edge.** Each pot that sees a flop is raked **5%, capped at 3·BB** ("no flop, no drop"). Rake is removed from the pot before it is awarded, so it silently drains every contested pot. This is the drag that makes skill *necessary*: a break-even-skill hero is a net loser after rake, so the neutral policy's win-rate must clear the rake to show positive EV, and it only does so against a soft enough field (§13). Rake is a `HoldemProfile` constant, tunable like every other table here.

**Deliberately out of scope for v1** (disclosed deferrals, in the spirit of Narcotics' deferred passive-turf-income): multi-table / tournament play and ICM; specific physical tells or timing reads; player-specific opponent modeling/adaptation across hands (archetypes are fixed per session); run-it-twice; straddles/antes. The math and state machine below are structured so multiway side pots and archetypes are first-class, so these are additive later, not rewrites.

---

## 4. Cards & hand evaluation

### 4.1 Representation

```
Card:  readonly struct — Rank ∈ [2,14] (11=J,12=Q,13=K,14=A), Suit ∈ [0,3].
       Stored as a single byte Code = (Rank<<2)|Suit for compact, alloc-free decks.
Deck:  a 52-length buffer the resolver shuffles with Fisher–Yates over RngState;
       stackalloc'd per hand (or a reused session scratch) — no heap (§10).
```

### 4.2 Best-5-of-7 → a single comparable `HandScore`

The evaluator returns one `int` that **totally orders** all hands: higher wins, equal is a tie (split pot — a value the pot-award logic in §8 relies on for chops). Encoding:

```
HandScore = (Category << 20) | (t1 << 16) | (t2 << 12) | (t3 << 8) | (t4 << 4) | t5

Category (high→low):  8 StraightFlush · 7 Quads · 6 FullHouse · 5 Flush
                      4 Straight · 3 Trips · 2 TwoPair · 1 Pair · 0 HighCard
t1..t5:  the category's tiebreak ranks, most-significant first, unused slots = 0.
```

Tiebreak vectors are the standard ones (Quads: quad rank, then kicker; Full House: trip rank, then pair rank; Flush/High Card: the five ranks descending; Two Pair: high pair, low pair, kicker; etc.). Ranks ≤ 14 fit in 4 bits, so the whole score fits in 24 bits — comparisons are a single `int` compare, zero-alloc, branch-light.

**The wheel** (A-2-3-4-5) is the one special case: the Ace plays low, so the straight's top card is **5**, not 14. Detect the A-5-4-3-2 pattern explicitly and set the straight's `t1 = 5`. (Royal flush needs no special case — it is just the Ace-high straight flush.)

### 4.3 Two acceptable implementations + a differential oracle

- **Recommended (production):** a histogram evaluator — rank-count and suit-count histograms over the 7 cards categorize in O(1)-ish with no combination loop. Fast, alloc-free.
- **Oracle (harness only):** the naive **C(7,5)=21-combo** brute force — evaluate every 5-card subset, take the max. Trivially correct, obviously slow.

`HoldemHarness` runs a **differential test**: over M random 7-card deals, the histogram evaluator and the 21-combo oracle must produce *bit-identical* scores. This is the same "prove the fast path against a naive oracle" discipline the Markov micro-sim uses; it makes the evaluator's correctness a mechanical check, not a matter of hand-picked fixtures (those exist too — royal flush > quads > … , wheel handling, split-pot equality).

---

## 5. The equity engine — the mandated "direct card simulation"

`.claude/rules/gritty_events.md`'s "direct mathematical card simulation evaluating pot odds" is this section. **Equity** = an agent's probability of winning (with ties counted as fractional wins) given its known cards, the current board, and the number of *live* opponents whose holes are unknown.

### 5.1 Postflop — bounded Monte-Carlo runout

```
Estimate(heroCards[2], board[3..5], liveOpponents k, samples K, ref rng) → equity ∈ [0,1]:
  remaining ← the 52 − known cards
  win ← 0
  repeat K times:
    partial Fisher–Yates draw from `remaining`:  k×2 opponent hole cards + (5 − |board|) runout cards
    heroBest ← EvaluateBest7(heroCards ∪ fullBoard)
    beats ← count of opponents with a worse best-7 ; ties ← count equal
    if beats == k:            win += 1
    elif beats + ties == k:   win += 1/(ties+1)      // split the pot equity
  return win / K
```

Every draw is from `ref RngState`, so equity is **deterministic per seed** (a harness requirement). `K` is a `HoldemProfile` knob (default ≈ 300 for opponent decisions — accuracy ±~3% is plenty for a fold/call threshold, and the harness can lower it for speed while a dedicated accuracy check runs a high-`K` pass). The runout scratch is `stackalloc`, so equity estimation is alloc-free.

### 5.2 Preflop — a baked equity table (no per-decision MC)

Running MC for every preflop decision, every hand, every session, in a harness that plays thousands of sessions is wasteful and slow. Instead **precompute** heads-up equity for each of the **169 canonical starting hands** (13 pairs + 78 suited + 78 offsuit) via a one-time offline high-`K` MC, and multiway-shrink it for `k` opponents. Store as a `static readonly` table in `HoldemProfile` (169×[1..5] ≈ 845 doubles ≈ 7 KB — trivial and checked in as data, the same "constants are data, retuning is a data edit" ethos as every calibration table here).

- Generation is a `HoldemHarness --gen-preflop` mode; the output is verified against published references (e.g., **AA vs 1 random ≈ 0.851**, **AKs vs 22 ≈ 0.50**) before it is committed as the baked table.
- This *is* still "card simulation evaluating pot odds" — the simulation is simply amortized offline, exactly as a game would ship a precomputed table rather than roll it live.

---

## 6. Pot-odds, MDF & bluffing math (the core Opus contribution)

The decision math is shared by the opponent AI (§7) and the hero autopilot. It is standard modern poker theory, stated exactly so the harness can assert the formulas hold.

### 6.1 Pot odds — the call threshold

To call `c` (chips owed) into a pot of `P` (chips already out there, *including* the bet being faced), the pot lays you `P : c`. Calling is break-even when

```
requiredEquity = c / (P + c)
```

Expressed as a **bet size** `s = b / P` (the bettor's bet as a fraction of the pot before the bet), the caller's required equity is `s / (1 + s)`. A pot-sized bet (`s = 1`) demands 50% equity to call; a half-pot bet (`s = 0.5`) demands 33%.

### 6.2 Minimum Defense Frequency (MDF)

Facing a bet of size `s`, the fraction of your range you must continue with to stop the bettor from profitably bluffing any two cards is

```
MDF = 1 / (1 + s)        (defend at least this share; fold at most s/(1+s))
```

Half-pot → defend ≥ 2/3; pot-sized → defend ≥ 1/2. An agent constructs its calling range by defending its strongest `MDF` fraction (equivalently: continue whenever `equity ≥ s/(1+s)`), which is exactly the pot-odds threshold in §6.1 — MDF and pot odds are two views of the same line.

### 6.3 Bluffing — the polarized value:bluff ratio

The centerpiece. On a bet of size `s`, for a bluff-catching opponent to be **indifferent** between calling and folding, the bettor's betting range must mix bluffs and value in the ratio

```
bluffs : value  =  s / (1 + s)  :  1
```

Derivation (river, polarized): the caller risks `c=b` to win `P+b`; indifference requires `B·(P+b) = V·b`, so `B/V = b/(P+b) = s/(1+s)`. A pot-sized bet ⇒ `B/V = 1/2` ⇒ the betting range is **2/3 value, 1/3 bluff**; a half-pot bet ⇒ `B/V = 1/3` ⇒ **3/4 value, 1/4 bluff**. Smaller bets should bluff *less often* (relative to value) — a result players routinely get wrong and the AI gets right by construction.

An agent that has decided to bet computes its GTO bluff frequency from its chosen size and then **modulates by personality** (§7): a Maniac multiplies it up (over-bluffs → exploitable by calling more), a Nit multiplies it down (under-bluffs → exploitable by folding more). This is where "skill" lives on both sides of the table: the field's *deviations* from these ratios are the hero's edge, and the hero autopilot's adherence to them is why a tight-aggressive policy beats a loose-passive one in the harness.

### 6.4 Value betting & semi-bluffs

- **Value:** bet when `equity ≥ 0.5 + valueMargin(archetype)` against the range that would call — the made-hand line.
- **Semi-bluff:** with a strong draw (high equity that is not yet a made hand), betting both folds out better hands *and* has equity when called; the agent treats draw equity as bet-worthy at a discount. In v1 this falls out of the MC equity naturally (a flush draw simply *has* ~35% equity), so no special-case code is required — the equity number already prices the draw. Explicit outs-based semi-bluff weighting is a disclosed v2 refinement.

---

## 7. Opponent AI & the hero autopilot

### 7.1 Archetypes

Each seat is assigned a fixed archetype at session start, drawn from the stakes tier's field-skew distribution (§3) using `RngState`. An archetype is a tuple of four tunables (all first-pass, `HoldemHarness`-calibrated, no design-doc anchor — same status as every Narcotics constant):

| Archetype | preflop entry equity | `valueMargin` | `bluffMult` (× the §6.3 GTO ratio) | bet size (× pot) |
|---|---|---|---|---|
| **Nit** (tight-passive) | 0.62 | +0.15 | 0.4 | 0.50 |
| **TAG** (tight-aggressive) | 0.55 | +0.10 | 1.0 | 0.66 |
| **LAG** (loose-aggressive) | 0.48 | +0.06 | 1.4 | 0.75 |
| **Station** (loose-passive) | 0.44 | +0.20 (rarely raises) | 0.2 | 0.50 |
| **Maniac** (hyper-aggressive) | 0.40 | +0.02 | 2.2 | 1.00 |

Field skew by stakes (the risk/reward lever made concrete): **Low** draws mostly Station/Maniac/LAG (a soft field a disciplined hero prints against); **High** draws mostly TAG/Nit/LAG (sharp, thin edges, rake bites hardest); **Mid** is a balanced mix.

### 7.2 The decision procedure (one agent, one action)

```
E ← equity(agentCards, board, liveOpponents)            // §5 (table preflop, MC postflop)
if there is a bet to face (owe c into pot P):
    potOdds ← c / (P + c)
    if E ≥ potOdds + valueMargin:      RAISE to size·P (value)          // clamp to [minRaise, stack]
    elif E ≥ potOdds:                  CALL (defend, MDF)               // occasionally raise if very drawy
    else:                              FOLD — unless a bluff roll fires (prob from §6.3 × bluffMult) → RAISE (bluff)
else (checked to, owe nothing):
    if E ≥ 0.5 + valueMargin:          BET size·P (value)
    else:                              CHECK — unless a bluff roll fires (§6.3 × bluffMult) → BET (bluff)
```

All bet sizes clamp to `[minRaiseTo, stack]` (a raise that can't meet the min-raise becomes a call or an all-in; a bet ≥ stack is an all-in). Bluff rolls draw from `ref RngState`. This single procedure, parameterized by the archetype tuple, produces the whole spectrum from rock to maniac, and — run at the **TAG** row with `reck`-modulated aggression — *is* the hero autopilot.

### 7.3 The hero autopilot (forfeit stand-in / harness driver)

The neutral hero policy is the §7.2 procedure at the **TAG** archetype, with two `HoldemContext`-driven adjustments so it still reads as "the avatar": `reck` (recklessness/100) nudges `valueMargin` down and `bluffMult` up (a reckless avatar plays looser and bluffs more — the same nerve proxy Narcotics uses), within bounded caps so it never degenerates into a Maniac. This gives the harness a deterministic, seed-stable hero to play full sessions, and gives the game a sane default if a real player ever needs one — but, per §2, it never auto-plays the player's actual money on forfeit.

---

## 8. The hand state machine & the interactive contract

This is the one place Hold'em is materially larger than Narcotics/Fencing: a hand is a variable-length sequence of betting decisions across four streets and up to six seats, and the resolver must interleave the hero's UI-driven decisions with the opponents' AI decisions.

### 8.1 Streets & flow

`Preflop → Flop → Turn → River → Showdown` (or an early end when all but one seat folds). Post blinds (SB/BB) relative to the button; deal two hole cards each; run a betting round per street; deal the 3-card flop / 1-card turn / 1-card river between rounds. A betting round ends when every live, non-all-in seat has matched the current bet (or checked around).

### 8.2 The interactive contract (Layer-1 API the UI/harness drive)

The resolver **advances to the next hero decision point (or hand end) on each call**, running all intervening opponent actions itself and recording them in an **action log** the state carries (so the UI can animate the opponents' moves it "missed"). This batches opponent play between hero turns — the clean contract for both a UI and the harness, mirroring how Fencing's `Counter`/`Accept` advance the negotiation and Narcotics' stage methods advance the run.

```
HoldemHand.StartHand(session, ref rng)
    → deals, posts blinds, runs opponents up to the hero's first decision (or hand end).
HoldemHand.SubmitHeroAction(ref state, HeroAction action, ref rng)
    → applies the hero's action, runs opponents + street transitions until the hero must
      act again or the hand ends; updates the action log.

HeroAction:  Fold · CheckOrCall · RaiseTo(long amount)      // amount is the total the hero is raising *to*
```

The state exposes, each decision point, everything a UI or policy needs: `Board`, `HeroCards`, `Pot` (main + side layers), `ToCall`, `MinRaiseTo`, `MaxRaiseTo` (= hero stack, i.e. all-in), each seat's `Stack`/`Committed`/`Status`/`LastAction`, whose turn, and the terminal `HandResult` (winners, amounts, showdown scores) once resolved. Illegal actions fail loud (a raise below `MinRaiseTo`, a bet above stack) — the same ctor/precondition throw discipline the other resolvers keep.

### 8.3 Side pots — the one algorithm worth stating

All-ins for less than the current bet create side pots; getting this right is why "half-implemented all-ins" is worse than none. Standard layered resolution, applied at showdown:

```
Given each live seat's total `Committed`:
  sort the distinct commit levels ascending
  for each level L (from the lowest upward):
     layer = (L − previousL) × (number of seats committed ≥ L)
     eligible = seats committed ≥ L that reached showdown (didn't fold)
     award `layer` (after rake, taken once from the aggregate pot) to the best HandScore
       among `eligible`; split equally on tied top scores (integer chip-odd goes to the
       first eligible seat left of the button — a fixed, documented tie-break)
     previousL = L
```

This conserves chips exactly, which the harness asserts as an invariant (§14): across any hand, `Σ seat stacks + rake taken` is constant — no chips created or destroyed.

---

## 9. The session — buy-in, bank & exit, the raid tail

A `HoldemSession` wraps a run of hands. The resolver tracks the hero's stack internally across hands and returns a single net `HustleResolution` at the end — **no mid-session DB writes** (the "resolver returns one delta bundle, Layer 2 applies it once" pattern; nothing is committed until the hero leaves the table).

```
HoldemSession.StartSession(in HoldemContext ctx, StakesTier tier, long buyIn, ref rng)
    → validates buyIn ∈ [20·BB, min(funds, 100·BB)]; seats the field (§7.1); sets the button.
loop:  StartHand → (hero plays via SubmitHeroAction) → hand resolves into stacks → raid check
HoldemSession.StandUp(ref state) → terminal:  FundsDelta = stackReturned − buyIn.
```

- **Bank & exit** = `StandUp` between hands: take the current stack and go. The direct analogue of Narcotics' bank-&-exit — the interactive tension is "one more hand for a bigger stack" vs. "leave with the win and cap raid exposure."
- **Bust** = stack hits 0 (no markers in v1): the session ends, `FundsDelta = −buyIn`.
- **The raid tail.** After each hand, roll a per-hand raid hazard `h = clamp(hBase + hHeat·heat + hStakes·stakesLevel, 0, hCap)` from `ref RngState`. The cumulative probability of a raid over an `n`-hand session is `1 − (1 − h)^n` — **the longer you sit in an illegal game, the likelier the night ends in a raid**, which is precisely what makes banking early meaningful. On a raid: the session ends immediately, the **on-table stack is seized** (`stackReturned = 0`, so `FundsDelta = −buyIn` — the max session loss, winnings and buy-in both gone), `Δdetection = +10`, `SetGamblingBustFlag`, `Δstress = +20`. `Low` stakes has `h = 0` (a friendly home game never gets raided — its whole risk is variance), so the raid tail scales in exactly with the stakes and heat that also raise the reward.
- **Funds accounting is a single net delta:** `FundsDelta = stackReturned − buyIn`, so `AdjustFunds`' floor-clamp is never load-bearing (buy-in ≤ funds by validation). `Δstress` scales with net loss (a losing night tilts the avatar); a high-variance win nudges `Δreck +1` (the gambling high). Baseline heat from merely playing a `Mid`/`High` underground game: `Δdetection += 1` (the Narcotics "doing business" baseline analogue; `Low` adds none).

---

## 10. Determinism & performance

- **One RNG.** Shuffles, MC runouts, opponent bluff rolls, and field seating all draw from the single `ref RngState` threaded through the session — same seed ⇒ bit-identical session resolution (a harness check, like the dispatcher's determinism contract).
- **Zero-alloc.** Decks and MC runout scratch are `stackalloc`; `Card`, `HoldemResolution`, and the archetype tuples are value types; the preflop equity table is a shared `static readonly` array. `HoldemHarness` asserts a warm played hand and a warm full session each allocate ~0 B.
- **The state-representation call (a real decision, made here).** Unlike Narcotics'/Fencing's tiny states, a 6-seat table state is large and mutated many times within a hand, so the readonly-record-struct-`with` pattern is a poor fit (deep-copying inline seat/board buffers every action). **Recommended: a `ref`-mutated value struct** using C# 12 `[InlineArray]` fixed buffers for the ≤6 seats and ≤5 board cards (mutated in place through `ref state`, not `with`) — value-typed and alloc-free, consistent with the codebase's struct-first ethos. **Sanctioned fallback (Fable's call, like 8b's shared-applier ruling):** a **pooled mutable `HoldemHandState` class**, one instance reused across the whole session/harness — CLAUDE.md explicitly endorses object pooling, poker is turn-based (not a per-frame hot path), and pooling satisfies the zero-GC mandate just as well. Either preserves resolver "purity" in the sense that matters here: no external side effects, deterministic given `(inputs, RngState)`. The `HoldemResolution` stays a `readonly struct` regardless.

---

## 11. Applying a resolution — reuse the writers (§ mirrors Narcotics §5)

A `HoldemResolution` is the shared `HustleResolution` shape (§1), so `HustleService` applies it through the exact primitives the other hustles and gritty events use, with the same application discipline (**DB writes in the service's own batch, then bus impulses** — a subscriber always observes the committed DB state):

| Resolution delta | Writer (already proven) |
|---|---|
| funds | `PlayerQueries.AdjustFunds` (atomic floor-clamped) + `FundsImpulseEvent` |
| detection_risk | `PlayerQueries.AdjustDetectionRisk` (8b's atomic clamp) |
| recklessness | `PlayerQueries.AdjustRecklessness` |
| stress | `StressImpulseEvent` on the bus |
| flag `gambling_bust` | `PlayerQueries.SetFlag` (`set_on_day` = session day — the cascade clock 8c reads) — **the one new flag field** on `HustleResolution` |

`HustleService` gains `BuildHoldemContext(playerId)` (snapshots funds/heat/reck — **no faction reps**, so it is simpler than the Narcotics builder; no `RelationshipGraph` touch, no `GameStateKeys` additions) and `ApplyHoldemResolution(playerId, in resolution, day)` (calls the shared `ApplyCore`, which already handles funds/detection/reck/stress/flags — the only source edit is teaching `ApplyCore`'s flag block the new `SetGamblingBustFlag`, and adding the `WorkActivity.Poker` arming branch). Fencing already proved the shared-`ApplyCore` path carries a hustle that touches no faction graph; Hold'em is the second such consumer.

**Deferred debt cascade (the health-tail parity note).** Narcotics' tail includes a `health_ceiling` hit (turf-war retaliation); Fencing's and v1 Hold'em's do not. The natural poker health-tail is **table markers** — house credit that lets a tilting player buy in beyond `funds`, with unpaid markers setting a `house_debt` flag that a future violent-collection gritty event reads (a `health_ceiling` hit). That needs a persisted debt *magnitude* (funds can't go negative — `AdjustFunds` floor-clamps), i.e. new state, so it is **explicitly deferred** to keep 8d schema-free, exactly as Narcotics deferred its passive turf income. v1 Hold'em's tail is raid (detection) + variance (funds); the debt/health cascade is a noted follow-on hook.

---

## 12. The accumulated-risk-state contract for 8c

Hold'em is a second writer of the same 8c-consumed risk vocabulary 8b established (`roster_availability_triad.md`), so 8c needs no new plumbing for it:

| Signal | Written by (Hold'em) | 8c consumer |
|---|---|---|
| `detection_risk` ↑ | raid (+10), `Mid`/`High` play baseline (+1) | **Suspension / Arrest** — the same threshold the PED and Narcotics paths already raise |
| flag `gambling_bust` | raid | **Arrest** trigger candidate (sibling of `narc_watchlist`) |
| `recklessness` ↑ | high-variance sessions | gritty-event prerequisites (`Recklessness > N`) — escalates the event graph |
| funds ↓↓ | variance / bust / raid | (economy pressure — feeds survival-economy events, not the triad) |

Hold'em writes **no** `health_ceiling` in v1 (the deferred markers/debt cascade, §11) and no faction edges (no on-field-rivalry hook — poker opponents are anonymous table archetypes, not rostered players). Its career tail runs purely through `detection_risk` → 8c's arrest/suspension.

---

## 13. Worked EVs / calibration targets (what `HoldemHarness` asserts)

Poker win-rates are stated in **bb/100** (big blinds won per 100 hands) — the honest unit; net-per-session follows from stack depth. These are *design targets to measure*, not point predictions (the Narcotics §3.4 figures were re-measured by the harness and moved — expect the same here; the **shape** is the contract, not the digits):

| Policy at `Low` (soft field) | expected win-rate | note |
|---|---|---|
| **TAG hero autopilot** | **positive after rake** (target ~+3 to +8 bb/100) | skill clears the rake vs. a weak field |
| **Calling-station hero** | **negative** (bleeds to rake + bad calls) | proves rake + poor play = loser |
| **Maniac hero** | **negative, high variance** | over-bluffs into a station-heavy field that never folds |

The assertions (the *shape*, over N seeded sessions per policy):

1. **Skill monotonicity:** at the same table/stakes, `TAG win-rate > LAG ≈ Nit > Maniac ≈ Station`. The single most important poker check — it proves the pot-odds/MDF/bluff math actually rewards good decisions.
2. **Rake binds:** a **random/coin-flip-quality hero** (equity-blind, acts uniformly) is **net negative** — the house edge is real, so poker is not a money printer for a mediocre player.
3. **Field-quality gradient:** the TAG hero's win-rate is **highest at `Low`, lowest (possibly negative) at `High`** — sharper fields erase the edge, the risk/reward lever working as designed.
4. **Raid tail:** EV degrades with `heat` and `stakes` via the raid hazard; a longer session carries strictly more cumulative raid risk (`1−(1−h)^n` rising in `n`), so "bank early" is measurably safer.

---

## 14. Acceptance checks (`Tools/HoldemHarness`, new — compiles `Assets/Simulation/Hustles/*.cs` + `RngState.cs`)

Structured exactly like `HustleHarness` (a `Check(name, pass, detail)` list, seed-search to force specific rolls, EV bands over N seeded runs). Consider extending `HustleHarness` instead of a new project if the compile set matches — Fable's call.

1. **Evaluator — differential + fixtures:** histogram evaluator ≡ 21-combo oracle bit-for-bit over M random 7-card deals; category ordering correct (royal > straight-flush > quads > … > high card); the A-5 **wheel** ranks as 5-high; identical best-5 across different 7-card holdings produce **equal** scores (split-pot ties).
2. **Equity engine:** MC equity matches known values within tolerance (AA vs 1 ≈ 0.85; AKs vs 22 ≈ 0.50; equity strictly decreases as `liveOpponents` rises); the baked preflop table matches a high-`K` offline recompute.
3. **Pot-odds / MDF / bluff:** an agent's continue threshold = `s/(1+s)`; facing a size-`s` bet it defends ≈ `MDF = 1/(1+s)` of a uniform range; a value-betting agent at neutral aggression mixes bluffs at the §6.3 ratio `s/(1+s)` (GTO indifference), and `bluffMult` scales it monotonically.
4. **Betting mechanics + chip conservation:** blinds posted, button rotates, illegal actions throw (sub-min-raise, over-stack); **`Σ stacks + rake` is invariant across every hand** (side-pot layering conserves chips); a forced multi-all-in hand splits side pots correctly.
5. **Session:** buy-in validated to `[20·BB, min(funds,100·BB)]`; `StandUp` returns `stack − buyIn`; bust ends at `−buyIn`; bank-&-exit between hands works; forfeit is a clean no-op.
6. **EV / skill bands (§13):** over N seeded sessions per policy — **skill monotonicity** (TAG > Maniac/Station), **rake binds** (random hero negative), **field gradient** (TAG best at Low, worst at High).
7. **Raid tail:** a forced-raid seed seizes the on-table stack and writes `detection +10` / `gambling_bust` / `stress +20`; measured raid rate ≈ `1−(1−h)^n`; `Low` never raids.
8. **Determinism:** same seed ⇒ identical session resolution (shuffles + MC + AI decisions all from the one `RngState`).
9. **Zero-alloc:** a warm played hand and a warm full session each allocate ~0 B.
10. **Resolution → writers (Layer-2 integration, in whichever harness compiles Data — likely `GrittyEventsHarness`, as with 8b's check 9):** a `HoldemResolution` applied through `HustleService` moves `funds`/`detection_risk`/`recklessness` by the clamped deltas and sets `gambling_bust` with the right `set_on_day`.

---

## 15. Model split & follow-ons

- **Opus 4.8 (this pass):** the math, the state machine, and the contracts above. No code.
- **Sonnet 5 (implements)** — suggested sub-sequence, since Layer 1 here is larger than a single hustle (split into 8d-1 / 8d-2 if one session is tight):
  - **8d-1:** `HoldemEvaluator` + the 21-combo oracle + `HoldemEquity` (postflop MC) + the offline `--gen-preflop` table generation → `HoldemHarness` checks 1–2. This is the self-contained, highest-value core.
  - **8d-2:** `HoldemHand` betting state machine + side pots + `HoldemAgent` (§6/§7) + `HoldemSession` + the hero autopilot → checks 3–9.
  - **Layer 2:** `WorkActivity.Poker`; `HustleService.BuildHoldemContext`/`ApplyHoldemResolution`; the one `HustleResolution.SetGamblingBustFlag` field + `ApplyCore` flag branch; the `GameManager` pending-session arming/forfeit for Poker → check 10.
  - **Layer 3 (deferred follow-on slice, per this session's scope):** `Assets/UI/TexasHoldemTable.tscn`+`.cs` — **`godot_scene_mapper` before any `GetNode<T>()`**, per `ui_conventions.md`; renders the hand DTO + action log, emits `Fold`/`Check-Call`/`Raise-to` signals; the ScheduleScreen Work-activity selector already gains `Poker` when the enum extends.
- **Fable 5 (reviews):** Hold'em does **not** touch `AtBatResolver`/rivalry/the baseball sim assembly, so `run_monte_carlo_batch` is a lighter "confirm no band moved" than 8b's rivalry concern — but still re-run it (the standing rule) and sign off on the new `detection_risk`/`gambling_bust` writer's clamping + single-writer discipline, the chip-conservation invariant, the skill-monotonicity band, and the state-representation call (§10 struct-vs-pooled-class).
- **Deferred (noted, not built here):** the `.tscn`/UI slice (this session's explicit deferral); table **markers / `house_debt` / the health-tail collection cascade** (§11); multi-table/tournament play + ICM; opponent adaptation and physical tells; outs-based semi-bluff weighting (§6.4).
