// v42e: 运行时验收 ①Dash 播翻滚 ②TryCast 播施法 + 截图
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

    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    var skill = Object.FindObjectOfType<InkWash.Player.PlayerActiveSkill>();
    var ph = Object.FindObjectOfType<InkWash.Combat.PlayerHealth>();
    if (pc == null || skill == null || ph == null)
    { sb.AppendLine("RESULT FAIL pc/skill/health null"); Debug.Log("[v42e]\n" + sb); yield break; }
    var animator = ph.GetComponentInChildren<Animator>();

    // ① 冲刺翻滚
    pc.BeginInputOverride();
    pc.RequestInjectedDash();
    bool dashRoll = false;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < 1.2f)
    {
        var info = animator.GetCurrentAnimatorClipInfo(0);
        if (info.Length > 0 && animator.GetCurrentAnimatorStateInfo(0).IsName("Dash"))
        {
            dashRoll = true;
            sb.AppendLine("dash t=" + sw.Elapsed.TotalSeconds.ToString("F2") + " clip=" + info[0].clip.name);
            if (sw.Elapsed.TotalSeconds > 0.15f && sw.Elapsed.TotalSeconds < 0.3f)
                UnityEngine.ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/v42_roll.png");
        }
        yield return null;
    }
    pc.EndInputOverride();
    yield return new WaitForSecondsRealtime(1.0f);   // 冷却

    // ② 施法
    skill.TryCast();
    bool castSeen = false;
    sw.Restart();
    while (sw.Elapsed.TotalSeconds < 2.2f)
    {
        var info = animator.GetCurrentAnimatorClipInfo(0);
        if (info.Length > 0 && animator.GetCurrentAnimatorStateInfo(0).IsName("Cast"))
        {
            if (!castSeen) sb.AppendLine("cast t=" + sw.Elapsed.TotalSeconds.ToString("F2") + " clip=" + info[0].clip.name);
            castSeen = true;
            if (sw.Elapsed.TotalSeconds > 0.2f && sw.Elapsed.TotalSeconds < 0.5f)
                UnityEngine.ScreenCapture.CaptureScreenshot("Assets/../Tools/screenshots/v42_cast.png");
        }
        yield return null;
    }
    sb.AppendLine("RESULT " + (dashRoll && castSeen) + "  dashRoll=" + dashRoll + " castSeen=" + castSeen);
    Debug.Log("[v42e]\n" + sb);
    yield break;
}

return Body();
