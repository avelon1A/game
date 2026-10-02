"""
Textured Meshy landmarks (refined GLBs) -> Resources/Props/big_<name>.fbx + texture PNGs in Props/Textures.
  Blender -b -P build_big_landmarks.py -- <dir with <name>_textured.glb> <unity Resources/Props dir> name1 name2 ...
Origin at the bottom centre, +Z up. WorldBuilder scales them to their landmark size.
"""
import bpy, os, sys
argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, NAMES = argv[0], argv[1], argv[2:]
tex_dir = os.path.join(OUT, "Textures"); os.makedirs(tex_dir, exist_ok=True)
for name in NAMES:
    src = os.path.join(SRC, f"{name}_textured.glb")
    if not os.path.exists(src): print("[big] missing", name); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in bpy.data.objects: o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    for e in [x for x in bpy.data.objects if x.type != 'MESH']: bpy.data.objects.remove(e, do_unlink=True)
    vs = o.data.vertices
    xs = [v.co.x for v in vs]; ys = [v.co.y for v in vs]; zs = [v.co.z for v in vs]
    cx, cy, z0 = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)
    for v in vs: v.co.x -= cx; v.co.y -= cy; v.co.z -= z0
    o.name = "big_" + name
    for img in bpy.data.images:
        if img.size[0] == 0: continue
        dst = os.path.join(tex_dir, f"big_{name}_{img.name.split('.')[0]}.png")
        img.filepath_raw = dst; img.file_format = 'PNG'; img.save()
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, f"big_{name}.fbx"), use_selection=False, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', path_mode='RELATIVE',
                             embed_textures=False, mesh_smooth_type='FACE')
    print("[big]", name, "faces", len(o.data.polygons), "size", round(max(xs)-min(xs),2), round(max(ys)-min(ys),2), round(max(zs)-z0,2))
