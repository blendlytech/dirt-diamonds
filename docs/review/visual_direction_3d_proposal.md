# Proposal: the arcade field goes from 16-bit to stylized 3D

**Status:** **PROPOSED, not canonical.** Nothing in `GAME_VISION.md` or the specs changes until the owner accepts this. The decision comes after the G-0c look test ([brief](../tasks/G-0c_3d_look_test_brief.md)).
**Author:** Claude Opus 5.5 (integration lead), 2026-10-15, at the owner's request.
**Trigger:** the owner played the G-0 prototype on 2026-10-14 and asked whether the game can look like RBI Baseball 14 (a 3D, broadcast-camera baseball game) instead of the current 16-bit direction.
**Affects:** GAME_VISION §3 pillar 2 and §5 game day; [arcade_baseball_overhaul.md](../design/arcade_baseball_overhaul.md) (the locked art row, §10, §13 Q6, §14, and the §12 G-5/G-8 rows); BUILD_PLAN Phase G; TEAM.md G-8; GEMINI.md §11; [gameplay_core.md](../design/gameplay_core.md) venues; [visual_redesign.md](../design/visual_redesign.md) §6.

---

## 1. The decision

**Recommended:** replace the 16-bit field with a **stylized 3D field**. That means real 3D perspective, lighting and grass, an RBI-style broadcast camera, and simplified stylized players rather than realistic ones. Adopt it **only if** the G-0c look test convinces the owner. Until then, the vision stays 16-bit.

