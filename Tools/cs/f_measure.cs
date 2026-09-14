// 精确测量：用 BakeMesh 得到"当前姿势的真实包围盒"，避免 SkinnedMeshRenderer.bounds 虚高
// 同时报出 CharacterController 胶囊尺寸 —— 那才是工程里的"人物标准身高"
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();

    sb.AppendLine("========== Feng ==========");
    var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Char_Feng/Fbx/Feng.fbx");
    var feng = Object.Instantiate(fbx);
    feng.transform.position = Vector3.zero; feng.transform.rotation = Quaternion.identity; feng.transform.localScale = Vector3.one;
    Bake(feng, sb, "绑定姿势");
    var fengAn = feng.GetComponentInChildren<Animator>();
    var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Char_Feng/Animation/Idle.anim");
    if (idle != null && fengAn != null && fengAn.avatar != null)
    {
        idle.SampleAnimation(feng, 0.65f);
        yield return null;
        Bake(feng, sb, "Idle@0.65s");
        idle.SampleAnimation(feng, 0f);
        yield return null;
        Bake(feng, sb, "Idle@0s");
    }
    var fengCC = feng.GetComponentInChildren<CharacterController>();
    sb.AppendLine("  CharacterController = " + (fengCC != null ? "有" : "无"));
    Object.DestroyImmediate(feng);

    sb.AppendLine("========== Player.prefab（现主角） ==========");
    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
    var pl = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    foreach (var mb in pl.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
    pl.transform.position = Vector3.zero; pl.transform.rotation = Quaternion.identity;
    Bake(pl, sb, "当前姿势");
    var vis = pl.transform.Find("Visual");
    if (vis != null)
    {
        sb.AppendLine("  Visual.localPosition = " + vis.localPosition.ToString("F4") + "  localScale = " + vis.localScale.ToString("F4"));
        Bake(vis.gameObject, sb, "仅 Visual 子树");
        // 把 Visual 归零后再量一次，看模型自身在原始位置的脚底高度
        var keep = vis.localPosition;
        vis.localPosition = Vector3.zero;
        Bake(vis.gameObject, sb, "Visual 归零后");
        vis.localPosition = keep;
    }
    var cc = pl.GetComponent<CharacterController>();
    if (cc != null)
        sb.AppendLine(string.Format("  CharacterController: height={0:F4} radius={1:F4} center={2:F4} skinWidth={3:F4}  => 胶囊顶 y={4:F4} 底 y={5:F4}",
            cc.height, cc.radius, cc.center.y, cc.skinWidth, cc.center.y + cc.height * 0.5f, cc.center.y - cc.height * 0.5f));
    else sb.AppendLine("  CharacterController = 无");
    Object.DestroyImmediate(pl);

    Debug.Log(sb.ToString());
    yield return null;
}

void Bake(GameObject go, System.Text.StringBuilder sb, string tag)
{
    var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    float mn = float.MaxValue, mx = float.MinValue, mnx = float.MaxValue, mxx = float.MinValue, mnz = float.MaxValue, mxz = float.MinValue;
    int n = 0;
    var m = new Mesh();
    foreach (var s in smrs)
    {
        if (s.sharedMesh == null) continue;
        s.BakeMesh(m, true);
        var l2w = s.transform.localToWorldMatrix;
        var verts = m.vertices;
        for (int i = 0; i < verts.Length; i++)
        {
            var w = l2w.MultiplyPoint3x4(verts[i]);
            if (w.y < mn) mn = w.y; if (w.y > mx) mx = w.y;
            if (w.x < mnx) mnx = w.x; if (w.x > mxx) mxx = w.x;
            if (w.z < mnz) mnz = w.z; if (w.z > mxz) mxz = w.z;
        }
        n++;
    }
    Object.DestroyImmediate(m);
    if (n == 0) { sb.AppendLine("  [" + tag + "] 无蒙皮网格"); return; }
    sb.AppendLine(string.Format("  [{0}] 蒙皮网格 {1} 个：身高 {2:F4}  (y {3:F4}..{4:F4})   宽 {5:F4} (x {6:F4}..{7:F4})   厚 {8:F4} (z {9:F4}..{10:F4})",
        tag, n, mx - mn, mn, mx, mxx - mnx, mnx, mxx, mxz - mnz, mnz, mxz));
    // 肩宽：只看躯干附近的水平跨度粗略估
}

return Body();
