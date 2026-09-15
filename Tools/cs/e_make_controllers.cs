// e_make_controllers.cs —— 生成三种敌人的 AnimatorController（编辑态）
//   参数面统一为 { Speed(float), Attack(trigger), Attack2(trigger), Hit(trigger), Dead(trigger) }
//   —— 统一参数面是为了让 EnemyBase 只用一套 SetTrigger/SetFloat 代码驱动所有敌人。
//   循环副本：KayKit 的 Rig_Large 片段 loopTime=False（Rig_Medium 的已经是对的），
//   直接塞进 Idle/Run 状态会在播完后卡住不动 —— 所以按项目既有约定生成循环副本。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    string ctrlDir = "Assets/_Project/Animations/Enemies";
    string loopDir = "Assets/_Project/Animations/Enemies/Loops";
    Directory.CreateDirectory(Path.Combine(projRoot, ctrlDir));
    Directory.CreateDirectory(Path.Combine(projRoot, loopDir));
    AssetDatabase.Refresh();

    // ---------- 工具 ----------
    AnimationClip Find(string fbx, string clip)
    {
        var arr = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>();
        return arr.FirstOrDefault(c => c.name == clip);
    }

    AnimationClip LoopCopy(AnimationClip src, string name)
    {
        if (src == null) return null;
        if (src.isLooping) return src;
        string path = loopDir + "/" + name + ".anim";
        var exist = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (exist != null) return exist;
        var c = Object.Instantiate(src);
        c.name = name;
        var s = AnimationUtility.GetAnimationClipSettings(c);
        s.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(c, s);
        AssetDatabase.CreateAsset(c, path);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    string MED = "Assets/ThirdParty/KayKit/Animations/Rig_Medium/";
    string LRG = "Assets/ThirdParty/KayKit/Animations/Rig_Large/";

    // ---------- 敌人素材（近战 / 远程 / 精英）----------
    var meleeIdle = Find(MED + "Rig_Medium_General.fbx", "Idle_A");
    var meleeRun = Find(MED + "Rig_Medium_MovementBasic.fbx", "Running_A");
    var meleeAtk = Find(MED + "Rig_Medium_CombatMelee.fbx", "Melee_1H_Attack_Slice_Diagonal");
    var meleeHit = Find(MED + "Rig_Medium_General.fbx", "Hit_A");
    var meleeDie = Find(MED + "Rig_Medium_General.fbx", "Death_A");

    var rngIdle = Find(MED + "Rig_Medium_General.fbx", "Idle_B");
    var rngRun = Find(MED + "Rig_Medium_MovementBasic.fbx", "Walking_B");
    var rngAtk = Find(MED + "Rig_Medium_CombatRanged.fbx", "Ranged_Magic_Spellcasting");
    var rngHit = Find(MED + "Rig_Medium_General.fbx", "Hit_B");
    var rngDie = Find(MED + "Rig_Medium_General.fbx", "Death_B");

    var eliIdle = LoopCopy(Find(LRG + "Rig_Large_General.fbx", "Idle_A"), "RigLarge_Idle_A_Loop");
    var eliRun = LoopCopy(Find(LRG + "Rig_Large_MovementBasic.fbx", "Walking_A"), "RigLarge_Walking_A_Loop");
    var eliAtk = Find(LRG + "Rig_Large_CombatMelee.fbx", "Melee_2H_Slam");
    var eliAtk2 = Find(LRG + "Rig_Large_CombatMelee.fbx", "Melee_2H_Attack");
    var eliHit = Find(LRG + "Rig_Large_General.fbx", "Hit_A");
    var eliDie = Find(LRG + "Rig_Large_General.fbx", "Death_A");

    sb.AppendLine("========== 素材解析 ==========");
    void Show(string label, AnimationClip c)
    {
        sb.AppendLine(string.Format("  {0,-10} {1,-34} len={2,6:F3} loop={3,-5} human={4}",
            label, c != null ? c.name : "(null)", c != null ? c.length : -1f,
            c != null ? c.isLooping.ToString() : "-", c != null ? c.isHumanMotion.ToString() : "-"));
    }
    Show("墨徒·Idle", meleeIdle); Show("墨徒·Run", meleeRun); Show("墨徒·Atk", meleeAtk);
    Show("墨徒·Hit", meleeHit); Show("墨徒·Die", meleeDie);
    Show("墨偶·Idle", rngIdle); Show("墨偶·Run", rngRun); Show("墨偶·Atk", rngAtk);
    Show("墨偶·Hit", rngHit); Show("墨偶·Die", rngDie);
    Show("墨魇·Idle", eliIdle); Show("墨魇·Run", eliRun); Show("墨魇·Atk", eliAtk);
    Show("墨魇·Atk2", eliAtk2); Show("墨魇·Hit", eliHit); Show("墨魇·Die", eliDie);

    // ---------- 构建控制器 ----------
    AnimatorController BuildController(string path, string label,
        AnimationClip idle, AnimationClip run, AnimationClip atk, AnimationClip atk2,
        AnimationClip hit, AnimationClip die, float runThreshold)
    {
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Attack2", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Dead", AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        var stIdle = sm.AddState("Idle"); stIdle.motion = idle; sm.defaultState = stIdle;
        var stRun = sm.AddState("Run"); stRun.motion = run;
        var stAtk = sm.AddState("Attack"); stAtk.motion = atk;
        var stHit = sm.AddState("Hit"); stHit.motion = hit;
        var stDie = sm.AddState("Dead"); stDie.motion = die;

        // Idle <-> Run：只看 Speed 意图（等价于本项目玩家那套"混合树参数喂意图"的教训）
        var t1 = stIdle.AddTransition(stRun); t1.hasExitTime = false; t1.duration = 0.10f;
        t1.AddCondition(AnimatorConditionMode.Greater, runThreshold, "Speed");
        var t2 = stRun.AddTransition(stIdle); t2.hasExitTime = false; t2.duration = 0.15f;
        t2.AddCondition(AnimatorConditionMode.Less, runThreshold, "Speed");

        // Attack / Hit：纯触发器（时间口径只留 C# 一份 —— 不挂 hasExitTime 条件）
        var a1 = sm.AddAnyStateTransition(stAtk); a1.hasExitTime = false; a1.duration = 0.08f;
        a1.canTransitionToSelf = false; a1.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        var back1 = stAtk.AddTransition(stRun); back1.hasExitTime = true; back1.exitTime = 1f; back1.duration = 0.12f;

        if (atk2 != null)
        {
            var stAtk2 = sm.AddState("Attack2"); stAtk2.motion = atk2;
            var a2 = sm.AddAnyStateTransition(stAtk2); a2.hasExitTime = false; a2.duration = 0.08f;
            a2.canTransitionToSelf = false; a2.AddCondition(AnimatorConditionMode.If, 0f, "Attack2");
            var back2 = stAtk2.AddTransition(stRun); back2.hasExitTime = true; back2.exitTime = 1f; back2.duration = 0.12f;
        }

        var h1 = sm.AddAnyStateTransition(stHit); h1.hasExitTime = false; h1.duration = 0.06f;
        h1.canTransitionToSelf = false; h1.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
        var hb = stHit.AddTransition(stRun); hb.hasExitTime = true; hb.exitTime = 1f; hb.duration = 0.14f;

        var d1 = sm.AddAnyStateTransition(stDie); d1.hasExitTime = false; d1.duration = 0.06f;
        d1.canTransitionToSelf = false; d1.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        sb.AppendLine("  已建控制器 " + label + " → " + path +
                      "  状态=" + sm.states.Length + " 转移=" + sm.anyStateTransitions.Length + "(AnyState)");
        return ctrl;
    }

    BuildController(ctrlDir + "/EnemyMelee.controller", "墨徒",
        meleeIdle, meleeRun, meleeAtk, null, meleeHit, meleeDie, 0.35f);
    BuildController(ctrlDir + "/EnemyRanged.controller", "墨偶",
        rngIdle, rngRun, rngAtk, null, rngHit, rngDie, 0.25f);
    BuildController(ctrlDir + "/EnemyElite.controller", "墨魇",
        eliIdle, eliRun, eliAtk, eliAtk2, eliHit, eliDie, 0.35f);

    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    // ---------- 复核：控制器里每个状态的 motion 是否真的挂上了 ----------
    sb.AppendLine();
    sb.AppendLine("========== 控制器复核 ==========");
    foreach (var f in new[] { "EnemyMelee", "EnemyRanged", "EnemyElite" })
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlDir + "/" + f + ".controller");
        if (ctrl == null) { sb.AppendLine("  [ERR] " + f + " 未生成"); continue; }
        sb.AppendLine("--- " + f + "  参数: " + string.Join(",", ctrl.parameters.Select(p => p.name + ":" + p.type)));
        foreach (var cs in ctrl.layers[0].stateMachine.states)
            sb.AppendLine("    " + cs.state.name.PadRight(10) + " motion=" +
                (cs.state.motion != null ? cs.state.motion.name : "**null**"));
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_make_controllers.txt"), sb.ToString());
    Debug.Log("[e_make_controllers] done");
    yield return null;
}

return Body();
