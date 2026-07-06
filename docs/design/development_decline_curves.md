# Development & Decline Curves (Phase 9d)

**Owner split: Opus 4.8 (this design — the curve model, the potential/ceiling
concept, the Practice lever, the season-boundary pass, the `AgeBonus` seam
evolution); Sonnet 5 (the tuning harness — calibrating growth/decline/potential/
practice constants to a stationary league equilibrium); Fable 5 (implementation +
review).** Turns the interleave plan's one-liner — "peak-age growth / veteran
decline curves; the Practice block finally produces a real stat effect" — into
the mechanism that makes a player's *ratings themselves* move over a career. This
is the piece that makes a career an **arc** rather than a fixed talent vector that
only ever moves teams.

## 1. Thesis & scope

9c left the ladder **sorting** players by their *current* ratings + season line
and reaching a static, talent-stratified equilibrium: with fixed ratings, once
the world is sorted it stops moving except for boundary yo-yo, and the middle
minors "hover at the generation mean by construction" (9c's own harness
disclosure — no age caps, no retirements until the founding generation ages
through, so nothing perturbs them). 9d is the perturbation. It makes the sorted
quantity **move**:

- **Peak-age growth** lifts a young player's ratings season over season, so a
  raw intake *develops* toward his ceiling — and climbs the ladder **because he
  develops**, which is the BUILD_PLAN exit criterion ("a generated player climbs
  HS→MLB across simulated seasons via performance **+ development**") that 9c
  could only half-satisfy.
- **Veteran decline** erodes ratings past the peak, so an aging star slips down
  the ladder (via 9c's existing relegation machinery) and eventually retires —
  "he falls and then retires," with **zero new retirement logic**.
- **The Practice block** (built inert in 9b, still ticking `ActionCatalog.Idle`)
  finally produces a stat effect: the avatar's scheduled Practice hours
  accelerate his own development, tying the Life-sim daily clock directly to the
  Baseball-sim career — the first mechanic where *how the player spends his day*
  changes *how good his avatar gets*.

9d is deliberately paired with, and distinct from, **9c**: 9c *sorts* by ratings;
9d *moves* ratings. The promotion sweep consumes the moved ratings with **no
change to §3's conservation math** — it already reads ratings at evaluation time
and throws them away, so a ratings-that-drift world is invisible to the sweep's
roster-invariant proof. The two compose cleanly: at each season boundary the
world **ages → develops → sorts**, and a prospect who develops past the promotion
bar this offseason rises next season.

Load-bearing properties this design commits to:

- **The first pass that legitimately MOVES the tier bands.** Every prior 9x/8x
  pass was inert-until-triggered and signed off on "no band moved." 9d's *whole
  purpose* is to move the simulation's steady state. Its acceptance is therefore
  **not** "no band moved" but "the world reaches a **new stationary equilibrium**
  Sonnet's tuning harness calibrates the band checks to, with the middle-minors
  flatness now perturbed into a real gradient." The **MLB bit-identity regression
  guard still holds** (it is a same-seed, MLB-only, *no-rollover* world — 9d fires
  only on `SeasonRolledOverEvent`, so an un-rolled world is bit-identical to
  pre-9d). This distinction is the single most important framing in the doc; §7
  and §10 make it precise.
- **A well-defined steady state must EXIST to calibrate to.** Ratings are
  bounded [0,100], each player's growth asymptotes at a **stored per-player
  potential** drawn from a stationary generator distribution, and decline is
  bounded and feeds retirement. So the rostered-population rating distribution
  has a stationary limit that Sonnet tunes the growth/decline/rawness constants
  to hold inside the §4 bands. This is why the model needs potential (§3):
  without a per-player anchor, growth is either uniform (collapses the talent
  spread) or a random walk (no anchor to stabilize the mean, no meaning to
  scouting a prospect). The considered-and-rejected no-schema alternative is
  recorded in §3.4.
- **Development is a pure curve behind a season-boundary manager**, the exact
  `PromotionScore`/`HeirGenetics` precedent: an engine-free, DB-free, RNG-optional
  `DevelopmentCurve` the harness pins fixtures against, and a `DevelopmentManager`
  that bulk-loads, applies it, and writes back in one batch — the `AgeAllPlayers`
  + `PromotionManager` sibling.

## 2. The development curve

Every rostered player's ratings move once per offseason. The move is a pure
function of `(currentRating, potentialRating, age, ratingKind, health, practice
credit, jitter)` — `DevelopmentCurve`, a static class with the same Data-clean,
zero-DB, `bell`-injectable profile as `HeirGenetics` and `PromotionScore`. Every
constant lives in a `DevelopmentProfile` static (the `PromotionProfile`/
`HeirGeneticsProfile`/`TierEffects` precedent): retuning is a data edit behind the
tuning harness, never a logic edit.

The career has three phases, pivoting on **`PeakAge` — reused verbatim from
`PromotionProfile.PeakAge` (27)** so the promotion age projection and the
development peak can never disagree about when a player peaks.

### 2.1 Growth (age ≤ PeakAge) — exponential approach to potential

A young player closes a fraction of the gap to his ceiling each season:

```
gap        = potential_i − rating_i                       (≥ 0 by construction)
growthFrac = GrowthRate · youthWeight(age)                (largest young → 0 at peak)
rating_i  ← rating_i + round( growthFrac · gap + physicalWeight_i · jitter )
```

- `youthWeight(age)` tapers linearly (first pass) from 1.0 at the youngest
  playable age to 0.0 at `PeakAge` — the youngest players develop fastest, and
  growth naturally stops at the peak. A raw 15-year-old with a big gap gains
  several points a year; a 26-year-old already near his ceiling barely moves.
- Because growth is a *fraction of the remaining gap*, it **asymptotes at
  potential and never overshoots** — this is the property that makes the
  steady-state distribution stationary (§7): a player converges to his stored
  ceiling, drawn from the same generator distribution every generation.
- The **Practice credit** (§4) adds a bounded extra fraction of the gap for the
  avatar only — the player's lever to develop faster than the NPC baseline.

### 2.2 Peak (age ≈ PeakAge) — plateau

`youthWeight → 0` and the decline term (§2.3) is still ~0, so a player at his
peak holds his ratings (modulo jitter). No special-cased "plateau band" is
needed; it falls out of both curves going to zero at `PeakAge`.

### 2.3 Decline (age > PeakAge) — accelerating erosion off current rating

```
declineFrac = DeclineRate · ageWeight(age) · healthScale(health)
rating_i   ← rating_i − round( kindWeight_i · declineFrac + jitter )
```

- `ageWeight(age)` grows with age past the peak — **gentle** in the early 30s,
  **steep** approaching `MandatoryRetirementAge` (42) — so decline accelerates
  the way real aging does, and a player's ratings collapse toward the end,
  feeding the 9c relegation/washout cascade before the age-42 hard retirement
  even fires.
- Decline is off the player's **current** rating, not his potential, so a player
  who over- or under-developed declines from where he actually is.
- **`kindWeight_i` splits physical from skill tools.** Physical ratings
  (`BatPower`, `BatContact`, `PitStuff`, `PitStamina`, `Fielding`) decline at
  full weight; the *command/discipline* skills (`BatDiscipline`, `PitControl`)
  decline at a reduced weight — plate discipline and pitcher command hold up with
  age in real baseball, and a crafty-veteran archetype (declining stuff, intact
  control) is exactly the texture this produces. `kindWeight` is a first-pass
  `DevelopmentProfile` vector, the `TierEffects.DeltasByTier` precedent (a
  per-rating weight table, tunable as data).
- **`healthScale(health)`** couples decline to `health_ceiling`: a player whose
  health has been eroded (PED costs from 8b, injuries from 8c) declines faster —
  the death-spiral into the health≤40 retirement floor becomes coherent, reusing
  the *existing* health stat with no new state. First-pass `healthScale = 1` at
  full health rising modestly as health falls; a clean off-switch (coefficient 0)
  for the harness's isolation checks.

### 2.4 Jitter — the bust/breakout texture

A small zero-centred `bell` draw (the `HeirGenetics.Bell` sum-of-three-uniforms
shape, reused) is added per rating per season, scaled by the phase weight, so two
identical prospects don't develop identically — one breaks out, one busts. The
jitter is drawn from a **dedicated forked `RngState`** (§5) so it never perturbs
the six sims' or the career's streams. Every `DevelopmentCurve` function exposes
a deterministic `bell`-injecting core (the `HeirGenetics`/`AtBatResolver.Compute`
precedent) so the harness pins the §2 fixtures and the §10.7 stream-isolation
identity (jitter's contribution vanishes at `bell = 0`) without reverse-
engineering a seed.

## 3. Potential & the schema change (v10)

Growth is meaningless without an answer to "grow toward **what**." That answer is
a **stored per-player potential** — the latent ceiling each rating develops
toward. It is genuinely new state (a player's ceiling is not derivable from his
current ratings), so 9d takes the first schema change of the 9x line.

### 3.1 `Player_Potential` — schema v10

A new additive table mirroring `Player_Ratings`, the exact
`Pitcher_Roles`/`Team_Tiers`/`Player_Absences`/`Player_Equipment` pattern (a
separate table, never an `ALTER` on `Player_Ratings`, so the migration stays
purely additive and `SchemaDefinitions.sql` remains the whole migration):

```sql
CREATE TABLE IF NOT EXISTS Player_Potential (
    player_id      TEXT    PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    bat_power      INTEGER NOT NULL DEFAULT 50 CHECK (bat_power      BETWEEN 0 AND 100),
    bat_contact    INTEGER NOT NULL DEFAULT 50 CHECK (bat_contact    BETWEEN 0 AND 100),
    bat_discipline INTEGER NOT NULL DEFAULT 50 CHECK (bat_discipline BETWEEN 0 AND 100),
    pit_stuff      INTEGER NOT NULL DEFAULT 50 CHECK (pit_stuff      BETWEEN 0 AND 100),
    pit_control    INTEGER NOT NULL DEFAULT 50 CHECK (pit_control    BETWEEN 0 AND 100),
    pit_stamina    INTEGER NOT NULL DEFAULT 50 CHECK (pit_stamina    BETWEEN 0 AND 100),
    fielding       INTEGER NOT NULL DEFAULT 50 CHECK (fielding       BETWEEN 0 AND 100)
) STRICT;
```

Validate the DDL idempotence + the backfill + every new query plan on a scratch
db via the SQLite MCP (or `validate_sqlite_schema`) **before** any C# is written —
No Blind Queries. `PRAGMA user_version` bumps **9 → 10**; the three hardcoded
`user_version` checks bump on schedule (the standing gotcha), and
`SchemaValidator`'s `RequiredTables` gains `Player_Potential`.

### 3.2 Migration backfill — the founding generation is frozen at current

Unlike the "no backfill" additive tables, `Player_Potential` **backfills in pure
SQL** — the `Pitch_Arsenals`/`Team_Tiers` copy-from-`Player_Ratings` precedent:

```sql
INSERT OR IGNORE INTO Player_Potential
    (player_id, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding)
    SELECT player_id, bat_power, bat_contact, bat_discipline, pit_stuff, pit_control, pit_stamina, fielding
    FROM Player_Ratings;
```

Every pre-v10 player gets **potential = current** — zero headroom, so they only
ever **decline** (no sudden growth spurt on the first 9d boot). The existing world
does not lurch: the founding generation ages and declines out gracefully, while
fresh intake generated *after* v10 carries real headroom and develops. Within a
few in-game seasons the whole population is 9d-native. This is a controlled,
testable rollout, and `INSERT OR IGNORE` makes it idempotent on every boot (a
post-v10 world's intake wrote its own potential row, so the backfill is a no-op
for it).

### 3.3 Generating raw intake — where headroom comes from

`LeagueGenerator.GeneratePlayer` (and `EnsureTierLeagues`/`GenerateIfEmpty` via
it) gains a `Player_Potential` write. **Potential is rolled at the normal
generation distribution (mean 50 ± `DefaultRatingSpread`); current ratings are
rolled at a per-age discount below it** — a "prospect discount" that shrinks to
zero by `PeakAge`:

```
potential_i = RollRating(spread)                          (the existing roll — the ceiling)
rawDiscount = ProspectDiscount · youthWeight(genAge)      (big for a 15-yo, 0 for a 27+-yo)
current_i   = clamp(potential_i − rawDiscount · gapRoll, 0, potential_i)
```

So a fresh 15-year-old HS intake is **raw now / projectable later** (e.g. current
~40, potential ~55, some ~75), and a generated veteran (MLB intake at 21–36) is
at-or-near his ceiling already. The prospect discount is what makes intake
develop instead of entering fully-formed — and it is **the band-moving lever**
(§7): raw HS intake shifts the HS environment, so `ProspectDiscount` is the
single most consequential number for Sonnet to calibrate. First-pass it **mild**
(small discount) to minimize band disruption; a bigger discount buys more
dramatic development at the cost of a re-baselined HS band.

Heir generation (`CareerManager.ConceiveChild` → `HeirGenetics.BlendRatings`) and
avatar creation (`CareerManager.CreateAvatar`) write potential the same way:

- **Heir:** the blended vector is the heir's **potential**; the current vector is
  discounted by his birth-age youthWeight (a newborn/teen heir is raw, develops
  toward the blend). This makes the genetic ceiling a *ceiling*, reached only by
  developing — a nice reinforcement of the heir-mechanics story.
- **Avatar (create-a-player):** the player-chosen ratings are the current; a
  19-year-old's potential = current + a youth-headroom roll, so a modestly-built
  avatar has room to develop (via Practice) while a max-built avatar (current
  100) has zero headroom and only decline ahead — a real, disclosed risk/reward
  in character creation (front-load talent vs. leave room to grow).

### 3.4 Rejected alternative — no-schema incremental development

Considered and rejected: a schema-free model where each season `rating += Δ(age)
+ jitter` with no stored ceiling. It delivers growth-then-decline and a perturbed
equilibrium with zero schema change, but it **collapses the talent spread** (an
age-only additive Δ moves all young players identically — nothing distinguishes a
future star from org filler except accumulated jitter, which is a pure random
walk with no anchor), and it is **mean-drift-fragile** (with no ceiling, keeping
the population mean stationary requires balancing the age-curve integral against
a shifting age distribution — a tuning nightmare that 9d's own churn keeps
moving). Stored potential fixes both: it anchors each player's ceiling so spread
is stationary and scouting a prospect is meaningful, and it makes the steady state
well-defined (growth asymptotes at a stationarily-drawn ceiling). The additive
table is the project's most-worn, lowest-risk change; paying it here buys a
correct, tunable equilibrium. The realism-vs-schema trade lands on realism.

## 4. The Practice lever — the avatar's stat effect

The Practice block finally does something. The avatar's scheduled Practice hours
accumulate over a season into a **practice credit**, which adds a bounded extra
growth term in the offseason development pass — the avatar develops faster (or,
for a veteran, staves off a little decline: "staying in shape") than the NPC
age-curve baseline.

**Accumulation (Life → Baseball via the sanctioned bridge).** Practice hours live
in the Life-sim `DaySchedule`; development lives in Baseball. They must not
reference each other (the CoreLoop wall). `GameManager` — the established bridge
that already owns `SubmitDaySchedule` and the `AvatarChangedEvent` sync — is the
one place they meet: on each day advance it adds the avatar's actually-ticked
Practice hours to an additive `Game_State` key **`avatar_practice_credit`** (the
cost-of-living-cadence / hustle-pointer `Game_State` precedent; a KV write, no
schema for this part). `Game_State` is a Baseball-readable surface, so
`DevelopmentManager` consumes it without ever seeing the Life sim.

**Consumption.** At the rollover, `DevelopmentManager` reads `avatar_practice_credit`,
converts it (points-per-hour with **diminishing returns and a seasonal cap** —
you cannot grind a 40 to a 100 in one winter) into the avatar's extra growth
fraction, applies it, and **clears the key**. All three knobs live in
`DevelopmentProfile`.

**The succession guard (contract, not timing-luck).** Practice credit is the
*continuing* avatar's training investment. A freshly-succeeded heir must get
**none** of the retiree's credit, and the retiree's credit is discarded (he has
left for FA). Rather than depend on `AvatarChangedEvent` dispatch ordering vs. the
development pass, `DevelopmentManager` applies practice credit only when **no
succession occurred this rollover** (`Career.LastSuccession.Kind != Succeeded`),
and clears the key regardless. `GameManager` also clears the key on
`AvatarChangedEvent` (creation + succession), so a new bloodline always starts at
zero. Avatar-only in v1: NPCs have no schedule, exactly as 8a's cost-of-living
drain is avatar-only.

This is the concrete payoff loop the interleave plan promised: **plan Practice
hours → your prospect develops faster → he clears the promotion bar sooner → he
climbs the ladder.** The Life-sim day now feeds the Baseball-sim career.

## 5. When it runs & ordering

The pass fires on **`SeasonRolledOverEvent`** — the same yearly hook `AgeAllPlayers`
and the 9c sweep already use. The required order and how the subscription FIFO
delivers it:

1. **Six `LeagueSimulator`s** — flush/normalize the completed season (day-154
   flush already did it; safety net).
2. **`CareerManager`** — `AgeAllPlayers()` (world +1), then succession
   (retire → heir handoff, or a parked pending choice).
3. **`DevelopmentManager` (new), attached AFTER `CareerManager`** — reads the
   post-aging ages, moves every rostered player's ratings toward/away from their
   potential, applies the avatar's practice credit, writes back.
4. **`PromotionManager` (9c), attached AFTER `DevelopmentManager`** — its sweep
   reads the **just-developed** ratings for the scouting component `S` and the
   completed season's line for the performance component `P`, and sorts.

**Develop-before-sort is load-bearing and correct:** a player's development *this
offseason* is what pushes his `S` over the promotion bar, so he climbs *because he
developed* (§1). `P` still reflects the season he actually played at his old
ratings ("what he did"); `S` reflects his new ratings ("what he now is") — the two
signals stay coherent, and 9c's conservation math never sees the difference.

Two precedented correctness requirements Fable honors, both mirroring 9c:

- **Flush before re-init.** `SeasonRolledOverEvent` fires after the new-season
  day-1 `DayAdvancedEvent`, so the sims have one day of the new season in their
  in-memory arrays. Before mutating ratings, `DevelopmentManager` calls
  `FlushPending()` on each registered tier sim, then after its batch re-runs each
  sim's + the micro-sim's `Initialize()` so the developed ratings bake into the
  effective-rating arrays (via `TierEffects`, automatically — §9). One-game-stale
  roster on day 1, the disclosed succession/9c precedent.
- **One batch, forked RNG.** All `UpsertRatings` writes commit in a single batch
  (one-transaction-per-tick); the only RNG is the §2.4 jitter, from a **dedicated
  forked `RngState`** (`Split()`, the world-gen/9c precedent), so the six sims'
  and the career's streams are never perturbed. A pass that changes **no** rating
  (a spread-0 world, or an all-at-ceiling/all-frozen population) is a **complete
  no-op** — no flush, no re-init, bit-identical world (the empty-ledger-neutrality
  bar, and what keeps the stream-isolation check provable).

**Re-init coordination with 9c (disclosed, negligible).** When both fire, the
season boundary re-`Initialize()`s the six sims **twice** — once after
development's rating writes, once after promotion's team moves. Each is an
idempotent bulk load of a few hundred rows on once-per-simulated-year load-time
code; the decoupling (each manager owns its own flush/batch/re-init, each
independently harness-drivable — the property 9c deliberately preserved) is worth
the redundant reload. A future "unified season-boundary pass" is the optimization
if profiling ever flags it; it will not. Because development mutates the **DB**
(committed before the sweep runs), `PromotionManager.LoadRoster` reads the
developed ratings regardless of the sims' in-memory state, so the ordering is
correct even though development re-inits first.

**Avatar.** Development never rosters/unrosters or moves anyone, so the avatar's
team and micro-slot are unchanged; the micro re-init picks up his developed
ratings by his stable slot index. For parity with 9c and defensive safety,
`DevelopmentManager` calls `Career?.ReactivateAvatar()` after its re-init when a
career is attached (it re-resolves to the same team/slot — a no-op-equivalent that
guarantees the slot re-find against the freshly-initialized micro arrays).

## 6. The `AgeBonus` seam — from static projection to real headroom

§13 of the 9c doc names the seam precisely: `PromotionScore.AgeBonus` is "the
static projection 9d replaces with a real rating trajectory." Today
`Scouting(roleRatingSum, age)` adds a flat, age-only `AgeBonus(age)` — a fudge
that *pretends* young players project up because, pre-9d, they didn't actually
develop. With real development and stored potential, scouting can project a
player's **actual remaining headroom**:

- `PromotionManager` already loads ratings for the sweep; it now also loads each
  player's `Player_Potential` (for the development pass in the same season
  boundary). It computes **`headroom = Σ_i (potential_i − rating_i)`** over the
  role's ratings and passes it into the score.
- `PromotionScore.AgeBonus(age)` evolves into **`ProjectionBonus(age, headroom)`**
  (add the parameter; keep the 100-centred scale, the clamp, and the
  `PromotionProfile` constants). A young player far below his ceiling projects up
  a lot (genuine scouting upside); a young player already at his ceiling, or an
  old player, projects flat-to-down. The age term still bounds it (a raw 15-year-old
  with huge headroom is still a *project*, not promoted on ceiling alone — his low
  current ratings gate his `S`, exactly as they should).

This is a **small, contained** change to `PromotionScore` — one new parameter, no
touch to the §2.3 combine, the §3 conservation math, or the ties-on-`player_id`
rule. It directly honors §13 ("replace its static projection with a real rating
trajectory") and makes the scouting signal mean what it claims: *do your tools
project up.* The `age`-only overload can stay for any caller that has no potential
in hand, defaulting `headroom = 0` (→ the current behavior), so the change is
additive.

## 7. Mean-stationarity & the new equilibrium — the band-moving contract

**This is the framing that separates 9d from every prior pass.** 8c/8e/9c all
signed off on "no band moved" because they were inert until an event/rollover
*triggered* them and their triggered effect was local. 9d moves ratings for the
whole rostered population every offseason; its steady state is a *different* world.

The design guarantees a steady state **exists** (bounded ratings; growth
asymptotes at potential drawn from a stationary generator distribution; decline
bounded and feeding retirement) — so there is a well-defined equilibrium to
calibrate to. It does **not** guarantee that equilibrium equals 9a's calibrated
bands. Two forces move them:

- **Raw intake (`ProspectDiscount`)** lowers the entry-level rating in the
  amateur tiers, softening HS/College offense-and-pitching (both sides shift, so
  the net band move is smaller than the discount, but nonzero).
- **The growth/decline balance** sets where the rostered mean settles: young
  cohorts developing up, old cohorts declining down, in a mix whose stationary
  point Sonnet tunes.

**Sonnet's tuning-harness target (the explicit calibration contract):** over K
generations of headless rollovers with development enabled, each tier's rate
stats settle to a **stationary window**, and:

1. the **middle-minors flatness 9c documented is gone** — the middle rungs
   (MinorA/AA/AAA) now show a real, monotone talent gradient, because young
   developers rise through them and declining veterans fall through them (this is
   9d perturbing exactly the behavior 9c handed off);
2. if the stationary tier bands differ from 9a's §4 numbers, the band checks are
   **re-baselined to the new equilibrium** and the change is documented +
   signed off (the FIRST legitimate band move in the project — Fable is both
   implementer and reviewer here, so the sign-off is explicit and recorded);
3. the **MLB bit-identity regression guard stays exact** (no-rollover world),
   and 9d is **inert without a rollover** — the empty-ledger-neutrality bar.

The tuning levers, in rough order of impact: `ProspectDiscount`, `GrowthRate`,
`DeclineRate` + `ageWeight` shape, `kindWeight` (physical/skill split), potential
spread, jitter spread, and the Practice conversion (points/hour, cap, diminishing
returns). All are `DevelopmentProfile` data edits behind the tuning harness.

## 8. Wiring

New **`DevelopmentManager.cs`** (`Assets/Simulation/Baseball/`, Baseball-only —
never references the Life sim, so the CoreLoop boundary scan stays clean),
three-piece in the `PromotionManager.cs`/`HeirGenetics.cs` shape:

- **`DevelopmentProfile`** (static) — every §2–§4 constant as data: `PeakAge`
  (reused from `PromotionProfile`), `GrowthRate`, `DeclineRate`, the
  `ageWeight`/`youthWeight` shapes, the per-rating `kindWeight` vector, `healthScale`,
  `ProspectDiscount`, jitter spread, Practice points/hour + seasonal cap +
  diminishing-returns curve. Retirement/health/maturity constants **reused** from
  `HeirGeneticsProfile`, never duplicated.
- **`DevelopmentCurve`** (static, pure, DB-free, RNG-optional) — `GrowthDelta`,
  `DeclineDelta`, `DevelopRating(current, potential, age, kind, health, practiceFrac,
  bell)`, `Headroom(currentVector, potentialVector, role)`, and the intake
  `RollPotential`/`RollRawCurrent` helpers `LeagueGenerator` calls. Deterministic
  `bell`-injecting cores for the harness, the `HeirGenetics` precedent.
- **`DevelopmentManager`** (engine-free, harness-drivable) — subscribes to
  `SeasonRolledOverEvent`; `RunDevelopment(completedSeasonYear)` public so the
  harness drives it directly. Bulk-loads `PlayerRow`s (age/health), ratings, and
  potentials up front (never mid-pass); reads `avatar_practice_credit`; applies the
  curve per rostered player; one batch of `UpsertRatings`; flush → commit →
  re-`Initialize()` all registered sims + micro + `Career?.ReactivateAvatar()`.
  Optional `CareerManager? Career` attachment property (the 9c `Promotions.Career`
  precedent — a null career, every NPC-only harness world, simply skips the
  practice term).

**Query surface** (No Blind Queries — validate the plans on a scratch db first):
`PlayerPotentialRow` DTO (mirrors `PlayerRatingsRow`); `UpsertPotential(in row)`,
`LoadAllPotential(dict)` (bulk, keyed by `player_id`), `TryGetPotential(id, out row)`
on `BaseballQueries`. The development pass also needs every rostered player's age +
ratings; `LoadRoster` already yields ratings but **not age** — reuse
`PlayerQueries.LoadAll(List<PlayerRow>)` for the age/health map (the 9c pass loads
exactly this way), joined by `player_id` in memory. No new index (the potential
read is a bulk full load once per offseason — a cold read, documented on the SQL
constant, the 9c season-line precedent).

**`LeagueGenerator`** — `GeneratePlayer` writes the potential row (§3.3); the
existing `spread`/`age` plumbing already threads through. **`CareerManager`** —
`CreateAvatar`/`ConceiveChild` write potential (§3.3). **`PromotionScore`** —
`AgeBonus` → `ProjectionBonus` (§6). **`GameManager`** — construct
`DevelopmentManager` with a forked `RngState`, subscribe it **between**
`Career.AttachTo` and `Promotions.AttachTo` (the §5 order); accumulate
`avatar_practice_credit` on day advance and clear it on `AvatarChangedEvent`
(§4); expose a public `Development` property whose read surface (last pass's
per-rating movement summary, the `LastRun`/`LastSuccession` seam) a future
"player development" UI card renders.

## 9. Tier environment interaction (automatic — the 9a/9c dividend)

A developed rating is a **stored, tier-relative** `Player_Ratings` value, so the
destination environment is applied by `TierEffects` at the sim's re-`Initialize`
with **zero extra plumbing** — the same property that let 9c move players between
tiers for free. A prospect who develops `+6` stuff in MinorAAA works those six
points against the `−4` environment; promote him and the same stored value works
against MLB's `−0`. Development moves the stored ratings; 9a's baking and 9c's
sorting consume them unchanged. This is why development is a pure rating write and
needs no awareness of tiers, environments, fatigue, PED, rivalry, or equipment —
they all layer on top of the moved base at `Initialize`/per-PA exactly as before.

## 10. Acceptance surface

The sim assembly is touched (ratings move), so `run_monte_carlo_batch` re-runs in
full and the new suite lives in **MonteCarloHarness** (it already drives headless
seasons, careers, and the tier ladder). GrittyEvents/NeedsDecay are unaffected on
the engine side (Practice accumulation is a `GameManager`/`Game_State` bridge, not
a Life-engine change); `SchemaValidator` gains `Player_Potential` (§3.1).

1. **Inert-without-rollover neutrality.** With `DevelopmentManager` present but no
   season rolled over, the world is **bit-identical** to pre-9d; the MLB
   bit-identity regression guard (same-seed, MLB-only, no rollover) stays exact.
   Development fires only on `SeasonRolledOverEvent`. The empty-ledger-neutrality
   precedent.
2. **Deterministic curve fixtures** (`DevelopmentCurve`, `bell`-injected): a young
   player below potential gains toward it; growth → 0 at `PeakAge` and never
   overshoots potential; a veteran declines; decline accelerates with age; skill
   ratings (`kindWeight`) decline slower than physical; `healthScale` speeds
   decline as health falls; clamps at [0,100]; a no-headroom (potential = current)
   young player does **not** grow; practice credit adds a bounded, capped bonus.
3. **Growth arc — the exit criterion (hard assertion).** Seed a young,
   high-potential, *raw* prospect; over K offseasons his ratings rise toward
   potential (monotone up to the peak, within tolerance of the ceiling), then
   decline (monotone down). Combined with 9c: he **starts below the promotion bar
   and develops over it**, climbing HS→…→MLB *because his ratings moved* — the
   distinct-from-9c check (9c's cream-rises seeds an already-max player; 9d's
   seeds a raw one who must develop first).
4. **Decline → relegation → retirement arc.** An aging star declines season over
   season, slips down the ladder via 9c's *existing* relegation machinery, and
   washes out / hits the age-42 or health-40 retirement — asserting **no new
   retirement logic** and that decline feeds the 9c cascade.
5. **The Practice lever (the "Practice finally does something" proof).** Two
   identical avatars, one scheduling max Practice hours each day, one none; the
   practicer develops measurably faster / peaks higher, bounded by the seasonal
   cap. Drives it through the `avatar_practice_credit` accumulate/consume path.
6. **New stationary equilibrium (§7, the band-move contract).** Over K generations
   the tier bands settle to a stationary window; the middle-minors flatness 9c
   documented is **perturbed into a monotone gradient**; if the equilibrium bands
   differ from 9a's, they are re-baselined and signed off. Mean-stationarity holds
   (the rostered rating distribution does not drift generation over generation
   beyond tolerance).
7. **Determinism / stream isolation.** Same seed → same development; disabling
   jitter (`bell`/forked-stream off) leaves the six sims' and the career's RNG
   streams **bit-identical** (the fork doesn't perturb them). Two same-seed worlds
   produce byte-identical developed rosters.
8. **Conservation still holds.** Re-run 9c's roster-invariant suite green —
   development moves ratings, not teams, so 816 / 9+5+3 / intake ≡ removals is
   untouched.
9. **Scouting-seam (§6).** In the sweep's `S`, a high-headroom young player
   out-projects an equal-current-rating no-headroom player; the projection reflects
   real remaining headroom, not the old age-only fudge.
10. **Migration.** An existing v9 save migrates v9→v10 through two clean headless
    `--quit` boots (second proves idempotence): every player gets potential =
    current (no rating jump), 816 players / 48 teams / avatar intact, integrity ok.
    `SchemaValidator` v10 scratch + live audit green; the three `user_version`
    checks bumped 9→10.

## 11. Disclosed simplifications & first-pass constants

- **v1 is age-driven, not playing-time-driven** for NPCs — an NPC develops on his
  age curve regardless of whether he played 600 PA or rode the bench (the macro
  sim has no per-NPC playing-time signal worth threading in yet). The avatar's
  Practice is the one playing-time-like lever. A "development tracks in-season
  usage" refinement is deferred.
- **Shared `PeakAge`, no per-player peak jitter** — everyone peaks at 27 in v1
  (reused from `PromotionProfile.PeakAge`); a per-player jittered peak age is a
  disclosed extension (needs one more stored field or a derived roll).
- **Founding generation frozen at current** (potential = current on migration) —
  existing players decline-only; only post-v10 intake gets the full arc (§3.2).
- **Practice is avatar-only** — NPCs have no schedule (the 8a cost-of-living
  precedent); a "prospects develop faster in a good org" NPC analog is out of
  scope.
- **The band move is real and expected** (§7) — 9d is the first pass whose bands
  legitimately shift; treat a moved band here as a re-baseline-and-sign-off, not a
  regression.
- **Health→decline coupling reuses the existing stat** — no injury-specific
  development state; a low-health player just declines faster (§2.3). The PED
  "juice to develop faster, pay in health/detection" dark-narrative hook is a
  disclosed **future** lever, not v1 (it would give 8b's PED system a permanent-
  rating consequence; deferred to keep the pass contained).
- **All constants first-pass**, tunable behind the tuning harness: `GrowthRate`,
  `DeclineRate`, `ageWeight`/`youthWeight` shapes, `kindWeight` physical/skill
  split, `healthScale`, `ProspectDiscount`, jitter spread, Practice points/hour +
  cap; retirement (42) / health (40) / maturity (19) / peak (27) all **reused**,
  never re-declared.

## 12. Suggested implementation sub-sequence

Larger than one slice; split if a session is tight. **Sonnet owns the tuning
harness (the calibration in §7 is the deliverable); Fable owns the engine +
review.**

- **9d-1 — the curve + schema + generation (engine core).** `Player_Potential`
  (v10, DDL + backfill + validate-first), the query surface, `DevelopmentProfile`/
  `DevelopmentCurve`, raw-intake generation (§3.3) across `LeagueGenerator` +
  `CareerManager` create/conceive, and the `DevelopmentManager` season-boundary
  pass wired between `CareerManager` and `PromotionManager`. Acceptance 1–4, 7, 8,
  10. Self-contained; no Practice, no scouting-seam.
- **9d-2 — the Practice lever + the `AgeBonus` seam.** The `avatar_practice_credit`
  accumulate/consume bridge (§4) and `PromotionScore.AgeBonus → ProjectionBonus`
  (§6). Acceptance 5, 9.
- **Sonnet's tuning-harness pass (can run alongside 9d-1's engine landing):** the
  §7 equilibrium calibration — find the `ProspectDiscount`/growth/decline point
  that holds a stationary ladder with a perturbed middle-minors gradient, and set
  (and, if moved, re-baseline + document) the tier band checks. Acceptance 6.

Fable's review is heavier than 9c (9d **does** move the sim's steady state, unlike
9c which only moved players between sims): the **band-move sign-off** (§7 — the
first legitimate re-baseline, recorded in this doc's addendum), the **curve
correctness** fixtures (§10.2–.4), the **stream-isolation** proof (§10.7), the
**Practice-lever** proof (§10.5), and the **conservation-still-holds** re-run
(§10.8). The curve constants are the tuning surface; if a playtest shows the world
developing too fast or too slow, tune `GrowthRate`/`DeclineRate`/`ProspectDiscount`
first, then the Practice conversion.

## 13. What 9d closes

9d is the payload the whole 9x ladder was built to carry. 9a stacked six tier
environments; 9b built the daily clock (with Practice inert); 9c made the tiers a
ladder that *sorts*; 9d makes the sorted quantity *move* — a raw prospect develops
and climbs **because he develops**, a veteran declines and falls and retires, and
the Practice hours the player schedules are what tilt his own avatar's arc. It
converts the interleave plan's final development bullet and the BUILD_PLAN's
"climbs HS→MLB via performance + development" exit criterion into a mechanism,
retires the "static equilibrium until 9d" caveat 9c disclosed, and gives the
Practice block the stat effect 9b promised. The remaining dynamic-career hooks —
per-player peak jitter, usage-driven development, the PED-development dark lever —
are disclosed extensions off this same `DevelopmentCurve`/`DevelopmentProfile`
seam, each a data-or-small-code edit with the equilibrium already calibrated.

## 14. Addendum — the §7 tuning pass (Sonnet 5, calibration result)

**Closes acceptance check 6, the central 9d deliverable.** Built a throwaway
multi-generation exploration tool (not shipped) that reuses the exact
`DevelopmentManager`/`PromotionManager` sources to run dozens of simulated
offseasons and print the tier ladder's time-averaged talent equilibrium, so
`DevelopmentProfile` constants could be iterated against a real long-run
outcome instead of a single-season guess.

**Finding: the 9d-1 first-pass constants (`ProspectDiscount=20`,
`GrowthRate=0.5`, `DeclineRate=6.0`) do NOT reach a monotone equilibrium.**
Over 80+ simulated offseasons (real games, multiple seeds), College's
time-averaged mean batter talent settled *above* MinorA's — a genuine,
reproducible inversion, not sampling noise (low variance, confirmed across
seeds and run lengths from 60 to 150 years). Root cause: HS and College are
the *only* tiers with a hard age cap (19/23) — every resident is in the pure
**growth** phase, nobody ever declines there. MinorA/MinorAA/MinorAAA/MLB have
no age cap short of the 42-year mandatory retirement, so each accumulates a
long-tenured population that ages *past* its peak and **declines in place for
potentially 10+ years** before a merit swap (capped at 2/1/1 per boundary per
role per year — a `PromotionProfile` constant, deliberately left untouched by
this pass) or forced retirement finally moves it. At `DeclineRate=6.0` that
multi-year erosion dragged the pro tiers' means down far enough to sit *below*
College's pure-growth mean — the "middle-minors flatness" 9c disclosed turned
out to hide a real inversion once 9d's own decline term was strong enough to
matter.

**Fix: halve `DeclineRate` to 3.0** (`DevelopmentProfile.DeclineRate`, doc
comment on the constant records the same reasoning). This is a pure magnitude
change — same accelerating shape (`ageWeight` quadratic 27→42), same
`kindWeight`/`healthScale` structure — it just pushes the age at which erosion
first rounds to a nonzero rating point from 32 to 34 (full-health physical
rating; decline is imperceptible immediately post-peak, picks up through the
mid-30s to retirement). `GrowthRate` and `ProspectDiscount` were tested too (0.3–0.5 and
20–45 respectively) and did **not** fix the inversion on their own — `GrowthRate`
changes shift both amateur and pro tiers together (the college/pro gap is set
by *who declines*, not how fast anyone grows), and `ProspectDiscount` only
changes the entry point, not the standing-population dynamic that causes the
inversion. `DeclineRate=4.0` also cleared the inversion but by a thin,
seed-fragile margin (~0.5 talent points at the College→MinorA boundary);
`3.0` clears it with a comfortable, reproducible margin (2–3 points) without
being a drastic re-tune.

**The calibrated equilibrium** (80 simulated offseasons, `DefaultRatingSpread`,
no games attached — see the harness note below for why that's equivalent to a
real-games run for this measurement — averaged over generations 31–80 to
discard the initial-generation transient):

```text
HS 124.3  <  College 145.1  <  MinorA 147.1  <  MinorAA 150.2  <  MinorAAA 155.4  <  MLB 166.0
```

Strictly monotone across all six tiers — the middle-minors flatness 9c
documented is gone, replaced by a real gradient — and stationary (first-half
vs second-half of the measurement window drifts under 3 points per tier,
comfortably inside the harness's 10-point bound). Confirmed reproducible
across three different seeds and measurement windows (60, 80, and 150
simulated years) before being baked in.

**The permanent proof** lives in `Tools/MonteCarloHarness`'s new
"Phase 9d §7 tuning harness" suite (`RunDevelopmentEquilibriumSuite`): 80
offseasons of aging → development → promotion with **no `LeagueSimulator`s
attached** — the same "no games" convention `RunPromotionSweepSuite`/
`RunDevelopmentArcSuite` already use. With no season line, performance `P`
shrinks to a constant 100 for every candidate, so `PromotionScore.Combine(100,
S)` is a monotone affine function of `S` alone: every promotion/merit-swap
decision is identical to a real-games world where `P` varies, just ~5x faster
(empirically verified: a real-games run at the same seed and constants landed
within ~2 talent points of the no-games run, same monotonicity/stationarity
verdict). The suite asserts strict monotonicity of the time-averaged talent
across all six tiers, a stationarity bound, and that the 9c roster-conservation
law still holds across all 80 developed+promoted offseasons.

**No other re-baseline was needed.** The 9a §4 static single-season tier bands
(`RunTierLadderSuite`) never roll over a season and don't run
`DevelopmentManager`, so they're insensitive to `GrowthRate`/`DeclineRate` (only
`ProspectDiscount` could touch that check, and it wasn't changed here); the 9c
§10.9 pre-9d equilibrium check (`RunPromotionSweepSuite`) never attaches
`DevelopmentManager` either. Both re-ran green, unchanged, confirming the
`DeclineRate` retune is scoped exactly to the long-run developed equilibrium it
was meant to fix. Four existing 9d-1 pure-curve fixtures (`RunDevelopmentCurveSuite`'s
decline/kindWeight/healthScale/practice-relief checks, pinned at ages 33/39)
were re-pinned at ages 35/41 with recomputed values to match the new, gentler
decline onset; one 9d-1 growth-arc magnitude check (peak-to-age-39 decline
margin) was loosened from 20 to 10 points to match the halved rate. All are
documented inline at the edited call sites.

**Full verification:** `dotnet build DirtAndDiamonds.sln` 0/0 across all 8
projects. `MonteCarloHarness` 249/249 (+4: the new §7 suite's four checks;
the loosened threshold was re-pinned in place, not removed). Every
other harness re-run clean per the standing "sim assembly touched" rule:
`CoreLoopHarness` 22/22, `SchemaValidator` 70/70, `GrittyEventsHarness` 72/72,
`HustleHarness` 31/31, `HoldemHarness` 63/63, `NeedsDecayHarness` 69/69. No
schema change — this pass is a `DevelopmentProfile` constant retune plus one
new harness suite, exactly the "data edit behind the tuning harness" the
design promised.

**Handed to Fable 5 for the closing review** (§12's assignment): the band-move
sign-off (this addendum is the record), curve-correctness re-check against the
re-pinned ages, stream isolation, the Practice-lever proof, and the
conservation re-run — all re-verified above but Fable's sign-off is still the
authoritative closing step per the design's own review assignment.

## 15. Closing review — Fable 5 sign-off (2026-07-06)

**9d is CLOSED end to end. The §7 band move is SIGNED OFF** — the first
legitimate re-baseline in the project, per the §7 contract:

- **Band-move sign-off (§7 check 2).** The `DeclineRate` 6.0→3.0 retune is the
  right lever and the right magnitude: the inversion's root cause (HS/College
  are the only age-capped, pure-growth tiers; the pro tiers accumulate
  long-tenured decliners the capped merit swaps clear slowly) is a *population*
  dynamic, so `GrowthRate`/`ProspectDiscount` correctly failed to fix it, and
  4.0's ~0.5-point seed-fragile margin vs 3.0's 2–3-point margin justifies the
  full halving. The calibrated equilibrium re-ran exactly: **HS 124.3 <
  College 145.1 < MinorA 147.1 < MinorAA 150.2 < MinorAAA 155.4 < MLB 166.0**,
  strictly monotone, max first-vs-second-half drift +2.6 points (MinorA)
  against the 10-point stationarity bound. The scope claim verified: the 9a §4
  static bands and the 9c §10.9 pre-9d equilibrium re-ran green *unchanged*
  (neither runs `DevelopmentManager`), and the MLB bit-identity guard is exact
  — the only band that moved is the one 9d exists to move.
- **Curve correctness at the re-pinned ages (§10.2).** All five re-pins
  verified by independent hand computation: at `DeclineRate=3.0`, erosion
  0.853→1 at 35, 2.613→3 at 41, skill-half 1.307→1, frail-1.6× 4.181→4,
  practice-relieved 1.96→2. The loosened growth-arc margin (20→10) is sound:
  expected peak→39 bat-sum decline under the halved rate is ~20 points
  (observed 20: 261→241), so 10 still proves decline is real under jitter.
- **Stream isolation (§10.7), Practice lever (§10.5), conservation (§10.8)**
  all re-ran green: byte-identical same-seed developed rosters, the at-peak
  no-op pass bit-identical, practice changed exactly one player with every NPC
  bit-identical, and the roster invariant held across 12 (9c), 24 (arc) and 80
  (equilibrium) rollovers.
- **Full sweep:** build 0/0 across all 8 projects; MonteCarloHarness
  **249/249**; CoreLoop 22/22, SchemaValidator 70/70, GrittyEvents 72/72,
  Hustle 31/31, Holdem 63/63, NeedsDecay 69/69.
- **Review corrections (docs only, no code/behavior change):** the addendum's
  check-count arithmetic ("+1 net" → +4: the new suite adds four checks; the
  loosened threshold was re-pinned, not removed) and the old-constant erosion
  onset ("~30" → 32 exactly, by 6·t² ≥ 0.5 → age ≥ 31.3).

The exit criterion holds as designed: a raw prospect climbs because he
develops, a veteran declines into the existing relegation/retirement
machinery, and the Practice block finally moves a stat. Remaining follow-ons
(per-player peak jitter, usage-driven development, the PED-development lever)
stay disclosed extensions — nothing blocks the commit checkpoint.
