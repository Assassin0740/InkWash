using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// d10：材质族修复的**确定性**实证。
// 思路：不等波次，自己在面板已就绪后 Instantiate 一条龙，
//       然后读它的 runtime 材质 —— 若族修复生效，它应保持自己的墨阶
//       （_BandBias −0.42 / _InkDensity 0.25 / _ChromaKeep 0.10 / 赭墨 _InkDark）。
//
// 与 d5 的失败现场逐项对照。同时拍一张图作为视觉证据。
public class d10_probe : MonoBehaviour
{
    static readonly StringBuilder sb = new StringBuilder();
    int _phase;
    float _t;
    GameObject _dragon;

    void Update()
    {
        _t += Time.unscaledDeltaTime;

        if (_phase == 0 && _t > 1.5f)
        {
            _phase = 1;
            Spawn();
            _t = 0f;
            return;
        }
        if (_phase == 1 && _t > 1.2f)
        {
            _phase = 2;
            Report();
            Shoot();
            _t = 0f;
            return;
        }
        // ★ 关键对照：把**主角那一族**的参数改掉，看龙会不会跟着变。
        //   修复前：会变（Broadcast 遍历 all entries）。修复后：不该变。
        if (_phase == 2 && _t > 0.5f)
        {
            _phase = 3;
            PerturbPlayerFamily();
            _t = 0f;
            return;
        }
        if (_phase == 3 && _t > 0.5f)
        {
            _phase = 4;
            sb.AppendLine("");
            sb.AppendLine("========== 对照：改主角族参数后，龙是否被波及 ==========");
            DumpRecursive(_dragon.transform, "  ");
            Debug.Log(sb.ToString());
        }
    }

