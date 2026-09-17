// ws_audit.cs —— 审计 Main.unity 里有没有别的「调试态残留」
//
// 起因：41f1c9a 那次提交里 Main.unity 有一个 772 字节的二进制改动，
// 事后查明是**做演示场时把 WaveSpawner 关掉**的状态被一起存进了场景。
// 一个状态能被误存，就可能有第二个 —— 所以动手修之前先把整个场景审一遍，
// 免得只修了看得见的那一处。
//
// 只读，不做任何修改。
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
sb.AppendLine("===== ws_audit：Main 场景调试态残留审计 =====");
sb.AppendLine("isPlaying = " + EditorApplication.isPlaying);

var sc = EditorSceneManager.GetActiveScene();
sb.AppendLine("场景 = " + sc.name + "　isDirty = " + sc.isDirty + "　roots = " + sc.rootCount);

var roots = sc.GetRootGameObjects();
sb.AppendLine();
sb.AppendLine("---------- 根对象 ----------");
foreach (var go in roots)
    sb.AppendLine("  " + go.name
                  + (go.activeSelf ? "" : "　★ inactive")
                  + "　组件 " + go.GetComponents<Component>().Length);

// ★ 本项目硬规矩：临时物统一 A<数字>_ 前缀。残留下来会直接影响玩法。
sb.AppendLine();
sb.AppendLine("---------- 疑似临时物（A<数字>_ 前缀）----------");
int tmp = 0;
foreach (var go in roots)
    if (System.Text.RegularExpressions.Regex.IsMatch(go.name, "^A\\d+_"))
    {
        sb.AppendLine("  ★ " + go.name);
        tmp++;
    }
sb.AppendLine("  命中 " + tmp + " 个");

// 演示场的手工物（不受 A<数字>_ 正则保护）
sb.AppendLine();
sb.AppendLine("---------- 演示场残留（手工命名）----------");
foreach (var go in roots)
{
    string n = go.name.ToLowerInvariant();
    if (n.Contains("hover") || n.Contains("showcase") || n.Contains("test") || n.Contains("demo"))
        sb.AppendLine("  ? " + go.name);
}

// 关键组件的 enabled 一览（这些是"看起来没事但被关掉就静默失效"的典型）
sb.AppendLine();
sb.AppendLine("---------- 关键组件 enabled ----------");
var types = new[] {
    typeof(InkWash.Enemies.WaveSpawner),
    typeof(InkWash.Roguelike.RunManager),
    typeof(InkWash.Enemies.RoomController),
    typeof(InkWash.Player.PlayerController),
    typeof(InkWash.CameraRig.ThirdPersonCamera),
    typeof(InkWash.UI.InkStylePanel),
    // 注：InkMaterialForcer 是**静态类**（无实例），不能进 FindObjectsOfType，
    // 否则整份探针抛 "The type has to be derived from UnityEngine.Object"。
};
foreach (var t in types)
{
    var found = UnityEngine.Object.FindObjectsOfType(t, true);
    if (found.Length == 0) { sb.AppendLine("  " + t.Name + "：场景里没有"); continue; }
    foreach (var o in found)
    {
        var b = o as Behaviour;
        var c = o as Collider;
        string state = b != null ? ("enabled=" + b.enabled) : (c != null ? ("enabled=" + c.enabled) : "n/a");
        var comp = o as Component;
        sb.AppendLine("  " + t.Name + " [" + comp.gameObject.name + "] " + state
                      + (b != null && !b.enabled ? "　★ 已禁用" : ""));
    }
}

// 玩家血/等级等运行时态是否被顺手存下来
sb.AppendLine();
sb.AppendLine("---------- 流水线设置（是否被调试态污染）----------");
var pipes = Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();
sb.AppendLine("  URP 资产数量 = " + pipes.Length);
foreach (var p in pipes)
    sb.AppendLine("    " + p.name + "　MSAA=" + p.msaaSampleCount);

return sb.ToString();
