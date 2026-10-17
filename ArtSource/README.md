# ArtSource — raw 3D art for the 16-bit sprite pipeline

Source files that get **rendered into 16-bit sprites and backgrounds** by `Tools/SpriteBake`. They are never shipped and never loaded by the game. The `.gdignore` file here keeps Godot from importing them. The game only ships the rendered, pixelized PNG sheets.

**Status:** started 2026-10-17 for the 16-bit look work, owned by the owner and Claude Opus 5.5. Everything here is an input; none of it is canonical game data.

## Pipeline

1. **Model:** Rodin (hyper3d.ai), Gen-2.5.
   - Characters: Text→Image→3D, Medium effort, **T/A pose on**, Quad mesh **8000**, Shaded material, Base Model, **glb**.
   - Stadium pieces: same, but T/A pose off; Quad **18000** for structures with many repeated parts (seats, railings).
   - Rules: no logos, letters or numbers; no real people, teams or ballparks.
2. **Rig:** Mixamo auto-rigger, Standard skeleton (65), symmetry on.
   - Download the **rigged body once**, in T-pose with no animation: FBX, With Skin.
3. **Animate:** download each Mixamo clip **once**, on any one rigged character, preferably **Without Skin** (smaller). All Mixamo rigs share bone names (`mixamorig:*`), so SpriteBake retargets every clip onto every body.
4. **Bake:** `Tools/SpriteBake` renders each body × clip × camera angle, pixelizes it (shared palette, sel-out outline) and writes sprite sheets plus timing data (contact and release frames).
   - Skin tone, hair colour, uniform and shirt colours are **palette swaps at bake or render time**, not separate models.
5. **Field:** the playing surface is generated from the park dimensions (`Tools/SpriteBake/prototype/field_bake.py`), not modelled, so it matches the physics exactly. Rodin supplies only the stadium kit pieces around it.

## Naming

`DD_<Kind>_<Subject>_<Variant>_v<NN>.<ext>`, PascalCase parts, no spaces.

