// s5_build.cs —— Sprint 5（Roguelike 循环 + 水墨特效）资产落地（编辑态，一次跑完）：
//   ① 生成 10 条技能资产（SkillData SO）→ Assets/_Project/Data/Skills/
//   ② Player 预制体挂 PlayerStats / SkillInventory / LevelSystem / InkLandingBloom
//   ③ 场景建 [Systems]（RunManager + SkillChoicePanel）与 [InkVfx]（InkHitVfx）
//   ④ 把 InkBloomFeature（墨晕）装配进 URP 渲染器资产
//   ⑤ 保存
//
// 为什么技能做成资产而不是代码里的常量表（D2 用户故事）：
//   论文要能展示"技能池是可配置的数据"，而且新增一条技能不该需要改代码、重编译。
//   同时 `id` 是稳定键 —— 层数与存档都以它为准，改名不断档。
//
// 本脚本**幂等**：已存在的技能资产按 id 原地更新字段，不重复创建
//   （重复 CreateAsset 会生成新 GUID，把 RunManager.skillPool 里的引用全打断）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using InkWash.Roguelike;
using InkWash.UI;
using InkWash.Effects;
using InkWash.Rendering;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var scene = SceneManager.GetActiveScene();
    sb.AppendLine("场景 = " + scene.name);
    sb.AppendLine("===== S5 Roguelike 循环 + 水墨特效 资产落地 =====");

    string dataDir = "Assets/_Project/Data";
    string skillDir = dataDir + "/Skills";
    string prefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";

    EnsureFolder("Assets/_Project");
    EnsureFolder(dataDir);
    EnsureFolder(skillDir);

    // ---------------- ① 技能资产 ----------------
    sb.AppendLine();
    sb.AppendLine("---- ① 技能资产 ----");

    var defs = new (string id, string name, string desc, SkillRarity rar, float w, int max,
                    StatKind st, float val, bool pct, string tag)[]
    {
        // 普通：权重刻意拉开（1.2 / 1.0 / 0.8 / 0.7），验收要拿同稀有度验证"权重真的起作用"
        ("skill_damage",  "墨锋", "刀锋凝墨，伤害提升 {v}（叠满共 {t}）",
            SkillRarity.Common, 1.2f, 5, StatKind.DamageBonus,     0.15f, true,  "攻"),
        ("skill_speed",   "踏雪", "身法轻捷，移动速度提升 {v}（叠满共 {t}）",
            SkillRarity.Common, 1.0f, 5, StatKind.MoveSpeedBonus,  0.12f, true,  "动"),
        ("skill_health",  "铁骨", "气血充盈，生命上限 +{v}（叠满共 +{t}）",
            SkillRarity.Common, 0.8f, 5, StatKind.MaxHealthBonus,  18f,   false, "生"),
        ("skill_atkspd",  "疾风", "出手如风，攻击速度提升 {v}（叠满共 {t}）",
            SkillRarity.Common, 0.7f, 4, StatKind.AttackSpeedBonus, 0.15f, true, "攻"),

        // 稀有
        ("skill_crit",    "点睛", "点睛成锋，暴击率提升 {v}（叠满共 {t}）",
            SkillRarity.Rare,   1.0f, 5, StatKind.CritChance,      0.08f, true,  "攻"),
        ("skill_leech",   "饮月", "刃上留情，命中回复相当于伤害的 {v}（叠满共 {t}）",
            SkillRarity.Rare,   1.0f, 4, StatKind.Lifesteal,       0.05f, true,  "生"),
        ("skill_dash",    "缩地", "缩地成寸，冲刺冷却缩短 {v}（叠满共 {t}）",
            SkillRarity.Rare,   1.0f, 4, StatKind.DashCooldownCut, 0.12f, true,  "动"),

        // 史诗
        ("skill_critdmg", "破军", "一动破军，暴击伤害额外 +{v}（叠满共 +{t}）",
            SkillRarity.Epic,   1.0f, 3, StatKind.CritDamage,      0.35f, false, "攻"),
        ("skill_killheal","回春", "杀伐起死，每击杀回复 {v} 点生命（叠满共 {t}）",
            SkillRarity.Epic,   1.0f, 3, StatKind.KillHeal,        6f,    false, "生"),
        ("skill_knock",   "倒海", "力可倒海，击退距离提升 {v}（叠满共 {t}）",
            SkillRarity.Epic,   1.0f, 3, StatKind.KnockbackBonus,  0.40f, true,  "攻"),
    };

    var assets = new List<SkillData>();
    int created = 0, updated = 0;
    foreach (var d in defs)
    {
        string path = skillDir + "/Skill_" + d.id + ".asset";
        var so = AssetDatabase.LoadAssetAtPath<SkillData>(path);
        bool isNew = so == null;
        if (isNew) so = ScriptableObject.CreateInstance<SkillData>();

        so.id = d.id;
        so.displayName = d.name;
        so.description = d.desc;
        so.rarity = d.rar;
        so.weight = d.w;
        so.maxStacks = d.max;
        so.stat = d.st;
        so.valuePerStack = d.val;
        so.showAsPercent = d.pct;
        so.tags = string.IsNullOrEmpty(d.tag) ? new string[0] : new[] { d.tag };

        if (isNew) { AssetDatabase.CreateAsset(so, path); created++; }
        else { EditorUtility.SetDirty(so); updated++; }
        assets.Add(so);
    }
    AssetDatabase.SaveAssets();
    sb.AppendLine("  技能资产 " + assets.Count + " 条（新建 " + created + " / 更新 " + updated + "）→ " + skillDir);
    foreach (var a in assets)
        sb.AppendLine("    · " + a.id + "  " + a.displayName + "  " + a.RarityName
                      + "  w=" + a.weight + "  上限" + a.maxStacks
                      + "  " + a.stat + " +" + a.valuePerStack);

    // ---------------- ② Player 预制体 ----------------
    sb.AppendLine();
    sb.AppendLine("---- ② Player 预制体组件 ----");
    var root = PrefabUtility.LoadPrefabContents(prefabPath);
    if (root == null) { sb.AppendLine("  ** 打不开 " + prefabPath); }
    else
    {
        var ps = GetOrAdd<PlayerStats>(root);
        var si = GetOrAdd<SkillInventory>(root);
        var ls = GetOrAdd<LevelSystem>(root);
        var lb = GetOrAdd<InkLandingBloom>(root);

        si.stats = ps;
        ls.stats = ps;
        lb.player = root.GetComponent<InkWash.Player.PlayerController>();

        // 消费方的引用也显式接上（代码里虽然会自动找，但显式赋值能把"找错对象"变成看得见的事实）
        var pc = root.GetComponent<InkWash.Player.PlayerController>();
        var ph = root.GetComponent<InkWash.Player.PlayerHealth>();
        if (pc != null) pc.stats = ps;
        if (ph != null) ph.stats = ps;
        var sh = root.GetComponentInChildren<InkWash.Player.PlayerSwordHitbox>(true);
        if (sh != null) { sh.stats = ps; sh.health = ph; }

        sb.AppendLine("  PlayerStats=" + (ps != null) + "  SkillInventory=" + (si != null)
                      + "  LevelSystem=" + (ls != null) + "  InkLandingBloom=" + (lb != null));
        sb.AppendLine("  接引用：Controller=" + (pc != null) + "  Health=" + (ph != null)
                      + "  SwordHitbox=" + (sh != null));

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        sb.AppendLine("  已保存 " + prefabPath);
    }

    // ---------------- ③ 场景对象 ----------------
    sb.AppendLine();
    sb.AppendLine("---- ③ 场景对象 ----");
    var playerGo = GameObject.Find("Player");
    var run = GetOrAddScene<RunManager>("[Systems]");
    var choice = run.gameObject.GetComponent<SkillChoicePanel>();
    if (choice == null) choice = run.gameObject.AddComponent<SkillChoicePanel>();

    run.choicePanel = choice;
    run.skillPool = assets;
    run.autoStartRun = false;      // 默认停在主菜单；验收/录屏显式调 StartRun()，避免动画测试被刷怪干扰
    run.roomsToClear = 3;

    if (playerGo != null)
    {
        run.playerHealth = playerGo.GetComponent<InkWash.Player.PlayerHealth>();
        run.inventory = playerGo.GetComponent<SkillInventory>();
        run.level = playerGo.GetComponent<LevelSystem>();
    }
    run.room = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.RoomController>();
    run.spawner = UnityEngine.Object.FindObjectOfType<InkWash.Enemies.WaveSpawner>();

    var ink = GetOrAddScene<InkHitVfx>("[InkVfx]");

    sb.AppendLine("  [Systems] RunManager 技能池=" + (run.skillPool != null ? run.skillPool.Count : 0)
                  + "  面板=" + (run.choicePanel != null)
                  + "  PlayerHealth=" + (run.playerHealth != null)
                  + "  Room=" + (run.room != null) + "  Spawner=" + (run.spawner != null)
                  + "  Inventory=" + (run.inventory != null) + "  Level=" + (run.level != null));
    sb.AppendLine("  [InkVfx] InkHitVfx 池=" + ink.poolSize);
    EditorUtility.SetDirty(run);
    EditorUtility.SetDirty(choice);
    EditorUtility.SetDirty(ink);

    // ---------------- ④ 装配墨晕 Feature ----------------
    sb.AppendLine();
    sb.AppendLine("---- ④ 装配 InkBloomFeature ----");
    foreach (var rpath in new[] { "Assets/Settings/URP-HighFidelity-Renderer.asset",
                                  "Assets/Settings/URP-Balanced-Renderer.asset" })
        AttachBloom(sb, rpath);

    // ---------------- ⑤ 保存 ----------------
    EditorSceneManager.MarkSceneDirty(scene);
    bool saved = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + saved);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/s5_build.txt"), sb.ToString());
    Debug.Log("[s5_build] done");
    yield return null;
}

