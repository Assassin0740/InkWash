// 可靠的编译状态检查。
//
// 为什么需要单独一个：域重载会把 Unity Console 清空，编译错误随之被冲掉，
// 于是 Tools/read_console.py 会报"控制台干净：无 error"—— 这是**假信号**。
// 本机踩过一次：PlaytestHarness.cs 里引用了不存在的变量，编译失败，
// 但 Console 读出来是干净的，验收跑的还是上一次编译出来的程序集，
// 报告里的新字段/新文案一个都没出现，差点误判成"改动无效"。
// 判编译成功与否只能看 EditorUtility.scriptCompilationFailed，不能看 Console。
using UnityEditor;
using UnityEngine;

var sb = new System.Text.StringBuilder();
sb.AppendLine("isPlaying               = " + Application.isPlaying);
sb.AppendLine("isCompiling             = " + EditorApplication.isCompiling);
sb.AppendLine("scriptCompilationFailed = " + EditorUtility.scriptCompilationFailed);
return sb.ToString();
