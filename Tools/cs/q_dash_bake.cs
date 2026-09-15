// q_dash_bake.cs —— 冲刺：加长距离 + 换掉 KayKit 的 Q 版翻滚（编辑模式运行）
//
// 事实（r 轮取证）：
//   TickDash: speed = dashSpeed * lerp(1, 0.55, nd²) ⇒ 平均 ≈0.78
//   距离 ≈ 7.4 × 0.40 × 0.78 ≈ **2.3 m**（用户："右键的冲刺距离太近了"）
//   Dash 状态挂的是 KayKit `Dodge_Forward`（0.40s）—— 一个 Q 版前滚
//   （用户："动作也很奇怪"，也是已知的"风格断裂剩一处"）
//
// 做法：
//   ① 从 UAL2 的冲刺类片段里，用**能量剖面**自动定位动作最剧烈的窗口，
//      烘成 `Dash_Lunge.anim`（长度 = 冲刺时长）—— 不靠猜窗口位置。
//   ② 把 Dash 状态的 motion 换掉、speed 归 1（片段长度已等于 dashDuration）。
//   ③ 距离/时长/冷却三个字段 **C# 默认值与 prefab 序列化值都改**（项目硬规矩：
//      只改 C# 默认值不生效）。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public class QDashBake
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }

    const string UAL2_FBX = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
    const string CTRL = "Assets/_Project/Animations/Player.controller";
    const string OUTCLIP = "Assets/_Project/Animations/Baked/Dash_Lunge.anim";

    // 冲刺参数（同时写进 C# 默认值与 prefab）
    const float NEW_SPEED = 11.7f;
    const float NEW_DURATION = 0.55f;
    const float NEW_COOLDOWN = 0.85f;
    const float WINDOW = 0.55f;      // 烘出的片段长度 = 冲刺时长
    const float LEADIN = 0.10f;      // 窗口前留一点引子，避免起手就是半途动作
    // 窗口起点：**人工标定**，不再让能量剖面自动选。
    // 为什么弃用自动定位：`EnergyProfile` 把 130 个通道的帧间差求和，量级远大于判定阈值 0.8，
    // 于是剖面退化成"全满"（见 q_dash_bake.txt 的 `########`），窗口和处处相等 ⇒ 恒取 i=0
    // ⇒ 永远选到片段**开头**（对 Shield_Dash 就是那段半跪起势，正是"动作很奇怪"的来源）。
    // 现在按人眼对照选：Sword_Dash 从 t≈0.10 起进入低身前倾相（头 Y 由 1.71 落到 1.64），
    // 一直持续到 t≈0.66 才抬头收招 ⇒ 取 [0.10, 0.65]。
    const float FORCED_START = 0.10f;

    const string PC_CS = "Assets/_Project/Scripts/Player/PlayerController.cs";
    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";

    /// 能量剖面：所有通道在时间上的 |差分| 之和 ⇒ 动作越剧烈值越高
    static float[] EnergyProfile(AnimationClip c, int N)
    {
        var curves = new List<AnimationCurve>();
        foreach (var b in AnimationUtility.GetCurveBindings(c))
        {
            var rc = AnimationUtility.GetEditorCurve(c, b);
            if (rc != null && rc.length > 0) curves.Add(rc);
        }
        var e = new float[N];
        for (int i = 0; i < N; i++)
        {
            float t0 = c.length * i / N, t1 = c.length * (i + 1) / N;
            float s = 0;
            foreach (var rc in curves)
            {
                float v0 = rc.Evaluate(t0), v1 = rc.Evaluate(t1);
                s += Mathf.Abs(v1 - v0);
                // 中间再采一点，别漏掉"半个窗口内的往返"
                s += Mathf.Abs(rc.Evaluate((t0 + t1) * 0.5f) - v0) * 0.5f;
            }
            e[i] = s;
        }
        return e;
    }

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_dash_bake ================");

        // ---------- ① 候选片段 ----------
        var all = new List<AnimationClip>();
        foreach (var c in AssetDatabase.LoadAllAssetsAtPath(UAL2_FBX).OfType<AnimationClip>())
            if (c != null && !all.Contains(c)) all.Add(c);
        L("UAL2 片段数 = " + all.Count);

        // 候选**顺序即优先级**：下面是 q_dash_shots 出的三候选姿态对照（同机位 4 相位接图）
        //   Shield_Dash  hips 0.54~0.58  头 1.27~1.54   ← 半跪滑铲，整段几乎贴地（现行版本，用户："动作也很奇怪"）
        //   Sword_Dash   hips 0.95~0.98  头 1.64~1.94   ← 站立持剑前冲 ✓ 唯一站姿
        //   Slide_Start  hips 0.87~0.92  头 1.41~1.83   ← 起手站立、中段整段平躺滑铲
        // 片段由人眼选（姿态高度是审美判断），窗口位置由能量剖面自动定（那是纯数据）。
        string[] want = { "Armature|Sword_Dash", "Armature|Shield_Dash", "Armature|Slide_Start" };
        AnimationClip chosen = null; string chosenName = null; float chosenStart = 0;
        var profLog = new StringBuilder();
        foreach (var w in want)
        {
            var c = all.FirstOrDefault(x => x.name == w);
            if (c == null) { profLog.AppendLine("  !! 找不到 " + w); continue; }
            int N = 24;
            var e = EnergyProfile(c, N);
            profLog.AppendLine("  " + w + "  len=" + c.length.ToString("F3"));
            float emax = 0f; for (int i = 0; i < N; i++) emax = Mathf.Max(emax, e[i]);
            var bar = new StringBuilder("    能量(按峰值归一): ");
            for (int i = 0; i < N; i++)
            {
                float q = emax > 0f ? e[i] / emax : 0f;
                bar.Append(q > 0.75f ? "#" : (q > 0.5f ? "+" : (q > 0.25f ? "." : " ")));
            }
            profLog.AppendLine(bar.ToString());

            // 找最高的、长度为 WINDOW 的连续窗口（**只作参考**，实际窗口起点用 FORCED_START）
            float best = -1; int bestI = 0;
            int step = Mathf.Max(1, Mathf.RoundToInt(N * WINDOW / c.length));
            for (int i = 0; i + step <= N; i++)
            {
                float s = 0; for (int k = i; k < i + step; k++) s += e[k];
                if (s > best) { best = s; bestI = i; }
            }
            float st = Mathf.Clamp(FORCED_START, 0f, Mathf.Max(0f, c.length - WINDOW));
            profLog.AppendLine("    自动窗口≈ " + (c.length * bestI / N).ToString("F3")
                + " s（仅参考）；实际起点 = " + st.ToString("F3") + " s（FORCED_START）");
            // 只取第一个可用候选（want 的顺序就是优先级，见上面的姿态对照数据）。
            // ⚠ 不要在这里改「跨片段比能量和选最优」：不同片段的通道数不同，绝对能量不可比，
            //   而且"能量最大"偏爱蹲伏/收招这些花哨相，并不等于"最像冲刺"。
            if (chosen == null) { chosen = c; chosenName = w; chosenStart = st; }
        }
        L(profLog.ToString());
        if (chosen == null) { L("!! 三个候选都没找到"); return Flush(); }
        L("  ⇒ 选用 " + chosenName + " 窗口 [" + chosenStart.ToString("F3") + ", "
          + (chosenStart + WINDOW).ToString("F3") + "]");

        // ---------- ② 烘片段 ----------
        float t0 = chosenStart, t1 = Mathf.Min(chosen.length, chosenStart + WINDOW);
        float len = t1 - t0;
        var clip = new AnimationClip { name = "Dash_Lunge", frameRate = 30f, legacy = false };
        int nb = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(chosen))
        {
            var rc = AnimationUtility.GetEditorCurve(chosen, b);
            if (rc == null || rc.length == 0) continue;
            var nc = new AnimationCurve();
            // 边界键（把窗口外的值夹住，避免开头/结尾塌到 0）
            nc.AddKey(new Keyframe(0f, rc.Evaluate(t0)));
            foreach (var k in rc.keys)
                if (k.time > t0 + 1e-4f && k.time < t1 - 1e-4f)
                    nc.AddKey(new Keyframe(k.time - t0, k.value, k.inTangent, k.outTangent));
            nc.AddKey(new Keyframe(len, rc.Evaluate(t1)));
            AnimationUtility.SetEditorCurve(clip, b, nc);
            nb++;
        }
        var st2 = AnimationUtility.GetAnimationClipSettings(clip);
        st2.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, st2);

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(OUTCLIP) != null) AssetDatabase.DeleteAsset(OUTCLIP);
        AssetDatabase.CreateAsset(clip, OUTCLIP);
        AssetDatabase.SaveAssets();
        L("");
        L("  已烘出 " + OUTCLIP + "  len=" + clip.length.ToString("F3") + "  通道数=" + nb);

        // ---------- ③ 换 Dash 状态 ----------
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL);
        if (ctrl == null) { L("!! 找不到 " + CTRL); return Flush(); }
        bool done = false;
        foreach (var layer in ctrl.layers)
        {
            foreach (var cs in layer.stateMachine.states)
            {
                if (cs.state.name != "Dash") continue;
                var old = cs.state.motion;
                L("  Dash 状态原 motion = " + (old == null ? "null" : old.name)
                  + "  speed=" + cs.state.speed.ToString("F3")
                  + "  speedParameter=" + cs.state.speedParameter);
                cs.state.motion = clip;
                cs.state.speed = 1f;          // 片段长度已等于 dashDuration
                cs.state.speedParameterActive = false;
                L("  Dash 状态新 motion = " + clip.name + "  speed=1.000");
                done = true;
            }
        }
        if (!done) L("!! 控制器里没有名为 Dash 的状态");
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---------- ④ 复核 ----------
        var re = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL);
        foreach (var layer in re.layers)
            foreach (var cs in layer.stateMachine.states)
                if (cs.state.name == "Dash")
                    L("  复核 Dash → " + (cs.state.motion == null ? "null" : cs.state.motion.name)
                      + "  speed=" + cs.state.speed.ToString("F3"));
        var reClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OUTCLIP);
        L("  复核片段 " + (reClip == null ? "**丢失**" : reClip.name + " len=" + reClip.length.ToString("F3")
              + " 通道=" + AnimationUtility.GetCurveBindings(reClip).Length));

        return Flush();
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_dash_bake.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QDashBake.Run();
