using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_swim：验证新游动：拍 4 个时刻的**链形状**，量
//   ① 每节实际弯曲偏移角（应接近 hoverAmplitudeDeg）
//   ② 世界空间"身体弯曲度"：首尾连线 vs 中段偏离（= 有没有真的弯成蛇形）
//   ③ 头/中/尾的横向摆幅对比（应头小尾大）
public class dg_swim : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    readonly StringBuilder _sb = new StringBuilder();
    float _t; int _step;
    Transform[] _bones;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        _dragon = Instantiate(pf, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
        _dragon.name = "dg_swim_Dragon";
        var player = GameObject.Find("Player");
        if (player != null) player.transform.position = new Vector3(0f, 0.05f, 9f);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        _comp = _dragon.GetComponentInChildren(_ty, true);
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < 4.0f + _step * 0.25f) return;
        if (_step >= 4) { Flush(); enabled = false; return; }
        _step++;

        var fSpine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        var fBase = _ty.GetField("_baseRot", BindingFlags.NonPublic | BindingFlags.Instance);
        var raw = fSpine.GetValue(_comp) as System.Collections.IList;
        var bs = fBase.GetValue(_comp) as System.Collections.IList;
        if (raw == null || raw.Count == 0) { Debug.Log("dg_swim ✗"); enabled = false; return; }

        var M = _dragon.transform;
        if (_bones == null || _bones.Length != raw.Count)
        {
            _bones = new Transform[raw.Count];
            for (int i = 0; i < raw.Count; i++) _bones[i] = raw[i] as Transform;
        }

        // ① 最大偏移角
        float maxOff = 0f, minOff = 999f, sum = 0f;
        for (int i = 0; i < raw.Count; i++)
        {
            float off = Quaternion.Angle((Quaternion)bs[i], _bones[i].localRotation);
            maxOff = Mathf.Max(maxOff, off); minOff = Mathf.Min(minOff, off); sum += off;
        }

        // ② 自身坐标系里的横向位置（x）—— 看波形形状
        var lat = new float[_bones.Length];
        for (int i = 0; i < _bones.Length; i++)
            lat[i] = M.InverseTransformPoint(_bones[i].position).x;
        float latMax = -999f, latMin = 999f;
        foreach (var v in lat) { latMax = Mathf.Max(latMax, v); latMin = Mathf.Min(latMin, v); }

        // ③ 分段摆幅（头 1/3 / 中 1/3 / 尾 1/3 的 x 跨度）
        int n = lat.Length, t3 = Mathf.Max(1, n / 3);
        float sHead = Span(lat, 0, t3), sMid = Span(lat, t3, 2 * t3), sTail = Span(lat, 2 * t3, n);

        _sb.AppendLine(string.Format(
            "t={0,4:F2} | 偏移角 avg={1,5:F2} max={2,5:F2} min={3,5:F2} | 横向跨度 {4,6:F2} m | 头 {5,6:F2} 中 {6,6:F2} 尾 {7,6:F2}",
            _t, sum / raw.Count, maxOff, minOff, latMax - latMin, sHead, sMid, sTail));
    }

    static float Span(float[] a, int i0, int i1)
    {
        float mn = 999f, mx = -999f;
        for (int i = i0; i < i1 && i < a.Length; i++) { mn = Mathf.Min(mn, a[i]); mx = Mathf.Max(mx, a[i]); }
        return mx - mn;
    }

    void Flush()
    {
        Debug.Log(_sb.ToString());
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_swim.txt", _sb.ToString()); } catch { }
    }
    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_swim");
g.AddComponent<dg_swim>();
return "DG_SWIM_STARTED";
