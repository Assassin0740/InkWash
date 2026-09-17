using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_enemy_probe2.cs —— 定位 MoShan / MoGuai 「实渲 0 像素」的根因
//
// 已排除：取景失败（宽 40 m 也空）、材质 null、被禁用、视锥剔除字段（与能渲染的 MoGu 同值）。
//
// 本轮的两个新增判别手段：
//  ★★ A. 双背景色对照 —— 上一轮的哨兵背景色是**品红**，而 Unity 缺材质时渲染的也正是**品红**。
//        同一次拍摄用两种背景各拍一张：
//          品红 0% + 亮绿 0%   ⇒ 真没画
//          品红 0% + 亮绿 >0%  ⇒ 渲染成了品红（missing material / 默认材质）⇒ 上一轮是假阴性
//  ★★ B. 顶点真值 vs 蒙皮后包围盒 —— 蒙皮若把几何搬走/缩没，s.bounds 与未蒙皮顶点会分裂。
//
// 与 dg_enemy_diag 的关键差别：**不禁用任何 MonoBehaviour**（只关 NavMeshAgent），
// 让 InkMaterialSwap 之类的运行期贴材质组件照常工作。
public class dg_enemy_probe2 : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _idx, _phase, _wait;

    static readonly string[] Names = { "MoShan", "MoGuai", "MoGu", "MoLong", "MoYan" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoYan.prefab"
    };
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_probe2.txt";
    const int N = 640;

    static readonly Color32 BgMagenta = new Color32(255, 0, 255, 255);
    static readonly Color32 BgGreen = new Color32(0, 255, 0, 255);

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_p2_Viz");
        NewLight("Sun", new Vector3(52f, 28f, 0f), 1.35f);
        NewLight("Fill", new Vector3(-35f, -150f, 0f), 0.55f);
        NewLight("Rim", new Vector3(8f, 195f, 0f), 0.7f);

        var cg = new GameObject("Cam"); cg.transform.SetParent(_viz.transform, false);
        _cam = cg.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 3000f;
        _cam.cullingMask = ~0;
        _cam.enabled = true;   // ★ 必须：禁用的 Camera 上 Render() 只清屏不画模型

        _sb.AppendLine("===== dg_enemy_probe2 =====");
        _sb.AppendLine("口径：不禁用任何 MonoBehaviour（只关 NavMeshAgent）；双背景色对照；顶点真值 vs 蒙皮包围盒。");
        _sb.AppendLine();
    }

    void NewLight(string n, Vector3 euler, float i)
    {
        var g = new GameObject(n); g.transform.SetParent(_viz.transform, false);
        var l = g.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = i;
        g.transform.rotation = Quaternion.Euler(euler);
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
            if (pf == null)
            {
                _sb.AppendLine("### " + Names[_idx] + "  ★ prefab 加载失败");
                _idx++; return;
            }
            var inst = Instantiate(pf, new Vector3(0f, 300f + _idx * 40f, 0f), Quaternion.identity);
            inst.name = "dg_p2_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        Dump(_spawned[0], Names[_idx]);
        _phase = 0; _idx++; _wait = 1;
    }

    void Dump(GameObject go, string tag)
    {
        _sb.AppendLine("### " + tag + "  activeInHierarchy=" + go.activeInHierarchy);
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _sb.AppendLine("    SMR=" + smrs.Length);

        // 取景包围盒：离群过滤（只并入 max 边长 0.01~20 m 的）
        Bounds shot = new Bounds(go.transform.position, Vector3.zero);
        bool first = true; int outlier = 0;
        foreach (var s in smrs)
        {
            float mx = Mathf.Max(s.bounds.size.x, Mathf.Max(s.bounds.size.y, s.bounds.size.z));
            if (mx < 0.01f || mx > 20f) { outlier++; continue; }
            if (first) { shot = s.bounds; first = false; } else shot.Encapsulate(s.bounds);
        }

        int k = 0;
        foreach (var s in smrs)
        {
            k++;
            if (k > 6) { _sb.AppendLine("    …（其余 " + (smrs.Length - 6) + " 个略）"); break; }

            var m = s.sharedMesh;
            string sh = (s.sharedMaterial != null && s.sharedMaterial.shader != null) ? s.sharedMaterial.shader.name : "-";
            _sb.AppendLine("    [" + k + "] " + s.transform.name
                          + "  enabled=" + s.enabled
                          + "  layer=" + s.gameObject.layer
                          + "  updateWhenOffscreen=" + s.updateWhenOffscreen);
            _sb.AppendLine("         mesh=" + (m == null ? "★null" : m.name + "(" + m.vertexCount + "v)")
                          + "  bindposes=" + (m == null || m.bindposes == null ? 0 : m.bindposes.Length));
            int bonesNull = 0;
            if (s.bones != null)
                foreach (var b in s.bones) if (b == null) bonesNull++;
            _sb.AppendLine("         bones.len=" + (s.bones == null ? 0 : s.bones.Length)
                          + "  其中 null=" + bonesNull
                          + "  rootBone=" + (s.rootBone == null ? "★null" : s.rootBone.name)
                          + "  非null=" + ((s.bones == null ? 0 : s.bones.Length) - bonesNull));
            _sb.AppendLine("         sharedMat=" + (s.sharedMaterial == null ? "★null" : s.sharedMaterial.name)
                          + "  shader=" + sh
                          + "  localBounds=" + s.localBounds.size.ToString("F3"));
            _sb.AppendLine("         s.bounds(蒙皮后) center=" + s.bounds.center.ToString("F3")
                          + " size=" + s.bounds.size.ToString("F3"));

            if (m != null)
            {
                // ★ 顶点真值：未蒙皮的局部 min/max，再经 localToWorld 变换
                var vs = m.vertices;
                Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                int nan = 0;
                foreach (var v in vs)
                {
                    if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                     || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)) { nan++; continue; }
                    mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
                }
                _sb.AppendLine("         顶点(局部) min=" + mn.ToString("F3") + " max=" + mx.ToString("F3")
                              + "  NaN/Inf=" + nan);
                var l2w = s.transform.localToWorldMatrix;
                Vector3 wc = l2w.MultiplyPoint3x4((mn + mx) * 0.5f);
                Vector3 ws = Vector3.Scale(mx - mn, s.transform.lossyScale);
                _sb.AppendLine("         顶点(未蒙皮世界) center=" + wc.ToString("F3") + " size=" + ws.ToString("F3")
                              + "   lossyScale=" + s.transform.lossyScale.ToString("F3"));
                _sb.AppendLine("         s.bounds.center 与未蒙皮世界 center 的距离 = "
                              + Vector3.Distance(s.bounds.center, wc).ToString("F3"));
            }
        }

        _sb.AppendLine("    用于取景的包围盒 center=" + shot.center.ToString("F3")
                      + " size=" + shot.size.ToString("F3") + "  剔除离群=" + outlier);

        // ★★ 双背景色对照
        DisableOthers(go);
        var res = new float[4];
        res[0] = OneShot(shot, BgMagenta, tag + "_magenta_top", true);
        res[1] = OneShot(shot, BgMagenta, tag + "_magenta_side", false);
        res[2] = OneShot(shot, BgGreen, tag + "_green_top", true);
        res[3] = OneShot(shot, BgGreen, tag + "_green_side", false);
        RestoreOthers();

        _sb.AppendLine("    像素  品红底 top=" + res[0].ToString("F2") + "%  side=" + res[1].ToString("F2") + "%"
                     + "   ||   绿底 top=" + res[2].ToString("F2") + "%  side=" + res[3].ToString("F2") + "%");
        if (res[0] < 0.02f && res[1] < 0.02f && (res[2] > 0.02f || res[3] > 0.02f))
            _sb.AppendLine("    ★★ 判定：品红底 0%、绿底有 → **渲染成了品红 = missing material 假阴性**！");
        else if (res[0] < 0.02f && res[1] < 0.02f && res[2] < 0.02f && res[3] < 0.02f)
            _sb.AppendLine("    ★★ 判定：两种背景都 0 → 真没画出来。");
        _sb.AppendLine();
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

    void RestoreOthers()
    {
        foreach (var r in _off) if (r != null) r.enabled = true;
        _off.Clear();
    }

    float OneShot(Bounds b, Color32 bg, string fileTag, bool top)
    {
        _cam.backgroundColor = bg;
        Vector3 c = b.center;
        float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (ext < 0.01f) ext = 1f;
        _cam.orthographicSize = ext * 0.60f;
        Vector3 d = top ? new Vector3(0.001f, 1f, 0.001f) : new Vector3(0.62f, 0.30f, -0.72f).normalized;
        _cam.transform.position = c + d * 150f;
        _cam.transform.rotation = Quaternion.LookRotation(-d, top ? Vector3.forward : Vector3.up);

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
            int nonBg = 0;
            foreach (var p in px)
                if (Mathf.Abs(p.r - bg.r) > 24 || Mathf.Abs(p.g - bg.g) > 24 || Mathf.Abs(p.b - bg.b) > 24) nonBg++;
            pct = 100f * nonBg / px.Length;
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/probe2_" + fileTag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    渲染异常(" + fileTag + "): " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        return pct;
    }

    void Finish()
    {
        _sb.AppendLine("相机 cullingMask=" + _cam.cullingMask + "  far=" + _cam.farClipPlane);
        int camN = 0;
        foreach (var cam in Camera.allCameras)
        { camN++; _sb.AppendLine("  场景相机 " + cam.name + " enabled=" + cam.enabled + " cullingMask=" + cam.cullingMask); }
        _sb.AppendLine("  场景相机数=" + camN);
        _sb.AppendLine();
        _sb.AppendLine("判据：绿底 >0 而品红底 =0 ⇒ 上一轮的 0.00% 是 missing material 造成的假阴性。");
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

var g = new GameObject("dg_enemy_probe2");
g.AddComponent<dg_enemy_probe2>();
return "DG_ENEMY_PROBE2_STARTED";
