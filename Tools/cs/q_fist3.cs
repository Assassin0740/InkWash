// q_fist3.cs —— 诊断「左手卷曲为什么无效」
//
//   怀疑点：Curl() 用 spreadDir = pinky − index 当铰链轴，这个轴在左右手上是**镜像**的，
//   沿同一个正角度旋转，在一侧是"屈"，在另一侧是"伸"（相当于反向掰）。
//
//   做法：关掉 WeaponHandPose，自己按同样的公式对左右手分别试 +θ / −θ，
//   打印 (近节夹角, 中指指尖到腕)。谁能缩短指尖到腕距离，谁就是正确的符号。
//   同时把两手的链名/轴向量打出来，看镜像关系。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null;
    yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_fist3] no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponent<InkWash.Player.CombatStance>();
    var pose = go.GetComponentInChildren<InkWash.Effects.WeaponHandPose>(true);
    var ik = go.GetComponent<InkWash.Player.FootIK>();

    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.RequestInjectedAttack();
    for (int i = 0; i < 90 && !stance.InCombat; i++) yield return null;
    ctl.EndInputOverride();
    ctl.enabled = false;
    if (ik != null) ik.enabled = false;
    if (pose != null) pose.enabled = false;      // 自己算，别跟组件抢

    sb.AppendLine("InCombat=" + stance.InCombat + "  武器挂点=" + stance.WeaponMountPath);
    sb.AppendLine("组件已临时关闭（pose.enabled=false），下面全部由本脚本自己算。");
    sb.AppendLine();

    var rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
    var lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);

    string[] keys = { "Index", "Mid", "Ring", "Pinky" };
    string[] sideTag = { "R_", "L_" };
    Transform[] hands = { rHand, lHand };
    string[] handName = { "右手（持剑）", "左手（空手）" };

    for (int h = 0; h < 2; h++)
    {
        var hand = hands[h];
        if (hand == null) { sb.AppendLine("hand=null"); continue; }

        sb.AppendLine("==================== " + handName[h] + " (" + hand.name + ") ====================");

        // ---- 收链 ----
        var roots = new List<Transform>();
        var chain = new List<List<Transform>>();
        var all = hand.GetComponentsInChildren<Transform>(true);
        foreach (var k in keys)
        {
            Transform root = null;
            foreach (var t in all)
            {
                if (!t.name.Contains(k)) continue;
                if (t.name.Contains("Toe")) continue;
                if (!t.IsChildOf(hand)) continue;
                if (t.name.EndsWith("1")) { root = t; break; }
            }
            var c = new List<Transform>();
            Transform cur = root;
            for (int i = 0; i < 3 && cur != null; i++) { c.Add(cur); cur = cur.childCount > 0 ? cur.GetChild(0) : null; }
            chain.Add(c);
            if (root != null) roots.Add(root);
        }
        string firstChain = chain[0].Count > 0 ? string.Join(" > ", chain[0].ConvertAll(t => t.name).ToArray()) : "<空>";
        sb.AppendLine("  链: " + firstChain + "   （共 " + roots.Count + " 条）");

        Transform index = null, pinky = null;
        for (int i = 0; i < chain.Count; i++)
        {
            if (chain[i].Count < 3) continue;
            var n = chain[i][0].name;
            if (n.Contains("Index")) index = chain[i][0];
            if (n.Contains("Pinky")) pinky = chain[i][0];
        }
        if (index == null || pinky == null) { sb.AppendLine("  ！食指/小指链不完整，跳过"); sb.AppendLine(); continue; }

        Vector3 fingerDir = hand.TransformDirection(Vector3.up).normalized;
        Vector3 spreadDir = (pinky.position - index.position).normalized;
        Vector3 palmN = Vector3.Cross(fingerDir, spreadDir).normalized;
        sb.AppendLine(string.Format("  hand.up(世界)={0}   spreadDir(小指−食指)={1}   palmN={2}",
            fingerDir.ToString("F3"), spreadDir.ToString("F3"), palmN.ToString("F3")));
        sb.AppendLine(string.Format("  指纹: 食指根={0}  小指根={1}  食指到小指距离={2:F3}m",
            index.position.ToString("F3"), pinky.position.ToString("F3"),
            (pinky.position - index.position).magnitude));

        // ---- 测量函数 ----
        var measure = new System.Func<float[]>(() =>
        {
            float s1 = 0f; int n = 0; float midTip = 0f;
            for (int i = 0; i < chain.Count; i++)
            {
                var c = chain[i];
                if (c.Count < 3) continue;
                float j1 = Vector3.Angle(c[1].position - c[0].position, c[2].position - c[1].position);
                s1 += j1; n++;
                if (c[0].name.Contains("Mid"))
                    midTip = (c[2].position - hand.position).magnitude;
            }
            return new float[] { s1 / Mathf.Max(1, n), midTip };
        });

        // ---- 记录原始局部旋转，便于反复试 ----
        var saved = new List<Quaternion>();
        var savedT = new List<Transform>();
        foreach (var c in chain) foreach (var t in c) { savedT.Add(t); saved.Add(t.localRotation); }

        var m0 = measure();
        sb.AppendLine(string.Format("  [基线·不卷]            近节={0,6:F1}°   中指指尖到腕={1:F3}m", m0[0], m0[1]));

        // 三角候选：+θ 沿 spreadDir / −θ 沿 spreadDir / +θ 沿 palmN
        string[] trialName = { " +70° 绕 spreadDir ", " −70° 绕 spreadDir ", " +70° 绕 palmN(normal)" };
        Vector3[] trialAxis = { spreadDir, -spreadDir, palmN };
        float[] trialDeg = { 70f, 70f, 70f };

        for (int ti = 0; ti < 3; ti++)
        {
            for (int i = 0; i < savedT.Count; i++) savedT[i].localRotation = saved[i];

            // 一次取轴（沿用组件的"卷之前统一取轴"约定）
            var axes = new Vector3[savedT.Count];
            for (int i = 0; i < savedT.Count; i++)
                axes[i] = savedT[i].InverseTransformDirection(trialAxis[ti]);

            for (int i = 0; i < savedT.Count; i++)
                savedT[i].localRotation = savedT[i].localRotation * Quaternion.AngleAxis(trialDeg[ti], axes[i]);

            var m = measure();
            sb.AppendLine(string.Format("  [{0}] 近节={1,6:F1}°   中指指尖到腕={2:F3}m   Δ指尖={3:+0.000;-0.000}m",
                trialName[ti], m[0], m[1], m[1] - m0[1]));

            for (int i = 0; i < savedT.Count; i++) savedT[i].localRotation = saved[i];
        }

        // 单位轴自检：局部轴与"骨骼自身指向子骨"的方向应接近垂直，否则转它等于拧麻花
        sb.AppendLine("  -- 局部轴正交性自检（|cos| 应接近 0；接近 1 = 在拧麻花，屈不起来）--");
        for (int i = 0; i < roots.Count; i++)
        {
            var c = chain[i];
            if (c.Count < 2) continue;
            Vector3 boneDir = (c[1].position - c[0].position).normalized;
            Vector3 axisAlongSpread = spreadDir;
            float cos = Mathf.Abs(Vector3.Dot(boneDir, axisAlongSpread));
            sb.AppendLine(string.Format("     {0,-22} |cos(骨向, spreadDir)|={1:F3}", c[0].name, cos));
        }
        sb.AppendLine();
    }

    // 还原
    if (pose != null) pose.enabled = true;
    if (ik != null) ik.enabled = true;
    if (ctl != null) ctl.enabled = true;

    sb.AppendLine(">>> 诊断完毕；组件已还原启用。");
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_fist3.txt"), sb.ToString());
    Debug.Log("[q_fist3] done");
    yield return null;
}

return Body();
