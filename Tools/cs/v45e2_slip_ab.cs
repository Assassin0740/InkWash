// v45e2: 滑步 A/B —— 拿 UAL2 自带走路片段当基线，对照 Mixamo 走/跑
// 两档支撑相阈值（脚踝 y 全程最低 10% / 20% 区间），判据敏感性一起看
using UnityEngine;
using UnityEditor;
using System.Text;

var sb = new System.Text.StringBuilder();
var player = GameObject.Find("Player");
if (player == null) return "no Player";
var animator = player.GetComponentInChildren<Animator>();
var lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
var rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
if (lf == null || rf == null || hips == null) return "no bones";

AnimationClip Load(string path, string clipName)
{
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        var c = o as AnimationClip;
        if (c != null && c.name == clipName) return c;
    }
    return null;
}

var cases = new (string label, string path, string clip)[]
{
    ("UAL2走路(基线)", "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx", "Armature|Walk_Carry_Loop"),
    ("Mixamo走路", "Assets/Mixamo/sword and shield walk.fbx", "mixamo.com"),
    ("Mixamo跑步", "Assets/Mixamo/sword and shield run.fbx", "mixamo.com"),
};

sb.AppendLine("片段 | 身体速度 | 脚速@10% | 比值 | 脚速@20% | 比值");

try
{
    AnimationMode.StartAnimationMode();
    foreach (var c in cases)
    {
        var clip = Load(c.path, c.clip);
        if (clip == null) { sb.AppendLine(c.label + " NO CLIP"); continue; }

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

        float bodyDist = 0f;
        for (int i = 1; i <= N; i++)
            bodyDist += Vector3.Distance(new Vector3(hx[i].x, 0, hx[i].z), new Vector3(hx[i - 1].x, 0, hx[i - 1].z));
        float bodySpeed = bodyDist / clip.length;

        System.Func<float, string> at = frac =>
        {
            float lmin = Mathf.Min(ly), lmax = Mathf.Max(ly);
            float rmin = Mathf.Min(ry), rmax = Mathf.Max(ry);
            float lth = lmin + (lmax - lmin) * frac;
            float rth = rmin + (rmax - rmin) * frac;
            float sum = 0f; int n = 0;
            for (int i = 1; i <= N; i++)
            {
                if (ly[i] <= lth) { Vector3 d = lx[i] - lx[i - 1]; d.y = 0f; sum += d.magnitude / dt; n++; }
                if (ry[i] <= rth) { Vector3 d = rx[i] - rx[i - 1]; d.y = 0f; sum += d.magnitude / dt; n++; }
            }
            float fs = n > 0 ? sum / n : -1f;
            return fs.ToString("0.##") + "|" + (bodySpeed > 0.01f ? (fs / bodySpeed).ToString("0.##") : "-");
        };

        sb.AppendLine($"{c.label,-14} | {bodySpeed,6:0.##} m/s | {at(0.10f),-12} | {at(0.20f)}");
    }
}
finally
{
    AnimationMode.StopAnimationMode();
}
return sb.ToString();
