// a23_dragon_prefab2.cs —— 造龙 Boss prefab（v2，修掉 a22 的"不能改 prefab 实例父级"）
//
// ============ a22 的错，记下来 ============
//   `Setting the parent of a transform which resides in a Prefab instance is not possible`
//   —— 我用 PrefabUtility.InstantiatePrefab 起了 Z_Dragon 的**预制体实例**，
//      然后想把它的子对象 SetParent 到另一个 prefab 实例下面。
//      Unity 不允许**跨预制体实例搬子树**：实例的层级结构由 prefab 资产决定，
//      改它等于在做一次"覆盖"，而目标父级属于**另一个** prefab ⇒ 直接拒绝。
//      （这也解释了 a7 当年为什么能成功：那次我用的是 `LoadPrefabContents` + `Instantiate`，
//        拿到的是**游离的克隆体**，不是实例 —— 但 `LoadPrefabContents` 改 root 的引用又不可靠
//        （硬规矩 12）。所以要同时满足"能搬"和"能改 root"两个条件。）
//
// ============ v2 的正确姿势 ============
//   ① 源模型：用 **LoadPrefabContents + Instantiate 克隆**（游离体，可自由搬）
//      —— 只把它当"素材来源"，不需要它的 prefab 关联
//   ② 宿主：用**场景实例**（InstantiatePrefab），这样最后能 ApplyPrefabInstance 回写
//      —— 满足硬规矩 12
//   ③ 克隆完立刻 UnloadPrefabContents 源容器
//
//   另一个必须修的点：a22 在 bbox=-Infinity 时**依然 SaveAsPrefabAsset**，
//   存出了一个空 Visual 的坏 prefab。本版加了**断言闸门**：量不到尺寸就**放弃存盘**并报错。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outName = "Z_Enemy_MoLong";
string outPath = EnemyDir + "/" + outName + ".prefab";

var tmplPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");

sb.AppendLine("========== a23 造龙 Boss prefab v2 ==========");
if (tmplPrefab == null || srcPrefab == null)
{
    sb.AppendLine("★ 读不到模板或源: tmpl=" + (tmplPrefab != null) + " src=" + (srcPrefab != null));
    File.WriteAllText(Path.Combine(root, "Tools/reports/a23_dragon_prefab2.txt"), sb.ToString());
    Debug.LogError("[a23]\n" + sb.ToString());
    return;
}
sb.AppendLine("模板 = Enemy_MoYan  源 = Z_Dragon  材质 = " + (mat != null ? mat.name : "null"));
sb.AppendLine();

// ---- 1) 宿主：场景实例（可 ApplyPrefabInstance 回写）----
var host = (GameObject)PrefabUtility.InstantiatePrefab(tmplPrefab);
host.transform.position = new Vector3(0f, 7000f, 0f);
host.transform.rotation = Quaternion.identity;

var visual = host.transform.Find("Visual");
if (visual == null)
{
    var v = new GameObject("Visual");
    v.transform.SetParent(host.transform, false);
    visual = v.transform;
}
var killed = new List<string>();
for (int i = visual.childCount - 1; i >= 0; i--)
{
    var c = visual.GetChild(i);
    killed.Add(c.name);
    UnityEngine.Object.DestroyImmediate(c.gameObject);
}
sb.AppendLine("  [1] 清空 Visual ×" + killed.Count + " → " + string.Join(", ", killed.ToArray()));

// ---- 2) 源模型：LoadPrefabContents + 克隆（游离体，可自由搬）----
var srcContents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
if (srcContents == null) { sb.AppendLine("★ LoadPrefabContents 失败"); Debug.LogError("[a23]"); return; }

var holder = new GameObject("Model");
holder.transform.SetParent(visual, false);
holder.transform.localPosition = Vector3.zero;
holder.transform.localRotation = Quaternion.identity;
holder.transform.localScale = Vector3.one;

int moved = 0;
var names = new List<string>();
for (int i = srcContents.transform.childCount - 1; i >= 0; i--)
{
    var ch = srcContents.transform.GetChild(i);
    names.Insert(0, ch.name);
    var clone = UnityEngine.Object.Instantiate(ch.gameObject);   // ★ 游离克隆体
    clone.name = ch.name;
    clone.transform.SetParent(holder.transform, false);
    clone.transform.localPosition = ch.localPosition;
    clone.transform.localRotation = ch.localRotation;
    clone.transform.localScale = ch.localScale;
    moved++;
}
sb.AppendLine("  [2] 克隆搬入 ×" + moved + " → " + string.Join(", ", names.ToArray()));

