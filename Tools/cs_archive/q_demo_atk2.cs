// q_demo_atk2.cs —— 攻击演示：三段连击 ×2 轮
// 时间驱动（不用帧数）—— 编辑器里帧率会随录制开销波动，按帧数等会把连击打断。
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_demo_atk2] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    var anim = go.GetComponent<Animator>();
    if (ctl == null || rig == null) { Debug.LogError("[q_demo_atk2] 缺组件"); yield break; }

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    rig.SetMouseLookEnabled(false);
    rig.SnapBehindTarget();
    rig.pitch = 6f;
    for (int i = 0; i < 30; i++) yield return null;

    float t0 = Time.time;
    for (int round = 0; round < 2; round++)
    {
        float t = Time.time;
        var states = new System.Text.StringBuilder();

        ctl.RequestInjectedAttack();
        while (Time.time - t < 0.85f) yield return null;
        var c = anim.GetCurrentAnimatorClipInfo(0);
        states.Append(c.Length > 0 ? c[0].clip.name : "?").Append(" → ");

        ctl.RequestInjectedAttack();
        while (Time.time - t < 1.80f) yield return null;
        c = anim.GetCurrentAnimatorClipInfo(0);
        states.Append(c.Length > 0 ? c[0].clip.name : "?").Append(" → ");

        ctl.RequestInjectedAttack();
        while (Time.time - t < 3.40f) yield return null;
        c = anim.GetCurrentAnimatorClipInfo(0);
        states.Append(c.Length > 0 ? c[0].clip.name : "?");

        sb.AppendLine("第 " + (round + 1) + " 轮: " + states.ToString());

        while (Time.time - t < 4.30f) yield return null;   // 收招静置
    }
    sb.AppendLine("两轮总耗时 " + (Time.time - t0).ToString("F2") + "s（每轮 "
        + ((Time.time - t0) / 2f).ToString("F2") + "s）");

    ctl.EndInputOverride();
    if (rig != null) rig.SetMouseLookEnabled(true);   // ★ 恢复鼠标视角
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_demo_atk2.txt"), sb.ToString());
    Debug.Log("[q_demo_atk2] done");
    yield return null;
}
return Body();
