# Task Brief: `13-6b` — the pipeline's phone decision cards and the read-only Draft board

**Author:** Claude Opus 5.5 (integration lead), 2026-11-26. **Recommended model:** Claude Sonnet 5.5 (UI and feature engineer). **Reviewer:** Gemini 4 Neon. The diff has no `Assets/Simulation/`, schema, persistence or `EffectiveRatings` paths (TEAM.md §1a). **Base:** `p13/integration` after the 13-6a merge. **Slicing:** 13-6 brief D8-A (owner, 2026-11-24): 13-6a built the engine, 13-6b builds the UI on `PipelineStatusView`, and 15c restyles it later.

## 0. Preconditions (check first; stop and report if any fails)

1. Your own worktree on `p13/13-6b-sonnet`, branched from `origin/p13/integration` at the post-13-6a head. Clean tree; the write check passes (`git commit --allow-empty -m "write check"`, then `git reset --soft HEAD~1`); at least 2 GB free on `C:`.
2. Baseline on that head before any edit: build 0/0; MC default **605/605**, with the normalized log SHA-256 recorded (the post-13-6a D7 anchor in `progress.md`); CoreLoop 43/43; Gritty 363/363.
3. Run `godot_scene_mapper` (or the Godot MCP) on `Assets/UI/BurnerPhone.tscn`, `Assets/UI/LeagueScreen.tscn` and `Assets/UI/Main.tscn` before writing any `GetNode<T>()` (ui_conventions.md).

## 1. Context

13-6a gave the pipeline a UI-facing API. Everything the UI needs already exists; 13-6b only reads it and sends intent.

| What | API (`GameManager.Pipeline`) | Notes |
| :-- | :-- | :-- |
| The recruiting choice (days 274–317, a just-finished HS senior) | `TryGetPendingRecruitingChoice(out PendingRecruitingChoice)` → `ResolveRecruiting(int teamId)` | `PendingRecruitingChoice(SeasonYear, Offers, IsWalkOn)`, with 1–5 `RecruitingOfferRow`s, or one walk-on. An unanswered choice resolves on Signing Day (day 318) to the best-ranked offer |
| The declaration (days 176–182, a College class 2–3 avatar) | `TryGetPendingDeclaration(out PendingDeclaration)` → `ResolveDeclaration(bool declare)` | `PendingDeclaration(SeasonYear, ClassYear, ProjectedSlot, ProjectedRound)`; round 0 = projected undrafted. A class-4 senior is automatically in the pool (no choice). Unanswered, it resolves at the deadline (day 183) by the NPC rule |
| The Draft board and class | `GetStatusView()` → `PipelineStatusView` | The avatar's class year, projected slot and round, his offers, `DraftBoard` (class ≥ 2, best first), this season's `DraftPicks` (after day 195), and free-agent counts by role. **It builds a database snapshot (tens of ms): call it when the screen opens or the day changes, never per frame** |

