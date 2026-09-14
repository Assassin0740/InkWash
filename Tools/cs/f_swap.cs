// 把 Player.prefab 的 Visual 子树从 KayKit Rogue_Hooded 换成 Char_Feng。
//
// 依据（都是实测出来的，不是猜）：
//   1. 现主角真实身高 = 2.1730（BakeMesh 逐网格，8 个网格全是角色部件，无武器/特效混入）
//      → 见 Tools/cs/f_measure2.cs
//   2. Feng 绑定姿势真实身高 = 0.9819，脚底在 y≈-0.0001（原点就在脚底，与 KayKit 的"原点在脚底上方"不同）
//      → 所以 Visual.localPosition.y 要由 0.088 改成 0
//   3. 缩放 S = 2.1730 / 0.9819 = 2.213 —— 对齐现主角身高，保证相机取景
//      （ThirdPersonCamera.pivotOffset 是固定 1.42，不看骨骼）与占屏比不变
//   4. 刀身/拖尾挂在右手骨下，会继承 Visual 的缩放 → 三个尺度参数必须同除 S
//
// 顺带：不动任何代码。脚本里没有硬编码骨骼路径，SwordVfx 用 GetBoneTransform(RightHand) 跟着 Avatar 走。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    System.Action flush = () => System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/f_swap.txt"), sb.ToString());

    const string PREFAB = "Assets/_Project/Prefabs/Player/Player.prefab";
    const string FENG_FBX = "Assets/Char_Feng/Fbx/Feng.fbx";
    const float S = 2.213f;          // 2.1730 / 0.9819

    var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FENG_FBX);
    if (fbxAsset == null) { sb.AppendLine("[ERR] 找不到 " + FENG_FBX); flush(); yield break; }

    Avatar fengAvatar = null;
    foreach (var a in AssetDatabase.LoadAllAssetsAtPath(FENG_FBX))
        if (a is Avatar av) { fengAvatar = av; break; }
    sb.AppendLine("Feng Avatar = " + (fengAvatar == null ? "NULL" : fengAvatar.name + " valid=" + fengAvatar.isValid + " human=" + fengAvatar.isHuman));
    if (fengAvatar == null) { sb.AppendLine("[ERR] Feng.fbx 里没有 Avatar"); flush(); yield break; }

    var contents = PrefabUtility.LoadPrefabContents(PREFAB);
    bool ok = true;

    var vis = contents.transform.Find("Visual");
    if (vis == null) { sb.AppendLine("[ERR] prefab 里没有 Visual"); ok = false; }

    GameObject feng = null;
    if (ok)
    {
        // ---- 1. 清掉旧的 Visual 子节点（bones + 8 个 SMR）----
        var old = new List<string>();
        for (int i = 0; i < vis.childCount; i++) old.Add(vis.GetChild(i).name);
        sb.AppendLine("清掉的旧子节点 " + vis.childCount + " 个：" + string.Join(", ", old.ToArray()));
        for (int i = vis.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(vis.GetChild(i).gameObject);

        // ---- 2. 实例化 Feng 并挂到 Visual 下 ----
        feng = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, vis);
        if (feng == null) { sb.AppendLine("[ERR] 实例化 Feng 失败"); ok = false; }
    }

    if (ok)
    {
        feng.name = "Feng";
        feng.transform.localPosition = Vector3.zero;
        feng.transform.localRotation = Quaternion.identity;
        feng.transform.localScale = Vector3.one;

        var strayAnim = feng.GetComponentInChildren<Animator>(true);
        if (strayAnim != null)
        {
            sb.AppendLine("删掉 Feng 自带的 Animator（在 " + strayAnim.gameObject.name + " 上）");
            Object.DestroyImmediate(strayAnim);
        }

        // ---- 3. Animator 换成 Feng 的 Avatar ----
        var anim = contents.GetComponent<Animator>();
        string oldAvatarName = anim.avatar != null ? anim.avatar.name : "NULL";
        anim.avatar = fengAvatar;
        sb.AppendLine("Animator.avatar: " + oldAvatarName + "  ->  " + fengAvatar.name);

        // ---- 4. Visual 的缩放与静态竖直偏移 ----
        sb.AppendLine(string.Format("Visual 旧值 localPosition={0} localScale={1}", vis.localPosition.ToString("F4"), vis.localScale.ToString("F4")));
        vis.localPosition = Vector3.zero;      // Feng 的原点已在脚底，不需要静态抵消
        vis.localScale = new Vector3(S, S, S);
        sb.AppendLine(string.Format("Visual 新值 localPosition={0} localScale={1}", vis.localPosition.ToString("F4"), vis.localScale.ToString("F4")));

        // ---- 5. 刀身/拖尾：挂在右手骨下会继承 S，三个尺度参数同除 S ----
        var vfx = contents.GetComponent<InkWash.Effects.SwordVfx>();
        if (vfx != null)
        {
            float ol = vfx.bladeLength, oy = vfx.bladeLocalOffset.y, ow = vfx.trailStartWidth;
            vfx.bladeLength = ol / S;
            vfx.bladeLocalOffset = new Vector3(vfx.bladeLocalOffset.x, oy / S, vfx.bladeLocalOffset.z);
            vfx.trailStartWidth = ow / S;
            sb.AppendLine(string.Format("刀身参数同除 S：bladeLength {0:F4}->{1:F4}   bladeLocalOffset.y {2:F4}->{3:F4}   trailStartWidth {4:F4}->{5:F4}",
                ol, vfx.bladeLength, oy, vfx.bladeLocalOffset.y, ow, vfx.trailStartWidth));
        }
        else sb.AppendLine("[WARN] 根上没找到 SwordVfx");

        // ---- 6. 保存 ----
        PrefabUtility.SaveAsPrefabAsset(contents, PREFAB);
        sb.AppendLine("已保存 " + PREFAB);

        // ---- 7. 复核：按新结构量一次身高（必须在 Unload 之前）----
        sb.AppendLine();
        sb.AppendLine("================ 复核（新结构实测） ================");
        var smrs = vis.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var m = new Mesh();
        float mn = float.MaxValue, mx = float.MinValue, mnx = float.MaxValue, mxx = float.MinValue;
        string names = "";
        foreach (var s in smrs)
        {
            if (s.sharedMesh == null) continue;
            s.BakeMesh(m, true);
            var l2w = s.transform.localToWorldMatrix;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                var w = l2w.MultiplyPoint3x4(v[i]);
                if (w.y < mn) mn = w.y; if (w.y > mx) mx = w.y;
                if (w.x < mnx) mnx = w.x; if (w.x > mxx) mxx = w.x;
            }
            names += s.name + "(" + v.Length + ") ";
        }
        Object.DestroyImmediate(m);
        sb.AppendLine("蒙皮网格 " + smrs.Length + " 个：" + names);
        sb.AppendLine(string.Format("新 Visual 子树：高 {0:F4}  (y {1:F4}..{2:F4})   宽 {3:F4}", mx - mn, mn, mx, mxx - mnx));
        sb.AppendLine(string.Format("目标身高 2.1730（现主角）—— 偏差 {0:F4}", (mx - mn) - 2.1730f));

        sb.AppendLine();
        sb.AppendLine("================ 新层级（深度 4） ================");
        Tree(vis, sb, 0, 4);
    }

    PrefabUtility.UnloadPrefabContents(contents);
    flush();
    Debug.Log("[swap] 完成。见 Tools/reports/f_swap.txt");
    yield return null;
}

void Tree(Transform t, System.Text.StringBuilder sb, int d, int maxD)
{
    var comps = new List<string>();
    foreach (var c in t.GetComponents<Component>())
        if (c != null && !(c is Transform)) comps.Add(c.GetType().Name);
    sb.AppendLine(new string(' ', 4 + d * 2) + t.name + "  [" + string.Join(",", comps.ToArray()) + "]");
    if (d >= maxD) { if (t.childCount > 0) sb.AppendLine(new string(' ', 6 + d * 2) + "... (" + t.childCount + " 子节点)"); return; }
    foreach (Transform c in t) Tree(c, sb, d + 1, maxD);
}

return Body();
