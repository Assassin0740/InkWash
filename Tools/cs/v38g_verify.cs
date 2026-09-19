// v38g_verify.cs —— 第三十八轮验收：状态 + 截图
using System.Collections;
using UnityEngine;

IEnumerator Body()
{
    Application.runInBackground = true;
    yield return new WaitForSeconds(1.2f);

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("Registry: " + InkWash.Rendering.InkStyleRegistry.Describe());
    sb.AppendLine("Stage = " + InkWash.UI.InkStylePanel.CurrentStage);
    var vol = Object.FindObjectOfType<UnityEngine.Rendering.Volume>();
    sb.AppendLine("Volume = " + (vol != null ? vol.gameObject.name + " isGlobal=" + vol.isGlobal + " profile=" + (vol.sharedProfile != null ? vol.sharedProfile.name : "null") : "NULL"));
    var motes = Object.FindObjectOfType<InkWash.Effects.AmbientInkMotes>();
    sb.AppendLine("Motes = " + (motes != null ? "ok" : "NULL"));
    var cam = Camera.main;
    sb.AppendLine("CamPost = " + cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()?.renderPostProcessing);
    int pcount = 0;
    foreach (var ps in Object.FindObjectsOfType<ParticleSystem>())
        if (ps.particleCount > 0) pcount += ps.particleCount;
    sb.AppendLine("Alive particles = " + pcount);
    var panel = Object.FindObjectOfType<InkWash.UI.InkStylePanel>();
    sb.AppendLine("Panel visible = " + (panel != null ? panel.visible.ToString() : "null"));
    Debug.Log("[v38g]\n" + sb.ToString());

    ScreenCapture.CaptureScreenshot("Tools/screenshots/v38_after.png");
    yield return new WaitForSeconds(1.0f);
    yield break;
}

return Body();
