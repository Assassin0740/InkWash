// W_Sword 精测：细扫剑身/护手/手柄的轴向边界 + 把导入器生成的材质属性全部倒出来。
//
// 为什么需要：
//   1. 12 段粗扫只能看出"最宽处在 Y 0.2~0.3"，但护手的确切起点决定挂到手骨后的对齐偏移。
//      对齐偏移算错 → 剑柄穿过手掌 / 剑格离开拳头，而且肉眼很难判断错多少。
//   2. 材质必须逐属性核对：URP/Lit 的 metallic 在 R、smoothness 在 A，
//      roughness 灰度图（无 alpha）若被直接接到 _MetallicGlossMap，
//      会读成"smoothness 恒为 1"—— 剑会变成镜面塑料球。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(
        System.IO.Path.Combine(projRoot, "Tools/reports/w_probe2.txt"), sb.ToString());

    const string FBX = "Assets/W_Sword/W_Sword.FBX";

    // ---------- 一、细扫轴向剖面 ----------
    sb.AppendLine("========== 一、细扫轴向剖面（200 段，找护手/手柄边界）==========");
    var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
    if (fbxAsset == null) { sb.AppendLine("[ERR] 加载不到 FBX"); flush(); yield break; }

    Mesh mesh = null;
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FBX)) if (o is Mesh mm) { mesh = mm; break; }
    if (mesh == null) { sb.AppendLine("[ERR] 找不到 Mesh"); flush(); yield break; }

    var v = mesh.vertices;
    var b = mesh.bounds;
    sb.AppendLine(string.Format("bounds Y {0:F4} .. {1:F4}  长 {2:F4}", b.min.y, b.max.y, b.size.y));
    sb.AppendLine();

    const int N = 200;
    float y0 = b.min.y, y1 = b.max.y;
    float step = (y1 - y0) / N;
    // 每层的 X/Z 跨度
    var spanX = new float[N];
    var spanZ = new float[N];
    var cnt = new int[N];
    for (int s = 0; s < N; s++)
    {
        float lo = y0 + step * s, hi = lo + step;
        float mnx = float.MaxValue, mxx = float.MinValue, mnz = float.MaxValue, mxz = float.MinValue;
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i].y < lo || v[i].y >= hi) continue;
            cnt[s]++;
            if (v[i].x < mnx) mnx = v[i].x; if (v[i].x > mxx) mxx = v[i].x;
            if (v[i].z < mnz) mnz = v[i].z; if (v[i].z > mxz) mxz = v[i].z;
        }
        spanX[s] = cnt[s] == 0 ? 0f : mxx - mnx;
        spanZ[s] = cnt[s] == 0 ? 0f : mxz - mnz;
    }

    // 打印：每 5 层一行，用 ASCII 条子示意 X 跨度
    sb.AppendLine("层号  Y区间            顶点数   X跨度    Z跨度   直观条");
    for (int s = 0; s < N; s += 5)
    {
        float lo = y0 + step * s;
        // 5 层取最大跨度做代表
        float bx = 0f, bz = 0f; int bc = 0;
        for (int k = s; k < Mathf.Min(s + 5, N); k++) { bx = Mathf.Max(bx, spanX[k]); bz = Mathf.Max(bz, spanZ[k]); bc += cnt[k]; }
        int bars = Mathf.Clamp(Mathf.RoundToInt(bx / 0.2324f * 30f), 0, 30);
        sb.AppendLine(string.Format("{0,4}  [{1,7:F4}..{2,7:F4}] {3,7}  {4:F4}  {5:F4}  {6}",
            s, lo, lo + step * 5, bc, bx, bz, new string('#', bars)));
    }
    sb.AppendLine();

    // 自动判定：以"剑身宽度"（取 -0.5..0.0 段的 X 跨度中位数）为基准，
    // 从剑尖端往柄端走，第一次 X 跨度 > 1.35×基准 的层 = 护手起点
    float baseW = 0f; int bc2 = 0;
    for (int s = 0; s < N; s++)
    {
        float lo = y0 + step * s;
        if (lo >= -0.50f && lo < 0f) { baseW += spanX[s]; bc2++; }
    }
    baseW = bc2 > 0 ? baseW / bc2 : 0.07f;
    sb.AppendLine(string.Format("剑身宽度基准（Y -0.5..0 平均 X 跨度）= {0:F4}", baseW));

    int guardLayer = -1;
    for (int s = 0; s < N; s++)
    {
        if (spanX[s] > baseW * 1.35f) { guardLayer = s; break; }
    }
    if (guardLayer >= 0)
    {
        float gy = y0 + step * guardLayer;
        sb.AppendLine(string.Format("→ 从剑尖端({0:F4})往柄端走，第一个「变宽」层 = 第 {1} 层，Y = {2:F4}", y0, guardLayer, gy));
    }
    else sb.AppendLine("→ 未找到明显变宽层（护手可能不明显）");

    // 剑尖确认：极值端 1% 的跨度
    float tipSpan = 0f;
    for (int s = 0; s < 3; s++) tipSpan = Mathf.Max(tipSpan, spanX[s]);
    float buttSpan = 0f;
    for (int s = N - 3; s < N; s++) buttSpan = Mathf.Max(buttSpan, spanX[s]);
    sb.AppendLine(string.Format("最低 1.5% 层最大 X 跨度 = {0:F4}  → {1}", tipSpan, tipSpan < baseW * 0.6f ? "收尖，是剑尖" : "没收尖"));
    sb.AppendLine(string.Format("最高 1.5% 层最大 X 跨度 = {0:F4}", buttSpan));
    sb.AppendLine();

    // ---------- 二、材质属性全量 ----------
    sb.AppendLine("========== 二、导入器生成的材质属性 ==========");
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FBX))
    {
        if (!(o is Material mat)) continue;
        sb.AppendLine(string.Format("材质 \"{0}\"   shader={1}", mat.name, mat.shader != null ? mat.shader.name : "null"));
        sb.AppendLine("  资产路径（= FBX 说明是内嵌子资产）：" + AssetDatabase.GetAssetPath(mat));
        int n = mat.shader != null ? mat.shader.GetPropertyCount() : 0;
        for (int i = 0; i < n; i++)
        {
            string pn = mat.shader.GetPropertyName(i);
            var pt = mat.shader.GetPropertyType(i);
            string val = "";
            try
            {
                switch (pt)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        var t = mat.GetTexture(pn);
                        val = t != null ? (t.name + "  [" + t.width + "x" + t.height + "  sRGB=" + ((t as Texture2D) != null && (t as Texture2D).isDataSRGB) + "]") : "null";
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        val = mat.GetFloat(pn).ToString("F4");
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        val = mat.GetColor(pn).ToString("F3");
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        val = mat.GetVector(pn).ToString("F3");
                        break;
                    default: val = "(其他类型)"; break;
                }
            }
            catch { val = "(读取失败)"; }
            if (val == "null" || val == "0.0000" || val == "(1.000, 1.000, 1.000, 1.000)") continue; // 只打有内容的
            sb.AppendLine(string.Format("    {0,-22} [{1,-8}] = {2}", pn, pt.ToString(), val));
        }
        sb.AppendLine("  --- 关键开关 ---");
        sb.AppendLine("    renderQueue = " + mat.renderQueue);
        sb.AppendLine("    enabledKeywords = " + string.Join(",", mat.shaderKeywords));
        sb.AppendLine();
    }

    // ---------- 三、贴图的导入设置 ----------
    sb.AppendLine("========== 三、贴图导入设置（sRGB 与类型对不对）==========");
    foreach (var path in new[] {
        "Assets/W_Sword/texture_pbr_20250901.png",
        "Assets/W_Sword/texture_pbr_20250901_normal.png",
        "Assets/W_Sword/texture_pbr_20250901_roughness.png" })
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { sb.AppendLine(path + "  → 无 TextureImporter"); continue; }
        sb.AppendLine(string.Format("{0}\n    textureType={1}  sRGBTexture={2}  maxTextureSize={3}  compression={4}  alphaSource={5}  mipmap={6}",
            System.IO.Path.GetFileName(path), ti.textureType, ti.sRGBTexture, ti.maxTextureSize,
            ti.textureCompression, ti.alphaSource, ti.mipmapEnabled));
    }

    flush();
}

return Body();
