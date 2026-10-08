# Clean reference renders of a hero for Meshy (front / side / back, T-pose, flat textured, white background, orthographic).
#   blender -b -P render_reference.py -- hero.fbx albedo.png out_prefix
import bpy, sys
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=a[0])
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
if arm.animation_data: arm.animation_data.action = None
arm.data.pose_position = 'REST'
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
img = bpy.data.images.load(a[1])
for slot in mesh.material_slots:
    mt = slot.material; mt.use_nodes = True; nt = mt.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    tex = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    if bsdf: nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
me = mesh.evaluated_get(dg).to_mesh()
vs = [mesh.matrix_world @ v.co for v in me.vertices]
lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))); hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
c = (lo + hi) / 2; size = max(hi.x - lo.x, hi.z - lo.z) * 1.08
scn = bpy.context.scene
scn.render.engine = 'BLENDER_WORKBENCH'
scn.display.shading.light = 'FLAT'; scn.display.shading.color_type = 'TEXTURE'
scn.render.film_transparent = False
scn.world = bpy.data.worlds.new("w"); scn.world.color = (1, 1, 1)
scn.display_settings.display_device = 'sRGB'; scn.view_settings.view_transform = 'Standard'
scn.render.resolution_x = scn.render.resolution_y = 1024
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = size
for nm, d in (("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0))), ("back", Vector((0, 1, 0)))):
    cam.location = c + d * 10; cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = f"{a[2]}_{nm}.png"; bpy.ops.render.render(write_still=True)
print("REF done")
