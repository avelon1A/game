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
ONLY = argv[argv.index("--only") + 1] if "--only" in argv else None
SCALE = 1.075         # male base is 1.86 m → 2.0 like the other heroes; the same factor keeps women naturally shorter
ATLAS = 4096

OUTFITS = f"{PACKS}/Modular Character Outfits - Fantasy[Standard]"
BASE = f"{PACKS}/Universal Base Characters[Standard]"
UAL = f"{PACKS}/Universal Animation Library[Standard]"
BT = f"{BASE}/Base Characters/Textures"
HAIR = f"{BASE}/Hairstyles/Rigged to Head Bone/FBX (Unity)"

# hero -> outfit, base body (head), extra hair meshes, outfit texture, recolour (hue shift in degrees of the green cloth)
HEROES = {
    "ranger":   dict(outfit="Male_Ranger",   base="Superhero_Male",   hair=[],                           tex="Ranger/T_Ranger_BaseColor.png",   hue=0),
    "huntress": dict(outfit="Female_Ranger", base="Superhero_Female", hair=[],                           tex="Ranger/T_Ranger_BaseColor.png",   hue=-115),
    "warden":   dict(outfit="Male_Ranger",   base="Superhero_Male",   hair=["Hair_Beard"],               tex="Ranger/T_Ranger_3_BaseColor.png", hue=0),
    "scout":    dict(outfit="Female_Ranger", base="Superhero_Female", hair=[],                           tex="Ranger/T_Ranger_BaseColor.png",   hue=75),
    "drifter":  dict(outfit="Male_Peasant",  base="Superhero_Male",   hair=["Hair_SimpleParted", "Hair_Beard"], tex="Peasant/T_Peasant_BaseColor.png", hue=0, hairTint=(0.32, 0.2, 0.12)),
    "wanderer": dict(outfit="Female_Peasant", base="Superhero_Female", hair=["Hair_Buns"],               tex="Peasant/T_Peasant_2_BaseColor.png", hue=0, hairTint=(0.62, 0.24, 0.12)),
}
SKIN = {  # the free packs only ship the darker 'Regular' skin for hands/arms, so heads use the matching dark tone
    "MI_Regular_Male": f"{OUTFITS}/Textures/Base/T_Regular_Male_Dark_BaseColor.png",
    "MI_Regular_Female": f"{OUTFITS}/Textures/Base/T_Regular_Female_Dark_BaseColor.png",
    "MI_Superhero_Male": f"{BT}/T_Superhero_Male_Dark.png",
    "MI_Superhero_Female": f"{BT}/T_Superhero_Female_Dark_BaseColor.png",
    "MI_Eyes": f"{BT}/T_Eye_Brown.png",
    "MI_Hair_1": f"{BT}/T_Hair_1_BaseColor.png",
    "MI_Hair_2": f"{BT}/T_Hair_2_BaseColor.png",
}
# atlas cells (x0, y0, size) in UV space: outfit and face get the big cells
CELLS = {
    "MI_Outfit": (0.0, 0.5, 0.5), "MI_Superhero": (0.5, 0.5, 0.5), "MI_Regular": (0.0, 0.0, 0.5),
    "MI_Eyes": (0.5, 0.25, 0.25), "MI_Hair_1": (0.75, 0.25, 0.25), "MI_Hair_2": (0.5, 0.0, 0.25),
}
# game clip name -> Universal Animation Library action (no duplicate sources: the FBX exporter merges identical takes)
CLIPS = {
    "idle": "Idle_Loop", "walk": "Walk_Loop", "run": "Jog_Fwd_Loop", "sprint": "Sprint_Loop",
    "jump": "Jump_Start", "fall": "Jump_Loop", "dash": "Roll", "shoot": "Pistol_Aim_Neutral",
    "hit": "Hit_Chest", "death": "Death01", "victory": "Dance_Loop",
}


# "a little like our heroes": stylized proportions (bigger head, hands and feet; shorter legs and arms).
# Baked into the rest pose, so every library animation still plays on the reshaped body.
CHIBI = {
    "Head": (1.7, 1.7, 1.7),
    "thigh_l": (1.1, 0.76, 1.1), "thigh_r": (1.1, 0.76, 1.1),
    "calf_l": (1.08, 0.8, 1.08), "calf_r": (1.08, 0.8, 1.08),
    "foot_l": (1.25, 1.25, 1.25), "foot_r": (1.25, 1.25, 1.25),
    "upperarm_l": (1.08, 0.88, 1.08), "upperarm_r": (1.08, 0.88, 1.08),
    "lowerarm_l": (1.06, 0.9, 1.06), "lowerarm_r": (1.06, 0.9, 1.06),
    "hand_l": (1.3, 1.3, 1.3), "hand_r": (1.3, 1.3, 1.3),
}
TARGET_H = {"Male": 2.0, "Female": 1.9}


