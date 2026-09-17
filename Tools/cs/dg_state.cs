using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// dg_state.cs —— 只读查询 Unity 当前状态（不依赖任何自定义类型，避免跨编译单元）
var sb = new StringBuilder();
sb.AppendLine("isPlaying   = " + Application.isPlaying);
sb.AppendLine("scene       = " + SceneManager.GetActiveScene().name);
sb.AppendLine("timeScale   = " + Time.timeScale);
sb.AppendLine("frameCount  = " + Time.frameCount);
sb.AppendLine("probe2 对象 = " + (GameObject.Find("dg_enemy_probe2") == null ? "不存在" : "在"));
sb.AppendLine("p2_Viz      = " + (GameObject.Find("dg_p2_Viz") == null ? "不存在" : "在"));

int n = 0;
foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
    if (r.name.StartsWith("dg_"))
    {
        sb.AppendLine("  dg_ 根: " + r.name + "  active=" + r.activeInHierarchy);
        n++;
    }
sb.AppendLine("dg_ 根对象数 = " + n);

sb.AppendLine("--- 最近 Console ---");
return sb.ToString();
