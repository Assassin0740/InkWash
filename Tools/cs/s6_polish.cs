// s6_polish.cs —— 画面质感修复（编辑态，一次跑完，幂等）
//
// 用户反馈原话：
//   「这个水墨风有点奇怪，像是普通积木玩偶一样，不够白啊，有点发灰，效果不好，
//     然后没有挥墨的那种效果感觉」
//
// 逐条对应的真因（都是**实测出来的**，不是猜的，见 Tools/reports/r_scene.txt）：
//
//   ① "像普通积木玩偶" —— 场景里 14 个几何体（4 面墙 + 4 道门 + 4 根柱 + 1 地面 + 1 平台）
//      全部用 `M_Whitebox_*`：**URP/Lit + 无贴图的中灰**（0.55 / 0.6 / 0.72 / 0.8）。
//      灰 + 连续 PBR 明暗 + 干净棱边 = 一眼就是积木盒子。
//      → 换成本项目自研的 `InkWash/InkSurface`（纸白 + 3 阶淡墨 + 飞白 + 积墨 + 高度留白）。
//      ★ 关键认知：**画面的中间调是被场景决定的**，人物只占几个百分点像素，
//        角色 shader 再水墨也救不回一个灰场景。水墨画里"纸比墨多"。
//
//   ② "不够白 / 发灰" —— 三个来源叠加：
//      · 场景环境光 Trilight：sky 0.62 / equator 0.50 / **ground 0.30**（地面方向极暗）
//      · 角色材质 `_PaperColor` 封顶在 0.847 —— 受光面**永远到不了白**
//      · 宣纸后处理纸纹反复相乘（strength 0.45）再压暗边缘（vignette 0.32）
//      → 环境光三色整体提亮、纸色提到 0.96、纸纹与压暗各降一半。
//
//   ③ "没有挥墨的效果" —— 刀光弧面偏小偏短（radius 1.45 / life 0.26）。
//      → 加大加长，并把拖尾门控放宽一点（只在真正有速度时出光这条**不能动**，
//        那是"刀还没动光先出来"的修复）。
//
// 本脚本幂等：重复运行只是把同样的值再写一遍。
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using InkWash.Effects;
using InkWash.Rendering;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var scene = SceneManager.GetActiveScene();
    sb.AppendLine("场景 = " + scene.name);
    sb.AppendLine("===== 画面质感修复（白盒 → 水墨 / 提亮 / 挥墨） =====");

    var surfShader = Shader.Find("InkWash/InkSurface");
    sb.AppendLine();
    sb.AppendLine("InkWash/InkSurface 找到 = " + (surfShader != null));
    if (surfShader == null)
    {
        sb.AppendLine("** Shader 未导入，先跑一次 AssetDatabase.Refresh() 再重跑本脚本");
        File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s6_polish.txt"), sb.ToString());
        yield return null;
        yield break;
    }

    var brushTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
        "Assets/_Project/Art/Textures/T_InkBrushNoise.png");
    sb.AppendLine("飞白噪声贴图 = " + (brushTex != null ? brushTex.name : "** 找不到"));

    // ==================================================================
    // ① 场景白盒 → 水墨材质
    // ==================================================================
    sb.AppendLine();
    sb.AppendLine("---- ① 场景材质换成 InkWash/InkSurface ----");

    // 纸白基调：墙最白（大面积留白），地面略沉（压住画面下部），柱与台居中。
    // 暗部一律用**淡墨**（0.44~0.52 冷灰蓝）而不是黑 —— 大面积深色会闷死整个空间。
    TuneSurface(sb, surfShader, brushTex, "M_Whitebox_Wall",
        paper: new Color(0.952f, 0.947f, 0.928f), ink: new Color(0.470f, 0.492f, 0.530f),
        bands: 3f, brushScale: 7f, brushStrength: 0.52f, mottle: 0.16f, mottleScale: 0.30f,
        heightFade: 0.22f, heightFrom: 1.0f, heightTo: 5.0f, softness: 0.16f);

    TuneSurface(sb, surfShader, brushTex, "M_Whitebox_Ground",
        paper: new Color(0.925f, 0.919f, 0.899f), ink: new Color(0.520f, 0.535f, 0.560f),
        bands: 3f, brushScale: 5.5f, brushStrength: 0.46f, mottle: 0.34f, mottleScale: 0.22f,
        heightFade: 0.0f, heightFrom: 0f, heightTo: 1f, softness: 0.20f);

    TuneSurface(sb, surfShader, brushTex, "M_Whitebox_Pillar",
        paper: new Color(0.905f, 0.900f, 0.880f), ink: new Color(0.440f, 0.460f, 0.500f),
        bands: 4f, brushScale: 9f, brushStrength: 0.55f, mottle: 0.20f, mottleScale: 0.45f,
        heightFade: 0.30f, heightFrom: 1.0f, heightTo: 6.0f, softness: 0.12f);

    TuneSurface(sb, surfShader, brushTex, "M_Whitebox_Platform",
        paper: new Color(0.958f, 0.953f, 0.934f), ink: new Color(0.500f, 0.518f, 0.552f),
        bands: 3f, brushScale: 6f, brushStrength: 0.48f, mottle: 0.24f, mottleScale: 0.30f,
        heightFade: 0.10f, heightFrom: 0.3f, heightTo: 2.0f, softness: 0.18f);

    // ==================================================================
    // ② 环境光提亮（"发灰"的最大单项）
    // ==================================================================
    sb.AppendLine();
    sb.AppendLine("---- ② 环境光 / 补光 ----");
    sb.AppendLine("  before: sky=" + C(RenderSettings.ambientSkyColor)
                  + "  equator=" + C(RenderSettings.ambientEquatorColor)
                  + "  ground=" + C(RenderSettings.ambientGroundColor)
                  + "  intensity=" + F(RenderSettings.ambientIntensity));

    RenderSettings.ambientMode = AmbientMode.Trilight;
    RenderSettings.ambientSkyColor = new Color(0.885f, 0.892f, 0.885f, 1f);
    RenderSettings.ambientEquatorColor = new Color(0.815f, 0.822f, 0.825f, 1f);
    // ★ ground 原来只有 0.30：所有**朝下**的面（墙根、角色下半身、地面遮挡处）几乎全黑，
    //   画面下半部整体发闷。这是"发灰"里最容易被忽略的一项。
    RenderSettings.ambientGroundColor = new Color(0.640f, 0.645f, 0.650f, 1f);
    RenderSettings.ambientIntensity = 1.0f;

    sb.AppendLine("  after : sky=" + C(RenderSettings.ambientSkyColor)
                  + "  equator=" + C(RenderSettings.ambientEquatorColor)
                  + "  ground=" + C(RenderSettings.ambientGroundColor));

    // 补光：原先 0.28，只够勾一点环境色；提到 0.45 让背光面也有纸色的透亮感
    var lights = UnityEngine.Object.FindObjectsOfType<Light>(true);
    foreach (var l in lights)
    {
        string low = l.gameObject.name.ToLowerInvariant();
        if (low.Contains("fill"))
        {
            sb.AppendLine("  " + l.gameObject.name + " 强度 " + F(l.intensity) + " → 0.45");
            l.intensity = 0.45f;
            EditorUtility.SetDirty(l);
        }
        else if (low.Contains("sun") || low.Contains("directional"))
        {
            sb.AppendLine("  " + l.gameObject.name + " 强度 " + F(l.intensity) + " → 1.30");
            l.intensity = 1.30f;
            EditorUtility.SetDirty(l);
        }
    }

    // ==================================================================
    // ③ 角色与敌人的墨色：受光面推向纸白
    // ==================================================================
    sb.AppendLine();
    sb.AppendLine("---- ③ 角色/敌人材质 ----");
    foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Art/Materials" }))
    {
        string p = AssetDatabase.GUIDToAssetPath(guid);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (mat == null || mat.shader == null) continue;
        if (!mat.shader.name.Contains("InkCharacter")) continue;

        string before = mat.HasProperty("_PaperColor") ? C(mat.GetColor("_PaperColor")) : "(无)";
        if (mat.HasProperty("_PaperColor"))
            mat.SetColor("_PaperColor", new Color(0.960f, 0.951f, 0.925f, 1f));
        // 墨色略调冷一点点，与纸白形成干净的冷暖对比（水墨的"墨"是冷青黑，不是纯灰黑）
        if (mat.HasProperty("_InkColor"))
            mat.SetColor("_InkColor", new Color(0.058f, 0.068f, 0.094f, 1f));
        // 轮廓墨略加强：白纸背景上，边缘墨线是"剪纸感"的来源
        if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", 0.95f);
        if (mat.HasProperty("_RimPower")) mat.SetFloat("_RimPower", 3.0f);

        EditorUtility.SetDirty(mat);
        sb.AppendLine("  " + Path.GetFileName(p) + "  _PaperColor " + before + " → " + C(mat.GetColor("_PaperColor")));
    }

    // ==================================================================
    // ④ 宣纸后处理：纸要更白、压暗要更少
    // ==================================================================
    sb.AppendLine();
    sb.AppendLine("---- ④ 宣纸底纹（InkPaper） ----");
    foreach (var rpath in new[] { "Assets/Settings/URP-HighFidelity-Renderer.asset",
                                  "Assets/Settings/URP-Balanced-Renderer.asset" })
        TunePaper(sb, rpath);

    // ==================================================================
    // ⑤ 挥墨：刀光加大
    // ==================================================================
    sb.AppendLine();
    sb.AppendLine("---- ⑤ 刀光 / 拖尾（SwordVfx） ----");
    TuneSword(sb, "Assets/_Project/Prefabs/Player/Player.prefab");

    // ==================================================================
    // 保存
    // ==================================================================
    EditorSceneManager.MarkSceneDirty(scene);
    bool saved = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + saved);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s6_polish.txt"), sb.ToString());
    Debug.Log("[s6_polish] done");
    yield return null;
}

