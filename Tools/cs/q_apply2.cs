// q_apply2.cs —— 用改名后的 Run01_Carry 重新落地；清理无用变体；补 CombatStance.idleSlotClip
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();

// ---------- ① 清理未被采用的烘焙变体（证据留在 reports/ + screenshots/）----------
string[] drop =
{
    "Assets/_Project/Animations/Baked/Run01_IdleArm.anim",
    "Assets/_Project/Animations/Baked/Run01_IdleArm_Io.anim",
    "Assets/_Project/Animations/Baked/Run01_IdleArm_Du.anim",
    "Assets/_Project/Animations/Baked/Run01_Still.anim",
    "Assets/_Project/Animations/Baked/Run01_Still_Hd.anim",
    "Assets/_Project/Animations/Baked/Run01_SwIdleArm.anim",
    "Assets/_Project/Animations/Baked/Run01_SwCarry.anim",
    "Assets/_Project/Animations/Baked/Run01_Carry_Io.anim",
    "Assets/_Project/Animations/Baked/Run01_Carry_Du.anim",
};
foreach (var p in drop) if (AssetDatabase.DeleteAsset(p)) sb.AppendLine("① 删除 " + p);
AssetDatabase.Refresh();

// ---------- ② 控制器 Run 状态 → Run01_Carry ----------
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
var carry = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_Carry.anim");
if (ctrl == null || carry == null) return "ERR: 控制器或 Run01_Carry 缺失";

foreach (var s in ctrl.layers[0].stateMachine.states)
{
    if (s.state.name != "Run") continue;
    string before = s.state.motion != null ? s.state.motion.name : "<null>";
    s.state.motion = carry;
    sb.AppendLine("② Run 状态 motion: " + before + "  →  " + carry.name);
}
EditorUtility.SetDirty(ctrl);
AssetDatabase.SaveAssets();

// ---------- ③ 预制体：combatIdleClip + idleSlotClip ----------
var swordIdleLoop = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Sword_Idle_Loop.anim");
var fengIdleLoop = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Feng_Idle_Loop.anim");

var contents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player/Player.prefab");
bool touched = false;
foreach (var mb in contents.GetComponentsInChildren<MonoBehaviour>(true))
{
    if (mb == null || mb.GetType().Name != "CombatStance") continue;
    var so = new SerializedObject(mb);
    var pc = so.FindProperty("combatIdleClip");
    var ps = so.FindProperty("idleSlotClip");
    if (pc != null)
    {
        string before = pc.objectReferenceValue != null ? pc.objectReferenceValue.name : "<null>";
        pc.objectReferenceValue = swordIdleLoop;
        sb.AppendLine("③ combatIdleClip: " + before + "  →  " + (swordIdleLoop != null ? swordIdleLoop.name : "<null>"));
    }
    if (ps != null)
    {
        string before = ps.objectReferenceValue != null ? ps.objectReferenceValue.name : "<null>";
        ps.objectReferenceValue = fengIdleLoop;
        sb.AppendLine("③ idleSlotClip:   " + before + "  →  " + (fengIdleLoop != null ? fengIdleLoop.name : "<null>"));
    }
    so.ApplyModifiedPropertiesWithoutUndo();
    touched = true;
}
if (touched) PrefabUtility.SaveAsPrefabAsset(contents, "Assets/_Project/Prefabs/Player/Player.prefab");
else sb.AppendLine("③ ★ 没找到 CombatStance");
PrefabUtility.UnloadPrefabContents(contents);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_apply2.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
