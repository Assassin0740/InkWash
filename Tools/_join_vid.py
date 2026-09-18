#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把 vid/ 下按条目前缀分段的录像帧拼成一段顺序序列，供 make_dragon_video.py 用。
（ffmpeg 的 image2 输入要连续编号，而录像帧是按条目各从 0 开始编号的。）
"""
import os
import shutil
import sys

SRC = r"D:/Unity Project/InkWash/Tools/screenshots/enemies/S23/vid"
DST = r"D:/Unity Project/InkWash/Tools/screenshots/enemies/S23/vid_all"
TAGS = ["drs_bite", "drs_breath"]

if os.path.isdir(DST):
    shutil.rmtree(DST)
os.makedirs(DST)

k = 0
for tag in TAGS:
    names = sorted(f for f in os.listdir(SRC) if f.startswith(tag + "_") and f.endswith(".png"))
    if not names:
        print("缺帧:", tag)
        sys.exit(1)
    for f in names:
        shutil.copyfile(os.path.join(SRC, f), os.path.join(DST, "drall_%04d.png" % k))
        k += 1
    print("%-10s %d 帧" % (tag, len(names)))

print("合计", k, "帧 ->", DST)
