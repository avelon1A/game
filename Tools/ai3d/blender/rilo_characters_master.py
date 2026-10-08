# RILO character master pass (Blender 5.x, background):
#   blender -b -P rilo_characters_master.py -- <Characters dir> <out dir>
# Imports the 5 game heroes, standardises scale/orientation/origin, cleans meshes, renames materials, builds LOD0-3,
# renders deformation test poses and a presentation lineup, writes a validation report and saves the final .blend + FBXs.
# Never destroys geometry it cannot fix safely: such issues go into the report as TODO.
import bpy, bmesh, sys, os, math, json
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
HEROES = [("vanguard", "VANGUARD", 1.80), ("volt", "VOLT", 1.80), ("lyra", "LYRA", 1.74), ("nova", "NOVA", 1.74), ("sol", "SOL", 1.86)]
LOD_TRIS = [60000, 25000, 9000, 3000]
report, todo = [], []

def log(*a): print("[RILO]", *a, flush=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scn = bpy.context.scene
final = bpy.data.collections.new("RILO_CHARACTERS_FINAL"); scn.collection.children.link(final)

def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)

def bounds(objs):
    vs = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    return Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))), Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))

def move_to(o, coll):
    for c in list(o.users_collection): c.objects.unlink(o)
    coll.objects.link(o)

chars = {}
for key, NAME, height in HEROES:
    path = os.path.join(SRC, key, key + ".fbx")
    if not os.path.exists(path): todo.append(f"{NAME}: {path} missing"); continue
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    coll = bpy.data.collections.new("RILO_" + NAME); final.children.link(coll)
    for o in new: move_to(o, coll)
    arm = next((o for o in new if o.type == 'ARMATURE'), None)
    meshes = [o for o in new if o.type == 'MESH']
    for o in new:
        if o.type not in ('ARMATURE', 'MESH'): bpy.data.objects.remove(o)   # cameras / lights / empties
    if arm is None or not meshes: todo.append(f"{NAME}: no armature or mesh"); continue
    if arm.animation_data: arm.animation_data.action = None
    for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0); pb.scale = (1, 1, 1)
    # ---- one Body mesh (Meshy heroes are one merged mesh already; join stray parts)
    bpy.ops.object.select_all(action='DESELECT')
    for m in meshes: m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    # ---- orientation: Meshy faces -Y → turn the whole character to face +Y, feet on Z = 0, centred, scaled to height
    root = bpy.data.objects.new("Character_Root_" + NAME.title(), None); coll.objects.link(root)
    bpy.context.view_layer.update()
    lo, hi = bounds([body])
    s = height / (hi.z - lo.z)
    arm.parent = None
    M = Matrix.Rotation(math.pi, 4, 'Z') @ Matrix.Scale(s, 4) @ Matrix.Translation(Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)))
    arm.matrix_world = M @ arm.matrix_world
    if body.parent != arm: body.matrix_world = M @ body.matrix_world
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    arm.parent = root; body.parent = arm
    body.parent_type = 'OBJECT'
    if not any(md.type == 'ARMATURE' for md in body.modifiers):
        md = body.modifiers.new("Armature", 'ARMATURE'); md.object = arm
    for md in body.modifiers:
        if md.type == 'ARMATURE': md.object = arm
    # ---- mesh cleanup (safe operations only)
    bm = bmesh.new(); bm.from_mesh(body.data)
    v0 = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00005)
    merged = v0 - len(bm.verts)
    loose = [v for v in bm.verts if not v.link_edges]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    degen = [f for f in bm.faces if f.calc_area() < 1e-12]
    bmesh.ops.delete(bm, geom=degen, context='FACES')
    nonman = sum(1 for e in bm.edges if not e.is_manifold)
    bm.to_mesh(body.data); bm.free(); body.data.update()
    # ---- materials: one painted atlas per hero
    for i, slot in enumerate(body.material_slots):
        if slot.material: slot.material.name = f"MAT_{NAME.title()}_Primary" + ("" if i == 0 else f"_{i}")
    if len(body.material_slots) <= 1:
        todo.append(f"{NAME}: single painted texture atlas — Skin/Hair/Metal/Rubber/Emissive split needs manual re-UV + texture separation")
    # ---- names
    arm.name = f"Armature_{NAME.title()}"; arm.data.name = arm.name
    body.name = f"{NAME.title()}_LOD0"; body.data.name = body.name
    # ---- LODs (collapse decimation keeps vertex groups / skin weights)
    lods = [body]
    t0 = tris(body)
    if t0 > LOD_TRIS[0]:
        md = body.modifiers.new("dec", 'DECIMATE'); md.ratio = LOD_TRIS[0] / t0
        bpy.context.view_layer.objects.active = body; bpy.ops.object.modifier_apply(modifier="dec")
    for li, target in enumerate(LOD_TRIS[1:], start=1):
        lod = body.copy(); lod.data = body.data.copy(); coll.objects.link(lod)
        lod.name = f"{NAME.title()}_LOD{li}"; lod.data.name = lod.name
        lod.parent = arm
        md = lod.modifiers.new("dec", 'DECIMATE'); md.ratio = min(1.0, target / max(tris(body), 1))
        bpy.context.view_layer.objects.active = lod; bpy.ops.object.modifier_apply(modifier="dec")
        lod.hide_render = True
        lods.append(lod)
    lo, hi = bounds([body])
    uv = len(body.data.uv_layers) > 0
    chars[NAME] = dict(arm=arm, body=body, lods=lods, root=root, coll=coll)
    report.append(dict(name=NAME, height=round(hi.z - lo.z, 3), tris_src=t0, tris=[tris(l) for l in lods], mats=len(body.material_slots),
                       bones=len(arm.data.bones), uv=uv, lods=len(lods), nonmanifold_edges=nonman, merged=merged, loose=len(loose), degenerate=len(degen)))
    log(NAME, report[-1])

