using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_skel：把**整个骨架**画成 ASCII 图（俯视 XZ + 侧视 XY），
//          `_spine` 的 24 个节点用 0-9a-n 标号，Muzzle 标 M，其余骨标 ·。
// 目的：一眼看清 _spine 到底是身体的哪一段 —— 是脊柱？尾巴？还是翅膀/腿。
public class dg_skel : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine;
    readonly StringBuilder _sb = new StringBuilder();
    bool _done;

    const string IDX = "0123456789abcdefghijklmn";

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_skel: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_skel_Dragon";
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
        Build();
        enabled = false;
    }

    void Build()
    {
        var M = _dragon.transform;
        var all = M.GetComponentsInChildren<Transform>(true);
        int n = all.Length;

        var pos = new Vector3[n];
        var mark = new char[n];
        for (int i = 0; i < n; i++)
        {
            pos[i] = M.InverseTransformPoint(all[i].position);
            mark[i] = '.';
            string nm = all[i].name;
            if (nm == "Muzzle") mark[i] = 'M';
        }
        // 该节点是否属于 _spine
        string[] spineName = new string[24];
        for (int i = 0; i < _spine.Count && i < 24; i++)
        {
            var t = _spine[i] as Transform;
            spineName[i] = t.name;
            for (int k = 0; k < n; k++) if (all[k] == t) mark[k] = IDX[i];
        }

        _sb.AppendLine("dragon 全骨架 ASCII 图   总节点 " + n + "   _spine " + _spine.Count);
        _sb.AppendLine("标记：0-9a-n = _spine[0..23]，M = Muzzle（嘴/头），· = 其他骨");
        _sb.AppendLine();

        // 找尾尖（离 Muzzle 最远的点）
        Vector3 muzzleV = Vector3.zero; bool hasM = false;
        for (int i = 0; i < n; i++) if (mark[i] == 'M') { muzzleV = pos[i]; hasM = true; }
        int far = 0; float fd = -1f;
        if (hasM)
            for (int i = 0; i < n; i++)
            {
                float d = (pos[i] - muzzleV).sqrMagnitude;
                if (d > fd) { fd = d; far = i; }
            }
        if (hasM)
            _sb.AppendLine(string.Format("Muzzle 局部 {0}    离它最远的骨 {1} 局部 {2}  距离 {3:F3} m",
                muzzleV.ToString("F2"), all[far].name, pos[far].ToString("F2"), Mathf.Sqrt(fd)));
        _sb.AppendLine();

        Plot(pos, mark, all, 0, 2, "俯视 XZ（列=X 行=Z，行自下而上是 +Z 向上）", 79, 27);
        _sb.AppendLine();
        Plot(pos, mark, all, 2, 1, "侧视 ZY（列=Z 行=Y）", 79, 21);

        _sb.AppendLine();
        _sb.AppendLine("── _spine 各节到 Muzzle / 到尾尖 的距离 ──");
        _sb.AppendLine(" i  名字            到Muzzle   到尾尖");
        for (int i = 0; i < _spine.Count; i++)
        {
            var t = _spine[i] as Transform;
            Vector3 p = M.InverseTransformPoint(t.position);
            _sb.AppendLine(string.Format("{0,3}  {1,-16} {2,8:F2} {3,8:F2}",
                i, t.name,
                hasM ? Vector3.Distance(p, muzzleV) : -1f,
                hasM ? Vector3.Distance(p, pos[far]) : -1f));
        }

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_skel.txt", s); } catch { }
    }

    void Plot(Vector3[] pos, char[] mark, Transform[] all, int ai, int bi, string title, int cols, int rows)
    {
        _sb.AppendLine("── " + title + " ──");
        float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
        foreach (var v in pos)
        {
            minA = Mathf.Min(minA, v[ai]); maxA = Mathf.Max(maxA, v[ai]);
            minB = Mathf.Min(minB, v[bi]); maxB = Mathf.Max(maxB, v[bi]);
        }
        float pa = (maxA - minA) * 0.04f + 0.02f, pb = (maxB - minB) * 0.04f + 0.02f;
        minA -= pa; maxA += pa; minB -= pb; maxB += pb;

        var grid = new char[rows, cols];
        var rank = new int[rows, cols];
        for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) { grid[r, c] = ' '; rank[r, c] = -1; }
        for (int i = 0; i < pos.Length; i++)
        {
            int c = Mathf.Clamp(Mathf.RoundToInt((pos[i][ai] - minA) / (maxA - minA) * (cols - 1)), 0, cols - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt((pos[i][bi] - minB) / (maxB - minB) * (rows - 1)), 0, rows - 1);
            int pri = mark[i] == '.' ? 0 : (mark[i] == 'M' ? 3 : 2);
            if (pri >= rank[r, c]) { rank[r, c] = pri; grid[r, c] = mark[i]; }
        }
        _sb.AppendLine(string.Format("  {0} 轴 {1:F2} → {2:F2}", bi == 2 ? "Z" : "Y", minB, maxB));
        for (int r = rows - 1; r >= 0; r--)
        {
            var line = new StringBuilder("|");
            for (int c = 0; c < cols; c++) line.Append(grid[r, c]);
            _sb.AppendLine(line.ToString());
        }
        _sb.AppendLine(string.Format("  +{0}   {1} 轴 {2:F2} → {3:F2}", new string('-', cols), ai == 0 ? "X" : "Z", minA, maxA));
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_skel");
g.AddComponent<dg_skel>();
return "DG_SKEL_STARTED";
