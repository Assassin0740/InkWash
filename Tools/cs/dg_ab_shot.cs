using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

// dg_ab_shot.cs —— 收尾两件事
//   ① 普查：Assets/_Project/Prefabs/Enemies 下每个 prefab 的 SMR 骨骼引用状况
//      （假设：能渲染的敌人有非 null 骨骼，不能渲染的没有）
//   ② 同一取景拍 A/B 对照：A=原 SMR（空）  B=同网格同材质换挂 MeshRenderer（有）
//      —— 给用户一张一眼能懂的证据图
public class dg_ab_shot : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _phase, _wait;

    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_ab_shot.txt";
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const int N = 768;
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_ab_Viz");
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

        _sb.AppendLine("===== dg_ab_shot =====");
        _sb.AppendLine();
    }

    void Start() { _phase = 0; _wait = 2; }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }
        if (_phase == 0) { Census(); _phase = 1; _wait = 1; return; }
        if (_phase == 1) { ABShot(); _phase = 2; _wait = 1; return; }
        Finish();
    }

    // ① 普查所有敌人 prefab 的骨骼引用
    void Census()
    {
        _sb.AppendLine("### ① 敌人 prefab 骨骼引用普查（编辑模式读资产）");
        _sb.AppendLine("    判据：SMR 的 bones 数组里【非 null 元素】的个数");
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" });
        var rows = new List<string>();
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string fn = Path.GetFileNameWithoutExtension(p);
            if (!(fn.Contains("Mo") || fn.Contains("Enemy") || fn.Contains("Dragon"))) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0) continue;
            int smrTotal = smrs.Length, smrWithBones = 0, totalBones = 0, totalNonNull = 0;
            foreach (var s in smrs)
            {
                var bs = s.bones;
                int nn = 0;
                if (bs != null) for (int i = 0; i < bs.Length; i++) if (bs[i] != null) nn++;
                totalBones += (bs == null ? 0 : bs.Length);
                totalNonNull += nn;
                if (nn > 0) smrWithBones++;
            }
            rows.Add("    " + fn.PadRight(24)
                     + "  SMR=" + smrTotal
                     + "  有骨骼的SMR=" + smrWithBones + "/" + smrTotal
                     + "  骨骼元素=" + totalBones
                     + "  非null=" + totalNonNull);
        }
        rows.Sort();
        foreach (var r in rows) _sb.AppendLine(r);
        _sb.AppendLine();
    }

    // ② 同取景 A/B 对照（MoShan）
    void ABShot()
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab");
        if (pf == null) { _sb.AppendLine("MoShan prefab 加载失败"); return; }
        var inst = Instantiate(pf, new Vector3(0f, 300f, 0f), Quaternion.identity);
        inst.name = "dg_ab_MoShan";
        _spawned.Add(inst);
        foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;

        var smrs = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer big = null; float bv = -1f;
        foreach (var s in smrs) { float v = s.bounds.size.x * s.bounds.size.y * s.bounds.size.z; if (v > bv) { bv = v; big = s; } }
        if (big == null) { _sb.AppendLine("没有 SMR"); return; }

        Vector3 center = big.bounds.center;
        float ext = Mathf.Max(big.bounds.size.x, Mathf.Max(big.bounds.size.y, big.bounds.size.z));
        if (ext < 0.05f) ext = 2f;
        float ortho = ext * 1.5f;

        // A：原样 SMR（把所有 SMR 都关掉其它、只留实例本身）
        DisableOthers(inst);
        Shoot(center, ortho, "AB_MoShan_A_原样SMR");
        RestoreOthers();

        // B：关掉 SMR，同一网格+材质挂 MeshRenderer 直画
        foreach (var s in smrs) s.enabled = false;
        var t = new GameObject("dg_ab_mr");
        t.transform.position = inst.transform.position;
        _spawned.Add(t);
        var mf = t.AddComponent<MeshFilter>();
        var mr = t.AddComponent<MeshRenderer>();
        mf.sharedMesh = big.sharedMesh;
        mr.sharedMaterials = big.sharedMaterials;

        DisableOthers(t);
        foreach (var s in smrs) if (s != null) s.enabled = false;   // DisableOthers 会恢复别处，这里保持 SMR 关闭
        Shoot(center, ortho, "AB_MoShan_B_同网格挂MeshRenderer");
        RestoreOthers();
        _sb.AppendLine("    取景 center=" + center.ToString("F2") + "  视野=" + (ortho * 2f).ToString("F2") + "m（A/B 完全相同）");
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
            _sb.AppendLine("    [" + tag + "] 非背景=" + (100f * nonBg / px.Length).ToString("F2") + "%  量化色数=" + cnt.Count);
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(ShotDir + "/" + tag + ".png", tex.EncodeToPNG());
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + tag + "] 异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
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

var g = new GameObject("dg_ab_shot");
g.AddComponent<dg_ab_shot>();
return "DG_AB_SHOT_STARTED";