| | **A. 16-bit pixel art** (current vision) | **B. Stylized 3D** (recommended) | **C. Realistic 3D** (RBI 14 as-is) |
| :-- | :-- | :-- | :-- |
| Look | SNES-era retro; the 1988 RBI framing | RBI 14's camera, field, lighting and depth, with simpler, chunkier players | Closest to the reference screenshot |
| Players | One sprite sheet, palette-swapped | One rigged base model, uniform colours by shader | Detailed models, likely per body type |
| Animation | Hand-drawn frames (a pixel artist, a pack or AI-assisted) | Stock humanoid library **plus a baseball-specific set** (bought mocap or custom-keyed) | Custom motion capture, cleaned |
| Ballparks (68 teams) | Tile kits | **One modular 3D kit per tier**, re-dressed per team | Detailed stadium models |
| Art cost and risk | Lowest | Medium: one character, one animation set and three kits | Highest: needs a dedicated 3D artist and animator |
| Low-end hardware (owner's Intel UHD 605 laptop) | Trivial | Feasible with care (§5) | Tight |
| Code effort | Planned already (G-5) | G-5 grows: a 3D camera, an animation sync layer, materials | As B, plus heavier optimization |

**Why B rather than C:** much of what makes RBI 14 look "real" comes from the **camera and the field**: a high angle behind the catcher, the whole infield in view, lit grass and dirt, real perspective. Code and a modest kit deliver those. Photoreal players are the expensive part, and without an art budget they're also the riskiest. B keeps that cost to one character, one animation set and three park kits.

**Why not stay A:** the owner's reaction to the placeholder is the signal. The question is which look the owner wants to sell on Steam. Code can't settle that, so G-0c puts a 3D test in the owner's hands before any money is spent.

## 2. What does NOT change

- **The physics, rules, determinism and calibration.** The arcade core already works in 3D (metres, field frame x/y/z). Godot only renders snapshots, so watched = skipped is untouched, and so is everything G-0a, G-0b and the reviews proved.
- **The UI direction (Phase 15 "Two Worlds").** The scorebug, lower-thirds, phone and every screen around the field are unaffected. In the recommended direction the field was always the one surface with its own style. A 3D field now sits inside the DIAMOND broadcast package, which arguably fits "prime-time sports broadcast" better than pixel art did.
- **Cutscenes, voice and the venue screens.** They stay 2D motion-comic and illustrated art.
- **Content lines and fiction rules.** No real teams, parks, players or likenesses. RBI 14 is a reference for camera and layout only, never its assets or trade dress. AI-made art stays pre-generated, curated and disclosed.

## 3. What changes if accepted (applied by Claude Opus after the owner's yes)

1. **GAME_VISION.md**, first, because a change of direction goes there before the specs:
   - §3 pillar 2: "a **real-time arcade ballgame with true physics**, in a 16-bit style (owner, 2026-10-13)" → "a **real-time arcade ballgame with true physics**, in a **stylized 3D broadcast style** (owner, <date>)".
   - §5 game day: "play the game on the 16-bit arcade field" → "play the game on the 3D arcade field".
2. **arcade_baseball_overhaul.md**:
   - the title and the locked "Art" row;
   - §10 rewritten, as sketched in §4 below;
   - §13 Q6 (art source) restated for 3D;
   - §14 gains the risk rows in §6 below;
   - the G-5 row grows (3D camera, animation sync) and the G-8 row becomes 3D art.
3. **The 16-bit wording in other docs:**
   - BUILD_PLAN Phase G (lines on "16-bit arcade ballgame" and "16-bit field");
   - GEMINI.md §11, the last bullet;
   - TEAM.md G-8;
   - gameplay_core venues;
   - visual_redesign §6, the ballpark kits row.
4. **The G-0 2D scene** stays as a debug reference until G-5 replaces both prototypes.

## 4. Sketch of the new arcade §10 (for review, not yet binding)

- **Rendering:** native-resolution 3D with a **render-scale setting** (Godot's 3D scaling), so low-end machines render the field below native while the UI stays sharp. Renderer choice (Forward+, Mobile or Compatibility) is decided from the G-0c measurements. It's a project-wide setting, so it's an integration-lead change, not part of the look test.
- **Views:**
  - Pitch view: a broadcast camera behind and above the catcher (the RBI 14 framing), with the batter offset to one side.
  - Hit view: the camera rises and follows the ball to its landing.
  - Camera presets are data, not code.
- **Team colours for free:** one base player model whose jersey, cap, trim and skin tone are shader parameters. This replaces the palette-swap sprite shader and does the same job.
- **Parks:** one modular kit per tier (HS chain-link and bleachers; a College ballpark; a Pro stadium with lights), re-dressed per venue from the catalog. Crowds are cheap billboards or impostors. Lighting is baked where possible.
- **Game feel:** unchanged list (hit-stop, shake, dust, chalk, ball trail, replay). The CRT filter becomes an optional "broadcast" post-process. Every effect still respects reduce-motion.
- **Animation contract**, which replaces the sprite-sheet contract:
  - one humanoid skeleton and named clips;
  - root motion off;
  - each action clip carries a **timing marker**: the swing's contact frame, the pitcher's release frame, the fielder's catch and throw frames;
  - the presentation layer scales playback so each marker lands on the time the physics already decided.
  - Animation never feeds back into the sim. The swing animation is timed to `SwingInput` and `ArcadeTuning.SwingDurationSec`; the bat mesh is cosmetic.

## 5. Art sourcing for B (owner decisions, quotes needed)

| Need | Options | Notes |
| :-- | :-- | :-- |
| Base player model | Commission one stylized rigged character; or start from a CC0 humanoid; or curated AI 3D generation plus rigging | One model serves 68 teams through shader colours |
| Generic animation (idle, run, walk, celebrate) | **CC0:** Quaternius Universal Animation Library (120+ clips, one humanoid rig, tested in Godot). Also Mixamo (commercial use allowed, standalone redistribution not) | Read each licence before shipping; record every source in the art licence log |
| **Baseball animation** (stance, swing, check swing, pitch delivery, catcher, fielding, throws, slides) | Paid sports mocap packs; commissioned keyframe animation; or keyed in-house as placeholders | **The biggest gap.** The CC0 libraries have no baseball motions, and Mixamo's sports coverage is thin. This is the line item to price first |
| Park kits (×3 tiers) | Asset-store modular stadium pieces plus team dressing; or commissioned | Dimensions come from the venue catalog |
| Ball, bat, gloves, bases, props | Simple; can be built in-house | — |

Anything AI-generated (models or textures) is **pre-generated, human-curated and disclosed** under Phase 17. This proposal adds no runtime AI.

**Hardware target to adopt:** 60 fps at 1366×768 on the owner's laptop class (Intel UHD 605), with 1280×720 and Steam Deck in mind. G-0c measures this. If stylized 3D can't hold it on any renderer at a reasonable render scale, that's a reason to stay with A.

## 6. Risks

| Risk | Mitigation |
| :-- | :-- |
| Baseball animations are expensive or hard to source | Price them first; G-0c proves the timing-marker sync with keyed placeholders; buy or commission only after the owner picks B |
| Animation and physics disagree (the bat visibly misses a ball the physics says was hit) | Marker-scaled playback, so the physics always leads; contact flash and hit-stop mask small offsets; IK for the final reach is a G-5 option |
| Low-end performance | Render scale, baked lighting, impostor crowds, LOD; measured in G-0c on the owner's laptop |
| Scope creep toward option C | Lock "stylized" in GAME_VISION; one base model; three kits, not 68 stadiums |
| 68 distinct parks in 3D | Kits plus dressing (unchanged from R-1 Q13); signature features are props, not bespoke models |
| Look-alike to a licensed real-league game | Camera and layout only; original models, uniforms, logos and parks; trademark screen as today |

## 7. Questions for the owner

1. After G-0c: **A, B or C?** If B, the vision changes (§3).
2. Which **camera preset** from G-0c becomes the default pitch view?
3. **Art budget:** are you willing to pay for a baseball animation set and one commissioned base character, or should we push curated AI and CC0 as far as they go and accept a rougher look?
4. Do you confirm the **hardware target**: 60 fps at 1366×768 on your laptop class?
