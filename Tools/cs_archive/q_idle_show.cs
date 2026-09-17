// 新待机（Feng 自带 Idle）出图 + 供录像。
// 保留控制器启用（这样角色处于真实 Idle 状态，且相机是真实第三人称机位）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/idle_new");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
    var pose = go.GetComponent<InkWash.Effects.WeaponHandPose>();

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    if (weapon == null) { Debug.LogError("no weapon"); yield break; }

    sb.AppendLine("========== 新待机核对 ==========");
    sb.AppendLine("Idle 实际播放 = " + (anim.GetCurrentAnimatorClipInfo(0).Length > 0
        ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "<空>"));
    sb.AppendLine("SwordVfx = " + (vfx != null ? vfx.WeaponName : "-") + "   HasWeapon=" + (vfx != null ? vfx.HasWeapon.ToString() : "-"));
    sb.AppendLine("WeaponHandPose = " + (pose != null ? ("armed=" + pose.Armed + " 卷指骨=" + pose.CurledBoneCount) : "(缺失)"));

    // 让 Idle 播一会儿，录到真实的待机循环
    for (int i = 0; i < 90; i++) yield return null;

    var camGo = new GameObject("TmpIdleNewCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    var bake = new Mesh();
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

    string[] views = { "F", "S", "B", "45" };
    float[] nts = { 0.0f, 0.5f };

    foreach (var nt in nts)
    {
        anim.Play("Idle", 0, nt);
        anim.Update(1f / 60f);
        yield return null;
        yield return null;

        Vector3 blade = weapon.TransformDirection(Vector3.down).normalized;
        Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
        sb.AppendLine("  nt=" + nt.ToString("F2") + "  剑身方向=" + blade.ToString("F3")
                      + "  与向下=" + Vector3.Angle(blade, Vector3.down).ToString("F1") + "°"
                      + "  与前方=" + Vector3.Angle(blade, fwd).ToString("F1") + "°");

        foreach (var view in views)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 viewDir = view == "F" ? fwd : view == "S" ? right
                            : view == "B" ? -fwd : (fwd * 0.6f + right * 0.8f).normalized;

            float mnY = float.MaxValue, mxY = float.MinValue;
            foreach (var s in smrs)
            {
                s.BakeMesh(bake, true);
                var m = s.transform.localToWorldMatrix;
                foreach (var v in bake.vertices) { float y = m.MultiplyPoint3x4(v).y; if (y < mnY) mnY = y; if (y > mxY) mxY = y; }
            }
            Vector3 center = new Vector3(go.transform.position.x, (mnY + mxY) * 0.5f, go.transform.position.z);
            float hh = Mathf.Max(1f, mxY - mnY);
            tcam.orthographic = false; tcam.fieldOfView = 34f;
            Vector3 cp = center + viewDir * (hh * 1.9f) + Vector3.up * (hh * 0.04f);
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);

            int W = 480, H = 620;
            var rt = RenderTexture.GetTemporary(W, H, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var snap = new Texture2D(W, H, TextureFormat.RGB24, false);
            snap.ReadPixels(new Rect(0, 0, W, H), 0, 0); snap.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(imgDir, "idle_" + view + "_t" + nt.ToString("F2").Replace(".", "") + ".png"),
                snap.EncodeToPNG());
            Object.Destroy(snap);
        }
    }

    // 转到左手侧看刀光锚点还对不对
    var tanchor = (Transform)null;
    foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "BladeTrail_Anchor") { tanchor = t; break; }
    if (tanchor != null)
    {
        Vector3 blade = weapon.TransformDirection(Vector3.down).normalized;
        Vector3 fist = hand.position;
        float al = Vector3.Dot(tanchor.position - fist, blade);
        float perp = (tanchor.position - fist - blade * al).magnitude;
        sb.AppendLine("刀光锚点：沿剑轴 " + al.ToString("F3") + " m ｜ 到剑轴垂距 " + perp.ToString("F4") + " m");
    }

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(bake);
    Object.Destroy(camGo);

    // 余下时间留给录像：真实待机循环
    for (int i = 0; i < 120; i++) yield return null;

    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_idlenew.txt"), sb.ToString());
    Debug.Log("[q_idle_show] done");
    yield return null;
}

return Body();
