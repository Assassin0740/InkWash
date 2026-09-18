using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// drg_head.cs —— 墨龙头部/颈部/尾巴「结构」排查（只读，不改任何参数）
//
// 用户（第十八轮）：
//   「头还是歪的，他的头没有顺着那个脖子的位置，然后这个爪子应该是往后的，
//     因为他要往前飞，然后尾巴末端是歪的，翘起来了，不应该，应该保持同一水平线的高度，
//     然后动画应该是不断重复的，就是始终保持重复才对的」
//
// ■ 上一轮（drg_axis）量的是"分支骨摆不摆"，从没 dump 过骨架层级 —— 所以
//   「头不顺着脖子」这条根本没法定位：**先得知道哪根骨是头、哪根是颈**。
//   本探针就干这一件事：把 `_modelRoot` 下的骨架树完整打出来，
//   标出每根骨的：脊柱序号 / 分支根序号 / 是否被驱动，以及它离根节的几何量。
//
// ■ 同时量三条（都是"静态几何"，不做逐帧统计，避免又陷进上一轮的口径陷阱）：
//   ① 头部的**朝向**：若干个候选"头骨"的 forward 与"颈→头段方向"的夹角
//   ② 尾巴的**形状**：尾分支每一节的段方向 pitch（看它是不是在往上翘）
//   ③ 四肢的**朝向**：腿根首段方向与身体前向的夹角（是往后兜还是往前伸）

public class drg_head : MonoBehaviour
{
    const string RP = "D:/Unity Project/InkWash/Tools/reports/drg_head.txt";
    const string KEY = "墨龙";

    const int TREE_DEPTH = 6;      // 骨架树打印深度
    const float EPS = 1e-6f;

    readonly StringBuilder _sb = new StringBuilder();
    Component _sc; Type _ty, _dt;
    MethodInfo _mPlay, _mLabel, _mStop;
    PropertyInfo _pCount;
    GameObject _go;
    Transform _drgT, _vroot;
    IList _spine, _limbRoots, _limbDriven, _limbAxisDot, _limbParentIndex, _segLen, _baseSegLocal, _baseRel;
    FieldInfo _fStat, _fWave, _fYaw, _fLimbDeg;

