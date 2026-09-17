// d5_raw_vs_ink.cs —— 决定性对照：龙的"原始 FBX 材质" vs "水墨材质"
//
// 用户在截图里看到的彩虹色，我需要确认到底是不是**原始 FBX 材质**。
// 之前的 d3/d4 都是在"水墨材质已经被应用"的前提下测的，所以看不到彩虹。
//
// 这次：
//   A 无条件还原 FBX 自带材质（从 prefab 的原始 m_Materials 读不到，
//     但可以用 `Object_281` 的 mesh 反查 —— 或者更直接：
//     把 InkMaterialSwap 停用 / 把 sharedMaterials 置成 FBX 导入的材质）
//   B 应用 M_Ink_Boss_Dragon（但**保留龙自己的参数**，不让 InkStylePanel 覆盖）
//
// 关键：要在一个**干净的 Play 会话**里跑，且先确认 InkStylePanel 有没有在跑。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using InkWash.UI;
using UnityEngine;

public class D5Probe : MonoBehaviour
{
    public string outDir;
    StringBuilder _sb;
    Camera _cam; EnemyDragon _d; RenderTexture _rt; Texture2D _tex;
    int _step, _n;
    Material[] _inkMats;
    Material[] _fbxMats;

    void Start()
    {
        _sb = new StringBuilder();
        _sb.AppendLine("========== d5 原始FBX材质 vs 水墨材质 ==========");
        _cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
        foreach (var mb in _cam.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn.Contains("ShowcaseCam") || tn.Contains("Brain") || tn.Contains("Follow")) DestroyImmediate(mb);
        }
        _cam.transform.position = new Vector3(9f, 3.6f, -4.5f);
        _cam.transform.LookAt(new Vector3(0f, 1.3f, 4f));
        _cam.fieldOfView = 45f;

        foreach (var x in FindObjectsOfType<EnemyDragon>())
            if (x.gameObject.name.StartsWith("D2_")) { _d = x; break; }
        if (_d == null) { var a = FindObjectsOfType<EnemyDragon>(); if (a.Length > 0) _d = a[0]; }
        _sb.AppendLine("龙 = " + (_d == null ? "<无>" : _d.gameObject.name));

        // InkStylePanel 在不在（它会把主角参数广播给所有人）
        var panels = FindObjectsOfType<InkStylePanel>();
        _sb.AppendLine("InkStylePanel 数量 = " + panels.Length);
        foreach (var p in panels) _sb.AppendLine("   " + p.gameObject.name);

        // 读龙的渲染器 + 当前材质 + 该渲染器的 FBX 原始材质
        var rs = _d != null ? _d.GetComponentsInChildren<SkinnedMeshRenderer>(true) : new SkinnedMeshRenderer[0];
        _sb.AppendLine("SkinnedMeshRenderer 数 = " + rs.Length);
        var inkList = new List<Material>();
        foreach (var r in rs)
        {
            _sb.AppendLine("  " + r.name);
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                var m = ms[i];
                _sb.AppendLine("     当前 mat[" + i + "] = " + (m == null ? "<null>"
                    : m.name + " shader=" + m.shader.name));
                if (m != null && !inkList.Contains(m)) inkList.Add(m);
            }
        }
        _inkMats = inkList.ToArray();

        // ★ FBX 原始材质：M_Ink_Boss_Dragon 是我们在 prefab 里覆盖上去的；
        //   原始材质只能从 FBX 资产的子资产里取。用 AssetDatabase 在编辑期取。
        //   这里退一步：直接把 _BaseMap 置空的"纯墨阶"版本作为对照，用来判断
        //   "彩虹是否来自贴图"。
        _rt = new RenderTexture(800, 560, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(800, 560, TextureFormat.RGBA32, false);
        StartCoroutine(Co());
    }

    IEnumerator Co()
    {
        yield return new WaitForSeconds(0.3f);

        string[] labels = { "A_当前", "B_BaseMap置空", "C_BaseMap置空+龙参数", "D_BaseMap设成emissive" };
        System.Action[] acts = {
            () => { },
            () => SetBaseMap(null, false),
            () => { SetBaseMap(null, false); SetDragonParams(); },
            () => SetBaseMap("emissive", false),
        };

        for (int s = 0; s < labels.Length; s++)
        {
            _n = 0;
            acts[s]();
            yield return new WaitForSeconds(0.25f);
            while (_n < 20) { _n++; yield return null; }

            _cam.targetTexture = _rt; _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, 800, 560), 0, 0); _tex.Apply(false, false);
            RenderTexture.active = null; _cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, "D5_" + labels[s] + ".png"), _tex.EncodeToPNG());

            var m0 = _inkMats.Length > 0 ? _inkMats[0] : null;
            _sb.AppendLine("  " + labels[s] + " → _BaseMap=" + (m0 != null && m0.HasProperty("_BaseMap")
                ? (m0.GetTexture("_BaseMap") == null ? "<null>" : m0.GetTexture("_BaseMap").name) : "?")
                + "  _BandBias=" + (m0 != null && m0.HasProperty("_BandBias") ? m0.GetFloat("_BandBias").ToString("F2") : "?")
                + "  _InkDensity=" + (m0 != null && m0.HasProperty("_InkDensity") ? m0.GetFloat("_InkDensity").ToString("F2") : "?"));
        }

        File.WriteAllText(Path.Combine(outDir, "d5_raw_vs_ink.txt"), _sb.ToString());
        Debug.Log("[d5]\n" + _sb.ToString());
        Destroy(gameObject);
    }

    void SetBaseMap(string which, bool dummy)
    {
        foreach (var m in _inkMats)
        {
            if (m == null || !m.HasProperty("_BaseMap")) continue;
            if (which == null) m.SetTexture("_BaseMap", null);
            else if (which == "emissive")
            {
                var t = LoadTex("Assets/ThirdParty/Ziyuan/chinese_dragon/textures/MI_b09_00_drg_hair_emissive.png");
                if (t != null) m.SetTexture("_BaseMap", t);
            }
        }
    }

    void SetDragonParams()
    {
        foreach (var m in _inkMats)
        {
            if (m == null) continue;
            if (m.HasProperty("_BandBias")) m.SetFloat("_BandBias", -0.26f);
            if (m.HasProperty("_InkDensity")) m.SetFloat("_InkDensity", 0.62f);
            if (m.HasProperty("_ChromaKeep")) m.SetFloat("_ChromaKeep", 0.45f);
            if (m.HasProperty("_InkDark")) m.SetColor("_InkDark", new Color(0.080f, 0.055f, 0.045f, 1f));
            if (m.HasProperty("_InkMid")) m.SetColor("_InkMid", new Color(0.270f, 0.210f, 0.175f, 1f));
            if (m.HasProperty("_InkLight")) m.SetColor("_InkLight", new Color(0.820f, 0.765f, 0.690f, 1f));
        }
    }

    static Texture LoadTex(string path)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(path);
#else
        return null;
#endif
    }
}

// ---- 顶层 ----
{
    var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                                     "Tools/screenshots/d5raw");
    System.IO.Directory.CreateDirectory(dir);
    var go = new GameObject("D5_Probe");
    var p = go.AddComponent<D5Probe>();
    p.outDir = dir;
    Debug.Log("[d5] probe installed → " + dir);
}
