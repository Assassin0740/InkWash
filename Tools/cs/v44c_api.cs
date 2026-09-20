using System.Collections;
using System.Text;
using UnityEngine;
using UnityEditor.Animations;

IEnumerator Body()
{
    var sb = new StringBuilder();
    foreach (var m in typeof(AnimatorStateMachine).GetMethods())
        if (m.Name.Contains("Remove") || m.Name.Contains("Transition")) sb.AppendLine("SM: " + m.Name);
    foreach (var m in typeof(AnimatorState).GetMethods())
        if (m.Name.Contains("Remove") || m.Name.Contains("Transition") || m.Name.Contains("Add")) sb.AppendLine("ST: " + m.Name + " " + string.Join(",", System.Linq.Enumerable.Select(m.GetParameters(), p => p.ParameterType.Name + " " + p.Name)));
    foreach (var p in typeof(AnimatorStateMachine).GetProperties())
        if (p.Name.Contains("ransition")) sb.AppendLine("SMP: " + p.Name + " set=" + p.CanWrite);
    Debug.Log("[v44c]\n" + sb.ToString());
    yield break;
}

return Body();
