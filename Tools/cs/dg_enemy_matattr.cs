using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// dg_enemy_matattr.cs —— 查材质【属性值】+ 交叉互换实验
//
// 背景：网格有索引、材质槽非 null、shader 名相同、取景余量充足、可见性正常，
//       但 MoShan / MoGuai 实渲 0 像素。
// 本探针做两件事：
//   ① 把材质的每一个 shader 属性值打出来（重点：_BaseMap 是否 null、alpha/cutoff、
//      renderQueue、_ZWrite、_Cull、_Bands、_InkDensity 等）—— 从没查过属性值。
//   ② 【决定性的交叉互换】：把 MoShan 的 SMR 换成 MoGu 的材质副本后重拍。
//      换了就出现 ⇒ 元凶是材质；换了还是 0 ⇒ 排除材质，问题在渲染层。
public class dg_enemy_matattr : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _idx, _phase, _wait;
    Material _ctrl;

    static readonly string[] Names = { "MoShan", "MoGuai", "MoGu" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab"
    };
    static readonly string[] MatNames = { "M_Ink_Enemy_MoShan", "M_Ink_Enemy_MoGuai", "M_Ink_Enemy_MoGu" };
    const string CtrlName = "M_Ink_Enemy_MoGu";     // 已知能正常渲染的对照材质
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_enemy_matattr.txt";
    const int N = 768;

    static readonly Color32 Bg = new Color32(0, 0, 255, 255);   // ★ 纯蓝（禁洋红）

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_mat_Viz");
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

        _ctrl = FindMat(CtrlName);

        _sb.AppendLine("===== dg_enemy_matattr =====");
        _sb.AppendLine("① 材质属性值全量 dump   ② 交叉互换：把被测模型换成 " + CtrlName + " 的副本后重拍");
        _sb.AppendLine("对照材质 " + CtrlName + " = " + (_ctrl == null ? "★ 没找到！" : "ok"));
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
            inst.name = "dg_mat_" + Names[_idx];
            foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
            _spawned.Add(inst);
            _phase = 1; _wait = 2;
            return;
        }

        var go = _spawned[0];
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var shot = UnionBounds(go, smrs);
        DisableOthers(go);

        if (_phase == 1)
        {
            DumpMaterial(MatNames[_idx], Names[_idx]);
            Shoot(shot, Names[_idx], "mat_" + Names[_idx] + "_orig");
            _phase = 2; _wait = 1;
            return;
        }

        // phase 2：交叉互换
        if (_ctrl != null)
        {
            int swapped = 0;
            foreach (var s in smrs) { s.sharedMaterial = new Material(_ctrl); swapped++; }
            _sb.AppendLine("    [互换] " + swapped + " 个 SMR 已换成 " + CtrlName + " 的副本");
            Shoot(shot, Names[_idx], "mat_" + Names[_idx] + "_swapped");
        }
        RestoreOthers();
        _sb.AppendLine();
        _phase = 0; _idx++; _wait = 1;
    }

    void DumpMaterial(string matName, string tag)
    {
        var mat = FindMat(matName);
        _sb.AppendLine("### " + tag + "   材质 " + matName);
        if (mat == null) { _sb.AppendLine("    ★ 材质资产没找到"); return; }
        var sh = mat.shader;
        _sb.AppendLine("    shader=" + (sh == null ? "<null>" : sh.name)
                      + "   renderQueue=" + mat.renderQueue
                      + "   RenderType=" + mat.GetTag("RenderType", false)
                      + "   enableInstancing=" + mat.enableInstancing);
        if (sh == null) return;
        int n = sh.GetPropertyCount();
        for (int i = 0; i < n; i++)
        {
            string pn = sh.GetPropertyName(i);
            var pt = sh.GetPropertyType(i);
            if (!mat.HasProperty(pn)) { _sb.AppendLine("      " + pn.PadRight(24) + " = <shader 有、材质无>"); continue; }
            string val;
            try
            {
                switch (pt)
                {
                    case ShaderPropertyType.Color: val = mat.GetColor(pn).ToString("F4"); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: val = mat.GetFloat(pn).ToString("F4"); break;
                    case ShaderPropertyType.Vector: val = mat.GetVector(pn).ToString("F4"); break;
                    case ShaderPropertyType.Texture:
                        {
                            var t = mat.GetTexture(pn);
                            if (t == null) val = "<null ★>";
                            else
                            {
                                var t2 = t as Texture2D;      // ★ GetTexture 返回 Texture 基类，format/alphaIsTransparency 只在 Texture2D 上
                                val = t.name + "  " + t.width + "x" + t.height + "  cls=" + t.GetType().Name
                                    + (t2 != null ? ("  fmt=" + t2.format + "  alphaIsTransparency=" + t2.alphaIsTransparency) : "");
                            }
                            break;
                        }
                    default: val = "(type " + pt + ")"; break;
                }
            }
            catch (Exception e) { val = "读取异常: " + e.Message; }
            _sb.AppendLine("      " + pn.PadRight(24) + " = " + val);
        }
    }

    Material FindMat(string name)
    {
        var gs = UnityEditor.AssetDatabase.FindAssets("t:Material " + name);
        foreach (var g in gs)
        {
            var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            if (Path.GetFileNameWithoutExtension(p) == name)
                return UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(p);
        }
        return null;
    }

    Bounds UnionBounds(GameObject go, SkinnedMeshRenderer[] smrs)
    {
        Bounds shot = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (var s in smrs)
        {
            float mx = Mathf.Max(s.bounds.size.x, Mathf.Max(s.bounds.size.y, s.bounds.size.z));
            if (mx < 0.01f || mx > 20f) continue;
            if (first) { shot = s.bounds; first = false; } else shot.Encapsulate(s.bounds);
        }
        return shot;
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

    void Shoot(Bounds b, string tag, string fileTag)
    {
        float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (ext < 0.05f) ext = 2f;
        Vector3 d = new Vector3(0.001f, 1f, 0.001f);
        _cam.orthographicSize = ext * 1.5f;            // ★ 大余量，别再切掉模型
        _cam.transform.position = b.center + d * 150f;
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
            _sb.AppendLine("    [" + fileTag + "] 视野=" + (ext * 3f).ToString("F2") + "m"
                          + "  非背景=" + (100f * nonBg / px.Length).ToString("F2") + "%"
                          + "  量化色数=" + cnt.Count);
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/" + fileTag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + fileTag + "] 渲染异常: " + e.Message); }
        _cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
    }

    void Finish()
    {
        _sb.AppendLine("怎么读：");
        _sb.AppendLine("  _swapped 出现模型而 _orig 没有 ⇒ 元凶是原材质（属性值/贴图），看 dump 里哪一项异常");
        _sb.AppendLine("  _swapped 仍为 0 ⇒ 材质无罪，问题在渲染层（骨骼/批处理/管线）");
        _sb.AppendLine("  重点看：_BaseMap 是否 <null ★>、Alpha/Cutoff 类属性、renderQueue 是否 3000(Transparent)");
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

var g = new GameObject("dg_enemy_matattr");
g.AddComponent<dg_enemy_matattr>();
return "DG_ENEMY_MATATTR_STARTED";
