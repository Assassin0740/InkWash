# 龙专用 glTF -> FBX：只烘真动画 + 不用 all_bones（避免 273骨×1380帧爆炸）
# 用法: blender -b --python gltf2fbx_dragon.py -- <gltf> <out_fbx>
import bpy, sys, os, time

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
GLTF, OUT = argv[0], argv[1]

def log(*a):
    print(f"[{time.strftime('%H:%M:%S')}]", *a, flush=True)

log("START", GLTF)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLTF)
log("imported")

acts = list(bpy.data.actions)
# 只留 fcurve 最多的那一条（真动画），其余空壳删掉
acts_sorted = sorted(acts, key=lambda a: len(a.fcurves), reverse=True)
log(f"all actions={len(acts)}")
for a in acts_sorted:
    log(f"  '{a.name}' fcurves={len(a.fcurves)}")
keep = acts_sorted[0]
log(f"KEEP only '{keep.name}'")
for a in acts:
    if a is not keep:
        bpy.data.actions.remove(a)
log(f"remaining actions={len(bpy.data.actions)}")

arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
log(f"mesh={len(meshes)} arm={len(arms)}")
for arm in arms:
    log(f"  armature '{arm.name}' bones={len(arm.data.bones)}")

# 关键：use_armature_deform_only=True 只烘有顶点权重的骨（273 里大部分是控制骨/空骨）
log("exporting fbx (deform-only, no all_bones)...")
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
    bake_anim_use_all_bones=False,          # <= 关键改动
    bake_anim_force_startend_keying=False,  # <= 少一遍扫描
    bake_anim_simplify_factor=1.0,          # <= 允许简化掉直线段
    add_leaf_bones=False,
    use_armature_deform_only=True,          # <= 关键改动
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
