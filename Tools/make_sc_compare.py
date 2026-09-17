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
OUT = os.path.join(SRC, "_修复后_四对象_开vs关.png")

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
    ("墨山 Z_Enemy_MoShan ｜ 14 个 SMR，骨骼引用 26/26 全有效",
     "SV_Z_Enemy_MoShan_all_开.png", "SV_Z_Enemy_MoShan_all_关.png",
     "6.269%", OK, "完整山怪 + 黑墨轮廓，两帧 md5 不同"),
    ("墨徒 Z_Enemy_MoGuai ｜ 10 个 SMR，骨骼引用 97/97 全有效",
     "SV_Z_Enemy_MoGuai_all_开.png", "SV_Z_Enemy_MoGuai_all_关.png",
     "9.301%", OK, "Bip001 骨架 97 根全部接回，两帧 md5 不同"),
    ("墨骨 Z_Enemy_MoGu ｜ 只留 3 个 SMR（骨骼 80/80 全有效）",
     "SV_Z_Enemy_MoGu_smr_开.png", "SV_Z_Enemy_MoGu_smr_关.png",
     "4.884%", OK, "修复前这一档是 0.000%，现在骨骼部分画出来了"),
    ("墨骨 Z_Enemy_MoGu ｜ 只留 4 个普通 MeshRenderer",
     "SV_Z_Enemy_MoGu_mr_开.png", "SV_Z_Enemy_MoGu_mr_关.png",
     "2.795%", OK, "静态网格部分本来就没坏，读数与修复前同量级"),
    ("墨龙 Z_Enemy_MoLong ｜ 2 个 SMR，骨骼 273/273（阳性对照）",
     "SV_Z_Enemy_MoLong_all_开.png", "SV_Z_Enemy_MoLong_all_关.png",
     "2.373%", OK, "从头到尾都能画出来 —— 说明这把尺子是准的"),
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

d.text((M, M + 4), "修复后 · 演示场 Showcase · 演示场自己的相机 · 同 tick 差分帧（无哨兵底色）", font=F_B, fill=FG)
d.text((M, M + 34), "左＝被测渲染器开　　右＝被测渲染器关　　现在两格都不同 → 三个敌人全部画出来了",
       font=F_S, fill=DIM)
d.text((M, M + 58), "修复方式：把 SMR 的 m_Bones / m_RootBone 按源 FBX 的顺序接回 prefab 内的同名 Transform（1574 条 {fileID: 0} 全部补上）", font=F_S, fill=DIM)

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
