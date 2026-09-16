// a27_dragon_final.cs —— 造龙 Boss prefab（终版）
//
// ============================================================================
//  ★★★ 本脚本修的是本会话**最严重的一个 bug**，必须写在最前面 ★★★
// ============================================================================
//
//  a22 / a23 / a25 三个版本都在最后调用了：
//        PrefabUtility.ApplyPrefabInstance(host, InteractionMode.AutomatedAction);
//  而 `host` 是用 `PrefabUtility.InstantiatePrefab(tmplPrefab)` 从
//  **Enemy_MoYan.prefab**（KayKit 精英骷髅怪）实例化出来的。
//
//  `ApplyPrefabInstance` 的语义是：**把场景实例的改动写回「它来源的那个 prefab 资产」**。
//  它不是"另存为"。所以这三轮实际上是在**把整条龙骨（275 根）灌进 Enemy_MoYan**，
//  9773 行新增、把那个已验收的骷髅怪彻底改坏 —— 而且：
//    · 控制台**一条报错都没有**
//    · 每轮报告都打印"已写回 Assets/.../Z_Enemy_MoLong.prefab"（**那句话是假的**，
//      我压根没生成那个文件；我只是拼了字符串打日志）
//    · `Z_Enemy_MoLong.prefab` 从头到尾**不存在**（a26 直接报"读不到"才暴露）
//
//  教训（已写入 MEMORY 硬规矩 16）：
//    ① **"另存为新资产" 必须用 `SaveAsPrefabAsset`**；`ApplyPrefabInstance` 只用于
//       "就地更新已有 prefab"。两者名字不像，语义差得远，而且**用错不报错**。
//    ② **日志里的"已完成"必须由文件系统证实**，不能只打印我打算做的事。
//       本版结尾用 `AssetDatabase.LoadAssetAtPath` **回读新资产**来证明它真的存在。
//    ③ 一旦发现某个脚本可能写坏了资产，**先 `git status` 看波及面**再继续。
//       本轮就是靠 `git status` 看到 `M Enemy_MoYan.prefab` 才确认根因的。
//
// ----------------------------------------------------------------------------
//  另外两个必须修的测量问题（本会话第三次栽在同一族）：
//
//  【测量陷阱·终极形态】`BakeMesh` 的输出是**渲染器局部空间**的蒙皮结果，
//    而"祖先缩放"会同时作用在 ①骨骼（⇒ 蒙皮结果）和 ②`localToWorldMatrix` 上，
//    两者相乘**相互抵消** ⇒ 用 `BakeMesh + l2w` 量出来的尺寸**对祖先缩放免疫**。
//    这就是 a13/a14/a25 "改了 scale 读数一字不差"的真正机制。
//    ⇒ 正确口径：**`SkinnedMeshRenderer.bounds`（Unity 自己算的世界 AABB）**
//      或 **骨骼世界位置的范围**。两者都不经过我手工的矩阵乘法。
//    本版两个都量，并互相对照。
//
//  【参考系】宿主**摆在原点**后再量 ⇒ go 空间 == 世界空间，从根上消除换算。
// ============================================================================
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

sb.AppendLine("========== a27 造龙 Boss prefab（终版）==========");
sb.AppendLine();

// ---- 0) 清掉可能存在的旧产物 ----
if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null)
{
    AssetDatabase.DeleteAsset(outPath);
    sb.AppendLine("  [0] 删除旧产物 " + outPath);
}

// ---- 0b) 防御性检查：模板必须干净（不含 drgon_*）----
var tmplPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");
if (tmplPrefab == null || srcPrefab == null)
{
    sb.AppendLine("★ 读不到模板或源"); Write(); Debug.LogError("[a27]\n" + sb.ToString()); return;
}
{
    int dirty = 0;
    foreach (var t in tmplPrefab.GetComponentsInChildren<Transform>(true))
        if (t.name.StartsWith("drgon_")) dirty++;
    sb.AppendLine("  [0b] 模板 Enemy_MoYan 里 drgon_* 节点数 = " + dirty + (dirty > 0 ? "  ★★ 模板被污染！请先 git checkout" : "  ✓ 干净"));
    if (dirty > 0) { Write(); Debug.LogError("[a27]\n" + sb.ToString()); return; }
}

// ---- 1) 宿主：场景实例（用于最后 SaveAsPrefabAsset；不 ApplyPrefabInstance）----
var host = (GameObject)PrefabUtility.InstantiatePrefab(tmplPrefab);
host.transform.position = Vector3.zero;
host.transform.rotation = Quaternion.identity;
host.transform.localScale = Vector3.one;

var visual = host.transform.Find("Visual");
if (visual == null) { var v = new GameObject("Visual"); v.transform.SetParent(host.transform, false); visual = v.transform; }

