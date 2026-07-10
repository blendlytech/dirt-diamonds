# High School Arc — Manual Playtest Checklist (plan §Verification-5)

The user-driven exit gate for the HS-0…HS-6 arc (`docs/design/high_school_person_layer.md`). Every automated gate is green (MC 345/345, GrittyEvents 190/190, SchemaValidator 106/106 @ v12, NeedsDecay 94/94, CoreLoop 22/22); this checklist covers what only human eyes can verify — screens rendering sensibly, flows feeling right, and the probability-gated content actually firing in live play.

**Setup:** build from a clean tree (`dotnet build DirtAndDiamonds.sln -c Debug`, expect 0/0). Sessions A–E want **fresh saves**; the existing real save (v12, day 99) works for B/C but has no child and mid-arc flags. Saves live under `%APPDATA%\Godot\app_userdata\Dirt & Diamonds\`. DB spot-checks (optional, marked 🗄) can run through the SQLite MCP or `sqlite3` against the save's `.db`.

Tick items as they pass. Anything that fails or feels wrong: note the day number and what was on screen — day + save file is usually enough to reproduce.

---

## Session A — Creation & the wealth divergence (two fresh saves)

Create **two avatars back-to-back**: one poorest background, one wealthiest (NewGameScreen → BackgroundRow). Same role/ratings otherwise.

- [ ] NewGameScreen shows the trait picker (TraitsRow) and background selector; backstory reveal renders and **re-roll** produces a different backstory.
- [ ] Avatar starts **age 16**, and the BaseballDashboard tier chip reads **FR** (freshman).
- [ ] **Phone divergence:** open the BurnerPhone on each save — hardware tier, plan, and starting minutes differ by wealth (poor = low tier/tight minutes, wealthy = better).
- [ ] **Allowance divergence:** funds trickle differs across the first few days between the two saves.
- [ ] **Transport divergence:** the wealthy save starts with (or is auto-gifted) better transport; ScheduleScreen's Transport line reflects it ("Transport: X h/day saved") while the poor save shows the "none" fallback.
- [ ] 🗄 `Family_Background` row exists for the avatar; `strictness` populated; `Player_Person` row is present (neutral 50s + trait shifts).

## Session B — A school week (either save, or the real save)

Plan a full week via ScheduleScreen. Include School hours daily, and rotate the free-time block (FreeTimeRow + FreeTimeActivityRow): **Study** one day, **Church** one day, **Video Games** one day, **Hangout** one day.

- [ ] Free-time slider + activity dropdown confirm cleanly; the plan line shows the chosen activity and hours; over-allocation past 24h disables Confirm.
- [ ] The GPA/person-stat line (GPA, Int, Disc, Happy) updates after day ticks — Video Games nudges Happiness up, Study nudges it down slightly.
- [ ] **GPA moves on the weekly beat** (every 7th day), not daily. Full attendance + study hours → GPA up; it starts at 2.50.
- [ ] **Truancy test:** plan one day with 0 School hours — next weekly tick shows the GPA drag.
- [ ] Transport refund: with a bike/car owned, planned days show the extra evening hour(s) ("banked" carry for the bike's 0.5/day).
- [ ] 🗄 `Player_Person` after the week: gpa moved off 2.5; happiness/charisma/morality/discipline drifted in the directions above.

## Session C — Marketplace & the item economy

- [ ] BurnerPhone → **Marketplace** tab lists the catalog with prices; buying deducts funds; owned items marked.
- [ ] **Item buy → social_status:** buy a status item (designer clothes / jewelry tier) — the effective social_status buff registers (🗄 `Player_Items` row lands; dating/social events key off the buffed value).
- [ ] Buying transport upgrades the ScheduleScreen Transport line immediately (event-driven re-projection, no restart needed).
- [ ] Parental autobuy (wealthy save): a gifted item appears without the avatar spending.

## Session D — Dating funnel → sneak-out → breakup/rekindle

Fresh save, age 16, let days tick (autopilot or light play). Expected pacing: `hs_crush_forms` seeds the funnel, `hs_ask_out` resolves within days of the crush; `hs_breakup` is weight 0.05 / cooldown 90, so a full funnel pass is a season-scale test.

- [ ] `hs_crush_forms` → `hs_ask_out` fires; accepting mints the sweetheart ("Riley"-role contact) and the Messages thread reflects it.
- [ ] **Parental approval gate:** `hs_parental_approval` fires **only** on a strict household (strictness ≥ 55). A lenient save never sees it — that's the gate working, not a missing event.
- [ ] **Sneak-out split:** high-recklessness avatar (≥ 60) eventually gets `hs_caught_sneaking_out`; low-recklessness gets `hs_snuck_out_clean`. (The cliff is deterministic at 60 by design.)
- [ ] Breakup (chosen or autopilot): the relationship actually ends — a later `hs_rekindle` (needs `hs_ex` ≥ 30 days, fires only while single) can restore it via "reach out".
- [ ] 🗄 `Entity_Flags`: `hs_dating` / `hs_ex` toggle correctly across the arc; `Game_State.avatar_ex_partner_id` set after an avatar breakup.

## Session E — Pregnancy → birth → Family tab (the never-clicked path)

Continue a save with an active `hs_dating` relationship. `hs_pregnancy_scare` → `hs_pregnancy_decision` (keep) → 270-day gestation → `hs_baby_born`.

- [ ] `hs_baby_born` fires ≈270 days after the keep decision and the **BirthNotificationScreen** presents it. (Single choice on the birth event is the shipped precedent, not a bug.)
- [ ] **BurnerPhone → Family tab with a real child** — this card has never rendered populated. Child listed; care/coaching/funding commitment controls usable.
- [ ] **ScheduleScreen FamilyRow:** family hours plannable; they count into the 24h budget; commitment persists across days.
- [ ] Rearing events (8-event batch, gated on `hs_has_child`) start appearing; choices visibly move the axes (🗄 `Child_Development` row: care/coaching/funding/neglect move off 50/50/50/0).
- [ ] 🗄 `Child_Rearing_Commitment` row exists and matches the Family tab settings.

## Session F — Grade progression & sim feel (long-horizon, background checks)

These ride whatever save gets deepest — check at season rollovers rather than dedicating a run.

- [ ] Tier chip advances FR → SO → JR → SR with age; four amateur seasons guaranteed (no promotion sweep while grade-gated).
- [ ] **Graduation on merit:** a strong senior promotes HS → College; a weak senior is **held back** (stays HS, retries) — not force-promoted.
- [ ] Attended games play clean with person levers live (PersonLedger refresh at game start): no errors, no stutter at PlayGame. Effects are ±2-rating subtle — "nothing broke" is the pass, not a visible stat swing.
- [ ] **NPC autonomy drift:** 🗄 `SELECT COUNT(*) FROM Relationships` week over week — count grows organically (mints), some NPC romances/breakups appear, and **no avatar or Child edge ever changes without the avatar acting** (conservation).
- [ ] **Heir nurture at takeover:** a doted-on child (high care/coaching/funding) shows raised interest + shifted potential on the SuccessionScreen at takeover; a neglected one shows the penalty. Neutral rearing = exactly the pre-HS numbers.
- [ ] Pro-era: GPA freezes once past HS (by design); `marriage_on_the_rocks` → `divorce_papers` arc reachable in a married pro save.

---

## Known behaviors that are NOT bugs (arc disclosures)

- Autopilot games simulated inside the same day-tick see the **previous** pump's person levers (one-pump lag on the skipped path only).
- A jailed avatar credits default school attendance for those days (≤ a few days, ~0 GPA impact).
- Mid-week saves reset the partial attendance/study accumulators (both sides together — the fraction stays fair).
- Plan line renders "Idle 0h" for a zeroed free-time block — cosmetic.
- `hs_hometown_anchor`'s long-distance thread has **no real weekly minute drain yet** (open seam); `hs_clubhouse_cancer` is narrated, not verified against a real teammate-ex (open seam).
- Divorce while `expecting` → single-parent birth: supported by design.
- Background league (macro tiers) picks up person-stat movement only at season rollover — season-stable by design; only attended games refresh per-game.

## If something fails

Note the in-game day, the screen, and which save. The autonomy/rearing/dating **constants are first-pass tunable data** — pacing complaints (too many breakups, GPA too swingy, rearing too slow) are data edits + a mandatory harness re-run, not engine work. Genuine defects: anything crashing, a gate firing when its prerequisite says it can't, avatar/Child edges moving on their own, or funds/stats moving without a cause on screen.
