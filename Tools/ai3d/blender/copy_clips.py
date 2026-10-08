# Copies clips bone-by-bone from one Meshy hero to another (same Mixamo bone names, both T-pose rests) — no humanoid
# muscle retargeting. Rotations are copied as is; the Hips translation is scaled by the hip-height ratio.
#   blender -b -P copy_clips.py -- target.fbx source.fbx out.fbx clip [clip ...]
import bpy, sys
a = sys.argv[sys.argv.index("--") + 1:]
tgt, src, out, names = a[0], a[1], a[2], a[3:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=tgt)
tarm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
before = set(bpy.data.objects); before_act = set(bpy.data.actions)
bpy.ops.import_scene.fbx(filepath=src)
sarm = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o not in before)
new_acts = [x for x in bpy.data.actions if x not in before_act]
def short(n): return n.split("|")[-1]
th = (tarm.matrix_world @ tarm.data.bones["mixamorig:Hips"].head_local).z
sh = (sarm.matrix_world @ sarm.data.bones["mixamorig:Hips"].head_local).z
ratio = th / sh
print("hips ratio", round(ratio, 3))
for n in names:
    srcact = next((x for x in new_acts if short(x.name) == n), None)
    if srcact is None: print("missing", n); continue
    # drop the target's own clip of that name
    for x in list(bpy.data.actions):
        if x not in new_acts and short(x.name) == n: bpy.data.actions.remove(x)
    act = srcact.copy()
    prefix = next((x.name.rsplit("|", 1)[0] + "|" for x in bpy.data.actions if x not in new_acts and "|" in x.name), "")
    act.name = prefix + n
    def curves(ac):
        if hasattr(ac, "fcurves") and len(ac.fcurves): return list(ac.fcurves)
        out = []
        for layer in getattr(ac, "layers", []):
            for strip in layer.strips:
                for cb in strip.channelbags: out += list(cb.fcurves)
        return out
    for fc in curves(act):
        if 'mixamorig:Hips' in fc.data_path and fc.data_path.endswith("location"):
            for k in fc.keyframe_points:
                k.co.y *= ratio; k.handle_left.y *= ratio; k.handle_right.y *= ratio
        bone = fc.data_path.split('"')[1] if '"' in fc.data_path else None
        if bone and bone not in tarm.data.bones: fc.mute = True
    act.use_fake_user = True
    act["rilo_clip"] = n
    print("copied", n)
for x in new_acts: bpy.data.actions.remove(x)
# clean names like the hero's own clips ("<rig>|<rig>|run")
pref = next((x.name.rsplit("|", 1)[0] for x in bpy.data.actions if "rilo_clip" not in x and "|" in x.name), "target_character|target_character")
for x in bpy.data.actions:
    if "rilo_clip" in x: x.name = pref + "|" + x["rilo_clip"]
for o in [o for o in bpy.data.objects if o not in before and o.type in ('ARMATURE', 'MESH')]: bpy.data.objects.remove(o)
for o in list(bpy.context.scene.objects):
    if o.type not in ('ARMATURE', 'MESH'): bpy.data.objects.remove(o)
if tarm.animation_data: tarm.animation_data.action = None
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
print("ACTIONS", [short(x.name) for x in bpy.data.actions]); print("WROTE", out)
