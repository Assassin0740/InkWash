// 宝剑接入第 1 步：建项目自有材质 + 武器预制体。
//
// 为什么材质要建成**项目自有资产**（而不是直接用 FBX 内嵌的 Material.002）：
//   FBX 内嵌材质的改动会随"重新导入"一起被重置，而且它所在的 Assets/W_Sword/ 是不入库的
//   （授权不可再分发）—— 调好的参数会跟着素材一起丢。建成 M_W_Sword.mat 就纳入版本控制，
//   只有它引用的贴图需要在克隆后靠 install 脚本复原（贴图 GUID 由包内 .meta 保真）。
//
// 预制体遵循 socket 约定：**根节点保持 identity**（根的原点=挂载点，谁拖进场景都落在脚下），
// 对齐用的位移/旋转/缩放放在子节点 Align 里。这样 SwordVfx 只要 Instantiate 到右手骨下即可，
// 不需要在代码里再存"挂点偏移/缩放"这类序列化字段 —— 少一个字段就少一次"换模型漏折算"的机会。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(
        System.IO.Path.Combine(projRoot, "Tools/reports/w_setup.txt"), sb.ToString());

    if (EditorApplication.isPlaying)
    {
        sb.AppendLine("[ERR] 编辑器在 Play 模式，改了会被回滚。先 stop。");
        flush(); yield break;
    }

    // ---------------- 一、贴图导入设置修正 ----------------
    sb.AppendLine("========== 一、贴图导入设置 ==========");
    const string BASE_TEX = "Assets/W_Sword/texture_pbr_20250901.png";
    const string NRM_TEX = "Assets/W_Sword/texture_pbr_20250901_normal.png";
    const string ROUGH_TEX = "Assets/W_Sword/texture_pbr_20250901_roughness.png";

    // roughness 是**线性数据图**，导入器默认给了 sRGB=True。虽然当前没接线，
    // 但留着错的开关是个雷：以后谁接上去会得到系统性偏亮的结果。
    var ti = AssetImporter.GetAtPath(ROUGH_TEX) as TextureImporter;
    if (ti != null)
    {
        sb.AppendLine(string.Format("  roughness: textureType={0} sRGB={1} maxSize={2}", ti.textureType, ti.sRGBTexture, ti.maxTextureSize));
        if (ti.sRGBTexture)
        {
            ti.sRGBTexture = false;
            ti.SaveAndReimport();
            sb.AppendLine("  → 已把 roughness 的 sRGB 关掉（数据图必须线性）");
        }
        else sb.AppendLine("  → sRGB 已是 false，无需改");
    }
    else sb.AppendLine("  [WARN] 读不到 roughness 的 TextureImporter");

    var tib = AssetImporter.GetAtPath(BASE_TEX) as TextureImporter;
    var tin = AssetImporter.GetAtPath(NRM_TEX) as TextureImporter;
    if (tib != null) sb.AppendLine(string.Format("  base:      textureType={0} sRGB={1} maxSize={2}", tib.textureType, tib.sRGBTexture, tib.maxTextureSize));
    if (tin != null) sb.AppendLine(string.Format("  normal:    textureType={0} sRGB={1} maxSize={2}", tin.textureType, tin.sRGBTexture, tin.maxTextureSize));
    sb.AppendLine();

    // ---------------- 二、材质 ----------------
    sb.AppendLine("========== 二、建材质 M_W_Sword.mat ==========");
    const string MAT_PATH = "Assets/_Project/Art/Materials/M_W_Sword.mat";
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(projRoot, "Assets/_Project/Art/Materials"));
    // 新目录必须让 AssetDatabase 先看到，否则 CreateAsset 会失败
    AssetDatabase.Refresh();

    var shader = Shader.Find("Universal Render Pipeline/Lit");
    if (shader == null) { sb.AppendLine("[ERR] 找不到 URP/Lit"); flush(); yield break; }

    var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(BASE_TEX);
    var nrmTex = AssetDatabase.LoadAssetAtPath<Texture2D>(NRM_TEX);
    if (baseTex == null || nrmTex == null) { sb.AppendLine("[ERR] 贴图加载失败"); flush(); yield break; }

    var mat = AssetDatabase.LoadAssetAtPath<Material>(MAT_PATH);
    if (mat == null)
    {
        mat = new Material(shader) { name = "M_W_Sword" };
        AssetDatabase.CreateAsset(mat, MAT_PATH);
        sb.AppendLine("  新建 " + MAT_PATH);
    }
    else sb.AppendLine("  复用已存在的 " + MAT_PATH);

    // 金属度/光滑度的取值依据（见 w_shot.txt 的 A/B）：
    //   导入器原版 _Metallic=0 / _Smoothness=0.1035 → 哑光但亮，靠 albedo 撑住观感
    //   试过 _Metallic=0.65 / _Smoothness=0.52 → **反而更暗**
    //   原因：场景没有反射探针，金属只能反射 128px 的默认天空盒，比漫反射直射光暗得多。
    //   所以走"低金属度 + 中等光滑度"：靠定向光的高光给出金属感，不依赖环境反射。
    mat.shader = shader;
    mat.SetTexture("_BaseMap", baseTex); mat.SetColor("_BaseColor", Color.white);
    mat.SetTexture("_BumpMap", nrmTex); mat.SetFloat("_BumpScale", 1f);
    mat.EnableKeyword("_NORMALMAP");
    mat.SetFloat("_WorkflowMode", 1f);        // 1 = Metallic 工作流
    mat.SetFloat("_Metallic", 0.30f);
    mat.SetFloat("_Smoothness", 0.38f);
    mat.SetFloat("_SpecularHighlights", 1f);
    mat.SetFloat("_EnvironmentReflections", 1f);
    mat.SetFloat("_Cull", 2f);
    mat.SetFloat("_Surface", 0f);             // 不透明
    mat.renderQueue = -1;
    EditorUtility.SetDirty(mat);
    AssetDatabase.SaveAssets();

    sb.AppendLine(string.Format("  shader = {0}", mat.shader.name));
    sb.AppendLine(string.Format("  _BaseMap = {0}  _BumpMap = {1}", mat.GetTexture("_BaseMap").name, mat.GetTexture("_BumpMap").name));
    sb.AppendLine(string.Format("  _Metallic = {0:F2}  _Smoothness = {1:F2}  keywords = {2}",
        mat.GetFloat("_Metallic"), mat.GetFloat("_Smoothness"), string.Join(",", mat.shaderKeywords)));
    sb.AppendLine();

    // ---------------- 三、武器预制体 ----------------
    sb.AppendLine("========== 三、建武器预制体 W_Sword.prefab ==========");

    // ---- 对齐参数（全部来自 w_probe2.txt 的实测 + w_fit.txt 的扫描结论）----
    const float MODEL_TIP_Y = -0.5988f;     // 剑尖
    const float GRIP_GUARD_Y = 0.3000f;     // 握柄/剑格 分界
    const float MODEL_POMMEL_Y = 0.5988f;   // 剑首端
    const float MODEL_TOTAL = 1.1976f;      // 模型原生总长
    const float VISUAL_SCALE = 2.213f;      // 角色 Visual.localScale（对齐旧主角身高的结果）
    const float GRIP_POINT_Y = 0.3945f;     // 挂点落在握柄上的位置（w_fit 扫描选出的折中）
    const float SCALE = 0.49f;              // 模型缩放（模型容器空间）
    const float FIST_OFFSET_WORLD = 0.045f; // 拳心相对手骨原点沿 +Y 的世界偏移

    float fistOffset = FIST_OFFSET_WORLD / VISUAL_SCALE;
    float alignY = fistOffset + SCALE * GRIP_POINT_Y;

    sb.AppendLine("  对齐参数：");
    sb.AppendLine(string.Format("    挂点（模型本地 Y）  {0:F4}", GRIP_POINT_Y));
    sb.AppendLine(string.Format("    缩放                {0:F4}", SCALE));
    sb.AppendLine(string.Format("    Align.localPosition (0, {0:F6}, 0)", alignY));
    sb.AppendLine(string.Format("    Align.localEuler    (180, 0, 0)   ← 模型剑尖在 -Y，翻过来才沿手骨 +Y 指出去", alignY));
    sb.AppendLine();
    sb.AppendLine("  换算出的世界观感：");
    sb.AppendLine(string.Format("    总长        {0:F3} m  = 身高的 {1:F1}%（真剑 ~57-60%）", MODEL_TOTAL * SCALE * VISUAL_SCALE,
        MODEL_TOTAL * SCALE * VISUAL_SCALE / 2.1730f * 100f));
    sb.AppendLine(string.Format("    剑身        {0:F3} m", (GRIP_GUARD_Y - MODEL_TIP_Y) * SCALE * VISUAL_SCALE));
    sb.AppendLine(string.Format("    剑格展      {0:F3} m", 0.2324f * SCALE * VISUAL_SCALE));
    sb.AppendLine(string.Format("    腕→剑格柄侧 {0:F3} m", (alignY - SCALE * GRIP_GUARD_Y) * VISUAL_SCALE));
    sb.AppendLine(string.Format("    腕→剑尖     {0:F3} m", (alignY - SCALE * MODEL_TIP_Y) * VISUAL_SCALE));
    sb.AppendLine(string.Format("    剑首端在腕后 {0:F3} m  （< 前臂长 0.25 m → 整段落在前臂+宽袖内部，视觉上被遮住）",
        (SCALE * MODEL_POMMEL_Y - alignY) * VISUAL_SCALE));
    sb.AppendLine();

    var fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/W_Sword/W_Sword.FBX");
    if (fbxModel == null) { sb.AppendLine("[ERR] 加载不到 FBX"); flush(); yield break; }

    const string PREFAB_PATH = "Assets/_Project/Prefabs/Weapons/W_Sword.prefab";
    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(projRoot, "Assets/_Project/Prefabs/Weapons"));

    var root = new GameObject("W_Sword");
    var align = new GameObject("Align");
    align.transform.SetParent(root.transform, false);
    align.transform.localPosition = new Vector3(0f, alignY, 0f);
    align.transform.localEulerAngles = new Vector3(180f, 0f, 0f);
    align.transform.localScale = new Vector3(SCALE, SCALE, SCALE);

    // 用 Object.Instantiate（普通克隆）而不是 InstantiatePrefab：
    // 后者会在预制体里嵌一层"模型预制体实例"，多一层嵌套关系，没必要。
    var model = (GameObject)Object.Instantiate(fbxModel, align.transform);
    model.name = "W_Sword";
    // MUST normalize the model node. This FBX root carries a native localEuler=(270,0,0)
    // (every probe/render script in this project forces rotation=identity first, so all
    //  measured anatomy is in the "after zeroing" frame). Without zeroing, the composite
    //  rotation becomes Align(180deg X) * model(270deg X) = R_x(90), and the blade ends up
    //  pointing along the hand bone's -Z instead of +Y.
    model.transform.localPosition = Vector3.zero;
    model.transform.localRotation = Quaternion.identity;
    model.transform.localScale = Vector3.one;
    sb.AppendLine(string.Format("  model node normalized: localPos={0} localEuler={1} localScale={2}",
        model.transform.localPosition.ToString("F4"), model.transform.localEulerAngles.ToString("F2"), model.transform.localScale.ToString("F4")));

    var mr = model.GetComponentInChildren<MeshRenderer>();
    if (mr != null)
    {
        mr.sharedMaterial = mat;
        sb.AppendLine("  已把 " + MAT_PATH + " 指到模型渲染器（覆盖 FBX 内嵌的 Material.002）");
        sb.AppendLine(string.Format("  shadowCastingMode = {0}  receiveShadows = {1}", mr.shadowCastingMode, mr.receiveShadows));
    }
    else sb.AppendLine("  [WARN] 模型没有 MeshRenderer");

    var saved = PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
    Object.DestroyImmediate(root);
    AssetDatabase.SaveAssets();

    if (saved == null) { sb.AppendLine("[ERR] 预制体保存失败"); flush(); yield break; }
    sb.AppendLine("  已保存 " + PREFAB_PATH);
    sb.AppendLine();

    // ---- 复核：重新读盘，实测层级与尺寸 ----
    sb.AppendLine("========== 四、重新读盘复核 ==========");
    var re = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
    if (re == null) { sb.AppendLine("[ERR] 复核读不到预制体"); flush(); yield break; }

    DumpTree(re.transform, re.transform, sb, 1);

    var reMr = re.GetComponentInChildren<MeshRenderer>();
    var reMf = re.GetComponentInChildren<MeshFilter>();
    if (reMr != null) sb.AppendLine("  渲染器材质 = " + (reMr.sharedMaterial != null ? reMr.sharedMaterial.name : "NULL"));
    if (reMf != null && reMf.sharedMesh != null)
    {
        // ⚠ 不能用 MultiplyVector(bounds.extents)：extents 是"半长"不是向量，带旋转时会算错符号。
        //   正确做法是把 8 个角点都变换过去再取包围盒。
        var b = reMf.sharedMesh.bounds;
        var l2w = reMf.transform.localToWorldMatrix;   // 预制体根是 identity → 这就是"挂点空间"
        var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);
            var w = l2w.MultiplyPoint3x4(corner);
            mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
        }
        sb.AppendLine(string.Format("  网格顶点 {0} / 三角 {1}", reMf.sharedMesh.vertexCount, reMf.sharedMesh.triangles.Length / 3));
        sb.AppendLine(string.Format("  挂点空间包围盒 X {0:F4}..{1:F4}  Y {2:F4}..{3:F4}  Z {4:F4}..{5:F4}",
            mn.x, mx.x, mn.y, mx.y, mn.z, mx.z));
        sb.AppendLine(string.Format("  → 世界观感：腕→剑尖 {0:F4} m，剑首端在腕后 {1:F4} m，剑格左右展 {2:F4} m，剑身前后厚 {3:F4} m",
            mx.y * VISUAL_SCALE, -mn.y * VISUAL_SCALE, (mx.x - mn.x) * VISUAL_SCALE, (mx.z - mn.z) * VISUAL_SCALE));

        // 直接验证两个关键点：模型本地剑尖 (0,-0.5988,0) 与剑首端 (0,+0.5988,0)
        Check(reMf.transform, "剑尖", new Vector3(0f, MODEL_TIP_Y, 0f), sb);
        Check(reMf.transform, "剑格柄侧", new Vector3(0f, GRIP_GUARD_Y, 0f), sb);
        Check(reMf.transform, "剑首端", new Vector3(0f, MODEL_POMMEL_Y, 0f), sb);
    }

    flush();
}

