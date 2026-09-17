// a39_dragon_settle.cs —— 按 a38 查明的事实把龙摆正（修掉 a38 的 -Infinity 读法 bug）
//
// a38 查明（现在有真值，不再猜）：
//   · 骨骼世界范围 = 4.38 × 1.64 × 4.27 m，最长轴 = X（蛇形盘在 X 方向）
//   · 末端骨 drgon_00 相对根位移 = (-0.20, +0.61, +1.20) ⇒ **头朝 +Z**
//   · smr.bounds.size=(4.56,1.95,4.43) 与 mesh.bounds×0.001811 吻合
//     ⇒ **smr.bounds 会跟着祖先缩放变**（a29/a33 的读数本来就对；
//        a36/a37 判它"退化"是我自己把 lossyScale 折算搞混了）
//
// a38 的 bug：我把测量函数写在 `PrefabUtility.InstantiatePrefab` 出来的实例上，
//   实例上 `smr.bones` 会读成全 null（硬规矩 18）⇒ 得到 -Infinity。
//   本脚本改用 **`smr.bounds`**（不依赖 bones 数组）来量，这是最省事的正解。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(root, "Tools/screenshots/dragon39");
Directory.CreateDirectory(shotDir);

sb.AppendLine("========== a39 龙定尺寸/落地/朝向（终版）==========");

const string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outPath = EnemyDir + "/Z_Enemy_MoLong.prefab";
var tmpl = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");

int dirty = 0;
foreach (var t in tmpl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) dirty++;
if (dirty > 0) { sb.AppendLine("★★ 模板被污染(" + dirty + ")，中止"); Write(); Debug.LogError("[a39]"); return; }

// ▲ 关键：从零重建，不在坏资产上做增量
if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null) AssetDatabase.DeleteAsset(outPath);

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
visual.localScale = Vector3.one;

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

// ★ 量法：用 smr.bounds 的世界 AABB（Renderer.bounds 已经是世界空间）
bool WorldBounds(out Bounds b)
{
    b = new Bounds();
    bool first = true;
    foreach (var r in host.GetComponentsInChildren<Renderer>(true))
    {
        if (r is MeshRenderer && r.GetComponent<MeshFilter>() == null) continue;
        if (first) { b = r.bounds; first = false; }
        else b.Encapsulate(r.bounds);
    }
    return !first;
}

{
    WorldBounds(out var b0);
    sb.AppendLine("  基线(scale=1, rot=0) 世界尺寸=(" + b0.size.x.ToString("F3") + ", " + b0.size.y.ToString("F3") + ", " + b0.size.z.ToString("F3") + ")");
    sb.AppendLine("    Y[" + b0.min.y.ToString("F3") + ", " + b0.max.y.ToString("F3") + "]");

    // 长轴 X → 绕 Y 转 -90°，把身体主轴对到 +Z（这样"朝前"就是 +Z，与其它敌人一致）
    holder.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
    WorldBounds(out var b1);
    sb.AppendLine("  长轴对+Z 后 世界尺寸=(" + b1.size.x.ToString("F3") + ", " + b1.size.y.ToString("F3") + ", " + b1.size.z.ToString("F3") + ")");

    // 落地
    holder.transform.localPosition = new Vector3(0f, -b1.min.y, 0f);
    WorldBounds(out var b2);
    sb.AppendLine("  落地后 Y[" + b2.min.y.ToString("F3") + ", " + b2.max.y.ToString("F3") + "]");
    sb.AppendLine("  ★ 体长(Z)=" + b2.size.z.ToString("F2") + " m  高(Y)=" + b2.size.y.ToString("F2") + " m  宽(X)=" + b2.size.x.ToString("F2") + " m");

    // 参考：主角 2.17 m ⇒ 龙高 / 主角高
    sb.AppendLine("  （主角 2.17 m ⇒ 龙高 = " + (b2.size.y / 2.17f).ToString("F2") + " 个主角高）");

    // 记录最终值
    sb.AppendLine("  Model.localPos=" + holder.transform.localPosition.ToString("F4")
                  + "  localRot=" + holder.transform.localRotation.eulerAngles.ToString("F1")
                  + "  localScale=" + holder.transform.localScale.ToString("F4"));
}

// ---- 组件 ----
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
    sb.AppendLine("  材质 → M_Ink_Boss_Dragon ✓");
}
else sb.AppendLine("  ★ 材质 M_Ink_Boss_Dragon 读不到！");

var cap = host.GetComponent<CapsuleCollider>();
if (cap == null) cap = host.AddComponent<CapsuleCollider>();
cap.direction = 2;              // Z 轴方向的长胶囊（龙是长条）
cap.height = 4.6f; cap.radius = 1.1f; cap.center = new Vector3(0f, 1.0f, 0f);

var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null) { var g = new GameObject("Hitbox"); g.transform.SetParent(host.transform, false); hb = g.AddComponent<InkWash.Combat.Hitbox>(); }
hb.owner = host; hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 1.0f, -1.5f); hb.pointB = new Vector3(0f, 1.2f, 2.5f); hb.radius = 1.2f;
hb.damage = 22f; hb.knockback = 7f; hb.hitStun = 0.45f; hb.hitStop = 0.07f;

var mz = host.transform.Find("Muzzle");
if (mz == null) { var g = new GameObject("Muzzle"); g.transform.SetParent(host.transform, false); mz = g.transform; }
mz.localPosition = new Vector3(0f, 1.4f, 2.6f);

host.name = "Z_Enemy_MoLong";
PrefabUtility.SaveAsPrefabAsset(host, outPath);   // ★ 正确 API（不是 ApplyPrefabInstance）
UnityEngine.Object.DestroyImmediate(host);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
sb.AppendLine();
sb.AppendLine("  回读 " + outPath + " = " + (saved != null ? "存在 ✓" : "★ 不存在"));
int dg2 = 0;
foreach (var t in tmpl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) dg2++;
sb.AppendLine("  模板 Enemy_MoYan 污染检查: drgon_* = " + dg2 + (dg2 == 0 ? " ✓ 干净" : " ★被污染"));

// ---- 渲染 8 方向 + 俯视 ----
sb.AppendLine();
sb.AppendLine("---- 渲染 ----");
var tmpScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

var gnd = GameObject.CreatePrimitive(PrimitiveType.Cube);
gnd.transform.position = new Vector3(0f, -0.05f, 0f);
gnd.transform.localScale = new Vector3(60f, 0.1f, 60f);

var refCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
refCube.transform.position = new Vector3(3.2f, 1.085f, 3.2f);
refCube.transform.localScale = new Vector3(0.6f, 2.17f, 0.6f);   // 主角身高

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

for (int k = 0; k < 8; k++)
{
    float a = k * 45f * Mathf.Deg2Rad;
    var p = new Vector3(Mathf.Sin(a) * 13f, 5.5f, Mathf.Cos(a) * 13f);
    Shoot("A" + k + "_" + (k * 45) + "deg", p, new Vector3(0f, 1.2f, 0f), 42f);
}
Shoot("A8_俯视", new Vector3(0.01f, 16f, 0.01f), Vector3.zero, 48f);

EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity", OpenSceneMode.Single);

Write();
Debug.Log("[a39]\n" + sb.ToString());

void Write() => File.WriteAllText(Path.Combine(root, "Tools/reports/a39_dragon_settle.txt"), sb.ToString());
