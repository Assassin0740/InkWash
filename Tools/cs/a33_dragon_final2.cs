// a33_dragon_final2.cs —— 龙 prefab 定尺寸（用已验证可信的口径）
//
// 经过 a26/a29/a31/a32 四轮排查，口径问题终于查清，结论如下：
//
//   【可信】`skinnedMeshRenderer.bounds`（a29 实测随 k 线性：3.02→6.04→9.28→12.09）
//           —— 但它是**渲染期惰性更新**，同一帧读可能拿到旧值。
//   【不可信】`BakeMesh + localToWorldMatrix`（a29：k=1 之后跳到 362.87 并冻结）
//   【不可信】在 holder 自己的空间量（自我抵消）
//   【读法的坑】`smr.bones` 在 **InstantiatePrefab 出来的实例**上可能读成全 null
//           （a31 误判），但 LoadPrefabContents 与新建实例都正常（a32 已证）
//           ⇒ **判断资产健康度要用源 prefab 或 LoadPrefabContents，不要用实例**
//
//   【蒙皮有效性】a32 已证：转 drgon_03 让顶点位移 1480（max）
//           ⇒ 程序驱动脊骨**确实会反映到画面**，EnemyDragon 的方案成立。
//
// 本脚本用 `smr.bounds` + **每轮独立实例化**（避开惰性更新）来定尺寸与落地。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
const string EnemyDir = "Assets/_Project/Prefabs/Enemies";
string outPath = EnemyDir + "/Z_Enemy_MoLong.prefab";

