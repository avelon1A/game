# VEIL character builder for Blender (run headless):
#   blender -b -P build_character.py -- <name> [--preview]
# Pipeline: Hunyuan3D shape (GLB) -> cleanup -> decimate -> normalize -> vertex colours projected from the
# concept art (occlusion-aware, flood-filled to hidden areas, palette-quantised for a cel look) ->
# heuristic humanoid skeleton -> automatic skin weights -> keyframed animation set -> FBX for Unity.
import bpy, bmesh, sys, os, math, json
import numpy as np
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
NAME = argv[0]
PREVIEW = "--preview" in argv
HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = os.path.dirname(HERE)
SRC = os.path.join(TOOLS, "output", "local", f"{NAME}.glb")
if not os.path.exists(SRC):
    SRC = os.path.join(TOOLS, "output", "hunyuan", f"{NAME}.glb")
POSE = os.path.join(TOOLS, "joints", f"{NAME}.json")
ART = os.path.join(TOOLS, "masks", f"{NAME}.png")
OUT_DIR = os.path.join(TOOLS, "..", "..", "Client", "Assets", "Veil", "Characters", NAME)
RENDER_DIR = os.path.join(TOOLS, "renders")
# textured mode: a finished, already-textured model (Meshy / hand-made GLB) — keep its own UVs + texture,
# skip the concept-art cleanup / projection. Joints come from joints/<name>_model.json on its front render.
SRC_ARG = argv[argv.index("--src") + 1] if "--src" in argv else None
TEXTURED = SRC_ARG is not None
if TEXTURED:
    SRC = os.path.abspath(SRC_ARG)
    ART = os.path.join(TOOLS, "masks", f"{NAME}_model.png")
    POSE = os.path.join(TOOLS, "joints", f"{NAME}_model.json")
HEIGHT = 2.0
TARGET_FACES = 70000
FPS = 30


def log(*a):
    print("[veil]", *a, flush=True)


# ============================================================ import & cleanup
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in bpy.context.scene.objects:
    o.select_set(o.type == 'MESH')
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != obj:
        bpy.data.objects.remove(o, do_unlink=True)
obj.name = NAME
me = obj.data
log("imported", len(me.vertices), "verts", len(me.polygons), "faces")


def verts_np():
    co = np.zeros(len(me.vertices) * 3, dtype=np.float32)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


if not TEXTURED:  # AI shape output only: slabs, pedestals, floating islands
    # ground slab: a thin plate at the bottom that is wider than the feet
    co = verts_np()
    z0, z1 = co[:, 2].min(), co[:, 2].max()
    h = z1 - z0
    band0 = co[co[:, 2] < z0 + 0.012 * h]
    band1 = co[(co[:, 2] > z0 + 0.03 * h) & (co[:, 2] < z0 + 0.05 * h)]
    def area(b): return (np.ptp(b[:, 0]) * np.ptp(b[:, 1])) if len(b) > 10 else 0
    if area(band0) > 1.5 * max(area(band1), 1e-6):
        cut = z0 + 0.018 * h
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.bisect(plane_co=(0, 0, cut), plane_no=(0, 0, 1), clear_inner=True, use_fill=True)
        bpy.ops.object.mode_set(mode='OBJECT')
        log("removed ground slab below", round(float(cut), 3))

    # pedestal/ramp from scenery: keep only low geometry that lies under the shoe footprint
    co = verts_np()
    z0, z1 = co[:, 2].min(), co[:, 2].max(); h = z1 - z0
    ref = co[(co[:, 2] > z0 + 0.09 * h) & (co[:, 2] < z0 + 0.13 * h)][:, :2]
    low_idx = np.where(co[:, 2] < z0 + 0.09 * h)[0]
    if len(ref) and len(low_idx) > 0.08 * len(co):
        from mathutils.kdtree import KDTree
        kd = KDTree(len(ref))
        for i, p2 in enumerate(ref): kd.insert((p2[0], p2[1], 0), i)
        kd.balance()
        bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
        dead = [bm.verts[i] for i in low_idx if kd.find((co[i, 0], co[i, 1], 0))[2] > 0.06 * h]
        bmesh.ops.delete(bm, geom=dead, context='VERTS')
        bm.to_mesh(me); bm.free()
        log("pedestal filter removed", len(dead), "verts")

    # drop small floating islands
    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen, islands = set(), []
    for v in bm.verts:
        if v.index in seen: continue
        stack, isl = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop(); isl.append(x)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index); stack.append(o)
        islands.append(isl)
    islands.sort(key=len, reverse=True)
    kill = [v for isl in islands[1:] if len(isl) < 0.02 * len(bm.verts) for v in isl]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(me); bm.free()
    log("islands", len(islands), "removed verts", len(kill))

# decimate to a game budget
dec = obj.modifiers.new("dec", 'DECIMATE')
dec.ratio = min(1.0, TARGET_FACES / max(1, len(me.polygons)))
bpy.ops.object.modifier_apply(modifier="dec")
log("decimated to", len(me.polygons), "faces")

# normalise: feet on the ground, centred, HEIGHT tall, facing -Y (Blender front)
co = verts_np()
z0, z1 = co[:, 2].min(), co[:, 2].max()
low = co[co[:, 2] < z0 + 0.5 * (z1 - z0)]
cx, cy = float(np.median(low[:, 0])), float(np.median(low[:, 1]))
s = HEIGHT / (z1 - z0)
obj.location = (-cx * s, -cy * s, -z0 * s)
obj.scale = (s, s, s)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for p in me.polygons: p.use_smooth = True
co = verts_np()
log("normalised: x", co[:, 0].min().round(3), co[:, 0].max().round(3), "y", co[:, 1].min().round(3), co[:, 1].max().round(3))

# ============================================================ vertex colours from the concept art
import bpy_extras
img = bpy.data.images.load(ART)
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)[::-1]  # row 0 = top
alpha = px[:, :, 3]
ys, xs = np.where(alpha > 0.5)
ix0, ix1, iy0, iy1 = xs.min(), xs.max(), ys.min(), ys.max()

co = verts_np()
mx0, mx1, mz0, mz1 = co[:, 0].min(), co[:, 0].max(), co[:, 2].min(), co[:, 2].max()
nrm = np.zeros(len(me.vertices) * 3, dtype=np.float32)
me.vertices.foreach_get("normal", nrm)
nrm = nrm.reshape(-1, 3)
bvh = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())

n = len(co)
col = np.zeros((n, 3), dtype=np.float32)
known = np.zeros(n, dtype=bool)
view = Vector((0, -1, 0))  # towards the camera (front)
for i in range(n):
    if nrm[i, 1] > -0.04:  # not facing the camera enough
        continue
    p = Vector(co[i]) + view * 0.003
    hit = bvh.ray_cast(p, view, 10.0)
    if hit[0] is not None:
        continue
    u = (co[i, 0] - mx0) / (mx1 - mx0)
    v = (co[i, 2] - mz0) / (mz1 - mz0)
    x = int(round(ix0 + u * (ix1 - ix0)))
    y = int(round(iy1 - v * (iy1 - iy0)))
    x = min(max(x, 0), W - 1); y = min(max(y, 0), H - 1)
    if alpha[y, x] < 0.5:
        continue
    col[i] = px[y, x, :3]
    known[i] = True
log("projected colours on", int(known.sum()), "of", n, "verts")

