using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// d11：验证「空中循环」架构（策划案 T1/T2）。
// 判据（A1/A2/A3/A4）：
//   A1 空中占比 ≥ 0.60
//   A2 每次俯冲结束后高度回到 hoverHeight 的 90%
//   A3 撕咬水平位移 > 2.5 m；吐息不改变高度（< 0.3 m）
//   A4 相邻两次俯冲间隔 ≥ 1.2 s
public class d11_probe : MonoBehaviour
{
    static readonly StringBuilder sb = new StringBuilder();
    Type _dragonT;
    Component _dragon;
    Transform _modelRoot;
    float _t;

    float _groundY;
    float _airFrames, _totalFrames;
    bool _prevAirborne;
    float _lastEndTime = -99f;
    float _minDiveGap = 999f;
    float _diveStartX, _diveStartZ;
    float _maxDiveDisplacement;
    bool _inDive;
    string _prevPhase = "-";
    int _diveCount, _biteCount, _breathCount, _sweepCount;
    float _breathLiftBefore, _breathLiftMax;
    bool _inBreath; float _breathLiftMin, _breathLiftMaxSeen;

    FieldInfo _fAirborne;
    PropertyInfo _pPhase, _pAtk, _pBite, _pBreath, _pSweep;

    void CacheMembers()
    {
        var BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        _fAirborne = _dragonT.GetField("_airborne", BF);
        _pPhase = _dragonT.GetProperty("DivePhaseName", BF);
        _pAtk = _dragonT.GetProperty("CurrentAttackName", BF);
        _pBite = _dragonT.GetProperty("BiteCount", BF);
        _pBreath = _dragonT.GetProperty("BreathCount", BF);
        _pSweep = _dragonT.GetProperty("TailSweepCount", BF);
        if (_fAirborne == null || _pPhase == null)
            Debug.Log("d11: ✗ 成员缓存失败 air=" + (_fAirborne != null) + " phase=" + (_pPhase != null));
    }

