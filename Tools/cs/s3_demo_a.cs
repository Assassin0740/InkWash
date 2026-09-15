// s3_demo_a.cs —— 录像证据 A：三只墨徒从四面八方逼近并围攻，玩家连续挥砍。
// 时间驱动（不是帧数驱动）：录制会把帧率压到 20~35，按帧数等会把段长放大好几倍。
using System.Collections;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;

IEnumerator Body()
{
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[s3_demo_a] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var stance = go.GetComponentInChildren<CombatStance>(true);

    spawner.ResetForTest();
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null) Destroy(e.gameObject);
    yield return null;

    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(0f, 0.45f, -2f);
    go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    if (cc != null) cc.enabled = true;
    ctl.ResetToLocomotion();
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    { float t = Time.time; while (Time.time - t < 0.5f) yield return null; }

    ctl.BeginInputOverride();
    spawner.Begin();

    // 段 1（6 s）：站定，让三只墨徒从三个方向围上来
    { float t = Time.time; while (Time.time - t < 6.0f) { ctl.SetInjectedMove(Vector2.zero, false); yield return null; } }

    // 段 2（5 s）：正面挥砍
    {
        float t = Time.time; float next = 0f;
        while (Time.time - t < 5.0f)
        {
            ctl.SetInjectedMove(Vector2.zero, false);
            if (Time.time >= next) { ctl.RequestInjectedAttack(); next = Time.time + 0.9f; }
            yield return null;
        }
    }

    // 段 3（4 s）：一边后撤一边回身再砍，展示追击与击退
    {
        float t = Time.time; float next = 0f;
        while (Time.time - t < 4.0f)
        {
            float el = Time.time - t;
            ctl.SetInjectedMove(new Vector2(0f, el < 1.6f ? -1f : 0f), false);
            if (el > 1.6f && Time.time >= next) { ctl.RequestInjectedAttack(); next = Time.time + 0.75f; }
            yield return null;
        }
    }

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    Debug.Log("[s3_demo_a] done");
    yield return null;
}
return Body();
