// 读 KI《Run01_Forward》片段的人形肌肉曲线，确认「扭腰」是不是烘焙在片段数据里
// （而不是重定向/骨架放出来的）。同时把同一套曲线名下的 UAL1 Walk_Loop 拉出来做对照。
string OUT = "Tools/reports/S2_ki_muscle.txt";

var sb = new System.Text.StringBuilder();

const string FbxKI = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";
const string FbxUAL1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";

System.Action<string, string> dump = (path, wantClip) =>
{
    sb.AppendLine("===== " + path + " =====");
    UnityEngine.AnimationClip clip = null;
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
    {
        var c = o as UnityEngine.AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        if (wantClip == null) { clip = c; break; }
        string k = c.name; int bar = k.LastIndexOf('|');
        if (bar >= 0) k = k.Substring(bar + 1);
        if (k == wantClip) { clip = c; break; }
    }
    if (clip == null) { sb.AppendLine("  [ERR] 找不到片段"); sb.AppendLine(); return; }
    sb.AppendLine("  片段 = " + clip.name + "  len=" + clip.length.ToString("F3") + "s  isHumanMotion=" + clip.isHumanMotion);
    var binds = UnityEditor.AnimationUtility.GetCurveBindings(clip);
    sb.AppendLine("  曲线总数 = " + binds.Length);

    string[] keys = { "Hips", "RootQ", "RootT", "Spine ", "Chest ", "UpperChest", "Shoulder" };
    var seen = new System.Collections.Generic.List<string>();
    foreach (var b in binds)
    {
        bool hit = false;
        foreach (var kk in keys) if (b.propertyName.Contains(kk)) { hit = true; break; }
        if (!hit) continue;
        var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, b);
        if (curve == null || curve.keys.Length == 0) continue;
        float mn = 9999f, mx = -9999f;
        foreach (var k in curve.keys) { mn = UnityEngine.Mathf.Min(mn, k.value); mx = UnityEngine.Mathf.Max(mx, k.value); }
        seen.Add(string.Format("    {0,-30} keys={1,3}   {2,8:F3} ~ {3,8:F3}   (p-p {4:F3})",
            b.propertyName, curve.keys.Length, mn, mx, mx - mn));
    }
    seen.Sort();
    foreach (var s in seen) sb.AppendLine(s);
    sb.AppendLine();
};

dump(FbxKI, null);
dump(FbxUAL1, "Walk_Loop");

System.IO.File.WriteAllText(OUT, sb.ToString());
UnityEngine.Debug.Log("[ki-muscle] written");
