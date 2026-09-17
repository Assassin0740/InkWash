using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_enemy_wide.cs —— 最后一刀：MoShan / MoGuai 是「没画」还是「跑到别处」
//
// 已排除的假设（见 Tools/reports/dg_enemy_shots.txt 与 dg_enemy_diag.txt）：
//   · 取景问题（改用离群过滤 + mesh.bounds 两条口径，仍 0.00%）
//   · 材质缺失（不禁用 MonoBehaviour 后 材质非空 10/10、14/14）
//   · 被禁用（m_IsActive:1 / m_Enabled:1，运行时 enabled=True activeInHierarchy=True）
//   · 视锥剔除相关的 m_UpdateWhenOffscreen / m_LocalBounds（与能正常渲染的 MoGu 同值）
//
// 本探针：冻结时间后，用**固定大范围**取景（正交 40 m，对准物体根节点位置）各拍一张。
//   若大范围里能看到 ⇒ 只是位置/尺度与预期不符；若仍是全空 ⇒ 真的没往屏幕上画任何东西。
public class dg_enemy_wide : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _phase, _wait, _idx;

    static readonly string[] Names = { "MoShan", "MoGuai", "MoGu" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab"
    };
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_wide.txt";
    const int N = 600;

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_wide_Viz");
        L("Sun", new Vector3(52f, 28f, 0f), 1.35f);
        L("Fill", new Vector3(-35f, -150f, 0f), 0.55f);

        var cg = new GameObject("Cam"); cg.transform.SetParent(_viz.transform, false);
        _cam = cg.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0f, 0f, 1f);   // ★ 哨兵色：纯蓝。绝不用洋红（那是 Unity 缺材质的渲染色）
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 2000f;
        _cam.enabled = true;
    }

    void L(string n, Vector3 e, float i)
    {
        var g = new GameObject(n); g.transform.SetParent(_viz.transform, false);
        var l = g.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = i;
        g.transform.rotation = Quaternion.Euler(e);
    }

    void Start()
    {
        _sb.AppendLine("===== dg_enemy_wide =====");
        Time.timeScale = 0f;   // 冻结：排除"拍了之后跑了"
        _phase = 0; _wait = 1;
    }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }
        if (_idx >= Paths.Length)
        {
            _sb.AppendLine();
            _sb.AppendLine("判据：宽取景仍全空 ⇒ 该 prebab 往屏幕没画任何东西（不是取景/位置问题）。");
            string s = _sb.ToString();
            Debug.Log(s);
            try { File.WriteAllText(ReportPath, s); } catch { }
            Time.timeScale = 1f;
            foreach (var g in _spawned) if (g != null) Destroy(g);
            enabled = false;
            return;
        }

        if (_phase == 0)
        {
            var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Paths[_idx]);
            if (pf == null) { _sb.AppendLine(Names[_idx] + " ★加载失败"); _idx++; return; }
            var inst = Instantiate(pf, new Vector3(0f, 500f + _idx * 80f, 0f), Quaternion.identity);
            inst.name = "dg_wide_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 3;
            return;
        }

        var go = _spawned[_spawned.Count - 1];

        // 冻结后把物体原地按住（防落体）
        Vector3 p = go.transform.position;
        _sb.AppendLine();
        _sb.AppendLine("### " + Names[_idx] + "  root world pos=" + p.ToString("F2"));

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var off = new List<Renderer>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == _viz || root == go) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; off.Add(r); }
        }

        // 逐 renderer 报「世界位置 + 尺寸」，找出它们到底在哪
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 d = r.bounds.center - p;
            _sb.AppendLine("    " + r.transform.name
                           + "  相对根: " + d.ToString("F2")
                           + "  size=" + r.bounds.size.ToString("F3")
                           + "  enabled=" + r.enabled);
        }

        foreach (float size in new float[] { 6f, 40f })
        {
            _cam.orthographicSize = size;
            Vector3 dir = new Vector3(0.62f, 0.30f, -0.72f).normalized;
            _cam.transform.position = p + dir * (size * 3f);
            _cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
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
                foreach (var q in px)
                    if (Mathf.Abs(q.r - 255) > 24 || q.g > 24 || Mathf.Abs(q.b - 255) > 24) nonBg++;
                float pct = 100f * nonBg / px.Length;
                _sb.AppendLine("    正交 " + size + "m 取景 -> 非背景像素 " + nonBg + " = " + pct.ToString("F2") + "%");
                File.WriteAllBytes(ShotDir + "/wide_" + Names[_idx] + "_" + (int)size + "m.png", tex.EncodeToPNG());
                Destroy(tex);
            }
            catch (Exception e) { _sb.AppendLine("    渲染异常: " + e.Message); }
            _cam.targetTexture = null; RenderTexture.active = null; Destroy(rt);
        }

        foreach (var r in off) if (r != null) r.enabled = true;
        _phase = 0; _idx++;
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        if (_viz != null) Destroy(_viz);
        foreach (var g in _spawned) if (g != null) Destroy(g);
    }
}

var g = new GameObject("dg_enemy_wide");
g.AddComponent<dg_enemy_wide>();
return "DG_ENEMY_WIDE_STARTED";
