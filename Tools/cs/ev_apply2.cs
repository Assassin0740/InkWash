using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// ev_apply2.cs —— 给六个敌人材质落盘 _HueKeep / _HueSat（固有色保留）
//
// 背景：shader 原设计"去色"（病 5）确实压住了视线干扰，但连长在模型上的**部位本色**
// 一起抹掉了 —— 腿/身体/武器全被统一成墨阶表那一族色。用户要求"不要剥夺"。
// 新增参数 _HueKeep（默认 0 = 旧行为）逐像素把贴图色相注回，明度仍由墨阶决定。
//
// ★ 只写敌人六个材质；主角 / 龙 / 武器 / 场景一律不写（保持 _HueKeep = 0）。
public class ev_apply2
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/ev_apply2.txt";

    static readonly string[] Mats =
    {
        "Assets/_Project/Art/Materials/M_Ink_Enemy_MoShan.mat",
        "Assets/_Project/Art/Materials/M_Ink_Enemy_MoGuai.mat",
        "Assets/_Project/Art/Materials/M_Ink_Enemy_MoGu.mat",
        "Assets/_Project/Art/Materials/M_Ink_Enemy_MoOu_0.mat",
        "Assets/_Project/Art/Materials/M_Ink_Enemy_MoTu_0.mat",
        "Assets/_Project/Art/Materials/M_Ink_Enemy_MoYan_0.mat",
    };

    public static string Run()
    {
        const float HK = 1.00f;   // 固有色保留：1.0 = 各部位本色全显
        const float HS = 1.00f;   // 饱和度：1.0 = 贴图原样

        var sb = new StringBuilder();
        sb.AppendLine("===== ev_apply2 —— 落盘 _HueKeep / _HueSat =====");
        sb.AppendLine("目标 _HueKeep = " + HK.ToString("F2") + "   _HueSat = " + HS.ToString("F2"));
        sb.AppendLine();

        int ok = 0;
        foreach (string p in Mats)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { sb.AppendLine("[X] 载入失败 " + p); continue; }
            if (!m.HasProperty("_HueKeep") || !m.HasProperty("_HueSat"))
            {
                sb.AppendLine("[X] shader 未提供 _HueKeep/_HueSat：" + m.name);
                continue;
            }
            m.SetFloat("_HueKeep", HK);
            m.SetFloat("_HueSat", HS);
            EditorUtility.SetDirty(m);
            ok++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        sb.AppendLine("落盘完成 " + ok + "/" + Mats.Length + " 份，回读确认：");
        foreach (string p in Mats)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { sb.AppendLine("   " + Path.GetFileName(p) + "  <载入失败>"); continue; }
            sb.AppendLine("   " + m.name.PadRight(24)
                + " _HueKeep=" + m.GetFloat("_HueKeep").ToString("F2")
                + "  _HueSat=" + m.GetFloat("_HueSat").ToString("F2")
                + "  _ChromaKeep=" + m.GetFloat("_ChromaKeep").ToString("F2")
                + "  _BandBias=" + m.GetFloat("_BandBias").ToString("F2"));
        }

        // 反向确认：主角 / 龙 / 剑必须仍是 0
        sb.AppendLine();
        sb.AppendLine("反向确认（必须全为 0.00）：");
        string[] others =
        {
            "Assets/_Project/Art/Materials/M_Character_Ink.mat",
            "Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat",
            "Assets/_Project/Art/Materials/M_W_Sword_Ink.mat",
        };
        foreach (string p in others)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { sb.AppendLine("   " + Path.GetFileName(p) + "  <不存在>"); continue; }
            float v = m.HasProperty("_HueKeep") ? m.GetFloat("_HueKeep") : -999f;
            sb.AppendLine("   " + m.name.PadRight(24) + " _HueKeep=" + (v < -100 ? "<无属性>" : v.ToString("F2"))
                + (v == 0f ? "  OK" : "  ★异常"));
        }

        return sb.ToString();
    }
}

string r = ev_apply2.Run();
Debug.Log(r);
try { File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/ev_apply2.txt", r); } catch { }
return "EV_APPLY2_DONE";
