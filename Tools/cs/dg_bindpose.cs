using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_bindpose.cs —— 定位「SMR 画不出来，但同一个 mesh 用 MeshRenderer 能画」
//
// 已知：bones 数组元素全 null（MoGu 80/MoShan 26/MoGuai 97 全 null）、rootBone=null。
//       蒙皮公式 vertex' = Σ w_i · boneMatrix_i · bindpose_i · vertex。
//       bones 全 null 时，bindpose_i 就成了唯一起作用的变换 —— 若它把顶点搬到别处，
//       几何就会画在视野之外，且【不报错】。
//
// ① 打印 bindposes 的平移/缩放范围，与 mesh.bounds / localBounds 对照
// ② 【实验 E】把每个 mesh 复制一份、bindposes 全设为单位矩阵，再赋给 SMR 重拍：
//      出现完整模型 ⇒ 根因就是 bindpose 变换，修复方案随之确定
// ★ 全程只改 mesh 的【副本】，不碰任何资产。
public class dg_bindpose : MonoBehaviour
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
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_bindpose.txt";
    const int N = 768;
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_bp_Viz");
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

        _sb.AppendLine("===== dg_bindpose =====");
        _sb.AppendLine("A 档=原样 SMR   E 档=bindposes 全单位矩阵（mesh 副本，不动资产）");
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
            inst.name = "dg_bp_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[0];
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        DisableOthers(go);
        _sb.AppendLine("### " + Names[_idx] + "   SMR=" + smrs.Length);

        // ① bindposes 统计（取最大的那个 SMR 详细看）
        SkinnedMeshRenderer big = null; float bv = -1f;
        foreach (var s in smrs) { float v = s.bounds.size.x * s.bounds.size.y * s.bounds.size.z; if (v > bv) { bv = v; big = s; } }
        if (big != null && big.sharedMesh != null)
        {
            var m = big.sharedMesh;
            var bps = m.bindposes;
            _sb.AppendLine("    [" + big.name + "] bindposes=" + bps.Length + "  mesh.bounds=" + m.bounds.size.ToString("F3")
                          + "  localBounds=" + big.localBounds.size.ToString("F3"));
            if (bps.Length > 0)
            {
                Vector3 tmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 tmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                float smin = float.MaxValue, smax = float.MinValue;
                foreach (var bp in bps)
                {
                    Vector3 t = new Vector3(bp.m03, bp.m13, bp.m23);
                    tmin = Vector3.Min(tmin, t); tmax = Vector3.Max(tmax, t);
                    float s = new Vector3(bp.m00, bp.m10, bp.m20).magnitude;
                    smin = Mathf.Min(smin, s); smax = Mathf.Max(smax, s);
                }
                _sb.AppendLine("    bindposes 平移范围: min=" + tmin.ToString("F3") + "  max=" + tmax.ToString("F3"));
                _sb.AppendLine("    bindposes 缩放范围: " + smin.ToString("F4") + " ~ " + smax.ToString("F4"));
                for (int i = 0; i < Mathf.Min(3, bps.Length); i++)
                    _sb.AppendLine("       bp[" + i + "] t=" + new Vector3(bps[i].m03, bps[i].m13, bps[i].m23).ToString("F4")
                                   + "  col0=" + new Vector3(bps[i].m00, bps[i].m10, bps[i].m20).ToString("F4"));
            }
        }

        Bounds rU = Union(smrs, go, true);
        float extA = Mathf.Max(rU.size.x, Mathf.Max(rU.size.y, rU.size.z)); if (extA < 0.05f) extA = 2f;
        Shoot(rU.center, extA * 1.5f, Names[_idx] + "_A_orig");

        // ② 实验 E：bindposes 全单位矩阵
        int done = 0;
        Bounds mU = new Bounds(go.transform.position, Vector3.zero); bool mf = true;
        foreach (var s in smrs)
        {
            if (s.sharedMesh == null) continue;
            if (!s.sharedMesh.isReadable && s.sharedMesh.bindposes.Length == 0) continue;
            var copy = UnityEngine.Object.Instantiate(s.sharedMesh);
            copy.name = s.sharedMesh.name + "_bpI";
            var id = new Matrix4x4[copy.bindposes.Length];
            for (int i = 0; i < id.Length; i++) id[i] = Matrix4x4.identity;
            copy.bindposes = id;
            s.sharedMesh = copy;
            done++;
            var mb = copy.bounds;
            var wc = s.transform.TransformPoint(mb.center);
            var ws = Vector3.Scale(mb.size, s.transform.lossyScale);
            var wb = new Bounds(wc, ws);
            if (mf) { mU = wb; mf = false; } else mU.Encapsulate(wb);
        }
        _sb.AppendLine("    [实验E] " + done + " 个 SMR 换成 bindposes=单位 的 mesh 副本");
        float extE = Mathf.Max(mU.size.x, Mathf.Max(mU.size.y, mU.size.z)); if (extE < 0.05f) extE = 2f;
        Shoot(mU.center, extE * 1.5f, Names[_idx] + "_E_identityBindpose");

        RestoreOthers();
        _sb.AppendLine();

        _phase = 0; _idx++; _wait = 1;
    }

    Bounds Union(SkinnedMeshRenderer[] smrs, GameObject go, bool filter)
    {
        Bounds b = new Bounds(go.transform.position, Vector3.zero); bool f = true;
        foreach (var s in smrs)
        {
            if (filter)
            {
                float mx = Mathf.Max(s.bounds.size.x, Mathf.Max(s.bounds.size.y, s.bounds.size.z));
                if (mx < 0.01f || mx > 20f) continue;
            }
            if (f) { b = s.bounds; f = false; } else b.Encapsulate(s.bounds);
        }
        return b;
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
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/bp_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + tag + "] 渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
    }

    void Finish()
    {
        _sb.AppendLine("怎么读：E 档出现完整模型而 A 档为 0 ⇒ 根因是 bindpose 变换把顶点搬走；");
        _sb.AppendLine("        修复方向：修正 bindposes，或改用 MeshFilter+MeshRenderer（静态网格）。");
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

var g = new GameObject("dg_bindpose");
g.AddComponent<dg_bindpose>();
return "DG_BINDPOSE_STARTED";
