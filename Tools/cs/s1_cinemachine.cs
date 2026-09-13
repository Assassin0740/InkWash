// S1-6：Cinemachine 跟随相机
// 关键设计：相机「跟位置、不跟朝向」。
//   若相机随角色转向（BindingMode.LockToTarget*），而输入又是相机相对的，
//   就会形成"相机转 → 输入方向变 → 角色再转 → 相机再转"的正反馈，表现为原地打转。
//   所以用 WorldSpace 偏移：相机 Yaw 恒定，W 永远是"远离相机"。
var sb = new System.Text.StringBuilder();

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.name != "Main") { sb.AppendLine("!! 不在 Main 场景，中止"); return sb.ToString(); }

var player = UnityEngine.GameObject.Find("Player");
if (player == null) { sb.AppendLine("!! 找不到 Player"); return sb.ToString(); }
var camGo = UnityEngine.GameObject.Find("Main Camera");
if (camGo == null) { sb.AppendLine("!! 找不到 Main Camera"); return sb.ToString(); }

// ---------- 1) Brain 挂在主相机上 ----------
var brain = camGo.GetComponent<Cinemachine.CinemachineBrain>();
if (brain == null) brain = camGo.AddComponent<Cinemachine.CinemachineBrain>();
brain.m_UpdateMethod = Cinemachine.CinemachineBrain.UpdateMethod.SmartUpdate;
brain.m_BlendUpdateMethod = Cinemachine.CinemachineBrain.BrainUpdateMethod.LateUpdate;
brain.m_DefaultBlend = new Cinemachine.CinemachineBlendDefinition(
    Cinemachine.CinemachineBlendDefinition.Style.EaseInOut, 0.35f);
sb.AppendLine("CinemachineBrain: SmartUpdate / LateUpdate / EaseInOut 0.35s");

// ---------- 2) 虚拟相机 ----------
var vcamGo = UnityEngine.GameObject.Find("VCam_Follow");
if (vcamGo == null) vcamGo = new UnityEngine.GameObject("VCam_Follow");
var vcam = vcamGo.GetComponent<Cinemachine.CinemachineVirtualCamera>();
if (vcam == null) vcam = vcamGo.AddComponent<Cinemachine.CinemachineVirtualCamera>();

vcamGo.transform.position = new UnityEngine.Vector3(0f, 10f, -14f);
vcam.Priority = 10;
vcam.m_Follow = player.transform;
vcam.m_LookAt = player.transform;
// 关掉 Lens 的 FOV 覆盖，跟随主相机
vcam.m_Lens.FieldOfView = 45f;

// Body：世界空间固定偏移 —— 相机不随角色转动
var transposer = vcam.GetCinemachineComponent<Cinemachine.CinemachineTransposer>();
if (transposer == null) transposer = vcam.AddCinemachineComponent<Cinemachine.CinemachineTransposer>();
transposer.m_BindingMode = Cinemachine.CinemachineTransposer.BindingMode.WorldSpace;
transposer.m_FollowOffset = new UnityEngine.Vector3(0f, 10.5f, -11.5f);
transposer.m_XDamping = 0.55f;
transposer.m_YDamping = 0.55f;
transposer.m_ZDamping = 0.55f;
transposer.m_PitchDamping = 0f;
transposer.m_YawDamping = 0f;
transposer.m_RollDamping = 0f;
sb.AppendLine("Body: Transposer / WorldSpace / offset=" + transposer.m_FollowOffset.ToString("F1") + " / damping 0.55");

// Aim：瞄准胸口（不瞄脚底），阻尼一致避免"追不上"
var composer = vcam.GetCinemachineComponent<Cinemachine.CinemachineComposer>();
if (composer == null) composer = vcam.AddCinemachineComponent<Cinemachine.CinemachineComposer>();
composer.m_TrackedObjectOffset = new UnityEngine.Vector3(0f, 1.1f, 0f);
composer.m_HorizontalDamping = 0.55f;
composer.m_VerticalDamping = 0.55f;
composer.m_ScreenX = 0.5f;
composer.m_ScreenY = 0.55f;          // 角色略低于画面中心，前方视野更多
composer.m_DeadZoneWidth = 0.04f;
composer.m_DeadZoneHeight = 0.04f;
composer.m_SoftZoneWidth = 0.75f;
composer.m_SoftZoneHeight = 0.75f;
sb.AppendLine("Aim: Composer / 瞄准点 +1.1 (胸口) / ScreenY 0.55 / 阻尼 0.55");

// ---------- 3) 对齐初始画面（Edit 模式也能看到构图） ----------
vcamGo.transform.rotation = UnityEngine.Quaternion.Euler(41f, 0f, 0f);
vcamGo.transform.position = player.transform.position + new UnityEngine.Vector3(0f, 10.5f, -11.5f);

UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
UnityEditor.AssetDatabase.SaveAssets();

// ---------- 4) 复查 ----------
sb.AppendLine();
sb.AppendLine("=== 复查 ===");
sb.AppendLine("   VCam_Follow 位置: " + vcamGo.transform.position.ToString("F2"));
sb.AppendLine("   VCam_Follow 旋转: " + vcamGo.transform.rotation.eulerAngles.ToString("F1"));
sb.AppendLine("   Follow: " + (vcam.m_Follow != null ? vcam.m_Follow.name : "null"));
sb.AppendLine("   LookAt: " + (vcam.m_LookAt != null ? vcam.m_LookAt.name : "null"));
sb.AppendLine("   Brain 所在对象: " + camGo.name);
sb.AppendLine("   场景根对象: ");
foreach (var r in scene.GetRootGameObjects()) sb.AppendLine("       " + r.name);

return sb.ToString();
