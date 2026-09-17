// a22_dragon_prefab.cs —— 把 Z_Dragon 造成可战斗的 Boss prefab
//
// 设计（严格对齐现有敌人的组织方式，以便 S3 验收口径能直接复用）：
//   根对象  Z_Enemy_MoLong  <NavMeshAgent CapsuleCollider Hitbox EnemyDragon InkMaterialSwap>
//     Visual              （模板里已有的容器；保留，动画/缩放都挂它下面）
//       Model             （原 Z_Dragon 的全部内容搬进来）
//     Muzzle              （龙息出膛点；EnemyDragon 会自己算嘴的位置，这里只是兼容）
//
// ★ 与三个小怪的差异：龙**不用 Animator**（无 controller），
//   所有动作由 EnemyDragon 直接驱动脊骨 Transform。
//
// ★ 尺寸：龙的原始包围盒 X[-3.896,1.837] Y[-0.042,2.237] Z[-3.610,2.002]
//   —— 长轴是 X（5.73 m），说明它原生是"侧躺/横着"的朝向。
//   作为 Boss 我们希望它**朝 +Z**（与 transform.forward 一致），所以要绕 Y 转 -90°，
//   然后把长轴长度收到约 7 m（Boss 体型）。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";

string outName = "Z_Enemy_MoLong";
var tmplPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");  // Elite 模板
var srcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");

sb.AppendLine("========== a22 造龙 Boss prefab ==========");
if (tmplPrefab == null) { sb.AppendLine("★ 读不到模板 Enemy_MoYan"); }
if (srcPrefab == null) { sb.AppendLine("★ 读不到 Z_Dragon"); }
if (mat == null) { sb.AppendLine("★ 读不到 M_Ink_Boss_Dragon"); }
if (tmplPrefab == null || srcPrefab == null)
{
    File.WriteAllText(Path.Combine(root, "Tools/reports/a22_dragon_prefab.txt"), sb.ToString());
    Debug.LogError("[a22]\n" + sb.ToString());
    return;
}
sb.AppendLine("模板 = Enemy_MoYan（Elite）  源 = Z_Dragon  材质 = " + (mat != null ? mat.name : "null"));
sb.AppendLine();

// ---- 用**场景实例**改（硬规矩 12：LoadPrefabContents 改 root 的引用不可靠）----
var tmpl = (GameObject)PrefabUtility.InstantiatePrefab(tmplPrefab);
tmpl.transform.position = new Vector3(0f, 7000f, 0f);
tmpl.transform.rotation = Quaternion.identity;

var src = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab);
src.transform.position = new Vector3(0f, 7100f, 0f);
src.transform.rotation = Quaternion.identity;

// ---- 1) Visual 容器：清掉旧的模型子树 ----
var visual = tmpl.transform.Find("Visual");
if (visual == null)
{
    var v = new GameObject("Visual");
    v.transform.SetParent(tmpl.transform, false);
    visual = v.transform;
    sb.AppendLine("  [1] 模板没有 Visual，已新建");
}
var killed = new List<string>();
for (int i = visual.childCount - 1; i >= 0; i--)
{
    var c = visual.GetChild(i);
    killed.Add(c.name);
    UnityEngine.Object.DestroyImmediate(c.gameObject);
}
sb.AppendLine("  [1] 清空 Visual 旧内容 ×" + killed.Count
              + (killed.Count > 0 ? " → " + string.Join(", ", killed.ToArray()) : ""));

// ---- 2) 把 Z_Dragon 整棵树搬进 Visual/Model ----
var holder = new GameObject("Model");
holder.transform.SetParent(visual, false);
holder.transform.localPosition = Vector3.zero;
holder.transform.localRotation = Quaternion.identity;
holder.transform.localScale = Vector3.one;

var srcKids = new List<Transform>();
for (int i = 0; i < src.transform.childCount; i++) srcKids.Add(src.transform.GetChild(i));
foreach (var ch in srcKids)
{
    ch.SetParent(holder.transform, true);   // ★ worldPositionStays=true：Z_Dragon 的根在 7100，搬完要复位
}
sb.AppendLine("  [2] 搬入 Z_Dragon 子对象 ×" + srcKids.Count
              + " → " + string.Join(", ", srcKids.ConvertAll(t => t.name).ToArray()));

