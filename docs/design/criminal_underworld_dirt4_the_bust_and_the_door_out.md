# Design — Criminal Underworld Content Arc: `DIRT-4` "The Bust & The Door Out"

**Author:** Claude Opus 4.8 (narrative architecture + the engine hook) · **Slice:** Content arc, batch 4 of 4 — **the finale**. Companion to `docs/design/criminal_underworld_content_arc.md` (the arc contract + `DIRT-1`), `docs/design/criminal_underworld_dirt2_the_fix.md` (`DIRT-2`), `docs/design/criminal_underworld_dirt3_product_and_turf.md` (`DIRT-3`) — read all three first — and to `docs/design/promotion_advancement_gates.md` (the ladder this batch finally lets a record bite). Obeys `docs/design/gritty_event_framework.md` (the closed content vocabulary) for its narrative half.

This is the promotion of the arc doc's §8 `DIRT-4` outline into a full buildable spec, and it is **different in kind from every batch before it.** `DIRT-1..3` were *calibration-inert content* — they touched zero `Assets/Simulation/` files by construction. `DIRT-4` is the batch where the user chose to finally make the arc's thesis *mechanically* true: **crime can take the diamonds away — permanently.** So it has two halves that ship together:

- **A narrative half** (Sonnet, calibration-inert): the pro-scale bust and the plea that reads `DIRT-2`'s `cooperated_with_league` hook, the record that follows you, and clean-break-at-scale.
- **An engine half** (Fable, calibration-*touching*): `PromotionManager` reads the avatar's criminal record and gates the climb — a lifetime ban ends the avatar's advancement; a rap sheet docks it. This is the first time the arc writes a line of `Assets/Simulation/`, so it is **not** calibration-inert, and the MLB Monte-Carlo guard re-runs.

Both halves were explicitly chosen by the user for the finale ("fold engine into DIRT-4"). The engine half is small and surgical by design — a two-flag read in one already-wired code path — but it earns its own gate discipline (§7.B).

**A correction this pass established that overrides the arc docs — read it before anything else (§2.1).** The `detection_risk`/`ped_active` "shared heat meter" the arc kept deferring to `DIRT-4` **cannot be built the way the earlier docs assumed**, because `ped_active` has no gameplay writer. The user's ruling: **leave the shipped PED events on `detection_risk`; do not re-gate them.** That decision and its evidence are §2.1.

---

## 1. Design intent — what the finale should feel like

- **The record is the point.** `DIRT-1` cost you money and a couple of arrest days. `DIRT-2` cost you a season and a stain the engine couldn't yet read. `DIRT-3` cost you a benching. `DIRT-4` is where the cost becomes *permanent and mechanical*: the rap sheet you built across the whole arc now sits between you and the call-up. A GM passes. The ladder stops moving for you. That is the diamonds finally, structurally, out of reach — not a lost season, a lost *ceiling*.
- **Two weights of mark, two weights of teeth.** A common arrest (`record_arrest`) is a dock — scouts knock your makeup, you can still climb if you outplay it. A lifetime ban (`banned_for_life`, from throwing whole games in `DIRT-2`) is terminal — you keep your locker, but you never get promoted again. The severity of the teeth matches the severity of the act, exactly as the arrest-vs-ban split in the content always implied.
- **The plea rewards the informant.** `DIRT-2` promised that cooperating with the league buys a better deal (`cooperated_with_league`, its disclosed forward hook). `DIRT-4` is where that promise pays: the pro-scale indictment reads the flag and offers materially lighter terms to the player who turned before the case landed. Snitching cost you the syndicate's wrath then; it saves you real games now.
- **There is a door out — the most expensive one in the arc — and it does not erase the record.** Going straight at pro scale clears `in_the_life` (and, at a steep price, can buy out the syndicate marker itself). But it **never** clears `record_arrest` or `banned_for_life`. You can stop being a criminal; you cannot stop *having been* one. The mark stays, the engine keeps reading it, and that honesty is the whole arc's closing note — and the natural pivot into the heir generation (`dirt_blacklisted` already gestures at it).

---

## 2. What already exists (converge, do not collide) — verified against shipped code this pass

`DIRT-4` is a **convergence-and-consolidation batch**: it converges on the shipped record flag, the promotion ladder, and `DIRT-2`'s cooperate hook, and it *consolidates* the arc's scattered arrests so one flag means "has a record." Grounded live this pass:

