"""
Converts chosen Kenney CC0 models (Tools/assets_dl/kenney, see README there) into game props for the Rilo island.
  Blender -b -P build_props.py -- <kenney dir> <unity Resources/Props dir>
Each prop: all meshes joined, origin at the bottom centre, +Z up -> FBX (textures embedded). Sizes stay as authored;
WorldBuilder fits every prop to its collision footprint at runtime.
"""
import bpy, os, sys, glob

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)

COM = "kenney_city-kit-commercial_2.1/Models/GLB format"
IND = "kenney_city-kit-industrial_2.0/Models/GLB format"
SUB = "kenney_city-kit-suburban_20/Models/GLB format"
ROAD = "kenney_city-kit-roads/Models/GLB format"
PIR = "kenney_pirate-kit/Models/GLB format"
HOL = "kenney_holiday-kit/Models/GLB format"
FAN = "kenney_fantasy-town-kit_2.0/Models/GLB format"
NAT = "kenney_nature-kit/Models/GLTF format"

# category -> list of (kit folder, model names)
PROPS = {
    "city":       [(COM, ["building-a", "building-b", "building-c", "building-d", "building-e", "building-f", "building-g",
                          "building-h", "building-i", "building-j", "building-k", "building-l", "building-m", "building-n"])],
    "tower":      [(COM, ["building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c", "building-skyscraper-d", "building-skyscraper-e"])],
    "warehouse":  [(IND, ["building-a", "building-c", "building-e", "building-g", "building-j", "building-m", "building-p", "building-s"])],
    "container":  [(IND, ["shipping-container-a", "shipping-container-b", "shipping-container-c"])],
    "tank":       [(IND, ["detail-tank-large", "water-tower", "detail-tank"])],
    "house":      [(SUB, ["building-type-a", "building-type-b", "building-type-c", "building-type-d", "building-type-e", "building-type-f",
                          "building-type-g", "building-type-h", "building-type-i", "building-type-j", "building-type-k", "building-type-l"])],
    "palm":       [(PIR, ["palm-straight", "palm-bend", "palm-detailed-straight", "palm-detailed-bend"])],
    "pine":       [(HOL, ["tree-snow-a", "tree-snow-b", "tree-snow-c"])],
    "tree":       [(NAT, ["tree_default", "tree_oak", "tree_detailed", "tree_fat", "tree_tall", "tree_pineRoundA", "tree_pineRoundC",
                          "tree_cone", "tree_default_dark", "tree_oak_dark", "tree_plateau", "tree_simple"])],
    "rock":       [(NAT, ["rock_largeA", "rock_largeB", "rock_tallA", "rock_tallC", "rock_tallE", "rock_smallA", "stone_largeA", "stone_tallB"])],
    "mesa":       [(NAT, ["rock_tallA", "rock_tallB", "rock_tallC", "rock_tallD", "rock_tallE", "rock_tallF", "stone_tallA", "stone_tallC"])],
    "stall":      [(FAN, ["stall", "stall-green", "stall-red"])],
    "crate":      [(PIR, ["crate", "barrel", "crate-bottles"])],
    "watchtower": [(PIR, ["tower-watch"])],
    "streetlight":[(ROAD, ["light-square", "light-curved"])],
    "parasol":    [(COM, ["detail-parasol-a", "detail-parasol-b"])],
    "bench":      [(HOL, ["bench", "bench-short"])],
    "planter":    [(SUB, ["planter"])],
    "dumpster":   [(ROAD, ["dumpster"])],
    "cone":       [(ROAD, ["construction-cone", "construction-barrier"])],
    "boat":       [(PIR, ["boat-row-large", "boat-row-small", "ship-small"])],
    "barrel":     [(PIR, ["barrel"])],
}


def has_image(mat):
    return mat and mat.use_nodes and any(n.type == 'TEX_IMAGE' and n.image for n in mat.node_tree.nodes)


def base_color(mat):
    if mat and mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                return tuple(n.inputs['Base Color'].default_value)
    return tuple(mat.diffuse_color) if mat else (1, 1, 1, 1)


