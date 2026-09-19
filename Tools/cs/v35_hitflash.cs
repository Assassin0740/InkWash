// v35_hitflash.cs —— 受击材质闪白冒烟：
//   ① 打怪后其渲染器 MPB 的 _HitFlash > 0.5（闪起来了）
//   ② ~0.6s 后衰减到 0（收得干净，无残留）
//   ③ 玩家被打后同样闪（PlayerHitFeedback 路径）
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v35] FAIL: RunManager 不在场"); yield break; }
    Application.runInBackground = true;

    run.ResetForTest();
    yield return null;
    run.StartRun();
    int guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;

    // 等第一波刷出来
    guard = 0;
    InkWash.Enemies.EnemyBase enemy = null;
    while (enemy == null && guard++ < 1200)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { Debug.Log("[v35] FAIL: 场上无怪"); yield break; }

    // MPB 读数助手
    var mpb = new MaterialPropertyBlock();
    System.Func<Renderer, float> readFlash = r =>
    {
        if (r == null) return -1f;
        r.GetPropertyBlock(mpb);
        return mpb.GetFloat("_HitFlash");   // 未设置时返回 0
    };

    // 找受击方第一个有墨材质的渲染器
    Renderer tgt = null;
    foreach (var r in enemy.GetComponentsInChildren<Renderer>())
        if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader.name.StartsWith("InkWash/")) { tgt = r; break; }
    if (tgt == null) { Debug.Log("[v35] FAIL: 怪身上找不到水墨渲染器"); yield break; }

    float before = readFlash(tgt);

    // 打一下（重击口径的伤害让闪白明显）
    var info = new InkWash.Combat.DamageInfo
    {
        amount = 30f,
        hitPoint = enemy.Transform.position + Vector3.up,
        hitDirection = Vector3.forward,
        hitStop = 0.085f,
        hitStun = 0.3f,
    };
    enemy.TakeDamage(info);
    yield return null;                      // 等 HitFlash.Update 至少跑一帧

    float peak = readFlash(tgt);

    // 衰减：decayPerSec=5 ⇒ 0.6s 后应归零（含清零残留）
    yield return new WaitForSecondsRealtime(0.6f);
    float after = readFlash(tgt);

    // 玩家路径：PlayerHitFeedback 订阅 Damaged —— 直接对玩家血量扣一次
    var phf = Object.FindObjectOfType<InkWash.Player.PlayerHitFeedback>();
    var hp = run.playerHealth;
    Renderer pTgt = null;
    foreach (var r in hp.GetComponentsInChildren<Renderer>())
        if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader.name.StartsWith("InkWash/")) { pTgt = r; break; }
    float pBefore = pTgt != null ? readFlash(pTgt) : -1f;
    float php0 = hp.Health;
    hp.TakeDamage(new InkWash.Combat.DamageInfo
    {
        amount = 20f,
        hitPoint = hp.transform.position + Vector3.up,
        hitDirection = Vector3.back,
        hitStop = 0.05f,
        hitStun = 0f,
    });
    yield return null;
    float pPeak = pTgt != null ? readFlash(pTgt) : -1f;
    bool pHit = hp.Health < php0;

    bool ok = before <= 0.001f && peak > 0.5f && after <= 0.001f && pPeak > 0.5f && pHit;
    sb.Append("tgt=").Append(tgt.name)
      .Append(" flash ").Append(before.ToString("F2")).Append("->").Append(peak.ToString("F2")).Append("->").Append(after.ToString("F2"))
      .Append(" player ").Append(pBefore.ToString("F2")).Append("->").Append(pPeak.ToString("F2"))
      .Append(" pHit=").Append(pHit)
      .Append(" => ").Append(ok ? "PASS" : "FAIL");
    Debug.Log("[v35] " + sb);
    yield return null;
}
return Body();
