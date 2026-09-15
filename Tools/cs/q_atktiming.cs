// 新攻击片段的时序标定：剑尖速度峰值（= 命中时刻）、脚部穿地、左臂外展
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.ForceStance(true);
    yield return null;

    var lU = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
    var lL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);

    // 找武器网格 + 剑尖顶点（局部 -Y 最远点）
    MeshFilter wmf = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { wmf = f; break; }
    if (wmf == null) { Debug.LogError("no weapon mesh"); yield break; }
    int tipIdx = 0;
    var verts = wmf.sharedMesh.vertices;
    for (int i = 1; i < verts.Length; i++) if (verts[i].y < verts[tipIdx].y) tipIdx = i;
    sb.AppendLine("武器网格 " + wmf.name + " 顶点 " + verts.Length + "  剑尖局部=" + verts[tipIdx]);

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();

    string[] states = { "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    float dt = 1f / 60f;

    foreach (var stateName in states)
    {
        // 该状态实际播的片段长度
        float clipLen = 0f;
        int N = 120;
        var ctlInfo = anim.runtimeAnimatorController;
        // 用 60 帧步进扫 3 秒足够覆盖
        N = 180;

        Vector3 prevTip = Vector3.zero;
        bool hasPrev = false;
        var speeds = new float[N + 1];
        var lAb = new float[N + 1];
        var footY = new float[N + 1];

        for (int i = 0; i <= N; i++)
        {
            float nt = i / (float)N;
            anim.Play(stateName, 0, nt);
            anim.Update(0f);
            anim.Update(0f);

            Vector3 tip = wmf.transform.TransformPoint(verts[tipIdx]);
            if (hasPrev) speeds[i] = (tip - prevTip).magnitude / dt;
            prevTip = tip; hasPrev = true;

            Vector3 ld = (lL.position - lU.position).normalized;
            lAb[i] = Vector3.Angle(ld, -go.transform.up);

            float mn = float.MaxValue;
            foreach (var s in smrs)
            {
                s.BakeMesh(bake, true);
                var m = s.transform.localToWorldMatrix;
                foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mn) mn = y; }
            }
            footY[i] = mn - go.transform.position.y;
        }

        // 长度：找动画状态自身长度
        var st = anim.GetCurrentAnimatorStateInfo(0);
        clipLen = st.length;

        // 峰值（跳过前 10% 起手，避免起手甩动干扰）
        int skip = Mathf.RoundToInt(N * 0.10f);
        float peak = 0f; int peakI = 0;
        for (int i = skip; i <= N; i++) if (speeds[i] > peak) { peak = speeds[i]; peakI = i; }

        float lMax = 0f; int over = 0;
        for (int i = 0; i <= N; i++) { if (lAb[i] > lMax) lMax = lAb[i]; if (lAb[i] > 55f) over++; }
        float footMin = float.MaxValue, footMax = float.MinValue;
        for (int i = 0; i <= N; i++) { if (footY[i] < footMin) footMin = footY[i]; if (footY[i] > footMax) footMax = footY[i]; }

        sb.AppendLine();
        sb.AppendLine("════ " + stateName + "  状态长度=" + clipLen.ToString("F3") + "s ════");
        sb.AppendLine("  剑尖速度峰值 = " + peak.ToString("F2") + " m/s  @ nt=" + (peakI / (float)N).ToString("F3")
                      + "  (即 t=" + (peakI / (float)N * clipLen).ToString("F3") + "s)");
        sb.AppendLine("  左臂外展 峰值=" + lMax.ToString("F1") + "°  >55°占比=" + (100f * over / (N + 1)).ToString("F0") + "%");
        sb.AppendLine("  脚最低点 相对原点 min=" + footMin.ToString("+0.000;-0.000") + "m  max=" + footMax.ToString("+0.000;-0.000") + "m");
        sb.AppendLine("  速度曲线（每 10% 取一点）：");
        sb.Append("   ");
        for (int k = 0; k <= 10; k++)
        {
            int i = Mathf.RoundToInt(N * k / 10f);
            sb.Append(" " + (k * 10) + "%=" + speeds[i].ToString("F1"));
        }
        sb.AppendLine();
    }

    Object.Destroy(bake);
    anim.runtimeAnimatorController = anim.runtimeAnimatorController;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_atktiming.txt"), sb.ToString());
    Debug.Log("[q_atktiming] done");
    yield return null;
}
return Body();
