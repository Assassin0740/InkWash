// dg_prefab_forensic.cs —— 一次问清「龙的 mesh 为什么读成 null」的全部层级
//
// 背景（已确认的硬事实）：
//   ① Z_Dragon.prefab 的 SMR 是 **inline**（m_Mesh 字段在文件里，不是占位）：
//        m_Mesh: {fileID: -3325053396650211315, guid: c68a78a74a299104a8f51cee5029c146, type: 3}
//      该 guid 已反查 = Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx
//      同一个 guid 的 **材质**（fileID -1725437989455171953）读出来是好的 ⇒ 不是"整个文件没导入"
//   ② 但编辑模式 sharedMesh == null，四种读法（含 LoadPrefabContents / InstantiatePrefab）一致
//   ③ Enemy_MoYan.prefab 单独读 8 个 SMR 全部正常，而在 MoLong 里一个都不出现
//   ⇒ 本轮要区分三件事，不再猜：
//      (a) fbx 里那个 mesh 子资产的 localId 还在不在（引用是否悬空）
//      (b) Z_Dragon.prefab 的 SMR 到底读到了什么（SerializeObject 原始字段）
//      (c) MoLong 里 Enemy_MoYan 那一支为什么整体不出现（层级实况 + 源链逐跳）
//
// 只读，不改任何资产；不进出 Play。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

const long MeshFileId = -3325053396650211315L;
const string FbxPath = "Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx";
const string DragonPrefab = "Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab";
const string MoLongPrefab = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
const string MoYanPrefab = "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab";

var sb = new StringBuilder();

Func<Transform, string> pathOf = (t) =>
{
    var p = new StringBuilder(t.name);
    Transform c = t.parent;
    int guard = 0;
    while (c != null && guard++ < 40) { p.Insert(0, c.name + "/"); c = c.parent; }
    return p.ToString();
};

Func<Transform, string> compsOf = (t) =>
{
    var cs = t.GetComponents<Component>();
    var p = new List<string>();
    foreach (var c in cs)
    {
        if (c == null) { p.Add("<Missing>"); continue; }
        p.Add(c.GetType().Name);
    }
    return string.Join(",", p.ToArray());
};

Func<SkinnedMeshRenderer, string> smrDesc = (s) =>
{
    var p = new List<string>();
    p.Add(pathOf(s.transform));
    p.Add("mesh=" + (s.sharedMesh == null ? "null" : s.sharedMesh.name + "(" + s.sharedMesh.vertexCount + "v)"));
    int ml = 0;
    var mn = new StringBuilder();
    if (s.sharedMaterials != null)
    {
        ml = s.sharedMaterials.Length;
        for (int i = 0; i < s.sharedMaterials.Length; i++)
            mn.Append(i > 0 ? "," : "").Append(s.sharedMaterials[i] == null ? "null" : s.sharedMaterials[i].name);
    }
    p.Add("mats=" + ml + "[" + mn + "]");
    p.Add("bones=" + (s.bones == null ? "null" : s.bones.Length.ToString()));
    p.Add("enabled=" + s.enabled);
    p.Add("activeInHierarchy=" + s.gameObject.activeInHierarchy);
    return string.Join("  |  ", p.ToArray());
};

// 逐跳追源（最多 6 跳），每跳打印资产路径 —— 用来看"只解析一层"具体停在哪
Func<UnityEngine.Object, string> sourceChain = (start) =>
{
    var p = new List<string>();
    UnityEngine.Object cur = start;
    int hop = 0;
    while (hop++ < 6)
    {
        if (cur == null) { p.Add("(null)"); break; }
        string ap = AssetDatabase.GetAssetPath(cur);
        if (string.IsNullOrEmpty(ap)) ap = "<无资产路径>";
        p.Add(hop + ":" + cur.GetType().Name + "@" + ap);
        UnityEngine.Object next = PrefabUtility.GetCorrespondingObjectFromSource(cur);
        if (next == null) { p.Add("源=null（到顶）"); break; }
        if (next == cur) { p.Add("源=自身（到顶）"); break; }
        cur = next;
    }
    return string.Join(" -> ", p.ToArray());
};

