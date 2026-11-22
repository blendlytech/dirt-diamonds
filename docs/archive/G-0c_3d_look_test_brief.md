> **Archived 2026-11-19 (W1).** Closed brief; kept for history.

# Task Brief: G-0c — 3D look test for the arcade field

**Recommended model:** Claude Sonnet 5.5 (UI and feature engineer; owns the presentation runtime)
**Reviewer:** GPT 6 Astra Pro
**Owner gate:** the owner plays it and answers three questions: **look direction A, B or C?** ([proposal](visual_direction_3d_proposal.md) §1); **which camera preset?**; and **is hitting fun?** (this also serves as the G-0 feel gate).
**Spec:** [visual_direction_3d_proposal.md](visual_direction_3d_proposal.md) (PROPOSED, not canonical). The physics and its seam are unchanged: [arcade_baseball_overhaul.md](arcade_baseball_overhaul.md) §2–§3, §5.1, and the G-0 [brief](G-0_arcade_feel_prototype_brief.md) §9–§12.
**Branch / worktree:** `pG/G-0c-sonnet` from the current `origin/main`, in your own worktree (TEAM.md §3).
**Environment (required):** this is visual work, so it **must run where Godot 4.7 Mono and .NET 8 run and you can see rendered frames**, which means the owner's PC. Use screenshots or captures to judge your own output. The cloud workspace cannot build Godot C#: NuGet is blocked, and G-0b was written blind there. If you can't build and run the scene, **stop before writing code** and say so.

## 1. Context

The G-0 prototype works and passed review, and the arcade physics core is merged. After playing it, the owner asked whether the game can look like RBI Baseball 14, a 3D broadcast-camera baseball game, rather than the 16-bit plan. The integration lead recommends **stylized 3D** (option B), but only if a cheap test convinces the owner first.

This task is that test: the **same physics, the same session driver, the same controls**, rendered in Godot 3D with an RBI-style camera, a lit field and placeholder animated players. It is a throwaway look test, not production art and not G-5. It informs one owner decision, and the decision may still be "stay 16-bit".

## 2. Hard constraints

**Allowed paths (everything else is read-only):**

- `Assets/UI/Arcade/**`: new files for the 3D scene. The existing 2D scene's behaviour must not change.
  - You may make **small, additive, read-only** additions to `PrototypeSession` (for example an exposed swing start or release time) if the 3D view needs them. Log each one.
- `Assets/Graphics/Arcade/Placeholder3D/**`: new. Imported **CC0** assets only, plus `LICENSES.md` recording each asset's name, author, source URL, licence and date downloaded.
- `Assets/Core/GameManager.cs`: **one sanctioned change.** Extend the prototype launch detection so that the new 3D scene also skips the career boot. Turn the single `ArcadePrototypeSceneSuffix` into a small list holding both scene paths. Change nothing else in that file.
- This brief and `docs/progress.md`.

**Rules:**

- **Nothing under `Assets/Simulation/`.** The arcade core is called only through `PrototypeSession`, which calls the `ArcadeAtBat` seam. All outcomes come from the physics; animation and rendering never feed back into it.
- **No `project.godot` changes**, including the renderer. Test renderers with the command-line flag instead (§5).
- **No AI-generated assets** in this test. Use only CC0 assets and things built in code, so the art licence record stays clean.
- **No DB and no career.** Launching the 3D scene must print the GameManager "career boot skipped" line and create no save, exactly like the 2D prototype.
- **UI rules:** the HUD, readout, tally and tuning panel reuse the Theme family with no per-scene overrides. Every control is focusable. Keyboard and pad work as in the 2D scene. Node references are cached in `_Ready`, and `GetNode` paths are verified against the `.tscn`.
- **Reduce-motion** gates hit-stop, shake and flashes, as in the 2D scene.
- **Coordinates:** use **one** helper for the field frame to Godot. The field frame is metres with +X toward first base, +Y toward centre field and +Z up. In Godot 3D that becomes `x = fieldX`, `y = fieldZ`, `z = -fieldY`. Check it by eye: seen from behind the plate, first base is on the **right**.

