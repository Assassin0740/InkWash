// a38_dragon_axis.cs —— 用 Unity 自己的 API 定龙的**真实尺寸与朝向**（不再靠包围盒猜）
//
// 为什么重开一轮：
//   a24~a37 我在"包围盒口径"上绕了八轮，每轮都推翻前一轮。
//   根因不是口径复杂，是**我没看过一张能说明问题的图**：
//   a37 渲染的 4 张图里，龙**根本没出现**（只有参照方块），而我先入为主把它当成"朝向不对"。
//   后来静态解析 Z_Dragon.prefab 才看清事实：
//     · 骨架是**单链**：_rootJoint → drgon_03 → drgon_04 → … → drgon_026，共 24 节
//     · 链上每节 localPosition 都是**纯 -X 位移**（累计 -4042 源单位）
//     · 换算 [×0.0024 ×0.7546 = 0.001811 m/单位] ⇒ **链沿 -X 走 7.32 m、Y 只 0.55 m、Z 只 1.80 m**
//   ⇒ **龙的身体主轴就是 X**，蛇一样盘在 X 方向上。
//   ⇒ 我在 a37 里转的 rot.y=180° 完全没解决问题（绕 Y 转不改变 X 轴朝向）。
//
// 本脚本要做的事（只做三件，不再夹带测量洁癖）：
//   ① 用 Mesh 顶点真值（Unity 解出来的 Mesh，不是我自己解 FBX）定**未蒙皮时的轴向与尺寸**
//   ② 用骨骼世界坐标定**蒙皮后的实际占位**（这是角色在场景里真实占的空间）
//   ③ 按 ② 的结果把龙**摆正**：长轴对到 +Z、头朝 +Z、脚落在 y=0
//      然后**渲染 8 个方向的图**（每 45°），保证不管头朝哪都能在图里看见它
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(root, "Tools/screenshots/dragon38");
Directory.CreateDirectory(shotDir);

sb.AppendLine("========== a38 用 Unity API 定龙的尺寸/轴向 ==========");

// ---------- ① 源 prefab：网格顶点真值 + 骨骼世界范围 ----------
var srcContents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
try
{
    sb.AppendLine();
    sb.AppendLine("---- ① 源 prefab：网格顶点真值 ----");
    foreach (var smr in srcContents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var m = smr.sharedMesh;
        if (m == null) continue;
        var vs = m.vertices;
        var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in vs) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
        sb.AppendLine("  " + smr.name + "  顶点=" + vs.Length);
        sb.AppendLine("    mesh 局部 X[" + mn.x.ToString("F2") + "," + mx.x.ToString("F2")
                      + "] Y[" + mn.y.ToString("F2") + "," + mx.y.ToString("F2")
                      + "] Z[" + mn.z.ToString("F2") + "," + mx.z.ToString("F2") + "]");
        sb.AppendLine("    尺寸=(" + (mx.x - mn.x).ToString("F2") + ", " + (mx.y - mn.y).ToString("F2")
                      + ", " + (mx.z - mn.z).ToString("F2") + ")");
        sb.AppendLine("    mesh.bounds.size=" + m.bounds.size.ToString("F2")
                      + "  smr.bounds.size=" + smr.bounds.size.ToString("F2"));
        sb.AppendLine("    smr.lossyScale=" + smr.transform.lossyScale.ToString("F4"));
    }

    sb.AppendLine();
    sb.AppendLine("---- ② 源 prefab：骨骼世界范围（= 场景里实际占的空间）----");
    {
        var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        int n = 0;
        foreach (var smr in srcContents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.bones == null) continue;
            foreach (var b in smr.bones)
            {
                if (b == null) continue;
                mn = Vector3.Min(mn, b.position); mx = Vector3.Max(mx, b.position); n++;
            }
        }
        var sz = mx - mn;
        sb.AppendLine("  骨骼数=" + n);
        sb.AppendLine("  范围 [" + mn.ToString("F2") + "] ~ [" + mx.ToString("F2") + "]");
        sb.AppendLine("  尺寸=(" + sz.x.ToString("F2") + ", " + sz.y.ToString("F2") + ", " + sz.z.ToString("F2") + ")");
        string axis = "X";
        if (sz.y > sz.x && sz.y > sz.z) axis = "Y";
        else if (sz.z > sz.x && sz.z > sz.y) axis = "Z";
        sb.AppendLine("  ★ 最长轴 = " + axis + "  （龙的身体主轴）");

        // 头在哪一端？脊骨链按名字排序的末端（drgon_00 是最后一节）
        Transform tip = null;
        foreach (var smr in srcContents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.bones == null) continue;
            foreach (var b in smr.bones)
            {
                if (b == null) continue;
                if (b.name == "drgon_00") { tip = b; break; }
            }
            if (tip != null) break;
        }
        if (tip != null)
        {
            sb.AppendLine("  末端骨 drgon_00 世界位置 = " + tip.position.ToString("F3"));
            sb.AppendLine("  根部骨 _rootJoint 世界位置 = " + srcContents.transform.position.ToString("F3"));
            var d = tip.position - srcContents.transform.position;
            sb.AppendLine("  ★ 头相对根的位移 = " + d.ToString("F3") + "  ⇒ 头朝 " + Dominant(d));
        }
    }
}
finally { PrefabUtility.UnloadPrefabContents(srcContents); }

