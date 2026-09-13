// S2 战斗装配：把代码层的改动真正落到场景 / 预制体上。
//
// 为什么必须有这一步：Unity 会把组件的字段值**序列化**进预制体。
// 改 C# 里的默认值**不会**回溯已有实例 —— 实测 Player.prefab 里仍是 S1 的旧值
// （walkSpeed 5.5 / runSpeed 9 / dashSpeed 18 / dashDuration 0.22）。
// 不显式推送，步伐同步与冲刺对齐就全是白做的。
var sb = new System.Text.StringBuilder();
string prefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
string shaderPath = "Assets/_Project/Shaders/InkSlash.shader";
string scenePath = "Assets/_Project/Scenes/Main.unity";

var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
sb.AppendLine("Shader 载入: " + (shader != null ? shader.name : "失败 → " + shaderPath));

// ==================================================================
// 1) 改玩家预制体
// ==================================================================
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
if (root == null)
{
    sb.AppendLine("!! 无法读取预制体: " + prefabPath);
    return sb.ToString();
}

// ---- 1a) 删掉 Visual 上多余的 Animator（controller=null，每帧刷警告）----
var visual = root.transform.Find("Visual");
if (visual != null)
{
    var stray = visual.GetComponent<Animator>();
    if (stray != null)
    {
        UnityEngine.Object.DestroyImmediate(stray, true);
        sb.AppendLine("[1a] 已删除 Visual 上多余的 Animator");
    }
    else sb.AppendLine("[1a] Visual 上没有多余 Animator");
}
else sb.AppendLine("[1a] 找不到 Visual 子节点");

// ---- 1b) 推送 PlayerController 数值（与 PlayerController.cs 的默认值对齐）----
var pc = root.GetComponent<InkWash.Player.PlayerController>();
if (pc == null) sb.AppendLine("[1b] !! 预制体上没有 PlayerController");
else
{
    pc.walkSpeed = 4.5f;
    pc.runSpeed = 7.2f;
    pc.acceleration = 26f;
    pc.deceleration = 34f;
    pc.turnSpeedDeg = 900f;

    pc.footSyncReferenceSpeed = 1.9f;
    pc.motionSpeedMin = 0.7f;
    // 上限必须 ≥ runSpeed/footSyncReferenceSpeed = 7.2/1.9 = 3.79，
    // 否则跑起来播放速度被截断，步频追不上位移 → 残余滑步（这正是"脚跟不上"的残留来源）。
    pc.motionSpeedMax = 4.2f;

    pc.dashSpeed = 14f;
    pc.dashDuration = 0.32f;      // 与 Slide_Start 动画时长对齐，消除"挥剑滑行"
    pc.dashCooldown = 0.55f;
    pc.dashInvincibleWindow = 0.2f;

    pc.comboLungeDistance = new float[] { 1.2f, 1.5f, 2.6f };
    pc.comboLungeDuration = new float[] { 0.22f, 0.24f, 0.42f };
    pc.comboSwingDuration = new float[] { 0.43f, 0.53f, 1.43f };
    pc.comboHitTime = new float[] { 0.15f, 0.18f, 0.4f };
    pc.comboRecCancelStart = new float[] { 0.3f, 0.3f, 0.45f };
    pc.attackInputBuffer = 0.25f;
    pc.attackTurnScale = 0.25f;

    pc.gravity = -24f;
    pc.groundedStick = -3f;

    sb.AppendLine("[1b] PlayerController 数值已推送: walk=" + pc.walkSpeed + " run=" + pc.runSpeed
        + " dash=" + pc.dashSpeed + "/" + pc.dashDuration + "s");
}

// ---- 1c) 挂 SwordVfx ----
var vfx = root.GetComponent<InkWash.Effects.SwordVfx>();
if (vfx == null)
{
    vfx = root.AddComponent<InkWash.Effects.SwordVfx>();
    sb.AppendLine("[1c] 已挂载 SwordVfx");
}
else sb.AppendLine("[1c] SwordVfx 已存在");

vfx.player = pc;
vfx.inkShader = shader;
vfx.bladeAnchor = null;    // 留空 → 自动找 hand_r
vfx.placeholderBlade = true;

UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(root);
sb.AppendLine("[1] 预制体已保存: " + prefabPath);

// ==================================================================
// 2) 改场景里的相机遮罩
//    层号实测（LayerMask.NameToLayer）：Ground = 10，Wall = 11 → 遮罩 3072。
//    踩过的坑：早先手工数 ProjectSettings/TagManager.asset 里的 Layer 列表，
//    把序号数错一位，写成 Ground=11 / Wall=12。务必用 NameToLayer 实测，不要靠数。
// ==================================================================
var cam = Camera.main;
if (cam == null) sb.AppendLine("[2] !! 场景里没有 MainCamera");
else
{
    var tpc = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
    if (tpc == null) sb.AppendLine("[2] !! MainCamera 上没有 ThirdPersonCamera");
    else
    {
        int ground = LayerMask.NameToLayer("Ground");
        int wall = LayerMask.NameToLayer("Wall");
        int mask = 0;
        if (ground >= 0) mask |= 1 << ground;
        if (wall >= 0) mask |= 1 << wall;
        tpc.collisionMask = mask;
        tpc.distance = 4.3f;
        tpc.pivotOffset = new Vector3(0f, 1.42f, 0f);
        tpc.baseFov = 48f;
        sb.AppendLine(string.Format("[2] 相机避障遮罩 = Ground({0})|Wall({1}) → {2}",
            ground, wall, tpc.collisionMask.value));
    }
}

// ==================================================================
// 3) 保存场景
// ==================================================================
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
sb.AppendLine("[3] 场景已保存: " + scenePath);

// ==================================================================
// 4) 复核：场景实例上的实际值
// ==================================================================
sb.AppendLine();
sb.AppendLine("========== 复核（场景实例）==========");
var playerGo = GameObject.Find("Player");
if (playerGo != null)
{
    var spc = playerGo.GetComponent<InkWash.Player.PlayerController>();
    if (spc != null)
        sb.AppendLine(string.Format("  PC: walk={0} run={1} dash={2}/{3}s footSync={4}",
            spc.walkSpeed, spc.runSpeed, spc.dashSpeed, spc.dashDuration, spc.footSyncReferenceSpeed));
    sb.AppendLine("  SwordVfx: " + (playerGo.GetComponent<InkWash.Effects.SwordVfx>() != null ? "已挂" : "缺失"));
    var v = playerGo.transform.Find("Visual");
    sb.AppendLine("  Visual 上的 Animator: (应为空) "
        + (v != null ? string.Join(",", System.Array.ConvertAll(v.GetComponents<Component>(),
            c => c == null ? "<Missing>" : c.GetType().Name)) : "找不到 Visual"));
}

return sb.ToString();
