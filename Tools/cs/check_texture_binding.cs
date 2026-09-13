// 验证：第三方 FBX 是否成功绑定了贴图。
// 这是"整包移动而不拆散"做法的关键验证点 ——
// Unity 的 FBX 导入器按 Recursive-Up 搜索贴图，
// 若 Exports/FBX 与 Textures 的相对层级被破坏，这里会看到 baseTex=NULL。
var sb = new System.Text.StringBuilder();

string[] fbxPaths = new string[] {
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters/Exports/FBX (Unity)/Imp.fbx",
    "Assets/ThirdParty/Quaternius/Bestiary-DungeonMonsters/Exports/FBX (Unity)/Puglin.fbx",
    "Assets/ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy/Modular Character Outfits - Fantasy[Standard]/Exports/FBX (Unity)/Outfits/Male_Ranger.fbx",
    "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary2/Unity/UAL2_Standard.fbx",
};

foreach (var p in fbxPaths)
{
    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
    string shortName = System.IO.Path.GetFileName(p);
    if (go == null)
    {
        sb.AppendLine(shortName + "  ==> 加载失败（路径不对或未导入）");
        continue;
    }

    int renderers = 0, mats = 0, withTex = 0, nullTex = 0;
    var names = new System.Collections.Generic.List<string>();
    foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
    {
        renderers++;
        foreach (var m in r.sharedMaterials)
        {
            if (m == null) continue;
            mats++;
            UnityEngine.Texture tex = null;
            if (m.HasProperty("_BaseMap")) tex = m.GetTexture("_BaseMap");
            else if (m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
            if (tex == null && m.mainTexture != null) tex = m.mainTexture;

            if (tex != null) { withTex++; names.Add(m.name + " → " + tex.name); }
            else { nullTex++; names.Add(m.name + " → NULL"); }
        }
    }

    // 骨骼与动画剪辑数量
    int clips = 0;
    var anim = go.GetComponent<UnityEngine.Animator>();
    var bones = go.GetComponentsInChildren<UnityEngine.Transform>(true).Length;

    var allAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
    foreach (var a in allAssets)
    {
        if (a is UnityEngine.AnimationClip) clips++;
    }

    sb.AppendLine(string.Format(
        "{0}: Renderer={1} 材质={2} 有贴图={3} 无贴图={4} 子节点(含骨骼)={5} 动画剪辑={6}",
        shortName, renderers, mats, withTex, nullTex, bones, clips));
    int show = System.Math.Min(names.Count, 3);
    for (int i = 0; i < show; i++) sb.AppendLine("      · " + names[i]);
}

return sb.ToString();
