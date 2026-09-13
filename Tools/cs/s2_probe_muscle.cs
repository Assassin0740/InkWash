// 直接读片段的**人形肌肉曲线**，确认「骨盆偏航过大」是烘焙在片段数据里的，
// 而不是运行时重定向放出来的。这样选型时才有依据：换片段能解决，改 Avatar 不能。
//
// 人形片段在 Unity 里不存骨骼曲线，存的是 muscle 曲线（绑定到 Animator，
// propertyName 形如 "Hips T.x" / "Spine Front-Back" / "Chest Twist" …）。
// 这里把三个候选片段里所有含 Hips / Spine / Chest 的曲线打出来，附 min~max。
//
// 结果写 Tools/reports/S2_muscle_clips.txt

var sb = new System.Text.StringBuilder();
const string FbxUAL1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";

var dict = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(FbxUAL1))
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null || c.name.StartsWith("__preview__")) continue;
    string k = c.name; int bar = k.LastIndexOf('|');
    if (bar >= 0) k = k.Substring(bar + 1);
    if (!dict.ContainsKey(k)) dict[k] = c;
}

string[] names = { "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop" };

foreach (var nm in names)
{
    UnityEngine.AnimationClip clip;
    if (!dict.TryGetValue(nm, out clip)) { sb.AppendLine(nm + ": MISSING"); continue; }
    sb.AppendLine("===== " + nm + "  len=" + clip.length.ToString("F3") + "s =====");

    var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
    sb.AppendLine("  曲线总数: " + bindings.Length);

    var shown = new System.Collections.Generic.List<string>();
    foreach (var b in bindings)
    {
        string p = b.propertyName;
        if (p.IndexOf("Hips") < 0 && p.IndexOf("Spine") < 0 && p.IndexOf("Chest") < 0
            && p.IndexOf("Root") < 0) continue;

        var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, b);
        if (curve == null) continue;
        float mn = 9999f, mx = -9999f;
        foreach (var kf in curve.keys)
        {
            mn = UnityEngine.Mathf.Min(mn, kf.value);
            mx = UnityEngine.Mathf.Max(mx, kf.value);
        }
        shown.Add(string.Format("    {0,-26} keys={1,3}  {2,9:F3} ~ {3,9:F3}   (p-p {4:F3})",
            p, curve.keys.Length, mn, mx, mx - mn));
    }
    shown.Sort();
    foreach (var s in shown) sb.AppendLine(s);
    sb.AppendLine();
}

System.IO.File.WriteAllText("Tools/reports/S2_muscle_clips.txt", sb.ToString());
return sb.ToString();
