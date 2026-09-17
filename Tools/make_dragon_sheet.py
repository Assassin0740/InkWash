# -*- coding: utf-8 -*-
"""
make_dragon_sheet.py —— 墨龙阵营描边验收图

列 = 旧暗褐（改前） / 新朱砂（已落盘）
行 = 全景 0° / 全景 180° / 近景 0° / 近景 180°

同一帧内背靠背 Camera.Render()，唯一变量 _OutlineColor —— 排除了光照/姿态/时机干扰。
"""
import os
from PIL import Image, ImageDraw, ImageFont

BASE = "D:/Unity Project/InkWash/Tools/screenshots/enemies"

BG    = (20, 23, 28)
PANEL = (32, 37, 44)
TITLE = (233, 240, 247)
SUB   = (146, 159, 174)
DIM   = (108, 120, 134)
ACC   = (214, 108, 86)

CW, GAP, MARGIN = 460, 14, 26
HEAD, LAB, FOOT = 106, 28, 96


def font(sz, bold=False):
    for p in (r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc",
              r"C:\Windows\Fonts\simhei.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


def rs():
    return getattr(Image, "Resampling", Image).LANCZOS


def load(name):
    p = os.path.join(BASE, name)
    if not os.path.exists(p):
        return Image.new("RGB", (CW, int(CW * 9 / 16)), PANEL), False
    im = Image.open(p).convert("RGB")
    return im.resize((CW, int(CW * 9 / 16)), rs()), True


COLS = [
    ("old", "旧 · (0.042, 0.038, 0.036) 暗褐", "红区 0%"),
    ("new", "新 · (0.48, 0.035, 0.022) 朱砂", "红区 7~21%"),
]

ROWS = [
    ("far_r0",   1600, "全景 0°",   "龙占屏 1.9%",   "红区像素 0%  →  9.0%"),
    ("far_r180", 1600, "全景 180°", "龙占屏 3.9%",   "红区像素 0%  →  7.2%"),
    ("near_r0",  1600, "近景 0°",   "龙占屏 6.9%",   "红区像素 0%  →  11.8%"),
    ("near_r180", 1600, "近景 180°", "龙占屏 2.5%",  "红区像素 0%  →  21.1%"),
]

CH = int(CW * 9 / 16)
W = MARGIN * 2 + len(COLS) * CW + (len(COLS) - 1) * GAP
H = HEAD + 30 + len(ROWS) * (LAB + CH) + (len(ROWS) - 1) * GAP + FOOT
cv = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(cv)

# ── 页眉
d.text((MARGIN, 24), "墨龙阵营描边 —— 旧暗褐 vs 新朱砂", font=font(30, True), fill=TITLE)
d.text((MARGIN, 62), "同一帧内背靠背 Camera.Render()，唯一变量 _OutlineColor（排除光照 / 姿态 / 时机）",
       font=font(15), fill=SUB)

# ── 列标题
cy = HEAD
for j, (key, lab, note) in enumerate(COLS):
    x = MARGIN + j * (CW + GAP)
    c = ACC if key == "new" else DIM
    d.rectangle([x, cy, x + CW - 1, cy + 22], fill=PANEL)
    d.text((x + 8, cy + 3), lab, font=font(14, True), fill=c)
    tw = d.textlength(note, font=font(13))
    d.text((x + CW - 10 - tw, cy + 4), note, font=font(13), fill=c)

gy = cy + 30
for i, (tag, _w, rlab, rnote, rstat) in enumerate(ROWS):
    y = gy + i * (LAB + CH + GAP)
    # 行标签
    d.text((MARGIN, y + 6), rlab, font=font(15, True), fill=TITLE)
    lx = MARGIN + d.textlength(rlab, font=font(15, True)) + 12
    d.text((lx, y + 7), rnote, font=font(13), fill=DIM)
    rx = MARGIN + CW * 2 + GAP - 10
    tw = d.textlength(rstat, font=font(13, True))
    d.text((rx - tw, y + 7), rstat, font=font(13, True), fill=ACC)

    for j, (key, _lab, _note) in enumerate(COLS):
        x = MARGIN + j * (CW + GAP)
        im, ok = load("ED_%s_%s_1600x900.png" % (tag, key))
        cv.paste(im, (x, y + LAB))
        d.rectangle([x, y + LAB, x + CW - 1, y + LAB + CH - 1], outline=(52, 58, 68) if ok else (120, 60, 60))

# ── 页脚
fy = H - FOOT + 8
d.line([MARGIN, fy - 6, W - MARGIN, fy - 6], fill=(48, 55, 64), width=1)
d.text((MARGIN, fy + 6),
       "判据：红区像素 = 描边落在「彩度 > 14 且色相 325~35°」的像素占对象像素的比 —— 旧暗褐为 0%（引不出阵营色）",
       font=font(14), fill=SUB)
d.text((MARGIN, fy + 30),
       "墨龙 _OutlineColor (0.042,0.038,0.036) → (0.48,0.035,0.022) 朱砂，与六个敌人逐位相同；_OutlineWidth 0.018 原已一致，未动",
       font=font(14), fill=SUB)
d.text((MARGIN, fy + 54),
       "落盘确证 = 读 .mat 文件文本（内存回读会拿到同一实例，不作数）    数据：Tools/reports/ev_dragon{,_verify}.txt",
       font=font(13), fill=DIM)

out = os.path.join(BASE, "_验收_墨龙朱砂描边.png")
cv.save(out)
print("saved", out, cv.size)
