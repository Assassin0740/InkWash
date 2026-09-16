using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class d7_probe : MonoBehaviour
{
    static readonly StringBuilder sb = new StringBuilder();
    float _t;
    bool _logged;

    void Update()
    {
        _t += Time.unscaledDeltaTime;

        bool dragonHere = false;
        foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (r == null) continue;
            if (r.name.IndexOf("MoLong", StringComparison.OrdinalIgnoreCase) >= 0) { dragonHere = true; break; }
        }
        if (!dragonHere && _t < 90f) return;
        if (_logged) return;
        _logged = true;
        Run();
    }

    void Run()
    {
        sb.Length = 0;
        sb.AppendLine("========== d7 材质族修复后 · 龙的 runtime 材质 ==========");
        sb.AppendLine("等待时长 = " + _t.ToString("0.0") + " s");

        var panelT = Type.GetType("InkWash.UI.InkStylePanel, Assembly-CSharp");
        if (panelT != null)
        {
            var panels = UnityEngine.Object.FindObjectsOfType(panelT);
            sb.AppendLine("InkStylePanel 数量 = " + panels.Length);
            foreach (var p in panels)
            {
                var fc = panelT.GetProperty("FamilyCount");
                var efn = panelT.GetProperty("EditingFamilyName");
                var rc = panelT.GetProperty("RegisteredCount");
                sb.AppendLine("  " + ((Component)p).gameObject.name
                    + "  Registered=" + (rc != null ? rc.GetValue(p).ToString() : "?")
                    + "  Families=" + (fc != null ? fc.GetValue(p).ToString() : "?")
                    + "  Editing=[" + (efn != null ? efn.GetValue(p).ToString() : "?") + "]");
            }
        }
        else sb.AppendLine("✗ 找不到 InkStylePanel");

        int dragons = 0;
        foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (r == null) continue;
            if (r.name.IndexOf("MoLong", StringComparison.OrdinalIgnoreCase) < 0
             && r.name.IndexOf("Dragon", StringComparison.OrdinalIgnoreCase) < 0) continue;
            dragons++;
            DumpRecursive(r.transform, "  ");
        }
        if (dragons == 0) sb.AppendLine("（场上没有龙）");

        Debug.Log(sb.ToString());
    }

    static void DumpRecursive(Transform tr, string pad)
    {
        foreach (var rd in tr.GetComponents<Renderer>())
        {
            bool ink = false;
            foreach (var m in rd.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name.StartsWith("InkWash/")) { ink = true; break; }
            if (!ink) continue;

            sb.AppendLine(pad + "──────── " + PathOf(tr) + " ────────");
            var mats = rd.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                sb.AppendLine(pad + "  mat[" + i + "] '" + m.name + "' shader='" + m.shader.name + "'");
                Flo(m, pad + "    ", "_BandBias");
                Flo(m, pad + "    ", "_InkDensity");
                Flo(m, pad + "    ", "_ChromaKeep");
                Flo(m, pad + "    ", "_Bands");
                Col(m, pad + "    ", "_InkDark");
                Col(m, pad + "    ", "_InkMid");
                Col(m, pad + "    ", "_InkLight");
                var bt = m.GetTexture("_BaseMap");
                sb.AppendLine(pad + "    _BaseMap = " + (bt == null ? "<null>" : bt.name));
            }
        }
        for (int i = 0; i < tr.childCount; i++) DumpRecursive(tr.GetChild(i), pad);
    }

    static void Flo(Material m, string pad, string n)
    {
        if (!m.HasProperty(n)) return;
        sb.AppendLine(pad + n + " = " + m.GetFloat(n).ToString("0.###"));
    }
    static void Col(Material m, string pad, string n)
    {
        if (!m.HasProperty(n)) return;
        var c = m.GetColor(n);
        sb.AppendLine(pad + n + " = (" + c.r.ToString("0.###") + ", " + c.g.ToString("0.###") + ", " + c.b.ToString("0.###") + ")");
    }
    static string PathOf(Transform t)
    {
        string s = t.name;
        var p = t.parent;
        int g = 0;
        while (p != null && g++ < 6) { s = p.name + "/" + s; p = p.parent; }
        return s;
    }
}

var go7 = new GameObject("D7_Probe");
go7.AddComponent<d7_probe>();
return "D7_STARTED";
