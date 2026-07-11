# High School Onboarding Events — Design Contract

**Author:** Opus 4.8 (architecture) · **Date:** 2026-07-10 · **Arc:** Act 1 fix (HS onboarding + rookie-batch tier gate)
**Status:** DESIGN COMPLETE — this is the authoring contract Sonnet 5 builds Stage 2 against. No code here.
**Plan of record:** `C:\Users\DELL\.claude\plans\let-s-look-at-act-jazzy-kay.md`
**Implements:** `docs/game-idas/HIGH_SCHOOL.md` Epic 5 ("Narrative Event Generator / Tutorial"), previously a one-line stub.

---

## 0. The problem this fixes

The demo's opening impression is broken two ways:

1. **No onboarding beat exists.** A fresh age-16 freshman gets *zero* first-day / tryouts / meet-the-coach content. Epic 5 was never designed.
2. **The wrong content fires instead.** `rookie_season_events.json` was authored for the old age-19 pro start. Its whole 12-event cascade hangs off `rookie_settled`, minted by `clubhouse_welcome` whose only prerequisite is `{ "flag_inactive": "rookie_settled" }` (weight 0.3/day from day 2). So a 16-year-old gets *"Welcome to the show, kid,"* rookie karaoke, a first pro paycheck, and motel road trips.

**The fix (this arc):** author a first-semester onboarding arc of **11 events** (week-one beats + a semester spine), and gate the entire rookie batch behind a new `tier` prerequisite so pro content can never fire at HS/College tier. A second new field, `gpa`, powers the report-card beat's honor-roll vs. slipping split.

**Calibration note:** no `Assets/Simulation/` line changes anywhere in this arc. The two new `SubjectField`s and the poll-SQL joins live in `Assets/Data/` (which MonteCarloHarness compiles but does not exercise for sim output) — **no band moves, no golden reset, no re-band.** MLB bit-identity must stay byte-exact through Stage 2.

---

## 1. Engine additions (the exact contract for Sonnet)

Two new `SubjectField` prerequisite fields, both following the shipped **Strictness** / **TeammateExOfPartner** precedent (poll LEFT JOIN, COALESCE to a neutral default, evaluator case, JSON name map). **No schema change** — `Team_Tiers` exists since v7, `Player_Person` since v11.

### 1.1 `tier` — the league-tier gate (load-bearing)

The whole arc turns on this field. It answers "what league tier does this subject's team play in," 0–5, so content can gate HS-only or pro-only.

- **JSON name:** `"tier"`. Enum: `SubjectField.Tier` appended after `TeammateExOfPartner` in `GrittyEventModel.cs`; name-map entry `"tier" => SubjectField.Tier` in `GrittyEventJson.cs`; evaluator case `SubjectField.Tier => subject.Tier` in `ConditionEvaluator.cs`.
- **Values = `LeagueTier` ordinals** (`BaseballDtos.cs`): `HS=0, College=1, MinorA=2, MinorAA=3, MinorAAA=4, MLB=5`.
- **`PollPlayerRow.Tier` (int)**, read at column index 10 (appended after the existing `TeammateExOfPartner` at index 9).
- **Poll SQL** — add `LEFT JOIN Team_Tiers tt ON tt.team_id = p.team_id` and this column expression:

  ```sql
  CASE WHEN p.team_id IS NULL THEN -1 ELSE COALESCE(tt.tier, 5) END
  ```

  **The `-1` sentinel is the load-bearing safety.** `TryGetTeamTier` COALESCEs a *missing tier row* to MLB(5) (the v6→v7 backfill convention), but unrostered players — the two seeded parent NPCs, a benched/displaced teammate — have **NULL `team_id`**. Without the `CASE`, they would read as MLB(5) and a `scope:any` event with `tier >= 2` would fire *on the avatar's parents*. `-1` matches no `>=`/`==`/`<` gate any content will ever write (all real gates are 0–5), so unrostered subjects are invisible to every tier prerequisite. **This is the single most important line for Fable to verify in Stage 3.**

- **MCP-validate before writing C#** (database_rules, No Blind Queries): confirm the `Team_Tiers` LEFT JOIN shape against the live save via the sqlite MCP — same discipline as the v13 CTE and the HS-5 strictness join.

### 1.2 `gpa` — the report-card branch gate

