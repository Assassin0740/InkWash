// 逐个片段量「扭腰」，用于在同一套条件下横比选型。
// 做法：把 Run 状态的 motion 临时换成候选片段，实时采样，结束后还原。
// （不 SaveAssets，退出 Play 即失效；跑完可用 s2_probe_animcfg.cs 核对还原情况）
//
// 结果写 Tools/reports/S2_twist_clips.txt

string OUT = "Tools/reports/S2_twist_clips.txt";
const string FbxUAL1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";

System.Collections.IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();

    var ctl = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { System.IO.File.WriteAllText(OUT, "[ERR] no PlayerController"); yield break; }
    var anim = ctl.animator;
    var root = ctl.transform;
    var lHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperLeg);
    var rHip = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperLeg);
    var lSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.LeftUpperArm);
    var rSh = anim.GetBoneTransform(UnityEngine.HumanBodyBones.RightUpperArm);

    var ac = anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
    if (ac == null) { System.IO.File.WriteAllText(OUT, "[ERR] runtimeAnimatorController 不是 AnimatorController"); yield break; }

    UnityEditor.Animations.AnimatorState stRun = null;
    foreach (var s in ac.layers[0].stateMachine.states)
        if (s.state.name == "Run") stRun = s.state;
    if (stRun == null) { System.IO.File.WriteAllText(OUT, "[ERR] 找不到 Run 状态"); yield break; }
    var orig = stRun.motion;
    sb.AppendLine("原 Run 片段: " + (orig != null ? orig.name : "null"));

    var dict = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(FbxUAL1))
    {
        var c = o as UnityEngine.AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        string k = c.name; int bar = k.LastIndexOf('|');
        if (bar >= 0) k = k.Substring(bar + 1);
        if (!dict.ContainsKey(k)) dict[k] = c;
    }

    System.Func<UnityEngine.Vector3, UnityEngine.Vector3, float> localYaw =
        (a, b) =>
        {
            UnityEngine.Vector3 d = root.InverseTransformDirection(b - a);
            return UnityEngine.Mathf.Atan2(d.x, d.z) * UnityEngine.Mathf.Rad2Deg;
        };

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(new UnityEngine.Vector2(0f, 1f), true);
    float w0 = UnityEngine.Time.time + 1.5f;
    while (UnityEngine.Time.time < w0) yield return null;

    string[] names = { "Sprint_Loop", "Jog_Fwd_Loop", "Walk_Loop" };

    foreach (var nm in names)
    {
        if (!dict.ContainsKey(nm)) { sb.AppendLine(nm + ": MISSING"); continue; }
        stRun.motion = dict[nm];

        float ws = UnityEngine.Time.time + 1.2f;
        while (UnityEngine.Time.time < ws) yield return null;

        float t1 = UnityEngine.Time.time;
        float hMin = 9999f, hMax = -9999f, sMin = 9999f, sMax = -9999f, twMin = 9999f, twMax = -9999f;
        int n = 0;
        float clipLen = dict[nm].length;
        while (UnityEngine.Time.time - t1 < 2.4f)
        {
            float hy = localYaw(lHip.position, rHip.position);
            float sy = localYaw(lSh.position, rSh.position);
            float tw = UnityEngine.Mathf.DeltaAngle(hy, sy);
            hMin = UnityEngine.Mathf.Min(hMin, hy); hMax = UnityEngine.Mathf.Max(hMax, hy);
            sMin = UnityEngine.Mathf.Min(sMin, sy); sMax = UnityEngine.Mathf.Max(sMax, sy);
            twMin = UnityEngine.Mathf.Min(twMin, tw); twMax = UnityEngine.Mathf.Max(twMax, tw);
            n++;
            yield return null;
        }

        var si = anim.GetCurrentAnimatorStateInfo(0);
        sb.AppendLine(string.Format("{0,-16} clipLen={1:F3}s  state={2,-6} frames={3}",
            nm, clipLen, si.IsName("Run") ? "Run" : "Other", n));
        sb.AppendLine(string.Format("    hip   yaw p-p = {0,6:F1} deg", hMax - hMin));
        sb.AppendLine(string.Format("    shldr yaw p-p = {0,6:F1} deg", sMax - sMin));
        sb.AppendLine(string.Format("    TWIST p-p     = {0,6:F1} deg", twMax - twMin));
    }

    stRun.motion = orig;   // 还原
    ctl.SetInjectedMove(UnityEngine.Vector2.zero, false);
    ctl.EndInputOverride();

    sb.AppendLine("已还原 Run 片段: " + (stRun.motion != null ? stRun.motion.name : "null"));
    System.IO.File.WriteAllText(OUT, sb.ToString());
    UnityEngine.Debug.Log("[twist-clips] written: " + OUT);
}

return Body();
