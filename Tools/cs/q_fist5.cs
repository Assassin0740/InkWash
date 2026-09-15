// 握拳度全场景扫描 + 反射读 WeaponHandPose 的解析结果。
// 目的：定位验收里「右 1.701」出现在哪个状态；并确认右手度量是不是"死的"。
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_fist5] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var anim = go.GetComponent<Animator>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);

    // ---- 反射读解析结果 ----
    var ty = typeof(InkWash.Effects.WeaponHandPose);
    FieldInfo F(string n) { return ty.GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance); }
    System.Func<string, string> Val = (n) => { var fi = F(n); return fi == null ? "<无此字段>" : (fi.GetValue(pose) == null ? "<null>" : fi.GetValue(pose).ToString()); };
    sb.AppendLine("=== WeaponHandPose 解析结果 ===");
    sb.AppendLine("rightHand(开关)=" + Val("rightHand") + "   leftHand(开关)=" + Val("leftHand"));
    sb.AppendLine("onlyWhenArmed=" + Val("onlyWhenArmed") + "   Armed=" + pose.Armed);
    sb.AppendLine("RightCurlSign=" + pose.RightCurlSign.ToString("F0") + "   LeftCurlSign=" + pose.LeftCurlSign.ToString("F0"));
    var rr = F("_rightRoots") != null ? (System.Collections.IList)F("_rightRoots").GetValue(pose) : null;
    var rb = F("_rightBones") != null ? (System.Collections.IList)F("_rightBones").GetValue(pose) : null;
    var lr = F("_leftRoots") != null ? (System.Collections.IList)F("_leftRoots").GetValue(pose) : null;
    var lb = F("_leftBones") != null ? (System.Collections.IList)F("_leftBones").GetValue(pose) : null;
    var names = new List<string>();
    if (rr != null) foreach (var o in rr) names.Add(((Transform)o).name);
    sb.AppendLine("_rightRoots=" + (rr != null ? rr.Count : -1) + " [" + string.Join(",", names.ToArray()) + "]   _rightBones=" + (rb != null ? rb.Count : -1));
    names.Clear();
    if (lr != null) foreach (var o in lr) names.Add(((Transform)o).name);
    sb.AppendLine("_leftRoots =" + (lr != null ? lr.Count : -1) + " [" + string.Join(",", names.ToArray()) + "]   _leftBones =" + (lb != null ? lb.Count : -1));
    sb.AppendLine("_rightSpread=" + Val("_rightSpread") + "   _leftSpread=" + Val("_leftSpread"));
    var mtR = F("_rightMidTip") != null ? (Transform)F("_rightMidTip").GetValue(pose) : null;
    var mtL = F("_leftMidTip") != null ? (Transform)F("_leftMidTip").GetValue(pose) : null;
    sb.AppendLine("_rightMidTip=" + (mtR != null ? mtR.name : "<null>") + "   _leftMidTip=" + (mtL != null ? mtL.name : "<null>"));
    sb.AppendLine("fingerCurl=" + Val("fingerCurl") + "  leftFingerCurl=" + Val("leftFingerCurl"));
    sb.AppendLine();

    string[] sns = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    System.Func<string> stateName = () =>
    {
        var st = anim.GetCurrentAnimatorStateInfo(0);
        foreach (var n in sns) if (st.IsName(n)) return n;
        return "?";
    };

    ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(true);
    for (int i = 0; i < 10; i++) yield return null;

    var byStateR = new Dictionary<string, float>();
    var byStateL = new Dictionary<string, float>();
    float maxR = -1f, maxL = -1f; string maxRState = "", maxLState = "";
    float maxRT = 0f, maxLT = 0f;
    int frame = 0;
    float t0 = Time.time;

    while (Time.time - t0 < 26f)
    {
        float t = Time.time - t0;
        // 时间表：0-4 站立 / 4-8 走 / 8-12 跑 / 12-16 走+冲刺 / 16-26 连击
        if (t < 4f) ctl.SetInjectedMove(Vector2.zero, false);
        else if (t < 8f) ctl.SetInjectedMove(new Vector2(0f, 1f), false);
        else if (t < 12f) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
        else if (t < 16f)
        {
            ctl.SetInjectedMove(new Vector2(0f, 1f), false);
            if (frame % 70 == 0) ctl.RequestInjectedDash();
        }
        else
        {
            ctl.SetInjectedMove(Vector2.zero, false);
            if (frame % 40 == 0) ctl.RequestInjectedAttack();   // 每 0.67s 按一次 → 接得上连击
        }

        string sn = stateName();
        if (pose.enabled && pose.CurledBoneCount > 0)
        {
            float r = pose.RightFistRatio, l = pose.LeftFistRatio;
            if (!byStateR.ContainsKey(sn) || r > byStateR[sn]) byStateR[sn] = r;
            if (!byStateL.ContainsKey(sn) || l > byStateL[sn]) byStateL[sn] = l;
            if (r > maxR) { maxR = r; maxRState = sn; maxRT = t; }
            if (l > maxL) { maxL = l; maxLState = sn; maxLT = t; }
        }
        frame++;
        yield return null;
    }

    ctl.EndInputOverride();

    sb.AppendLine("== 各状态下右手/左手握拳度的最大值 ==");
    foreach (var kv in byStateR)
        sb.AppendLine("   " + kv.Key.PadRight(9) + " R=" + kv.Value.ToString("F3")
            + "   L=" + (byStateL.ContainsKey(kv.Key) ? byStateL[kv.Key].ToString("F3") : "-"));
    sb.AppendLine();
    sb.AppendLine("右手峰值 " + maxR.ToString("F3") + " @ t=" + maxRT.ToString("F1") + "s [" + maxRState + "]");
    sb.AppendLine("左手峰值 " + maxL.ToString("F3") + " @ t=" + maxLT.ToString("F1") + "s [" + maxLState + "]");
    sb.AppendLine("（五指摊开≈2.2 / 握成拳≈1.2，验收阈值 1.7）");

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_fist5.txt"), sb.ToString());
    Debug.Log("[q_fist5] done");
    yield return null;
}
return Body();
