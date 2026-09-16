# 把 glTF 批量转成 FBX（供 Unity 原生导入）
# 用法: blender --background --python gltf2fbx.py -- <in_dir> <out_dir> [name1 name2 ...]
import bpy, sys, os, glob

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
IN_DIR, OUT_DIR = argv[0], argv[1]

names = sys.argv[sys.argv.index("--") + 1 + 2:] if len(sys.argv) > 2 else []
targets = names if names else None


def discover_gltf(root):
    """找到所有 scene.gltf，用父目录名当 tag"""
    out = []
    for dirpath, dirnames, filenames in os.walk(root):
        for fn in filenames:
            if fn.lower().endswith((".gltf", ".glb")):
                base = os.path.splitext(fn)[0]
                parent = os.path.basename(dirpath)
                tag = parent if base.lower() in ("scene", "untitled") else base
                out.append((tag, os.path.join(dirpath, fn)))
    return out


files = discover_gltf(IN_DIR)
print(f"FOUND {len(files)} gltf")
for tag, path in sorted(files):
    print(f"  {tag} <- {path}")


def convert(tag, gltf_path, outdir):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.import_scene.gltf(filepath=gltf_path)
    except Exception as e:
        print("IMPORT_FAIL", tag, e)
        return None

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    acts = list(bpy.data.actions)
    print(f"  {tag}: mesh={len(meshes)} arm={len(arms)} actions={len(acts)}")

    # 每个 mesh 的三角面数
    tot = 0
    for m in meshes:
        me = m.data
        me.calc_loop_triangles()
        tot += len(me.loop_triangles)
    print(f"  {tag}: triangles={tot}")

    # 贴图：把用到的贴图单独拷出来（Unity 需要独立文件）
    texmap = {}
    for img in bpy.data.images:
        if not img.filepath:
            continue
        src = bpy.path.abspath(img.filepath)
        if os.path.isfile(src):
            texmap[img.name] = src

    out_fbx = os.path.join(outdir, tag + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=out_fbx,
        path_mode="COPY",
        embed_textures=False,
        use_selection=False,
        use_visible=False,
        object_types={"MESH", "ARMATURE", "EMPTY"},
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_bones=True,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        add_leaf_bones=False,
        use_armature_deform_only=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        armature_nodetype="NULL",
        apply_scale_options="FBX_SCALE_ALL",
        apply_unit_scale=True,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        use_tspace=True,
        use_custom_props=False,
        batch_mode="OFF",
        use_batch_own_dir=False,
    )
    return out_fbx, tot, len(acts)


os.makedirs(OUT_DIR, exist_ok=True)
summary = []
for tag, path in sorted(files):
    if targets and tag not in targets:
        continue
    print("=" * 60)
    print("CONVERT", tag)
    r = convert(tag, path, OUT_DIR)
    if r:
        f, tris, na = r
        summary.append((tag, tris, na, os.path.getsize(f) / 1048576))
        print(f"  OK -> {f}")

print("=" * 60)
print("SUMMARY_BEGIN")
for tag, tris, na, mb in summary:
    print(f"  {tag:34s} tris={tris:>7}  actions={na:>3}  fbx={mb:.2f}MB")
print("SUMMARY_END")
