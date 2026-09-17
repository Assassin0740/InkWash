// 换模型后的第一眼：三视图 + 半身特写 + 带地面的全身图。
// 取景参数与 s3_shot_player.cs 完全一致，便于和 Tools/screenshots/kaykit/char_*.png 做 A/B 对比。
// 度量用 BakeMesh（Renderer.bounds 对蒙皮网格不可靠，会虚高）。
using System.Collections;
using System.Collections.Generic;

string OUT_IMG = "Tools/screenshots/feng2";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, OUT_IMG);
    System.IO.Directory.CreateDirectory(imgDir);

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { Debug.LogError("no PlayerController"); yield break; }
    var root = ctl.transform;
    var mainCam = Camera.main;

    var ik = root.GetComponent<InkWash.Player.FootIK>();
    if (ik != null) ik.enabled = false;
    ctl.enabled = false;

    // ---- 用 BakeMesh 量真实世界包围盒 ----
    float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
    var bake = new Mesh();
    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    foreach (var s in smrs)
    {
        if (s.sharedMesh == null) continue;
        s.BakeMesh(bake, true);
        var l2w = s.transform.localToWorldMatrix;
        foreach (var v in bake.vertices)
        {
            var w = l2w.MultiplyPoint3x4(v);
            if (w.y < minY) minY = w.y; if (w.y > maxY) maxY = w.y;
            if (w.x < minX) minX = w.x; if (w.x > maxX) maxX = w.x;
        }
    }
    Object.Destroy(bake);

    float groundY = root.position.y;
    {
        var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f);
        float best = float.MinValue;
        foreach (var h in hits) { if (h.collider.transform.IsChildOf(root)) continue; if (h.point.y > best) best = h.point.y; }
        if (best > float.MinValue) groundY = best;
    }

    sb.AppendLine("root.position = " + root.position.ToString("F3"));
    sb.AppendLine("地面 Y        = " + groundY.ToString("F3"));
    sb.AppendLine(string.Format("BakeMesh 世界包围盒 y {0:F4}..{1:F4}  高 {2:F4}  脚底离地 {3:F4}", minY, maxY, maxY - minY, minY - groundY));
    sb.AppendLine(string.Format("宽 x {0:F4}..{1:F4} = {2:F4} (T 姿势臂展)", minX, maxX, maxX - minX));
    var vis = root.Find("Visual");
    if (vis != null) sb.AppendLine(string.Format("Visual localScale={0} localPos={1}", vis.localScale.ToString("F4"), vis.localPosition.ToString("F4")));
    var anim = root.GetComponent<Animator>();
    if (anim != null) sb.AppendLine("Animator.avatar = " + (anim.avatar == null ? "NULL" : anim.avatar.name));
    sb.AppendLine("蒙皮网格 " + smrs.Length + " 个：" + string.Join(",", System.Array.ConvertAll(smrs, s => s.name)));
    Debug.Log(sb.ToString());

    // ---- 相机 ----
    var camGo = new GameObject("TmpShotCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(mainCam);
    tcam.tag = "Untagged";
    tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.13f, 0.15f, 0.18f);
    tcam.fieldOfView = 35f;

    var keep = new HashSet<Renderer>(root.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();

    Vector3 facing = root.forward; facing.y = 0f;
    if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
    facing.Normalize();
    Vector3 rightAxis = Vector3.Cross(Vector3.up, facing).normalized;
    Vector3 center = new Vector3(root.position.x, (minY + maxY) * 0.5f, root.position.z);
    float height = maxY - minY;

    // ---- 1. 三视图（隐藏环境，与 KayKit 那版同取景）----
    HideEnv(hidden, keep);
    Capture(tcam, imgDir, "char_front", 512, 768, height * 1.6f, center, facing);
    yield return null;
    Capture(tcam, imgDir, "char_side", 512, 768, height * 1.6f, center, rightAxis);
    yield return null;
    Capture(tcam, imgDir, "char_back", 512, 768, height * 1.6f, center, -facing);
    yield return null;

    // 半身特写：模拟"角色占屏 43%"的近景观感
    Vector3 bustCenter = new Vector3(root.position.x, minY + height * 0.78f, root.position.z);
    Capture(tcam, imgDir, "char_bust", 512, 640, height * 0.62f, bustCenter, facing);
    yield return null;

    // ---- 2. 带环境（含地面）：看脚是否真的踩在地上 ----
    RestoreEnv(hidden);
    Vector3 fullCenter = new Vector3(root.position.x, (groundY + maxY) * 0.5f, root.position.z);
    Capture(tcam, imgDir, "char_ground", 640, 640, height * 2.0f, fullCenter, rightAxis);
    yield return null;

    Object.Destroy(camGo);
    ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    Debug.Log("[shot2] done -> " + imgDir);
}

void HideEnv(List<Renderer> hidden, HashSet<Renderer> keep)
{
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }
}

void RestoreEnv(List<Renderer> hidden)
{
    foreach (var r in hidden) if (r != null) r.enabled = true;
    hidden.Clear();
}

void Capture(Camera tcam, string imgDir, string name, int W, int H, float dist, Vector3 center, Vector3 dir)
{
    Vector3 camPos = center + dir.normalized * dist;
    tcam.transform.position = camPos;
    tcam.transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);

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

return Body();
