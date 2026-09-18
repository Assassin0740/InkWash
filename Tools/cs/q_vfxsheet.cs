// q_vfxsheet.cs —— 把新导入的粒子 prefab 逐个出图做「实物对照表」（**编辑模式**，不进 Play）
//
// 为什么要在编辑模式出图
// ----------------------
// 这批素材是商店包，材质一半是内置管线（在 URP 下渲染成洋红）。光看文件名判断不了，
// 必须**看图**。进 Play 会带来域重载 / 场景被换掉 / 退出 Play 一堆麻烦，
// 而粒子在编辑模式里可以用 ParticleSystem.Simulate(秒) 手动推进 —— 干净得多。
//
// 四个关键手法（前两个是踩坑后改的）
// ---------------------------------
// ① `ps.Simulate(t, withChildren:false, restart:true)`：编辑模式把粒子推进到 t 秒。
//    对**每个**系统单独调用（withChildren=true 会重复推进子物体）。
// ② **背景参考色必须实测**：项目是 Linear 色彩空间，`Camera.backgroundColor = 0.38 灰`
//    在 sRGB 输出里是 0.65 灰 ⇒ 拿 0.38 去比，整帧 440x440 全被判成「非背景」，
//    「哪一帧粒子最多」的判据当场失效。改为**取图中出现最多的那个颜色**当背景
//    （直方图法），它自己会找到真正的底色。
// ③ **取景必须自校准**：`Renderer.bounds` 比实际内容大一个量级
//    （第一版按它取景，电弧在 440px 里只占 ~20px）。改为
//    渲染 → 测内容包围盒 → 收紧 → 再渲染，迭代 3 次。判据是**图里的像素**，
//    不是 prefab 自称的尺寸。
// ④ 相机固定朝 +Z（rotation = identity），这样像素↔世界是纯线性映射，
//    不用 LookAt 去绕。取景只动「中心 + 正交半高」。
//
// 采样时间不写死：对每个 prefab 试 T_SAMPLES 三个时刻，取最终帧里**内容像素最多**的那张。
// 这个判据同时是「Simulate 在编辑模式是否生效」的体检（全 ~0 就是没生效）。
//
// 中灰背景而不是纯黑：加法混合（电弧/光斑）在中灰上仍然亮，
// 而**黑烟**在纯黑背景上根本看不见。中灰能同时容纳两者。
//
// 本探针**不修改任何资产**，只读 prefab + 写 PNG。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

const string DIR = "D:/Unity Project/InkWash/Tools/screenshots/vfx";
const string RP = "D:/Unity Project/InkWash/Tools/reports/vfx_sheet.txt";
const int W = 440, H = 440;
static readonly float[] T_SAMPLES = new float[] { 0.35f, 1.10f, 2.40f };
static readonly float[] MARGIN = new float[] { 1.00f, 1.60f, 1.28f, 1.16f };  // 第 0 次用固定大框

Directory.CreateDirectory(DIR);

// (标签, prefab 路径, 归类)
var CAND = new (string tag, string path, string group)[] {
    // ── 电弧 / 电光（墨龙「喷息带电弧」「俯冲穿云电光」要的就是这一族）──
    ("01_SparksElec",  "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/ElectricalSparks.prefab", "电弧"),
    ("02_SparksEffect","Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/SparksEffect.prefab", "电弧"),
    ("03_ParticlesLight","Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/ParticlesLight.prefab", "电弧"),
    ("04_EnergyExplosion","Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/EnergyExplosion.prefab", "电弧"),
    ("05_PlasmaExplosion","Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/PlasmaExplosionEffect.prefab", "电弧"),
    ("15_SimpleFXLightray","Assets/SimpleFX/Prefabs/FX_Lightray.prefab", "电弧"),
    // ── 烟 / 雾 ──
    ("06_SmokeEffect", "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/SmokeEffect.prefab", "烟"),
    ("07_GroundFog",   "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/GroundFog.prefab", "烟"),
    ("08_RisingSteam", "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/RisingSteam.prefab", "烟"),
    ("09_DustMotes",   "Assets/UnityTechnologies/ParticlePack/EffectExamples/Misc Effects/Prefabs/DustMotesEffect.prefab", "烟"),
    ("10_FogBlue",     "Assets/Fog Particles/Prefabs/Bluish Fog.prefab", "烟"),
    ("11_SimpleFXSmoke","Assets/SimpleFX/Prefabs/FX_Smoke.prefab", "烟"),
    // ── 跨包：WarFX 的黑烟（材质是作者自写着色器，预期洋红；列出来是为了**留证据**）──
    ("12_WFX_GrenadeBlack","Assets/WarFX Assets/WarFX/_Effects/Smoke/WFX_SmokeGrenade Black.prefab", "黑烟"),
    ("13_WFX_FireBlackSmoke","Assets/WarFX Assets/WarFX/_Effects/Fire/Black Smoke/WFX_Fire Natural (Black Smoke).prefab", "黑烟"),
    ("14_WFX_GrenadeABBlack","Assets/WarFX Assets/WarFX/_Effects/Smoke/WFX_SmokeGrenade AlphaBlend Black.prefab", "黑烟"),
};

