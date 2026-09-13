// 把「角色正下方一条向下射线」打到的所有碰撞体逐条列出（名字/层/是否Trigger/Y），
// 用来判断 GroundYUnder 到底把什么当成了地面。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player";
Vector3 p = player.transform.position;
sb.AppendLine("Player transform.position = " + p.ToString("F4"));

sb.AppendLine();
sb.AppendLine("-- 从 Player 上方 3m 向下 10m 的所有命中 --");
var hits = Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 12f);
sb.AppendLine("命中数 = " + hits.Length);
foreach (var h in hits)
{
    var tr = h.collider.transform;
    bool self = tr == player.transform || tr.IsChildOf(player.transform);
    sb.AppendLine(string.Format("  y={0,8:F4}  layer={1,-3}({2})  trigger={3,-5}  self={4,-5}  {5}",
        h.point.y, h.collider.gameObject.layer, LayerMask.LayerToName(h.collider.gameObject.layer),
        h.collider.isTrigger, self, GetPath(tr, player.transform)));
}

sb.AppendLine();
sb.AppendLine("-- 同一射线，忽略 Trigger 后 --");
var hits2 = Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
sb.AppendLine("命中数 = " + hits2.Length);
foreach (var h in hits2)
{
    var tr = h.collider.transform;
    bool self = tr == player.transform || tr.IsChildOf(player.transform);
    sb.AppendLine(string.Format("  y={0,8:F4}  trigger={1,-5}  self={2,-5}  {3}",
        h.point.y, h.collider.isTrigger, self, GetPath(tr, player.transform)));
}

sb.AppendLine();
sb.AppendLine("-- 场景里所有 Collider 概览（前 40 个） --");
var all = UnityEngine.Object.FindObjectsOfType<Collider>();
sb.AppendLine("总数 = " + all.Length);
int n = 0;
foreach (var c in all)
{
    if (n++ >= 40) { sb.AppendLine("  ..."); break; }
    bool self = c.transform == player.transform || c.transform.IsChildOf(player.transform);
    sb.AppendLine(string.Format("  y={0,8:F4}  boundMinY={1,8:F4}  trigger={2,-5}  enabled={3,-5}  self={4,-5}  {5}",
        c.transform.position.y, c.bounds.min.y, c.isTrigger, c.enabled, self, GetPath(c.transform, player.transform)));
}

return sb.ToString();

// 本地函数：输出相对 Player 的层级路径
string GetPath(Transform t, Transform root)
{
    var s = t.name;
    var cur = t.parent;
    int guard = 0;
    while (cur != null && guard++ < 10) { s = cur.name + "/" + s; cur = cur.parent; }
    return s;
}
