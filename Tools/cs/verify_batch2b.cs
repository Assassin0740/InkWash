// 第二批之二验收：Ultimate Monsters / Universal Base Characters
// 输出：模型数、贴图绑定情况、骨骼类型（Generic / Human）、动画剪辑数
var sb = new System.Text.StringBuilder();

System.Action<string, string> scan = (label, dir) =>
{
    var guids = UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { dir });
    int total = 0, withTex = 0, nullTex = 0, human = 0, generic = 0, legacy = 0, other = 0, clips = 0;
    var noTex = new System.Collections.Generic.List<string>();
    var humans = new System.Collections.Generic.List<string>();

    foreach (var g in guids)
    {
        string p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
        var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
        if (go == null) continue;
        total++;

        int lw = 0, ln = 0;
        foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                UnityEngine.Texture t = null;
                if (m.HasProperty("_BaseMap")) t = m.GetTexture("_BaseMap");
                else if (m.HasProperty("_MainTex")) t = m.GetTexture("_MainTex");
                if (t == null && m.mainTexture != null) t = m.mainTexture;
                if (t != null) lw++; else ln++;
            }
        withTex += lw; nullTex += ln;
        if (ln > 0) noTex.Add(System.IO.Path.GetFileNameWithoutExtension(p) + "(缺" + ln + ")");

        var mi = UnityEditor.AssetImporter.GetAtPath(p) as UnityEditor.ModelImporter;
        var at = mi != null ? mi.animationType : UnityEditor.ModelImporterAnimationType.None;
        string s = at.ToString();
        if (s == "Human") { human++; if (humans.Count < 6) humans.Add(System.IO.Path.GetFileNameWithoutExtension(p)); }
        else if (s == "Generic") generic++;
        else if (s == "Legacy") legacy++;
        else other++;

        foreach (var a in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p))
            if (a is UnityEngine.AnimationClip) clips++;
    }

    sb.AppendLine("【" + label + "】模型=" + total
        + " | 材质槽有贴图=" + withTex + " 无贴图=" + nullTex
        + " | Human=" + human + " Generic=" + generic + " Legacy=" + legacy + " 其他=" + other
        + " | 动画剪辑=" + clips);
    if (humans.Count > 0) sb.AppendLine("     已人形化: " + string.Join(", ", humans));
    if (noTex.Count > 0)
    {
        int sh = System.Math.Min(noTex.Count, 8);
        sb.AppendLine("     ⚠ 贴图缺失 " + noTex.Count + " 个，示例: " + string.Join(", ", noTex.GetRange(0, sh)));
    }
};

string TP = "Assets/ThirdParty/Quaternius/";
scan("Ultimate Monsters", TP + "UltimateMonsters");
scan("UBC 基础角色", TP + "UniversalBaseCharacters/Base Characters");
scan("UBC 发型", TP + "UniversalBaseCharacters/Hairstyles");
scan("Bestiary(对照)", TP + "Bestiary-DungeonMonsters");

return sb.ToString();