var sb = new StringBuilder();
sb.AppendLine("=== 粒子 prefab 实物对照（编辑模式 Simulate + 自校准取景）===");
sb.AppendLine("相机固定朝 +Z / 正交 / 中灰背景 / 取景迭代 3 次收紧 / 三个时刻取内容像素最多者");
sb.AppendLine();
sb.AppendLine("tag                   归类  选用时刻  内容px   占画幅   系统数  材质着色器");
sb.AppendLine("--------------------------------------------------------------------------------");

var basePos = new Vector3(0f, 4000f, 0f);

// 光照：URP/Particles/Lit 与 URP/Lit 需要光
var lightGo = new GameObject("PROBE_VFXSHEET_LIGHT");
var lt = lightGo.AddComponent<Light>();
lt.type = LightType.Directional;
lt.intensity = 1.15f;
lightGo.transform.rotation = Quaternion.Euler(38f, -35f, 0f);

var camGo = new GameObject("PROBE_VFXSHEET_CAM");
var cam = camGo.AddComponent<Camera>();
cam.orthographic = true;
cam.clearFlags = CameraClearFlags.SolidColor;
cam.backgroundColor = new Color(0.22f, 0.22f, 0.24f, 1f);   // 深灰；实际底色以实测为准
cam.nearClipPlane = 0.02f;
cam.farClipPlane = 400f;
cam.enabled = false;
cam.transform.rotation = Quaternion.identity;                // 固定朝 +Z
var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
cam.targetTexture = rt;

var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
var bgHist = new int[16 * 16 * 16];

// 渲染一次并返回：背景色 / 内容包围盒（像素，cx,cy 为质心，bw,bh 为尺寸）/ 内容像素数
void RenderAndMeasure(out Color32 bg, out int cx, out int cy, out int bw, out int bh, out int npx)
{
    cam.Render();
    RenderTexture.active = rt;
    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
    tex.Apply();
    RenderTexture.active = null;
    var px = tex.GetPixels32();

    // ① 背景 = 出现最多的量化颜色（Linear 色彩空间下不能假设 backgroundColor 就是底色的 sRGB 值）
    Array.Clear(bgHist, 0, bgHist.Length);
    for (int i = 0; i < px.Length; i++)
    {
        int k = ((px[i].r >> 4) << 8) | ((px[i].g >> 4) << 4) | (px[i].b >> 4);
        bgHist[k]++;
    }
    int bestK = 0, bestV = -1;
    for (int k = 0; k < bgHist.Length; k++) if (bgHist[k] > bestV) { bestV = bgHist[k]; bestK = k; }
    long sr = 0, sg = 0, sbl = 0; int cnt = 0;
    for (int i = 0; i < px.Length; i++)
    {
        int k = ((px[i].r >> 4) << 8) | ((px[i].g >> 4) << 4) | (px[i].b >> 4);
        if (k == bestK) { sr += px[i].r; sg += px[i].g; sbl += px[i].b; cnt++; }
    }
    bg = new Color32((byte)(sr / Mathf.Max(1, cnt)), (byte)(sg / Mathf.Max(1, cnt)), (byte)(sbl / Mathf.Max(1, cnt)), 255);

    // ② 内容包围盒
    int minX = W, maxX = -1, minY = H, maxY = -1; npx = 0;
    for (int y = 0; y < H; y++)
    {
        for (int x = 0; x < W; x++)
        {
            var p = px[y * W + x];
            int d = Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b);
            if (d > 24)
            {
                npx++;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
        }
    }
    if (npx < 12) { cx = W / 2; cy = H / 2; bw = 0; bh = 0; return; }
    cx = (minX + maxX) / 2; cy = (minY + maxY) / 2;
    bw = maxX - minX + 1; bh = maxY - minY + 1;
}

Color32 _bg; int _cx, _cy, _bw, _bh, _n;

int okN = 0, susN = 0;
var rows = new List<string>();

