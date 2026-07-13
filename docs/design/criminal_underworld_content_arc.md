# Design — Criminal Underworld Content Arc ("The Dirt")

**Author:** Claude Opus 4.8 (narrative architecture) · **Slice:** Content arc, batch 1 of a multi-batch criminal spine · **Status:** spec written for Sonnet 5 to author `DIRT-1` against; Fable 5 reviews + gates. Companion to `docs/design/gritty_event_framework.md` (the content contract this obeys) and `docs/design/presentation_layer_narrative.md` (contacts/threads).

The user picked **the "dirt" — the criminal underworld** as the lead content arc (roadmap item 6). "Dirt & Diamonds" is a two-word thesis: the **diamonds** are the ballfield and the clean career; the **dirt** is the fast money you can reach for when the diamonds don't pay the rent. This arc is the dirt — and its whole point is that the two halves are **entangled**: crime is the fastest way to fund a stalled career, and it is also the fastest way to *end* one (a bust benches you; a fixed game gets you banned). Every criminal choice is a bet against the career.

This document specs the arc's shape, the **authoring contract** every batch obeys, and **`DIRT-1` ("The On-Ramp") in full buildable detail**. Later batches (`DIRT-2..4`) are outlined in §8 and will get their own full specs when their turn comes.

---

## 1. Design intent — what the dirt should feel like

- **Money pressure is the doorway, not villainy.** The player doesn't wake up a criminal; they get pulled in one reasonable-seeming step at a time — a card game to make rent, holding a package for a guy, ducking a debt. Recklessness and a thin bankroll are the gates, never "are you evil."
- **The descent is a staircase, paced in weeks and seasons.** Each step raises the stakes and narrows the exits. The `min_days_since` cascade clock (framework §3) is the arc's spine: a debt you duck this month is *sold to worse people* next month; a job you run this season is the reference that gets you the game-fixing offer next season.
- **Crime cashes out against the career.** The mechanical teeth are `absence` (a bust or suspension that benches you — real lost playing time), `stress` (the life sim's decay accelerant), `funds` (the reason you started), `person_stat: morality/reputation` (who you're becoming), and eventually a `rival` edge with the whole league. Fast money that never cost anything would be free money; it must be able to take the diamonds away.
- **There is always a door out — and it's expensive.** Every deep-end flag has a clean-break exit that clears it, at a cost (money, a promise, a plea). Redemption is authored, not automatic (the shipped `redemption_tour` is the template).

---

## 2. What already exists (do not collide; do converge)

Three criminal threads already ship (`core_events.json`, `career_arc_events.json`, `roster_availability_events.json`). **New content must extend these, not fork parallel copies.**

| Shipped thread | Flags | Cascade |
|---|---|---|
| **Syndicate loan-shark** | `compromised_syndicate` → `syndicate_marked` | `back_alley_bribe` (recklessness>75, funds<500) sets `compromised_syndicate`; `syndicate_shakedown` fires at **+365d**; `defy_them` → `syndicate_marked` → `syndicate_enforcers` at +14d. |
| **PED / steroid scandal** | `steroid_scandal_public` | `caught_juicing` (**`detection_risk > 70`**) → `redemption_tour` at +180d → clears. |
| **Engine-written hustle flags** | `narc_watchlist`, `bad_product`, `controls_turf_f`/`controls_turf_<n>`, `ped_active` | Written by `HustleService` (Phase 8b). Content **may gate** on them (`flag_active`) without setting them — check #2 treats them as pre-satisfied. |

