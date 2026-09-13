// 音频 Resources 加载路径复检。
// 上一次体检用错了路径（Resources.Load 的路径是相对 Resources 目录的，不该带 "Audio/Resources/" 前缀）。
var sb = new System.Text.StringBuilder();
sb.AppendLine("=== Resources.Load 复检（正确路径） ===");
int ok = 0, bad = 0;

System.Action<string> tryLoad = n =>
{
    var clip = UnityEngine.Resources.Load<UnityEngine.AudioClip>(n);
    if (clip != null) { ok++; sb.AppendLine("  [OK]   " + n + "   " + clip.length.ToString("F2") + "s"); }
    else { bad++; sb.AppendLine("  [缺失] " + n); }
};

tryLoad(InkWash.Core.GameAudio.Bgm.ThemeMain);
tryLoad(InkWash.Core.GameAudio.Sfx.SwingA);
tryLoad(InkWash.Core.GameAudio.Sfx.SwingB);
tryLoad(InkWash.Core.GameAudio.Sfx.SwingHeavy);
tryLoad(InkWash.Core.GameAudio.Sfx.Dash);
tryLoad(InkWash.Core.GameAudio.Sfx.Draw);
tryLoad(InkWash.Core.GameAudio.Sfx.HitBlade);
tryLoad(InkWash.Core.GameAudio.Sfx.HitFlesh);
tryLoad(InkWash.Core.GameAudio.Sfx.Footstep);
tryLoad(InkWash.Core.GameAudio.Sfx.UiClick);
sb.AppendLine("  → OK " + ok + " / 缺失 " + bad);

sb.AppendLine();
sb.AppendLine("=== AudioDirector 接线 ===");
var go = UnityEngine.GameObject.Find("Player");
if (go != null)
{
    var ad = go.GetComponent<InkWash.Core.AudioDirector>();
    if (ad == null) sb.AppendLine("  !! Player 上没有 AudioDirector");
    else
    {
        var so = new UnityEditor.SerializedObject(ad);
        var it = so.GetIterator();
        while (it.NextVisible(true))
            sb.AppendLine("  " + it.propertyPath + " = " + (it.propertyType == UnityEditor.SerializedPropertyType.ObjectReference
                ? (it.objectReferenceValue == null ? "(null)" : it.objectReferenceValue.name) : it.ToString()));
    }
}

return sb.ToString();
