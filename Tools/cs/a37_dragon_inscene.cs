// a37_dragon_inscene.cs —— 把龙放进主场景，用**真实游戏相机**看它（终结测量螺旋）
//
// 走到这里必须承认一件事：我在"包围盒口径"上已经绕了 a24/a26/a29/a31/a33/a34/a35/a36 八轮，
//   每轮都发现前一轮的口径有问题。本项目硬规矩 3 说得对，但也提示了**止损条件**：
//   "只差一项未通过 → 先怀疑度量本身，做对照实验" —— 我已经做了对照实验，
//   得到了可靠结论（见下），现在该**换一个不依赖这些口径的判据**：人眼 / 像素。
//
// ★ 已确证的结论（不再纠结）：
//   · 源 prefab 的骨骼世界范围 = 4.38 × 1.64 × 4.27 m  ← 这**就是**龙的真实尺寸，
//     因为它是通过 Transform.position（最朴素的 API）得到的，不涉及蒙皮语义。
//   · `BakeMesh` 对祖先缩放免疫（a36 ③：k 从 0.0001 到 1000 读数恒为 2388.64）
//     ⇒ 它**不能**用来验证"我缩放对了吗"，但可以用来验证"蒙皮结构是否自洽"
//     （比值 0.48 / 1.00 ⇒ 自洽，没有爆炸）。
//   · 龙原生 lossyScale ≈ 0.0018（厘米→米），所以**不要**再乘一个大缩放。
//
// ⇒ 本脚本的做法：**把龙按"源尺寸"直接放进主场景试玩台**，
//    用真实相机渲染一张图，然后**看图**。这是唯一能终结争议的判据。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(root, "Tools/screenshots/dragon");
Directory.CreateDirectory(shotDir);

sb.AppendLine("========== a37 把龙放进场景用真机相机看 ==========");

// ---- 重造龙 prefab：这次**不做任何容器缩放**，只做"朝向 + 落地" ----
// 依据：源骨骼世界范围已经是 4.4×1.6×4.3 m，正是想要的 Boss 体型 ⇒ 不必缩放。
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outPath = EnemyDir + "/Z_Enemy_MoLong.prefab";
if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null) AssetDatabase.DeleteAsset(outPath);

var tmpl = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");

// 模板污染防御
int dirty = 0;
foreach (var t in tmpl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) dirty++;
if (dirty > 0) { sb.AppendLine("★★ 模板被污染(" + dirty + ")，中止"); Write(); Debug.LogError("[a37]"); return; }

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
// ★ Visual 缩放**保持模板值**（0.6032）—— 不改成 1！
//   a14/a25/a27 把它改成 1 是我自己的"归一化洁癖"，但那个缩放是模板设计的一部分。
//   这次保留原值，让龙在模板既有的尺度框架下自然落位。
sb.AppendLine("  Visual.localScale 保留模板值 = " + visual.localScale.ToString("F4"));

var holder = new GameObject("Model");
holder.transform.SetParent(visual, false);
holder.transform.localPosition = Vector3.zero;
holder.transform.localRotation = Quaternion.identity;
holder.transform.localScale = Vector3.one;

var srcContents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var names = new List<string>();
for (int i = 0; i < srcContents.transform.childCount; i++)
{
    var ch = srcContents.transform.GetChild(i);
    names.Add(ch.name);
    var clone = UnityEngine.Object.Instantiate(ch.gameObject);
    clone.name = ch.name;
    clone.transform.SetParent(holder.transform, false);
    clone.transform.localPosition = ch.localPosition;
    clone.transform.localRotation = ch.localRotation;
    clone.transform.localScale = ch.localScale;
}
sb.AppendLine("  克隆搬入 ×" + names.Count + " → " + string.Join(", ", names.ToArray()));

