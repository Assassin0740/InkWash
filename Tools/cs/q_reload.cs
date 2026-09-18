// q_reload.cs —— 强制 Unity 重新加载程序集（domain reload）。
//
// 诊断链（09-18 实测，一步步排除）：
//   ① `grep -ac "_PokeCompile" Library/ScriptAssemblies/Assembly-CSharp.dll` = 2
//      ⇒ **磁盘上的 DLL 确实是最新编译产物**（新类在、删掉的 `AddSpinePitch` 不在）。
//   ② 但 Play 里读 `diveSpiralAmp` 仍是旧值 2.2
//      ⇒ **Unity 内存中加载的程序集是旧的**：编译产物写盘了，但**域重载没发生**，
//         Play 复用的还是上一次加载的 Assembly。
//   ③ 所以判据要分两层：**DLL 文件内容**（用符号名 grep）≠ **运行时实际加载的程序集**。
//      只看 mtime 会以为"没编译"，只看运行时值会以为"编译没生效"，两个都会把人带偏。
//
// 本脚本请求一次脚本重载，让运行时真正切到新程序集。
using UnityEditor;

EditorUtility.RequestScriptReload();
return "RELOAD_REQUESTED";
