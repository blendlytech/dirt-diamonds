# Full Project Scan & Handoff Packet (for Claude Opus 5.5)

**Date:** 2026-11-19  
**Prepared for:** Claude Opus 5.5 (requested by owner)  
**Classification:** Review & handoff packet (`docs/review/` — non-canonical per [docs/README.md](../README.md) until owner-accepted decisions are promoted into [GAME_VISION.md](../GAME_VISION.md), [BUILD_PLAN.md](../BUILD_PLAN.md), [TEAM.md](../TEAM.md), and active specs)  
**Audited checkout:** branch `update/seed-data-and-docs` (`2da33f9`)  
**Cross-checked branches:** `main` (`4e6002d`), `p13/integration` (`31de035`), `p13/13-3-opus` (`9d8dbf3`), `p13/13-4-gpt` (`6993e70`), `origin/math/R1-MATH-gpt` (`7eea3e8`)  
**Verification scope & limits:** Read-only source, DDL, git-graph, scene, asset, and doc audit. **No build, test harness, Godot boot, export, or live-save inspection was executed** during this scan (and `dotnet msbuild` on the solution failed in the sandboxed check due to NuGet/SDK resolution outside the full environment). Do not cite any historical test count below as a current passing gate on `2da33f9` without re-running on the target branch.

> **Authority & staffing note:** [TEAM.md](../TEAM.md) §1a currently records the 2026-10-25 GPT-primary staffing directive; this handoff is prepared specifically for **Claude Opus 5.5** at the owner's explicit request on 2026-11-19. Confirm branch target and current role directives before modifying shared/integration branches or launching dependent slices.

---

## 1. Executive Snapshot & Inventory

