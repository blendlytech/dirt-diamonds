# Substances & Addiction: Steroids, Drugs, Alcohol

**Author:** Claude Opus 5.5 (architecture + narrative) · **Status:** design draft 2026-10-10. No code yet. Owner decisions resolved (§9).
**Authority:** owner decision D18 ([temptation_and_lineage.md](temptation_and_lineage.md) §0): each substance gives a **dramatic** boost in one area that really helps, with **real** consequences that differ for each.
**Builds on:** the shipped PED hooks ([baseball_pa_outcome_model.md](baseball_pa_outcome_model.md) §6, [baseball_markov_micro_sim.md](baseball_markov_micro_sim.md)), the PED event arc (`roster_availability_events.json`, `career_arc_events.json`), the narcotics hustle's `uses_product` hook ([hustles_narcotics_fencing.md](hustles_narcotics_fencing.md) §3.2), and the shared `Active_Effects` layer ([temptation_and_lineage.md](temptation_and_lineage.md) §6.4).
**Closes:** the known gap in [criminal_underworld_dirt4_the_bust_and_the_door_out.md](criminal_underworld_dirt4_the_bust_and_the_door_out.md) §2.1, where `ped_active` has **no gameplay writer**, so the shipped 1.5× multiplier never fires in the live game.

---

## 1. What already exists

| Piece | Where | State |
| :-- | :-- | :-- |
| PED multiplier: hitter power ×1.5, pitcher stamina ×1.5 (clamped to 100) | `AtBatResolver.PedPowerMultiplier`; micro-sim §PED | Built and calibrated, **never triggered** |
| Per-game PED costs: −1 `health_ceiling`, +2 `detection_risk` | `LeagueSimulator.PedHealthCostPerGame` / `PedDetectionRiskPerGame` | Built, never triggered |
| `ped_active` flag read at bulk load | `LeagueSimulator.cs:225` | Only the Monte Carlo test fixture writes it |
| Detection ladder: test flag (≥ 60), suspensions (≥ 80/90), scandal (> 70) | `roster_availability_events.json`, `career_arc_events.json` | Live, driven by `detection_risk` |
| Health erosion → faster decline → forced retirement below the floor | `DevelopmentManager`, `HeirGenetics.HealthRetirementFloor` | Live |
| `uses_product`: a user takes a health hit on a bad batch | `NarcoticsHustle` | Hook live, but no event sets the flag |
| Absences: Injury, Suspension, Arrest | `AbsenceReason` | Live |

The steroid half of D18 is mostly **wiring the shipped system to a writer**. Everything else is new, built on one shared addiction engine (§3).

---

## 2. The catalog: one big edge, one distinct price

Every on-field number below is a **Pro Math data value** (first-pass magnitudes are shown as intent, not as final numbers). Each one rides the `Active_Effects` bake, so no rows means the shipped code path, bit-identical.

### 2.1 Steroids: **power** (hitters) / **stamina** (pitchers)

- **The edge:** the shipped ×1.5 multiplier, now live. A 60-power bat hits like a 90.
- **The price (distinct: the body and the record book):**
  - −1 `health_ceiling` per game (shipped). This means earlier decline and an earlier forced retirement.
  - +2 `detection_risk` per game (shipped), feeding the test → suspension → scandal ladder.
  - **Roid rage:** while active, recklessness rises and teamwork falls. Clubhouse-fight and road-rage events can lead to arrest.
  - **Fertility:** `PregnancyChance` is scaled down while active. That's medically real, and it plays against the affair risk (temptation §6.2).
  - **The asterisk:** any record, milestone or championship reached with steroids active, or with `steroid_scandal_public`, carries an **asterisk** in press clippings and the record book, permanently. Breaking the GOAT's record (temptation §4.3) while juicing makes the press story about the asterisk, not the record.
  - **Withdrawal crash:** stopping after dependence leaves a power or stamina **deficit** for weeks (a negative `Active_Effects` row).

### 2.2 Amphetamines ("greenies"): **focus and energy**

- **The edge:**
  - Hitters: contact and discipline up.
  - Pitchers: control up, and the fatigue model's short-rest penalty is reduced, so they can pitch on short rest.
  - Everyone: "never tired" on back-to-back game days.
- **The price (distinct: the heart and sleep):**
  - The `sleep` need drains several times faster.
  - A **crash day** after each window: a heavy contact/control penalty.
  - **Cardiac events:** a random chance per use that rises with dependence. Each one means a long Injury absence and a large `health_ceiling` hit; rarely, the career ends.
  - Banned: feeds `detection_risk`, like steroids.

### 2.3 Cocaine: **invincibility**

