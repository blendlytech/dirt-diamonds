# Task Brief: G-0 — Arcade feel prototype (batter vs pitcher)

G-0 runs as **two sequential halves** with one owner gate at the end:

| Half | What | Recommended model | Branch / worktree |
| :-- | :-- | :-- | :-- |
| **G-0a** | Physics core: pitch flight, bat-ball contact, batted-ball flight, plus `Tools/ArcadeHarness` prototype suites | **Claude Opus 5.5** | `pG/G-0a-opus` in `..\dnd-opus` |
| **G-0b** | Playable standalone Godot scene over the G-0a core: placeholder 16-bit sprites, controls, landing readout, live tuning panel | **Claude Sonnet 5.5** | `pG/G-0b-sonnet` in `..\dnd-sonnet`, branched from the reviewed G-0a |

**Reviewer:** Gemini 3.1 Pro (both halves). **Owner gate:** after G-0b: **"hitting is fun."** Until the owner says yes, iterate inside G-0; nothing downstream (G-2+) builds on this.
**Spec:** [arcade_baseball_overhaul.md](../design/arcade_baseball_overhaul.md) §2 (architecture), §3.1–§3.3 (physics), §4.1 (ratings mapping), §5.1 (batting controls), §10.1–§10.2 (views), §12 (G-0 row).
**Binding:** GEMINI.md §1–§11.

## 1. Context

The owner chose true arcade physics for every game the avatar's team plays (GAME_VISION v2.1 §3 pillar 2, §5). Everything else in Phase G is engineering. G-0 answers the one question no spec can: **does it feel good to hit?** It's deliberately tiny:

- one batter (the human) against one pitcher (a simple AI);
- no fielders, no runners, no DB, no league, no career.

The pitch comes in, you swing, and the ball flies. You see where it lands, how hard you hit it, and at what angle.

## 2. Hard constraints

**G-0a may only touch:**

- `Assets/Simulation/Arcade/**`, new;
- `Tools/ArcadeHarness/**`, new;
- `DirtAndDiamonds.sln`, to add the harness project;
- this brief and `docs/progress.md`.

**G-0b may only touch:**

- `Assets/UI/Arcade/**`, new;
- `Assets/Graphics/Arcade/Placeholder/**`, new, if it needs any image at all (prefer drawing in code);
- one entry point to open the prototype scene: a debug-only button or launch argument, never in the shipped career flow;
- this brief and `docs/progress.md`.

**Rules for both halves:**

- **Engine independence:** nothing under `Assets/Simulation/Arcade/` may reference Godot, the Data layer, the Life sim or `MicroGame`. The only exception is `RngState`, the established pure-PRNG exception used by `HoldemHarness`. If a Godot or Data type leaks in, `ArcadeHarness` stops building, and that is the check.
- **Determinism:** a fixed step, contact solved at the exact plane crossing, and every random draw from a caller-owned `RngState`. The same seed + the same inputs must give a bit-identical result.
- **Zero allocation** in the per-pitch and per-step paths. Trajectories go into caller-provided buffers.
- **Inert:** no file under `Assets/Simulation/Baseball/`, `Life/`, `Narrative/` or `Hustles/` changes. MC and Gritty harnesses are untouched by construction (`git diff --stat` is the evidence).
- **Every number is a first-pass proposal** in one tuning class (`ArcadeTuning`), with a comment saying so. Gemini Pro Math owns the final values in G-1/G-3.
- UI rules (G-0b): read-only over the sim, `GetNode` paths verified with `godot_scene_mapper`, and node references cached in `_Ready`.

## 3. Inputs

- The spec sections above.
- `Assets/Simulation/Baseball/RngState.cs`, and the `PitchType` vocabulary (Fastball / Breaking / Offspeed), mirrored, not referenced, so Arcade stays standalone.
- **Reference physics:** air density ≈ 1.2 kg/m³; ball 0.145 kg, radius 3.66 cm; drag C_d ≈ 0.35; Magnus lift from the spin parameter S = rω/v.
- **Reference outcomes** (MLB Statcast shapes): a 95 mph fastball reaches the plate in ≈ 0.42 s; a 100 mph / 28° fly carries ≈ 380–430 ft; a 110 mph / 25° drive carries ≈ 430–480 ft.

## 4. Outputs

**G-0a: the physics core** (`Assets/Simulation/Arcade/`)

