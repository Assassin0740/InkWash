// s6_verify_members.cs —— 编译新鲜度的**可信判据**
//
// 只看 DLL 文件时间戳会被误导（导入器可能不重写文件、时间戳也可能被外部工具动过）。
// 真正可信的是：**新加的成员在不在已加载的程序集里**。
//
// ★ 本脚本上一版自己踩了一个坑并已修正：Check 的最后一个参数是 isMethod，
//   上一版把 OnSwingStarted（其实是私有方法）当成字段查 → 报了"缺失"，
//   差点让我误判成"DLL 过期"。**探针写错会产出假证据**，比不写探针更危险。
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    yield return null;
    var asm = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
    sb.AppendLine("Assembly-CSharp = " + (asm != null ? "已加载" : "未找到"));

    // 方法
    Check(sb, asm, "InkWash.CameraRig.ThirdPersonCamera", "StopShake", true);
    Check(sb, asm, "InkWash.Roguelike.RunManager", "TakeOverTimeScale", true);
    Check(sb, asm, "InkWash.Effects.SwordVfx", "OnSwingStarted", true);
    Check(sb, asm, "InkWash.Player.PlayerController", "IsLeavingAttack", true);
    Check(sb, asm, "InkWash.Player.PlayerController", "IsAttackStateName", true);
    // 字段
    Check(sb, asm, "InkWash.Roguelike.RunManager", "cameraRig", false);
    Check(sb, asm, "InkWash.Effects.SwordVfx", "_trail", false);
    Check(sb, asm, "InkWash.Player.PlayerController", "_restartQueued", false);

    sb.AppendLine();
    sb.AppendLine(">> 全部为 OK ⇒ 本轮（含攻击连招修复）的代码已在运行的程序集里。");
    sb.AppendLine();

    // 顺带把 PlayerController 里与攻击时序相关的成员列出来，排查时不必反复回读源码
    sb.AppendLine("PlayerController 与攻击时序相关的成员：");
    var pc = asm != null ? asm.GetType("InkWash.Player.PlayerController") : null;
    if (pc != null)
    {
        foreach (var f in pc.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                           .OrderBy(f => f.Name))
            if (Hit(f.Name)) sb.AppendLine("  [字段] " + f.FieldType.Name + " " + f.Name);
        foreach (var m in pc.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                           .OrderBy(m => m.Name))
            if (Hit(m.Name)) sb.AppendLine("  [方法] " + m.Name);
    }

    System.IO.File.WriteAllText(System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Application.dataPath),
        "Tools/reports/s6_members.txt"), sb.ToString());
    Debug.Log("[s6_verify_members] done");
    yield return null;
}

static bool Hit(string n)
{
    foreach (var k in new[] { "atk", "attack", "phase", "swing", "queued", "leav", "comb", "restart" })
        if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
    return false;
}

void Check(StringBuilder sb, Assembly asm, string tn, string member, bool isMethod)
{
    if (asm == null) return;
    var t = asm.GetType(tn);
    if (t == null) { sb.AppendLine("  " + tn + "." + member + " → **类型缺失**"); return; }
    var flags = BindingFlags.Public | BindingFlags.NonPublic
              | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    bool ok = isMethod ? t.GetMethod(member, flags) != null : t.GetField(member, flags) != null;
    sb.AppendLine("  " + tn + "." + member + (isMethod ? "()" : "")
        + " → " + (ok ? "OK（新代码已在 DLL 里）" : "**缺失（DLL 还是旧的）**"));
}

return Body();
