using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// d13：诊断龙卡在哪个状态（d11 显示 airborne 恒 false、0 次攻击）
public class d13_probe : MonoBehaviour
{
    static readonly StringBuilder sb = new StringBuilder();
    Type _dragonT; Component _d;
    FieldInfo _fState, _fStateTime, _fDist, _fAirborne, _fGroundCaptured, _fCooldown;
    PropertyInfo _pCanSee;   // 可能不存在
    MethodInfo _mCanSee;
    float _el; int _tick;

    void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { _dragonT = asm.GetType("InkWash.Enemies.EnemyDragon"); if (_dragonT != null) break; }
        if (_dragonT == null) { Debug.Log("d13 ✗ 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        var go = Instantiate(prefab, new Vector3(0f, 0.05f, 7f), Quaternion.identity);
        go.name = "D13_MoLong";
        _d = go.GetComponentInChildren(_dragonT, true);

        var BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        _fState = _dragonT.GetField("_state", BF);
        _fStateTime = _dragonT.GetField("_stateTime", BF);
        _fDist = _dragonT.GetField("_distanceToPlayer", BF);
        _fAirborne = _dragonT.GetField("_airborne", BF);
        _fGroundCaptured = _dragonT.GetField("_groundCaptured", BF);
        _fCooldown = _dragonT.GetField("_cooldownTimer", BF);
        _mCanSee = _dragonT.GetMethod("CanSeePlayer", BF);
        // 也看看基类的私有
        if (_fState == null)
        {
            var bt = _dragonT.BaseType;
            _fState = bt.GetField("_state", BF);
            _fStateTime = bt.GetField("_stateTime", BF);
            _fDist = bt.GetField("_distanceToPlayer", BF);
            _mCanSee = bt.GetMethod("CanSeePlayer", BF);
        }
        sb.AppendLine("d13 成员：_state=" + (_fState != null) + " _stateTime=" + (_fStateTime != null)
            + " _dist=" + (_fDist != null) + " _airborne=" + (_fAirborne != null)
            + " CanSeePlayer=" + (_mCanSee != null) + " _groundCaptured=" + (_fGroundCaptured != null));
    }

    void Update()
    {
        if (_d == null) return;
        _el += Time.unscaledDeltaTime;
        _tick++;
        if (_tick % 60 != 0) return;   // 每秒记一次

        sb.Length = 0;
        sb.Append("d13 t=" + _el.ToString("0.0") + "s");
        if (_fState != null) sb.Append("  state=" + _fState.GetValue(_d));
        if (_fStateTime != null) sb.Append("  stateTime=" + ((float)_fStateTime.GetValue(_d)).ToString("0.00"));
        if (_fDist != null) sb.Append("  dist=" + ((float)_fDist.GetValue(_d)).ToString("0.00"));
        if (_fAirborne != null) sb.Append("  air=" + _fAirborne.GetValue(_d));
        if (_fCooldown != null) sb.Append("  cd=" + ((float)_fCooldown.GetValue(_d)).ToString("0.00"));
        if (_mCanSee != null)
        {
            try { sb.Append("  canSee=" + _mCanSee.Invoke(_d, null)); } catch (Exception e) { sb.Append("  canSee=ERR:" + e.InnerException?.Message); }
        }
        if (_d.transform != null) sb.Append("  posY=" + _d.transform.position.y.ToString("0.00"));
        Debug.Log(sb.ToString());

        if (_el > 12f) enabled = false;
    }
}

var g = new GameObject("D13_Probe");
g.AddComponent<d13_probe>();
return "D13_STARTED";
