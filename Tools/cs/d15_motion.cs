using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// d15：量"盘旋时的身体到底动没动"
// 判据：脊骨各节的 localRotation 逐帧变化幅度。若只有前几节在动 ⇒ 就是"只有头在动"。
public class D15_Motion : MonoBehaviour
{
    private Component _dragon;
    private readonly List<Transform> _spine = new List<Transform>();
    private readonly List<Quaternion> _baseRot = new List<Quaternion>();
    private readonly List<float> _maxDelta = new List<float>();
    private readonly List<Transform> _all = new List<Transform>();
    private Transform _modelRoot;
    private float _t;
    private int _frames;
    private string _stateName = "-";
    private bool _airborne;
    private string _phaseName = "-";
    private float _lift;
    private readonly StringBuilder _sb = new StringBuilder();

    private MethodInfo _miGetProp;

    void Start()
    {
        var go = GameObject.Find("D2_MoLong");
        if (go == null)
        {
            foreach (var t in FindObjectsOfType<Transform>(true))
            {
                if (t.name.Contains("MoLong") || t.name.Contains("Dragon"))
                { go = t.gameObject; break; }
            }
        }
        if (go == null) { Debug.Log("[D15] 场景里没有龙"); _t = 9999f; return; }

        var monos = go.GetComponents<MonoBehaviour>();
        foreach (var m in monos)
        {
            if (m != null && m.GetType().Name == "EnemyDragon") { _dragon = m; break; }
        }
        if (_dragon == null) { Debug.Log("[D15] 没找到 EnemyDragon 组件"); _t = 9999f; return; }

        var ty = _dragon.GetType();

        // 用反射拿私有字段 _spine / _baseRot / _modelRoot
        var fSpine = ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fSpine != null)
        {
            var list = fSpine.GetValue(_dragon) as System.Collections.IEnumerable;
            if (list != null)
                foreach (var o in list) { var tr = o as Transform; if (tr != null) { _spine.Add(tr); _maxDelta.Add(0f); } }
        }
        var fBase = ty.GetField("_baseRot", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fBase != null)
        {
            var list = fBase.GetValue(_dragon) as System.Collections.IEnumerable;
            if (list != null)
                foreach (var o in list) if (o is Quaternion) _baseRot.Add((Quaternion)o);
        }
        var fRoot = ty.GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fRoot != null) _modelRoot = fRoot.GetValue(_dragon) as Transform;
        var fLift = ty.GetField("_currentLift", BindingFlags.NonPublic | BindingFlags.Instance);

        Debug.Log(string.Format("[D15] 龙={0} 脊骨节数={1} baseRot={2} modelRoot={3}",
            go.name, _spine.Count, _baseRot.Count, _modelRoot != null ? _modelRoot.name : "<null>"));

        _miGetProp = ty.GetProperty("DivePhaseName", BindingFlags.Public | BindingFlags.Instance) != null
            ? ty.GetProperty("DivePhaseName", BindingFlags.Public | BindingFlags.Instance).GetGetMethod() : null;
        _fLift = fLift;
        _ty = ty;
        _go = go;
    }

    private System.Type _ty;
    private GameObject _go;
    private FieldInfo _fLift;

    // 存在性检查：清单说这个脚本要求至少一条顶层语句 ⇒ 这里自带 bootstrap
    // （实际由 exec_cs.py 追加 `new GameObject("D15").AddComponent<D15_Motion>();`）

    void Update()
    {
        if (_dragon == null) return;
        _t += Time.deltaTime;
        _frames++;

        // 逐帧记录每节相对基准姿态的最大偏角
        for (int i = 0; i < _spine.Count && i < _baseRot.Count; i++)
        {
            if (_spine[i] == null) continue;
            float d = Quaternion.Angle(_spine[i].localRotation, _baseRot[i]);
            if (d > _maxDelta[i]) _maxDelta[i] = d;
        }

        if (_ty != null)
        {
            var pState = _ty.GetProperty("DebugStateName", BindingFlags.Public | BindingFlags.Instance);
            if (pState != null) _stateName = pState.GetValue(_dragon, null) as string ?? _stateName;
            var pAir = _ty.GetProperty("Airborne", BindingFlags.Public | BindingFlags.Instance);
            if (pAir != null) { var v = pAir.GetValue(_dragon, null); if (v is bool) _airborne = (bool)v; }
            if (_miGetProp != null) _phaseName = _miGetProp.Invoke(_dragon, null) as string ?? _phaseName;
            if (_fLift != null) { var v = _fLift.GetValue(_dragon); if (v is float) _lift = (float)v; }
        }

        if (_t >= 18f)
        {
            _sb.Length = 0;
            _sb.AppendLine("[D15] ===== 脊骨运动幅度（相对基准姿态的最大偏角，度） =====");
            _sb.AppendLine("[D15] state=" + _stateName + " airborne=" + _airborne + " phase=" + _phaseName + " lift=" + _lift.ToString("F2"));
            for (int i = 0; i < _maxDelta.Count; i++)
            {
                string tag = _maxDelta[i] < 1.0f ? "  <== 没动" : (_maxDelta[i] < 5f ? "  <== 微动" : "");
                _sb.AppendLine(string.Format("[D15] 节{0,2}  maxΔ={1,6:F2}°{2}", i, _maxDelta[i], tag));
            }
            Debug.Log(_sb.ToString());
            _t = -99999f;
            enabled = false;
        }
    }
}

// bootstrap: Roslyn 脚本要求至少一条顶层语句
var d15go = new GameObject("D15_Probe");
d15go.AddComponent<D15_Motion>();
Debug.Log("[D15] probe mounted");
