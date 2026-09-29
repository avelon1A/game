# Blender: import a GLB, clay-render front / 3-quarter / back views.  blender -b -P render_preview.py -- in.glb out_prefix
import bpy, sys, math, mathutils
argv = sys.argv[sys.argv.index("--") + 1:]
src, prefix = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
# bounds
mn = mathutils.Vector((1e9,)*3); mx = mathutils.Vector((-1e9,)*3)
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(c)
        mn = mathutils.Vector(map(min, mn, w)); mx = mathutils.Vector(map(max, mx, w))
center = (mn + mx) / 2; size = (mx - mn)
print("BOUNDS", tuple(round(v,3) for v in mn), tuple(round(v,3) for v in mx))
mat = bpy.data.materials.new("clay"); mat.use_nodes = True
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.8, 0.78, 0.75, 1)
for o in objs:
    o.data.materials.clear(); o.data.materials.append(mat)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
scene.render.resolution_x = 700; scene.render.resolution_y = 900
scene.world = bpy.data.worlds.new("w"); scene.world.color = (0.9, 0.92, 0.98)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scene.collection.objects.link(sun)
sun.data.energy = 4; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(size.x, size.z, size.y) * 1.15
up = 'Z' if size.z >= size.y else 'Y'
for name, ang in [("front", 0), ("quarter", 40), ("side", 90), ("back", 180)]:
    a = math.radians(ang); d = max(size) * 3
    if up == 'Z':
        cam.location = center + mathutils.Vector((math.sin(a) * d, -math.cos(a) * d, 0))
    else:
        cam.location = center + mathutils.Vector((math.sin(a) * d, 0, math.cos(a) * d))
    direction = center - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = f"{prefix}_{name}.png"
    bpy.ops.render.render(write_still=True)
print("RENDERED", prefix)