- **The edge:** confidence **and** happiness overrides to 100 (both `PersonEffects` levers maxed), stress wiped, the social need filled. **Slumps, harassment and pressure can't touch them while high.** That's especially potent on the Trailblazer route (temptation §8), where pressure is the opponent.
- **The price (distinct: money, paranoia and the law):**
  - **The most expensive habit:** the cost per use climbs with dependence. A funds drain becomes a **debt to a dealer**, which plugs into the DIRT syndicate arc.
  - **Paranoia:** teamwork falls, and relationship affinity decays with partner, family and teammates.
  - **Possession arrests:** an `Arrest` absence and the legal track.
  - **Overdose:** chance rises with dependence, and more so when mixed with alcohol.
  - Sets `uses_product`, so a narcotics-hustle bad batch now costs health (the shipped hook comes alive).

### 2.4 Painkillers (opioids): **never miss a game**

- **The edge:** Injury absences shrink to a fraction, and the injured-play penalty disappears. The player stays on the field through everything.
- **The price (distinct: hidden damage and the hardest addiction):**
  - **The fastest dependence** in the catalog.
  - **Masked injuries compound:** each injury played through adds hidden damage, and when it surfaces it's a **severe** injury with a large `health_ceiling` loss.
  - **Overdose:** the highest risk, multiplied by alcohol; the result is hospitalization and a long absence. Whether it can be fatal is an owner decision (§9).
  - **Brutal withdrawal:** sleep, stress and happiness crash for weeks.
  - Without a prescription, possession arrests apply, as with cocaine.

### 2.5 Alcohol: **stress and social**

- **The edge:** heavy stress relief, the social need filled, charisma at events and parties, and "liquid courage" (a small confidence bump for the next game). It's the only **legal** one, for ages 21 and over.
- **The price (distinct: slow, and it hurts the people around you):**
  - **Hangover:** drinking the night before a game day applies a contact/control penalty that day.
  - **DUI:** a random chance on nights out that rises with dependence. It leads to arrest, suspension, or a crash: injury to self or others, legal and financial fallout, reputation.
  - **Home:** affinity decays with partner and children. It feeds the divorce arc, and it **lowers `Child_Development` nurture**, so an heir raised by a drinker is likelier to walk away from baseball.
  - **Loose lips:** it raises the fling chance **and** affair `DiscoveryChance` (temptation §6.2).
  - **Late-career liver:** after years of heavy dependence, `health_ceiling` erodes.
  - **Underage (18–20):** drinking is illegal in College, and getting caught can mean team discipline.

### 2.6 Overdose and death (D20)

An overdose **can be fatal** (cocaine, painkillers, and mixing either with alcohol). The fatal share of overdoses is a Pro Math value that rises with dependence and mixing; the rest are hospitalizations.

- **A fatal overdose ends the avatar's life and career that day.** The order matters:
  1. a death story beat (captions; press clippings);
  2. the lineage reveal (temptation §4.4), if it hasn't happened yet, delivered posthumously, through the father's last text going unanswered;
  3. succession, evaluated **immediately regardless of age**: a willing heir ≥ 19 takes over, otherwise the lineage is over (`LineageFailure`).
- **Engine:** a new `CareerManager.AvatarDied(cause)` forces the `EvaluateSuccession` path (today it triggers only at the retirement age or the health floor). That's an Opus sim change with succession fixtures and Gemini Pro review.
- The avatar's hidden children stay hidden. Their discovery can still happen through a later heir (temptation §7.3).

---

## 3. The shared addiction engine

```sql
CREATE TABLE IF NOT EXISTS Substance_Use (
    player_id       TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    substance       TEXT    NOT NULL,                       -- catalog key
    dependence      INTEGER NOT NULL DEFAULT 0 CHECK (dependence BETWEEN 0 AND 100),
    uses_total      INTEGER NOT NULL DEFAULT 0,
    last_use_day    INTEGER,
    clean_since_day INTEGER,
    rehab_count     INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (player_id, substance)
) STRICT;
```

The catalog (`Assets/Data/Substances/substances.json`) holds the numbers. The **effect kinds** are a closed engine enum: rating delta, rating multiplier, person-stat override, need drain, absence scale, chance modifier, and so on. Content tunes values; it never invents mechanics.

