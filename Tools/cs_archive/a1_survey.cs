// a1_survey.cs —— 接战斗前的现状盘点
//
// 目标：在写任何代码之前，把"敌人系统当前长什么样"一次性摸清。
//   1. 三个 Enemy_Mo* prefab 各挂了什么组件、参数是多少
//   2. 三个 AnimatorController 的完整状态/参数/转义（换骨架必然要重指 clip）
//   3. WaveSpawner 当前 waves 配置（哪个 prefab 出现在第几波）
//   4. Ziyuan prefab 的 Animator / 骨骼名（接战斗要配的）
//
// ★ 本工程的 Codely 脚本编译坑（csc 比日常写法严）：
//   - 字符串插值里**不能直接写三元表达式**（CS8361，`:` 会终止插值）⇒ 必须加括号
//   - `System` 与 `UnityEngine` 都有 `Object` ⇒ 显式写 `UnityEngine.Object`
//   - 格式说明符不能带尾随空格（CS8088）
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using InkWash.Enemies;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

string N(string s) => string.IsNullOrEmpty(s) ? "null" : s;
string Q(bool b) => b ? "Y" : "N";

// ---------- 1) 敌人 prefab ----------
sb.AppendLine("================ 1) 敌人 prefab ================");
foreach (var path in new[]
{
    "Assets/_Project/Prefabs/Enemies/Enemy_MoOu.prefab",
    "Assets/_Project/Prefabs/Enemies/Enemy_MoTu.prefab",
    "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab",
})
{
    sb.AppendLine();
    sb.AppendLine("--- " + Path.GetFileName(path) + " ---");

    var content = PrefabUtility.LoadPrefabContents(path);
    if (content == null) { sb.AppendLine("★ 读不到"); continue; }

    var trs = content.GetComponentsInChildren<Transform>(true);
    sb.AppendLine("  层级（共 " + trs.Length + " 个 Transform）:");
    foreach (var t in trs)
    {
        int d = 0; var p = t.parent;
        while (p != null) { d++; p = p.parent; }
        string depth = new string(' ', Mathf.Min(24, d * 2));

        var names = new StringBuilder();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) { names.Append("[Missing] "); continue; }
            names.Append(c.GetType().Name);
            if (c is EnemyBase eb)
                names.Append(" {hp=").Append(eb.maxHealth)
                     .Append(" sight=").Append(eb.sightRange)
                     .Append(" atkRange=").Append(eb.attackRange)
                     .Append(" windup=").Append(eb.attackWindup)
                     .Append(" spd=").Append(eb.chaseSpeed).Append('/').Append(eb.walkSpeed)
                     .Append('}');
            if (c is Animator an)
                names.Append(" [ctrl=").Append(an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "null")
                     .Append(" avatar=").Append(an.avatar != null ? an.avatar.name : "null")
                     .Append(" human=").Append(Q(an.avatar != null && an.avatar.isHuman))
                     .Append(']');
            names.Append(' ');
        }
        sb.AppendLine("    " + depth + t.name + "  <" + names + ">");
    }

    sb.AppendLine("  渲染器材质:");
    foreach (var r in content.GetComponentsInChildren<Renderer>(true))
        foreach (var m in r.sharedMaterials)
            sb.AppendLine("    " + r.name + " -> " + N(m != null ? m.name : null));

    PrefabUtility.UnloadPrefabContents(content);
}

// ---------- 2) AnimatorController ----------
sb.AppendLine();
sb.AppendLine("================ 2) 敌人 AnimatorController ================");

void DumpSM(StringBuilder b, AnimatorStateMachine sm, int indent)
{
    if (sm == null) return;
    string pad = new string(' ', indent * 2);
    foreach (var cs in sm.states)
    {
        var s = cs.state;
        string mot;
        if (s.motion is AnimationClip c) mot = c.name;
        else if (s.motion is BlendTree bt) mot = "BlendTree:" + bt.name;
        else mot = s.motion == null ? "null" : s.motion.name;
        b.AppendLine(pad + "STATE '" + s.name + "'  motion=" + mot + "  speed=" + s.speed);
    }
    foreach (var sub in sm.stateMachines)
        b.AppendLine(pad + "SUBSM '" + sub.stateMachine.name + "'");

    foreach (var t in sm.anyStateTransitions)
        b.AppendLine(pad + "ANY-> '" + DumpDest(t) + "' cond=" + DumpConds(t));

    foreach (var cs in sm.states)
        foreach (var t in cs.state.transitions)
            b.AppendLine(pad + "'" + cs.state.name + "' -> '" + DumpDest(t) + "' cond=" + DumpConds(t) + " hasExit=" + Q(t.hasExitTime));
}

string DumpDest(AnimatorTransitionBase t)
{
    if (t.destinationState != null) return t.destinationState.name;
    if (t.destinationStateMachine != null) return t.destinationStateMachine.name;
    return "?";
}

string DumpConds(AnimatorTransitionBase t)
{
    var s = new StringBuilder();
    foreach (var c in t.conditions)
        s.Append(c.parameter).Append('[').Append(c.mode).Append("] ");
    return s.Length == 0 ? "(none)" : s.ToString();
}

