using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_diag：量墨龙脊柱的**几何真值**，回答三个问题：
//   1. 每节的 localPosition 间距是多少（判断"关节分离"是缩放还是旋转造成）
//   2. 哪个 local 轴真的能让身子弯（沿 X/Y/Z 分别加 20°，量尾尖位移）
//   3. 各节的世界朝向差多少（判断"只有头在动"）
public class dg_diag : MonoBehaviour
{
    Component _dragon;
    int _step;
    float _t;
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
        if (_dragon == null) { Debug.Log("dg_diag ✗ 场上没有 EnemyDragon"); enabled = false; return; }
        _sb.AppendLine("[dg_diag] 找到 " + _dragon.gameObject.name);
    }

    List<Transform> Spine()
    {
        var list = new List<Transform>();
        var f = _dragon.GetType().GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        var raw = f.GetValue(_dragon) as System.Collections.IEnumerable;
        if (raw == null) return list;
        foreach (var o in raw) { var tr = o as Transform; if (tr != null) list.Add(tr); }
        return list;
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_step == 0 && _t > 1.0f)
        {
            _step = 1;
            var sp = Spine();
            _sb.AppendLine("脊骨节数 = " + sp.Count);
            if (sp.Count < 3) { Flush(); return; }

            // ── 问题 1：各节间距（localPosition 模长）与父级缩放到尾尖的累积 ──
            _sb.AppendLine("── 每节 localPosition 与缩放 ──");
            for (int i = 0; i < sp.Count; i++)
            {
                var p = sp[i].localPosition;
                var s = sp[i].localScale;
                _sb.AppendLine(string.Format("  [{0,2}] {1,-12} lp=({2:F3},{3:F3},{4:F3}) |lp|={5:F3} ls=({6:F2},{7:F2},{8:F2})",
                    i, sp[i].name, p.x, p.y, p.z, p.magnitude, s.x, s.y, s.z));
            }

            // ── 问题 2：哪个轴能弯 ──
            var last = sp[sp.Count - 1];
            Vector3 tipBase = last.position;
            var baseRot = new Quaternion[sp.Count];
            for (int i = 0; i < sp.Count; i++) baseRot[i] = sp[i].localRotation;

            _sb.AppendLine("── 各轴 +20° 对尾尖的位移（世界米）──");
            for (int axis = 0; axis < 3; axis++)
            {
                for (int i = 0; i < sp.Count; i++)
                {
                    Vector3 e = Vector3.zero; e[axis] = 20f;
                    sp[i].localRotation = baseRot[i] * Quaternion.Euler(e);
                }
                Physics.SyncTransforms();
                float d = Vector3.Distance(tipBase, last.position);
                _sb.AppendLine(string.Format("  轴{0} 每节+20° ⇒ 尾尖位移 {1:F3} m", "XYZ"[axis], d));
                for (int i = 0; i < sp.Count; i++) sp[i].localRotation = baseRot[i];
                Physics.SyncTransforms();
            }

            // ── 问题 3：世界朝向沿链的差 ──
            _sb.AppendLine("── 相邻节的 world 朝向夹角 ──");
            for (int i = 1; i < sp.Count; i++)
                _sb.AppendLine(string.Format("  [{0,2}]→[{1,2}] 夹角 {2:F2}°", i - 1, i,
                    Quaternion.Angle(sp[i - 1].rotation, sp[i].rotation)));

            // ── 问题 4：父级链的缩放累积 ──
            Vector3 acc = Vector3.one;
            var root = sp[0].parent;
            var chain = new List<Transform>();
            var c = sp[0];
            while (c != null) { chain.Add(c); c = c.parent; }
            chain.Reverse();
            _sb.Append("  从根到首节的缩放累积：");
            foreach (var tr in chain) { acc.Scale(tr.localScale); _sb.Append(tr.name + "×"); }
            _sb.AppendLine(string.Format(" = ({0:F3},{1:F3},{2:F3})", acc.x, acc.y, acc.z));

            Flush();
        }
        if (_step == 1 && _t > 3.0f) enabled = false;
    }

    void Flush()
    {
        Debug.Log(_sb.ToString());
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_diag.txt", _sb.ToString()); } catch { }
    }
}

var g = new GameObject("dg_diag");
g.AddComponent<dg_diag>();
return "DG_DIAG_STARTED";
