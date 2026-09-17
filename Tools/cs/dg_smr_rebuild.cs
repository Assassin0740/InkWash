using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// dg_smr_rebuild.cs —— 一刀切开「网格蒙皮数据」vs「SMR 组件/骨骼引用」
//
// 已知：同一个 mesh + 同一个材质，挂 MeshRenderer 能画出完整山怪；挂原 SMR 则 0 像素。
//       网格索引正常、材质正常、bindposes 正常、SMR 全部序列化字段与能渲染的 MoGu 相同。
//
// 三个测试对象（都放在 MoShan 实例旁，轮流单独拍摄）：
//   T1 全新 SMR + MoShan 的 mesh + MoShan 材质，bones 空          ⇒ 画不出 = 网格蒙皮数据(f. boneWeights)坏
//   T2 全新 SMR + MoGu  的 mesh + MoGu  材质，bones 空            ⇒ 能画 = 排除「新建 SMR 本身就不行」
//   T3 全新 SMR + MoShan 的 mesh + 26 个有效骨骼引用(指向同一 Transform)
//                                                                 ⇒ 能画 = 根因是「bones 全 null ⇒ 蒙皮产出 NaN/退化」
// ★ 只操作运行时对象与 mesh 副本，不碰任何资产。
public class dg_smr_rebuild : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _phase, _wait;
    int _testIdx = -1;
    GameObject[] _tests = new GameObject[3];
    string[] _testTags = { "T1_newSMR_MoShanMesh", "T2_newSMR_MoGuMesh", "T3_newSMR_validBones" };
    Bounds[] _testBounds = new Bounds[3];

    static readonly string MoShanPath = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab";
    static readonly string MoGuPath = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab";
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_smr_rebuild.txt";
    const int N = 768;
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_sr_Viz");
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
        _cam.nearClipPlane = 0.01f; _cam.farClipPlane = 5000f;
        _cam.cullingMask = ~0;
        _cam.enabled = true;

        _sb.AppendLine("===== dg_smr_rebuild =====");
        _sb.AppendLine("T1=新SMR+MoShan网格(无骨骼)  T2=新SMR+MoGu网格(无骨骼,对照)  T3=新SMR+MoShan网格+有效骨骼引用");
        _sb.AppendLine();
    }

    void Start() { _phase = 0; _wait = 2; }

    SkinnedMeshRenderer BiggestSMR(GameObject go)
    {
        SkinnedMeshRenderer b = null; float v = -1f;
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (s.sharedMesh == null) continue;
            float x = s.bounds.size.x * s.bounds.size.y * s.bounds.size.z;
            if (x > v) { v = x; b = s; }
        }
        return b;
    }

    GameObject MakeTest(string name, Vector3 pos)
    {
        var t = new GameObject(name);
        t.transform.position = pos;
        _spawned.Add(t);
        return t;
    }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }

        if (_phase == 0)
        {
            var pfS = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MoShanPath);
            var pfG = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(MoGuPath);
            if (pfS == null || pfG == null) { _sb.AppendLine("prefab 加载失败"); Finish(); return; }
            var instS = Instantiate(pfS, new Vector3(0f, 300f, 0f), Quaternion.identity);
            var instG = Instantiate(pfG, new Vector3(0f, 500f, 0f), Quaternion.identity);
            instS.name = "dg_sr_MoShan"; instG.name = "dg_sr_MoGu";
            _spawned.Add(instS); _spawned.Add(instG);
            foreach (var ag in instS.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            foreach (var ag in instG.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;

            var sS = BiggestSMR(instS);
            var sG = BiggestSMR(instG);
            _sb.AppendLine("MoShan 最大 SMR=" + (sS == null ? "<none>" : sS.name + " mesh=" + sS.sharedMesh.name)
                          + "   MoGu 最大 SMR=" + (sG == null ? "<none>" : sG.name + " mesh=" + sG.sharedMesh.name));

            // T1：新 SMR + MoShan mesh，无骨骼
            var t1 = MakeTest("dg_sr_T1", new Vector3(6f, 300f, 0f));
            var r1 = t1.AddComponent<SkinnedMeshRenderer>();
            r1.sharedMesh = sS.sharedMesh;
            r1.sharedMaterials = sS.sharedMaterials;
            _testBounds[0] = WorldBoundsOf(t1, sS.sharedMesh.bounds);
            _tests[0] = t1;

            // T2：新 SMR + MoGu mesh，无骨骼（带 MoGu 的缩放以归一化到 1 m 级）
            var t2 = MakeTest("dg_sr_T2", new Vector3(14f, 300f, 0f));
            t2.transform.localScale = new Vector3(0.017f, 0.017f, 0.017f);
            var r2 = t2.AddComponent<SkinnedMeshRenderer>();
            r2.sharedMesh = sG.sharedMesh;
            r2.sharedMaterials = sG.sharedMaterials;
            _testBounds[1] = WorldBoundsOf(t2, sG.sharedMesh.bounds);
            _tests[1] = t2;

            // T3：新 SMR + MoShan mesh + 26 个有效骨骼引用（全部指向 t3 自己）
            var t3 = MakeTest("dg_sr_T3", new Vector3(22f, 300f, 0f));
            var r3 = t3.AddComponent<SkinnedMeshRenderer>();
            r3.sharedMesh = sS.sharedMesh;
            r3.sharedMaterials = sS.sharedMaterials;
            int n = sS.sharedMesh.bindposes.Length;
            var bones = new Transform[n];
            for (int i = 0; i < n; i++) bones[i] = t3.transform;
            r3.bones = bones;
            r3.rootBone = t3.transform;
            _testBounds[2] = WorldBoundsOf(t3, sS.sharedMesh.bounds);
            _tests[2] = t3;

            _sb.AppendLine("T1 bounds=" + _testBounds[0].center.ToString("F2") + " / " + _testBounds[0].size.ToString("F2"));
            _sb.AppendLine("T2 bounds=" + _testBounds[1].center.ToString("F2") + " / " + _testBounds[1].size.ToString("F2"));
            _sb.AppendLine("T3 bounds=" + _testBounds[2].center.ToString("F2") + " / " + _testBounds[2].size.ToString("F2")
                          + "  （bones=" + n + " 个，全指向 t3 自身）");
            _sb.AppendLine();
            _phase = 1; _testIdx = 0; _wait = 2;
            return;
        }

        if (_testIdx >= 3) { Finish(); return; }

        // 只让当前测试对象可见
        HideAllTests();
        var rr = _tests[_testIdx].GetComponent<Renderer>();
        rr.enabled = true;
        DisableOthers(_tests[_testIdx]);
        _sb.AppendLine("### " + _testTags[_testIdx]);
        float ext = Mathf.Max(_testBounds[_testIdx].size.x, Mathf.Max(_testBounds[_testIdx].size.y, _testBounds[_testIdx].size.z));
        if (ext < 0.05f) ext = 2f;
        Shoot(_testBounds[_testIdx].center, ext * 1.5f, _testTags[_testIdx]);
        _sb.AppendLine();

        _testIdx++;
        _wait = 1;
    }

    void HideAllTests()
    {
        for (int i = 0; i < 3; i++)
        {
            if (_tests[i] == null) continue;
            var r = _tests[i].GetComponent<Renderer>();
            if (r != null) r.enabled = false;
        }
    }

    Bounds WorldBoundsOf(GameObject go, Bounds localMeshBounds)
    {
        Vector3 c = go.transform.TransformPoint(localMeshBounds.center);
        Vector3 s = Vector3.Scale(localMeshBounds.size, go.transform.lossyScale);
        return new Bounds(c, s);
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
            _sb.AppendLine("    [" + tag + "] 视野=" + (orthoSize * 2f).ToString("F2") + "m"
                          + "  非背景=" + (100f * nonBg / px.Length).ToString("F2") + "%"
                          + "  量化色数=" + cnt.Count);
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/sr_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + tag + "] 渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        RestoreOthers();
    }

    void Finish()
    {
        _sb.AppendLine("怎么读：");
        _sb.AppendLine("  T1=0 & T2>0 & T3>0 ⇒ 网格蒙皮数据有问题(boneWeights)，但补上有效骨骼引用即可渲染");
        _sb.AppendLine("  T1=0 & T2>0 & T3=0 ⇒ 网格蒙皮数据坏且补骨骼也救不了 ⇒ 只能改 MeshFilter+MeshRenderer");
        _sb.AppendLine("  T1>0 ⇒ 网格无罪，原 SMR 组件的隐藏状态有问题");
        _sb.AppendLine("  T2=0 ⇒ 实验本身失效（新建 SMR 不带骨骼就画不出），结论不可用");
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

var g = new GameObject("dg_smr_rebuild");
g.AddComponent<dg_smr_rebuild>();
return "DG_SMR_REBUILD_STARTED";
