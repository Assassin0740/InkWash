# 单个 glTF -> FBX（带进度输出，无缓冲）
# 用法: blender -b --python gltf2fbx_one.py -- <gltf_path> <out_fbx>
import bpy, sys, os, time

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
GLTF, OUT = argv[0], argv[1]

def log(*a):
    print(f"[{time.strftime('%H:%M:%S')}]", *a, flush=True)

log("START", GLTF)
bpy.ops.wm.read_factory_settings(use_empty=True)
log("importing...")
t0 = time.time()
bpy.ops.import_scene.gltf(filepath=GLTF)
log(f"import done in {time.time()-t0:.1f}s")

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
acts = list(bpy.data.actions)
log(f"mesh={len(meshes)} arm={len(arms)} actions={len(acts)}")

for a in acts:
    fr = a.frame_range
    log(f"  action '{a.name}' frames {fr[0]:.0f}-{fr[1]:.0f} fcurves={len(a.fcurves)}")

tot = 0
for m in meshes:
    me = m.data
    me.calc_loop_triangles()
    tot += len(me.loop_triangles)
log(f"triangles={tot}")

for arm in arms:
    log(f"  armature '{arm.name}' bones={len(arm.data.bones)}")

# 贴图：Blender 导入 glTF 后 image.filepath 指向解包出的临时文件
# 我们把它拷到 out_fbx 同级的 <name>_tex/ 里
outdir = os.path.dirname(OUT)
texout = os.path.join(outdir, os.path.splitext(os.path.basename(OUT))[0] + "_tex")
n_tex = 0
for img in bpy.data.images:
    if not img.filepath:
        continue
    src = bpy.path.abspath(img.filepath)
    if not os.path.isfile(src):
        continue
    os.makedirs(texout, exist_ok=True)
    dst = os.path.join(texout, img.name + os.path.splitext(src)[1])
    try:
        import shutil
        shutil.copy2(src, dst)
        n_tex += 1
    except Exception as e:
        log("TEX_COPY_FAIL", img.name, e)
log(f"textures copied={n_tex} -> {texout}")

log("exporting fbx...")
t0 = time.time()
bpy.ops.export_scene.fbx(
    filepath=OUT,
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
log(f"export done in {time.time()-t0:.1f}s -> {OUT} ({os.path.getsize(OUT)/1048576:.2f}MB)")
log("DONE")
