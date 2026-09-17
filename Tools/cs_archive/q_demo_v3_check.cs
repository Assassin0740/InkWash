// q_demo_v3_check.cs —— 只跑 q_demo_v3 的第 6 段（三段连击），打印逐帧状态时间线
//   目的：证明演示里那三次 RequestInjectedAttack 真的把连击推进到了第 3 段
//   （q_demo_v3 的分段表只在段末采样一次，看到的是收招后的 Idle，证明不了）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var anim = go.GetComponent<Animator>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    var cc = go.GetComponent<CharacterController>();

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;
    void Park() { if (cc != null) cc.enabled = false; go.transform.position = startPos; go.transform.rotation = startRot; if (cc != null) cc.enabled = true; }

    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(true);
    Park();
    float t = Time.time; while (Time.time - t < 1.0f) yield return null;

    var timeline = new List<string>();
    int maxStep = 0;
    float peakL = -1f, peakR = -1f;

    t = Time.time;
    while (Time.time - t < 3.2f)
    {
        float e = Time.time - t;
        if (e >= 0.15f && e < 0.16f) ctl.RequestInjectedAttack();
        if (e >= 0.72f && e < 0.73f) ctl.RequestInjectedAttack();
        if (e >= 1.35f && e < 1.36f) ctl.RequestInjectedAttack();

        string s = StateName(anim);
        if (timeline.Count == 0 || timeline[timeline.Count - 1] != s) timeline.Add(s);
        if (ctl.ComboStep > maxStep) maxStep = ctl.ComboStep;

        var si = anim.GetCurrentAnimatorStateInfo(0);
        bool atk = si.IsName("Atk1") || si.IsName("Atk1Rec") || si.IsName("Atk2") || si.IsName("Atk2Rec") || si.IsName("Atk3");
        if (atk && pose != null && pose.CurledBoneCount > 0)
        {
            if (pose.LeftFistRatio > peakL) peakL = pose.LeftFistRatio;
            if (pose.RightFistRatio > peakR) peakR = pose.RightFistRatio;
        }
        yield return null;
    }

    sb.AppendLine("连击状态时间线: " + string.Join(" -> ", timeline.ToArray()));
    sb.AppendLine("最高连击段位 = " + maxStep + "   （期望 3）");
    sb.AppendLine("攻击帧握拳度峰值 右 " + peakR.ToString("F3") + " / 左 " + peakL.ToString("F3") + "   （阈值 < 1.7）");

    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.ForceStance(false);
    var cam = Camera.main;
    if (cam != null) { var rig = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>(); if (rig != null) rig.SetMouseLookEnabled(true); }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_demo_v3_check.txt"), sb.ToString());
    Debug.Log("[q_demo_v3_check] done");
    yield return null;
}

string StateName(Animator anim)
{
    var si = anim.GetCurrentAnimatorStateInfo(0);
    string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    for (int i = 0; i < names.Length; i++) if (si.IsName(names[i])) return names[i];
    return "?";
}

return Body();
