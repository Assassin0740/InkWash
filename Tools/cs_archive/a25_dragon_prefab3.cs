// a25_dragon_prefab3.cs —— 造龙 Boss prefab（v3：从零重建 + 原点归一测量）
//
// ============ a22 / a23 两轮的错误清单（这是第三次栽在同一个坑上）============
//
//  【错 1｜累积污染】a23 不幂等。它往**已被 a22 写坏的 prefab** 上再叠加一层 ⇒
//      Visual 下出现两个 `Model`、根下出现两个 `Muzzle`。
//      根因：a22 在量不到尺寸时**依然存盘**，产出了一个半成品资产；
//      a23 又在这个半成品上做增量。**坏资产一旦入库，后续所有增量都建在流沙上。**
//      ⇒ 修法：**先删资产，从零重建**；并且每一版都必须能独立重复执行。
//
//  【错 2｜参考系错】a23 的"落地"写成：
//          holder.localPosition.y -= LowPercentile(host, 0.01f)
//      但 `LowPercentile` 是在 **host 已被摆在 y=7000** 时量的世界坐标 ⇒
//      量出来是 -7000.005，减掉它就等于把模型**再抬高 7000**。
//      ⇒ 正确做法：**把 host 摆到原点再量**，让"go 空间"与"世界空间"重合，
//        从根上消除参考系歧义（这也是 a14 学到的：只有一个口径，不要换算）。
//
//  【错 3｜对源素材尺度与形状的误判】a18 报的包围盒 X[-3.896,1.837] 是**A-pose 取样**，
//      而蒙皮后的真实形状是 X[-6.87,4.77] Y 才 1.3 高 —— 龙是一条**扁平的带子**
//      （27 000 顶点全铺在一个薄片上）。所以：
//        · "身高归一"对它没有意义（1.3 m 的"高"里大半是鳍）
//        · 该按**体长**归一，且体长要取 X/Z 里较大者
//      ⇒ 本版按体长归一，并且**同时报出 X/Y/Z 三个维度**让人能一眼看出是扁的。
//
// ============ v3 口径 ============
//   0. 删掉旧资产（保证从零开始，杜绝累积）
//   1. 宿主场景实例摆在 **原点**（measurement frame == world frame）
//   2. 源模型用 LoadPrefabContents + Instantiate 克隆（游离体才能换父级）
//   3. 一切量化都在"host 在原点"的前提下做 ⇒ 无需任何坐标换算
//   4. 断言闸门：量不到合理尺寸就**不存盘**
//   5. 最后把 host 归零后 ApplyPrefabInstance
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

sb.AppendLine("========== a25 造龙 Boss prefab v3（从零重建）==========");
sb.AppendLine();

// ---- 0) 删旧资产 ----
if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null)
{
    AssetDatabase.DeleteAsset(outPath);
    sb.AppendLine("  [0] 已删除旧资产 " + outPath + "（杜绝累积污染）");
}
else sb.AppendLine("  [0] 旧资产不存在（干净）");

var tmplPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");
if (tmplPrefab == null || srcPrefab == null)
{
    sb.AppendLine("★ 读不到模板或源"); File.WriteAllText(Path.Combine(root, "Tools/reports/a25_dragon_prefab3.txt"), sb.ToString()); Debug.LogError("[a25]"); return;
}

// ---- 1) 宿主摆在**原点** ----
var host = (GameObject)PrefabUtility.InstantiatePrefab(tmplPrefab);
host.transform.position = Vector3.zero;        // ★ 关键：测量参考系 = 世界参考系
host.transform.rotation = Quaternion.identity;
host.transform.localScale = Vector3.one;

var visual = host.transform.Find("Visual");
if (visual == null) { var v = new GameObject("Visual"); v.transform.SetParent(host.transform, false); visual = v.transform; }

// ---- 1b) 清空 Visual，并把非白名单的根子对象（旧 Muzzle/Hitbox 等）一并清掉 ----
var killedV = new List<string>();
for (int i = visual.childCount - 1; i >= 0; i--) { var c = visual.GetChild(i); killedV.Add(c.name); UnityEngine.Object.DestroyImmediate(c.gameObject); }
sb.AppendLine("  [1] 清空 Visual ×" + killedV.Count + " → " + string.Join(", ", killedV.ToArray()));

