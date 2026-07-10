# High School Person Layer (HS-0 design)

**Owner: Opus 4.8 (design). Implemented by HS-1…HS-6** per the approved plan
(`review-docs-game-idas-high-school-md-and-sequential-pillow.md`). This is the
contract the whole High School arc builds against: it fixes the semantics of the
schema-v11 person layer that already shipped (`Player_Person`,
`Family_Background`, `Phone_State`, `Player_Items`, `Child_Development`) and
pins every number, formula, and identity-contract the later phases must honor.

Everything here follows the house discipline already load-bearing across the
project: **modifiers are computed into EFFECTIVE values upstream of the unchanged
`AtBatResolver`, never a calibration-table edit** (the PED / fatigue / rivalry /
tier / equipment precedent), and **the neutral state is the identity by
contract** so the MLB bit-identity guard world stays byte-exact until HS-6
deliberately re-bands.

---

## 1. Thesis & scope

The person layer turns the start of every career — and every heir's career —
into a life-sim origin: who you are (academics / social / moral scalars), where
you came from (family wealth, parents, home), what you carry (phone, items,
transport), and how the people around you live their own lives. All of it
persists into College / Pro and feeds the succession loop that already exists
(`HeirGenetics`, `ConceiveChild`, `Succeed`).

Two hard boundaries keep this from destabilizing the shipped sim:

1. **The social/economic stats never touch the calibration path.** GPA,
   reputation, social status, attractiveness, charisma, etc. feed *events,
   dating, opportunities, and parental behavior* — never a baseball rating. Items
   buff only these stats. So the entire marketplace / phone / dating surface is
   calibration-inert by construction.
2. **Exactly three person stats feed the sim, and only at HS-6, and only
   capped and zero-centered:** `teamwork`, `confidence`, `happiness`
   (§6). Every other stat is invisible to `run_monte_carlo_batch`. None of the
   three is item-buyable, so money can never buy on-field performance through
   this layer (that lane belongs to `Player_Equipment`, §equipment doc).

The result: HS-1…HS-5 add zero calibration risk (the bit-identity guard must
stay byte-exact through all of them), and HS-6 is the single, planned, Fable-
owned re-band.

---

## 2. The person-stat model (curated core)

`Player_Person` is one row per person in the universe, NPCs included. `gpa` is
REAL on the real 0.0–4.0 academic scale (2.5 = neutral); the other twelve are
INTEGER 0–100 with **50 = neutral** (the `Player_Ratings` convention). The v11
backfill writes the neutral row for every existing `Players` id, and readers
never have to special-case an absent row.

### 2.1 Semantics — every stat has a consumer

| Stat | Group | Primary consumers | Moves via |
|---|---|---|---|
| `gpa` | Academic | College-recruiting events, parental approval | Weekly drift (§2.3) |
| `intelligence` | Academic | GPA drift input, "smart choice" event gates | Set at creation; events |
| `maturity` | Academic | Impulse-control event outcomes, parental approval, dating stability | Events; slow growth with age |
| `happiness` | Academic | Life-sim mood, stress interplay, **sim lever (§6)** | Actions/events + weak reversion (§2.3) |
| `charisma` | Social | Social-action outcomes, dating success, reputation gain rate | Actions (Hangout); events |
| `confidence` | Social | Dating/social outcomes, **sim lever (§6)** | Actions; events |
| `reputation` | Social | Social standing, event gates; **item-buffable (§5)** | Events; item aggregation |
| `social_status` | Social | Dating caste gap (parental approval), event gates; **item-buffable** | Family tier seed; item aggregation |
| `attractiveness` | Social | Dating opportunity (spec §3.2: unattractive → fewer chances, compensate by grinding/items); **item-buffable** | Backstory roll; item aggregation |
| `teamwork` | Social | Clubhouse-cancer hit, **sim lever (§6, defense)** | Events; NPC-autonomy tick |
| `morality` | Moral | Alignment derivation, peer-pressure outcomes, parental approval | Actions (Church); events |
| `discipline` | Moral | GPA drift input, resists VideoGames drift, peer-pressure resistance; **reserved sim lever** | Actions; events |
| `work_ethic` | Moral | Offseason-development nudge (HS-6+), job outcomes, self-buy-transport reward | Actions; transport self-buy |

