using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_body：**不猜解剖学**，直接算骨架树的「直径路径」（相距最远的两点之间的唯一通路）
//          —— 对一条蛇形龙，这条路径就是**尾尖↔头部**的完整身体链。
//          再把骨链渲染成图（顶视 + 侧视），让形状自己作证。
//
// 为什么必须这么做：ResolveSpine 的「每层取第一个子节点」在分叉骨架上是不可靠的 ——
//   `_spine` 到吻部的距离先减后增（i=11 时 1.42 m，i=23 又回到 3.31 m），
//   这不是脊柱的形状。身体链必须用树结构本身推出来。
public class dg_body : MonoBehaviour
{
    GameObject _dragon; Component _comp; Type _ty;
    System.Collections.IList _spine;
    readonly StringBuilder _sb = new StringBuilder();
    bool _done;

    const string IDX = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.LogError("dg_body: 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, Vector3.zero, Quaternion.identity);
        _dragon.name = "dg_body_Dragon";
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
        try { Build(); } catch (Exception e) { Debug.LogError("dg_body: " + e); }
        enabled = false;
    }

    // ---------- 骨架树 ----------
    Transform _root;
    List<Transform> _nodes;
    List<int>[] _adj;

    void BuildTree()
    {
        _root = null;
        var smrs = _dragon.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var s in smrs) if (s.rootBone != null) { _root = s.rootBone; break; }
        if (_root == null) _root = _dragon.transform;