| Shipped surface | Where (verified) | `DIRT-4` uses it as |
|---|---|---|
| **`record_arrest`** — the arc's record flag | Set by `narcotics_arrest` (`roster_availability_events.json:25`); **read by NOTHING today** — a planted orphan ("set a record flag for future cascades," `roster_availability_triad.md`) | **The canonical record.** `DIRT-4` is its **first reader** (engine + `dirt_the_record_follows`) and **consolidates its writers** (§4.1). |
| **`banned_for_life`** — the terminal mark | Set by `dirt_permanent_ban` (`dirt_underworld_events.json:560`), read by `dirt_blacklisted` (`:579`), **never cleared** (`DIRT-2` §4.4) | The **hard** promotion block (engine §5.B). Still terminal; `DIRT-4` never clears it. |
| **`cooperated_with_league`** — the informant hook | Set by `dirt_turn_informant_a/b` (`:620`, `:654`), **read by nothing** (`DIRT-2`'s disclosed `DIRT-4` forward hook) | **The plea-quality branch** — `dirt_the_indictment_a` gates on it for the better deal (§5.A). Fulfils the hook. |
| **`in_the_life`** — the arc spine | Set across `DIRT-1/2/3` | Entry gate for the pro-scale bust and the door-out; cleared by clean-break-at-scale. |
| **`compromised_syndicate`** — the marker | `back_alley_bribe`, `DIRT-1` `dirt_debt_sold`, `DIRT-2` `dirt_made_useful`; feeds the +365 `syndicate_shakedown` | Optionally **cleared** by the expensive full-buyout choice (the one disclosed content clear of the syndicate flag — §5.C, disclose). |
| **`{"type":"absence","reason":"arrest"/"suspension","days":N}`** | roster triad; `DIRT-1` `dirt_busted`; `DIRT-3` `dirt_robbery_fallout`; `DIRT-2` `dirt_league_investigation` | The pro-scale legal-jeopardy teeth. Benches by games missed (`until_day = fire_day + days + 1`); overlaps don't stack. |
| **`PromotionManager` avatar gate** — the climb | `PromotionManager.cs` (avatar eligibility ~`:487–:643`); reads flags via `BaseballQueries.LoadActiveFlagPlayerIds` (`:902`, indexed, already a ctor dependency) | **The engine teeth.** The avatar's promote/skip decision reads `record_arrest` + `banned_for_life` (§5, §8). |

**The deferred hooks this batch closes.** `DIRT-2` §8.2 parked *"`cooperated_with_league` is a forward hook for DIRT-4 (the plea-at-scale gives cooperators a better deal)"* and §8.1 parked *"wiring `banned_for_life` into `CareerManager`… a future Simulation/Baseball engine task."* `DIRT-4` closes **both**: the cooperate hook via `dirt_the_indictment_a`, and the ban-teeth via the `PromotionManager` read (the engine half). Call both out in the batch header as the seams being closed.

### 2.1 The PED / `detection_risk` collision — corrected, and the user's ruling

The arc docs (§2 collision 1 in the arc doc, echoed in `DIRT-2`/`DIRT-3`) repeatedly deferred a *"re-gate `caught_juicing` on `ped_active` so crime and PEDs can share the field"* cleanup to `DIRT-4`. **That cleanup cannot be built as described, because the premise is false against shipped code.** Verified this pass with a repo-wide sweep for `ped_active` / `PedActiveFlagName`:

- **`ped_active` has no gameplay writer.** It is written `true` in exactly one place — `Tools/MonteCarloHarness/Program.cs:801`, a **test fixture**, cleared 23 lines later. No content event, and **not** `HustleService`, ever sets it. The narrative steroid arc only ever sets `steroid_scandal_public`.
- **Consequences the arc docs got backwards:** (a) the 1.5× PED power/stamina multiplier (`AtBatResolver`/`MicroGame`/`LeagueSimulator` read `ped_active`) is **dead in any real save** — it only fires in the harness; (b) **`detection_risk` is the only live PED-event trigger**, and the only thing that raises it in-game is the **hustle mini-games** (`DIRT-3` collision 3). So the "PED scandal" events (`caught_juicing` + the three `detection_risk` suspension tiers) are *already* reachable **only by hustle players**, never by actual juicers — because there is no juicer path at all. `ped_active` and the steroid content are two disconnected halves of an unfinished PED system.
- **Therefore the re-gate is a trap:** adding `flag_active: ped_active` to those four events would make them **never fire in the live game**, silently deleting the entire steroid arc.

**User ruling (locked): leave the shipped PED events on `detection_risk`; do NOT re-gate them.** `DIRT-4` keeps to boolean crime flags and touches none of the four PED events' *gates*. Two allowed, optional consequences:

1. **Optional prose re-theme (Sonnet's craft, no gating change).** Since those events today only ever fire off hustle-driven `detection_risk`, their "steroid" framing is fictionally wrong. Sonnet *may* lightly re-theme the four events' `prompt`/`label` copy toward **general league drug-testing / heat** so the fiction matches what actually triggers them. This is a pure text edit — no prereq, consequence, weight, or flag change — and is calibration-inert. Disclose if done; leave the ids untouched.
2. **Disclosed deferred follow-on (not this batch):** finishing the PED system — a "start juicing" on-ramp that *sets* `ped_active` (bringing the dead 1.5× multiplier alive), after which the four events could safely re-gate on `ped_active` — is its own future arc, out of scope for the finale. Carry it in the header as a known gap, not a `DIRT-4` task.

**Net for `DIRT-4`: no PED-event gate changes. The heat conflation the arc kept deferring is resolved by *documentation and (optional) re-theme*, not by re-gating.**

---

## 3. Where `DIRT-4` sits in the spine

`DIRT-4` hangs off the deep-in spine (`in_the_life`, and `DIRT-2`'s `cooperated_with_league` / `banned_for_life`) and, uniquely, off the **record flag the engine now reads**.

```
   DIRT-1/2/3 deep-in outcomes:  in_the_life        cooperated_with_league   banned_for_life
   (all on-ramps set the spine)  compromised_syn.   (DIRT-2 informant)       (DIRT-2 threw a game)
                                       │                     │                      │
                        tier >= 2 ─────┤                     │ (better-deal branch) │
                                       ▼                     ▼                      │
                          ┌──────── THE PRO-SCALE BUST ───────────┐                 │
                          │ dirt_the_indictment_a (cooperated →   │                 │
                          │   lighter plea)   /  _b (didn't →      │                 │
                          │   harsher plea)                       │                 │
                          │  BOTH: arrest/suspension absence +     │                 │
                          │        set_flag record_arrest ────────┼──┐              │
                          └───────────────────────────────────────┘  │              │
                                                                      ▼              ▼
   CONSOLIDATION (content edits): DIRT-1 dirt_busted  +──────►  record_arrest   +  banned_for_life
                                  DIRT-3 dirt_robbery_fallout   (the canonical record the ENGINE reads)
                                  now ALSO set record_arrest          │                    │
                                  (narcotics_arrest already does)     ▼                    ▼
                                                        ╔══════════ ENGINE HALF (Fable) ══════════╗
                                                        ║ PromotionManager avatar gate reads both: ║
                                                        ║  record_arrest  → A-score dock (climb    ║
                                                        ║                   harder, not blocked)   ║
                                                        ║  banned_for_life → promotion INELIGIBLE  ║
                                                        ║                   (the climb ends)        ║
                                                        ╚═══════════════════════════════════════════╝
                                                                      │
                    narrative faces:  dirt_the_record_follows (reads record_arrest)
                                      dirt_blacklisted (shipped, reads banned_for_life)

   THE DOOR OUT (pro scale):  dirt_going_straight (in_the_life & tier>=2)
        clears in_the_life; optional expensive buyout clears compromised_syndicate;
        NEVER clears record_arrest / banned_for_life  → the mark stays, the engine keeps reading it
```

---

## 4. Authoring contract deltas (on top of arc doc §4 + DIRT-2/3 §4)

Everything in the arc doc §4 holds for the content half (closed vocabulary, `scope: avatar` for the descent, `interest` is a no-op on the avatar, flag hygiene, absence semantics). The engine half is governed by §7.B. `DIRT-4`-specific restatements:

1. **`record_arrest` is the canonical record — reuse it, do not mint a new flag.** It already ships (set by `narcotics_arrest`) and is read by nothing. `DIRT-4` makes it the one flag that means "has a criminal record," and **consolidates its writers** so the arc's arrests are consistent: add `set_flag record_arrest` to **`DIRT-1`'s `dirt_busted`** (both the plea and fight-it branches — an arrest is an arrest) and **`DIRT-3`'s `dirt_robbery_fallout`** (both branches), and to **both** `DIRT-4` indictment events. `narcotics_arrest` already sets it. After this, every arrest in the game leaves the mark the engine reads. These are shipped-content edits (two existing files), still calibration-inert (no `Assets/Simulation/` touch on the content side). Disclose the two edited events in the header.
2. **`record_arrest` and `banned_for_life` are terminal — never cleared by content.** The record follows you (§1). Do not author a clear for either. `banned_for_life` was already terminal (`DIRT-2` §4.4); `DIRT-4` makes `record_arrest` terminal too, by design and now with mechanical weight. (This is legal under check #5: an orphan *set* is forbidden, but both flags are *read* — by the engine and by `dirt_the_record_follows`/`dirt_blacklisted`.)
3. **The record teeth are AVATAR-ONLY (engine half).** `PromotionManager` reads the record only for the avatar, in the avatar-eligibility branch — **never** in the NPC set-based sweep. NPCs accumulate no criminal flags (these are `scope: avatar` content), and keeping the NPC sweep byte-identical is what preserves the conservation law + determinism harness (§7.B). Semantically correct (the player's story), mechanically essential (neutrality).
4. **`tier >= 2`, avatar scope, explicit floor** for the pro-scale beats (the indictment and the door-out) — same NULL-team sentinel-−1 immunity as `DIRT-2` (avatar scope + `>=` floor never leaks onto unrostered −1 subjects). `dirt_the_record_follows` gates on `record_arrest` (which a tier-0 `dirt_busted` can set), so it is **not** tier-floored — the record follows a player who never made the show too, which is correct; its teeth simply have nothing to bite until they try to climb.

---

## 5. `DIRT-4` — the content half (build this: Sonnet)

**File:** append to `Assets/Narrative/Events/Content/dirt_underworld_events.json` (keep the arc in one document, as `DIRT-2`/`DIRT-3` did) **or** a sibling `dirt_the_bust_events.json` in the same `Content/` folder — **author's call; disclose which.** Both load identically. If appended, extend the existing `"//"` header. **Scope:** `avatar` throughout. **Category:** `hustle` (family/heir-pivot beats may use `family`; the clubhouse/scout beat may use `baseball`). **~4–5 new events + the §4.1 consolidation edits.** Weights ≤ 0.15; everything cooldown-paced or one-shot-flagged.

### 5.1 Flag lifecycle (author to this table exactly)

| Flag | Kind | Read as gate by | Written by `DIRT-4` |
|---|---|---|---|
| `in_the_life` (spine) | content | `dirt_the_indictment_a/b`, `dirt_going_straight` | **cleared** by `dirt_going_straight` (get out for good) |
| `cooperated_with_league` (DIRT-2) | content | `dirt_the_indictment_a` (better-deal gate), `dirt_the_indictment_b` (`flag_inactive`) | — (read-only; the hook fulfilled) |
| **`record_arrest`** (the canonical record) | **terminal** | `dirt_the_record_follows` **+ the ENGINE (§5-teeth)** | **set** by `dirt_the_indictment_a/b`, and by the §4.1 consolidation edits to `dirt_busted` / `dirt_robbery_fallout` |
| `banned_for_life` (DIRT-2, terminal) | terminal | `dirt_blacklisted` (shipped) **+ the ENGINE (§5-teeth)** | — (never written or cleared here) |
| `compromised_syndicate` | content | (shipped `syndicate_shakedown`) | **optionally cleared** by `dirt_going_straight`'s expensive buyout choice (§5.C — the one disclosed content clear; disclose) |

No new content flags. The only `set_flag` is `record_arrest` (read by the engine + `dirt_the_record_follows`); the only content clears are `in_the_life` (spine door-out, precedented by `DIRT-1`) and the optional `compromised_syndicate` buyout. No orphans; no dead-end sets.

### 5.2 Event slots

Contract per slot: gate intent, branch shape, consequence *intent*. Prose, exact numbers, and `prompt`/`label`/`outcome`/`text_message` copy are Sonnet's craft. Contacts per §6: the feds/DA/league security/booking = `unknown`; the lawyer = `agent_diaz`; the door-out voices = `coach_reyes` / `family_mom`; Sal go-between = `the_bookie` (texture only).

**THE PRO-SCALE BUST (the plea — the `cooperated_with_league` payoff, an OR-split pair)**

The indictment is one beat with two mutually-exclusive gates on the informant flag (the framework §3 OR-split idiom, exactly as `dirt_turn_informant_a/b`). Both gate `flag_active in_the_life`, `field tier >= 2`, `min_days_since ~30`, `flag_inactive banned_for_life`; both carry a cooldown (~45) so a fight-it grind re-offers; both **set `record_arrest`** on the plea.

1. **`dirt_the_indictment_a`** ★ — *the plea, COOPERATED path.* Gate additionally `flag_active cooperated_with_league`. Contact `agent_diaz` / `unknown`. Weight ~0.10. The better deal `DIRT-2` promised the informant.
   - *take the deal* — `absence reason:"arrest", days: 2` (light — you helped them), small `funds −` (fees), `set_flag record_arrest`, `clear_flag in_the_life`? **No — leave `in_the_life`;** the record and the life are separate axes (the door-out clears the life; the bust marks the record). `person_stat reputation −small`, `stress +`. Delayed `text_message` relief beat ("your cooperation was noted") at `delay_days: 1`.
   - *fight it anyway* — `absence reason:"arrest", days: 4`, bigger `funds −`, `set_flag record_arrest`, `stress +big`, `relationship kind:"rival", affinity:-30, target:"league"`. Even a cooperator who litigates loses more games.
2. **`dirt_the_indictment_b`** ★ — *the plea, did-NOT-cooperate path.* Gate additionally `flag_inactive cooperated_with_league`. Contact `unknown` / `agent_diaz`. Weight ~0.10. The full weight of the case.
   - *take the plea* — `absence reason:"arrest", days: 5` (heavier), `funds −` (real legal bill), `set_flag record_arrest`, `person_stat reputation −`, `stress +`. Mirrors `dirt_busted`'s plea at pro scale.
   - *fight it* — `absence reason:"arrest", days: 7`, biggest `funds −`, `set_flag record_arrest`, `stress +big`, `relationship kind:"rival", affinity:-50, target:"league"`. The costliest survival.

**THE RECORD FOLLOWS (the narrative face of the engine teeth)**

3. **`dirt_the_record_follows`** — *the mark, felt.* Gate: `flag_active record_arrest, min_days_since ~30`. Contact `unknown` (a GM/scout, faceless) / `coach_reyes`. Category `baseball` or `hustle`. Cooldown ~45 (`record_arrest` never clears — cooldown is the only pacing). Weight ~0.10. This is where the player *reads about* the promotion the engine is quietly denying them — a call-up that went to someone else, a scout's note about "character concerns." No new mechanical teeth (the engine owns those); pure texture that makes the invisible penalty legible. Consequences: `person_stat reputation −small`, `stress +`, small `funds −` or none. **Does not clear `record_arrest`.** *(Design note for Fable's review: this beat must not itself apply a promotion/rating penalty — the teeth live in `PromotionManager` (§5-teeth); this event only narrates them. Two systems, one flag, never the same tick.)*

**THE DOOR OUT (pro scale — the most expensive exit in the arc)**

4. **`dirt_going_straight`** ★ — *clean-break-at-scale.* Gate: `flag_active in_the_life, field tier >= 2, min_days_since ~60`. Contact `family_mom` / `coach_reyes`. Cooldown ~45 (one-more-season re-offers). Weight ~0.08. The pro-scale counterpart to `DIRT-1`'s `dirt_clean_break`; `DIRT-4` **converges** its full-life exit here rather than duplicating the tier-0 one — disclose that a tier-0 player still uses `dirt_clean_break`, and this is the pro version.
   - *get out for good* — `clear_flag in_the_life`, **big** `funds −` (buying out of the life costs real money at pro scale), `stress −big`, `person_stat morality +`, `person_stat maturity +`. The exit. **Leaves `record_arrest`/`banned_for_life` (the mark stays — §1).**
   - *buy the marker back* (**optional** second exit choice; **only meaningful if `compromised_syndicate` is active** — Sonnet may gate this as a separate `_buyout` event `flag_active compromised_syndicate` if a per-choice conditional is awkward) — `clear_flag in_the_life`, **`clear_flag compromised_syndicate`** (§5.C — the one disclosed content clear of the syndicate flag; defuses the +365 shakedown), **very large** `funds −`, `stress −`, `person_stat morality +`. The total buyout. **Disclose this as the sole place content writes `compromised_syndicate` false;** it is mechanically safe (single `Entity_Flags` table; `HustleService` itself calls `SetFlag(..., false, day)`) and semantically the "pay your way fully clean" ending the arc earns. Still leaves the record.
   - *one more season on top* — `funds +`, `person_stat morality −`, keeps `in_the_life` (the pull back; cooldown re-offers). Mirrors `DIRT-1`'s *one last score*.

**OPTIONAL — the heir pivot (Sonnet's call, disclose if used)**

5. **`dirt_the_next_generation`** — *what the record costs the ones who come after.* Gate: `flag_active record_arrest` (or `flag_active banned_for_life`) `, min_days_since ~60`. Contact `family_mom` / `family_dad`. Category `family`. Cooldown ~60. Weight ~0.06. The closing emotional note `dirt_blacklisted` gestured at — the name means one thing now, and it's the name your kid inherits. Pure texture (`person_stat`, `stress`), the natural bridge into heir content. Does not clear anything. **Author's call whether to include; it adds no new flag and reads a terminal flag already read elsewhere, so it is orphan-free either way.**

### 5.3 Pacing & load-order note

Same discipline as `DIRT-1/2/3` (arc doc §5.3): the underworld batch sorts after the weight-1.0 onboarding chains; the one-fire-per-day cap + file-order tiebreak mean those win their slots. No `DIRT-4` content weight exceeds 0.10. The pro-scale beats gate `tier >= 2`, structurally unreachable in the HS onboarding phase; `dirt_the_record_follows` needs `record_arrest`, which only an arrest can set. **Fresh-save safety is structural:** no `DIRT-4` gate can hold on a fresh tier-0, no-flags save at any day. Verify in the harness (§7.A).

### 5.4 The §4.1 consolidation edits (shipped-content changes — call out precisely)

Three existing events must gain `set_flag record_arrest` so the engine reads a consistent record:

- **`dirt_busted`** (DIRT-1, `dirt_underworld_events.json:199`) — add `set_flag record_arrest` to **both** the *take the plea* and *fight it* branches. (It already applies an `arrest` absence and clears `been_lifting`; it just never marked the record.)
- **`dirt_robbery_fallout`** (DIRT-3, `dirt_underworld_events.json:870`) — add `set_flag record_arrest` to **both** branches (it already clears `robbery_bust` on the plea; add the record mark).
- **`narcotics_arrest`** (`roster_availability_events.json:25`) — **already sets `record_arrest`; no edit** (verify it's on the surviving branch).

These are additive `set_flag`s to existing choices — no gate/weight/absence change, no id change. Calibration-inert (content only). Enumerate them in the batch header.

---

## 5-teeth. `DIRT-4` — the engine half (build this: Fable)

**This is the calibration-touching slice.** It is deliberately minimal: a two-flag read in the single avatar-eligibility code path that already exists, plus two `PromotionProfile` constants. It references **no** Life-sim surface (the CoreLoop boundary scan stays clean), adds **no** schema, and reuses the **already-wired, indexed** flag reader.

### 5-teeth.1 Where it hooks

`PromotionManager` decides the avatar's promotion in `RunOffseasonPass` (avatar eligibility computed ~`PromotionManager.cs:487–:504`; the avatar's `A`-ranking inside the per-`(tier,role)` sweep helper ~`:599–:643`). The avatar is already special-cased there (`isAvatar`, `avatarEligible`, the HS-senior-year gate at `:639`). **The record read slots into exactly that branch — nowhere else.**

The flag reader already exists and is already a `PromotionManager` dependency surface: **`BaseballQueries.LoadActiveFlagPlayerIds(string flagName, List<string> destination)`** (`BaseballQueries.cs:902`, rides the `idx_entity_flags_active_name` partial index). Load the avatar's record once per offseason pass (two calls — `record_arrest`, `banned_for_life` — into a reused buffer, membership-test the avatar id), so it is a bounded, indexed read on the yearly rollover, not a per-frame or per-player cost.

### 5-teeth.2 The two teeth

Evaluated **only for the avatar**, **only** in the avatar-eligibility branch (§4.3):

1. **`banned_for_life` active → the avatar is promotion-INELIGIBLE this offseason.** Set the avatar's `avatarEligible` to false (or short-circuit its rise) so it is excluded from vacancy-promotion and merit-swap *upward* — exactly as if it hadn't cleared the bar. The lifetime ban ends the climb. **Consistent with promotion doc §6's v1 stance (the avatar promotes but never auto-relegates):** the ban blocks the *rise*, it does not relegate the player — you keep your tier and locker, you just never advance again. This is the terminal mechanical teeth `DIRT-2` §8.1 promised and could not deliver.
2. **`record_arrest` active (and not banned) → a modest, tunable dock to the avatar's advancement score `A`.** Subtract `PromotionProfile.RecordArrestPenalty` (a small point value on the §2.3 `A` scale — first-pass small, e.g. enough to lose a marginal call-up but not to sink a genuinely elite line) from the avatar's `A` **before** it is ranked against its cohort. The rap sheet is a scout's makeup ding: you can still climb by outplaying it, you just carry a handicap. Tunable behind `run_monte_carlo_batch` like every other `PromotionProfile` constant.

Both are avatar-only reads; the NPC sweep, the conservation math, and the RNG streams are **untouched** (§4.3). `banned_for_life` dominates `record_arrest` (if banned, the dock is moot — ineligible is ineligible).

### 5-teeth.3 New calibration constants (in `PromotionProfile`, the existing static)

- `RecordArrestPenalty` — points docked from the avatar's `A` when `record_arrest` is active. First-pass **small** (Fable tunes; a first-pass suggestion is on the order of a low-single-digit fraction of the ~100-centered `A` scale — enough to break ties against the player and cost a borderline promotion, not enough to override a real talent gap). Document it beside the `wP/wS`/`SwapMargin` constants with the same "first-pass, tunable behind the MC batch" note.
- (No constant needed for the ban — it is a hard eligibility flip, not a weight.)

### 5-teeth.4 "Contracts" vs. promotion — a scoping disclosure

The user's framing was "a record that gates future contracts." The shipped economy has **no recurring per-season salary or contract-signing step** — promotion is a `SetTeam` (promotion doc §7), and the only funds event is the one-time creation grant in `CareerManager.CreateAvatarCore` (`:384`), which happens at game start before any record exists. So **there is no salary number for a record to dampen.** In the shipped model the career *is* the climb, and the truest mechanical expression of "a record costs you your career trajectory" is **promotion suppression**, which is what §5-teeth.2 does. Disclose this in the header: record→salary is unavailable (tier-flat economy, promotion doc §11's disclosed simplification); record→promotion is the teeth. If a tier-scaled salary is ever built, having the record already gate promotion is the natural seam to also gate pay.

---

## 6. Contacts

**No new contact row.** `DIRT-4` reuses `unknown` (feds/DA/league/booking/scout — the anonymity principle), `agent_diaz` (the pro-era lawyer — the established legal voice), `coach_reyes`, `family_mom`/`family_dad`, and optionally `the_bookie` (Sal, texture). Zero `contacts.json` / `ContactRegistry.cs` touch. The check #8 contact-registry unresolved pin stays **0**.

---

## 7. Gates & acceptance (Fable's review)

`DIRT-4` has a content gate set (§7.A, the standing pure-content discipline) **and** an engine gate set (§7.B, because it touches `Assets/Simulation/`). Both must pass.

### 7.A Content half (calibration-inert)

1. **`dotnet build`** 0/0.
2. **`GrittyEventsHarness`** — `check_event_graph_integrity` over the whole `Content/` folder passes. Bump the whole-folder **event-count literal** by the `DIRT-4` count (post-`DIRT-3` baseline **110** → **~114–115**, depending on whether the optional heir beat and the buyout-as-separate-event are used); the two count pins live in `Tools/GrittyEventsHarness/Program.cs` (DIRT-3 spec found them at `:249`/`:403`; **re-locate them, the line numbers drift**). Add each new event id to the `TryGetById` roster. Contact-registry unresolved pin stays **0**.
3. **New harness checks (batch-specific proofs):**
   - **The cooperate hook pays:** seed `in_the_life` + `tier = 2` + `cooperated_with_league` → `dirt_the_indictment_a` eligible and `dirt_the_indictment_b` **not**; seed without `cooperated_with_league` → `_b` eligible and `_a` **not** (the OR-split is mutually exclusive). Assert `_a`'s plea absence is shorter than `_b`'s (the better deal is real).
   - **Every arrest marks the record (consolidation):** assert `dirt_busted` (both branches), `dirt_robbery_fallout` (both branches), `narcotics_arrest`, and both `dirt_the_indictment_*` events emit `set_flag record_arrest`; assert `record_arrest` is **read** (by `dirt_the_record_follows` + — proven separately in §7.B — the engine) and is **never cleared** by any content branch.
   - **The door-out clears the right flags and NOT the record:** `dirt_going_straight` *get out for good* clears `in_the_life`; the buyout choice additionally clears `compromised_syndicate`; **neither clears `record_arrest` or `banned_for_life`** (assert the marks persist through the exit — the arc's closing invariant).
   - **Fresh-save inert (structural):** empty-flag fresh tier-0 avatar → **zero** `DIRT-4` gates hold at any day (reuse the `DIRT-1` `holdableOnDay2 == 0` idiom).
   - **No unpaced loop (#4):** every `DIRT-4` event carries `cooldown_days > 0` or self-clears a gate; enumerate the cooldowns as prior batches did.
   - **Optional PED re-theme is gate-inert (if done):** assert the four PED events' **prerequisites, consequences, weights, and ids are byte-unchanged** — only prompt/label text differs. (A cheap guard that the re-theme didn't accidentally re-gate.)
4. **Contact registry:** unresolved pin **0** (no new contact).

### 7.B Engine half (calibration-TOUCHING — the new discipline for this arc)

The content half is calibration-inert, but §5-teeth touches `Assets/Simulation/Baseball/PromotionManager.cs` (+ the `PromotionProfile` constants). So, unlike `DIRT-1/2/3`, **`git diff --stat -- Assets/Simulation` is NOT empty**, and the full baseball guard runs. This is the same gate shape as the 9c promotion slice itself (promotion doc §10).

1. **Neutrality guard (load-bearing).** With **no avatar record flag set**, a season with the engine change present is **bit-identical** to the pre-`DIRT-4` world: the MLB bit-identity regression guard (PA/H/ER) stays byte-exact, the 9c conservation law holds, and same-seed promotions are unchanged. The mechanic is inert until the avatar actually carries `record_arrest`/`banned_for_life` — the empty-ledger-neutrality precedent (rivalry / availability / gear / 9c itself). **This is the primary engine gate:** prove the two-flag read changes nothing when the flags are unset.
2. **NPC sweep untouched.** Assert the record read is avatar-only: the NPC removal set, top-down sweep, HS intake, and RNG streams are byte-identical to pre-`DIRT-4` (the record teeth must not perturb the conservation harness or determinism check — §4.3).
3. **The ban blocks the climb.** Seed a dominating avatar (an `A` that *would* promote) + `banned_for_life` → assert it is **not** promoted that offseason (ineligible), and that it is **not** relegated either (v1 stance — keeps its tier). Clear the flag → the same avatar promotes (proving the block is the cause).
4. **The rap sheet docks the climb.** Seed an avatar whose `A` sits **just above** the promotion bar → with `record_arrest`, `A − RecordArrestPenalty` drops it **below** the bar → not promoted; a comfortably-elite avatar with `record_arrest` **still** promotes (the dock is a handicap, not a wall). Proves §5-teeth.2's "harder, not impossible."
5. **`run_monte_carlo_batch` in full** (the sim assembly is touched): MLB bit-identity byte-exact in the no-record case, the 9a tier ladder + 9c conservation in-band, MonteCarloHarness green (the new engine checks live here, siblings of the 9c neutrality/conservation suites). CoreLoop / NeedsDecay / Schema unaffected (no schema change — `SchemaValidator user_version` unchanged).
6. **Determinism / stream isolation.** Same seed → same promotions with the record read present-but-unfired; enabling the read for a seeded record avatar changes only that avatar's decision, nothing else.

### 7.C Live boot

Live boot (godot MCP) on the real save: batch loads, dispatcher logs the new folder count, errors empty. Because the engine teeth need a record flag + a season rollover, seed `record_arrest` (and separately `banned_for_life`) on a **scratch copy** for the promotion smoke-test rather than advancing/dirtying the playtest save. Leave the playtest save un-advanced so the beats stay fresh for the user.

---

## 8. Wiring summary (the engine half, for Fable)

- **`BaseballQueries.LoadActiveFlagPlayerIds`** (`:902`) — **reuse as-is** to read `record_arrest` and `banned_for_life`. No new query, no new SQL const, no schema. (If a per-player single-flag probe reads cleaner than a by-flag list + membership test, a thin `bool IsFlagActive(playerId, flagName)` sibling of the existing `SqlSelectActiveFlagPlayerIds` is acceptable — Fable's call — but it is *not* required; the list read is already indexed and already a dependency.)
- **`PromotionManager`** — in the avatar-eligibility branch only (`~:487–:643`): load the avatar's two record flags once per pass; if `banned_for_life`, flip `avatarEligible`/short-circuit the rise; else if `record_arrest`, dock `PromotionProfile.RecordArrestPenalty` from the avatar's `A` before ranking. Nothing else in the sweep changes.
- **`PromotionProfile`** — add `RecordArrestPenalty` beside the existing first-pass constants, same "tunable behind `run_monte_carlo_batch`" documentation.
- **No `GameManager` wiring change** (`PromotionManager` is already constructed with `BaseballQueries` and subscribed to `SeasonRolledOverEvent`). **No schema change.** **No Life-sim reference** (CoreLoop scan clean).

---

## 9. Boundary disclosures (carry into the batch header)

1. **`DIRT-4` is the first arc batch that is NOT calibration-inert** — it touches `PromotionManager`. The neutrality guard (§7.B.1) is why that's safe: the teeth are inert until the avatar carries a record flag, so a no-record world is byte-identical to pre-`DIRT-4`. The MLB bit-identity guard re-runs and must stay exact in that case.
2. **The record teeth are avatar-only** (§4.3) — NPCs never read the record; the NPC sweep is byte-identical. Semantically correct, mechanically essential to the conservation/determinism harness.
3. **"Contracts" maps to promotion, not salary** (§5-teeth.4) — the shipped economy has no per-season salary to dampen; the record gates the climb. record→pay is a seam for a future tier-scaled economy, not this batch.
4. **`record_arrest` and `banned_for_life` are permanent** — the door-out (`dirt_going_straight`) never clears them; going straight ends the *life*, not the *record*. This is the arc's deliberate closing note.
5. **The one content clear of `compromised_syndicate`** — `dirt_going_straight`'s buyout choice is the sole place content writes the syndicate flag false, defusing the +365 shakedown at a steep price. Mechanically identical to `HustleService`'s own `SetFlag(..., false, day)`; disclosed as the intended "pay fully clean" ending.
6. **The PED events are NOT re-gated** (§2.1) — the arc-doc `ped_active` re-gate was a trap (`ped_active` has no gameplay writer; re-gating would strand the steroid arc). The conflation is resolved by documentation + optional prose re-theme, not by gating. **Two disclosed gaps left for future work:** (a) `ped_active` has no gameplay writer, so the 1.5× PED multiplier is dead in live saves and the "PED scandal" events actually fire off hustle-driven `detection_risk` — finishing the PED system (a "start juicing" on-ramp) is a separate future arc; (b) until then, the four PED events remain thematically mislabeled unless re-themed.
7. **The avatar event-choice UI** — like the rest of the arc, these `scope: avatar` beats autopilot-resolve until that surface ships; the indictment plea and the door-out are strong customers for it (give the harshest/most-consequential branches the lowest `autopilot_weight` so autopilot rarely self-selects a season-ending fight-it or the permanent buyout).

---

## 10. Model split & follow-ons

- **Opus 4.8 (this pass):** this spec (both halves). This is the **final** arc spec — `DIRT-4` closes the criminal underworld content arc.
- **Sonnet 5 (content half):** author the ~4–5 `DIRT-4` events to §5's contract (prose, numbers, `prompt`/`label`/`outcome`/`text_message`); apply the §5.4 consolidation edits (`set_flag record_arrest` onto `dirt_busted` + `dirt_robbery_fallout`); optional PED prose re-theme (§2.1, no gate change); extend `GrittyEventsHarness` with §7.A.3's checks + the count-literal bumps + `TryGetById` roster. No `contacts.json` touch.
- **Fable 5 (engine half + review):** build §5-teeth (`PromotionManager` avatar-record read + `PromotionProfile.RecordArrestPenalty`, reusing `BaseballQueries.LoadActiveFlagPlayerIds`); add the §7.B engine harness checks to MonteCarloHarness (neutrality, ban-blocks, dock-docks, NPC-untouched, determinism); run `run_monte_carlo_batch` in full; then review the content half (§7.A) + sign-off + live boot (§7.C). The engine slice is small enough that one Fable pass can build-and-review, but the neutrality/conservation sign-off is the load-bearing gate — treat it with 9c-level care.
- **Deferred (explicitly not this batch):** finishing the PED system (a `ped_active` "start juicing" on-ramp that revives the 1.5× multiplier, after which the four PED events could re-gate — §2.1); a tier-scaled salary economy that a record could also dampen (§5-teeth.4); the avatar event-choice UI (§9.7); symmetric avatar *relegation* on a ban (promotion doc §6 leaves this one flag away — `DIRT-4` deliberately keeps the kinder v1 "block the rise, don't take the job" stance).