// ---------- ③ 从零重建 Z_Enemy_MoLong，按骨长轴摆正 ----------
sb.AppendLine();
sb.AppendLine("---- ③ 重建 Z_Enemy_MoLong（按骨长轴摆正）----");
string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outPath = EnemyDir + "/Z_Enemy_MoLong.prefab";
if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null) AssetDatabase.DeleteAsset(outPath);

var tmpl = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");

int dirty = 0;
foreach (var t in tmpl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) dirty++;
if (dirty > 0) { sb.AppendLine("★★ 模板被污染(" + dirty + ")，中止"); Write(); Debug.LogError("[a38]"); return; }

var host = (GameObject)PrefabUtility.InstantiatePrefab(tmpl);
host.transform.position = Vector3.zero;
host.transform.rotation = Quaternion.identity;
host.transform.localScale = Vector3.one;
var visual = host.transform.Find("Visual");
if (visual == null) { var v = new GameObject("Visual"); v.transform.SetParent(host.transform, false); visual = v.transform; }
for (int i = visual.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(visual.GetChild(i).gameObject);
for (int i = host.transform.childCount - 1; i >= 0; i--)
{
    var c = host.transform.GetChild(i);
    if (c.name != "Visual") UnityEngine.Object.DestroyImmediate(c.gameObject);
}
visual.localScale = Vector3.one;   // 归一，后面所有尺度只由 holder 控制（单一所有权）

var holder = new GameObject("Model");
holder.transform.SetParent(visual, false);
holder.transform.localPosition = Vector3.zero;
holder.transform.localRotation = Quaternion.identity;
holder.transform.localScale = Vector3.one;

var contents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var names = new List<string>();
try
{
    for (int i = 0; i < contents.transform.childCount; i++)
    {
        var ch = contents.transform.GetChild(i);
        names.Add(ch.name);
        var clone = UnityEngine.Object.Instantiate(ch.gameObject);
        clone.name = ch.name;
        clone.transform.SetParent(holder.transform, false);
        clone.transform.localPosition = ch.localPosition;
        clone.transform.localRotation = ch.localRotation;
        clone.transform.localScale = ch.localScale;
    }
}
finally { PrefabUtility.UnloadPrefabContents(contents); }
sb.AppendLine("  克隆搬入 ×" + names.Count + " → " + string.Join(", ", names.ToArray()));

// 量一次基线（scale=1, rot=0）
Vector3 Bounds(out Vector3 mn, out Vector3 mx, out string axisOut)
{
    mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    var smrs = host.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    foreach (var smr in smrs)
    {
        if (smr.bones == null) continue;
        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            mn = Vector3.Min(mn, b.position); mx = Vector3.Max(mx, b.position);
        }
    }
    var s = mx - mn;
    axisOut = (s.x >= s.y && s.x >= s.z) ? "X" : (s.y >= s.z ? "Y" : "Z");
    return s;
}