void Check(Transform meshTf, string name, Vector3 meshLocalPoint, System.Text.StringBuilder sb)
{
    // 预制体根 = identity，所以 meshTf 的 localToWorldMatrix 就是"挂点空间"矩阵
    var p = meshTf.localToWorldMatrix.MultiplyPoint3x4(meshLocalPoint);
    sb.AppendLine(string.Format("  验证 {0,-8} 挂点空间 ({1,7:F4}, {2,7:F4}, {3,7:F4}) → 沿手骨 +Y 的世界距离 {4,7:F4} m  {5}",
        name, p.x, p.y, p.z, p.y * 2.213f,
        p.y > 0.01f ? "（腕前方，符合预期）" : (p.y < -0.01f ? "（腕后方）" : "（正好在腕上）")));
}

void DumpTree(Transform t, Transform root, System.Text.StringBuilder sb, int depth)
{
    var mr = t.GetComponent<MeshRenderer>();
    string tag = mr == null ? "" : " [MeshRenderer]";
    sb.AppendLine(string.Format("{0}{1}{2}", new string(' ', depth * 2), t.name, tag));
    sb.AppendLine(string.Format("{0}  localPos={1} localEuler={2} localScale={3}",
        new string(' ', depth * 2), t.localPosition.ToString("F4"), t.localEulerAngles.ToString("F2"), t.localScale.ToString("F4")));
    for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), root, sb, depth + 1);
}

return Body();
