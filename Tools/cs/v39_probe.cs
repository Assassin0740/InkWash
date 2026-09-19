// v39_probe.cs —— B 组前置探针：InkLandingBloom 挂载 / 玩家结构 / 门与房间事件
using UnityEngine;

var sb = new System.Text.StringBuilder();

// ① 玩家身上有没有 InkLandingBloom
var pcs = Object.FindObjectsOfType<InkWash.Player.PlayerController>(true);
foreach (var pc in pcs)
{
    var go = pc.gameObject;
    sb.AppendLine("Player '" + go.name + "' children:");
    var bloom = go.GetComponent<InkWash.Effects.InkLandingBloom>() ?? go.GetComponentInChildren<InkWash.Effects.InkLandingBloom>(true);
    sb.AppendLine("  InkLandingBloom = " + (bloom != null ? "已挂" : "无"));
    var trail = go.GetComponentInChildren<TrailRenderer>(true);
    sb.AppendLine("  TrailRenderer = " + (trail != null ? trail.name : "无"));
    var fx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    sb.AppendLine("  SwordVfx = " + (fx != null ? fx.gameObject.name : "无"));
}

// ② 门的结构（RoomController）
var room = Object.FindObjectOfType<InkWash.Enemies.RoomController>(true);
if (room != null)
{
    sb.AppendLine("RoomController '" + room.gameObject.name + "' doors:");
    foreach (var t in room.GetComponentsInChildren<Transform>(true))
        if (t.name.ToLower().Contains("door"))
            sb.AppendLine("  door obj: " + t.name + " pos=" + t.position);
}

// ③ RunManager 的门引用
var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>(true);
if (run != null)
{
    var f = typeof(InkWash.Roguelike.RunManager).GetField("door", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    var f2 = typeof(InkWash.Roguelike.RunManager).GetField("doorPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
    sb.AppendLine("RunManager.door = " + (f != null ? f.GetValue(run)?.ToString() ?? "null" : "字段不存在"));
    sb.AppendLine("RunManager.doorPoint = " + (f2 != null ? f2.GetValue(run)?.ToString() ?? "null" : "字段不存在"));
}

Debug.Log("[v39probe]\n" + sb.ToString());
return sb.ToString();
