# Gameplay Core: Attributes, the At-Bat (Batting & Pitching), Ballparks & Venues

**Author:** Claude Opus 5.5 · **Status:** design draft 2026-10-10, **revised 2026-10-13 for the arcade decision**, **HIGH PRIORITY** (owner directive). **The attended game itself (physics, controls, AI, watch/skip, its calibration and look) is specified in [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md).** This doc keeps the Phase G umbrella: attribute definitions, venues and the handoff protocol. Phase G in [BUILD_PLAN.md](../BUILD_PLAN.md). Numbers in §1 are computed from the shipped code; everything marked *proposal* is a first pass for Gemini Pro Math and the Claude Fable 5.1 review to challenge.
**Authority:** [GAME_VISION.md](../GAME_VISION.md) v2 §5 and §12. The shipped contracts this builds on: [baseball_pa_outcome_model.md](../reference/baseball_pa_outcome_model.md), [at_bat_read_input_model.md](../reference/at_bat_read_input_model.md), [at_bat_presentation.md](../reference/at_bat_presentation.md), [high_school_person_layer.md](../reference/high_school_person_layer.md) §6, [equipment_quality.md](../reference/equipment_quality.md), [development_decline_curves.md](../reference/development_decline_curves.md).

---

## 0. Owner directives (2026-10-10)

| # | Directive |
| :-: | :-- |
| G1 | **Gameplay mechanics are a high priority.** First the pitcher/batter interface, then stadium mapping and the look of each park. |
| G2 | **Every team gets its own interactive stadium screen** (College and Pro) **or school screen** (HS), with its own art. |
| G3 | **Every attribute is defined:** what it affects, by how much, and how it combines with the player's own gameplay skill. |
| G4 | **Every task names its recommended model**, and every model stops when its task is done and tells the human which model is suggested for the next one (§5). |
| G5 | **True arcade physics (2026-10-13).** Bat contact, ball trajectory and fielding are decided by real-time physics and the player's skill. Ratings shape the physics. You control your player, with a take-over-the-team toggle. Fielding is assisted. 16-bit art. Watch the entire game, with skips. Spec: [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md). |

---

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
| **intelligence, maturity, charisma, reputation, social_status, attractiveness, morality, discipline (person), work_ethic** | Event prerequisites and outcomes, dating and NPC autonomy, person drift, item rewards (`PersonDrift`, `EventConsequenceApplier`, `NpcAutonomyService`, `ItemService`) | **None reach the field.** *Proposal:* work_ethic scales practice → development; discipline (person) adds to how early the pitch reads; maturity shrinks slump and stress effects. Owner/Math call. |
| **gpa** | HS eligibility, school events, recruiting context | — |
| **Needs** (hunger, sleep, hygiene, social, fitness) and **stress** | Life sim only: utility AI, forced actions, events | **They never touch the field.** A starving, sleepless player bats like a rested one. *Proposal:* a small, capped game-day penalty from low sleep/fitness and high stress (an `Active_Effects` row baked at game start, bit-identical when needs are healthy). Owner/Math call. |
| **health_ceiling** | Injury rust, decline speed, forced retirement below the floor | — |
| **recklessness** | Event and hustle prerequisites; risk rolls | — |
| **baseball_interest** | Heirs only: will this child play? | — |
| **detection_risk** | Steroid tests, suspensions, scandal | — |
| **funds** | Survival economy, hustles, purchases, the search (Phase L) | — |

### 1.4 Ratings × player skill (the core principle, revised 2026-10-13)

> **Ratings shape the physics. Player skill decides what happens.**

- **Attended games (arcade).** Each rating becomes a physical capability: power → bat speed, contact → sweet-spot size and timing window, discipline → how early the pitch reads, stuff/arsenal → pitch speed and break, control → release-meter width and scatter, stamina → fatigue, fielding → reaction and reach, plus the new **speed** and **arm**. The player's timing and aim decide the outcome. Full mapping: [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §4.
- **Background games** use the shipped resolver from ratings only (§1.1), so the league stays calibrated. AI-vs-AI arcade games must land in the same per-tier bands (arcade §9).
- **How much should skill be worth?** It's now a **difficulty** question: presets tune only the human's assists (timing window, cursor size, read cue, fielding assist), never the AI or the ratings. *Proposal (owner to confirm):* at the default difficulty, an average-skill player matches their ratings, and an excellent or poor one plays about ±30 rating points away. That's the same "about a 40-to-70 gap" as before, now measured by the scripted-skill bots in `ArcadeHarness`. **Math owns the tuning.**
- The shipped "Read the Pitch" model ([at_bat_read_input_model.md](../reference/at_bat_read_input_model.md)) stays in the code as the **Classic sim** fallback until the arcade engine is proven (arcade §11).

---

## 2. The at-bat interfaces (G1): replaced by the arcade engine

**Superseded 2026-10-13 (owner decision G5).** The former §2.1 read-model view upgrade, §2.2 pitching mode (an `IPitcherPolicy` human branch) and §2.3 fielding moments are **not built**. Batting, pitching, fielding and baserunning are all live controls on the arcade field:

