# Adds a relaxed standing "idle" made on the hero's OWN skeleton (no retargeting): spine/neck/head straight with the eyes
# level, shoulders at rest, arms hanging at the sides with soft elbows, breathing + a small weight shift. Keeps every clip.
#   blender -b -P add_own_idle.py -- in.fbx out.fbx render_prefix
import bpy, sys, math
from mathutils import Vector, Matrix
a = sys.argv[sys.argv.index("--") + 1:]
src, out, prefix = a[0], a[1], a[2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
for o in list(bpy.context.scene.objects):
    if o.type not in ('ARMATURE', 'MESH'): bpy.data.objects.remove(o)
# preview texture: the hero's albedo (Characters/<hero>/<hero>_albedo.png) when the FBX's own image isn't found
alb = a[3] if len(a) > 3 else None
if alb:
    img = bpy.data.images.load(alb)
    for slot in mesh.material_slots:
        mt = slot.material
        if not mt: continue
        mt.use_nodes = True
        nt = mt.node_tree
        bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        tex = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new('ShaderNodeTexImage')
        tex.image = img
        if bsdf: nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
# smooth the skin weights around both shoulders / armpits: Meshy's auto-rig splits chest and arm too sharply there,
# so the jacket creases into a hard corner when the arms come down from the T-pose
import bmesh
bpy.context.view_layer.update()
joints = [arm.matrix_world @ (arm.data.bones.get("mixamorig:" + n) or arm.data.bones[n]).head_local for n in ("LeftArm", "RightArm")]
hz = max((mesh.matrix_world @ v.co).z for v in mesh.data.vertices) - min((mesh.matrix_world @ v.co).z for v in mesh.data.vertices)
R = hz * 0.085
bpy.context.view_layer.objects.active = mesh
for v in mesh.data.vertices:
    p = mesh.matrix_world @ v.co
    v.select = any((p - j).length < R for j in joints)
nsel = sum(1 for v in mesh.data.vertices if v.select)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_mode(type='VERT')
bpy.ops.object.mode_set(mode='OBJECT')
for v in mesh.data.vertices:
    p = mesh.matrix_world @ v.co
    v.select = any((p - j).length < R for j in joints)
bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
mesh.data.use_paint_mask_vertex = True
bpy.ops.object.vertex_group_smooth(group_select_mode='ALL', factor=0.6, repeat=12, expand=0.2)
bpy.ops.object.mode_set(mode='OBJECT')
print("SMOOTHED", nsel, "shoulder vertices")
old = [x.name for x in bpy.data.actions]
prefix_name = old[0].rsplit("|", 1)[0] + "|" if old and "|" in old[0] else ""
for x in list(bpy.data.actions):
    if x.name.endswith("|idle") or x.name == "idle": bpy.data.actions.remove(x)
arm.animation_data_create(); arm.animation_data.action = None
# bone names: Meshy downloads use Mixamo ("mixamorig:Spine1"); Meshy's rigging API uses Spine02 (lowest) / Spine01 / Spine / neck
ALIAS = {"Spine": ["Spine02"], "Spine1": ["Spine01"], "Spine2": ["Spine"], "Neck": ["neck"]}
def BN(n):
    for c in ["mixamorig:" + n] + ALIAS.get(n, []) + [n]:
        if c in arm.data.bones and not (n == "Spine" and c == "Spine" and "Spine02" in arm.data.bones): return c
    return None
P = lambda n: arm.pose.bones.get(BN(n)) if BN(n) else None
for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
W = arm.matrix_world
def upd(): bpy.context.view_layer.update()
def reset():
    for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    upd()
def aim(n, d, roll_keep=True):
    pb = P(n)
    if pb is None: return
    upd()
    M = W @ pb.matrix; h = M.translation
    cur = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
    q = cur.rotation_difference(Vector(d).normalized())
    pb.matrix = W.inverted() @ (Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ M)
# which way does the character face? toes are in front of the ankles
upd()
foot, toe = W @ arm.data.bones[BN("LeftFoot")].head_local, W @ arm.data.bones[BN("LeftToeBase")].head_local
F = Vector((0, 1 if toe.y > foot.y else -1, 0))
L = Vector((1 if (W @ arm.data.bones[BN("LeftArm")].head_local).x > 0 else -1, 0, 0))
print("FACING", F, "LEFT", L)

act = bpy.data.actions.new(prefix_name + "idle"); arm.animation_data.action = act
FR = 120
for f in range(0, FR + 1, 10):
    ph = math.sin(2 * math.pi * f / FR); ph2 = math.sin(2 * math.pi * f / FR + 1.2)
    reset()
    up = Vector((0, 0, 1))
    # casual stand: weight on the right leg (hips drift right and drop on the left), left knee eased forward,
    # shoulders counter-tilt, a small twist towards the camera side, head slightly tilted, arms relaxed and uneven
    hips = P("Hips")
    if hips:
        hips.location = (0, 0, 0); upd()
        Mh = W @ hips.matrix
        # world offset → hips local: shift towards the standing (right) leg, settle down a touch
        off = (-L) * (0.035 + 0.004 * ph2) + Vector((0, 0, -0.012))
        hips.matrix = W.inverted() @ (Matrix.Translation(off) @ Mh)
        upd()
        Mh = W @ hips.matrix; h = Mh.translation
        R = Matrix.Rotation(math.radians(4.0), 4, F) @ Matrix.Rotation(math.radians(-6.0), 4, 'Z')
        hips.matrix = W.inverted() @ (Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ Mh)
    # standing (right) leg straight down to the ground under the body, relaxed (left) leg a little forward and out, knee bent
    aim("RightUpLeg", -L * 0.04 + Vector((0, 0, -1)))
    aim("RightLeg", -L * 0.02 + Vector((0, 0, -1)))
    aim("LeftUpLeg", L * 0.10 + F * 0.12 + Vector((0, 0, -1)))
    aim("LeftLeg", L * 0.08 - F * 0.06 + Vector((0, 0, -1)))
    for foot in ("LeftFoot", "RightFoot"):
        pf = P(foot)
        if pf: upd()
    aim("Spine", up - L * 0.05 + F * 0.02)
    aim("Spine1", up - L * 0.03 + F * (0.02 - 0.012 * ph))
    aim("Spine2", up + L * 0.04 + F * (0.03 - 0.015 * ph))
    sp2 = P("Spine2")
    if sp2:
        upd(); M = W @ sp2.matrix; h = M.translation
        sp2.matrix = W.inverted() @ (Matrix.Translation(h) @ Matrix.Rotation(math.radians(7), 4, 'Z') @ Matrix.Translation(-h) @ M)
    aim("Neck", up + F * 0.08 - L * 0.02)
    aim("Head", up + F * 0.04 + L * 0.06)          # small head tilt, eyes level
    hd = P("Head")
    if hd:
        upd(); M = W @ hd.matrix; h = M.translation
        hd.matrix = W.inverted() @ (Matrix.Translation(h) @ Matrix.Rotation(math.radians(-8 + 2 * ph2), 4, 'Z') @ Matrix.Translation(-h) @ M)
    for side, s in (("Left", 1), ("Right", -1)):
        lat = L * s
        sh = P(side + "Shoulder")
        if sh:
            upd()
            M = W @ sh.matrix
            cur = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
            aim(side + "Shoulder", cur + Vector((0, 0, -0.22 if side == "Left" else -0.16)) + F * 0.08)
        relaxed = side == "Left"
        aim(side + "Arm", lat * ((0.22 if relaxed else 0.30) + 0.008 * ph) + F * (0.10 if relaxed else 0.02) + Vector((0, 0, -1)))
        aim(side + "ForeArm", lat * 0.08 + F * (0.35 if relaxed else 0.18) + Vector((0, 0, -1)))
        aim(side + "Hand", lat * 0.04 + F * (0.30 if relaxed else 0.12) + Vector((0, 0, -1)))
    upd()
    for pb in arm.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=f)
        if pb.name.endswith("Hips"): pb.keyframe_insert("location", frame=f)

