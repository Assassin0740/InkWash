// a42_bone_clone_fix.cs —— 龙蒙皮塌陷的真正根因与修法
//
// ===== 根因（a41 已锁死）=====
//   源链路完全健康：
//     FBX        → bones=273 非null=273 rootBone=_rootJoint  ✓
//     Z_Dragon.prefab → bones=273 非null=273 rootBone=_rootJoint  ✓ (546 引用, 0 broken)
//   只有**我建的** Z_Enemy_MoLong.prefab 坏了：
//     文本里 m_Bones 546 条引用**都还在**，但全部解析成 null；rootBone 也被清零
//   ⇒ 这是典型的 **悬空 fileID**：SMR 还在引用"原来的那批 Transform"，
//     而我把整棵子树用 `Instantiate` **克隆**成了新对象 ⇒ 引用指向的原对象不在新 prefab 里。
//
// ===== 为什么之前的做法必然踩这个坑 =====
//   我一直在做：InstantiatePrefab(模板) → 清空 Visual → Instantiate(源子树) → 搬进 Visual → 存盘
//   源 prefab 的 SMR 是**跨 prefab 引用**自己的子树；一旦子树被整体克隆，
//   引用关系**必须重新指向克隆体**。而 `Instantiate` 出来的新对象，
//   其 SMR.bones 指向的是**克隆体自己**（这没问题），
//   但我存盘时用的是 `SaveAsPrefabAsset` ——
//   它只保留"在同一个 prefab 作用域内能解析的引用"。
//
// ===== 正确做法 =====
//   不要"克隆源子树再拼"。而是：
//   ① 以**源 prefab 实例**为基底（它自带完整的骨骼引用关系）
//   ② 把**模板的组件**（EnemyDragon / Hitbox / Muzzle / 材质）搬到它上面
//   ③ 用 `SaveAsPrefabAsset` 存成新资产
//   —— 关键在于：**骨骼树必须整体保留在同一个 prefab 作用域内**，
//      不能把它拆出来克隆再拼回去。
//
//   若必须拼（例如要插一层 Visual/Model 容器），则搬完之后必须
//   **逐个重绑** SMR 的 bones / rootBone 到新层级里的对应 Transform。
//   本脚本走"重绑"路线（因为项目约定要有 Visual/Model 容器）。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(root, "Tools/screenshots/dragon42");
Directory.CreateDirectory(shotDir);

sb.AppendLine("========== a42 龙 prefab：修 bones 引用（重绑）==========");

const string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outPath = EnemyDir + "/Z_Enemy_MoLong.prefab";
string srcPath = "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab";

var tmpl = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Ink_Boss_Dragon.mat");

int dirty = 0;
foreach (var t in tmpl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) dirty++;
if (dirty > 0) { sb.AppendLine("★★ 模板被污染(" + dirty + ")，中止"); W(); Debug.LogError("[a42]"); return; }

// ---------- 做法：把**源 prefab 整棵树**搬进模板，然后**重绑 SMR 引用** ----------
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

// ★ 关键改动：先把**源 prefab 实例化进场景**（作为 Prefab 实例，引用关系天然完整），
//   再把这个**实例整体**挂到 Visual 下。这样引用不会断。
var srcInst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(srcPath));
sb.AppendLine("  源实例化完成: " + srcInst.name);

// 挂到 Visual 下（保留世界变换不变，再用局部值修正）
srcInst.transform.SetParent(visual, false);
srcInst.transform.localPosition = Vector3.zero;
srcInst.transform.localRotation = Quaternion.identity;
srcInst.transform.localScale = Vector3.one;

// 插一层 Model 容器（项目约定：Visual/Model/...）
// 做法：先保持 srcInst 不动，改名为 Model —— 避免再插一层导致引用路径变化
srcInst.name = "Model";
// 内容物保持名字（Z_Dragon 这个根名改成 Model，子节点 Object_6/281/282 不变）
sb.AppendLine("  源 prefab 实例 → 命名为 Model，挂在 Visual 下");

// 量尺寸（用 smr.bounds，世界空间，可信）
bool WB(out Bounds b)
{
    b = new Bounds(); bool first = true;
    foreach (var r in host.GetComponentsInChildren<Renderer>(true))
    {
        if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
    }
    return !first;
}

// 检查搬入后的引用完整性
void AuditBones(string tag)
{
    foreach (var smr in host.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        int nn = 0, nu = 0;
        if (smr.bones != null) foreach (var b in smr.bones) { if (b == null) nu++; else nn++; }
        sb.AppendLine("  [" + tag + "] " + smr.name + " bones=" + (smr.bones == null ? -1 : smr.bones.Length)
                      + " 非null=" + nn + " null=" + nu
                      + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "NULL"));
    }
}
AuditBones("搬入后");

// 朝向：源 prefab 根有 lossyScale 0.7546，骨骼世界范围 4.38×1.64×4.27（长轴 X，头朝 +Z）
// 先摆到 scale=1 看真实尺寸
srcInst.transform.localScale = Vector3.one;
WB(out var b0);
sb.AppendLine("  基线(scale=1) 世界尺寸=(" + b0.size.x.ToString("F2") + ", " + b0.size.y.ToString("F2") + ", " + b0.size.z.ToString("F2") + ")  Y[" + b0.min.y.ToString("F2") + "," + b0.max.y.ToString("F2") + "]");

