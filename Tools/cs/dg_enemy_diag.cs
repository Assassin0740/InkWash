using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_enemy_diag.cs —— 分辨「MoShan/MoGuai 渲染 0.00%」是真不可见，还是我的取景失败
//
// 已知（prefab 文本层）：
//   Z_Enemy_MoShan 14 个 SMR：m_RootBone = 0，m_Bones 26 条**全是 0**
//   Z_Enemy_MoGu    3 个 SMR：m_RootBone = 0，m_Bones 80 条**全是 0**   ← 但它渲染出来了
//   ⇒ 空骨骼本身不必然导致不可见，所以不能只凭像素 0 下结论。
//
// 本探针的关键差别：**取景不用 Renderer.bounds**（它会随蒙皮/姿态漂），
//   改用 mesh 自带 bounds 经 renderer.localToWorldMatrix 变换 —— 与蒙皮无关。
//   两组像素数一起报，取景失败与真不可见就能分开。
public class dg_enemy_diag : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _phase, _wait, _idx;

    static readonly string[] Names = { "MoShan", "MoGuai", "MoGu", "MoLong" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab"
    };
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_diag.txt";
    const int N = 600;

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_diag_Viz");
        NewLight("Sun", new Vector3(52f, 28f, 0f), 1.35f);
        NewLight("Fill", new Vector3(-35f, -150f, 0f), 0.55f);
        NewLight("Rim", new Vector3(8f, 195f, 0f), 0.7f);

        var cg = new GameObject("Cam"); cg.transform.SetParent(_viz.transform, false);
        _cam = cg.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(1f, 0f, 1f);
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 800f;
        _cam.enabled = true;
    }

    void NewLight(string n, Vector3 euler, float i)
    {
        var g = new GameObject(n); g.transform.SetParent(_viz.transform, false);
        var l = g.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = i;
        g.transform.rotation = Quaternion.Euler(euler);
    }

    void Start() { _phase = 0; _wait = 1; }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }
        if (_idx >= Paths.Length)
        {
            _sb.AppendLine();
            _sb.AppendLine("判据：两列像素都 = 0 才是真不可见；只有 Renderer.bounds 那列为 0，说明取景依赖了坏包围盒。");
            string s = _sb.ToString();
            Debug.Log(s);
            try { File.WriteAllText(ReportPath, s); } catch { }
            foreach (var g in _spawned) if (g != null) Destroy(g);
            enabled = false;
            return;
        }

        if (_phase == 0)
        {
            var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Paths[_idx]);
            if (pf == null) { _sb.AppendLine(Names[_idx] + " ★prefab 加载失败"); _idx++; return; }
            var inst = Instantiate(pf, new Vector3(0f, 300f + _idx * 40f, 0f), Quaternion.identity);
            inst.name = "dg_diag_" + Names[_idx];
            foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[_spawned.Count - 1];
        _sb.AppendLine();
        _sb.AppendLine("### " + Names[_idx] + "   root.activeInHierarchy=" + go.activeInHierarchy);
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _sb.AppendLine("    SMR=" + smrs.Length);
        int i2 = 0;
        foreach (var s in smrs)
        {
            if (i2++ >= 3) { _sb.AppendLine("    …（其余 " + (smrs.Length - 3) + " 个略）"); break; }
            string mat = s.sharedMaterial == null ? "null" : s.sharedMaterial.name;
            string sh = (s.sharedMaterial == null || s.sharedMaterial.shader == null) ? "-" : s.sharedMaterial.shader.name;
            _sb.AppendLine("      " + s.transform.name
                          + "  enabled=" + s.enabled + " activeInHier=" + s.gameObject.activeInHierarchy
                          + "  layer=" + s.gameObject.layer);
            _sb.AppendLine("        mesh=" + (s.sharedMesh == null ? "★null" : s.sharedMesh.name + "(" + s.sharedMesh.vertexCount + "v)")
                          + "  mesh.bindposes=" + (s.sharedMesh == null || s.sharedMesh.bindposes == null ? 0 : s.sharedMesh.bindposes.Length)
                          + "  renderer.bones=" + (s.bones == null ? 0 : s.bones.Length)
                          + "  rootBone=" + (s.rootBone == null ? "null" : s.rootBone.name));
            _sb.AppendLine("        mat=" + mat + "  shader=" + sh
                          + "  renderer.bounds.size=" + s.bounds.size.ToString("F3")
                          + "  mesh.bounds.size=" + (s.sharedMesh == null ? Vector3.zero : s.sharedMesh.bounds.size).ToString("F3"));
        }

        float[] px = Shoot(go, Names[_idx]);
        _sb.AppendLine("    像素(按 Renderer.bounds 取景) = " + px[0].ToString("F2") + "%"
                       + "    像素(按 mesh.bounds 取景) = " + px[1].ToString("F2") + "%");
        _phase = 0; _idx++;
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

        // A：按 Renderer.bounds（旧法）
        Bounds a = new Bounds(go.transform.position, Vector3.zero);
        bool firstA = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        { if (firstA) { a = r.bounds; firstA = false; } else a.Encapsulate(r.bounds); }

        // B：按 mesh 自带 bounds（与蒙皮无关）
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool firstB = true;
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (s.sharedMesh == null) continue;
            Vector3 c = s.transform.TransformPoint(s.sharedMesh.bounds.center);
            Vector3 e = Vector3.Scale(s.sharedMesh.bounds.extents, s.transform.lossyScale);
            Bounds bb = new Bounds(c, e * 2f);
            if (firstB) { b = bb; firstB = false; } else b.Encapsulate(bb);
        }

        var res = new float[2];
        res[0] = OneCamera(a, tag + "_byRenderer");
        res[1] = OneCamera(b, tag + "_byMesh");
        foreach (var r in off) if (r != null) r.enabled = true;
        return res;
    }

    float OneCamera(Bounds b, string fileTag)
    {
        Vector3 c = b.center;
        float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (ext < 0.01f) ext = 1f;
        _cam.orthographicSize = ext * 0.60f;
        Vector3 d = new Vector3(0.62f, 0.30f, -0.72f).normalized;
        _cam.transform.position = c + d * 150f;
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
            int nonBg = 0;
            foreach (var p in px)
                if (Mathf.Abs(p.r - 255) > 24 || p.g > 24 || Mathf.Abs(p.b - 255) > 24) nonBg++;
            pct = 100f * nonBg / px.Length;
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/" + fileTag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        return pct;
    }

    void OnDestroy()
    {
        if (_viz != null) Destroy(_viz);
        foreach (var g in _spawned) if (g != null) Destroy(g);
    }
}

var g = new GameObject("dg_enemy_diag");
g.AddComponent<dg_enemy_diag>();
return "DG_ENEMY_DIAG_STARTED";
