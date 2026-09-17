// q_apply.cs —— 落地本轮两处修复
//   ① 持剑待机：新建 UAL1「Rig|Sword_Idle」的循环副本 → 指到 CombatStance.combatIdleClip
//   ② 跑步：控制器 Run 状态的 motion 换成烘焙好的「持剑跑」Run01_IdleArm
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

var sb = new StringBuilder();
AssetDatabase.Refresh();

AnimationClip Clip(string p, string n)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
    {
        var c = o as AnimationClip;
        if (c != null && !c.name.StartsWith("__preview__") && (n == null || c.name == n)) return c;
    }
    return null;
}

// ---------- ① 持剑待机循环副本 ----------
string ual1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
var swordIdle = Clip(ual1, "Rig|Sword_Idle");
if (swordIdle == null) return "ERR: 找不到 Rig|Sword_Idle";

const string dest = "Assets/_Project/Animations/Sword_Idle_Loop.anim";
var copy = Object.Instantiate(swordIdle);
copy.name = "Sword_Idle_Loop";
var st = AnimationUtility.GetAnimationClipSettings(copy);
st.loopTime = true;
AnimationUtility.SetAnimationClipSettings(copy, st);
AssetDatabase.DeleteAsset(dest);
AssetDatabase.CreateAsset(copy, dest);
sb.AppendLine("① 写出 " + dest + "  len=" + copy.length.ToString("F3") + "  loop=" + copy.isLooping
    + "  曲线数=" + AnimationUtility.GetCurveBindings(copy).Length
    + "  (源 " + swordIdle.name + " 曲线数=" + AnimationUtility.GetCurveBindings(swordIdle).Length + ")");

// ---------- ② 控制器 Run 状态换 mototion ----------
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
var baked = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Animations/Baked/Run01_IdleArm.anim");
if (ctrl == null || baked == null) return "ERR: 控制器或烘焙片段缺失";

bool found = false;
foreach (var s in ctrl.layers[0].stateMachine.states)
{
    if (s.state.name != "Run") continue;
    string before = s.state.motion != null ? s.state.motion.name : "<null>";
    s.state.motion = baked;
    found = true;
    sb.AppendLine("② Run 状态 motion: " + before + "  →  " + baked.name);
}
if (!found) sb.AppendLine("② ★ 没找到名为 Run 的状态");
EditorUtility.SetDirty(ctrl);
AssetDatabase.SaveAssets();

// ---------- ③ 预制体上的 combatIdleClip ----------
var newLoopClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(dest);
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
if (prefab == null) return sb.ToString() + "\nERR: 预制体找不到";
var contents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Player/Player.prefab");
bool set = false;
foreach (var mb in contents.GetComponentsInChildren<MonoBehaviour>(true))
{
    if (mb == null || mb.GetType().Name != "CombatStance") continue;
    var so = new SerializedObject(mb);
    var p = so.FindProperty("combatIdleClip");
    if (p == null) { sb.AppendLine("③ ★ 找不到 combatIdleClip 字段"); continue; }
    string before = p.objectReferenceValue != null ? p.objectReferenceValue.name : "<null>";
    p.objectReferenceValue = newLoopClip;
    so.ApplyModifiedPropertiesWithoutUndo();
    set = true;
    sb.AppendLine("③ CombatStance.combatIdleClip: " + before + "  →  " + (newLoopClip != null ? newLoopClip.name : "<null>"));
}
if (set) PrefabUtility.SaveAsPrefabAsset(contents, "Assets/_Project/Prefabs/Player/Player.prefab");
else sb.AppendLine("③ ★ 没找到 CombatStance 上的 combatIdleClip");
PrefabUtility.UnloadPrefabContents(contents);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
File.WriteAllText("Tools/reports/q_apply.txt", sb.ToString(), new UTF8Encoding(false));
return sb.ToString();
