# -*- coding: utf-8 -*-
"""第二十四轮：把 drg_headnod 的三相位对照拼成一张"决定性"图。
上排 = swimWaveHeadGain 1.0（工程现值），下排 = 0.0（建议值）。
同一台相机只算一次 ⇒ 三列可直接横向比对；两排之间只有那一格不同。
"""
import os
from PIL import Image, ImageDraw, ImageFont

SRC = r"D:\Unity Project\InkWash\Tools\screenshots\enemies\H24"
OUT = os.path.join(SRC, "_头vs头端增益_v1.png")

CW, CH = 520, 347
COLS = [("000", "相位 0.00"), ("030", "相位 0.30"), ("060", "相位 0.60")]
ROWS = [
    ("100", "swimWaveHeadGain = 1.00（工程现值）   吻部仰角 −33.77 / +15.87 / −33.30   ⇒ 极差 49.65°", (176, 42, 42)),
    ("000", "swimWaveHeadGain = 0.00（建议值）     吻部仰角  −7.38 /  −6.91 /  −7.45   ⇒ 极差  0.55°", (28, 110, 54)),
]

COLBAR, ROWBAR, CAP = 30, 36, 46
W = CW * len(COLS)
H = COLBAR + len(ROWS) * (ROWBAR + CH) + CAP

def font(sz, bold=False):
    for p in [r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc",
              r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()

F_COL, F_ROW, F_CAP = font(18, True), font(17, True), font(15)

sheet = Image.new("RGB", (W, H), (250, 250, 248))
d = ImageDraw.Draw(sheet)

# 列标题
for k, (_, lab) in enumerate(COLS):
    d.text((k * CW + 10, 6), lab, fill=(40, 40, 40), font=F_COL)

y = COLBAR
for tag, title, col in ROWS:
    d.rectangle([0, y, W, y + ROWBAR - 1], fill=col)
    d.text((10, y + 8), title, fill=(255, 255, 255), font=F_ROW)
    y += ROWBAR
    for k, (ph, _) in enumerate(COLS):
        p = os.path.join(SRC, "hn2_hg%s_ph%s.png" % (tag, ph))
        if not os.path.exists(p):
            d.rectangle([k * CW, y, k * CW + CW - 1, y + CH - 1], fill=(230, 230, 230))
            d.text((k * CW + 12, y + 12), "缺图 " + os.path.basename(p), fill=(150, 0, 0), font=F_CAP)
            continue
        sheet.paste(Image.open(p).convert("RGB").resize((CW, CH), Image.LANCZOS), (k * CW, y))
        d.rectangle([k * CW, y, k * CW + CW - 1, y + CH - 1], outline=(190, 190, 190))
    y += CH

d.text((10, y + 8),
       "同机位、pitch=0（纯模型基准）、hoverStationary（位置/朝向冻结但蛇形波照跑）"
       "—— 每列同一行波相位，两排之间只差 swimWaveHeadGain 一格。",
       fill=(70, 70, 70), font=F_CAP)

sheet.save(OUT)
print("saved", OUT, sheet.size, os.path.getsize(OUT), "bytes")
