using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_enemy_cull.cs —— 定论：MoShan/MoGuai 是不是被「视锥剔除」掉了？
//
// 依据：updateWhenOffscreen=False 时，Unity **用 localBounds 做视锥剔除**，不重算蒙皮包围盒。
//   若 localBounds 的 center/size 与实际几何不符 ⇒ 物体被判"不在视锥内" ⇒ 直接不画，
//   而 Console 干净、renderer.bounds 看起来还正常（它正是由 localBounds 推的）。
// 实验：同一取景，只改这一个开关，三档对比。差异只能来自它。
//
// ★ 背景一律用品红 (255,0,255)：8-bit RT 抖动后是 (240,0,240)/(236,0,236)，与背景差 ≤19 < 阈值 24 ⇒ 干净。
//   绿底 (0,255,0) 抖动出 (44,240,44)，差 44 > 24 ⇒ 会产生 ~25% 的恒定假噪声，不可用。
public class dg_enemy_cull : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _idx, _phase, _wait, _cfg;

    static readonly string[] Names = { "MoShan", "MoGuai", "MoGu", "MoLong" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab"
    };

    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_cull.txt";
    const int N = 512;
    // ★ 哨兵色：纯蓝。绝不用洋红（Unity 缺材质的渲染色，看图者无法区分画布与真缺材质）
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_cull_Viz");
        for (int i = 0; i < 3; i++)
        {
            var g = new GameObject("L" + i); g.transform.SetParent(_viz.transform, false);
            var l = g.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = i == 0 ? 1.35f : 0.6f;
            g.transform.rotation = Quaternion.Euler(i == 0 ? new Vector3(52f, 28f, 0f) : new Vector3(-35f, -150f, 0f));
        }
        var cg = new GameObject("Cam"); cg.transform.SetParent(_viz.transform, false);
        _cam = cg.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = Bg;
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 5000f;
        _cam.cullingMask = ~0;
        _cam.enabled = true;

        _sb.AppendLine("===== dg_enemy_cull =====");
        _sb.AppendLine("品红底（抖动 ≤19 < 阈值 24 ⇒ 判据干净）。取景统一：以 root 为心、正交 8 m、斜 30°。");
        _sb.AppendLine("A=原样  B=updateWhenOffscreen=true  C=B+localBounds 设为超大（绝对在视锥内）");
        _sb.AppendLine();
    }

    void Start() { _phase = 0; _wait = 2; }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }
        if (_idx >= Names.Length) { Finish(); return; }

        if (_phase == 0)
        {
            foreach (var g in _spawned) if (g != null) Destroy(g);
            _spawned.Clear();
            var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Paths[_idx]);
            if (pf == null) { _sb.AppendLine("### " + Names[_idx] + " prefab 加载失败"); _idx++; return; }
            var inst = Instantiate(pf, new Vector3(0f, 300f + _idx * 40f, 0f), Quaternion.identity);
            inst.name = "dg_cull_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            DisableOthers(inst);
            _cfg = 0;
            _sb.AppendLine("### " + Names[_idx]);
            foreach (var s in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                _sb.AppendLine("    " + s.transform.name
                              + "  localBounds.center=" + s.localBounds.center.ToString("F3")
                              + "  size=" + s.localBounds.size.ToString("F3")
                              + "  updateWhenOffscreen=" + s.updateWhenOffscreen);
            }
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[0];
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (_cfg == 1)
            foreach (var s in smrs) s.updateWhenOffscreen = true;
        else if (_cfg == 2)
            foreach (var s in smrs) s.localBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

        string label = _cfg == 0 ? "A 原样" : (_cfg == 1 ? "B updateWhenOffscreen=true" : "C B + localBounds 超大");
        float pct = Shoot(go, Names[_idx] + "_cfg" + _cfg, label);
        _sb.AppendLine("    [" + label + "]  非背景 = " + pct.ToString("F2") + "%");

        _cfg++;
        if (_cfg > 2)
        {
            // 复位，避免影响下一个 prefab（其实是新实例，但保持一致）
            RestoreOthers();
            _sb.AppendLine();
            _phase = 0; _idx++; _wait = 1;
        }
        else _wait = 1;
    }

    readonly List<Renderer> _off = new List<Renderer>();
    void DisableOthers(GameObject target)
    {
        _off.Clear();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == _viz || root == target) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; _off.Add(r); }
        }
    }
    void RestoreOthers() { foreach (var r in _off) if (r != null) r.enabled = true; _off.Clear(); }

    float Shoot(GameObject go, string tag, string label)
    {
        Vector3 c = go.transform.position;
        _cam.orthographicSize = 4f;
        Vector3 d = new Vector3(0.55f, 0.42f, -0.72f).normalized;
        _cam.transform.position = c + d * 40f;
        _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);

        var rt = new RenderTexture(N, N, 24);
        _cam.targetTexture = rt;
        float pct = 0f;
        try
        {
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, N, N), 0, 0);
            tex.Apply();
            Color32[] px = tex.GetPixels32();
            int nonBg = 0, dark = 0;
            foreach (var p in px)
            {
                if (Mathf.Abs(p.r - Bg.r) > 24 || Mathf.Abs(p.g - Bg.g) > 24 || Mathf.Abs(p.b - Bg.b) > 24) nonBg++;
                int luma = (p.r * 30 + p.g * 59 + p.b * 11) / 100;
                if (luma < 100) dark++;
            }
            pct = 100f * nonBg / px.Length;
            _sb.AppendLine("        深色像素(luma<100) = " + (100f * dark / px.Length).ToString("F2") + "%");
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/cull_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        return pct;
    }

    void Finish()
    {
        _sb.AppendLine("判据：若 B 或 C 突然出现大量像素 ⇒ 根因 = localBounds 导致视锥剔除，修法就是关掉 updateWhenOffscreen。");
        _sb.AppendLine("      若三档全 0 ⇒ 不是剔除问题，需查网格/骨骼本身。");
        string s = _sb.ToString();
        Debug.Log(s);
        try { File.WriteAllText(ReportPath, s); } catch { }
        foreach (var g in _spawned) if (g != null) Destroy(g);
        enabled = false;
    }

    void OnDestroy()
    {
        if (_viz != null) Destroy(_viz);
        foreach (var g in _spawned) if (g != null) Destroy(g);
    }
}

var g = new GameObject("dg_enemy_cull");
g.AddComponent<dg_enemy_cull>();
return "DG_ENEMY_CULL_STARTED";
