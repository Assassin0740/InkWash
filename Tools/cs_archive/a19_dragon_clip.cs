// a19_dragon_clip.cs —— 龙那唯一一段 57.5 s 的片段到底是什么？
//
// 为什么要先看它：程序驱动骨骼是"原语"，但**如果自带的片段本身就是可用的运动**，
//   那正确做法是"用它 + 程序叠加"，而不是从零写一套正弦波 —— 前者能白拿作者调好的曲线。
//
// 具体要回答：
//   1. 57.5 s 是一整个大循环，还是多段动作连在一起？（沿时间扫骨骼位移找拐点）
//   2. 髋/头/尾各在什么位置、动多少？（决定能不能切出"盘旋""甩尾"）
//   3. 顶点是否整体位移（= 根位移）？有的话可以把飞行轨迹拿出来。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

const string FbxPath = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";
var clipSrc = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
AnimationClip clip = null;
foreach (var o in clipSrc)
    if (o is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }

sb.AppendLine("========== 龙的片段分析 ==========");
if (clip == null) { sb.AppendLine("★ 找不到片段"); File.WriteAllText(Path.Combine(root, "Tools/reports/a19_dragon_clip.txt"), sb.ToString()); Debug.Log(sb.ToString()); return; }

sb.AppendLine("片段 '" + clip.name + "'  len=" + clip.length.ToString("F3") + " s  frameRate=" + clip.frameRate
              + "  isLooping=" + clip.isLooping + "  legacy=" + clip.legacy);
sb.AppendLine("绑定曲线总数 = " + AnimationUtility.GetCurveBindings(clip).Length
              + "  对象曲线 = " + AnimationUtility.GetObjectReferenceCurveBindings(clip).Length);

// ---- 1) 把所有 Transform 曲线按"路径"分组，看有多少骨骼被动过 ----
var bindings = AnimationUtility.GetCurveBindings(clip);
var byPath = new Dictionary<string, int>();
foreach (var b in bindings)
{
    if (!byPath.ContainsKey(b.path)) byPath[b.path] = 0;
    byPath[b.path]++;
}
sb.AppendLine();
sb.AppendLine("被动画的路径数 = " + byPath.Count);
int shown = 0;
foreach (var kv in byPath)
{
    if (shown++ >= 25) { sb.AppendLine("  ...（还有 " + (byPath.Count - 25) + " 条）"); break; }
    sb.AppendLine("  " + kv.Key + "  ← " + kv.Value + " 条曲线");
}

// ---- 2) 沿时间扫"整体幅度"，找动作分段 ----
sb.AppendLine();
sb.AppendLine("---- 沿时间扫描（每 2 s 一格，找动作拐点）----");
sb.AppendLine("  t(s)   位移最大骨骼路径                                     该时刻总位移(m)");
int steps = 30;
for (int i = 0; i <= steps; i++)
{
    float t = clip.length * i / steps;
    float mx = 0f; string who = "";
    foreach (var b in bindings)
    {
        if (b.type != typeof(Transform)) continue;
        var c = AnimationUtility.GetEditorCurve(clip, b);
        if (c == null) continue;
        float v = c.Evaluate(t);
        // 只关心位置通道的绝对值幅度
        if (b.propertyName.StartsWith("m_LocalPosition"))
        {
            // 保守：用单通道绝对值当幅度指标
            if (Mathf.Abs(v) > mx) { mx = Mathf.Abs(v); who = b.path + "." + b.propertyName; }
        }
    }
    sb.AppendLine("  " + t.ToString("F1").PadLeft(5) + "   " + who.PadRight(56) + "  " + mx.ToString("F4"));
}

UnityEngine.Object.DestroyImmediate(clip);
File.WriteAllText(Path.Combine(root, "Tools/reports/a19_dragon_clip.txt"), sb.ToString());
Debug.Log("[a19]\n" + sb.ToString());
