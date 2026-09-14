// Play 态剧照：刀身是 SwordVfx 在 Start() 里程序化生成的，只有 Play 态才存在。
// 逐一取 Idle / Walk / Run / Atk1 / Atk2 / Atk3 的动作瞬间，验证：
//   ① 刀身方向对不对（挂在右手骨、沿骨骼 +Y；CC_Base 骨架的轴向可能与 KayKit 不同）
//   ② 长袍与腿有没有穿插（用户最初提的穿模问题）
//   ③ 整体姿态在真实光照下是否正常
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/feng2");
    System.IO.Directory.CreateDirectory(imgDir);

    // 等 Start()（SwordVfx 建刀身）跑完
    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    // 刀身：报告它挂在哪、世界包围盒多大、刀尖在哪
    Transform blade = null;
    var stack = new Stack<Transform>();
    stack.Push(go.transform);
    while (stack.Count > 0)
    {
        var t = stack.Pop();
        if (t.name.Contains("Blade")) { blade = t; break; }
        foreach (Transform c in t) stack.Push(c);
    }
    if (blade == null) sb.AppendLine("[WARN] 找不到 PlaceholderBlade（placeholderBlade 关了？）");
    else
    {
        var mr = blade.GetComponent<MeshRenderer>();
        sb.AppendLine("刀身 = " + FullPath(blade, go.transform) + "  worldScale=" + blade.lossyScale.ToString("F4"));
        sb.AppendLine("  世界包围盒 size = " + (mr != null ? mr.bounds.size.ToString("F4") : "-") + "  center = " + (mr != null ? mr.bounds.center.ToString("F4") : "-"));
        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand != null)
            sb.AppendLine("  右手骨 worldPos = " + hand.position.ToString("F4") + "  手骨 +Y 世界方向 = " + hand.TransformDirection(Vector3.up).ToString("F3"));
        sb.AppendLine("  （刀长设定经 1/S 折算后 = 0.4745 局部，×lossyScale 后应≈1.05 世界米；竖直站姿时刀尖约在右手上方 1m）");
    }
    Debug.Log(sb.ToString());

    // 相机
    var camGo = new GameObject("TmpPlayCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged";
    tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.13f, 0.15f, 0.18f);
    tcam.fieldOfView = 35f;

    // 隐藏环境
    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) r.enabled = false;

    var bake = new Mesh();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    System.Func<float[]> bounds = () =>
    {
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (var s in smrs)
        {
            s.BakeMesh(bake, true);
            var m = s.transform.localToWorldMatrix;
            foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mn) mn = y; if (y > mx) mx = y; }
        }
        return new float[] { mn, mx };
    };

    // 四分之三侧视（45°），最容易看出刀身与长袍
    Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
    Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
    Vector3 dir45 = (fwd * 0.6f + right * 0.8f).normalized;

    string[] shots = {
        "Idle|0.30|play_idle",
        "Walk|0.10|play_walk",
        "Run|0.10|play_run",
        "Atk1|0.38|play_atk1",
        "Atk2|0.45|play_atk2",
        "Atk3|0.55|play_atk3",
    };

    foreach (var spec in shots)
    {
        var parts = spec.Split('|');
        string state = parts[0]; float nt = float.Parse(parts[1]); string name = parts[2];

        anim.Play(state, 0, nt);
        anim.Update(1f / 60f);
        yield return null;

        var b = bounds();
        Vector3 center = new Vector3(go.transform.position.x, (b[0] + b[1]) * 0.5f, go.transform.position.z);
        float h = b[1] - b[0];
        Vector3 camPos = center + dir45 * (h * 1.55f);
        tcam.transform.position = camPos;
        tcam.transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);

        int W = 512, H = 640;
        var rt = RenderTexture.GetTemporary(W, H, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var snap = new Texture2D(W, H, TextureFormat.RGB24, false);
        snap.ReadPixels(new Rect(0, 0, W, H), 0, 0); snap.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, name + ".png"), snap.EncodeToPNG());
        Object.Destroy(snap);
    }

    Object.Destroy(bake);
    Object.Destroy(camGo);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    Debug.Log("[shot-play] done -> " + imgDir);
    yield return null;
}
string FullPath(Transform t, Transform root)
{
    var s = t.name;
    while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

string FullPathTop(Transform t)
{
    var s = t.name;
    while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
    return s;
}

return Body();