// ======================================================================
// 工具
// ======================================================================

/// 把一个材质换成 InkWash/InkSurface 并写入参数。
/// 为什么是"改现有材质"而不是"新建材质再替换场景引用"：
///   改动越少越不容易漏 —— 场景里 14 个渲染器都指向这 4 个材质，
///   改 shader 与参数是**原地生效**，不涉及任何引用重连（也就不会出现漏改一个对象）。
void TuneSurface(StringBuilder sb, Shader shader, Texture2D brush, string matName,
                 Color paper, Color ink, float bands, float brushScale, float brushStrength,
                 float mottle, float mottleScale, float heightFade, float heightFrom,
                 float heightTo, float softness)
{
    string path = "Assets/_Project/Art/Materials/" + matName + ".mat";
    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (mat == null) { sb.AppendLine("  ** 找不到 " + path); return; }

    string oldShader = mat.shader != null ? mat.shader.name : "(无)";
    mat.shader = shader;
    mat.renderQueue = (int)RenderQueue.Geometry;

    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
    if (mat.HasProperty("_PaperColor")) mat.SetColor("_PaperColor", paper);
    if (mat.HasProperty("_InkColor")) mat.SetColor("_InkColor", ink);
    if (mat.HasProperty("_Bands")) mat.SetFloat("_Bands", bands);
    if (mat.HasProperty("_BandSoftness")) mat.SetFloat("_BandSoftness", softness);
    if (mat.HasProperty("_InkDensity")) mat.SetFloat("_InkDensity", 0.5f);
    if (mat.HasProperty("_BrushTex") && brush != null) mat.SetTexture("_BrushTex", brush);
    if (mat.HasProperty("_BrushScale")) mat.SetFloat("_BrushScale", brushScale);
    if (mat.HasProperty("_BrushStrength")) mat.SetFloat("_BrushStrength", brushStrength);
    if (mat.HasProperty("_InkMottle")) mat.SetFloat("_InkMottle", mottle);
    if (mat.HasProperty("_MottleScale")) mat.SetFloat("_MottleScale", mottleScale);
    if (mat.HasProperty("_HeightFade")) mat.SetFloat("_HeightFade", heightFade);
    if (mat.HasProperty("_HeightFrom")) mat.SetFloat("_HeightFrom", heightFrom);
    if (mat.HasProperty("_HeightTo")) mat.SetFloat("_HeightTo", heightTo);
    if (mat.HasProperty("_AmbientTint")) mat.SetColor("_AmbientTint", new Color(0.92f, 0.945f, 1f, 1f));

    EditorUtility.SetDirty(mat);
    sb.AppendLine("  " + matName + "  " + oldShader + " → " + shader.name
                  + "  纸白" + C(paper) + " 淡墨" + C(ink) + " 阶" + F(bands) + " 飞白" + F(brushStrength));
}

