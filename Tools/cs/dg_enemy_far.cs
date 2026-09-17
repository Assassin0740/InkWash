using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_enemy_far.cs —— 几何到底画在哪个尺度上？
//
// 线索：s.bounds / localBounds 的比值恒为 ~890，而 lossyScale=0.001
//   ⇒ renderer.bounds 是**由 localBounds 推出来的**（updateWhenOffscreen=False 时 Unity 就这么干），
//     不是蒙皮真值。既然取景靠的是它，而 6 m / 40 m 固定取景都拍空，
//     那么几何有可能被画在**完全不同的尺度**上。
// 做法：同一个模型，用一组跨度极大的正交取景各拍一张，看形状在哪个尺度上出现。
public class dg_enemy_far : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _idx, _phase, _wait, _scaleIdx;

    static readonly string[] Names = { "MoShan", "MoGuai", "MoGu" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab"
    };
    static readonly float[] Sizes = { 5f, 50f, 500f, 5000f };

    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_far.txt";
    const int N = 480;

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_far_Viz");
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
        _cam.backgroundColor = new Color32(0, 255, 0, 255);
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 200000f;
        _cam.cullingMask = ~0;
        _cam.enabled = true;

        _sb.AppendLine("===== dg_enemy_far =====");
        _sb.AppendLine("绿底（墨色在绿底上判非背景可靠）。正交取景尺寸 5 / 50 / 500 / 5000 m，相机距离 = size*4。");
        _sb.AppendLine("判据：某档突然出现大量像素 ⇒ 几何就画在那个尺度上。");
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
            inst.name = "dg_far_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2; _scaleIdx = 0;
            return;
        }

        var go = _spawned[0];
        if (_scaleIdx == 0)
        {
            _sb.AppendLine("### " + Names[_idx]);
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var s in smrs)
            {
                _sb.AppendLine("    " + s.transform.name
                              + "  localBounds=" + s.localBounds.size.ToString("F3")
                              + "  s.bounds=" + s.bounds.size.ToString("F3")
                              + "  lossyScale=" + s.transform.lossyScale.ToString("F5")
                              + "  updateWhenOffscreen=" + s.updateWhenOffscreen);
                break;   // 只看第一个，够了
            }
            DisableOthers(go);
        }

        if (_scaleIdx >= Sizes.Length) { RestoreOthers(); _sb.AppendLine(); _phase = 0; _idx++; _wait = 1; return; }

        float sz = Sizes[_scaleIdx];
        float pct = Shoot(go, sz, Names[_idx] + "_far" + (int)sz);
        _sb.AppendLine("    正交尺寸 " + sz.ToString("F0").PadLeft(6) + " m（相机距离 " + (sz * 4f).ToString("F0")
                      + "）⇒ 非背景 " + pct.ToString("F2") + "%");
        _scaleIdx++; _wait = 1;
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

    float Shoot(GameObject go, float size, string tag)
    {
        Vector3 c = go.transform.position;
        _cam.orthographicSize = size;
        Vector3 d = new Vector3(0.62f, 0.30f, -0.72f).normalized;
        _cam.transform.position = c + d * (size * 4f);
        _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);

        var bg = new Color32(0, 255, 0, 255);
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
            int nonBg = 0; int dark = 0;
            foreach (var p in px)
            {
                if (Mathf.Abs(p.r - bg.r) > 24 || Mathf.Abs(p.g - bg.g) > 24 || Mathf.Abs(p.b - bg.b) > 24) nonBg++;
                // ★ 新判据：不透明的深色（墨色主体）—— luma < 100
                int luma = (p.r * 30 + p.g * 59 + p.b * 11) / 100;
                if (luma < 100) dark++;
            }
            pct = 100f * nonBg / px.Length;
            _sb.AppendLine("        深色像素(luma<100) = " + (100f * dark / px.Length).ToString("F2") + "%");
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/far_" + tag + ".png", tex.EncodeToPNG()); } catch { }
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

var g = new GameObject("dg_enemy_far");
g.AddComponent<dg_enemy_far>();
return "DG_ENEMY_FAR_STARTED";
