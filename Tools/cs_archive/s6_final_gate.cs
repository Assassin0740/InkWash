// s6_final_gate.cs —— 提交前的最后一道闸门。顶层语句风格（用 return 交出结果）。
//  ① 编译状态：`scriptCompilationFailed` 必须为 False；
//  ② DLL 新鲜度：Assembly-CSharp.dll 的写入时间必须**不早于**最新 .cs 源 ——
//     这是本项目反复踩过的坑（DLL 比源旧 ⇒ 新类型不可见，却"看起来编译成功"）；
//  ③ 关键类型/方法可见性：本轮的修复点必须真的在运行的程序集里；
//  ④ Shader 全表：确认没有哪个 shader 编译失败（失败时会拿到 null / pass=0）。
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;

var sb = new StringBuilder();
sb.AppendLine("===== 提交闸门（Sprint 6） =====");
sb.AppendLine();

// ---- ① 编译状态 ----
sb.AppendLine("---- ① 编译状态 ----");
sb.AppendLine("  isCompiling              = " + EditorApplication.isCompiling);
sb.AppendLine("  scriptCompilationFailed  = " + EditorUtility.scriptCompilationFailed);
sb.AppendLine("  isUpdating               = " + EditorApplication.isUpdating);
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
sb.AppendLine("  最新源  = " + newest.ToString("HH:mm:ss") + "  " + newestName);
sb.AppendLine("  判定    = " + (dllTime >= newest ? "**新鲜**" : "**过期（DLL 比源旧 ⇒ 类型可见性不可信）**"));
sb.AppendLine();

// ---- ③ 关键类型可见性 ----
sb.AppendLine("---- ③ 本轮修复点的类型可见性 ----");
var asm = AppDomain.CurrentDomain.GetAssemblies()
    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
if (asm == null) sb.AppendLine("  Assembly-CSharp = 未加载！");
else
{
    sb.AppendLine("  Assembly-CSharp = 已加载");
    foreach (var q in new[]
    {
        "InkWash.Effects.SwordVfx",
        "InkWash.CameraRig.ThirdPersonCamera",
        "InkWash.Player.PlayerController",
        "InkWash.Roguelike.RunManager",
        "InkWash.Rendering.InkStyleRegistry",
    })
    {
        sb.AppendLine("    " + (asm.GetType(q) != null ? "OK  " : "缺失 ") + q);
    }

    // ★ 教训：本轮探针第一版只查 Instance|Public，把两个**真存在**的方法报成「缺失」——
    //   `SwordVfx.BuildArcMesh/BuildBladeMesh` 是 `private static`，`RunManager.TakeOverTimeScale`
    //   是 `private`。**闸门报假阴性等于没有闸门**：查成员一律先用全标志位。
    const BindingFlags ALL = BindingFlags.Instance | BindingFlags.Static
                           | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    var sv = asm.GetType("InkWash.Effects.SwordVfx");
    if (sv != null)
    {
        foreach (var m in new[] { "BuildArcMesh", "BuildBladeMesh", "OnSwingStarted" })
            sb.AppendLine("    SwordVfx." + m + " → " + (sv.GetMethod(m, ALL) != null ? "OK" : "缺失"));
        sb.AppendLine("    SwordVfx._trail → " + (sv.GetField("_trail", ALL) != null ? "OK" : "缺失"));
    }
    var cam = asm.GetType("InkWash.CameraRig.ThirdPersonCamera");
    sb.AppendLine("    ThirdPersonCamera.StopShake() → "
        + (cam != null && cam.GetMethod("StopShake", ALL) != null ? "OK" : "缺失"));
    var rm = asm.GetType("InkWash.Roguelike.RunManager");
    sb.AppendLine("    RunManager.TakeOverTimeScale() → "
        + (rm != null && rm.GetMethod("TakeOverTimeScale", ALL) != null ? "OK" : "缺失"));
    var pc = asm.GetType("InkWash.Player.PlayerController");
    sb.AppendLine("    PlayerController._restartQueued → "
        + (pc != null && pc.GetField("_restartQueued", ALL) != null ? "OK" : "缺失"));
}
sb.AppendLine();

// ---- ④ Shader 全表 ----
sb.AppendLine("---- ④ Shader 表 ----");
foreach (var n in new[]
{
    "InkWash/InkCharacter", "InkWash/InkSurface", "InkWash/InkSky", "InkWash/InkSlash",
    "InkWash/InkSplash",
    "Hidden/InkWash/InkEdge", "Hidden/InkWash/InkPaper", "Hidden/InkWash/InkBloom",
})
{
    var sh = Shader.Find(n);
    sb.AppendLine("  " + n.PadRight(30) + " → " + (sh == null ? "**缺失**" : ("OK  pass=" + sh.passCount)));
}

Debug.Log(sb.ToString());
return sb.ToString();
