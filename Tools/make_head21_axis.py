# -*- coding: utf-8 -*-
"""
make_head21_axis.py —— 给 drg_head21 的六档头特写叠上「世界水平参考线 + 吻部轴」，拼成对照图

与 head20 版的两处不同：
  · 文件名是 `h21_side_p30.png` 这种**绝对档位**（不是 rel），报告里 SCREEN 行也是 `p=p30`
  · 标题与标签直接写**绝对 headAlignPitchDeg**，用户指着哪张，我就把那个数填进 prefab/C#

用法：python Tools/make_head21_axis.py
产物：Tools/screenshots/enemies/H21/h21_anno_side_*.png（单张）
      Tools/screenshots/enemies/_墨龙_头部档位_绝对值_v2.png（对照图）
"""
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\Unity Project\InkWash"
SRC = os.path.join(ROOT, "Tools", "screenshots", "enemies", "H21")
RP = os.path.join(ROOT, "Tools", "reports", "drg_head21.txt")
SHEET = os.path.join(ROOT, "Tools", "screenshots", "enemies", "_墨龙_头部档位_绝对值_v2.png")

W, H = 900, 600


def font(size, bold=False):
    cands = ([r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttc"] if bold
             else [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc"])
    for p in cands + [r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def parse():
    """返回 {tag: dict}, {tag: {点: (x,y)}} —— tag 形如 p30 / m23。"""
    txt = open(RP, encoding="utf-8", errors="replace").read()
    tbl, scr = {}, {}
    started = False
    for ln in txt.splitlines():
        s = ln.strip()
        if s.startswith("SCREEN "):
            tok = s.split()
            tag = tok[1].split("=")[1]
            d, i = {}, 2
            while i + 2 < len(tok) + 1 and i + 2 <= len(tok):
                k = tok[i]
                try:
                    d[k] = (float(tok[i + 1]) * W, float(tok[i + 2]) * H)
                except (ValueError, IndexError):
                    break
                i += 3
            scr[tag] = d
            continue
        if s.startswith("── Pass B"):
            started = True
            continue
        if not started:
            continue
        m = re.match(r"^(-?[\d.]+)\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+([\d.]+)\s+m$", s)
        if m:
            p = float(m.group(1))
            tag = ("p" if p >= 0 else "m") + "%02d" % abs(int(round(p)))
            tbl[tag] = dict(pitch=p, elev=float(m.group(2)), yaw=float(m.group(3)),
                            skull=float(m.group(4)), fwdspan=float(m.group(5)))
    return tbl, scr


def dashed(d, p0, p1, fill, width=3, dash=16, gap=11):
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    L = (dx * dx + dy * dy) ** 0.5
    if L < 1e-3:
        return
    ux, uy = dx / L, dy / L
    t = 0.0
    while t < L:
        e = min(t + dash, L)
        d.line([(x0 + ux * t, y0 + uy * t), (x0 + ux * e, y0 + uy * e)], fill=fill, width=width)
        t = e + gap


def dot(d, p, r, fill, outline=(20, 20, 20)):
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill, outline=outline, width=2)


def annotate(path, tag, t, sc):
    im = Image.open(path).convert("RGB")
    d = ImageDraw.Draw(im)
    f, fb = font(19), font(22)

    if "refA" in sc and "refB" in sc:
        ax, ay = sc["refA"]
        bx, by = sc["refB"]
        vx, vy = bx - ax, by - ay
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            vx, vy = vx / n, vy / n
            d.line([(ax - vx * 3000, ay - vy * 3000), (bx + vx * 3000, by + vy * 3000)],
                   fill=(120, 120, 90), width=1)
            dashed(d, (ax, ay), (ax + vx * 1400, ay + vy * 1400), (255, 236, 140))
            dashed(d, (ax, ay), (ax - vx * 1400, ay - vy * 1400), (255, 236, 140))

    if "rear" in sc and "tip" in sc:
        rx, ry = sc["rear"]
        tx, ty = sc["tip"]
        vx, vy = tx - rx, ty - ry
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            d.line([(rx, ry), (tx + vx / n * 90, ty + vy / n * 90)], fill=(40, 220, 235), width=4)

    if "neck" in sc:
        dot(d, sc["neck"], 5, (150, 150, 160))
    if "rear" in sc:
        dot(d, sc["rear"], 6, (80, 140, 255))
    if "tip" in sc:
        dot(d, sc["tip"], 7, (240, 60, 60))
    if "top" in sc:
        dot(d, sc["top"], 5, (90, 220, 120))

    box = [(8, 8), (372, 122)]
    d.rectangle(box, fill=(18, 20, 24), outline=(90, 96, 110))
    d.text((16, 14), "headAlignPitchDeg = %.0f" % t["pitch"], font=fb, fill=(255, 240, 200))
    d.text((16, 44), "吻部仰角 %.2f°   偏航 %.2f°" % (t["elev"], t["yaw"]), font=f, fill=(120, 240, 250))
    d.text((16, 68), "颅顶仰角 %.2f°" % t["skull"], font=f, fill=(150, 240, 170))
    d.text((16, 92), "前向跨度 %.4f m" % t["fwdspan"], font=f, fill=(210, 214, 224))

    out = os.path.join(SRC, "h21_anno_side_%s.png" % tag)
    im.save(out)
    return im


def main():
    tbl, scr = parse()
    if not tbl:
        print("× 报告里没解析到 Pass B 表格：", RP)
        return 1
    keys = [k for k in ["p30", "p05", "m05", "m15", "m23", "m30"] if k in tbl]
    print("解析到 %d 档：%s" % (len(tbl), ",".join(sorted(tbl))))

    tiles = {}
    for tag in keys:
        if tag not in scr:
            print("  缺 SCREEN %s" % tag)
            continue
        fp = os.path.join(SRC, "h21_side_%s.png" % tag)
        if not os.path.exists(fp):
            print("  缺图 %s" % fp)
            continue
        tiles[tag] = annotate(fp, tag, tbl[tag], scr[tag])
        print("  标注 %s" % tag)
    if not tiles:
        return 1

    SCALE = 0.52
    PAD, LBL_H = 8, 38
    tw, th = int(W * SCALE), int(H * SCALE)
    cw = PAD + len(tiles) * (tw + PAD)
    ch = PAD + 66 + th + LBL_H + PAD
    cv = Image.new("RGB", (cw, ch), (238, 240, 244))
    d = ImageDraw.Draw(cv)
    d.rectangle([0, 0, cw, 44], fill=(24, 26, 30))
    d.text((10, 9), "墨龙头部档位（绝对值）—— 同一行波相位 0.50 / 同机位 / 蒙皮网格实测（顶点索引已锁死）",
           font=font(22, True), fill=(255, 235, 200))
    d.text((10, 48), "黄虚线 = 真正的世界水平（三维投影，不是图上画的横线）｜青线 = 吻部轴（颈根后→吻端）",
           font=font(18), fill=(70, 76, 88))

    y = 72
    for k, (tag, im) in enumerate(tiles.items()):
        x = PAD + k * (tw + PAD)
        cv.paste(im.resize((tw, th), Image.LANCZOS), (x, y))
        d.rectangle([x - 1, y - 1, x + tw, y + th], outline=(160, 166, 176))
        t = tbl[tag]
        col = (22, 122, 60) if abs(t["elev"]) < 3 else ((180, 60, 40) if t["elev"] > 20 else (40, 44, 52))
        d.text((x + 4, y + th + 4), "pitch=%+.0f°   吻部仰角 %+.1f°" % (t["pitch"], t["elev"]),
               font=font(20, True), fill=col)
    cv.save(SHEET)
    print("完成：%s（%dx%d）" % (SHEET, cw, ch))
    return 0


if __name__ == "__main__":
    sys.exit(main())
