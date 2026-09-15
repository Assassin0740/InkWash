// d_video.cs —— 手感 / 视觉演示（运行时 + 录屏）
//
// 为什么必须录动态：攻击特效是"一段过程"，单帧抓图既看不到笔触的走势，
// 也分不清"没出光"和"出了但形状不对"。上一轮的单帧抓图就误导过一次 ——
// 那一团黑色只有连起来看才知道是拖尾自交，不是弧光。
//
// 计时全部走 Time.unscaledTime：边录边跑会掉到十几 fps，用 scaled 会被拖长。
using System.Collections;
using UnityEngine;
using InkWash.Effects;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[d_video] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var vfx = go.GetComponentInChildren<SwordVfx>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();

    if (panel != null) { panel.visible = false; panel.enabled = false; }
    if (choice != null) choice.Hide();

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 0.9f; }
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (spawner != null && spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length; i++)
            if (spawner.waves[i] != null) spawner.waves[i].delayBefore = 0.3f;

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, true);

    if (run != null) run.StartRun();

    // 等怪刷出来
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 3.0f) yield return null; }

    float t0 = Time.unscaledTime;
    int phase = 0;
    float phaseAt = t0;

    while (Time.unscaledTime - t0 < 14.0f)
    {
        float el = Time.unscaledTime - phaseAt;
        switch (phase)
        {
            case 0:   // 跑动（默认档）
                ctl.SetInjectedMove(new Vector2(0.30f, 0.95f), true);
                if (el > 2.2f) { phase = 1; phaseAt = Time.unscaledTime; }
                break;

            case 1:   // 站定
                ctl.SetInjectedMove(Vector2.zero, true);
                if (el > 0.35f) { phase = 2; phaseAt = Time.unscaledTime; }
                break;

            case 2:   // 三段连击
                ctl.RequestInjectedAttack();
                if (el > 0.30f) { phase = 3; phaseAt = Time.unscaledTime; }
                break;
            case 3:
                if (el > 0.42f) { ctl.RequestInjectedAttack(); phase = 4; phaseAt = Time.unscaledTime; }
                break;
            case 4:
                if (el > 0.72f) { ctl.RequestInjectedAttack(); phase = 5; phaseAt = Time.unscaledTime; }
                break;
            case 5:
                if (el > 1.30f) { phase = 6; phaseAt = Time.unscaledTime; }
                break;

            case 6:   // 慢走（Shift 档）
                ctl.SetInjectedMove(new Vector2(-0.9f, 0.4f), false);
                if (el > 1.8f) { phase = 0; phaseAt = Time.unscaledTime; }
                break;
        }
        yield return null;
    }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    if (vfx != null)
        Debug.Log("[d_video] done　挥砍 " + vfx.SwingCount + " 次　弧光 " + vfx.ArcSpawnCount
                  + " 次　拖尾点亮 " + vfx.TrailEmitStartCount + " 次");
    else
        Debug.Log("[d_video] done");
    yield return null;
}
return Body();
