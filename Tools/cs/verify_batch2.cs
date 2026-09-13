// 第二批素材验收：
// 1) Stylized Nature MegaKit —— 贴图是否绑上（整包移动的关键验证点）
// 2) Kenney Impact Sounds —— 音频剪辑数与加载情况
// 3) 中国风 BGM —— 是否被 Unity 识别为 AudioClip
var sb = new System.Text.StringBuilder();

string natureDir = "Assets/ThirdParty/Quaternius/StylizedNatureMegaKit/FBX (Unity)";
var guids = UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { natureDir });

int total = 0, withTex = 0, nullTex = 0, noMat = 0;
var bad = new System.Collections.Generic.List<string>();
var sample = new System.Collections.Generic.List<string>();

foreach (var g in guids)
{
    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
    if (go == null) continue;
    total++;

    int localWith = 0, localNull = 0, localMat = 0;
    foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
    {
        foreach (var m in r.sharedMaterials)
        {
            if (m == null) continue;
            localMat++;
            UnityEngine.Texture tex = null;
            if (m.HasProperty("_BaseMap")) tex = m.GetTexture("_BaseMap");
            else if (m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
            if (tex == null && m.mainTexture != null) tex = m.mainTexture;
            if (tex != null) { localWith++; sample.Add(System.IO.Path.GetFileNameWithoutExtension(p) + " → " + tex.name); }
            else localNull++;
        }
    }
    if (localMat == 0) noMat++;
    withTex += localWith;
    nullTex += localNull;
    if (localNull > 0) bad.Add(System.IO.Path.GetFileNameWithoutExtension(p) + "(缺" + localNull + ")");
}

sb.AppendLine("【Nature MegaKit】FBX 总数=" + total
    + " | 材质槽有贴图=" + withTex + " 无贴图=" + nullTex
    + " | 无材质的模型=" + noMat);
if (bad.Count > 0)
{
    int show = System.Math.Min(bad.Count, 12);
    sb.AppendLine("  ⚠ 存在未绑定贴图的模型（前 " + show + " 个）:");
    for (int i = 0; i < show; i++) sb.AppendLine("      · " + bad[i]);
}
else sb.AppendLine("  ✓ 全部模型贴图绑定成功");

int sc = System.Math.Min(sample.Count, 5);
for (int i = 0; i < sc; i++) sb.AppendLine("      样例 " + sample[i]);

// --- Kenney Impact Sounds ---
var sndGuids = UnityEditor.AssetDatabase.FindAssets("t:AudioClip",
    new[] { "Assets/ThirdParty/Kenney/Impact-Sounds" });
sb.AppendLine("");
sb.AppendLine("【Kenney Impact Sounds】AudioClip 数=" + sndGuids.Length);

// --- BGM ---
string bgm = "Assets/_Project/Audio/BGM/et11lx-chinese-ancient-style-music-love-etlx-247345.mp3";
var bgmClip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>(bgm);
if (bgmClip != null)
{
    sb.AppendLine("");
    sb.AppendLine("【BGM】" + bgmClip.name
        + " | 时长=" + bgmClip.length.ToString("F1") + "s"
        + " | 采样率=" + bgmClip.frequency
        + " | 声道=" + bgmClip.channels);
}
else
{
    sb.AppendLine("");
    sb.AppendLine("【BGM】加载失败（路径不对或未导入）");
}

return sb.ToString();
