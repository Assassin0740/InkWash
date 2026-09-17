# -*- coding: utf-8 -*-
"""
make_ev_sheet.py —— 敌人「有色 + 红墨边」验收图合成

图 1  _验收_六敌人_有色+红墨边.png      ：六个敌人改造后（同机位、右侧主角作参照）
图 2  _定标_墨阶×红边色.png            ：墨阶(_BandBias) × 红边色 二维定标
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


def font(sz, bold=False):
    for p in (r"C:\Windows\Fonts\msyhbd.ttc" if bold else r"C:\Windows\Fonts\msyh.ttc",
              r"C:\Windows\Fonts\simhei.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


def resample():
    return getattr(Image, "Resampling", Image).LANCZOS


def load(name, w):
    p = os.path.join(BASE, name)
    if not os.path.exists(p):
        im = Image.new("RGB", (w, int(w * 9 / 16)), PANEL)
        return im, False
    im = Image.open(p).convert("RGB")
    h = int(im.height * w / im.width)
    return im.resize((w, h), resample()), True


FOOT = 78
GAP, MARGIN = 14, 26


def build_grid(rows, cols, cw, title, subtitle, out, row_labels=None, col_labels=None):
    """rows/cols: 行列数；每格名由 loader(i,j) 提供"""
    gap, margin = GAP, MARGIN
    ch = int(cw * 9 / 16)
    lab_h = 30 if row_labels else 0
    head = 104
    head += 30 if col_labels else 0
    foot = FOOT
    W = margin * 2 + cols * cw + (cols - 1) * gap
    H = head + rows * (ch + lab_h) + (rows - 1) * gap + foot
    cv = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(cv)

    d.text((margin, 26), title, font=font(34, True), fill=TITLE)
    d.text((margin, 68), subtitle, font=font(19), fill=SUB)

    y0 = head
    if col_labels:
        for j, lb in enumerate(col_labels):
            x = margin + j * (cw + gap)
            d.text((x + 6, y0 - 26), lb, font=font(21, True), fill=ACC)
        y0 += 30

    return cv, d, y0, cw, ch, gap, margin, lab_h


def paste_cell(cv, d, x, y, cw, ch, loader, rowlabel=None):
    im, ok = loader(cw)
    cv.paste(im, (x, y))
    if not ok:
        d.rectangle([x, y, x + cw, y + ch], outline=(90, 60, 60), width=2)
        d.text((x + 12, y + 12), "缺图", font=font(20), fill=(180, 110, 110))
    d.rectangle([x, y, x + cw, y + ch], outline=(52, 59, 68), width=1)
    if rowlabel:
        d.text((x + 8, y + ch + 5), rowlabel, font=font(19, True), fill=TITLE)


# ============ 图 1：六敌人改造后 ============
ENEMIES = ["墨徒", "墨偶", "墨魇", "墨山", "墨骨", "墨龙"]
rows, cols, cw = 3, 2, 752
t = "敌人「有色 + 红色水墨描边」—— 改造后"
s = ("同机位差分帧 · 右侧白衣者为主角（色系参照） · 材质参数：_ChromaKeep 0.45 / _BandBias 0.00 / "
     "_OutlineColor 朱砂 / _OutlineWidth 0.018")
cv, d, y0, cw, ch, gap, margin, lab_h = build_grid(
    rows, cols, cw, t, s, "", row_labels=[True] * rows)

for i, name in enumerate(ENEMIES):
    r, c = divmod(i, cols)
    x = margin + c * (cw + gap)
    y = y0 + r * (ch + lab_h + gap)
    paste_cell(cv, d, x, y, cw, ch,
               lambda w, n=name: load(f"EVV_{n}_新_1600x900.png", w),
               rowlabel=f"{name}（改造后）")

d.line([margin, cv.height - FOOT + 6, cv.width - margin, cv.height - FOOT + 6], fill=(48, 55, 64), width=1)
d.text((margin, cv.height - FOOT + 22),
       "墨龙未改（Boss 浓墨为有意设计，颜色走 glTF emissive）—— 是否也加红边待定",
       font=font(20), fill=ACC)

cv.save(os.path.join(BASE, "_验收_六敌人_有色+红墨边.png"))
print("图1 OK", cv.size)


# ============ 图 2：墨阶 × 红边色 定标 ============
BB = ["bb-0.15", "bb+0.00", "bb+0.10"]
BBL = ["_BandBias = -0.15（深墨，最接近旧观感）", "_BandBias = 0.00（当前落盘）", "_BandBias = +0.10（最亮）"]
RR = ["R1", "R2", "R3"]
RL = ["R1 暗朱砂 (0.35,0.03,0.02)", "R2 中朱砂 (0.48,0.035,0.022)", "R3 亮朱砂 (0.62,0.045,0.025)"]

rows, cols, cw = 4, 3, 620
t = "墨阶 × 红边色 二维定标（样本：墨徒、墨山）"
s = "_ChromaKeep 固定 0.45 · 运行时 clone 材质 · 请挑一行墨阶 + 一列红边色"
cv2, d2, y0, cw, ch, gap, margin, lab_h = build_grid(
    rows, cols, cw, t, s, "", row_labels=[True] * rows, col_labels=RL)

for i, bb in enumerate(BB):
    for j, rr in enumerate(RR):
        x = margin + j * (cw + gap)
        y = y0 + i * (ch + lab_h + gap)
        paste_cell(cv2, d2, x, y, cw, ch,
                   lambda w, b=bb, r2=rr: load(f"ET_墨徒_{b}_{r2}.png", w),
                   rowlabel=f"墨徒 · {BBL[i]}")

for j, r2 in enumerate(RR):
    x = margin + j * (cw + gap)
    y = y0 + 3 * (ch + lab_h + gap)
    paste_cell(cv2, d2, x, y, cw, ch,
               lambda w, r3=r2: load(f"ET_墨山_bb+0.00_{r3}.png", w) if r3 == "R2"
               else load(f"ET_墨山_bb+0.00_R2.png", w),
               rowlabel="墨山验证 · _BandBias = 0.00（该材质的红边三档未扫，同列显示以对齐网格）")

cv2.save(os.path.join(BASE, "_定标_墨阶×红边色.png"))
print("图2 OK", cv2.size)