// 检查残留
var leftover = new List<string>();
for (int i = visual.childCount - 1; i >= 0; i--)
{
    var c = visual.GetChild(i);
    if (c.name != "Model") leftover.Add(c.name);
}
sb.AppendLine("  [2b] Visual 下非 Model 残留 = " + leftover.Count
              + (leftover.Count > 0 ? " → " + string.Join(", ", leftover.ToArray()) : ""));

// ---- 3) 量尺寸（★ 闸门：量不到就不许存盘）----
Vector3 mn0, mx0;
{
    var b = VertexBounds(host);
    mn0 = b.mn; mx0 = b.mx;
}
float lx0 = mx0.x - mn0.x, ly0 = mx0.y - mn0.y, lz0 = mx0.z - mn0.z;
float long0 = Mathf.Max(lx0, lz0);
sb.AppendLine("  [3] 搬入后包围盒 X=" + lx0.ToString("F3") + " Y=" + ly0.ToString("F3") + " Z=" + lz0.ToString("F3")
              + "  长轴=" + long0.ToString("F3"));
if (!IsFinite(long0) || long0 < 0.5f)
{
    sb.AppendLine("  ★★ 尺寸不可信（" + long0 + "）—— 按闸门规则**放弃存盘**，不产出坏 prefab");
    PrefabUtility.UnloadPrefabContents(srcContents);
    UnityEngine.Object.DestroyImmediate(host);
    File.WriteAllText(Path.Combine(root, "Tools/reports/a23_dragon_prefab2.txt"), sb.ToString());
    Debug.LogError("[a23]\n" + sb.ToString());
    return;
}

// ---- 4) 定向：原生长轴是 X ⇒ 绕 Y -90° 让头朝 +Z ----
holder.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
sb.AppendLine("  [4] Model.localRotation = (0,-90,0)");

// ---- 5) 缩放（按体长归一化到 7 m）----
float targetLength = 7.0f;
float k = targetLength / long0;
holder.transform.localScale = Vector3.one * k;
sb.AppendLine("  [5] k = " + targetLength + " / " + long0.ToString("F3") + " = " + k.ToString("F5"));

// ---- 6) 落地（1 分位=脚底 → 0）----
float lowP = LowPercentile(host, 0.01f);
holder.transform.localPosition = new Vector3(holder.transform.localPosition.x,
    holder.transform.localPosition.y - lowP, holder.transform.localPosition.z);
sb.AppendLine("  [6] 落地: 1分位=" + lowP.ToString("F3") + " → holder.localPosition=" + holder.transform.localPosition.ToString("F4"));

// ---- 7) 复核（go 空间）----
{
    var b = VertexBounds(host);
    float lx = b.mx.x - b.mn.x, ly = b.mx.y - b.mn.y, lz = b.mx.z - b.mn.z;
    sb.AppendLine("  [7] ★复核(go 空间): X=" + lx.ToString("F3") + " Y=" + ly.ToString("F3") + " Z=" + lz.ToString("F3")
                  + "  体长=" + Mathf.Max(lx, lz).ToString("F3") + "  脚底偏离0=" + b.mn.y.ToString("F4"));
}

// ---- 8) 换敌人组件：删旧、加 EnemyDragon ----
var oldEnemy = host.GetComponent<InkWash.Enemies.EnemyBase>();
if (oldEnemy != null) { sb.AppendLine("  [8] 移除 " + oldEnemy.GetType().Name); UnityEngine.Object.DestroyImmediate(oldEnemy); }
var dragon = host.AddComponent<InkWash.Enemies.EnemyDragon>();
sb.AppendLine("  [8] 已加 EnemyDragon");