        _nodes = new List<Transform>();
        var map = new Dictionary<Transform, int>();
        void Add(Transform t) { map[t] = _nodes.Count; _nodes.Add(t); }
        Add(_root);
        for (int i = 0; i < _nodes.Count; i++)
        {
            var t = _nodes[i];
            for (int k = 0; k < t.childCount; k++)
            {
                var c = t.GetChild(k);
                if (!map.ContainsKey(c)) Add(c);
            }
        }
        _adj = new List<int>[_nodes.Count];
        for (int i = 0; i < _nodes.Count; i++) _adj[i] = new List<int>();
        for (int i = 0; i < _nodes.Count; i++)
        {
            var t = _nodes[i];
            for (int k = 0; k < t.childCount; k++)
            {
                var c = t.GetChild(k);
                int j = map[c];
                _adj[i].Add(j); _adj[j].Add(i);
            }
        }
    }

    int Bfs(int src, out int[] prev)
    {
        int n = _nodes.Count;
        prev = new int[n];
        var dist = new int[n];
        for (int i = 0; i < n; i++) { prev[i] = -1; dist[i] = -1; }
        var q = new Queue<int>();
        q.Enqueue(src); dist[src] = 0;
        int far = src;
        while (q.Count > 0)
        {
            int u = q.Dequeue();
            if (dist[u] > dist[far]) far = u;
            foreach (var v in _adj[u]) if (dist[v] < 0) { dist[v] = dist[u] + 1; prev[v] = u; q.Enqueue(v); }
        }
        return far;
    }

    List<int> DiameterPath()
    {
        int a = Bfs(0, out _);
        int b = Bfs(a, out int[] prev);
        var path = new List<int>();
        int cur = b;
        while (cur >= 0) { path.Add(cur); cur = prev[cur]; }
        path.Reverse();
        return path;
    }

    int SpineIndexOf(Transform t)
    {
        for (int i = 0; i < _spine.Count; i++) if ((_spine[i] as Transform) == t) return i;
        return -1;
    }

    void Build()
    {
        var M = _dragon.transform;
        BuildTree();

        var allT = _dragon.GetComponentsInChildren<Transform>(true);
        Vector3 muzzle = Vector3.zero; bool hasM = false;
        foreach (var t in allT) if (t.name == "Muzzle") { muzzle = M.InverseTransformPoint(t.position); hasM = true; }

        var path = DiameterPath();
        var P = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++) P[i] = M.InverseTransformPoint(_nodes[path[i]].position);

        _sb.AppendLine("骨架树直径路径（= 真实身体链）");
        _sb.AppendLine("骨架节点 " + _nodes.Count + "   路径长度 " + path.Count + " 节");
        if (hasM) _sb.AppendLine("Muzzle 局部 " + muzzle.ToString("F2"));
        _sb.AppendLine();
        _sb.AppendLine(" 节  名字            局部坐标                 在_spine里?  到Muzzle  上一节段长  逐节弯角");
        float arc = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            int si = SpineIndexOf(_nodes[path[i]]);
            string seg = "", bendS = "";
            if (i > 0) { float L = (P[i] - P[i - 1]).magnitude; arc += L; seg = L.ToString("F3"); }
            if (i > 1)
            {
                Vector3 d0 = P[i - 1] - P[i - 2], d1 = P[i] - P[i - 1];
                if (d0.sqrMagnitude > 1e-9f && d1.sqrMagnitude > 1e-9f) bendS = Vector3.Angle(d0, d1).ToString("F1") + "°";
            }
            _sb.AppendLine(string.Format("{0,4}  {1,-16} {2,-26} {3,-11} {4,8:F2}  {5,9}  {6,8}",
                i, _nodes[path[i]].name, P[i].ToString("F2"),
                si >= 0 ? ("_spine[" + si + "]") : "—",
                hasM ? Vector3.Distance(P[i], muzzle) : -1f, seg, bendS));
        }
        _sb.AppendLine();
        _sb.AppendLine("折线总长(路径弧长) = " + arc.ToString("F3") + " m");
        _sb.AppendLine("首尾直线 = " + (P[path.Count - 1] - P[0]).magnitude.ToString("F3") + " m");
        _sb.AppendLine("端 A = " + _nodes[path[0]].name + "  " + P[0].ToString("F2")
                       + (hasM ? ("   到Muzzle " + Vector3.Distance(P[0], muzzle).ToString("F2")) : ""));
        _sb.AppendLine("端 B = " + _nodes[path[path.Count - 1]].name + "  " + P[path.Count - 1].ToString("F2")
                       + (hasM ? ("   到Muzzle " + Vector3.Distance(P[path.Count - 1], muzzle).ToString("F2")) : ""));

        Render(path, P, muzzle, hasM);

        string s = _sb.ToString();
        Debug.Log(s);
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_body.txt", s); } catch { }
    }

    // ---------- 渲染 ----------
    GameObject _vizRoot;

    void Render(List<int> path, Vector3[] P, Vector3 muzzle, bool hasM)
    {
        var M = _dragon.transform;
        var b = new Bounds(M.InverseTransformPoint(_dragon.transform.position), Vector3.zero);
        bool first = true;
        foreach (var r in _dragon.GetComponentsInChildren<Renderer>(true))
        {
            var c = M.InverseTransformPoint(r.bounds.center);
            var e = r.bounds.extents;
            if (first) { b = new Bounds(c, e * 2f); first = false; }
            else b.Encapsulate(new Bounds(c, e * 2f));
        }
        Vector3 center = b.center;

        _vizRoot = new GameObject("dg_body_Viz");
        float R = Mathf.Max(b.size.x, b.size.z) * 0.012f + 0.03f;

        // 身体链珠子：端 A 蓝 → 端 B 红（B 端若更近 Muzzle 则是头）
        bool headIsB = hasM &&
            Vector3.Distance(P[P.Length - 1], muzzle) < Vector3.Distance(P[0], muzzle);
        for (int i = 0; i < path.Count; i++)
        {
            float u = P.Length <= 1 ? 0f : i / (float)(P.Length - 1);
            float t = headIsB ? u : 1f - u;      // t: 0 = 尾, 1 = 头
            var col = Color.Lerp(new Color(0.15f, 0.35f, 0.85f), new Color(0.9f, 0.15f, 0.1f), t);
            MakeSphere(_vizRoot.transform, _nodes[path[i]].position, R * (0.9f + 1.2f * t), col);
        }
        // 其余骨：小灰球
        foreach (var t in _dragon.GetComponentsInChildren<Transform>(true))
            if (!path.Contains(_nodes.IndexOf(t)) && t.name != "Muzzle")
                MakeSphere(_vizRoot.transform, t.position, R * 0.35f, new Color(0.55f, 0.55f, 0.58f));
        if (hasM) MakeSphere(_vizRoot.transform, M.TransformPoint(muzzle), R * 1.6f, new Color(0.1f, 0.85f, 0.35f));

        // 相机 + 光
        var lightGo = new GameObject("dg_body_Light");
        lightGo.transform.SetParent(_vizRoot.transform, false);
        var lt = lightGo.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.1f;
        lt.transform.rotation = Quaternion.Euler(45f, 30f, 0f);

        var camGo = new GameObject("dg_body_Cam");
        camGo.transform.SetParent(_vizRoot.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.13f, 0.14f, 0.16f);
        float ext = Mathf.Max(b.size.x, b.size.z, b.size.y);
        cam.orthographicSize = ext * 0.58f;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;

        var rt = new RenderTexture(1100, 1100, 24);
        cam.targetTexture = rt;

        string dir = "D:/Unity Project/InkWash/Tools/screenshots/dragon";
        try { System.IO.Directory.CreateDirectory(dir); } catch { }

        Vector3[] dirs = {
            new Vector3(0f, 1f, 0.001f),   // 顶视
            new Vector3(1f, 0.001f, 0f),   // 侧视（从 +X）
        };
        string[] names = { "skel_top.png", "skel_side.png" };
        for (int k = 0; k < dirs.Length; k++)
        {
            cam.transform.position = center + dirs[k] * (ext * 2f);
            cam.transform.rotation = Quaternion.LookRotation(-dirs[k].normalized, Vector3.up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1100, 1100, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, 1100f, 1100f), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(dir + "/" + names[k], tex.EncodeToPNG());
            Destroy(tex);
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
        _sb.AppendLine();
        _sb.AppendLine("已出图：" + dir + "/skel_top.png  skel_side.png");
    }

    void MakeSphere(Transform parent, Vector3 worldPos, float r, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "dot";
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        go.transform.localScale = Vector3.one * r * 2f;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void OnDestroy()
    {
        if (_vizRoot != null) Destroy(_vizRoot);
        if (_dragon != null) Destroy(_dragon);
    }
}

var g = new GameObject("dg_body");
g.AddComponent<dg_body>();
return "DG_BODY_STARTED";
