// s6_land_slash.cs —— 把刀光新参数落到**预制体**上（编辑态，幂等）
//
// ★ 为什么必须有这一步（踩过）：
//   改 `SwordVfx.cs` 里的字段**默认值**，对场景/预制体上**已经序列化过**的组件毫无影响 ——
//   默认值只在"组件第一次被添加"时生效。上一版 s6_sky.cs 已经把
//   拖尾宽度 0.52 / 阈值 2.6（那一轮的值）写死在 Player.prefab 上，
//   所以这次改脚本默认值跑出来一切照旧，逐帧数据一点没变。
//   规矩：**改组件参数 = 必须同步写一个落地脚本改预制体**，不能只改脚本默认值。
//
// 本轮落地的两组：
//   ① 弧光：更细、更小、更短命、少滚转（原来是一块半米宽的硬边色块）
//   ② 拖尾：宽度按容器缩放折算 + 门控只挡"完全静止"（详见 SwordVfx 里的实测注释）
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using InkWash.Effects;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    const string prefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
    sb.AppendLine("===== 刀光参数落地 =====");
    sb.AppendLine("预制体 = " + prefabPath);
    sb.AppendLine("InkWash/InkSlash 可用 = " + (Shader.Find("InkWash/InkSlash") != null)
                  + "   （曾长期缺失，刀光退回不支持顶点色的 URP/Unlit）");
    sb.AppendLine();

    var root = PrefabUtility.LoadPrefabContents(prefabPath);
    if (root == null) { sb.AppendLine("** 打不开 " + prefabPath); yield break; }

    var vfx = root.GetComponentInChildren<SwordVfx>(true);
    if (vfx == null)
    {
        sb.AppendLine("** 预制体里找不到 SwordVfx");
        PrefabUtility.UnloadPrefabContents(root);
        yield break;
    }

    sb.AppendLine("before:");
    Dump(sb, vfx);

    // ---- ① 弧光：细、小、短命、少滚转 ----
    vfx.enableArc = true;
    vfx.arcRadius = 1.30f;
    vfx.arcThickness = 0.46f;
    vfx.arcSweepDeg = 128f;
    vfx.arcLifetime = 0.24f;
    vfx.arcHeight = 1.25f;
    vfx.arcTiltDeg = -10f;

    // ---- ② 拖尾：宽度按容器缩放折算（0.118 × 2.213 = 世界 0.261 m）----
    vfx.trailStartWidth = 0.118f;
    vfx.trailEndWidth = 0.015f;
    vfx.trailTime = 0.34f;
    vfx.trailMinSpeed = 0.15f;      // 只挡"完全静止"
    vfx.trailHoldMinSpeed = 0.05f;
    vfx.trailStartDelay = 0.04f;

    // 锚点必须贴剑轴（手骨局部 +Z），偏离轴心会让轨迹自交成一团
    vfx.bladeLocalOffset = new Vector3(-0.0055f, 0.0623f, 0.2998f);

    // 墨色略深一点、飞白别再吃 alpha（0.30 时整笔被干笔噪声拉薄，发灰）
    vfx.inkColor = new Color(0.045f, 0.050f, 0.068f, 0.96f);
    vfx.flyingWhite = 0.22f;

    EditorUtility.SetDirty(vfx);
    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

    sb.AppendLine();
    sb.AppendLine("after:");
    Dump(sb, vfx);

    PrefabUtility.UnloadPrefabContents(root);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    // ---- 复核：重新读一遍盘，确认真的写进去了（不靠内存里的对象自证）----
    sb.AppendLine();
    var check = PrefabUtility.LoadPrefabContents(prefabPath);
    var cv = check != null ? check.GetComponentInChildren<SwordVfx>(true) : null;
    sb.AppendLine("复核（重新读盘）：" + (cv == null ? "** 读不到" : ""));
    if (cv != null)
    {
        Dump(sb, cv);
        sb.AppendLine("  宽度世界值 = " + (cv.trailStartWidth * 2.213f).ToString("0.###") + " m"
                      + "（容器 Visual.localScale = 2.213）");
        PrefabUtility.UnloadPrefabContents(check);
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s6_land_slash.txt"), sb.ToString());
    Debug.Log("[s6_land_slash] done");
    yield return null;
}

void Dump(StringBuilder sb, SwordVfx v)
{
    sb.AppendLine("  弧  r=" + F(v.arcRadius) + " 厚=" + F(v.arcThickness) + " 角=" + F(v.arcSweepDeg)
                  + " 命=" + F(v.arcLifetime) + " 高=" + F(v.arcHeight) + " 滚转=" + F(v.arcTiltDeg)
                  + " 启用=" + v.enableArc);
    sb.AppendLine("  拖尾 时=" + F(v.trailTime) + " 宽=" + F(v.trailStartWidth) + " 末端宽=" + F(v.trailEndWidth)
                  + " 进入阈=" + F(v.trailMinSpeed) + " 维持阈=" + F(v.trailHoldMinSpeed)
                  + " 起手静默=" + F(v.trailStartDelay));
    sb.AppendLine("  锚点=" + V(v.bladeLocalOffset) + "  墨色=" + v.inkColor + "  飞白=" + F(v.flyingWhite));
}

static string F(float f) { return f.ToString("0.###"); }
static string V(Vector3 v) { return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")"; }

return Body();
