# Task Brief: `13-7` — the Phase 13 ripple: Steam achievements and presence, standings and leaders, content tier docs

**Author:** Claude Opus 5.5 (integration lead), 2026-11-26. **Recommended model:** Claude Sonnet 5.5 (UI and feature engineer; TEAM.md §2 row 13-7). **Reviewer:** Gemini 4 Carbon. The diff has one small read-only `Assets/Simulation/` view and one `Assets/Data/` DTO (§2), which are Carbon's jurisdiction (TEAM.md §1a). **Owner gate:** D1–D6 (§3) before the build. **Spec:** [league_restructure.md](../design/league_restructure.md) §15 row 13-7 ("Ripple: achievements/rich presence, content tier docs, Steam README tokens, dashboard/standings/leaders cards"; gates GrittyEvents, full suite, live boot), §2.3, §8, §13; [steam_commercial_release.md](../design/steam_commercial_release.md) §3. **Branch / worktree:** `p13/13-7-sonnet` in your own worktree (`C:\Users\DELL\dnd-sonnet-137`), from `origin/p13/integration`. **Base:** `1d86e6f` (= `origin/main`; schema v14 has been on `main` since `3d0eaa5`). Every citation below was verified at `1d86e6f`.

**Status:** READY TO BUILD. The owner decided **D1–D6 = all A** on 2026-11-26 (§3), including D2's confirmation that no real Steamworks app or partner-site entry exists.

## 0. Preconditions (check first; stop and report if any fails)

