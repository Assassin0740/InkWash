// a4_retarget2.cs —— 重定向验证（v2：用真实 prefab 实例驱动）
//
// a3 的失败教训：我建了个只有空 Animator、**没有骨骼层级**的探针对象，
//   `GetBoneTransform(Hips)` 必然返回 null —— Humanoid 重定向要靠真实骨骼树
//   把肌肉空间姿态解算回本地旋转，没有骨头就无从解算。
//   ★ 这是「测量方法错了」而不是「被测对象坏了」：报告里 14 个片段全灭，
//     那个"全灭"本身就是测量方式不对的信号（真失败不会整齐到 100%）。
//
// v2 做法：实例化**真实的 Z_Undead prefab**（有完整骨骼），给它换 controller，
//   然后逐帧推进读髋骨世界坐标。这才是能真正回答"重定向后动没动"的方式。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

// 临时 controller 目录
const string TmpDir = "Assets/_Project/Animations/_A4Tmp";
if (!AssetDatabase.IsValidFolder(TmpDir))
    AssetDatabase.CreateFolder("Assets/_Project/Animations", "_A4Tmp");

// 拿出来源片段（含裸 clip 的 FBX）
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

// 被测对象：实例化真实 prefab
GameObject MakeInstance(string prefabPath)
{
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.transform.position = new Vector3(0f, 2000f, 0f);   // 扔到场地外，不影响任何东西
    return go;
}

// 用真实实例量"喂某片段后骨架动没动"
float MeasureOnInstance(GameObject inst, AnimationClip clip, out string err)
{
    err = null;
    var anim = inst.GetComponentInChildren<Animator>(true);
    if (anim == null) { err = "实例上没有 Animator"; return -1f; }
    if (anim.avatar == null || !anim.avatar.isHuman) { err = "avatar 不是 Humanoid"; return -1f; }

    // 建单状态 controller
    string cpath = TmpDir + "/A4C_" + Mathf.Abs(clip.name.GetHashCode()) + ".controller";
    var ac = AnimatorController.CreateAnimatorControllerAtPath(cpath);
    ac.AddMotion(clip);
    var prevCtrl = anim.runtimeAnimatorController;
    anim.runtimeAnimatorController = ac;

    // 初始化：必须让 Animator 先走一帧，骨骼层级才被 avatar 绑定
    anim.Play(0, 0, 0f);
    anim.Update(0f);

    var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    if (hips == null) { err = "GetBoneTransform(Hips) = null（avatar 未绑定到这套骨骼）"; anim.runtimeAnimatorController = prevCtrl; return -1f; }

    float dur = Mathf.Max(0.1f, clip.length);
    int steps = Mathf.Clamp((int)(dur / 0.05f), 8, 160);
    float total = 0f, spanMin = float.MaxValue, spanMax = float.MinValue;
    Vector3 prev = Vector3.zero;
    bool got = false;
    // 同时看一只手：髋部可能原地不动，手一定在动
    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    float handTotal = 0f; Vector3 prevH = Vector3.zero; bool gotH = false;

    for (int i = 0; i <= steps; i++)
    {
        float t = i / (float)steps * dur;
        anim.Play(0, 0, Mathf.Clamp01(t / dur));
        anim.Update(0f);

        var p = hips.position;
        if (got) total += Vector3.Distance(p, prev);
        prev = p; got = true;
        spanMin = Mathf.Min(spanMin, p.y); spanMax = Mathf.Max(spanMax, p.y);

        if (hand != null)
        {
            var hp = hand.position;
            if (gotH) handTotal += Vector3.Distance(hp, prevH);
            prevH = hp; gotH = true;
        }
    }

    anim.runtimeAnimatorController = prevCtrl;
    AssetDatabase.DeleteAsset(cpath);
    err = "髋路径=" + total.ToString("F3") + " 手路径=" + handTotal.ToString("F3")
          + " 髋Y范围=[" + spanMin.ToString("F2") + "," + spanMax.ToString("F2") + "]";
    return total;
}

// ============ 主测：Z_Undead 用别人的片段 ============
sb.AppendLine("========== A) Z_Undead（Humanoid）吃其他怪的片段 ==========");
var undead = MakeInstance("Assets/_Project/Prefabs/Ziyuan/Z_Undead.prefab");
{
    var anim = undead.GetComponentInChildren<Animator>(true);
    sb.AppendLine("  实例 avatar = " + (anim.avatar != null ? anim.avatar.name : "null")
                  + " isHuman=" + (anim.avatar != null && anim.avatar.isHuman));

    // 先喂它自己的片段做基线
    foreach (var c in ClipsOf(fbxUndead))
    {
        string e; float m = MeasureOnInstance(undead, c, out e);
        sb.AppendLine("  [自有基线] " + c.name.PadRight(36) + " " + e);
    }

    // 再喂 Orge / Orc 的片段
    foreach (var (fbx, tag) in new[] { (fbxOrge, "Orge"), (fbxOrc, "Orc") })
        foreach (var c in ClipsOf(fbx))
        {
            string e; float m = MeasureOnInstance(undead, c, out e);
            sb.AppendLine("  [" + tag + "→Undead] " + c.name.PadRight(44) + " " + e);
        }
}
UnityEngine.Object.DestroyImmediate(undead);

// ============ 对照：Z_Orge 吃自己的片段 ============
sb.AppendLine();
sb.AppendLine("========== B) 对照：Z_Orge 吃自己的片段（应当明显有动作）==========");
var orge = MakeInstance("Assets/_Project/Prefabs/Ziyuan/Z_Orge.prefab");
{
    var anim = orge.GetComponentInChildren<Animator>(true);
    sb.AppendLine("  实例 avatar = " + (anim.avatar != null ? anim.avatar.name : "null")
                  + " isHuman=" + (anim.avatar != null && anim.avatar.isHuman));
    foreach (var c in ClipsOf(fbxOrge))
    {
        string e; float m = MeasureOnInstance(orge, c, out e);
        sb.AppendLine("  [自有] " + c.name.PadRight(50) + " " + e);
    }
}
UnityEngine.Object.DestroyImmediate(orge);

// ============ 对照：Z_Orc 吃自己的片段 ============
sb.AppendLine();
sb.AppendLine("========== C) 对照：Z_Orc 吃自己的片段 ==========");
var orc = MakeInstance("Assets/_Project/Prefabs/Ziyuan/Z_Orc.prefab");
{
    var anim = orc.GetComponentInChildren<Animator>(true);
    sb.AppendLine("  实例 avatar = " + (anim.avatar != null ? anim.avatar.name : "null")
                  + " isHuman=" + (anim.avatar != null && anim.avatar.isHuman));
    foreach (var c in ClipsOf(fbxOrc))
    {
        string e; float m = MeasureOnInstance(orc, c, out e);
        sb.AppendLine("  [自有] " + c.name.PadRight(50) + " " + e);
    }
}
UnityEngine.Object.DestroyImmediate(orc);

AssetDatabase.DeleteAsset(TmpDir);

File.WriteAllText(Path.Combine(root, "Tools/reports/a4_retarget2.txt"), sb.ToString());
Debug.Log("[a4] 完成 " + sb.Length + " 字符\n" + sb.ToString());
