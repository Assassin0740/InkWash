// cap_dragon4：M7 交付用 —— 依次播放龙的四个招式，一次录完（盘旋 / 俯冲撕咬 / 俯冲扫尾 / 吐息）。
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

IEnumerator Run()
{
    Type tyAS = null;
    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
    { var t = a.GetType("InkWash.DebugTools.ActionShowcase"); if (t != null) { tyAS = t; break; } }
    var insts = UnityEngine.Object.FindObjectsOfType(tyAS, true);
    if (insts == null || insts.Length == 0) { Debug.Log("[CAP] 演示场不在当前场景"); yield break; }
    var play = tyAS.GetMethod("PlayForCapture", BindingFlags.Public | BindingFlags.Instance);

    // 演示场条目：17 盘旋 / 18 俯冲撕咬 / 19 俯冲扫尾 / 20 吐息
    string[] names = { "盘旋 Circling", "俯冲撕咬 Dive->Bite", "俯冲扫尾 Dive->Sweep", "吐息 Breath" };
    int[] entries = { 17, 18, 19, 20 };

    for (int i = 0; i < entries.Length; i++)
    {
        play.Invoke(insts[0], new object[] { entries[i] });
        Debug.Log("[CAP] 条目 " + entries[i] + " = " + names[i]);
        float t0 = Time.time;
        while (Time.time - t0 < 2.6f) yield return null;
    }
    Debug.Log("[CAP] 四个招式录制结束");
}

return Run();
