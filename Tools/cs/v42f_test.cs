// v42f: 施法诊断 —— 参数存在性 / TryCast 返回值 / castCount / 状态机实时采样
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

    var skill = Object.FindObjectOfType<InkWash.Player.PlayerActiveSkill>();
    var ph = Object.FindObjectOfType<InkWash.Combat.PlayerHealth>();
    var animator = ph != null ? ph.GetComponentInChildren<Animator>() : null;
    if (skill == null || animator == null) { sb.AppendLine("FAIL null"); Debug.Log("[v42f]\n" + sb); yield break; }

    bool hasCast = false;
    foreach (var p in animator.parameters)
        if (p.name == "Cast") { hasCast = true; sb.AppendLine("param Cast found, type=" + p.type); }
    if (!hasCast) sb.AppendLine("param Cast NOT FOUND in runtime animator");

    int cc0 = skill.CastCount;
    bool ok = skill.TryCast();
    sb.AppendLine("TryCast=" + ok + " castCount " + cc0 + "->" + skill.CastCount + " cooldownLeft=" + skill.CooldownLeft.ToString("F2"));
    // 触发器消费观察：逐步采样状态与 transition 信息
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string last = "";
    while (sw.Elapsed.TotalSeconds < 2.0f)
    {
        var stn = animator.GetCurrentAnimatorStateInfo(0);
        string nm = "?";
        var info = animator.GetCurrentAnimatorClipInfo(0);
        if (info.Length > 0) nm = info[0].clip.name;
        string key = stn.IsName("Cast") + ":" + nm + ":" + stn.normalizedTime.ToString("F2");
        if (key != last) { sb.AppendLine("t=" + sw.Elapsed.TotalSeconds.ToString("F2") + " isCast=" + (stn.IsName("Cast") ? 1 : 0) + " clip=" + nm + " nT=" + stn.normalizedTime.ToString("F2")); last = key; }
        yield return null;
    }
    Debug.Log("[v42f]\n" + sb);
    yield break;
}

return Body();
