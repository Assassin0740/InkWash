// v30_s5diag3.cs —— 房间推进断链定位：引用接线 + 内部计时器
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

IEnumerator Body()
{
    var spawners = Object.FindObjectsOfType<InkWash.Enemies.WaveSpawner>(true);
    var rooms = Object.FindObjectsOfType<InkWash.Enemies.RoomController>(true);
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    var sb = new System.Text.StringBuilder();
    sb.Append("WaveSpawner x").Append(spawners.Length)
      .Append(" RoomController x").Append(rooms.Length)
      .Append(" RunManager ").Append(run != null ? "x1" : "NONE");

    foreach (var s in spawners)
        sb.Append(" || sp[").Append(s.name).Append("] compEnabled=").Append(s.enabled)
          .Append(" goActive=").Append(s.gameObject.activeInHierarchy)
          .Append(" allClr=").Append(s.AllCleared);

    foreach (var r in rooms)
    {
        sb.Append(" || rc[").Append(r.name).Append("] isCleared=").Append(r.IsCleared);
        var f = typeof(InkWash.Enemies.RoomController).GetField("spawner", BindingFlags.Public | BindingFlags.Instance);
        var linked = f != null ? f.GetValue(r) as InkWash.Enemies.WaveSpawner : null;
        sb.Append(" spawnerRef=").Append(linked != null ? linked.name : "NULL");
        // RoomController 是否启用
        sb.Append(" compEnabled=").Append(r.enabled).Append(" goActive=").Append(r.gameObject.activeInHierarchy);
    }

    if (run != null)
    {
        var t = typeof(InkWash.Roguelike.RunManager);
        var fr = t.GetField("room", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var roomRef = fr != null ? fr.GetValue(run) as InkWash.Enemies.RoomController : null;
        sb.Append(" || run.room=").Append(roomRef != null ? roomRef.name : "NULL");
        foreach (var fn in new[] { "_nextRoomTimer", "_state", "_roomIndex" })
        {
            var f = t.GetField(fn, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) sb.Append(" ").Append(fn).Append("=").Append(f.GetValue(run));
        }
        sb.Append(" state=").Append(run.State).Append(" roomIdx=").Append(run.RoomIndex);
        // RunManager 组件是否启用
        sb.Append(" compEnabled=").Append(run.enabled).Append(" goActive=").Append(run.gameObject.activeInHierarchy);
    }
    Debug.Log("[diag3] " + sb.ToString());
    yield return null;
}
return Body();
