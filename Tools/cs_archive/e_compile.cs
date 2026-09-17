// e_compile.cs —— 强制把新写的 .cs 编进 Assembly-CSharp，并**显式**报告编译结果
//   坑：桥送审的脚本 DLL 时间戳可能早于源文件，Refresh 不一定触发重编，
//   而 Console 里"什么都没有"看起来像"编译通过" —— 其实是根本没编。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string[] paths = {
        "Assets/_Project/Scripts/Combat/DamageInfo.cs",
        "Assets/_Project/Scripts/Combat/IDamageable.cs",
        "Assets/_Project/Scripts/Combat/Hitbox.cs",
        "Assets/_Project/Scripts/Combat/HitStop.cs",
        "Assets/_Project/Scripts/Combat/PlayerRef.cs",
        "Assets/_Project/Scripts/Player/PlayerHealth.cs",
        "Assets/_Project/Scripts/Player/PlayerSwordHitbox.cs",
        "Assets/_Project/Scripts/Enemies/EnemyBase.cs",
        "Assets/_Project/Scripts/Enemies/EnemyMelee.cs",
        "Assets/_Project/Scripts/Enemies/EnemyRanged.cs",
        "Assets/_Project/Scripts/Enemies/EnemyElite.cs",
        "Assets/_Project/Scripts/Enemies/InkProjectile.cs",
        "Assets/_Project/Scripts/Enemies/WaveSpawner.cs",
        "Assets/_Project/Scripts/Enemies/RoomController.cs",
    };
    yield return null; yield return null;

    foreach (var p in paths)
    {
        if (AssetDatabase.LoadAssetAtPath<MonoScript>(p) == null)
            sb.AppendLine("[缺失] " + p);
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    }
    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    CompilationPipeline.RequestScriptCompilation();
    sb.AppendLine("[forcecompile] 已请求重编");
    yield return null;

    // 等编译真正结束（最多 30 s）
    float t = Time.realtimeSinceStartup;
    while (EditorApplication.isCompiling && Time.realtimeSinceStartup - t < 30f) yield return null;
    while (EditorUtility.scriptCompilationFailed ||
           (!EditorApplication.isCompiling && Time.realtimeSinceStartup - t < 1.2f)) yield return null;

    sb.AppendLine("srciptCompilationFailed = " + EditorUtility.scriptCompilationFailed);
    sb.AppendLine("isCompiling = " + EditorApplication.isCompiling);

    // 类型是否真的可见（比"Console 无红字"强得多）
    string[] types = {
        "InkWash.Combat.Faction", "InkWash.Combat.DamageInfo", "InkWash.Combat.IDamageable",
        "InkWash.Combat.Hitbox", "InkWash.Combat.HitStop", "InkWash.Combat.PlayerRef",
        "InkWash.Player.PlayerHealth", "InkWash.Player.PlayerSwordHitbox",
        "InkWash.Enemies.EnemyBase", "InkWash.Enemies.EnemyMelee", "InkWash.Enemies.EnemyRanged",
        "InkWash.Enemies.EnemyElite", "InkWash.Enemies.InkProjectile",
        "InkWash.Enemies.WaveSpawner", "InkWash.Enemies.RoomController",
    };
    var asm = System.AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
    sb.AppendLine("Assembly-CSharp = " + (asm != null ? "已加载" : "未找到"));
    if (asm != null)
        foreach (var tn in types)
        {
            var ty = asm.GetType(tn);
            sb.AppendLine("  " + tn + " → " + (ty != null ? "OK" : "**未找到**"));
        }

    System.IO.File.WriteAllText(System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Application.dataPath), "Tools/reports/e_compile.txt"), sb.ToString());
    Debug.Log("[e_compile] done  failed=" + EditorUtility.scriptCompilationFailed);
    yield return null;
}

return Body();
