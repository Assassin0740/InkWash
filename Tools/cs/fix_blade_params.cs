// 修正换 Feng 时**漏折算**的刀身尺度参数。
//
// 背景：换模型时 Visual.localScale 从 1 变成 2.213，所有"模型容器空间"的尺寸参数
// 都必须同除 S 才能保持原来的**世界观感**。上一轮改了 bladeLength / bladeLocalOffset.y /
// trailStartWidth，但**漏了 bladeWidth 和 trailEndWidth**。
//
// 后果（实测）：bladeWidth 仍是 0.075（模型空间）→ 世界宽 0.075 × 2.213 = 0.166 m。
// 一把 1.05 m 长的剑宽 16.6 cm（比例 6:1）—— 真人剑约 20:1，正常游戏剑也 ≥ 12:1。
// 所以剑看起来是**一块平板/船桨**，不是细长的剑。这是"占位刀像平板"的主因之一。
// （另一半原因：BuildBladeMesh 从来没调 RecalculateNormals，网格无法线 → 光照失效。）
//
// 本脚本只做一件事：把这两个值按 1/S 写回预制体，并打印折算前后的世界值供核对。
// S 从预制体里的 Visual.localScale 实读，不写死常量。
using System.Collections;
using UnityEngine;
using UnityEditor;
using InkWash.Effects;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(
        System.IO.Path.Combine(projRoot, "Tools/reports/fix_blade_params.txt"), sb.ToString());

    if (EditorApplication.isPlaying)
    {
        sb.AppendLine("[ERR] 编辑器还在 Play 模式，改了会被退出 Play 时回滚。先 stop。");
        flush();
        yield break;
    }

    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";

    // 期望的**世界空间**观感（与 KayKit 时代一致：当时 Visual.localScale = 1，local 即 world）
    const float WANT_BLADE_LENGTH = 1.05f;   // 剑身长 1.05 m
    const float WANT_BLADE_WIDTH = 0.075f;   // 剑身宽 7.5 cm（比例 14:1，风格化尺度）
    const float WANT_TRAIL_START = 0.34f;    // 拖尾根宽 34 cm
    const float WANT_TRAIL_END = 0.02f;      // 拖尾梢宽 2 cm
    const float WANT_BLADE_OFFSET_Y = 0.62f; // 拖尾起点距手骨 62 cm

    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    if (contents == null) { sb.AppendLine("[ERR] 打不开 " + PREFAB); flush(); yield break; }

    var anim = contents.GetComponent<Animator>();
    Transform vis = anim != null ? anim.transform.Find("Visual") : contents.transform.Find("Visual");
    if (vis == null) { sb.AppendLine("[ERR] 找不到 Visual 节点"); PrefabUtility.UnloadPrefabContents(contents); flush(); yield break; }

    float S = vis.localScale.x;
    sb.AppendLine("预制体：" + PREFAB);
    sb.AppendLine(string.Format("Visual.localScale = {0}   → 折算因子 S = {0}", S));
    sb.AppendLine();

    var vfx = contents.GetComponentInChildren<SwordVfx>(true);
    if (vfx == null) { sb.AppendLine("[ERR] 找不到 SwordVfx"); PrefabUtility.UnloadPrefabContents(contents); flush(); yield break; }

    sb.AppendLine("=== 修改前（模型空间 → 世界 = 值 × S）===");
    sb.AppendLine(string.Format("  bladeLength      {0:F6} → 世界 {1:F4} m   （期望 {2:F4}）",
        vfx.bladeLength, vfx.bladeLength * S, WANT_BLADE_LENGTH));
    sb.AppendLine(string.Format("  bladeWidth       {0:F6} → 世界 {1:F4} m   （期望 {2:F4}）  ← 缺口",
        vfx.bladeWidth, vfx.bladeWidth * S, WANT_BLADE_WIDTH));
    sb.AppendLine(string.Format("  trailStartWidth  {0:F6} → 世界 {1:F4} m   （期望 {2:F4}）",
        vfx.trailStartWidth, vfx.trailStartWidth * S, WANT_TRAIL_START));
    sb.AppendLine(string.Format("  trailEndWidth    {0:F6} → 世界 {1:F4} m   （期望 {2:F4}）  ← 缺口",
        vfx.trailEndWidth, vfx.trailEndWidth * S, WANT_TRAIL_END));
    sb.AppendLine(string.Format("  bladeLocalOffset.y {0:F6} → 世界 {1:F4} m （期望 {2:F4}）",
        vfx.bladeLocalOffset.y, vfx.bladeLocalOffset.y * S, WANT_BLADE_OFFSET_Y));
    sb.AppendLine();

    // 全部按 1/S 重算（而不是只补那两个）——保证"世界值 = 期望值"，重复执行也幂等
    vfx.bladeLength = WANT_BLADE_LENGTH / S;
    vfx.bladeWidth = WANT_BLADE_WIDTH / S;
    vfx.trailStartWidth = WANT_TRAIL_START / S;
    vfx.trailEndWidth = WANT_TRAIL_END / S;
    vfx.bladeLocalOffset = new Vector3(vfx.bladeLocalOffset.x, WANT_BLADE_OFFSET_Y / S, vfx.bladeLocalOffset.z);

    PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
    PrefabUtility.UnloadPrefabContents(contents);

    // 复核：重新读盘，确认真的落盘了（不是只在内存里改了）
    var reread = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
    var v2 = reread != null ? reread.GetComponentInChildren<SwordVfx>(true) : null;
    var anim2 = reread != null ? reread.GetComponent<Animator>() : null;
    var vis2 = anim2 != null ? anim2.transform.Find("Visual") : null;
    float S2 = vis2 != null ? vis2.localScale.x : S;

    sb.AppendLine("=== 修改后（重新读盘复核）===");
    if (v2 == null)
    {
        sb.AppendLine("[ERR] 复核读不到 SwordVfx");
    }
    else
    {
        sb.AppendLine(string.Format("  S = {0}", S2));
        sb.AppendLine(string.Format("  bladeLength      {0:F6} → 世界 {1:F4} m", v2.bladeLength, v2.bladeLength * S2));
        sb.AppendLine(string.Format("  bladeWidth       {0:F6} → 世界 {1:F4} m", v2.bladeWidth, v2.bladeWidth * S2));
        sb.AppendLine(string.Format("  trailStartWidth  {0:F6} → 世界 {1:F4} m", v2.trailStartWidth, v2.trailStartWidth * S2));
        sb.AppendLine(string.Format("  trailEndWidth    {0:F6} → 世界 {1:F4} m", v2.trailEndWidth, v2.trailEndWidth * S2));
        sb.AppendLine(string.Format("  bladeLocalOffset.y {0:F6} → 世界 {1:F4} m", v2.bladeLocalOffset.y, v2.bladeLocalOffset.y * S2));
        sb.AppendLine();
        sb.AppendLine(string.Format("  剑身长宽比 = {0:F1} : 1 （1 m 剑宽 {1:F1} cm）",
            v2.bladeLength / v2.bladeWidth, v2.bladeWidth * S2 * 100f));
    }

    flush();
}

return Body();
