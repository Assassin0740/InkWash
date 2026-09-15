// s4_build.cs —— Sprint 4（水墨风）资产落地（编辑态，一次跑完）：
//   ① 程序化生成「飞白噪声」与「宣纸底纹」两张 data 贴图（确定性，可复现）
//   ② 生成水墨角色材质 + 给主角/敌人挂 InkMaterialSwap（记录原 PBR 材质，供四阶段对照）
//   ③ 把 InkEdgeFeature / InkPaperFeature 装配进 URP 渲染器资产
//   ④ 场景里放一个参数面板（InkStylePanel）
//   ⑤ 保存
//
// 贴图为什么要程序化生成而不是找素材：
//   * 授权干净（完全自有）
//   * **可复现**：论文里写的参数（频率、层数、种子）和截图能一一对上；网上找的纸纹说不清来源
//   * 无缝：周期性哈希保证左右/上下能平铺，屏幕空间采样时不会看到接缝
//
// 本脚本**幂等**：重复跑不会把"水墨材质"当成"原始材质"记下来（那会让阶段 1 白模失效）。
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using InkWash.Rendering;
using InkWash.UI;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var scene = SceneManager.GetActiveScene();
    sb.AppendLine("场景 = " + scene.name);
    sb.AppendLine("===== S4 水墨风资产落地 =====");

    string texDir = "Assets/_Project/Art/Textures";
    string matDir = "Assets/_Project/Art/Materials";
    EnsureFolder("Assets/_Project/Art");
    EnsureFolder(texDir);
    EnsureFolder(matDir);

    // ---------------- ① 程序化贴图 ----------------
    sb.AppendLine();
    sb.AppendLine("---- ① 程序化贴图 ----");

    string noisePath = texDir + "/T_InkBrushNoise.png";
    WriteTexture(noisePath, 256, (x, y, w, h) =>
    {
        // 三层不同尺度的无缝 fBm 叠加：单层 fBm 看着像云，三层才有"墨在纸上蹭"的颗粒
        float a = Fbm(x, y, w, h, 4f, 4, 11);
        float b = Fbm(x, y, w, h, 11f, 3, 23);
        float c = Fbm(x, y, w, h, 29f, 2, 37);
        float v = Mathf.Clamp01(a * 0.55f + b * 0.30f + c * 0.15f);
        // 抬高对比：飞白的"断/连"需要明确的二值倾向，灰蒙蒙的噪声只会让线变脏
        v = Mathf.Clamp01((v - 0.5f) * 1.65f + 0.5f);
        return new Color(v, v, v, 1f);
    });
    sb.AppendLine("  飞白噪声 → " + noisePath);

    string paperPath = texDir + "/T_XuanPaper.png";
    WriteTexture(paperPath, 512, (x, y, w, h) =>
    {
        // R = 纸纤维：横竖两个方向的拉长噪声取平均 —— 纸浆是交织的，单向只会像布料
        float hx = Fbm(x, y, w, h, 5f, 4, 101, stretchX: 1f / 6f);
        float hy = Fbm(x, y, w, h, 5f, 4, 101, stretchY: 1f / 6f);
        float fibre = 0.5f + (hx * 0.5f + hy * 0.5f - 0.5f) * 0.85f;
        // G = 生宣颗粒：高频、低对比，负责"吃墨"的粗糙感
        float grain = Fbm(x, y, w, h, 46f, 2, 211);
        grain = 0.5f + (grain - 0.5f) * 0.9f;
        // B = 纸里的杂质/云斑：极低频，给整张纸一点不均匀
        float blotch = Fbm(x, y, w, h, 1.6f, 3, 307);
        return new Color(Mathf.Clamp01(fibre), Mathf.Clamp01(grain), Mathf.Clamp01(blotch), 1f);
    });
    sb.AppendLine("  宣纸底纹 → " + paperPath);

    AssetDatabase.Refresh();
    var noiseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(noisePath);
    var paperTex = AssetDatabase.LoadAssetAtPath<Texture2D>(paperPath);
    // ★ 必须关掉 sRGB：这两张是**数据**贴图不是颜色贴图，当颜色走一遍 gamma 会把
    //   噪声的分布压偏（细颗粒会整体变亮），飞白和纸纹的对比全变。
    ConfigureImporter(noisePath, false);
    ConfigureImporter(paperPath, false);
    sb.AppendLine("  导入设置：sRGB=Off   Wrap=Repeat   生成 Mipmap");

    // ---------------- ② 水墨材质 + 换材质组件 ----------------
    sb.AppendLine();
    sb.AppendLine("---- ② 角色/敌人水墨材质 ----");
    var inkShader = Shader.Find("InkWash/InkCharacter");
    if (inkShader == null)
    {
        sb.AppendLine("  ** 找不到 InkWash/InkCharacter —— 先让它编译通过再重跑本脚本");
    }
    else
    {
        // 主角原材质在 FBX 内部，从它的所有子资产里挑带 _BaseMap 的那个材质
        Texture2D fengDiffuse = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/Char_Feng/Fbx/Feng.fbx"))
        {
            var mat = a as Material;
            if (mat != null && mat.HasProperty("_BaseMap"))
            {
                fengDiffuse = mat.GetTexture("_BaseMap") as Texture2D;
                break;
            }
        }
        string mp = BuildInkMaterial(matDir, "M_Character_Ink", inkShader, fengDiffuse, noiseTex, Color.white);
        var charInk = AssetDatabase.LoadAssetAtPath<Material>(mp);
        sb.AppendLine("  " + mp + "   漫反射=" + (fengDiffuse != null ? fengDiffuse.name : "<无>"));

        // ---- 主角（场景对象）----
        var player = GameObject.Find("Player");
        if (player == null) sb.AppendLine("  ** 场景里找不到 Player");
        else
        {
            var smr = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) sb.AppendLine("  ** Player 下没有 SkinnedMeshRenderer");
            else
            {
                var swap = EnsureSwap(player, smr, new[] { charInk });
                sb.AppendLine("  Player ← InkMaterialSwap  槽 " + swap.inkMaterials.Length
                              + "   原材质=" + DescribeMats(swap.litMaterials));
            }
        }

        // ---- 敌人（预制体：运行时按波次生成，靠组件 OnEnable 自注册）----
        string[] enemyPrefabs =
        {
            "Assets/_Project/Prefabs/Enemies/Enemy_MoTu.prefab",
            "Assets/_Project/Prefabs/Enemies/Enemy_MoOu.prefab",
            "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab",
        };
        foreach (var pp in enemyPrefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(pp);
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null)
            {
                PrefabUtility.UnloadPrefabContents(root);
                sb.AppendLine("  ** " + Path.GetFileName(pp) + " 没找到 SkinnedMeshRenderer");
                continue;
            }

            var cur = smr.sharedMaterials;
            string stem = Path.GetFileNameWithoutExtension(pp);
            var newMats = new Material[cur.Length];
            for (int i = 0; i < cur.Length; i++)
            {
                Texture2D tex = (cur[i] != null && cur[i].HasProperty("_BaseMap"))
                    ? cur[i].GetTexture("_BaseMap") as Texture2D : null;
                newMats[i] = AssetDatabase.LoadAssetAtPath<Material>(
                    BuildInkMaterial(matDir, "M_Ink_" + stem + "_" + i, inkShader, tex, noiseTex, Color.white));
            }

            var swap = EnsureSwap(root, smr, newMats);
            smr.sharedMaterials = newMats;      // 预制体默认就是水墨（"敌人本就活在水墨世界里"）
            PrefabUtility.SaveAsPrefabAsset(root, pp);
            PrefabUtility.UnloadPrefabContents(root);
            sb.AppendLine("  " + Path.GetFileName(pp) + " ← InkMaterialSwap  槽 " + newMats.Length
                          + "   原材质=" + DescribeMats(swap.litMaterials));
        }
    }

    // ---------------- ③ 装配 RendererFeature ----------------
    sb.AppendLine();
    sb.AppendLine("---- ③ 装配 RendererFeature ----");
    // HighFidelity 是当前生效的渲染器；Balanced 一并装，免得切画质时水墨效果"消失"
    foreach (var rpath in new[] { "Assets/Settings/URP-HighFidelity-Renderer.asset",
                                  "Assets/Settings/URP-Balanced-Renderer.asset" })
        AttachFeatures(sb, rpath, noiseTex, paperTex);

    // ---------------- ④ 参数面板 ----------------
    sb.AppendLine();
    sb.AppendLine("---- ④ 参数面板 ----");
    var panelGo = GameObject.Find("InkStyle");
    if (panelGo == null) panelGo = new GameObject("InkStyle");
    var panel = panelGo.GetComponent<InkStylePanel>();
    if (panel == null) panel = panelGo.AddComponent<InkStylePanel>();
    panel.inkMaterial = AssetDatabase.LoadAssetAtPath<Material>(matDir + "/M_Character_Ink.mat");
    sb.AppendLine("  调参材质 = " + (panel.inkMaterial != null ? panel.inkMaterial.name : "<无>"));
    EditorUtility.SetDirty(panel);

    // ---------------- ⑤ 保存 ----------------
    EditorSceneManager.MarkSceneDirty(scene);
    bool saved = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + saved);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s4_build.txt"), sb.ToString());
    Debug.Log("[s4_build] done");
    yield return null;
}

