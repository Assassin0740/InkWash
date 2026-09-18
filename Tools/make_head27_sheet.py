# -*- coding: utf-8 -*-
"""第二十七轮「头随颈」对照表：两个档位 × 6 个时刻 × (俯视 / 前视)。

两行**同一机位、同一冻结姿态**，唯一变量 = 头（末尾 headAlignLinks 节）的写法：
  A 绝对锁  = _spine[i].rotation = rootRot · headExtra · _restRel[i]     （第二十六轮出货）
  B 随颈    = _spine[i].rotation = _spine[i-1].rotation · headExtra · FBX原生局部角  （本轮出货）
每格角标 = 实测「头 − 颈」夹角（容器系）。A 会随脖子摆动而漂，B 应当**恒定**。
"""
import os
from PIL import Image, ImageDraw, ImageFont

D = r'D:\Unity Project\InkWash\Tools\screenshots\enemies\H28'

# 实测（reports/drg_headfollow27.txt）
DIFF_A = [4.1, -8.0, -18.8, -25.7, -27.9, -25.3]
DIFF_B = [-6.6, -6.6, -6.6, -6.6, -6.6, -6.6]
SPAN_A = max(DIFF_A) - min(DIFF_A)
SPAN_B = max(DIFF_B) - min(DIFF_B)

ROWS = [
    (u'A  绝对锁（上一轮出货）', (170, 20, 20),
     [u'头−颈 夹角 %.1f° … %.1f°（跨度 %.1f°）' % (min(DIFF_A), max(DIFF_A), SPAN_A),
      u'= 头被钉在容器基准上',
      u'「脖子动、头不动」就是它']),
    (u'B  随颈（本轮出货）★', (0, 110, 0),
     [u'头−颈 夹角恒 −6.6°（跨度 %.1f°）' % SPAN_B,
      u'= 与父节保持 FBX 原生局部角',
      u'头/颈 p2p 比 1.010、相对角误差 0.00°']),
]
PREFIX = ['head_w0_s', 'head_w1_s']

CW, CH = 380, 253
PAD, TOP, LEF = 8, 158, 300


def font(sz):
    for p in [r'C:\Windows\Fonts\msyh.ttc', r'C:\Windows\Fonts\simhei.ttf', r'C:\Windows\Fonts\simsun.ttc']:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


F_T, F_L, F_S, F_X, F_N = font(27), font(19), font(15), font(14), font(21)
ROW_H = CH + PAD + 74
W = LEF + 6 * (CW + PAD) + PAD


def build(view, out_name, title, notes):
    y0 = TOP
    y_wide = y0 + 2 * ROW_H + 46
    WW = (W - LEF - 3 * PAD) // 2
    WH = WW * 2 // 3
    H = y_wide + 42 + WH + 18
    img = Image.new('RGB', (W, H), (250, 250, 248))
    d = ImageDraw.Draw(img)

    d.text((PAD, 12), title, font=F_T, fill=(15, 15, 15))
    for i, (t, col) in enumerate(notes):
        d.text((PAD, 50 + i * 21), t, font=F_S, fill=col)

    for c in range(6):
        nm = u'时刻 %d' % (c + 1)
        tw = d.textlength(nm, font=F_L)
        d.text((LEF + c * (CW + PAD) + CW / 2 - tw / 2, TOP - 28), nm, font=F_L, fill=(40, 40, 40))

    for r in range(2):
        y = y0 + r * ROW_H
        label, col, sub = ROWS[r]
        diffs = DIFF_A if r == 0 else DIFF_B
        d.text((PAD, y + 8), label, font=F_L, fill=col)
        for i, line in enumerate(sub):
            d.text((PAD + 12, y + 40 + i * 22), line, font=F_X, fill=col)
        for c in range(6):
            fn = PREFIX[r] + str(c) + '_' + view + '.png'
            p = os.path.join(D, fn)
            x = LEF + c * (CW + PAD)
            if not os.path.exists(p):
                d.rectangle([x, y, x + CW, y + CH], outline=(200, 80, 80))
                d.text((x + 8, y + 8), u'缺 ' + fn, font=F_S, fill=(200, 80, 80))
                continue
            img.paste(Image.open(p).convert('RGB').resize((CW, CH), Image.LANCZOS), (x, y))
            d.rectangle([x, y, x + CW, y + CH], outline=(175, 175, 175))
            lab = u'头−颈 %+.1f°' % diffs[c]
            d.rectangle([x + 1, y + 1, x + 128, y + 30], fill=(255, 255, 255))
            d.text((x + 7, y + 5), lab, font=F_N, fill=col)

    d.text((PAD, y_wide - 26), u'远景（整条身体，侧视）：', font=F_L, fill=(15, 15, 15))
    for i, fn in enumerate(['wide_w0.png', 'wide_w1.png']):
        p = os.path.join(D, fn)
        x = LEF + i * (WW + PAD)
        if os.path.exists(p):
            img.paste(Image.open(p).convert('RGB').resize((WW, WH), Image.LANCZOS), (x, y_wide + 14))
            d.rectangle([x, y_wide + 14, x + WW, y_wide + 14 + WH], outline=(175, 175, 175))
            d.text((x + 7, y_wide + 20), ROWS[i][0], font=F_L, fill=ROWS[i][1])

    out = os.path.join(D, out_name)
    img.save(out)
    print(u'OK -> %s  %s' % (out, img.size))


build('top', '_头随颈_v1_俯视.png',
      u'墨龙头部：「绝对锁」 vs 「随颈」（第二十七轮）',
      [(u'同一物件 / 同一相机 / 同一**冻结姿态**，唯一变量 = 末尾 %d 节（头）的写法。' % 2, (70, 70, 70)),
       (u'A = rootRot·headExtra·基准   B = 父节世界旋转·headExtra·FBX原生局部角（两者只差参考系）', (70, 70, 70)),
       (u'★ 自检：出图前先比「探针手算」与「组件刚写下的值」= 0.0000°（6/6）⇒ 重写忠实，对照成立。', (60, 110, 60)),
       (u'★ 用户原话：「脖子动的时候，头一直保持一个方向没有旋转，要面向过来」。', (150, 40, 40))])

build('front', '_头随颈_v1_前视.png',
      u'同一数据 · 前视（沿身轴看头）：偏航最直观',
      [(u'前视 = 相机在身体前方沿身轴回看头，脖子左右摆时「吻部摆没摆」一目了然。', (70, 70, 70)),
       (u'★ A 行：吻部相对脖子**纹丝不动**（头−颈 夹角被脖子甩着变）。', (170, 20, 20)),
       (u'★ B 行：吻部跟着脖子一起摆，且与脖子的相对角**恒定** = FBX 原生值。', (60, 110, 60)),
       (u'度量细节见 Tools/reports/drg_headfollow27.txt。', (70, 70, 70))])