| File | Content |
| :-- | :-- |
| `Vec3.cs` | Small double-precision vector struct |
| `ArcadeTuning.cs` | Every constant and first-pass ratings mapping (§4.1), marked *proposal* |
| `BallPhysics.cs` | RK4 integrator: gravity + drag + Magnus. A plane-crossing solver, a ground/fence detector, and trajectory fill into a caller span |
| `PitchModel.cs` | Pitch types with speed/spin profiles. An aim solver that finds the release velocity hitting a target at the plate. Control scatter and fatigue, plus bounded aftertouch |
| `ContactModel.cs` | Swing input (start time, cursor point, power-swing flag) + the ball state at the contact plane → whiff, foul tip, or batted ball (exit velocity, launch angle, spray, spin). Timing → spray; vertical offset → launch angle; distance from the sweet spot → collision efficiency |
| `BattedBallModel.cs` | Flight to first landing or the fence; a default park; HR / fly / liner / grounder / pop-up / foul classification; carry and hang time |
| `ArcadeAtBat.cs` | A tiny driver for one pitch: AI pitch choice and aim, then pitch flight, the human swing, and the result. It exposes everything a scene needs to animate |

**G-0a: `Tools/ArcadeHarness`** (prototype suites; G-2 adds rules suites, G-3 adds calibration):

1. Pitch timing and break versus the reference shapes.
2. Aim-solver accuracy (zero scatter → within 1 mm).
3. Batted-ball carry versus the reference shapes.
4. Contact behavior: centered and on time gives peak exit velocity; off-center drops it; power is monotonic; early pulls and late goes opposite field; outside the timing window or the cursor is a whiff.
5. Determinism (bit-identical replays) and zero allocations on the hot paths.

**G-0b: the scene** (`Assets/UI/Arcade/ArcadePrototype.tscn` + `.cs`)

- A 320×180 SubViewport, integer-scaled with nearest filtering.
- The pitch view (behind the plate), with the ball and its shadow.
- Batting: the contact cursor on the left stick / WASD, swing on A / Space, power swing on RT / Shift.
- On contact, a simple field view that follows the ball to its landing.
- A readout: exit velocity, launch angle, spray, distance, result.
- A live **tuning panel** (debug only): batter power/contact, pitcher stuff/control, pitch type, difficulty multipliers. The owner can then turn the knobs while judging feel.
- A session tally: swings, hits, home runs, whiffs, average exit velocity.

## 5. Tests and gates

| Gate | Command | Expected |
| :-- | :-- | :-- |
| Arcade harness | `dotnet run --project Tools/ArcadeHarness -c Release` | All suites pass; exit code 0 |
| Isolation | `git diff --stat origin/main -- Assets/Simulation/Baseball Assets/Simulation/Life Assets/Narrative Assets/Simulation/Hustles` | Empty |
| Game build (G-0b, and G-0a if the toolchain is available) | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings, 0 errors |
| Live boot (G-0b) | Open the prototype scene in Godot | Clean console; pitch → swing → flight → readout loops |

## 6. Definition of done

**G-0a:**

- [x] Every file in §4 exists under `Assets/Simulation/Arcade/` with no Godot, Data or Life reference.
- [x] `ArcadeHarness` builds standalone and every suite passes.
- [x] The isolation diff is empty.
- [x] The branch is pushed, and the handoff names G-0b.

**G-0b:**

- [ ] The scene runs standalone from the debug entry point.
- [ ] Hitting, the readout, the tuning panel and the tally all work on keyboard and on a pad.
- [x] The isolation diff is empty, and the game build is clean.
- [ ] The owner has played it.

**When your half's boxes are all checked, stop.**

## 7. Out of scope

- Fielders, runners, the rules engine, counts beyond a single pitch, AI batters, and the human pitching.
- Any DB read or write, career integration, `EffectiveRatings`, the booth, cutscenes.
- Real art, audio, venue data.
- Calibration to league bands: that's G-3.

