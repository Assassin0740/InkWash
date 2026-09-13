// 查「躯干倾角」为什么恒为 0.000°。
// 断言是「> -8°」，0 会无条件通过 —— 若测量本身失效，这就是个假通过。
using System.Collections;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { Debug.Log("[lean] no ctl"); yield break; }
    var anim = ctl.animator;
    var go = ctl.gameObject;

    sb.AppendLine("isHuman = " + anim.isHuman);
    string[] bones = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
                       "LeftUpperArm", "RightUpperArm", "LeftFoot", "RightFoot" };
    foreach (var b in bones)
    {
        var hb = (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), b);
        var t = anim.GetBoneTransform(hb);
        sb.AppendLine(string.Format("  {0,-14} -> {1}", b, t == null ? "(null)" : t.name));
    }

    // 逐状态量一遍 chest - hips 的朝向
    string[] states = { "Idle", "Walk", "Run", "Atk1" };
    foreach (var nm in states)
    {
        anim.Play(nm, 0, 0f);
        yield return null;
        anim.Play(nm, 0, 0.4f);
        anim.Update(1f / 60f);
        var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Neck);
        if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Head);
        sb.AppendLine(string.Format("  降级后 upper = {0}", chest == null ? "(null)" : chest.name));
        if (hips == null || chest == null) { sb.AppendLine("[" + nm + "] 骨骼缺失"); continue; }
        Vector3 d = chest.position - hips.position;
        Vector3 fwd = go.transform.forward; fwd.y = 0f; fwd.Normalize();
        float along = Vector3.Dot(d, fwd);
        sb.AppendLine(string.Format("[{0,-6}] hips={1} chest={2}  deltaY={3:F4} deltaFwd={4:F5}  倾角={5:F2}°",
            nm, hips.name, chest.name, d.y, along, Mathf.Atan2(along, Mathf.Max(Mathf.Abs(d.y), 1e-4f)) * Mathf.Rad2Deg));
        yield return null;
    }
    Debug.Log("[lean] " + sb.ToString());
}
return Body();
