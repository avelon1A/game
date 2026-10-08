# Meshy textured vehicle GLB -> game-ready FBX + texture (Resources/Vehicles).
#   blender -b -P build_meshy_vehicle.py -- in.glb out_dir name length max_tris
# The body is laid along the Y axis with the nose at -Y (Blender front), scaled to `length`, origin at the bottom centre.
# Helicopters: faces in the top slice above the cabin become a separate "Rotor" object so the game can spin it.
import bpy, sys, os, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name, length, max_tris = argv[0], argv[1], argv[2], float(argv[3]), int(argv[4])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.parent = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

img = None
for slot in obj.material_slots:
    if slot.material and slot.material.use_nodes:
        for n in slot.material.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image: img = n.image; break
    if img: break
if img:
    img.filepath_raw = os.path.join(out_dir, name + "_tex.png"); img.file_format = 'PNG'; img.save()

tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
if tris > max_tris:
    m = obj.modifiers.new("dec", 'DECIMATE'); m.ratio = max_tris / tris
    bpy.ops.object.modifier_apply(modifier="dec")

def bounds():
    vs = [obj.matrix_world @ v.co for v in obj.data.vertices]
    return vs, Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))), Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
vs, lo, hi = bounds()
size = hi - lo
# longest horizontal axis -> Y
if size.x > size.y:
    obj.rotation_euler = (0, 0, 1.5708); bpy.ops.object.transform_apply(rotation=True)
vs, lo, hi = bounds()
s = length / max(hi.y - lo.y, 1e-6)
obj.scale = (s, s, s); bpy.ops.object.transform_apply(scale=True)
vs, lo, hi = bounds()
obj.location -= Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)); bpy.ops.object.transform_apply(location=True)
vs, lo, hi = bounds()
L = hi.y - lo.y; H = hi.z - lo.z
# nose = the end with the taller cross-section (cabin); tail boom is thin. Put the nose at -Y.
def end_h(a, b):
    zs = [v.z for v in vs if a <= v.y <= b]
    return (max(zs) - min(zs)) if zs else 0
if end_h(hi.y - L * 0.2, hi.y) > end_h(lo.y, lo.y + L * 0.2):
    obj.rotation_euler = (0, 0, 3.14159); bpy.ops.object.transform_apply(rotation=True)
    vs, lo, hi = bounds()
obj.name = name

# split the main rotor: loose parts up around the rotor mast (blades droop, so not just a top slice):
# a part counts when it sits in the top 30 % and either touches the mast axis or is flat and long (a blade)
top = [v for v in vs if v.z > hi.z - H * 0.05]
hub = Vector((sum(v.x for v in top) / len(top), sum(v.y for v in top) / len(top), 0))
bpy.ops.object.mode_set(mode='EDIT')
bm = bmesh.from_edit_mesh(obj.data)
bm.verts.ensure_lookup_table()
seen = set(); sel = 0
for f in bm.faces: f.select = False
for f0 in bm.faces:
    if f0.index in seen: continue
    part, stack = [], [f0]; seen.add(f0.index)
    while stack:
        f = stack.pop(); part.append(f)
        for e in f.edges:
            for g in e.link_faces:
                if g.index not in seen: seen.add(g.index); stack.append(g)
    pv = {v for f in part for v in f.verts}
    zs = [v.co.z for v in pv]
    if min(zs) < hi.z - H * 0.30: continue
    near = min(((v.co.x - hub.x) ** 2 + (v.co.y - hub.y) ** 2) ** 0.5 for v in pv)
    far = max(((v.co.x - hub.x) ** 2 + (v.co.y - hub.y) ** 2) ** 0.5 for v in pv)
    flat_long = (max(zs) - min(zs)) < H * 0.15 and far > L * 0.25
    if near < L * 0.06 or flat_long:
        for f in part: f.select = True
        sel += len(part)
bmesh.update_edit_mesh(obj.data)
if sel > 0: bpy.ops.mesh.separate(type='SELECTED')
bpy.ops.object.mode_set(mode='OBJECT')
rotor = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o != obj]
if rotor:
    r = rotor[0]; r.name = "Rotor"
    bpy.context.scene.cursor.location = Vector((hub.x, hub.y, hi.z - H * 0.1))
    bpy.ops.object.select_all(action='DESELECT'); r.select_set(True); bpy.context.view_layer.objects.active = r
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    if len(argv) > 5:   # debug render of the rotor alone
        obj.hide_render = True
        scn = bpy.context.scene; scn.render.engine = 'BLENDER_WORKBENCH'; scn.render.resolution_x = 480; scn.render.resolution_y = 360
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
        cam.location = Vector((L * 0.8, -L * 0.8, H * 2.5)); cam.rotation_euler = (Vector((0, 0, H)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = argv[5]; bpy.ops.render.render(write_still=True)
        obj.hide_render = False
print(f"VEHICLE {name}: length {L:.2f} height {H:.2f}, rotor faces {sel}")
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + ".fbx"), use_selection=False, apply_unit_scale=True,
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, path_mode='COPY', embed_textures=False)
