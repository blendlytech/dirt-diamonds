# Progress & Next Steps

At the end of every coding session, before you clear the chat, instruct Fable 5 to append a summary of what was completed and what the immediate next steps are to this document. When you begin a new session, you can load this file to instantly orient the AI without needing to explain the whole project again.

*(Note: Detailed logs for Phases 0 through 7 have been archived to keep this file concise. See [progress_archive.md](file:///c:/Users/DELL/dirt&diamonds/docs/progress_archive.md) for the full history of those phases.)*

---

## Current Status (As of 2026-07-04)

**Phases 0–7 are COMPLETE and fully harness-proven.**

- **Phase 0:** Toolchain & Foundation (Godot, SQLite, MCP setup)
- **Phase 1:** Database Core (Schema definitions, Queries, Validator)
- **Phase 2:** Core Loop, Time & Event Bus (GameManager, TimeManager)
- **Phase 3:** Baseball Macro-Sim (M1 - LeagueSimulator, AtBatResolver)
- **Phase 4:** Markov Micro-Sim (M2 - PitchChain, Fatigue, AtBatView slice)
- **Phase 5:** Career Wiring / Player Avatar (MicroGame, Attended games)
- **Phase 6:** Relationships / Rivalry / Succession (3-generation run proven)
- **Phase 7:** Gritty Event framework (Life sim needs, marriage/conception)

Schema is at **v7**. We are proceeding with the **Phase 8/9 Interleave Plan** — see [phase_8_9_interleave_plan.md](file:///c:/Users/DELL/dirt&diamonds/docs/phase_8_9_interleave_plan.md).

---

## 9a — Tier Schema + Multi-Tier Macro-Sim: SHIPPED (2026-07-04, Fable 5 engine + Opus 4.8 delta design, NOT YET COMMITTED)

**All 9a exit criteria met on the first calibration run — no delta nudges needed.** MonteCarloHarness 135→**153/153** (new tier-ladder suite: §5 HS fixture to 5 decimals, per-tier §4 band checks, strictly-monotone AVG/R/G ladder, and an MLB **bit-identity** regression guard against a same-seed MLB-only world); CoreLoop 22/22; SchemaValidator 62→**64/64** (Team_Tiers added to RequiredTables); GrittyEvents 53/53; NeedsDecay 49/49; `dotnet build` 0/0 all 6 projects; live save migrated v6→v7 through two clean headless `--quit` boots (second boot proves the top-up idempotent: 48 teams / 817 players both times, integrity ok, avatar intact on its MLB team).

**Design doc:** [design/tier_league_environments.md](file:///c:/Users/DELL/dirt&diamonds/docs/design/tier_league_environments.md) (Opus 4.8; §7 empirical appendix added by Fable). The single-lever design: batter knobs 0 everywhere (ratings are tier-relative); the environment rides the run-prevention triple `pitStuff/pitControl/teamDefense` at −20/−16/−12/−8/−4/0 descending HS→MLB. Measured: HS .308/.390/.509 @ 6.97 R/G → MLB .249/.318/.417 @ 4.28, strictly monotone.

**Load-bearing decisions:**

- **Schema v7 = new `Team_Tiers` table** (PK team_id → Teams CASCADE, tier 0–5 CHECK, STRICT) + `INSERT OR IGNORE` backfill of pre-v7 teams as MLB — the Pitcher_Roles/Life_Stress additive pattern, NOT an ALTER on Teams. DDL idempotence + all new query plans validated on a scratch db before any C# (No Blind Queries).
- **`GenerateIfEmpty` stays the frozen MLB prefix** (teams 1–8, identical draw sequence; tier rows draw no rng). The five lower tiers come from a NEW `LeagueGenerator.EnsureTierLeagues` — the `EnsureV4` migration-function pattern — serving both migrated saves and fresh worlds, no-op once populated. Team ids in hundred-blocks (HS 101–108 … AAA 501–508); tier-appropriate generated ages (HS 15–18 … MLB frozen 21–36) via optional `GeneratePlayer` params defaulting to the frozen roll. **Consequence: every pre-existing harness fixture keeps its MLB-only 8-team scratch world byte-for-byte** — only the CareerManager ctor churned.
- **`LeagueSimulator` is now one-instance-per-tier** (ctor gained `LeagueTier tier = MLB`; tier-scoped `LoadTeamsByTier`/`LoadRosterByTier`). Tier deltas **bake into the rating arrays at Initialize** — per-PA hot path untouched; PED/rivalry layer on top of the shifted values. New `TierEffects` holds the §2 delta table: MLB all-zero BY CONTRACT (`Shift(r, 0)` identity keeps M1 bit-exact); tuning is a data edit behind `run_monte_carlo_batch`, same as RivalryEffects.
- **`MicroGame` stays GLOBAL** (it was already team-count-flexible): loads all 48 teams and bakes deltas PER TEAM from each player's team tier — an attended HS game plays in the HS environment the macro-sim calibrates, so §11 macro/micro consistency holds tier-by-tier by construction. Defense is mean-then-shift in BOTH sims (same byte for the same team).
- **`CareerManager` takes a new `LeagueDirectory`** (tier → sim map; sparse registration is the harness contract via a `Solo(league)` helper — 8 mechanical call-site edits). `ActivateAvatar` resolves the avatar team's tier (`TryGetTeamTier`) and builds a TIER-RELATIVE `_avatarTeamIndex` over `LoadTeamsByTier` — the pressure-test finding; `LeagueSchedule.TryGetPairingFor` now gets a 0..7 index within the right league. `CreateAvatar`/`Succeed` flush + re-init only the avatar tier's sim.
- **`StatsNormalizer.NormalizeSeason(year, tier)`** — tier-scoped UPDATEs (players currently rostered in the tier), so six sims flushing the same day each rewrite only their own league. Tier-scoped league-totals queries added for the harness/future standings UI.
- **New-game careers start in HS**: `NewGameScreen` populates from `LoadTeamsByTier(HS)` only. The existing live-save avatar stays MLB (its team's backfilled tier).

**Known artifacts / disclosed simplifications:** uniform 9+5+3 rosters HS→MLB (interleave-plan disclosure); **`LoadFreeAgents` deliberately NOT tier-scoped** — free agents are tierless entities (team_id NULL has no Team_Tiers row to filter on), so succession backfill draws from the global pool; in practice the pool is avatar-tier alumni, and formalizing player↔tier movement is exactly 9c's promotion model. NPCs never promote or age out of a tier (a 15-year-old HS NPC ages upward in place across seasons — 9c's concern); HS avatar starts at `StartingAge` 19 while generated HS teammates are 15–18 (revisit with 9b/9c age handling); life-sim NPC count grew 137→817 (all tiers seed the needs engine; the day-tick flush now writes ~817 Life_Needs rows — well inside the benchmarked batch budget); K%/BB%/HR separate tiers only gently by design (doc §1 discloses — AVG/R/G carry tier identity).

## 9b — Bare Daily-Clock Skeleton: SHIPPED (2026-07-04, Fable 5 engine + Sonnet 5 UI, NOT YET COMMITTED)

**Both 9b engine guarantees harness-proven.** NeedsDecayHarness 49→**61/61** (new 9b daily-clock section); MonteCarloHarness **153/153** re-run per the standing rule (CareerManager touched — no band moved, MLB bit-identity guard still exact); CoreLoop 22/22 (Life↔Baseball boundary scan clean); GrittyEvents 53/53 (wildcard-compiles the Life folder); `dotnet build` 0/0 all 6 projects; live headless `--quit` boot clean (v7, day 43, avatar + 817 NPCs, no errors). No schema change — a day plan is intent, not sim state; it deliberately does not persist (same precedent as pending choices/cooldowns).

**What shipped:**

- **`DaySchedule` (new, `Assets/Simulation/Life/DaySchedule.cs`):** readonly struct, five hour blocks (Sleep/School/Practice/Game/Work), ctor-validated ≥0 and ≤24; unallocated hours are "free". Life-folder-local and Data/Baseball-free, so `NeedsDecayHarness`'s wildcard compile stays untouched.
- **`LifeSimManager` avatar split:** `SetAvatar` / `SetTodaySchedule` / `TryGetTodaySchedule` / `ClearTodaySchedule` / `AvatarSchoolAvailable`. With no plan set, the avatar days through the **exact pre-9b `TickHour` loop** — marking an avatar alone changes nothing (the `AutopilotAttendedGames` mirror; headless callers unmodified). A set plan is **one-shot**: consumed by the day it runs, an unplanned tomorrow autopilots (the pending-game-forfeit mirror). `SetAvatar` drops any pending plan (an heir never inherits the retiree's day).
- **Canonical day order — Sleep runs LAST (found the hard way):** first draft ran Sleep first and the harness caught it — sleeping from a full meter burns the whole restore against the 100-clamp, an 8h-sleep day ended at Sleep 33.5. Order is now School → Practice → Game → Work → free hours (standard autopilot, crisis override intact) → Sleep as the night cap (worst end-of-day Sleep 69+ over the scripted week). Sleep restores per-hour at `ActionCatalog.Sleep`'s own rate/environment (one tuning source for both paths); School/Practice/Game/Work ride `ActionCatalog.Idle`'s neutral definition — each future upgrade (9d development, 8a Work payout) is a one-line definition swap. Manual blocks are uninterruptible (player intent outranks the crisis override in the skeleton — disclosed; the override still governs free hours).
- **School gate:** `SetTodaySchedule` rejects School hours unless `AvatarSchoolAvailable` — a plain bool because the Life assembly must never see `LeagueTier`; the bridge projects `tier is HS or College` into it.
- **`AvatarChangedEvent` (new, CoreEvents.cs):** published by `CareerManager.ActivateAvatar` when a bus is attached (creation + succession); GameManager subscribes and `SyncLifeSimAvatar` re-points the Life sim + school gate. Boot-time load activates before the career attaches, so `_Ready` syncs that case directly. **Side-effect fix of a pre-existing gap:** a mid-session avatar (fresh creation, or an heir born this session) was never seeded into the life sim until next boot — the sync handler now projects its Players row in first (`Seed` is idempotent).
- **Harness additions (the plan's two validation demands):** (1) a never-planned avatar reproduces the pre-9b autopilot trace **bit-for-bit over 30 days** (needs+stress+funds, avatar and bystander, stress-seeded worlds); (2) a fully-allocated 24h day matches a closed-form recomputation of the block math **bit-exactly**; plus a scripted week of manual plans (one-shot consumption daily, bystander NPC bit-identical to a no-avatar control world, night cap keeps Sleep restored, Hygiene off-critical via evening Showers, funds strictly falling proves the free-hour autopilot really acts — blocks never spend).

**Disclosed findings / known artifacts:** a 19h-committed plan **genuinely starves Hunger** (one evening Eat +45 can't outrun ~100/day decay; end-of-day Hunger 0.0 all week) — real tuning reality, deliberately unasserted; 8a/9d should consider meal access on Work/School blocks. Blocks charge no `FinancialCost` and grant no restore except Sleep. The plan is in-memory only (lost on quit — pacing/intent, not sim state). Game hours are life-side inert; coordinating "Game block ⇒ play the pending attended game" is the ScheduleScreen's job against `CareerManager.TryGetPendingGame`.

**UI half (Sonnet 5):** `Assets/UI/ScheduleScreen.tscn`+`.cs`, wired into `Main.tscn` as a sixth always-present overlay (declared last). Deliberately **not modal** like EventChoiceScreen/SuccessionScreen — a plan is optional, so it never joins `AttendedGameScreen`'s day-advance gate — instead it follows `BirthNotificationScreen`'s corner-anchored, non-blocking idiom (bottom-right here, to stay clear of the top bar and the top-right birth banner). Five `HSlider`s (0–24, matching NewGameScreen's slider+value-label convention rather than SpinBox) for Sleep/School/Practice/Game/Work; Confirm disables itself once the running total exceeds 24h (`OverAllocatedFormat`) instead of letting `DaySchedule`'s ctor throw. Polls `LifeSim.AvatarSchoolAvailable` and `Career.TryGetPendingGame` every frame to show/hide the School and Game rows and zero their sliders the instant either goes unavailable (tier change, offseason day) — a stale hour never rides along silently. The plan-status label is dirty-flag gated on the polled `DaySchedule` fields per ui_conventions (no per-frame string formatting); row `Visible` assignments are unconditional every frame, matching SuccessionScreen's precedent. `dotnet build` 0/0; booted the live save headless via the Godot MCP (schema v7, day 43, avatar + 817 NPCs) with the new overlay in the tree for multiple frames — zero debug-output errors, confirming every `GetNode<T>()` path resolves.

## Immediate Next Steps (model assignments)

- **Sonnet 5 (engine + UI):** **8a — Survival Economy + Legal Work** per the interleave plan — Work block payout plugs into `TickScheduledDay`'s Work definition swap; consider the disclosed Hunger-starvation finding (meal access on Work/School blocks) in the same pass.
- Standing rule: anything touching the sim assembly re-runs `run_monte_carlo_batch`; tier-delta retunes are data edits in `TierEffects` gated by the §4 bands (escalate to Fable 5 if a band itself must move).
