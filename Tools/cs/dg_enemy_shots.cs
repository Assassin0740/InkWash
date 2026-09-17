using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_enemy_shots.cs v2 —— 7 个敌人的可见性截图集（Play 模式）
//
// v1 的两个度量错误（本轮自己踩出来的，v2 已修）：
//   ① 一刀切禁用所有 MonoBehaviour ⇒ `InkMaterialSwap` 也没跑 ⇒
//      运行时要靠它贴的材质全是 null ⇒ 渲染 0 像素。**这是测量假阴性，不是敌人坏了。**
//   ② 直接 union 所有 Renderer.bounds 取景 ⇒ 个别 SMR 的 bounds 是原始模型尺度
//      （实测 MoGuai 的 mesh.bounds 是 1269×1964×566 m）⇒ 相机被拖到极远，画面全空。
//      ⇒ 改用**离群过滤**后的并集取景（只并入 max 边长落在 0.01~20 m 的包围盒）。
//
// 保留的不变量：相机必须 enabled=true（禁用的 Camera 上 Render() 只清屏不画模型）。
public class dg_enemy_shots : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _phase, _wait, _idx;

    static readonly string[] Prefabs =
    {
        "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab",
        "Assets/_Project/Prefabs/Enemies/Enemy_MoTu.prefab",
        "Assets/_Project/Prefabs/Enemies/Enemy_MoOu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab"
    };
    static readonly string[] Short = { "MoYan", "MoTu", "MoOu", "MoGu", "MoGuai", "MoShan", "MoLong" };

    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_shots.txt";
    const int N = 600;

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_shots_Viz");
        NewLight("Sun", new Vector3(52f, 28f, 0f), 1.35f);
        NewLight("Fill", new Vector3(-35f, -150f, 0f), 0.55f);
        NewLight("Rim", new Vector3(8f, 195f, 0f), 0.7f);

        var cg = new GameObject("Cam"); cg.transform.SetParent(_viz.transform, false);
        _cam = cg.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0f, 0f, 1f);   // ★ 哨兵色：纯蓝。绝不用洋红（那是 Unity 缺材质的渲染色）
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 800f;
        _cam.enabled = true;    // ★
    }

    void NewLight(string n, Vector3 euler, float i)
    {
        var g = new GameObject(n); g.transform.SetParent(_viz.transform, false);
        var l = g.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = i;
        g.transform.rotation = Quaternion.Euler(euler);
    }

    void Start()
    {
        _sb.AppendLine("===== dg_enemy_shots v2 =====");
        _sb.AppendLine("isPlaying=" + Application.isPlaying);
        _sb.AppendLine("口径：不禁用 MonoBehaviour（让 InkMaterialSwap 正常贴材质）；取景用离群过滤后的包围盒并集。");
        _sb.AppendLine();
        _sb.AppendLine("Prefab".PadRight(9) + "SMR/有mesh".PadRight(12) + "材质非空".PadRight(10)
                      + "视图1".PadRight(9) + "视图2".PadRight(9) + "取景范围(m)".PadRight(22) + "网格名(前3)");
        try { Directory.CreateDirectory(ShotDir); } catch { }
        _phase = 0; _wait = 1;
    }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }
        if (_idx >= Prefabs.Length) { Finish(); return; }

        if (_phase == 0)
        {
            var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[_idx]);
            if (pf == null) { _sb.AppendLine(Short[_idx].PadRight(9) + "★prefab 加载失败"); _idx++; return; }
            var inst = Instantiate(pf, new Vector3(0f, 400f + _idx * 60f, 0f), Quaternion.identity);
            inst.name = "dg_shot_" + Short[_idx];
            // 只关 NavMeshAgent（避免没有导航网格时自由落体）；★ 不动 MonoBehaviour
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 3;   // 等 Start 跑完（InkMaterialSwap）+ 蒙皮更新
            return;
        }

        var go = _spawned[_spawned.Count - 1];
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int withMesh = 0, withMat = 0;
        var names = new StringBuilder();
        foreach (var s in smrs)
        {
            if (s.sharedMesh != null)
            {
                withMesh++;
                if (names.Length < 46) names.Append(s.sharedMesh.name).Append(" ");
            }
            if (s.sharedMaterial != null) withMat++;
        }

        float[] pct = Shoot(go, Short[_idx]);
        string boundsTxt = _lastBoundsTxt;
        _sb.AppendLine(Short[_idx].PadRight(9)
                       + (smrs.Length + "/" + withMesh).PadRight(12)
                       + (withMat + "/" + smrs.Length).PadRight(10)
                       + ((pct[0].ToString("F2") + "%").PadRight(9))
                       + ((pct[1].ToString("F2") + "%").PadRight(9))
                       + boundsTxt.PadRight(22)
                       + names.ToString().Trim());
        _phase = 0; _idx++;
    }

    string _lastBoundsTxt = "-";

    Bounds RobustBounds(GameObject go)
    {
        Bounds sum = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        int skipped = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            Bounds b = r.bounds;
            float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (m < 0.01f || m > 20f) { skipped++; continue; }   // 离群：原始模型尺度或空
            if (!any) { sum = b; any = true; } else sum.Encapsulate(b);
        }
        if (!any)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (!any) { sum = r.bounds; any = true; } else sum.Encapsulate(r.bounds);
        }
        _lastBoundsTxt = "(" + sum.size.x.ToString("F2") + "," + sum.size.y.ToString("F2") + "," + sum.size.z.ToString("F2") + ")"
                         + (skipped > 0 ? " 剔除" + skipped : "");
        return sum;
    }

    float[] Shoot(GameObject go, string tag)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var off = new List<Renderer>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == _viz || root == go) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; off.Add(r); }
        }

        Bounds b = RobustBounds(go);
        Vector3 c = b.center;
        float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (ext < 0.05f) ext = 1f;

        Vector3[] dirs = { new Vector3(0.62f, 0.30f, -0.72f), new Vector3(1f, 0.05f, 0f) };
        string[] sufs = { "3q", "side" };
        var res = new float[2];

        for (int k = 0; k < 2; k++)
        {
            _cam.orthographicSize = ext * 0.60f;
            Vector3 d = dirs[k].normalized;
            _cam.transform.position = c + d * (ext * 4f + 20f);
            _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);

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
                foreach (var p in px)
                    if (Mathf.Abs(p.r - 255) > 24 || p.g > 24 || Mathf.Abs(p.b - 255) > 24) nonBg++;
                res[k] = 100f * nonBg / px.Length;
                File.WriteAllBytes(ShotDir + "/" + tag + "_" + sufs[k] + ".png", tex.EncodeToPNG());
                Destroy(tex);
            }
            catch (Exception e) { _sb.AppendLine("   渲染异常 " + tag + ": " + e.Message); }
            _cam.targetTexture = null;
            RenderTexture.active = null;
            Destroy(rt);
        }
        foreach (var r in off) if (r != null) r.enabled = true;
        return res;
    }

    void Finish()
    {
        _sb.AppendLine();
        _sb.AppendLine("判据：两视图都 ≈0 ⇒ 该敌人渲染不出来；只有一侧为 0 ⇒ 多半是取景问题，需复核。");
        _sb.AppendLine("图片: " + ShotDir);
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

var g = new GameObject("dg_enemy_shots");
g.AddComponent<dg_enemy_shots>();
return "DG_ENEMY_SHOTS_STARTED";