sb.AppendLine("========== a33 龙 prefab 定尺寸（终版）==========");

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
if (prefab == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a33_dragon_final2.txt"), sb.ToString()); Debug.LogError(sb.ToString()); return; }

// ★ 关键手法：**每次测量都新建一个实例**，且新建后立刻读。
//   惰性更新的坑在于"同一实例改了再读"；新建实例的 bounds 是初次计算，值正确。
//   若仍担心，就"改 → 销毁 → 重建 → 读"。
float MeasureLongest(float holderScale, Vector3 holderRot, float holderY, out Vector3 mn, out Vector3 mx, out float yLow)
{
    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    inst.transform.position = Vector3.zero;
    inst.transform.rotation = Quaternion.identity;
    inst.transform.localScale = Vector3.one;

    var visual = inst.transform.Find("Visual");
    if (visual != null) visual.localScale = Vector3.one;

    var holder = inst.transform.Find("Visual/Model");
    if (holder != null)
    {
        holder.localScale = Vector3.one * holderScale;
        holder.localRotation = Quaternion.Euler(holderRot);
        holder.localPosition = new Vector3(0f, holderY, 0f);
    }

    var b = BoundsOf(inst);
    mn = b.mn; mx = b.mx;
    yLow = b.mn.y;
    float len = Mathf.Max(b.mx.x - b.mn.x, b.mx.z - b.mn.z);

    UnityEngine.Object.DestroyImmediate(inst);
    return len;
}

// ---- 1) 基准：scale=1, 无旋转, 无位移 ----
Vector3 mn, mx; float yl;
float len0 = MeasureLongest(1f, Vector3.zero, 0f, out mn, out mx, out yl);
sb.AppendLine("  [1] 基准（scale=1, rot=0）体长=" + len0.ToString("F4")
              + "  Y[" + mn.y.ToString("F3") + "," + mx.y.ToString("F3") + "]");
if (len0 < 0.5f) { sb.AppendLine("★ 基准异常，放弃"); Write(); Debug.LogError("[a33]"); return; }

// ---- 2) 定向：长轴按实测 ----
float lx0 = mx.x - mn.x, lz0 = mx.z - mn.z;
bool longIsX = lx0 >= lz0;
float rotY = longIsX ? 270f : 0f;
float len1 = MeasureLongest(1f, new Vector3(0f, rotY, 0f), 0f, out mn, out mx, out yl);
sb.AppendLine("  [2] 长轴=" + (longIsX ? "X" : "Z") + " → rot.y=" + rotY
              + "  旋转后体长=" + len1.ToString("F4"));

// ---- 3) 缩放：归一到 7 m ----
float target = 7.0f;
float k = target / len1;
float len2 = MeasureLongest(k, new Vector3(0f, rotY, 0f), 0f, out mn, out mx, out yl);
sb.AppendLine("  [3] k = " + target + " / " + len1.ToString("F4") + " = " + k.ToString("F5")
              + "  →  缩放后体长=" + len2.ToString("F4")
              + (Mathf.Abs(len2 - target) < 0.3f ? "  ✓ 生效" : "  ★ 不符"));
if (Mathf.Abs(len2 - target) > 0.3f && len2 > 0.01f)
{
    k *= target / len2;
    len2 = MeasureLongest(k, new Vector3(0f, rotY, 0f), 0f, out mn, out mx, out yl);
    sb.AppendLine("  二次迭代 k=" + k.ToString("F5") + " → 体长=" + len2.ToString("F4")
                  + (Mathf.Abs(len2 - target) < 0.3f ? "  ✓" : "  ★"));
}

// ---- 4) 落地：Y 底 → 0 ----
float dy = -yl;
float len3 = MeasureLongest(k, new Vector3(0f, rotY, 0f), dy, out mn, out mx, out yl);
sb.AppendLine("  [4] 落地 dy=" + dy.ToString("F4") + " → Y[" + mn.y.ToString("F4") + "," + mx.y.ToString("F3") + "]"
              + "  体长=" + len3.ToString("F4"));

// ---- 5) 应用到真实实例并存盘 ----
var host = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
host.transform.position = Vector3.zero;
host.transform.rotation = Quaternion.identity;
host.transform.localScale = Vector3.one;
var vv = host.transform.Find("Visual");
if (vv != null) vv.localScale = Vector3.one;
var hh = host.transform.Find("Visual/Model");
if (hh != null)
{
    hh.localScale = Vector3.one * k;
    hh.localRotation = Quaternion.Euler(0f, rotY, 0f);
    hh.localPosition = new Vector3(0f, dy, 0f);
}
// 胶囊体按实际体型
var cap = host.GetComponent<CapsuleCollider>();
if (cap != null)
{
    float h = mx.y - mn.y;
    cap.height = Mathf.Max(1.5f, h);
    cap.radius = 1.4f;
    cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
    sb.AppendLine("  [5] Capsule: h=" + cap.height.ToString("F2") + " r=1.4 centerY=" + cap.center.y.ToString("F2"));
}
// Hitbox 按体长伸展
var hb = host.GetComponentInChildren<InkWash.Combat.Hitbox>(true);
if (hb != null)
{
    hb.pointA = new Vector3(0f, 1.0f, 0.5f);
    hb.pointB = new Vector3(0f, 1.2f, len3 * 0.62f);
    hb.radius = 1.5f;
    sb.AppendLine("  [5] Hitbox B.z = " + hb.pointB.z.ToString("F2"));
}
var muzzle = host.transform.Find("Muzzle");
if (muzzle != null) muzzle.localPosition = new Vector3(0f, mx.y * 0.75f, len3 * 0.45f);

host.name = "Z_Enemy_MoLong";
PrefabUtility.SaveAsPrefabAsset(host, outPath);
UnityEngine.Object.DestroyImmediate(host);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

// ---- 6) 回读证实 ----
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
var saved = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
sb.AppendLine();
sb.AppendLine("  [6] 回读 " + outPath + " = " + (saved != null ? "存在 ✓" : "★ 不存在"));
if (saved != null)
{
    sb.AppendLine("      Model.scale=" + saved.transform.Find("Visual/Model").localScale.ToString("F5")
                  + "  rot.y=" + saved.transform.Find("Visual/Model").localRotation.eulerAngles.y.ToString("F0")
                  + "  pos.y=" + saved.transform.Find("Visual/Model").localPosition.y.ToString("F4"));
    var tm2 = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyDir + "/Enemy_MoYan.prefab");
    int d = 0; foreach (var t in tm2.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("drgon_")) d++;
    sb.AppendLine("      模板污染=" + d + (d == 0 ? " ✓" : " ★★"));
}

Write();
Debug.Log("[a33]\n" + sb.ToString());

void Write() => File.WriteAllText(Path.Combine(root, "Tools/reports/a33_dragon_final2.txt"), sb.ToString());

(Vector3 mn, Vector3 mx) BoundsOf(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    bool any = false;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    { var bb = smr.bounds; mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true; }
    foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
    {
        if (mr.GetComponent<SkinnedMeshRenderer>() != null) continue;
        var bb = mr.bounds; mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true;
    }
    return any ? (mn, mx) : (Vector3.zero, Vector3.zero);
}
