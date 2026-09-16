using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_chain：把 `_spine` 这 24 个节点**摊开看**。
//
// 疑点：ResolveSpine 每层只取「第一个非 SkinnedMeshRenderer 子节点」，
//       对带四肢/翅的龙骨架很可能走岔 —— 采到的未必是脊柱。
//       而且 dg_axis 量出「基准逐节弯角 20.84°、折线 8.27 m 只跨 4.18 m」，
//       说明这条链是**弯的**（甚至可能在打圈），与"骨架原生笔直"的旧结论冲突。
// 这里打印：节点名 / 龙的局部坐标 / 段长 / 段方向 / 逐节弯角 / 每节点的子节点名，
//       再画一张 XZ 俯视 ASCII 图，让形状自己说话。
public class dg_chain : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine;
    readonly StringBuilder _sb = new StringBuilder();
    bool _done;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_chain: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_chain_Dragon";
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
        if (_spine == null || _spine.Count == 0) { Debug.LogError("dg_chain: spine 空"); enabled = false; return; }
        Build();
        enabled = false;
    }

    void Build()
    {
        int n = _spine.Count;
        var M = _dragon.transform;

        _sb.AppendLine("骨链摊开  节点数 = " + n);
        var smrs = _dragon.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _sb.AppendLine("SkinnedMeshRenderer = " + smrs.Length);
        foreach (var smr in smrs)
            _sb.AppendLine("  rootBone = " + (smr.rootBone != null ? smr.rootBone.name : "<null>")
                           + "   bones=" + smr.bones.Length + "   顶点=" + smr.sharedMesh.vertexCount);
        _sb.AppendLine();

        var p = new Vector3[n];
        for (int i = 0; i < n; i++) p[i] = M.InverseTransformPoint((_spine[i] as Transform).position);

        float total = 0f;
        _sb.AppendLine(" i  名字                     龙的局部坐标              段长    段方向                逐节弯角   子节点");
        for (int i = 0; i < n; i++)
        {
            var t = _spine[i] as Transform;
            string seg = "";
            float segLen = 0f; string dirS = "";
            if (i + 1 < n)
            {
                Vector3 d = p[i + 1] - p[i]; segLen = d.magnitude; total += segLen;
                dirS = segLen > 1e-6f ? d.normalized.ToString("F2") : "-";
            }
            string bend = "";
            if (i > 0 && i + 1 < n)
            {
                Vector3 d0 = p[i] - p[i - 1], d1 = p[i + 1] - p[i];
                if (d0.sqrMagnitude > 1e-9f && d1.sqrMagnitude > 1e-9f)
                    bend = Vector3.Angle(d0, d1).ToString("F1") + "°";
            }
            var kids = new StringBuilder();
            for (int k = 0; k < t.childCount; k++)
            {
                var c = t.GetChild(k);
                if (k > 0) kids.Append(",");
                kids.Append(c.name);
                if (c.GetComponent<SkinnedMeshRenderer>() != null) kids.Append("(M)");
            }
            _sb.AppendLine(string.Format("{0,3}  {1,-24} {2,-26} {3,6:F2}  {4,-22} {5,8}   {6}",
                i, t.name, p[i].ToString("F3"), segLen, dirS, bend, kids.ToString()));
        }
        _sb.AppendLine();
        _sb.AppendLine("折线总长 = " + total.ToString("F3") + " m   首尾直线 = " + (p[n - 1] - p[0]).magnitude.ToString("F3") + " m");
        _sb.AppendLine();

        _sb.AppendLine("── XZ 俯视轮廓（列=X，行=Z；# = 节点）──");
        Plot(p, 0, 2);
        _sb.AppendLine();
        _sb.AppendLine("── XY 侧视轮廓（列=X，行=Y）──");
        Plot(p, 0, 1);

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_chain.txt", s); } catch { }
    }

    void Plot(Vector3[] p, int ai, int bi, int cols = 59, int rows = 21)
    {
        float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
        foreach (var v in p)
        {
            minA = Mathf.Min(minA, v[ai]); maxA = Mathf.Max(maxA, v[ai]);
            minB = Mathf.Min(minB, v[bi]); maxB = Mathf.Max(maxB, v[bi]);
        }
        float pa = (maxA - minA) * 0.08f + 0.05f, pb = (maxB - minB) * 0.08f + 0.05f;
        minA -= pa; maxA += pa; minB -= pb; maxB += pb;

        var grid = new char[rows, cols];
        for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) grid[r, c] = ' ';
        for (int i = 0; i < p.Length; i++)
        {
            int c = Mathf.Clamp(Mathf.RoundToInt((p[i][ai] - minA) / (maxA - minA) * (cols - 1)), 0, cols - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt((p[i][bi] - minB) / (maxB - minB) * (rows - 1)), 0, rows - 1);
            grid[r, c] = i == 0 ? 'H' : (i == p.Length - 1 ? 'T' : '#');
        }
        _sb.AppendLine("  " + minB.ToString("F2") + " → " + maxB.ToString("F2"));
        for (int r = rows - 1; r >= 0; r--)
        {
            var line = new StringBuilder("| ");
            for (int c = 0; c < cols; c++) line.Append(grid[r, c]);
            _sb.AppendLine(line.ToString());
        }
        _sb.AppendLine("  └" + new string('-', cols) + "   " + minA.ToString("F2") + " → " + maxA.ToString("F2"));
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_chain");
g.AddComponent<dg_chain>();
return "DG_CHAIN_STARTED";
