using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text;

// ev_apply.cs —— 落盘：六个敌人材质 → 「有色 + 红色水墨描边」
//
// 用户要求：敌人也要有颜色、接近主角；再加红色水墨边标识阵营。
//
// ■ 数值依据（ev_scan 实测，非拍脑袋）
//   _ChromaKeep 是乘数，效果取决于各贴图自身彩度 ⇒ 已实测扫描（受光 0°，_BandBias=+0.10）：
//     墨山 MoShan  0.30→14.1  0.45→21.4  0.70→23.7  0.98→26.5（单调，未饱和）
//     墨徒 MoGuai  0.28→11.3  0.45→15.3  0.70→16.0  0.98→16.7（0.70 起饱和）
//     墨骨 MoGu    0.22→ 8.5  0.45→11.2  0.70→11.3  0.98→11.5（0.45 即饱和）
//     墨偶 MoOu    0.12→ 9.4  0.45→12.6  0.70→11.5  0.98→12.1（0.45 即饱和）
//     墨魇 MoYan   0.12→ 9.2  0.45→11.2  0.70→ 9.8  0.98→ 9.4（0.45 即饱和）
//   主角参照：受光/背光彩度 15.9~20.5
//   ⇒ 统一取 _ChromaKeep = 0.45：全部落入饱和区，彩度 11~21，与主角同量级；
//     再往上只增加过曝风险（墨龙当年 _ChromaKeep 0.45 就过曝成彩虹，不得不压到 0.10）。
//
//   _BandBias 是「这个物体用几号墨」的显式分配，负值加在 saturate 之前 ⇒
//     暗部一路被推到最浓一阶 = 塌成焦墨黑块（纯黑占比 31%~52%）。
//   按《美术风格规范》§9.20：明暗差需压到 0.1 量级。
//   ⇒ 统一 +0.10（主角 +0.15，差 0.05），保留「敌人比主角略沉」的层级。
//
//   _OutlineColor 改朱砂红。★ 已实测：Material.SetColor 在 Linear 空间下【不做】色彩空间转换，
//     传入值原样存盘、原样在 shader 里当线性值用（主角现有 (0.03,0.042,0.075) 即是线性值）。
//     故此处直接写线性值 (0.35, 0.030, 0.020)，换算成 sRGB 约 #A03026 —— 暗朱砂、印泥色。
//     亮度（线性）约 0.098，为主角描边 0.042 的 2.3 倍：足够当识别符号，又不至于抢过主体。
//
//   _OutlineWidth 统一 0.018（原 Ziyuan 系 0.014 / KayKit 系 0.020 两套不一致）。
//     作为阵营标识，粗细需成体系；0.018 对应屏幕约 7 px，与墨龙同级。
//
// ★ 本脚本把改动写进磁盘资产；执行前请确认 Unity 不在 Play 模式。

System.Func<Material, string, string> Get = (m, p) => m.HasProperty(p) ? m.GetFloat(p).ToString("F3") : "<无>";
System.Func<Material, string, string> Col = (m, p) =>
{
    if (!m.HasProperty(p)) return "<无>";
    var c = m.GetColor(p);
    return "(" + c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3") + ")";
};

float CK  = 0.45f;
float BB  = 0.00f;
float OLW = 0.018f;
Color RED = new Color(0.48f, 0.035f, 0.022f, 1f);

var targets = new string[]
{
    "M_Ink_Enemy_MoShan",
    "M_Ink_Enemy_MoGuai",
    "M_Ink_Enemy_MoGu",
    "M_Ink_Enemy_MoOu_0",
    "M_Ink_Enemy_MoTu_0",
    "M_Ink_Enemy_MoYan_0",
};

var sb = new StringBuilder();
sb.AppendLine("=== ev_apply：六个敌人材质落盘 ===");
sb.AppendLine("目标  _ChromaKeep=" + CK.ToString("F2") + "  _BandBias=" + BB.ToString("F2")
              + "  _OutlineColor=" + RED.r.ToString("F3") + "/" + RED.g.ToString("F3") + "/" + RED.b.ToString("F3")
              + "  _OutlineWidth=" + OLW.ToString("F3"));
sb.AppendLine("activeColorSpace = " + QualitySettings.activeColorSpace
              + "   isPlaying = " + EditorApplication.isPlaying);
sb.AppendLine();

int ok = 0, miss = 0;
foreach (string n in targets)
{
    var guids = AssetDatabase.FindAssets("t:Material " + n);
    if (guids.Length == 0) { sb.AppendLine("x " + n + " : 材质未找到"); miss++; continue; }

    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { sb.AppendLine("x " + n + " : 加载失败 " + path); miss++; continue; }

    string old = "  _ChromaKeep=" + Get(m, "_ChromaKeep")
               + " _BandBias=" + Get(m, "_BandBias")
               + " _OLW=" + Get(m, "_OutlineWidth")
               + " _OutlineColor=" + Col(m, "_OutlineColor");

    if (m.HasProperty("_ChromaKeep"))   m.SetFloat("_ChromaKeep", CK);
    if (m.HasProperty("_BandBias"))     m.SetFloat("_BandBias", BB);
    if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", OLW);
    if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", RED);

    EditorUtility.SetDirty(m);
    sb.AppendLine("√ " + n);
    sb.AppendLine("     旧:" + old);
    sb.AppendLine("     新:  _ChromaKeep=" + Get(m, "_ChromaKeep")
                  + " _BandBias=" + Get(m, "_BandBias")
                  + " _OLW=" + Get(m, "_OutlineWidth")
                  + " _OutlineColor=" + Col(m, "_OutlineColor"));
    ok++;
}

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

sb.AppendLine();
sb.AppendLine("落盘完成：成功 " + ok + " / 缺失 " + miss);

sb.AppendLine();
sb.AppendLine("── 磁盘回读核对（_OutlineColor 应为 0.35/0.03/0.02）──");
foreach (string n in targets)
{
    var gs = AssetDatabase.FindAssets("t:Material " + n);
    if (gs.Length == 0) continue;
    string p = AssetDatabase.GUIDToAssetPath(gs[0]);
    foreach (var line in File.ReadAllLines(p))
    {
        string t = line.Trim();
        if (t.StartsWith("- _ChromaKeep") || t.StartsWith("- _BandBias")
            || t.StartsWith("- _OutlineColor") || t.StartsWith("- _OutlineWidth"))
            sb.AppendLine("   " + n.PadRight(24) + t);
    }
}

string s = sb.ToString();
File.WriteAllText("Tools/reports/ev_apply.txt", s);
Debug.Log(s);
return "EV_APPLY_OK ok=" + ok + " miss=" + miss;
