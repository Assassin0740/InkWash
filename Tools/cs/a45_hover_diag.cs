// a45_hover_diag.cs —— 查「盘旋升空只有 0.33 m」的根因
//
// a44 实测：盘旋期 Y 范围 [0.03, 0.35]，升幅只有 0.33 m，而 hoverHeight=3.6。
//
// 代码回顾（EnemyDragon.cs）：
//   ResolveSpine() 里  _modelRoot = _spine[0].parent;
//   AttackMovement() 里 _modelRoot.localPosition = base + up*lift;
//
// 疑点：`_spine[0]` 到底是谁？如果 ResolveSpine 的"跳过空容器"逻辑
//   把起点定在 `drgon_03`（而非 `_rootJoint`），那 `_modelRoot` 就是 `_rootJoint`，
//   而 `_rootJoint` 的 localPosition 是 (0,0,0) —— 改它没问题。
//   但如果起点定在 `_rootJoint`，`_modelRoot` 就是 `Object_6`…
//   更关键：**lift 有没有被别的地方覆盖**。
//
// 本脚本在 Play 里实例化龙、单调 DoHover（走状态机），
// 逐帧打印：transform.position.y（根）、_modelRoot 是谁、它的 localPosition.y。
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var probe = UnityEngine.Object.FindObjectOfType<HoverProbe>();
if (probe != null) UnityEngine.Object.DestroyImmediate(probe);

GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
Vector3 spawn = player != null ? player.transform.position + new Vector3(0f, 0f, 6f) : new Vector3(0f, 0.3f, 6f);
var go = (GameObject)UnityEngine.Object.Instantiate(prefab, spawn, Quaternion.identity);
go.name = "HoverTestDragon";

var p2 = go.AddComponent<HoverProbe>();
p2.reportPath = Path.Combine(root, "Tools/reports/a45_hover_diag.txt");
Debug.Log("[a45] 已刷龙，探针启动");

public class HoverProbe : MonoBehaviour
{
    public string reportPath;
    private EnemyDragon _d;
    private float _t0;
    private readonly List<string> _rows = new List<string>();

    void Start()
    {
        _d = GetComponent<EnemyDragon>();
        _t0 = Time.time;
        // 反射拿私有的 _modelRoot / _baseRot，只为诊断
        var f = typeof(EnemyDragon).GetField("_modelRoot",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var mr = f != null ? f.GetValue(_d) as Transform : null;
        var fs = typeof(EnemyDragon).GetField("_spine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var spine = fs != null ? fs.GetValue(_d) as System.Collections.IList : null;

        var sb0 = new StringBuilder();
        sb0.AppendLine("---- 结构诊断 ----");
        sb0.AppendLine("  _modelRoot = " + (mr != null ? Path(mr) : "null"));
        sb0.AppendLine("  _modelRoot.localPosition = " + (mr != null ? mr.localPosition.ToString("F4") : "-"));
        sb0.AppendLine("  spine 节数 = " + (spine != null ? spine.Count : -1));
        if (spine != null && spine.Count > 0)
        {
            var s0 = spine[0] as Transform;
            sb0.AppendLine("  spine[0] = " + (s0 != null ? Path(s0) : "null"));
            sb0.AppendLine("  spine[0].parent = " + (s0 != null && s0.parent != null ? Path(s0.parent) : "null"));
        }
        // 打印从根到 Model 的层级链
        sb0.AppendLine("  层级链（根→下 4 层）：");
        void Dump(Transform t, int d)
        {
            sb0.AppendLine("    " + new string(' ', d * 2) + t.name
                + "  lPos=" + t.localPosition.ToString("F3"));
            if (d >= 3) return;
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), d + 1);
        }
        Dump(transform, 0);
        File.WriteAllText(reportPath, sb0.ToString());
        Debug.Log("[a45]\n" + sb0.ToString());
    }

    void Update()
    {
        if (Time.time - _t0 > 14f)
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== a45 盘旋升空诊断 ==========");
            sb.AppendLine("  AttackName | root.y | modelRoot.localY | airborne");
            foreach (var r in _rows) sb.AppendLine(r);
            File.AppendAllText(reportPath, "\n" + sb.ToString());
            Debug.Log("[a45 done]\n" + sb.ToString());
            UnityEngine.Object.Destroy(gameObject);
        }
        var f = typeof(EnemyDragon).GetField("_modelRoot",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var mr = f != null ? f.GetValue(_d) as Transform : null;
        if (_rows.Count < 400 && Time.frameCount % 20 == 0)
            _rows.Add("  " + (_d.CurrentAttackName ?? "-").PadRight(6)
                      + " | " + transform.position.y.ToString("F3")
                      + " | " + (mr != null ? mr.localPosition.y.ToString("F4") : "-")
                      + " | " + _d.Airborne);
    }

    static string Path(Transform t)
    {
        var s = t.name;
        var p = t.parent;
        int g = 0;
        while (p != null && g++ < 8) { s = p.name + "/" + s; p = p.parent; }
        return s;
    }
}
