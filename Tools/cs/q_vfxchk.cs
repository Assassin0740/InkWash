// q_vfxchk.cs —— 只读体检「新导入的 4 个粒子包」：着色器是不是内置管线（URP 下会洋红）+ 关键贴图的 alpha
//
// 为什么要查这个
// --------------
// 项目是 URP 14.0.12。Unity 商店里的粒子包（Particle Pack / WarFX / Fog Particles / SimpleFX）
// 绝大多数材质挂的是**内置管线着色器**（Standard / Legacy Shaders/Particles/* / Particles/Standard Unlit），
// 或者作者自写的 CG 着色器（WarFX 那 11 个 WFX_S_*）。这些在 URP 下**不会报错**，
// 只会静静地渲染成洋红 —— 属于典型「静默失效」。
//
// 所以本探针只回答两件事：
//   ① 这 4 个包里，哪些着色器在 URP 下可用、哪些会洋红（给结论，不猜）
//   ② 项目真正要用的那批贴图，alpha 通道在不在（TIF 没带 alpha ⇒ 粒子是个实心方块）
//
// 判「洋红」的口径（不写死期望值，按名字分类）：
//   · 名字以 `Universal Render Pipeline/` 开头         ⇒ URP 原生，✅
//   · 名字以 `Particles/` / `Legacy Shaders/` / `Standard` 开头，或叫 `Standard` ⇒ 内置管线，❌
//   · 其它（作者自定义）⇒ 一律先当 ❌，理由写在报告里，由人看图确认
// 注意：本探针**不修改任何资产**。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

var sb = new StringBuilder();

// ---------- 0) 先确认现在到底跑的是哪个管线 ----------
var rp = GraphicsSettings.currentRenderPipeline;
sb.AppendLine("=== 0. 渲染管线 ===");
sb.AppendLine("  currentRenderPipeline = " + (rp == null ? "（null = 内置管线！）" : rp.GetType().Name + " / " + rp.name));

string[] PACKS = new string[] {
    "Assets/UnityTechnologies",
    "Assets/WarFX Assets",
    "Assets/Fog Particles",
    "Assets/SimpleFX",
};

// ---------- 1) 着色器分族 + URP 判定 ----------
sb.AppendLine();
sb.AppendLine("=== 1. 材质 → 着色器（判 URP 会不会洋红）===");

var byShader = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
int matCount = 0;
foreach (var dir in PACKS)
{
    if (!AssetDatabase.IsValidFolder(dir)) { sb.AppendLine("  [缺失] " + dir); continue; }
    foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { dir }))
    {
        var path = AssetDatabase.GUIDToAssetPath(g);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) continue;
        matCount++;
        string sn = mat.shader != null ? mat.shader.name : "(shader 为 null)";
        if (!byShader.ContainsKey(sn)) byShader[sn] = new List<string>();
        byShader[sn].Add(path);
    }
}
sb.AppendLine("  材质总数 " + matCount + "，不同着色器 " + byShader.Count + " 个");

int nOk = 0, nBad = 0, nUnknown = 0;
foreach (var kv in byShader)
{
    string sn = kv.Key;
    string verdict;
    if (sn.StartsWith("Universal Render Pipeline/")) { verdict = "✅ URP 原生"; nOk += kv.Value.Count; }
    else if (sn == "Standard" || sn.StartsWith("Particles/") || sn.StartsWith("Legacy Shaders/")
             || sn.StartsWith("Mobile/") || sn.StartsWith("Sprites/"))
    { verdict = "❌ 内置管线 ⇒ URP 下洋红"; nBad += kv.Value.Count; }
    else { verdict = "⚠ 作者自定义（当 ❌ 处理，看图为证）"; nUnknown += kv.Value.Count; }

    sb.AppendLine("  " + kv.Value.Count.ToString().PadLeft(4) + " 个  " + verdict + "   " + sn);
    int show = Mathf.Min(2, kv.Value.Count);
    for (int i = 0; i < show; i++) sb.AppendLine("             例: " + kv.Value[i]);
}
sb.AppendLine("  ── 合计：URP 可用 " + nOk + " / 需换材质 " + nBad + " + 自定义 " + nUnknown + " ──");

// ---------- 2) 关键贴图的导入设置（alpha / sRGB / 尺寸）----------
sb.AppendLine();
sb.AppendLine("=== 2. 关键贴图（项目要「只取贴图」，所以这才是能用的东西）===");

