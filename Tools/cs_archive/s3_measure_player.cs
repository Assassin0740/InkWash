// 精确量角色在**实际动画姿态**下的包围盒（Renderer.bounds 对蒙皮网格只是近似，
// 这里用 SkinnedMeshRenderer.BakeMesh 取真实顶点），据此把脚底对齐到地面。
using System.Collections;

string OUT_TXT = "Tools/reports/S3_measure.txt";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, OUT_TXT), sb.ToString());

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { sb.AppendLine("[ERR] no PlayerController"); flush(); yield break; }
    var root = ctl.transform;
    var anim = ctl.animator;
    var vis = root.Find("Visual");

    float groundY = root.position.y;
    var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f);
    float best = float.MinValue;
    foreach (var h in hits) { if (h.collider.transform.IsChildOf(root)) continue; if (h.point.y > best) best = h.point.y; }
    if (best > float.MinValue) groundY = best;

    var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh(); bake.MarkDynamic();

    // 读一帧，等动画稳定在待机
    yield return null; yield return null;

    var st = anim.GetCurrentAnimatorStateInfo(0);
    string stateName = "?";
    foreach (var n in new[] { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk2", "Atk3" })
        if (st.IsName(n)) stateName = n;

    // 真实顶点包围盒
    var min = new Vector3(9999f, 9999f, 9999f);
    var max = new Vector3(-9999f, -9999f, -9999f);
    foreach (var s in smrs)
    {
        s.BakeMesh(bake);
        var verts = bake.vertices;
        var m = s.transform.localToWorldMatrix;
        foreach (var v in verts)
        {
            var wp = m.MultiplyPoint3x4(v);
            min = Vector3.Min(min, wp); max = Vector3.Max(max, wp);
        }
    }

    var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    var head = anim.GetBoneTransform(HumanBodyBones.Head);
    var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
    var rf = anim.GetBoneTransform(HumanBodyBones.RightFoot);

    sb.AppendLine("当前状态      = " + stateName);
    sb.AppendLine("root.y        = " + root.position.y.ToString("F3"));
    sb.AppendLine("地面 Y        = " + groundY.ToString("F3"));
    sb.AppendLine(string.Format("真实包围盒    y {0:F3} .. {1:F3}   高 {2:F3}   脚底离地 {3:F3}",
        min.y, max.y, max.y - min.y, min.y - groundY));
    sb.AppendLine(string.Format("            x {0:F3} .. {1:F3}   宽 {2:F3}", min.x, max.x, max.x - min.x));
    sb.AppendLine(string.Format("            z {0:F3} .. {1:F3}   深 {2:F3}", min.z, max.z, max.z - min.z));
    sb.AppendLine("hips.y        = " + hips.position.y.ToString("F3"));
    sb.AppendLine("head.y        = " + head.position.y.ToString("F3"));
    sb.AppendLine("footL.y       = " + lf.position.y.ToString("F3") + "   footR.y = " + rf.position.y.ToString("F3"));
    sb.AppendLine("Visual.localPos = " + vis.localPosition.ToString("F4"));
    sb.AppendLine();

    // 建议：把脚底压到地面
    float need = groundY - min.y;
    sb.AppendLine(string.Format("把脚底对齐地面需要把 Visual 上移 {0:F4}（当前 localPos.y={1:F4} -> {2:F4}）",
        need, vis.localPosition.y, vis.localPosition.y + need));
    sb.AppendLine(string.Format("CharacterController 高 {0:F2}，角色实际高 {1:F3}，比值 {2:F3}",
        root.GetComponent<CharacterController>().height, max.y - min.y,
        root.GetComponent<CharacterController>().height / (max.y - min.y)));

    Object.Destroy(bake);
    flush();
    Debug.Log("[measure] " + sb.ToString());
}
return Body();
