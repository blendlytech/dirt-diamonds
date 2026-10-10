# Lineage, Temptation & the Trailblazer: Hidden Bloodlines, Pregnancy, the Female Route

**Author:** Claude Opus 5.5 (architecture + narrative) · **Status:** design draft 2026-10-10. No code yet. Owner decisions D1–D21 are all resolved (§0, §15).
**Authority:** owner decisions recorded in §0. They supersede the 16-1 brief ([../tasks/16-1_opening_sequence_brief.md](../tasks/16-1_opening_sequence_brief.md)) wherever the two disagree.
**Builds on:** [marriage_and_conception.md](../reference/marriage_and_conception.md) (`conceive_child`, `ChildBornEvent`), [heir_mechanics.md](../reference/heir_mechanics.md) (succession), [high_school_person_layer.md](../reference/high_school_person_layer.md) (person stats → `PersonEffects`), [presentation_layer_narrative.md](../reference/presentation_layer_narrative.md) (phone threads), [cutscenes_motion_comic.md](cutscenes_motion_comic.md) (Phase 16).

---

## 0. Owner decisions (2026-10-10)

| # | Decision |
| :-: | :-- |
| D1 | The avatar is a GOAT's illegitimate child, placed for adoption at birth and raised poor. The avatar doesn't know. |
| D2 | The career starts in the **Freshman** year (unchanged from today). |
| D3 | Ethnicity is a creation choice; the GOAT father is picked to match it. |
| D4 | The father's **first contact** comes at the avatar's **first Pro contract** (drafted or undrafted). He congratulates the avatar, needles them that they'll never beat his record, and says he's pulling for them. The relationship starts there. |
| D5 | The avatar learns the truth **only** on winning the Pro World Championship or at retirement, whichever comes first. |
| D6 | **No staple mentors.** The legends fill the record book; only the father texts. |
| D7 | Pregnancies are planned (after marriage) or unplanned (flings, girlfriends, affairs). **Never abortion.** Any child can be kept or placed for adoption. |
| D8 | Temptation has a real lure: a big confidence/happiness surge that lifts on-field play, against a hidden chance of getting caught or a pregnancy. |
| D9 | A child placed for adoption **lives on in the world, hidden**. It's revealed only if the avatar's line ever **plays against** that child's line, or if the avatar **pays to search** once they can afford it. Once found, the avatar chooses whether to tell the child; a told child joins the lineage, **officially at 18**. |
| D10 | The avatar can be **male or female**. Women can play pro. A female avatar's route is a story in itself: only the **3rd girl in the nation** to try out for and make an all-male team, with unique press articles and in-game moments, **for the first girl only**. |
| D11 | Voice-over is **broadcasters only** (Phase 14 scope change). Everything in this doc is text: phone, press, captions. |
| D12 | The Trailblazer route happens **once per save**. Her goal is to be the **first woman ever to play pro**. Through HS and college the dream is a joke to everyone else; in her **college senior year** it becomes real. |
| D13 | The avatar can **date either gender**, and can **adopt** children into the family. |
| D14 | A hidden child landing on the avatar's **own team** gets its **own reveal**, separate from the opposing-roster reveal. |
| D15 | Succession eligibility stays at **19** (`MaturityAge`). A claimed child becomes official at 18 and heir-eligible at 19. |
| D16 | **The missing piece.** Placing a child for adoption **freezes one attribute, picked at random from all of them** (from the least to the most significant), at its current value. It stays exactly there until the parent **reclaims** that child. The search can't start until the child is **19**; it costs a lot of money, takes a random **1 week to a full season**, and its stress hurts the parent's attributes until it ends. |
| D17 | **Affairs:** every affair gives **100% confidence plus a clutch boost** with nominal attribute gains. The chance of getting caught **and** the chance of a pregnancy **rise with every affair**, and each roll is random. |
| D18 | **Substances** (drugs, steroids, alcohol) each give a dramatic boost in one area, with real consequences that differ for each. Specified in [substances_and_addiction.md](substances_and_addiction.md). |
| D19 | Substance and temptation **boosts are 18+ only** (tier ≥ College); HS gets the story and the chance to say no. Owner accepted the recommendations: the unfinished Trailblazer dream passes to a later female heir, adopted-in children are heir-eligible, and the missing piece freezes the **base** rating. |
| D20 | **An overdose can be fatal** ([substances_and_addiction.md](substances_and_addiction.md) §2.6). |
| D21 | **The GOAT's record is juiced in 50% of careers**, rolled once at career start and hidden (§4.3). |

**Hard line (architecture lead, Steam policy):** the temptation lure (§6) is **adults-only**: tier ≥ College **and** both characters ≥ 18. HS romance keeps its shipped shape: implied, off-screen, the pregnancy a consequence and never a reward. Steam prohibits sexual content involving minors, and a stat reward for sex by HS characters would put the submission and the rating at risk.

---

## 1. The theme that ties it together

The game opens on a secret: a legend's affair, a child given away. Every system here lets the player **become their father**: chase his record, give in to the same temptation, maybe place a child of their own, and decades later have that child step into the batter's box against their heir. One data model (§3.3) carries both secrets, so the mirror is structural, not just thematic.

---

## 2. What already exists

