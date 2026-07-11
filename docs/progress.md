# Progress & Next Steps

Session-orientation file for the AI models. **Keep it lean:** at session end, append a COMPACT entry to the Session Log (what shipped, commit, gates, next step + model — a few lines, not a blow-by-blow). Full detail belongs in [progress_archive.md](progress_archive.md). When the Session Log grows past ~30 entries, move the oldest to the archive.

*(Archived: Phases 0–12, the HS Grade System, the full High School arc HS-0…HS-6, Act 1 onboarding, the phone Events/Messages split, the HS calendar slice, and UI slices A/B — all in [progress_archive.md](progress_archive.md).)*

---

## Current Status (2026-07-11)

**All prior arcs COMPLETE and committed.** Current focus: the `docs/game-idas/GAME_IMPROVEMENTS.md` UI/content roadmap (Slices A+B shipped; D–G, tutorial, content arc, hustle minigames remain — see Roadmap below).

- **Gate suite (all green as of Slice B `a7bd114`):** MonteCarlo **345/345** (MLB bit-identity guard PA 48384 / H 10969 / ER 5237, byte-exact), GrittyEvents **256/256** (79 events), CoreLoop **22/22**, NeedsDecay **102/102**, SchemaValidator **111/111** — schema **v13**.
- **Live save:** a fresh day-1 avatar (the old day-119 checklist save is superseded).
- ⚠ **Session hygiene:** an auto-committer is active AND parallel sessions occur — run `git status` + `git log` FIRST every session; never trust a stale "uncommitted" label.

### Standing workflow rules

- **Model roles:** **Opus** = design docs/specs. **Sonnet 5** = content batches, data/constants edits, UI builds. **Fable 5** = engine + anything touching the calibrated core, and the standing review of every Sonnet build.
- Any `Assets/Simulation/` touch ⇒ mandatory `run_monte_carlo_batch` re-run; the MLB bit-identity guard must stay byte-exact unless a re-band is explicitly sanctioned.
- Content batches gate through `check_event_graph_integrity`; schema changes through `validate_sqlite_schema`; playtest pacing/copy complaints = data edits (Sonnet) + harness re-run.

---

## ROADMAP — remaining work (dependency-ordered, model-tagged)

