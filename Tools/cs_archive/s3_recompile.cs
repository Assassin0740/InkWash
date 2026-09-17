// 强制 Unity 重新导入 + 重新编译脚本。
// 背景：这里的编辑器似乎没开 Auto Refresh（或文件监听漏了），
// 直接改 .cs 之后跑验收，跑的还是上一次编译出来的程序集 ——
// 表现是报告里看不到新加的字段/文案，很容易误判成"改动无效"。
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
sb.AppendLine("刷新前: isPlaying=" + Application.isPlaying + " isCompiling=" + EditorApplication.isCompiling);

AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

sb.AppendLine("Refresh 已请求: isPlaying=" + Application.isPlaying + " isCompiling=" + EditorApplication.isCompiling);
return sb.ToString();
