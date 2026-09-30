"""
Builds the "ranger" hero from free CC0 Quaternius packs (no generation, no re-rigging):
  - Modular Character Outfits - Fantasy: Male_Ranger outfit (hood, body, arms, belts, boots)
  - Universal Base Characters: Superhero_Male head + eyes + eyebrows (under the hood)
  - Universal Animation Library: walk / jog / sprint / idle / jump / roll / pistol / hit / death / dance
All three share Quaternius' 65-bone universal rig, so the animations play on the outfit's armature directly.
Output (Unity pipeline, see Editor/CharacterBuilder.cs): Assets/Veil/Characters/ranger/{ranger.fbx, ranger_albedo.png, ranger_textured.txt}

  Blender -b -P build_ranger.py -- <packs dir (Tools/assets_dl/x)> <unity Characters dir> [--preview <dir>]
"""
import bpy, bmesh, os, sys
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
PACKS, OUT_ROOT = argv[0], argv[1]
PREVIEW = argv[argv.index("--preview") + 1] if "--preview" in argv else None
NAME = "ranger"
HEIGHT = 2.0          # same as the other heroes (build_character.py)
ATLAS = 4096

OUTFITS = f"{PACKS}/Modular Character Outfits - Fantasy[Standard]"
BASE = f"{PACKS}/Universal Base Characters[Standard]"
UAL = f"{PACKS}/Universal Animation Library[Standard]"
TEX = {
    "MI_Ranger": f"{OUTFITS}/Textures/Ranger/T_Ranger_BaseColor.png",
    "MI_Regular_Male": f"{OUTFITS}/Textures/Base/T_Regular_Male_Dark_BaseColor.png",
    "MI_Superhero_Male": f"{BASE}/Base Characters/Textures/T_Superhero_Male_Dark.png",
    "MI_Eyes": f"{BASE}/Base Characters/Textures/T_Eye_Brown.png",
    "MI_Hair_1": f"{BASE}/Base Characters/Textures/T_Hair_1_BaseColor.png",
}
# atlas cells (x0, y0, size) in 0..1 UV space: big cells for the outfit and the face
CELLS = {
    "MI_Ranger": (0.0, 0.5, 0.5),
    "MI_Superhero_Male": (0.5, 0.5, 0.5),
    "MI_Regular_Male": (0.0, 0.0, 0.5),
    "MI_Eyes": (0.5, 0.25, 0.25),
    "MI_Hair_1": (0.75, 0.25, 0.25),
}
# game clip name -> Universal Animation Library action
CLIPS = {
    "idle": "Idle_Loop", "walk": "Walk_Loop", "run": "Jog_Fwd_Loop", "sprint": "Sprint_Loop",
    "jump": "Jump_Start", "fall": "Jump_Loop", "dash": "Roll", "shoot": "Pistol_Aim_Neutral",
    "hit": "Hit_Chest", "death": "Death01", "victory": "Dance_Loop",
}


def log(*a): print("[ranger]", *a, flush=True)


