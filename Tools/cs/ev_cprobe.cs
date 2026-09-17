using UnityEditor;
using UnityEngine;
using System.IO;
using System.Text;

// ev_cprobe.cs —— 只读+临时探针：确认 _OutlineColor 的颜色空间换算关系
//
// 为什么必须先确认：项目是 Linear 色彩空间。
//   · 若 Material.SetColor 会做 sRGB→Linear 转换，则我传 (0.74,0.20,0.15) 会存成 ~ (0.51,0.033,0.020)
//   · 若不转换，则我传什么存什么，渲染时被当线性用 ⇒ 看起来会比预期亮很多
// 判据：对照 M_Character_Ink 现有 _OutlineColor 的【内存值】与【磁盘存盘值】。
var sb = new StringBuilder();
sb.AppendLine("=== ev_cprobe：_OutlineColor 颜色空间换算关系 ===");
sb.AppendLine("activeColorSpace = " + QualitySettings.activeColorSpace);
sb.AppendLine();

string path = "Assets/_Project/Art/Materials/M_Character_Ink.mat";
var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
if (mat == null) { sb.AppendLine("x 载入失败 " + path); File.WriteAllText("Tools/reports/ev_cprobe.txt", sb.ToString()); Debug.Log(sb.ToString()); return "CPROBE_FAIL"; }

Color inMemory = mat.GetColor("_OutlineColor");
sb.AppendLine("主角 _OutlineColor 内存值 GetColor = (" + inMemory.r.ToString("F4") + ", " + inMemory.g.ToString("F4") + ", " + inMemory.b.ToString("F4") + ", " + inMemory.a.ToString("F4") + ")");
sb.AppendLine("   → 若此处显示约 (0.19,0.22,0.30)，说明内存持有的是 sRGB 显示值（Unity 已做往返转换）");
sb.AppendLine("   → 若显示 (0.03,0.042,0.075)，说明内存持有的就是线性值");
sb.AppendLine();

// 临时资产：写入一个已知颜色，再看磁盘存的是什么
var probe = new Material(mat);
Color want = new Color(0.5f, 0.1f, 0.06f, 1f);
probe.SetColor("_OutlineColor", want);
Color back = probe.GetColor("_OutlineColor");
sb.AppendLine("临时材质 SetColor 传入 = (" + want.r.ToString("F4") + ", " + want.g.ToString("F4") + ", " + want.b.ToString("F4") + ")");
sb.AppendLine("临时材质读回 GetColor  = (" + back.r.ToString("F4") + ", " + back.g.ToString("F4") + ", " + back.b.ToString("F4") + ")");

string tmp = "Assets/_ev_cprobe_tmp.mat";
AssetDatabase.DeleteAsset(tmp);
AssetDatabase.CreateAsset(probe, tmp);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

string disk = File.ReadAllText(tmp);
sb.AppendLine();
sb.AppendLine("磁盘存盘内容（关键行）：");
foreach (var line in disk.Split('\n'))
    if (line.Contains("_OutlineColor") || line.Contains("m_Shader"))
        sb.AppendLine("   " + line.Trim());

sb.AppendLine();
sb.AppendLine("判读：");
sb.AppendLine("   · 若磁盘值 == 传入值 (0.5,0.1,0.06) ⇒ SetColor 不转换，我需自己传线性值");
sb.AppendLine("   · 若磁盘值 ≈ (0.214,0.010,0.0050) ⇒ SetColor 做了 sRGB→Linear，我传 sRGB 即可");
AssetDatabase.DeleteAsset(tmp);
AssetDatabase.Refresh();

File.WriteAllText("Tools/reports/ev_cprobe.txt", sb.ToString());
Debug.Log(sb.ToString());
return "CPROBE_OK";