# check renders (textured) at mid-breath
scn = bpy.context.scene; scn.frame_set(30)
scn.render.engine = 'BLENDER_WORKBENCH'; scn.display.shading.color_type = 'TEXTURE'
scn.world = bpy.data.worlds.new("w"); scn.world.color = (0.55, 0.58, 0.64)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]; H = max(zs) - min(zs); zc = min(zs) + H * 0.5
scn.render.resolution_x, scn.render.resolution_y = 420, 560
for nm, d, zz, dist in (("front", F, zc, 2.6), ("side", L, zc, 2.6), ("three_quarter", (F + L).normalized(), zc, 2.6),
                        ("shoulders_front", F, min(zs) + H * 0.74, 0.9), ("shoulders_back", -F, min(zs) + H * 0.74, 0.9),
                        ("shoulders_34", (F + L).normalized(), min(zs) + H * 0.74, 0.9)):
    zc_ = zz
    pos = Vector((0, 0, zc_)) + Vector(d) * H * dist
    cam.location = pos; cam.rotation_euler = (Vector((0, 0, zc_)) - pos).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = f"{prefix}_{nm}.png"; bpy.ops.render.render(write_still=True)
arm.animation_data.action = None
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
print("ACTIONS", [x.name for x in bpy.data.actions]); print("WROTE", out)
