// d_look.cs —— 画面质感对比图（运行时，不录屏，抓 4 张 PNG）
//
// 目的：用户反馈"发灰 / 像积木 / 没有挥墨感"，改完之后**要能一眼对比**，不能靠嘴说。
// 抓图用 ScreenCapture.CaptureScreenshot —— 它抓帧末最终后台缓冲，
// 后处理（墨线 / 宣纸 / 墨晕）与 IMGUI 都在里面（Camera.Render 那条路拍不到 OnGUI）。
//
// 计时一律用 Time.unscaledTime：抓图本身会压帧率，用 scaled 时间会被拖长。
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
    string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/look"));
    Directory.CreateDirectory(dir);

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[d_look] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var run = Object.FindObjectOfType<RunManager>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();
    var cam = Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();

    // 参数面板挡住左半边画面，对比图里必须关掉（它是开发期工具，不属于成品画面）
    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();

    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 0.35f; }
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.SetSeed(20260916); run.nextRoomDelay = 999f; }
    if (spawner != null && spawner.waves != null)
        for (int i = 0; i < spawner.waves.Length; i++)
            if (spawner.waves[i] != null) spawner.waves[i].delayBefore = 0.2f;

    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);

    if (run != null) run.StartRun();

    // 等怪刷出来并靠过来 —— 空场地看不出"墨线把角色从背景里切出来"这件事
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 4.0f) yield return null; }

    // ---------------- ① 战斗全景（静止，好读底色） ----------------
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "look_1_idle.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.7f) yield return null; }

    // ---------------- ② 跑动中（默认档 = 跑，Shift = 慢走） ----------------
    ctl.SetInjectedMove(new Vector2(0.55f, 0.85f), true);
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.1f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "look_2_run.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.7f) yield return null; }

    // ---------------- ③ 攻击瞬间（弧光张到最大的那几帧） ----------------
    ctl.SetInjectedMove(Vector2.zero, true);
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.3f) yield return null; }

    ctl.RequestInjectedAttack();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.13f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "look_3_slash.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.5f) yield return null; }

    // ---------------- ④ 命中（顿帧 + 溅墨 + 震屏） ----------------
    // 挑起后两段连击，命中时刻在 0.19s / 0.48s 附近
    ctl.RequestInjectedAttack();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.55f) yield return null; }
    ctl.RequestInjectedAttack();
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.22f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "look_4_hit.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.6f) yield return null; }

    // ---------------- ⑤ 慢走档（Shift） ----------------
    ctl.ResetToLocomotion();
    ctl.SetInjectedMove(new Vector2(0f, 1f), false);
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 1.2f) yield return null; }
    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "look_5_walk.png"));
    { float t = Time.unscaledTime; while (Time.unscaledTime - t < 0.7f) yield return null; }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    int enemies = Object.FindObjectsOfType<EnemyBase>().Length;
    Debug.Log("[d_look] done　敌人 " + enemies + "　相机震屏中=" + (cam != null && cam.IsShaking)
              + "　目录 " + dir);
    yield return null;
}
return Body();
