using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_anc：把 Muzzle（吻部）和尾尖的**祖先链**打出来，
//         看它们与 `_spine` 的关系 —— 头/尾到底挂在 _spine 的哪一节上，
//         还是压根不在 _spine 上。
public class dg_anc : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine;
    readonly StringBuilder _sb = new StringBuilder();
    bool _done;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_anc: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_anc_Dragon";
        _dragon.transform.position = Vector3.zero;
        _dragon.transform.rotation = Quaternion.identity;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        _comp = _dragon.GetComponentInChildren(_ty, true);
        var mb = _comp as MonoBehaviour; if (mb != null) mb.enabled = false;

        _spine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(_comp) as System.Collections.IList;
    }

    void Update()
    {
        if (_done) return;
        _done = true;
        if (_spine == null || _spine.Count == 0) { Debug.LogError("dg_anc: spine 空"); enabled = false; return; }
        Build();
        enabled = false;
    }

    int SpineIndexOf(Transform t)
    {
        for (int i = 0; i < _spine.Count; i++) if ((_spine[i] as Transform) == t) return i;
        return -1;
    }

    void Ancestors(string label, Transform t)
    {
        var M = _dragon.transform;
        _sb.AppendLine("── " + label + " 的祖先链（自下而上）──");
        int d = 0; var cur = t;
        while (cur != null && d < 60)
        {
            int si = SpineIndexOf(cur);
            _sb.AppendLine(string.Format("  d{0,2}  {1,-14} 局部 {2,-26} {3}",
                d, cur.name, M.InverseTransformPoint(cur.position).ToString("F2"),
                si >= 0 ? ("★ _spine[" + si + "]") : ""));
            if (si == 0) { _sb.AppendLine("       ↑ 到达 _spine[0]（链根），停止"); break; }
            if (cur.parent == M) { _sb.AppendLine("       ↑ 到达模型容器，停止"); break; }
            cur = cur.parent; d++;
        }
        _sb.AppendLine();
    }

    Transform FindDeep(string name)
    {
        foreach (var t in _dragon.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    void Build()
    {
        var M = _dragon.transform;
        var all = _dragon.GetComponentsInChildren<Transform>(true);

        _sb.AppendLine("dragon 头部/尾部 与 _spine 的关系");
        _sb.AppendLine("模型容器 = " + M.name + "  子节点数 = " + M.childCount);
        for (int i = 0; i < M.childCount; i++) _sb.AppendLine("   子 " + M.GetChild(i).name);
        _sb.AppendLine();

        // 端到端最远两点
        int ia = 0, ib = 0; float maxD = 0f;
        for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
            {
                float dd = (all[i].position - all[j].position).sqrMagnitude;
                if (dd > maxD) { maxD = dd; ia = i; ib = j; }
            }
        _sb.AppendLine(string.Format("端到端最远两点：{0} {1}  ←→  {2} {3}   距离 {4:F3} m",
            all[ia].name, M.InverseTransformPoint(all[ia].position).ToString("F2"),
            all[ib].name, M.InverseTransformPoint(all[ib].position).ToString("F2"), Mathf.Sqrt(maxD)));
        _sb.AppendLine();

        var muzzle = FindDeep("Muzzle");
        if (muzzle != null) Ancestors("Muzzle（吻部 = 头）", muzzle);
        else _sb.AppendLine("找不到 Muzzle\n");

        Ancestors("端到端远端 A: " + all[ia].name, all[ia]);
        Ancestors("端到端远端 B: " + all[ib].name, all[ib]);

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_anc.txt", s); } catch { }
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_anc");
g.AddComponent<dg_anc>();
return "DG_ANC_STARTED";
