// 腕部扭转角度扫描：验证「徒手待机 + 固定腕部旋前角」能不能凑出自然的持剑姿态。
// 若扫描中能找到一档既自然又不穿腿的角度，就不必去外部下素材。
//
// 扭转轴 = 前臂轴（肘→腕），这是解剖上真实存在的自由度（旋前/旋后），
// 不会破坏手腕与手臂的衔接；本项目角色袖口宽大，能遮住腕部形变。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/idle_twist");
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

    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var elbow = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

    Transform weapon = null;
    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
        if (f.sharedMesh != null && f.sharedMesh.vertexCount > 20000) { weapon = f.transform; break; }
    if (weapon == null) { Debug.LogError("no weapon"); yield break; }

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    var bake = new Mesh();

    AnimationClip kiIdle = Clip("Assets/ThirdParty/KevinIglesias/Animations/Male/Idles/HumanM@Idle01.fbx", "Idle01");
    AnimationClip kayIdle = Clip("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_General.fbx", "Idle_A");

    var clips = new List<(string label, AnimationClip clip)> { ("KI_Idle01", kiIdle), ("KayKit_Idle_A", kayIdle) };
    float[] twists = { 0f, 45f, 90f, 135f, -45f };

    var original = anim.runtimeAnimatorController;

    var camGo = new GameObject("TmpIdleTwistCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int TW = 330, TH = 460;
    var sheet = new Texture2D(TW * twists.Length, TH * clips.Count, TextureFormat.RGB24, false);

    for (int ci = 0; ci < clips.Count; ci++)
    {
        for (int ti = 0; ti < twists.Length; ti++)
        {
            var ovr = new AnimatorOverrideController(original);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ovr.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                if (pairs[i].Key != null && pairs[i].Key.name == "Idle_A")
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, clips[ci].clip);
            ovr.ApplyOverrides(pairs);
            anim.runtimeAnimatorController = ovr;

            anim.Play("Idle", 0, 0f);
            anim.Update(1f / 60f);
            anim.Play("Idle", 0, 0.35f);
            anim.Update(1f / 60f);
            yield return null;
            yield return null;

            Vector3 axis = (hand.position - elbow.position).normalized;
            hand.rotation = Quaternion.AngleAxis(twists[ti], axis) * hand.rotation;

            Vector3 blade = weapon.TransformDirection(Vector3.down).normalized;
            Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
            float angDown = Vector3.Angle(blade, Vector3.down);
            float angFwd = Vector3.Angle(blade, fwd);

            // 剑尖离角色根的水平距离（判断会不会扫到腿）
            Vector3 tip = weapon.TransformPoint(new Vector3(0f, -0.5988f, 0f));
            float horiz = new Vector2(tip.x - go.transform.position.x, tip.z - go.transform.position.z).magnitude;

            sb.AppendLine(clips[ci].label + "  扭转=" + twists[ti].ToString("F0") + "°"
                          + "  剑与向下=" + angDown.ToString("F1") + "°"
                          + "  与前方=" + angFwd.ToString("F1") + "°"
                          + "  剑尖水平距=" + horiz.ToString("F2") + " m"
                          + "  剑尖高=" + tip.y.ToString("F2") + " m");

            // 侧面视角：最利于判剑的朝向
            Vector3 viewDir = Vector3.Cross(Vector3.up, fwd).normalized;
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
            Vector3 cp = center + viewDir * (hh * 1.8f) + Vector3.up * (hh * 0.05f);
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

            sheet.SetPixels(ti * TW, (clips.Count - 1 - ci) * TH, TW, TH, tile.GetPixels());
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, clips[ci].label + "_tw" + twists[ti].ToString("F0") + ".png"), tile.EncodeToPNG());
            Object.Destroy(tile);

            anim.runtimeAnimatorController = original;
        }
    }

    sheet.Apply();
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(imgDir, "_sheet.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(bake);
    Object.Destroy(camGo);
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    sb.AppendLine();
    sb.AppendLine("列 = 扭转角 " + string.Join(", ", twists) + "（行 0=KI_Idle01, 行 1=KayKit_Idle_A）→ " + imgDir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_idletwist.txt"), sb.ToString());
    Debug.Log("[q_idletwist] done");
    yield return null;
}

AnimationClip Clip(string path, string subName)
{
    if (subName == null)
    {
        var c0 = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (c0 != null) return c0;
    }
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        if (!(o is AnimationClip c)) continue;
        if (c.name.StartsWith("__preview__")) continue;
        if (subName == null || c.name == subName) return c;
    }
    return null;
}

return Body();