- **The pending getters are cached per day** and invalidated on every answer, so the phone can poll them each frame (13-6a §11.3 #17). A second `Resolve…` throws `InvalidOperationException`: the UI must not offer it.
- **The clock never stops** for a pipeline decision (D5-A). The card names its deadline; the autopilot answers if the player doesn't.
- **The texts already exist.** `PipelineBeatWriter` writes the offers, Signing Day, declaration, Draft Night and release texts into the phone's message log (`pipeline.*` keys). 13-6b adds the decision **cards**, not texts.
- **Precedent:** `SuccessionScreen` (pending choice → `CareerManager.ResolvePendingSuccession`) and the Burner Phone's pending gritty-event card (reply chips, the prominence pass) are the patterns to follow.
- **Parent phase (D4b-C):** a released avatar is unrostered (`CareerManager.IsAvatarRostered == false`, `AvatarTeamId == 0`). 13-6a checked the screens statically; 13-6b runs the live check (§4 item 4).

## 2. Hard constraints

- **Allowed paths:** `Assets/UI/` (the phone's cards, the Draft board, their `.tscn` and scripts), `Assets/UI/Theme/` only if a style token is missing (no per-scene overrides), and `Assets/Localization/strings.csv` (new `ui.pipeline.*` keys; re-import `strings.en.translation` per the Godot headless note, keeping only the translation). Plus `Tools/MonteCarloHarness/` only if `ui-strings` needs its key list extended, docs (this brief, `progress.md`), and a UI skill note if needed. **Everything else is read-only:** `Assets/Simulation/`, `Assets/Data/`, `Assets/Core/` and `Assets/Narrative/`.
- **If the UI needs a read that isn't exposed** (for example a program's display name or rank on an offer row), **stop and report it to Opus.** Don't add it to `PipelineManager` or the queries yourself; that path needs Carbon's review.
- **Presentation never drives the sim** (GEMINI.md §1): the UI reads DTOs and calls only `ResolveRecruiting` / `ResolveDeclaration`. It never writes the database and never uses the sim's `RngState`.
- **Styling** flows from the Theme family (`Assets/UI/Theme/`), and every interactive control gets focus navigation (GEMINI.md §7). This is the shipped dark-mode look; 15c restyles it.
- **Text:** every string is a strings-table key. No avatar pronouns (the avatar can be male or female); player names appear in text only.

## 3. Inputs

The 13-6 brief §3 D5-A and D8-A; `PipelineManager.cs` (the public API above); `PipelineDtos.cs` (`PipelineStatusView`, `DraftBoardEntry`, `RecruitingOfferRow`, `DraftPickRow`); `BurnerPhone.cs` / `.tscn`; `LeagueScreen.cs` / `.tscn`; `SuccessionScreen.cs`; `.agents/rules/ui_conventions.md`; [league_restructure.md](../design/league_restructure.md) §9.

## 4. Outputs

1. **The recruiting card (Burner Phone).** While a recruiting choice is pending, a card lists each offer (program and national rank), or the walk-on, with one button per program. Pressing one calls `ResolveRecruiting(teamId)`, and the card collapses into an "answered" state. It names the deadline (Signing Day, the second Wednesday of November) and says an unanswered choice goes to the best-ranked offer.
2. **The declaration card (Burner Phone).** While a declaration is pending, a card shows the projected round ("projected round N", or "projected undrafted") and two buttons: **Declare** (`ResolveDeclaration(true)`) and **Stay in school** (`ResolveDeclaration(false)`). It names the deadline (day 183, the first Monday of July).
3. **The read-only Draft board.** Recommended home: a section of the League phone tab. Before day 195 it shows the top of `DraftBoard` (rank, name, role, College team, class year), with the avatar's row highlighted and his projected round. From day 195 it shows the season's `DraftPicks` by round (pick, Pro team, player), with the avatar's pick highlighted. It's view-only, calls `GetStatusView()` on open and on day change, and lists nothing for a player who isn't in College (an empty state).
4. **The live parent-phase smoke check** (13-6a §11.6 #6, Carbon §12.4): in Godot, with an avatar in the D4b-C parent phase (unrostered), the dashboard, phone, calendar, League and Player screens open and the week advances without errors. Record what you saw. If something breaks outside `Assets/UI/`, report it rather than patching it.
5. **Strings:** `ui.pipeline.*` keys for every card and board label; the translation re-imported.

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings / 0 errors |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | 605/605 (plus any `ui-strings` checks you add). Outside `ui-strings`, the normalized log is byte-identical to the §0 baseline: the sim didn't move |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | 43/43 (Life ↔ Baseball boundary scan clean) |
| Gritty | `dotnet run --project Tools/GrittyEventsHarness -c Release` | 363/363 |
| Godot | headless import plus a scene load of the changed `.tscn`s; then the §4 item 4 run | No errors; the cards appear on their days and resolve once; the board renders |

**Manual check (record it in the handoff):** from a save on day 175, College class 2, advance into the window. The declaration card appears; answering it removes the card, and the choice survives a reload. Then, from an HS senior save on day 273, advance to day 274: the recruiting card appears, and pressing a program resolves it once.

## 6. Definition of done

- [x] §0 recorded; §4 items 1–5 built; every string keyed; focus navigation on every control.
- [x] §5 gates green; the manual check and the parent-phase smoke check recorded.
- [x] Gemini 4 Neon's review passed (§9 PASS WITH FIXES; §10 reconciled; §11 PASS). Merged into p13/integration.
- [x] Handoff (§8) and one `progress.md` line. **Then stop.**

## 7. Out of scope

Any engine change (the pipeline, the queries, the events, the texts); Draft Night and Signing Day cutscenes (16b); the 15c restyle; the parent-phase jobs, the training mini-game and their UI (the parent-phase brief); the `RichPresenceWriter` tier latch (Carbon §12.3, the parent-phase / 13-7 pass).

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built> · Commit: <sha> · Gates: build, MC (total, byte-identical outside ui-strings), CoreLoop, Gritty, Godot (import, scenes, manual + parent-phase checks)
Open: <unfinished items, risks, owner questions>
Recommended next: 13-6b review by Gemini 4 Neon (then Opus merges into p13/integration)
```

Then write the complete prompt for the next model (TEAM.md §3 step 8) and add one compact line to [progress.md](../progress.md). Don't start the next slice.

---

## 9. Independent review — Gemini 4 Neon, 2026-11-26

**Reviewer:** Gemini 4 Neon (mid-tier engineer and UI/content reviewer, [TEAM.md](../TEAM.md) §1/§1a). **Branch / worktree:** `p13/13-6b-review-neon` in `.worktrees/13-6b-review-neon` at `2594700`, reviewing exact diff `90356ab..2594700` (`p13/13-6b-sonnet`, authored by Claude Sonnet 5.5). Docs only; no `Assets/` code edited.

**Verdict: PASS WITH FIXES** — **0 blockers, 1 should-fix (S1), 3 nits (N1–N3).**

### 9.1 Preconditions & scope (§0)

- **Environment:** `git fetch` clean; HEAD `2594700`; clean tree; git write check (`git commit --allow-empty` + `git reset --soft HEAD~1`) passed; `C:` free space **2.76 GB** (≥ 2 GB).
- **Diff scope (`git diff --stat 90356ab..2594700`):** 11 files, `779 insertions(+), 2 deletions(-)`, matching §2 exactly: `Assets/Localization/strings.csv`, `Assets/Localization/strings.en.translation`, `Assets/UI/BurnerPhone.Pipeline.cs`, `Assets/UI/BurnerPhone.cs`, `Assets/UI/BurnerPhone.tscn`, `Assets/UI/LeagueScreen.Draft.cs`, `Assets/UI/LeagueScreen.cs`, `Assets/UI/LeagueScreen.tscn`, `Assets/UI/Theme/DirtAndDiamonds.tres`, `Tools/MonteCarloHarness/Program.cs`, `docs/progress.md`. **Zero** paths under `Assets/Simulation/`, `Assets/Data/`, `Assets/Core/`, or `Assets/Narrative/`, and zero schema/persistence/`EffectiveRatings` changes (Neon review jurisdiction confirmed per TEAM §1a).

### 9.2 Reproduced gates (§5)

| Gate | Command | Result |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** (10 projects) |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | **608/608** (`605 + 3` `ui-strings` checks). Normalized log (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`) SHA-256: `646f3618f74e18cfbca80bf7ebc826c1b77a2e0f2050ca0a16765f7d5d88481e`. Reverting only the 3 new `13-6b:` PASS lines and the 3 `ui-strings`/total count lines (`67→23 keys`, `608→605`, `6/6→3/3`) reconstructs the exact `90356ab` baseline SHA-256 `c34b1e3f01a35ca76a5216f42592cb8ad6f9b11091c961beaef16555656ab968` (**byte-identical** outside `ui-strings`) |
| `ui-strings` negative controls | `dotnet MonteCarloHarness.dll --only ui-strings --repo <scratch>` | Control: **6/6 PASS** (exit 0). Missing key (`ui.pipeline.banner.recruit` removed): **5/6 FAIL** on check #1 (exit 1). Unused key (`ui.pipeline.unused.probe` added): **5/6 FAIL** on check #2 (exit 1). Avatar pronoun (`"his"` in `ui.pipeline.declare.body`): **5/6 FAIL** on check #3 (exit 1) |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | **43/43** (Life ↔ Baseball boundary scan clean) |
| GrittyEvents | `dotnet run --project Tools/GrittyEventsHarness -c Release` | **363/363** |
| Godot import & translation | `Godot_v4.7-stable_mono_win64_console.exe --headless --path . --import` (churn reverted) | Exit 0. `strings.en.translation` SHA-256 unchanged pre/post import (`E66DB1E7D79CB9B8D5BCDD1C9CF41423ACE4269039149DFFDBC56A0977513CA3`). Headless GDScript verification against scratch `APPDATA`: **67/67** `strings.csv` rows match `strings.en.translation` (`0` mismatches); `DirtAndDiamonds.tres` loads cleanly with `HighlightLabel` present; `BurnerPhone.tscn`, `LeagueScreen.tscn`, and `Main.tscn` load/instantiate with 0 errors; all 11 `GetNode` paths resolve live (`LIVE_NODE_PATHS_MISSING=0`) |
| Live career driver check | Optional multi-year `Main.tscn` career walkthrough (days 175/273/parent-phase) | **Not run** by reviewer (only headless scene/translation/boot check above was run; full live career playback relies on the author's recorded run in `docs/progress.md`) |

### 9.3 Findings

#### Blockers
None.

#### Should-fix
- **S1 — `Assets/UI/LeagueScreen.cs:83–117` (and `Assets/UI/LeagueScreen.Draft.cs:45, 57–71`): same-day interactive takeover from the parent phase leaves `LeagueScreen` hidden for the heir's entire first day.**
  - **Scenario:** In the live game (`Career.AutopilotSuccession = false`, `Assets/Core/GameManager.cs:562`), an unrostered parent-phase avatar reaches day 1 of a new season (e.g. day 731) when a willing child turns 16. `PipelineManager.RunRollover` (`Assets/Simulation/Baseball/PipelineManager.cs:1990–1993`) calls `Career.ParkTakeoverChoice()` rather than completing the takeover inline. Once day 731's events drain (`gm.Events.PendingCount == 0`), `LeagueScreen._Process` (`LeagueScreen.cs:110–116`) sets `_shownDay = 731` and calls `Refresh(gm, career)` while the avatar is still the unrostered parent, latching `_tierKnown = false` and `Visible = false` (`:130–132`). Still on day 731, the player selects the heir on `SuccessionScreen` (`SuccessionScreen.cs:107` → `CareerManager.ResolvePendingSuccession` → `TakeOver`), making the rostered HS freshman the new avatar. On every remaining frame of day 731, `LeagueScreen._Process` sets `Visible = hasAvatar && _tierKnown` (`false`, `:102`) and hits `if (day == _shownDay || ...)` (`731 == 731`, `:111`), never calling `Refresh` — leaving the League tab completely blank until day 732. (Autopilot takeover is unaffected because `CompleteTakeover` runs inside `RunRollover` before `PendingCount` drains; `LeagueScreen.Draft.cs:65` shares the same day-only dirty flag for any same-day avatar swap.)
  - **Fix:** Include `career.AvatarPlayerId` (`_shownAvatarId` / `_draftShownAvatarId`) in the dirty-flag check of `LeagueScreen._Process` (`:111`) and `LeagueScreen.RefreshDraftCard` (`:65`) so a same-day takeover or succession refreshes immediately.

#### Nits
- **N1 — `Assets/Localization/strings.csv:45` (`ui.pipeline.declare.deadline`):** Copy reads *"Decide by the first Monday of July. If you don't, your projection and age decide for you."* `PipelineRules.ShouldNpcDeclare` (`Assets/Simulation/Baseball/PipelineRules.cs:463–467`) is `classYear >= 2 && (classYear >= 3 || age >= 22 || (projectedSlot >= 1 && projectedSlot <= 64))`. In steady state (freshman HS start at 16 → College So at 21, Jr at 22), `classYear >= 3` and `age >= 22` coincide (Amendment 3), so the copy holds for standard careers; however, it omits `classYear >= 3` (an age-19 College succession heir reaching class 3 at age 20 auto-declares on class year, not age) and does not indicate which way the default goes (juniors / top-2-round projections declare; lower-projected sophomores stay).
- **N2 — `Assets/UI/BurnerPhone.Pipeline.cs:242–247` & `Assets/UI/LeagueScreen.Draft.cs:197` (1280×800 wrapping):** Dynamic `ReplyChip` choice buttons and pooled Draft board/pick row `Label`s do not set `AutowrapMode = TextServer.AutowrapMode.WordSmart` (unlike `PipelineBodyLabel`, `PipelineDeadlineLabel`, `DraftSummaryLabel`, `DraftNoteLabel`, `StandingsLabel`, and `LeadersLabel`). At 1280×800 (`1600×1000` virtual canvas, `BurnerPhone` 1/3 width ≈ ~417 px inside a `Card`), the longest shipped offer (`"Saltmarsh State Stingrays - ranked #24 nationally"`, 49 chars) fits tightly near the card width, but a slightly longer player name or post-trademark team rename would widen the card or trigger horizontal scroll in `LeagueScroll`.
- **N3 — `Assets/UI/BurnerPhone.Pipeline.cs:230–238` (`RenderAnsweredCard`):** Collapsing to the answered state hides `_pipelineBodyLabel` (`:234`), clears `_pipelineDeadlineLabel.Text = string.Empty` (`:235`), and empties `_pipelineChoicesBox`, but leaves both `_pipelineDeadlineLabel` and `_pipelineChoicesBox` `Visible = true`. Inside `PipelineCardLayout` (`VBoxContainer`, theme `separation = 10`), a visible empty `Label` reserves a line height plus separation. Toggling `.Visible` on `_pipelineDeadlineLabel` and `_pipelineChoicesBox` keeps the answered card compact.

### 9.4 Checklist verification (all items checked in source)

1. **Presentation never drives sim:** `BurnerPhone.Pipeline.cs` calls only `ResolveRecruiting` (`:297`) and `ResolveDeclaration` (`:309`); no DB writes, no `RngState`, clock never stops. Both handlers re-check `TryGetPending*` (`:292`, `:304`) and call `DisablePipelineChoices()` (`:296`, `:308`), and `PipelineManager` invalidates `_pendingCacheDay = -1` (`PipelineManager.cs:666, 1204`), so double-clicks and post-deadline presses never reach a second `Resolve*`.
2. **Polling vs snapshot discipline:** `TryGetPending*` polling (`BurnerPhone.Pipeline.cs:99–106`) rides `PipelineManager`'s per-day cache (`PipelineManager.cs:586–615, 1127–1137`) behind the `(kind, day)` dirty flag (`:112–115`). `LeagueScreen.Draft.cs:57–71` gates `GetStatusView()` (`:90`) on `IsVisibleInTree()`, `Events.PendingCount == 0`, day change, and `inCollege` (`:79–88`); non-College avatars never call `GetStatusView()`.
3. **Strings & `ui-strings`:** All 44 `ui.pipeline.*` keys cover every player-facing string; zero avatar pronouns; names in text only; all 3 negative controls fail as designed (§9.2).
4. **Styling & focus:** Zero per-scene style overrides in new nodes; `DirtAndDiamonds.tres:660–662` adds only `HighlightLabel`. Interactive controls use themed focus styles (`sb_focus`), `LinkPipelineChoiceFocus()` (`BurnerPhone.Pipeline.cs:256–271`) links Up/Down neighbors, and `OnPipelineBannerPressed()` (`:335–342`) moves focus to choice 0. Layout works at 1280×800 (see Nit N2).
5. **Existing-file behavior changes:** (a) `BurnerPhone.cs:855` → `ApplyEventsTabBadge()` (`BurnerPhone.Pipeline.cs:147–151`) preserves gritty-only behavior (`_shownHasPending` updates at `BurnerPhone.cs:686` before `RefreshDecisionAttentionCues` at `:691`) and handles simultaneous gritty + pipeline pending choices cleanly. (b) `LeagueScreen.cs` `_tierKnown` prevents unrostered flicker, with **S1** on same-day interactive takeover.
6. **Inferred data correctness:** Offer ranking (`BurnerPhone.Pipeline.cs:175–208`) mirrors `PipelineManager.RankedPrograms` (`PipelineManager.cs:960–995`) and `PlayoffManager.GetNationalRanking` (`PlayoffManager.cs:1028–1043`); `RankingCalculator.Rank` (`Standings.cs:374–378`) always writes distinct 1..24 ranks, and incomplete rankings fall back to `TeamId` ascending — matching `RunSigningDay`'s auto-pick (`PipelineManager.cs:1281–1293`) in both ranked and unranked cases. Declaration deadline copy reviewed under **N1**.
7. **Node paths (`godot_scene_mapper`):** All 11 `GetNode<T>` paths (7 in `BurnerPhone.Pipeline.cs:66–72`, 4 in `LeagueScreen.Draft.cs:49–52`) verified against `BurnerPhone.tscn` / `LeagueScreen.tscn` (exact path and type) and live headless Godot instantiation (`0` missing).

### 9.5 Unverified items

- Full multi-year interactive Godot live playthrough (advancing day-175 class-2 and day-273 HS-senior saves and exercising the parent-phase save live inside `Main.tscn`) was **not** reproduced in this review session; headless Godot verification covered `--import`, 67/67 translation lookup parity, theme load, scene instantiation, live `GetNode` path resolution, and clean scratch-`APPDATA` v14 boot.

---

## 10. Reconciliation of §9 — Claude Opus 5.5, 2026-11-26

**Branch / worktree:** `p13/13-6b-reconcile-opus` in `C:\Users\DELL\dnd-opus-136b-merge`, fast-forwarded from Neon's `8a5e644` (itself a fast-forward of `2594700` = `p13/13-6b-sonnet`, base `90356ab` = `p13/integration`). **Patch commit:** `3157736`. **Not merged yet:** the patch is Opus-authored C# and copy, so per TEAM §1a it goes to Gemini 4 Neon for a targeted delta re-review before the `--no-ff` merge (§10.4).

### 10.1 Citation check

Every §9 citation was checked against the files before editing: `LeagueScreen.cs:83–117`, `LeagueScreen.Draft.cs:45, 57–71, 197`, `BurnerPhone.Pipeline.cs:230–247`, `strings.csv:45`, `GameManager.cs:562` (`AutopilotSuccession = false`), `PipelineManager.cs:1990–1993` (`ParkTakeoverChoice`), `SuccessionScreen.cs:107` and `PipelineRules.cs:463–467` are all accurate. One immaterial slip: N2 says the scene labels use `WordSmart`; the `.tscn` labels are `autowrap_mode = 2` (`Word`). The code-built precedent (`BurnerPhone.cs:981–1184`) is `WordSmart`, which the patch follows.

### 10.2 Dispositions

| Finding | Disposition | Change |
| :-- | :-- | :-- |
| **S1** same-day takeover leaves the League tab hidden | **Fixed** | `LeagueScreen.cs`: the dirty-flag identity is `(day, career.AvatarPlayerId)` (`_shownAvatarId`), and the gated tier `Refresh` now runs **before** `RefreshDraftCard`, so the Draft card's `IsVisibleInTree()` gate sees the settled `Visible` in the same frame (before, `Visible = hasAvatar && _tierKnown` used the stale verdict, so the card could not self-heal either). `LeagueScreen.Draft.cs`: identity `(day, avatar)` (`_draftShownAvatarId`). `AvatarPlayerId` is read only after the `HasAvatar` early return, so it never throws |
| **N1** declaration deadline copy | **Applied** | `ui.pipeline.declare.deadline`: *"Decide by the first Monday of July. Left unanswered, a junior declares; a sophomore declares only if projected in the first two rounds or 22 or older."* Matches `ShouldNpcDeclare` for the card's class 2–3 window (`AutoDeclareClassYear = 3`, `AutoDeclareAge = 22`, `DeclareProjectedSlotMax = 64` = 2 rounds × 32 Pro picks; class 4 never sees the card). No pronoun; key set unchanged (44 rows) |
| **N2** wrapping at 1280×800 | **Applied** | `AutowrapMode = WordSmart` on the dynamic `ReplyChip` choice buttons (`BurnerPhone.Pipeline.cs`) and the pooled Draft row labels (`LeagueScreen.Draft.cs`) |
| **N3** answered card gap | **Applied** | `RenderAnsweredCard` hides `_pipelineDeadlineLabel` and `_pipelineChoicesBox`; `RenderRecruitingCard` / `RenderDeclarationCard` re-show both |

**Diff `8a5e644..3157736`:** 5 files, +32 / −12: `Assets/Localization/strings.csv`, `Assets/Localization/strings.en.translation`, `Assets/UI/BurnerPhone.Pipeline.cs`, `Assets/UI/LeagueScreen.Draft.cs`, `Assets/UI/LeagueScreen.cs`. All within §2; zero `Assets/Simulation/`, `Assets/Data/`, `Assets/Core/` or `Assets/Narrative/` paths.

### 10.3 Gates on `3157736`

| Gate | Result |
| :-- | :-- |
| Build (`dotnet build DirtAndDiamonds.sln -c Debug`) | **0 warnings / 0 errors** |
| MC default | **608/608**. Normalized log (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`) SHA-256 **`646f3618f74e18cfbca80bf7ebc826c1b77a2e0f2050ca0a16765f7d5d88481e`**, identical to Neon's §9.2 run on `2594700`. Reverting only the 6 `ui-strings` lines (3 `13-6b:` PASS lines; `67→23 keys`; `608→605`; `6/6→3/3`) reproduces the post-13-6a anchor **`c34b1e3f01a35ca76a5216f42592cb8ad6f9b11091c961beaef16555656ab968`** exactly |
| CoreLoop | **43/43** |
| GrittyEvents | **363/363** |
| Godot | `--headless --path . --import` exit 0; `strings.en.translation` `E66DB1E7…` → `56845482…`; every `.import` / `.uid` churn reverted (only the translation kept). The 25 import-log errors are the fresh-worktree first import (empty `.godot/` cache: the theme loads before its Barlow fonts are imported), not this patch. Headless GDScript lookup: **67/67** `strings.csv` rows match the translation, 0 mismatches; the new deadline copy resolves |

**Not run:** a live interactive same-day takeover in `Main.tscn` (it needs a parent-phase save with a willing 16-year-old child on a season's day 1). The S1 fix is reasoned from the frame order above; the re-review may reproduce it or accept the static argument.

### 10.4 Merge decision (TEAM §1a)

The patch is small and UI-only, but it is Opus-authored C# and player-facing copy, and no model approves its own code. It routes to **Gemini 4 Neon** (UI jurisdiction: no sim, schema, persistence or `EffectiveRatings` paths) for a delta re-review of `8a5e644..3157736` (plus this docs commit) on `p13/13-6b-rereview-neon` in `.worktrees/13-6b-rereview-neon`. On PASS, Opus fast-forwards to the re-review head, ticks §6 box 3, merges `--no-ff` into `p13/integration`, reruns the gates post-merge, and logs it. `main` stays untouched (the owner's D8-A go-ahead).

---

## 11. Delta re-review — Gemini 4 Neon, 2026-11-26

**Reviewer:** Gemini 4 Neon (mid-tier engineer and UI/content reviewer, [TEAM.md](../TEAM.md) §1/§1a). **Branch / worktree:** `p13/13-6b-rereview-neon` in `.worktrees/13-6b-rereview-neon` at `e4dd7ff`, reviewing the targeted reconciliation delta `8a5e644..e4dd7ff` (code/assets `8a5e644..3157736`, docs `3157736..e4dd7ff`, authored by Claude Opus 5.5). Docs only; zero edits to `Assets/` or `Tools/`.

**Verdict: PASS** — **0 blockers, 0 should-fixes, 0 nits.**

### 11.1 Preconditions & scope (§0)

- **Environment:** `git fetch` clean; `HEAD` = `origin/p13/13-6b-rereview-neon` = `origin/p13/13-6b-reconcile-opus` = `e4dd7fff2169ef6bdb0819e0af9e1b56e21648ad`; `8a5e644` is an ancestor; clean working tree; git write check (`git commit --allow-empty` + `git reset --soft HEAD~1`) passed after owner granted unsandboxed `git`/`dotnet`/Godot execution; `C:` free space **5.85 GB** (≥ 2 GB).
- **Delta scope (`git diff --stat 8a5e644..e4dd7ff`):** 7 files (`+75 / −17`): `Assets/Localization/strings.csv`, `Assets/Localization/strings.en.translation`, `Assets/UI/BurnerPhone.Pipeline.cs`, `Assets/UI/LeagueScreen.Draft.cs`, `Assets/UI/LeagueScreen.cs` (code patch `3157736`: 5 files, `+32 / −12`), plus `docs/progress.md` and `docs/tasks/13-6b_phone_cards_draft_board_brief.md` (`e4dd7ff`). All within §2; **zero** paths under `Assets/Simulation/`, `Assets/Data/`, `Assets/Core/`, or `Assets/Narrative/`.

### 11.2 Verification of §9 reconciliation items

1. **S1 — Fixed (`Assets/UI/LeagueScreen.cs:85–86, 104–123, 137–139`; `Assets/UI/LeagueScreen.Draft.cs:46–47, 62–75, 83–94`):**
   - Both dirty-flag identities are now `(day, career.AvatarPlayerId)` compared with `StringComparison.Ordinal` (`_shownDay`/`_shownAvatarId` at `LeagueScreen.cs:85–86, 113–121`; `_draftShownDay`/`_draftShownAvatarId` at `LeagueScreen.Draft.cs:46–47, 66–74`).
   - `career.AvatarPlayerId` (`CareerManager.cs:321–322`, throws when `!HasAvatar`) is read only after the `if (!hasAvatar) return;` early return (`LeagueScreen.cs:104–109` → `:114`, `:120`, and `:123` → `LeagueScreen.Draft.cs:67`).
   - The gated `Refresh(gm, career)` (`LeagueScreen.cs:115–121`) runs **before** `RefreshDraftCard(gm, career)` (`:123`), so `Refresh` settles `_tierKnown` and `Visible` (`:137–139`) in the same frame before `RefreshDraftCard`'s `IsVisibleInTree()` check (`LeagueScreen.Draft.cs:62`).
   - **Same-day takeover walkthrough:** unrostered parent on season day 1 → `PipelineManager.RunRollover` (`PipelineManager.cs:1990–1993`) calls `Career.ParkTakeoverChoice()` (`CareerManager.cs:1475–1483`) → `LeagueScreen._Process` settles `(day, parentId)` with `_tierKnown = false`, `Visible = false` → player picks heir on `SuccessionScreen` (`SuccessionScreen.cs:107`) → `CareerManager.ResolvePendingSuccession` (`CareerManager.cs:1322–1346`) → `TakeOver` (`:1569–1593`) → `CompleteTakeover` (`:1556–1562`) → `ActivateAvatar` (`:878–903`) switches `_avatarPlayerId` to the rostered HS freshman and publishes `AvatarChangedEvent` → once `gm.Events.PendingCount == 0` on that same day, `!string.Equals(avatarId, _shownAvatarId, StringComparison.Ordinal)` triggers `Refresh` (`_tierKnown = true`, `Visible = true`, HS Standings/Leaders populated) and `RefreshDraftCard` immediately sees `IsVisibleInTree() == true` (when the League tab is open) and refreshes for `(day, heirId)` (or defers cleanly until tab open if hidden).
   - **No regressions:** `GetStatusView()` remains gated behind `IsVisibleInTree()`, `PendingCount == 0`, `(day, avatarId)` change, and `inCollege` (`LeagueScreen.Draft.cs:62–94`); unrostered parent stays hidden without flicker (`LeagueScreen.cs:105, 138–139`).
2. **N1 — Applied (`Assets/Localization/strings.csv:45`, `Assets/Localization/strings.en.translation`):**
   - `ui.pipeline.declare.deadline`: *"Decide by the first Monday of July. Left unanswered, a junior declares; a sophomore declares only if projected in the first two rounds or 22 or older."*
   - Matches `PipelineRules.ShouldNpcDeclare` (`PipelineRules.cs:463–467`) and its constants (`DraftEligibleClassYear = 2` `:39`, `AutoDeclareClassYear = 3` `:42`, `AutoDeclareAge = 22` `:45`, `DeclareProjectedSlotMax = 64` `:48`; `DraftRounds = 4` `:121` × 32 Pro teams ⇒ top 2 rounds = 64) for the declaration card's class 2–3 window (`PipelineManager.cs:628–629` excludes class 1 and class 4). Zero avatar pronouns. `strings.en.translation` regenerated (SHA-256 `568454827D550B11304FC59D6603C792C65C279EAC891589FE12CC741B6BDEFE`, unchanged across re-import); **67/67** CSV rows match via headless Godot lookup.
3. **N2 — Applied (`Assets/UI/BurnerPhone.Pipeline.cs:250–258`; `Assets/UI/LeagueScreen.Draft.cs:199–204`):**
   - Dynamic `ReplyChip` choice `Button`s (`BurnerPhone.Pipeline.cs:255`, `ExpandFill` inside `_pipelineChoicesBox` `VBoxContainer`) and pooled Draft row `Label`s (`LeagueScreen.Draft.cs:201`, inside `_draftRows` `VBoxContainer`) both set `AutowrapMode = TextServer.AutowrapMode.WordSmart` (matching the code-built precedent in `BurnerPhone.cs:981, 989, 1027, 1032, 1184`; §10.1's `.tscn` check that scene labels use `autowrap_mode = 2` = `Word` in `BurnerPhone.tscn:163, 169, 179` and `LeagueScreen.tscn:29, 47, 64, 70` is confirmed). `LinkPipelineChoiceFocus()` (`BurnerPhone.Pipeline.cs:265–280`) and themed `ReplyChip/styles/focus = sb_focus` (`DirtAndDiamonds.tres:729`) are unaffected.
4. **N3 — Applied (`Assets/UI/BurnerPhone.Pipeline.cs:169–170, 224–225, 234–246, 344–351`):**
   - `RenderAnsweredCard()` hides `_pipelineDeadlineLabel` (`:242`) and `_pipelineChoicesBox` (`:243`). Both `RenderRecruitingCard` (`:169–170`) and `RenderDeclarationCard` (`:224–225`) re-show both. `OnPipelineBannerPressed()` (`:344–351`) is reachable only when `_pipelineBanner.Visible == true`, which `RefreshPipelineCard` (`:136–137`) sets only after one of the two decision renders has re-shown `_pipelineChoicesBox` and added its choice buttons, so `GrabFocus()` on child 0 always targets a visible button.
5. **§10 & `docs/progress.md` accuracy:** All SHAs, file counts/diff stats, dispositions, §6 checkbox states (1, 2, 4 ticked; 3 awaiting this re-review), gate totals, MC SHA-256 hashes, and `strings.en.translation` hash (`56845482…`) match the repository and reproduced runs.

### 11.3 Reproduced gates on `e4dd7ff`

| Gate | Command | Result |
| :-- | :-- | :-- |
| Build | `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings / 0 errors** (10 projects, exit 0) |
| MC default | `dotnet run --project Tools/MonteCarloHarness -c Release` | **608/608** (`ui-strings`: 6/6, 67 keys, 40 referenced / 44 `ui.pipeline.*` rows, 0 pronouns). Normalized log (CRLF→LF, `\d+(\.\d+)? ms` → `N ms`) SHA-256: **`646f3618f74e18cfbca80bf7ebc826c1b77a2e0f2050ca0a16765f7d5d88481e`**. Reverting only the 6 `ui-strings` lines reproduces the post-13-6a baseline SHA-256 **`c34b1e3f01a35ca76a5216f42592cb8ad6f9b11091c961beaef16555656ab968`** exactly |
| CoreLoop | `dotnet run --project Tools/CoreLoopHarness -c Release` | **43/43** (Life ↔ Baseball boundary scan clean, exit 0) |
| GrittyEvents | `dotnet run --project Tools/GrittyEventsHarness -c Release` | **363/363** (exit 0) |
| Godot import & headless check | `Godot_v4.7-stable_mono_win64_console.exe --headless --path . --import` + scratch GDScript on scratch `APPDATA` (all `.import`/`.uid` churn reverted) | `--import` exit 0 (expected fresh-worktree Barlow first-import cache-order noise only); `strings.en.translation` SHA-256 byte-identical pre/post import (`568454827D550B11304FC59D6603C792C65C279EAC891589FE12CC741B6BDEFE`); **67/67** `strings.csv` rows match translation (`ui.pipeline.declare.deadline` resolves to exact N1 text); `DirtAndDiamonds.tres`, `BurnerPhone.tscn`, and `LeagueScreen.tscn` load/instantiate with 0 errors |
| Live same-day takeover in `Main.tscn` | Optional | **Not run**; S1 static frame-order proof in §11.2 accepted |

### 11.4 Findings

- **Blockers:** None.
- **Should-fix:** None.
- **Nits:** None.

**Ready for merge:** Claude Opus 5.5 may fast-forward to this re-review head, tick §6 box 3, merge `--no-ff` into `p13/integration`, and run the post-merge gates. `main` remains untouched pending the owner's D8-A go-ahead.
