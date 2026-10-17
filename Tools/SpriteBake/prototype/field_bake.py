"""Procedural ballpark field at true dimensions, rendered for the 16-bit views.

Field frame = the arcade physics frame: origin at the back point of home plate,
+X toward first base, +Y toward centre field, +Z up, metres.

Usage: python3 field_bake.py <out_dir> [LL LC C RC RL WALL_FT]
Writes pitch_view_bg.png (perspective, behind the plate), field_map.png
(orthographic oblique map for the hit view) and camera.json (the projection
contract the game must use for sprites and the ball).
"""
import json
import math
import sys

import bpy
from mathutils import Vector

out = sys.argv[1]
dims = [float(x) for x in sys.argv[2:8]] if len(sys.argv) >= 8 else [330, 375, 400, 375, 330, 10]
FT = 0.3048
LL, LC, C, RC, RL, WALL = (d * FT for d in dims)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def mat(name, rgb, rough=0.9, stripes=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    if stripes is None:
        b.inputs["Base Color"].default_value = (*rgb, 1)
        return m
    # Mow stripes: alternating bands across the field (world Y), plus a
    # second, perpendicular set for a checker pattern in the outfield.
    rgb2, width = stripes
    coord = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(coord.outputs["Object"], sep.inputs[0])
    div = nt.nodes.new("ShaderNodeMath")
    div.operation = "DIVIDE"
    div.inputs[1].default_value = width
    nt.links.new(sep.outputs["Y"], div.inputs[0])
    fl = nt.nodes.new("ShaderNodeMath")
    fl.operation = "FLOOR"
    nt.links.new(div.outputs[0], fl.inputs[0])
    md = nt.nodes.new("ShaderNodeMath")
    md.operation = "MODULO"
    md.inputs[1].default_value = 2.0
    nt.links.new(fl.outputs[0], md.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs["A"].default_value = (*rgb, 1)
    mix.inputs["B"].default_value = (*rgb2, 1)
    nt.links.new(md.outputs[0], mix.inputs["Factor"])
    nt.links.new(mix.outputs["Result"], b.inputs["Base Color"])
    return m


GRASS = mat("Grass", (0.16, 0.40, 0.14), stripes=((0.12, 0.33, 0.11), 4.0))
DIRT = mat("Dirt", (0.55, 0.33, 0.18))
DIRT_L = mat("MoundDirt", (0.62, 0.40, 0.23))
CHALK = mat("Chalk", (0.93, 0.93, 0.90), rough=0.6)
WALLM = mat("Wall", (0.05, 0.20, 0.12))
YELLOW = mat("WallTop", (0.95, 0.80, 0.15))
STANDS = mat("Stands", (0.22, 0.21, 0.28))


def poly(name, pts, z, material):
    me = bpy.data.meshes.new(name)
    me.from_pydata([(x, y, z) for x, y in pts], [], [list(range(len(pts)))])
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    ob.data.materials.append(material)
    return ob


def circle(cx, cy, r, n=64):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def fence_r(spray_deg):
    a = max(-45.0, min(45.0, spray_deg))
    pts = [(-45, LL), (-22.5, LC), (0, C), (22.5, RC), (45, RL)]
    for (a0, r0), (a1, r1) in zip(pts, pts[1:]):
        if a0 <= a <= a1:
            return r0 + (r1 - r0) * (a - a0) / (a1 - a0)
    return C


def at(spray_deg, r):
    s = math.radians(spray_deg)
    return (r * math.sin(s), r * math.cos(s))


# Ground: a large grass apron (foul territory included).
poly("Ground", [(-160, -80), (160, -80), (160, 170), (-160, 170)], 0.0, GRASS)
# Warning track (15 ft) inside the fence, fair territory.
arc = [-45 + i for i in range(91)]
track_outer = [at(a, fence_r(a)) for a in arc]
track_inner = [at(a, fence_r(a) - 15 * FT) for a in reversed(arc)]
poly("Track", track_outer + track_inner, 0.004, DIRT)
# Infield dirt: 95 ft arc from the front of the rubber, joined to the foul lines.
rub = (0.0, 60.5 * FT)
arc_pts = []
for i in range(0, 361):
    ang = math.radians(i * 0.5)  # counter-clockwise from +X (first-base side) to -X
    x = rub[0] + 95 * FT * math.cos(ang)
    y = rub[1] + 95 * FT * math.sin(ang)
    if y >= abs(x):  # fair territory only
        arc_pts.append((x, y))
poly("InfieldDirt", [(0, -1.2)] + arc_pts, 0.006, DIRT)
# Infield grass: the diamond inset by the 3 ft base paths.
b1, b2, b3 = at(45, 27.43), (0.0, 38.80), at(-45, 27.43)
inset = 3 * FT
g_home = (0.0, 3.96 + 0.3)
poly("InfieldGrass", [g_home, (b1[0] - inset * 1.4, b1[1]), (0, b2[1] - inset * 1.4), (b3[0] + inset * 1.4, b3[1])], 0.008, GRASS)
# Home plate circle (13 ft radius) and mound (9 ft radius, slight dome).
poly("HomeCircle", circle(0.0, 0.3, 3.96), 0.010, DIRT)
bpy.ops.mesh.primitive_uv_sphere_add(radius=2.74, location=(0, 59 * FT, -2.74 + 0.25), segments=48, ring_count=24)
mound = bpy.context.active_object
mound.scale.z = 0.12
mound.location.z = 0.0
mound.data.materials.append(DIRT_L)
# Rubber, plate, bases.
poly("Rubber", [(-0.305, rub[1] - 0.076), (0.305, rub[1] - 0.076), (0.305, rub[1] + 0.076), (-0.305, rub[1] + 0.076)], 0.33, CHALK)
poly("Plate", [(-0.216, 0.4318), (0.216, 0.4318), (0.216, 0.216), (0.0, 0.0), (-0.216, 0.216)], 0.014, CHALK)
for bx, by in (b1, b2, b3):
    s = 0.19
    poly("Base", [(bx - s, by - s), (bx + s, by - s), (bx + s, by + s), (bx - s, by + s)], 0.05, CHALK)


def line(name, p0, p1, w=0.076, z=0.012):
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    L = math.hypot(dx, dy)
    nx, ny = -dy / L * w / 2, dx / L * w / 2
    poly(name, [(p0[0] + nx, p0[1] + ny), (p1[0] + nx, p1[1] + ny), (p1[0] - nx, p1[1] - ny), (p0[0] - nx, p0[1] - ny)], z, CHALK)


# Foul lines to the fence.
line("Line1B", (0.25, 0.25), at(45, RL))
line("Line3B", (-0.25, 0.25), at(-45, LL))
# Batter's boxes: 4 x 6 ft, 6 in from the plate, centred on the plate.
for side in (-1, 1):
    x0 = side * (0.216 + 0.152)
    x1 = side * (0.216 + 0.152 + 1.22)
    y0, y1 = 0.216 - 0.915, 0.216 + 0.915
    line("BoxA", (x0, y0), (x0, y1))
    line("BoxB", (x1, y0), (x1, y1))
    line("BoxC", (x0, y0), (x1, y0))
    line("BoxD", (x0, y1), (x1, y1))

# Outfield wall along the fence profile, with a yellow top.
verts, faces = [], []
for i, a in enumerate(arc):
    x, y = at(a, fence_r(a))
    verts += [(x, y, 0.0), (x, y, WALL)]
    if i:
        k = 2 * i
        faces.append([k - 2, k, k + 1, k - 1])
me = bpy.data.meshes.new("Wall")
me.from_pydata(verts, [], faces)
wall = bpy.data.objects.new("Wall", me)
scene.collection.objects.link(wall)
wall.data.materials.append(WALLM)
verts = []
faces = []
for i, a in enumerate(arc):
    x, y = at(a, fence_r(a) - 0.02)
    verts += [(x, y, WALL - 0.25), (x, y, WALL + 0.02)]
    if i:
        k = 2 * i
        faces.append([k - 2, k, k + 1, k - 1])
me = bpy.data.meshes.new("WallTop")
me.from_pydata(verts, [], faces)
wt = bpy.data.objects.new("WallTop", me)
scene.collection.objects.link(wt)
wt.data.materials.append(YELLOW)
# Placeholder stands beyond the wall (to be replaced by the Rodin kit).
verts, faces = [], []
for i, a in enumerate(arc):
    r = fence_r(a)
    x0, y0 = at(a, r + 4)
    x1, y1 = at(a, r + 40)
    verts += [(x0, y0, WALL + 1), (x1, y1, WALL + 22)]
    if i:
        k = 2 * i
        faces.append([k - 2, k, k + 1, k - 1])
me = bpy.data.meshes.new("Stands")
me.from_pydata(verts, [], faces)
st = bpy.data.objects.new("Stands", me)
scene.collection.objects.link(st)
st.data.materials.append(STANDS)

# Lighting: a sun from behind the third-base side, plus sky fill.
sun = bpy.data.lights.new("Sun", "SUN")
sun.energy = 2.2
so = bpy.data.objects.new("Sun", sun)
scene.collection.objects.link(so)
so.rotation_euler = (math.radians(40), 0, math.radians(-150))
world = bpy.data.worlds.new("W")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.45, 0.65, 0.95, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.35

scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 24
scene.cycles.use_denoising = False
scene.view_settings.view_transform = "Standard"
scene.render.image_settings.file_format = "PNG"


def look_at(cam, target):
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


contract = {}

# 1. Pitch view: perspective camera behind and above home plate.
cd = bpy.data.cameras.new("PitchCam")
cd.sensor_fit = "VERTICAL"
cd.angle = math.radians(35)
cam = bpy.data.objects.new("PitchCam", cd)
scene.collection.objects.link(cam)
cam.location = (0.0, -16.0, 8.75)
look_at(cam, (0.0, 23.0, 0.0))
scene.camera = cam
scene.render.resolution_x, scene.render.resolution_y = 1280, 720
scene.render.filepath = f"{out}/pitch_view_bg_hi.png"
bpy.ops.render.render(write_still=True)
contract["pitch_view"] = {"type": "perspective", "location": list(cam.location), "look_at": [0.0, 23.0, 0.0],
                          "fov_v_deg": 35, "render": [1280, 720], "pixel": [320, 180]}

# 2. Field map: orthographic, from behind home plate, 55 deg down.
cd2 = bpy.data.cameras.new("MapCam")
cd2.type = "ORTHO"
span = max(C, LC, RC) * 1.25
cd2.ortho_scale = span
cam2 = bpy.data.objects.new("MapCam", cd2)
scene.collection.objects.link(cam2)
elev = math.radians(55)
center = Vector((0.0, max(C, LC, RC) * 0.48, 0.0))
cam2.location = center + Vector((0.0, -math.cos(elev), math.sin(elev))) * 200
look_at(cam2, center)
scene.camera = cam2
scene.render.resolution_x, scene.render.resolution_y = 1600, 1600
scene.render.filepath = f"{out}/field_map_hi.png"
bpy.ops.render.render(write_still=True)
contract["field_map"] = {"type": "ortho_oblique", "elev_deg": 55, "center": list(center), "ortho_scale_m": span,
                         "render": [1600, 1600], "pixel": [400, 400]}
contract["park_ft"] = dims
with open(f"{out}/camera.json", "w") as fh:
    json.dump(contract, fh, indent=1)
print("done")