// ======================================================================
// 工具
// ======================================================================

/// 幂等：第二次跑时**不要**用已经是水墨材质的当前槽位覆盖已记录的原始材质，
/// 否则阶段 1「白模」会切回水墨材质 —— 对照实验直接失效，而且没有任何报错。
InkMaterialSwap EnsureSwap(GameObject host, Renderer target, Material[] ink)
{
    var swap = host.GetComponent<InkMaterialSwap>();
    if (swap == null) swap = host.AddComponent<InkMaterialSwap>();
    swap.target = target;
    if (swap.litMaterials == null || swap.litMaterials.Length == 0)
        swap.litMaterials = target.sharedMaterials;
    swap.inkMaterials = ink;
    EditorUtility.SetDirty(swap);
    return swap;
}

string DescribeMats(Material[] mats)
{
    if (mats == null || mats.Length == 0) return "<空>";
    return string.Join(" + ", mats.Select(m => m == null ? "<null>" : m.name).ToArray());
}

void EnsureFolder(string path)
{
    if (AssetDatabase.IsValidFolder(path)) return;
    string parent = Path.GetDirectoryName(path).Replace('\\', '/');
    string leaf = Path.GetFileName(path);
    if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
    AssetDatabase.CreateFolder(parent, leaf);
}

void WriteTexture(string path, int size, System.Func<int, int, int, int, Color> gen)
{
    var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false);
    var px = new Color[size * size];
    for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = gen(x, y, size, size);
    tex.SetPixels(px);
    tex.Apply(true);
    File.WriteAllBytes(path, tex.EncodeToPNG());
    Object.DestroyImmediate(tex);
}

