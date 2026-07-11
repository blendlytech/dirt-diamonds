# High School Arc — Manual Playtest Checklist (plan §Verification-5)

The user-driven exit gate for the HS-0…HS-6 arc (`docs/design/high_school_person_layer.md`), the Act-1 onboarding fix (`docs/design/hs_onboarding_events.md`), and the day-1 phone tutorial (`docs/design/day1_phone_tutorial.md`). Every automated gate is green (MC 345/345, GrittyEvents **277/277** (83 events), SchemaValidator 111/111 @ v13, NeedsDecay 102/102, CoreLoop 22/22); this checklist covers what only human eyes can verify — screens rendering sensibly, flows feeling right, and the probability-gated content actually firing in live play.

**Setup:** build from a clean tree (`dotnet build DirtAndDiamonds.sln -c Debug`, expect 0/0). Sessions A–E want **fresh saves**; the existing real save (v12, day 99) auto-migrates to v13 on next boot (purely additive — new `Relationship_History` table only, same pre-v12 no-row-fallback precedent) and works for B/C but has no child and mid-arc flags. Saves live under `%APPDATA%\Godot\app_userdata\Dirt & Diamonds\`. DB spot-checks (optional, marked 🗄) can run through the SQLite MCP or `sqlite3` against the save's `.db`.

Tick items as they pass. Anything that fails or feels wrong: note the day number and what was on screen — day + save file is usually enough to reproduce.

---

## Session A — Creation & the wealth divergence (two fresh saves)

Create **two avatars back-to-back**: one poorest background, one wealthiest (NewGameScreen → BackgroundRow). Same role/ratings otherwise.

- [ ] NewGameScreen shows the trait picker (TraitsRow) and background selector; backstory reveal renders and **re-roll** produces a different backstory.
- [ ] Avatar starts **age 16**, and the BaseballDashboard tier chip reads **FR** (freshman).
- [ ] **Phone divergence:** open the BurnerPhone on each save — hardware tier, plan, and starting minutes differ by wealth (poor = low tier/tight minutes, wealthy = better).
- [ ] **Allowance divergence:** funds trickle differs across the first few days between the two saves.
- [ ] **Transport divergence:** the wealthy save starts with (or is auto-gifted) better transport; plan a day with an away-block (e.g. Work) on each save and confirm ScheduleScreen's Travel line shows fewer hours/trip on the wealthy save than the poor one.
- [ ] 🗄 `Family_Background` row exists for the avatar; `strictness` populated; `Player_Person` row is present (neutral 50s + trait shifts).

## Session A2 — HS onboarding arc (Epic 5, the Act-1 fix)

Fresh save, either avatar from Session A. Let days tick (autopilot or light play) from day 2. This replaces the old "expect zero week-1 events" expectation — that was the bug (a fresh freshman got nothing narratively for its opening weeks); the onboarding arc is the fix. (An existing pre-fix save works too: the avatar had provably never received ANY event — see the dispatcher starvation fix in the Stage-3 review entry — so `hs_first_day` arrives on the first day-advance after this build, whatever the calendar says.) **Note the one-day shift from the day-1 phone tutorial (Session A2b): `hs_first_day` now lands day 3, not day 2 — the tutorial's `tut_welcome` wins day 2 outright.**

- [ ] **Days 3–14 deliver the onboarding beats on the BurnerPhone in roughly chain order:** first day (Mom) → meet the coach → tryouts → first practice → a lunchroom/social beat (a classmate, "Marcus") → first-game nerves. Roughly one beat per day, tapering off after about day 9-10. (A `hs_crush_forms` dating-funnel text may interleave on some days — that's the documented ~5%/day romance overlap, not a bug; it never drops an onboarding beat, only occasionally shifts one by a day. The tutorial's own beats also interleave on the odd days through day 8 — see Session A2b — that's by design, not a competing bug.)
- [ ] **HARD FAIL if seen:** any rookie/pro-clubhouse content ("Welcome to the show, kid," rookie karaoke/hazing, a first pro paycheck, a motel/road-trip beat) appearing while the tier chip still reads HS. This content must be impossible below College tier — any appearance means the tier gate broke.
- [ ] **Report card ~day 28-29** (three weeks after the first game): a *solid* report card from Mom (the typical case, GPA still at or above the 2.5 default) **or** a coach's eligibility warning from Coach Malone (only if the player has actively tanked their GPA below 2.5 by then). Exactly one of the two ever fires, never both.
- [ ] Around day 21-22ish, a crosstown-rival text seed (a classmate showing you a rival shortstop running his mouth) may appear; around made-team+45 days, a homecoming beat.
- [ ] 🗄 `Entity_Flags`: `hs_started` → `hs_met_coach` → `hs_made_team` → `hs_practiced` → `hs_debut_done` set in that order across the early days; `got_report_card` set once, around day 28-29.
- [ ] **BurnerPhone "Events vs Messages" split:** the phone's FIRST tab is now **Events** — scene-prose cards (day/season, contact name, prompt, then a resolution line once you've answered) in one scrolling feed, oldest to newest. An unanswered choice (e.g. `hs_tryouts`) renders as the last card with reply-chip buttons underneath it, right there in the feed — no separate popup. **Messages** is the second tab and now holds ONLY real companion texts (no scene prose, no choice buttons): Mom's "Knock 'em dead today, kiddo." should land in Messages the same day `hs_first_day` resolves in Events; Coach Malone's tryouts verdict ("Roster's up. You're on it.") should NOT appear the moment you pick a tryouts choice — it lands in Messages the *next* day.

## Session A2b — Day-1 phone tutorial (`docs/design/day1_phone_tutorial.md`)

Same fresh save as A2 — the two arcs interleave, so play them together.

- [ ] On a fresh save, **day 2 opens the "Getting Started" thread** (the phone welcome, `tut_welcome`) in Events, then **day 3 delivers `hs_first_day`** (first day of school, Mom). Over days 2–8 the tutorial tips and the story beats **alternate**, roughly one card per day (`tut_welcome`@2, `hs_first_day`@3, `tut_daily_loop`@4, `hs_meet_coach`@5, `tut_game_day`@6, `hs_tryouts`@7, `tut_wrap`@8) — the Getting-Started thread accumulates in **Messages** and stays there, revisitable any time.
- [ ] The **skip choice** on `tut_welcome` ("I've played before — skip the tips") dismisses the rest of the tutorial in one tap — no further Getting-Started cards appear, and the narrative onboarding arc is unaffected.
- [ ] A gen-2 heir (a fresh tier-0 avatar on a later playthrough) sees exactly **one** Getting-Started welcome card, dismissible the same way — not the whole thread again.
- [ ] The tutorial's consequences are flags only — confirm no funds/stress/person-stat change results from any tutorial choice (🗄 `Player_Person`/funds unchanged by tutorial answers alone).

## Session B — A school week (either save, or the real save)

Plan a full week via ScheduleScreen. Include School hours daily, and rotate the free-time block (FreeTimeRow + FreeTimeActivityRow): **Study** one day, **Church** one day, **Video Games** one day, **Hangout** one day.

- [ ] Free-time slider + activity dropdown confirm cleanly; the plan line shows the chosen activity and hours; over-allocation past 24h disables Confirm.
- [ ] The GPA/person-stat line (GPA, Int, Disc, Happy) updates after day ticks — Video Games nudges Happiness up, Study nudges it down slightly.
- [ ] **GPA moves on the weekly beat** (every 7th day), not daily. Full attendance + study hours → GPA up; it starts at 2.50.
- [ ] **Truancy test:** plan one day with 0 School hours — next weekly tick shows the GPA drag.
- [ ] Travel cost: a School day (plus any Work/Hangout/Church hours) shows a "Travel today" line whose hours shrink once a bike/car is owned; a day with everything at home (e.g. Study free time, no Work) shows no Travel line at all.
- [ ] 🗄 `Player_Person` after the week: gpa moved off 2.5; happiness/charisma/morality/discipline drifted in the directions above.

## Session C — Marketplace & the item economy

- [ ] BurnerPhone → **Marketplace** tab lists the catalog with prices; buying deducts funds; owned items marked.
- [ ] **Item buy → social_status:** buy a status item (designer clothes / jewelry tier) — the effective social_status buff registers (🗄 `Player_Items` row lands; dating/social events key off the buffed value).
- [ ] Buying transport shrinks the ScheduleScreen Travel line immediately, with no restart needed (event-driven re-projection) — only visible on a plan with an away-block scheduled.
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
- `hs_hometown_anchor`'s "commit to long-distance" branch now bills 20 min/week via the family tick (closed 2026-07-09, Sonnet 5 — `FamilyService.IsCommittedLongDistance`, gated on `long_distance` + `hs_dating` both active so an ex from the "grow apart" branch is never billed; Wi-Fi bypasses it like any metered action). `hs_clubhouse_cancer` now graph-reaches its premise (closed 2026-07-09, Sonnet 5 — schema v13 `Relationship_History`, `SubjectField.TeammateExOfPartner`, `RelationshipTargetSelector.TeammateExOfPartner`): it only fires when a real teammate has ex-history with the current partner, and its rival/friend consequence lands on that specific teammate, never a random pool draw.
- Divorce while `expecting` → single-parent birth: supported by design.
- Background league (macro tiers) picks up person-stat movement only at season rollover — season-stable by design; only attended games refresh per-game.
- **Gen-2 refire (the onboarding arc):** `Entity_Flags` are per-player, so once `Succeed` creates a new age-16 heir, that heir's `hs_started`/`hs_met_coach`/etc. are all unset — the heir gets its own onboarding arc from scratch, re-living day one of high school. This is desired, not a leftover flag bug.
- **A duplicate beat if the phone goes unanswered:** an unresolved pending choice doesn't set its flags until it resolves, so a no-cooldown weight-1.0 beat (e.g. `hs_first_day`) can re-fire the next day, which forfeits the first fire to autopilot (its flags then land) and pends the repeat — the thread may show the same beat twice, then the chain proceeds at a one-day lag. Answering beats the day they arrive avoids it entirely. Existing forfeit semantics, self-healing, not a stall; if it grates in play, small `cooldown_days` on the week-one beats is a pure data edit.
- A pre-fix save that already has `rookie_settled` active (from the old bug) heals with zero DB surgery: the rookie batch's new `tier >= 2` floor blocks it at HS/College tier regardless, and the onboarding chain starts normally on next boot since its own flags were never set.

## If something fails

Note the in-game day, the screen, and which save. The autonomy/rearing/dating **constants are first-pass tunable data** — pacing complaints (too many breakups, GPA too swingy, rearing too slow) are data edits + a mandatory harness re-run, not engine work. Genuine defects: anything crashing, a gate firing when its prerequisite says it can't, avatar/Child edges moving on their own, or funds/stats moving without a cause on screen.
