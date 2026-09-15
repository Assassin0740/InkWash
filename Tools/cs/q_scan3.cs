// q_scan3.cs —— 二维扫描 (BodyBase, liftSmooth)：验证「把 IK 提速后能否用小 BodyBase 同时做到不飘且不穿地」
//
// 上一轮一维扫描的结论是「贴地」与「不飘」不可兼得：
//   BodyBase=0.04 → 中位 0.120（最不飘）但穿地帧 24.9%、实测最低 -0.127
//   BodyBase=0.25 → 实测最低 +0.001（不穿地）但中位 0.313（最飘）
//   原因：BodyLift 的 need 为负时被 clamp 到 0 ⇒ 只会抬不会压；而它又是平滑的（liftSmooth=14 ⇒ τ≈71ms），
//         跟不上跑步落地（≈100ms 内最低点突变 0.2m）⇒ 只能靠加大 BodyBase 提前把整段抬起来。
// 本实验：BodyBase 固定在小值，只提高 liftSmooth 让跟随变快，看「实测最低」能否被压回 ≥ -0.02。
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/scan3");
    Directory.CreateDirectory(imgDir);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var cc = go.GetComponent<CharacterController>();

    float oldExit = stance != null ? stance.combatExitDelay : 6f;
    if (stance != null) stance.combatExitDelay = 99999f;
    float oldLiftSmooth = ik.liftSmooth;

    Vector3 startPos = go.transform.position;
    Quaternion startRot = go.transform.rotation;
    void Park()
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos; go.transform.rotation = startRot;
        if (cc != null) cc.enabled = true;
    }

    Transform Bone(HumanBodyBones b) { return anim.GetBoneTransform(b); }
    var weapon = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    var wInst = weapon != null ? weapon.WeaponInstance : null;
    var weaponRends = wInst != null ? new HashSet<Renderer>(wInst.GetComponentsInChildren<Renderer>(true)) : new HashSet<Renderer>();
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
    var bakeMesh = new Mesh { name = "QS3_Bake" };
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

    var ovr = anim.runtimeAnimatorController as AnimatorOverrideController;
    AnimationClip runKey = null, runOrig = null;
    if (ovr != null)
    {
        var pr = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        ovr.GetOverrides(pr);
        foreach (var kv in pr) if (kv.Key != null && kv.Key.name.StartsWith("Run")) { runKey = kv.Key; runOrig = kv.Value; break; }
    }
    var target = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run04_KI_Carry.anim");
    if (ovr != null && runKey != null && target != null) ovr[runKey] = target;
    sb.AppendLine("Run 槽位 key = " + (runKey != null ? runKey.name : "?") + "  临时换成 " + (target != null ? target.name : "<缺>"));
    sb.AppendLine();

    void SetOffset(string key, float y)
    {
        var arr = ik.stateOffsets;
        for (int i = 0; i < arr.Length; i++) if (arr[i].state == key) { arr[i].y = y; ik.stateOffsets = arr; return; }
        var list = new List<InkWash.Player.FootIK.StateYOffset>(arr);
        list.Add(new InkWash.Player.FootIK.StateYOffset { state = key, y = y });
        ik.stateOffsets = list.ToArray();
    }
    float GetOffset(string key)
    {
        if (ik.stateOffsets == null) return 0f;
        for (int i = 0; i < ik.stateOffsets.Length; i++) if (ik.stateOffsets[i].state == key) return ik.stateOffsets[i].y;
        return float.NaN;
    }

    float origRun = GetOffset("Run");
    float origClip = GetOffset("Run04_KI_Carry");

    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    if (stance != null) stance.ForceStance(true);
    { float t = Time.time; while (Time.time - t < 1.5f) yield return null; }

    sb.AppendLine("组合                      实测最低    p10     中位     p90     最高   穿地帧%  浮空>5cm%  BodyLift均值/最大   抖动");
    sb.AppendLine("-------------------------------------------------------------------------------------------------------------------------");

    var combos = new (float baseY, float smooth)[]
    {
        (0.10f,  90f),
        (0.12f,  90f),
        (0.10f, 150f),
        (0.12f, 150f),
        (0.24f,  14f),
    };

    foreach (var cb in combos)
    {
        SetOffset("Run", cb.baseY);
        SetOffset("Run04_KI_Carry", cb.baseY);
        ik.liftSmooth = cb.smooth;
        Park();
        { float t = Time.time; while (Time.time - t < 1.2f) yield return null; }

        var lo = new List<float>(); var lift = new List<float>();
        float t0 = Time.time;
        while (Time.time - t0 < 2.6f)
        {
            float gy; bool gok = GroundY(out gy);
            float low = LowestShoe();
            if (gok && low < float.MaxValue) { lo.Add(low - gy); lift.Add(ik.BodyLift); }
            yield return null;
        }
        // 抖动量：相邻帧 BodyLift 差分的 RMS（m/帧）
        float jitter = 0f;
        if (lift.Count > 1)
        {
            double acc = 0; int cnt = 0;
            for (int i = 1; i < lift.Count; i++) { double d = lift[i] - lift[i - 1]; acc += d * d; cnt++; }
            jitter = cnt > 0 ? (float)System.Math.Sqrt(acc / cnt) : 0f;
        }
        if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);

        var sr = lo.OrderBy(x => x).ToList();
        float mn = sr.Count > 0 ? sr[0] : 0f;
        float p10 = sr.Count > 0 ? sr[(int)(sr.Count * 0.10f)] : 0f;
        float md = sr.Count > 0 ? sr[sr.Count / 2] : 0f;
        float p90 = sr.Count > 0 ? sr[(int)(sr.Count * 0.90f)] : 0f;
        float mx = sr.Count > 0 ? sr[sr.Count - 1] : 0f;
        float pen = sr.Count > 0 ? sr.Count(x => x < -0.02f) * 100f / sr.Count : 0f;
        float flt = sr.Count > 0 ? sr.Count(x => x > 0.05f) * 100f / sr.Count : 0f;
        float lAvg = lift.Count > 0 ? lift.Average() : 0f;
        float lMax = lift.Count > 0 ? lift.Max() : 0f;
        sb.AppendLine(string.Format("BodyBase={0:F2} liftSmooth={1,3:F0}   {2,7:F3}  {3,7:F3}  {4,7:F3}  {5,7:F3}  {6,6:F3}   {7,5:F1}    {8,5:F1}     {9:F3}/{10:F3}   抖动 {11:F4}",
            cb.baseY, cb.smooth, mn, p10, md, p90, mx, pen, flt, lAvg, lMax, jitter));
        { float t = Time.time; while (Time.time - t < 0.3f) yield return null; }
    }

    sb.AppendLine();
    sb.AppendLine("判读：若某档 liftSmooth 提高后「实测最低」回到 ≥ -0.02 而「中位」仍小，则该档为最优 —— ");
    sb.AppendLine("      即用小 BodyBase（不飘）+ 快速 IK（不穿地），推翻「二者不可兼得」。");

    // 还原
    if (ovr != null && runKey != null) ovr[runKey] = runOrig;
    ik.liftSmooth = oldLiftSmooth;
    SetOffset("Run", origRun);
    if (!float.IsNaN(origClip)) SetOffset("Run04_KI_Carry", origClip);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = oldExit;
    Park();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_scan3.txt"), sb.ToString());
    Debug.Log("[q_scan3] done");
    yield return null;
}

return Body();
