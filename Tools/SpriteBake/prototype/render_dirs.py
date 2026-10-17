"""Prototype sprite bake: render a GLB character from 8 directions with an
orthographic camera, supersampled, for later pixelization.

Usage: python3 render_dirs.py <model.glb> <out_dir> <mode: flat|lit> [supersample_px]
"""
import math
import sys

import bpy

model, out_dir, mode = sys.argv[1], sys.argv[2], sys.argv[3]
size = int(sys.argv[4]) if len(sys.argv) > 4 else 512

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=model)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
root = bpy.data.objects.new("Root", None)
bpy.context.scene.collection.objects.link(root)
for o in bpy.context.scene.objects:
    if o.parent is None and o is not root:
        o.parent = root

# Material: Rodin "Shaded" = emissive texture, black base colour.
for o in meshes:
    for slot in o.material_slots:
        mat = slot.material
        nt = mat.node_tree
        tex = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
        tex.interpolation = "Closest" if mode == "flat" else "Linear"
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        for n in list(nt.nodes):
            if n not in (tex, out):
                nt.nodes.remove(n)
        if mode == "flat":
            sh = nt.nodes.new("ShaderNodeEmission")
            nt.links.new(tex.outputs["Color"], sh.inputs["Color"])
        else:
            sh = nt.nodes.new("ShaderNodeBsdfDiffuse")
            nt.links.new(tex.outputs["Color"], sh.inputs["Color"])
        nt.links.new(sh.outputs[0], out.inputs["Surface"])

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 8 if mode == "flat" else 32
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
bg = world.node_tree.nodes["Background"]
bg.inputs["Color"].default_value = (1, 1, 1, 1)
bg.inputs["Strength"].default_value = 0.0 if mode == "flat" else 0.55

if mode == "lit":
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 2.6
    sun.angle = math.radians(20)
    so = bpy.data.objects.new("Sun", sun)
    scene.collection.objects.link(so)
    # Key light from the camera's upper left, fixed to the camera (so every
    # direction is lit the same way, like hand-drawn sprites).
    so.rotation_euler = (math.radians(50), 0, math.radians(-35))

cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 2.25
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
# Slightly elevated view (~20 deg down), looking +Y at the model's front (-Y).
elev = math.radians(20)
dist = 10.0
cam.location = (0, -dist * math.cos(elev), 0.95 + dist * math.sin(elev))
cam.rotation_euler = (math.radians(90) - elev, 0, 0)

names = ["S", "SE", "E", "NE", "N", "NW", "W", "SW"]
for i, name in enumerate(names):
    root.rotation_euler = (0, 0, math.radians(45 * i))
    scene.render.filepath = f"{out_dir}/{mode}_{i}_{name}.png"
    bpy.ops.render.render(write_still=True)
print("done")
