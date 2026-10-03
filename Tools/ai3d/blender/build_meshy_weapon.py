# Meshy textured weapon GLB -> game-ready FBX + texture for Resources/Weapons.
#   blender -b -P build_meshy_weapon.py -- in.glb out_dir name length_units max_tris grip_frac grip_h muzzle_h
# grip_frac: grip position from the stock end (0..1); grip_h / muzzle_h: heights as a fraction of the gun height.
# Empties "Grip" and "MuzzlePt" are exported; WeaponImport.cs uses them instead of guessing.
# The gun is laid along +X (longest axis), scaled so its length = length_units (WeaponImport scales by 0.8),
# decimated to max_tris and exported with its base-colour texture as <name>_tex.png.
# WeaponImport.cs then puts the grip at the pivot and adds the "Muzzle" point.
import bpy, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name, length, max_tris = argv[0], argv[1], argv[2], float(argv[3]), int(argv[4])
grip_frac, grip_h, muzzle_h = float(argv[5]), float(argv[6]), float(argv[7])

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

# texture: the base colour image of the first material
img = None
for slot in obj.material_slots:
    if slot.material and slot.material.use_nodes:
        for n in slot.material.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                img = n.image; break
    if img: break
if img:
    img.filepath_raw = os.path.join(out_dir, name + "_tex.png")
    img.file_format = 'PNG'
    img.save()

# decimate
tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
if tris > max_tris:
    m = obj.modifiers.new("dec", 'DECIMATE')
    m.ratio = max_tris / tris
    bpy.ops.object.modifier_apply(modifier="dec")

# lay the longest horizontal axis along X, normalise length, centre at origin
bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
size = Vector((max(v.x for v in bb) - min(v.x for v in bb), max(v.y for v in bb) - min(v.y for v in bb), max(v.z for v in bb) - min(v.z for v in bb)))
if size.y > size.x:
    obj.rotation_euler = (0, 0, 1.5708)
    bpy.ops.object.transform_apply(rotation=True)
    size = Vector((size.y, size.x, size.z))
s = length / max(size.x, 1e-6)
obj.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)
bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
ctr = sum(bb, Vector()) / 8
obj.location -= ctr
bpy.ops.object.transform_apply(location=True)
obj.name = name

# which end is the muzzle? the barrel tip is thin, the stock end is tall
vs = [obj.matrix_world @ v.co for v in obj.data.vertices]
xmin = min(v.x for v in vs); xmax = max(v.x for v in vs); zmin = min(v.z for v in vs); zmax = max(v.z for v in vs)
L = xmax - xmin; H = zmax - zmin
def end_height(lo, hi):
    zs = [v.z for v in vs if lo <= v.x <= hi]
    return (max(zs) - min(zs)) if zs else 0
muzzle_plus = end_height(xmax - L * 0.06, xmax) < end_height(xmin, xmin + L * 0.06)
sx = 1 if muzzle_plus else -1
rear = xmin if muzzle_plus else xmax
grip = Vector((rear + sx * L * grip_frac, 0, zmin + H * grip_h))
muz = Vector((xmax if muzzle_plus else xmin, 0, zmin + H * muzzle_h))
for nm, loc in (("Grip", grip), ("MuzzlePt", muz)):
    e = bpy.data.objects.new(nm, None); e.location = loc
    bpy.context.scene.collection.objects.link(e)
print(f"muzzle at {'+X' if muzzle_plus else '-X'}")

tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + ".fbx"), use_selection=False, apply_unit_scale=True,
                         axis_forward='-Z', axis_up='Y', bake_space_transform=True, path_mode='COPY', embed_textures=False)
print(f"WEAPON {name}: {tris} tris, length {length}, texture {'yes' if img else 'no'}")
