# Promotion & Advancement Gates (Phase 9c)

**Owner split: Opus 4.8 (this design — the promotion model, performance + scouting);
Fable 5 (the tier-transfer handoff — the avatar changes team *and* tier mid-save,
the succession-handoff's sibling, same roster-invariant discipline).** Turns the
interleave plan's one-liner —
"a generated player climbs HS→MLB across simulated seasons via performance +
development" — into the mechanism that moves players up and down the 9a tier
ladder each offseason. This is the piece that makes the ladder a *ladder* rather
than six sealed leagues.

## 1. Thesis & scope

Post-9a the world is six independent 8-team leagues (HS, College, MinorA,
MinorAA, MinorAAA, MLB), each a closed universe of `TeamCount(8) ×
RosterSizePerTeam(17)` = 136 rostered players (72 batters + 40 starters + 24
relievers), 816 across the ladder. A player is *born into* a tier and never
leaves it: the 9a log discloses its own dead-end — "NPCs never promote or age
out of a tier (a 15-year-old HS NPC ages upward in place across seasons)," and
succession "stays within the family franchise's tier — a cross-tier handoff is
9c's promotion seam" (`CareerManager.Succeed`'s own comment).

9c is the **sorting mechanism**. Each offseason it re-sorts players across the
ladder so that talent + production determine tier: the best HS players rise, the
worst MLB players fall, aged/broken players retire out, and fresh 15-year-olds
enter at the bottom. It is deliberately paired with, and distinct from, **9d**
(development/decline curves): 9c sorts players by their *current* ratings and
season line; 9d makes the ratings themselves *move* over a career. Until 9d
lands, the ladder reaches a **talent-stratified equilibrium** after a few
seasons (higher tiers hold higher raw ratings, marginal players yo-yo at the
boundaries) — which is itself the correct, testable behavior, not a gap.

Load-bearing properties this design commits to:

- **No schema change.** Every move is a `PlayerQueries.SetTeam` (existing), every
  retirement is `SetTeam(null)` (the succession retiree precedent), every intake
  is `LeagueGenerator.GeneratePlayer` (existing), and a player's tier is *derived*
  from their team's `Team_Tiers` row — moving teams moves tiers for free. Holds
  the standing "no migration a mechanic doesn't need" discipline (8b/8d).
- **The roster invariant is a hard conservation law**, proven exactly by the
  harness — the sibling of Hold'em's chip-conservation Σ-check and succession's
  9+5+3 preservation: after every offseason pass, **every tier still has exactly
  8 teams × (9 batters + 5 starters + 3 relievers)**, and the rostered population
  is constant at 816 (removals to free agency are matched one-for-one by HS
  intake).
