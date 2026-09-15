// d_edge.cs —— 墨线误报定位（运行时，对照实验）
//
// 现象：地面与墙面上出现贯穿的斜线，且每条都指向灭点 ——
//       说明它们对应世界空间里的某组平行线（大平面的三角形剖分边 / 墙角）。
//       关掉墨线后完全消失（见 diag_no_edge.png），所以是 InkEdge 的误报。
//
// 方法：把三项（深度断崖 / 法线折痕 / 天空剪影）分别置零，各抓一张。
//   "哪一项归零之后线消失"就是直接答案 —— 比继续推理快得多，
//   也比"把灵敏度整体调低碰运气"安全（整体调低会连带削掉真正想要的轮廓）。
using System.Collections;
using System.IO;
using UnityEngine;
using InkWash.Player;
using InkWash.Rendering;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/edge"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    var go = ph != null ? ph.gameObject : null;
    var ctl = go != null ? go.GetComponent<PlayerController>() : null;
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();

    var s = InkStyleRegistry.EnsureEdgeRuntime();
    if (s == null) { Debug.LogError("[d_edge] 拿不到 Edge Settings"); yield break; }

    sb.AppendLine("===== 墨线误报定位 =====");
    sb.AppendLine("基线参数：depthSens=" + s.depthSensitivity + "  depthBias=" + s.depthBias
                  + "  normalSens=" + s.normalSensitivity + "  normalBias=" + s.normalBias
                  + "  lineStrength=" + s.lineStrength + "  distanceFade=" + s.distanceFade);

    if (ctl != null)
    {
        ctl.ResetToLocomotion();
        ctl.BeginInputOverride();
        ctl.SetInjectedMove(Vector2.zero, true);
    }
    if (run != null) { run.ResetForTest(); run.SetSeed(7); }
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.2f) yield return null; }

    // ---- 基线 ----
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "edge_base.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }

    // ---- 只关深度断崖 ----
    float dSave = s.depthSensitivity;
    s.depthSensitivity = 0f;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.4f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "edge_no_depth.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }
    s.depthSensitivity = dSave;

    // ---- 只关法线折痕 ----
    float nSave = s.normalSensitivity;
    s.normalSensitivity = 0f;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.4f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "edge_no_normal.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }
    s.normalSensitivity = nSave;

    // ---- 只关远处淡出（看是不是"远处才有"） ----
    float fSave = s.distanceFade;
    s.distanceFade = 400f;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.4f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "edge_far_open.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }
    s.distanceFade = fSave;

    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, true); ctl.EndInputOverride(); }

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/d_edge.txt"), sb.ToString());
    Debug.Log("[d_edge] done");
    yield return null;
}
return Body();