{
    var s = Bounds(out var mn0, out var mx0, out var axis0);
    sb.AppendLine("  基线(scale=1 rot=0): 尺寸=(" + s.x.ToString("F2") + ", " + s.y.ToString("F2") + ", " + s.z.ToString("F2") + ")  最长轴=" + axis0);
    sb.AppendLine("    Y 范围 [" + mn0.y.ToString("F2") + ", " + mx0.y.ToString("F2") + "]");

    // 长轴对到 +Z：X 轴 → 绕 Y 转 -90°；Z 轴 → 不转；Y 轴 → 绕 X 转 -90°（不合理，龙不该竖着）
    if (axis0 == "X") holder.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
    else if (axis0 == "Y") holder.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

    var s2 = Bounds(out var mn2, out var mx2, out var axis2);
    sb.AppendLine("  长轴对+Z后: 尺寸=(" + s2.x.ToString("F2") + ", " + s2.y.ToString("F2") + ", " + s2.z.ToString("F2") + ")  最长轴=" + axis2);

    // 落地
    holder.transform.localPosition = new Vector3(0f, -mn2.y, 0f);
    var s3 = Bounds(out var mn3, out var mx3, out _);
    sb.AppendLine("  落地后: Y[" + mn3.y.ToString("F3") + ", " + mx3.y.ToString("F3") + "]  尺寸=(" + s3.x.ToString("F2") + ", " + s3.y.ToString("F2") + ", " + s3.z.ToString("F2") + ")");
    sb.AppendLine("  ★ 最终体长(Z) = " + s3.z.ToString("F2") + " m   高(Y) = " + s3.y.ToString("F2") + " m");
}

// 组件
var oldE = host.GetComponent<InkWash.Enemies.EnemyBase>();
if (oldE != null) UnityEngine.Object.DestroyImmediate(oldE);
var dragon = host.AddComponent<InkWash.Enemies.EnemyDragon>();
dragon.enemyName = "墨龙";
dragon.maxHealth = 620f; dragon.walkSpeed = 2.2f; dragon.chaseSpeed = 4.4f;
dragon.sightRange = 30f; dragon.loseSightRange = 40f;
dragon.attackRange = 7.0f; dragon.attackCooldown = 0.9f;
dragon.xpReward = 120; dragon.spawnDelay = 0.6f; dragon.destroyAfterDeath = 3.5f;
dragon.eyeOffset = new Vector3(0f, 2f, 1.5f);

var sw = host.GetComponent<InkWash.Rendering.InkMaterialSwap>() ?? host.AddComponent<InkWash.Rendering.InkMaterialSwap>();
if (mat != null)
{
    sw.inkMaterials = new[] { mat };
    foreach (var r in host.GetComponentsInChildren<Renderer>(true))
    {
        var ms = r.sharedMaterials;
        if (ms.Length == 0) r.sharedMaterials = new[] { mat };
        else { for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms; }
    }
}
var cap = host.GetComponent<CapsuleCollider>();
if (cap == null) cap = host.AddComponent<CapsuleCollider>();
cap.height = 3.0f; cap.radius = 1.4f; cap.center = new Vector3(0f, 1.5f, 0f);
cap.direction = 1;
var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null) { var g = new GameObject("Hitbox"); g.transform.SetParent(host.transform, false); hb = g.AddComponent<InkWash.Combat.Hitbox>(); }
hb.owner = host; hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 1.0f, 0.5f); hb.pointB = new Vector3(0f, 1.2f, 4.0f); hb.radius = 1.4f;
hb.damage = 22f; hb.knockback = 7f; hb.hitStun = 0.45f; hb.hitStop = 0.07f;
var mz = host.transform.Find("Muzzle");
if (mz == null) { var g = new GameObject("Muzzle"); g.transform.SetParent(host.transform, false); mz = g.transform; }
mz.localPosition = new Vector3(0f, 1.8f, 3.0f);