| Seam | Where | Used for |
| :-- | :-- | :-- |
| `Family_Background` (wealth tier, parent ids) | schema v11 | The adoptive household; poverty start (§4.1) |
| `Entity_Flags` | schema | `is_adopted`, lineage flags |
| `conceive_child` → `ChildConceptionRequestedEvent` → `CareerManager.ConceiveChild` | marriage_and_conception.md §4 | Every birth (§5) |
| Planned arc `met_someone → starting_a_family → child_is_born` | `marriage_and_family_events.json` | Planned pregnancy |
| HS arc `hs_pregnancy_scare → hs_pregnancy_decision → hs_baby_born` | `hs_dating_events.json` | HS pregnancy (**must lose `end_the_pregnancy`**, §5.3) |
| `confidence` → bat_power/pit_stuff, `happiness` → bat_contact/pit_control | `PersonEffects.Bake` (`LeagueSimulator.cs:317`) | The lure's on-field effect (§6.1), already calibrated |
| `Player_Absences` + `AbsenceReason` | `PlayerDtos.cs:129` | Maternity absence (§5.2) |
| `EvaluateSuccession` (any willing child ≥ `MaturityAge` 19, interest ≥ 40) | `CareerManager.cs:845` | Heirs, now any gender (§7.6) |
| HS intake: one generated 15–16-year-old per vacancy | `PromotionManager.cs:765` | Where hidden children enter the league (§7.2) |
| Phone threads stored as `Game_Logs` rows (`narrative_msg`), future-dated rows for delayed texts | `NarrativeLogQueries.cs` | Father thread, PI thread, child thread, press (§9) |

---

## 3. Data model (schema v15, additive only; lands after 13-1's v14)

### 3.1 `Player_Identity`

```sql
CREATE TABLE IF NOT EXISTS Player_Identity (
    player_id       TEXT PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    gender          INTEGER NOT NULL DEFAULT 0 CHECK (gender IN (0, 1)),   -- 0 = male, 1 = female
    ethnicity       TEXT,                                                  -- content key (identity catalog); NULL = unspecified
    listed_position TEXT                                                   -- 'SP','RP','C','1B','2B','3B','SS','LF','CF','RF'; narrative only
) STRICT;
INSERT OR IGNORE INTO Player_Identity (player_id) SELECT player_id FROM Players;
```

