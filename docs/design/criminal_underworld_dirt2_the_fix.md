# Design — Criminal Underworld Content Arc: `DIRT-2` "The Fix"

**Author:** Claude Opus 4.8 (narrative architecture) · **Slice:** Content arc, batch 2 of the criminal spine · **Status:** spec written for Sonnet 5 to author `DIRT-2` against; Fable 5 reviews + gates. Companion to `docs/design/criminal_underworld_content_arc.md` (the arc contract + `DIRT-1`, which this obeys and extends — read that first) and `docs/design/gritty_event_framework.md` (the closed content vocabulary).

This is the promotion of the arc doc's §8 `DIRT-2` outline into a full buildable spec. `DIRT-1` was the on-ramp — a broke kid getting pulled into a gambling debt one reasonable step at a time. `DIRT-2` is the payoff of the arc's whole thesis: **crime finally comes for the diamonds directly.** The syndicate that has owned your marker since the minors notices you made the show, and now you're worth more to them as a fixer than as a debtor. The ask is no longer "hold this package" — it's "shave a run," "throw the game." The stakes are baseball-terminal for the first time: a suspension that benches you for real games, the whole league turning on you, and — at the deep end — a `banned_for_life` mark that follows the career forever.

---

## 1. Design intent — what "The Fix" should feel like

- **This is where the two words of the title finally collide.** `DIRT-1`'s crime cost you money, stress, a couple of arrest days. `DIRT-2`'s crime costs you *baseball* — the diamonds themselves. The suspension is not an inconvenience; at tier ≥ 2 it is lost playing time in a career you clawed your way into. That contrast is the entire point of gating on `tier >= 2`: the same fix at tier 0 would be an abstraction, but here it benches a *rostered pro*.
- **You don't get asked until you're worth fixing.** The doorway is no longer poverty (that was `DIRT-1`); it's *value*. You are useful now. The syndicate's interest is a perverse form of having made it. The ask should read as a promotion into a darker league.
- **The teeth are the risk, not the act.** Fixing a game pays and deepens your complicity; it does not itself bench you. The bench comes from *getting caught*, and getting caught is a separate event whose odds rise the longer and deeper you're in (the shipped `been_lifting → dirt_busted` idiom at pro scale). Fast money that never risked the career would be free money.
- **There is a door out, and it's the hardest one in the arc.** You can turn on them — cooperate with the league before the investigation catches you — and clear your complicity. But informing on the syndicate *is* defying the syndicate, so the honest exit routes you straight into the shipped `syndicate_marked → syndicate_enforcers` cascade. Redemption here costs a run-in with two men by your car. That is the correct price.

---

## 2. What already exists (converge, do not collide)

`DIRT-2` is almost entirely a **convergence batch** — it plugs into three shipped cascades rather than inventing machinery. Verified live this pass:

