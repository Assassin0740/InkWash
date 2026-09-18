# 一次性补丁：判据 7 换尺子 —— 用 ProfilerRecorder 读「GC Allocated In Frame」
#
# 为什么必须换：现在用的是 `GC.GetTotalMemory(false)` 的窗口差，
# 它 = 窗口内分配量 − 窗口内被 GC 回收的量 ⇒ **测不了分配速率**。
# 实测（第二十三轮，特效**关**着）三段窗口分别是 0 / 524 / 668 KB，
# 还出现过 -134068 KB（一次 GC 发生）⇒ 这个尺子的噪声和被测量同量级。
import io

P = r"D:/Unity Project/InkWash/Tools/cs/drg_storm23.cs"
s = io.open(P, encoding="utf-8", newline="").read()
NL = "\r\n" if "\r\n" in s else "\n"
A = NL.join
print("换行符:", repr(NL), "长度:", len(s))

reps = [
    # ① using
    ("using System.Text;" + NL + "using UnityEngine;",
     A(["using System.Text;",
        "using Unity.Profiling;   // ★ 判据 7 的正确尺子：ProfilerRecorder(\"GC Allocated In Frame\")",
        "using UnityEngine;"])),

    # ② 建 recorder
    ("        Time.captureFramerate = 0;" + NL + "        yield return null; yield return null;",
     A(["        Time.captureFramerate = 0;",
        "        // ★ 判据 7 的正确尺子：`GC.GetTotalMemory` 的窗口差 = 分配量 − 窗口内被回收的量，",
        "        //   它测不了分配速率。实测（特效**关**着）三段窗口是 0 / 524 / 668 KB，",
        "        //   还出现过 -134068 KB（一次 GC 发生）⇒ 噪声与被测量同量级，得不出结论。",
        "        //   `GC Allocated In Frame` 是**逐帧**计数，不含回收，直接就是 B/帧。",
        "        var rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, \"GC Allocated In Frame\");",
        "        yield return null; yield return null;"])),

    # ③ 窗口内累加
    ("                long m0 = GC.GetTotalMemory(false);",
     A(["                long m0 = GC.GetTotalMemory(false);",
        "                long alloc = 0;                    // 本窗口累计分配字节（rec.Valid 时才有效）"])),

    ("                    yield return null;" + NL + "                    float dt = Time.unscaledDeltaTime * 1000f;",
     A(["                    yield return null;",
        "                    if (rec.Valid) alloc += rec.LastValue;",
        "                    float dt = Time.unscaledDeltaTime * 1000f;"])),

    # ④ 报告行
    ("                               + \"  GCΔ \" + (gc[w] / 1024f).ToString(\"F1\") + \" KB / 90 帧\");",
     A(["                               + \"  GCΔ \" + (gc[w] / 1024f).ToString(\"F1\") + \" KB / 90 帧\"",
        "                               + \"  ｜ **分配 \" + (rec.Valid ? (alloc / 90f).ToString(\"F0\") + \" B/帧**\"",
        "                                                   : \"n/a（本版本没有 GC Allocated In Frame 计数器）\"));"])),

    # ⑤ 收尾
    ("        if (_fStormOn != null && _drg != null) _fStormOn.SetValue(_drg, true);",
     A(["        if (rec.Valid) rec.Dispose();",
        "        if (_fStormOn != null && _drg != null) _fStormOn.SetValue(_drg, true);"])),
]

for old, new in reps:
    n = s.count(old)
    assert n == 1, "期望 1 处，实际 %d 处：%s" % (n, old[:70].replace(NL, "\\n"))
    s = s.replace(old, new)
    print("ok:", old[:56].replace(NL, "\\n"))

io.open(P, "w", encoding="utf-8", newline="").write(s)
print("已写入", P)
