# SpriteBake — prototype scripts (2026-10-16/17, Claude Opus 5.5)

Throwaway-quality scripts that proved the 16-bit pipeline for the owner's look test: a Rodin model plus Mixamo baseball clips, rendered in headless Blender, pixelized into sprites and composed into a 320×180 pitch-view mock. They are **not** production tooling. The G-0d brief turns them into `Tools/SpriteBake` proper (parameterized, tested, run on the owner's PC).

Requirements: Blender 5.2 as a Python module (`pip install bpy`) or `blender -b -P`; Pillow, NumPy, pygltflib.

| Script | What it does |
| :-- | :-- |
| `render_dirs.py` | Renders a static GLB from 8 directions (orthographic, lit or flat) |
| `render_anim.py` | Renders a Mixamo FBX clip at a camera yaw/elevation, optionally with a bat on the right hand; writes frames + `track.json` (hand, barrel and feet screen positions per frame). Needs `DD_TEXTURE` = the Rodin colour texture |
| `pixelize.py` | Box-downsample, shared median-cut palette, selective outline, preview sheet |
| `compose_mock.py` | The pitch-view mock: field, pitcher, perspective ball with late break, batter, contact sync, hit-stop |
| `field_bake.py` | Procedural regulation field from park dimensions (physics frame), pitch-view and field-map renders + `camera.json` |
| `stance_axis.py` | Measures a batting clip's stance (feet, hips) to find the correct behind-the-plate camera yaw |
| `inspect_fbx.py` | Lists bones, frames and textures of FBX clips |

Findings worth keeping:
- **Batting camera:** from behind the plate, the Mixamo swing's camera yaw is **198°** (measured from the feet, not guessed). The swing is right-handed; left-handed batters are the mirror.
- **Contact frame:** the barrel is furthest over the plate at clip frame **≈33** (index 16 at step 2). Load to contact takes about 0.13 s, close to the physics' 0.150 s swing.
- **Pitch release:** clip frame **≈91**. Front-view yaw is **0°**.
- **Bat:** parent it to `mixamorig:RightHand` along the bone's local **+X**, grip 7 cm along the bone. Mixamo FBX armatures are scaled 0.01, so set the world matrix directly.
- **Colours:** Mixamo FBX round-trips can lose the Rodin texture, so load it from the original GLB.
