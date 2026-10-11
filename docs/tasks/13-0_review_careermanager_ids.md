# 13-0 Review: deterministic `CareerManager` ids (Claude Opus → Gemini 3.1 Pro)

**Status:** **merged to `main` 2026-10-10** (owner approved; 13-0 closed, see [Merge record](#merge-record-2026-10-10)). Re-review of `c151a01`: approve with conditions C1/C2, both met at merge. See [Re-review](#re-review-of-c151a01-2026-10-10) at the end. Original review: changes requested, 2026-10-10. **Slice:** 13-0 (MC flake). **Reviewer:** Claude Opus 5.5 (TEAM.md §2). **Binding rules:** GEMINI.md §1–§9.

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

---

## Re-review of `c151a01` (2026-10-10)

**Reviewer:** Claude Opus 5.5. **Commit:** `c151a01` on `p13/13-0-gemini` (Gemini 3.1 Pro), based on `b044caf`, which is 10 commits behind `main`. **Gates run in:** a scratch detached worktree `..\dnd-opus-130` at `c151a01`, not Gemini's worktree.

### Verdict: approve the code with two conditions; 13-0 isn't closed until the MCP repair lands

The id fix is correct, and the harness is now deterministic. One required item was done differently than I asked: the drift-bound floor was widened. The evidence below shows that the widening is needed and doesn't weaken what the spec asks the check to prove, so I accept it on condition that the justification goes into the code. The sqlite-MCP half of 13-0 (league_restructure §15) is **not done**.

### Requirement by requirement

| # | Requirement | Result | Evidence |
| :-- | :-- | :-- | :-- |
| a | Ids come from `_rng`: 16 bytes from two `NextUInt64`, `0xDD` tag kept, `_idCounter` gone | **Pass** | `CareerManager.NextGuid()` mirrors `LeagueGenerator.NextGuid(ref rng)` and sets `bytes[15] = 0xDD`. It's used for the avatar, both household parents and heirs. There's no `_idCounter`; the counter fix was never committed. No `Guid.NewGuid()` remains anywhere under `Assets/`. Logged ids end in `…dd`. |
| b | Reload uniqueness: new `CareerManager` on the same db, then `ConceiveChild`, and the insert succeeds | **Pass** (meets the brief as written) | `RunCareerIdsSuite`: `cReload` on `db1` → `LoadExistingAvatar()` → `ConceiveChild`. It asserts the new id differs from the avatar and the first heir, and that `TryGetById` finds the row. A PK collision would throw and crash the harness, so a pass proves the insert. `CareerManager` isn't `IDisposable`, so dropping the instance is the right reading of "dispose". Notes: the reload uses a different seed (`MicroSeed + 999`), which models the game's per-boot reseed. The test therefore can't fail by construction, and the real guarantee rests on the boot seed (see F3). The `bool` returned by `LoadExistingAvatar()` is ignored. |
| c | Same-seed determinism: identical avatar/heir ids and slot numbers | **Pass** | Two in-process worlds give an identical avatar (`8ebbd1b1-…-02dd`), heir (`8a1b8761-…-dddd`) and slot. Cross-process determinism is proven by the 20-run diff. |
| d | Lineage drift-bound check relabeled `gen2 in 40–73` | **Changed beyond the ask: relabeled *and* the floor widened 40 → 39** | See below. |
| e | "Harness stabilization": what exactly changed | **Three things, none touching a guard** | See below. |

### (d) The drift-bound floor, 40 → 39

My review said "don't widen its band." Gemini widened it, and the commit and log justify it only as "for the new deterministic RNG output." Here's what I verified:

- With deterministic ids, gen 2 lands at **exactly 39 in 20/20 runs** (`bat_power 90 → 39 → 36`). That's the old failing value, so a `>= 40` floor would now fail every run. Reseeding to dodge it would be worse.
- The floor isn't a spec number. heir_mechanics §8 F5 and check 9 require that the line **regresses** (child below the founder) with **no upward ratchet**, near the mean after 3 generations. F5's analytic `90 → 60 → 52.5` is the lottery-off *potential*. The harness checks the *current* rating, with the lottery on and after development and aging, so the floor is only a "didn't collapse" sanity bound picked from observed runs (41 and 39 were both seen when ids were random).
- At 39, gen 2 sits 51 below the founder and 11 below the mean, and gen 3 at 36 is within ±20 of the mean. Every property the spec asks for still holds.

**Accepted, on condition (C1):** a comment at the check states the above: the floor is a collapse guard, not a calibration value, and 39 is the pinned deterministic gen-2 value. Without that, a later reader can't tell this widening from a tolerance quietly loosened to make a run pass.

### (e) What the "harness stabilization" changed

1. **Failed-check recap on the harness tail.** This is the §15 13-0 deliverable. It only prints and changes no outcome. **Fine.**
2. **Bridge snapshot race (`scripted UI drives a full interactive game`).** The check is `snapshots >= HumanPitchesSeen`, so extra counting would loosen it. I looked at the bridge to see whether this could double count. `PlayerIntentBridge.TryGetSnapshot` is **consuming**: it returns true once per published snapshot and clears `_snapshotDirty`. `BeginAwait` sets the snapshot and `_awaiting` atomically under `_gate`. The old loop had a real race: a `TryGetSnapshot` miss, then the sim publishes and awaits, then the UI sees `IsAwaitingIntent` and submits, and the next `BeginAwait` overwrites the snapshot before anyone reads it. The new read inside the awaiting branch closes that race. Because reads consume, the count can't exceed the number of publishes, so this is a real fix, not a loosening. Evidence: **16 snapshots == 16 pitches in all 20 runs**. The trailing post-`Join` read is redundant (with the inner read, no snapshot can be left unread) but harmless. Follow-up F1: tighten to `==`.
3. **Drift floor:** covered in (d).

No guard was re-baselined, and no other expectation or tolerance changed. The check count went 352 → 354, the two new Career-ids checks; nothing was removed.

### Gates (scratch worktree at `c151a01`)

| Gate | Result |
| :-- | :-- |
| `dotnet build DirtAndDiamonds.sln -c Debug` | **0 warnings, 0 errors** |
| MonteCarloHarness ×20 (Release) | **20/20 exit 0, 354/354 each** |
| Byte-identity across the 20 logs | **All 20 identical** after masking only `<n> ms` (3 lines: the 730-day run, career season and 3-generation timings). One md5 for all 20. |
| Resolver guard / season-pipeline guard | **Unchanged:** `PA 48384/48384 H 10969/10969 ER 5237/5237`, 20/20 |

### The sqlite MCP: not repaired (13-0 is still open)

- **On `main`, `.mcp.json` points `mcp-sqlite` at the live save** (`…/app_userdata/Dirt & Diamonds/dirt_and_diamonds.db`). That breaks GEMINI.md §4 ("never the live save") on the shared branch right now. The live file is 0 bytes, so nothing has been harmed yet.
- Gemini's **uncommitted** edit in `..\dnd-gemini` repoints it at `c:/Users/DELL/dirt_and_diamonds/scratch.db`. That's the right direction, since it moves the MCP off the live save. But the file is **0 bytes**, so it has no schema and can't validate anything. It also sits in the shared checkout where the auto-committer runs (it's covered by `*.db` in `.gitignore`, so it won't be committed). In this session the `sqlite` MCP also failed to connect (timeout).
- **Condition C2 (closes 13-0):** commit an `.mcp.json` that targets a scratch DB outside every worktree and is built from `SchemaDefinitions.sql`, plus a one-line way to rebuild it. Until that lands, `Tools/SchemaValidator` (scratch pass) is the GEMINI.md §4 validation path; its skill doc says so.

### Follow-ups (non-blocking)

- **F1:** tighten the snapshot check to `snapshots == HumanPitchesSeen`. That's stronger, and it now holds exactly.
- **F2:** `progress.md` will conflict on rebase. Gemini's entry was written into the pre-restructure layout; move it into the current Session Log.
- **F3, a correction to my own review:** the boot stream isn't wall-clock seeded. `GameManager.cs:268` seeds from `Environment.TickCount64` (ms since OS start). A reload replays career ids only if two boots land on the same tick, which is negligible but not zero (e.g. a launcher that starts at login). 13-1 rewrites the boot (clean break), so mix `DateTime.UtcNow.Ticks` into that seed there.
- **F4:** `.agents/skills/validate_sqlite_schema/SKILL.md` gives the script path as `.claude/skills/…`, but the skill lives in `.agents/skills/`.

### Merge plan (on the owner's go-ahead only)

Rebase `c151a01` onto current `main`. Main's 10 newer commits are docs plus comment-path edits; the only overlap is `progress.md` and `Program.cs` comments. Then add C1 (the comment) and, if the owner wants it, F1 and C2. Re-run the build and 20× MC with the byte-identity diff, and fast-forward `main`. No push unless the owner says so.

### Merge record (2026-10-10)

The owner approved the merge, F1 and C2. On branch `p13/13-0-merge` off `main`:

- `57be806` is Gemini's `c151a01`, cherry-picked (authorship kept). The `progress.md` conflict was resolved by moving Gemini's entry into the current Session Log (F2).
- `dc185ee` is this review.
- `1de2ca3` adds C1, F1, C2 and F4:
  - the drift-floor rationale comment;
  - the snapshot check tightened to `==`;
  - `.mcp.json` pointed at `C:/Users/DELL/dnd-scratch/schema_scratch.db`;
  - `SchemaValidator --emit <path>`, which builds that db from `SchemaDefinitions.sql` through the real `DatabaseManager` and refuses `app_userdata` paths;
  - the skill doc's script path fixed and the scratch db documented.

Gates on the merge branch:

- Build: 0 warnings, 0 errors.
- SchemaValidator scratch pass: 111/111.
- `--emit` builds a v13 db (24 tables, `integrity_check` ok) and refuses a path under the save directory (exit 2). The live save is untouched.
- MC: 20/20 × 354/354, byte-identical apart from 3 `ms` lines. The only line that differs from pre-rebase `c151a01` is the snapshot check's new label.
- Guard: PA 48384 / H 10969 / ER 5237.

F3 (mixing the wall clock into the boot seed) moves to 13-1.
