# -*- coding: utf-8 -*-
"""演示场面板逐条目召唤（PlayForCapture）· 同 tick 差分帧 —— 6 个敌人验收图。

左格每行是演员渲染器【开】，同名 _B_关 是渲染器【关】。
两帧同 tick 背靠背 Camera.Render()，先把演员 shadowCastingMode 置 Off ⇒ 差集是纯几何像素。
这里只贴【开】帧，因为"看得见"这件事在前几轮的 _修复前_ 对比图里已经证过一次了。
"""
import os
from PIL import Image, ImageDraw, ImageFont

SRC = r"D:/Unity Project/InkWash/Tools/screenshots/scene"
OUT = os.path.join(SRC, "_验收_演示场面板逐条目召唤_六个敌人.png")

FONT = r"C:/Windows/Fonts/msyh.ttc"
F_T = ImageFont.truetype(FONT, 20)
F_S = ImageFont.truetype(FONT, 15)
F_B = ImageFont.truetype(FONT, 22)

BG = (30, 32, 36)
FG = (238, 238, 240)
DIM = (158, 162, 170)
OK = (86, 200, 120)

CELLS = [
    ("墨徒", "Z_Enemy_MoGuai", "SC_墨徒_A_开.png", "1.015%", "10 SMR · 97/97 骨骼"),
    ("墨偶", "Enemy_MoOu", "SC_墨偶_A_开.png", "0.860%", "8 SMR · 23/23 骨骼"),
    ("墨魇", "Enemy_MoYan", "SC_墨魇_A_开.png", "1.329%", "8 SMR · 23/23 骨骼"),
    ("墨山", "Z_Enemy_MoShan", "SC_墨山_A_开.png", "1.407%", "14 SMR · 26/26 骨骼"),
    ("墨骨", "Z_Enemy_MoGu", "SC_墨骨_A_开.png", "0.494%", "3 SMR · 80/80 骨骼"),
    ("墨龙", "Z_Enemy_MoLong", "SC_墨龙_A_开.png", "2.964%", "2 SMR · 273/273 骨骼（阳性对照）"),
]

COLS = 3
PW = 420
PH = int(PW * 720 / 1280)
M = 20
HEAD = 92
CAP = 50
ROWS = (len(CELLS) + COLS - 1) // COLS

W = M + COLS * PW + (COLS - 1) * M + M
H = M + HEAD + ROWS * (PH + CAP) + M

canvas = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(canvas)

d.text((M, M + 2), "测试场景 Showcase · 演示场面板逐条目召唤（PlayForCapture）", font=F_B, fill=FG)
d.text((M, M + 34), "演示场自己的相机 · 同 tick 差分帧 · 演员开/关两帧逐像素差 = 纯几何像素", font=F_S, fill=DIM)
d.text((M, M + 58), "六个敌人全部画出像素，且「骨骼引用全 null 的 SMR」均为 0", font=F_S, fill=DIM)

for i, (nick, pf, fn, pct, note) in enumerate(CELLS):
    r, c = divmod(i, COLS)
    x = M + c * (PW + M)
    y = M + HEAD + r * (PH + CAP)
    im = Image.open(os.path.join(SRC, fn)).convert("RGB").resize((PW, PH), Image.LANCZOS)
    canvas.paste(im, (x, y))
    d.rectangle([x, y, x + PW - 1, y + PH - 1], outline=(92, 96, 102), width=1)
    d.rectangle([x + 8, y + 8, x + 8 + 12, y + 8 + 12], fill=OK)
    d.text((x + 26, y + 5), "开", font=F_S, fill=(255, 255, 255))

    cy = y + PH + 5
    d.text((x, cy), nick + "　" + pf, font=F_T, fill=FG)
    d.text((x, cy + 25), "纯几何像素 " + pct + "　" + note, font=F_S, fill=OK)

canvas.save(OUT)
print("saved", OUT, canvas.size)
