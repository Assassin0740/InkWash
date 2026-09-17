// 只读：当前开着哪个场景、在不在 Play、演示场在不在场
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
var sb = new System.Text.StringBuilder();
var act = SceneManager.GetActiveScene();
sb.AppendLine("activeScene = " + act.name + "  (" + act.path + ")");
sb.AppendLine("isPlaying    = " + UnityEditor.EditorApplication.isPlaying);
sb.AppendLine("isPaused     = " + UnityEditor.EditorApplication.isPaused);
var asm = System.AppDomain.CurrentDomain.GetAssemblies();
System.Type as_ = null;
foreach (var a in asm) { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { as_ = t; break; } }
if (as_ == null) sb.AppendLine("ActionShowcase 类型：找不到");
else
{
    var all = UnityEngine.Object.FindObjectsOfType(as_, true);
    sb.AppendLine("ActionShowcase 实例数 = " + all.Length);
    foreach (var o in all)
    {
        var c = o as Component;
        sb.AppendLine("  在 " + c.gameObject.name + "  pos=" + c.transform.position.ToString("F2"));
    }
}
sb.AppendLine("场景根对象：");
foreach (var g in act.GetRootGameObjects()) sb.AppendLine("  " + g.name);
return sb.ToString();
