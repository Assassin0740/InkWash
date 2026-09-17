using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_tree：`_spine` 到底覆盖了龙的哪些部分？尾巴在不在里面？
//
// 动机：ResolveSpine 每层只取「第一个非 SMR 子节点」。_spine[0]=drgon_03 的子树有 271 个节点
//       （全骨架 273），说明它是**根**而不是尾尖。若尾/腿是从根分出去的别的分支，
//       那么 `_spine` 只覆盖了身体的一半 ——「像蛇一样蜿蜒」根本无从谈起。
// 做法：把 _spine[0] 的每个直接子节点各自「一路取第一个子节点」走到底，
//       报告每条链的跳数、末端名字与末端局部坐标。谁伸得最远，谁就是身体/尾。
public class dg_tree : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine;
    readonly StringBuilder _sb = new StringBuilder();
    bool _done;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_tree: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_tree_Dragon";
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
        if (_spine == null || _spine.Count == 0) { Debug.LogError("dg_tree: spine 空"); enabled = false; return; }
        Build();
        enabled = false;
    }

    int CountDesc(Transform t)
    {
        int c = 0;
        for (int i = 0; i < t.childCount; i++) c += 1 + CountDesc(t.GetChild(i));
        return c;
    }

    /// <summary>一路取第一个非 SMR 子节点走到底。</summary>
    void FirstChildChain(Transform start, out int hops, out Transform end)
    {
        hops = 0; end = start; var cur = start; int guard = 0;
        while (guard++ < 500)
        {
            Transform next = null;
            for (int i = 0; i < cur.childCount; i++)
            {
                var c = cur.GetChild(i);
                if (c.GetComponent<SkinnedMeshRenderer>() != null) continue;
                next = c; break;
            }
            if (next == null) break;
            cur = next; hops++; end = cur;
        }
    }

    void Build()
    {
        var M = _dragon.transform;
        var root = _spine[0] as Transform;
        int n = _spine.Count;

        _sb.AppendLine("dragon 骨链覆盖范围探查");
        _sb.AppendLine("骨架总节点(含SMR) = " + CountDesc(M) + "   _spine.Count = " + n);
        _sb.AppendLine();

        _sb.AppendLine("── _spine 链 ──");
        var names = new StringBuilder();
        for (int i = 0; i < n; i++) { if (i > 0) names.Append(" → "); names.Append((_spine[i] as Transform).name); }
        _sb.AppendLine("  " + names.ToString());
        _sb.AppendLine("  首 " + M.InverseTransformPoint((_spine[0] as Transform).position).ToString("F2")
                       + "   末 " + M.InverseTransformPoint((_spine[n - 1] as Transform).position).ToString("F2"));
        _sb.AppendLine();

        _sb.AppendLine("── 从 _spine[0] (" + root.name + ") 各分支「一路第一子节点」走到底 ──");
        _sb.AppendLine("子节点名            子树节点数   跳数   链末端名字         末端局部坐标            末端距根");
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            int hops; Transform end;
            FirstChildChain(c, out hops, out end);
            int sub = CountDesc(c);
            Vector3 ep = M.InverseTransformPoint(end.position);
            Vector3 rp = M.InverseTransformPoint(root.position);
            _sb.AppendLine(string.Format("{0,-20} {1,8} {2,7}   {3,-18} {4,-24} {5,7:F2} m",
                c.name, sub, hops, end.name, ep.ToString("F2"), Vector3.Distance(ep, rp)));
        }
        _sb.AppendLine();

        _sb.AppendLine("── 全骨架：从根出发最长的一条链 ──");
        Transform best = null; int bestHops = -1;
        for (int i = 0; i < root.childCount; i++)
        {
            int hops; Transform end;
            FirstChildChain(root.GetChild(i), out hops, out end);
            if (hops > bestHops) { bestHops = hops; best = end; }
        }
        if (best != null)
        {
            _sb.AppendLine("  最长链跳数 = " + bestHops + "  末端 = " + best.name
                           + "  局部 " + M.InverseTransformPoint(best.position).ToString("F2"));
        }

        // 端到端：全骨架里相距最远的两个节点（近似主轴）
        var all = M.GetComponentsInChildren<Transform>(true);
        _sb.AppendLine("  全骨架节点数(Transform) = " + all.Length);
        int ia = 0, ib = 0; float maxD = 0f;
        for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
            {
                float d = (all[i].position - all[j].position).sqrMagnitude;
                if (d > maxD) { maxD = d; ia = i; ib = j; }
            }
        Vector3 A = M.InverseTransformPoint(all[ia].position), B = M.InverseTransformPoint(all[ib].position);
        _sb.AppendLine("  相距最远的两点 " + A.ToString("F2") + " ←→ " + B.ToString("F2")
                       + "  距离 " + Mathf.Sqrt(maxD).ToString("F3") + " m");
        _sb.AppendLine("      = " + all[ia].name + "  ←→  " + all[ib].name);

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_tree.txt", s); } catch { }
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_tree");
g.AddComponent<dg_tree>();
return "DG_TREE_STARTED";
