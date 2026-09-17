// q_run2.cs —— 跑步候选：剑的「甩动幅度」量化 + 游戏机位连拍
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
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/run2");
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

    AnimationClip Sub(string p, string n)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
        {
            var c = o as AnimationClip;
            if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
        }
        return null;
    }
    string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
    string ual2 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";

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

    var cands = new List<(string label, AnimationClip clip)>
    {
        ("KI   Run01_Forward ", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx", null)),
        ("UAL1 Rig|Jog_Fwd   ", Sub(ual1, "Rig|Jog_Fwd_Loop")),
        ("UAL1 Rig|Sprint    ", Sub(ual1, "Rig|Sprint_Loop")),
        ("UAL2 Rig|Jog_Fwd*  ", Sub(ual2, "Armature|Jog_Fwd_Loop")),
    };

    sb.AppendLine("=== 跑步候选：剑的甩动（在角色自身坐标系里量）===");
    sb.AppendLine("候选                 时长   剑尖总行程  剑尖最大偏移  剑轴·竖直(范围)  躯干朝向波动");
    foreach (var (label, clip) in cands)
    {
        if (clip == null) { sb.AppendLine(label + " ★ 找不到"); continue; }
        const int NS = 90;
        float path = 0f, maxExc = 0f, angMin = 999f, angMax = -999f;
        Vector3 prev = Vector3.zero; bool first = true;
        // 躯干朝向：胸骨 forward 的水平偏航范围
        float yawMin = 999f, yawMax = -999f;
        for (int i = 0; i <= NS; i++)
        {
            clip.SampleAnimation(go, clip.length * i / (float)NS);
            Vector3 grip = weapon.transform.position, tip = BladeTip();
            Vector3 rel = go.transform.InverseTransformPoint(tip);   // 相对角色根的局部坐标
            if (!first) path += Vector3.Distance(prev, rel);
            prev = rel; first = false;
            maxExc = Mathf.Max(maxExc, rel.magnitude);
            float ang = Vector3.Angle((tip - grip).normalized, Vector3.up);
            if (ang < angMin) angMin = ang;
            if (ang > angMax) angMax = ang;
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (chest != null)
            {
                float yaw = chest.rotation.eulerAngles.y;
                if (yaw < yawMin) yawMin = yaw;
                if (yaw > yawMax) yawMax = yaw;
            }
        }
        sb.AppendLine(string.Format("{0} {1:F3}s  {2,8:F3} m  {3,10:F3} m   {4,5:F0}~{5,-4:F0}°    {6,5:F1}°",
            label, clip.length, path, maxExc, angMin, angMax, Mathf.Min(YawSpan(yawMin, yawMax), 360f - YawSpan(yawMin, yawMax))));
    }
    sb.AppendLine("判读：剑尖总行程越小，剑越稳（跑动中剑乱甩 = 廉价感主要来源）。");
    sb.AppendLine("      「躯干朝向波动」= 胸骨水平偏航在循环内的跨度。");

    // ---- 游戏机位连拍：12 帧 ----
    var camGo = new GameObject("TmpRunCam");
    var tcam = camGo.AddComponent<Camera>();
    tcam.CopyFrom(Camera.main);
    tcam.tag = "Untagged"; tcam.enabled = false;
    tcam.clearFlags = CameraClearFlags.SolidColor;
    tcam.backgroundColor = new Color(0.15f, 0.16f, 0.19f);

    var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
    var hidden = new List<Renderer>();
    foreach (var r in Object.FindObjectsOfType<Renderer>())
        if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

    int W = 200, H = 300, COLS = 12;
    var rowsList = cands.Where(c => c.clip != null).ToList();
    var sheet = new Texture2D(W * COLS, H * rowsList.Count, TextureFormat.RGB24, false);
    int row = 0;
    Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 0.95f, go.transform.position.z);
    Vector3 view = (-go.transform.forward * 0.82f + go.transform.right * 0.57f).normalized;
    foreach (var (label, clip) in rowsList)
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
            sheet.SetPixels(c * W, (rowsList.Count - 1 - row) * H, W, H, tile.GetPixels());
            Object.Destroy(tile);
        }
        row++;
    }
    sheet.Apply();
    File.WriteAllBytes(Path.Combine(imgDir, "_run_gamecam.png"), sheet.EncodeToPNG());
    Object.Destroy(sheet);
    sb.AppendLine();
    sb.AppendLine("出图 _run_gamecam.png  行顺序（上起）：" + string.Join(" | ", rowsList.Select(x => x.label)));

    foreach (var r in hidden) if (r != null) r.enabled = true;
    Object.Destroy(camGo);

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;
    if (stance != null) stance.ForceStance(false);
    var rig = Camera.main != null ? Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_run2.txt"), sb.ToString());
    Debug.Log("[q_run2] done");
    yield return null;
}

static float YawSpan(float a, float b) { return Mathf.Abs(Mathf.DeltaAngle(a, b)); }

return Body();
