using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_enemy_mesh.cs —— 两件事一起做
//   ① 查网格本身：subMeshCount / GetIndexCount / vertexCount / bindposes（"有顶点没面"是重点嫌疑）
//   ② 用【大余量取景】重拍：上一版 orthographicSize = ext*0.60 只给 20% 余量，MoGu 被切了顶部
//      —— 取景太紧会露出模型的一部分，但不会导致"全空"，所以对 MoShan 的 0% 无影响；
//         但尺子不严谨就得修。这一版 A 档给 3 倍余量、B 档完全不依赖包围盒（固定 6 m 视野）。
public class dg_enemy_mesh : MonoBehaviour
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
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_mesh.txt";
    const int N = 768;

    // ★ 哨兵色纪律：纯蓝。绝不用洋红（那是 Unity 缺材质的渲染色）
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_mesh_Viz");
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

        _sb.AppendLine("===== dg_enemy_mesh =====");
        _sb.AppendLine("口径：蓝底哨兵（抖动 ≤21 < 阈值 24）。A 档 = 包围盒取景 ×1.5 余量（全高 3×最大边）；");
        _sb.AppendLine("      B 档 = 不依赖包围盒，固定 6 m 视野、相机对准 实例位置+1.5m。");
        _sb.AppendLine("重点：subMeshCount / GetIndexCount —— 若 idx=0 而 v>0，就是「有顶点没面」。");
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
            inst.name = "dg_mesh_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[0];
        DumpMesh(go);

        // A 档：包围盒取景（大余量）
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Bounds shot = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (var s in smrs)
        {
            float mx = Mathf.Max(s.bounds.size.x, Mathf.Max(s.bounds.size.y, s.bounds.size.z));
            if (mx < 0.01f || mx > 20f) continue;
            if (first) { shot = s.bounds; first = false; } else shot.Encapsulate(s.bounds);
        }
        float ext = Mathf.Max(shot.size.x, Mathf.Max(shot.size.y, shot.size.z));
        if (ext < 0.05f) ext = 2f;

        DisableOthers(go);
        Shoot(shot.center, ext * 1.5f, Names[_idx] + "_A_bbox");
        Shoot(go.transform.position + Vector3.up * 1.5f, 3.0f, Names[_idx] + "_B_fixed6m");
        RestoreOthers();
        _sb.AppendLine();

        _phase = 0; _idx++; _wait = 1;
    }

    void DumpMesh(GameObject go)
    {
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _sb.AppendLine("### " + Names[_idx] + "   SMR=" + smrs.Length);
        long totalIdx = 0; int nullMesh = 0;
        foreach (var s in smrs)
        {
            var m = s.sharedMesh;
            if (m == null) { nullMesh++; _sb.AppendLine("    <mesh=null>  " + s.name); continue; }
            int sub = -1, idx = -1;
            string err = "";
            try
            {
                sub = m.subMeshCount;
                idx = 0;
                for (int i = 0; i < sub; i++) idx += (int)m.GetIndexCount(i);
            }
            catch (Exception e) { err = " 异常:" + e.Message; }
            string mat = s.sharedMaterial == null ? "<null>" : s.sharedMaterial.name;
            _sb.AppendLine("    " + s.name.PadRight(24)
                + " mesh=" + m.name.PadRight(18)
                + " v=" + m.vertexCount
                + " sub=" + sub
                + " idx=" + idx
                + " tri=" + (idx < 0 ? -1 : idx / 3)
                + " bind=" + m.bindposes.Length
                + "  mat=" + mat
                + "  lossy=" + s.transform.lossyScale.x.ToString("F4")
                + "  rBounds=" + s.bounds.size.ToString("F3")
                + "  lBounds=" + s.localBounds.size.ToString("F3")
                + err);
            if (idx > 0) totalIdx += idx;
        }
        _sb.AppendLine("    mesh=null 的 SMR 数=" + nullMesh + "   全部三角形索引合计=" + totalIdx);
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
            _sb.AppendLine("    [" + tag + "]  视野=" + (orthoSize * 2f).ToString("F2") + "m"
                          + "  非背景=" + (100f * nonBg / px.Length).ToString("F2") + "%"
                          + "  量化色数=" + cnt.Count);
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/mesh_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + tag + "] 渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
    }

    void Finish()
    {
        _sb.AppendLine("怎么读：MoGu = 正常对照组（画面里应看到完整模型）；");
        _sb.AppendLine("       MoShan/MoGuai 若 idx>0 却仍 0 像素 ⇒ 几何存在但没被画 ⇒ 排除「网格缺面」；");
        _sb.AppendLine("       若 idx=0 ⇒ 「有顶点没面」，导出环节把索引丢了。");
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

var g = new GameObject("dg_enemy_mesh");
g.AddComponent<dg_enemy_mesh>();
return "DG_ENEMY_MESH_STARTED";