    /// <summary>把主角那一族的 _BandBias 设成一个夸张值（−0.75），看龙是否跟着变。</summary>
    void PerturbPlayerFamily()
    {
        var panelT = FindType("InkWash.UI.InkStylePanel");
        if (panelT == null) { sb.AppendLine("✗ 找不到 panel 类型"); return; }
        var panels = UnityEngine.Object.FindObjectsOfType(panelT);
        if (panels.Length == 0) { sb.AppendLine("✗ 场上无 panel"); return; }

        var fcP = panelT.GetProperty("FamilyCount");
        int fc = (int)fcP.GetValue(panels[0]);
        sb.AppendLine("（扰动前）FamilyCount = " + fc);

        // 逐族试：找到"主角"那一族（源材质名含 Character）
        var idxM = panelT.GetMethod("IndexOfFamily");
        var editM = panelT.GetMethod("EditFamily");
        var efnP = panelT.GetProperty("EditingFamilyName");

        int target = -1;
        var famsField = panelT.GetField("Families", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (famsField != null)
        {
            var list = famsField.GetValue(null) as System.Collections.IEnumerable;
            int i = 0;
            foreach (var f in list)
            {
                var srcF = f.GetType().GetField("source");
                var src = srcF != null ? srcF.GetValue(f) as Material : null;
                sb.AppendLine("   族[" + i + "] source = " + (src != null ? src.name : "<null>"));
                if (src != null && src.name.IndexOf("Character", StringComparison.OrdinalIgnoreCase) >= 0) target = i;
                i++;
            }
        }
        if (target < 0) { sb.AppendLine("（没定位到主角族，跳过扰动）"); return; }

        editM.Invoke(panels[0], new object[] { target });
        sb.AppendLine("切到族[" + target + "] Editing=[" + efnP.GetValue(panels[0]) + "]");

        // 直接改 _edit（Family.edit）上的 _BandBias
        var editFamilyF = panelT.GetField("_edit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var edit = editFamilyF.GetValue(panels[0]) as Material;
        if (edit != null)
        {
            edit.SetFloat("_BandBias", -0.75f);
            sb.AppendLine("已把主角族 _edit._BandBias 设为 -0.75（夸张值）");
            // 触发 Broadcast
            var bc = panelT.GetMethod("Broadcast", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (bc != null) { bc.Invoke(panels[0], null); sb.AppendLine("Broadcast 已调用"); }
        }
    }

    void Spawn()
    {
        var prefab = FindPrefab();
        if (prefab == null) { sb.AppendLine("✗ 找不到 Z_Enemy_MoLong 预制体"); return; }

        // 玩家前方 7 m
        Vector3 pos = Vector3.zero;
        var pt = Type.GetType("InkWash.Combat.PlayerRef, Assembly-CSharp")
              ?? FindType("PlayerRef");
        if (pt != null)
        {
            var pm = pt.GetProperty("Position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (pm != null) pos = (Vector3)pm.GetValue(null);
        }
        pos += new Vector3(0f, 0f, 7f);

        _dragon = Instantiate(prefab, pos, Quaternion.identity);
        _dragon.name = "D10_MoLong";

        // 补水墨化（与 WaveSpawner.SpawnOne 一致）
        var forcerT = FindType("InkWash.Rendering.InkMaterialForcer");
        if (forcerT != null)
        {
            var f = _dragon.GetComponentInChildren(forcerT, true);
            var m = forcerT.GetMethod("ForceInk", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (f != null && m != null) { m.Invoke(f, null); sb.AppendLine("ForceInk 已调用"); }
            else sb.AppendLine("（没找到 InkMaterialForcer 实例或 ForceInk 方法）");
        }
        sb.AppendLine("已生成 D10_MoLong @ " + pos);
    }

    void Report()
    {
        sb.AppendLine("========== d10 材质族修复实证 ==========");

        var panelT = FindType("InkWash.UI.InkStylePanel");
        if (panelT != null)
        {
            var panels = UnityEngine.Object.FindObjectsOfType(panelT);
            sb.AppendLine("InkStylePanel 数量 = " + panels.Length);
            foreach (var p in panels)
            {
                var fc = panelT.GetProperty("FamilyCount");
                var efn = panelT.GetProperty("EditingFamilyName");
                var rc = panelT.GetProperty("RegisteredCount");
                sb.AppendLine("  " + ((Component)p).gameObject.name
                    + "  Registered=" + (rc != null ? rc.GetValue(p).ToString() : "?")
                    + "  Families=" + (fc != null ? fc.GetValue(p).ToString() : "?")
                    + "  Editing=[" + (efn != null ? efn.GetValue(p).ToString() : "?") + "]");
            }
        }

        if (_dragon == null) { sb.AppendLine("（没生成龙）"); return; }
        DumpRecursive(_dragon.transform, "  ");
    }

    void Shoot()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) { Debug.Log(sb.ToString()); return; }
            // 相机对准龙
            if (_dragon != null)
            {
                var look = _dragon.transform.position + Vector3.up * 1.5f;
                var p = look - Vector3.forward * 13f + Vector3.up * 4f;
                cam.transform.position = p;
                cam.transform.LookAt(look);
            }
            var rt = new RenderTexture(1176, 882, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var prevA = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevA;
            var bytes = tex.EncodeToPNG();
            string dir = System.IO.Path.Combine(Application.dataPath, "../Tools/screenshots/d10");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "D10_dragon_after.png");
            System.IO.File.WriteAllBytes(path, bytes);
            sb.AppendLine("截图已存 = " + path);
            UnityEngine.Object.Destroy(tex);
            rt.Release();
        }
        catch (Exception e) { sb.AppendLine("截图失败：" + e.Message); }
        Debug.Log(sb.ToString());
    }

    static GameObject FindPrefab()
    {
        string path = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (pf != null) return pf;
        foreach (var g in UnityEditor.AssetDatabase.FindAssets("Z_Enemy_MoLong t:Prefab"))
        {
            var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            var o = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (o != null) return o;
        }
        return null;
    }

    static Type FindType(string full)
    {
        var t = Type.GetType(full + ", Assembly-CSharp");
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType(full);
            if (t != null) return t;
        }
        return null;
    }

    static void DumpRecursive(Transform tr, string pad)
    {
        foreach (var rd in tr.GetComponents<Renderer>())
        {
            bool ink = false;
            foreach (var m in rd.sharedMaterials)
                if (m != null && m.shader != null && m.shader.name.StartsWith("InkWash/")) { ink = true; break; }
            if (!ink) continue;
            sb.AppendLine(pad + "──────── " + PathOf(tr) + "  size=" + rd.bounds.size.ToString("0.00") + " ────────");
            var mats = rd.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                sb.AppendLine(pad + "  mat[" + i + "] '" + m.name + "'");
                Flo(m, pad + "      ", "_BandBias");
                Flo(m, pad + "      ", "_InkDensity");
                Flo(m, pad + "      ", "_ChromaKeep");
                Flo(m, pad + "      ", "_Bands");
                Col(m, pad + "      ", "_InkDark");
                var bt = m.GetTexture("_BaseMap");
                sb.AppendLine(pad + "      _BaseMap = " + (bt == null ? "<null>" : bt.name));
            }
        }
        for (int i = 0; i < tr.childCount; i++) DumpRecursive(tr.GetChild(i), pad);
    }

    static void Flo(Material m, string pad, string n)
    { if (m.HasProperty(n)) sb.AppendLine(pad + n + " = " + m.GetFloat(n).ToString("0.###")); }
    static void Col(Material m, string pad, string n)
    {
        if (!m.HasProperty(n)) return;
        var c = m.GetColor(n);
        sb.AppendLine(pad + n + " = (" + c.r.ToString("0.###") + ", " + c.g.ToString("0.###") + ", " + c.b.ToString("0.###") + ")");
    }
    static string PathOf(Transform t)
    { string s = t.name; var p = t.parent; int g = 0; while (p != null && g++ < 6) { s = p.name + "/" + s; p = p.parent; } return s; }
}

var g10 = new GameObject("D10_Probe");
g10.AddComponent<d10_probe>();
return "D10_STARTED";
