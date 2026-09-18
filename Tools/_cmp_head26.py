# -*- coding: utf-8 -*-
"""基准帧 vs 出货帧的逐像素对照：左=rest，中=new，右=50/50 叠加。

若「头载体偏差 0.00°」这条推理成立，头簇应完全重合（叠加后头是实心清晰的黑），
只有**身体**因为「拉直 / C 形弯」而错开成鬼影。
"""
import os
from PIL import Image, ImageChops

D = r'D:\Unity Project\InkWash\Tools\screenshots\enemies\H27'


def load(n):
    return Image.open(os.path.join(D, n)).convert('RGB')


for tag in ['side', 'top', 'front']:
    a = load('head_rest_%s.png' % tag)
    b = load('head_new_ph000_%s.png' % tag)
    w, h = a.size
    blend = Image.blend(a, b, 0.5)
    diff = ImageChops.difference(a, b)
    # 差异放大 2 倍便于看
    diff = diff.point(lambda v: min(255, v * 2))
    sheet = Image.new('RGB', (w * 4 + 30, h), (255, 255, 255))
    for i, im in enumerate([a, b, blend, diff]):
        sheet.paste(im, (i * (w + 10), 0))
    out = os.path.join(D, '_对照_%s.png' % tag)
    sheet.save(out)
    # 统计头簇区域（画面中心 1/3）的差异
    cx0, cy0, cx1, cy1 = w // 3, h // 3, w * 2 // 3, h * 2 // 3
    dr = diff.crop((cx0, cy0, cx1, cy1))
    px = list(dr.getdata())
    hot = sum(1 for p in px if (p[0] + p[1] + p[2]) > 90)
    print('%-6s -> %s  中心区差异像素 %d / %d (%.1f%%)' % (tag, os.path.basename(out), hot, len(px), 100.0 * hot / len(px)))
