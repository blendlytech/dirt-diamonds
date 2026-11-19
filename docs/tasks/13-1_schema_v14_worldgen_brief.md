# Task Brief: 13-1 — Schema v14, clean-break boot, `LeagueTier` collapse, 68-team world-gen

**Status:** **APPROVED by the owner 2026-10-18**, with every recommendation accepted: the §5.3 expected-red list, including the career boot stopping at `LeagueSimulator`'s 8-team check until 13-2/13-3 (the in-13-1 `TeamCount` alternative is rejected); the §3.2 interpretations 1–5 as written; the Appendix A placeholder names for the build (the TEAM.md team-names gate stays open for a later data-edit rename). **Not yet startable:** §0 preconditions 1, 2 and 4 are open (the CS chain isn't merged, `p13/integration` doesn't exist, and this session has no .NET machine).
**Recommended model:** Claude Opus 5.5 (TEAM.md §2), with Gemini 3.8 Flash for the seed data in §3.3
**Reviewer:** GPT 6 Astra Pro (independent review of Claude's sim diffs, TEAM.md §1a)
**Owner gate:** team names (TEAM.md §2), still open. The Appendix A names are placeholders that a later data edit can rename, so they don't block the build. The §5.3 expected-red list was approved on 2026-10-18.
**Spec:** [league_restructure.md](../design/league_restructure.md) §1, §2, §5.1, §5.3, §12, §13, §14, §15 (13-1 row), §16-I1, Appendix A; [arcade_baseball_overhaul.md](../design/arcade_baseball_overhaul.md) §8.1 (`Player_Fielding`)
**Branch / worktree:** `p13/13-1-opus`, branched from **`p13/integration`**, in your own worktree (`git worktree add ..\dnd-opus-131 -b p13/13-1-opus origin/p13/integration`). Merges into `p13/integration`, **never `main`** (§15).

## 0. Preconditions (check first; stop and report if any fails)

1. The CS chain (`p13/CS-rules-opus`, CS-0 + CS-M + rules sync) is merged to `main` by the owner. 13-1 doesn't depend on its content, but `p13/integration` has to start from a `main` that already has the current spec.
2. **`p13/integration` exists.** It didn't on 2026-10-18. If it's still missing, Claude Opus creates it from `origin/main` and pushes it. That's the integration lead's job, not a slice change. Record the base sha in the handoff.
3. `git fetch --all`, `git status`, `git worktree list`. Nobody else has a worktree on `p13/13-1-opus`.
4. A machine with the .NET 8 SDK and NuGet access. The cloud workspace can't run `dotnet` (`dot.net` and `api.nuget.org` are blocked), so this slice runs on the owner's PC.
5. Baseline on the branch base before any edit: `dotnet build DirtAndDiamonds.sln -c Debug` → 0/0; SchemaValidator 111/111; MonteCarlo 354/354. Record any difference from these numbers before starting.

## 1. Context

Phase 13 turns the shipped six-tier ladder (8 teams per tier, schema v13) into the game in [GAME_VISION.md](../GAME_VISION.md) §3/§4: **HS → College → Draft → Pro**, three tiers, 68 teams with conferences and divisions. 13-1 is the foundation. It changes the world's shape (schema, enum, generated data) and leaves the sim's behavior on that world to 13-2…13-6. GAME_VISION already settles that v14 is a clean break: old saves are refused, never migrated, and the old file is kept.

Shipped facts this brief relies on (verified at `ebaee21`):

- `Assets/Data/Database/BaseballDtos.cs:30`: `LeagueTier { HS, College, MinorA, MinorAA, MinorAAA, MLB = 5 }`.
- `Assets/Data/Database/SchemaDefinitions.sql:543`: `PRAGMA user_version = 13`; `Team_Tiers.tier CHECK (tier BETWEEN 0 AND 5)` with a v6→v7 backfill to tier 5; `Teams(team_id, city, name, abbreviation, league, division)`.
- `COALESCE(tt.tier, 5)` (a missing tier reads as MLB) at `BaseballQueries.cs:27`, `:51` and `NarrativePollQueries.cs:103`. The spec (§13 ConditionEvaluator row) makes the default **Pro (2)**.
- `LeagueDirectory.TierCount = 6`; `LeagueGenerator.TierTeamIdBase` special-cases MLB ids 1–8; `TierTeamSeeds` + `TeamSeeds` hold the 40 shipped seeds; `TierAgeRolls` College is 18–21 (the spec says 18–22).
- `GameManager._Ready` calls `_database.ApplySchema(ddl)` on whatever save is at `user://dirt_and_diamonds.db`, **then** runs `GenerateIfEmpty` → `EnsureV4` → `EnsureTierLeagues`, then builds one `LeagueSimulator` per tier. `LeagueSimulator` **throws unless a tier has exactly `TeamCount = 8` teams** (`LeagueSimulator.cs:211–216`).
- `TierEffects` has six delta rows (HS −20, College −16, MinorA −12, MinorAA −8, MinorAAA −4, MLB 0).
- `schools.json` brands the 8 HS teams, and boot cross-checks it against the HS roster (`GameManager` ~L566–584; it fails loudly on a mismatch).

## 2. Hard constraints

- **Allowed paths** (everything else is read-only):
  - `Assets/Data/Database/`: `SchemaDefinitions.sql`, `DatabaseManager.cs`, `BaseballDtos.cs`, `BaseballQueries.cs`, `NarrativePollQueries.cs`, plus new query/DTO files for the new tables.
  - `Assets/Simulation/Baseball/`: `LeagueGenerator.cs`, `LeagueDirectory.cs`, `TierEffects.cs`; **compile-only** edits (enum renames, no behavior change) in `LeagueSimulator.cs`, `CareerManager.cs`, `PromotionManager.cs`, `DevelopmentManager.cs`, `HsSeasonCalendar.cs`, `StatsNormalizer.cs`.
  - `Assets/Core/GameManager.cs` (boot order and the clean-break hook).
  - `Assets/UI/`: one new minimal clean-break dialog (scene + script), plus compile-only tier-name edits in `PlayerScreen.cs`, `LeagueScreen.cs`, `CalendarScreen.cs`, `NewGameScreen.cs`.
  - `Assets/Platform/Steam/AchievementManager.cs`, `RichPresenceWriter.cs`: compile-only (§4.6).
  - `Assets/Economy/Family/FamilyService.cs`: compile-only, if the enum change touches it.
  - `Assets/Data/Schools/schools.json` and `Assets/Data/League/home_parks.json`: folded in from 13-1a (Flash), plus their loader (`Assets/Data/League/`).
  - `Tools/SchemaValidator/`, `Tools/MonteCarloHarness/`, `Tools/CoreLoopHarness/`, `Tools/GrittyEventsHarness/`: version pins, enum renames, new checks and the explicit retirement of migration checks (§5.4).
  - `docs/progress.md` (one line) and this brief's §9 review section (the reviewer's).
