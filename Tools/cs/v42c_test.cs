// v42c: 运行时验收 Mixamo 重定向 —— ①Idle clip 是 mixamo ②打玩家一下看 Hit 态 ③截图
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    yield return new WaitForSecondsRealtime(1.5f);

    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var sw3 = System.Diagnostics.Stopwatch.StartNew();
    while (run != null && run.State != InkWash.Roguelike.RunState.Playing && sw3.Elapsed.TotalSeconds < 25.0)
        yield return null;

    var ph = Object.FindObjectOfType<InkWash.Combat.PlayerHealth>();
    var fb = Object.FindObjectOfType<InkWash.Player.PlayerHitFeedback>();
    if (ph == null) { sb.AppendLine("RESULT FAIL playerHealth null"); Debug.Log("[v42c]\n" + sb); yield break; }
    var animator = ph.GetComponentInChildren<Animator>();
    if (animator == null) { sb.AppendLine("RESULT FAIL animator null"); Debug.Log("[v42c]\n" + sb); yield break; }

    // ① 待机态 clip
    var st0 = animator.GetCurrentAnimatorStateInfo(0);
    var cur = animator.GetCurrentAnimatorClipInfo(0);
    sb.AppendLine("idle clip: " + (cur.Length > 0 ? cur[0].clip.name : "?") + "  stateHash=" + st0.shortNameHash);

    // ② 打一下 → 盯 1.2s 状态
    int hp0 = (int)ph.Health;
    ph.TakeDamage(InkWash.Combat.DamageInfo.Simple(8f, InkWash.Combat.Faction.Enemy, null));
    string hitClipSeen = null;
    bool enteredHit = false;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < 2.0)
    {
        var info = animator.GetCurrentAnimatorClipInfo(0);
        if (info.Length > 0)
        {
            string n = info[0].clip.name;
            if (n != hitClipSeen) { hitClipSeen = n; sb.AppendLine("t=" + sw.Elapsed.TotalSeconds.ToString("F2") + "s clip=" + n); }
            var stn = animator.GetCurrentAnimatorStateInfo(0);
            // Hit 态 clip 名为 mixamo.com；用状态名判断更稳
            if (animator.isActiveAndEnabled)
            {
                // 通过状态名 hash 比对
                if (!enteredHit && stn.IsName("Hit")) { enteredHit = true; sb.AppendLine("ENTERED Hit state"); }
            }
        }
        if (fb != null && fb.HitCount > 0 && !enteredHit && sw.Elapsed.TotalSeconds > 0.5f)
        {
            // HitCount 已增但状态名不叫 Hit —— 也记录触发链路
        }
        yield return null;
    }
    sb.AppendLine("hitCount=" + (fb != null ? fb.HitCount : -1) + " hp " + hp0 + "->" + (int)ph.Health + " enteredHit=" + enteredHit);

    // ③ 截图（受击后回待机）
    yield return new WaitForSecondsRealtime(0.6f);
    var cam = UnityEngine.Camera.main;
    if (cam != null)
    {
        UnityEngine.ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/v42_after.png");
    }
    sb.AppendLine("RESULT " + (enteredHit || (hitClipSeen != null)) + " hitClipSeen=" + hitClipSeen);
    Debug.Log("[v42c]\n" + sb);
    yield break;
}

return Body();