int kv = 0;
for (int i = visual.childCount - 1; i >= 0; i--) { UnityEngine.Object.DestroyImmediate(visual.GetChild(i).gameObject); kv++; }
int kr = 0;
for (int i = host.transform.childCount - 1; i >= 0; i--)
{
    var c = host.transform.GetChild(i);
    if (c.name == "Visual") continue;
    UnityEngine.Object.DestroyImmediate(c.gameObject); kr++;
}
sb.AppendLine("  [1] 清空 Visual ×" + kv + "，清空根下非 Visual ×" + kr);

// ---- 2) 源模型克隆 ----
var srcContents = PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
if (srcContents == null) { sb.AppendLine("★ LoadPrefabContents 失败"); Write(); Debug.LogError("[a27]\n" + sb.ToString()); return; }

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
    clone.transform.SetParent(holder.transform, false);
    clone.transform.localPosition = ch.localPosition;
    clone.transform.localRotation = ch.localRotation;
    clone.transform.localScale = ch.localScale;
}
sb.AppendLine("  [2] 克隆搬入 ×" + names.Count + " → " + string.Join(", ", names.ToArray()));

// ---- 3) 归一 Visual 缩放 ----
visual.localScale = Vector3.one;

// ---- 4) 量尺寸：**用 smr.bounds（世界 AABB）+ 骨骼范围**，不用 BakeMesh+l2w ----
{
    var a = WorldBounds(host);
    sb.AppendLine("  [4] smr.bounds 世界 AABB: X[" + a.mn.x.ToString("F3") + "," + a.mx.x.ToString("F3") + "]"
                  + " Y[" + a.mn.y.ToString("F3") + "," + a.mx.y.ToString("F3") + "]"
                  + " Z[" + a.mn.z.ToString("F3") + "," + a.mx.z.ToString("F3") + "]");
    float lx = a.mx.x - a.mn.x, ly = a.mx.y - a.mn.y, lz = a.mx.z - a.mn.z;
    sb.AppendLine("      尺寸 X=" + lx.ToString("F3") + " Y=" + ly.ToString("F3") + " Z=" + lz.ToString("F3"));

    float longAxis = Mathf.Max(lx, lz);
    if (longAxis < 0.5f)
    {
        sb.AppendLine("  ★★ 尺寸不可信 → 放弃，不产出资产");
        PrefabUtility.UnloadPrefabContents(srcContents);
        UnityEngine.Object.DestroyImmediate(host);
        Write(); Debug.LogError("[a27]\n" + sb.ToString()); return;
    }

    // ---- 5) 定向 + 缩放：把长轴对齐 +Z 并归一化到目标体长 ----
    //   实测长轴是 X（原生横向）⇒ 绕 Y -90°
    bool longIsX = lx >= lz;
    holder.transform.localRotation = longIsX ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;
    float targetLength = 7.0f;
    float k = targetLength / longAxis;
    holder.transform.localScale = Vector3.one * k;
    sb.AppendLine("  [5] 长轴=" + (longIsX ? "X" : "Z") + " ⇒ Model.rot=" + holder.transform.localRotation.eulerAngles.ToString("F0")
                  + "  k = " + targetLength + " / " + longAxis.ToString("F3") + " = " + k.ToString("F5"));

    // ---- 6) 缩放后**重量**（证明生效：这次一定变）----
    var b1 = WorldBounds(host);
    float lx1 = b1.mx.x - b1.mn.x, lz1 = b1.mx.z - b1.mn.z;
    sb.AppendLine("  [6] ★缩放后重量(smr.bounds): 体长=" + Mathf.Max(lx1, lz1).ToString("F3")
                  + "（应 ≈ " + targetLength + "）"
                  + (Mathf.Abs(Mathf.Max(lx1, lz1) - targetLength) < 0.5f ? "  ✓ 缩放确实生效" : "  ★ 仍未生效"));

    // ---- 7) 落地：把 Y 底抬到 0 ----
    float dy = -b1.mn.y;
    holder.transform.localPosition = new Vector3(0f, holder.transform.localPosition.y + dy, 0f);
    var b2 = WorldBounds(host);
    sb.AppendLine("  [7] 落地 Y底 " + b1.mn.y.ToString("F4") + " → " + b2.mn.y.ToString("F4")
                  + "  Y顶=" + b2.mx.y.ToString("F3"));
    sb.AppendLine("      ★复核: X[" + b2.mn.x.ToString("F2") + "," + b2.mx.x.ToString("F2") + "]"
                  + " Y[" + b2.mn.y.ToString("F2") + "," + b2.mx.y.ToString("F2") + "]"
                  + " Z[" + b2.mn.z.ToString("F2") + "," + b2.mx.z.ToString("F2") + "]");
}

