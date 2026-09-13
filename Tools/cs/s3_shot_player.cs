// 换模型后的第一眼：量尺寸 + 拍正/侧/背三视图，确认落地与比例。
using System.Collections;

string OUT_IMG = "Tools/screenshots/kaykit";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, OUT_IMG);
    System.IO.Directory.CreateDirectory(imgDir);

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { Debug.LogError("no PlayerController"); yield break; }
    var root = ctl.transform;
    var anim = ctl.animator;
    var mainCam = Camera.main;

    // 量角色在世界里的实际包围盒
    float minY = float.MaxValue, maxY = float.MinValue;
    float minX = float.MaxValue, maxX = float.MinValue;
    foreach (var r in root.GetComponentsInChildren<Renderer>())
    {
        if (r is TrailRenderer) continue;
        minY = Mathf.Min(minY, r.bounds.min.y); maxY = Mathf.Max(maxY, r.bounds.max.y);
        minX = Mathf.Min(minX, r.bounds.min.x); maxX = Mathf.Max(maxX, r.bounds.max.x);
    }
    // 地面
    float groundY = root.position.y;
    var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f);
    float best = float.MinValue;
    foreach (var h in hits) { if (h.collider.transform.IsChildOf(root)) continue; if (h.point.y > best) best = h.point.y; }
    if (best > float.MinValue) groundY = best;

    sb.AppendLine("root.position = " + root.position.ToString("F3"));
    sb.AppendLine("地面 Y        = " + groundY.ToString("F3"));
    sb.AppendLine(string.Format("角色世界包围盒 y {0:F3}..{1:F3}  高 {2:F3}  离地 {3:F3}", minY, maxY, maxY - minY, minY - groundY));
    sb.AppendLine(string.Format("宽度 x {0:F3}..{1:F3} = {2:F3}", minX, maxX, maxX - minX));
    var cc = root.GetComponent<CharacterController>();
    if (cc != null) sb.AppendLine(string.Format("CharacterController 高 {0:F2} 半径 {1:F2} 中心 y {2:F2}", cc.height, cc.radius, cc.center.y));
    var vis = root.Find("Visual");
    if (vis != null) sb.AppendLine(string.Format("Visual localScale={0} localPos={1}", vis.localScale.ToString("F3"), vis.localPosition.ToString("F4")));
    Debug.Log(sb.ToString());

    // 相机
    var camGo = new GameObject("TmpShotCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(mainCam);
    tcam.tag = "Untagged";
    tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.13f, 0.15f, 0.18f);
    tcam.fieldOfView = 35f;

    var keep = new System.Collections.Generic.HashSet<Renderer>(root.GetComponentsInChildren<Renderer>());
    var hidden = new System.Collections.Generic.List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    string[] views = { "front", "side", "back" };
    int CW = 512, CH = 768;
    foreach (var v in views)
    {
        Vector3 facing = root.forward; facing.y = 0f;
        if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
        facing.Normalize();
        Vector3 rightAxis = Vector3.Cross(Vector3.up, facing).normalized;
        Vector3 center = new Vector3(root.position.x, (minY + maxY) * 0.5f, root.position.z);
        Vector3 dir = v == "front" ? facing : (v == "side" ? rightAxis : -facing);
        float dist = (maxY - minY) * 1.6f;
        Vector3 camPos = center + dir * dist;
        tcam.transform.position = camPos;
        tcam.transform.rotation = Quaternion.LookRotation(center - camPos, Vector3.up);

        var rt = RenderTexture.GetTemporary(CW, CH, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var snap = new Texture2D(CW, CH, TextureFormat.RGB24, false);
        snap.ReadPixels(new Rect(0, 0, CW, CH), 0, 0); snap.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "char_" + v + ".png"), snap.EncodeToPNG());
        Object.Destroy(snap);
        yield return null;
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);
    Debug.Log("[shot] done");
}
return Body();
