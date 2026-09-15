// g-10：修道具材质（前一次赋值没落盘）+ 调整树位构图
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

var sb = new StringBuilder();
string projRoot = Path.GetDirectoryName(Application.dataPath);

Bounds WorldBounds(GameObject go)
{
    var rs = go.GetComponentsInChildren<Renderer>();
    if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
    var b = rs[0].bounds;
    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
    return b;
}

IEnumerator Body()
{
    yield return null;
    var scene = SceneManager.GetActiveScene();
    if (scene.path != "Assets/_Project/Scenes/Main.unity") { Debug.LogError("[g_props2] 场景不对"); yield break; }

    string bak = Path.Combine(projRoot, "Tools/tmp/Main.unity.bak_after_props");
    File.Copy(Path.Combine(projRoot, scene.path), bak, true);
    sb.AppendLine("备份 -> Tools/tmp/Main.unity.bak_after_props");
    sb.AppendLine();

    var mProp = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Prop.mat");
    sb.AppendLine("M_Ink_Prop 资产 = " + (mProp == null ? "** 不存在 **" : mProp.name + " shader=" + mProp.shader.name));
    if (mProp == null) { Debug.LogError("[g_props2] 缺 M_Ink_Prop"); yield break; }
    sb.AppendLine();

    var propRoot = GameObject.Find("Environment/Props");
    if (propRoot == null) { Debug.LogError("[g_props2] 找不到 Environment/Props"); yield break; }

    // ---------- ① 诊断当前材质 ----------
    sb.AppendLine("=== 修复前 ===");
    var all = propRoot.GetComponentsInChildren<Renderer>();
    foreach (var r in all)
    {
        var m = r.sharedMaterials.Length > 0 ? r.sharedMaterials[0] : null;
        sb.AppendLine("  " + r.transform.parent.name + " → " + (m == null ? "null" : m.name));
    }

    // ---------- ② 重新赋材质（写整个数组，形成 prefab override）----------
    int fixedN = 0;
    foreach (var r in all)
    {
        var arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
        for (int i = 0; i < arr.Length; i++) arr[i] = mProp;
        r.sharedMaterials = arr;
        EditorUtility.SetDirty(r);
        fixedN++;
    }
    sb.AppendLine();
    sb.AppendLine("重新赋材质渲染器数 = " + fixedN);

    // ---------- ③ 调整松树/树位，改善构图 ----------
    // 原来贴着 (±17.5,±17.5) 的角，在 48° FOV 下正好被屏幕边缘裁掉，读起来像"贴纸"。
    // 往内收到 ±13 一带：仍在内院之外（不占战斗场地），但会完整落进画面。
    var moves = new (string name, int index, Vector3 pos, float yaw)[]
    {
        ("Pine_1", 0, new Vector3(13.0f, 0f, 13.0f), 15f),
        ("Pine_3", 1, new Vector3(-13.0f, 0f, 13.0f), 55f),
        ("Pine_2", 2, new Vector3(13.0f, 0f, -13.0f), 125f),
        ("Pine_4", 3, new Vector3(-13.0f, 0f, -13.0f), 215f),
        ("TwistedTree_1", 8, new Vector3(12.0f, 0f, 15.5f), 260f),
        ("TwistedTree_3", 9, new Vector3(-12.0f, 0f, -15.5f), 95f),
        ("DeadTree_2", 6, new Vector3(15.5f, 0f, -12.0f), 20f),
        ("DeadTree_4", 7, new Vector3(-15.5f, 0f, 12.0f), 155f),
    };
    sb.AppendLine();
    sb.AppendLine("=== 树位调整 ===");
    for (int i = 0; i < propRoot.transform.childCount; i++)
    {
        var c = propRoot.transform.GetChild(i);
        foreach (var mv in moves)
        {
            if (c.name != mv.name) continue;
            c.position = mv.pos;
            c.rotation = Quaternion.Euler(0f, mv.yaw, 0f);
            var b = WorldBounds(c.gameObject);
            c.position += Vector3.up * (0f - b.min.y);
            sb.AppendLine("  " + mv.name + " idx" + i + " → " + mv.pos.ToString("F1"));
            break;
        }
    }

    // ---------- ④ 保存（★ 不再 AssetDatabase.Refresh()：它会重导 fbX 并回滚实例 override）----------
    EditorSceneManager.MarkSceneDirty(scene);
    bool ok = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + ok + "（未调用 AssetDatabase.Refresh）");

    // ---------- ⑤ 复核 ----------
    sb.AppendLine();
    sb.AppendLine("=== 修复后 ===");
    foreach (var r in propRoot.GetComponentsInChildren<Renderer>())
    {
        var m = r.sharedMaterials.Length > 0 ? r.sharedMaterials[0] : null;
        sb.AppendLine("  " + r.transform.parent.name + " → " + (m == null ? "null" : m.name));
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_props2.txt"), sb.ToString());
    Debug.Log("[g_props2] done\n" + sb.ToString());
    yield return null;
}

return Body();
