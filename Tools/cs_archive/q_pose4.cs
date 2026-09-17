// q_pose4.cs —— 两件事一次量清
//  A. 战斗姿态「持剑待机」时，剑相对头/躯干的位置（剑插脑门？）+ 三视图出图
//  B. 「跑步僵硬」的客观量化：逐骨「角度行程」（一个循环内累计转动角），与走路/其他跑步片段对照
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    string imgDir = Path.Combine(projRoot, "Tools/screenshots/pose_idle");
    Directory.CreateDirectory(imgDir);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var vfx = go.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);

    var head = anim.GetBoneTransform(HumanBodyBones.Head);
    var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
    var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    var rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var rLower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
    var lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);

    sb.AppendLine("=========== A. 战斗姿态「持剑待机」的剑位 ===========");
    sb.AppendLine("（先切到战斗姿态，静置 1.2 s 让 Idle 稳定，再量）");

    // ---- 切到战斗姿态 ----
    if (stance != null) { stance.ForceStance(true); }
    float t0 = Time.time;
    while (Time.time - t0 < 1.2f) yield return null;

    if (stance != null)
    {
        sb.AppendLine("CombatStance.InCombat         = " + stance.InCombat);
        sb.AppendLine("CombatStance.CurrentIdleClip  = " + stance.CurrentIdleClipName);
        sb.AppendLine("CombatStance.WeaponMountPath  = " + stance.WeaponMountPath);
    }
    var st = anim.GetCurrentAnimatorStateInfo(0);
    sb.AppendLine("Animator 当前状态 hash         = " + st.shortNameHash + "   nt=" + st.normalizedTime.ToString("F3"));

    var weapon = vfx != null ? vfx.WeaponInstance : null;
    sb.AppendLine("武器实例                      = " + (weapon != null ? weapon.name : "<null>"));
    if (weapon != null)
    {
        Renderer mr = weapon.GetComponentInChildren<MeshRenderer>();
        if (mr == null) mr = weapon.GetComponentInChildren<Renderer>();
        var lb = mr != null ? mr.localBounds : new Bounds();
        Vector3 root = weapon.transform.position;
        Vector3 axis = weapon.transform.up;                       // socket 约定：+Y = 指向剑尖
        float tipLocal = lb.max.y;
        float worldScale = weapon.transform.lossyScale.y;
        // 用 bladeAnchor 定剑轴（比节点 up 更贴实际剑身）
        Vector3 anchor = vfx != null && vfx.bladeAnchor != null ? vfx.bladeAnchor.position : root + axis;
        Vector3 dirB = (anchor - root).sqrMagnitude > 1e-8f ? (anchor - root).normalized : axis;

        sb.AppendLine();
        sb.AppendLine("武器根（握柄中点）世界坐标     = " + V(root));
        sb.AppendLine("bladeAnchor 世界坐标          = " + V(anchor));
        sb.AppendLine("剑轴（归一）                  = " + V(dirB));
        sb.AppendLine("剑轴与世界 up 夹角            = " + Vector3.Angle(dirB, Vector3.up).ToString("F1") + "°");
        sb.AppendLine("mesh localBounds 尺寸         = " + V(lb.size) + "   最大y(局部)=" + tipLocal.ToString("F3"));
        sb.AppendLine("武器 lossyScale              = " + V(weapon.transform.lossyScale));
        Vector3 tip2 = root + dirB * (tipLocal * worldScale);
        sb.AppendLine("剑尖（估算）                  = " + V(tip2));

        sb.AppendLine();
        sb.AppendLine("头骨世界坐标                  = " + V(head.position));
        sb.AppendLine("胸骨世界坐标                  = " + V(chest.position));
        sb.AppendLine("右手世界坐标                  = " + V(rHand.position));
        sb.AppendLine("左手世界坐标                  = " + V(lHand.position));

        float dHead = DistPointSeg(head.position, root, tip2);
        float dChest = DistPointSeg(chest.position, root, tip2);
        sb.AppendLine();
        sb.AppendLine("★ 剑身线段 → 头骨   最近距离 = " + dHead.ToString("F4") + " m");
        sb.AppendLine("  剑身线段 → 胸骨   最近距离 = " + dChest.ToString("F4") + " m");
        sb.AppendLine("  头骨高度 = " + head.position.y.ToString("F3") + "   左手↔右手距离 = " + Vector3.Distance(lHand.position, rHand.position).ToString("F3"));

        // 剑轴 vs 前臂轴（验证「剑身 ⊥ 前臂」）
        Vector3 foreArm = (rHand.position - rLower.position).normalized;
        sb.AppendLine("  剑轴 vs 前臂轴 夹角 = " + Vector3.Angle(dirB, foreArm).ToString("F1") + "°   (≈90° 表示剑身⊥前臂)");
    }

    // ---- 出图：正视 / 侧视 / 俯视 ----
    {
        var camGo = new GameObject("TmpPoseCam");
        var tcam = camGo.AddComponent<Camera>();
        tcam.CopyFrom(Camera.main);
        tcam.tag = "Untagged"; tcam.enabled = false;
        tcam.clearFlags = CameraClearFlags.SolidColor;
        tcam.backgroundColor = new Color(0.16f, 0.17f, 0.20f);

        var keep = new HashSet<Renderer>(go.GetComponentsInChildren<Renderer>());
        var hidden = new List<Renderer>();
        foreach (var r in Object.FindObjectsOfType<Renderer>())
            if (r.enabled && !keep.Contains(r)) { r.enabled = false; hidden.Add(r); }

        int W = 300, H = 420;
        var sheet = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
        Vector3 center = new Vector3(go.transform.position.x, go.transform.position.y + 1.00f, go.transform.position.z);
        Vector3[] dirs = { -go.transform.forward, go.transform.right, Vector3.up };
        string[] names = { "正视", "侧视", "俯视" };
        for (int i = 0; i < 3; i++)
        {
            Vector3 cp = center + dirs[i] * 5f;
            tcam.orthographic = true; tcam.orthographicSize = 1.05f;
            tcam.transform.position = cp;
            tcam.transform.rotation = Quaternion.LookRotation(center - cp, i == 2 ? go.transform.forward : Vector3.up);
            var rt = RenderTexture.GetTemporary(W, H, 24);
            tcam.targetTexture = rt; tcam.Render(); tcam.targetTexture = null;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
            tile.ReadPixels(new Rect(0, 0, W, H), 0, 0); tile.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            sheet.SetPixels(i * W, 0, W, H, tile.GetPixels());
            Object.Destroy(tile);
            sb.AppendLine("出图位 " + i + " = " + names[i]);
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(imgDir, "_idle_3view.png"), sheet.EncodeToPNG());
        Object.Destroy(sheet);
        foreach (var r in hidden) if (r != null) r.enabled = true;
        Object.Destroy(camGo);
    }

    // ---- 恢复 ----
    if (stance != null) { stance.ForceStance(false); }
    // 相机与鼠标（上次踩过的坑：演示脚本结尾必须还原）
    var camFix = Camera.main;
    if (camFix != null)
    {
        var rig = camFix.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
        if (rig != null) rig.SetMouseLookEnabled(true);
    }

    // =====================================================================
    sb.AppendLine();
    sb.AppendLine("=========== B. 「动作僵硬」的客观量化 ===========");
    sb.AppendLine("指标：一个循环内该骨骼「累计转动角度」（越大越活）。幅度小 = 僵硬。");

    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    if (ctl != null) ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (stance != null) stance.enabled = false;
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

    var cands = new List<(string label, AnimationClip clip)>();
    cands.Add(("RUN  当前: Run01_Forward(KI)", Sub("Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx", null)));
    cands.Add(("RUN  KayKit Running_A", Sub("Assets/ThirdParty/KayKit/Animations/Rig_Medium/Rig_Medium_MovementBasic.fbx", "Running_A")));
    cands.Add(("RUN  UAL2 Rig|Run_Loop", Sub("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx", "Rig|Run_Loop")));
    cands.Add(("WALK 当前: Feng_Walk_Loop", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Feng_Walk_Loop.anim")));
    cands.Add(("IDLE 当前: Feng_Idle_Loop", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Feng_Idle_Loop.anim")));

    var bones = new (string n, HumanBodyBones b)[]
    {
        ("腰 hips", HumanBodyBones.Hips), ("脊 spine", HumanBodyBones.Spine),
        ("胸 chest", HumanBodyBones.Chest), ("头 head", HumanBodyBones.Head),
        ("左肩 LShoulder", HumanBodyBones.LeftUpperArm), ("右肩 RShoulder", HumanBodyBones.RightUpperArm),
        ("左肘 LElbow", HumanBodyBones.LeftLowerArm), ("右肘 RElbow", HumanBodyBones.RightLowerArm),
        ("左髋 LThigh", HumanBodyBones.LeftUpperLeg), ("右髋 RThigh", HumanBodyBones.RightUpperLeg),
        ("左膝 LKnee", HumanBodyBones.LeftLowerLeg), ("右膝 RKnee", HumanBodyBones.RightLowerLeg),
    };

    const int N = 60;
    sb.AppendLine();
    sb.AppendLine("片段                          时长    " + string.Join(" ", System.Linq.Enumerable.Select(bones, x => Pad(x.n, 13))));
    sb.AppendLine(new string('-', 40 + 14 * bones.Length));
    foreach (var (label, clip) in cands)
    {
        if (clip == null) { sb.AppendLine(Pad(label, 30) + " ★ 找不到"); continue; }
        var travel = new float[bones.Length];
        var prevQ = new Quaternion[bones.Length];
        for (int i = 0; i <= N; i++)
        {
            clip.SampleAnimation(go, clip.length * i / (float)N);
            for (int b = 0; b < bones.Length; b++)
            {
                var tr = anim.GetBoneTransform(bones[b].b);
                if (tr == null) continue;
                if (i > 0) travel[b] += Quaternion.Angle(prevQ[b], tr.rotation);
                prevQ[b] = tr.rotation;
            }
        }
        sb.AppendLine(Pad(label, 30) + clip.length.ToString("F3") + "  "
            + string.Join(" ", System.Linq.Enumerable.Select(travel, x => Pad(x.ToString("F0") + "°", 13))));
    }

    sb.AppendLine();
    sb.AppendLine("判读：同行里各骨数值越大越活。「跑」应当明显大于「走」；若只比走大一点或更小 ⇒ 僵硬。");

    // ---- Phase C：UAL2 里有哪些可用片段（找「持剑待机」的候选）----
    sb.AppendLine();
    sb.AppendLine("=========== C. UAL2 片段清单（找持剑待机候选）===========");
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx"))
    {
        var c = o as AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        sb.AppendLine("  " + Pad(c.name, 40) + c.length.ToString("F3") + "s");
    }

    anim.enabled = animWas;
    if (ctl != null) ctl.enabled = true;
    if (ik != null) ik.enabled = true;
    if (stance != null) stance.enabled = true;

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_pose4.txt"), sb.ToString());
    Debug.Log("[q_pose4] done");
    yield return null;
}

static string V(Vector3 v) { return string.Format("({0:F3}, {1:F3}, {2:F3})", v.x, v.y, v.z); }
static string Pad(string s, int n) { return s.Length >= n ? s.Substring(0, n) : s + new string(' ', n - s.Length); }

static float DistPointSeg(Vector3 p, Vector3 a, Vector3 b)
{
    Vector3 ab = b - a;
    float L2 = ab.sqrMagnitude;
    if (L2 < 1e-9f) return Vector3.Distance(p, a);
    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / L2);
    return Vector3.Distance(p, a + ab * t);
}

return Body();
