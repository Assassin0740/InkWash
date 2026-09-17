// a43_wire_boss.cs —— 加第四波：墨龙 Boss
//
// 用户要求「很多的战斗」，龙是这套素材里唯一的 Boss 级敌人（6 m 长），
// 所以放在**最后**，前面三波积累后才压上来。
//
// ★ 与 a15 的关键区别：a15 是**替换**，本脚本是**追加** ——
//   必须保留前三波原样，只在末尾 append 一波，否则会把上一轮成果冲掉。
//   做法：先读出现有波次（场景序列化值），构造新数组 = 原有 + 新增。
//
// ★ 同时也补上 `breathProjectilePrefab` 的绑定：`EnemyDragon.SpawnBreath()`
//   依赖它，没配的话龙息只会"张嘴不吐"（它自己会 LogWarning，但那是在运行时，
//   编译期/编辑器期都看不出来）⇒ **必须在建 prefab 时就显式写上**。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";

sb.AppendLine("========== a43 加第四波·墨龙 Boss ==========");

var P_MoLong = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Z_Enemy_MoLong.prefab");
var P_MoShan = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Z_Enemy_MoShan.prefab");
var P_MoGuai = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Z_Enemy_MoGuai.prefab");
var P_MoGu   = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Z_Enemy_MoGu.prefab");
var P_InkProj = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Ink_Projectile.prefab");

if (P_MoLong == null) { sb.AppendLine("★ 读不到 Z_Enemy_MoLong.prefab"); W(); Debug.LogError("[a43]"); return; }
sb.AppendLine("  Z_Enemy_MoLong = ok");
sb.AppendLine("  Ink_Projectile = " + (P_InkProj != null ? "ok" : "★ null（龙息将没有弹药）"));

// ---------- 先把墨弹绑到龙的 EnemyDragon 上 ----------
if (P_InkProj != null)
{
    var c = PrefabUtility.LoadPrefabContents(EnemyDir + "/Z_Enemy_MoLong.prefab");
    try
    {
        var d = c.GetComponent<InkWash.Enemies.EnemyDragon>();
        if (d != null)
        {
            d.breathProjectilePrefab = P_InkProj;
            d.breathProjectileSpeed = 16f;
            d.breathProjectileDamage = 14f;
            d.breathProjectileCount = 3;
            d.breathSpreadDeg = 9f;
            PrefabUtility.SaveAsPrefabAsset(c, EnemyDir + "/Z_Enemy_MoLong.prefab");
            sb.AppendLine("  已把 Ink_Projectile 绑到 EnemyDragon.breathProjectilePrefab ✓");
        }
        else sb.AppendLine("  ★ Z_Enemy_MoLong 上没有 EnemyDragon 组件");
    }
    finally { PrefabUtility.UnloadPrefabContents(c); }
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
}

// ---------- 追加第四波 ----------
var scene = EditorSceneManager.GetActiveScene();
sb.AppendLine();
sb.AppendLine("场景 = " + scene.name);

var spawner = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
if (spawner == null) { sb.AppendLine("★ 场景里找不到 WaveSpawner"); W(); Debug.LogError("[a43]"); return; }

sb.AppendLine();
sb.AppendLine("---- 改前 ----");
DumpWaves(spawner, sb);

// 保留现有波次
var old = spawner.waves ?? new InkWash.Enemies.Wave[0];
var list = new List<InkWash.Enemies.Wave>();
foreach (var w in old) if (w != null) list.Add(w);

// 幂等：如果已经有"墨龙"这一波，先去掉再重加
list.RemoveAll(w => w.label != null && w.label.Contains("墨龙"));

list.Add(new InkWash.Enemies.Wave
{
    label = "第四波·墨龙",
    delayBefore = 3.0f,
    entries = new[]
    {
        new InkWash.Enemies.EnemySpawnEntry { prefab = P_MoLong, count = 1 },
    },
});

spawner.waves = list.ToArray();
EditorUtility.SetDirty(spawner);
EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);

sb.AppendLine();
sb.AppendLine("---- 改后 ----");
DumpWaves(spawner, sb);

spawner.waves = list.ToArray();
EditorUtility.SetDirty(spawner);
EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);

AssetDatabase.SaveAssets();

// ---------- 复核：从磁盘重开场景读序列化值（不是读内存同一个对象）----------
sb.AppendLine();
sb.AppendLine("---- 复核：从磁盘重开场景 ----");
EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity", OpenSceneMode.Single);
var sp2 = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();
if (sp2 == null) sb.AppendLine("  ★ 重开后找不到 WaveSpawner");
else DumpWaves(sp2, sb);

W();
Debug.Log("[a43]\n" + sb.ToString());

void DumpWaves(InkWash.Enemies.WaveSpawner sp, StringBuilder s)
{
    if (sp.waves == null) { s.AppendLine("  (waves 为 null)"); return; }
    s.AppendLine("  波数 = " + sp.waves.Length);
    for (int i = 0; i < sp.waves.Length; i++)
    {
        var w = sp.waves[i];
        if (w == null) { s.AppendLine("  [" + i + "] ★ null"); continue; }
        var parts = new List<string>();
        if (w.entries != null)
            foreach (var e in w.entries)
                parts.Add((e == null || e.prefab == null ? "★null" : e.prefab.name) + " x" + (e == null ? 0 : e.count));
        s.AppendLine("  [" + i + "] '" + w.label + "' delay=" + w.delayBefore
                     + "  → " + string.Join(" + ", parts.ToArray()));
    }
}

void W() => File.WriteAllText(Path.Combine(root, "Tools/reports/a43_wire_boss.txt"), sb.ToString());
