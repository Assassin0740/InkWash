using System;
using System.Reflection;
using UnityEngine;

// sc_set：把演示场切换到**指定条目并无限循环**（供"用户看着游戏画面口述"的复核流程用）。
//
// 条目号从 Tools/tmp/showcase_index.txt 读 —— 这样每次换动作只要改那个文件，
// 不用重写脚本（Codely 的 .cs 是独立编译单元，参数只能走文件/静态字段）。
//
// 同时 StopAutoClose()：关掉"到点自动收场"，于是该条目会一直循环演下去，
// 直到下一次 sc_set 或手动 Stop。
var path = "D:/Unity Project/InkWash/Tools/tmp/showcase_index.txt";
int idx = -1;
try { idx = int.Parse(System.IO.File.ReadAllText(path).Trim()); } catch { }
if (idx < 0) return "SC_SET_NO_INDEX";

Type ty = null;
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{ var t = asm.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { ty = t; break; } }
if (ty == null) return "SC_SET_NO_TYPE";

UnityEngine.Object target = null;
foreach (var o in UnityEngine.Object.FindObjectsOfType(ty))
{ if (o != null) { target = o; break; } }
if (target == null) return "SC_SET_NO_INSTANCE";

var mStop = ty.GetMethod("StopAutoClose", BindingFlags.Public | BindingFlags.Instance);
var mPlay = ty.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);
var mLabel = ty.GetMethod("ItemLabel", BindingFlags.Public | BindingFlags.Instance);
var mCount = ty.GetProperty("ItemCount", BindingFlags.Public | BindingFlags.Instance);
if (mStop == null || mPlay == null) return "SC_SET_MISSING_API";

int count = mCount != null ? (int)mCount.GetValue(target, null) : -1;
if (idx >= count) return "SC_SET_BAD_INDEX " + idx + " >= " + count;

mStop.Invoke(target, null);
mPlay.Invoke(target, new object[] { idx });
string label = mLabel != null ? (string)mLabel.Invoke(target, new object[] { idx }) : "?";

var mResume = ty.GetMethod("ResumeAutoClose", BindingFlags.Public | BindingFlags.Instance);
return "SC_SET_OK idx=" + idx + " count=" + count + " [" + label + "] 循环中";
