// 只读探针：dump Ziyuan 三敌人的节点树 —— 聚焦渲染器与其父链、武器类节点
var sb = new System.Text.StringBuilder();

string[] PATHS = {
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab",
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
    "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
};

string[] KEY = { "weapon", "axe", "sword", "mace", "staff", "blade", "shield", "club", "bow", "spear", "prop", "hand", "wrist", "attach" };

foreach (var P in PATHS)
{
    var root = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(P);
    sb.AppendLine("════════════════════════════════════════════════════");
    sb.AppendLine("prefab = " + (root == null ? "NULL" : root.name) + "   " + P);
    if (root == null) { sb.AppendLine(); continue; }

    int total = 0, nMR = 0, nSMR = 0;
    var rlines = new System.Collections.Generic.List<string>();
    var klines = new System.Collections.Generic.List<string>();

    void Walk(UnityEngine.Transform t, string path)
    {
        total++;
        var go = t.gameObject;
        string full = path + "/" + t.name;

        var mr = go.GetComponent<UnityEngine.MeshRenderer>();
        if (mr != null)
        {
            nMR++;
            var mf = go.GetComponent<UnityEngine.MeshFilter>();
            int vc = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.vertexCount : -1;
            string ms = "";
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) ms += (mats[i] == null ? "null" : mats[i].name) + " ";
            var bb = mr.localBounds;
            rlines.Add("  [MR ] " + full + "   v=" + vc + " mat=" + ms
                + " bounds=" + bb.center.ToString("F2") + "/" + bb.size.ToString("F2"));
        }

        var smr = go.GetComponent<UnityEngine.SkinnedMeshRenderer>();
        if (smr != null)
        {
            nSMR++;
            int vc2 = smr.sharedMesh != null ? smr.sharedMesh.vertexCount : -1;
            string ms2 = "";
            var mats2 = smr.sharedMaterials;
            for (int i = 0; i < mats2.Length; i++) ms2 += (mats2[i] == null ? "null" : mats2[i].name) + " ";
            var lb = smr.localBounds;
            int nNull = 0;
            for (int i = 0; i < smr.bones.Length; i++) if (smr.bones[i] == null) nNull++;
            rlines.Add("  [SMR] " + full + "   v=" + vc2
                + " bones=" + smr.bones.Length + "(null=" + nNull + ")"
                + " root=" + (smr.rootBone == null ? "NULL" : smr.rootBone.name)
                + " mat=" + ms2
                + " lb=" + lb.center.ToString("F2") + "/" + lb.size.ToString("F2"));
        }

        string low = t.name.ToLowerInvariant();
        foreach (var k in KEY)
        {
            if (low.Contains(k))
            {
                klines.Add("  【关键字:" + k + "】" + full + "   lp=" + t.localPosition.ToString("F3")
                    + "  子=" + t.childCount + "  渲染器=" + (go.GetComponent<UnityEngine.Renderer>() != null ? "有" : "无"));
                break;
            }
        }

        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), full);
    }

    Walk(root.transform, "");

    sb.AppendLine("节点总数 = " + total + "   MR = " + nMR + "   SMR = " + nSMR);
    sb.AppendLine();
    sb.AppendLine("── 渲染器（父链已标出）──");
    foreach (var l in rlines) sb.AppendLine(l);
    sb.AppendLine();
    sb.AppendLine("── 关键字命中节点 ──");
    if (klines.Count == 0) sb.AppendLine("  （无）");
    foreach (var l in klines) sb.AppendLine(l);
    sb.AppendLine();
}

System.IO.File.WriteAllText("Tools/reports/q_gua.txt", sb.ToString());
return "q_gua done";
