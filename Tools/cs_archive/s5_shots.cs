// s5_shots.cs —— 论文用静态图：① 三选一升级面板 ② 通关结算界面
//
// 为什么这两张要单独拍：三选一面板与结算界面都是 **IMGUI 屏幕空间叠加**，
// 而 InkStylePanel 那套四阶段图走的是 Camera.Render —— 那条路拍不到 OnGUI。
// 这里用 ScreenCapture.CaptureScreenshot（在帧末抓最终后台缓冲，**包含 IMGUI**）。
//
// 注意：录制/截图会压帧率，所以脚本一律用 Time.unscaledTime 计时。
using System.Collections;
using System.IO;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/s5"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[s5_shots] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();
    var level = go.GetComponent<LevelSystem>();
    var inv = go.GetComponent<SkillInventory>();

    ph.maxHealth = 1000f; ph.ResetHealth();
    if (panel != null) { panel.ApplyStage(4); panel.visible = false; }
    if (choice != null) choice.Hide();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 0.05f; }
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260915); run.nextRoomDelay = 0.2f; }
    if (inv != null) inv.ResetAll();
    if (level != null) level.ResetAll();
    if (spawner != null && spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length; i++)
            if (spawner.waves[i] != null) spawner.waves[i].delayBefore = 0.1f;

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);

    if (run != null) run.StartRun();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.6f) yield return null; }

    // ① 三选一面板
    if (level != null && run != null) level.GrantXp(level.XpToNext);
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }
    bool showing = choice != null && choice.IsShowing;
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "M5_choice_panel.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }

    // ② 通关结算
    if (choice != null && choice.IsShowing) choice.InjectChoice(0);
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.3f) yield return null; }

    float tClear = Time.unscaledTime;
    float holdAt = -1f;
    int picks = 1;
    while (run != null && !run.IsRunOver && Time.unscaledTime - tClear < 12f)
    {
        if (run.State == RunState.Reward)
        {
            if (holdAt < 0f) holdAt = Time.unscaledTime;
            if (Time.unscaledTime - holdAt > 0.25f && choice != null && choice.IsShowing)
            {
                int n = choice.Options != null ? choice.Options.Count : 0;
                choice.InjectChoice(picks % Mathf.Max(1, n));
                picks++; holdAt = -1f;
            }
            yield return null;
            continue;
        }
        foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        {
            if (e == null || !e.IsAlive) continue;
            e.TakeDamage(new DamageInfo
            {
                amount = 100000f, sourceFaction = Faction.Player, hitDirection = Vector3.zero,
                knockback = 0f, hitStun = 0f, hitStop = 0f,
            });
        }
        yield return null;
    }
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.0f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "M5_victory.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    Debug.Log("[s5_shots] done　面板可见=" + showing + "　最终状态=" + (run != null ? run.State.ToString() : "?")
              + "　选择 " + picks + " 次");
    yield return null;
}
return Body();
