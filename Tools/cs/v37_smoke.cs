// v37_smoke.cs —— 第三十七轮冒烟：BGM ✓ 敌尸坠地 ✓ 玩家死亡动画 ✓ 音效通路 ✓
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v37s] FAIL no RunManager"); yield break; }
    Application.runInBackground = true;

    // ① BGM（Awake 已 PlayBgm）
    yield return null;
    bool bgm = false;
    foreach (var src in Object.FindObjectsOfType<AudioSource>())
        if (src.gameObject.name == "AudioManager" && src.isPlaying && src.loop) { bgm = true; break; }

    // ② 音效通路：直接调 Play 不炸 + 池存在
    bool sfxOk = true;
    try
    {
        InkWash.Audio.AudioManager.Play("Swing_A");
        InkWash.Audio.AudioManager.Play("Hit_Flesh");
        InkWash.Audio.AudioManager.Play("不存在的音效");    // 应静默跳过
    }
    catch (System.Exception e) { sfxOk = false; Debug.Log("[v37s] sfx ex=" + e.Message); }

    // ③ 敌尸坠地
    run.ResetForTest(); yield return null;
    run.StartRun();
    int guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    guard = 0;
    InkWash.Enemies.EnemyBase enemy = null;
    while (enemy == null && guard++ < 1200)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { Debug.Log("[v37s] FAIL no enemy"); yield break; }

    float y0 = enemy.Transform.position.y;
    // 先把怪放到空中 2m（模拟被击退飞起的死亡瞬间），再杀 —— 贴地的怪本来就不需要坠
    enemy.Transform.position = new Vector3(enemy.Transform.position.x, 2f, enemy.Transform.position.z);
    enemy.TakeDamage(new InkWash.Combat.DamageInfo
    { amount = 9999f, hitPoint = enemy.Transform.position, hitDirection = Vector3.forward, hitStop = 0.085f, hitStun = 0f });
    yield return new WaitForSecondsRealtime(1.4f);
    float y1 = enemy != null ? enemy.Transform.position.y : 2f;
    bool corpseFell = y1 < 0.35f;   // 回到地面附近

    // ④ 玩家死亡动画（重开一局避免脏状态）
    run.StartRun();
    guard = 0;
    while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    var hp = run.playerHealth;
    var anim = hp.GetComponentInChildren<Animator>();
    bool hasDead = false;
    if (anim != null && anim.runtimeAnimatorController != null)
        foreach (var p in anim.parameters)
            if (p.type == UnityEngine.AnimatorControllerParameterType.Trigger && p.name == "Dead") { hasDead = true; break; }

    float php0 = hp.Health;
    hp.TakeDamage(new InkWash.Combat.DamageInfo
    { amount = 99999f, hitPoint = hp.transform.position, hitDirection = Vector3.back, hitStop = 0f, hitStun = 0f });
    yield return new WaitForSecondsRealtime(0.9f);
    bool inDeathState = anim != null && anim.GetCurrentAnimatorStateInfo(0).IsName("Death");
    bool upright = Vector3.Dot(hp.transform.up, Vector3.up) > 0.5f;   // 没被程序化拧倒

    bool ok = bgm && sfxOk && corpseFell && hasDead && inDeathState && upright;
    sb.Append("bgm=").Append(bgm).Append(" sfxOk=").Append(sfxOk)
      .Append(" corpse ").Append(y0.ToString("F2")).Append("->").Append(y1.ToString("F2")).Append(" fell=").Append(corpseFell)
      .Append(" hasDead=").Append(hasDead).Append(" deathState=").Append(inDeathState).Append(" upright=").Append(upright)
      .Append(" => ").Append(ok ? "PASS" : "FAIL");
    Debug.Log("[v37s] " + sb);
    yield return null;
}
return Body();
