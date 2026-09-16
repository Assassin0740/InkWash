// d3_dragon_fix_probe.cs —— 龙的彩虹色拆解：逐个变量回滚，看谁贡献了什么
//
// 【已确认的两个缺陷】
//  ① 龙的墨阶参数被**主角的**覆盖了
//     —— `InkStylePanel.ApplyStage()` 里 `if (_edit != null) Broadcast()`，
//        而 `_edit` 是 `inkMaterial`（Inspector 里配的**主角**材质）的副本
//        ⇒ 每次切阶段都把主角的 `_BandBias/_InkDensity/_InkDark/_InkMid/_InkLight`
//          盖到**所有**敌人头上。
//     d1 实测：龙 runtime 读到 _BandBias 0.150 / _InkDensity 0.550 /
//              _InkDark (0.045,0.065,0.135) —— 与 M_Character_Ink 逐位相同，
//              而 M_Ink_Boss_Dragon 自己写的是 _BandBias -0.26 / _InkDensity 0.62 /
//              _InkDark (0.08,0.055,0.045)。
//  ② `_BaseMap` 挂的是 `MI_b09_00_drg_hair_clearcoat`（青色**毛发光罩**贴图），
//     而这条龙**根本没有 body albedo 贴图**（glTF 里 body 是 baseColorFactor 深红棕）。
//     `_InkDensity 0.55` 会放 55% 的贴图信息进来 ⇒ 那张青图直接染到整个龙身。
//
// 本脚本：把这两个变量的四种组合各渲一张图存盘，让人眼直接判谁是主因。
//   变体 A：现状（主角参数 + 青贴图）
//   变体 B：只修参数（龙自己的参数 + 青贴图）
//   变体 C：只修贴图（主角参数 + 贴图置空）
//   变体 D：都修（龙自己的参数 + 贴图置空）
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEngine;

public class D3Probe : MonoBehaviour
{
    public string outDir;
    int _n;
    StringBuilder _sb;
    Camera _cam;
    EnemyDragon _d;
    RenderTexture _rt;
    Texture2D _tex;
    Material _dragonMat;
    Material _charMat;
    int _shot;
    readonly int[] _warmup = { 25, 40, 55, 70 };

    void Start()
    {
        _sb = new StringBuilder();
        _sb.AppendLine("========== d3 龙的彩虹色拆解 ==========");
        _cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();

        foreach (var mb in _cam.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn.Contains("ShowcaseCam") || tn.Contains("Brain") || tn.Contains("Follow"))
                DestroyImmediate(mb);
        }
        // 机位：侧面看整条龙（龙长轴 X、头朝 +Z）
        _cam.transform.position = new Vector3(12f, 4.2f, -3f);
        _cam.transform.LookAt(new Vector3(0f, 1.3f, 4f));
        _cam.fieldOfView = 45f;

        // 找龙
        var ds = FindObjectsOfType<EnemyDragon>();
        foreach (var x in ds) if (x.gameObject.name.StartsWith("D2_")) { _d = x; break; }
        if (_d == null && ds.Length > 0) _d = ds[0];
        _sb.AppendLine("龙 = " + (_d == null ? "<未找到>" : _d.gameObject.name));

        // 找龙的运行时材质（第一个 SkinnedMeshRenderer 的 sharedMaterial）
        if (_d != null)
        {
            foreach (var r in _d.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMaterial != null) { _dragonMat = r.sharedMaterial; break; }
            }
        }
        _sb.AppendLine("龙材质 = " + (_dragonMat == null ? "<null>" : _dragonMat.name));

        _rt = new RenderTexture(880, 620, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(880, 620, TextureFormat.RGBA32, false);

        _sb.AppendLine();
        _sb.AppendLine("── 变体 A：现状（主角参数 + 青贴图）");
        Dump("  ");
        Shooter();
    }

    void Dump(string pad)
    {
        if (_dragonMat == null) return;
        _sb.AppendLine(pad + "_BandBias=" + _dragonMat.GetFloat("_BandBias").ToString("F3")
                     + "  _InkDensity=" + _dragonMat.GetFloat("_InkDensity").ToString("F3")
                     + "  _ChromaKeep=" + _dragonMat.GetFloat("_ChromaKeep").ToString("F3"));
        _sb.AppendLine(pad + "_InkDark=" + _dragonMat.GetColor("_InkDark").ToString("F3")
                     + "  _InkMid=" + _dragonMat.GetColor("_InkMid").ToString("F3")
                     + "  _InkLight=" + _dragonMat.GetColor("_InkLight").ToString("F3"));
        var t = _dragonMat.GetTexture("_BaseMap");
        _sb.AppendLine(pad + "_BaseMap=" + (t == null ? "<null>" : t.name));
    }

    void Shooter() { StartCoroutine(Co()); }

    IEnumerator Co()
    {
        yield return new WaitForSeconds(0.2f);
        while (_shot < 4)
        {
            int need = _warmup[_shot];
            if (_n < need) { _n++; yield return null; continue; }

            _cam.targetTexture = _rt;
            _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, 880, 620), 0, 0);
            _tex.Apply(false, false);
            RenderTexture.active = null;
            _cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, "D3_" + "ABCD"[_shot] + ".png"), _tex.EncodeToPNG());

            _shot++;
            if (_shot < 4) { ApplyVariant(_shot); _sb.AppendLine(); _sb.AppendLine("── 变体 " + "ABCD"[_shot]); Dump("  "); }
        }
        File.WriteAllText(Path.Combine(outDir, "d3_dragon_fix.txt"), _sb.ToString());
        Debug.Log("[d3]\n" + _sb.ToString());
        Destroy(gameObject);
    }

    void ApplyVariant(int v)
    {
        if (_dragonMat == null) return;
        var all = new List<Material>();
        foreach (var r in _d.GetComponentInChildren<SkinnedMeshRenderer>(true).transform.root
                          .GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var mm in r.sharedMaterials) if (mm != null && !all.Contains(mm)) all.Add(mm);

        // 龙自己的正确参数（抄自 M_Ink_Boss_Dragon.mat）
        bool fixParams = (v == 1 || v == 3);
        bool fixTex    = (v == 2 || v == 3);
        foreach (var m in all)
        {
            if (fixParams)
            {
                m.SetFloat("_BandBias", -0.26f);
                m.SetFloat("_InkDensity", 0.62f);
                m.SetFloat("_ChromaKeep", 0.45f);
                m.SetColor("_InkDark",  new Color(0.080f, 0.055f, 0.045f, 1f));
                m.SetColor("_InkMid",   new Color(0.270f, 0.210f, 0.175f, 1f));
                m.SetColor("_InkLight", new Color(0.820f, 0.765f, 0.690f, 1f));
                m.SetFloat("_Bands", 4f);
            }
            if (fixTex)
            {
                // 置空 _BaseMap ⇒ 退化成纯 baseColorFactor（深红棕），
                // 避免那张青色 clearcoat 贴图被 _InkDensity 放进来
                m.SetTexture("_BaseMap", null);
            }
        }
    }
}

// ---- 顶层 ----
{
    var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                                     "Tools/screenshots/d3dragon");
    System.IO.Directory.CreateDirectory(dir);
    var go = new GameObject("D3_Probe");
    var p = go.AddComponent<D3Probe>();
    p.outDir = dir;
    Debug.Log("[d3] probe installed → " + dir);
}
