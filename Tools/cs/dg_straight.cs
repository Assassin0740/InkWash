using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_straight：两件事
//   ① 判定 `_spine` 链条的**首尾哪一端是头**（按子树规模 + 端点局部坐标）
//   ② 验证「拉直」算法：逐节把段方向对齐到指定朝向，看能否得到一条真正的直线
//
// 背景：dg_chain 已证骨架原生姿态是一条大 C/S 曲线（折线 8.27 m 只跨 4.18 m，
//       逐节弯角 7.9~65.6°）。若把波叠在这条弯基准上，永远摆不出"一条蛇"。
//       「拉直」= 覆盖基准姿态，重建一条直线的 `_baseRot`。
public class dg_straight : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine;
    readonly StringBuilder _sb = new StringBuilder();
    Quaternion[] _orig;

    static readonly string[] FNames = { "+Z (模型前方)", "-Z", "首尾连线 (弦)" };
    bool _done;

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_straight: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_straight_Dragon";
        _dragon.transform.position = Vector3.zero;
        _dragon.transform.rotation = Quaternion.identity;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        _comp = _dragon.GetComponentInChildren(_ty, true);
        var mb = _comp as MonoBehaviour; if (mb != null) mb.enabled = false;

        _spine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(_comp) as System.Collections.IList;

        if (_spine == null || _spine.Count == 0) { Debug.LogError("dg_straight: spine 空"); enabled = false; return; }
        _orig = new Quaternion[_spine.Count];
        for (int i = 0; i < _spine.Count; i++) _orig[i] = (_spine[i] as Transform).localRotation;
    }

    void Update()
    {
        if (_done) return;
        _done = true;
        Build();
        enabled = false;
    }

    int CountDesc(Transform t, int cap)
    {
        int c = 0;
        for (int i = 0; i < t.childCount; i++)
        {
            c += 1 + CountDesc(t.GetChild(i), cap);
            if (c > cap) return c;
        }
        return c;
    }

    Vector3[] LocalPos()
    {
        int n = _spine.Count; var p = new Vector3[n];
        var M = _dragon.transform;
        for (int i = 0; i < n; i++) p[i] = M.InverseTransformPoint((_spine[i] as Transform).position);
        return p;
    }

    float MeanBend(Vector3[] p)
    {
        float sum = 0f; int c = 0;
        for (int i = 1; i + 1 < p.Length; i++)
        {
            Vector3 d0 = p[i] - p[i - 1], d1 = p[i + 1] - p[i];
            if (d0.sqrMagnitude < 1e-9f || d1.sqrMagnitude < 1e-9f) continue;
            sum += Vector3.Angle(d0, d1); c++;
        }
        return c > 0 ? sum / c : 0f;
    }

    void Restore()
    {
        for (int i = 0; i < _spine.Count; i++) (_spine[i] as Transform).localRotation = _orig[i];
    }

    /// <summary>逐节把「本节点→下一节点」的段方向对齐到 F（龙局部空间）。</summary>
    void Straighten(Vector3 localF)
    {
        var M = _dragon.transform;
        Vector3 worldF = M.TransformDirection(localF.normalized);
        for (int i = 0; i + 1 < _spine.Count; i++)
        {
            var a = _spine[i] as Transform;
            var b = _spine[i + 1] as Transform;
            Vector3 d = b.position - a.position;
            if (d.sqrMagnitude < 1e-10f) continue;
            Quaternion q = Quaternion.FromToRotation(d.normalized, worldF);
            a.rotation = q * a.rotation;
        }
    }

    void Build()
    {
        int n = _spine.Count;
        var M = _dragon.transform;

        _sb.AppendLine("龙骨架 端点身份 & 拉直实验");
        _sb.AppendLine("模型 localScale = " + M.localScale.ToString("F4") + "  lossyScale = " + M.lossyScale.ToString("F4"));
        _sb.AppendLine();

        var p0 = LocalPos();
        _sb.AppendLine("── ① 两端身份 ──");
        for (int k = 0; k < 2; k++)
        {
            int idx = k == 0 ? 0 : n - 1;
            var t = _spine[idx] as Transform;
            _sb.AppendLine(string.Format("  _spine[{0}] = {1,-12} 局部 {2,-26} 直接子节点 {3,3}   子树总节点 {4}",
                idx, t.name, p0[idx].ToString("F3"), t.childCount, CountDesc(t, 999)));
        }
        _sb.AppendLine();
        _sb.AppendLine("  （头的角/颌/牙/须会让子树显著更大；尾尖基本是叶子）");
        _sb.AppendLine("  模型前方 = +Z（代码注释：朝向来自 drgon_00，模型在 +Z）");
        _sb.AppendLine(string.Format("  终点相对起点：{0}   在 +Z 上的投影 = {1:F3} m",
            (p0[n - 1] - p0[0]).ToString("F3"), p0[n - 1].z - p0[0].z));
        _sb.AppendLine();

        _sb.AppendLine("── ② 拉直实验 ──");
        _sb.AppendLine("对齐朝向            平均逐节弯角   折线总长    首尾直线   起点局部                终点局部");
        // 原始
        {
            float arc = 0f; for (int i = 0; i + 1 < n; i++) arc += (p0[i + 1] - p0[i]).magnitude;
            _sb.AppendLine(string.Format("{0,-18} {1,10:F2}° {2,9:F3} m {3,9:F3} m  {4,-22} {5}",
                "原始（未处理）", MeanBend(p0), arc, (p0[n - 1] - p0[0]).magnitude,
                p0[0].ToString("F2"), p0[n - 1].ToString("F2")));
        }
        Vector3[] fs = { Vector3.forward, Vector3.back, (p0[n - 1] - p0[0]).normalized };
        for (int k = 0; k < 3; k++)
        {
            Restore();
            Straighten(fs[k]);
            var p = LocalPos();
            float arc = 0f; for (int i = 0; i + 1 < n; i++) arc += (p[i + 1] - p[i]).magnitude;
            _sb.AppendLine(string.Format("{0,-18} {1,10:F2}° {2,9:F3} m {3,9:F3} m  {4,-22} {5}",
                FNames[k], MeanBend(p), arc, (p[n - 1] - p[0]).magnitude,
                p[0].ToString("F2"), p[n - 1].ToString("F2")));
        }
        Restore();
        _sb.AppendLine();
        _sb.AppendLine("判读：平均逐节弯角 ≈ 0° 且 折线总长 ≈ 首尾直线 ⇒ 拉直成功（真的是一条直线）");

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_straight.txt", s); } catch { }
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_straight");
g.AddComponent<dg_straight>();
return "DG_STRAIGHT_STARTED";
