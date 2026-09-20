// v45e: 固定步长采样，量「支撑相脚是否打滑」——判据不依赖运行时帧率
// 原理：片段自带根位移，角色整体以 v_body 前进；支撑脚在支撑相应**相对世界静止**。
//       若支撑相脚的世界水平速度 / 身体速度 明显大于 0，就是片段自带滑步。
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Text;

var sb = new System.Text.StringBuilder();
var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
if (ctrl == null) return "no controller";
var player = GameObject.Find("Player");
if (player == null) return "no Player";
var animator = player.GetComponentInChildren<Animator>();
var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
if (lf == null || rf == null || hips == null) return "no bones";

AnimatorState Find(string n)
{
    foreach (var s in ctrl.layers[0].stateMachine.states)
        if (s.state.name == n) return s.state;
    return null;
}

sb.AppendLine("state | 身体速度 | 支撑相脚速均值 | 脚速/身速 | 支撑帧/总帧");

try
{
    AnimationMode.StartAnimationMode();
    foreach (var name in new[] { "Walk", "Run" })
    {
        var st = Find(name);
        if (st == null) { sb.AppendLine(name + " MISSING"); continue; }
        var clip = st.motion as AnimationClip;
        if (clip == null) { sb.AppendLine(name + " non-clip"); continue; }

        int N = 120;
        var ly = new float[N + 1]; var ry = new float[N + 1];
        var lx = new Vector3[N + 1]; var rx = new Vector3[N + 1];
        var hx = new Vector3[N + 1];
        float dt = clip.length / N;

        for (int i = 0; i <= N; i++)
        {
            float t = clip.length * i / (float)N;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(player, clip, t);
            AnimationMode.EndSampling();
            ly[i] = lf.position.y; ry[i] = rf.position.y;
            lx[i] = lf.position; rx[i] = rf.position;
            hx[i] = hips.position;
        }

        // 身体速度 = hips 净位移 / 时长
        float bodyDist = 0f;
        for (int i = 1; i <= N; i++) bodyDist += Vector3.Distance(new Vector3(hx[i].x, 0, hx[i].z), new Vector3(hx[i - 1].x, 0, hx[i - 1].z));
        float bodySpeed = bodyDist / clip.length;

        // 支撑相：脚踝 y 处于该脚全程最低 15% 区间
        float lmin = Mathf.Min(ly), rmin = Mathf.Min(ry);
        float lth = lmin + (Mathf.Max(ly) - lmin) * 0.15f;
        float rth = rmin + (Mathf.Max(ry) - rmin) * 0.15f;

        float sum = 0f; int n = 0;
        for (int i = 1; i <= N; i++)
        {
            if (ly[i] <= lth)
            {
                Vector3 d = lx[i] - lx[i - 1]; d.y = 0f;
                sum += d.magnitude / dt; n++;
            }
            if (ry[i] <= rth)
            {
                Vector3 d = rx[i] - rx[i - 1]; d.y = 0f;
                sum += d.magnitude / dt; n++;
            }
        }
        float footSpeed = n > 0 ? sum / n : -1f;
        float ratio = bodySpeed > 0.01f ? footSpeed / bodySpeed : -1f;
        sb.AppendLine($"{name,-6} | {bodySpeed,6:0.##} m/s | {footSpeed,8:0.##} m/s | {ratio,8:0.##} | {n}/{2 * N}");
    }
}
finally
{
    AnimationMode.StopAnimationMode();
}
return sb.ToString();