// 长轴 X → 绕 Y -90° 对到 +Z
srcInst.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
WB(out var b1);
sb.AppendLine("  长轴对+Z 后 世界尺寸=(" + b1.size.x.ToString("F2") + ", " + b1.size.y.ToString("F2") + ", " + b1.size.z.ToString("F2") + ")");

// 落地
srcInst.transform.localPosition = new Vector3(0f, -b1.min.y, 0f);
WB(out var b2);
sb.AppendLine("  落地后 Y[" + b2.min.y.ToString("F3") + ", " + b2.max.y.ToString("F3") + "]");
sb.AppendLine("  ★ 体长(Z)=" + b2.size.z.ToString("F2") + " m  高(Y)=" + b2.size.y.ToString("F2") + " m  宽(X)=" + b2.size.x.ToString("F2") + " m");

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
}

var cap = host.GetComponent<CapsuleCollider>();
if (cap == null) cap = host.AddComponent<CapsuleCollider>();
cap.direction = 2; cap.height = 4.6f; cap.radius = 1.1f; cap.center = new Vector3(0f, 1.0f, 0f);

var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb == null) { var g = new GameObject("Hitbox"); g.transform.SetParent(host.transform, false); hb = g.AddComponent<InkWash.Combat.Hitbox>(); }
hb.owner = host; hb.ownerFaction = InkWash.Combat.Faction.Enemy;
hb.pointA = new Vector3(0f, 1.0f, -1.5f); hb.pointB = new Vector3(0f, 1.2f, 2.5f); hb.radius = 1.2f;
hb.damage = 22f; hb.knockback = 7f; hb.hitStun = 0.45f; hb.hitStop = 0.07f;

var mz = host.transform.Find("Muzzle");
if (mz == null) { var g = new GameObject("Muzzle"); g.transform.SetParent(host.transform, false); mz = g.transform; }
mz.localPosition = new Vector3(0f, 1.4f, 2.6f);

host.name = "Z_Enemy_MoLong";
PrefabUtility.SaveAsPrefabAsset(host, outPath);
UnityEngine.Object.DestroyImmediate(host);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

// ---- 回读审计（这是唯一算数的验收）----
sb.AppendLine();
sb.AppendLine("---- 回读审计 ----");
var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
sb.AppendLine("  " + outPath + " = " + (saved != null ? "存在 ✓" : "★ 不存在"));
if (saved != null)
{
    var c = PrefabUtility.LoadPrefabContents(outPath);
    try
    {
        foreach (var smr in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int nn = 0, nu = 0;
            if (smr.bones != null) foreach (var b in smr.bones) { if (b == null) nu++; else nn++; }
            sb.AppendLine("  " + smr.name + " bones=" + (smr.bones == null ? -1 : smr.bones.Length)
                          + " 非null=" + nn + " null=" + nu
                          + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "NULL"));
        }
        var smrs = c.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (smrs.Length > 0 && smrs[0].bones != null)
        {
            var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            int n = 0;
            foreach (var smr in smrs)
            {
                if (smr.bones == null) continue;
                foreach (var b in smr.bones)
                {
                    if (b == null) continue;
                    var p = b.position; n++;
                    if (Mathf.Abs(p.x) > 1e5f || Mathf.Abs(p.y) > 1e5f || Mathf.Abs(p.z) > 1e5f) continue;
                    mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                }
            }
            sb.AppendLine("  骨骼数=" + n + "  世界范围 [" + mn.ToString("F2") + "] ~ [" + mx.ToString("F2") + "]");
            sb.AppendLine("  尺寸=(" + (mx.x - mn.x).ToString("F2") + ", " + (mx.y - mn.y).ToString("F2") + ", " + (mx.z - mn.z).ToString("F2") + ")");
        }
    }
    finally { PrefabUtility.UnloadPrefabContents(c); }
}
int dg2 = 0;
foreach (var t in tmpl.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) dg2++;
sb.AppendLine("  模板污染检查: drgon_* = " + dg2 + (dg2 == 0 ? " ✓" : " ★被污染"));

// ---- 渲染验证（这才是唯一能证明"看得出来"的判据）----
sb.AppendLine();
sb.AppendLine("---- 渲染 ----");
var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

var gnd = GameObject.CreatePrimitive(PrimitiveType.Cube);
gnd.transform.position = new Vector3(0f, -0.05f, 0f);
gnd.transform.localScale = new Vector3(60f, 0.1f, 60f);

var refCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
refCube.transform.position = new Vector3(3.2f, 1.085f, 3.2f);
refCube.transform.localScale = new Vector3(0.6f, 2.17f, 0.6f);

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
    Shoot("A" + k + "_" + (k * 45) + "deg", new Vector3(Mathf.Sin(a) * 12f, 5f, Mathf.Cos(a) * 12f), new Vector3(0f, 1.2f, 0f), 42f);
}
Shoot("A8_俯视", new Vector3(0.01f, 15f, 0.01f), Vector3.zero, 48f);

EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity", OpenSceneMode.Single);

W();
Debug.Log("[a42]\n" + sb.ToString());

void W() => File.WriteAllText(Path.Combine(root, "Tools/reports/a42_bone_clone_fix.txt"), sb.ToString());
