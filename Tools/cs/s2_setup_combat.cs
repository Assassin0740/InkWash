// S2.1 战斗装配：把代码层的改动真正落到场景 / 预制体上。
//
// 为什么必须有这一步：Unity 会把组件字段值**序列化**进预制体，
// 改 C# 里的默认值**不会**回溯已有实例。本轮改了速度模型、冲刺时长、拖尾门控，
// 不显式推送就全是白做的。
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

var visual = root.transform.Find("Visual");

// ---- 1a) 删掉 Visual 上多余的 Animator（controller=null，每帧刷警告）----
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
    // 走路 / 跑步分开：上一版 walk=4.5（其实是慢跑速度）跑=7.2，两档都挤在"跑步"体感上，
    // 而且都用同一段"抱物走"动画硬撑。现在走路 2.2、跑步 5.6，各配自己的动画片段。
    pc.walkSpeed = 2.2f;
    pc.runSpeed = 5.6f;
    pc.runAnimEnterSpeed = 3.0f;
    pc.runAnimExitSpeed = 2.5f;
    pc.acceleration = 26f;
    pc.deceleration = 34f;
    pc.turnSpeedDeg = 900f;

    pc.walkRefSpeed = 1.55f;
    pc.runRefSpeed = 3.10f;
    pc.motionSpeedMin = 0.6f;
    // 上限必须 ≥ max(2.2/1.55, 5.6/3.1) = max(1.42, 1.81)，取 2.2 留余量
    pc.motionSpeedMax = 2.2f;

    pc.dashSpeed = 7.4f;
    pc.dashDuration = 0.72f;      // 与 UAL1 Roll（1.47s）对齐：状态机按 length/本值 定速
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
        + " dash=" + pc.dashSpeed + "/" + pc.dashDuration + "s"
        + " refSpeed=" + pc.walkRefSpeed + "/" + pc.runRefSpeed);
}

// ---- 1c) 挂 SwordVfx 并配拖尾门控 ----
var vfx = root.GetComponent<InkWash.Effects.SwordVfx>();
if (vfx == null) { vfx = root.AddComponent<InkWash.Effects.SwordVfx>(); sb.AppendLine("[1c] 已挂载 SwordVfx"); }
else sb.AppendLine("[1c] SwordVfx 已存在");

vfx.player = pc;
vfx.inkShader = shader;
vfx.bladeAnchor = null;    // 留空 → 自动找 hand_r
vfx.placeholderBlade = true;
// 拖尾门控（问题：刀还没速度就出刀光）
vfx.trailMinSpeed = 3.0f;
vfx.trailStartDelay = 0.05f;
vfx.trailHoldMinSpeed = 1.6f;
sb.AppendLine("[1c] 拖尾门控: 进入≥" + vfx.trailMinSpeed + " m/s 维持≥" + vfx.trailHoldMinSpeed + " m/s");

// ---- 1d) 挂 FootIK（问题：攻击时脚陷进地面）----
var footIk = root.GetComponent<InkWash.Player.FootIK>();
if (footIk == null) { footIk = root.AddComponent<InkWash.Player.FootIK>(); sb.AppendLine("[1d] 已挂载 FootIK"); }
else sb.AppendLine("[1d] FootIK 已存在");

footIk.enableIk = true;
// 翻滚不做帧内贴地（身体本来就贴地滚），但基准偏移照旧生效
footIk.noGroundFixStates = new string[] { "Dash" };
footIk.groundClearance = 0.005f;   // 网格最低点与地面的目标间隙
footIk.probeUp = 0.5f;
footIk.probeDown = 1.5f;
footIk.liftSmooth = 14f;
footIk.maxLift = 0.8f;

