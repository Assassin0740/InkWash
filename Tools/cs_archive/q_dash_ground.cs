// q_dash_ground.cs —— 冲刺（Dash_Lunge）贴地核查
//
// 起因：Dash 的 motion 从 KayKit `Dodge_Forward` 换成了自建的 `Dash_Lunge`，
//       但 `FootIK.stateOffsets` 里只有 `Dash: -0.0041`（旧片段时期标的近零值），
//       而 `S2_pose` 的静态度量显示 Dash 整段最低点**比地面低 0.64 m**，
//       且该判定对 Dash 有豁免（`noGroundFixStates`）⇒ 这个洞不会被任何断言拦住。
//
//       项目硬规矩 #4：换片段必然重标 stateOffsets。本条当时漏了。
//
// 本探针在实机 Play 里逐帧取证：容器被抬到哪、网格真实最低点在哪、地面在哪。
// 摆到空旷地面 → 先跑 20 帧做基线 → 触发一次冲刺 → 逐帧记录 90 帧。
// 输出：Tools/reports/q_dash_ground.txt + Tools/screenshots/dash/dash_ground_*.png
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;
using InkWash.Roguelike;
using InkWash.UI;

IEnumerator Body()
{
    string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    string repPath = Path.Combine(root, "Tools/reports/q_dash_ground.txt");
    string shotDir = Path.Combine(root, "Tools/screenshots/dash");
    Directory.CreateDirectory(shotDir);
    Directory.CreateDirectory(Path.GetDirectoryName(repPath));

    var sb = new StringBuilder();
    sb.AppendLine("================ q_dash_ground（冲刺贴地核查）================");

    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[q_dash_ground] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var footIk = go.GetComponentInChildren<FootIK>(true);
    var anim = go.GetComponentInChildren<Animator>();
    var panel = Object.FindObjectOfType<InkStylePanel>();
    var choice = Object.FindObjectOfType<SkillChoicePanel>();
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    var room = Object.FindObjectOfType<RoomController>();
    var run = Object.FindObjectOfType<RunManager>();

    if (panel != null) panel.visible = false;
    if (choice != null) choice.Hide();
    ph.maxHealth = 100000f; ph.ResetHealth();
    if (spawner != null) { spawner.ResetForTest(); spawner.spawnInterval = 999f; }
    if (room != null) room.ResetForTest();
    if (run != null) { run.ResetForTest(); run.nextRoomDelay = 999f; }

    sb.AppendLine();
    sb.AppendLine("---------- FootIK 状态 ----------");
    if (footIk == null) sb.AppendLine("  !! 没找到 FootIK");
    else
    {
        sb.AppendLine("  enableIk = " + footIk.enableIk);
        sb.AppendLine("  noGroundFixStates = [" + string.Join(", ", footIk.noGroundFixStates) + "]"
            + "   ← 列在这里的状态只吃基准偏移、不做帧内贴地");
        sb.AppendLine("  OffsetTarget = " + (footIk.OffsetTarget != null ? footIk.OffsetTarget.name : "null"));
        sb.AppendLine("  stateOffsets（查表顺序：先按**片段名**，再按**状态名**）：");
        if (footIk.stateOffsets != null)
            foreach (var o in footIk.stateOffsets) sb.AppendLine("    " + o.state + "  y=" + o.y.ToString("F4"));
    }

    if (anim != null && anim.runtimeAnimatorController != null)
    {
        sb.AppendLine();
        sb.AppendLine("---------- 控制器里名字含 Dash/Lunge 的片段 ----------");
        foreach (var c in anim.runtimeAnimatorController.animationClips)
            if (c.name.Contains("Dash") || c.name.Contains("Lunge"))
                sb.AppendLine("  " + c.name + "  len=" + c.length.ToString("F3"));
    }

    // ---- 摆位 ----
    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(0f, 0.05f, -8f);
    go.transform.rotation = Quaternion.identity;
    if (cc != null) cc.enabled = true;
    ctl.ResetToLocomotion();

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
    var bake = new Mesh();

    // ---- 跟随式侧视相机（冲刺会把角色往前带 5 m，固定机位跟不住）----
    var rcGo = new GameObject("RT_DashGroundCam");
    var rc = rcGo.AddComponent<Camera>();
    var live = Camera.main;
    if (live != null)
    {
        rc.fieldOfView = live.fieldOfView; rc.nearClipPlane = live.nearClipPlane;
        rc.farClipPlane = live.farClipPlane; rc.cullingMask = live.cullingMask;
    }
    rc.clearFlags = CameraClearFlags.SolidColor;
    rc.backgroundColor = new Color(0.86f, 0.86f, 0.86f, 1f);
    rc.enabled = false;

    var rows = new List<string>();
    int frame = 0;
    int dashStartFrame = -1;
    float runWorstRel = float.MaxValue, dashWorstRel = float.MaxValue, transWorstRel = float.MaxValue, dashWorstZ = 0f;
    int dashFrames = 0, transFrames = 0;

    System.Func<Camera, int, int, Texture2D> shoot = (c, W, H) =>
    {
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        var prevT = c.targetTexture;
        c.targetTexture = rt;
        c.Render();
        c.targetTexture = prevT;
        var prevA = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        t.Apply();
        RenderTexture.active = prevA;
        RenderTexture.ReleaseTemporary(rt);
        return t;
    };

    System.Action<string> sample = (tag) =>
    {
        Vector3 pos = go.transform.position;
        // 地面高度：必须**排除角色自身**。普通 Physics.Raycast 从头顶往下打，
        // 第一个命中的就是角色自己的 CharacterController 顶面（实测读到 1.7~2.0 m 的假地面），
        // 于是「最低点 − 地面」恒为 -1.7 量级的垃圾值。FootIK.ProbeGround 也是同样的取法。
        float gy = float.NaN;
        var hits = Physics.RaycastAll(pos + Vector3.up * 2.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        float bestGy = float.MinValue;
        for (int h = 0; h < hits.Length; h++)
        {
            var tr = hits[h].collider.transform;
            if (tr == go.transform || tr.IsChildOf(go.transform)) continue;
            if (hits[h].point.y > bestGy) { bestGy = hits[h].point.y; gy = bestGy; }
        }

        float low = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            smrs[k].BakeMesh(bake);
            var m = smrs[k].transform.localToWorldMatrix;
            var vs = bake.vertices;
            int n = vs.Length;
            for (int i = 0; i < n; i++) low = Mathf.Min(low, m.MultiplyPoint3x4(vs[i]).y);
        }

        float rel = low - gy;   // 负 = 穿地
        string offKey = footIk != null ? footIk.CurrentOffsetKey : "n/a";
        float offVal = footIk != null ? footIk.AppliedBodyOffset : 0f;
        float ctrLocalY = footIk != null && footIk.OffsetTarget != null ? footIk.OffsetTarget.localPosition.y : 0f;
        float lift = footIk != null ? footIk.BodyLift : 0f;
        float pen = footIk != null ? footIk.RawPenetration : 0f;
        bool hasG = footIk != null && footIk.HasGroundInfo;

        rows.Add(string.Format(
            "  {0,3}  {1,5:F2}  {2,-6}  {3,7:F3}  {4,8:F4}  {5,7:F3}  {6,6:F3}  {7,8:F3}  {8,8:F4}  {9,-16}  lift={10,7:F4}  pen={11,7:F4}  g={12}",
            frame, Time.time, tag, pos.y, ctrLocalY, low, gy, rel, offVal, offKey, lift, pen, hasG ? "Y" : "N"));

        if (tag == "Run" && rel < runWorstRel) runWorstRel = rel;
        if (tag == "Dash/过渡") { transFrames++; if (rel < transWorstRel) transWorstRel = rel; }
        else if (tag == "Dash") { dashFrames++; if (rel < dashWorstRel) { dashWorstRel = rel; dashWorstZ = pos.z; } }
    };

    // ---- 相机跟随 ----
    System.Action placeCam = () =>
    {
        Vector3 p = go.transform.position;
        rc.transform.position = p + new Vector3(3.5f, 1.05f, -0.2f);
        rc.transform.LookAt(p + new Vector3(0f, 0.75f, 0f));
    };

    sb.AppendLine();
    sb.AppendLine("列：帧号 | t | 阶段 | 根Y | 容器localY | 网格最低世界Y | 地面Y | 最低-地面 | 生效偏移 | 偏移键 | BodyLift | RawPenetration | 有地面信息");
    sb.AppendLine("----------------------------------------------------------------------");

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.up, true);

    // ---- 阶段 1：奔跑基线（20 帧）----
    for (int i = 0; i < 20; i++) { yield return null; frame++; placeCam(); sample("Run"); }

    placeCam();
    var texRun = shoot(rc, 640, 400);
    File.WriteAllBytes(Path.Combine(shotDir, "dash_ground_run.png"), texRun.EncodeToPNG());
    Object.Destroy(texRun);

    // ---- 阶段 2：触发冲刺 ----
    ctl.RequestInjectedDash();
    bool shotDash = false, shotTrans = false;
    for (int i = 0; i < 90; i++)
    {
        yield return null; frame++;
        placeCam();
        if (ctl.IsDashing && dashStartFrame < 0) dashStartFrame = frame;
        // 冲刺**头 6 帧**单独归一类：那是 Animator 正在混合、且 CombatStance 正在切武器挂点的区间，
        // 与稳态冲刺不是一回事（实测稳态贴地良好，而过渡帧的补偿会短暂撞上 maxLift 上限）。
        string tag = ctl.IsDashing
            ? ((frame - dashStartFrame) < 6 ? "Dash/过渡" : "Dash")
            : "post";
        sample(tag);

        if (ctl.IsDashing && !shotTrans && (frame - dashStartFrame) < 6)
        {
            shotTrans = true;
            var texT = shoot(rc, 640, 400);
            File.WriteAllBytes(Path.Combine(shotDir, "dash_ground_transition.png"), texT.EncodeToPNG());
            Object.Destroy(texT);
        }

        if (ctl.IsDashing && !shotDash && ctl.transform.position.z > -5.2f)
        {
            shotDash = true;
            var tex = shoot(rc, 640, 400);
            File.WriteAllBytes(Path.Combine(shotDir, "dash_ground_mid.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }

    ctl.SetInjectedMove(Vector2.zero, true);
    ctl.EndInputOverride();

    Object.Destroy(rcGo);
    Object.Destroy(bake);

    // ---- 只印变化的行，避免刷屏 ----
    sb.AppendLine();
    sb.AppendLine("（上面是逐帧全量，下面是每个阶段的首/中/末各一行摘要）");
    foreach (var r in rows)
    {
        string[] parts = r.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) continue;
        int fr = 0; int.TryParse(parts[0], out fr);
        if (fr == 1 || fr == 10 || fr == 20 || fr == 21 || fr == 26 || fr == 32 || fr == 40 || fr == 50 || fr == 70 || fr == 105)
            sb.AppendLine(r);
    }

    sb.AppendLine();
    sb.AppendLine("---------- 判定 ----------");
    sb.AppendLine("  奔跑基线（未冲刺）最低点相对地面的最差值 = " + runWorstRel.ToString("F3") + " m");
    sb.AppendLine("  冲刺**稳态**最低点相对地面的最差值 = " + dashWorstRel.ToString("F3") + " m"
        + "（出现在 z=" + dashWorstZ.ToString("F2") + "，共 " + dashFrames + " 帧）");
    sb.AppendLine("  冲刺**过渡**（头 6 帧：混合 + 切武器挂点）最差值 = " + transWorstRel.ToString("F3") + " m"
        + "（共 " + transFrames + " 帧）  ← 单列，因为它的 `BodyLift` 会撞 maxLift 上限，"
        + "与稳态不是同一套机制（看 dash_ground_transition.png）");
    bool ok = dashWorstRel >= -0.02f;
    sb.AppendLine("  判据：冲刺稳态也不该把人埋进地里（最低点 ≥ -0.02 m）⇒ " + (ok ? "**通过**" : "**不通过**"));
    if (!ok)
        sb.AppendLine("  ⇒ 需要把 " + (-dashWorstRel).ToString("F3") + " m 的抬升量补进 FootIK.stateOffsets 的 `Dash` 项。");

    sb.AppendLine();
    sb.AppendLine("================ end ================");

    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[q_dash_ground] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

return Body();
