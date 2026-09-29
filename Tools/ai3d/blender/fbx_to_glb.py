# Meshy FBX package -> GLB with the base-colour texture wired up (so the character pipeline can use it).
#   blender -b -P fbx_to_glb.py -- <model.fbx> <basecolor.png> <out.glb>
import bpy, sys
from mathutils import Vector
fbx, tex, out = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
img = bpy.data.images.load(tex)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in bpy.context.scene.objects: o.select_set(o in meshes)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
mat = bpy.data.materials.new("Material_0"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img
nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
obj.data.materials.clear(); obj.data.materials.append(mat)
mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
for v in obj.data.vertices:
    mn = Vector(map(min, mn, v.co)); mx = Vector(map(max, mx, v.co))
print("[veil] bounds", tuple(round(x, 3) for x in mn), tuple(round(x, 3) for x in mx), "verts", len(obj.data.vertices))
for o in list(bpy.context.scene.objects):
    if o != obj: bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=False)
print("[veil] wrote", out)