The "overflow" personality traits the spec lists (Humility, Generosity,
Patience, Kindness, Humor, Leadership) are **not columns** — they are
`Entity_Flags` (`trait_generous`, `trait_leader`, …), chosen at creation or
earned, and read by event prerequisites without column sprawl.

### 2.2 Sticky, not decaying — the needs contrast

Person stats are **traits and reputation, not needs.** Unlike Hunger/Sleep,
they do **not** auto-decay toward 0. They are sticky: they change only when an
action or event moves them. This is a deliberate design statement — it is why
the person layer needs no per-hour decay engine and no `NeedsEngine`-style
tick. Two exceptions, both mild and both HS-4:

- **`gpa`** — the one true drift model. Weekly:
  `Δgpa = GpaBase · (SchoolAttendanceFrac) · f(intelligence, discipline) + StudyHoursTerm − PartnerDrag − StressDrag`,
  clamped 0.0–4.0. Neutral inputs (school fully attended, intelligence 50,
  discipline 50, no study, no partner, no stress) hold GPA at its current value
  (drift 0). Exact closed form is HS-4's to pin; the harness fixture asserts a
  hand-computed week.
- **`happiness`** — weak mean-reversion toward a per-person setpoint (default
  50, shifted by items/relationships), so a spike from an event bleeds back over
  ~a week. Reversion rate is small enough that action/event deltas dominate.

Everything else holds its value until moved.

### 2.3 Alignment is derived, never stored

`morality` maps to the spec's Good/Neutral/Troubled alignment at fixed
thresholds, computed at read:

| morality | Alignment |
|---|---|
| ≥ 67 | Good |
| 34 – 66 | Neutral |
| ≤ 33 | Troubled |

No `alignment` column exists; a single helper (`PersonAlignment.From(morality)`)
is the only source. Thresholds are first-pass tunable.

### 2.4 Stored base vs. effective (item buffs)

Three status stats accept passive item bonuses: `attractiveness`,
`social_status`, `reputation`. The **stored** column is the person's intrinsic
base; the **effective** value read by dating/events is
`clamp(base + Σ item modifiers, 0, 100)` computed at read time and **never
written back** — selling or losing an item reverts cleanly, the exact
`EquipmentLedger` "computed, not written" discipline (§5.2). No other stat takes
item buffs.

### 2.5 NPC seeding policy (the bit-identity guard)

- The v11 backfill and any pre-HS-2 path write the **exact neutral row** (all
  50 / gpa 2.5). The Monte Carlo bit-identity guard world is seeded from exactly
  these neutral rows.
- HS-2's world-gen (`LeagueGenerator`) may seed organic NPCs with a **modest
  triangular spread around 50** (`Bell`-shaped, the `LeagueGenerator.RollRating`
  draw) so the universe isn't uniformly average.
- **Rolled NPC spreads must not enter the sim path before HS-6.** Before HS-6
  nothing reads person stats into a rating, so the spread is narratively live but
  calibration-inert. At HS-6 the seeded neutral guard world (all 50s → §6 delta 0
  → identity) stays byte-exact; live worlds with spread get the planned re-band.

---

## 3. Wealth tiers (`Family_Background`)

