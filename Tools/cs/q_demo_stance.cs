// q_demo_stance.cs —— 演示录屏：平时站着（垂手 · 剑在背）→ 攻击拔剑 → 静置自动收剑
// 用 --record 录制；节奏按 prefab 默认 combatExitDelay = 6s 走一遍完整循环。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponent<InkWash.Player.CombatStance>();
    if (ctl == null || stance == null) { Debug.LogError("缺组件"); yield break; }

    for (int i = 0; i < 180 && !stance.IsReady; i++) yield return null;
    Debug.Log("[q_demo_stance] ready = " + stance.IsReady + "  combatExitDelay = " + stance.combatExitDelay);

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);

    // ① 平时站立 3s
    for (int i = 0; i < 180; i++) yield return null;

    // ② 拔剑攻击
    ctl.RequestInjectedAttack();
    // ③ 攻击播完（约 2.3s）+ 战斗待机 + combatExitDelay(6s) 自动收剑
    for (int i = 0; i < 720; i++) yield return null;

    Debug.Log("[q_demo_stance] end: InCombat=" + stance.InCombat
              + " 挂点=" + stance.WeaponMountPath
              + " 进战斗=" + stance.SwitchToCombatCount + " 退战斗=" + stance.SwitchToRelaxedCount);

    ctl.EndInputOverride();
    Debug.Log("[q_demo_stance] done");
    yield return null;
}

return Body();
