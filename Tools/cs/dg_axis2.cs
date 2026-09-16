using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_axis2：在**当前（已拉直）的基准姿态**上重测蛇形波的弯曲平面。
//
// 为什么必须重测：`dg_axis` 的结论（局部 Y = 水平弯、局部 Z = 垂直弯）是在
// **未拉直的大 C 形基准**上量的。之后加了 StraightenSpineBase()，
// 每节骨骼都被一个 FromToRotation 修正过 —— 这个修正是**绕链方向带 roll** 的，
// 所以"局部 Y 指向哪"整条链逐节都变了。
//
// 本探针直接量**最终效果**，不再问轴向：
//   把节点位置取到 modelRoot 局部系，看三种驱动下链节点的
//     横向跨度(x) / 垂直跨度(y) / 沿体轴跨度(z)
//   哪个跨度大，就知道身体到底在哪个平面里摆。
//
//   案例 0：基准（无波）
//   案例 1：现有写法  base * Euler(0, off, 0)            ← 局部 Y
//   案例 2：候选写法  AngleAxis(off, up) * baseWorld      ← 世界/模型「上」轴
public class dg_axis2 : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine, _base;
    Transform _root;
    readonly StringBuilder _sb = new StringBuilder();

    const int Cases = 3;
    const int Phases = 8;
    const float Amp = 20f;        // 每节振幅（度），取大一点便于读数
    const float StepDeg = 15f;    // 相位步进（度/节），与生产一致

    int _n;
    Vector3[][][] _pos;           // [case][phase][node]
    Quaternion[] _baseWorld;
    int _case, _phase, _wait = 2;
    bool _done;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_axis2: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_axis2_Dragon";
        _dragon.transform.position = Vector3.zero;
        _dragon.transform.rotation = Quaternion.identity;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { Debug.LogError("dg_axis2: 找不到 EnemyDragon"); enabled = false; return; }

        _comp = _dragon.GetComponentInChildren(_ty, true);
        if (_comp == null) { Debug.LogError("dg_axis2: 龙身上没有 EnemyDragon"); enabled = false; return; }

        _spine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(_comp) as System.Collections.IList;
        _base = _ty.GetField("_baseRot", BindingFlags.NonPublic | BindingFlags.Instance)
                   .GetValue(_comp) as System.Collections.IList;
        var fRoot = _ty.GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fRoot != null) _root = fRoot.GetValue(_comp) as Transform;
        if (_root == null) _root = _dragon.transform;

        if (_spine == null || _spine.Count == 0)
        { Debug.LogError("dg_axis2: spine 空"); enabled = false; return; }

        _n = _spine.Count;
        _baseWorld = new Quaternion[_n];
        for (int i = 0; i < _n; i++) _baseWorld[i] = (_spine[i] as Transform).rotation;

        _pos = new Vector3[Cases][][];
        for (int c = 0; c < Cases; c++)
        {
            _pos[c] = new Vector3[Phases][];
            for (int p = 0; p < Phases; p++) _pos[c][p] = new Vector3[_n];
        }

        Disable();
        Apply(0, 0);
    }

    void Disable()
    {
        var mb = _comp as MonoBehaviour;
        if (mb != null && mb.enabled) mb.enabled = false;
    }

    void Update()
    {
        Disable();
        if (_done) return;
        if (_wait > 0) { _wait--; if (_wait == 0) Measure(); return; }

        if (_phase < Phases - 1) { _phase++; Apply(_case, _phase); _wait = 1; return; }
        if (_case < Cases - 1) { _case++; _phase = 0; Apply(_case, 0); _wait = 1; return; }
        Finish();
    }

    void Apply(int c, int phase)
    {
        float ph = phase * (2f * Mathf.PI / Phases);
        float step = StepDeg * Mathf.Deg2Rad;
        for (int i = 0; i < _n; i++)
        {
            var tr = _spine[i] as Transform;
            float off = Amp * Mathf.Sin(ph - i * step);
            if (c == 0) tr.localRotation = (Quaternion)_base[i];
            else if (c == 1) tr.localRotation = (Quaternion)_base[i] * Quaternion.Euler(0f, off, 0f);
            else tr.rotation = Quaternion.AngleAxis(off, Vector3.up) * _baseWorld[i];
        }
    }

    void Measure()
    {
        for (int i = 0; i < _n; i++)
            _pos[_case][_phase][i] = _root.InverseTransformPoint((_spine[i] as Transform).position);
    }

    void Finish()
    {
        string[] names = { "基准（无波）", "现有 base*Euler(0,yaw,0) 局部Y", "候选 AngleAxis(yaw,up)*baseWorld" };
        int[] probe = { 0, 6, 12, 17, 23 };

        _sb.AppendLine("龙骨蛇形波「弯曲平面」实测  节点数=" + _n
                       + "  振幅=" + Amp.ToString("F0") + "°/节  相位步进=" + StepDeg.ToString("F0") + "°/节");
        _sb.AppendLine("坐标 = modelRoot 局部系；x=横向  y=垂直  z=沿体轴");
        _sb.AppendLine();
        _sb.AppendLine("案例                             节点   横向跨度x   垂直跨度y   沿轴跨度z   主摆平面");
        for (int c = 0; c < Cases; c++)
        {
            for (int k = 0; k < probe.Length; k++)
            {
                int i = probe[k];
                float xmin = float.MaxValue, xmax = float.MinValue;
                float ymin = float.MaxValue, ymax = float.MinValue;
                float zmin = float.MaxValue, zmax = float.MinValue;
                for (int p = 0; p < Phases; p++)
                {
                    var v = _pos[c][p][i];
                    if (v.x < xmin) xmin = v.x; if (v.x > xmax) xmax = v.x;
                    if (v.y < ymin) ymin = v.y; if (v.y > ymax) ymax = v.y;
                    if (v.z < zmin) zmin = v.z; if (v.z > zmax) zmax = v.z;
                }
                float xr = xmax - xmin, yr = ymax - ymin, zr = zmax - zmin;
                string plane = c == 0 ? "-" : (xr >= yr ? "【左右 horizontal】" : "【上下 vertical】");
                _sb.AppendLine(string.Format("{0,-32} n{1,3}   {2,8:F3}   {3,8:F3}   {4,8:F3}   {5}",
                    c == 0 ? names[c] : names[c], i, xr, yr, zr, plane));
            }
        }
        _sb.AppendLine();
        _sb.AppendLine("判读：x 跨度 >> y 跨度 ⇒ 左右蜿蜒；y 跨度 >> x 跨度 ⇒ 上下蜿蜒。");

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_axis2.txt", s); } catch { }
        _done = true;
        enabled = false;
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g2 = new GameObject("dg_axis2");
g2.AddComponent<dg_axis2>();
return "DG_AXIS2_STARTED";
