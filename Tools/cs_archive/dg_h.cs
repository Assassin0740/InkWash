using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_h：量墨龙的**高度真值**：transform.y / _modelRoot.localPosition.y /
//       _modelRootBaseLocalPos.y / _currentLift / 尾尖世界 Y。
//       用来验证"Visual 自带 3.6 m + hoverHeight 3.6 m = 7.2 m"的叠加假设。
public class dg_h : MonoBehaviour
{
    Component _dragon;
    int _step; float _t;
    readonly StringBuilder _sb = new StringBuilder();

    void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("InkWash.Enemies.EnemyDragon");
            if (t == null) continue;
            foreach (var c in FindObjectsOfType(t)) { _dragon = c as Component; break; }
            if (_dragon != null) break;
        }
        if (_dragon == null) { Debug.Log("dg_h ✗ 没找到 EnemyDragon"); enabled = false; return; }
        _sb.AppendLine("目标：" + _dragon.gameObject.name);
        _sb.AppendLine("t(s) | tf.y | base.y | root.lp.y | curLift | 首节世界Y | 尾节世界Y | 前向");
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < 1.0f) return;
        if (_t > 9.0f) { Flush(); enabled = false; return; }
        if (_step++ % 15 != 0) return;

        var ty = _dragon.GetType();
        var fRootF = ty.GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);
        var fBase = ty.GetField("_modelRootBaseLocalPos", BindingFlags.NonPublic | BindingFlags.Instance);
        var fLift = ty.GetField("_currentLift", BindingFlags.NonPublic | BindingFlags.Instance);
        var fSpine = ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        var fBody = ty.GetField("bodyLift", BindingFlags.Public | BindingFlags.Instance);

        var root = fRootF.GetValue(_dragon) as Transform;
        Vector3 b = (Vector3)fBase.GetValue(_dragon);
        float lift = fLift != null ? (float)fLift.GetValue(_dragon) : -1f;
        float body = fBody != null ? (float)fBody.GetValue(_dragon) : -1f;

        float y0 = 0f, yN = 0f;
        var raw = fSpine.GetValue(_dragon) as System.Collections.IList;
        if (raw != null && raw.Count > 0)
        {
            y0 = (raw[0] as Transform).position.y;
            yN = (raw[raw.Count - 1] as Transform).position.y;
        }

        _sb.AppendLine(string.Format("{0,5:F2} | {1,6:F2} | {2,6:F2} | {3,8:F2} | {4,7:F2} | {5,8:F2} | {6,8:F2} | {7}",
            _t, _dragon.transform.position.y, b.y,
            root != null ? root.localPosition.y : -1f,
            lift, y0, yN,
            _dragon.transform.forward.ToString("F2")));
        if (_step < 3) _sb.AppendLine("   (bodyLift=" + body + "  rootName=" + (root != null ? root.name : "-") + ")");
    }

    void Flush()
    {
        Debug.Log(_sb.ToString());
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_h.txt", _sb.ToString()); } catch { }
    }
}

var g = new GameObject("dg_h");
g.AddComponent<dg_h>();
return "DG_H_STARTED";
