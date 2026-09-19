// v38_shot2.cs —— 运行态截图：等文件落盘为止
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    Application.runInBackground = true;
    yield return new WaitForSeconds(1.0f);
    ScreenCapture.CaptureScreenshot("Tools/screenshots/v38_gameview.png");
    // 截图在帧末写入，等文件出现
    int guard = 0;
    while (!System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath, "..", "Tools/screenshots/v38_gameview.png")) && guard++ < 120)
        yield return null;
    yield return new WaitForSeconds(0.2f);
    var p = System.IO.Path.Combine(Application.dataPath, "..", "Tools/screenshots/v38_gameview.png");
    Debug.Log("[v38shot] exists=" + System.IO.File.Exists(p) + " size=" + (System.IO.File.Exists(p) ? new System.IO.FileInfo(p).Length : 0));
    yield break;
}

return Body();
