# Task Brief: `W1` — SR-0 doc, link and agent-rule repair (parallel wave)

**Author:** Claude Opus 5.5 (integration lead), 2026-11-19 · **Runner:** Neon Orchestrator · **Workers:** Gemini Hydrogen ×5 (H1–H5) + Gemini 4 Neon (N1) · **Reviewer:** Gemini 4 Neon (combined diff) · **Integration gate:** Claude Opus 5.5 · **Owner gate:** none (docs, rules and one harness file; no runtime code)

Binding: [GEMINI.md](../../GEMINI.md) §1–§11 and [TEAM.md](../TEAM.md) §1a. Source of the findings: [full_project_scan_opus_handoff.md](../review/full_project_scan_opus_handoff.md) §2.5 and §4.1 ("Blind harness tests").

## 1. Context

Commit `2da33f9` deleted five docs that live specs, briefs, `GEMINI.md` and the agent rules still link to or rely on. Several `.agents/rules/` files still describe the cancelled full-arcade engine, 59 per-team voices and interactive Fencing/Robbery, which contradicts GAME_VISION v3.0. Agents load those rules every session, so they keep spreading the cancelled scope. This wave restores the contracts, fixes every broken link and brings the rules in line with v3.0. **All destinations below are already decided. Workers apply them and make no design calls of their own.**

## 2. Hard constraints

- **Branch:** `docs/W1-repair-neon`, created from `p13/integration` at or after `db95744`, in its own worktree (`git worktree add ..\dnd-neon-w1 -b docs/W1-repair-neon p13/integration`). Before starting, check that the worktree can write to the shared `.git` (a test commit on a throwaway branch). Never commit to `p13/integration` or `main`.
- **Allowed paths:** only the files named in each worker's section. **No file under `Assets/`** (N1 may touch only `Tools/GrittyEventsHarness/Program.cs`). No `.tscn`, no `.csproj`, no data JSON.
- **Restored text is copied verbatim** with `git show 2da33f9^:<path>`. Only a banner is added at the top. No rewording.
- **Disjoint files:** each worker touches only its own files, so the wave merges without conflicts. If a fix needs a file that belongs to another worker, report it in the handoff and don't edit it.
- No deletions except the `git mv` moves in H3.

## 3. Inputs

- Deleted sources, all at `2da33f9^`: `docs/design/gameplay_core.md`, `docs/design/arcade_baseball_overhaul.md`, `docs/reference/at_bat_read_input_model.md`, `docs/review/visual_direction_3d_proposal.md`, `docs/tasks/G-0_arcade_feel_prototype_brief.md`.
- The v3.0 truth: [GEMINI.md](../../GEMINI.md) §1, §5, §9, §11; [scope_realignment.md](../design/scope_realignment.md) §2–§3.
- Broken-link baseline on `db95744`: **41** (listed in §5).

## 4. Outputs, by worker

### H1 (Hydrogen) — restore contracts (new files only)

| Restore from `2da33f9^` | To | Banner (first line, then a blank line) |
| :-- | :-- | :-- |
| `docs/reference/at_bat_read_input_model.md` | **same path** | `> **Restored 2026-11-19 (W1).** Documents the shipped attended MicroGame read input, which is authoritative again under GAME_VISION v3.0 / GEMINI §11. Clutch Moment (SR-3a) layers on top; it doesn't replace this.` |
| `docs/design/gameplay_core.md` | `docs/archive/gameplay_core.md` | `> **Archived 2026-11-19 (W1).** Superseded by [scope_realignment.md](../design/scope_realignment.md). §1 attribute contracts remain binding and live in [reference/attribute_contracts.md](../reference/attribute_contracts.md).` |
| §1 only of `gameplay_core.md` (from `## 1. Attributes: the definitions` up to, not including, `## 2.`) | `docs/reference/attribute_contracts.md` | `# Attribute contracts` then `> **Extracted 2026-11-19 (W1)** verbatim from the archived gameplay_core.md §1. Binding per GEMINI §11.` |
| `docs/design/arcade_baseball_overhaul.md` | `docs/archive/arcade_baseball_overhaul.md` | `> **Archived 2026-11-19 (W1).** Full arcade physics, AI and calibration were cancelled 2026-10-25; see [scope_realignment.md](../design/scope_realignment.md) §2.1.` |
| `docs/review/visual_direction_3d_proposal.md` | `docs/archive/visual_direction_3d_proposal.md` | `> **Archived 2026-11-19 (W1).** Historical proposal; G-0c cancelled.` |
| `docs/tasks/G-0_arcade_feel_prototype_brief.md` | `docs/archive/G-0_arcade_feel_prototype_brief.md` | `> **Archived 2026-11-19 (W1).** Prototype complete; follow-on work is SR-3a/SR-3b.` |

