using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_shape：量**链的几何形状**：逐节世界位置 → 判断"是否真的弯曲"。
//   关键指标：
//     · 每节与前一节的距离（= 该节骨长，应恒定）
//     · 相邻节方向的变化角（= 曲率，游动时应沿链平滑变化）
//     · 首/中/尾的横向偏移（= 蛇形波的振幅在哪一段）
public class dg_shape : MonoBehaviour
{
    GameObject _dragon;
    Component _comp; Type _ty;
    readonly StringBuilder _sb = new StringBuilder();
    float _t; int _step;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.Log("dg_shape ✗"); enabled = false; return; }
        _dragon = Instantiate(pf, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
        _dragon.name = "dg_shape_Dragon";
        var player = GameObject.Find("Player");
        if (player != null) player.transform.position = new Vector3(0f, 0.05f, 9f);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        _comp = _dragon.GetComponentInChildren(_ty, true);
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < 4.0f || _step != 0) return;
        _step = 1;

        var fSpine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        var raw = fSpine.GetValue(_comp) as System.Collections.IList;
        if (raw == null || raw.Count == 0) { Debug.Log("dg_shape ✗ spine 空"); return; }

        var bones = new Transform[raw.Count];
        for (int i = 0; i < raw.Count; i++) bones[i] = raw[i] as Transform;

        // 用龙的 transform 做参照，把每节转到"龙自身坐标系"
        var M = _dragon.transform;
        _sb.AppendLine("=== 链形状（龙自身坐标系：fwd=+Z, right=+X, up=+Y）===");
        _sb.AppendLine(" i |  name     | 距前一节 | 自身坐标 (x=横, y=高, z=前) | 相邻方向角 | 累计横偏");

        Vector3 prevLocal = M.InverseTransformPoint(bones[0].position);
        float accLat = 0f;
        for (int i = 0; i < bones.Length; i++)
        {
            Vector3 lp = M.InverseTransformPoint(bones[i].position);
            float segLen = i == 0 ? 0f : Vector3.Distance(lp, prevLocal);
            float bend = 0f;
            if (i > 0)
            {
                Vector3 dPrev = (prevLocal - M.InverseTransformPoint(bones[i - 1].position));
                Vector3 dCur = (lp - prevLocal);
                if (dPrev.sqrMagnitude > 1e-6f && dCur.sqrMagnitude > 1e-6f)
                    bend = Vector3.Angle(dPrev, dCur);
            }
            accLat += lp.x;
            _sb.AppendLine(string.Format("{0,3} | {1,-10} | {2,8:F3} | ({3,6:F2},{4,6:F2},{5,6:F2}) | {6,8:F2}° | {7,8:F2}",
                i, bones[i].name, segLen, lp.x, lp.y, lp.z, bend, accLat));
            prevLocal = lp;
        }

        // 总长
        float total = 0f;
        for (int i = 1; i < bones.Length; i++)
            total += Vector3.Distance(M.InverseTransformPoint(bones[i].position),
                                      M.InverseTransformPoint(bones[i - 1].position));
        _sb.AppendLine("链总长 = " + total.ToString("F2") + " m（" + bones.Length + " 节）");
        _sb.AppendLine("首节 y=" + M.InverseTransformPoint(bones[0].position).y.ToString("F2")
            + "  尾节 y=" + M.InverseTransformPoint(bones[bones.Length - 1].position).y.ToString("F2"));

        Debug.Log(_sb.ToString());
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_shape.txt", _sb.ToString()); } catch { }
        enabled = false;
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_shape");
g.AddComponent<dg_shape>();
return "DG_SHAPE_STARTED";