Source: `docs/game-idas/GAME_IMPROVEMENTS.md` (user-refreshed 2026-07-11). Already resolved from that doc: text messages = DONE; item-popup removal = MOOT (no popup infra exists); autosave-per-event = DONE by architecture (surfaced in Slice B's note).

| # | Slice | Model flow | Notes / dependencies |
| --- | ----- | ---------- | -------------------- |
| 1 | **Slice D — UI audio foundation** | Opus spec → Fable build | Greenfield (zero audio infra): autoload `UiSfx`, pooled `AudioStreamPlayer`s, placeholder tones, button/tab wiring. Unlocks Slice B's parked volume slider. |
| 2 | **Tutorial phone thread** (day-1 onboarding in the cellphone) | Opus outline → Sonnet content → Fable review | Reuses the onboarding-event machinery as a day-1 contact/thread. Can run before or parallel to D — user's call. |
| 3 | **Slice G — real-time clock + time pause/speed** | **Opus design DONE + UNBLOCKED** (`docs/design/real_time_clock_slice_g.md`) → **Fable build next** | Metronome model: cosmetic intraday clock drives the unchanged discrete `AdvanceDay`; sim stays day-atomic. **Persistence DECIDED: Option B** — `time_of_day_minutes`+`time_speed` as Game_State **KV keys** (NOT DDL: no `user_version` bump, no SchemaValidator change, absent-key default=08:00). Sub-slices G-1 (model+KV+gate) → G-2 (TimeControlBar) → G-3 (phone time) → G-4 optional. |
| 4 | **Slice E — phone tier visual theming** | Opus spec → Fable build | Per-tier bezel/screen themes off the Phone_State tier BurnerPhone already caches. |
| 5 | **Slice F — UI animation/immersion pass** | Opus motion spec → Fable build | Tab/message tweens, funds ticker, "different UI per location". Depends on A (done) + D. |
| 6 | **Content arc — Story/Player/NPC sections** | Opus narrative spec → Sonnet batches → Fable review | User decision: starts AFTER the UI slices. Every batch gates through `check_event_graph_integrity`. |
| 7 | **Hustle minigames** — Texas Hold'em, narcotics stages, fencing, robberies | Opus spec per minigame → build → Fable review | `Tools/HoldemHarness` already exists as the Hold'em math core; `gritty_events.md` Hustle State Machine rules apply. After D–F unless re-prioritized. |

**Follow-ups riding playtest verdicts (not slices):**

- **Development practice-lever retune** — the calendar slice's top seam: max ~78 practice h/season vs the 500h e-fold conversion. **Sonnet 5** data edit + mandatory MC re-run, after the user's calendar-pacing verdict.
- **At-Bat Read-Input model** — code-complete + signed off; open: §7.4 manual playtest (user) → then the re-band (**Fable 5**).
- **College/pro tier calendars** (if wanted after HS pacing verdict) — Sonnet build → Fable review, same `TryGetLeagueScheduleDay` seam.
- Parked small seams for any Sonnet slot: college batch reads `hs_scouted`; pro-arc rekindle content; 3 deferred 12c cleanups; MC-harness FAILED-recap-on-tail.

## VERY NEXT STEPS

1. **User plays** (the only open exits, all user-driven):
   - Consolidated UI + Slice B: top bar day+funds, phone Calendar tab + status-bar clock, school mandated-note row, Settings → Save Now shows "Saved ✓ — day N". *(The `847d676` Player/League phone tabs are user-confirmed — layout renders correctly.)*
   - Checklist **Session A2** (`docs/hs_manual_playtest_checklist.md`): Events tab heads with categories (Baseball/Family/…), Mom's same-day text, Malone's next-day tryouts verdict; eyeball the feed auto-scroll-to-newest.
   - Calendar pacing: browse March 1 (season start) + a summer week; confirm ScheduleScreen's locked rows match CalendarScreen.
   - Longer-standing: checklist Sessions B–F; At-Bat §7.4 playtest.
2. **First build slot after that:** Slice D audio (needs the Opus spec first) **or** the tutorial thread (needs the Opus outline first) — **user picks priority**. Slice G's Opus design doc can be commissioned any time; it blocks nothing else.

---

## Session Log (compact — full detail in the archive)

- **2026-07-11 — Slice G design doc (Opus) — DONE + UNBLOCKED:** `docs/design/real_time_clock_slice_g.md`. Core decision: the intraday clock is a **presentation/driver-layer metronome** — a pure `GameClock` (Core, Godot-free) accumulates real-time→game-minutes and, on midnight, calls the **unchanged** `Clock.AdvanceDay()`; the sim gains NO hour/minute resolution (rejected re-plumbing thousands of entities). Play/Skip reconcile losslessly: Skip Day retained as instant step; continuous clock = paced/interruptible Skip; "Play Game" becomes the contextual at-bat launch. Safety-critical seam = promote the dashboard's private day-advance-blocked predicate to a shared `GameManager.CanAdvanceDay` (auto-pause on any decision gate); one-roll-per-frame back-pressure. Reframe surfaced: 1min=1sec ⇒ 24 real-min/day, so the clock is ambient/tactical, NOT generational traversal (that stays discrete Skip + offseason fast-sim). **User chose Option B (persist time-of-day) — and it's cheaper than first framed: `Game_State` is KV, so `time_of_day_minutes`+`time_speed` are new KV KEYS (like `avatar_practice_credit`), NOT a DDL change — no `user_version` bump, no SchemaValidator change, no migration (absent-key → 08:00 default); checkpoint-only writes via existing `SaveNow`/`_ExitTree`.** Sub-slices G-1 (pure model + KV seed/restore + shared gate) → G-2 (TimeControlBar replaces Play/Skip) → G-3 (phone time-of-day) → G-4 optional "pause each morning". No `Simulation/` touch ⇒ MC inert. **Next: Fable builds G-1.**
- **2026-07-11 — `847d676` verification (Fable 5):** the owed gate record for the parallel-session PlayerScreen/LeagueScreen extraction. `git status` clean at `b68454f` (the doc/skill commit atop it), diff scope confirmed pure `Assets/UI/` + `Main.cs` wiring (no sim/schema touch ⇒ no MC re-run mandated). **Build 0/0** (game + all 6 harness projects), **live boot (godot MCP) on the real save clean:** schema v13, WAL, FK on, day 1 fresh avatar, 48 teams, 819 life-sim NPCs, 79 gritty events dispatcher polling, ~18 s run, **errors empty**. Known-benign: Steam DLL-absent notice (dev environment, pre-existing). **User eyeball same day: Player/League tab layout confirmed correct.** `847d676` is fully closed; UI work may layer on it.
- **2026-07-11 — PlayerScreen/LeagueScreen extraction (`847d676`, parallel session):** dashboard scouting card + stat panels moved into self-contained phone tabs; shipped without a gate run — gate-verified AND user-confirmed live same day (entry above). **CLOSED.**
- **2026-07-11 — Slice B Save+Options (`a7bd114`, Fable 5):** `GameManager.SaveNow()` checkpoint intent + phone Settings tab (Save Now, "Saved ✓ — day N", parked volume slider). All gates green; NeedsDecay 102/102.
- **2026-07-11 — Travel-time checkpoint (`02177f1`, another session's build):** per-trip `TravelTime` + `DaySchedule.TravelHours` + planner forcing; gate-verified then committed pre-B (NeedsDecay 94→102).
- **2026-07-11 — Slice A UI consolidation (`67c9f7b`, Fable 5):** TopBar day+funds, CalendarScreen → phone tab, phone status-bar clock, School slider → mandated note folded into the hours budget.
- **2026-07-11 — HS calendar slice (`b1bc77d`, Fable 5):** sparse 26-game Tue/Fri spring season, Mon–Fri school with breaks, calendar-forced planner blocks, browsable CalendarScreen; MC 345/345, HS band held, MLB guard byte-exact.
- **2026-07-10 — Phone Events/Messages split + per-choice outcomes/delayed texts + category headings + History tab (Sonnet 5 builds, Fable 5 reviews SIGNED OFF):** GrittyEvents 232→256; §4.3 never-gates invariant held; feed capped at 200 cards.
- **2026-07-10 — Act 1 onboarding arc (Opus design → Sonnet build → Fable review) + school-life batch:** `SubjectField.Tier`/`.Gpa`, 11-event onboarding chain, rookie batch `tier>=2` re-gate, +13 school-life events (79 total). **Critical dispatcher fix: avatar hoisted to sweep front — it had been starved of ALL gritty events for 112 days by scope:any NPC saturation of `MaxFiresPerDay`.**
- **2026-07-08…10 — High School arc HS-0…HS-6 + every disclosed seam closed (schema v11→v13):** person layer + backstory/family + phone/marketplace/items economy + GPA drift + dating/pregnancy/child-rearing + NPC autonomy + PersonEffects sim layer + PersonLedger. MC guard byte-exact throughout; the planned HS-6 re-band moved NO band. Exit: user plays `docs/hs_manual_playtest_checklist.md`.
- **2026-07-08 — HS Grade System + freshman start age 16 (`a3fc6cb`-era):** grade derived from age, merit-based graduation, held-back mechanic.
- **Pre-2026-07-08 — Phases 0–12 (see archive):** engine foundation, all harnesses, At-Bat Read-Input model (code-complete, playtest pending), Phase-12 "Saleable" 12a–12d.