## 3. Inputs

- `Assets/UI/Arcade/PrototypeSession.cs` (pure C# driver) and `PrototypeSettings.cs`. Reuse both. The 3D view reads exactly what the 2D view reads:
  - `Phase`, `WindupProgress`, `BallPosition`/`BallVisible`, `SwingProgress` (1.0 = contact time);
  - `CursorX/Z`, `CursorHalfWidthM/HeightM`, `CursorLocked`, `PowerSwingActive`;
  - `ContactPoint`, `HasLanding`/`LandingPosition`, `InHitView`, `HitStopActive`, `ShakeActive`;
  - `Park`, `Readout`, `Tally`.
- `ArcadePrototype.cs` and `ArcadeFieldView.cs`: the 2D reference for input registration, the HUD, the tuning panel and the effects. Copy or extract their HUD and tuning code as you see fit, as long as the 2D scene keeps working identically.
- `ArcadeTuning.SwingDurationSec(power)`, `PrototypeSession.WindupSec`, and the field geometry constants (plate, zone, contact plane).
- **Placeholder characters:** a CC0 rigged humanoid such as the Quaternius Universal Animation Library (CC0, one humanoid rig, 120+ clips, tested in Godot), or another CC0 source.
  - These libraries have **no baseball motions**. Use their idle and locomotion clips, and **key the swing and the pitch delivery yourself** in Godot (`AnimationPlayer` on the same skeleton). Rough is fine.
  - If you can't download assets from your environment, ask the owner to download a named file and place it in `Placeholder3D/`. Never fetch anything that needs an account or a login.
- The reference the owner gave: an RBI Baseball 14 screenshot. The camera is behind and above the catcher, the whole infield is visible, the batter is offset to the side, the mound sits mid-frame, and the fielders are visible. Use it for **camera framing only**; no assets or look-alike branding.

## 4. Outputs

**Scene:** `Assets/UI/Arcade/ArcadePrototype3D.tscn`, with its scripts (e.g. `ArcadePrototype3D.cs` for the root and HUD, `ArcadeFieldView3D.cs` for the 3D world).

1. **Field:**
   - lit 3D grass with mow stripes, done with a shader or textures;
   - infield dirt, mound, plate, batter's boxes, chalk lines and foul poles;
   - an outfield wall at `Park.FenceDistanceM`;
   - simple stands with a cheap crowd (billboards or noise texture);
   - a procedural sky and a sun with shadows;
   - a **day/night toggle** in the tuning panel (night = light towers plus spotlights).
2. **Players:**
   - a pitcher and a batter (required), plus a catcher and an umpire (static is fine);
   - one base model, with **uniform colours as material or shader parameters**. Two team colour sets switchable in the panel prove the "one model, 68 teams" idea.
3. **Animation timing contract**, the core technical proof:
   - the swing clip has a marked contact frame, and playback is scaled so that frame lands exactly when `SwingProgress` reaches 1.0;
   - the pitch delivery's release frame lands at the end of the windup (`WindupProgress` = 1.0);
   - the bat is a mesh on a `BoneAttachment3D` and is cosmetic only;
   - Document the contract in a short comment block or a doc section: clip names, markers, how playback is scaled.
4. **Ball:** a sphere with a shadow (a blob or a real shadow), a trail on hard-hit balls, the contact flash, and the cursor drawn as a ring or reticle in the zone plane.
5. **Cameras:**
   - **Broadcast** (the RBI framing; default);
   - **Low behind batter** (like the 2D scene);
   - **Free tuning:** height, distance, field of view and look-at height, as live sliders with a readout of the current values, so the owner can tell us the numbers;
   - **hit cam**: on contact it rises and smoothly follows the ball to its landing, shows a landing marker, then returns to the pitch camera.
6. **HUD:** the same readout, tally, tuning panel and controls as the 2D scene, plus:
   - camera preset and sliders, day/night and the team colour set;
   - a **render-scale** slider (`Viewport.Scaling3DScale`);
   - an **FPS counter** and the **active renderer name** on screen.

## 5. Tests and gates

| Gate | Command / action | Expected |
| :-- | :-- | :-- |
| Game build | `dotnet build DirtAndDiamonds.sln -c Debug` | 0 warnings, 0 errors |
| Arcade harness (untouched) | `dotnet run --project Tools/ArcadeHarness -c Release` | 55/55; hash unchanged on the same machine |
| Isolation | `git diff --stat origin/main -- Assets/Simulation Assets/Narrative project.godot` | Empty |
| 3D boot, no DB | F6 on `ArcadePrototype3D.tscn`, and the CLI with its `res://` path, in a disposable user dir | "career boot skipped" line; no `dirt_and_diamonds.db` created during play or after quitting |
| 2D prototype unchanged | F6 on `ArcadePrototype.tscn` | Same behaviour and HUD as before; no DB |
| Normal game unaffected | Plain launch, disposable user dir | Career boots as before |
| Full loop | Play it | Windup → pitch → swing → hit cam to landing → readout → next pitch; tally updates; keyboard and pad |
| Animation sync | On-screen check plus a log line | The swing contact frame lands within one rendered frame of `SwingProgress` = 1.0, and the release frame at `WindupProgress` = 1.0, at 1× and with hit-stop |
| **Performance on the owner's laptop** (Intel UHD 605) | Run fullscreen at 1366×768 three times: default renderer, `--rendering-method mobile`, `--rendering-method gl_compatibility` | Report average FPS at render scale 1.0 and at the lowest scale that still looks acceptable, day and night. **Target: 60 fps on at least one renderer.** If none reaches it, report that plainly: it's decision input, not a failure |

## 6. Definition of done

- [ ] `ArcadePrototype3D.tscn` runs standalone from F6 and the CLI with no career DB, using the extended GameManager detection.
- [ ] Broadcast, low and free cameras work, along with the hit cam, day/night, team colours, render scale and the FPS/renderer readout.
- [ ] The swing and pitch animations are marker-synced to the physics, and the contract is documented.
- [ ] HUD, readout, tally and tuning work on keyboard and pad. The 2D scene is unchanged.
- [ ] Build clean; ArcadeHarness 55/55; isolation empty; `LICENSES.md` lists every imported asset (all CC0).
- [ ] FPS measured on the owner's laptop for all three renderers, and recorded in the handoff.
- [ ] Screenshots of each camera preset, day and night, saved outside the repo and referenced in the handoff.
- [ ] The owner has played it and answered the three gate questions (left open for the owner).

**When every item except the owner's is checked, stop and hand off.**

## 7. Out of scope

- Production art, a commissioned character, bought mocap, AI-generated assets.
- Fielders that move, runners, throws, any rules or game flow beyond one pitch and its batted ball.
- Per-team stadiums or the venue catalog; character customization.
- Any change to physics, tuning numbers, `project.godot`, the renderer setting, GAME_VISION or the specs. If the owner picks B, Claude Opus updates the vision and specs afterwards.
- G-1, G-2, G-5 and production versions of any of this.

## 8. Handoff (required: end your session with this, addressed to the human)

```text
Done: <what was built>  · Commit: <sha>  · Gates: <name → result, ...>
Open: <anything unfinished, risks, owner questions>
Recommended next: G-0c review by GPT 6 Astra Pro, then the owner look gate (A/B/C, camera preset, hitting fun); if B is chosen, the GAME_VISION/spec update by Claude Opus 5.5
```

Include the FPS table (renderer × render scale × day/night) and the screenshot paths. Then add one compact line to [progress.md](../progress.md). Don't start the next slice.
