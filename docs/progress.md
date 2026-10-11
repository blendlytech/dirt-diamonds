# Progress & Next Steps

Session-orientation file for every model. **Keep it lean:** at session end, append one compact entry to the Session Log (what shipped, the commit, the gates, the next step and its owner). Full history lives in [archive/progress_archive.md](archive/progress_archive.md). When the log passes ~30 entries, move the oldest there. Roles: [TEAM.md](TEAM.md). Docs map: [README.md](README.md).

---

## Current status (2026-10-10): R-1 recommendations accepted; scoped implementation briefs ready

**Owner follow-up:** all R-1 recommendations are accepted. Canonical specs and [BUILD_PLAN execution order](BUILD_PLAN.md#r-1-accepted-execution-order-2026-10-10) now include eight repair briefs. Runtime fixes are pending their assigned owners and Math gates; no new paid generation or external account action is authorized.

**The game is defined** in [GAME_VISION.md](GAME_VISION.md) v2, with the owner-approved R-1 amendments. R-1 review and decision reconciliation are complete on review/r1; runtime implementation remains pending. Phase G begins with R1-MATH/G-8a acceptance, and Phase 13 with 13-0 then 13-1.

| Phase | Spec | State | Very next action (owner) |
| :-- | :-- | :-- | :-- |
| **13 League Restructure** (critical path) | [design/league_restructure.md](design/league_restructure.md) | Design complete | **13-0:** the MC flake is root-caused (random `Guid.NewGuid()` ids in `CareerManager`); an in-flight counter fix needs one change before merge ([tasks/13-0_review_careermanager_ids.md](tasks/13-0_review_careermanager_ids.md)); repair the `sqlite` MCP to point at a scratch DB. Then **13-1** schema v14 + world-gen (Claude Opus). |
| **G Gameplay Core** (**high priority**) | [design/gameplay_core.md](design/gameplay_core.md) | Design draft; [pitching_mode.md](design/pitching_mode.md) **rev 2** (on `main`) | **G-2:** Gemini Pro Math curve review of rev 2, then owner acceptance; then the engine (GPT 6 Astra Pro, G-8a → G-2a → G-2b → G-8b). **G-8a** Read v2 design (GPT) can start now. **G-1** runs inside R-1 (GPT 6 Astra Pro). |
| **L Lineage, Temptation & Vice** | [design/temptation_and_lineage.md](design/temptation_and_lineage.md), [design/substances_and_addiction.md](design/substances_and_addiction.md) | Design complete; **L-0 done** | **L-1** after 13-1 (schema v15 + identity + legends + poverty). |
| **14 Voice & Broadcast Audio** | [design/voice_and_broadcast_audio.md](design/voice_and_broadcast_audio.md) | Design complete (broadcasters only) | 14a buses + `VoiceDirector` (Gemini Pro); 14b `Tools/VoiceGen` pilot (GPT Astra Pro; needs the owner's commercial ElevenLabs plan). Can run alongside 13. |
| **15 Visual Redesign** | [design/visual_redesign.md](design/visual_redesign.md) | Design complete | 15a style frames for both worlds. **The owner picks the art direction.** |
| **16 Cutscenes** | [design/cutscenes_motion_comic.md](design/cutscenes_motion_comic.md) | Design complete | Blocked on 14a/14b + 15a/15b. |
| **17 Steam Commercial Release** | [design/steam_commercial_release.md](design/steam_commercial_release.md) | Design complete | Early owner actions: Steamworks app, trademark screen, Coming Soon timing. |

**Owner decisions (canonical record: GAME_VISION v2; detail: temptation §0 D1–D21):**

- **2026-10-09:** 56 unique announcers plus 3 HS radio voices; in-engine motion-comic cutscenes; schema v14 is a clean break for old saves; a complete visual redesign is allowed.
- **2026-10-10:**
  - Voice is **broadcasters only**.
  - **Skill band (O1):** player skill is worth about the 40-vs-70 rating gap (±.100 OPS around ratings), capped, with a blind or average player at the ratings line, for pitching **and** batting. Batting gets re-tuned (G-8, GPT 6 Astra Pro).
  - **GPT 6 Astra Pro** is a full senior engineer for gameplay models (plentiful subscription usage, so prefer it over metered credit for heavy work).
  - **Never abortion**, and adoption is always offered.
  - The GOAT-father lineage, with the first text on the first Pro contract and the reveal on a World Championship or at career end.
  - Freshman start and guaranteed poverty. No mentors.
  - Male or female avatar. Dating either gender, and adopting in.
  - The Trailblazer route, once per save.
  - The missing piece and the paid search (child ≥ 19).
  - Opponent and teammate reveals. Heir age stays 19.
  - Affairs: confidence 100 plus clutch, with escalating random risk.
  - Five substances, each with a distinct edge and price. Boosts are 18+. A fatal overdose is possible.
  - The father's record is juiced in 50% of careers.
  - The repo is private.

**Open for the owner:**

- the art direction (15a);
- league, brand and team names;
- final league/brand naming and clearance (league §16 I1–I5 defaults are accepted);
- the ethnicity list and legend backstories (L-2);
- the two owner-authored functions, `ChooseFatherFallback` and `DiscoveryChance`;
- operational verification that any auto-committer cannot target main/shared branches (policy settled, TEAM.md §5);
- Math delivery of the accepted gameplay constants/contracts; all five Phase G owner choices are settled by O1 and R-1.

**Gate baseline (2026-10-10):**

- Build: `dotnet build DirtAndDiamonds.sln -c Debug` gives 0 warnings, 0 errors.
- GrittyEvents: **363/363**.
- MonteCarlo: last full run 345/345, with a rare stochastic flake under 13-0.
- Schema: **v13** shipped (v14 is next, v15 follows for Phase L).

**Known state:**

- The live save `dirt_and_diamonds.db` was found at 0 bytes on 2026-10-09. That's moot after v14.
- The shipped `place_for_adoption` choice doesn't create a child yet (fixed in L-4).
- The shipped steroid multiplier has no trigger (fixed in S-1).

---

## Very next steps

1. **Gemini 3.1 Pro Math: G-2 curve review** of [pitching_mode.md](design/pitching_mode.md) rev 2 (the §8 constants, the §7.3 estimates, the §3.5 centering), then **owner acceptance** of the spec.
2. **GPT 6 Astra Pro: G-8a** Read v2 design, per its brief (unblocked: rev 2's §3.5/§4.4 is the centering template).
3. **Claude Opus: 13-0 → 13-1**, the critical path.
4. **GPT 6 Astra Pro: R-1** full-project review in its own worktree `..\dnd-gpt-review` ([brief](tasks/R-1_full_review_brief.md)), gameplay first (G-1 inside it). Runs alongside G-8a; output to `docs/review/r1/`, then owner + Opus triage.

---

## Session Log (compact)

- **2026-10-10 — R-1 reassigned + G-work merged to `main` (owner; Claude Opus 5.5 recorded it; docs only):** owner decisions: (1) merge the G-work branch chain (`g2/fable-pack` → `g2/fable-design` → `g8/read-v2-brief` → `g2/spec-rev2`) into `main`; (2) **R-1 goes to GPT 6 Astra Pro**. The brief was renamed `tasks/R-1_full_review_brief.md` (output `docs/review/r1/`, worktree `..\dnd-gpt-review`, branch `review/r1`), and G-1 moves inside it. TEAM.md marks Fable 5.1 inactive. CLAUDE.md, GEMINI.md, README, BUILD_PLAN and gameplay_core were updated to match. **Next: Math curve review of G-2 rev 2 ∥ G-8a + R-1 (GPT 6 Astra Pro, separate worktrees) ∥ 13-0 (Opus).**

- **2026-10-10 — Fable 5.1 check-up + G-2 spec rev 2 (Claude Opus 5.5, branch `g2/spec-rev2`; docs only, no code/sim touch):** audited what Fable 5.1 left. R-1 (full review) **was never started**. Fable's only output is the G-2 design (`6ba9055`), which is high quality and sound, but its revision pass never ran. Re-derived every number in the spec and in the feasibility review (all of the review's reproduce), then applied the review in place as **rev 2**: B1 centering (new §3.5: analytic `ExecCenter`, zero-mean "indicator minus reference" anticipation terms, G1 restated with a ≤ .008 second-order residual), B2 waste-preserving mixture, B3 closed-form Reference `pIn`, C1 (Challenge re-derived at ≈ .78–.81 vs .728), C2 doubles, engine answers Q1–Q10 + Q3b folded in (verified in code). New findings fixed: C3 (control-20 scatter numbers), C4 (max-mass bound), D1 (`aT` miss term used `ρT[type]`, not zero-mean). Updated TEAM.md (R-1 not started; G-1 blocked; G-2 rev 2), BUILD_PLAN G-1/G-2 owners, the G-8 brief (points to rev 2) and the R-1 brief (status banner). Gates not run (docs only). **Next: Gemini Pro Math curve review of rev 2 ∥ G-8a by GPT 6 Astra Pro ∥ 13-0 (Opus); the owner decides R-1.**

- **2026-10-10 — G-2 engine reassigned (owner; Claude Opus 5.5 recorded it):** the G-2a/b/d pitching engine moves from Claude Opus to **GPT 6 Astra Pro**; G-2c/G-2d UI stays with Sonnet; reviews are Gemini Pro (sim diff) + Opus (merge gate). GPT owns both `SimulatePa` slices and sequences them G-8a → G-2a → G-2b → G-8b. Opus is freed for Phase 13. TEAM.md takes precedence over the owner column of pitching_mode.md §10. **Next: the G-2 engine brief (Opus) after Fable's revision lands.**
- **2026-10-10 — O1 decided + G-8 brief (Claude Opus 5.5; docs only):** owner: re-tune batting to the ±.100 OPS band and use GPT 6 Astra Pro. New [tasks/G-8_read_v2_batting_reband_brief.md](tasks/G-8_read_v2_batting_reband_brief.md); TEAM.md (GPT role expanded, G-8 row), gameplay_core §4/§6 updated. **Next: Fable G-2 revision (in flight, `g2/fable-design`) ∥ G-8a design by GPT 6 Astra Pro in `..\dnd-gpt`.**
- **2026-10-10 — G-2 pitching-mode design (Claude Fable 5.1, branch `g2/fable-design`; docs only, no code/sim touch):** new [design/pitching_mode.md](design/pitching_mode.md): three inputs (type, 17-cell target, effort), triangular scatter from control/effort/movement/fatigue, a new `BatterIntentKind.Model` branch that tilts the neutral chain's location-conditioned rates resolver-style (reference pitcher = ratings by construction, band = gain budget ±.100 OPS), an NPC tendency book with capped leans as the anti-omniscience bound, "always challenge" dominated because known strikes lose called strikes, Invariant N by an early `HasCell` branch, 8 scripted bots + 9 gates (suite 8), 50-row constants table for Pro Math, 4 engine slices (G-2a/b Opus, G-2c Sonnet, G-2d both). Disagreement: batting as shipped (1.8 OPS spread) needs a Read v2 re-band to the same idiom. Gates not run (no code). **Next: G-2 feasibility review by Claude Opus 5.5 (+ Gemini Pro Math on the curves), then a Fable revision pass.**
- **2026-10-10 — G-2 Fable handoff pack (Claude Opus 5.5; docs only, no code/sim touch):** the owner assigned the G-2 pitching-mode **design** to Claude Fable 5.1 on prepaid API credit ($13.06, $5 approval steps). New [tasks/G-2_pitching_context_pack.md](tasks/G-2_pitching_context_pack.md) (verified code facts, so Fable reads nothing else) and [tasks/G-2_pitching_design_fable_brief.md](tasks/G-2_pitching_design_fable_brief.md). TEAM.md §2 and gameplay_core §4 G-2 rows updated. gameplay_core §1.4 corrected: an unused engine pitching seam exists (`PitchCall`, `InteractivePitcherPolicy`); the career path passes `NeutralPitcherPolicy`. **Finding:** the shipped batting A0 spread is perfect 1.898 / random .439 / fooled .101 OPS, far wider than the §1.4 "±.100 OPS" skill target, and a blind guesser bats well below the ratings-only .727. This is an owner/Math decision, and the Fable spec must take a position. MC 352/352. **Next: G-2 design by Claude Fable 5.1 (new session, API key, its own worktree) → Opus feasibility review → Fable revision.**
- **2026-10-10 — Vision v2 + full docs reconciliation (Claude Opus 5.5; docs only, no code/sim touch):**
  - GAME_VISION rewritten as v2, start to finish, with D1–D21.
  - BUILD_PLAN condensed (Phases 0–12 into one table) and Phase L added; the stale delegation doctrine is gone.
  - TEAM.md gains Phase L/S slices and Fable 5.1's reviewer role.
  - GEMINI.md §10 added; new CLAUDE.md (imports GEMINI.md) and AGENTS.md.
  - Every reference doc re-headed; archive docs bannered; the July-era progress sections moved to the archive.
  - The Fable 5.1 review brief written.
- **2026-10-10 — Docs reorganized by authority (`bf37329`, Claude Opus 5.5):** `design/` (active) · `reference/` (shipped, 29) · `archive/` (superseded, 7); every link and path rewritten; 0 broken links; build 0/0; GrittyEvents 363/363.
- **2026-10-10 — L-0 (`6a488cd`, Claude Opus 5.5):** voice is broadcasters-only across every spec, rule and skill; new designs `temptation_and_lineage.md` and `substances_and_addiction.md`; `end_the_pregnancy` removed from `hs_pregnancy_decision`; 16-1 brief revised; GrittyEvents 363/363.
- **2026-10-10 — Team model confirmed + 13-0 flake root-caused (Claude Opus 5.5; docs only, no code/sim touch):** owner confirmed [TEAM.md](TEAM.md) (Opus = integration lead); GEMINI.md mission, league_restructure owner line and TEAM.md §3.2 (one **worktree** per model) updated. MC flake = `CareerManager` minting avatar/parent/heir ids with `Guid.NewGuid()`; random ids reorder `(team_id, player_id)` RNG consumption (observed: slot 34 vs 42, lineage drift bound `90→41→37` pass vs `90→39→36` fail; isolated replay is deterministic). A parallel Gemini session's in-flight counter fix collides ids after a reload; review + required change in [tasks/13-0_review_careermanager_ids.md](tasks/13-0_review_careermanager_ids.md). Gates not run on a final fix (none merged yet). **Next: Gemini finishes 13-0 per the review in its own worktree → Opus re-review + 20/20 MC → 13-1 (Opus).**
- **2026-10-09 — New vision documented (docs only, no code/sim touch):** user directive to rebuild the league per the source spec, ship for sale on Steam, add ElevenLabs voice (per-team announcers with taglines, phone voice notes, cutscene VO), animated cutscenes for story/achievements/big plays, and a completely new visual design. New: `GAME_VISION.md` (replaces the missing `GAME_IDEA.md`); design docs `league_restructure.md` (P13), `voice_and_broadcast_audio.md` (P14), `visual_redesign.md` (P15), `cutscenes_motion_comic.md` (P16), `steam_commercial_release.md` (P17). BUILD_PLAN gets Phases 13–17 and a dependency map; GEMINI.md rewritten; rules updated (baseball_engine, database_schema, ui_conventions, gritty_events, new audio_voice_cutscenes); skills updated (check_event_graph_integrity checks #9/#10 and tier ordinals, run_monte_carlo_batch Phase 13 note, new validate_audio_manifest); all 33 prior design docs bannered with what changed; checklist, GAME_IMPROVEMENTS, interleave plan and Steam README annotated. Gates not run (no code touched). **Next: 13-0 (sqlite MCP + MC flake), 15a style frames for the user, 14a/14b pilot.**
- **2026-10-09 — Docs reconciliation (docs only, no code/sim touch):** BUILD_PLAN phase statuses corrected (8/9, 10, 11 COMPLETE; Phase 12 + post-12 arcs added), legacy→Gemini role table added, the user's league spec framed as **Phase 13 — League Restructure (PROPOSED)** with its conflicts against shipped code enumerated (tier collapse + save migration, `const` roster sizes vs `MicroGame` stackalloc, HS 1-game/week vs the 26-game calendar, `PromotionManager` → draft, MLB guard re-sanction). Gates not run (no code touched). **Next: repair `sqlite` MCP → MC flake recap/seed → Phase 13 design doc (Gemini 3.1 Pro Math).**

- **2026-10-10 — R-1 review completed (GPT 6 Astra Pro, branch review/r1; baseline 2dec343):** seven reports in [review/r1/00_project_map.md](review/r1/00_project_map.md); checkpoint commits 79f94c9, 95a4d7b, 2d16a84, 16ebe68, 2a9df67. G-1 weights verified; pitching centering/fatigue/waste conflicts and save/relationship/narrative risks documented. Plain factual doc corrections are listed in 01_conflicts; no runtime code changed. Gates: Debug build 0 warnings/errors; Gritty 363/363, CoreLoop 22/22, Needs 117/117, Hustle 95/95, Holdem 63/63, Schema 111/111. MC first 351/352 (failing name not retained), diagnostic repeat 352/352; flake unresolved. No live-save or Godot/export verification. **Next: owner + Claude Opus R-1 triage (17 questions), then 13-0 by Opus with Gemini Pro review; accepted repair proposals need briefs.**

- **2026-10-10 — Owner accepted all R-1 recommendations (GPT 6 Astra Pro, review/r1; docs/contracts only):** recorded Q01–Q17 approval; reconciled vision/G/lineage/draft/NPC PED/cutscene/voice/Steam contracts and workflow. Added eight scoped briefs (SAVE-1/2, FAMILY, MATH, EFFECTS, LEARN, REVEAL, STEAM) with assigned models, dependencies, allowed paths and gates; linked from BUILD_PLAN/TEAM. Park factors/manual fielding deferred beyond 1.0; practice and modifier clarity accepted. Runtime implementation, Math proofs and external auto-committer configuration remain pending. Concrete art/names/legend/function inputs and account prerequisites are still needed. Verification: documentation diff/link checks; no build rerun for documentation-only edits. **Next: 13-0 by Opus (Gemini Pro review), alongside R1-MATH by Gemini Pro Math and G-8a by GPT; SAVE-1 follows.**
