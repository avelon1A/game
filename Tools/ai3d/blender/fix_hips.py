# Meshy auto-rig fix: moves a hero's Hips joint in line with its spine and legs (front/back only), keeps the mesh and all clips.
#   blender -b -P fix_hips.py -- hero.fbx out.fbx
import bpy, sys
argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
if arm.animation_data: arm.animation_data.action = None
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
hips = eb["mixamorig:Hips"]
ref = [eb[n].head.y for n in ("mixamorig:Spine", "mixamorig:LeftUpLeg", "mixamorig:RightUpLeg") if n in eb]
y = sum(ref) / len(ref)
print("HIPS y", round(hips.head.y, 3), "->", round(y, 3))
d = y - hips.head.y
hips.head.y += d; hips.tail.y += d
bpy.ops.object.mode_set(mode='OBJECT')
# hips translation keys were relative to the old joint: shift them by the same amount in bone space (z/up is not touched)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=argv[1], use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
print("WROTE", argv[1])