foreach (var c in CAND)
{
    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(c.path);
    if (pf == null) { sb.AppendLine(c.tag.PadRight(22) + "  [缺 prefab] " + c.path); susN++; continue; }

    var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
    go.transform.position = basePos;
    go.transform.rotation = Quaternion.identity;
    var systems = go.GetComponentsInChildren<ParticleSystem>(true);

    var shaders = new List<string>();
    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        foreach (var m in r.sharedMaterials)
            if (m != null && m.shader != null && !shaders.Contains(m.shader.name)) shaders.Add(m.shader.name);
    int nNonUrp = 0;
    foreach (var s in shaders) if (!s.StartsWith("Universal Render Pipeline/")) nNonUrp++;

    int bestN = -1; float bestT = T_SAMPLES[0]; byte[] bestPng = null; float bestFill = 0f;

    foreach (var t in T_SAMPLES)
    {
        foreach (var ps in systems) ps.Simulate(t, false, true);

        // ── 自校准取景：第 0 次用固定大框，之后按上一帧实测的内容包围盒收紧 ──
        Vector2 center = new Vector2(basePos.x, basePos.y);
        float halfH = 14f;
        for (int it = 0; it < MARGIN.Length; it++)
        {
            cam.transform.position = new Vector3(center.x, center.y, basePos.z - 60f);
            cam.orthographicSize = halfH;
            RenderAndMeasure(out _bg, out _cx, out _cy, out _bw, out _bh, out _n);
            if (_n < 12 || _bw == 0) break;
            // 像素 → 世界（正交 + 相机朝 +Z 时是纯线性映射）
            float pxToW = 2f * halfH / H;
            center = new Vector2(center.x + (_cx - W * 0.5f) * pxToW,
                                 center.y + (_cy - H * 0.5f) * pxToW);
            float halfNeed = Mathf.Max(_bw, _bh) * 0.5f * pxToW;
            halfH = Mathf.Max(0.02f, halfNeed * MARGIN[it]);
        }
        // 最后一次 RenderAndMeasure 得到的就是终帧
        cam.transform.position = new Vector3(center.x, center.y, basePos.z - 60f);
        cam.orthographicSize = halfH;
        RenderAndMeasure(out _bg, out _cx, out _cy, out _bw, out _bh, out _n);

        float fill = (float)_n / (W * H);
        if (_n > bestN) { bestN = _n; bestT = t; bestFill = fill; bestPng = tex.EncodeToPNG(); }
    }

    if (bestPng != null) File.WriteAllBytes(DIR + "/" + c.tag + ".png", bestPng);

    string sh = shaders.Count == 0 ? "(无渲染器)" : string.Join(" | ", shaders.ToArray());
    sb.AppendLine(c.tag.PadRight(22) + c.group.PadRight(6)
                  + bestT.ToString("F2").PadRight(10) + bestN.ToString().PadLeft(7) + "  "
                  + (bestFill * 100f).ToString("F2").PadLeft(6) + "%  "
                  + systems.Length.ToString().PadLeft(3) + "    "
                  + (nNonUrp == 0 ? "[全URP] " : "[非URP " + nNonUrp + "] ") + sh);
    rows.Add(c.tag + "\t" + c.group + "\t" + bestT.ToString("F2") + "\t" + bestN);
    if (bestN > 800) okN++; else susN++;

    UnityEngine.Object.DestroyImmediate(go);
}

cam.targetTexture = null;
UnityEngine.Object.DestroyImmediate(rt);
UnityEngine.Object.DestroyImmediate(camGo);
UnityEngine.Object.DestroyImmediate(lightGo);

sb.AppendLine();
sb.AppendLine("渲染有内容（内容像素 > 800）：" + okN + " 个；过稀/失败：" + susN + " 个");
sb.AppendLine("★ 过稀的两种可能：① Simulate 在编辑模式对该 prefab 无效 ② 该特效本来就稀疏（如远处的符文光点）。");
sb.AppendLine("★ 「非URP」= 材质着色器不是 Universal Render Pipeline/* ⇒ 拖进场景是洋红，只能取它的贴图。");
sb.AppendLine("★ 「占画幅%」= 内容像素占全画幅比例，太低说明取景仍偏松（看图上应该占 5%~40%）。");

Directory.CreateDirectory(Path.GetDirectoryName(RP));
File.WriteAllText(RP, sb.ToString(), new UTF8Encoding(false));
return "渲染 " + CAND.Length + " 个 prefab，有内容 " + okN + "，过稀 " + susN + "；报告 Tools/reports/vfx_sheet.txt";