var killedR = new List<string>();
for (int i = host.transform.childCount - 1; i >= 0; i--)
{
    var c = host.transform.GetChild(i);
    if (c.name == "Visual") continue;
    killedR.Add(c.name);
    UnityEngine.Object.DestroyImmediate(c.gameObject);
}
sb.AppendLine("  [1b] 清空根下非 Visual 子对象 ×" + killedR.Count
              + (killedR.Count > 0 ? " → " + string.Join(", ", killedR.ToArray()) : ""));

// ---- 2) 源模型克隆搬入 ----
var srcContents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
if (srcContents == null) { sb.AppendLine("★ LoadPrefabContents 失败"); File.WriteAllText(Path.Combine(root, "Tools/reports/a25_dragon_prefab3.txt"), sb.ToString()); Debug.LogError("[a25]"); return; }

var holder = new GameObject("Model");
holder.transform.SetParent(visual, false);
holder.transform.localPosition = Vector3.zero;
holder.transform.localRotation = Quaternion.identity;
holder.transform.localScale = Vector3.one;

var names = new List<string>();
for (int i = 0; i < srcContents.transform.childCount; i++)
{
    var ch = srcContents.transform.GetChild(i);
    names.Add(ch.name);
    var clone = UnityEngine.Object.Instantiate(ch.gameObject);
    clone.name = ch.name;
    clone.transform.SetParent(holder.transform, false);   // ★ false：保留局部值，不带世界偏移
    clone.transform.localPosition = ch.localPosition;
    clone.transform.localRotation = ch.localRotation;
    clone.transform.localScale = ch.localScale;
}
sb.AppendLine("  [2] 克隆搬入 ×" + names.Count + " → " + string.Join(", ", names.ToArray()));

// ---- 3) 把 Visual 的缩放归一（模板里带 0.6032 的 KayKit 缩放，会污染测量与最终尺度）----
float visScaleBefore = visual.localScale.x;
visual.localScale = Vector3.one;
sb.AppendLine("  [3] Visual.localScale " + visScaleBefore.ToString("F4") + " → 1");

// ---- 4) 量尺寸（host 在原点 ⇒ go 空间 == 世界空间）----
Vector3 mn0, mx0;
{ var b = VertexBounds(host); mn0 = b.mn; mx0 = b.mx; }
float lx0 = mx0.x - mn0.x, ly0 = mx0.y - mn0.y, lz0 = mx0.z - mn0.z;
float long0 = Mathf.Max(lx0, lz0);
sb.AppendLine("  [4] 包围盒 X=" + lx0.ToString("F3") + " Y=" + ly0.ToString("F3") + " Z=" + lz0.ToString("F3")
              + "  体长=" + long0.ToString("F3"));

if (float.IsNaN(long0) || float.IsInfinity(long0) || long0 < 0.5f)
{
    sb.AppendLine("  ★★ 尺寸不可信 —— 按闸门**放弃存盘**");
    PrefabUtility.UnloadPrefabContents(srcContents);
    UnityEngine.Object.DestroyImmediate(host);
    File.WriteAllText(Path.Combine(root, "Tools/reports/a25_dragon_prefab3.txt"), sb.ToString());
    Debug.LogError("[a25]\n" + sb.ToString());
    return;
}

// ---- 5) 定向 + 缩放 ----
//   龙原生是"扁带"，且长轴 X。绕 Y -90° 让长轴对齐 +Z（= forward）。
//   但注意：长轴 X 是 4.33（a18 的 A-pose 值），蒙皮后是 11.64 —— 以**实测**为准。
holder.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
float targetLength = 7.0f;
float k = targetLength / long0;
holder.transform.localScale = Vector3.one * k;
sb.AppendLine("  [5] Model.rot=(0,-90,0)  k = " + targetLength + " / " + long0.ToString("F3") + " = " + k.ToString("F5"));

// ---- 6) 落地：在原点坐标系下直接量 1 分位 ----
float lowP = LowPercentile(host, 0.01f);
holder.transform.localPosition = new Vector3(0f, -lowP, 0f);
sb.AppendLine("  [6] 1分位(原点系)=" + lowP.ToString("F4") + " → Model.localPosition.y=" + (-lowP).ToString("F4"));

// ---- 7) 复核 ----
{
    var b = VertexBounds(host);
    float lx = b.mx.x - b.mn.x, ly = b.mx.y - b.mn.y, lz = b.mx.z - b.mn.z;
    sb.AppendLine("  [7] ★复核: X=" + lx.ToString("F3") + " Y=" + ly.ToString("F3") + " Z=" + lz.ToString("F3")
                  + "  体长=" + Mathf.Max(lx, lz).ToString("F3")
                  + "  Y底=" + b.mn.y.ToString("F4") + " Y顶=" + b.mx.y.ToString("F4"));
    if (Mathf.Abs(b.mn.y) > 0.05f) sb.AppendLine("       ★ 脚底未贴 0（差 " + b.mn.y.ToString("F4") + "）");
}

