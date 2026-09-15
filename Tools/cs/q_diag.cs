// q_diag.cs —— 三问题综合诊断：① 鼠标视角链路 ② 走路是否循环 ③ 状态切换是否硬切
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    // ================= A. 运行环境 =================
    sb.AppendLine("================ A. 运行环境 ================");
    sb.AppendLine("Application.isPlaying  = " + Application.isPlaying);
    sb.AppendLine("Time.timeScale         = " + Time.timeScale.ToString("F2"));
    sb.AppendLine("Time.frameCount        = " + Time.frameCount);
    sb.AppendLine("Cursor.lockState       = " + Cursor.lockState);
    sb.AppendLine("Cursor.visible         = " + Cursor.visible);
    sb.AppendLine("场景内 Camera 数       = " + Object.FindObjectsOfType<Camera>().Length);
    sb.AppendLine("Camera.main            = " + (Camera.main != null ? Camera.main.name : "<null>"));

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_diag] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var anim = go.GetComponent<Animator>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;

    // ================= B. 相机链路 =================
    sb.AppendLine();
    sb.AppendLine("================ B. 相机链路 ================");
    if (rig == null)
    {
        sb.AppendLine("rig = <null>  ← 相机上没有 ThirdPersonCamera 组件");
    }
    else
    {
        sb.AppendLine("enabled                = " + rig.enabled
            + "    activeInHierarchy = " + rig.gameObject.activeInHierarchy);
        sb.AppendLine("mouseLookEnabled       = " + rig.mouseLookEnabled
            + "    lockCursorOnStart = " + rig.lockCursorOnStart);
        sb.AppendLine("yaw / pitch            = " + rig.yaw.ToString("F2") + " / " + rig.pitch.ToString("F2"));
        sb.AppendLine("target                 = " + (rig.target != null ? rig.target.name : "<null>"));
    }

    // 输入轴可用性（轴没配置时 Input.GetAxis 会抛异常）
    sb.AppendLine();
    sb.AppendLine("---- 输入轴可用性 ----");
    string[] axes = { "Mouse X", "Mouse Y", "Horizontal", "Vertical" };
    foreach (var a in axes)
    {
        try { sb.AppendLine("  " + a.PadRight(12) + " = " + Input.GetAxis(a).ToString("F4") + "   OK"); }
        catch (System.Exception e) { sb.AppendLine("  " + a.PadRight(12) + " 抛异常: " + e.GetType().Name + " " + e.Message); }
    }

    // 相机是否真的吃 yaw：直接改 yaw，看下一帧 transform 有没有跟
    if (rig != null)
    {
        float y0 = rig.yaw;
        rig.yaw = y0 + 30f;
        yield return null; yield return null;
        sb.AppendLine("  改 yaw +30 后：相机 eulerAngles.y = " + rig.transform.eulerAngles.y.ToString("F1")
            + "（原本 " + y0.ToString("F1") + "，能变说明相机跟 yaw 的链路是通的）");
        rig.yaw = y0;
        yield return null;
    }

    // ================= C. 控制器所有片段的循环状态 =================
    sb.AppendLine();
    sb.AppendLine("================ C. 片段循环状态 ================");
    var rac = anim.runtimeAnimatorController;
    if (rac != null)
    {
        foreach (var c in rac.animationClips)
            sb.AppendLine("  " + c.name.PadRight(36) + " len=" + c.length.ToString("F3")
                + "  isLooping=" + c.isLooping);
    }

    // ================= D. 走路实测：normalizedTime 轨迹 =================
    sb.AppendLine();
    sb.AppendLine("================ D. 走路 4s 实测 ================");
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    if (rig != null) rig.SetMouseLookEnabled(false);
    for (int i = 0; i < 20; i++) yield return null;

    ctl.SetInjectedMove(new Vector2(0f, 1f), false);
    sb.AppendLine("  帧    状态     片段                    isLoop   normTime(循环应>1后回0)");
    float prevNorm = -1f;
    int loopBackCount = 0;
    for (int i = 0; i < 180; i++)
    {
        if (i % 12 == 0)
        {
            var si = anim.GetCurrentAnimatorStateInfo(0);
            var ci = anim.GetCurrentAnimatorClipInfo(0);
            string cn = ci.Length > 0 ? ci[0].clip.name : "?";
            bool lp = ci.Length > 0 && ci[0].clip.isLooping;
            float nt = si.normalizedTime;
            sb.AppendLine("  " + i.ToString().PadRight(5) + " " + cn.PadRight(24)
                + lp.ToString().PadRight(8) + nt.ToString("F3"));
            if (prevNorm >= 0f && nt < prevNorm - 0.1f) loopBackCount++;
            prevNorm = nt;
        }
        yield return null;
    }
    sb.AppendLine("  → 观测到 normTime 回绕次数 = " + loopBackCount
        + (loopBackCount == 0 ? "   ★ 没有回绕 = 片段不循环（播完停在末帧）" : "   （有回绕 = 正常循环）"));

    // ================= E. 连贯性：状态切换轨迹 =================
    sb.AppendLine();
    sb.AppendLine("================ E. 状态切换轨迹（走→跑） ================");
    ctl.SetInjectedMove(new Vector2(0f, 1f), true);   // 带 Shift = 跑
    string lastState = "";
    float lastSwitchT = Time.time;
    for (int i = 0; i < 120; i++)
    {
        var si = anim.GetCurrentAnimatorStateInfo(0);
        var ci = anim.GetCurrentAnimatorClipInfo(0);
        string cn = ci.Length > 0 ? ci[0].clip.name : "?";
        if (cn != lastState)
        {
            sb.AppendLine("  t=" + Time.time.ToString("F2") + "  → " + cn
                + "   normTime=" + si.normalizedTime.ToString("F2")
                + "   间隔=" + (Time.time - lastSwitchT).ToString("F2") + "s");
            lastState = cn;
            lastSwitchT = Time.time;
        }
        yield return null;
    }

    ctl.SetInjectedMove(Vector2.zero, false);
    for (int i = 0; i < 40; i++) yield return null;
    ctl.EndInputOverride();
    if (rig != null) rig.SetMouseLookEnabled(true);

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_diag.txt"), sb.ToString());
    Debug.Log("[q_diag] done");
    yield return null;
}
return Body();
