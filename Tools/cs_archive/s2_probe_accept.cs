// S2 实机验收前的环境体检：一次跑完，把"验收要用到的前提"全部打印出来。
// 只要这份输出对得上，后面的 Play 验收报告才有意义。
var sb = new System.Text.StringBuilder();

// ---------- 1) 层号（相机避障遮罩依赖它） ----------
sb.AppendLine("=== 1) 层号 ===");
foreach (var name in new string[] { "Default", "Ground", "Wall", "Player" })
{
    int ly = UnityEngine.LayerMask.NameToLayer(name);
    sb.AppendLine("  " + name + " = " + ly + (ly >= 0 ? "  bit=" + (1 << ly) : "  (未定义)"));
}

// ---------- 2) Animator 层与参数 ----------
sb.AppendLine();
sb.AppendLine("=== 2) Player.controller ===");
var playerGo = UnityEngine.GameObject.Find("Player");
if (playerGo == null) { sb.AppendLine("  !! 场景里找不到 Player"); }
else
{
    var anim = playerGo.GetComponentInChildren<UnityEngine.Animator>();
    if (anim == null) sb.AppendLine("  !! 找不到 Animator");
    else
    {
        var ac = anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        sb.AppendLine("  Animator 所在节点: " + anim.gameObject.name);
        sb.AppendLine("  控制器: " + (ac != null ? ac.name : "(null)"));
        if (ac != null)
        {
            for (int i = 0; i < ac.layers.Length; i++)
            {
                var L = ac.layers[i];
                sb.AppendLine(string.Format("  层[{0}] {1}  权重={2}  遮罩={3}  模式={4}",
                    i, L.name, L.defaultWeight,
                    L.avatarMask == null ? "(无)" : L.avatarMask.name, L.blendingMode));
            }
            sb.AppendLine("  参数: " + string.Join(", ", System.Array.ConvertAll(
                ac.parameters, p => p.name + ":" + p.type)));

            // 上半身遮罩到底开了哪些部件 —— 直接决定"抱空气"有没有被盖住
            var m = ac.layers.Length > 1 ? ac.layers[1].avatarMask : null;
            if (m != null)
            {
                var on = new System.Collections.Generic.List<string>();
                var off = new System.Collections.Generic.List<string>();
                for (int i = 0; i < (int)UnityEngine.AvatarMaskBodyPart.LastBodyPart; i++)
                    (m.GetHumanoidBodyPartActive((UnityEngine.AvatarMaskBodyPart)i) ? on : off)
                        .Add(((UnityEngine.AvatarMaskBodyPart)i).ToString());
                sb.AppendLine("  上半身遮罩 开: " + string.Join(",", on.ToArray()));
                sb.AppendLine("  上半身遮罩 关: " + string.Join(",", off.ToArray()));
            }
        }
    }
}

// ---------- 3) 场景里的玩家组件与刀光探针 ----------
sb.AppendLine();
sb.AppendLine("=== 3) 玩家组件 ===");
if (playerGo != null)
{
    var pc = playerGo.GetComponent<InkWash.Player.PlayerController>();
    sb.AppendLine("  PlayerController: " + (pc != null ? "已挂" : "缺失"));
    if (pc != null)
        sb.AppendLine(string.Format("    walk={0} run={1} accel={2} decel={3} | 参考速度 走={4} 跑={5} 播放速度[{6}~{7}]"
            + " | dash={8}/{9}s 冷却={10} | 连击位移=[{11}] 时长=[{12}] 取消起点=[{13}]",
            pc.walkSpeed, pc.runSpeed, pc.acceleration, pc.deceleration,
            pc.walkRefSpeed, pc.runRefSpeed, pc.motionSpeedMin, pc.motionSpeedMax,
            pc.dashSpeed, pc.dashDuration, pc.dashCooldown,
            string.Join(",", System.Array.ConvertAll(pc.comboLungeDistance, v => v.ToString("0.##"))),
            string.Join(",", System.Array.ConvertAll(pc.comboLungeDuration, v => v.ToString("0.##"))),
            string.Join(",", System.Array.ConvertAll(pc.comboRecCancelStart, v => v.ToString("0.##")))));

    var vfx = playerGo.GetComponent<InkWash.Effects.SwordVfx>();
    sb.AppendLine("  SwordVfx: " + (vfx != null ? "已挂" : "缺失"));
    if (vfx != null)
    {
        sb.AppendLine("    inkShader=" + (vfx.inkShader != null ? vfx.inkShader.name : "(null，将用 Shader.Find)")
            + "  placeholderBlade=" + vfx.placeholderBlade + "  震屏=" + vfx.enableShake);
        // 编辑模式下 Awake 不跑，所以运行时对象要现场构造一次才能验证
        sb.AppendLine("    [编辑模式] bladeAnchor=" + (vfx.bladeAnchor != null ? vfx.bladeAnchor.name : "(待运行时解析)"));
    }

    var ad = playerGo.GetComponent<InkWash.Core.AudioDirector>();
    sb.AppendLine("  AudioDirector: " + (ad != null ? "已挂" : "缺失"));

    var vis = playerGo.transform.Find("Visual");
    sb.AppendLine("  Visual 上组件: " + (vis != null
        ? string.Join(",", System.Array.ConvertAll(vis.GetComponents<UnityEngine.Component>(),
            c => c == null ? "<Missing>" : c.GetType().Name))
        : "找不到 Visual"));

    var cc = playerGo.GetComponent<UnityEngine.CharacterController>();
    sb.AppendLine("  CharacterController: " + (cc != null
        ? "半径=" + cc.radius + " 高=" + cc.height + " 中心Y=" + cc.center.y : "缺失"));
}

