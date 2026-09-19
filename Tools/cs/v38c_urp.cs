// v38c_urp.cs —— 检查 URP RendererData 上挂载的 RendererFeature 状态
using System.Linq;
using UnityEngine;
using UnityEditor;

var sb = new System.Text.StringBuilder();
var rp = UnityEngine.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
if (rp == null) { return "no URP"; }
var so = new SerializedObject(rp);
var rdList = so.FindProperty("m_RendererDataList");
for (int i = 0; i < rdList.arraySize; i++)
{
    var rdRef = rdList.GetArrayElementAtIndex(i).objectReferenceValue;
    if (rdRef == null) { sb.AppendLine("[" + i + "] null"); continue; }
    sb.AppendLine("[" + i + "] RendererData: " + rdRef.name + " (" + rdRef.GetType().Name + ")  path=" + AssetDatabase.GetAssetPath(rdRef));
    var rdSo = new SerializedObject(rdRef);
    var feats = rdSo.FindProperty("m_RendererFeatures");
    sb.AppendLine("    rendererFeatures x" + feats.arraySize);
    for (int j = 0; j < feats.arraySize; j++)
    {
        var f = feats.GetArrayElementAtIndex(j).objectReferenceValue;
        var en = rdSo.FindProperty("m_RendererFeaturesList")?.GetArrayElementAtIndex(j)?.FindPropertyRelative("isActive")?.boolValue;
        if (f == null) { sb.AppendLine("      [" + j + "] NULL"); continue; }
        sb.AppendLine("      [" + j + "] " + f.GetType().Name + " '" + f.name + "' active=" + en + " path=" + AssetDatabase.GetAssetPath(f));
        var fso = new SerializedObject(f);
        var it = fso.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.depth > 1) continue;
            if (it.propertyType == SerializedPropertyType.Float || it.propertyType == SerializedPropertyType.Integer || it.propertyType == SerializedPropertyType.Boolean)
                sb.AppendLine("          " + it.propertyPath + " = " + it.floatValue);
            else if (it.propertyType == SerializedPropertyType.ObjectReference && it.propertyPath.Contains("aterial"))
                sb.AppendLine("          " + it.propertyPath + " = " + (it.objectReferenceValue != null ? it.objectReferenceValue.name : "NULL"));
        }
    }
    var post = rdSo.FindProperty("m_PostProcessData");
    if (post != null) sb.AppendLine("    postProcessData = " + (post.objectReferenceValue != null ? post.objectReferenceValue.name : "NULL"));
}
Debug.Log("[v38c]\n" + sb.ToString());
return sb.ToString();
