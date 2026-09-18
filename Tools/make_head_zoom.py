# -*- coding: utf-8 -*-
"""把 H21 六档侧视图的**头部区域**裁出来放大，做成"肉眼能判抬头"的对照图。

为什么需要：900x600 的全景里龙头只占 ~150 px 宽、还是深墨色剪影 + 浅灰天，
在缩略图上根本分不出"平视"和"抬 15 度"。三轮尺子翻车的教训就是——
数字给候选区间，**裁决在眼睛**；那就先把眼睛能用的图做出来。

输入：Tools/screenshots/enemies/H21/h21_anno_side_*.png （已叠好黄虚线=真水平、
      青线=吻部轴；标签在左上角，裁的时候要保住）
输出：Tools/screenshots/enemies/_墨龙_头部_放大对照_v3.png
"""
import os
import re

from PIL import Image, ImageDraw

SRC = "Tools/screenshots/enemies/H21"
OUT = "Tools/screenshots/enemies/_墨龙_头部_放大对照_v3.png"

# 裁切窗口（900x600 原图坐标）—— 由暗像素分布 + 肉眼确认，龙头在 x 330~560
BOX = (250, 150, 640, 440)
SC = 2.4          # 放大倍数
LBL = 34
PAD = 12

# 档位 -> 报告里量到的吻部仰角（来自 Tools/reports/drg_head21.txt Pass B）
ANGLE = {
    "p30": "+25.8",
    "p05": "+1.8",
    "m05": "-7.8",
    "m15": "-17.5",
    "m23": "-25.3",
    "m30": "-32.1",
}
ORDER = ["p30", "p05", "m05", "m15", "m23", "m30"]


def main():
    tiles = []
    for tag in ORDER:
        f = os.path.join(SRC, "h21_anno_side_%s.png" % tag)
        if not os.path.exists(f):
            print("缺图", f)
            return
        im = Image.open(f).convert("RGB").crop(BOX)
        im = im.resize((int(im.width * SC), int(im.height * SC)), Image.LANCZOS)
        tiles.append((tag, im))

    tw, th = tiles[0][1].size
    cols, rows = 3, 2
    W = PAD + cols * (tw + PAD)
    H = PAD + 74 + rows * (th + LBL + PAD)

    sheet = Image.new("RGB", (W, H), (247, 247, 249))
    d = ImageDraw.Draw(sheet)

    # 中文用系统字体，避免 PIL 默认字体不支持
    def font(sz):
        for p in (r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttc",
                  r"C:\Windows\Fonts\simhei.ttf"):
            if os.path.exists(p):
                try:
                    return __import__("PIL.ImageFont", fromlist=["truetype"]).truetype(p, sz)
                except Exception:
                    pass
        return None

    F1, F2 = font(22), font(19)
    d.text((PAD, 14), "墨龙头部 · 侧视放大对照（黄虚线 = 真正的世界水平；青线 = 吻部轴）",
           fill=(20, 20, 24), font=F1)
    d.text((PAD, 44), "六档 headAlignPitchDeg 逐档出图，同一个行波相位 0.50 / 同机位 / 蒙皮网格实测",
           fill=(90, 90, 100), font=F2)

    for i, (tag, im) in enumerate(tiles):
        r, c = divmod(i, cols)
        x = PAD + c * (tw + PAD)
        y = PAD + 74 + r * (th + LBL + PAD)
        sheet.paste(im, (x, y))

        val = tag[1:]
        val = ("+" if tag[0] == "p" else "-") + val
        txt = "headAlignPitchDeg = %s     吻部仰角 %s°" % (val, ANGLE[tag])
        hot = tag in ("p30", "m30")
        d.text((x + 4, y + th + 6), txt, fill=(150, 20, 20) if hot else (20, 90, 40), font=F2)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.save(OUT)
    print("写出", OUT, sheet.size)


if __name__ == "__main__":
    main()