**The convergence (the arc's keystone).** `compromised_syndicate` is a *content-writable* `set_flag`, and `syndicate_shakedown` reads it on **any** subject (scope:any, `min_days_since: 365`). So a *new* on-ramp that sets `compromised_syndicate` reaches the shipped shakedown → enforcers cascade for free. `DIRT-1`'s gambling-debt spiral uses exactly this: when the player ducks the bookie long enough, **the book sells the marker to the syndicate** (`dirt_debt_sold` sets `compromised_syndicate`). Two on-ramps (bribe, debt), one deep end. Fable's harness must prove **both** paths reach `syndicate_shakedown`.

**Two collisions this arc must route around — both real, both disclosed:**

1. **`detection_risk` is the PED meter, not a general "heat" gauge.** `caught_juicing` gates on `detection_risk > 70` **with no check for actual PED use**. If criminal content raised `detection_risk`, a player who never juiced would get the "tester with a clipboard, the numbers don't lie" event. **Rule: `DIRT-*` tracks legal jeopardy with boolean flags (`been_lifting`, `dodging_debt`, …), never the numeric `detection_risk` field.** If a future batch wants a shared numeric heat meter, the clean fix is to re-gate `caught_juicing` on the engine flag `ped_active` (a Sonnet content edit + harness re-run) — out of scope for `DIRT-1`, noted for `DIRT-4`.
2. **The syndicate stays anonymous by design.** `contacts.json` states the syndicate/arrest beats live on the reserved `unknown` thread deliberately ("anonymous threats, not named relationships"). Keep the *faceless* threats on `unknown`. A *recurring, familiar* criminal associate (the bookie you place bets with) is a named relationship and earns one new contact (§6).

---

## 3. The arc spine (multi-batch map)

```
                 money pressure + recklessness
                              │
        ┌──────────── DIRT-1: THE ON-RAMP ────────────┐
        │  petty crime · a gambling debt · the bookie  │
        │  master flag: in_the_life                    │
        └───────┬───────────────────────────┬─────────┘
                │ duck the debt              │ work it off / stay in
                ▼                            ▼
      compromised_syndicate          in_the_life active
      (CONVERGES into shipped        │
       syndicate_shakedown@+365)     ▼
                              DIRT-2: THE FIX (tier≥2)
                              syndicate calls in the marker:
                              shave runs / throw a game
                              → suspension absence, league rival
                                     │
                    ┌────────────────┼────────────────┐
                    ▼                                  ▼
        DIRT-3: PRODUCT & TURF                DIRT-4: THE BUST & THE DOOR OUT
        fencing / moving weight               arrest absence · plea · a record
        (narrative wrap of HustleService      that follows you · going straight
         bad_product / controls_turf flags)   (clean-break at scale)
```

`DIRT-1` is fully specced below. `DIRT-2..4` are §8 outlines. **`in_the_life`** is the spine flag every later batch reads as its entry gate; the whole arc is escapable because the clean-break exits clear it.

---

## 4. The authoring contract (every `DIRT-*` batch holds these)

This is the reusable checklist. It restates the framework §2–§4 constraints in the specific shapes this arc will trip over.

1. **Closed vocabulary — no engine changes.** Author only within the shipped fields/ops/consequences (enumerated in the framework doc and the integrity SKILL). A new consequence type or poll field is a **Fable engine task**, not a content batch. This arc needs none — it is expressible entirely in `funds / stress / set_flag / clear_flag / absence / person_stat / relationship`.
2. **Prereq fields are a closed list; person_stats are write-only.** Gateable fields: `funds, age, recklessness, health_ceiling, detection_risk, baseball_interest, strictness, teammate_ex_of_partner, tier, gpa`. **`morality`, `reputation`, `confidence`, etc. can be *written* (`person_stat`) but never *gated on*** — there is no poll column for them. Criminal on-ramps gate on **`recklessness`** (a real Players field) and **`funds`**, and branch on **flags**, never on a person-stat threshold.
3. **`scope: avatar` for the descent.** `DIRT-1` is the *played character's* story and wants the future choice UI to surface it, so every event is `scope: avatar`. This also sidesteps two hazards for free: (a) the scope:any × weight blast radius (check #6), and (b) the **NULL-team tier leak** (check #7 — a scope:any `tier <` gate leaks onto unrostered NPCs polling as sentinel `-1`). Avatar scope with explicit `tier == 0` / `tier >= N` gates is immune. Ambient scope:any underworld *texture* is allowed in later batches but must mind both hazards.
4. **`interest` is a no-op on the avatar.** `baseball_interest` starts at the 100 ceiling for the avatar, so any `interest` delta clamps to nothing. Do not use it as an avatar reward — it is reserved for heir content (`age < 16`). Avatar "reward" is `funds`, `stress` relief, or `person_stat`.
5. **Flag hygiene (checks #2/#3/#4).** Every flag a prerequisite reads must be `set_flag`-written somewhere in the library (or be a documented engine flag). Every `set_flag` should be read by some prerequisite (a forward hook for a not-yet-shipped batch is allowed but **must be listed in the batch header**). No unpaced loops: any event whose choices re-satisfy its own gate needs `cooldown_days > 0`, a self-blocking flag, or a consequence that breaks the gate.
6. **Absence semantics (roster triad).** `{"type":"absence","reason":"injury|suspension|arrest","days":N≥1}` benches the subject for `until_day = fire_day + days + 1` (games missed, not calendar days). Overlaps don't stack — a shorter absence on an already-benched player is a silent no-op. For an `injury` absence the engine computes rust from live `health_ceiling` — **do not also author a rating debuff.** Crime uses `arrest` and `suspension`.
7. **Contacts (check #8).** Faceless threats → `unknown`. The one new named associate (`the_bookie`, §6) ships on an **existing** role to stay pure-content (zero `ContactRegistry.cs` touch — the role parser is a closed switch). Any contact id a batch tags must have a row in `contacts.json` in the same change.
8. **Calibration-inert.** Content touches **zero** `Assets/Simulation/` files, so the MLB bit-identity guard is untouched by construction. The gate is `git diff --stat -- Assets/Simulation` **empty** + the `GrittyEventsHarness` (which runs `check_event_graph_integrity`) + `dotnet build` 0/0. No `run_monte_carlo_batch` re-run is mandated, but running it to confirm byte-exact is cheap insurance.

---

## 5. `DIRT-1` — "The On-Ramp" (the lead batch, build this)

**File:** `Assets/Narrative/Events/Content/dirt_underworld_events.json`. **Author:** Sonnet 5. **Scope:** `avatar` throughout. **Category:** `hustle` (family-fallout beats may use `family`). **~11 events.** Weights ≤ 0.15, everything cooldown-paced, one-shot-flagged, or self-clearing.

### 5.1 Flag lifecycle (the state machine — author to this table exactly)

| Flag | Introduced by | Read as gate by | Cleared by |
|---|---|---|---|
| `owes_the_book` | `dirt_high_stakes_game` (chase-the-pot) | `dirt_the_marker` | `dirt_the_marker` (pay), `dirt_run_the_package` (clean run), `dirt_debt_sold` |
| `dodging_debt` | `dirt_the_marker` (duck) | `dirt_debt_sold` | `dirt_debt_sold` |
| `running_errands` | `dirt_the_marker` (work it off) | `dirt_run_the_package` | `dirt_run_the_package` (clean run) |
| `been_lifting` | `dirt_sticky_fingers` (grab it) | `dirt_busted` | `dirt_busted` (take the plea), `dirt_clean_break` |
| **`in_the_life`** (spine) | `dirt_debt_sold`, `dirt_run_the_package` (any stay-in choice) | `dirt_family_finds_out`, `dirt_hot_merch`, `dirt_clean_break`, **(DIRT-2 entry — forward hook)** | `dirt_clean_break` (get out clean) |
| `compromised_syndicate` **(EXISTING)** | `dirt_debt_sold` **(new writer)** + `back_alley_bribe` (shipped) | `syndicate_shakedown` **(shipped)** | (shipped syndicate arc) |

Every flag set is read by a prerequisite → **no orphans**. `in_the_life` is additionally a **disclosed forward hook** for `DIRT-2`, but it is already read *within* `DIRT-1` (clean-break/family/hot-merch), so it is not a dead-end. List it in the header anyway.

### 5.2 Event slots

The narrative prose and the exact numbers are Sonnet's craft — below is the **contract per slot**: gate intent, the branch shape, and the consequence *intent*. Give every event a `prompt`; give every choice a `label`; use `outcome`/`text_message` where a beat wants an immediate or delayed payoff (the bookie texting "we're square" a day after you pay is exactly the amendment-§1 delayed-text idiom).

**MAIN CASCADE — the gambling debt spiral**

1. **`dirt_high_stakes_game`** — *entry.* Gate: `recklessness > 45`, `flag_inactive: owes_the_book`. Contact `the_bookie`. Cooldown ~60. Weight ~0.10.
   - *cash out early* — small `funds` +, `stress` −small. The disciplined win; no flag. (`autopilot_weight` highest — most sessions dodge the trap.)
   - *chase the pot* — `funds` − (you lost), `set_flag owes_the_book`, `stress` +. The debt path.
   - *don't sit down* — nothing / trivial `stress`.
2. **`dirt_the_marker`** — *the collection.* Gate: `flag_active owes_the_book, min_days_since ~10`. Contact `the_bookie`. Weight ~0.30 (he *will* find you).
   - *pay the marker* — `funds` − (principal + vig), `clear_flag owes_the_book`, `person_stat discipline +`. **Exit.** Delayed `text_message` "we're square." at `delay_days: 1`.
   - *work it off* — `set_flag running_errands`, small `funds` +, `stress` +. Keeps `owes_the_book`.
   - *duck the call* — `set_flag dodging_debt`, `stress` +, `person_stat morality −`.
3. **`dirt_debt_sold`** — *CONVERGENCE into the syndicate spine.* Gate: `flag_active dodging_debt, min_days_since ~30`, `flag_inactive: compromised_syndicate`. Contact `unknown` (the escalation is faceless now). Weight ~0.5. **One or two grim choices — there is no clean out here, that's the point.** Consequences on the "…" choice(s): `set_flag compromised_syndicate`, `set_flag in_the_life`, `clear_flag dodging_debt`, `clear_flag owes_the_book`, `stress` +big. This is the bridge; from here the shipped `syndicate_shakedown` owns the arc at +365d.
4. **`dirt_run_the_package`** — *working it off.* Gate: `flag_active running_errands, min_days_since ~7`. Contact `the_bookie`/`unknown`. Cooldown ~21 (a big debt is several jobs). Weight ~0.25.
   - *clean run* — `funds` +, `clear_flag running_errands`, `clear_flag owes_the_book`, `set_flag in_the_life`. **Exit via labor** — but you're now `in_the_life`.
   - *keep the extra* — more `funds`, `person_stat morality −`, `set_flag in_the_life`; keeps `running_errands` (the grind continues, cooldown-paced — **not** an unpaced loop since the clean-run choice can break it).
   - *cold feet* — `stress` +, `person_stat discipline −`; keeps `running_errands`.

**PETTY-CRIME TEXTURE (on-ramps & the bust)**

5. **`dirt_lookout`** — *HS on-ramp* (ties the dirt into the tier-0 phase currently in playtest). Gate: `tier == 0`, `funds < ~150`, `flag_active hs_started, min_days_since ~14` (belt-and-braces so it never lands in the onboarding week; mirrors the `hs_school_life` pacing audit). Contact `hs_friend`. Weight ~0.08.
   - *stand lookout* — small `funds` +, `person_stat morality −`, `stress` +.
   - *walk home* — `person_stat discipline +`.
6. **`dirt_sticky_fingers`** — *broke-and-lift* (tier-agnostic). Gate: `funds < ~200`. Cooldown ~45. Weight ~0.08.
   - *grab it and go* — small `funds` +, `person_stat morality −`, `stress` +, `set_flag been_lifting`.
   - *put it back* — `person_stat discipline +`, `stress` −small.
7. **`dirt_busted`** — *the petty-crime bust* (the mechanical teeth). Gate: `flag_active been_lifting, min_days_since ~20`. Contact `unknown` (booking) / `agent_diaz` (the lawyer, pro-era). Weight low ~0.08.
   - *take the plea* — `absence reason:"arrest", days: 2`, `funds` − (lawyer), `clear_flag been_lifting`, `person_stat reputation −`, `stress` +. The paid exit.
   - *fight it* — `absence reason:"arrest", days: 3`, more `funds` −, keeps `been_lifting`, `stress` +big.

**FALLOUT & TEXTURE**

8. **`dirt_family_finds_out`** — Gate: `flag_active in_the_life, min_days_since ~30`. Contact `family_mom` or `family_dad`. Category `family`. Weight ~0.12.
   - *promise to stop* — `stress` −, `person_stat maturity +` (a promise is not an exit — does **not** clear `in_the_life`).
   - *lie about the money* — `person_stat morality −`, `stress` +.
   - *tell them the truth* — mixed: `person_stat maturity +`, `stress` +, `person_stat morality +`.
9. **`dirt_hot_merch`** — *fencing flavor* (a light foretaste of DIRT-3). Gate: `flag_active in_the_life`. Cooldown ~30. Weight ~0.08. Optionally also `flag_active bad_product` (engine flag, pre-satisfied per check #2) to tie into `HustleService` — **author's call, disclose if used**.
   - *move it* — `funds` +, `person_stat morality −`, `stress` +.
   - *not my problem* — `stress` −small.
10. **`dirt_street_rep`** — *who you're becoming* (writes reputation; no gate on it since reputation isn't pollable). Gate: `flag_active in_the_life, min_days_since ~45`. Weight ~0.06. Single/few choices moving `person_stat reputation`/`social_status` ±, small `funds`. Pure texture.

**THE DOOR OUT**

11. **`dirt_clean_break`** — *going straight* (the redemption counterpart; reads the spine flag, closing the loop). Gate: `flag_active in_the_life, min_days_since ~60`. Contact `family_mom`/`coach_reyes`. Weight ~0.08.
    - *get out clean* — `clear_flag in_the_life`, `clear_flag been_lifting`, `stress` −big, `person_stat morality +`, `person_stat maturity +`. **The exit.**
    - *one last score* — `funds` +, `person_stat morality −`, `stress` +; keeps `in_the_life` (the pull back in). Cooldown-paced so it can re-offer.

### 5.3 Pacing & load-order note

`dirt_underworld_events.json` sorts after `career_arc_events.json` but the per-subject **one-fire-per-day** cap and file-order tiebreak mean the weight-1.0 onboarding chains always win their slots first (same discipline as `hs_school_life_events.json`'s audit). No `DIRT-1` weight exceeds 0.30; the `min_days_since` gates keep the cascade steps apart; nothing lands inside the HS onboarding week. Verify none of these gates can hold on **day 1–2** of a fresh save.

---

## 6. New contact row

Add to `Assets/Narrative/Contacts/contacts.json` in the same change (check #8):

```json
{ "id": "the_bookie", "display_name": "Sal", "role": "friend" }
```

`role: "friend"` ships pure-content: `ContactJson`'s role parser is a **closed switch** (throws on an unknown string), and `friend` is already a valid role (`hs_friend`). "Sal" is a *familiar* associate — deliberately named, unlike the faceless syndicate, which stays on `unknown` per the registry's stated anonymity principle. Update the registry's `//` header to describe the new thread (the batch that mints a contact documents it, same as every prior thread). If a *distinct* `criminal`/`fixer` role is later wanted for tone, that's a one-line `ContactRegistry.cs` enum-case add — a small Fable engine task, disclosed here, **not** required for `DIRT-1`.

---

## 7. Gates & acceptance (Fable's review)

`DIRT-1` is pure content — no schema, no scene, no `Simulation/` touch. The gate set:

1. **`dotnet build`** 0 warnings / 0 errors (a malformed batch fails the loader loudly at build/boot).
2. **`GrittyEventsHarness`** (`dotnet run --project Tools/GrittyEventsHarness -c Release`) — its first check is `check_event_graph_integrity` over the whole `Content/` folder. Bump the whole-folder **event-count literal** (79 → 79 + `DIRT-1` count) and the contact-registry pin (unresolved must stay 0 — the new `the_bookie` row satisfies it).
3. **New harness checks to add** (the batch-specific proofs, same pattern as `RunTutorialArcChecks`):
   - **Both on-ramps reach the shakedown:** seed `back_alley_bribe`→`compromised_syndicate` *and* the debt path `dirt_high_stakes_game(chase)`→`dirt_the_marker(duck)`→`dirt_debt_sold`→`compromised_syndicate`; assert `syndicate_shakedown` becomes eligible at **+365** on both.
   - **The debt spiral is escapable at every step:** pay-off (`dirt_the_marker` pay) clears `owes_the_book`; clean-run clears `running_errands`+`owes_the_book`; `dirt_clean_break` clears `in_the_life`+`been_lifting`.
   - **The bust benches:** `dirt_busted` produces an `arrest` absence with the right `until_day`.
   - **No unpaced loop / no orphan flag** across the batch (the standing checks #2–#4).
4. **Calibration-inert:** `git diff --stat -- Assets/Simulation` **empty**. `run_monte_carlo_batch` not mandated; if run, MLB guard must stay byte-exact (PA 48384 / H 10969 / ER 5237). CoreLoop / NeedsDecay / Schema unaffected.
5. **Live boot** (godot MCP) on the real save: batch loads, dispatcher logs the new folder count, errors empty, save left un-advanced so the beats stay fresh for the user's playtest.

---

## 8. `DIRT-2..4` — outlines (full specs when their turn comes)

- **`DIRT-2` — "The Fix" (tier ≥ 2, the diamonds-meet-dirt centerpiece).** **→ NOW FULLY SPECCED: `docs/design/criminal_underworld_dirt2_the_fix.md` (Opus, 2026-07-13).** Entry gates on `flag_active compromised_syndicate` (the primary ramp — they own your marker) or `flag_active in_the_life` + `flag_inactive compromised_syndicate` (the second ramp, `dirt_made_useful`, a third writer of `compromised_syndicate`), **and** `tier >= 2` (rostered pro). The syndicate calls in the marker: tip a line, shave runs, throw a game. Stakes are baseball-terminal — a `suspension` absence, a permanent `rival` edge with `target: league`, a `banned_for_life` flag later batches read (career-ending wiring is a disclosed future engine task — `CareerManager` reads no flags today). Clean use of the shipped spine: `compromised_syndicate` is the *prerequisite*, the fix is the *ask*; refusing/informing routes into the shipped `syndicate_marked → syndicate_enforcers` cascade.
- **`DIRT-3` — "Product & Turf."** Narrative wrap around the `HustleService` engine flags (`bad_product`, `controls_turf_f`/`controls_turf_<n>`, `narc_watchlist`) — gate on them (`flag_active`, pre-satisfied per check #2) so the interactive-hustle minigame slice (roadmap item 7) and the narrative layer reference the same state. Fencing escalation, a bad batch, a turf dispute that spawns a `rival`.
- **`DIRT-4` — "The Bust & The Door Out."** The full legal-jeopardy beat at pro scale (`arrest`/`suspension` absences, a plea, a record that gates future contracts) and clean-break-at-scale. This is where the `detection_risk` re-gate (§2, collision 1) belongs if a shared numeric heat meter is wanted: re-gate `caught_juicing` on `ped_active` so crime and PEDs can share the field cleanly.

---

## 9. Model split & follow-ons

- **Opus 4.8 (this pass):** this spec; the `DIRT-2..4` full specs later.
- **Sonnet 5 (next):** author `dirt_underworld_events.json` to §5's contract (prose, exact numbers, `prompt`/`label`/`outcome`/`text_message` copy), add the `the_bookie` contact row, extend the `GrittyEventsHarness` with §7.3's checks + count pin. Run `check_event_graph_integrity`.
- **Fable 5 (review):** §7 gate set + the standing Sonnet-build review; sign-off; live boot.
- **Deferred (not this arc):** the avatar event-choice UI (these `scope: avatar` criminal beats autopilot-resolve until it ships — they are *designed* to be the high-agency decisions the player will most want to make themselves, so they are a strong first customer for that UI); the `detection_risk`/`ped_active` re-gate (DIRT-4); a distinct `criminal` contact role (optional, one enum case).