def chibify(rig, mesh):
    """Pose the proportion changes, bake them into the mesh, then make that pose the new rest pose."""
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for eb in rig.data.edit_bones:   # a bone's scale must not stretch its children (they only follow its tail)
        eb.inherit_scale = 'NONE' if eb.parent is not None and eb.parent.name in CHIBI and not eb.name.startswith(("index", "middle", "ring", "pinky", "thumb")) else eb.inherit_scale
    bpy.ops.object.mode_set(mode='POSE')
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
        if pb.name in CHIBI: pb.scale = CHIBI[pb.name]
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    # bake the posed shape into the mesh
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = mesh; mesh.select_set(True)
    mod = next(m for m in mesh.modifiers if m.type == 'ARMATURE')
    name = mod.name
    bpy.ops.object.modifier_apply(modifier=name)
    # the pose becomes the rest pose
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    for b in rig.data.bones: b.inherit_scale = 'FULL'
    m2 = mesh.modifiers.new(name, 'ARMATURE'); m2.object = rig
    log("chibi proportions applied")


def cell_of(mat_name):
    if mat_name.startswith("MI_Ranger") or mat_name.startswith("MI_Peasant"): return "MI_Outfit"
    for k in ("MI_Superhero", "MI_Regular", "MI_Eyes", "MI_Hair_1", "MI_Hair_2"):
        if mat_name.startswith(k): return k
    return "MI_Outfit"


def recolour(px, hue_deg):
    """Rotate the hue of saturated green/teal cloth (leather, metal and skin stay as they are)."""
    if not hue_deg: return px
    rgb = px[..., :3]
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn + 1e-6
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60.0
    sat = d / (mx + 1e-6)
    mask = (sat > 0.28) & (h > 70) & (h < 175) & (mx > 0.06)
    h2 = (h + hue_deg) % 360
    c = mx * sat; x = c * (1 - np.abs((h2 / 60.0) % 2 - 1)); m = mx - c
    k = (h2 // 60).astype(int)
    rr = np.select([k == 0, k == 1, k == 2, k == 3, k == 4, k == 5], [c, x, 0, 0, x, c])
    gg = np.select([k == 0, k == 1, k == 2, k == 3, k == 4, k == 5], [x, c, c, x, 0, 0])
    bb = np.select([k == 0, k == 1, k == 2, k == 3, k == 4, k == 5], [0, 0, x, c, c, x])
    out = px.copy()
    for ch, v in enumerate((rr, gg, bb)): out[..., ch] = np.where(mask, v + m, rgb[..., ch])
    return out


def log(*a): print("[ranger]", *a, flush=True)


def imported(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def build(NAME, H):
    bpy.ops.wm.read_factory_settings(use_empty=True)

    # ---------------------------------------------------------------- outfit (keeps its armature = the rig)
    objs = imported(f"{OUTFITS}/Exports/FBX (Unity)/Outfits/{H['outfit']}.fbx")
    rig = next(o for o in objs if o.type == 'ARMATURE')
    rig.name = "Rig"
    meshes = [o for o in objs if o.type == 'MESH']
    log("outfit parts:", [m.name for m in meshes])

    # ---------------------------------------------------------------- head from the base character
    bobjs = imported(f"{BASE}/Base Characters/Unity/{H['base']}_FullBody.fbx")
    for hn in H["hair"]: bobjs += imported(f"{HAIR}/{hn}.fbx")
    brigs = [o for o in bobjs if o.type == 'ARMATURE']
    brig = brigs[0]
    bmeshes = [o for o in bobjs if o.type == 'MESH']
    body = next(o for o in bmeshes if o.name.lower().startswith("superhero"))
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
    for b in brigs: bpy.data.objects.remove(b, do_unlink=True)

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
            mat = m.material_slots[poly.material_index].material.name if m.material_slots else "MI_Outfit"
            key = cell_of(mat)
            x0, y0, s = CELLS[key]
            for li in poly.loop_indices:
                u, v = uvs[li].uv
                u -= np.floor(u) if (u < -0.01 or u > 1.01) else 0.0
                v -= np.floor(v) if (v < -0.01 or v > 1.01) else 0.0
                uvs[li].uv = (x0 + min(max(u, 0.0), 1.0) * s, y0 + min(max(v, 0.0), 1.0) * s)

    atlas = np.zeros((ATLAS, ATLAS, 4), dtype=np.float32); atlas[..., 3] = 1.0
    gender = "Female" if "Female" in H["base"] else "Male"
    tex_for = {"MI_Outfit": f"{OUTFITS}/Textures/{H['tex']}", "MI_Superhero": SKIN[f"MI_Superhero_{gender}"],
               "MI_Regular": SKIN[f"MI_Regular_{gender}"], "MI_Eyes": SKIN["MI_Eyes"], "MI_Hair_1": SKIN["MI_Hair_1"], "MI_Hair_2": SKIN["MI_Hair_2"]}
    for key, path in tex_for.items():
        x0, y0, s = CELLS[key]
        n = int(ATLAS * s)
        img = bpy.data.images.load(path)
        img.scale(n, n)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(n, n, 4)
        if key == "MI_Outfit": px = recolour(px, H["hue"])
        if key.startswith("MI_Hair") and H.get("hairTint"):   # the pack's hair textures are grey, meant to be tinted
            px[..., :3] *= np.array(H["hairTint"], dtype=np.float32) * 1.6
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
    chibify(rig, mesh)
    bpy.context.view_layer.update()
    zs = [(mesh.matrix_world @ v.co).z for v in mesh.data.vertices]
    s = TARGET_H[gender] / (max(zs) - min(zs))
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


for name, h in HEROES.items():
    if ONLY and name != ONLY: continue
    build(name, h)
