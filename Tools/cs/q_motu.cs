// 只读探针：dump Enemy_MoTu.prefab 的完整节点树 + 每个渲染器的网格/材质
// 目的：查清「手里的斧子 + 另一根棒子」分别来自哪个节点、挂在哪、会不会跟随动画
var sb = new System.Text.StringBuilder();

string P = "Assets/_Project/Prefabs/Enemies/Enemy_MoTu.prefab";
var root = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(P);
sb.AppendLine("prefab = " + (root == null ? "NULL" : root.name));
sb.AppendLine();

int total = 0;
int nSMR = 0, nMR = 0;

void Dump(UnityEngine.Transform t, int d)
{
    total++;
    var go = t.gameObject;
    var line = new System.Text.StringBuilder();
    line.Append(new string(' ', d * 2)).Append(t.name);

    var mr = go.GetComponent<UnityEngine.MeshRenderer>();
    if (mr != null)
    {
        nMR++;
        var mf = go.GetComponent<UnityEngine.MeshFilter>();
        int vc = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.vertexCount : -1;
        string ms = "";
        var mats = mr.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) ms += (mats[i] == null ? "null" : mats[i].name) + " ";
        line.Append("   [MR v=" + vc + " mat=" + ms + "]");
    }

    var smr = go.GetComponent<UnityEngine.SkinnedMeshRenderer>();
    if (smr != null)
    {
        nSMR++;
        int vc2 = smr.sharedMesh != null ? smr.sharedMesh.vertexCount : -1;
        string ms2 = "";
        var mats2 = smr.sharedMaterials;
        for (int i = 0; i < mats2.Length; i++) ms2 += (mats2[i] == null ? "null" : mats2[i].name) + " ";
        line.Append("   [SMR v=" + vc2
            + " root=" + (smr.rootBone == null ? "-" : smr.rootBone.name)
            + " nBones=" + smr.bones.Length
            + " mat=" + ms2 + "]");
    }

    line.Append("   lp=" + t.localPosition.ToString("F3"));
    sb.AppendLine(line.ToString());

    for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), d + 1);
}

if (root != null) Dump(root.transform, 0);

sb.AppendLine();
sb.AppendLine("节点总数 = " + total + "   MeshRenderer = " + nMR + "   SkinnedMeshRenderer = " + nSMR);

System.IO.File.WriteAllText("Tools/reports/q_motu.txt", sb.ToString());
return "q_motu done: nodes=" + total + " MR=" + nMR + " SMR=" + nSMR;
