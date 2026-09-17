// r1_probe.cs —— 第九轮开工前的现场清点
// 只读，不改任何东西。回答五个问题：
//   ① 编辑器在不在 Play（决定后面要不要先 stop）
//   ② 主角层级 + 剑挂在哪（"跑步时剑不跟着动"要么是没挂在手骨上，要么是挂在手骨上但被别的东西覆盖）
//   ③ 敌人用的是什么预制体 / 什么材质 / 多大 / 什么颜色（"怪物太丑"要落到具体资产）
//   ④ 动画控制器里有哪些状态、各挂哪个片段；还有哪些**已导入但没用上**的片段（"多加攻击动作"的原料）
//   ⑤ 输入绑定现状（"多加技能按键"要知道现在有几个）
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Animations;

public class R1Probe
{
    static StringBuilder _sb;

    static void L(string s) { _sb.AppendLine(s); }

    static string MatDesc(Renderer r)
    {
        if (r == null) return "(null)";
        var sb = new StringBuilder();
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (m == null) { sb.Append("[null]"); continue; }
            string path = AssetDatabase.GetAssetPath(m);
            string col = "-";
            if (m.HasProperty("_BaseColor")) col = m.GetColor("_BaseColor").ToString("F3");
            else if (m.HasProperty("_Color")) col = m.GetColor("_Color").ToString("F3");
            string extra = "";
            if (m.HasProperty("_BandBias")) extra += " bandBias=" + m.GetFloat("_BandBias").ToString("F3");
            if (m.HasProperty("_InkDensity")) extra += " inkDen=" + m.GetFloat("_InkDensity").ToString("F3");
            if (m.HasProperty("_ChromaKeep")) extra += " chroma=" + m.GetFloat("_ChromaKeep").ToString("F3");
            if (m.HasProperty("_OutlineWidth")) extra += " outline=" + m.GetFloat("_OutlineWidth").ToString("F3");
            if (m.HasProperty("_Bands")) extra += " bands=" + m.GetFloat("_Bands").ToString("F2");
            sb.Append(m.name + "(shader=" + m.shader.name + " base=" + col + extra + ")\n");
            sb.Append("      asset=" + (string.IsNullOrEmpty(path) ? "**运行时实例**" : path) + "\n");
        }
        return sb.ToString();
    }

    static void DumpTree(Transform t, int depth, string tag)
    {
        if (t == null) return;
        string pad = new string(' ', depth * 2);
        var comps = new List<string>();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) { comps.Add("MISSING"); continue; }
            string n = c.GetType().Name;
            if (n == "Transform") continue;
            comps.Add(n);
        }
        float scale = t.lossyScale.x;
        L(pad + "- " + t.name + "  [" + string.Join(",", comps) + "]  lossyScale=" + scale.ToString("F3")
          + "  worldPos=" + t.position.ToString("F3"));
        for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), depth + 1, tag);
    }

    public static string Run()
    {
        _sb = new StringBuilder();

        L("================ r1_probe ================");
        L("isPlaying = " + EditorApplication.isPlaying
          + "  isCompiling = " + EditorApplication.isCompiling
          + "  compFailed = " + EditorUtility.scriptCompilationFailed);

        // ---------- ① 主角 ----------
        var player = GameObject.Find("Player");
        if (player == null)
        {
            var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
            if (pc != null) player = pc.gameObject;
        }
        L("");
        L("---------- ① 主角 ----------");
        if (player == null) { L("(找不到 Player)"); }
        else
        {
            L("Player at " + player.transform.position.ToString("F3"));
            DumpTree(player.transform, 0, "player");

            var anim = player.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                L("  Animator: avatar=" + (anim.avatar != null ? anim.avatar.name : "null")
                  + " human=" + (anim.avatar != null && anim.avatar.isHuman)
                  + " ctrl=" + (anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null")
                  + " speed=" + anim.speed);
                var occ = anim.runtimeAnimatorController as AnimatorOverrideController;
                if (occ != null)
                {
                    L("  ★ AnimatorOverrideController base=" + occ.runtimeAnimatorController.name);
                    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                    occ.GetOverrides(pairs);
                    foreach (var kv in pairs)
                        L("      slot '" + (kv.Key != null ? kv.Key.name : "null") + "' -> "
                          + (kv.Value != null ? kv.Value.name + " (" + kv.Value.length.ToString("F3") + "s)" : "<empty>"));
                }
            }

            // 剑：找出所有名字像剑的物体，打印它的父链
            L("  ---- 疑似武器/挂件的父链 ----");
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("sword") || n.Contains("weapon") || n.Contains("blade") || n.Contains("w_sword"))
                {
                    var chain = new List<string>();
                    var cur = t;
                    while (cur != null) { chain.Add(cur.name); cur = cur.parent; }
                    chain.Reverse();
                    L("    '" + t.name + "'  chain = " + string.Join(" / ", chain));
                    var mr = t.GetComponentInChildren<MeshRenderer>();
                    if (mr != null) L("        mat: " + MatDesc(mr).Trim());
                }
            }
        }

        // ---------- ② 敌人 ----------
        L("");
        L("---------- ② 敌人 ----------");
        var spawner = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
        if (spawner != null)
        {
            L("WaveSpawner 存在；字段清点见下");
            var so = new SerializedObject(spawner);
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference
                    && it.objectReferenceValue != null)
                    L("    " + it.propertyPath + " = " + it.objectReferenceValue.name
                      + "  (" + it.objectReferenceValue.GetType().Name + ")");
                else if (it.propertyType == SerializedPropertyType.Float
                      || it.propertyType == SerializedPropertyType.Integer)
                    L("    " + it.propertyPath + " = " + it.doubleValue);
            }
        }
        else L("(场景里没有 WaveSpawner)");

        // 场景里现存的敌人实例
        var enemies = UnityEngine.Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>();
        L("场景里现在有 " + enemies.Length + " 个 EnemyBase");
        foreach (var e in enemies)
        {
            L("  enemy '" + e.gameObject.name + "'  pos=" + e.transform.position.ToString("F2")
              + "  lossyScale=" + e.transform.lossyScale.ToString("F3"));
            DumpTree(e.transform, 2, "enemy");
        }

        // 所有敌人预制体（按 GUID 找，看目录）
        L("");
        L("  ---- Assets 里所有敌人预制体 ----");
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project", "Assets/ThirdParty" });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            string lower = p.ToLowerInvariant();
            if (!(lower.Contains("enemy") || lower.Contains("skeleton") || lower.Contains("skel")
                  || lower.Contains("kaykit") || lower.Contains("undead") || lower.Contains("monster"))) continue;
            L("    " + p);
        }

        // ---------- ③ 动画控制器 ----------
        L("");
        L("---------- ③ 动画控制器状态 ----------");
        var ctrlGuids = AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/_Project" });
        foreach (var g in ctrlGuids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
            if (ctrl == null) continue;
            L("  controller: " + p);
            foreach (var layer in ctrl.layers)
            {
                L("    layer '" + layer.name + "'  sm=" + (layer.stateMachine != null ? layer.stateMachine.name : "-"));
                if (layer.stateMachine == null) continue;
                foreach (var cs in layer.stateMachine.states)
                {
                    var st = cs.state;
                    string clipNames = "";
                    if (st.motion is AnimationClip) clipNames = ((AnimationClip)st.motion).name
                        + " (" + ((AnimationClip)st.motion).length.ToString("F3") + "s)";
                    else if (st.motion is BlendTree) clipNames = "<BlendTree>";
                    else clipNames = st.motion == null ? "<none>" : st.motion.name;
                    L("        state '" + st.name + "'  clip=" + clipNames
                      + "  speed=" + st.speed.ToString("F2")
                      + "  parts=" + (st.motion is BlendTree ? DumpBlendTree((BlendTree)st.motion) : "-"));
                    foreach (var tr in st.transitions)
                    {
                        string conds = "";
                        foreach (var c in tr.conditions)
                            conds += c.mode + "(" + c.parameter + (c.mode == AnimatorConditionMode.If || c.mode == AnimatorConditionMode.IfNot ? "" : " " + c.threshold.ToString("F2")) + ") ";
                        L("            -> " + tr.destinationState.name
                          + "  exitTime=" + tr.hasExitTime + "/" + tr.exitTime.ToString("F2")
                          + "  dur=" + tr.duration.ToString("F2")
                          + "  interrupt=" + tr.interruptionSource
                          + "  cond=[" + conds + "]");
                    }
                }
            }
        }

        // ---------- ④ 可用片段（原料） ----------
        L("");
        L("---------- ④ 已导入的全部 AnimationClip（按目录） ----------");
        var clipDirs = new Dictionary<string, List<string>>();
        var allGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty", "Assets/_Project" });
        foreach (var g in allGuids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!p.ToLowerInvariant().EndsWith(".fbx")) continue;
            var subs = AssetDatabase.LoadAllAssetsAtPath(p);
            foreach (var s in subs)
            {
                var c = s as AnimationClip;
                if (c == null) continue;
                if (c.name.StartsWith("__preview")) continue;
                string dir = p.Substring(0, p.LastIndexOf('/'));
                if (!clipDirs.ContainsKey(dir)) clipDirs[dir] = new List<string>();
                clipDirs[dir].Add(c.name + " " + c.length.ToString("F2") + "s");
            }
        }
        foreach (var kv in clipDirs)
        {
            // 只打疑似剑术/攻击/移动的目录
            string d = kv.Key.ToLowerInvariant();
            if (!(d.Contains("ual") || d.Contains("sword") || d.Contains("attack") || d.Contains("combat")
                  || d.Contains("feng") || d.Contains("kianim") || d.Contains("soldier"))) continue;
            L("  [" + kv.Key + "]");
            kv.Value.Sort();
            foreach (var c in kv.Value) L("      " + c);
        }

        L("");
        L("================ end ================");
        // 长报告走 File.WriteAllText 落盘 —— 桥会把 return 的长字符串截断
        string outp = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Tools/reports/r1_probe.txt"));
        System.IO.File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }

    static string DumpBlendTree(BlendTree bt)
    {
        var sb = new StringBuilder();
        sb.Append("bt('" + bt.name + "' type=" + bt.blendType + " px=" + bt.blendParameter + " py=" + bt.blendParameterY + ") children=");
        var kids = bt.children;
        for (int i = 0; i < kids.Length; i++)
        {
            var c = kids[i];
            string cn = c.motion is AnimationClip ? ((AnimationClip)c.motion).name : (c.motion == null ? "<none>" : c.motion.name);
            sb.Append("[pos=" + c.position.ToString("F2") + " " + cn + "]");
        }
        return sb.ToString();
    }
}

return R1Probe.Run();
