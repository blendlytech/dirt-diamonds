# 13-1 Review Request: schema v14, 3 tiers, Appendix A world-gen (Claude Opus → Gemini 3.1 Pro)

**Status:** ready for review, 2026-10-11. **Slice:** 13-1 ([league_restructure.md](../design/league_restructure.md) §15). **Author:** Claude Opus 5.5. **Reviewer:** Gemini 3.1 Pro (TEAM.md §2: independent review of Opus's sim diffs). **Binding:** GEMINI.md §1–§11.
**Branch:** `p13/13-1-opus` (commit `d0c0777`, rebased on `p13/integration` = `beb1843`). **Merges into:** `p13/integration`, **not** `main` (owner decision, spec §15). **Owner gate before merge:** team names (Appendix A) and interpretation I6.

## How to review

Work in your own worktree (TEAM.md §3), e.g. `git worktree add ..\dnd-gemini-131 p13/13-1-opus`. Don't build in `..\dnd-opus`. Review `git diff beb1843..p13/13-1-opus` against the spec sections named below, rerun the gates, and append your verdict (approve / changes requested, with evidence) to the end of this file.

## What changed, and what to check

| Area | Change | Spec | Check |
| :-- | :-- | :-- | :-- |
| DDL | v14: `Conferences`, `Divisions`; `Teams` + `conference_id` (NOT NULL FK), `division_id` (FK, NULL for HS), `home_park` (NOT NULL); `Team_Tiers.tier` CHECK 0–2 and the v6→v7 tier-5 backfill removed; `Playoff_Series`, `Schedule_Games` (+ `ix_sched_day`), `Team_Seasons`, `Roster_Designations`, `College_Enrollment`, `Recruiting_Offers`, `Draft_Picks`; `user_version = 14` | §12 | STRICT everywhere; FKs on team and player columns (stricter than §12's sketch, deliberately); CHECKs sensible; script idempotent |
| Enum | `LeagueTier { HS = 0, College = 1, Pro = 2 }`; `LeagueDirectory.TierCount = 3`; `TierEffects` 3 rows (−20 / −16 / 0); `COALESCE(tt.tier, 2)` | §2.1, §11.1 | Pro = 2 keeps every shipped `tier >= 2` content gate; no other numeric tier assumption left |
| Structure | `Assets/Data/Database/LeagueStructure.cs`: 6 conferences, 14 divisions, 68 seeds; ids `(tier+1)*100+n`, conferences `(tier+1)*10+n`, divisions `conf*10+n` | §2.2, §2.3, App. A | Matches Appendix A team by team (names, *new* flags, placement) |
| World-gen | `LeagueGenerator.GenerateWorld`: structure + 68 teams + rosters (HS 3–4 SP / 2–4 RP / 11–14 pos; College and Pro 4 / 5 / 15), ages HS 15–18, College 18–22, Pro 21–36, arsenals | §5.1, §5.3, **I6** | Deterministic; one batch; no rng outside `ref rng` |
| Legacy ladder | `GenerateIfEmpty` / `EnsureV4` / `EnsureTierLeagues` kept as the **calibration fixture** (8 teams × 9+5+3 per tier, frozen draw order, ids 1–8 for Pro), now schema-valid v14 via Appendix A abbreviations. The three minor tiers are gone | §15, §11.3 | HS/College draw first, so their rosters are byte-identical to before |
| Clean break | `SaveCompatibility.Prepare` (Data layer) + `DatabaseManager.PeekSchemaVersion` (read-only, unpooled): missing/0-byte/no-schema → new; v14 → load; v1–13 → moved to `name.vN.bak` (then `.vN.1.bak`, …; never overwritten; `-wal`/`-shm` follow); > v14 → refused, untouched | §14, GEMINI §4, R-1 D20 (DatabaseManager owns connections) | Ordering: main file first, sidecars after; a failed sidecar move throws before boot opens a new save |
| Boot | `GameManager`: clean-break check before the DB opens; `GenerateWorld` replaces the three legacy calls; seed mixes `DateTime.UtcNow.Ticks` into `TickCount64` (13-0 review F3; R-1 Q02's persisted streams are R1-SAVE-2) | §14 | `SaveCheck` / `LegacySaveBackupPath` exposed for a front-end notice (UI pending) |
| Steam | `#Tier_Pro` replaces the minor/MLB tokens; Went Pro at `tier >= Pro`; **The Show no longer fires** (no MLB rung) until 17a redefines it | steam §3 | Intentional deferral, not a regression |

## Integration state (expected, documented)

- **The game does not run a season on this branch.** `LeagueSimulator.Initialize` still requires 8 teams × 9+5+3 per tier and **refuses the 68-team world with a clear exception**. A harness check asserts this. 13-2 (roster capacities) and 13-3 (schedules) lift it; `p13/integration` reaches `main` only after 13-3 (spec §15).
- **Not in this slice:** the front-end "older save" dialog (Sonnet), real `home_park` names (placeholder `"{City} Ballpark"`, G-4/Flash), Steam README tokens (13-7), the `Schedule_Games` writer (13-3) and the other new tables' writers (13-4…13-6).

## Harness changes: every expectation that moved, and why

- **9a tier ladder:** `TierBands` cut to three rows (HS, College and the MLB row as Pro), verbatim per §11.2. Labels say 3-tier / 24 teams. **Season seeds:** the suite derived each tier's seed as `SeasonSeed + tier`; Pro's ordinal dropped 5 → 2, which silently moved the guard season. `TierSeedOffset` pins Pro to the retired MLB offset 5, so the **season-pipeline guard is unchanged: PA 48384 / H 10969 / ER 5237.** Please confirm this is a seed-plumbing fix, not a re-baseline.
- **9c/9d (PromotionManager, retired in 13-6):** paths and counts generalized to 3 tiers (relegation rungs = Pro − College; `[TierCount − 1]` instead of `[5]`; 24 teams / 408 rostered). Numbers in these suites change because the ladder is shorter; no tolerance was loosened.
- **Check count 354 → 361:** −6 removed (the three minor tiers' two band checks each), +13 added (8 world-gen v14, 5 save-compat).
- **GrittyEvents:** ad-hoc fixture teams get a conference and a park; two poll checks now expect tier **2** (Pro) where they expected 5 (MLB), per §2.1.
- **SchemaValidator / CoreLoop:** v13 → v14 version checks; SchemaValidator also requires the 9 new tables, `ix_sched_day` and the Teams v14 column types.
- **MC unhandled-exception check** now prints the stack trace (diagnostics only).

## Gates (author's run on `d0c0777`)

See the 13-1 entry in [progress.md](../progress.md) for the final numbers. The bar: build 0 warnings / 0 errors; SchemaValidator all green; MC full suite 20/20 green and **byte-identical apart from `ms`**; guard PA 48384 / H 10969 / ER 5237; GrittyEvents, CoreLoop, NeedsDecay, Hustle and Holdem all green.

## Questions for the reviewer

1. Is keeping the legacy 8-team ladder as the calibration fixture until 13-3 the right seam, or should 13-2 move the calibration world to Pro-shaped rosters first?
2. FKs from the new tables to `Teams` are stricter than §12's sketch. Any objection?
3. `LeagueStructure` lives in the Data layer, so harnesses that only compile `Assets/Data/Database` can build valid teams. Acceptable?
