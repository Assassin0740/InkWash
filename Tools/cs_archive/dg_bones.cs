using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_bones：直接打印墨龙的**骨骼层级真值**，确认 spineRoot 指到哪、drgon_* 在哪、
//           以及 _spine 为什么是空的。
public class dg_bones : MonoBehaviour
{
    Component _dragon;
    int _step; float _t;
    readonly StringBuilder _sb = new StringBuilder();

    void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("InkWash.Enemies.EnemyDragon");
            if (t == null) continue;
            foreach (var c in FindObjectsOfType(t)) { _dragon = c as Component; break; }
            if (_dragon != null) break;
        }
        if (_dragon == null) { Debug.Log("dg_bones ✗ 没找到 EnemyDragon"); enabled = false; return; }
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_step != 0 || _t < 2.0f) return;
        _step = 1;

        var ty = _dragon.GetType();
        _sb.AppendLine("=== " + _dragon.gameObject.name + " ===");

        // spineRoot 字段
        var fRoot = ty.GetField("spineRoot", BindingFlags.Public | BindingFlags.Instance);
        var root = fRoot != null ? fRoot.GetValue(_dragon) as Transform : null;
        _sb.AppendLine("spineRoot = " + (root == null ? "<null>" : root.name));

        var fFound = ty.GetField("_spineLinksFound", BindingFlags.NonPublic | BindingFlags.Instance);
        _sb.AppendLine("_spineLinksFound = " + (fFound != null ? fFound.GetValue(_dragon).ToString() : "?"));

        var fSpine = ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        var raw = fSpine != null ? fSpine.GetValue(_dragon) as System.Collections.ICollection : null;
        _sb.AppendLine("_spine.Count = " + (raw != null ? raw.Count.ToString() : "null"));

        // 全部 SkinnedMeshRenderer
        _sb.AppendLine("── SkinnedMeshRenderer ──");
        foreach (var smr in _dragon.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            _sb.AppendLine(string.Format("  {0}  rootBone={1}  bones={2}",
                smr.name, smr.rootBone != null ? smr.rootBone.name : "<null>",
                smr.bones != null ? smr.bones.Length : 0));

        // 骨骼层级（带缩进）
        _sb.AppendLine("── 骨骼层级（drgon_* / 含 rootJoint 附近）──");
        var all = _dragon.GetComponentsInChildren<Transform>(true);
        _sb.AppendLine("  Transform 总数 = " + all.Length);
        int shown = 0;
        foreach (var tr in all)
        {
            string n = tr.name;
            bool interesting = n.StartsWith("drgon") || n == "_rootJoint" || n.Contains("Root")
                               || n.Contains("Skeleton") || n.Contains("Armature") || n.Contains("Visual")
                               || n.Contains("Model");
            if (!interesting) continue;
            if (shown++ > 90) { _sb.AppendLine("  ...(截断)"); break; }
            _sb.AppendLine("  " + Depth(tr, _dragon.transform) + n
                + "   localPos=(" + tr.localPosition.x.ToString("F2") + ","
                + tr.localPosition.y.ToString("F2") + "," + tr.localPosition.z.ToString("F2") + ")"
                + "   children=" + tr.childCount);
        }

        Debug.Log(_sb.ToString());
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_bones.txt", _sb.ToString()); } catch { }
        enabled = false;
    }

    static string Depth(Transform t, Transform root)
    {
        int d = 0; var c = t;
        while (c != null && c != root) { d++; c = c.parent; }
        return new string(' ', Mathf.Min(d * 2, 40));
    }
}

var g = new GameObject("dg_bones");
g.AddComponent<dg_bones>();
return "DG_BONES_STARTED";