sb.AppendLine("===== dg_prefab_forensic =====");
sb.AppendLine("isPlaying=" + EditorApplication.isPlaying
              + "   activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
              + "   isDirty=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty);

// ================= 1. fbx 子资产：那个 mesh fileID 还在不在 =================
sb.AppendLine();
sb.AppendLine("[1] " + FbxPath);
sb.AppendLine("    磁盘存在=" + File.Exists(FbxPath));
string guid = AssetDatabase.AssetPathToGUID(FbxPath);
sb.AppendLine("    guid=" + guid);
sb.AppendLine("    依赖哈希=" + AssetDatabase.GetAssetDependencyHash(FbxPath).ToString());
UnityEngine.Object[] subs = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
sb.AppendLine("    LoadAllAssetsAtPath -> " + subs.Length + " 个子资产");
bool idFound = false;
int meshCount = 0;
foreach (var o in subs)
{
    if (o == null) continue;
    string g = "";
    long lid = 0;
    bool ok = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out g, out lid);
    string extra = "";
    Mesh m = o as Mesh;
    if (m != null)
    {
        meshCount++;
        extra = "  verts=" + m.vertexCount + " bindposes=" + (m.bindposes == null ? 0 : m.bindposes.Length)
                + " blendShapes=" + m.blendShapeCount;
    }
    string mark = "";
    if (ok && lid == MeshFileId) { mark = "   ★★★ 正是 m_Mesh 引用的那个 fileID"; idFound = true; }
    sb.AppendLine("      " + o.GetType().Name.PadRight(20) + " " + o.name.PadRight(36)
                  + " localId=" + (ok ? lid.ToString() : "<取不到>") + extra + mark);
}
sb.AppendLine("    Mesh 子资产 " + meshCount + " 个");
sb.AppendLine("    ⇒ m_Mesh 的 fileID(" + MeshFileId + ") 在 fbx 里 "
              + (idFound ? "**存在** ⇒ 引用不悬空，问题在别处" : "★**不存在** ⇒ 引用悬空（fbx 内部 id 变了）"));

// ================= 2. Z_Dragon.prefab 的 SMR 原始字段 =================
sb.AppendLine();
sb.AppendLine("[2] " + DragonPrefab);
GameObject dg = AssetDatabase.LoadAssetAtPath<GameObject>(DragonPrefab);
sb.AppendLine("    LoadAssetAtPath -> " + (dg == null ? "null" : dg.name + " (子物体 " + dg.transform.childCount + " 个)"));
if (dg != null)
{
    var smrs = dg.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    sb.AppendLine("    SMR 共 " + smrs.Length + " 个");
    foreach (var s in smrs)
    {
        sb.AppendLine("      " + smrDesc(s));
        sb.AppendLine("        源链: " + sourceChain(s));
        var so = new SerializedObject(s);
        var pMesh = so.FindProperty("m_Mesh");
        if (pMesh != null)
        {
            sb.AppendLine("        [Serialized] m_Mesh.objectReferenceValue="
                          + (pMesh.objectReferenceValue == null ? "null" : pMesh.objectReferenceValue.name)
                          + "  instanceIDValue=" + pMesh.objectReferenceInstanceIDValue);
        }
        var pMats = so.FindProperty("m_Materials");
        if (pMats != null)
            sb.AppendLine("        [Serialized] m_Materials.arraySize=" + pMats.arraySize);
        var pBones = so.FindProperty("m_Bones");
        if (pBones != null)
            sb.AppendLine("        [Serialized] m_Bones.arraySize=" + pBones.arraySize);
        // 骨骼里 drgon_ 命名实况（前 5 + 后 5），确认脊椎链还在
        if (s.bones != null && s.bones.Length > 0)
        {
            var nm = new StringBuilder();
            for (int i = 0; i < s.bones.Length && i < 5; i++)
                nm.Append(s.bones[i] == null ? "<null>" : s.bones[i].name).Append(" ");
            nm.Append("... ");
            for (int i = Mathf.Max(0, s.bones.Length - 5); i < s.bones.Length; i++)
                nm.Append(s.bones[i] == null ? "<null>" : s.bones[i].name).Append(" ");
            sb.AppendLine("        bones[0..4 + 末5] = " + nm);
        }
    }
}

