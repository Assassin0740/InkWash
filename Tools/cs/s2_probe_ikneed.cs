// 直查：Atk3 各归一化时刻，FootIK 实际算出的 need / BodyLift / 基准偏移是多少。
var sb = new System.Text.StringBuilder();
var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player";
var anim = player.GetComponentInChildren<Animator>(true);
var ik = player.GetComponent<InkWash.Player.FootIK>();
if (ik == null) return "[ERR] 无 FootIK";
sb.AppendLine("FootIK: enableIk=" + ik.enableIk + " soleClearance=" + ik.soleClearance
    + " maxLift=" + ik.maxLift + " liftSmooth=" + ik.liftSmooth
    + " noFix=" + (ik.noGroundFixStates != null ? string.Join(",", ik.noGroundFixStates) : "-"));
if (ik.stateOffsets != null)
    foreach (var o in ik.stateOffsets) sb.AppendLine("  offset " + o.state + " = " + o.y.ToString("F3"));

var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
var bake = new UnityEngine.Mesh();

float groundY = player.transform.position.y;
var hs = Physics.RaycastAll(player.transform.position + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
float bb = float.MinValue;
foreach (var h in hs)
{
    var tr = h.collider.transform;
    if (tr == player.transform || tr.IsChildOf(player.transform)) continue;
    if (h.point.y > bb) bb = h.point.y;
}
if (bb > float.MinValue) groundY = bb;
sb.AppendLine("地面 Y = " + groundY.ToString("F4"));
sb.AppendLine();

System.Func<float> meshMin = () =>
{
    float lo = float.MaxValue;
    for (int k = 0; k < smrs.Length; k++)
    {
        smrs[k].BakeMesh(bake);
        var vs = bake.vertices;
        var m = smrs[k].transform.localToWorldMatrix;
        for (int i = 0; i < vs.Length; i++) { float y = m.MultiplyPoint3x4(vs[i]).y; if (y < lo) lo = y; }
    }
    return lo;
};

float saved = ik.liftSmooth;
ik.liftSmooth = 1e6f;

int hash = Animator.StringToHash("Atk3");
sb.AppendLine("Atk3 逐时刻：");
sb.AppendLine("  nt      原本网格最低    BodyBase    RawPen    BodyLift   修正后网格最低(rel地面)  当前状态");
for (int i = 0; i <= 20; i++)
{
    float nt = i / 20f;
    anim.Play(hash, 0, nt);
    anim.Update(1f / 60f);

    // 先记录"贴地后"的实际值
    float after = meshMin() - groundY;

    // 再看 IK 内部：把 enableIk 关掉重算一遍"无 IK"值，再打开
    ik.enableIk = false;
    anim.Play(hash, 0, nt);
    anim.Update(1f / 60f);
    float before = meshMin() - groundY;
    ik.enableIk = true;
    anim.Play(hash, 0, nt);
    anim.Update(1f / 60f);

    sb.AppendLine(string.Format("  {0:F2}   {1,9:F4}      {2,9:F4}  {3,8:F4}  {4,8:F4}     {5,9:F4}            {6}",
        nt, before, ik.BodyBase, ik.RawPenetration, ik.BodyLift, after, ik.CurrentState));
}

ik.liftSmooth = saved;
anim.Play(Animator.StringToHash("Idle"), 0, 0f);
anim.Update(1f / 60f);
UnityEngine.Object.Destroy(bake);
return sb.ToString();
