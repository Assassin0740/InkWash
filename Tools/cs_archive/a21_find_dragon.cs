// a21_find_dragon.cs —— 查 EnemyDragon 到底在不在，以及有哪些 Enemy* 类型
//
// a20 用 Type.GetType("..., Assembly-CSharp") 返回 null。有两种可能：
//   (a) 类型真没编译进去（有编译错误）
//   (b) 类型在别的程序集里（本项目代码可能挂在 asmdef 下，不在 Assembly-CSharp）
// 必须区分开 —— 否则我会去改一个根本没问题的东西。
//
// 做法：遍历所有已加载程序集，把名字里带 Enemy 的类型全列出来。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

sb.AppendLine("========== a21 枚举所有含 'Enemy' 的类型 ==========");
sb.AppendLine();

int totalAsm = 0, totalMatch = 0;
var asmNames = new List<string>();
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    totalAsm++;
    Type[] types;
    try { types = asm.GetTypes(); }
    catch (Exception) { continue; }

    var hits = new List<string>();
    foreach (var t in types)
    {
        if (t == null) continue;
        if (t.Name.Contains("Enemy") || t.Name.Contains("Dragon"))
            hits.Add("    " + t.FullName);
    }
    if (hits.Count > 0)
    {
        totalMatch += hits.Count;
        asmNames.Add(asm.GetName().Name);
        sb.AppendLine("--- 程序集 " + asm.GetName().Name + "（" + hits.Count + " 个命中）---");
        foreach (var h in hits) sb.AppendLine(h);
        sb.AppendLine();
    }
}

sb.AppendLine("扫描程序集总数 = " + totalAsm + "  命中类型 = " + totalMatch);
sb.AppendLine("含命中的程序集: " + string.Join(", ", asmNames.ToArray()));

// 专门报 EnemyDragon
sb.AppendLine();
sb.AppendLine("---- 专门找 EnemyDragon ----");
bool found = false;
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    Type[] types;
    try { types = asm.GetTypes(); } catch (Exception) { continue; }
    foreach (var t in types)
        if (t.Name == "EnemyDragon")
        {
            sb.AppendLine("  ✓ 找到: " + t.FullName + "  在程序集 " + asm.GetName().Name);
            found = true;
        }
}
if (!found) sb.AppendLine("  ★ 所有已加载程序集里都没有 EnemyDragon ⇒ 编译没发生或失败");

// 单独列出项目自己的程序集（名字带 Assembly-CSharp 或 InkWash）
sb.AppendLine();
sb.AppendLine("---- 项目相关程序集 ----");
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    string n = asm.GetName().Name;
    if (n.Contains("Assembly-CSharp") || n.Contains("InkWash"))
    {
        int cnt = 0;
        try { cnt = asm.GetTypes().Length; } catch (Exception) { cnt = -1; }
        sb.AppendLine("  " + n + "  类型数=" + cnt);
    }
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a21_find_dragon.txt"), sb.ToString());
Debug.Log("[a21]\n" + sb.ToString());
