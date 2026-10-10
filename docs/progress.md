# Progress & Next Steps

Session-orientation file for every model. **Keep it lean:** at session end, append one compact entry to the Session Log (what shipped, the commit, the gates, the next step and its owner). Full history lives in [archive/progress_archive.md](archive/progress_archive.md). When the log passes ~30 entries, move the oldest there. Roles: [TEAM.md](TEAM.md). Docs map: [README.md](README.md).

---

## Current status (2026-10-10): vision v2 locked, docs reconciled, full review next

**The game is defined** in [GAME_VISION.md](GAME_VISION.md) **v2**, which covers everything start to finish, including the lineage, temptation, vice and Trailblazer systems. **Next:** Claude Fable 5.1's independent full-project review ([tasks/fable_5_1_full_review_brief.md](tasks/fable_5_1_full_review_brief.md)), then the build continues from its accepted proposals.

| Phase | Spec | State | Very next action (owner) |
| :-- | :-- | :-- | :-- |
| **13 League Restructure** (critical path) | [design/league_restructure.md](design/league_restructure.md) | Design complete | **13-0:** the MC flake is root-caused (random `Guid.NewGuid()` ids in `CareerManager`); an in-flight counter fix needs one change before merge ([tasks/13-0_review_careermanager_ids.md](tasks/13-0_review_careermanager_ids.md)); repair the `sqlite` MCP to point at a scratch DB. Then **13-1** schema v14 + world-gen (Claude Opus). |
| **G Gameplay Core** (**high priority**) | [design/gameplay_core.md](design/gameplay_core.md) | Design draft | **G-1** attribute audit (Fable 5.1 within R-1 → Math) and the **G-2** pitching-mode design (Claude Opus), alongside 13-0/13-1. |
| **L Lineage, Temptation & Vice** | [design/temptation_and_lineage.md](design/temptation_and_lineage.md), [design/substances_and_addiction.md](design/substances_and_addiction.md) | Design complete; **L-0 done** | **L-1** after 13-1 (schema v15 + identity + legends + poverty). |
| **14 Voice & Broadcast Audio** | [design/voice_and_broadcast_audio.md](design/voice_and_broadcast_audio.md) | Design complete (broadcasters only) | 14a buses + `VoiceDirector` (Gemini Pro); 14b `Tools/VoiceGen` pilot (GPT Astra Pro; needs the owner's commercial ElevenLabs plan). Can run alongside 13. |
| **15 Visual Redesign** | [design/visual_redesign.md](design/visual_redesign.md) | Design complete | 15a style frames for both worlds. **The owner picks the art direction.** |
| **16 Cutscenes** | [design/cutscenes_motion_comic.md](design/cutscenes_motion_comic.md) | Design complete | Blocked on 14a/14b + 15a/15b. |
| **17 Steam Commercial Release** | [design/steam_commercial_release.md](design/steam_commercial_release.md) | Design complete | Early owner actions: Steamworks app, trademark screen, Coming Soon timing. |

**Owner decisions (canonical record: GAME_VISION v2; detail: temptation §0 D1–D21):**

- **2026-10-09:** 56 unique announcers plus 3 HS radio voices; in-engine motion-comic cutscenes; schema v14 is a clean break for old saves; a complete visual redesign is allowed.
- **2026-10-10:**
  - Voice is **broadcasters only**.
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
- the league_restructure §16 interpretations;
- the ethnicity list and legend backstories (L-2);
- the two owner-authored functions, `ChooseFatherFallback` and `DiscoveryChance`;
- whether to keep the auto-committer (TEAM.md §5);
- the five Phase G decisions (gameplay_core §6): how much skill is worth, needs and stress on the field, more person stats on the field, park factors, fielding moments.

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

1. **Claude Fable 5.1: the full-project review**, with Phase G (gameplay) as its first focus (R-1, per its brief). It writes to `docs/review/fable_5_1/`.
2. **Owner + Claude Opus: triage** the review's proposals. Accepted ones update GAME_VISION and the specs first, then become slices.
3. **Claude Opus: 13-0 → 13-1**, the critical path, which resumes after triage (or in parallel, if the owner prefers).

---

## Session Log (compact)

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
