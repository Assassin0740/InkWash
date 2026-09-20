// v42: Assets/Mixamo 下 10 个 Mixamo fbx 的 rig 设为 Humanoid 并重导入，
// 打印 Player.controller 的 Idle 态 motion 结构（是否 BlendTree）与全部参数
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string[] guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Mixamo" });
    sb.AppendLine("[v42] model count=" + guids.Length);
    foreach (var g in guids)
    {
        string path = AssetDatabase.GUIDToAssetPath(g);
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) { sb.AppendLine("[v42] no importer " + path); continue; }
        var want = (ModelImporterAnimationType)3; // Humanoid（团结引擎枚举名不一致，用数值）
        if (imp.animationType != want)
        {
            imp.animationType = want;
            imp.importAnimation = true;
            imp.SaveAndReimport();
            sb.AppendLine("[v42] set Humanoid + reimport: " + path);
        }
        else sb.AppendLine("[v42] already Humanoid: " + path);
    }

    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
    if (ctrl == null) { sb.AppendLine("[v42] controller NOT FOUND"); Debug.Log("[v42]\n" + sb); yield break; }
    sb.AppendLine("[v42] params: " + string.Join(",", System.Array.ConvertAll(ctrl.parameters, p => p.name + ":" + p.type)));
    foreach (var layer in ctrl.layers)
    {
        var sm = layer.stateMachine;
        foreach (var st in sm.states)
        {
            string motionDesc = "null";
            var m = st.state.motion;
            if (m is BlendTree bt)
            {
                motionDesc = "BlendTree(children=";
                foreach (var c in bt.children) motionDesc += (c.motion != null ? c.motion.name : "null") + " | ";
                motionDesc += ")";
            }
            else if (m != null) motionDesc = "Clip:" + m.name + " [" + AssetDatabase.GetAssetPath(m) + "]";
            sb.AppendLine("[v42] state " + st.state.name + " -> " + motionDesc);
        }
    }
    Debug.Log("[v42]\n" + sb);
    yield break;
}

return Body();