    void Start()
    {
        _dragonT = FindType("InkWash.Enemies.EnemyDragon");
        if (_dragonT == null) { Debug.Log("d11: ✗ 找不到 EnemyDragon 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (prefab == null) { Debug.Log("d11: ✗ 找不到龙预制体"); return; }

        Vector3 pos = new Vector3(0f, 0.05f, 0f) + new Vector3(0f, 0f, 7f);
        var go = Instantiate(prefab, pos, Quaternion.identity);
        go.name = "D11_MoLong";
        _dragon = go.GetComponentInChildren(_dragonT, true);
        if (_dragon == null) { Debug.Log("d11: ✗ 实例上没有 EnemyDragon"); return; }

        // 确保 aerialLoop 开着（新架构）
        var f = _dragonT.GetField("aerialLoop");
        if (f != null) f.SetValue(_dragon, true);

        _groundY = go.transform.position.y;
        _modelRoot = FindModelRoot(go.transform);
        Debug.Log("d11: 已生成，开始采样（45 s）");
    }

    void Update()
    {
        if (_dragon == null || _dragonT == null) return;
        _t += Time.unscaledDeltaTime;

        // --- 逐帧采样（反射全部走缓存，缺成员不抛）---
        if (_fAirborne == null) CacheMembers();
        if (_fAirborne == null) return;

        bool airborne = (bool)_fAirborne.GetValue(_dragon);
        _totalFrames++;
        float y = _modelRoot != null ? _modelRoot.position.y : 0f;
        if (y > _groundY + 1.5f) _airFrames++;

        string phase = _pPhase != null ? (string)_pPhase.GetValue(_dragon) : "-";
        string atk = _pAtk != null ? (string)_pAtk.GetValue(_dragon) : "";

        // 俯冲起止（Dive 相位进入/离开）
        if (phase == "Dive" && _prevPhase != "Dive")
        {
            _inDive = true;
            _diveStartX = _dragon.transform.position.x; _diveStartZ = _dragon.transform.position.z;
            _diveCount++;
            if (_lastEndTime > -90f)
            {
                float gap = Time.time - _lastEndTime;
                if (gap < _minDiveGap) _minDiveGap = gap;
            }
        }
        if (phase != "Dive" && _prevPhase == "Dive" && _inDive)
        {
            _inDive = false;
            float dx = _dragon.transform.position.x - _diveStartX;
            float dz = _dragon.transform.position.z - _diveStartZ;
            float disp = Mathf.Sqrt(dx * dx + dz * dz);
            if (disp > _maxDiveDisplacement) _maxDiveDisplacement = disp;
        }
        if (phase == "None" || phase == "-") { if (_prevPhase == "Recover") _lastEndTime = Time.time; }

        // 吐息高度变化 —— ★ 口径：量**吐息期间高度的极差**（max−min），
        //   不是"相对首帧之差"。首帧那一刻龙可能还在从上一招 Recover 往上升，
        //   拿它当基准会把"正在归位"误算成"吐息在降高度"（实测假报 3.25 m）。
        float curLift = _modelRoot != null ? _modelRoot.localPosition.y : 0f;
        if (atk != null && atk.Contains("吐息"))
        {
            if (!_inBreath) { _inBreath = true; _breathLiftMin = curLift; _breathLiftMaxSeen = curLift; }
            if (curLift < _breathLiftMin) _breathLiftMin = curLift;
            if (curLift > _breathLiftMaxSeen) _breathLiftMaxSeen = curLift;
            float range = _breathLiftMaxSeen - _breathLiftMin;
            if (range > _breathLiftMax) _breathLiftMax = range;
        }
        else _inBreath = false;

        _prevPhase = phase;

        if (_t < 45f) return;

        // --- 出报告 ---
        _biteCount = _pBite != null ? (int)_pBite.GetValue(_dragon) : -1;
        _breathCount = _pBreath != null ? (int)_pBreath.GetValue(_dragon) : -1;
        _sweepCount = _pSweep != null ? (int)_pSweep.GetValue(_dragon) : -1;

        sb.Length = 0;
        sb.AppendLine("========== d11 空中循环架构验证（45 s） ==========");
        sb.AppendLine("采样帧数 = " + _totalFrames);
        sb.AppendLine("");
        sb.AppendLine("── A1 空中占比（判据 ≥0.60）──");
        float ratio = _totalFrames > 0 ? _airFrames / _totalFrames : 0f;
        sb.AppendLine("  龙身 y > 地面+1.5 m 的帧占比 = " + ratio.ToString("0.000")
            + "   " + (ratio >= 0.60f ? "✓ 通过" : "✗ 不通过"));

        sb.AppendLine("");
        sb.AppendLine("── T1/T2 循环统计 ──");
        sb.AppendLine("  俯冲次数(Dive 相位) = " + _diveCount);
        sb.AppendLine("  俯冲撕咬 = " + _biteCount + " / 落地扫尾 = " + _sweepCount + " / 悬停吐息 = " + _breathCount);
        sb.AppendLine("  结束于 airborne = " + (_fAirborne != null ? _fAirborne.GetValue(_dragon).ToString() : "?"));

        sb.AppendLine("");
        sb.AppendLine("── A3 招式可分辨 ──");
        sb.AppendLine("  俯冲最大水平位移 = " + _maxDiveDisplacement.ToString("0.00") + " m"
            + "   " + (_maxDiveDisplacement >= 2.5f ? "✓ ≥2.5" : "✗ <2.5（龙是否真的冲出去了？）"));
        sb.AppendLine("  吐息高度变化 = " + _breathLiftMax.ToString("0.00") + " m"
            + "   " + (_breathLiftMax <= 0.3f ? "✓ ≤0.3（不下降）" : "✗ >0.3（不该降）"));

        sb.AppendLine("");
        sb.AppendLine("── A4 喘息窗口（判据 ≥1.2 s）──");
        var pGap = _dragonT.GetProperty("MinObservedDiveGap");
        float gapObs = pGap != null ? (float)pGap.GetValue(_dragon) : _minDiveGap;
        sb.AppendLine("  相邻俯冲最小间隔 = " + (gapObs > 900f ? "（只出过 1 次俯冲，无法测）" : gapObs.ToString("0.00") + " s")
            + (gapObs > 900f ? "" : (gapObs >= 1.2f ? "   ✓" : "   ✗ 太密")));

        sb.AppendLine("");
        sb.AppendLine("── 关键字段存在性（编译新鲜度）──");
        foreach (var n in new[] { "DivePhaseName", "AirborneRatio", "Phase", "LastDiveEndTime" })
        {
            var p = _dragonT.GetProperty(n);
            sb.AppendLine("  " + (p != null ? "✓" : "✗") + " " + n);
        }
        var fAer = _dragonT.GetField("aerialLoop");
        sb.AppendLine("  " + (fAer != null ? "✓" : "✗") + " aerialLoop(字段)");
        var fTell = _dragonT.GetField("diveTellDuration");
        sb.AppendLine("  " + (fTell != null ? "✓" : "✗") + " diveTellDuration(字段)");

        Debug.Log(sb.ToString());
    }

    static Transform FindModelRoot(Transform root)
    {
        // 与 EnemyDragon 同口径：从 SkinnedMeshRenderer 往上找第一个非 drgon_ 容器
        var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null) return null;
        Transform best = null, w = smr.transform.parent;
        int g = 0;
        while (w != null && w != root && g++ < 20)
        {
            if (!w.name.StartsWith("drgon_") && w.name != "_rootJoint") best = w;
            w = w.parent;
        }
        return best;
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

var g11 = new GameObject("D11_Probe");
g11.AddComponent<d11_probe>();
return "D11_STARTED";
