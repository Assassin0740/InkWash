using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

// dg_smr_deep.cs —— 补上上两轮 dump 的盲区
// 前两轮用 NextVisible(enter=false) ⇒ 数组子元素【根本没被展开】：
//   m_Bones / m_Materials / m_BlendShapeWeights 的内容全是盲区（只看到 "(Generic)"）。
// 本轮：完整展开（Next(true)）后 diff，并补一个【清空 blendShapeWeights】实验。
public class dg_smr_deep : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    GameObject _viz; Camera _cam;
    int _phase, _wait;
    readonly Dictionary<string, string>[] _maps = new Dictionary<string, string>[3];
    static readonly string[] Names = { "MoGu", "MoShan", "MoGuai" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab"
    };
    const string ShotDir = "D:/Unity Project/InkWash/Tools/screenshots/enemies";
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_smr_deep.txt";
    const int N = 768;
    static readonly Color32 Bg = new Color32(0, 0, 255, 255);

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);

        _viz = new GameObject("dg_deep_Viz");
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

        _sb.AppendLine("===== dg_smr_deep =====");
        _sb.AppendLine("完整展开数组后 diff；并做【清空 blendShapeWeights】实验。");
        _sb.AppendLine();
    }

    void Start() { _phase = 0; _wait = 2; }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }

        if (_phase == 0)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Paths[i]);
                if (pf == null) { _maps[i] = new Dictionary<string, string>(); continue; }
                var inst = Instantiate(pf, new Vector3(0f, 300f + i * 80f, 0f), Quaternion.identity);
                inst.name = "dg_deep_" + Names[i];
                foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
                _spawned.Add(inst);
            }
            _phase = 1; _wait = 3;
            return;
        }

        if (_phase == 1)
        {
            for (int i = 0; i < Names.Length; i++) _maps[i] = Dump(Names[i], _spawned[i]);
            // diff（以 MoGu 为基准）
            var b = _maps[0];
            for (int i = 1; i < Names.Length; i++)
            {
                _sb.AppendLine("### MoGu vs " + Names[i] + "   差异：");
                int d = 0;
                foreach (var kv in b)
                {
                    string o;
                    if (!_maps[i].TryGetValue(kv.Key, out o)) { _sb.AppendLine("    [仅 MoGu] " + kv.Key + " = " + kv.Value); d++; continue; }
                    if (o != kv.Value) { _sb.AppendLine("    " + kv.Key + "\n        MoGu   = " + kv.Value + "\n        " + Names[i] + " = " + o); d++; }
                }
                foreach (var kv in _maps[i]) if (!b.ContainsKey(kv.Key)) { _sb.AppendLine("    [仅 " + Names[i] + "] " + kv.Key + " = " + kv.Value); d++; }
                _sb.AppendLine("    ---- 差异数=" + d);
                _sb.AppendLine();
            }
            _phase = 2; _wait = 2;
            return;
        }

        if (_phase == 2)
        {
            // 实验 G：清空每个 SMR 的 m_BlendShapeWeights
            for (int i = 0; i < Names.Length; i++)
            {
                var smrs = _spawned[i].GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var s in smrs)
                {
                    var so = new SerializedObject(s);
                    var p = so.FindProperty("m_BlendShapeWeights");
                    if (p != null)
                    {
                        _sb.AppendLine("[" + Names[i] + "/" + s.name + "] blendShapeWeights.size=" + p.arraySize
                                      + "  mesh.blendShapeCount=" + (s.sharedMesh == null ? -1 : s.sharedMesh.blendShapeCount));
                        if (p.arraySize > 0)
                        {
                            string vals = "";
                            for (int k = 0; k < Mathf.Min(5, p.arraySize); k++) vals += p.GetArrayElementAtIndex(k).floatValue.ToString("F4") + " ";
                            _sb.AppendLine("      前几个值: " + vals);
                            p.ClearArray();                       // ★ 实验 G
                            so.ApplyModifiedPropertiesWithoutUndo();
                            _sb.AppendLine("      → 已清空");
                        }
                    }
                }
            }
            _phase = 3; _wait = 2;
            return;
        }

        if (_phase == 3)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                var go = _spawned[i];
                DisableOthers(go);
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Bounds rU = new Bounds(go.transform.position, Vector3.zero); bool f = true;
                foreach (var s in smrs)
                {
                    float mx = Mathf.Max(s.bounds.size.x, Mathf.Max(s.bounds.size.y, s.bounds.size.z));
                    if (mx < 0.01f || mx > 20f) continue;
                    if (f) { rU = s.bounds; f = false; } else rU.Encapsulate(s.bounds);
                }
                float ext = Mathf.Max(rU.size.x, Mathf.Max(rU.size.y, rU.size.z)); if (ext < 0.05f) ext = 2f;
                Shoot(rU.center, ext * 1.5f, Names[i] + "_G_noBlendShape");
                RestoreOthers();
            }
            Finish();
        }
    }

    Dictionary<string, string> Dump(string name, GameObject go)
    {
        var map = new Dictionary<string, string>();
        SkinnedMeshRenderer big = null; float bv = -1f;
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            float v = s.bounds.size.x * s.bounds.size.y * s.bounds.size.z;
            if (v > bv) { bv = v; big = s; }
        }
        _sb.AppendLine("### " + name + "   最大 SMR=" + (big == null ? "<none>" : big.name));
        if (big == null) return map;

        var so = new SerializedObject(big);
        var it = so.GetIterator();
        while (it.NextVisible(true))
        {
            string path = it.propertyPath;
            // 跳过 bones 的元素（80~97 条，冗长；其 null 情况已由运行时 API 确认为全 null）
            if (path.StartsWith("m_Bones.Array.data")) continue;
            string val;
            try
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.ObjectReference:
                        val = it.objectReferenceValue == null ? "null" : (it.objectReferenceValue.name + " (id=" + it.objectReferenceInstanceIDValue + ")");
                        break;
                    case SerializedPropertyType.Integer: val = it.longValue.ToString(); break;
                    case SerializedPropertyType.Boolean: val = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Float: val = it.floatValue.ToString("F6"); break;
                    case SerializedPropertyType.ArraySize: val = "size=" + it.intValue; break;
                    case SerializedPropertyType.Bounds: val = it.boundsValue.center.ToString("F4") + " / " + it.boundsValue.size.ToString("F4"); break;
                    default: val = "(" + it.propertyType + ")"; break;
                }
            }
            catch (Exception e) { val = "<异常:" + e.Message + ">"; }
            map[path] = val;
            _sb.AppendLine("    " + path.PadRight(44) + " = " + val);
        }
        _sb.AppendLine();
        return map;
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
                          + "  非背景=" + (100f * nonBg / px.Length).ToString("F2") + "%  量化色数=" + cnt.Count);
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(ShotDir + "/deep_" + tag + ".png", tex.EncodeToPNG()); } catch { }
            Destroy(tex);
        }
        catch (Exception e) { _sb.AppendLine("    [" + tag + "] 渲染异常: " + e.Message); }
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

var g = new GameObject("dg_smr_deep");
g.AddComponent<dg_smr_deep>();
return "DG_SMR_DEEP_STARTED";
