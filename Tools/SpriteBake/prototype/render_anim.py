"""Render a Mixamo FBX clip as supersampled frames for sprite pixelization.

Usage (positional):
  fbx out_dir yaw_deg elev_deg frame_step size ortho_scale [bat_axis] [frame_list]
bat_axis: none | x | -x | z | -z  (bat parented to the right hand, along that local axis)
frame_list: comma-separated explicit frames (overrides frame_step)
"""
import math
import sys

import os

import bpy
from mathutils import Matrix, Vector

TEXTURE = os.environ["DD_TEXTURE"]

fbx, out_dir = sys.argv[1], sys.argv[2]
yaw, elev = float(sys.argv[3]), float(sys.argv[4])
step, size, ortho = int(sys.argv[5]), int(sys.argv[6]), float(sys.argv[7])
bat_axis = sys.argv[8] if len(sys.argv) > 8 else "none"
frame_list = [int(f) for f in sys.argv[9].split(",")] if len(sys.argv) > 9 and sys.argv[9] else None

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
meshes = [o for o in scene.objects if o.type == "MESH"]
img = bpy.data.images.load(TEXTURE)

# Lit diffuse material from the Rodin colour texture (same look as the still test).
mat = bpy.data.materials.new("Player")
mat.use_nodes = True
nt = mat.node_tree
nt.nodes.clear()
tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = img
dif = nt.nodes.new("ShaderNodeBsdfDiffuse")
out = nt.nodes.new("ShaderNodeOutputMaterial")
nt.links.new(tex.outputs["Color"], dif.inputs["Color"])
nt.links.new(dif.outputs[0], out.inputs["Surface"])
for m in meshes:
    m.data.materials.clear()
    m.data.materials.append(mat)


def solid(name, rgb):
    mm = bpy.data.materials.new(name)
    mm.use_nodes = True
    b = mm.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = 0.8
    return mm


hand = next(b for b in arm.pose.bones if b.name.endswith("RightHand"))
act = arm.animation_data.action
if bat_axis != "none":
    # 33-inch bat built along +Z: knob 6 cm below the grip, barrel above.
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.013, radius2=0.034, depth=0.84)
    bat = bpy.context.active_object
    bat.data.materials.append(solid("Bat", (0.62, 0.42, 0.22)))
    for v in bat.data.vertices:
        v.co.z += 0.42 - 0.06
    scene.frame_set(int(act.frame_range[0]))
    bpy.context.view_layer.update()
    mw = arm.matrix_world
    head, tail = mw @ hand.head, mw @ hand.tail
    ydir = (tail - head).normalized()
    brot = (mw.to_quaternion() @ hand.matrix.to_quaternion()).to_matrix()
    axis = {"x": brot.col[0], "-x": -brot.col[0], "z": brot.col[2], "-z": -brot.col[2]}[bat_axis].normalized()
    grip = head + ydir * 0.07
    # Orthonormal frame with +Z along the bat axis.
    zx = axis
    xx = ydir.cross(zx).normalized()
    yx = zx.cross(xx).normalized()
    world = Matrix((xx, yx, zx)).transposed().to_4x4()
    world.translation = grip
    bat.parent = arm
    bat.parent_type = "BONE"
    bat.parent_bone = hand.name
    bpy.context.view_layer.update()
    bat.matrix_world = world

f0, f1 = (int(x) for x in act.frame_range)

scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 16
scene.cycles.use_denoising = False
scene.render.film_transparent = True
scene.render.resolution_x = size
scene.render.resolution_y = size
scene.view_settings.view_transform = "Standard"
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
world = bpy.data.worlds.new("W")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (1, 1, 1, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55

# Camera orbits the origin at yaw; the key light follows the camera (upper left).
cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = ortho
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
e, y = math.radians(elev), math.radians(yaw)
dist, target_z = 12.0, ortho * 0.42
cam.location = (dist * math.cos(e) * math.sin(y), -dist * math.cos(e) * math.cos(y), target_z + dist * math.sin(e))
cam.rotation_euler = (math.radians(90) - e, 0, y)
sun = bpy.data.lights.new("Sun", "SUN")
sun.energy = 2.6
so = bpy.data.objects.new("Sun", sun)
scene.collection.objects.link(so)
so.rotation_euler = (math.radians(50), 0, y + math.radians(-35))

import json

from bpy_extras.object_utils import world_to_camera_view

frames = frame_list or list(range(f0, f1 + 1, step))
track = []
for i, f in enumerate(frames):
    scene.frame_set(f)
    bpy.context.view_layer.update()
    hp = world_to_camera_view(scene, cam, arm.matrix_world @ hand.tail)
    rec = {"i": i, "frame": f, "hand_x": hp.x, "hand_y": 1.0 - hp.y}
    if bat_axis != "none":
        bp = world_to_camera_view(scene, cam, bat.matrix_world @ Vector((0, 0, 0.62)))
        rec["barrel_x"], rec["barrel_y"] = bp.x, 1.0 - bp.y
    lf = world_to_camera_view(scene, cam, arm.matrix_world @ next(b for b in arm.pose.bones if b.name.endswith("LeftFoot")).head)
    rf = world_to_camera_view(scene, cam, arm.matrix_world @ next(b for b in arm.pose.bones if b.name.endswith("RightFoot")).head)
    rec["feet_x"], rec["feet_y"] = (lf.x + rf.x) / 2, 1.0 - min(lf.y, rf.y)
    track.append(rec)
    scene.render.filepath = f"{out_dir}/f{i:03d}.png"
    bpy.ops.render.render(write_still=True)
with open(f"{out_dir}/track.json", "w") as fh:
    json.dump(track, fh)
print("rendered", len(frames), "frames of", f0, f1, "hand bone", hand.name)
