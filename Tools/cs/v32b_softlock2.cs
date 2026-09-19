// v32b_softlock2.cs —— 软锁冒烟 v2：帧驱动 + 速率统计（分离"逻辑没生效"与"编辑器失焦没 tick"）
using System;
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    if (run == null) { Debug.Log("[v32b2] FAIL: RunManager 不在场"); yield break; }

    Application.runInBackground = true;   // 编辑器失焦也别停 Player Loop

    int guard = 0;
    if (run.State != InkWash.Roguelike.RunState.Playing)
    {
        run.StartRun();
        while (run.State != InkWash.Roguelike.RunState.Playing && guard++ < 900) yield return null;
    }
    if (run.State != InkWash.Roguelike.RunState.Playing) { Debug.Log("[v32b2] FAIL 未进 Playing"); yield break; }

    var cam = Object.FindObjectOfType<InkWash.CameraRig.ThirdPersonCamera>();
    if (cam == null) { Debug.Log("[v32b2] FAIL: 无相机"); yield break; }

    InkWash.Enemies.EnemyBase enemy = null;
    guard = 0;
    while (enemy == null && guard++ < 480)
    {
        float best = float.MaxValue;
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
        {
            if (!e.IsAlive) continue;
            float d = Vector3.Distance(e.Transform.position, cam.target.position);
            if (d < best) { best = d; enemy = e; }
        }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { Debug.Log("[v32b2] SKIP: 场景无敌人"); yield break; }

    Vector3 aim = cam.target.position + cam.pivotOffset;
    Vector3 to = enemy.Transform.position + Vector3.up * 1.2f - aim; to.y = 0f;
    float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;

    cam.yaw = Mathf.DeltaAngle(0f, want) >= 0f ? want - 25f : want + 25f;
    float yaw0 = cam.yaw;

    // 帧驱动：最多 600 帧，每帧记录累计 scaled 时间与 yaw
    float accT = 0f;
    int frames = 0;
    bool pass = false;
    while (frames < 600)
    {
        yield return null;
        accT += Time.deltaTime;
        frames++;
        float errNow = Mathf.Abs(Mathf.DeltaAngle(cam.Yaw, want));
        if (errNow < 5f) { pass = true; break; }
    }

    float err = Mathf.Abs(Mathf.DeltaAngle(cam.Yaw, want));
    float rate = Mathf.Abs(Mathf.DeltaAngle(yaw0, cam.Yaw)) / Mathf.Max(accT, 1e-4f);
    sb.Append("enemy=").Append(enemy.name)
      .Append(" frames=").Append(frames)
      .Append(" scaledT=").Append(accT.ToString("F2"))
      .Append(" want=").Append(want.ToString("F1"))
      .Append(" yaw0=").Append(yaw0.ToString("F1"))
      .Append(" yawEnd=").Append(cam.Yaw.ToString("F1"))
      .Append(" err=").Append(err.ToString("F2"))
      .Append(" rate=").Append(rate.ToString("F0")).Append("deg/s")
      .Append(" lockTgt=").Append(cam.SoftLockTarget != null ? cam.SoftLockTarget.name : "NONE")
      .Append(" timeScale=").Append(Time.timeScale.ToString("F2"))
      .Append(" => ").Append(pass ? "PASS" : "FAIL");
    Debug.Log("[v32b2] " + sb);
    yield return null;
}
return Body();