| Phase | Rule |
| :-- | :-- |
| **Use** | Writes the high (`Active_Effects`, the substance's duration). Raises dependence by the substance's rate times a random roll. Rolls the per-use risks (cardiac, DUI, overdose, arrest), each scaled by dependence. |
| **Tolerance** | The high keeps its **strength** (D18: dramatic) but **shortens** as dependence rises. Users need it more often, so every risk is rolled more often: the same escalate-and-roll shape as affairs. |
| **Craving** | At dependence ≥ the threshold, craving events arrive, and use becomes the easy choice. Resisting costs stress and happiness. |
| **Withdrawal** | At dependence ≥ the threshold with no use for N days, the substance's own withdrawal penalty applies (§2), then dependence decays slowly. |
| **Recovery** | Rehab through a contact (coach, agent or partner intervention): funds, a new `AbsenceReason.Rehab = 5`, and a reputation hit if it goes public. Dependence drops sharply. After that, each craving rolls **relapse** at a chance that falls with clean days. Clean-streak milestones are story beats. |
| **Mixing** | Cross-substance modifiers live in the catalog (alcohol × opioids overdose, alcohol × cocaine overdose). |

**Detection tracks:**
- Banned (steroids, amphetamines) → the shipped `detection_risk` ladder.
- Illegal (cocaine, opioids without a prescription) → arrest events.
- Alcohol → DUI and underage only.

**Getting it:** offers come through people (a teammate, a trainer, a booster, a party, a dealer from the DIRT arc). Once the player knows a supplier, a phone contact lets them seek it out deliberately, so use is the **player's choice**, not something that only happens to them.

---

## 4. Wiring steroids (closing the DIRT-4 gap)

1. `ped_active` becomes **engine-written**: set while the avatar has an active `steroids` effect and cleared when it expires. It's documented as an engine flag, so `check_event_graph_integrity` #2 accepts it.
2. That revives the shipped ×1.5 multiplier and per-game costs with no change to their calibrated math.
3. **Then** the four shipped PED events can safely re-gate on `ped_active` as well as `detection_risk`, the follow-up that DIRT-4 §2.1 deferred. Re-gating before step 1 would silently delete the steroid arc.

---

## 5. Cross-system effects

| With | Effect |
| :-- | :-- |
| Affairs (temptation §6) | Alcohol raises fling chance and discovery; steroids cut pregnancy chance |
| Missing piece (temptation §5.6) | Temporary boosts still apply to a locked stat (the lock is base-rating only); withdrawal deficits stack on it |
| Heirs | A parent's active addiction lowers `Child_Development` nurture (alcohol most) |
| The GOAT father | **In 50% of careers** (D21, rolled at creation, hidden) the record the avatar is chasing was itself juiced (temptation §4.3) |
| Trailblazer | Cocaine's pressure immunity is the most tempting on her route; the press treats a woman's scandal harder (content tone, not mechanics) |
| Narcotics hustle | Cocaine/opioid dependence sets `uses_product` |

---

## 6. Boundaries and calibration

- **Avatar-only in v1.** League NPCs keep the shipped PED event arc. Substance use by NPCs would move league-wide bands, so it's a later, separately calibrated pass.
- **The effects bake through `Active_Effects`** (temptation §6.4). Opioid absence-scaling acts on the absence writer, not the per-PA path. Only clutch (affairs) reads live leverage.
- **Gates:**
  - every magnitude is a Pro Math value, gated by `run_monte_carlo_batch`;
  - "no rows = bit-identical" is a harness assertion;
  - schema changes go through `validate_sqlite_schema`;
  - substance events go through `check_event_graph_integrity`.

## 7. Validation

- **Substance engine** (pure, harness-tested, a `SubstanceHarness` alongside `HustleHarness`):
  - dependence growth, tolerance shortening, withdrawal onset and decay, relapse decay;
  - each risk rises with dependence;
  - mixing multipliers.
- **MonteCarloHarness:**
  - `ped_active` follows the steroid effect exactly, and the shipped multiplier and costs fire;
  - each substance's edge and price lands on the right rating, need or absence, and nowhere else;
  - no rows means bit-identical;
  - no band moves with the avatar clean.
- **GrittyEventsHarness:**
  - catalog parsing;
  - unknown effect kinds rejected;
  - the age-gate guard (§9.1).

## 8. Slices

| Slice | Content | Author | Reviewer | Depends on |
| :-- | :-- | :-- | :-- | :-- |
| **S-1** | `Substance_Use`, the engine, `ped_active` writer, `AbsenceReason.Rehab`, catalog loader | Claude Opus | Gemini Pro | `Active_Effects` (temptation L-5) |
| **S-2** | Magnitudes for every edge and risk | Gemini Pro Math | Claude Opus | S-1 |
| **S-3** | Content: offers, supplier contacts, cravings, rehab, a consequence arc per substance, PED re-gate | Claude Opus (scripts) → Flash | Gemini Pro | S-1 |
| **S-4** | Asterisk in press and record book | Gemini Pro (press runtime) + Flash | Claude Opus | temptation L-7 |

## 9. Owner decisions (resolved 2026-10-10)

1. **Age gate (D19):** substance **boosts** are **18+ only** (tier ≥ College). HS gets the story (a teammate's steroid bust, a party, a friend's overdose) and the chance to say no, but no stat reward for a minor using. Underage drinking at 18–20 stays as a College consequence risk. A loader guard rejects substance-use consequences without the age/tier prerequisites.
2. **Fatal overdose (D20):** yes (§2.6).
3. **The GOAT's asterisk (D21):** 50/50, rolled once at career start and hidden (temptation §4.3).
