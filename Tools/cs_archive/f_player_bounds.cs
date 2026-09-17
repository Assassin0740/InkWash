// 查 Player.prefab 里每个 Renderer 的世界包围盒，找出把身高数字撑大的东西
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
    if (prefab == null) { Debug.LogError("no prefab"); yield break; }
    var inst = Object.Instantiate(prefab);
    inst.transform.position = Vector3.zero;
    inst.transform.rotation = Quaternion.identity;

    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
    {
        var b = r.bounds;
        sb.AppendLine(string.Format("{0,-42} {1,-18} y {2,8:F4}..{3,8:F4} (高 {4,7:F4})  x {5,8:F4}..{6,8:F4}  z {7,8:F4}..{8,8:F4}",
            Path(r.transform, inst.transform), r.GetType().Name,
            b.min.y, b.max.y, b.size.y, b.min.x, b.max.x, b.min.z, b.max.z));
    }
    sb.AppendLine("---- 仅 MeshRenderer/SkinnedMeshRenderer，且排除名字含 Vfx/Trail/Sword/Weapon ----");
    float mn = float.MaxValue, mx = float.MinValue;
    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
    {
        string n = r.name.ToLower();
        if (n.Contains("vfx") || n.Contains("trail") || n.Contains("sword") || n.Contains("weapon") || n.Contains("katana")) continue;
        if (r is TrailRenderer) continue;
        mn = Mathf.Min(mn, r.bounds.min.y); mx = Mathf.Max(mx, r.bounds.max.y);
    }
    sb.AppendLine(string.Format("过滤后：y {0:F4}..{1:F4}  身高 {2:F4}", mn, mx, mx - mn));

    // 只看 Visual 子树
    var vis = inst.transform.Find("Visual");
    if (vis != null)
    {
        float vmn = float.MaxValue, vmx = float.MinValue;
        foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
        { if (r is TrailRenderer) continue; vmn = Mathf.Min(vmn, r.bounds.min.y); vmx = Mathf.Max(vmx, r.bounds.max.y); }
        sb.AppendLine(string.Format("Visual 子树：y {0:F4}..{1:F4}  身高 {2:F4}", vmn, vmx, vmx - vmn));
        sb.AppendLine("Visual 直接子节点：");
        foreach (Transform c in vis) sb.AppendLine("   " + c.name + "  scale=" + c.localScale.ToString("F4") + " pos=" + c.localPosition.ToString("F4"));
    }

    // 骨骼：Head 骨的世界高度（判断真实"人形"身高）
    foreach (var t in inst.GetComponentsInChildren<Transform>(true))
        if (t.name.ToLower().Contains("head") || t.name.ToLower().Contains("hips") || t.name.ToLower().Contains("foot"))
            sb.AppendLine(string.Format("  骨骼 {0,-28} 世界 y={1:F4}", Path(t, inst.transform), t.position.y));

    Object.DestroyImmediate(inst);
    Debug.Log(sb.ToString());
    yield return null;
}

string Path(Transform t, Transform root)
{
    var s = t.name;
    var p = t.parent;
    int guard = 0;
    while (p != null && p != root && guard++ < 8) { s = p.name + "/" + s; p = p.parent; }
    return s;
}

return Body();