| Metric | Current State (`update/seed-data-and-docs` @ `2da33f9`) | Across Unmerged Branches |
| :--- | :--- | :--- |
| **Game C# Code** | **129 files / 35,928 LOC** (`Simulation` 57 files / 14,957 LOC; `UI` 32 / 9,566; `Data` 12 / 4,640; `Narrative` 7 / 2,384; `Core` 8 / 2,261; `Economy` 10 / 1,540; `Platform` 3 / 580) | **+9,567 insertions** across `p13/integration` (+5,866), `p13/13-3-opus` (+2,903), and `p13/13-4-gpt` (+798) |
| **Headless Harnesses** | **8 projects / 11 C# files / 15,951 LOC** in [DirtAndDiamonds.sln](../../DirtAndDiamonds.sln) (`MonteCarloHarness` 6,259; `GrittyEventsHarness` 4,481; `NeedsDecayHarness` 1,405; `HustleHarness` 1,150; `HoldemHarness` 1,108; `SchemaValidator` 802; `ArcadeHarness` 399; `CoreLoopHarness` 347) | +`Slice133Suites.cs` (1,018 LOC) on `p13/13-3-opus` & `PlayoffRankingSuite.cs` (212 LOC) on `p13/13-4-gpt` |
| **Godot Scenes** | **22 `.tscn` files** (21 in `Assets/UI/` + `Assets/Main.tscn`), **0 broken `GetNode` paths** | +`CleanBreakDialog.tscn` on `p13/integration` |
| **SQLite Schema** | **v13** (24 `STRICT` tables, 6-tier ladder) in [SchemaDefinitions.sql](../../Assets/Data/Database/SchemaDefinitions.sql#L543) | **v14** (35 tables, 3-tier + playoffs/draft) on `p13/integration`; **v15** (Phase L) unbuilt |
| **Narrative Content** | **10 JSON files / 116 events**, 12 contacts in [contacts.json](../../Assets/Narrative/Contacts/contacts.json), 15 items in [items.json](../../Assets/Data/Items/items.json) | No SR `outcomes[]` or Fencing/Robbery arcs yet |
| **Art & Audio** | **13 PNGs** (~62.8 MiB) in `Assets/Graphics/`, **0 audio files** (`Assets/Audio/` absent; `UiSfx` synth blips only), **36 raw files** (~26.3 MiB) in `ArtSource/` | — |
| **Secrets & Repo Safety** | `.env` is **untracked** (`git ls-files .env` empty); **0 `TODO`/`FIXME`/`HACK`** comments in `Assets/` | — |
| **Local Disk / Worktrees** | `C:\` has **12.33 GB free / 118.3 GB**; `.worktrees/` uses **1,871.55 MiB** (10 live + 20 prunable worktrees) | Previously hit 16 KB free on 2026-10-25, blocking builds & 20× MC replay |

### Existing Strengths to Preserve
- **Strict simulation isolation:** Zero `using` directives or C# type references between `Assets/Simulation/Life/` and `Assets/Simulation/Baseball/`. Zero `Godot` imports anywhere outside `Assets/UI/`, `Assets/Platform/Steam/`, and [GameManager.cs](../../Assets/Core/GameManager.cs#L15-L16). `Assets/Simulation/Arcade/` is completely Godot/DB/Life-free.
- **Zero-GC inner loops & strong test infrastructure:** Baseball/Life/Arcade inner loops avoid LINQ and per-PA allocations; 8 dedicated headless harnesses (~16k LOC); 0 broken `GetNode` paths across all 22 scenes; Facepunch.Steamworks 2.5.2 cleanly RID-scoped in [DirtAndDiamonds.csproj](../../DirtAndDiamonds.csproj#L22-L36).

---

## 2. Critical Issues Found Right Now (Fix First)

### 2.1 Severe Repository Split-Brain Across Active Branches
The repository currently has **three diverged realities**, none of which holds all current work:
1. **`main` (`4e6002d`, 2026-10-18):** Stuck on schema v13, pre-SR-0, pre-Phase-13 runtime code, pre-GPT roster.
2. **Current checkout `update/seed-data-and-docs` (`2da33f9`, +9 commits ahead of `main`):** Contains `GAME_VISION` v3.0 ([GAME_VISION.md](../GAME_VISION.md)), [scope_realignment.md](../design/scope_realignment.md), GPT roster updates, HS art assets, and working-tree edits (`D docs/architectural_refactoring_scope_alignment_directive.md`, `M docs/progress.md`, untracked [ARCHITECTURAL_DIRECTIVE_V1.md](../ARCHITECTURAL_DIRECTIVE_V1.md), [architectural_directive_v1_audit.md](architectural_directive_v1_audit.md), [industrial_broadcast_theme.md](../../Assets/UI/Theme/industrial_broadcast_theme.md), [theme_tokens_registry.cs](../../Assets/UI/Theme/theme_tokens_registry.cs)) — **but has zero Phase 13 C# code**, so [docs/progress.md](../progress.md#L17) falsely reports *"13-1 is next."*
3. **Unmerged Phase 13 & Math branches (branched from `4e6002d`, lacking `GAME_VISION` v3.0 / SR-0):**
   - `p13/integration` (`31de035`, 49 files / +5,866 insertions / -972 deletions): **13-1a + 13-1 + 13-2 are already implemented, reviewed, and merged** (with post-merge fix review accepted on `origin/p13/13-2-review-gpt` @ `cfb3c09`).
   - `p13/13-3-opus` (`9d8dbf3`, 26 files / +2,903 insertions / -497 deletions): **13-3 is already implemented** (`TierSeasonCalendar`, `SeasonScheduler`, `Schedule_Games`, Pro guard candidate), awaiting M1–M5 Math check + 20× replay (earlier replay blocked when `C:` filled to 16 KB free).
   - `p13/13-4-gpt` (`6993e70`, 12 files / +798 insertions): **13-4 playoff core + ranking is already implemented** (`PlayoffManager`, `RankingCalculator`, `ChampionshipWonEvent`, 47/47 focused checks reported in branch commit), pending attended-postseason bridge, bracket UI, and independent review.
   - `origin/math/R1-MATH-gpt` (`7eea3e8`): **`docs/design/math/fatigue_life_contracts.md`** is already authored, awaiting review.

### 2.2 Current Checkout (`update/seed-data-and-docs`) Crashes on Boot & Fails `GrittyEventsHarness`
- Commit `36ff523` merged 12-team v14 [schools.json](../../Assets/Data/Schools/schools.json) (`101–112`, inserting `105=BRK`, `106=CDG`, shifting `PNH/WSF/SMT/RVT` to `107–110`, adding `111=GRF`, `112=HLC`) onto a **v13 C# checkout** where [LeagueGenerator.cs](../../Assets/Simulation/Baseball/LeagueGenerator.cs#L34-L44) still seeds 8 HS teams (`101–108`, with `105=PNH`, `106=WSF`). (Directly violated [13-1a_seed_data_flash_brief.md](../archive/13-1a_seed_data_flash_brief.md#L8), which warned never to merge to a non-13-1 branch.)
- At boot, [GameManager.cs](../../Assets/Core/GameManager.cs#L577-L580) calls [SchoolCatalog.ValidateAgainstRoster](../../Assets/Data/Schools/SchoolCatalog.cs#L147-L173), which **immediately throws `InvalidOperationException`** (team 105 `BRK` ≠ `PNH`, plus 106–108 mismatches and 4 orphan entries `109–112`).
- [Tools/GrittyEventsHarness/Program.cs](../../Tools/GrittyEventsHarness/Program.cs#L4455-L4477) also fails because it asserts `catalog.Count == 8` and `101–108`.
- **Trademark regression:** `36ff523` pulled the *pre-review* [home_parks.json](../../Assets/Data/League/home_parks.json#L187) containing `"Bayport Harbor Yards"` for team `301` — which Opus review `1a2c749` (merged into `p13/integration`) already flagged as echoing real-world *"Ballpark at Harbor Yard"* and renamed to `"Bayport Tidewater Park"`.

### 2.3 Broken Untracked Phase 15 Theme Drop
- Untracked [theme_tokens_registry.cs](../../Assets/UI/Theme/theme_tokens_registry.cs#L12) references `res://Assets/UI/Theme/IndustrialBroadcast.tres`, which **does not exist**.
- Its Godot `[gd_resource type="Theme"]` payload was saved as Markdown at [industrial_broadcast_theme.md](../../Assets/UI/Theme/industrial_broadcast_theme.md) (513 lines). Even if renamed to `.tres`, it has **inline `;` comments after property values** (e.g., lines 24, 98, 103, 118, 143, 178, 220, 270, 361) that risk Godot parser failure and **zero `ExtResource` font bindings** (unlike [DirtAndDiamonds.tres](../../Assets/UI/Theme/DirtAndDiamonds.tres#L3-L9)). Meanwhile, `theme_tokens_registry.cs` uses snake_case (violating `GEMINI.md §3`), lacks a `.uid`, is auto-compiled by `DirtAndDiamonds.csproj`, and has 0 callers.

### 2.4 Duplicate/Unreferenced Art, Export Leak & Missing `.import` Files
- **`Team-Images/` (32.69 MiB / ~34.3 MB):** All 6 PNGs in `Team-Images/Cardinals/` and `Team-Images/Longhorns/` are **byte-for-byte SHA256 duplicates** of `Assets/Graphics/HighSchools/CRW_*` and `LKS_*`. `Team-Images/` has **no `.gdignore`**, Godot already generated 6 `.import` files for it, and [export_presets.cfg](../../export_presets.cfg#L39) (`exclude_filter="Tools/*, .mcp.json"`) **will pack all ~34 MB into exported builds**.
- **Duplicate unreferenced cover art (22.44 MiB / ~23.5 MB):** `Assets/Graphics/Covers/cover_art.png` (11.22 MiB) and `Assets/Graphics/UI/cover_art.png` (11.22 MiB) are **byte-for-byte identical** and have **0 references** in any `.cs` or `.tscn`.
- **Missing `.import` sidecars & missing school/venue art:** All 5 PNGs added in `c7f2392` (`BRK_field`, `BRK_logo`, `BRK_school`, `CDG_logo`, `CDG_school`) lack committed `.import` files (required by [.gitignore](../../.gitignore#L2-L4)); `CDG_field.png` is missing; **25 of 36** image paths in [schools.json](../../Assets/Data/Schools/schools.json) do not exist yet; and **none** of the `res://Assets/Graphics/Venues/...` paths in [venues.json](../../Assets/Data/Venues/venues.json#L26-L32) exist on disk.

### 2.5 Broken Governance Links & Stale `.agents/rules/` Poisoning Future Sessions
- **33 broken Markdown links** across core governance/design docs (`GEMINI.md:97`, [GAME_VISION.md](../GAME_VISION.md#L38), [BUILD_PLAN.md](../BUILD_PLAN.md#L19), [scope_realignment.md](../design/scope_realignment.md#L3)) caused by `2da33f9` deleting `gameplay_core.md`, `arcade_baseball_overhaul.md`, `at_bat_read_input_model.md`, and `visual_direction_3d_proposal.md`, plus the unstaged deletion of `architectural_refactoring_scope_alignment_directive.md`. Critically, **`at_bat_read_input_model.md`** documents the shipped Markov `MicroGame` read input that v3.0 restored as authoritative, and **`gameplay_core.md` §1** held the attribute contracts still cited by `GEMINI.md §11`.
- **4 of 7 `.agents/rules/` files & 1 skill contradict `GAME_VISION` v3.0:**
  - [baseball_engine.md](../../.agents/rules/baseball_engine.md#L28-L34) still instructs agents to **replace `MicroGame` with full real-time arcade physics**.
  - [audio_voice_cutscenes.md](../../.agents/rules/audio_voice_cutscenes.md#L7) & [validate_audio_manifest/SKILL.md](../../.agents/skills/validate_audio_manifest/SKILL.md#L20) still enforce **59 announcer voices (56 team + 3 HS)** instead of 6 conference personas.
  - [gritty_events.md](../../.agents/rules/gritty_events.md#L29-L34) still treats **Fencing & Robbery as interactive hustle nodes**.
  - [life_sim_ai.md](../../.agents/rules/life_sim_ai.md) omits the **Weekly Lifestyle Stance** pivot.
  - Also note [ui_conventions.md](../../.agents/rules/ui_conventions.md#L34) still points to nonexistent `Assets/Data/Teams/team_colors.json`, and `run_monte_carlo_batch/SKILL.md` omits the `Clutch` integration / `ArcadeHarness` rules from `GEMINI.md §9, §11`.

### 2.6 Disk Space & Worktree Bloat
- `.worktrees/` inside the repo consumes **1,871.55 MiB** across 10 checkouts, alongside **20 prunable worktrees** outside the repo (`C:\Users\DELL\dnd-*`, `%TEMP%`, `.codex/worktrees`). On a 118.3 GB `C:\` drive with only **12.33 GB free**, one more build wave risks repeating the 2026-10-18 / 2026-10-25 disk-exhaustion failures.

---

## 3. What Needs To Be Done Next (Ordered Critical Path)

Follow this sequence to unify the repository, close Gate 1 cleanly, and reach the **15-Minute Steam Next Fest Loop (Gate 2)**:

### Step 0 — Repo Hygiene, Branch Reconciliation & Disk Recovery (Before Any Feature Code)
1. **Prune stale worktrees & reclaim ~1.8+ GB:** Run `git worktree prune` and clean obsolete/merged checkouts in `.worktrees/` (with owner confirmation for any removal), delete duplicate `Team-Images/` (or add `.gdignore` + export exclude), and remove duplicate `cover_art.png` (owner "ask first" applies to deletions).
2. **Unify the split-brain branches:**
   - Either land the SR-0 docs (`GAME_VISION` v3.0, `scope_realignment.md`, `ARCHITECTURAL_DIRECTIVE_V1.md`, `architectural_directive_v1_audit.md`) onto `main` **without** the premature v14 `schools.json` (keeping `main` bootable on v13), **OR** forward-port those docs + HS PNG assets onto `p13/integration` while preserving `p13/integration`'s C1-corrected [home_parks.json](../../Assets/Data/League/home_parks.json) (`"Bayport Tidewater Park"`).
   - Run `godot --headless --import` (or open in editor) and commit the missing `.import` sidecars for the 5 `c7f2392` PNGs (`BRK_*`, `CDG_logo`, `CDG_school`).
3. **Repair governance & agent rules:** Restore or extract active contracts from deleted `gameplay_core.md` §1 and `at_bat_read_input_model.md` (available in git history at `0c2774d`), repair all 33 broken doc links, move closed briefs (`13-0`, `CS-M`, `13-1a`, cancelled `G-0c`) to `docs/archive/`, and sync `.agents/rules/{baseball_engine,audio_voice_cutscenes,gritty_events,life_sim_ai,ui_conventions}.md` + `.agents/skills/` to `GAME_VISION` v3.0.
4. **Resolve untracked theme files:** Either convert `industrial_broadcast_theme.md` into a valid, font-bound `IndustrialBroadcast.tres` (stripping inline `;` comments) and rename `theme_tokens_registry.cs` → `ThemeTokens.cs` as the **15a/15b** baseline, or shelve them cleanly out of the dirty working tree.

### Step 1 — Lock Open Owner Decisions (Unblocks Gate 1 & Gate 2)
From [scope_realignment.md §9](../design/scope_realignment.md#L285-L295) and [architectural_directive_v1_audit.md §7](architectural_directive_v1_audit.md#L198):
- **D-1 / D-2 / D-3 (Gate 1 boundary):** Confirm full Phase 13 vs shortened `13-1…13-3` (+ HS half of `13-6`) before Gate 2, and confirm whether **v15 DDL only** (Phase L tables) + a named backend exception for `SR-1a` / `SR-2a` land before the Gate 1 schema/mechanics freeze.
- **D-8 (15a Art Direction — Gate 2 long pole):** Formally confirm whether the untracked **"Industrial Broadcast"** theme (`#0D0F12` + `#00E5FF` / `#FFB300` / `#FF2A54`, 0px corners, 2px borders) IS the chosen 15a direction, or request style-frame exploration first.
- **D-4 / D-5 / D-6 / D-7 / D-9:** Pitcher intent deferred past Gate 4 (recommended); 6 single-voice conference booths vs 12; per-file delete approval (`ScheduleScreen`, `FencingScreen`, `RobberyScreen`, 0-byte stubs, duplicate art, stray root `.py` scripts); G-7/G-8 pause beyond HS; Next Fest target date.
- **Remaining non-Gate-2 owner inputs:** Final league/team/Red Dirt Series trademark screening, Steamworks App ID (replacing `DevAppId = 480`), ethnicity/legends list, `ChooseFatherFallback` & `DiscoveryChance` formulas, and operational check that any external auto-committer cannot target `main` or `p13/integration`.

### Step 2 — Close Gate 1 (Phase 13 Core + Save Durability)
Do **not** rewrite `13-1…13-4` from scratch — finish their review/merge pipeline:
1. **Finish `13-3` (`p13/13-3-opus`):** Run M1–M5 Math verification, resolve the pooled Pro R/9 = `4.164` (< `4.2`) band failure reported in `13-4`/`13-3`, run the 20× MC determinism replay (now that disk space is available), obtain owner Pro guard sanction, and merge `13-3` into `p13/integration`.
2. **Finish `13-4` (`p13/13-4-gpt`):** Complete the attended-postseason bridge (`CareerManager`/`MicroGame`/`PlayoffManager`), bracket UI, and independent review; merge to `p13/integration` (or sequence behind Gate 2 if shortened `D-2` is approved).
3. **Land `R1-SAVE-1` + `R1-SAVE-2` (Mandatory before Weekly Stance):** Fix the release-blocking save bugs (`B01`, `C01`, `L01`, `N01` + `C03`) per [R1-SAVE-1](../tasks/R1-SAVE-1_save_boundary.md) and [R1-SAVE-2](../tasks/R1-SAVE-2_recovery_and_rng.md). As [architectural_directive_v1_audit.md](architectural_directive_v1_audit.md#L110) proved, multi-day weekly advancement is unsafe until day settlement, league flush, relationship dirty sets, and pending choices are atomic and resumable.
4. **Optionally land `13-5`, `13-6` (HS graduation → recruiting for Gate 4), and `L-1` v15 DDL** per `D-2`/`D-3`.

### Step 3 — Execute Gate 2: The "15-Minute Steam Next Fest Loop"
Author scoped task briefs in `docs/tasks/` first (none exist yet for `SR-1…SR-4`), then execute in order:
1. **`SR-1a` (`outcomes[]` + durable pending FIFO):** Extend `EventChoice` with hidden stat/flag-weighted `outcomes[]`, persist chosen branch/text in `Game_Logs`, and replace the single-slot auto-forfeit pending choice in [EventConsequenceApplier.cs](../../Assets/Narrative/Events/EventConsequenceApplier.cs#L172-L180) with a durable FIFO queue.
2. **`SR-2a` / `SR-2b` (Weekly Lifestyle Stance):** Implement `LifestyleStance`, `StanceScheduleBuilder`, `WeeklyUpkeep` (**replacing**, not doubling, [LifeSimManager.cs:554](../../Assets/Simulation/Life/LifeSimManager.cs#L554)), and Core `WeekAdvanceCoordinator` (settling one day at a time and halting cleanly on events, stress/funds crossings, and Friday games). Move clock driving out of `TimeControlBar` into Core.
3. **`SR-1b` / `SR-1c` (Fencing & Robbery → Burner Phone):** Mine calibrated payouts/risks from `FencingNegotiation` and `RobberyHustle` into 3–4 node Burner Phone arcs in [dirt_underworld_events.json](../../Assets/Narrative/Events/Content/dirt_underworld_events.json) (preserving `hot_goods` / `robbery_bust` / `fencing_sting` chains); add a week-2 HS temptation in [hs_onboarding_events.json](../../Assets/Narrative/Events/Content/hs_onboarding_events.json); disconnect `ScheduleScreen`, `FencingScreen`, and `RobberyScreen` from navigation.
4. **`SR-4` + Scoped Phase 15 (`15b` / `15c`):** Apply the approved Theme to **Burner Phone**, **Dashboard / Standings / Weekly Stance**, and **At-Bat (`MicroGame`) Broadcast View**, plus a 15-minute onboarding/backstory flow and local `SessionLog` telemetry.

### Step 4 — Gate 3, Gate 4 & Post-Playtest Roadmap
- **Gate 3 (`SR-5` Golden Slice):** Hold all batch art (`GPT_art_production_prompt.md`, 68-team logos, `Tools/SpriteBake` runs) until 1 batter sheet, 1 pitcher sheet, 1 stadium backdrop, and 1 Burner Phone screen hit shipping quality.
- **Gate 4 (External HS Playtest):** 10-game HS Friday season + recruiting offers + HS gritty events on exported builds with session logs.
- **Post-Gate 4:** Finish remaining Phase 13 (`13-5`/`13-6`/`13-7`), Phase L runtime (`L-1…L-8`, `S-1…S-4`, `R1-FAMILY`, `R1-REVEAL`), optional **`SR-3a`/`SR-3b` Clutch Moment**, **Phase 14 / `SR-9`** (6 conference announcers), **Phase 16** (motion comics), **`SR-6`/`SR-7`/`SR-8`** (`mods/` loader, Tabloid feed, Rap Sheet exporter), and **Phase 17** (Steam commercial release).

---

## 4. What Can Be Improved (Engineering, Quality & Pipeline)

### 4.1 Unfixed R-1 & Newly Discovered Backend Defects (11 of 12 R-1 Bugs Still Open)
Only `B02` (`CareerManager.NextGuid()` deterministic IDs) has been fixed in the scanned checkout. Open defects verified in code:

| ID | Severity | Location & Verified Defect |
| :--- | :--- | :--- |
| **`B01`** | **High** | [LeagueSimulator.cs:426-429](../../Assets/Simulation/Baseball/LeagueSimulator.cs#L426-L429), [GameManager.cs:687-727,1087-1106](../../Assets/Core/GameManager.cs#L1087-L1106): `FlushPending()` runs only every 7-day cycle; neither `SaveNow()` nor `_ExitTree()` flushes leagues, losing mid-cycle background stats while `current_day` survives. |
| **`B03`** | **High** | [PitcherFatigue.cs:99-100](../../Assets/Simulation/Baseball/PitcherFatigue.cs#L99-L100): Decays stuff/control toward `50` (`50.0 + (r - 50) * m`), causing below-50 effective pitchers (routine in HS/College) to **improve when fatigued** (30 → 40 at `m = 0.5`). Review and adopt the fix drafted on `origin/math/R1-MATH-gpt` (`fatigue_life_contracts.md`). |
| **`C01`** | **High** | [TimeManager.cs:74-81](../../Assets/Core/TimeManager.cs#L74-L81), `EventBus.cs:188-201`: Commits `current_day` before publishing events with no durable work/recovery journal. |
| **`L01`** | **High** | [GameManager.cs:1401-1426](../../Assets/Core/GameManager.cs#L1401-L1426), [RelationshipGraph.cs:227-236,308-318](../../Assets/Simulation/Life/RelationshipGraph.cs#L227-L236): `CollectDirty`/`CollectDirtyHistory` clear `_dirty` and `_dirtyHistory` *before* SQLite transaction begins; rollback permanently drops retry sets. |
| **`N02`** | **High** | [EventConsequenceApplier.cs:538-564,702-724](../../Assets/Narrative/Events/EventConsequenceApplier.cs#L538-L564): Marriage only adjusts affinity when an edge exists (never reclassifies Friend/Rival/ex to `Partner`), and target selection does not exclude already-partnered targets. |
| **New: Flush double-count** | **High** | [LeagueSimulator.cs:892-908](../../Assets/Simulation/Baseball/LeagueSimulator.cs#L892-L908): Commits additive stats at `:892`, calls `_normalizer.NormalizeSeason()` at `:903`, and clears `_batting`/`_pitching`/`_pedGamesPlayed` only at `:905-908`; normalizer failure causes duplicate stat addition on retry. |
| **`B04`** | Medium | [GameManager.cs:312,328](../../Assets/Core/GameManager.cs#L312): Reseeds RNG from `Environment.TickCount64` on boot; RNG streams are not persisted across save/reload. |
| **`C02`** | Medium | [TimeControlBar.cs:181-182,319-320](../../Assets/UI/TimeControlBar.cs#L181-L182), [Main.cs:161](../../Assets/UI/Main.cs#L161), [TutorialOverlay.cs:299](../../Assets/UI/TutorialOverlay.cs#L299): UI directly drives clock / `AutopilotAttendedGames` and writes `Game_State`. |
| **`C03`** | Medium | [BaseballDashboard.cs:283-289](../../Assets/UI/BaseballDashboard.cs#L283-L289), [DatabaseManager.cs:203-251](../../Assets/Data/Database/DatabaseManager.cs#L203-L251), [GameManager.cs:1093](../../Assets/Core/GameManager.cs#L1093): `PlayPendingGame` (and DB flush) runs on `Task.Run` while `DatabaseManager` releases `_dbLock` before callers bind parameters or consume readers; `SaveNow` checks `IsBatchActive` but not `InteractiveGameInFlight`. |
| **`N01`** | Medium | [EventConsequenceApplier.cs:111-112,156-160,172-182](../../Assets/Narrative/Events/EventConsequenceApplier.cs#L146-L182), `EventDispatcher.cs:56`: Single in-memory `_pending` choice cleared before commit and overwritten/auto-forfeited if a second avatar event arrives; cooldowns are in-memory only. |
| **`E01`** | Medium | [HustleService.cs:188-212,230-304](../../Assets/Economy/Hustles/HustleService.cs#L188-L212): Writes `controls_turf_local` (`:209`) and faction relationships outside `ApplyCore`'s money transaction with no recovery record. |
| **`S01`** | Medium | [SteamIntegration.cs:89-96](../../Assets/Platform/Steam/SteamIntegration.cs#L89-L96), [AchievementManager.cs:136-141,166-197](../../Assets/Platform/Steam/AchievementManager.cs#L136-L141): Event-driven unlocks and `Journeyman` increments lost when Steam is unavailable; no offline-to-online reconciliation. |
| **New: Blind harness tests** | Medium | [Tools/GrittyEventsHarness/Program.cs:4496-4507](../../Tools/GrittyEventsHarness/Program.cs#L4496-L4507): All 6 negative `SchoolCatalogJson` parser fixtures use legacy `"image_path"` instead of `"images": { "logo", "school", "field" }`, so all 6 throw early on missing `"images"` without exercising bad hex, missing colors, missing `school_name`, or duplicate `team_id`. |

### 4.2 Architecture & Maintainability Improvements
- **Build the shared `EffectiveRatings` stack (`GEMINI.md §11`):** `LeagueSimulator.cs` and `MicroGame.cs` currently duplicate 5 parallel blocks (tier+person baking, replacement player creation, `ApplyRust`, gear+rust math, ledger refresh caches, and `BattingLine`/`PitchingLine` structs/flush loops). Consolidating them prevents drift before `R1-EFFECTS`, `SR-3a`, and Phase L `Active_Effects` land.
- **Enforce the UI Read-Only Boundary (`R-1 C02`):** Remove direct `GameState.SetInt64` writes in [Main.cs:161](../../Assets/UI/Main.cs#L161) and [TutorialOverlay.cs:299](../../Assets/UI/TutorialOverlay.cs#L299), move clock/day-advance control out of [TimeControlBar.cs](../../Assets/UI/TimeControlBar.cs#L181-L182), stop stepping hustle state machines inside UI screens (`FencingScreen`, `RobberyScreen`, `NarcoticsHustleScreen`, `TexasHoldemTable`), and route UI reads through Core DTOs rather than exposing raw `*Queries` handles to 6 UI controllers.
- **Decompose God Classes incrementally as slices touch them:**
  - `BurnerPhone.cs` (**1,746 LOC**; plus 70 layout overrides in `BurnerPhone.tscn`) → split per-tab subcontrollers.
  - `CareerManager.cs` (**1,565 LOC**), `GameManager.cs` (**1,471 LOC**), `BaseballQueries.cs` (**1,324 LOC**), `MicroGame.cs` (**1,268 LOC**), `PitchChain.cs` (**1,100 LOC**) → extract `WeekAdvanceCoordinator` and persistence/flush coordinators out of `GameManager`.
- **Unify UI Styling & Remove Inline Overrides:** Replace [UiColors.cs](../../Assets/UI/UiColors.cs), the 10 inline `.tscn` style/color overrides, 141 `theme_override_constants/*` overrides, and hardcoded swatches in `PortraitView.cs` with the single Phase 15 Theme + `ThemeTokens`.

### 4.3 Performance, Build, Asset & Export Hygiene
- **Targeted `MicroGame.FlushGame`:** [MicroGame.cs:1221](../../Assets/Simulation/Baseball/MicroGame.cs#L1221) iterates every `_roster.Length` slot across all tiers even though only 2 teams played — restrict flush to the 2 participating teams (increasingly important as Phase 13 expands world size).
- **Skip redundant boot schema backfills & fix transaction boundary:** [DatabaseManager.ApplySchema](../../Assets/Data/Database/DatabaseManager.cs#L105-L119) executes the entire DDL + 5 `INSERT OR IGNORE` backfill scans on every boot regardless of `PRAGMA user_version`, and `PRAGMA user_version = 13;` sits outside `BEGIN TRANSACTION ... COMMIT` ([SchemaDefinitions.sql:539-543](../../Assets/Data/Database/SchemaDefinitions.sql#L539-L543)). Also make `DatabaseManager.CommitBatch()` safely clear/dispose `_activeBatch` on commit exception, and replace column-name interpolation in `PersonQueries.cs:174,243-245` with fixed constant SQL queries.
- **Tighten [export_presets.cfg](../../export_presets.cfg):**
  - Add `Team-Images/*` (or delete the folder) to `exclude_filter`.
  - Add `*Assets/Data/League/*.json`, `*Assets/Data/Life/*.json`, and strings tables to `include_filter` as C# loaders land.
  - Populate Windows `application/icon`, `product_name`, `file_version`, and set `dotnet/include_debug_symbols=false` for release builds.
- **Clean up dead/stray repo clutter (requires owner delete approval):**
  - 0-byte stubs: `Assets/Economy/Hustles/NarcoticsTerritoryManager.cs`, `TexasHoldemLogic.cs`.
  - Empty dir: `Assets/UI/dashboard/`.
  - Root stray files: `find_tags.py`, `find_tags2.py`, `find_urls.py`, `find_urls2.py`, `lint_results.txt`, `scratch.db`, and obsolete root brief [GPT_art_production_prompt.md](../../GPT_art_production_prompt.md) (targets paused 314-image batch generation and unavailable models).
  - Hardcoded machine paths (`C:\Users\DELL\...`) in [launch_game.bat](../../launch_game.bat) and [launch_editor.bat](../../launch_editor.bat) (fall back to `GODOT_PATH` / `PATH`).
- **Asset sizing & metadata:** Downscale/re-compress the 9–10+ MiB HS PNGs (`CRW_field.png`, `CRW_school.png`, `LKS_field.png`) to keep exports well under the Phase 17 `< 2 GB` cap, and remove/replace the obsolete 68 per-team `"booth"` blocks in [venues.json](../../Assets/Data/Venues/venues.json) when Phase 14's 6-conference model is implemented.
- **Steam readiness:** Swap `DevAppId = 480` in [SteamIntegration.cs:27](../../Assets/Platform/Steam/SteamIntegration.cs#L27) once the real App ID exists, add offline achievement reconciliation (`S01`), and update legacy `MinorA`/`MLB` achievement checks in [AchievementManager.cs:227-234](../../Assets/Platform/Steam/AchievementManager.cs#L227-L234) (`13-7` / `17a`).

---

## 5. Recommended Opus 5.5 Starting Checklist

1. **Confirm branch strategy & owner inputs first:** Decide whether `update/seed-data-and-docs` merges docs-only to `main` (reverting the premature v14 `schools.json`/`home_parks.json` change on `main`) or merges forward into `p13/integration` (keeping `p13/integration`'s C1-fixed `home_parks.json`), and ask the owner for delete approvals + `D-1…D-9` / `15a` choices.
2. **Reclaim disk & sanitize workspace:** Prune obsolete worktrees/build folders (`~1.8+ GB`), remove duplicate art (`Team-Images/`, duplicate `cover_art.png`) once approved, generate/commit missing `.import` files for `c7f2392`, and resolve the untracked `IndustrialBroadcast` theme drop.
3. **Repair governance & agent rules:** Restore `gameplay_core.md` §1 and `at_bat_read_input_model.md` contracts from `0c2774d`, fix the 33 broken links, archive closed briefs, and align `.agents/rules/` + `validate_audio_manifest` + `run_monte_carlo_batch` to `GAME_VISION` v3.0.
4. **Close Gate 1 on `p13/integration`:** Complete `13-3` Math verification / Pro R/9 check / 20× MC replay / guard sanction → resolve `13-4` attended bridge/UI → land `R1-SAVE-1` & `R1-SAVE-2` (fixing `B01`, `C01`, `L01`, `N01`, `C03`, and the `FlushAccumulated` double-count bug) → optionally land `L-1` v15 DDL per `D-3`.
5. **Draft Gate 2 briefs & build the 15-Minute Loop:** Brief and execute `SR-1a` → `SR-2a/2b` → `SR-1b/1c` → `SR-4` + scoped Phase 15 (`15b`/`15c`), keeping `Assets/Simulation/Baseball/` neutral bytes locked.

