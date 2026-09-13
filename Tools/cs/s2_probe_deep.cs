// 定位"穿地最深的那一刻"到底是哪个身体部位在穿地。
// 做法：取当前姿势网格最低的那个顶点（世界坐标），再在 Humanoid 骨骼里找离它最近的骨头。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player";
var anim = player.GetComponentInChildren<Animator>(true);
if (anim == null || !anim.isHuman) return "[ERR] 无 Humanoid Animator";

var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
var bake = new UnityEngine.Mesh();

var bones = new HumanBodyBones[]
{
    HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
    HumanBodyBones.Neck, HumanBodyBones.Head,
    HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
    HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
    HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
    HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
};

System.Func<Vector3, Transform> nearestBone = (p) =>
{
    Transform best = null; float bd = float.MaxValue;
    foreach (var b in bones)
    {
        var t = anim.GetBoneTransform(b);
        if (t == null) continue;
        float d = (t.position - p).sqrMagnitude;
        if (d < bd) { bd = d; best = t; }
    }
    return best;
};

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
sb.AppendLine("地面 Y = " + groundY.ToString("F4") + "   Player.root Y = " + player.transform.position.y.ToString("F4"));
sb.AppendLine();

string[] states = { "Atk3", "Atk2Rec", "Atk1Rec" };
float[] nts = { 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.35f };

foreach (var s in states)
{
    int hash = Animator.StringToHash(s);
    if (!anim.HasState(0, hash)) continue;
    sb.AppendLine("== " + s + " ==");
    foreach (var nt in nts)
    {
        anim.Play(hash, 0, nt);
        anim.Update(1f / 60f);

        Vector3 lowest = Vector3.zero; float lo = float.MaxValue;
        for (int k = 0; k < smrs.Length; k++)
        {
            smrs[k].BakeMesh(bake);
            var vs = bake.vertices;
            var m = smrs[k].transform.localToWorldMatrix;
            for (int i = 0; i < vs.Length; i++)
            {
                var w = m.MultiplyPoint3x4(vs[i]);
                if (w.y < lo) { lo = w.y; lowest = w; }
            }
        }

        var nb = nearestBone(lowest);
        var lf = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
        var rf = anim.GetIKPosition(AvatarIKGoal.RightFoot);
        var lt = anim.GetBoneTransform(HumanBodyBones.LeftToes);
        var rt = anim.GetBoneTransform(HumanBodyBones.RightToes);

        sb.AppendLine(string.Format(
            "  nt={0:F2}  网格最低 {1,7:F4}(rel {2,7:F4})  最近骨骼={3,-16}  踝IK L/R={4:F3}/{5:F3}  趾 L/R={6}/{7}",
            nt, lo, lo - groundY, nb != null ? nb.name : "?", lf.y, rf.y,
            lt != null ? lt.position.y.ToString("F3") : "-",
            rt != null ? rt.position.y.ToString("F3") : "-"));
    }
    sb.AppendLine();
}

anim.Play(Animator.StringToHash("Idle"), 0, 0f);
anim.Update(1f / 60f);
UnityEngine.Object.Destroy(bake);
return sb.ToString();
