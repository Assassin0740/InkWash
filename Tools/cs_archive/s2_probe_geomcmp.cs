// 对照探针：查清「姿态体检」与「几何探针」两套测量为何给出不同的绝对高度。
// 关键：同时打印 角色 transform.Y / 地面Y / 网格最低绝对Y / FootIK 内部量，
// 并分别在 PlayerController 启用(dbg A)与禁用(dbg B, 与体检一致)下各测一遍。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player，先进入 Play";
var anim = player.GetComponentInChildren<Animator>(true);
if (anim == null) return "[ERR] 无 Animator";
var ctl = player.GetComponent<InkWash.Player.PlayerController>();
var footIk = player.GetComponent<InkWash.Player.FootIK>();

var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
var bake = new UnityEngine.Mesh();

System.Func<Vector3, Transform, float> groundUnder = (worldPos, self) =>
{
    var hs = Physics.RaycastAll(worldPos + Vector3.up * 3f, Vector3.down, 10f);
    float b = float.MinValue;
    for (int i = 0; i < hs.Length; i++)
    {
        var tr = hs[i].collider.transform;
        if (self != null && (tr == self || tr.IsChildOf(self))) continue;
        if (hs[i].point.y > b) b = hs[i].point.y;
    }
    return b > float.MinValue ? b : worldPos.y;
};

System.Func<float> meshMinY = () =>
{
    float lo = float.MaxValue;
    for (int k = 0; k < smrs.Length; k++)
    {
        smrs[k].BakeMesh(bake);
        var vs = bake.vertices;
        var m = smrs[k].transform.localToWorldMatrix;
        for (int i = 0; i < vs.Length; i++)
        {
            float y = m.MultiplyPoint3x4(vs[i]).y;
            if (y < lo) lo = y;
        }
    }
    return lo;
};

System.Action<string> report = (tag) =>
{
    anim.Play(Animator.StringToHash("Idle"), 0, 0f);
    anim.Update(1f / 60f);
    anim.Update(1f / 60f);

    Vector3 p = player.transform.position;
    float g = groundUnder(p, player.transform);
    float mm = meshMinY();
    sb.AppendLine("[" + tag + "]");
    sb.AppendLine("  transform.position = " + p.ToString("F4"));
    sb.AppendLine("  地面 Y = " + g.ToString("F4") + "   网格最低 Y = " + mm.ToString("F4")
        + "   相对地面 = " + (mm - g).ToString("F4"));
    sb.AppendLine("  Animator: layers=" + anim.layerCount + "  posture=" + anim.GetCurrentAnimatorStateInfo(0).fullPathHash);
    if (footIk != null)
        sb.AppendLine("  FootIK: BodyLift=" + footIk.BodyLift.ToString("F4")
            + "  RawPen=" + footIk.RawPenetration.ToString("F4")
            + "  HasGround=" + footIk.HasGroundInfo + "  groundedFeet=" + footIk.GroundedFootCount
            + "  footHeight=" + footIk.footHeight + "  liftSmooth=" + footIk.liftSmooth);
    else sb.AppendLine("  (无 FootIK)");
    if (ctl != null)
        sb.AppendLine("  PlayerController: enabled=" + ctl.enabled + "  speed=" + ctl.CurrentSpeed.ToString("F3"));
};

if (ctl != null && ctl.enabled) report("A ctl=on");

// 与姿态体检一致：关掉 PlayerController
bool wasOn = ctl != null && ctl.enabled;
if (ctl != null) ctl.enabled = false;
for (int i = 0; i < 4; i++) { }
report("B ctl=off");
if (ctl != null) ctl.enabled = wasOn;

UnityEngine.Object.Destroy(bake);
return sb.ToString();