Inside the restored files, fix relative links so they resolve from the new location. Don't change any other text.

### H3 (Hydrogen) — archive closed briefs

`git mv` these four from `docs/tasks/` to `docs/archive/`: `13-0_review_careermanager_ids.md`, `CS-M_college_math.md`, `13-1a_seed_data_flash_brief.md`, `G-0c_3d_look_test_brief.md`. Add the banner `> **Archived 2026-11-19 (W1).** Closed brief; kept for history.` to each. H3 owns **every link inside the four moved files** and **every inbound link to them** from any file in the repo (find them with `grep -rn "<filename>" --include=*.md`). For the G-0c brief, point its dead links at the H1 archive paths.

### H2 (Hydrogen) — retarget the remaining broken links

Rules: `arcade_baseball_overhaul.md` → `archive/arcade_baseball_overhaul.md` (relative from the source). `gameplay_core.md` → `reference/attribute_contracts.md` when the sentence is about attributes, otherwise `archive/gameplay_core.md`. `visual_direction_3d_proposal.md` → `archive/visual_direction_3d_proposal.md`. `../architectural_refactoring_scope_alignment_directive.md` → `../ARCHITECTURAL_DIRECTIVE_V1.md`. `at_bat_read_input_model.md` resolves once H1 lands, so leave it alone. The `file:///c:/Users/DELL/...` links in `docs/reference/asset_generation_status.md` → relative `../../Assets/Graphics/HighSchools/...`. Also change the GEMINI.md §11 sentence "Attribute contracts remain in [docs/design/gameplay_core.md] §1" to point at `docs/reference/attribute_contracts.md` (link text and target).

