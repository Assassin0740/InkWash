// q_blade.cs —— 持剑待机：剑相对头/脸的位置（整个循环扫一遍）+ 用真正游戏相机出图
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pose_idle");
    Directory.CreateDirectory(imgDir);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);

    var head = anim.GetBoneTransform(HumanBodyBones.Head);
    var neck = anim.GetBoneTransform(HumanBodyBones.Neck);
    var rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var rLower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

    if (stance != null) stance.ForceStance(true);
    float t0 = Time.time;
    while (Time.time - t0 < 1.0f) yield return null;

    var weapon = vfx != null ? vfx.WeaponInstance : null;
    if (weapon == null) { sb.AppendLine("★ 武器没拿到"); File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_blade.txt"), sb.ToString()); yield break; }

    Renderer mr = weapon.GetComponentInChildren<MeshRenderer>();
    if (mr == null) mr = weapon.GetComponentInChildren<Renderer>();
    var lb = mr != null ? mr.localBounds : new Bounds();
    float yScale = weapon.transform.lossyScale.y;

    sb.AppendLine("=== 轴向探明（判断哪根轴是剑身）===");
    sb.AppendLine("weapon.up      = " + V(weapon.transform.up));
    sb.AppendLine("weapon.forward = " + V(weapon.transform.forward));
    sb.AppendLine("mesh localBounds size = " + V(lb.size) + "  min=" + V(lb.min) + " max=" + V(lb.max));
    sb.AppendLine("bladeAnchor    = " + (vfx.bladeAnchor != null ? vfx.bladeAnchor.name + "  pos=" + V(vfx.bladeAnchor.position) + "  localPos=" + V(vfx.bladeAnchor.localPosition) : "<null>"));
    sb.AppendLine();
    sb.AppendLine("=== 整个待机循环（2.5 s，20 Hz）扫剑身线段 → 头骨/颈骨 最低距离 ===");
    sb.AppendLine("   t(s)   剑轴·up夹角   剑尖相对头骨(dx,dy,dz)    剑尖到头骨距离   剑身到头骨   剑身到颈骨");

    float minHead = 999f, minNeck = 999f, minTip = 999f;
    float angMin = 999f, angMax = -999f;
    float tStart = Time.time;
    int n = 0;
    float nextSample = 0f;
    while (Time.time - tStart < 2.55f)
    {
        float el = Time.time - tStart;
        if (el >= nextSample)
        {
            nextSample += 0.05f;
            // 剑轴：优先用 bladeAnchor 方向；拿不到就退回 up/floating
            Vector3 root = weapon.transform.position;
            Vector3 axis = weapon.transform.up;
            if (vfx.bladeAnchor != null && (vfx.bladeAnchor.position - root).sqrMagnitude > 1e-6f)
                axis = (vfx.bladeAnchor.position - root).normalized;

            Vector3 tip = root + axis * (lb.max.y * yScale);
            float ang = Vector3.Angle(axis, Vector3.up);
            if (ang < angMin) angMin = ang;
            if (ang > angMax) angMax = ang;

            float dHead = DistPointSeg(head.position, root, tip);
            float dNeck = DistPointSeg(neck.position, root, tip);
            float dTip = Vector3.Distance(tip, head.position);
            if (dHead < minHead) minHead = dHead;
            if (dNeck < minNeck) minNeck = dNeck;
            if (dTip < minTip) minTip = dTip;

            Vector3 rel = tip - head.position;
            sb.AppendLine(string.Format("  {0,5:F2}   {1,10:F1}°   ({2,7:F3},{3,7:F3},{4,7:F3})   {5,10:F3}   {6,9:F3}   {7,9:F3}",
                el, ang, rel.x, rel.y, rel.z, dTip, dHead, dNeck));
            n++;
        }
        yield return null;
    }

    sb.AppendLine();
    sb.AppendLine("★ 汇总：剑轴与竖直夹角范围 = " + angMin.ToString("F1") + "° ~ " + angMax.ToString("F1") + "°");
    sb.AppendLine("★ 剑身线段 → 头骨 全程最近 = " + minHead.ToString("F4") + " m   （人头半径≈0.10 m，<0.10 即真穿模）");
    sb.AppendLine("★ 剑身线段 → 颈骨 全程最近 = " + minNeck.ToString("F4") + " m");
    sb.AppendLine("★ 剑尖 → 头骨      全程最近 = " + minTip.ToString("F4") + " m");
    sb.AppendLine("样本数 = " + n);

    // ---------- 出图：先用真正的游戏相机（用户所见），再补三个正交视图 ----------
    {
        var camGo = new GameObject("TmpBladeCam");
        var tcam = camGo.AddComponent<Camera>();
        tcam.CopyFrom(Camera.main);
        tcam.tag = "Untagged"; tcam.enabled = false;
        tcam.clearFlags = CameraClearFlags.SolidColor;
        tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

        var keep = new System.Collections.Generic.HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
        var hidden = new System.Collections.Generic.List<Renderer>();
        foreach (var r in Object.FindObjectsOfType<Renderer>())
            if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

        int W = 340, H = 420;

        // ① 游戏相机原样
        {
            var rt = RenderTexture.GetTemporary(W * 2, H * 2, 24);
            var realCam = Camera.main;
            if (realCam != null)
            {
                realCam.targetTexture = rt;
                realCam.Render();
                realCam.targetTexture = null;
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tile = new Texture2D(W * 2, H * 2, TextureFormat.RGB24, false);
                tile.ReadPixels(new Rect(0, 0, W * 2, H * 2), 0, 0); tile.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(imgDir, "_gamecam.png"), tile.EncodeToPNG());
                Object.Destroy(tile);
                sb.AppendLine("已出图 _gamecam.png（游戏相机原样）");
            }
            RenderTexture.ReleaseTemporary(rt);
        }

        // ② 三个正交视图（框住全身 2.5 m）
        Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.25f, go.transform.position.z);
        Vector3[] dirs = { -go.transform.forward, go.transform.right, Vector3.up };
        string[] names = { "背视", "右侧视", "俯视" };
        var sheet = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
        for (int i = 0; i < 3; i++)
        {
            Vector3 cp = center + dirs[i] * 6f;
            tcam.orthographic = true; tcam.orthographicSize = 1.35f;
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(center - cp, i == 2 ? go.transform.forward : Vector3.up);
            var rt = RenderTexture.GetTemporary(W, H, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            sheet.SetPixels(i * W, 0, W, H, tile.GetPixels());
            Object.Destroy(tile);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_ortho_3view.png"), sheet.EncodeToPNG());
        Object.Destroy(sheet);
        sb.AppendLine("已出图 _ortho_3view.png（" + string.Join(" / ", names) + "）");

        foreach (var r in hidden) if (r != null) r.enabled = true;
        Object.Destroy(camGo);
    }

    if (stance != null) stance.ForceStance(false);
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_blade.txt"), sb.ToString());
    Debug.Log("[q_blade] done");
    yield return null;
}

static string V(Vector3 v) { return string.Format("({0:F3},{1:F3},{2:F3})", v.x, v.y, v.z); }
static float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
{
    Vector3 ab = b - a; float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return Vector3.Distance(p, a);
    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / L2);
    return Vector3.Distance(p, a + ab * t);
}

return Body();