foreach (var path in new[]
{
    "Assets/_Project/Animations/Enemies/EnemyMelee.controller",
    "Assets/_Project/Animations/Enemies/EnemyElite.controller",
    "Assets/_Project/Animations/Enemies/EnemyRanged.controller",
})
{
    var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
    if (ac == null) { sb.AppendLine("★ 读不到 " + path); continue; }
    sb.AppendLine();
    sb.AppendLine("--- " + Path.GetFileName(path) + " ---");

    sb.AppendLine("  参数:");
    foreach (var prm in ac.parameters)
        sb.AppendLine("    [" + prm.type + "] " + prm.name);

    sb.AppendLine("  层级:");
    foreach (var lyr in ac.layers)
    {
        string def = lyr.stateMachine != null && lyr.stateMachine.defaultState != null ? lyr.stateMachine.defaultState.name : "null";
        sb.AppendLine("    Layer '" + lyr.name + "' (default=" + def + ")");
        DumpSM(sb, lyr.stateMachine, 3);
    }
}

// ---------- 3) WaveSpawner 当前配置 ----------
sb.AppendLine();
sb.AppendLine("================ 3) WaveSpawner 场景实例 ================");
var spawners = UnityEngine.Object.FindObjectsOfType<WaveSpawner>(true);
sb.AppendLine("找到 " + spawners.Length + " 个 WaveSpawner");
foreach (var sp in spawners)
{
    sb.AppendLine("--- " + sp.gameObject.name + " (active=" + Q(sp.gameObject.activeInHierarchy) + ") ---");
    sb.AppendLine("  autoStart=" + Q(sp.autoStart) + " interval=" + sp.spawnInterval
                  + " center=" + sp.spawnCenter + " radius=" + sp.spawnRadius
                  + " minDist=" + sp.minDistanceToPlayer);
    if (sp.waves == null) { sb.AppendLine("  waves = null"); continue; }
    for (int i = 0; i < sp.waves.Length; i++)
    {
        var w = sp.waves[i];
        sb.AppendLine("  Wave[" + i + "] '" + w.label + "' delay=" + w.delayBefore);
        if (w.entries == null) continue;
        foreach (var e in w.entries)
            sb.AppendLine("      " + (e != null && e.prefab != null ? e.prefab.name : "null")
                          + " x" + (e != null ? e.count : 0));
    }
}

// ---------- 4) Ziyuan prefab 的 Animator / 骨骼 ----------
sb.AppendLine();
sb.AppendLine("================ 4) Ziyuan prefab 的 Animator / 骨骼 ================");
foreach (var zn in new[] { "Z_Orge", "Z_Orc", "Z_Undead", "Z_Dragon", "Z_Dragon_LP" })
{
    string zpath = "Assets/_Project/Prefabs/Ziyuan/" + zn + ".prefab";
    var content = PrefabUtility.LoadPrefabContents(zpath);
    sb.AppendLine("--- " + zn + " ---");
    if (content == null) { sb.AppendLine("  ★ 读不到 " + zpath); continue; }

    var an = content.GetComponentInChildren<Animator>(true);
    if (an == null) sb.AppendLine("  (无 Animator)");
    else
    {
        sb.AppendLine("  controller=" + (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "null"));
        sb.AppendLine("  avatar=" + (an.avatar != null ? an.avatar.name : "null")
                      + " isHuman=" + Q(an.avatar != null && an.avatar.isHuman)
                      + " valid=" + Q(an.avatar != null && an.avatar.isValid));
    }

    var smr = content.GetComponentInChildren<SkinnedMeshRenderer>(true);
    if (smr != null && smr.bones != null && smr.bones.Length > 0)
    {
        sb.AppendLine("  骨骼数=" + smr.bones.Length + "，前 20 个名字:");
        var line = new StringBuilder("      ");
        for (int i = 0; i < Mathf.Min(20, smr.bones.Length); i++)
            line.Append(smr.bones[i] != null ? smr.bones[i].name : "null").Append(' ');
        sb.AppendLine(line.ToString());
    }
    else sb.AppendLine("  (无 SkinnedMeshRenderer)");

    // 攻击片段候选：列 FBX 里所有 AnimationClip
    sb.AppendLine("  可用片段（从 FBX 读）:");
    var rend = content.GetComponentInChildren<SkinnedMeshRenderer>(true);
    PrefabUtility.UnloadPrefabContents(content);
}

// ---------- 5) 各 FBX 里的片段清单 ----------
sb.AppendLine();
sb.AppendLine("================ 5) Ziyuan FBX 内片段清单 ================");
foreach (var f in Directory.GetFiles(Path.Combine(Application.dataPath, "ThirdParty/Ziyuan/_fbx"), "*.fbx"))
{
    string ap = "Assets/ThirdParty/Ziyuan/_fbx/" + Path.GetFileName(f);
    var clips = AssetDatabase.LoadAllAssetsAtPath(ap);
    sb.AppendLine("--- " + Path.GetFileName(f) + " ---");
    foreach (var o in clips)
        if (o is AnimationClip cl)
            sb.AppendLine("    clip '" + cl.name + "'  len=" + cl.length.ToString("F3") + "s  legacy=" + Q(cl.legacy) + "  loop=" + Q(cl.isLooping));
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a1_survey.txt"), sb.ToString());
Debug.Log("[a1] 调查完成，报告 " + sb.Length + " 字符");