// 因为用了 worldPositionStays=true，搬进来的局部位置带了 7100 的偏移 ⇒ 复位
foreach (var ch in srcKids)
{
    ch.localPosition -= Vector3.up * 100f;   // 抵消 7100-7000
}
// 更稳的做法：直接把 holder 的局部位置设成源根与模板根的差
holder.transform.localPosition = new Vector3(
    src.transform.position.x - tmpl.transform.position.x,
    0f,
    src.transform.position.z - tmpl.transform.position.z);

sb.AppendLine("  [2b] holder.localPosition = " + holder.transform.localPosition.ToString("F3"));

// ---- 3) 加 EnemyDragon 组件（先删掉模板可能自带的旧敌人组件）----
// 模板 Enemy_MoYan 上是 EnemyElite ⇒ 必须换成 EnemyDragon，否则行为完全是旧的
var oldEnemy = tmpl.GetComponent<InkWash.Enemies.EnemyBase>();
if (oldEnemy != null)
{
    sb.AppendLine("  [3] 移除模板旧组件: " + oldEnemy.GetType().Name);
    UnityEngine.Object.DestroyImmediate(oldEnemy);
}
var dragon = tmpl.AddComponent<InkWash.Enemies.EnemyDragon>();
sb.AppendLine("  [3] 已加 EnemyDragon");

// ---- 4) 配 InkMaterialSwap 与渲染器材质 ----
var sw = tmpl.GetComponentInChildren<InkWash.Rendering.InkMaterialSwap>(true);
if (sw == null) sw = tmpl.AddComponent<InkWash.Rendering.InkMaterialSwap>();
if (mat != null)
{
    sw.inkMaterials = new[] { mat };
    int rc = 0;
    foreach (var r in tmpl.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
        r.sharedMaterials = mats;
        rc++;
    }
    sb.AppendLine("  [4] InkMaterialSwap.inkMaterials=[" + mat.name + "]  渲染器换材质 ×" + rc);
}

// ---- 5) 定向：长轴是 X ⇒ 绕 Y 转 -90°，让头朝 +Z ----
//   先量一次朝向：龙头在哪一侧？a18 显示前段骨骼 X 为正且 Z 接近 0，
//   而 bbox X[-3.896, 1.837] —— 头（细长端）应在 X 正侧。
//   直接把 Model 绕 Y 转 -90°，使 +X 映射到 +Z。
var modelT = holder.transform;
modelT.localRotation = Quaternion.Euler(0f, -90f, 0f);
sb.AppendLine("  [5] Model.localRotation = (0, -90, 0)  → 让原生 +X 轴向对齐 transform.forward(+Z)");

// ---- 6) 缩放与落地 ----
//   量顶点真值（BakeMesh），把"分位高"归一 + 脚底落 0。
//   注意：龙是**趴/盘**的姿态，身高意义不大，这里改按**体长**归一（Boss 目标体长 7 m）。
float targetLength = 7.0f;
float bodyH = 0f, bodyLong = 0f;
{
    var (mn, mx) = VertexBounds(tmpl);
    float lx = mx.x - mn.x, ly = mx.y - mn.y, lz = mx.z - mn.z;
    bodyH = ly;
    bodyLong = Mathf.Max(lx, lz);
    sb.AppendLine("  [6] 旋转前包围盒: X=" + lx.ToString("F3") + " Y=" + ly.ToString("F3") + " Z=" + lz.ToString("F3")
                  + "  体长取=" + bodyLong.ToString("F3"));
}
if (bodyLong < 0.05f) { sb.AppendLine("  ★ 体长异常，跳过缩放"); }
else
{
    float k = targetLength / bodyLong;
    holder.transform.localScale = Vector3.one * k;
    sb.AppendLine("  [6] k = " + targetLength.ToString("F1") + " / " + bodyLong.ToString("F3") + " = " + k.ToString("F4"));

    // 落地：量最低分位，抬到 0
    var (mn2, mx2) = VertexBounds(tmpl);
    float lowP = LowPercentile(tmpl, 0.01f);
    float dy = -lowP;
    holder.transform.localPosition = new Vector3(holder.transform.localPosition.x,
        holder.transform.localPosition.y + dy, holder.transform.localPosition.z);
    var (mn3, mx3) = VertexBounds(tmpl);
    sb.AppendLine("  [6b] 落地后 go 空间 Y=[" + mn3.y.ToString("F3") + "," + mx3.y.ToString("F3") + "]"
                  + "  体长=" + Mathf.Max(mx3.x - mn3.x, mx3.z - mn3.z).ToString("F3"));
}

