// 把自研第三人称相机接到主相机上，并停用原先的 Cinemachine 相机与 Brain（保留对象便于回退）。
var sb = new System.Text.StringBuilder();

var camGo = UnityEngine.GameObject.Find("Main Camera");
if (camGo == null) { sb.AppendLine("找不到 Main Camera"); return sb.ToString(); }
var cam = camGo.GetComponent<UnityEngine.Camera>();
sb.AppendLine("主相机: " + camGo.name + "  原 FOV=" + cam.fieldOfView);

// ---------- 1. 停用 Cinemachine ----------
var brain = camGo.GetComponent<Cinemachine.CinemachineBrain>();
if (brain != null)
{
    brain.enabled = false;
    sb.AppendLine("已停用 CinemachineBrain（保留组件）");
}

foreach (var vc in UnityEngine.Object.FindObjectsOfType<Cinemachine.CinemachineVirtualCamera>(true))
{
    if (vc.gameObject.activeSelf)
    {
        vc.gameObject.SetActive(false);
        sb.AppendLine("已停用虚拟相机 GameObject: " + vc.name);
    }
}

// ---------- 2. 挂自研相机 ----------
var tpc = camGo.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
if (tpc == null)
{
    tpc = camGo.AddComponent<InkWash.CameraRig.ThirdPersonCamera>();
    sb.AppendLine("已挂载 ThirdPersonCamera");
}
else sb.AppendLine("ThirdPersonCamera 已存在，更新参数");

var player = UnityEngine.GameObject.Find("Player");
if (player != null) tpc.target = player.transform;
sb.AppendLine("跟随目标: " + (tpc.target == null ? "(null)" : tpc.target.name));

// 近距离第三人称参数
tpc.pivotOffset = new UnityEngine.Vector3(0f, 1.42f, 0f);
tpc.distance = 4.3f;
tpc.yaw = 0f;
tpc.pitch = 14f;
tpc.pitchMin = -22f;
tpc.pitchMax = 55f;
tpc.yawSensitivity = 3.2f;
tpc.pitchSensitivity = 2.0f;
tpc.followDamp = 0.10f;
tpc.leadCompensation = 0.10f;
tpc.baseFov = 48f;
tpc.fovSpeedGain = 8f;
tpc.fovSpeedMax = 12f;
tpc.fovDamp = 0.15f;
tpc.collisionRadius = 0.28f;
tpc.minDistance = 1.2f;
tpc.lockCursorOnStart = true;

// 避障只挡墙体（Layer 11 = Wall）
int wallLayer = UnityEngine.LayerMask.NameToLayer("Wall");
if (wallLayer >= 0)
{
    tpc.collisionMask = 1 << wallLayer;
    sb.AppendLine("避障层: Wall(" + wallLayer + ")");
}
else sb.AppendLine("[警告] 未找到 Wall 层，避障关闭");

cam.fieldOfView = tpc.baseFov;
cam.nearClipPlane = 0.12f;

UnityEditor.EditorUtility.SetDirty(tpc);
UnityEditor.EditorUtility.SetDirty(camGo);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
sb.AppendLine("场景已保存");

// ---------- 3. 复核结果 ----------
sb.AppendLine();
sb.AppendLine("=== 复核 ===");
sb.AppendLine("  相机 FOV=" + cam.fieldOfView + "  近裁=" + cam.nearClipPlane);
sb.AppendLine("  距离=" + tpc.distance + "m  枢轴高度=" + tpc.pivotOffset.y + "  俯仰=" + tpc.pitch + "°");
sb.AppendLine("  预期相机位置≈ 目标 + (0, " + (tpc.pivotOffset.y + tpc.distance * Mathf.Sin(tpc.pitch * Mathf.Deg2Rad)).ToString("F2")
    + ", " + (-tpc.distance * Mathf.Cos(tpc.pitch * Mathf.Deg2Rad)).ToString("F2") + ")");
sb.AppendLine("  CinemachineBrain 启用=" + (brain != null && brain.enabled));
int activeVc = 0;
foreach (var vc in UnityEngine.Object.FindObjectsOfType<Cinemachine.CinemachineVirtualCamera>(true))
    if (vc.gameObject.activeSelf) activeVc++;
sb.AppendLine("  仍在启用的虚拟相机数=" + activeVc);

return sb.ToString();
