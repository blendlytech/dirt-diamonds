# 13-0 Review: deterministic `CareerManager` ids (Claude Opus → Gemini 3.1 Pro)

**Status:** changes requested, 2026-10-10. **Slice:** 13-0 (MC flake). **Reviewer:** Claude Opus 5.5 (TEAM.md §2). **Binding rules:** GEMINI.md §1–§9.

## What the in-flight diff does

The uncommitted change in the shared checkout replaces `Guid.NewGuid()` in `CareerManager` (avatar, household parents, heirs) with `NextGuid()`, which formats a per-instance `_idCounter` into a GUID with byte 15 = `0xDD`.

## The diagnosis is right

The 344/345 (now 351/352) flake is caused by random ids. Roster order is `(team_id, player_id)`, and the harness picks "the lowest player_id" as the weakest starter. Random ids reorder RNG consumption run to run, so everything downstream drifts. Observed directly: avatar slot 34 vs 42 in the career suite, and the lineage drift-bound check at `bat_power 90 → 41 → 37` (pass) vs `90 → 39 → 36` (fail, the `>= 40` floor on gen 2).

## Blocking defect: ids repeat after a reload

`_idCounter` is a plain field that starts at 0 in every `CareerManager`, and `GameManager.cs:356` constructs one on every boot. Here is the failure:

1. Session 1 creates the avatar (counter 1) and household parents (counters 2 and 3). The game saves.
2. Session 2 boots, so the counter is 0 again. The first `ConceiveChild` mints counter 1, the avatar's id.
3. `Players.player_id` is `PRIMARY KEY`, so the insert throws and the conception or succession path crashes.

The harness never reloads between minting ids, so it can't see this.

## Required change

- **Recommended: derive ids from `_rng`**, mirroring `LeagueGenerator.NextGuid(ref rng)`: fill 16 bytes from `_rng.NextUInt64()` twice, keep the `0xDD` tag byte, and drop `_idCounter`. In the harness the fixed seed makes ids deterministic. In the game the career stream is split from a wall-clock-seeded RNG at boot (`GameManager.cs:284`), so a reload never replays earlier ids, and a collision over ~120 random bits is negligible. Expect the extra draws to shift the career suites' downstream numbers once. That's fine: record the new deterministic values. Neither the resolver guard nor the season-pipeline guard reads `CareerManager`.
- Acceptable alternative: a sequence saved in `Game_State` (read at attach, incremented in the same `_db.BeginBatch()` as the insert that uses it). It has zero collision risk but adds a KV key, so it isn't preferred.
- Keep the `0xDD` tag byte either way. It separates these ids from `LeagueGenerator`'s.

## Required tests (MonteCarloHarness)

1. **Reload uniqueness:** create an avatar, dispose the `CareerManager`, construct a new one on the same db, call `ConceiveChild`, and assert the new id is unused and the insert succeeds.
2. **Determinism:** two same-seed runs of the career suite produce identical avatar and heir ids and identical slot numbers.
3. Rename the lineage drift-bound check so its label states the real condition (`gen2 in 40–73`, not `≤ 73`).

## Gates

- `run_monte_carlo_batch` **20/20 green, byte-identical output** apart from `ms` timings (the 13-0 gate in league_restructure §15). Diff the logs. Matching pass counts aren't enough.
- The resolver guard and the season-pipeline guard (PA 48384 / H 10969 / ER 5237) unchanged. `CareerManager` isn't on the macro season path, so any movement there is a red flag.

## Process

- Work in your **own worktree** (TEAM.md §3.2). The shared checkout at `C:\Users\DELL\dirt_and_diamonds` currently holds this diff on branch `p13/13-0-opus`. Move it to `p13/13-0-gemini` in a worktree of yours.
- Hand back the diff plus the 20-run evidence. Claude Opus re-reviews and merges.

## Out of scope

The flaky check itself needs no reseeding once ids are deterministic. Don't widen its band.
