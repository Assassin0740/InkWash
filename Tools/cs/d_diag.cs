// d_diag.cs —— 画面问题定位探针（运行时）
//
// 两个待查的现象：
//   ① 攻击时**看不到刀光**（用户："没有挥墨的那种效果感觉"）
//      → 打印 SwordVfx 的全部只读探针，并在挥砍后 0.12s 找一次场景里的 "SlashArc" 实例，
//        把它的世界坐标、相机坐标、包围盒、材质 alpha 全打出来。
//        "弧光到底有没有生成"和"生成了但看不见"是两件完全不同的事，
//        不区分清楚就会去改错的参数。
//   ② 地面上有贯穿的直线
//      → 逐层关掉（墨线 / 宣纸 / 墨晕）各抓一张，看线是谁画的。
//        全是三层之一造成的，就不用去动场景几何。
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Effects;
using InkWash.Player;
using InkWash.Rendering;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/diag"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[d_diag] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var vfx = go.GetComponentInChildren<SwordVfx>(true);
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var cam = Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
    var camera = cam != null ? cam.GetComponent<Camera>() : Camera.main;

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();

    sb.AppendLine("===== 画面诊断 =====");
    sb.AppendLine("SwordVfx 找到 = " + (vfx != null));
    if (vfx != null)
    {
        sb.AppendLine("  武器        = " + vfx.WeaponName);
        sb.AppendLine("  右手骨骼    = " + vfx.HasBladeAnchor);
        sb.AppendLine("  inkShader   = " + (vfx.inkShader != null ? vfx.inkShader.name : "(空 → Shader.Find)"));
    }
    sb.AppendLine("相机        = " + (camera != null ? camera.name : "** 找不到"));
    sb.AppendLine("三层状态    = " + InkStyleRegistry.Describe());
    sb.AppendLine("Edge/Paper/Bloom 对象 = " + (InkStyleRegistry.Edge != null) + "/"
                  + (InkStyleRegistry.Paper != null) + "/" + (InkStyleRegistry.Bloom != null));

    // 顺带把已经不在用的 Shader.Find 路径也验证一遍
    var slashShader = Shader.Find("InkWash/InkSlash");
    sb.AppendLine("Shader.Find(\"InkWash/InkSlash\") = " + (slashShader != null ? slashShader.name : "** 找不到"));
    if (slashShader != null)
        sb.AppendLine("  isSupported = " + slashShader.isSupported
                      + "  passCount = " + slashShader.passCount);

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 0.6f; }
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, true);
    if (run != null) run.StartRun();

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 2.5f) yield return null; }

    // ---------------- 第 1 段：挥砍，逐帧追弧光 ----------------
    sb.AppendLine();
    sb.AppendLine("---- 挥砍实测 ----");
    int arcBefore = SwordVfx.ActiveArcCount;
    int spawnBefore = vfx != null ? vfx.ArcSpawnCount : -1;
    int swingBefore = vfx != null ? vfx.SwingCount : -1;

    ctl.RequestInjectedAttack();

    GameObject arcSeen = null;
    Vector3 arcPos = Vector3.zero;
    float arcSeenAt = -1f;
    float t0 = Time.unscaledTime;
    bool shot = false;
    while (Time.unscaledTime - t0 < 0.9f)
    {
        if (arcSeen == null)
        {
            var found = GameObject.Find("SlashArc");
            if (found != null) { arcSeen = found; arcPos = found.transform.position; arcSeenAt = Time.unscaledTime - t0; }
        }
        if (!shot && Time.unscaledTime - t0 > 0.14f)
        {
            shot = true;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "diag_slash_all.png"));
        }
        yield return null;
    }

    sb.AppendLine("  SwingCount    " + swingBefore + " → " + (vfx != null ? vfx.SwingCount : -1));
    sb.AppendLine("  ArcSpawnCount " + spawnBefore + " → " + (vfx != null ? vfx.ArcSpawnCount : -1));
    sb.AppendLine("  ActiveArcCount " + arcBefore + " → " + SwordVfx.ActiveArcCount);
    if (vfx != null)
    {
        sb.AppendLine("  TrailEmitStartCount = " + vfx.TrailEmitStartCount
                      + "   IsTrailEmitting = " + vfx.IsTrailEmitting
                      + "   TrailPositionCount = " + vfx.TrailPositionCount);
        sb.AppendLine("  BladeSpeed = " + vfx.BladeSpeed.ToString("0.###")
                      + "   EmitStartBladeSpeed = " + vfx.EmitStartBladeSpeed.ToString("0.###"));
    }
    sb.AppendLine("  找到 SlashArc 实例 = " + (arcSeen != null));
    if (arcSeen != null)
    {
        sb.AppendLine("    出现于挥砍后 " + arcSeenAt.ToString("0.###") + " s");
        sb.AppendLine("    世界坐标 " + V3(arcPos));
        sb.AppendLine("    相机坐标 " + (camera != null ? V3(camera.transform.position) : "(无)"));
        sb.AppendLine("    与相机距离 " + (camera != null ? Vector3.Distance(camera.transform.position, arcPos).ToString("0.###") : "?"));
        sb.AppendLine("    localScale " + V3(arcSeen.transform.localScale) + "   active=" + arcSeen.activeInHierarchy);
        var mr = arcSeen.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            sb.AppendLine("    包围盒 center=" + V3(mr.bounds.center) + " size=" + V3(mr.bounds.size));
            sb.AppendLine("    可见=" + mr.isVisible + "  材质=" + (mr.material != null ? mr.material.shader.name : "(无)"));
            if (mr.material != null)
            {
                var m = mr.material;
                sb.AppendLine("    _BaseColor=" + (m.HasProperty("_BaseColor") ? C(m.GetColor("_BaseColor")) : "(无)")
                              + "  _FlyingWhite=" + (m.HasProperty("_FlyingWhite") ? m.GetFloat("_FlyingWhite").ToString("0.###") : "(无)")
                              + "  _Intensity=" + (m.HasProperty("_Intensity") ? m.GetFloat("_Intensity").ToString("0.###") : "(无)"));
            }
            // 判断是否落在视锥里：把包围盒中心投影到屏幕
            if (camera != null)
            {
                Vector3 sp = camera.WorldToScreenPoint(mr.bounds.center);
                sb.AppendLine("    屏幕坐标 " + V3(sp) + "（z>0 才在相机前方）");
            }
        }
    }

    // ---------------- 第 2 段：逐层关闭抓图，定位地面直线 ----------------
    sb.AppendLine();
    sb.AppendLine("---- 逐层关闭 ----");
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.4f) yield return null; }

    InkStyleRegistry.EdgeOn = false;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.4f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "diag_no_edge.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }

    InkStyleRegistry.EdgeOn = true;
    InkStyleRegistry.PaperOn = false;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.4f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "diag_no_paper.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }

    InkStyleRegistry.PaperOn = true;
    InkStyleRegistry.BloomOn = false;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.4f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "diag_no_bloom.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }

    InkStyleRegistry.BloomOn = true;
    InkStyleRegistry.EdgeOn = null;
    InkStyleRegistry.PaperOn = null;
    InkStyleRegistry.BloomOn = null;
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.3f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "diag_all_on.png"));

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/d_diag.txt"), sb.ToString());
    Debug.Log("[d_diag] done");
    yield return null;
}

static string V3(Vector3 v) { return "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")"; }
static string C(Color c) { return "(" + c.r.ToString("0.###") + ", " + c.g.ToString("0.###") + ", " + c.b.ToString("0.###") + ", " + c.a.ToString("0.###") + ")"; }

return Body();
