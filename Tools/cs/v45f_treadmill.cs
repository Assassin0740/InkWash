// v45f: 测各移动片段的「跑步机速度」 v_t —— 这才是 refSpeed 的正确基准。
// 推导（applyRootMotion=false，根位移被丢弃）：
//   动画播放 k 倍速时，脚相对身体向后蹬的速度 = k · v_t_1x
//   脚的世界速度 = 身体速度 v − k·v_t_1x
//   不滑步要求脚世界速度 = 0 ⇒ k = v / v_t_1x
//   而代码 k = v / refSpeed ⇒ refSpeed 必须等于 v_t_1x（不是 hips 的根位移速度！）
// v_t_1x 的测法：支撑相内 (hips 世界速度 − 脚世界速度)。原地片段 hips≈0 ⇒ v_t = −脚速。
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

var cases = new (string label, string path, string clipName)[]
{
    ("Feng走路(老)", "Assets/_Project/Animations/Feng_Walk_Loop.anim", ""),
    ("KI跑步(老)",   "Assets/_Project/Animations/Baked/Run04_KI_Carry.anim", ""),
    ("Mixamo走路",   "Assets/Mixamo/sword and shield walk.fbx", "mixamo.com"),
    ("Mixamo跑步",   "Assets/Mixamo/sword and shield run.fbx", "mixamo.com"),
};

AnimationClip Load(string path, string clipName)
{
    if (clipName == "")
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
    {
        var c = o as AnimationClip;
        if (c != null && c.name == clipName) return c;
    }
    return null;
}

sb.AppendLine("片段 | 片段长 | hips速度 | 支撑相脚速 | 跑步机速度v_t | 步数(脚触地)");

try
{
    AnimationMode.StartAnimationMode();
    foreach (var c in cases)
    {
        var clip = Load(c.path, c.clipName);
        if (clip == null) { sb.AppendLine(c.label + " NO CLIP"); continue; }

        int N = 150;
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
            lx[i] = lf.position; rx[i] = rf.position; hx[i] = hips.position;
        }

        float hipDist = 0f;
        for (int i = 1; i <= N; i++)
            hipDist += Vector3.Distance(new Vector3(hx[i].x, 0, hx[i].z), new Vector3(hx[i - 1].x, 0, hx[i - 1].z));
        float hipSpeed = hipDist / clip.length;

        float lmin = Mathf.Min(ly), lmax = Mathf.Max(ly);
        float rmin = Mathf.Min(ry), rmax = Mathf.Max(ry);
        float lth = lmin + (lmax - lmin) * 0.10f;
        float rth = rmin + (rmax - rmin) * 0.10f;

        float sum = 0f; int n = 0;
        for (int i = 1; i <= N; i++)
        {
            if (ly[i] <= lth) { Vector3 d = lx[i] - lx[i - 1]; d.y = 0f; sum += d.magnitude / dt; n++; }
            if (ry[i] <= rth) { Vector3 d = rx[i] - rx[i - 1]; d.y = 0f; sum += d.magnitude / dt; n++; }
        }
        float footSpeed = n > 0 ? sum / n : -1f;
        float vt = hipSpeed - footSpeed;

        // 数触地次数：脚踝 y 下穿阈值
        int steps = 0;
        bool lw = false, rw = false;
        for (int i = 0; i <= N; i++)
        {
            bool ln = ly[i] <= lth;
            if (ln && !lw) steps++;
            lw = ln;
            bool rn = ry[i] <= rth;
            if (rn && !rw) steps++;
            rw = rn;
        }

        sb.AppendLine($"{c.label,-12} | {clip.length,5:0.##}s | {hipSpeed,7:0.##} | {footSpeed,9:0.##} | {vt,12:0.##} | {steps}");
    }
}
finally
{
    AnimationMode.StopAnimationMode();
}
return sb.ToString();
