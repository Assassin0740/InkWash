// a48_hover_verify.cs —— 独立场地验证龙的盘旋升空（不依赖试玩台）
//
// 为什么不用试玩台：
//   a44/a46 的教训是「先证明台子是好的，再证明龙能打」。试玩台会因为
//   NavMeshAgent 建不起来而静默自由落体，把一切读数污染掉。
//
// 本脚本的做法：
//   ① 在场景里**自建一块平地**（Cube 放大），铺到 y=0
//   ② 在平地中心生成龙（不挂 NavMeshAgent 依赖 —— 龙自带 Update() 锁地兜底）
//   ③ 挂探针，逐帧记录 Model 容器的 localPosition.y 与 transform.y
//   ④ 主动调用 EnterHover，观察升幅
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

var root = Path.GetDirectoryName(Application.dataPath);
var sb = new StringBuilder();
sb.AppendLine("========== a48 盘旋升空独立验证 ==========");

// ---- 清掉上次残留 ----
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.name.StartsWith("HoverTest") || g.name.StartsWith("Z_Enemy_MoLong_Test"))
        UnityEngine.Object.DestroyImmediate(g);

// ---- 自建平地 ----
var ground = GameObject.Find("A48_Ground");
if (ground == null)
{
    ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
    ground.name = "A48_Ground";
    ground.transform.position = new Vector3(0f, -0.5f, 0f);
    ground.transform.localScale = new Vector3(80f, 1f, 80f);
}

// ---- 读龙 prefab 真值 ----
string prefabPath = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
if (prefab == null) { sb.AppendLine("★ 找不到 prefab: " + prefabPath); }
else
{
    sb.AppendLine("prefab = " + prefab.name);

    // 用 InstantiatePrefab 拿 Prefab 实例（避免骨骼引用断裂）
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.name = "HoverTestDragon";
    go.transform.position = new Vector3(0f, 0.2f, 0f);

    var d = go.GetComponent<EnemyDragon>();
    sb.AppendLine("EnemyDragon = " + (d != null ? "有" : "★无"));

    // ---- 骨骼健康 ----
    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
    if (smr != null)
    {
        int nn = 0;
        foreach (var b in smr.bones) if (b != null) nn++;
        sb.AppendLine("SMR=" + smr.name + " bones=" + smr.bones.Length
                      + " 非null=" + nn + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "NULL"));
    }

    // ---- 反射读私有字段 ----
    var t = typeof(EnemyDragon);
    var fModelRoot = t.GetField("_modelRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var fSpine = t.GetField("_spine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var fAirborne = t.GetField("_airborne", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var fHoverH = t.GetField("hoverHeight", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
    var mBeginHover = t.GetMethod("BeginHover", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

    var spine = fSpine != null ? fSpine.GetValue(d) as System.Collections.IList : null;
    sb.AppendLine("脊骨链节数 = " + (spine != null ? spine.Count : -1));
    sb.AppendLine("hoverHeight = " + (fHoverH != null ? fHoverH.GetValue(d).ToString() : "?"));

    var mr = fModelRoot != null ? fModelRoot.GetValue(d) as Transform : null;
    sb.AppendLine("_modelRoot = " + (mr != null ? Path2(mr) : "★null"));
    if (mr != null) sb.AppendLine("  localPosition = " + mr.localPosition.ToString("F4"));

    if (d != null)
    {
        // ★ 不启 NavMeshAgent 的依赖路径：直接调 BeginHover 观察模型抬升
        d.enabled = true;
        if (mBeginHover != null)
        {
            mBeginHover.Invoke(d, null);
            sb.AppendLine("已调用 BeginHover");
        }
        else sb.AppendLine("★ 找不到 BeginHover 方法");
    }

    // ---- 当场量升幅 ----
    float y0 = mr != null ? mr.localPosition.y : 0f;
    sb.AppendLine();
    sb.AppendLine("---- 立即量（同一帧，EnterHover 后） ----");
    sb.AppendLine("  modelRoot.localPosition.y = " + (mr != null ? mr.localPosition.y.ToString("F4") : "?"));
    sb.AppendLine("  transform.y = " + go.transform.position.y.ToString("F3"));

    // 龙有多重 SMR，都列一下
    sb.AppendLine();
    sb.AppendLine("---- 所有 SkinnedMeshRenderer ----");
    foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        int nn = 0;
        foreach (var b in s.bones) if (b != null) nn++;
        sb.AppendLine("  " + Path2(s.transform) + " bones=" + s.bones.Length + " 非null=" + nn
                      + " rootBone=" + (s.rootBone != null ? s.rootBone.name : "NULL")
                      + " shader=" + (s.sharedMaterial != null && s.sharedMaterial.shader != null ? s.sharedMaterial.shader.name : "?"));
    }

    // 场景标记
    EditorUtility.SetDirty(go);
}

string Path2(Transform t)
{
    var s = t.name;
    var p = t.parent;
    int g = 0;
    while (p != null && g++ < 8) { s = p.name + "/" + s; p = p.parent; }
    return s;
}

File.WriteAllText(Path.Combine(root, "Tools/reports/a48_hover_verify.txt"), sb.ToString());
Debug.Log("[a48]\n" + sb.ToString());
