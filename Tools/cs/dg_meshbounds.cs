using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_meshbounds.cs —— 直捣黄龙：我的取景全都建立在 renderer.bounds 上，
// 而 renderer.bounds = SMR.localBounds × lossyScale（推的），本工程已知 localBounds
// 与 mesh.bounds 不一致。若 localBounds 撒谎 ⇒ 取景对着空气 ⇒ 全是 0.00%。
//
// 三档取景对照，一次定位：
//   A = 现行做法（renderer.bounds union）
//   B = mesh.bounds 世界化（Mesh.bounds 是顶点真值域，不含 transform）
//   C = 兜底：不依赖任何 bounds，固定 2000 m 视野，对准实例位置
public class dg_meshbounds : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _idx, _phase, _wait;

    static readonly string[] Names = { "MoGu", "MoShan", "MoGuai" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab"
    };
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_meshbounds.txt";
    const int N = 768;
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);   // ★ 纯蓝（禁洋红）

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_mb_Viz");
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
        _cam.nearClipPlane = 0.01f; _cam.farClipPlane = 100000f;   // 兜底档要把远裁剪面放到极大
        _cam.cullingMask = ~0;
        _cam.enabled = true;

        _sb.AppendLine("===== dg_meshbounds =====");
        _sb.AppendLine("A=renderer.bounds 取景（现行）  B=mesh.bounds 世界化取景  C=固定 2000m 视野（不依赖 bounds）");
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
            var inst = Instantiate(pf, new Vector3(0f, 300f + _idx * 60f, 0f), Quaternion.identity);
            inst.name = "dg_mb_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[0];
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _sb.AppendLine("### " + Names[_idx] + "   实例位置=" + go.transform.position.ToString("F1")
                      + "   SMR=" + smrs.Length);

        // —— 逐个 SMR 把两个包围盒摊出来 ——
        Bounds rUnion = new Bounds(go.transform.position, Vector3.zero);
        Bounds mUnion = new Bounds(go.transform.position, Vector3.zero);
        bool rFirst = true, mFirst = true;
        foreach (var s in smrs)
        {
            var m = s.sharedMesh;
            if (m == null) continue;
            Bounds mb = m.bounds;                                   // 顶点真值域（局部空间）
            Vector3 wc = s.transform.TransformPoint(mb.center);     // 世界化
            Vector3 ws = Vector3.Scale(mb.size, s.transform.lossyScale);
            Bounds wb = new Bounds(wc, ws);

            if (rFirst) { rUnion = s.bounds; rFirst = false; } else rUnion.Encapsulate(s.bounds);
            if (mFirst) { mUnion = wb; mFirst = false; } else mUnion.Encapsulate(wb);

            _sb.AppendLine("    " + s.name.PadRight(14)
                + "  meshB.size=" + mb.size.ToString("F3")
                + "  localB.size=" + s.localBounds.size.ToString("F3")
                + "  rendererB.size=" + s.bounds.size.ToString("F3")
                + "  lossy=" + s.transform.lossyScale.x.ToString("F4")
                + "  SMRpos=" + s.transform.position.ToString("F2"));
        }
        _sb.AppendLine("    → A档取景(renderer union) center=" + rUnion.center.ToString("F2") + " size=" + rUnion.size.ToString("F2"));
        _sb.AppendLine("    → B档取景(mesh.bounds 世界) center=" + mUnion.center.ToString("F2") + " size=" + mUnion.size.ToString("F2"));

        DisableOthers(go);
        float extA = Mathf.Max(rUnion.size.x, Mathf.Max(rUnion.size.y, rUnion.size.z));
        float extB = Mathf.Max(mUnion.size.x, Mathf.Max(mUnion.size.y, mUnion.size.z));
        if (extA < 0.05f) extA = 2f;
        if (extB < 0.05f) extB = 2f;
        Shoot(rUnion.center, extA * 1.5f, Names[_idx] + "_A_rendererB");
        Shoot(mUnion.center, extB * 1.5f, Names[_idx] + "_B_meshB");
        Shoot(go.transform.position + Vector3.up * 1.5f, 1000f, Names[_idx] + "_C_fixed2000m");   // 视野 2000 m
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

    void Shoot(Vector3 center, float orthoSize, string tag)
    {
        Vector3 d = new Vector3(0.001f, 1f, 0.001f);
        _cam.orthographicSize = orthoSize;
        _cam.transform.position = center + d * 150f;
        _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.forward);
        _cam.backgroundColor = Bg;

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
            int nonBg = 0;
            var cnt = new Dictionary<int, int>();
            foreach (var p in px)
            {
                int key = ((p.r >> 4) << 8) | ((p.g >> 4) << 4) | (p.b >> 4);
                if (!cnt.ContainsKey(key)) cnt[key] = 0;
                cnt[key]++;
                if (Mathf.Abs(p.r - Bg.r) > 24 || Mathf.Abs(p.g - Bg.g) > 24 || Mathf.Abs(p.b - Bg.b) > 24) nonBg++;
            }
            _sb.AppendLine("    [" + tag + "] 视野=" + (orthoSize * 2f).ToString("F1") + "m"
                          + "  非背景=" + (100f * nonBg / px.Length).ToString("F2") + "%"
                          + "  量化色数=" + cnt.Count);
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/mb_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + tag + "] 渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
    }

    void Finish()
    {
        _sb.AppendLine("怎么读：若 B 档/C 档出现模型而 A 档为 0 ⇒ SMR.localBounds 撒谎，取景一直对着空气；");
        _sb.AppendLine("        若三档全 0 ⇒ 几何确实没被画（与包围盒无关）。");
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

var g = new GameObject("dg_meshbounds");
g.AddComponent<dg_meshbounds>();
return "DG_MESHBOUNDS_STARTED";
