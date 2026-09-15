// h_scene_bounds.cs —— 场地真实边界 + 验收复位点的合法性核对
//
// 背景：S1/S2 的「疾跑」阶段实测速度 0.000 m/s、受阻帧占比 61.5%。
//       验收约定「每阶段复位到 StageAnchor = (0, 0.05, -14)」（PlaytestHarness L59）。
//       怀疑：场地从 10×10 扩到 20×20 之后（S3 v1.14），这个复位点落到了院墙**外面**，
//       于是 4.0 m/s × 2.5 s = 10 m 会在 ~3.8 m 处撞上南墙 —— 读数就变成"顶着墙"。
//       本探针把场地各面的包围盒量出来，直接判定复位点在不在场地内，并算出可用跑道长度。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    string repPath = Path.Combine(root, "Tools/reports/h_scene_bounds.txt");

    var sb = new StringBuilder();
    sb.AppendLine("========== h_scene_bounds 场地边界 vs 验收复位点 ==========");

    var anchor = new Vector3(0f, 0.05f, -14f);
    sb.AppendLine("验收复位点 StageAnchor = " + V(anchor));
    sb.AppendLine();

    // 逐个环境物体报世界包围盒（含 Collider，因为碰撞才是关键）
    var env = GameObject.Find("Environment");
    sb.AppendLine("---- ① 环境物体的世界包围盒（Renderer）----");
    float wallSOuter = float.NaN, wallNOuter = float.NaN, wallWOuter = float.NaN, wallEOuter = float.NaN;
    if (env != null)
    {
        foreach (var t in env.GetComponentsInChildren<Transform>(true))
        {
            var rs = t.GetComponentsInChildren<Renderer>(false);
            var cols = t.GetComponentsInChildren<Collider>(false);
            if (rs.Length == 0 && cols.Length == 0) continue;
            var b = new Bounds(t.position, Vector3.zero);
            bool any = false;
            foreach (var r in rs) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            foreach (var c in cols) { if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds); }
            if (!any) continue;
            sb.AppendLine("  " + Pad(t.name, 18)
                          + " pos=" + V(t.position)
                          + "  x[" + F(b.min.x) + "," + F(b.max.x) + "]"
                          + "  y[" + F(b.min.y) + "," + F(b.max.y) + "]"
                          + "  z[" + F(b.min.z) + "," + F(b.max.z) + "]"
                          + "  renderer=" + rs.Length + " collider=" + cols.Length);
            string n = t.name;
            if (n.Contains("Wall_S")) wallSOuter = b.min.z;
            if (n.Contains("Wall_N")) wallNOuter = b.max.z;
            if (n.Contains("Wall_W")) wallWOuter = b.min.x;
            if (n.Contains("Wall_E")) wallEOuter = b.max.x;
        }
    }
    sb.AppendLine();

    sb.AppendLine("---- ② 判定 ----");
    sb.AppendLine("  南墙外表面 z = " + F(wallSOuter) + "　北墙 " + F(wallNOuter)
                  + "　西墙 x = " + F(wallWOuter) + "　东墙 " + F(wallEOuter));
    bool inside = anchor.z > wallSOuter && anchor.z < wallNOuter
               && anchor.x > wallWOuter && anchor.x < wallEOuter;
    sb.AppendLine("  复位点在场地内？ " + (inside ? "是" : "**否 —— 在院墙外面**"));
    if (!float.IsNaN(wallSOuter))
    {
        sb.AppendLine("  向南（−z）可用跑道 = " + F(anchor.z - wallSOuter) + " m");
        sb.AppendLine("  向北（+z）可用跑道 = " + F(wallNOuter - anchor.z) + " m");
        sb.AppendLine("  验收「疾跑 2.5 s」在 runSpeed=" + F(FindRunSpeed()) + " m/s 下需要 "
                      + F(FindRunSpeed() * 2.5f) + " m");
    }
    sb.AppendLine("  ⇒ 建议复位点（南墙内 1 m，向北留最长跑道）= "
                  + "(" + F(anchor.x) + ", " + F(anchor.y) + ", " + F(wallSOuter + 1f) + ")");
    sb.AppendLine();

    // ---- ③ 实跑验证：从复位点向前跑 2.5 s，看到底在哪停下 ----
    sb.AppendLine("---- ③ 实跑验证（从复位点向北跑 2.5 s）----");
    var ph = Object.FindObjectOfType<InkWash.Player.PlayerHealth>();
    if (ph == null) { sb.AppendLine("  找不到 PlayerHealth，跳过实跑"); }
    else
    {
        var go = ph.gameObject;
        var ctl = go.GetComponent<InkWash.Player.PlayerController>();
        var cc = go.GetComponent<CharacterController>();
        if (ctl != null)
        {
            ctl.BeginInputOverride();
            foreach (var startZ in new float[] { -14f, -9f })
            {
                if (cc != null) cc.enabled = false;
                go.transform.position = new Vector3(0f, 0.05f, startZ);
                go.transform.rotation = Quaternion.identity;
                if (cc != null) cc.enabled = true;
                ctl.ResetToLocomotion();
                ctl.SetInjectedMove(Vector2.zero, false);
                float ts = Time.unscaledTime; while (Time.unscaledTime - ts < 0.7f) yield return null;
                var p0 = go.transform.position;
                ctl.SetInjectedMove(new Vector2(0f, 1f), true);
                float t0 = Time.unscaledTime;
                float sumSpeed = 0f; int n = 0; int blocked = 0;
                while (Time.unscaledTime - t0 < 2.5f)
                {
                    sumSpeed += ctl.CurrentSpeed; n++;
                    if (ctl.CurrentSpeed < ctl.DesiredSpeed * 0.4f) blocked++;
                    yield return null;
                }
                var p1 = go.transform.position;
                ctl.SetInjectedMove(Vector2.zero, false);
                sb.AppendLine("  起点 z=" + F(startZ) + " → 终点 z=" + F(p1.z)
                              + "　位移 " + F((p1 - p0).magnitude) + " m"
                              + "　平均速度 " + F(sumSpeed / Mathf.Max(1, n)) + " m/s"
                              + "　受阻帧 " + F(blocked * 100f / Mathf.Max(1, n)) + " %");
            }
            ctl.EndInputOverride();
        }
    }

    string report = sb.ToString();
    File.WriteAllText(repPath, report, new UTF8Encoding(false));
    Debug.Log("[h_scene_bounds] 报告已落盘 " + repPath);
    Debug.Log(report);
    yield return null;
}

float FindRunSpeed()
{
    var ph = Object.FindObjectOfType<InkWash.Player.PlayerHealth>();
    if (ph == null) return 0f;
    var c = ph.GetComponent<InkWash.Player.PlayerController>();
    return c != null ? c.runSpeed : 0f;
}

string Pad(string s, int n) { return s.Length >= n ? s : s + new string(' ', n - s.Length); }
string V(Vector3 v) { return "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")"; }
string F(float v) { return v.ToString("0.###"); }

return Body();
