// 把 Player.prefab 的 SwordVfx 接到正式武器上：weaponPrefab = W_Sword.prefab，placeholderBlade = false。
//
// 这是"换成正式武器"的**唯一开关**：改的是预制体上的两个序列化字段。
// ⚠ 序列化字段必须在**非 Play** 状态下改 —— Play 期改了会在退出 Play 时被静默回滚。
using System.Collections;
using UnityEngine;
using UnityEditor;
using InkWash.Effects;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(
        System.IO.Path.Combine(projRoot, "Tools/reports/w_wire.txt"), sb.ToString());

    if (EditorApplication.isPlaying)
    {
        sb.AppendLine("[ERR] 编辑器在 Play 模式，改了会被回滚。先 stop。");
        flush(); yield break;
    }

    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";
    const string WEAPON = "Assets/_Project/Prefabs/Weapons/W_Sword.prefab";

    var weaponAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WEAPON);
    if (weaponAsset == null) { sb.AppendLine("[ERR] 加载不到 " + WEAPON); flush(); yield break; }

    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    if (contents == null) { sb.AppendLine("[ERR] 打不开 " + PREFAB); flush(); yield break; }

    var vfx = contents.GetComponentInChildren<SwordVfx>(true);
    if (vfx == null)
    {
        sb.AppendLine("[ERR] Player.prefab 里找不到 SwordVfx");
        PrefabUtility.UnloadPrefabContents(contents); flush(); yield break;
    }

    sb.AppendLine("========== 接线前 ==========");
    sb.AppendLine("  weaponPrefab      = " + (vfx.weaponPrefab != null ? vfx.weaponPrefab.name : "NULL"));
    sb.AppendLine("  placeholderBlade  = " + vfx.placeholderBlade);
    sb.AppendLine("  bladeAnchor 解析  = " + (vfx.bladeAnchor != null ? vfx.bladeAnchor.name : "(留空，运行时自动找右手骨)"));
    sb.AppendLine();

    vfx.weaponPrefab = weaponAsset;
    vfx.placeholderBlade = false;

    PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
    PrefabUtility.UnloadPrefabContents(contents);

    // 复核：重新读盘（不是在内存里改完就算）
    var re = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
    var v2 = re != null ? re.GetComponentInChildren<SwordVfx>(true) : null;

    sb.AppendLine("========== 接线后（重新读盘复核）==========");
    if (v2 == null) sb.AppendLine("[ERR] 复核读不到 SwordVfx");
    else
    {
        sb.AppendLine("  weaponPrefab      = " + (v2.weaponPrefab != null ? v2.weaponPrefab.name : "NULL"));
        sb.AppendLine("  weaponPrefab 路径 = " + (v2.weaponPrefab != null ? AssetDatabase.GetAssetPath(v2.weaponPrefab) : "-"));
        sb.AppendLine("  placeholderBlade  = " + v2.placeholderBlade);
        sb.AppendLine("  其余刀光参数（应保持上一轮验证过的值）：");
        sb.AppendLine(string.Format("    bladeLocalOffset = {0}", v2.bladeLocalOffset.ToString("F6")));
        sb.AppendLine(string.Format("    bladeLength      = {0:F6}  bladeWidth = {1:F6}  （占位剑用，本次失效）", v2.bladeLength, v2.bladeWidth));
        sb.AppendLine(string.Format("    trailStartWidth  = {0:F6}  trailEndWidth = {1:F6}", v2.trailStartWidth, v2.trailEndWidth));
        sb.AppendLine(string.Format("    trailMinSpeed    = {0:F2}  trailHoldMinSpeed = {1:F2}", v2.trailMinSpeed, v2.trailHoldMinSpeed));
        sb.AppendLine(string.Format("    arcRadius={0:F2} arcThickness={1:F2} arcSweepDeg={2:F0}", v2.arcRadius, v2.arcThickness, v2.arcSweepDeg));
    }

    flush();
}

return Body();