- **Not allowed:** `Assets/Simulation/Arcade/` (it mirrors the tier as its own type, `ArcadeTuning.cs:3`, and must stay that way; editing it pulls in the ArcadeHarness gate), `Assets/Simulation/Life/`, `AtBatResolver.cs`, `BaseOutMatrices.cs`, `MicroGame.cs`, `LeagueSchedule.cs`, any spec under `docs/design/` (a spec error goes in the handoff), `GEMINI.md`, `.agents/`.
- **Calibration contract.** The resolver and its §8/§5 per-PA guard stay byte-exact. 13-1 changes no probability, weight or tier delta value, and drops only the three Minor rows. The season-pipeline guard (PA 48384 / H 10969 / ER 5237) **will move**, because world-gen changes. 13-3 retires it under §11.3, so 13-1 reports the new numbers and must not re-baseline it.
- **Determinism.** World-gen is a pure function of the seed: the same `RngState` seed gives byte-identical tables. Draw order is fixed: tier HS → College → Pro, team id ascending, then roster slot. Keep the per-player fork so person stats don't shift rating draws (the HS-2 pattern).
- **Architecture** (GEMINI §1). World-gen and the boot hook are Core/Baseball. The dialog only reads a state flag and emits an intent (`ConfirmCleanBreak` / quit). It never touches the file, the DB or any `RngState`.
- **Database rules** (GEMINI §4). Validate every new table and column through `validate_sqlite_schema` or the sqlite MCP on a **scratch** DB (`C:/Users/DELL/dnd-scratch/schema_scratch.db`; rebuild with `dotnet run --project Tools/SchemaValidator -c Release -- --emit <path>`), never the live save. Use parameterized SQL and one transaction for world-gen.
- **Identity stays out.** `Player_Identity` (gender, ethnicity, listed position) is **v15 / L-1**, not 13-1. `Player_Fielding.defensive_position` is the sim's position and the only one (GEMINI §11).
- **Names.** Fictional names only. Don't add MLB's or the NCAA's championship-series names, real league names, or "Diamond League" (§2.2). The Pro label stays the placeholder `PRO`.
- **Content lines** (GEMINI §10): untouched. 13-1 writes no narrative content.

## 3. Inputs

### 3.1 Spec decisions to implement as written

- Enum `LeagueTier : byte { HS = 0, College = 1, Pro = 2 }` (§2.1). The unrostered sentinel `-1` is unchanged.
- Team ids `TierTeamIdBase(tier) = (tier + 1) * 100` for every tier. HS 101–112, College 201–224, Pro 301–332. The MLB 1–8 special case is gone (§2.2).
- `Teams.league` labels `HS` / `CBL` / `PRO` (display only).
- Conferences and divisions per §2.3 and the Appendix A tables, exactly: team order, names, abbreviations.
- v14 DDL per §12: `Conferences`, `Divisions`, the new `Teams` columns (`conference_id`, `division_id` (NULL for HS), `home_park`), `Schedule_Games`, `Playoff_Series`, `Team_Seasons`, `Roster_Designations`, `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks`, plus `Player_Fielding` (arcade §8.1). 13-1 **creates** every v14 table so the version is complete. Only the world tables are **populated** (§4.3); later slices fill the rest.
- Clean break per §14: `user_version < 14` is refused, the old file is renamed `dirt_and_diamonds.v13.bak` and never deleted, and a new world is generated.
- Ages per §5.3: HS 15–18, College **18–22**, Pro 21–36.
- Tier deltas per §11.1: HS −20, College −16, Pro 0.

### 3.2 Interpretations this brief makes (the reviewer checks them; the owner can overrule)

