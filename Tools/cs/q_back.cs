// q_back.cs —— 预演：非战斗（垂手站立）时剑的几种处置方式
// 不改任何资产，只在运行时把武器节点临时摆到背部/腰侧渲染，演完还原。
//
// 轴向约定（本项目已实测）：weapon = 含大网格的那个节点（Align 的子节点，localRotation = identity）
//   剑尖世界方向 = weapon.TransformDirection(Vector3.down)   ← 网格空间 -Y 是剑尖
//   刃厚法向     = weapon.TransformDirection(Vector3.forward) ← 网格空间 Z 是厚度
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/stand_back");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;

    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    if (weapon == null) { Debug.LogError("no weapon"); yield break; }
    var weaponRend = weapon.GetComponent<Renderer>();
    float weaponWorldScale = weapon.lossyScale.x;   // 世界缩放（含 Align 0.49 与角色 Visual 2.213）

    sb.AppendLine("武器节点: " + weapon.name + "  父=" + (weapon.parent != null ? weapon.parent.name : "-")
                  + "  爷=" + (weapon.parent != null && weapon.parent.parent != null ? weapon.parent.parent.name : "-"));
    sb.AppendLine("  localEuler=" + weapon.localEulerAngles.ToString("F2") + "  localScale=" + weapon.localScale.ToString("F3"));
    sb.AppendLine("  轴向自检(世界): -Y(剑尖)=" + weapon.TransformDirection(Vector3.down).ToString("F3")
                  + "  +Z(刃厚)=" + weapon.TransformDirection(Vector3.forward).ToString("F3"));
    sb.AppendLine();

    Transform chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
    if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
    Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    sb.AppendLine("骨骼: UpperChest/Chest=" + (chest != null ? chest.name : "null")
                  + "  Hips=" + (hips != null ? hips.name : "null"));
    if (chest == null || hips == null) { Debug.LogError("骨骼取不到"); yield break; }
    sb.AppendLine();

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();

    // 统一用 UAL1 的垂手待机
    AnimationClip ual1 = Clip("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx", "Rig|Idle_Loop");
    var original = anim.runtimeAnimatorController;
    var ovr = new AnimatorOverrideController(original);
    var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
    ovr.GetOverrides(pairs);
    for (int i = 0; i < pairs.Count; i++)
        if (pairs[i].Key != null && pairs[i].Key.name == "Idle")
            pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, ual1);
    ovr.ApplyOverrides(pairs);
    anim.runtimeAnimatorController = ovr;
    anim.Play("Idle", 0, 0f);
    anim.Update(1f / 60f);
    anim.Play("Idle", 0, 0.35f);
    anim.Update(1f / 60f);
    yield return null;
    yield return null;
    sb.AppendLine("实际播放片段: " + (anim.GetCurrentAnimatorClipInfo(0).Length > 0 ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "<空>"));
    sb.AppendLine();

    Transform origParent = weapon.parent;
    Vector3 op = weapon.localPosition; Quaternion oq = weapon.localRotation; Vector3 os = weapon.localScale;

    Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
    Vector3 right = go.transform.right; right.y = 0f; right.Normalize();
    Vector3 back = -fwd;

    // 候选：label, 隐藏, 剑尖世界方向, 刃厚法向, 世界位置
    var plans = new List<(string label, bool hide, Vector3 bladeDir, Vector3 flatDir, Vector3 worldPos)>();
    plans.Add(("S0_隐藏", true, Vector3.zero, Vector3.zero, Vector3.zero));
    plans.Add(("S1_竖直贴背", false, Vector3.down, back,
               chest.position + back * 0.13f + Vector3.up * 0.02f));
    plans.Add(("S2_斜跨背40度", false, (Vector3.down + right * 0.55f).normalized, back,
               chest.position + back * 0.14f - Vector3.up * 0.03f));
    plans.Add(("S3_腰侧斜挂", false, (Vector3.down + fwd * 0.45f + right * 0.25f).normalized, right,
               hips.position + right * 0.155f + back * 0.05f - Vector3.up * 0.10f));
    plans.Add(("S4_贴背_刃朝左右", false, Vector3.down, right,
               chest.position + back * 0.10f + Vector3.up * 0.02f));

    var camGo = new GameObject("TmpBackCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 400, TH = 560;
    string[] viewNames = { "front", "side", "back" };
    var sheet = new Texture2D(TW * plans.Count, TH * 3, TextureFormat.RGB24, false);

    for (int vi = 0; vi < 3; vi++)
    {
        for (int pi = 0; pi < plans.Count; pi++)
        {
            var plan = plans[pi];

            if (plan.hide)
            {
                weaponRend.enabled = false;
            }
            else
            {
                weaponRend.enabled = true;
                weapon.SetParent(null, true);
                Vector3 bladeLocal = Vector3.down;
                Vector3 flatLocal = Vector3.forward;

                Quaternion r1 = Quaternion.FromToRotation(weapon.rotation * bladeLocal, plan.bladeDir);
                weapon.rotation = r1 * weapon.rotation;
                Vector3 flatW = weapon.rotation * flatLocal;
                float roll = Vector3.SignedAngle(flatW, plan.flatDir, plan.bladeDir);
                weapon.rotation = Quaternion.AngleAxis(roll, plan.bladeDir) * weapon.rotation;
                weapon.position = plan.worldPos;

                Vector3 bladeW = (weapon.rotation * bladeLocal).normalized;
                // 剑尖 = 沿 bladeW 从网格中心（weapon.position 即网格原点）向剑尖走 0.5988 * Align缩放
                float tipDist = 0.5988f * weaponWorldScale;
                Vector3 tipW = weapon.position + bladeW * tipDist;
                sb.AppendLine(string.Format("{0,-18} 剑尖朝向={1,-24} 与向下={2,6:F1}° 与前方={3,6:F1}° 剑尖高度={4,5:F3}m",
                    plan.label, bladeW.ToString("F2"),
                    Vector3.Angle(bladeW, Vector3.down), Vector3.Angle(bladeW, fwd), tipW.y));
            }
            yield return null;

            Vector3 viewDir = vi == 0 ? fwd : (vi == 1 ? right : back);

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
            Vector3 cp = center + viewDir * (hh * 2.1f) + Vector3.up * (hh * 0.04f);
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);

            var rt = RenderTexture.GetTemporary(TW, TH, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tile = new Texture2D(TW, TH, TextureFormat.RGB24, false);
            tile.ReadPixels(new Rect(0, 0, TW, TH), 0, 0); tile.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            sheet.SetPixels(pi * TW, (2 - vi) * TH, TW, TH, tile.GetPixels());
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, string.Format("{0}_{1}.png", plan.label, viewNames[vi])), tile.EncodeToPNG());
            Object.Destroy(tile);

            if (!plan.hide)
            {
                weapon.SetParent(origParent, false);
                weapon.localPosition = op; weapon.localRotation = oq; weapon.localScale = os;
            }
            weaponRend.enabled = true;
        }
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_sheet.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(bake);
    Object.Destroy(camGo);
    anim.runtimeAnimatorController = original;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_back.txt"), sb.ToString());
    Debug.Log("[q_back] done");
    yield return null;
}

AnimationClip Clip(string path, string subName)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        if (!(o is AnimationClip c)) continue;
        if (c.name.StartsWith("__preview__")) continue;
        if (subName == null || c.name == subName) return c;
    }
    return null;
}

return Body();
