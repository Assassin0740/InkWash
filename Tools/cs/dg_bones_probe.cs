using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_bones_probe.cs —— 三个盲点一起打
//   ① smr.bones 数组里【有没有 null 元素】（之前只印了长度！）
//      项目坑表有「跨 prefab 克隆让 m_Bones 全悬空」——若元素全 null，蒙皮矩阵非法 ⇒ 顶点被
//      变换到 NaN/无穷 ⇒ 完全不可见，且不报错。
//   ② 材质的 shaderKeywords（变体差异 ⇒ 可能命中未编译变体 ⇒ 不画）
//   ③ 【决定性实验】把同一个 sharedMesh + 同一个材质挂到普通 MeshRenderer 上直画：
//      画出来了 ⇒ 网格/材质没问题，问题出在 SMR 蒙皮路径；
//      还是画不出 ⇒ 网格数据本身有问题。
public class dg_bones_probe : MonoBehaviour
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
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_bones_probe.txt";
    const int N = 768;
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);   // ★ 纯蓝（禁洋红）

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_bones_Viz");
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
        _cam.nearClipPlane = 0.01f; _cam.farClipPlane = 3000f;
        _cam.cullingMask = ~0;
        _cam.enabled = true;

        _sb.AppendLine("===== dg_bones_probe =====");
        _sb.AppendLine("① bones 数组的 null 元素数  ② 材质 shaderKeywords  ③ MeshRenderer 直画同一 mesh");
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
            inst.name = "dg_bones_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[0];
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        DisableOthers(go);

        _sb.AppendLine("### " + Names[_idx] + "   SMR=" + smrs.Length);

        // ① bones 数组内情
        int allNull = 0, allOk = 0;
        foreach (var s in smrs)
        {
            var bs = s.bones;
            int nul = 0, nonNul = 0;
            string firstFew = "";
            if (bs != null)
            {
                for (int i = 0; i < bs.Length; i++)
                {
                    if (bs[i] == null) nul++;
                    else { nonNul++; if (nonNul <= 3) firstFew += bs[i].name + " "; }
                }
            }
            if (bs == null || nul == bs.Length) allNull++; else allOk++;
            _sb.AppendLine("    " + s.name.PadRight(14)
                + " bones.len=" + (bs == null ? -1 : bs.Length)
                + "  null元素=" + nul
                + "  非null=" + nonNul
                + "  rootBone=" + (s.rootBone == null ? "<null>" : s.rootBone.name)
                + (firstFew.Length > 0 ? ("  例: " + firstFew) : ""));
        }
        _sb.AppendLine("    → 全 null 的 SMR 数=" + allNull + "   有非 null 骨骼的 SMR 数=" + allOk);

        // ② 材质关键字
        var mats = new HashSet<Material>();
        foreach (var s in smrs) foreach (var m in s.sharedMaterials) if (m != null) mats.Add(m);
        foreach (var m in mats)
        {
            var kw = m.shaderKeywords;
            _sb.AppendLine("    材质 " + m.name + "  keywords=[" + string.Join(",", kw) + "]  数=" + kw.Length
                          + "  pass数=" + (m.shader == null ? -1 : m.shader.passCount));
        }

        // ③ MeshRenderer 直画实验：把最大的 mesh 挂到普通 MeshRenderer 上
        SkinnedMeshRenderer big = null; float bv = -1f;
        foreach (var s in smrs) { float v = s.bounds.size.x * s.bounds.size.y * s.bounds.size.z; if (v > bv) { bv = v; big = s; } }
        var test = new GameObject("dg_mr_test");
        test.transform.position = go.transform.position;
        test.transform.rotation = Quaternion.identity;
        _spawned.Add(test);
        if (big != null && big.sharedMesh != null)
        {
            var mf = test.AddComponent<MeshFilter>();
            var mr = test.AddComponent<MeshRenderer>();
            mf.sharedMesh = big.sharedMesh;
            mr.sharedMaterials = big.sharedMaterials;
            _sb.AppendLine("    [实验] MeshRenderer 直画 mesh=" + big.sharedMesh.name
                          + "  triangleCount=" + big.sharedMesh.triangles.Length / 3
                          + "  bounds=" + big.sharedMesh.bounds.size.ToString("F3"));
        }

        // 取景：A 档（原 SMR, renderer.bounds）×1.5  +  D 档（MeshRenderer 实验，按 mesh.bounds）
        Bounds rU = new Bounds(go.transform.position, Vector3.zero); bool f = true;
        foreach (var s in smrs)
        {
            float mx = Mathf.Max(s.bounds.size.x, Mathf.Max(s.bounds.size.y, s.bounds.size.z));
            if (mx < 0.01f || mx > 20f) continue;
            if (f) { rU = s.bounds; f = false; } else rU.Encapsulate(s.bounds);
        }
        float extA = Mathf.Max(rU.size.x, Mathf.Max(rU.size.y, rU.size.z)); if (extA < 0.05f) extA = 2f;
        Shoot(rU.center, extA * 1.5f, Names[_idx] + "_A_skinned");

        if (big != null && big.sharedMesh != null)
        {
            var mb = big.sharedMesh.bounds;
            Vector3 wc = test.transform.TransformPoint(mb.center);
            float extD = Mathf.Max(mb.size.x, Mathf.Max(mb.size.y, mb.size.z));
            if (extD < 0.05f) extD = 2f;
            Shoot(wc, extD * 1.5f, Names[_idx] + "_D_meshrender");
        }
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
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/bn_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + tag + "] 渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
    }

    void Finish()
    {
        _sb.AppendLine("怎么读：");
        _sb.AppendLine("  bones null元素 == bones.len（全 null）而 MoGu 也是全 null 却正常 ⇒ 不是充分原因");
        _sb.AppendLine("  D 档画出来而 A 档没有 ⇒ 网格+材质无罪，问题在 SMR 蒙皮路径");
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

var g = new GameObject("dg_bones_probe");
g.AddComponent<dg_bones_probe>();
return "DG_BONES_PROBE_STARTED";
