"""Print the batter's stance geometry: foot positions, hip facing, and the yaw
(in render_anim.py's convention) of the pitcher direction and catcher camera."""
import math
import sys

import bpy

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=sys.argv[1])
arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
scene = bpy.context.scene
for f in [int(x) for x in sys.argv[2].split(",")]:
    scene.frame_set(f)
    bpy.context.view_layer.update()
    mw = arm.matrix_world
    pb = {b.name.split(":")[-1]: b for b in arm.pose.bones}
    lf = mw @ pb["LeftFoot"].head
    rf = mw @ pb["RightFoot"].head
    hips = pb["Hips"]
    # Hips bone: local Z is the facing direction for Mixamo rigs.
    fwd = (mw.to_quaternion() @ hips.matrix.to_quaternion()) @ __import__("mathutils").Vector((0, 0, 1))
    lh = mw @ pb["LeftHand"].head
    print(f"frame {f}: LeftFoot {tuple(round(v,2) for v in lf)} RightFoot {tuple(round(v,2) for v in rf)} "
          f"hipsFwd {tuple(round(v,2) for v in fwd)} LeftHand {tuple(round(v,2) for v in lh)}")
    d = lf - rf
    # Camera yaw convention: camera at (sin y, -cos y) * dist, looking back at the origin.
    ang_l = math.degrees(math.atan2(d.x, -d.y))
    print(f"   back-foot->front-foot direction yaw {ang_l:.0f} deg (camera placed in this direction looks from the front foot side)")