# ------------------------------------------------------------------ pose helpers (world-space aiming of Mixamo bones)
def B(arm, n):
    for pb in arm.pose.bones:
        if pb.name == n or pb.name.endswith(":" + n): return pb
def upd(): bpy.context.view_layer.update()
def aim(arm, n, d):
    pb = B(arm, n)
    if pb is None: return
    upd()
    M = arm.matrix_world @ pb.matrix; h = M.translation
    cur = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
    q = cur.rotation_difference(Vector(d).normalized())
    pb.matrix = arm.matrix_world.inverted() @ (Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ M)
def reset(arm):
    for pb in arm.pose.bones: pb.location = (0, 0, 0); pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = (1, 0, 0, 0)
    upd()
F = 1   # characters face +Y
def pose(arm, name):
    reset(arm)
    if name == "T": return
    # poses are written with +X = the character's left; facing +Y the left side is world -X
    A = lambda side, d: aim(arm, side, (-d[0], d[1], d[2]))
    if name in ("A", "Idle"):
        A("LeftArm", (0.7, 0, -0.7)); A("RightArm", (-0.7, 0, -0.7)); A("LeftForeArm", (0.6, 0.1, -0.8)); A("RightForeArm", (-0.6, 0.1, -0.8))
    elif name == "ArmsUp":
        A("LeftArm", (0.2, 0, 1)); A("RightArm", (-0.2, 0, 1)); A("LeftForeArm", (0.2, 0, 1)); A("RightForeArm", (-0.2, 0, 1))
    elif name == "ArmsForward":
        for s, x in (("Left", 0.15), ("Right", -0.15)): A(s + "Arm", (x, F, 0)); A(s + "ForeArm", (x, F, 0))
    elif name == "Elbow":
        A("LeftArm", (1, 0, 0)); A("RightArm", (-1, 0, 0)); A("LeftForeArm", (0, 0, 1)); A("RightForeArm", (0, 0, 1))
    elif name in ("Knees", "Crouch"):
        deep = name == "Crouch"
        hips = B(arm, "Hips"); upd()
        for s, x in (("Left", 0.1), ("Right", -0.1)):
            A(s + "UpLeg", (x, F * (0.9 if deep else 0.5), -1)); A(s + "Leg", (x, -F * (0.8 if deep else 0.4), -1))
        A("LeftArm", (0.6, 0.3, -0.7)); A("RightArm", (-0.6, 0.3, -0.7))
    elif name == "Run":
        A("LeftUpLeg", (0.1, F * 0.7, -1)); A("LeftLeg", (0.1, -F * 0.2, -1)); A("RightUpLeg", (-0.1, -F * 0.5, -1)); A("RightLeg", (-0.1, -F * 1.2, -0.6))
        A("LeftArm", (0.3, -F * 0.6, -0.8)); A("RightArm", (-0.3, F * 0.6, -0.8)); A("LeftForeArm", (0.2, F * 0.4, -0.4)); A("RightForeArm", (-0.2, F * 1, 0.2))
    elif name == "Jump":
        for s, x in (("Left", 0.12), ("Right", -0.12)): A(s + "UpLeg", (x, F * 1, -0.3)); A(s + "Leg", (x, -F * 0.3, -1))
        A("LeftArm", (0.6, 0, 0.8)); A("RightArm", (-0.6, 0, 0.8))
    elif name == "Weapon":
        A("RightArm", (-0.25, F, -0.25)); A("RightForeArm", (0.05, F, 0)); A("LeftArm", (0.3, F, -0.4)); A("LeftForeArm", (-0.5, F, 0.1))
    upd()

POSES = ["T", "A", "ArmsUp", "ArmsForward", "Elbow", "Knees", "Run", "Crouch", "Jump", "Weapon"]

# ------------------------------------------------------------------ rendering
scn.render.engine = 'BLENDER_WORKBENCH'
scn.display.shading.light = 'STUDIO'; scn.display.shading.color_type = 'TEXTURE'
scn.display.shading.show_cavity = False
scn.world = bpy.data.worlds.new("Studio"); scn.world.color = (0.55, 0.58, 0.64)
cam = bpy.data.objects.new("PresentationCamera", bpy.data.cameras.new("PresentationCamera")); scn.collection.objects.link(cam); scn.camera = cam
def shoot(path, pos, look, lens=50, w=640, h=720):
    scn.render.resolution_x, scn.render.resolution_y = w, h
    cam.data.lens = lens
    cam.location = pos; cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = path; bpy.ops.render.render(write_still=True)