Don't start G-1 or G-2.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built>  · Commit: <sha>  · Gates: <name → result, ...>
Open: <anything unfinished, risks, owner questions>
Recommended next: <slice> by <model> (reviewer: <model>)
```

- **G-0a** hands off to **G-0b by Claude Sonnet 5.5** (reviewer Gemini Pro). G-0a's review by Gemini Pro can run in parallel.
- **G-0b** hands off to **the owner** for the "hitting is fun" gate.

Then add one compact line to [progress.md](../progress.md).

## 9. Independent G-0a/G-0b review — 2026-10-14, GPT 6 Astra Pro

Reviewed `c916426` (physics) and `4f731dc` (scene) on `pG/G-0-review-astra`. The owner's attached review instructions authorize this role; the later roster update `afcf751`, TEAM §1a, also assigns former Gemini independent-review duties to GPT. Neither reviewed branch contains that later roster update. **Verdict: changes requested; not approved for integration.** Fix commit: `4a24681`. No physics source, tuning magnitude, macro/life/narrative/hustle code, or tracked project configuration changed.

### Findings (severity first)

1. **P1 — debug prototype opens and writes the career save. Reported, not fixed.** `Assets/UI/Main.cs:60` switches scenes only after the autoload runs `Assets/Core/GameManager.cs:230` (database open) and `:239` (schema application). Direct scene launch has the same autoload. Startup loads/seeds the league and exit persists the clock, violating the no-DB/no-career boundary. Trying the prototype in the ordinary user directory therefore touches the career DB. All review runs used a disposable custom user directory; the live career save was not used. Repairing the earlier autoload requires an integration-lead bootstrap change outside the allowed UI hook.
2. **P2 — a bounce after the contact plane vetoes valid contact. Reported, not fixed.** `Assets/Simulation/Arcade/ArcadeAtBat.cs:128` replaces successful contact-plane reachability with the plate-plane result; `:132` derives `Bounced` from that later result, and `:145` forces every swing to whiff. A deterministic probe (seed 3, average Pro fastballs aimed low) reached the contact plane at z=0.054692 m, above the 0.0366 m ground-contact radius, then bounced before the plate. An exactly centered/on-time swing still became `SwingingStrike`. Preserve contact-plane reachability separately from strike classification.
3. **P2 — ground wins even when the plane crossing happens first inside the step. Reported, not fixed.** `Assets/Simulation/Arcade/BallPhysics.cs:133` tests ground before plane without comparing crossing fractions. Reproduction: position (0, 0.61, 0.04), velocity (0, -40, -1), zero spin/steer, plane y=0.60. The solver returns false at ground contact t=0.003334, y=0.476723, although the plane was crossed at t=0.000250 with z=0.039745, still above ground. Compare the within-step event times. This is separate from finding 2.
4. **P2 — pad users could not activate tuning controls. Fixed in `4a24681`.** `Assets/UI/Arcade/ArcadePrototype.cs:185` now ensures pad accept/cancel/navigation bindings while preserving existing mappings. Previously `ui_accept` contained only Enter/keypad Enter/Space; simulated A could not change any of the four selectors or two toggles, or activate buttons. Keyboard and simulated pad control checks now pass; all 20 controls are reachable. Physical hardware remains untested.
5. **P2 — canvas stretch broke integer pixel scaling. Fixed in `4a24681`.** `Assets/UI/Arcade/ArcadePrototype.cs:221` calculates integer scale in physical pixels and compensates for project canvas stretch. `Assets/UI/Arcade/ArcadePrototype.tscn:25` permits independent field positioning/scaling. Previously 1366x768 stretched the nominal 5x field through a 1600x900 canvas, yielding approximately 4.26875x/4.26667x physical scaling. The field now remains 320x180 at an integer physical scale, centered with spare space. Window-resize signals cover unchanged logical root size.
6. **P3 — proposal labeling is incomplete. Reported, not fixed.** `Assets/Simulation/Arcade/ArcadeTuning.cs:30` distinguishes textbook/rulebook constants from proposals. In particular `:47` and `:48` do not label the selected drag/lift assumptions as proposals. Gameplay mappings are labeled, but the instruction that every tuning number is a first-pass proposal is not literally met. Clarify the distinction without changing magnitudes during this review.

### Verification and limits

| Gate | Result |
| :-- | :-- |
| ArcadeHarness Release | **51/51, exit 0**. 95 mph pitch: 0.401 s; 100 mph/28 degrees: 417 ft; 110 mph/25 degrees: 460 ft. Seeded replay passes; 500 hot-path pitches/swings/flights allocate 0 bytes. Not rerun after UI-only edits. |
| Full solution Debug build | **0 warnings, 0 errors**, including final UI fixes, .NET SDK 8.0.422. No compile errors were present in the author's scene. |
| Isolation command in §5 | **Empty**. No macro, Life, Narrative or Hustles changes. Arcade C# source unchanged; only generated script identifiers added there. |
| Engine/source review | Pure C# core, only RngState dependency; fixed step 1/240; interpolated intra-step contact crossing; caller-owned buffers. The bounce findings qualify the crossing review. |
| Scene mapper/style/RNG | All 10 cached GetNode paths match; no per-scene theme overrides; own arcade RNG and separate renderer System.Random cosmetics. HUD strings update on changes, consistent with the dirty-HUD rule. |
| Godot boot and scripted interaction | **Godot 4.7 Mono, 59 assertions pass, exit 0, no error output.** Debug hook, viewport, physical integer scaling/fit at 1280x720, 1280x800, 1920x1080, 1920x1200 and 1366x768; keyboard/pad focus traversal; all selectors/sliders/toggles change; reseed/reset actions, panel shortcuts, keyboard swing/tally. Controller events were synthesized. |
| Rendered layout | OpenGL compatibility on Intel UHD 605. Pitch captures at 1280x720 and 1280x800 and the 1920x1080 tuning capture were visually inspected; a 1920x1080 pitch capture was also saved. No engine errors. Expected informational Steam-unavailable diagnostic: this checkout lacks steam_api64. |
| Session cycle probe | Seeded centered input enters Hit at 102.73 mph, with one swing/InPlay tally, then Result and a new Windup. This verifies the pure driver, not human timing/feel. |
| Still unverified | Physical controller, owner playtest, and visually inspected complete hit-to-landing cycle. |

The four G-0a §6 boxes are factual delivery checks and are checked; they do **not** override the change-request verdict. G-0b's build/isolation box is checked. Standalone stays open because successful boot still has the DB side effect; complete keyboard/pad gameplay stays open for hardware/full-loop validation; owner-played stays open.

Generated identifiers for the G-0 core, scene scripts and harness are committed. Unrelated import churn was reverted. A local **untracked `override.cfg`** redirects this review checkout's `user://` to `DirtAndDiamonds-G0-review-astra`, a disposable directory. Keep it for a safe prototype playtest; it is not a shipping fix for finding 1. Test programs, logs and rendered captures are outside the repository in the chat's visualization directory.

