// s3_diag_ai.cs —— 诊断：敌人为什么不追玩家？玩家的剑判定为什么打不到人？
// 只做观测、不改数据。输出 Tools/reports/s3_diag_ai.txt
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { sb.AppendLine("找不到 PlayerHealth"); File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s3_diag_ai.txt"), sb.ToString()); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();

    sb.AppendLine("===== s3_diag_ai =====");
    sb.AppendLine("spawner 配置: spawnCenter=" + spawner.spawnCenter + "  spawnRadius=" + spawner.spawnRadius
                  + "  minDistanceToPlayer=" + spawner.minDistanceToPlayer
                  + "  spawnPoints=" + (spawner.spawnPoints != null ? spawner.spawnPoints.Length : -1)
                  + "  waves=" + spawner.WaveCount + "  autoStart=" + spawner.autoStart
                  + "  当前 AllCleared=" + spawner.AllCleared + "  SpawnedCount=" + spawner.SpawnedCount);
    sb.AppendLine("玩家 position = " + go.transform.position);
    sb.AppendLine("PlayerRef.Exists = " + PlayerRef.Exists + "  Position = " + PlayerRef.Position);
    sb.AppendLine("PlayerRef.Instance == go ? " + (PlayerRef.Instance == go.transform));

    HitStop.Enabled = false;
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);

    // 把玩家放到场地中心偏北，保证在敌人的"正面"半区之外也有对照
    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(0f, 0.45f, -3f);
    if (cc != null) cc.enabled = true;
    ctl.ResetToLocomotion();

    if (!spawner.AllCleared && spawner.SpawnedCount > 0)
        sb.AppendLine("!! 警告：进入脚本时 spawner 已经在跑（多半是上一次 --runtime 残留的 Play 会话）");
    spawner.ResetDiagnostics();

    // 逐帧追踪前 2.5 秒，看状态机到底走没走进 Chase
    sb.AppendLine();
    sb.AppendLine("---- 生成后逐帧追踪（只打状态变化）----");
    string prevTrace = "";
    spawner.Begin();
    {
        float t = Time.time;
        while (Time.time - t < 2.5f)
        {
            var live = new List<EnemyBase>(Object.FindObjectsOfType<EnemyBase>());
            string cur = "";
            foreach (var e in live)
                cur += e.enemyName + ":" + e.State + "@" + e.DistanceToPlayer.ToString("F1")
                       + "(y" + e.transform.position.y.ToString("F2") + ") ";
            if (cur != prevTrace) { sb.AppendLine("   t=" + (Time.time - t).ToString("F2") + "  " + cur); prevTrace = cur; }
            yield return null;
        }
    }

    { float t = Time.time; while (spawner.SpawnedCount < 3 && Time.time - t < 10f) yield return null; }
    { float t = Time.time; while (Time.time - t < 3.0f) yield return null; }

    var enemies = new List<EnemyBase>(Object.FindObjectsOfType<EnemyBase>());
    sb.AppendLine();
    sb.AppendLine("敌人数 = " + enemies.Count);
    foreach (var e in enemies)
    {
        var ag = e.GetComponent<NavMeshAgent>();
        Vector3 eye = e.transform.position + e.eyeOffset;
        Vector3 target = PlayerRef.Position + Vector3.up * 1.0f;
        Vector3 to = target - eye;
        float dist = to.magnitude;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        float ang = flat.sqrMagnitude > 1e-4f ? Vector3.Angle(e.transform.forward, flat) : -1f;

        string lineHit = "<无遮挡>";
        RaycastHit h;
        if (Physics.Linecast(eye, target, out h, ~0, QueryTriggerInteraction.Ignore))
        {
            var tr = h.collider.transform;
            bool self = tr == e.transform || tr.IsChildOf(e.transform);
            bool ply = PlayerRef.Instance != null && (tr == PlayerRef.Instance || tr.IsChildOf(PlayerRef.Instance));
            lineHit = h.collider.name + "  layer=" + h.collider.gameObject.layer
                      + "  是敌人自己=" + self + " 是玩家=" + ply + " 距命中=" + h.distance.ToString("F2");
        }

        sb.AppendLine("--- " + e.enemyName + "  state=" + e.State + "  stateTime=" + e.StateTime.ToString("F2"));
        sb.AppendLine("    pos=" + e.transform.position.ToString("F3") + "  eulerY=" + e.transform.eulerAngles.y.ToString("F1"));
        sb.AppendLine("    forward=" + e.transform.forward.ToString("F3"));
        sb.AppendLine("    dist=" + dist.ToString("F3") + "  sightRange=" + e.sightRange
                      + "  (dist>range? " + (dist > e.sightRange) + ")");
        sb.AppendLine("    angle=" + ang.ToString("F1") + "  半视野=" + (e.sightAngleDeg * 0.5f).ToString("F1")
                      + "  (angle>half? " + (ang > e.sightAngleDeg * 0.5f) + ")");
        sb.AppendLine("    requireLineOfSight=" + e.requireLineOfSight + "  Linecast → " + lineHit);
        sb.AppendLine("    agent: enabled=" + (ag != null && ag.enabled) + " isOnNavMesh=" + (ag != null && ag.isOnNavMesh)
                      + " isStopped=" + (ag != null && ag.isStopped)
                      + " hasPath=" + (ag != null && ag.hasPath)
                      + " speed=" + (ag != null ? ag.speed.ToString("F2") : "-")
                      + " pos=" + (ag != null ? ag.nextPosition.ToString("F2") : "-"));
        sb.AppendLine("    animator: null? " + (e.animator == null) + "  paramCount=" + (e.animator != null && e.animator.runtimeAnimatorController != null ? e.animator.parameters.Length : -1));
    }

    // ---- 手动把它放到玩家面前，看它会不会追 ----
    sb.AppendLine();
    sb.AppendLine("---- 强制把第一只敌人瞬移到玩家面前 6 m，观察 2 s ----");
    if (enemies.Count > 0)
    {
        var e0 = enemies[0];
        var ag0 = e0.GetComponent<NavMeshAgent>();
        Vector3 p = PlayerRef.Position + new Vector3(0f, 0f, 6f);
        if (ag0 != null && ag0.enabled) ag0.Warp(p); else e0.transform.position = p;
        { float t = Time.time; while (Time.time - t < 2.0f) yield return null; }
        sb.AppendLine("  " + e0.enemyName + "  state=" + e0.State + "  pos=" + e0.transform.position.ToString("F3")
                      + "  dist=" + e0.DistanceToPlayer.ToString("F2")
                      + "  agent.velocity=" + (ag0 != null ? ag0.velocity.ToString("F3") : "-")
                      + "  hasPath=" + (ag0 != null && ag0.hasPath)
                      + "  desiredVelocity=" + (ag0 != null ? ag0.desiredVelocity.ToString("F3") : "-")
                      + "  remainingDistance=" + (ag0 != null ? ag0.remainingDistance.ToString("F2") : "-"));
    }

    // ---- 玩家挥剑打它 ----
    sb.AppendLine();
    sb.AppendLine("---- 玩家贴到它面前 1.7 m 并挥剑，逐帧记录判定体 ----");
    if (enemies.Count > 0)
    {
        var target = enemies[0];
        var hbT = go.transform.Find("SwordHitbox");
        var hb = hbT != null ? hbT.GetComponent<Hitbox>() : null;
        var psh = hbT != null ? hbT.GetComponent<PlayerSwordHitbox>() : null;
        var vfx = go.GetComponent<InkWash.Effects.SwordVfx>();
        sb.AppendLine("  SwordHitbox 节点 = " + (hbT != null) + "  Hitbox = " + (hb != null) + "  PlayerSwordHitbox = " + (psh != null));
        sb.AppendLine("  SwordVfx = " + (vfx != null) + "  WeaponInstance = " + (vfx != null && vfx.WeaponInstance != null ? vfx.WeaponInstance.name : "<null>"));
        if (hb != null) hb.ResetDiagnostics();
        if (psh != null) psh.ResetDiagnostics();

        var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
        if (stance != null) stance.combatExitDelay = 99999f;

        Vector3 to = target.transform.position - go.transform.position; to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) to = Vector3.forward;
        to.Normalize();
        if (cc != null) cc.enabled = false;
        go.transform.position = target.transform.position - to * 1.7f + Vector3.up * 0.1f;
        go.transform.rotation = Quaternion.LookRotation(to, Vector3.up);
        if (cc != null) cc.enabled = true;
        ctl.ResetToLocomotion();
        if (stance != null) stance.ForceStance(true);
        { float t = Time.time; while (Time.time - t < 0.6f) yield return null; }

        sb.AppendLine("  挥剑前：玩家=" + go.transform.position.ToString("F3") + "  目标=" + target.transform.position.ToString("F3")
                      + "  水平距=" + Vector3.Distance(new Vector3(go.transform.position.x, 0, go.transform.position.z),
                                                     new Vector3(target.transform.position.x, 0, target.transform.position.z)).ToString("F3"));
        float hp0 = target.Health;
        ctl.RequestInjectedAttack();
        float t0 = Time.time;
        int frames = 0;
        while (Time.time - t0 < 1.5f)
        {
            frames++;
            if (hb != null)
            {
                Vector3 a = hbT.TransformPoint(hb.pointA);
                Vector3 b = hbT.TransformPoint(hb.pointB);
                var buf = new Collider[32];
                int n = Physics.OverlapCapsuleNonAlloc(a, b, hb.radius, buf, hb.targetMask, QueryTriggerInteraction.Ignore);
                string names = "";
                for (int i = 0; i < n; i++) if (buf[i] != null) names += "[" + buf[i].name + " L" + buf[i].gameObject.layer + "]";
                if (hb.IsActive || hb.HitCount > 0)
                    sb.AppendLine("    f" + frames + " t=" + (Time.time - t0).ToString("F2")
                                  + "  active=" + hb.IsActive + "  A=" + a.ToString("F2") + " B=" + b.ToString("F2")
                                  + " r=" + hb.radius.ToString("F2") + "  扫描到 " + n + " 个: " + names
                                  + "  HitCount=" + hb.HitCount);
            }
            yield return null;
        }
        sb.AppendLine("  HitMomentCount=" + (psh != null ? psh.HitMomentCount : -1)
                      + "  剑身长度=" + (psh != null ? psh.LastBladeLength.ToString("F3") : "-")
                      + "  开窗=" + (hb != null ? hb.ActivationCount : -1)
                      + "  命中=" + (hb != null ? hb.HitCount : -1));
        sb.AppendLine("  目标 HP " + hp0.ToString("F1") + " → " + target.Health.ToString("F1")
                      + "  受击次数=" + target.DamageTakenCount + "  状态=" + target.State);
    }

    // 收尾
    foreach (var e in Object.FindObjectsOfType<EnemyBase>())
        if (e != null && e.IsAlive)
            e.TakeDamage(new DamageInfo { amount = 100000f, sourceFaction = Faction.Player });
    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s3_diag_ai.txt"), sb.ToString());
    Debug.Log("[s3_diag_ai] done");
    yield return null;
}
return Body();
