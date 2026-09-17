// a49_hover_probe.cs —— Play 模式下逐帧验证盘旋升空（非阻塞探针）
//
// 背景：
//   a48 在 Edit 模式跑，`ResolveSpine()` 挂在 `Awake` ⇒ 没触发 ⇒ 脊骨=0、_modelRoot=null，
//   量不到升幅。必须进 Play。
//
//   Codely 的 `--runtime` **禁止阻塞调用**（Thread.Sleep / Task.Wait 全被拦），
//   所以长时间采样只能挂一个 MonoBehaviour，在它自己的 Update() 里逐帧采，采完写文件 + 自毁。
//
// 本脚本做什么：
//   ① 进 Play 后等 1 s（让 Awake/Start 跑完）
//   ② 在平地上生成龙，等它 ResolveSpine 完成
//   ③ 挂 DragonHoverProbe：强制 BeginHover，逐帧记录
//        - _modelRoot 的层级路径（一次）
//        - _modelRoot.localPosition.y
//        - transform.position.y
//        - _spine.Count
//   ④ 8 s 后写报告，自毁
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a49 盘旋升空 Play 模式验证 ==========");

// 清残留
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.name.StartsWith("HoverTest") || g.name.StartsWith("A49_"))
        UnityEngine.Object.DestroyImmediate(g);

var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
ground.name = "A49_Ground";
ground.transform.position = new Vector3(0f, -0.5f, 0f);
ground.transform.localScale = new Vector3(80f, 1f, 80f);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
if (prefab == null) { Debug.LogError("[a49] ★ 找不到 prefab"); }
else
{
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.name = "HoverTestDragon";
    go.transform.position = new Vector3(0f, 0.1f, 0f);
    var probe = go.AddComponent<HoverProbe>();
    probe.reportPath = Path.Combine(root, "Tools/reports/a49_hover_probe.txt");
    sb.AppendLine("已生成 HoverTestDragon + HoverProbe");
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a49_launch.txt"), sb.ToString());
Debug.Log("[a49]\n" + sb.ToString());

// ==================================================================

public class HoverProbe : MonoBehaviour
{
    public string reportPath;
    private EnemyDragon _d;
    private Transform _modelRoot;
    private float _t;
    private float _t0;
    private float _baseLocalY;
    private readonly List<string> _rows = new List<string>();
    private bool _begun;
    private float _minLocalY = float.MaxValue, _maxLocalY = float.MinValue;
    private float _minWorldY = float.MaxValue, _maxWorldY = float.MinValue;
    private string _rootPath = "?";
    private int _spineCount = -1;

    private void Start()
    {
        _t0 = Time.time;
        _d = GetComponent<EnemyDragon>();
        var t = typeof(EnemyDragon);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var fRoot = t.GetField("_modelRoot", flags);
        var fSpine = t.GetField("_spine", flags);
        var fCaptured = t.GetField("_groundCaptured", flags);
        var fGroundY = t.GetField("_groundY", flags);

        if (fRoot != null) _modelRoot = fRoot.GetValue(_d) as Transform;
        if (fSpine != null)
        {
            var sp = fSpine.GetValue(_d) as System.Collections.IList;
            _spineCount = sp != null ? sp.Count : -1;
        }
        _rootPath = _modelRoot != null ? P(_modelRoot) : "★null";
        _baseLocalY = _modelRoot != null ? _modelRoot.localPosition.y : 0f;
        Debug.Log("[a49probe] Start: spine=" + _spineCount + "  modelRoot=" + _rootPath
                  + "  baseLocalY=" + _baseLocalY.ToString("F4")
                  + "  groundCaptured=" + (fCaptured != null ? fCaptured.GetValue(_d).ToString() : "?")
                  + "  groundY=" + (fGroundY != null ? fGroundY.GetValue(_d).ToString() : "?"));
    }

    private void Update()
    {
        _t += Time.deltaTime;

        // 第 1 s：确认 Awake 已跑完、锁地已捕获
        if (!_begun && _t > 1.0f)
        {
            _begun = true;
            if (_d != null)
            {
                // 直接走 BossAttack 路径：进入盘旋
                _d.ForceBeginHoverForTest();
            }
            Debug.Log("[a49probe] 1s: 已请求 BeginHover, modelRoot="
                      + (_modelRoot != null ? _modelRoot.localPosition.ToString("F4") : "null"));
        }

        if (_modelRoot != null)
        {
            float ly = _modelRoot.localPosition.y;
            _minLocalY = Mathf.Min(_minLocalY, ly);
            _maxLocalY = Mathf.Max(_maxLocalY, ly);
            float wy = transform.position.y;
            _minWorldY = Mathf.Min(_minWorldY, wy);
            _maxWorldY = Mathf.Max(_maxWorldY, wy);

            // 每 0.5 s 记一行
            if (_rows.Count < 40 && _t > _begunAt + _rows.Count * 0.5f)
                _rows.Add(string.Format("  t={0:F2}  rootLocalY={1:F4}  worldY={2:F4}",
                    _t, ly, wy));
        }

        if (_t > 9.0f)
        {
            Finish();
        }
    }

    private float _begunAt = 1.0f;

    private void Finish()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== a49 盘旋升空逐帧记录 ==========");
        sb.AppendLine("脊骨链节数 = " + _spineCount);
        sb.AppendLine("_modelRoot = " + _rootPath);
        sb.AppendLine("baseLocalY = " + _baseLocalY.ToString("F4"));
        sb.AppendLine("hoverHeight = " + (_d != null ? _d.hoverHeight.ToString("F2") : "?") + " m");
        sb.AppendLine();
        sb.AppendLine("---- 逐帧采样 ----");
        foreach (var r in _rows) sb.AppendLine(r);
        sb.AppendLine();
        sb.AppendLine("---- 结论 ----");
        sb.AppendLine("  modelRoot.localY 范围 [" + _minLocalY.ToString("F4") + ", " + _maxLocalY.ToString("F4") + "]"
                      + "  升幅 = " + (_maxLocalY - _minLocalY).ToString("F4") + " m");
        sb.AppendLine("  transform.worldY 范围 [" + _minWorldY.ToString("F4") + ", " + _maxWorldY.ToString("F4") + "]");
        sb.AppendLine("  ★ 目标升幅 " + (_d != null ? _d.hoverHeight.ToString("F2") : "3.6") + " m");

        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log("[a49]\n" + sb.ToString());
        Destroy(gameObject);
    }

    private static string P(Transform t)
    {
        var s = t.name; var p = t.parent; int g = 0;
        while (p != null && g++ < 8) { s = p.name + "/" + s; p = p.parent; }
        return s;
    }
}