Done: independent review, two UI fixes, build/boot/input/layout checks. Commit: `4a24681` (fixes), followed by this review record. Open: findings 1-3 and 6, physical pad/full visual cycle, owner feel gate. No merge. Recommended next: **G-0 repairs by Claude Opus 5.5 (reviewer: GPT 6 Astra Pro)**, then the **owner's "hitting is fun" playtest**, with **Claude Sonnet 5.5** tuning from feedback (reviewer: GPT 6 Astra Pro; Opus merge gate). Do not start G-1/G-2.

## 10. G-0 repairs — 2026-10-14, Claude Opus 5.5

Branch `pG/G-0-repairs-opus` (from `pG/G-0-review-astra` @ `e198328`). Addresses §9 findings 1, 2, 3 and 6; findings 4 and 5 were fixed in review (`4a24681`). **Awaiting GPT 6 Astra Pro re-review.**

1. **Finding 1 (P1), fixed: the prototype no longer touches the career save.** The `--arcade-proto` hook in `Assets/UI/Main.cs` is removed (Main.tscn loads its screens before any redirect, so it could never be DB-free). The entry point is now the prototype **as the run scene**: F6 in the editor, or `godot --path . res://Assets/UI/Arcade/ArcadePrototype.tscn`. `GameManager.IsArcadePrototypeLaunch()` (`Assets/Core/GameManager.cs`, integration-lead change outside G-0b's paths) recognizes that launch target (`res://` or `uid://` argument) and skips the whole career boot: no Steam, no save opened, no schema, no league seed, no bus; `_Process` off and every `_ExitTree` flush null-guarded, so quitting writes nothing. `ArcadePrototype._Ready` raises an error if a career save is ever open beside it. Still written by Godot itself: `user://logs/` and UiSfx's settings file (not the career DB).
2. **Finding 2 (P2), fixed:** `PitchFlight` separates hittability from the call. `Bounced` = touched down before the contact plane (unhittable); new `BouncedBeforePlate` = touched down before the plate (never a strike). A pitch that bounces between the two is hittable and, taken, a ball. The scene's "in the dirt" text follows the plate; its whiff reason follows hittability.
3. **Finding 3 (P2), fixed:** `BallPhysics.FlyToPlane` compares the within-step crossing fractions when ground and plane fall in one step; the earlier event wins, a tie goes to the plane.
4. **Finding 6 (P3), fixed (comments + one data move, no magnitude changed):** `ArcadeTuning` now separates FACTS (gravity, ball, plate, conversions) from PROPOSAL model assumptions (air density, drag, lift curve, zone heights, fixed step) and PROPOSAL gameplay mappings. The Sawicki lift coefficients moved from `BallPhysics.LiftCoefficient` into `ArcadeTuning`; bit-identical (session hash unchanged across the move).

| Gate | Result |
| :-- | :-- |
| ArcadeHarness Release | **55/55, exit 0** (51 + 4 new regressions for findings 2 and 3; the new checks fail on `e198328`). 95 mph 0.401 s; 100 mph/28° 417 ft; 110 mph/25° 460 ft. Determinism session hash `2603B56FB5D4B6E5` → `A88CC92A2AF45A6F`: an expected behavior change (low pitches that now reach contact / the plane), stable across reruns and the lift-constant move. .NET SDK 8.0.131. |
| Isolation (§5) | **Empty.** |
| Full solution Debug build | **Not run**: the game project needs Godot.NET.Sdk from NuGet, blocked in this workspace. Changed Godot-side files parse cleanly (tree-sitter C#). |
| Live boot | **Not run** (no Godot here). Re-review should confirm: F6 launch prints the "career boot skipped" line, creates no `dirt_and_diamonds.db` in a fresh `user://`, and the full loop runs. |

