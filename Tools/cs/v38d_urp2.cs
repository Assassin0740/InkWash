// v38d_urp2.cs —— 用 boolValue 正确读取 feature isActive
using UnityEngine;
using UnityEditor;

var sb = new System.Text.StringBuilder();
var rp = UnityEngine.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
var so = new SerializedObject(rp);
var rdRef = so.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue;
var rdSo = new SerializedObject(rdRef);
var feats = rdSo.FindProperty("m_RendererFeatures");
var list = rdSo.FindProperty("m_RendererFeaturesList");
sb.AppendLine("features x" + feats.arraySize + " list x" + list.arraySize);
for (int j = 0; j < feats.arraySize; j++)
{
    var f = feats.GetArrayElementAtIndex(j).objectReferenceValue;
    bool isActive = list.GetArrayElementAtIndex(j).FindPropertyRelative("isActive").boolValue;
    if (f == null) { sb.AppendLine("[" + j + "] NULL active=" + isActive); continue; }
    var fso = new SerializedObject(f);
    sb.AppendLine("[" + j + "] " + f.GetType().Name + " '" + f.name + "' isActive=" + isActive
        + " m_Enabled=" + fso.FindProperty("m_Enabled").boolValue);
}
Debug.Log("[v38d]\n" + sb.ToString());
return sb.ToString();
