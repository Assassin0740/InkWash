// b_gate.cs —— 增量 B 的闸门。顶层语句风格（用 return 交出结果）。
//
// ① 编译状态：`scriptCompilationFailed` 必须为 False
// ② DLL 新鲜度：Assembly-CSharp.dll 必须**不早于**最新 .cs 源
// ③ ★ **Shader 消息**（本闸门新增，也是最关键的一项）：
//    着色器里的 HLSL 错误**不会进 Console**。`Shader.Find` 照样返回对象、
//    `passCount` 照样有值 —— 所以"Console 干净 + pass 数正常"根本证明不了 shader 编得过。
//    唯一可靠的做法是读 `ShaderUtil.GetShaderMessages`。
//    本项目之前几轮的闸门都只查了 passCount，那是个**假闸门**。
// ④ 材质属性：新加的墨阶属性必须真的落在材质上（改名后不回填 = 静默回落到默认值）
//    并检查 `_OutlineWidth` 是否在 .mat 里**显式**写出 —— 缺了不会报错，只会静默用 shader 默认值
// ⑤ 光照：InkLighting 已生效
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

var sb = new StringBuilder();
sb.AppendLine("===== 增量 B 闸门：墨阶阶梯 =====");
sb.AppendLine();

// ---- ① 编译状态 ----
sb.AppendLine("---- ① 编译状态 ----");
sb.AppendLine("  isCompiling             = " + EditorApplication.isCompiling);
sb.AppendLine("  scriptCompilationFailed = " + EditorUtility.scriptCompilationFailed);
sb.AppendLine();

// ---- ② DLL 新鲜度 ----
sb.AppendLine("---- ② DLL 新鲜度 ----");
string dll = "Library/ScriptAssemblies/Assembly-CSharp.dll";
var dllTime = File.Exists(dll) ? File.GetLastWriteTime(dll) : DateTime.MinValue;
DateTime newest = DateTime.MinValue;
string newestName = "(none)";
foreach (var f in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories))
{
    var t = File.GetLastWriteTime(f);
    if (t > newest) { newest = t; newestName = f.Replace('\\', '/'); }
}
sb.AppendLine("  DLL   写入 = " + (dllTime == DateTime.MinValue ? "(缺失)" : dllTime.ToString("HH:mm:ss")));
sb.AppendLine("  最新源     = " + newest.ToString("HH:mm:ss") + "  " + newestName);
sb.AppendLine("  判定       = " + (dllTime >= newest ? "新鲜" : "**过期（类型可见性不可信）**"));
sb.AppendLine();

// ---- ③ Shader 消息（HLSL 编译是否真的过） ----
sb.AppendLine("---- ③ Shader 消息 ----");
int shaderErr = 0, shaderWarn = 0;
foreach (var n in new[]
{
    "InkWash/InkSurface", "InkWash/InkCharacter", "InkWash/InkSky", "InkWash/InkSlash",
    "InkWash/InkSplash",
    "Hidden/InkWash/InkEdge", "Hidden/InkWash/InkPaper", "Hidden/InkWash/InkBloom",
})
{
    var sh = Shader.Find(n);
    if (sh == null) { sb.AppendLine("  " + n.PadRight(28) + " → **找不到**"); shaderErr++; continue; }

    int err = 0, warn = 0;
    string firstErr = null;
    try
    {
        // ★ 不要直接引用 `ShaderCompilerMessageSeverity` 枚举 —— 团结引擎里它不叫这个名字
        //   （实测 CS0103）。改为反射读消息对象的 severity/message 字段：
        //   不去猜 API 名，就不存在猜错的问题。
        var msgs = ShaderUtil.GetShaderMessages(sh);
        foreach (var m in msgs)
        {
            var t = m.GetType();
            string sev = "", txt = "";
            var ps = t.GetProperty("severity") ?? (System.Reflection.PropertyInfo)null;
            var pm = t.GetProperty("message");
            if (ps != null) sev = ps.GetValue(m)?.ToString() ?? "";
            if (pm != null) txt = pm.GetValue(m)?.ToString() ?? "";
            if (sev.StartsWith("Error")) { err++; if (firstErr == null) firstErr = txt; }
            else if (sev.StartsWith("Warning")) warn++;
        }
    }
    catch (Exception e) { sb.AppendLine("    (读 shader 消息失败：" + e.Message + ")"); }

    shaderErr += err; shaderWarn += warn;
    sb.AppendLine("  " + n.PadRight(28) + " → pass=" + sh.passCount
                  + "  error=" + err + " warning=" + warn
                  + (err > 0 ? "  **" + firstErr + "**" : ""));
}
sb.AppendLine("  合计 error=" + shaderErr + " warning=" + shaderWarn);
sb.AppendLine();

