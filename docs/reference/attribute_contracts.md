# Attribute contracts

> **Extracted 2026-11-19 (W1)** verbatim from the archived gameplay_core.md §1. Binding per GEMINI §11.

## 1. Attributes: the definitions

### 1.1 How a rating becomes an outcome (shipped)

Every rating runs 0–100, with 50 as league average. The resolver turns each into a deviation `d = (rating − 50) / 50` in [−1, +1]. Each plate-appearance outcome's weight is `base × exp(k × Σ w·d)`, and the weights are renormalized ([AtBatResolver.cs](../../Assets/Simulation/Baseball/AtBatResolver.cs) `ComputeProbabilities`). A team of all-50s reproduces the calibrated Pro line exactly: **.247 / .315 / .412, K 22.5%, BB 9.0%, HR 3.2% of PA**.

**What +10 and +20 points are worth** against league-average opposition, from the shipped formula. These are rate effects per PA, before any modifiers or player skill:

| Attribute (who) | Drives | +10 | +20 |
| :-- | :-- | :-- | :-- |
| **bat_power** (hitter) | HR and doubles up; outs down slightly | OPS **+.034** (HR 3.2% → 3.7%) | OPS **+.071** (HR → 4.2%) |
| **bat_contact** (hitter) | Singles and doubles up; strikeouts and outs down | OPS **+.050** (AVG .247 → .269; K 22.5% → 21.2%) | OPS **+.103** (AVG → .292) |
| **bat_discipline** (hitter) | Walks up; strikeouts down. On the arcade field, also how early the pitch reads (arcade §4.1) | OPS +.011 (BB 9.0% → 10.0%) | OPS +.022 (BB → 11.1%) |
| **pit_stuff** (pitcher) | Strikeouts up; HR, singles and doubles down | opponent OPS **−.045** (K → 24.2%) | opponent OPS **−.089** (K → 26.0%) |
| **pit_control** (pitcher) | Walks down. On the arcade field, also the release-meter width and scatter (arcade §4.1) | opponent OPS −.006 (BB → 8.1%) | opponent OPS −.012 (BB → 7.4%) |
| **pit_stamina** (pitcher) | Pitch capacity before fatigue; fatigue decays effective stuff and control toward 50 (floor ×0.5) | more innings at full strength | — |
| **fielding** (everyone) | The **team-defense** average of the lineup: outs up; singles, doubles and triples down | team OPS allowed **−.027** (one +10 *team* average) | −.052 |

**Read-out:** on the shipped weights, **contact is the strongest hitting attribute**, and **stuff is the strongest pitching one**. **Discipline and control are nearly inert in background sims.** Their main value is on the arcade field: discipline sets how early the pitch reads, and control sets the release meter and scatter (arcade §4.1). *Proposal for Math:* either accept that (they're "attended-game" attributes), or raise their macro weights in the Phase 13 re-band.

### 1.2 What stacks on a rating (shipped + planned)

Each step clamps to [0, 100] (`TierEffects.Shift`). The order is pinned in code and in harnesses.

| Layer | Size | Applies to | Status |
| :-- | :-- | :-- | :-- |
| Tier environment | HS −20, College −16, Pro 0 (on pitStuff, pitControl, teamDefense) | everyone in that tier | shipped (Phase 13 sets the final values) |
| Person stats (`PersonEffects`) | **±2** each: confidence → power/stuff; happiness → contact/control; teamwork → team defense | everyone | shipped |
| Equipment | up to **+6** on the gear's slot | the owner | shipped |
| Rivalry | small ± vs a rival | rivals | shipped |
| Steroids (`ped_active`) | power ×1.5 (hitter) / stamina ×1.5 (pitcher) | the user | shipped hook; the trigger lands in S-1 |
| Fatigue | effective stuff/control decay toward 50, floor ×0.5 | pitchers | shipped |
| `Active_Effects` (affair swagger, substance edges, hangover, withdrawal, search stress) | Pro Math values | the avatar | Phase L |
| Clutch (affairs) | high-leverage PAs only | the avatar | Phase L |
| Missing piece | the base rating is frozen exactly | the parent | Phase L |

### 1.3 Every other attribute

| Attribute | What it affects today | Gameplay gap / proposal |
| :-- | :-- | :-- |
| **confidence, happiness, teamwork** | The only person stats that reach the field (±2 points each, §1.2); also events | — |
| **intelligence, maturity, charisma, reputation, social_status, attractiveness, morality, discipline (person), work_ethic** | Event prerequisites and outcomes, dating and NPC autonomy, person drift, item rewards (`PersonDrift`, `EventConsequenceApplier`, `NpcAutonomyService`, `ItemService`) | **None reach the field.** **Accepted R-1:** work_ethic scales actual practice development with a bounded Math-calibrated modifier. Additional person discipline/composure field scalers are deferred until after 1.0. |
| **gpa** | HS eligibility, school events, recruiting context | — |
| **Needs** (hunger, sleep, hygiene, social, fitness) and **stress** | Life sim only: utility AI, forced actions, events | **They never touch the field.** A starving, sleepless player bats like a rested one. **Accepted R-1:** one small capped game-start projection from needs/stress, visible to the player and consistent across attended (arcade) and background avatar games. It enters through `EffectiveRatings` (arcade §4.2). Healthy/no-effects is exact identity; Math owns constants and prevents double counting. |
| **health_ceiling** | Injury rust, decline speed, forced retirement below the floor | — |
| **recklessness** | Event and hustle prerequisites; risk rolls | — |
| **baseball_interest** | Heirs only: will this child play? | — |
| **detection_risk** | Steroid tests, suspensions, scandal | — |
| **funds** | Survival economy, hustles, purchases, the search (Phase L) | — |

### 1.4 Ratings × player skill (the core principle, revised 2026-10-13)

> **Ratings shape the physics. Player skill decides what happens.**

- **Attended games (arcade).** Each rating becomes a physical capability: power → bat speed, contact → sweet-spot size and timing window, discipline → how early the pitch reads, stuff/arsenal → pitch speed and break, control → release-meter width and scatter, stamina → fatigue, fielding → reaction and reach, plus the new **speed** and **arm**. The player's timing and aim decide the outcome. Full mapping: [arcade_baseball_overhaul.md](../archive/arcade_baseball_overhaul.md) §4.
- **Background games** use the shipped resolver from ratings only (§1.1), so the league stays calibrated. AI-vs-AI arcade games must land in the same per-tier bands (arcade §9).
- **How much should skill be worth?** *Owner decided 2026-10-10 (O1):* about the gap between a **40- and a 70-rated** player (roughly ±.100 OPS around ratings), capped, with an average (or blind) player at the ratings line, for batting **and** pitching. Under arcade physics this is a **difficulty** target: presets tune only the human's assists (timing window, cursor size, read cue, fielding assist), never the AI or the ratings. At the default difficulty, an average-skill player matches their ratings and an excellent or poor one lands about ±30 rating points away, measured by the scripted-skill bots in `ArcadeHarness`. **Math owns the tuning.**
- The shipped "Read the Pitch" model ([at_bat_read_input_model.md](../reference/at_bat_read_input_model.md)) stays in the code as the **Classic sim** fallback until the arcade engine is proven (arcade §11).

---

