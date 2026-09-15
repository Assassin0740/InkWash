// q_off_scan.cs —— 活体扫描跑步的 BodyBase：BodyLift 只上抬不下降 ⇒ BodyBase 决定浮空量
//
// 原理：FootIK.AppliedBodyOffset = BodyBase + BodyLift，
//       BodyLift 的 need = (groundY + clearance) - low.y - BodyBase，为负时被 clamp 到 0 ⇒ 只会抬不会压。
//   因此「整段浮空」完全由 BodyBase 决定：BodyBase 越大越飘，越小越靠 BodyLift 动态贴地（但可能跟不上而穿地）。
//   本扫描就找「最低点仍 ≥ -0.02（IK 跟得上）」前提下**最小**的 BodyBase ⇒ 浮空最小。
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/offscan");
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
    var bakeMesh = new Mesh { name = "QScan_Bake" };
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

    // 扫描期间：把 main camera 交给临时相机拍斜视图
    var camGo = new GameObject("TmpScanCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main); tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor; tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
    var keepSet = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>()) if (r.enabled && !keepSet.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int CW = 520, CH = 340;
    Texture2D Shot(Vector3 center, Vector3 dir, float ortho)
    {
        Vector3 cp = center + dir * 3f;
        tcam.orthographic = true; tcam.orthographicSize = ortho;
        tcam.transform.position = cp; tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
        var rt = RenderTexture.GetTemporary(CW, CH, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tile = new Texture2D(CW, CH, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, CW, CH), 0, 0); tile.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return tile;
    }

    // 每档一张图：机位在角色斜前方地面高度，能看到角色脚与地面接触线
    var keys = new string[] { "Run", "Run02_Sprint_Carry" };
    float[] orig = new float[keys.Length];
    for (int i = 0; i < keys.Length; i++) orig[i] = LookupOffset(ik, keys[i]);
    float LookupOffset(InkWash.Player.FootIK k, string key)
    {
        if (k.stateOffsets == null) return 0f;
        for (int i = 0; i < k.stateOffsets.Length; i++) if (k.stateOffsets[i].state == key) return k.stateOffsets[i].y;
        return float.NaN;
    }
    void SetOffset(string key, float y)
    {
        var arr = ik.stateOffsets;
        for (int i = 0; i < arr.Length; i++) if (arr[i].state == key) { arr[i].y = y; ik.stateOffsets = arr; return; }
        var list = new List<InkWash.Player.FootIK.StateYOffset>(arr);
        list.Add(new InkWash.Player.FootIK.StateYOffset { state = key, y = y });
        ik.stateOffsets = list.ToArray();
    }

    sb.AppendLine("=== 扫描前 ===");
    sb.AppendLine("  groundClearance = " + ik.groundClearance.ToString("F4") + "   liftSmooth = " + ik.liftSmooth.ToString("F1") + "   maxLift = " + ik.maxLift.ToString("F2"));
    foreach (var k in keys) sb.AppendLine("  stateOffsets[\"" + k + "\"] = " + LookupOffset(ik, k).ToString("F4"));

    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    if (stance != null) stance.ForceStance(true);
    { float t = Time.time; while (Time.time - t < 1.5f) yield return null; }
    sb.AppendLine("  当前播放 = " + NowClip(anim));
    sb.AppendLine();

    float[] oks = { 0.04f, 0.10f, 0.15f, 0.20f, 0.25f };
    var rows = new List<string>();
    var tiles = new List<Texture2D>();
    var shotNames = new List<string>();

    foreach (float O in oks)
    {
        foreach (var k in keys) SetOffset(k, O);
        Park();
        { float t = Time.time; while (Time.time - t < 1.1f) yield return null; }   // 让 BodyLift 收敛

        var lo = new List<float>();        // 世界坐标：鞋底最低点 − 地面（含全部偏移）
        var lift = new List<float>();
        var basev = new List<float>();
        float t0 = Time.time; int n = 0;
        while (Time.time - t0 < 2.6f)
        {
            float gy; bool gok = GroundY(out gy);
            float low = LowestShoe();
            if (gok && low < float.MaxValue)
            {
                lo.Add(low - gy);
                lift.Add(ik.BodyLift); basev.Add(ik.BodyBase);
            }
            if (n == 20)
            {
                var fc = new Vector3(go.transform.position.x, go.transform.position.y + 0.55f, go.transform.position.z);
                var dir = (-go.transform.forward * 0.55f + go.transform.right * 0.72f + Vector3.up * 0.18f).normalized;
                tiles.Add(Shot(fc, dir, 1.05f));
                shotNames.Add(O.ToString("F2"));
            }
            n++;
            yield return null;
        }
        if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);

        var sr = lo.OrderBy(x => x).ToList();
        float mn = sr.Count > 0 ? sr[0] : 0f;
        float p10 = sr.Count > 0 ? sr[(int)(sr.Count * 0.10f)] : 0f;
        float md = sr.Count > 0 ? sr[sr.Count / 2] : 0f;
        float p90 = sr.Count > 0 ? sr[(int)(sr.Count * 0.90f)] : 0f;
        float mx = sr.Count > 0 ? sr[sr.Count - 1] : 0f;
        float pen = sr.Count > 0 ? sr.Count(x => x < -0.02f) * 100f / sr.Count : 0f;
        float floatPct = sr.Count > 0 ? sr.Count(x => x > 0.05f) * 100f / sr.Count : 0f;
        float liftMax = lift.Count > 0 ? lift.Max() : 0f;
        float liftAvg = lift.Count > 0 ? lift.Average() : 0f;
        rows.Add(string.Format("BodyBase={0:F2}  实测最低 {1,7:F3}  p10 {2,7:F3}  中位 {3,7:F3}  p90 {4,7:F3}  最高 {5,6:F3}  "
            + "穿地帧 {6,5:F1}%  浮空>5cm帧 {7,5:F1}%  BodyLift max {8:F3} 均值 {9:F3}",
            O, mn, p10, md, p90, mx, pen, floatPct, liftMax, liftAvg));
        sb.AppendLine(rows[rows.Count - 1]);
        { float t = Time.time; while (Time.time - t < 0.3f) yield return null; }
    }

    string NowClip(Animator a)
    {
        var b = new List<AnimatorClipInfo>(4); a.GetCurrentAnimatorClipInfo(0, b);
        return (b.Count > 0 && b[0].clip != null) ? b[0].clip.name : "?";
    }

    sb.AppendLine();
    sb.AppendLine("判读：");
    sb.AppendLine("  · 体检两条断言用的是「实测最低」（全循环最深帧）：需 ≥ -0.02（脚不穿地）且 ≤ +0.05（不浮空）。");
    sb.AppendLine("  · 「中位」才是用户看到的飘：BodyBase 越大中位越大（整段被抬起来）。");
    sb.AppendLine("  · BodyLift max 越大说明 IK 介入越多、越可能因 liftSmooth 滞后而在落地瞬间漏穿地。");
    sb.AppendLine("  ⇒ 选择：实测最低 ≥ -0.06 同时中位最小的档（配合实检复核）。");

    if (tiles.Count > 0)
    {
        var fig = new Texture2D(CW * tiles.Count, CH, TextureFormat.RGB24, false);
        for (int i = 0; i < tiles.Count; i++) fig.SetPixels(i * CW, 0, CW, CH, tiles[i].GetPixels());
        fig.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_off_scan.png"), fig.EncodeToPNG());
        Object.Destroy(fig);
        sb.AppendLine();
        sb.AppendLine("图 = _off_scan.png  从左到右 BodyBase = " + string.Join(" / ", shotNames) + "（同一斜视机位，比较角色与地面的高度关系）");
    }
    foreach (var t in tiles) Object.Destroy(t);

    // 还原原值
    for (int i = 0; i < keys.Length; i++) if (!float.IsNaN(orig[i])) SetOffset(keys[i], orig[i]);
    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo); Object.Destroy(bakeMesh);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = oldExit;
    Park();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_off_scan.txt"), sb.ToString());
    Debug.Log("[q_off_scan] done");
    yield return null;
}

return Body();
