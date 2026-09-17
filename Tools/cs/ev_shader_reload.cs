using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 强制重新导入 InkCharacter.shader，并用反射调 ShaderUtil.GetShaderMessages 读编译消息。
// ★ HLSL 错误不进 Console，只能这样查；查不到也要靠渲染判据兜底（品红 = 编译失败）。
const string P = "Assets/_Project/Art/Shaders/InkCharacter.shader";
AssetDatabase.ImportAsset(P, ImportAssetOptions.ForceUpdate);
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

var sh = AssetDatabase.LoadAssetAtPath<Shader>(P);
var sb = new StringBuilder();
sb.AppendLine("===== ev_shader_reload =====");
sb.AppendLine("shader = " + (sh == null ? "NULL" : sh.name) + "   isSupported = " + (sh != null && sh.isSupported));

if (sh != null)
{
    try
    {
        var mi = typeof(ShaderUtil).GetMethod("GetShaderMessages",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            null, new Type[] { typeof(Shader) }, null);
        if (mi != null)
        {
            var arr = mi.Invoke(null, new object[] { sh }) as Array;
            int cnt = arr == null ? 0 : arr.Length;
            sb.AppendLine("GetShaderMessages 条数 = " + cnt);
            if (cnt > 0)
            {
                foreach (var e in arr)
                {
                    var t = e.GetType();
                    var fm = t.GetField("message"); var fs = t.GetField("severity"); var fp = t.GetField("platform");
                    sb.AppendLine("  [" + (fs != null ? fs.GetValue(e).ToString() : "?") + "] "
                        + (fp != null ? fp.GetValue(e).ToString() : "?") + " : "
                        + (fm != null ? fm.GetValue(e).ToString() : "?"));
                }
            }
            else sb.AppendLine("★ 无编译消息 ⇒ shader 干净");
        }
        else sb.AppendLine("GetShaderMessages 反射未命中（改用渲染判据）");
    }
    catch (Exception ex) { sb.AppendLine("反射异常：" + ex.Message); }

    // 顺带确认新属性真的注册进 shader 了
    var tmp = new Material(sh);
    sb.AppendLine("HasProperty(_HueKeep) = " + tmp.HasProperty("_HueKeep")
                + "   HasProperty(_HueSat) = " + tmp.HasProperty("_HueSat"));
    if (tmp.HasProperty("_HueKeep"))
        sb.AppendLine("_HueKeep 默认值 = " + tmp.GetFloat("_HueKeep").ToString("F3")
                    + "   _HueSat 默认值 = " + tmp.GetFloat("_HueSat").ToString("F3"));
    UnityEngine.Object.DestroyImmediate(tmp);
}

Debug.Log(sb.ToString());
try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/ev_shader.txt", sb.ToString()); } catch { }
return "SHADER_RELOADED";
