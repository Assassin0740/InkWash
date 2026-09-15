// 刀光锚点复核：把锚点换算到**武器网格节点的局部空间**。
// 若锚点确实落在剑轴上，它的局部 x、z 应 ≈ 0（剑轴 = 该节点的局部 Y）。
// 上一版报告里写的「垂距 0.1407 m」是量错基准（用了腕关节而非拳心，腕→拳心正好 0.141 m）。
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }

    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    Transform anchor = null;
    foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "BladeTrail_Anchor") { anchor = t; break; }
    if (weapon == null || anchor == null) { Debug.LogError("缺对象"); yield break; }

    var anim = go.GetComponent<Animator>();
    var b = weapon.GetComponent<MeshFilter>().sharedMesh.bounds;

    sb.AppendLine("========== 刀光锚点复核（武器局部空间）==========");
    string[] states = { "Idle", "Walk", "Atk1", "Atk3" };
    foreach (var st in states)
    {
        anim.Play(st, 0, 0f);
        anim.Update(1f / 60f);
        anim.Play(st, 0, 0.35f);
        anim.Update(1f / 60f);
        yield return null; yield return null;

        Vector3 local = weapon.InverseTransformPoint(anchor.position);
        float radial = Mathf.Sqrt(local.x * local.x + local.z * local.z);
        // 沿剑轴的归一化位置：网格局部 Y 从 min.y（剑尖）到 max.y（剑首）
        float pct = (local.y - b.min.y) / Mathf.Max(1e-6f, b.max.y - b.min.y) * 100f;
        sb.AppendLine("  " + st.PadRight(6)
                      + "  锚点局部 = (" + local.x.ToString("F4") + ", " + local.y.ToString("F4") + ", " + local.z.ToString("F4") + ")"
                      + "  离剑轴 = " + radial.ToString("F4") + " m"
                      + "  处于剑身 " + pct.ToString("F1") + "%（0=剑尖 100=剑首）"
                      + (radial < 0.01f ? "  [通过]" : "  [未通过]"));
    }

    // 顺带复核剑的实际朝向（Idle）
    anim.Play("Idle", 0, 0.25f); anim.Update(1f / 60f);
    yield return null; yield return null;
    Vector3 blade = weapon.TransformDirection(Vector3.down).normalized;
    Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
    sb.AppendLine();
    sb.AppendLine("Idle 剑身方向 = " + blade.ToString("F3")
                  + "  与「竖直向下」夹角 = " + Vector3.Angle(blade, Vector3.down).ToString("F1") + "°"
                  + "  与「正前方」夹角 = " + Vector3.Angle(blade, fwd).ToString("F1") + "°");
    sb.AppendLine("（换 Idle 前：与向下 91.6° —— 水平横戳；现在应接近 0° 表示竖直向上）");

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_anchor.txt"), sb.ToString());
    Debug.Log("[q_anchor] done");
    yield return null;
}
return Body();
