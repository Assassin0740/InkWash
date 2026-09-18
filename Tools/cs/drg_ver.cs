// drg_ver.cs —— 判定「改过的 .cs 默认值到底进内存没有」。
//
// 上一轮（09-18）的终点结论是：**DLL 文件是新的，但 Play 里读到的仍是旧默认值**
// （`diveSpiralAmp` 源码 3.0 → 运行时 2.2），`RequestScriptReload` 带不进去，需重启 Unity。
//
// 本探针在**编辑模式**下直接问「当前进程里加载的 Assembly-CSharp」：
//   ① 程序集从哪来（路径 + 文件 mtime + 文件字节数）
//   ② DLL **文件内容**里有没有新符号（按符号名 grep，mtime 会撒谎）
//   ③ **实例化一个 EnemyDragon** 后回读字段值 —— 这才是 Play 真正会用的数
// 三者只要 ③ 是新值，就说明重启已经生效，不必再折腾。
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

var sb = new StringBuilder();

// ---------- ① 找到加载中的 Assembly-CSharp ----------
Assembly asm = null;
Type t = null;
foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
{
    var tt = a.GetType("InkWash.Enemies.EnemyDragon");
    if (tt != null) { asm = a; t = tt; break; }
}

if (t == null)
{
    Debug.Log("[drg_ver] 没找到 InkWash.Enemies.EnemyDragon");
    return "[drg_ver] FAIL: type not found";
}

sb.AppendLine("程序集 = " + asm.GetName().Name + "  ver=" + asm.GetName().Version);
string loc = null;
try { loc = asm.Location; } catch { }
sb.AppendLine("Location = " + (loc ?? "(dynamic)"));

if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
{
    var fi = new FileInfo(loc);
    sb.AppendLine("DLL 文件 mtime = " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "  字节 = " + fi.Length);

    // ---------- ② DLL 文件内容判据（mtime 会撒谎） ----------
    string txt = File.ReadAllText(loc);
    foreach (var sym in new string[] { "diveSpiralAmp", "diveSpiralVertAmp", "diveSwimScale",
                                       "ApplySwimWave", "AddSpinePitch", "waveAmpHeadGain" })
    {
        int c = 0, i = 0;
        while ((i = txt.IndexOf(sym, i, StringComparison.Ordinal)) >= 0) { c++; i += sym.Length; }
        sb.AppendLine("  DLL 文件里 " + sym.PadRight(20) + " 命中 " + c + " 次");
    }
}
else
{
    sb.AppendLine("DLL 文件不可读 —— 只能靠 ③ 判断");
}

// ---------- ③ 实例化后回读字段（Play 真正用的数） ----------
var go = new GameObject("_drg_ver");
var comp = go.AddComponent(t) as MonoBehaviour;
if (comp == null)
{
    UnityEngine.Object.DestroyImmediate(go);
    Debug.Log("[drg_ver] AddComponent 失败");
    return sb.ToString() + "\n[drg_ver] FAIL: AddComponent";
}

sb.AppendLine("");
sb.AppendLine("=== 实例化后回读的默认值（★ 权威） ===");
foreach (var name in new string[] {
    "diveSpiralAmp", "diveSpiralTurns", "diveSpiralVertAmp", "diveSwimScale",
    "hoverAmplitudeDeg", "hoverPitchAmplitudeDeg", "hoverFrequency", "hoverPhaseStepDeg",
    "restMotionScale", "restLift", "restSpeedScale", "waveAmpHeadGain",
    "bodySwayAmp", "breathProjectileSpeed", "orbitLateralAmp" })
{
    var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
    sb.AppendLine("  " + name.PadRight(24) + " = " + (f == null ? "(字段不存在)" : f.GetValue(comp).ToString()));
}

// 有没有残留的旧方法（应该已经删掉了）
var m = t.GetMethod("AddSpinePitch", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
sb.AppendLine("  AddSpinePitch 方法            = " + (m == null ? "已删除 ✅" : "**仍存在** ❌"));

UnityEngine.Object.DestroyImmediate(go);

// ---------- 判定 ----------
sb.AppendLine("");
sb.AppendLine(">>> 期望：diveSpiralAmp = 3（不是 2.2）、AddSpinePitch 已删除。");

Debug.Log("[drg_ver]\n" + sb.ToString());
return sb.ToString();
