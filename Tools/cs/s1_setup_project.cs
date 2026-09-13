// S1-1：工程地基配置
//   a) 开启 URP 的深度纹理 / 不透明纹理（S4 屏幕空间水墨描边依赖它，现在关着）
//   b) 补齐分层（战斗判定要用 LayerMask）
var sb = new System.Text.StringBuilder();

// ---------- a) URP 资产：开启 Depth / Opaque Texture ----------
sb.AppendLine("=== A. URP 资产配置 ===");
string[] urpAssets = {
    "Assets/Settings/URP-HighFidelity.asset",
    "Assets/Settings/URP-Balanced.asset",
    "Assets/Settings/URP-Performant.asset"
};
foreach (var path in urpAssets)
{
    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
    if (asset == null) { sb.AppendLine("   " + path + "  -> 加载失败"); continue; }
    var so = new UnityEditor.SerializedObject(asset);

    // 先反射打印真实字段名，避免猜错
    string[] fields = { "m_RequireDepthTexture", "m_RequireOpaqueTexture" };
    foreach (var fn in fields)
    {
        var prop = so.FindProperty(fn);
        if (prop == null)
        {
            sb.AppendLine("   [" + System.IO.Path.GetFileName(path) + "] 字段 " + fn + " 不存在！实际候选：");
            var it = so.GetIterator();
            while (it.NextVisible(true))
                if (it.name.ToLowerInvariant().Contains("depth") || it.name.ToLowerInvariant().Contains("opaque"))
                    sb.AppendLine("        " + it.name + " = " + it.propertyType);
            continue;
        }
        bool before = prop.boolValue;
        prop.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        var check = new UnityEditor.SerializedObject(asset).FindProperty(fn);
        sb.AppendLine("   [" + System.IO.Path.GetFileName(path) + "] " + fn + ": " + before + " -> " + (check != null ? check.boolValue.ToString() : "?"));
    }
    UnityEditor.EditorUtility.SetDirty(asset);
}
UnityEditor.AssetDatabase.SaveAssets();

// ---------- b) 补齐 Layer ----------
sb.AppendLine();
sb.AppendLine("=== B. Layer 配置 ===");
var tagManager = new UnityEditor.SerializedObject(
    UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
var layersProp = tagManager.FindProperty("layers");

string[] want = { "Player", "Enemy", "Ground", "Wall", "Hitbox", "FX", "CameraZone" };
var existing = new System.Collections.Generic.List<string>();
for (int i = 0; i < layersProp.arraySize; i++)
    existing.Add(layersProp.GetArrayElementAtIndex(i).stringValue);

foreach (var w in want)
{
    if (existing.Contains(w)) { sb.AppendLine("   " + w + " 已存在 (index " + existing.IndexOf(w) + ")"); continue; }
    bool placed = false;
    for (int i = 8; i < layersProp.arraySize; i++)   // 0-7 是 Unity 保留层
    {
        var el = layersProp.GetArrayElementAtIndex(i);
        if (string.IsNullOrEmpty(el.stringValue))
        {
            el.stringValue = w;
            tagManager.ApplyModifiedProperties();
            sb.AppendLine("   " + w + " -> 新建于 index " + i);
            placed = true;
            break;
        }
    }
    if (!placed) sb.AppendLine("   " + w + " -> 无空位可用！");
}
UnityEditor.AssetDatabase.SaveAssets();

// ---------- 复查 ----------
sb.AppendLine();
sb.AppendLine("=== 复查：最终 Layer 表 ===");
var tm2 = new UnityEditor.SerializedObject(
    UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
var lp2 = tm2.FindProperty("layers");
for (int i = 0; i < lp2.arraySize; i++)
{
    string v = lp2.GetArrayElementAtIndex(i).stringValue;
    if (!string.IsNullOrEmpty(v)) sb.AppendLine("   " + i + ": " + v);
}

sb.AppendLine();
sb.AppendLine("=== 复查：渲染管线是否已生成深度纹理 ===");
var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
if (rp != null)
{
    sb.AppendLine("   supportsCameraDepthTexture  = " + rp.supportsCameraDepthTexture);
    sb.AppendLine("   supportsCameraOpaqueTexture = " + rp.supportsCameraOpaqueTexture);
}

return sb.ToString();