| Shipped surface | Where | `DIRT-2` uses it as |
|---|---|---|
| **`compromised_syndicate`** (set by `back_alley_bribe` *and* `DIRT-1`'s `dirt_debt_sold`) | `core_events.json`, `dirt_underworld_events.json` | **Entry prerequisite** for the primary fix ramp — "they own your marker." `DIRT-2` is also a **third writer** (via `dirt_made_useful`, §5.2). |
| **`syndicate_shakedown`** @ `compromised_syndicate` +365 | `core_events.json` | Untouched. `DIRT-2`'s new `compromised_syndicate` writer back-feeds it for free, exactly as `DIRT-1` did. |
| **`syndicate_marked` → `syndicate_enforcers`** @ +14d (`fight_back` writes `rival/league -60`) | `career_arc_events.json` | **Refusal routing.** Refusing the fix / informing sets `syndicate_marked`, reaching the shipped enforcer beat. `DIRT-2` is a new writer of `syndicate_marked`; it is already read by the shipped enforcers, so no orphan. |
| **`in_the_life`** (the arc spine, set across `DIRT-1`) | `dirt_underworld_events.json` | **Entry prerequisite for the second ramp** (`dirt_made_useful`) — the pro who worked the debt off clean but is still known. |
| **`{"type":"absence","reason":"suspension","days":N}`** + a `served_*` flag | `roster_availability_events.json` | The mechanical teeth. Benches by *games missed* (`until_day = fire_day + days + 1`); overlaps don't stack. |
| **`{"type":"relationship","kind":"rival","affinity":-N,"target":"league"}`** | `syndicate_shakedown`/`syndicate_enforcers` | The whole-league rival edge — publishes `RivalryChangedEvent` through the untouched Phase-6 chain. `DIRT-2` reuses this idiom verbatim for the league turning on a fixer. |

**Three collisions this batch must route around — all real, all disclosed:**

1. **`detection_risk` is still the PED meter, not a heat gauge.** `DIRT-2` continues `DIRT-1`'s rule (arc doc §2, collision 1): legal jeopardy is tracked with **boolean flags** (`on_the_hook`, `shaving_runs`, `threw_a_game`), **never** the numeric `detection_risk` field. A fixer who never juiced must never draw the `caught_juicing` "tester with a clipboard" beat. The shared-numeric-heat re-gate remains a `DIRT-4` concern.
2. **`banned_for_life` has no live career-ending wiring.** Verified: `CareerManager.cs` reads **zero** Entity_Flags today. So a pure-content `banned_for_life` flag **cannot** actually end a career or block a promotion — that is a `Simulation/Baseball` engine task, **out of scope** for calibration-inert content. Within `DIRT-2`, the flag's real teeth are the **season-length suspension**, the **`rival/league` edge**, and the **`dirt_blacklisted` epilogue** that reads it. The flag is planted as the durable record for `DIRT-4` and a future `CareerManager` read (disclosed forward hook, §5.1); it is **not** claimed to end the career on its own. Do not let the prose promise a career-ending consequence the engine won't deliver yet — write it as a permanent stain and a season lost, not a retirement.
3. **The syndicate stays faceless.** The fix, the investigation, and league security ride the reserved `unknown` thread (the registry's stated anonymity principle). The one familiar criminal face — Sal (`the_bookie`) — may appear as a **go-between** in texture, but the marquee ask and the bust are `unknown`. **`DIRT-2` mints no new contact row** (purer than `DIRT-1`): zero `ContactRegistry.cs` touch.

---

## 3. Where `DIRT-2` sits in the spine

```
   DIRT-1 outcomes:  compromised_syndicate        in_the_life (worked off clean,
                     (owes the syndicate)          never sold to the syndicate)
                            │                                   │
              tier >= 2  ───┤                                   ├─── tier >= 2
                            ▼                                   ▼
                   ┌── dirt_the_ask ──┐                 dirt_made_useful
                   │  (they own you)  │                 (the crew sells your
                   └────────┬─────────┘                  name UP now you're a pro)
                            │                                   │ engage
                            │                        sets compromised_syndicate
                            │                        ── SECOND RAMP converges ──┐
                            │                                                   │
                            ▼◄──────────────────────────────────────────────────┘
                        on_the_hook ──► dirt_the_tip (low-stakes leak, recurring)
                            │
                            ▼ dirt_shave_runs
                   ┌────────┴──────────┐
              shave │            throw │  (also sets threw_a_game)
                    ▼                  ▼
              shaving_runs ────────────┘
                    │
        ┌───────────┼─────────────────────────────┬───────────────────────┐
        ▼           ▼                             ▼                        ▼
  dirt_league_   (deny/stonewall)          dirt_permanent_ban        dirt_turn_informant
  investigation   longer suspension        (needs threw_a_game)      (the door out — but
  suspension 20   + rival/league           banned_for_life +         informing = defiance →
  clears flags                             season suspension +       sets syndicate_marked →
  (survivable)                             rival/league -70          shipped enforcers)
                                                  │
                                                  ▼
                                            dirt_blacklisted (epilogue; reads
                                            banned_for_life — the record follows you)

  REFUSE at dirt_the_ask ──► syndicate_marked ──► shipped syndicate_enforcers @ +14
  Never-crossed-the-line out: dirt_walk_away_clean (on_the_hook & !shaving_runs) clears
      on_the_hook  (but compromised_syndicate persists → the +365 shakedown still looms)
```

---

## 4. Authoring contract deltas (on top of arc doc §4)

Everything in the arc doc §4 holds unchanged (closed vocabulary, no engine changes, scope:avatar for the descent, `interest` is a no-op on the avatar, flag hygiene, absence semantics, calibration-inert). The `DIRT-2`-specific restatements:

1. **The fix is narrative, not simulation.** "Shave a run" / "throw a game" is expressed **entirely** as a consequence bundle (`funds +`, `person_stat morality/reputation −`, `stress +`, `set_flag`) plus a **complicity flag that raises the bust odds**. It does **not** hook `AtBatResolver`, `LeagueSimulator`, or standings — the batch touches **zero** `Assets/Simulation/` files by construction. The mechanical consequence is the *suspension absence when caught*, applied through the shipped roster triad. This is the single most likely thing to get wrong: **do not try to make the avatar actually underperform in the sim.**
2. **Complicity is a two-rung ladder, tracked by two flags.** `on_the_hook` (you engaged — took the meeting, leaked a line) and `shaving_runs` (you actually fixed a result). A third flag, `threw_a_game`, is the **aggravating** rung set only by the deepest choice; it is the sole gate that unlocks the permanent-ban beat. This separation lets the common bust be survivable (suspension, clears flags) while reserving `banned_for_life` for players who threw whole games.
3. **`tier >= 2`, avatar scope, explicit floor.** Every fix-cascade event gates `{ "field": "tier", "op": ">=", "value": 2 }`. Avatar scope + a `>=` floor (never `<`) is immune to the NULL-team sentinel −1 leak (`NarrativePollQueries` reads unrostered subjects as −1; −1 fails `>= 2`). This is the same proven shape as the shipped rookie batch's `tier >= 2`.
4. **`banned_for_life` is terminal and never cleared inside this arc.** A never-cleared flag is legal (check #5 forbids orphan *sets*, and it is read by `dirt_blacklisted`). It is a permanent record by design; any redemption-at-scale belongs to `DIRT-4`. Do not author a clear for it.

---

## 5. `DIRT-2` — "The Fix" (build this)

**File:** append to `Assets/Narrative/Events/Content/dirt_underworld_events.json` (same file as `DIRT-1` — it is the underworld batch; keep the arc in one document) **or** a sibling `dirt_the_fix_events.json` in the same `Content/` folder — **author's call; disclose which.** Either loads identically (the dispatcher globs the folder). If appended, extend the existing file `"//"` header; if a new file, give it its own header in the `DIRT-1` style. **Author:** Sonnet 5. **Scope:** `avatar` throughout. **Category:** `hustle` (clubhouse beat may use `baseball`; family fallout `family`). **~10 events.** Weights ≤ 0.25; everything cooldown-paced, one-shot-flagged via prereq/consequence flag pairs, or self-clearing.

### 5.1 Flag lifecycle (author to this table exactly)

| Flag | Introduced by | Read as gate by | Cleared by |
|---|---|---|---|
| `on_the_hook` | `dirt_the_ask` (engage), `dirt_made_useful` (engage) | `dirt_the_tip`, `dirt_shave_runs`, `dirt_turn_informant`, `dirt_walk_away_clean` | `dirt_walk_away_clean`, `dirt_turn_informant`, `dirt_league_investigation` (take the suspension) |
| `shaving_runs` | `dirt_shave_runs` (shave **or** throw) | `dirt_teammate_knows`, `dirt_league_investigation`, `dirt_turn_informant` | `dirt_league_investigation` (take the suspension), `dirt_turn_informant` |
| `threw_a_game` | `dirt_shave_runs` (throw only) | `dirt_permanent_ban` | `dirt_permanent_ban`, `dirt_league_investigation` (take the suspension — belt-and-braces) |
| **`banned_for_life`** (terminal) | `dirt_permanent_ban` | `dirt_blacklisted`, **(DIRT-4 + future `CareerManager` read — forward hook)** | *(never — terminal by design)* |
| `cooperated_with_league` | `dirt_turn_informant` (cooperate) | **(DIRT-4 "the door out at scale" — forward hook; optional in-batch reader is Sonnet's call)** | *(DIRT-4)* |
| `compromised_syndicate` **(EXISTING)** | `dirt_made_useful` (engage) **— new third writer** | `dirt_the_ask` (**new reader**) + shipped `syndicate_shakedown` | *(shipped syndicate arc)* |
| `syndicate_marked` **(EXISTING)** | `dirt_the_ask` (refuse), `dirt_turn_informant` (cooperate) **— new writers** | shipped `syndicate_enforcers` @ +14d | shipped `syndicate_enforcers` (pay off) |
| `in_the_life` **(from DIRT-1)** | *(DIRT-1)* | `dirt_made_useful` (entry gate) | *(DIRT-1's `dirt_clean_break`)* |

Every new `set_flag` is read by a prerequisite **or** is a disclosed forward hook listed here (`banned_for_life`, `cooperated_with_league`). No orphans; no dead-end sets. List both forward hooks in the batch header.

### 5.2 Event slots

Contract per slot: gate intent, branch shape, consequence *intent*. Prose, exact numbers, and `prompt`/`label`/`outcome`/`text_message` copy are Sonnet's craft. Contacts per §2 collision 3: syndicate/league-security/booking = `unknown`; lawyer = `agent_diaz`; clubhouse = `coach_reyes`; family = `family_mom`/`family_dad`; Sal go-between = `the_bookie` (optional, texture only).

**THE ASK (entry — two ramps into the same cascade)**

1. **`dirt_the_ask`** — *primary entry (they already own you).* Gate: `flag_active compromised_syndicate` (add `min_days_since ~30` so it can't land the same week the marker was sold), `field tier >= 2`, `flag_inactive on_the_hook`, `flag_inactive syndicate_marked` (a player already on the enforcer track is not on the fix track — clean separation), `flag_inactive banned_for_life`. Contact `unknown`. Weight ~0.12. Cooldown ~45 (for the *stall* branch to re-offer).
   - *hear them out* — `set_flag on_the_hook`, small `funds +`, `stress +`, `person_stat morality −`. The soft yes.
   - *tell them no* — `set_flag syndicate_marked` (→ shipped `syndicate_enforcers` @ +14d), `stress +big`. Refusing the people who own you is not free.
   - *stall* — `stress +`, no flag (buys time; cooldown re-offers).
2. **`dirt_made_useful`** — *second ramp / the CONVERGENCE (the pro who's known but not yet owned).* Gate: `flag_active in_the_life`, `flag_inactive compromised_syndicate`, `field tier >= 2`, `flag_inactive on_the_hook`, `flag_inactive syndicate_marked`. Contact `unknown` (with Sal optionally named as the messenger — he made a call up the chain). Weight ~0.10. Cooldown ~45.
   - *let them in* — `set_flag compromised_syndicate`, `set_flag on_the_hook`, `funds +`, `stress +`, `person_stat morality −`. **This is the keystone:** it mints `compromised_syndicate` at the pro tier, converging into both the `DIRT-2` fix cascade **and** the shipped `syndicate_shakedown` @ +365 — a third on-ramp to the deep end, mirroring `DIRT-1`'s "two on-ramps, one deep end" one tier up.
   - *stay clean* — `stress +small`, `person_stat discipline +`. They don't own you yet, so the no is cheap here (contrast `dirt_the_ask`). No mark.

**THE FIX (the ask escalates)**

3. **`dirt_the_tip`** — *low-stakes rung (leak, don't fix).* Gate: `flag_active on_the_hook, min_days_since ~10`, `field tier >= 2`, `flag_inactive shaving_runs`. Contact `unknown`. Cooldown ~21. Weight ~0.15. Feed inside info (a lineup, an injury) — pays a little, keeps you on the hook, never touches a result.
   - *pass the tip* — `funds +`, `stress +small`, `person_stat morality −` (keeps `on_the_hook`; does **not** set `shaving_runs` — the line hasn't been crossed).
   - *give them nothing* — `stress +small` (keeps `on_the_hook`; cooldown re-offers).
4. **`dirt_shave_runs`** — *the active fix (the line).* Gate: `flag_active on_the_hook, min_days_since ~14`, `field tier >= 2`. Contact `unknown`. Cooldown ~30. Weight ~0.25 (once you're on the hook, they press).
   - *shave a run* — `funds +` (real money now), `set_flag shaving_runs`, `stress +`, `person_stat morality −`. Underperform in a spot — abstracted, no sim touch.
   - *throw the whole game* — bigger `funds +`, `set_flag shaving_runs`, `set_flag threw_a_game`, `stress +big`, `person_stat morality −big`, `person_stat reputation −`. The deep end that unlocks the permanent ban.
   - *not this time* — `stress +`, keeps `on_the_hook` (still owned, just didn't deliver).
5. **`dirt_teammate_knows`** — *clubhouse pressure (texture).* Gate: `flag_active shaving_runs, min_days_since ~10`, `field tier >= 2`. Contact `coach_reyes` **or** `unknown` (a teammate). Category `baseball`. Cooldown ~30. Weight ~0.10. Someone in the room noticed the effort dip / is in on it too.
   - *bring him in / buy his silence* — `funds −`, `person_stat morality −`, `stress +` (keeps `shaving_runs`).
   - *deny it to his face* — `stress +`, `person_stat reputation −` (keeps `shaving_runs`). Pure texture; neither clears a gate → the cooldown is the pacing (check #4 holds).

**THE TEETH (getting caught)**

6. **`dirt_league_investigation`** — *the bust (survivable).* Gate: `flag_active shaving_runs, min_days_since ~21`, `field tier >= 2`, `flag_inactive banned_for_life`. Contact `unknown` (league security) / `agent_diaz` (the lawyer). Weight ~0.10 (the sword hanging). This is the primary mechanical-teeth beat.
   - *take the suspension* — `absence reason:"suspension", days: 20`, `funds −` (fine + legal), `clear_flag shaving_runs`, `clear_flag threw_a_game`, `clear_flag on_the_hook`, `set_flag served_suspension` (reuse the shipped flag), `person_stat reputation −big`, `stress +`. The costly survival — benched, but not banned.
   - *deny and stonewall* — `absence reason:"suspension", days: 30`, bigger `funds −`, keeps `shaving_runs` (the investigation grinds on; cooldown re-offers), `stress +big`, `relationship kind:"rival", affinity:-40, target:"league"`. Fighting it turns the league against you and costs more games.
7. **`dirt_permanent_ban`** — *the terminal (reserved for the deepest).* Gate: `flag_active threw_a_game, min_days_since ~30`, `field tier >= 2`, `flag_inactive banned_for_life`. Contact `unknown` / `agent_diaz`. Weight ~0.08 (low — the nuclear outcome, and until the choice UI ships this should rarely autopilot-fire; give the harshest branch the lowest `autopilot_weight`). Little real agency here by design.
   - *lawyer it down* — `absence reason:"suspension", days: 60` (effectively the season), `set_flag banned_for_life`, `clear_flag shaving_runs`, `clear_flag threw_a_game`, `clear_flag on_the_hook`, `relationship kind:"rival", affinity:-70, target:"league"`, `person_stat reputation −big`, `stress +big`. You reduce the games; the ban stands. **See §2 collision 2: this benches a season and stains the record — it does NOT itself end the career (unwired). Write the prose to that truth.**
8. **`dirt_blacklisted`** — *epilogue (reads the terminal flag → not an orphan).* Gate: `flag_active banned_for_life, min_days_since ~30`. Contact `family_mom` / `unknown`. Category `hustle` (or `family`). Cooldown ~60. Weight ~0.08. Life with the record — doors closed, the name means one thing now. Texture: `person_stat`, `stress`, small `funds`. This is where "crime can take the diamonds away — permanently" lands emotionally, and the natural pivot toward heir content. **Does not clear `banned_for_life`.**

**THE DOOR OUT**

9. **`dirt_turn_informant`** — *the hardest exit (cooperate before the bust catches you).* Gate: `(flag_active on_the_hook OR flag_active shaving_runs)` — express as **two events sharing consequences** per framework §3 (OR is not a single prereq), i.e. an `_a` variant gating `on_the_hook` and a `_b` variant gating `shaving_runs`; add `min_days_since ~30`, `field tier >= 2`, `flag_inactive banned_for_life`, `flag_inactive syndicate_marked`. Contact `agent_diaz` / `unknown` (a federal contact). Weight ~0.08. Cooldown ~45.
   - *cooperate* — `clear_flag on_the_hook`, `clear_flag shaving_runs`, `clear_flag threw_a_game`, `set_flag cooperated_with_league` (DIRT-4 forward hook), `set_flag syndicate_marked` (informing **is** defiance → shipped `syndicate_enforcers` @ +14d), `stress +` now with a delayed `text_message` relief beat, `person_stat morality +`. The honest out that still costs you a run-in with two men by your car. **Avoids the permanent ban** (you self-reported before the investigation landed) but not the syndicate's wrath.
   - *stay in* — `stress +`, keeps complicity (the pull to keep collecting).
10. **`dirt_walk_away_clean`** — *the soft out (never crossed the line).* Gate: `flag_active on_the_hook`, `flag_inactive shaving_runs`, `min_days_since ~30`, `field tier >= 2`, `flag_inactive banned_for_life`. Contact `coach_reyes` / `family_mom`. Weight ~0.08. Cooldown ~30.
   - *walk away* — `clear_flag on_the_hook`, `stress −`, `person_stat morality +`. You leaked a tip or two but never fixed a result, so you can still get out — **but `compromised_syndicate` persists** (the marker is still theirs → the shipped `syndicate_shakedown` @ +365 still looms). A real but partial escape; disclose the persisting flag.
   - *one more meeting* — `funds +small`, `person_stat morality −`, keeps `on_the_hook` (the pull back in; cooldown re-offers). Mirrors `DIRT-1`'s `dirt_clean_break`/*one last score*.

### 5.3 Pacing & load-order note

Same discipline as `DIRT-1` (arc doc §5.3): the underworld batch sorts after the weight-1.0 onboarding chains, and the per-subject one-fire-per-day cap + file-order tiebreak mean those always win their slots. No `DIRT-2` weight exceeds 0.25. Every fix-cascade event additionally gates `tier >= 2`, so **none can hold at tier 0/1** — they are structurally unreachable during the HS onboarding phase that's in playtest, a stronger guarantee than `DIRT-1`'s `funds`/`hs_started` belt-and-braces. Verify no `DIRT-2` gate can hold on a fresh tier-0 save at any day. `min_days_since` on `compromised_syndicate` (~30) at `dirt_the_ask` keeps the ask off the heels of the compromise.

---

## 6. Contacts

**No new contact row.** `DIRT-2` reuses `unknown` (syndicate/league/booking — anonymity principle), `agent_diaz` (pro-era lawyer, already the roster-availability legal voice), `coach_reyes`, `family_mom`/`family_dad`, and optionally `the_bookie` (Sal) as a familiar go-between. Zero `contacts.json` / `ContactRegistry.cs` touch. If a distinct `criminal`/`fixer` role is ever wanted for tone, that remains the optional one-line enum-case add disclosed in the arc doc §6 — **not** required here.

---

## 7. Gates & acceptance (Fable's review)

Pure content — no schema, no scene, no `Simulation/` touch. Gate set (extends arc doc §7):

1. **`dotnet build`** 0/0 (a malformed batch fails the loader loudly at boot).
2. **`GrittyEventsHarness`** — `check_event_graph_integrity` over the whole `Content/` folder passes. Bump the whole-folder **event-count literal** by the `DIRT-2` count (post-`DIRT-1` baseline is **90** events → **~100**). Contact-registry unresolved pin stays **0** (no new contact).
3. **New harness checks to add** (batch-specific proofs, same pattern as `RunTutorialArcChecks`/the `DIRT-1` cascade checks):
   - **Both ramps reach the fix cascade:** (a) seed `compromised_syndicate` + `tier = 2` → `dirt_the_ask` eligible; engage → `on_the_hook` set. (b) seed `in_the_life` (no `compromised_syndicate`) + `tier = 2` → `dirt_made_useful` eligible; *let them in* → `compromised_syndicate` **and** `on_the_hook` set → assert **both** the fix cascade **and** the shipped `syndicate_shakedown` become eligible at +365 (the third-writer convergence — the `DIRT-1` proof, one tier up).
   - **The fix reaches the teeth:** `on_the_hook` → `dirt_shave_runs`(*shave*) → `shaving_runs` → `dirt_league_investigation` eligible at +21 → *take the suspension* produces a `suspension` absence with the correct `until_day = fire_day + 20 + 1` and clears `shaving_runs`/`on_the_hook`/`threw_a_game`.
   - **The terminal path:** `dirt_shave_runs`(*throw*) → `threw_a_game` → `dirt_permanent_ban` eligible at +30 → sets `banned_for_life` → `dirt_blacklisted` eligible (proving `banned_for_life` is **read**, not orphaned) and a season-length suspension is applied.
   - **Refusal / informing routes to the shipped enforcers:** `dirt_the_ask`(*refuse*) sets `syndicate_marked` → shipped `syndicate_enforcers` eligible at +14. Same for `dirt_turn_informant`(*cooperate*).
   - **The doors clear the right flags:** `dirt_turn_informant` clears `on_the_hook`/`shaving_runs`/`threw_a_game` + sets `cooperated_with_league`; `dirt_walk_away_clean` clears `on_the_hook` **but leaves `compromised_syndicate` set** (assert the marker persists).
   - **`tier` gate safety:** assert every fix-cascade event is inert at `tier = 0` and `tier = 1`, and that an unrostered (NULL-team, sentinel −1) subject satisfies none of them.
   - **Standing checks:** no unpaced loop (#4 — every event either self-clears on a branch or carries an explicit cooldown; enumerate the cooldown-paced ones as `DIRT-1`'s header did), no orphan flag (#2/#3 — `banned_for_life`/`cooperated_with_league` are the two disclosed forward hooks).
4. **Calibration-inert:** `git diff --stat -- Assets/Simulation` **empty**. `run_monte_carlo_batch` not mandated; if run, MLB guard byte-exact. CoreLoop / NeedsDecay / Schema unaffected.
5. **Live boot** (godot MCP) on the real save: batch loads, dispatcher logs the new folder count, errors empty, save left un-advanced so the beats stay fresh for the user's playtest. If the avatar isn't yet tier ≥ 2 in the live save, seed `compromised_syndicate`/`tier` on a scratch copy for the cascade smoke-test rather than advancing the playtest save.

---

## 8. Boundary disclosures (carry into the batch header)

1. **`banned_for_life` does not end the career** — `CareerManager` reads no flags today; wiring it (block promotion / force retirement) is a future `Simulation/Baseball` engine task, out of scope. Teeth here = season suspension + `rival/league` + `dirt_blacklisted`.
2. **`cooperated_with_league`** is a forward hook for `DIRT-4` (the plea-at-scale gives cooperators a better deal). Optional in-batch reader is Sonnet's call; if none, it's a disclosed forward-hook set (legal per check #5).
3. **The fix is abstracted** — no `Assets/Simulation/` touch; "throw a game" is a consequence bundle + complicity flag, not a sim hook.
4. **File placement** (append to `dirt_underworld_events.json` vs. new `dirt_the_fix_events.json`) is Sonnet's call — disclose which; both load identically.
5. **`syndicate_marked` re-entry** — `DIRT-2` is a new writer; the fix cascade gates `flag_inactive syndicate_marked` so a player already on the enforcer track can't be double-marked. Disclosed as intentional separation.

---

## 9. Model split & follow-ons

- **Opus 4.8 (this pass):** this spec; `DIRT-3`/`DIRT-4` full specs later.
- **Sonnet 5 (next):** author the ~10 `DIRT-2` events to §5's contract (prose, numbers, `prompt`/`label`/`outcome`/`text_message`), extend the `GrittyEventsHarness` with §7.3's checks + the count-literal bump, run `check_event_graph_integrity`. No `contacts.json` touch.
- **Fable 5 (review):** §7 gate set + the standing build review; sign-off; live boot.
- **Deferred (not this arc):** wiring `banned_for_life` into `CareerManager` (engine); the `cooperated_with_league` reader (`DIRT-4`); the `detection_risk`/`ped_active` shared-heat re-gate (`DIRT-4`); the avatar event-choice UI (these `scope: avatar` beats autopilot-resolve until it ships — the fix decisions are the strongest customer yet for that surface, and §7.3 already pins the low `autopilot_weight` on the nuclear branches so autopilot rarely self-selects a ban).
