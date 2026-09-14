// Char_Feng 素材体检（第二版）：完整报告写盘 + 实际世界身高 + 内嵌动画明细
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    sb.AppendLine("========== Char_Feng 素材体检 ==========");

    string fbxPath = "Assets/Char_Feng/Fbx/Feng.fbx";
    var objs = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
    sb.AppendLine("FBX 子资源数 = " + objs.Length);

    GameObject prefabGO = null;
    var meshes = new List<Mesh>();
    var avatars = new List<Avatar>();
    var mats = new List<Material>();
    var clips = new List<AnimationClip>();
    foreach (var o in objs)
    {
        if (o is GameObject g && prefabGO == null) prefabGO = g;
        else if (o is Mesh m) meshes.Add(m);
        else if (o is Avatar a) avatars.Add(a);
        else if (o is Material mt) mats.Add(mt);
        else if (o is AnimationClip c) clips.Add(c);
    }
    sb.AppendLine(string.Format("  GameObject={0} Mesh={1} Avatar={2} Material={3} 内嵌Clip={4}",
        prefabGO != null, meshes.Count, avatars.Count, mats.Count, clips.Count));

    int totalTris = 0, totalVerts = 0;
    foreach (var m in meshes)
    {
        int t = 0;
        for (int i = 0; i < m.subMeshCount; i++) t += (int)(m.GetIndexCount(i) / 3);
        totalTris += t; totalVerts += m.vertexCount;
        sb.AppendLine(string.Format("  Mesh '{0}': 顶点 {1} 三角面 {2} 子网格 {3} 包围盒高 {4:F4} 约束={5}",
            m.name, m.vertexCount, t, m.subMeshCount, m.bounds.size.y, m.isReadable));
    }
    sb.AppendLine(string.Format("合计：顶点 {0} 三角面 {1}", totalVerts, totalTris));

    foreach (var mt in mats)
        sb.AppendLine(string.Format("  Material '{0}' shader={1}", mt.name, mt.shader != null ? mt.shader.name : "NULL"));

    foreach (var a in avatars)
        sb.AppendLine(string.Format("  Avatar '{0}' isValid={1} isHuman={2} 已映射骨={3} skeleton={4}",
            a.name, a.isValid, a.isHuman, a.humanDescription.human.Length, a.humanDescription.skeleton.Length));

    foreach (var c in clips)
        sb.AppendLine(string.Format("  [FBX内嵌] '{0}' 长度 {1:F3}s 循环 {2} isHumanMotion={3} 曲线 {4}",
            c.name, c.length, c.isLooping, c.isHumanMotion, AnimationUtility.GetCurveBindings(c).Length));

    sb.AppendLine("---------- Animation/ 下 .anim ----------");
    foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Char_Feng/Animation" }))
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g));
        if (c == null) continue;
        sb.AppendLine(string.Format("  {0,-12} 长度 {1,7:F3}s 循环 {2,-6} isHumanMotion={3,-6} 曲线 {4}",
            c.name, c.length, c.isLooping, c.isHumanMotion, AnimationUtility.GetCurveBindings(c).Length));
    }

    foreach (var g in AssetDatabase.FindAssets("t:Texture", new[] { "Assets/Char_Feng" }))
    {
        string p = AssetDatabase.GUIDToAssetPath(g);
        var ti = AssetImporter.GetAtPath(p) as TextureImporter;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        sb.AppendLine(string.Format("  贴图 {0} {1}x{2} sRGB={3} 类型={4} mipmap={5} maxSize={6}",
            System.IO.Path.GetFileName(p), tex != null ? tex.width : 0, tex != null ? tex.height : 0,
            ti != null ? ti.sRGBTexture.ToString() : "?", ti != null ? ti.textureType.ToString() : "?",
            ti != null ? ti.mipmapEnabled.ToString() : "?", ti != null ? ti.maxTextureSize.ToString() : "?"));
    }

    // ---- 实际世界尺寸：实例化到当前场景量一次，完事销毁 ----
    sb.AppendLine("---------- 实际世界尺寸（实例化测量）----------");
    if (prefabGO != null)
    {
        var inst = Object.Instantiate(prefabGO);
        inst.name = "__FengProbe";
        inst.transform.position = Vector3.zero;
        inst.transform.rotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;

        float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        int rendCount = 0;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r is TrailRenderer) continue;
            rendCount++;
            var b = r.bounds;
            minY = Mathf.Min(minY, b.min.y); maxY = Mathf.Max(maxY, b.max.y);
            minX = Mathf.Min(minX, b.min.x); maxX = Mathf.Max(maxX, b.max.x);
            minZ = Mathf.Min(minZ, b.min.z); maxZ = Mathf.Max(maxZ, b.max.z);
        }
        sb.AppendLine(string.Format("  渲染器数={0}", rendCount));
        sb.AppendLine(string.Format("  世界包围盒：y {0:F4}..{1:F4}  身高 {2:F4}", minY, maxY, maxY - minY));
        sb.AppendLine(string.Format("              x {0:F4}..{1:F4}  宽 {2:F4}", minX, maxX, maxX - minX));
        sb.AppendLine(string.Format("              z {0:F4}..{1:F4}  厚 {2:F4}", minZ, maxZ, maxZ - minZ));
        sb.AppendLine("  root/localScale = " + inst.transform.localScale.ToString("F4"));
        var t0 = inst.transform;
        sb.AppendLine(string.Format("  顶层 '{0}' localScale={1} localPos={2}",
            t0.name, t0.localScale.ToString("F4"), t0.localPosition.ToString("F4")));
        foreach (var ch in t0.GetComponentsInChildren<Transform>(true))
        {
            if (ch.name == "root" || ch.name == "Mesh" || ch.name == "CC_Base_Hip" || ch.name == "CC_Base_Head")
                sb.AppendLine(string.Format("    '{0}' 层级深度={1} localScale={2} localPos={3}",
                    ch.name, Depth(ch, t0), ch.localScale.ToString("F4"), ch.localPosition.ToString("F4")));
        }
        // 关键骨骼世界高度
        Transform hips = null, head = null, lfoot = null, rfoot = null, lhand = null, chest = null;
        foreach (var ch in inst.GetComponentsInChildren<Transform>(true))
        {
            if (ch.name == "CC_Base_Hip") hips = ch;
            else if (ch.name == "CC_Base_Head") head = ch;
            else if (ch.name == "CC_Base_L_Foot") lfoot = ch;
            else if (ch.name == "CC_Base_R_Foot") rfoot = ch;
            else if (ch.name == "CC_Base_L_Hand") lhand = ch;
            else if (ch.name == "CC_Base_Spine02") chest = ch;
        }
        sb.AppendLine(string.Format("  Hips 世界 y={0:F4}   Head 世界 y={1:F4}   Chest 世界 y={2:F4}", 
            hips != null ? hips.position.y : -1, head != null ? head.position.y : -1, chest != null ? chest.position.y : -1));
        sb.AppendLine(string.Format("  LFoot 世界 y={0:F4}  RFoot 世界 y={1:F4}  LHand 世界 y={2:F4}",
            lfoot != null ? lfoot.position.y : -1, rfoot != null ? rfoot.position.y : -1, lhand != null ? lhand.position.y : -1));
        Object.DestroyImmediate(inst);
    }

    string outPath = System.IO.Path.Combine(projRoot, "Tools/reports/f_probe.txt");
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath));
    System.IO.File.WriteAllText(outPath, sb.ToString());
    Debug.Log("[f_probe] 报告已写入 Tools/reports/f_probe.txt，共 " + sb.Length + " 字符");
    yield return null;
}

int Depth(Transform t, Transform root)
{
    int d = 0;
    while (t != null && t != root) { t = t.parent; d++; }
    return d;
}

return Body();
