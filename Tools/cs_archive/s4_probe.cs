// s4_probe.cs —— Sprint 4（水墨风）开工前的体检：
// 角色材质/贴图现状、URP 渲染器资产与已有 Feature、深度/法线可用性、色彩空间。
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    sb.AppendLine("===== S4 开工体检 =====");
    sb.AppendLine("色彩空间 = " + PlayerSettings.colorSpace);
    sb.AppendLine("当前构建目标 = " + EditorUserBuildSettings.activeBuildTarget);
    sb.AppendLine("图形 API = " + string.Join(", ", PlayerSettings.GetGraphicsAPIs(EditorUserBuildSettings.activeBuildTarget)));

    // ---- URP 管线资产 ----
    var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
    sb.AppendLine();
    sb.AppendLine("URP 资产 = " + (rp != null ? rp.name : "**不是 URP**"));
    if (rp != null)
    {
        sb.AppendLine("  msaa=" + rp.msaaSampleCount + "  hdr=" + rp.supportsHDR
                      + "  shadowDist=" + rp.shadowDistance
                      + "  renderScale=" + rp.renderScale);
        sb.AppendLine("  depthTexture=" + rp.supportsCameraDepthTexture
                      + "  opaqueTexture=" + rp.supportsCameraOpaqueTexture);
        sb.AppendLine("  rendererData 类型 = " + rp.scriptableRenderer.GetType().Name);
    }

    // ---- 渲染器资产里的 Feature ----
    sb.AppendLine();
    string[] rendererGuids = AssetDatabase.FindAssets("t:UniversalRendererData");
    sb.AppendLine("找到 " + rendererGuids.Length + " 个 UniversalRendererData：");
    foreach (var g in rendererGuids)
    {
        string p = AssetDatabase.GUIDToAssetPath(g);
        sb.AppendLine("  " + p);
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(p);
        if (data == null) continue;
        var so = new SerializedObject(data);
        var feats = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");
        if (feats != null)
        {
            sb.AppendLine("    已装配 Feature " + feats.arraySize + " 个（map=" + (map != null ? map.arraySize : -1) + "）：");
            for (int i = 0; i < feats.arraySize; i++)
            {
                var f = feats.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                sb.AppendLine("      [" + i + "] " + (f != null ? f.name + "  (" + f.GetType().Name + ")  active=" + f.isActive : "<null>"));
            }
        }
        var dn = so.FindProperty("m_DepthPrimingMode");
        if (dn != null) sb.AppendLine("    m_DepthPrimingMode = " + dn.enumValueIndex);
    }

    // ---- 角色渲染器与材质 ----
    sb.AppendLine();
    sb.AppendLine("---- 角色材质 ----");
    var player = GameObject.Find("Player");
    if (player != null)
    {
        foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            sb.AppendLine("  " + smr.name + "  sharedMesh=" + (smr.sharedMesh != null ? smr.sharedMesh.name : "-")
                          + "  子网格 " + smr.sharedMaterials.Length);
            foreach (var m in smr.sharedMaterials)
            {
                if (m == null) { sb.AppendLine("     <null>"); continue; }
                sb.AppendLine("     " + m.name + "   shader=" + m.shader.name
                              + "   path=" + AssetDatabase.GetAssetPath(m));
                var bs = m.FindPass("UniversalForward");
                sb.AppendLine("        pass(UniversalForward) = " + bs);
                foreach (var pn in new[] { "_BaseMap", "_BaseColor", "_Metallic", "_Smoothness" })
                    if (m.HasProperty(pn))
                    {
                        var t = m.GetTexture(pn);
                        sb.AppendLine("        " + pn + " = " + (t != null ? t.name + "  " + t.width + "x" + t.height + "  sRGB=" + t.isDataSRGB : m.GetColor(pn).ToString()));
                    }
            }
        }
        // 角色渲染层
        sb.AppendLine("  Player layer = " + player.layer + " (" + LayerMask.LayerToName(player.layer) + ")");
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
            if (t.gameObject.layer != player.layer)
                sb.AppendLine("    子节点 " + t.name + " 用了不同 layer = " + t.gameObject.layer + " (" + LayerMask.LayerToName(t.gameObject.layer) + ")");
    }
    else sb.AppendLine("  找不到 Player");

    // ---- 已有水墨相关资产 ----
    sb.AppendLine();
    sb.AppendLine("---- 已有候选资产 ----");
    foreach (var pat in new[] { "t:Shader InkWash", "t:Shader Ink", "t:Shader Toon", "t:Texture2D Xuan", "t:Texture2D Paper" })
        sb.AppendLine("  " + pat + " → " + AssetDatabase.FindAssets(pat).Length + " 个");

    // ---- 贴图资产清单（Art 目录）----
    sb.AppendLine();
    sb.AppendLine("---- Art/Textures 现状 ----");
    string artDir = "Assets/_Project/Art";
    if (AssetDatabase.IsValidFolder(artDir))
        foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { artDir }))
            sb.AppendLine("  " + AssetDatabase.GUIDToAssetPath(g));

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s4_probe.txt"), sb.ToString());
    Debug.Log("[s4_probe] done");
    yield return null;
}
return Body();