// ---------- 4) 相机 ----------
sb.AppendLine();
sb.AppendLine("=== 4) 相机 ===");
var cam = UnityEngine.Camera.main;
if (cam == null) sb.AppendLine("  !! 没有 Main Camera");
else
{
    sb.AppendLine("  " + cam.name + "  FOV=" + cam.fieldOfView + "  近裁=" + cam.nearClipPlane);
    var tpc = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
    if (tpc == null) sb.AppendLine("  !! 未挂 ThirdPersonCamera");
    else
        sb.AppendLine(string.Format("  distance={0}  枢轴Y={1}  yaw={2}  pitch={3}  基准FOV={4}  避障遮罩={5}",
            tpc.distance, tpc.pivotOffset.y, tpc.yaw, tpc.pitch, tpc.baseFov, tpc.collisionMask.value));
}

// ---------- 5) UAL2 里到底有哪些"移动/攻击"片段 ----------
// 「脚跟不上」的根因判断：如果存在正经的 Walk/Run 循环，就不该用 Walk_Carry 去顶跑速。
sb.AppendLine();
sb.AppendLine("=== 5) UAL2 可用片段（含 Walk/Run/Jog/Slide/Sword） ===");
const string FbxPath = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx";
int total = 0;
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(FbxPath))
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null || c.name.StartsWith("__preview__")) continue;
    total++;
    string n = c.name;
    if (n.Contains("Walk") || n.Contains("Run") || n.Contains("Jog") || n.Contains("Sprint")
        || n.Contains("Slide") || n.Contains("Sword") || n.Contains("Idle"))
        sb.AppendLine(string.Format("  {0,-42} 时长={1:F2}s  循环={2}", n, c.length, c.isLooping));
}
sb.AppendLine("  （片段总数 " + total + "）");

// ---------- 6) 音频资源 ----------
sb.AppendLine();
sb.AppendLine("=== 6) 音频 Resources ===");
foreach (var n in new string[] {
    "Audio/Resources/BGM/et11lx-chinese-ancient-style-music-love-etlx-247345",
    "Audio/Resources/SFX/sfx_swing_a", "Audio/Resources/SFX/sfx_swing_b",
    "Audio/Resources/SFX/sfx_swing_heavy", "Audio/Resources/SFX/sfx_dash",
    "Audio/Resources/SFX/sfx_draw", "Audio/Resources/SFX/sfx_hit_blade",
    "Audio/Resources/SFX/sfx_hit_flesh", "Audio/Resources/SFX/sfx_footstep",
    "Audio/Resources/SFX/sfx_ui_click" })
{
    var clip = UnityEngine.Resources.Load<UnityEngine.AudioClip>(n);
    sb.AppendLine("  " + (clip != null ? "[OK] " : "[缺失] ") + n
        + (clip != null ? "  " + clip.length.ToString("F2") + "s" : ""));
}

return sb.ToString();