// ---- ④ 材质属性 ----
sb.AppendLine("---- ④ 材质属性（新墨阶表是否真的落上） ----");
string[] mats =
{
    "M_Whitebox_Ground", "M_Whitebox_Wall", "M_Whitebox_Pillar", "M_Whitebox_Platform",
    "M_Character_Ink", "M_Ink_Enemy_MoOu_0", "M_Ink_Enemy_MoTu_0", "M_Ink_Enemy_MoYan_0",
    "M_W_Sword_Ink", "M_Ink_Eave", "M_Ink_Prop",
};
int matBad = 0;
foreach (var mn in mats)
{
    var m = AssetDatabase.LoadAssetAtPath<Material>(
        "Assets/_Project/Art/Materials/" + mn + ".mat");
    if (m == null) { sb.AppendLine("  " + mn + " → **加载失败**"); matBad++; continue; }

    bool ok = m.HasProperty("_InkDark") && m.HasProperty("_InkMid") && m.HasProperty("_InkLight")
              && m.HasProperty("_BandBias") && m.HasProperty("_LadderSkew");
    bool dead = m.HasProperty("_InkColor") || m.HasProperty("_PaperColor");

    // ★ 「shader 有这个属性」≠「材质显式设了这个值」。
    //   材质文件里没有这一行 ⇒ **静默落到 shader 默认值** —— grep 查不到、改 shader 会连带改观感。
    //   所以凡是带 _OutlineWidth / _ChromaKeep 的材质，都要求它在 .mat 里**显式写出来**（本项目硬规矩 #1）。
    bool needOutline = m.HasProperty("_OutlineWidth");
    bool explicitOutline = !needOutline || HasExplicit(mn, "_OutlineWidth");
    if (needOutline && !explicitOutline) matBad++;
    // _ChromaKeep 同理：它在 InkCharacter 上有默认 0.45，材质里不写就永远吃默认值 ——
    // 本轮"人物墨彩"就是踩这个坑进来的（角色材质从来没显式写过它）。
    bool needChroma = m.HasProperty("_ChromaKeep");
    bool explicitChroma = !needChroma || HasExplicit(mn, "_ChromaKeep");
    if (needChroma && !explicitChroma) matBad++;
    if (!ok || dead) matBad++;

    // 墨阶亮度：用相对亮度看分配是否合理（焦<浓<重<淡<清）
    float la = m.HasProperty("_InkDark") ? Luma(m.GetColor("_InkDark")) : -1f;
    float lm = m.HasProperty("_InkMid") ? Luma(m.GetColor("_InkMid")) : -1f;
    float ll = m.HasProperty("_InkLight") ? Luma(m.GetColor("_InkLight")) : -1f;
    sb.AppendLine("  " + mn.PadRight(22)
                  + (ok ? "OK " : "**缺属性**")
                  + (dead ? " **旧属性还在**" : "")
                  + "  焦=" + F(la) + " 重=" + F(lm) + " 清=" + F(ll)
                  + "  _BandBias=" + (m.HasProperty("_BandBias") ? F(m.GetFloat("_BandBias")) : "?")
                  + "  _Bands=" + (m.HasProperty("_Bands") ? F(m.GetFloat("_Bands")) : "?")
                  + (needOutline
                     ? ("  _OutlineW=" + (explicitOutline ? F(m.GetFloat("_OutlineWidth")) : "**未显式设置**"))
                     : "")
                  + (needChroma
                     ? ("  _ChromaKeep=" + (explicitChroma ? F(m.GetFloat("_ChromaKeep")) : "**未显式设置**"))
                     : ""));
}
sb.AppendLine();
// 角色墨线的屏幕像素换算：世界米 × 362 ≈ px（`h_oline` 实测，含 ~1.6 px 地板）
{
    var cm = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Character_Ink.mat");
    if (cm != null && cm.HasProperty("_OutlineWidth"))
    {
        float w = cm.GetFloat("_OutlineWidth");
        sb.AppendLine("  【角色墨线换算】_OutlineWidth=" + F(w) + " m × ≈300 + 1.6 ≈ "
                      + F(w * 300f + 1.6f) + " px　(目标 3.5~6；标定见 Tools/reports/h_oline.txt)");
        sb.AppendLine();
    }
}

// ---- ⑤ 光照与调色板一致性 ----
sb.AppendLine("---- ⑤ 光照 / 调色板 ----");
var lt = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
if (lt == null) sb.AppendLine("  Assembly-CSharp 未加载");
else
{
    sb.AppendLine("  InkLighting 类型 → " + (lt.GetType("InkWash.Rendering.InkLighting") != null ? "OK" : "**缺失**"));
}
sb.AppendLine("  " + InkWash.Rendering.InkLighting.Describe());
sb.AppendLine();

// ---- ⑥ 场景里的白盒是否真的挂上了这些材质 ----
sb.AppendLine("---- ⑥ 场景白盒材质引用 ----");
var counts = new System.Collections.Generic.Dictionary<string, int>();
foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
{
    var s = r.sharedMaterial;
    if (s == null) continue;
    if (!counts.ContainsKey(s.name)) counts[s.name] = 0;
    counts[s.name]++;
}
foreach (var kv in counts.OrderByDescending(k => k.Value))
    sb.AppendLine("  " + kv.Key.PadRight(28) + " x" + kv.Value);
sb.AppendLine();

string verdict = (EditorUtility.scriptCompilationFailed == false && dllTime >= newest
                  && shaderErr == 0 && matBad == 0) ? "**全部通过**" : "**有未通过项**";
sb.AppendLine("===== 判定：" + verdict + " =====");

Debug.Log(sb.ToString());
return sb.ToString();

static float Luma(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }
static string F(float v) { return v.ToString("0.###"); }

/// .mat 里是否**显式**写了某个浮点属性（Material.HasProperty 只查 shader 声明，查不出这个）
static bool HasExplicit(string matName, string prop)
{
    string p = "Assets/_Project/Art/Materials/" + matName + ".mat";
    if (!File.Exists(p)) return false;
    foreach (var l in File.ReadAllLines(p))
        if (l.TrimStart().StartsWith("- " + prop + ":")) return true;
    return false;
}
