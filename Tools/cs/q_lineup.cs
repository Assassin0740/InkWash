// q_lineup.cs —— 主角与三只怪物逐个分格近景（判断丑不丑 / 区分度够不够）
//
// ★ 场景里**没有**敌人实例：它们由 WaveSpawner 运行时生成，所以
//   `FindObjectsOfType` 与 `Resources.FindObjectsOfTypeAll` 都找不到。
//   而 `--runtime` 编译下 `UNITY_EDITOR` 未定义 ⇒ 也不能用 AssetDatabase 加载预制体。
//   解法：**反射读 WaveSpawner 自己配的预制体引用**再实例化 —— 拿到的就是游戏里那一份。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;

public class QLineup
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    const int TW = 460, TH = 700;
    const int COLS = 4, ROWS = 1;

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_lineup ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/look"));
        Directory.CreateDirectory(_dir);

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();

        // ---- 反射抓 WaveSpawner 配的敌人预制体 ----
        var prefabs = new List<GameObject>();
        foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "WaveSpawner") continue;
            var wf = mb.GetType().GetField("waves");
            if (wf == null) continue;
            var waves = wf.GetValue(mb) as Array;
            if (waves == null) continue;
            foreach (var w in waves)
            {
                if (w == null) continue;
                var ef = w.GetType().GetField("entries");
                if (ef == null) continue;
                var entries = ef.GetValue(w) as Array;
                if (entries == null) continue;
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    var pf = e.GetType().GetField("prefab");
                    var prefab = pf != null ? pf.GetValue(e) as GameObject : null;
                    if (prefab != null && !prefabs.Contains(prefab)) prefabs.Add(prefab);
                }
            }
        }
        L("  WaveSpawner 配置的敌人预制体 " + prefabs.Count + " 个：");
        foreach (var p in prefabs) L("    " + p.name);

        var found = new List<GameObject>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            var inst = UnityEngine.Object.Instantiate(prefabs[i]);
            inst.name = prefabs[i].name + "_shot";
            inst.transform.position = new Vector3(-60f - i * 4f, 0f, -60f);
            // 与 WaveSpawner 生成的怪走同一条路：补一次水墨化
            InkWash.Rendering.InkMaterialForcer.ForceInk(inst, inst.name, true);
            found.Add(inst);
        }
        L("  已实例化 " + found.Count + " 个");

        var lineup = new List<GameObject>();
        if (pc != null) lineup.Add(pc.gameObject);
        lineup.AddRange(found);

        for (int i = 0; i < lineup.Count; i++)
        {
            var t = lineup[i].transform;
            t.gameObject.SetActive(true);
            var cc = t.GetComponent<CharacterController>();
            bool hadCc = cc != null;
            if (hadCc) cc.enabled = false;
            t.position = new Vector3(-4.5f + i * 3.0f, 0.05f, 0f);
            t.rotation = Quaternion.Euler(0f, 180f, 0f);
            if (hadCc) cc.enabled = true;
        }

        L("");
        L("---------- 包围盒尺寸（世界米）----------");
        foreach (var g in lineup)
        {
            var rs = g.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (rs.Length == 0) { L("  " + g.name + " 无渲染器"); continue; }
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            L(string.Format("  {0,-24} 高={1:F3}  宽={2:F3}  深={3:F3}  渲染器={4}",
                g.name, b.size.y, b.size.x, b.size.z, rs.Length));
        }

        var live = Camera.main;
        var go = new GameObject("RT_LineupCam");
        _cam = go.AddComponent<Camera>();
        if (live != null)
        {
            _cam.nearClipPlane = live.nearClipPlane;
            _cam.farClipPlane = live.farClipPlane;
            _cam.cullingMask = live.cullingMask;
        }
        _cam.fieldOfView = 34f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        _cam.enabled = false;

        int W = TW * COLS, H = TH * ROWS;
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        var prevPR = _cam.pixelRect;
        _cam.targetTexture = rt;

        int used = 0;
        for (int i = 0; i < lineup.Count && i < COLS * ROWS; i++)
        {
            var rs = lineup[i].GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float cy = b.center.y, hh = Mathf.Max(b.size.y, 1.0f);
            float dist = hh * 1.9f;
            _cam.transform.position = new Vector3(b.center.x, cy + hh * 0.05f, b.center.z - dist);
            _cam.transform.LookAt(new Vector3(b.center.x, cy, b.center.z));
            _cam.pixelRect = new Rect(i * TW, 0, TW, TH);
            _cam.Render();
            used++;
        }
        _cam.pixelRect = prevPR;
        _cam.targetTexture = null;

        var pa = RenderTexture.active; RenderTexture.active = rt;
        var sheet = new Texture2D(W, H, TextureFormat.RGBA32, false);
        sheet.ReadPixels(new Rect(0, 0, W, H), 0, 0); sheet.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(_dir, "lineup.png"), sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);
        L("");
        L("  出图 " + used + " 格，顺序 = " + string.Join(" | ", lineup.Take(used).Select(g => g.name).ToArray()));

        foreach (var g in found) UnityEngine.Object.Destroy(g);
        UnityEngine.Object.Destroy(go);
        return Flush();
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_lineup.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QLineup.Run();
