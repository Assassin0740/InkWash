// v38b_audit2.cs —— 深挖：天空盒 / URP后处理配置 / URP-Lit归属 / 未被使用的InkWash shader
using System.Linq;
using UnityEngine;
using UnityEditor;

var sb = new System.Text.StringBuilder();

// ① RenderSettings 天空盒（反射兜底）
System.Type rsType = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    try { foreach (var t in a.GetTypes()) if (t.Name == "RenderSettings" && t.GetProperty("skybox") != null) { rsType = t; break; } }
    catch { }
    if (rsType != null) break;
}
if (rsType != null)
{
    var sky = rsType.GetProperty("skybox").GetValue(null) as Material;
    var fog = rsType.GetProperty("fog").GetValue(null);
    sb.AppendLine("RenderSettings.skybox = " + (sky != null ? sky.name + " (" + sky.shader.name + ")" : "null") + " fog=" + fog);
}
else sb.AppendLine("RenderSettings 反射未找到");

// ② URP Asset
var rp = UnityEngine.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
if (rp != null)
{
    sb.AppendLine("URP Asset: " + rp.name);
    sb.AppendLine("  postProcessing(全局) = " + rp.supportsHDR + " hdr=" + rp.supportsHDR + " msaa=" + rp.msaaSampleCount);
    var so = new SerializedObject(rp);
    var mPost = so.FindProperty("m_PostProcessingFeature");
    sb.AppendLine("  rendererList count = " + so.FindProperty("m_RendererDataList").arraySize);
}

// ③ URP/Lit 材质归属
var renderers = Object.FindObjectsOfType<Renderer>(true);
foreach (var r in renderers)
    foreach (var m in r.sharedMaterials)
        if (m != null && m.shader != null && m.shader.name.Contains("Lit"))
            sb.AppendLine("URP/Lit 材质 '" + m.name + "' @ " + PathOf(r.gameObject));

// ④ 全工程 InkWash shader 的被引用材质数
string[] guids = AssetDatabase.FindAssets("t:Material");
var shaderUse = new System.Collections.Generic.Dictionary<string, int>();
foreach (var g in guids)
{
    var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
    if (mat == null || mat.shader == null) continue;
    if (!mat.shader.name.StartsWith("InkWash")) continue;
    shaderUse[mat.shader.name + " | " + mat.name] = shaderUse.TryGetValue(mat.shader.name + " | " + mat.name, out var n) ? n + 1 : 1;
}
sb.AppendLine("== 工程内 InkWash 材质清单:");
foreach (var kv in shaderUse.OrderBy(k => k.Key))
    sb.AppendLine("  " + kv.Key + "  (被引用 " + kv.Value + " renderer 于当前场景)");

// ⑤ 场景使用的材质里哪些 shader 存在于工程但没被用
string[] shaderGuids = AssetDatabase.FindAssets("t:Shader", new string[] { "Assets/_Project/Art/Shaders", "Assets/_Project/Shaders" });
sb.AppendLine("== 工程 shader 资产:");
var usedShaders = new System.Collections.Generic.HashSet<string>();
foreach (var r in renderers) foreach (var m in r.sharedMaterials) if (m != null && m.shader != null) usedShaders.Add(m.shader.name);
foreach (var g in shaderGuids)
{
    var sh = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g));
    if (sh != null) sb.AppendLine("  " + sh.name + (usedShaders.Contains(sh.name) ? "  [场景在用]" : "  [未使用!]"));
}

Debug.Log("[v38b]\n" + sb.ToString());
return sb.ToString();

string PathOf(GameObject go)
{
    var path = go.name; var t = go.transform;
    while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
    return path;
}
