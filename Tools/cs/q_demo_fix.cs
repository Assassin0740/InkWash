// q_demo_fix.cs —— 录「走路 / 跑步 / 连击」演示，视角 = 游戏默认第三人称（角色背后）
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_demo_fix] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (ctl == null || rig == null) { Debug.LogError("[q_demo_fix] 缺组件"); yield break; }

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    rig.SetMouseLookEnabled(false);
    rig.SnapBehindTarget();
    rig.pitch = 3f;
    for (int i = 0; i < 40; i++) yield return null;

    var anim = go.GetComponent<Animator>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);

    // ---- ① 走路 4s（非战斗：剑在背、手垂下；看手臂是否还外张）----
    ctl.SetInjectedMove(new Vector2(0f, 1f), false);
    for (int i = 0; i < 80; i++) yield return null;
    var ci = anim.GetCurrentAnimatorClipInfo(0);
    sb.AppendLine("① 走路 4s 片段=" + (ci.Length > 0 ? ci[0].clip.name : "?") + "  速度=" + ctl.CurrentSpeed.ToString("F2"));

    // ---- ② 跑步 3s（Shift）----
    ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    for (int i = 0; i < 60; i++) yield return null;
    ci = anim.GetCurrentAnimatorClipInfo(0);
    sb.AppendLine("② 跑步 3s 片段=" + (ci.Length > 0 ? ci[0].clip.name : "?") + "  速度=" + ctl.CurrentSpeed.ToString("F2"));

    // ---- ③ 三段连击 ×3 轮 ----
    ctl.SetInjectedMove(Vector2.zero, false);
    for (int i = 0; i < 30; i++) yield return null;
    for (int round = 0; round < 3; round++)
    {
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 34; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 34; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 80; i++) yield return null;
    }
    if (pose != null)
        sb.AppendLine("③ 收尾握拳度 右=" + pose.RightFistRatio.ToString("F2") + " 左=" + pose.LeftFistRatio.ToString("F2"));

    ctl.EndInputOverride();
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_demo_fix.txt"), sb.ToString());
    Debug.Log("[q_demo_fix] done");
    yield return null;
}
return Body();
