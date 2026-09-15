// q_demo_v3.cs —— 本轮修复的演示录屏（时间驱动；结尾还原姿态与鼠标）
//   0.0~1.6s  非战斗待机（垂手，剑在背）
//   1.6~2.0s  切战斗姿态（拔剑）
//   2.0~4.4s  持剑待机（UAL1 Sword_Idle_Loop —— 剑不再插在脑门）★
//   4.4~8.0s  跑步（烘焙「持剑跑」Run01_Carry —— 剑不再抡）      ★
//   8.0~9.0s  停下，回到持剑待机
//   9.0~12.2s 三段连击（顺带展示左手握拳补丁已修好）
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var anim = go.GetComponent<Animator>();
    var cc = go.GetComponent<CharacterController>();
    var cam = Camera.main;
    var rig = cam != null ? cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;

    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos;
        go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    sb.AppendLine("段                时长    InCombat  Idle片段           所在状态        速度");
    float t;

    // 1) 非战斗待机
    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(false);
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    t = Time.time; while (Time.time - t < 1.6f) yield return null;
    sb.AppendLine(Seg("1 非战斗待机", 1.6f, stance, anim, ctl));

    // 2) 拔剑
    if (stance != null) stance.ForceStance(true);
    t = Time.time; while (Time.time - t < 0.4f) yield return null;

    // 3) 持剑待机 ★
    Park();
    t = Time.time; while (Time.time - t < 2.4f) yield return null;
    sb.AppendLine(Seg("3 持剑待机 ★", 2.4f, stance, anim, ctl));

    // 4) 跑步 ★（ForceStance 顺带把「6 秒自动收剑」计时清零）
    Park();
    if (stance != null) stance.ForceStance(true);
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    t = Time.time; while (Time.time - t < 3.6f) yield return null;
    sb.AppendLine(Seg("4 跑步 ★", 3.6f, stance, anim, ctl));

    // 5) 停下
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    t = Time.time; while (Time.time - t < 1.0f) yield return null;
    sb.AppendLine(Seg("5 停回待机", 1.0f, stance, anim, ctl));

    // 6) 三段连击（后摇取消窗口内再按）
    Park();
    if (stance != null) stance.ForceStance(true);
    t = Time.time;
    while (Time.time - t < 3.2f)
    {
        float e = Time.time - t;
        if (e >= 0.15f && e < 0.16f) ctl.RequestInjectedAttack();
        if (e >= 0.72f && e < 0.73f) ctl.RequestInjectedAttack();
        if (e >= 1.35f && e < 1.36f) ctl.RequestInjectedAttack();
        yield return null;
    }
    sb.AppendLine(Seg("6 三段连击", 3.2f, stance, anim, ctl));

    // 收尾：还原姿态与鼠标
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.ForceStance(false);
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_demo_v3.txt"), sb.ToString());
    Debug.Log("[q_demo_v3] done");
    yield return null;
}

string Seg(string name, float dur, InkWash.Player.CombatStance st, Animator an, InkWash.Player.PlayerController pc)
{
    string idle = st != null ? st.CurrentIdleClipName : "?";
    string mount = st != null ? st.WeaponMountPath : "?";
    string stt = "<n/a>";
    var si = an.GetCurrentAnimatorStateInfo(0);
    if (si.IsName("Run")) stt = "Run";
    else if (si.IsName("Walk")) stt = "Walk";
    else if (si.IsName("Idle")) stt = "Idle";
    else if (si.IsName("Atk1")) stt = "Atk1";
    else if (si.IsName("Atk1Rec")) stt = "Atk1Rec";
    else if (si.IsName("Atk2")) stt = "Atk2";
    else if (si.IsName("Atk2Rec")) stt = "Atk2Rec";
    else if (si.IsName("Atk3")) stt = "Atk3";
    else if (si.IsName("Dash")) stt = "Dash";
    return string.Format("{0,-16} {1,4:F1}s   {2,-8} {3,-18} {4,-14} {5:F2}  [{6}]",
        name, dur, st != null ? st.InCombat.ToString() : "?", idle, stt, pc != null ? pc.CurrentSpeed : 0f, mount);
}

return Body();
