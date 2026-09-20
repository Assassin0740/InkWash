// v45b: 量出 Player.controller 每个状态的片段时长 + 烘焙位移速度
// 目的：核对代码侧时间口径（comboSwingDuration）与参考速度（walkRefSpeed/runRefSpeed）是否还匹配片段
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
if (ctrl == null) return "no controller";

var player = GameObject.Find("Player");
if (player == null) return "no Player in scene (需打开 Main.unity)";
var animator = player.GetComponentInChildren<Animator>();
if (animator == null) return "no animator";
var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
if (hips == null) return "no hips";

var sm = ctrl.layers[0].stateMachine;
sb.AppendLine("state | speed | clip | len | dur=len/speed | 烘焙地速(m/s) | 净位移(m)");

try
{
    AnimationMode.StartAnimationMode();
    foreach (var s in sm.states)
    {
        var st = s.state;
        var clip = st.motion as AnimationClip;
        if (clip == null) { sb.AppendLine($"{st.name} | {st.speed:0.##} | (non-clip motion)"); continue; }

        int N = 24;
        var pts = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i <= N; i++)
        {
            float t = clip.length * i / (float)N;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(player, clip, t);
            AnimationMode.EndSampling();
            pts.Add(new Vector3(hips.position.x, 0f, hips.position.z));
        }
        float path = 0f;
        for (int i = 1; i < pts.Count; i++) path += Vector3.Distance(pts[i], pts[i - 1]);
        float net = Vector3.Distance(pts[pts.Count - 1], pts[0]);
        float dur = clip.length / (st.speed > 0.001f ? st.speed : 1f);
        float gs = dur > 0.001f ? path / clip.length : 0f;   // 每秒烘焙速度（未计 state.speed）

        sb.AppendLine($"{st.name,-9} | {st.speed,6:0.###} | {clip.name,-26} | {clip.length,5:0.##}s | {dur,5:0.##}s | {gs,6:0.##} | {net,5:0.##}");
    }
}
finally
{
    AnimationMode.StopAnimationMode();
}
return sb.ToString();
