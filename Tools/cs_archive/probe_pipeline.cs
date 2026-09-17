// 探针：查询渲染管线现状（S1 地基，必须先确认再动工）
var sb = new System.Text.StringBuilder();

sb.AppendLine("=== 渲染管线 ===");
var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
sb.AppendLine("currentRenderPipeline: " + (rp == null ? "NULL (Built-in!)" : rp.name + "  [" + rp.GetType().FullName + "]"));
var def = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
sb.AppendLine("defaultRenderPipeline: " + (def == null ? "NULL" : def.name));

var urp = rp as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
if (urp != null)
{
    sb.AppendLine("renderScale             : " + urp.renderScale);
    sb.AppendLine("msaaSampleCount         : " + urp.msaaSampleCount);
    sb.AppendLine("supportsHDR             : " + urp.supportsHDR);
    sb.AppendLine("shadowDistance          : " + urp.shadowDistance);
    sb.AppendLine("supportsCameraDepthTexture   : " + urp.supportsCameraDepthTexture);
    sb.AppendLine("supportsCameraOpaqueTexture  : " + urp.supportsCameraOpaqueTexture);
}
else
{
    sb.AppendLine("!! 当前不是 URP —— 水墨后处理将无法使用 ScriptableRendererFeature");
}

sb.AppendLine();
sb.AppendLine("=== 质量等级 -> 管线资产 ===");
int cur = UnityEngine.QualitySettings.GetQualityLevel();
string[] names = UnityEngine.QualitySettings.names;
for (int i = 0; i < names.Length; i++)
{
    var qs = UnityEngine.QualitySettings.GetRenderPipelineAssetAt(i);
    sb.AppendLine((i == cur ? " *[" : "  [") + i + "] " + names[i].PadRight(12) + " -> " + (qs == null ? "NULL" : qs.name));
}
sb.AppendLine("当前等级: [" + cur + "] " + UnityEngine.QualitySettings.names[cur]);
sb.AppendLine("antiAliasing=" + UnityEngine.QualitySettings.antiAliasing + "  vSync=" + UnityEngine.QualitySettings.vSyncCount + "  shadowDistance=" + UnityEngine.QualitySettings.shadowDistance);

sb.AppendLine();
sb.AppendLine("=== 色彩空间 / 目标平台 ===");
sb.AppendLine("colorSpace: " + UnityEditor.PlayerSettings.colorSpace);
sb.AppendLine("activeBuildTarget: " + UnityEditor.EditorUserBuildSettings.activeBuildTarget);

sb.AppendLine();
sb.AppendLine("=== 已有场景 ===");
var sceneGuids = UnityEditor.AssetDatabase.FindAssets("t:Scene");
if (sceneGuids.Length == 0) sb.AppendLine("   (无)");
foreach (var g in sceneGuids) sb.AppendLine("   " + UnityEditor.AssetDatabase.GUIDToAssetPath(g));

sb.AppendLine();
sb.AppendLine("=== 关键包版本 ===");
string[] want = { "com.unity.render-pipelines.universal", "com.unity.cinemachine", "com.unity.ai.navigation", "com.unity.textmeshpro", "com.unity.inputsystem", "com.unity.timeline", "com.unity.probuilder" };
var all = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
foreach (var w in want)
{
    bool hit = false;
    foreach (var p in all) { if (p.name == w) { sb.AppendLine("   " + p.name + " = " + p.version); hit = true; break; } }
    if (!hit) sb.AppendLine("   " + w + " = (未安装)");
}

sb.AppendLine();
sb.AppendLine("=== Tag / Layer 现状（建场景要用） ===");
sb.AppendLine("Tags: " + string.Join(", ", UnityEditorInternal.InternalEditorUtility.tags));
var layers = new System.Collections.Generic.List<string>();
for (int i = 0; i < 32; i++) { string ln = UnityEngine.LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(ln)) layers.Add(i + ":" + ln); }
sb.AppendLine("Layers: " + string.Join(", ", layers));

return sb.ToString();
