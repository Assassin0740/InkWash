# -*- coding: utf-8 -*-
"""第二十六轮头部对照表（出货配置版）：三档 × 三视角 + 一排远景。

三档用**同一个物件、同一台相机、同一套材质**，唯一变量 = 姿态。
基准档 = 把骨骼就地写回 FBX 局部旋转（且跨帧等蒙皮算完才渲染）。
出货档 = prefab 实值（headLockUseRestPose=True / headAlignPitchDeg=0）。
"""
import os
from PIL import Image, ImageDraw, ImageFont

D = r'D:\Unity Project\InkWash\Tools\screenshots\enemies\H27'
OUT = os.path.join(D, '_头锁定换基_v3.png')
CW, CH = 440, 293
PAD, TOP, LEF = 10, 152, 330

ROWS = [
    (u'rest（FBX 原生基准）', (0, 120, 0),
     [u'头载体偏差 0.00°',
      u'= 模型师傅摆的那个姿态',
      u'（你说「对」的那个）']),
    (u'old（锁拉直后的 _baseRel）', (170, 20, 20),
     [u'偏差 138.97°  ★ 歪在这',
      u'= 第二十五轮的行为',
      u'（「三档全歪」的根源）']),
    (u'new（锁 FBX 基准）＝ 出货', (0, 110, 0),
     [u'偏差 0.00°  ✅',
      u'俯仰/偏航/滚转三列',
      u'与基准帧逐个相同，且三相位一致']),
]
FILES = [
    ['head_rest_side.png', 'head_rest_top.png', 'head_rest_front.png'],
    ['head_old_ph000_side.png', 'head_old_ph000_top.png', 'head_old_ph000_front.png'],
    ['head_new_ph000_side.png', 'head_new_ph000_top.png', 'head_new_ph000_front.png'],
]
COLS = [u'侧视', u'俯视', u'前视（沿身轴看）']
WIDES = [('wide_rest_side.png', u'rest 基准'), ('wide_old_ph000_side.png', u'old 旧锁'),
         ('wide_new_ph000_side.png', u'new 出货')]


def font(sz):
    for p in [r'C:\Windows\Fonts\msyh.ttc', r'C:\Windows\Fonts\simhei.ttf', r'C:\Windows\Fonts\simsun.ttc']:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


F_T, F_L, F_S, F_X = font(27), font(19), font(15), font(14)

ROW_H = CH + PAD + 74
y0 = TOP
y_wide = y0 + 3 * ROW_H + 44
w = LEF + 3 * (CW + PAD) + PAD
h = y_wide + 46 + CH * 3 // 4 + 20
img = Image.new('RGB', (w, h), (250, 250, 248))
d = ImageDraw.Draw(img)

d.text((PAD, 12), u'墨龙头部：「锁拉直后基准」 vs 「锁 FBX 原生基准」（第二十六轮）', font=F_T, fill=(15, 15, 15))
d.text((PAD, 50), u'同一物件 / 同一相机 / 同一材质，唯一变量 = 姿态。判据 = 头载体 drgon_025（头簇挂点）相对 FBX 基准被拧了多少度。',
       font=F_S, fill=(70, 70, 70))
d.text((PAD, 72), u'★ 头簇（39 叶 + 鬃/须/角/颌）是刚性分支、从不被驱动 ⇒ 挂点被拧多少，头就歪多少（自检 0.0000° 证实）。',
       font=F_S, fill=(70, 70, 70))
d.text((PAD, 94), u'★ 根因：StraightenSpineBase() 把颈根 drgon_025 也转了 93.37°、段方向偏 66.56°，而头簇只能跟着走；',
       font=F_S, fill=(150, 40, 40))
d.text((PAD, 114), u'   旧锁锁的正是「拉直之后」的基准 ⇒ 三档全锁在同一个被拧歪 139° 的姿态上。',
       font=F_S, fill=(150, 40, 40))

for c, nm in enumerate(COLS):
    tw = d.textlength(nm, font=F_L)
    d.text((LEF + c * (CW + PAD) + CW / 2 - tw / 2, TOP - 28), nm, font=F_L, fill=(40, 40, 40))

for r in range(3):
    y = y0 + r * ROW_H
    label, col, sub = ROWS[r]
    d.text((PAD, y + 8), label, font=F_L, fill=col)
    for i, line in enumerate(sub):
        d.text((PAD + 12, y + 40 + i * 22), line, font=F_X, fill=(60, 110, 60) if r in (0, 2) else (100, 100, 100))
    for c, fn in enumerate(FILES[r]):
        p = os.path.join(D, fn)
        x = LEF + c * (CW + PAD)
        if not os.path.exists(p):
            d.rectangle([x, y, x + CW, y + CH], outline=(200, 80, 80))
            d.text((x + 8, y + 8), u'缺 ' + fn, font=F_S, fill=(200, 80, 80))
            continue
        img.paste(Image.open(p).convert('RGB').resize((CW, CH), Image.LANCZOS), (x, y))
        d.rectangle([x, y, x + CW, y + CH], outline=(175, 175, 175))

d.text((PAD, y_wide - 30), u'远景（侧视，整条身体）：', font=F_L, fill=(15, 15, 15))
d.text((PAD + 240, y_wide - 28), u'基准档身体是 C 形弯的、其余两档是直的 —— 那是拉直的效果（预期）。',
       font=F_X, fill=(100, 100, 100))
d.text((PAD, y_wide - 10), u'要看的是**头的朝向相对身体**，不是身体形状。', font=F_X, fill=(100, 100, 100))
WW = (w - LEF - 4 * PAD) // 3
WH = WW * 2 // 3
for i, (fn, lab) in enumerate(WIDES):
    p = os.path.join(D, fn)
    x = LEF + i * (WW + PAD)
    if os.path.exists(p):
        img.paste(Image.open(p).convert('RGB').resize((WW, WH), Image.LANCZOS), (x, y_wide + 14))
        d.rectangle([x, y_wide + 14, x + WW, y_wide + 14 + WH], outline=(175, 175, 175))
        d.text((x + 7, y_wide + 20), lab, font=F_L, fill=(190, 30, 30))

img.save(OUT)
print(u'OK -> ' + OUT, img.size)
