// s4_demo_b.cs —— M4 录像证据 ②：全效果（阶段 4）下的战斗段。
// 目的：证明水墨风格在**动起来**的时候也成立 —— 墨线跟着四肢走、飞白不闪、宣纸不过曝。
using System.Collections;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.UI;

IEnumerator Body()
{
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[s4_demo_b] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();

    if (spawner != null) spawner.ResetForTest();
    foreach (var e in Object.FindObjectsOfType<EnemyBase>()) if (e != null) Destroy(e.gameObject);
    if (ctl != null) ctl.ResetToLocomotion();
    yield return null;

    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(0f, 0.45f, -2f);
    go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    if (cc != null) cc.enabled = true;
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    if (panel != null) { panel.visible = true; panel.ApplyStage(3); }   // 全效果

    ctl.BeginInputOverride();
    if (spawner != null) spawner.Begin();
    { float t = Time.time; while (Time.time - t < 0.8f) yield return null; }

    // 段 1（5 s）：站定，让墨徒围上来（看它们在墨线下的样子）
    ph.ResetHealth();
    { float t = Time.time; while (Time.time - t < 5.0f) { ctl.SetInjectedMove(Vector2.zero, false); yield return null; } }

    // 段 2（8 s）：连续挥砍（看刀光、击退、命中在墨线下的表现）
    ph.ResetHealth();
    {
        float t0 = Time.time, next = 0f;
        while (Time.time - t0 < 8.0f)
        {
            ctl.SetInjectedMove(Vector2.zero, false);
            if (Time.time >= next) { ctl.RequestInjectedAttack(); next = Time.time + 0.85f; }
            yield return null;
        }
    }

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    Debug.Log("[s4_demo_b] done");
    yield return null;
}
return Body();
