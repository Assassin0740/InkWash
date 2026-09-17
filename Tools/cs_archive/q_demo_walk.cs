// q_demo_walk.cs —— 移动演示：待机 1s → 走路 2.5s（验证循环）→ 复位 → 跑步 1.5s
//
// ★ 用「时间」驱动而不是帧数：编辑器里边录屏边跑，帧率会从 137fps 掉到 20~35fps，
//   按帧数等会把段长放大数倍（上一版走路段实际跑了 11.3s = 14m，直接撞墙，
//   跑步段 CurrentSpeed 变成 0.00）。
// ★ 段间复位角色位置：走路+跑步的总位移会超出 Arena，撞墙后角色贴着墙原地踏步。
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_demo_walk] no Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    var anim = go.GetComponent<Animator>();
    var cc = go.GetComponent<CharacterController>();
    if (ctl == null || rig == null) { Debug.LogError("[q_demo_walk] 缺组件"); yield break; }

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    rig.SetMouseLookEnabled(false);
    rig.SnapBehindTarget();
    rig.pitch = 5f;
    float t0 = Time.time;
    while (Time.time - t0 < 0.6f) yield return null;

    // ① 非战斗待机 1s（垂手 + 背剑）
    float t1 = Time.time;
    while (Time.time - t1 < 1.0f) yield return null;
    var ci = anim.GetCurrentAnimatorClipInfo(0);
    sb.AppendLine("① 待机片段 = " + (ci.Length > 0 ? ci[0].clip.name : "?")
        + "  isLooping=" + (ci.Length > 0 && ci[0].clip.isLooping));

    // ② 走路 2.5s
    ctl.SetInjectedMove(new Vector2(0f, 1f), false);
    float t2 = Time.time;
    while (Time.time - t2 < 0.30f) yield return null;            // 等状态切过去
    float ntA = anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
    string walkClip = "";
    while (Time.time - t2 < 2.50f)
    {
        var c = anim.GetCurrentAnimatorClipInfo(0);
        if (c.Length > 0) walkClip = c[0].clip.name;
        yield return null;
    }
    float ntB = anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
    sb.AppendLine("② 走路 2.5s 片段=" + walkClip + "  速度=" + ctl.CurrentSpeed.ToString("F2")
        + "  循环增量 " + (ntB - ntA).ToString("F2")
        + "（0.97s 一循环，(2.5-0.3)s 应 ≈2.3；停在 0 才是没循环）"
        + "  位移 " + Vector3.Distance(startPos, go.transform.position).ToString("F2") + "m");

    // ---- 复位（避免越走越远撞墙）----
    ctl.SetInjectedMove(Vector2.zero, false);
    if (cc != null) cc.enabled = false;
    go.transform.position = startPos;
    go.transform.rotation = startRot;
    if (cc != null) cc.enabled = true;
    float tr = Time.time;
    while (Time.time - tr < 0.4f) yield return null;

    // ③ 跑步 1.5s
    ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    float t3 = Time.time;
    while (Time.time - t3 < 0.25f) yield return null;
    float ntC = anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
    string runClip = "";
    while (Time.time - t3 < 1.50f)
    {
        var c = anim.GetCurrentAnimatorClipInfo(0);
        if (c.Length > 0) runClip = c[0].clip.name;
        yield return null;
    }
    float ntD = anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
    sb.AppendLine("③ 跑步 1.5s 片段=" + runClip + "  速度=" + ctl.CurrentSpeed.ToString("F2")
        + "  循环增量 " + (ntD - ntC).ToString("F2") + "（0.62s 一循环，(1.5-0.25)s 应 ≈2.0）"
        + "  位移 " + Vector3.Distance(startPos, go.transform.position).ToString("F2") + "m");

    ctl.SetInjectedMove(Vector2.zero, false);
    float te = Time.time;
    while (Time.time - te < 0.5f) yield return null;

    ctl.EndInputOverride();
    if (rig != null) rig.SetMouseLookEnabled(true);   // ★ 恢复鼠标视角
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_demo_walk.txt"), sb.ToString());
    Debug.Log("[q_demo_walk] done");
    yield return null;
}
return Body();