/// 改 InkPaper Feature 的参数（子资产，两个渲染器各一份）。
void TunePaper(StringBuilder sb, string rendererPath)
{
    var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
    if (data == null) { sb.AppendLine("  ** 找不到 " + rendererPath); return; }

    var so = new SerializedObject(data);
    var feats = so.FindProperty("m_RendererFeatures");
    if (feats == null) { sb.AppendLine("  ** " + rendererPath + " 结构异常"); return; }

    for (int i = 0; i < feats.arraySize; i++)
    {
        var f = feats.GetArrayElementAtIndex(i).objectReferenceValue;
        var paper = f as InkPaperFeature;
        if (paper == null) continue;

        var s = paper.settings;
        sb.AppendLine("  " + Path.GetFileName(rendererPath) + " → " + f.name);
        sb.AppendLine("    paperStrength " + F(s.paperStrength) + " → 0.26　（纸纹相乘会整体压暗，降一半）");
        sb.AppendLine("    vignette      " + F(s.vignette) + " → 0.14　（原 0.32 把画面四周压灰）");
        sb.AppendLine("    tintStrength  " + F(s.tintStrength) + " → 0.12　（米黄着染会显脏）");
        sb.AppendLine("    paperTint     " + C(s.paperTint) + " → (0.98, 0.972, 0.95)");
        sb.AppendLine("    grainStrength " + F(s.grainStrength) + " → 0.15");
        sb.AppendLine("    inkDeepen     " + F(s.inkDeepen) + " → 0.20　（暗部保持沉，对比才立得住）");

        s.paperStrength = 0.26f;
        s.vignette = 0.14f;
        s.tintStrength = 0.12f;
        s.paperTint = new Color(0.980f, 0.972f, 0.950f, 1f);
        s.grainStrength = 0.15f;
        s.inkDeepen = 0.20f;
        s.paperContrast = 1.10f;

        EditorUtility.SetDirty(paper);
        EditorUtility.SetDirty(data);
    }
}

