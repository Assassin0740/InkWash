// q_final.cs —— 收口验收：① 重击键位断言 ② 最终展示图（主角前后 / 墨化敌人 / 场景全景）
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;

public class QFinal
{
    static StringBuilder _sb;
    static void L(string s) { _sb.AppendLine(s); }
    static Camera _cam;
    static string _dir;

    const int TW = 560, TH = 760;
    const int COLS = 4, ROWS = 1;

    public static string Run()
    {
        _sb = new StringBuilder();
        L("================ q_final ================");
        _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/screenshots/final"));
        Directory.CreateDirectory(_dir);

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc == null) { L("!! 没找到 PlayerController"); return Flush(); }
        var anim = pc.GetComponentInChildren<Animator>();

        // ================= ① 重击键位断言 =================
        L("");
        L("---------- ① 重击（K 键）断言 ----------");
        int swing = 0, hit = 0, lastSwing = -1;
        pc.SwingStarted += s => { swing++; lastSwing = s; };
        pc.HitMoment += s => { hit++; };

        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.position = new Vector3(0f, 0.05f, 0f);
        pc.transform.rotation = Quaternion.identity;
        if (cc != null) cc.enabled = true;

        L("  调用前 Phase = " + pc.Phase);
        bool ok = pc.TryStartHeavyAttack();
        L("  TryStartHeavyAttack() 返回 = " + ok);
        L("  调用后 Phase = " + pc.Phase + "   IsDashing=" + pc.IsDashing);
        L("  SwingStarted 触发次数 = " + swing + "   最后一段号 = " + lastSwing + "（应为 3 = 重击终结技）");
        anim.Update(0.01f);
        var nxt = anim.GetNextAnimatorStateInfo(0);
        var cur = anim.GetCurrentAnimatorStateInfo(0);
        string nextName = "(none)";
        for (int i = 0; i < anim.runtimeAnimatorController.animationClips.Length; i++) { }
        L("  Animator 当前状态 hash=" + cur.shortNameHash + "  目标状态 hash=" + nxt.shortNameHash);
        L("  断言：Phase=Attack 且 段号=3 ⇒ " +
            ((pc.Phase.ToString() == "Attack" && lastSwing == 3) ? "**通过**" : "**未通过**"));