| Kind | Pattern | Examples |
| :-- | :-- | :-- |
| Player body | `DD_Player_<M|F>_<Build>_vNN` | `DD_Player_M_Athletic_v01`, `DD_Player_M_Stocky_v01`, `DD_Player_M_Lean_v01`, `DD_Player_F_Athletic_v01` |
| Fan body | `DD_Fan_<Type>_<A-F>_vNN` (`_Geo` = shape only, before Rodin's Material step) | `DD_Fan_Man_A_v01`, `DD_Fan_Woman_C_v01`, `DD_Fan_Elder_B_v01`, `DD_Fan_Kid_D_v01` |
| Animation | `DD_Anim_<Role>_<Action>_vNN` | `DD_Anim_Bat_Swing_v01`, `DD_Anim_Fan_Cheer_v01` |
| Stadium piece | `DD_Kit_<Pro|College|HS>_<Piece>_vNN` | `DD_Kit_Pro_Grandstand_v01`, `DD_Kit_HS_Bleachers_v01` |
| Prop | `DD_Prop_<Item>_vNN` | `DD_Prop_Bat_v01`, `DD_Prop_Helmet_v01` |

A rigged body keeps its body name with `_Rig`: `DD_Fan_Man_A_Rig_v01.fbx`. A new version of anything bumps `vNN`; the old file stays until nothing uses it.

## Folders

```
models/players/   player bodies (Rodin glb + Mixamo-rigged fbx)
models/fans/      crowd bodies
models/stadium/   pro/ college/ hs/ kit pieces
models/props/     bat, helmet, gloves, bases...
anims/baseball/   Mixamo baseball clips
anims/fans/       Mixamo crowd clips (sit, clap, cheer, disappointed)
```

## Manifest

Every file gets a row. The **AI** column feeds the Steam AI-content disclosure (Phase 17). Licences are as understood on the date added; re-check before release.

| File | What it is | Source | AI-generated | Licence / terms | Added |
| :-- | :-- | :-- | :-- | :-- | :-- |
| models/players/DD_Player_M_Athletic_v01.glb | Base male player, athletic, T-pose, red/grey/navy test uniform | Rodin Gen-2.5 (owner's account, Creator trial), 0.5 credit | **Yes** | Rodin Creator plan lists "any use"; **owner to confirm** in account terms | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_Swing_v01.fbx | Batting swing (Mixamo "Baseball Hit"), 60 f @ 30 fps, right-handed, contact ≈ f31–33 | Mixamo, rigged on DD_Player_M_Athletic, with skin | No (mocap library) | Adobe Mixamo: use in games OK; no redistribution of raw files | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_Bunt_v01.fbx | Bunt (Mixamo "Baseball Bunt") | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_StanceIdle_v01.fbx | Batting-stance waggle (Mixamo "Baseball Idle"), 46 f | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_StepOutIdle_v01.fbx | Batter steps out of the box between pitches (Mixamo "Baseball Milling Idle") | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_StepIn_v01.fbx | Batter walks into the box (Mixamo "Baseball Step In") | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_StepUpToBat_v01.fbx | Steps up to bat (Mixamo "Baseball Step Up To Bat") | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_OnDeck_v01.fbx | On-deck circle routine (Mixamo "Batter On Deck"), 1121 f | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_WalkOut_v01.fbx | Walks away from the plate (Mixamo "Baseball Walk Out") | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Bat_StrikeoutReaction_v01.fbx | Batter's strikeout reaction: stance, then turns away (Mixamo "Baseball Strike"). **Not** an umpire call | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Pitch_Delivery_v01.fbx | Full pitching delivery (Mixamo "Baseball Pitching"), 157 f, release ≈ f91 | Mixamo | No | as above | 2026-10-16 |
| anims/baseball/DD_Anim_Catcher_CrouchCatchThrow_v01.fbx | Catcher crouch → pop-up catch → throw → re-crouch (Mixamo "Baseball Catcher"), 276 f | Mixamo | No | as above | 2026-10-16 |

| models/fans/DD_Fan_Elder_A_Geo_v01.glb | Older man, newsboy cap, cardigan. Rodin source name `crowd_oldman`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Elder_B_Geo_v01.glb | Older woman, open cardigan/jacket. Rodin source name `crowd_oldwoman`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Elder_C_Geo_v01.glb | Older man, bucket hat, big belly, shorts. Rodin source name `crowd_oldman2`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Elder_D_Geo_v01.glb | Older woman, hair in bun, jacket, arms close. Rodin source name `crowd_oldwoman2`. Status: **regenerate**: arms against body | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Kid_A_Geo_v01.glb | Boy ~8, t-shirt and shorts. Rodin source name `crowd_kid`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Kid_B_Geo_v01.glb | Girl ~10, pigtails, overall shorts. Rodin source name `crowd_kidg`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Kid_C_Geo_v01.glb | Teen boy, hoodie, headphones. Rodin source name `crowd_boy`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Kid_D_Geo_v01.glb | Teen, hoodie, headphones, arms against body. Rodin source name `crowd_grl10`. Status: **regenerate**: arms against body | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Man_A_Geo_v01.glb | Man, 30s, zip hoodie (hood down), jeans. Rodin source name `crowd_30s`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Man_B_Geo_v01.glb | Man, 40s, short hair, t-shirt. Rodin source name `crowd_40s`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Man_C_Geo_v01.glb | Man, 20s, slim, short hair, t-shirt. Rodin source name `crowd_20s`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Man_D_Geo_v01.glb | Man, 20s, tousled hair, joggers. Rodin source name `crowd_20s2`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Woman_A_Geo_v01.glb | Woman, high ponytail, fitted top. Rodin source name `crowd_fml1`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Woman_B_Geo_v01.glb | Woman, black hair in ponytail. Rodin source name `crowd_blk_pt`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Woman_C_Geo_v01.glb | Woman, long hair, arms low (gap present). Rodin source name `crowd_fml2`. Status: geometry only; needs Material; arms low, check Mixamo rig | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Woman_D_Geo_v01.glb | Woman/young woman, short hair, collared jacket. Rodin source name `crowd_grl4`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |
| models/fans/DD_Fan_Woman_E_Geo_v01.glb | Woman/young woman, bob hair, jacket. Rodin source name `crowd_grl5`. Status: geometry only; needs Rodin Material step | Rodin Gen-2.5 (owner) | **Yes** | Rodin Creator plan, owner to confirm | 2026-10-17 |

**Still needed:** umpire calls (Mixamo has no baseball umpire clip, so look for "pointing"/"strike" gestures or hand-key them), fielding (ground ball, catch, throw), running and sliding, plus the fan clips.