H2 files: `GEMINI.md`, `docs/BUILD_PLAN.md`, `docs/GAME_VISION.md`, `docs/progress.md` (status lines only, never past log entries' wording beyond the link target), `docs/design/{cutscenes_motion_comic,league_restructure,scope_realignment,temptation_and_lineage,visual_redesign,voice_and_broadcast_audio}.md`, `docs/reference/{asset_generation_status,at_bat_presentation}.md`, `docs/tasks/{13-1_schema_v14_worldgen_brief,13-2_roster_capacity_innings_starters_brief,R-1_full_review_brief,R1-EFFECTS_life_projections,R1-LEARN_practice_and_modifiers,R1-MATH_gameplay_contracts}.md`.

### H4 (Hydrogen) — sync `.agents/rules/` to v3.0

Replace each stale passage with the matching GEMINI.md wording. Quote GEMINI rather than paraphrasing it.

- `baseball_engine.md` lines ~28–34 ("Arcade engine … every game the avatar's team plays runs on a real-time physics engine…"): replace with the GEMINI §11 bullets. `MicroGame` stays authoritative for attended games. `Assets/Simulation/Arcade/` is only the optional offense-only Clutch Moment timing core. Neutral paths stay byte-identical. There is one `EffectiveRatings` stack. Gates are ArcadeHarness plus `run_monte_carlo_batch`. Remove AI-vs-AI calibration.
- `audio_voice_cutscenes.md` line 7: "(56 College/Pro booths + 3 HS pool voices)" → "(six conference announcer personas, two per tier, one booth per conference; GEMINI §5)". Also fix any other per-team-voice counts in the file.
- `gritty_events.md` lines ~32–33: Fencing and Robbery are **Burner Phone narrative branches** (SR-1b, `outcomes[]`), not interactive hustle nodes. Narcotics and Texas Hold'em stay interactive. Keep the engine-flag list on line ~37 unchanged: `hot_goods`, `robbery_bust` and `fencing_sting` are preserved chains.
- `life_sim_ai.md`: add a short "Weekly Lifestyle Stance" section quoting GEMINI §1's last bullet (four stances; school/practice/work/upkeep resolve automatically; `DaySchedule` and chore-clicking retired; week advance stops on stress spikes, depleted funds, gritty events, game days).
- `ui_conventions.md` line 34: remove the nonexistent `Assets/Data/Teams/team_colors.json`. Write instead: "HS team colors come from `Assets/Data/Schools/schools.json` (`colors`); College/Pro palette source is defined in 15b. All styling flows through the Theme family (GEMINI §7)."

### H5 (Hydrogen) — sync `.agents/skills/`

- `validate_audio_manifest/SKILL.md` (line ~20 "every College and Pro team (56) has a persona"): change to six conference personas (two per tier including HS), each with all clip categories at their minimum counts. Fix any other 56/59 references in that skill.
- `run_monte_carlo_batch/SKILL.md`: add the GEMINI §9/§11 rules. Changes under `Assets/Simulation/Arcade/` also run `dotnet run --project Tools/ArcadeHarness -c Release`. Clutch integration must keep neutral macro/micro output byte-identical. A change to the shared `EffectiveRatings` stack runs both harnesses.

### N1 (Gemini 4 Neon) — un-blind the school-catalog negative tests

`Tools/GrittyEventsHarness/Program.cs` ~4496–4512: the six negative `SchoolParseThrows` test inputs use the old `"image_path"` field. The loader now requires `"images": { "logo", "school", "field" }`, so every one of them throws early on the missing `images` and never reaches the defect it claims to test.

- Rewrite each input to the current valid shape, with exactly one defect: bad hex, missing colors, missing `school_name`, non-integer `team_id`, or duplicate `team_id`.
- Turn the "missing image_path" case into "missing `images.field`".
- Prove each case tests its own defect: temporarily remove the defect and the test should then pass parsing. Note the result in the handoff, then revert the probe.
- No other file.

## 5. Tests and gates

1. Run the link check from the repo root, skipping `docs/archive/` and `docs/review/` as sources: **0 broken relative links**. The baseline is 41 at `db95744`, made up of `gameplay_core.md` ×9, `arcade_baseball_overhaul.md` ×18, `at_bat_read_input_model.md` ×2, `visual_direction_3d_proposal.md` ×4, the directive rename ×1, `file:///` ×6, and `G-0_arcade_feel_prototype_brief.md` ×1.
2. `grep -rniE "real-time physics engine|AI-vs-AI|56 College/Pro|\(56\)|team_colors\.json" .agents/` → **0 hits**.
3. `git diff --stat p13/integration` lists only the files named in §4 (plus the new and archived files).
4. N1: `dotnet build DirtAndDiamonds.sln -c Debug` → 0 warnings, 0 errors. `dotnet run --project Tools/GrittyEventsHarness -c Release` → all green, with the school-catalog count unchanged or higher.
5. Neon reviews the combined diff against this brief. Restored files must match `2da33f9^` byte-for-byte apart from the banner and link-path fixes (check with `git diff --no-index`).

## 6. Definition of done

All five gates pass. Neon's review verdict is recorded at the end of this brief (§9). One compact `docs/progress.md` entry. Handoff sent to the human. Opus then fast-forwards or merges into `p13/integration`.

## 7. Out of scope

No rewording of the restored docs. No new design content. No runtime C# outside N1's harness file. No asset, `.import`, `export_presets.cfg` or deletion work: duplicate art and stubs wait for per-item owner approval, and the `.import` sidecars are Opus's. Don't touch the parked `15a/industrial-broadcast-draft` branch.

## 8. Handoff (required)

End with: commit SHA(s) on `docs/W1-repair-neon`; each gate's result with its output; the N1 probe results; any link you couldn't fix and why; files you needed but didn't own. **Recommended next:** Opus integration gate, then Wave 2 (13-3 Math check by Gemini 4 Argon). Don't start Wave 2 yourself.

## 9. Review verdict (Neon fills in)

Pending.
