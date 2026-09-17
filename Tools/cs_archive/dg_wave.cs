using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_wave：直接量**每节的 localRotation 实际偏移量**（相对 _baseRot），
//          以及链在**世界空间**的弯曲角。回答：
//            · ApplySpineOffsetsRaw 到底有没有把旋转写进去
//            · 每节实际转了多少度、绕哪个轴
public class dg_wave : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    readonly StringBuilder _sb = new StringBuilder();
    float _t; int _step;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        _dragon = Instantiate(pf, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
        _dragon.name = "dg_wave_Dragon";
        var player = GameObject.Find("Player");
        if (player != null) player.transform.position = new Vector3(0f, 0.05f, 9f);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        _comp = _dragon.GetComponentInChildren(_ty, true);
    }

    void Update()
    {
        _t += Time.deltaTime;
        // 采样 4 个时刻，看波形是否随时间变化
        if (_t < 4.0f + _step * 0.35f || _step >= 4) return;
        _step++;

        var fSpine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        var fBase = _ty.GetField("_baseRot", BindingFlags.NonPublic | BindingFlags.Instance);
        var raw = fSpine.GetValue(_comp) as System.Collections.IList;
        var bs = fBase.GetValue(_comp) as System.Collections.IList;
        if (raw == null || raw.Count == 0) { Debug.Log("dg_wave ✗ spine 空"); enabled = false; return; }

        _sb.AppendLine("──── 采样 t=" + _t.ToString("F2") + "s ────");
        _sb.AppendLine(" i | 相对基准的偏移角 | 偏移欧拉(轴角) | 世界Z轴与切线夹角");
        var M = _dragon.transform;
        for (int i = 0; i < raw.Count; i++)
        {
            var tr = raw[i] as Transform;
            var bq = (Quaternion)bs[i];
            float off = Quaternion.Angle(bq, tr.localRotation);
            Vector3 ax; float ang;
            (tr.localRotation * Quaternion.Inverse(bq)).ToAngleAxis(out ang, out ax);

            // 世界弯曲：本节的朝向与上一节的夹角
            float bend = 0f;
            if (i > 0)
            {
                var prev = raw[i - 1] as Transform;
                Vector3 d1 = (M.InverseTransformPoint(prev.position) - M.InverseTransformPoint(tr.position)).normalized;
                if (i > 1)
                {
                    var p2 = raw[i - 2] as Transform;
                    Vector3 d0 = (M.InverseTransformPoint(p2.position) - M.InverseTransformPoint(prev.position)).normalized;
                    bend = Vector3.Angle(d0, d1);
                }
            }
            _sb.AppendLine(string.Format("{0,3} | {1,10:F2}° | ({2,5:F2},{3,5:F2},{4,5:F2}) {5,5:F1}° | {6,8:F2}°",
                i, off, ax.x, ax.y, ax.z, ang, bend));
        }
        if (_step >= 4)
        {
            Debug.Log(_sb.ToString());
            try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_wave.txt", _sb.ToString()); } catch { }
            enabled = false;
        }
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_wave");
g.AddComponent<dg_wave>();
return "DG_WAVE_STARTED";
