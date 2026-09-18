// q_unlock.cs —— 诊断/修复「Unity 不重编脚本」。
//
// 现象（09-18 实测）：改了 `EnemyDragon.cs`（字面量 2.2 → 3.0）之后
//   · `AssetDatabase.Refresh(ForceUpdate)` → isCompiling 有时 True 有时 False
//   · `CompilationPipeline.RequestScriptCompilation()` → 无效果
//   · 新增一个 .cs 文件（本会强制重编）→ DLL **mtime 变了但字节大小完全没变**
//   ⇒ 结论：编译被**推迟**了，不是"认为没变化"。
//
// 最可能的原因：桥（Codely）为安全执行 C# 脚本会调 `EditorApplication.LockReloadAssemblies()`，
//   若没有配对释放，Unity 会一直不重载程序集 ⇒ 编译无限期挂起。
// 本脚本先解锁再请求重编；判据看 DLL 的 **大小**（新增类必然变大）而不只是 mtime。
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

EditorApplication.UnlockReloadAssemblies();
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
CompilationPipeline.RequestScriptCompilation();
Debug.Log("[q_unlock] UnlockReloadAssemblies + Refresh + RequestScriptCompilation");

return "UNLOCK_REQUESTED";
