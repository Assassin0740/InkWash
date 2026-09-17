// s6_sky.cs —— 换水墨天空盒 + 重设刀光参数（编辑态，幂等）
//
// 两件事：
//   ① Default-Skybox 是蓝色渐变，与水墨完全冲突 → 换成 InkWash/InkSky
//   ② 上一版 s6_polish 把刀光"加大"了（弧 2.10 / 拖尾宽 0.52），
//      但实测数据推翻了那个方向：整段挥砍只攒到 2~5 个**拖尾顶点**，
//      在 0.52 m 的宽度下就成了"一坨黑块"而不是笔触。
//      真正的病根有两个（见 SwordVfx 的注释），都已改在代码里：
//        · 弧面建在水平面 → 第三人称平视几乎看不到（已改到竖直 XY 平面）
//        · Clear() 放在"检测到该发射"那帧 → 一笔墨被反复截断（已挪到挥砍开始）
//      所以这里的数值是**收回去**：轨迹长了，宽度就该跟着降。
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using InkWash.Effects;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var scene = SceneManager.GetActiveScene();
    sb.AppendLine("场景 = " + scene.name);
    sb.AppendLine("===== 水墨天空盒 + 刀光重设 =====");

    // ==================================================================
    // ① 天空盒材质
    // ==================================================================
    sb.AppendLine();
    sb.AppendLine("---- ① 天空盒 ----");
    var skyShader = Shader.Find("InkWash/InkSky");
    sb.AppendLine("  InkWash/InkSky 找到 = " + (skyShader != null));

    if (skyShader != null)
    {
        string matPath = "Assets/_Project/Art/Materials/M_InkSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        bool isNew = sky == null;
        if (isNew) sky = new Material(skyShader);
        sky.shader = skyShader;
        sky.name = "M_InkSky";

        var brush = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/_Project/Art/Textures/T_InkBrushNoise.png");

        sky.SetColor("_HorizonColor", new Color(0.966f, 0.963f, 0.952f, 1f));
        sky.SetColor("_ZenithColor", new Color(0.858f, 0.872f, 0.890f, 1f));
        sky.SetFloat("_GradientPow", 1.30f);
        sky.SetColor("_MountainColor", new Color(0.700f, 0.726f, 0.768f, 1f));
        sky.SetFloat("_MountainStrength", 0.58f);
        sky.SetFloat("_MountainBase", 0.005f);
        sky.SetFloat("_MountainHeight", 0.135f);
        sky.SetFloat("_MountainSharp", 180f);
        if (brush != null) sky.SetTexture("_BrushTex", brush);
        sky.SetFloat("_BrushScale", 6f);
        sky.SetFloat("_DryStrength", 0.16f);

        if (isNew) AssetDatabase.CreateAsset(sky, matPath);
        EditorUtility.SetDirty(sky);
        AssetDatabase.SaveAssets();

        var old = RenderSettings.skybox;
        sb.AppendLine("  天空盒 " + (old != null ? old.name : "(无)") + " → M_InkSky"
                      + (isNew ? "（新建）" : "（更新）"));
        RenderSettings.skybox = sky;
        DynamicGI.UpdateEnvironment();
    }
    else
    {
        sb.AppendLine("  ** Shader 未导入，先 Refresh 再重跑");
    }

    // ==================================================================
    // ② 刀光参数
    // ==================================================================
    sb.AppendLine();
    sb.AppendLine("---- ② 刀光 / 拖尾（SwordVfx） ----");
    TuneSword(sb, "Assets/_Project/Prefabs/Player/Player.prefab");

    // ==================================================================
    // 保存
    // ==================================================================
    EditorSceneManager.MarkSceneDirty(scene);
    bool saved = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + saved);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s6_sky.txt"), sb.ToString());
    Debug.Log("[s6_sky] done");
    yield return null;
}

void TuneSword(StringBuilder sb, string prefabPath)
{
    var root = PrefabUtility.LoadPrefabContents(prefabPath);
    if (root == null) { sb.AppendLine("  ** 打不开 " + prefabPath); return; }

    var vfx = root.GetComponentInChildren<SwordVfx>(true);
    if (vfx == null) { sb.AppendLine("  ** 预制体里找不到 SwordVfx"); PrefabUtility.UnloadPrefabContents(root); return; }

    sb.AppendLine("  before: 弧 r=" + F(vfx.arcRadius) + " 厚=" + F(vfx.arcThickness)
                  + " 角=" + F(vfx.arcSweepDeg) + " 命=" + F(vfx.arcLifetime) + " 高=" + F(vfx.arcHeight)
                  + " | 拖尾 时=" + F(vfx.trailTime) + " 宽=" + F(vfx.trailStartWidth)
                  + " 阈值=" + F(vfx.trailMinSpeed) + " | 锚点=" + V(vfx.bladeLocalOffset));

    // 弧：竖直平面里的"一撇"，尺寸贴着角色身高（1.78 m）
    vfx.arcRadius = 1.45f;
    vfx.arcThickness = 0.70f;
    vfx.arcSweepDeg = 155f;
    vfx.arcLifetime = 0.30f;
    vfx.arcHeight = 1.25f;
    vfx.arcTiltDeg = -28f;
    vfx.enableArc = true;

    // 拖尾：**变窄、变长、留得久**
    vfx.trailTime = 0.30f;
    vfx.trailStartWidth = 0.26f;
    vfx.trailEndWidth = 0.015f;
    vfx.trailMinSpeed = 1.0f;        // 只挡"完全静止"，见 SwordVfx 里的实测说明
    vfx.trailHoldMinSpeed = 0.4f;
    vfx.trailStartDelay = 0.04f;

    // 锚点必须贴剑轴（+Z），偏离轴心会让轨迹自交成一团
    vfx.bladeLocalOffset = new Vector3(-0.0055f, 0.0623f, 0.2998f);

    vfx.inkColor = new Color(0.045f, 0.050f, 0.068f, 0.96f);
    vfx.flyingWhite = 0.30f;

    EditorUtility.SetDirty(vfx);
    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
    PrefabUtility.UnloadPrefabContents(root);

    sb.AppendLine("  after : 弧 r=" + F(vfx.arcRadius) + " 厚=" + F(vfx.arcThickness)
                  + " 角=" + F(vfx.arcSweepDeg) + " 命=" + F(vfx.arcLifetime) + " 高=" + F(vfx.arcHeight)
                  + " | 拖尾 时=" + F(vfx.trailTime) + " 宽=" + F(vfx.trailStartWidth)
                  + " 阈值=" + F(vfx.trailMinSpeed) + " | 锚点=" + V(vfx.bladeLocalOffset));
    sb.AppendLine("  已保存 " + prefabPath);
}

static string F(float f) { return f.ToString("0.###"); }
static string V(Vector3 v) { return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")"; }

return Body();
