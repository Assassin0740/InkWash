// 只读：确认演示场已就位（Play 中）
var sb = new System.Text.StringBuilder();
sb.AppendLine("isPlaying = " + UnityEditor.EditorApplication.isPlaying);
var act = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.AppendLine("activeScene = " + act.name);
var asm = System.AppDomain.CurrentDomain.GetAssemblies();
System.Type t = null;
foreach (var a in asm) { var x = a.GetType("InkWash.DebugTools.ActionShowcase"); if (x != null) { t = x; break; } }
if (t == null) sb.AppendLine("找不到 ActionShowcase 类型");
else { var all = UnityEngine.Object.FindObjectsOfType(t, true); sb.AppendLine("ActionShowcase 实例 = " + all.Length); }
sb.AppendLine("根对象：");
foreach (var g in act.GetRootGameObjects()) sb.AppendLine("  " + g.name);
var cam = Camera.main;
sb.AppendLine("Camera.main = " + (cam != null ? (cam.name + " " + cam.pixelWidth + "x" + cam.pixelHeight + " pos=" + cam.transform.position.ToString("F2")) : "<null>"));
return sb.ToString();
