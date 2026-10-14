# Task Brief: G-0 — Arcade feel prototype (batter vs pitcher)

G-0 runs as **two sequential halves** with one owner gate at the end:

| Half | What | Recommended model | Branch / worktree |
| :-- | :-- | :-- | :-- |
| **G-0a** | Physics core: pitch flight, bat-ball contact, batted-ball flight, plus `Tools/ArcadeHarness` prototype suites | **Claude Opus 5.5** | `pG/G-0a-opus` in `..\dnd-opus` |
| **G-0b** | Playable standalone Godot scene over the G-0a core: placeholder 16-bit sprites, controls, landing readout, live tuning panel | **Claude Sonnet 5.5** | `pG/G-0b-sonnet` in `..\dnd-sonnet`, branched from the reviewed G-0a |

**Reviewer:** GPT 6 Astra Pro (both halves; owner 2026-10-13, Gemini 3.1 Pro unavailable). G-0a also had a Claude Opus stand-in review on 2026-10-13, and its one bug is fixed (`c916426`). **Owner gate:** after G-0b: **"hitting is fun."** Until the owner says yes, iterate inside G-0; nothing downstream (G-2+) builds on this.
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
- **Every number is a first-pass proposal** in one tuning class (`ArcadeTuning`), with a comment saying so. GPT 6 Astra Pro (Math) owns the final values in G-1/G-3.
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

- [ ] Every file in §4 exists under `Assets/Simulation/Arcade/` with no Godot, Data or Life reference.
- [ ] `ArcadeHarness` builds standalone and every suite passes.
- [ ] The isolation diff is empty.
- [ ] The branch is pushed, and the handoff names G-0b.

**G-0b:**

- [ ] The scene runs standalone from the debug entry point.
- [ ] Hitting, the readout, the tuning panel and the tally all work on keyboard and on a pad.
- [ ] The isolation diff is empty, and the game build is clean.
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

- **G-0a** hands off to **G-0b by Claude Sonnet 5.5** (reviewer GPT 6 Astra Pro). GPT 6 Astra Pro's independent review of G-0a can run in parallel.
- **G-0b** hands off to **the owner** for the "hitting is fun" gate.

Then add one compact line to [progress.md](../progress.md).
