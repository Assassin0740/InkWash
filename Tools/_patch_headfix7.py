# -*- coding: utf-8 -*-
"""第二十六轮补丁 7：把「半径比 1.3252」的**已证来源**写进读法。
来源由 drg_scale26.cs 实测钉死：
  Z_Enemy_MoLong.prefab 根 localScale = 1.0000（骨骼 lossy 0.0024）
  Z_Dragon.prefab       根 localScale = 0.7546（骨骼 lossy 0.0018）
  1 / 0.7546 = 1.3252  ← 与报表里恒定的半径比逐位相同
"""
import io, sys

P = "Tools/cs/drg_headfix.cs"
src = io.open(P, encoding="utf-8").read()
orig = src

OLD = '''        _sb.AppendLine("  · **半径比** = |叶骨-挂点| 活体 / 基准（均值）。第一版这里报的是「Δpos 0.1935m」，");
        _sb.AppendLine("    恒定出现在**包括 rest 在内**的每一行 ⇒ 那是跨实例标尺差（厘米制 scale），已改为比值单列。");'''

NEW = '''        _sb.AppendLine("  · **半径比** = |叶骨-挂点| 活体 / 基准（均值）。第一版这里报的是「Δpos 0.1935m」，");
        _sb.AppendLine("    恒定出现在**包括 rest 在内**的每一行 ⇒ 那是跨实例标尺差，不是姿态误差。");
        _sb.AppendLine("    ★ 来源已由 drg_scale26.cs 实测钉死：`Z_Enemy_MoLong.prefab` 根 localScale = 1.0000");
        _sb.AppendLine("      （骨骼 lossy 0.0024），`Z_Dragon.prefab` 根 localScale = 0.7546（骨骼 lossy 0.0018）");
        _sb.AppendLine("      ⇒ 1 / 0.7546 = **1.3252**，与上表半径比逐位相同。纯均匀缩放，与姿态无关；");
        _sb.AppendLine("      0.1935 m 也由此对上：0.1935 / 0.3252 = 0.595 m = 叶骨到挂点的平均距离。");'''

c = src.count(OLD)
print("count =", c)
if c != 1:
    sys.exit(1)
src = src.replace(OLD, NEW)
assert src != orig
io.open(P, "w", encoding="utf-8", newline="\n").write(src)
print("WROTE %s  (%d -> %d bytes)" % (P, len(orig), len(src)))
