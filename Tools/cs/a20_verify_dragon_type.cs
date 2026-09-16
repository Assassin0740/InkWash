// a20_verify_dragon_type.cs —— 证明 EnemyDragon 真的编译进已加载程序集
//
// 为什么不能只看 read_console：本项目硬规矩 7 —— "编译新鲜度 = 新成员在不在已加载 Assembly 里"。
//   `refresh` 的返回值、空的控制台都不足以证明。唯一可靠的做法是**用反射去查类型**。
//   查不到 ⇒ 说明编译没发生（或失败），此时任何"接下来测一下"都是在测旧代码。
//
// 顺带把 EnemyDragon 的关键参数打出来，确认默认值生效（硬规矩 6：C# 默认值改了要复核）。
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

var t = Type.GetType("InkWash.Enemies.EnemyDragon, Assembly-CSharp");
sb.AppendLine("========== a20 验证 EnemyDragon 类型已加载 ==========");
sb.AppendLine("Type.GetType('InkWash.Enemies.EnemyDragon, Assembly-CSharp') = " + (t == null ? "★ null（没编译进去）" : t.FullName));

if (t != null)
{
    sb.AppendLine("基类 = " + t.BaseType.FullName);
    sb.AppendLine("是 abstract = " + t.IsAbstract);

    sb.AppendLine();
    sb.AppendLine("---- 公开字段默认值 ----");
    var go = new GameObject("__tmp_dragon_probe");
    var comp = go.AddComponent(t);
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
    {
        object v = null;
        try { v = f.GetValue(comp); } catch (Exception e) { v = "★" + e.GetType().Name; }
        string vs = v == null ? "null" : v.ToString();
        if (v is UnityEngine.Object uo) vs = uo == null ? "null" : uo.name;
        sb.AppendLine("  " + f.FieldType.Name.PadRight(16) + " " + f.Name.PadRight(28) + " = " + vs);
    }

    sb.AppendLine();
    sb.AppendLine("---- 重写的方法（应当只有 ChooseAttack / AttackMovement / PerformHit / TakeDamage / OnDied / OnEnterState / Awake）----");
    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
        sb.AppendLine("  " + m.ReturnType.Name.PadRight(10) + " " + m.Name);

    UnityEngine.Object.DestroyImmediate(go);
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a20_verify_dragon_type.txt"), sb.ToString());
Debug.Log("[a20]\n" + sb.ToString());
