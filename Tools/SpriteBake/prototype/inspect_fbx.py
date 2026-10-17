import sys

import bpy

for path in sys.argv[1:]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    objs = bpy.context.scene.objects
    arm = [o for o in objs if o.type == "ARMATURE"]
    meshes = [o for o in objs if o.type == "MESH"]
    act = arm[0].animation_data.action if arm and arm[0].animation_data else None
    imgs = [i.name for i in bpy.data.images]
    rng = tuple(int(x) for x in act.frame_range) if act else None
    bones = len(arm[0].data.bones) if arm else 0
    tris = sum(len(m.data.polygons) for m in meshes)
    dims = tuple(round(d, 2) for d in meshes[0].dimensions) if meshes else None
    print(f"{path.split('/')[-1]}: armature={len(arm)} bones={bones} meshes={len(meshes)} polys={tris} dims={dims} "
          f"frames={rng} fps={bpy.context.scene.render.fps} images={imgs}")
