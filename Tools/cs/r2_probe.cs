// r2_probe.cs —— 第九轮：地面 / 敌人 / 跑步片段 / 可用动画原料
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Animations;

public class R2Probe
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    static string MP(Material m, params string[] props)
    {
        if (m == null) return "(null)";
        var sb = new StringBuilder();
        for (int i = 0; i < props.Length; i++)
        {
            string p = props[i];
            if (!m.HasProperty(p)) { sb.Append(p + "=<无> "); continue; }
            // 先用声明类型判断，失败再换
            try
            {
                float f = m.GetFloat(p);
                sb.Append(p + "=" + f.ToString("F3") + " ");
                continue;
            }
            catch { }
            try
            {
                Color c = m.GetColor(p);
                sb.Append(p + "=(" + c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3") + ") ");
            }
            catch { sb.Append(p + "=<类型不明> "); }
        }
        return sb.ToString().Trim();
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        string root = Application.dataPath + "/..";
        L("=========== r2_probe ===========");

        // ---------- ① 场景里的地面 / 墙 / 台子 ----------
        L("");
        L("---------- ① 场景实体（带 Collider 或 Renderer 的） ----------");
        var all = UnityEngine.Object.FindObjectsOfType<Transform>();
        foreach (var t in all)
        {
            if (t.parent != null) continue;                 // 只看根物体
            var r = t.GetComponentInChildren<Renderer>();
            if (r == null) continue;
            L("  root: " + t.name + "  worldPos=" + t.position.ToString("F2")
              + "  lossyScale=" + t.lossyScale.ToString("F3"));
        }

        L("");
        L("  ---- 逐个 Renderer（前 80 个）----");
        var rends = UnityEngine.Object.FindObjectsOfType<Renderer>();
        L("  总数 " + rends.Length);
        int shown = 0;
        foreach (var r in rends)
        {
            if (shown++ >= 80) { L("  ... 省略"); break; }
            var m0 = r.sharedMaterial;
            string path = m0 != null ? AssetDatabase.GetAssetPath(m0) : "";
            var b = r.bounds;
            L("  * " + r.name + "  mat=" + (m0 != null ? m0.name : "null")
              + "  asset=" + (string.IsNullOrEmpty(path) ? "**实例**" : path) + "  matObjId=" + (m0 != null ? m0.GetInstanceID() : 0));
            L("      bounds center=" + b.center.ToString("F2") + " size=" + b.size.ToString("F2"));
            L("      " + MP(m0, "_Bands", "_BandBias", "_BandSoftness", "_InkDensity", "_BrushStrength", "_StrokeAmp",
                              "_StrokeScale", "_StrokeStretch", "_GrainAmp", "_GrainScale", "_InkMottle", "_MottleScale",
                              "_AerialStrength", "_HeightFade", "_OutlineWidth", "_ContactInk"));
        }

        // ---------- ② 材质逐个读全参数 ----------
        L("");
        L("---------- ② 材质参数 ----------");
        string[] matPaths = {
            "Assets/_Project/Art/Materials/M_Whitebox_Ground.mat",
            "Assets/_Project/Art/Materials/M_Whitebox_Wall.mat",
            "Assets/_Project/Art/Materials/M_Whitebox_Platform.mat",
            "Assets/_Project/Art/Materials/M_Whitebox_Pillar.mat",
            "Assets/_Project/Art/Materials/M_Ink_Eave.mat",
            "Assets/_Project/Art/Materials/M_Ink_Prop.mat",
            "Assets/_Project/Art/Materials/M_Character_Ink.mat",
            "Assets/_Project/Art/Materials/M_Ink_Enemy_MoTu_0.mat",
            "Assets/_Project/Art/Materials/M_Ink_Enemy_MoOu_0.mat",
            "Assets/_Project/Art/Materials/M_Ink_Enemy_MoYan_0.mat",
            "Assets/_Project/Art/Materials/M_W_Sword_Ink.mat",
        };
        string[] props = {
            "_InkDark", "_InkMid", "_InkLight", "_LadderSkew", "_Bands", "_BandSoftness", "_BandBias",
            "_InkDensity", "_BaseColor", "_BrushScale", "_BrushStrength", "_InkMottle", "_MottleScale",
            "_GrainScale", "_GrainAmp", "_StrokeScale", "_StrokeStretch", "_StrokeAmp",
            "_HeightFade", "_HeightFrom", "_HeightTo", "_AmbientTint",
            "_ContactInk", "_ContactHeight", "_GroundY", "_AerialFrom", "_AerialTo", "_AerialStrength",
            "_OutlineWidth", "_OutlineColor", "_OutlineDistScale", "_OutlineFacing", "_OutlineDry", "_OutlineScale",
            "_ChromaKeep", "_InkDensity2",
        };
        foreach (var p in matPaths)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { L("  (缺) " + p); continue; }
            L("  " + System.IO.Path.GetFileNameWithoutExtension(p) + "   shader=" + m.shader.name);
            L("      " + MP(m, props));
        }

        // ---------- ③ 敌人预制体 ----------
        L("");
        L("---------- ③ 敌人预制体 ----------");
        string[] eps = {
            "Assets/_Project/Prefabs/Enemies/Enemy_MoTu.prefab",
            "Assets/_Project/Prefabs/Enemies/Enemy_MoOu.prefab",
            "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab",
        };
        var m3 = new Mesh();
        foreach (var ep in eps)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ep);
            if (go == null) { L("  (缺) " + ep); continue; }
            L("  " + System.IO.Path.GetFileNameWithoutExtension(ep) + "  scale=" + go.transform.localScale.ToString("F3"));
            L("      层级: " + TreeStr(go.transform, 0));
            var anim = go.GetComponentInChildren<Animator>();
            if (anim != null)
                L("      Animator ctrl=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null")
                  + " avatar=" + (anim.avatar != null ? anim.avatar.name : "null"));
            // 用 BakeMesh 量真身高
            float miny = float.MaxValue, maxy = float.MinValue, minx = float.MaxValue, maxx = float.MinValue;
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                s.BakeMesh(m3, true);
                var l2w = s.transform.localToWorldMatrix;
                foreach (var v in m3.vertices)
                {
                    var w = l2w.MultiplyPoint3x4(v);
                    if (w.y < miny) miny = w.y; if (w.y > maxy) maxy = w.y;
                    if (w.x < minx) minx = w.x; if (w.x > maxx) maxx = w.x;
                }
            }
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var b = mr.bounds;
                if (b.min.y < miny) miny = b.min.y; if (b.max.y > maxy) maxy = b.max.y;
                if (b.min.x < minx) minx = b.min.x; if (b.max.x > maxx) maxx = b.max.x;
            }
            L("      BakeMesh 身高 = " + (maxy - miny).ToString("F3") + " m   宽(X) = " + (maxx - minx).ToString("F3"));
            foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                L("      SMR '" + s.name + "'  verts=" + s.sharedMesh.vertexCount
                  + "  tris=" + TriCount(s.sharedMesh)
                  + "  mat=" + s.sharedMaterial.name);
            }
            var eb = go.GetComponentInChildren<InkWash.Enemies.EnemyBase>();
            if (eb != null)
            {
                var so = new SerializedObject(eb);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                        L("      field " + it.propertyPath + " = " + it.objectReferenceValue.name);
                    else if (it.propertyType == SerializedPropertyType.Float || it.propertyType == SerializedPropertyType.Integer)
                        L("      field " + it.propertyPath + " = " + it.doubleValue);
                }
            }
        }

        // ---------- ④ 主角预制体 ----------
        L("");
        L("---------- ④ 主角预制体 ----------");
        var pl = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
        L("  Player.prefab 层级:");
        L(TreeStr(pl.transform, 1));
        var sv = pl.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
        if (sv != null)
        {
            var so = new SerializedObject(sv);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                    L("      SwordVfx." + it.propertyPath + " = " + it.objectReferenceValue.name
                      + " (" + it.objectReferenceValue.GetType().Name + ")");
                else if (it.propertyType == SerializedPropertyType.Float) L("      SwordVfx." + it.propertyPath + " = " + it.doubleValue.ToString("F3"));
                else if (it.propertyType == SerializedPropertyType.Boolean) L("      SwordVfx." + it.propertyPath + " = " + it.boolValue);
            }
        }
        var cs2 = pl.GetComponentInChildren<InkWash.Player.CombatStance>(true);
        if (cs2 != null)
        {
            var so = new SerializedObject(cs2);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                    L("      CombatStance." + it.propertyPath + " = " + it.objectReferenceValue.name
                      + " (" + it.objectReferenceValue.GetType().Name + ")");
                else if (it.propertyType == SerializedPropertyType.Float) L("      CombatStance." + it.propertyPath + " = " + it.doubleValue.ToString("F3"));
                else if (it.propertyType == SerializedPropertyType.Boolean) L("      CombatStance." + it.propertyPath + " = " + it.boolValue);
            }
        }

        // ---------- ⑤ 跑步有关的片段：来源 + 手臂曲线是否静止 ----------
        L("");
        L("---------- ⑤ Run 片段取证 ----------");
        foreach (var p in new[] { "Assets/_Project/Animations/Baked/Run04_KI_Carry.anim",
                                  "Assets/_Project/Animations/Baked/Idle_Carry_A.anim",
                                  "Assets/_Project/Animations/Feng_Idle_Loop.anim",
                                  "Assets/_Project/Animations/Feng_Walk_Loop.anim",
                                  "Assets/_Project/Animations/Sword_Idle_Loop.anim" })
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
            if (c == null) { L("  (缺) " + p); continue; }
            L("  " + System.IO.Path.GetFileName(p) + "  len=" + c.length.ToString("F3")
              + "  loop=" + c.isLooping  + "  legacy=" + c.legacy + "  curves=" + AnimationUtility.GetCurveBindings(c).Length);
            // 统计「手臂」曲线的活动量
            var binds = AnimationUtility.GetCurveBindings(c);
            int armCurves = 0; int armMoved = 0; float maxRange = 0f;
            string worst = "";
            foreach (var b in binds)
            {
                string pn = b.path.ToLowerInvariant();
                if (!(pn.Contains("arm") || pn.Contains("hand") || pn.Contains("forearm") || pn.Contains("shoulder") || pn.Contains("clavicle")))
                    continue;
                armCurves++;
                var curve = AnimationUtility.GetEditorCurve(c, b);
                if (curve == null || curve.length < 2) continue;
                float mn = float.MaxValue, mx = float.MinValue;
                foreach (var k in curve.keys) { if (k.value < mn) mn = k.value; if (k.value > mx) mx = k.value; }
                float range = mx - mn;
                if (range > 0.02f) armMoved++;
                if (range > maxRange) { maxRange = range; worst = b.path + "." + b.propertyName + " range=" + range.ToString("F3"); }
            }
            L("      手臂/肩曲线 " + armCurves + " 条，其中活动(range>0.02) " + armMoved + " 条");
            L("      最大活动: " + worst);
        }

        // ---------- ⑥ 可用原料：Kevin Iglesias + UAL 全部片段 ----------
        L("");
        L("---------- ⑥ 全工程 AnimationClip（按 FBX） ----------");
        var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
        var byDir = new SortedDictionary<string, List<string>>();
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!p.ToLowerInvariant().EndsWith(".fbx")) continue;
            var subs = AssetDatabase.LoadAllAssetsAtPath(p);
            foreach (var s in subs)
            {
                var c = s as AnimationClip;
                if (c == null || c.name.StartsWith("__preview")) continue;
                string dir = p.Contains("/") ? p.Substring(0, p.LastIndexOf('/')) : p;
                if (!byDir.ContainsKey(dir)) byDir[dir] = new List<string>();
                byDir[dir].Add(c.name + " (" + c.length.ToString("F2") + "s)");
            }
        }
        foreach (var kv in byDir)
        {
            L("  [" + kv.Key + "]");
            kv.Value.Sort();
            foreach (var c in kv.Value) L("      " + c);
        }

        // ---------- ⑦ 技能栏现状 ----------
        L("");
        L("---------- ⑦ 技能 / Roguelike ----------");
        var inv = pl.GetComponentInChildren<InkWash.Roguelike.SkillInventory>(true);
        if (inv != null)
        {
            var so = new SerializedObject(inv);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                    L("      " + it.propertyPath + " = " + it.objectReferenceValue.name);
                else if (it.propertyType == SerializedPropertyType.Float || it.propertyType == SerializedPropertyType.Integer)
                    L("      " + it.propertyPath + " = " + it.doubleValue);
            }
        }
        var poolGuids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/_Project" });
        L("  ScriptableObject 资产：");
        foreach (var g in poolGuids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var o = AssetDatabase.LoadAssetAtPath<ScriptableObject>(p);
            L("      " + p + "  (" + (o != null ? o.GetType().Name : "?") + ")");
        }

        L("");
        L("=========== end ===========");
        string outp = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Tools/reports/r2_probe.txt"));
        System.IO.File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }

    static string TreeStr(Transform t, int d)
    {
        if (t == null) return "";
        var sb = new StringBuilder();
        sb.Append(new string(' ', d * 2) + "- " + t.name);
        var cl = new List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) { cl.Add("MISSING"); continue; }
            if (c is Transform) continue;
            cl.Add(c.GetType().Name);
        }
        sb.Append("  [" + string.Join(",", cl) + "]");
        sb.Append(" ls=" + t.localScale.ToString("F3"));
        sb.Append("\n");
        for (int i = 0; i < t.childCount; i++) sb.Append(TreeStr(t.GetChild(i), d + 1));
        return sb.ToString();
    }

    static int TriCount(Mesh m)
    {
        if (m == null) return 0;
        int n = 0;
        for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i) / 3;
        return n;
    }
}

return R2Probe.Run();
