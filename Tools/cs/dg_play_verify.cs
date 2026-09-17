using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_play_verify.cs —— Play 模式复核「龙到底能不能渲染出来」
//
// 编辑模式已查实的硬事实（见 Tools/reports/dg_prefab_forensic.txt / dg_mesh_hunt.txt）：
//   · Z_Dragon.prefab 的 SMR: m_Mesh -> chinese_dragon.fbx 的 localId -3325053396650211315
//   · 该 id 在全工程 284 个 Model / 569 个 Mesh 里**零命中** ⇒ 引用悬空
//   · 同文件的材质引用(-1725437989455171953)命中 ⇒ 只有这一个网格引用是断的
//   · 骨骼完好：bones=180、rootBone=drgon_dup2
//   ⇒ 预测：Play 里龙也渲染不出像素（不是"编辑模式假阴性"）
//
// 本探针就是去证伪/证实这条预测：把龙实例化出来，**等一帧让蒙皮更新**（dg_look v1 的坑），
//   用正交相机量「非背景像素占比」，并落 PNG 作为肉眼证据；顺带读运行时 _spine 真值。
// 只读，不改任何资产。
public class dg_play_verify : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    GameObject _moLong, _dragonOnly, _viz;
    Camera _cam;
    int _phase, _wait;
    Component _comp; Type _ty;

    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_play_verify.txt";
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/dragon";

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go == gameObject) continue;
            if (go.name.StartsWith("dg_")) Destroy(go);
        }

        _viz = new GameObject("dg_play_Viz");
        var lg = new GameObject("L"); lg.transform.SetParent(_viz.transform, false);
        var lt = lg.AddComponent<Light>();
        lt.type = LightType.Directional; lt.intensity = 1.3f; lt.color = Color.white;
        lg.transform.rotation = Quaternion.Euler(55f, 30f, 0f);
        var lg2 = new GameObject("L2"); lg2.transform.SetParent(_viz.transform, false);
        var lt2 = lg2.AddComponent<Light>();
        lt2.type = LightType.Directional; lt2.intensity = 0.5f;
        lg2.transform.rotation = Quaternion.Euler(-40f, -140f, 0f);

        var cg = new GameObject("C"); cg.transform.SetParent(_viz.transform, false);
        _cam = cg.AddComponent<Camera>();
        _cam.orthographic = true;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(1f, 0f, 1f);   // 纯品红 = 背景哨兵色
        _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 500f;
        _cam.enabled = false;
    }

    void Start()
    {
        _sb.AppendLine("===== dg_play_verify =====");
        _sb.AppendLine("isPlaying=" + Application.isPlaying
                      + "  scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        _sb.AppendLine("EnemyDragon 类型 = " + (_ty == null ? "★找不到" : _ty.FullName));

        // ---- 实例化 MoLong（Awake 会跑 ResolveSpine）----
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { _sb.AppendLine("★ MoLong prefab 加载失败"); Finish(); return; }

        _moLong = Instantiate(pf, new Vector3(0f, 100f, 0f), Quaternion.identity);
        _moLong.name = "dg_play_MoLong";
        _comp = _moLong.GetComponentInChildren(_ty, true);
        // 立刻停机：Awake 已跑完（ResolveSpine/基线都在 Awake 里），停掉可避免它巡游/下落漂移
        var mb = _comp as MonoBehaviour; if (mb != null) mb.enabled = false;
        var agent = _moLong.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true);
        if (agent != null) agent.enabled = false;

        _sb.AppendLine("MoLong 已实例化，EnemyDragon 已停机；等 2 帧让蒙皮更新…");
        _phase = 0; _wait = 2;
    }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }
        switch (_phase)
        {
            case 0:
                _sb.AppendLine();
                _sb.AppendLine("[1] MoLong（Z_Enemy_MoLong.prefab 实例）");
                Describe(_moLong, "molong");
                _sb.AppendLine();
                _sb.AppendLine("[2] 同一时刻单独实例化 Z_Dragon.prefab（视觉子树源）");
                var pf2 = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
                if (pf2 == null) _sb.AppendLine("    ★ Z_Dragon.prefab 加载失败");
                else
                {
                    _dragonOnly = Instantiate(pf2, new Vector3(0f, 60f, 0f), Quaternion.identity);
                    _dragonOnly.name = "dg_play_DragonOnly";
                    _phase = 1; _wait = 2;
                    _sb.AppendLine("    已实例化，等 2 帧…");
                    return;
                }
                _phase = 2;
                break;

            case 1:
                _sb.AppendLine();
                Describe(_dragonOnly, "dragononly");
                _phase = 2;
                break;

            case 2:
                // ---- 运行时 _spine 真值 ----
                _sb.AppendLine();
                _sb.AppendLine("[3] 运行时 ResolveSpine 真值（MoLong 的 EnemyDragon）");
                if (_comp != null)
                {
                    var pProp = _ty.GetProperty("SpineLinksFound", BindingFlags.Public | BindingFlags.Instance);
                    object v = pProp == null ? null : pProp.GetValue(_comp);
                    _sb.AppendLine("    SpineLinksFound = " + (v == null ? "?" : v.ToString()));
                    var fSpine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
                    var spine = fSpine == null ? null : fSpine.GetValue(_comp) as IList;
                    _sb.AppendLine("    _spine.Count    = " + (spine == null ? "?" : spine.Count.ToString()));
                    if (spine != null && spine.Count > 0)
                    {
                        var nm = new StringBuilder();
                        for (int i = 0; i < spine.Count; i++)
                        {
                            var t = spine[i] as Transform;
                            if (i < 6 || i >= spine.Count - 4)
                                nm.Append(i).Append(":").Append(t == null ? "<null>" : t.name).Append("  ");
                        }
                        _sb.AppendLine("    节点=" + nm);
                    }
                    var fMr = _ty.GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);
                    var mr = fMr == null ? null : fMr.GetValue(_comp) as Transform;
                    _sb.AppendLine("    _modelRoot      = " + (mr == null ? "<null>" : mr.name));
                    var fBr = _ty.GetField("_baseRel", BindingFlags.NonPublic | BindingFlags.Instance);
                    var br = fBr == null ? null : fBr.GetValue(_comp) as IList;
                    _sb.AppendLine("    _baseRel.Count  = " + (br == null ? "?" : br.Count.ToString()));
                }
                else _sb.AppendLine("    ★ 拿不到 EnemyDragon 组件");
                Finish();
                break;
        }
    }

    void Describe(GameObject go, string tag)
    {
        if (go == null) { _sb.AppendLine("    <null>"); return; }
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int withMesh = 0;
        foreach (var s in smrs) if (s.sharedMesh != null) withMesh++;
        _sb.AppendLine("    SMR=" + smrs.Length + "  有mesh=" + withMesh);
        foreach (var s in smrs)
        {
            string mats = "";
            if (s.sharedMaterials != null)
                for (int i = 0; i < s.sharedMaterials.Length; i++)
                    mats += (i > 0 ? "," : "") + (s.sharedMaterials[i] == null ? "null" : s.sharedMaterials[i].name);
            _sb.AppendLine("      " + PathOf(s.transform)
                          + "\n        mesh=" + (s.sharedMesh == null ? "★null" : s.sharedMesh.name + "(" + s.sharedMesh.vertexCount + "v)")
                          + "  mats=" + mats
                          + "  bones=" + (s.bones == null ? 0 : s.bones.Length)
                          + "  rootBone=" + (s.rootBone == null ? "null" : s.rootBone.name));
            Bounds b = s.bounds;
            _sb.AppendLine("        renderer.bounds size=" + b.size.ToString("F3") + " center=" + b.center.ToString("F3"));
        }
        var rs = go.GetComponentsInChildren<MeshRenderer>(true);
        _sb.AppendLine("    MeshRenderer=" + rs.Length);
        Measure(go, tag);
    }

    string PathOf(Transform t)
    {
        var p = new StringBuilder(t.name);
        Transform c = t.parent; int gd = 0;
        while (c != null && gd++ < 40) { p.Insert(0, c.name + "/"); c = c.parent; }
        return p.ToString();
    }

    // 只让目标物体可见地渲一张图，量「非背景像素占比」
    void Measure(GameObject go, string tag)
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var disabled = new System.Collections.Generic.List<Renderer>();
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root == _viz) continue;
            bool ours = (root == go);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (ours) continue;
                if (r.enabled) { r.enabled = false; disabled.Add(r); }
            }
        }

        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        Vector3 c = b.center;
        float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (ext < 0.001f) ext = 1f;
        _cam.orthographicSize = ext * 0.62f;
        _sb.AppendLine("    用于取景的包围盒 size=" + b.size.ToString("F3") + "（空则退化为 1）");

        string[] dirTag = { "top", "side" };
        Vector3[] dirs = { new Vector3(0f, 1f, -0.03f), new Vector3(1f, 0.03f, 0f) };
        const int N = 800;

        try { System.IO.Directory.CreateDirectory(ShotDir); } catch { }

        for (int k = 0; k < 2; k++)
        {
            Vector3 d = dirs[k].normalized;
            _cam.transform.position = c + d * 120f;
            _cam.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);

            var rt = new RenderTexture(N, N, 24);
            _cam.targetTexture = rt;
            _cam.enabled = true;      // ★ 必须打开：禁用的 Camera 上 Render() 不画模型
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
                {
                    // 背景哨兵 = 品红 (255,0,255)；容忍压缩/AA 抖动
                    if (Mathf.Abs(p.r - 255) > 24 || p.g > 24 || Mathf.Abs(p.b - 255) > 24) nonBg++;
                }
                float pct = 100f * nonBg / (float)px.Length;
                _sb.AppendLine("    [" + dirTag[k] + "] 非背景像素 " + nonBg + " / " + px.Length
                              + " = " + pct.ToString("F2") + "%"
                              + (nonBg == 0 ? "   ★★ 完全空渲染 ⇒ 网格确实没挂上" : ""));

                string f = ShotDir + "/play_" + tag + "_" + dirTag[k] + ".png";
                System.IO.File.WriteAllBytes(f, tex.EncodeToPNG());
                _sb.AppendLine("        图: " + f);
                Destroy(tex);
            }
            catch (Exception e) { _sb.AppendLine("    渲染异常: " + e.GetType().Name + " " + e.Message); }
            _cam.targetTexture = null;
            RenderTexture.active = null;
            Destroy(rt);
        }

        foreach (var r in disabled) if (r != null) r.enabled = true;
    }

    void Finish()
    {
        _sb.AppendLine();
        _sb.AppendLine("===== 判据 =====");
        _sb.AppendLine("  非背景像素 = 0  ⇒ 网格引用悬空已实锤，龙在 Play 里也渲染不出来");
        _sb.AppendLine("  非背景像素 > 0  ⇒ 编辑模式那个 null 是读取假阴性，运行时能解析");
        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText(ReportPath, s); } catch { }
        enabled = false;
    }

    void OnDestroy()
    {
        if (_viz != null) Destroy(_viz);
        if (_moLong != null) Destroy(_moLong);
        if (_dragonOnly != null) Destroy(_dragonOnly);
    }
}

var g = new GameObject("dg_play_verify");
g.AddComponent<dg_play_verify>();
return "DG_PLAY_VERIFY_STARTED";
