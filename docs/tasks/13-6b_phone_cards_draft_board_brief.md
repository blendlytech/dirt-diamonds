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

- [ ] §0 recorded; §4 items 1–5 built; every string keyed; focus navigation on every control.
- [ ] §5 gates green; the manual check and the parent-phase smoke check recorded.
- [ ] Gemini 4 Neon's review passed. Then Opus merges into `p13/integration`.
- [ ] Handoff (§8) and one `progress.md` line. **Then stop.**

## 7. Out of scope

Any engine change (the pipeline, the queries, the events, the texts); Draft Night and Signing Day cutscenes (16b); the 15c restyle; the parent-phase jobs, the training mini-game and their UI (the parent-phase brief); the `RichPresenceWriter` tier latch (Carbon §12.3, the parent-phase / 13-7 pass).

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built> · Commit: <sha> · Gates: build, MC (total, byte-identical outside ui-strings), CoreLoop, Gritty, Godot (import, scenes, manual + parent-phase checks)
Open: <unfinished items, risks, owner questions>
Recommended next: 13-6b review by Gemini 4 Neon (then Opus merges into p13/integration)
```

Then write the complete prompt for the next model (TEAM.md §3 step 8) and add one compact line to [progress.md](../progress.md). Don't start the next slice.
