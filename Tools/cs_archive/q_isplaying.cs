// q_isplaying.cs —— 编辑态探针：Unity 是否停留在 Play 会话里（上次录屏脚本可能没退出）
using UnityEngine;
using UnityEditor;

var sb = new System.Text.StringBuilder();
sb.AppendLine("EditorApplication.isPlaying             = " + EditorApplication.isPlaying);
sb.AppendLine("isPlayingOrWillChangePlaymode           = " + EditorApplication.isPlayingOrWillChangePlaymode);
sb.AppendLine("isCompiling                             = " + EditorApplication.isCompiling);
sb.AppendLine("isUpdating                              = " + EditorApplication.isUpdating);

var go = GameObject.Find("Player");
sb.AppendLine("Play 场景里的 Player                    = " + (go != null ? ("找到  pos=" + go.transform.position) : "<null>"));

var cam = GameObject.Find("Main Camera");
sb.AppendLine("Main Camera                             = " + (cam != null ? "找到" : "<null>"));
if (cam != null)
{
    var rig = cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>();
    sb.AppendLine("  ThirdPersonCamera.enabled             = " + (rig != null ? rig.enabled.ToString() : "<组件缺失>"));
    if (rig != null)
    {
        sb.AppendLine("  mouseLookEnabled                      = " + rig.mouseLookEnabled);
        sb.AppendLine("  lockCursorOnStart                     = " + rig.lockCursorOnStart);
    }
}
return sb.ToString();
