// 骨架实况：换到 Char_Feng（CC_Base 骨架）之后，把右手相关的骨骼连同世界坐标打出来。
// 目的：确认 w_grip.cs 按名字抓到的指骨到底在哪 —— 上一轮量出"腕到拳心 0.69m"，显然抓错了。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[w_tree] no Player"); yield break; }

    var all = go.GetComponentsInChildren<Transform>(true);
    sb.AppendLine("========== Player 子层级 Transform 总数 = " + all.Length + " ==========");

    var anim = go.GetComponent<Animator>();
    sb.AppendLine("  Animator.avatar = " + (anim != null && anim.avatar != null ? anim.avatar.name : "(null)"));
    if (anim != null)
    {
        var h = anim.GetBoneTransform(HumanBodyBones.RightHand);
        sb.AppendLine("  GetBoneTransform(RightHand) = " + (h != null ? h.name + " @ " + h.position.ToString("F4") : "(null)"));
        var l = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        sb.AppendLine("  GetBoneTransform(RightLowerArm) = " + (l != null ? l.name + " @ " + l.position.ToString("F4") : "(null)"));
        var av = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
        sb.AppendLine("  GetBoneTransform(RightThumbProximal) = " + (av != null ? av.name + " @ " + av.position.ToString("F4") : "(null)"));
    }

    // 所有含关键字的骨骼
    string[] keys = { "Hand", "Index", "Pinky", "Thumb", "Mid", "Ring", "Forearm", "Upperarm", "Clavicle", "Wrist" };
    sb.AppendLine();
    sb.AppendLine("========== 含关键词的骨骼（名 / 世界坐标 / 父） ==========");
    var hits = new List<Transform>();
    foreach (var t in all)
    {
        foreach (var k in keys)
            if (t.name.Contains(k)) { hits.Add(t); break; }
    }
    hits.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    foreach (var t in hits)
        sb.AppendLine("  " + t.name.PadRight(34) + " " + t.position.ToString("F4") + "   parent=" + (t.parent != null ? t.parent.name : "-"));
    sb.AppendLine("  共 " + hits.Count + " 个");

    // 以 RightHand 为根，深度 ≤ 3 的子树
    if (anim != null)
    {
        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand != null)
        {
            sb.AppendLine();
            sb.AppendLine("========== RightHand 子树（深度 ≤ 4）==========");
            Dump(hand, 0, 4, sb);
        }
    }

    // Visual 之下的直接子节点（看清楚有几个骨架/模型）
    sb.AppendLine();
    sb.AppendLine("========== Visual 之下（深度 2）==========");
    var vis = go.transform.Find("Visual");
    if (vis != null) foreach (Transform c in vis) sb.AppendLine("  " + c.name + "  [" + Kind(c) + "]  children=" + c.childCount);
    else sb.AppendLine("  (没有 Visual)");

    Debug.Log(sb.ToString());
    yield return null;
}

void Dump(Transform t, int d, int maxD, System.Text.StringBuilder sb)
{
    sb.AppendLine("  " + new string(' ', d * 2) + t.name + " @ " + t.position.ToString("F4"));
    if (d >= maxD) return;
    foreach (Transform c in t) Dump(c, d + 1, maxD, sb);
}

string Kind(Transform t)
{
    var s = "";
    if (t.GetComponent<SkinnedMeshRenderer>() != null) s += "SkinnedMesh ";
    if (t.GetComponent<MeshRenderer>() != null) s += "Mesh ";
    if (t.GetComponent<Animator>() != null) s += "Animator ";
    return s.Trim();
}

return Body();
