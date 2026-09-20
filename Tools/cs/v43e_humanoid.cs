// v43e: force Humanoid rig on new Mixamo imports + loop idle, then resample candidates
using UnityEngine;
using UnityEditor;
using System.Text;

var sb = new System.Text.StringBuilder();
string[] newOnes = new string[] {
    "sword and shield slash (3).fbx",
    "sword and shield slash (4).fbx",
    "sword and shield slash (5).fbx",
    "sword and shield attack (2).fbx",
    "sword and shield attack (3).fbx",
    "sword and shield attack (4).fbx",
    "sword and shield kick.fbx"
};
foreach (var n in newOnes)
{
    string p = "Assets/Mixamo/" + n;
    var imp = AssetImporter.GetAtPath(p) as ModelImporter;
    if (imp == null) { sb.AppendLine(n + " no importer"); continue; }
    imp.animationType = (ModelImporterAnimationType)3; // Humanoid
    imp.SaveAndReimport();
    sb.AppendLine(n + " -> Humanoid, len=");
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
        if (o is AnimationClip c && !c.name.StartsWith("__preview")) sb.AppendLine("   clip " + c.name + " " + c.length.ToString("F2") + "s");
}
return sb.ToString();
