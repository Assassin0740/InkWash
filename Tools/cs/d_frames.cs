// d_frames.cs —— 攻击过程的逐帧连拍（运行时）
//
// 视频没法在这里逐帧"看"，所以直接把挥砍拆成 12 张定时快照。
// 这样能区分三件事：
//   · 光**根本没出**（每一张都干净）
//   · 光出了但**形状不对**（像是自交的一团）
//   · 光出了但**存在太短 / 太淡**（只有一两张能看到）
//
// ScreenCapture.CaptureScreenshot 是帧末写入，两次调用之间必须隔帧，否则会互相覆盖。
using System.Collections;
using System.IO;
using UnityEngine;
using InkWash.Effects;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/fx"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[d_frames] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var vfx = go.GetComponentInChildren<SwordVfx>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 999f; }
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, true);
    if (run != null) run.StartRun();

    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.6f) yield return null; }

    // ---- 第一段：只出一刀，逐帧看 ----
    ctl.RequestInjectedAttack();
    int emitOnArcs = 0, emitOnTrail = 0;
    var log = new System.Text.StringBuilder();
    for (int i = 0; i < 14; i++)
    {
        // 隔一帧抓一次（CaptureScreenshot 帧末写入）
        yield return null;
        yield return null;

        ScreenCapture.CaptureScreenshot(Path.Combine(dir, "fx_" + i.ToString("00") + ".png"));
        if (vfx != null)
        {
            if (SwordVfx.ActiveArcCount > 0) emitOnArcs++;
            if (vfx.IsTrailEmitting) emitOnTrail++;
            log.AppendLine("  帧" + i.ToString("00")
                           + "  Arc=" + SwordVfx.ActiveArcCount
                           + "  TrailEmit=" + vfx.IsTrailEmitting
                           + "  TrailVerts=" + vfx.TrailPositionCount
                           + "  BladeSpeed=" + vfx.BladeSpeed.ToString("0.##"));
        }
    }

    log.AppendLine("有弧光的帧数 = " + emitOnArcs + " / 14");
    log.AppendLine("拖尾发射中的帧数 = " + emitOnTrail + " / 14");
    File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/reports/d_frames.txt"), log.ToString());

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    Debug.Log("[d_frames] done　" + log.ToString().Replace("\n", " | "));
    yield return null;
}
return Body();
