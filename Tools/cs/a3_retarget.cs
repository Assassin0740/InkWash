// a3_retarget.cs —— 验证 Z_Undead 能否重定向复用其他怪的片段
//
// 背景：Z_Undead 的 FBX 里**只有 1 段 Slash（1.625s）**，没有 idle/walk/run/hit/death。
//   但它是 Humanoid（isHuman=Y valid=Y）⇒ 理论上可以用 Humanoid 重定向复用
//   Z_Orge / Z_Orc 的片段。
//
// 本脚本要回答：**Humanoid 重定向到 Z_Undead 的 avatar 上，动画是否真的动起来**。
//   做法：对每个候选片段，把它挂到一个临时 Animator（avatar = Z_Undead 的 avatar）上，
//   逐帧采样 → 读关键骨骼的世界坐标 → 算总位移。
//   若位移接近 0 ⇒ 重定向失败（片段与该骨架不兼容，会得到"站着不动的怪"且零报错）。
//
// ★ 这条"零报错但不动"正是本项目最怕的静默失效，所以必须实测而不是看 isHuman 标志位。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

// Z_Undead 的 avatar
var undeadPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Undead.prefab");
Avatar undeadAvatar = null;
{
    var c = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Undead.prefab");
    var an = c.GetComponentInChildren<Animator>(true);
    undeadAvatar = an != null ? an.avatar : null;
    PrefabUtility.UnloadPrefabContents(c);
}
sb.AppendLine("Z_Undead avatar = " + (undeadAvatar != null ? undeadAvatar.name : "null")
              + " isHuman=" + (undeadAvatar != null && undeadAvatar.isHuman)
              + " valid=" + (undeadAvatar != null && undeadAvatar.isValid));

// 建一个临时实例来驱动动画采样
var probe = new GameObject("A3_Probe");
var anim = probe.AddComponent<Animator>();
anim.avatar = undeadAvatar;
anim.applyRootMotion = false;

// 造一个只含一个 state 的临时 controller
string tmpDir = "Assets/_Project/Animations/_A3Tmp";
bool createdDir = false;
if (!AssetDatabase.IsValidFolder(tmpDir))
{
    AssetDatabase.CreateFolder("Assets/_Project/Animations", "_A3Tmp");
    createdDir = true;
}

AnimationClip MakeTestClip(params AnimationClip[] src)
{
    var nc = new AnimationClip();
    nc.name = "A3_Test";
    foreach (var c in src)
        foreach (var b in AnimationUtility.GetCurveBindings(c))
        {
            var cb = new EditorCurveBinding { path = b.path, type = b.type, propertyName = b.propertyName };
            AnimationUtility.SetEditorCurve(nc, cb, AnimationUtility.GetEditorCurve(c, b));
        }
    return nc;
}

float MeasureMotion(AnimationClip clip)
{
    if (clip == null) return -1f;
    var ac = AnimatorController.CreateAnimatorControllerAtPath(tmpDir + "/A3C_" + Mathf.Abs(clip.name.GetHashCode()) + ".controller");
    ac.AddMotion(clip);
    anim.runtimeAnimatorController = ac;

    float dur = Mathf.Max(0.1f, clip.length);
    int steps = Mathf.Clamp((int)(dur / 0.05f), 8, 120);
    Vector3 prev = Vector3.zero;
    float total = 0f;
    bool got = false;
    var poses = new List<Vector3>();

    for (int i = 0; i <= steps; i++)
    {
        float t = i / (float)steps * dur;
        anim.Play(0, 0, t / dur);
        anim.Update(0f);
        // 取骨盆世界坐标（Humanoid 下一定有）
        var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null) { total = -2f; break; }
        var p = hips.position;
        poses.Add(p);
        if (got) total += Vector3.Distance(p, prev);
        prev = p; got = true;
    }

    UnityEngine.Object.DestroyImmediate(ac);
    return total;
}

sb.AppendLine();
sb.AppendLine("========== 候选片段 → 重定向到 Z_Undead 后的髋部总路径 ==========");
sb.AppendLine("（接近 0 = 重定向失败；明显 > 0 = 动起来了）");
sb.AppendLine();

