// q_demo2.cs —— 本轮改动的自动演示（配合 exec_cs.py --record 录制）
//
// 覆盖用户六条诉求里"要看动态"的那几条：
//   ① 战斗姿态下的**跑步**（剑随右臂摆动 —— 上一轮之前右臂 9 条通道被烘成常数）
//   ② **冲刺**（新片段 Dash_Lunge，站立持剑前冲 + 贴着地面，距离 5.5m）
//   ③ 三段连击 + **K 重击**
//   ④ 地面材质统一后的观感（跑动时会扫过地面/高台的交界）
//
// 全程用**时间驱动**（`Time.time`）而不是帧计数：边录屏边跑时帧率会掉到 20~35fps，
// 按帧数等会把每一段拉长数倍（上一轮录屏正是因此撞墙）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[q_demo2] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var room = Object.FindObjectOfType<RoomController>();
    var run = Object.FindObjectOfType<RunManager>();

    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();
    ph.maxHealth = 100000f; ph.ResetHealth();
    if (room != null) room.ResetForTest();
    if (run != null) run.nextRoomDelay = 999f;

    // 摆到场地南侧空旷处，朝 +Z
    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(0f, 0.05f, -8f);
    go.transform.rotation = Quaternion.identity;
    if (cc != null) cc.enabled = true;
    ctl.ResetToLocomotion();

    // 战斗姿态（武器在右手）—— 这样才能看到"剑随右臂摆动"
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    yield return null;

    float t0 = Time.time;
    bool Until(float d) => Time.time - t0 < d;

    // ---- 0.0~1.0s 待机（战斗持剑待机）----
    while (Until(1.0f)) { ctl.SetInjectedMove(Vector2.zero, false); yield return null; }

    // ---- 1.0~2.6s 侧向跑步（看剑是否随右臂摆）----
    while (Until(2.6f)) { ctl.SetInjectedMove(new Vector2(1f, 0f), true); yield return null; }

    // ---- 2.6~3.4s 转向前进 + 冲刺 ----
    while (Until(3.0f)) { ctl.SetInjectedMove(new Vector2(0f, 1f), true); yield return null; }
    ctl.RequestInjectedDash();
    while (Until(4.0f)) { ctl.SetInjectedMove(new Vector2(0f, 1f), true); yield return null; }

    // ---- 4.0~4.6s 减速站定 ----
    while (Until(4.6f)) { ctl.SetInjectedMove(Vector2.zero, true); yield return null; }

    // ---- 4.6~7.6s 三段连击（每段按自己的时长留够时间）----
    ctl.RequestInjectedAttack();
    while (Until(5.5f)) { yield return null; }
    ctl.RequestInjectedAttack();
    while (Until(6.4f)) { yield return null; }
    ctl.RequestInjectedAttack();
    while (Until(7.8f)) { yield return null; }

    // ---- 7.8~9.6s 重击（K）----
    ctl.RequestInjectedHeavyAttack();
    while (Until(9.6f)) { yield return null; }

    // ---- 9.6~11.0s 收招站定 ----
    while (Until(11.0f)) { ctl.SetInjectedMove(Vector2.zero, true); yield return null; }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;

    Debug.Log("[q_demo2] 演示结束（约 11 s）");
    yield return null;
}

return Body();