# repaint plain-colour kits into the Rilo palette (sRGB hex) by material name
def recolor(cat, mat_name, c):
    n = mat_name.lower()
    hexs = None
    if "leaf" in n: hexs = ["#4caf50", "#3f9e45", "#5cbf4a", "#3a8f3f"]
    elif "wood" in n or "bark" in n: hexs = ["#8b5a3c"]
    elif cat == "mesa": hexs = ["#d9844a", "#c96a3a", "#e3a066"]
    elif "grass" in n: hexs = ["#6cbf4a"]
    elif "stone" in n or "rock" in n: hexs = ["#9a9aa6", "#878796"]
    if hexs is None:
        return (max(0.0, c[0]) ** (1 / 2.2), max(0.0, c[1]) ** (1 / 2.2), max(0.0, c[2]) ** (1 / 2.2))
    h = hexs[hash(mat_name + cat) % len(hexs)].lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def bake_palette(o, tex_dir, name):
    """Plain-colour materials (e.g. Kenney Nature Kit) -> one small palette texture + UVs, like the other kits."""
    mats = list(o.data.materials)
    if not mats or any(has_image(m) for m in mats):
        return
    os.makedirs(tex_dir, exist_ok=True)
    n = len(mats); size = 8
    img = bpy.data.images.new(name + "_pal", size, size, alpha=False)
    px = [0.0] * (size * size * 4)
    for i, m in enumerate(mats):
        c = recolor(name.split("_")[0], m.name if m else "", base_color(m))
        x, y = i % size, i // size
        k = (y * size + x) * 4
        px[k:k + 4] = [c[0], c[1], c[2], 1.0]
    img.pixels = px
    path = os.path.join(tex_dir, name + "_pal.png")
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    uv = o.data.uv_layers.new(name="pal") if not o.data.uv_layers else o.data.uv_layers[0]
    for poly in o.data.polygons:
        i = poly.material_index
        u, v = ((i % size) + 0.5) / size, ((i // size) + 0.5) / size
        for li in poly.loop_indices:
            uv.data[li].uv = (u, v)
    pal = bpy.data.materials.new(name + "_mat"); pal.use_nodes = True
    bsdf = pal.node_tree.nodes.get("Principled BSDF")
    tex = pal.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = img
    pal.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    o.data.materials.clear(); o.data.materials.append(pal)
    for poly in o.data.polygons: poly.material_index = 0


def convert(path, out):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if not meshes:
        return False
    for o in bpy.data.objects:
        o.select_set(o.type == 'MESH')
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    for e in [x for x in bpy.data.objects if x.type != 'MESH']:
        bpy.data.objects.remove(e, do_unlink=True)
    # origin: bottom centre
    xs = [v.co.x for v in o.data.vertices]; ys = [v.co.y for v in o.data.vertices]; zs = [v.co.z for v in o.data.vertices]
    cx, cy, z0 = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)
    for v in o.data.vertices:
        v.co.x -= cx; v.co.y -= cy; v.co.z -= z0
    o.location = (0, 0, 0)
    o.name = os.path.splitext(os.path.basename(out))[0]
    bake_palette(o, os.path.join(os.path.dirname(out), "Textures"), o.name)
    # textures: save each image once next to the props (Unity finds them by relative path)
    tex_dir = os.path.join(os.path.dirname(out), "Textures")
    os.makedirs(tex_dir, exist_ok=True)
    kit = os.path.basename(os.path.dirname(os.path.dirname(os.path.dirname(path))))
    for img in bpy.data.images:
        if img.size[0] == 0: continue
        dst = os.path.join(tex_dir, f"{kit}_{os.path.splitext(img.name)[0]}.png")
        if not os.path.exists(dst):
            img.filepath_raw = dst; img.file_format = 'PNG'; img.save()
        else:
            img.filepath_raw = dst
        img.packed_file and img.unpack(method='REMOVE')
    bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', path_mode='RELATIVE', embed_textures=False, mesh_smooth_type='FACE')
    return True


n = 0
for cat, groups in PROPS.items():
    i = 0
    for kit, names in groups:
        for name in names:
            src = None
            for ext in (".glb", ".gltf"):
                cand = os.path.join(SRC, kit, name + ext)
                if os.path.exists(cand): src = cand; break
            if src is None:
                print("[props] missing", kit, name); continue
            if convert(src, os.path.join(OUT, f"{cat}_{i}.fbx")):
                i += 1; n += 1
    print(f"[props] {cat}: {i}")
print(f"[props] total {n}")
