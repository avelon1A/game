"""
Meshy preview models (untextured) -> coloured landmark props (Resources/Props/landmark_<name>.fbx).
Colouring by height bands / normals keeps them in the toon palette without spending Meshy texture credits.
  Blender -b -P build_landmarks.py -- <meshy dir> <unity Resources/Props dir>
"""
import bpy, os, sys
argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]

def hexc(h): h = h.lstrip('#'); return tuple(int(h[i:i+2], 16) / 255 for i in (0, 2, 4))

# name -> list of (max relative height, colour) bands, low to high
SPEC = {
    "tower":    [(0.06, "#9a96b8"), (0.70, "#2d2a45"), (0.78, "#b46bff"), (1.01, "#3a3560")],
    "terminal": [(0.45, "#4a4d5e"), (0.62, "#6b7088"), (1.01, "#39d98a")],
    "dam":      [(0.25, "#8f939e"), (1.01, "#c9ccd6")],
    "crane":    [(0.12, "#3b3752"), (1.01, "#e8a33a")],
}

for name, bands in SPEC.items():
    src = os.path.join(SRC, name + "_preview.glb")
    if not os.path.exists(src): print("[landmark] missing", name); continue
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
    cx, cy, z0, z1 = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs), max(zs)
    for v in vs: v.co.x -= cx; v.co.y -= cy; v.co.z -= z0
    H = max(z1 - z0, 1e-4)
    o.data.materials.clear()
    for i, (_, col) in enumerate(bands):
        m = bpy.data.materials.new(f"{name}_{i}"); m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*[c ** 2.2 for c in hexc(col)], 1)
        o.data.materials.append(m)
    for p in o.data.polygons:
        z = sum(vs[i].co.z for i in p.vertices) / len(p.vertices) / H
        idx = next(i for i, (mx, _) in enumerate(bands) if z <= mx)
        p.material_index = idx
    # reuse the palette baker from build_props
    sys.path.insert(0, os.path.dirname(__file__))
    o.name = "landmark_" + name
    import importlib.util
    spec = importlib.util.spec_from_file_location("bp", os.path.join(os.path.dirname(__file__), "build_props_lib.py"))
    out = os.path.join(OUT, f"landmark_{name}.fbx")
    bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE')
    print("[landmark]", name, "faces", len(o.data.polygons))
