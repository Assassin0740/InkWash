// 足部几何探针：量出「踝关节(Fooot) → 脚底」与「脚趾关节(Toes) → 脚底」的真实距离，
// 供 FootIK 的 footHeight / toeHeight 取值使用。
// 原理：Idle 是已知贴地姿势（姿态体检实测网格最低点 ≈ +0.004m），
//       在该姿势下 (关节Y - 网格最低Y) 就是关节到脚底的净距离。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player，先进入 Play";
var anim = player.GetComponentInChildren<Animator>(true);
if (anim == null) return "[ERR] 无 Animator";
if (!anim.isHuman) return "[ERR] Animator 不是 Humanoid";
sb.AppendLine("humanoid=True  rootMotion=" + anim.applyRootMotion + "  updateMode=" + anim.updateMode);

// ---- 地面 ----
float groundY = player.transform.position.y;
var hits = Physics.RaycastAll(player.transform.position + Vector3.up * 3f, Vector3.down, 10f);
float best = float.MinValue;
foreach (var h in hits)
{
    if (h.collider.transform.IsChildOf(player.transform)) continue;
    if (h.point.y > best) best = h.point.y;
}
if (best > float.MinValue) groundY = best;
sb.AppendLine("地面 Y = " + groundY.ToString("F4"));

var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
var bakeMesh = new UnityEngine.Mesh();

// 返回当前姿势下网格最低顶点的世界 Y
System.Func<float> lowestMeshY = () =>
{
    float lo = float.MaxValue;
    for (int k = 0; k < smrs.Length; k++)
    {
        // 逐顶点比 AABB 更准（AABB 的 min.y 已经是最低顶点，等价；这里直接读 vertices 更直观）
        smrs[k].BakeMesh(bakeMesh);
        var verts = bakeMesh.vertices;
        var m = smrs[k].transform.localToWorldMatrix;
        for (int i = 0; i < verts.Length; i++)
        {
            float y = m.MultiplyPoint3x4(verts[i]).y;
            if (y < lo) lo = y;
        }
    }
    return lo;
};

sb.AppendLine();
sb.AppendLine("-- Idle（已知贴地）几何 --");
anim.Play(Animator.StringToHash("Idle"), 0, 0f);
anim.Update(1f / 60f);
anim.Update(1f / 60f);   // 两帧，避开状态切换那一帧的过渡

float lowIdle = lowestMeshY();
float soleY = lowIdle;
Vector3 lf = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
Vector3 rf = anim.GetIKPosition(AvatarIKGoal.RightFoot);
var ltoe = anim.GetBoneTransform(HumanBodyBones.LeftToes);
var rtoe = anim.GetBoneTransform(HumanBodyBones.RightToes);
var lheel = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
var rheel = anim.GetBoneTransform(HumanBodyBones.RightFoot);

sb.AppendLine("  网格最低 Y = " + lowIdle.ToString("F4") + "   (相对地面 " + (lowIdle - groundY).ToString("F4") + ")");
sb.AppendLine("  L踝(IK) Y=" + lf.y.ToString("F4") + "  → 踝到脚底 = " + (lf.y - soleY).ToString("F4"));
sb.AppendLine("  R踝(IK) Y=" + rf.y.ToString("F4") + "  → 踝到脚底 = " + (rf.y - soleY).ToString("F4"));
if (ltoe != null) sb.AppendLine("  L趾(bone) Y=" + ltoe.position.y.ToString("F4") + "  → 趾到脚底 = " + (ltoe.position.y - soleY).ToString("F4"));
if (rtoe != null) sb.AppendLine("  R趾(bone) Y=" + rtoe.position.y.ToString("F4") + "  → 趾到脚底 = " + (rtoe.position.y - soleY).ToString("F4"));
if (lheel != null) sb.AppendLine("  L脚骨骼 Y=" + lheel.position.y.ToString("F4") + "  → 到脚底 = " + (lheel.position.y - soleY).ToString("F4"));
if (rheel != null) sb.AppendLine("  R脚骨骼 Y=" + rheel.position.y.ToString("F4") + "  → 到脚底 = " + (rheel.position.y - soleY).ToString("F4"));

// ---- 各状态下「踝相对脚底」的关系（看踝点探测是否够用）----
sb.AppendLine();
sb.AppendLine("-- 各状态：网格最低点 / 最低点的踝高 / 脚趾高 --");
sb.AppendLine("状态         网格最低(rel地面)   踝高最小   趾高最小   (踝高=踝Y-网格最低Y)");
string[] states = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
foreach (var name in states)
{
    int hash = Animator.StringToHash(name);
    if (!anim.HasState(0, hash)) { sb.AppendLine(string.Format("{0,-11} (无)", name)); continue; }

    float deepest = float.MaxValue; float deepestAt = 0f;
    float minAnkleGap = float.MaxValue; float minToeGap = float.MaxValue;
    int steps = 24;
    for (int i = 0; i <= steps; i++)
    {
        float nt = i / (float)steps;
        anim.Play(hash, 0, nt);
        anim.Update(1f / 60f);
        float lo = lowestMeshY();
        float la = anim.GetIKPosition(AvatarIKGoal.LeftFoot).y;
        float ra = anim.GetIKPosition(AvatarIKGoal.RightFoot).y;
        float ankleGap = Mathf.Min(la, ra) - lo;   // 最低那只踝相对网格最低
        minAnkleGap = Mathf.Min(minAnkleGap, ankleGap);
        if (ltoe != null && rtoe != null)
        {
            float tg = Mathf.Min(ltoe.position.y, rtoe.position.y) - lo;
            minToeGap = Mathf.Min(minToeGap, tg);
        }
        if (lo < deepest) { deepest = lo; deepestAt = nt; }
    }
    sb.AppendLine(string.Format("{0,-11} {1,12:F4}(nt={2:F2})  {3,9:F4}  {4,9:F4}",
        name, deepest - groundY, deepestAt, minAnkleGap,
        (minToeGap == float.MaxValue ? 0f : minToeGap)));
}

anim.Play(Animator.StringToHash("Idle"), 0, 0f);
anim.Update(1f / 60f);
UnityEngine.Object.Destroy(bakeMesh);
return sb.ToString();
