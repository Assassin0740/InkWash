// a60_ground_check.cs —— 录一段地面，确认 z-fighting 已消失
//
// a59 已经删掉了 A48_Ground（与 Environment/Ground 顶面 100% 共面的那个），
// 静态扫描 "共面对 = 0"。但 z-fighting 是**逐帧抖动**、静态单帧看不出来，
// 所以必须用录像来验：让相机贴地平扫，地面若有闪烁会非常明显。
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);

// 只留场景自带的东西；清掉一切临时物
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g != null && System.Text.RegularExpressions.Regex.IsMatch(g.name, @"^A\d+_"))
        UnityEngine.Object.DestroyImmediate(g);

var sb = new StringBuilder();
sb.AppendLine("========== a60 地面平扫验证 ==========");

var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindObjectOfType<Camera>();
if (cam != null)
{
    // 移除一切会抢相机的组件（CinemachineBrain 是 a54~a57 构图全废的真凶）
    foreach (var mb in cam.GetComponents<MonoBehaviour>())
    {
        if (mb == null) continue;
        string tn = mb.GetType().Name;
        if (tn.Contains("ShowcaseCam") || tn.Contains("Brain") || tn.Contains("Follow"))
        { UnityEngine.Object.DestroyImmediate(mb); sb.AppendLine("移除 " + tn); }
    }
    // 贴地平扫机位：1.2 m 高、朝 -Z 方向看，从场地北侧往南推进
    cam.transform.position = new Vector3(-6f, 1.2f, 12f);
    cam.transform.LookAt(new Vector3(6f, 0.4f, -8f));
    cam.fieldOfView = 55f;
    sb.AppendLine("相机 = " + cam.transform.position.ToString("F2") + " → lookAt (6, 0.4, -8)  fov=55");
}

// 让玩家站到镜头前，作为"引擎在跑"的证据（不然画面静止说不清是卡住了还是没闪）
GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player != null)
{
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    player.transform.position = new Vector3(0f, 0.3f, 2f);
    if (cc != null) cc.enabled = true;
    sb.AppendLine("玩家 = " + player.transform.position.ToString("F2"));
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a60_ground_check.txt"), sb.ToString());
Debug.Log("[a60]\n" + sb.ToString());
