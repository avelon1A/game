# Makes a relaxed standing idle (arms down at the sides, soft elbows, breathing, small weight shift) on a Meshy
# rigged character (Mixamo skeleton) and saves it as one more "<name>_Animation_Stand_Idle_withSkin.glb".
#   blender -b -P make_stand_idle.py -- in_withSkin.glb out_withSkin.glb [side_render.png]
import bpy, sys, math
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
arm.animation_data_clear()
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')

def B(name):
    for pb in arm.pose.bones:
        if pb.name == name or pb.name.endswith(":" + name) or pb.name.endswith("_" + name): return pb
    return None

names = {k: B(k) for k in ["Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftArm", "RightArm",
                          "LeftForeArm", "RightForeArm", "LeftHand", "RightHand"]}
print("BONES", {k: (v.name if v else None) for k, v in names.items()})
for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
W = arm.matrix_world
fwd = Vector((0, -1, 0))   # glTF import: character faces -Y

def upd(): bpy.context.view_layer.update()

def aim(pb, d):
    """Rotate pb (armature space) so its +Y (head→tail) points along world direction d."""
    if pb is None: return
    upd()
    M = W @ pb.matrix
    head = M.translation
    cur = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
    q = cur.rotation_difference(d.normalized())
    newM = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ M
    pb.matrix = W.inverted() @ newM

def turn(pb, axis, deg):
    if pb is None: return
    upd()
    M = W @ pb.matrix
    head = M.translation
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = W.inverted() @ (Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ M)

FR = 120
scn = bpy.context.scene
scn.frame_start, scn.frame_end = 0, FR
for f in range(0, FR + 1, 10):
    ph = math.sin(2 * math.pi * f / FR)          # breathing
    ph2 = math.sin(2 * math.pi * f / FR + 1.2)   # weight shift, a little out of phase
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
    upd()
    # chest breathes (tiny lean back on the inhale), head settles, weight shifts side to side
    turn(names["Spine2"], 'X', 1.2 * ph)
    turn(names["Spine"], 'Y', 0.8 * ph2)
    turn(names["Hips"], 'Y', -1.0 * ph2)
    turn(names["Head"], 'X', -2.0 - 0.8 * ph)
    for side, sx in (("Left", 1), ("Right", -1)):
        # rest pose is a T/A pose: arms hang down, slightly out from the body and a touch forward
        # (glTF: +X is the character's left, -Y its front)
        spread = 0.24 + 0.01 * ph
        aim(names[side + "Arm"], Vector((sx * spread, -0.06, -1)))
        aim(names[side + "ForeArm"], Vector((sx * 0.12, -0.24, -1)))   # soft elbow
        aim(names[side + "Hand"], Vector((sx * 0.08, -0.2, -1)))
    upd()
    for pb in arm.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=f)
        if pb == names["Hips"]: pb.keyframe_insert("location", frame=f)
act = arm.animation_data.action
act.name = "Stand_Idle"
for fc in getattr(act, "fcurves", []):
    for k in fc.keyframe_points: k.interpolation = 'BEZIER'
bpy.ops.object.mode_set(mode='OBJECT')

if len(argv) > 2:   # side view check at mid-breath
    scn.frame_set(30)
    scn.render.engine = 'BLENDER_WORKBENCH'; scn.render.resolution_x = 360; scn.render.resolution_y = 480
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    for nm, pos in (("side", Vector((3.2, 0, 1.0))), ("front", Vector((0, -3.2, 1.0)))):
        cam.location = pos; cam.rotation_euler = (Vector((0, 0, 0.9)) - pos).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = argv[2].replace(".png", "_" + nm + ".png"); bpy.ops.render.render(write_still=True)

bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_animations=True, export_skins=True)
print("IDLE written", out)
