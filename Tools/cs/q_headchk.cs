// q_headchk.cs —— 只读核对「headAlignPitchDeg」三层是否一致（源码默认值 / prefab 序列化值 / 运行时实例值）
//
// 为什么要三层一起看（坑表「静默失效」）：
//   · 改 .cs 默认值 ⇒ Prom 的 prefab 覆盖会让它不生效
//   · 只改 prefab   ⇒ 源码默认值腐烂，下一个人 clone 出来行为不同
//   · 改完 .cs 若没发生域重载 ⇒ Play 里读到的还是旧程序集
//   ⇒ 三个来源分开报，谁没跟上就一眼看出。
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

var sb = new StringBuilder();
sb.AppendLine("isPlaying = " + EditorApplication.isPlaying);

// ① 源码默认值
const string CS = "Assets/_Project/Scripts/Enemies/EnemyDragon.cs";
var txt = File.ReadAllText(CS);
var m = Regex.Match(txt, @"public\s+float\s+headAlignPitchDeg\s*=\s*([0-9.]+)f");
sb.AppendLine("① 源码默认值        = " + (m.Success ? m.Groups[1].Value : "解析失败"));

// ② prefab 上的序列化值
const string PF = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
var go = AssetDatabase.LoadAssetAtPath<GameObject>(PF);
if (go == null) sb.AppendLine("② prefab 加载失败   = " + PF);
else
{
    System.Type dt = null;
    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
    { var t = a.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { dt = t; break; } }
    if (dt == null) sb.AppendLine("② EnemyDragon 类型找不到");
    else
    {
        var comp = go.GetComponent(dt);
        if (comp == null) sb.AppendLine("② prefab 上没有 EnemyDragon 组件");
        else
        {
            var so = new SerializedObject(comp);
            foreach (var name in new[] { "headAlignPitchDeg", "headAlignLinks", "hoverStationary",
                                         "tailFollowBodyWave", "tailFollowGain", "swimWaveEnvPow" })
            {
                var p = so.FindProperty(name);
                if (p == null) { sb.AppendLine("② " + name + " : 属性不存在"); continue; }
                string v = p.propertyType == SerializedPropertyType.Float ? p.floatValue.ToString("F3")
                         : p.propertyType == SerializedPropertyType.Integer ? p.intValue.ToString()
                         : p.propertyType == SerializedPropertyType.Boolean ? p.boolValue.ToString()
                         : p.propertyType.ToString();
                sb.AppendLine("② prefab." + name + " = " + v);
            }
        }
    }
}

// ③ 运行时实例（Play 中才有）
if (EditorApplication.isPlaying)
{
    System.Type dt = null;
    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
    { var t = a.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { dt = t; break; } }
    if (dt != null)
    {
        var f = dt.GetField("headAlignPitchDeg",
                            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        var all = UnityEngine.Object.FindObjectsOfType(dt, true);
        sb.AppendLine("③ 运行时 EnemyDragon 实例数 = " + all.Length);
        foreach (var o in all)
        {
            var c = o as Component;
            sb.AppendLine("   " + c.gameObject.name + "  headAlignPitchDeg = "
                          + (f != null ? f.GetValue(o).ToString() : "字段缺失"));
        }
    }
}
return sb.ToString();