var cands = new List<(string label, string asset, string clipName)>();
string fbxOrge = "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx";
string fbxOrc = "Assets/ThirdParty/Ziyuan/_fbx/low-poly_orc.fbx";
string fbxUndead = "Assets/ThirdParty/Ziyuan/_fbx/cursed_undead_soldier_rig.fbx";
string fbxDragon = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";

foreach (var (path, tag, want) in new[]
{
    (fbxOrge, "Orge", new[] { "Ideal_Object_5", "Walk_Object_5", "Run_Object_5", "Punch_Object_5", "Slam_Object_5", "Death_Object_5", "Roar2_Object_5" }),
    (fbxOrc, "Orc", new[] { "ANM_IDLE_Object_4", "ANM_WALK_Object_4", "ANM_RUN_Object_4", "ANM_ORC_LIGHT_ATTACK_Object_4", "ANM_DAMAGED_Object_4", "ANM_DEATH_Object_4" }),
    (fbxUndead, "Undead", new[] { "Slash_GLTF_created_0" }),
})
{
    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
    var clips = new List<AnimationClip>();
    foreach (var o in assets)
        if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clips.Add(c);

    // ★ 注意：Unity 里这些片段的实际名字是 'Object_5|Armature|Walk_Object_5' 这种
    //   「节点路径|动作名」复合形式（a1_survey 报告里能看到），不是裸动作名。
    //   所以必须用 **EndsWith** 匹配，不能写 `c.name == wantName`（会一个都找不到，
    //   而且零报错 —— 表现为"所有片段都缺"，很容易误判成素材没导进来）。
    foreach (var wantName in want)
    {
        AnimationClip found = null;
        foreach (var c in clips)
            if (c.name == wantName || c.name.EndsWith("|" + wantName)) { found = c; break; }
        if (found == null)
        {
            sb.AppendLine("  [" + tag + "] ★ 找不到 '" + wantName + "'（该 FBX 共 " + clips.Count + " 段）");
            continue;
        }

        float m = MeasureMotion(found);
        string verdict = m < 0 ? "★ 取不到髋骨"
            : (m < 0.02f ? "★★ 没动（重定向失败）"
            : (m < 0.3f ? "○ 轻微" : "✓ 动起来了"));
        sb.AppendLine("  [" + tag + "] " + wantName.PadRight(34) + " len=" + found.length.ToString("F3")
                      + "s  髋部路径=" + m.ToString("F3") + " m   " + verdict);
    }
}

sb.AppendLine();
sb.AppendLine("--- 各 FBX 实际片段名（复核用）---");
foreach (var (path, tag) in new[] { (fbxOrge, "Orge"), (fbxOrc, "Orc"), (fbxUndead, "Undead"), (fbxDragon, "Dragon") })
{
    sb.AppendLine("  [" + tag + "]");
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
            sb.AppendLine("      '" + c.name + "'  len=" + c.length.ToString("F3"));
}

sb.AppendLine();
sb.AppendLine("========== 对照：Orge 片段重定向到 Orge 自己的 avatar ==========");
Avatar orgeAvatar = null;
{
    var c = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Orge.prefab");
    var an = c.GetComponentInChildren<Animator>(true);
    orgeAvatar = an != null ? an.avatar : null;
    PrefabUtility.UnloadPrefabContents(c);
}
sb.AppendLine("Z_Orge avatar = " + (orgeAvatar != null ? orgeAvatar.name : "null"));
{
    anim.avatar = orgeAvatar;
    var assets = AssetDatabase.LoadAllAssetsAtPath(fbxOrge);
    foreach (var o in assets)
    {
        if (!(o is AnimationClip c) || c.name.StartsWith("__preview__")) continue;
        if (c.name != "Walk_Object_5" && c.name != "Run_Object_5") continue;
        float m = MeasureMotion(c);
        sb.AppendLine("  [Orge→Orge] " + c.name + " 髋部路径=" + m.ToString("F3"));
    }
}

// 清理
UnityEngine.Object.DestroyImmediate(probe);
if (createdDir) AssetDatabase.DeleteAsset(tmpDir);

File.WriteAllText(Path.Combine(root, "Tools/reports/a3_retarget.txt"), sb.ToString());
Debug.Log("[a3] 完成 " + sb.Length + " 字符\n" + sb.ToString());
