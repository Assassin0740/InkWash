// v43a: dump Player.controller attack-chain clip names & lengths + state/transition table
using UnityEngine;
using System.Text;

var sb = new System.Text.StringBuilder();

string controllerPath = "Assets/_Project/Animations/Player.controller";
var ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
if (ctrl == null) { return "controller not found"; }

// 1) all animation clips referenced by the controller with lengths
var clips = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(controllerPath);
// clips live in the fbx sub-assets; iterate states instead
foreach (var layer in ((UnityEditor.Animations.AnimatorController)ctrl).layers)
{
    foreach (var st in layer.stateMachine.states)
    {
        var a = st.state;
        var motion = a.motion as AnimationClip;
        string mname = motion == null ? (a.motion == null ? "NULL" : a.motion.name + "(blendtree)") : motion.name;
        float len = motion == null ? -1f : motion.length;
        bool loop = motion != null && motion.isLooping;
        sb.AppendLine(string.Format("{0} | speed={1:F3} | clip={2} | len={3:F3}s | loop={4}",
            a.name, a.speed, mname, len, loop));
    }
}

sb.AppendLine("---- transitions of attack chain ----");
foreach (var layer in ((UnityEditor.Animations.AnimatorController)ctrl).layers)
{
    foreach (var st in layer.stateMachine.states)
    {
        var a = st.state;
        if (a.name != "Atk1" && a.name != "Atk2" && a.name != "Atk3"
            && a.name != "Atk1Rec" && a.name != "Atk2Rec") continue;
        foreach (var t in a.transitions)
        {
            string dst = t.destinationState == null ? "EXIT" : t.destinationState.name;
            string cond = "";
            foreach (var c in t.conditions) cond += c.mode.ToString() + ":" + c.parameter + ">=" + c.threshold + " ";
            sb.AppendLine(string.Format("{0} -> {1} | exitTime={2:F2} hasExit={3} | dur={4:F2} | int={5} | cond[{6}]",
                a.name, dst, t.exitTime, t.hasExitTime, t.duration, t.interruptionSource, cond));
        }
    }
}
return sb.ToString();