Backstory generation (HS-2's pure `BackstoryGenerator`) rolls one of five wealth
tiers, which sets the family's economic reality: starting funds, weekly
allowance, phone hardware/plan, home Wi-Fi, and the transport the parents gift
(or don't). `wealth_tier` is stored; everything else in the table is derived
from it at generation and then persisted.

| Tier | Name | Freq | Household income | Start funds | Allowance/wk | Phone tier | Phone plan | Home Wi-Fi | Parental transport |
|---|---|---|---|---|---|---|---|---|---|
| 0 | Destitute | 12% | ~$14k | $50 | $0 | 1 (burner) | 0 (prepaid) | No | none |
| 1 | Working-class | 28% | ~$40k | $200 | $10 | 1 (burner) | 0 (prepaid) | No | none |
| 2 | Middle-class | 35% | ~$72k | **$500** | $25 | 2 (mid) | 1 (basic) | Yes | Bike |
| 3 | Comfortable | 18% | ~$135k | $1,500 | $60 | 2 (mid) | 1 (basic) | Yes | Used car |
| 4 | Wealthy | 7% | ~$320k | $5,000 | $150 | 3 (flagship) | 2 (unlimited) | Yes | New car + auto-buys |

**Continuity anchor:** tier 2 (the modal 35%) starting funds = **$500** = the
shipped flat `CareerManager.StartingFunds`. A "typical" new avatar is therefore
economically unchanged; the tiers spread wealth *around* the value the economy
was already tuned against, so 8a's cost-of-living / Legal Work / hustle math
still lands where it was calibrated.

- **`household_income`** is flavor + a coarse input to a few events (a rich kid's
  "buy your way out" branch); it does not directly meter gameplay.
- **`home_wifi`** follows the spec verbatim (§4.2): the poorest families (tiers
  0–1) have no home internet, forcing travel to free-Wi-Fi locations (§4).
- **`strictness`** (0–100) is rolled **independently** of wealth — a triangular
  draw around 50 — so wealth and parental strictness are orthogonal narrative
  axes (a strict poor family, a permissive rich one). It gates HS-5 dating
  approval and the sneaking-out risk.
- **`allowance_weekly`** is paid on the weekly family tick (the cost-of-living
  cadence), the steady drip that funds the phone/marketplace economy for a
  player with no job.

### 3.1 Transportation (spec §4.5)

Transport is **owning a `Player_Items` entry of category Transport** — there is
no separate transport table. The wealth tier decides what the parents gift:

- **Tiers 3–4 gift a car** at creation (age 16); tier 2 gifts a **bike**. The
  gift is a normal `Player_Items` insert with `acquired_day = 0`.
- **Tiers 0–1 get nothing** and must self-buy a bike or car. Buying your own
  transport pays a **one-time person-stat reward** — `+work_ethic`,
  `+discipline`, and `+maturity` (the spec's "responsibility") — that gifted
  players never receive. This is the compensating mechanic: poverty costs money
  and time but builds character the wealthy don't earn.
- Transport **refunds schedule hours** (§5.3): a car frees ~1 h/day over
  walking, a bike ~0.5 h/day.

### 3.2 Parental auto-purchase

On the weekly family tick, tier-3/4 parents autonomously buy tier-appropriate
items for the avatar (a phone upgrade, nicer clothes, a car at 16) when the
avatar doesn't already own something at that level. Each catalog item carries an
optional `autobuy_min_tier` (§5.1); the tick buys the highest-value item the
family tier qualifies for and the avatar lacks. Poorer players save up and buy
their own — the same purchase flow, no parental gift.

---

## 4. The smartphone economy (`Phone_State`)

The phone is the UI hub (`BurnerPhone.tscn`). Two orthogonal axes: **hardware
tier gates features**, **plan + minutes meters usage**.

### 4.1 Hardware tiers (`tier` 1–3)

| Tier | Name | Unlocks |
|---|---|---|
| 1 | Burner | Messages + Bank only |
| 2 | Mid | + Marketplace |
| 3 | Flagship | + Social/Contacts app, smoother transitions |

Locked tabs render **visible-but-disabled with an upgrade hint** (never hidden),
so the player sees what a better phone would buy. Upgrading is a Marketplace
purchase that rewrites `Phone_State.tier`.

### 4.2 Plans & minutes (`plan` 0–2, `minutes_remaining`)

| Plan | Name | Metering |
|---|---|---|
| 0 | Prepaid | Pay-per-minute; buy minute bundles at the carrier |
| 1 | Basic | Weekly minute allotment auto-refilled on the family tick |
| 2 | Unlimited | No metering — bypasses all minute accounting |

First-pass minute economy (tunable data):

| Action | Cost |
|---|---|
| Carrier bundle purchase | **$10 → 100 minutes** (prepaid top-up) |
| Basic-plan weekly refill | 50 minutes / week (auto, plan 1) |
| Initiate a text/DM to a contact | 2 min |
| Voice-call a contact | 6 min |
| Browse the Marketplace (per session) | 3 min |
| Long-distance relationship weekly upkeep (HS-5 hometown anchor) | 20 min / week |

**Wi-Fi bypass:** any phone action taken while on Wi-Fi is free. Wi-Fi is
available at home iff `home_wifi` (tiers 2–4), or at a **free-Wi-Fi location**
(school, library) via a schedule action that costs a **time block** instead of
minutes. A tier-0/1 kid with no home Wi-Fi and a $10/wk allowance genuinely
can't afford many minutes and must trade travel time for connectivity — exactly
the spec's intent. Unlimited (plan 2) never touches any of this.

### 4.3 The narrative-never-gates invariant (hard rule)

**Answering or resolving a pending gritty-event thread is never minute-gated and
never Wi-Fi-gated — it always costs 0 minutes.** Minutes gate only
*player-initiated* actions (calling a friend, browsing the shop). With no
minutes, the player simply can't initiate; any pending event still falls to
**autopilot resolution** (the established forfeit precedent —
`EventConsequenceApplier`'s weighted autopilot choice). The Gritty Event
dispatcher can therefore never stall on an empty minute balance. HS-3 must wire
this so the pending-event path and the minute-metered path are physically
separate.

---

## 5. The item catalog (`Assets/Data/Items/items.json`, HS-3)

A content JSON, loaded and **validated loudly at load** (every `item_id` a
`Player_Items` row references must exist; category must match). It is content,
not schema — hence `Player_Items.item_id` is not an FK.

### 5.1 Entry format

```json
{
  "id": "used_sedan",
  "name": "Used Sedan",
  "category": "Transport",
  "price": 1200,
  "modifiers": { "social_status": 4, "reputation": 2 },
  "transport_hours_saved": 1.0,
  "autobuy_min_tier": 3
}
```

| Field | Meaning |
|---|---|
| `id` | Stable key stored in `Player_Items.item_id`; never reused |
| `name` | UI copy (player-facing text lives in content, not C#) |
| `category` | `Transport` / `Clothing` / `Jewelry` / `Food` / `Gear` → `ItemCategory` enum 1–5 |
| `price` | Purchase cost against `Players.funds` (atomic `AdjustFunds`) |
| `modifiers` | Passive person-stat buffs, **status stats only** (`attractiveness` / `social_status` / `reputation`) — see §5.2 |
| `transport_hours_saved` | Transport only: daily schedule hours refunded (car ~1.0, bike ~0.5) |
| `autobuy_min_tier` | Optional: wealth tier at/above which parents may auto-gift this (§3.2) |

`Food` items are consumable narrative flavor + minor `happiness` / need nudges
(the one place a catalog item may touch `happiness`, applied as an event, not a
standing passive); `Gear` here is lifestyle gear — **baseball gear quality stays
in `Player_Equipment`**, untouched.

### 5.2 Passive-modifier math (calibration-inert)

Owned items contribute a **passive additive buff** to the three status stats,
aggregated and clamped at read, never persisted:

```
effective_stat = clamp( base_stat + Σ_owned modifiers[stat], 0, 100 )
buff is further capped:  Σ_owned modifiers[stat] ≤ ItemBuffCap   (default 15)
```

The per-stat aggregate cap (default **+15**) stops a player rocketing
`social_status` to 100 by hoarding jewelry; laddering into better single items
matters more than sheer count. Because these three stats **do not feed the sim**
(§6 reads only teamwork/confidence/happiness, none item-buyable), the entire
item system is invisible to `run_monte_carlo_batch` — no bit-identity concern,
no re-band. This is the same clean separation equipment quality made, inverted:
equipment buys ratings and never touches social stats; items buy social stats
and never touch ratings.

### 5.3 Transport hours

A Transport item's `transport_hours_saved` is refunded to the daily schedule by
the Time Manager (HS-4) — the highest-value owned transport wins (a car
supersedes a bike). This is the mechanical payoff of owning a car and the thing
a parental car-gift or a self-buy actually delivers.

---

## 6. Person → performance: the zero-at-50 modifier (HS-6 contract)

This is the **only** path from the person layer into the sim, and it lands in
HS-6 as a new `PersonEffects` layer — the exact `TierEffects` / `RivalryEffects`
/ `EquipmentEffects` precedent: constant, zero-centered, capped effective-rating
deltas fed into the **unchanged** `AtBatResolver`, baked at `Initialize()`
(macro) and per-game (micro).

### 6.1 The formula

For a person stat `s` (0–100) driving a rating, the effective-rating delta in
points is:

```
PointsFor(s, cap) = clamp( round_away_from_zero( cap · (s − 50) / 50 ), −cap, +cap )
```

- **Zero-at-50 by contract:** `PointsFor(50, cap) = 0` for every cap. All-neutral
  person stats therefore leave every effective rating bit-identical — the MLB
  bit-identity guard (seeded all-50) stays byte-exact. This is the direct analog
  of `TierEffects`' "MLB vector all-zero by contract" and `EquipmentEffects`'
  "quality 0 is identity."
- **Linear, symmetric:** `s = 100 → +cap`, `s = 0 → −cap`.
- **Rounded away-from-zero, then clamped** to the rating's [0,100] via
  `TierEffects.Shift` (reuse, don't duplicate the clamp).

### 6.2 The mapping (first-pass, per role)

| Person stat | Batter effect | Pitcher effect | Cap |
|---|---|---|---|
| `confidence` | `bat_power` | `pit_stuff` | ±2 |
| `happiness` | `bat_contact` | `pit_control` | ±2 |
| `teamwork` | team defense (`fielding`) | team defense (`fielding`) | ±2 |

- **Cap is ±2 by default, tunable up to ±3** (the plan's ±2–3 band). On the
  resolver's ~50-points-per-σ scale that is ≈ ±0.04–0.06σ per knob —
  deliberately *smaller* than equipment's +6 (+0.12σ): who you are nudges the
  margins; it does not swing a career.
- **`bat_discipline` / `pit_stamina` are untouched** — plate judgment and
  endurance belong to the discipline rating and the fatigue model respectively
  (the equipment-doc precedent). **`discipline` (the person stat) → command is a
  reserved fourth lever, deferred** past HS-6's first band so the initial re-band
  moves as few knobs as possible.
- **`teamwork` → team defense** is a season-stable, roster-load-time bake (like
  tier), never a per-PA move — a shadowed replacement call-up carries none of it,
  the same rule rivalry/gear already follow.

### 6.3 Order of operations

`PersonEffects` slots into the existing effective-ratings chain at the same
`Initialize`/roster-load stage as tier, before the per-PA modifiers:

```
base rating → tier delta → person delta (§6) → [per-PA: gear ↑ → rust ↓ → rivalry → resolver PED ×1.5]
```

Order only matters at the 0/100 clamps and is harness-pinned so it can never
silently flip. The all-neutral fast path (`personDelta == 0`) resolves through
the exact pre-HS-6 call — same conjunction trick tier/gear/rivalry already use.

### 6.4 The re-band

HS-6 is the single sanctioned calibration touch. The neutral guard world stays
byte-exact (§2.5); live worlds carrying NPC person-spread get a documented
`run_monte_carlo_batch` re-band with golden resets recorded where §4/§8 bands
move — Fable-owned, exactly as the plan's HS-6 states.

### 6.5 The per-game refresh (PersonLedger — the HS-6 disclosure, closed)

§6.2's season-stable bake read literally meant in-season lever movement reached
the sim only at the next re-Initialize beat. The follow-up closes that for
**attended games only**: a `PersonLedger` (the EquipmentLedger transport
pattern) mirrors committed lever values off the bus, and `MicroGame` re-bakes
affected slots at game start — never mid-game, never a DB read (micro runs off
the main thread).

- **Transport:** `PersonLeversChangedEvent`, published AFTER the writing batch
  commits, carrying the row's **read-back absolutes** (never the nominal nudge
  — `AdjustStat` clamps in SQL, so only the row knows what moved). Publishers:
  the HS-4 daily settle flush (GameManager) and the gritty `person_stat`
  consequence (EventConsequenceApplier). ItemService's §3.1 reward touches no
  lever (work_ethic/discipline/maturity), so it stays silent.
- **No boot seed, by design:** Initialize already bakes every persisted row;
  the ledger carries only post-boot movement. Empty ledger = the Initialize
  bake stands = bit-identity (every harness guard world). Absolutes make a
  survivor from before a re-Initialize beat redundant, never stale.
- **Refresh ≡ re-Initialize:** the refresh shares the ONE bake site
  (`BakeSlotRatings` + the retained fielding/teamwork sums) with Initialize,
  so whenever ledger and `Player_Person` agree — the read-back contract — the
  per-game refresh lands byte-identical arrays to a fresh Initialize.
  Harness-pinned (suite 10 section (i)).
- **Scope:** the macro (tier) sims deliberately stay on the §6.2 season-stable
  bake — background NPC seasons don't chase daily mood. Autopilot games played
  inside the same day-tick dispatch see the previous pump's levers (one-pump
  lag); player-attended games always cross a pump and see everything settled
  through the last day tick.

---

## 7. Nurture: child-rearing blend (`Child_Development`)

Full child-rearing (locked decision #3): an heir's final traits are **nature
blended with nurture**, not the one-shot conception roll alone. The nature
baseline is `HeirGenetics` (unchanged); nurture is the `Child_Development` axes
accumulated over the rearing years, folded in at maturity/takeover.

### 7.1 The axes

`Child_Development` holds four 0–100 axes per child, neutral start
**care 50 / coaching 50 / funding 50 / neglect 0**:

| Axis | Fed by | Blends into |
|---|---|---|
| `care` | Time spent, rearing events, the Family phone section | `baseball_interest` |
| `coaching` | Backyard-catch / lessons events, funded coaching | Potential ratings (skill ceiling) |
| `funding` | Money allocated to the child (equipment, camps) | Potential ratings (skill ceiling) |
| `neglect` | Missed obligations, abandonment choices | Drags both, the failure axis |

Axes accumulate from HS-5 rearing events plus a weekly family tick that applies
the player's time/funds allocation. A recurring child-support expense rides the
cost-of-living cadence.

### 7.2 The blend (at maturity / takeover)

Nature is fixed at conception: `HeirGenetics.BlendRatings` → the heir's Potential
row, `HeirGenetics.RollInterest(childEdgeAffinity)` → a provisional interest.
At maturity/takeover (`EvaluateSuccession` already reveals interest there),
`NurtureBlend.Apply` folds in the accumulated axes — same zero-centered,
round-away-from-zero, clamped discipline as everything else:

```
nurtureΔ_potential = clamp( round( PotentialNurtureCap ·
        ( wCoach·(coaching−50)/50 + wFund·(funding−50)/50 − wNeglect·(neglect/100) ) ),
      −PotentialNurtureCap, +PotentialNurtureCap )

final_potential_r  = clamp( nature_potential_r + nurtureΔ_potential, 0, 100 )   // per rating

nurtureΔ_interest  = clamp( round( InterestNurtureCap · ( (care−50)/50 − (neglect/100) ) ),
                            −InterestNurtureCap, +InterestNurtureCap )

final_interest     = clamp( nature_interest + nurtureΔ_interest, 0, 100 )
```

First-pass constants (tunable data, `NurtureProfile` beside `HeirGeneticsProfile`):

| Constant | Value | Note |
|---|---|---|
| `PotentialNurtureCap` | ±8 pts | Real career lever, but nature dominates |
| `wCoach` / `wFund` / `wNeglect` | 0.5 / 0.3 / 0.5 | Coaching leads, funding supports, neglect bites |
| `InterestNurtureCap` | ±20 pts | The failure engine — nurture can cross the willing threshold (40) either way |

### 7.3 The nurture identity contract

At neutral axes (**care 50 / coaching 50 / funding 50 / neglect 0**) both deltas
are exactly 0, so `final = nature`. This makes **"no `Child_Development` row" and
"a neutral row" produce the identical pure-nature heir** — exactly what the v11
schema comment promises ("No row = pure-nature heir"). Existing heirs and NPC
children are untouched, and the `MonteCarloHarness` heir fixtures stay valid.

Per spec: a **neglected child can still become a star** (nature can carry an
+8-capped nurture penalty) and a doted-on one can still bust — nurture shifts the
odds, it does not determine the outcome. "Not guaranteed… develops dynamically."

---

## 8. NPC autonomy tick (HS-5)

The spec (§4.3) requires NPCs to live their own social lives — form friendships,
rivalries, romances without the player — because "dating the rival's ex" is
impossible unless NPCs *have* exes. Today `RelationshipGraph` is written only by
events, so this is genuinely new.

### 8.1 Budget & determinism

- **Weekly cadence** (the family-tick/`dayOfSeason % 7` beat), deterministic,
  driven by a **dedicated RNG stream split off the world seed** (`RngState.Split`
  — the schema-v4 precedent) so it can never perturb the sim's calibrated draw
  order.
- **Hard budget: `MaxPairInteractionsPerWeek` (default 256)** pair-interactions,
  regardless of population — this is what keeps 800+ NPCs from an O(N²) blow-up.
  Candidates are drawn by priority: (1) same-team pairs, (2) pairs with an
  existing edge (deepen or decay), (3) a bounded random sample of the rest.
- Each interaction nudges affinity by a small delta (±1…±3) from person-stat
  compatibility (charisma/teamwork proximity); occasionally (low probability)
  promotes an edge to Friend / Rival / Partner. Partner edges among NPCs are the
  exes the dating layer needs.

### 8.2 Conservation & isolation

- The tick **never mutates the avatar's own edges** — player agency stays
  event/UI-driven; the avatar is excluded from the candidate pool.
- NPC-NPC edges do **not** enter the sim before HS-6, and even then only through
  the §6 person-stat path on *rostered* players; the existing rivalry→PA
  transport is untouched and remains the only edge-driven sim effect until then.
- The tick writes through `RelationshipGraph` (publish-only) exactly like the
  event path, so persistence rides the shipped `CollectDirty` → day-tick/exit
  flush with no new plumbing.

---

## 9. Implementation map

| Phase | Deliverable | This doc's sections | Owner |
|---|---|---|---|
| HS-1 | Schema v11 person layer (shipped) | §2, §3, §7.1 (tables) | Fable 5 |
| HS-2 | `BackstoryGenerator`, `CreateAvatar` seeding, parent rows, trait picker | §2.5, §3, §3.1 | Fable engine / Sonnet UI |
| HS-3 | Phone tiers/minutes/Wi-Fi, Marketplace, transport, auto-purchase | §3.2, §4, §5 | Sonnet UI / Fable seams |
| HS-4 | Time-Manager person-stat action channel, GPA drift, transport hours | §2.2, §2.3, §5.3 | Fable 5 |
| HS-5 | `ConsequenceKind.PersonStat`, NPC-autonomy tick, dating, pregnancy + child-rearing, content | §2.1, §7, §8 | Sonnet content / Fable engine |
| HS-6 | `PersonEffects` sim layer + re-band | §6 | Fable 5 (sanctioned sim touch) |

### 9.1 Per-phase acceptance hooks

- **HS-2:** `MonteCarloHarness` (where `CareerManager` compiles) — creation
  determinism + wealth-tier frequency bands + starting-funds ranges; the
  bit-identity guard **stays byte-exact** (no person stat reaches the sim yet).
- **HS-3:** minutes-economy arithmetic; the "pending event resolves at 0
  minutes" invariant proven; item-buff aggregation + cap; live-boot idempotence.
- **HS-4:** `NeedsDecayHarness` — new action definitions, GPA closed-form
  fixture, **neutral-avatar bit-identity** for unset features.
- **HS-5:** `GrittyEventsHarness` + `check_event_graph_integrity` (3-choice
  minimum on the `hs_` batch); NPC-autonomy conservation (budget honored, avatar
  untouched, RNG-split proven); nurture-blend fixtures incl. the §7.3 identity.
- **HS-6:** full `run_monte_carlo_batch` re-band — neutral world byte-exact,
  §6 arithmetic pinned, directional league check, documented golden resets.

---

## 10. Disclosed simplifications

- **Person stats don't decay** (§2.2) — only GPA drifts and happiness weakly
  reverts. A future "reputation fades" mechanic is out of scope.
- **Only three stats feed the sim** (§6), capped ±2–3, one reserved lever
  deferred. The other ten are narrative/economic only in this arc.
- **Items buff only status stats** (§5.2), summed with a single aggregate cap —
  no per-slot itemization, no set bonuses, no wear.
- **Strictness is wealth-independent** (§3) — a deliberate orthogonal axis, not a
  correlated roll.
- **NPC autonomy is budgeted, not exhaustive** (§8) — 256 interactions/week is a
  sample of the social graph, not a full pass; distant NPCs evolve slowly by
  design.
- **Nurture is bounded** (§7.2) — nature dominates; a maxed/neglected upbringing
  shifts odds within ±8 potential / ±20 interest, never a guaranteed outcome.
- **Wealth tier is per person, not inherited economically** — an heir's family is
  the avatar's own household (HS-2 gives `Succeed` the family context rather than
  re-rolling); wealth as a rolled backstory only happens for a founding avatar.

---

## 11. Constants summary (first-pass, all tunable data)

| Constant | Value | Section |
|---|---|---|
| Neutral person stat / GPA | 50 / 2.5 | §2 |
| Alignment thresholds | Good ≥67, Troubled ≤33 | §2.3 |
| Wealth-tier frequencies | 12 / 28 / 35 / 18 / 7 % | §3 |
| Starting funds by tier | 50 / 200 / 500 / 1,500 / 5,000 | §3 |
| Allowance/wk by tier | 0 / 10 / 25 / 60 / 150 | §3 |
| Self-buy transport reward | +work_ethic, +discipline, +maturity (one-time) | §3.1 |
| Minute bundle | $10 → 100 min | §4.2 |
| Basic-plan refill | 50 min/wk | §4.2 |
| Minute costs | text 2 / call 6 / browse 3 / LDR 20/wk | §4.2 |
| Pending-event minute cost | **0, always** (invariant) | §4.3 |
| `ItemBuffCap` (per status stat) | +15 | §5.2 |
| Transport hours saved | car 1.0 / bike 0.5 per day | §5.3 |
| `PersonEffects` cap | ±2 (tunable to ±3) | §6.2 |
| `PotentialNurtureCap` | ±8 | §7.2 |
| `InterestNurtureCap` | ±20 | §7.2 |
| Nurture weights (coach/fund/neglect) | 0.5 / 0.3 / 0.5 | §7.2 |
| `ChildSupportPerChild` | $20/wk, mandatory | §7.1 |
| `CareNeutralHoursPerWeek` / `CareHoursPerPoint` | 3h baseline, ±1pt per 2h beyond it | §7.1 |
| `FundingDollarsPerPoint` | $50/wk per +1pt (schema's $300 cap ⇒ +5 cap exactly) | §7.1 |
| Neglect accrual / recovery | +3/wk (zero hours AND zero funding) / −1/wk otherwise | §7.1 |
| `MaxPairInteractionsPerWeek` (+ tier shares 128/64/remainder) | 256 (same-team 128, edge 64, random remainder) | §8.1 |
| `MintProbability` | 0.15 per edgeless draw | §8.1 |
| `PartnerPromoteProbability` / `PartnerPromoteMinAffinity` | 0.04 per interaction / affinity ≥15 | §8.1 |
| `BreakupProbability` / `BreakupBitterShare` | 0.02 per interaction / 40% bitter | §8.1 |
| Breakup affinities (friend / rival) | +15 / −25 | §8.1 |
| `MaxNudge`, `PositiveFloor`/`PositiveRange` | ±1–3 magnitude, 0.25–0.75 positive-outcome probability | §8.1 |

All values here are first-pass and change as **data edits**, not logic edits —
the `HeirGeneticsProfile` / `NeedDecayProfile` / `TierEffects` table discipline.
Any edit that reaches the sim (§6, §7 potential blend feeding a playable heir)
re-runs `run_monte_carlo_batch` under the standing rule.

### 11.1 2026-07-10 tuning pass

`ChildRearingService`'s `careDelta`/`fundingDelta` formulas were rescaled
(values only, no schema/logic change): the original 2h/week care baseline and
$25-per-point funding divisor saturated the ±5 cap almost immediately — any
single ~1h/day family habit maxed care, and funding maxed out at $150/wk even
though `Child_Rearing_Commitment.weekly_funding` allows up to $300. The new
baseline (3h/week neutral, ±1 care point per 2h beyond it) and funding divisor
($50/point) give a real gradient across the slider's/schema's actual reachable
ranges, and funding now saturates exactly at the schema's $300 ceiling instead
of leaving its top half inert. Also switched the rounding from the CLR's
to-even default to the codebase-wide away-from-zero discipline
(`NurtureBlend`/`DevelopmentManager`'s own `RoundAwayFromZero`), which this
formula had silently diverged from. `NpcAutonomyService`'s constants were
reviewed and left unchanged — each already carries an inline first-principles
justification (reachable-band comments) from HS-5 authorship; no gradient or
reachability defect was found. No harness pins exact numeric output from
either service (both are structural/conservation-checked only), so this is a
pure data edit — no golden reset required.
