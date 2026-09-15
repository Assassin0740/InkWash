// q_fist2.cs —— 量「攻击时手为什么是张开的」
//
//   对 Idle / Atk1 / Atk2 / Atk3 四个状态 × 三个归一化时间点，
//   逐状态打印左右手四指的关节夹角与「中指指尖到腕」距离，并对比三种补丁配置：
//     C1 现状（右手卷、左手不卷）
//     C2 左手也卷（满力 fingerCurl = 70/85/55）
//     C3 左手也卷（六成力 = 48/58/38，做成"松拳"）
//   同时出近景图。指标是**帧率无关**的关节夹角，不看目测。
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/hand_probe");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_fist2] no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponent<InkWash.Player.CombatStance>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (pose == null || anim == null) { Debug.LogError("[q_fist2] 缺组件"); yield break; }

    // ---------- 前置：真进一次战斗，让 CombatStance 把武器搬到手上、补丁打开 ----------
    for (int i = 0; i < 180 && !stance.IsReady; i++) yield return null;
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.RequestInjectedAttack();
    for (int i = 0; i < 90 && !stance.InCombat; i++) yield return null;
    ctl.EndInputOverride();

    sb.AppendLine("========== 前置 ==========");
    sb.AppendLine("  InCombat=" + stance.InCombat + "  武器挂点=" + stance.WeaponMountPath
        + "  补丁 enabled=" + pose.enabled + "  Armed=" + pose.Armed
        + "  rightHand=" + pose.rightHand + "  leftHand=" + pose.leftHand);
    sb.AppendLine("  说明：Armed 只在「真拿到武器」时为 true；补丁 enabled 由 CombatStance 控制。");
    sb.AppendLine();

    stance.combatExitDelay = 9999f;   // 运行时锁住，测试期间别自动收剑
    ctl.enabled = false;              // 别跟 anim.Play 抢状态（沿用 w_shot_play2 的做法）
    if (ik != null) ik.enabled = false;

    var rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);

    // ---- 量一只手：四指的（近节 / 中节）关节夹角 + 中指指尖到腕 ----
    var measure = new System.Func<Transform, string>((hand) =>
    {
        if (hand == null) return "hand=null";
        string[] keys = { "Index", "Mid", "Ring", "Pinky" };
        float s1 = 0f, s2 = 0f; int n = 0; float midTipDist = -1f;
        var all = hand.GetComponentsInChildren<Transform>(true);
        foreach (var k in keys)
        {
            Transform root = null;
            foreach (var t in all)
            {
                if (!t.name.Contains(k)) continue;
                if (t.name.Contains("Toe")) continue;            // CC_Base 把脚趾也叫 Index/MidToe1
                if (!t.IsChildOf(hand)) continue;
                if (t.name.EndsWith("1")) { root = t; break; }
            }
            if (root == null || root.childCount == 0) continue;
            Transform p1 = root.GetChild(0);
            if (p1.childCount == 0) continue;
            Transform p2 = p1.GetChild(0);
            Vector3 a = p1.position - root.position;
            Vector3 b = p2.position - p1.position;
            float j1 = Vector3.Angle(a, b);
            float j2 = p2.childCount > 0 ? Vector3.Angle(b, p2.GetChild(0).position - p2.position) : 0f;
            s1 += j1; s2 += j2; n++;
            if (k == "Mid")
            {
                Transform tip = p2.childCount > 0 ? p2.GetChild(0) : p2;
                midTipDist = (tip.position - hand.position).magnitude;
            }
        }
        if (n == 0) return "找不到指链";
        return string.Format("近节={0,5:F1}°  中节={1,5:F1}°  中指指尖到腕={2:F3}m  (指链 {3} 条)",
            s1 / n, s2 / n, midTipDist, n);
    });

    // ---- 临时相机（近景） ----
    var camGo = new GameObject("TmpFistCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged";
    tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);

    string[] states = { "Idle", "Atk1", "Atk2", "Atk3" };
    float[] times = { 0.20f, 0.50f, 0.80f };
    string[] names = { "C1 现状（右卷 / 左不卷）", "C2 左手也卷（满力 70/85/55）", "C3 左手也卷（六成力 48/58/38）" };

    Vector3 baseFinger = pose.fingerCurl, baseThumb = pose.thumbCurl;
    Vector3 softFinger = new Vector3(48f, 58f, 38f), softThumb = new Vector3(20f, 20f, 16f);

    for (int pass = 0; pass < 3; pass++)
    {
        pose.rightHand = true;
        pose.leftHand = pass >= 1;
        pose.fingerCurl = pass == 2 ? softFinger : baseFinger;
        pose.thumbCurl = pass == 2 ? softThumb : baseThumb;

        sb.AppendLine("==================== " + names[pass] + " ====================");
        sb.AppendLine("  rightHand=" + pose.rightHand + "  leftHand=" + pose.leftHand
            + "  fingerCurl=" + pose.fingerCurl + "  thumbCurl=" + pose.thumbCurl);

        foreach (var st in states)
        {
            sb.AppendLine("  [" + st + "]");
            foreach (var nt in times)
            {
                anim.Play("Idle", 0, 0f);
                anim.Update(1f / 60f);
                anim.Play(st, 0, nt);
                anim.Update(1f / 60f);
                yield return null;
                yield return null;

                var cur = anim.GetCurrentAnimatorClipInfo(0);
                string clip = cur.Length > 0 ? cur[0].clip.name : "<空>";
                var info = anim.GetCurrentAnimatorStateInfo(0);
                sb.AppendLine(string.Format("    t={0:F2}  状态={1,-10} 片段={2,-26} 本帧卷={3}根  Armed={4}",
                    nt, info.IsName(st) ? st : "<被抢>", clip, pose.CurledBoneCount, pose.Armed));
                sb.AppendLine(string.Format("      符号 右={0:+0;-0} 左={1:+0;-0}   握拳度 右={2:F2} 左={3:F2}   (五指摊开≈2.2 / 握拳≈1.2)",
                    pose.RightCurlSign, pose.LeftCurlSign, pose.RightFistRatio, pose.LeftFistRatio));
                sb.AppendLine("       右: " + measure(rHand));
                sb.AppendLine("       左: " + measure(lHand));

                // 出图：Atk1 t=0.5 拍左右手近景（C1 与 C3 各一遍，方便并排比）
                if (st == "Atk1" && Mathf.Abs(nt - 0.5f) < 0.01f && (pass == 0 || pass == 2))
                {
                    string tag = pass == 0 ? "C1" : "C3";
                    Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
                    Vector3 rt2 = go.transform.right; rt2.y = 0f; rt2.Normalize();
                    yield return CloseUp(tcam, rHand, System.IO.Path.Combine(imgDir, tag + "_Rhand_atk1.png"),
                        (rt2 * 0.7f - fwd * 0.9f + Vector3.up * 0.35f).normalized);
                    yield return CloseUp(tcam, lHand, System.IO.Path.Combine(imgDir, tag + "_Lhand_atk1.png"),
                        (-rt2 * 0.7f - fwd * 0.9f + Vector3.up * 0.35f).normalized);
                }
            }
        }
        sb.AppendLine();
    }

    // ---------- 还原运行时配置（资产本身没动过）----------
    pose.rightHand = true; pose.leftHand = false;
    pose.fingerCurl = baseFinger; pose.thumbCurl = baseThumb;
    if (ik != null) ik.enabled = true;
    if (ctl != null) ctl.enabled = true;
    Object.Destroy(camGo);

    sb.AppendLine(">>> 全部跑完；运行时配置已还原为资产值（rightHand=1 / leftHand=0 / fingerCurl=" + baseFinger + "）");
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_fist2.txt"), sb.ToString());
    Debug.Log("[q_fist2] done");
    yield return null;
}

// 把相机怼到某根骨骼上拍近景（正交，尺寸 0.20 m）
IEnumerator CloseUp(Camera cam, Transform target, string path, Vector3 viewDir)
{
    if (target == null) yield break;
    Vector3 focus = target.position;
    cam.orthographic = true;
    cam.orthographicSize = 0.20f;
    cam.transform.position = focus + viewDir * 1.5f;
    cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);
    var rt = RenderTexture.GetTemporary(560, 560, 24);
    cam.targetTexture = rt;
    cam.Render();
    cam.targetTexture = null;
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var tex = new Texture2D(560, 560, TextureFormat.RGB24, false);
    tex.ReadPixels(new Rect(0, 0, 560, 560), 0, 0);
    tex.Apply();
    RenderTexture.active = prev;
    RenderTexture.ReleaseTemporary(rt);
    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
    Object.Destroy(tex);
    yield return null;
}

return Body();