/// 刀光加大 —— "没有挥墨的那种效果感觉"。
void TuneSword(StringBuilder sb, string prefabPath)
{
    var root = PrefabUtility.LoadPrefabContents(prefabPath);
    if (root == null) { sb.AppendLine("  ** 打不开 " + prefabPath); return; }

    var vfx = root.GetComponentInChildren<SwordVfx>(true);
    if (vfx == null) { sb.AppendLine("  ** 预制体里找不到 SwordVfx"); PrefabUtility.UnloadPrefabContents(root); return; }

    sb.AppendLine("  before: 弧 r=" + F(vfx.arcRadius) + " 厚=" + F(vfx.arcThickness)
                  + " 角=" + F(vfx.arcSweepDeg) + " 命=" + F(vfx.arcLifetime)
                  + " | 拖尾 时=" + F(vfx.trailTime) + " 起宽=" + F(vfx.trailStartWidth));

    // 弧面加大加长：原来 1.45 半径 / 0.26 s，在 1.78 m 高的角色身上只够糊一小片，
    // 而且消失得太快，镜头晃一下就没影了 —— 观感上就是"没出墨"。
    vfx.arcRadius = 2.10f;
    vfx.arcThickness = 1.25f;
    vfx.arcSweepDeg = 155f;
    vfx.arcLifetime = 0.34f;
    vfx.arcHeight = 1.10f;

    // 拖尾：更宽更久，笔触才"拖"得出来
    vfx.trailTime = 0.24f;
    vfx.trailStartWidth = 0.52f;
    vfx.trailEndWidth = 0.03f;

    // ★ 门控阈值**只微调，不能取消**：trailMinSpeed 是"刀还没速度、光先出来"那个 bug 的修复。
    //   降低会让起手阶段重新出光；这里从 3.0 放到 2.6，只是让它在真挥起来时更早接上。
    vfx.trailMinSpeed = 2.6f;
    vfx.trailHoldMinSpeed = 1.4f;
    vfx.trailStartDelay = 0.04f;

    // 墨色更实（alpha 0.92 → 0.96），飞白略降让弧面读得出"实墨"而不是一层灰雾
    vfx.inkColor = new Color(0.045f, 0.050f, 0.068f, 0.96f);
    vfx.flyingWhite = 0.30f;

    EditorUtility.SetDirty(vfx);
    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
    PrefabUtility.UnloadPrefabContents(root);

    sb.AppendLine("  after : 弧 r=" + F(vfx.arcRadius) + " 厚=" + F(vfx.arcThickness)
                  + " 角=" + F(vfx.arcSweepDeg) + " 命=" + F(vfx.arcLifetime)
                  + " | 拖尾 时=" + F(vfx.trailTime) + " 起宽=" + F(vfx.trailStartWidth));
}

static string C(Color c) { return "(" + F(c.r) + ", " + F(c.g) + ", " + F(c.b) + ")"; }
static string F(float f) { return f.ToString("0.###"); }

return Body();
