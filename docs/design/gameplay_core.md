# Gameplay Core: Attributes, the At-Bat (Batting & Pitching), Ballparks & Venues

**Author:** Claude Opus 5.5 · **Status:** design draft 2026-10-10, **HIGH PRIORITY** (owner directive). Phase G in [BUILD_PLAN.md](../BUILD_PLAN.md). Numbers in §1 are computed from the shipped code; everything marked *proposal* is a first pass for Gemini Pro Math and the Claude Fable 5.1 review to challenge.
**Authority:** [GAME_VISION.md](../GAME_VISION.md) v2 §5 and §12. The shipped contracts this builds on: [baseball_pa_outcome_model.md](../reference/baseball_pa_outcome_model.md), [at_bat_read_input_model.md](../reference/at_bat_read_input_model.md), [at_bat_presentation.md](../reference/at_bat_presentation.md), [high_school_person_layer.md](../reference/high_school_person_layer.md) §6, [equipment_quality.md](../reference/equipment_quality.md), [development_decline_curves.md](../reference/development_decline_curves.md).

---

## 0. Owner directives (2026-10-10)

| # | Directive |
| :-: | :-- |
| G1 | **Gameplay mechanics are a high priority.** First the pitcher/batter interface, then stadium mapping and the look of each park. |
| G2 | **Every team gets its own interactive stadium screen** (College and Pro) **or school screen** (HS), with its own art. |
| G3 | **Every attribute is defined:** what it affects, by how much, and how it combines with the player's own gameplay skill. |
| G4 | **Every task names its recommended model**, and every model stops when its task is done and tells the human which model is suggested for the next one (§5). |

---

## 1. Attributes: the definitions

### 1.1 How a rating becomes an outcome (shipped)

Every rating runs 0–100, with 50 as league average. The resolver turns each into a deviation `d = (rating − 50) / 50` in [−1, +1]. Each plate-appearance outcome's weight is `base × exp(k × Σ w·d)`, and the weights are renormalized ([AtBatResolver.cs](../../Assets/Simulation/Baseball/AtBatResolver.cs) `ComputeProbabilities`). A team of all-50s reproduces the calibrated Pro line exactly: **.247 / .315 / .412, K 22.5%, BB 9.0%, HR 3.2% of PA**.

**What +10 and +20 points are worth** against league-average opposition, from the shipped formula. These are rate effects per PA, before any modifiers or player skill:

| Attribute (who) | Drives | +10 | +20 |
| :-- | :-- | :-- | :-- |
| **bat_power** (hitter) | HR and doubles up; outs down slightly | OPS **+.034** (HR 3.2% → 3.7%) | OPS **+.071** (HR → 4.2%) |
| **bat_contact** (hitter) | Singles and doubles up; strikeouts and outs down | OPS **+.050** (AVG .247 → .269; K 22.5% → 21.2%) | OPS **+.103** (AVG → .292) |
| **bat_discipline** (hitter) | Walks up; strikeouts down. In attended games, also how much a good read is worth (§1.4) | OPS +.011 (BB 9.0% → 10.0%) | OPS +.022 (BB → 11.1%) |
| **pit_stuff** (pitcher) | Strikeouts up; HR, singles and doubles down | opponent OPS **−.045** (K → 24.2%) | opponent OPS **−.089** (K → 26.0%) |
| **pit_control** (pitcher) | Walks down. In attended games, also where pitches land (§2.2) | opponent OPS −.006 (BB → 8.1%) | opponent OPS −.012 (BB → 7.4%) |
| **pit_stamina** (pitcher) | Pitch capacity before fatigue; fatigue decays effective stuff and control toward 50 (floor ×0.5) | more innings at full strength | — |
| **fielding** (everyone) | The **team-defense** average of the lineup: outs up; singles, doubles and triples down | team OPS allowed **−.027** (one +10 *team* average) | −.052 |