string[] TEX = new string[] {
    // 电弧 / 电光
    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Textures/Lightning.tif",
    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Textures/LightningTrail.tif",
    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Textures/EnergyEffect.tif",
    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Textures/SparkParticle.tif",
    // 烟 / 雾
    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Textures/SmokeLoop.tif",
    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Textures/SmokeLoop02.tif",
    "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Textures/SmokePuff.png",
    "Assets/Fog Particles/Texture/Smoke Sprite Sheet.png",
    "Assets/WarFX Assets/WarFX/Desktop/Textures/Smoke/WFX_T_SmokeLoopAlpha.tga",
    "Assets/WarFX Assets/WarFX/Desktop/Textures/Smoke/WFX_T_SmokeNoise.tga",
    "Assets/WarFX Assets/WarFX/Desktop/Textures/Smoke/WFX_T_SmokeSpikeStretched A8.tga",
    // 通用光斑
    "Assets/WarFX Assets/WarFX/Desktop/Textures/Misc/WFX_T_GlowCircle A8.png",
};

sb.AppendLine("  贴图                                                     尺寸        格式                alpha源       有alpha sRGB  mips");
foreach (var t in TEX)
{
    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(t);
    if (tex == null) { sb.AppendLine("  [缺失] " + Path.GetFileName(t)); continue; }
    var imp = AssetImporter.GetAtPath(t) as TextureImporter;
    string alphaSrc = imp != null ? imp.alphaSource.ToString() : "?";
    bool hasA = imp != null && imp.DoesSourceTextureHaveAlpha();
    string srgb = imp != null ? imp.sRGBTexture.ToString() : "?";
    string mips = imp != null ? imp.mipmapEnabled.ToString() : "?";
    sb.AppendLine("  " + Path.GetFileName(t).PadRight(44)
                  + (tex.width + "x" + tex.height).PadRight(12)
                  + tex.format.ToString().PadRight(20)
                  + alphaSrc.PadRight(14) + hasA.ToString().PadRight(7)
                  + srgb.PadRight(6) + mips);
}

// ---------- 3) 逐条列出「本次特效要用的那几族」的每个材质 ----------
//   为什么逐条列：结论要能落到「哪个 prefab 能直接拖、哪个必须重建材质」，
//   只给统计数字没法指导动手。
sb.AppendLine();
sb.AppendLine("=== 3. 本次特效相关族：逐材质判定 ===");
string[] FAM = new string[] {
    "Fire & Explosion Effects/Materials",   // 含 Lightning / LightningTrail / EnergyExplosion / PlasmaExplosion
    "Smoke & Steam Effects/Materials",      // 黑烟主力
    "Misc Effects/Materials",               // 含 ElectricalSparks / FireFly
    "Magic Effects/Materials",
};
foreach (var dir in PACKS)
{
    if (!AssetDatabase.IsValidFolder(dir)) continue;
    foreach (var fam in FAM)
    {
        var full = dir + "/ParticlePack/EffectExamples/" + fam;
        if (!AssetDatabase.IsValidFolder(full)) continue;
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { full }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            string sn = mat.shader != null ? mat.shader.name : "(null)";
            string v = sn.StartsWith("Universal Render Pipeline/") ? "可直用" : "要重建";
            sb.AppendLine("  [" + v + "] " + Path.GetFileNameWithoutExtension(path).PadRight(30) + " ← " + sn);
        }
    }
}

// ---------- 4) 一句话结论 ----------
sb.AppendLine();
sb.AppendLine("=== 4. 结论 ===");
sb.AppendLine("  · URP 原生材质 = 可以直接拖进场景；其余一律渲染成洋红，只能拿它的贴图。");
sb.AppendLine("  · 有alpha=False 的贴图不能直接当粒子贴图（会渲染成实心方块）—— 见第 2 节那一列。");

// ---------- 落盘 + 返回摘要 ----------
//   探针返回值有长度上限，报告必须写文件，否则尾部会被截断（本次就是这么发现的）。
string RP = "D:/Unity Project/InkWash/Tools/reports/vfx_packs_chk.txt";
Directory.CreateDirectory(Path.GetDirectoryName(RP));
File.WriteAllText(RP, sb.ToString(), new UTF8Encoding(false));

var head = new StringBuilder();
head.AppendLine("报告已写入 Tools/reports/vfx_packs_chk.txt（" + sb.Length + " 字符）");
head.AppendLine(sb.ToString(0, Mathf.Min(1200, sb.Length)));
return head.ToString();
