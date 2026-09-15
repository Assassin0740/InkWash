// e_types.cs —— 只查类型可见性（不触发重编，避免域重载把自己的脚本打断）
//
// ★ 一个必须一起看的指标：**编译结果是否过期**。
//   编译失败时 Unity **不会卸载上一次成功的 Assembly-CSharp**，所以 `asm.GetType(...)` 依然
//   全部返回 OK —— 只查类型会得到一个"全绿"的假通过（本轮就踩了：
//   scriptCompilationFailed=True 的同时 16 个类型全是 OK）。
//   因此这里额外比对 DLL 的写入时间与最新源文件的写入时间，DLL 更旧即判定为过期。
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new StringBuilder();
    yield return null; yield return null;
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    var asm = System.AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
    sb.AppendLine("Assembly-CSharp = " + (asm != null ? "已加载" : "未找到"));
    sb.AppendLine("scriptCompilationFailed = " + EditorUtility.scriptCompilationFailed);

    // ---- 过期检查 ----
    string dll = Path.Combine(projRoot, "Library/ScriptAssemblies/Assembly-CSharp.dll");
    var csFiles = Directory.Exists(Path.Combine(Application.dataPath, "_Project"))
        ? Directory.GetFiles(Path.Combine(Application.dataPath, "_Project"), "*.cs", SearchOption.AllDirectories)
        : new string[0];
    if (File.Exists(dll) && csFiles.Length > 0)
    {
        var dllTime = File.GetLastWriteTime(dll);
        string newest = csFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
        var srcTime = File.GetLastWriteTime(newest);
        bool stale = srcTime > dllTime;
        sb.AppendLine("DLL   写入 = " + dllTime.ToString("HH:mm:ss"));
        sb.AppendLine("最新源   = " + srcTime.ToString("HH:mm:ss") + "   " + Path.GetFileName(newest));
        sb.AppendLine("编译结果 = " + (stale ? "**过期（DLL 比源文件旧 ⇒ 下面的 OK 不可信）**" : "最新"));
    }
    else
    {
        sb.AppendLine("DLL = " + (File.Exists(dll) ? "存在" : "**不存在**") + "   源文件数 = " + csFiles.Length);
    }

    string[] types = {
        "InkWash.Combat.Faction", "InkWash.Combat.DamageInfo", "InkWash.Combat.IDamageable",
        "InkWash.Combat.Hitbox", "InkWash.Combat.HitStop", "InkWash.Combat.PlayerRef",
        "InkWash.Player.PlayerHealth", "InkWash.Player.PlayerSwordHitbox",
        "InkWash.Enemies.EnemyBase", "InkWash.Enemies.EnemyMelee", "InkWash.Enemies.EnemyRanged",
        "InkWash.Enemies.EnemyElite", "InkWash.Enemies.InkProjectile",
        "InkWash.Enemies.WaveSpawner", "InkWash.Enemies.RoomController",
        // 验收台本身：它引用了上面全部类型，一旦编译不过整条 Assembly-CSharp 都会消失
        "InkWash.Utils.PlaytestHarness",
        // Sprint 4 水墨风
        "InkWash.Rendering.InkEdgeFeature", "InkWash.Rendering.InkPaperFeature",
        "InkWash.Rendering.InkFullScreenPass", "InkWash.Rendering.InkStyleRegistry",
        "InkWash.UI.InkStylePanel", "InkWash.UI.InkMaterialSwap",
        // Sprint 5 Roguelike 循环 + 水墨特效
        "InkWash.Roguelike.StatKind", "InkWash.Roguelike.PlayerStats",
        "InkWash.Roguelike.SkillData", "InkWash.Roguelike.SkillPool",
        "InkWash.Roguelike.SkillInventory", "InkWash.Roguelike.LevelSystem",
        "InkWash.Roguelike.RunState", "InkWash.Roguelike.RunManager",
        "InkWash.UI.SkillChoicePanel",
        "InkWash.Rendering.InkBloomFeature",
        "InkWash.Effects.InkHitVfx", "InkWash.Effects.InkLandingBloom",
    };
    if (asm != null)
        foreach (var tn in types)
        {
            var ty = asm.GetType(tn);
            sb.AppendLine("  " + tn + " → " + (ty != null ? "OK" : "**未找到**"));
        }

    // ---- 新加的成员单独点名：类型存在不代表方法存在（增量改动最容易漏的就是这里）----
    sb.AppendLine("关键成员：");
    Check(sb, asm, "InkWash.Enemies.WaveSpawner", "ResetForTest");
    Check(sb, asm, "InkWash.Enemies.WaveSpawner", "Begin");
    Check(sb, asm, "InkWash.Enemies.RoomController", "ResetForTest");
    Check(sb, asm, "InkWash.Player.PlayerHealth", "ClearInvincibility");
    Check(sb, asm, "InkWash.Utils.PlaytestHarness", "S3EnemyFlow");
    Check(sb, asm, "InkWash.Utils.PlaytestHarness", "S4InkFlow");
    // Sprint 5
    Check(sb, asm, "InkWash.Utils.PlaytestHarness", "S5RoguelikeFlow");
    Check(sb, asm, "InkWash.Combat.HitStop", "ForceEnd");
    Check(sb, asm, "InkWash.Roguelike.PlayerStats", "RollDamage");
    Check(sb, asm, "InkWash.Roguelike.PlayerStats", "Add");
    Check(sb, asm, "InkWash.Roguelike.SkillPool", "Draw");
    Check(sb, asm, "InkWash.Roguelike.SkillInventory", "Acquire");
    Check(sb, asm, "InkWash.Roguelike.LevelSystem", "GrantXp");
    Check(sb, asm, "InkWash.Roguelike.RunManager", "StartRun");
    Check(sb, asm, "InkWash.Roguelike.RunManager", "SetState");
    Check(sb, asm, "InkWash.UI.SkillChoicePanel", "InjectChoice");
    Check(sb, asm, "InkWash.Enemies.EnemyBase", "AnyDied");
    Check(sb, asm, "InkWash.Enemies.EnemyBase", "xpReward");
    Check(sb, asm, "InkWash.Player.PlayerController", "CurrentAttackSpeed");
    Check(sb, asm, "InkWash.Rendering.InkStyleRegistry", "AllRegistered");
    Check(sb, asm, "InkWash.Effects.InkHitVfx", "Spawn");
    Check(sb, asm, "InkWash.Effects.InkLandingBloom", "BloomCount");

    // ---- 着色器是否真的编译通过（Shader.Find 只找得到「编译成功」的 shader）----
    sb.AppendLine("着色器：");
    // ★ 这份清单本身就是一条"防回归"：`InkWash/InkSlash` 曾经**从来不存在**，
    //   于是刀光静默退回 URP/Unlit（不支持顶点色）→ 弧面变成硬边色块。
    //   把实际用到的 shader 名字全列进来，缺失就当场暴露，而不是靠肉眼看出画面不对。
    foreach (var sn in new[] { "InkWash/InkCharacter", "InkWash/InkSurface", "InkWash/InkSky",
                               "InkWash/InkSlash",
                               "Hidden/InkWash/InkEdge", "Hidden/InkWash/InkPaper",
                               "Hidden/InkWash/InkBloom", "InkWash/InkSplash" })
    {
        var sh = Shader.Find(sn);
        sb.AppendLine("  " + sn + " → " + (sh != null ? "OK  pass=" + sh.passCount : "**找不到（没编译出来？）**"));
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_types.txt"), sb.ToString());
    Debug.Log("[e_types] done");
    yield return null;
}

void Check(StringBuilder sb, System.Reflection.Assembly asm, string typeName, string member)
{
    if (asm == null) return;
    var t = asm.GetType(typeName);
    if (t == null) { sb.AppendLine("  " + typeName + "." + member + " → **类型缺失**"); return; }
    var m = t.GetMember(member, System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.Static);
    sb.AppendLine("  " + typeName + "." + member + " → " + (m != null && m.Length > 0 ? "OK" : "**缺失**"));
}

return Body();