void ConfigureImporter(string path, bool sRGB)
{
    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
    if (ti == null) return;
    ti.textureType = TextureImporterType.Default;
    ti.sRGBTexture = sRGB;
    ti.wrapMode = TextureWrapMode.Repeat;
    ti.filterMode = FilterMode.Bilinear;
    ti.mipmapEnabled = true;
    ti.alphaSource = TextureImporterAlphaSource.None;
    ti.maxTextureSize = 512;
    ti.textureCompression = TextureImporterCompression.CompressedHQ;
    ti.SaveAndReimport();
}

/// 周期性哈希：坐标按 period 取模 ⇒ 贴图左右/上下无缝。
float Hash(int x, int y, int period, int seed)
{
    x = ((x % period) + period) % period;
    y = ((y % period) + period) % period;
    unchecked
    {
        int n = x * 374761393 + y * 668265263 + seed * 1442695041;
        n = (n ^ (n >> 13)) * 1274126177;
        n = n ^ (n >> 16);
        return (n & 0x7fffffff) / (float)0x7fffffff;
    }
}

float ValueNoise(float x, float y, int period, int seed)
{
    int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
    float fx = x - x0, fy = y - y0;
    // smoothstep 插值：线性插值会留下菱形网格纹
    fx = fx * fx * (3f - 2f * fx);
    fy = fy * fy * (3f - 2f * fy);
    float a = Hash(x0, y0, period, seed);
    float b = Hash(x0 + 1, y0, period, seed);
    float c = Hash(x0, y0 + 1, period, seed);
    float d = Hash(x0 + 1, y0 + 1, period, seed);
    return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
}

/// 无缝 fBm。`baseFreq` 是**整张图上**的格数，会被 round 成整数以保证周期性。
float Fbm(int px, int py, int w, int h, float baseFreq, int octaves, int seed,
          float stretchX = 1f, float stretchY = 1f)
{
    float sum = 0f, amp = 1f, norm = 0f;
    int period = Mathf.Max(1, Mathf.RoundToInt(baseFreq));
    float freq = period;
    for (int o = 0; o < octaves; o++)
    {
        float u = (float)px / w * freq * stretchX;
        float v = (float)py / h * freq * stretchY;
        sum += ValueNoise(u, v, period, seed + o * 97) * amp;
        norm += amp;
        amp *= 0.5f;
        freq *= 2f;
        period *= 2;
    }
    return sum / Mathf.Max(norm, 1e-5f);
}

