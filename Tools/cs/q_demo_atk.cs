// q_demo_atk.cs —— 录三段连击演示，视角 = 游戏默认第三人称（角色背后，与用户截图同视角）
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_demo_atk] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (ctl == null || rig == null) { Debug.LogError("[q_demo_atk] 缺组件"); yield break; }

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    rig.SetMouseLookEnabled(false);
    rig.SnapBehindTarget();
    rig.pitch = 2f;
    for (int i = 0; i < 40; i++) yield return null;

    sb.AppendLine("相机距离 " + rig.CurrentDistance.ToString("F2") + "m   pitch=" + rig.pitch.ToString("F1"));

    // 三段连击；段间隔 46 帧（0.77s）> 取消窗口开启时刻（攻击开始后约 0.70s）
    for (int round = 0; round < 4; round++)
    {
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 46; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 46; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 110; i++) yield return null;
    }

    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    if (pose != null)
        sb.AppendLine("收尾 握拳度 右=" + pose.RightFistRatio.ToString("F2")
            + " 左=" + pose.LeftFistRatio.ToString("F2") + "（摊开≈2.2 / 握拳≈1.2）");
    var anim = go.GetComponent<Animator>();
    var ci = anim.GetCurrentAnimatorClipInfo(0);
    sb.AppendLine("收尾状态片段 = " + (ci.Length > 0 ? ci[0].clip.name : "<空>"));

    ctl.EndInputOverride();
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_demo_atk.txt"), sb.ToString());
    Debug.Log("[q_demo_atk] done");
    yield return null;
}
return Body();
