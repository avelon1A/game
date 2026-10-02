"""
Imports a Meshy *rigged* character download (one GLB per animation, Mixamo skeleton) into the Unity hero pipeline.
  Blender -b -P build_meshy_rigged.py -- <folder with *_Animation_<Name>_withSkin.glb> <unity Characters dir> <hero name>
Output: Characters/<hero>/{<hero>.fbx (mesh + rig + clips), <hero>_albedo.png, <hero>_textured.txt}.
Clips it lacks (idle, jump, roll, aim, hit, death…) come from the Universal Animation Library via Unity humanoid
retargeting (CharacterBuilder: HumanoidHeroes).
"""
import bpy, glob, os, re, sys
import numpy as np
from mathutils import Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_ROOT, NAME = argv[0], argv[1], argv[2]
HEIGHT = 2.0
# Meshy animation name -> game clip (first match wins; unknown ones are kept under their own name)
CLIP_MAP = [("walking", "walk"), ("casual_walk", "walk_casual"), ("running", "run"), ("run_03", "run_alt"),
            ("runfast", "sprint"), ("agree_gesture", "victory"), ("skill_01", "skill")]


def log(*a): print(f"[{NAME}]", *a, flush=True)


def clip_name(path):
    m = re.search(r"_Animation_(.+?)_withSkin", os.path.basename(path))
    raw = (m.group(1) if m else os.path.basename(path)).lower()
    for key, clip in CLIP_MAP:
        if raw == key: return clip
    return raw


def fcurves_of(act):
    out = []
    if hasattr(act, "layers") and len(getattr(act, "layers", [])):
        for layer in act.layers:
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []): out.append((bag.fcurves, list(bag.fcurves)))
    else:
        out.append((act.fcurves, list(act.fcurves)))
    return out


files = sorted(glob.glob(f"{SRC}/*_withSkin.glb"))
base = next((f for f in files if clip_name(f) == "walk"), files[0])
bpy.ops.wm.read_factory_settings(use_empty=True)

clips = {}
rig = mesh = None
for f in [base] + [x for x in files if x != base]:
    before_objs, before_acts = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=f)
    new_objs = [o for o in bpy.data.objects if o not in before_objs]
    new_acts = [a for a in bpy.data.actions if a not in before_acts]
    name = clip_name(f)
    if rig is None:
        rig = next(o for o in new_objs if o.type == 'ARMATURE')
        meshes = [o for o in new_objs if o.type == 'MESH' and len(o.data.vertices) > 1000]
        for o in new_objs:
            if o.type == 'MESH' and o not in meshes: bpy.data.objects.remove(o, do_unlink=True)   # Meshy's helper sphere
        mesh = meshes[0]
        keep_objs = {rig.name, mesh.name}
    else:
        for o in new_objs: bpy.data.objects.remove(o, do_unlink=True)
    if new_acts:
        a = new_acts[0]; a.name = name; a.use_fake_user = True; clips[name] = a
        for extra in new_acts[1:]: bpy.data.actions.remove(extra)
log("clips:", sorted(clips))

# only bone motion: drop keys on the armature object itself so our size / facing stay
for a in clips.values():
    for coll, curves in fcurves_of(a):
        for fc in curves:
            if not fc.data_path.startswith("pose.bones"): coll.remove(fc)

# texture → <hero>_albedo.png
os.makedirs(f"{OUT_ROOT}/{NAME}", exist_ok=True)
img = next((n.image for m in mesh.data.materials if m and m.use_nodes for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
if img is None: sys.exit("no texture found in the GLB")
img.filepath_raw = f"{OUT_ROOT}/{NAME}/{NAME}_albedo.png"; img.file_format = 'PNG'; img.save()
log("texture", img.size[:])

# size + facing like the other heroes: HEIGHT tall, feet on the ground, facing -Y
rig.animation_data_create(); rig.animation_data.action = None
# glTF import leaves the last clip's pose on the bones; Unity builds the humanoid reference from the exported default
# pose, so put every bone back to its rest pose first
for pb in rig.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
bpy.context.view_layer.update()
zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
s = HEIGHT / (max(zs) - min(zs))
rig.matrix_world = Matrix.Scale(s, 4) @ rig.matrix_world
bpy.context.view_layer.update()
# Meshy GLBs already face the right way after import (Mixamo 'Left' bones end up on the character's left); do NOT turn —
# a turned model has its left/right swapped for Unity's humanoid retargeting
bpy.context.view_layer.update()
zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
rig.location.z -= min(zs)
log("height", round(max(zs) - min(zs), 3))

bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True); mesh.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=f"{OUT_ROOT}/{NAME}/{NAME}.fbx", use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
open(f"{OUT_ROOT}/{NAME}/{NAME}_textured.txt", "w").write("Meshy rigged model\n")
log("exported")
