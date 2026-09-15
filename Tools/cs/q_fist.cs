// q_fist.cs —— Play 模式：逐帧取证「攻击帧左手握拳度 2.601」到底出在哪一帧
//   假设：评测窗口用 IsName("Atk1") 判定，但 Idle→Atk1 的交叉淡化期间
//   GetCurrentAnimatorStateInfo(0) 已经报 Atk1 ⇒ 那些帧的**实际姿势是 Idle ⊕ Atk1 的混合**。
//   本轮把 Idle 槽位从 Feng_Idle_Loop 换成 Sword_Idle_Loop，混合结果就变了。
//   本脚本逐帧打印 IsInTransition / normalizedTime / 左右握拳度，定位峰值帧。
using System.Collections;
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

    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos;
        go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    if (ctl != null) ctl.BeginInputOverride();
    if (stance != null) { stance.ForceStance(true); }
    Park();

    // 静置 1s，让姿势稳下来（这一步也顺便让 Resolve() 跑完、spread 缓存定下来）
    float t = Time.time; while (Time.time - t < 1.0f) yield return null;

    sb.AppendLine("持剑待机 Idle 槽位片段 = " + (stance != null ? stance.CurrentIdleClipName : "?"));
    sb.AppendLine("pose.enabled=" + (pose != null ? pose.enabled.ToString() : "null")
                + "  Armed=" + (pose != null ? pose.Armed.ToString() : "?")
                + "  LeftCurlSign=" + (pose != null ? pose.LeftCurlSign.ToString("F2") : "?")
                + "  RightCurlSign=" + (pose != null ? pose.RightCurlSign.ToString("F2") : "?"));
    sb.AppendLine();

    // 记录淡化前的 Idle 基态
    {
        var buf = new System.Collections.Generic.List<AnimatorClipInfo>(4);
        anim.GetCurrentAnimatorClipInfo(0, buf);
        sb.AppendLine("清除攻击前 Idle 实际片段 = " + (buf.Count > 0 && buf[0].clip != null ? buf[0].clip.name : "?"));
    }
    sb.AppendLine();

    sb.AppendLine("  帧     t(s)  状态            淡化  归一t    左拳度   右拳度  卷指数  实际片段(layer0)");
    sb.AppendLine("------ ------- -------------- ------ -------- -------- ------- ------ ----------------");

    float peakL = -1f, peakR = -1f;
    float peakL_t = -1f, peakL_nt = -1f; bool peakL_trans = false, peakL_atk = false;
    string peakL_state = "?", peakL_clip = "?";
    float peakL_beforeL = -1f;

    var clipBuf = new System.Collections.Generic.List<AnimatorClipInfo>(4);
    int frame = 0;
    t = Time.time;
    bool fired = false;
    float prevL = -1f;

    while (Time.time - t < 1.6f)
    {
        if (!fired && Time.time - t >= 0.15f)
        {
            if (ctl != null) ctl.RequestInjectedAttack();
            fired = true;
        }
        var si = anim.GetCurrentAnimatorStateInfo(0);
        bool inTrans = anim.IsInTransition(0);
        clipBuf.Clear();
        anim.GetCurrentAnimatorClipInfo(0, clipBuf);
        string clip = (clipBuf.Count > 0 && clipBuf[0].clip != null) ? clipBuf[0].clip.name : "?";
        string stName = StateName(si);
        float L = pose != null ? pose.LeftFistRatio : -1f;
        float R = pose != null ? pose.RightFistRatio : -1f;

        // 只在「攻击状态帧」里比（和验收口径一致）
        bool isAtk = stName.StartsWith("Atk");
        if (isAtk && L > peakL)
        {
            peakL = L;
            peakL_t = Time.time - t; peakL_nt = si.normalizedTime;
            peakL_trans = inTrans; peakL_atk = isAtk;
            peakL_state = stName; peakL_clip = clip;
            peakL_beforeL = prevL;
        }
        if (isAtk && R > peakR) peakR = R;

        if (Time.time - t < 1.25f && frame % 1 == 0)
            sb.AppendLine(string.Format("{0,6} {1,7:F3} {2,-14} {3,-6} {4,8:F2} {5,8:F2} {6,7:F2} {7,6} {8}",
                frame, Time.time - t, stName, inTrans ? "淡化" : "稳定", si.normalizedTime,
                L, R, pose != null ? pose.CurledBoneCount : 0, clip));

        prevL = L;
        frame++;
        yield return null;
    }

    sb.AppendLine();
    sb.AppendLine("──────────────────────────────────────────────");
    sb.AppendLine(string.Format("攻击状态帧 左拳度峰值 = {0:F3}  （t={1:F3}s  归一t={2:F2}  状态={3}）",
        peakL, peakL_t, peakL_nt, peakL_state));
    sb.AppendLine(string.Format("     该帧是否处于状态过渡中 = {0}", peakL_trans ? "★ 是（姿势是混合姿态，不是纯攻击姿态）" : "否（纯攻击姿态）"));
    sb.AppendLine(string.Format("     该帧 layer0 实际混合到的片段 = {0}", peakL_clip));
    sb.AppendLine(string.Format("     该帧前一帧左拳度 = {0:F3}", peakL_beforeL));
    sb.AppendLine(string.Format("攻击状态帧 右拳度峰值 = {0:F3}", peakR));

    // 二次确认：单独统计「纯攻击姿态帧」（非过渡）
    yield return null;

    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.ForceStance(false);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_fist.txt"), sb.ToString());
    Debug.Log("[q_fist] done");
    yield return null;
}

string StateName(AnimatorStateInfo si)
{
    string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    for (int i = 0; i < names.Length; i++) if (si.IsName(names[i])) return names[i];
    return "?";
}

return Body();