**Read-out:** on the shipped weights, **contact is the strongest hitting attribute**, and **stuff is the strongest pitching one**. **Discipline and control are nearly inert in background sims.** Their main value is in attended games: discipline scales the read (§1.4), and control will drive pitch location (§2.2). *Proposal for Math:* either accept that (they're "attended-game" attributes), or raise their macro weights in the Phase 13 re-band.

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
| **intelligence, maturity, charisma, reputation, social_status, attractiveness, morality, discipline (person), work_ethic** | Event prerequisites and outcomes, dating and NPC autonomy, person drift, item rewards (`PersonDrift`, `EventConsequenceApplier`, `NpcAutonomyService`, `ItemService`) | **None reach the field.** *Proposal:* work_ethic scales practice → development; discipline (person) feeds plate discipline's read scaler; maturity shrinks slump and stress effects. Owner/Math call. |
| **gpa** | HS eligibility, school events, recruiting context | — |
| **Needs** (hunger, sleep, hygiene, social, fitness) and **stress** | Life sim only: utility AI, forced actions, events | **They never touch the field.** A starving, sleepless player bats like a rested one. *Proposal:* a small, capped game-day penalty from low sleep/fitness and high stress (an `Active_Effects` row baked at game start, bit-identical when needs are healthy). Owner/Math call. |
| **health_ceiling** | Injury rust, decline speed, forced retirement below the floor | — |
| **recklessness** | Event and hustle prerequisites; risk rolls | — |
| **baseball_interest** | Heirs only: will this child play? | — |
| **detection_risk** | Steroid tests, suspensions, scandal | — |
| **funds** | Survival economy, hustles, purchases, the search (Phase L) | — |

### 1.4 Ratings × player skill (the core principle)

> **Ratings set the floor and the ceiling. Player skill decides where you land between them.**

- **Batting (shipped, [at_bat_read_input_model.md](../reference/at_bat_read_input_model.md)).** Each pitch, the player guesses the **type** (FB / breaking / offspeed) and the **location** (3×3 grid or "ball"), and sets an **approach** dial.
  - The read quality `R` blends the two guesses. **Discipline** sharpens how much `R` is worth.
  - The swing is emergent: belief versus a discipline-set threshold, plus the approach.
  - Contact quality `q` scales with `R` and **contact**. **Power** enters through the resolver anchor.
  - **Reads are decisive.** A perfect read with high contact gives peak damage; a fooled swing gives weak contact or a whiff.
- **Pitching:** no player-facing mode yet. The career path passes `NeutralPitcherPolicy`, so a pitcher player only watches. An engine seam does exist: `PitchCall` (type + in/out-of-zone target, control-driven execution) and `InteractivePitcherPolicy`, but it is unused and NPC batters stay neutral against it. See [G-2 context pack](../tasks/G-2_pitching_context_pack.md) §6. §2.2 designs the mode.
- **Background games** use ratings only (no skill), so the league stays calibrated.
- **How much should skill be worth?** *Owner decided 2026-10-10 (§6, O1):* for an average-rated avatar, the gap between a poor and an excellent player-skill game should be about the gap between a **40- and a 70-rated** player (roughly ±.100 OPS). It's capped so skill can't make a 30-rated player elite, or ruin an 85.
  - This is the human branch only. The neutral, calibrated branch never changes (Invariant N in the read-input spec).
  - **Math owns the tuning**, through a scripted-skill harness: a perfect bot, a coin-flip bot and a contrarian bot.

---

## 2. The at-bat interfaces (G1)

### 2.1 Batting: upgrade the shipped read model (presentation-first)

The mechanics ship and work. The **interface** is thin. Upgrades, in priority order:

1. **A broadcast-style at-bat view**: catcher-eye camera, a visible pitch path (release → plate, type-tinted), the 3×3 zone overlay you aim your read into, and the count, outs, runners and score.
2. **Feedback that teaches:**
   - after each pitch, show where it really went against your read;
   - keep a per-pitcher tendency card (scouting) that sharpens your next guess;
   - show your hot/cold zones.
3. **Pace:** the reveal waits for the broadcaster's call; fast-forward option; reduce-motion.
4. **Accessibility:** a hold-to-confirm option, colorblind-safe zone tints, keyboard and controller.

No sim change, so this is calibration-inert.

### 2.2 Pitching: a new interactive mode (the biggest gameplay gap)

*Proposal, mirroring the batting model so both halves feel like one game.*

- **Per pitch, the player chooses:**
  - a **pitch** from the avatar's arsenal (`Pitch_Arsenals`; shipped data);
  - a **target** (3×3 grid plus 8 edge/chase cells);
  - an **intent** dial (pitch to contact ↔ go for the strikeout).
- **Where it lands:**
  - **Control** sets how tightly the pitch hits the target: a scatter radius that shrinks with control and grows with fatigue.
  - **Stuff** sets how hard it is to square up (whiff and weak-contact odds).
  - **Stamina** sets how long both hold.
- **The batter's AI read:** it uses the batter's discipline and contact, with a simple model of the batter guessing your **tendencies**. So mixing pitches and locations is the player's skill: predictable pitchers get hit.
- **Outcome:** the same swing-gated class draw and `DrawBallInPlay` as the batting model. A wild miss into the heart of the zone is punished; painting the edge after setting it up is rewarded.
- **Calibration contract:** a new human branch only (`IPitcherPolicy` already exists as the seam). `NeutralPitcherPolicy` stays byte-identical, so every band and guard is untouched by construction. Harness coverage: self-consistency, plus the skill-bot spread from §1.4.

### 2.3 Fielding moments (*proposal*)

The avatar's `fielding` rating only feeds the team average today, and listed positions are story-only.

*Proposal:* occasional **defensive moments** for the avatar's listed position: a quick read-and-react prompt on a ball hit to them, graded against fielding.
- It's opt-in in Settings, attended games only, on the human branch only.
- This would make listed position mean something on the field. It's an owner call and an R-1 review item, because positions are currently story-only by design (temptation §3.1).

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

- The at-bat view uses the **home park's** field art and dimensions overlay, so every park looks like itself.
- **Park factors:** *proposal: off at launch* (neutral, calibration-inert). If the owner wants parks to *play* differently, it becomes a separate Math slice with per-park HR/2B/3B factors, re-banded through `run_monte_carlo_batch`, with league-wide factors averaging 1.0. **Owner decision.**

### 3.4 Art scope

68 venues × about 5–7 layered scenes, plus day/night variants for the field, in the approved Phase 15 direction. Pre-generated, curated and disclosed (the AI-art rule). This feeds slice 15d. The two shipped HS field images (CRW, LKS) are the seed.

---

## 4. Slices (Phase G: high priority, runs alongside Phase 13)

| Slice | Content | Recommended model | Reviewer | Gate |
| :-- | :-- | :-- | :-- | :-- |
| **G-1** | Attribute audit: verify §1 against code, settle the macro weights of discipline and control, decide the needs/stress and person-stat proposals | **GPT 6 Astra Pro** (in review R-1; reassigned from Claude Fable 5.1, 2026-10-10) → **Gemini Pro Math** (numbers) | Claude Opus | Owner: "how much skill is worth" |
| **G-2** | Pitching mode design → engine (`IPitcherPolicy` human branch, scatter model, batter tendency read) | **Claude Fable 5.1** (design, owner 2026-10-10: [brief](../tasks/G-2_pitching_design_fable_brief.md)) → **GPT 6 Astra Pro** (engine; owner reassignment 2026-10-10) + **Gemini Pro Math** (scatter/whiff curves); UI: **Claude Sonnet 5.5** | Gemini Pro (sim diff) + Claude Opus (merge gate) | MC bit-identical on the neutral branch; skill-bot spread |
| **G-3** | At-bat view rebuild for batting + pitching (§2.1, §2.2 UI) | **Claude Sonnet 5.5** | Gemini Pro | Screenshot review; playtest |
| **G-4** | Venue catalog (68 entries) + venue screen runtime + hotspots | **Claude Sonnet 5.5** (screen) + **Gemini 3.8 Flash** (catalog data) | Claude Opus | Every team resolves a venue; no missing-art fallbacks |
| **G-5** | Venue art production | **Owner + AI art tools**, **Gemini 3.8 Flash** (integration) | Claude Opus (consistency) | **Owner taste** |
| **G-6** | Stadium mapping in the at-bat; optional park factors | **Claude Sonnet 5.5** (visual) / **Gemini Pro Math** (factors, if approved) | Claude Opus | Inert unless factors are approved |
| **G-7** | Fielding moments (if approved) | **Claude Opus 5.5** + **Claude Sonnet 5.5** | Gemini Pro | Human branch only |
| **G-8** | Read v2: re-band batting to the same skill band and tilt idiom as the pitching mode ([brief](../tasks/G-8_read_v2_batting_reband_brief.md)) | **GPT 6 Astra Pro** (design → engine) | Claude Opus + Gemini Pro | Random reader ≈ ratings; perfect/fooled ≈ ±.100 OPS |

**Order:** G-1 and the G-2 design start now, alongside 13-0/13-1. G-3 and G-4 follow the 15a/15b art direction and design system. G-5 runs inside 15d.

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

1. ~~**How much is skill worth** against ratings (§1.4)?~~ **Decided by the owner 2026-10-10:** about the 40-to-70 rating gap (±.100 OPS around ratings), capped, with a blind or average player at the ratings line, for **both** pitching (G-2) and batting (re-band: G-8).
2. **Needs and stress on the field** (§1.3)? Recommended: a small, capped game-day penalty.
3. **Person stats on the field** beyond the shipped three (§1.3)? Recommended: work_ethic → development, person discipline → read scaler.
4. **Park factors** (§3.3)? Recommended: off at launch.
5. **Fielding moments** (§2.3)? Recommended: yes, opt-in.
