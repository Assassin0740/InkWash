using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// d17：定位"落地窗口" —— 逐 0.25 s 采样龙的高度与相位，
//      回答：盘旋时它到底在不在天上？哪个相位把它拉到地面？
public class d17_probe : MonoBehaviour
{
    Type _t; Component _d; Transform _modelRoot;
    float _el, _lastLog; int _n;
    PropertyInfo _pPhase, _pAir, _pY;
    FieldInfo _fLift, _fCurLift, _fModelRoot;
    Transform _root;
    Vector3 _baseLocal;
    readonly StringBuilder _sb = new StringBuilder();

    void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { _t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (_t != null) break; }
        if (_t == null) { Debug.Log("d17 ✗ 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (prefab == null) { Debug.Log("d17 ✗ 预制体"); return; }

        var pt = FindType("InkWash.Combat.PlayerRef");
        Vector3 pp = Vector3.zero;
        if (pt != null)
        {
            var pm = pt.GetProperty("Position", BindingFlags.Public | BindingFlags.Static);
            if (pm != null) pp = (Vector3)pm.GetValue(null);
        }

        var go = Instantiate(prefab, pp + new Vector3(6f, 0.05f, 4f), Quaternion.identity);
        go.name = "D17_MoLong";
        _d = go.GetComponentInChildren(_t, true);
        if (_d == null) { Debug.Log("d17 ✗ 组件"); return; }

        var fLoop = _t.GetField("aerialLoop");
        if (fLoop != null) fLoop.SetValue(_d, true);

        _pPhase = _t.GetProperty("DivePhaseName", BindingFlags.Public | BindingFlags.Instance);
        _pAir = _t.GetProperty("Airborne", BindingFlags.Public | BindingFlags.Instance);
        _fCurLift = _t.GetField("_currentLift", BindingFlags.NonPublic | BindingFlags.Instance);
        _fModelRoot = _t.GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);

        Debug.Log("d17 就位 玩家=" + pp.ToString("F2"));
    }

    void Update()
    {
        if (_d == null) return;
        _el += Time.unscaledDeltaTime;
        if (_el - _lastLog < 0.25f) return;
        _lastLog = _el; _n++;

        if (_root == null && _fModelRoot != null)
        {
            _root = _fModelRoot.GetValue(_d) as Transform;
            if (_root != null) _baseLocal = _root.localPosition;
        }

        string ph = _pPhase != null ? (_pPhase.GetValue(_d, null) as string) : "-";
        bool air = false;
        if (_pAir != null) { var v = _pAir.GetValue(_d, null); if (v is bool) air = (bool)v; }
        float lift = 0f;
        if (_fCurLift != null) { var v = _fCurLift.GetValue(_d); if (v is float) lift = (float)v; }

        Vector3 rootPos = _root != null ? _root.position : Vector3.zero;
        float localY = _root != null ? _root.localPosition.y : 0f;
        float dxz = 0f;
        var pt = FindType("InkWash.Combat.PlayerRef");
        if (pt != null)
        {
            var pm = pt.GetProperty("Position", BindingFlags.Public | BindingFlags.Static);
            if (pm != null)
            {
                Vector3 pv = (Vector3)pm.GetValue(null);
                dxz = new Vector2(rootPos.x - pv.x, rootPos.z - pv.z).magnitude;
            }
        }

        _sb.Length = 0;
        _sb.Append(string.Format("[D17] t={0,5:F2} phase={1,-8} air={2,-5} lift={3,5:F2} rootY={4,7:F2} localY={5,6:F2} 距玩家={6,5:F1}",
            _el, ph, air, lift, rootPos.y, localY, dxz));
        Debug.Log(_sb.ToString());

        if (_n >= 60) { Debug.Log("d17 采样完毕 60 点"); enabled = false; }
    }

    static Type FindType(string full)
    {
        var t = Type.GetType(full + ", Assembly-CSharp");
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType(full); if (t != null) return t; }
        return null;
    }
}

var g17 = new GameObject("D17_Probe");
g17.AddComponent<d17_probe>();
return "D17_STARTED";