def imported(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------------------------------------------------------------- outfit (keeps its armature = the rig)
objs = imported(f"{OUTFITS}/Exports/FBX (Unity)/Outfits/Male_Ranger.fbx")
rig = next(o for o in objs if o.type == 'ARMATURE')
rig.name = "Rig"
meshes = [o for o in objs if o.type == 'MESH']
log("outfit parts:", [m.name for m in meshes])

# ---------------------------------------------------------------- head from the base character
bobjs = imported(f"{BASE}/Base Characters/Unity/Superhero_Male_FullBody.fbx")
brig = next(o for o in bobjs if o.type == 'ARMATURE')
bmeshes = [o for o in bobjs if o.type == 'MESH']
body = next(o for o in bmeshes if o.name.startswith("SuperHero"))
# keep only the head + upper neck (the hood and collar hide the seam)
keep_groups = {body.vertex_groups[n].index for n in ("Head", "neck_01") if n in body.vertex_groups}
bm = bmesh.new(); bm.from_mesh(body.data)
dl = bm.verts.layers.deform.active
drop = []
for v in bm.verts:
    w = v[dl]
    head_w = sum(val for g, val in w.items() if g in keep_groups)
    if head_w < 0.5: drop.append(v)
bmesh.ops.delete(bm, geom=drop, context='VERTS')
bm.to_mesh(body.data); bm.free()
log("head verts kept:", len(body.data.vertices))
# line the base head up with the outfit rig's head bone
off = (rig.matrix_world @ rig.data.bones["Head"].head_local) - (brig.matrix_world @ brig.data.bones["Head"].head_local)
log("head offset", tuple(round(x, 3) for x in off))
for m in bmeshes:
    mw = m.matrix_world.copy()
    m.parent = rig
    m.matrix_world = mw
    for mod in m.modifiers:
        if mod.type == 'ARMATURE': mod.object = rig
    m.data.transform(m.matrix_world.inverted() @ __import__("mathutils").Matrix.Translation(off) @ m.matrix_world)
    meshes.append(m)
bpy.data.objects.remove(brig, do_unlink=True)

# ---------------------------------------------------------------- animations (same bone names → play on our rig)
aobjs = imported(f"{UAL}/Unity/UAL1_Standard.fbx")
src = {a.name.split("|")[-1]: a for a in bpy.data.actions}
log("library actions:", len(src))
for o in aobjs: bpy.data.objects.remove(o, do_unlink=True)
made = {}
for clip, act in CLIPS.items():
    a = src[act].copy()
    a.name = clip
    a.use_fake_user = True
    made[clip] = a
for a in list(bpy.data.actions):
    if a.name not in made: bpy.data.actions.remove(a)


def action_fcurves(act):
    """All F-curves of an action (Blender 4.4+ layered actions keep them in channel bags)."""
    if hasattr(act, "layers") and len(getattr(act, "layers", [])):
        out = []
        for layer in act.layers:
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    out.append((bag.fcurves, list(bag.fcurves)))
        return out
    return [(act.fcurves, list(act.fcurves))]


# the library's actions also key the armature *object* (rotation / scale / location): drop those so the clips only
# move bones and our size + facing below stay in effect
for a in made.values():
    for coll, curves in action_fcurves(a):
        for fc in curves:
            if not fc.data_path.startswith("pose.bones"): coll.remove(fc)

# ---------------------------------------------------------------- one mesh, one atlas texture (the game uses one material per hero)
for m in meshes:
    uvs = m.data.uv_layers.active.data
    for poly in m.data.polygons:
        mat = m.material_slots[poly.material_index].material.name if m.material_slots else "MI_Ranger"
        key = next((k for k in CELLS if mat.startswith(k)), "MI_Ranger")
        x0, y0, s = CELLS[key]
        for li in poly.loop_indices:
            u, v = uvs[li].uv
            u -= np.floor(u) if (u < -0.01 or u > 1.01) else 0.0
            v -= np.floor(v) if (v < -0.01 or v > 1.01) else 0.0
            uvs[li].uv = (x0 + min(max(u, 0.0), 1.0) * s, y0 + min(max(v, 0.0), 1.0) * s)

atlas = np.zeros((ATLAS, ATLAS, 4), dtype=np.float32); atlas[..., 3] = 1.0
for key, path in TEX.items():
    x0, y0, s = CELLS[key]
    n = int(ATLAS * s)
    img = bpy.data.images.load(path)
    img.scale(n, n)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(n, n, 4)
    ix, iy = int(x0 * ATLAS), int(y0 * ATLAS)
    atlas[iy:iy + n, ix:ix + n] = px
    bpy.data.images.remove(img)
os.makedirs(f"{OUT_ROOT}/{NAME}", exist_ok=True)
out_img = bpy.data.images.new("atlas", ATLAS, ATLAS)
out_img.pixels = atlas.ravel()
out_img.filepath_raw = f"{OUT_ROOT}/{NAME}/{NAME}_albedo.png"
out_img.file_format = 'PNG'
out_img.save()
log("atlas saved")

mat = bpy.data.materials.new(NAME); mat.use_nodes = True
bsdf = mat.node_tree.nodes["Principled BSDF"]
tex = mat.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = out_img
mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
for m in meshes:
    m.data.materials.clear(); m.data.materials.append(mat)

bpy.ops.object.select_all(action='DESELECT')
for m in meshes: m.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
mesh = bpy.context.view_layer.objects.active
mesh.name = NAME

# ---------------------------------------------------------------- size + facing like the other heroes (HEIGHT tall, facing -Y)
bpy.context.view_layer.update()
zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
s = HEIGHT / (max(zs) - min(zs))
rig.scale = rig.scale * s
bpy.context.view_layer.update()
# Quaternius FBX (Unity export) faces +Y after import (verified with the preview renders): turn to -Y like the others.
# The imported rig uses quaternion rotation, so rotate the world matrix rather than rotation_euler.
from mathutils import Matrix
rig.matrix_world = Matrix.Rotation(np.pi, 4, 'Z') @ rig.matrix_world
log("rotated to face -Y")
bpy.context.view_layer.update()
zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
rig.location.z -= min(zs)
log("scale", round(s, 3), "height", round(max(zs) - min(zs), 3))

rig.animation_data_create()
rig.animation_data.action = None   # the FBX exporter skips the rig's assigned action, so export with none assigned

# ---------------------------------------------------------------- export for Unity
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True); mesh.select_set(True)
bpy.context.view_layer.objects.active = rig
out = f"{OUT_ROOT}/{NAME}/{NAME}.fbx"
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
open(f"{OUT_ROOT}/{NAME}/{NAME}_textured.txt", "w").write("finished textured model (Quaternius CC0)\n")
log("exported", out, "clips:", sorted(made))

# ---------------------------------------------------------------- optional preview frames of the walk
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 520, 640
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.5
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scn.collection.objects.link(sun)
    sun.data.energy = 3; sun.rotation_euler = (0.8, 0.2, 0.6)
    scn.camera = cam
    world = bpy.data.worlds.new("w"); scn.world = world; world.color = (0.25, 0.25, 0.32)
    rig.animation_data.action = made["idle"]
    for clip, views in (("walk", ((0.0, -6, 1.0, 1.5708, 0, 0), (-6, 0, 1.0, 1.5708, 0, -1.5708))), ("idle", ((0.0, -6, 1.0, 1.5708, 0, 0),))):
        rig.animation_data.action = made[clip]
        f0, f1 = map(int, made[clip].frame_range)
        for vi, (x, y, z, rx, ry, rz) in enumerate(views):
            cam.location = (x, y, z); cam.rotation_euler = (rx, ry, rz)
            for k, fr in enumerate(np.linspace(f0, f1, 4, endpoint=False).astype(int)):
                scn.frame_set(int(fr))
                scn.render.filepath = f"{PREVIEW}/{clip}_{vi}_{k}.png"
                bpy.ops.render.render(write_still=True)
    log("previews in", PREVIEW)