if not TEXTURED:
    # ---- back view (optional reference art seen from behind): mirrored projection onto back-facing surfaces
    ART_B = os.path.join(TOOLS, "masks", f"{NAME}_back.png")
    back_known = np.zeros(n, dtype=bool)
    imgB = None
    if os.path.exists(ART_B):
        imgB = bpy.data.images.load(ART_B)
        WB, HB = imgB.size
        pxB = np.array(imgB.pixels[:], dtype=np.float32).reshape(HB, WB, 4)[::-1]
        alphaB = pxB[:, :, 3]
        ysb, xsb = np.where(alphaB > 0.5)
        bx0, bx1, by0, by1 = xsb.min(), xsb.max(), ysb.min(), ysb.max()
        viewB = Vector((0, 1, 0))
        for i in range(n):
            if nrm[i, 1] < 0.04:
                continue
            hit = bvh.ray_cast(Vector(co[i]) + viewB * 0.003, viewB, 10.0)
            if hit[0] is not None:
                continue
            u = (mx1 - co[i, 0]) / (mx1 - mx0)          # seen from behind: character's left is on the image left
            v = (co[i, 2] - mz0) / (mz1 - mz0)
            x = min(max(int(round(bx0 + u * (bx1 - bx0))), 0), WB - 1)
            y = min(max(int(round(by1 - v * (by1 - by0))), 0), HB - 1)
            if alphaB[y, x] < 0.5:
                continue
            col[i] = pxB[y, x, :3]
            known[i] = True
            back_known[i] = True
        log("back projection on", int(back_known.sum()), "verts")
    proj_known = known.copy()

    # palette quantisation (k-means) -> flat cartoon colours, removes baked-in shading noise
    K = 14
    samples = col[known]
    rng = np.random.default_rng(0)
    cent = samples[rng.choice(len(samples), K, replace=False)]
    for _ in range(20):
        d = ((samples[:, None, :] - cent[None, :, :]) ** 2).sum(-1)
        lab = d.argmin(1)
        for k in range(K):
            m = lab == k
            if m.any(): cent[k] = samples[m].mean(0)
    d = ((col[known][:, None, :] - cent[None, :, :]) ** 2).sum(-1)
    labels = np.full(n, -1, dtype=np.int32)
    labels[known] = d.argmin(1)

    # hidden (back/side) verts: sample the concept art near the silhouette edge at the same height.
    # The back of a jacket = the jacket colour seen at its edge; back of the head = hair/hood colour.
    row_segs = {}
    def segs(py_):
        if py_ in row_segs: return row_segs[py_]
        m = alpha[py_] > 0.5
        idx = np.flatnonzero(np.diff(np.concatenate([[0], m.astype(np.int8), [0]])))
        row_segs[py_] = list(zip(idx[0::2], idx[1::2] - 1))
        return row_segs[py_]
    edge_col = np.zeros((n, 3), dtype=np.float32)
    edge_ok = np.zeros(n, dtype=bool)
    # head: skip silhouette-edge sampling there. At cheek height the silhouette edge is hair/headphones, which
    # smeared hair colour over the side of the face. Hidden head verts instead take the nearest projected colour
    # over the surface (flood fill below), so cheeks continue as skin and hair continues as hair.
    head_py = iy0 + 0.40 * (iy1 - iy0)
    try:
        import json as _json
        _pj = _json.load(open(POSE))["joints"]
        head_py = min(_pj["shoulderL_img"][1], _pj["shoulderR_img"][1]) - 0.02 * (iy1 - iy0)
    except Exception:
        pass
    for i in range(n):
        if proj_known[i]: continue
        u = (co[i, 0] - mx0) / (mx1 - mx0); v = (co[i, 2] - mz0) / (mz1 - mz0)
        x = int(round(ix0 + u * (ix1 - ix0))); y = int(round(iy1 - v * (iy1 - iy0)))
        x = min(max(x, 0), W - 1); y = min(max(y, 0), H - 1)
        if y < head_py: continue
        sg = segs(y)
        if not sg: continue
        best_s = min(sg, key=lambda t: 0 if t[0] <= x <= t[1] else min(abs(x - t[0]), abs(x - t[1])))
        width = best_s[1] - best_s[0]
        inset = max(3, int(0.16 * width))
        side = 1 if x >= 0.5 * (best_s[0] + best_s[1]) else -1
        sx_ = best_s[1] - inset if side > 0 else best_s[0] + inset
        sx_ = min(max(sx_, best_s[0]), best_s[1])
        ya, yb = max(0, y - 10), min(H, y + 11)
        xa = max(best_s[0], min(sx_, sx_ - side * inset // 2) - 3)
        xb = min(best_s[1] + 1, max(sx_, sx_ - side * inset // 2) + 4)
        patch = px[ya:yb, xa:xb]
        pm = patch[patch[:, :, 3] > 0.5][:, :3] if patch.size else np.zeros((0, 3))
        edge_col[i] = np.median(pm, 0) if len(pm) else px[y, sx_, :3]
        edge_ok[i] = True
    if edge_ok.any():
        # diffuse colours over the surface in hidden regions (visible front stays fixed), then snap to the palette
        bm2 = bmesh.new(); bm2.from_mesh(me)
        ev = np.array([(e.verts[0].index, e.verts[1].index) for e in bm2.edges], dtype=np.int64); bm2.free()
        src = np.concatenate([ev[:, 0], ev[:, 1]]); dst = np.concatenate([ev[:, 1], ev[:, 0]])
        deg = np.bincount(dst, minlength=n).astype(np.float32) + 1
        cc = np.where(proj_known[:, None], cent[np.clip(labels, 0, K - 1)], edge_col).astype(np.float32)
        hidden = ~proj_known & edge_ok
        for _ in range(40):
            acc = cc.copy()
            np.add.at(acc, dst, cc[src])
            smooth = acc / deg[:, None]
            cc[hidden] = smooth[hidden]
        d = ((cc[hidden][:, None, :] - cent[None, :, :]) ** 2).sum(-1)
        labels[hidden] = d.argmin(1)
        known = proj_known | edge_ok
    log("edge-sampled hidden verts:", int(edge_ok.sum()))

    # flood-fill anything still unknown through mesh connectivity (geodesic nearest known colour)
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    from collections import deque
    nbrs = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
    bm.free()
    q = deque(i for i in range(n) if known[i])
    while q:
        i = q.popleft()
        for j in nbrs[i]:
            if not known[j]:
                known[j] = True
                labels[j] = labels[i]
                q.append(j)
    # majority (mode) filter: removes speckle noise, leaves clean cartoon colour regions
    for it in range(14):
        new = labels.copy()
        for i in range(n):
            if not nbrs[i] or (it >= 5 and proj_known[i]): continue
            cnt = np.bincount(labels[nbrs[i] + [i]], minlength=K)
            new[i] = int(cnt.argmax())
        labels = new
    col = cent[np.clip(labels, 0, K - 1)]
    # one smoothing pass to soften the seam between projected and filled regions
    attr = me.color_attributes.new("Col", 'BYTE_COLOR', 'POINT')
    rgba = np.concatenate([col, np.ones((n, 1), dtype=np.float32)], 1)
    attr.data.foreach_set("color_srgb", rgba.ravel())
    me.color_attributes.active_color = attr
    log("vertex colours done")

    # ---- texture bake: concept art projected on the front (full detail: eyes, logos) + flat colours elsewhere ----
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.scene.objects: o.select_set(o == obj)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004)
    bpy.ops.object.mode_set(mode='OBJECT')
    # give the head (face!) far more texels: scale head UV islands up, then repack everything
    HEAD_Z = mz0 + (iy1 - head_py) / (iy1 - iy0) * (mz1 - mz0)
    HEAD_UV_SCALE = 2.1
    bmu = bmesh.new(); bmu.from_mesh(me); bmu.faces.ensure_lookup_table()
    uvl = bmu.loops.layers.uv.active
    def _uv_key(l): return (round(l[uvl].uv.x, 6), round(l[uvl].uv.y, 6))
    face_isl = [-1] * len(bmu.faces); isls = []
    for f in bmu.faces:
        if face_isl[f.index] >= 0: continue
        stack = [f]; face_isl[f.index] = len(isls); members = []
        while stack:
            g = stack.pop(); members.append(g)
            for l in g.loops:
                for lo in l.edge.link_loops:
                    h_ = lo.face
                    if face_isl[h_.index] >= 0 or h_ is g: continue
                    # same island if the shared edge has matching UVs on both sides (not a seam)
                    a0, a1 = _uv_key(l), _uv_key(l.link_loop_next)
                    b0, b1 = _uv_key(lo), _uv_key(lo.link_loop_next)
                    if {a0, a1} == {b0, b1}:
                        face_isl[h_.index] = len(isls); stack.append(h_)
        isls.append(members)
    scaled = 0
    for members in isls:
        zc = sum(f.calc_center_median().z for f in members) / len(members)
        if zc < HEAD_Z: continue
        cu = sum((l[uvl].uv for f in members for l in f.loops), Vector((0, 0))) / sum(len(f.loops) for f in members)
        for f in members:
            for l in f.loops: l[uvl].uv = cu + (l[uvl].uv - cu) * HEAD_UV_SCALE
        scaled += 1
    bmu.to_mesh(me); bmu.free()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, scale=True, margin=0.003)
    bpy.ops.object.mode_set(mode='OBJECT')
    log("head UV islands scaled x", HEAD_UV_SCALE, ":", scaled, "of", len(isls))

    visf = np.zeros(n, dtype=np.float32)
    visf[proj_known] = np.clip((-nrm[proj_known, 1] - 0.22) / 0.3, 0, 1)
    for _ in range(2):
        visf = np.array([0.5 * visf[i] + 0.5 * (visf[nbrs[i]].mean() if nbrs[i] else visf[i]) for i in range(n)], dtype=np.float32)
    visb = np.zeros(n, dtype=np.float32)
    visb[back_known] = np.clip((nrm[back_known, 1] - 0.22) / 0.3, 0, 1)
    for _ in range(2):
        visb = np.array([0.5 * visb[i] + 0.5 * (visb[nbrs[i]].mean() if nbrs[i] else visb[i]) for i in range(n)], dtype=np.float32)
    visb_attr = me.color_attributes.new("VisB", 'FLOAT_COLOR', 'POINT')
    visb_attr.data.foreach_set("color", np.repeat(visb, 4).reshape(-1, 4).clip(0, 1).ravel())
    vis_attr = me.color_attributes.new("Vis", 'FLOAT_COLOR', 'POINT')
    vis_attr.data.foreach_set("color", np.repeat(visf, 4).reshape(-1, 4).clip(0, 1).ravel())

    TEX = 4096
    baked = bpy.data.images.new(f"{NAME}_albedo", TEX, TEX, alpha=False)
    bake_mat = bpy.data.materials.new("bake"); bake_mat.use_nodes = True
    nt = bake_mat.node_tree; N = nt.nodes; L = nt.links
    for nd in list(N): N.remove(nd)
    out_n = N.new("ShaderNodeOutputMaterial")
    emit = N.new("ShaderNodeEmission")
    geo = N.new("ShaderNodeNewGeometry")
    sep = N.new("ShaderNodeSeparateXYZ"); L.new(geo.outputs["Position"], sep.inputs[0])
    A = (ix1 - ix0) / (W * (mx1 - mx0)); B = (ix0 - mx0 * (ix1 - ix0) / (mx1 - mx0)) / W
    C = (iy1 - iy0) / ((mz1 - mz0) * H); D = 1 - iy1 / H - mz0 * C
    def madd(inp, a, b):
        m = N.new("ShaderNodeMath"); m.operation = 'MULTIPLY_ADD'
        L.new(inp, m.inputs[0]); m.inputs[1].default_value = a; m.inputs[2].default_value = b
        return m.outputs[0]
    comb = N.new("ShaderNodeCombineXYZ")
    L.new(madd(sep.outputs["X"], A, B), comb.inputs["X"]); L.new(madd(sep.outputs["Z"], C, D), comb.inputs["Y"])
    art = N.new("ShaderNodeTexImage"); art.image = img; art.extension = 'CLIP'; art.interpolation = 'Cubic'
    L.new(comb.outputs[0], art.inputs["Vector"])
    vcn = N.new("ShaderNodeVertexColor"); vcn.layer_name = "Col"
    visn = N.new("ShaderNodeVertexColor"); visn.layer_name = "Vis"
    fac = N.new("ShaderNodeMath"); fac.operation = 'MULTIPLY'
    L.new(visn.outputs["Color"], fac.inputs[0]); L.new(art.outputs["Alpha"], fac.inputs[1])
    mix = N.new("ShaderNodeMix"); mix.data_type = 'RGBA'
    rgba_in = [x for x in mix.inputs if x.type == 'RGBA']
    base_col = vcn.outputs["Color"]
    if imgB is not None:
        A2 = -(bx1 - bx0) / ((mx1 - mx0) * WB); B2 = (bx0 + mx1 * (bx1 - bx0) / (mx1 - mx0)) / WB
        C2 = (by1 - by0) / ((mz1 - mz0) * HB); D2 = 1 - by1 / HB - mz0 * C2
        combB = N.new("ShaderNodeCombineXYZ")
        L.new(madd(sep.outputs["X"], A2, B2), combB.inputs["X"]); L.new(madd(sep.outputs["Z"], C2, D2), combB.inputs["Y"])
        artB = N.new("ShaderNodeTexImage"); artB.image = imgB; artB.extension = 'CLIP'; artB.interpolation = 'Cubic'
        L.new(combB.outputs[0], artB.inputs["Vector"])
        visbn = N.new("ShaderNodeVertexColor"); visbn.layer_name = "VisB"
        facB = N.new("ShaderNodeMath"); facB.operation = 'MULTIPLY'
        L.new(visbn.outputs["Color"], facB.inputs[0]); L.new(artB.outputs["Alpha"], facB.inputs[1])
        mixB = N.new("ShaderNodeMix"); mixB.data_type = 'RGBA'
        rgbB = [x for x in mixB.inputs if x.type == 'RGBA']
        L.new(facB.outputs[0], mixB.inputs["Factor"]); L.new(vcn.outputs["Color"], rgbB[0]); L.new(artB.outputs["Color"], rgbB[1])
        base_col = [x for x in mixB.outputs if x.type == 'RGBA'][0]
    L.new(fac.outputs[0], mix.inputs["Factor"]); L.new(base_col, rgba_in[0]); L.new(art.outputs["Color"], rgba_in[1])
    L.new([x for x in mix.outputs if x.type == 'RGBA'][0], emit.inputs["Color"])
    L.new(emit.outputs[0], out_n.inputs["Surface"])
    target = N.new("ShaderNodeTexImage"); target.image = baked
    N.active = target
    me.materials.clear(); me.materials.append(bake_mat)
    scene_ = bpy.context.scene
    scene_.render.engine = 'CYCLES'
    scene_.cycles.samples = 1
    scene_.cycles.device = 'CPU'
    bpy.ops.object.bake(type='EMIT', margin=12, use_clear=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    tex_path = os.path.abspath(os.path.join(OUT_DIR, f"{NAME}_albedo.png"))
    baked.filepath_raw = tex_path; baked.file_format = 'PNG'; baked.save()
    log("baked texture", tex_path)

    GLOW_F = os.path.join(TOOLS, "masks", f"{NAME}_glow.png")
    GLOW_B = os.path.join(TOOLS, "masks", f"{NAME}_back_glow.png")
    if os.path.exists(GLOW_F):
        black = N.new("ShaderNodeRGB"); black.outputs[0].default_value = (0, 0, 0, 1)
        art.image = bpy.data.images.load(GLOW_F)
        L.new(black.outputs[0], rgba_in[0])
        if imgB is not None and os.path.exists(GLOW_B):
            artB.image = bpy.data.images.load(GLOW_B)
            L.new(black.outputs[0], rgbB[0])
            L.new([x for x in mixB.outputs if x.type == 'RGBA'][0], rgba_in[0])
        emis = bpy.data.images.new(f"{NAME}_emission", TEX // 2, TEX // 2, alpha=False)
        target.image = emis
        N.active = target
        bpy.ops.object.bake(type='EMIT', margin=8, use_clear=True)
        emis_path = os.path.abspath(os.path.join(OUT_DIR, f"{NAME}_emission.png"))
        emis.filepath_raw = emis_path; emis.file_format = 'PNG'; emis.save()
        log("baked emission", emis_path)

    mat = bpy.data.materials.new(f"{NAME}_mat")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = baked
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    me.materials.clear(); me.materials.append(mat)
else:
    # ---- keep the model's own multi-view texture; derive a soft neon glow map from its bright cyan/blue parts
    src_img = None
    for m_ in me.materials:
        if not m_ or not m_.use_nodes: continue
        for nd in m_.node_tree.nodes:
            if nd.type == 'TEX_IMAGE' and nd.image and any(l.to_socket.name == 'Base Color' for l in nd.outputs[0].links):
                src_img = nd.image
    if src_img is None: raise SystemExit("textured model has no base colour texture")
    TW, TH = src_img.size
    tpx = np.array(src_img.pixels[:], dtype=np.float32).reshape(TH, TW, 4)
    os.makedirs(OUT_DIR, exist_ok=True)
    baked = bpy.data.images.new(f"{NAME}_albedo", TW, TH, alpha=False)
    baked.pixels = tpx.ravel()
    tex_path = os.path.abspath(os.path.join(OUT_DIR, f"{NAME}_albedo.png"))
    baked.filepath_raw = tex_path; baked.file_format = 'PNG'; baked.save()
    log("kept model texture", TW, "x", TH, "->", tex_path)
    rgb = tpx[:, :, :3]
    mxc = rgb.max(2); mnc = rgb.min(2)
    sat = np.where(mxc > 1e-4, (mxc - mnc) / np.maximum(mxc, 1e-4), 0)
    r_, g_, b_ = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    GLOW = argv[argv.index("--glow") + 1] if "--glow" in argv else "cyan"
    if GLOW == "pink":     # neon magenta rings / trims, strict so pink hair doesn't glow
        neon = (sat > 0.65) & (mxc > 0.8) & (r_ > g_ * 1.5) & (b_ > g_)
    elif GLOW == "none":
        neon = np.zeros(mxc.shape, dtype=bool)
    else:                  # cyan / electric blue
        neon = (sat > 0.55) & (mxc > 0.55) & (b_ >= r_ * 1.6) & (g_ > r_)
    glow = np.zeros_like(tpx); glow[:, :, 3] = 1
    glow[:, :, :3] = rgb * neon[:, :, None] * 0.55
    emis = bpy.data.images.new(f"{NAME}_emission", TW, TH, alpha=False)
    emis.pixels = glow.ravel()
    emis_path = os.path.abspath(os.path.join(OUT_DIR, f"{NAME}_emission.png"))
    emis.filepath_raw = emis_path; emis.file_format = 'PNG'; emis.save()
    log("glow map", round(float(neon.mean()) * 100, 1), "% of texels ->", emis_path)
    head_py = iy0 + 0.40 * (iy1 - iy0)
    try:
        _pj = json.load(open(POSE))["joints"]
        head_py = min(_pj["shoulderL_img"][1], _pj["shoulderR_img"][1]) - 0.02 * (iy1 - iy0)
    except Exception:
        pass
    HEAD_Z = mz0 + (iy1 - head_py) / (iy1 - iy0) * (mz1 - mz0)
    mat = bpy.data.materials.new(f"{NAME}_mat")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = baked
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    me.materials.clear(); me.materials.append(mat)


# ============================================================ skeleton (heuristic humanoid)
co = verts_np()
Hh = HEIGHT

def slice_(z0, z1):
    return co[(co[:, 2] >= z0) & (co[:, 2] < z1)]

# legs: two foot clusters near the ground -> midline between them -> crotch where that gap closes
feet = slice_(0.04 * Hh, 0.11 * Hh)
fx = np.sort(feet[:, 0])
gaps = np.diff(fx)
kgap = int(np.argmax(gaps)) if len(gaps) else 0
mid = float(0.5 * (fx[kgap] + fx[kgap + 1])) if len(gaps) and gaps[kgap] > 0.01 else 0.0
crotch = 0.3 * Hh
for z in np.arange(0.12 * Hh, 0.5 * Hh, 0.01):
    s_ = slice_(z, z + 0.01)
    if len(s_) and (np.abs(s_[:, 0] - mid) < 0.02 * Hh).sum() > 3:
        crotch = float(z); break
crotch = float(np.clip(crotch, 0.24 * Hh, 0.36 * Hh))
if os.path.exists(POSE):
    import json as _json
    _pj = _json.load(open(POSE))["joints"]
    hy_img = 0.5 * (_pj["hipL_img"][1] + _pj["hipR_img"][1])
    hz = mz0 + (iy1 - hy_img) / (iy1 - iy0) * (mz1 - mz0)
    if 0.2 * Hh < hz < 0.42 * Hh:
        crotch = float(hz - 0.03)
        log("crotch from pose:", round(crotch, 3))
def leg_centroid(z0_, z1_, sgn):
    b_ = slice_(z0_, z1_)
    b_ = b_[(b_[:, 0] - mid) * sgn > 0.01]
    return (float(np.median(b_[:, 0])), float(np.median(b_[:, 1]))) if len(b_) > 5 else (mid + sgn * 0.12, 0.0)
LEG = {}
for side, sgn in (("L", 1), ("R", -1)):
    hx, hy = leg_centroid(crotch - 0.08, crotch - 0.02, sgn)
    kz = 0.5 * (crotch + 0.09 * Hh)
    kx, ky = leg_centroid(kz - 0.03, kz + 0.03, sgn)
    ax_, ay_ = leg_centroid(0.08 * Hh, 0.11 * Hh, sgn)
    LEG[side] = (Vector((hx, hy, crotch)), Vector((kx, ky, kz)), Vector((ax_, ay_, 0.085 * Hh)))
legL, legR = LEG["L"][0].x, LEG["R"][0].x

# neck: narrowest slice in the upper-middle band (between chest and head)
best, neck = 1e9, 0.56 * Hh
for z in np.arange(0.46 * Hh, 0.66 * Hh, 0.01):
    s_ = slice_(z, z + 0.012)
    if len(s_) < 5: continue
    w = np.ptp(s_[:, 0])
    if w < best: best, neck = w, float(z)

# arms: at mid-torso height find the outer clusters separated by a gap from the torso
def arm_x(side):
    zmid = crotch + 0.35 * (neck - crotch)
    s_ = slice_(zmid - 0.04, zmid + 0.04)
    xs_ = np.sort(s_[:, 0] * side)
    xs_ = xs_[xs_ > 0]
    if len(xs_) < 10: return 0.3 * side
    gaps = np.diff(xs_)
    k = int(np.argmax(gaps))
    if gaps[k] > 0.015 * Hh:
        return float(np.median(xs_[k + 1:])) * side
    return float(xs_[-1] - 0.05) * side
armL, armR = arm_x(1), arm_x(-1)

def hand_z(ax):
    side = co[np.abs(co[:, 0] - ax) < 0.06 * Hh]
    side = side[side[:, 2] > 0.12 * Hh]
    return float(side[:, 2].min()) + 0.03 * Hh if len(side) else crotch

handLz, handRz = hand_z(armL), hand_z(armR)
shoulder_z = neck - 0.06 * Hh
shoulder_x = 0.85
head_top = float(co[:, 2].max())
foot_front = float(co[co[:, 2] < 0.08 * Hh][:, 1].min())

J = {
    "hips": Vector((0, 0, crotch + 0.04 * Hh)),
    "spine": Vector((0, 0, crotch + 0.12 * Hh)),
    "chest": Vector((0, 0, crotch + 0.55 * (neck - crotch))),
    "neck": Vector((0, 0, neck)),
    "head": Vector((0, 0, neck + 0.05 * Hh)),
    "top": Vector((0, 0, head_top)),
}
for side, sx, ax, hz in (("L", 1, armL, handLz), ("R", -1, armR, handRz)):
    sh = Vector((ax * 0.8, 0, shoulder_z))
    hand = Vector((ax, 0, hz + 0.03 * Hh))
    J["shoulder" + side] = Vector((sx * 0.08 * Hh, 0, shoulder_z + 0.01))
    J["upperarm" + side] = sh
    J["elbow" + side] = sh.lerp(hand, 0.5) + Vector((0, 0.01, 0))
    J["hand" + side] = hand
    J["handtip" + side] = hand + Vector((0, 0, -0.06 * Hh))
    hip_, knee_, ankle_ = LEG[side]
    J["hip" + side] = hip_.copy()
    J["knee" + side] = knee_.copy()
    J["ankle" + side] = ankle_.copy()
    J["toe" + side] = Vector((ankle_.x, foot_front + 0.04, 0.03 * Hh))
# ---- refine with 2D pose from the concept art (arms) + depth centring via ray casts ----
import json
def img_to_world(px_, py_):
    return (mx0 + (px_ - ix0) / (ix1 - ix0) * (mx1 - mx0), mz0 + (iy1 - py_) / (iy1 - iy0) * (mz1 - mz0))

def depth_mid(x, z, fallback=0.0):
    a = bvh.ray_cast(Vector((x, -5, z)), Vector((0, 1, 0)), 10)
    b = bvh.ray_cast(Vector((x, 5, z)), Vector((0, -1, 0)), 10)
    if a[0] is None or b[0] is None: return fallback
    return 0.5 * (a[0].y + b[0].y)

used_pose = False
if os.path.exists(POSE):
    pj = json.load(open(POSE))["joints"]
    for side, sx in (("L", 1), ("R", -1)):
        sh = img_to_world(*pj[f"shoulder{side}_img"][:2])
        el = img_to_world(*pj[f"elbow{side}_img"][:2])
        wr = img_to_world(*pj[f"wrist{side}_img"][:2])
        ok = wr[1] < el[1] < sh[1] and sh[0] * sx > 0.05 and wr[0] * sx > 0.05 and (sh[1] - wr[1]) > 0.15
        if not ok:
            log("pose arms rejected for", side); continue
        used_pose = True
        J["upperarm" + side] = Vector((sh[0] * 0.92, 0, sh[1] - 0.02))
        J["elbow" + side] = Vector((el[0], 0, el[1]))
        J["hand" + side] = Vector((wr[0], 0, wr[1]))
        d = (J["hand" + side] - J["elbow" + side]).normalized()
        J["handtip" + side] = J["hand" + side] + d * 0.12
        J["shoulder" + side] = Vector((sx * 0.06, 0, J["upperarm" + side].z + 0.02))
        pass  # legs come from mesh analysis
    J["neck"].z = max(J["neck"].z, max(J["upperarmL"].z, J["upperarmR"].z) + 0.04)
    J["head"].z = J["neck"].z + 0.05 * Hh
    J["chest"].z = min(J["chest"].z, J["neck"].z - 0.12)
# every joint sits in the middle of the body part's depth (not at y=0)
for k, v in J.items():
    if k.startswith("toe") or k[:3] in ("hip", "kne", "ank"): continue
    v.y = depth_mid(v.x, v.z, v.y)
for side in ("L", "R"):
    J["knee" + side].y -= 0.02  # tiny forward bend so knees always fold the right way
    J["elbow" + side].y += 0.02
log("pose refinement:", "used" if used_pose else "not available")
log("legs: mid", round(mid, 3), "hipL", tuple(round(v, 2) for v in J["hipL"]), "kneeL", tuple(round(v, 2) for v in J["kneeL"]), "ankleL", tuple(round(v, 2) for v in J["ankleL"]))
log("joints: crotch", round(crotch, 3), "neck", round(neck, 3), "arms", round(armL, 3), round(armR, 3), "hands z", round(handLz, 3), round(handRz, 3))

arm_data = bpy.data.armatures.new(f"{NAME}_rig")
rig = bpy.data.objects.new(f"{NAME}_rig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones

def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head, b.tail = J[head] if isinstance(head, str) else head, J[tail] if isinstance(tail, str) else tail
    if parent: b.parent = eb[parent]; b.use_connect = connect
    b.roll = 0
    return b

bone("Hips", "hips", "spine")
bone("Spine", "spine", "chest", "Hips", True)
bone("Chest", "chest", "neck", "Spine", True)
bone("Neck", "neck", "head", "Chest", True)
bone("Head", "head", "top", "Neck", True)
for s in ("L", "R"):
    bone(f"Shoulder.{s}", J["neck"] + Vector((0, 0, -0.04 * Hh)), f"upperarm{s}", "Chest")
    bone(f"UpperArm.{s}", f"upperarm{s}", f"elbow{s}", f"Shoulder.{s}", True)
    bone(f"LowerArm.{s}", f"elbow{s}", f"hand{s}", f"UpperArm.{s}", True)
    bone(f"Hand.{s}", f"hand{s}", f"handtip{s}", f"LowerArm.{s}", True)
    bone(f"UpperLeg.{s}", f"hip{s}", f"knee{s}", "Hips")
    bone(f"LowerLeg.{s}", f"knee{s}", f"ankle{s}", f"UpperLeg.{s}", True)
    bone(f"Foot.{s}", f"ankle{s}", f"toe{s}", f"LowerLeg.{s}", True)
bpy.ops.object.mode_set(mode='OBJECT')

# automatic (bone heat) weights
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True); rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
unweighted = sum(1 for v in me.vertices if not any(g.weight > 0.01 for g in v.groups))
log("skinned; unweighted verts:", unweighted)
if unweighted > 0.02 * len(me.vertices):
    # Meshes built from many separate pieces (layered hair / clothes) break bone-heat. Solve heat weights on a
    # watertight voxel-remeshed proxy instead, then transfer them to the real mesh (nearest surface, interpolated).
    log("heat weights incomplete -> solving on a watertight proxy")
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.duplicate(linked=False)
    proxy = bpy.context.view_layer.objects.active
    proxy.parent = None
    for md in list(proxy.modifiers): proxy.modifiers.remove(md)
    proxy.vertex_groups.clear()
    rm = proxy.modifiers.new("remesh", 'REMESH'); rm.mode = 'VOXEL'; rm.voxel_size = 0.018; rm.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier="remesh")
    dm_ = proxy.modifiers.new("dec", 'DECIMATE'); dm_.ratio = min(1.0, 45000 / max(1, len(proxy.data.polygons)))
    bpy.ops.object.modifier_apply(modifier="dec")
    bpy.ops.object.select_all(action='DESELECT')
    proxy.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    pu = sum(1 for v in proxy.data.vertices if not any(g.weight > 0.01 for g in v.groups))
    log("proxy", len(proxy.data.polygons), "faces, unweighted", pu)
    for g in list(obj.vertex_groups): obj.vertex_groups.remove(g)
    for b in rig.data.bones: obj.vertex_groups.new(name=b.name)
    dt = obj.modifiers.new("wt", 'DATA_TRANSFER')
    dt.object = proxy
    dt.use_vert_data = True
    dt.data_types_verts = {'VGROUP_WEIGHTS'}
    dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src = 'ALL'
    dt.layers_vgroup_select_dst = 'NAME'
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    while obj.modifiers[0].name != "wt": bpy.ops.object.modifier_move_up(modifier="wt")
    bpy.ops.object.modifier_apply(modifier="wt")
    bpy.data.objects.remove(proxy, do_unlink=True)
    unweighted = sum(1 for v in me.vertices if not any(g.weight > 0.01 for g in v.groups))
    log("weights transferred from proxy; unweighted verts:", unweighted)

# ---- weight cleanup: fists rest on the hips in the concept pose, so heat weights glue hip/pouch
# geometry to the hands. Arm bones may only influence vertices close to the arm chain and lateral to the torso.
def seg_dist(p, a_, b_):
    ab = b_ - a_
    t = max(0.0, min(1.0, (p - a_).dot(ab) / max(ab.length_squared, 1e-8)))
    return (p - (a_ + ab * t)).length
arm_groups = {}
for sd in ("L", "R"):
    chain = [(J["upperarm" + sd], J["elbow" + sd]), (J["elbow" + sd], J["hand" + sd]), (J["hand" + sd], J["handtip" + sd])]
    for gname in (f"UpperArm.{sd}", f"LowerArm.{sd}", f"Hand.{sd}"):
        arm_groups[obj.vertex_groups[gname].index] = (sd, chain)
removed = 0
body_chain = [(J["hips"], J["chest"]), (J["hipL"], J["kneeL"]), (J["hipR"], J["kneeR"]), (J["hipL"], J["hipR"])]
body_fallback = obj.vertex_groups["Hips"].index
for v in me.vertices:
    p = v.co
    for g in list(v.groups):
        if g.group not in arm_groups or g.weight <= 0: continue
        sd, chain = arm_groups[g.group]
        sx = 1 if sd == "L" else -1
        dmin = min(seg_dist(p, a_, b_) for a_, b_ in chain)
        medial = p.x * sx < abs(J["upperarm" + sd].x) * 0.55
        # below the chest, geometry nearer the hips / legs than the arm (skirts, pouches beside the fists) stays with the body
        dbody = min(seg_dist(p, a_, b_) for a_, b_ in body_chain) if p.z < J["chest"].z else 9
        if dmin > 0.17 or (medial and p.z < J["upperarm" + sd].z - 0.05) or dbody < dmin * 1.15:
            obj.vertex_groups[g.group].remove([v.index]); removed += 1
    if not any(g.weight > 0.001 for g in v.groups):
        # give orphaned vertices to the nearest torso/leg bone by height
        gn = "Hips" if p.z < J["spine"].z else ("Spine" if p.z < J["chest"].z else "Chest")
        obj.vertex_groups[gn].add([v.index], 1.0, 'REPLACE')
# ---- rigid pieces: a small separate mesh piece (strap, pouch, glove shell) must move as ONE part — either with
# the arm or with the body. Half-and-half pieces stretch into long ribbons when the arm moves.
bm_i = bmesh.new(); bm_i.from_mesh(me); bm_i.verts.ensure_lookup_table()
seen_i = [False] * len(bm_i.verts); pieces = []
for v0 in bm_i.verts:
    if seen_i[v0.index]: continue
    stack = [v0]; seen_i[v0.index] = True; comp = []
    while stack:
        x = stack.pop(); comp.append(x.index)
        for e in x.link_edges:
            o_ = e.other_vert(x)
            if not seen_i[o_.index]: seen_i[o_.index] = True; stack.append(o_)
    pieces.append(comp)
bm_i.free()
arm_idx = set(arm_groups.keys())
fixed = 0
for comp in pieces:
    if len(comp) > 0.06 * len(me.vertices) or len(comp) < 3: continue
    arm_w = []
    for vi in comp:
        gs = me.vertices[vi].groups
        tot = sum(g.weight for g in gs) or 1
        arm_w.append(sum(g.weight for g in gs if g.group in arm_idx) / tot)
    mean = sum(arm_w) / len(arm_w)
    if max(arm_w) < 0.05: continue
    if mean < 0.5:   # mostly body → strip the arm entirely
        for vi in comp:
            for g in list(me.vertices[vi].groups):
                if g.group in arm_idx: obj.vertex_groups[g.group].remove([vi])
            if not any(g.weight > 0.001 for g in me.vertices[vi].groups):
                pz = me.vertices[vi].co.z
                obj.vertex_groups["Hips" if pz < J["spine"].z else ("Spine" if pz < J["chest"].z else "Chest")].add([vi], 1.0, 'REPLACE')
    else:            # mostly arm → the whole piece follows its dominant arm bone
        cnt = {}
        for vi in comp:
            for g in me.vertices[vi].groups:
                if g.group in arm_idx: cnt[g.group] = cnt.get(g.group, 0) + g.weight
        dom = max(cnt, key=cnt.get)
        for vi in comp:
            for g in list(me.vertices[vi].groups): obj.vertex_groups[g.group].remove([vi])
            obj.vertex_groups[dom].add([vi], 1.0, 'REPLACE')
    fixed += 1
log("rigid pieces fixed:", fixed, "of", len(pieces), "pieces")
bpy.context.view_layer.objects.active = obj
for o in bpy.context.scene.objects: o.select_set(o == obj)
bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
bpy.ops.object.vertex_group_smooth(group_select_mode='ALL', factor=0.5, repeat=2)
bpy.ops.object.vertex_group_normalize_all(lock_active=False)
bpy.ops.object.mode_set(mode='OBJECT')
log("weight cleanup: removed", removed, "arm influences")

# ============================================================ animation
scene = bpy.context.scene
scene.render.fps = FPS
pb = rig.pose.bones
for b in pb: b.rotation_mode = 'QUATERNION'
REST = {b.name: b.bone.matrix_local.to_3x3() for b in pb}

# ---- neutral pose: the mesh was generated in the concept's hero stance (legs wide, fists on hips).
# Gameplay animations are built on a relaxed neutral pose instead: legs straight under the hips,
# feet forward, arms hanging at the sides. Only the "lobby" clip keeps the original stance.
NEUTRAL_TARGET = {
    "UpperLeg.L": Vector((0.03, 0.0, -1)), "UpperLeg.R": Vector((-0.03, 0.0, -1)),
    "LowerLeg.L": Vector((0.0, 0.03, -1)), "LowerLeg.R": Vector((0.0, 0.03, -1)),
    "Foot.L": Vector((0.06, -1, -0.25)), "Foot.R": Vector((-0.06, -1, -0.25)),
    "UpperArm.L": Vector((0.30, 0.03, -1)), "UpperArm.R": Vector((-0.30, 0.03, -1)),
    "LowerArm.L": Vector((0.10, -0.22, -1)), "LowerArm.R": Vector((-0.10, -0.22, -1)),
}
# legs: aim each straight leg at a natural foot spot just inside the hip line (chibi hips are wide)
for side, sgn in (("L", 1), ("R", -1)):
    hip_ = J["hip" + side]
    off = float(np.clip(abs(hip_.x - mid) * 0.7, 0.09, 0.16))
    foot_spot = Vector((mid + sgn * off, hip_.y, J["ankle" + side].z))
    d_ = (foot_spot - hip_).normalized()
    NEUTRAL_TARGET["UpperLeg." + side] = d_ + Vector((0, -0.04, 0))
    NEUTRAL_TARGET["LowerLeg." + side] = d_ + Vector((0, 0.04, 0))
    NEUTRAL_TARGET.pop("Foot." + side, None)
KEEP_REST_ORIENT = {"Foot.L", "Foot.R"}   # sneakers keep their modelled orientation
NABS, NLOC = {}, {}
for b in pb:  # parents come before children (creation order)
    parentN = NABS[b.parent.name] if b.parent else Quaternion()
    if b.name in KEEP_REST_ORIENT:
        NABS[b.name] = Quaternion()
    elif b.name in NEUTRAL_TARGET:
        cur = parentN @ (b.bone.tail_local - b.bone.head_local)
        NABS[b.name] = cur.normalized().rotation_difference(NEUTRAL_TARGET[b.name].normalized()) @ parentN
    else:
        NABS[b.name] = parentN
    rel = parentN.inverted() @ NABS[b.name]
    R = REST[b.name]
    NLOC[b.name] = (R.inverted() @ rel.to_matrix() @ R).to_quaternion()
USE_NEUTRAL = [True]
if "--debug-neutral" in argv:
    for bb in pb: bb.rotation_quaternion = NLOC[bb.name]
    bpy.context.view_layer.update()
    for bb in pb:
        if bb.name in NEUTRAL_TARGET:
            d = (bb.tail - bb.head).normalized()
            log(f"neutral {bb.name}: rest {tuple(round(v,2) for v in (bb.bone.tail_local - bb.bone.head_local).normalized())} posed {tuple(round(v,2) for v in d)} target {tuple(round(v,2) for v in NEUTRAL_TARGET[bb.name].normalized())}")
    for bb in pb: bb.rotation_quaternion = Quaternion()

def rot_world(bname, axis, deg):
    """Rotation of a pose bone about an armature-space axis, expressed in the bone's rest frame."""
    a = (REST[bname].inverted() @ Vector(axis)).normalized()
    return Quaternion(a, math.radians(deg))

# armature space: character faces -Y. Positive rotation about +X swings a hanging limb BACKWARD (+Y),
# so "forward swing" is negative. Abduction of the left arm (+X side) is rotation about +Y (negative moves it out).
def pose_frame(frame, spec):
    """spec: bone -> list of (axis, degrees) applied in order; plus optional ('loc', (x,y,z)) for Hips."""
    for b in pb:
        q = Quaternion()
        loc = Vector()
        for item in spec.get(b.name, []):
            if item[0] == "loc": loc = Vector(item[1])
            else: q = rot_world(b.name, item[0], item[1]) @ q
        b.rotation_quaternion = (q @ NLOC[b.name]) if USE_NEUTRAL[0] else q
        b.location = REST[b.name].inverted() @ loc if b.name == "Hips" else Vector()
        b.keyframe_insert("rotation_quaternion", frame=frame)
        b.keyframe_insert("location", frame=frame)

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)

def gait(t, amp_leg, amp_knee, amp_arm, lean, bob, elbow):
    s, c = math.sin(t), math.cos(t)
    spec = {
        "Hips": [("loc", (0, 0, abs(c) * bob - bob * 0.5)), (Z, s * 8)],
        "Spine": [(X, -lean * 0.5)],
        "Chest": [(X, -lean * 0.5), (Z, -s * 7)],
        "Neck": [(X, lean * 0.6)],
        "UpperLeg.L": [(X, -s * amp_leg)], "UpperLeg.R": [(X, s * amp_leg)],
        "LowerLeg.L": [(X, max(0, c) * amp_knee + 6)], "LowerLeg.R": [(X, max(0, -c) * amp_knee + 6)],
        "Foot.L": [(X, s * amp_leg * 0.3 - max(0, c) * amp_knee * 0.3)], "Foot.R": [(X, -s * amp_leg * 0.3 - max(0, -c) * amp_knee * 0.3)],
        "UpperArm.L": [(Y, 4), (X, s * amp_arm)], "UpperArm.R": [(Y, -4), (X, -s * amp_arm)],
        "LowerArm.L": [(X, -elbow)], "LowerArm.R": [(X, -elbow)],
    }
    return spec

def make_action(name, frames, fn, loop=True, neutral=True):
    USE_NEUTRAL[0] = neutral
    act = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = act
    for f in range(frames + 1):
        t = f / frames
        pose_frame(f + 1, fn(t))
    act.frame_range = (1, frames + 1)
    act.use_fake_user = True
    if loop:
        for fc in getattr(act, "fcurves", []):
            fc.modifiers.new('CYCLES')
    return act

def cycle(duration):
    return max(8, int(round(duration * FPS)))

acts = []
# lobby / portrait / podium: the concept-art hero stance, breathing
acts.append(make_action("lobby", cycle(2.4), lambda t: {
    "Hips": [("loc", (0, 0, math.sin(t * 2 * math.pi) * 0.008))],
    "Chest": [(X, math.sin(t * 2 * math.pi) * 2)],
    "Neck": [(Z, math.sin(t * 2 * math.pi) * 3)],
    "UpperArm.L": [(Y, -3 - math.sin(t * 2 * math.pi) * 2)], "UpperArm.R": [(Y, 3 + math.sin(t * 2 * math.pi) * 2)],
}, neutral=False))
# gameplay idle: relaxed, natural standing on the neutral pose
acts.append(make_action("idle", cycle(2.6), lambda t: {
    "Hips": [("loc", (0, 0, math.sin(t * 2 * math.pi) * 0.01)), (Z, math.sin(t * 2 * math.pi) * 2)],
    "Chest": [(X, math.sin(t * 2 * math.pi) * 2.5)],
    "Neck": [(Z, math.sin(t * 2 * math.pi) * 4), (X, -2)],
    "UpperArm.L": [(Y, -math.sin(t * 2 * math.pi) * 3), (X, 3)], "UpperArm.R": [(Y, math.sin(t * 2 * math.pi) * 3), (X, 3)],
    "LowerArm.L": [(X, -10)], "LowerArm.R": [(X, -10)],
    "UpperLeg.L": [(Y, -2)], "UpperLeg.R": [(Y, 2)],
}))
acts.append(make_action("walk", cycle(0.62), lambda t: gait(t * 2 * math.pi, 28, 45, 24, 4, 0.03, 18)))
acts.append(make_action("run", cycle(0.38), lambda t: gait(t * 2 * math.pi, 42, 80, 32, 12, 0.06, 58)))
acts.append(make_action("sprint", cycle(0.32), lambda t: gait(t * 2 * math.pi, 55, 100, 44, 20, 0.07, 70)))
# jump / fall: arms stay close to the body (big sideways raises tear these fused AI meshes at the
# shoulders), legs tuck like a real hop: lead knee up, trailing leg bent back.
acts.append(make_action("jump", 9, lambda t: {
    "Hips": [("loc", (0, 0, 0))],
    "Spine": [(X, -9 * t)], "Chest": [(X, -4 * t)], "Neck": [(X, 8 * t)],
    "UpperLeg.L": [(X, -58 * t)], "LowerLeg.L": [(X, 92 * t)], "Foot.L": [(X, 18 * t)],
    "UpperLeg.R": [(X, -22 * t)], "LowerLeg.R": [(X, 78 * t)], "Foot.R": [(X, 22 * t)],
    "UpperArm.L": [(Y, -6 * t), (X, 26 * t)], "UpperArm.R": [(Y, 6 * t), (X, -30 * t)],
    "LowerArm.L": [(X, -20 * t)], "LowerArm.R": [(X, -38 * t)],
}, loop=False))
acts.append(make_action("fall", cycle(0.8), lambda t: {
    "Spine": [(X, 3)], "Neck": [(X, -3)],
    "UpperLeg.L": [(X, -18 + math.sin(t * 6.283) * 4)], "LowerLeg.L": [(X, 30)], "Foot.L": [(X, -6)],
    "UpperLeg.R": [(X, -4 - math.sin(t * 6.283) * 4)], "LowerLeg.R": [(X, 20)], "Foot.R": [(X, -4)],
    "UpperArm.L": [(Y, -16 + math.sin(t * 6.283) * 3), (X, -8)], "UpperArm.R": [(Y, 16 - math.sin(t * 6.283) * 3), (X, -8)],
    "LowerArm.L": [(X, -22)], "LowerArm.R": [(X, -22)],
}))
acts.append(make_action("dash", 9, lambda t: {
    "Spine": [(X, -25)], "Chest": [(X, -12)], "Neck": [(X, 25)],
    "UpperArm.L": [(X, 70), (Y, -15)], "UpperArm.R": [(X, 70), (Y, 15)], "LowerArm.L": [(X, -10)], "LowerArm.R": [(X, -10)],
    "UpperLeg.L": [(X, -45)], "LowerLeg.L": [(X, 70)], "UpperLeg.R": [(X, 35)], "LowerLeg.R": [(X, 25)],
}, loop=False))
acts.append(make_action("shoot", cycle(0.45), lambda t: {
    "Chest": [(Z, -12)],
    "UpperArm.R": [(X, -88 + 14 * max(0, 1 - t * 4)), (Y, 4)], "LowerArm.R": [(X, -4 - 10 * max(0, 1 - t * 4))],
    "UpperArm.L": [(X, -35), (Y, -12)], "LowerArm.L": [(X, -60)],
}))
acts.append(make_action("hit", 9, lambda t: {
    "Spine": [(X, 18 * math.sin(t * math.pi))], "Neck": [(X, 12 * math.sin(t * math.pi))],
    "UpperArm.L": [(Y, -25 * math.sin(t * math.pi))], "UpperArm.R": [(Y, 25 * math.sin(t * math.pi))],
}, loop=False))
acts.append(make_action("death", 24, lambda t: {
    "Hips": [("loc", (0, 0.35 * min(1, t * 1.4), -0.55 * min(1, t * 1.4) ** 2)), (X, 80 * min(1, t * 1.3))],
    "UpperArm.L": [(Y, -70 * min(1, t * 2))], "UpperArm.R": [(Y, 70 * min(1, t * 2))],
    "UpperLeg.L": [(X, -30 * min(1, t * 2))], "LowerLeg.L": [(X, 40 * min(1, t * 2))],
    "Neck": [(X, -20 * min(1, t * 2))],
}, loop=False))
acts.append(make_action("victory", cycle(1.1), lambda t: {
    "Hips": [("loc", (0, 0, abs(math.sin(t * 2 * math.pi)) * 0.3))],
    "UpperArm.R": [(Y, 150 + math.sin(t * 4 * math.pi) * 12)], "LowerArm.R": [(X, -20)],
    "UpperArm.L": [(Y, -25)], "LowerArm.L": [(X, -90)],
    "UpperLeg.L": [(X, -25 * abs(math.sin(t * 2 * math.pi)))], "LowerLeg.L": [(X, 45 * abs(math.sin(t * 2 * math.pi)))],
    "UpperLeg.R": [(X, -12 * abs(math.sin(t * 2 * math.pi)))], "LowerLeg.R": [(X, 30 * abs(math.sin(t * 2 * math.pi)))],
    "Neck": [(X, -8)],
}))
log("actions:", ", ".join(a.name for a in acts))

# ============================================================ preview renders / export
def render_pose(action, frame, path):
    rig.animation_data.action = action
    scene.frame_set(frame)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

if PREVIEW:
    scene.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else scene.render.engine
    scene.render.resolution_x, scene.render.resolution_y = 520, 620
    scene.world = bpy.data.worlds.new("w"); scene.world.color = (0.85, 0.88, 0.95)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
    sun.data.energy = 3; sun.rotation_euler = (math.radians(45), 0, math.radians(-25))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.6
    os.makedirs(RENDER_DIR, exist_ok=True)
    mk = bpy.data.materials.new("mk"); mk.use_nodes = True
    mk.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0.1, 0.6, 1)
    mk.node_tree.nodes["Principled BSDF"].inputs["Emission Color"].default_value = (1, 0.1, 0.6, 1)
    mk.node_tree.nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 3
    for b in pb:
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.028)
        sph = bpy.context.active_object
        sph.data.materials.append(mk)
        sph.show_in_front = True
        sph.parent = rig; sph.parent_type = 'BONE'; sph.parent_bone = b.name
        sph.location = (0, -b.bone.length, 0)  # bone-parented objects sit at the tail; move to head
    for act, fr in (("shoot", 1), ("run", 3), ("walk", 1), ("dash", 5)):
        rig.animation_data.action = bpy.data.actions[act]; scene.frame_set(fr)
        bpy.context.view_layer.update()
        hr = rig.pose.bones["Hand.R"].head; fl = rig.pose.bones["Foot.L"].head; fr_ = rig.pose.bones["Foot.R"].head
        log(f"check {act}@{fr}: handR {tuple(round(v,2) for v in hr)}  footL {tuple(round(v,2) for v in fl)} footR {tuple(round(v,2) for v in fr_)}")
    shots = [("lobby", 0, "lobby", 1), ("idle_front", 0, "idle", 1), ("walk_front", 0, "walk", 5), ("run_side", 90, "run", 3), ("front", 0, "idle", 1), ("quarter", 35, "idle", 1), ("back", 180, "idle", 1), ("run", 70, "run", 3), ("run2", 70, "run", 8), ("jump", 30, "jump", 11), ("fall", 30, "fall", 5), ("shoot", 30, "shoot", 1), ("victory", 20, "victory", 8)]
    for name, ang, act, fr in shots:
        a = math.radians(ang)
        cam.location = Vector((math.sin(a) * 6, -math.cos(a) * 6, 1.05))
        cam.rotation_euler = (Vector((0, 0, 1.05)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sun.rotation_euler = (math.radians(50), 0, a + math.radians(-25))
        render_pose(bpy.data.actions[act], fr, os.path.join(RENDER_DIR, f"{NAME}_{name}.png"))
    log("previews rendered")
    # flat-lit face close-ups (texture exactly as baked, no Blender lighting) for checking face quality
    flat = bpy.data.materials.new("flat"); flat.use_nodes = True
    fN, fL = flat.node_tree.nodes, flat.node_tree.links
    for nd in list(fN): fN.remove(nd)
    fo = fN.new("ShaderNodeOutputMaterial"); fe = fN.new("ShaderNodeEmission"); ft = fN.new("ShaderNodeTexImage"); ft.image = baked
    fL.new(ft.outputs[0], fe.inputs[0]); fL.new(fe.outputs[0], fo.inputs[0])
    me.materials[0] = flat
    for ob in bpy.context.scene.objects:
        if ob.type == 'MESH' and ob != obj: ob.hide_render = True
    old_vt = scene.view_settings.view_transform; scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x = scene.render.resolution_y = 600
    cam.data.ortho_scale = (J["top"].z - J["neck"].z) * 1.2
    hc = Vector((0, 0, 0.5 * (J["top"].z + J["neck"].z)))
    for tag, ang in (("face_f", 0), ("face_q", 35)):
        a = math.radians(ang)
        cam.location = hc + Vector((math.sin(a) * 6, -math.cos(a) * 6, 0))
        cam.rotation_euler = (hc - cam.location).to_track_quat('-Z', 'Y').to_euler()
        render_pose(bpy.data.actions["idle"], 1, os.path.join(RENDER_DIR, f"{NAME}_{tag}.png"))
    scene.view_settings.view_transform = old_vt
    for ob in bpy.context.scene.objects:
        if ob.type == 'MESH': ob.hide_render = False
    me.materials[0] = mat

# ---- smooth head normals (anime-style): lumpy AI face geometry makes blotchy shading and rim light.
# Blend each head vertex normal toward an ellipsoid fitted to the head, fading out towards the neck.
cob = verts_np()
hm = cob[:, 2] > HEAD_Z
if hm.sum() > 50:
    hc_ = cob[hm].mean(0); hr_ = np.maximum(np.ptp(cob[hm], 0) * 0.5, 1e-3)
    nb = np.zeros(len(me.vertices) * 3, dtype=np.float32); me.vertices.foreach_get("normal", nb); nb = nb.reshape(-1, 3)
    ell = (cob - hc_) / (hr_ ** 2)
    ell /= np.linalg.norm(ell, axis=1, keepdims=True) + 1e-9
    w = np.clip((cob[:, 2] - HEAD_Z) / (0.08 * (mz1 - mz0)), 0, 1) * 0.7
    nn = nb * (1 - w[:, None]) + ell * w[:, None]
    nn /= np.linalg.norm(nn, axis=1, keepdims=True) + 1e-9
    me.normals_split_custom_set_from_vertices([tuple(v) for v in nn])
    log("smoothed head normals on", int((w > 0).sum()), "verts")

# ---- LOD1: a lighter copy of the skinned mesh (same armature, same texture) for distance / mobile
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True); bpy.context.view_layer.objects.active = obj
bpy.ops.object.duplicate(linked=False)
lod = bpy.context.view_layer.objects.active
dm = lod.modifiers.new("dec", 'DECIMATE'); dm.ratio = 0.3
bpy.ops.object.modifier_move_to_index(modifier="dec", index=0)
bpy.ops.object.modifier_apply(modifier="dec")
obj.name = f"{NAME}_LOD0"; lod.name = f"{NAME}_LOD1"
log("LOD1 faces", len(lod.data.polygons), "LOD0 faces", len(obj.data.polygons))

os.makedirs(OUT_DIR, exist_ok=True)
# marker for Unity's CharacterBuilder: textured models get more toon lighting than projected concept art
_marker = os.path.join(OUT_DIR, f"{NAME}_textured.txt")
if TEXTURED: open(_marker, "w").write(f"source: {os.path.basename(SRC)}\n")
elif os.path.exists(_marker): os.remove(_marker)
rig.animation_data.action = bpy.data.actions["idle"]
scene.frame_set(1)
out = os.path.abspath(os.path.join(OUT_DIR, f"{NAME}.fbx"))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
log("exported", out)
