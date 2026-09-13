// 逐渲染器对比两种取最低点的算法：
//   (a) 姿态体检用的 bake.bounds 八个角 → localToWorldMatrix
//   (b) 逐顶点 bake.vertices → localToWorldMatrix
// 目的是找出为什么两者能差 0.11m。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player";
var anim = player.GetComponentInChildren<Animator>(true);

var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
var bake = new UnityEngine.Mesh();

anim.Play(Animator.StringToHash("Idle"), 0, 0f);
anim.Update(1f / 60f);
anim.Update(1f / 60f);

sb.AppendLine("Idle 状态，逐 SkinnedMeshRenderer：");
sb.AppendLine(string.Format("{0,-28} {1,10} {2,12} {3,12} {4,10}",
    "renderer", "顶点数", "AABB角最低Y", "逐顶点最低Y", "bounds.min.y"));
float globalCorner = float.MaxValue, globalVert = float.MaxValue;
foreach (var smr in smrs)
{
    smr.BakeMesh(bake);
    var m = smr.transform.localToWorldMatrix;
    var b = bake.bounds;

    float corLo = float.MaxValue;
    for (int cx = 0; cx < 2; cx++)
    for (int cy = 0; cy < 2; cy++)
    for (int cz = 0; cz < 2; cz++)
    {
        var c = new Vector3(cx == 0 ? b.min.x : b.max.x, cy == 0 ? b.min.y : b.max.y, cz == 0 ? b.min.z : b.max.z);
        corLo = Mathf.Min(corLo, m.MultiplyPoint3x4(c).y);
    }

    var vs = bake.vertices;
    float vLo = float.MaxValue;
    for (int i = 0; i < vs.Length; i++) vLo = Mathf.Min(vLo, m.MultiplyPoint3x4(vs[i]).y);

    globalCorner = Mathf.Min(globalCorner, corLo);
    globalVert = Mathf.Min(globalVert, vLo);

    sb.AppendLine(string.Format("{0,-28} {1,10} {2,12:F4} {3,12:F4} {4,10:F4}",
        smr.name, vs.Length, corLo, vLo, b.min.y));
}
sb.AppendLine();
sb.AppendLine("全局：AABB角最低 " + globalCorner.ToString("F4") + "   逐顶点最低 " + globalVert.ToString("F4")
    + "   差 " + (globalVert - globalCorner).ToString("F4"));
sb.AppendLine("Arena 顶面 = 0.3000   Ground 顶面 = 0.0000   Player.root = "
    + player.transform.position.y.ToString("F4"));

UnityEngine.Object.Destroy(bake);
return sb.ToString();
