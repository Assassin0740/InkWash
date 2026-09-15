// q_show_baked.cs —— 渲染烘焙变体 + 量剑的甩动（挑最佳「持剑跑」）
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/baked");
    Directory.CreateDirectory(imgDir);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);

    if (stance != null) stance.ForceStance(true);
    float tw = Time.time; while (Time.time - tw < 0.4f) yield return null;
    var weapon = vfx != null ? vfx.WeaponInstance : null;

    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.enabled = false;
    if (pose != null) pose.enabled = false;
    bool animWas = anim.enabled;
    anim.enabled = false;

    Vector3 BladeTip()
    {
        var rs = weapon.GetComponentsInChildren<Renderer>()
            .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (rs.Length == 0) return weapon.transform.position;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        Vector3 sz = b.size;
        Vector3 ax = sz.x >= sz.y && sz.x >= sz.z ? Vector3.right : (sz.y >= sz.z ? Vector3.up : Vector3.forward);
        float half = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)) * 0.5f;
        Vector3 p1 = b.center + ax * half, p2 = b.center - ax * half;
        Vector3 grip = weapon.transform.position;
        return Vector3.Distance(p1, grip) >= Vector3.Distance(p2, grip) ? p1 : p2;
    }

    var names = new List<string>();
    foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Project/Animations/Baked" }))
    {
        string p = AssetDatabase.GUIDToAssetPath(g);
        var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        if (c != null) names.Add(p);
    }
    names.Sort();
    sb.AppendLine("=== 烘焙变体：剑的甩动 ===");
    sb.AppendLine("变体                    剑尖总行程   剑轴·竖直范围   剑尖相对头骨(dx,dy,dz)  剑→面部最小");

    var clips = new List<(string label, AnimationClip clip)>();
    var head = anim.GetBoneTransform(HumanBodyBones.Head);
    foreach (var p in names)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
        if (clip == null) continue;
        clips.Add((clip.name, clip));
        const int NS = 90;
        float path = 0f, angMin = 999f, angMax = -999f, minFace = 999f;
        Vector3 prev = Vector3.zero; bool first = true; Vector3 lastRel = Vector3.zero;
        for (int i = 0; i <= NS; i++)
        {
            clip.SampleAnimation(go, clip.length * i / (float)NS);
            Vector3 grip = weapon.transform.position, tip = BladeTip();
            Vector3 rel = go.transform.InverseTransformPoint(tip);
            if (!first) path += Vector3.Distance(prev, rel);
            prev = rel; first = false;
            lastRel = tip - head.position;
            float ang = Vector3.Angle((tip - grip).normalized, Vector3.up);
            if (ang < angMin) angMin = ang;
            if (ang > angMax) angMax = ang;
            Vector3 face = head.position + head.up * 0.07f + head.forward * 0.07f;
            minFace = Mathf.Min(minFace, DistPointSeg(face, grip, tip));
        }
        sb.AppendLine(string.Format("{0,-22} {1,8:F3} m   {2,4:F0}~{3,-4:F0}°   ({4,6:F2},{5,6:F2},{6,6:F2})   {7,7:F3}",
            clip.name, path, angMin, angMax, lastRel.x, lastRel.y, lastRel.z, minFace));
    }

    // ---- 出图：每个变体 6 帧游戏机位 ----
    var camGo = new GameObject("TmpBakedCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int W = 200, H = 290, COLS = 7;
    var sheet = new Texture2D(W * COLS, H * clips.Count, TextureFormat.RGB24, false);
    int row = 0;
    Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 0.95f, go.transform.position.z);
    Vector3 view = (-go.transform.forward * 0.82f + go.transform.right * 0.57f).normalized;
    foreach (var (label, clip) in clips)
    {
        for (int c = 0; c < COLS; c++)
        {
            clip.SampleAnimation(go, clip.length * c / (float)COLS);
            yield return null;
            Vector3 cp = center + view * 6f;
            tcam.orthographic = true; tcam.orthographicSize = 1.25f;
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(center - cp, Vector3.up);
            var rt = RenderTexture.GetTemporary(W, H, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev2 = RenderTexture.active; RenderTexture.active = rt;
            var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
            RenderTexture.active = prev2; RenderTexture.ReleaseTemporary(rt);
            sheet.SetPixels(c * W, (clips.Count - 1 - row) * H, W, H, tile.GetPixels());
            Object.Destroy(tile);
        }
        row++;
    }
    sheet.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_baked.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);
    sb.AppendLine();
    sb.AppendLine("出图 _baked.png  行顺序（上起）：" + string.Join(" | ", clips.Select(x => x.label)));

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;
    if (stance != null) stance.ForceStance(false);
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_show_baked.txt"), sb.ToString());
    Debug.Log("[q_show_baked] done");
    yield return null;
}

static float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
{
    Vector3 ab = b - a; float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return Vector3.Distance(p, a);
    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / L2);
    return Vector3.Distance(p, a + ab * t);
}

return Body();
