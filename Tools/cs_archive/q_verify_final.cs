// q_verify_final.cs —— 本轮最终验证（活体，用已落地的配置，不做任何 override）
//   ① 持剑待机：双脚横距/纵距/脚偏角（「跨立」判据）+ 剑→面部距离（「插脑门」判据）+ 大图
//   ② 跑步：鞋底最低点分布（最低/中位 —— 中位才是用户看到的「高出来一格」）+ 脚总偏角左右差
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/final");
    Directory.CreateDirectory(imgDir);
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
    var lf = Bone(HumanBodyBones.LeftFoot);
    var rf = Bone(HumanBodyBones.RightFoot);
    var hip = Bone(HumanBodyBones.Hips);
    var head = Bone(HumanBodyBones.Head);

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
    var bakeMesh = new Mesh { name = "QF_Bake" };
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
    // 剑到面部的最近距离（用武器的顶点）
    float SwordToFace()
    {
        if (weapon == null) return -1f;
        float best = float.MaxValue; Vector3 h = head.position + Vector3.up * 0.06f;
        foreach (var r in weapon.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                float d = Vector3.Distance(c, h);
                if (d < best) best = d;
            }
        }
        return best;
    }

    // 相机
    var camGo = new GameObject("TmpFinalCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main); tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor; tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);
    var keepSet = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>()) if (r.enabled && !keepSet.Contains(r)) { r.enabled = false; hidden.Add(r); }

    Texture2D Shot(Vector3 center, Vector3 dir, float ortho, int W, int H)
    {
        Vector3 cp = center + dir * 4f;
        tcam.orthographic = true; tcam.orthographicSize = ortho;
        tcam.transform.position = cp; tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
        var rt = RenderTexture.GetTemporary(W, H, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return tile;
    }
    Vector3 BodyC() { return new Vector3(go.transform.position.x, go.transform.position.y + 1.05f, go.transform.position.z); }
    Vector3 GameDir() { return (-go.transform.forward * 0.75f + go.transform.right * 0.55f + Vector3.up * 0.38f).normalized; }

    if (ctl != null) ctl.BeginInputOverride();
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    if (stance != null) stance.ForceStance(true);
    { float t = Time.time; while (Time.time - t < 1.0f) yield return null; }

    // ============ ① 持剑待机 ============
    sb.AppendLine("=== ① 持剑待机（combatIdleClip = " + (stance != null && stance.combatIdleClip != null ? stance.combatIdleClip.name : "?") + "）===");
    {
        var cbuf = new List<AnimatorClipInfo>(4); anim.GetCurrentAnimatorClipInfo(0, cbuf);
        sb.AppendLine("  实际播放 = " + ((cbuf.Count > 0 && cbuf[0].clip != null) ? cbuf[0].clip.name : "?"));
        var dx = new List<float>(); var dz = new List<float>(); var hy = new List<float>();
        var q0L = lf.rotation; var q0R = rf.rotation;
        var angL = new List<float>(); var angR = new List<float>();
        var sw = new List<float>();
        for (int i = 0; i < 30; i++)
        {
            dx.Add(Mathf.Abs(lf.position.x - rf.position.x));
            dz.Add(Mathf.Abs(lf.position.z - rf.position.z));
            hy.Add(hip.position.y);
            angL.Add(Quaternion.Angle(q0L, lf.rotation));
            angR.Add(Quaternion.Angle(q0R, rf.rotation));
            sw.Add(SwordToFace());
            yield return null;
        }
        sb.AppendLine(string.Format("  双脚横距 {0:F3} m   纵距 {1:F3} m   髋高 {2:F3} m   脚自身抖动 L {3:F2}° / R {4:F2}°   剑→面部 {5:F3} m",
            dx.Average(), dz.Average(), hy.Average(), angL.Max(), angR.Max(), sw.Average()));
        sb.AppendLine("  对照：旧 Sword_Idle_Loop 横距 0.599 / 纵距 0.090 / 髋高 0.901 / 脚偏角 L18.5° R22.9° / 剑→面部 0.833");
        sb.AppendLine("        非战斗待机        横距 0.526 / 纵距 0.289 / 髋高 0.993");
        sb.AppendLine("  判据：横距应接近 0.52、纵距接近 0.29（= 自然站姿），脚偏角应 < 5°（旧值 18~23° 就是「跨立+外八」）");
        sb.AppendLine();

        int W = 600, H = 780;
        var a = Shot(BodyC(), GameDir(), 1.40f, W, H);
        var b = Shot(BodyC(), go.transform.forward, 1.40f, W, H);
        var sh = new Texture2D(W * 2, H, TextureFormat.RGB24, false);
        sh.SetPixels(0, 0, W, H, a.GetPixels());
        sh.SetPixels(W, 0, W, H, b.GetPixels());
        sh.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_idle_combat_final.png"), sh.EncodeToPNG());
        Object.Destroy(sh); Object.Destroy(a); Object.Destroy(b);
        sb.AppendLine("  图 = _idle_combat_final.png（左=斜后上机位 / 右=正视）");
    }
    sb.AppendLine();

    // ============ ② 跑步 ============
    sb.AppendLine("=== ② 跑步（控制器 Run = " + "Run04_KI_Carry" + "）===");
    if (stance != null) stance.ForceStance(false);
    { float t = Time.time; while (Time.time - t < 0.4f) yield return null; }
    Park();
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    { float t = Time.time; while (Time.time - t < 1.8f) yield return null; }   // 预热让 BodyLift 收敛

    // 基准（站立姿势）脚旋转 —— 必须先停移动、等角色真正回到 Idle 再取，否则取到的是跑步中的姿态
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    { float t = Time.time; while (Time.time - t < 0.9f) yield return null; }
    Quaternion qIdleL = lf.rotation, qIdleR = rf.rotation;
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    { float t = Time.time; while (Time.time - t < 1.4f) yield return null; }

    {
        var cbuf = new List<AnimatorClipInfo>(4); anim.GetCurrentAnimatorClipInfo(0, cbuf);
        sb.AppendLine("  实际播放 = " + ((cbuf.Count > 0 && cbuf[0].clip != null) ? cbuf[0].clip.name : "?"));
        sb.AppendLine("  liftSmooth = " + ik.liftSmooth.ToString("F1") + "   Run 偏移 = " + ik.BodyBase.ToString("F4"));
        var lo = new List<float>(); var angL = new List<float>(); var angR = new List<float>();
        var shots = new List<Texture2D>();
        float t0 = Time.time; int n = 0;
        while (Time.time - t0 < 3.0f)
        {
            float gy; bool gok = GroundY(out gy);
            float low = LowestShoe();
            if (gok && low < float.MaxValue) lo.Add(low - gy);
            angL.Add(Quaternion.Angle(qIdleL, lf.rotation));
            angR.Add(Quaternion.Angle(qIdleR, rf.rotation));
            if (n % 23 == 0 && shots.Count < 6) shots.Add(Shot(BodyC(), GameDir(), 1.45f, 400, 520));
            n++;
            yield return null;
        }
        if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);

        var sr = lo.OrderBy(x => x).ToList();
        float mn = sr[0], p10 = sr[(int)(sr.Count * 0.10f)], md = sr[sr.Count / 2];
        float p90 = sr[(int)(sr.Count * 0.90f)], mx = sr[sr.Count - 1];
        float pen = sr.Count(x => x < -0.02f) * 100f / sr.Count;
        float flt = sr.Count(x => x > 0.05f) * 100f / sr.Count;
        sb.AppendLine(string.Format("  鞋底相对地面：最低 {0,7:F3}  p10 {1,7:F3}  中位 {2,7:F3}  p90 {3,7:F3}  最高 {4,6:F3}  穿地帧 {5:F1}%  浮空>5cm帧 {6:F1}%",
            mn, p10, md, p90, mx, pen, flt));
        sb.AppendLine(string.Format("  脚总偏角（相对非战斗站姿）：左脚 {0:F1}°  右脚 {1:F1}°  左右差 {2:F1}°",
            angL.Average(), angR.Average(), Mathf.Abs(angR.Average() - angL.Average())));
        sb.AppendLine();
        sb.AppendLine("  对照（修改前）：最低 -0.142  中位 +0.001（+偏移0.1735 ⇒ 中位帧浮空 0.174）  左右差 55.4°");
        sb.AppendLine("  目标：中位 + 偏移 = 浮空量应显著小于 0.174；左右差应 < 20°");
        sb.AppendLine(string.Format("  >>> 本帧中位浮空 = {0:F3} m（= 中位 {1:F3} + 偏移 {2:F3}）", md + ik.BodyBase, md, ik.BodyBase));

        if (shots.Count > 0)
        {
            var fig = new Texture2D(400 * shots.Count, 520, TextureFormat.RGB24, false);
            for (int i = 0; i < shots.Count; i++) fig.SetPixels(i * 400, 0, 400, 520, shots[i].GetPixels());
            fig.Apply();
            File.WriteAllBytes(Path.Combine(imgDir, "_run_final.png"), fig.EncodeToPNG());
            Object.Destroy(fig);
            sb.AppendLine("  图 = _run_final.png（游戏机位 6 帧）");
        }
        foreach (var t in shots) Object.Destroy(t);
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo); Object.Destroy(bakeMesh);
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.combatExitDelay = oldExit;
    Park();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_verify_final.txt"), sb.ToString());
    Debug.Log("[q_verify_final] done");
    yield return null;
}

return Body();
