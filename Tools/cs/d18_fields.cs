using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// d18：核对 prefab 实例上**实际生效**的字段值。
// 判据：aerialLoop 必须 true；strikeLift 必须不是 0（0 会让龙趴地）。
public class d18_probe : MonoBehaviour
{
    void Start()
    {
        Type t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) break; }
        if (t == null) { Debug.Log("d18 ✗ 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (prefab == null) { Debug.Log("d18 ✗ 预制体"); return; }

        var go = Instantiate(prefab);
        go.name = "D18_MoLong";
        var comp = go.GetComponentInChildren(t, true);
        if (comp == null) { Debug.Log("d18 ✗ 组件"); return; }

        var sb = new StringBuilder();
        sb.AppendLine("[D18] ===== prefab 实例上实际生效的字段 =====");
        string[] floats = { "liftSpeed", "strikeLift", "sweepLift", "bodyLift",
                            "hoverHeight", "hoverOrbitRadius", "hoverAmplitudeDeg",
                            "diveTellDuration", "diveDuration", "recoverDuration",
                            "diveMinInterval", "circleMoveSpeed", "diveSpeedMul" };
        foreach (var fn in floats)
        {
            var f = t.GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { sb.AppendLine("[D18] " + fn + " = <字段不存在>"); continue; }
            var v = f.GetValue(comp);
            sb.AppendLine(string.Format("[D18] {0,-20} = {1}", fn, v));
        }
        string[] bools = { "aerialLoop" };
        foreach (var fn in bools)
        {
            var f = t.GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { sb.AppendLine("[D18] " + fn + " = <字段不存在>"); continue; }
            sb.AppendLine(string.Format("[D18] {0,-20} = {1}", fn, f.GetValue(comp)));
        }
        string[] ints = { "spineLinkCount", "biteHeadLinks", "spineLinksFound" };
        foreach (var fn in ints)
        {
            var f = t.GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { sb.AppendLine("[D18] " + fn + " = <字段不存在>"); continue; }
            sb.AppendLine(string.Format("[D18] {0,-20} = {1}", fn, f.GetValue(comp)));
        }
        Debug.Log(sb.ToString());
        Destroy(go);
    }
}

var g18 = new GameObject("D18_Probe");
g18.AddComponent<d18_probe>();
return "D18_STARTED";