1. **`Team_Tiers` stays a table, narrowed to `CHECK (tier BETWEEN 0 AND 2)`, and loses the v6→v7 backfill.** §12 says "Teams: tier is 0..2" without saying which table holds it. Keeping `Team_Tiers` keeps every shipped tier join intact (`BaseballQueries`, `NarrativePollQueries`, `FamilyService`, Steam). With a clean break, every `Teams` change goes into the `CREATE TABLE` itself; there is no `ALTER`.
2. **The old migration machinery is dead code.** The v3→v4, v6→v7, v9→v10 and v10→v11 backfills in the DDL and `LeagueGenerator.EnsureV4` / `EnsureTierLeagues` only ever ran on migrated saves. Remove the ones v14 makes wrong (the tier-5 backfill) or impossible (the top-ups). Keep a backfill only if a fresh v14 world actually depends on it, and list every removal in the handoff. `GenerateIfEmpty` becomes the single v14 world-gen.
3. **Per-tier roster generation targets** (§5.1 gives limits, and targets only for HS; these are proposals for the reviewer):
   - HS: SP 3–4, RP 2–4, position 11–14 (as specified).
   - College: **4 SP / 5 RP / 15 position = 24**, inside every College cap (≤ 4 / ≤ 5 / ≤ 17, org 26) and leaving 2 org slots for recruiting.
   - Pro: **exactly 4 / 5 / 15 = 24** (the Pro org cap). Count draws come from the rng in the fixed order above.
   - Designations (`Roster_Designations`) stay empty in 13-1; 13-5 writes them.
