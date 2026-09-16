// d1_dragon_mat.cs —— 龙为什么是彩色的：运行时材质取证
//
// 用户报「龙为什么变成这样了」（截图里龙是灰/青/红/白/棕的分段彩虹色）。
// 文件层面已查：
//   · Z_Enemy_MoLong.prefab 里只有 2 个渲染器，都指向 M_Ink_Boss_Dragon
//   · M_Ink_Boss_Dragon 参数正常（_InkDark/_InkMid/_InkLight 都设了）
//   · 但它挂的 _BaseMap = MI_b09_00_drg_hair_clearcoat.png（一张**青色的毛发光罩贴图**）
//   · glTF 里龙体只有 2 个材质，body 是 baseColorFactor 深红棕、**没有 body albedo 贴图**
//
// 所以要打**运行时真值**：场上那条龙每个渲染器的 shader / 材质名 / 关键参数 / 贴图。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEngine;

public class D1Probe : MonoBehaviour
{
    public string outPath;
    int _n;
    StringBuilder _sb;

    void Start() { _sb = new StringBuilder(); _sb.AppendLine("========== d1 龙的运行时材质取证 =========="); }

    void Update()
    {
        if (++_n < 20) return;

        var dragons = FindObjectsOfType<EnemyDragon>();
        _sb.AppendLine("场上 EnemyDragon 数量 = " + dragons.Length);

        foreach (var d in dragons)
        {
            _sb.AppendLine();
            _sb.AppendLine("──── " + d.gameObject.name + " ────");
            _sb.AppendLine("  位置=" + d.transform.position.ToString("F2")
                        + "  本地缩放=" + d.transform.localScale.ToString("F3")
                        + "  世界缩放=" + d.transform.lossyScale.ToString("F3"));
            _sb.AppendLine("  HP=" + d.Health + "/" + d.maxHealth
                        + "  airborne=" + d.IsAirborneForTest
                        + "  attack=" + d.CurrentAttackName);

            var rends = d.GetComponentsInChildren<Renderer>(true);
            _sb.AppendLine("  渲染器数 = " + rends.Length);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                var mats = r.sharedMaterials;
                _sb.AppendLine("  [" + i + "] " + r.GetType().Name + " @ " + Hier(r.transform, d.transform));
                _sb.AppendLine("       size=" + r.bounds.size.ToString("F2") + "  enabled=" + r.enabled);
                for (int j = 0; j < mats.Length; j++)
                {
                    var m = mats[j];
                    if (m == null) { _sb.AppendLine("       mat[" + j + "] = <null>"); continue; }
                    _sb.AppendLine("       mat[" + j + "] '" + m.name + "'  shader='" + m.shader.name + "'");
                    DumpMat(m);
                }
            }
        }

        _sb.AppendLine();
        _sb.AppendLine("──── 场景里所有名字含 Dragon/MoLong 的对象 ────");
        foreach (var g in FindObjectsOfType<GameObject>())
        {
            if (g == null) continue;
            string n = g.name.ToLower();
            if (n.Contains("dragon") || n.Contains("molong"))
                _sb.AppendLine("  " + (g.activeInHierarchy ? "· " : "× ") + Hier(g.transform, null));
        }

        File.WriteAllText(outPath, _sb.ToString());
        Debug.Log("[d1]\n" + _sb.ToString());
        Destroy(gameObject);
    }

    static string Hier(Transform t, Transform stopAt)
    {
        var parts = new List<string>();
        var c = t;
        while (c != null && c != stopAt) { parts.Insert(0, c.name); c = c.parent; }
        return string.Join("/", parts.ToArray());
    }

    void DumpMat(Material m)
    {
        string pad = "         ";
        string[] fs = { "_ChromaKeep", "_BandBias", "_InkDensity", "_Bands", "_OutlineWidth", "_SpecStrength" };
        foreach (var f in fs)
            if (m.HasProperty(f)) _sb.AppendLine(pad + f + " = " + m.GetFloat(f).ToString("F3"));
        if (m.HasProperty("_BaseColor")) _sb.AppendLine(pad + "_BaseColor = " + m.GetColor("_BaseColor").ToString("F3"));
        if (m.HasProperty("_InkDark"))   _sb.AppendLine(pad + "_InkDark   = " + m.GetColor("_InkDark").ToString("F3"));
        if (m.HasProperty("_InkMid"))    _sb.AppendLine(pad + "_InkMid    = " + m.GetColor("_InkMid").ToString("F3"));
        if (m.HasProperty("_InkLight"))  _sb.AppendLine(pad + "_InkLight  = " + m.GetColor("_InkLight").ToString("F3"));
        if (m.HasProperty("_BaseMap"))
        {
            var t = m.GetTexture("_BaseMap");
            _sb.AppendLine(pad + "_BaseMap = " + (t == null ? "<null>" : t.name));
        }
        var kw = m.shaderKeywords;
        if (kw != null && kw.Length > 0) _sb.AppendLine(pad + "keywords = " + string.Join(",", kw));
    }
}

// ---- 顶层：装探针 ----
{
    var go = new GameObject("D1_Probe");
    var p = go.AddComponent<D1Probe>();
    p.outPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                                       "Tools/reports/d1_dragon_mat.txt");
    Debug.Log("[d1] probe installed");
}
