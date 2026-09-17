using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// d9：查 WaveSpawner 的波次表 —— 龙在第几波，以及推进方式（便于自动化直达龙）
public class d9_probe : MonoBehaviour
{
    float _t;
    bool _done;

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        if (_t < 1.0f || _done) return;
        _done = true;
        var sb = new StringBuilder();
        sb.AppendLine("========== d9 波次表 ==========");

        Type wsT = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            wsT = asm.GetType("InkWash.Enemies.WaveSpawner");
            if (wsT != null) break;
        }
        if (wsT == null) { sb.AppendLine("✗ 找不到 WaveSpawner"); Debug.Log(sb.ToString()); return; }

        var insts = UnityEngine.Object.FindObjectsOfType(wsT);
        sb.AppendLine("WaveSpawner 数量 = " + insts.Length);
        foreach (var o in insts)
        {
            var comp = o as Component;
            sb.AppendLine("── " + comp.gameObject.name + " ──");
            foreach (var f in wsT.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object v = null;
                try { v = f.GetValue(o); } catch { }
                if (v == null) { sb.AppendLine("  " + f.Name + " = <null>"); continue; }

                var arr = v as System.Collections.IEnumerable;
                if (arr != null && !(v is string))
                {
                    int i = 0;
                    foreach (var item in arr)
                    {
                        if (item == null) { sb.AppendLine("  " + f.Name + "[" + i + "] = <null>"); i++; continue; }
                        var it = item.GetType();
                        var pf = it.GetField("prefab");
                        var cf = it.GetField("count");
                        var df = it.GetField("delay");
                        string extra = "";
                        if (pf != null) { var pv = pf.GetValue(item) as UnityEngine.Object; extra += " prefab=" + (pv != null ? pv.name : "<null>"); }
                        if (cf != null) extra += " count=" + cf.GetValue(item);
                        if (df != null) extra += " delay=" + df.GetValue(item);
                        sb.AppendLine("  " + f.Name + "[" + i + "] " + it.Name + extra);
                        i++;
                    }
                    if (i == 0) sb.AppendLine("  " + f.Name + " = (空)");
                }
                else sb.AppendLine("  " + f.Name + " = " + v);
            }
        }
        Debug.Log(sb.ToString());
    }
}

var g = new GameObject("D9_Probe");
g.AddComponent<d9_probe>();
return "D9_STARTED";