4. **`Player_Fielding` generation.** Every player gets a row, pitchers `P`. Each team's position players cover all 8 field positions at least once (C, 1B, 2B, 3B, SS, LF, CF, RF). The rest of each roster draws a primary position from a fixed table, and `secondary_position` is NULL or a neighbouring position. `speed` and `arm` roll the shipped rating bell (spread 25, `DefaultRatingSpread`). Mark the position weights and the secondary rate as **PROPOSAL, Math-owned (GPT 6 Astra Pro, G-1/G-4)**; 13-1 owns only the "all 8 covered" invariant. Don't touch the existing `Player_Ratings.fielding` column (its arcade meaning is G-4's to define).
5. **Clean-break flow with consent.** The spec's dialog asks "Start a new career?", so the file can't be renamed before the player answers. Boot order becomes: open → read `user_version` **before** `ApplySchema` → if it is 1–13 (an old save) or > 14 (a newer build's save), set `GameManager.PendingSaveRefusal` and stop the career boot there. The dialog offers **Start a new career** (→ `ConfirmCleanBreak()`) or **Quit**; for a version > 14, only Quit (never rename a newer save).
   - `ConfirmCleanBreak()` checkpoints the WAL (reuse `DatabaseManager`'s existing `wal_checkpoint(TRUNCATE)` helper, ~L160, which already handles a lingering reader), closes the connection, renames the `.db` to `dirt_and_diamonds.v13.bak` (never overwrite: if that name exists, use `.v13.1.bak`, `.v13.2.bak`, …), moves any remaining `-wal`/`-shm` sidecars with it under the same suffix, then re-runs the boot from the top on a new file.
   - `user_version = 0` (no file, a 0-byte file like the one found on 2026-10-09, or an empty DB) is a fresh world, with no dialog.
   - Steam Auto-Cloud syncs only `dirt_and_diamonds.db` (`DatabaseManager.cs` ~L163), so the `.bak` stays local. Say so in the dialog's subtitle string.
   - Player-facing text goes through strings-table keys, never C# literals. The dialog uses the current theme as is; 15b restyles it.

### 3.3 Seed data (Gemini 3.8 Flash, in parallel; reviewed by Claude Opus before 13-1 consumes it)

- **4 new `schools.json` entries** for Brookhaven Bulldogs (BRK), Cedar Grove Comets (CDG), Granite Falls Gators (GRF) and Hillcrest Hurricanes (HLC), following the file's entry contract (team_id, abbreviation, school_name, mascot, colors). Team ids follow Appendix A order: North 101–106 (the six North schools in table order), South 107–112. **That renumbers 4 of the 8 shipped schools** (shipped HS ids 101–108 were East/West blocks of 4). Re-key every entry, not just the new ones, and boot's cross-check will catch a miss.
- **`home_park` for all 68 teams**: fictional, trademark-safe park names. If Flash's list isn't ready, 13-1 ships the placeholder `"{City} Ballpark"` (HS: `"{School} Field"`), each marked as a placeholder in a code comment. G-7's venue catalog replaces them later.
- Flash writes into a data table that 13-1 consumes, never into C# logic. **Brief: [13-1a_seed_data_flash_brief.md](../archive/13-1a_seed_data_flash_brief.md)**, which delivers `schools.json` (12 entries) and `Assets/Data/League/home_parks.json` (68 rows) on `p13/13-1a-flash`. 13-1 reads `home_parks.json` with the same load-and-validate pattern as `schools.json` (loud failure on a missing or mismatched row), and its allowed paths include that file and `Assets/Data/League/`. The 4 new schools' image files don't exist yet: the career boot is expected-red anyway (§5.3), and the team picker showing missing art for them is listed as open for the art pipeline.

## 4. Outputs

1. **`SchemaDefinitions.sql` v14**: the full §12 DDL plus `Player_Fielding`, `Team_Tiers` narrowed (§3.2.1), dead backfills removed (§3.2.2), `PRAGMA user_version = 14`. STRICT tables, FKs, `CHECK` ranges (tier 0–2, ratings 0–100, `defensive_position` in the 9-value set, `Schedule_Games.phase` 0–2, `status` 0–2, `best_of` in (1, 3, 5)), and the §12 index.
2. **`BaseballDtos.cs`**: the three-value enum; DTOs for the conference, division and fielding rows; `TeamRow` gains `ConferenceId`, `DivisionId?` and `HomePark`. Update the doc comments that describe the ladder.
3. **Query layer**: every `COALESCE(tt.tier, 5)` becomes `COALESCE(tt.tier, 2)`. Add loaders/writers for `Conferences`, `Divisions`, `Player_Fielding` and the new `Teams` columns. Parameterized and plan-cached.
4. **`LeagueGenerator`**: one deterministic `GenerateIfEmpty` that writes 3 tiers → 2 + 2 + 2 conferences → 0 + 6 + 8 divisions → 68 teams (Appendix A, uniform id bases) → per-tier rosters (§3.2.3) with ratings, roles, arsenals, potential, person rows and `Player_Fielding`, all in **one transaction**. `TierAgeRolls` and `TierLeagueLabels` cut to three rows. `TeamSeeds`/`TierTeamSeeds` replaced by one Appendix A table (all 40 shipped identities kept; Pro takes the 8 MLB + 24 minor-league seeds).
5. **`LeagueDirectory.TierCount = 3`** and **`TierEffects`** with three rows; every `TierCount` loop still compiles and runs to 3.
6. **Enum fallout, compile-only, with the meaning preserved:**
   - `MinorA`/`MinorAA`/`MinorAAA`/`MLB` → `Pro` wherever "pro ball" was meant.
   - `LeagueSimulator`'s default `tier = LeagueTier.MLB` → `Pro`.
   - `RichPresenceWriter.TierToken`: `Pro` → a Pro token. Keep the pinned token constants and drop the minor ones.
   - `AchievementManager` ~L227–231: `>= MinorA` → `>= Pro`, `== MLB` → `== Pro`. **This makes two achievements fire on the same event.** Leave it, and list it in the handoff for 13-7 (achievement redesign is 13-7's job).
   - `PlayerScreen` tier names → 3 strings.
   - `PromotionManager` keeps compiling (13-6 replaces it). Its six-tier ladder logic is expected-red; don't redesign it.
7. **`GameManager`** boot order per §3.2.5, plus the minimal dialog.
8. **Harness changes** (§5).
9. **Handoff note** and one `progress.md` line.

## 5. Tests and gates

### 5.1 Must pass

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings, 0 errors** |
| Schema | `dotnet run --project Tools/SchemaValidator -c Release` | All green. The count changes from 111 (new v14 checks, retired migration checks listed per §5.4). It includes: applies at **v14**; re-apply idempotent; every §12 table and column present (introspected via `pragma_table_info`); `foreign_key_check` clean; `integrity_check` ok; `CHECK` rejects tier 3, a bad `defensive_position`, `best_of = 2`; FK rejects an orphan `Player_Fielding` row |
| Clean break | SchemaValidator (new checks on scratch files) | A v13 file → refusal flag set, file untouched until confirm; confirm → `.v13.bak` exists with the original bytes (hash match), sidecars moved, new file at v14; an existing `.v13.bak` → `.v13.1.bak`, nothing overwritten; a v15 file → refused, never renamed; a 0-byte / missing file → fresh world, no flag |
| World-gen | `dotnet run --project Tools/MonteCarloHarness -c Release -- --only worldgen-v14` (new filter: runs only this suite and exits non-zero on any failure; the default invocation is unchanged) | 68 teams (12/24/32); ids exactly 101–112, 201–224, 301–332; conference/division membership equals Appendix A cell by cell; HS division NULL; per-team roster counts inside §5.1 and §3.2.3; ages inside §5.3; every player has ratings, a role (pitchers), an arsenal (pitchers), potential, a person row and **exactly one** `Player_Fielding` row; each team covers all 8 field positions; no player on two teams; FK clean |
| Determinism | same suite | Two generations from the same seed → byte-identical dumps of every world table (ordered by PK); a different seed → different ratings, same structure |
| Allocation | none new | World-gen is load-time code, so allocation is allowed. Nothing on the per-PA or per-day path changes in 13-1, so no zero-alloc check moves |
| Isolation | `git diff --stat origin/p13/integration...HEAD` | Only §2 allowed paths. No `Assets/Simulation/Arcade/`, `Assets/Simulation/Life/`, `AtBatResolver.cs`, `BaseOutMatrices.cs` or `MicroGame.cs` |

### 5.2 Must not move

- **The resolver guard** (MC per-PA §8 batch + §5 fixtures): byte-identical to the baseline. If the default MC run can't reach those checks because an earlier world-building suite throws, run them in isolation (add them to the `--only` filter as `resolver`) and report both runs.

### 5.3 Expected red on `p13/integration` after 13-1 (owner-approved 2026-10-18; the handoff reports the actual list)

- **Live career boot** stops at `LeagueSimulator.Initialize` ("requires exactly 8 teams"), since the per-tier team count and the schedule belong to 13-2/13-3. A fresh boot gets through schema + world-gen + the schools cross-check, then throws there. *Alternative (not recommended):* make `TeamCount` per instance inside 13-1. The circle-method schedule works for any even count (12/24/32 are all even), but it touches the zero-alloc buffers and drags 13-1 into the full MC gate that 13-2 owns.
- **MonteCarlo default suite**: every suite that generates a world and runs a season (season-pipeline guard, tier ladder, availability, promotion). Report pass/total and the failing suite names, never a deleted check.
- **CoreLoop / GrittyEvents**: whatever depends on the old world shape or `COALESCE(tt.tier, 5)` (e.g. Gritty ~L2258–2283 asserts tier 5). Update the version pins and the tier-5 assertions to the v14 meaning (that's a spec change, not a loosened check). Anything that needs a running league is expected-red.
- **`PromotionManager`** behavior (13-6 replaces it).

### 5.4 Retiring checks

A clean break makes some checks meaningless: the v3→v4 and v6→v7 migration fixtures, and the "pre-v7 tier defaults to MLB" check. Retire each one **by name, with the reason**, in the handoff, and add its v14 replacement where there is one (e.g. "missing tier reads as Pro"). Never delete a check just to make a count match.

## 6. Definition of done

*Ticked by Claude Opus 5.5 on 2026-10-18 at `0db094d`; evidence in §8.1.*

- [x] Preconditions §0 recorded (base sha, baseline numbers).
- [x] v14 DDL complete; `validate_sqlite_schema` run on a scratch DB; `user_version = 14`.
- [x] Three-value `LeagueTier`; `TierCount = 3`; three `TierEffects` rows; no `MinorA`/`MinorAA`/`MinorAAA`/`MLB` identifier left in `Assets/` or `Tools/` (`git grep -n -E "LeagueTier\.(Minor|MLB)"` → none).
- [x] No `COALESCE(tt.tier, 5)` left (`git grep -n "tt.tier, 5"` → none).
- [x] World-gen produces Appendix A exactly, deterministically, in one transaction, with `Player_Fielding` for every player.
- [x] `schools.json` has 12 entries keyed 101–112; boot's cross-check passes (run headless through the same `ValidateAgainstRoster`; not run in a live Godot boot).
- [x] Clean-break boot + dialog behave per §3.2.5; the `.bak` is never deleted or overwritten (the flow is proven headless through `SaveCompatibility`; the dialog itself is not exercised in a live Godot boot).
- [x] §5.1 all green; §5.2 unchanged; §5.3 actual list recorded; §5.4 retirements listed.
- [x] Committed on `p13/13-1-opus` and pushed. Not merged.
- [x] Handoff + one `progress.md` line. **Then stop.**

## 7. Out of scope

- `RosterCapacity`, per-team counts, regulation innings, starter selection, `MinRestDays` (**13-2**).
- `SeasonScheduler`, `TierSeasonCalendar`, populating `Schedule_Games`, bands, the new Pro guard (**13-3**).
- Playoffs, ranking, `Team_Seasons` writes, `ChampionshipWonEvent` (**13-4**).
- The Sunday lock, `Roster_Designations` writes, forfeits, retiring the call-up (**13-5**).
- Graduation, recruiting, the draft; replacing `PromotionManager`; the enrollment/offer/draft tables' writers (**13-6**).
- Achievement and rich-presence redesign, standings UI, content tier docs (**13-7**). 13-1 only keeps them compiling.
- `Player_Identity`, legends, poverty (**L-1**, schema v15, which branches from `p13/integration` after 13-1).
- The venue catalog (**G-7**); `EffectiveRatings` and what `Player_Fielding` means to the arcade engine (**G-4**).
- Any edit under `docs/design/` (errors go in the handoff), and merging anything.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built>  · Base: p13/integration @ <sha>  · Commit: <sha>
Gates: build → <w/e>; SchemaValidator → <p/t>; worldgen-v14 → <p/t>; determinism → <same/diff>;
       resolver guard → <byte-exact?>; isolation → <empty?>
Expected red (actual): <suite → p/t, reason>; retired checks: <name → reason → replacement>
Season guard moved to: PA <n> / H <n> / ER <n> (report only; 13-3 records the new Pro guard)
Interpretations used: §3.2.1–5 <kept / changed and why>
Open: <owner/team-name items, Flash data status, AchievementManager double-fire for 13-7, risks>
Recommended next: 13-1 review by GPT 6 Astra Pro; then 13-2 by Claude Opus (reviewer: GPT 6 Astra Pro)
```

Then add one compact line to [progress.md](../progress.md). Don't start 13-2.

### 8.1 Handoff as delivered (Claude Opus 5.5, 2026-10-18)

```text
Done: schema v14 (§12 + Player_Fielding), LeagueTier HS/College/Pro, Appendix A world-gen (68 teams,
      one deterministic transaction), consent-based clean-break boot + dialog
      · Base: p13/integration @ 4e6002d (+ fold of p13/13-1a-review-opus @ 1a2c749) · Commit: 0db094d
Gates: build → 0 warnings / 0 errors; SchemaValidator → 167/167; worldgen-v14 → 23/23;
       determinism → same (seed → byte-identical dumps, sha A4B89F1135D8; other seed → same structure,
       new ratings); resolver guard → byte-exact (13/13, every line identical to the baseline incl.
       the allocation sink); isolation → no forbidden path (one out-of-list file: Assets/UI/Main.cs)
Expected red (actual): MonteCarlo default 154/180. Green: resolver 13/13, worldgen-v14 23/23,
       micro-analytic, read-model-a0, development-curve, scouting-grade, portrait-tile. RED (25 suites):
       season-pipeline 2/4, micro-game 0/1, career-ids 0/1, career-wiring 0/1, v4-bullpen-arsenal 1/2,
       rivalry 3/4, heir-genetics 9/10, lineage-succession 0/1, lineage-failure 0/1, succession-edge 0/1,
       nurture-blend 8/9, conception-request 0/1, backstory-person 9/10, person-effects 15/16,
       tier-ladder 2/3, availability 3/4, equipment 5/6, promotion-sweep 3/4, promotion-stream 0/1,
       promotion-avatar 0/1, promotion-record 0/1, development-arc 3/4, development-stream 0/1,
       practice-seam 5/6, development-equilibrium 0/1. Causes: LeagueSimulator "requires exactly 8
       teams" (19), PromotionManager 6-tier check (2), MicroGame 9+5+3 attended-team check (2),
       fixtures hard-coding team ids 1–8 (2), season-pipeline 8-team roster shape (1).
       GrittyEvents 363/363, CoreLoop 22/22 (pins + tier assertions moved to the v14 meaning).
       Live career boot: stops at LeagueSimulator.Initialize as approved (not run in Godot).
Retired checks: SchemaValidator "v4 backfill: pitcher role defaults to Starter", "v4 backfill: three
       arsenal rows, usage sums to 100", "v4 backfill: stuff-derived ratings (80 → FB 80/40, BRK 60/80,
       OFF 55/85)", "v4 backfill is OR IGNORE (re-apply never clobbers existing rows)" → the v3→v4
       backfill is gone (clean break) → "v14 clean break: re-apply backfills no role, arsenal or
       potential". MC "EnsureV4 tops a short bullpen back up and is then a no-op" → v3→v4 top-up gone
       → worldgen-v14 "every pitcher has a role and a 3-pitch arsenal…". MC "EnsureTierLeagues seeds 5
       missing tiers once (idempotent): 48 teams…" → v6→v7 top-up gone → worldgen-v14 "68 teams,
       12 HS / 24 College / 32 Pro" + per-team rosters. The pre-v7 "missing tier reads as MLB" readings
       became "missing tier reads as Pro" (SchemaValidator + Gritty).
Season guard moved to: not computable in 13-1 — the guard's suite (tier-ladder) stops at the HS
       sim's 8-team check before any season runs. 13-3 records the new Pro guard.
Interpretations used: §3.2.1–5 kept as written. Details: backup tag is always ".v13.bak" (spec name)
       whatever the pre-v14 version; the Player_Person standing heal is the one DDL backfill kept (intake,
       fillers and newborns rely on it); RichPresence keeps emitting the minted #Tier_MLB for Pro until
       17a; fielding depth weights / secondary rate / neighbours are Math PROPOSALs on their own stream.
```

### 8.2 Review condition resolved (Claude Opus 5.5, 2026-10-18; owner chose the strings table)

GPT's §9.1 condition (the dialog's copy was scene source text, not strings-table keys) is resolved:

- New project strings table `Assets/Localization/strings.csv` (Godot CSV translation, `keys,en`; dotted lowercase keys, the `league.college.final.name` style). Six keys `ui.clean_break.{title, older.body, older.subtitle, newer.body, quit, start}`; the English text is unchanged.
- `CleanBreakDialog.tscn` texts are now those keys (Godot auto-translate renders them); `project.godot` gains `[internationalization] locale/translations` → `res://Assets/Localization/strings.en.translation`. The importer output (`strings.csv.import`, `strings.en.translation`) and the `.uid` files of the four new 13-1 scripts are committed, the repo's `*.import` policy.
- Gates: build 0/0; SchemaValidator 167/167; new MC suite `--only ui-strings` 3/3 (table well-formed, every keyed-scene text has a row, project registration and import target exist; a mutated key fails it); MC default **157/183** (= 154/180 + 3, the same 25 approved red suites); resolver lines identical to the baseline; Gritty 363/363. Headless Godot 4.7 with `APPDATA` redirected to scratch: locale `en_US`, all six keys resolve to their English text.
- Correction to the progress line: **six** checks were retired (four SchemaValidator, two MC), as §8.1 and §9.3 list, not five.
- Not re-reviewed by GPT: this follow-up landed after the §9 verdict. The owner directed the merge into `p13/integration` after it.

## 9. Review (GPT 6 Astra Pro)

### 9.1 Verdict and scope (2026-10-18)

**Approve with conditions** for `p13/integration` only. Reviewed `bff655e` (implementation `0db094d`) against `4e6002d`; fetched refs resolve to those exact commits. No merge performed. The owner-approved §3.2 interpretations and §5.3 expected reds stand. No blocking sim/schema defect found. Before merge, resolve the localization condition below by implementing the brief's strings-table requirement or obtaining an explicit owner exception for source-text translation keys. This is not approval to ship the currently expected-red career build.

**Findings / conditions:**

- **[P2, brief compliance] Dialog localization:** `Assets/UI/CleanBreakDialog.tscn:34` and the other five `text` properties contain English source text; the change supplies no strings-table entries. `CleanBreakDialog.cs:19` explicitly substitutes “translation-ready scene text” for §3.2.5's strings-table keys. Godot can translate scene source strings, so this is not a broken English dialog, but it is an undisclosed departure from the approved delivery requirement. Opus should supply the requested keys/catalog, or the owner should explicitly accept the source-string-key convention for this slice. No localization files were added by this review.
- **Main.cs exception accepted:** `Assets/UI/Main.cs:115` hosts the refusal dialog, parks UI overlay processing, and restores it after confirmation. It only reads refusal state and changes presentation nodes; it writes no DB/file/sim state and consumes no RNG. `CleanBreakDialog` sends the confirmation intent to Core. This small out-of-list edit is necessary to make the new dialog reachable and prevent overlays polling an unconstructed career. The folded 13-1a brief/data changes are provenance from the approved seed-data fold, not a hidden sim expansion.
- **RichPresence exception accepted for integration, due in 17a before release:** `Assets/Platform/Steam/RichPresenceWriter.cs:48` retains the minted `#Tier_MLB` wire token for Pro. Keeping an existing partner-site key avoids emitting an unregistered token. It differs from §4.6's Pro-token wording, so 17a must coordinate `#Tier_Pro` and its partner localization; this review does not authorize shipping the old league branding. The achievement double-fire remains the disclosed 13-7 item.
- **Math sign-off is provisional, limited to 13-1:** accept the labelled depth weights `3/1/2/1/2/1/2/1`, secondary rate `0.35`, and neighbour rows in `LeagueGenerator.cs:67–98` as schema/world-gen proposals, not calibrated arcade/lineup policy. All weights are positive (sum 13); neighbours are legal, distinct from the primary, and pitchers have no secondary. Guaranteed first-eight coverage is independent of those draws. Seven Pro depth draws yield a second primary catcher with probability `1-(10/13)^7`, about 84%; they do **not** guarantee two catchers. G-4 must address arcade §8.2's two-catcher target and validate utility/position suitability before these proposals become final. This does not reopen the owner's §3.2.4 decision that 13-1 guarantees only all-eight coverage.

### 9.2 Independent gate re-runs

| Gate | Reviewer result |
| :-- | :-- |
| `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** |
| `dotnet run --project Tools/SchemaValidator -c Release` | **167/167** |
| `dotnet run --project Tools/MonteCarloHarness -c Release -- --only worldgen-v14`, two separate invocations | **23/23 each**; both same-seed dump pairs hash **A4B89F1135D8**, 653,924 characters; different-seed structure hash **A8DE3B874F17**, ratings differ |
| `dotnet run --project Tools/MonteCarloHarness -c Release -- --only resolver` | **13/13**; all 13 PASS/result lines exactly equal the untouched base harness's first 13 results, including allocation sink **73839**; league line `.247/.316/.412`, OPS `.728`, K `22.4%`, BB `9.2%`, HR/PA `3.2%` |
| `dotnet run --project Tools/MonteCarloHarness -c Release` | **154/180**, exact §8.1 red suite names/counts and causes reproduced (below) |
| `dotnet run --project Tools/GrittyEventsHarness -c Release` | **363/363** |
| `dotnet run --project Tools/CoreLoopHarness -c Release` | **22/22** |
| `git diff --stat origin/p13/integration...origin/p13/13-1-opus` | 32 files, +3115/-625 including folded seed-data/docs; Main.cs exception assessed above; zero diff in resolver, BaseOutMatrices, MicroGame, Arcade or Life |

The base resolver comparison used an untouched `git archive` extraction of `4e6002d` inside this clone's `.git/review-scratch`. Its default harness reached and passed all 13 resolver checks before a later missing `Assets/Data/Items/items.json` in the deliberately limited extraction stopped unrelated suites (161/162 overall). That extraction run is **not** claimed as a full baseline gate. Only its completed resolver output is used for the comparison. The head's full default gate did run normally against the complete clone.

Actual reds: `season-pipeline 2/4`; `micro-game 0/1`; `career-ids 0/1`; `career-wiring 0/1`; `v4-bullpen-arsenal 1/2`; `rivalry 3/4`; `heir-genetics 9/10`; `lineage-succession 0/1`; `lineage-failure 0/1`; `succession-edge 0/1`; `nurture-blend 8/9`; `conception-request 0/1`; `backstory-person 9/10`; `person-effects 15/16`; `tier-ladder 2/3`; `availability 3/4`; `equipment 5/6`; `promotion-sweep 3/4`; `promotion-stream 0/1`; `promotion-avatar 0/1`; `promotion-record 0/1`; `development-arc 3/4`; `development-stream 0/1`; `practice-seam 5/6`; `development-equilibrium 0/1`. These are 25 red suites / 26 failed checks: 19 eight-team simulator guards, two promotion-ladder guards, two attended-team shape guards, two old-team-id fixtures, and one old roster-count assertion. No new failure cause. The season guard remains unreachable, not re-baselined.

### 9.3 Schema, generation and retirement audit

- Compared v14 DDL with league_restructure §12 and arcade §8.1: all required tables/columns, STRICT declarations, FKs, tier/rating/position/phase/status/best-of checks and `ix_sched_day` are present. HS division NULL is generated correctly; missing tier reads Pro. Existing unrelated v13 table shapes carry forward. Removed role/arsenal, tier-5 and potential migration fills and both generator top-ups are appropriate to the approved clean break; only the idempotent `Player_Person` standing heal remains.
- Read all 68 `LeagueStructure.Teams` entries and conference/division definitions against Appendix A; the rerun harness also parses the design document independently and compares each persisted cell, rather than merely comparing the generator with itself. IDs 101–112 / 201–224 / 301–332, names, abbreviations, conference/division membership and labels match. Schools/parks validation passes.
- HS generates 3–4 SP, 2–4 RP, 11–14 position players; College and Pro generate 4/5/15. Ages are HS 15–18, College 18–22, Pro 21–36. Every generated player has the required rows, exactly one fielding row, and every team covers all eight non-pitcher positions; pitchers are P. Later-slice tables remain unpopulated.
- Draw order is explicit: two substream seeds, ascending HS/College/Pro team IDs, position/SP/RP slots; the per-player discount/person fork remains. Fielding and arsenal streams are separate from rating draws. Fixed-count tiers consume no count draw. One BeginBatch/CommitBatch covers structure and players; the late missing-park rollback gate passes. Query additions use bound parameters and cached commands. HS/College/Pro deltas remain -20/-16/0. Protected engines have no diff.
- Retirement count clarification: there are **six named removals** in §8.1 (four SchemaValidator checks plus two MC checks), not five. Each is justified below; the old missing-tier expectation is updated, not silently dropped.

| Retired check | Reason | Replacement checked |
| :-- | :-- | :-- |
| `v4 backfill: pitcher role defaults to Starter` | No v3 migration under the v14 clean break | `v14 clean break: re-apply backfills no role, arsenal or potential`; generated pitcher-role invariant |
| `v4 backfill: three arsenal rows, usage sums to 100` | Removed migration-only arsenal fill | Same negative re-apply check; worldgen pitcher three-pitch/usage invariant |
| `v4 backfill: stuff-derived ratings (80 → FB 80/40, BRK 60/80, OFF 55/85)` | Those deterministic migrated values are no longer produced | Negative re-apply check and generated arsenal validity; no claim that fresh arsenals use the retired values |
| `v4 backfill is OR IGNORE (re-apply never clobbers existing rows)` | There is no role/arsenal backfill to re-apply | Negative re-apply check plus world-gen idempotence/dump checks |
| `EnsureV4 tops a short bullpen back up and is then a no-op` | Approved removal of the v3 world top-up | Per-tier roster counts and every-pitcher role/arsenal worldgen checks |
| `EnsureTierLeagues seeds 5 missing tiers once (idempotent): 48 teams…` | Approved replacement of six-tier migration with fresh 68-team generation | 12/24/32 shape, Appendix A membership, complete roster and idempotence checks |

The former missing-tier-as-MLB expectations now assert Pro in SchemaValidator/GrittyEvents, consistent with the changed enum and default. No retirement is being used to hide one of the approved season/promotion failures.

### 9.4 Godot scratch-save exercise

Godot **4.7.stable.mono.official.5b4e0cb0f** is available and was exercised headlessly with the actual GameManager, Main scene and CleanBreakDialog. A small temporary SceneTree driver instantiated Main and emitted the real Start button's `pressed` signal. APPDATA was redirected to this clone's `.git/review-scratch/appdata`; a separate probe verified `OS.get_user_data_dir()` there before the game ran. No live save was read or written. This verifies scene/control behavior, not a visual/manual accessibility pass.

- Synthetic v13 fixture made from the base's full schema plus a sentinel: dialog present, Start visible, refusal occurs before schema/world writes; pre-consent bytes unchanged.
- A first fixture used SQLite's DELETE journal mode; confirmation changed its header when DatabaseManager enabled WAL, so its backup was logically preserved but not byte-identical. This limits the literal byte-preservation claim for arbitrary external SQLite files. The shipped manager already uses WAL, so the fixture was corrected to the actual game save format and rerun; no product code was changed.
- WAL v13 fixture SHA-256 `dcb9750a804dc2d5b4d5d563d7d28e2c0ec5d791af2b590278d3616a394812bc`: confirmation produced a **byte-identical** `.v13.1.bak`, left the existing `.v13.bak` unchanged, and created a **v14** file. The first run also verified **68 teams**. Boot then hit exactly the approved HS “requires exactly 8 teams; found 12” guard, after world generation and school validation.
- Synthetic WAL v15 fixture: dialog present, Start hidden, bytes unchanged, original filename retained, no new backup. Core's reinspection/rejection of a direct newer-save confirmation and sidecar/backup collision cases also passed in SchemaValidator.
- Steam initialized in its existing unavailable/offline fallback because the native DLL was unavailable; no Steam write was attempted. Temporary Godot import metadata was removed from the review diff.

### 9.5 Owner handoff

Review records only: this §9 and one progress line, committed on `p13/13-1-review-gpt` (the commit containing this section identifies the review). No source fixes, no merges. **Recommended next: resolve/accept the localization condition, then Claude Opus merges 13-1 into `p13/integration` with owner approval (never main), then 13-2 by Claude Opus, independently reviewed by GPT 6 Astra Pro.** Keep the 17a presence-token and G-4 Math follow-ups explicit; team-name/art gates remain open as already documented. Stop here.

### 9.6 Independent localization follow-up review (2026-10-25)

**APPROVED: the section 9.1 localization condition is closed.** Separate reviewer session under current TEAM sections 1a/3, isolated branch `p13/13-1-loc-review-astra` at `14662e4`; reviewed only `8aa12a1..14662e4`, not the earlier core implementation or subsequent integration changes. No blocking findings. The six scene keys exactly cover the six unique English CSV entries and preserve the former copy; project registration and CSV import destination agree with the committed translation resource. All four script UIDs are unique in tracked content and have corresponding scripts. No simulation or save-path behavior changed.

Independent gates: `dotnet build --nologo` **passed, 0 errors / 2 NU1900 warnings** (NuGet vulnerability feed unavailable); the first sandbox invocation exited 1 without diagnostics, so the successful run used escalation. `dotnet run --project Tools/MonteCarloHarness -c Release -- --only ui-strings` **3/3 passed** (same feed warning). A temporary minimal Godot 4.7 project loaded the committed binary translation through project registration: at `en_US`, **6/6 values matched the CSV exactly**, exit 0; APPDATA was redirected to scratch, and no game/autoload/live save ran. Godot reported an incidental root-certificate-store warning. Four UID/companion checks and `git diff --check 8aa12a1 14662e4` passed. This is a resource-resolution check, not a new visual/accessibility or full-slice gate run.

The original integration-only verdict and its expected-red limitations remain; this review adds no release approval and waives no owner gate. The 17a presence-token, G-4 Math, team-name/art and later-slice items remain outside scope. Only this existing brief and the compact progress log changed; no runtime edits, merge or push. **Recommended next: GPT 6.1 Sol completes the existing 13-1 recovery/integration gate using this independent review; any new implementation requires its assigned independent reviewer.**
