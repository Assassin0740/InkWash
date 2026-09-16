# glTF -> FBX，带「自动单位归一」与「垃圾清理」
#
# 为什么需要：Sketchfab 上不同作者用的单位不一样。
#   mountain_orge  原始高度 4.86  （米，正常）
#   low-poly_orc   原始高度 3.09  （米，正常）
#   cursed_undead  原始高度 413.1 （厘米！）
#   chinese_dragon 原始高度 542.0 （厘米！）
#   dragon_LP      原始高度 0.026 （某种更小的单位）
# 直接把不同单位的模型放进一个场景里必然比例全错。
# 这里在 Blender 侧量出真实高度，缩放到「目标身高」，再导出。
#
# 同时删掉 Sketchfab glTF 自带的 `Icosphere`（环境球，纯垃圾几何）。
#
# 用法: blender -b --python gltf2fbx_scaled.py -- <gltf> <out_fbx> <target_height> [keep_all_actions]
import bpy, sys, os, time, math

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
GLTF, OUT, TARGET_H = argv[0], argv[1], float(argv[2])
KEEP_ALL = (len(argv) > 3 and argv[3].lower() in ("1", "true", "yes"))

def log(*a):
    print(f"[{time.strftime('%H:%M:%S')}]", *a, flush=True)

log("START", GLTF, "target_h=", TARGET_H)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLTF)
log("imported")

# ---- 1. 删垃圾：Icosphere / 环境球 ----
removed = []
for o in list(bpy.context.scene.objects):
    nm = o.name.lower()
    if o.type == "MESH" and (nm.startswith("icosphere") or nm.startswith("sphere")):
        removed.append(o.name)
        bpy.data.objects.remove(o, do_unlink=True)
log(f"removed junk: {removed}")

# ---- 2. 量真实高度（世界空间顶点真值）----
def world_bounds():
    lo = [1e18] * 3
    hi = [-1e18] * 3
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        dg = bpy.context.evaluated_depsgraph_get()
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        if me is None:
            continue
        mw = o.matrix_world
        for v in me.vertices:
            w = mw @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i]); hi[i] = max(hi[i], w[i])
        ev.to_mesh_clear()
    return lo, hi

lo, hi = world_bounds()
size = [hi[i] - lo[i] for i in range(3)]
log(f"raw bounds size = ({size[0]:.4f}, {size[1]:.4f}, {size[2]:.4f})")
raw_h = size[1]
log(f"raw height = {raw_h:.4f}")

if raw_h <= 1e-6:
    log("!! 高度近 0，放弃缩放")
    scale = 1.0
else:
    scale = TARGET_H / raw_h
log(f"scale factor = {scale:.6f}  ({raw_h:.3f} -> {TARGET_H})")

# ---- 3. 应用缩放：直接改所有顶层对象的 matrix_world ----
# 骨架缩放要小心（会污染骨骼空间），但这里是统一等比 + 导出烘焙，安全
tops = [o for o in bpy.context.scene.objects if o.parent is None]
for o in tops:
    o.matrix_world = (
        __import__("mathutils").Matrix.Scale(scale, 4) @ o.matrix_world
    )
bpy.context.view_layer.update()
log(f"scaled {len(tops)} top-level objects")

lo2, hi2 = world_bounds()
log(f"after bounds height = {hi2[1] - lo2[1]:.4f}")

# ---- 4. 动画裁剪（可选：只留 fcurve 最多的那条）----
acts = list(bpy.data.actions)
if acts and not KEEP_ALL:
    best = max(acts, key=lambda a: len(a.fcurves))
    removed_acts = [a.name for a in acts if a is not best]
    for a in acts:
        if a is not best:
            bpy.data.actions.remove(a)
    log(f"kept only '{best.name}', removed {len(removed_acts)}: {removed_acts}")
log(f"actions = {len(bpy.data.actions)}")

arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
log(f"mesh={len(meshes)} arm={len(arms)}")
for arm in arms:
    log(f"  armature '{arm.name}' bones={len(arm.data.bones)}")
tot = 0
for m in meshes:
    m.data.calc_loop_triangles()
    tot += len(m.data.loop_triangles)
log(f"triangles={tot}")

# ---- 5. 导出 ----
log("exporting fbx...")
t0 = time.time()
bpy.ops.export_scene.fbx(
    filepath=OUT,
    path_mode="COPY",
    embed_textures=False,
    use_selection=False,
    use_visible=False,
    object_types={"MESH", "ARMATURE"},
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_bones=False,
    bake_anim_force_startend_keying=False,
    bake_anim_simplify_factor=1.0,
    add_leaf_bones=False,
    use_armature_deform_only=True,
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
log(f"SUMMARY rawH={raw_h:.4f} scale={scale:.6f} finalH={hi2[1]-lo2[1]:.4f} tris={tot}")
log("DONE")