// ======================================================================
// 工具
// ======================================================================

T GetOrAdd<T>(GameObject go) where T : Component
{
    var c = go.GetComponent<T>();
    return c != null ? c : go.AddComponent<T>();
}

void EnsureFolder(string path)
{
    if (AssetDatabase.IsValidFolder(path)) return;
    string parent = Path.GetDirectoryName(path).Replace('\\', '/');
    if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
    AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
}

T GetOrAddScene<T>(string name) where T : Component
{
    var go = GameObject.Find(name);
    if (go == null) go = new GameObject(name);
    return GetOrAdd<T>(go);
}

/// 把墨晕 Feature 追加进渲染器资产。**幂等**：已存在就跳过。
/// 与 s4_build 的 AttachFeatures 同一套写法，包括 m_RendererFeatureMap 的 localFileID ——
/// 少写 map 那一项，编辑器重启后 Feature 顺序会乱（甚至报 renderer feature null）。
void AttachBloom(StringBuilder sb, string rendererPath)
{
    var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
    if (data == null) { sb.AppendLine("  ** 找不到 " + rendererPath); return; }

    var so = new SerializedObject(data);
    var feats = so.FindProperty("m_RendererFeatures");
    var map = so.FindProperty("m_RendererFeatureMap");
    if (feats == null || map == null) { sb.AppendLine("  ** " + rendererPath + " 结构异常"); return; }

    bool hasBloom = false;
    for (int i = 0; i < feats.arraySize; i++)
    {
        // as 而不是 is：UnityEngine.Object 的 `is` 在对象被销毁时会给出误导性结果
        var f = feats.GetArrayElementAtIndex(i).objectReferenceValue;
        if (f as InkBloomFeature != null) hasBloom = true;
    }

    if (!hasBloom)
    {
        var bloom = ScriptableObject.CreateInstance<InkBloomFeature>();
        bloom.name = "Ink Bloom 墨晕扩散";
        AssetDatabase.AddObjectToAsset(bloom, data);
        feats.arraySize++;
        feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = bloom;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(bloom, out string _, out long localId);
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        sb.AppendLine("  " + Path.GetFileName(rendererPath) + " ← 追加 InkBloomFeature");
    }
    else
    {
        sb.AppendLine("  " + Path.GetFileName(rendererPath) + " 已有 InkBloomFeature，跳过");
    }

    so.ApplyModifiedPropertiesWithoutUndo();
    data.SetDirty();
    EditorUtility.SetDirty(data);
    AssetDatabase.SaveAssets();

    sb.AppendLine("  " + Path.GetFileName(rendererPath) + " 现有 Feature " + feats.arraySize
                  + " 个 / map " + map.arraySize
                  + (feats.arraySize == map.arraySize ? "  [一致]" : "  [** 不一致，顺序会乱]"));
}

return Body();
