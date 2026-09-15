// q_demo_v2.cs —— 本轮修复的演示录屏（时间驱动；结尾还原姿态与鼠标）
//   0.0~1.8s  非战斗待机（垂手，剑在背）
//   1.8~2.2s  切战斗姿态（拔剑）
//   2.2~4.2s  持剑待机（新的 UAL1 Sword_Idle —— 剑不再插在脑门）
//   4.2~7.7s  跑步（新的烘焙「持剑跑」—— 剑不再抡）
//   7.7~9.0s  停下，回到持剑待机
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

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;

    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos;
        go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    sb.AppendLine("段                时长    InCombat  Idle片段            Run状态片段      速度");
    float t;

    // 1) 非战斗待机
    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(false);
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    t = Time.time; while (Time.time - t < 1.8f) yield return null;
    sb.AppendLine(Seg("1 非战斗待机", 1.8f, stance, anim, ctl));

    // 2) 拔剑
    if (stance != null) stance.ForceStance(true);
    t = Time.time; while (Time.time - t < 0.4f) yield return null;

    // 3) 持剑待机
    Park();
    t = Time.time; while (Time.time - t < 2.0f) yield return null;
    sb.AppendLine(Seg("3 持剑待机", 2.0f, stance, anim, ctl));

    // 4) 跑步（ForceStance 顺带把「6 秒自动收剑」计时清零）
    Park();
    if (stance != null) stance.ForceStance(true);
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    t = Time.time; while (Time.time - t < 3.5f) yield return null;
    sb.AppendLine(Seg("4 跑步", 3.5f, stance, anim, ctl));

    // 5) 停下
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    t = Time.time; while (Time.time - t < 1.4f) yield return null;
    sb.AppendLine(Seg("5 停回待机", 1.4f, stance, anim, ctl));

    // 收尾：还原姿态与鼠标
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.ForceStance(false);
    var cam = Camera.main;
    if (cam != null)
    {
        var rig = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
        if (rig != null) rig.SetMouseLookEnabled(true);
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_demo_v2.txt"), sb.ToString());
    Debug.Log("[q_demo_v2] done");
    yield return null;
}

string Seg(string name, float dur, InkWash.Player.CombatStance st, Animator an, InkWash.Player.PlayerController pc)
{
    string idle = st != null ? st.CurrentIdleClipName : "?";
    string mount = st != null ? st.WeaponMountPath : "?";
    string run = "<n/a>";
    var si = an.GetCurrentAnimatorStateInfo(0);
    if (si.IsName("Run") || si.IsName("Walk") || si.IsName("Idle")) run = si.IsName("Run") ? "Run" : (si.IsName("Walk") ? "Walk" : "Idle");
    return string.Format("{0,-16} {1,4:F1}s   {2,-8} {3,-18} {4,-16} {5:F2}  [{6}]",
        name, dur, st != null ? st.InCombat.ToString() : "?", idle, run, pc != null ? pc.CurrentSpeed : 0f, mount);
}

return Body();