// ================= 3. MoLong：层级实况 + 源链 =================
sb.AppendLine();
sb.AppendLine("[3] " + MoLongPrefab);
GameObject ml = AssetDatabase.LoadAssetAtPath<GameObject>(MoLongPrefab);
sb.AppendLine("    LoadAssetAtPath -> " + (ml == null ? "null" : ml.name + " (子物体 " + ml.transform.childCount + " 个)"));
if (ml != null)
{
    sb.AppendLine("    --- 层级（深度 <=5，含 inactive）---");
    var stack = new List<KeyValuePair<Transform, int>>();
    for (int i = ml.transform.childCount - 1; i >= 0; i--)
        stack.Add(new KeyValuePair<Transform, int>(ml.transform.GetChild(i), 1));
    int printed = 0;
    while (stack.Count > 0 && printed < 120)
    {
        var kv = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        Transform t = kv.Key;
        int dp = kv.Value;
        sb.AppendLine("      " + new string(' ', dp * 2) + t.name
                      + "  [" + compsOf(t) + "]  activeSelf=" + t.gameObject.activeSelf);
        printed++;
        if (dp < 5)
            for (int i = t.childCount - 1; i >= 0; i--)
                stack.Add(new KeyValuePair<Transform, int>(t.GetChild(i), dp + 1));
    }
    sb.AppendLine("      （共打印 " + printed + " 个节点；stack 剩余 " + stack.Count + "）");

    sb.AppendLine("    --- 每个根子树的 SMR 数 ---");
    for (int i = 0; i < ml.transform.childCount; i++)
    {
        Transform c = ml.transform.GetChild(i);
        var sc = c.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int withMesh = 0;
        foreach (var s in sc) if (s.sharedMesh != null) withMesh++;
        sb.AppendLine("      " + c.name + ": 子物体 " + c.GetComponentsInChildren<Transform>(true).Length
                      + " 个, SMR=" + sc.Length + ", 有mesh=" + withMesh);
    }

    var mls = ml.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    sb.AppendLine("    --- MoLong 全树 SMR " + mls.Length + " 个 ---");
    foreach (var s in mls)
    {
        sb.AppendLine("      " + smrDesc(s));
        sb.AppendLine("        源链: " + sourceChain(s));
    }
}

// ================= 4. Enemy_MoYan 单独读（对照） =================
sb.AppendLine();
sb.AppendLine("[4] " + MoYanPrefab + "（对照：它单独读是好的）");
GameObject my = AssetDatabase.LoadAssetAtPath<GameObject>(MoYanPrefab);
if (my != null)
{
    var ys = my.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    sb.AppendLine("    SMR " + ys.Length + " 个，有mesh " + CountWithMesh(ys) + " 个");
    if (ys.Length > 0) sb.AppendLine("    第一个源链: " + sourceChain(ys[0]));
    sb.AppendLine("    根子物体 " + my.transform.childCount + " 个：" + ChildNames(my.transform));
}

// ================= 5. 场景里有没有龙的实例 =================
sb.AppendLine();
sb.AppendLine("[5] 当前场景里的龙实例");
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
int found = 0;
foreach (var root in scene.GetRootGameObjects())
{
    var all = root.GetComponentsInChildren<Transform>(true);
    foreach (var t in all)
    {
        if (t.name == "Z_Enemy_MoLong" || t.name == "Z_Dragon" || t.name == "dragon")
        {
            var sc = t.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int wm = 0;
            foreach (var s in sc) if (s.sharedMesh != null) wm++;
            sb.AppendLine("      " + pathOf(t) + "  SMR=" + sc.Length + "  有mesh=" + wm
                          + "  activeInHierarchy=" + t.gameObject.activeInHierarchy);
            found++;
        }
    }
}
if (found == 0) sb.AppendLine("      （场景里没有叫 Z_Enemy_MoLong / Z_Dragon / dragon 的对象）");

// ================= 6. NavMesh 现状（只读，为烤图做准备） =================
sb.AppendLine();
sb.AppendLine("[6] NavMesh 现状（只读）");
try
{
    var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
    sb.AppendLine("    CalculateTriangulation: 顶点 " + tri.vertices.Length + " / 三角形 " + (tri.indices.Length / 3));
    sb.AppendLine("    ⇒ " + (tri.vertices.Length == 0 ? "★ 场景 NavMesh 为空（未烤或未保存）" : "已有烘焙数据"));
}
catch (Exception e)
{
    sb.AppendLine("    CalculateTriangulation 抛异常: " + e.GetType().Name + " " + e.Message);
}

// ================= 落盘 =================
string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "reports");
Directory.CreateDirectory(outDir);
string outPath = Path.Combine(outDir, "dg_prefab_forensic.txt");
File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
sb.AppendLine();
sb.AppendLine("报告已写入: " + outPath);
return sb.ToString();

// 顶层语句里用的两个小助手（放在 return 之后，C# 允许局部函数）
static int CountWithMesh(SkinnedMeshRenderer[] arr)
{
    int n = 0;
    foreach (var s in arr) if (s != null && s.sharedMesh != null) n++;
    return n;
}

static string ChildNames(Transform t)
{
    var sb = new StringBuilder();
    for (int i = 0; i < t.childCount; i++) sb.Append(t.GetChild(i).name).Append(" ");
    return sb.ToString();
}