1. Your own worktree on `p13/13-7-sonnet`, from `origin/p13/integration` at `1d86e6f` or a docs-only descendant. Clean tree; the write check passes (`git commit --allow-empty -m "write check"`, then `git reset --soft HEAD~1`); at least 2 GB free on `C:`.
2. This brief's §3 records the owner's D1–D6. If it doesn't, stop.
3. Baseline on that head before any edit: build 0/0; MC default **608/608** with the normalized log SHA-256 **`646f3618f74e18cfbca80bf7ebc826c1b77a2e0f2050ca0a16765f7d5d88481e`** (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`; the post-13-6b D7 anchor, `progress.md`); CoreLoop 43/43; Gritty 363/363.
4. Run `godot_scene_mapper` (or the Godot MCP) on `Assets/UI/LeagueScreen.tscn` and `Assets/UI/Main.tscn` before writing any `GetNode<T>()` (ui_conventions.md).

## 1. Context

Phase 13 changed the league: three tiers (HS 0, College 1, Pro 2), conferences and divisions, real schedules, playoffs, forfeits, the pipeline and the parent phase. Slices 13-1 to 13-6b kept the Phase 11 Steam layer and the phone's League cards **compiling**, not **correct**. 13-7 closes that gap. It is runtime-only inside v14, adds **no DDL**, and must not block Gate 2 ([scope_realignment.md](../design/scope_realignment.md) Gate 1 row).

### 1.1 What is wrong today (verified at `1d86e6f`)

| # | Finding | Where | Owner of the fix |
| :-- | :-- | :-- | :-- |
| F1 | **Achievement double-fire.** Schema v14 has one professional tier, so `ACH_WENT_PRO` (`tier >= Pro`) and `ACH_THE_SHOW` (`tier == Pro`) fire on the same event. Disclosed in 13-1 and left for 13-7 | `AchievementManager.cs:225–237`; [13-1 brief](13-1_schema_v14_worldgen_brief.md) :99, :157, :171 | 13-7 (D1) |
| F2 | **`#Tier_MLB` still on the wire** for Pro, plus MLB/minor-league copy in the README. 13-1 kept it because it was "already minted". But the app still runs on Valve's Spacewar dev id (`SteamIntegration.cs:27`, `DevAppId = 480`): **no real partner site exists, so nothing is minted yet** | `RichPresenceWriter.cs:43–48, 170–175`; `README.md:94–114, 134–135`; [13-1 brief](13-1_schema_v14_worldgen_brief.md) :236 | 13-7 or 17a (D2) |
| F3 | **Presence latches the old tier in the parent phase.** A released avatar is activated unrostered: `CareerManager.ActivateUnrostered` publishes `AvatarChangedEvent(avatarId, 0)` (`CareerManager.cs:838–853`). `PublishCareerPresence` skips the `tier` key when `TryGetTeamTier(0)` is false, so `#Tier_College`/`#Tier_MLB` stays latched, and a fresh boot of a parent-phase save (`GameManager.cs:833`) shows an empty `%tier%` | `RichPresenceWriter.cs:150–168`; [13-6 brief](13-6_pipeline_draft_brief.md) :1125 (Carbon §12.3 obs. 1) | 13-7 (D3) |
| F4 | **Standings are summed from the current roster's pitcher decisions** (`SUM(Pitching_Stats.w/l)` over `Players.team_id`). Forfeits (`Schedule_Games.status = 2`) record no decision, and Sunday-lock free agency moves a pitcher's W/L to his new team. So the League tab's standings can disagree with `Team_Seasons`, which seeding and the Draft order read | `BaseballQueries.cs:351–356`; `LeagueScreen.cs:154–212`; truth: `Standings.cs:267–316` (`TeamSeasonsWriter`, regular season, final or forfeit) | 13-7 (D4) |
| F5 | **Standings are one flat table per tier** (12, 24 or 32 rows), sorted by pct → margin → team id. The spec groups by conference and division (§2.3, §13 "Standings (conferences/divisions)"), and seeding orders by `Standings.Order` (head-to-head, run differential, runs allowed, seeded coin). The card's tie order can contradict the real seeding | `LeagueScreen.cs:180–189`; `Standings.cs:123–150`; `PlayoffManager.cs:267–334` | 13-7 (D4) |
| F6 | **Leader qualifiers are fixed at 10 PA / 15 outs** whatever the season length: HS plays 10 games of 7 innings, Pro 64 of 9 | `LeagueScreen.cs:77–79` | 13-7 (D5) |
| F7 | **`ACH_JOURNEYMAN` says "ten seasons of professional baseball"**, but `STAT_SEASONS_PLAYED` counts every rollover while a bloodline holds a career, HS and College included | `AchievementManager.cs:157–170`; `README.md:142–153` | 13-7 (D6) |
| F8 | **Stale v13 comments.** "9c promotion republish" (`PromotionManager` was deleted in 13-6a); "a save already in MLB"; "COALESCEd to MLB(5)" (it is `COALESCE(tt.tier, 2)`, `NarrativePollQueries.cs:103`); the rookie batch's "(MinorA and above)"; the calendar's "non-HS tiers… play daily" | `AchievementManager.cs:131, 144`; `RichPresenceWriter.cs:15, 122`; `GrittyEventModel.cs:92–97`; `rookie_season_events.json:2`; `CalendarScreen.cs:196–198` | 13-7 |
| F9 | **Content tier docs still say "Planned".** Six content-authoring references carry the 2026-10-09 banner "Tier ordinals become HS=0, College=1, Pro=2 in schema v14". That landed in 13-1. The 26 `tier >= 2` gates (rookie batch and underworld) still mean "professional" (v13 `MinorA = 2`, v14 `Pro = 2`), and the 25 `tier == 0` gates still mean HS, so **no content changes meaning** | `docs/reference/` `gritty_event_framework.md:5`, `hs_onboarding_events.md:5`, `criminal_underworld_content_arc.md:5`, `criminal_underworld_dirt2_the_fix.md:5`, `criminal_underworld_dirt3_product_and_turf.md:5`, `criminal_underworld_dirt4_the_bust_and_the_door_out.md:5` | 13-7 |

Already correct, so no change is needed: `PlayerScreen` tier names (`"High School,College,Pro"`, `PlayerScreen.cs:33`); `CalendarScreen` postseason games (13-4 N2, `CareerManager.cs:2023–2026`); the leaders exclude postseason (postseason player lines are discarded, `PlayoffManager.cs:60–61, 1170`); `.agents/rules/gritty_events.md:11` already states v14 ordinals.

### 1.2 The 13-7 / 17a line

**13-7 makes the shipped Phase 11 surfaces true to v14:** F1–F3 and F7, the README token tables, and a pure, harness-tested home for the decisions. **17a builds the commercial set** ([steam_commercial_release.md](../design/steam_commercial_release.md) §3): the new achievements (Signed, Walk-On, Draft Day, First-Rounder, State Champs / National Champs / Ring Bearer, Walk-Off, Grand Slam, Benched, Short-Handed, Going Straight, Banned for Life, and the Phase L proposals), **The Show redefined as "play your first Pro game"**, `AchievementUnlockedEvent`, the offline eligibility/outbox (R-1 Q14), `#Phase_Playoffs` / `#Phase_DraftNight`, the "#7 nationally" presence line, icons, the partner-site minting and the `DevAppId` swap. 13-7 adds no achievement and no event.

### 1.3 The 13-7 / 15c / SR line

13-7 corrects the **data and grouping** of the League tab's existing label-based cards in the shipped look. **15c** owns the restyle and the bracket screen (13-4 D6). **SR-4** rebuilds the Gate 2 phone screens on top of this and inherits the correct data. The schedule planner (`ScheduleScreen`) is retired by SR-2b/SR-1c, so it gets no 13-7 work.

## 2. Hard constraints

- **Allowed paths:**
  - `Assets/Platform/Steam/AchievementManager.cs`, `RichPresenceWriter.cs`, `README.md`, and one new Steam-free helper, `Assets/Platform/Steam/SteamRules.cs` (§4 item 4).
  - `Assets/UI/LeagueScreen.cs` (`.tscn` only if a node is truly needed) and `Assets/UI/CalendarScreen.cs` (comment only).
  - `Assets/Localization/strings.csv` for new `ui.league.*` keys. Re-import `strings.en.translation` headlessly and keep only the translation (memory: Godot headless import side effects).
  - **Justified sim/data touches (Carbon reviews them as sim/persistence diffs):**
    - `Assets/Simulation/Baseball/PlayoffManager.cs`: **one** new read-only view method, `GetStandings` (§4 item 5), beside `GetBracket` / `GetNationalRanking` (`PlayoffManager.cs:985–1044`, the D6-B "read-only views" block). It reads; it never writes, never batches, never draws an RNG, and nothing in the sim calls it.
    - `Assets/Data/Database/PlayoffDtos.cs`: its view records only.
  - **Comment-only touches (the gates prove inertness):** `Assets/Narrative/Events/GrittyEventModel.cs` (doc comment); `Assets/Narrative/Events/Content/rookie_season_events.json` (the `"//"` string only, no event field).
  - `Tools/MonteCarloHarness/` (the new checks, §5, and linking `SteamRules.cs`).
  - Docs: this brief, `progress.md`, and the six `docs/reference/` banners (F9).
- **Read-only:** everything else, including `SchemaDefinitions.sql`, every SQL string (leave `SqlSelectTeamRecordsByTier` / `LoadTeamRecords` in place even if unused; their removal is a later cleanup), `CareerManager`, `PipelineManager`, `LeagueSimulator`, `Standings.cs`, `GameManager.cs`, and all gritty content fields.
- **If you need anything else** (a new query, a new event, a `GameManager` property), **stop and report it to Opus.** Don't add it yourself.
- **Calibration:** MC must not move. Outside the lines this slice adds (§5), the normalized MC log stays byte-identical to `646f3618…`. The Pro guard stays PA 81433 / H 18471 / ER 8803, ledger `932ACE26…8B84`.
- **Presentation never drives the sim** (GEMINI.md §1). The Steam layer stays a pure bus subscriber whose only side effect is a guarded `SteamIntegration` call. No display wording in C# (`RichPresenceWriter.cs:23–28`): tokens and raw values only.
- **Names:** never use MLB's or the NCAA's names (MLB, "major leagues", "World Series", "College World Series") in any new token, string, constant or doc copy (GEMINI.md §2, §8). Conference and division names come from data (`LoadConferences` / `LoadDivisions`, `BaseballQueries.cs:687–709`); league and brand names stay pending the owner's trademark screen.
- **Text:** every new player-facing string is a strings-table key. No avatar pronouns. Names appear in text only.
- **Styling and focus:** Theme family only; no per-scene overrides; focus navigation on any new interactive control (GEMINI.md §7).

## 3. Owner decisions (D1–D6)

Recommendations first. **Status: DECIDED by the owner, 2026-11-26: D1 A, D2 A, D3 A, D4 A, D5 A, D6 A ("All A").** For D2 the owner confirms there is no real Steamworks app yet: no partner-site rich-presence localization and no achievement entries exist, so retiring `#Tier_MLB` costs nothing. Build only the A options below; B and C stay here as the record of what was weighed.

**D1 — The achievement double-fire (F1).**
- **A (recommended):** stop firing `ACH_WENT_PRO` and delete its const. §3 of the commercial spec already removes "Went Pro" (no minors). `ACH_THE_SHOW` keeps firing on reaching Pro until 17a redefines it as "play your first Pro game". README: mark `ACH_WENT_PRO` retired, and change The Show's copy to "Reach the Pro league."
- B: keep `ACH_WENT_PRO` on reaching Pro and stop `ACH_THE_SHOW` until 17a.
- C: leave both firing until 17a (status quo).

**D2 — Retire `#Tier_MLB` now (F2).**
- **A (recommended):** emit `#Tier_Pro` for Pro now. Rewrite the README token list to `#Tier_HS` / `#Tier_College` / `#Tier_Pro` and delete the minor-league and MLB entries. This is free while the app is on the dev id. **It assumes no real Steamworks app has rich-presence localization or achievements entered.** The owner confirms that, since creating the app is the owner's step (GEMINI.md §8).
- B: keep `#Tier_MLB` until 17a.

**D3 — Presence for an unrostered avatar in the parent phase (F3).**
- **A (recommended):** a new status token, `#Status_Family`. Suggested partner-site copy: "Season %season% · %player%, age %age% · raising the next generation". Wording belongs to the site; the token id is the contract. When the avatar has no team, clear the `tier` key (an empty value deletes a rich-presence key in Steamworks). It covers the live switch and the boot sync, and it reverts to `#Status_Career` on takeover.
- B: show `#Status_BetweenCareers` while unrostered.
- C: status quo (the latch).

**D4 — The standings card (F4, F5).**
- **A (recommended):**
  - **Source:** a read-only `PlayoffManager.GetStandings(seasonYear, tier)` view. It builds its own `SeasonTable` from `PlayoffQueries.LoadCompletedRegularSeason` (`PlayoffQueries.cs:150`) and orders each scope with `Standings.Order`, using the tier's `LeagueDirectory.Get(tier).ScheduleSeed`. That's the same function, inputs and seed selection uses (`PlayoffManager.cs:228–334`), so the card cannot contradict the seeding. W/L/RS/RA then match `Team_Seasons`.
  - **Display:** the **avatar's conference, grouped by division**:
    - HS: the conference, 6 rows, no divisions.
    - College: 3 divisions × 4, with each team's `#N` national rank.
    - Pro: 4 divisions × 4.
  - Each team's postseason seed appears once selection has set it (`TeamSeasonRow.Seed`).
- B: as A, but the whole tier, both conferences.
- C: no sim touch. Read `Team_Seasons` in the UI through a new `GameManager` exposure and keep the simple sort. Ties may contradict the seeding.

**D5 — Leader qualifiers (F6).**
- **A (recommended):** scale with games played, the familiar "per team game" rule adjusted to regulation innings (`TierRules.RegulationInnings`, `TierRules.cs:13`):
  - rate leaders (AVG, OPS): **PA ≥ ⌈G × innings × 0.344⌉**, which is 3.1 per 9-inning game and about 2.4 per 7-inning game;
  - ERA: **outs ≥ ⌈G × innings ÷ 3⌉**, which is 1 IP per 9-inning game;
  - where **G** is the avatar team's regular-season games completed (W + L from the D4 view).
  - These are presentation thresholds, not calibration; they're named constants with this comment.
- B: per-tier fixed minimums.
- C: keep 10 PA / 15 outs.

**D6 — Journeyman (F7).**
- **A (recommended):** change the copy, not the code. README: "Play ten seasons." The stat keeps counting every season a bloodline holds a career, which is what it has always done.
- B: count only seasons the avatar finishes on a Pro roster (a code change in `OnSeasonRolledOver`).

## 4. Outputs

1. **Achievements (D1, D6):** `EvaluateTier` per D1; the README achievements table per D1/D6; F8 comments fixed in the file.
2. **Rich presence (D2, D3):** the tier token per D2; the unrostered status per D3, on both `OnAvatarChanged` and `SyncFromBoot`; F8 comments fixed.
3. **README:** the "Keys the game sets" and token sections match the code exactly (D2/D3). Delete the stale "Shipped tokens (v13 build)" / "Planned change" notes (`README.md:101–111`) once the code matches; keep the sentence that 17a adds `#Phase_*`.
4. **`SteamRules.cs` (new, Steam-free and Godot-free):** the pure decisions as `internal static` functions:
   - the tier token for a `LeagueTier`;
   - the status token for (has team, lineage over);
   - the tier achievements for a `LeagueTier`.
   `AchievementManager` / `RichPresenceWriter` call them. Link this one file into `MonteCarloHarness.csproj`, because the harnesses don't compile `Assets/Platform/` (Facepunch).
5. **`PlayoffManager.GetStandings` (D4)** returns a view DTO, for example `StandingsView(SeasonYear, Tier, IReadOnlyList<StandingsGroupView>)`. A group carries `ConferenceId`, `DivisionId?` and ordered rows. A row carries `TeamId, Wins, Losses, RunsScored, RunsAllowed, NationalRank?, Seed?`. It allocates at UI time (the `GetBracket` precedent), runs on the UI thread, and is called once per (day, avatar) refresh, never per frame.
6. **`LeagueScreen` (D4, D5):**
   - standings from `GetStandings`, grouped with conference/division headers from data, the avatar's row marked as today, and GB computed per group against the group leader;
   - leaders with the D5 qualifiers;
   - the existing `(day, avatar)` dirty flag and `_tierKnown` hide (13-6b) kept unchanged;
   - new copy (headers, rank and seed formats) as `ui.league.*` keys.
7. **Comments and docs (F8, F9):** the comment fixes; the six `docs/reference/` banners turned from "Planned changes" into a dated "Landed in schema v14 (13-1)" note. They keep their history; the ordinals text says what now holds.

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings / 0 errors |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | 608 plus your new checks, all PASS. **Removing exactly the lines this slice adds** (the new checks and the changed count lines) reproduces `646f3618…`. Record both SHA-256s |
| New MC checks | in a new `--only` suite (name it, e.g. `ripple`) | (1) `SteamRules`: HS/College/Pro → `#Tier_HS`/`#Tier_College`/`#Tier_Pro` (D2-A); unrostered → the D3 status; Pro → exactly one tier achievement (D1). (2) A source scan of `Assets/Platform/Steam/*.cs`: no `#Tier_MLB`, `#Tier_Minor`, `ACH_WENT_PRO` literal (per D1/D2). (3) `GetStandings` agrees with selection: on a fixed-seed world after selection, HS top 3 per conference = seeds 1–3; Pro division leaders = the seed 1–4 set; College rows carry the written national rank. Every view row's W/L/RS/RA equals its `Team_Seasons` row, forfeits included. (4) `ui-strings` extended to `LeagueScreen.cs` and `ui.league.*` (key present, none unused, no pronoun) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 43/43 (the Life ↔ Baseball boundary scan stays clean) |
| Gritty | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 |
| Event graph | the `check_event_graph_integrity` skill | clean (the rookie batch's comment changed) |
| Godot | headless `--import` (keep only `strings.en.translation`; revert `.import`/`.uid` churn) + translation parity over every `strings.csv` row + `LeagueScreen.tscn` and `Main.tscn` load with 0 errors | exit 0; parity N/N, 0 mismatches |
| Live boot | Godot, scratch `APPDATA` | A new career boots. The League tab shows the HS conference's standings and leaders. Then record from saves (or a driven season) a College mid-season (3 division groups, `#N` ranks) and a Pro mid-season (4 division groups), and confirm one forfeit counts in the standings. Then the parent phase: no League tab and no error. Presence and achievements no-op without a Steam client, so their behaviour is proven by MC check (1) |

## 6. Definition of done

- [x] §0 recorded; D1–D6 applied as decided; §4 items 1–7 built. (Sonnet 5.5, 2026-11-26)
- [x] §5 gates green; both MC SHA-256s and the live-boot observations recorded in the handoff. (Sonnet 5.5, 2026-11-26)
- [x] Gemini 4 Carbon's review passed (§9, PASS WITH FIXES: S1, N1, N2), Opus reconciled (§10, `b0060c9`), Gemini 4 Neon's delta review passed (§11, PASS 0/0/0, `2c484c4`). Then Opus merges into `p13/integration`. (Opus 5.5, 2026-11-27)
- [x] Handoff (§8) and one `progress.md` line. **Then stop.** (Sonnet 5.5 build line, Carbon §9, Opus §10, Neon §11 and the merge entry, 2026-11-26/27)

## 7. Out of scope

- Everything 17a owns (§1.2): new achievements or events, The Show's "first Pro game" rule, `AchievementUnlockedEvent`, offline/outbox persistence, `#Phase_*`, the presence rank line, icons, partner-site entries, the `DevAppId` swap. **Never perform a Steamworks partner-site action** (GEMINI.md §8).
- The restyle, the bracket screen and Draft Night visuals (15c); SR-4's Gate 2 screens; the planner (SR-2b / SR-1c).
- Any schema, SQL, query, event or sim-behaviour change beyond the one read-only view; deleting the old standings query.
- New gritty content, or any change to a content field (only the `"//"` comment).
- The parent-phase jobs, the training mini-game and their brief (D4b); 13-7's successor slices.
- Merging, `main`.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built>  · Commit: <sha>  · Gates: build, MC (N/N; SHA-256 new + reconstructed 646f3618…), ripple suite, CoreLoop, Gritty, event graph, Godot, live boot
Open: <anything unfinished, risks, owner questions>
Recommended next: Gemini 4 Carbon reviews 13-7 on p13/13-7-review-carbon (pre-created by you or Opus); then Opus reconciles and merges
```

Then add one compact line to [progress.md](../progress.md), and write the complete prompt for the next model (TEAM.md §3 step 8). Don't start the next slice.

---

## 9. Review — Gemini 4 Carbon

**Reviewer:** Gemini 4 Carbon (simulation, calibration and math owner; independent reviewer of `13-7` per [TEAM.md](../TEAM.md) §1a/§2), **2026-11-26**.  
**Branch / worktree:** `p13/13-7-review-carbon` in `.worktrees/13-7-review-carbon`, reviewing `origin/p13/13-7-sonnet` at `6a49dae` (`aa5cff5..6a49dae`: 23 files changed, `+696 / -139`). Every `file:line` citation and quote below was verified directly at `6a49dae`. **Review only:** no code, SQL, schema, content or scene files edited.

**Verdict:** **PASS WITH FIXES** — **0 blockers, 1 should-fix (S1), 2 nits (N1–N2)**.

Every build, calibration, reconstruction, core-loop, narrative and scope gate reproduces cleanly on `6a49dae`: `PlayoffManager.GetStandings` is strictly read-only over local buffers and shares selection's exact `SeasonTable` + `Standings.Order` + `ScheduleSeed` contract; the resolver guard (`6d924228…`), sanctioned Pro guard (`PA 81433 / H 18471 / ER 8803`, ledger `932ACE26…8B84`), normalized default MC log (`9c4703c8…`), and reconstructed pre-13-7 baseline (`646f3618…`) are all byte-exact; `LeagueScreen` implements D4-A and D5-A cleanly; and all comment/doc/JSON ripple edits are calibration- and content-inert. One event-ordering edge case in `RichPresenceWriter.OnAvatarChanged` (**S1**: D4b-C `NoHeirs` and parent-phase `NoWillingHeir` game-over rollovers queue `AvatarChangedEvent(avatarId, 0)` behind `SeasonRolledOverEvent` in the same pump, overwriting `#Status_BetweenCareers` with `#Status_Family`) should be reconciled before merging into `p13/integration`.

### 9.1 Preconditions & Gates Reproduced on `6a49dae`

| Check / Gate | Command / Method | Result |
| :-- | :-- | :-- |
| **§0 Preconditions & scope** | Empty-commit write check + soft reset; `git status`; `C:` free space; `git diff --stat aa5cff5..6a49dae` | Write check passed; clean tree at `6a49dae`; **`7.18 GB`** free on `C:` (`>= 2 GB`); exactly the **23** §2 allowed files touched (`PlayoffDtos.cs`, `strings.csv`, `strings.en.translation`, `rookie_season_events.json`, `GrittyEventModel.cs`, 4 `Assets/Platform/Steam/` files, `PlayoffManager.cs`, `CalendarScreen.cs`, `LeagueScreen.cs`, 3 `Tools/MonteCarloHarness/` files, 6 `docs/reference/` banners, this brief, `progress.md`); `git ls-files .env` empty |
| **Build (Debug)** | `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** (all 10 solution projects) |
| **MC default** | `dotnet run --project Tools/MonteCarloHarness -c Release -- --repo …` | **623/623 PASS** (`608` baseline + `3` `ui-strings` + `12` `ripple`). Normalized LF/`N ms` SHA-256: **`9c4703c88dbd0fae9bbaa76b4e90d1bbb879df45e8f623700bd184cd8f8d9ed4`** (exact match). Reconstructed baseline SHA-256 (deleting `--- 13-7 ripple`, the `15` `[PASS] 13-7` lines, and `  ok   ripple: 12/12`; restoring `ok   ui-strings: 6/6`, `608/608 checks passed.`, and `non-empty — 67 keys`): **`646f3618f74e18cfbca80bf7ebc826c1b77a2e0f2050ca0a16765f7d5d88481e`** (byte-exact match to post-13-6b anchor). Pro guard byte-exact: **`PA 81433 / H 18471 / ER 8803`**, ledger **`932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`**. Resolver LF SHA-256: **`6d92422863f22d73c3539306a2a2442a65bcdedf505ddc5bb24611782e9eda89`**. All `16/16` allocation probes **`0 B`**; **`0`** leaked `%TEMP%\dnd_*` files |
| **CoreLoop** | `dotnet run --project Tools/CoreLoopHarness -c Release -- --repo …` | **43/43 PASS** (`Life <-> Baseball` boundary scan clean) |
| **GrittyEvents** | `dotnet run --project Tools/GrittyEventsHarness -c Release -- --repo …` | **363/363 PASS** (`idle poll stays under 512 B/poll — 464 B/poll`) |
| **Event graph & rookie JSON** | `check_event_graph_integrity` audit + AST diff of `rookie_season_events.json` (`aa5cff5` vs `6a49dae` minus `"//"`) | **Clean:** `116` events across `10` files, `0` orphaned prerequisite flags, `0` unresolved contacts, `0` `voice_note` / `voice_profile` occurrences, `0` `scope:any` `tier < N` leaks; `rookie_season_events.json` AST minus `"//"` byte-identical (`12` events, `8,718` compressed chars) |
| **Translation binary** | Binary RSRC inspection of `Assets/Localization/strings.en.translation` (sandbox blocks Godot `--import` IPC) | Valid `RSRC` (`4,512` bytes, SHA-256 `93607e26…`); all 4 new `ui.league.standings.*` target strings (`"{0} Conference"`, `"{0} Division"`, `"#{0}"`, `"Seed {0}"`) verified present alongside Sonnet's recorded `71/71` headless parity |

### 9.2 Checklist Verification (A–E)

#### A. Simulation & Persistence (`PlayoffManager.cs`, `PlayoffDtos.cs`)
1. **Sole `Assets/Simulation` change & read-only proof:** `PlayoffManager.GetStandings` + private static `StandingsGroup` (`Assets/Simulation/Baseball/PlayoffManager.cs:1040–1099`) is the **only** diff under `Assets/Simulation/`, paired with three `readonly record struct` view DTOs in `Assets/Data/Database/PlayoffDtos.cs:96–107` (`StandingsRowView`, `StandingsGroupView`, `StandingsView`). It opens no `_db` batch, calls no `Upsert*`/`Insert*`/`Update*`/`Set*`/`Record*` query, touches no `RngState`, and mutates zero fields on `PlayoffManager`. Repo-wide `git grep GetStandings` confirms the only non-comment callers are `Assets/UI/LeagueScreen.cs:179` (`StandingsView view = gm.Playoffs.GetStandings(seasonYear, tier);`) and `Tools/MonteCarloHarness/Slice137Suites.cs:176, 197, 226, 252`.
2. **Scratch isolation & exact input parity with selection:** `GetStandings` allocates its own `var teams = new List<TeamRow>(RosterCapacity.MaxTeamsPerTier);` (`:1042`), `var completed = new List<CompletedGame>();` (`:1050`), `var table = new SeasonTable();` (`:1052`), `var seasons = new List<TeamSeasonRow>(teams.Count);` (`:1054`), and `var groups = new List<StandingsGroupView>();` (`:1058`) — never touching day-handler/postseason scratch (`_series`, `_games`, `_teamSeasons`, `_completed`, `_dueIds`, `_table`, `_teamsByTier` at `:85–91`, or `_writer` at `:81`). Inputs match selection (`RunSelection` `:234, 245`, `SelectHs` `:271–273`, `SelectPro` `:314–321`) identically: `_queries.LoadCompletedRegularSeason(seasonYear, tier, completed)` (`:1051` → `PlayoffQueries.cs:24–27, 150`: `WHERE season_year = @seasonYear AND tier = @tier AND phase = 0 AND status IN (1, 2)`), `table.Build(teamIds, completed)` (`:1053`), `ulong seed = _leagues.Get(tier).ScheduleSeed;` (`:1056`), and `Standings.Order(table, scope, seed, seasonYear, tier)` (`:1079`) over `Conferences(teams)` (`:1059`), `Divisions(teams, conference)` (`:1061`), and `TeamsIn(teams, conference, null | division)` (`:1064, 1069`). For HS (`Teams.division_id IS NULL`), `divisions.Count == 0` emits two conference groups with `DivisionId = null` (`:1062–1065`).
3. **Live W/L vs weekly `NationalRank` / selection `Seed` lag (flagged per review prompt, not a defect):**
   - W/L/RS/RA come live from `Schedule_Games` (`status IN (1, 2)`), whereas `NationalRank` and `Seed` ride along from `Team_Seasons` (`PlayoffManager.cs:1055, 1086–1093`).
   - Macro NPC games write both `Schedule_Games` and `Team_Seasons` (including College `NationalRank`) atomically in Sunday's `LeagueSimulator.FlushAccumulated` batch (`Assets/Simulation/Baseball/LeagueSimulator.cs:1692–1704`). However, an **attended** regular-season game (`MicroGame.FlushGame`, `Assets/Simulation/Baseball/MicroGame.cs:1807–1810`) or attended regular-season forfeit commits its `Schedule_Games` row immediately mid-week without recomputing `Team_Seasons`.
   - **Consequence:** Between an attended mid-week game (e.g. Wednesday/Saturday in College, Tue/Thu/Sat in Pro, Friday in HS) and Sunday's macro flush, the card shows the avatar team's and opponent's **live** W/L/RS/RA (temporarily 1–2 GP ahead of un-flushed NPC pairings, which `Standings.CompareFraction` and `LeagueScreen.StandingsAheadGamesBehindFormat` handle cleanly), while College `#N` reflects last Sunday's weekly poll (and is `null`/omitted before week 1's first Sunday flush). `Seed` is `null` until `PostseasonCalendar.SelectionDay(tier)`, where `RunSelection` runs `sim.FlushPending()`, `_writer.Recompute(...)`, and `SetSeed(...)` in one batch (`PlayoffManager.cs:232–259`), so seeds never lag W/L once set (and the College play-in winner's seed lands on `Team_Seasons` in `AdvanceCollege` at `:706`).
   - **Judgment:** **Acceptable and desirable.** Weekly College polls ("#7 nationally") are weekly by spec ([league_restructure.md](../design/league_restructure.md) §8.2, matching `GetNationalRanking` at `PlayoffManager.cs:1101`), while reflecting the player's just-completed game immediately in W-L is strictly better UX than showing stale pre-game W-L until Sunday.
4. **Threading:** `GetStandings` runs on the UI thread (`LeagueScreen._Process` → `Refresh` → `RefreshStandings`, `LeagueScreen.cs:135, 160, 179`) and shares pooled prepared commands (`_baseball.LoadTeamsByTier`, `_queries.LoadCompletedRegularSeason`, `_queries.LoadTeamSeasons`, plus `_leagues.Get(tier).ScheduleSeed` → `SeasonScheduler.ResolveSeed(_gameState)` at `LeagueSimulator.cs:320`) on the single `DatabaseManager` connection, matching `GetBracket` (`PlayoffManager.cs:990–1025`) and the pre-13-7 `LeagueScreen` queries (`LoadTeamsByTier`, `LoadTeamRecords`, `Load*Leaders`). The background Gritty poller uses its own `ReadOnlyView` connection (`GameManager.cs:809`, `DatabaseManager.cs:358–398`). For the worker-thread interactive game (`BaseballDashboard.cs:283–289`), `LeagueScreen._Process` settles `(_shownDay, _shownAvatarId)` on the day-advance frame *before* `OnPlayGameRequested` can set `InteractiveGameInFlight = true` (`BaseballDashboard.cs:272`), and `GameManager.CanAdvanceDay` (`GameManager.cs:1140–1143`) blocks day advances until `_gameTask` completes. Avoiding `_teamsByTier` (`TeamsOf`) inside `GetStandings` (`:1042`) also avoids racing `AdvanceBracket` on lazy list initialization. **No new threading risk** beyond the known, tracked R-1 `C03` single-connection command-sharing item.
5. **Reachability of `_leagues.Get(tier)` throw from `LeagueScreen`:** `LeagueDirectory.Get(tier)` (`Assets/Simulation/Baseball/LeagueDirectory.cs:29–31`) throws only for an unregistered tier. `GameManager.BootCareer` (`Assets/Core/GameManager.cs:468–489`) registers all three `LeagueTier`s (`0..TierCount - 1`) before enabling `_Process` (`:851`), and `LeagueScreen.Refresh` (`Assets/UI/LeagueScreen.cs:152–158`) guards `gm.Baseball.TryGetTeamTier(player.TeamId ?? 0, out LeagueTier tier)` (which queries `Teams LEFT JOIN Team_Tiers`, returning `false` for `teamId == 0` and `0..2` via `CHECK (tier IN (0, 1, 2))` otherwise) before calling `RefreshStandings`. **Unreachable from `LeagueScreen`.**
6. **UI allocation cost & dirty/visibility guards:** `GetStandings` allocates ~30–40 KB once per `(day, avatar)` settle point, zero per frame. `LeagueScreen`'s dirty-flag fields (`_shownDay`, `_shownAvatarId` at `LeagueScreen.cs:100–101`), `_tierKnown` parent-phase hide (`:106, 120, 152–158`), and `_Process` gate (`gm.Events.PendingCount == 0 && (day != _shownDay || !string.Equals(avatarId, _shownAvatarId, StringComparison.Ordinal))` at `:130–136`, before `RefreshDraftCard` at `:138`) are **unchanged**.

#### B. Steam Layer (`SteamRules.cs`, `RichPresenceWriter.cs`, `AchievementManager.cs`, `README.md`)
- `SteamRules.cs` (`Assets/Platform/Steam/SteamRules.cs:1–74`) imports only `DirtAndDiamonds.Data` (`:1`) — zero Facepunch, Steamworks, Godot or I/O dependencies — and pins `#Tier_HS` / `#Tier_College` / `#Tier_Pro` (`:29–43`, D2-A), `#Status_Career` / `#Status_Family` / `#Status_BetweenCareers` (`:23–27, 51–54`, D3-A), and `ACH_THE_SHOW` as the sole Pro tier achievement (`:62–73`, D1-A) over static arrays via `ReadOnlySpan<string>` (`0 B`).
- `RichPresenceWriter.PublishCareerPresence` (`Assets/Platform/Steam/RichPresenceWriter.cs:157–158`) clears `TierKey` with `string.Empty` whenever `_baseball.TryGetTeamTier(_avatarTeamId, out LeagueTier tier)` is `false`, covering both `SyncFromBoot` (`:93–103`) and `OnAvatarChanged` (`:121–126`) for live parent-phase releases (`AvatarTeamId == 0`) and reloads, while restoring `#Tier_HS` + `#Status_Career` on child takeover. *(See **Should-Fix S1** below for the `LineageIsOver()` rollover interaction on `OnAvatarChanged`.)*
- `AchievementManager.cs` deletes `AchWentPro` (`:27–35`) and loops `SteamRules.TierAchievements(tier)` (`:230–233`), keeping `ACH_THE_SHOW` firing alone on Pro.
- `Assets/Platform/Steam/README.md` (`:87–106, 135–162`) matches the code's keys, tokens and 7 code-fired + 1 stat-linked achievements verbatim; `Journeyman` reads `"Play ten seasons."` (`:144`, D6-A); `The Show` reads `"Reach the Pro league."` (`:137`, D1-A); `ACH_WENT_PRO` is marked retired (`:146–149`); no MLB/NCAA/real-league names appear anywhere in new code or copy. *(See **Nit N2** on line `:113`.)*

#### C. `LeagueScreen` (`LeagueScreen.cs`, `strings.csv`)
- Renders the avatar's conference grouped by division (`LeagueScreen.cs:173–254`): HS 1 conference × 6 rows (no division header), College 3 divisions × 4 rows with `#N` (`ui.league.standings.rank`), Pro 4 divisions × 4 rows, avatar row prefixed via `StandingsAvatarRowFormat` (`"* {0}"`, `:251–253`), GB computed per group against `rows[0]` (`:237–240`), and postseason seeds appended via `ui.league.standings.seed` (`:247–250`).
- **D5 qualifier math (`LeagueScreen.cs:92–94, 319–321`):**
  - `int innings = TierRules.RegulationInnings(tier);` (`7` HS, `9` College/Pro; `TierRules.cs:13`)
  - `int minPa = Math.Max(MinQualifier, (gamesPlayed * innings * QualifyingPaPerThousandInnings + 999) / 1000);` (`344` per `1000` innings, floor `1`)
  - `int minOuts = Math.Max(MinQualifier, (gamesPlayed * innings + OutsPerInning - 1) / OutsPerInning);` (`OutsPerInning = 3`, floor `1`)
  - Hand-verified: `G = 0` → `1 PA / 1 out`; `G = 6` HS (`inn = 7`) → `(14448 + 999) / 1000 = 15 PA`, `44 / 3 = 14 outs`; `G = 18` College (`inn = 9`) → `(55728 + 999) / 1000 = 56 PA`, `164 / 3 = 54 outs`; `G = 24` Pro (`inn = 9`) → `(74304 + 999) / 1000 = 75 PA`, `218 / 3 = 72 outs`.
- All 4 new strings (`strings.csv:69–72`) are `ui.league.standings.*` keys with zero pronouns. `LeagueScreen.tscn` is untouched; zero per-scene theme overrides and zero new interactive controls added.

#### D. Inertness (`GrittyEventModel.cs`, `CalendarScreen.cs`, `rookie_season_events.json`, SQL/Schema, F9 Banners)
- `Assets/Narrative/Events/GrittyEventModel.cs:94–99` and `Assets/UI/CalendarScreen.cs:196–200` are strictly comment-only edits.
- `Assets/Narrative/Events/Content/rookie_season_events.json:2` changes only the top-level `"//"` string; parsed JSON minus `"//"` is byte-for-byte identical to `aa5cff5`.
- Zero schema, SQL, query or bus-event changes; `BaseballQueries.SqlSelectTeamRecordsByTier` (`:351–356`) and `LoadTeamRecords` (`:1366–1383`) remain intact and unused per §2.
- All six `docs/reference/` line-5 banners (`gritty_event_framework.md:5`, `hs_onboarding_events.md:5`, `criminal_underworld_content_arc.md:5`, `criminal_underworld_dirt2_the_fix.md:5`, `criminal_underworld_dirt3_product_and_turf.md:5`, `criminal_underworld_dirt4_the_bust_and_the_door_out.md:5`) accurately record v14's landed ordinals while preserving gate history.

#### E. Tests (`Tools/MonteCarloHarness/Slice137Suites.cs`, `Program.cs`)
- Audited all **12** `ripple` checks (`Slice137Suites.cs:39, 48, 64, 84, 89, 182, 188, 212, 217, 239, 244, 255`) and all **3** `ui.league.*` checks (`Program.cs:5649–5655`). **None is vacuous:** every `GetStandings` check asserts concrete non-empty group/row counts, seed/rank invariants, or `2 * games` conservation (`midGamesInView == 40`, `hsViewGames == 120`, `proViewGames == 2048`, `collegeViewGames == 672`) that fail on empty, ungrouped, unsorted or `Team_Seasons`-divergent outputs, and the `ui-strings` checks require `Count > 0`. *(See **Nit N1** for two minor test-strength notes.)*

---

### 9.3 Findings (0 Blockers, 1 Should-Fix, 2 Nits)

- **[Should-Fix S1] `RichPresenceWriter.OnAvatarChanged` omits `LineageIsOver()`, so D4b-C `NoHeirs` and parent-phase `NoWillingHeir` game-over rollovers overwrite `#Status_BetweenCareers` with `#Status_Family` in the same bus pump**
  - **Verified citations at `6a49dae`:**
    - `Assets/Platform/Steam/RichPresenceWriter.cs:121–126`:
      ```csharp
      private void OnAvatarChanged(AvatarChangedEvent e)
      {
          _avatarId = e.AvatarPlayerId;
          _avatarTeamId = e.TeamId;
          PublishCareerPresence();
      }
      ```
    - `Assets/Platform/Steam/RichPresenceWriter.cs:167–169` (compare `SyncFromBoot` at `:95–99` and `OnSeasonRolledOver` at `:138–142`):
      ```csharp
      // Only reached with a live bloodline (the callers route a lineage-over
      // state to SetBetweenCareers), so the status is career or family.
      _steam.SetRichPresence(SteamDisplayKey, SteamRules.StatusToken(hasTeam, lineageOver: false));
      ```
    - `Assets/Simulation/Baseball/PipelineManager.cs:1781–1785, 1807–1811, 1925–1936, 1986–1989`:
      ```csharp
      if (Career is { HasAvatar: true } && anyMove)
      {
          Career.ReactivateAvatar();
      }
      ```
    - `Assets/Simulation/Baseball/CareerManager.cs:853, 1259–1268`:
      ```csharp
      if (!_inParentPhase && !IsLineageOver)
      ...
      ActivateUnrostered(avatarId); // line 853: _bus?.Publish(new AvatarChangedEvent(avatarId, 0));
      ```
  - **Defect:** When `PipelineManager.RunRollover` ends a bloodline — either releasing a childless avatar (`NoHeirs`, `PipelineManager.cs:1781–1785, 1927`) or ending the parent phase when all children are `>= 16` and unwilling (`NoWillingHeir`, `:1807–1811, 1935`) — `lineage_over_reason` commits in the rollover batch, and `:1988` then calls `Career.ReactivateAvatar()` (`anyMove` is `true` on every normal rollover). `CareerManager.ReactivateAvatar()` (`:1259–1268`) explicitly permits `IsLineageOver` and calls `ActivateUnrostered(avatarId)`, queuing `AvatarChangedEvent(avatarId, 0)` into `EventBus` **behind** `SeasonRolledOverEvent`. Within that same `EventBus.DispatchPending()` pump:
    1. `RichPresenceWriter.OnSeasonRolledOver` (`:138–142`) sees `LineageIsOver() == true` and calls `SetBetweenCareers()`, publishing `#Status_BetweenCareers` and clearing `_avatarId = null`.
    2. Immediately afterward, the queued `AvatarChangedEvent(avatarId, 0)` dispatches to `OnAvatarChanged` (`:121–126`), which lacks the `if (LineageIsOver())` guard, restores `_avatarId = avatarId` / `_avatarTeamId = 0`, and calls `PublishCareerPresence()` (`lineageOver: false`), **clobbering `#Status_BetweenCareers` with `#Status_Family` (*"raising the next generation"*) on a dead lineage**. Because the clock stops on game-over, `#Status_Family` stays latched for the rest of the session (contradicting D3-A, `SteamRules.cs:46–48`, and `README.md:119–120`).
  - **Fix (within §2 allowed paths):** Add `if (LineageIsOver()) { SetBetweenCareers(); return; }` at the top of `RichPresenceWriter.OnAvatarChanged` (`RichPresenceWriter.cs:121`, matching `SyncFromBoot` at `:95` and `OnSeasonRolledOver` at `:138`; optionally also clear `TierKey` to `string.Empty` in `SetBetweenCareers()` at `:110–114`), so all three entry points uphold `:167`'s live-bloodline invariant, and extend the `RichPresenceWriter.cs` check in `Slice137Suites.cs:89–95` to verify it.

- **[Nit N1] `Slice137Suites.cs` exercises `GetStandings` on tie-free HS `HashPower` records, and `GroupsOrderedByWinPct` evaluates `0 >= 0` if a `0-0` row ever appears**
  - **Verified citations at `6a49dae`:**
    - `Tools/MonteCarloHarness/Slice137Suites.cs:119`: `bool homeWins = HashPower(row.HomeTeamId) > HashPower(row.AwayTeamId);`
    - `Tools/MonteCarloHarness/Slice137Suites.cs:138–140`:
      ```csharp
      private static bool GroupsOrderedByWinPct(StandingsView view) =>
          view.Groups.All(g => g.Rows.Zip(g.Rows.Skip(1)).All(p =>
              (long)p.First.Wins * (p.Second.Wins + p.Second.Losses) >= (long)p.Second.Wins * (p.First.Wins + p.First.Losses)));
      ```
    - `Tools/MonteCarloHarness/Slice137Suites.cs:179`: `bool hsShape = hsView.Groups.Count == 2 && hsView.Groups.All(g => g.DivisionId is null && g.Rows.Count == 6);`
  - **Detail:** (a) Strict `HashPower` dominance makes a 10-game HS double round-robin finish `10-0, 8-2, 6-4, 4-6, 2-8, 0-10` with no win% ties, so tiebreak agreement relies on `Standings.Order`'s direct unit suite (`Slice134Suites.cs:359–405`) rather than a tied group in `Slice137Suites`; `hsShape` (`:179`) could also include `.Select(g => g.ConferenceId).Distinct().Count() == 2` like Pro (`:201`) and College (`:230`). (b) In `GroupsOrderedByWinPct`, a `0-0` team zeroes both sides (`0 >= 0` even for `0-2` ahead of `0-0`, whereas `SeasonTable.CompareFraction` at `Standings.cs:109–120` reads `0-0` as `1/2`); harmless here since `limit: 20` gives every HS team `3–4` games, or replaceable with `SeasonTable.CompareFraction(...) >= 0`.

- **[Nit N2] Pre-existing stale class name in `Assets/Platform/Steam/README.md:113` (`BaseballDashboard` → `PlayerScreen`)**
  - **Verified citations at `6a49dae`:**
    - `Assets/Platform/Steam/README.md:113`: `(The tier display names mirror BaseballDashboard's exported TierNamesCsv defaults`
    - `Assets/UI/PlayerScreen.cs:33`: `public string TierNamesCsv { get; set; } = "High School,College,Pro";`
  - **Detail:** `TierNamesCsv` moved from `BaseballDashboard` to `PlayerScreen` in the earlier phone UI reorg (`BaseballDashboard.cs:28–34`). Optional one-word doc tidy while `README.md` is open.


---

## 10. Reconciliation — Claude Opus 5.5

**Reconciler:** Claude Opus 5.5 (integration lead), **2026-11-26**, on `p13/13-7-reconcile-opus` (worktree `C:\Users\DELL\dnd-opus-137-reconcile`) from `02fe1fb` (= `6a49dae` + Carbon's §9). **Route: TEAM.md §1a Option B.** Opus authored the S1 code fix, so Opus can't approve it. The delta touches only `Assets/Platform/Steam/` and `Tools/MonteCarloHarness/`, with **zero** `Assets/Simulation/`, schema, persistence or `EffectiveRatings` paths, so **Gemini 4 Neon** reviews it (§11) before the merge.

### 10.1 Citation check (all verified at `6a49dae`)

| Finding | Carbon's citations | Verdict |
| :-- | :-- | :-- |
| **S1** | `RichPresenceWriter.cs:121–126` (no guard) vs `:95–99`, `:138–142`; `:167–169` (the "only reached with a live bloodline" invariant); `PipelineManager.cs:1781–1785` (NoHeirs), `:1807–1811` (takeover GameOver), `:1925–1936` (in-batch writes), `:1986–1989` (`ReactivateAvatar`); `CareerManager.cs:853`, `:1259–1268` | **Confirmed.** The chain also holds at the bus: `PipelineManager.OnSeasonRolledOver` (`:319`) is a `SeasonRolledOverEvent` handler; `WriteReleaseStateInBatch` (`CareerManager.cs:1369`) writes `lineage_over_reason` for NoHeirs, `WriteGameOverInBatch` (`:1390`) for the others; `EventBus.Publish` only enqueues and `DispatchPending` drains events published during the drain (`EventBus.cs:65–85`); `RichPresenceWriter` subscribes after the pipeline (`GameManager.cs:829` vs `Pipeline.AttachTo` at `:610`). So the queued `AvatarChangedEvent(id, 0)` reaches `OnAvatarChanged` after `OnSeasonRolledOver` has set `#Status_BetweenCareers`. `lineage_over_reason` is never cleared (writers `CareerManager.cs:1307, 1369, 1390` only), so the guard can't hide a live career. `AchievementManager.OnAvatarChanged` is unaffected (team 0 makes `EvaluateTier` a no-op) |
| **N1** | `Slice137Suites.cs:119`, `:138–140`, `:179`; `Standings.cs:109–120` | **Confirmed** (harmless today, as Carbon says) |
| **N2** | `README.md:113`; `PlayerScreen.cs:33` | **Confirmed** |

### 10.2 Fixes (`p13/13-7-reconcile-opus`)

- **S1 (fixed):** `RichPresenceWriter.OnAvatarChanged` opens with `if (LineageIsOver()) { SetBetweenCareers(); return; }`, the same guard as `SyncFromBoot` and `OnSeasonRolledOver`, so all three entry points uphold `PublishCareerPresence`'s live-bloodline invariant. **Optional part taken:** `SetBetweenCareers` also clears `TierKey` (`string.Empty`), so a career's last tier never stays latched under `#Status_BetweenCareers`. The doc comment records the same-pump ordering.
- **S1 pin (harness):** the existing `13-7 source scan` check (same check, renamed, so the `ripple` count stays 12) now slices each method body (`MethodBody`) and requires, in `SyncFromBoot`, `OnAvatarChanged` and `OnSeasonRolledOver`, `if (LineageIsOver())` → `SetBetweenCareers();` → `PublishCareerPresence();` in that order (`GuardsLineageOver`), plus `SetRichPresence(TierKey, string.Empty)` inside `SetBetweenCareers`. A file-wide `Contains` would pass with only one method guarded. **Mutation-tested:** with `6a49dae`'s `RichPresenceWriter.cs` the check fails (`ripple` 11/12); with only the `OnAvatarChanged` guard removed it fails with `unguarded: private void OnAvatarChanged(`; with the fix it passes `3/3 guarded`.
- **N1 (taken):** `GroupsOrderedByWinPct` compares with `SeasonTable.CompareFraction` (0-0 reads ½, exactly as `Standings.Order`); `hsShape` also requires two distinct conference ids. **Not taken:** a tied-record fixture. `Standings.Order`'s tiebreaks are pinned by its own suite (`Slice134Suites.cs:359–405`), and `GetStandings` calls it unchanged.
- **N2 (taken):** `README.md:113` `BaseballDashboard` → `PlayerScreen`. The README's `tier` key row and publish-cadence paragraph now also say that the between-careers status clears `tier` and survives the game-over rollover's re-activation.

No DDL, SQL, schema, event, sim or content change; every path is inside §2.

### 10.3 Gates on the reconciled tree

| Gate | Result |
| :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug`: **0 warnings / 0 errors** |
| MC default | **623/623 PASS** (`ripple` 12/12, `ui-strings` 9/9). Normalized SHA-256 (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`): **`c9f2caf8a972c0e64288c2fec9a1a28f0d6937cc3fba6eacab6a5cbf45c92aa2`**. It differs from Carbon's `9c4703c8…` only by the renamed source-scan check line. Carbon's §9.1 recipe (drop `--- 13-7 ripple`, the 15 `[PASS] 13-7` lines and `ok   ripple: 12/12`; restore `ok   ui-strings: 6/6`, `608/608 checks passed.`, `non-empty — 67 keys`) reproduces **`646f3618f74e18cfbca80bf7ebc826c1b77a2e0f2050ca0a16765f7d5d88481e`** byte-exact. Pro guard **PA 81433 / H 18471 / ER 8803**, ledger **`932ACE26…8B84`** |
| CoreLoop | **43/43** (Life ↔ Baseball boundary scan clean) |
| Gritty | **363/363** (idle poll 464 B/poll) |
| Not rerun (no inputs changed) | event graph (no content), Godot `--import`/parity (no `strings.csv`, scene or translation change), live boot (presence no-ops without a Steam client; the behaviour is pinned by the MC source scan, as in §5) |

### 10.4 Next

**Gemini 4 Neon** delta-reviews `02fe1fb..<this commit>` on `p13/13-7-delta-review-neon` (`.worktrees/13-7-delta-review-neon`, pre-created by Opus) and records §11. On a clean PASS, Opus fast-forwards this branch to Neon's §11 commit, ticks §6 boxes 3–4 and merges `--no-ff` into `p13/integration`. `main` waits for the owner's go-ahead.

---

## 11. Delta review — Gemini 4 Neon

**Reviewer:** Gemini 4 Neon (mid-tier engineer and UI/content reviewer; [TEAM.md](../TEAM.md) §1/§1a Option B), **2026-11-27**.  
**Branch / worktree:** `p13/13-7-delta-review-neon` in `.worktrees/13-7-delta-review-neon`, reviewing `02fe1fb..b0060c9` (5 files, `+110 / -9`: `Assets/Platform/Steam/README.md`, `Assets/Platform/Steam/RichPresenceWriter.cs`, `Tools/MonteCarloHarness/Slice137Suites.cs`, `docs/progress.md`, `docs/tasks/13-7_ripple_brief.md`; zero `Assets/Simulation/`, schema, persistence or `EffectiveRatings` paths). Every `file:line` citation below was verified directly at `b0060c9`. **Review only:** no code, harness, README, SQL or content edits retained.

**Verdict:** **PASS** — **0 blockers, 0 should-fixes, 0 nits.**

### 11.1 Preconditions & Delta Verification (`02fe1fb..b0060c9`)

- **Preconditions:** Worktree write check (`git commit --allow-empty` + `git reset --soft HEAD~1`) passed; tree clean at `b0060c9` (parent `02fe1fb`); **`4.54 GB`** free on `C:` (`>= 2 GB`); `git diff --stat 02fe1fb..b0060c9` touches only the 5 expected files; `git ls-files .env` empty.
- **1. S1 fix (`Assets/Platform/Steam/RichPresenceWriter.cs`):**
  - `OnAvatarChanged` (`RichPresenceWriter.cs:129–139`) opens with `if (LineageIsOver()) { SetBetweenCareers(); return; }` (`:131–135`), matching `SyncFromBoot` (`:93–103`, guard `:95–99`) and `OnSeasonRolledOver` (`:141–157`, guard `:151–155`) and upholding `PublishCareerPresence`'s `:180–182` live-bloodline invariant across all three entry points.
  - Bug chain confirmed at `b0060c9`: `PipelineManager.OnSeasonRolledOver` (`Assets/Simulation/Baseball/PipelineManager.cs:319`) → `RunRollover` (`NoHeirs` at `:1781–1785, 1925–1928` → `CareerManager.WriteReleaseStateInBatch` at `Assets/Simulation/Baseball/CareerManager.cs:1369`; parent-phase `GameOver` at `PipelineManager.cs:1807–1811, 1933–1936` → `CareerManager.WriteGameOverInBatch` at `CareerManager.cs:1390`) → `Career.ReactivateAvatar()` (`PipelineManager.cs:1986–1989`) → `CareerManager.ReactivateAvatar` (`CareerManager.cs:1259–1268`, permits `IsLineageOver`) → `ActivateUnrostered(avatarId)` (`CareerManager.cs:838–854`, publishing `AvatarChangedEvent(avatarId, 0)` at `:853`). `EventBus.Publish` (`Assets/Core/EventBus.cs:64–78`) only enqueues, `DispatchPending` (`EventBus.cs:80–105`) drains handler-published events in the same pump, and `Pipeline.AttachTo(Events)` (`Assets/Core/GameManager.cs:610`) subscribes before `_presence.AttachTo(Events)` (`GameManager.cs:829`).
  - `lineage_over_reason` (`Assets/Data/Database/GameStateQueries.cs:34`) is never cleared: repo-wide grep confirms the only writers are `CareerManager.cs:1307, 1369, 1390`, so the guard can never hide a live career.
- **2. `SetBetweenCareers` clearing `TierKey` (`RichPresenceWriter.cs:110–117`):**
  - `_steam.SetRichPresence(TierKey, string.Empty)` (`:115`) runs before `SteamDisplayKey` (`:116`). On an avatar-less boot (`GameManager.cs:835–838`), `SteamIntegration.SetRichPresence` (`Assets/Platform/Steam/SteamIntegration.cs:121–128`) no-ops without Steam and idempotently deletes the `"tier"` key when Steam is up. Harmless, and consistent with D3-A and `Assets/Platform/Steam/README.md:91, 118–120`.
- **3. Harness (`Tools/MonteCarloHarness/Slice137Suites.cs`):**
  - `MethodBody` (`Slice137Suites.cs:111–128`) and `GuardsLineageOver` (`:131–137`) scope the scan to each method body (`SyncFromBoot`, `OnAvatarChanged`, `OnSeasonRolledOver` at `:93–94`, plus `SetBetweenCareers` at `:95–96`) and require `if (LineageIsOver())` → `SetBetweenCareers();` → `PublishCareerPresence();` in order.
  - Robust to CRLF checkouts (`\r\n    public ` matches `\n    public `, confirmed live on this CRLF checkout of `RichPresenceWriter.cs`), and missing signatures return `string.Empty`, failing both `GuardsLineageOver` and `betweenClearsTier`.
  - Check name (`Slice137Suites.cs:97`) stays unique, prefixed `"13-7"`, and keeps `ripple` at 12 checks (`:39, 48, 64, 84, 97, 230, 236, 260, 265, 287, 292, 303`; `ui-strings` 13-7 checks remain 3 at `Program.cs:5649, 5652, 5655`).
  - **Mutation test repeated:** temporarily deleting the `if (LineageIsOver())` block from `OnAvatarChanged` and running `--only ripple` returned exit 1 (`RED ripple: 11/12`, `unguarded: private void OnAvatarChanged(`); `RichPresenceWriter.cs` was restored byte-exact (`git diff Assets/` empty).
- **4. N1 (`Slice137Suites.cs`):**
  - `GroupsOrderedByWinPct` (`:184–187`) delegates to `SeasonTable.CompareFraction` (`Assets/Simulation/Baseball/Standings.cs:109–120`), reading `0-0` as ½ just like `Standings.Order` (`Standings.cs:155`). `hsShape` (`:226–227`) requires two distinct `ConferenceId` values, matching `proShape` (`:248–249`) and `collegeShape` (`:277–278`).
  - Skipping a tied-record fixture (§10.2) is acceptable: `PlayoffManager.GetStandings` calls `Standings.Order` unchanged, and `Standings.Order`'s tiebreak ladder is already pinned by `Tools/MonteCarloHarness/Slice134Suites.cs:359–409`.
- **5. N2 & docs (`Assets/Platform/Steam/README.md`):**
  - `README.md:113` names `PlayerScreen` (verified at `Assets/UI/PlayerScreen.cs:33`).
  - `README.md:91` (`tier` row) and `:116–125` (publish-cadence paragraph) match `RichPresenceWriter.cs:93–183` and `GameManager.cs:830–838` verbatim.
  - Zero MLB/NCAA/real-league names in the delta; zero player-facing strings added, so no new strings-table keys are needed.

### 11.2 Gates Reproduced on `b0060c9`

| Gate | Command | Result |
| :-- | :-- | :-- |
| **Build (Debug)** | `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** (10/10 projects) |
| **MC default (solo)** | `dotnet run --project Tools/MonteCarloHarness -c Release` | **623/623 PASS** (`ripple` 12/12, `ui-strings` 9/9). Normalized LF/`N ms` SHA-256: **`c9f2caf8a972c0e64288c2fec9a1a28f0d6937cc3fba6eacab6a5cbf45c92aa2`** (exact match). §9.1 reconstruction recipe SHA-256: **`646f3618f74e18cfbca80bf7ebc826c1b77a2e0f2050ca0a16765f7d5d88481e`** (exact match). Pro guard: **PA 81433 / H 18471 / ER 8803**, ledger **`932ACE26E257707A76256F8C06EE9EAA2E8FF52A014DF64654A84DEBF0718B84`** |
| **CoreLoop** | `dotnet run --project Tools/CoreLoopHarness -c Release` | **43/43 PASS** (Life ↔ Baseball boundary scan clean) |
| **GrittyEvents** | `dotnet run --project Tools/GrittyEventsHarness -c Release` | **363/363 PASS** (`idle poll stays under 512 B/poll — 464 B/poll`) |
| **Mutation test** | `--only ripple` without `OnAvatarChanged` guard | **11/12 RED** (`unguarded: private void OnAvatarChanged(`); restored byte-exact |

### 11.3 Findings

None (**0 blockers, 0 should-fixes, 0 nits**). Ready for **Claude Opus 5.5** to fast-forward `p13/13-7-reconcile-opus` to this commit, tick §6 boxes 3–4, and merge `13-7` `--no-ff` into `origin/p13/integration`.
