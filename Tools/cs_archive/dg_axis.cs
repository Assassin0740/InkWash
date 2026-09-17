using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_axis：判定龙骨链的**弯曲轴**。
//
// 背景：`ApplySpineOffsetsRaw` 用 `Euler(pitch, yaw, 0)`，而 `DoDiveTell`/`DoStrikeBite`
//       用 `Euler(pitch, 0, 0)`。若链的方向是节的 local ±X，则：
//         绕 local X = **自转/扭转**（不弯）
//         绕 local Y / Z = **弯曲**
//       如果猜错，"低头俯角"实际是"把头拧一圈"，而行波会被做成螺旋 —— 必须先量。
//
// 手法：整条链每节叠同一个 Euler 偏移，然后量
//   ① 逐节弯角（相邻两段方向的夹角）—— 扭转时 ≈0°，弯曲时 ≈ 施加角
//   ② 偏移旋转轴 与 该节链方向 的夹角 —— 平行(≈0°) = 扭转，垂直(≈90°) = 弯曲
//   ③ 末端位移的三个分量 —— 判断弯在哪个平面（水平 / 垂直）
public class dg_axis : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine, _base;
    readonly StringBuilder _sb = new StringBuilder();

    static readonly string[] Names = { "基准 (0,0,0)", "绕 X (15,0,0)", "绕 Y (0,15,0)", "绕 Z (0,0,15)" };
    static readonly Vector3[] Eul =
    {
        new Vector3(0f, 0f, 0f),
        new Vector3(15f, 0f, 0f),
        new Vector3(0f, 15f, 0f),
        new Vector3(0f, 0f, 15f)
    };

    int _test; int _wait = 2; bool _done;
    Vector3[][] _pos = new Vector3[4][];
    float[] _perJoint = new float[4];
    Vector3[] _tailDelta = new Vector3[4];
    float[] _axisVsDir = new float[4];
    float[] _headToTail = new float[4];

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_axis: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_axis_Dragon";
        _dragon.transform.position = Vector3.zero;
        _dragon.transform.rotation = Quaternion.identity;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { Debug.LogError("dg_axis: 找不到 EnemyDragon 类型"); enabled = false; return; }

        _comp = _dragon.GetComponentInChildren(_ty, true);
        if (_comp == null) { Debug.LogError("dg_axis: 龙身上没挂 EnemyDragon"); enabled = false; return; }

        _spine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(_comp) as System.Collections.IList;
        _base = _ty.GetField("_baseRot", BindingFlags.NonPublic | BindingFlags.Instance)
                   .GetValue(_comp) as System.Collections.IList;

        if (_spine == null || _spine.Count == 0)
        { Debug.LogError("dg_axis: spine 空 (count=" + (_spine == null ? -1 : _spine.Count) + ")"); enabled = false; return; }

        DisableDragon();
        Apply(0);
    }

    void DisableDragon()
    {
        var mb = _comp as MonoBehaviour;
        if (mb != null && mb.enabled) mb.enabled = false;
    }

    void Update()
    {
        DisableDragon();
        if (_done) return;

        if (_wait > 0) { _wait--; if (_wait == 0) Measure(); return; }
        if (_test < 3) { _test++; Apply(_test); _wait = 2; return; }

        Finish();
    }

    void Apply(int k)
    {
        var e = Eul[k];
        for (int i = 0; i < _spine.Count; i++)
        {
            var tr = _spine[i] as Transform;
            var bq = (Quaternion)_base[i];
            tr.localRotation = bq * Quaternion.Euler(e.x, e.y, e.z);
        }
    }

    void Measure()
    {
        int n = _spine.Count;
        var M = _dragon.transform;
        var p = new Vector3[n];
        for (int i = 0; i < n; i++) p[i] = M.InverseTransformPoint((_spine[i] as Transform).position);
        _pos[_test] = p;

        // ① 逐节弯角：相邻两段方向的平均夹角
        float sum = 0f; int cnt = 0;
        for (int i = 0; i + 2 < n; i++)
        {
            Vector3 d0 = p[i + 1] - p[i];
            Vector3 d1 = p[i + 2] - p[i + 1];
            if (d0.sqrMagnitude < 1e-8f || d1.sqrMagnitude < 1e-8f) continue;
            sum += Vector3.Angle(d0, d1); cnt++;
        }
        _perJoint[_test] = cnt > 0 ? sum / cnt : 0f;

        // ② 中点节的「偏移旋转轴」与该节「链方向」的夹角
        int mid = n / 2;
        var midT = _spine[mid] as Transform;
        var childT = _spine[mid + 1] as Transform;
        Vector3 localDir = midT.InverseTransformPoint(childT.position);
        var offQ = midT.localRotation * Quaternion.Inverse((Quaternion)_base[mid]);
        Vector3 ax; float ang; offQ.ToAngleAxis(out ang, out ax);
        _axisVsDir[_test] = (localDir.sqrMagnitude > 1e-8f && ang > 0.01f)
            ? Vector3.Angle(ax, localDir.normalized) : -1f;

        // ③ 末端位移 + 首尾连线夹角
        if (_test == 0) { _tailDelta[0] = Vector3.zero; }
        else { _tailDelta[_test] = p[n - 1] - _pos[0][n - 1]; }

        Vector3 h = p[n - 1] - p[0];
        Vector3 h0 = _pos[0][n - 1] - _pos[0][0];
        _headToTail[_test] = (h.sqrMagnitude > 1e-8f && h0.sqrMagnitude > 1e-8f)
            ? Vector3.Angle(h0, h) : 0f;
    }

    void Finish()
    {
        int n = _spine.Count;
        var p0 = _pos[0];
        var M = _dragon.transform;

        _sb.AppendLine("龙骨链弯曲轴实验  节点数 = " + n);
        _sb.AppendLine("模型缩放 = " + M.lossyScale.ToString("F4") + "  容器 localPos = " + M.localPosition.ToString("F3"));
        _sb.AppendLine();

        // 基准链形状
        Vector3 span0 = p0[n - 1] - p0[0];
        float len = 0f;
        for (int i = 0; i + 1 < n; i++) len += (p0[i + 1] - p0[i]).magnitude;
        _sb.AppendLine("── 基准链（未加任何偏移）──");
        _sb.AppendLine("  首节局部 " + p0[0].ToString("F3") + "   末节局部 " + p0[n - 1].ToString("F3"));
        _sb.AppendLine("  首尾位移 " + span0.ToString("F3") + "   长度=" + span0.magnitude.ToString("F3") + " m");
        _sb.AppendLine("  折线总长 " + len.ToString("F3") + " m");
        _sb.AppendLine("  首节方向 " + (p0[1] - p0[0]).normalized.ToString("F3")
                       + "  逐节弯角 " + _perJoint[0].ToString("F3") + "°  (0 = 完全笔直)");
        _sb.AppendLine();

        _sb.AppendLine("── 每节统一叠 15° 偏移 ──");
        _sb.AppendLine("测试            逐节弯角   轴↔链方向    末端位移(局部)              首尾连线偏转");
        for (int k = 0; k < 4; k++)
        {
            string av = _axisVsDir[k] < 0f ? "  n/a " : _axisVsDir[k].ToString("F1").PadLeft(5) + "°";
            _sb.AppendLine(string.Format("{0,-14} {1,8:F2}° {2,10}  ({3,7:F2},{4,7:F2},{5,7:F2})  {6,8:F2}°",
                Names[k], _perJoint[k], av,
                _tailDelta[k].x, _tailDelta[k].y, _tailDelta[k].z, _headToTail[k]));
        }
        _sb.AppendLine();
        _sb.AppendLine("判读：");
        _sb.AppendLine("  逐节弯角 ≈ 15° 且 轴↔链方向 ≈ 90° ⇒ 该轴是【弯曲】");
        _sb.AppendLine("  逐节弯角 ≈ 0°  且 轴↔链方向 ≈ 0°  ⇒ 该轴是【自转/扭转】，不产生弯曲");
        _sb.AppendLine("  末端位移 y 分量 ≈ 0  ⇒ 弯在【水平面】；y 分量显著 ⇒ 弯在【垂直面】");

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_axis.txt", s); } catch { }
        _done = true;
        enabled = false;
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_axis");
g.AddComponent<dg_axis>();
return "DG_AXIS_STARTED";
