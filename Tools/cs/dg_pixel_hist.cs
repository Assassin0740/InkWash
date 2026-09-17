using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_pixel_hist.cs —— 像素直方图：把"非背景像素"到底是什么颜色摆出来
//
// 起因：MoShan(14 SMR) 与 MoGuai(10 SMR) 两个完全不同的模型，
//   绿底非背景像素都是 27.14%，且 top/side 也相同 ⇒ 该数字与模型无关，怀疑是判据/色彩空间问题。
// 本探针不猜，直接统计每种颜色出现多少次。
public class dg_pixel_hist : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _idx, _phase, _wait;

    static readonly string[] Names = { "MoShan", "MoGu" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab"
    };
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_pixel_hist.txt";
    const int N = 640;

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_hist_Viz");
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
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 3000f;
        _cam.cullingMask = ~0;
        _cam.enabled = true;

        _sb.AppendLine("===== dg_pixel_hist =====");
        _sb.AppendLine("口径：同一模型，先绿底拍一张、再品红底拍一张，逐色统计直方图（量化 4 bit/通道）。");
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
            inst.name = "dg_hist_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[0];
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Bounds shot = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (var s in smrs)
        {
            float mx = Mathf.Max(s.bounds.size.x, Mathf.Max(s.bounds.size.y, s.bounds.size.z));
            if (mx < 0.01f || mx > 20f) continue;
            if (first) { shot = s.bounds; first = false; } else shot.Encapsulate(s.bounds);
        }
        _sb.AppendLine("### " + Names[_idx] + "  SMR=" + smrs.Length
                      + "  取景 center=" + shot.center.ToString("F3") + " size=" + shot.size.ToString("F3"));

        DisableOthers(go);
        Histogram(shot, new Color32(0, 255, 0, 255), Names[_idx] + "_green", 27.14f);
        Histogram(shot, new Color32(255, 0, 255, 255), Names[_idx] + "_magenta", 0f);
        RestoreOthers();
        _sb.AppendLine();

        _phase = 0; _idx++; _wait = 1;
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

    void Histogram(Bounds b, Color32 bg, string tag, float expectPct)
    {
        Vector3 c = b.center;
        float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (ext < 0.01f) ext = 1f;
        _cam.orthographicSize = ext * 0.60f;
        Vector3 d = new Vector3(0.001f, 1f, 0.001f);
        _cam.transform.position = c + d * 150f;
        _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.forward);
        _cam.backgroundColor = bg;

        var rt = new RenderTexture(N, N, 24);
        _cam.targetTexture = rt;
        try
        {
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, N, N), 0, 0);
            tex.Apply();
            Color32[] px = tex.GetPixels32();

            var cnt = new Dictionary<int, int>();
            var sample = new Dictionary<int, Color32>();
            int nonBg = 0;
            foreach (var p in px)
            {
                int key = ((p.r >> 4) << 8) | ((p.g >> 4) << 4) | (p.b >> 4);
                if (!cnt.ContainsKey(key)) { cnt[key] = 0; sample[key] = p; }
                cnt[key]++;
                if (Mathf.Abs(p.r - bg.r) > 24 || Mathf.Abs(p.g - bg.g) > 24 || Mathf.Abs(p.b - bg.b) > 24) nonBg++;
            }
            float pct = 100f * nonBg / px.Length;
            _sb.AppendLine("  [" + tag + "]  非背景=" + pct.ToString("F2") + "%   像素总数=" + px.Length);

            var keys = new List<int>(cnt.Keys);
            keys.Sort((a, b2) => cnt[b2].CompareTo(cnt[a]));
            int lim = Mathf.Min(8, keys.Count);
            for (int i = 0; i < lim; i++)
            {
                var s = sample[keys[i]];
                bool isBg = Mathf.Abs(s.r - bg.r) <= 24 && Mathf.Abs(s.g - bg.g) <= 24 && Mathf.Abs(s.b - bg.b) <= 24;
                _sb.AppendLine("       " + cnt[keys[i]].ToString().PadLeft(7) + " px  RGB("
                              + s.r + "," + s.g + "," + s.b + ")  "
                              + (isBg ? "判=背景" : "判=非背景") + "   "
                              + (100f * cnt[keys[i]] / px.Length).ToString("F2") + "%");
            }
            _sb.AppendLine("     不同量化色数=" + cnt.Count + "   （参考：上一轮该背景报到 " + expectPct.ToString("F2") + "%）");
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/hist_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("  渲染异常(" + tag + "): " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
    }

    void Finish()
    {
        _sb.AppendLine("看什么：如果「判=非背景」的 top 颜色是**淡色**（各通道与背景只差一点点），");
        _sb.AppendLine("那 27% 就是阈值噪声，不是模型；如果 top 颜色里出现**第三种颜色**，那才是模型画出来的东西。");
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

var g = new GameObject("dg_pixel_hist");
g.AddComponent<dg_pixel_hist>();
return "DG_PIXEL_HIST_STARTED";
