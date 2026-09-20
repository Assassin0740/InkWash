// v41d_test.cs —— 皮肤网格 ↔ 骨骼绑定诊断：Animator 动的骨头是不是渲染网格的骨头
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    Application.runInBackground = true;
    var run = Object.FindObjectOfType<InkWash.Roguelike.RunManager>();
    run.ResetForTest(); yield return null;
    run.StartRun();
    var sw3 = System.Diagnostics.Stopwatch.StartNew();
    while (run.State != InkWash.Roguelike.RunState.Playing && sw3.Elapsed.TotalSeconds < 25.0) yield return null;
    yield return new WaitForSecondsRealtime(1.0f);
    var pc = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    var player = pc != null ? pc.transform : null;
    var ph = player != null ? player.GetComponent<InkWash.Player.PlayerHealth>() : null;

    InkWash.Enemies.EnemyBase enemy = null;
    var sw0 = System.Diagnostics.Stopwatch.StartNew();
    while (enemy == null && sw0.Elapsed.TotalSeconds < 25.0)
    {
        foreach (var e in Object.FindObjectsOfType<InkWash.Enemies.EnemyBase>())
            if (e.IsAlive && e.name.Contains("MoGuai")) { enemy = e; break; }
        if (enemy == null) yield return null;
    }
    if (enemy == null) { sb.AppendLine("FAIL no MoGuai"); Debug.Log("[v41d]\n" + sb); yield break; }
    var go = enemy.gameObject;
    var anim = go.GetComponentInChildren<Animator>(true);

    sb.AppendLine("Animator 在 '" + (anim.transform.name) + "'（enemy子级=" + anim.transform.IsChildOf(go.transform) + "）avatar=" + (anim.avatar != null ? anim.avatar.name : "NULL") + " hasTransformHierarchy=" + anim.hasTransformHierarchy);

    // 渲染器清单
    var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    sb.AppendLine("SkinnedMeshRenderer 数量 = " + renderers.Length);
    foreach (var r in renderers)
    {
        sb.AppendLine("SMR '" + r.name + "' rootBone=" + (r.rootBone != null ? r.rootBone.name : "NULL")
            + " bones=" + r.bones.Length
            + " visible=" + r.enabled + "/" + r.gameObject.activeInHierarchy
            + " firstBone=" + (r.bones.Length > 0 ? r.bones[0].name : "-"));
    }
    // 也查静态 MeshRenderer（怪物可能是静态网格！）
    var statics = go.GetComponentsInChildren<MeshRenderer>(true);
    sb.AppendLine("静态 MeshRenderer 数量 = " + statics.Length);
    foreach (var r in statics) sb.AppendLine("  MR '" + r.name + "' on '" + r.transform.parent.name + "'");

    // 手部骨头是否真的在动（打攻击触发器后采样 0.5s）
    Transform hand = null;
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
        if (t.name.ToLower().Contains("hand") || t.name.ToLower().Contains("wrist")) { hand = t; break; }
    if (hand != null)
    {
        Vector3 p0 = hand.position;
        anim.SetTrigger("Attack");
        float moved = 0f; Vector3 prev = p0;
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        while (sw2.Elapsed.TotalSeconds < 0.8)
        {
            yield return null;
            moved += Vector3.Distance(hand.position, prev);
            prev = hand.position;
            if (ph != null && ph.IsAlive && ph.HealthRatio < 0.9f) ph.Heal(999f);
        }
        sb.AppendLine("手骨 '" + hand.name + "' 0.8s 累计位移 = " + moved.ToString("F3") + " m（>0.05 即骨骼在动）");
    }
    else sb.AppendLine("未找到 hand/wrist 骨");
    Debug.Log("[v41d]\n" + sb.ToString());
    yield break;
}

return Body();
