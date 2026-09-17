// a5_retarget3.cs —— 重定向验证（v3：修掉「Update(0f) 不推进」）
//
// 两轮测量的教训，都记在这里（这类"测量本身错了"最费时间）：
//   v1（a3）：建了个**没有骨骼层级**的空探针 ⇒ GetBoneTransform 恒 null。
//            教训：Humanoid 重定向要靠真实骨骼树解算，测量对象必须带完整骨架。
//   v2（a4）：用真实 prefab 了，但循环里写 `anim.Play(...); anim.Update(0f);`
//            —— **Update(0f) 时间增量为 0，Animator 不会推进**，
//            于是所有片段（连自己的）都读到同一个静止姿态 ⇒ 全 0。
//            教训：手动驱动 Animator 必须给**非零 dt**，且不能用 Play() 去"定位"时间
//            （Play 的 normalizedTime 只在**下一次 Update 生效**）。
//
// v3 做法（确定性采样）：
//   `anim.Play(stateHash, 0, normalizedTime)` 定位 → `anim.Update(dt)` 推进一小步
//   → 读骨骼。关键是 dt 必须 > 0，且定位后要再 Update 一次才反映到 Transform。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

const string TmpDir = "Assets/_Project/Animations/_A5Tmp";
if (!AssetDatabase.IsValidFolder(TmpDir))
    AssetDatabase.CreateFolder("Assets/_Project/Animations", "_A5Tmp");

string fbxOrge = "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx";
string fbxOrc = "Assets/ThirdParty/Ziyuan/_fbx/low-poly_orc.fbx";
string fbxUndead = "Assets/ThirdParty/Ziyuan/_fbx/cursed_undead_soldier_rig.fbx";

List<AnimationClip> ClipsOf(string fbx)
{
    var r = new List<AnimationClip>();
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
        if (o is AnimationClip c && !c.name.StartsWith("__preview__")) r.Add(c);
    return r;
}

GameObject MakeInstance(string prefabPath)
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 2000f, 0f);
    return go;
}

// 采样一个片段，返回 (髋部总路径, 手部总路径, 髋Y跨度)
(float hips, float hand, float yspan) Sample(GameObject inst, AnimationClip clip)
{
    var anim = inst.GetComponentInChildren<Animator>(true);
    if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return (-1f, -1f, -1f);

    string cpath = TmpDir + "/A5C_" + Mathf.Abs(clip.name.GetHashCode()) + ".controller";
    var ac = AnimatorController.CreateAnimatorControllerAtPath(cpath);
    ac.AddMotion(clip);
    var prevCtrl = anim.runtimeAnimatorController;
    anim.runtimeAnimatorController = ac;

    // 让 Animator 完成一次完整初始化（骨骼绑定发生在第一帧）
    anim.Play(0, 0, 0f);
    anim.Update(0.016f);

    var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    if (hips == null)
    {
        anim.runtimeAnimatorController = prevCtrl;
        AssetDatabase.DeleteAsset(cpath);
        return (-1f, -1f, -1f);
    }

    float dur = Mathf.Max(0.1f, clip.length);
    int steps = Mathf.Clamp((int)(dur / 0.033f), 10, 200);
    float dt = dur / steps;

    float hp = 0f, hd = 0f, ymin = float.MaxValue, ymax = float.MinValue;
    Vector3 prevP = Vector3.zero, prevH = Vector3.zero;
    bool got = false;

    for (int i = 0; i <= steps; i++)
    {
        float norm = Mathf.Clamp01(i / (float)steps);
        anim.Play(0, 0, norm);
        anim.Update(dt);          // ★ 非零 dt，才会真正推进

        var p = hips.position;
        if (got)
        {
            hp += Vector3.Distance(p, prevP);
            if (hand != null) hd += Vector3.Distance(hand.position, prevH);
        }
        prevP = p;
        if (hand != null) prevH = hand.position;
        got = true;
        ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y);
    }

    anim.runtimeAnimatorController = prevCtrl;
    AssetDatabase.DeleteAsset(cpath);
    return (hp, hd, ymax - ymin);
}

void Dump(GameObject inst, string tag, IEnumerable<AnimationClip> clips, StringBuilder b)
{
    foreach (var c in clips)
    {
        var (hp, hd, ys) = Sample(inst, c);
        string verdict;
        if (hp < 0f) verdict = "★ 取不到髋骨";
        else if (hp + hd < 0.05f) verdict = "★★ 完全静止（重定向失败）";
        else if (hp + hd < 0.5f) verdict = "○ 动作很轻微";
        else verdict = "✓ 动起来了";
        b.AppendLine("  " + tag.PadRight(16) + c.name.PadRight(48)
                     + " 髋=" + hp.ToString("F3").PadLeft(7)
                     + " 手=" + hd.ToString("F3").PadLeft(7)
                     + " 髋Y跨=" + ys.ToString("F3").PadLeft(6) + "  " + verdict);
    }
}

// ============ A) 各怪吃自己的片段（基线：必须先有动作）============
sb.AppendLine("========== A) 基线：各怪吃自己的片段 ==========");
foreach (var (prefab, fbx, tag) in new[]
{
    ("Assets/_Project/Prefabs/Ziyuan/Z_Orge.prefab", fbxOrge, "Orge"),
    ("Assets/_Project/Prefabs/Ziyuan/Z_Orc.prefab", fbxOrc, "Orc"),
    ("Assets/_Project/Prefabs/Ziyuan/Z_Undead.prefab", fbxUndead, "Undead"),
})
{
    var inst = MakeInstance(prefab);
    var anim = inst.GetComponentInChildren<Animator>(true);
    sb.AppendLine();
    sb.AppendLine("--- " + tag + "  avatar=" + (anim != null && anim.avatar != null ? anim.avatar.name : "null")
                  + " isHuman=" + (anim != null && anim.avatar != null && anim.avatar.isHuman) + " ---");
    Dump(inst, "[自有]", ClipsOf(fbx), sb);
    UnityEngine.Object.DestroyImmediate(inst);
}

// ============ B) Z_Undead 吃别人的片段（重定向验证）============
sb.AppendLine();
sb.AppendLine("========== B) Z_Undead 吃 Orge/Orc 的片段（Humanoid 重定向）==========");
{
    var inst = MakeInstance("Assets/_Project/Prefabs/Ziyuan/Z_Undead.prefab");
    foreach (var (fbx, tag) in new[] { (fbxOrge, "Orge→Undead"), (fbxOrc, "Orc→Undead") })
        Dump(inst, "[" + tag + "]", ClipsOf(fbx), sb);
    UnityEngine.Object.DestroyImmediate(inst);
}

AssetDatabase.DeleteAsset(TmpDir);

File.WriteAllText(Path.Combine(root, "Tools/reports/a5_retarget3.txt"), sb.ToString());
Debug.Log("[a5] 完成 " + sb.Length + " 字符\n" + sb.ToString());
