// g-3：运行时材质实参体检 —— 上一轮改的 _BandBias 到底有没有真的落到渲染上
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.CameraRig;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);

string[] kKeys =
{
    "_BandBias", "_InkDensity", "_ChromaKeep", "_Bands", "_LadderSkew",
    "_GrainScale", "_GrainAmp", "_StrokeScale", "_StrokeStretch", "_StrokeAmp",
    "_InkMottle", "_MottleScale", "_OutlineWidth", "_OutlineFacing", "_OutlineDry",
    "_ContactInk", "_ContactHeight", "_GroundY", "_SpecStrength", "_RimStrength",
};

string[] kCols = { "_InkDark", "_InkMid", "_InkLight", "_OutlineColor", "_RimColor", "_BaseColor" };

IEnumerator Body()
{
    yield return null; yield return null;

    string PathOf(Transform t)
    {
        var s = t.name; var p = t.parent;
        while (p != null) { s = p.name + "/" + s; p = p.parent; }
        return s;
    }

    sb.AppendLine("=== 运行时材质实参 ===");
    var mats = new List<Material>();
    var seen = new HashSet<int>();
    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
    {
        var m = r.sharedMaterial;
        if (m == null) continue;
        if (!m.shader.name.StartsWith("InkWash/")) continue;
        if (!seen.Add(m.GetInstanceID())) continue;
        mats.Add(m);
    }
    foreach (var m in mats)
    {
        sb.AppendLine();
        sb.AppendLine("--- " + m.name + "   shader=" + m.shader.name);
        var parts = new List<string>();
        foreach (var k in kKeys)
        {
            if (!m.HasProperty(k)) continue;
            float v = 0f;
            try { v = m.GetFloat(k); } catch { continue; }
            parts.Add(k + "=" + v.ToString("0.###"));
        }
        for (int i = 0; i < parts.Count; i += 4)
            sb.AppendLine("    " + string.Join("  ", parts.GetRange(i, Math.Min(4, parts.Count - i)).ToArray()));
        foreach (var k in kCols)
        {
            if (!m.HasProperty(k)) continue;
            Color c;
            try { c = m.GetColor(k); } catch { continue; }
            sb.AppendLine("    " + k + " = " + c.r.ToString("0.###") + ", " + c.g.ToString("0.###") + ", " + c.b.ToString("0.###"));
        }
    }

    sb.AppendLine();
    sb.AppendLine("=== 角色渲染器实际引用 ===");
    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
    {
        var p = PathOf(r.transform);
        if (p.IndexOf("Player", StringComparison.OrdinalIgnoreCase) < 0) continue;
        if (r.sharedMaterial == null) continue;
        sb.AppendLine("  " + p + "  → " + r.sharedMaterial.name + " (instance=" + !r.sharedMaterial.name.EndsWith("Ink") + ")");
    }

    sb.AppendLine();
    sb.AppendLine("=== 场景几何尺寸（算纹理尺度用）===");
    foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
    {
        var b = r.bounds;
        var p = PathOf(r.transform);
        if (p.IndexOf("Environment") < 0 && p.IndexOf("Gates") < 0 && p.IndexOf("Gate") < 0) continue;
        sb.AppendLine("  " + p + "  size=" + b.size.ToString("F2"));
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_mat.txt"), sb.ToString());
    Debug.Log("[g_mat] done\n" + sb.ToString());
    yield return null;
}

return Body();
