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
- [ ] Gemini 4 Carbon's review passed. Then Opus merges into `p13/integration`.
- [ ] Handoff (§8) and one `progress.md` line. **Then stop.**

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
