// 运行时探针 v3：用 SkinnedMeshRenderer.BakeMesh() 取「当前姿势下网格的最低顶点」，
// 这是判断脚是否穿地/悬空的唯一可靠量（骨骼关节点不等于脚底）。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player，先进入 Play";
var anim = player.GetComponentInChildren<Animator>(true);
if (anim == null) return "[ERR] 无 Animator";

sb.AppendLine("=== 层级与变换 ===");
sb.AppendLine("Animator: go=" + anim.gameObject.name + "  rootMotion=" + anim.applyRootMotion
    + "  updateMode=" + anim.updateMode + "  culling=" + anim.cullingMode
    + "  humanoid=" + anim.isHuman);
Transform t = player.transform;
int guard = 0;
while (t != null && guard++ < 8)
{
    sb.AppendLine("  [" + t.name + "] localPos=" + t.localPosition.ToString("F3")
        + " localScale=" + t.localScale.ToString("F3")
        + " comps=" + string.Join(",", System.Array.ConvertAll(t.GetComponents<Component>(), c => c.GetType().Name)));
    if (t.childCount == 0) break;
    t = t.GetChild(0);
}

// ---- 地面 ----
float groundY = float.NaN;
var hits = Physics.RaycastAll(player.transform.position + Vector3.up * 3f, Vector3.down, 10f);
float best = float.MinValue;
foreach (var h in hits)
{
    if (h.collider.transform.IsChildOf(player.transform)) continue;
    if (h.point.y > best) best = h.point.y;
}
if (best > float.MinValue) groundY = best;
sb.AppendLine("地面 Y = " + (float.IsNaN(groundY) ? "(未命中)" : groundY.ToString("F3")));

var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
sb.AppendLine("SkinnedMeshRenderer: " + string.Join(", ", System.Array.ConvertAll(smrs, r => r.name)));

// ---- 逐状态烘焙取最低顶点 ----
int[] layerIdx = new int[anim.layerCount];
for (int i = 1; i < anim.layerCount; i++) anim.SetLayerWeight(i, 0f);

var bakeMesh = new UnityEngine.Mesh();
string[] states = { "Idle", "Move", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };

sb.AppendLine();
sb.AppendLine("状态        网格最低点(相对地面)  最高时刻最低  最深时刻最低   全片最低出现在");
sb.AppendLine("----------------------------------------------------------------------------------");

foreach (var name in states)
{
    int hash = Animator.StringToHash(name);
    if (!anim.HasState(0, hash)) { sb.AppendLine(string.Format("{0,-11} (无此状态)", name)); continue; }

    float deepest = float.MaxValue, shallowest = float.MinValue, deepestAt = 0f, shallowestAt = 0f;
    int steps = 24;
    for (int i = 0; i <= steps; i++)
    {
        float nt = i / (float)steps;
        anim.Play(hash, 0, nt);
        anim.Update(0f);

        float g = float.IsNaN(groundY) ? player.transform.position.y : groundY;
        float lo = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            // 注意：BakeMesh 产出的是**渲染器局部空间**的顶点，必须自己变换到世界空间。
            // 直接拿 bakeMesh.bounds.min.y 当世界坐标会得到"整个人在地下一米"的假数据。
            smrs[k].BakeMesh(bakeMesh);
            var lb = bakeMesh.bounds;
            var m = smrs[k].transform.localToWorldMatrix;
            Vector3 c0 = lb.min, c1 = lb.max;
            for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
            for (int cz = 0; cz < 2; cz++)
            {
                Vector3 corner = new Vector3(cx == 0 ? c0.x : c1.x, cy == 0 ? c0.y : c1.y, cz == 0 ? c0.z : c1.z);
                lo = Mathf.Min(lo, m.MultiplyPoint3x4(corner).y - g);
            }
        }
        if (lo < deepest) { deepest = lo; deepestAt = nt; }
        if (lo > shallowest) { shallowest = lo; shallowestAt = nt; }
    }
    sb.AppendLine(string.Format("{0,-11} {1,14:F3}   {2,10:F3}(nt={3:F2})   {4,8:F3}(nt={5:F2})",
        name, deepest, shallowest, shallowestAt, deepest, deepestAt));
}

anim.Play(Animator.StringToHash("Idle"), 0, 0f);
anim.Update(0f);
for (int i = 1; i < anim.layerCount; i++) anim.SetLayerWeight(i, 1f);
UnityEngine.Object.Destroy(bakeMesh);

sb.AppendLine();
sb.AppendLine("判读：≈0 = 脚底正好贴地；> +0.05 = 悬空；< -0.02 = 穿地（值即穿地深度 m）。");
return sb.ToString();