Powers the honor-roll vs. slipping split (§3, events 7–8). Same pattern, one LEFT JOIN.

- **JSON name:** `"gpa"`. Enum `SubjectField.Gpa`; name-map `"gpa" => SubjectField.Gpa`; evaluator `SubjectField.Gpa => subject.Gpa`.
- **`PollPlayerRow.Gpa` (double)**, read at column index 11 (`reader.GetDouble(11)`).
- **Poll SQL** — add `LEFT JOIN Player_Person pp ON pp.player_id = p.player_id` and column `COALESCE(pp.gpa, 2.5)`. `Player_Person` is backfilled for every player each boot, so a row effectively always exists; COALESCE to the schema default `2.5` defends the pre-v11-migrated edge and keeps row shape total (the Strictness precedent exactly).
- Value range 0.0–4.0. The two report-card variants partition at **2.5** (`>= 2.5` vs `< 2.5`) — see §3.

**Why add `gpa` at all (the plan's open decision, resolved):** ADD IT. The marginal cost is near-zero because Sonnet is already adding a `SubjectField` with a LEFT JOIN in the same pass — `gpa` is the identical shape, a second join on the same `p.player_id`. It buys genuinely different content (a proud-parent honor-roll beat vs. a coach-eligibility-warning beat) and makes the GPA the entire HS-4 person layer already computes **visible to the player in narrative for the first time**. It is also forward-useful (academic-probation / eligibility content the grade system already implies). The alternative — a single pacing-only report card — leaves `gpa` uncomputed-into-narrative and wastes the shipped person layer.

### 1.3 What does NOT change

No new `ConsequenceKind`, no new `RelationshipTargetSelector`, no schema DDL, no `EventScope` change, no dispatcher change. Onboarding uses only the existing consequence vocabulary: `set_flag`, `clear_flag` (unused here), `person_stat`, `stress`, `funds`, `relationship`. (`interest` is deliberately avoided — the avatar's `baseball_interest` starts at the 100 ceiling, so any interest delta clamps to a no-op, per the rookie batch's own disclosure.)

---

## 2. The onboarding arc at a glance

11 events, all `scope: "avatar"`, all carrying `{ "field": "tier", "op": "==", "value": 0 }` (HS-only — see §4.3 for why *every* event, not just the entry). File: `Assets/Narrative/Events/Content/hs_onboarding_events.json`.

| # | id | contact | weight | cooldown | gate (beyond `tier==0`) | sets | HIGH_SCHOOL ≥3 |
|---|---|---|---|---|---|---|---|
| **Week-one chain (days ~2–9, a beat a day)** ||||||||
| 1 | `hs_first_day` | family_mom | 1.0 | — | `!hs_started` | `hs_started` | 3 ✓ |
| 2 | `hs_meet_coach` | coach_hs | 1.0 | — | `hs_started` (min 1), `!hs_met_coach` | `hs_met_coach` | 3 ✓ |
| 3 | `hs_tryouts` | coach_hs | 1.0 | — | `hs_met_coach` (min 1), `!hs_made_team` | `hs_made_team` | 3 ✓ |
| 4 | `hs_first_practice` | coach_hs | 1.0 | — | `hs_made_team` (min 1), `!hs_practiced` | `hs_practiced` | 3 ✓ |
| 5 | `hs_first_game_nerves` | coach_hs | 1.0 | — | `hs_practiced` (min 2), `!hs_debut_done` | `hs_debut_done` | 3 ✓ |
| 6 | `hs_lunchroom` | hs_friend | 1.0 | — | `hs_started` (min 2), `!hs_found_crew` | `hs_found_crew` | 3 ✓ |
| **Semester spine (weeks ~3–8)** ||||||||
| 7 | `hs_first_report_card` | family_mom | 1.0 | — | `hs_debut_done` (min 21), `!got_report_card`, **`gpa >= 2.5`** | `got_report_card` | 3 ✓ |
| 8 | `hs_report_card_slipping` | coach_hs | 1.0 | — | `hs_debut_done` (min 21), `!got_report_card`, **`gpa < 2.5`** | `got_report_card` | 3 ✓ |
| 9 | `hs_coach_checkin` | coach_hs | 0.15 | 30 | `hs_made_team` (recurring — no one-shot flag) | — | 3 ✓ |
| 10 | `hs_crosstown_rival_seed` | hs_friend | 0.5 | — | `hs_debut_done` (min 14), `!hs_rival_seeded` | `hs_rival_seeded` | 3 ✓ |
| 11 | `hs_homecoming` | hs_friend | 0.4 | — | `hs_made_team` (min 45), `!hs_homecoming_done` | `hs_homecoming_done` | 3 ✓ |

**Recommended file order = table order** (the mandatory week-one chain first, then the parallel social seed, then the spine). Order only matters when two events are simultaneously eligible for the one-fire-per-subject-per-day slot; listing the chain first keeps week one on-spine. "min N" = `min_days_since` on that `flag_active`.

Events 7 and 8 **partition on `gpa`** (`>= 2.5` vs `< 2.5`) and share `flag_inactive: got_report_card`, so **exactly one fires** per avatar per HS career (the OR-is-two-events idiom). A fresh avatar at the default gpa 2.5 lands on the *solid* branch (`>= 2.5`); only a player who has actively let their gpa slip below 2.5 (truancy/hustles) gets the coach's eligibility warning. Both set `got_report_card`, closing the beat.

---

## 3. Beat-by-beat authoring spec

Near-final prompts and labels below (Sonnet may polish copy; the **mechanics — flags, consequences, weights, autopilot weights — are the contract**). Every choice sets its beat's one-shot flag where applicable, so the beat completes regardless of choice (the `clubhouse_welcome` "both choices set `rookie_settled`" precedent). Person-stat targets are drawn from the twelve `PersonStatId` columns; deltas are small (±1…±6, atomic-clamped). GPA is never a consequence (deliberately unreachable — it moves only through the weekly closed form).

### 1 · `hs_first_day` — ENTRY
- **contact** family_mom · **weight** 1.0 · **prereq** `tier==0`, `!hs_started`
- **prompt:** "First bell of freshman year. New building, a locker that won't open, a schedule you're pretty sure is wrong. Mom texts: *'Knock 'em dead today, kiddo.'*"
- **choices (all set `hs_started`):**
  1. "Walk in like you own the place" — aw 2 — confidence +3, social_status +2
  2. "Head down, find your classes" — aw 2 — discipline +2
  3. "Ask her to pick you up early" — aw 1 — stress +4, maturity −1

### 2 · `hs_meet_coach`
- **contact** coach_hs · **weight** 1.0 · **prereq** `tier==0`, `hs_started` (min 1), `!hs_met_coach`
- **prompt:** "Coach Malone stops you in the hall. *'You're the freshman who can supposedly hit. Tryouts are Thursday. Don't make me regret knowing your name.'*"
- **choices (all set `hs_met_coach`):**
  1. "Promise you'll be there early" — aw 2 — confidence +2, work_ethic +2
  2. "Play it humble" — aw 2 — maturity +2
  3. "Tell him he won't regret it" — aw 1 — confidence +3, stress +2

### 3 · `hs_tryouts`
- **contact** coach_hs · **weight** 1.0 · **prereq** `tier==0`, `hs_met_coach` (min 1), `!hs_made_team`
- **prompt:** "Tryouts. Forty kids, fifteen spots, a coach with a clipboard who hasn't smiled once. Your name gets called for the first round."
- **choices (all set `hs_made_team`):**
  1. "Leave it all on the field" — aw 2 — confidence +3, work_ethic +2, stress +5
  2. "Play smart, don't force it" — aw 2 — discipline +3
  3. "Hype up the other freshmen to settle your nerves" — aw 1 — teamwork +3, relationship friend +8 → teammate

### 4 · `hs_first_practice`
- **contact** coach_hs · **weight** 1.0 · **prereq** `tier==0`, `hs_made_team` (min 1), `!hs_practiced`
- **prompt:** "You made it. First official practice — Coach Malone runs the team into the ground before anyone touches a ball. Around the tenth sprint, a few guys glance over to see if you'll break."
- **choices (all set `hs_practiced`):**
  1. "Set the pace, don't break" — aw 2 — teamwork +4, confidence +2, stress +4
  2. "Just survive it" — aw 2 — discipline +2
  3. "Crack a joke to loosen everyone up" — aw 1 — charisma +3, relationship friend +10 → teammate

### 5 · `hs_first_game_nerves`
- **contact** coach_hs · **weight** 1.0 · **prereq** `tier==0`, `hs_practiced` (min 2), `!hs_debut_done`
- **prompt:** "First game tomorrow. You're in the lineup. Tonight, staring at the ceiling, every version plays out — the ones where you're a hero and the ones where you're not."
- **choices (all set `hs_debut_done`):**
  1. "Visualize the good version until you fall asleep" — aw 2 — confidence +4, stress −5
  2. "Get up and take fifty swings in the garage" — aw 1 — work_ethic +3, stress +2
  3. "Text a teammate who's just as nervous" — aw 2 — teamwork +3, relationship friend +8 → teammate, stress −3

### 6 · `hs_lunchroom` — social seed (parallel, off `hs_started`)
- **contact** hs_friend · **weight** 1.0 · **prereq** `tier==0`, `hs_started` (min 2), `!hs_found_crew`
- **prompt:** "Lunch, day three. Tray in hand, nowhere obvious to sit. A table of kids from your grade clocks you standing there — one waves you over before it gets awkward."
- **choices (all set `hs_found_crew`):**
  1. "Take the seat" — aw 2 — social_status +3, relationship friend +8 → teammate, stress −3
  2. "Sit with the team instead" — aw 2 — teamwork +3, relationship friend +6 → teammate
  3. "Eat alone and scout the room" — aw 1 — intelligence +2, social_status −1

### 7 · `hs_first_report_card` — solid (`gpa >= 2.5`)
- **contact** family_mom · **weight** 1.0 · **prereq** `tier==0`, `hs_debut_done` (min 21), `!got_report_card`, `gpa >= 2.5`
- **prompt:** "First report card of high school hits the mailbox. Mom reads it twice and sticks it on the fridge before you're even home. *'We are SO proud of you.'*"
- **choices (all set `got_report_card`):**
  1. "Let yourself enjoy it" — aw 2 — happiness +4, confidence +2
  2. "Set your sights higher" — aw 1 — discipline +3, work_ethic +2
  3. "Downplay it, keep grinding" — aw 1 — maturity +2

### 8 · `hs_report_card_slipping` — slipping (`gpa < 2.5`)
- **contact** coach_hs · **weight** 1.0 · **prereq** `tier==0`, `hs_debut_done` (min 21), `!got_report_card`, `gpa < 2.5`
- **prompt:** "Coach Malone drops a printout on the bench next to you — your grades. *'No pass, no play. You want to keep that uniform, you fix this. I'm not asking.'*"
- **choices (all set `got_report_card`):**
  1. "Commit to turning it around" — aw 2 — discipline +4, work_ethic +3, stress +4
  2. "Ask a classmate for tutoring" — aw 1 — intelligence +2, relationship friend +6 → teammate, social_status −1
  3. "Argue grades don't matter if you can play" — aw 1 — maturity −3, stress +6

### 9 · `hs_coach_checkin` — recurring texture
- **contact** coach_hs · **weight** 0.15 · **cooldown** 30 · **prereq** `tier==0`, `hs_made_team` (NO one-shot flag — repeats every ~30 days)
- **prompt:** "Coach Malone catches you after the bell. *'How you holding up, kid? Season's long. School, ball, home — a lot to carry at your age.'*"
- **choices (no flag):**
  1. "Tell him the truth" — aw 2 — stress −6, maturity +1
  2. "Say you're fine, keep moving" — aw 2 — discipline +1
  3. "Ask him for extra reps" — aw 1 — work_ethic +2, stress +2

### 10 · `hs_crosstown_rival_seed`
- **contact** hs_friend · **weight** 0.5 · **prereq** `tier==0`, `hs_debut_done` (min 14), `!hs_rival_seeded`
- **prompt:** "Your buddy shoves a phone in your face — a clip of the shortstop from Westbrook, the crosstown school, running his mouth about your team. *'He said your name, man. By name.'*"
- **choices (all set `hs_rival_seeded`):**
  1. "Let your game do the talking" — aw 2 — discipline +3, relationship rival −15 → opponent
  2. "Fire back online" — aw 1 — confidence +2, stress +3, relationship rival −25 → opponent
  3. "Laugh it off, screenshot it for later" — aw 1 — charisma +2, relationship rival −8 → opponent
- **Note on the `opponent` target:** best-effort. If the `opponent` selector's pool is empty at HS tier (no scheduled opposing player resolvable), the relationship consequence is a **skip-by-design no-op** (the existing empty-pool precedent) — the `set_flag hs_rival_seeded` is the durable outcome, so the beat always completes. Sonnet: if `opponent` resolution proves unavailable at HS tier during the harness pass, drop the edge and keep the flag; do **not** add a new selector.

### 11 · `hs_homecoming`
- **contact** hs_friend · **weight** 0.4 · **prereq** `tier==0`, `hs_made_team` (min 45), `!hs_homecoming_done`
- **prompt:** "Homecoming's this weekend. Half the school's going, tickets are twenty bucks, and your crew keeps asking if you're in. First real 'everyone's there' night of the year."
- **choices (all set `hs_homecoming_done`):**
  1. "Buy a ticket, go all in" — aw 2 — funds −20, social_status +4, happiness +3, relationship friend +8 → teammate
  2. "Go, keep it low-key" — aw 2 — funds −20, happiness +2
  3. "Skip it, rest for the game" — aw 1 — discipline +2, social_status −2

---

## 4. Flag graph, closure & pacing

### 4.1 The chain

```
 (creation seeds NO narrative flags)
        │
        ▼  tier==0, !hs_started
   hs_first_day ──sets──▶ hs_started
        ├──(min 1)──▶ hs_meet_coach ──▶ hs_met_coach
        │                   └──(min 1)──▶ hs_tryouts ──▶ hs_made_team
        │                        ├──(min 1)──▶ hs_first_practice ──▶ hs_practiced
        │                        │                 └──(min 2)──▶ hs_first_game_nerves ──▶ hs_debut_done
        │                        ├────────────────────────────────────▶ hs_coach_checkin (recurring, cd 30)
        │                        └──(min 45)──▶ hs_homecoming ──▶ hs_homecoming_done
        └──(min 2)──▶ hs_lunchroom ──▶ hs_found_crew

   hs_debut_done ──(min 21)──▶ hs_first_report_card [gpa≥2.5] ──▶ got_report_card
                 └─(min 21)──▶ hs_report_card_slipping [gpa<2.5] ─▶ got_report_card   (mutually exclusive on gpa)
                 └─(min 14)──▶ hs_crosstown_rival_seed ──▶ hs_rival_seeded
```

### 4.2 Closure analysis (`check_event_graph_integrity` #1–#6 pre-clearance)

- **Every `flag_active` read is set by an onboarding event:** `hs_started`←1, `hs_met_coach`←2, `hs_made_team`←3, `hs_practiced`←4, `hs_debut_done`←5. No dangling reads. ✓
- **No orphan set_flags:** each of `hs_started / hs_met_coach / hs_made_team / hs_practiced / hs_debut_done` is read by ≥1 downstream `flag_active`. The one-shot guards (`hs_found_crew`, `got_report_card`, `hs_rival_seeded`, `hs_homecoming_done`) are read **self-referentially** as `flag_inactive` on their own event — the documented once-ever idiom (rookie batch header precedent), not an orphan. ✓
- **≥3 choices:** all 11 events carry exactly 3 (HIGH_SCHOOL.md §2). ✓
- **Scope (#6):** all 11 are `scope: "avatar"` → satisfied by scope. ✓
- **Age/tier reachability (#5):** entry gates `tier==0`, which a fresh HS avatar satisfies and a pro avatar never does; the chain is reachable and terminates (no infinite non-cooldown loop — `hs_coach_checkin` is the only repeater and it is cooldown-paced). ✓
- **No dead ends:** every one-shot event's flag opens ≥1 successor or is a terminal spine beat; the arc reaches `got_report_card` / `hs_homecoming_done` and stops. ✓

Sonnet must additionally **refresh the skill's stale field-vocabulary line** (`.claude/skills/check_event_graph_integrity/SKILL.md` is missing strictness / teammate_ex_of_partner / person_stat / child_development / rekindle) and **add `tier` and `gpa`** while there.

### 4.3 Why `tier==0` on *every* event, not just the entry

The shipped HS idiom (`hs_dating`) gates the field condition only on the entry and lets the chain inherit via flags. This arc **deliberately deviates** — `tier==0` on all 11 — for two reasons, mirroring the rookie batch's own "gate all, not just entry" call:

1. **The live save is already flag-contaminated.** The day-99 dev avatar has almost certainly fired `clubhouse_welcome` (0.3/day over ~97 evaluated days), so `rookie_settled` is likely active there. The onboarding chain flags (`hs_started` etc.) are *not* yet set on that save, so onboarding will retroactively start on next boot (desired). Gating every event on `tier==0` guarantees that once an avatar graduates HS→College, **no lingering onboarding spine beat can fire in the College years** even if a `min_days_since` window happens to open post-graduation. It self-gates to the HS tier, period.
2. **Bulletproofing the succession path.** A gen-2 heir (§6) gets its own flags but also its own tier; `tier==0` keeps the redundant guard honest with zero reasoning about flag lifetimes across tiers.

The cost is one extra prerequisite leaf per event — negligible, and consistent with the rookie batch's mirror-image decision.

### 4.4 Pacing feel

Week-one beats are **weight 1.0** → each fires the first evaluated day its `min_days_since` window opens (subject to the one-fire-per-day cap and §5). With the min-days chain, week one delivers roughly one beat per day across days ~2–9. The spine tapers: the report card at ~debut+21 (~day 28), the rival seed within a few days of debut+14, homecoming around made-team+45, and `hs_coach_checkin` as a low-weight recurring pulse thereafter. All weights/cooldowns/min-days are **first-pass, tunable as pure data edits** (§7).

---

## 5. Day-2 competition audit (plan item 4)

**Question:** on day 2 of a fresh save (first evaluated day — day 1 is recorded, never evaluated), can anything steal the one-fire-per-subject slot from `hs_first_day`?

**Dispatcher mechanics (verified in `EventDispatcher.EvaluateDay`):** events are iterated in library (= file load) order; for each, scope → cooldown → prerequisites → an **independent weight roll** (`_rng.NextDouble() >= weight` ⇒ *continue*, not stop). A satisfied-but-lost event does **not** block later events. Only a satisfied-AND-won event fires and `break`s. So a **weight-1.0** event fires the moment it's reached, unless an *earlier-in-order* event both holds and wins its own roll first.

**Audit** — for a fresh 16-year-old (funds 50–5000, recklessness 0, detection_risk 0, health_ceiling 100, baseball_interest 100, age 16, gpa 2.5, no flags):

| Batch | Why every event fails on day 2 |
|---|---|
| core_events | `recklessness>75`/`>60`, `age<16` (heir catch), or `flag_active` — all fail |
| career_arc | `detection_risk>70`, `health_ceiling<40`, `age>=19`/`>=21`, `age<16`, or `flag_active` — all fail |
| marriage_and_family | `age>20` or `flag_active married`/`expecting`/`marriage_strained` — all fail |
| roster_availability | `detection_risk>=60/80/90`, `health_ceiling<50`, or `flag_active` — all fail |
| child_rearing | `flag_active hs_has_child`/`hs_child_neglect_pattern` — all fail |
| rookie_season | **`tier>=2` (this arc)** vs. avatar tier 0 — all fail |
| **hs_dating** | **`hs_crush_forms` HOLDS** (`age<20` ✓, `!hs_dating`/`!hs_interest`/`!married` ✓). Weight **0.05**. The one competitor. |

**Load order** is alphabetical (`DirAccess.GetFiles()` returns sorted; `LoadGrittyEventContent` does not re-sort), so `hs_dating_events.json` loads **before** `hs_onboarding_events.json` — `hs_crush_forms` is evaluated *before* `hs_first_day` every day.

**Residual slip (stated, not eliminated):** each day of week one, `hs_crush_forms` rolls first at weight 0.05. ~95%/day it loses and the dispatcher falls through to the weight-1.0 onboarding beat, which then fires. ~5%/day it wins and pre-empts that day's onboarding beat, firing a "someone caught your eye in the cafeteria" text instead; the displaced onboarding beat simply fires the next evaluated day. Over a ~7-day week-one window the chance *at least one* crush beat interleaves is ≈ 1 − 0.95⁷ ≈ **30%**.

**This is acceptable, not a bug.** Romance and onboarding are not mutually exclusive; a single crush beat sprinkled into week one is realistic texture and self-heals (no onboarding beat is ever *dropped*, only occasionally time-shifted by a day). It is categorically different from the pre-fix breakage (pro paychecks at 16). **Recommendation: keep `hs_first_day` at weight 1.0** so onboarding beats are never themselves lost.

**Optional polish (user's call — NOT mandated by this arc):** to make week one 100% onboarding-only, add `{ "flag_active": "hs_debut_done" }` to `hs_crush_forms`'s prerequisites so the romance funnel cannot open until the week-one arc completes (thematically: you settle into school before the crush subplot begins). Caveats Sonnet must weigh before doing this:
- It edits **shipped, already-reviewed HS-5 content** (`hs_dating_events.json`).
- It **ripples into the GrittyEventsHarness** — any existing `World` check that exercises the dating funnel from a bare world must first set `hs_debut_done`, or it will stop firing.
- **Migration-safe:** the live day-99 avatar has no `hs_debut_done`, but it also has no `hs_started`, so it re-runs the onboarding chain on next boot and reaches `hs_debut_done` normally — romance is delayed, never blocked. If the live avatar is already `hs_dating`/`hs_interest`, the gate is moot.

Given the ripple into shipped content and its harness, this polish is **left out of the core arc** and flagged for the user to request explicitly. The core deliverable (onboarding fires, rookie content blocked) is complete without it.

---

## 6. Rookie batch re-gate (plan item 5)

Edit `rookie_season_events.json`:

1. **Add `{ "field": "tier", "op": ">=", "value": 2 }` to the `prerequisites` array of ALL 12 events** — `clubhouse_welcome`, `playbook_hazing`, `homesick`, `first_paycheck`, `splurge_callback`, `frugal_callback`, `coach_pep_talk`, `in_a_slump`, `on_a_heater`, `clubhouse_prank`, `road_roommate`, `rookie_advice_veteran`. Every event, not just the `rookie_settled` entry — the live save is likely `rookie_settled`-contaminated, and self-gating every event heals it with zero DB surgery (a contaminated avatar at tier 0/1 simply never satisfies any rookie event again).
2. **`tier >= 2` = MinorA and above** (pro ball). HS(0) and College(1) are excluded — correct: this batch is professional minor-league life ("first *real* paycheck," motel road trips, clubhouse veterans). Amateur HS/College players never see it. The avatar reaches tier 2 only after graduating HS→College→signing a pro deal.
3. **Rewrite the batch header comment:** drop the `StartingAge 19` premise (stale — the avatar now starts at 16). State the new gate: *"Every event requires `tier >= 2` (MinorA+), so this professional-clubhouse content is unreachable at HS (0) or College (1) tier regardless of `rookie_settled` — the fix for a 16-year-old freshman receiving pro-onboarding content. `rookie_settled` remains the intra-batch entry gate, now AND-ed with the tier floor."*

This clears `check_event_graph_integrity` #5 (age/tier reachability), which the rookie batch currently violates (its content is reachable by a fresh flagless avatar with no tier/age floor).

---

## 7. Contacts (plan / registry)

Add to `Assets/Narrative/Contacts/contacts.json` (`RunContactRegistryChecks` pins `unresolved == 0`, so every `contact` id referenced by content must exist):

| id | display_name | role | carries |
|---|---|---|---|
| `coach_hs` | Coach Malone | coach | events 2,3,4,5,8,9 — the HS varsity coach. **Distinct from `coach_reyes`** (the pro clubhouse coach) exactly as `hs_sweetheart` is kept distinct from `girlfriend_jess`, so the HS and pro arcs never share a thread. |
| `hs_friend` | Marcus | friend | events 6,10,11 — a classmate/buddy; the peer voice for the social/rival/homecoming beats. |

Events 1 and 7 reuse existing `family_mom`. Update the registry header comment to note the two new HS-onboarding threads.

---

## 8. Constants summary (first-pass, tunable as data edits)

| Constant | Value | Rationale |
|---|---|---|
| Week-one beat weight | 1.0 | Guaranteed fire once the min-days window opens — the tutorial cadence; also wins over the 0.05 crush competitor. |
| Week-one `min_days_since` | 1 (chain), 2 (nerves, lunchroom) | ~One beat per day across days 2–9. |
| Report-card `min_days_since` | 21 (on `hs_debut_done`) | First report card ~3 weeks after the debut (~day 28). |
| Report-card gpa partition | 2.5 | The schema default — a neutral/unmoved gpa lands on the encouraging *solid* branch; only an actively-slipped gpa gets the coach warning. No dead zone, no overlap. |
| `hs_coach_checkin` weight / cooldown | 0.15 / 30 | Low-frequency recurring pulse. |
| Rival seed weight / min-days | 0.5 / 14 | Lands within a few days of debut+14. |
| Homecoming weight / min-days | 0.4 / 45 | A ~1.5-month-in social milestone. |
| person_stat deltas | ±1…±6 | Small nudges; atomic-clamped 0–100. |
| Rookie tier floor | `tier >= 2` | MinorA+; excludes HS/College. |

All of the above move via pure JSON edits + a mandatory harness re-run — no sim touch, so no re-band.

---

## 9. Verification & checklist deltas (plan item 6)

### 9.1 Harness contract for Sonnet (Stage 2)

- Bump both hardcoded event-count literals from **55 → 66** (11 new events).
- `TryGetById` pins for all 11 new ids + confirm the 12 rookie ids still resolve (now tier-gated).
- Loader **accept** checks for `"tier"` and `"gpa"` fields; **reject** check for an unknown field name still throwing `FormatException`.
- `ConditionEvaluator` unit checks: a `tier==0` subject vs `tier>=2`; NULL-team → −1 sentinel matches no gate; `gpa` `>=`/`<` around 2.5.
- `World` integration checks (the load-bearing regression guards):
  - (a) **fresh age-16 HS world** — `hs_first_day` fires on day 2 and the chain progresses on pacing (deterministic-pacing proof).
  - (b) **regression guard** — fresh HS world: `clubhouse_welcome` (and any rookie event) **never fires** across N days.
  - (c) **positive control** — a `tier>=2` (pro) world: `clubhouse_welcome` **still fires**.
  - (d) **contaminated-save guard** — HS world with `rookie_settled` pre-set: rookie chain events still **never fire** (tier floor holds).
  - (e) **NULL-team subject** (unrostered parent) never matches a tier gate.
- Gates: `dotnet build` 0/0; GrittyEvents 210 → new total, exit 0; `run_monte_carlo_batch` **345/345 with MLB bit-identity byte-exact** (the poll SQL lives in Data which MC compiles — confirm unchanged output); CoreLoop 22/22; NeedsDecay 94/94; SchemaValidator 111/111 (no schema touch); live godot-MCP boot on the real save — "gritty events 66" logged, errors empty, clean stop. Sqlite-MCP spot-check the live save's `Entity_Flags` for `rookie_settled` (report the finding to confirm the contaminated-save guard matters; **no DB surgery**).

### 9.2 Playtest checklist deltas (`docs/hs_manual_playtest_checklist.md`)

- **Sessions A/B — replace "expect zero week-1 events" with:** on a fresh save, days 2–14 deliver the onboarding beats on the BurnerPhone in roughly chain order — first day (Mom) → meet coach → tryouts → first practice → lunchroom (a classmate) → first-game nerves. Roughly one beat per day, tapering after ~day 9.
- **New hard-fail criterion:** **no rookie content may ever appear at HS tier** — no "Welcome to the show," no first pro paycheck, no motel/road-trip beats. Any such appearance is a defect (the tier gate failed).
- **Report card ~day 28:** a *solid* report card from Mom (gpa ≥ 2.5, the default/typical case) **or** a *slipping* eligibility warning from Coach Malone (gpa < 2.5, only if the player has tanked their gpa).
- **gen-2 refire note:** after `Succeed` creates a new age-16 heir, `Entity_Flags` are per-player, so the heir's `hs_started` etc. are unset — **the heir gets its own onboarding arc from scratch.** This is desired: every new bloodline re-lives the first day of high school.
- Fix the stale header gate-count (currently says 206/206 → the new GrittyEvents total).

---

## 10. Model handoff

- **Stage 2 → Sonnet 5:** build against this contract — the two `SubjectField`s + poll joins (MCP-validate first), `hs_onboarding_events.json`, the rookie re-gate + header rewrite, the two contacts, the harness suite, the skill/ checklist/ progress.md docs, all gates. Direct precedent: the v13 `TeammateExOfPartner` field pass (harder than this) + three prior content batches.
- **Stage 3 → Fable 5:** standing review — line-by-line on the NULL-team `−1` sentinel (§1.1), read-only-view discipline, that no `scope:any` event can now reach unrostered parents via tier, and the content vs. this contract (≥3 choices, flag closure §4.2, integrity #1–6); independent gate re-run incl. MC ×2; sign-off + memory.
