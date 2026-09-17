// S2 音频装配：把 AudioDirector 挂到玩家预制体，并校验音频资源能被 Resources 找到。
var sb = new System.Text.StringBuilder();

// ---- 0) 类型可见性 ----
sb.AppendLine("[0] 类型检查:");
sb.AppendLine("    GameAudio     : " + (System.Type.GetType("InkWash.Core.GameAudio, Assembly-CSharp") != null));
sb.AppendLine("    AudioDirector : " + (System.Type.GetType("InkWash.Core.AudioDirector, Assembly-CSharp") != null));
sb.AppendLine("    QFramework.AudioKit : " + (System.Type.GetType("QFramework.AudioKit, AudioKit") != null));
sb.AppendLine();

// ---- 1) 音频资源能否按名字取到（AudioKit 的加载器就是 Resources.Load）----
sb.AppendLine("[1] Resources 取音频（名字必须与 GameAudio 常量一致）:");
string[] clips = new string[]
{
    "BGM/et11lx-chinese-ancient-style-music-love-etlx-247345",
    "SFX/Swing_A", "SFX/Swing_B", "SFX/Swing_Heavy",
    "SFX/Dash", "SFX/Draw",
    "SFX/Hit_Blade", "SFX/Hit_Flesh", "SFX/Footstep", "SFX/UI_Click",
};
int ok = 0;
foreach (var c in clips)
{
    var clip = Resources.Load<AudioClip>(c);
    if (clip != null) ok++;
    sb.AppendLine(string.Format("    {0,-58} {1}", c, clip != null ? "OK " + clip.length.ToString("F2") + "s" : "缺失"));
}
sb.AppendLine("    → " + ok + "/" + clips.Length + " 可用");
sb.AppendLine();

// ---- 2) 挂 AudioDirector ----
string prefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
if (root == null) sb.AppendLine("[2] !! 无法读取预制体");
else
{
    var pc = root.GetComponent<InkWash.Player.PlayerController>();
    var ad = root.GetComponent<InkWash.Core.AudioDirector>();
    if (ad == null)
    {
        ad = root.AddComponent<InkWash.Core.AudioDirector>();
        sb.AppendLine("[2] 已挂载 AudioDirector");
    }
    else sb.AppendLine("[2] AudioDirector 已存在");

    ad.player = pc;
    ad.playMusicOnStart = true;
    ad.musicClip = "BGM/et11lx-chinese-ancient-style-music-love-etlx-247345";
    ad.musicVolume = 0.45f;
    ad.enableSwingSfx = true;
    ad.swingVolume = 0.65f;
    ad.dashVolume = 0.55f;

    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
    UnityEditor.PrefabUtility.UnloadPrefabContents(root);
    sb.AppendLine("    预制体已保存: " + prefabPath);
}

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
    UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
    "Assets/_Project/Scenes/Main.unity");

// ---- 3) 复核场景实例 ----
sb.AppendLine();
sb.AppendLine("[3] 场景实例复核:");
var playerGo = GameObject.Find("Player");
if (playerGo == null) sb.AppendLine("    找不到 Player");
else
{
    sb.AppendLine("    AudioDirector: " + (playerGo.GetComponent<InkWash.Core.AudioDirector>() != null ? "已挂" : "缺失"));
    sb.AppendLine("    SwordVfx     : " + (playerGo.GetComponent<InkWash.Effects.SwordVfx>() != null ? "已挂" : "缺失"));
    var v = playerGo.transform.Find("Visual");
    sb.AppendLine("    Visual 组件  : " + (v != null ? string.Join(",", System.Array.ConvertAll(
        v.GetComponents<Component>(), c => c == null ? "<Missing>" : c.GetType().Name)) : "?"));
}
return sb.ToString();
