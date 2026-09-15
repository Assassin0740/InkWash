// r_shadercheck.cs —— 按项目硬规矩查 HLSL 编译（Console 干净 ≠ 编译成功）
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEditor;

public class RShaderCheck
{
    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("isCompiling=" + EditorApplication.isCompiling
                    + " compFailed=" + EditorUtility.scriptCompilationFailed);
        string[] names = { "InkWash/InkSurface", "InkWash/InkCharacter", "InkWash/InkSlash",
                           "Hidden/InkWash/InkPaper", "Hidden/InkWash/InkEdge", "Hidden/InkWash/InkBloom",
                           "InkWash/InkSky", "InkWash/InkSplash" };
        bool allOk = true;
        foreach (var n in names)
        {
            var sh = Shader.Find(n);
            if (sh == null) { sb.AppendLine("!! 找不到 " + n); allOk = false; continue; }
            int err = 0, warn = 0;
            foreach (var msg in ShaderUtil.GetShaderMessages(sh))
            {
                string sev = msg.GetType().GetProperty("severity").GetValue(msg).ToString();
                string txt = msg.GetType().GetProperty("message").GetValue(msg).ToString();
                string pl  = "";
                try { pl = " line " + msg.GetType().GetProperty("line").GetValue(msg); } catch { }
                if (sev.Contains("Error")) { err++; allOk = false; sb.AppendLine("   [ERR] " + n + " " + txt + pl); }
                else { warn++; sb.AppendLine("   [warn] " + n + " " + txt + pl); }
            }
            sb.AppendLine(n.PadRight(22) + " passCount=" + sh.passCount + "  error=" + err + "  warning=" + warn);
        }
        // InkSurface 场景材质的真实 pass 数
        foreach (var p in new[] { "Assets/_Project/Art/Materials/M_Whitebox_Ground.mat",
                                  "Assets/_Project/Art/Materials/M_Character_Ink.mat" })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null) sb.AppendLine(p + " shader=" + m.shader.name + " passCount=" + m.passCount);
        }
        sb.AppendLine(">>> " + (allOk ? "全部 shader 无错误" : "**有 shader 编译错误**"));
        return sb.ToString();
    }
}
return RShaderCheck.Run();
