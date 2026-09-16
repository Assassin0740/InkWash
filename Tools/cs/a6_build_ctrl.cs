// a6_build_ctrl.cs —— 为三个新怪建 AnimatorController
//
// 设计：**完全复用现有敌人的状态机拓扑**（Idle/Run/Attack/Attack2/Hit/Dead +
//   5 个参数 Speed float / Attack / Attack2 / Hit / Dead trigger）。
//   理由：EnemyBase 的状态机是**按这个拓扑写死**的（HasParam 按 hash 查、Enter 时 SetTrigger）。
//   换拓扑就必须改 C#，而"换骨架不该顺带改行为逻辑"—— 这是本项目的硬规矩
//   （换模型 = 换骨架，但行为参数与状态机时间口径应当原样保留，否则老验收全失效）。
//
// 片段分配（依据 a5_retarget3 的实测：哪些片段真的会动）：
//
//   Orge（山怪）→ 给 EnemyMelee 或 Elite 用
//     Idle    = Ideal3                （0.355/0.568 轻微呼吸，适合待机）
//     Run     = Run                   （3.048 走跑）
//     Attack  = Punch                 （0.614/4.585 抬手明显）
//     Attack2 = Slam                  （3.388/5.916 重砸）
//     Hit     = Roar2                 （1.317/1.813 上半身抖动，当受击抖动）
//     Dead    = Death                 （2.302/2.365 倒地）
//
//   Orc（兽人）→ 给 EnemyMelee 用
//     Idle    = ANM_ORC_Roar          ★ 弃用 ANM_IDLE（实测 0.023 = 本身静止）
//     Run     = ANM_RUN               （0.268/0.389）
//     Attack  = ANM_ORC_LIGHT_ATTACK  （0.540/1.710）
//     Attack2 = ANM_ATTACK_CRIT       （1.902/3.236 重击）
//     Hit     = ANM_DAMAGED           （0.797/1.508）
//     Dead    = ANM_DEATH             （2.659/2.598）
//
//   Undead（不死兵）→ 给 EnemyRanged 用（它只有 Slash，其余靠重定向）
//     Idle    = Orge.Ideal3           （重定向，0.232/0.226 最轻微，当待机最合适）
//     Run     = Orge.Walk             （重定向，0.756/0.750）
//     Attack  = Slash                 （自有，0.946/1.800 —— 保留自己的味道）
//     Hit     = Orc.ANM_DAMAGED       （重定向，0.719/1.093）
//     Dead    = Orc.ANM_DEATH         （重定向，2.010/1.980）
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

const string OutDir = "Assets/_Project/Animations/Enemies";
const string FbxOrge = "Assets/ThirdParty/Ziyuan/_fbx/mountain_orge.fbx";
const string FbxOrc = "Assets/ThirdParty/Ziyuan/_fbx/low-poly_orc.fbx";
const string FbxUndead = "Assets/ThirdParty/Ziyuan/_fbx/cursed_undead_soldier_rig.fbx";

// 按「后缀」找片段（真实名是 'Object_5|Armature|Walk_Object_5' 这种复合形式）
AnimationClip FindClip(string fbx, string suffix)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
        if (o is AnimationClip c && !c.name.StartsWith("__preview__")
            && (c.name == suffix || c.name.EndsWith("|" + suffix)))
            return c;
    return null;
}

