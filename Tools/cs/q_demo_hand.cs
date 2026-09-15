// q_demo_hand.cs —— 录一段「看得清手」的攻击演示（相机拉到 1.8 m）。
//   目的是给用户直接看：攻击全程两只手都是握着的（左手不再五指摊开）。
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_demo_hand] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (ctl == null || rig == null) { Debug.LogError("[q_demo_hand] 缺 PlayerController / ThirdPersonCamera"); yield break; }

    float baseDistance = rig.distance;
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    rig.SetMouseLookEnabled(false);
    rig.SnapBehindTarget();
    rig.yaw += 150f;          // 转到侧前方，左手在外侧、看得见
    rig.pitch = 4f;
    rig.distance = 1.8f;      // 拉近

    for (int i = 0; i < 45; i++) yield return null;   // 等相机距离收敛

    sb.AppendLine("相机距离 目标=" + rig.distance + "  实际=" + rig.CurrentDistance.ToString("F2") + "m");
    sb.AppendLine("相机世界位置 = " + Camera.main.transform.position.ToString("F2"));
    sb.AppendLine("角色世界位置 = " + go.transform.position.ToString("F2"));
    sb.AppendLine("相机到角色水平距离 = "
        + Vector3.Distance(new Vector3(Camera.main.transform.position.x, 0f, Camera.main.transform.position.z),
                           new Vector3(go.transform.position.x, 0f, go.transform.position.z)).ToString("F2") + "m");
    sb.AppendLine();
    sb.AppendLine("接着循环打三段连击（录制中）。");

    // 循环三段连击：每段按一次，间隔 0.42s / 0.42s / 1.15s
    for (int round = 0; round < 5; round++)
    {
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 25; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 25; i++) yield return null;
        ctl.RequestInjectedAttack();
        for (int i = 0; i < 70; i++) yield return null;
    }

    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    if (pose != null)
        sb.AppendLine("收尾时 握拳度 右=" + pose.RightFistRatio.ToString("F2")
            + " 左=" + pose.LeftFistRatio.ToString("F2") + "   （摊开≈2.2 / 握拳≈1.2）");

    ctl.EndInputOverride();
    rig.distance = baseDistance;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_demo_hand.txt"), sb.ToString());
    Debug.Log("[q_demo_hand] done");
    yield return null;
}

return Body();
