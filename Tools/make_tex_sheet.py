# -*- coding: utf-8 -*-
"""把四个敌人（5 个材质实例）的 _BaseMap 贴图拼成对照图：
   左：贴图缩略图；右：该贴图的平均色 / 主色族色块。
   目的：让"它自己的颜色是什么"变成一张能看的图。
"""
import os, colorsys
from collections import defaultdict
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TARGETS = [
    ("墨山",      "Ziyuan 山怪 Orge",   "Assets/ThirdParty/Ziyuan/mountain_orge/textures/Orge_LP_baseColor.png"),
    ("墨怪",      "Ziyuan 兽人 Orc",    "Assets/ThirdParty/Ziyuan/low-poly_orc/textures/07_-_Default_diffuse.png"),
    ("墨骨",      "Ziyuan 不死兵",      "Assets/ThirdParty/Ziyuan/cursed_undead_soldier_rig/textures/Undead_Material_baseColor.png"),
    ("墨偶/徒/魇", "KayKit 骷髅（共用）", "Assets/ThirdParty/KayKit/Skeletons/Characters/skeleton_texture_A.png"),
]

THUMB = 300
PAD = 22
HEAD = 132
ROW_GAP = 18
FOOT = 96
W = PAD * 2 + THUMB + 30 + 330


def font(sz, bold=False):
    for p in ("C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc",
              "C:/Windows/Fonts/simhei.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


def hue_name(a, b):
    for lo, hi, nm in ((0, 15, "红"), (15, 45, "橙"), (45, 70, "黄"), (70, 160, "绿"),
                       (160, 200, "青"), (200, 255, "蓝"), (255, 290, "紫"),
                       (290, 330, "品红"), (330, 360, "红")):
        if a < hi and b > lo:
            return nm
    return "?"


def families(im, topn=3):
    px = im.convert("RGBA").load()
    w, h = im.size
    step = max(1, int((w * h) ** 0.5 / 200))
    n = 0
    sr = sg = sb = 0.0
    bins = defaultdict(lambda: [0, 0.0, 0.0, 0.0])
    for y in range(0, h, step):
        for x in range(0, w, step):
            r, g, b, a = px[x, y]
            if a < 16:
                continue
            rf, gf, bf = r / 255.0, g / 255.0, b / 255.0
            if 0.2126 * rf + 0.7152 * gf + 0.0722 * bf < 0.02:
                continue
            n += 1
            sr += r; sg += g; sb += b
            bkt = int(colorsys.rgb_to_hsv(rf, gf, bf)[0] * 12) % 12
            e = bins[bkt]; e[0] += 1; e[1] += r; e[2] += g; e[3] += b
    top = sorted(bins.items(), key=lambda kv: -kv[1][0])[:topn]
    out = []
    for bkt, e in top:
        c = e[0]
        out.append((e[0] / max(n, 1), (e[1] / c, e[2] / c, e[3] / c), (bkt * 30, (bkt + 1) * 30)))
    return (sr / max(n, 1), sg / max(n, 1), sb / max(n, 1)), out


def main():
    rows = []
    for label, note, rel in TARGETS:
        p = os.path.join(ROOT, rel)
        im = Image.open(p)
        thumb = im.convert("RGBA").copy()
        thumb.thumbnail((THUMB, THUMB), Image.LANCZOS)
        avg, fams = families(im)
        rows.append((label, note, thumb, avg, fams))

    row_h = max(THUMB, 130)
    H = HEAD + len(rows) * (row_h + ROW_GAP) + FOOT
    cv = Image.new("RGB", (W, H), (28, 30, 34))
    d = ImageDraw.Draw(cv)

    d.text((PAD, 34), "敌人 _BaseMap 贴图本色对照", font=font(30, True), fill=(238, 240, 244))
    d.text((PAD, 76), "左：各敌人材质正在用的贴图缩略图   右：该贴图自身的平均色与主要色相族",
           font=font(16), fill=(150, 158, 170))
    d.text((PAD, 100), "—— 「它自己的颜色」就是这些；当前六个材质的墨阶表把它们统一压成了同一族暖赭",
           font=font(15), fill=(196, 122, 96))

    y = HEAD
    for label, note, thumb, avg, fams in rows:
        d.rectangle([PAD - 6, y - 6, W - PAD + 6, y + row_h + 6], fill=(36, 39, 44))
        # 缩略图（居中到 THUMB 框）
        tx = PAD + (THUMB - thumb.width) // 2
        ty = y + (row_h - thumb.height) // 2
        bg = Image.new("RGB", (THUMB, row_h), (48, 51, 57))
        bg.paste(thumb, ((THUMB - thumb.width) // 2, (row_h - thumb.height) // 2), thumb)
        cv.paste(bg, (PAD, y))

        cx = PAD + THUMB + 30
        d.text((cx, y + 4), label, font=font(25, True), fill=(240, 243, 247))
        d.text((cx + 4, y + 40), note, font=font(14), fill=(140, 148, 160))

        # 平均色大块
        bx, by = cx + 4, y + 68
        d.rectangle([bx, by, bx + 118, by + 54], fill=(int(avg[0]), int(avg[1]), int(avg[2])))
        d.rectangle([bx, by, bx + 118, by + 54], outline=(96, 102, 112))
        d.text((bx + 126, by + 4), "平均色", font=font(14, True), fill=(206, 212, 222))
        d.text((bx + 126, by + 26), f"sRGB({int(avg[0])},{int(avg[1])},{int(avg[2])})",
               font=font(14), fill=(160, 168, 180))

        # 主色族条
        sy = by + 62
        d.text((bx, sy - 2), "主要色相族（按像素占比）", font=font(13), fill=(140, 148, 160))
        sy += 20
        for frac, c, (a, b) in fams:
            d.rectangle([bx, sy, bx + 46, sy + 26], fill=(int(c[0]), int(c[1]), int(c[2])))
            d.rectangle([bx, sy, bx + 46, sy + 26], outline=(90, 96, 106))
            d.text((bx + 54, sy + 4), f"{hue_name(a, b)} {a}-{b}°   {frac*100:.0f}%",
                   font=font(14), fill=(206, 212, 222))
            sy += 30

        y += row_h + ROW_GAP

    d.line([PAD, H - FOOT + 8, W - PAD, H - FOOT + 8], fill=(60, 66, 74), width=1)
    d.text((PAD, H - FOOT + 22),
           "落盘方案：按各自贴图本色重写墨阶表 _InkDark/_InkMid/_InkLight（保持明度档位不变，只换色相）",
           font=font(15), fill=(150, 158, 170))
    d.text((PAD, H - FOOT + 46),
           "KayKit 三个共用同一张贴图 ⇒ 忠于原样时三者同色；如需区分需人为指定，待定",
           font=font(15), fill=(150, 158, 170))

    out = os.path.join(ROOT, "Tools/screenshots/enemies/_敌人贴图本色对照.png")
    cv.save(out)
    print("已生成:", out, cv.size)


if __name__ == "__main__":
    main()
