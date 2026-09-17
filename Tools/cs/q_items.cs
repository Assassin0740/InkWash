// 只读：确认 ActionShowcase 的条目清单是否是【改动生效后】的版本
// 判据：加墨山/墨骨前是 23 条，加后应为 29 条（每个敌人 AddEnemy 出 3 条）
using System.Text;
using System.Reflection;

var sb = new StringBuilder();
System.Type ty = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    var x = a.GetType("InkWash.DebugTools.ActionShowcase");
    if (x != null) { ty = x; break; }
}
if (ty == null) return "找不到 ActionShowcase 类型";

var insts = UnityEngine.Object.FindObjectsOfType(ty, true);
sb.AppendLine("ActionShowcase 实例 = " + insts.Length);
if (insts.Length == 0) return sb.ToString();

var comp = insts[0] as UnityEngine.MonoBehaviour;
var pCount = ty.GetProperty("ItemCount", BindingFlags.Public | BindingFlags.Instance);
var mLabel = ty.GetMethod("ItemLabel", BindingFlags.Public | BindingFlags.Instance);
var mGroup = ty.GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

int n = pCount != null ? (int)pCount.GetValue(comp) : -1;
sb.AppendLine("ItemCount = " + n);

if (mGroup != null)
{
    var list = mGroup.GetValue(comp) as System.Collections.IEnumerable;
    int i = 0;
    if (list != null)
        foreach (var it in list)
        {
            var t = it.GetType();
            var gf = t.GetField("group"); var lf = t.GetField("label"); var pf = t.GetField("prefab");
            string g = gf != null ? (string)gf.GetValue(it) : "?";
            string l = lf != null ? (string)lf.GetValue(it) : "?";
            var po = pf != null ? pf.GetValue(it) as UnityEngine.Object : null;
            sb.AppendLine(string.Format("[{0,2}] {1,-22} {2,-16} prefab={3}", i, g, l, po == null ? "<null>" : po.name));
            i++;
        }
}
return sb.ToString();
