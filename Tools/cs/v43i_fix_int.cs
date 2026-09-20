// v43i: fix interruptionSource on Atk1/Atk2 -> Idle edges to Source
using UnityEditor;
using UnityEditor.Animations;
using System.Text;

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
var sm = ctrl.layers[0].stateMachine;
foreach (var cs in sm.states)
{
    var a = cs.state;
    if (a.name != "Atk1" && a.name != "Atk2") continue;
    foreach (var t in a.transitions)
    {
        if (t.destinationState != null && t.destinationState.name == "Idle" && t.hasExitTime)
        {
            t.interruptionSource = TransitionInterruptionSource.Source;
            sb.AppendLine(a.name + "->Idle int=Source");
        }
    }
}
AssetDatabase.SaveAssets();
return sb.ToString();