// ---- 8) 组件：删旧敌人、加 EnemyDragon ----
var oldEnemy = host.GetComponent<InkWash.Enemies.EnemyBase>();
if (oldEnemy != null) { UnityEngine.Object.DestroyImmediate(oldEnemy); sb.AppendLine("  [8] 移除 " + oldEnemy.GetType().Name); }
var dragon = host.AddComponent<InkWash.Enemies.EnemyDragon>();
sb.AppendLine("  [8] 加 EnemyDragon");

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
        if (mats.Length == 0) r.sharedMaterials = new[] { mat };
        else { for (int i = 0; i < mats.Length; i++) mats[i] = mat; r.sharedMaterials = mats; }
        rc++;
    }
}
sb.AppendLine("  [9] 换材质 ×" + rc);

// ---- 10) 数值 / 碰撞 / Hitbox / Muzzle ----
dragon.enemyName = "墨龙";
dragon.maxHealth = 620f; dragon.walkSpeed = 2.2f; dragon.chaseSpeed = 4.4f;
dragon.sightRange = 30f; dragon.loseSightRange = 40f;
dragon.attackRange = 7.0f; dragon.attackCooldown = 0.9f;
dragon.xpReward = 120; dragon.spawnDelay = 0.6f; dragon.destroyAfterDeath = 3.5f;
dragon.eyeOffset = new Vector3(0f, 2f, 1.5f);
sb.AppendLine("  [10] hp=620 spd=4.4 sight=30 atkRange=7 cd=0.9 xp=120");

var cap = host.GetComponent<CapsuleCollider>();
if (cap != null) { cap.height = 2.6f; cap.radius = 1.6f; cap.center = new Vector3(0f, 1.3f, 0f); }

var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null) { var g = new GameObject("Hitbox"); g.transform.SetParent(host.transform, false); hb = g.AddComponent<InkWash.Combat.Hitbox>(); }
hb.owner = host;
hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 1.2f, 0.5f);
hb.pointB = new Vector3(0f, 1.6f, 4.5f);
hb.radius = 1.6f; hb.damage = 22f; hb.knockback = 7f; hb.hitStun = 0.45f; hb.hitStop = 0.07f;

// Muzzle：本版**新建前先确认没有**（v2 就是这里累积的）
var muzzle = host.transform.Find("Muzzle");
if (muzzle == null) { var g = new GameObject("Muzzle"); g.transform.SetParent(host.transform, false); muzzle = g.transform; }
muzzle.localPosition = new Vector3(0f, 2.0f, 3.0f);
sb.AppendLine("  [10] Hitbox + Muzzle(0,2,3) 配好");

// ---- 11) 存盘 ----
//   把 host 摆到原点（已在原点）后 ApplyPrefabInstance ⇒ 存的是"局部值"，与位置无关
host.name = outName;
PrefabUtility.ApplyPrefabInstance(host, InteractionMode.AutomatedAction);
UnityEngine.Object.DestroyImmediate(host);
PrefabUtility.UnloadPrefabContents(srcContents);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
sb.AppendLine("  [11] 已写回 " + outPath);

File.WriteAllText(Path.Combine(root, "Tools/reports/a25_dragon_prefab3.txt"), sb.ToString());
Debug.Log("[a25]\n" + sb.ToString());

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
        var l2w = smr.transform.localToWorldMatrix;
        foreach (var v in baked.vertices)
        { var p = inv.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v)); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); any = true; }
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null || mf.sharedMesh == null) continue;
        var l2w = mf.transform.localToWorldMatrix;
        foreach (var v in mf.sharedMesh.vertices)
        { var p = inv.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v)); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); any = true; }
    }
    return any ? (mn, mx) : (Vector3.zero, Vector3.zero);
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
        var l2w = smr.transform.localToWorldMatrix;
        foreach (var v in baked.vertices) ys.Add(inv.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v)).y);
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null || mf.sharedMesh == null) continue;
        var l2w = mf.transform.localToWorldMatrix;
        foreach (var v in mf.sharedMesh.vertices) ys.Add(inv.MultiplyPoint3x4(l2w.MultiplyPoint3x4(v)).y);
    }
    if (ys.Count == 0) return 0f;
    ys.Sort();
    return ys[Mathf.Clamp((int)(ys.Count * q), 0, ys.Count - 1)];
}
