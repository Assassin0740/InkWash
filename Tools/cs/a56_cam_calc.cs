// a56_cam_calc.cs —— 一次算对相机：先量出龙与玩家的真实包围盒，再反算机位
//
// 前面在相机上绕了三版（跟随取中点 / 11 m 侧移 / 17 m 俯拍），全是"先猜再拍、
// 拍完看图才发现不对"。这次改成**先量再拍**：
//   ① 把龙和玩家按演示布局摆好
//   ② 用 Renderer.bounds 求出两者的联合包围盒（这是唯一可信的尺寸口径）
//   ③ 按相机 fov / 宽高比反算"要多远才装得下"
//   ④ 取一个 45° 俯角的机位，打印出位置与"包围盒 8 个角在不在视野内"的自检结果
//
// 这样出来的机位是算出来的，不是试出来的。
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);

foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.name.StartsWith("A5") || g.name.StartsWith("HoverTest"))
        UnityEngine.Object.DestroyImmediate(g);

var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
ground.name = "A56_Ground";
ground.transform.position = new Vector3(0f, -0.6f, 0f);
ground.transform.localScale = new Vector3(160f, 1f, 160f);

GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player != null)
{
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    player.transform.position = new Vector3(0f, 0.3f, 10f);
    if (cc != null) cc.enabled = true;
}

var ws = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
if (ws != null) ws.enabled = false;

var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
var dg = p != null ? PrefabUtility.InstantiatePrefab(p) as GameObject : null;
if (dg != null) dg.transform.position = new Vector3(0f, 0.3f, 4f);

var go = new GameObject("A56_Calc");
var calc = go.AddComponent<CamCalc>();
calc.dragon = dg;
calc.player = player;
calc.reportPath = Path.Combine(root, "Tools/reports/a56_cam_calc.txt");
Debug.Log("[a56] 已挂, dragon=" + (dg != null) + " player=" + (player != null));

public class CamCalc : MonoBehaviour
{
    public GameObject dragon;
    public GameObject player;
    public string reportPath;
    private float _t;

    private void Update()
    {
        _t += Time.deltaTime;
        if (_t < 1.2f) return;
        enabled = false;
        Run();
    }

    private void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== a56 相机反算 ==========");
        var cam = Camera.main;
        if (cam == null) { sb.AppendLine("★ 没有主相机"); Flush(sb); return; }

        // ---- 联合包围盒 ----
        Bounds b = new Bounds(new Vector3(0f, 1f, 5f), Vector3.zero);
        bool first = true;
        foreach (var r in dragon.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled) continue;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            sb.AppendLine("  龙渲染器 " + r.name + " bounds=" + r.bounds.center.ToString("F2")
                          + " size=" + r.bounds.size.ToString("F2"));
        }
        if (player != null)
            foreach (var r in player.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                b.Encapsulate(r.bounds);
            }
        sb.AppendLine();
        sb.AppendLine("联合包围盒 center=" + b.center.ToString("F3") + "  size=" + b.size.ToString("F3"));
        sb.AppendLine("  8 角 = " + string.Join(" | ", new[]
        {
            new Vector3(b.min.x, b.min.y, b.min.z), new Vector3(b.min.x, b.min.y, b.max.z),
            new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.min.x, b.max.y, b.max.z),
            new Vector3(b.max.x, b.min.y, b.min.z), new Vector3(b.max.x, b.min.y, b.max.z),
            new Vector3(b.max.x, b.max.y, b.min.z), new Vector3(b.max.x, b.max.y, b.max.z),
        }.Select(v => v.ToString("F1"))));
        sb.AppendLine("  半径(含外接球) = " + b.extents.magnitude.ToString("F2"));
        sb.AppendLine();

        // ---- 反算距离 ----
        float fov = cam.fieldOfView;
        float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
        float vHalf = fov * 0.5f * Mathf.Deg2Rad;
        float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * aspect);
        float radius = b.extents.magnitude;
        float distV = radius / Mathf.Sin(vHalf);
        float distH = radius / Mathf.Sin(hHalf);
        float need = Mathf.Max(distV, distH) * 1.06f;   // 6% 余量

        sb.AppendLine("相机 fov=" + fov + "  aspect=" + aspect.ToString("F3"));
        sb.AppendLine("  竖直半角=" + (vHalf * Mathf.Rad2Deg).ToString("F1") + "°  水平半角=" + (hHalf * Mathf.Rad2Deg).ToString("F1") + "°");
        sb.AppendLine("  装下外接球需要的距离：竖直 " + distV.ToString("F2") + " m / 水平 " + distH.ToString("F2") + " m");
        sb.AppendLine("  ⇒ 取 " + need.ToString("F2") + " m（含 6% 余量）");
        sb.AppendLine();

        // ---- 定机位：从 b.center 沿 135° 方向（后/侧上方）拉 need 距离 ----
        Vector3 dir = new Vector3(0.55f, 0.62f, -0.56f).normalized;  // 后上方偏右
        Vector3 pos = b.center + dir * need;
        cam.transform.position = pos;
        cam.transform.LookAt(b.center);
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, need * 4f);

        sb.AppendLine("相机位置 = " + pos.ToString("F3") + "  lookAt = " + b.center.ToString("F3"));
        sb.AppendLine();

        // ---- 自检：8 个角是否都在视野内 ----
        sb.AppendLine("---- 自检：包围盒 8 角投影到视口坐标（0~1 为可见） ----");
        int inside = 0;
        foreach (var v in new[]
        {
            new Vector3(b.min.x, b.min.y, b.min.z), new Vector3(b.min.x, b.min.y, b.max.z),
            new Vector3(b.min.x, b.max.y, b.min.z), new Vector3(b.min.x, b.max.y, b.max.z),
            new Vector3(b.max.x, b.min.y, b.min.z), new Vector3(b.max.x, b.min.y, b.max.z),
            new Vector3(b.max.x, b.max.y, b.min.z), new Vector3(b.max.x, b.max.y, b.max.z),
        })
        {
            var vp = cam.WorldToViewportPoint(v);
            bool ok = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            if (ok) inside++;
            sb.AppendLine("  " + v.ToString("F1") + " → 视口(" + vp.x.ToString("F2") + ", " + vp.y.ToString("F2")
                          + ", z=" + vp.z.ToString("F2") + ")  " + (ok ? "✓" : "★ 出画"));
        }
        sb.AppendLine("  ⇒ " + inside + " / 8 在视野内"
                      + (inside == 8 ? "  ✓ 构图成立" : "  ★ 还会裁掉一部分"));

        Flush(sb);
    }

    private void Flush(StringBuilder sb)
    {
        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log("[a56]\n" + sb.ToString());
        Destroy(gameObject);
    }
}
