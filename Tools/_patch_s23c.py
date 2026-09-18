# 一次性补丁：判据 4 再加一个「有烟的帧」窗口（语义窗口，分母大且稳定）
import io

P = r"D:/Unity Project/InkWash/Tools/cs/drg_storm23.cs"
s = io.open(P, encoding="utf-8", newline="").read()
NL = "\r\n" if "\r\n" in s else "\n"
A = NL.join
print("换行符:", repr(NL), "长度:", len(s))

reps = [
    ("        int diveFrames = 0, diveNear = 0;",
     A(["        int diveFrames = 0, diveNear = 0;",
        "        // ★★ 判据 4 的**语义窗口**：只在「确实有烟」的帧上问「烟里有没有电弧」。",
        "        //   为什么不用相位窗口（Tell/Dive/Strike）：那段只有 18 帧，同一份代码连跑三次",
        "        //   分别得到 50% / 61% / 67% —— **摆动来自相位边界和龙的轨迹，不是效果本身**。",
        "        //   而「没有烟的帧」问「烟里有没有电弧」本身就是个错问题（无烟段必然不达标）。",
        "        int smokeFrames = 0, smokeNear = 0;"])),

    ("            if (phDive) { diveFrames++; accDistDive.Add(minD); if (minD < 0.6f) diveNear++; }",
     A(["            if (phDive) { diveFrames++; accDistDive.Add(minD); if (minD < 0.6f) diveNear++; }",
        "            if (smokeLv > 0.10f) { smokeFrames++; if (minD < 0.6f) smokeNear++; }"])),

    ("                       + \"   ← ★ 判据 4 的**正确窗口**（原文就是「俯冲段」），要 ≥80%\");",
     A(["                       + \"   ← 相位窗口（n 小，摆动大，只作参考）\");",
        "        _sb.AppendLine(\"  有烟的帧(浓度>0.10) <0.6 m 的帧 \" + smokeNear + \"/\" + smokeFrames",
        "                       + \" = \" + (smokeFrames > 0 ? (100f * smokeNear / smokeFrames).ToString(\"F1\") + \"%\" : \"n/a\")",
        "                       + \"   ← ★★ 判据 4 的**语义窗口**（只在有烟时才问烟里有没有电弧），要 ≥80%\");"])),
]

for old, new in reps:
    n = s.count(old)
    assert n == 1, "期望 1 处，实际 %d 处：%s" % (n, old[:70].replace(NL, "\\n"))
    s = s.replace(old, new)
    print("ok:", old[:56].replace(NL, "\\n"))

io.open(P, "w", encoding="utf-8", newline="").write(s)
print("已写入", P)
