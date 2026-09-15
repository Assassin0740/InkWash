// g-9：场景布置（编辑态）—— ① 院墙/石门加瓦檐 ② 墙外环带布置自然道具
//
// 为什么这么做：素材库里没有建筑类素材（Kenney 三包全是音效、KayKit 是角色/骷髅、
// Quaternius 只有 StylizedNatureMegaKit 这一套树石草木）。所以"墙还是白盒"不是漏换，
// 是确实没有可用的墙。院墙的中式感用 Cube 拼**瓦檐**来给（成本最低、辨识度最高），
// 空间层次则交给自然素材 —— 但它们全部布置在**内外墙之间的环带**（|x| 或 |z| 在 10~21），
// 玩家在内院隔着 2.5m 高的石门能看见树石剪影，却不占战斗场地、不改导航网格。
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

string F(float v) { return v.ToString("0.###"); }

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
    sb.AppendLine("当前场景 = " + scene.path + "  isDirty=" + scene.isDirty);
    if (scene.path != "Assets/_Project/Scenes/Main.unity")
    {
        Debug.LogError("[g_props] 当前不是 Main.unity，已中止以免改错场景");
        yield break;
    }

    // ---------- 0) 备份 ----------
    string bak = Path.Combine(projRoot, "Tools/tmp/Main.unity.bak_before_props");
    File.Copy(Path.Combine(projRoot, scene.path), bak, true);
    sb.AppendLine("已备份 -> Tools/tmp/Main.unity.bak_before_props  (" + new FileInfo(bak).Length + " bytes)");
    sb.AppendLine();

    // ---------- 1) 材质 ----------
    Material CloneMat(string name, string from, string shaderMustBe)
    {
        string path = "Assets/_Project/Art/Materials/" + name + ".mat";
        var old = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (old != null) AssetDatabase.DeleteAsset(path);
        var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/" + from + ".mat");
        if (src == null) { sb.AppendLine("  ** 找不到模板 " + from); return null; }
        if (shaderMustBe != null && src.shader.name != shaderMustBe)
            sb.AppendLine("  !! 模板 " + from + " 的 shader 是 " + src.shader.name + "，期望 " + shaderMustBe);
        var m = new Material(src) { name = name };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // 瓦檐：比墙浓一点，才有"压顶"的重量感
    var mEave = CloneMat("M_Ink_Eave", "M_Whitebox_Wall", "InkWash/InkSurface");
    if (mEave != null)
    {
        mEave.SetFloat("_BandBias", -0.22f);
        mEave.SetFloat("_Bands", 1f);
        mEave.SetFloat("_OutlineWidth", 0.03f);
        EditorUtility.SetDirty(mEave);
        sb.AppendLine("材质 M_Ink_Eave  _BandBias=-0.22  _Bands=1  _OutlineWidth=0.03");
    }
    // 自然道具：一个偏淡的墨块材质
    var mProp = CloneMat("M_Ink_Prop", "M_Whitebox_Pillar", "InkWash/InkSurface");
    if (mProp != null)
    {
        mProp.SetFloat("_BandBias", 0.02f);
        mProp.SetFloat("_Bands", 1f);
        mProp.SetFloat("_GrainAmp", 0.16f);
        mProp.SetFloat("_StrokeAmp", 0.10f);
        mProp.SetFloat("_OutlineWidth", 0.022f);
        EditorUtility.SetDirty(mProp);
        sb.AppendLine("材质 M_Ink_Prop  _BandBias=0.02  _Bands=1  _OutlineWidth=0.022");
    }
    AssetDatabase.SaveAssets();

    // ---------- 2) 瓦檐 ----------
    var envRoot = GameObject.Find("Environment");
    if (envRoot == null) { Debug.LogError("[g_props] 找不到 Environment"); yield break; }

    var eaveRoot = GameObject.Find("Environment/Eaves");
    if (eaveRoot == null) eaveRoot = new GameObject("Eaves");
    eaveRoot.transform.SetParent(envRoot.transform, false);

    // 位置、尺寸（比对应墙体宽 0.8m 做外挑、长 1.0m 两端各出 0.5m）
    var eaves = new (string name, Vector3 pos, Vector3 size)[]
    {
        ("Eave_N", new Vector3(0f, 4.16f, 22f),  new Vector3(45.6f, 0.32f, 1.40f)),
        ("Eave_S", new Vector3(0f, 4.16f, -22f), new Vector3(45.6f, 0.32f, 1.40f)),
        ("Eave_E", new Vector3(22f, 4.16f, 0f),  new Vector3(1.40f, 0.32f, 45.6f)),
        ("Eave_W", new Vector3(-22f, 4.16f, 0f), new Vector3(1.40f, 0.32f, 45.6f)),
        ("EaveGate_N", new Vector3(0f, 2.94f, 10f),  new Vector3(21.8f, 0.28f, 1.20f)),
        ("EaveGate_S", new Vector3(0f, 2.94f, -10f), new Vector3(21.8f, 0.28f, 1.20f)),
        ("EaveGate_E", new Vector3(10f, 2.94f, 0f),  new Vector3(1.20f, 0.28f, 21.8f)),
        ("EaveGate_W", new Vector3(-10f, 2.94f, 0f), new Vector3(1.20f, 0.28f, 21.8f)),
    };
    sb.AppendLine();
    sb.AppendLine("=== 瓦檐 ===");
    foreach (var e in eaves)
    {
        var exist = GameObject.Find("Environment/Eaves/" + e.name);
        if (exist != null) UnityEngine.Object.DestroyImmediate(exist);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = e.name;
        go.transform.position = e.pos;
        go.transform.localScale = e.size;
        go.transform.SetParent(eaveRoot.transform, true);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        if (mEave != null) go.GetComponent<MeshRenderer>().sharedMaterial = mEave;
        sb.AppendLine(string.Format("  {0,-13} pos={1} size={2}", e.name, e.pos.ToString("F2"), e.size.ToString("F2")));
    }

    // ---------- 3) 自然道具 ----------
    const string NAT = "Assets/ThirdParty/Quaternius/StylizedNatureMegaKit/FBX (Unity)/";
    var propRoot = GameObject.Find("Environment/Props");
    if (propRoot == null) propRoot = new GameObject("Props");
    propRoot.transform.SetParent(envRoot.transform, false);

    // 全部落在内外墙之间（|x| 或 |z| ∈ 11~20），不占战斗场地
    var props = new (string fbx, Vector3 pos, float targetSize, float yaw)[]
    {
        // 四角松树：从内院越过石门能看到树冠，给场地"墙外有林"的纵深
        ("Pine_1", new Vector3(17.5f, 0f, 17.5f), 8.0f, 0f),
        ("Pine_3", new Vector3(-17.5f, 0f, 17.5f), 9.0f, 40f),
        ("Pine_2", new Vector3(17.5f, 0f, -17.5f), 7.5f, 110f),
        ("Pine_4", new Vector3(-17.5f, 0f, -17.5f), 8.5f, 210f),
        ("Pine_5", new Vector3(19.5f, 0f, 0f), 7.0f, 75f),
        ("Pine_1", new Vector3(-19.5f, 0f, 0f), 7.5f, 300f),
        // 枯树：禅院的萧疏感
        ("DeadTree_2", new Vector3(13.5f, 0f, -14f), 6.0f, 20f),
        ("DeadTree_4", new Vector3(-13.5f, 0f, 14f), 5.5f, 155f),
        ("TwistedTree_1", new Vector3(14.5f, 0f, 14.5f), 6.2f, 260f),
        ("TwistedTree_3", new Vector3(-14.5f, 0f, -14.5f), 5.8f, 95f),
        // 石头：沿墙脚散布
        ("Rock_Medium_1", new Vector3(19.0f, 0f, 8.5f), 1.5f, 0f),
        ("Rock_Medium_2", new Vector3(19.2f, 0f, -7.5f), 1.2f, 65f),
        ("Rock_Medium_3", new Vector3(-19.0f, 0f, 9.5f), 1.7f, 130f),
        ("Rock_Medium_1", new Vector3(-19.2f, 0f, -8.5f), 1.4f, 200f),
        ("Rock_Medium_2", new Vector3(8.5f, 0f, 19.0f), 1.6f, 15f),
        ("Rock_Medium_3", new Vector3(-8.5f, 0f, -19.0f), 1.3f, 285f),
        // 灌木
        ("Bush_Common", new Vector3(15.5f, 0f, 12.5f), 1.6f, 0f),
        ("Bush_Common", new Vector3(-15.5f, 0f, -12.5f), 1.4f, 80f),
        ("Bush_Common", new Vector3(12.5f, 0f, -16.5f), 1.5f, 170f),
        ("Bush_Common", new Vector3(-12.5f, 0f, 16.5f), 1.7f, 250f),
    };

    sb.AppendLine();
    sb.AppendLine("=== 自然道具 ===");
    foreach (var p in props)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NAT + p.fbx + ".fbx");
        if (prefab == null) { sb.AppendLine("  ** 加载失败 " + p.fbx); continue; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = p.fbx;
        go.transform.SetParent(propRoot.transform, true);
        go.transform.position = p.pos;
        go.transform.rotation = Quaternion.Euler(0f, p.yaw, 0f);

        // 归一化尺寸：最大边缩到目标值（原模型尺寸各异，直接摆会大小不一）
        var b0 = WorldBounds(go);
        float maxE = Mathf.Max(b0.size.x, Mathf.Max(b0.size.y, b0.size.z));
        if (maxE > 1e-4f) go.transform.localScale = Vector3.one * (p.targetSize / maxE);

        // 让**底部**落在 y=0 而不是原点 —— 否则松树会悬空/陷地
        var b1 = WorldBounds(go);
        go.transform.position += Vector3.up * (0f - b1.min.y);

        // 道具不参与碰撞与寻路（导航网格已烘好，不重烘）
        foreach (var c in go.GetComponentsInChildren<Collider>())
            UnityEngine.Object.DestroyImmediate(c);

        var b2 = WorldBounds(go);
        if (mProp != null)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = mProp;

        sb.AppendLine(string.Format("  {0,-16} pos={1}  缩放后高={2}  底Y={3}  渲染器={4}",
            p.fbx, p.pos.ToString("F1"), F(b2.size.y), F(b2.min.y), go.GetComponentsInChildren<Renderer>().Length));
    }

    // ---------- 4) 保存 ----------
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    bool ok = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + ok);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/g_props.txt"), sb.ToString());
    Debug.Log("[g_props] done\n" + sb.ToString());
    yield return null;
}

return Body();
