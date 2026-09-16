// d4_dragon_isolate.cs —— 隔离测试：到底哪个 uniform 让龙身出现"贴图原色"
//
// d3 的 A/B/C/D 四变体几乎看不出差别 ⇒ 说明我改的那几个值不是主因。
// 现在用**极端值**做单变量隔离（一次只把一个参数推到极端），看哪个能把
// 龙身从"墨色"变回"贴图原色"。这是最快的归因法。
//
// 候选：
//   _InkDensity   —— 贴图信息量（0=全墨阶，1=全贴图）
//   _ChromaKeep   —— 色相回填量
//   _Bands        —— 墨阶量化开关（<0.5 = 关闭）
//   _BandBias     —— 墨阶偏移
//   _BaseMap      —— 直接置空
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEngine;

public class D4Probe : MonoBehaviour
{
    public string outDir;
    StringBuilder _sb;
    Camera _cam; EnemyDragon _d; RenderTexture _rt; Texture2D _tex;
    int _step, _n;
    readonly List<string> _labels = new List<string>();
    readonly List<System.Action> _actions = new List<System.Action>();
    Material _m0, _m1;

    void Start()
    {
        _sb = new StringBuilder();
        _sb.AppendLine("========== d4 单变量极端隔离 ==========");
        _cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
        foreach (var mb in _cam.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn.Contains("ShowcaseCam") || tn.Contains("Brain") || tn.Contains("Follow")) DestroyImmediate(mb);
        }
        _cam.transform.position = new Vector3(9f, 4.0f, -4f);
        _cam.transform.LookAt(new Vector3(0f, 1.3f, 4f));
        _cam.fieldOfView = 45f;

        foreach (var x in FindObjectsOfType<EnemyDragon>())
            if (x.gameObject.name.StartsWith("D2_")) { _d = x; break; }
        if (_d == null) { var a = FindObjectsOfType<EnemyDragon>(); if (a.Length > 0) _d = a[0]; }
        _sb.AppendLine("龙 = " + (_d == null ? "<无>" : _d.gameObject.name));

        var mats = new List<Material>();
        if (_d != null)
            foreach (var r in _d.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var mm in r.sharedMaterials)
                    if (mm != null && !mats.Contains(mm)) mats.Add(mm);
        if (mats.Count > 0) _m0 = mats[0];
        if (mats.Count > 1) _m1 = mats[1];
        _sb.AppendLine("材质数 = " + mats.Count + (mats.Count > 0 ? "  m0 shader=" + _m0.shader.name : ""));

        // 记录原始值，便于还原
        _origDensity = _m0 != null && _m0.HasProperty("_InkDensity") ? _m0.GetFloat("_InkDensity") : 0.62f;
        _origChroma  = _m0 != null && _m0.HasProperty("_ChromaKeep") ? _m0.GetFloat("_ChromaKeep") : 0.45f;
        _origBaseMap = _m0 != null && _m0.HasProperty("_BaseMap") ? _m0.GetTexture("_BaseMap") : null;

        AddTest("00_基线", () => { });
        AddTest("01_InkDensity=1", () => { ForEach(m => { if (m.HasProperty("_InkDensity")) m.SetFloat("_InkDensity", 1f); }); });
        AddTest("02_ChromaKeep=1", () => { ForEach(m => { if (m.HasProperty("_ChromaKeep")) m.SetFloat("_ChromaKeep", 1f); }); });
        AddTest("03_Bands=0(关量化)", () => { ForEach(m => { if (m.HasProperty("_Bands")) m.SetFloat("_Bands", 0f); }); });
        AddTest("04_BaseMap=null", () => { ForEach(m => { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null); }); });
        AddTest("05_全还原", () => { Restore(); });

        _rt = new RenderTexture(800, 560, 24, RenderTextureFormat.ARGB32);
        _tex = new Texture2D(800, 560, TextureFormat.RGBA32, false);
        StartCoroutine(Co());
    }

    float _origDensity, _origChroma; Texture _origBaseMap;
    void Restore()
    {
        ForEach(m => {
            if (m.HasProperty("_InkDensity")) m.SetFloat("_InkDensity", _origDensity);
            if (m.HasProperty("_ChromaKeep")) m.SetFloat("_ChromaKeep", _origChroma);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", _origBaseMap);
            if (m.HasProperty("_Bands")) m.SetFloat("_Bands", 4f);
        });
    }

    void ForEach(System.Action<Material> act)
    {
        if (_m0 != null) act(_m0);
        if (_m1 != null) act(_m1);
    }

    void AddTest(string label, System.Action act) { _labels.Add(label); _actions.Add(act); }

    IEnumerator Co()
    {
        yield return new WaitForSeconds(0.3f);
        while (_step < _labels.Count)
        {
            if (_n < 22) { _n++; yield return null; continue; }

            _cam.targetTexture = _rt; _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, 800, 560), 0, 0); _tex.Apply(false, false);
            RenderTexture.active = null; _cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(outDir, "D4_" + _labels[_step] + ".png"), _tex.EncodeToPNG());

            _sb.AppendLine("  " + _labels[_step] + "  → _InkDensity=" + F("_InkDensity")
                         + " _ChromaKeep=" + F("_ChromaKeep") + " _Bands=" + F("_Bands")
                         + " _BaseMap=" + (_m0 != null && _m0.HasProperty("_BaseMap")
                             ? (_m0.GetTexture("_BaseMap") == null ? "<null>" : _m0.GetTexture("_BaseMap").name) : "?"));

            _step++;
            if (_step < _labels.Count) { _actions[_step](); _n = 0; yield return new WaitForSeconds(0.15f); }
        }
        File.WriteAllText(Path.Combine(outDir, "d4_isolate.txt"), _sb.ToString());
        Debug.Log("[d4]\n" + _sb.ToString());
        Destroy(gameObject);
    }

    string F(string p) => (_m0 != null && _m0.HasProperty(p)) ? _m0.GetFloat(p).ToString("F2") : "?";
}

// ---- 顶层 ----
{
    var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath),
                                     "Tools/screenshots/d4dragon");
    System.IO.Directory.CreateDirectory(dir);
    var go = new GameObject("D4_Probe");
    var p = go.AddComponent<D4Probe>();
    p.outDir = dir;
    Debug.Log("[d4] probe installed → " + dir);
}
