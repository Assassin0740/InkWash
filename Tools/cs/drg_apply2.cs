using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Text;

// drg_apply2.cs —— 第二轮落盘：把「静默段」从「贴地趴卧 + 完全静止」改成「降低 + 放缓 + 仍蛇形」
//
// ■ 为什么改
//   `enablePerformanceCycle` 复现的是龙自带动画的结构（前进 7 s ↔ 静默 7 s）。
//   但静默段的三个参数是给**旧的低空掠飞**配的：
//     restLift      0.35 m  ⇒ 龙会一路降到几乎贴地（配合新的 7~9 m 巡航高度就是一次 6.7 m 猛坠）
//     restMotionScale 0.06  ⇒ 脊骨波压到 6%，实测弦垂距 **0.00 m** ⇒ 整条龙被拉成一根直棍
//     restSpeedScale  0.12  ⇒ 几乎不前进
//
//   用户的定案是「**时时刻刻**都要沿 S 飞行」+「高空巡游」⇒ 上面第一、二条直接冲突。
//   这里不删掉表演循环（那是有文档依据的特性），只把静默段改成
//   「飞得低一点、游得慢一点，但**身体照样是 S**」—— 保留节奏感，满足要件。
//
// ★ 写磁盘；执行前确认不在 Play。报告 → Tools/reports/drg_apply2.txt

string PP = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
string RP = "Tools/reports/drg_apply2.txt";

var sb = new StringBuilder();
sb.AppendLine("=== drg_apply2：静默段改造 ===");
sb.AppendLine("isPlaying = " + EditorApplication.isPlaying);
sb.AppendLine();

if (EditorApplication.isPlaying)
{
    File.WriteAllText(RP, sb.ToString() + "x 处于 Play，拒绝写盘\n");
    return "DRG_APPLY2_FAIL playing";
}

var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PP);
if (asset == null) { File.WriteAllText(RP, sb.ToString() + "x prefab 未找到\n"); return "DRG_APPLY2_FAIL noprefab"; }

Type dt = null;
foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
{
    var t = a.GetType("InkWash.Enemies.EnemyDragon");
    if (t != null) { dt = t; break; }
}
if (dt == null) { File.WriteAllText(RP, sb.ToString() + "x 找不到 EnemyDragon 类型\n"); return "DRG_APPLY2_FAIL notype"; }

string[] NAMES = { "restMotionScale", "restLift", "restSpeedScale" };
float[] VALS = { 0.50f, 4.5f, 0.45f };

var oldComp = asset.GetComponent(dt);
if (oldComp == null) { File.WriteAllText(RP, sb.ToString() + "x prefab 上没有 EnemyDragon\n"); return "DRG_APPLY2_FAIL nocomp"; }

sb.AppendLine("── 旧值 ──");
for (int i = 0; i < NAMES.Length; i++)
{
    var f = dt.GetField(NAMES[i], BindingFlags.Public | BindingFlags.Instance);
    sb.AppendLine("   " + NAMES[i].PadRight(18) + (f == null ? "<无>" : f.GetValue(oldComp).ToString()));
}
sb.AppendLine();

var inst = PrefabUtility.InstantiatePrefab(asset) as GameObject;
if (inst == null) { File.WriteAllText(RP, sb.ToString() + "x Instantiate 失败\n"); return "DRG_APPLY2_FAIL inst"; }
inst.name = "drg_apply2_tmp";

var comp = inst.GetComponent(dt);
int ok = 0;
for (int i = 0; i < NAMES.Length; i++)
{
    var f = dt.GetField(NAMES[i], BindingFlags.Public | BindingFlags.Instance);
    if (f == null || f.FieldType != typeof(float)) { sb.AppendLine("   ⚠ 跳过 " + NAMES[i]); continue; }
    f.SetValue(comp, VALS[i]);
    ok++;
}

PrefabUtility.ApplyPrefabInstance(inst, InteractionMode.AutomatedAction);
UnityEngine.Object.DestroyImmediate(inst);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

sb.AppendLine("已写入 " + ok + " 个字段");
sb.AppendLine();
sb.AppendLine("── 磁盘回读（.prefab 文本）──");
var lines = File.ReadAllLines(PP);
string[] keys = { "enablePerformanceCycle", "advanceDuration", "restDuration",
                  "restMotionScale", "restLift", "restSpeedScale" };
foreach (string k in keys)
{
    bool hit = false;
    foreach (var ln in lines)
    {
        string t = ln.Trim();
        if (t.StartsWith(k + ":")) { sb.AppendLine("   " + t); hit = true; break; }
    }
    if (!hit) sb.AppendLine("   " + k + ": <无此键 ⇒ C# 默认值>");
}

string s = sb.ToString();
File.WriteAllText(RP, s);
Debug.Log(s);
return "DRG_APPLY2_OK set=" + ok;
