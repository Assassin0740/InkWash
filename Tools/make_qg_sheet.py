# -*- coding: utf-8 -*-
"""把 QG_*.png 拼成一张总览图：整体三视角 + 逐件单渲"""
import os, glob
from PIL import Image, ImageDraw, ImageFont

SRC = "Tools/screenshots/enemies"
OUT = os.path.join(SRC, "_诊断_墨怪_逐件拆解.png")

def font(sz):
    for p in ("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/simhei.ttf", "C:/Windows/Fonts/simsun.ttc"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()

alls = ["QG_all_f.png", "QG_all_q.png", "QG_all_r.png"]
onlys = sorted(glob.glob(os.path.join(SRC, "QG_only_*.png")))

items = [(f, "整体·正") for f in alls[:1]]
items += [(f, "整体·3/4") for f in alls[1:2]]
items += [(f, "整体·侧") for f in alls[2:3]]
for f in onlys:
    nm = os.path.basename(f)[len("QG_only_"):-4]
    items.append((f, nm))

CW = 330
CH = 310
COLS = 5
GAP = 8
MARGIN = 14
LAB = 26
HEAD = 96 + 30

rows = (len(items) + COLS - 1) // COLS
W = MARGIN * 2 + COLS * CW + (COLS - 1) * GAP
H = HEAD + rows * (CH + LAB) + (rows - 1) * GAP + 90

cv = Image.new("RGB", (W, H), (242, 244, 247))
d = ImageDraw.Draw(cv)

f_title = font(26)
f_sub = font(16)
f_lab = font(15)

d.text((MARGIN, 20), "墨怪（Z_Enemy_MoGuai）逐件拆解 —— 定位「斧子 / 悬空棒子」", font=f_title, fill=(24, 28, 34))
d.text((MARGIN, 56), "演示场面板上的「墨徒」条目实际加载的是这个 prefab（ActionShowcase.cs:445）。"
                      "每格 = 只开一个渲染器，同一机位。", font=f_sub, fill=(90, 98, 108))

y = HEAD
for i, (path, lab) in enumerate(items):
    r, c = divmod(i, COLS)
    x = MARGIN + c * (CW + GAP)
    yy = y + r * (CH + LAB + GAP)
    if not os.path.exists(path):
        continue
    im = Image.open(path).convert("RGB")
    im.thumbnail((CW, CH), Image.LANCZOS)
    ox = x + (CW - im.width) // 2
    oy = yy + (CH - im.height) // 2
    cv.paste(im, (ox, oy))
    d.rectangle([x, yy, x + CW, yy + CH], outline=(206, 212, 220), width=1)
    d.text((x + 4, yy + CH + 5), lab, font=f_lab, fill=(40, 46, 54))

d.line([MARGIN, H - 74, W - MARGIN, H - 74], fill=(200, 206, 214), width=1)
d.text((MARGIN, H - 62), "结论：Object_25 = 双刃战斧（全长约 2.0 m）。Box001 实际只有 2.6 mm（导出垃圾，不可见）。",
       font=f_sub, fill=(30, 36, 44))
d.text((MARGIN, H - 40), "其余 Object_7/9/11/13/15/17/19/21/23 为身体各部件，全部 97 骨蒙皮、零退化。",
       font=f_sub, fill=(30, 36, 44))

cv.save(OUT)
print("已保存:", OUT, cv.size, "格数 =", len(items))