host.name = "Z_Enemy_MoLong";
PrefabUtility.SaveAsPrefabAsset(host, outPath);
UnityEngine.Object.DestroyImmediate(host);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
sb.AppendLine("  回读 " + outPath + " = " + (saved != null ? "存在 ✓" : "★ 不存在"));

// ---------- ④ 渲染 8 个方向 ----------
sb.AppendLine();
sb.AppendLine("---- ④ 渲染 8 方向（每 45°）----");
var tmpScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
    UnityEditor.SceneManagement.NewSceneMode.Single);

var gnd = GameObject.CreatePrimitive(PrimitiveType.Cube);
gnd.transform.position = new Vector3(0f, -0.05f, 0f);
gnd.transform.localScale = new Vector3(60f, 0.1f, 60f);

var refCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
refCube.transform.position = new Vector3(4f, 1.0f, 4f);
refCube.transform.localScale = new Vector3(0.6f, 2.0f, 0.6f);  // 2 m = 主角身高

var inst = (GameObject)PrefabUtility.InstantiatePrefab(saved);
inst.transform.position = Vector3.zero;
inst.transform.rotation = Quaternion.identity;

var lg = new GameObject("Sun");
var lt = lg.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.35f;
lg.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

var camGo = new GameObject("Cam");
var cam = camGo.AddComponent<Camera>();
cam.clearFlags = CameraClearFlags.SolidColor;
cam.backgroundColor = new Color(0.93f, 0.93f, 0.91f, 1f);

void Shoot(string name, Vector3 pos, Vector3 look, float fov)
{
    camGo.transform.position = pos;
    camGo.transform.LookAt(look);
    cam.fieldOfView = fov;
    var rt = new RenderTexture(900, 700, 24, RenderTextureFormat.ARGB32);
    rt.antiAliasing = 4;
    cam.targetTexture = rt;
    cam.Render();
    RenderTexture.active = rt;
    var t = new Texture2D(900, 700, TextureFormat.RGB24, false);
    t.ReadPixels(new Rect(0, 0, 900, 700), 0, 0);
    t.Apply();
    RenderTexture.active = null;
    cam.targetTexture = null;
    File.WriteAllBytes(Path.Combine(shotDir, name + ".png"), t.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(t);
    rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
    sb.AppendLine("  " + name);
}

// 相机绕 Y 每 45° 一张，俯角 18°，距 16 m，看得见 7 m 长的东西
for (int k = 0; k < 8; k++)
{
    float a = k * 45f * Mathf.Deg2Rad;
    var p = new Vector3(Mathf.Sin(a) * 15f, 6.5f, Mathf.Cos(a) * 15f);
    Shoot("A" + k + "_" + (k * 45) + "deg", p, new Vector3(0f, 1.4f, 0f), 42f);
}

// 正上方俯瞰（最容易看清蛇形盘绕的整体形状）
Shoot("A8_俯视", new Vector3(0.01f, 18f, 0.01f), Vector3.zero, 50f);

UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
    UnityEditor.SceneManagement.NewSceneMode.Single);
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity",
    UnityEditor.SceneManagement.OpenSceneMode.Single);

Write();
Debug.Log("[a38]\n" + sb.ToString());

string Dominant(Vector3 v)
{
    var a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    if (a.x >= a.y && a.x >= a.z) return v.x > 0 ? "+X" : "-X";
    if (a.y >= a.z) return v.y > 0 ? "+Y" : "-Y";
    return v.z > 0 ? "+Z" : "-Z";
}

void Write() => File.WriteAllText(Path.Combine(root, "Tools/reports/a38_dragon_axis.txt"), sb.ToString());