// ---- 8) 组件 ----
var oldEnemy = host.GetComponent<InkWash.Enemies.EnemyBase>();
if (oldEnemy != null) UnityEngine.Object.DestroyImmediate(oldEnemy);
var dragon = host.AddComponent<InkWash.Enemies.EnemyDragon>();
dragon.enemyName = "墨龙";
dragon.maxHealth = 620f; dragon.walkSpeed = 2.2f; dragon.chaseSpeed = 4.4f;
dragon.sightRange = 30f; dragon.loseSightRange = 40f;
dragon.attackRange = 7.0f; dragon.attackCooldown = 0.9f;
dragon.xpReward = 120; dragon.spawnDelay = 0.6f; dragon.destroyAfterDeath = 3.5f;
dragon.eyeOffset = new Vector3(0f, 2f, 1.5f);
sb.AppendLine("  [8] EnemyDragon: hp=620 spd=4.4 sight=30 atkRange=7 xp=120");

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
sb.AppendLine("  [9] 材质 ×" + rc + " → " + (mat != null ? mat.name : "null"));

// ---- 10) 碰撞 / Hitbox / Muzzle ----
var cap = host.GetComponent<CapsuleCollider>();
if (cap != null) { cap.height = 2.6f; cap.radius = 1.6f; cap.center = new Vector3(0f, 1.3f, 0f); }

var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null) { var g = new GameObject("Hitbox"); g.transform.SetParent(host.transform, false); hb = g.AddComponent<InkWash.Combat.Hitbox>(); }
hb.owner = host;
hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 1.2f, 0.5f);
hb.pointB = new Vector3(0f, 1.6f, 4.5f);
hb.radius = 1.6f; hb.damage = 22f; hb.knockback = 7f;
hb.hitStun = 0.45f; hb.hitStop = 0.07f;

var muzzle = host.transform.Find("Muzzle");
if (muzzle == null) { var g = new GameObject("Muzzle"); g.transform.SetParent(host.transform, false); muzzle = g.transform; }
muzzle.localPosition = new Vector3(0f, 2.0f, 3.0f);
sb.AppendLine("  [10] Collider + Hitbox + Muzzle 配好");

// ---- 11) ★★ 用 SaveAsPrefabAsset 另存（不是 ApplyPrefabInstance！）----
host.name = outName;
PrefabUtility.SaveAsPrefabAsset(host, outPath);
UnityEngine.Object.DestroyImmediate(host);
PrefabUtility.UnloadPrefabContents(srcContents);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// ---- 12) ★★ 回读证实（不接受"我打了日志"当证据）----
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
sb.AppendLine();
sb.AppendLine("  [12] ★回读证实 " + outPath);
if (saved == null)
{
    sb.AppendLine("       ★★ 资产不存在 —— 存盘失败");
    Debug.LogError("[a27] 存盘失败\n" + sb.ToString());
    Write(); return;
}
int drgBones = 0, eneComp = 0, rends = 0, capsules = 0;
foreach (var t in saved.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) drgBones++;
foreach (var c in saved.GetComponents<MonoBehaviour>()) if (c is InkWash.Enemies.EnemyDragon) eneComp++;
rends = saved.GetComponentsInChildren<Renderer>(true).Length;
capsules = saved.GetComponentsInChildren<Collider>(true).Length;
sb.AppendLine("       存在 ✓  龙骨节点=" + drgBones + "  EnemyDragon 组件=" + eneComp
              + "  渲染器=" + rends + "  碰撞体=" + capsules
              + "  子对象=" + (saved.transform.childCount));
sb.AppendLine("       Visual/Model = " + (saved.transform.Find("Visual/Model") != null ? "✓" : "★ 缺失"));

// 同时证伪"模板没被污染"
{
    var tm2 = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
    int d2 = 0;
    foreach (var t in tm2.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) d2++;
    sb.AppendLine("  [12b] 模板 Enemy_MoYan 污染检查: drgon_* = " + d2 + (d2 == 0 ? "  ✓ 未被污染" : "  ★★ 又被污染了"));
}

Write();
Debug.Log("[a27]\n" + sb.ToString());

void Write() => File.WriteAllText(Path.Combine(root, "Tools/reports/a27_dragon_final.txt"), sb.ToString());

// ★ 用 smr.bounds（Unity 自己算的世界 AABB），绕开 BakeMesh+l2w 的自我抵消
(Vector3 mn, Vector3 mx) WorldBounds(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    bool any = false;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        var bb = smr.bounds;
        mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true;
    }
    foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
    {
        if (mr.GetComponent<SkinnedMeshRenderer>() != null) continue;
        var bb = mr.bounds;
        mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true;
    }
    return any ? (mn, mx) : (Vector3.zero, Vector3.zero);
}
