// 只读探针：检查 Z_Orc.controller 的动画剪辑是否驱动 Axe_1_Dummy_089
//           + dump 绑定姿态下斧子挂点与手骨的位置关系
var sb = new System.Text.StringBuilder();

// ── ① controller 与剪辑曲线 ──
string cp = UnityEditor.AssetDatabase.GUIDToAssetPath("9d856e6eb1a35444d90954cf52ecfe77");
sb.AppendLine("controller = " + cp);
var ac = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(cp);
if (ac == null) { sb.AppendLine("× 加载失败"); }
else
{
    var clips = ac.animationClips;
    sb.AppendLine("剪辑数 = " + clips.Length);
    sb.AppendLine();
    foreach (var c in clips)
    {
        var binds = UnityEditor.AnimationUtility.GetCurveBindings(c);
        var obinds = UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(c);
        int nAxe = 0;
        var axePaths = new System.Collections.Generic.List<string>();
        var samplePaths = new System.Collections.Generic.List<string>();
        foreach (var b in binds)
        {
            if (b.path.IndexOf("Axe", System.StringComparison.OrdinalIgnoreCase) >= 0)
            { nAxe++; if (axePaths.Count < 8) axePaths.Add(b.path + " :: " + b.propertyName); }
            if (samplePaths.Count < 4) samplePaths.Add(b.path + " :: " + b.propertyName);
        }
        sb.AppendLine("── " + c.name + "   时长=" + c.length.ToString("F2") + "s  曲线=" + binds.Length + "  对象曲线=" + obinds.Length + "  含Axe=" + nAxe);
        sb.AppendLine("   样例路径: " + string.Join(" | ", samplePaths.ToArray()));
        if (axePaths.Count > 0)
            sb.AppendLine("   ⚠ Axe 相关: " + string.Join(" | ", axePaths.ToArray()));
    }
    sb.AppendLine();
    // 控制器状态
    sb.AppendLine("层数 = " + ac.layers.Length + "   参数 = " + ac.parameters.Length);
    for (int i = 0; i < ac.layers.Length && i < 3; i++)
    {
        var l = ac.layers[i];
        int nStates = l.stateMachine == null ? 0 : l.stateMachine.states.Length;
        sb.Append("  层[" + i + "] " + l.name + " 状态=" + nStates + " : ");
        if (l.stateMachine != null)
            for (int s = 0; s < l.stateMachine.states.Length && s < 10; s++)
                sb.Append(l.stateMachine.states[s].state.name + " ");
        sb.AppendLine();
    }
}

// ── ② 绑定姿态下斧子挂点 vs 手骨 ──
sb.AppendLine();
sb.AppendLine("===== 绑定姿态位置 =====");
string P = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab";
var root = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(P);
if (root != null)
{
    var inst = UnityEngine.Object.Instantiate(root);
    inst.transform.position = UnityEngine.Vector3.zero;
    inst.transform.rotation = UnityEngine.Quaternion.identity;

    UnityEngine.Transform axe = null, rh = null, lh = null, rj = null, spine = null;
    foreach (var t in inst.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        if (t.name.StartsWith("Axe_1_Dummy")) axe = t;
        else if (t.name.StartsWith("Bip001 R Hand")) rh = t;
        else if (t.name.StartsWith("Bip001 L Hand")) lh = t;
        else if (t.name == "_rootJoint") rj = t;
        else if (t.name.StartsWith("Bip001 Spine_03")) spine = t;
    }
    void Show(string tag, UnityEngine.Transform t)
    {
        if (t == null) { sb.AppendLine("  " + tag + " = 未找到"); return; }
        sb.AppendLine("  " + tag.PadRight(22) + " world=" + t.position.ToString("F3")
            + "  parent=" + (t.parent == null ? "-" : t.parent.name)
            + "  子=" + t.childCount);
    }
    Show("_rootJoint", rj);
    Show("Bip001 Spine_03", spine);
    Show("Bip001 R Hand_017", rh);
    Show("Bip001 L Hand_037", lh);
    Show("Axe_1_Dummy_089", axe);

    if (axe != null && rh != null)
    {
        sb.AppendLine("  |Axe − RHand| = " + UnityEngine.Vector3.Distance(axe.position, rh.position).ToString("F3") + " m");
        sb.AppendLine("  |Axe − LHand| = " + (lh == null ? "-" : UnityEngine.Vector3.Distance(axe.position, lh.position).ToString("F3")) + " m");
        // 斧子的轴向（用它自己的 up/forward 近似）
        sb.AppendLine("  Axe 世界朝向 up=" + axe.up.ToString("F3") + " fwd=" + axe.forward.ToString("F3"));
        if (rh != null)
        {
            // 若挂到右手下，需要用的局部变换
            var inv = UnityEngine.Matrix4x4.TRS(rh.position, rh.rotation, UnityEngine.Vector3.one).inverse
                      * UnityEngine.Matrix4x4.TRS(axe.position, axe.rotation, UnityEngine.Vector3.one);
            var lp = inv.GetColumn(3);
            var lr = UnityEngine.Quaternion.LookRotation(inv.GetColumn(2), inv.GetColumn(1));
            sb.AppendLine("  → 挂到 RHand 下应设 localPosition = " + lp.ToString("F4"));
            sb.AppendLine("  → 挂到 RHand 下应设 localRotation = " + lr.ToString("F4") + "  euler=" + lr.eulerAngles.ToString("F2"));
        }
    }
    UnityEngine.Object.DestroyImmediate(inst);
}

System.IO.File.WriteAllText("Tools/reports/q_gua_clip.txt", sb.ToString());
return "q_gua_clip done";
