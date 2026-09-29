# Renders a textured model (e.g. a Meshy / hand-made GLB) straight from the front, flat-lit, so the character
# pipeline can use it exactly like concept art: masks/<name>_model.png (RGBA) for the skeleton heuristics and
# input/<name>_model_clean.png (white background) for MediaPipe pose detection.
#   blender -b -P render_model_front.py -- <model.glb> <name>
import bpy, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, NAME = argv[0], argv[1]
TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = 2048

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
for o in meshes:
    for v in o.data.vertices:
        w = o.matrix_world @ v.co
        mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
center = (mn + mx) / 2
size = mx - mn

# flat: base colour texture as emission (what the eye sees, no Blender lighting)
for o in meshes:
    for m in o.data.materials:
        nt = m.node_tree
        img = None
        for n in nt.nodes:
            if n.type == 'TEX_IMAGE' and n.image and any(l.to_socket.name == 'Base Color' for l in n.outputs[0].links):
                img = n.image
        if img is None: continue
        for n in list(nt.nodes): nt.nodes.remove(n)
        out = nt.nodes.new("ShaderNodeOutputMaterial"); em = nt.nodes.new("ShaderNodeEmission"); tx = nt.nodes.new("ShaderNodeTexImage")
        tx.image = img
        nt.links.new(tx.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], out.inputs[0])

sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else sc.render.engine
sc.render.resolution_x = sc.render.resolution_y = RES
sc.render.film_transparent = True
sc.view_settings.view_transform = 'Standard'
sc.world = bpy.data.worlds.new("w"); sc.world.color = (1, 1, 1)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = max(size.x, size.z) * 1.08
cam.location = Vector((center.x, mn.y - 5, center.z))
cam.rotation_euler = (math.radians(90), 0, 0)   # looking along +Y at the model's front (-Y)
os.makedirs(os.path.join(TOOLS, "masks"), exist_ok=True)
os.makedirs(os.path.join(TOOLS, "input"), exist_ok=True)
rgba = os.path.join(TOOLS, "masks", f"{NAME}_model.png")
sc.render.image_settings.color_mode = 'RGBA'
sc.render.filepath = rgba
bpy.ops.render.render(write_still=True)

# white-background copy for pose detection
img = bpy.data.images.load(rgba)
px = list(img.pixels)
for i in range(0, len(px), 4):
    a = px[i + 3]
    for c in range(3): px[i + c] = px[i + c] * a + (1 - a)
    px[i + 3] = 1
out = bpy.data.images.new("clean", RES, RES)
out.pixels = px
out.filepath_raw = os.path.join(TOOLS, "input", f"{NAME}_model_clean.png"); out.file_format = 'PNG'; out.save()
print("[veil] front render", rgba)
