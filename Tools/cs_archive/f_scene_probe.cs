// 搞清"地面 Y = 0.300"到底是哪个碰撞体：列表 + 逐条 raycast 命中。
// 这直接决定 FootIK 标定的基准对不对（上一轮报告写的也是 0.3000）。
// 顺带清理编辑期 Destroy 失败残留的 TmpShotCam。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/f_scene_probe.txt"), sb.ToString());

    // 清残留
    var stray = GameObject.Find("TmpShotCam");
    if (stray != null) { sb.AppendLine("删掉残留的 TmpShotCam"); Object.DestroyImmediate(stray); }

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { sb.AppendLine("[ERR] no PlayerController"); flush(); yield break; }
    var root = ctl.transform;
    sb.AppendLine("Player root.position = " + root.position.ToString("F4"));
    sb.AppendLine();

    sb.AppendLine("================ 场景里所有带碰撞体的物体 ================");
    foreach (var c in Object.FindObjectsOfType<Collider>())
    {
        if (c.transform.IsChildOf(root)) continue;
        var b = c.bounds;
        sb.AppendLine(string.Format("  {0,-30} layer={1,-3}({2})  type={3}  包围盒 y {4:F4}..{5:F4}  x {6:F3}..{7:F3}  z {8:F3}..{9:F3}  isTrigger={10}",
            c.gameObject.name, c.gameObject.layer, LayerMask.LayerToName(c.gameObject.layer),
            c.GetType().Name, b.min.y, b.max.y, b.min.x, b.max.x, b.min.z, b.max.z, c.isTrigger));
    }

    sb.AppendLine();
    sb.AppendLine("================ 玩家正下方逐条 raycast（从 y+3 向下 10m，含 Trigger）================ ");
    var hits = Physics.RaycastAll(root.position + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Collide);
    System.Array.Sort(hits, (a, b2) => b2.point.y.CompareTo(a.point.y));
    foreach (var h in hits)
    {
        sb.AppendLine(string.Format("  命中 y={0:F4}  {1,-28} ({2})  collider={3}",
            h.point.y, h.collider.gameObject.name, LayerMask.LayerToName(h.collider.gameObject.layer), h.collider.GetType().Name));
    }

    sb.AppendLine();
    sb.AppendLine("================ 地面/地板候选对象 ================");
    foreach (var t in Object.FindObjectsOfType<Transform>())
    {
        string n = t.name.ToLower();
        if (!(n.Contains("ground") || n.Contains("floor") || n.Contains("plane") || n.Contains("terrain"))) continue;
        var r = t.GetComponent<Renderer>();
        var c = t.GetComponent<Collider>();
        sb.AppendLine("  " + t.name + "  pos=" + t.position.ToString("F4")
            + "  scale=" + t.lossyScale.ToString("F3")
            + (r != null ? "  renderer.bounds.y " + r.bounds.min.y.ToString("F4") + ".." + r.bounds.max.y.ToString("F4") : "")
            + (c != null ? "  collider.bounds.y " + c.bounds.min.y.ToString("F4") + ".." + c.bounds.max.y.ToString("F4") : "  (无碰撞体)"));
    }

    flush();
    Debug.Log("[probe] 完成，见 Tools/reports/f_scene_probe.txt");
    yield return null;
}
return Body();
