// e_probe.cs —— Sprint 3/4 起手体检（编辑态，只读）
//   ① KayKit Skeletons：骨架是否 Rig_Medium、是否 Humanoid、网格规模、材质
//   ② KayKit 动画库：可用片段清单（战斗近战/远程/移动/受击/死亡）
//   ③ URP：当前渲染管线与 Renderer 列表（为后面加 RendererFeature 定位）
//   ④ NavMesh：ai.navigation 是否可用、场景是否有 NavMeshSurface
//   ⑤ 场景白盒尺寸（给出生点/巡逻点定标）
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    sb.AppendLine("========== ① KayKit Skeletons 体检 ==========");
    string skelDir = "Assets/ThirdParty/KayKit/Skeletons/Characters";
    foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { skelDir }))
    {
        string p = AssetDatabase.GUIDToAssetPath(guid);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
        sb.AppendLine("--- " + Path.GetFileName(p));
        if (go == null) { sb.AppendLine("    (加载失败)"); continue; }
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.sharedMesh != null)
            sb.AppendLine(string.Format("    Mesh: {0}  verts={1}  tris={2}  bones={3}  bounds_h={4:F3}",
                smr.sharedMesh.name, smr.sharedMesh.vertexCount, smr.sharedMesh.triangles.Length / 3,
                smr.bones != null ? smr.bones.Length : 0, smr.sharedMesh.bounds.size.y));
        else sb.AppendLine("    (无 SkinnedMeshRenderer)");
        if (smr != null && smr.sharedMaterial != null)
            sb.AppendLine("    Material: " + smr.sharedMaterial.name + "  shader=" + smr.sharedMaterial.shader.name);
        // 骨骼根名
        var trs = go.GetComponentsInChildren<Transform>(true);
        sb.AppendLine("    节点总数=" + trs.Length + "  根名=" + go.transform.name +
                      "  首层子=" + string.Join(",", Enumerable.Range(0, Mathf.Min(4, go.transform.childCount)).Select(i => go.transform.GetChild(i).name)));
        var hips = trs.FirstOrDefault(t => t.name.ToLower().Contains("hips") || t.name.ToLower().Contains("root"));
        sb.AppendLine("    疑似脊椎/根骨 = " + (hips != null ? hips.name : "(无)"));
        // 内部动画片段
        var subs = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().ToList();
        sb.AppendLine("    内置片段=" + subs.Count);
        var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine("    内置 Avatar=" + (av != null ? av.name + " valid=" + av.isValid + " human=" + av.isHuman : "(无)"));
    }

    sb.AppendLine();
    sb.AppendLine("========== ② KayKit 动画库片段清单 ==========");
    string animDir = "Assets/ThirdParty/KayKit/Animations";
    foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { animDir }))
    {
        string p = AssetDatabase.GUIDToAssetPath(guid);
        var clips = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().ToList();
        if (clips.Count == 0) continue;
        sb.AppendLine("--- " + Path.GetFileName(p) + "  (" + clips.Count + " 段)");
        foreach (var c in clips.OrderBy(x => x.name))
            sb.AppendLine(string.Format("    {0,-34} len={1,6:F3}  loop={2,-5}  human={3}",
                c.name, c.length, c.isLooping, c.isHumanMotion));
    }

    sb.AppendLine();
    sb.AppendLine("========== ③ URP 管线 ==========");
    var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
    sb.AppendLine("  currentRenderPipeline = " + (rp != null ? rp.name + " (" + rp.GetType().FullName + ")" : "(null)"));
    var q = QualitySettings.GetQualityLevel();
    sb.AppendLine("  当前质量等级 = " + q + " / " + QualitySettings.names.Length);
    var urpAssetPath = AssetDatabase.GetAssetPath(rp);
    sb.AppendLine("  管线资产路径 = " + urpAssetPath);
    if (!string.IsNullOrEmpty(urpAssetPath))
    {
        var urpSo = new SerializedObject(rp);
        var listProp = urpSo.FindProperty("m_RendererDataList");
        if (listProp != null)
        {
            sb.AppendLine("  RendererDataList 长度 = " + listProp.arraySize);
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var ro = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                if (ro == null) { sb.AppendLine("    [" + i + "] (null)"); continue; }
                string rp2 = AssetDatabase.GetAssetPath(ro);
                sb.AppendLine("    [" + i + "] " + ro.name + "  path=" + rp2);
                var featProp = new SerializedObject(ro).FindProperty("m_RendererFeatures");
                if (featProp != null)
                {
                    sb.AppendLine("        已有 Feature 数 = " + featProp.arraySize);
                    for (int j = 0; j < featProp.arraySize; j++)
                    {
                        var f = featProp.GetArrayElementAtIndex(j).objectReferenceValue;
                        sb.AppendLine("          - " + (f != null ? f.GetType().Name + " [" + f.name + "]" : "(null)"));
                    }
                }
            }
        }
        // 每像素光照 / 阴影等关键项
        var p2 = urpSo.FindProperty("m_MSAA");
        sb.AppendLine("  MSAA = " + (p2 != null ? p2.intValue.ToString() : "?"));
        var rp3 = urpSo.FindProperty("m_SupportsHDR");
        sb.AppendLine("  HDR = " + (rp3 != null ? rp3.boolValue.ToString() : "?"));
        var depthP = urpSo.FindProperty("m_RequireDepthTexture");
        sb.AppendLine("  RequireDepthTexture = " + (depthP != null ? depthP.boolValue.ToString() : "?"));
        var opaP = urpSo.FindProperty("m_RequireOpaqueTexture");
        sb.AppendLine("  RequireOpaqueTexture = " + (opaP != null ? opaP.boolValue.ToString() : "?"));
    }

    sb.AppendLine();
    sb.AppendLine("========== ④ NavMesh 能力 ==========");
    var tNav = System.AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
        .Where(t => t.Name == "NavMeshSurface" || t.Name == "NavMeshAgent").Select(t => t.FullName).Distinct().ToList();
    sb.AppendLine("  可用类型: " + (tNav.Count > 0 ? string.Join(" | ", tNav) : "(未找到 —— ai.navigation 包可能没装)"));
    var surfaces = Object.FindObjectsOfType<MonoBehaviour>().Where(m => m != null && m.GetType().Name == "NavMeshSurface").ToList();
    sb.AppendLine("  场景中的 NavMeshSurface = " + surfaces.Count);
    var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
    sb.AppendLine("  现有 NavMesh：顶点 " + tri.vertices.Length + " / 三角 " + tri.indices.Length / 3);

    sb.AppendLine();
    sb.AppendLine("========== ⑤ 场景白盒尺寸 ==========");
    foreach (var nm in new[] { "Ground", "Arena", "Wall_N", "Wall_S", "Wall_E", "Wall_W" })
    {
        var g = GameObject.Find(nm);
        if (g == null) { sb.AppendLine("  " + nm + " (未找到)"); continue; }
        var r = g.GetComponent<Renderer>();
        var col = g.GetComponent<Collider>();
        sb.AppendLine(string.Format("  {0,-8} pos={1}  " + (r != null ? "size=" + r.bounds.size.ToString("F2") : (col != null ? "colSize=" + col.bounds.size.ToString("F2") : "")),
            nm, g.transform.position.ToString("F2"), r != null ? r.bounds.size.x : 0f, r != null ? r.bounds.size.y : 0f, r != null ? r.bounds.size.z : 0f));
    }
    var pil = GameObject.Find("Pillars");
    if (pil != null) sb.AppendLine("  Pillars 子数 = " + pil.transform.childCount);

    sb.AppendLine();
    sb.AppendLine("========== ⑥ 现有玩家可复用接口 ==========");
    var pcType = System.AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
        .FirstOrDefault(t => t.Name == "PlayerController" && t.Namespace == "InkWash.Player");
    if (pcType != null)
    {
        foreach (var m in pcType.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            if (m is System.Reflection.MethodInfo || m is System.Reflection.EventInfo || m is System.Reflection.PropertyInfo)
                sb.AppendLine("  " + m.MemberType + " " + m.Name);
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_probe.txt"), sb.ToString());
    Debug.Log("[e_probe] done");
    yield return null;
}

return Body();
