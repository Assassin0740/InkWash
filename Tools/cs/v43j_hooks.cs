// v43j: runtime combo verification — drive TryAttack via reflection, log state timeline
using UnityEngine;
using System.Collections;
using System.Text;
using System.Reflection;

var sb = new System.Text.StringBuilder();
var player = GameObject.Find("Player");
if (player == null) return "no Player";
var ctlType = null as System.Type;
foreach (var mb in player.GetComponentsInChildren<MonoBehaviour>())
{
    var t = mb.GetType();
    if (t.Name == "PlayerController") { ctlType = t; break; }
}
if (ctlType == null) return "no PlayerController";
var ctl = player.GetComponentInChildren(ctlType);
var miTry = ctlType.GetMethod("TryAttack", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
if (miTry == null) return "no TryAttack";
var piCombo = ctlType.GetProperty("ComboStep");
var piPhase = ctlType.GetProperty("PhaseTag");
var anim = player.GetComponentInChildren<Animator>();

string StateInfo()
{
    var st = anim.GetCurrentAnimatorStateInfo(0);
    bool tr = anim.IsInTransition(0);
    string nxt = "";
    if (tr) { var ni = anim.GetNextAnimatorStateInfo(0); nxt = "->" + (ni.shortNameHash != 0 ? "transiting" : ""); }
    return st.shortNameHash.ToString() + (tr ? "(T)" : "");
}
string StateName()
{
    var st = anim.GetCurrentAnimatorStateInfo(0);
    if (st.IsName("Idle")) return "Idle";
    if (st.IsName("Walk")) return "Walk";
    if (st.IsName("Run")) return "Run";
    if (st.IsName("Dash")) return "Dash";
    if (st.IsName("Atk1")) return "Atk1";
    if (st.IsName("Atk2")) return "Atk2";
    if (st.IsName("Atk3")) return "Atk3";
    return "?";
}

return sb.ToString() + "hooks-ok";