// ---- 7) 碰撞体 ----
var cap = tmpl.GetComponent<CapsuleCollider>();
if (cap != null)
{
    cap.height = 2.6f;
    cap.radius = 2.0f;
    cap.center = new Vector3(0f, 1.3f, 0f);
    sb.AppendLine("  [7] CapsuleCollider h=2.6 r=2.0 centerY=1.3（龙体型大，判定体也要大）");
}

// ---- 8) 行为参数（Boss 数值）----
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
dragon.eyeOffset = new Vector3(0f, 2.0f, 1.5f);
dragon.bodyLift = 0f;
sb.AppendLine("  [8] Boss 数值: hp=620 spd=4.4 sight=30 atkRange=7 cd=0.9 xp=120");

// ---- 9) Hitbox：龙是近战+远程混合，Hitbox 覆盖体前一大块 ----
var hb = tmpl.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null)
{
    var hbGo = new GameObject("Hitbox");
    hbGo.transform.SetParent(tmpl.transform, false);
    hb = hbGo.AddComponent<InkWash.Combat.Hitbox>();
}
hb.owner = tmpl;
hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 1.2f, 0.5f);
hb.pointB = new Vector3(0f, 1.6f, 4.5f);   // 前伸到 4.5 m（龙体长）
hb.radius = 1.6f;
hb.damage = 22f;
hb.knockback = 7f;
hb.hitStun = 0.45f;
hb.hitStop = 0.07f;
sb.AppendLine("  [9] Hitbox: 线段 (0,1.2,0.5)→(0,1.6,4.5) r=1.6 dmg=22 kb=7");

// ---- 10) Muzzle（龙息兼容点）----
var muzzle = tmpl.transform.Find("Muzzle");
if (muzzle == null)
{
    var m = new GameObject("Muzzle");
    m.transform.SetParent(tmpl.transform, false);
    muzzle = m.transform;
}
muzzle.localPosition = new Vector3(0f, 2.0f, 3.0f);
sb.AppendLine("  [10] Muzzle.localPosition = (0, 2.0, 3.0)");

// ---- 11) 存盘 ----
tmpl.name = outName;
string outPath = EnemyDir + "/" + outName + ".prefab";
PrefabUtility.SaveAsPrefabAsset(tmpl, outPath);
sb.AppendLine("  [11] 已存 " + outPath);

UnityEngine.Object.DestroyImmediate(src);

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

File.WriteAllText(Path.Combine(root, "Tools/reports/a22_dragon_prefab.txt"), sb.ToString());
Debug.Log("[a22]\n" + sb.ToString());

(Vector3 mn, Vector3 mx) VertexBounds(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    var inv = go.transform.worldToLocalMatrix;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.sharedMesh == null) continue;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        foreach (var v in baked.vertices)
        {
            var p = inv.MultiplyPoint3x4(smr.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
        UnityEngine.Object.DestroyImmediate(baked);
    }
    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
    {
        if (mf.GetComponent<SkinnedMeshRenderer>() != null || mf.sharedMesh == null) continue;
        foreach (var v in mf.sharedMesh.vertices)
        {
            var p = inv.MultiplyPoint3x4(mf.transform.localToWorldMatrix.MultiplyPoint3x4(v));
            mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
        }
    }
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
