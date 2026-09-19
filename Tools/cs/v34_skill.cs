// v34_skill.cs —— 墨爆（PlayerActiveSkill）冒烟：
//   ① 组件挂在玩家身上 ② TryCast 命中半径内敌人并扣血 ③ 冷却锁 ④ 墨花池动 ⑤ HUD 读数口
using System;
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v34] FAIL: RunManager 不在场"); yield break; }
    var choice = Object.FindObjectOfType<InkWash.Roguelike.SkillChoicePanel>();
    Application.runInBackground = true;

    run.ResetForTest();
    yield return null;
    run.StartRun();
    int guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    if (run.State != InkWash.Roguelike.RunState.Playing) { Debug.Log("[v34] FAIL 未进 Playing"); yield break; }

    // 等第一波刷出来
    guard = 0;
    while (run.spawner.SpawnedCount == 0 && guard++ < 1200) yield return null;

    var skill = Object.FindObjectOfType<InkWash.Player.PlayerActiveSkill>();
    if (skill == null) { Debug.Log("[v34] FAIL: PlayerActiveSkill 不在场（prefab 挂载失败？）"); yield break; }

    // 找一只活怪，把玩家挪到它旁边 2m（保证在 5m 半径内）
    InkWash.Enemies.EnemyBase enemy = null;
    guard = 0;
    while (enemy == null && guard++ < 1200)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { Debug.Log("[v34] FAIL: 场上无怪"); yield break; }

    var player = run.playerHealth.transform;
    Vector3 ep = enemy.Transform.position;
    Vector3 dst = ep + new Vector3(2f, 0f, 0f);
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    player.position = dst;
    if (cc != null) cc.enabled = true;
    yield return null;

    float hp0 = enemy.Health;
    int vfx0 = InkWash.Effects.InkHitVfx.Instance != null ? InkWash.Effects.InkHitVfx.Instance.SpawnCount : -1;

    // 施放
    bool ok1 = skill.TryCast();
    yield return null;

    bool cooldownLocked = !skill.TryCast();                 // 冷却中应拒绝
    float cdLeft = skill.CooldownLeft;
    int hits = skill.LastHitCount;
    float hp1 = enemy.IsAlive ? enemy.Health : 0f;
    bool hpDropped = hp1 < hp0 || !enemy.IsAlive;
    int vfx1 = InkWash.Effects.InkHitVfx.Instance != null ? InkWash.Effects.InkHitVfx.Instance.SpawnCount : -1;
    bool vfxFired = vfx0 >= 0 && vfx1 > vfx0;

    // 冷却倒数（走 1.5s 实际时间）
    yield return new WaitForSecondsRealtime(1.5f);
    bool cdTicking = skill.CooldownLeft < cdLeft && skill.CooldownLeft > 0f;

    sb.Append("comp=").Append(skill != null)
      .Append(" cast1=").Append(ok1)
      .Append(" hits=").Append(hits)
      .Append(" hp ").Append(hp0.ToString("F0")).Append("->").Append(hp1.ToString("F0"))
      .Append(" hpDropped=").Append(hpDropped)
      .Append(" cdLock=").Append(cooldownLocked)
      .Append(" cd=").Append(cdLeft.ToString("F1")).Append("s tick=").Append(cdTicking)
      .Append(" vfx ").Append(vfx0).Append("->").Append(vfx1)
      .Append(" castN=").Append(skill.CastCount)
      .Append(" => ").Append((ok1 && hits >= 1 && hpDropped && cooldownLocked && cdTicking && vfxFired) ? "PASS" : "FAIL");
    Debug.Log("[v34] " + sb);
    yield return null;
}
return Body();