        // ================= ② 最终展示图 =================
        // 重新抓敌人（反射读 WaveSpawner 配置的预制体）
        var prefabs = new List<GameObject>();
        foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "WaveSpawner") continue;
            var wf = mb.GetType().GetField("waves"); if (wf == null) continue;
            var waves = wf.GetValue(mb) as Array; if (waves == null) continue;
            foreach (var w in waves)
            {
                var ef = w.GetType().GetField("entries"); if (ef == null) continue;
                var es = ef.GetValue(w) as Array; if (es == null) continue;
                foreach (var e in es)
                {
                    var pf = e.GetType().GetField("prefab");
                    var go = pf != null ? pf.GetValue(e) as GameObject : null;
                    if (go != null && !prefabs.Contains(go)) prefabs.Add(go);
                }
            }
        }
        var enemies = new List<GameObject>();
        foreach (var p in prefabs)
        {
            var inst = UnityEngine.Object.Instantiate(p);
            inst.name = p.name + "_fin";
            inst.transform.position = new Vector3(-40f - enemies.Count * 4f, 0f, -40f);
            InkWash.Rendering.InkMaterialForcer.ForceInk(inst, inst.name, true);
            var ea = inst.GetComponentInChildren<Animator>();
            if (ea != null)
            {
                var ids = ea.runtimeAnimatorController != null
                        ? ea.runtimeAnimatorController.animationClips.Select(c => c.name).ToArray() : new string[0];
                string pick = ids.FirstOrDefault(s => s.ToLower().Contains("idle")) ?? (ids.Length > 0 ? ids[0] : null);
                if (pick != null) { ea.applyRootMotion = false; ea.Play(pick, 0, 0f); ea.Update(0.0001f); }
            }
            enemies.Add(inst);
        }
        L("");
        L("  已实例化并墨化敌人 " + enemies.Count + " 个");

        var live = Camera.main;
        var cgo = new GameObject("RT_FinalCam");
        _cam = cgo.AddComponent<Camera>();
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
        anim.applyRootMotion = false;
        anim.Play("Idle", 0, 0f); anim.Update(0.0001f);

        // 依次拍：主角前 / 主角后 / 选一只怪 / 主角与怪并肩（看区分度）
        var shots = new List<(Transform tf, bool front)>();
        shots.Add((pc.transform, true));
        shots.Add((pc.transform, false));
        if (enemies.Count > 0) shots.Add((enemies[0].transform, true));

        for (int i = 0; i < shots.Count && i < COLS; i++)
        {
            var tf = shots[i].tf;
            var sk = tf.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).ToArray();
            Bounds b;
            if (sk.Length > 0) { b = sk[0].bounds; foreach (var r in sk) b.Encapsulate(r.bounds); }
            else { b = tf.GetComponentsInChildren<Renderer>()[0].bounds; }
            float hh = Mathf.Max(b.size.y, 1f);
            float dist = hh * 1.6f;
            Vector3 aim = b.center;
            _cam.transform.position = new Vector3(aim.x, aim.y, aim.z + (shots[i].front ? -dist : dist));
            _cam.transform.LookAt(aim);
            _cam.pixelRect = new Rect(i * TW, 0, TW, TH);
            _cam.Render();
        }
        // 第 4 格：主角 + 全部敌人并排（区分度）
        int slot = shots.Count;
        if (slot < COLS)
        {
            var line = new List<Transform> { pc.transform };
            line.AddRange(enemies.Select(e => e.transform));
            float span = 1.5f; float x0 = -(line.Count - 1) * span * 0.5f;
            for (int i = 0; i < line.Count; i++)
            {
                var t2 = line[i];
                var c2 = t2.GetComponent<CharacterController>();
                bool h2 = c2 != null; if (h2) c2.enabled = false;
                t2.position = new Vector3(x0 + i * span, 0.05f, 0f);
                t2.rotation = Quaternion.Euler(0f, 0f, 0f);
                if (h2) c2.enabled = true;
            }
            int n = line.Count;
            _cam.transform.position = new Vector3(0f, 1.15f, -6.0f - n * 0.35f);
            _cam.transform.LookAt(new Vector3(0f, 1.0f, 0f));
            _cam.fieldOfView = 30f;
            _cam.pixelRect = new Rect(slot * TW, 0, TW, TH);
            _cam.Render();
            L("  第 " + (slot + 1) + " 格 = 主角与 " + (n - 1) + " 只怪并排");
        }

        _cam.pixelRect = prevPR;
        _cam.targetTexture = null;
        var pa = RenderTexture.active; RenderTexture.active = rt;
        var sheet = new Texture2D(W, H, TextureFormat.RGBA32, false);
        sheet.ReadPixels(new Rect(0, 0, W, H), 0, 0); sheet.Apply();
        RenderTexture.active = pa; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(_dir, "final_sheet.png"), sheet.EncodeToPNG());
        UnityEngine.Object.Destroy(sheet);
        L("  格1 = 主角正面  格2 = 主角背面（看背上的剑）  格3 = 墨化后的怪  格4 = 并排对比");

        foreach (var e in enemies) UnityEngine.Object.Destroy(e);
        UnityEngine.Object.Destroy(cgo);
        return Flush();
    }

    static string Flush()
    {
        L("");
        L("================ end ================");
        string outp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/reports/q_final.txt"));
        File.WriteAllText(outp, _sb.ToString());
        return "written: " + outp + "  (" + _sb.Length + " chars)";
    }
}

return QFinal.Run();
