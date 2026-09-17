// 摸清 Player.prefab 的结构：根组件、Animator 的 avatar / 关键开关、
// Visual 子树（含骨骼命名）、以及"占位刀身"挂在哪个手骨下 —— 换模型前必须先知道这些。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/f_prefab_tree.txt"), sb.ToString());

    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
    if (prefab == null) { sb.AppendLine("[ERR] prefab not found"); flush(); yield break; }

    sb.AppendLine("================ Player.prefab 根组件 ================");
    foreach (var c in prefab.GetComponents<Component>())
        sb.AppendLine("  " + (c == null ? "(null)" : c.GetType().Name));

    var anim = prefab.GetComponent<Animator>();
    if (anim != null)
    {
        var av = anim.avatar;
        sb.AppendLine("  Animator.avatar = " + (av == null ? "NULL" : av.name + "  valid=" + av.isValid + " human=" + av.isHuman));
        if (av != null)
        {
            var p = AssetDatabase.GetAssetPath(av);
            sb.AppendLine("  avatar 资产路径 = " + (string.IsNullOrEmpty(p) ? "(内嵌于模型)" : p));
        }
        sb.AppendLine("  applyRootMotion = " + anim.applyRootMotion);
        sb.AppendLine("  cullingMode     = " + anim.cullingMode);
        sb.AppendLine("  updateMode      = " + anim.updateMode);
        sb.AppendLine("  runtimeController = " + (anim.runtimeAnimatorController == null ? "NULL" : anim.runtimeAnimatorController.name));
    }
    else sb.AppendLine("  Animator = 无");

    sb.AppendLine();
    sb.AppendLine("================ Visual 子树（深度 3） ================");
    var vis = prefab.transform.Find("Visual");
    if (vis == null) sb.AppendLine("  没有 Visual！");
    else
    {
        sb.AppendLine("  Visual localPosition=" + vis.localPosition.ToString("F4") + " localScale=" + vis.localScale.ToString("F4"));
        Tree(vis, sb, 0, 3);
    }

    sb.AppendLine();
    sb.AppendLine("================ 全层次里的 Renderer / Animator / 非骨骼名 ================");
    foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
    {
        string n = t.name;
        bool interesting = t.GetComponent<Renderer>() != null
            || t.GetComponent<Animator>() != null
            || n.ToLower().Contains("sword") || n.ToLower().Contains("blade") || n.ToLower().Contains("weapon")
            || n.ToLower().Contains("trail") || n.ToLower().Contains("vfx") || n.ToLower().Contains("fx")
            || n.ToLower().Contains("hitbox") || n.ToLower().Contains("root");
        if (!interesting) continue;
        var comps = new List<string>();
        foreach (var c in t.GetComponents<Component>())
            if (c != null && !(c is Transform)) comps.Add(c.GetType().Name);
        sb.AppendLine("  " + FullPath(t, prefab.transform) + "  [" + string.Join(",", comps.ToArray()) + "]");
    }

    flush();
    Debug.Log("[tree] 完成，见 Tools/reports/f_prefab_tree.txt  行数=" + sb.ToString().Split('\n').Length);
    yield return null;
}

void Tree(Transform t, System.Text.StringBuilder sb, int d, int maxD)
{
    string pad = new string(' ', 4 + d * 2);
    var comps = new List<string>();
    foreach (var c in t.GetComponents<Component>())
        if (c != null && !(c is Transform)) comps.Add(c.GetType().Name);
    sb.AppendLine(pad + t.name + "  [" + string.Join(",", comps.ToArray()) + "]");
    if (d >= maxD) { if (t.childCount > 0) sb.AppendLine(pad + "  ... (" + t.childCount + " 子节点)"); return; }
    foreach (Transform c in t) Tree(c, sb, d + 1, maxD);
}

string FullPath(Transform t, Transform root)
{
    var s = t.name;
    while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

return Body();
