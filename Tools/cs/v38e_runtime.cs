// v38e_runtime.cs —— 运行时水墨三件套状态
using UnityEngine;

var sb = new System.Text.StringBuilder();
sb.AppendLine("InkStylePanel.CurrentStage = " + InkWash.UI.InkStylePanel.CurrentStage);
sb.AppendLine("RegisteredCount = " + InkWash.UI.InkStylePanel.RegisteredCount);
sb.AppendLine("InkStyleRegistry: " + InkWash.Rendering.InkStyleRegistry.Describe());
sb.AppendLine("AllRegistered = " + InkWash.Rendering.InkStyleRegistry.AllRegistered);
var ps = Object.FindObjectsOfType<ParticleSystem>(true);
sb.AppendLine("Play 中 ParticleSystem x" + ps.Length);
var vols = Object.FindObjectsOfType<UnityEngine.Rendering.Volume>(true);
sb.AppendLine("Play 中 Volume x" + vols.Length);
var cam = Camera.main;
if (cam != null)
    sb.AppendLine("Camera postEnabled=" + cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()?.renderPostProcessing);
var panel = Object.FindObjectOfType<InkWash.UI.InkStylePanel>();
sb.AppendLine("Panel 实例 @ " + (panel != null ? panel.gameObject.name : "null") + " stage=" + (panel != null ? panel.Stage.ToString() : "-"));
Debug.Log("[v38e]\n" + sb.ToString());
return sb.ToString();