// ---- 9) 材质 ----
var sw = host.GetComponent<InkWash.Rendering.InkMaterialSwap>();
if (sw == null) sw = host.AddComponent<InkWash.Rendering.InkMaterialSwap>();
int rc = 0;
if (mat != null)
{
    sw.inkMaterials = new[] { mat };
    foreach (var r in host.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        if (mats.Length == 0) { r.sharedMaterials = new[] { mat }; rc++; continue; }
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        r.sharedMaterials = mats; rc++;
    }
}
sb.AppendLine("  [9] 材质换上 ×" + rc + "  inkMaterials=[" + (mat != null ? mat.name : "null") + "]");

// ---- 10) 数值 / 碰撞 / Hitbox / Muzzle ----
dragon.enemyName = "墨龙";
dragon.maxHealth = 620f;
dragon.walkSpeed = 2.2f;
dragon.chaseSpeed = 4.4f;
dragon.sightRange = 30f;
dragon.loseSightRange = 40f;
dragon.attackRange = 7.0f;
dragon.attackCooldown = 0.9f;
dragon.xpReward = 120;
dragon.spawnDelay = 0.6f;
dragon.destroyAfterDeath = 3.5f;
dragon.eyeOffset = new Vector3(0f, 2f, 1.5f);
sb.AppendLine("  [10] hp=620 spd=4.4 sight=30 atkRange=7 cd=0.9 xp=120");

var cap = host.GetComponent<CapsuleCollider>();
if (cap != null) { cap.height = 2.6f; cap.radius = 1.6f; cap.center = new Vector3(0f, 1.3f, 0f); sb.AppendLine("  [10] Capsule h=2.6 r=1.6"); }

var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null)
{
    var g = new GameObject("Hitbox");
    g.transform.SetParent(host.transform, false);
    hb = g.AddComponent<InkWash.Combat.Hitbox>();
}
hb.owner = host;
hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 1.2f, 0.5f);
hb.pointB = new Vector3(0f, 1.6f, 4.5f);
hb.radius = 1.6f;
hb.damage = 22f;
hb.knockback = 7f;
hb.hitStun = 0.45f;
hb.hitStop = 0.07f;
sb.AppendLine("  [10] Hitbox (0,1.2,0.5)→(0,1.6,4.5) r=1.6 dmg=22");

var muzzle = host.transform.Find("Muzzle");
if (muzzle == null)
{
    var g = new GameObject("Muzzle");
    g.transform.SetParent(host.transform, false);
    muzzle = g.transform;
}
muzzle.localPosition = new Vector3(0f, 2.0f, 3.0f);
sb.AppendLine("  [10] Muzzle (0,2.0,3.0)");

// ---- 11) 存盘（场景实例 + ApplyPrefabInstance）----
host.name = outName;
PrefabUtility.ApplyPrefabInstance(host, InteractionMode.AutomatedAction);
UnityEngine.Object.DestroyImmediate(host);
PrefabUtility.UnloadPrefabContents(srcContents);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
sb.AppendLine("  [11] 已写回 " + outPath);

File.WriteAllText(Path.Combine(root, "Tools/reports/a23_dragon_prefab2.txt"), sb.ToString());
Debug.Log("[a23]\n" + sb.ToString());

bool IsFinite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

(Vector3 mn, Vector3 mx) VertexBounds(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    var inv = go.transform.worldToLocalMatrix;
    bool any = false;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        foreach (var v in baked.vertices)
        {
            var p = inv.MultiplyPoint3x4(smr.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); any = true;
        }
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null || mf.sharedMesh == null) continue;
        foreach (var v in mf.sharedMesh.vertices)
        {
            var p = inv.MultiplyPoint3x4(mf.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); any = true;
        }
    }
    if (!any) return (new Vector3(0, 0, 0), new Vector3(0, 0, 0));
    return (mn, mx);
}

float LowPercentile(GameObject go, float q)
{
    var ys = new List<float>();
    var inv = go.transform.worldToLocalMatrix;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        foreach (var v in baked.vertices)
            ys.Add(inv.MultiplyPoint3x4(smr.transform.localToWorldMatrix.MultiplyPoint3x4(v)).y);
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null || mf.sharedMesh == null) continue;
        foreach (var v in mf.sharedMesh.vertices)
            ys.Add(inv.MultiplyPoint3x4(mf.transform.localToWorldMatrix.MultiplyPoint3x4(v)).y);
    }
    if (ys.Count == 0) return 0f;
    ys.Sort();
    return ys[Mathf.Clamp((int)(ys.Count * q), 0, ys.Count - 1)];
}
