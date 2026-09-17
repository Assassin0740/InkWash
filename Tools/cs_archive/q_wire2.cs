// q_wire2.cs —— 编辑期：把 BackSocket 挂点与 CombatStance 组件落到 Player.prefab
// 常量来自 Tools/reports/q_socket.txt（已在 Play 模式垂手姿态下求解并渲染确认）
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new StringBuilder();
    const string PATH = "Assets/_Project/Prefabs/Player/Player.prefab";
    const string BONE = "CC_Base_Spine02";

    // 来自 q_socket.txt（父骨骼 = CC_Base_Spine02）
    Vector3 SOCKET_POS = new Vector3(-0.041672f, 0.196906f, -0.055625f);
    Quaternion SOCKET_ROT = new Quaternion(0.617769f, -0.359445f, 0.591123f, 0.373810f);

    var root = PrefabUtility.LoadPrefabContents(PATH);
    if (root == null) { Debug.LogError("[q_wire2] 打不开 prefab"); yield break; }
    try
    {
        // ---------- 1. 找骨骼 ----------
        Transform bone = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == BONE) { bone = t; break; }
        if (bone == null) { Debug.LogError("[q_wire2] 找不到骨骼 " + BONE); yield break; }
        sb.AppendLine("父骨骼: " + BONE + "  (世界路径 " + Path(bone) + ")");

        // ---------- 2. 建 / 更新 BackSocket ----------
        Transform socket = null;
        foreach (var t in bone.GetComponentsInChildren<Transform>(true))
            if (t != bone && t.name == "BackSocket") { socket = t; break; }
        if (socket == null)
        {
            socket = new GameObject("BackSocket").transform;
            socket.SetParent(bone, false);
            sb.AppendLine("新建 BackSocket");
        }
        else sb.AppendLine("复用已存在的 BackSocket");
        socket.localPosition = SOCKET_POS;
        socket.localRotation = SOCKET_ROT;
        socket.localScale = Vector3.one;
        sb.AppendLine("  localPos   = " + socket.localPosition.ToString("F6"));
        sb.AppendLine("  localEuler = " + socket.localEulerAngles.ToString("F4"));
        sb.AppendLine("  localScale = " + socket.localScale.ToString("F3"));

        // ---------- 3. 挂 CombatStance ----------
        var stance = root.GetComponent<InkWash.Player.CombatStance>();
        if (stance == null)
        {
            stance = root.AddComponent<InkWash.Player.CombatStance>();
            sb.AppendLine("新建 CombatStance 组件");
        }
        else sb.AppendLine("复用已存在的 CombatStance");

        var anim = root.GetComponent<Animator>();
        var ctl = root.GetComponent<InkWash.Player.PlayerController>();
        var vfx = root.GetComponentInChildren<InkWash.Effects.SwordVfx>(true);
        var pose = root.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
        if (anim == null) { Debug.LogError("[q_wire2] 没有 Animator"); yield break; }
        if (ctl == null) { Debug.LogError("[q_wire2] 没有 PlayerController"); yield break; }
        if (vfx == null) { Debug.LogError("[q_wire2] 没有 SwordVfx"); yield break; }

        AnimationClip relaxed = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(
            "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx"))
            if (o is AnimationClip c && c.name == "Rig|Idle_Loop") { relaxed = c; break; }
        if (relaxed == null) { Debug.LogError("[q_wire2] 找不到 UAL1 的 Rig|Idle_Loop"); yield break; }

        stance.animator = anim;
        stance.player = ctl;
        stance.swordVfx = vfx;
        stance.backSocket = socket;
        stance.handPose = pose;
        stance.relaxedIdleClip = relaxed;
        stance.combatExitDelay = 6f;
        stance.startInCombat = false;

        sb.AppendLine();
        sb.AppendLine("========== 接线后（重新读盘复核）==========");
        sb.AppendLine("  animator          = " + (stance.animator != null ? stance.animator.name : "null"));
        sb.AppendLine("  player            = " + (stance.player != null ? stance.player.name : "null"));
        sb.AppendLine("  swordVfx          = " + (stance.swordVfx != null ? stance.swordVfx.name : "null")
                      + "  (weaponPrefab=" + (stance.swordVfx != null && stance.swordVfx.weaponPrefab != null ? stance.swordVfx.weaponPrefab.name : "null") + ")");
        sb.AppendLine("  backSocket        = " + (stance.backSocket != null ? Path(stance.backSocket) : "null"));
        sb.AppendLine("  handPose          = " + (stance.handPose != null ? stance.handPose.name : "null"));
        sb.AppendLine("  relaxedIdleClip   = " + (stance.relaxedIdleClip != null ? stance.relaxedIdleClip.name : "null"));
        sb.AppendLine("  combatExitDelay   = " + stance.combatExitDelay);
        sb.AppendLine("  startInCombat     = " + stance.startInCombat);

        PrefabUtility.SaveAsPrefabAsset(root, PATH);
        sb.AppendLine();
        sb.AppendLine("已保存 " + PATH);
    }
    finally
    {
        PrefabUtility.UnloadPrefabContents(root);
    }

    System.IO.File.WriteAllText("Tools/reports/q_wire2.txt", sb.ToString(), new UTF8Encoding(false));
    Debug.Log("[q_wire2] done");
    yield return null;
}

string Path(Transform t)
{
    var sb = new StringBuilder(t.name);
    var p = t.parent;
    while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
    return sb.ToString();
}

return Body();
