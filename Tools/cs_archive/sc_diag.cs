using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// sc_diag：打印演示场条目清单 + 逐个试播并报告"舞台上有什么"
public class sc_diag : MonoBehaviour
{
    Component _sc;
    int _i; float _t;

    void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("InkWash.DebugTools.ActionShowcase");
            if (t == null) continue;
            foreach (var c in FindObjectsOfType(t)) { _sc = c as Component; break; }
            if (_sc != null) break;
        }
        if (_sc == null) { Debug.Log("sc_diag ✗ 没有 ActionShowcase"); enabled = false; return; }

        var ty = _sc.GetType();
        var pCount = ty.GetProperty("ItemCount");
        int n = pCount != null ? (int)pCount.GetValue(_sc, null) : 0;
        var mLabel = ty.GetMethod("ItemLabel");
        var sb = new StringBuilder();
        sb.AppendLine("[sc_diag] 条目总数 = " + n);
        for (int i = 0; i < n; i++)
            sb.AppendLine(string.Format("[sc_diag] {0,2}. {1}", i, mLabel.Invoke(_sc, new object[] { i })));
        Debug.Log(sb.ToString());

        // 逐条试播，报舞台上出现了几个对象
        Invoke("Step", 0.5f);
    }

    void Step()
    {
        var ty = _sc.GetType();
        var mPlay = ty.GetMethod("PlayForCapture");
        if (_i < 23)
        {
            mPlay.Invoke(_sc, new object[] { _i });
            var actors = 0; string names = "";
            foreach (var go in FindObjectsOfType<GameObject>())
                if (go.name.StartsWith("Showcase_Actor")) { actors++; names += go.name + " "; }
            var smrs = 0;
            foreach (var s in FindObjectsOfType<SkinnedMeshRenderer>()) smrs++;
            Debug.Log(string.Format("[sc_diag] 条目{0,2} 演员={1} [{2}] 场景SMR={3}", _i, actors, names.Trim(), smrs));
            _i++;
            Invoke("Step", 0.6f);
        }
        else Debug.Log("[sc_diag] 完成");
    }
}

var g = new GameObject("sc_diag");
g.AddComponent<sc_diag>();
return "SC_DIAG_STARTED";
