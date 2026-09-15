// q_clearance.cs —— 口径裁决：把「鞋底离地高度」拆成「动画本体 + IK 抬升」两个分量
//   目的：核实 q_verify_final 里「中位浮空」的打印是否把 BodyBase 重复计入。
//   做法：同一 Play 会话内，分别测「持剑待机」「跑步」，逐帧同时记录
//         live = 鞋底最低点 − 地面Y（含全部偏移，= 用户真正看到的高度）
//         applied = ik.AppliedBodyOffset（= BodyBase + BodyLift）
//         raw = live − applied（= 动画本体把鞋底放在哪）
//   判读：raw 的中位应与 BodyBase 无关（= 片段固有量）；live 的中位才是「高出来一格」。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    var cc = go.GetComponent<CharacterController>();

    float oldExit = stance != null ? stance.combatExitDelay : 6f;
    if (stance != null) stance.combatExitDelay = 99999f;

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;
    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos; go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }
    var weapon = vfx != null ? vfx.WeaponInstance : null;
    var weaponRends = weapon != null ? new HashSet<Renderer>(weapon.GetComponentsInChildren<Renderer>(true)) : new HashSet<Renderer>();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
        .Where(s => s != null && s.enabled && !weaponRends.Contains(s)).ToArray();

    var shoeMask = new Dictionary<SkinnedMeshRenderer, bool[]>();
    foreach (var s in smrs)
    {
        var mesh = s.sharedMesh; if (mesh == null) continue;
        var foots = new[] { Bone(HumanBodyBones.LeftFoot), Bone(HumanBodyBones.LeftToes),
                            Bone(HumanBodyBones.RightFoot), Bone(HumanBodyBones.RightToes) };
        var ids = new HashSet<int>();
        for (int i = 0; i < s.bones.Length; i++)
            if (s.bones[i] != null && foots.Any(f => f != null && f == s.bones[i])) ids.Add(i);
        var bw = mesh.boneWeights;
        var mask = new bool[mesh.vertexCount];
        for (int v = 0; v < mask.Length && v < bw.Length; v++)
        {
            var b = bw[v]; float w = 0f;
            if (ids.Contains(b.boneIndex0)) w += b.weight0;
            if (ids.Contains(b.boneIndex1)) w += b.weight1;
            if (ids.Contains(b.boneIndex2)) w += b.weight2;
            if (ids.Contains(b.boneIndex3)) w += b.weight3;
            mask[v] = w > 0.5f;
        }
        shoeMask[s] = mask;
    }
    var bakeMesh = new Mesh { name = "QC_Bake" };
    var vbuf = new List<Vector3>(16384);
    float LowestShoe()
    {
        float lo = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            var s = smrs[k];
            if (!shoeMask.TryGetValue(s, out var mk)) continue;
            s.BakeMesh(bakeMesh); bakeMesh.GetVertices(vbuf);
            var m = s.transform.localToWorldMatrix;
            for (int i = 0; i < vbuf.Count; i++)
            {
                if (!mk[i]) continue;
                float y = m.m10 * vbuf[i].x + m.m11 * vbuf[i].y + m.m12 * vbuf[i].z + m.m13;
                if (y < lo) lo = y;
            }
        }
        return lo;
    }
    bool GroundY(out float y)
    {
        y = go.transform.position.y; bool ok = false; float best = float.MinValue;
        var hits = Physics.RaycastAll(go.transform.position + Vector3.up * 1.0f, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var tr = hits[i].collider.transform;
            if (tr == go.transform || tr.IsChildOf(go.transform)) continue;
            if (hits[i].point.y > best) { best = hits[i].point.y; ok = true; }
        }
        if (ok) y = best;
        return ok;
    }

    void Report(string title, List<float> live, List<float> raw, List<float> app, List<float> ikProp)
    {
        var L = live.OrderBy(x => x).ToList();
        var R = raw.OrderBy(x => x).ToList();
        var A = app.OrderBy(x => x).ToList();
        var P = ikProp.OrderBy(x => x).ToList();
        float Pct(List<float> s, float p) { return s[Mathf.Clamp((int)(s.Count * p), 0, s.Count - 1)]; }
        sb.AppendLine("=== " + title + "（n=" + L.Count + " 帧）===");
        sb.AppendLine(string.Format("  live（鞋底−地面，含全部偏移） 最低 {0,7:F3}  p10 {1,7:F3}  中位 {2,7:F3}  p90 {3,7:F3}  最高 {4,6:F3}",
            L[0], Pct(L, 0.10f), Pct(L, 0.50f), Pct(L, 0.90f), L[L.Count - 1]));
        sb.AppendLine(string.Format("  applied（BodyBase+BodyLift）    最低 {0,7:F3}  中位 {1,7:F3}  最高 {2,6:F3}",
            A[0], Pct(A, 0.50f), A[A.Count - 1]));
        sb.AppendLine(string.Format("  raw = live − applied（动画本体）最低 {0,7:F3}  p10 {1,7:F3}  中位 {2,7:F3}  p90 {3,7:F3}  最高 {4,6:F3}",
            R[0], Pct(R, 0.10f), Pct(R, 0.50f), Pct(R, 0.90f), R[R.Count - 1]));
        sb.AppendLine(string.Format("  交叉验证：FootIK.LowestMeshY 与自算 BakeMesh 逐帧差  最大 {0:F4} m（应≈0）", ikProp.Count > 0 ? ikProp.Max() : -1f));
        sb.AppendLine();
    }

    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    if (stance != null) stance.ForceStance(true);
    { float t = Time.time; while (Time.time - t < 1.0f) yield return null; }

    // ---- ① 持剑待机 ----
    {
        sb.AppendLine("--- ① 持剑待机（Idle_Carry_A）---");
        sb.AppendLine("  BodyBase = " + ik.BodyBase.ToString("F4") + "   liftSmooth = " + ik.liftSmooth.ToString("F1"));
        var live = new List<float>(); var raw = new List<float>(); var app = new List<float>(); var dif = new List<float>();
        float t0 = Time.time;
        while (Time.time - t0 < 2.0f)
        {
            float gy; if (GroundY(out gy))
            {
                float sh = LowestShoe();
                if (sh < float.MaxValue)
                {
                    live.Add(sh - gy); app.Add(ik.AppliedBodyOffset); raw.Add(sh - gy - ik.AppliedBodyOffset);
                    if (ik.HasGroundInfo) dif.Add(Mathf.Abs(ik.LowestMeshY - sh));
                }
            }
            yield return null;
        }
        Report("持剑待机", live, raw, app, dif);
    }

    // ---- ② 跑步 ----
    Park();
    if (stance != null) stance.ForceStance(false);
    { float t = Time.time; while (Time.time - t < 0.5f) yield return null; }
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    { float t = Time.time; while (Time.time - t < 1.5f) yield return null; }   // 预热
    {
        sb.AppendLine("--- ② 跑步（Roll → 控制器 Run）---");
        sb.AppendLine("  BodyBase = " + ik.BodyBase.ToString("F4") + "   liftSmooth = " + ik.liftSmooth.ToString("F1"));
        var live = new List<float>(); var raw = new List<float>(); var app = new List<float>(); var dif = new List<float>();
        float t0 = Time.time;
        while (Time.time - t0 < 3.0f)
        {
            float gy; if (GroundY(out gy))
            {
                float sh = LowestShoe();
                if (sh < float.MaxValue)
                {
                    live.Add(sh - gy); app.Add(ik.AppliedBodyOffset); raw.Add(sh - gy - ik.AppliedBodyOffset);
                    if (ik.HasGroundInfo) dif.Add(Mathf.Abs(ik.LowestMeshY - sh));
                }
            }
            yield return null;
        }
        if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
        Report("跑步", live, raw, app, dif);
    }

    sb.AppendLine("判读：raw 的中位 = 片段固有（与 BodyBase 无关）；live 的中位 = 用户看到的「高出来一格」。");
    sb.AppendLine("      若 live 中位 ≈ raw 中位 + BodyBase，说明查询与加法同源（旧脚本的 0.174 即由此得出），口径自洽。");

    Object.Destroy(bakeMesh);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = oldExit;
    Park();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_clearance.txt"), sb.ToString());
    Debug.Log("[q_clearance] done");
    yield return null;
}

return Body();
