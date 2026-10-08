# Proportion fix on a game hero, keeping its clips: joints are moved (pose translations along each bone, uniform scale for
# hands / feet), the deformed mesh is baked and that pose becomes the new rest pose. Bone directions don't change, so every
# rotation clip still plays the same.
#   blender -b -P fix_proportions.py -- in.fbx out.fbx render_prefix shoulder_w upper_arm forearm hand_scale foot_scale
import bpy, sys, math
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
src, out, prefix = a[0], a[1], a[2]
SHOULDER, UPPER, FORE, HANDS, FEET = (float(x) for x in a[3:8])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
if arm.animation_data: arm.animation_data.action = None
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
for o in list(bpy.context.scene.objects):
    if o.type not in ('ARMATURE', 'MESH'): bpy.data.objects.remove(o)
for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'; pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
bpy.context.view_layer.update()

H = max((mesh.matrix_world @ v.co).z for v in mesh.data.vertices) - min((mesh.matrix_world @ v.co).z for v in mesh.data.vertices)
k = H / 1.8   # measurements are in the normalised 1.8 m space
P = lambda n: arm.pose.bones["mixamorig:" + n]
Bn = lambda n: arm.data.bones["mixamorig:" + n]
W = lambda n: arm.matrix_world @ Bn(n).head_local

def render(tag):
    scn = bpy.context.scene; scn.render.engine = 'BLENDER_WORKBENCH'; scn.display.shading.color_type = 'TEXTURE'
    scn.world = scn.world or bpy.data.worlds.new("w"); scn.world.color = (0.55, 0.58, 0.64)
    cam = scn.camera
    if cam is None:
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    cam.data.lens = 50; scn.render.resolution_x, scn.render.resolution_y = 420, 520
    zc = H * 0.5
    for nm, pos in (("front", Vector((0, -H * 2.6, zc))), ("side", Vector((H * 2.6, 0, zc)))):
        cam.location = pos; cam.rotation_euler = (Vector((0, 0, zc)) - pos).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = f"{prefix}_{tag}_{nm}.png"; bpy.ops.render.render(write_still=True)

render("before")
before = dict(sh=(W("LeftArm") - W("RightArm")).length / k, ua=(W("LeftForeArm") - W("LeftArm")).length / k, fa=(W("LeftHand") - W("LeftForeArm")).length / k)
print("BEFORE", before)
for side in ("Left", "Right"):
    # pose translation is in the bone's rest space: +Y runs along the bone, so these slide each joint further out along the arm
    P(side + "Arm").location.y += (SHOULDER - before["sh"]) / 2 * k / Bn(side + "Arm").length * Bn(side + "Arm").length
    P(side + "ForeArm").location.y += (UPPER - before["ua"]) * k
    P(side + "Hand").location.y += (FORE - before["fa"]) * k
    P(side + "Hand").scale = (HANDS, HANDS, HANDS)
    P(side + "Foot").scale = (FEET, FEET, FEET)
bpy.context.view_layer.update()
after = dict(sh=None)
# bake: apply the armature deformation to the mesh, make this pose the rest pose, re-bind
bpy.context.view_layer.objects.active = mesh
md = next(m for m in mesh.modifiers if m.type == 'ARMATURE')
name = md.name
bpy.ops.object.modifier_apply(modifier=name)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.select_all(action='SELECT'); bpy.ops.pose.armature_apply(selected=False); bpy.ops.object.mode_set(mode='OBJECT')
md = mesh.modifiers.new("Armature", 'ARMATURE'); md.object = arm
bpy.context.view_layer.update()
W2 = lambda n: arm.matrix_world @ arm.data.bones["mixamorig:" + n].head_local
print("AFTER", dict(sh=(W2("LeftArm") - W2("RightArm")).length / k, ua=(W2("LeftForeArm") - W2("LeftArm")).length / k, fa=(W2("LeftHand") - W2("LeftForeArm")).length / k))
render("after")
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
print("WROTE", out)
