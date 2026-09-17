// q_shot_hand.cs —— 用与录屏相同的相机机位，拍几张「攻击中的手」剧照（直接抓 Camera.main）。
// 顺带确认 1.8 m 近景的取景是否把两只手都框进去。
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string imgDir = System.IO.Path.Combine(projRoot, "Tools/screenshots/hand_demo_shot");
    System.IO.Directory.CreateDirectory(imgDir);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var rig = Camera.main.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
    var cam = Camera.main;

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    rig.SetMouseLookEnabled(false);
    rig.SnapBehindTarget();
    rig.yaw += 150f;
    rig.pitch = 4f;
    rig.distance = 1.8f;
    for (int i = 0; i < 45; i++) yield return null;

    string[] tags = { "atk1_early", "atk1_mid", "atk1_late", "atk2_mid" };
    int ti = 0;
    ctl.RequestInjectedAttack();
    for (int i = 0; i < 130; i++)
    {
        if (i == 10 || i == 22 || i == 34) { yield return Snap(cam, imgDir, tags[ti]); ti++; }
        if (i == 55) ctl.RequestInjectedAttack();
        yield return null;
    }
    yield return Snap(cam, imgDir, tags[3]);

    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    if (pose != null)
        sb.AppendLine("握拳度 右=" + pose.RightFistRatio.ToString("F2") + " 左=" + pose.LeftFistRatio.ToString("F2"));
    sb.AppendLine("相机距离 = " + rig.CurrentDistance.ToString("F2") + "m，FOV = " + cam.fieldOfView.ToString("F0") + "°");

    ctl.EndInputOverride();
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_shot_hand.txt"), sb.ToString());
    Debug.Log("[q_shot_hand] done");
    yield return null;
}

// 直接抓主相机这一帧（截图接口在本工程被禁用，所以走 RenderTexture）
IEnumerator Snap(Camera cam, string dir, string tag)
{
    var rt = RenderTexture.GetTemporary(640, 360, 24);
    cam.targetTexture = rt;
    cam.Render();
    cam.targetTexture = null;
    var prev = RenderTexture.active;
    RenderTexture.active = rt;
    var tex = new Texture2D(640, 360, TextureFormat.RGB24, false);
    tex.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
    tex.Apply();
    RenderTexture.active = prev;
    RenderTexture.ReleaseTemporary(rt);
    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, tag + ".png"), tex.EncodeToPNG());
    Object.Destroy(tex);
    yield return null;
}

return Body();