- **The bulk NPC churn and the single avatar handoff are cleanly separated** —
  the same split 9a/succession already draw between "the bulk world" (set-based
  DB work) and "the one avatar" (`Succeed`'s one delicate mutation). Opus designs
  both; Fable implements the avatar handoff against §6's contract.

## 2. The advancement score

Every rostered non-avatar player is ranked, within their `(tier, role)` cohort,
by a single **advancement score** `A`. The brief names the two inputs directly —
*performance* ("did they earn it this season") and *scouting* ("can they handle
the next level") — and the tier-relative machinery of 9a is exactly what makes
them combine honestly across tiers.

### 2.1 Performance component `P` — tier-relative, reliability-weighted

Because ratings are tier-relative (50 = average in *every* tier) and each tier's
environment is baked into effective ratings at `Initialize` (`TierEffects`), a
player's *raw* season line already means "how they produced against
tier-appropriate competition." So `P` is their season rate stat expressed
relative to their **own tier's league average that season** — the same
tier-scoped aggregates the Monte Carlo band checks already read:

- **Batters:** `rawP = OPS_player / max(OPS_tierLeague, ε)` — an OPS+-style ratio,
  1.0 = a league-average bat. Tier OPS comes from
  `BaseballQueries.LoadLeagueBattingTotals(seasonYear, tier)` (OBP =
  (H+BB)/(AB+BB), SLG = TB/AB, TB = H+2·2B+3·3B... i.e. the exact
  `SqlNormalizeBattingRates` formula).
- **Pitchers:** `rawP = ERA_tierLeague / max(ERA_player, ε)` — ERA−-style,
  inverted so a lower ERA scores higher, from
  `LoadLeaguePitchingTotals(seasonYear, tier)` (ERA = 27·ER/outs). WHIP is a
  first-pass-unused secondary; keep the hook.
- **Reliability shrinkage** toward the tier mean, so a 3-PA fluke never
  out-ranks a full season: `P = 1 + w·(rawP − 1)`, with `w = PA/(PA + PA_k)` for
  batters (`IP/(IP + IP_k)` for pitchers). A regular sees ~600 PA over a 154-game
  round-robin, so `PA_k ≈ 200` shrinks a tiny sample most of the way back to 1.0
  while barely touching a full season. This is the standard `n/(n+k)`
  reliability weight, and it is what disqualifies a shadow call-up's discard-line
  fragment or an absence-shortened season from spuriously topping the cohort.

`P` is reported on a 100-centered scale (`100·P`) so it adds cleanly to `S`.

### 2.2 Scouting component `S` — projected talent (ratings + age)

Scouting values the *ceiling*, not the box score:

- `rawTalent` = the role-summed `Player_Ratings` — batter Power+Contact+Discipline,
  pitcher Stuff+Control+Stamina — the **exact metric** `FindDisplacedPlayer`,
  `EvaluateSuccession`, and `FindStrongestFreeAgent` already rank by. Reuse it
  verbatim (0–300, 150 = all-average).
- **Age bonus** — scouting projects *upside*, so two identical rating vectors are
  not equal prospects. `S_talent = rawTalent + ageBonus(age)`, where `ageBonus`
  is a modest, monotonically non-increasing function of age: the youngest players
  carry up to `+AgeBonusMax` projected points, tapering to 0 around `PeakAge`
  (≈27) and going mildly negative for veterans past it. First-pass a simple
  linear taper; the exact curve is a `PromotionProfile` data edit and is the
  natural dovetail into 9d's growth/decline. It is deliberately small — it breaks
  ties toward youth and keeps the amateur tiers young; it never overrides a real
  talent gap.

`S` is reported as `100·(S_talent / 150)` (100 = an all-average player of that
role), same scale as `P`.

### 2.3 The combine

`A = wP·P + wS·S`, `wP + wS = 1`, first-pass `wP = wS = 0.5`. Ties break on
`player_id` — the deterministic tiebreak already used everywhere
(`FindDisplacedPlayer`, `EvaluateSuccession`).

**Why both signals, and why tier-relative makes it work:** a genuine talent
posts gaudy numbers in a soft tier (HS pitchers are effectively −20 stuff, so a
90-power bat *mashes*) and merely-good numbers once promoted (MLB pitchers are
full-strength) — because the *competition* stiffens, not his stored ratings.
So `P` auto-recalibrates by tier (his tier-relative OPS+ falls toward 100 as he
climbs), while `S` (his high raw tools) is what *justified* the climb. A hot
season from a low-ceiling 30-year-old (`P` high, `S` low, age bonus negative) is
correctly out-ranked by a struggling blue-chip 17-year-old (`P` middling, `S`
high, age bonus max) — both signals must agree for a durable promotion. All
weights/constants are first-pass and tunable as data edits behind
`run_monte_carlo_batch`, exactly like the tier/rivalry/equipment deltas.

## 3. The conservation model & offseason algorithm

The ladder is not a pyramid — it is six equal 136-slot leagues (a disclosed 9a
simplification: uniform 8×17 across HS→MLB). Movement must therefore *conserve*
each tier at exactly 136, per role. Three operation types do this, each
individually conservation-safe:

| Operation | Effect on rostered counts | Conserves a tier because… |
|---|---|---|
| **Merit swap** (T↔T+1) | −1/+1 in T, +1/−1 in T+1 | matched exchange, net 0 both tiers |
| **Vacancy promotion** (T→T+1) | −1 in T, +1 in T+1 | leaves a hole in T the next tier down fills |
| **Removal** (retire/age-out/health) | −1 in that tier | opens a vacancy the cascade fills |
| **HS intake** (generate) | +1 in HS | terminates one cascade |

Because every removal ultimately terminates one cascade at HS with exactly one
generated intake, **#intake per year ≡ #removals per year**, and rostered
population is invariant at 816.

### 3.1 The removal set (who leaves the ladder this offseason)

A rostered, **non-avatar** player is removed to free agency (`SetTeam(null)`,
stats preserved — the legacy-DB philosophy and the succession-retiree precedent)
when any holds, evaluated on the *post-aging* age (§4):

- **Retirement:** `age ≥ MandatoryRetirementAge(42)` **or**
  `health_ceiling ≤ HealthRetirementFloor(40)` — the exact succession triggers
  (`HeirGeneticsProfile`), reused so NPC and avatar aging out share one rule.
- **Amateur age-out:** the player is in an amateur tier (HS/College) and
  `age > TierAgeCap[tier]` (first-pass HS ≈ 19, College ≈ 23) and did not earn
  promotion this cycle. This ends the "15-year-old ages to 30 in high school"
  nonsense 9a disclosed; the washed-out player is a low-tier free agent, still
  inside the signable window, available to the succession backfill pool.

Removals are the *only* thing that changes total population; everything else is
exchange.

### 3.2 The single top-down sweep (per role)

For each role R ∈ {Batter, Starter, Reliever} independently, sweep tiers from
**MLB(5) down to HS(0)**. Maintain `need[T]` = the number of role-R bodies tier
T must import to return to `8·n_R` (n_R ∈ {9,5,3}):

1. Start each tier's `need` at its §3.1 removals.
2. At tier T, `need[T]` also includes every role-R player T just **exported
   upward** to satisfy `need[T+1]` (vacancy promotions) or via a merit swap.
3. **Fill `need[T]` from T−1's best:** rank T−1's role-R players by `A`; the top
   `need[T]` (excluding the avatar, §5) are promoted into T. This defers `need[T]`
   downward as new vacancies in T−1.
4. **Merit swaps with T−1:** additionally, while the best remaining T−1 candidate
   out-ranks the worst T incumbent by more than `SwapMargin` and the per-boundary
   `SwapCap[R]` is not exhausted, swap them (the T−1 riser goes up, the T
   incumbent is *relegated* down to T−1). Swaps are net-zero; the margin provides
   **hysteresis** so a marginal "AAAA" player does not yo-yo every year.
5. At **HS(0)**, `need[0]` is satisfied by generating `need[0]` fresh intake
   players of role R (`LeagueGenerator.GeneratePlayer` at the young end of
   `TierAgeRolls[HS]` ≈ 15–16, plus `GenerateArsenal` for pitchers, at
   `DefaultRatingSpread` so intake carries real bust/star variance — the pool
   every future MLB star is drawn from).

Processing top-down means each tier hands its freshly-opened holes to the tier
below as that tier's `need`, and the whole ladder rebalances in one pass with
**zero cross-boundary bookkeeping beyond the running `need` counter** — each
boundary×role is an independent matched reconciliation, which is exactly what
makes the conservation law trivially provable and the harness check exact.

Role is preserved across every move (a starter fills a starter slot, etc.),
matching succession's role-matched displacement — this is what keeps each tier's
9/5/3 composition exact. A player never changes role via promotion (disclosed
§11).

### 3.3 Why the cascade is bounded

Cascade depth is bounded by the tier count (≤5 promotions per removal), and
removals per year are modest (only the aged/broken/aged-out), so annual movement
is small and the leagues stay recognizable season to season. `SwapCap[R]` bounds
merit churn independently.

## 4. When it runs & ordering

The pass fires on **`SeasonRolledOverEvent`** (the year boundary, absolute day
= k·365+1), the same yearly hook `CareerManager` already uses for aging and
succession. By that moment the completed season's stats are final: each
`LeagueSimulator` normalized its league at the day-154 cycle flush during the
season, and its rollover handler is only a mid-season-attach safety net.

Handler order is bus subscription order (per-channel FIFO, stated in
`GameManager`). The required sequence, and how to get it:

1. **Six `LeagueSimulator`s** — flush/normalize any unflushed prior-season stats
   (already done at day 154; safety net).
2. **`CareerManager`** — `AgeAllPlayers()` (world +1 year), then the succession
   check (avatar retire → heir handoff, or a parked pending-succession choice in
   the interactive game).
3. **`PromotionManager` (new, §8), attached last** — reads the just-completed
   season's tier-scoped lines and the post-aging ages, runs §3's removal set +
   sweep for the NPCs, then (§6) the avatar's own handoff.

Two correctness requirements Fable must honor, both precedented:

- **Flush before re-init.** `SeasonRolledOverEvent` fires *after* the
  `DayAdvancedEvent` for new-season day 1 (TimeManager publishes DayAdvanced then
  SeasonRolledOver), so the six background sims have already simulated one day of
  the new season into their in-memory arrays. Before mutating any tier's roster,
  `PromotionManager` calls `FlushPending()` on each affected tier sim — exactly
  what `CreateAvatar`/`Succeed` do — so that day's additive stats survive the
  re-init. The consequence is a **one-game-stale roster on new-season day 1** (1
  of 154 games played pre-promotion); disclosed §11, the succession-mid-season
  precedent.
- **One batch, forked RNG.** All `SetTeam`/`GeneratePlayer` mutations commit in a
  single batch transaction (the one-transaction-per-tick rule); the only RNG draw
  is HS intake generation, from a **dedicated forked `RngState`** (`Split()`, the
  world-gen/EnsureTierLeagues precedent) so the six sims' and the career's streams
  are never perturbed. After the batch, re-`Initialize()` all six tier sims + the
  micro-sim once, then re-activate the avatar (§6) — the `Succeed` tail,
  generalized.

## 5. Eligibility gates & the avatar exclusion

- The **avatar is excluded from the NPC removal set and the set-based sweep
  entirely** — its lifecycle is owned by succession (retirement) and §6 (tier
  transfer). The sweep and every `Find…` helper already tiebreak on `player_id`;
  the avatar id is filtered out before ranking.
- **Promotion candidates** need a real performance signal *or* they are fresh
  intake — a generated 15-year-old has no prior line and enters at HS only, never
  mid-ladder, so this is automatic.
- **Relegation** targets any non-avatar upper-tier player the sweep out-ranks by
  the margin; a removal-eligible player is *removed* (leaves the system) rather
  than relegated (which would just clog the tier below with someone about to
  retire anyway).

## 6. Avatar tier-transfer handoff — Fable's contract

This is the explicitly-assigned sibling of `Succeed`: the one delicate mutation
that moves the avatar's team **and** tier mid-save. Opus fixes the *gate and
destination*; Fable owns the *mutation mechanics*.

**Gate.** The avatar is ranked by the **same `A`** among its `(tier, role)`
cohort. It promotes when it clears the same bar an NPC would — i.e. the avatar's
`A` places it among the tier's role-R players who would rise (a vacancy
promotion or a merit swap out-ranking an upper-tier incumbent). This makes the
player's on-field production — their interactive at-bats and autopiloted team
games — *directly* drive the HS→MLB climb the exit criteria demand.

**Destination.** The avatar lands on a specific upper-tier **team + role slot**:
the slot of the incumbent it out-ranked (that incumbent relegates into the
avatar's vacated slot), or an open vacancy. The roster invariant is preserved by
the same matched-exchange logic as the NPC sweep — the avatar's move is one
element of the reconciliation, just applied through the careful single-avatar
path instead of the bulk batch.

**Mechanics (the `Succeed` dance, generalized cross-tier):**

- One batch: `SetTeam(avatar, newTeamId)`, the counterpart's `SetTeam`, any
  role-matched displacement/backfill (reuse `FindDisplacedPlayer` /
  `FindStrongestFreeAgent` / the generated-filler fallback verbatim).
- **FlushPending on both the origin and destination tier sims** before re-init
  (the avatar's stats and the day-1 blip must survive).
- Re-`Initialize()` both affected tier sims + the micro-sim, then the
  `ActivateAvatar` tail: `_avatarTeamIndex` **re-resolves against the *new*
  tier's** `LoadTeamsByTier` (the 9a tier-relative-index discipline — a game in
  the new tier's round-robin must schedule correctly), `_avatarSlot` re-finds,
  `SetAttendedTeam` re-established on the new tier's sim, and **`AvatarChangedEvent`
  republished** — which the 9b GameManager bridge already consumes to re-point
  the Life-sim avatar pointer and the tier-derived **school gate** (school stays
  available across HS→College, drops at College→MinorA) for free. The avatar
  keeps every prior-season stat row (they persist by `player_id`); only the team
  changes.

**Interactions:**

- **Succession precedence.** If a succession is pending or the lineage is over,
  the avatar-promotion step is **skipped that offseason** (the avatar is on its
  way out; don't also promote it) — the NPC churn still runs. A freshly-succeeded
  heir has no prior-season line, so its `P` shrinks to league-average and it is
  not promoted on its debut offseason (correct).
- **v1 recommendation — avatar promotes, never auto-relegates.** A slump costs
  the player the *promotion*, not their job; NPC relegation is still fully
  modeled and harness-proven. This is the kinder first-pass feel and the simpler
  first handoff; the hook to enable symmetric avatar relegation is a one-flag
  data edit. Flagged as a tunable, not a blocker.

## 7. No schema change

Stated up front (§1) and worth re-earning: promotion is `SetTeam`, removal is
`SetTeam(null)`, intake is `GeneratePlayer`, tier is derived from `Team_Tiers`
via the team, and season lines / ratings / ages all already exist. Nothing needs
to be *stored* that isn't — the advancement score is computed at evaluation time
and thrown away. `SchemaValidator`'s `user_version` stays at **9** (no bump — a
first for a 9x pass, and a genuine de-risking of Fable's implementation). New HS
intake players get no `Player_Absences`/`Player_Equipment`/`Life_Needs` rows
(defaults apply); retirees keep theirs harmlessly (CASCADE cleans only on hard
delete — the 8c/8e disclosure).

## 8. Wiring

New **`PromotionManager`** (`Assets/Simulation/Baseball/PromotionManager.cs`,
Baseball-only — never references the Life sim, so the CoreLoop boundary scan
stays clean). Constructed in `GameManager` with `DatabaseManager`,
`PlayerQueries`, `BaseballQueries`, `GameStateQueries` (avatar id),
`LeagueDirectory`, `MicroGame`, `GlobalState`, and a **forked `RngState`**;
subscribed to `SeasonRolledOverEvent` **after** `CareerManager.AttachTo` so the
§4 order holds. It reads through the existing tier-scoped query surface
(`LoadRosterByTier`, `LoadLeagueBattingTotals(year, tier)` /
`LoadLeaguePitchingTotals(year, tier)`, `LoadTeamsByTier`) and mutates through
`SetTeam` + `LeagueGenerator.GeneratePlayer`. All calibration lives in a
`PromotionProfile` static (the `HeirGeneticsProfile`/`TierEffects` precedent):
`wP/wS`, `PA_k/IP_k`, `AgeBonusMax/PeakAge`, `SwapMargin/SwapCap[R]`,
`TierAgeCap[]`. Retirement/maturity constants are **reused** from
`HeirGeneticsProfile`, not duplicated.

## 9. Tier environment interaction (automatic)

A moved player keeps their stored (tier-relative) `Player_Ratings`; the
destination tier's `TierEffects` delta bakes into their *effective* ratings at
that sim's re-`Initialize` — so a MinorAAA 50-stuff arm promoted to MLB now works
in the −0 environment against full-strength bats instead of AAA's −4, with **zero
extra plumbing**. This is why `P` is the *did-you-dominate-your-level* signal and
`S` the *do-your-tools-project-up* signal: the environment does the cross-tier
translation, and the ladder stratifies by raw ratings + the generation spread
(`DefaultRatingSpread`) until 9d makes ratings drift.

## 10. Acceptance surface

The sim assembly is touched, so `run_monte_carlo_batch` re-runs in full; the new
suite lives in **MonteCarloHarness** (it already drives whole headless seasons,
careers, and the tier ladder). GrittyEvents/NeedsDecay are unaffected (no
Life/Narrative surface); SchemaValidator is unchanged (§7).

1. **Neutrality guard.** A season with `PromotionManager` absent (or present but
   with no rollover yet) is **bit-identical** to the pre-9c world; the MLB
   bit-identity regression guard (PA/H/ER) and the full 9a tier ladder stay
   exact/in-band. The mechanic is inert until a season actually rolls over with
   it enabled — the empty-ledger-neutrality precedent (rivalry/availability/gear).
2. **Roster-invariant conservation (load-bearing).** After N simulated season
   rollovers with promotion enabled, assert per `(tier, team, role)` that every
   team is **exactly 9 batters + 5 starters + 3 relievers**, every tier is 8
   teams, total rostered = 816 every year, and each year's intake count equals
   its removal count. The chip-conservation / 9+5+3 sibling.
3. **Cream rises (the BUILD_PLAN exit criterion, as a hard assertion).** Seed one
   deliberately-elite (max-rating, young) HS player; over K seasons his
   `team_id`'s tier climbs strictly HS→…→MLB. Symmetric: a deliberately-terrible
   AAA/MLB player relegates.
4. **Retirement + intake balance.** Force an aged/injured/aged-out cohort; assert
   each removal is matched by exactly one cascade of role-matched promotions
   terminating in one HS intake of that role, retirees land in FA with stat rows
   intact, and no tier is left short.
5. **Performance shrinkage gate.** A tiny-sample hot line (3 PA, 1.000 OPS) does
   **not** out-rank a full-season star; a full-season strong line does.
6. **Scouting/age gate.** Equal ratings → the younger player promotes; equal age
   → the higher-rated promotes; a high-ceiling youth out-ranks a low-ceiling vet
   with a marginally better season (both signals bind).
7. **Avatar cross-tier handoff (Fable's proof).** A dominating avatar promotes to
   a real upper-tier team+slot; `_avatarTeamIndex` re-resolves against the new
   tier; pending-game scheduling lands in the new tier's round-robin;
   `AvatarChangedEvent` republished flips the school gate correctly across
   HS→College→MinorA; stat continuity across the move; both tier sims + micro
   re-init clean; skipped cleanly when a succession is pending.
8. **Determinism / stream isolation.** Same seed → same promotions; disabling
   intake generation leaves the six sims' and career's RNG streams bit-identical
   (the fork doesn't perturb them).
9. **Talent-stratified equilibrium.** After enough seasons with no rating drift
   (pre-9d), the ladder is talent-monotone across tiers and movement damps to
   boundary yo-yo only — the correct steady state, and the explicit handoff shape
   9d will then perturb.

## 11. Disclosed simplifications & first-pass constants

- **Uniform 8×17 pyramid** (equal-width tiers) — inherited from 9a; the ladder is
  six stacked leagues with promotion/relegation, not a narrowing pyramid.
- **Role never changes via promotion** — a starter stays a starter up and down the
  ladder; a "converted to reliever" mechanic is out of scope.
- **One-game-stale roster on new-season day 1** — promotions apply after that
  day's league games are simulated and flushed (§4); the succession-mid-season
  precedent.
- **Avatar promotes, never auto-relegates in v1** (§6) — a disclosed
  player-experience call, one flag from symmetric.
- **The free-agent pool grows unbounded** across a long dynasty (removals persist
  as historical rows) — a pre-existing property of the succession retiree path,
  not new; the legacy-DB philosophy. `LoadFreeAgents`'s age/health window already
  excludes retired-by-age/health rows from being re-signed.
- **Talent-stratified equilibrium until 9d** (§1/§10.9) — with static ratings the
  ladder stops moving once sorted; 9d's curves are what keep careers dynamic.
- **Tier-flat economy** — 8e already disclosed that gear/pricing is tier-flat and
  flagged "revisit with 9c's economy movement"; 9c *is* that movement, but
  economy re-pricing per tier is deliberately left to a follow-on (a promoted
  avatar keeps its funds/gear; tier-relative pricing is a separate lever).
- **All constants are first-pass** and tunable behind `run_monte_carlo_batch`:
  `wP=wS=0.5`, `PA_k≈200`/`IP_k≈40`, `AgeBonusMax` small with `PeakAge≈27`,
  `SwapMargin`/`SwapCap[R]` modest, `TierAgeCap` HS≈19/College≈23; retirement
  (42) / health (40) / maturity (19) reused from `HeirGeneticsProfile`.

## 12. Suggested implementation sub-sequence (Fable)

Larger than one hustle; split if a session is tight:

- **9c-1 — NPC churn engine + harness.** `PromotionManager` + `PromotionProfile`,
  the score, the removal set, the top-down sweep, HS intake, re-init — everything
  in §2–§5, §8. Acceptance checks 1–6, 8, 9. Self-contained; no avatar path.
- **9c-2 — Avatar tier-transfer handoff.** §6's `Succeed`-sibling mutation, the
  succession-precedence interaction, `AvatarChangedEvent`/school-gate wiring.
  Acceptance check 7.

Fable review is lighter than 8b (9c touches **no** `AtBatResolver` calibration —
only *which* players sit in which tier sim): a **neutrality "no band moved"**
`run_monte_carlo_batch` pass (the mechanic is inert without a rollover), the
**conservation sign-off** (check 2, the hard law), and the **avatar-handoff
roster-invariant sign-off** (check 7 — the delicate mutation). The score
constants are the tuning surface; if a playtest shows the ladder sorting too fast
or too slow, tune `SwapCap`/`SwapMargin` and the `wP/wS` split first.

## 13. Handoff to 9d

9c leaves the ladder *sorting* players by their current ratings + season line and
reaching a static equilibrium. 9d makes the sorted quantity move: peak-age growth
lifts a young intake's ratings season over season (so he climbs *because he
develops*, the full exit criterion), veteran decline erodes them (so he falls and
then retires), and the Practice block (built inert in 9b) finally produces the
stat effect that feeds this. The `ageBonus` in §2.2 is the seam — 9d replaces its
static projection with a real rating trajectory, and the promotion sweep consumes
the moved ratings with no change to §3's conservation math.