- The **sim never reads this table.** `listed_position` is a story label (the father's record, articles, announcer roles), not a defensive position. Calibration stays inert.
- The backfill gives every existing row `gender = 0`: the shipped league is all-male, and so are all generated players (D10's premise).
- Children get a seeded gender roll at `ConceiveChild` from a `Split()` of `CareerManager._rng`, so no existing draw moves.

### 3.2 Legends

- **Content:** `Assets/Data/Legends/legends.json`, one entry per legend: `legend_id`, names, `ethnicity`, `portrait_key`, and a **position-agnostic** backstory (it uses `{position}` and `{record_stat}` tokens because positions are assigned per save). Loaded and validated loudly, like `items.json`.
- **People:** each legend is an unrostered `Players` row (retired age, `team_id NULL`), so they get relationships, flags, portraits and a phone contact for free.
- **Per save:**

```sql
CREATE TABLE IF NOT EXISTS Legend_Assignments (
    legend_player_id TEXT PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    position         TEXT NOT NULL UNIQUE,
    record_stat      TEXT NOT NULL,   -- key into the record map (§4.3)
    record_value     REAL NOT NULL
) STRICT;
```

### 3.3 `Biological_Parentage`: one table for every secret

```sql
CREATE TABLE IF NOT EXISTS Biological_Parentage (
    child_id        TEXT PRIMARY KEY REFERENCES Players(player_id) ON DELETE CASCADE,
    bio_parent_id   TEXT NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    other_parent_id TEXT REFERENCES Players(player_id) ON DELETE SET NULL,
    origin          INTEGER NOT NULL CHECK (origin IN (0, 1)),  -- 0 = legend secret (D1), 1 = placed for adoption (D9)
    parent_located  INTEGER NOT NULL DEFAULT 0 CHECK (parent_located IN (0, 1)),  -- parent knows who/where the child is
    child_knows     INTEGER NOT NULL DEFAULT 0 CHECK (child_knows IN (0, 1)),     -- child knows who the parent is
    told_day        INTEGER,   -- day the child was told (claim)
    official_day    INTEGER,   -- day the Child edge was written (§7.5)
    locked_stat     TEXT CHECK (locked_stat IN ('bat_power','bat_contact','bat_discipline',
                                                'pit_stuff','pit_control','pit_stamina','fielding','happiness')),
                               -- origin 1 only: the parent's missing piece (§5.6); NULL for origin 0
    unlocked_day    INTEGER    -- day the lock lifted (reconnection, §7.5); NULL while locked
) STRICT;
CREATE INDEX IF NOT EXISTS idx_bio_parent ON Biological_Parentage(bio_parent_id);
```

| Case | `bio_parent_id` | `parent_located` | `child_knows` |
| :-- | :-- | :-: | :-: |
| The GOAT and the avatar (D1) | legend | 1 | 0 → 1 at the reveal (§4.4) |
| A child the avatar placed (D9) | avatar | 0 → 1 when found (§7.3/§7.4) | 0 → 1 when told (§7.5) |

**Secrecy invariant:** nothing outside `LineageQueries` reads this table. No UI, no `RelationshipGraph` hydration, no succession, no family screen. A hidden child has **no `Child` edge**, so every shipped lineage path is blind to them by construction.

### 3.4 `Indiscretions`: the hidden risk ledger

```sql
CREATE TABLE IF NOT EXISTS Indiscretions (
    indiscretion_id INTEGER PRIMARY KEY,
    player_id       TEXT NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,  -- the avatar
    partner_id      TEXT REFERENCES Players(player_id) ON DELETE SET NULL,
    kind            INTEGER NOT NULL CHECK (kind IN (0, 1, 2)),  -- 0 = fling, 1 = premarital (dating), 2 = affair (married)
    day             INTEGER NOT NULL,
    discovered      INTEGER NOT NULL DEFAULT 0 CHECK (discovered IN (0, 1)),
    conceived       INTEGER NOT NULL DEFAULT 0 CHECK (conceived IN (0, 1))
) STRICT;
CREATE INDEX IF NOT EXISTS idx_indiscretions_player ON Indiscretions(player_id);
```

Hidden from the player. It drives the discovery chance (§6.2) and supplies the co-parent when an unplanned pregnancy follows (§5.1).

### 3.5 Flags

`is_adopted` (set at creation), `knows_true_lineage` (absent until D5), `father_contacted`, `lineage_reveal_pending`, `trailblazer_route` (§8). They're all ordinary `Entity_Flags` rows.

---

## 4. The GOAT father arc

### 4.1 Creation (inside the `CreateAvatar` batch)

1. Write `Player_Identity` for the avatar (gender, ethnicity, listed position).
2. **Poverty:** map the backstory's **same single percentile draw** onto tiers 0–1 instead of 0–4. The draw order is unchanged, so careers stay deterministic. Re-roll stays within poverty. The backstory harness gains an adoption-mode frequency table. The adoptive parents are the household `CreateAvatar` already builds (`family_mom`, `family_dad`).
3. **Assign legends** from a `Split()` of the career stream: pick the father from the legends whose `ethnicity` matches; give him the avatar's `listed_position`; shuffle the rest into the other nine positions; write `Legend_Assignments`.
4. **No-match fallback:** an owner-authored rule (`LegendAssignment.ChooseFatherFallback`). Owner contribution.
5. Write `Biological_Parentage(avatar, father, origin 0, parent_located 1, child_knows 0)` and set `is_adopted`.

### 4.2 First contact (D4)

- **Trigger:** a new additive Core event, `ProContractSignedEvent(playerId, teamId, day, wasDrafted)`, published by the 13-6 pipeline for both drafted and undrafted signings. An avatar-scope event fires once on the first one (`flag_inactive: father_contacted`).
- **Thread:** a new contact, `unknown_fan`, separate from the syndicate's `unknown`. Its display name is resolved **at render time**: "Unknown Number" until `knows_true_lineage`, then the father's name. Stored rows are never rewritten.
- **Draft script** (Opus to finalize): *"Saw you signed. Congrats, kid. Pros now. Real pitching. For what it's worth, I'm pulling for you. Still, let's be honest: you'll never touch {father_name}'s {father_record_value} {father_record_stat}. 😉"* The joke is that he's talking about himself in the third person.
- The texts never include a voice note (D11).

### 4.3 The record map (trackable stats only)

| Pos | Record | Pos | Record |
| :-- | :-- | :-- | :-- |
| SP | Strikeouts | 2B | Hits |
| RP | Saves | 3B | RBIs |
| C | Walks | SS | Doubles |
| 1B | Home runs | LF | Slugging % (minimum career PA) |
| CF | Triples (switches to stolen bases if base-running is ever simulated; `sb` is written as 0 today) | RF | Total bases |

This lives in data (`legends.json` → `record_map`), not code. **Pro Math** sets `record_value` per stat so it's "unbreakable but imaginable" for a 64-game Pro season.

**The father's asterisk (D21):** at career creation, a 50/50 roll on the legend `Split()` sets `Game_State['father_record_juiced']`. It's hidden. In a juiced career, late record-chase press starts asking questions about the old era, and the lineage reveal (§4.4) can confirm it. Then the record the avatar chased was never clean, and the asterisk ([substances_and_addiction.md](substances_and_addiction.md) §2.1) was the father's secret before the bloodline was. In a clean career his record is just what he said it was.

### 4.4 The ongoing relationship and the reveal (D5)

- **Ongoing:** a `Friend` edge (never `Child`) from the first contact. Avatar-scope events on the `unknown_fan` thread: first Pro home run, slumps, scandal (a discovered indiscretion; he's in no position to judge, and his text knows it), and a **record-chase ladder** at 50/75/90/100% of his mark, where the needling gets sharper and then turns proud.
- **The reveal:** the first of
  - `ChampionshipWonEvent` (new in 16b) at Pro tier with the avatar on the winning roster, or
  - retirement.
- **Ordering hazard:** retirement triggers succession, which swaps the avatar. The reveal must fire **before** `Succeed()`. The retirement flow becomes *retire → `lineage_reveal_pending` → reveal event + story cutscene → succession UI*.
- **On reveal:** `child_knows = 1`, set `knows_true_lineage`, and the thread re-labels. A story cutscene (text captions only) plays with the `father_portrait` and `father_name` slots.

---

## 5. Pregnancy

### 5.1 Planned and unplanned

| Path | Source | Co-parent |
| :-- | :-- | :-- |
| Planned | Marriage arc (`start_trying`), as shipped | The `Partner` edge (shipped behavior) |
| Unplanned, dating | A relationship event (HS: the shipped implied arc; College+: §6) | The dating partner |
| Unplanned, fling/affair | §6 temptation outcome | `Indiscretions.partner_id` |

`conceive_child` gains an optional payload, `"co_parent": "partner" | "indiscretion"` (default `partner`, so shipped content stays byte-identical), plus a **placement** step: every birth event offers **keep** or **place for adoption** (D7).

**Conception needs an opposite-sex pair** (D13). The pregnancy paths, planned or unplanned, are only reachable when the avatar and the co-parent are one male and one female (`Player_Identity.gender`). A same-sex couple's family-planning beat offers **adoption** (§5.5) in place of `start_trying`. Same-sex flings and affairs carry the full temptation and discovery risk (§6) but no pregnancy roll.

### 5.2 Who carries

- **Male avatar:** the partner carries; the avatar learns from a text, and gets the choice.
- **Female avatar:** she carries. A new `AbsenceReason.Maternity = 4` (additive enum) covers the late term and recovery. A pregnancy that overlaps a season becomes a career decision (an offseason due date or a mid-season absence), with contract and reputation effects as content.

### 5.3 Never abortion: content changes and a guard

- Remove `end_the_pregnancy` from `hs_pregnancy_decision`.
- `place_for_adoption` no longer just ends the arc. It routes through the birth (`hs_expecting_confirmed` → 270 days → birth with placement), so the child exists (§7).
- **New integrity check** (`check_event_graph_integrity` #11): every consequence that clears an `*expecting*` flag must sit on a path that reaches a birth event (`conceive_child`). No choice anywhere may end a pregnancy without a birth.

### 5.4 Keeping a child vs. placing one

- **Keep:** the shipped `ConceiveChild` path (`Child` edges, `Child_Development`, succession-eligible).
- **Place:** `ConceiveChild` creates the child, then writes **no `Child` edge**. Instead it writes `Biological_Parentage(origin 1)`, an adoptive `Family_Background` with generated NPC parents (the avatar's own creation inverted), and the adoptive surname. The child ages on the shared yearly tick. Their genetics (blended ratings, rolled interest) are the real bloodline's, so a hidden child tends to be gifted. Placement also costs the parent **the missing piece** (§5.6).

### 5.5 Adopting a child into the family (D13)

Any avatar can adopt: single or partnered, any gender, any couple.

- **Flow:** an agency contact (text-only) runs the process, with fees (funds), a home-study wait (game time) and a match. Content can gate it on stability: household income, no open arrest record.
- **Engine:** a new `CareerManager.AdoptChild(ageAtPlacement)`. It creates a child from **generated** birth parents (their ratings come from the intake distribution, not the avatar's), takes the avatar's surname, writes a `Child` edge to the avatar (and partner), and sets an `adopted_in` flag.
  - Their interest roll uses the generated-player distribution; `Child_Development` nurture applies exactly as it does for a birth child.
- **Heirs:** `EvaluateSuccession` only looks at `Child` edges, interest, age and ratings, so an adopted child is **heir-eligible by construction**. That fits the game's premise, since the avatar was adopted too.
- **Guard:** the adoption pool never draws a hidden child of this dynasty (§3.3). It always generates a fresh child.

### 5.6 The missing piece (D16)

Placing a child takes a piece of the parent that doesn't come back until they reclaim that child.

- **The draw:** at the moment of placement, `CareerManager` draws one attribute **uniformly from all seven ratings that aren't already locked**: `bat_power`, `bat_contact`, `bat_discipline`, `pit_stuff`, `pit_control`, `pit_stamina`, `fielding`. It uses a `Split()` of its stream, so no other draw moves.
  - The pool is deliberately everything, from the least significant attribute (a hitter's pitching stuff) to the most (their power). The luck of the draw is part of the cost.
  - It's stored on that child's row as `Biological_Parentage.locked_stat`.
- **The freeze is exact:** whatever the attribute reads, it **stays**. At 55 power, the player is at 55 power until the reclaim: no growth, no decline.
  - `DevelopmentManager` is the one place ratings change (the offseason pass, `DevelopmentManager.cs:498`), and it writes the locked column back unchanged.
  - Affair nudges (§6.1) skip it too.
  - No lock rows means the exact shipped code path, bit-identical.
- **Scope: the base rating (D19).** Gear, `PersonEffects`, substance edges and the search-stress penalty (§7.4) still apply in-game on top of the frozen base, because they're temporary modifiers.
- **Several placements:** each locks a **different** attribute. Once all seven are locked, a further placement locks `happiness`, and `PersonQueries.AdjustStat` holds it fixed. Each lock belongs to its own child.
- **The cure is reclaiming (D16):** the lock lifts on **reclaim**, the day the `Child` edge is written (§7.5, `official_day`; also sets `unlocked_day`).
  - Through the paid search (§7.4) the child is already ≥ 19, so telling them and reclaiming them happen together.
  - Through a chance meeting (§7.3) with a younger child, telling them starts the relationship, but the lock holds until the reclaim at 18.
  - Finding a child and keeping your distance leaves the lock in place.
  - Once lifted, the attribute develops normally from its frozen value the next offseason, with no catch-up; content can add a reunion surge.
- **Visible to the player, silent to the world:** the Player screen shows the frozen attribute with a hollow mark and the tooltip "*Something's missing.*" It never names or hints at the child, and no NPC, article or event reads the lock.
- **Ownership:** the lock belongs to the parent. After succession the heir carries none.
- **The mirror:** the GOAT gave the avatar away, so he has a missing piece too, as story only. At the reveal (§4.4) his text admits something left his game the year he signed the papers.
- **Balance note (Pro Math + playtest):** the **keep** path needs costs just as real (funds, stress, time, Child_Development commitment), so the choice stays a dilemma. Any harness that autopilots event choices must keep placement out of Monte Carlo guard runs.

---

## 6. Temptation (adults-only: tier ≥ College, both ≥ 18)

### 6.1 The lure

Temptation events (road trips, a hotel bar, a teammate's party, an old flame, a persistent admirer, the mistress arc) offer a choice whose **immediate** payoff is large.

**The affair package (D17, married avatar, `Indiscretions.kind = 2`).** Every time, at full strength (the high never fades with repeats; the **risk** is what escalates):

| Effect | Size | Mechanism |
| :-- | :-- | :-- |
| **Confidence → 100** | For the swagger window (Pro Math; first pass 7 days) | A temporary **override** in a new `Active_Effects` table (§6.4). The stored `confidence` is untouched and reasserts when the window ends. Through `PersonEffects` this is the lever's max, **+2 power/stuff**. |
| **Clutch** | The real kick: a large boost in **high-leverage PAs only** (Pro Math) | A new `ClutchEffects` stage in the resolver chain (§6.4). Leverage uses the announcer's `late_tension` definition (inning ≥ regulation − 2, margin ≤ 2, runner in scoring position), so "clutch" means the same thing on the field and in the booth. |
| **Nominal attribute points** | +1 to one role stat per affair (a random pick, capped per season) | A permanent rating nudge published as `RatingNudgeRequestedEvent` (Narrative can't call Baseball) and serviced by `CareerManager`. A **missing-piece lock (§5.6) blocks the nudge** on its locked stat. |

**Flings and premarital** (`kind` 0/1) carry a lighter version: confidence and happiness surges and stress relief, with no clutch and no nudge. The affair is the top-tier lure because the betrayal is the cost.

### 6.2 The risk: escalating and random (D17)

Each accepted temptation writes an `Indiscretions` row and rolls two hidden chances. **Both rise with every affair, and both are random:** the escalation sets the odds, and the dice decide.

- **Pregnancy:** `TemptationMath.PregnancyChance(affairCount)`, a base chance plus a step per prior `Indiscretions` row (Pro Math sets base and step). It's never rolled for a same-sex pair (§5.1). On a hit, the pregnancy arc for that co-parent starts after a delay.
- **Discovery:** `TemptationMath.DiscoveryChance(affairCount, recklessness, fame)` (`Assets/Simulation/Life/`, pure and harness-tested). **Owner contribution:** the curve that decides how strong the pull feels.
  - It rises with every affair, plus recklessness and fame (reputation/social status, tier). Alcohol on board raises it too ([substances_and_addiction.md](substances_and_addiction.md)).
  - It's rolled at the encounter and again on later "loose end" beats: a text from the wrong number, a photo, a tabloid. Old affairs stay live risks.

**Fallout on discovery** (content, flag-cascaded): marriage strain into the shipped divorce arc, a reputation and press hit (§9), child support (funds), clubhouse chemistry (teamwork), and, for a married avatar, the partner's own story beats.

### 6.3 The partner pool and the kin guard

- **Either gender** (D13): partners are generated civilians with a stored gender, replacing the shipped `league`-only pool (marriage_and_conception.md §9.1). The avatar's dating preference is a creation or settings choice that filters the pool.
- **Kin guard (silent):** the partner, fling and affair pools **exclude anyone biologically related to the current avatar**:
  - their bio parent (the legend);
  - any hidden child of any avatar in the dynasty, and those children's descendants;
  - any half-sibling.

  The exclusion is invisible: no message, no greyed-out option, so it never leaks a secret. `LineageQueries.IsKin(a, b)` is the one check, used by every pool builder.

### 6.4 The shared effect layer: `Active_Effects` + clutch (also used by substances)

```sql
CREATE TABLE IF NOT EXISTS Active_Effects (
    player_id    TEXT    NOT NULL REFERENCES Players(player_id) ON DELETE CASCADE,
    effect       TEXT    NOT NULL,   -- 'affair_swagger', 'steroids', 'amphetamines', 'hangover', ... (content key)
    started_day  INTEGER NOT NULL,
    expires_day  INTEGER NOT NULL,
    magnitude    INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (player_id, effect)
) STRICT;
```

- **One layer for every temporary edge:** affair swagger, each substance's high, hangovers, withdrawal.
- **When it applies:** effects are **baked at the same re-init beats** as `PersonEffects`/gear, through a version-stamped cache like the gear-boost cache. They're rebuilt when an effect starts or expires (a day-tick check), never computed per PA. The exception is the **clutch** stage, which needs the live game state; it's a single leverage test, skipped entirely unless the batter or pitcher has an active clutch effect.
- **Calibration:** no `Active_Effects` rows means the exact shipped code path, so the Monte Carlo guard stays byte-exact. Each effect's magnitude is a Pro Math data value with a `run_monte_carlo_batch` gate.

### 6.5 HS

HS keeps the shipped arc: dating → the implied "We're late" → the decision (keep or adopt) → birth. **No temptation events and no stat reward for sex before tier ≥ College with both characters ≥ 18.** A loader guard rejects any temptation-category event without those prerequisites.

---

## 7. Hidden children (D9)

### 7.1 Lifecycle

`placed (hidden)` → `found` (`parent_located = 1`) → `told` (`child_knows = 1`, `told_day`) → `official` (`Child` edge, `official_day`, at age ≥ 18).

The avatar knows a child was placed, because they made the choice. They don't know who the child is or where they live.

### 7.2 Entering the league

At HS intake (`PromotionManager`, tier 0), before generating a fresh 15–16-year-old for a vacancy, the intake **takes an eligible hidden child**: the right age, revealed interest ≥ `InterestPlayThreshold`, and not yet rostered. It's still one-for-one, so the conservation law holds.

- A gifted hidden child is a real prospect, rising through the same pipeline as everyone else.
- This touches the calibrated pipeline. It lands after 13-6, with a Monte Carlo gate.

### 7.3 Reveal trigger A: crossing paths with the family

Two triggers, each with its own reveal (D14). Both fire at any tier and in any game type, playoffs included, and each fires at most once per (avatar, kin) pair.

| Trigger | Condition | Reveal |
| :-- | :-- | :-- |
| **Opponent** | The current avatar and a hidden kin are on **opposing, non-absent** rosters for a game that day | `kin_reveal_opponent`: across the diamond, a likeness the avatar can't place |
| **Teammate** | The current avatar and a hidden kin are on the **same roster** (checked daily, so a trade, promotion or signing counts the first day they share a clubhouse) | `kin_reveal_teammate`: a **slow burn**. Shared dugout beats raise a hidden suspicion counter (the same swing, the same superstition, a birthday that lines up), and the reveal lands when it peaks or on a forcing event (a blood drive, an injury needing a donor, a parent's visit) |

- **Who counts as kin:** any hidden child (`Biological_Parentage`, origin 1, `child_knows = 0`) of any avatar in this dynasty, against the **current avatar** of any generation.
- **Detection:** a read-only `KinEncounterWatcher` (Baseball assembly) on `DayAdvancedEvent`. It checks today's avatar roster and opponent against an in-memory set of hidden-kin ids, then publishes `HiddenKinEncounteredEvent(avatarId, kinId, day, isTeammate)`. It never writes sim state, so it's calibration-inert.
- **Who it usually hits:** the timing makes it likely that the reveal reaches an **heir**, not the original avatar. A child placed at 25 reaches the Pros around the time the avatar's heir does, so the meeting is often between **half-siblings**. That's the "include its lineage" clause in practice.
- **The opponent scene:** a story beat (text plus a story cutscene with captions) built on a likeness the avatar can't place. The pitcher has their eyes. Then the box score, then a name, then a call.
- **Either reveal:**
  - If the current avatar **is** the bio parent → `found`, and the tell choice follows (§7.5).
  - If the current avatar is a descendant (a half-sibling, say), it's a family revelation and a `Friend` edge. The kin can't become *this* avatar's heir, because they're a sibling, not a child.

### 7.4 Reveal trigger B: the paid search (D16)

- **When:** only once the placed child is **19**. Before that the PI won't take the case: records are sealed, and that's the law in this world. The parent also needs the money.
- **Cost: a big hit, and it's felt.** A large up-front retainer plus fees as it runs (Pro Math sizes it against Pro salaries so it hurts even a pro). Hiring a private-investigator contact (`pi_contact`, text-only) starts it.
- **Duration: completely random,** anywhere from **1 week to a full season**: a uniform draw of days on a `Split()` stream, fixed when the search starts and hidden from the player. The PI's update texts arrive along the way.
- **It hurts while it runs:** a `search_stress` row in `Active_Effects` (§6.4) for the whole search. It raises `Life_Stress` and applies a **negative attribute penalty** across the board (Pro Math) **until the search ends**. The player carries it on the field every day it lasts.
- **Exposure:** each PI update rolls a leak chance (higher if the child came from an affair and the parent is married or famous) into the scandal fallout (§6.2).
- **It always ends in a find:** the PI delivers a name, a town and whether they play ball. `parent_located = 1`, the stress effect expires, and the tell-and-reclaim choice follows (§7.5).

### 7.5 Telling and claiming

Once a child is found, by either trigger, the avatar chooses:

- **Tell them:** `child_knows = 1` and `told_day` are set, a child contact thread opens, and **the missing piece unlocks** (`unlocked_day`, §5.6). The relationship is real from that moment (texts, visits, events), but **not official**.
  - At the yearly tick where the child is ≥ 18, `ClaimWatcher` writes the `Child` edge and sets `official_day`. From then on the child is the avatar's lineage, and succession can see them.
- **Keep the distance:** the record stays `found`, **the stat stays frozen**, and the avatar can reach out later. Some events can force the issue (the child finds *them*).

### 7.6 Succession

Succession stays **any willing child, any gender**: born, claimed or adopted in (D10, D13).

- **Age (D15):** official at 18, heir-eligible at `MaturityAge = 19` (unchanged, so the heir harness fixtures don't move).
- **Engine gap:** a claimed child may **already be rostered** (§7.2), but `Succeed()` assumes an unrostered heir. `Succeed` must handle "the heir already has a team": the heir keeps their own team, and the retiree's slot backfills as usual. That's an Opus sim change, reviewed by Gemini Pro.

---

## 8. The female route: "the Trailblazer" (D10, D12)

### 8.1 Rules

- **Gender is story-only.** No ratings penalty, no cap, no sim read. Her ability is her sliders. Her obstacles are **social** and arrive as events; through `PersonEffects`, pressure that shakes her confidence still costs her at the plate, using the calibrated math everyone shares.
- **Once per save (D12):** the route belongs to whoever becomes **the first woman to play pro** in this world.
  - `Game_State['first_female_pro_id']` is empty at world creation.
  - A female avatar gets `trailblazer_route` while that key is empty.
  - When she signs her first Pro contract (`ProContractSignedEvent`), the key is set to her id.
  - From then on, no one else can be first: a later female heir plays the standard route, and the press calls her "the daughter of the woman who broke the barrier."
  - If a trailblazer retires before reaching the Pros, the key stays empty, so a later female heir inherits the unfinished dream (D19).
- **She's the GOAT's daughter.** The father's first text (§4.2) lands the day the first woman in pro baseball signs. He knows exactly who she is; the world doesn't.

### 8.2 The arc: a joke, then a possibility, then a first

The dream of playing pro is the spine. Everyone else's view of it moves through three acts.

| Act | When | The dream is… | Beats |
| :-- | :-- | :-- | :-- |
| **I. The joke** | HS, all four years | A punchline | She makes an all-male varsity through the `hs_tryouts` variant, only the **3rd girl in the nation** to do it. Coach Malone's skepticism, the locker-room logistics, the team accepting her, the first hostile crowd. She says "pro" out loud and gets laughed at: teammates, a guidance counselor, a local columnist's "cute story" piece. A good game earns "not bad, for a girl." |
| **I. The joke, still** | College, freshman–junior | A novelty | A scholarship that the papers treat as a stunt. A rival coach who won't shake hands. Fans waving signs. Scouts who come for a teammate and don't take notes on her. The articles get longer but stay in the human-interest section. |
| **II. The possibility** | College **senior year** | Real | The turn. A scout's notebook comes out. A national outlet runs a serious piece: "*Could {avatar_first} be the first?*" Mock drafts list her. The jokes go quiet, and the opposition gets louder and more organized (a petition, a commissioner's non-answer, threats). Draft-season stress spikes. Her performance, through `PersonEffects`, decides whether it's real. |
| **III. The first** | First Pro contract | History | Draft Night (or the undrafted signing): the first woman in pro baseball. A front-page article, a story cutscene (captions only), the broadcaster's welcome call, the GOAT's first text, and her first Pro at-bat as its own beat. After that, the route carries on as the "only one" experiences (§8.3) for her whole Pro career. |

The two girls who came before her in HS show up in the press only, as names in her articles. Neither went pro. They don't text her (D6).

### 8.3 Only-her experiences

- Media attention: reputation up, stress up.
- Sponsorship offers (funds) and online abuse (stress, confidence).
- Fan mail from girls who play.
- Her own press set (§9).
- A small set of **broadcaster** calls for her firsts ("History at Lighthouse Field…"). These are allowed under D11.

### 8.4 Announcer rule (Phase 14 amendment)

Pre-recorded calls can't know the avatar's gender, so **avatar-facing categories use roles, never pronouns** ("the kid", "the Barons' shortstop"). The Phase 14 doc already avoids names; this extends it to pronouns. The trailblazer calls are the only gendered avatar lines, and they're gated on `trailblazer_route`.

---

## 9. Press clippings (a new, shared feature)

- **Content:** `Assets/Narrative/Press/*.json` article templates: `id`, trigger (a bus event + prerequisites, same shape as gritty events), `route: any | trailblazer`, `outlet` (a fictional paper per tier/town), `headline`, `body` (tokens §10), and an optional `cutscene`.
- **Delivery:** persisted as `Game_Logs` rows with `event_type = 'press_article'` (additive, the `narrative_msg` pattern). They're shown in a **News** tab on the phone (Sonnet) and archived as a clippings scrapbook.
- **Launch set:** both routes get the draft, signing, records, championship, scandal (§6.2) and the record chase against the father's mark. The trailblazer route gets its exclusive set across the §8.2 ladder.

---

## 10. Text tokens: `NarrativeTokenResolver`

- **Resolved at write time** (snapshot). A stored text says what it said the day it arrived. Unknown tokens fail at content load, and integrity check #12 verifies them.
- **Vocabulary:** `{avatar_first}`, `{avatar_subject}`/`{avatar_object}`/`{avatar_possessive}` (from `Player_Identity.gender`), `{father_name}`, `{father_record_stat}`, `{father_record_value}`, `{position}`, `{team_name}`, `{kin_name}`, `{outlet}`.
- **Audit:** shipped content is written in the second person, so the gendered-text audit is small. The main item is `girlfriend_jess` (a gendered pro-era contact), which becomes partner-role neutral.

---

## 11. Content rules

1. Temptation and its rewards are adults-only (§6.5). Nothing sexual is explicit at any age; everything stays off-screen and implied.
2. Never abortion (§5.3). Placement for adoption is always offered.
3. Text only (D11). No `voice_note`, no cutscene VO beyond broadcaster calls.
4. Gender never touches the sim (§8.1).
5. The secrecy invariant (§3.3): no UI may hint at a hidden child or the father before their reveal.

---

## 12. Boundaries and calibration

| Change | Assembly | Gate |
| :-- | :-- | :-- |
| v15 tables, `LineageQueries` | Data | `validate_sqlite_schema` |
| `CreateAvatar` (identity, legends, poverty mapping) | Simulation/Baseball | `run_monte_carlo_batch` (no band moves), Gemini Pro review |
| `ConceiveChild` (gender roll, co-parent, placement) | Simulation/Baseball | Monte Carlo + heir fixtures |
| HS intake takes a hidden child (§7.2) | Simulation/Baseball | Monte Carlo, Pro Math (talent distribution) |
| `Succeed()` with a rostered heir (§7.6) | Simulation/Baseball | Monte Carlo + succession fixtures |
| `DevelopmentManager` missing-piece clamp; `AdjustStat` happiness lock (§5.6) | Simulation/Baseball, Data | Monte Carlo (no lock rows = bit-identical) |
| `KinEncounterWatcher`, `ClaimWatcher` | Baseball (read-only) / Narrative | Inert |
| `Active_Effects` bake (confidence override, substance edges) (§6.4) | Simulation/Baseball | Pro Math + Monte Carlo (no rows = bit-identical) |
| `ClutchEffects` leverage stage in the resolver chain (§6.1) | Simulation/Baseball | Pro Math + Monte Carlo; skipped unless an active clutch effect exists |
| `RatingNudgeRequestedEvent` affair nudges (§6.1) | Core → Simulation/Baseball | Monte Carlo; respects missing-piece locks |
| Events, press, tokens, contacts | Narrative / content | `check_event_graph_integrity` (+ #11, #12) |

---

## 13. Validation

- **GrittyEventsHarness:**
  - the token resolver (every token; unknown tokens rejected);
  - `co_parent` and placement parsing;
  - the adults-only loader guard;
  - the never-abortion check (#11).
- **MonteCarloHarness:**
  - legend assignment is deterministic, matches ethnicity, places the father at the avatar's position, and fills all 10 positions uniquely;
  - a placed child has no `Child` edge and a `Biological_Parentage` row;
  - intake takes an eligible hidden child exactly one-for-one;
  - `KinEncounterWatcher` fires once per pair, with `isTeammate` set correctly for a shared roster and an opposing one, and never for an absent player;
  - `IsKin` excludes every blood relative from the partner, fling and affair pools, and the adoption pool never yields a dynasty hidden child;
  - conception is unreachable for a same-sex pair; adoption creates a `Child` edge with generated (non-blended) ratings and is heir-eligible;
  - `first_female_pro_id` is set exactly once, and `trailblazer_route` is never granted after it;
  - placement locks exactly one unlocked attribute out of all seven (seeded, deterministic); the locked value stays exactly the same across offseasons (no rise, no fall); a second placement locks a different one; the eighth lock falls on `happiness`;
  - the search can't start before the child is 19; its duration falls in [7 days, one season] and is fixed at the start; `search_stress` applies for exactly that span;
  - finding without reclaiming keeps the lock; the reclaim lifts only that child's lock, and development resumes from the frozen value;
  - an affair sets the confidence override to 100 for the window and restores the stored value after; clutch applies only in `late_tension` PAs; both chances rise with each `Indiscretions` row;
  - the claim writes the `Child` edge only at ≥ 18;
  - the reveal precedes `Succeed()`;
  - no §8 band moves.
- **Content harness:** every identity-catalog ethnicity has ≥ 1 legend; ≥ 10 legends; the record map covers all 10 positions with tracked stats.

---

## 14. Slices

| Slice | Content | Author | Reviewer | Depends on |
| :-- | :-- | :-- | :-- | :-- |
| **L-0** | Docs: voice amendments (D11), remove `end_the_pregnancy`, 16-1 brief revision | Claude Opus | Gemini Pro | — |
| **L-1** | Schema v15 + `LineageQueries` + `CreateAvatar` identity/legends/poverty + New Game gender/ethnicity/position UI | Claude Opus (Sonnet: UI) | Gemini Pro | 13-1 |
| **L-2** | `legends.json`, identity catalog, record map | Flash | Claude Opus | L-1 |
| **L-3** | Token resolver, `unknown_fan`, father arc events, `ProContractSignedEvent`, reveal ordering | Claude Opus (scripts) + Gemini Pro (event) | Claude Opus / Gemini Pro | 13-6, 16b |
| **L-4** | Pregnancy: `co_parent`, placement, gender roll, opposite-sex rule, Maternity absence, check #11; `AdoptChild` + agency arc; missing-piece lock + `DevelopmentManager` clamp + Player-screen mark | Claude Opus (Flash: agency events) | Gemini Pro | L-1 |
| **L-5** | Temptation events + `Indiscretions` + `TemptationMath` (`DiscoveryChance`, `PregnancyChance`) + `Active_Effects` + `ClutchEffects` + rating nudges; civilian partner pool (either gender) + `IsKin` guard | Claude Opus (+ owner: `DiscoveryChance`) + Math | Gemini Pro | L-4 |
| **L-6** | Hidden children: intake, `KinEncounterWatcher` (opponent + teammate), both reveal arcs, PI search, tell/claim (+ unlock), `Succeed` rostered heir | Claude Opus | Gemini Pro + Math | L-4, 13-6 |
| **L-7** | Press clippings (runtime + News tab) + launch articles | Gemini Pro (runtime) + Sonnet (tab) + Flash (articles) | Claude Opus | L-3 |
| **L-8** | Trailblazer route content + trailblazer broadcaster calls | Claude Opus (story) → Flash | Owner (**story sign-off**) | L-7, 14c |

---

## 15. Open owner decisions

**All resolved 2026-10-10** (D12–D21). The owner accepted the recommendations: the unfinished Trailblazer dream passes to a later female heir (§8.1), adopted-in children are heir-eligible (§5.5), and the missing piece freezes the base rating (§5.6).

**Owner contributions** (code the owner writes when the slice starts): `LegendAssignment.ChooseFatherFallback` (§4.1) and `TemptationMath.DiscoveryChance` (§6.2).
