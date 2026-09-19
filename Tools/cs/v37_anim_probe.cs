// v37_anim_probe.cs —— 玩家/敌人动画现状探针：controller 状态/参数 + 玩家骨架 + 场景墙 collider
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

string Report(AnimatorController ac, string tag)
{
    var sb = new StringBuilder();
    sb.Append("[" + tag + "] params:");
    foreach (var p in ac.parameters) sb.Append(" ").Append(p.name).Append("(").Append(p.type).Append(")");
    foreach (var l in ac.layers)
    {
        sb.Append("\n  layer=").Append(l.name);
        foreach (var st in l.stateMachine.states)
        {
            var s = st.state;
            string clip = s.motion != null ? s.motion.name : "<null>";
            bool loop = false;
            var ca = s.motion as AnimationClip;
            if (ca != null) loop = ca.isLooping;
            sb.Append("\n    state=").Append(s.name).Append(" clip=").Append(clip).Append(" loop=").Append(loop);
        }
    }
    return sb.ToString();
}

string walls = "";
foreach (var go in Object.FindObjectsOfType<GameObject>(false))
{
    if (go.activeInHierarchy && System.Text.RegularExpressions.Regex.IsMatch(go.name, @"(?i)wall|qiang|墙|fence|柱|pillar|column"))
    {
        var cols = go.GetComponentsInChildren<Collider>(true);
        var rends = go.GetComponentsInChildren<MeshRenderer>(true);
        walls += "\n  " + go.name + " cols=" + cols.Length + " rends=" + rends.Length + " layer=" + LayerMask.LayerToName(go.layer);
    }
}

var result = new StringBuilder();
var player = GameObject.FindObjectOfType<InkWash.Player.PlayerHealth>();
if (player != null)
{
    var anim = player.GetComponentInChildren<Animator>();
    result.Append("[player] model=").Append(anim != null ? anim.gameObject.name : "<none>")
          .Append(" avatar=").Append(anim != null && anim.avatar != null)
          .Append("\n");
    var rig = player.GetComponentInChildren<SkinnedMeshRenderer>();
    if (rig != null) result.Append("[player] smr=").Append(rig.gameObject.name).Append(" rootBone=").Append(rig.rootBone != null ? rig.rootBone.name : "<null>").Append("\n");
    if (anim != null && anim.runtimeAnimatorController is AnimatorController pac)
        result.Append(Report(pac, "Player.controller"));
}
foreach (var cn in new[] { "EnemyMelee", "EnemyRanged", "EnemyElite" })
{
    var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Enemies/" + cn + ".controller");
    if (ac != null) result.Append("\n").Append(Report(ac, cn));
}
result.Append("\n[scene walls]").Append(walls);
Debug.Log("[v37] " + result);
