// 把 w_solve.cs 解出来的握持参数写进资产（必须在**非 Play** 态跑，Play 下 WriteGuard 会拦资产写）。
//
// 写三处：
//   1. W_Sword.prefab 的 Align 节点：localRotation / localPosition
//   2. Player.prefab 的 SwordVfx：bladeLocalOffset（刀光挂点必须跟着剑身方向走）
//   3. Player.prefab 根：补一个 WeaponHandPose 组件（徒手动画的握拳补丁）
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

const string WEAPON_PREFAB = "Assets/_Project/Prefabs/Weapons/W_Sword.prefab";
const string PLAYER_PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();

    // ---- 来自 Tools/reports/w_solve.txt（flatN 方案）----
    Vector3 ALIGN_POS = new Vector3(-0.005492450f, 0.062623090f, 0.204533600f);
    Quaternion ALIGN_ROT = new Quaternion(-0.499112900f, -0.500882100f, 0.499116400f, 0.500885400f);
    Vector3 TRAIL_OFFSET = new Vector3(-0.005491800f, 0.062286160f, 0.299759600f);

    yield return null;

    // ---------- 1. 武器 prefab 的 Align ----------
    sb.AppendLine("========== 1. " + WEAPON_PREFAB + " ==========");
    var wroot = PrefabUtility.LoadPrefabContents(WEAPON_PREFAB);
    if (wroot == null) { Debug.LogError("[w_apply2] 载入武器 prefab 失败"); yield break; }

    Transform align = null;
    foreach (var t in wroot.GetComponentsInChildren<Transform>(true))
        if (t.name == "Align") { align = t; break; }
    if (align == null) { Debug.LogError("[w_apply2] 武器 prefab 里没有 Align"); PrefabUtility.UnloadPrefabContents(wroot); yield break; }

    sb.AppendLine("  改前 Align  pos=" + align.localPosition.ToString("F6") + "  rot=" + align.localRotation.eulerAngles.ToString("F3") + "  scale=" + align.localScale.ToString("F3"));
    align.localPosition = ALIGN_POS;
    align.localRotation = ALIGN_ROT;
    PrefabUtility.SaveAsPrefabAsset(wroot, WEAPON_PREFAB);
    PrefabUtility.UnloadPrefabContents(wroot);
    sb.AppendLine("  改后 Align  pos=" + ALIGN_POS.ToString("F6") + "  rot=" + ALIGN_ROT.eulerAngles.ToString("F3"));

    // 回读校验
    var wcheck = AssetDatabase.LoadAssetAtPath<GameObject>(WEAPON_PREFAB);
    foreach (var t in wcheck.GetComponentsInChildren<Transform>(true))
        if (t.name == "Align")
        {
            sb.AppendLine("  回读 Align  pos=" + t.localPosition.ToString("F6") + "  scale=" + t.localScale.ToString("F3"));
            sb.AppendLine("        dot(pos,期望) = " + Vector3.Dot(t.localPosition, ALIGN_POS).ToString("F6")
                + "  |pos|x|期望| = " + (t.localPosition.magnitude * ALIGN_POS.magnitude).ToString("F6"));
        }

    // ---------- 2. Player prefab 的 SwordVfx ----------
    sb.AppendLine();
    sb.AppendLine("========== 2. " + PLAYER_PREFAB + " ==========");
    var proot = PrefabUtility.LoadPrefabContents(PLAYER_PREFAB);
    if (proot == null) { Debug.LogError("[w_apply2] 载入 Player prefab 失败"); yield break; }

    var vfx = proot.GetComponent<InkWash.Effects.SwordVfx>();
    if (vfx == null) { Debug.LogError("[w_apply2] Player 上没有 SwordVfx"); PrefabUtility.UnloadPrefabContents(proot); yield break; }
    sb.AppendLine("  改前 bladeLocalOffset = " + vfx.bladeLocalOffset.ToString("F6")
        + "  weaponPrefab=" + (vfx.weaponPrefab != null ? vfx.weaponPrefab.name : "(null)")
        + "  placeholderBlade=" + vfx.placeholderBlade);
    vfx.bladeLocalOffset = TRAIL_OFFSET;

    // ---------- 3. 握拳补丁 ----------
    var pose = proot.GetComponent<InkWash.Effects.WeaponHandPose>();
    bool added = false;
    if (pose == null)
    {
        pose = proot.AddComponent<InkWash.Effects.WeaponHandPose>();
        added = true;
    }
    sb.AppendLine((added ? "  新增 " : "  已有 ") + "WeaponHandPose：rightHand=" + pose.rightHand
        + " onlyWhenArmed=" + pose.onlyWhenArmed
        + " fingerCurl=" + pose.fingerCurl.ToString("F0") + " thumbCurl=" + pose.thumbCurl.ToString("F0"));

    PrefabUtility.SaveAsPrefabAsset(proot, PLAYER_PREFAB);
    PrefabUtility.UnloadPrefabContents(proot);

    // 回读校验
    var pcheck = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PREFAB);
    var v2 = pcheck.GetComponent<InkWash.Effects.SwordVfx>();
    var p2 = pcheck.GetComponent<InkWash.Effects.WeaponHandPose>();
    sb.AppendLine("  回读 bladeLocalOffset = " + (v2 != null ? v2.bladeLocalOffset.ToString("F6") : "(无)"));
    sb.AppendLine("  回读 SwordVfx.bladeLength = " + (v2 != null ? v2.bladeLength.ToString("F4") : "-"));
    sb.AppendLine("  回读 WeaponHandPose 组件 = " + (p2 != null ? "存在" : "缺失"));

    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    Debug.Log(sb.ToString());
    Debug.Log("[w_apply2] done");
    yield return null;
}

return Body();
