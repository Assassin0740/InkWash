using System.Reflection;
using System.Text;
using UnityEngine;

// dg_probe2_peek.cs —— 分帧探针卡住时，用反射读它的内部状态（含已累积的报告文本）
var sb = new StringBuilder();
var probe = GameObject.Find("dg_enemy_probe2");
if (probe == null)
{
    sb.AppendLine("探针对象不存在（可能已 Finish 并自毁）");
}
else
{
    MonoBehaviour target = null;
    foreach (var mb in probe.GetComponents<MonoBehaviour>())
        if (mb != null && mb.GetType().Name == "dg_enemy_probe2") { target = mb; break; }

    if (target == null) sb.AppendLine("★ 没找到 dg_enemy_probe2 组件");
    else
    {
        var t = target.GetType();
        System.Func<string, object> Get = n =>
        {
            var f = t.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? "★无此字段" : f.GetValue(target);
        };
        sb.AppendLine("enabled = " + target.enabled);
        sb.AppendLine("_phase=" + Get("_phase") + "  _idx=" + Get("_idx") + "  _wait=" + Get("_wait"));

        var sbf = t.GetField("_sb", BindingFlags.NonPublic | BindingFlags.Instance);
        var val = sbf == null ? null : sbf.GetValue(target) as StringBuilder;
        sb.AppendLine("_sb.Length = " + (val == null ? -1 : val.Length));
        if (val != null && val.Length > 0)
        {
            sb.AppendLine("--- _sb 末尾 1800 字符 ---");
            int st = Mathf.Max(0, val.Length - 1800);
            sb.AppendLine(val.ToString(st, val.Length - st));
        }
    }
}
return sb.ToString();
