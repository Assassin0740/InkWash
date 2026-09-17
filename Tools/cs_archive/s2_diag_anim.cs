// 诊断：Avatar 类型 / 遮罩是否生效 / Player 的 Y 偏移与胶囊 / applyRootMotion
var sb = new System.Text.StringBuilder();

const string Fbx = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
const string CtrlPath = "Assets/_Project/Animations/Player.controller";
const string MaskPath = "Assets/_Project/Animations/Mask_UpperBody.mask";
const string PrefabPath = "Assets/_Project/Prefabs/Player.prefab";

// ---- 1. FBX 的 Avatar 与动画类型 ----
var imp = UnityEditor.AssetImporter.GetAtPath(Fbx) as UnityEditor.ModelImporter;
if (imp != null)
{
    sb.AppendLine("=== FBX 导入设置 ===");
    sb.AppendLine("  animationType = " + imp.animationType);
    sb.AppendLine("  avatarSetup   = " + imp.avatarSetup);
    sb.AppendLine("  sourceAvatar  = " + (imp.sourceAvatar == null ? "(无)" : imp.sourceAvatar.name));
    sb.AppendLine("  importAnimation = " + imp.importAnimation);
}
var avatar = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Avatar>(Fbx);
if (avatar != null)
{
    sb.AppendLine("  Avatar.isHuman = " + avatar.isHuman);
    sb.AppendLine("  Avatar.isValid = " + avatar.isValid);
    sb.AppendLine("  Avatar.name    = " + avatar.name);
}
else sb.AppendLine("  Avatar 取不到（可能 Avatar 存在子资源里）");

// ---- 2. 遮罩内容 ----
var mask = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AvatarMask>(MaskPath);
if (mask != null)
{
    sb.AppendLine("=== 遮罩 " + MaskPath + " ===");
    for (int i = 0; i < (int)UnityEngine.AvatarMaskBodyPart.LastBodyPart; i++)
        sb.AppendLine("  " + (UnityEngine.AvatarMaskBodyPart)i + " = " + mask.GetHumanoidBodyPartActive((UnityEngine.AvatarMaskBodyPart)i));
    sb.AppendLine("  transformCount = " + mask.transformCount);
}
else sb.AppendLine("遮罩取不到");

// ---- 3. 控制器图层 ----
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ac != null)
{
    sb.AppendLine("=== 控制器 " + CtrlPath + " ===");
    foreach (var L in ac.layers)
        sb.AppendLine("  " + L.name + "  weight=" + L.defaultWeight + "  mask=" + (L.avatarMask == null ? "(无)" : L.avatarMask.name)
            + "  mode=" + L.blendingMode + "  iK=" + L.iKPass);
}

// ---- 4. Player 预制体结构与关键数值 ----
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
if (prefab != null)
{
    sb.AppendLine("=== Player.prefab ===");
    var root = prefab.transform;
    sb.AppendLine("  root 名=" + root.name + "  localPos=" + root.localPosition + "  scale=" + root.localScale);
    for (int i = 0; i < root.childCount; i++)
    {
        var ch = root.GetChild(i);
        sb.AppendLine("  子:" + ch.name + "  localPos=" + ch.localPosition + "  scale=" + ch.localScale
            + "  hasAnimator=" + (ch.GetComponent<Animator>() != null));
    }
    var an = prefab.GetComponent<Animator>();
    if (an == null) an = prefab.GetComponentInChildren<Animator>(true);
    if (an != null)
    {
        sb.AppendLine("  Animator: go=" + an.gameObject.name + "  ctrl=" + (an.runtimeAnimatorController == null ? "(无)" : an.runtimeAnimatorController.name)
            + "  avatar=" + (an.avatar == null ? "(无)" : (an.avatar.name + " isHuman=" + an.avatar.isHuman))
            + "  rootMotion=" + an.applyRootMotion + "  updateMode=" + an.updateMode + "  culling=" + an.cullingMode);
    }
    var cc = prefab.GetComponent<CharacterController>();
    if (cc != null)
        sb.AppendLine("  CC: height=" + cc.height + " radius=" + cc.radius + " center=" + cc.center + " skinWidth=" + cc.skinWidth + " slopeLimit=" + cc.slopeLimit);
    else sb.AppendLine("  CC: 无");

    // 脚底的实际高度：用 Renderer 包围盒底部相对 root 的位置
    var rends = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    sb.AppendLine("  SkinnedMeshRenderer 数=" + rends.Length);
}

// ---- 5. 场景里 Main.unity 的相机与地面 ----
sb.AppendLine("=== 场景中玩家实例（若场景已打开）===");
var go = GameObject.Find("Player");
if (go != null)
{
    sb.AppendLine("  运行时 Player pos=" + go.transform.position + "  scale=" + go.transform.localScale);
    var an2 = go.GetComponentInChildren<Animator>(true);
    if (an2 != null) sb.AppendLine("  Animator go=" + an2.gameObject.name + " localPos=" + an2.transform.localPosition);
    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        sb.AppendLine("  SMR " + r.name + " bounds.min.y=" + r.bounds.min.y.ToString("F3") + " max.y=" + r.bounds.max.y.ToString("F3"));
}
else sb.AppendLine("  场景里没有 Player（未进 Play 或未打开场景）");

return sb.ToString();