| Interface | Where it's specified |
| :-- | :-- |
| Batting: timing, contact cursor, power swing | [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §3.2, §5.1 |
| Pitching: pitch, aim, release meter, aftertouch | arcade §3.1, §5.2 |
| Fielding (assisted) and throws | arcade §3.4, §5.3 |
| Baserunning; take over the team | arcade §3.5, §5.4, §5.5 |
| Watch and skip; play-by-play; box score | arcade §7 |
| Broadcast beats and ★ big plays | arcade §7.6 |

What carried over from the old §2: the accessibility goals (keyboard and controller, remapping, colorblind-safe cues, reduce-motion) and the pacing rule that a reveal waits for the broadcaster's call. Both are in arcade §5 and §10.

---

## 3. Ballparks & venues (G1, G2)

### 3.1 The venue catalog

`Assets/Data/Venues/venues.json` gets one entry per team, **all 68**. It generalizes the shipped `schools.json` pattern (`images.logo/school/field`, `SchoolArtLibrary`).

- **HS → school venue:** campus exterior, field, dugout/locker, gym, parking lot.
- **College / Pro → stadium venue:** exterior, concourse, field (day and night), home clubhouse, manager's office, training room, players' lot.
- **Shared data:** park name, city, capacity, surface, **dimensions** (LF/LCF/CF/RCF/RF distances, wall heights), signature features (a short porch, an ivy wall, a harbor beyond right), team colors and the announcer booth (Phase 14).

### 3.2 The interactive venue screen (one per team)

When the avatar is "at the park" (game day, practice, or a visit), the hub is **that team's own venue screen**: layered art with hotspots, tinted by team colors and time of day.

| Hotspot | Opens |
| :-- | :-- |
| Field | Today's game / practice / batting cage (a practice at-bat using the §2 interfaces) |
| Clubhouse / locker | Teammates (relationships), your locker (gear, equipment), the team board (roster status, Sunday lock) |
| Manager's / coach's office | Coach thread, playing time, the depth chart |
| Training room | Injuries, rehab, health, and (Phase L) where substances are offered |
| Stands / concourse | Fans, press (clippings), family in the seats |
| Lot / exit | Leave for the street side: the phone, hustles, home |

Away games show the **opponent's** venue (read-only hotspots: field, visitors' clubhouse).

### 3.3 Stadium mapping in the at-bat

- The arcade field is built from the **home park's** catalog entry: its dimensions, walls, surface and signature features (arcade §10.4).
- **Park factors now emerge from geometry:** a short porch really does give up more home runs. *Proposal:* keep it, with Math checking that each tier's league-wide average stays neutral (arcade §13 Q5). Background games stay park-neutral unless the owner also wants macro park factors, which would be a separate Math slice gated by `run_monte_carlo_batch`. **Owner decision.**

### 3.4 Art scope

68 venues × about 5–7 layered scenes, plus day/night variants for the field, in the approved Phase 15 direction. Pre-generated, curated and disclosed (the AI-art rule). This feeds slice 15d. The two shipped HS field images (CRW, LKS) are the seed. The **16-bit field tilesets** (one per venue, arcade §10.4) are a separate art set from these venue screens, produced in slice G-8.

---

## 4. Slices (Phase G: high priority, runs alongside Phase 13)

**Revised 2026-10-13.** The Phase G slice table (G-0 through G-9), with recommended models, reviewers, gates and dependencies, now lives in [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §12. In short:

- **G-0** feel prototype (starts now);
- **G-1** attribute audit → ratings-to-physics mapping;
- **G-2** physics core;
- **G-3** AI + calibration;
- **G-4** integration;
- **G-5** arcade presentation + watch & skip;
- **G-6** team takeover;
- **G-7** venue catalog + screens (§3);
- **G-8** art;
- **G-9** tuning + difficulty.

---

## 5. The model-handoff protocol (G4)

Every task brief names its **recommended model**, from [TEAM.md](../TEAM.md) §2. Every model working a task:

1. Works **only** that task, in its own worktree and branch.
2. **Stops when the task's definition of done is met.** It doesn't drift into the next slice, even an obvious one.
3. Ends with a **handoff note to the human** containing:
   - what was done, the commit, and the gates it ran with their results;
   - anything left open;
   - **"Recommended next: `<slice>` by `<model>`"** (from TEAM.md §2), plus the reviewer if the work needs review.
4. Logs one compact line in [progress.md](../progress.md).

The human starts the next model. No model starts another model's slice.

---

## 6. Open owner decisions

1. ~~Arcade physics vs the read model~~ **Decided 2026-10-13: arcade physics** (G5).
2. **Default difficulty target** (§1.4)? Recommended: average skill = your ratings; excellent or poor ≈ ±30 rating points.
3. **Needs and stress on the field** (§1.3)? Recommended: a small, capped game-day penalty. On the arcade field this would land as slower reactions and a narrower timing window.
4. **Person stats on the field** beyond the shipped three (§1.3)? Recommended: work_ethic → development, person discipline → how early the pitch reads.
5. **Parks play differently by geometry** (§3.3)? Recommended: yes, tier-neutral on average.
6. ~~Fielding moments~~ **Superseded:** fielding is fully playable (assisted) on the arcade field.
7. The arcade-specific questions: steals, pickoffs and bunts in v1; takeover stats; Classic's fate; the art source ([arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §13).
