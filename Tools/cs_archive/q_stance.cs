// q_stance.cs —— 运行时验证「战斗 / 非战斗」双姿态
//   ① 默认应为非战斗：Idle 播垂手片段、武器挂在背上
//   ② 触发攻击后应切到战斗：Idle 播持剑待机、武器回到右手
//   ③ 静置 combatExitDelay 秒后应自动收回
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/stance_test");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponent<InkWash.Player.CombatStance>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    if (stance == null) { Debug.LogError("no CombatStance（组件没挂上？）"); yield break; }

    for (int i = 0; i < 180 && !stance.IsReady; i++) yield return null;
    sb.AppendLine("CombatStance.IsReady = " + stance.IsReady);
    if (!stance.IsReady) { System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_stance.txt"), sb.ToString()); yield break; }
    sb.AppendLine("  combatExitDelay = " + stance.combatExitDelay + "  startInCombat = " + stance.startInCombat);
    sb.AppendLine("  relaxedIdleClip = " + (stance.relaxedIdleClip != null ? stance.relaxedIdleClip.name : "null"));
    sb.AppendLine("  backSocket      = " + (stance.backSocket != null ? stance.backSocket.name : "null"));
    sb.AppendLine();

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();
    var camGo = new GameObject("TmpStanceCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);
    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
    Vector3 right = go.transform.right; right.y = 0f; right.Normalize();
    Vector3 back = -fwd;
    string[] vn = { "front", "side", "back" };

    // ---- 用局部函数记录 + 渲染 ----
    var report = new System.Func<string>(() =>
    {
        var cur = anim.GetCurrentAnimatorClipInfo(0);
        string clip = cur.Length > 0 ? cur[0].clip.name : "<空>";
        var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
        string mount = "?";
        if (vfx != null && vfx.WeaponInstance != null)
        {
            var p = vfx.WeaponInstance.transform.parent;
            mount = p == null ? "无父" : (p.name == "BackSocket" ? "背:" + p.name : "手:" + p.name);
        }
        return string.Format("  InCombat={0,-5}  Idle片段={1,-20} 武器挂点={2,-16} 握拳补丁={3,-5} 距上次战斗输入={4:F2}s",
            stance.InCombat, clip, mount, (pose != null ? pose.enabled.ToString() : "n/a"), stance.SinceCombatInput);
    });

    // ---------- ① 初始 ----------
    for (int i = 0; i < 40; i++) yield return null;
    sb.AppendLine("① 初始（期望：非战斗 / 垂手 / 剑在背）");
    sb.AppendLine(report());
    sb.AppendLine("  期望 Idle 片段应含 Idle_Loop、挂点应为「背:BackSocket」");
    yield return Shot(anim, tcam, smrs, bake, go, fwd, right, imgDir, "A_relaxed", vn);

    // ---------- ② 攻击 ----------
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.RequestInjectedAttack();
    for (int i = 0; i < 25; i++) yield return null;
    sb.AppendLine();
    sb.AppendLine("② 触发攻击后（期望：战斗 / 持剑待机 / 剑在右手）");
    sb.AppendLine(report());
    sb.AppendLine("  Phase=" + ctl.Phase + "  SwitchToCombatCount=" + stance.SwitchToCombatCount);
    yield return Shot(anim, tcam, smrs, bake, go, fwd, right, imgDir, "B_combat", vn);

    // ---------- ③ 静置 ----------
    stance.combatExitDelay = 1.5f;   // 只改运行时实例，不动资产
    sb.AppendLine();
    sb.AppendLine("③ 把 combatExitDelay 临时改为 1.5s，静置观察是否自动收回（打时间线）");
    bool sawRelaxed = false;
    for (int i = 0; i < 720; i++)
    {
        if (i % 30 == 0)
            sb.AppendLine(string.Format("   [t={0,5:F2}s] Phase={1,-11} InCombat={2,-5} SinceCombatInput={3,5:F2} 挂点={4,-16} 进战斗={5} 退战斗={6}",
                i / 60f, ctl.Phase, stance.InCombat, stance.SinceCombatInput, stance.WeaponMountPath,
                stance.SwitchToCombatCount, stance.SwitchToRelaxedCount));
        if (!stance.InCombat && i > 60) { sawRelaxed = true; break; }
        yield return null;
    }
    sb.AppendLine(report());
    sb.AppendLine("  >>> 观察到自动收回 = " + sawRelaxed + "   进战斗次数=" + stance.SwitchToCombatCount + "  退战斗次数=" + stance.SwitchToRelaxedCount);
    yield return Shot(anim, tcam, smrs, bake, go, fwd, right, imgDir, "C_back", vn);

    ctl.EndInputOverride();

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(bake);
    Object.Destroy(camGo);
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_stance.txt"), sb.ToString());
    Debug.Log("[q_stance] done");
    yield return null;
}

IEnumerator Shot(Animator anim, Camera tcam, SkinnedMeshRenderer[] smrs, Mesh bake,
                 GameObject go, Vector3 fwd, Vector3 right, string imgDir, string tag, string[] vn)
{
    for (int vi = 0; vi < 3; vi++)
    {
        Vector3 viewDir = vi == 0 ? fwd : (vi == 1 ? right : -fwd);
        float mnY = float.MaxValue, mxY = float.MinValue;
        foreach (var s in smrs)
        {
            s.BakeMesh(bake, true);
            var m = s.transform.localToWorldMatrix;
            foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mnY) mnY = y; if (y > mxY) mxY = y; }
        }
        Vector3 center = new Vector3(go.transform.position.x, (mnY + mxY) * 0.5f, go.transform.position.z);
        float hh = Mathf.Max(1f, mxY - mnY);
        tcam.orthographic = false; tcam.fieldOfView = 36f;
        Vector3 cp = center + viewDir * (hh * 2.15f) + Vector3.up * (hh * 0.03f);
        tcam.transform.position = cp;
        tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
        var rt = RenderTexture.GetTemporary(440, 600, 24);
        tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tile = new Texture2D(440, 600, TextureFormat.RGB24, false);
        tile.ReadPixels(new Rect(0, 0, 440, 600), 0, 0); tile.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, tag + "_" + vn[vi] + ".png"), tile.EncodeToPNG());
        Object.Destroy(tile);
        yield return null;
    }
}

return Body();
