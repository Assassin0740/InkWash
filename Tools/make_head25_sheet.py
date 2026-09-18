# -*- coding: utf-8 -*-
# 第二十五轮对照图：三档（不锁 / 锁定基准 / 锁定+现静态值）× 行波相位三列。
import os
from PIL import Image, ImageDraw, ImageFont

DIR = r'D:/Unity Project/InkWash/Tools/screenshots/enemies/H25'
OUT = DIR + '/_头锁定基准_v2.png'

ROWS = [
    ([u'A  不锁（旧行为）', u'吻部仰角极差 49.74°', u'偏航极差 14.55°'],
     ['hl_free_ph000.png', 'hl_free_ph030.png', 'hl_free_ph060.png']),
    ([u'B  锁定基准  pitch=0', u'吻部仰角极差 1.02°', u'偏航极差 0.52°'],
     ['hl_locked_ph000.png', 'hl_locked_ph030.png', 'hl_locked_ph060.png']),
    ([u'C  锁定  pitch=-12（现值）', u'吻部仰角极差 1.11°', u'偏航极差 0.57°'],
     ['hl_locked12_ph000.png', None, None]),
]
COLS = [u'行波相位 0.00', u'行波相位 0.30', u'行波相位 0.60']

SCALE = 0.5
PAD = 10
HEAD = 86
LEFT = 320

font = fsmall = None
for fp in [r'C:/Windows/Fonts/msyh.ttc', r'C:/Windows/Fonts/simhei.ttf', r'C:/Windows/Fonts/simsun.ttc']:
    if os.path.exists(fp):
        try:
            font = ImageFont.truetype(fp, 22)
            fsmall = ImageFont.truetype(fp, 19)
            break
        except Exception:
            pass
if font is None:
    font = fsmall = ImageFont.load_default()

sample = Image.open(os.path.join(DIR, ROWS[0][1][0]))
tw, th = int(sample.width * SCALE), int(sample.height * SCALE)

W = LEFT + len(COLS) * (tw + PAD) + PAD
H = HEAD + len(ROWS) * (th + PAD) + PAD

canvas = Image.new('RGB', (W, H), (250, 250, 252))
d = ImageDraw.Draw(canvas)

d.text((PAD, 10), u'墨龙头部：不锁 vs 锁定基准（hoverStationary 冻结位置，三行波相位同机位同参数）',
       fill=(20, 20, 30), font=font)
d.text((PAD, 44), u'★ 判据看「同一行的三张图之间，头/颈角度变不变」，不是看单张',
       fill=(120, 60, 40), font=fsmall)

for c, ct in enumerate(COLS):
    x = LEFT + c * (tw + PAD)
    d.text((x + 4, 58), ct, fill=(70, 70, 95), font=fsmall)

for r, (lines, files) in enumerate(ROWS):
    y = HEAD + r * (th + PAD)
    for ln, seg in enumerate(lines):
        col = (20, 20, 30) if ln == 0 else (90, 90, 110)
        d.text((6, y + 6 + ln * 28), seg, fill=col, font=font if ln == 0 else fsmall)
    for c, fn in enumerate(files):
        x = LEFT + c * (tw + PAD)
        if fn is None:
            d.multiline_text((x + 10, y + th // 2 - 14), u'锁定后与相位无关\n一张即可', fill=(150, 150, 165), font=fsmall)
            continue
        p = os.path.join(DIR, fn)
        if not os.path.exists(p):
            d.text((x + 4, y + th // 2), u'缺图 ' + fn, fill=(200, 60, 60), font=font)
            continue
        im = Image.open(p).convert('RGB').resize((tw, th), Image.LANCZOS)
        canvas.paste(im, (x, y))

canvas.save(OUT)
print(u'已写出', OUT, canvas.size)
