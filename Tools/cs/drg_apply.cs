using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Text;

// drg_apply.cs —— 把「龙的运动核」新参数写进 Z_Enemy_MoLong.prefab
//
// 用户诉求（四轮问答定案）：
//   「龙的这个移动，不像是真的龙，很僵硬，没有按照 S 形去扭动，不像蛇」
//   → 1.5 个波 · 35°/节 · 慢而沉 · 头也摆但比尾小 · 绕圈 + 路径波浪化 · 高空巡游
//
// ■ 为什么必须同时改 prefab（本项目硬规矩 #6）
//   C# 字段初始值只在「prefab 里没有同名键」时生效。这些键 prefab 里都有 ⇒ 只改 C#
//   **完全不生效**（历史上栽过多次）。反过来，字段**换名**就等于让 C# 默认值生效
//   （waveAmpRootGain 当初就是这么修的）。
//
// ■ 为什么用 Instantiate + ApplyPrefabInstance，而不是 LoadPrefabContents
//   Z_Enemy_MoLong.prefab 里**嵌套**了 Z_Dragon（龙模型本体），
//   LoadPrefabContents 有把嵌套 prefab 洗成实体的风险（会把 stripped 占位洗掉、文件暴涨）。
//
// ■ 数值依据（详见 EnemyDragon.cs 的 Tooltip 与《美术风格规范》）
//   hoverAmplitudeDeg 16→35   链的侧向偏移 ≈ 振幅×波长/(2π)：16° ⇒ 仅 0.47 m ＝ 身长 5.7%（直棍）；
//                             35° ⇒ 约 1.4 m ＝ 身长 17%（真蛇 10~15%、游戏里的龙 ≈20%）
//   hoverPhaseStepDeg 15→22.5 15° ⇒ 24 节恰好一个整波 ⇒ 正负弯积分抵消、头尾回到中轴；
//                             22.5° ⇒ 1.5 个波 ⇒ 头尾反向、真 S 形
//   hoverPitchAmplitudeDeg 1.5→16  垂直/水平比 9% ⇒「会摆的扁片」；46% 才是三维游动
//   hoverFrequency 0.45→0.27  振幅拉到 35° 后 0.45 Hz 显急躁；0.27 ≈ 3.7 s 一周期，巨物感
//   waveAmpHeadGain 0.30→0.55 旧值头部只弯 4.8°（等于头不动）；用户要「头也摆但比尾小」
//   hoverHeightP1/2/3 3.6/4.4/5.2 → 7/8/9   龙身长 8.2 m，3.6 m 高掠地 ⇒ 俯冲落差仅 2 m
//   swimTurnRate 1.1→1.5      航向要跟得上新的蛇形摆动（否则路径仍接近圆）
//   pathWaveAmpDeg/WaveCount  新增：把蛇行从「进出分量」改到「航向」，才是 S 形轨迹
//   orbitLateralAmp → 0       旧蛇行分量废弃（留着会叠加成花瓣形轨迹）
//
// ★ 本脚本写磁盘；执行前确认 Unity 不在 Play。报告 → Tools/reports/drg_apply.txt

string PP = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
string RP = "Tools/reports/drg_apply.txt";

var sb = new StringBuilder();
sb.AppendLine("=== drg_apply：墨龙运动核参数落盘 ===");
sb.AppendLine("prefab = " + PP);
sb.AppendLine("isPlaying = " + EditorApplication.isPlaying);
sb.AppendLine();

if (EditorApplication.isPlaying)
{
    File.WriteAllText(RP, sb.ToString() + "x 处于 Play 模式，拒绝写盘。先 stop。\n");
    return "DRG_APPLY_FAIL playing";
}

var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PP);
if (asset == null)
{
    File.WriteAllText(RP, sb.ToString() + "x prefab 未找到\n");
    return "DRG_APPLY_FAIL noprefab";
}

Type dt = null;
foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
{
    var t = a.GetType("InkWash.Enemies.EnemyDragon");
    if (t != null) { dt = t; break; }
}
if (dt == null)
{
    File.WriteAllText(RP, sb.ToString() + "x 找不到 InkWash.Enemies.EnemyDragon 类型（编译没通过？）\n");
    return "DRG_APPLY_FAIL notype";
}