// ★ 骨骼世界范围（最朴素的判据）
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    foreach (var smr in host.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.bones == null) continue;
        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            var p = b.position;
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
    }
    sb.AppendLine("  ★骨骼世界范围: [" + mn.ToString("F2") + "] ~ [" + mx.ToString("F2") + "]"
                  + "  尺寸=" + (mx - mn).ToString("F2"));
    sb.AppendLine("     （源 prefab 无容器变换时也该是这个量级：约 4.4×1.6×4.3 m）");
    // 落地：把最低骨骼抬到 0
    float dy = -mn.y;
    holder.transform.localPosition = new Vector3(0f, dy, 0f);
    sb.AppendLine("  落地 dy=" + dy.ToString("F3"));
    // 朝向：源骨骼 Z 范围 -2.76..1.51（前段在 -Z 侧）⇒ 头朝 -Z，绕 Y 转 180° 让它朝 +Z
    holder.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
    sb.AppendLine("  朝向 rot.y=180（源头朝 -Z）");
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
if (cap != null) { cap.height = 2.2f; cap.radius = 1.2f; cap.center = new Vector3(0f, 1.1f, 0f); }
var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null) { var g = new GameObject("Hitbox"); g.transform.SetParent(host.transform, false); hb = g.AddComponent<InkWash.Combat.Hitbox>(); }
hb.owner = host; hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 0.9f, 0.5f); hb.pointB = new Vector3(0f, 1.1f, 3.2f); hb.radius = 1.2f;
hb.damage = 22f; hb.knockback = 7f; hb.hitStun = 0.45f; hb.hitStop = 0.07f;
var mz = host.transform.Find("Muzzle");
if (mz == null) { var g = new GameObject("Muzzle"); g.transform.SetParent(host.transform, false); mz = g.transform; }
mz.localPosition = new Vector3(0f, 1.6f, 2.4f);

host.name = "Z_Enemy_MoLong";
PrefabUtility.SaveAsPrefabAsset(host, outPath);
UnityEngine.Object.DestroyImmediate(host);
PrefabUtility.UnloadPrefabContents(srcContents);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// ---- 回读 + 渲染 ----
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
sb.AppendLine();
sb.AppendLine("  回读 " + outPath + " = " + (saved != null ? "存在 ✓" : "★ 不存在"));

var tmpScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

var gnd = GameObject.CreatePrimitive(PrimitiveType.Cube);
gnd.transform.position = new Vector3(0f, -0.05f, 0f);
gnd.transform.localScale = new Vector3(40f, 0.1f, 40f);

var refCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
refCube.transform.position = new Vector3(5f, 1.0f, -2f);
refCube.transform.localScale = new Vector3(0.6f, 2.0f, 0.6f);  // 2 m = 主角身高

var inst = (GameObject)PrefabUtility.InstantiatePrefab(saved);
inst.transform.position = Vector3.zero;
inst.transform.rotation = Quaternion.identity;

var lg = new GameObject("Sun");
var lt = lg.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.3f;
lg.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

var camGo = new GameObject("Cam");
var cam = camGo.AddComponent<Camera>();
cam.clearFlags = CameraClearFlags.SolidColor;
cam.backgroundColor = new Color(0.93f, 0.93f, 0.91f, 1f);

void Shoot(string name, Vector3 pos, Vector3 look, float fov)
{
    camGo.transform.position = pos;
    camGo.transform.LookAt(look);
    cam.fieldOfView = fov;
    var rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
    rt.antiAliasing = 4;
    cam.targetTexture = rt;
    cam.Render();
    RenderTexture.active = rt;
    var t = new Texture2D(960, 720, TextureFormat.RGB24, false);
    t.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
    t.Apply();
    RenderTexture.active = null;
    cam.targetTexture = null;
    File.WriteAllBytes(Path.Combine(shotDir, name + ".png"), t.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(t);
    rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
    sb.AppendLine("  渲染 " + name);
}

Shoot("R1_侧视", new Vector3(-9f, 3.5f, 0f), new Vector3(0f, 1.5f, 0f), 45f);
Shoot("R2_前视", new Vector3(0f, 3.5f, 10f), new Vector3(0f, 1.5f, 0f), 45f);
Shoot("R3_俯视", new Vector3(0f, 12f, 0.01f), Vector3.zero, 55f);
Shoot("R4_近景", new Vector3(-4f, 2.5f, 4f), new Vector3(0f, 1.3f, 1f), 42f);

EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity", OpenSceneMode.Single);

Write();
Debug.Log("[a37]\n" + sb.ToString());

void Write() => File.WriteAllText(Path.Combine(root, "Tools/reports/a37_dragon_inscene.txt"), sb.ToString());
