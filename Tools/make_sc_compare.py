# -*- coding: utf-8 -*-
"""演示场 · 正常相机 · 同 tick 差分帧 —— 敌人可见性总表。

每行两张：左 = 被测渲染器【开】，右 = 被测渲染器【关】。
两帧是同 tick 背靠背 Camera.Render()，中间不推进帧 ⇒ 相机与动画姿态完全一致。
画面没有任何哨兵底色，就是演示场 Showcase_Camera 的正常渲染结果。

读法：
  左右一样（md5 相同）→ 被测渲染器一个像素都没画。把"开"换成"关"，画面没有任何变化。
  左右不同            → 差异处就是它画出来的像素。
"""
import os
from PIL import Image, ImageDraw, ImageFont

SRC = r"D:/Unity Project/InkWash/Tools/screenshots/scene"
OUT = os.path.join(SRC, "_对比_演示场正常相机_四个对象_开vs关.png")

FONT = r"C:/Windows/Fonts/msyh.ttc"
F_T = ImageFont.truetype(FONT, 26)
F_S = ImageFont.truetype(FONT, 17)
F_B = ImageFont.truetype(FONT, 21)

BG = (30, 32, 36)
FG = (238, 238, 240)
DIM = (158, 162, 170)
OK = (86, 200, 120)
BAD = (232, 92, 92)

ROWS = [
    ("墨山 Z_Enemy_MoShan ｜ 14 个 SMR，骨骼引用 0/14 有效",
     "SV_Z_Enemy_MoShan_all_开.png", "SV_Z_Enemy_MoShan_all_关.png",
     "0.000%", BAD, "两帧 md5 完全相同 → 一个像素都没画"),
    ("墨徒 Z_Enemy_MoGuai ｜ 10 个 SMR，骨骼引用 0/10 有效",
     "SV_Z_Enemy_MoGuai_all_开.png", "SV_Z_Enemy_MoGuai_all_关.png",
     "0.000%", BAD, "只剩 1 个像素的噪点（921600 分之 1）"),
    ("墨骨 Z_Enemy_MoGu ｜ 只留 3 个 SMR（骨骼 0/3）",
     "SV_Z_Enemy_MoGu_smr_开.png", "SV_Z_Enemy_MoGu_smr_关.png",
     "0.000%", BAD, "两帧 md5 完全相同 → 骨骼部分也是零"),
    ("墨骨 Z_Enemy_MoGu ｜ 只留 4 个普通 MeshRenderer",
     "SV_Z_Enemy_MoGu_mr_开.png", "SV_Z_Enemy_MoGu_mr_关.png",
     "1.930%", OK, "它唯一画得出来的部分 = 这 4 块静态网格"),
    ("墨龙 Z_Enemy_MoLong ｜ 2 个 SMR，骨骼 273/273 有效（阳性对照）",
     "SV_Z_Enemy_MoLong_all_开.png", "SV_Z_Enemy_MoLong_all_关.png",
     "2.063%", OK, "同一套相机与代码，有骨骼的就能画出来"),
]

PW = 470
PH = int(PW * 720 / 1280)
M = 20
HEAD = 100
CAP = 62
W = M + PW + M + PW + M
H = M + HEAD + len(ROWS) * (PH + CAP) + M

canvas = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(canvas)

d.text((M, M + 4), "演示场 Showcase · 演示场自己的相机 · 同 tick 差分帧（无哨兵底色）", font=F_B, fill=FG)
d.text((M, M + 34), "左＝被测渲染器开　　右＝被测渲染器关　　两格一样 → 该渲染器在游戏里一个像素都没画",
       font=F_S, fill=DIM)
d.text((M, M + 58), "判据自带阳性对照：墨龙在同一套相机 / 同一套代码下读 2.063%", font=F_S, fill=DIM)

y = M + HEAD
for title, fa, fb, badge, col, note in ROWS:
    d.text((M, y), title, font=F_T, fill=FG)
    y += 30
    for k, fn in enumerate((fa, fb)):
        im = Image.open(os.path.join(SRC, fn)).convert("RGB").resize((PW, PH), Image.LANCZOS)
        x = M + k * (PW + M)
        canvas.paste(im, (x, y))
        d.rectangle([x, y, x + PW - 1, y + PH - 1], outline=(92, 96, 102), width=1)
        d.rectangle([x + 8, y + 8, x + 8 + 12, y + 8 + 12],
                    fill=(70, 210, 90) if k == 0 else (215, 70, 70))
        d.text((x + 26, y + 5), "开" if k == 0 else "关", font=F_S, fill=(255, 255, 255))

    cy = y + PH + 6
    d.rectangle([M + 6, cy + 4, M + 6 + 12, cy + 4 + 12], fill=col)
    d.text((M + 26, cy - 1), "纯几何像素 " + badge, font=F_B, fill=col)
    d.text((M + 26 + 190, cy + 3), note, font=F_S, fill=DIM)
    y += PH + CAP

canvas.save(OUT)
print("saved", OUT, canvas.size)