string BuildInkMaterial(string dir, string name, Shader shader, Texture2D baseMap, Texture2D brushTex, Color baseColor)
{
    string path = dir + "/" + name + ".mat";
    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null)
    {
        m = new Material(shader);
        AssetDatabase.CreateAsset(m, path);
    }
    m.shader = shader;
    if (baseMap != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", baseMap);
    if (brushTex != null && m.HasProperty("_BrushTex")) m.SetTexture("_BrushTex", brushTex);
    m.SetColor("_BaseColor", baseColor);

    // 默认值即"论文里的推荐参数"；调参走运行时面板，不在这里反复改
    if (m.HasProperty("_InkColor")) m.SetColor("_InkColor", new Color(0.055f, 0.063f, 0.086f, 1f));
    if (m.HasProperty("_PaperColor")) m.SetColor("_PaperColor", new Color(0.847f, 0.812f, 0.741f, 1f));
    if (m.HasProperty("_Bands")) m.SetFloat("_Bands", 4f);
    if (m.HasProperty("_BandSoftness")) m.SetFloat("_BandSoftness", 0.06f);
    if (m.HasProperty("_InkDensity")) m.SetFloat("_InkDensity", 0.75f);
    if (m.HasProperty("_BrushScale")) m.SetFloat("_BrushScale", 26f);
    if (m.HasProperty("_BrushStrength")) m.SetFloat("_BrushStrength", 0.6f);
    if (m.HasProperty("_BrushUvFromWorld")) m.SetFloat("_BrushUvFromWorld", 1f);
    if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(0.08f, 0.09f, 0.12f, 1f));
    if (m.HasProperty("_RimPower")) m.SetFloat("_RimPower", 3.2f);
    if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.85f);
    if (m.HasProperty("_SpecBands")) m.SetFloat("_SpecBands", 2f);
    if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", 0.5f);
    if (m.HasProperty("_SpecSize")) m.SetFloat("_SpecSize", 48f);

    EditorUtility.SetDirty(m);
    return path;
}

void AttachFeatures(StringBuilder sb, string rendererPath, Texture2D noiseTex, Texture2D paperTex)
{
    var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
    if (data == null) { sb.AppendLine("  ** 找不到 " + rendererPath); return; }

    var so = new SerializedObject(data);
    var feats = so.FindProperty("m_RendererFeatures");
    var map = so.FindProperty("m_RendererFeatureMap");
    if (feats == null || map == null) { sb.AppendLine("  ** " + rendererPath + " 结构异常"); return; }

    bool hasEdge = false, hasPaper = false;
    for (int i = 0; i < feats.arraySize; i++)
    {
        var f = feats.GetArrayElementAtIndex(i).objectReferenceValue;
        if (f is InkEdgeFeature) hasEdge = true;
        if (f is InkPaperFeature) hasPaper = true;
    }

    if (!hasEdge)
    {
        var edge = ScriptableObject.CreateInstance<InkEdgeFeature>();
        edge.name = "Ink Edge 飞白墨线";
        edge.settings.dryBrushTex = noiseTex;
        AssetDatabase.AddObjectToAsset(edge, data);
        AppendFeature(feats, map, edge);
        sb.AppendLine("  " + Path.GetFileName(rendererPath) + " ← 追加 InkEdgeFeature");
    }
    if (!hasPaper)
    {
        var paper = ScriptableObject.CreateInstance<InkPaperFeature>();
        paper.name = "Ink Paper 宣纸底纹";
        paper.settings.paperTex = paperTex;
        AssetDatabase.AddObjectToAsset(paper, data);
        AppendFeature(feats, map, paper);
        sb.AppendLine("  " + Path.GetFileName(rendererPath) + " ← 追加 InkPaperFeature");
    }

    so.ApplyModifiedPropertiesWithoutUndo();
    data.SetDirty();
    EditorUtility.SetDirty(data);
    AssetDatabase.SaveAssets();

    sb.AppendLine("  " + Path.GetFileName(rendererPath) + " 现有 Feature " + feats.arraySize
                  + " 个 / map " + map.arraySize
                  + (feats.arraySize == map.arraySize ? "  [一致]" : "  [** 不一致，顺序会乱]"));
}

void AppendFeature(SerializedProperty feats, SerializedProperty map, ScriptableRendererFeature feature)
{
    feats.arraySize++;
    feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = feature;

    // m_RendererFeatureMap 存的是每个 Feature 的 localFileID，用来定顺序。
    // 少写这一项，编辑器重启后 Feature 顺序会乱（甚至报 "renderer feature null"）。
    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
    map.arraySize++;
    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
}

return Body();
