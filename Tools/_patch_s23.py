# 一次性补丁：给 drg_storm23 的判据 4 加上「俯冲段」专用窗口
# 每个替换都带 count(old) 断言 —— 上一轮的教训是同文件并行 Edit 会静默丢改动。
import io

P = r"D:/Unity Project/InkWash/Tools/cs/drg_storm23.cs"
s = io.open(P, encoding="utf-8", newline="").read()
NL = "\r\n" if "\r\n" in s else "\n"
print("换行符:", repr(NL), "长度:", len(s))

A = NL.join

reps = [
    # ① 声明
    ("        int nearFrames = 0, frames = 0;",
     A(["        int nearFrames = 0, frames = 0;",
        "        // ★ 判据 4 的**正确窗口**：原文是「俯冲段」（Tell/Dive/Strike）。",
        "        //   「-（进场）」与「Recover」这两段里烟还没冒或已经散了 ⇒ 根本没有烟可以靠近，",
        "        //   把它们算进分母就是在稀释指标（实测 bite 的 Recover n=130，占 156 帧的 83%）。",
        "        int diveFrames = 0, diveNear = 0;"])),

    # ② 累加器
    ("        var accDist = new Acc(); var accLen = new Acc(); var accPuff = new Acc();",
     "        var accDist = new Acc(); var accDistDive = new Acc(); var accLen = new Acc(); var accPuff = new Acc();"),

    # ③ 逐帧统计（插在相位读出来之后，因为它要用 ph）
    (A(["            if (!byPhase.TryGetValue(ph, out pa)) { pa = new Acc(); byPhase[ph] = pa; }",
        "            pa.Add(arcN);"]),
     A(["            if (!byPhase.TryGetValue(ph, out pa)) { pa = new Acc(); byPhase[ph] = pa; }",
        "            pa.Add(arcN);",
        "            bool phDive = ph == \"Tell\" || ph == \"Dive\" || ph == \"Strike\";",
        "            if (phDive) { diveFrames++; accDistDive.Add(minD); if (minD < 0.6f) diveNear++; }"])),

    # ④ 报告行
    ("                       + \"%   ← 判据 4 要 ≥80%（距离=999 表示无烟，不计入达标）\");",
     A(["                       + \"%   ← 全窗（含 Recover/进场这些**没有烟**的段）\");",
        "        _sb.AppendLine(\"  俯冲段(Tell/Dive/Strike) <0.6 m 的帧 \" + diveNear + \"/\" + diveFrames",
        "                       + \" = \" + (diveFrames > 0 ? (100f * diveNear / diveFrames).ToString(\"F1\") + \"%\" : \"n/a\")",
        "                       + \"  ｜ 距离中位 \" + accDistDive.Median().ToString(\"F3\") + \" m\"",
        "                       + \"   ← ★ 判据 4 的**正确窗口**（原文就是「俯冲段」），要 ≥80%\");"])),
]

for old, new in reps:
    n = s.count(old)
    assert n == 1, "期望 1 处，实际 %d 处：%s" % (n, old[:70])
    s = s.replace(old, new)
    print("ok:", old[:56].replace(NL, "\\n"))

io.open(P, "w", encoding="utf-8", newline="").write(s)
print("已写入", P)
