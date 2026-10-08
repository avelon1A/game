# Game-file cleanup for one hero, keeping its rig, clips, orientation and size (same export as build_meshy_rigged.py):
# merge duplicate vertices, drop loose / zero-area geometry, decimate to max_tris, rename the material MAT_<Hero>_Primary.
#   blender -b -P optimize_hero.py -- in.fbx out.fbx HeroName max_tris
import bpy, bmesh, sys
argv = sys.argv[sys.argv.index("--") + 1:]
src, out, name, max_tris = argv[0], argv[1], argv[2], int(argv[3])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
if arm.animation_data: arm.animation_data.action = None
for o in list(bpy.context.scene.objects):
    if o.type not in ('ARMATURE', 'MESH'): bpy.data.objects.remove(o)
for m in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
    bm = bmesh.new(); bm.from_mesh(m.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00005)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_edges], context='VERTS')
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-12], context='FACES')
    bm.to_mesh(m.data); bm.free(); m.data.update()
    t = sum(len(p.vertices) - 2 for p in m.data.polygons)
    if t > max_tris:
        md = m.modifiers.new("dec", 'DECIMATE'); md.ratio = max_tris / t
        bpy.context.view_layer.objects.active = m
        # the armature modifier must stay last
        while m.modifiers.find("dec") > 0: bpy.ops.object.modifier_move_up(modifier="dec")
        bpy.ops.object.modifier_apply(modifier="dec")
    for slot in m.material_slots:
        if slot.material: slot.material.name = f"MAT_{name}_Primary"
    print("OPT", name, m.name, t, "->", sum(len(p.vertices) - 2 for p in m.data.polygons))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE', colors_type='SRGB')
print("WROTE", out)
