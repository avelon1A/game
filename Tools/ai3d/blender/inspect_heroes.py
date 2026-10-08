# Measures every hero (rest pose, normalised to the same 1.8 m height) for the RILO proportion standard + mesh health.
#   blender -b -P inspect_heroes.py -- <Characters dir> <out.json>
import bpy, bmesh, sys, os, json, math
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
res = {}
for key in ["vanguard", "volt", "lyra", "nova", "sol"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, key, key + ".fbx"))
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    if arm.animation_data: arm.animation_data.action = None
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    dg = bpy.context.evaluated_depsgraph_get()
    vs = []
    islands = []; tris = 0; nonman = 0; thin = 0; mats = set(); texres = []
    for m in meshes:
        me = m.evaluated_get(dg).to_mesh()
        vs += [m.matrix_world @ v.co for v in me.vertices]
        tris += sum(len(p.vertices) - 2 for p in me.polygons)
        for s in m.material_slots:
            if s.material:
                mats.add(s.material.name)
                if s.material.use_nodes:
                    for n in s.material.node_tree.nodes:
                        if n.type == 'TEX_IMAGE' and n.image: texres.append(list(n.image.size))
        bm = bmesh.new(); bm.from_mesh(m.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00005)
        nonman += sum(1 for e in bm.edges if not e.is_manifold)
        bm.verts.ensure_lookup_table()
        seen = set()
        for v0 in bm.verts:
            if v0.index in seen: continue
            stack = [v0]; seen.add(v0.index); comp = []
            while stack:
                v = stack.pop(); comp.append(v)
                for e in v.link_edges:
                    o = e.other_vert(v)
                    if o.index not in seen: seen.add(o.index); stack.append(o)
            co = [m.matrix_world @ v.co for v in comp]
            islands.append((len(comp), min(c.z for c in co), max(c.z for c in co)))
        bm.free()
    zs = [v.z for v in vs]; lo, hi = min(zs), max(zs); H = hi - lo
    k = 1.8 / H
    def bone(n):
        b = arm.data.bones.get("mixamorig:" + n)
        return (arm.matrix_world @ b.head_local) if b else None
    def tail(n):
        b = arm.data.bones.get("mixamorig:" + n)
        return (arm.matrix_world @ b.tail_local) if b else None
    hips, head, neck = bone("Hips"), bone("Head"), bone("Neck")
    ls, rs = bone("LeftArm"), bone("RightArm")
    le, lw, lh = bone("LeftForeArm"), bone("LeftHand"), bone("LeftHandMiddle1")
    lu, lk, la, lt = bone("LeftUpLeg"), bone("LeftLeg"), bone("LeftFoot"), bone("LeftToeBase")
    d = lambda a, b: (a - b).length * k if a is not None and b is not None else None
    # head size: mesh above the neck joint
    head_top = hi; head_h = (hi - neck.z) * k if neck else None
    # foot length: mesh extent near the floor
    foot = [v for v in vs if v.z < lo + 0.06 * H]
    foot_len = (max(v.y for v in foot) - min(v.y for v in foot)) * k if foot else None
    width = (max(v.x for v in vs) - min(v.x for v in vs)) * k
    depth = (max(v.y for v in vs) - min(v.y for v in vs)) * k
    small = [i for i in islands if i[0] < 60]
    res[key] = dict(
        height_raw=round(H, 3), tris=tris, materials=len(mats), textures=texres, islands=len(islands), small_islands=len(small),
        nonmanifold_edges=nonman, bones=len(arm.data.bones),
        head_height=round(head_h, 3) if head_h else None, heads_tall=round(1.8 / head_h, 2) if head_h else None,
        pelvis_height=round((hips.z - lo) * k, 3), shoulder_width=round(d(ls, rs), 3),
        upper_arm=round(d(ls, le), 3), forearm=round(d(le, lw), 3), hand=round(d(lw, tail("LeftHandMiddle3") or lh), 3) if lw else None,
        thigh=round(d(lu, lk), 3), shin=round(d(lk, la), 3), leg_total=round((lu.z - lo) * k, 3),
        torso=round(d(hips, neck), 3), neck_to_head=round(d(neck, head), 3), foot_len=round(foot_len, 3) if foot_len else None,
        bbox_width=round(width, 3), bbox_depth=round(depth, 3),
        hips_vs_spine_offset=round(((bone("Spine") or hips) - hips).y * k, 3),
    )
    print("[INSPECT]", key, json.dumps(res[key]), flush=True)
json.dump(res, open(OUT, "w"), indent=1)
