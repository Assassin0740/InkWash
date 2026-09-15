// 定位「握拳度峰值 1.701」出现在哪个动画状态。
// 验收的采信条件是 pose.enabled && CurledBoneCount>0，**没有限定在攻击状态** ——
// 本轮换了 Walk/Run 片段，战斗中的走路帧手部基础姿势变了，怀疑是它把峰值抬上去的。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_fist4] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var anim = go.GetComponent<Animator>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (ctl == null || anim == null || pose == null) { Debug.LogError("[q_fist4] 缺组件"); yield break; }

    string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    System.Func<string> stateName = () =>
    {
        var st = anim.GetCurrentAnimatorStateInfo(0);
        foreach (var n in names) if (st.IsName(n)) return n;
        var ci = anim.GetCurrentAnimatorClipInfo(0);
        return ci.Length > 0 ? "?" + ci[0].clip.name : "?";
    };

    ctl.BeginInputOverride();
    if (stance != null) stance.ForceStance(true);
    for (int i = 0; i < 10; i++) yield return null;

    var byState = new Dictionary<string, float>();   // 状态 → 该状态下观察到的最大握拳度（右手）
    float maxR = -1f, maxL = -1f; string maxRState = "", maxLState = "";
    var lines = new List<string>();
    int frame = 0;
    float t0 = Time.time;

    sb.AppendLine("时刻   状态       pose.en  Curl  握拳度R  握拳度L   CurrentSpeed");
    sb.AppendLine("-----------------------------------------------------------------");
    while (Time.time - t0 < 14f)
    {
        float t = Time.time - t0;
        // 前进（走路/跑步）+ 每 1.6 秒攻击一次，模拟验收的战斗 stage
        ctl.SetInjectedMove(new Vector2(0f, 1f), false);
        if (frame % 96 == 0) ctl.RequestInjectedAttack();

        string sn = stateName();
        bool inAtk = sn.StartsWith("Atk");
        if (pose.enabled && pose.CurledBoneCount > 0)
        {
            float r = pose.RightFistRatio, l = pose.LeftFistRatio;
            if (!byState.ContainsKey(sn) || r > byState[sn]) byState[sn] = r;
            if (r > maxR) { maxR = r; maxRState = sn; }
            if (l > maxL) { maxL = l; maxLState = sn; }
            if (r > 1.45f || l > 1.45f)
                lines.Add(string.Format("{0,5:F2}   {1,-9} {2}  {3,4}  {4,8:F3} {5,8:F3}   {6,6:F2}{7}",
                    t, sn, pose.enabled ? "Y" : "N", pose.CurledBoneCount, r, l,
                    ctl.CurrentSpeed, inAtk ? "" : "   ← 非攻击"));
        }
        frame++;
        yield return null;
    }

    ctl.EndInputOverride();

    sb.AppendLine("== 比值 > 1.45 的帧 ==");
    foreach (var s in lines) sb.AppendLine(s);
    sb.AppendLine();
    sb.AppendLine("== 各状态下右手握拳度的最大值 ==");
    foreach (var kv in byState) sb.AppendLine("   " + kv.Key.PadRight(9) + " " + kv.Value.ToString("F3"));
    sb.AppendLine();
    sb.AppendLine("右手峰值 " + maxR.ToString("F3") + " 出现在 [" + maxRState + "]");
    sb.AppendLine("左手峰值 " + maxL.ToString("F3") + " 出现在 [" + maxLState + "]");
    sb.AppendLine("（五指摊开≈2.2 / 握成拳≈1.2，验收阈值 1.7）");

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_fist4.txt"), sb.ToString());
    Debug.Log("[q_fist4] done");
    yield return null;
}
return Body();
