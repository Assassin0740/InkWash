#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把 Tools/screenshots/vfx/*.png 拼成一张「实物对照表」，并逐张判有没有洋红。

为什么要程序判洋红
------------------
「哪个 prefab 在 URP 下会变洋红」不能靠猜着色器名 —— Legacy Shaders 的 pass 没有
LightMode 标签时会落到 `SRPDefaultUnlit`，URP **照样会画**（本次实测：WarFX 的作者
自写着色器渲染正常，不是洋红）。所以判据必须是**图上有没有洋红像素**。

洋红判据：Unity 的错误着色器输出是线性 (1,0,1)，sRGB 输出即 (255,0,255)。
取「R>200 且 B>200 且 G<90」的像素；占比 > 1% 就算「该 prefab 有材质没渲染」。

输出：Tools/screenshots/vfx/_粒子素材_实物对照_v1.png
"""
import io
import os
import sys
import json

sys.stdout.reconfigure(encoding="utf-8")

from PIL import Image, ImageDraw, ImageFont
import numpy as np

ROOT = "D:/Unity Project/InkWash"
SRC = os.path.join(ROOT, "Tools", "screenshots", "vfx")
RP = os.path.join(ROOT, "Tools", "reports", "vfx_sheet.txt")
OUT = os.path.join(ROOT, "Tools", "screenshots", "vfx", "_粒子素材_实物对照_v1.png")

W = 1080


class M:
    def __init__(self):
        self.c = {}

    def font(self, s, bold=False):
        k = (s, bold)
        if k in self.c:
            return self.c[k]
        for p in [r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc",
                  r"C:\Windows\Fonts\msyh.ttc",
                  r"C:\Windows\Fonts\simhei.ttf",
                  r"C:\Windows\Fonts\arial.ttf"]:
            if os.path.exists(p):
                try:
                    self.c[k] = ImageFont.truetype(p, s)
                    return self.c[k]
                except Exception:
                    pass
        self.c[k] = ImageFont.load_default()
        return self.c[k]


M = M()


def parse_report():
    """从报告里取 tag → (归类, 时刻, 内容px, 着色器串)"""
    rows = {}
    if not os.path.exists(RP):
        return rows
    for line in io.open(RP, encoding="utf-8", errors="replace").read().splitlines():
        parts = line.split()
        if len(parts) < 4:
            continue
        tag = parts[0]
        if not tag[:2].isdigit():
            continue
        grp = parts[1]
        t = parts[2]
        n = parts[3]
        urp = "全URP" if "[全URP]" in line else "非URP"
        sh = line.split("[非URP", 1)[-1].split("]", 1)[-1].strip() if "[非URP" in line \
            else line.split("[全URP]", 1)[-1].strip()
        rows[tag] = dict(group=grp, t=t, n=int(n), urp=urp, shaders=sh)
    return rows


def magenta_ratio(im):
    a = np.asarray(im.convert("RGB")).astype(np.int16)
    m = (a[:, :, 0] > 200) & (a[:, :, 2] > 200) & (a[:, :, 1] < 90)
    return m.sum() / float(a.shape[0] * a.shape[1])


def main():
    meta = parse_report()
    tags = sorted([f[:-4] for f in os.listdir(SRC)
                   if f.endswith(".png") and not f.startswith("_")])
    print("找到 %d 张单图" % len(tags))

    tiles = []
    for tag in tags:
        f = os.path.join(SRC, tag + ".png")
        im = Image.open(f).convert("RGB")
        mr = magenta_ratio(im)
        info = meta.get(tag, {})
        tiles.append((tag, im, mr, info))

    # 网格
    NC = 5
    TW = 196
    PAD = 10
    LBL = 52
    HEAD = 104
    NR = (len(tiles) + NC - 1) // NC
    cw = PAD + NC * (TW + PAD)
    ch = HEAD + PAD + NR * (TW + LBL + PAD)

    cv = Image.new("RGB", (cw, ch), (232, 234, 238))
    d = ImageDraw.Draw(cv)

    d.rectangle([0, 0, cw, HEAD - 10], fill=(26, 28, 32))
    d.text((14, 12), "新导入粒子素材 · 实物对照（编辑模式 Simulate 出图，不是宣传图）",
           font=M.font(25, True), fill=(255, 236, 200))
    d.text((14, 48), "每格：该 prefab 自己渲染出来的样子｜红框 = 图里有洋红像素（材质没在 URP 下渲染）｜"
                     "其余都是正常渲染",
           font=M.font(16), fill=(150, 156, 170))
    d.text((14, 74), "「全URP」= 材质是 Universal Render Pipeline/*；"
                     "但**别只看名字** —— 有些内置着色器 URP 照样画（见红框判定）",
           font=M.font(16), fill=(150, 156, 170))

    for i, (tag, im, mr, info) in enumerate(tiles):
        r, c = divmod(i, NC)
        x = PAD + c * (TW + PAD)
        y = HEAD + PAD // 2 + r * (TW + LBL + PAD)
        t = im.resize((TW, TW), Image.LANCZOS)
        cv.paste(t, (x, y))
        bad = mr > 0.01
        d.rectangle([x - 1, y - 1, x + TW, y + TW],
                    outline=(214, 40, 40) if bad else (150, 156, 166),
                    width=3 if bad else 1)

        label = tag.split("_", 1)[1] if "_" in tag else tag
        grp = info.get("group", "")
        d.text((x + 1, y + TW + 4), label, font=M.font(15, True),
               fill=(176, 34, 34) if bad else (30, 34, 40))
        sub = (grp + " · t=" + str(info.get("t", "?"))) if info else grp
        d.text((x + 1, y + TW + 22), sub, font=M.font(13), fill=(95, 100, 110))
        d.text((x + 1, y + TW + 36),
               ("洋红 %.1f%%" % (mr * 100)) if bad else ("正常 · %s" % info.get("urp", "")),
               font=M.font(13, True), fill=(176, 34, 34) if bad else (24, 120, 60))

    cv.save(OUT)
    print("写出 %s  (%d x %d)" % (OUT, cw, ch))

    print("\n=== 洋红判定明细 ===")
    for tag, _im, mr, info in tiles:
        print("  %-24s %-6s 洋红 %6.2f%%  %s" % (tag, info.get("group", "?"), mr * 100,
                                                "❌ 有材质没渲染" if mr > 0.01 else "✅ 正常"))

    json.dump({t[0]: dict(magenta=t[2], **{k: v for k, v in t[3].items()})
               for t in tiles},
              io.open(os.path.join(ROOT, "Tools", "reports", "vfx_sheet.json"), "w",
                      encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
