// v32_hitfeel.cs —— 第三十二轮打击感链路冒烟：
// A. 玩家受击：扣血 + PlayerHitFeedback 计数 + 震屏 + FOV 冲击 + 受击闪屏 + 顿帧计数
// B. 龙受击：flinch 强度点亮 → 0.6s 内衰减 + 硬直状态
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v32] FAIL: RunManager 不在场"); yield break; }

    // 进 Playing（从主菜单直接开局）
    int guard = 0;
    if (run.State != InkWash.Roguelike.RunState.Playing)
    {
        run.StartRun();
        while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    }
    sb.Append("state=").Append(run.State).Append(" (wait=").Append(guard).Append(")");
    if (run.State != InkWash.Roguelike.RunState.Playing) { Debug.Log("[v32] FAIL 未进入 Playing: " + sb); yield break; }

    var hp = Object.FindObjectOfType<InkWash.Player.PlayerHealth>();
    var phf = Object.FindObjectOfType<InkWash.Player.PlayerHitFeedback>();
    var cam = Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
    if (hp == null || phf == null || cam == null)
    {
        Debug.Log("[v32] FAIL 组件缺失 hp=" + (hp != null) + " phf=" + (phf != null) + " cam=" + (cam != null));
        yield break;
    }

    // ---- A. 玩家受击 ----
    phf.ResetDiagnostics();
    InkWash.Combat.HitStop.ResetDiagnostics();
    hp.ClearInvincibility();
    float before = hp.Health;
    var info = new InkWash.Combat.DamageInfo
    {
        amount = 10f,
        hitPoint = hp.transform.position + Vector3.up,
        hitDirection = Vector3.forward,
        knockback = 0f,
        hitStun = 0f,
        hitStop = 0.02f,
        sourceFaction = InkWash.Combat.Faction.Enemy,
    };
    bool dmg = hp.TakeDamage(info);
    yield return null; yield return null;

    sb.Append(" || A: dmg=").Append(dmg)
      .Append(" hp ").Append(before.ToString("F1")).Append("->").Append(hp.Health.ToString("F1"))
      .Append(" phfHit=").Append(phf.HitCount)
      .Append(" shake=").Append(cam.IsShaking)
      .Append(" fovPunch=").Append(cam.IsFovPunching)
      .Append(" hitstopCnt=").Append(InkWash.Combat.HitStop.RequestCount);

    var pres = InkWash.UI.RunPresentation.Instance;
    if (pres != null)
    {
        var f = typeof(InkWash.UI.RunPresentation).GetField("_hitFlash",
            BindingFlags.NonPublic | BindingFlags.Instance);
        sb.Append(" flash=").Append(f != null ? f.GetValue(pres) : "no-field");
    }
    else sb.Append(" flash=NO-PRESENTATION");

    // ---- B. 龙受击 flinch ----
    var drg = Object.FindObjectOfType<InkWash.Enemies.EnemyDragon>();
    if (drg == null)
    {
        sb.Append(" || B.dragon: SKIP（场景无龙）");
    }
    else
    {
        float h0 = drg.Health;
        var di = new InkWash.Combat.DamageInfo
        {
            amount = 6f,
            hitPoint = drg.transform.position + Vector3.up * 2f,
            hitDirection = new Vector3(0.7f, 0f, 0.7f).normalized,
            knockback = 2f,
            hitStun = 0.3f,
            hitStop = 0.05f,
            sourceFaction = InkWash.Combat.Faction.Player,
        };
        bool d2 = drg.TakeDamage(di);
        yield return null;
        float fp0 = drg.FlinchPower;
        string st0 = drg.State.ToString();
        yield return new WaitForSeconds(0.6f);
        sb.Append(" || B.dragon: dmg=").Append(d2)
          .Append(" hp ").Append(h0.ToString("F1")).Append("->").Append(drg.Health.ToString("F1"))
          .Append(" stun@0=").Append(st0)
          .Append(" flinch@0=").Append(fp0.ToString("F2"))
          .Append(" flinch@0.6s=").Append(drg.FlinchPower.ToString("F2"))
          .Append(" state@0.6s=").Append(drg.State);
    }

    Debug.Log("[v32] " + sb.ToString());
    yield return null;
}
return Body();