string[] NAMES = {
    "hoverHeight", "hoverAmplitudeDeg", "hoverFrequency", "hoverPhaseStepDeg",
    "hoverPitchAmplitudeDeg", "waveAmpRootGain", "waveAmpHeadGain",
    "bodySwayAmp", "bodySwayFreq", "limbSwingFreq", "swimTurnRate",
    "pathWaveAmpDeg", "pathWaveCount",
    "hoverHeightP1", "hoverHeightP2", "hoverHeightP3",
    "orbitLateralAmp"
};
float[] VALS = {
    7.0f, 35.0f, 0.27f, 22.5f,
    16.0f, 1.30f, 0.55f,
    1.0f, 0.27f, 0.27f, 1.5f,
    30.0f, 3.0f,
    7.0f, 8.0f, 9.0f,
    0.0f
};

// ── 1. 旧值（读 prefab 资产上的组件）──
var oldComp = asset.GetComponent(dt);
if (oldComp == null)
{
    File.WriteAllText(RP, sb.ToString() + "x prefab 上没有 EnemyDragon 组件\n");
    return "DRG_APPLY_FAIL nocomp";
}
sb.AppendLine("── 旧值 ──");
for (int i = 0; i < NAMES.Length; i++)
{
    var f = dt.GetField(NAMES[i], BindingFlags.Public | BindingFlags.Instance);
    sb.AppendLine("   " + NAMES[i].PadRight(24) + (f == null ? "<无此字段>" : f.GetValue(oldComp).ToString()));
}
sb.AppendLine();

// ── 2. 实例化 → 改 → 回写 prefab ──
var inst = PrefabUtility.InstantiatePrefab(asset) as GameObject;
if (inst == null)
{
    File.WriteAllText(RP, sb.ToString() + "x InstantiatePrefab 失败\n");
    return "DRG_APPLY_FAIL inst";
}
inst.name = "drg_apply_tmp";

var comp = inst.GetComponent(dt);
int setOk = 0;
var missing = new StringBuilder();
for (int i = 0; i < NAMES.Length; i++)
{
    var f = dt.GetField(NAMES[i], BindingFlags.Public | BindingFlags.Instance);
    if (f == null || f.FieldType != typeof(float)) { missing.Append(NAMES[i] + " "); continue; }
    f.SetValue(comp, VALS[i]);
    setOk++;
}

PrefabUtility.ApplyPrefabInstance(inst, InteractionMode.AutomatedAction);
UnityEngine.Object.DestroyImmediate(inst);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

sb.AppendLine("── 已写入 " + setOk + " 个字段" + (missing.Length > 0 ? "；跳过（无字段）: " + missing : "") + " ──");
sb.AppendLine();

// ── 3. 磁盘回读核对（★ 内存回读拿到的是同一个实例，永远显示成功；必须读文件文本）──
sb.AppendLine("── 磁盘回读（.prefab 文件文本）──");
var allKeys = new string[] {
    "hoverHeight", "hoverAmplitudeDeg", "hoverFrequency", "hoverPhaseStepDeg",
    "hoverPitchAmplitudeDeg", "waveAmpRootGain", "waveAmpHeadGain",
    "bodySwayAmp", "bodySwayFreq", "limbSwingDeg", "limbSwingFreq",
    "swimSpeed", "swimTurnRate", "swimRadiusGain",
    "pathWaveAmpDeg", "pathWaveCount", "orbitLateralAmp",
    "hoverHeightP1", "hoverHeightP2", "hoverHeightP3"
};
var lines = File.ReadAllLines(PP);
foreach (string k in allKeys)
{
    bool hit = false;
    foreach (var ln in lines)
    {
        string t = ln.Trim();
        if (t.StartsWith(k + ":"))
        {
            sb.AppendLine("   " + t);
            hit = true;
            break;
        }
    }
    if (!hit) sb.AppendLine("   " + k + ": <文件里没有这个键 ⇒ 用 C# 默认值>");
}

string s = sb.ToString();
File.WriteAllText(RP, s);
Debug.Log(s);
return "DRG_APPLY_OK set=" + setOk;
