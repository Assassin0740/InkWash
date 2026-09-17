using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// ev_dragon.cs —— 给墨龙加朱砂红水墨描边（与六个敌人同一套阵营标识）
//
// 用户：「墨龙也改了吧」
//
// 现状：龙 _OutlineColor = (0.042, 0.038, 0.036) 暗褐 —— 在它自己 −0.34 浓墨的身体上
//       几乎看不出来；宽度 0.018 已与敌人一致，无需动。
// 目标：只换 _OutlineColor 为朱砂线性 (0.48, 0.035, 0.022)，与六个敌人逐位相同。
//
// ★ 该材质被 3 个 prefab 引用（Z_Enemy_MoLong / Z_Dragon[嵌套] / Z_Dragon_LP[孤立无引用]）
//   ⇒ 改一份等于三处同时变，报告里列出，避免"我明明只改了一个"的错觉。
// ★ 落盘确证不能只看内存对象回读（那是同一个 Material 实例）——
//   必须读 .mat **文件文本**，确认字节真的写进磁盘。
public class ev_dragon
{
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/ev_dragon.txt";
    const string MatPath    = "Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat";
    const string DiskPath   = "D:/Unity Project/InkWash/Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat";
    const bool   APPLY      = true;

    static readonly Color RED = new Color(0.48f, 0.035f, 0.022f, 1f);  // 朱砂（线性，与敌人同）
    const float W = 0.018f;

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== ev_dragon —— 墨龙朱砂阵营描边 =====");
        sb.AppendLine("APPLY = " + (APPLY ? "true（落盘）" : "false（预演）"));
        sb.AppendLine();

        var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (m == null) { sb.AppendLine("[X] 载入失败 " + MatPath); return sb.ToString(); }

        Color old = m.HasProperty("_OutlineColor") ? m.GetColor("_OutlineColor") : Color.black;
        sb.AppendLine("改前（内存对象）：");
        sb.AppendLine("   _OutlineColor = (" + F(old.r) + ", " + F(old.g) + ", " + F(old.b) + ")"
                      + "   _OutlineWidth = " + m.GetFloat("_OutlineWidth").ToString("F4")
                      + "   _OutlineDry = " + m.GetFloat("_OutlineDry").ToString("F3"));
        sb.AppendLine();

        if (APPLY)
        {
            m.SetColor("_OutlineColor", RED);
            m.SetFloat("_OutlineWidth", W);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ★ 落盘确证：直接读磁盘文件文本
        sb.AppendLine("落盘确证（读 .mat 文件文本）：");
        try
        {
            string txt = File.ReadAllText(DiskPath);
            var mo = Regex.Match(txt, @"_OutlineColor:\s*\{r:\s*([-\d.]+),\s*g:\s*([-\d.]+),\s*b:\s*([-\d.]+)");
            var mw = Regex.Match(txt, @"_OutlineWidth:\s*([-\d.]+)");
            sb.AppendLine("   _OutlineColor = " + (mo.Success ? "(" + mo.Groups[1].Value + ", " + mo.Groups[2].Value + ", " + mo.Groups[3].Value + ")" : "<未找到>"));
            sb.AppendLine("   _OutlineWidth = " + (mw.Success ? mw.Groups[1].Value : "<未找到>"));
            bool hitR = mo.Success && Mathf.Abs(float.Parse(mo.Groups[1].Value) - RED.r) < 0.001f;
            sb.AppendLine(hitR ? "   [OK] 朱砂红确已写入磁盘" : "   [X] 磁盘上不是目标值");
        }
        catch (IOException e) { sb.AppendLine("   [X] 读盘失败 " + e.Message); }

        // 连带影响
        sb.AppendLine();
        sb.AppendLine("连带影响（该材质被下列资产引用，改一份等于三处同变）：");
        sb.AppendLine("   Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab   <- 正式 BOSS");
        sb.AppendLine("   Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab          <- 被上面嵌套引用（同一视觉）");
        sb.AppendLine("   Assets/_Project/Prefabs/Ziyuan/Z_Dragon_LP.prefab       <- 孤立资产，工程内无引用者");

        // 反向确认：其他阵营标识不受影响
        sb.AppendLine();
        sb.AppendLine("反向确认（主角 / 剑 / 六个敌人 的描边色应保持原值）：");
        string[] others =
        {
            "Assets/_Project/Art/Materials/M_Character_Ink.mat",
            "Assets/_Project/Art/Materials/M_W_Sword_Ink.mat",
            "Assets/_Project/Art/Materials/M_Ink_Enemy_MoShan.mat",
            "Assets/_Project/Art/Materials/M_Ink_Enemy_MoGu.mat",
        };
        foreach (string p in others)
        {
            var mm = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (mm == null) { sb.AppendLine("   " + Path.GetFileName(p) + "  <不存在>"); continue; }
            Color c = mm.HasProperty("_OutlineColor") ? mm.GetColor("_OutlineColor") : Color.black;
            bool isRed = Mathf.Abs(c.r - RED.r) < 0.001f && Mathf.Abs(c.g - RED.g) < 0.001f;
            sb.AppendLine("   " + mm.name.PadRight(24)
                + " (" + F(c.r) + ", " + F(c.g) + ", " + F(c.b) + ")"
                + (isRed ? "  ← 朱砂（敌人，符合预期）" : "  ← 原值未动"));
        }

        return sb.ToString();
    }

    static string F(float v) { return v.ToString("F3"); }
}

string r = ev_dragon.Run();
Debug.Log(r);
try { File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/ev_dragon.txt", r); } catch { }
return "EV_DRAGON_DONE";