// 每段动画的竖直基准偏移 = -(该段动画「无 IK 时脚底相对地面的最低点」)，见 Tools/reports/S2_pose_latest.txt。
// 背景：UAL1（待机/走/跑）与 UAL2（攻击）两套动画包的髋部基准高度不一致，
// 待机/走/跑/前两段攻击整体偏高 6~15cm（人飘在半空），后摇/收招整体偏低 12~30cm（脚埋进地板）。
// 偏高只能靠这里的静态偏移压回来 —— FootIK 的帧内修正只抬不压（压会粘住走路摆动相）。
// 偏低的三段（Atk1Rec/Atk2Rec/Atk3）偏移留 0，交给帧内动态抬升逐帧处理，比整体平移更贴合。
footIk.stateOffsets = new InkWash.Player.FootIK.StateYOffset[]
{
    new InkWash.Player.FootIK.StateYOffset { state = "Idle",     y = -0.092f },
    new InkWash.Player.FootIK.StateYOffset { state = "Walk",     y = -0.066f },
    new InkWash.Player.FootIK.StateYOffset { state = "Run",      y = -0.155f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk1",     y = -0.116f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk2",     y = -0.123f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk1Rec",  y =  0.000f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk2Rec",  y =  0.000f },
    new InkWash.Player.FootIK.StateYOffset { state = "Atk3",     y =  0.000f },
    // 翻滚：整段最低点在 -0.220 —— 不修就会铲进地板 22cm。抬到最低点刚好擦地最少必要量。
    new InkWash.Player.FootIK.StateYOffset { state = "Dash",     y =  0.220f },
};

int gLayer = LayerMask.NameToLayer("Ground");
int wLayer = LayerMask.NameToLayer("Wall");
int fmask = 0;
if (gLayer >= 0) fmask |= 1 << gLayer;
if (wLayer >= 0) fmask |= 1 << wLayer;
footIk.groundMask = fmask;
sb.AppendLine(string.Format("[1d] FootIK 地面遮罩 = Ground({0})|Wall({1}) → {2}，基准偏移表 {3} 条",
    gLayer, wLayer, footIk.groundMask.value, footIk.stateOffsets.Length));

if (visual != null)
    sb.AppendLine("[1d] Visual.localPosition = " + visual.localPosition.ToString("F4") + "（模型相对胶囊底的偏移）");

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
        tpc.collisionMask = fmask;
        tpc.distance = 4.3f;
        tpc.pivotOffset = new Vector3(0f, 1.42f, 0f);
        tpc.baseFov = 48f;
        sb.AppendLine(string.Format("[2] 相机避障遮罩 = Ground({0})|Wall({1}) → {2}",
            gLayer, wLayer, tpc.collisionMask.value));
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
        sb.AppendLine(string.Format("  PC: walk={0} run={1} dash={2}/{3}s refSpeed={4}/{5} motionMax={6}",
            spc.walkSpeed, spc.runSpeed, spc.dashSpeed, spc.dashDuration,
            spc.walkRefSpeed, spc.runRefSpeed, spc.motionSpeedMax));

    var sv = playerGo.GetComponent<InkWash.Effects.SwordVfx>();
    sb.AppendLine("  SwordVfx: " + (sv != null ? ("已挂，trailMinSpeed=" + sv.trailMinSpeed) : "缺失"));
    var sf = playerGo.GetComponent<InkWash.Player.FootIK>();
    sb.AppendLine("  FootIK: " + (sf != null
        ? ("已挂，mask=" + sf.groundMask.value + " groundClearance=" + sf.groundClearance
           + " 基准偏移 " + (sf.stateOffsets != null ? sf.stateOffsets.Length : 0) + " 条")
        : "缺失"));

    var an = playerGo.GetComponent<Animator>();
    if (an != null)
    {
        var ctrl = an.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        sb.AppendLine("  Animator: ctrl=" + (ctrl != null ? ctrl.name : "(无)")
            + "  层数=" + (ctrl != null ? ctrl.layers.Length : 0)
            + "  第0层IKPass=" + (ctrl != null && ctrl.layers.Length > 0 ? ctrl.layers[0].iKPass.ToString() : "-")
            + "  avatar=" + (an.avatar != null ? (an.avatar.name + "/isHuman=" + an.avatar.isHuman) : "(无)"));
    }

    var v = playerGo.transform.Find("Visual");
    sb.AppendLine("  Visual 上的组件: (应只有 Transform) "
        + (v != null ? string.Join(",", System.Array.ConvertAll(v.GetComponents<Component>(),
            c => c == null ? "<Missing>" : c.GetType().Name)) : "找不到 Visual"));
    if (v != null) sb.AppendLine("  Visual.localPosition = " + v.localPosition.ToString("F3"));
}

return sb.ToString();
