"""Top-down max-height grid of the big landmarks (normalised: widest side = 1). Prints JSON per model."""
import bpy, sys, json, os
argv = sys.argv[sys.argv.index("--") + 1:]
SRC, N = argv[0], int(argv[1]); NAMES = argv[2:]
out = {}
for name in NAMES:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, f"{name}_textured.glb"))
    pts = []
    dg = bpy.context.evaluated_depsgraph_get()
    for o in bpy.data.objects:
        if o.type != 'MESH': continue
        m = o.matrix_world
        me = o.data
        for p in me.polygons:
            c = m @ p.center
            pts.append((c.x, c.y, c.z))
        for v in me.vertices:
            c = m @ v.co
            pts.append((c.x, c.y, c.z))
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; zs = [p[2] for p in pts]
    x0, x1, y0, y1, z0 = min(xs), max(xs), min(ys), max(ys), min(zs)
    w = max(x1 - x0, y1 - y0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    g = [[0.0] * N for _ in range(N)]
    for x, y, z in pts:
        i = int(((x - cx) / w + 0.5) * N); j = int(((y - cy) / w + 0.5) * N)
        if 0 <= i < N and 0 <= j < N: g[j][i] = max(g[j][i], (z - z0) / w)
    out[name] = {"w": w, "sx": (x1 - x0) / w, "sy": (y1 - y0) / w, "h": (max(zs) - z0) / w, "grid": g}
print("HMAP" + json.dumps(out))
