// z6_mats —— B 路线：为 Ziyuan 四个素材创建水墨材质
// 绑定各自 baseColor 贴图（保留色相来源），墨阶参数按「主角花青 / 敌人赭墨」约定分配
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string outDir = "Assets/_Project/Art/Materials";
string texDir = "Assets/ThirdParty/Ziyuan";
string brushTex = "Assets/_Project/Art/Textures/T_InkBrushNoise.png";
string shaderPath = "Assets/_Project/Art/Shaders/InkCharacter.shader";

var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
sb.AppendLine($"shader = {(shader == null ? "NULL!" : shader.name)}");
var brush = AssetDatabase.LoadAssetAtPath<Texture2D>(brushTex);
sb.AppendLine($"brushTex = {(brush == null ? "NULL!" : brush.name)}");
sb.AppendLine();

// 材质定义：名字 / 贴图路径 / 墨阶 / 密度 / 墨彩 / 轮廓
// 敌人系 = 赭墨（暖、偏黑），Boss 龙 = 更浓更冷
var defs = new (string matName, string texPath, float[] floats, Color[] cols)[]
{
    ("M_Ink_Enemy_MoShan", texDir + "/mountain_orge/textures/Orge_LP_baseColor.png",
        new float[] { 0.30f, 0.058f, 5f, 0.5f, 0.72f, 0.014f, 1.6f, -0.18f, 0.06f, 4f, 26f, 0.6f, 1f, 0.50f, 0.42f, 3f, 0.95f, 2f, 48f, 0.5f },
        new Color[] {
            new Color(0.055f, 0.040f, 0.032f, 1f),   // _OutlineColor 赭墨轮廓
            Color.white,                              // _BaseColor
            new Color(0.100f, 0.068f, 0.055f, 1f),   // _InkDark
            new Color(0.845f, 0.790f, 0.715f, 1f),   // _InkLight
            new Color(0.300f, 0.235f, 0.195f, 1f),   // _InkMid
            new Color(0.08f, 0.09f, 0.12f, 1f)       // _RimColor
        }),

    ("M_Ink_Enemy_MoGuai", texDir + "/low-poly_orc/textures/07_-_Default_diffuse.png",
        new float[] { 0.28f, 0.058f, 5f, 0.5f, 0.72f, 0.014f, 1.6f, -0.16f, 0.06f, 4f, 26f, 0.6f, 1f, 0.48f, 0.42f, 3f, 0.95f, 2f, 48f, 0.5f },
        new Color[] {
            new Color(0.058f, 0.042f, 0.034f, 1f),
            Color.white,
            new Color(0.105f, 0.072f, 0.058f, 1f),
            new Color(0.850f, 0.795f, 0.720f, 1f),
            new Color(0.305f, 0.240f, 0.200f, 1f),
            new Color(0.08f, 0.09f, 0.12f, 1f)
        }),

    ("M_Ink_Enemy_MoGu", texDir + "/cursed_undead_soldier_rig/textures/Undead_Material_baseColor.png",
        new float[] { 0.22f, 0.058f, 5f, 0.5f, 0.72f, 0.014f, 1.6f, -0.24f, 0.06f, 4f, 26f, 0.6f, 1f, 0.55f, 0.42f, 3f, 0.95f, 2f, 48f, 0.5f },
        new Color[] {
            new Color(0.050f, 0.038f, 0.030f, 1f),
            Color.white,
            new Color(0.090f, 0.062f, 0.050f, 1f),
            new Color(0.830f, 0.775f, 0.700f, 1f),
            new Color(0.285f, 0.225f, 0.185f, 1f),
            new Color(0.08f, 0.09f, 0.12f, 1f)
        }),

    ("M_Ink_Boss_Dragon", texDir + "/chinese_dragon/textures/MI_b09_00_drg_hair_clearcoat.png",
        new float[] { 0.45f, 0.058f, 6f, 0.45f, 0.72f, 0.018f, 1.6f, -0.26f, 0.06f, 4f, 26f, 0.6f, 1f, 0.62f, 0.42f, 3f, 1.00f, 2f, 48f, 0.6f },
        new Color[] {
            new Color(0.042f, 0.032f, 0.028f, 1f),
            Color.white,
            new Color(0.080f, 0.055f, 0.045f, 1f),
            new Color(0.820f, 0.765f, 0.690f, 1f),
            new Color(0.270f, 0.210f, 0.175f, 1f),
            new Color(0.08f, 0.09f, 0.12f, 1f)
        }),
};

// 浮点属性顺序（与 defs.floats 一一对应）
string[] floatKeys = {
    "_ChromaKeep","_OutlineDistScale","_OutlineScale","_OutlineDry","_OutlineFacing",
    "_OutlineWidth","_MottleScale","_BandBias","_BandSoftness","_Bands",
    "_BrushScale","_BrushStrength","_BrushUvFromWorld","_InkDensity","_LadderSkew",
    "_RimPower","_RimStrength","_SpecBands","_SpecSize","_SpecStrength"
};
string[] colorKeys = { "_OutlineColor","_BaseColor","_InkDark","_InkLight","_InkMid","_RimColor" };

foreach (var d in defs)
{
    string path = outDir + "/" + d.matName + ".mat";
    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(d.texPath);
    sb.AppendLine($"--- {d.matName} ---");
    sb.AppendLine($"  baseColor tex = {(tex == null ? "!! NULL " + d.texPath : tex.name)}");

    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
    bool isNew = mat == null;
    if (isNew) mat = new Material(shader);
    mat.shader = shader;
    mat.name = d.matName;

    mat.SetTexture("_BaseMap", tex);
    mat.SetTexture("_BrushTex", brush);
    for (int i = 0; i < floatKeys.Length; i++) mat.SetFloat(floatKeys[i], d.floats[i]);
    for (int i = 0; i < colorKeys.Length; i++) mat.SetColor(colorKeys[i], d.cols[i]);

    if (isNew) AssetDatabase.CreateAsset(mat, path);
    else EditorUtility.SetDirty(mat);
    sb.AppendLine($"  {(isNew ? "CREATE" : "UPDATE")} {path}");
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// 复核：重新读盘确认参数真的写进去了（本项目硬规矩：改完必须回读）
sb.AppendLine();
sb.AppendLine("=== 回读复核（重新 LoadAssetAtPath）===");
foreach (var d in defs)
{
    string path = outDir + "/" + d.matName + ".mat";
    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { sb.AppendLine($"{d.matName}: !! 读不到"); continue; }
    var t = m.GetTexture("_BaseMap");
    sb.AppendLine($"{d.matName}: shader={m.shader.name} baseMap={(t == null ? "null" : t.name)}");
    sb.AppendLine($"   _BandBias={m.GetFloat("_BandBias"):F2} _InkDensity={m.GetFloat("_InkDensity"):F2} _ChromaKeep={m.GetFloat("_ChromaKeep"):F2} _OutlineWidth={m.GetFloat("_OutlineWidth"):F3}");
}

Directory.CreateDirectory("Tools/reports");
File.WriteAllText("Tools/reports/z6_mats.txt", sb.ToString(), Encoding.UTF8);
return "WROTE";
