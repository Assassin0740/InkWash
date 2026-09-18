# -*- coding: utf-8 -*-
"""
make_head19_sheet.py —— 把 drg_head19.cs 的六档头特写拼成一张对照图，给人眼选

布局：两行（侧视 / 前 3/4）× 六列（0° / 5° / 10° / 15° / 20° / 30°）
用法：python Tools/make_head19_sheet.py
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\Unity Project\InkWash"
SRC = os.path.join(ROOT, "Tools", "screenshots", "enemies", "H19")
OUT = os.path.join(ROOT, "Tools", "screenshots", "enemies", "_墨龙_头部档位对照_v1.png")

PITCH = [0, 5, 10, 15, 20, 30]
SCALE = 0.42
PAD = 8
LBL_H = 34
ROW_H = 36


def font(size):
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc",
              r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def main():
    tiles = {}
    for view in ("side", "q"):
        for p in PITCH:
            f = os.path.join(SRC, "h19_%s_p%02d.png" % (view, p))
            if not os.path.exists(f):
                print("缺图:", f)
                sys.exit(1)
            im = Image.open(f).convert("RGB")
            tiles[(view, p)] = im.resize(
                (int(im.width * SCALE), int(im.height * SCALE)), Image.LANCZOS)

    tw, th = tiles[("side", 0)].size
    f_lbl = font(26)
    f_row = font(28)

    canvas_w = ROW_H + len(PITCH) * (tw + PAD) + PAD
    canvas_h = PAD + 46 + 2 * (th + LBL_H + PAD)
    cv = Image.new("RGB", (canvas_w, canvas_h), (238, 240, 244))
    d = ImageDraw.Draw(cv)

    d.rectangle([0, 0, canvas_w, 44], fill=(24, 26, 30))
    d.text((10, 9), "墨龙头部俯仰档位对照 —— headAlignPitchDeg（同一行波相位 / 同一机位）",
           font=font(24), fill=(255, 235, 200))

    y = 46 + PAD
    for view, row_name in (("side", "侧视"), ("q", "前 3/4")):
        d.text((PAD, y + th // 2 - 16), row_name, font=f_row, fill=(40, 44, 52))
        for k, p in enumerate(PITCH):
            x = ROW_H + PAD + k * (tw + PAD)
            cv.paste(tiles[(view, p)], (x, y))
            d.rectangle([x - 1, y - 1, x + tw, y + th], outline=(160, 166, 176))
            txt = "headAlignPitchDeg = %d°" % p
            col = (200, 60, 60) if p == 30 else (40, 44, 52)
            d.text((x + 4, y + th + 5), txt, font=f_lbl, fill=col)
        y += th + LBL_H + PAD

    cv.save(OUT)
    print("完成：%s（%dx%d，每格 %dx%d）" % (OUT, canvas_w, canvas_h, tw, th))


if __name__ == "__main__":
    main()