// 建一个敌人 controller
AnimatorController Build(string name, (string fbx, string suffix) idle,
    (string fbx, string suffix) run,
    (string fbx, string suffix) atk,
    (string fbx, string suffix) atk2,
    (string fbx, string suffix) hit,
    (string fbx, string suffix) dead,
    StringBuilder log)
{
    string path = OutDir + "/" + name;   // name 已含 ".controller"

    var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
    // 注意：`CreateAnimatorControllerAtPath` **不会**预置任何参数
    // （早期误以为它会塞一个默认 float，直接 RemoveParameter(0) 会数组越界）
    foreach (var p in ac.parameters) { }
    ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
    ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
    ac.AddParameter("Attack2", AnimatorControllerParameterType.Trigger);
    ac.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
    ac.AddParameter("Dead", AnimatorControllerParameterType.Trigger);

    var sm = ac.layers[0].stateMachine;

    AnimatorState AddState(string sname, AnimationClip clip, float speed)
    {
        var st = sm.AddState(sname);
        st.motion = clip;
        st.speed = speed;
        return st;
    }

    AnimationClip C((string fbx, string suffix) t)
    {
        if (string.IsNullOrEmpty(t.fbx)) return null;
        return FindClip(t.fbx, t.suffix);
    }

    var cIdle = C(idle); var cRun = C(run); var cAtk = C(atk);
    var cAtk2 = C(atk2); var cHit = C(hit); var cDead = C(dead);

    // 缺片段要吵（静默缺失会表现为"某状态永远静止"，零报错）
    foreach (var (t, nm) in new[] { (idle, "Idle"), (run, "Run"), (atk, "Attack"), (hit, "Hit"), (dead, "Dead") })
        if (C(t) == null) log.AppendLine("  ★★ " + name + " 的 " + nm + " 片段缺失: " + t.suffix);

    var sIdle = AddState("Idle", cIdle, 1f);
    var sRun = AddState("Run", cRun, 1f);
    var sAtk = AddState("Attack", cAtk, 1f);
    var sHit = AddState("Hit", cHit, 1f);
    var sDead = AddState("Dead", cDead, 1f);
    AnimatorState sAtk2 = null;
    if (cAtk2 != null) sAtk2 = AddState("Attack2", cAtk2, 1f);

    sm.defaultState = sIdle;

    // ANY → Attack / Hit / Dead（顺序即优先级）
    var t1 = sm.AddAnyStateTransition(sAtk);
    t1.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
    t1.duration = 0.06f; t1.hasExitTime = false;
    if (sAtk2 != null)
    {
        var t2 = sm.AddAnyStateTransition(sAtk2);
        t2.AddCondition(AnimatorConditionMode.If, 0f, "Attack2");
        t2.duration = 0.06f; t2.hasExitTime = false;
    }
    var t3 = sm.AddAnyStateTransition(sHit);
    t3.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
    t3.duration = 0.05f; t3.hasExitTime = false;
    var t4 = sm.AddAnyStateTransition(sDead);
    t4.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
    t4.duration = 0.08f; t4.hasExitTime = false;

    // Idle ↔ Run
    var t5 = sIdle.AddTransition(sRun);
    t5.AddCondition(AnimatorConditionMode.Greater, 0.05f, "Speed");
    t5.duration = 0.12f; t5.hasExitTime = false;
    var t6 = sRun.AddTransition(sIdle);
    t6.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");
    t6.duration = 0.12f; t6.hasExitTime = false;

    // Attack / Hit / Attack2 → Run（靠 exitTime 自然收招）
    foreach (var st in new[] { sAtk, sAtk2, sHit })
    {
        if (st == null) continue;
        var tt = st.AddTransition(sRun);
        tt.duration = 0.15f; tt.hasExitTime = true; tt.exitTime = 1f;
    }

    log.AppendLine("  建好 " + name + ".controller: 状态 " + (sAtk2 != null ? 6 : 5)
                   + " 个，片段 Idle=" + (cIdle != null ? cIdle.name : "null")
                   + " / Run=" + (cRun != null ? cRun.name : "null")
                   + " / Attack=" + (cAtk != null ? cAtk.name : "null")
                   + " / Attack2=" + (cAtk2 != null ? cAtk2.name : "null")
                   + " / Hit=" + (cHit != null ? cHit.name : "null")
                   + " / Dead=" + (cDead != null ? cDead.name : "null"));

    return ac;
}

sb.AppendLine("========== 建三个新怪的 controller ==========");

// ---- Z_Enemy_Orge（山怪，近战重击型，给 EnemyElite 的骨架）----
Build("Z_Orge.controller",
    (FbxOrge, "Ideal3_Object_5"), (FbxOrge, "Run_Object_5"),
    (FbxOrge, "Punch_Object_5"), (FbxOrge, "Slam_Object_5"),
    (FbxOrge, "Roar2_Object_5"), (FbxOrge, "Death_Object_5"), sb);

// ---- Z_Enemy_Orc（兽人，近战连击型，给 EnemyMelee）----
Build("Z_Orc.controller",
    (FbxOrc, "ANM_ORC_Roar_Object_4"), (FbxOrc, "ANM_RUN_Object_4"),
    (FbxOrc, "ANM_ORC_LIGHT_ATTACK_Object_4"), (FbxOrc, "ANM_ATTACK_CRIT_Object_4"),
    (FbxOrc, "ANM_DAMAGED_Object_4"), (FbxOrc, "ANM_DEATH_Object_4"), sb);

// ---- Z_Enemy_Undead（不死兵，远程型，Idle/Run/Hit/Dead 靠重定向 Orge/Orc）----
// 注意：第 5 个参数（Attack2）传 default 而不是 null —— 元组字面量不接受 null
// （CS1503），必须用 default 或显式构造。
Build("Z_Undead.controller",
    (FbxOrge, "Ideal3_Object_5"), (FbxOrge, "Walk_Object_5"),
    (FbxUndead, "Slash_GLTF_created_0"), default,
    (FbxOrc, "ANM_DAMAGED_Object_4"), (FbxOrc, "ANM_DEATH_Object_4"), sb);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a6_build_ctrl.txt"), sb.ToString());
Debug.Log("[a6] " + sb.ToString());