def solo(name):
    for N, c in chars.items():
        vis = N == name
        c["root"].location = (0, 0, 0)
        for o in c["coll"].objects: o.hide_render = not vis or (o.type == 'MESH' and not o.name.endswith("_LOD0"))

os.makedirs(os.path.join(OUT, "poses"), exist_ok=True)
for N, c in chars.items():
    solo(N)
    for p in POSES:
        pose(c["arm"], p)
        shoot(os.path.join(OUT, "poses", f"{N}_{p}.png"), (3.2, 3.2, 1.3), (0, 0, 0.95), lens=45, w=300, h=380)
    reset(c["arm"])

# ------------------------------------------------------------------ presentation: lineup in A-pose with labels
names = list(chars.keys())
for i, N in enumerate(names):
    c = chars[N]
    for o in c["coll"].objects: o.hide_render = o.type == 'MESH' and not o.name.endswith("_LOD0")
    c["root"].location = ((i - (len(names) - 1) / 2) * 1.4, 0, 0)
    upd(); pose(c["arm"], "A")
    txt = bpy.data.curves.new("Label_" + N, 'FONT'); txt.body = N; txt.size = 0.18; txt.align_x = 'CENTER'
    lab = bpy.data.objects.new("Label_" + N, txt); scn.collection.objects.link(lab)
    lab.location = (c["root"].location.x, 0.5, 0.05)
    lab.rotation_euler = (math.radians(90), 0, math.pi)   # standing, readable from the front camera (+Y)
os.makedirs(os.path.join(OUT, "presentation"), exist_ok=True)
for view, pos in (("front", (0, 9, 1.1)), ("side", (9, 0, 1.1)), ("back", (0, -9, 1.1)), ("three_quarter", (6.5, 6.5, 2.2))):
    shoot(os.path.join(OUT, "presentation", f"lineup_{view}.png"), pos, (0, 0, 0.9), lens=40, w=1600, h=700)

# ------------------------------------------------------------------ FBX per character (Unity: LOD0..3 names → LODGroup)
os.makedirs(os.path.join(OUT, "fbx"), exist_ok=True)
for N, c in chars.items():
    reset(c["arm"])
    bpy.ops.object.select_all(action='DESELECT')
    c["arm"].select_set(True)
    for l in c["lods"]: l.select_set(True)
    bpy.context.view_layer.objects.active = c["arm"]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "fbx", f"RILO_{N}.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=False, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='Z', axis_up='Y', mesh_smooth_type='FACE')

# ------------------------------------------------------------------ validation report
lines = ["| Character | Height | Triangles LOD0/1/2/3 | Materials | Bones | UV | LOD | Non-manifold edges | Ready |", "|---|---|---|---|---|---|---|---|---|"]
for r in report:
    ok_h = 1.6 <= r["height"] <= 1.95
    ok_t = r["tris"][0] <= LOD_TRIS[0] * 1.02
    ready = ok_h and ok_t and r["uv"] and r["lods"] == 4 and r["bones"] > 20
    r["ready"] = ready and r["nonmanifold_edges"] == 0
    flag = "READY" if r["ready"] else ("READY*" if ready else "NO")
    lines.append(f"| {r['name']} | {r['height']} m | {'/'.join(str(t) for t in r['tris'])} (src {r['tris_src']}) | {r['mats']} | {r['bones']} | {'yes' if r['uv'] else 'NO'} | {r['lods']} | {r['nonmanifold_edges']} | {flag} |")
summary = [
    "", f"Characters: {len(report)}", f"Armatures: {len(chars)}", f"Materials: {sum(r['mats'] for r in report)}",
    f"LOD meshes: {sum(r['lods'] for r in report)}",
    "Cleanup: " + ", ".join(f"{r['name']} merged {r['merged']} verts / {r['loose']} loose / {r['degenerate']} zero-area faces" for r in report),
    "READY* = passes every check except open (non-manifold) edges, which are normal for Meshy single-shell clothing/hair; closing them needs manual work.",
    "", "Remaining TODO:"] + [f"- {t}" for t in todo] + [
    "- Body / Clothes / Hair / Accessories are one merged Meshy mesh per hero: separating them needs manual mesh selection + re-UV.",
    "- Proportions: heights are standardised; limb/head proportions were not altered (would redesign the characters).",
    "- Bone names kept as the shared Mixamo humanoid layout (Hips/Spine/…): renaming to Root/Pelvis/… would break every animation clip; Unity humanoid maps them as is.",
]
open(os.path.join(OUT, "VALIDATION.md"), "w").write("# RILO characters — validation\n\n" + "\n".join(lines + summary) + "\n")
log("\n" + "\n".join(lines + summary))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "rilo_characters_final.blend"))
log("saved", os.path.join(OUT, "rilo_characters_final.blend"))
