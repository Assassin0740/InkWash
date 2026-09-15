// q_demo_final.cs —— 演示：待机 → 走路(验证循环) → 跑步 → 三段连击 ×3 → 静置
// ★ 与旧版 q_demo_fix 的区别：结尾**恢复鼠标视角**（上次漏了这步，留下一个禁用了鼠标的
//   Play 会话，用户回到 Unity 发现「鼠标转视角没了」），并统计走路片段的循环回绕次数。
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_demo_final] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    var anim = go.GetComponent<Animator>();
    if (ctl == null || rig == null) { Debug.LogError("[q_demo_final] 缺组件"); yield break; }

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    rig.SetMouseLookEnabled(false);
    rig.SnapBehindTarget();
    rig.pitch = 4f;
    for (int i = 0; i < 30; i++) yield return null;

    // ① 非战斗待机（垂手 + 背剑）
    for (int i = 0; i < 25; i++) yield return null;
    var ci = anim.GetCurrentAnimatorClipInfo(0);
    sb.AppendLine("① 待机片段 = " + (ci.Length > 0 ? ci[0].clip.name : "?")
        + "  isLooping=" + (ci.Length > 0 && ci[0].clip.isLooping));

    // ② 走路 3.5s —— 顺带统计循环回绕次数（片段 1.333s ÷ 倍率 1.374 ≈ 0.97s 一循环，应回绕 ≥3 次）
    ctl.SetInjectedMove(new Vector2(0f, 1f), false);
    int wrap = 0; float prevNt = -1f; string curClip = "";
    for (int i = 0; i < 210; i++)
    {
        var info = anim.GetCurrentAnimatorStateInfo(0);
        float nt = info.normalizedTime;
        var c = anim.GetCurrentAnimatorClipInfo(0);
        if (c.Length > 0) curClip = c[0].clip.name;
        if (prevNt >= 0f && nt < prevNt - 0.05f) wrap++;
        prevNt = nt;
        yield return null;
    }
    sb.AppendLine("② 走路 3.5s 片段=" + curClip + "  速度=" + ctl.CurrentSpeed.ToString("F2")
        + "  循环回绕 " + wrap + " 次（3.5s ÷ 0.97s ≈ 3.6 个循环，应 ≥3）");

    // ③ 跑步 1.5s
    ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    for (int i = 0; i < 90; i++) yield return null;
    ci = anim.GetCurrentAnimatorClipInfo(0);
    sb.AppendLine("③ 跑步 3s 片段=" + (ci.Length > 0 ? ci[0].clip.name : "?")
        + "  速度=" + ctl.CurrentSpeed.ToString("F2"));

    // ④ 三段连击 ×3 轮
    ctl.SetInjectedMove(Vector2.zero, false);
    for (int i = 0; i < 25; i++) yield return null;
    float t0 = Time.time;
    for (int round = 0; round < 3; round++)
    {
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 30; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 30; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 70; i++) yield return null;
    }
    sb.AppendLine("④ 三段连击 ×3 轮 耗时 " + (Time.time - t0).ToString("F2") + "s（每轮 "
        + ((Time.time - t0) / 3f).ToString("F2") + "s）");

    // ⑤ 收招后静置
    for (int i = 0; i < 30; i++) yield return null;

    ctl.EndInputOverride();
    if (rig != null) rig.SetMouseLookEnabled(true);   // ★ 恢复鼠标视角
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_demo_final.txt"), sb.ToString());
    Debug.Log("[q_demo_final] done");
    yield return null;
}
return Body();