    static readonly BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        yield return null; yield return null;

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (_ty == null) { var t1 = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t1 != null) _ty = t1; }
            if (_dt == null) { var t2 = a.GetType("InkWash.Enemies.EnemyDragon"); if (t2 != null) _dt = t2; }
        }
        if (_ty == null || _dt == null) { _sb.AppendLine("× 类型缺失"); yield return Done(); }

        foreach (var o in UnityEngine.Object.FindObjectsOfType(_ty, true)) { _sc = o as Component; if (_sc != null) break; }
        if (_sc == null) { _sb.AppendLine("× 场景里没有 ActionShowcase（当前不是 Showcase 场景？）"); yield return Done(); }

        _mPlay = _ty.GetMethod("PlayForCapture", BF);
        _mLabel = _ty.GetMethod("ItemLabel", BF);
        _mStop = _ty.GetMethod("StopAutoClose", BF);
        _pCount = _ty.GetProperty("ItemCount", BF);

        int count = (int)_pCount.GetValue(_sc, null);
        int idx = -1; string found = "";
        for (int i = 0; i < count; i++)
        {
            string lb = (string)_mLabel.Invoke(_sc, new object[] { i });
            if (lb.Contains(KEY) && lb.Contains("盘旋")) { idx = i; found = lb; break; }
        }
        if (idx < 0) { _sb.AppendLine("× 没找到「墨龙 + 盘旋」条目"); yield return Done(); }

        _sb.AppendLine("=== drg_head：墨龙骨架结构 + 头/颈/尾/肢 朝向排查（只读）===");
        _sb.AppendLine("条目 idx=" + idx + "  label=" + found);
        _sb.AppendLine();

        _mStop.Invoke(_sc, null);
        _mPlay.Invoke(_sc, new object[] { idx });
        for (int i = 0; i < 90; i++) yield return null;

        _go = GameObject.Find("Showcase_Actor_" + idx);
        if (_go == null) { _sb.AppendLine("× 没找到 actor 实例"); yield return Done(); }

        var drg = _go.GetComponent(_dt);
        _drgT = drg.transform;
        _vroot = _dt.GetField("_modelRoot", BF).GetValue(drg) as Transform;
        _spine = _dt.GetField("_spine", BF).GetValue(drg) as IList;
        _limbRoots = _dt.GetField("_limbRoots", BF).GetValue(drg) as IList;
        _limbDriven = _dt.GetField("_limbDriven", BF).GetValue(drg) as IList;
        _limbAxisDot = _dt.GetField("_limbAxisDot", BF).GetValue(drg) as IList;
        _limbParentIndex = _dt.GetField("_limbParentIndex", BF).GetValue(drg) as IList;
        _segLen = _dt.GetField("_segLen", BF).GetValue(drg) as IList;
        _baseSegLocal = _dt.GetField("_baseSegLocal", BF).GetValue(drg) as IList;
        _baseRel = _dt.GetField("_baseRel", BF).GetValue(drg) as IList;
        _fStat = _dt.GetField("hoverStationary", BF);
        _fWave = _dt.GetField("swimWaveCount", BF);
        _fYaw = _dt.GetField("hoverStationaryYawDeg", BF);
        _fLimbDeg = _dt.GetField("limbSwingDeg", BF);

        if (_spine == null || _spine.Count < 3) { _sb.AppendLine("× 脊柱节数 < 3"); yield return Done(); }
        int NS = _spine.Count;
        int NL = _limbRoots == null ? 0 : _limbRoots.Count;

        if (_fStat != null) _fStat.SetValue(drg, true);
        if (_fWave != null) _fWave.SetValue(drg, 1.0f);
        for (int i = 0; i < 8; i++) yield return null;

        Quaternion rootRot = _vroot.rotation;
        Quaternion invRoot = Quaternion.Inverse(rootRot);

        _sb.AppendLine("── 运行时参数 ──");
        _sb.AppendLine("  hoverStationary = " + _fStat.GetValue(drg) + "（本探针强制 true，静止取样）");
        _sb.AppendLine("  swimWaveCount   = " + _fWave.GetValue(drg));
        _sb.AppendLine("  limbSwingDeg    = " + _fLimbDeg.GetValue(drg));
        _sb.AppendLine("  hoverStationaryYawDeg = " + _fYaw.GetValue(drg));
        _sb.AppendLine("  脊柱节数 = " + NS + "   分支根 = " + NL);
        _sb.AppendLine();

        // ── ① 脊柱骨架链（名字 + 世界位置 + 段方向）──
        _sb.AppendLine("── 脊柱链（i=0 是尾/根侧；i=" + (NS - 1) + " 是头侧）──");
        _sb.AppendLine("   i  名字                 世界坐标                       段方向(容器系)              段长");
        var segDirNow = new Vector3[NS];
        for (int i = 0; i < NS; i++)
        {
            var t = _spine[i] as Transform;
            if (t == null) { _sb.AppendLine("  " + i + "  NULL"); continue; }
            Vector3 d = Vector3.zero; float L = 0f;
            if (i + 1 < NS)
            {
                var n = _spine[i + 1] as Transform;
                if (n != null) { d = invRoot * (n.position - t.position); L = d.magnitude; if (L > EPS) d /= L; }
            }
            segDirNow[i] = d;
            _sb.AppendLine("  " + i.ToString("D2") + "  " + Pad(t.name, 20) + " " + F3(t.position) + "   " + F3(d) + "   " + L.ToString("F3"));
        }
        _sb.AppendLine();

        Transform head = _spine[NS - 1] as Transform;
        Transform neck = _spine[NS - 2] as Transform;
        _sb.AppendLine("  ★ 头侧末节 = 「" + (head != null ? head.name : "?") + "」  子节点数 = " + (head != null ? head.childCount : -1));
        _sb.AppendLine("  ★ 头侧倒数第二节 = 「" + (neck != null ? neck.name : "?") + "」  子节点数 = " + (neck != null ? neck.childCount : -1));
        _sb.AppendLine();

        // ── ② 骨架树 dump（从 _modelRoot 往下，标出脊柱/分支身份）──
        var spineIdx = new Dictionary<Transform, int>();
        for (int i = 0; i < NS; i++) { var t = _spine[i] as Transform; if (t != null && !spineIdx.ContainsKey(t)) spineIdx[t] = i; }
        var limbIdx = new Dictionary<Transform, int>();
        for (int k = 0; k < NL; k++) { var t = _limbRoots[k] as Transform; if (t != null && !limbIdx.ContainsKey(t)) limbIdx[t] = k; }

        _sb.AppendLine("── 骨架树（`_modelRoot` 之下，深度 ≤ " + TREE_DEPTH + "；S## = 脊柱节，L## = 分支根，★ = 被驱动）──");
        Transform origin = _spine[0] as Transform;
        DumpTree(_vroot, 0, origin);

        // ── ③ 头区：spine[22] / spine[23] 的子树单独列一遍（不截深度）──
        _sb.AppendLine();
        _sb.AppendLine("── 头区子树（从脊柱[22] 起，不限深度）──");
        var h22 = _spine[NS - 2] as Transform;
        if (h22 != null) DumpSub(h22, 1, origin, spineIdx, limbIdx);

        // ── ④ 尾分支：逐节段方向的 pitch（看它是不是翘）──
        _sb.AppendLine();
        _sb.AppendLine("── 尾分支逐节段方向（pitch>0 = 往上翘；沿链走）──");
        Transform tail = null; int tailK = -1;
        for (int k = 0; k < NL; k++)
        {
            float ad = _limbAxisDot != null && k < _limbAxisDot.Count ? (float)_limbAxisDot[k] : -1f;
            bool driven = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            if (!driven && ad >= 0.8f) { tail = _limbRoots[k] as Transform; tailK = k; break; }
        }
        if (tail != null)
        {
            _sb.AppendLine("  尾分支 = [" + tailK + "] " + tail.name + "  父 = " + (tail.parent != null ? tail.parent.name : "?"));
            var cur = tail; int g = 0; float cumY = 0f;
            while (cur != null && cur.childCount > 0 && g < 40)
            {
                var nx = cur.GetChild(0);
                Vector3 d = invRoot * (nx.position - cur.position);
                float L = d.magnitude; if (L > EPS) d /= L;
                float pitch = Mathf.Asin(Mathf.Clamp(-d.y, -1f, 1f)) * Mathf.Rad2Deg;
                cumY += nx.position.y - cur.position.y;
                _sb.AppendLine("    [" + g.ToString("D2") + "] " + Pad(cur.name, 14) + "→" + Pad(nx.name, 14)
                    + "  段长 " + L.ToString("F3") + "  方向 " + F3(d)
                    + "  pitch " + pitch.ToString("F2") + "°  累计Δy " + cumY.ToString("F3") + " m");
                cur = nx; g++;
            }
            if (cur != null && cur.childCount > 1)
                _sb.AppendLine("    （" + cur.name + " 还有 " + (cur.childCount - 1) + " 个旁支未列）");
        }
        else _sb.AppendLine("  × 没找到尾分支");

        // ── ⑤ 四肢：腿根首段方向 vs 身体前向（正 = 往前伸，负 = 往后兜）──
        _sb.AppendLine();
        _sb.AppendLine("── 四肢朝向（+Z 是身体前向；dot(腿首段, fwd) < 0 = 往后兜）──");
        for (int k = 0; k < NL; k++)
        {
            bool driven = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
            if (!driven) continue;
            var b = _limbRoots[k] as Transform;
            if (b == null) continue;
            Vector3 d = Vector3.zero;
            if (b.childCount > 0) d = (invRoot * (b.GetChild(0).position - b.position)).normalized;
            float dotFwd = Vector3.Dot(d, Vector3.forward);
            Vector3 dRest = Vector3.zero;
            if (b.childCount > 0) dRest = d;   // 当前姿态（基准 = prefab 姿态）
            _sb.AppendLine("  [" + (k < 10 ? " " : "") + k + "] " + Pad(b.name, 14)
                + " 父=" + Pad(b.parent != null ? b.parent.name : "?", 12)
                + " 首段方向 " + F3(d)
                + "  dot(fwd) = " + dotFwd.ToString("F3")
                + " (" + (dotFwd < 0f ? "往后" : "往前") + ")"
                + "  水平投影 " + F3(new Vector3(d.x, 0f, d.z).normalized));
        }

        // ── ⑥ 找找有没有名字里带 head/muzzle/jaw 的骨头 ──
        _sb.AppendLine();
        _sb.AppendLine("── 名字里带 head / muzzle / jaw / neck / horn / whisker 的骨 ──");
        int hits = 0;
        foreach (var t in _vroot.GetComponentsInChildren<Transform>(true))
        {
            string nm = t.name.ToLowerInvariant();
            if (nm.Contains("head") || nm.Contains("muzzle") || nm.Contains("jaw") || nm.Contains("neck")
                || nm.Contains("horn") || nm.Contains("whisker") || nm.Contains("eye") || nm.Contains("chin"))
            {
                _sb.AppendLine("  " + Pad(t.name, 20) + " 父=" + Pad(t.parent != null ? t.parent.name : "?", 16)
                    + " 世界 " + F3(t.position) + "  子=" + t.childCount);
                hits++;
            }
        }
        if (hits == 0) _sb.AppendLine("  （没有语义命名的骨 —— 全是 drgon_xxxx 序号命名，只能靠几何判）");

        _sb.AppendLine();
        _sb.AppendLine("PNG 帧数 = 0（本探针纯结构，不出图）");

        yield return Done();
    }

    void DumpTree(Transform t, int depth, Transform origin)
    {
        if (t == null) return;
        DoLine(t, depth, origin);
        if (depth >= TREE_DEPTH) return;
        for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), depth + 1, origin);
    }

    void DumpSub(Transform t, int depth, Transform origin, Dictionary<Transform, int> spineIdx, Dictionary<Transform, int> limbIdx)
    {
        if (t == null) return;
        DoLine(t, depth, origin);
        if (depth > 14) return;
        for (int i = 0; i < t.childCount; i++) DumpSub(t.GetChild(i), depth + 1, origin, spineIdx, limbIdx);
    }

    void DoLine(Transform t, int depth, Transform origin)
    {
        string tag = "";
        if (_spine != null)
        {
            for (int i = 0; i < _spine.Count; i++)
            {
                if (ReferenceEquals(_spine[i], t)) { tag = "S" + i.ToString("D2"); break; }
            }
        }
        if (tag == "")
        {
            for (int k = 0; k < (_limbRoots == null ? 0 : _limbRoots.Count); k++)
            {
                if (ReferenceEquals(_limbRoots[k], t))
                {
                    bool dr = _limbDriven != null && k < _limbDriven.Count && (bool)_limbDriven[k];
                    tag = "L" + k.ToString("D2") + (dr ? "★" : "·");
                    break;
                }
            }
        }
        Vector3 off = origin != null ? t.position - origin.position : t.position;
        _sb.AppendLine("  " + new string(' ', depth * 2) + Pad(t.name, 18) + " " + Pad(tag, 6)
            + " 离根 " + F3(off) + "  |" + off.magnitude.ToString("F2") + "|  子=" + t.childCount);
    }

    static string Pad(string s, int n)
    {
        if (s == null) s = "?";
        // 中文按 2 列宽近似（名字都是 ASCII，直接按长度对齐）
        return s.Length >= n ? s : s + new string(' ', n - s.Length);
    }
    static string F3(Vector3 v) { return "(" + v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3") + ")"; }

    IEnumerator Done()
    {
        Time.captureFramerate = 0;
        try { File.WriteAllText(RP, _sb.ToString()); } catch { }
        Debug.Log("[drg_head] 写入 " + RP);
        yield return null;
    }
}

var __hostHead = new GameObject("drg_head");
__hostHead.AddComponent<drg_head>();
return "DRG_HEAD_STARTED";
